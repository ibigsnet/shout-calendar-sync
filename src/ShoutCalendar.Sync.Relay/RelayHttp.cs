using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ShoutCalendar.Core;

namespace ShoutCalendar.Sync;

public sealed class RelayHttp : IDisposable
{
    private readonly RelayLog log = new();
    private readonly HttpListener listener = new();
    private readonly string storeDirectory;
    private readonly IDisposable storeLease;
    private long persistedRevision;
    private readonly string[] peers;
    private readonly int minimumProtocol;
    private readonly string adminToken;
    private readonly HashSet<string> trustedProxies;
    private readonly int globalRequestsPerSecond;
    private long requestSecond;
    private int requestsThisSecond;
    private readonly int retentionDays;
    private readonly int requestsPerClient;
    private readonly RelayMeter meter = new();
    private readonly object gate = new();
    private readonly SemaphoreSlim connections;
    private readonly ConcurrentDictionary<long, Task> pending = new();
    private readonly Dictionary<string, CachedReply> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> peerETags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ClientRate> clientRates = new(StringComparer.Ordinal);
    private readonly HttpClient peerClient = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        { Timeout = TimeSpan.FromSeconds(15) };
    private CancellationTokenSource? cancel;
    private Task? loop;
    private long sequence;
    private long second;
    private int bytesThisSecond;
    private int itemsThisSecond;
    private DateTimeOffset prunedAt;
    private sealed record CachedReply(long Revision, byte[] Body, string Signature, string ETag);
    private readonly record struct ClientRate(long Window, int Requests);

    public RelayHttp(string storeDirectory, int port, SyncLimits? limits = null, IEnumerable<string>? peers = null,
        int minimumProtocol = RelayProtocol.Version, bool loopback = false, string? adminToken = null,
        IEnumerable<string>? trustedProxies = null, int retentionDays = 14, int requestsPerClient = 120,
        int globalRequestsPerSecond = 2000)
    {
        Directory.CreateDirectory(storeDirectory);
        this.storeDirectory = storeDirectory;
        this.log.Limits = (limits ?? new SyncLimits { MaxConnections = 128 }).Clamp();
        this.connections = new SemaphoreSlim(this.log.Limits.MaxConnections);
        this.minimumProtocol = Math.Max(1, minimumProtocol);
        this.adminToken = adminToken ?? "";
        this.trustedProxies = new HashSet<string>((trustedProxies ?? []).Select(value => IPAddress.Parse(value).ToString()), StringComparer.Ordinal);
        this.globalRequestsPerSecond = Math.Clamp(globalRequestsPerSecond, 1, 100_000);
        this.retentionDays = Math.Clamp(retentionDays, 1, 365);
        this.requestsPerClient = Math.Clamp(requestsPerClient, 1, 100_000);
        this.peers = (peers ?? []).Where(peer => Uri.TryCreate(peer, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        this.storeLease = RelayFiles.Acquire(storeDirectory);
        try
        {
            RelayFiles.Load(this.storeDirectory, this.log);
            this.persistedRevision = this.log.Revision;
            this.Prune();
        }
        catch { this.storeLease.Dispose(); throw; }
        this.listener.Prefixes.Add(loopback ? $"http://127.0.0.1:{port}/" : $"http://*:{port}/");
    }

    public void Start()
    {
        this.listener.Start();
        this.cancel = new CancellationTokenSource();
        var token = this.cancel.Token;
        this.loop = Task.Run(() => this.Run(token));
    }

    public void Dispose()
    {
        this.cancel?.Cancel();
        this.listener.Close();
        this.peerClient.CancelPendingRequests();
        try { this.loop?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) { }
        catch (ObjectDisposedException) { }
        this.peerClient.Dispose();
        this.cancel?.Dispose();
        this.connections.Dispose();
        this.storeLease.Dispose();
    }

    private async Task Run(CancellationToken token)
    {
        var peerTask = this.PeerLoop(token);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var context = await this.listener.GetContextAsync().WaitAsync(token);
                if (!this.connections.Wait(0))
                {
                    this.meter.NoteSubmit(RelayProtocol.Dropped);
                    try { await Write(context, RelayProtocol.Dropped, code: 429, token: token); }
                    catch (Exception) { TryClose(context, 429); }
                    continue;
                }
                var id = Interlocked.Increment(ref this.sequence);
                var task = this.Handle(context, token);
                this.pending[id] = task;
                _ = task.ContinueWith(completed => this.pending.TryRemove(id, out _),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (token.IsCancellationRequested && ex is OperationCanceledException or HttpListenerException or ObjectDisposedException) { }
        finally
        {
            try { await Task.WhenAll(this.pending.Values.Append(peerTask)); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }

    private async Task Handle(HttpListenerContext context, CancellationToken token)
    {
        this.meter.BeginRequest();
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        token = requestTimeout.Token;
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        var path = context.Request.Url?.AbsolutePath ?? "";
        try
        {
            var clientKey = this.ClientKey(context);
            if (!this.AllowClient(clientKey, started))
            {
                this.meter.NoteSubmit(RelayProtocol.Dropped);
                await Write(context, RelayProtocol.Dropped, code: 429, token: token);
                return;
            }
            if (path == "/metrics") { await this.WriteMetrics(context, token); return; }
            if (path == "/v1/status")
            {
                await Write(context, this.NeedsUpgrade(context) ? RelayProtocol.Upgrade : this.StatusWord(), token: token);
                return;
            }
            if (context.Request.HttpMethod != "POST")
            {
                await Write(context, RelayProtocol.Refused, code: 405, token: token);
                return;
            }
            if (path is not ("/v1/submit" or "/v1/fetch" or "/v1/catalog" or "/v1/purge"))
            {
                await Write(context, RelayProtocol.Refused, code: 404, token: token);
                return;
            }
            if (context.Request.ContentLength64 > RelayIo.MaxBody)
            {
                await Write(context, RelayProtocol.Refused, code: 413, token: token);
                return;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var body = await ReadBounded(context.Request.InputStream, RelayIo.MaxBody, timeout.Token);
            if (path == "/v1/purge")
            {
                var supplied = Encoding.UTF8.GetBytes(context.Request.Headers["X-Sync-Admin"] ?? "");
                if (this.adminToken.Length == 0 || !CryptographicOperations.FixedTimeEquals(supplied, Encoding.UTF8.GetBytes(this.adminToken)))
                {
                    await Write(context, RelayProtocol.Denied, code: 403, token: token);
                    return;
                }
                this.Purge(Encoding.UTF8.GetString(body));
                await Write(context, RelayProtocol.Ok, token: token);
                return;
            }
            if (this.NeedsUpgrade(context))
            {
                await Write(context, RelayProtocol.Upgrade, code: 426, token: token);
                return;
            }
            var signature = Convert.FromBase64String(context.Request.Headers["X-Sync-Signature"] ?? "");
            using var signer = SyncProof.CreatePlatformSigner();
            if (body.Length == 0 || signature.Length != 64 || !signer.VerifyData(body, signature, HashAlgorithmName.SHA256))
            {
                this.meter.NoteSubmit(RelayProtocol.Denied);
                await Write(context, RelayProtocol.Denied, code: 403, token: token);
                return;
            }
            this.meter.NoteClient(clientKey, started);
            if (path == "/v1/submit")
            {
                var status = this.Submit(body);
                this.meter.NoteSubmit(status);
                await Write(context, status, code: status == RelayProtocol.Dropped ? 429 : 200, token: token);
                return;
            }
            var world = path == "/v1/catalog" ? "*" : Encoding.UTF8.GetString(body).Trim();
            var reply = this.ReadSnapshot(world, signer);
            if (reply is null) { await Write(context, RelayProtocol.Refused, code: 400, token: token); return; }
            if (path == "/v1/fetch") this.meter.NoteFetch(); else this.meter.NoteCatalog();
            context.Response.Headers["ETag"] = reply.ETag;
            if (context.Request.Headers["If-None-Match"] == reply.ETag)
            {
                await Write(context, RelayProtocol.Ok, payload: [], code: 304, token: token);
                return;
            }
            await Write(context, RelayProtocol.Ok, reply.Signature, reply.Body, token: token);
        }
        catch (OperationCanceledException) { TryClose(context, 408); }
        catch (InvalidDataException) { TryClose(context, 413); }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException) { TryClose(context, 400); }
        catch (Exception ex)
        {
            this.meter.NoteFailure();
            Console.Error.WriteLine($"relay {path}: {ex.GetType().Name}");
            TryClose(context, 503);
        }
        finally
        {
            if (path != "/metrics") this.meter.NoteDuration(watch.Elapsed, started);
            this.meter.EndRequest();
            this.connections.Release();
        }
    }

    private string Submit(byte[] body)
    {
        lock (this.gate)
        {
            this.Prune();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now != this.second) { this.second = now; this.bytesThisSecond = 0; this.itemsThisSecond = 0; }
            var candidate = RelayCodec.Decode(body);
            var cutoff = DateTimeOffset.UtcNow.AddDays(-this.retentionDays);
            var status = candidate is not null && RelayLog.Expired(candidate, cutoff, DateOnly.FromDateTime(cutoff.UtcDateTime))
                ? RelayProtocol.Refused : this.log.AcceptVerified(body, 1, this.bytesThisSecond, this.itemsThisSecond);
            this.bytesThisSecond = (int)Math.Min(int.MaxValue, (long)this.bytesThisSecond + body.Length);
            this.itemsThisSecond++;
            this.Save();
            return status;
        }
    }

    private void Purge(string key)
    {
        lock (this.gate) { this.log.Purge(key); this.Save(); }
    }

    private CachedReply? ReadSnapshot(string world, ECDsa signer)
    {
        lock (this.gate) { this.Prune(); this.Save(); return this.Snapshot(world, signer); }
    }

    private CachedReply? Snapshot(string world, ECDsa signer)
    {
        if (world != "*" && !PlayableWorlds.TryCanonical(world, out world)) return null;
        if (this.cache.TryGetValue(world, out var cached) && cached.Revision == this.log.Revision) return cached;
        IReadOnlyList<SyncAnnouncement> rows;
        if (world == "*") rows = this.log.Events.Where(item => ShareFormat.Accepts(item.ShareFormat)
            && !this.log.Tombstones.Contains(item.Id) && !this.log.Tombstones.Contains(item.ContentKey)).ToArray();
        else if (this.log.ReadVerified(world, out rows) != RelayProtocol.Ok) return null;
        var gone = this.log.Tombstones.Select(key => new SyncAnnouncement { Id = "gone:" + key, ContentKey = key, World = world });
        var payload = RelayCodec.EncodeList(rows.Concat(gone));
        cached = new CachedReply(this.log.Revision, payload,
            Convert.ToBase64String(signer.SignData(payload, HashAlgorithmName.SHA256)),
            '"' + Convert.ToHexString(SHA256.HashData(payload)) + '"');
        var cacheLimit = Math.Max(1, this.log.Limits.MaxMemoryBytes - this.log.StoredBytes);
        if (this.cache.Values.Sum(item => (long)item.Body.Length) + payload.Length > cacheLimit) this.cache.Clear();
        if (payload.Length <= cacheLimit) this.cache[world] = cached;
        return cached;
    }

    private void Prune()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - this.prunedAt < TimeSpan.FromHours(1)) return;
        var removed = this.log.PruneLegacy() + this.log.PruneExpired(now, this.retentionDays);
        if (removed > 0) this.Save();
        this.prunedAt = now;
    }

    private void Save()
    {
        if (this.persistedRevision == this.log.Revision) return;
        RelayFiles.Save(this.storeDirectory, this.log);
        this.persistedRevision = this.log.Revision;
        this.cache.Clear();
    }

    private async Task PeerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var peer in this.peers)
            {
                try { await this.Pull(peer, token); }
                catch (Exception) when (!token.IsCancellationRequested) { this.meter.NotePeer(peer, false); }
            }
            await Task.Delay(TimeSpan.FromSeconds(20 + Random.Shared.NextDouble() * 5), token);
        }
    }

    internal async Task Pull(string peer, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        token = timeout.Token;
        var body = Encoding.UTF8.GetBytes("catalog");
        using var signer = SyncProof.CreatePlatformSigner();
        using var request = new HttpRequestMessage(HttpMethod.Post, peer.TrimEnd('/') + "/v1/catalog");
        request.Headers.TryAddWithoutValidation("X-Sync-Protocol", RelayProtocol.Version.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Sync-Share-Format", ShareFormat.Current.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Sync-Signature", Convert.ToBase64String(signer.SignData(body, HashAlgorithmName.SHA256)));
        request.Content = new ByteArrayContent(body);
        if (this.peerETags.TryGetValue(peer, out var etag)) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var response = await this.peerClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotModified) { this.meter.NotePeer(peer, true); return; }
        response.EnsureSuccessStatusCode();
        if (!response.Headers.TryGetValues("X-Sync-Status", out var states) || states.FirstOrDefault() != RelayProtocol.Ok)
            throw new InvalidDataException("Peer refused the catalog request.");
        var payload = await ReadBounded(await response.Content.ReadAsStreamAsync(token), this.log.Limits.MaxMemoryBytes, token);
        var signature = response.Headers.TryGetValues("X-Sync-Signature", out var values) ? values.FirstOrDefault() : null;
        if (signature is null || !signer.VerifyData(payload, Convert.FromBase64String(signature), HashAlgorithmName.SHA256))
            throw new InvalidDataException("Peer signature is invalid.");
        if (!RelayCodec.TryDecodeList(payload, out var rows)) throw new InvalidDataException("Invalid peer catalog.");
        if (this.MergePeer(rows) && response.Headers.ETag is not null) this.peerETags[peer] = response.Headers.ETag.ToString();
        this.meter.NotePeer(peer, true);
    }

    private bool MergePeer(IReadOnlyList<SyncAnnouncement> rows)
    {
        lock (this.gate)
        {
            var revision = this.log.Revision;
            var count = 0;
            var bytes = 0;
            var complete = true;
            foreach (var gone in rows.Where(item => item.Id.StartsWith("gone:", StringComparison.Ordinal)))
                this.log.Purge(gone.ContentKey);
            foreach (var item in rows.Where(item => !item.Id.StartsWith("gone:", StringComparison.Ordinal)))
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-this.retentionDays);
                if (RelayLog.Expired(item, cutoff, DateOnly.FromDateTime(cutoff.UtcDateTime))) continue;
                var encoded = RelayCodec.Encode(item);
                var status = this.log.AcceptVerified(encoded, 1, bytes, count);
                if (status == RelayProtocol.Dropped) { complete = false; break; }
                if (status == RelayProtocol.Stored && this.log.Revision != revision)
                {
                    bytes += encoded.Length;
                    count++;
                    revision = this.log.Revision;
                }
            }
            this.Save();
            return complete;
        }
    }

    private bool AllowClient(string client, DateTimeOffset now)
    {
        var window = now.ToUnixTimeSeconds() / 10;
        lock (this.clientRates)
        {
            var second = now.ToUnixTimeSeconds();
            if (second != this.requestSecond) { this.requestSecond = second; this.requestsThisSecond = 0; }
            if (++this.requestsThisSecond > this.globalRequestsPerSecond) return false;
            if (this.clientRates.Count >= 10_000)
            {
                foreach (var stale in this.clientRates.Where(pair => pair.Value.Window < window).Select(pair => pair.Key).ToArray())
                    this.clientRates.Remove(stale);
                if (this.clientRates.Count >= 10_000 && !this.clientRates.ContainsKey(client)) return false;
            }
            this.clientRates.TryGetValue(client, out var previous);
            var requests = previous.Window == window ? previous.Requests + 1 : 1;
            this.clientRates[client] = new ClientRate(window, requests);
            return requests <= this.requestsPerClient;
        }
    }

    private string ClientKey(HttpListenerContext context)
    {
        var remote = context.Request.RemoteEndPoint?.Address.ToString() ?? "unknown";
        if (this.trustedProxies.Contains(remote) && IPAddress.TryParse(context.Request.Headers["X-Real-IP"], out var forwarded)) return forwarded.ToString();
        return context.Request.RemoteEndPoint?.Address.ToString() ?? "unknown";
    }

    private string StatusWord()
    {
        lock (this.gate)
            return this.persistedRevision != this.log.Revision || RelayHealth.IsDegraded(this.meter.Load(this.log.StoredBytes, this.log.Limits.MaxStoredBytes,
                this.peers.Length, DateTimeOffset.UtcNow)) ? RelayProtocol.Degraded : RelayProtocol.Online;
    }

    private async Task WriteMetrics(HttpListenerContext context, CancellationToken token)
    {
        string metrics;
        lock (this.gate)
        {
            var load = this.meter.Load(this.log.StoredBytes, this.log.Limits.MaxStoredBytes, this.peers.Length, DateTimeOffset.UtcNow);
            metrics = this.meter.Prometheus(load, this.log.Events.Count, this.log.Tombstones.Count, this.peers.Length,
                this.log.Events.Count(item => !ShareFormat.Accepts(item.ShareFormat)));
        }
        context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
        await Write(context, RelayProtocol.Ok, payload: Encoding.UTF8.GetBytes(metrics), token: token);
    }

    private bool NeedsUpgrade(HttpListenerContext context)
    {
        var protocol = int.TryParse(context.Request.Headers["X-Sync-Protocol"], out var version) ? version : RelayProtocol.Version;
        var format = int.TryParse(context.Request.Headers["X-Sync-Share-Format"], out var parsed) ? parsed : ShareFormat.Legacy;
        return protocol < this.minimumProtocol || !ShareFormat.Accepts(format);
    }

    internal static async Task<byte[]> ReadBounded(Stream stream, int maximum, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16_384];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) != 0)
        {
            if (buffer.Length + read > maximum) throw new InvalidDataException("Relay payload exceeded its limit.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static async Task Write(HttpListenerContext context, string status, string? signature = null,
        byte[]? payload = null, int code = 200, CancellationToken token = default)
    {
        context.Response.StatusCode = code;
        context.Response.Headers["X-Sync-Status"] = status;
        if (code == 429) context.Response.Headers["Retry-After"] = "10";
        if (signature is not null) context.Response.Headers["X-Sync-Signature"] = signature;
        payload ??= Encoding.ASCII.GetBytes(status);
        context.Response.ContentLength64 = payload.Length;
        try { await context.Response.OutputStream.WriteAsync(payload, token); }
        finally { context.Response.Close(); }
    }

    private static void TryClose(HttpListenerContext context, int code)
    {
        try
        {
            context.Response.StatusCode = code;
            context.Response.ContentLength64 = 0;
            context.Response.KeepAlive = false;
            context.Response.Close();
        }
        catch (Exception)
        {
            try { context.Response.Abort(); } catch (Exception) { }
        }
    }
}
