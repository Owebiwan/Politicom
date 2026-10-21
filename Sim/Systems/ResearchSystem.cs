namespace Politicom.Sim.Systems;

/// <summary>
/// Decentralized research: ideas emerge in educated communes, shaped by their needs and values,
/// and spread along trust links. The player can only nudge (circulate ideas, sponsor congresses).
/// </summary>
internal static class ResearchSystem
{
    public static void Run(World w)
    {
        foreach (var key in w.IdeaNudges.Keys.ToList())
            if (--w.IdeaNudges[key] <= 0) w.IdeaNudges.Remove(key);

        Discover(w);
        Diffuse(w);
        AnnounceWidespread(w);
    }

    private static void Discover(World w)
    {
        var b = w.Balance;
        foreach (var c in w.Active)
        {
            if (!w.Rng.Chance(b.DiscoveryRate * (0.25 + c.PracticalEducation))) continue;
            var candidates = w.Techs.All.Where(t => !c.Techs.Contains(t.Id) && CanDiscover(c, t)).ToList();
            if (candidates.Count == 0) continue;
            var weights = candidates.Select(t => Weight(w, c, t)).ToList();
            var tech = w.Rng.PickWeighted(candidates, weights);
            bool first = !w.Communes.Any(o => o.Techs.Contains(tech.Id));
            c.Techs.Add(tech.Id);
            if (first)
                w.Emit(Severity.Info, $"{c.Name} develops {tech.Name}. {tech.Perk} — but {Lower(tech.Risk)}", c.Id);
        }
    }

    private static void Diffuse(World w)
    {
        var b = w.Balance;
        double boost = 1 + w.SocietyEffect(e => e.Diffusion) + w.TransportBonus;
        var adoptions = new List<(Commune C, string Tech)>();
        foreach (var (i, j) in w.Trust.Edges())
        {
            var a = w.Communes[i];
            var c = w.Communes[j];
            if (a.Occupied || c.Occupied) continue;
            double trust = w.Trust[i, j];
            foreach (var (src, dst) in new[] { (a, c), (c, a) })
            {
                foreach (var id in src.Techs)
                {
                    if (dst.Techs.Contains(id)) continue;
                    var t = w.Techs[id];
                    if (dst.PracticalEducation < t.MinPractical * 0.5 || !t.Prerequisites.All(dst.Techs.Contains)) continue;
                    double p = b.DiffusionRate * trust * (0.4 + dst.PracticalEducation) * boost * (w.IdeaNudges.ContainsKey(id) ? 2 : 1);
                    if (w.Rng.Chance(p)) adoptions.Add((dst, id));
                }
            }
        }
        foreach (var (c, id) in adoptions) c.Techs.Add(id);
    }

    private static void AnnounceWidespread(World w)
    {
        w.RefreshTechShares();
        foreach (var t in w.Techs.All)
        {
            if (w.TechShare(t.Id) < 0.5 || !w.WidespreadTechs.Add(t.Id)) continue;
            w.Emit(t.Effects.DriftPerTick > 0 ? Severity.Warning : Severity.Good,
                $"{t.Name} is now used by most of the federation." + (t.Effects.DriftPerTick > 0 ? $" Risk: {t.Risk}" : ""));
        }
    }

    internal static bool CanDiscover(Commune c, TechDefinition t) =>
        t.Prerequisites.All(c.Techs.Contains) && c.PracticalEducation >= t.MinPractical && c.PoliticalEducation >= t.MinPolitical;

    private static double Weight(World w, Commune c, TechDefinition t)
    {
        var a = t.Affinity;
        double kind = c.Kind switch
        {
            CommuneKind.Agrarian => a.Agrarian,
            CommuneKind.Industrial => a.Industrial,
            _ => (a.Agrarian + a.Industrial) / 2,
        };
        return 1 + kind
                 + a.Militant * CommuneValues.To01(c.Values.Militancy)
                 + a.Federalist * CommuneValues.To01(c.Values.Federalism)
                 + a.Collectivist * CommuneValues.To01(c.Values.Collectivism)
                 + (w.IdeaNudges.ContainsKey(t.Id) ? w.Balance.IdeaNudgeWeight : 0);
    }

    private static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
