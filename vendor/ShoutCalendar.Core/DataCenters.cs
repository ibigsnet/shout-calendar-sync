namespace ShoutCalendar.Core;

public static class DataCenters
{
    public static readonly IReadOnlyList<Group> All =
    [
        new("Aether", ["Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren"]),
        new("Primal", ["Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros"]),
        new("Crystal", ["Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera"]),
        new("Dynamis", ["Cuchulainn", "Golem", "Halicarnassus", "Kraken", "Maduin", "Marilith", "Rafflesia", "Seraph"]),
        new("Chaos", ["Cerberus", "Louisoix", "Moogle", "Omega", "Phantom", "Ragnarok", "Sagittarius", "Spriggan"]),
        new("Light", ["Alpha", "Lich", "Odin", "Phoenix", "Raiden", "Shiva", "Twintania", "Zodiark"]),
        new("Elemental", ["Aegis", "Atomos", "Carbuncle", "Garuda", "Gungnir", "Kujata", "Tonberry", "Typhon"]),
        new("Gaia", ["Alexander", "Bahamut", "Durandal", "Fenrir", "Ifrit", "Ridill", "Tiamat", "Ultima"]),
        new("Mana", ["Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan"]),
        new("Meteor", ["Belias", "Mandragora", "Ramuh", "Shinryu", "Unicorn", "Valefor", "Yojimbo", "Zeromus"]),
        new("Materia", ["Bismarck", "Ravana", "Sephirot", "Sophia", "Zurvan"]),
    ];

    public readonly record struct Group(string Name, IReadOnlyList<string> Worlds);

    public static bool IsName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && All.Any(group => group.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool TryGroup(string? world, out string group)
    {
        group = "";
        if (string.IsNullOrWhiteSpace(world))
            return false;
        foreach (var center in All)
        {
            foreach (var name in center.Worlds)
            {
                if (!name.Equals(world.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                group = center.Name;
                return true;
            }
        }

        return false;
    }

    public static string SharedOn(IEnumerable<string> viewing)
    {
        var open = new HashSet<string>(viewing.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        var all = new List<string>();
        var most = new List<string>();
        var except = new List<string>();
        var listed = new List<string>();
        var grouped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in All)
        {
            var selected = group.Worlds.Where(open.Contains).ToList();
            if (selected.Count == 0)
                continue;
            foreach (var name in selected)
                grouped.Add(name);
            var missing = group.Worlds.Where(name => !open.Contains(name)).ToList();
            if (missing.Count == 0)
                all.Add(group.Name);
            else if (missing.Count <= 2 && selected.Count >= group.Worlds.Count - 2)
                except.Add(missing.Count == 1
                    ? $"all of {group.Name} except {missing[0]}"
                    : $"all of {group.Name} except {Join(missing)}");
            else if (selected.Count * 2 > group.Worlds.Count)
                most.Add(group.Name);
            else
                listed.AddRange(selected);
        }

        foreach (var name in open)
        {
            if (!grouped.Contains(name))
                listed.Add(name);
        }

        var parts = new List<string>();
        if (all.Count > 0)
            parts.Add("all of " + Join(all));
        if (most.Count > 0)
            parts.Add("most of " + Join(most));
        parts.AddRange(except);
        if (listed.Count > 0)
            parts.Add(Join(listed));
        return parts.Count == 0 ? "Shared on no worlds" : "Shared on " + Join(parts);
    }

    public static string ViewingLabel(IEnumerable<string> viewing)
    {
        var summary = SharedOn(viewing);
        const string lead = "Shared on ";
        var worlds = summary.StartsWith(lead, StringComparison.Ordinal) ? summary[lead.Length..] : summary;
        return "Viewing calendars: Local + Sync: " + worlds + " · details";
    }

    private static string Join(IReadOnlyList<string> names)
    {
        if (names.Count <= 1)
            return names.Count == 0 ? "" : names[0];
        if (names.Count == 2)
            return names[0] + " and " + names[1];
        return string.Join(", ", names.Take(names.Count - 1)) + ", and " + names[^1];
    }

    public static bool SameCenter(string? left, string? right) =>
        TryGroup(left, out var first) && TryGroup(right, out var second) && first.Equals(second, StringComparison.OrdinalIgnoreCase);

    public static string TravelLine(string needed, string current)
    {
        if (SameCenter(needed, current) && TryGroup(needed, out var center))
            return $"that plot is on {needed}. You are on {current} ({center}). Use the main aetheryte in Limsa Lominsa, Gridania, or Ul'dah and choose Visit Another World Server. City shards and other cities cannot switch worlds.";
        var from = TryGroup(current, out var here) ? $"{current} ({here})" : current;
        var to = TryGroup(needed, out var there) ? $"{needed} ({there})" : needed;
        return $"that plot is on {to}. You are on {from}. Log out to the character list and choose Visit Another Data Center.";
    }
}
