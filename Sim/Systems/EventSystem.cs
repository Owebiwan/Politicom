namespace Politicom.Sim.Systems;

/// <summary>Fires scripted historical events and applies the chosen (or default) option.</summary>
internal static class EventSystem
{
    public static void Run(World w)
    {
        foreach (var pending in w.PendingEvents.ToList())
        {
            if (w.Tick <= pending.Expires) continue;
            var def = w.EventDefinition(pending.Id);
            Resolve(w, def, def.DefaultOption, answered: false);
        }

        foreach (var def in w.Scenario.Events)
        {
            if (def.Tick != w.Tick || w.FiredEvents.Contains(def.Id)) continue;
            if (!def.RequiresFederated.All(name => w.Communes.Any(c => c.Name == name && c.InFederation))) continue;
            w.FiredEvents.Add(def.Id);
            w.PendingEvents.Add(new PendingEvent(def.Id, w.Tick + def.ExpiresAfter));
            w.Emit(Severity.Decision, $"{def.Title}{(".?!".Contains(def.Title[^1]) ? "" : ".")} {def.Text}", title: def.Title);
        }
    }

    public static ActionResult Choose(World w, string eventId, int option)
    {
        var pending = w.PendingEvents.FirstOrDefault(p => p.Id == eventId);
        if (pending is null) return ActionResult.Fail($"No pending event '{eventId}'.");
        var def = w.EventDefinition(eventId);
        if (option < 0 || option >= def.Options.Count) return ActionResult.Fail($"'{def.Title}' has options 1–{def.Options.Count}.");
        Resolve(w, def, option, answered: true);
        return ActionResult.Done($"{def.Title}: {def.Options[option].Label}.");
    }

    private static void Resolve(World w, EventDefinition def, int option, bool answered)
    {
        w.PendingEvents.RemoveAll(p => p.Id == def.Id);
        var chosen = def.Options[option];
        Apply(w, chosen.Effects);
        w.Emit(Severity.Info, (answered ? $"{def.Title}: {chosen.Label}." : $"{def.Title}: with no answer from the movement, events take their course — {chosen.Label}.")
                              + (chosen.Description.Length > 0 ? $" {chosen.Description}" : ""));
    }

    internal static void Apply(World w, EventEffects e)
    {
        var b = w.Balance;
        var targets = e.Communes.Length == 0
            ? w.Federation.ToList()
            : e.Communes.Select(name => w.Communes.FirstOrDefault(c => c.Name == name)
                                        ?? throw new InvalidDataException($"Event names unknown commune '{name}'."))
                .Where(c => !c.Occupied).ToList();
        var ids = targets.Select(c => c.Id).ToHashSet();

        if (e.Drift != 0) w.BumpDrift(e.Drift);
        w.Influence = Math.Clamp(w.Influence + e.Influence, 0, b.InfluenceCap);
        foreach (var c in targets)
        {
            if (e.Food != 0) c.Food = Math.Max(0, c.Food + e.Food * c.Population);
            if (e.Population != 0) c.Population = Math.Max(1, c.Population * (1 + e.Population));
            if (e.Culture != 0) c.Culture = Math.Clamp(c.Culture + e.Culture * (e.Culture > 0 ? 1 - c.Culture : 1), 0, 1);
            if (e.Tech.Length > 0 && w.Techs.Contains(e.Tech) && w.Reached(w.Techs[e.Tech])) c.Techs.Add(e.Tech);
            c.Wellbeing = Math.Clamp(c.Wellbeing + e.Wellbeing, 0, 1);
            if (e.PoliticalEducation != 0) c.PoliticalEducation = Math.Clamp(c.PoliticalEducation + e.PoliticalEducation * (1 - c.PoliticalEducation), 0, 1);
        }
        if (e.Trust != 0)
            foreach (var (a, c) in w.Trust.Edges())
                if (ids.Contains(a) && ids.Contains(c)) w.Trust.Add(a, c, e.Trust);
        foreach (var enemy in w.Enemies.Where(x => e.Enemy.Length == 0 || x.Name == e.Enemy))
            enemy.Strength = Math.Max(0, enemy.Strength + e.EnemyStrength);

        if (e.GoodsToFront > 0)
        {
            var fronts = w.Federation.Where(c => c.Front).ToList();
            foreach (var c in fronts) c.Goods += e.GoodsToFront / fronts.Count;
        }
        w.Scenario.FoodImports = Math.Max(0, w.Scenario.FoodImports + e.FoodImports);
        w.Ally.MilitarizationTicks += e.MilitarizationTicks;
        if (e.EndAllyOffers)
        {
            w.Ally.OfferInterval = 0;
            w.Ally.OfferPending = false;
        }
        if (e.PartyInfluence != 0)
            foreach (var c in w.Federation) c.PartyInfluence = Math.Clamp(c.PartyInfluence + e.PartyInfluence * (1 - c.PartyInfluence), 0, 1);
        if (e.Federate.Length > 0) Global.RevolutionSystem.Federate(w, e.Federate);
        foreach (var (id, delta) in e.Stances)
            if (w.States.FirstOrDefault(s => s.Id == id) is { } state)
                state.Stance = Math.Clamp(state.Stance + delta, -1, 1);
        foreach (var name in e.Secede)
        {
            var c = w.Communes.FirstOrDefault(x => x.Name == name);
            if (c is { InFederation: true })
            {
                c.Federated = false;
                w.PendingSecessions.RemoveAll(s => s.Commune == c.Id);
                foreach (var p in w.ActiveProjects) p.Participants.Remove(c.Id);
                w.Emit(Severity.Warning, $"{c.Name} leaves the federation.", c.Id);
            }
        }
    }
}
