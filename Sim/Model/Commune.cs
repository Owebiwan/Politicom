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

    // Decaying ledgers used to spot free riders.
    public double AidGiven { get; set; }
    public double AidReceived { get; set; }

    // Derived each tick; kept for UI and AI.
    [JsonInclude] public double FoodProduced { get; internal set; }
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
