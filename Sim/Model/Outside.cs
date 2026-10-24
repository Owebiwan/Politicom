using System.Text.Json.Serialization;

namespace Politicom.Sim;

public sealed class Offensive
{
    public string Name { get; set; } = "";
    public int Tick { get; set; }
    public int Duration { get; set; }
    public double Strength { get; set; }
}

/// <summary>The hostile neighbouring state applying outside pressure.</summary>
public sealed class HostileState
{
    public string Name { get; set; } = "";
    /// <summary>Map colour for its front and occupied communes (#rrggbb).</summary>
    public string Color { get; set; } = "#eb4d40";
    public double Strength { get; set; }
    public double Growth { get; set; }
    /// <summary>Turn after which steady growth stops (set when a game runs past its history); null = always.</summary>
    public int? GrowthUntil { get; set; }
    /// <summary>How radicalized its workers are (the Spread score). Forces it to divert troops to repression.</summary>
    public double Radicalization { get; set; }
    /// <summary>Wear from occupying communes that have no head to cut off.</summary>
    public double Exhaustion { get; set; }
    public List<Offensive> Offensives { get; set; } = new();
    /// <summary>States that intervene on this enemy's side when hostile enough (e.g. Germany and Italy for the Nationalists).</summary>
    public string[] Sponsors { get; set; } = Array.Empty<string>();
    /// <summary>0..1: how modern its army and industry are. It advances every turn; falling behind it costs the federation.</summary>
    public double TechLevel { get; set; } = 0.5;
    /// <summary>Its technology level when the game started.</summary>
    public double TechStart { get; set; }
    /// <summary>Strength added this turn by intervening states; recomputed every turn.</summary>
    [JsonInclude] public double Intervention { get; internal set; }
    [JsonInclude] public Offensive? CurrentOffensive { get; internal set; }
    [JsonInclude] public double EffectiveStrength { get; internal set; }
}

/// <summary>The nominal ally that offers arms in exchange for centralization.</summary>
public sealed class AllyState
{
    public string Name { get; set; } = "";
    public int NextOfferTick { get; set; }
    public int OfferInterval { get; set; }
    public double ArmsGoods { get; set; }
    [JsonInclude] public bool OfferPending { get; internal set; }
    [JsonInclude] public int OfferExpires { get; internal set; }
    [JsonInclude] public int MilitarizationTicks { get; internal set; }
    [JsonInclude] public int Accepted { get; internal set; }
    [JsonInclude] public int Declined { get; internal set; }
}

public sealed record PendingSecession(int Commune, int Deadline);

public enum Severity { Info, Good, Warning, Danger, Decision }

/// <param name="Pause">The front ends should stop the clock for this, as they do for decisions.</param>
/// <param name="Title">A heading for pop-ups (decisions and big news); empty for ordinary log lines.</param>
public sealed record GameEvent(int Tick, DateOnly Date, Severity Severity, string Text, int? Commune = null, bool Pause = false, string Title = "")
{
    /// <summary>Whether a running clock should stop: every decision, and events flagged to pause.</summary>
    public bool StopsTheClock => Pause || Severity == Severity.Decision;
}

public enum Outcome { Ongoing, Survived, Destroyed, BecameState }

/// <summary>One week's headline numbers, kept for charts and the end-of-game summary.</summary>
/// <summary>One turn's headline numbers, kept for charts, the ledger and the end-of-game summary.</summary>
public sealed record WeekRecord(int Tick, double Drift, int Federated, int Occupied, int Seceded, double MeanTrust, double Influence, double EnemyStrength,
    double Food = 0, double Goods = 0, double FoodPrice = 1, double GoodsPrice = 1, double Enthusiasm = 0, double Fighters = 0,
    double Gold = 0, double Science = 0, double Culture = 0, double Wellbeing = 0);

/// <summary>Success is measured over time on four scores, not by conquest. Each runs 0..100.</summary>
public sealed class Scores
{
    [JsonInclude] internal double SurvivalSum;
    [JsonInclude] internal double QualitySum;
    [JsonInclude] internal double FreedomSum;
    [JsonInclude] internal int Samples;

    [JsonInclude] public double Survival { get; internal set; }
    [JsonInclude] public double QualityOfLife { get; internal set; }
    [JsonInclude] public double Spread { get; internal set; }
    [JsonInclude] public double Freedom { get; internal set; }
    [JsonIgnore] public double Total => (Survival + QualityOfLife + Spread + Freedom) / 4;
}
