using System.Numerics;

namespace ShoutCalendar.Core;

public sealed class CalendarLog
{
    private readonly List<CalendarEntry> entries = new();

    public IReadOnlyList<CalendarEntry> Entries => this.entries;

    public int Revision { get; private set; }

    private void Touch() => this.Revision++;

    public bool Add(CalendarEntry? entry)
    {
        if (entry is null)
            return false;
        entry = Stamp(entry);
        var when = entry.DetectedAt == default ? DateTimeOffset.UtcNow : entry.DetectedAt;
        var context = this.entries.Where(row => !row.Manual).Select(row => SyncAnnouncement.FromLocal(row, row.SpeakerWorld)).ToArray();
        var candidate = SyncAnnouncement.FromLocal(entry, entry.SpeakerWorld);
        var same = this.entries.FindIndex(row =>
            EventIdentity.SameRepost(row, entry)
            || (!row.Manual && !entry.Manual && SyncMerge.CanMergeAmong(context, SyncAnnouncement.FromLocal(row, row.SpeakerWorld), candidate))
            || EventIdentity.SameEntry(row, entry)
            || EventIdentity.SameSpeaker(row, entry, when));
        if (same >= 0)
        {
            this.entries[same] = Stamp(Merge(this.entries[same], entry));
            this.Touch();
            return true;
        }

        if (string.IsNullOrEmpty(entry.Id))
            entry = entry with { Id = Guid.NewGuid().ToString("N"), Accepted = false };
        this.entries.Add(Stamp(entry));
        this.Touch();
        return true;
    }

    public int FoldReposts()
    {
        var removed = 0;
        var context = this.entries.Where(row => !row.Manual).Select(row => SyncAnnouncement.FromLocal(row, row.SpeakerWorld)).ToArray();
        for (var i = 0; i < this.entries.Count; i++)
        {
            for (var j = i + 1; j < this.entries.Count;)
            {
                var left = this.entries[i];
                var right = this.entries[j];
                if (left.Manual || right.Manual || !(EventIdentity.SameRepost(left, right)
                    || SyncMerge.CanMergeAmong(context, SyncAnnouncement.FromLocal(left, left.SpeakerWorld), SyncAnnouncement.FromLocal(right, right.SpeakerWorld))))
                {
                    j++;
                    continue;
                }

                this.entries[i] = Merge(this.entries[i], this.entries[j]);
                this.entries.RemoveAt(j);
                removed++;
                context = this.entries.Where(row => !row.Manual).Select(row => SyncAnnouncement.FromLocal(row, row.SpeakerWorld)).ToArray();
                j = i + 1;
            }
        }

        if (removed > 0)
            this.Touch();
        return removed;
    }

    public int Absorb(IEnumerable<SyncAnnouncement> shared)
    {
        var changed = 0;
        var updates = shared.ToArray();
        var context = updates.Concat(this.entries.Where(row => !row.Manual).Select(row => SyncAnnouncement.FromLocal(row, row.SpeakerWorld))).ToArray();
        foreach (var item in updates)
        {
            if (string.IsNullOrWhiteSpace(item.Text))
                continue;
            var incoming = FromShared(item);
            var index = this.entries.FindIndex(row => !row.Manual &&
                (EventIdentity.SameRepost(row, incoming) || SyncMerge.CanMergeAmong(context, SyncAnnouncement.FromLocal(row, row.SpeakerWorld), item)));
            if (index < 0)
                continue;
            var merged = Merge(this.entries[index], incoming);
            if (merged == this.entries[index])
                continue;
            this.entries[index] = merged;
            changed++;
        }

        if (changed > 0)
            this.Touch();
        return changed;
    }

    public int Reharvest(Func<CalendarEntry, CalendarEntry?> read)
    {
        var changed = 0;
        for (var i = 0; i < this.entries.Count; i++)
        {
            var current = this.entries[i];
            if (current.Manual || current.NoteUpdated || string.IsNullOrWhiteSpace(current.EventText))
                continue;
            var next = read(current);
            if (next is null)
                continue;
            var refreshClocks = ZoneClock.TryNowUntil(current.EventText, out _);
            var updated = current with
            {
                Date = refreshClocks ? next.Date ?? current.Date : current.Date ?? next.Date,
                Time = refreshClocks ? next.Time ?? current.Time : current.Time ?? next.Time,
                End = refreshClocks ? next.End ?? current.End : current.End ?? next.End,
                Ward = current.Ward ?? next.Ward,
                Server = string.IsNullOrWhiteSpace(current.Server) ? next.Server : current.Server,
                Place = PreferPlace(current.Place, next.Place),
            };
            if (updated == current)
                continue;
            this.entries[i] = updated;
            changed++;
        }

        if (changed > 0)
            this.Touch();
        return changed;
    }

    private static CalendarEntry Merge(CalendarEntry current, CalendarEntry incoming)
    {
        var previous = SyncAnnouncement.FromLocal(current, current.SpeakerWorld);
        var update = SyncAnnouncement.FromLocal(incoming, incoming.SpeakerWorld);
        update.Id = previous.Id;
        previous.ClockEditedLocally = current.NoteUpdated;
        SyncMerge.PreferBody(previous, update);
        var wire = update;
        var clock = SyncClock.Entry(wire);
        var newer = incoming.DetectedAt > current.DetectedAt || incoming.Revision > current.Revision;
        return current with
        {
            EventText = current.NoteUpdated ? current.EventText : wire.Text,
            Date = clock.Date, Time = clock.Time, End = clock.End,
            StartUtc = clock.StartUtc, EndUtc = clock.EndUtc, SourceTimeZone = clock.SourceTimeZone,
            Repeat = clock.Repeat ?? current.Repeat,
            ExcludedDates = incoming.ExcludedDates is { Length: > 0 }
                ? (current.ExcludedDates ?? []).Concat(incoming.ExcludedDates).Distinct().ToArray() : current.ExcludedDates,
            SeriesDeleted = current.SeriesDeleted || incoming.SeriesDeleted,
            RepeatUntil = incoming.RepeatUntil is DateOnly until && (current.RepeatUntil is null || until < current.RepeatUntil)
                ? until : current.RepeatUntil,
            Revision = Math.Max(current.Revision, incoming.Revision),
            Ward = newer ? incoming.Ward ?? current.Ward : current.Ward ?? incoming.Ward,
            Server = wire.World.Length > 0 ? wire.World : current.Server,
            Place = current.NoteUpdated ? current.Place : wire.Place,
            Accepted = current.Accepted || incoming.Accepted,
            Pinned = current.Pinned || incoming.Pinned,
            DetectedAt = incoming.DetectedAt > current.DetectedAt ? incoming.DetectedAt : current.DetectedAt,
        };
    }

    private static CalendarEntry FromShared(SyncAnnouncement item)
    {
        return SyncClock.Entry(item) with { Ward = NumberFrom($"{item.Text} {item.Place}") };
    }

    private static int? NumberFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\b(?:ward\s*|[Ww])(\d{1,2})\b");
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }

    private static string PreferPlace(string current, string next)
    {
        if (string.IsNullOrWhiteSpace(current))
            return next ?? "";
        if (string.IsNullOrWhiteSpace(next))
        {
            if (current.Contains("ward", StringComparison.OrdinalIgnoreCase) || current.Contains("plot", StringComparison.OrdinalIgnoreCase))
                return current;
            return "";
        }

        current = DropDataCenterVenue(current, next);
        var nextDistrict = HousingTravel.DistrictName(next);
        var currentDistrict = HousingTravel.DistrictName(current);
        if (nextDistrict is not null && !nextDistrict.Equals(currentDistrict, StringComparison.OrdinalIgnoreCase))
            return next;
        return next.Length >= current.Length ? next : current;
    }

    private static string DropDataCenterVenue(string current, string next)
    {
        var parts = current.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (next.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(part);
                continue;
            }

            var words = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 3 && DataCenters.IsName(words[0]))
                continue;
            kept.Add(part);
        }

        return string.Join(", ", kept);
    }

    public bool Rewrite(string id, CalendarEntry incoming)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0)
            return false;
        var current = this.entries[index];
        this.entries[index] = Stamp(incoming with
        {
            Id = current.Id,
            Accepted = current.Accepted,
            Sender = string.IsNullOrWhiteSpace(incoming.Sender) ? current.Sender : incoming.Sender,
            NoteUpdated = current.NoteUpdated,
        });
        this.Touch();
        return true;
    }

    public bool Revise(string id, string note, string dateText, string timeText, string placeText, DateTimeOffset heardAt, PlaceCatalog places, IReadOnlySet<int> channels)
    {
        var current = this.entries.FirstOrDefault(entry => entry.Id == id);
        return current is not null && this.TryRevise(id, note, dateText, timeText, placeText, current.Color,
            heardAt, places, channels, out _);
    }

    public bool TryRevise(string id, string note, string dateText, string timeText, string placeText, Vector4? color,
        DateTimeOffset heardAt, PlaceCatalog places, IReadOnlySet<int> channels, out ClockInputErrors errors, TimeZoneInfo? zone = null)
    {
        if (!ClockInput.TryParse(dateText, timeText, out var input, out errors)) return false;
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0)
        {
            errors = new ClockInputErrors(Message: "This invite is no longer available.");
            return false;
        }
        zone ??= TimeZoneInfo.Local;
        var current = this.entries[index];
        var clockChanged = input != ClockInput.FromEntry(current, zone);
        var clock = current;
        if (clockChanged)
        {
            if (!input.ValidateZone(zone, out errors)) return false;
            clock = current with
            {
                Date = input.Date, Time = input.Start, End = input.End,
                StartUtc = SyncClock.Instant(input.Date, input.Start, zone.Id),
                EndUtc = SyncClock.Instant(SyncClock.EndDate(input.Date, input.Start, input.End), input.End, zone.Id),
                SourceTimeZone = input.Start is null ? "" : zone.Id,
                Repeat = current.Repeat is not null && input.Date is DateOnly anchor
                    ? current.Repeat with { Weekday = anchor.DayOfWeek } : current.Repeat,
            };
        }
        else if (current.StartUtc is null && current.SourceTimeZone.Length == 0 && !current.NoteUpdated && ZoneClock.Labeled(current.EventText))
        {
            var canonical = SyncClock.Entry(SyncAnnouncement.FromLocal(current, current.Server ?? "", calendarZone: zone), zone);
            clock = current with { Date = canonical.Date, Time = canonical.Time, End = canonical.End,
                StartUtc = canonical.StartUtc, EndUtc = canonical.EndUtc, SourceTimeZone = canonical.SourceTimeZone };
        }

        var parsed = ShoutHarvest.TryHarvest(note, ShoutHarvest.ShoutChannel, heardAt, places, channels, zone: zone);
        this.entries[index] = clock with
        {
            EventText = string.IsNullOrWhiteSpace(note) ? current.EventText : note.Trim(),
            Place = string.IsNullOrWhiteSpace(placeText) ? parsed?.Place ?? "" : placeText.Trim(),
            Ward = parsed?.Ward ?? current.Ward,
            Server = parsed?.Server ?? current.Server,
            Color = color,
            Revision = Math.Min(int.MaxValue - 1, current.Revision + 1),
            NoteUpdated = true,
        };
        this.Touch();
        return true;
    }

    public bool SetColor(string id, Vector4? color)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0)
            return false;
        this.entries[index] = this.entries[index] with { Color = color };
        this.Touch();
        return true;
    }

    public bool Delete(string id, DateOnly occurrence, DeleteScope scope)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0) return false;
        var entry = this.entries[index];
        if (entry.Repeat is null) return this.Remove(id);
        this.entries[index] = EventDeletion.Apply(entry, occurrence, scope);
        this.Touch();
        return true;
    }

    public bool Remove(string id)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0)
            return false;
        this.entries.RemoveAt(index);
        this.Touch();
        return true;
    }

    public bool Accept(string id)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0 || this.entries[index].Accepted)
            return false;
        this.entries[index] = this.entries[index] with { Accepted = true, Hidden = false };
        this.Touch();
        return true;
    }

    public bool SetPinned(string id, bool pinned)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0 || this.entries[index].Pinned == pinned)
            return false;
        this.entries[index] = this.entries[index] with { Pinned = pinned };
        this.Touch();
        return true;
    }

    public bool SetHidden(string id, bool hidden)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0 || this.entries[index].Hidden == hidden)
            return false;
        this.entries[index] = this.entries[index] with { Hidden = hidden };
        this.Touch();
        return true;
    }

    public int SetPendingHidden(bool hidden)
    {
        var count = 0;
        for (var i = 0; i < this.entries.Count; i++)
        {
            if (this.entries[i].Accepted || this.entries[i].Hidden == hidden)
                continue;
            this.entries[i] = this.entries[i] with { Hidden = hidden };
            count++;
        }

        if (count > 0)
            this.Touch();
        return count;
    }

    public int AcceptPending()
    {
        var count = 0;
        for (var i = 0; i < this.entries.Count; i++)
        {
            if (this.entries[i].Accepted)
                continue;
            this.entries[i] = this.entries[i] with { Accepted = true };
            count++;
        }

        if (count > 0)
            this.Touch();
        return count;
    }

    public int ExpireUnaccepted(DateTimeOffset now, int holdDays)
    {
        if (holdDays < 1)
            holdDays = 1;
        var cutoff = now - TimeSpan.FromDays(holdDays);
        var removed = this.entries.RemoveAll(entry => !entry.Accepted && !entry.SeriesDeleted && entry.ExcludedDates is not { Length: > 0 } && entry.RepeatUntil is null && entry.DetectedAt < cutoff);
        if (removed > 0)
            this.Touch();
        return removed;
    }

    public void Clear()
    {
        if (this.entries.Count == 0)
            return;
        this.entries.Clear();
        this.Touch();
    }

    public int ClearAccepted()
    {
        var removed = this.entries.RemoveAll(entry => entry.Accepted);
        if (removed > 0)
            this.Touch();
        return removed;
    }

    public int ClearUnaccepted()
    {
        var removed = this.entries.RemoveAll(entry => !entry.Accepted);
        if (removed > 0)
            this.Touch();
        return removed;
    }

    public int ClearPast(DateTime now, TimeZoneInfo? zone = null)
    {
        var removed = this.entries.RemoveAll(entry => PastEvents.Ended(entry, now, zone));
        if (removed > 0)
            this.Touch();
        return removed;
    }

    private static CalendarEntry Stamp(CalendarEntry entry)
    {
        return entry.DetectedAt == default
            ? entry with { DetectedAt = DateTimeOffset.UtcNow }
            : entry;
    }

    public void Restore(IEnumerable<CalendarEntry> saved)
    {
        foreach (var entry in saved)
        {
            var stored = string.IsNullOrEmpty(entry.Id)
            ? entry with { Id = Guid.NewGuid().ToString("N") }
            : entry;
        this.entries.Add(Stamp(stored));
        }

        this.Touch();
    }
}
