using System.Globalization;
using Politicom.Sim;

namespace Politicom.Cli;

internal sealed class Options
{
    private readonly Dictionary<string, string> _values = new();
    private readonly List<string> _sets = new();

    public static Options Parse(IEnumerable<string> args)
    {
        var o = new Options();
        var list = args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (!list[i].StartsWith("--")) throw new ArgumentException($"Unexpected argument '{list[i]}'.");
            string key = list[i][2..];
            if (i + 1 >= list.Count) throw new ArgumentException($"--{key} needs a value.");
            string value = list[++i];
            if (key == "set") o._sets.Add(value);
            else o._values[key] = value;
        }
        return o;
    }

    public string Get(string key, string fallback) => _values.GetValueOrDefault(key, fallback);
    public string? Get(string key) => _values.GetValueOrDefault(key);
    public int GetInt(string key, int fallback) => _values.TryGetValue(key, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;
    public ulong GetSeed() => _values.TryGetValue("seed", out var v) ? ulong.Parse(v, CultureInfo.InvariantCulture) : 1;
    /// <summary>A bare "sandbox" means a generated map seeded by --seed.</summary>
    public string Scenario => Get("scenario", "catalonia_1936") is var id && id == Sim.Sandbox.SandboxGenerator.IdPrefix
        ? Sim.Sandbox.SandboxGenerator.Id(GetSeed())
        : id;

    /// <summary>True for a bare --scenario sandbox: batch runs then generate a fresh map per game.</summary>
    public bool FreshSandboxPerRun => Get("scenario") == Sim.Sandbox.SandboxGenerator.IdPrefix;

    public Balance BuildBalance()
    {
        var b = new Balance();
        if (Get("balance") is { } path) b.ApplyOverrides(File.ReadAllText(path));
        foreach (var s in _sets)
        {
            var (name, value) = SplitAssignment(s);
            b.Set(name, double.Parse(value, CultureInfo.InvariantCulture));
        }
        return b;
    }

    public (string Name, double[] Values)? Sweep()
    {
        if (Get("sweep") is not { } s) return null;
        var (name, values) = SplitAssignment(s);
        return (name, values.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray());
    }

    private static (string, string) SplitAssignment(string s)
    {
        int eq = s.IndexOf('=');
        if (eq <= 0) throw new ArgumentException($"Expected Name=Value, got '{s}'.");
        return (s[..eq], s[(eq + 1)..]);
    }
}
