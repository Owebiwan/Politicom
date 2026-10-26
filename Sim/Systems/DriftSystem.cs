namespace Politicom.Sim.Systems;

internal static class DriftSystem
{
    public static void Run(World w)
    {
        var b = w.Balance;
        var d = new DriftBreakdown
        {
            Baseline = b.BaselineDrift,
            Technology = w.SocietyEffect(e => e.DriftPerTick),
            ExpertTrap = b.ExpertTrapRate * w.Mean(c => c.ExpertGap),
            WarCommittee = w.WarCommittee ? b.WarCommitteeDrift : 0,
            WarStress = b.WarStressDrift * LosingShare(w),
            ForeignTrade = w.ForeignTrade ? b.TradeDrift : 0,
            PartyCells = b.PartyDrift * w.Mean(c => c.PartyInfluence),
            Militarization = w.Ally.MilitarizationTicks > 0 ? b.MilitarizationDrift : 0,
            Conscription = w.Conscription ? b.ConscriptionDrift * w.Weeks : 0,
            Treasury = b.GoldHoldingDrift * w.Gold * w.Weeks,
            Figures = FigureSystem.Drift(w),
            LargeProjects = w.ActiveProjects.Sum(p => b.LargeProjectDrift * Math.Max(0, p.Participants.Count - b.LargeProjectThreshold)),
            Events = w.PendingDriftBump,
            PoliticalEducationBrake = b.PoliticalBrake * w.Mean(c => c.PoliticalEducation),
            TechnologyBrake = w.SocietyEffect(e => e.DriftBrake),
            CultureBrake = b.CultureDriftBrake * CultureSystem.Mean(w) * w.Weeks,
        };
        w.PendingDriftBump = 0;
        w.LastDrift = d;

        var before = w.Drift.Stage;
        var changed = w.Drift.Apply(d.Net, b.StageHysteresis);
        // A warning a few points before each threshold, while there is still time to recall delegates.
        // Once per threshold: it re-arms only after drift falls well back, so hovering doesn't nag.
        if (w.Drift.Value < w.DriftWarnedAt - 9) w.DriftWarnedAt = 0;
        foreach (var t in DriftTracker.Thresholds)
            if (w.DriftWarnedAt < t && w.Drift.Value >= t - 3 && w.Drift.Value < t && w.Drift.Stage < DriftStage.State)
            {
                w.DriftWarnedAt = t;
                w.Emit(Severity.Warning, $"Hierarchy drift is nearing {t:0} ({w.Drift.Value:0.0}). " +
                    (t >= 100 ? "At 100 a state crystallizes and the revolution is over." : $"{DriftEffects.Name((DriftStage)(Array.IndexOf(DriftTracker.Thresholds, t) + 1))} will form unless it is pushed back."), pause: true, title: "Hierarchy is gathering");
            }
        if (changed is { } stage && stage != DriftStage.State)
        {
            bool rising = stage > before;
            w.Emit(rising ? Severity.Danger : Severity.Good,
                (rising ? $"Hierarchy hardens: {DriftEffects.Name(stage)}. " : $"Self-management restored: back to {DriftEffects.Name(stage)}. ")
                + DriftEffects.Describe(stage), pause: rising, title: rising ? $"Hierarchy hardens: {DriftEffects.Name(stage)}" : "");
        }
    }

    /// <summary>Share of front communes currently losing ground (pressure above defense).</summary>
    private static double LosingShare(World w)
    {
        var fronts = w.Federation.Where(c => c.Front).ToList();
        return fronts.Count == 0 ? 0 : fronts.Count(c => c.Pressure > c.Defense) / (double)fronts.Count;
    }
}

internal static class InfluenceSystem
{
    public static void Run(World w)
    {
        var b = w.Balance;
        double regen = b.InfluenceBase
                       + b.InfluencePolitical * w.Mean(c => c.PoliticalEducation)
                       + b.InfluenceWellbeing * w.Mean(c => c.Wellbeing)
                       + w.SocietyEffect(e => e.Influence);
        w.Influence = Math.Min(b.InfluenceCap, w.Influence + regen);
        if (w.RecallPenaltyTicks > 0) w.RecallPenaltyTicks--;
    }

    /// <summary>Influence regenerated per tick at the current state (for UI).</summary>
    public static double Regen(World w) =>
        w.Balance.InfluenceBase + w.Balance.InfluencePolitical * w.Mean(c => c.PoliticalEducation)
        + w.Balance.InfluenceWellbeing * w.Mean(c => c.Wellbeing) + w.SocietyEffect(e => e.Influence);
}

internal static class ScoreSystem
{
    public static void Run(World w)
    {
        var s = w.Scores;
        var active = w.Active.ToList();
        var b = w.Balance;
        s.Samples++;
        s.SurvivalSum += Math.Min(1, w.Federation.Count() / (double)w.InitialCommuneCount);
        s.Survival = 100 * s.SurvivalSum / Math.Max(1, w.Scenario.EndTick);

        double foodSecurity = active.Count == 0 ? 0 : active.Average(c => c.Starving ? 0 : Math.Clamp(c.Food / (c.Population * b.FoodNeed * 2), 0, 1));
        double quality = active.Count == 0 ? 0 : 100 * (0.4 * active.Average(c => c.Wellbeing) + 0.2 * foodSecurity
                                                       + 0.2 * active.Average(c => c.PracticalEducation) + 0.2 * active.Average(c => c.PoliticalEducation));
        s.QualitySum += quality;
        s.QualityOfLife = s.QualitySum / s.Samples;

        // Spread: the enemy's workers, the world's workers (weighted by power), and revolutions won.
        if (w.States.Count == 0) s.Spread = 100 * w.Radicalization;
        else
        {
            double world = w.States.Sum(x => x.Power * (x.Sister ? 1 : x.Radicalization)) / w.States.Sum(x => x.Power);
            s.Spread = Math.Min(100, 100 * (0.4 * w.Radicalization + 0.6 * world) + 10 * w.States.Count(x => x.Sister));
        }

        s.FreedomSum += 100 - w.Drift.Value;
        s.Freedom = s.FreedomSum / s.Samples;
    }
}
