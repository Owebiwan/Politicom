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
    public List<EventOption> Options { get; set; } = new();
}

public sealed class EventOption
{
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public EventEffects Effects { get; set; } = new();
}

public sealed class EventEffects
{
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
    public int MilitarizationTicks { get; set; }
    /// <summary>The ally stops offering arms for the rest of the game.</summary>
    public bool EndAllyOffers { get; set; }
    /// <summary>Communes that leave the federation as a result.</summary>
    public string[] Secede { get; set; } = Array.Empty<string>();

    /// <summary>Short human-readable summary, e.g. "+6 drift, +25 influence".</summary>
    public string Summary()
    {
        var parts = new List<string>();
        void Add(double v, string what, string format = "+0.##;-0.##")
        {
            if (Math.Abs(v) > 1e-9) parts.Add($"{v.ToString(format, CultureInfo.InvariantCulture)} {what}");
        }
        string scope = Communes.Length == 0 ? "" : Communes.Length <= 2 ? $" ({string.Join(", ", Communes)})" : $" ({Communes.Length} communes)";
        Add(Drift, "drift");
        Add(Influence, "influence");
        Add(Wellbeing * 100, "% wellbeing" + scope);
        Add(Trust * 100, "% trust" + scope);
        Add(PoliticalEducation * 100, "% political education" + scope);
        Add(EnemyStrength, Enemy.Length > 0 ? $"{Enemy} strength" : "enemy strength");
        Add(GoodsToFront, "goods to the front");
        if (MilitarizationTicks > 0) parts.Add($"militarized {MilitarizationTicks} weeks");
        if (EndAllyOffers) parts.Add("no more arms offers");
        if (Secede.Length > 0) parts.Add($"{string.Join(", ", Secede)} leave");
        return parts.Count == 0 ? "no direct effect" : string.Join(", ", parts);
    }
}

public sealed record PendingEvent(string Id, int Expires);
