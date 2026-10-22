using System.Text.Json;

namespace Politicom.Sim.Systems;

/// <summary>
/// Now and then something happens that nobody planned: a harvest, a fire, a stranger with
/// news, a quarrel over water. One fits the age, the regime and the communes; it arrives as a
/// decision like the historical ones. At most one every few weeks, and each kind once a game.
/// </summary>
internal static class RandomEventSystem
{
    public static void Run(World w)
    {
        var pool = w.Scenario.RandomEvents;
        var b = w.Balance;
        if (pool.Count == 0 || w.Tick < 2 || (w.Tick - w.LastRandomEvent) * w.Weeks < b.RandomEventGap) return;
        if (!w.Rng.Chance(b.RandomEventRate * w.Weeks)) return;
        var eligible = pool.Where(t => Fits(w, t)).ToList();
        if (eligible.Count == 0) return;
        var template = w.Rng.PickWeighted(eligible, eligible.Select(t => t.Weight).ToList());
        var commune = PickOne(w, Candidates(w, template.Needs));
        var neighbours = w.Trust.Neighbors(commune.Id).Select(n => w.Communes[n]).Where(c => c.InFederation).ToList();
        var neighbour = neighbours.Count > 0 ? PickOne(w, neighbours) : commune;
        var state = w.States.Where(s => !s.Sister).OrderByDescending(s => s.Weight).FirstOrDefault();
        string region = w.Scenario.HomeState is { } home && w.Scenario.States.Count > 0
            ? Global.StateText.Country(w.Scenario.Name.Replace("The ", "").Replace(" Commune", ""))
            : w.Scenario.Name;

        string Esc(string s) => JsonEncodedText.Encode(s).ToString();
        string json = JsonSerializer.Serialize(template.Event, Json.Options)
            .Replace("{commune}", Esc(commune.Name))
            .Replace("{neighbour}", Esc(neighbour.Name))
            .Replace("{enemy}", Esc(w.Enemies.Count > 0 ? w.Enemies[0].Name : "enemy"))
            .Replace("{region}", Esc(region))
            .Replace("{stateId}", Esc(state?.Id ?? ""))
            .Replace("{state}", Esc(state?.Name ?? "a foreign power"));
        var def = JsonSerializer.Deserialize<EventDefinition>(json, Json.Options)!;
        def.Id = $"random_{template.Id}_{w.Tick}";
        def.Tick = w.Tick + 1;
        // Blows to the enemy are in proportion to its strength (a template's -1 is a sixth of an army of 6), and modest:
        // random events colour a game, they shouldn't decide it.
        double scale = w.Enemies.Count > 0 ? w.Enemies[0].Strength / 6 : 0.5;
        foreach (var o in def.Options)
        {
            o.Effects.Stances.Remove(""); // no foreign state to warm or cool
            o.Effects.EnemyStrength = Math.Round(o.Effects.EnemyStrength * scale, 2);
        }
        w.Scenario.Events.Add(def);
        w.LastRandomEvent = w.Tick;
    }

    private static T PickOne<T>(World w, List<T> items) => items[w.Rng.Next(items.Count)];

    private static bool Fits(World w, RandomEventTemplate t)
    {
        if (w.Year < t.Eras[0] || w.Year > t.Eras[1]) return false;
        if (t.Regimes.Length > 0 && !t.Regimes.Contains(w.Scenario.HomeIdeology ?? "", StringComparer.OrdinalIgnoreCase)) return false;
        if (w.FiredEvents.Any(id => id.StartsWith($"random_{t.Id}_")) || w.Scenario.Events.Any(e => e.Id.StartsWith($"random_{t.Id}_"))) return false;
        if (t.Needs == "world" && w.States.Count == 0) return false;
        return Candidates(w, t.Needs).Count > 0;
    }

    private static List<Commune> Candidates(World w, string needs) => w.Federation.Where(c => needs switch
    {
        "agrarian" => c.Kind == CommuneKind.Agrarian,
        "industrial" => c.Kind == CommuneKind.Industrial,
        "front" => c.Front,
        "rear" => !c.Front,
        _ => true,
    }).ToList();
}

/// <summary>
/// A regional rising spreads: the country's other cities watch the federation, and rise to
/// join it — likelier near it, when the enemy's own workers are radicalized, and where the
/// movement's culture runs deep; less likely as the federation hardens into a state.
/// </summary>
internal static class RisingSystem
{
    public static void Run(World w)
    {
        var reserve = w.Scenario.Reserve;
        if (reserve.Count == 0) return;
        var b = w.Balance;
        var members = w.Federation.ToList();
        if (members.Count == 0) return;
        double spirit = (1 + 3 * w.Radicalization) * (1 + CultureSystem.Mean(w)) * Math.Max(0, 1 - w.Drift.Value / 150);
        foreach (var d in reserve)
        {
            if (w.Risen.Contains(d.Name)) continue;
            double km = members.Min(c => Global.WorldSetup.DistanceKm(c.Longitude, c.Latitude, d.Lon, d.Lat));
            double p = b.RisingRate * w.Weeks * spirit * Math.Exp(-km / b.RisingReachKm);
            if (!w.Rng.Chance(p)) continue;
            w.Risen.Add(d.Name);
            w.AddCommunes(new List<CommuneDefinition> { d });
            var c = w.Communes[^1];
            c.Culture = b.StartCulture * c.PoliticalEducation;
            w.Emit(Severity.Good, $"{d.Name} rises! Its people take the town, drive out the officials and vote to join the federation.",
                c.Id, pause: true, title: $"{d.Name} rises");
            return; // one a turn
        }
    }
}
