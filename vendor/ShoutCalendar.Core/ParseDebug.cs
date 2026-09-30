using System.Globalization;

namespace ShoutCalendar.Core;

public static class ParseDebug
{
    public static string Line(CalendarEntry entry)
    {
        var title = EventTitle.Readable(EventTitle.Choose(entry.EventText));
        if (title.Length == 0)
            title = "Invite";
        var when = entry.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "no date";
        if (entry.Time is TimeOnly time)
        {
            when += " " + (entry.End is TimeOnly end
                ? $"{time.ToString("HH:mm", CultureInfo.InvariantCulture)}-{end.ToString("HH:mm", CultureInfo.InvariantCulture)}"
                : time.ToString("HH:mm", CultureInfo.InvariantCulture));
        }

        var spot = HousingTravel.FindVenue(entry.Place, entry.EventText, entry.Ward, entry.Server, entry.SpeakerWorld);
        var place = spot is HousingSpot housing ? housing.Label : (entry.Place ?? "").Trim();
        var line = $"Shout Calendar: [Debug] Kept {title}. {when}.";
        if (place.Length > 0)
            line += " " + place + ".";
        return line;
    }
}
