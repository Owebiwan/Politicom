namespace Politicom.Sim.Systems;

/// <summary>
/// Generations, as Crusader Kings passes from one generation to the next: once a year figures
/// grow older; past sixty they may die of age, and past sixty-five they may retire, ending
/// their work with a few words on what they did. New militants rise from the communes with the
/// strongest political life, more often when the movement has few figures at work; they take
/// their role from their town (a commander from the front, an organizer from the works, an
/// educator where people read) and their character from it too (militant towns breed the brave
/// and the zealous, well-read ones the honest and the humble), and they start out loyal to the
/// movement that raised them, and close to any figure working nearby, from whom they learned.
/// </summary>
internal static class GenerationSystem
{
    public static void Run(World w)
    {
        int year = w.Year;
        if (w.FigureYear == 0) { w.FigureYear = year; return; }
        if (year == w.FigureYear) return;
        w.FigureYear = year;
        foreach (var f in w.Figures.Where(f => f.Alive && f.Born != 0).ToList()) Age(w, f, year - f.Born);
        Rise(w, year);
    }

    private static void Age(World w, Figure f, int age)
    {
        double death = age < 55 ? 0.005 : age < 65 ? 0.02 : age < 75 ? 0.06 : 0.15;
        if (w.Rng.Chance(death))
        {
            FigureSystem.Kill(w, f, $"{f.Name} has died, aged {age}. {Legacy(w, f)}");
            return;
        }
        if (!f.Retired && age >= 65 && w.Rng.Chance(age >= 72 ? 0.5 : 0.25))
        {
            f.Retired = true;
            f.Commune = null;
            f.Empowered = false;
            f.Asking = false;
            w.Emit(Severity.Info, $"{f.Name} retires from the work at {age}. {Legacy(w, f)}", title: $"{f.Name} retires");
        }
    }

    /// <summary>A line on what they leave behind.</summary>
    private static string Legacy(World w, Figure f) =>
        f.Empowered ? "They held power to the end; whoever takes their place inherits an office."
        : f.Prestige >= 0.6 ? $"Their name will be remembered in the {Figure.RoleName(f.Role)}s' songs."
        : f.Regard <= -0.4 ? "They leave bitter, and their following with them."
        : "Others carry on what they began.";

    private static void Rise(World w, int year)
    {
        var members = w.Federation.ToList();
        if (members.Count == 0) return;
        int working = w.Figures.Count(f => f.Alive && !f.Retired);
        double chance = working < 3 ? 0.9 : working < 5 ? 0.45 : working < 7 ? 0.15 : 0;
        if (!w.Rng.Chance(chance)) return;

        // Where a militant is likeliest to come from: a big town with a strong political life.
        var weights = members.Select(c => c.Population * (0.3 + c.PoliticalEducation) * (0.5 + c.Culture)).ToList();
        var home = w.Rng.PickWeighted(members, weights);
        var role = home.Front ? FigureRole.Commander
            : home.Kind == CommuneKind.Industrial && w.Rng.Chance(0.6) ? FigureRole.Organizer
            : home.PoliticalEducation > 0.4 && w.Rng.Chance(0.5) ? FigureRole.Educator
            : FigureRole.Orator;
        var traits = Traits(w, home);
        var trades = FigureSystem.Trades[role];
        string trade = trades[w.Rng.Next(trades.Length)];
        string name = $"The {trade} of {home.Name}";
        if (w.Figures.Any(f => f.Name == name)) name = $"The {trade} of {home.Name} ({year})";
        var f = new Figure
        {
            Id = w.Figures.Count, Name = name, Role = role, Commune = home.Id, Traits = traits,
            Prestige = 0.2, Ambition = Math.Round(0.1 + w.Rng.NextDouble() * 0.3 + (traits.Contains(FigureTrait.Ambitious) ? 0.15 : 0), 2),
            Born = year - 22 - w.Rng.Next(14), Regard = 0.15,
            Description = $"A {trade} from {home.Name}, of the generation the revolution raised.",
        };
        // They learned from whoever was working nearby.
        var mentor = w.Figures.Where(o => o.Alive && !o.Retired && o.Commune is int oc && (oc == home.Id || w.Trust.Neighbors(home.Id).Contains(oc)))
            .OrderByDescending(o => o.Prestige).FirstOrDefault();
        if (mentor != null)
        {
            Figure.Feel(f, mentor, 0.45);
            f.Memories.Add(new FigureMemory(w.Tick, $"learned the work from {mentor.Name}", 0));
        }
        w.Figures.Add(f);
        w.Emit(Severity.Good, $"A new voice: {name}, {Figure.RoleName(role)}, {string.Join(" and ", traits.Select(Figure.TraitName))}"
                              + (mentor != null ? $", who learned the work from {mentor.Name}." : "."), home.Id, title: $"A new voice in {home.Name}");
    }

    /// <summary>Two traits, leaning toward what their town is like.</summary>
    private static List<FigureTrait> Traits(World w, Commune c)
    {
        double militancy = CommuneValues.To01(c.Values.Militancy);
        var weights = new Dictionary<FigureTrait, double>
        {
            [FigureTrait.Brave] = 0.5 + militancy + (c.Front ? 0.5 : 0),
            [FigureTrait.Cautious] = 1.2 - militancy,
            [FigureTrait.Zealous] = 0.4 + militancy + c.PoliticalEducation,
            [FigureTrait.Pragmatic] = 0.6 + c.PracticalEducation - c.PoliticalEducation * 0.5,
            [FigureTrait.Honest] = 0.4 + 1.2 * c.PoliticalEducation,
            [FigureTrait.Humble] = 0.4 + c.PoliticalEducation + c.Culture,
            [FigureTrait.Ambitious] = 0.5 + Math.Max(0, c.PracticalEducation - c.PoliticalEducation) * 2 + w.Drift.Value / 100,
            [FigureTrait.Vain] = 0.4 + w.Drift.Value / 100,
            [FigureTrait.Eloquent] = 0.4 + c.Culture * 1.5,
        };
        var opposites = new[] { (FigureTrait.Brave, FigureTrait.Cautious), (FigureTrait.Ambitious, FigureTrait.Humble), (FigureTrait.Zealous, FigureTrait.Pragmatic), (FigureTrait.Honest, FigureTrait.Vain) };
        var first = w.Rng.PickWeighted(weights.Keys.ToList(), weights.Values.Select(v => Math.Max(0.05, v)).ToList());
        var rest = weights.Keys.Where(t => t != first && !opposites.Any(o => (o.Item1 == first && o.Item2 == t) || (o.Item2 == first && o.Item1 == t))).ToList();
        var second = w.Rng.PickWeighted(rest, rest.Select(t => Math.Max(0.05, weights[t])).ToList());
        return new List<FigureTrait> { first, second };
    }
}
