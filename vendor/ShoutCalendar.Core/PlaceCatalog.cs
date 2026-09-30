namespace ShoutCalendar.Core;

public sealed class PlaceCatalog
{
    public static PlaceCatalog Empty { get; } = new([]);

    private readonly string[] namesLongestFirst;

    public PlaceCatalog(IEnumerable<string> names)
    {
        this.namesLongestFirst = names
            .Select(name => name.Trim())
            .Where(name => name.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(name => name.Length)
            .ToArray();
    }

    public int Count => this.namesLongestFirst.Length;

    public IReadOnlyList<string> Match(string text)
    {
        var found = new List<string>();
        var taken = new List<(int Start, int End)>();
        foreach (var name in this.namesLongestFirst)
        {
            var index = 0;
            while (index < text.Length)
            {
                var at = text.IndexOf(name, index, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    break;
                var end = at + name.Length;
                var leftFree = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                var rightFree = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                var overlaps = taken.Any(span => at < span.End && end > span.Start);
                if (leftFree && rightFree && !overlaps)
                {
                    found.Add(name);
                    taken.Add((at, end));
                    break;
                }

                index = at + 1;
            }
        }

        foreach (var name in this.namesLongestFirst.OrderBy(name => name.Length))
        {
            var space = name.IndexOf(' ');
            if (space < 5)
                continue;
            var head = name[..space];
            if (OrdinaryWord(head) || DataCenters.IsName(head))
                continue;
            if (found.Any(hit => hit.StartsWith(head, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!ContainsWord(text, head))
                continue;
            found.Add(name);
        }

        foreach (var name in this.namesLongestFirst.OrderBy(name => name.Length))
        {
            if (!name.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = name[4..];
            if (rest.Length < 4 || found.Contains(name) || !ContainsPhrase(text, rest))
                continue;
            found.Add(name);
        }

        return found;
    }

    private static bool OrdinaryWord(string word) =>
        word.Equals("information", StringComparison.OrdinalIgnoreCase)
        || word.Equals("company", StringComparison.OrdinalIgnoreCase)
        || word.Equals("private", StringComparison.OrdinalIgnoreCase)
        || word.Equals("center", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsPhrase(string text, string phrase)
    {
        var at = text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return false;
        var end = at + phrase.Length;
        var leftFree = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
        var rightFree = end >= text.Length || !char.IsLetterOrDigit(text[end]);
        return leftFree && rightFree;
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = 0;
        while (index < text.Length)
        {
            var at = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return false;
            var end = at + word.Length;
            var leftFree = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            var rightFree = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (leftFree && rightFree)
                return true;
            index = at + 1;
        }

        return false;
    }
}
