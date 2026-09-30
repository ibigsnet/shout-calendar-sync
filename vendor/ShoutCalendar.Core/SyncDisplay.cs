namespace ShoutCalendar.Core;

public static class SyncDisplay
{
    public static int Stamp(IReadOnlyList<SyncAnnouncement> rows)
    {
        var hash = new HashCode();
        hash.Add(rows.Count);
        foreach (var row in rows)
        {
            hash.Add(row.SeriesDeleted); hash.Add(row.RepeatUntil);
            foreach (var date in row.ExcludedDates) hash.Add(date);
            hash.Add(row.Id); hash.Add(row.Revision); hash.Add(row.World); hash.Add(row.Channel);
            hash.Add(row.Accepted); hash.Add(row.Hidden); hash.Add(row.Declined);
            hash.Add(row.Date); hash.Add(row.Time); hash.Add(row.End);
            hash.Add(row.StartUtc); hash.Add(row.EndUtc); hash.Add(row.SourceTimeZone); hash.Add(row.Repeat);
            hash.Add(row.Text); hash.Add(row.Title); hash.Add(row.Place); hash.Add(row.NoteUpdated); hash.Add(row.ClockEditedLocally);
            hash.Add(row.FromSync); hash.Add(row.HarvestedLocally); hash.Add(row.ShareFormat); hash.Add(row.ObservedAt);
        }
        return hash.ToHashCode();
    }
}
