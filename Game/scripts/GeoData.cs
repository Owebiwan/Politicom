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
    /// <summary>Provinces the federation's outer edge follows; drawn only when <see cref="DrawProvinces"/>.</summary>
    /// <summary>Contested provinces are held only near the communes; the rest are held entire.</summary>
    public List<(string Name, Vector2[] Ring, bool Contested)> Provinces { get; } = new();
    public bool DrawProvinces { get; private set; }
    /// <summary>Real boundaries by commune name, when the scenario has them.</summary>
    public Dictionary<string, Vector2[]> Territories { get; } = new();

    /// <summary>Geography generated with a sandbox map.</summary>
    public static GeoData From(Politicom.Sim.Sandbox.GeographyDefinition g)
    {
        static Vector2[] Points(double[][] pts) => pts.Select(p => new Vector2((float)p[0], (float)p[1])).ToArray();
        var geo = new GeoData
        {
            Bounds = new Rect2((float)g.Bbox[0], (float)g.Bbox[1], (float)(g.Bbox[2] - g.Bbox[0]), (float)(g.Bbox[3] - g.Bbox[1])),
            DrawProvinces = g.DrawProvinces,
        };
        geo.Land.AddRange(g.Land.Select(Points));
        geo.Lakes.AddRange(g.Lakes.Select(Points));
        geo.Borders.AddRange(g.Borders.Select(Points));
        geo.Rivers.AddRange(g.Rivers.Select(r => (r.Name, Points(r.Points))));
        geo.Provinces.AddRange(g.Provinces.Select(p => (p.Name, Points(p.Ring), p.Contested)));
        return geo;
    }

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
        if (root.TryGetProperty("provinces", out var provinces))
            foreach (var p in provinces.EnumerateArray())
                geo.Provinces.Add((p.GetProperty("name").GetString() ?? "", Points(p.GetProperty("ring")),
                    p.TryGetProperty("contested", out var c) && c.GetBoolean()));
        geo.DrawProvinces = root.TryGetProperty("drawProvinces", out var draw) && draw.GetBoolean();
        if (root.TryGetProperty("territories", out var territories))
            foreach (var t in territories.EnumerateObject()) geo.Territories[t.Name] = Points(t.Value);
        return geo;
    }

    /// <summary>
    /// Land for filling, with every vertex on the clipping box pushed far outward, so where land
    /// runs off the edge of the data it carries on past the edge of the screen instead of
    /// stopping in a straight line against the sea.
    /// </summary>
    public Vector2[] Extended(Vector2[] ring)
    {
        const float eps = 1e-4f;
        float far = Mathf.Max(Bounds.Size.X, Bounds.Size.Y) * 4;
        return ring.Select(p =>
        {
            var q = p;
            if (Mathf.Abs(p.X - Bounds.Position.X) < eps) q.X -= far;
            if (Mathf.Abs(p.X - Bounds.End.X) < eps) q.X += far;
            if (Mathf.Abs(p.Y - Bounds.Position.Y) < eps) q.Y -= far;
            if (Mathf.Abs(p.Y - Bounds.End.Y) < eps) q.Y += far;
            return q;
        }).ToArray();
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
