namespace ShoutCalendar.Core;

public static class EventAlarm
{
    public const int MinSound = 1;

    public const int MaxSound = 16;

    public readonly record struct Hit(string Id, bool Accepted, bool AtStart = false);

    public static int ClampSound(int sound) => sound is >= MinSound and <= MaxSound ? sound : MinSound;

    public static DateTime MinuteOf(DateTime now) =>
        new(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);

    public static IReadOnlyList<Hit> Due(
        IEnumerable<CalendarEntry> entries,
        DateTime now,
        DateTime? previousMinute,
        bool alarmAccepted,
        bool alarmUnaccepted,
        int minutesBefore,
        TimeZoneInfo? zone = null)
    {
        if (previousMinute is null)
            return [];

        var minute = MinuteOf(now);
        if (previousMinute.Value == minute)
            return [];

        if (minutesBefore < 0)
            minutesBefore = 0;

        var hits = new List<Hit>();
        foreach (var stored in entries)
        {
            var eventMoment = minute.AddMinutes(minutesBefore);
            var entry = SyncClock.OnDate(stored, DateOnly.FromDateTime(eventMoment), zone);
            if (entry is null || !Clock(entry, zone, out var day, out var time) || string.IsNullOrEmpty(entry.Id))
                continue;
            if (entry.Accepted)
            {
                if (!alarmAccepted)
                    continue;
            }
            else if (!alarmUnaccepted)
            {
                continue;
            }

            if (eventMoment.Hour != time.Hour || eventMoment.Minute != time.Minute)
                continue;
            hits.Add(new Hit(entry.Id, entry.Accepted));
        }

        return hits;
    }

    public static bool IsStartMinute(CalendarEntry entry, DateTime now, TimeZoneInfo? zone = null)
    {
        entry = SyncClock.OnDate(entry, DateOnly.FromDateTime(now), zone)!;
        if (entry is null || !Clock(entry, zone, out var day, out var time) || string.IsNullOrEmpty(entry.Id))
            return false;
        var minute = MinuteOf(now);
        return minute.Hour == time.Hour && minute.Minute == time.Minute;
    }

    public static bool AlreadyDue(CalendarEntry entry, DateTime now, int minutesBefore, TimeZoneInfo? zone = null)
    {
        if (entry.Repeat is not null)
        {
            entry = SyncClock.OnDate(entry, DateOnly.FromDateTime(now.AddMinutes(Math.Max(0, minutesBefore))), zone)!;
            if (entry is null) return false;
        }
        if (!Clock(entry, zone, out var faced, out var time))
            return false;
        if (minutesBefore < 0)
            minutesBefore = 0;
        var today = DateOnly.FromDateTime(now);
        DateOnly day;
        if (entry.Repeat is not null)
        {
            day = faced;
        }
        else
        {
            day = faced;
        }

        var start = day.ToDateTime(time);
        var grace = Math.Max(minutesBefore, 1);
        return now >= start.AddMinutes(-minutesBefore) && now <= start.AddMinutes(grace);
    }

    private static bool Clock(CalendarEntry entry, TimeZoneInfo? zone, out DateOnly day, out TimeOnly time)
    {
        var face = ZoneClock.Shown(entry, zone);
        day = face.Date;
        if (face.Time is not TimeOnly shown)
        {
            time = default;
            return false;
        }

        time = shown;
        return true;
    }

    private static bool OnDay(CalendarEntry entry, DateOnly faced, DateOnly moment)
    {
        if (entry.Repeat is null)
            return faced == moment;
        return EventRepeat.FallsOn(entry, moment);
    }
}
