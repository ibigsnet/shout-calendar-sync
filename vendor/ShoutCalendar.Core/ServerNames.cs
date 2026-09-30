using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

public static class ServerNames
{
    public static readonly IReadOnlyList<string> All =
    [
        "Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren",
        "Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros",
        "Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera",
        "Cuchulainn", "Golem", "Halicarnassus", "Kraken", "Maduin", "Marilith", "Rafflesia", "Seraph",
        "Cerberus", "Louisoix", "Moogle", "Omega", "Phantom", "Ragnarok", "Sagittarius", "Spriggan",
        "Alpha", "Lich", "Odin", "Phoenix", "Raiden", "Shiva", "Twintania", "Zodiark",
        "Aegis", "Atomos", "Carbuncle", "Garuda", "Gungnir", "Kujata", "Tonberry", "Typhon",
        "Alexander", "Bahamut", "Durandal", "Fenrir", "Ifrit", "Ridill", "Tiamat", "Ultima",
        "Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan",
        "Belias", "Mandragora", "Ramuh", "Shinryu", "Unicorn", "Valefor", "Yojimbo", "Zeromus",
        "Bismarck", "Ravana", "Sephirot", "Sophia", "Zurvan",
        "Aether", "Primal", "Crystal", "Dynamis", "Chaos", "Light", "Elemental", "Gaia", "Mana", "Meteor", "Materia",
    ];

    private static readonly (string Alias, string Name)[] ShortNames =
    [
        ("Dyn", "Dynamis"),
        ("Krak", "Kraken"),
        ("Bryn", "Brynhildr"),
        ("Crys", "Crystal"),
        ("Raff", "Rafflesia"),
        ("Zal", "Zalera"),
        ("Diab", "Diabolos"),
    ];

    private static readonly HashSet<string> NotAWorld = new(StringComparer.OrdinalIgnoreCase)
    {
        "atom", "club", "come", "free", "from", "game", "gold", "have", "here", "home",
        "host", "join", "just", "last", "mate", "mist", "next", "only", "open", "plot",
        "show", "that", "this", "time", "ward", "week", "with", "your",
    };

    private static readonly Dictionary<string, string> CanonicalByFold = BuildCanonical();

    private static readonly Regex Pattern = new(
        @"\b(?:" + string.Join("|", Tokens().OrderByDescending(name => name.Length).Select(Regex.Escape)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> Match(string? text)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return found;
        foreach (Match match in Pattern.Matches(text))
            Add(found, match.Value);
        foreach (Match token in TokenPattern.Matches(text))
        {
            if (Add(found, token.Value))
                continue;
            if (NotAWorld.Contains(token.Value))
                continue;
            if (TryUniquePrefix(token.Value, out var prefix))
                Add(found, prefix);
            else if (TryUniqueTypo(token.Value, out var typo))
                Add(found, typo);
        }

        return found;
    }

    public static bool TryAdvertised(string? text, out string world)
    {
        world = "";
        string? found = null;
        foreach (var name in Match(text))
        {
            if (!PlayableWorlds.TryCanonical(name, out var canonical))
                continue;
            if (found is null)
            {
                found = canonical;
                continue;
            }

            if (!found.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (found is null)
            return false;
        world = found;
        return true;
    }

    public static bool ForServer(string? text, string? storedWorld, string? here)
    {
        if (!PlayableWorlds.TryCanonical(here, out var home))
            return true;
        if (TryAdvertised(text, out var named))
            return named.Equals(home, StringComparison.OrdinalIgnoreCase);
        if (PlayableWorlds.TryNamedWorld(storedWorld, out var stored))
            return stored.Equals(home, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static readonly Regex TokenPattern = new(
        @"[A-Za-z]{3,}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool Add(List<string> found, string token)
    {
        if (!CanonicalByFold.TryGetValue(token, out var canonical))
            return false;
        if (!found.Contains(canonical))
            found.Add(canonical);
        return true;
    }

    private static bool TryUniquePrefix(string token, out string world)
    {
        world = "";
        if (token.Length < 4)
            return false;
        string? hit = null;
        foreach (var name in All)
        {
            if (!name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                continue;
            if (token.Length * 2 < name.Length)
                continue;
            if (hit is not null && !hit.Equals(name, StringComparison.OrdinalIgnoreCase))
                return false;
            hit = name;
        }

        if (hit is null)
            return false;
        world = hit;
        return true;
    }

    private static bool TryUniqueTypo(string token, out string world)
    {
        world = "";
        if (token.Length < 5)
            return false;
        string? hit = null;
        foreach (var name in All)
        {
            if (Math.Abs(name.Length - token.Length) > 1)
                continue;
            if (!SharesPrefix(token, name, 3) || Edits(token, name) != 1)
                continue;
            if (hit is not null)
                return false;
            hit = name;
        }

        if (hit is null)
            return false;
        world = hit;
        return true;
    }

    private static bool SharesPrefix(string left, string right, int count)
    {
        if (left.Length < count || right.Length < count)
            return false;
        return left.AsSpan(0, count).Equals(right.AsSpan(0, count), StringComparison.OrdinalIgnoreCase);
    }

    private static int Edits(string left, string right)
    {
        var rows = left.Length + 1;
        var cols = right.Length + 1;
        var prev = new int[cols];
        var next = new int[cols];
        for (var col = 0; col < cols; col++)
            prev[col] = col;
        for (var row = 1; row < rows; row++)
        {
            next[0] = row;
            var min = next[0];
            for (var col = 1; col < cols; col++)
            {
                var cost = char.ToUpperInvariant(left[row - 1]) == char.ToUpperInvariant(right[col - 1]) ? 0 : 1;
                next[col] = Math.Min(Math.Min(next[col - 1] + 1, prev[col] + 1), prev[col - 1] + cost);
                if (next[col] < min)
                    min = next[col];
            }

            if (min > 1)
                return 2;
            (prev, next) = (next, prev);
        }

        return prev[cols - 1];
    }

    private static Dictionary<string, string> BuildCanonical()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in All)
            map[name] = name;
        foreach (var (alias, name) in ShortNames)
            map[alias] = name;
        return map;
    }

    private static IEnumerable<string> Tokens()
    {
        foreach (var name in All)
            yield return name;
        foreach (var (alias, _) in ShortNames)
            yield return alias;
    }
}
