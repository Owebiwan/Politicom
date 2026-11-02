namespace Politicom.Sim.Systems;

/// <summary>
/// Goals for the long middle of a game (see <see cref="Goal"/>): every few months, when nothing
/// else is set, one that fits the moment is offered as a decision. A goal taken on is checked
/// each turn: met, it lifts enthusiasm and brings influence; past its deadline, it costs a little
/// enthusiasm. A goal not taken up simply lapses, so the game's balance doesn't depend on them.
/// </summary>
public static class GoalSystem
{
    internal static void Run(World w)
    {
        var b = w.Balance;
        foreach (var g in w.Goals.ToList())
        {
            if (!g.Active)
            {
                if (w.Tick > g.Offered + 6) w.Goals.Remove(g); // not taken up
                continue;
            }
            bool met = Met(w, g, out bool lost);
            if (met && (g.Kind != GoalKind.Hold || w.Tick >= g.Deadline))
            {
                w.Goals.Remove(g);
                w.Enthusiasm = Math.Min(1, w.Enthusiasm + b.GoalEnthusiasm);
                w.Influence = Math.Min(b.InfluenceCap, w.Influence + b.GoalInfluence);
                w.Emit(Severity.Good, $"Goal met: {g.Title}. The assemblies take heart (+{b.GoalEnthusiasm:P0} enthusiasm, +{b.GoalInfluence:0} influence).",
                    g.Commune >= 0 ? g.Commune : null, pause: true, title: "Goal met");
            }
            else if (lost || w.Tick >= g.Deadline)
            {
                w.Goals.Remove(g);
                w.Enthusiasm = Math.Max(0, w.Enthusiasm - b.GoalFailEnthusiasm);
                w.Emit(Severity.Warning, $"Goal missed: {g.Title}. ({-b.GoalFailEnthusiasm:P0} enthusiasm)",
                    g.Commune >= 0 ? g.Commune : null, title: "Goal missed");
            }
        }

        if (w.Goals.Count > 0 || w.Tick * w.Weeks < b.GoalFirstWeek || (w.Tick - w.LastGoal) * w.Weeks < b.GoalInterval) return;
        if (Pick(w) is not { } goal) return;
        w.LastGoal = w.Tick;
        w.Goals.Add(goal);
        w.Scenario.Events.Add(new EventDefinition
        {
            Id = goal.Id,
            Tick = w.Tick + 1,
            Title = $"A goal: {goal.Title}",
            Text = Text(w, goal),
            DefaultOption = 1,
            ExpiresAfter = 4,
            Options =
            {
                new EventOption
                {
                    Label = "Take it on",
                    Description = $"Met by {w.Format(w.DateAt(goal.Deadline), "d MMMM yyyy")}: +{b.GoalEnthusiasm:P0} enthusiasm, +{b.GoalInfluence:0} influence. Missed: {-b.GoalFailEnthusiasm:P0} enthusiasm.",
                    Effects = new EventEffects { StartGoal = goal.Id },
                },
                new EventOption { Label = "Not now", Description = "Nothing is promised, and nothing is lost." },
            },
        });
    }

    /// <summary>Whether a goal is met now; `lost` when it can no longer be (the town has fallen).</summary>
    public static bool Met(World w, Goal g, out bool lost)
    {
        lost = false;
        switch (g.Kind)
        {
            case GoalKind.Hold:
                var c = w.Communes[g.Commune];
                lost = c.Occupied || !c.InFederation;
                return !lost;
            case GoalKind.LowerDrift: return w.Drift.Value <= g.Target;
            case GoalKind.EndHunger: return !w.Federation.Any(x => x.Starving);
            default: return Progress(w, g) >= g.Target;
        }
    }

    /// <summary>Where the measured quantity stands now.</summary>
    public static double Progress(World w, Goal g) => g.Kind switch
    {
        GoalKind.LowerDrift => w.Drift.Value,
        GoalKind.Trust => MeanTrust(w),
        GoalKind.Education => w.Federation.Any() ? w.Federation.Average(c => c.PoliticalEducation) : 0,
        GoalKind.Radicalize => w.Radicalization,
        GoalKind.EndHunger => w.Federation.Count(c => c.Starving),
        _ => 0,
    };

    /// <summary>One line on a goal under way, for the dashboard.</summary>
    public static string Describe(World w, Goal g)
    {
        int left = Math.Max(0, g.Deadline - w.Tick);
        string when = $"{w.Turns(left)} left (by {w.Format(w.DateAt(g.Deadline), "d MMMM yyyy")})";
        string now = g.Kind switch
        {
            GoalKind.Hold => w.Communes[g.Commune].Occupied ? "fallen" : $"holding, control {w.Communes[g.Commune].Control:P0}",
            GoalKind.LowerDrift => $"drift {w.Drift.Value:0.0}, target {g.Target:0.0}",
            GoalKind.EndHunger => $"{Progress(w, g):0} communes going hungry",
            GoalKind.Trust => $"trust {Progress(w, g):P0}, target {g.Target:P0}",
            GoalKind.Education => $"political education {Progress(w, g):P0}, target {g.Target:P0}",
            GoalKind.Radicalize => $"enemy workers radicalized {Progress(w, g):P0}, target {g.Target:P0}",
            _ => "",
        };
        return $"{char.ToUpperInvariant(g.Title[0])}{g.Title[1..]}: {now} · {when}";
    }

    private static double MeanTrust(World w)
    {
        var edges = w.Trust.Edges().Where(e => w.Communes[e.A].InFederation && w.Communes[e.B].InFederation).ToList();
        return edges.Count == 0 ? 0 : edges.Average(e => w.Trust[e.A, e.B]);
    }

    private static int Ticks(World w, double weeks) => Math.Max(1, (int)Math.Ceiling(weeks / w.Weeks));

    /// <summary>A goal that fits the moment, or none.</summary>
    private static Goal? Pick(World w)
    {
        var options = new List<(Goal Goal, double Weight)>();
        Goal Make(GoalKind kind, string title, double weeks, double target = 0, double start = 0, int commune = -1) => new()
        {
            Id = $"goal_{kind}_{w.Tick}", Kind = kind, Title = title, Target = target, Start = start, Commune = commune,
            Offered = w.Tick, Deadline = w.Tick + 1 + Ticks(w, weeks),
        };

        if (w.Federation.Where(c => c.Front && c.Control < 0.95).OrderBy(c => c.Control).FirstOrDefault() is { } threatened)
            options.Add((Make(GoalKind.Hold, $"hold {threatened.Name}", 12, commune: threatened.Id), 3));
        if (w.Drift.Value >= 15)
        {
            double target = Math.Round(Math.Max(5, w.Drift.Value - 6), 1);
            options.Add((Make(GoalKind.LowerDrift, $"bring drift down to {target:0.0}", 16, target, w.Drift.Value), 2));
        }
        if (w.Federation.Any(c => c.Starving))
            options.Add((Make(GoalKind.EndHunger, "end the hunger", 8, start: w.Federation.Count(c => c.Starving)), 3));
        double trust = MeanTrust(w);
        if (trust < 0.85)
            options.Add((Make(GoalKind.Trust, $"raise trust to {trust + 0.05:P0}", 16, Math.Round(trust + 0.05, 3), trust), 1));
        double education = w.Federation.Any() ? w.Federation.Average(c => c.PoliticalEducation) : 1;
        if (education < 0.6)
            options.Add((Make(GoalKind.Education, $"raise political education to {education + 0.04:P0}", 20, Math.Round(education + 0.04, 3), education), 1));
        if (w.Enemies.Count > 0 && w.Radicalization < 0.6)
            options.Add((Make(GoalKind.Radicalize, $"radicalize the enemy's workers to {w.Radicalization + 0.08:P0}", 20,
                Math.Round(w.Radicalization + 0.08, 3), w.Radicalization), 1));
        if (options.Count == 0) return null;
        return w.Rng.PickWeighted(options.Select(o => o.Goal).ToList(), options.Select(o => o.Weight).ToList());
    }

    private static string Text(World w, Goal g)
    {
        string by = w.Format(w.DateAt(g.Deadline), "d MMMM yyyy");
        return g.Kind switch
        {
            GoalKind.Hold => $"{w.Communes[g.Commune].Name} is the weak point of the front. The assemblies ask the movement to make holding it a common cause: if it still stands on {by}, the whole federation will know it can be done.",
            GoalKind.LowerDrift => $"Committees are hardening into offices. Delegates propose a campaign of recall and rotation: bring hierarchy drift down to {g.Target:0.0} by {by}.",
            GoalKind.EndHunger => $"Some communes are going hungry. The assemblies propose a pledge: no commune hungry by {by}.",
            GoalKind.Trust => $"Old quarrels between communes are souring cooperation. Delegates propose a season of exchanges and joint work: raise trust to {g.Target:P0} by {by}.",
            GoalKind.Education => $"Too few have time to read, argue and decide. The ateneos propose a push: political education at {g.Target:P0} by {by}.",
            GoalKind.Radicalize => $"Behind the enemy's lines, workers are listening. The movement could make reaching them a goal: {g.Target:P0} radicalized by {by}.",
            _ => "",
        };
    }
}
