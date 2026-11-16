namespace Politicom.Sim;

/// <summary>A commune worth acting on now: what's wrong, what would help, and what that costs.</summary>
public sealed record Concern(int Commune, int Urgency, string Problem, string Remedy, PlayerAction? Action, double Cost)
{
    /// <summary>The remedy in a word or two, for a button; Remedy has the details.</summary>
    public string Short => Action switch
    {
        Mediate => "Mediate",
        HoldCommune => "Hold by force",
        Fortify => "Fortify",
        ChooseEventOption => "Back the campaign",
        ProposeProject { Kind: ProjectKind.DefensiveCampaign } => "Propose a campaign",
        ProposeProject { Kind: ProjectKind.FamineRelief } => "Famine relief",
        ExposeCells => "Expose cells",
        FundSchool => "Political school",
        AddressFaction => "Address them",
        _ => Remedy,
    };
}

/// <summary>
/// The few communes that most need the movement this turn, with the action that would help each,
/// so a large federation can be played without searching it town by town. Ordered by urgency:
/// a commune about to leave, a front giving way, hunger, a party taking root, the expert trap,
/// thin political education. One concern per commune (its most urgent).
/// </summary>
public static class Attention
{
    public static List<Concern> List(World w, int max = 6)
    {
        var b = w.Balance;
        bool Running(ProjectKind k, int? target = null) =>
            w.ActiveProjects.Any(p => p.Kind == k && (target is null || p.Target == target));
        var found = new List<Concern>();

        foreach (var c in w.Federation)
        {
            var neighbours = w.Trust.Neighbors(c.Id).Where(n => w.Communes[n].InFederation).ToList();
            int? coldest = neighbours.Count == 0 ? null : neighbours.MinBy(n => w.Trust[c.Id, n]);
            var leaving = w.PendingSecessions.FirstOrDefault(s => s.Commune == c.Id);

            if (leaving != null)
            {
                int left = Math.Max(0, leaving.Deadline - w.Tick);
                found.Add(coldest is int n
                    ? new(c.Id, 100, $"votes to leave the federation in {w.Turns(left)}",
                        $"Mediate with {w.Communes[n].Name} (trust {w.Trust[c.Id, n]:P0})", new Mediate(c.Id, n), b.CostMediate)
                    : new(c.Id, 100, $"votes to leave the federation in {w.Turns(left)}", "Hold it by force (drift)", new HoldCommune(c.Id), b.CostForceStay));
            }
            else if (c.Front && c.Pressure > c.Defense)
            {
                int urgency = 80 + (int)(20 * (1 - c.Control));
                string problem = $"losing ground at the front (control {c.Control:P0}, attack {c.Pressure / Math.Max(1e-6, c.Defense):0.0}× its defence)";
                if (c.Fortifying == 0 && c.Fortification < 0.95 && c.Goods >= c.Population * b.FortifyGoods)
                    found.Add(new(c.Id, urgency, problem, $"Fortify (works {c.Fortification:P0})", new Fortify(c.Id), b.CostFortify));
                else if (w.PendingEvents.FirstOrDefault(p => p.Id.StartsWith($"proposal_{ProjectKind.DefensiveCampaign}_")) is { } asked)
                    found.Add(new(c.Id, urgency, problem, $"Back {w.EventDefinition(asked.Id).Title.Split(" proposes")[0]}'s defensive campaign",
                        new ChooseEventOption(asked.Id, 0), b.CostBackProposal));
                else if (!Running(ProjectKind.DefensiveCampaign) && w.ActiveProjects.Count() < b.MaxActiveProjects)
                    found.Add(new(c.Id, urgency, problem, "Propose a defensive campaign", new ProposeProject(ProjectKind.DefensiveCampaign), b.CostPropose));
                else
                    found.Add(new(c.Id, urgency - 10, problem, c.Fortifying > 0 ? "Digging in already; hold on" : "A campaign is under way; hold on", null, 0));
            }
            else if (c.Starving && !Running(ProjectKind.FamineRelief, c.Id) && w.ActiveProjects.Count() < b.MaxActiveProjects)
                found.Add(new(c.Id, 75, "is going hungry", "Propose famine relief", new ProposeProject(ProjectKind.FamineRelief, c.Id), b.CostPropose));
            else if (c.Discontent > 0.5 && coldest is int cold)
                found.Add(new(c.Id, 60, $"is discontented ({c.Discontent:0.00}; above 0.6 it may vote to leave)",
                    $"Mediate with {w.Communes[cold].Name} (trust {w.Trust[c.Id, cold]:P0})", new Mediate(c.Id, cold), b.CostMediate));
            else if (c.PartyInfluence > 0.4)
                found.Add(new(c.Id, 50, $"party cells hold {c.PartyInfluence:P0} of its committees", "Expose the cells", new ExposeCells(c.Id), b.CostExpose));
            else if (c.PracticalEducation > c.PoliticalEducation + 0.1 && c.PoliticalSchooling < b.MaxSchooling)
                found.Add(new(c.Id, 40, $"experts are taking charge (practical {c.PracticalEducation:P0}, political {c.PoliticalEducation:P0})",
                    "Fund a political school", new FundSchool(c.Id, EducationTrack.Political), b.CostSchool));
            else if (c.PoliticalEducation < 0.15 && c.PoliticalSchooling < b.MaxSchooling)
                found.Add(new(c.Id, 30, $"political education is thin ({c.PoliticalEducation:P0})",
                    "Fund a political school", new FundSchool(c.Id, EducationTrack.Political), b.CostSchool));
        }

        // A faction close to making its demand: shown on the commune at its heart.
        foreach (var f in w.Factions.Where(f => f.Strength >= 0.6 && f.Members.Count > 0))
        {
            int heart = f.Leader is int l && w.Figures[l].Commune is int lc ? lc : f.Members.OrderByDescending(id => w.Communes[id].Population).First();
            found.RemoveAll(x => x.Commune == heart);
            found.Add(new(heart, 70 + (int)(20 * f.Strength), $"{Faction.Name(f.Kind).ToLowerInvariant()} ({f.Members.Count} communes, strength {f.Strength:P0}) will soon demand {Faction.Demand(f.Kind)}",
                $"Address {Faction.Name(f.Kind).ToLowerInvariant()}", new AddressFaction(f.Kind), b.CostAddressFaction));
        }

        found = found.OrderByDescending(x => x.Urgency).ThenBy(x => w.Communes[x.Commune].Name).Take(max).ToList();
        // A defensive campaign helps the whole front: offer it once (on the worst town), not once per town.
        bool offered = false;
        for (int i = 0; i < found.Count; i++)
            if (found[i].Action is ProposeProject { Kind: ProjectKind.DefensiveCampaign } or ChooseEventOption)
            {
                if (offered) found[i] = found[i] with { Remedy = "The defensive campaign above would help here too", Action = null, Cost = 0 };
                offered = true;
            }
        return found;
    }
}
