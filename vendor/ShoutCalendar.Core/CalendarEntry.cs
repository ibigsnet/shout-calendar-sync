using System.Numerics;

namespace ShoutCalendar.Core;

public sealed record CalendarEntry(
    DateOnly? Date,
    TimeOnly? Time,
    TimeOnly? End,
    int? Ward,
    string? Server,
    string Place,
    string EventText,
    string Sender,
    bool Accepted,
    string Id,
    DateTimeOffset DetectedAt,
    EventRepeat? Repeat = null,
    int Channel = 0,
    bool NoteUpdated = false,
    bool Manual = false,
    string SpeakerWorld = "",
    Vector4? Color = null,
    bool Hidden = false,
    bool Pinned = false,
    DateTimeOffset? StartUtc = null,
    DateTimeOffset? EndUtc = null,
    string SourceTimeZone = "",
    int Revision = 1,
    DateOnly[]? ExcludedDates = null,
    DateOnly? RepeatUntil = null,
    bool SeriesDeleted = false);
