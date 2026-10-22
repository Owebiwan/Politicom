using System.Text.Json.Serialization;

namespace Politicom.Sim.Global;

/// <summary>Regimes. Empire, Tribal and Theocracy are for the earlier ages.</summary>
public enum Ideology { Liberal, Conservative, Fascist, Communist, Monarchy, Libertarian, Empire, Tribal, Theocracy }

/// <summary>A state as the scenario starts it: from the era's world map plus scenario overrides.</summary>
public sealed class StateDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Ideology Ideology { get; set; }
    /// <summary>Military and economic weight: 1 for small states, up to 10 for the great powers.</summary>
    public double Power { get; set; } = 1;
    /// <summary>How much the state cares about the federation: 1 next door, falling with distance.</summary>
    public double Proximity { get; set; }
    public double InitialStance { get; set; }
    public string[] Neighbours { get; set; } = Array.Empty<string>();
    /// <summary>Its largest cities (home country first): where communes appear if it federates.</summary>
    public List<City> Cities { get; set; } = new();
}

/// <summary>What a state is currently doing about the federation.</summary>
[Flags]
public enum StateActions
{
    None = 0,
    Embargo = 1,
    ArmsSales = 2,
    Volunteers = 4,
    Intervention = 8,
}

/// <summary>
/// An AI state on the world map. An aggregate, not a simulation of its own society: a stance
/// toward the federation, the actions that stance leads to, and how radicalized its workers are.
/// </summary>
public sealed class WorldState
{
    public string Id { get; init; } = "";
    public string Name { get; set; } = "";
    public Ideology Ideology { get; set; }
    public double Power { get; init; }
    public double Proximity { get; init; }
    public string[] Neighbours { get; init; } = Array.Empty<string>();

    /// <summary>−1 (hostile) to 1 (friendly).</summary>
    public double Stance { get; set; }
    /// <summary>0..1: how radicalized its workers are.</summary>
    public double Radicalization { get; set; }
    [JsonInclude] public StateActions Actions { get; internal set; }
    /// <summary>0..1: how hard the state cracks down; raised by a crushed revolution, fading slowly.</summary>
    public double Repression { get; set; }
    /// <summary>Turns left in a revolution under way (0 = none).</summary>
    public int Uprising { get; set; }
    /// <summary>Extra chance of success from the movement's support for an uprising under way.</summary>
    public double UprisingSupport { get; set; }
    /// <summary>0..1: how worn down by war it is.</summary>
    public double Weariness { get; set; }
    /// <summary>Revolutions that have won here.</summary>
    public int RevolutionCount { get; set; }
    /// <summary>0..1: how advanced its industry and science are, within the age.</summary>
    public double TechLevel { get; set; }
    /// <summary>Its cities have joined the federation.</summary>
    public bool Federated { get; set; }
    /// <summary>A libertarian revolution won here: a sister federation.</summary>
    [JsonIgnore] public bool Sister => Ideology == Ideology.Libertarian;

    /// <summary>
    /// 0..1: how strongly its own culture (nation, church, party) resists the movement's ideas
    /// among its workers. Strong, ideological states resist most.
    /// </summary>
    [JsonIgnore] public double CulturalWeight => Math.Clamp(Power / 10 * Ideology switch
    {
        Ideology.Fascist => 1.0,
        Ideology.Communist => 0.9,
        Ideology.Monarchy => 0.8,
        Ideology.Empire => 0.8,
        Ideology.Theocracy => 1.0,
        Ideology.Tribal => 0.3,
        Ideology.Conservative => 0.6,
        Ideology.Liberal => 0.5,
        _ => 0,
    }, 0, 1);

    /// <summary>Power that counts toward the federation, given distance.</summary>
    [JsonIgnore] public double Weight => Power * Proximity;

    public bool Does(StateActions a) => (Actions & a) != 0;
}

public static class StateText
{
    public static string Name(Ideology i) => i switch
    {
        Ideology.Liberal => "Liberal",
        Ideology.Conservative => "Conservative",
        Ideology.Fascist => "Fascist",
        Ideology.Communist => "Authoritarian communist",
        Ideology.Monarchy => "Monarchy",
        Ideology.Empire => "Empire",
        Ideology.Tribal => "Stateless peoples",
        Ideology.Theocracy => "Theocracy",
        _ => "Libertarian",
    };

    public static string Stance(double s) => s switch
    {
        < -0.6 => "hostile",
        < -0.2 => "unfriendly",
        <= 0.2 => "neutral",
        <= 0.5 => "sympathetic",
        _ => "friendly",
    };

    private static readonly Dictionary<string, string> Countries = new()
    {
        ["French"] = "France", ["British"] = "Britain", ["German"] = "Germany", ["Spanish"] = "Spain",
        ["Portuguese"] = "Portugal", ["Russian"] = "Russia", ["Soviet"] = "Russia", ["Ottoman"] = "Turkey",
        ["Mongolian"] = "Mongolia", ["Ukrainian"] = "Ukraine", ["Hungarian"] = "Hungary", ["Qing"] = "China",
        ["Austria-Hungary"] = "Austria-Hungary", ["German-Austria"] = "Austria",
    };

    /// <summary>The country's name without its regime ("French Republic" → "France"), for naming what replaces it.</summary>
    public static string Country(string name)
    {
        foreach (var prefix in new[] { "Kingdom of ", "Empire of ", "Federal State of ", "Republic of ", "Union of ", "Principality of ",
                                       "National State of ", "People's Republic of ", "Confederation of ", "Free Communes of ", "Workers' Republic of ", "Brotherhood of ", "Commonwealth of ",
                                       "Khedivate of ", "Democratic Republic of ", "United States of ", "Kingdom of Serbs, Croats and Slovenes" })
            if (name.StartsWith(prefix)) return prefix.StartsWith("Kingdom of Serbs") ? "Yugoslavia" : name[prefix.Length..];
        name = name.Replace(" (Versailles)", "");
        foreach (var suffix in new[] { " People's Republic", " Democratic Republic", " Republic", " Empire", " Reich", " State", " Union" })
            if (name.EndsWith(suffix)) name = name[..^suffix.Length];
        return Countries.TryGetValue(name, out var country) ? country : name.StartsWith("The ") ? name[4..] : name;
    }

    public static string Actions(StateActions a)
    {
        var parts = new List<string>();
        if ((a & StateActions.Intervention) != 0) parts.Add("intervening against you");
        if ((a & StateActions.Embargo) != 0) parts.Add("embargo");
        if ((a & StateActions.ArmsSales) != 0) parts.Add("selling arms");
        if ((a & StateActions.Volunteers) != 0) parts.Add("volunteers");
        return parts.Count == 0 ? "watching" : string.Join(", ", parts);
    }
}

/// <summary>Builds the scenario's states from the era's world map.</summary>
public static class WorldSetup
{
    /// <summary>Starting stance by ideology, before scenario overrides.</summary>
    public static double BaseStance(Ideology i) => i switch
    {
        Ideology.Liberal => -0.05,
        Ideology.Conservative => -0.3,
        Ideology.Fascist => -0.75,
        Ideology.Monarchy => -0.35,
        Ideology.Empire => -0.45,
        Ideology.Theocracy => -0.6,
        Ideology.Tribal => 0.15, // no state to defend: curious, sometimes kin
        Ideology.Communist => 0.1,
        _ => 0.8,
    };

    public static Ideology Parse(string ideology) => Enum.TryParse<Ideology>(ideology, true, out var i) ? i : Ideology.Conservative;

    /// <summary>
    /// Fills <see cref="ScenarioDefinition.States"/> from the atlas, unless the scenario already
    /// has states (a generated map, or a scenario embedded in a save). The home state is the
    /// federation's own country, so it isn't an outside actor.
    /// </summary>
    public static void Attach(ScenarioDefinition scenario, Atlas atlas)
    {
        if (scenario.States.Count > 0 || scenario.HomeState is null) return;
        double lon = scenario.Communes.Average(c => c.Lon), lat = scenario.Communes.Average(c => c.Lat);
        var home = atlas.State(scenario.HomeState);
        var homeNeighbours = new HashSet<string>(home?.Neighbours ?? Array.Empty<string>());
        foreach (var s in atlas.States)
        {
            if (s.Id == scenario.HomeState) continue;
            var ideology = Parse(s.Ideology);
            double km = s.Capital is { } c ? DistanceKm(lon, lat, c.Lon, c.Lat) : 5000;
            scenario.States.Add(new StateDefinition
            {
                Id = s.Id,
                Name = s.Name,
                Ideology = ideology,
                Power = s.Power,
                Proximity = homeNeighbours.Contains(s.Id) ? 1 : Math.Round(1 / (1 + km / 1500), 3),
                InitialStance = scenario.Stances.TryGetValue(s.Id, out var stance) ? stance : BaseStance(ideology),
                Neighbours = s.Neighbours.ToArray(),
                Cities = s.Cities.Take(6).ToList(),
            });
        }
    }

    public static double DistanceKm(double lon1, double lat1, double lon2, double lat2)
    {
        double r = Math.PI / 180;
        double dLat = (lat2 - lat1) * r, dLon = (lon2 - lon1) * r;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * r) * Math.Cos(lat2 * r) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 6371 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
