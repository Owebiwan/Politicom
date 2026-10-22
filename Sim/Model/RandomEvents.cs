using System.Text.Json;

namespace Politicom.Sim;

/// <summary>
/// A random event that can happen in any game whose era, regime and communes suit it
/// (Data/events/random_events.json). Placeholders are filled when it fires:
/// {commune} a commune that fits, {neighbour} one of its neighbours, {enemy} the main enemy,
/// {region} the country, {state} and {stateId} the most interested foreign state.
/// </summary>
public sealed class RandomEventTemplate
{
    public string Id { get; set; } = "";
    /// <summary>Years it can happen in, [from, to] (negative = BC).</summary>
    public int[] Eras { get; set; } = { -100000, 100000 };
    /// <summary>Regimes it suits (the one the movement rises against), e.g. "empire"; empty = any.</summary>
    public string[] Regimes { get; set; } = Array.Empty<string>();
    /// <summary>"agrarian", "industrial", "front", "rear" or "world" (needs other states); empty = any.</summary>
    public string Needs { get; set; } = "";
    public double Weight { get; set; } = 1;
    public EventDefinition Event { get; set; } = new();

    public static List<RandomEventTemplate> Parse(string json) =>
        JsonSerializer.Deserialize<RandomEventFile>(json, Json.Options)?.Events
        ?? throw new InvalidDataException("Could not read the random events file.");

    public static List<RandomEventTemplate> Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new();

    private sealed class RandomEventFile
    {
        public List<RandomEventTemplate> Events { get; set; } = new();
    }
}
