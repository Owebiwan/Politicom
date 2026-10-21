namespace Politicom.Sim.Ai;

/// <summary>
/// Scripted players for headless batch runs. Comparing them is how we check that the core
/// dilemma holds: if centralizing always wins, the game teaches the wrong lesson.
/// </summary>
public interface IPolicy
{
    string Name { get; }
    void Act(World w);
}

public static class Policies
{
    public static readonly IReadOnlyList<IPolicy> All = new IPolicy[]
    {
        new Passive(), new Educator(), new Centralizer(), new Balanced(), new Trader(),
    };

    public static IPolicy Get(string name) =>
        All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown policy '{name}'. Known: {string.Join(", ", All.Select(p => p.Name))}.");
}

/// <summary>Does nothing. The baseline: what the communes do on their own.</summary>
public sealed class Passive : IPolicy
{
    public string Name => "passive";

    public void Act(World w)
    {
        if (w.Ally.OfferPending) w.Apply(new AnswerAllyOffer(false));
    }
}

/// <summary>Invests everything in political education, mediation and recall. Never centralizes.</summary>
public sealed class Educator : IPolicy
{
    public string Name => "educator";

    public void Act(World w)
    {
        if (w.Ally.OfferPending) w.Apply(new AnswerAllyOffer(false));
        Helpers.AnswerEvents(w, e => e.Drift - e.Influence * 0.05);
        foreach (var s in w.PendingSecessions.ToList()) Helpers.MediateFor(w, s.Commune);
        if (w.Drift.Value > 20 && w.Influence >= w.Balance.CostRecall * DriftEffects.RecallCost(w.Drift.Stage) + 5) w.Apply(new RecallDelegates());
        if (w.Tick % 10 == 3 && Helpers.LeastPolitical(w) is { } t) w.Apply(new ProposeProject(ProjectKind.AteneoNetwork, t.Id));
        Helpers.ReliefForStarving(w);
        while (w.Influence > 20)
        {
            var c = Helpers.LeastPolitical(w);
            if (c is null || !w.Apply(new FundSchool(c.Id, EducationTrack.Political)).Ok) break;
        }
        if (w.Influence > 12) Helpers.MediateWeakest(w);
    }
}

/// <summary>Wins the war at any cost: war committee, government arms, practical schooling, statistics.</summary>
public sealed class Centralizer : IPolicy
{
    public string Name => "centralizer";

    public void Act(World w)
    {
        if (!w.WarCommittee) w.Apply(new SetWarCommittee(true));
        if (!w.ForeignTrade) w.Apply(new SetForeignTrade(true));
        if (w.Ally.OfferPending) w.Apply(new AnswerAllyOffer(true));
        Helpers.AnswerEvents(w, e => e.EnemyStrength * 3 - e.Influence * 0.1);
        foreach (var s in w.PendingSecessions.ToList()) w.Apply(new HoldCommune(s.Commune));
        if (!w.IdeaNudges.ContainsKey("federal_statistics")) w.Apply(new CirculateIdea("federal_statistics"));
        if (w.Tick % 15 == 2) w.Apply(new ProposeProject(ProjectKind.Collectivization));
        if (w.OffensiveUnderway || w.Tick % 12 == 0) w.Apply(new ProposeProject(ProjectKind.DefensiveCampaign));
        w.Apply(new SponsorCongress());
        while (w.Influence > 20)
        {
            var c = w.Federation.Where(c => c.PracticalSchooling < w.Balance.MaxSchooling).OrderBy(c => c.PracticalEducation).FirstOrDefault();
            if (c is null || !w.Apply(new FundSchool(c.Id, EducationTrack.Practical)).Ok) break;
        }
    }
}

/// <summary>
/// Plays the dilemma: centralizes only in emergencies, dissolves the committee when drift climbs,
/// keeps the two education tracks in balance, and recalls delegates before thresholds.
/// </summary>
public sealed class Balanced : IPolicy
{
    public string Name => "balanced";

    public void Act(World w) => Act(w, trade: null);

    /// <summary>trade: null = only when the front is short of arms; true = whenever drift allows.</summary>
    internal void Act(World w, bool? trade)
    {
        var b = w.Balance;
        bool crisis = w.Federation.Any(c => c.Front && c.Control < 0.5) || w.OffensiveUnderway;
        double margin = (w.Drift.NextThreshold ?? 100) - w.Drift.Value;

        if (w.WarCommittee && (!crisis || margin < 6)) w.Apply(new SetWarCommittee(false));
        else if (!w.WarCommittee && crisis && margin > 12) w.Apply(new SetWarCommittee(true));

        // Trade grain for arms only while the front is short and drift has room to spare.
        bool wantTrade = trade ?? w.Federation.Any(c => c.Front && c.GoodsCoverage < 0.9);
        if (w.ForeignTrade && (!wantTrade || margin < 6)) w.Apply(new SetForeignTrade(false));
        else if (!w.ForeignTrade && wantTrade && margin > 10 && w.Embargo < 0.5) w.Apply(new SetForeignTrade(true));

        if (w.Ally.OfferPending) w.Apply(new AnswerAllyOffer(crisis && w.Drift.Value < 15));
        Helpers.AnswerEvents(w, e => e.Drift + e.EnemyStrength * (crisis ? 6 : 3) - e.Influence * 0.05 - e.Wellbeing * 10 - e.Trust * 20);

        foreach (var s in w.PendingSecessions.ToList()) Helpers.MediateFor(w, s.Commune);
        if (margin < 5 && w.Influence >= b.CostRecall * DriftEffects.RecallCost(w.Drift.Stage)) w.Apply(new RecallDelegates());

        if (crisis) w.Apply(new ProposeProject(ProjectKind.DefensiveCampaign));
        Helpers.ReliefForStarving(w);
        if (w.Tick % 20 == 5 && w.TechShare("militia_doctrine") < 0.5) w.Apply(new CirculateIdea("militia_doctrine"));
        if (w.Tick % 20 == 15) w.Apply(new CirculateIdea("imperative_mandate"));
        if (w.Tick % 16 == 8 && w.Influence > 40) w.Apply(new SponsorCongress());

        while (w.Influence > 25)
        {
            // Fund whichever track is further behind where it's most behind.
            var c = w.Federation.OrderByDescending(c => Math.Abs(c.PracticalEducation - c.PoliticalEducation) + (1 - c.PoliticalEducation) * 0.3).FirstOrDefault();
            if (c is null) break;
            var track = c.PracticalEducation > c.PoliticalEducation - 0.05 ? EducationTrack.Political : EducationTrack.Practical;
            if (!w.Apply(new FundSchool(c.Id, track)).Ok) break;
        }
        if (w.Influence > 15) Helpers.MediateWeakest(w);
    }
}

/// <summary>The balanced player, but trading grain for arms whenever drift has room. Measures whether trade pays.</summary>
public sealed class Trader : IPolicy
{
    private readonly Balanced _balanced = new();

    public string Name => "trader";

    public void Act(World w) => _balanced.Act(w, trade: true);
}

internal static class Helpers
{
    /// <summary>Answers every pending event with the option whose effects score lowest.</summary>
    public static void AnswerEvents(World w, Func<EventEffects, double> cost)
    {
        foreach (var pending in w.PendingEvents.ToList())
        {
            var options = w.EventDefinition(pending.Id).Options;
            int best = Enumerable.Range(0, options.Count).MinBy(i => cost(options[i].Effects));
            w.Apply(new ChooseEventOption(pending.Id, best));
        }
    }

    public static Commune? LeastPolitical(World w) =>
        w.Federation.Where(c => c.PoliticalSchooling < w.Balance.MaxSchooling).OrderBy(c => c.PoliticalEducation - c.ExpertGap).FirstOrDefault();

    public static void MediateFor(World w, int commune)
    {
        var weakest = w.Trust.Neighbors(commune).Where(n => w.Communes[n].InFederation).OrderBy(n => w.Trust[commune, n]).Take(2).ToList();
        foreach (var n in weakest) w.Apply(new Mediate(commune, n));
    }

    public static void MediateWeakest(World w)
    {
        var edge = w.Trust.Edges()
            .Where(e => w.Communes[e.A].InFederation && w.Communes[e.B].InFederation)
            .OrderBy(e => w.Trust[e.A, e.B]).FirstOrDefault();
        if (edge != default) w.Apply(new Mediate(edge.A, edge.B));
    }

    public static void ReliefForStarving(World w)
    {
        var hungry = w.Federation.Where(c => c.Starving).OrderByDescending(c => c.Population).FirstOrDefault();
        if (hungry != null && !w.ActiveProjects.Any(p => p.Kind == ProjectKind.FamineRelief && p.Target == hungry.Id))
            w.Apply(new ProposeProject(ProjectKind.FamineRelief, hungry.Id));
    }
}
