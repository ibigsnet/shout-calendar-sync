using System.Globalization;
using System.Text.Json;

namespace ShoutCalendar.Core;

public sealed class SyncSnapshot
{
    public ShareSettings Settings { get; set; } = new();

    public SyncLimits Limits { get; set; } = new();

    public List<string> CheckedWorlds { get; set; } = new();

    public string Selected { get; set; } = "";

    public string LastViewed { get; set; } = "";

    public List<string> ViewedWorlds { get; set; } = new();

    public bool ShowSync { get; set; } = true;

    public string RelayHost { get; set; } = "";

    public int RelayPort { get; set; }

    public string RelayChoice { get; set; } = SyncRelays.PublicLabel;

    public int HoldOffSeconds { get; set; } = 60;

    public string SyncStatus { get; set; } = "";

    public bool Informedaholic { get; set; }

    public bool MirrorRelay { get; set; }
    public string BackupRelayHost { get; set; } = "";
    public int BackupRelayPort { get; set; } = 443;
    public string BackupRelayChoice { get; set; } = "";

    public bool DebugPerf { get; set; }

    public List<string> PerfLog { get; set; } = new();

    public List<SyncAnnouncement> Events { get; set; } = new();

    public List<string> DismissedKeys { get; set; } = new();

    public string BookId { get; set; } = "";
}

public sealed class SyncBook
{
    public SyncBook(string currentWorld)
    {
        this.Worlds = new WorldCalendar(currentWorld);
        this.BookId = Guid.NewGuid().ToString("N");
    }

    public string BookId { get; private set; }

    public List<SyncAnnouncement> Events { get; } = new();

    private readonly object eventsGate = new();

    public SyncAnnouncement[] CopyEvents()
    {
        lock (this.eventsGate)
            return this.Events.ToArray();
    }

    public List<string> DismissedKeys { get; } = new();

    public ShareSettings Settings { get; private set; } = new();

    public WorldCalendar Worlds { get; }

    public SyncLimits Limits { get; set; } = new();

    public bool SettingsStored { get; private set; } = true;

    public bool ShowSync { get; set; } = true;

    public string RelayHost { get; set; } = "";

    public int RelayPort { get; set; }

    public string RelayChoice { get; set; } = SyncRelays.PublicLabel;

    public int HoldOffSeconds { get; set; } = 60;

    public bool Informedaholic { get; set; }

    public bool MirrorRelay { get; set; }
    public string BackupRelayHost { get; set; } = "";
    public int BackupRelayPort { get; set; } = 443;
    public string BackupRelayChoice { get; set; } = SyncRelays.PublicLabel;

    public RelayEndpoint? Backup => this.BackupRelayChoice switch
    {
        SyncRelays.PublicLabel => new(SyncRelays.PublicHost, SyncRelays.PublicPort),
        SyncRelays.CustomLabel when !string.IsNullOrWhiteSpace(this.BackupRelayHost) => new(this.BackupRelayHost, this.BackupRelayPort),
        _ => null,
    };

    public RelayEndpoint? DistinctBackup => this.Backup is { } backup
        && !SyncRelays.SameEndpoint(this.RelayHost, this.RelayPort, backup.Host, backup.Port) ? backup : null;

    public string RelayStatus { get; set; } = "";

    public string SyncStatus { get; set; } = "";

    public SyncFillReport? LastFill { get; private set; }

    public bool DebugPerf { get; set; }

    private readonly List<string> perfLog = new();

    private readonly object perfGate = new();

    public string[] CopyPerf()
    {
        lock (this.perfGate)
            return this.perfLog.ToArray();
    }

    public void NotePerf(string line)
    {
        if (!this.DebugPerf || string.IsNullOrWhiteSpace(line))
            return;
        lock (this.perfGate)
        {
            this.perfLog.Add(SyncRelays.Redact(line.Trim()));
            var extra = this.perfLog.Count - SyncPerf.Keep;
            if (extra > 0)
                this.perfLog.RemoveRange(0, extra);
        }
    }

    public SyncProgress? Progress { get; private set; }

    public void PublishStatus(string status, string? performanceLine = null, SyncProgress? progress = null)
    {
        this.SyncStatus = SyncRelays.Redact(status);
        if (progress is not null) this.Progress = progress with
        {
            Relay = SyncRelays.Redact(progress.Relay), Detail = SyncRelays.Redact(progress.Detail),
            LastSuccess = progress.LastSuccess ?? this.Progress?.LastSuccess,
        };
        if (!string.IsNullOrWhiteSpace(performanceLine)) this.NotePerf(performanceLine);
    }

    public bool RestoreIfEmpty(string? json)
    {
        if (!SyncResume.Choose(this.ToJson(), json).RestoreStore) return false;
        return this.ApplyJson(json);
    }

    public void ClearPerf()
    {
        lock (this.perfGate)
            this.perfLog.Clear();
    }

    public int StoredBytes
    {
        get
        {
            lock (this.eventsGate)
                return this.Events.Sum(item => item.PayloadBytes);
        }
    }

    public void ForceShare()
    {
        this.Settings.Shout.Contribute = true;
        this.Settings.Yell.Contribute = true;
        this.Settings.Shout.Receive = true;
        this.Settings.Yell.Receive = true;
        this.Settings.ShareUnaccepted = true;
        this.Settings.ShareAccepted = true;
        this.Settings.ShareNoteUpdates = true;
        if (this.RelayChoice != SyncRelays.CustomLabel)
        {
            this.RelayChoice = SyncRelays.PublicLabel;
            this.RelayHost = SyncRelays.PublicHost;
            this.RelayPort = SyncRelays.PublicPort;
        }
    }

    public void PrepareDebugResync()
    {
        lock (this.eventsGate)
        {
            this.DismissedKeys.Clear();
            foreach (var item in this.Events)
            {
                item.ExcludedDates.Clear(); item.RepeatUntil = null; item.SeriesDeleted = false;
            }
        }
        this.SyncStatus = "Re-syncing from relay…";
    }

    public void Detach()
    {
        lock (this.eventsGate)
            this.Events.RemoveAll(item => item.FromSync && !item.HarvestedLocally);
        lock (this.eventsGate)
            foreach (var item in this.Events)
                item.FromSync = false;
        this.Settings = new ShareSettings();
        this.SettingsStored = false;
        this.ShowSync = true;
        this.RelayHost = "";
        this.RelayPort = 0;
        this.RelayChoice = SyncRelays.PublicLabel;
        this.HoldOffSeconds = 60;
        this.Informedaholic = false;
        this.MirrorRelay = false;
        this.BackupRelayHost = "";
        this.BackupRelayPort = SyncRelays.PublicPort;
        this.BackupRelayChoice = SyncRelays.PublicLabel;
        this.Progress = null;
        this.DebugPerf = false;
        this.ClearPerf();
        this.RelayStatus = "";
        this.SyncStatus = "";
        this.Limits = new SyncLimits();
        this.Worlds.ClearExtras();
    }

    public bool SetCategory(string id, string? label)
    {
        lock (this.eventsGate)
            return this.SetCategoryUnlocked(id, label);
    }

    private bool SetCategoryUnlocked(string id, string? label)
    {
        var item = this.Events.FirstOrDefault(row => row.Id == id);
        if (item is null)
            return false;
        return EventCategories.TrySet(item, label);
    }

    public bool Delete(string id, DateOnly occurrence, DeleteScope scope)
    {
        lock (this.eventsGate)
        {
            var item = this.Events.FirstOrDefault(row => row.Id == id);
            if (item is null) return false;
            var entry = SyncClock.Entry(item);
            if (entry.Repeat is null) return this.DismissUnlocked(id);
            var changed = EventDeletion.Apply(entry, occurrence, scope);
            item.ExcludedDates = changed.ExcludedDates?.ToList() ?? new();
            item.RepeatUntil = changed.RepeatUntil;
            item.SeriesDeleted = changed.SeriesDeleted;
            return true;
        }
    }

    public bool Dismiss(string id)
    {
        lock (this.eventsGate)
            return this.DismissUnlocked(id);
    }

    private bool DismissUnlocked(string id)
    {
        var item = this.Events.FirstOrDefault(row => row.Id == id);
        if (item is null)
            return false;
        this.RememberDismissed(item);
        this.Events.Remove(item);
        return true;
    }

    public int DismissPast(DateTime now, TimeZoneInfo? zone = null) =>
        this.DismissMatching(item => PastEvents.Ended(item, now, zone));

    public int DismissOpen(ClearTarget target)
    {
        var open = new HashSet<string>(this.Worlds.Viewing(), StringComparer.OrdinalIgnoreCase);
        return this.DismissMatching(item => open.Contains(item.World) && target switch
        {
            ClearTarget.SyncAccepted => item.Accepted,
            ClearTarget.SyncUnaccepted => item.IsSyncPending,
            _ => false,
        });
    }

    public int DismissMatching(Func<SyncAnnouncement, bool> match)
    {
        lock (this.eventsGate)
            return this.DismissMatchingUnlocked(match);
    }

    private int DismissMatchingUnlocked(Func<SyncAnnouncement, bool> match)
    {
        var gone = this.Events.Where(match).ToList();
        foreach (var item in gone)
        {
            this.RememberDismissed(item);
            this.Events.Remove(item);
        }

        return gone.Count;
    }

    private void RememberDismissed(SyncAnnouncement item)
    {
        if (!string.IsNullOrEmpty(item.Id) && !this.DismissedKeys.Contains(item.Id)) this.DismissedKeys.Add(item.Id);
        var key = string.IsNullOrEmpty(item.ContentKey) ? SyncMerge.Key(item) : item.ContentKey;
        if (!this.DismissedKeys.Contains(key))
            this.DismissedKeys.Add(key);
    }

    public bool DeclineRemote(string id)
    {
        lock (this.eventsGate)
            return this.DeclineRemoteUnlocked(id);
    }

    private bool DeclineRemoteUnlocked(string id)
    {
        var item = this.Events.FirstOrDefault(row => row.Id == id);
        if (item is null)
            return false;
        item.Declined = true;
        item.Accepted = false;
        return true;
    }

    public bool HideRemote(string id, bool hidden = true)
    {
        lock (this.eventsGate)
            return this.HideRemoteUnlocked(id, hidden);
    }

    private bool HideRemoteUnlocked(string id, bool hidden = true)
    {
        var item = this.Events.FirstOrDefault(row => row.Id == id);
        if (item is null || item.Hidden == hidden)
            return false;
        item.Hidden = hidden;
        if (!hidden)
            item.Declined = false;
        return true;
    }

    public int HidePending(bool hidden)
    {
        lock (this.eventsGate)
            return this.HidePendingUnlocked(hidden);
    }

    private int HidePendingUnlocked(bool hidden)
    {
        var count = 0;
        foreach (var item in this.Events)
        {
            if (!item.FromSync || item.HarvestedLocally || item.Accepted || item.Declined || item.Hidden == hidden)
                continue;
            item.Hidden = hidden;
            count++;
        }

        return count;
    }

    public void AcceptSame(CalendarEntry entry, string heardOn)
    {
        lock (this.eventsGate)
            this.AcceptSameUnlocked(entry, heardOn);
    }

    private void AcceptSameUnlocked(CalendarEntry entry, string heardOn)
    {
        var world = ShareWorld.Choose(heardOn, entry.SpeakerWorld, entry.Server);
        var text = $"{entry.EventText} {entry.Place}";
        foreach (var item in this.Events)
        {
            if (!EventIdentity.Compatible(entry, item)) continue;
            if (!EventIdentity.SameRepost(entry, item)
                && !EventIdentity.SameShout(item.World, item.Text, world, text))
                continue;
            item.Declined = false;
            item.Accepted = true;
        }
    }

    public void ApplyTombstones(IEnumerable<string> keys)
    {
        lock (this.eventsGate)
            this.ApplyTombstonesUnlocked(keys);
    }

    private void ApplyTombstonesUnlocked(IEnumerable<string> keys)
    {
        var gone = new HashSet<string>(keys.Where(key => !string.IsNullOrWhiteSpace(key)), StringComparer.OrdinalIgnoreCase);
        if (gone.Count == 0)
            return;
        this.Events.RemoveAll(item => gone.Contains(item.Id) || gone.Contains(item.ContentKey) || gone.Contains(SyncMerge.Key(item)) || gone.Contains(SyncMerge.LegacyKey(item)));
        foreach (var key in gone)
        {
            if (!this.DismissedKeys.Contains(key))
                this.DismissedKeys.Add(key);
        }
    }

    public bool AcceptRemote(string id)
    {
        lock (this.eventsGate)
            return this.AcceptRemoteUnlocked(id);
    }

    public bool SetClock(string id, string dateText, string timeText) => this.SetClock(id, dateText, timeText, out _);

    public bool SetClock(string id, string dateText, string timeText, out ClockInputErrors errors, TimeZoneInfo? zone = null)
    {
        if (!ClockInput.TryParse(dateText, timeText, out var input, out errors)) return false;
        zone ??= TimeZoneInfo.Local;
        lock (this.eventsGate)
        {
            var item = this.Events.FirstOrDefault(row => row.Id == id);
            if (item is null)
            {
                errors = new ClockInputErrors(Message: "This invite is no longer available.");
                return false;
            }
            if (input == ClockInput.FromEntry(SyncClock.Entry(item, zone), zone)) return false;
            if (!input.ValidateZone(zone, out errors)) return false;
            item.Date = input.DateText;
            item.Time = input.Start?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
            item.End = input.End?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
            item.SourceTimeZone = input.Start is null ? "" : zone.Id;
            item.StartUtc = SyncClock.Instant(input.Date, input.Start, item.SourceTimeZone);
            item.EndUtc = SyncClock.Instant(SyncClock.EndDate(input.Date, input.Start, input.End), input.End, item.SourceTimeZone);
            if (EventRepeat.Parse(item.Repeat) is EventRepeat repeat && input.Date is DateOnly anchor)
                item.Repeat = (repeat with { Weekday = anchor.DayOfWeek }).Store();
            item.NoteUpdated = true;
            item.ClockEditedLocally = true;
            item.ContentKey = SyncMerge.Key(item);
            return true;
        }
    }

    private bool AcceptRemoteUnlocked(string id)
    {
        var item = this.Events.FirstOrDefault(row => row.Id == id && row.FromSync && !row.HarvestedLocally);
        if (item is null || item.Accepted)
            return false;
        item.Accepted = true;
        item.Hidden = false;
        return true;
    }

    public int AcceptAllRemote()
    {
        lock (this.eventsGate)
            return this.AcceptAllRemoteUnlocked();
    }

    private int AcceptAllRemoteUnlocked()
    {
        var count = 0;
        foreach (var item in this.Events)
        {
            if (!item.IsSyncPending)
                continue;
            item.Accepted = true;
            count++;
        }

        return count;
    }

    public int Ingest(byte[]? proof, IReadOnlyList<SyncAnnouncement> incoming, int openConnections, PlaceCatalog? places = null)
    {
        if (!SyncGate.AllowRead(proof))
            return -1;
        if (openConnections > this.Limits.Clamp().MaxConnections)
            return 0;

        lock (this.eventsGate)
            this.ApplyTombstonesUnlocked(incoming.Where(item => item.Id.StartsWith("gone:", StringComparison.Ordinal)).Select(item => item.ContentKey));
        lock (this.eventsGate)
        {
            var now = DateTime.Now;
            var fetchedWorlds = this.Worlds.Fetched().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dismissed = this.DismissedKeys.ToHashSet(StringComparer.Ordinal);
            var held = new Dictionary<string, (int Revision, DateTimeOffset Observed)>(StringComparer.Ordinal);
            foreach (var row in this.Events)
            {
                foreach (var key in new[] { row.SourceKey, SyncMerge.Key(row) })
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!held.TryGetValue(key, out var prior)) held[key] = (row.Revision, row.ObservedAt);
                    else held[key] = (Math.Max(prior.Revision, row.Revision), prior.Observed > row.ObservedAt ? prior.Observed : row.ObservedAt);
                }
            }
            bool AlreadyKnown(SyncAnnouncement item) => held.TryGetValue(SyncMerge.Key(item), out var prior)
                && item.Revision <= prior.Revision && item.ObservedAt <= prior.Observed;
            var shareable = SyncFill.Prioritize(
                incoming
                    .Where(item => !item.Id.StartsWith("gone:", StringComparison.Ordinal))
                    .Where(RelayLog.Valid)
                    .Where(item => SharePolicy.ShouldReceive(item.Channel, this.Settings))
                    .Where(item => PlayableWorlds.TryCanonical(item.World, out _))
                    .Where(item => fetchedWorlds.Contains(item.World)
                        || this.Events.Any(row => row.Id == item.Id && SyncMerge.Same(row, item)))
                    .Where(item => !AlreadyKnown(item))
                    .Where(item => !dismissed.Contains(item.Id) && !dismissed.Contains(SyncMerge.Key(item))
                        && !dismissed.Contains(SyncMerge.LegacyKey(item)))
                    .Where(item => ShoutHarvest.IsSharedEvent(item.Text, item.Channel, DateTimeOffset.UtcNow, places)),
                now);
            var admitted = new List<SyncAnnouncement>();
            var receivedBytes = 0;
            var added = 0;
            var storedBytes = this.StoredBytes;
            foreach (var item in shareable)
            {
                if (admitted.Count >= this.Limits.Clamp().MaxItemsPerTick) break;
                var replaced = SyncMerge.PrepareReplacement(this.Events, item);
                if (SyncBudget.Admit([item], row => row.PayloadBytes, this.Limits, openConnections,
                    receivedBytes, storedBytes, storedBytes, _ => replaced?.PayloadBytes ?? 0).Count == 0) continue;
                if (ServerNames.TryAdvertised(item.Text, out var advertised))
                    item.World = advertised;
                if (!PlayableWorlds.TryCanonical(item.World, out var world))
                    continue;
                if (replaced is null && !this.Worlds.Fetched().Contains(world, StringComparer.Ordinal))
                    continue;
                item.World = world;
                item.FromSync = true;
                item.HarvestedLocally = false;
                item.Accepted = false;
                admitted.Add(item);
                receivedBytes += item.PayloadBytes;
                var oldBytes = replaced?.PayloadBytes ?? 0;
                if (SyncMerge.Apply(this.Events, item) == SyncMergeResult.Duplicate)
                    continue;
                storedBytes += item.PayloadBytes - oldBytes;
                added++;
            }

            this.LastFill = SyncFill.Measure(shareable, admitted, now, TimeSpan.Zero);
            if (added > 0) this.FoldRepostsUnlocked();

            if (this.Informedaholic)
                this.AcceptAllRemoteUnlocked();
            if (SyncFill.StatusTip(this.LastFill.Value) is string tip)
                this.SyncStatus = tip;
            return added;
        }
    }

    private int FoldRepostsUnlocked()
    {
        var removed = 0;
        for (var i = 0; i < this.Events.Count; i++)
        {
            for (var j = i + 1; j < this.Events.Count;)
            {
                if (!(EventIdentity.SameRepost(this.Events[i], this.Events[j])
                    || SyncMerge.CanMergeAmong(this.Events, this.Events[i], this.Events[j])))
                {
                    j++;
                    continue;
                }

                SyncMerge.Combine(this.Events[i], this.Events[j]);
                this.Events.RemoveAt(j);
                removed++;
                j = i + 1; // Enrichment may now identify a row skipped earlier in this pass.
            }
        }

        return removed;
    }

    private bool AlreadyHeld(SyncAnnouncement item)
    {
        var key = SyncMerge.Key(item);
        return this.Events.Any(row => (row.SourceKey == key || SyncMerge.Key(row) == key) && item.Revision <= row.Revision && item.ObservedAt <= row.ObservedAt);
    }

    public string ToJson()
    {
        var snapshot = new SyncSnapshot
        {
            Settings = this.Settings,
            Limits = this.Limits,
            CheckedWorlds = this.Worlds.Extras().ToList(),
            Selected = this.Worlds.Selected,
            LastViewed = this.Worlds.LastPicked,
            ViewedWorlds = this.Worlds.Viewing().ToList(),
            ShowSync = this.ShowSync,
            RelayHost = this.RelayHost,
            RelayPort = this.RelayPort,
            RelayChoice = this.RelayChoice,
            HoldOffSeconds = this.HoldOffSeconds,
            SyncStatus = this.SyncStatus,
            Informedaholic = this.Informedaholic,
            MirrorRelay = this.MirrorRelay,
            BackupRelayHost = this.BackupRelayHost,
            BackupRelayPort = this.BackupRelayPort,
            BackupRelayChoice = this.BackupRelayChoice,
            DebugPerf = this.DebugPerf,
            PerfLog = this.CopyPerf().ToList(),
            Events = this.CopyEvents().ToList(),
            DismissedKeys = this.DismissedKeys.ToList(),
            BookId = this.BookId,
        };
        return JsonSerializer.Serialize(snapshot, SnapshotJson.Options);
    }

    public bool ApplyJson(string? json, bool foldReposts = true)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;
        SyncSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<SyncSnapshot>(json, SnapshotJson.Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (snapshot is null)
            return false;
        this.Settings = snapshot.Settings ?? new ShareSettings();
        this.Settings.Shout ??= new ChannelModes();
        this.Settings.Yell ??= new ChannelModes();
        this.Limits = (snapshot.Limits ?? new SyncLimits()).Clamp();
        this.ShowSync = snapshot.ShowSync;
        this.RelayHost = snapshot.RelayHost ?? "";
        this.RelayPort = snapshot.RelayPort < 1 ? 0 : snapshot.RelayPort;
        this.RelayChoice = string.IsNullOrWhiteSpace(snapshot.RelayChoice) ? SyncRelays.PublicLabel : snapshot.RelayChoice;
        this.HoldOffSeconds = snapshot.HoldOffSeconds < 0 ? 0 : snapshot.HoldOffSeconds;
        this.SyncStatus = SyncRelays.Redact(snapshot.SyncStatus ?? "");
        this.Informedaholic = snapshot.Informedaholic;
        this.MirrorRelay = snapshot.MirrorRelay;
        this.BackupRelayHost = snapshot.BackupRelayHost ?? "";
        this.BackupRelayPort = Math.Clamp(snapshot.BackupRelayPort, 1, 65535);
        this.BackupRelayChoice = snapshot.BackupRelayChoice is SyncRelays.PublicLabel or SyncRelays.CustomLabel or SyncRelays.OffLabel
            ? snapshot.BackupRelayChoice
            : string.IsNullOrWhiteSpace(this.BackupRelayHost) || SyncRelays.IsPublic(this.BackupRelayHost, this.BackupRelayPort)
                ? SyncRelays.PublicLabel : SyncRelays.CustomLabel;
        this.DebugPerf = snapshot.DebugPerf;
        lock (this.perfGate)
        {
            this.perfLog.Clear();
            foreach (var line in snapshot.PerfLog ?? [])
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                this.perfLog.Add(SyncRelays.Redact(line.Trim()));
            }

            var extra = this.perfLog.Count - SyncPerf.Keep;
            if (extra > 0)
                this.perfLog.RemoveRange(0, extra);
        }
        if (this.Limits.BytesPerSecond == 65_536)
            this.Limits.BytesPerSecond = 1_000_000;
        if (this.Limits.UploadBytesPerSecond < 1)
            this.Limits.UploadBytesPerSecond = 125_000;
        if (this.Limits.MaxStoredBytes == 1_048_576)
            this.Limits.MaxStoredBytes = 32_000_000;
        if (this.Limits.MaxMemoryBytes == 2_097_152)
            this.Limits.MaxMemoryBytes = 64_000_000;
        if (this.Limits.MaxItemsPerTick == 32)
            this.Limits.MaxItemsPerTick = 64;
        this.Worlds.ClearExtras();
        foreach (var world in snapshot.CheckedWorlds ?? [])
            this.Worlds.SetChecked(world, true);
        if (!string.IsNullOrWhiteSpace(snapshot.LastViewed))
            this.Worlds.RememberPick(snapshot.LastViewed);
        else if (!string.IsNullOrWhiteSpace(snapshot.Selected))
            this.Worlds.RememberPick(snapshot.Selected);
        if (snapshot.ViewedWorlds is { Count: > 0 })
            this.Worlds.UseView(snapshot.ViewedWorlds);
        else if (!string.IsNullOrWhiteSpace(snapshot.Selected))
            this.Worlds.Select(snapshot.Selected);
        lock (this.eventsGate)
        {
            this.Events.Clear();
            foreach (var item in snapshot.Events ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                    continue;
                item.ExcludedDates ??= new();
                this.Events.Add(item);
            }

            this.DismissedKeys.Clear();
            foreach (var key in snapshot.DismissedKeys ?? [])
            {
                if (!string.IsNullOrWhiteSpace(key) && !this.DismissedKeys.Contains(key))
                    this.DismissedKeys.Add(key);
            }

            if (!string.IsNullOrWhiteSpace(snapshot.BookId))
                this.BookId = snapshot.BookId;
            if (foldReposts) this.FoldRepostsUnlocked();
        }

        this.SettingsStored = true;
        return true;
    }
}
