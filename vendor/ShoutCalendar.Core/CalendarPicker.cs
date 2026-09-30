namespace ShoutCalendar.Core;

public static class CalendarPicker
{
    public static IReadOnlyList<int> Years(int currentYear, int displayedYear, int radius = 50)
    {
        if (radius < 1)
            radius = 1;
        var years = new List<int>(radius * 2 + 2);
        for (var year = currentYear - radius; year <= currentYear + radius; year++)
            years.Add(year);
        if (displayedYear < years[0] || displayedYear > years[^1])
            years.Add(displayedYear);
        years.Sort();
        return years;
    }

    public static int ScrollIndex(IReadOnlyList<int> years, int currentYear)
    {
        for (var index = 0; index < years.Count; index++)
        {
            if (years[index] == currentYear)
                return index;
        }

        return 0;
    }
}
