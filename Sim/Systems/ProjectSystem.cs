namespace Politicom.Sim.Systems;

/// <summary>
/// Voluntary projects. Every invited commune weighs the project against its values, needs and
/// trust in the others, and joins or leaves on its own.
/// </summary>
internal static class ProjectSystem
{
    public static void Run(World w)
    {
        var b = w.Balance;
        foreach (var p in w.ActiveProjects.ToList())
        {
            Decide(w, p);

            var participants = p.Participants.Select(id => w.Communes[id]).ToList();
            double effort = participants.Sum(c => b.ContributionBase * Math.Sqrt(c.Population) * (c.GoodsShort ? 0.5 : 1) * c.Control);
            p.LastOverhead = Coordination.Cost(
                participants.Count,
                participants.Count == 0 ? 0 : participants.Average(c => c.PoliticalEducation),
                w.SocietyEffect(e => e.Coordination),
                w.Drift.Stage,
                w.WarCommittee,
                b);
            p.LastOutput = effort * (1 - p.LastOverhead);
            p.Progress += p.LastOutput;
            foreach (var c in participants) c.Goods -= 0.3 * b.ContributionBase * Math.Sqrt(c.Population);

            if (p.Progress >= p.Work) Complete(w, p);
            else if (w.Tick >= p.Deadline && !p.Extended && p.Progress >= 0.75 * p.Work) Extend(w, p);
            else if (w.Tick >= p.Deadline) Fail(w, p);
        }
    }

    /// <summary>
    /// How much a commune wants to be part of a project. Positive means it would join.
    /// Answers the design question "utility score or vote?": a utility score per commune, where
    /// values stand in for how the assembly would vote.
    /// </summary>
    public static double Utility(World w, Project p, Commune c)
    {
        var def = p.Definition;
        var v = c.Values;
        var prof = def.Profile;
        double norm = Math.Abs(prof.Collectivism) + Math.Abs(prof.Militancy) + Math.Abs(prof.Federalism);
        double fit = norm == 0 ? 0 : (v.Collectivism * prof.Collectivism + v.Militancy * prof.Militancy + v.Federalism * prof.Federalism) / norm;

        var partners = p.Participants.Where(x => x != c.Id && w.Trust.Connected(c.Id, x)).ToList();
        double trust = partners.Count > 0 ? partners.Average(x => w.Trust[c.Id, x]) : w.Trust.AverageTrust(c.Id);

        double localism = 1 - CommuneValues.To01(v.Federalism);
        int otherProjects = w.ProjectsJoinedBy(c.Id) - (p.Participants.Contains(c.Id) ? 1 : 0);

        return 0.35 * fit
               + 0.6 * (trust - 0.5)
               + Benefit(w, p, c)
               + 0.15 * c.PoliticalEducation
               - (c.Starving ? 0.4 : 0)
               - 0.15 * otherProjects
               - 0.2 * localism * (def.FederationWide ? 1 : 0.3)
               - 0.1
               + p.Persuasion.GetValueOrDefault(c.Id);
    }

    private static double Benefit(World w, Project p, Commune c)
    {
        var mil = CommuneValues.To01(c.Values.Militancy);
        switch (p.Kind)
        {
            case ProjectKind.FamineRelief:
                var target = p.Target!.Value;
                double surplus = c.Food > EconomySystem.Reserve(w.Balance, c, true) ? 0.1 : -0.1;
                return w.Trust.Connected(c.Id, target) ? 0.1 + 0.4 * w.Trust[c.Id, target] + surplus : surplus;
            case ProjectKind.Railway:
                return c.Id == p.Target || c.Id == p.Target2 ? 0.4 : 0.1 * CommuneValues.To01(c.Values.Federalism);
            case ProjectKind.DefensiveCampaign:
                if (c.Front) return 0.5;
                return w.Trust.Neighbors(c.Id).Any(n => w.Communes[n].Front) ? 0.1 + 0.25 * mil : 0.2 * mil - 0.1;
            case ProjectKind.AteneoNetwork:
                return (c.Id == p.Target ? 0.3 : 0.1) + 0.2 * CommuneValues.To01(c.Values.Collectivism);
            case ProjectKind.Collectivization:
                return c.Kind == CommuneKind.Agrarian ? -0.3 : 0.15 + 0.2 * CommuneValues.To01(c.Values.Collectivism);
            default:
                return 0;
        }
    }

    private static void Decide(World w, Project p)
    {
        var b = w.Balance;
        foreach (var id in p.Invited)
        {
            var c = w.Communes[id];
            bool inProject = p.Participants.Contains(id);
            if (!c.InFederation)
            {
                p.Participants.Remove(id);
                continue;
            }
            double u = Utility(w, p, c);
            if (inProject && u < -0.25 && w.Rng.Chance(0.3))
            {
                p.Participants.Remove(id);
                foreach (var other in p.Participants) w.Trust.Add(id, other, -b.BrokenCommitmentTrustLoss);
                w.Emit(Severity.Warning, $"{c.Name} walks out of the {p.Definition.Name.ToLowerInvariant()}. Its partners feel let down.", id);
            }
            else if (!inProject && u > 0 && w.Rng.Chance(b.JoinChance))
            {
                p.Participants.Add(id);
            }
        }
    }

    private static void Complete(World w, Project p)
    {
        var b = w.Balance;
        p.Status = ProjectStatus.Completed;
        var participants = p.Participants.ToList();

        // Kept agreements build trust.
        foreach (var a in participants)
            foreach (var c in participants)
                if (a < c) w.Trust.Add(a, c, 0.03);

        string effect;
        switch (p.Kind)
        {
            case ProjectKind.FamineRelief:
                var target = w.Communes[p.Target!.Value];
                target.Food += target.Population * b.FoodNeed * 4;
                target.Wellbeing = Math.Min(1, target.Wellbeing + 0.1);
                foreach (var id in participants) w.Trust.Add(id, target.Id, 0.06);
                effect = $"{target.Name} is fed.";
                break;
            case ProjectKind.Railway:
                int a1 = p.Target!.Value, a2 = p.Target2!.Value;
                if (w.Trust.Connected(a1, a2)) w.Trust.Add(a1, a2, 0.1);
                else w.Trust.Link(a1, a2, 0.55);
                w.TransportBonus = Math.Min(0.6, w.TransportBonus + 0.1);
                effect = $"{w.Communes[a1].Name} and {w.Communes[a2].Name} are linked; pooled goods travel cheaper. The line's organizers have gained standing.";
                break;
            case ProjectKind.DefensiveCampaign:
                var strongest = w.Enemies.MaxBy(x => x.EffectiveStrength)!;
                strongest.Strength *= 0.85;
                foreach (var a in participants)
                    foreach (var c in participants)
                        if (a < c) w.Trust.Add(a, c, 0.05);
                effect = $"The {strongest.Name} is pushed back.";
                break;
            case ProjectKind.AteneoNetwork:
                foreach (var id in participants)
                {
                    var c = w.Communes[id];
                    c.PoliticalEducation += 0.06 * (1 - c.PoliticalEducation);
                }
                effect = "Libraries and debate nights open across the area.";
                break;
            case ProjectKind.Collectivization:
                foreach (var id in participants)
                    if (w.Communes[id].Kind != CommuneKind.Agrarian) w.Communes[id].ProductivityBonus += 0.12;
                effect = "Industry runs to a common plan. The planners who drew it up now matter a great deal.";
                break;
            default:
                effect = "";
                break;
        }
        w.BumpDrift(p.Definition.CompletionDrift);
        w.Emit(Severity.Good, $"{p.Definition.Name} completed with {participants.Count} communes. {effect}");
    }

    /// <summary>Projects run late. One extension for a project that is most of the way there.</summary>
    private static void Extend(World w, Project p)
    {
        p.Extended = true;
        p.Deadline = w.Tick + (int)Math.Ceiling(0.25 * w.Balance.ProjectDeadlineFactor * p.Definition.ExpectedWeeks);
        w.Emit(Severity.Warning, $"The {p.Definition.Name.ToLowerInvariant()} is running late ({p.Progress:0}/{p.Work:0}). " +
                                 $"Its participants give it until {w.Turn} {p.Deadline} — once.");
    }

    private static void Fail(World w, Project p)
    {
        p.Status = ProjectStatus.Failed;
        var participants = p.Participants.ToList();
        foreach (var a in participants)
            foreach (var c in participants)
                if (a < c) w.Trust.Add(a, c, -w.Balance.BrokenCommitmentTrustLoss * 0.5);
        string why = participants.Count * 2 < p.Invited.Count
            ? "Too few communes would commit."
            : $"{p.LastOverhead:P0} of the effort was lost to coordinating {participants.Count} communes.";
        w.Emit(Severity.Warning, $"{p.Definition.Name} collapses unfinished ({p.Progress:0}/{p.Work:0}). {why}");
    }

    /// <summary>Who gets asked to join.</summary>
    internal static IEnumerable<int> Invitees(World w, ProjectKind kind, int? target, int? target2)
    {
        bool Member(int id) => w.Communes[id].InFederation;
        IEnumerable<int> Around(int id, int hops) =>
            w.Federation.Where(c => w.Trust.Hops(id, c.Id) <= hops).Select(c => c.Id);

        return kind switch
        {
            ProjectKind.FamineRelief => Around(target!.Value, 2).Where(id => id != target),
            ProjectKind.Railway => Around(target!.Value, 1).Concat(Around(target2!.Value, 1)).Distinct(),
            ProjectKind.DefensiveCampaign => w.Federation.Where(c => c.Front)
                .SelectMany(c => w.Trust.Neighbors(c.Id).Append(c.Id)).Where(Member).Distinct(),
            ProjectKind.AteneoNetwork => Around(target!.Value, 1),
            ProjectKind.Collectivization => w.Federation.Where(c => c.Kind != CommuneKind.Agrarian).Select(c => c.Id),
            _ => Enumerable.Empty<int>(),
        };
    }
}
