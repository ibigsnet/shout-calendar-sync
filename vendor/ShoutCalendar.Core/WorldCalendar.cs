namespace ShoutCalendar.Core;

public static class PlayableWorlds
{
    private static readonly HashSet<string> DataCenters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aether", "Primal", "Crystal", "Dynamis", "Chaos", "Light",
        "Elemental", "Gaia", "Mana", "Meteor", "Materia",
    };

    public static IReadOnlyList<string> All { get; } =
        ServerNames.All.Where(name => !DataCenters.Contains(name)).ToArray();

    public static bool TryCanonical(string? name, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(name))
            return false;
        foreach (var world in All)
        {
            if (!world.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            canonical = world;
            return true;
        }

        return false;
    }

    public static bool TryNamedWorld(string? serverField, out string world)
    {
        world = "";
        if (string.IsNullOrWhiteSpace(serverField))
            return false;
        foreach (var part in serverField.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryCanonical(part, out world))
                return true;
        }

        return false;
    }
}

public sealed class WorldCalendar
{
    private readonly HashSet<string> extras = new(StringComparer.Ordinal);
    private readonly HashSet<string> viewing = new(StringComparer.Ordinal);
    private string lastPicked = "";

    public WorldCalendar(string currentWorld)
    {
        if (!PlayableWorlds.TryCanonical(currentWorld, out var home))
            throw new ArgumentException("The current world is not a playable world.", nameof(currentWorld));
        this.Home = home;
        this.Here = home;
        this.Selected = home;
        this.lastPicked = home;
        this.viewing.Add(home);
    }

    public string Home { get; }

    public string Here { get; private set; }

    public string Selected { get; private set; }

    public void Notice(string? world)
    {
        if (PlayableWorlds.TryCanonical(world, out var here))
            this.Here = here;
    }

    public bool IsChecked(string world)
    {
        if (!PlayableWorlds.TryCanonical(world, out var canonical))
            return false;
        return canonical == this.Home || this.extras.Contains(canonical);
    }

    public void SetChecked(string world, bool on)
    {
        if (!PlayableWorlds.TryCanonical(world, out var canonical) || canonical == this.Home)
            return;
        if (on)
        {
            this.extras.Add(canonical);
            return;
        }

        this.extras.Remove(canonical);
        this.viewing.Remove(canonical);
        if (this.viewing.Count == 0)
            this.viewing.Add(this.Home);
        if (this.Selected == canonical)
            this.Selected = this.Home;
    }

    public bool IsViewing(string world)
    {
        if (!PlayableWorlds.TryCanonical(world, out var canonical))
            return false;
        return this.viewing.Contains(canonical);
    }

    public void SetViewing(string world, bool on)
    {
        if (!PlayableWorlds.TryCanonical(world, out var canonical) || !this.IsChecked(canonical))
            return;
        if (on)
        {
            this.viewing.Add(canonical);
            this.Selected = canonical;
            this.lastPicked = canonical;
            return;
        }

        if (this.viewing.Count <= 1 && this.viewing.Contains(canonical))
            return;
        this.viewing.Remove(canonical);
        this.KeepSelection();
    }

    public void SetViewDataCenter(string dataCenter, bool on)
    {
        foreach (var group in DataCenters.All)
        {
            if (!group.Name.Equals(dataCenter, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var world in group.Worlds)
            {
                if (!this.IsChecked(world) || !PlayableWorlds.TryCanonical(world, out var canonical))
                    continue;
                if (on)
                    this.viewing.Add(canonical);
                else
                    this.viewing.Remove(canonical);
            }

            if (this.viewing.Count == 0)
                this.RestoreLastPicked();
            else
                this.KeepSelection();
            return;
        }
    }

    public string LastPicked => this.lastPicked;

    public void RememberPick(string world)
    {
        if (PlayableWorlds.TryCanonical(world, out var canonical) && this.IsChecked(canonical))
            this.lastPicked = canonical;
    }

    private void KeepSelection()
    {
        if (this.viewing.Contains(this.Selected))
            return;
        if (PlayableWorlds.TryCanonical(this.lastPicked, out var picked) && this.viewing.Contains(picked))
        {
            this.Selected = picked;
            return;
        }

        this.Selected = this.Viewing().FirstOrDefault() ?? this.Home;
    }

    private void RestoreLastPicked()
    {
        var restore = this.Home;
        if (PlayableWorlds.TryCanonical(this.lastPicked, out var picked) && this.IsChecked(picked))
            restore = picked;
        this.viewing.Add(restore);
        this.Selected = restore;
    }

    public bool DataCenterViewed(string dataCenter)
    {
        var any = false;
        foreach (var group in DataCenters.All)
        {
            if (!group.Name.Equals(dataCenter, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var world in group.Worlds)
            {
                if (!this.IsChecked(world))
                    continue;
                any = true;
                if (!this.IsViewing(world))
                    return false;
            }

            return any;
        }

        return false;
    }

    public IReadOnlyList<string> Viewing()
    {
        var list = new List<string>();
        foreach (var world in PlayableWorlds.All)
        {
            if (this.viewing.Contains(world))
                list.Add(world);
        }

        return list;
    }

    public void UseView(IEnumerable<string>? worlds)
    {
        var next = new List<string>();
        foreach (var world in worlds ?? [])
        {
            if (PlayableWorlds.TryCanonical(world, out var canonical) && this.IsChecked(canonical))
                next.Add(canonical);
        }

        if (next.Count == 0)
            return;
        this.viewing.Clear();
        foreach (var world in next)
            this.viewing.Add(world);
        this.KeepSelection();
    }

    public void SetDataCenter(string dataCenter, bool on)
    {
        foreach (var group in DataCenters.All)
        {
            if (!group.Name.Equals(dataCenter, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var world in group.Worlds)
                this.SetChecked(world, on);
            return;
        }
    }

    public bool DataCenterChecked(string dataCenter)
    {
        foreach (var group in DataCenters.All)
        {
            if (!group.Name.Equals(dataCenter, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var world in group.Worlds)
            {
                if (!this.IsChecked(world))
                    return false;
            }

            return group.Worlds.Count > 0;
        }

        return false;
    }

    public IReadOnlyList<string> Extras()
    {
        var list = new List<string>();
        foreach (var world in PlayableWorlds.All)
        {
            if (this.extras.Contains(world))
                list.Add(world);
        }

        return list;
    }

    public IReadOnlyList<string> Selectable()
    {
        var list = new List<string> { this.Home };
        list.AddRange(this.Extras());
        return list;
    }

    public IReadOnlyList<string> Fetched() => this.Selectable();

    public bool Select(string world)
    {
        if (!PlayableWorlds.TryCanonical(world, out var canonical))
            return false;
        if (!this.IsChecked(canonical))
            return false;
        this.viewing.Clear();
        this.viewing.Add(canonical);
        this.Selected = canonical;
        this.lastPicked = canonical;
        return true;
    }

    public IReadOnlyList<SyncAnnouncement> Visible(IEnumerable<SyncAnnouncement> events)
    {
        var open = new HashSet<string>(this.Viewing(), StringComparer.OrdinalIgnoreCase);
        return events.Where(item => open.Contains(item.World)).ToArray();
    }

    public bool SharesPending(string world, bool showAll, string? standing)
    {
        if (!PlayableWorlds.TryCanonical(world, out var canonical))
            return true;
        if (PlayableWorlds.TryCanonical(standing, out var here) && here.Equals(canonical, StringComparison.OrdinalIgnoreCase))
            return true;
        return showAll && this.IsChecked(canonical);
    }

    public bool DrawnPending(string world, bool showAll, string? standing)
    {
        if (!this.SharesPending(world, showAll, standing))
            return false;
        if (!PlayableWorlds.TryCanonical(world, out var canonical))
            return true;
        if (PlayableWorlds.TryCanonical(standing, out var here) && here.Equals(canonical, StringComparison.OrdinalIgnoreCase))
            return true;
        return this.IsViewing(canonical);
    }

    public bool PendingVisible(string world, PendingScope scope, string? standing)
    {
        if (!PlayableWorlds.TryCanonical(world, out var canonical)) return true;
        var here = PlayableWorlds.TryCanonical(standing, out var current) && current == canonical;
        return scope switch
        {
            PendingScope.CurrentWorld => here,
            PendingScope.OpenCalendars => this.IsChecked(canonical) && this.IsViewing(canonical),
            PendingScope.SyncedWorlds => this.IsChecked(canonical),
            _ => false,
        };
    }

    public bool ShowsLocal(CalendarEntry entry)
    {
        if (entry.Manual)
            return true;
        if (ServerNames.TryAdvertised(entry.EventText, out var advertised))
            return this.IsViewing(advertised);
        var named = false;
        if (!string.IsNullOrWhiteSpace(entry.Server))
        {
            foreach (var part in entry.Server.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!PlayableWorlds.TryCanonical(part, out var world))
                    continue;
                named = true;
                if (this.IsViewing(world))
                    return true;
            }
        }

        if (named)
            return false;
        if (PlayableWorlds.TryCanonical(entry.SpeakerWorld, out var from) && this.IsViewing(from))
            return true;
        return this.IsViewing(this.Home);
    }

    public bool ShowsOnOpenCalendar(CalendarEntry entry, bool includeCurrentServer, string? standing)
    {
        if (this.ShowsLocal(entry))
            return true;
        if (!includeCurrentServer)
            return false;
        return PlayableWorlds.TryCanonical(standing, out var here) && this.IsViewing(here);
    }

    public void ClearExtras()
    {
        this.extras.Clear();
        this.viewing.Clear();
        this.viewing.Add(this.Home);
        this.Selected = this.Home;
    }
}
