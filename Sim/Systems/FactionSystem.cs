namespace Politicom.Sim.Systems;

/// <summary>
/// Factions (<see cref="Faction"/>): each turn every commune leans toward the factions whose
/// grievance it shares; those that lean far enough join. A faction's power is the share of the
/// federation's people in it, times how strongly they feel, and more with a figure at its head;
/// its strength grows while that power is real and fades when it isn't. While it presses it adds
/// drift. At full strength it puts its demand to the movement: conceding buys peace at a price,
/// refusing angers its communes (the localists leave). Addressing a faction (an action) wins some
/// of its people back.
/// </summary>
internal static class FactionSystem
{
    public static void Run(World w)
    {
        var b = w.Balance;
        if (w.Factions.Count == 0)
            foreach (var k in Enum.GetValues<FactionKind>()) w.Factions.Add(new Faction { Kind = k });
        var members = w.Federation.ToList();
        double people = Math.Max(1, members.Sum(c => c.Population));
        foreach (var f in w.Factions)
        {
            if (w.PendingEvents.Any(p => p.Id.StartsWith($"faction_{f.Kind}_"))) continue; // waiting for an answer
            // Who belongs: join past 0.6, leave below 0.4.
            f.Members = members.Where(c => Affinity(w, c, f.Kind) >= (f.Members.Contains(c.Id) ? 0.4 : 0.6)).Select(c => c.Id).ToList();
            f.Leader = Leader(w, f);
            // Power: half the share of the federation's people, half the share of its communes (many small
            // towns can press as hard as one city), times how strongly they feel.
            double feeling = f.Members.Count == 0 ? 0 : f.Members.Average(id => Math.Min(1, Affinity(w, w.Communes[id], f.Kind)));
            double power = (0.5 * f.Members.Sum(id => w.Communes[id].Population) / people + 0.5 * f.Members.Count / (double)Math.Max(1, members.Count)) * feeling;
            if (f.Leader is int l) power += 0.08 + 0.12 * w.Figures[l].Prestige;
            if (f.Members.Count < 3) power = 0; // two towns are a grumble, not a faction
            double change = b.FactionGrowth * (power - b.FactionThreshold) * w.Weeks;
            bool full = f.Strength >= 1;
            f.Strength = Math.Clamp(f.Strength + change, 0, 1);
            if (full || f.Strength >= 1) Demand(w, f);
        }
    }

    /// <summary>How strongly a commune shares a faction's grievance (about 0..1.2).</summary>
    public static double Affinity(World w, Commune c, FactionKind k) => k switch
    {
        FactionKind.Centralists => (c.Front ? 0.35 : 0) + (c.Front && c.Pressure > c.Defense ? 0.3 : 0)
                                   + 0.5 * (1 - c.PoliticalEducation) - 0.4 * c.Culture + 0.3 * w.Drift.Value / 100,
        FactionKind.Party => 2.0 * c.PartyInfluence,
        FactionKind.Planners => 3 * c.ExpertGap + (c.Kind == CommuneKind.Industrial ? 0.15 : 0) + 0.3 * c.PracticalEducation,
        _ => 1.1 * c.Discontent + 0.3 * (1 - w.Trust.AverageTrust(c.Id, n => w.Communes[n].InFederation)),
    };

    /// <summary>A figure working in a member commune whose character fits the cause.</summary>
    private static int? Leader(World w, Faction f)
    {
        bool Fits(Figure x) => f.Kind switch
        {
            FactionKind.Centralists => x.Has(FigureTrait.Pragmatic) || x.Has(FigureTrait.Ambitious) || x.Empowered,
            FactionKind.Planners => x.Role == FigureRole.Organizer && (x.Has(FigureTrait.Pragmatic) || x.Has(FigureTrait.Ambitious)),
            FactionKind.Localists => x.Regard <= -0.3,
            _ => false, // the party has its own cadres
        };
        return w.Figures.Where(x => x.Alive && x.Commune is int id && f.Members.Contains(id) && Fits(x))
            .OrderByDescending(x => x.Prestige).Select(x => (int?)x.Id).FirstOrDefault();
    }

    /// <summary>Drift from factions pressing their demands: little while they're weak, much as they near their demand.</summary>
    public static double Drift(World w) => w.Factions.Sum(f => w.Balance.FactionDrift * f.Strength * f.Strength) * w.Weeks;

    private static void Demand(World w, Faction f)
    {
        var b = w.Balance;
        var names = f.Members.Select(id => w.Communes[id].Name).ToArray();
        string who = names.Length <= 3 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(3))} and {names.Length - 3} more";
        string leader = f.Leader is int l ? $", led by {w.Figures[l].Name}," : "";
        f.Strength = 0.6; // their demand is made; if refused, they'll be back
        var concede = new EventEffects { Communes = names, Wellbeing = 0.05, Faction = f.Kind.ToString(), FactionStrength = -0.6 };
        var refuse = new EventEffects { Communes = names, Wellbeing = -0.06, Trust = -0.03, Enthusiasm = -0.02 };
        string grant = "", deny = "";
        switch (f.Kind)
        {
            case FactionKind.Centralists:
                concede.Drift = 6; concede.MilitarizationTicks = 12;
                grant = "A committee takes the militias in hand: stronger fronts, and a new power in the federation.";
                deny = "The assemblies keep the militias. The front communes feel abandoned.";
                break;
            case FactionKind.Party:
                concede.Drift = 4; concede.PartyInfluence = 0.15; concede.GoodsToFront = 40;
                grant = "Commissars join the columns, and the party's arms with them.";
                deny = "No commissars. The party's friends say the movement is splitting the antifascist front.";
                break;
            case FactionKind.Planners:
                concede.Drift = 5; concede.GoodsToFront = 30; concede.Wellbeing = 0.06;
                grant = "Engineers draw up one plan for every works: more output, and experts who decide.";
                deny = "The works stay in the hands of their assemblies, and the engineers grumble.";
                break;
            default:
                concede.Drift = -1; concede.Trust = -0.04; concede.Wellbeing = 0.08;
                refuse = new EventEffects { Secede = names.Take(Math.Max(1, names.Length / 2)).ToArray(), Enthusiasm = -0.04 };
                grant = "They keep their own counsel and take part in what they choose. The federation loosens.";
                deny = "Refused, the most aggrieved of them leave the federation.";
                break;
        }
        if (f.Leader is int lead)
        {
            concede.Figure = w.Figures[lead].Name;
            concede.FigurePrestige = 0.1; concede.FigureAmbition = 0.08; concede.FigureRegard = 0.2; concede.Memory = $"won the {Faction.Name(f.Kind).ToLowerInvariant()}' demand";
            refuse.Figure = w.Figures[lead].Name;
            refuse.FigureRegard = -0.25; refuse.Memory = $"saw the {Faction.Name(f.Kind).ToLowerInvariant()}' demand refused";
        }
        w.Scenario.Events.Add(new EventDefinition
        {
            Id = $"faction_{f.Kind}_{w.Tick}",
            Tick = w.Tick + 1,
            Title = $"{Faction.Name(f.Kind)} make their demand",
            Text = $"{Faction.Name(f.Kind)}{leader} speak for {who}. They demand {Faction.Demand(f.Kind)}, and they are strong enough now that the congress must answer.",
            Figure = f.Leader is int fig ? w.Figures[fig].Name : "",
            DefaultOption = 1,
            ExpiresAfter = 4,
            Options =
            {
                new EventOption { Label = "Concede", Description = grant, Effects = concede },
                new EventOption { Label = "Refuse", Description = deny, Effects = refuse },
            },
        });
    }

    /// <summary>The movement meets a faction's communes, hears them out and answers them: much of its strength fades.</summary>
    public static ActionResult Address(World w, FactionKind kind)
    {
        var b = w.Balance;
        var f = w.Factions.FirstOrDefault(x => x.Kind == kind);
        if (f is null || f.Strength < 0.05) return ActionResult.Fail("That faction isn't pressing anything.");
        if (w.Influence < b.CostAddressFaction) return ActionResult.Fail($"Addressing them costs {b.CostAddressFaction:0} influence.");
        w.Influence -= b.CostAddressFaction;
        f.Strength = Math.Max(0, f.Strength - b.AddressFactionRelief);
        foreach (var id in f.Members) w.Communes[id].Wellbeing = Math.Min(1, w.Communes[id].Wellbeing + 0.02);
        w.Emit(Severity.Info, $"Delegates meet {Faction.Name(f.Kind).ToLowerInvariant()} in their communes and answer them point by point. Many go home satisfied.");
        return ActionResult.Done($"{Faction.Name(f.Kind)} addressed: strength {f.Strength:P0}.");
    }
}
