namespace ShoutCalendar.Core;

public static class ChatChannels
{
    public static readonly IReadOnlyList<ChatListenOption> All =
    [
        new(10, "Say", true),
        new(ShoutHarvest.ShoutChannel, "Shout", true),
        new(30, "Yell", true),
        new(13, "Tell (incoming)", true),
        new(12, "Tell (outgoing)", false),
        new(14, "Party", false),
        new(15, "Alliance", false),
        new(32, "Cross-world party", false),
        new(16, "Linkshell 1", false),
        new(17, "Linkshell 2", false),
        new(18, "Linkshell 3", false),
        new(19, "Linkshell 4", false),
        new(20, "Linkshell 5", false),
        new(21, "Linkshell 6", false),
        new(22, "Linkshell 7", false),
        new(23, "Linkshell 8", false),
        new(ShoutHarvest.FreeCompanyChannel, "Free company", true),
        new(69, "Free company announcement", true),
        new(27, "Novice network", true),
        new(36, "PvP team", false),
        new(37, "Cross-world linkshell 1", false),
        new(101, "Cross-world linkshell 2", false),
        new(102, "Cross-world linkshell 3", false),
        new(103, "Cross-world linkshell 4", false),
        new(104, "Cross-world linkshell 5", false),
        new(105, "Cross-world linkshell 6", false),
        new(106, "Cross-world linkshell 7", false),
        new(107, "Cross-world linkshell 8", false),
    ];

    public static IReadOnlyList<int> DefaultIds { get; } = All.Where(option => option.DefaultOn).Select(option => option.Channel).ToArray();

    public static bool IsKnown(int channel) => All.Any(option => option.Channel == channel);

    public static bool Allows(int channel, IReadOnlySet<int>? selected)
    {
        var allowed = selected ?? DefaultIds.ToHashSet();
        return allowed.Contains(channel);
    }
}

public sealed record ChatListenOption(int Channel, string Label, bool DefaultOn);
