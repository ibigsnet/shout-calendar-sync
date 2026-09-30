using System.Globalization;

namespace ShoutCalendar.Core;

public static class AlarmNotice
{
    public readonly record struct Ring(CalendarEntry Entry, int MinutesBefore, bool Accepted);

    public static string Line(CalendarEntry entry, int minutesBefore, string? here = null)
    {
        var lead = Lead(entry, minutesBefore);
        var spot = HousingTravel.FindVenue(entry.Place, entry.EventText, entry.Ward, entry.Server, entry.SpeakerWorld);
        var needed = spot?.World ?? "";
        if (needed.Length == 0 && PlayableWorlds.TryNamedWorld(entry.SpeakerWorld, out var named))
            needed = named;

        var parts = new List<string> { "Shout Calendar:", lead + "." };
        if (PlayableWorlds.TryCanonical(here, out var standing)
            && needed.Length > 0
            && !needed.Equals(standing, StringComparison.OrdinalIgnoreCase))
            parts.Add(Hop(needed, standing));
        if (spot is HousingSpot housing && housing.CityAetheryteId is not null)
        {
            var ward = housing.Ward is int number
                ? $" Select {housing.District} ward {number.ToString(CultureInfo.InvariantCulture)}."
                : "";
            parts.Add($"Teleport: {housing.City} aetheryte.{ward}");
        }

        return string.Join(" ", parts);
    }

    public static IReadOnlyList<Ring> Dedupe(IEnumerable<Ring> rings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<Ring>();
        foreach (var ring in rings)
        {
            if (string.IsNullOrEmpty(ring.Entry.Id) || !seen.Add(Identity(ring.Entry)))
                continue;
            kept.Add(ring);
        }

        return kept;
    }

    public static string Broadcast(IReadOnlyList<Ring> rings, string? here = null)
    {
        if (rings.Count == 0)
            return "";
        if (rings.Count == 1)
            return Line(rings[0].Entry, rings[0].MinutesBefore, here);

        var parts = new List<string> { "Shout Calendar:" };
        foreach (var ring in rings)
            parts.Add(Brief(ring.Entry, ring.MinutesBefore) + ".");
        return string.Join(" ", parts);
    }

    private static string Lead(CalendarEntry entry, int minutesBefore)
    {
        var title = EventTitle.Readable(EventTitle.Choose(entry.EventText));
        var name = title.Length > 0 ? title : "An event";
        var when = entry.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        var lead = minutesBefore > 0
            ? $"{name} starts in {minutesBefore} minutes"
            : $"{name} is starting";
        if (when.Length > 0)
            lead += " at " + when;
        if (entry.Date is DateOnly day)
            lead += " on " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return lead;
    }

    private static string Brief(CalendarEntry entry, int minutesBefore)
    {
        var lead = Lead(entry, minutesBefore);
        var where = Where(entry);
        return where.Length == 0 ? lead : lead + ", " + where;
    }

    private static string Where(CalendarEntry entry)
    {
        var spot = HousingTravel.FindVenue(entry.Place, entry.EventText, entry.Ward, entry.Server, entry.SpeakerWorld);
        var world = spot?.World ?? "";
        if (world.Length == 0)
            world = WorldOf(entry);
        if (spot is not HousingSpot housing || housing.Ward is not int number || housing.District.Length == 0)
            return world;
        var place = housing.District + " ward " + number.ToString(CultureInfo.InvariantCulture);
        return world.Length == 0 ? place : world + ", " + place;
    }

    private static string Identity(CalendarEntry entry)
    {
        var title = EventTitle.Readable(EventTitle.Choose(entry.EventText));
        if (title.Length == 0)
            title = entry.Id;
        var time = entry.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        var date = entry.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        return string.Join('|', title, date, time, WorldOf(entry));
    }

    private static string WorldOf(CalendarEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Server))
        {
            foreach (var part in entry.Server.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (PlayableWorlds.TryNamedWorld(part, out var named))
                    return named;
            }
        }

        return PlayableWorlds.TryNamedWorld(entry.SpeakerWorld, out var speaker) ? speaker : "";
    }

    private static string Hop(string needed, string current)
    {
        if (DataCenters.SameCenter(needed, current) && DataCenters.TryGroup(needed, out var center))
            return $"Server hop to {needed} first. You are on {current} ({center}). Visit Another World Server from Limsa Lominsa, Gridania, or Ul'dah.";
        var from = DataCenters.TryGroup(current, out var here) ? $"{current} ({here})" : current;
        var to = DataCenters.TryGroup(needed, out var there) ? $"{needed} ({there})" : needed;
        return $"Server hop to {to} first. You are on {from}. Log out and choose Visit Another Data Center.";
    }
}
