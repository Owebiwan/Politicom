namespace Politicom.Sim.Systems;

/// <summary>
/// Outside pressure: each hostile state's front, its offensives, contagion across the borders, and
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
        foreach (var c in w.Communes) c.Pressure = 0;
        var pressureBy = new Dictionary<(int Commune, int Enemy), double>();

        for (int k = 0; k < w.Enemies.Count; k++)
        {
            var e = w.Enemies[k];
            if (e.GrowthUntil is not int until || w.Tick < until)
                e.Strength += e.Growth * (1 + 0.5 * e.Radicalization); // neighbours see a threat
            e.Exhaustion = Math.Max(0, e.Exhaustion - b.ExhaustionDecay);

            var offensive = e.Offensives.FirstOrDefault(o => w.Tick >= o.Tick && w.Tick < o.Tick + o.Duration);
            if (offensive != null && offensive.Tick != e.CurrentOffensive?.Tick)
                w.Emit(Severity.Danger, $"{offensive.Name} begins.");
            e.CurrentOffensive = offensive;

            e.EffectiveStrength = (e.Strength + (offensive?.Strength ?? 0) + e.Intervention)
                                  * (1 - 0.4 * e.Radicalization)
                                  * (1 - Math.Clamp(e.Exhaustion, 0, 0.6));

            int enemy = k;
            var fronts = w.Active.Where(c => c.Fronts.Contains(enemy)).ToList();
            if (fronts.Count == 0) continue;
            double perFront = e.EffectiveStrength * b.PressureScale / fronts.Count;
            foreach (var c in fronts)
            {
                c.Pressure += perFront;
                pressureBy[(c.Id, k)] = perFront;
            }
        }

        var allFronts = w.Active.Where(c => c.Front).ToList();
        if (allFronts.Count == 0) return;
        double campaign = w.ActiveProjects.Where(p => p.Kind == ProjectKind.DefensiveCampaign).Sum(p => p.LastOutput) * 0.5 / allFronts.Count;
        var fallen = new List<Commune>();
        foreach (var c in allFronts)
        {
            c.Defense = Defense(w, c) + campaign;
            if (c.Pressure > c.Defense)
                c.Control -= b.ControlLossRate * w.Scenario.WarPace * (c.Pressure - c.Defense) / c.Pressure;
            else
                c.Control = Math.Min(1, c.Control + b.ControlRecovery * w.Scenario.WarPace);
            if (c.Control <= 0) fallen.Add(c);
        }

        // Shared struggle: neighbouring communes under attack together come to trust each other.
        foreach (var c in allFronts)
            foreach (var n in w.Trust.Neighbors(c.Id))
                if (n > c.Id && !w.Communes[n].Occupied)
                    w.Trust.Add(c.Id, n, b.SharedStruggleTrustGain * (w.Communes[n].Front ? 1 : 0.5));

        foreach (var c in fallen)
        {
            // The enemy pressing hardest takes it.
            int occupier = c.Fronts.OrderByDescending(k => pressureBy.GetValueOrDefault((c.Id, k))).ThenBy(k => k).First();
            Occupy(w, c, occupier);
        }

        foreach (var c in w.Communes)
            if (c.OccupiedBy is int k && c.Techs.Contains("militia_doctrine"))
                w.Enemies[k].Exhaustion += b.GuerrillaExhaustion;
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
            support += w.Trust[c.Id, n] * Militia(b, d) * b.NeighborSupport * (d.Federated ? 1 : 0.3) * (w.Rail(c.Id, n) ? b.RailSupport : 1);
        }
        double mult = (1 + w.LocalEffect(c, e => e.Defense))
                      * (1 + DriftEffects.Defense(w.Drift.Stage))
                      * (w.WarCommittee ? 1 + b.WarCommitteeDefense : 1)
                      * (w.Ally.MilitarizationTicks > 0 ? 1 + b.MilitarizationDefense : 1)
                      * (b.ArmsShortageDefense + (1 - b.ArmsShortageDefense) * c.GoodsCoverage)
                      * (1 + b.StockpileDefense * Math.Clamp(c.Goods / (c.Population * b.StockpilePerPop), 0, 1))
                      * (w.RecallPenaltyTicks > 0 ? 0.9 : 1)
                      * (1 + w.VolunteerDefense);
        return (own + support) * mult;
    }

    private static double Militia(Balance b, Commune c) =>
        c.Population * b.MilitiaPerPop * (0.6 + 0.4 * CommuneValues.To01(c.Values.Militancy));

    private static void Occupy(World w, Commune c, int enemy)
    {
        var e = w.Enemies[enemy];
        c.Occupied = true;
        c.OccupiedBy = enemy;
        c.Fronts.Clear();
        c.Control = 0;
        w.PendingSecessions.RemoveAll(s => s.Commune == c.Id);
        foreach (var p in w.ActiveProjects) p.Participants.Remove(c.Id);
        e.Exhaustion += w.Balance.OccupationExhaustion;
        foreach (var n in w.Trust.Neighbors(c.Id))
            if (!w.Communes[n].Occupied) w.Communes[n].Fronts.Add(enemy);
        w.Emit(Severity.Danger, $"{c.Name} falls to the {e.Name}. With no capital to take, every commune has to be taken one by one.", c.Id);
    }

    /// <summary>Contagion: political education and the press radicalize workers across each border.</summary>
    private static void Contagion(World w)
    {
        var b = w.Balance;
        double press = 1 + w.SocietyEffect(t => t.Contagion);
        double meanPolitical = w.Mean(c => c.PoliticalEducation);
        for (int k = 0; k < w.Enemies.Count; k++)
        {
            var e = w.Enemies[k];
            int enemy = k;
            var border = w.Federation.Where(c => c.Fronts.Contains(enemy)).ToList();
            double voice = 0.5 * meanPolitical + 0.5 * (border.Count == 0 ? 0 : border.Average(c => c.PoliticalEducation));
            double before = e.Radicalization;
            e.Radicalization = Math.Clamp(e.Radicalization + b.ContagionRate * voice * press - b.RepressionRate * e.Radicalization, 0, 1);
            foreach (var mark in new[] { 0.25, 0.5 })
                if (before < mark && e.Radicalization >= mark)
                    w.Emit(Severity.Good, $"Workers behind {e.Name} lines are organizing. It must divert troops to repression.");
        }
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
                $"(+{w.Balance.MilitarizationDefense:P0} defense for {w.Turns(w.Balance.MilitarizationTicks)}, +{w.Balance.ArmsAcceptDrift:0} drift now and more while it lasts)");
        }
    }
}
