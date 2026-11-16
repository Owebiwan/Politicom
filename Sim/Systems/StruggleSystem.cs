namespace Politicom.Sim.Systems;

/// <summary>
/// The struggle between allies (<see cref="Struggle"/>). Each turn tension drifts with the
/// party's growth, a war going badly, collectivization and the movement serving under the allies'
/// command, and eases slowly back toward an uneasy middle; choices move it in jumps (arms taken or
/// refused, cells exposed, demands conceded). At 90 it breaks into open conflict: resist, or
/// yield. Kept under 20 for half a year, the allies recognize the collectives.
/// </summary>
internal static class StruggleSystem
{
    public static void Run(World w)
    {
        if (w.Struggle is null)
        {
            if (Create(w) is not { } created) return;
            w.Struggle = created;
        }
        var s = w.Struggle;
        if (s.Recognized && s.Tension < 35) return; // settled, unless it flares again
        var b = w.Balance;
        double party = w.Federation.Any() ? w.Federation.Average(c => c.PartyInfluence) : 0;
        var fronts = w.Federation.Where(c => c.Front).ToList();
        double losing = fronts.Count == 0 ? 0 : fronts.Count(c => c.Pressure > c.Defense) / (double)fronts.Count;
        var parts = new List<Part>
        {
            new("The party's cells growing", 5 * party),
            new("Blame for a war going badly", 1.2 * losing),
            new("Collectivization under way", w.ActiveProjects.Any(p => p.Kind == ProjectKind.Collectivization) ? 0.6 : 0),
            new("Serving under their command", w.Ally.MilitarizationTicks > 0 ? -1.2 : 0),
            new("Time cools tempers", (30 - s.Tension) * 0.02),
        };
        s.LastChange = parts.Where(p => Math.Abs(p.Value) > 0.005).Select(p => p with { Value = Math.Round(p.Value * w.Weeks, 2) }).ToList();
        var before = s.Phase;
        s.Tension = Math.Clamp(s.Tension + parts.Sum(p => p.Value) * w.Weeks * b.StrugglePace, 0, 100);
        Announce(w, s, before);

        if (s.Tension >= 90 && (w.Tick - s.LastRupture) * w.Weeks >= 52 && !w.PendingEvents.Any(p => p.Id.StartsWith("struggle_")))
            Rupture(w, s);
        s.CalmTicks = s.Tension < 20 ? s.CalmTicks + 1 : 0;
        if (!s.Recognized && s.CalmTicks * w.Weeks >= 26) Recognize(w, s);
    }

    private static Struggle? Create(World w)
    {
        string ally = w.Ally.Name;
        var rival = Global.RivalSystem.Rivals(w).OrderByDescending(r => r.Weight).FirstOrDefault();
        if (ally.Length == 0 && rival is null) return null;
        string with = ally.Length > 0 ? ally : rival!.Name;
        string name = w.Scenario.Id.StartsWith("catalonia") ? "The antifascist front"
            : w.Scenario.Id.StartsWith("free_territory") ? "The alliance with the Bolsheviks"
            : $"The alliance with the {with}";
        return new Struggle { Name = name, With = with };
    }

    /// <summary>A jump in tension from a choice (arms, cells, demands).</summary>
    public static void Catalyst(World w, double amount, string why)
    {
        if (w.Struggle is not { } s || amount == 0) return;
        var before = s.Phase;
        s.Tension = Math.Clamp(s.Tension + amount, 0, 100);
        s.Catalysts.Add(new StruggleCatalyst(w.Tick, why, amount));
        if (s.Catalysts.Count > 8) s.Catalysts.RemoveAt(0);
        Announce(w, s, before);
    }

    private static void Announce(World w, Struggle s, StrugglePhase before)
    {
        if (!s.UpdatePhase()) return;
        bool worse = s.Phase > before;
        w.Emit(worse ? Severity.Warning : Severity.Good,
            $"{s.Name}: {Struggle.PhaseName(s.Phase).ToLowerInvariant()} — {Struggle.PhaseEffect(s.Phase)}.",
            title: $"{s.Name}: {Struggle.PhaseName(s.Phase)}");
    }

    private static void Rupture(World w, Struggle s)
    {
        s.LastRupture = w.Tick;
        var biggest = w.Federation.OrderByDescending(c => c.Population).First();
        w.Scenario.Events.Add(new EventDefinition
        {
            Id = $"struggle_rupture_{w.Tick}",
            Tick = w.Tick + 1,
            Title = "Barricades",
            Text = $"The struggle with the {s.With} has broken into the open. Their guards try to seize the movement's buildings in {biggest.Name}; "
                 + "the barricades go up. The leadership urges a ceasefire; the militants want to fight it out.",
            DefaultOption = 1,
            ExpiresAfter = 3,
            Options =
            {
                new EventOption
                {
                    Label = "Resist",
                    Description = "The movement holds its ground. Blood is spilled between allies, and the front is stripped to fight at home.",
                    Effects = new EventEffects
                    {
                        Communes = new[] { biggest.Name }, Population = -0.01, Drift = -3, Enthusiasm = -0.04, PartyInfluence = -0.15,
                        EnemyStrength = 0.3, EndAllyOffers = true, Tension = -30,
                    },
                },
                new EventOption
                {
                    Label = "Accept the ceasefire",
                    Description = "The barricades come down on their terms: their police stay, their officers take command.",
                    Effects = new EventEffects { Drift = 8, PartyInfluence = 0.1, Wellbeing = -0.05, MilitarizationTicks = 12, Tension = -45 },
                },
            },
        });
    }

    private static void Recognize(World w, Struggle s)
    {
        s.Recognized = true;
        w.Scenario.FoodImports *= 1.2;
        w.Enthusiasm = Math.Min(1, w.Enthusiasm + 0.05);
        w.PendingDriftBump -= 2;
        w.Emit(Severity.Good, $"The {s.With} recognizes the collectives as they are. Supplies flow more freely, and the assemblies keep what they built.",
            pause: true, title: "Recognition");
    }

    /// <summary>The struggle's weight on defence: a united front helps, allies holding back hurt.</summary>
    public static double DefenseFactor(World w) => w.Struggle?.Phase switch
    {
        StrugglePhase.Cooperation => 1.05,
        StrugglePhase.Hostility => 0.95,
        _ => 1,
    };

    public static bool Hostile(World w) => w.Struggle?.Phase == StrugglePhase.Hostility;
}
