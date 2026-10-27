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

    // ── Decisions at the front ──
    /// <summary>Defence added at full fortification (+25%).</summary>
    public double FortDefense { get; set; } = 0.25;
    public double CostFortify { get; set; } = 8;
    /// <summary>Goods per thousand people the works take from the commune.</summary>
    public double FortifyGoods { get; set; } = 1.5;
    public int FortifyTicks { get; set; } = 3;
    /// <summary>Fortification added per turn of work (three turns: about 0.6).</summary>
    public double FortifyRate { get; set; } = 0.2;
    /// <summary>Fortification lost per turn while the front gives way.</summary>
    public double FortWear { get; set; } = 0.03;
    public double CostRaid { get; set; } = 6;
    public int RaidCooldown { get; set; } = 4;
    public double RaidExhaustion { get; set; } = 0.04;
    /// <summary>Goods per thousand people captured by a successful raid.</summary>
    public double RaidSpoils { get; set; } = 0.8;
    /// <summary>Share of the commune's fighters lost when a raid fails.</summary>
    public double RaidLosses { get; set; } = 0.15;

    // ── Peace (open-world starts) ──
    /// <summary>Turns an enemy must go without taking a commune before it considers terms (scaled by turn length).</summary>
    public double PeaceStalemateWeeks { get; set; } = 40;
    /// <summary>Exhaustion that counts as much as a full stalemate.</summary>
    public double PeaceExhaustion { get; set; } = 0.3;
    public double PeaceOfferRate { get; set; } = 0.15;
    public int PeaceRefusalGap { get; set; } = 20;
    /// <summary>Drift from making peace: delegates at a table with a government.</summary>
    public double PeaceDrift { get; set; } = 3;
    /// <summary>Ceilings on drift sums that grow over decades (short games stay far below them).</summary>
    public double TechDriftCeiling { get; set; } = 0.25;
    public double TechBrakeCeiling { get; set; } = 0.3;
    public double ProjectDriftCeiling { get; set; } = 0.15;
    /// <summary>Campaigns: years a treaty holds for certain, then the yearly chance it is broken.</summary>
    public double TreatyYears { get; set; } = 4;
    public double TreatyBreakPerYear { get; set; } = 0.2;
    /// <summary>Campaigns: drift brake per week while no commune is on a front.</summary>
    public double PeaceDriftBrake { get; set; } = 0.2;

    // ── Notable figures ──
    /// <summary>Defence a commander adds to their commune at prestige 0.5 (half that next door).</summary>
    public double FigureDefense { get; set; } = 0.12;
    public double FigureTrust { get; set; } = 0.004;
    public double FigureEnthusiasm { get; set; } = 0.002;
    /// <summary>Food and goods per thousand people per turn an organizer adds.</summary>
    public double FigureProduce { get; set; } = 0.06;
    public double FigureEducation { get; set; } = 0.006;
    public double FigurePrestigeGain { get; set; } = 0.012;
    public double FigureAmbitionGrowth { get; set; } = 0.006;
    /// <summary>Drift per turn per unit of prestige x ambition, for each figure at work.</summary>
    public double FigureDrift { get; set; } = 0.1;
    /// <summary>Prestige x ambition at which a figure asks for power.</summary>
    public double FigureBidThreshold { get; set; } = 0.42;
    public double FigureEmpowerDrift { get; set; } = 4;
    public double FigureDeathRate { get; set; } = 0.012;
    public double FigureDeathEnthusiasm { get; set; } = 0.06;
    public double FigureRotateWellbeing { get; set; } = 0.03;
    public double FigureRotateDriftRelief { get; set; } = 1;
    public int FigureRestTicks { get; set; } = 4;
    public double CostPostFigure { get; set; } = 4;
    public double CostRotateFigure { get; set; } = 2;
    public double CostAgitate { get; set; } = 6;
    public double CostAppeal { get; set; } = 12;
    public int AppealTicks { get; set; } = 12;
    public int AppealCooldown { get; set; } = 20;

    // ── Revolutions abroad ─────────────────────────────────────────────────
    /// <summary>Radicalization above which a state's workers may rise.</summary>
    public double RevolutionThreshold { get; set; } = 0.55;
    /// <summary>Chance per turn of a rising, per point of radicalization over the threshold.</summary>
    public double UprisingChance { get; set; } = 0.6;
    public int UprisingTicks { get; set; } = 8;
    /// <summary>Extra contagion per neighbouring sister federation.</summary>
    public double SisterContagion { get; set; } = 0.5;
    public double CostSupportUprising { get; set; } = 15;
    public double SupportUprisingEffect { get; set; } = 0.15;
    public int FederateDelay { get; set; } = 6;
    public int FederateCities { get; set; } = 5;

    // ── Wars between states ────────────────────────────────────────────────
    /// <summary>Chance per turn of war between two neighbours, per point of ideological hostility.</summary>
    public double WarChance { get; set; } = 0.0004;
    public double WearinessRate { get; set; } = 0.012;
    public double PeaceChance { get; set; } = 0.06;
    /// <summary>Radicalization per turn per point of war weariness.</summary>
    public double WarRadicalization { get; set; } = 0.0012;
    /// <summary>Share of a warring state's strength tied up in its own war.</summary>
    public double WarCommitment { get; set; } = 0.6;

    // ── Ideological rivals ─────────────────────────────────────────────────
    /// <summary>Party cell growth per turn, per unit of rival pressure.</summary>
    public double PartyGrowth { get; set; } = 0.0008;
    /// <summary>Party cell decay per turn, per point of political education.</summary>
    public double PartyFade { get; set; } = 0.012;
    /// <summary>Drift per turn when party cells hold every commune.</summary>
    public double PartyDrift { get; set; } = 0.12;
    public int RivalOfferInterval { get; set; } = 30;
    public double RivalArms { get; set; } = 30;
    public double RivalAcceptDrift { get; set; } = 4;
    public double CostExpose { get; set; } = 8;

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

    // ── Militia, gold and enthusiasm ───────────────────────────────────────
    /// <summary>Share of a commune's people in its militia, as things stand (more at the front).</summary>
    public double BaseMobilization { get; set; } = 0.1;
    public double FrontMobilization { get; set; } = 0;
    /// <summary>Share of the gap to what a commune volunteers that closes each week.</summary>
    public double MobilizationPull { get; set; } = 0.08;
    public double MaxMobilization { get; set; } = 0.35;
    /// <summary>How much more a fully militant commune volunteers than a pacific one (0 = no difference).</summary>
    public double VolunteerMilitancy { get; set; } = 0.3;
    /// <summary>Militia share added by a call for volunteers (half that in the neighbours).</summary>
    public double VolunteerCall { get; set; } = 0.05;
    public double CostVolunteers { get; set; } = 8;
    /// <summary>Under conscription no commune has fewer under arms than this.</summary>
    public double ConscriptionFloor { get; set; } = 0.2;
    public double ConscriptionDrift { get; set; } = 0.15;
    public double ConscriptionEnthusiasm { get; set; } = 0.004;
    /// <summary>People lost per week, as a share of a losing front commune's militia.</summary>
    public double CombatLoss { get; set; } = 0.01;
    public double StartEnthusiasm { get; set; } = 0.8;
    public double EnthusiasmDecay { get; set; } = 0.001;
    public double EnthusiasmCulture { get; set; } = 0.012;
    public double EnthusiasmHunger { get; set; } = 0.02;
    public double EnthusiasmHierarchy { get; set; } = 0.001;
    /// <summary>Production, militia and contagion change by this much per point of enthusiasm away from the start.</summary>
    public double EnthusiasmEffect { get; set; } = 0.15;
    /// <summary>Enthusiasm lost when a commune falls.</summary>
    public double EnthusiasmLoss { get; set; } = 0.015;
    /// <summary>Gold from seizing the treasuries, per thousand people in the federation.</summary>
    public double TreasuryGold { get; set; } = 0.12;
    public double SeizeDrift { get; set; } = 3;
    /// <summary>Drift per week per unit of gold held: the committee that guards it.</summary>
    public double GoldHoldingDrift { get; set; } = 0.0015;
    public double GoldPerPurchase { get; set; } = 10;
    public double GoldArms { get; set; } = 25;
    public double GoldFood { get; set; } = 40;
    public double GoldMachines { get; set; } = 0.03;
    public double PurchaseDrift { get; set; } = 1;

    // ── Scarcity prices ────────────────────────────────────────────────────
    /// <summary>Weeks of stores at which a price is 1.</summary>
    public double PriceWeeks { get; set; } = 4;
    /// <summary>Wellbeing industrial towns lose per point the food price is above 1.</summary>
    public double DearFoodWellbeing { get; set; } = 0.06;

    // ── Communes' own proposals ────────────────────────────────────────────
    /// <summary>Weeks between a commune's proposals at least, and the chance each week after that.</summary>
    public double ProposalGap { get; set; } = 7;
    public double ProposalRate { get; set; } = 0.25;
    /// <summary>Influence to back a commune's proposal (cheaper than proposing it yourself).</summary>
    public double CostBackProposal { get; set; } = 6;

    // ── Random events and risings ──────────────────────────────────────────
    /// <summary>Chance per week of a random event, once the gap since the last has passed.</summary>
    public double RandomEventRate { get; set; } = 0.12;
    /// <summary>Weeks between random events at least.</summary>
    public double RandomEventGap { get; set; } = 6;
    /// <summary>Chance per week that a city of a regional rising's country rises, next door and with the movement's ideas at full strength.</summary>
    public double RisingRate { get; set; } = 0.02;
    /// <summary>Distance (km) over which a city's chance of rising falls by e.</summary>
    public double RisingReachKm { get; set; } = 250;

    // ── Science, culture and technology levels ─────────────────────────────
    /// <summary>Science for the first breakthrough; each later one costs BreakthroughGrowth more.</summary>
    public double BreakthroughCost { get; set; } = 100;
    public double BreakthroughGrowth { get; set; } = 0.2;
    /// <summary>Extra discovery weight for technologies in the research focus.</summary>
    public double FocusWeight { get; set; } = 3;
    /// <summary>Science bonus from trade, times the trading partners' mean technology level.</summary>
    public double TradeScience { get; set; } = 0.3;
    public double SisterScience { get; set; } = 0.1;
    /// <summary>Starting culture as a share of political education.</summary>
    public double StartCulture { get; set; } = 0.3;
    public double CultureRate { get; set; } = 0.012;
    public double CultureSpread { get; set; } = 0.03;
    public double CultureDecay { get; set; } = 0.006;
    /// <summary>Drift brake per turn at full culture across the federation.</summary>
    public double CultureDriftBrake { get; set; } = 0.05;
    /// <summary>Trust gained per turn between neighbours at full culture.</summary>
    public double CultureTrust { get; set; } = 0.002;
    /// <summary>Contagion multiplier at full culture.</summary>
    public double CultureContagion { get; set; } = 0.6;
    /// <summary>How much culture keeps rival parties' cells out (1 = completely at full culture).</summary>
    public double CultureResistsParty { get; set; } = 0.6;
    /// <summary>How much a state's own culture resists contagion among its workers, at full weight.</summary>
    public double RivalCultureResistance { get; set; } = 0.4;
    public double CostCulturalWork { get; set; } = 12;
    public int CulturalWorkCooldown { get; set; } = 8;
    public double StateTechGrowth { get; set; } = 0.0015;
    public double EnemyTechGrowth { get; set; } = 0.003;
    /// <summary>How the federation's technology level compares with an enemy's (it moves on a smaller scale).</summary>
    public double FederationTechScale { get; set; } = 2;
    public double TechEdge { get; set; } = 0.8;
    public double TechEdgeCap { get; set; } = 0.2;

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
