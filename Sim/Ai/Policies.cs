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
        Helpers.AcceptFederation(w);
        Helpers.AnswerEvents(w, e => e.Drift - e.Influence * 0.05);
        foreach (var s in w.PendingSecessions.ToList()) Helpers.MediateFor(w, s.Commune);
        if (w.Tick % 15 == 7 && Helpers.NextBrake(w) is { } brake) w.Apply(new CirculateIdea(brake));
        Helpers.Diplomacy(w, crisis: w.Federation.Any(c => c.Front && c.Control < 0.5), reserve: 15);
        if (w.Drift.Value > 20 && w.Influence >= w.Balance.CostRecall * DriftEffects.RecallCost(w.Drift.Stage) + 5) w.Apply(new RecallDelegates());
        if (w.Tick % 10 == 3 && Helpers.LeastPolitical(w) is { } t) w.Apply(new ProposeProject(ProjectKind.AteneoNetwork, t.Id));
        if (w.ResearchFocus != "Organizational") w.Apply(new SetResearchFocus("Organizational"));
        if (w.Tick % 5 == 2 && w.Influence > 25) Helpers.CulturalWork(w, CulturalWorkKind.SchoolMovement);
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
        Helpers.Figures(w, crisis: true, rotateAt: 2); // never rotates; grants whatever they ask (below)
        Helpers.AnswerEvents(w, e => e.EnemyStrength * 3 - e.Influence * 0.1);
        foreach (var s in w.PendingSecessions.ToList()) w.Apply(new HoldCommune(s.Commune));
        if (!w.IdeaNudges.ContainsKey("federal_statistics")) w.Apply(new CirculateIdea("federal_statistics"));
        if (w.ResearchFocus != "Military") w.Apply(new SetResearchFocus("Military"));
        if (!w.Conscription) w.Apply(new SetConscription(true));
        if (!w.TreasuriesSeized) w.Apply(new SeizeTreasuries());
        if (w.Gold >= w.Balance.GoldPerPurchase) w.Apply(new BuyAbroad(Purchase.Arms));
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
        Helpers.AcceptFederation(w);
        // Back communes' own proposals, unless it's the planners' collectivization or influence is short.
        foreach (var pending in w.PendingEvents.Where(p => p.Id.StartsWith("proposal_") && !p.Id.Contains("Collectivization")).ToList())
            if (w.Influence > b.CostBackProposal + 10) w.Apply(new ChooseEventOption(pending.Id, 0));
        // Peace, when it's offered and drift can bear the delegates at the table.
        foreach (var pending in w.PendingEvents.Where(p => p.Id.StartsWith("peace_")).ToList())
            w.Apply(new ChooseEventOption(pending.Id, margin > b.PeaceDrift + 3 ? 0 : 1));
        Helpers.AnswerEvents(w, e => e.Drift + e.EnemyStrength * (crisis ? 6 : 3) - e.Influence * 0.05 - e.Wellbeing * 10 - e.Trust * 20);

        foreach (var s in w.PendingSecessions.ToList()) Helpers.MediateFor(w, s.Commune);
        Helpers.Figures(w, crisis, rotateAt: 0.3);
        Helpers.Front(w, crisis);
        if (margin < 5 && w.Influence >= b.CostRecall * DriftEffects.RecallCost(w.Drift.Stage)) w.Apply(new RecallDelegates());

        if (crisis) w.Apply(new ProposeProject(ProjectKind.DefensiveCampaign));
        Helpers.Diplomacy(w, crisis, reserve: 20);
        Helpers.ReliefForStarving(w);
        if (w.Tick % 20 == 5 && w.TechShare("militia_doctrine") < 0.5) w.Apply(new CirculateIdea("militia_doctrine"));
        if (w.Tick % 20 == 15 && Helpers.NextBrake(w) is { } brake) w.Apply(new CirculateIdea(brake));
        if (w.Tick % 16 == 8 && w.Influence > 40) w.Apply(new SponsorCongress());

        // Science to the front in a crisis, to the drift brakes when drift is close to a threshold.
        string focus = crisis ? "Military" : margin < 10 ? "Organizational" : w.Federation.Any(c => c.Starving) ? "Agricultural" : "Industrial";
        if (focus != w.ResearchFocus) w.Apply(new SetResearchFocus(focus));
        if (w.Tick % 6 == 1 && w.Influence > 35) Helpers.CulturalWork(w, crisis ? CulturalWorkKind.Newspaper : CulturalWorkKind.ArtMovement);

        // Fighters: volunteers where the front is weakest. No conscription: that's the centralizer's way.
        if (crisis && w.Tick % 3 == 0 && w.Influence > 20
            && w.Federation.Where(c => c.Front).OrderBy(c => c.Control).FirstOrDefault() is { } weakest)
            w.Apply(new CallVolunteers(weakest.Id));
        // Gold: seized in a crisis, spent at once (a hoard feeds its guards).
        if (crisis && !w.TreasuriesSeized && w.Drift.Value < 10 && w.LastDrift.Net < 0.15)
            w.Apply(new SeizeTreasuries()); // only with drift to spare, and not climbing fast: the gold's guards will add to it
        if (w.Gold >= w.Balance.GoldPerPurchase && margin > 5 && (crisis || w.Federation.Any(c => c.Starving)))
            w.Apply(new BuyAbroad(w.Federation.Any(c => c.Starving) ? Purchase.Grain : Purchase.Arms));

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
    /// <summary>
    /// The movement's foreign policy: delegates to the state whose stance matters most right now
    /// (an intervener, or a powerful state near a threshold), and an appeal for volunteers in a crisis.
    /// </summary>
    public static void Diplomacy(World w, bool crisis, double reserve)
    {
        if (w.States.Count == 0) return;
        if (w.Tick % 6 == 2 && w.Influence > reserve + w.Balance.CostDelegates)
        {
            var target = w.States
                .Where(s => s.Ideology != Global.Ideology.Fascist && s.Stance < 0.6)
                .OrderByDescending(s => s.Weight * (s.Does(Global.StateActions.Intervention) || s.Does(Global.StateActions.Embargo) ? 2 : 1)
                                        * (Math.Abs(s.Stance + 0.45) < 0.25 || Math.Abs(s.Stance - 0.3) < 0.2 ? 1.5 : 1))
                .FirstOrDefault();
            if (target != null) w.Apply(new SendDelegates(target.Id));
        }
        if (crisis && w.States.Any(s => s.Does(Global.StateActions.Volunteers)) && w.Influence > reserve + w.Balance.CostAppeal)
            w.Apply(new InternationalAppeal());

        // Party cells: expose them where they're strongest once they start to matter.
        if (w.Tick % 8 == 3 && w.Influence > reserve + w.Balance.CostExpose
            && w.Federation.OrderByDescending(c => c.PartyInfluence).FirstOrDefault() is { PartyInfluence: > 0.3 } cell)
            w.Apply(new ExposeCells(cell.Id));

        // Revolutions abroad: back a rising under way, and fan the ones close to breaking out.
        foreach (var s in w.States.Where(s => s.Uprising > 0 && s.UprisingSupport == 0))
            if (w.Influence > reserve + w.Balance.CostSupportUprising) w.Apply(new SupportUprising(s.Id));
        if (w.Tick % 10 == 4 && w.Influence > reserve + w.Balance.CostAgitate
            && w.States.Where(s => !s.Sister && s.Uprising == 0 && s.Radicalization > 0.3 && s.Repression < 0.3)
                       .OrderByDescending(s => s.Radicalization).FirstOrDefault() is { } ripe)
            w.Apply(new AgitateWorkers(ripe.Id));
    }

    /// <summary>A cultural work where culture is thinnest (and the work isn't still being taken up).</summary>
    /// <summary>
    /// Figures where they help most: commanders to the weakest front (in a crisis) or the most pressed
    /// one, orators where trust is thinnest, organizers where food is shortest, educators where political
    /// education is lowest. Anyone whose prestige x ambition passes `rotateAt` goes back to the ranks.
    /// </summary>
    public static void Figures(World w, bool crisis, double rotateAt)
    {
        if (w.Tick % 4 != 1) return;
        foreach (var f in w.Figures.Where(f => f.Alive && f.Resting == 0).ToList())
        {
            if (f.Commune != null && f.Hunger >= rotateAt && w.Influence > w.Balance.CostRotateFigure + 10)
            {
                w.Apply(new RotateFigure(f.Id));
                continue;
            }
            var members = w.Federation.ToList();
            if (members.Count == 0) return;
            Commune? best = f.Role switch
            {
                FigureRole.Commander => members.Where(c => c.Front).OrderBy(c => c.Control - (c.Pressure > c.Defense ? 1 : 0)).FirstOrDefault(),
                FigureRole.Orator => members.OrderBy(c => w.Trust.AverageTrust(c.Id)).FirstOrDefault(),
                FigureRole.Organizer => members.OrderBy(c => c.Food / Math.Max(0.1, c.Population)).FirstOrDefault(),
                _ => members.OrderBy(c => c.PoliticalEducation).FirstOrDefault(),
            };
            // Move only for a real gain: a commander to a worse front, the others once their post is in good shape.
            if (best is null || best.Id == f.Commune) continue;
            if (f.Commune is int now && w.Communes[now].InFederation && f.Role != FigureRole.Commander && (w.Tick / 4 + f.Id) % 3 != 0) continue; // settled in: move now and then
            if (w.Influence > w.Balance.CostPostFigure + 12) w.Apply(new PostFigure(f.Id, best.Id));
        }
    }

    /// <summary>Dig in where the front is weakest; raid from where it holds firmly.</summary>
    public static void Front(World w, bool crisis)
    {
        var b = w.Balance;
        var fronts = w.Federation.Where(c => c.Front).ToList();
        if (fronts.Count == 0) return;
        // Influence spent here isn't spent on schools: only with drift well in hand.
        double margin = (w.Drift.NextThreshold ?? 100) - w.Drift.Value;
        if (margin < 15 || w.LastDrift.Net > 0.25) return;
        if (w.Tick % 3 == 0 && w.Influence > b.CostFortify + 4
            && fronts.Where(c => c.Fortifying == 0 && c.Fortification < 0.4 && c.Goods > c.Population * b.FortifyGoods * 2)
                     .OrderBy(c => c.Defense - c.Pressure).FirstOrDefault() is { } weak
            && weak.Pressure > weak.Defense)
            w.Apply(new Fortify(weak.Id));
        if (w.Tick % 5 == 2 && w.Influence > b.CostRaid + 4
            && fronts.Where(c => w.Tick - c.LastRaid >= b.RaidCooldown && c.RaidChance >= 0.65).OrderByDescending(c => c.RaidChance).FirstOrDefault() is { } firm)
            w.Apply(new Raid(firm.Id));
    }

    public static void CulturalWork(World w, CulturalWorkKind kind)
    {
        var c = w.Federation.Where(c => w.Tick - c.LastCulturalWork >= CulturalWorks.Cooldown(w))
                            .OrderBy(c => c.Culture - (kind == CulturalWorkKind.Newspaper && c.Front ? 0.3 : 0)).FirstOrDefault();
        if (c != null) w.Apply(new FoundCulturalWork(c.Id, kind));
    }

    /// <summary>Accept a sister federation's offer to federate.</summary>
    public static void AcceptFederation(World w)
    {
        foreach (var pending in w.PendingEvents.ToList())
        {
            var options = w.EventDefinition(pending.Id).Options;
            int join = options.FindIndex(o => o.Effects.Federate.Length > 0);
            if (join >= 0) w.Apply(new ChooseEventOption(pending.Id, join));
        }
    }

    /// <summary>
    /// The drift brake most worth circulating: one some commune could discover or adopt now,
    /// least widespread first. Null if there is none.
    /// </summary>
    public static string? NextBrake(World w) =>
        w.Techs.All
            .Where(t => t.Effects.DriftBrake > 0 && w.TechShare(t.Id) < 0.6 && !w.IdeaNudges.ContainsKey(t.Id))
            .Where(t => w.Federation.Any(c => !c.Techs.Contains(t.Id) && Systems.ResearchSystem.CanAdopt(w, c, t)))
            .OrderBy(t => w.TechShare(t.Id)).ThenByDescending(t => t.Effects.DriftBrake)
            .Select(t => t.Id).FirstOrDefault();

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
