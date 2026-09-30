using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

public static class IconText
{
    private static readonly Regex GluedZone = new(
        @"(?<=[ap]m)(?=(?:PDT|PST|EDT|EST|CDT|CST|MDT|MST|PT|ET|CT|MT|ST)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Plain(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var buffer = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            if (ch == '\uE031' || ch == '\uE0D0')
            {
                buffer.Append(' ');
                continue;
            }

            if (ch == '\uE06D')
            {
                buffer.Append("am");
                continue;
            }

            if (ch == '\uE06E')
            {
                buffer.Append("pm");
                continue;
            }

            if (ch == '\uE0D1')
            {
                buffer.Append(" ST");
                continue;
            }

            if (ch == '\uE0D2')
            {
                buffer.Append(" ET");
                continue;
            }

            if (ch is >= '\uE060' and <= '\uE069')
            {
                buffer.Append((char)('0' + (ch - '\uE060')));
                continue;
            }

            if (ch is >= '\uE08F' and <= '\uE098')
            {
                buffer.Append((char)('0' + (ch - '\uE08F')));
                continue;
            }

            if (ch is >= '\uE0E0' and <= '\uE0E9')
            {
                buffer.Append((char)('0' + (ch - '\uE0E0')));
                continue;
            }

            if (ch is >= '\uE099' and <= '\uE0AE')
            {
                var number = 10 + (ch - '\uE099');
                if (buffer.Length > 0 && char.IsDigit(buffer[^1]))
                    buffer.Append(':');
                buffer.Append(number.ToString(CultureInfo.InvariantCulture));
                continue;
            }

            buffer.Append(ch);
        }

        return GluedZone.Replace(buffer.ToString(), " ");
    }
}
