using System.Globalization;
using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

public static class ZoneClock
{
    public readonly record struct Wall(TimeOnly Time, string? Label);

    public readonly record struct Face(DateOnly Date, TimeOnly? Time);

    public readonly record struct Range(DateOnly Date, TimeOnly? Start, TimeOnly? End);

    private static readonly Regex ClockRegex = new(
        @"\b(?:(?<h24>[01]?\d|2[0-3]):(?<m>[0-5]\d)(?:\s*(?<ampm>[ap](?:\.?m\.?)?))?|(?<h12>[1-9]|1[0-2])\s*(?<ampm2>[ap](?:\.?m\.?)?))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RangeRegex = new(
        @"(?<![A-Za-z0-9])(?<h1>\d{1,2})\s*(?<a1>[ap](?:\.?m\.?)?)?\s*[-–—]\s*(?<h2>\d{1,2})\s*(?<a2>[ap](?:\.?m\.?)?)?(?![A-Za-z0-9:])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ZoneRegex = new(
        @"^\s*[\(\[]?\s*(?<zone>PDT|PST|EDT|EST|CDT|CST|MDT|MST|PT|ET|CT|MT|ST)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BetweenClocks = new(
        @"^\s*(?:to|[-–—])\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NowUntilRegex = new(
        @"\b(?:right\s+now|now)\s*(?:to|till|until|[-–—])\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MidnightRegex = new(
        @"^midnight\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ZoneHourRegex = new(
        @"(?<![A-Za-z0-9:])(?<h>1[0-2]|0?[1-9])\s*(?<zone>PDT|PST|EDT|EST|CDT|CST|MDT|MST|PT|ET|CT|MT)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryTyped(string? text, out TimeOnly start, out TimeOnly? end)
    {
        start = default;
        end = null;
        var walls = Walls(text);
        if (walls.Count == 0)
            return false;
        start = walls[0].Time;
        if (walls.Count > 1)
            end = walls[1].Time;
        return true;
    }

    public static bool TryNowUntil(string? text, out Wall end)
    {
        end = default;
        text = IconText.Plain(text);
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var lead = NowUntilRegex.Match(text);
        if (!lead.Success)
            return false;
        var rest = text[(lead.Index + lead.Length)..];
        if (MidnightRegex.IsMatch(rest))
        {
            var midnight = MidnightRegex.Match(rest);
            end = new Wall(new TimeOnly(0, 0), LabelAfter(rest, midnight));
            return true;
        }

        var clock = ClockRegex.Match(rest);
        if (!clock.Success || clock.Index != 0 || !TryRead(clock, out var time))
            return false;
        end = new Wall(time, LabelAfter(rest, clock));
        return true;
    }

    public static IReadOnlyList<Wall> Walls(string? text)
    {
        var found = new List<(int Index, int End, Wall Wall)>();
        text = IconText.Plain(text);
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var covered = new List<(int Start, int End)>();
        foreach (Match match in RangeRegex.Matches(text))
        {
            if (!TryRange(match, out var start, out var end))
                continue;
            var label = LabelAfter(text, match);
            var endAt = match.Index + match.Length;
            found.Add((match.Index, endAt, new Wall(start, label)));
            found.Add((match.Index + 1, endAt, new Wall(end, label)));
            covered.Add((match.Index, endAt));
        }

        foreach (Match match in ClockRegex.Matches(text))
        {
            if (covered.Any(span => match.Index >= span.Start && match.Index < span.End))
                continue;
            if (!TryRead(match, out var time))
                continue;
            found.Add((match.Index, match.Index + match.Length, new Wall(time, LabelAfter(text, match))));
        }

        foreach (Match match in ZoneHourRegex.Matches(text))
        {
            var startAt = match.Index;
            var endAt = match.Index + match.Length;
            if (covered.Any(span => startAt < span.End && endAt > span.Start))
                continue;
            if (found.Any(item => startAt < item.End && endAt > item.Index))
                continue;
            if (!int.TryParse(match.Groups["h"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hour))
                continue;
            if (!ToHour(hour, "p", out var time))
                continue;
            found.Add((startAt, endAt, new Wall(time, match.Groups["zone"].Value)));
        }

        found.Sort((left, right) => left.Index.CompareTo(right.Index));
        for (var i = 0; i < found.Count - 1; i++)
        {
            if (Converts(found[i].Wall.Label) || !Converts(found[i + 1].Wall.Label))
                continue;
            var between = text[found[i].End..found[i + 1].Index];
            if (!BetweenClocks.IsMatch(between))
                continue;
            found[i] = (found[i].Index, found[i].End, found[i].Wall with { Label = found[i + 1].Wall.Label });
        }

        return found.Select(item => item.Wall).ToArray();
    }

    public static bool Converts(string? label) => SourceId(label) is not null;

    public static bool Labeled(string? text)
    {
        foreach (var wall in Walls(text))
        {
            if (Converts(wall.Label))
                return true;
        }

        return false;
    }

    public static (DateOnly Date, TimeOnly Time) Move(DateOnly civilDate, TimeOnly wall, string? label, TimeZoneInfo calendarZone)
    {
        var sourceId = SourceId(label);
        if (sourceId is null)
            return (civilDate, wall);
        TimeZoneInfo source;
        try
        {
            source = TimeZoneInfo.FindSystemTimeZoneById(sourceId);
        }
        catch (TimeZoneNotFoundException)
        {
            return (civilDate, wall);
        }

        var unspecified = DateTime.SpecifyKind(civilDate.ToDateTime(wall), DateTimeKind.Unspecified);
        if (source.IsInvalidTime(unspecified))
            return (civilDate, wall);
        DateTime utc;
        try
        {
            utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, source);
        }
        catch (ArgumentException)
        {
            return (civilDate, wall);
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, calendarZone);
        return (DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    public static Face Shown(CalendarEntry entry, TimeZoneInfo? calendarZone)
    {
        if (entry.StartUtc is not null || entry.SourceTimeZone.Length > 0)
        {
            var range = SyncClock.Range(entry, calendarZone ?? TimeZoneInfo.Local);
            return new Face(range.Date, range.Start);
        }
        var storedDate = entry.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (entry.NoteUpdated || calendarZone is null || string.IsNullOrWhiteSpace(entry.EventText))
            return new Face(storedDate, entry.Time);
        foreach (var wall in Walls(entry.EventText))
        {
            if (!Converts(wall.Label))
                continue;
            var moved = Move(storedDate, wall.Time, wall.Label, calendarZone);
            if (AlreadyShown(entry, storedDate, moved))
                return new Face(storedDate, entry.Time ?? moved.Time);
            foreach (var shift in new[] { -1, 1 })
            {
                if (shift < 0 && storedDate == DateOnly.MinValue)
                    continue;
                if (shift > 0 && storedDate == DateOnly.MaxValue)
                    continue;
                var civil = storedDate.AddDays(shift);
                var other = Move(civil, wall.Time, wall.Label, calendarZone);
                if (AlreadyShown(entry, storedDate, other))
                    return new Face(storedDate, entry.Time ?? other.Time);
            }

            return new Face(moved.Date, moved.Time);
        }

        return new Face(storedDate, entry.Time);
    }

    public static Range ShownRange(CalendarEntry entry, TimeZoneInfo? calendarZone)
    {
        if (entry.StartUtc is not null || entry.SourceTimeZone.Length > 0)
            return SyncClock.Range(entry, calendarZone ?? TimeZoneInfo.Local);
        var storedDate = entry.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (entry.NoteUpdated) return new Range(storedDate, entry.Time, entry.End);
        if (TryNowUntil(entry.EventText, out var until))
        {
            var untilEnd = entry.End ?? until.Time;
            if (calendarZone is not null && Converts(until.Label) && entry.Time is TimeOnly begin)
            {
                var untilCivil = SyncClock.EndDate(storedDate, begin, until.Time) ?? storedDate;
                untilEnd = Move(untilCivil, until.Time, until.Label, calendarZone).Time;
            }

            return new Range(storedDate, entry.Time, untilEnd);
        }

        if (entry.NoteUpdated || calendarZone is null || string.IsNullOrWhiteSpace(entry.EventText))
            return new Range(storedDate, entry.Time, entry.End);
        var walls = Walls(entry.EventText);
        if (walls.Count == 0 || !Converts(walls[0].Label))
            return new Range(storedDate, entry.Time, entry.End);

        var civil = CivilDate(storedDate, walls[0], entry.Time, calendarZone);
        var start = Move(civil, walls[0].Time, walls[0].Label, calendarZone);
        TimeOnly? end = entry.End;
        if (walls.Count > 1 && Converts(walls[1].Label))
        {
            var endCivil = SyncClock.EndDate(civil, walls[0].Time, walls[1].Time) ?? civil;
            end = Move(endCivil, walls[1].Time, walls[1].Label, calendarZone).Time;
        }

        return new Range(start.Date, start.Time, end);
    }

    public static DateOnly CivilDate(DateOnly stored, Wall wall, TimeOnly? storedTime, TimeZoneInfo zone)
    {
        if (storedTime is not TimeOnly time || time == wall.Time)
            return stored;
        var same = Move(stored, wall.Time, wall.Label, zone);
        if (same.Date == stored && same.Time == time)
            return stored;
        foreach (var shift in new[] { -1, 1 })
        {
            if (shift < 0 && stored == DateOnly.MinValue)
                continue;
            if (shift > 0 && stored == DateOnly.MaxValue)
                continue;
            var civil = stored.AddDays(shift);
            var moved = Move(civil, wall.Time, wall.Label, zone);
            if (moved.Date == stored && moved.Time == time)
                return civil;
        }

        return stored;
    }

    private static bool AlreadyShown(CalendarEntry entry, DateOnly storedDate, (DateOnly Date, TimeOnly Time) moved) =>
        entry.Time is TimeOnly stored && moved.Date == storedDate && moved.Time == stored;

    private static string? LabelAfter(string text, Match clock)
    {
        var rest = text[(clock.Index + clock.Length)..];
        var zone = ZoneRegex.Match(rest);
        return zone.Success ? zone.Groups["zone"].Value : null;
    }

    public static string? SourceId(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;
        return label.Trim().ToUpperInvariant() switch
        {
            "PT" or "PDT" or "PST" => "America/Los_Angeles",
            "ET" or "EDT" or "EST" => "America/New_York",
            "CT" or "CDT" or "CST" => "America/Chicago",
            "MT" or "MDT" or "MST" => "America/Denver",
            _ => null,
        };
    }

    private static bool TryRange(Match match, out TimeOnly start, out TimeOnly end)
    {
        start = default;
        end = default;
        if (!int.TryParse(match.Groups["h1"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var h1))
            return false;
        if (!int.TryParse(match.Groups["h2"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var h2))
            return false;
        if (h1 > 23 || h2 > 23)
            return false;
        var startHalf = Half(match.Groups["a1"].Value);
        var endHalf = Half(match.Groups["a2"].Value);
        if (h1 > 12 || h2 > 12)
        {
            if (startHalf is not null || endHalf is not null)
                return false;
            start = new TimeOnly(h1, 0);
            end = new TimeOnly(h2, 0);
            return true;
        }

        if (h1 == 0 || h2 == 0)
            return false;
        startHalf ??= "p";
        if (endHalf is null)
            endHalf = h2 == 12 || h2 < h1 ? (startHalf == "p" ? "a" : "p") : startHalf;
        return ToHour(h1, startHalf, out start) && ToHour(h2, endHalf, out end);
    }

    private static string? Half(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return raw.Trim().StartsWith('a') || raw.Trim().StartsWith('A') ? "a" : "p";
    }

    private static bool ToHour(int hour, string half, out TimeOnly time)
    {
        time = default;
        if (hour is < 1 or > 12)
            return false;
        var clock = half == "a"
            ? hour == 12 ? 0 : hour
            : hour == 12 ? 12 : hour + 12;
        time = new TimeOnly(clock, 0);
        return true;
    }

    private static bool TryRead(Match match, out TimeOnly time)
    {
        time = default;
        int hour;
        int minute;
        string? ampm;
        if (match.Groups["h24"].Success)
        {
            hour = int.Parse(match.Groups["h24"].Value, CultureInfo.InvariantCulture);
            minute = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
            ampm = match.Groups["ampm"].Success ? match.Groups["ampm"].Value : null;
        }
        else
        {
            hour = int.Parse(match.Groups["h12"].Value, CultureInfo.InvariantCulture);
            minute = 0;
            ampm = match.Groups["ampm2"].Value;
        }

        if (ampm is not null)
        {
            var marker = ampm.Replace(".", "", StringComparison.Ordinal).ToLowerInvariant();
            if (hour is < 1 or > 12)
                return false;
            if (marker is "a" or "am")
                hour = hour == 12 ? 0 : hour;
            else if (marker is "p" or "pm")
                hour = hour == 12 ? 12 : hour + 12;
            else
                return false;
        }

        time = new TimeOnly(hour, minute);
        return true;
    }
}
