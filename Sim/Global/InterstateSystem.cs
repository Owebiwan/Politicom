namespace Politicom.Sim.Global;

/// <summary>A war between states, scripted from history or broken out in play.</summary>
public sealed class War
{
    public string Name { get; set; } = "";
    public List<string> SideA { get; set; } = new();
    public List<string> SideB { get; set; } = new();
    public int StartTick { get; set; }
    /// <summary>Peace isn't possible before this turn (scripted wars last at least their history).</summary>
    public int EarliestPeace { get; set; }
    public bool Over { get; set; }

    public bool Involves(string id) => SideA.Contains(id) || SideB.Contains(id);
    public List<string> Enemies(string id) => SideA.Contains(id) ? SideB : SideA.Contains(id) || !SideB.Contains(id) ? new() : SideA;
}

/// <summary>A war the scenario starts on a given turn.</summary>
public sealed class ScriptedWar
{
    public string Name { get; set; } = "";
    public int Tick { get; set; }
    /// <summary>Turns before peace becomes possible.</summary>
    public int Duration { get; set; } = 52;
    public string[] SideA { get; set; } = Array.Empty<string>();
    public string[] SideB { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Wars between states. Borders don't change; wars wear states down. A state at war has fewer
/// troops to spare for intervening against the federation, and war-weary workers radicalize,
/// so revolutions follow wars.
/// </summary>
public static class InterstateSystem
{
    /// <summary>How hostile two regimes are to each other, 0..1.</summary>
    public static double Hostility(Ideology a, Ideology b)
    {
        if (a == b) return a switch { Ideology.Fascist => 0.2, Ideology.Empire => 0.4, Ideology.Theocracy => 0.3, Ideology.Tribal => 0.15, _ => 0 };
        if (a > b) (a, b) = (b, a);
        return (a, b) switch
        {
            (Ideology.Fascist, Ideology.Communist) => 1,
            (Ideology.Liberal, Ideology.Fascist) => 0.6,
            (Ideology.Fascist, Ideology.Libertarian) => 0.9,
            (Ideology.Communist, Ideology.Libertarian) => 0.5,
            (_, Ideology.Communist) => 0.5,
            (_, Ideology.Libertarian) => 0.6,
            (Ideology.Conservative, Ideology.Fascist) => 0.3,
            (Ideology.Fascist, Ideology.Monarchy) => 0.3,
            // The earlier ages: empires conquer, faiths crusade, free peoples are raided.
            (Ideology.Libertarian, Ideology.Empire) => 0.6,
            (Ideology.Libertarian, Ideology.Theocracy) => 0.8,
            (Ideology.Libertarian, Ideology.Tribal) => 0.05,
            (Ideology.Communist, Ideology.Empire or Ideology.Theocracy) => 0.5,
            (Ideology.Monarchy, Ideology.Empire) => 0.4,
            (Ideology.Empire, Ideology.Tribal) => 0.5,
            (Ideology.Empire, Ideology.Theocracy) => 0.35,
            (Ideology.Monarchy, Ideology.Tribal) => 0.3,
            (Ideology.Tribal, Ideology.Theocracy) => 0.4,
            (Ideology.Monarchy, Ideology.Theocracy) => 0.25,
            _ => 0.15,
        };
    }

    internal static void Run(World w)
    {
        if (w.States.Count == 0) return;
        var b = w.Balance;
        var byId = w.States.ToDictionary(s => s.Id);

        // Scripted wars start on their turn, if both sides still exist as such.
        foreach (var sw in w.Scenario.Wars.Where(x => x.Tick == w.Tick))
        {
            var a = sw.SideA.Where(byId.ContainsKey).ToList();
            var c = sw.SideB.Where(byId.ContainsKey).ToList();
            if (a.Count > 0 && c.Count > 0) Start(w, sw.Name, a, c, w.Tick + sw.Duration);
        }

        // Wars break out between hostile neighbours, now and then.
        foreach (var s in w.States)
            foreach (var nid in s.Neighbours)
            {
                if (string.CompareOrdinal(s.Id, nid) >= 0 || !byId.TryGetValue(nid, out var n)) continue;
                if (w.Wars.Any(x => !x.Over && x.Involves(s.Id) && x.Involves(nid))) continue;
                double chance = b.WarChance * Hostility(s.Ideology, n.Ideology) * (1 + Math.Abs(s.Power - n.Power) / 5);
                if (!w.Rng.Chance(chance)) continue;
                var (attacker, defender) = s.Power >= n.Power ? (s, n) : (n, s);
                if (attacker.Ideology == Ideology.Libertarian) continue; // free federations defend; they don't invade
                var sideA = new List<string> { attacker.Id };
                var sideB = new List<string> { defender.Id };
                // Allies: states close in ideology to one side and hostile to the other may join.
                foreach (var o in w.States.Where(o => o != s && o != n && o.Power >= 3 && o.Ideology != Ideology.Libertarian))
                {
                    double forB = Hostility(o.Ideology, attacker.Ideology) - Hostility(o.Ideology, defender.Ideology);
                    if (forB > 0.4 && w.Rng.Chance(0.5)) sideB.Add(o.Id);
                    else if (forB < -0.4 && w.Rng.Chance(0.5)) sideA.Add(o.Id);
                }
                Start(w, $"The {StateText.Country(attacker.Name)}–{StateText.Country(defender.Name)} war", sideA, sideB, w.Tick + 26);
            }

        // Wear, and peace.
        foreach (var s in w.States)
        {
            var fighting = w.Wars.Where(x => !x.Over && x.Involves(s.Id)).ToList();
            if (fighting.Count == 0)
            {
                s.Weariness = Math.Max(0, s.Weariness - 0.01);
                continue;
            }
            double foes = fighting.SelectMany(x => x.Enemies(s.Id)).Distinct().Where(byId.ContainsKey).Sum(id => byId[id].Power);
            s.Weariness = Math.Min(1, s.Weariness + b.WearinessRate * (0.5 + foes / Math.Max(1, s.Power)) / 2);
            // War-weary workers radicalize: revolutions follow wars.
            s.Radicalization = Math.Min(1, s.Radicalization + b.WarRadicalization * s.Weariness);
        }
        foreach (var war in w.Wars.Where(x => !x.Over && w.Tick >= x.EarliestPeace))
        {
            double weary = war.SideA.Concat(war.SideB).Where(byId.ContainsKey).Select(id => byId[id].Weariness).DefaultIfEmpty(0).Max();
            if (w.Rng.Chance(b.PeaceChance * weary))
            {
                war.Over = true;
                w.Emit(Severity.Info, $"{war.Name} ends in an exhausted peace.");
            }
        }
    }

    private static void Start(World w, string name, List<string> a, List<string> b, int earliestPeace)
    {
        w.Wars.Add(new War { Name = name, SideA = a, SideB = b, StartTick = w.Tick, EarliestPeace = earliestPeace });
        string Names(List<string> ids) => string.Join(", ", ids.Select(id => w.States.First(s => s.Id == id).Name));
        bool near = a.Concat(b).Any(id => w.States.First(s => s.Id == id).Proximity >= 0.4);
        w.Emit(near ? Severity.Warning : Severity.Info, $"War: {Names(a)} against {Names(b)}.");
    }

    /// <summary>Share of a state's strength it can spare abroad: less while at war.</summary>
    public static double Available(World w, WorldState s) =>
        w.Wars.Any(x => !x.Over && x.Involves(s.Id)) ? 1 - w.Balance.WarCommitment : 1;
}
