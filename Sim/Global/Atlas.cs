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
    public double Power { get; init; } = 1;
    public IReadOnlyList<string> Neighbours { get; init; } = Array.Empty<string>();
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

    /// <summary>The world of a generated map: its land and its neighbouring countries.</summary>
    public static Atlas FromGenerated(Sandbox.GeographyDefinition g) => new()
    {
        Era = "generated",
        Land = g.Land,
        Borders = g.Neighbours.SelectMany(OuterEdges).ToList(),
        States = g.Neighbours.Select(n => new AtlasState
        {
            Id = n.Id,
            Name = n.Name,
            Ideology = n.Ideology,
            Color = n.Color,
            Power = n.Power,
            Rings = n.Rings,
        }).ToList(),
    };

    /// <summary>
    /// A neighbour is several cells; its border is the edges only one of its cells has (edges two
    /// of its cells share are internal). Returned as two-point lines.
    /// </summary>
    private static IEnumerable<double[][]> OuterEdges(Sandbox.NeighbourDefinition n)
    {
        static (double, double, double, double) Key(double[] a, double[] b)
        {
            var (p, q) = (a[0], a[1]).CompareTo((b[0], b[1])) <= 0 ? (a, b) : (b, a);
            return (Math.Round(p[0], 3), Math.Round(p[1], 3), Math.Round(q[0], 3), Math.Round(q[1], 3));
        }
        var edges = n.Rings.SelectMany(r => r.Select((p, i) => (A: p, B: r[(i + 1) % r.Length]))).ToList();
        var counts = edges.GroupBy(e => Key(e.A, e.B)).ToDictionary(g => g.Key, g => g.Count());
        return edges.Where(e => counts[Key(e.A, e.B)] == 1).Select(e => new[] { e.A, e.B });
    }

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
                Power = s.TryGetProperty("power", out var power) ? power.GetDouble() : 1,
                Neighbours = s.TryGetProperty("neighbours", out var n) ? n.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>(),
                Capital = s.TryGetProperty("capital", out var cap) && cap.ValueKind == JsonValueKind.Object ? CityOf(cap) : null,
                Cities = s.GetProperty("cities").EnumerateArray().Select(CityOf).ToList(),
                Rings = s.GetProperty("rings").EnumerateArray().Select(Points).ToList(),
            }).ToList(),
        };
    }
}
