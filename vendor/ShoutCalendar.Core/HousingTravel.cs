using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

public readonly record struct HousingSpot(string District, int? Ward, int? Plot, string? World)
{
    public string Label
    {
        get
        {
            var label = this.District;
            if (this.Ward is int ward)
                label += " ward " + ward.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (this.Plot is int plot)
                label += " plot " + plot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(this.World))
                label += " on " + this.World;
            return label;
        }
    }

    public string City => this.District switch
    {
        "The Lavender Beds" => "New Gridania",
        "The Goblet" => "Ul'dah - Steps of Nald",
        "Mist" => "Limsa Lominsa Lower Decks",
        "Shirogane" => "Kugane",
        "Empyreum" => "Foundation",
        _ => this.District,
    };

    public uint? CityAetheryteId => this.District switch
    {
        "The Lavender Beds" => 2,   // New Gridania
        "The Goblet" => 9,          // Ul'dah - Steps of Nald
        "Mist" => 8,                // Limsa Lominsa Lower Decks
        "Shirogane" => 111,         // Kugane
        "Empyreum" => 70,           // Foundation
        _ => null,
    };

    public uint? WardTerritoryId => this.District switch
    {
        "The Lavender Beds" => 340,
        "The Goblet" => 341,
        "Mist" => 339,
        "Shirogane" => 641,
        "Empyreum" => 979,
        _ => null,
    };

    public uint? CityTerritoryId => this.District switch
    {
        "The Lavender Beds" => 132,
        "The Goblet" => 130,
        "Mist" => 129,
        "Shirogane" => 628,
        "Empyreum" => 418,
        _ => null,
    };
}

public static class HousingTravel
{
    private static readonly (string Token, string Name)[] Districts =
    [
        ("Goblet", "The Goblet"),
        ("Lavender Beds", "The Lavender Beds"),
        ("Lavender", "The Lavender Beds"),
        ("Lav Beds", "The Lavender Beds"),
        ("Lav. Beds", "The Lavender Beds"),
        ("Shirogane", "Shirogane"),
        ("Shiro", "Shirogane"),
        ("Empyreum", "Empyreum"),
        ("Empy", "Empyreum"),
        ("Mist", "Mist"),
        ("Gob", "The Goblet"),
        ("LB", "The Lavender Beds"),
    ];

    public static string? DistrictName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        foreach (var (token, name) in Districts)
        {
            if (HasToken(text, token))
                return name;
        }

        return null;
    }

    private static bool HasToken(string blob, string token)
    {
        var pattern = string.Join(@"\s+", token.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
        return Regex.IsMatch(
            blob,
            $@"(?<![A-Za-z]){pattern}(?![A-Za-z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static readonly Regex PlotRegex = new(
        @"(?:\bplot[\s\-–—·•．.]*#?[\s\-–—·•．.]*|(?<![A-Za-z])[Pp])(?<n>\d{1,2})(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WardRegex = new(
        @"(?:\bward[\s\-–—·•．.]*#?[\s\-–—·•．.]*|(?<![A-Za-z])[Ww])(?<n>\d{1,2})(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static HousingSpot? FindVenue(string? place, string? text, int? ward, string? storedServer, string? speakerWorld, string? heardOn = null)
    {
        var world = ShareWorld.Choose(heardOn, speakerWorld, storedServer, text);
        return Find(place, text, ward, world.Length > 0 ? world : storedServer);
    }

    public static HousingSpot? Find(string? place, string? text, int? ward, string? server)
    {
        var blob = IconText.Plain($"{place} {text}");
        string? district = null;
        foreach (var (token, name) in Districts)
        {
            if (!HasToken(blob, token))
                continue;
            district = name;
            break;
        }

        if (district is null)
            return null;

        int? plot = null;
        var plotMatch = PlotRegex.Match(blob);
        if (plotMatch.Success && int.TryParse(plotMatch.Groups["n"].Value, out var plotNumber) && plotNumber is >= 1 and <= 60)
            plot = plotNumber;
        if (ward is null)
        {
            var wardMatch = WardRegex.Match(blob);
            if (wardMatch.Success && int.TryParse(wardMatch.Groups["n"].Value, out var wardNumber) && wardNumber is >= 1 and <= 30)
                ward = wardNumber;
        }

        string? world = null;
        if (ServerNames.TryAdvertised(blob, out var advertised))
            world = advertised;
        else if (PlayableWorlds.TryNamedWorld(server, out var named))
            world = named;
        else
        {
            foreach (var candidate in ServerNames.Match(blob))
            {
                if (!PlayableWorlds.TryCanonical(candidate, out var playable))
                    continue;
                world = playable;
                break;
            }
        }

        return new HousingSpot(district, ward, plot, world);
    }
}
