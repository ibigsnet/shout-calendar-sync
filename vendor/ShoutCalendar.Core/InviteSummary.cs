namespace ShoutCalendar.Core;

public static class InviteSummary
{
    public static string Preview(string? text, int limit = 120)
    {
        var plain = string.Join(" ", EventTitle.Readable(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return plain.Length <= limit ? plain : plain[..Math.Max(0, limit - 1)] + "…";
    }

    public static string Title(string? text)
    {
        var title = EventTitle.Readable(EventTitle.Choose(text));
        return title.Length > 0 ? title : Preview(text, 72);
    }
}
