namespace ShoutCalendar.Core;

public sealed class RelayLog
{
    private readonly List<SyncAnnouncement> events = new();
    private readonly HashSet<string> tombstones = new(StringComparer.OrdinalIgnoreCase);

    public SyncLimits Limits { get; set; } = new();

    private long measuredRevision = -1;
    private int storedBytes;

    public int StoredBytes
    {
        get
        {
            if (this.measuredRevision != this.Revision)
            {
                this.storedBytes = this.events.Sum(item => item.PayloadBytes);
                this.measuredRevision = this.Revision;
            }
            return this.storedBytes;
        }
    }

    public IReadOnlyList<SyncAnnouncement> Events => this.events;

    public IReadOnlyCollection<string> Tombstones => this.tombstones;

    public long Revision { get; private set; }

    public SyncMergeResult Merge(SyncAnnouncement item)
    {
        var previous = this.events.FirstOrDefault(row => SyncMerge.Same(row, item));
        var previousRevision = previous?.Revision ?? 0;
        var result = SyncMerge.Apply(this.events, item);
        if (result == SyncMergeResult.Updated)
        {
            var kept = this.events.First(row => SyncMerge.Same(row, item));
            kept.Revision = Math.Max(kept.Revision, Math.Min(int.MaxValue - 1, previousRevision + 1));
        }
        if (result != SyncMergeResult.Duplicate)
            this.Revision++;
        return result;
    }

    public void Restore(IEnumerable<SyncAnnouncement> rows)
    {
        foreach (var item in rows)
        {
            if (!Valid(item)) continue;
            item.ContentKey = SyncMerge.Key(item);
            if (item.ObservedAt == default) item.ObservedAt = DateTimeOffset.UtcNow;
            this.Merge(item);
        }
    }

    public string Accept(byte[] payload, byte[]? signature, int openConnections, int bytesThisSecond, int itemsThisTick)
    {
        if (payload is null || payload.Length == 0 || signature is null || !SyncGate.Verify(payload, signature))
            return RelayProtocol.Denied;

        return this.AcceptVerified(payload, openConnections, bytesThisSecond, itemsThisTick);
    }

    public string AcceptVerified(byte[] payload, int openConnections, int bytesThisSecond, int itemsThisTick)
    {
        if (payload.Length > 16_384)
            return RelayProtocol.Refused;

        var item = RelayCodec.Decode(payload);
        if (item is null || !Valid(item) || !PlayableWorlds.TryCanonical(item.World, out var world))
            return RelayProtocol.Refused;

        var cap = this.Limits.Clamp();
        item.World = world;
        item.FromSync = true;
        item.HarvestedLocally = false;
        item.Accepted = false;
        item.ContentKey = SyncMerge.Key(item);
        if (this.tombstones.Contains(item.ContentKey) || this.tombstones.Contains(SyncMerge.LegacyKey(item)) || this.tombstones.Contains(item.Id))
            return RelayProtocol.Stored;
        if (this.events.Any(row => SyncMerge.Key(row) == item.ContentKey && item.Revision <= row.Revision && item.ObservedAt <= row.ObservedAt))
            return RelayProtocol.Stored;
        if (itemsThisTick >= cap.MaxItemsPerTick)
            return RelayProtocol.Dropped;
        var now = DateTimeOffset.UtcNow;
        if (item.ObservedAt == default || item.ObservedAt > now) item.ObservedAt = now;
        var replaced = SyncMerge.PrepareReplacement(this.events, item);
        var measured = RelayCodec.Decode(RelayCodec.Encode(item))!;
        if (replaced is not null)
            measured.Revision = Math.Max(measured.Revision, Math.Min(int.MaxValue - 1, replaced.Revision + 1));
        var weight = Math.Max(payload.Length, measured.PayloadBytes);
        var admitted = SyncBudget.Admit(
            [item],
            _ => weight,
            cap,
            openConnections,
            bytesThisSecond,
            this.StoredBytes,
            this.StoredBytes,
            _ => replaced?.PayloadBytes ?? 0);
        if (admitted.Count == 0)
            return RelayProtocol.Dropped;

        this.Merge(item);
        return RelayProtocol.Stored;
    }

    public string Read(byte[] payload, byte[]? signature, out IReadOnlyList<SyncAnnouncement> rows)
    {
        rows = Array.Empty<SyncAnnouncement>();
        if (payload is null || payload.Length == 0 || signature is null || !SyncGate.Verify(payload, signature))
            return RelayProtocol.Denied;
        var world = System.Text.Encoding.UTF8.GetString(payload).Trim();
        return this.ReadVerified(world, out rows);
    }

    public string ReadVerified(string world, out IReadOnlyList<SyncAnnouncement> rows)
    {
        rows = Array.Empty<SyncAnnouncement>();
        if (!PlayableWorlds.TryCanonical(world, out var canonical))
            return RelayProtocol.Refused;
        rows = this.events
            .Where(item => item.World.Equals(canonical, StringComparison.OrdinalIgnoreCase) && SharePolicy.IsShareable(item.Channel))
            .Where(item => ShareFormat.Accepts(item.ShareFormat))
            .Where(item => !this.tombstones.Contains(item.ContentKey) && !this.tombstones.Contains(SyncMerge.LegacyKey(item)) && !this.tombstones.Contains(item.Id))
            .ToArray();
        return RelayProtocol.Ok;
    }

    public static bool Valid(SyncAnnouncement item) =>
        SharePolicy.IsShareable(item.Channel) && ShareFormat.Accepts(item.ShareFormat)
        && PlayableWorlds.TryCanonical(item.World, out _)
        && !string.IsNullOrWhiteSpace(item.Id) && item.Id.Length <= 128 && !item.Id.StartsWith("gone:", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(item.Text) && item.Text.Length <= 4096
        && item.Title is { Length: <= 512 } && item.Place is { Length: <= 1024 }
        && item.Category is { Length: <= 128 } && item.Date is { Length: <= 32 } && item.Time is { Length: <= 32 }
        && item.ContentKey is { Length: <= 128 } && item.PluginVersion is { Length: <= 64 }
        && (item.Date.Length == 0 || DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _))
        && (item.Time.Length == 0 || TimeOnly.TryParseExact(item.Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _))
        && item.SourceTimeZone is { Length: <= 128 } && item.Repeat is { Length: <= 64 } && item.End is { Length: <= 32 }
        && (item.End.Length == 0 || TimeOnly.TryParseExact(item.End, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _))
        && (item.StartUtc is null || item.EndUtc is null || item.EndUtc >= item.StartUtc)
        && item.Revision is >= 0 and < int.MaxValue;

    public int PruneExpired(DateTimeOffset now, int retentionDays)
    {
        var cutoff = now.AddDays(-retentionDays);
        var day = DateOnly.FromDateTime(cutoff.UtcDateTime);
        var count = this.events.RemoveAll(item => Expired(item, cutoff, day));
        if (count > 0) this.Revision++;
        return count;
    }

    public static bool Expired(SyncAnnouncement item, DateTimeOffset cutoff, DateOnly day)
    {
        if (string.IsNullOrEmpty(item.Repeat) && DateOnly.TryParseExact(item.Date, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
            return date < day;
        return item.ObservedAt != default && item.ObservedAt < cutoff;
    }

    public int PruneLegacy()
    {
        var count = this.events.RemoveAll(item => !ShareFormat.Accepts(item.ShareFormat));
        if (count > 0)
            this.Revision++;
        return count;
    }

    public int PrunePast(DateOnly cutoff)
    {
        var count = this.events.RemoveAll(item => DateOnly.TryParseExact(item.Date, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
            && date < cutoff);
        if (count > 0)
            this.Revision++;
        return count;
    }

    public bool Purge(string idOrKey)
    {
        if (string.IsNullOrWhiteSpace(idOrKey))
            return false;
        var item = this.events.FirstOrDefault(row =>
            row.Id.Equals(idOrKey, StringComparison.OrdinalIgnoreCase)
            || row.ContentKey.Equals(idOrKey, StringComparison.OrdinalIgnoreCase)
            || SyncMerge.Key(row).Equals(idOrKey, StringComparison.OrdinalIgnoreCase));
        if (idOrKey.Length > 256) return false;
        var key = item is null ? idOrKey.Trim() : (string.IsNullOrEmpty(item.ContentKey) ? SyncMerge.Key(item) : item.ContentKey);
        if (this.tombstones.Add(key))
            this.Revision++;
        if (item is not null)
        {
            if (this.tombstones.Add(item.Id)) this.Revision++;
            this.events.RemoveAll(row => row.Id == item.Id || SyncMerge.Key(row) == key);
            this.Revision++;
        }

        return item is not null;
    }

    public void RestoreTombstones(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                if (this.tombstones.Add(key.Trim()))
                    this.Revision++;
            }
        }
    }
}
