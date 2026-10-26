using System.Text.Json.Serialization;

namespace Politicom.Sim;

/// <summary>What a figure is good at.</summary>
public enum FigureRole
{
    /// <summary>Leads a column: the commune they're in (and its neighbours, less) defends better.</summary>
    Commander,
    /// <summary>Speaks in the assemblies: trust grows around them, and the revolution's enthusiasm with it.</summary>
    Orator,
    /// <summary>Runs the syndicates' work: more food and goods where they are.</summary>
    Organizer,
    /// <summary>Teaches and publishes: political education rises where they are and next door.</summary>
    Educator,
}

/// <summary>A notable figure as a scenario names them (historical people), or as one is generated.</summary>
public sealed class FigureDefinition
{
    public string Name { get; set; } = "";
    public FigureRole Role { get; set; }
    /// <summary>Where they start (a commune's name).</summary>
    public string Commune { get; set; } = "";
    /// <summary>How readily prestige turns into a wish for power (0 incorruptible .. 1 a would-be minister).</summary>
    public double Ambition { get; set; } = 0.3;
    public double Prestige { get; set; } = 0.3;
    public string Description { get; set; } = "";
}

/// <summary>
/// A notable figure of the movement. Wherever they are, they make things better: a commander's
/// commune holds, an orator's assemblies trust each other, an organizer's works produce, an
/// educator's people read. Their prestige grows with their work; with prestige their ambition
/// grows, and an ambitious, prestigious figure adds hierarchy drift and in time asks for real
/// power. The movement can rotate them back to the ranks: the old anarchist answer, which costs
/// their prestige and disappoints their followers. Commanders can fall at the front.
/// </summary>
public sealed class Figure
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public FigureRole Role { get; set; }
    public string Description { get; set; } = "";
    /// <summary>The commune they're working in, or null (resting after rotation, or between postings).</summary>
    public int? Commune { get; set; }
    public double Prestige { get; set; }
    public double Ambition { get; set; }
    public bool Alive { get; set; } = true;
    /// <summary>Turns left back in the ranks after a rotation, before they can take a post again.</summary>
    public int Resting { get; set; }
    /// <summary>Granted the power they asked for: twice the effect, and much more drift.</summary>
    public bool Empowered { get; set; }
    /// <summary>A request for power is waiting for the movement's answer.</summary>
    public bool Asking { get; set; }

    /// <summary>How close they are to asking for power (0..1); they ask at about 0.45.</summary>
    [JsonIgnore] public double Hunger => Prestige * Ambition;

    public static string RoleName(FigureRole role) => role switch
    {
        FigureRole.Commander => "commander",
        FigureRole.Orator => "orator",
        FigureRole.Organizer => "organizer",
        _ => "educator",
    };

    public static string Does(FigureRole role) => role switch
    {
        FigureRole.Commander => "the commune and its neighbours defend better",
        FigureRole.Orator => "trust grows around them, and the revolution's enthusiasm",
        FigureRole.Organizer => "more food and goods where they are",
        _ => "political education rises there and next door",
    };
}
