using System.Text;

namespace ShoutCalendar.Core;

public static class PromptLayout
{
    public static string Wrap(string? text, int columns)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        if (columns < 8)
            columns = 8;

        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in Words(paragraph))
            {
                if (line.Length == 0)
                {
                    HardAppend(lines, line, word, columns);
                    continue;
                }

                if (line.Length + 1 + word.Length <= columns)
                {
                    line.Append(' ');
                    line.Append(word);
                    continue;
                }

                lines.Add(line.ToString());
                line.Clear();
                HardAppend(lines, line, word, columns);
            }

            lines.Add(line.ToString());
        }

        return string.Join('\n', lines);
    }

    private static void HardAppend(List<string> lines, StringBuilder line, string word, int columns)
    {
        var rest = word;
        while (rest.Length > columns)
        {
            lines.Add(rest[..columns]);
            rest = rest[columns..];
        }

        line.Append(rest);
    }

    private static IEnumerable<string> Words(string paragraph)
    {
        var start = 0;
        for (var i = 0; i <= paragraph.Length; i++)
        {
            if (i < paragraph.Length && paragraph[i] != ' ')
                continue;
            if (i > start)
                yield return paragraph[start..i];
            start = i + 1;
        }
    }
}
