namespace Politicom.Sim.Systems;

/// <summary>
/// Peace, in open-world starts (the historical scenarios keep their history): an enemy that has
/// gone a long time without taking a commune, or is worn down, may offer terms. Accepting closes
/// its front (it keeps what it took) at the price of some drift, since delegates sit at a table
/// with a government. Peace with every enemy ends the game: a recognised autonomy.
/// </summary>
internal static class PeaceSystem
{
    public static bool Applies(World w) => w.Scenario.Id.StartsWith("open:") || w.Scenario.Id.StartsWith("sandbox:");

    /// <summary>How ready an enemy is to talk (offers come at 1): a long stalemate, a long war, exhaustion.</summary>
    public static double Readiness(World w, HostileState e)
    {
        var b = w.Balance;
        double stalemate = (w.Tick - Math.Max(e.LastGain, 0)) * w.Weeks / b.PeaceStalemateWeeks;
        double weariness = w.Tick * w.Weeks / 78.0; // governments tire of a long war: a year and a half counts for this much
        return Math.Min(1.5, stalemate) * 0.6 + Math.Min(1.5, weariness) * 0.35 + e.Exhaustion / b.PeaceExhaustion * 0.6;
    }

    public static void Run(World w)
    {
        if (!Applies(w) || w.Tick < 8) return;
        var b = w.Balance;
        for (int k = 0; k < w.Enemies.Count; k++)
        {
            var e = w.Enemies[k];
            if (e.AtPeace || e.CurrentOffensive != null || w.Tick - e.LastPeaceOffer < b.PeaceRefusalGap) continue;
            if (!w.Communes.Any(c => c.Fronts.Contains(k))) continue; // nothing left to fight over
            if (w.PendingEvents.Any(p => p.Id.StartsWith($"peace_{k}_"))) continue;
            if (Readiness(w, e) < 1 || !w.Rng.Chance(b.PeaceOfferRate * w.Weeks)) continue;
            e.LastPeaceOffer = w.Tick;
            int held = w.Communes.Count(c => c.OccupiedBy == k);
            w.Scenario.Events.Add(new EventDefinition
            {
                Id = $"peace_{k}_{w.Tick}",
                Tick = w.Tick + 1,
                Title = $"The {e.Name} offers terms",
                Text = $"The {e.Name} cannot break the federation, and its own people are tired of the war. Its envoys propose an armistice: " +
                       $"the front stays where it is{(held > 0 ? $", and the {held} communes it holds stay in its hands" : "")}, and it leaves the free communes alone.",
                DefaultOption = 1,
                ExpiresAfter = 4,
                Options =
                {
                    new EventOption
                    {
                        Label = "Make peace",
                        Description = $"The front closes. Delegates at a table with a government: drift +{b.PeaceDrift:0}. Peace with every enemy ends the game.",
                        Effects = new EventEffects { MakePeace = e.Name, Drift = b.PeaceDrift, Wellbeing = 0.03 },
                    },
                    new EventOption
                    {
                        Label = "Fight on",
                        Description = "No terms with a government. The militias take heart; the war goes on.",
                        Effects = new EventEffects { Enthusiasm = 0.03 },
                    },
                },
            });
        }
    }

    public static void MakePeace(World w, HostileState e)
    {
        if (e.AtPeace) return;
        e.AtPeace = true;
        int k = w.Enemies.ToList().IndexOf(e);
        foreach (var c in w.Communes) c.Fronts.Remove(k);
        w.Emit(Severity.Good, $"Peace with the {e.Name}. Its front closes; the militias come home.", title: "Peace");
    }
}
