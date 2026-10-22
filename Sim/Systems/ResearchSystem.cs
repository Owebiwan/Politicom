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
            var weights = candidates.Select(t => Weight(w, c, t) * Openness(w, c, t)).ToList();
            var tech = w.Rng.PickWeighted(candidates, weights);
            bool first = !w.Communes.Any(o => o.Techs.Contains(tech.Id));
            c.Techs.Add(tech.Id);
            if (first)
                w.Emit(Severity.Info, $"{c.Name} develops {tech.Name}. {tech.Perk}. Risk: {Lower(tech.Risk)}", c.Id);
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
                    if (!CanAdopt(dst, t)) continue;
                    double p = b.DiffusionRate * trust * (0.4 + dst.PracticalEducation) * boost * (w.IdeaNudges.ContainsKey(id) ? 2 : 1)
                               * (w.Rail(src.Id, dst.Id) ? b.RailDiffusion : 1) * Openness(w, dst, t);
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

    /// <summary>
    /// Adopting an idea someone else developed is easier than discovering it: half the education
    /// on both tracks. Used for diffusion along trust links and for congresses.
    /// </summary>
    internal static bool CanAdopt(Commune c, TechDefinition t) =>
        t.Prerequisites.All(c.Techs.Contains) && c.PracticalEducation >= t.MinPractical * 0.5 && c.PoliticalEducation >= t.MinPolitical * 0.5;

    /// <summary>
    /// A commune that has taken a line's free path (holds a technology there that brakes drift
    /// more than it adds) is reluctant to take up that line's centralizing technologies.
    /// </summary>
    internal static double Openness(World w, Commune c, TechDefinition t)
    {
        if (t.Effects.DriftPerTick <= t.Effects.DriftBrake) return 1;
        foreach (var id in c.Techs)
        {
            var held = w.Techs[id];
            if (held.Category == t.Category && held.Effects.DriftBrake > held.Effects.DriftPerTick) return w.Balance.PathResistance;
        }
        return 1;
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
