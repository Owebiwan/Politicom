namespace Politicom.Sim.Systems;

/// <summary>
/// The movement's notable figures (<see cref="Figure"/>): what they do where they are, how their
/// prestige and ambition grow, when they ask for power, and when commanders fall.
/// </summary>
internal static class FigureSystem
{
    // Trades for generated figures, by role: era-neutral enough for any start.
    private static readonly Dictionary<FigureRole, string[]> Trades = new()
    {
        [FigureRole.Commander] = new[] { "smith", "carter", "miner", "shepherd", "boatman" },
        [FigureRole.Orator] = new[] { "weaver", "baker", "tanner", "washerwoman", "cobbler" },
        [FigureRole.Organizer] = new[] { "mason", "cooper", "dockworker", "potter", "miller" },
        [FigureRole.Educator] = new[] { "schoolteacher", "scribe", "midwife", "bookbinder", "healer" },
    };

    /// <summary>The scenario's figures, or (for generated starts) one of each role from the largest communes.</summary>
    public static void Create(World w, Rng rng)
    {
        var defs = w.Scenario.Figures;
        if (defs.Count == 0)
        {
            var big = w.Communes.Where(c => c.InFederation).OrderByDescending(c => c.Population).ToList();
            var front = w.Communes.Where(c => c.Front).OrderByDescending(c => c.Population).FirstOrDefault();
            if (big.Count == 0) return;
            foreach (var (role, i) in new[] { (FigureRole.Commander, 0), (FigureRole.Orator, 0), (FigureRole.Organizer, 1), (FigureRole.Educator, 2) })
            {
                var home = role == FigureRole.Commander && front != null ? front : big[Math.Min(i, big.Count - 1)];
                var trades = Trades[role];
                string trade = trades[rng.Next(trades.Length)];
                defs.Add(new FigureDefinition
                {
                    Name = $"The {trade} of {home.Name}", Role = role, Commune = home.Name,
                    Ambition = Math.Round(0.15 + rng.NextDouble() * 0.5, 2), Prestige = 0.25,
                    Description = $"A {trade} the assemblies of {home.Name} listen to.",
                });
            }
        }
        foreach (var d in defs)
        {
            var home = w.Communes.FirstOrDefault(c => c.Name == d.Commune);
            if (home is null) throw new InvalidDataException($"Figure '{d.Name}' starts in unknown commune '{d.Commune}'.");
            w.Figures.Add(new Figure
            {
                Id = w.Figures.Count, Name = d.Name, Role = d.Role, Description = d.Description,
                Commune = home.Id, Prestige = d.Prestige, Ambition = d.Ambition,
            });
        }
    }

    private static IEnumerable<(Figure Figure, Commune Commune)> AtWork(World w)
    {
        foreach (var f in w.Figures)
            if (f.Alive && f.Commune is int id && w.Communes[id].InFederation)
                yield return (f, w.Communes[id]);
    }

    /// <summary>A figure's strength: grows with prestige, doubled once empowered.</summary>
    private static double Weight(Figure f) => (0.5 + f.Prestige) * (f.Empowered ? 2 : 1);

    /// <summary>Extra defence (a fraction) commanders give a commune: their own, and half to their neighbours.</summary>
    public static double Defense(World w, Commune c)
    {
        double extra = 0;
        foreach (var (f, at) in AtWork(w))
        {
            if (f.Role != FigureRole.Commander) continue;
            if (at.Id == c.Id) extra += w.Balance.FigureDefense * Weight(f);
            else if (w.Trust.Neighbors(at.Id).Contains(c.Id)) extra += w.Balance.FigureDefense * Weight(f) * 0.5;
        }
        return extra;
    }

    /// <summary>Drift from figures' gathered power: prestige times ambition, much more once empowered.</summary>
    public static double Drift(World w) =>
        AtWork(w).Sum(x => w.Balance.FigureDrift * x.Figure.Hunger * (x.Figure.Empowered ? 3 : 1)) * w.Weeks;

    public static void Run(World w)
    {
        var b = w.Balance;
        foreach (var f in w.Figures.Where(f => f.Alive && f.Resting > 0))
            if (--f.Resting == 0) w.Emit(Severity.Info, $"{f.Name} is ready to take up work again.");

        foreach (var (f, c) in AtWork(w).ToList())
        {
            double k = Weight(f) * w.Weeks;
            switch (f.Role)
            {
                case FigureRole.Orator:
                    foreach (var n in w.Trust.Neighbors(c.Id))
                        if (w.Communes[n].InFederation) w.Trust.Add(c.Id, n, b.FigureTrust * k);
                    w.Enthusiasm = Math.Min(1, w.Enthusiasm + b.FigureEnthusiasm * k);
                    break;
                case FigureRole.Organizer:
                    c.Food += c.Population * b.FigureProduce * k;
                    c.Goods += c.Population * b.FigureProduce * k;
                    break;
                case FigureRole.Educator:
                    c.PoliticalEducation += b.FigureEducation * k * (1 - c.PoliticalEducation);
                    foreach (var n in w.Trust.Neighbors(c.Id))
                    {
                        var d = w.Communes[n];
                        if (d.InFederation) d.PoliticalEducation += b.FigureEducation * 0.5 * k * (1 - d.PoliticalEducation);
                    }
                    break;
            }
            // Prestige grows with the work; a commander's faster while the front holds. Ambition follows
            // prestige, slower where the commune's political education is high (it keeps them honest).
            double gain = b.FigurePrestigeGain * w.Weeks * (f.Role == FigureRole.Commander && c.Front && c.Pressure <= c.Defense ? 2 : 1);
            f.Prestige = Math.Min(1, f.Prestige + gain * (1 - f.Prestige));
            f.Ambition = Math.Min(1, f.Ambition + b.FigureAmbitionGrowth * f.Prestige * (1 - c.PoliticalEducation) * w.Weeks);

            // A commander at a losing front may fall.
            if (f.Role == FigureRole.Commander && c.Front && c.Pressure > c.Defense && w.Rng.Chance(b.FigureDeathRate * w.Weeks))
            {
                Kill(w, f, $"{f.Name} has fallen at {c.Name}. The column carries the body home; the commune mourns.");
                continue;
            }
            // Ask for power once prestige and ambition run high.
            if (!f.Asking && !f.Empowered && f.Hunger >= b.FigureBidThreshold) Ask(w, f, c);
        }
    }

    public static void Kill(World w, Figure f, string text)
    {
        if (!f.Alive) return;
        f.Alive = false;
        f.Asking = false;
        w.Enthusiasm = Math.Max(0, w.Enthusiasm - w.Balance.FigureDeathEnthusiasm);
        w.Emit(Severity.Danger, text, title: $"{f.Name} is dead");
    }

    private static void Ask(World w, Figure f, Commune c)
    {
        f.Asking = true;
        string what = f.Role switch
        {
            FigureRole.Commander => $"asks to command all the militias of the front, with the power to appoint officers",
            FigureRole.Orator => $"asks the congress to make them general secretary of the movement",
            FigureRole.Organizer => $"asks for a council of the economy with them at its head, to direct the collectives",
            _ => $"asks to run the movement's press and schools as a single office under them",
        };
        w.Scenario.Events.Add(new EventDefinition
        {
            Id = $"figure_bid_{f.Id}_{w.Tick}",
            Tick = w.Tick + 1,
            Title = $"{f.Name} asks for power",
            Figure = f.Name,
            Text = $"{f.Name}, the movement's {Figure.RoleName(f.Role)} in {c.Name}, {what}. Their prestige is high ({f.Prestige:P0}), " +
                   $"and many would follow them. It would make them far more effective, and it is exactly how a revolution grows a new state.",
            DefaultOption = 1,
            ExpiresAfter = 4,
            Options =
            {
                new EventOption
                {
                    Label = "Grant it",
                    Description = $"Twice as effective, and drift: now, and every {w.Turn} while they hold it.",
                    Effects = new EventEffects { Figure = f.Name, FigureChoice = FigureChoice.Empower, Drift = w.Balance.FigureEmpowerDrift },
                },
                new EventOption
                {
                    Label = "Rotate them back to the ranks",
                    Description = "Mandates are temporary. They lose much of their prestige, rest a while, and their followers are disappointed.",
                    Effects = new EventEffects { Figure = f.Name, FigureChoice = FigureChoice.Rotate },
                },
            },
        });
    }

    /// <summary>Back to the ranks: prestige and ambition fall, a short rest, a little disappointment, a little less drift.</summary>
    public static void Rotate(World w, Figure f)
    {
        var b = w.Balance;
        if (f.Commune is int id && w.Communes[id].InFederation) w.Communes[id].Wellbeing = Math.Max(0, w.Communes[id].Wellbeing - b.FigureRotateWellbeing);
        f.Prestige *= 0.45;
        f.Ambition *= 0.4;
        f.Empowered = false;
        f.Asking = false;
        f.Commune = null;
        f.Resting = b.FigureRestTicks;
        w.PendingDriftBump -= b.FigureRotateDriftRelief;
    }

    public static void Empower(World w, Figure f)
    {
        f.Empowered = true;
        f.Asking = false;
    }
}
