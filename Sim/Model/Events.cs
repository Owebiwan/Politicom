using System.Globalization;

namespace Politicom.Sim;

/// <summary>A scripted historical decision, defined in the scenario JSON.</summary>
public sealed class EventDefinition
{
    public string Id { get; set; } = "";
    public int Tick { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>The option taken if the movement doesn't answer in time.</summary>
    public int DefaultOption { get; set; }
    public int ExpiresAfter { get; set; } = 4;
    /// <summary>Only fires if all of these communes are still free and federated.</summary>
    public string[] RequiresFederated { get; set; } = Array.Empty<string>();
    /// <summary>The figure (by name) the event is about, shown with it.</summary>
    public string Figure { get; set; } = "";
    public List<EventOption> Options { get; set; } = new();
}

public sealed class EventOption
{
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public EventEffects Effects { get; set; } = new();
}

/// <summary>What a decision does with a figure who asked for power.</summary>
public enum FigureChoice { Empower, Rotate }

public sealed class EventEffects
{
    /// <summary>Food per thousand people in the affected communes (a harvest, a flood).</summary>
    public double Food { get; set; }
    /// <summary>Fractional change in the affected communes' population (refugees, plague).</summary>
    public double Population { get; set; }
    /// <summary>Culture added in the affected communes.</summary>
    public double Culture { get; set; }
    /// <summary>A project to start (a commune's proposal the movement backs), with its target communes by name.</summary>
    public ProjectKind? StartProject { get; set; }
    public string ProjectTarget { get; set; } = "";
    public string ProjectTarget2 { get; set; } = "";
    /// <summary>Gold gained (or spent).</summary>
    public double Gold { get; set; }
    /// <summary>Change in the revolution's enthusiasm.</summary>
    public double Enthusiasm { get; set; }
    /// <summary>A technique the affected communes learn (if the world has reached it).</summary>
    public string Tech { get; set; } = "";
    /// <summary>Communes affected by wellbeing, trust and education effects. Empty = the whole federation.</summary>
    public string[] Communes { get; set; } = Array.Empty<string>();
    public double Drift { get; set; }
    public double Influence { get; set; }
    public double Wellbeing { get; set; }
    /// <summary>Added to every trust edge between affected communes.</summary>
    public double Trust { get; set; }
    public double PoliticalEducation { get; set; }
    public double EnemyStrength { get; set; }
    /// <summary>Which enemy EnemyStrength applies to (by name). Empty = all of them.</summary>
    public string Enemy { get; set; } = "";
    /// <summary>Goods shared out among the front communes.</summary>
    public double GoodsToFront { get; set; }
    /// <summary>Change to the scenario's food imports per turn (a blockade tightening or easing).</summary>
    public double FoodImports { get; set; }
    public int MilitarizationTicks { get; set; }
    /// <summary>The ally stops offering arms for the rest of the game.</summary>
    public bool EndAllyOffers { get; set; }
    /// <summary>Communes that leave the federation as a result.</summary>
    public string[] Secede { get; set; } = Array.Empty<string>();
    /// <summary>Added to every commune's party influence (a rival's cells gaining ground).</summary>
    public double PartyInfluence { get; set; }
    /// <summary>A notable figure (by name) the choice concerns, and what happens to them.</summary>
    public string Figure { get; set; } = "";
    public FigureChoice? FigureChoice { get; set; }
    /// <summary>An enemy (by name) the federation makes peace with.</summary>
    public string MakePeace { get; set; } = "";
    /// <summary>A notable figure (by name) who dies (Durruti on the Madrid front).</summary>
    public string KillFigure { get; set; } = "";
    /// <summary>A sister federation whose cities join yours (by state id).</summary>
    public string Federate { get; set; } = "";
    /// <summary>Changes to outside states' stance toward the federation, by state id.</summary>
    public Dictionary<string, double> Stances { get; set; } = new();
    /// <summary>A goal (by id) the movement takes on (GoalSystem).</summary>
    public string StartGoal { get; set; } = "";
    /// <summary>Changes to <see cref="Figure"/>'s prestige and ambition, and the chance they are killed doing it.</summary>
    public double FigurePrestige { get; set; }
    public double FigureAmbition { get; set; }
    public double FigureRisk { get; set; }
    /// <summary>Another figure (by name) the choice goes against, and the change to their prestige and ambition.</summary>
    public string Rival { get; set; } = "";
    public double RivalPrestige { get; set; }
    public double RivalAmbition { get; set; }
    /// <summary>How the choice makes the figure and the rival feel about the movement, and what each remembers of it.</summary>
    public double FigureRegard { get; set; }
    public string Memory { get; set; } = "";
    public double RivalRegard { get; set; }
    public string RivalMemory { get; set; } = "";
    /// <summary>Change in how the figure and the rival feel about each other.</summary>
    public double Relation { get; set; }
    /// <summary>A jump in the struggle between allies' tension.</summary>
    public double Tension { get; set; }
    /// <summary>A faction (by kind) whose strength changes.</summary>
    public string Faction { get; set; } = "";
    public double FactionStrength { get; set; }
    /// <summary>Added to the target communes' control of their ground (front communes only).</summary>
    public double Control { get; set; }
    /// <summary>Added to the enemy's exhaustion.</summary>
    public double EnemyExhaustion { get; set; }

    /// <summary>Short human-readable summary, e.g. "+6 drift, +25 influence".</summary>
    public string Summary(string turn = "week")
    {
        var parts = new List<string>();
        void Add(double v, string what, string format = "+0.##;-0.##")
        {
            if (Math.Abs(v) > 1e-9) parts.Add($"{v.ToString(format, CultureInfo.InvariantCulture)} {what}");
        }
        string scope = Communes.Length == 0 ? "" : Communes.Length <= 2 ? $" ({string.Join(", ", Communes)})" : $" ({Communes.Length} communes)";
        Add(Drift, "drift");
        Add(Food, "food per thousand people" + scope);
        Add(Gold, "gold");
        if (StartProject is { } kind) parts.Add($"starts a {ProjectCatalog.Get(kind).Name.ToLowerInvariant()}");
        Add(Enthusiasm * 100, "% enthusiasm");
        Add(Population * 100, "% population" + scope);
        Add(Culture * 100, "% culture" + scope);
        if (Tech.Length > 0) parts.Add($"learn {Tech.Replace('_', ' ')}{scope}");
        Add(Influence, "influence");
        Add(Wellbeing * 100, "% wellbeing" + scope);
        Add(Trust * 100, "% trust" + scope);
        Add(PoliticalEducation * 100, "% political education" + scope);
        Add(EnemyStrength, Enemy.Length > 0 ? $"{Enemy} strength" : "enemy strength");
        Add(GoodsToFront, "goods to the front");
        Add(FoodImports, "food imports per " + turn);
        if (MilitarizationTicks > 0) parts.Add($"militarized {MilitarizationTicks} {turn}s");
        if (EndAllyOffers) parts.Add("no more arms offers");
        if (Secede.Length > 0) parts.Add($"{string.Join(", ", Secede)} leave");
        foreach (var (state, delta) in Stances) parts.Add($"{state} {(delta > 0 ? "warmer" : "cooler")} toward you");
        if (Federate.Length > 0) parts.Add("its cities join your federation");
        if (PartyInfluence > 0) parts.Add($"party cells +{PartyInfluence * 100:0}%");
        if (StartGoal.Length > 0) parts.Add("the goal is taken on");
        Add(Control * 100, "% ground held" + scope);
        Add(EnemyExhaustion * 100, "% enemy exhaustion");
        Add(Tension, "tension with the allies");
        if (Figure.Length > 0) Add(FigurePrestige * 100, $"% {Figure}'s prestige");
        if (Figure.Length > 0) Add(FigureAmbition * 100, $"% {Figure}'s ambition");
        if (FigureRisk > 0) parts.Add($"{FigureRisk:P0} risk to {Figure}'s life");
        if (Rival.Length > 0) Add(RivalPrestige * 100, $"% {Rival}'s prestige");
        if (Rival.Length > 0) Add(RivalAmbition * 100, $"% {Rival}'s ambition");
        if (Figure.Length > 0 && FigureRegard != 0) parts.Add($"{Figure} {(FigureRegard > 0 ? "warmer" : "cooler")} toward you");
        if (Rival.Length > 0 && RivalRegard != 0) parts.Add($"{Rival} {(RivalRegard > 0 ? "warmer" : "cooler")} toward you");
        if (Rival.Length > 0 && Relation < 0) parts.Add($"{Figure} and {Rival} grow apart");
        if (Rival.Length > 0 && Relation > 0) parts.Add($"{Figure} and {Rival} grow closer");
        return parts.Count == 0 ? "no direct effect" : string.Join(", ", parts);
    }
}

public sealed record PendingEvent(string Id, int Expires);
