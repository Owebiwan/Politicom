namespace Politicom.Sim;

public enum EducationTrack { Practical, Political }

/// <summary>
/// What the movement can do. There is deliberately no "order commune" action: the player
/// persuades, funds, mediates and circulates ideas; communes decide for themselves.
/// </summary>
public abstract record PlayerAction;

public sealed record FundSchool(int Commune, EducationTrack Track) : PlayerAction;
public sealed record CirculatePamphlets(int Commune) : PlayerAction;
public sealed record Mediate(int A, int B) : PlayerAction;
public sealed record ProposeProject(ProjectKind Kind, int? Target = null, int? Target2 = null) : PlayerAction;
public sealed record Persuade(int Commune, int ProjectId) : PlayerAction;
public sealed record SponsorCongress : PlayerAction;
public sealed record CirculateIdea(string TechId) : PlayerAction;
public sealed record RecallDelegates : PlayerAction;
public sealed record SetWarCommittee(bool Active) : PlayerAction;
/// <summary>Open or close trade with capitalist states: food for arms, at the price of drift.</summary>
public sealed record SetForeignTrade(bool Active) : PlayerAction;
public sealed record AnswerAllyOffer(bool Accept) : PlayerAction;
public sealed record HoldCommune(int Commune) : PlayerAction;
/// <summary>Send delegates to win a foreign state's sympathy.</summary>
public sealed record SendDelegates(string StateId) : PlayerAction;
/// <summary>Agitate a foreign state's workers: they radicalize, and the state resents it.</summary>
public sealed record AgitateWorkers(string StateId) : PlayerAction;
/// <summary>Expose a rival party's cells in a commune and its neighbours.</summary>
public sealed record ExposeCells(int Commune) : PlayerAction;
/// <summary>Send arms and organizers to a revolution under way abroad.</summary>
public sealed record SupportUprising(string StateId) : PlayerAction;
/// <summary>An appeal to the workers of the world: double the volunteers for a while.</summary>
public sealed record InternationalAppeal : PlayerAction;
/// <summary>Ask the communes to put their science into one line of technology (null = balanced).</summary>
public sealed record SetResearchFocus(string? Category) : PlayerAction;

public enum CulturalWorkKind { Newspaper, SchoolMovement, ArtMovement }

public static class CulturalWorks
{
    /// <summary>Turns before a commune can take up another cultural work (a set number of weeks).</summary>
    public static int Cooldown(World w) => (int)Math.Ceiling(w.Balance.CulturalWorkCooldown / w.Weeks);

    /// <summary>What each cultural work does, for the UI.</summary>
    public static string Describe(CulturalWorkKind kind) => kind switch
    {
        CulturalWorkKind.Newspaper => "A newspaper: culture here and in neighbouring communes, and its pages reach the enemy's workers.",
        CulturalWorkKind.SchoolMovement => "A school movement: culture and political education here and around it.",
        _ => "Songs, theatre and posters: a lot of culture here, and the neighbours warm to it.",
    };

    public static string Name(CulturalWorkKind kind, int year = 2000) => kind switch
    {
        CulturalWorkKind.Newspaper => year < 1450 ? "Send out storytellers" : year < 1700 ? "Print broadsheets" : "Found a newspaper",
        CulturalWorkKind.SchoolMovement => year < 1450 ? "Open a school of the commons" : "Start a school movement",
        _ => year < 1450 ? "Hold festivals and songs" : "Back an art movement",
    };
}

/// <summary>Found a cultural work in a commune: it builds cultural influence there and around it.</summary>
public sealed record FoundCulturalWork(int Commune, CulturalWorkKind Kind) : PlayerAction;

/// <summary>Answer a scripted historical event. Option is zero-based.</summary>
public sealed record ChooseEventOption(string EventId, int Option) : PlayerAction;

public sealed record ActionResult(bool Ok, string Message)
{
    public static ActionResult Done(string message) => new(true, message);
    public static ActionResult Fail(string message) => new(false, message);
}

internal static class ActionSystem
{
    public static ActionResult Apply(World w, PlayerAction action)
    {
        return action switch
        {
            FundSchool a => FundSchool(w, a),
            CirculatePamphlets a => Pamphlets(w, a),
            Mediate a => Mediate(w, a),
            ProposeProject a => Propose(w, a),
            Persuade a => Persuade(w, a),
            SponsorCongress => Congress(w),
            CirculateIdea a => Idea(w, a),
            RecallDelegates => Recall(w),
            SetWarCommittee a => WarCommittee(w, a),
            SetForeignTrade a => ForeignTrade(w, a),
            AnswerAllyOffer a => Ally(w, a),
            HoldCommune a => Hold(w, a),
            ChooseEventOption a => Systems.EventSystem.Choose(w, a.EventId, a.Option),
            SendDelegates a => Delegates(w, a),
            AgitateWorkers a => Agitate(w, a),
            InternationalAppeal => Appeal(w),
            SupportUprising a => Support(w, a),
            ExposeCells a => Expose(w, a),
            SetResearchFocus a => Focus(w, a),
            FoundCulturalWork a => CulturalWork(w, a),
            _ => ActionResult.Fail($"Unknown action {action.GetType().Name}."),
        };
    }

    private static ActionResult FundSchool(World w, FundSchool a)
    {
        if (Member(w, a.Commune) is not { } c) return NotMember(w, a.Commune);
        var b = w.Balance;
        double current = a.Track == EducationTrack.Practical ? c.PracticalSchooling : c.PoliticalSchooling;
        if (current >= b.MaxSchooling) return ActionResult.Fail($"{c.Name} cannot spare more labor for schooling.");
        if (!Spend(w, b.CostSchool)) return Broke(w, b.CostSchool);
        double next = Math.Min(b.MaxSchooling, current + b.FundedSchoolingBoost);
        if (a.Track == EducationTrack.Practical) c.PracticalSchooling = next;
        else c.PoliticalSchooling = next;
        string note = a.Track == EducationTrack.Practical && c.PracticalEducation > c.PoliticalEducation
            ? " Careful: its practical education already outruns its political education (the expert trap)."
            : "";
        w.Emit(Severity.Info, $"You fund {a.Track.ToString().ToLowerInvariant()} schooling in {c.Name}. Fewer hands in the fields for now.{note}", c.Id);
        return ActionResult.Done($"Funded {a.Track.ToString().ToLowerInvariant()} schooling in {c.Name}.");
    }

    private static ActionResult Pamphlets(World w, CirculatePamphlets a)
    {
        if (Member(w, a.Commune) is not { } c) return NotMember(w, a.Commune);
        if (!Spend(w, w.Balance.CostPamphlets)) return Broke(w, w.Balance.CostPamphlets);
        c.PoliticalEducation += 0.03 * (1 - c.PoliticalEducation);
        foreach (var n in w.Trust.Neighbors(c.Id))
        {
            var d = w.Communes[n];
            if (!d.Occupied) d.PoliticalEducation += 0.015 * w.Trust[c.Id, n] * (1 - d.PoliticalEducation);
        }
        foreach (var k in c.Fronts) w.Enemies[k].Radicalization = Math.Min(1, w.Enemies[k].Radicalization + 0.01);
        return ActionResult.Done($"Pamphlets circulate in and around {c.Name}.");
    }

    private static ActionResult Mediate(World w, Mediate a)
    {
        if (Valid(w, a.A) is not { } x || Valid(w, a.B) is not { } y) return ActionResult.Fail("Both communes must be free.");
        if (!w.Trust.Connected(x.Id, y.Id)) return ActionResult.Fail($"{x.Name} and {y.Name} are not neighbours.");
        if (!Spend(w, w.Balance.CostMediate)) return Broke(w, w.Balance.CostMediate);
        double gain = w.Balance.MediationTrustGain * (1 + 0.5 * (x.PoliticalEducation + y.PoliticalEducation) / 2);
        w.Trust.Add(x.Id, y.Id, gain);
        w.Emit(Severity.Info, $"You mediate between {x.Name} and {y.Name}. Trust {w.Trust[x.Id, y.Id]:P0}.", x.Id);
        return ActionResult.Done($"Mediated {x.Name} ↔ {y.Name}: trust now {w.Trust[x.Id, y.Id]:P0}.");
    }

    private static ActionResult Propose(World w, ProposeProject a)
    {
        var b = w.Balance;
        var def = ProjectCatalog.Get(a.Kind);
        if (w.ActiveProjects.Count() >= b.MaxActiveProjects) return ActionResult.Fail($"At most {b.MaxActiveProjects} projects can run at once.");
        if (def.TargetsNeeded >= 1 && (a.Target is null || Member(w, a.Target.Value) is null)) return ActionResult.Fail($"{def.Name} needs a federated target commune.");
        if (def.TargetsNeeded >= 2 && (a.Target2 is null || Member(w, a.Target2.Value) is null || a.Target2 == a.Target))
            return ActionResult.Fail($"{def.Name} needs two different federated communes.");
        if (a.Kind == ProjectKind.DefensiveCampaign && !w.Federation.Any(c => c.Front)) return ActionResult.Fail("There is no front to defend.");
        if (a.Kind == ProjectKind.Railway && a.Target is int r1 && a.Target2 is int r2 && w.Rail(r1, r2))
            return ActionResult.Fail("Those two communes are already joined by rail.");
        if (w.ActiveProjects.Any(p => p.Kind == a.Kind && p.Target == a.Target && p.Target2 == a.Target2))
            return ActionResult.Fail("That project is already under way.");
        if (!Spend(w, b.CostPropose)) return Broke(w, b.CostPropose);

        var project = new Project
        {
            Id = w.NextProjectId++,
            Kind = a.Kind,
            Target = a.Target,
            Target2 = a.Target2,
            StartedTick = w.Tick,
            Work = def.Work,
            Deadline = w.Tick + (int)Math.Ceiling(b.ProjectDeadlineFactor * def.ExpectedWeeks),
        };
        foreach (var id in Systems.ProjectSystem.Invitees(w, a.Kind, a.Target, a.Target2)) project.Invited.Add(id);
        w.Projects.Add(project);
        string where = a.Target is { } t ? $" for {w.Communes[t].Name}" + (a.Target2 is { } t2 ? $" and {w.Communes[t2].Name}" : "") : "";
        w.Emit(Severity.Info, $"You propose a {def.Name.ToLowerInvariant()}{where}. {project.Invited.Count} communes are invited to join.");
        return ActionResult.Done($"Proposed project #{project.Id}: {def.Name}{where}.");
    }

    private static ActionResult Persuade(World w, Persuade a)
    {
        if (Member(w, a.Commune) is not { } c) return NotMember(w, a.Commune);
        var p = w.ActiveProjects.FirstOrDefault(x => x.Id == a.ProjectId);
        if (p is null) return ActionResult.Fail($"No active project #{a.ProjectId}.");
        if (!p.Invited.Contains(c.Id)) return ActionResult.Fail($"{c.Name} was not invited to that project.");
        if (!Spend(w, w.Balance.CostPersuade)) return Broke(w, w.Balance.CostPersuade);
        p.Persuasion[c.Id] = p.Persuasion.GetValueOrDefault(c.Id) + 0.3;
        return ActionResult.Done($"Organizers make the case for {p.Definition.Name.ToLowerInvariant()} in {c.Name}'s assembly.");
    }

    private static ActionResult Congress(World w)
    {
        var b = w.Balance;
        if (w.Tick - w.LastCongressTick < b.CongressCooldown)
            return ActionResult.Fail($"The last congress was too recent (wait {w.Turns(b.CongressCooldown - (w.Tick - w.LastCongressTick))}).");
        if (!Spend(w, b.CostCongress)) return Broke(w, b.CostCongress);
        w.LastCongressTick = w.Tick;
        var members = w.Federation.ToList();
        int adoptions = 0;
        foreach (var t in w.LiveTechs.Where(t => w.TechShare(t.Id) >= 0.25))
            foreach (var c in members.Where(c => !c.Techs.Contains(t.Id) && Systems.ResearchSystem.CanAdopt(w, c, t)))
                if (w.Rng.Chance(0.35 * (0.4 + c.PracticalEducation)))
                {
                    c.Techs.Add(t.Id);
                    adoptions++;
                }
        foreach (var (i, j) in w.Trust.Edges())
            if (w.Communes[i].InFederation && w.Communes[j].InFederation) w.Trust.Add(i, j, 0.02);
        w.BumpDrift(b.CongressDrift);
        w.Emit(Severity.Good, $"A regional congress meets. Delegates share {adoptions} techniques and goodwill — and a standing secretariat lingers afterwards.");
        return ActionResult.Done($"Congress held: {adoptions} adoptions.");
    }

    private static ActionResult Idea(World w, CirculateIdea a)
    {
        if (!w.Techs.Contains(a.TechId)) return ActionResult.Fail($"Unknown technology '{a.TechId}'.");
        if (!Spend(w, w.Balance.CostCirculateIdea)) return Broke(w, w.Balance.CostCirculateIdea);
        w.IdeaNudges[a.TechId] = w.Balance.IdeaNudgeTicks;
        var t = w.Techs[a.TechId];
        return ActionResult.Done($"Pamphlets and speakers promote {t.Name} for {w.Turns(w.Balance.IdeaNudgeTicks)}.");
    }

    private static ActionResult Focus(World w, SetResearchFocus a)
    {
        if (a.Category is { } line && !w.Techs.Categories.Contains(line)) return ActionResult.Fail($"Unknown line of technology '{line}'.");
        if (w.ResearchFocus == a.Category) return ActionResult.Done("The research focus is unchanged.");
        w.ResearchFocus = a.Category;
        string text = a.Category is null
            ? "The movement asks the communes to research whatever they need most."
            : $"The movement asks the communes to put their science into {a.Category.ToLowerInvariant()} techniques.";
        w.Emit(Severity.Info, text);
        return ActionResult.Done(text);
    }

    private static ActionResult CulturalWork(World w, FoundCulturalWork a)
    {
        if (Member(w, a.Commune) is not { } c) return NotMember(w, a.Commune);
        var b = w.Balance;
        int cooldown = CulturalWorks.Cooldown(w);
        if (w.Tick - c.LastCulturalWork < cooldown)
            return ActionResult.Fail($"{c.Name} is still taking up its last cultural work (wait {w.Turns(cooldown - (w.Tick - c.LastCulturalWork))}).");
        if (!Spend(w, b.CostCulturalWork)) return Broke(w, b.CostCulturalWork);
        c.LastCulturalWork = w.Tick;
        void Raise(Commune x, double amount) => x.Culture = Math.Min(1, x.Culture + amount * (1 - x.Culture));
        var neighbours = w.Trust.Neighbors(c.Id).Where(n => !w.Communes[n].Occupied).ToList();
        string text;
        switch (a.Kind)
        {
            case CulturalWorkKind.Newspaper:
                Raise(c, 0.12);
                foreach (var n in neighbours) Raise(w.Communes[n], 0.06 * w.Trust[c.Id, n]);
                foreach (var e in w.Enemies) e.Radicalization = Math.Min(1, e.Radicalization + 0.01);
                text = w.Year < 1450 ? $"Storytellers carry the federation's news out from {c.Name}, from market to market."
                     : w.Year < 1700 ? $"Broadsheets printed in {c.Name} are read aloud in every tavern around it."
                     : $"A new paper is printed in {c.Name} and read well beyond it.";
                break;
            case CulturalWorkKind.SchoolMovement:
                Raise(c, 0.08);
                c.PoliticalEducation = Math.Min(1, c.PoliticalEducation + 0.03 * (1 - c.PoliticalEducation));
                foreach (var n in neighbours)
                {
                    var d = w.Communes[n];
                    Raise(d, 0.04 * w.Trust[c.Id, n]);
                    d.PoliticalEducation = Math.Min(1, d.PoliticalEducation + 0.015 * w.Trust[c.Id, n] * (1 - d.PoliticalEducation));
                }
                text = $"Free schools open in {c.Name}: evening classes, reading circles, a school for the children.";
                break;
            default:
                Raise(c, 0.2);
                foreach (var n in neighbours) w.Trust.Add(c.Id, n, 0.02);
                text = $"{c.Name} sings: new songs, street theatre and posters celebrate the revolution.";
                break;
        }
        w.Emit(Severity.Good, text, c.Id);
        return ActionResult.Done(text);
    }

    private static ActionResult Recall(World w)
    {
        var b = w.Balance;
        double cost = b.CostRecall * DriftEffects.RecallCost(w.Drift.Stage);
        if (!Spend(w, cost)) return Broke(w, cost);
        w.BumpDrift(-b.RecallDrift);
        w.RecallPenaltyTicks = 4;
        foreach (var p in w.ActiveProjects) p.Progress *= 0.9;
        w.Emit(Severity.Good, "Delegates are recalled and replaced by rotation. Informal power is broken up — and experience is lost with it.");
        return ActionResult.Done("Delegates recalled.");
    }

    private static ActionResult WarCommittee(World w, SetWarCommittee a)
    {
        if (w.WarCommittee == a.Active) return ActionResult.Fail(a.Active ? "The war committee is already sitting." : "There is no war committee.");
        w.WarCommittee = a.Active;
        w.Emit(a.Active ? Severity.Warning : Severity.Good, a.Active
            ? $"A war committee takes charge of the militias. Faster decisions, stronger defense — and drift every {w.Turn} it sits."
            : "The war committee dissolves. Militias answer to their assemblies again.");
        return ActionResult.Done(a.Active ? "War committee formed." : "War committee dissolved.");
    }

    private static ActionResult ForeignTrade(World w, SetForeignTrade a)
    {
        if (w.ForeignTrade == a.Active) return ActionResult.Fail(a.Active ? "Trade is already open." : "There is no foreign trade to stop.");
        w.ForeignTrade = a.Active;
        w.Emit(a.Active ? Severity.Warning : Severity.Good, a.Active
            ? $"A trade bureau starts selling grain abroad for arms. Buyers want currency, and someone has to keep the accounts. ({w.Embargo:P0} of the world embargoes the federation.)"
            : "Foreign trade stops. Exchange runs on mutual aid again.");
        return ActionResult.Done(a.Active ? "Foreign trade opened." : "Foreign trade closed.");
    }

    private static ActionResult Ally(World w, AnswerAllyOffer a)
    {
        var ally = w.Ally;
        var b = w.Balance;
        if (!ally.OfferPending) return ActionResult.Fail("There is no offer on the table.");
        ally.OfferPending = false;
        ally.NextOfferTick = w.Tick + ally.OfferInterval;
        if (!a.Accept)
        {
            ally.Declined++;
            w.Emit(Severity.Info, $"You turn down the {ally.Name}'s terms. The militias stay under their own control.");
            return ActionResult.Done("Offer declined.");
        }
        ally.Accepted++;
        ally.MilitarizationTicks += b.MilitarizationTicks;
        var fronts = w.Federation.Where(c => c.Front).ToList();
        foreach (var c in fronts) c.Goods += ally.ArmsGoods / Math.Max(1, fronts.Count);
        w.BumpDrift(b.ArmsAcceptDrift);
        w.Emit(Severity.Warning, $"Arms arrive from the {ally.Name}. The columns become regular brigades with officers.");
        return ActionResult.Done("Arms accepted; militias militarized.");
    }

    private static ActionResult Hold(World w, HoldCommune a)
    {
        var b = w.Balance;
        var pending = w.PendingSecessions.FirstOrDefault(s => s.Commune == a.Commune);
        if (pending is null) return ActionResult.Fail("That commune is not trying to leave.");
        if (!Spend(w, b.CostForceStay)) return Broke(w, b.CostForceStay);
        var c = w.Communes[a.Commune];
        w.PendingSecessions.Remove(pending);
        foreach (var n in w.Trust.Neighbors(c.Id)) w.Trust.Add(c.Id, n, -b.ForceStayTrustLoss);
        c.Wellbeing = Math.Max(0, c.Wellbeing - 0.1);
        w.BumpDrift(b.ForceStayDrift);
        w.Emit(Severity.Danger, $"{c.Name} is kept in the federation against its assembly's will. Everyone notices.", c.Id);
        return ActionResult.Done($"{c.Name} held by force.");
    }

    private static ActionResult Delegates(World w, SendDelegates a)
    {
        if (w.States.FirstOrDefault(s => s.Id == a.StateId) is not { } s) return ActionResult.Fail($"No state '{a.StateId}'.");
        var b = w.Balance;
        if (!Spend(w, b.CostDelegates)) return Broke(w, b.CostDelegates);
        // Fascist governments barely listen; everyone listens less the further they already lean.
        double resistance = s.Ideology == Global.Ideology.Fascist ? 0.3 : 1;
        s.Stance = Math.Clamp(s.Stance + b.DelegateEffect * resistance * (1 - 0.5 * Math.Max(0, s.Stance)), -1, 1);
        w.Emit(Severity.Info, $"Delegates of the movement tour {s.Name}: meetings, newspapers, unions. It is now {Global.StateText.Stance(s.Stance)} ({s.Stance:+0.00;-0.00}).");
        return ActionResult.Done($"Delegates sent to {s.Name}: stance {s.Stance:+0.00;-0.00}.");
    }

    private static ActionResult Agitate(World w, AgitateWorkers a)
    {
        if (w.States.FirstOrDefault(s => s.Id == a.StateId) is not { } s) return ActionResult.Fail($"No state '{a.StateId}'.");
        var b = w.Balance;
        if (!Spend(w, b.CostAgitate)) return Broke(w, b.CostAgitate);
        s.Radicalization = Math.Min(1, s.Radicalization + b.AgitateEffect * (1 - s.Radicalization));
        s.Stance = Math.Clamp(s.Stance - 0.04, -1, 1);
        return ActionResult.Done($"Agitators reach the workers of {s.Name}: {s.Radicalization:P0} radicalized. Its government notices.");
    }

    private static ActionResult Expose(World w, ExposeCells a)
    {
        if (Member(w, a.Commune) is not { } c) return NotMember(w, a.Commune);
        var b = w.Balance;
        if (!Spend(w, b.CostExpose)) return Broke(w, b.CostExpose);
        c.PartyInfluence *= 0.4;
        foreach (var n in w.Trust.Neighbors(c.Id)) w.Communes[n].PartyInfluence *= 0.75;
        foreach (var r in Global.RivalSystem.Rivals(w).ToList()) r.Stance = Math.Clamp(r.Stance - 0.05, -1, 1);
        w.Emit(Severity.Info, $"Assemblies in and around {c.Name} debate the party's methods in the open. Its cells lose ground; its patrons abroad are displeased.", c.Id);
        return ActionResult.Done($"Party cells exposed in {c.Name}.");
    }

    private static ActionResult Support(World w, SupportUprising a)
    {
        if (w.States.FirstOrDefault(s => s.Id == a.StateId) is not { } s) return ActionResult.Fail($"No state '{a.StateId}'.");
        if (s.Uprising == 0) return ActionResult.Fail($"There is no revolution under way in {s.Name}.");
        var b = w.Balance;
        if (s.UprisingSupport >= 2 * b.SupportUprisingEffect) return ActionResult.Fail("You are already sending all the support you can.");
        if (!Spend(w, b.CostSupportUprising)) return Broke(w, b.CostSupportUprising);
        s.UprisingSupport += b.SupportUprisingEffect;
        w.BumpDrift(1); // organizing abroad needs organizers
        w.Emit(Severity.Info, $"Arms and organizers cross into {s.Name} to support the rising (success now {Global.RevolutionSystem.SuccessChance(w, s):P0}).");
        return ActionResult.Done($"Support sent to the revolution in {s.Name}.");
    }

    private static ActionResult Appeal(World w)
    {
        var b = w.Balance;
        if (w.States.Count == 0) return ActionResult.Fail("There is no outside world on this map.");
        if (w.Tick - w.LastAppealTick < b.AppealCooldown)
            return ActionResult.Fail($"The last appeal was too recent (wait {w.Turns(b.AppealCooldown - (w.Tick - w.LastAppealTick))}).");
        if (!Spend(w, b.CostAppeal)) return Broke(w, b.CostAppeal);
        w.LastAppealTick = w.Tick;
        w.AppealTicks = b.AppealTicks;
        foreach (var s in w.States.Where(s => s.Stance > 0.2)) s.Stance = Math.Min(1, s.Stance + 0.05);
        w.Emit(Severity.Good, $"An appeal to the workers of the world: for {w.Turns(b.AppealTicks)}, twice as many volunteers.");
        return ActionResult.Done("International appeal made.");
    }

    private static Commune? Valid(World w, int id) =>
        id >= 0 && id < w.Communes.Count && !w.Communes[id].Occupied ? w.Communes[id] : null;

    private static Commune? Member(World w, int id) =>
        Valid(w, id) is { Federated: true } c ? c : null;

    private static ActionResult NotMember(World w, int id) =>
        ActionResult.Fail(id >= 0 && id < w.Communes.Count ? $"{w.Communes[id].Name} is not a free federated commune." : $"No commune #{id}.");

    private static bool Spend(World w, double cost)
    {
        if (w.Influence < cost) return false;
        w.Influence -= cost;
        return true;
    }

    private static ActionResult Broke(World w, double cost) =>
        ActionResult.Fail($"Not enough influence ({w.Influence:0} of {cost:0}).");
}
