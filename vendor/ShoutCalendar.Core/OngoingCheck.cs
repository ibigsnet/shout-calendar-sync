namespace ShoutCalendar.Core;

public static class OngoingCheck
{
    public static bool IsOngoing(CalendarEntry entry, DateTimeOffset evaluationInstant)
    {
        if (entry.Time is not TimeOnly start || entry.End is not TimeOnly end || end <= start)
            return false;

        var utc = evaluationInstant.UtcDateTime;
        if (!EventRepeat.FallsOn(entry, DateOnly.FromDateTime(utc)))
            return false;

        var clock = new TimeOnly(utc.Hour, utc.Minute);
        return clock >= start && clock <= end;
    }
}
