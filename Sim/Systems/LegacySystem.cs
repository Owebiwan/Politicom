namespace Politicom.Sim.Systems;

/// <summary>
/// Chapters and their legacies (<see cref="LegacyState"/>):
/// four long paths, three milestones on each per chapter. Each milestone reached brings a little
/// influence and enthusiasm. At the chapter's end a path with two milestones becomes a legacy
/// (level 1), with all three a lasting one (level 2), and legacies add up from chapter to
/// chapter: granaries held in common, songs everyone knows, a people in arms, rationalist
/// schools. In a short game the chapter is the whole game, and its legacies are its epitaph.
/// </summary>
public static class LegacySystem
{
    /// <summary>Weeks in a campaign's chapter.</summary>
    public const int ChapterWeeks = 260;

    public static readonly LegacyPath[] Paths = { LegacyPath.MutualAid, LegacyPath.Culture, LegacyPath.Defence, LegacyPath.Knowledge };

    public static string Name(LegacyPath p) => p switch
    {
        LegacyPath.MutualAid => "Mutual aid",
        LegacyPath.Culture => "The new culture",
        LegacyPath.Defence => "Defence of the revolution",
        _ => "Knowledge",
    };

    /// <summary>The legacy a path leaves, and what it does at each level.</summary>
    public static string LegacyName(LegacyPath p) => p switch
    {
        LegacyPath.MutualAid => "Granaries held in common",
        LegacyPath.Culture => "Songs everyone knows",
        LegacyPath.Defence => "A people in arms",
        _ => "Rationalist schools",
    };

    public static TechEffects LegacyEffects(LegacyPath p, int level) => p switch
    {
        LegacyPath.MutualAid => new TechEffects { Aid = 0.15 * level, FoodProduction = 0.03 * level },
        LegacyPath.Culture => new TechEffects { Culture = 0.1 * level, Enthusiasm = 0.0004 * level },
        LegacyPath.Defence => new TechEffects { Defense = 0.05 * level },
        _ => new TechEffects { Science = 0.1 * level, TrustGrowth = 0.03 * level },
    };

    /// <summary>Each path's three milestones: what to do, and how far along the movement is (1 = reached).</summary>
    public static (string Label, double Progress)[] Milestones(World w, LegacyPath p)
    {
        var s = w.Legacy;
        var members = w.Federation.Where(c => !c.Occupied).ToList();
        double Mean(Func<Commune, double> f) => members.Count == 0 ? 0 : members.Average(f);
        // A legacy is the whole federation's: with many of the chapter's communes lost, every milestone recedes.
        double held = Math.Min(1, members.Count / Math.Max(1, 0.75 * s.CommunesAtStart));
        return Scale(p switch
        {
            LegacyPath.MutualAid => new[]
            {
                ("A season without hunger anywhere (13 weeks)", s.QuietTicks * w.Weeks / 13),
                ("Wellbeing 70% across the federation", Mean(c => c.Wellbeing) / 0.7),
                ("No commune below 50% wellbeing, for 8 weeks", s.SharedTicks * w.Weeks / 8),
            },
            LegacyPath.Culture => new[]
            {
                ("Culture 25% across the federation", Mean(c => c.Culture) / 0.25),
                ("Culture 40% across the federation", Mean(c => c.Culture) / 0.4),
                ("Culture 55% and enthusiasm 75%", Math.Min(Mean(c => c.Culture) / 0.55, w.Enthusiasm / 0.75)),
            },
            LegacyPath.Defence => new[]
            {
                ("Throw back 12 attacks", s.Repulsed / 12.0),
                ("Hold every front for 26 weeks without losing a town", s.HeldTicks * w.Weeks / 26),
                ("Exhaust an enemy (50%) or force it to make peace",
                    s.PeaceMade ? 1 : w.Enemies.Count == 0 ? 0 : w.Enemies.Max(e => e.Exhaustion) / 0.5),
            },
            _ => new[]
            {
                ("Practical education 30% across the federation", Mean(c => c.PracticalEducation) / 0.3),
                ("Four technologies new to the federation", (Techs(w) - s.TechsAtStart) / 4.0),
                ("Practical and political education 50%", Math.Min(Mean(c => c.PracticalEducation), Mean(c => c.PoliticalEducation)) / 0.5),
            },
        }, held);
    }

    private static (string Label, double Progress)[] Scale((string Label, double Progress)[] ms, double held) =>
        ms.Select(m => (m.Label, m.Progress * held)).ToArray();

    private static int Techs(World w) => w.Federation.SelectMany(c => c.Techs).Distinct().Count();

    internal static void Start(World w)
    {
        var s = w.Legacy;
        s.ChapterStart = w.Tick;
        int length = w.Scenario.IsCampaign ? (int)Math.Ceiling(ChapterWeeks / w.Weeks) : w.Scenario.EndTick;
        s.ChapterEnd = Math.Min(w.Scenario.EndTick, w.Tick + length);
        // A short last chapter in a campaign is folded into the one before.
        if (w.Scenario.EndTick - s.ChapterEnd < length / 2) s.ChapterEnd = w.Scenario.EndTick;
        s.Reached.Clear();
        s.QuietTicks = s.SharedTicks = s.Repulsed = s.HeldTicks = 0;
        s.PeaceMade = false;
        s.AtWar.Clear();
        s.TechsAtStart = Techs(w);
        s.CommunesAtStart = w.Federation.Count(c => !c.Occupied);
        // What already stands when the chapter opens counts, but earns nothing.
        foreach (var p in Paths)
        {
            var ms = Milestones(w, p);
            for (int i = 0; i < ms.Length; i++)
                if (ms[i].Progress >= 1) s.Reached.Add($"{p}:{i}");
        }
    }

    internal static void Run(World w)
    {
        var s = w.Legacy;
        var b = w.Balance;
        if (s.ChapterEnd == 0) Start(w);
        var members = w.Federation.Where(c => !c.Occupied).ToList();

        // The counters.
        s.QuietTicks = members.Any(c => c.Starving) ? 0 : s.QuietTicks + 1;
        s.SharedTicks = members.Count > 0 && members.All(c => c.Wellbeing >= 0.5 && !c.Starving) ? s.SharedTicks + 1 : 0;
        var battles = w.Battles.Where(r => r.Tick == w.Tick).ToList();
        s.Repulsed += battles.Count(r => r.Result == BattleResult.Repulsed);
        if (battles.Any(r => r.Result == BattleResult.Fell)) s.HeldTicks = 0;
        else if (members.Any(c => c.Front)) s.HeldTicks++;
        for (int k = 0; k < w.Enemies.Count; k++)
        {
            if (!w.Enemies[k].AtPeace) s.AtWar.Add(k);
            else if (s.AtWar.Contains(k)) s.PeaceMade = true;
        }

        // Milestones reached (from the chapter's ninth week: a milestone is earned, not found).
        foreach (var p in Paths)
        {
            if ((w.Tick - s.ChapterStart) * w.Weeks < 8) break;
            var ms = Milestones(w, p);
            for (int i = 0; i < ms.Length; i++)
            {
                if (s.Has(p, i) || ms[i].Progress < 1) continue;
                s.Reached.Add($"{p}:{i}");
                w.Influence = Math.Min(b.InfluenceCap, w.Influence + b.MilestoneInfluence);
                w.Enthusiasm = Math.Min(1, w.Enthusiasm + b.MilestoneEnthusiasm);
                int n = s.Count(p);
                string toward = n == 3 ? $"All three: {LegacyName(p).ToLowerInvariant()} will be this chapter's lasting legacy."
                              : n == 2 ? $"Two of three: {LegacyName(p).ToLowerInvariant()} will be a legacy of this chapter."
                              : "One of three on this path.";
                w.Emit(Severity.Good, $"{Name(p)}: {ms[i].Label.ToLowerInvariant()}. {toward} (+{b.MilestoneInfluence:0} influence)", title: "A milestone");
            }
        }

        if (w.Tick + 1 >= s.ChapterEnd && w.Tick + 1 < w.Scenario.EndTick) EndChapter(w);
    }

    /// <summary>The chapter closes: paths with two or three milestones become legacies, and the next chapter begins.</summary>
    private static void EndChapter(World w)
    {
        var s = w.Legacy;
        var earned = new List<string>();
        foreach (var p in Paths)
        {
            int n = s.Count(p);
            if (n < 2) continue;
            s.Levels[p.ToString()] = s.Level(p) + (n == 3 ? 2 : 1);
            earned.Add($"{LegacyName(p)} ({Name(p).ToLowerInvariant()}{(n == 3 ? ", lasting" : "")})");
        }
        string years = $"{Ages.Year(w.DateAt(s.ChapterStart).Year + w.Scenario.YearOffset)}–{Ages.Year(w.Year)}";
        s.History.Add($"Chapter {s.Chapter} ({years}): " + (earned.Count == 0 ? "no legacy" : string.Join("; ", earned)));
        w.Emit(Severity.Good,
            $"The end of chapter {s.Chapter} ({years}). " + (earned.Count == 0
                ? "It leaves no legacy: no path reached two milestones."
                : $"It leaves {(earned.Count == 1 ? "a legacy" : "legacies")}: {string.Join("; ", earned)}. They last from now on.")
            + " A new chapter begins.",
            pause: true, title: $"Chapter {s.Chapter} ends");
        CrisisSystem.End(w);
        s.Chapter++;
        Start(w);
    }

    /// <summary>The legacies' standing effects, for World.StandingEffects.</summary>
    internal static IEnumerable<(string Label, TechEffects Effects)> Effects(World w)
    {
        foreach (var p in Paths)
            if (w.Legacy.Level(p) is int level and > 0)
                yield return ($"Legacy: {LegacyName(p).ToLowerInvariant()}", LegacyEffects(p, level));
    }
}
