namespace ShoutCalendar.Core;

public static class TellDraft
{
    public static string Command(string? sender, string? speakerWorld, string? eventWorld, string? title)
    {
        var (name, world) = SenderName.TellTarget(sender, speakerWorld);
        if (world.Length == 0 && PlayableWorlds.TryNamedWorld(eventWorld, out var named))
            world = named;
        if (!SenderName.IsCharacter(name))
            return "";
        var who = world.Length > 0 ? $"{name}@{world}" : name;
        var subject = EventTitle.Readable(title);
        var about = subject.Length == 0 || subject.Equals("that invite", StringComparison.OrdinalIgnoreCase)
            ? "that invite"
            : "that invite, " + subject;
        return $"/tell {who} Hey, I had a question about {about}. What location will we be meeting up at?";
    }
}
