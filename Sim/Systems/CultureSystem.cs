namespace Politicom.Sim.Systems;

/// <summary>
/// Culture: how deeply the movement's ideas have taken root in a commune, through its press,
/// schools, songs and theatre. It grows from political education and cultural institutions,
/// spreads to trusted neighbours and fades if nothing feeds it. A cultured federation resists
/// hierarchy (a drift brake), trusts more easily, keeps rival parties' cells out, and its ideas
/// carry further abroad.
/// </summary>
internal static class CultureSystem
{
    public static void Run(World w)
    {
        var b = w.Balance;
        var before = w.Communes.Select(c => c.Culture).ToArray();
        foreach (var c in w.Active)
        {
            double output = b.CultureRate * (c.PoliticalEducation + w.LocalEffect(c, e => e.Culture));
            double spread = 0;
            foreach (var n in w.Trust.Neighbors(c.Id))
                if (!w.Communes[n].Occupied) spread += b.CultureSpread * w.Trust[c.Id, n] * Math.Max(0, before[n] - before[c.Id]);
            c.Culture = Math.Clamp(c.Culture + ((output + spread) * (1 - c.Culture) - b.CultureDecay * c.Culture) * w.Weeks, 0, 1);
        }
    }

    /// <summary>Mean culture across the federation.</summary>
    public static double Mean(World w) => w.Mean(c => c.Culture);
}
