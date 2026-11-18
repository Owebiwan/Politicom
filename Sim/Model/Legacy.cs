namespace Politicom.Sim;

/// <summary>The four long paths a chapter can be remembered by.</summary>
public enum LegacyPath { MutualAid, Culture, Defence, Knowledge }

public enum CrisisKind { Plague, HungerYears, Offensive, CounterRevolution }

/// <summary>
/// The movement's chapters, their legacies and their crises (LegacySystem, CrisisSystem). A game
/// is told in chapters: the whole of a short game, about five years each in a campaign. In each
/// chapter the movement can reach three milestones on each of four paths (mutual aid, the new
/// culture, the defence of the revolution, knowledge). At the chapter's end, every path with two
/// or three milestones becomes a permanent legacy. In the last quarter of a chapter (in campaigns
/// and open starts) a crisis of the age builds in three stages, each with a choice of policy.
/// </summary>
public sealed class LegacyState
{
    public int Chapter { get; set; } = 1;
    public int ChapterStart { get; set; }
    public int ChapterEnd { get; set; }
    /// <summary>Milestones reached this chapter, as "Path:index".</summary>
    public HashSet<string> Reached { get; set; } = new();
    /// <summary>Legacies earned in past chapters: level by path name (each chapter adds 1 for two milestones, 2 for three).</summary>
    public Dictionary<string, int> Levels { get; set; } = new();
    /// <summary>One line per finished chapter.</summary>
    public List<string> History { get; set; } = new();

    // Counters for this chapter's milestones.
    public int QuietTicks { get; set; }
    public int SharedTicks { get; set; }
    public int Repulsed { get; set; }
    public int HeldTicks { get; set; }
    public int TechsAtStart { get; set; }
    public int CommunesAtStart { get; set; }
    public bool PeaceMade { get; set; }
    public HashSet<int> AtWar { get; set; } = new();

    public Crisis? Crisis { get; set; }
    public CrisisKind? LastCrisis { get; set; }

    public int Level(LegacyPath p) => Levels.GetValueOrDefault(p.ToString());
    public bool Has(LegacyPath p, int milestone) => Reached.Contains($"{p}:{milestone}");
    public int Count(LegacyPath p) => Enumerable.Range(0, 3).Count(i => Has(p, i));
}

/// <summary>A crisis of the age, building in three stages to the end of the chapter.</summary>
public sealed class Crisis
{
    public CrisisKind Kind { get; set; }
    public string Name { get; set; } = "";
    /// <summary>0 before it breaks, then 1..3.</summary>
    public int Stage { get; set; }
    public int Start { get; set; }
    public int End { get; set; }
    /// <summary>The policies chosen, by id.</summary>
    public List<string> Policies { get; set; } = new();
    /// <summary>Communes the plague has reached.</summary>
    public HashSet<int> Afflicted { get; set; } = new();
    /// <summary>The enemy whose offensive it is.</summary>
    public int Enemy { get; set; } = -1;
    public int CommunesAtStart { get; set; }
    public double Dead { get; set; }
}
