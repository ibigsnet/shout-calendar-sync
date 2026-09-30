using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

public static class EventIdentity
{
    private static readonly Regex ClockRegex = new(
        @"\b(?:[01]?\d|2[0-3]):[0-5]\d\b|\b(?:[1-9]|1[0-2])(?::[0-5]\d)?\s*[ap]\.?m\.?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WardRegex = new(
        @"\b(?:ward\s*|[Ww])(\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlotRegex = new(
        @"\b(?:plot\s*|[Pp])(\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "tonight", "today", "tomorrow", "crystal", "aether", "primal", "dynamis", "chaos",
        "light", "elemental", "gaia", "mana", "meteor", "materia", "goblet", "mist",
        "shirogane", "empyreum", "lavender", "beds", "ward", "plot", "with", "from", "that",
        "this", "your", "have", "been", "will", "come", "join",
    };

    public static bool SameShout(string? world, string? text, string? otherWorld, string? otherText)
    {
        if (!string.IsNullOrWhiteSpace(world)
            && !string.IsNullOrWhiteSpace(otherWorld)
            && !world.Trim().Equals(otherWorld.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var leftWard = Number(WardRegex, IconText.Plain(text));
        var rightWard = Number(WardRegex, IconText.Plain(otherText));
        if (leftWard is null || rightWard is null || leftWard != rightWard)
            return false;
        var leftPlot = Number(PlotRegex, IconText.Plain(text));
        var rightPlot = Number(PlotRegex, IconText.Plain(otherText));
        if (leftPlot is null || rightPlot is null || leftPlot != rightPlot)
            return false;
        return Overlap(text, otherText);
    }

    public static bool SameEntry(CalendarEntry left, CalendarEntry right)
    {
        if (left.Manual || right.Manual || !Compatible(left, right)) return false;
        var leftWorld = ShareWorld.Choose("", left.SpeakerWorld, left.Server);
        var rightWorld = ShareWorld.Choose("", right.SpeakerWorld, right.Server);
        var leftText = $"{left.EventText} {left.Place}";
        var rightText = $"{right.EventText} {right.Place}";
        return SameShout(leftWorld, leftText, rightWorld, rightText);
    }

    public static bool SameRepost(CalendarEntry left, CalendarEntry right)
    {
        if (left.Manual || right.Manual)
            return false;
        return SameSeries(SyncAnnouncement.FromLocal(left, left.SpeakerWorld), SyncAnnouncement.FromLocal(right, right.SpeakerWorld))
            || SameFacts(FactsOf(left), FactsOf(right));
    }

    public static bool SameRepost(SyncAnnouncement left, SyncAnnouncement right) =>
        SameSeries(left, right) || SameFacts(FactsOf(left), FactsOf(right));

    public static bool SameSeries(SyncAnnouncement left, SyncAnnouncement right)
    {
        var repeat = RepeatOf(left);
        if (repeat is null || repeat != RepeatOf(right) || SourceZone(left) != SourceZone(right)) return false;
        if (!DateOnly.TryParse(left.Date, out var a) || !DateOnly.TryParse(right.Date, out var b)) return false;
        if (!repeat.Includes(a < b ? a : b, a < b ? b : a)) return false;
        return SameFacts(FactsOf(left) with { Day = null }, FactsOf(right) with { Day = null });
    }

    public static EventRepeat? RepeatOf(SyncAnnouncement item) => EventRepeat.Parse(item.Repeat)
        ?? (item.ShareFormat < 2 ? SyncClock.Entry(item, TimeZoneInfo.Utc).Repeat : null);

    private static string SourceZone(SyncAnnouncement item) => item.SourceTimeZone.Length > 0 ? item.SourceTimeZone
        : ZoneClock.Walls(item.Text).Select(wall => ZoneClock.SourceId(wall.Label)).FirstOrDefault(zone => zone is not null) ?? "";

    public static bool SameRepost(CalendarEntry local, SyncAnnouncement shared)
    {
        if (local.Manual)
            return false;
        return SameSeries(SyncAnnouncement.FromLocal(local, local.SpeakerWorld), shared) || SameFacts(FactsOf(local), FactsOf(shared));
    }

    public static bool SameSpeaker(CalendarEntry current, CalendarEntry incoming, DateTimeOffset when)
    {
        if (current.Manual || incoming.Manual || !Compatible(current, incoming))
            return false;
        if (string.IsNullOrWhiteSpace(current.Sender) || string.IsNullOrWhiteSpace(incoming.Sender))
            return false;
        if (!current.Sender.Equals(incoming.Sender, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!SimilarChat(current.Channel, incoming.Channel))
            return false;
        var earlier = current.DetectedAt == default ? when : current.DetectedAt;
        var gap = when - earlier;
        if (gap < TimeSpan.Zero || gap > TimeSpan.FromHours(8))
            return false;
        if (current.Ward is int leftWard && incoming.Ward is int rightWard && leftWard != rightWard)
            return false;
        var leftPlot = Number(PlotRegex, $"{current.EventText} {current.Place}");
        var rightPlot = Number(PlotRegex, $"{incoming.EventText} {incoming.Place}");
        if (leftPlot is not null && rightPlot is not null && leftPlot != rightPlot)
            return false;
        var leftWorld = ShareWorld.Choose("", current.SpeakerWorld, current.Server);
        var rightWorld = ShareWorld.Choose("", incoming.SpeakerWorld, incoming.Server);
        return string.IsNullOrWhiteSpace(leftWorld)
            || string.IsNullOrWhiteSpace(rightWorld)
            || leftWorld.Equals(rightWorld, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Compatible(CalendarEntry left, CalendarEntry right) => Compatible(FactsOf(left), FactsOf(right));
    public static bool Compatible(SyncAnnouncement left, SyncAnnouncement right) => Compatible(FactsOf(left), FactsOf(right));
    public static bool Compatible(CalendarEntry left, SyncAnnouncement right) => Compatible(FactsOf(left), FactsOf(right));

    private static bool Compatible(Facts left, Facts right)
    {
        if (left.Worlds.Count > 0 && right.Worlds.Count > 0 && !left.Worlds.Overlaps(right.Worlds)) return false;
        if (left.Day is DateOnly a && right.Day is DateOnly b && a != b) return false;
        if (left.Ward is int w && right.Ward is int x && w != x) return false;
        if (left.Plot is int p && right.Plot is int q && p != q) return false;
        var district = HousingTravel.DistrictName(left.Text);
        var otherDistrict = HousingTravel.DistrictName(right.Text);
        return district is null || otherDistrict is null || district == otherDistrict;
    }

    private readonly record struct Facts(HashSet<string> Worlds, DateOnly? Day, int? Ward, int? Plot, string Title, string Text);

    private static Facts FactsOf(CalendarEntry entry)
    {
        var text = $"{entry.EventText} {entry.Place}";
        var civil = SyncAnnouncement.FromLocal(entry, entry.SpeakerWorld);
        var day = DateOnly.TryParseExact(civil.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed) ? parsed : entry.Date;
        return new Facts(Worlds(entry.Server, entry.SpeakerWorld), day, entry.Ward ?? Number(WardRegex, text), Number(PlotRegex, text), RepostTitle(entry.EventText), entry.EventText);
    }

    private static Facts FactsOf(SyncAnnouncement item)
    {
        DateOnly? day = DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
        return new Facts(Worlds(item.World, ""), day, Number(WardRegex, $"{item.Text} {item.Place}"), Number(PlotRegex, $"{item.Text} {item.Place}"), item.Title.Length > 0 ? item.Title : RepostTitle(item.Text), $"{item.Text} {item.Place}");
    }

    private static bool SameFacts(Facts left, Facts right)
    {
        if (!Compatible(left, right)) return false;
        if (string.IsNullOrWhiteSpace(left.Text) || string.IsNullOrWhiteSpace(right.Text))
            return false;
        if (left.Worlds.Count > 0 && right.Worlds.Count > 0 && !left.Worlds.Overlaps(right.Worlds))
            return false;
        if (left.Ward is int leftWard && right.Ward is int rightWard && leftWard != rightWard)
            return false;
        if (left.Plot is int leftPlot && right.Plot is int rightPlot && leftPlot != rightPlot)
            return false;
        if (left.Day is DateOnly leftDay && right.Day is DateOnly rightDay && leftDay != rightDay)
            return false;
        if (left.Title.Length >= 4 && left.Title.Equals(right.Title, StringComparison.OrdinalIgnoreCase))
            return true;
        return NearCopy(left.Text, right.Text);
    }

    private static HashSet<string> Worlds(string? server, string? speaker)
    {
        var worlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddWorlds(worlds, server);
        if (worlds.Count == 0) AddWorlds(worlds, speaker);
        return worlds;
    }

    private static void AddWorlds(HashSet<string> worlds, string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return;
        foreach (var part in field.Split([',', '/', '|', '•'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (PlayableWorlds.TryCanonical(part, out var world))
                worlds.Add(world);
        }
    }

    private static readonly Regex MarkedName = new(
        @"[♦◎]\s*([A-Za-z][A-Za-z0-9 ]{1,24}?)\s*[♦◎]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string RepostTitle(string? text)
    {
        var chosen = EventTitle.Readable(EventTitle.Choose(text));
        if (chosen.Length >= 4)
            return chosen;
        var plain = EventTitle.Readable(text);
        var match = MarkedName.Match(plain);
        if (!match.Success)
            return "";
        var name = match.Groups[1].Value.Trim();
        return name.Length >= 4 ? name : "";
    }

    public static bool IsAbbreviation(string? full, string? brief)
    {
        var a = EventTitle.SearchKey(full).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var b = EventTitle.SearchKey(brief).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return b.Length > 0 && b.All(a.Contains);
    }

    public static bool CanCorrectWorld(SyncAnnouncement current, SyncAnnouncement incoming)
    {
        if (current.World.Equals(incoming.World, StringComparison.OrdinalIgnoreCase)) return false;
        if (!ServerNames.TryAdvertised(incoming.Text, out var named) || !named.Equals(incoming.World, StringComparison.OrdinalIgnoreCase)) return false;
        var stableUpdate = current.Id.Length > 0 && current.Id == incoming.Id && (incoming.Revision > current.Revision || incoming.ObservedAt > current.ObservedAt);
        if (stableUpdate) return true;
        if (ServerNames.TryAdvertised(current.Text, out _)) return false;
        var sameDate = current.Date.Length > 0 && current.Date == incoming.Date;
        var repeat = RepeatOf(current);
        var sameSeries = repeat is not null && repeat == RepeatOf(incoming) && SourceZone(current) == SourceZone(incoming)
            && DateOnly.TryParse(current.Date, out var a) && DateOnly.TryParse(incoming.Date, out var b)
            && repeat.Includes(a < b ? a : b, a < b ? b : a);
        if (!sameDate && !sameSeries) return false;
        var left = FactsOf(current) with { Worlds = Worlds(incoming.World, "") };
        return Compatible(left with { Day = sameSeries ? null : left.Day }, FactsOf(incoming) with { Day = sameSeries ? null : FactsOf(incoming).Day }) && NearCopy(current.Text, incoming.Text);
    }

    private static bool NearCopy(string? left, string? right)
    {
        var a = Fold(left);
        var b = Fold(right);
        if (a.Length >= 40 && b.Length >= 40 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)))
            return true;
        var leftWords = Words(a);
        var rightWords = Words(b);
        if (leftWords.Count == 0 || rightWords.Count == 0)
            return false;
        var shared = leftWords.Intersect(rightWords, StringComparer.OrdinalIgnoreCase).Count();
        var smaller = Math.Min(leftWords.Count, rightWords.Count);
        return shared >= 6 && shared * 5 >= smaller * 4;
    }

    private static string Fold(string? text)
    {
        var key = EventTitle.SearchKey(text);
        return string.Join(' ', key.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool SimilarChat(int left, int right)
    {
        if (left == right)
            return true;
        return SharePolicy.IsShareable(left) && SharePolicy.IsShareable(right);
    }

    private static int? Number(Regex regex, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = regex.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }

    private static bool Overlap(string? leftText, string? rightText)
    {
        var left = Words(leftText);
        var right = Words(rightText);
        if (left.Count == 0 || right.Count == 0)
            return false;
        var shared = left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count();
        var smaller = Math.Min(left.Count, right.Count);
        return shared >= 3 && shared * 2 >= smaller;
    }

    private static HashSet<string> Words(string? text)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return words;
        var stripped = ClockRegex.Replace(text, " ");
        foreach (var raw in stripped.Split([' ', '\r', '\n', '\t', ',', '.', '!', '?', '/', '|', ':', ';', '&', '♥', '★', '♪'], StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim().ToLowerInvariant();
            if (word.Length < 4 || Skip.Contains(word))
                continue;
            words.Add(word);
        }

        return words;
    }
}
