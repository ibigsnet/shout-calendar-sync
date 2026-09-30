namespace ShoutCalendar.Core;

public enum ResetTone
{
    Crystal,
    Cactus,
    Event,
}

public enum ScheduleClock
{
    WeeklyTuesday,
    FridayMorning,
    DailyReset,
    GrandCompany,
    Cactpot,
    Campaign,
}

public sealed record ScheduleItem(
    string Id,
    string Name,
    string Chip,
    string Detail,
    ResetTone Tone,
    bool DefaultOn,
    string Group,
    ScheduleClock Clock);

public readonly record struct GuidePin(string Label, string PlaceName, float X, float Y, bool HasMap, string? Quest);

public sealed record ScheduleOccurrence(
    string Id,
    string Name,
    string Chip,
    string Detail,
    DateTime LocalStart,
    DateTime LocalEnd,
    ResetTone Tone)
{
    public string Key => this.Id + ":" + this.LocalStart.ToString("yyyyMMddHHmm");

    public DateOnly StartDate => DateOnly.FromDateTime(this.LocalStart);

    public DateOnly EndDate => DateOnly.FromDateTime(this.LocalEnd);
}

public readonly record struct SpanSegment(int Row, int FirstColumn, int LastColumn);

public static class GameSchedule
{
    public const string Daily = "daily-reset";
    public const string Weekly = "weekly-reset";
    public const string Tails = "wondrous-tails";
    public const string Fashion = "fashion-report";
    public const string FashionJudging = "fashion-judging";
    public const string Deliveries = "custom-deliveries";
    public const string Challenge = "challenge-log";
    public const string Raids = "raid-lockouts";
    public const string Hunts = "elite-hunts";
    public const string Carnivale = "masked-carnivale";
    public const string Squadron = "squadron-priority";
    public const string Doman = "doman-enclave";
    public const string Faux = "faux-hollows";
    public const string Pvp = "pvp-weekly";
    public const string Island = "island-sanctuary";
    public const string GrandCompany = "grand-company";
    public const string Cactpot = "jumbo-cactpot";
    public const string Nocturne = "nocturne-2026";

    public const string GroupWeekly = "weekly";
    public const string GroupDaily = "daily";
    public const string GroupGrand = "grand-company";
    public const string GroupCactpot = "cactpot";
    public const string GroupEvent = "event";

    public const string RegionNa = "na";
    public const string RegionEu = "eu";
    public const string RegionJp = "jp";
    public const string RegionOc = "oc";

    private static readonly DateTimeOffset NocturneStart = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NocturneEnd = new(2026, 10, 13, 14, 59, 0, TimeSpan.Zero);

    public static IReadOnlyList<ScheduleItem> Items { get; } =
    [
        Item(Cactpot, "Jumbo Cactpot", "Cactpot", "Weekly Gold Saucer drawing. Up to three tickets. The early bird bonus lasts one hour after the draw. The clock follows the data-center region.", ResetTone.Cactus, true, GroupCactpot, ScheduleClock.Cactpot),
        Item(Weekly, "Weekly reset", "Weekly", "Tuesday 08:00 UTC. Tomestone cap, plus the weekly rows in this list.", ResetTone.Crystal, true, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Tails, "Wondrous Tails", "Tails", "Next journal from Khloe Aliapoh in Idyllshire (5.7, 6.1). Same Tuesday 08:00 UTC as the weekly reset. A book can be held for two weeks from the Tuesday it was issued. That reset is the earliest a new book is available, after the previous one is turned in or has expired.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Fashion, "Fashion Report theme", "Fashion", "New theme and hints from the Masked Rose at the Tuesday weekly reset. Judging opens on Friday.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(FashionJudging, "Fashion Report judging", "Judge", "Judging opens Friday 08:00 UTC. Turn the report in before the next weekly reset.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.FridayMorning),
        Item(Deliveries, "Custom deliveries", "Deliveries", "Twelve turn-ins per week, six per client. Tuesday 08:00 UTC.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Challenge, "Challenge log", "Challenge", "Tuesday 08:00 UTC.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Raids, "Raid lockouts", "Raids", "Savage, alliance, and normal raid reward limits. Tuesday 08:00 UTC.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Hunts, "Elite hunt marks", "Hunts", "Weekly elite marks. Tuesday 08:00 UTC.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Carnivale, "Masked Carnivale", "Carnivale", "Weekly targets. Tuesday 08:00 UTC.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Squadron, "Squadron priority", "Squadron", "Adventurer squadron priority mission. Tuesday 08:00 UTC.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Doman, "Doman enclave", "Doman", "Reconstruction donation. Tuesday 08:00 UTC.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Faux, "Faux Hollows", "Faux", "Tuesday 08:00 UTC.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Pvp, "PvP weekly", "PvP", "Weekly PvP performance. Tuesday 08:00 UTC.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Island, "Island Sanctuary", "Island", "Tuesday 08:00 UTC.", ResetTone.Crystal, false, GroupWeekly, ScheduleClock.WeeklyTuesday),
        Item(Daily, "Daily reset", "Daily", "15:00 UTC. Duty roulette bonuses, allied society quests, and daily hunt bills.", ResetTone.Crystal, false, GroupDaily, ScheduleClock.DailyReset),
        Item(GrandCompany, "Grand Company", "GC", "20:00 UTC. Supply and provisioning, Rowena collectables, and squadron training.", ResetTone.Crystal, false, GroupGrand, ScheduleClock.GrandCompany),
        Item(Nocturne, "A Nocturne for Heroes", "Nocturne", "Final Fantasy XV return. The Regalia Type-G, the four-seat black car, is 200,000 MGP from the Ironworks Vendor in the Gold Saucer after the quests. Kipih Jakkya starts it in Ul'dah, Steps of Nald (8.5, 9.7). Level 50 and the quest The Ultimate Weapon. 24 Sep 2026 08:00 UTC through 13 Oct 2026 14:59 UTC.", ResetTone.Event, true, GroupEvent, ScheduleClock.Campaign),
    ];

    private static ScheduleItem Item(
        string id,
        string name,
        string chip,
        string detail,
        ResetTone tone,
        bool defaultOn,
        string group,
        ScheduleClock clock) => new(id, name, chip, detail, tone, defaultOn, group, clock);

    public static IEnumerable<string> MergeSaved(IEnumerable<string>? saved)
    {
        if (saved is null)
            return DefaultIds;
        return saved.Where(IsKnown);
    }

    public static IReadOnlyList<string> DefaultIds { get; } = Items.Where(item => item.DefaultOn).Select(item => item.Id).ToArray();

    private static readonly GuidePin[] NocturnePins =
    [
        new("Kipih Jakkya", "Ul'dah - Steps of Nald", 8.5f, 9.7f, true, "The Man in Black"),
        new("Ironworks Vendor", "The Gold Saucer", 5.2f, 6.3f, true, null),
        new("In the Dark of Night", "", 0f, 0f, false, "In the Dark of Night"),
        new("Messenger of the Winds", "", 0f, 0f, false, "Messenger of the Winds"),
        new("The Ultimate Weapon", "", 0f, 0f, false, "The Ultimate Weapon"),
    ];

    private static readonly Dictionary<string, GuidePin[]> Pins = new(StringComparer.Ordinal)
    {
        ["Nocturne"] = NocturnePins,
        ["Cactpot"] =
        [
            new("Jumbo Cactpot Broker", "The Gold Saucer", 8.6f, 5.9f, true, "Hitting the Cactpot"),
        ],
        ["Tails"] =
        [
            new("Khloe Aliapoh", "Idyllshire", 5.7f, 6.1f, true, null),
            new("Unctuous Adventurer", "Idyllshire", 7.0f, 5.9f, true, "Keeping Up with the Aliapohs"),
        ],
        ["Fashion"] =
        [
            new("Masked Rose", "The Gold Saucer", 7.2f, 7.4f, true, "Passion for Fashion"),
        ],
        ["Judge"] =
        [
            new("Masked Rose", "The Gold Saucer", 7.2f, 7.4f, true, null),
        ],
        ["Faux"] =
        [
            new("Faux Commander", "Idyllshire", 5.7f, 6.1f, true, "Fantastic Mr. Faux"),
        ],
        ["Carnivale"] =
        [
            new("Maudlin Latool Ja", "Ul'dah - Steps of Thal", 12.5f, 13.0f, true, "The Real Folk Blues"),
            new("Celestium Attendant", "Ul'dah - Steps of Thal", 11.5f, 13.2f, true, null),
        ],
    };

    public static IReadOnlyList<GuidePin> Guide(string? chip) =>
        chip is not null && Pins.TryGetValue(chip, out var pins) ? pins : [];

    public static bool IsKnown(string? id) => Items.Any(item => item.Id == id);

    public static string NormalizeRegion(string? region) => region switch
    {
        RegionEu or RegionJp or RegionOc => region,
        _ => RegionNa,
    };

    public static string RegionLabel(string region) => NormalizeRegion(region) switch
    {
        RegionEu => "Europe (Chaos, Light)",
        RegionJp => "Japan (Elemental, Gaia, Mana, Meteor)",
        RegionOc => "Oceania (Materia)",
        _ => "North America (Crystal, Aether, Primal, Dynamis)",
    };

    public static IReadOnlyList<ScheduleOccurrence> InMonth(
        int year,
        int month,
        TimeZoneInfo zone,
        string? region,
        IReadOnlySet<string> enabled)
    {
        var found = new List<ScheduleOccurrence>();
        var chosen = NormalizeRegion(region);
        foreach (var item in Items)
        {
            if (!enabled.Contains(item.Id))
                continue;
            if (item.Clock == ScheduleClock.Campaign)
            {
                var occurrence = Campaign(item, NocturneStart, NocturneEnd, zone, year, month);
                if (occurrence is not null)
                    found.Add(occurrence);
                continue;
            }

            foreach (var instant in Instants(item.Clock, chosen, year, month))
            {
                var local = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
                if (local.Year != year || local.Month != month)
                    continue;
                found.Add(new ScheduleOccurrence(item.Id, item.Name, item.Chip, item.Detail, local, local, item.Tone));
            }
        }

        return found;
    }

    public static string NextLine(string id, DateTimeOffset now, TimeZoneInfo zone, string? region)
    {
        var item = Items.First(candidate => candidate.Id == id);
        if (item.Clock == ScheduleClock.Campaign)
        {
            var start = TimeZoneInfo.ConvertTime(NocturneStart, zone);
            var end = TimeZoneInfo.ConvertTime(NocturneEnd, zone);
            return $"{start:ddd d MMM HH:mm} – {end:ddd d MMM HH:mm}";
        }

        var following = now.UtcDateTime.AddMonths(1);
        var upcoming = Instants(item.Clock, NormalizeRegion(region), now.Year, now.Month)
            .Concat(Instants(item.Clock, NormalizeRegion(region), following.Year, following.Month))
            .Where(instant => instant >= now.AddMinutes(-1))
            .Order()
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();
        if (upcoming is null)
            return item.Detail;
        var local = TimeZoneInfo.ConvertTime(upcoming.Value, zone);
        return $"{local:ddd d MMM HH:mm}";
    }

    public static IReadOnlyList<SpanSegment> Segments(CalendarMonth month, DateOnly start, DateOnly end)
    {
        var segments = new List<SpanSegment>();
        int? row = null;
        var first = 0;
        var last = 0;
        for (var index = 0; index < month.Cells.Count; index++)
        {
            if (month.Cells[index].Date is not DateOnly date)
                continue;
            if (date < start || date > end)
                continue;
            var column = index % 7;
            var thisRow = index / 7;
            if (row is null || thisRow != row || column != last + 1)
            {
                if (row is int open)
                    segments.Add(new SpanSegment(open, first, last));
                row = thisRow;
                first = column;
                last = column;
            }
            else
            {
                last = column;
            }
        }

        if (row is int close)
            segments.Add(new SpanSegment(close, first, last));
        return segments;
    }

    public static SpanSegment? WeekSegment(DateOnly weekStart, DateOnly start, DateOnly end)
    {
        var weekEnd = weekStart.AddDays(6);
        var from = start < weekStart ? weekStart : start;
        var to = end > weekEnd ? weekEnd : end;
        if (to < from)
            return null;
        return new SpanSegment(0, from.DayNumber - weekStart.DayNumber, to.DayNumber - weekStart.DayNumber);
    }

    public static IReadOnlyDictionary<string, int> Lanes(IReadOnlyList<ScheduleOccurrence> items) =>
        Lanes(items.Select(item => (item.Key, item.StartDate, item.EndDate)));

    public static IReadOnlyDictionary<string, int> Lanes(IEnumerable<(string Key, DateOnly Start, DateOnly End)> items)
    {
        var lanes = new Dictionary<string, int>();
        var ends = new List<DateOnly>();
        foreach (var item in items.OrderBy(item => item.Start).ThenBy(item => item.End).ThenBy(item => item.Key))
        {
            var lane = ends.FindIndex(end => end < item.Start);
            if (lane < 0)
            {
                lane = ends.Count;
                ends.Add(item.End);
            }
            else
            {
                ends[lane] = item.End;
            }

            lanes[item.Key] = lane;
        }

        return lanes;
    }

    public static IReadOnlyDictionary<DateOnly, int> LaneSlotsByDay(
        IReadOnlyDictionary<string, int> lanes,
        IEnumerable<(string Key, DateOnly Start, DateOnly End)> items)
    {
        var slots = new Dictionary<DateOnly, int>();
        foreach (var item in items)
        {
            if (!lanes.TryGetValue(item.Key, out var lane))
                continue;
            var need = lane + 1;
            for (var day = item.Start; day <= item.End; day = day.AddDays(1))
            {
                if (!slots.TryGetValue(day, out var have) || need > have)
                    slots[day] = need;
            }
        }

        return slots;
    }

    private static ScheduleOccurrence? Campaign(
        ScheduleItem item,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        TimeZoneInfo zone,
        int year,
        int month)
    {
        var start = TimeZoneInfo.ConvertTime(startUtc, zone).DateTime;
        var end = TimeZoneInfo.ConvertTime(endUtc, zone).DateTime;
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        if (DateOnly.FromDateTime(end) < monthStart || DateOnly.FromDateTime(start) > monthEnd)
            return null;
        return new ScheduleOccurrence(item.Id, item.Name, item.Chip, item.Detail, start, end, item.Tone);
    }

    public static IReadOnlyList<ScheduleOccurrence> Due(
        DateTime now,
        DateTime? previousMinute,
        TimeZoneInfo zone,
        string? region,
        IReadOnlySet<string> enabled,
        int minutesBefore)
    {
        if (previousMinute is null)
            return [];
        var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);
        if (previousMinute.Value == minute)
            return [];
        if (minutesBefore < 0)
            minutesBefore = 0;

        var marks = new List<ScheduleOccurrence>();
        var cursor = new DateOnly(now.Year, now.Month, 1).AddMonths(-1);
        for (var i = 0; i < 3; i++)
        {
            marks.AddRange(InMonth(cursor.Year, cursor.Month, zone, region, enabled));
            cursor = cursor.AddMonths(1);
        }

        var hits = new List<ScheduleOccurrence>();
        var seen = new HashSet<string>();
        foreach (var mark in marks)
        {
            if (!seen.Add(mark.Key + ":" + minute.ToString("yyyyMMddHHmm")))
                continue;
            var start = Minute(mark.LocalStart.AddMinutes(-minutesBefore), now.Kind);
            var end = Minute(mark.LocalEnd.AddMinutes(-minutesBefore), now.Kind);
            var point = mark.StartDate == mark.EndDate;
            if (start == minute || (!point && end == minute))
                hits.Add(mark);
        }

        return hits;
    }

    private static DateTime Minute(DateTime value, DateTimeKind kind) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, kind);

    private static IEnumerable<DateTimeOffset> Instants(ScheduleClock clock, string region, int year, int month)
    {
        if (clock == ScheduleClock.Campaign)
            yield break;

        var first = new DateOnly(year, month, 1).AddDays(-2);
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month)).AddDays(2);
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (clock == ScheduleClock.DailyReset)
                yield return Utc(date, 15, 0);
            else if (clock == ScheduleClock.GrandCompany)
                yield return Utc(date, 20, 0);
            else if (clock == ScheduleClock.WeeklyTuesday && date.DayOfWeek == DayOfWeek.Tuesday)
                yield return Utc(date, 8, 0);
            else if (clock == ScheduleClock.FridayMorning && date.DayOfWeek == DayOfWeek.Friday)
                yield return Utc(date, 8, 0);
            else if (clock == ScheduleClock.Cactpot && MatchesCactpot(date, region))
                yield return Utc(date, CactpotHour(region), 0);
        }
    }

    private static bool MatchesCactpot(DateOnly utcDate, string region) =>
        utcDate.DayOfWeek == (NormalizeRegion(region) == RegionNa ? DayOfWeek.Sunday : DayOfWeek.Saturday);

    private static int CactpotHour(string region) => NormalizeRegion(region) switch
    {
        RegionEu => 19,
        RegionJp => 12,
        RegionOc => 9,
        _ => 2,
    };

    private static DateTimeOffset Utc(DateOnly date, int hour, int minute) =>
        new(date.Year, date.Month, date.Day, hour, minute, 0, TimeSpan.Zero);
}
