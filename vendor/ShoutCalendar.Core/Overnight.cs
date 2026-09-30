namespace ShoutCalendar.Core;

public static class Overnight
{
    public static bool ContinuesNextDay(TimeOnly start, TimeOnly end) =>
        end != TimeOnly.MinValue && end <= start;

    public static bool Is(TimeOnly? start, TimeOnly? end) =>
        start is TimeOnly begin && end is TimeOnly stop && ContinuesNextDay(begin, stop);

    public static bool Covers(DateOnly day, DateOnly startDay, TimeOnly? start, TimeOnly? end)
    {
        if (!Is(start, end))
            return day == startDay;
        return day == startDay || day == startDay.AddDays(1);
    }

    public static (DateOnly Start, DateOnly End)? Span(DateOnly startDay, TimeOnly? start, TimeOnly? end)
    {
        if (!Is(start, end) || startDay == DateOnly.MaxValue)
            return null;
        return (startDay, startDay.AddDays(1));
    }

    public readonly record struct Occurrence(CalendarEntry Entry, DateOnly Start, DateOnly End, TimeOnly StartClock, TimeOnly EndClock);

    public static IEnumerable<Occurrence> Spans(CalendarEntry entry, DateOnly from, DateOnly to, TimeZoneInfo? zone = null)
    {
        if (to < from || (entry.Date is null && entry.StartUtc is null))
            yield break;
        zone ??= TimeZoneInfo.Local;
        if (entry.Repeat is null)
        {
            if (InRange(entry, from, to, zone) is Occurrence single)
                yield return single;
            yield break;
        }

        var first = from == DateOnly.MinValue ? from : from.AddDays(-1);
        for (var number = first.DayNumber; number <= to.DayNumber; number++)
        {
            var occurrence = SyncClock.OnDate(entry, DateOnly.FromDayNumber(number), zone);
            if (occurrence is not null && InRange(occurrence, from, to, zone) is Occurrence span)
                yield return span;
        }
    }

    private static Occurrence? InRange(CalendarEntry entry, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var (day, start, end) = Shown(entry, zone);
        if (start is not TimeOnly begin || end is not TimeOnly stop || Span(day, begin, stop) is not { } span
            || span.End < from || span.Start > to)
            return null;
        return new Occurrence(entry, span.Start, span.End, begin, stop);
    }

    public static (DateOnly Start, TimeOnly? StartClock, TimeOnly? EndClock) Shown(CalendarEntry entry, TimeZoneInfo? zone)
    {
        var shown = ZoneClock.ShownRange(entry, zone);
        var startClock = shown.Start ?? entry.Time;
        var endClock = shown.End ?? entry.End;
        return (shown.Date, startClock, endClock);
    }
}
