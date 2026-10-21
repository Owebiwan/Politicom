using System.Text.Json;

namespace Politicom.Sim;

public sealed class ScenarioDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public int EndTick { get; set; }
    public int NeighborCount { get; set; } = 3;
    public double NeighborRadiusKm { get; set; } = 25;
    public List<HostileState> Enemies { get; set; } = new();
    public AllyState Ally { get; set; } = new();
    public List<CommuneDefinition> Communes { get; set; } = new();
    public List<EventDefinition> Events { get; set; } = new();

    public static ScenarioDefinition Load(string path) => Parse(File.ReadAllText(path));

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
}
