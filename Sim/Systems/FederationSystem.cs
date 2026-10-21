namespace Politicom.Sim.Systems;

/// <summary>
/// Free federation. Communes can leave, and leaving is legitimate. The player gets a warning and
/// can mediate, or hold the commune by force — which hurts far more than losing it.
/// </summary>
internal static class FederationSystem
{
    public static void Run(World w)
    {
        var b = w.Balance;
        foreach (var c in w.Active) c.Discontent = Discontent(w, c);

        foreach (var s in w.PendingSecessions.ToList())
        {
            var c = w.Communes[s.Commune];
            if (c.Discontent < b.SecessionThreshold - 0.05)
            {
                w.PendingSecessions.Remove(s);
                w.Emit(Severity.Good, $"{c.Name}'s assembly reconsiders and stays in the federation.", c.Id);
            }
            else if (w.Tick >= s.Deadline)
            {
                w.PendingSecessions.Remove(s);
                Secede(w, c);
            }
        }

        foreach (var c in w.Active)
        {
            if (c.Federated)
            {
                if (w.PendingSecessions.Any(s => s.Commune == c.Id) || c.Discontent <= b.SecessionThreshold) continue;
                if (!w.Rng.Chance(b.SecessionChance * (c.Discontent - b.SecessionThreshold) * 10)) continue;
                w.PendingSecessions.Add(new PendingSecession(c.Id, w.Tick + b.SecessionWarningTicks));
                w.Emit(Severity.Decision,
                    $"{c.Name}'s assembly votes to leave the federation in {b.SecessionWarningTicks} weeks. " +
                    $"(trust {w.Trust.AverageTrust(c.Id):P0}, wellbeing {c.Wellbeing:P0}) Mediate to change its mind, or hold it by force.", c.Id);
            }
            else if (c.Discontent < b.RejoinThreshold && w.Rng.Chance(b.RejoinChance))
            {
                c.Federated = true;
                w.Emit(Severity.Good, $"{c.Name} rejoins the federation of its own accord.", c.Id);
            }
        }
    }

    internal static double Discontent(World w, Commune c)
    {
        double trust = w.Trust.AverageTrust(c.Id, n => w.Communes[n].InFederation);
        return 0.45 * (1 - trust)
               + 0.25 * (1 - c.Wellbeing)
               + 0.15 * (1 - CommuneValues.To01(c.Values.Federalism))
               + DriftEffects.SecessionPressure(w.Drift.Stage)
               + (c.Starving ? 0.1 : 0);
    }

    private static void Secede(World w, Commune c)
    {
        c.Federated = false;
        foreach (var n in w.Trust.Neighbors(c.Id))
            if (w.Communes[n].InFederation) w.Trust.Add(c.Id, n, -0.03);
        foreach (var p in w.ActiveProjects) p.Participants.Remove(c.Id);
        w.Emit(Severity.Warning, $"{c.Name} leaves the federation. It remains free — just not with you.", c.Id);
    }
}
