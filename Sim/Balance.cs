using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Politicom.Sim;

/// <summary>
/// Every tunable number in the simulation. Batch runs override these by name
/// (<c>--set CoordinationExponent=1.4</c> or <c>--sweep ...</c>) so the open design
/// questions can be answered with data instead of guesses.
/// </summary>
public sealed class Balance
{
    // ── Economy ────────────────────────────────────────────────────────────
    public double FoodNeed { get; set; } = 1.0;
    public double GoodsNeed { get; set; } = 0.45;
    public double AgrarianFoodYield { get; set; } = 1.85;
    public double AgrarianGoodsYield { get; set; } = 0.2;
    public double IndustrialFoodYield { get; set; } = 0.35;
    public double IndustrialGoodsYield { get; set; } = 1.15;
    public double MixedFoodYield { get; set; } = 1.1;
    public double MixedGoodsYield { get; set; } = 0.65;
    /// <summary>Self-managed workplaces: ±this fraction of output at trust 1 / 0.</summary>
    public double TrustProductivity { get; set; } = 0.3;
    public double PracticalProductivity { get; set; } = 0.5;
    /// <summary>Ticks of consumption a commune keeps before it shares surplus.</summary>
    public double ReserveTicks { get; set; } = 3;
    public double StorageTicks { get; set; } = 12;
    public double NeighborGenerosity { get; set; } = 0.6;
    /// <summary>Share of surplus a fully trusting federated commune puts in the federal pool.</summary>
    public double PoolShare { get; set; } = 0.5;
    /// <summary>Transport loss on pooled goods; railways reduce it.</summary>
    public double PoolLoss { get; set; } = 0.2;
    public double FamineMortality { get; set; } = 0.01;
    public double ProjectLabor { get; set; } = 0.05;

    // ── Trust ──────────────────────────────────────────────────────────────
    public double AidTrustGain { get; set; } = 0.008;
    public double HoardingTrustLoss { get; set; } = 0.01;
    public double FreeRideTrustLoss { get; set; } = 0.006;
    public double ValueFrictionRate { get; set; } = 0.003;
    public double TrustBaselinePull { get; set; } = 0.004;
    public double BrokenCommitmentTrustLoss { get; set; } = 0.05;
    public double SharedStruggleTrustGain { get; set; } = 0.003;
    public double MediationTrustGain { get; set; } = 0.12;
    public double WarCommitteeTrustLoss { get; set; } = 0.0008;

    // ── Education ──────────────────────────────────────────────────────────
    public double SchoolingRate { get; set; } = 0.12;
    public double EducationDecay { get; set; } = 0.001;
    public double BaselineSchooling { get; set; } = 0.02;
    public double FundedSchoolingBoost { get; set; } = 0.04;
    public double SchoolingRelax { get; set; } = 0.04;
    public double MaxSchooling { get; set; } = 0.25;

    // ── Research ───────────────────────────────────────────────────────────
    public double DiscoveryRate { get; set; } = 0.012;
    public double DiffusionRate { get; set; } = 0.05;
    public double IdeaNudgeWeight { get; set; } = 4;
    /// <summary>
    /// How readily a commune that holds a line's free path (a brake) still takes up that line's
    /// centralizing technologies, relative to one that doesn't. Below 1, the paths are a choice.
    /// </summary>
    public double PathResistance { get; set; } = 0.35;
    public int IdeaNudgeTicks { get; set; } = 20;

    // ── Voluntary projects ─────────────────────────────────────────────────
    public double ContributionBase { get; set; } = 0.5;
    public double CoordinationScale { get; set; } = 0.08;
    public double CoordinationExponent { get; set; } = 1.3;
    public double PoliticalCoordinationRelief { get; set; } = 0.5;
    public double ProjectDeadlineFactor { get; set; } = 3;
    public int LargeProjectThreshold { get; set; } = 6;
    public double LargeProjectDrift { get; set; } = 0.03;
    public double JoinChance { get; set; } = 0.5;
    public int MaxActiveProjects { get; set; } = 3;

    // ── Railways ───────────────────────────────────────────────────────────
    /// <summary>Multiplier on neighbour aid between communes joined by rail.</summary>
    public double RailAid { get; set; } = 1.15;
    /// <summary>Multiplier on militia support between communes joined by rail.</summary>
    public double RailSupport { get; set; } = 1.25;
    /// <summary>Multiplier on ideas spreading between communes joined by rail.</summary>
    public double RailDiffusion { get; set; } = 1.3;

    // ── Outside states ─────────────────────────────────────────────────────
    /// <summary>How fast a state's stance moves toward where its interests put it, per turn.</summary>
    public double StanceDrift { get; set; } = 0.03;
    /// <summary>Goods to the front per turn, per unit of a friendly state's weight × stance.</summary>
    public double ArmsPerWeight { get; set; } = 0.8;
    /// <summary>Defense bonus per unit of volunteering states' weight × stance (capped).</summary>
    public double VolunteerPerWeight { get; set; } = 0.04;
    public double VolunteerCap { get; set; } = 0.3;
    /// <summary>Volunteers per unit of a state's power × its workers' radicalization.</summary>
    public double WorkerVolunteers { get; set; } = 0.5;
    /// <summary>Share of its sponsored enemy's strength added per unit of an intervening state's weight × hostility.</summary>
    public double InterventionPerWeight { get; set; } = 0.02;
    /// <summary>Most that interventions can add, as a share of the enemy's own strength.</summary>
    public double InterventionCap { get; set; } = 0.6;
    public double DelegateEffect { get; set; } = 0.12;
    public double AgitateEffect { get; set; } = 0.05;
    public double CostDelegates { get; set; } = 6;
    public double CostAgitate { get; set; } = 6;
    public double CostAppeal { get; set; } = 12;
    public int AppealTicks { get; set; } = 12;
    public int AppealCooldown { get; set; } = 20;

    // ── Foreign trade ──────────────────────────────────────────────────────
    /// <summary>Share of each commune's food surplus sold abroad per turn while trading.</summary>
    public double TradeShare { get; set; } = 0.3;
    /// <summary>Goods bought per unit of food sold, before embargoes.</summary>
    public double TradeRate { get; set; } = 0.8;
    /// <summary>Drift per turn from currency, accounts and a trade bureau.</summary>
    public double TradeDrift { get; set; } = 0.12;
    /// <summary>Embargo strength per point of mean radicalization behind enemy lines.</summary>
    public double EmbargoPerRadicalization { get; set; } = 1.5;

    // ── War and outside pressure ───────────────────────────────────────────
    public double MilitiaPerPop { get; set; } = 0.12;
    public double NeighborSupport { get; set; } = 0.25;
    public double PressureScale { get; set; } = 3.4;
    public double ControlLossRate { get; set; } = 0.12;
    public double ControlRecovery { get; set; } = 0.02;
    /// <summary>"No head to cut off": each occupied commune exhausts the occupier.</summary>
    public double OccupationExhaustion { get; set; } = 0.015;
    public double ExhaustionDecay { get; set; } = 0.006;
    public double GuerrillaExhaustion { get; set; } = 0.002;
    public double ArmsUse { get; set; } = 0.5;
    public double ArmsShortageDefense { get; set; } = 0.6;
    /// <summary>Defense bonus for a front commune with a full arms stockpile.</summary>
    public double StockpileDefense { get; set; } = 0.15;
    /// <summary>Goods per head (thousands) that count as a full stockpile.</summary>
    public double StockpilePerPop { get; set; } = 3;
    public double WarCommitteeDefense { get; set; } = 0.35;
    public double WarCommitteeCoordination { get; set; } = 0.3;
    public double MilitarizationDefense { get; set; } = 0.3;
    public int MilitarizationTicks { get; set; } = 26;
    public double ContagionRate { get; set; } = 0.004;
    public double RepressionRate { get; set; } = 0.002;

    // ── Hierarchy drift ────────────────────────────────────────────────────
    public double BaselineDrift { get; set; } = 0.08;
    /// <summary>Drift per tick per point of (practical − political) education gap.</summary>
    public double ExpertTrapRate { get; set; } = 0.6;
    public double PoliticalBrake { get; set; } = 0.2;
    public double WarCommitteeDrift { get; set; } = 0.5;
    /// <summary>The temptation: while the front is losing ground, people hand power to whoever gets things done.</summary>
    public double WarStressDrift { get; set; } = 0.5;
    public double MilitarizationDrift { get; set; } = 0.25;
    public double ArmsAcceptDrift { get; set; } = 5;
    public double ForceStayDrift { get; set; } = 6;
    public double CongressDrift { get; set; } = 1.5;
    public double RecallDrift { get; set; } = 6;
    /// <summary>How far below a threshold drift must fall before a stage is undone.</summary>
    public double StageHysteresis { get; set; } = 10;

    // ── Federation and secession ───────────────────────────────────────────
    public double SecessionThreshold { get; set; } = 0.55;
    public double SecessionChance { get; set; } = 0.3;
    public double RejoinThreshold { get; set; } = 0.3;
    public double RejoinChance { get; set; } = 0.03;
    public int SecessionWarningTicks { get; set; } = 2;
    public double ForceStayTrustLoss { get; set; } = 0.15;
    /// <summary>Destroyed when fewer than this share of communes remain free and federated.</summary>
    public double CollapseFraction { get; set; } = 0.3;

    // ── Influence (player resource) ────────────────────────────────────────
    public double StartInfluence { get; set; } = 25;
    public double InfluenceBase { get; set; } = 1.0;
    public double InfluencePolitical { get; set; } = 2.0;
    public double InfluenceWellbeing { get; set; } = 0.5;
    public double InfluenceCap { get; set; } = 100;
    public double CostSchool { get; set; } = 8;
    public double CostPamphlets { get; set; } = 5;
    public double CostMediate { get; set; } = 6;
    public double CostPropose { get; set; } = 10;
    public double CostPersuade { get; set; } = 4;
    public double CostCongress { get; set; } = 15;
    public double CostCirculateIdea { get; set; } = 6;
    public double CostRecall { get; set; } = 12;
    public double CostForceStay { get; set; } = 10;
    public int CongressCooldown { get; set; } = 8;

    public Balance Clone() => (Balance)MemberwiseClone();

    public static IEnumerable<string> ParameterNames() =>
        typeof(Balance).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite).Select(p => p.Name);

    public double Get(string name) => Convert.ToDouble(Property(name).GetValue(this), CultureInfo.InvariantCulture);

    public void Set(string name, double value)
    {
        var p = Property(name);
        p.SetValue(this, p.PropertyType == typeof(int) ? (object)(int)Math.Round(value) : value);
    }

    /// <summary>Applies a JSON object of { "ParameterName": number } overrides.</summary>
    public void ApplyOverrides(string json)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                     ?? throw new InvalidDataException("Balance overrides must be a JSON object of numbers.");
        foreach (var (k, v) in values) Set(k, v);
    }

    private static PropertyInfo Property(string name) =>
        typeof(Balance).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
        ?? throw new ArgumentException($"Unknown balance parameter '{name}'.");
}
