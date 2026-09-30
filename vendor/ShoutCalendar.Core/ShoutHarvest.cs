using System.Globalization;
using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

public static class ShoutHarvest
{
    public const int ShoutChannel = 0x0B;

    public const int FreeCompanyChannel = 24;

    private static readonly Regex WardRegex = new(
        @"\b(?:ward[\s\-–—·•．.]*#?[\s\-–—·•．.]*|(?<![A-Za-z])[Ww])(?<n>30|[12][0-9]|[1-9])(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlotShorthandRegex = new(
        @"(?<![A-Za-z])[Pp](?<n>[1-9]|[1-5][0-9]|60)(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WeekdayRegex = new(
        @"\b(?:the\s+)?(?<after>sunday|monday|tuesday|wednesday|thursday|friday|saturday)\s+after\s+next\b|\b(?:(?<when>next|this)\s+)?(?<weekday>sunday|monday|tuesday|wednesday|thursday|friday|saturday)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DateRegex = new(
        @"\b(?<m>1[0-2]|0?[1-9])/(?<d>3[01]|[12]\d|0?[1-9])/(?<y>\d{4}|\d{2})\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MonthDayRegex = new(
        @"\b(?<mon>jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?\s+(?<d>3[01]|[12]\d|0?[1-9])(?:st|nd|rd|th)?(?:,?\s*(?<y>\d{4}))?\b|\b(?<d2>3[01]|[12]\d|0?[1-9])(?:st|nd|rd|th)?\s+(?:of\s+)?(?<mon2>jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex InMinutesRegex = new(
        @"\bin\s+(?<n>\d{1,3})\s*(?:minutes?|mins?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlaceWordRegex = new(
        @"\b(?<kind>plot|apartment|room|house|cottage)[\s\-–—·•．.]*#?[\s\-–—·•．.]*(?<n>\d{1,3})\b|\b(?<sub>subdivision)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static CalendarEntry? TryHarvest(
        string? text,
        int channel,
        DateTimeOffset shoutTimestamp,
        PlaceCatalog? places = null,
        IReadOnlySet<int>? channels = null,
        string? housingHint = null,
        bool aggressive = false,
        TimeZoneInfo? zone = null)
    {
        if (!IsWatched(channel, channels) || string.IsNullOrWhiteSpace(text))
            return null;

        var source = text.Trim();
        text = IconText.Plain(source);
        var clocks = ReadClocks(text);
        var heardLocal = zone is null
            ? shoutTimestamp.ToLocalTime().DateTime
            : TimeZoneInfo.ConvertTime(shoutTimestamp, zone).DateTime;
        DateOnly? nowDay = null;
        var pinnedSpan = false;
        var nowOnly = false;
        if (ZoneClock.TryNowUntil(text, out var until))
        {
            var start = new TimeOnly(heardLocal.Hour, heardLocal.Minute);
            var untilEnd = until.Time;
            nowDay = DateOnly.FromDateTime(heardLocal);
            if (zone is not null && ZoneClock.Converts(until.Label))
            {
                var untilCivil = SyncClock.EndDate(nowDay, start, untilEnd) ?? nowDay.Value;
                untilEnd = ZoneClock.Move(untilCivil, untilEnd, until.Label, zone).Time;
            }

            clocks = [start, untilEnd];
            pinnedSpan = true;
        }
        else if (clocks.Count == 0 && TryInMinutes(text, out var minutes))
        {
            var departs = heardLocal.AddMinutes(minutes);
            clocks.Add(TimeOnly.FromDateTime(departs));
            nowDay = DateOnly.FromDateTime(departs);
        }
        else if (clocks.Count == 0 && (NowRegex.IsMatch(text) || LiveInvite.IsGathering(text)))
        {
            clocks.Add(new TimeOnly(heardLocal.Hour, heardLocal.Minute));
            nowDay = DateOnly.FromDateTime(heardLocal);
            nowOnly = true;
        }

        var heard = HeardDay(shoutTimestamp, zone);
        var writtenDate = ReadDate(text, heard);
        var statedDate = writtenDate ?? ReadRelativeDay(text, heard) ?? ReadWeekday(text, heard);

        int? ward = null;
        var wardMatch = WardRegex.Match(text);
        if (wardMatch.Success)
            ward = int.Parse(wardMatch.Groups["n"].Value, CultureInfo.InvariantCulture);

        var servers = ServerNames.Match(text);
        var locations = (places ?? PlaceCatalog.Empty).Match(text);
        var coordinates = ReadCoordinates(text);
        var coordinateZone = MapMentions.Read(text).Select(spot => spot.Place).FirstOrDefault(place => !string.IsNullOrEmpty(place)) ?? "";
        var extras = ReadPlaceWords(text);
        if (!extras.Any(extra => extra.StartsWith("plot ", StringComparison.Ordinal)))
        {
            var plot = PlotShorthandRegex.Match(text);
            if (plot.Success)
                extras.Add($"plot {plot.Groups["n"].Value}");
        }

        var placeParts = new List<string>();
        if (ward is int wardNumber)
            placeParts.Add($"ward {wardNumber.ToString(CultureInfo.InvariantCulture)}");
        placeParts.AddRange(extras);
        if (!string.IsNullOrEmpty(coordinateZone) && !placeParts.Any(part => part.Contains(coordinateZone, StringComparison.OrdinalIgnoreCase)))
            placeParts.Add(coordinateZone);
        if (coordinates is not null)
            placeParts.Add(coordinates);
        placeParts.AddRange(locations);
        placeParts.AddRange(servers);
        var district = HousingTravel.DistrictName(text);
        if (district is not null)
        {
            placeParts.RemoveAll(part =>
            {
                var named = HousingTravel.DistrictName(part);
                return named is not null && !named.Equals(district, StringComparison.OrdinalIgnoreCase);
            });
            if (!placeParts.Any(part => part.Contains(district, StringComparison.OrdinalIgnoreCase)))
                placeParts.Add(district);
        }
        else if (ward is not null && housingHint is not null && !PlaceAlreadyNamesDistrict(placeParts))
        {
            placeParts.Add(housingHint);
        }

        var strongDate = writtenDate is not null;
        var strongTime = pinnedSpan || (clocks.Count > 0 && !nowOnly);
        var strongPlace = ward is not null || coordinates is not null || coordinateZone.Length > 0 || district is not null || extras.Count > 0;
        var hasDate = statedDate is not null;
        var hasTime = clocks.Count > 0;
        var hasPlace = placeParts.Count > 0;
        if (aggressive && writtenDate is null)
        {
            var signals = (hasDate ? 1 : 0) + (hasTime ? 1 : 0) + (hasPlace ? 1 : 0);
            var strong = (strongDate ? 1 : 0) + (strongTime ? 1 : 0) + (strongPlace ? 1 : 0);
            if (signals < 2 || strong < 1)
                return null;
            if (nowOnly && !strongPlace)
                return null;
            if (nowOnly && strongPlace && ward is null && extras.Count == 0 && coordinates is null)
                return null;
        }

        if (clocks.Count == 0
            && ward is null
            && extras.Count == 0
            && coordinates is null
            && coordinateZone.Length == 0
            && district is null
            && locations.Count == 0
            && UntilDateRegex.IsMatch(text))
            return null;
        if ((!aggressive || writtenDate is not null) && !hasDate && !hasTime && !hasPlace)
            return null;

        var repeat = ReadRepeat(text, statedDate ?? heard);
        DateOnly? date = statedDate;
        if (repeat is not null)
            date = repeat.FirstOnOrAfter(statedDate ?? heard);
        else if (date is null && nowDay is not null)
            date = nowDay;
        else if (date is null && clocks.Count > 0)
            date = heard;
        if (!pinnedSpan && zone is not null && date is DateOnly civil && clocks.Count > 0)
        {
            var walls = ZoneClock.Walls(text);
            var shifted = new List<TimeOnly>(walls.Count);
            DateOnly? zonedDate = null;
            TimeOnly? firstWall = null;
            foreach (var wall in walls)
            {
                var civilForWall = SyncClock.EndDate(civil, firstWall, wall.Time) ?? civil;
                var moved = ZoneClock.Converts(wall.Label)
                    ? ZoneClock.Move(civilForWall, wall.Time, wall.Label, zone)
                    : (civilForWall, wall.Time);
                if (firstWall is null)
                    zonedDate = moved.Item1;
                firstWall ??= wall.Time;
                shifted.Add(moved.Item2);
            }

            if (shifted.Count > 0)
            {
                clocks = shifted;
                if (zonedDate is DateOnly start && walls.Count > 0 && ZoneClock.Converts(walls[0].Label))
                    date = start;
            }
        }

        TimeOnly? end = clocks.Count == 2 ? clocks[1] : null;
        TimeOnly? time = clocks.Count > 0 ? clocks[0] : null;
        var server = servers.Count == 0 ? null : string.Join(", ", servers);

        return new CalendarEntry(
            date,
            time,
            end,
            ward,
            server,
            string.Join(", ", placeParts),
            source,
            "",
            false,
            "",
            shoutTimestamp,
            repeat,
            channel);
    }

    public static IReadOnlyList<CalendarEntry> HarvestLog(ReadOnlySpan<byte> log, PlaceCatalog? places = null)
    {
        var found = new List<CalendarEntry>();
        foreach (var line in ChatLogReader.Read(log))
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(line.TimestampUnix);
            var entry = TryHarvest(line.Message, line.Channel, when, places);
            if (entry is not null)
                found.Add(entry);
        }

        return found;
    }

    public static bool IsSharedEvent(string? text, int channel, DateTimeOffset when, PlaceCatalog? places = null)
    {
        if (channel is not SharePolicy.ShoutChannel and not SharePolicy.YellChannel)
            return false;
        return TryHarvest(text, channel, when, places, aggressive: true) is not null;
    }

    public static bool IsWatched(int channel, IReadOnlySet<int>? channels = null) => ChatChannels.Allows(channel, channels);

    private static readonly Regex UntilDateRegex = new(
        @"\b(?:until|till|through)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NowRegex = new(
        @"\b(?:right\s+now|now)\b|\bright\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RelativeDayRegex = new(
        @"\b(?<day>tomorrow|tonight|today)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static DateOnly HeardDay(DateTimeOffset shoutTimestamp, TimeZoneInfo? zone)
    {
        if (zone is null)
            return DateOnly.FromDateTime(shoutTimestamp.UtcDateTime);
        try
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(shoutTimestamp, zone).DateTime);
        }
        catch (ArgumentException)
        {
            return DateOnly.FromDateTime(shoutTimestamp.UtcDateTime);
        }
    }

    private static DateOnly? ReadRelativeDay(string text, DateOnly heard)
    {
        var match = RelativeDayRegex.Match(text);
        if (!match.Success)
            return null;
        return match.Groups["day"].Value.Equals("tomorrow", StringComparison.OrdinalIgnoreCase)
            ? heard.AddDays(1)
            : heard;
    }

    private static readonly Regex EveryOtherRegex = new(
        @"\bevery\s+other\s+(?:week|(?<weekday>sunday|monday|tuesday|wednesday|thursday|friday|saturday))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EveryNthRegex = new(
        @"\bevery\s+(?<nths>(?:(?:first|1st|second|2nd|third|3rd|fourth|4th|fifth|5th|last)\b\s*(?:and|,)?\s*)+)(?<weekday>sunday|monday|tuesday|wednesday|thursday|friday|saturday)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EveryWeekRegex = new(
        @"\bevery\s+(?<weekday>sunday|monday|tuesday|wednesday|thursday|friday|saturday)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static EventRepeat? ReadRepeat(string text, DateOnly anchor)
    {
        var other = EveryOtherRegex.Match(text);
        if (other.Success)
        {
            var weekday = other.Groups["weekday"].Success
                ? Weekday(other.Groups["weekday"].Value)
                : anchor.DayOfWeek;
            return weekday is DayOfWeek day ? new EventRepeat(EventRepeat.Biweekly, day, 0) : null;
        }

        var nth = EveryNthRegex.Match(text);
        if (nth.Success && Weekday(nth.Groups["weekday"].Value) is DayOfWeek nthDay)
        {
            var mask = 0;
            var words = nth.Groups["nths"].Value;
            if (words.Contains("1st", StringComparison.OrdinalIgnoreCase) || words.Contains("first", StringComparison.OrdinalIgnoreCase))
                mask |= 1;
            if (words.Contains("2nd", StringComparison.OrdinalIgnoreCase) || words.Contains("second", StringComparison.OrdinalIgnoreCase))
                mask |= 2;
            if (words.Contains("3rd", StringComparison.OrdinalIgnoreCase) || words.Contains("third", StringComparison.OrdinalIgnoreCase))
                mask |= 4;
            if (words.Contains("4th", StringComparison.OrdinalIgnoreCase) || words.Contains("fourth", StringComparison.OrdinalIgnoreCase))
                mask |= 8;
            if (words.Contains("5th", StringComparison.OrdinalIgnoreCase) || words.Contains("fifth", StringComparison.OrdinalIgnoreCase))
                mask |= 32;
            if (words.Contains("last", StringComparison.OrdinalIgnoreCase))
                mask |= 16;
            if (mask != 0)
                return new EventRepeat(EventRepeat.Month, nthDay, mask);
        }

        var weekly = EveryWeekRegex.Match(text);
        if (weekly.Success && Weekday(weekly.Groups["weekday"].Value) is DayOfWeek weeklyDay)
            return new EventRepeat(EventRepeat.Weekly, weeklyDay, 0);
        return null;
    }

    private static DayOfWeek? Weekday(string text)
    {
        var names = new[]
        {
            "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday",
        };
        var index = Array.FindIndex(names, name => name.Equals(text, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : (DayOfWeek)index;
    }

    private static DateOnly? ReadWeekday(string text, DateOnly today)
    {
        var match = WeekdayRegex.Match(text);
        if (!match.Success)
            return null;

        var names = new[]
        {
            "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday",
        };
        var written = match.Groups["after"].Success ? match.Groups["after"].Value : match.Groups["weekday"].Value;
        var target = Array.FindIndex(names, name => name.Equals(written, StringComparison.OrdinalIgnoreCase));
        if (target < 0)
            return null;

        var delta = (target - (int)today.DayOfWeek + 7) % 7;
        if (match.Groups["after"].Success)
            delta = delta == 0 ? 14 : delta + 7;
        else if (match.Groups["when"].Success
            && match.Groups["when"].Value.Equals("next", StringComparison.OrdinalIgnoreCase)
            && delta == 0)
            delta = 7;
        return today.AddDays(delta);
    }

    private static DateOnly? ReadDate(string text, DateOnly heard)
    {
        var match = DateRegex.Match(text);
        if (match.Success)
        {
            var month = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);
            var year = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
            if (year < 100)
                year += 2000;
            return TryDay(year, month, day);
        }

        var named = MonthDayRegex.Match(text);
        if (!named.Success)
            return null;
        var monthName = named.Groups["mon"].Success ? named.Groups["mon"].Value : named.Groups["mon2"].Value;
        var dayText = named.Groups["d"].Success ? named.Groups["d"].Value : named.Groups["d2"].Value;
        if (!MonthNumber(monthName, out var namedMonth))
            return null;
        var namedDay = int.Parse(dayText, CultureInfo.InvariantCulture);
        if (named.Groups["y"].Success)
            return TryDay(int.Parse(named.Groups["y"].Value, CultureInfo.InvariantCulture), namedMonth, namedDay);
        var sameYear = TryDay(heard.Year, namedMonth, namedDay);
        if (sameYear is DateOnly dayThisYear && dayThisYear < heard)
            return TryDay(heard.Year + 1, namedMonth, namedDay);
        return sameYear;
    }

    private static bool MonthNumber(string name, out int month)
    {
        month = name.ToLowerInvariant() switch
        {
            "jan" or "january" => 1,
            "feb" or "february" => 2,
            "mar" or "march" => 3,
            "apr" or "april" => 4,
            "may" => 5,
            "jun" or "june" => 6,
            "jul" or "july" => 7,
            "aug" or "august" => 8,
            "sep" or "sept" or "september" => 9,
            "oct" or "october" => 10,
            "nov" or "november" => 11,
            "dec" or "december" => 12,
            _ => 0,
        };
        return month != 0;
    }

    private static DateOnly? TryDay(int year, int month, int day)
    {
        if (month is < 1 or > 12 || day is < 1 or > 31)
            return null;
        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool PlaceAlreadyNamesDistrict(List<string> placeParts)
    {
        foreach (var part in placeParts)
        {
            if (part.Contains("Mist", StringComparison.OrdinalIgnoreCase)
                || part.Contains("Lavender", StringComparison.OrdinalIgnoreCase)
                || part.Contains("Goblet", StringComparison.OrdinalIgnoreCase)
                || part.Contains("Shirogane", StringComparison.OrdinalIgnoreCase)
                || part.Contains("Empyreum", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryInMinutes(string text, out int minutes)
    {
        minutes = 0;
        var match = InMinutesRegex.Match(text);
        if (!match.Success)
            return false;
        if (!int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
            return false;
        return minutes is >= 1 and <= 180;
    }

    private static string? ReadCoordinates(string text)
    {
        var spot = MapMentions.Read(text).FirstOrDefault();
        if (spot.X == 0 && spot.Y == 0 && string.IsNullOrEmpty(spot.Place))
            return null;
        return FormattableString.Invariant($"x {spot.X:0.##}, y {spot.Y:0.##}");
    }

    private static List<TimeOnly> ReadClocks(string text)
    {
        var clocks = new List<TimeOnly>();
        foreach (var wall in ZoneClock.Walls(text))
            clocks.Add(wall.Time);
        return clocks;
    }

    private static List<string> ReadPlaceWords(string text)
    {
        var extras = new List<string>();
        foreach (Match match in PlaceWordRegex.Matches(text))
        {
            if (match.Groups["sub"].Success)
            {
                extras.Add("subdivision");
                continue;
            }

            var number = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
            if (number <= 0)
                continue;
            extras.Add($"{match.Groups["kind"].Value.ToLowerInvariant()} {number.ToString(CultureInfo.InvariantCulture)}");
        }

        return extras;
    }
}
