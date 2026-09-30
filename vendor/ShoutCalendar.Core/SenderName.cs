namespace ShoutCalendar.Core;

public static class SenderName
{
    public static string Clean(string? sender)
    {
        if (string.IsNullOrWhiteSpace(sender))
            return "";
        var kept = new char[sender.Length];
        for (var i = 0; i < sender.Length; i++)
        {
            var ch = sender[i];
            kept[i] = char.IsLetter(ch) || ch is ' ' or '\'' or '\u2019' or '-' ? ch : ' ';
        }

        var words = new string(kept).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 4 && words.Length % 2 == 0)
        {
            var half = words.Length / 2;
            var left = string.Join(' ', words[..half]);
            var right = string.Join(' ', words[half..]);
            if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
                return left;
        }

        return string.Join(' ', words);
    }

    public static (string Name, string World) TellTarget(string? sender, string? speakerWorld)
    {
        var clean = Clean(sender);
        var preferred = "";
        if (PlayableWorlds.TryCanonical(speakerWorld, out var known))
            preferred = known;
        if (clean.Length == 0)
            return ("", preferred);

        if (preferred.Length > 0 && TryStrip(clean, preferred, out var named))
            return (named, preferred);

        foreach (var world in PlayableWorlds.All.OrderByDescending(item => item.Length))
        {
            if (!TryStrip(clean, world, out var stripped))
                continue;
            return (stripped, preferred.Length > 0 ? preferred : world);
        }

        return (clean, preferred);
    }

    public static bool IsCharacter(string? name)
    {
        var words = Clean(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 2)
            return false;
        foreach (var word in words)
        {
            if (word.Length is < 2 or > 15 || NotAName.Contains(word))
                return false;
        }

        return true;
    }

    private static readonly HashSet<string> NotAName = new(StringComparer.OrdinalIgnoreCase)
    {
        "ward", "plot", "the", "goblet", "mist", "shirogane", "empyreum", "lavender", "beds", "subdivision", "district",
    };

    private static bool TryStrip(string clean, string world, out string name)
    {
        name = clean;
        if (world.Length == 0 || clean.Length <= world.Length)
            return false;
        if (clean.EndsWith(" " + world, StringComparison.OrdinalIgnoreCase))
        {
            name = clean[..^(world.Length + 1)].Trim();
            return name.Length > 0;
        }

        if (!clean.EndsWith(world, StringComparison.OrdinalIgnoreCase))
            return false;
        var boundary = clean.Length - world.Length;
        if (!char.IsLetter(clean[boundary - 1]))
            return false;
        name = clean[..boundary].Trim();
        return name.Length >= 2;
    }
}
