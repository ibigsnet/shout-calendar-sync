namespace ShoutCalendar.Core;

public static class HousingDistrict
{
    public static string? FromZone(string? zoneText)
    {
        if (string.IsNullOrWhiteSpace(zoneText))
            return null;
        if (zoneText.Contains("Limsa", StringComparison.OrdinalIgnoreCase))
            return "Mist";
        if (zoneText.Contains("Gridania", StringComparison.OrdinalIgnoreCase))
            return "The Lavender Beds";
        if (zoneText.Contains("Ul'dah", StringComparison.OrdinalIgnoreCase)
            || zoneText.Contains("Ul’dah", StringComparison.OrdinalIgnoreCase))
            return "The Goblet";
        if (zoneText.Contains("Kugane", StringComparison.OrdinalIgnoreCase))
            return "Shirogane";
        if (zoneText.Contains("Ishgard", StringComparison.OrdinalIgnoreCase)
            || zoneText.Contains("Foundation", StringComparison.OrdinalIgnoreCase)
            || zoneText.Contains("Pillars", StringComparison.OrdinalIgnoreCase)
            || zoneText.Contains("Firmament", StringComparison.OrdinalIgnoreCase))
            return "Empyreum";
        return null;
    }
}
