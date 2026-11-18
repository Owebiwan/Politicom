namespace Politicom.Sim.Systems;

/// <summary>
/// The people: how many there are in each commune, and why that changes. Communes that eat well
/// and live well grow slowly, and shrink in misery; hunger kills (in the economy). The fighting
/// kills militia, more when they are outmatched, and townspeople when ground is lost. When a
/// town loses ground or falls, people flee to the free communes next to it, more to those they
/// trust: hands and fighters for their hosts, but more mouths too. And people drift, slowly,
/// toward neighbours that are better off. Runs after the war, so this turn's battles count.
/// </summary>
internal static class PopulationSystem
{
    public static void Run(World w)
    {
        var b = w.Balance;
        double weeks = w.Weeks;
        var change = w.Communes.ToDictionary(c => c.Id, _ => new Dictionary<string, double>());
        void Add(Commune c, string why, double amount)
        {
            if (amount == 0) return;
            change[c.Id][why] = change[c.Id].GetValueOrDefault(why) + amount;
            c.Population = Math.Max(0.05, c.Population + amount);
        }
        foreach (var c in w.Communes)
        {
            if (c.FamineDead > 0) change[c.Id]["Hunger"] = -c.FamineDead; // already taken off by the economy
            c.FamineDead = 0;
        }

        // Births over deaths.
        foreach (var c in w.Active)
            Add(c, "Births over deaths", c.Population * Growth(w, c) * weeks);

        // This turn's battles: the militia's dead, the townspeople killed, and those who flee.
        foreach (var r in w.Battles.Where(r => r.Tick == w.Tick))
        {
            var c = w.Communes[r.Commune];
            double odds = Math.Clamp(r.Attack / Math.Max(1e-6, r.Defense), 0.3, 2.5);
            double lost = Math.Max(0, r.ControlBefore - r.ControlAfter);
            // Outmatched, a line loses men fast (CombatLoss); every battle takes a few.
            r.Dead = r.Militia * (b.CombatLoss * Math.Clamp(odds - 1, 0, 1) + b.BattleDeathRate * Math.Min(1, odds)) * weeks;
            r.CiviliansDead = c.Population * b.CivilianDeaths * lost;
            double dead = r.Dead + r.CiviliansDead;
            c.WarDead += dead;
            Add(c, "Killed in the fighting", -dead);
            double fleeing = c.Population * (b.FleeOnLoss * lost + (r.Result == BattleResult.Fell ? b.FleeOnFall : 0));
            r.Fled = fleeing;
            if (fleeing > 0) Flee(w, c, fleeing, r.Result == BattleResult.Fell, Add);
        }

        // People moving to neighbours that are better off.
        Migrate(w, Add);

        foreach (var c in w.Communes)
        {
            c.Refugees = Math.Min(c.Population, c.Refugees * Math.Max(0, 1 - b.RefugeesSettle * weeks));
            c.PopulationChange = change[c.Id].Where(kv => Math.Abs(kv.Value) > 1e-6).Select(kv => new Part(kv.Key, kv.Value)).ToList();
        }
    }

    /// <summary>Weekly natural growth: up to PopulationGrowth at full wellbeing with food in store, below zero in misery.</summary>
    internal static double Growth(World w, Commune c)
    {
        if (c.Starving) return 0; // hunger is counted on its own
        double wellbeing = Math.Clamp((c.Wellbeing - 0.4) / 0.4, -1, 1);
        double fed = c.Food >= EconomySystem.Reserve(w.Balance, c, true) ? 1 : 0.3;
        return w.Balance.PopulationGrowth * (wellbeing > 0 ? wellbeing * fed : wellbeing);
    }

    private static void Flee(World w, Commune from, double fleeing, bool fell, Action<Commune, string, double> add)
    {
        var shelter = w.Trust.Neighbors(from.Id).Select(n => w.Communes[n]).Where(n => !n.Occupied).ToList();
        // Those who flee are the townspeople: the fighters stay, now a larger share of fewer people.
        double fighters = from.Population * from.Mobilization;
        add(from, "Fled the fighting", -fleeing);
        if (!from.Occupied) from.Mobilization = Math.Min(1, fighters / from.Population);
        if (shelter.Count == 0)
        {
            if (fell) w.Emit(Severity.Warning, $"{fleeing:0.0} thousand flee {from.Name}, with nowhere free to go: they scatter abroad.", from.Id);
            return;
        }
        double Weight(Commune n) => (0.3 + w.Trust[n.Id, from.Id]) * (n.Front ? 0.5 : 1) * (n.InFederation ? 1 : 0.4);
        double total = shelter.Sum(Weight);
        foreach (var n in shelter)
        {
            double share = fleeing * Weight(n) / total;
            add(n, $"Refugees from {from.Name}", share);
            n.Refugees += share;
        }
        var main = shelter.OrderByDescending(Weight).First();
        if (fell || fleeing > from.Population * 0.04)
            w.Emit(Severity.Warning,
                $"{fleeing:0.0} thousand flee {from.Name}{(fell ? " as it falls" : " as the front gives way")}. Most go to {main.Name}: more hands and fighters, and more mouths to feed.",
                main.Id, title: "Refugees");
    }

    /// <summary>Each commune's pull: wellbeing, food in store, safety from the front.</summary>
    private static double Pull(World w, Commune c) =>
        c.Wellbeing + (c.Starving ? -0.3 : 0.1 * Math.Min(1, c.Food / Math.Max(1e-6, EconomySystem.Reserve(w.Balance, c, true))))
        - (c.Front ? 0.15 : 0) - 0.2 * c.FreeRiding;

    private static void Migrate(World w, Action<Commune, string, double> add)
    {
        var b = w.Balance;
        var members = w.Federation.Where(c => !c.Occupied).ToList();
        var pull = members.ToDictionary(c => c.Id, c => Pull(w, c));
        var moves = new List<(Commune From, Commune To, double Amount)>();
        foreach (var c in members)
            foreach (int n in w.Trust.Neighbors(c.Id))
            {
                if (!pull.TryGetValue(n, out double there)) continue;
                double gap = there - pull[c.Id] - 0.05;
                if (gap <= 0) continue;
                double amount = c.Population * b.MigrationRate * gap * (0.5 + 0.5 * w.Trust[c.Id, n]) * w.Weeks;
                moves.Add((c, w.Communes[n], Math.Min(amount, c.Population * 0.01 * w.Weeks)));
            }
        foreach (var (from, to, amount) in moves)
        {
            add(from, "Moved to better-off neighbours", -amount);
            add(to, "Came from neighbours", amount);
        }
    }
}
