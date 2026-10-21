using System.Globalization;
using Politicom.Cli;
using Politicom.Sim;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var options = Options.Parse(args.Skip(1));
try
{
    return args.FirstOrDefault() switch
    {
        "batch" => Batch.Run(options),
        "run" => SingleRun.Run(options),
        "play" => Play.Run(options),
        "params" => ListParams(),
        _ => Usage(),
    };
}
catch (Exception e) when (e is ArgumentException or InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

static int ListParams()
{
    var b = new Balance();
    foreach (var name in Balance.ParameterNames()) Console.WriteLine($"{name,-30} {b.Get(name)}");
    return 0;
}

static int Usage()
{
    Console.WriteLine("""
        politicom — Anarchist grand strategy simulation

        Usage:
          politicom play   [--seed N] [--scenario ID] [--load FILE]
              Play the scenario in the terminal (or resume a saved game).

          politicom run    [--policy NAME] [--seed N] [--timeseries FILE.csv] [--set Param=Value ...]
              One headless game with a scripted player; prints the event log.

          politicom batch  [--runs N] [--policies a,b,...] [--seed N] [--out FILE.csv]
                           [--set Param=Value ...] [--sweep Param=v1,v2,...]
              Many headless games per policy (and per sweep value) — for balancing.

          politicom params
              List every balance parameter and its default.

        Policies: passive, educator, centralizer, balanced
        """);
    return 0;
}
