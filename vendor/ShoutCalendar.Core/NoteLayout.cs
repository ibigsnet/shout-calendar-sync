using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

public static class NoteLayout
{
    private static readonly Regex Sections = new(@"\s+[|/]\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Sentences = new(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> Lines(string? text)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return lines;
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            foreach (var section in Sections.Split(raw))
            {
                var bit = section.Trim();
                if (bit.Length == 0)
                    continue;
                if (bit.Length < 90)
                {
                    AddSection(lines, bit);
                    continue;
                }

                foreach (var sentence in Sentences.Split(bit))
                {
                    var line = sentence.Trim();
                    if (line.Length > 0)
                        AddSection(lines, line);
                }
            }
        }

        return lines;
    }

    private static void AddSection(List<string> lines, string bit)
    {
        var links = LinkFinder.Find(bit);
        if (links.Count == 0)
        {
            lines.Add(bit);
            return;
        }

        var rest = bit;
        foreach (var link in links)
        {
            rest = Cut(rest, link.Url);
            rest = Cut(rest, "http://" + link.Label);
            rest = Cut(rest, link.Label);
        }

        rest = Collapse(rest).Trim(' ', '|', '/', '-', '•', ',', ';');
        if (rest.Length > 0)
            lines.Add(rest);
        foreach (var link in links)
            lines.Add(link.Label);
    }

    private static string Cut(string text, string token)
    {
        if (token.Length == 0)
            return text;
        return text.Replace(token, " ", StringComparison.OrdinalIgnoreCase);
    }

    private static string Collapse(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }
}
