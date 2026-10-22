namespace Politicom.Sim.Global;

/// <summary>
/// How the outside world reacts to the federation. Each state's stance drifts toward where its
/// interests put it, and crossing thresholds (with a little hysteresis) starts or stops actions:
/// embargo, arms sales, volunteers, and intervention on the enemy's side.
/// </summary>
internal static class DiplomacySystem
{
    public static void Run(World w)
    {
        if (w.States.Count == 0) return;
        var b = w.Balance;
        double order = w.Drift.Value / 100;

        _newEmbargoes.Clear();
        foreach (var s in w.States)
        {
            double target = w.Baseline(s.Id);
            switch (s.Ideology)
            {
                // A federation that looks orderly reassures governments that fear chaos...
                case Ideology.Liberal or Ideology.Conservative or Ideology.Monarchy or Ideology.Empire or Ideology.Theocracy:
                    target += 0.4 * order;
                    if (w.ForeignTrade && !s.Does(StateActions.Embargo)) target += 0.1;
                    break;
                // ...an authoritarian revolutionary state likes it for the same reason...
                case Ideology.Communist:
                    target += 0.3 * order;
                    break;
                // ...and sympathizers abroad feel betrayed.
                case Ideology.Libertarian:
                    target -= 0.6 * order;
                    break;
            }
            // Governments whose own workers are radicalizing fear the example.
            if (s.Ideology is not (Ideology.Communist or Ideology.Libertarian or Ideology.Tribal)) target -= 0.5 * s.Radicalization;

            s.Stance = Math.Clamp(s.Stance + (target - s.Stance) * b.StanceDrift, -1, 1);
            Decide(w, s);

            // Contagion reaches abroad too, weaker with distance.
            double voice = w.Mean(c => c.PoliticalEducation) * (1 + b.CultureContagion * Systems.CultureSystem.Mean(w))
                           * (1 - b.RivalCultureResistance * s.CulturalWeight) * w.CommunicationSpeed;
            int sisters = w.States.Count(o => o.Sister && s.Neighbours.Contains(o.Id));
            s.Radicalization = Math.Clamp(s.Radicalization
                + b.ContagionRate * (voice * (1 + w.SocietyEffect(t => t.Contagion)) * s.Proximity * 0.5 + b.SisterContagion * sisters) * (1 - s.Repression)
                - b.RepressionRate * s.Radicalization, 0, 1);
        }

        // Embargoes often start together (a whole bloc at once): one line in the log, not twenty.
        if (_newEmbargoes.Count > 0)
            w.Emit(Severity.Info, _newEmbargoes.Count == 1 ? $"{_newEmbargoes[0]} embargoes the federation."
                : $"{string.Join(", ", _newEmbargoes.Take(_newEmbargoes.Count - 1))} and {_newEmbargoes[^1]} embargo the federation.");

        // Interventions strengthen the enemy they sponsor (or the main enemy), as a share of that
        // enemy's own strength: a foreign legion adds to an army, it doesn't replace one.
        var support = new Dictionary<HostileState, double>();
        foreach (var s in w.States.Where(s => s.Does(StateActions.Intervention)))
        {
            var enemy = w.Enemies.FirstOrDefault(e => e.Sponsors.Contains(s.Id)) ?? w.Enemies.FirstOrDefault();
            if (enemy != null) support[enemy] = support.GetValueOrDefault(enemy) + s.Weight * -s.Stance * InterstateSystem.Available(w, s);
        }
        foreach (var e in w.Enemies)
            e.Intervention = e.Strength * Math.Min(b.InterventionCap, b.InterventionPerWeight * support.GetValueOrDefault(e));

        // Arms bought from friendly states go to the front.
        double arms = w.States.Where(s => s.Does(StateActions.ArmsSales)).Sum(s => b.ArmsPerWeight * s.Weight * s.Stance);
        var fronts = w.Federation.Where(c => c.Front).ToList();
        if (arms > 0 && fronts.Count > 0)
            foreach (var c in fronts) c.Goods += arms / fronts.Count;
        w.LastArms = arms;

        // Volunteers come from friendly governments, and from radicalized workers anywhere, like the
        // International Brigades. A hostile government stops most of them at the border.
        double volunteers = w.States.Where(s => s.Does(StateActions.Volunteers)).Sum(s => s.Weight * s.Stance * InterstateSystem.Available(w, s))
                            + w.States.Sum(s => b.WorkerVolunteers * s.Power * s.Radicalization * (s.Stance < -0.6 ? 0.3 : 1));
        w.VolunteerDefense = Math.Min(b.VolunteerCap, b.VolunteerPerWeight * volunteers * (w.AppealTicks > 0 ? 2 : 1));
        if (w.AppealTicks > 0) w.AppealTicks--;
    }

    /// <summary>Thresholds with hysteresis, so a state doesn't flip every turn.</summary>
    private static void Decide(World w, WorldState s)
    {
        // Any government can intervene against a revolution (the Entente did in Russia), except revolutionary ones.
        // Stateless peoples neither blockade nor send armies.
        bool right = s.Ideology is not (Ideology.Communist or Ideology.Libertarian or Ideology.Tribal);
        bool canEmbargo = s.Ideology is not (Ideology.Communist or Ideology.Libertarian or Ideology.Tribal);
        Toggle(w, s, StateActions.Embargo, canEmbargo && s.Stance < -0.3, s.Stance > -0.2,
            $"{s.Name} embargoes the federation.", $"{s.Name} lifts its embargo.");
        Toggle(w, s, StateActions.ArmsSales, !s.Does(StateActions.Embargo) && s.Stance > 0.35, s.Stance < 0.25,
            $"{s.Name} starts selling arms to the federation.", $"{s.Name} stops selling arms.");
        Toggle(w, s, StateActions.Volunteers, s.Stance > 0.5, s.Stance < 0.4,
            $"Volunteers from {s.Name} arrive to fight alongside the militias.", $"Volunteers from {s.Name} stop coming.");
        // Troops cross oceans only for an enemy they already back; otherwise only states near enough intervene.
        bool reach = s.Proximity >= 0.25 || w.Enemies.Any(e => e.Sponsors.Contains(s.Id));
        Toggle(w, s, StateActions.Intervention, right && reach && s.Weight >= 0.8 && s.Stance < -0.6, s.Stance > -0.5 || !reach,
            $"{s.Name} intervenes: its troops and aircraft join the {Sponsored(w, s)}.", $"{s.Name} withdraws its forces.");
    }

    private static string Sponsored(World w, WorldState s) =>
        (w.Enemies.FirstOrDefault(e => e.Sponsors.Contains(s.Id)) ?? w.Enemies.FirstOrDefault())?.Name ?? "enemy";

    private static void Toggle(World w, WorldState s, StateActions action, bool on, bool off, string started, string stopped)
    {
        bool doing = s.Does(action);
        if (!doing && on)
        {
            s.Actions |= action;
            if (!Newsworthy(s, action)) { }
            else if (action == StateActions.Embargo) _newEmbargoes.Add(s.Name);
            else w.Emit(action == StateActions.Intervention ? Severity.Danger : Severity.Info, started);
        }
        else if (doing && off)
        {
            s.Actions &= ~action;
            if (Newsworthy(s, action)) w.Emit(Severity.Info, stopped);
        }
    }

    [ThreadStatic] private static List<string>? _embargoBuffer;
    private static List<string> _newEmbargoes => _embargoBuffer ??= new List<string>();

    /// <summary>Whether the log should mention it: far-off states' embargoes matter little and would flood it.</summary>
    private static bool Newsworthy(WorldState s, StateActions action) =>
        s.Weight >= 0.4 && (action != StateActions.Embargo || s.Proximity >= 0.3);

    /// <summary>The world's embargo: the share of outside weight refusing to trade.</summary>
    public static double Embargo(World w)
    {
        var traders = w.States.Where(s => s.Ideology != Ideology.Libertarian).ToList();
        double total = traders.Sum(s => s.Weight);
        return total <= 0 ? 0 : traders.Where(s => s.Does(StateActions.Embargo)).Sum(s => s.Weight) / total;
    }
}
