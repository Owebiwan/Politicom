using System.Text.Json.Serialization;

namespace Politicom.Sim;

public enum CommuneKind { Agrarian, Industrial, Mixed }

/// <summary>Each axis runs −1..1; positive means collectivist, militant, federalist.</summary>
public readonly record struct CommuneValues(double Collectivism, double Militancy, double Federalism)
{
    /// <summary>0 = identical values, 1 = opposite on every axis.</summary>
    public double Distance(CommuneValues o) =>
        (Math.Abs(Collectivism - o.Collectivism) + Math.Abs(Militancy - o.Militancy) + Math.Abs(Federalism - o.Federalism)) / 6.0;

    public static double To01(double axis) => (axis + 1) / 2;
}

/// <summary>
/// A commune or syndicate. It acts on its own every tick; the player only changes
/// the conditions it acts in.
/// </summary>
public sealed class Commune
{
    public Commune(int id, string name, CommuneKind kind)
    {
        Id = id;
        Name = name;
        Kind = kind;
    }

    public int Id { get; }
    public string Name { get; }
    public CommuneKind Kind { get; }
    public string Description { get; set; } = "";

    public double Longitude { get; set; }
    public double Latitude { get; set; }
    /// <summary>Map position normalised to 0..1 (north is up, Y grows downward).</summary>
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Thousands of people.</summary>
    public double Population { get; set; }
    /// <summary>Refugees sheltering here (thousands, counted in Population), settling in over months.</summary>
    public double Refugees { get; set; }
    /// <summary>Its people killed in the fighting, since the start (thousands).</summary>
    public double WarDead { get; set; }
    public double Food { get; set; }
    public double Goods { get; set; }
    public CommuneValues Values { get; set; }

    public double PracticalEducation { get; set; }
    public double PoliticalEducation { get; set; }
    /// <summary>Share of labor spent on schooling, per track.</summary>
    public double PracticalSchooling { get; set; }
    public double PoliticalSchooling { get; set; }

    public double Wellbeing { get; set; } = 0.6;
    public bool Federated { get; set; } = true;
    public bool Occupied { get; set; }
    /// <summary>Indices of the enemies this commune faces. Empty = behind the lines.</summary>
    public HashSet<int> Fronts { get; init; } = new();
    [JsonIgnore] public bool Front => Fronts.Count > 0;
    /// <summary>Index of the enemy occupying it, if any.</summary>
    [JsonInclude] public int? OccupiedBy { get; internal set; }
    /// <summary>1 = fully held; at 0 the commune is occupied.</summary>
    public double Control { get; set; } = 1;
    public HashSet<string> Techs { get; init; } = new();
    /// <summary>Permanent bonus from completed collectivization projects.</summary>
    public double ProductivityBonus { get; set; }
    /// <summary>0..1: how far a rival party's cells have taken hold here.</summary>
    public double PartyInfluence { get; set; }
    /// <summary>Share of its people in the militia.</summary>
    public double Mobilization { get; set; }
    /// <summary>0..1: cultural influence of the movement's ideas here (the press, schools, songs).</summary>
    public double Culture { get; set; }
    /// <summary>Turn of the last cultural work founded here.</summary>
    public int LastCulturalWork { get; set; } = -999;
    /// <summary>Earthworks, barricades and trenches (0..1): defence at the front. Worn down while the front gives way.</summary>
    public double Fortification { get; set; }
    /// <summary>Turns of fortification work still to come.</summary>
    public int Fortifying { get; set; }
    /// <summary>Turn of the commune's last raid on the enemy.</summary>
    public int LastRaid { get; set; } = -999;
    /// <summary>A raid's chance of success: better the more firmly the commune holds its line.</summary>
    [JsonIgnore] public double RaidChance => Math.Clamp(0.2 + 0.4 * Math.Min(2, Defense / Math.Max(0.01, Pressure)), 0.15, 0.85);
    /// <summary>Research produced last turn.</summary>
    [JsonInclude] public double Science { get; internal set; }

    // Decaying ledgers used to spot free riders.
    public double AidGiven { get; set; }
    public double AidReceived { get; set; }

    // Derived each tick; kept for UI and AI.
    [JsonInclude] public double FoodProduced { get; internal set; }
    /// <summary>Last turn's change in population, by cause (thousands): births over deaths, famine, the war, refugees, people moving.</summary>
    [JsonInclude] public List<Part> PopulationChange { get; internal set; } = new();
    /// <summary>Deaths from hunger this turn, set by the economy for PopulationSystem's account.</summary>
    [JsonIgnore] internal double FamineDead { get; set; }
    [JsonInclude] public double GoodsProduced { get; internal set; }
    [JsonInclude] public bool Starving { get; internal set; }
    [JsonInclude] public bool GoodsShort { get; internal set; }
    /// <summary>Share of this week's goods need (including arms for the front) that was met.</summary>
    [JsonInclude] public double GoodsCoverage { get; internal set; } = 1;
    [JsonInclude] public double FreeRiding { get; internal set; }
    [JsonInclude] public double Discontent { get; internal set; }
    [JsonInclude] public double Defense { get; internal set; }
    [JsonInclude] public double Pressure { get; internal set; }

    [JsonIgnore] public bool InFederation => Federated && !Occupied;
    /// <summary>The expert trap: practical education running ahead of political.</summary>
    [JsonIgnore] public double ExpertGap => Math.Max(0, PracticalEducation - PoliticalEducation);

    public override string ToString() => Name;
}
