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
            Militarization = w.Ally.MilitarizationTicks > 0 ? b.MilitarizationDrift : 0,
            LargeProjects = w.ActiveProjects.Sum(p => b.LargeProjectDrift * Math.Max(0, p.Participants.Count - b.LargeProjectThreshold)),
            Events = w.PendingDriftBump,
            PoliticalEducationBrake = b.PoliticalBrake * w.Mean(c => c.PoliticalEducation),
            TechnologyBrake = w.SocietyEffect(e => e.DriftBrake),
        };
        w.PendingDriftBump = 0;
        w.LastDrift = d;

        var before = w.Drift.Stage;
        var changed = w.Drift.Apply(d.Net, b.StageHysteresis);
        if (changed is { } stage && stage != DriftStage.State)
        {
            bool rising = stage > before;
            w.Emit(rising ? Severity.Danger : Severity.Good,
                (rising ? $"Hierarchy hardens: {DriftEffects.Name(stage)}. " : $"Self-management restored: back to {DriftEffects.Name(stage)}. ")
                + DriftEffects.Describe(stage));
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
        s.SurvivalSum += w.Federation.Count() / (double)w.InitialCommuneCount;
        s.Survival = 100 * s.SurvivalSum / Math.Max(1, w.Scenario.EndTick);

        double foodSecurity = active.Count == 0 ? 0 : active.Average(c => c.Starving ? 0 : Math.Clamp(c.Food / (c.Population * b.FoodNeed * 2), 0, 1));
        double quality = active.Count == 0 ? 0 : 100 * (0.4 * active.Average(c => c.Wellbeing) + 0.2 * foodSecurity
                                                       + 0.2 * active.Average(c => c.PracticalEducation) + 0.2 * active.Average(c => c.PoliticalEducation));
        s.QualitySum += quality;
        s.QualityOfLife = s.QualitySum / s.Samples;

        s.Spread = 100 * w.Radicalization;

        s.FreedomSum += 100 - w.Drift.Value;
        s.Freedom = s.FreedomSum / s.Samples;
    }
}
