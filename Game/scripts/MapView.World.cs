using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim.Global;

namespace Politicom.Game;

/// <summary>
/// The world layer of the map: every state of the era, filled and outlined, under the region.
/// State fills are triangulated once into a mesh in layout space and drawn with a transform, so
/// zooming and panning cost nothing; coasts and borders are culled to what's on screen. Below
/// <see cref="DetailZoom"/> the region's small detail (towns, armies, railways) is hidden and
/// country names take over.
/// </summary>
public partial class MapView
{
    /// <summary>Zoom below which the map reads as a world map.</summary>
    public const float DetailZoom = 0.22f;
    private const float MaxZoom = 6f;

    private static readonly Color WorldLand = new("2b2e31");
    private static readonly Color WorldCoast = new("3d5670");
    private static readonly Color WorldBorder = new("8a8f96");
    private static readonly Dictionary<string, Color> IdeologyColors = new()
    {
        ["liberal"] = new("5b7fb0"),
        ["conservative"] = new("8a7b5c"),
        ["fascist"] = new("4a4a4a"),
        ["communist"] = new("a0392f"),
        ["monarchy"] = new("7b5f8f"),
        ["libertarian"] = new("c4473a"),
    };

    private Atlas? _globe;

    /// <summary>The era's world map, or null (generated sandbox maps have none yet).</summary>
    public Atlas? Globe
    {
        get => _globe;
        set
        {
            if (ReferenceEquals(value, _globe)) return;
            _globe = value;
            _globeLayout = null;
            _globeMesh = null;
            QueueRedraw();
        }
    }

    /// <summary>The state under the pointer at world zoom, for the tooltip.</summary>
    private AtlasState? _hoverState;

    private sealed class GlobeLayout
    {
        public List<(AtlasState State, Vector2[] Ring, Rect2 Box)> StateRings { get; } = new();
        public List<(Vector2[] Line, Rect2 Box)> Coasts { get; } = new();
        public List<(Vector2[] Line, Rect2 Box)> Borders { get; } = new();
        public Dictionary<string, (Vector2 At, float Area)> Labels { get; } = new();
        public Rect2 Extent;
        public Rect2 Region;
    }

    private GlobeLayout? _globeLayout;
    private Vector2[]? _globeLayoutFor;
    private ArrayMesh? _globeMesh;
    private MapMode _globeMeshMode;

    private GlobeLayout? Globe_Layout()
    {
        if (Globe is null || Geo is null) return null;
        var layout = Layout(); // sets _project
        if (_globeLayout != null && ReferenceEquals(_globeLayoutFor, layout)) return _globeLayout;
        _globeMesh = null;
        _globeLayoutFor = layout;
        var g = new GlobeLayout();
        Rect2 Box(Vector2[] pts)
        {
            var min = new Vector2(pts.Min(p => p.X), pts.Min(p => p.Y));
            var max = new Vector2(pts.Max(p => p.X), pts.Max(p => p.Y));
            return new Rect2(min, max - min);
        }
        Vector2 P(double[] p) => _project(new Vector2((float)p[0], (float)p[1]));
        foreach (var s in Globe.States)
            foreach (var ring in s.Rings)
            {
                var pts = ring.Select(P).ToArray();
                g.StateRings.Add((s, pts, Box(pts)));
            }
        foreach (var ring in Globe.Land)
        {
            var pts = ring.Select(P).Append(P(ring[0])).ToArray();
            g.Coasts.Add((pts, Box(pts)));
        }
        foreach (var line in Globe.Borders)
        {
            var pts = line.Select(P).ToArray();
            g.Borders.Add((pts, Box(pts)));
        }
        foreach (var s in Globe.States)
        {
            // Label on the piece holding the capital (the metropole, not a colony), sized by it.
            var pieces = g.StateRings.Where(r => r.State == s).ToList();
            // Capitals are often on the coast, just outside a simplified outline: test the piece's box.
            var home = s.Capital is { } cap
                ? pieces.Where(r => r.Box.Grow(r.Box.Size.Length() * 0.02f + 2).HasPoint(_project(new Vector2((float)cap.Lon, (float)cap.Lat))))
                        .OrderByDescending(r => r.Box.Area).FirstOrDefault()
                : default;
            var piece = home.Ring != null ? home : pieces.OrderByDescending(r => r.Box.Area).First();
            g.Labels[s.Id] = (piece.Box.GetCenter(), piece.Box.Area);
        }
        var all = g.Coasts.Select(c => c.Box).Concat(g.StateRings.Select(r => r.Box)).ToList();
        g.Extent = all.Aggregate(all[0], (a, b) => a.Merge(b));
        var b0 = _project(Geo.Bounds.Position);
        var b1 = _project(Geo.Bounds.End);
        g.Region = new Rect2(b0, Vector2.Zero).Expand(b1);
        return _globeLayout = g;
    }

    /// <summary>Smallest zoom: the whole world fits the map pane.</summary>
    private float MinZoom
    {
        get
        {
            if (Globe_Layout() is not { } g) return 0.6f;
            var area = Drawable;
            float fit = Mathf.Min(area.Size.X / (g.Extent.Size.X * _fitScale), area.Size.Y / (g.Extent.Size.Y * _fitScale));
            return Mathf.Min(0.6f, fit * 0.95f);
        }
    }

    private float ClampZoom(float z) => Mathf.Clamp(z, MinZoom, MaxZoom);

    /// <summary>Zooms out to show the whole world, centred on it.</summary>
    public void ShowWorld()
    {
        if (Globe_Layout() is not { } g) return;
        _zoom = MinZoom;
        _pan = -(g.Extent.GetCenter() * _fitScale + _fitOffset - Size / 2) * _zoom;
        QueueRedraw();
    }

    /// <summary>Back to the region at its home zoom.</summary>
    public void ShowRegion()
    {
        _zoom = 1;
        _pan = Vector2.Zero;
        QueueRedraw();
    }

    public bool AtWorldZoom => Globe != null && _zoom < DetailZoom;

    private void BuildGlobeMesh(GlobeLayout g)
    {
        var verts = new List<Vector2>();
        var colors = new List<Color>();
        var indices = new List<int>();
        void Add(Vector2[] ring, Color color)
        {
            foreach (var piece in Simple(Clean(ring)))
            {
                var clean = Clean(piece);
                if (clean.Length < 3) continue;
                // Most pieces triangulate directly; the odd awkward one is split into convex parts and fanned.
                var parts = Geometry2D.IsPolygonClockwise(clean) ? new[] { clean.Reverse().ToArray() } : new[] { clean };
                foreach (var part in parts)
                {
                    var tri = Geometry2D.TriangulatePolygon(part);
                    if (tri.Length > 0)
                    {
                        int start = verts.Count;
                        verts.AddRange(part);
                        colors.AddRange(Enumerable.Repeat(color, part.Length));
                        indices.AddRange(tri.Select(i => i + start));
                        continue;
                    }
                    foreach (var convex in Geometry2D.DecomposePolygonInConvex(part))
                    {
                        int start = verts.Count;
                        verts.AddRange(convex);
                        colors.AddRange(Enumerable.Repeat(color, convex.Length));
                        for (int k = 1; k + 1 < convex.Length; k++) indices.AddRange(new[] { start, start + k, start + k + 1 });
                    }
                }
            }
        }
        foreach (var (line, _) in g.Coasts) Add(line[..^1], WorldLand);
        // Largest first, so enclaves (Lesotho in South Africa) are drawn on top.
        foreach (var (state, ring, _) in g.StateRings.OrderByDescending(r => r.Box.Area)) Add(ring, StateFill(state));

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        _globeMesh = new ArrayMesh();
        _globeMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _globeMeshMode = Mode;
    }

    /// <summary>Drops repeated and closing points, which make triangulation fail.</summary>
    private static Vector2[] Clean(Vector2[] ring)
    {
        var result = new List<Vector2>(ring.Length);
        foreach (var p in ring)
            if (result.Count == 0 || result[^1].DistanceSquaredTo(p) > 1e-6f) result.Add(p);
        while (result.Count > 1 && result[0].DistanceSquaredTo(result[^1]) <= 1e-6f) result.RemoveAt(result.Count - 1);
        return result.ToArray();
    }

    private Color StateFill(AtlasState s)
    {
        if (Mode == MapMode.Ideology)
            return IdeologyColors.TryGetValue(s.Ideology, out var c) ? c : new Color("777777");
        // Political map: each state's colour, muted so the region stands out.
        return Color.FromString(s.Color, new Color("777777")).Darkened(0.35f);
    }

    /// <summary>Draws the world under everything else. Returns false if there is no world layer.</summary>
    private bool DrawGlobe(Font font)
    {
        if (Globe_Layout() is not { } g) return false;
        if (_globeMesh == null || _globeMeshMode != Mode) BuildGlobeMesh(g);

        // ToScreen is affine: layout * s + o.
        float s = _fitScale * _zoom;
        var o = (_fitOffset - Size / 2) * _zoom + Size / 2 + _pan;
        DrawSetTransform(o, 0, new Vector2(s, s));
        DrawMesh(_globeMesh!, null);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        var view = new Rect2(-o / s, Size / s); // the visible area, in layout space
        bool InRegion(Vector2 a, Vector2 b) => _zoom >= DetailZoom && g.Region.HasPoint((a + b) / 2);
        foreach (var (line, box) in g.Coasts)
            if (box.Intersects(view)) DrawCulled(line, WorldCoast, 1.2f, InRegion);
        foreach (var (line, box) in g.Borders)
            if (box.Intersects(view)) DrawCulled(line, WorldBorder, _zoom < DetailZoom ? 1f : 1.4f, InRegion);
        return true;
    }

    // Inside the region its own, more detailed coast and borders replace the world's.

    private void DrawCulled(Vector2[] line, Color color, float width, Func<Vector2, Vector2, bool> skip)
    {
        var run = new List<Vector2>();
        void Flush()
        {
            if (run.Count >= 2) DrawPolyline(run.ToArray(), color, width, true);
            run.Clear();
        }
        for (int i = 0; i + 1 < line.Length; i++)
        {
            if (skip(line[i], line[i + 1])) { Flush(); continue; }
            if (run.Count == 0) run.Add(ToScreen(line[i]));
            run.Add(ToScreen(line[i + 1]));
        }
        Flush();
    }

    /// <summary>At world zoom the region is a speck: mark it with a pulsing ring.</summary>
    private void DrawRegionMarker()
    {
        if (Globe_Layout() is not { } g) return;
        var at = ToScreen(g.Region.GetCenter());
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_time * 3);
        DrawArc(at, 10 + 6 * pulse, 0, Mathf.Tau, 32, new Color(Anarchist, 0.9f - 0.5f * pulse), 2.5f, true);
        DrawCircle(at, 4, Anarchist);
    }

    /// <summary>Country names at world zoom: biggest first, skipped where they'd overlap or not fit.</summary>
    private void DrawGlobeLabels(Font font, List<Rect2> taken)
    {
        if (Globe_Layout() is not { } g || Globe is null) return;
        float s = _fitScale * _zoom;
        foreach (var state in Globe.States.OrderByDescending(st => g.Labels[st.Id].Area))
        {
            var (at, area) = g.Labels[state.Id];
            float screenSide = Mathf.Sqrt(area) * s;
            if (screenSide < 28) continue;
            int size = (int)Mathf.Clamp(screenSide / 9, 10, 20);
            var text = state.Name.ToUpperInvariant();
            var extent = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
            if (extent.X > screenSide * 1.6f) continue;
            var p = ToScreen(at) - extent / 2 + new Vector2(0, size * 0.35f);
            var rect = new Rect2(p - new Vector2(0, size), extent);
            if (!new Rect2(Vector2.Zero, Size).Encloses(rect) || taken.Any(t => t.Intersects(rect))) continue;
            taken.Add(rect);
            DrawStringOutline(font, p, text, HorizontalAlignment.Left, -1, size, 3, new Color(0, 0, 0, 0.6f));
            DrawString(font, p, text, HorizontalAlignment.Left, -1, size, new Color(1, 1, 1, 0.78f));
        }
    }

    private AtlasState? HitState(Vector2 at)
    {
        if (Globe_Layout() is not { } g) return null;
        var p = ToLayout(at);
        // Smallest first, so an enclave wins over the state around it.
        foreach (var (state, ring, box) in g.StateRings.OrderBy(r => r.Box.Area))
            if (box.HasPoint(p) && Geometry2D.IsPointInPolygon(p, ring)) return state;
        return null;
    }

    private void DrawStateTooltip(Font font, AtlasState s)
    {
        var lines = new[]
        {
            s.Name,
            $"{char.ToUpperInvariant(s.Ideology[0])}{s.Ideology[1..]} · capital {s.Capital?.Name ?? "—"}",
        };
        float width = lines.Max(l => font.GetStringSize(l, HorizontalAlignment.Left, -1, 13).X) + 20;
        float height = lines.Length * 17 + 12;
        var at = _mouse + new Vector2(16, 16);
        if (at.X + width > Size.X) at.X = _mouse.X - width - 8;
        if (at.Y + height > Size.Y) at.Y = _mouse.Y - height - 8;
        DrawRect(new Rect2(at, new Vector2(width, height)), new Color(0.03f, 0.03f, 0.04f, 0.92f));
        DrawRect(new Rect2(at, new Vector2(width, height)), new Color(1, 1, 1, 0.15f), false, 1);
        for (int i = 0; i < lines.Length; i++)
            DrawString(font, at + new Vector2(10, 20 + i * 17), lines[i], HorizontalAlignment.Left, -1, i == 0 ? 15 : 13, i == 0 ? Colors.White : Label);
    }
}
