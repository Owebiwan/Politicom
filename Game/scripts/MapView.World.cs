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
        ["empire"] = new("a0703a"),
        ["tribal"] = new("5f7f55"),
        ["theocracy"] = new("c9b26b"),
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
            _meshVerts = null;
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
        /// <summary>Each state's name line: its home piece's centre, main axis (radians) and length along it.</summary>
        public Dictionary<string, (Vector2 At, float Angle, float Length)> NameLines { get; } = new();
        public Rect2 Extent;
        public Rect2 Region;
    }

    private GlobeLayout? _globeLayout;
    private Vector2[]? _globeLayoutFor;
    private ArrayMesh? _globeMesh;

    private GlobeLayout? Globe_Layout()
    {
        if (Globe is null || Geo is null) return null;
        var layout = Layout(); // sets _project
        if (_globeLayout != null && ReferenceEquals(_globeLayoutFor, layout)) return _globeLayout;
        _globeMesh = null;
        _meshVerts = null;
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
                // Generated neighbours stop at the edge of the generated area: carry them on past it.
                var pts = Generated
                    ? Geo.Extended(ring.Select(p => new Vector2((float)p[0], (float)p[1])).ToArray()).Select(_project).ToArray()
                    : ring.Select(P).ToArray();
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
            g.NameLines[s.Id] = NameLine(piece.Ring, piece.Box);
        }
        var all = g.Coasts.Select(c => c.Box).Concat(g.StateRings.Select(r => r.Box)).ToList();
        g.Extent = all.Aggregate(all[0], (a, b) => a.Merge(b));
        var b0 = _project(Geo.Bounds.Position);
        var b1 = _project(Geo.Bounds.End);
        g.Region = new Rect2(b0, Vector2.Zero).Expand(b1);
        return _globeLayout = g;
    }

    /// <summary>
    /// What the view may show, in layout space: the whole world, or for generated maps (whose
    /// neighbours run off to infinity) the generated area. Null when there's nothing to bound.
    /// </summary>
    private Rect2? Bounds
    {
        get
        {
            if (Globe_Layout() is { } g && !Generated) return g.Extent;
            if (Geo is null) return null;
            var a = _project(Geo.Bounds.Position);
            var b = _project(Geo.Bounds.End);
            return new Rect2(a, Vector2.Zero).Expand(b);
        }
    }

    /// <summary>Smallest zoom: the bounded area just fills the map pane.</summary>
    private float MinZoom
    {
        get
        {
            if (Bounds is not { } r) return 0.6f;
            var area = new Rect2(Vector2.Zero, Size);
            float fit = Mathf.Min(area.Size.X / (r.Size.X * _fitScale), area.Size.Y / (r.Size.Y * _fitScale));
            // Fit the whole world in view (letterboxed along one axis), but never zoom out past it.
            return Mathf.Min(fit, 1);
        }
    }

    /// <summary>
    /// Keeps the view on the map: along an axis where the map is larger than the pane, no edge
    /// may come inside it; where it's smaller, the map is centred.
    /// </summary>
    private void ClampView()
    {
        if (Bounds is not { } r) return;
        _zoom = Mathf.Max(_zoom, MinZoom);
        var tl = ToScreen(r.Position);
        var br = ToScreen(r.End);
        var shift = Vector2.Zero;
        for (int axis = 0; axis < 2; axis++)
        {
            float lo = tl[axis], hi = br[axis], size = Size[axis];
            if (hi - lo <= size) shift[axis] = (size - (hi - lo)) / 2 - lo;
            else if (lo > 0) shift[axis] = -lo;
            else if (hi < size) shift[axis] = size - hi;
        }
        _pan += shift;
    }

    private float ClampZoom(float z) => Mathf.Clamp(z, MinZoom, MaxZoom);

    /// <summary>Zooms out to show the whole world, centred on it.</summary>
    public void ShowWorld()
    {
        if (Globe_Layout() is not { } g) return;
        _zoom = MinZoom;
        _pan = -(g.Extent.GetCenter() * _fitScale + _fitOffset - Size / 2) * _zoom;
        ClampView();
        QueueRedraw();
    }

    /// <summary>Back to the region at its home zoom.</summary>
    public void ShowRegion()
    {
        _zoom = 1;
        _pan = Vector2.Zero;
        ClampView();
        QueueRedraw();
    }

    public bool AtWorldZoom => Globe != null && _zoom < DetailZoom;

    /// <summary>A generated map's world: only its neighbours, which end at the edge of the generated area.</summary>
    private bool Generated => Globe?.Era == "generated";

    // The mesh's geometry is triangulated once; colours are refilled when the map mode changes,
    // or every turn for the modes that follow the simulation.
    private Vector2[]? _meshVerts;
    private int[]? _meshIndices;
    private int[]? _meshOwner; // per vertex: index into Globe.States, or -1 for plain land
    private (MapMode Mode, int Tick, string? Selected) _meshKey = (MapMode.Trust, -1, null);

    private void BuildGlobeMesh(GlobeLayout g)
    {
        var verts = new List<Vector2>();
        var owners = new List<int>();
        var indices = new List<int>();
        var stateIndex = Globe!.States.Select((s, i) => (s, i)).ToDictionary(x => x.s, x => x.i);
        void Add(Vector2[] ring, int owner)
        {
            foreach (var piece in Simple(Clean(ring)))
            {
                var clean = Clean(piece);
                if (clean.Length < 3) continue;
                var part = Geometry2D.IsPolygonClockwise(clean) ? clean.Reverse().ToArray() : clean;
                var tri = Geometry2D.TriangulatePolygon(part);
                if (tri.Length > 0)
                {
                    int start = verts.Count;
                    verts.AddRange(part);
                    owners.AddRange(Enumerable.Repeat(owner, part.Length));
                    indices.AddRange(tri.Select(i => i + start));
                    continue;
                }
                // The odd awkward piece: split into convex parts and fan them.
                foreach (var convex in Geometry2D.DecomposePolygonInConvex(part))
                {
                    int start = verts.Count;
                    verts.AddRange(convex);
                    owners.AddRange(Enumerable.Repeat(owner, convex.Length));
                    for (int k = 1; k + 1 < convex.Length; k++) indices.AddRange(new[] { start, start + k, start + k + 1 });
                }
            }
        }
        foreach (var (line, _) in g.Coasts) Add(line[..^1], -1);
        // Largest first, so enclaves (Lesotho in South Africa) are drawn on top.
        foreach (var (state, ring, _) in g.StateRings.OrderByDescending(r => r.Box.Area)) Add(ring, stateIndex[state]);
        _meshVerts = verts.ToArray();
        _meshIndices = indices.ToArray();
        _meshOwner = owners.ToArray();
        _meshKey = (Mode, -1, null);
        _globeMesh = null;
    }

    private void ColorGlobeMesh()
    {
        // Revolutions recolour states in any mode, so every mode refreshes per turn.
        var key = (Mode, World?.Tick ?? 0, SelectedState);
        if (_globeMesh != null && key == _meshKey) return;
        var fills = Globe!.States.Select(StateFill).ToArray();
        var colors = new Color[_meshVerts!.Length];
        // Alpha is the tint over the real ground (land.gdshader): bare land shows the ground itself.
        for (int i = 0; i < colors.Length; i++) colors[i] = _meshOwner![i] < 0 ? WorldLand with { A = 0 } : fills[_meshOwner[i]] with { A = StateTint };
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _meshVerts;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = _meshIndices!;
        _globeMesh = new ArrayMesh();
        _globeMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _meshKey = key;
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
        var sim = World?.States.FirstOrDefault(x => x.Id == s.Id);
        string ideology = sim?.Ideology.ToString().ToLowerInvariant() ?? s.Ideology;
        Color c;
        if (sim is { Sister: true } && Mode is not (MapMode.Diplomacy or MapMode.Revolution))
            c = new Color("8a3a32"); // a sister federation
        else if (Mode == MapMode.Ideology)
            c = IdeologyColors.TryGetValue(ideology, out var ic) ? ic : new Color("777777");
        else if (Mode == MapMode.Diplomacy)
            c = sim is null ? new Color("4a4d52")
                : sim.Stance < 0 ? new Color("6d6f73").Lerp(new Color("b0392f"), (float)-sim.Stance)
                : new Color("6d6f73").Lerp(new Color("4f9a5c"), (float)sim.Stance);
        else if (Mode == MapMode.Culture)
            // Rival cultures: how strongly each state's nation, church or party resists the movement's ideas.
            c = sim is null ? new Color("4a4d52") : new Color("4a4d52").Lerp(new Color("6a4c93"), Mathf.Clamp((float)sim.CulturalWeight * 1.5f, 0, 1));
        else if (Mode == MapMode.Revolution)
            c = sim is null ? new Color("4a4d52") : new Color("3a3233").Lerp(new Color("e0443a"), Mathf.Clamp((float)sim.Radicalization * 2.5f, 0, 1));
        else // political map: each state's colour, muted so the region stands out
            c = Color.FromString(s.Color, new Color("777777")).Darkened(TerrainShown ? 0.05f : 0.35f);
        return s.Id == SelectedState ? c.Lightened(0.25f) : c;
    }

    /// <summary>Draws the world under everything else. Returns false if there is no world layer.</summary>
    private bool DrawGlobe(Font font)
    {
        if (Globe_Layout() is not { } g) return false;
        if (_meshVerts == null) BuildGlobeMesh(g);
        ColorGlobeMesh();

        // ToScreen is affine: layout * s + o. The land layer draws the fills, under everything here.
        float s = _fitScale * _zoom;
        var o = (_fitOffset - Size / 2) * _zoom + Size / 2 + _pan;
        if (!LandActive)
        {
            DrawSetTransform(o, 0, new Vector2(s, s));
            DrawMesh(_globeMesh!, null);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        var view = new Rect2(-o / s, Size / s); // the visible area, in layout space
        bool InRegion(Vector2 a, Vector2 b) => _zoom >= DetailZoom && g.Region.HasPoint((a + b) / 2);
        if (Generated)
        {
            // The region draws its own coast; neighbours' borders are drawn except along the artificial edge.
            const float eps = 0.5f;
            bool OnEdge(Vector2 a, Vector2 b) =>
                (Mathf.Abs(a.X - g.Region.Position.X) < eps && Mathf.Abs(b.X - g.Region.Position.X) < eps) ||
                (Mathf.Abs(a.X - g.Region.End.X) < eps && Mathf.Abs(b.X - g.Region.End.X) < eps) ||
                (Mathf.Abs(a.Y - g.Region.Position.Y) < eps && Mathf.Abs(b.Y - g.Region.Position.Y) < eps) ||
                (Mathf.Abs(a.Y - g.Region.End.Y) < eps && Mathf.Abs(b.Y - g.Region.End.Y) < eps);
            foreach (var (line, box) in g.Borders)
                if (box.Intersects(view)) DrawCulled(line, WorldBorder, 1.6f, OnEdge);
            return true;
        }
        foreach (var (line, box) in g.Coasts)
            if (box.Intersects(view)) DrawCulled(line, WorldCoast, 1.2f, InRegion);
        // Borders as on Victoria 3's map: a broad soft shadow under a firm line with a light edge.
        bool far = _zoom < DetailZoom;
        foreach (var (line, box) in g.Borders)
            if (box.Intersects(view))
            {
                DrawCulled(line, new Color(0, 0, 0, 0.22f), far ? 3f : 5.5f, InRegion);
                DrawCulled(line, new Color(0.08f, 0.07f, 0.06f, 0.75f), far ? 1.2f : 2f, InRegion);
                if (!far) DrawCulled(line, new Color(0.95f, 0.9f, 0.78f, 0.28f), 0.7f, InRegion);
            }
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

    /// <summary>Wars under way: a red dashed line between the capitals of the warring states.</summary>
    private void DrawWars()
    {
        if (World is not { } w || Globe is null || w.Wars.Count == 0) return;
        Vector2? Capital(string id) => Globe.State(id)?.Capital is { } c ? ToScreen(_project(new Vector2((float)c.Lon, (float)c.Lat))) : null;
        var war = new Color("e0443a");
        foreach (var x in w.Wars.Where(x => !x.Over))
            foreach (var a in x.SideA)
                foreach (var b in x.SideB)
                    if (Capital(a) is { } pa && Capital(b) is { } pb)
                    {
                        DrawDashedLine(pa, pb, new Color(war, 0.75f), 2, 7);
                        DrawCircle(pa, 3, war);
                        DrawCircle(pb, 3, war);
                    }
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
    /// <summary>
    /// Where a country's name runs, as on Victoria 3's map: along the shape's longest axis (from
    /// the outline's spread, tilted no more than 35°) through its middle, as long as the shape is.
    /// </summary>
    private static (Vector2 At, float Angle, float Length) NameLine(Vector2[] ring, Rect2 box)
    {
        var c = ring.Aggregate(Vector2.Zero, (a, b) => a + b) / ring.Length;
        if (!Geometry2D.IsPointInPolygon(c, ring)) c = box.GetCenter();
        float sxx = 0, syy = 0, sxy = 0;
        foreach (var p in ring)
        {
            var d = p - c;
            sxx += d.X * d.X; syy += d.Y * d.Y; sxy += d.X * d.Y;
        }
        float angle = 0.5f * Mathf.Atan2(2 * sxy, sxx - syy);
        angle = Mathf.Clamp(Mathf.Wrap(angle, -Mathf.Pi / 2, Mathf.Pi / 2), -0.61f, 0.61f);
        var dir = Vector2.Right.Rotated(angle);
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var p in ring)
        {
            float t = (p - c).Dot(dir);
            lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t);
        }
        return (c + dir * (lo + hi) / 2, angle, hi - lo);
    }

    /// <summary>
    /// Country names across their territory: spaced capitals in the title face, sized to the
    /// country, along its main axis with a gentle bow, faint enough to read through.
    /// </summary>
    private void DrawGlobeLabels(Font font, List<Rect2> taken)
    {
        if (Globe_Layout() is not { } g || Globe is null) return;
        var face = UiTheme.Display;
        float s = _fitScale * _zoom;
        var view = new Rect2(Vector2.Zero, Size);
        foreach (var state in Globe.States.OrderByDescending(st => g.Labels[st.Id].Area))
        {
            var (at, angle, length) = g.NameLines[state.Id];
            float span = length * s * 0.72f; // leave the ends of the country clear
            if (span < 60) continue;
            var text = StateText.Country(World?.States.FirstOrDefault(x => x.Id == state.Id)?.Name ?? state.Name).ToUpperInvariant();
            if (text.Length == 0) continue;
            // Size from the room: wide countries get big, airy names.
            float probe = face.GetStringSize(text, HorizontalAlignment.Left, -1, 20).X;
            int size = (int)Mathf.Clamp(span / (probe / 20 + 0.6f * (text.Length - 1) / 1) * 1.0f, 11, 34);
            float plain = face.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            float tracking = Mathf.Clamp((span - plain) / Mathf.Max(1, text.Length - 1), size * 0.08f, size * 0.9f);
            float total = plain + tracking * (text.Length - 1);
            if (total > span * 1.15f && size == 11) continue; // no room even small
            var centre = ToScreen(at);
            var dir = Vector2.Right.Rotated(angle);
            var normal = dir.Orthogonal();
            var bounds = new Rect2(centre, Vector2.Zero).Grow(size).Expand(centre + dir * total / 2).Expand(centre - dir * total / 2);
            if (!view.Intersects(bounds) || taken.Any(t => t.Intersects(bounds))) continue;
            taken.Add(bounds);
            // Cream over the dark sea and lands; brown ink on the paper chart.
            float paper = PaperBlend();
            var ink = new Color(1, 0.97f, 0.88f, 0.62f).Lerp(new Color(0.24f, 0.17f, 0.10f, 0.72f), paper);
            var shade = new Color(0, 0, 0, 0.45f).Lerp(new Color(1, 0.96f, 0.85f, 0.35f), paper);
            float x = -total / 2;
            float bow = total * 0.06f; // a slight arch, highest in the middle
            foreach (char ch in text)
            {
                string glyph = ch.ToString();
                float w = face.GetStringSize(glyph, HorizontalAlignment.Left, -1, size).X;
                float mid = x + w / 2;
                float u = mid / (total / 2); // -1..1 along the name
                var pos = centre + dir * mid - normal * bow * (1 - u * u);
                float tilt = angle + Mathf.Atan(2 * bow * u / Mathf.Max(1, total / 2)) * 0.9f;
                DrawSetTransform(pos, tilt, Vector2.One);
                var origin = new Vector2(-w / 2, size * 0.35f);
                DrawString(face, origin + new Vector2(0, 1.5f), glyph, HorizontalAlignment.Left, -1, size, shade);
                DrawString(face, origin, glyph, HorizontalAlignment.Left, -1, size, ink);
                x += w + tracking;
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
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
        var sim = World?.States.FirstOrDefault(x => x.Id == s.Id);
        var lines = new List<string>
        {
            sim?.Name ?? s.Name,
            $"{char.ToUpperInvariant(s.Ideology[0])}{s.Ideology[1..]} · capital {s.Capital?.Name ?? "—"}",
        };
        if (sim != null)
        {
            lines.Add($"{char.ToUpperInvariant(StateText.Stance(sim.Stance)[0])}{StateText.Stance(sim.Stance)[1..]} toward you ({sim.Stance:+0.00;-0.00}) · {StateText.Actions(sim.Actions)}");
            var wars = World!.Wars.Where(x => !x.Over && x.Involves(sim.Id)).ToList();
            if (wars.Count > 0)
                lines.Add("At war with " + string.Join(", ", wars.SelectMany(x => x.Enemies(sim.Id)).Distinct()
                    .Select(id => World.States.FirstOrDefault(o => o.Id == id)?.Name ?? id)) + $" · weariness {sim.Weariness:P0}");
            lines.Add($"Workers radicalized {sim.Radicalization:P0} · weight {sim.Weight:0.0}"
                      + (sim.Uprising > 0 ? $" · REVOLUTION under way ({World!.Turns(sim.Uprising)})" : ""));
        }
        else if (World?.Scenario.HomeState == s.Id) lines.Add("Your federation's own country");
        float width = lines.Max(l => font.GetStringSize(l, HorizontalAlignment.Left, -1, 13).X) + 20;
        float height = lines.Count * 17 + 12;
        var at = _mouse + new Vector2(16, 16);
        if (at.X + width > Size.X) at.X = _mouse.X - width - 8;
        if (at.Y + height > Size.Y) at.Y = _mouse.Y - height - 8;
        DrawStyleBox(UiTheme.FramePanel(0), new Rect2(at, new Vector2(width, height)).Grow(4)); // the same gold-rimmed frame as the interface's tooltips
        for (int i = 0; i < lines.Count; i++)
            DrawString(font, at + new Vector2(10, 20 + i * 17), lines[i], HorizontalAlignment.Left, -1, i == 0 ? 15 : 13, i == 0 ? Colors.White : Label);
    }
}
