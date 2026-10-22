using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Politicom.Game;

/// <summary>
/// Real geography for a scenario (Data/geo/&lt;id&gt;.json, built by tools/geo/build_geo.py):
/// land, lakes, rivers and land borders in lon/lat, plus real commune boundaries where they
/// exist (Paris's arrondissements). Generated sandbox maps have none.
/// </summary>
public sealed class GeoData
{
    /// <summary>The area the data was clipped to (lon_min, lat_min, lon_max, lat_max).</summary>
    public Rect2 Bounds { get; private set; }
    public List<Vector2[]> Land { get; } = new();
    public List<Vector2[]> Lakes { get; } = new();
    public List<Vector2[]> Borders { get; } = new();
    public List<(string Name, Vector2[] Points)> Rivers { get; } = new();
    /// <summary>Real boundaries by commune name, when the scenario has them.</summary>
    public Dictionary<string, Vector2[]> Territories { get; } = new();

    public static GeoData Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var geo = new GeoData();
        static Vector2[] Points(JsonElement e) =>
            e.EnumerateArray().Select(p => new Vector2(p[0].GetSingle(), p[1].GetSingle())).ToArray();

        var b = root.GetProperty("bbox").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        geo.Bounds = new Rect2(b[0], b[1], b[2] - b[0], b[3] - b[1]);
        foreach (var ring in root.GetProperty("land").EnumerateArray()) geo.Land.Add(Points(ring));
        foreach (var ring in root.GetProperty("lakes").EnumerateArray()) geo.Lakes.Add(Points(ring));
        foreach (var line in root.GetProperty("borders").EnumerateArray()) geo.Borders.Add(Points(line));
        foreach (var river in root.GetProperty("rivers").EnumerateArray())
            geo.Rivers.Add((river.GetProperty("name").GetString() ?? "", Points(river.GetProperty("points"))));
        if (root.TryGetProperty("territories", out var territories))
            foreach (var t in territories.EnumerateObject()) geo.Territories[t.Name] = Points(t.Value);
        return geo;
    }

    /// <summary>
    /// Whether a land edge runs along the clipping box rather than a real coast: such edges are
    /// filled like any other but must not be drawn as coastline.
    /// </summary>
    public bool OnBounds(Vector2 a, Vector2 b)
    {
        const float eps = 1e-4f;
        bool Same(float x, float y, float edge) => Mathf.Abs(x - edge) < eps && Mathf.Abs(y - edge) < eps;
        return Same(a.X, b.X, Bounds.Position.X) || Same(a.X, b.X, Bounds.End.X)
            || Same(a.Y, b.Y, Bounds.Position.Y) || Same(a.Y, b.Y, Bounds.End.Y);
    }
}
