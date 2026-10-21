namespace Politicom.Sim.Systems;

/// <summary>
/// Two tracks. Practical raises productivity and innovation; political lowers coordination cost,
/// slows drift and builds trust. Schooling costs labor (see EconomySystem).
/// </summary>
internal static class EducationSystem
{
    public static void Run(World w)
    {
        var b = w.Balance;
        double stage = DriftEffects.PoliticalEducation(w.Drift.Stage);
        foreach (var c in w.Active)
        {
            double politicalRate = stage * (1 + w.LocalEffect(c, e => e.PoliticalEducation));
            c.PracticalEducation = Math.Clamp(c.PracticalEducation
                + c.PracticalSchooling * b.SchoolingRate * (1 - c.PracticalEducation)
                - b.EducationDecay * c.PracticalEducation, 0, 1);
            c.PoliticalEducation = Math.Clamp(c.PoliticalEducation
                + c.PoliticalSchooling * b.SchoolingRate * politicalRate * (1 - c.PoliticalEducation)
                - b.EducationDecay * c.PoliticalEducation, 0, 1);

            // Communes run their own schools. Funded schooling relaxes back to what they'd choose themselves.
            c.PracticalSchooling += (BaselinePractical(b, c) - c.PracticalSchooling) * b.SchoolingRelax;
            c.PoliticalSchooling += (BaselinePolitical(b, c) - c.PoliticalSchooling) * b.SchoolingRelax;
        }
    }

    private static double BaselinePractical(Balance b, Commune c) =>
        b.BaselineSchooling * (c.Kind == CommuneKind.Agrarian ? 0.8 : 1.3);

    private static double BaselinePolitical(Balance b, Commune c) =>
        b.BaselineSchooling * (0.4 + 0.4 * CommuneValues.To01(c.Values.Collectivism) + 0.4 * CommuneValues.To01(c.Values.Federalism));
}
