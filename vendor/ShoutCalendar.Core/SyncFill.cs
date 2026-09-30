namespace ShoutCalendar.Core;

public static class SyncFill
{
    public static readonly TimeSpan SlowPass = TimeSpan.FromSeconds(2);

    public static IReadOnlyList<SyncAnnouncement> Prioritize(
        IEnumerable<SyncAnnouncement> incoming,
        DateTime now,
        TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        return incoming
            .OrderBy(item => PastEvents.Ended(item, now, zone) ? 1 : 0)
            .ThenBy(item => item.Date ?? "")
            .ThenBy(item => item.Time ?? "")
            .ThenBy(item => item.Id ?? "", StringComparer.Ordinal)
            .ToList();
    }

    public static SyncFillReport Measure(
        IReadOnlyList<SyncAnnouncement> fetched,
        IReadOnlyList<SyncAnnouncement> admitted,
        DateTime now,
        TimeSpan elapsed,
        TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var fetchedCurrent = fetched.Count(item => !PastEvents.Ended(item, now, zone));
        var fetchedPast = fetched.Count - fetchedCurrent;
        var admittedSet = new HashSet<string>(admitted.Select(SyncMerge.Key), StringComparer.Ordinal);
        var admittedCurrent = admitted.Count(item => !PastEvents.Ended(item, now, zone));
        var admittedPast = admitted.Count - admittedCurrent;
        var droppedCurrent = Math.Max(0, fetchedCurrent - admittedCurrent);
        var deferredPast = Math.Max(0, fetchedPast - admittedPast);
        var droppedByCap = Math.Max(0, fetched.Count - admitted.Count);
        return new SyncFillReport(
            fetched.Count,
            admitted.Count,
            admittedCurrent,
            admittedPast,
            deferredPast,
            droppedByCap,
            droppedCurrent,
            elapsed >= SlowPass);
    }

    public static SyncFillReport FromPass(
        IReadOnlyList<SyncAnnouncement> released,
        int added,
        DateTime now,
        TimeSpan elapsed)
    {
        if (released.Count == 0)
            return new SyncFillReport(0, 0, 0, 0, 0, 0, 0, false);
        IReadOnlyList<SyncAnnouncement> admitted = added >= released.Count
            ? released
            : released.Take(Math.Max(0, added)).ToList();
        return Measure(released, admitted, now, elapsed);
    }

    public static string? StatusTip(SyncFillReport report)
    {
        if (report.DroppedCurrent > 0)
            return $"Limited — {report.DroppedCurrent} current {(report.DroppedCurrent == 1 ? "invite" : "invites")} waiting for a later pass.";
        return null;
    }

    public static string ChatTip() =>
        "Shout Calendar: invites are waiting for a later sync pass. If the count does not fall, check Invites per sync pass and the storage and transfer limits.";
}

public readonly record struct SyncFillReport(
    int Fetched,
    int Admitted,
    int AdmittedCurrent,
    int AdmittedPast,
    int DeferredPast,
    int DroppedByCap,
    int DroppedCurrent,
    bool Slow);
