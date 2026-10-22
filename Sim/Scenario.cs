using System.Text.Json;

namespace Politicom.Sim;

public sealed class ScenarioDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public int EndTick { get; set; }
    /// <summary>Calendar days per turn. 7 for most scenarios; shorter for sieges like Paris 1871.</summary>
    public int DaysPerTick { get; set; } = 7;
    /// <summary>Food and goods arriving from outside each turn, shared by population among federated communes.</summary>
    public double FoodImports { get; set; }
    /// <summary>Multiplies how fast front communes lose and regain control — higher for street fighting.</summary>
    public double WarPace { get; set; } = 1;
    public double GoodsImports { get; set; }
    public int NeighborCount { get; set; } = 3;
    public double NeighborRadiusKm { get; set; } = 25;
    public List<HostileState> Enemies { get; set; } = new();
    public AllyState Ally { get; set; } = new();
    public List<CommuneDefinition> Communes { get; set; } = new();
    public List<EventDefinition> Events { get; set; } = new();
    /// <summary>Railway lines at the start, as pairs of commune names. A line also makes the two neighbours.</summary>
    public List<string[]> Railways { get; set; } = new();
    /// <summary>The era of the world map this scenario is played on (Data/world/&lt;era&gt;.json), or null.</summary>
    public string? World { get; set; }
    /// <summary>The world state the region lies in.</summary>
    public string? HomeState { get; set; }
    /// <summary>Generated geography (sandbox maps). Historical scenarios load theirs from Data/geo.</summary>
    public Sandbox.GeographyDefinition? Geography { get; set; }

    public static ScenarioDefinition Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// A scenario by id: a file in Data/scenarios, or a generated map for
    /// "sandbox:SEED" / "sandbox:SEED:COMMUNES".
    /// </summary>
    public static ScenarioDefinition Resolve(string dataDir, string id)
    {
        var (baseId, length) = SplitLength(id);
        if (Sandbox.SandboxGenerator.TryParseId(baseId, out var seed, out var communes))
            return Sandbox.SandboxGenerator.Load(dataDir).Generate(seed, communes, length: length);
        var scenario = Load(DataPaths.Scenario(dataDir, baseId));
        return length is int turns ? scenario.WithLength(turns) : scenario;
    }

    public const int MaxTurns = 500;
    internal static double RenewedBase = 0.6;
    internal static double RenewedGrowth = 0.06;

    /// <summary>"catalonia_1936@250" → ("catalonia_1936", 250); no suffix → (id, null).</summary>
    public static (string BaseId, int? Length) SplitLength(string id)
    {
        int at = id.LastIndexOf('@');
        if (at < 0) return (id, null);
        if (!int.TryParse(id[(at + 1)..], out int turns)) throw new ArgumentException($"Bad game length in scenario id '{id}'.");
        return (id[..at], turns);
    }

    /// <summary>
    /// A copy that runs for <paramref name="turns"/> turns (at least the historical length, at
    /// most <see cref="MaxTurns"/>). After the scripted history the war goes on: every enemy that
    /// fought keeps launching renewed offensives, starting from its average offensive (not its
    /// climactic last one) and growing slowly. Deterministic, so a given length always gives the
    /// same game.
    /// </summary>
    public ScenarioDefinition WithLength(int turns)
    {
        var (baseId, _) = SplitLength(Id);
        var copy = JsonSerializer.Deserialize<ScenarioDefinition>(JsonSerializer.Serialize(this, Json.Options), Json.Options)!;
        int historical = EndTick;
        turns = Math.Clamp(turns, historical, MaxTurns);
        copy.Id = turns == historical ? baseId : $"{baseId}@{turns}";
        copy.EndTick = turns;
        if (turns == historical) return copy;

        int interval = Math.Max(10, historical / 3);
        foreach (var e in copy.Enemies)
        {
            e.GrowthUntil = historical; // past the history, the enemy rebuilds only through its offensives
            var last = e.Offensives.OrderBy(o => o.Tick).LastOrDefault();
            if (last is null) continue;
            double typical = e.Offensives.Average(o => o.Strength);
            int k = 1;
            for (int tick = Math.Max(historical, last.Tick + last.Duration) + interval / 2; tick < turns; tick += interval, k++)
                e.Offensives.Add(new Offensive
                {
                    Name = $"A renewed offensive by the {e.Name}",
                    Tick = tick,
                    Duration = Math.Max(3, Math.Min(last.Duration, interval / 2)),
                    Strength = Math.Round(typical * (RenewedBase + RenewedGrowth * k), 1),
                });
        }
        return copy;
    }

    public static ScenarioDefinition Parse(string json) =>
        JsonSerializer.Deserialize<ScenarioDefinition>(json, Json.Options)
        ?? throw new InvalidDataException("Could not read the scenario file.");
}

public sealed class CommuneDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public CommuneKind Kind { get; set; }
    public double Lon { get; set; }
    public double Lat { get; set; }
    public double Population { get; set; }
    public double Collectivism { get; set; }
    public double Militancy { get; set; }
    public double Federalism { get; set; }
    public double Practical { get; set; }
    public double Political { get; set; }
    /// <summary>Names of the enemies this commune starts on the front against.</summary>
    public string[] FrontOf { get; set; } = Array.Empty<string>();
    public string[] Techs { get; set; } = Array.Empty<string>();
}

/// <summary>Finds the shared Data/ directory from the CLI, tests or the Godot project.</summary>
public static class DataPaths
{
    public static string Find(params string[] startDirectories)
    {
        var starts = startDirectories.Concat(new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() });
        foreach (var start in starts)
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Data");
                if (File.Exists(Path.Combine(candidate, "technologies.json"))) return candidate;
            }
        }
        throw new DirectoryNotFoundException("Could not find the Data/ directory (looked for Data/technologies.json in parent folders).");
    }

    public static string Scenario(string dataDir, string id) => Path.Combine(dataDir, "scenarios", id + ".json");
    public static string Technologies(string dataDir) => Path.Combine(dataDir, "technologies.json");
    public static string World(string dataDir, string era) => Path.Combine(dataDir, "world", era + ".json");
    public static string SandboxTemplates(string dataDir) => Path.Combine(dataDir, "sandbox", "event_templates.json");
}
