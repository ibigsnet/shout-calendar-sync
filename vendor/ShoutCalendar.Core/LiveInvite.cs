using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

public static class LiveInvite
{
    private static readonly Regex Activity = new(@"\b(?:hunt(?:\s+train)?|train|fate(?:\s+train)?|s[- ]?rank|a[- ]?rank|world\s+boss|treasure\s+map(?:s)?|map\s+party)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Gathering = new(@"\b(?:assembling|gathering|forming|rallying|starting\s+(?:now|at)|departing|leaving\s+now|about\s+to\s+(?:start|depart)|meet(?:ing)?\s+(?:up\s+)?(?:at|here)|join\s+us\s+(?:at|here))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NotNow = new(@"\b(?:yesterday|tomorrow|tonight|monday|tuesday|wednesday|thursday|friday|saturday|sunday|cancelled|canceled|finished|ended|over|already\s+left|was|were|will|next\s+week|not\s+(?:assembling|gathering|forming))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsGathering(string? text) => !string.IsNullOrWhiteSpace(text)
        && Activity.IsMatch(text) && Gathering.IsMatch(text) && !NotNow.IsMatch(text)
        && MapMentions.Read(text).Any(spot => spot.Place.Length > 0);

    public static bool IsRecent(string? text, DateTimeOffset observed, DateTimeOffset now) =>
        observed != default && now >= observed && now - observed <= TimeSpan.FromMinutes(30) && IsGathering(text);
}
