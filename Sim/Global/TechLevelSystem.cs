namespace Politicom.Sim.Global;

/// <summary>
/// The world doesn't stand still. States advance in technology, faster when large and at peace;
/// the enemy modernizes its army. A federation that keeps up with research holds its own; one
/// that falls behind faces a stronger enemy each year (see <see cref="Systems.WarSystem"/>).
/// </summary>
public static class TechLevelSystem
{
    /// <summary>A state's starting level: large, liberal and fascist industrial states lead.</summary>
    public static double Initial(StateDefinition d) =>
        Math.Clamp(0.2 + 0.05 * d.Power + d.Ideology switch { Ideology.Liberal => 0.1, Ideology.Fascist => 0.05, _ => 0 }, 0, 0.9);

    internal static void Run(World w)
    {
        var b = w.Balance;
        foreach (var s in w.States)
        {
            bool atWar = w.Wars.Any(x => !x.Over && x.Involves(s.Id));
            s.TechLevel = Math.Min(1, s.TechLevel + w.Weeks * b.StateTechGrowth * (0.5 + s.Power / 10) * (atWar ? 0.6 : 1) * (1 - 0.5 * s.TechLevel));
        }
        foreach (var e in w.Enemies)
        {
            // Sponsors share their weapons and advisers.
            double sponsors = w.States.Where(s => e.Sponsors.Contains(s.Id) && s.Does(StateActions.Intervention)).Select(s => s.TechLevel).DefaultIfEmpty(0).Max();
            double growth = w.Weeks * b.EnemyTechGrowth * (1 + (sponsors > e.TechLevel ? 1 : 0));
            e.TechLevel = Math.Min(1, e.TechLevel + growth);
        }
    }

    /// <summary>
    /// The enemy's strength multiplier from technology: how far it has advanced since the start,
    /// against how far the federation has. Keeping up cancels it, but
    /// research can't make the enemy weaker than it started: it is a defence, not a win button.
    /// </summary>
    public static double EnemyEdge(World w, HostileState e)
    {
        var b = w.Balance;
        double theirs = e.TechLevel - e.TechStart;
        double ours = b.FederationTechScale * (w.TechLevel - w.TechLevelStart);
        return 1 + Math.Min(b.TechEdgeCap, b.TechEdge * Math.Max(0, theirs - ours));
    }
}
