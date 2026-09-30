namespace ShoutCalendar.Core;

public sealed record EventRepeat(string Kind, DayOfWeek Weekday, int Mask)
{
    public const string Weekly = "weekly";
    public const string Biweekly = "biweekly";
    public const string Month = "month";

    public string Label => this.Kind switch
    {
        Biweekly => $"every other {this.Weekday}",
        Month => "every " + this.NthLabel() + " " + this.Weekday,
        _ => $"every {this.Weekday}",
    };

    public static bool FallsOn(CalendarEntry entry, DateOnly day)
    {
        if (entry.SeriesDeleted || entry.ExcludedDates?.Contains(day) == true || (entry.RepeatUntil is DateOnly until && day >= until))
            return false;
        if (entry.Repeat is null)
            return entry.Date == day;
        if (entry.Date is not DateOnly anchor || day < anchor)
            return false;
        return entry.Repeat.Includes(anchor, day);
    }

    public DateOnly FirstOnOrAfter(DateOnly start)
    {
        for (var i = 0; i < 370; i++)
        {
            var day = start.AddDays(i);
            if (this.Matches(day))
                return day;
        }

        return start;
    }

    public bool Includes(DateOnly anchor, DateOnly day)
    {
        if (!this.Matches(day))
            return false;
        if (this.Kind == Month)
            return true;
        var span = day.DayNumber - anchor.DayNumber;
        var step = this.Kind == Biweekly ? 14 : 7;
        return span % step == 0;
    }

    public string Store()
    {
        if (this.Kind == Month)
            return $"{Month}:{(int)this.Weekday}:{this.Mask}";
        return $"{this.Kind}:{(int)this.Weekday}";
    }

    public static EventRepeat? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var parts = text.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var weekday) || weekday is < 0 or > 6)
            return null;
        var day = (DayOfWeek)weekday;
        if (parts[0] == Month && parts.Length >= 3 && int.TryParse(parts[2], out var mask))
            return new EventRepeat(Month, day, mask);
        if (parts[0] is Weekly or Biweekly)
            return new EventRepeat(parts[0], day, 0);
        return null;
    }

    private bool Matches(DateOnly day)
    {
        if (day.DayOfWeek != this.Weekday)
            return false;
        if (this.Kind != Month)
            return true;
        var nth = ((day.Day - 1) / 7) + 1;
        var last = day.AddDays(7).Month != day.Month;
        if (last && (this.Mask & 16) != 0)
            return true;
        if (nth == 5 && (this.Mask & 32) != 0)
            return true;
        return nth is >= 1 and <= 4 && (this.Mask & (1 << (nth - 1))) != 0;
    }

    private string NthLabel()
    {
        var names = new List<string>();
        if ((this.Mask & 1) != 0) names.Add("1st");
        if ((this.Mask & 2) != 0) names.Add("2nd");
        if ((this.Mask & 4) != 0) names.Add("3rd");
        if ((this.Mask & 8) != 0) names.Add("4th");
        if ((this.Mask & 16) != 0) names.Add("last");
        if ((this.Mask & 32) != 0) names.Add("5th");
        return names.Count == 0 ? "matching" : string.Join(" and ", names);
    }
}
