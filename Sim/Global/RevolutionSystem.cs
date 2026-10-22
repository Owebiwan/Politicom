namespace Politicom.Sim.Global;

/// <summary>
/// Revolutions abroad. Workers radicalized past a threshold may rise; the rising is fought out
/// over several turns and is crushed, or wins. A libertarian victory makes a sister federation,
/// which later offers to federate: its largest cities then join the federation as communes. An
/// authoritarian victory makes a rival, likelier the more centralized the federation itself has
/// become, because revolutions copy what they see.
/// </summary>
public static class RevolutionSystem
{
    internal static void Run(World w)
    {
        var b = w.Balance;
        foreach (var s in w.States)
        {
            s.Repression = Math.Max(0, s.Repression - 0.008);
            if (s.Sister || s.Ideology == Ideology.Communist && s.RevolutionCount > 0) continue;

            if (s.Uprising > 0)
            {
                if (--s.Uprising == 0) Resolve(w, s);
                continue;
            }
            double over = s.Radicalization - b.RevolutionThreshold;
            if (over > 0 && w.Rng.Chance(b.UprisingChance * over))
            {
                s.Uprising = b.UprisingTicks;
                s.UprisingSupport = 0;
                w.Emit(Severity.Decision, $"Revolution in {s.Name}! Its workers have risen. The outcome will be decided in {w.Turns(b.UprisingTicks)}; " +
                                          "you can send support from the World tab.", pause: true);
            }
        }
    }

    /// <summary>Chance a rising succeeds: radical workers win, strong and repressive states hold.</summary>
    public static double SuccessChance(World w, WorldState s) =>
        Math.Clamp(0.2 + s.Radicalization - 0.05 * s.Power - 0.5 * s.Repression + s.UprisingSupport
                   + 0.08 * w.States.Count(o => o.Sister && s.Neighbours.Contains(o.Id)), 0.05, 0.9);

    /// <summary>Chance a successful revolution goes authoritarian.</summary>
    public static double AuthoritarianChance(World w, WorldState s) =>
        Math.Clamp(0.25 + 0.45 * w.Drift.Value / 100 - 0.15 * w.States.Count(o => o.Sister) / Math.Max(1.0, w.States.Count / 10.0), 0.1, 0.9);

    private static void Resolve(World w, WorldState s)
    {
        string country = StateText.Country(s.Name);
        if (!w.Rng.Chance(SuccessChance(w, s)))
        {
            s.Radicalization *= 0.3;
            s.Repression = Math.Min(1, s.Repression + 0.5);
            s.Stance = Math.Clamp(s.Stance - 0.2, -1, 1);
            w.Emit(Severity.Danger, $"The revolution in {s.Name} is crushed. Its organizers are shot or imprisoned; repression hardens.");
            return;
        }
        s.RevolutionCount++;
        s.Actions = StateActions.None;
        s.Repression = 0;
        if (w.Rng.Chance(AuthoritarianChance(w, s)))
        {
            s.Ideology = Ideology.Communist;
            s.Name = $"Workers' Republic of {country}";
            s.Stance = 0.2;
            w.SetBaseline(s.Id, 0.2);
            w.Emit(Severity.Warning, $"The revolution wins in {country} — and a party takes power in its name: the {s.Name}. " +
                                     "A new rival for the loyalty of the world's workers.", pause: true);
            return;
        }
        s.Ideology = Ideology.Libertarian;
        s.Name = $"Free Communes of {country}";
        s.Stance = 0.9;
        w.SetBaseline(s.Id, 0.9);
        w.Emit(Severity.Good, $"The revolution wins in {country}: the {s.Name} — a sister federation. Its workers will send arms and volunteers.", pause: true);
        if (w.Scenario.States.FirstOrDefault(d => d.Id == s.Id) is { Cities.Count: > 0 }) OfferFederation(w, s);
    }

    /// <summary>Schedules the sister federation's offer to join yours, as a decision.</summary>
    private static void OfferFederation(World w, WorldState s)
    {
        var b = w.Balance;
        var ev = new EventDefinition
        {
            Id = $"federate_{s.Id}_{w.Tick}",
            Tick = w.Tick + b.FederateDelay,
            Title = $"{s.Name} proposes to federate",
            Text = $"The assemblies of the {s.Name} vote to join your federation. Its cities would become communes of one free federation, sharing aid, ideas and defense.",
            DefaultOption = 1,
            ExpiresAfter = 6,
            Options =
            {
                new EventOption
                {
                    Label = "Federate",
                    Description = "One federation across borders. More to hold together — and more to share.",
                    Effects = new EventEffects { Federate = s.Id, Drift = 2 },
                },
                new EventOption
                {
                    Label = "Remain sister federations",
                    Description = "Allies, each governing itself.",
                    Effects = new EventEffects { Stances = { [s.Id] = 0.05 } },
                },
            },
        };
        w.Scenario.Events.Add(ev);
    }

    /// <summary>A sister federation's largest cities join the federation as communes.</summary>
    internal static void Federate(World w, string stateId)
    {
        var s = w.States.FirstOrDefault(x => x.Id == stateId);
        var def = w.Scenario.States.FirstOrDefault(d => d.Id == stateId);
        if (s is null || def is null || s.Federated || !s.Sister) return;
        var cities = def.Cities.Take(w.Balance.FederateCities).ToList();
        if (cities.Count == 0) return;
        s.Federated = true;
        w.AddCommunes(cities.Select(c => new CommuneDefinition
        {
            Name = c.Name,
            Description = $"A commune of the former {StateText.Country(s.Name)}, federated after its revolution.",
            Kind = CommuneKind.Industrial,
            Lon = c.Lon,
            Lat = c.Lat,
            Population = Math.Clamp(Math.Round(Math.Sqrt(Math.Max(c.Pop, 1)) / 60), 8, 60),
            Collectivism = 0.6, Militancy = 0.5, Federalism = 0.7,
            Practical = 0.3, Political = 0.35,
            Techs = new[] { "open_assemblies", "libertarian_press" }.Where(w.Techs.Contains).ToArray(),
        }).ToList());
        w.Emit(Severity.Good, $"{string.Join(", ", cities.Select(c => c.Name))} join the federation.", pause: true);
    }
}
