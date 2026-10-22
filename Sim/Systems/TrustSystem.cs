namespace Politicom.Sim.Systems;

/// <summary>
/// Slow background forces on trust. (Gains from aid, projects and shared struggle happen
/// where those things happen.) Erosion comes from value friction, free-riding and coercion.
/// </summary>
internal static class TrustSystem
{
    public static void Run(World w)
    {
        var b = w.Balance;
        double erosion = DriftEffects.TrustErosion(w.Drift.Stage) + (w.WarCommittee ? b.WarCommitteeTrustLoss : 0);
        foreach (var (i, j) in w.Trust.Edges())
        {
            var a = w.Communes[i];
            var c = w.Communes[j];
            if (a.Occupied || c.Occupied) continue;
            double t = w.Trust[i, j];
            // Political education is also mediation skill: it softens disagreements.
            double mediation = 1 - 0.5 * (a.PoliticalEducation + c.PoliticalEducation) / 2;
            double delta = -b.ValueFrictionRate * a.Values.Distance(c.Values) * 2 * mediation
                           - b.FreeRideTrustLoss * (a.FreeRiding + c.FreeRiding)
                           - erosion
                           + b.TrustBaselinePull * (0.5 - t)
                           + b.CultureTrust * (a.Culture + c.Culture) / 2;
            w.Trust.Add(i, j, delta);
        }
    }
}
