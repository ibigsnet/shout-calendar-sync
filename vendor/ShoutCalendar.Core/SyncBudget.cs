namespace ShoutCalendar.Core;

public sealed class SyncLimits
{
    public int MaxConnections { get; set; } = 4;

    public int BytesPerSecond { get; set; } = 1_000_000;

    public int UploadBytesPerSecond { get; set; } = 125_000;

    public int MaxStoredBytes { get; set; } = 32_000_000;

    public int MaxItemsPerTick { get; set; } = 64;

    public int MaxMemoryBytes { get; set; } = 64_000_000;

    public SyncLimits Clamp()
    {
        static int AtLeastOne(int value) => value < 1 ? 1 : value;
        return new SyncLimits
        {
            MaxConnections = AtLeastOne(this.MaxConnections),
            BytesPerSecond = AtLeastOne(this.BytesPerSecond),
            UploadBytesPerSecond = AtLeastOne(this.UploadBytesPerSecond < 1 ? 125_000 : this.UploadBytesPerSecond),
            MaxStoredBytes = AtLeastOne(this.MaxStoredBytes),
            MaxItemsPerTick = AtLeastOne(this.MaxItemsPerTick),
            MaxMemoryBytes = AtLeastOne(this.MaxMemoryBytes),
        };
    }
}

public static class SyncBudget
{
    public static IReadOnlyList<T> Admit<T>(
        IReadOnlyList<T> incoming,
        Func<T, int> bytesOf,
        SyncLimits limits,
        int openConnections,
        int bytesAlreadyThisSecond,
        int storedBytes,
        int memoryBytes,
        Func<T, int>? replacedBytes = null)
    {
        var cap = limits.Clamp();
        if (openConnections > cap.MaxConnections)
            return Array.Empty<T>();

        var taken = new List<T>();
        long rate = Math.Max(0, bytesAlreadyThisSecond);
        long stored = Math.Max(0, storedBytes);
        long memory = Math.Max(0, memoryBytes);
        foreach (var item in incoming)
        {
            if (taken.Count >= cap.MaxItemsPerTick)
                break;
            var size = Math.Max(1, bytesOf(item));
            var growth = size - Math.Max(0, replacedBytes?.Invoke(item) ?? 0);
            if (rate + size > cap.BytesPerSecond || stored + growth > cap.MaxStoredBytes || memory + growth > cap.MaxMemoryBytes)
                continue;
            taken.Add(item);
            rate += size;
            stored += growth;
            memory += growth;
        }

        return taken;
    }

    public static IReadOnlyList<SyncAnnouncement> SelectUploads(
        IEnumerable<SyncAnnouncement> outbound,
        SyncLimits limits,
        IReadOnlyDictionary<string, int>? alreadySent = null)
    {
        var left = limits.Clamp().UploadBytesPerSecond;
        var seen = alreadySent is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(alreadySent, StringComparer.Ordinal);
        var chosen = new List<SyncAnnouncement>();
        foreach (var item in outbound)
        {
            if (item is null)
                continue;
            var key = SyncMerge.Key(item);
            var revision = item.Revision < 1 ? 1 : item.Revision;
            if (seen.TryGetValue(key, out var sent) && revision <= sent)
                continue;
            var size = Math.Max(1, item.PayloadBytes);
            if (size > left)
                continue;
            chosen.Add(item);
            seen[key] = revision;
            left -= size;
        }

        return chosen;
    }
}
