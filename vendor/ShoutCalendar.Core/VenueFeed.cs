using System.Globalization;
using System.Text.Json;

namespace ShoutCalendar.Core;

public static class VenueFeed
{
    public static bool MayFetch(bool enabled, bool rememberChoice, bool openExternal) =>
        enabled && rememberChoice && openExternal;

    public static IReadOnlyList<CalendarEntry> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            var rows = new List<CalendarEntry>();
            foreach (var venue in document.RootElement.EnumerateArray())
            {
                var name = Text(venue, "name");
                var id = Text(venue, "id");
                if (name.Length == 0 || id.Length == 0 || !venue.TryGetProperty("location", out var location))
                    continue;
                var place = Place(location);
                var world = Text(location, "world");
                if (!venue.TryGetProperty("schedule", out var schedule) || schedule.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var opening in schedule.EnumerateArray())
                {
                    if (!opening.TryGetProperty("resolution", out var resolution))
                        continue;
                    var start = Text(resolution, "start");
                    if (!DateTimeOffset.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
                        continue;
                    var discord = Text(venue, "discord");
                    var text = discord.Length == 0 ? name : name + " " + discord;
                    rows.Add(new CalendarEntry(
                        DateOnly.FromDateTime(when.DateTime),
                        TimeOnly.FromDateTime(when.DateTime),
                        null,
                        Int(location, "ward"),
                        world,
                        place,
                        text,
                        "FFXIV Venues",
                        false,
                        "venue:" + id + ":" + start,
                        DateTimeOffset.UtcNow,
                        null,
                        0,
                        false,
                        true));
                }
            }

            return rows;
        }
    }

    private static string Place(JsonElement location)
    {
        var parts = new List<string>();
        var district = Text(location, "district");
        var ward = Int(location, "ward");
        var plot = Int(location, "plot");
        var world = Text(location, "world");
        if (ward is int wardNumber)
            parts.Add("ward " + wardNumber.ToString(CultureInfo.InvariantCulture));
        if (plot is int plotNumber)
            parts.Add("plot " + plotNumber.ToString(CultureInfo.InvariantCulture));
        if (district.Length > 0)
            parts.Add(district);
        if (world.Length > 0)
            parts.Add(world);
        return string.Join(", ", parts);
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) && number > 0
            ? number
            : null;
}
