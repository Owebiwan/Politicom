using System.Text;
using Politicom.Sim;
using Politicom.Sim.Ai;

namespace Politicom.Cli;

/// <summary>One headless game with a scripted player, with an optional per-week CSV.</summary>
internal static class SingleRun
{
    public static int Run(Options o)
    {
        var policy = Policies.Get(o.Get("policy", "balanced"));
        var world = World.Load(DataPaths.Find(), o.Scenario, o.BuildBalance(), o.GetSeed());
        var series = new StringBuilder("tick,date,influence,drift,stage,federated,occupied,seceded,mean_trust,mean_wellbeing,mean_practical,mean_political,enemy_strength,radicalization,survival,quality,spread,freedom\n");

        int printed = 0;
        while (world.Outcome == Outcome.Ongoing)
        {
            policy.Act(world);
            world.Step();
            for (; printed < world.Log.Count; printed++)
                if (world.Log[printed].Severity != Severity.Info) Console.WriteLine(Render.Event(world.Log[printed]));
            series.AppendLine(Row(world));
        }

        Console.WriteLine(Render.Status(world));
        Console.WriteLine($"Outcome: {Render.Bold(world.Outcome.ToString())} after {world.Tick} weeks with policy '{policy.Name}' (seed {world.Seed}).");
        if (o.Get("timeseries") is { } path)
        {
            File.WriteAllText(path, series.ToString());
            Console.WriteLine($"Wrote weekly time series to {path}");
        }
        return 0;
    }

    private static string Row(World w)
    {
        var edges = w.Trust.Edges().Where(e => !w.Communes[e.A].Occupied && !w.Communes[e.B].Occupied).ToList();
        string F(double v) => Batch.F(v);
        return string.Join(",", w.Tick, w.Date.ToString("yyyy-MM-dd"), F(w.Influence), F(w.Drift.Value), w.Drift.Stage,
            w.Federation.Count(), w.Communes.Count(c => c.Occupied), w.Communes.Count(c => !c.Occupied && !c.Federated),
            F(edges.Count == 0 ? 0 : edges.Average(e => w.Trust[e.A, e.B])),
            F(w.Mean(c => c.Wellbeing)), F(w.Mean(c => c.PracticalEducation)), F(w.Mean(c => c.PoliticalEducation)),
            F(w.Enemies.Sum(e => e.EffectiveStrength)), F(w.Radicalization),
            F(w.Scores.Survival), F(w.Scores.QualityOfLife), F(w.Scores.Spread), F(w.Scores.Freedom));
    }
}
