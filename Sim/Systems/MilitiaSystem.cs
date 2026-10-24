namespace Politicom.Sim.Systems;

/// <summary>
/// Who fights, and in what spirit. Each commune's militia drifts toward what it volunteers —
/// more where people are militant, at the front, and when enthusiasm runs high — or, under
/// conscription, at least the levy. Losing front communes lose fighters. Enthusiasm wears down
/// over a long war, with hunger, lost communes and hierarchy, and is renewed by culture (and by
/// towns rising, festivals and revolutions won abroad, elsewhere).
/// </summary>
internal static class MilitiaSystem
{
    public static void Run(World w)
    {
        var b = w.Balance;
        foreach (var c in w.Federation)
        {
            double volunteers = w.BaselineMobilization(c) * (1 - b.VolunteerMilitancy / 2 + b.VolunteerMilitancy * CommuneValues.To01(c.Values.Militancy)) * (0.5 + 0.625 * w.Enthusiasm);
            double target = Math.Min(b.MaxMobilization, w.Conscription ? Math.Max(volunteers, b.ConscriptionFloor) : volunteers);
            c.Mobilization += (target - c.Mobilization) * Math.Min(1, b.MobilizationPull * w.Weeks);
            if (c.Front && c.Pressure > c.Defense && c.Defense > 0)
            {
                double lost = c.Population * c.Mobilization * b.CombatLoss * Math.Min(1, c.Pressure / c.Defense - 1) * w.Weeks;
                c.Population = Math.Max(1, c.Population - lost);
            }
        }

        var members = w.Federation.ToList();
        if (members.Count == 0) return;
        double hungry = members.Count(c => c.Starving) / (double)members.Count;
        double change = -b.EnthusiasmDecay
                        + b.EnthusiasmCulture * CultureSystem.Mean(w)
                        - b.EnthusiasmHunger * hungry
                        - b.EnthusiasmHierarchy * (int)w.Drift.Stage
                        - (w.Conscription ? b.ConscriptionEnthusiasm : 0);
        w.Enthusiasm = Math.Clamp(w.Enthusiasm + change * w.Weeks, 0, 1);
    }
}
