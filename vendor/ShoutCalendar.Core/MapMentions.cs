using System.Globalization;
using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

public static partial class MapMentions
{
    public readonly record struct Spot(float X, float Y, string Place = "");

    private static readonly Regex Pair = new(
        @"(?:(?<![A-Za-z])(?:[\uE000-\uF8FF]+\s*)?(?-i:(?<zone>[A-Z][A-Za-z'’.\-]*(?:\s+[A-Z][A-Za-z'’.\-]*){0,4}))\s+)?\(\s*(?<x>\d{1,2}(?:\.\d+)?)\s*,\s*(?<y>\d{1,2}(?:\.\d+)?)\s*\)|\bx\s+(?<x>\d{1,2}(?:\.\d+)?)\s*(?:,\s*y|,|\s+y)\s*(?<y>\d{1,2}(?:\.\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<Spot> Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var found = new List<Spot>();
        foreach (Match match in Pair.Matches(text))
        {
            if (!float.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                continue;
            if (!float.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                continue;
            if (x is < 0 or > 50 || y is < 0 or > 50)
                continue;
            if (found.Any(spot => Math.Abs(spot.X - x) < 0.05f && Math.Abs(spot.Y - y) < 0.05f))
                continue;
            var zone = match.Groups["zone"].Success ? match.Groups["zone"].Value.Trim() : "";
            found.Add(new Spot(x, y, zone));
        }

        return found;
    }
}
