using System.Globalization;

namespace ShoutCalendar.Core;

public static class PastEvents
{
    public static bool Ended(CalendarEntry entry, DateTime now, TimeZoneInfo? zone = null)
    {
        if (entry.Repeat is not null)
            return false;
        zone ??= TimeZoneInfo.Local;
        var shown = ZoneClock.ShownRange(entry, zone);
        var walls = ZoneClock.Walls(entry.EventText);
        var start = shown.Start ?? entry.Time;
        var end = shown.End ?? entry.End;
        if (end is null && walls.Count >= 2)
            end = walls[1].Time;
        if (end is null)
            return PastTone.Ended(shown.Date, null, null, now);
        return PastTone.Ended(shown.Date, start, end, now);
    }

    public static bool Ended(SyncAnnouncement item, DateTime now, TimeZoneInfo? zone = null)
    {
        return Ended(SyncClock.Entry(item, zone), now, zone);
    }
}
