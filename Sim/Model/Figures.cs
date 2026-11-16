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

/// <summary>
/// A figure's character, as Crusader Kings gives its people traits. Each shapes how they work,
/// what they do when they act on their own, and how fast fame turns into appetite.
/// </summary>
public enum FigureTrait
{
    /// <summary>Leads from the front: a commander's line holds better, and they risk their life more.</summary>
    Brave,
    /// <summary>Spares lives: less risk, a little less fight; prefers holding to striking.</summary>
    Cautious,
    /// <summary>Wants more: ambition grows half again as fast.</summary>
    Ambitious,
    /// <summary>Wants little: ambition grows at half the rate.</summary>
    Humble,
    /// <summary>Plays straight: the power they gather adds less drift.</summary>
    Honest,
    /// <summary>Loves the crowd: prestige comes faster, and a slight cuts deeper.</summary>
    Vain,
    /// <summary>A true believer: fights the party, and takes compromises badly.</summary>
    Zealous,
    /// <summary>Takes what works: forgives compromises, and leans toward the centralists.</summary>
    Pragmatic,
    /// <summary>A gift for words: speeches and papers reach further.</summary>
    Eloquent,
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
    public List<FigureTrait> Traits { get; set; } = new();
    /// <summary>Year of birth (0: unknown, they don't age).</summary>
    public int Born { get; set; }
}

/// <summary>Something a figure remembers the movement doing, for or against them.</summary>
public sealed record FigureMemory(int Tick, string Text, double Regard);

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
    public List<FigureTrait> Traits { get; set; } = new();
    /// <summary>Year of birth (0: unknown, they don't age); retired from the work in old age.</summary>
    public int Born { get; set; }
    public bool Retired { get; set; }
    /// <summary>How they feel about the movement's leadership (-1 bitter .. 1 devoted): what it has done to them.</summary>
    public double Regard { get; set; }
    /// <summary>How they feel about each other figure (by id): above 0.4 friends, below -0.4 rivals.</summary>
    public Dictionary<int, double> Opinions { get; set; } = new();
    /// <summary>What they remember the movement doing, newest last (a few at most).</summary>
    public List<FigureMemory> Memories { get; set; } = new();

    public bool Has(FigureTrait t) => Traits.Contains(t);
    public double OpinionOf(int other) => Opinions.GetValueOrDefault(other);
    public bool FriendOf(int other) => OpinionOf(other) >= 0.4;
    public bool RivalOf(int other) => OpinionOf(other) <= -0.4;

    /// <summary>How readily ambition grows: ambitious faster, humble slower, a grudge feeds it.</summary>
    [JsonIgnore] public double AmbitionPace => (Has(FigureTrait.Ambitious) ? 1.5 : 1) * (Has(FigureTrait.Humble) ? 0.5 : 1) * (Regard < -0.3 ? 1.4 : 1);

    /// <summary>How well they work: devoted figures give more, bitter ones less.</summary>
    [JsonIgnore] public double Zeal => 1 + 0.25 * Math.Clamp(Regard, -1, 1);

    public static string TraitName(FigureTrait t) => t.ToString().ToLowerInvariant();

    public static string RegardWord(double regard) =>
        regard >= 0.5 ? "devoted" : regard >= 0.2 ? "loyal" : regard > -0.2 ? "steady" : regard > -0.5 ? "resentful" : "bitter";

    /// <summary>Remembers something, and feels it.</summary>
    public void Remember(int tick, string text, double regard)
    {
        Regard = Math.Clamp(Regard + regard, -1, 1);
        Memories.Add(new FigureMemory(tick, text, regard));
        if (Memories.Count > 5) Memories.RemoveAt(0);
    }

    /// <summary>Changes how they feel about another figure (and that one about them, half as much).</summary>
    public static void Feel(Figure a, Figure b, double change)
    {
        a.Opinions[b.Id] = Math.Clamp(a.OpinionOf(b.Id) + change, -1, 1);
        b.Opinions[a.Id] = Math.Clamp(b.OpinionOf(a.Id) + change * 0.5, -1, 1);
    }

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
