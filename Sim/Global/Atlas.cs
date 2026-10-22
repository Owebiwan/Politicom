using System.Text.Json;

namespace Politicom.Sim.Global;

public sealed record City(string Name, double Lon, double Lat, int Pop);

/// <summary>A state on the world map, as the era data describes it.</summary>
public sealed class AtlasState
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Ideology { get; init; } = "";
    public string Color { get; init; } = "#808080";
    public City? Capital { get; init; }
    public IReadOnlyList<City> Cities { get; init; } = Array.Empty<City>();
    /// <summary>Outlines in lon/lat; the first is the largest piece.</summary>
    public IReadOnlyList<double[][]> Rings { get; init; } = Array.Empty<double[][]>();
}

/// <summary>
/// The world map of one era (Data/world/&lt;era&gt;.json, built by tools/geo/build_world.py from
/// Natural Earth): period states, coastlines and the borders between states.
/// </summary>
public sealed class Atlas
{
    public string Era { get; private init; } = "";
    public string Note { get; private init; } = "";
    public IReadOnlyList<double[][]> Land { get; private init; } = Array.Empty<double[][]>();
    public IReadOnlyList<double[][]> Borders { get; private init; } = Array.Empty<double[][]>();
    public IReadOnlyList<AtlasState> States { get; private init; } = Array.Empty<AtlasState>();

    public AtlasState? State(string id) => States.FirstOrDefault(s => s.Id == id);

    public static Atlas Load(string dataDir, string era) => Parse(File.ReadAllText(DataPaths.World(dataDir, era)));

    public static Atlas Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        static double[][] Points(JsonElement e) => e.EnumerateArray().Select(p => new[] { p[0].GetDouble(), p[1].GetDouble() }).ToArray();
        static City CityOf(JsonElement c) => new(c.GetProperty("name").GetString() ?? "", c.GetProperty("lon").GetDouble(), c.GetProperty("lat").GetDouble(),
            c.TryGetProperty("pop", out var pop) ? pop.GetInt32() : 0);
        return new Atlas
        {
            Era = root.GetProperty("era").GetString() ?? "",
            Note = root.TryGetProperty("note", out var note) ? note.GetString() ?? "" : "",
            Land = root.GetProperty("land").EnumerateArray().Select(Points).ToList(),
            Borders = root.GetProperty("borders").EnumerateArray().Select(Points).ToList(),
            States = root.GetProperty("states").EnumerateArray().Select(s => new AtlasState
            {
                Id = s.GetProperty("id").GetString() ?? "",
                Name = s.GetProperty("name").GetString() ?? "",
                Ideology = s.GetProperty("ideology").GetString() ?? "",
                Color = s.GetProperty("color").GetString() ?? "#808080",
                Capital = s.TryGetProperty("capital", out var cap) && cap.ValueKind == JsonValueKind.Object ? CityOf(cap) : null,
                Cities = s.GetProperty("cities").EnumerateArray().Select(CityOf).ToList(),
                Rings = s.GetProperty("rings").EnumerateArray().Select(Points).ToList(),
            }).ToList(),
        };
    }
}
