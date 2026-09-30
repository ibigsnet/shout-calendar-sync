namespace ShoutCalendar.Core;

public static class EventTitle
{
    public static string Choose(string? text, string? category = null)
    {
        var boxed = Boxed(text);
        if (boxed.Length > 0)
            return boxed;
        var marked = Marked(text);
        if (marked.Length > 0)
            return marked;
        return string.IsNullOrWhiteSpace(category) ? "" : category.Trim();
    }

    private static string Boxed(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var phrases = new List<(int Start, int End, int Words, int Letters)>();
        var start = -1;
        var words = 0;
        var letters = 0;
        var inWord = false;
        void Close(int index)
        {
            if (start >= 0 && letters > 0)
                phrases.Add((start, index, words, letters));
            start = -1;
            words = 0;
            letters = 0;
            inWord = false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is >= '\uE071' and <= '\uE08A')
            {
                if (start < 0)
                    start = i;
                if (!inWord)
                {
                    words++;
                    inWord = true;
                }

                letters++;
                continue;
            }

            if (start >= 0 && (ch is '\'' or '\u2019' || char.IsWhiteSpace(ch) || ch is >= '\uE000' and <= '\uF8FF'))
            {
                inWord = false;
                continue;
            }

            Close(i);
        }

        Close(text.Length);
        foreach (var phrase in phrases)
        {
            if (phrase.Words >= 2)
                return text[phrase.Start..phrase.End].Trim();
        }

        foreach (var phrase in phrases)
        {
            if (phrase.Letters >= 4)
                return text[phrase.Start..phrase.End].Trim();
        }

        return "";
    }

    private static string Marked(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var stars = System.Text.RegularExpressions.Regex.Match(text, @"★\s*([^★\r\n]{2,40}?)\s*★");
        if (stars.Success)
            return stars.Groups[1].Value.Trim();
        var corners = System.Text.RegularExpressions.Regex.Match(text, @"【\s*([^】\r\n]{2,40}?)\s*】");
        return corners.Success ? corners.Groups[1].Value.Trim() : "";
    }

    public static string Readable(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return "";
        var chars = new char[title.Length];
        var count = 0;
        foreach (var ch in title)
        {
            if (ch is >= '\uE071' and <= '\uE08A')
                chars[count++] = (char)('A' + (ch - '\uE071'));
            else if (ch is >= '\uE000' and <= '\uF8FF' || ch is '★' or '☆' or '△' or '□' or '【' or '】')
                continue;
            else
                chars[count++] = ch;
        }

        return new string(chars, 0, count).Trim();
    }

    public static string SearchKey(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var chars = new char[text.Length];
        var count = 0;
        foreach (var ch in text)
        {
            if (ch is >= '\uE071' and <= '\uE08A')
                chars[count++] = (char)('a' + (ch - '\uE071'));
            else if (ch is >= '\uFF21' and <= '\uFF3A')
                chars[count++] = (char)('a' + (ch - '\uFF21'));
            else if (ch is >= '\uFF41' and <= '\uFF5A')
                chars[count++] = (char)('a' + (ch - '\uFF41'));
            else if (ch is >= '\uE000' and <= '\uF8FF' || ch is '★' or '☆' or '△' or '□' or '【' or '】')
                continue;
            else
                chars[count++] = char.ToLowerInvariant(ch);
        }

        return new string(chars, 0, count);
    }
}
