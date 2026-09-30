namespace ShoutCalendar.Core;

public enum DeleteScope { Occurrence, Following, Series }

public static class EventDeletion
{
    public static CalendarEntry Apply(CalendarEntry entry, DateOnly sourceDay, DeleteScope scope) => scope switch
    {
        DeleteScope.Series => entry with { SeriesDeleted = true },
        DeleteScope.Following => entry with { RepeatUntil = entry.RepeatUntil is DateOnly until && until < sourceDay ? until : sourceDay },
        _ => entry with { ExcludedDates = (entry.ExcludedDates ?? []).Append(sourceDay).Distinct().Order().ToArray() },
    };
}
