namespace Politicom.Sim.Systems;

/// <summary>
/// Communes put forward their own plans: famine relief when they're starving, defence works at
/// a front that's losing, an athenaeum where political education is thin, a railway between
/// industrial towns, collectivizing the works once the war allows it. The movement can back a
/// plan (influence; the project starts with them as its heart) or let it lapse. Nobody is
/// ordered to do anything: the commune asked, and the others are invited.
/// </summary>
internal static class ProposalSystem
{
    public static void Run(World w)
    {
        var b = w.Balance;
        if (w.Tick < 3 || (w.Tick - w.LastProposal) * w.Weeks < b.ProposalGap || !w.Rng.Chance(b.ProposalRate * w.Weeks)) return;
        if (w.PendingEvents.Any(p => p.Id.StartsWith("proposal_")) || w.ActiveProjects.Count() >= b.MaxActiveProjects) return;
        var plan = Choose(w);
        if (plan is null) return;
        var (commune, kind, other, why) = plan.Value;
        var def = ProjectCatalog.Get(kind);
        string target = commune.Name;
        string title = $"{commune.Name} proposes {(kind == ProjectKind.Railway ? "a railway" : Article(def.Name))}";
        w.Scenario.Events.Add(new EventDefinition
        {
            Id = $"proposal_{kind}_{commune.Id}_{w.Tick}",
            Tick = w.Tick + 1,
            Title = title,
            Text = $"{why} The assembly of {commune.Name} proposes {Article(def.Name)}{(other is null ? "" : $" to {other.Name}")} and asks the movement to back it. {def.Description}",
            DefaultOption = 1,
            ExpiresAfter = 4,
            Options =
            {
                new EventOption
                {
                    Label = "Back the proposal",
                    Description = "Organizers and influence behind it; the communes it concerns are invited to join.",
                    Effects = new EventEffects
                    {
                        Influence = -b.CostBackProposal, StartProject = kind,
                        ProjectTarget = def.TargetsNeeded >= 1 ? target : "", ProjectTarget2 = other?.Name ?? "",
                        Trust = 0.02, Communes = other is null ? new[] { target } : new[] { target, other.Name },
                    },
                },
                new EventOption
                {
                    Label = "Not now",
                    Description = "The plan lapses. Its backers are disappointed.",
                    Effects = new EventEffects { Wellbeing = -0.02, Communes = new[] { target } },
                },
            },
        });
        w.LastProposal = w.Tick;
        w.LastProposalKind = kind;
    }

    private static string Article(string name) => ("aeiouAEIOU".Contains(name[0]) ? "an " : "a ") + name.ToLowerInvariant();

    private static (Commune Commune, ProjectKind Kind, Commune? Other, string Why)? Choose(World w)
    {
        bool Running(ProjectKind k, int? t = null) => w.ActiveProjects.Any(p => p.Kind == k && (t is null || p.Target == t));
        var members = w.Federation.ToList();
        if (members.FirstOrDefault(c => c.Starving && !Running(ProjectKind.FamineRelief, c.Id)) is { } hungry)
            return (hungry, ProjectKind.FamineRelief, null, $"{hungry.Name} is going hungry.");
        if (members.Where(c => c.Front && c.Pressure > c.Defense).OrderBy(c => c.Control).FirstOrDefault() is { } losing && !Running(ProjectKind.DefensiveCampaign))
            return (losing, ProjectKind.DefensiveCampaign, null, $"The front at {losing.Name} is giving way.");
        if (members.Where(c => c.PoliticalEducation < 0.15 && !Running(ProjectKind.AteneoNetwork, c.Id)).OrderBy(c => c.PoliticalEducation).FirstOrDefault() is { } thin
            && w.Rng.Chance(0.5))
            return (thin, ProjectKind.AteneoNetwork, null, $"Few in {thin.Name} have had time for reading or debate.");
        // Railways join towns worth the line: not two quarters of one city.
        if (w.LastProposalKind != ProjectKind.Railway)
        foreach (var c in members.Where(c => c.Kind == CommuneKind.Industrial).OrderByDescending(c => c.Population))
        {
            var partner = w.Trust.Neighbors(c.Id).Select(n => w.Communes[n])
                .Where(n => n.InFederation && !w.Rail(c.Id, n.Id) && !Running(ProjectKind.Railway, c.Id)
                            && Global.WorldSetup.DistanceKm(c.Longitude, c.Latitude, n.Longitude, n.Latitude) > 15)
                .OrderByDescending(n => n.Population).FirstOrDefault();
            if (partner != null && w.Rng.Chance(0.4))
                return (c, ProjectKind.Railway, partner, $"Carts between {c.Name} and {partner.Name} can't carry what the works need.");
        }
        if (!w.Federation.Any(c => c.Front && c.Pressure > c.Defense) && !Running(ProjectKind.Collectivization) && w.Rng.Chance(0.15)
            && members.Where(c => c.Kind == CommuneKind.Industrial).OrderByDescending(c => c.Population).FirstOrDefault() is { } works)
            return (works, ProjectKind.Collectivization, null, $"The works councils of {works.Name} want one plan for every collective.");
        return null;
    }
}
