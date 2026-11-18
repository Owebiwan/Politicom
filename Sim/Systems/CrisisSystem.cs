namespace Politicom.Sim.Systems;

/// <summary>
/// The crisis of the age (as Civilization VII's age crises): in campaigns and open starts, the
/// last quarter of each chapter brings one, chosen for the age and the moment: a plague (cholera,
/// influenza), the hungry years, the enemy's great offensive, or the counter-revolution within.
/// It breaks, rises to its height and comes to a head in three stages, each heavier than the last,
/// and at each the movement chooses a policy, each double-edged, that holds until the crisis ends
/// with the chapter. Historical scenarios keep their own history and have none.
/// </summary>
public static class CrisisSystem
{
    public sealed record Policy(string Id, string Label, string Description, TechEffects Effects, EventEffects? Now = null);

    public static bool Enabled(World w) => w.Balance.Crises > 0 && (
        w.Scenario.IsCampaign || w.Scenario.Id.StartsWith("open:") || w.Scenario.Id.StartsWith("sandbox"));

    public static string Name(CrisisKind kind, int year, string enemy = "") => kind switch
    {
        CrisisKind.Plague => year < 1700 ? "The plague" : year < 1900 ? "Cholera" : year < 1930 ? "The influenza" : "The epidemic",
        CrisisKind.HungerYears => year < 1800 ? "The years of dearth" : "The hungry years",
        CrisisKind.Offensive => enemy.Length > 0 ? $"The great offensive of the {enemy}" : "The great offensive",
        _ => "The counter-revolution within",
    };

    internal static void Run(World w)
    {
        if (!Enabled(w)) return;
        var s = w.Legacy;
        int length = s.ChapterEnd - s.ChapterStart;
        if (length * w.Weeks < 40) return;
        int window = Math.Max(3, length / 4);
        int start = s.ChapterEnd - window;
        if (s.Crisis is null)
        {
            if (w.Tick >= start && w.Tick < s.ChapterEnd - 2) Begin(w, start, s.ChapterEnd);
            return;
        }
        var c = s.Crisis;
        if (c.Stage == 0 || w.Tick >= s.ChapterEnd) return;
        int stage = 1 + Math.Min(2, (w.Tick - c.Start) * 3 / Math.Max(1, c.End - c.Start));
        if (stage > c.Stage)
        {
            c.Stage = stage;
            Stage(w, c);
        }
        Pressure(w, c);
    }

    private static void Begin(World w, int start, int end)
    {
        var s = w.Legacy;
        var rng = w.Rng;
        int year = w.Year;
        var fighting = Enumerable.Range(0, w.Enemies.Count).Where(k => !w.Enemies[k].AtPeace).ToList();
        double party = w.Federation.Any() ? w.Federation.Average(c => c.PartyInfluence) : 0;
        var weights = new List<(CrisisKind Kind, double Weight)>
        {
            (CrisisKind.Plague, year < 1700 ? 3 : year < 1920 ? 2 : 1),
            (CrisisKind.HungerYears, 2),
            // Short games already end in their own great offensive; campaigns get one per chapter.
            (CrisisKind.Offensive, fighting.Count > 0 && w.Scenario.IsCampaign ? 3 : 0),
            (CrisisKind.CounterRevolution, w.Drift.Value >= 30 || party > 0.05 ? 2.5 : 1),
        };
        weights.RemoveAll(x => x.Weight <= 0 || x.Kind == s.LastCrisis);
        if (w.Balance.CrisisOnly >= 0) weights.RemoveAll(x => (int)x.Kind != (int)w.Balance.CrisisOnly);
        if (weights.Count == 0) return;
        double total = weights.Sum(x => x.Weight), roll = rng.NextDouble() * total;
        var kind = weights[^1].Kind;
        foreach (var (k, wt) in weights)
        {
            if (roll < wt) { kind = k; break; }
            roll -= wt;
        }
        int enemy = kind == CrisisKind.Offensive ? fighting.OrderByDescending(k => w.Enemies[k].EffectiveStrength).First() : -1;
        var crisis = new Crisis
        {
            Kind = kind, Start = start, End = end, Enemy = enemy, Stage = 1,
            Name = Name(kind, year, enemy >= 0 ? w.Enemies[enemy].Name : ""),
            CommunesAtStart = w.Federation.Count(),
        };
        s.Crisis = crisis;
        s.LastCrisis = kind;
        if (kind == CrisisKind.Plague)
        {
            // It comes in by the busiest road: the largest town, or a port.
            var first = w.Federation.Where(c => !c.Occupied).OrderByDescending(c => c.Population).FirstOrDefault();
            if (first != null) crisis.Afflicted.Add(first.Id);
        }
        if (kind == CrisisKind.Offensive)
        {
            var e = w.Enemies[enemy];
            double typical = e.Offensives.Count > 0 ? e.Offensives.Average(o => o.Strength) : Math.Max(1, e.Strength);
            for (int i = 0; i < 3; i++)
            {
                int from = start + (end - start) * i / 3, to = start + (end - start) * (i + 1) / 3;
                e.Offensives.Add(new Offensive { Name = $"{crisis.Name}, {Stages[i]}", Tick = from, Duration = Math.Max(1, (to - from) / 2), Strength = Math.Round(typical * w.Balance.CrisisOffensive[i], 1) });
            }
        }
        Stage(w, crisis);
    }

    private static readonly string[] Stages = { "it breaks", "at its height", "the reckoning" };

    /// <summary>A stage opens: what it brings, and the choice of policy.</summary>
    private static void Stage(World w, Crisis c)
    {
        var (title, text) = Telling(w, c);
        var options = Policies(c.Kind, c.Stage);
        w.Scenario.Events.Add(new EventDefinition
        {
            Id = $"crisis_{c.Kind}_{c.Stage}_{w.Tick}",
            Tick = w.Tick + 1,
            Title = $"{c.Name}: {title}",
            Text = text,
            DefaultOption = options.Length - 1,
            ExpiresAfter = 4,
            Options = options.Select(p => new EventOption
            {
                Label = p.Label,
                Description = p.Description + (p.Effects.Describe().Any() ? " Until the crisis ends: " + string.Join(", ", p.Effects.Describe().Select(d => d.Text.Replace(" (holders)", ""))) + "." : ""),
                Effects = Merge(p.Now, p.Id),
            }).ToList(),
        });
    }

    private static EventEffects Merge(EventEffects? now, string policy)
    {
        var e = now ?? new EventEffects();
        e.CrisisPolicy = policy;
        return e;
    }

    private static (string Title, string Text) Telling(World w, Crisis c)
    {
        string first = c.Afflicted.Count > 0 ? w.Communes[c.Afflicted.First()].Name : "the towns";
        return (c.Kind, c.Stage) switch
        {
            (CrisisKind.Plague, 1) => ("the first cases", $"The sickness has come to {first}. It will follow the roads, the rails and the aid convoys from town to town. The assemblies must decide how to meet it."),
            (CrisisKind.Plague, 2) => ("at its height", "The sickness is in town after town. Workshops stand empty, the fields go untended, and every assembly counts its dead."),
            (CrisisKind.Plague, _) => ("the reckoning", "It is passing at last. What the federation does now will be remembered: how it buried its dead, and whom it blamed."),
            (CrisisKind.HungerYears, 1) => ("a poor harvest", "The harvest has failed across the region: drought, then blight. There will be less to eat for a long while."),
            (CrisisKind.HungerYears, 2) => ("the lean months", "The granaries are emptying. The towns that grow their own food are tempted to keep it."),
            (CrisisKind.HungerYears, _) => ("the last of the stores", "The worst is nearly over, but the last stores must last until the next harvest comes in."),
            (CrisisKind.Offensive, 1) => ("the build-up", $"The {(c.Enemy >= 0 ? w.Enemies[c.Enemy].Name : "enemy")} is massing men and guns along the front: an offensive greater than any before."),
            (CrisisKind.Offensive, 2) => ("the assault", "The offensive has broken on the line. The front is holding in places and giving way in others."),
            (CrisisKind.Offensive, _) => ("the last push", "The enemy throws in everything it has left. If the line holds now, it holds for good."),
            (_, 1) => ("whispers", "Within the movement, some say the assemblies are too slow for these times: the party offers order, the committees want powers. Cells are forming."),
            (_, 2) => ("the split", "The quarrel is in the open: rival meetings, rival papers, rival guards. Communes take sides."),
            (_, _) => ("the showdown", "It comes to a head: the movement will settle who decides, and how."),
        };
    }

    /// <summary>The policies offered at each stage: the first ones act, the last one is to wait it out.</summary>
    public static Policy[] Policies(CrisisKind kind, int stage) => (kind, stage) switch
    {
        (CrisisKind.Plague, 1) => new[]
        {
            new Policy("plague_quarantine", "Quarantine the stricken towns", "Guards on the roads and the rails stopped: the sickness spreads far more slowly, but so does everything else.",
                new TechEffects { TrustGrowth = -0.1, GoodsProduction = -0.05 }),
            new Policy("plague_open", "Keep the roads open", "Trade and aid go on as before, and so does the sickness.", new TechEffects()),
        },
        (CrisisKind.Plague, 2) => new[]
        {
            new Policy("plague_physicians", "Brigades of physicians and nurses", "Doctors, students and volunteers go where it is worst: half as many die.",
                new TechEffects { Wellbeing = 0.02 }, new EventEffects { Influence = -15 }),
            new Policy("plague_alone", "Each commune for itself", "Every town looks after its own, and its neighbours less.", new TechEffects { Aid = -0.3 }),
        },
        (CrisisKind.Plague, _) => new[]
        {
            new Policy("plague_rebuild", "Bury the dead together, and rebuild", "Common funerals and work brigades in the stricken towns.",
                new TechEffects { TrustGrowth = 0.1, GoodsProduction = -0.05 }),
            new Policy("plague_blame", "Blame the outsiders", "It came from elsewhere, people say, and someone must answer for it. A guard is set on strangers.",
                new TechEffects { Wellbeing = 0.02, DriftPerTick = 0.05, Contagion = -0.2 }),
        },
        (CrisisKind.HungerYears, 1) => new[]
        {
            new Policy("hunger_ration", "Ration by need across the federation", "What there is is shared by need, through the federal pool and between neighbours.",
                new TechEffects { Aid = 0.4, Wellbeing = -0.02 }),
            new Policy("hunger_market", "Let each town trade for what it lacks", "Towns bargain for grain: more is grown for sale, but the better-off eat first.",
                new TechEffects { FoodProduction = 0.04, FreeRiding = -0.15 }),
        },
        (CrisisKind.HungerYears, 2) => new[]
        {
            new Policy("hunger_requisition", "Requisition from the villages that hoard", "Food committees search the barns. There is more to share, and bitterness.",
                new TechEffects { DriftPerTick = 0.04, TrustGrowth = -0.1 }, new EventEffects { Food = 1.5 }),
            new Policy("hunger_appeal", "Appeal to the movements abroad", "Workers abroad send grain ships and convoys, at a cost in favours.",
                new TechEffects(), new EventEffects { Influence = -20, Food = 2 }),
            new Policy("hunger_wait", "Tighten belts", "Nothing is done that cannot be undone.", new TechEffects { Wellbeing = -0.03 }),
        },
        (CrisisKind.HungerYears, _) => new[]
        {
            new Policy("hunger_sow", "Sow new fields together", "Work brigades break new ground for the next harvest.",
                new TechEffects { FoodProduction = 0.06 }, new EventEffects { Influence = -10 }),
            new Policy("hunger_endure", "Hold on until the harvest", "The assemblies ask for patience.", new TechEffects { Wellbeing = -0.02 }),
        },
        (CrisisKind.Offensive, 1) => new[]
        {
            new Policy("offensive_fortify", "Dig in along the whole front", "Trenches, wire and works everywhere the line runs, built with the factories' goods.",
                new TechEffects { Defense = 0.1, GoodsProduction = -0.08 }),
            new Policy("offensive_strike", "Strike first", "Raids and sabotage behind their lines before they are ready.",
                new TechEffects(), new EventEffects { EnemyExhaustion = 0.08, Enthusiasm = 0.02 }),
        },
        (CrisisKind.Offensive, 2) => new[]
        {
            new Policy("offensive_mobilize", "A general mobilization", "Every able body to the front: the line thickens, the fields thin, and command hardens.",
                new TechEffects { Defense = 0.15, FoodProduction = -0.08, DriftPerTick = 0.03 }),
            new Policy("offensive_volunteers", "Columns of volunteers", "Volunteers from the rear, armed with what the federation can spare.",
                new TechEffects { Defense = 0.07 }, new EventEffects { Influence = -10 }),
        },
        (CrisisKind.Offensive, _) => new[]
        {
            new Policy("offensive_hold", "Hold every town", "Not a street given up: the militia stands where it is.", new TechEffects { Defense = 0.1, Wellbeing = -0.04 }),
            new Policy("offensive_trust", "Trust the line as it is", "The columns know their ground. The assemblies leave it to them.", new TechEffects { Enthusiasm = 0.0005 }),
        },
        (_, 1) => new[]
        {
            new Policy("counter_open", "Open the assemblies to the critics", "Let them speak, and answer them: slow and patient.",
                new TechEffects { DriftBrake = 0.04 }, new EventEffects { Influence = -10 }),
            new Policy("counter_expel", "Expel the agitators", "The party's men are put out of the unions and committees. It works, for now, and it sets a precedent.",
                new TechEffects { DriftPerTick = 0.05 }, new EventEffects { PartyInfluence = -0.1 }),
        },
        (_, 2) => new[]
        {
            new Policy("counter_congress", "A congress of all the communes", "Every commune sends recallable delegates to settle it in the open.",
                new TechEffects { TrustGrowth = 0.1 }, new EventEffects { Influence = -15 }),
            new Policy("counter_powers", "Emergency powers for the committees", "The committees act without waiting for the assemblies. Things move faster.",
                new TechEffects { DriftPerTick = 0.08, Defense = 0.05 }),
        },
        (_, _) => new[]
        {
            new Policy("counter_recall", "Recall every delegate", "Every delegate is sent back to their assembly and must be mandated afresh.",
                new TechEffects { DriftBrake = 0.06, Wellbeing = -0.02 }),
            new Policy("counter_wait", "Let it burn itself out", "The assemblies wait for the quarrel to tire.", new TechEffects()),
        },
    };

    private static readonly Dictionary<string, Policy> ById =
        Enum.GetValues<CrisisKind>().SelectMany(k => Enumerable.Range(1, 3).SelectMany(st => Policies(k, st))).ToDictionary(p => p.Id);

    public static Policy? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>A crisis policy chosen in its event.</summary>
    internal static void Adopt(World w, string id)
    {
        if (w.Legacy.Crisis is { } c && Find(id) is { } p && !c.Policies.Contains(id)) c.Policies.Add(id);
    }

    /// <summary>The crisis's weight while it lasts, by stage, beyond its policies.</summary>
    private static TechEffects Weight(Crisis c) => c.Kind switch
    {
        CrisisKind.HungerYears => new TechEffects { FoodProduction = -new[] { 0, 0.08, 0.14, 0.18 }[c.Stage] },
        CrisisKind.CounterRevolution => new TechEffects { DriftPerTick = 0.03 * c.Stage, Wellbeing = -0.015 * c.Stage },
        CrisisKind.Plague => new TechEffects { GoodsProduction = -0.03 * c.Stage },
        _ => new TechEffects(),
    };

    /// <summary>What the crisis does each turn: the plague spreads and kills, the party's cells grow.</summary>
    private static void Pressure(World w, Crisis c)
    {
        var b = w.Balance;
        switch (c.Kind)
        {
            case CrisisKind.Plague:
                double spread = b.PlagueSpread * c.Stage * (c.Policies.Contains("plague_quarantine") ? 0.35 : 1) * w.Weeks;
                double mortality = b.PlagueMortality * c.Stage * (c.Policies.Contains("plague_physicians") ? 0.5 : 1) * w.Weeks;
                foreach (int id in c.Afflicted.ToList())
                {
                    var town = w.Communes[id];
                    if (town.Occupied) continue;
                    double dead = town.Population * mortality;
                    town.Population -= dead;
                    c.Dead += dead;
                    town.PopulationChange.Add(new Part(c.Name, -dead));
                    foreach (int n in w.Trust.Neighbors(id))
                        if (!c.Afflicted.Contains(n) && !w.Communes[n].Occupied && w.Rng.Chance(Math.Min(0.9, spread * (w.Rail(id, n) ? 1.5 : 1))))
                            c.Afflicted.Add(n);
                }
                break;
            case CrisisKind.CounterRevolution:
                foreach (var town in w.Federation)
                    town.PartyInfluence = Math.Min(1, town.PartyInfluence + b.CrisisPartyGrowth * c.Stage * w.Weeks);
                break;
        }
    }

    /// <summary>The crisis ends with the chapter: how the movement came through it.</summary>
    internal static void End(World w)
    {
        if (w.Legacy.Crisis is not { } c) return;
        int lost = c.CommunesAtStart - w.Federation.Count();
        bool weathered = lost <= 0;
        if (weathered)
        {
            w.Enthusiasm = Math.Min(1, w.Enthusiasm + w.Balance.CrisisWeathered);
            w.Influence = Math.Min(w.Balance.InfluenceCap, w.Influence + 10);
        }
        w.Emit(weathered ? Severity.Good : Severity.Warning,
            $"{c.Name} is over." + (c.Dead > 0.05 ? $" It killed {c.Dead:0.0} thousand." : "")
            + (weathered ? " The federation came through it whole, and the assemblies are prouder for it (+enthusiasm, +10 influence)."
                         : $" The federation came through it {lost} commune{(lost == 1 ? "" : "s")} fewer."),
            title: "The crisis passes");
        w.Legacy.Crisis = null;
    }

    /// <summary>Whether a commune has the plague now (for its wellbeing and the map).</summary>
    public static bool Afflicted(World w, Commune c) =>
        w.Legacy.Crisis is { Kind: CrisisKind.Plague, Stage: > 0 } crisis && crisis.Afflicted.Contains(c.Id);

    /// <summary>The crisis's standing effects (its weight and the policies chosen), for World.StandingEffects.</summary>
    internal static IEnumerable<(string Label, TechEffects Effects)> Effects(World w)
    {
        if (w.Legacy.Crisis is not { Stage: > 0 } c) yield break;
        yield return (c.Name, Weight(c));
        foreach (var id in c.Policies)
            if (Find(id) is { } p)
                yield return ($"Crisis policy: {p.Label.ToLowerInvariant()}", p.Effects);
    }
}
