using System.Globalization;

namespace ShoutCalendar.Core;

public sealed record MonthCell(int? Day, IReadOnlyList<CalendarEntry> Entries, DateOnly? Date = null);

public sealed class CalendarMonth
{
    private CalendarMonth(int year, int month, IReadOnlyList<CalendarEntry> entries, IReadOnlyList<MonthCell> cells)
    {
        this.Year = year;
        this.Month = month;
        this.Entries = entries;
        this.Cells = cells;
    }

    public int Year { get; }

    public int Month { get; }

    public IReadOnlyList<CalendarEntry> Entries { get; }

    public IReadOnlyList<MonthCell> Cells { get; }

    public string Title => new DateOnly(this.Year, this.Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public static CalendarMonth Create(int year, int month, IReadOnlyList<CalendarEntry> entries)
    {
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month));

        var first = new DateOnly(year, month, 1);
        var lead = (int)first.DayOfWeek;
        var days = DateTime.DaysInMonth(year, month);
        var count = lead + days;
        while (count % 7 != 0)
            count++;
        var gridStart = first.AddDays(-lead);

        var byDate = new Dictionary<DateOnly, List<CalendarEntry>>();
        for (var index = 0; index < count; index++)
        {
            var day = gridStart.AddDays(index);
            foreach (var entry in entries)
            {
                if (!Covers(entry, day))
                    continue;
                if (!byDate.TryGetValue(day, out var list))
                {
                    list = new List<CalendarEntry>();
                    byDate[day] = list;
                }

                list.Add(entry);
            }
        }

        var cells = new List<MonthCell>(count);
        for (var index = 0; index < count; index++)
        {
            var date = gridStart.AddDays(index);
            var inMonth = date.Year == year && date.Month == month;
            IReadOnlyList<CalendarEntry> onDay = byDate.TryGetValue(date, out var list)
                ? list
                : Array.Empty<CalendarEntry>();
            cells.Add(new MonthCell(inMonth ? date.Day : null, onDay, date));
        }

        return new CalendarMonth(year, month, entries, cells);
    }

    public CalendarMonth Page(int monthDelta)
    {
        var shifted = new DateOnly(this.Year, this.Month, 1).AddMonths(monthDelta);
        return Create(shifted.Year, shifted.Month, this.Entries);
    }

    public IReadOnlyList<CalendarEntry> OnDay(int day)
    {
        if (day < 1 || day > DateTime.DaysInMonth(this.Year, this.Month))
            return Array.Empty<CalendarEntry>();

        foreach (var cell in this.Cells)
        {
            if (cell.Day == day)
                return cell.Entries;
        }

        return Array.Empty<CalendarEntry>();
    }

    private static bool Covers(CalendarEntry entry, DateOnly day)
    {
        var occurrence = SyncClock.OnDate(entry, day)
            ?? (day > DateOnly.MinValue ? SyncClock.OnDate(entry, day.AddDays(-1)) : null);
        if (occurrence is null) return false;
        var range = ZoneClock.ShownRange(occurrence, TimeZoneInfo.Local);
        return Overnight.Covers(day, range.Date, range.Start, range.End);
    }
}
