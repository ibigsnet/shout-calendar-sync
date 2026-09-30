using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

public static partial class LinkFinder
{
    public readonly record struct Found(string Label, string Url);

    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DiscordRegex = new(
        @"(?:https?://)?(?:discord\.gg|discord(?:app)?\.com/invite)/([A-Za-z0-9-]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TwitchRegex = new(
        @"(?:https?://)?(?:www\.)?twitch\.(?:tv|com)/([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BareRegex = new(
        @"(?<![\w@./])(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:carrd\.co|co\.uk|com\.au|com|net|org|io|gg|app|site|page|xyz|info|club|online|live|link|dev|art|wiki|blog|store|shop|social|moe|tv|me|us|uk|ca|eu|de|fr|jp|au|nz|be|ly|gl|ee|ai|cc|sh|co)(?![a-z0-9-])(?:/[^\s<>""']*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<Found> Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var ranked = new List<(int At, int End, string Url)>();
        foreach (Match match in UrlRegex.Matches(text))
            ranked.Add((match.Index, match.Index + match.Length, TrimTail(match.Value)));
        foreach (Match match in DiscordRegex.Matches(text))
        {
            var url = "https://discord.gg/" + match.Groups[1].Value;
            if (Covered(ranked, match.Index, match.Index + match.Length))
                continue;
            ranked.Add((match.Index, match.Index + match.Length, url));
        }

        foreach (Match match in TwitchRegex.Matches(text))
        {
            var login = match.Groups[1].Value;
            var url = "https://twitch.tv/" + login;
            if (Covered(ranked, match.Index, match.Index + match.Length)
                || ranked.Any(hit =>
                    hit.Url.Contains("twitch", StringComparison.OrdinalIgnoreCase)
                    && hit.Url.Contains("/" + login, StringComparison.OrdinalIgnoreCase)))
                continue;
            ranked.Add((match.Index, match.Index + match.Length, url));
        }

        foreach (Match match in BareRegex.Matches(text))
        {
            var end = match.Index + match.Length;
            if (Covered(ranked, match.Index, end))
                continue;
            var host = TrimTail(match.Value);
            if (host.Count(ch => ch == '.') < 1)
                continue;
            var url = "https://" + host;
            if (DuplicatesHost(ranked, url))
                continue;
            ranked.Add((match.Index, end, url));
        }

        var found = new List<Found>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in ranked.OrderBy(hit => hit.At))
            Add(found, seen, hit.Url);
        return found;
    }

    private static bool Covered(List<(int At, int End, string Url)> ranked, int start, int end) =>
        ranked.Any(hit => start < hit.End && end > hit.At);

    private static bool DuplicatesHost(List<(int At, int End, string Url)> ranked, string url)
    {
        var path = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url["https://".Length..] : url;
        var slash = path.IndexOf('/');
        var host = slash < 0 ? path : path[..slash];
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];
        var tail = slash < 0 ? "" : path[slash..];
        foreach (var hit in ranked)
        {
            if (hit.Url.Contains(host, StringComparison.OrdinalIgnoreCase))
                return true;
            if (tail.Length > 1
                && host.StartsWith("twitch.", StringComparison.OrdinalIgnoreCase)
                && hit.Url.Contains("twitch.", StringComparison.OrdinalIgnoreCase)
                && hit.Url.Contains(tail, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsHttp(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static void Add(List<Found> found, HashSet<string> seen, string url)
    {
        if (!IsHttp(url) || !seen.Add(url))
            return;
        var label = url;
        if (label.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            label = label["https://".Length..];
        else if (label.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            label = label["http://".Length..];
        found.Add(new Found(label, url));
    }

    private static string TrimTail(string value) =>
        value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '>', '"', '\'');
}
