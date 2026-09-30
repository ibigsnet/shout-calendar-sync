using System.Globalization;
using System.Text;
using System.Text.Json;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using ShoutCalendar.Core;

namespace ShoutCalendar.Sync;

public sealed class Plugin : IDalamudPlugin
{
    private readonly string storePath;
    private readonly CancellationTokenSource cancel = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly RelayTransport transport = new();
    private readonly byte[] proof = SyncProof.Sign(SyncGate.ChallengeBytes.Span);
    private readonly ICallGateSubscriber<byte[], string, bool> attach;
    private readonly ICallGateSubscriber<byte[], bool> detach;
    private readonly ICallGateSubscriber<byte[], string> pull;
    private readonly ICallGateSubscriber<byte[], int, string, string> ingest;
    private readonly ICallGateSubscriber<byte[], string> read;
    private readonly ICallGateSubscriber<byte[], string, bool> apply;
    private readonly ICallGateSubscriber<byte[], string, bool> status;
    private readonly ICallGateSubscriber<bool> openCalendar;
    private readonly ICallGateProvider<bool> requestNow;
    private readonly ICallGateProvider<bool> requestResync;
    private readonly Dictionary<string, int> sentRevision = new(StringComparer.Ordinal);
    private readonly Task loop;
    private DateTimeOffset? attachedAt;
    private string? attachedBook;
    private int syncNowRequested;
    private int syncResyncRequested;
    private bool missingNotice;
    private string? storedSnapshot;
    private SyncBook? passBook;
    private string activeRelay = "";
    private string failedPrimary = "";
    private DateTimeOffset primaryRetryAt;

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    public Plugin()
    {
        this.storePath = Path.Combine(PluginInterface.ConfigDirectory.FullName, "sync-store.json");
        this.attach = PluginInterface.GetIpcSubscriber<byte[], string, bool>("ShoutCalendar.Sync.Attach");
        this.detach = PluginInterface.GetIpcSubscriber<byte[], bool>("ShoutCalendar.Sync.Detach");
        this.pull = PluginInterface.GetIpcSubscriber<byte[], string>("ShoutCalendar.Sync.Pull");
        this.ingest = PluginInterface.GetIpcSubscriber<byte[], int, string, string>("ShoutCalendar.Sync.IngestV2");
        this.read = PluginInterface.GetIpcSubscriber<byte[], string>("ShoutCalendar.Sync.Read");
        this.apply = PluginInterface.GetIpcSubscriber<byte[], string, bool>("ShoutCalendar.Sync.Apply");
        this.status = PluginInterface.GetIpcSubscriber<byte[], string, bool>("ShoutCalendar.Sync.Status");
        this.openCalendar = PluginInterface.GetIpcSubscriber<bool>("ShoutCalendar.Open");
        this.requestNow = PluginInterface.GetIpcProvider<bool>("ShoutCalendar.Sync.RequestNow");
        this.requestNow.RegisterFunc(this.OnRequestNow);
        this.requestResync = PluginInterface.GetIpcProvider<bool>("ShoutCalendar.Sync.RequestResync");
        this.requestResync.RegisterFunc(this.OnRequestResync);
        PluginInterface.UiBuilder.OpenMainUi += this.OpenCalendar;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenCalendar;
        this.loop = Task.Run(() => this.Run(this.cancel.Token));
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenCalendar;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenCalendar;
        this.requestNow.UnregisterFunc();
        this.requestResync.UnregisterFunc();
        this.cancel.Cancel();
        try { this.loop.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        void SaveAndDetach()
        {
            try
            {
                var json = this.read.InvokeFunc(this.proof);
                if (!string.IsNullOrWhiteSpace(json)) AtomicFile.Write(this.storePath, Encoding.UTF8.GetBytes(json));
                this.detach.InvokeFunc(this.proof);
            }
            catch (IpcError) { }
            catch (Exception ex) { Log.Warning("Sync could not save its final state ({Failure}).", RelayDiagnostics.Failure(ex)); }
        }
        if (Framework.IsInFrameworkUpdateThread) SaveAndDetach();
        else _ = Framework.RunOnFrameworkThread(SaveAndDetach);
        this.transport.Dispose();
        this.wake.Dispose();
        this.cancel.Dispose();
    }

    private bool OnRequestNow() { Interlocked.Exchange(ref this.syncNowRequested, 1); this.Kick(); return true; }
    private bool OnRequestResync()
    {
        Interlocked.Exchange(ref this.syncResyncRequested, 1);
        return this.OnRequestNow();
    }
    private void Kick()
    {
        try { this.wake.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    private Task<T> OnFramework<T>(Func<T> work, CancellationToken token) => Framework.RunOnTick(() =>
    {
        token.ThrowIfCancellationRequested();
        return work();
    }, cancellationToken: token).WaitAsync(token);

    private async Task Run(CancellationToken token)
    {
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(10 + Random.Shared.NextDouble() * 2);
            var rateLimited = false;
            try { await this.Pass(token); failures = 0; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (IpcError)
            {
                if (!this.missingNotice)
                {
                    this.missingNotice = true;
                    Log.Warning("Sync requires the matching current Shout Calendar version; waiting for its IPC interface.");
                }
                delay = TimeSpan.FromSeconds(20);
            }
            catch (Exception ex)
            {
                failures = Math.Min(failures + 1, 6);
                delay = TimeSpan.FromSeconds(Math.Min(120, 5 * Math.Pow(2, failures)) + Random.Shared.NextDouble() * 3);
                if (ex is RelayException { RetryAfter: TimeSpan retry } && retry > delay) delay = retry;
                rateLimited = ex is RelayException { StatusCode: System.Net.HttpStatusCode.TooManyRequests };
                var message = ex is RelayException { Status: RelayProtocol.Upgrade }
                    ? "Sync needs an update. Update Shout Calendar and Sync."
                    : $"Sync failed; retrying in {Math.Ceiling(delay.TotalSeconds):0}s.";
                Log.Warning("Sync pass failed ({Failure}); retrying with backoff.", RelayDiagnostics.Failure(ex));
                try { await this.PublishStatus(message, null, token, new SyncProgress(
                    ex is RelayException { Status: RelayProtocol.Upgrade } ? SyncPhase.Upgrade : SyncPhase.Retry,
                    DateTimeOffset.UtcNow, this.activeRelay, NextAttempt: DateTimeOffset.UtcNow + delay,
                    Detail: RelayDiagnostics.Failure(ex))); }
                catch (IpcError) { }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            }
            try
            {
                if (rateLimited) await Task.Delay(delay, token);
                else await this.wake.WaitAsync(delay, token);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    private sealed record Prepared(string World, string Snapshot, string Outbound, bool Force);

    private async Task Pass(CancellationToken token)
    {
        this.storedSnapshot ??= File.Exists(this.storePath) ? await File.ReadAllTextAsync(this.storePath, token) : "";
        var store = this.storedSnapshot;
        var prepared = await this.OnFramework(() =>
        {
            var world = WorldName();
            if (world is null || !this.attach.InvokeFunc(this.proof, world)) return null;
            this.apply.InvokeFunc(this.proof, store);
            var reset = Interlocked.Exchange(ref this.syncResyncRequested, 0) != 0;
            var force = Interlocked.Exchange(ref this.syncNowRequested, 0) != 0 || reset;
            if (reset)
            {
                this.status.InvokeFunc(this.proof, JsonSerializer.Serialize(new SyncStatusUpdate("Re-syncing from relay…")));
                this.transport.ResetCache();
                this.sentRevision.Clear();
            }
            return new Prepared(world, this.read.InvokeFunc(this.proof), this.pull.InvokeFunc(this.proof), force);
        }, token);
        if (prepared is null)
        {
            await this.PublishStatus("Waiting for a playable world", null, token, new SyncProgress(SyncPhase.Paused, DateTimeOffset.UtcNow));
            return;
        }
        var book = new SyncBook(prepared.World);
        if (!book.ApplyJson(prepared.Snapshot, foldReposts: false)) return;
        book.ForceShare();
        this.passBook = book;
        if (this.attachedBook != book.BookId)
        {
            this.attachedBook = book.BookId;
            Log.Information("Shout Calendar Sync attached; saved choices retained.");
            this.attachedAt = DateTimeOffset.UtcNow;
            this.sentRevision.Clear();
            this.transport.ResetCache();
        }
        this.missingNotice = false;
        var ready = prepared.Force || DateTimeOffset.UtcNow >= this.attachedAt!.Value.AddSeconds(book.HoldOffSeconds);
        var outbound = RelayCodec.DecodeList(Encoding.UTF8.GetBytes(prepared.Outbound));
        var backup = book.DistinctBackup;
        var hasBackup = backup is not null;
        var endpoint = book.RelayHost + ":" + book.RelayPort;
        SyncPassSample? sample;
        if (hasBackup && this.failedPrimary == endpoint && DateTimeOffset.UtcNow < this.primaryRetryAt)
            sample = await this.Exchange(book, backup!.Host, backup.Port, outbound, ready, token);
        else
        {
            try
            {
                sample = await this.Exchange(book, book.RelayHost, book.RelayPort, outbound, ready, token);
                this.failedPrimary = "";
            }
            catch (Exception ex) when (!token.IsCancellationRequested && hasBackup && RelayFailover.IsAvailabilityFailure(ex))
            {
                this.failedPrimary = endpoint;
                this.primaryRetryAt = DateTimeOffset.UtcNow.AddSeconds(60);
                sample = await this.Exchange(book, backup!.Host, backup.Port, outbound, ready, token);
            }
        }
        string status;
        if (sample is SyncPassSample measured)
        {
            status = SyncFill.StatusTip(measured.Report)
                ?? (measured.Report.DeferredPast > 0 ? $"{measured.Report.DeferredPast} past invites waiting · {measured.Added} applied"
                    : measured.Added > 0 ? $"Sync complete! {measured.Added} applied" : "Sync complete!");
        }
        else
        {
            var seconds = Math.Max(1, Math.Ceiling((this.attachedAt!.Value.AddSeconds(book.HoldOffSeconds) - DateTimeOffset.UtcNow).TotalSeconds));
            status = $"Waiting to sync ({seconds:0}s)";
        }
        var progressAt = DateTimeOffset.UtcNow;
        var progress = sample is SyncPassSample done
            ? new SyncProgress(done.Report.DroppedCurrent + done.Report.DeferredPast > 0 ? SyncPhase.CatchingUp : SyncPhase.Current,
                progressAt, this.activeRelay, LastSuccess: progressAt, NextAttempt: progressAt.AddSeconds(12),
                Catalog: done.RelayRows, Applied: done.Added, Waiting: done.Report.DroppedCurrent + done.Report.DeferredPast,
                DownloadBytes: done.RelayBytes, UploadBytes: done.UploadBytes,
                Detail: this.failedPrimary.Length > 0 ? "Using your backup relay; the primary will be retried after its cooldown." : "Checked subscribed worlds. Personal deletions and view filters still apply.")
            : new SyncProgress(SyncPhase.Waiting, progressAt, this.activeRelay, NextAttempt: this.attachedAt!.Value.AddSeconds(book.HoldOffSeconds));
        await this.PublishStatus(status, sample, token, progress);
        var updated = await this.OnFramework(() => this.read.InvokeFunc(this.proof), token);
        if (!string.IsNullOrWhiteSpace(updated) && updated != store)
        {
            AtomicFile.Write(this.storePath, Encoding.UTF8.GetBytes(updated));
            this.storedSnapshot = updated;
        }
    }

    private async Task<SyncPassSample?> Exchange(SyncBook book, string host, int port,
        IReadOnlyList<SyncAnnouncement> outbound, bool fetch, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(host) || port < 1) throw new InvalidOperationException("Choose a relay to sync.");
        var started = DateTimeOffset.UtcNow;
        this.activeRelay = SyncRelays.Display(host, port);
        await this.PublishStatus("Sharing invitations", null, token, new SyncProgress(SyncPhase.Uploading, started, this.activeRelay));
        var endpoint = host.ToLowerInvariant() + ":" + port + "|";
        var currentKeys = new HashSet<string>(outbound.Select(item => endpoint + SyncMerge.Key(item)), StringComparer.Ordinal);
        foreach (var key in this.sentRevision.Keys.Where(key => key.StartsWith(endpoint, StringComparison.Ordinal) && !currentKeys.Contains(key)).ToArray())
            this.sentRevision.Remove(key);
        var sent = this.sentRevision.Where(pair => pair.Key.StartsWith(endpoint, StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key[endpoint.Length..], pair => pair.Value, StringComparer.Ordinal);
        var uploaded = 0;
        foreach (var item in SyncBudget.SelectUploads(outbound, book.Limits, sent).Take(book.Limits.MaxItemsPerTick))
        {
            try { await this.transport.SubmitAsync(host, port, item, book.Limits, token); }
            catch (RelayException ex) when (ex.Status == RelayProtocol.Refused)
            {
                this.sentRevision[endpoint + SyncMerge.Key(item)] = Math.Max(1, item.Revision);
                throw;
            }
            uploaded += item.PayloadBytes;
            this.sentRevision[endpoint + SyncMerge.Key(item)] = Math.Max(1, item.Revision);
        }
        if (!fetch) return null;
        await this.PublishStatus("Checking relay", null, token, new SyncProgress(SyncPhase.Downloading, DateTimeOffset.UtcNow, this.activeRelay));
        var incoming = new List<SyncAnnouncement>();
        var bytes = 0;
        var catalog = RelayTransport.IsHttp(host, port) ? await this.transport.FetchCatalogAsync(host, book.Limits, token) : null;
        if (catalog is not null)
        {
            bytes += this.transport.LastDownloadBytes;
            var worlds = new HashSet<string>(book.Worlds.Fetched(), StringComparer.OrdinalIgnoreCase);
            incoming.AddRange(catalog.Where(row => row.Id.StartsWith("gone:", StringComparison.Ordinal) || worlds.Contains(row.World)
                || book.Events.Any(held => held.Id == row.Id && SyncMerge.Same(held, row))));
        }
        else
        {
            long buffered = 0;
            foreach (var world in book.Worlds.Fetched())
            {
                var rows = await this.transport.FetchAsync(host, port, world, book.Limits, token);
                buffered += rows.Sum(row => (long)row.PayloadBytes);
                if (buffered > book.Limits.MaxMemoryBytes) throw new InvalidDataException("Combined world catalogs exceed the buffer budget.");
                incoming.AddRange(rows);
                bytes += this.transport.LastDownloadBytes;
            }
        }
        var before = book.CopyEvents().GroupBy(SyncMerge.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (Revision: group.Max(item => item.Revision), ObservedAt: group.Max(item => item.ObservedAt)), StringComparer.Ordinal);
        book.Ingest(this.proof, incoming, 1);
        var batch = book.CopyEvents().Where(item => !before.TryGetValue(SyncMerge.Key(item), out var revision) || (item.Revision > revision.Revision || item.ObservedAt > revision.ObservedAt))
            .Concat(incoming.Where(item => item.Id.StartsWith("gone:", StringComparison.Ordinal))).ToArray();
        var added = 0;
        if (batch.Length > 0)
        {
            await this.PublishStatus("Applying updates", null, token, new SyncProgress(SyncPhase.Applying, DateTimeOffset.UtcNow, this.activeRelay));
            var json = Encoding.UTF8.GetString(RelayCodec.EncodeList(batch));
            var reply = await this.OnFramework(() => this.ingest.InvokeFunc(this.proof, 1, json), token);
            var applied = JsonSerializer.Deserialize<SyncIngestReply>(reply) ?? throw new InvalidDataException("Calendar rejected the sync reply.");
            added = applied.Added;
        }
        var elapsed = DateTimeOffset.UtcNow - started;
        var report = book.LastFill ?? new SyncFillReport(0, 0, 0, 0, 0, 0, 0, false);
        return new SyncPassSample(incoming.Count, bytes, batch.Length, added, uploaded, elapsed, report with { Slow = elapsed >= SyncFill.SlowPass });
    }

    private async Task PublishStatus(string message, SyncPassSample? sample, CancellationToken token, SyncProgress? progress = null)
    {
        string? line = null;
        if (sample is SyncPassSample measured && this.passBook is { DebugPerf: true } book)
        {
            line = SyncPerf.Line(DateTimeOffset.Now, measured, book.Limits, book.StoredBytes);
            Log.Information("Sync performance: {Line}", line);
        }
        var json = JsonSerializer.Serialize(new SyncStatusUpdate(message, line, Progress: progress));
        await this.OnFramework(() => this.status.InvokeFunc(this.proof, json), token);
    }

    private void OpenCalendar()
    {
        try { this.openCalendar.InvokeFunc(); }
        catch (IpcError)
        {
            ChatGui.Print("Shout Calendar Sync requires Shout Calendar. Install and enable Shout Calendar, then open Sync again.");
        }
    }

    private static string? WorldName()
    {
        if (PlayerState.IsLoaded && PlayerState.CurrentWorld.IsValid)
        {
            var name = PlayerState.CurrentWorld.Value.Name.ExtractText();
            if (PlayableWorlds.TryCanonical(name, out var world)) return world;
        }
        return null;
    }
}
