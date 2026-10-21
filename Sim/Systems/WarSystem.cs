namespace Politicom.Sim.Systems;

/// <summary>
/// Outside pressure: the hostile state's front, its offensives, contagion across the border, and
/// the ally's offers of arms in exchange for centralization. Militias defend home well; there is
/// no capital to take, and every occupied commune wears the occupier down.
/// </summary>
internal static class WarSystem
{
    public static void Run(World w)
    {
        Ally(w);
        Front(w);
        Contagion(w);
    }

    private static void Front(World w)
    {
        var b = w.Balance;
        var e = w.Enemy;
        e.Strength += e.Growth * (1 + 0.5 * e.Radicalization); // neighbours see a threat
        e.Exhaustion = Math.Max(0, e.Exhaustion - b.ExhaustionDecay);

        var offensive = e.Offensives.FirstOrDefault(o => w.Tick >= o.Tick && w.Tick < o.Tick + o.Duration);
        if (offensive != null && offensive.Tick != e.CurrentOffensive?.Tick)
            w.Emit(Severity.Danger, $"The {offensive.Name} begins.");
        e.CurrentOffensive = offensive;

        e.EffectiveStrength = (e.Strength + (offensive?.Strength ?? 0))
                              * (1 - 0.4 * e.Radicalization)
                              * (1 - Math.Clamp(e.Exhaustion, 0, 0.6));

        var fronts = w.Active.Where(c => c.Front).ToList();
        foreach (var c in w.Communes) c.Pressure = 0;
        if (fronts.Count == 0) return;

        double perFront = e.EffectiveStrength * b.PressureScale / fronts.Count;
        double campaign = w.ActiveProjects.Where(p => p.Kind == ProjectKind.DefensiveCampaign).Sum(p => p.LastOutput) * 0.5 / fronts.Count;
        var fallen = new List<Commune>();
        foreach (var c in fronts)
        {
            c.Pressure = perFront;
            c.Defense = Defense(w, c) + campaign;
            if (c.Pressure > c.Defense)
                c.Control -= b.ControlLossRate * (c.Pressure - c.Defense) / c.Pressure;
            else
                c.Control = Math.Min(1, c.Control + b.ControlRecovery);
            if (c.Control <= 0) fallen.Add(c);
        }

        // Shared struggle: neighbouring communes under attack together come to trust each other.
        foreach (var c in fronts)
            foreach (var n in w.Trust.Neighbors(c.Id))
                if (n > c.Id && !w.Communes[n].Occupied)
                    w.Trust.Add(c.Id, n, b.SharedStruggleTrustGain * (w.Communes[n].Front ? 1 : 0.5));

        foreach (var c in fallen) Occupy(w, c);

        int guerrillas = w.Communes.Count(c => c.Occupied && c.Techs.Contains("militia_doctrine"));
        e.Exhaustion += guerrillas * b.GuerrillaExhaustion;
    }

    internal static double Defense(World w, Commune c)
    {
        var b = w.Balance;
        double own = Militia(b, c);
        double support = 0;
        foreach (var n in w.Trust.Neighbors(c.Id))
        {
            var d = w.Communes[n];
            if (d.Occupied || d.Front) continue;
            support += w.Trust[c.Id, n] * Militia(b, d) * b.NeighborSupport * (d.Federated ? 1 : 0.3);
        }
        double mult = (1 + w.LocalEffect(c, e => e.Defense))
                      * (1 + DriftEffects.Defense(w.Drift.Stage))
                      * (w.WarCommittee ? 1 + b.WarCommitteeDefense : 1)
                      * (w.Ally.MilitarizationTicks > 0 ? 1 + b.MilitarizationDefense : 1)
                      * (b.ArmsShortageDefense + (1 - b.ArmsShortageDefense) * c.GoodsCoverage)
                      * (w.RecallPenaltyTicks > 0 ? 0.9 : 1);
        return (own + support) * mult;
    }

    private static double Militia(Balance b, Commune c) =>
        c.Population * b.MilitiaPerPop * (0.6 + 0.4 * CommuneValues.To01(c.Values.Militancy));

    private static void Occupy(World w, Commune c)
    {
        c.Occupied = true;
        c.Front = false;
        c.Control = 0;
        w.PendingSecessions.RemoveAll(s => s.Commune == c.Id);
        foreach (var p in w.ActiveProjects) p.Participants.Remove(c.Id);
        w.Enemy.Exhaustion += w.Balance.OccupationExhaustion;
        foreach (var n in w.Trust.Neighbors(c.Id))
            if (!w.Communes[n].Occupied) w.Communes[n].Front = true;
        w.Emit(Severity.Danger, $"{c.Name} falls to the {w.Enemy.Name}. With no capital to take, the occupation must be fought village by village.", c.Id);
    }

    /// <summary>Contagion: political education and the press radicalize workers across the border.</summary>
    private static void Contagion(World w)
    {
        var b = w.Balance;
        var e = w.Enemy;
        var border = w.Federation.Where(c => c.Front).ToList();
        double voice = 0.5 * w.Mean(c => c.PoliticalEducation) + 0.5 * (border.Count == 0 ? 0 : border.Average(c => c.PoliticalEducation));
        double before = e.Radicalization;
        e.Radicalization = Math.Clamp(e.Radicalization
            + b.ContagionRate * voice * (1 + w.SocietyEffect(t => t.Contagion))
            - b.RepressionRate * e.Radicalization, 0, 1);
        foreach (var mark in new[] { 0.25, 0.5 })
            if (before < mark && e.Radicalization >= mark)
                w.Emit(Severity.Good, $"Workers behind {e.Name} lines are organizing. It must divert troops to repression.");
    }

    private static void Ally(World w)
    {
        var a = w.Ally;
        if (a.MilitarizationTicks > 0) a.MilitarizationTicks--;
        if (a.OfferPending && w.Tick > a.OfferExpires)
        {
            a.OfferPending = false;
            a.Declined++;
            a.NextOfferTick = w.Tick + a.OfferInterval;
            w.Emit(Severity.Info, $"The {a.Name}'s offer lapses unanswered.");
        }
        if (!a.OfferPending && a.OfferInterval > 0 && w.Tick >= a.NextOfferTick)
        {
            a.OfferPending = true;
            a.OfferExpires = w.Tick + 4;
            w.Emit(Severity.Decision,
                $"The {a.Name} offers arms for the front — if the militias accept militarization under its command. " +
                $"(+{w.Balance.MilitarizationDefense:P0} defense for {w.Balance.MilitarizationTicks} weeks, +{w.Balance.ArmsAcceptDrift:0} drift now and more while it lasts)");
        }
    }
}
