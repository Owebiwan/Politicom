using System.Globalization;
using System.Text;
using Politicom.Sim;
using Politicom.Sim.Ai;

namespace Politicom.Cli;

/// <summary>Headless Monte-Carlo runs for balancing. Each run is deterministic in its seed.</summary>
internal static class Batch
{
    private sealed record Result(string Policy, double? SweepValue, ulong Seed, Outcome Outcome, int Ticks,
        double Survival, double Quality, double Spread, double Freedom, double Total,
        double FinalDrift, DriftStage Stage, int Federated, int Occupied, int Seceded, int Techs, double MeanTrust);

    public static int Run(Options o)
    {
        int runs = o.GetInt("runs", 200);
        ulong seed = o.GetSeed();
        var policies = o.Get("policies", string.Join(",", Policies.All.Select(p => p.Name))).Split(',').Select(Policies.Get).ToList();
        var sweep = o.Sweep();
        var sweepValues = sweep?.Values.Select(v => (double?)v).ToArray() ?? new double?[] { null };
        var baseBalance = o.BuildBalance();

        string dataDir = DataPaths.Find();
        // A bare --scenario sandbox measures balance across many generated maps: one per game.
        var sandbox = o.FreshSandboxPerRun ? Sim.Sandbox.SandboxGenerator.Load(dataDir) : null;
        var fixedScenario = sandbox == null ? ScenarioDefinition.Resolve(dataDir, o.Scenario) : null;
        var maps = new System.Collections.Concurrent.ConcurrentDictionary<ulong, Lazy<ScenarioDefinition>>(); // every policy plays the same map per seed
        var techs = TechCatalog.Load(DataPaths.Technologies(dataDir));

        var jobs = (from v in sweepValues from p in policies from i in Enumerable.Range(0, runs) select (Value: v, Policy: p, Seed: seed + (ulong)i)).ToList();
        var results = new Result[jobs.Count];
        var started = DateTime.UtcNow;
        Parallel.For(0, jobs.Count, i =>
        {
            var (value, policyTemplate, s) = jobs[i];
            var balance = baseBalance.Clone();
            if (value is { } v) balance.Set(sweep!.Value.Name, v);
            var scenario = fixedScenario ?? maps.GetOrAdd(s, key => new Lazy<ScenarioDefinition>(() => sandbox!.Generate(key))).Value;
            var world = World.Create(scenario, techs, balance, s);
            var policy = Policies.Get(policyTemplate.Name);
            while (world.Outcome == Outcome.Ongoing)
            {
                policy.Act(world);
                world.Step();
            }
            results[i] = Summarize(policyTemplate.Name, value, s, world);
        });

        PrintSummary(results, sweep?.Name, runs, DateTime.UtcNow - started);
        if (o.Get("out") is { } path)
        {
            WriteCsv(path, results, sweep?.Name);
            Console.WriteLine($"\nWrote {results.Length} rows to {path}");
        }
        return 0;
    }

    private static Result Summarize(string policy, double? value, ulong seed, World w)
    {
        var edges = w.Trust.Edges().Where(e => !w.Communes[e.A].Occupied && !w.Communes[e.B].Occupied).ToList();
        return new Result(policy, value, seed, w.Outcome, w.Tick,
            w.Scores.Survival, w.Scores.QualityOfLife, w.Scores.Spread, w.Scores.Freedom, w.Scores.Total,
            w.Drift.Value, w.Drift.Stage,
            w.Federation.Count(), w.Communes.Count(c => c.Occupied), w.Communes.Count(c => !c.Occupied && !c.Federated),
            w.Techs.All.Count(t => w.TechShare(t.Id) >= 0.5),
            edges.Count == 0 ? 0 : edges.Average(e => w.Trust[e.A, e.B]));
    }

    private static void PrintSummary(Result[] results, string? sweepName, int runs, TimeSpan elapsed)
    {
        Console.WriteLine($"{results.Length} games in {elapsed.TotalSeconds:0.0}s ({runs} per group)\n");
        string sweepCol = sweepName is null ? "" : $"{sweepName,-22}";
        Console.WriteLine($"{sweepCol}{"policy",-12} {"survived",8} {"destroyed",9} {"state",6} │ {"surv",5} {"QoL",5} {"spread",6} {"free",5} {"total",5} │ {"drift",5} {"fed",4} {"occ",4} {"sec",4} {"trust",5}");
        Console.WriteLine(new string('─', sweepCol.Length + 100));
        foreach (var g in results.GroupBy(r => (r.SweepValue, r.Policy)))
        {
            var list = g.ToList();
            double Pct(Outcome oc) => 100.0 * list.Count(r => r.Outcome == oc) / list.Count;
            string sv = sweepName is null ? "" : $"{g.Key.SweepValue,-22:0.###}";
            Console.WriteLine($"{sv}{g.Key.Policy,-12} {Pct(Outcome.Survived),7:0}% {Pct(Outcome.Destroyed),8:0}% {Pct(Outcome.BecameState),5:0}% │ " +
                              $"{list.Average(r => r.Survival),5:0} {list.Average(r => r.Quality),5:0} {list.Average(r => r.Spread),6:0} {list.Average(r => r.Freedom),5:0} {list.Average(r => r.Total),5:0} │ " +
                              $"{list.Average(r => r.FinalDrift),5:0} {list.Average(r => r.Federated),4:0} {list.Average(r => r.Occupied),4:0} {list.Average(r => r.Seceded),4:0.0} {list.Average(r => r.MeanTrust),5:0.00}");
        }
        Console.WriteLine("\nsurvived/destroyed/state = outcome share · scores 0–100 · drift = final hierarchy drift · fed/occ/sec = communes federated/occupied/seceded at end");
    }

    private static void WriteCsv(string path, Result[] results, string? sweepName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"policy,{sweepName ?? "sweep"},seed,outcome,ticks,survival,quality,spread,freedom,total,final_drift,stage,federated,occupied,seceded,widespread_techs,mean_trust");
        foreach (var r in results)
            sb.AppendLine(string.Join(",", r.Policy, r.SweepValue?.ToString(CultureInfo.InvariantCulture) ?? "", r.Seed, r.Outcome, r.Ticks,
                F(r.Survival), F(r.Quality), F(r.Spread), F(r.Freedom), F(r.Total), F(r.FinalDrift), r.Stage,
                r.Federated, r.Occupied, r.Seceded, r.Techs, F(r.MeanTrust)));
        File.WriteAllText(path, sb.ToString());
    }

    internal static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
