namespace ShoutCalendar.Core;

public static class ShareWorld
{
    public static string Choose(string? heardOn, string? speakerHome, string? namedInText, string? shoutText = null)
    {
        if (ServerNames.TryAdvertised(shoutText, out var advertised))
            return advertised;
        if (PlayableWorlds.TryNamedWorld(namedInText, out var named))
            return named;
        if (LiveInvite.IsGathering(shoutText) && PlayableWorlds.TryCanonical(heardOn, out var assembling))
            return assembling;
        if (PlayableWorlds.TryCanonical(speakerHome, out var home))
            return home;
        if (PlayableWorlds.TryCanonical(heardOn, out var heard))
            return heard;
        return (heardOn ?? "").Trim();
    }
}
