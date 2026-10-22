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

        Produce(w);
        Discover(w);
        Breakthrough(w);
        Diffuse(w);
        AnnounceWidespread(w);
    }

    /// <summary>
    /// Science: each commune's research, from its people, its practical education and the
    /// institutions it holds. Trade with more advanced states and sister federations add to it.
    /// </summary>
    private static void Produce(World w)
    {
        var b = w.Balance;
        foreach (var c in w.Communes)
            c.Science = c.InFederation ? Math.Sqrt(c.Population) * (0.2 + c.PracticalEducation) * (1 + w.LocalEffect(c, e => e.Science)) / 10 : 0;
        double contacts = 0;
        if (w.ForeignTrade && w.States.Count > 0)
        {
            var partners = w.States.Where(s => !s.Sister && !s.Does(Global.StateActions.Embargo)).ToList();
            double weight = partners.Sum(s => s.Weight);
            if (weight > 0) contacts += b.TradeScience * partners.Sum(s => s.Weight * s.TechLevel) / weight;
        }
        contacts += b.SisterScience * w.States.Count(s => s.Sister);
        w.LastScience = w.Communes.Sum(c => c.Science) * (1 + contacts) * w.Weeks;
        w.Science += w.LastScience;
    }

    /// <summary>The science needed for the next breakthrough: each one costs more.</summary>
    public static double BreakthroughCost(World w) => w.Balance.BreakthroughCost * (1 + w.Balance.BreakthroughGrowth * w.Breakthroughs);

    /// <summary>
    /// When enough science has accumulated, the commune with the most research makes a
    /// discovery in the line the movement has asked for. Without a focus, nobody organizes the
    /// research: science still speeds discoveries, but no breakthroughs come of it.
    /// </summary>
    private static void Breakthrough(World w)
    {
        double cost = BreakthroughCost(w);
        if (w.ResearchFocus is null || w.Science < cost) return;
        var options = w.Federation
            .SelectMany(c => w.LiveTechs.Where(t => !c.Techs.Contains(t.Id) && CanDiscover(w, c, t)).Select(t => (c, t)))
            .ToList();
        if (options.Any(o => o.t.Category == w.ResearchFocus)) options = options.Where(o => o.t.Category == w.ResearchFocus).ToList();
        if (options.Count == 0) return; // science keeps accumulating until something is within reach
        var weights = options.Select(o => o.c.Science * Weight(w, o.c, o.t) * Openness(w, o.c, o.t)).ToList();
        var (commune, tech) = w.Rng.PickWeighted(options, weights);
        w.Science -= cost;
        w.Breakthroughs++;
        bool first = !w.Communes.Any(o => o.Techs.Contains(tech.Id));
        commune.Techs.Add(tech.Id);
        w.Emit(Severity.Good, $"Breakthrough: {commune.Name}'s researchers {(first ? "develop" : "work out")} {tech.Name}. {tech.Perk}. Risk: {Lower(tech.Risk)}", commune.Id);
    }

    private static void Discover(World w)
    {
        var b = w.Balance;
        foreach (var c in w.Active)
        {
            if (!w.Rng.Chance(b.DiscoveryRate * (0.25 + c.PracticalEducation) * (1 + w.LocalEffect(c, e => e.Science)))) continue;
            var candidates = w.LiveTechs.Where(t => !c.Techs.Contains(t.Id) && CanDiscover(w, c, t)).ToList();
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
                    if (!CanAdopt(w, dst, t)) continue;
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
        foreach (var t in w.LiveTechs)
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
    internal static bool CanAdopt(World w, Commune c, TechDefinition t) =>
        w.Reached(t) && !w.CommonKnowledge(t) && t.Prerequisites.All(p => w.Knows(c, p)) && c.PracticalEducation >= t.MinPractical * 0.5 && c.PoliticalEducation >= t.MinPolitical * 0.5;

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

    internal static bool CanDiscover(World w, Commune c, TechDefinition t) =>
        w.Reached(t) && !w.CommonKnowledge(t) && t.Prerequisites.All(p => w.Knows(c, p)) && c.PracticalEducation >= t.MinPractical && c.PoliticalEducation >= t.MinPolitical;

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
                 + (w.IdeaNudges.ContainsKey(t.Id) ? w.Balance.IdeaNudgeWeight : 0)
                 + (w.ResearchFocus == t.Category ? w.Balance.FocusWeight : 0);
    }

    private static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
