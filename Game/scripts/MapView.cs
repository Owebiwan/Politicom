using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

public enum MapMode { Trust, Education, Values, Wellbeing }

/// <summary>
/// The map. With real geography (<see cref="GeoData"/>), communes sit at their true positions
/// over sea, land, rivers and borders, and each territory is a Voronoi cell clipped to the land
/// and to the federation's outline (or a real boundary, as for Paris). Generated maps without
/// geography fall back to relaxed positions and rounded cells. Territories are filled by the map
/// mode over a tint for the land; occupied land takes the occupier's colour; front borders are
/// drawn in the enemy's colour; the trust network is an overlay.
/// </summary>
public partial class MapView : Control
{
    private const float Margin = 70;
    private const float LegendHeight = 52;
    private static readonly Color Highlight = new("f2c94c");
    private static readonly Color Background = new("101216");
    private static readonly Color Border = new("0b0c0f");
    private static readonly Color FarmLand = new("3b4a35");
    private static readonly Color MixedLand = new("4a4838");
    private static readonly Color Industry = new("44464e");
    private static readonly Color Label = new(0.85f, 0.86f, 0.88f);
    private static readonly Color Muted = new(0.45f, 0.47f, 0.5f);
    private static readonly Color Danger = new(0.92f, 0.3f, 0.25f);
    private static readonly Color Warn = new(0.95f, 0.75f, 0.25f);
    private static readonly Color Decision = new(0.3f, 0.85f, 0.95f);
    private static readonly Color Sea = new("122030");
    private static readonly Color Coast = new("3a5a78");
    private static readonly Color NeutralLand = new("2e3134");
    private static readonly Color River = new("3b6890");
    private static readonly Color CountryBorder = new("6b7178");

    private int? _hover;
    private Vector2 _mouse;
    private double _time;
    private float _zoom = 1;
    private Vector2 _pan;
    private bool _dragging;
    private Vector2 _dragFrom;

    private World? _world;

    /// <summary>Setting a new world (another scenario, a loaded save) discards the cached layout and view.</summary>
    public World? World
    {
        get => _world;
        set
        {
            if (ReferenceEquals(value, _world)) return;
            _world = value;
            Invalidate();
            _zoom = 1;
            _pan = Vector2.Zero;
            _hover = null;
            QueueRedraw();
        }
    }

    private GeoData? _geo;

    /// <summary>Real geography for the current scenario, or null for a generated map.</summary>
    public GeoData? Geo
    {
        get => _geo;
        set
        {
            if (ReferenceEquals(value, _geo)) return;
            _geo = value;
            Invalidate();
            QueueRedraw();
        }
    }

    private void Invalidate()
    {
        _layout = null;
        _cells = null;
    }

    public MapMode Mode { get; set; } = MapMode.Trust;
    public bool ShowTrustLinks { get; set; } = true;
    public int? Selected { get; set; }
    public int? Secondary { get; set; }
    public string? Banner { get; set; }

    /// <summary>Commune id, and whether shift was held (secondary selection).</summary>
    public event Action<int, bool>? CommuneClicked;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (World is { } w && (w.PendingSecessions.Count > 0 || w.Ally.OfferPending)) QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            // Wheel or pinch zooms; drag or two-finger scroll pans; double-click empty space resets.
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } up:
                ZoomAt(up.Position, 1.15f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } down:
                ZoomAt(down.Position, 1 / 1.15f);
                break;
            case InputEventMagnifyGesture pinch:
                ZoomAt(pinch.Position, pinch.Factor);
                break;
            case InputEventPanGesture scroll:
                _pan -= scroll.Delta * 12;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right or MouseButton.Middle } button:
                if (!button.Pressed)
                    _dragging = false;
                else if (button.ButtonIndex == MouseButton.Left && Hit(button.Position) is int id)
                    CommuneClicked?.Invoke(id, button.ShiftPressed);
                else if (button.DoubleClick)
                {
                    _zoom = 1;
                    _pan = Vector2.Zero;
                    QueueRedraw();
                }
                else
                {
                    _dragging = true;
                    _dragFrom = button.Position;
                }
                break;
            case InputEventMouseMotion motion:
                if (_dragging)
                {
                    _pan += motion.Position - _dragFrom;
                    _dragFrom = motion.Position;
                    QueueRedraw();
                }
                _mouse = motion.Position;
                var hit = Hit(motion.Position);
                if (hit != _hover)
                {
                    _hover = hit;
                    QueueRedraw();
                }
                return;
            default:
                return;
        }
        AcceptEvent();
    }

    public override void _Draw()
    {
        // A city map (real boundaries, no coast) sits on land; a region sits on the sea.
        DrawRect(new Rect2(Vector2.Zero, Size), Geo == null ? Background : Geo.Territories.Count > 0 ? NeutralLand : Sea);
        if (World is not { } w) return;
        var font = ThemeDB.FallbackFont;
        var cells = Cells();
        var screen = cells.Select(pieces => pieces.Select(piece => piece.Select(ToScreen).ToArray()).ToArray()).ToArray();

        if (_geoLayout is { } g)
        {
            foreach (var land in g.LandFill) DrawColoredPolygon(Screen(land), NeutralLand);
            for (int r = 0; r < g.Land.Count; r++)
            {
                var ring = Screen(g.Land[r]);
                var raw = Geo!.Land[r];
                for (int k = 0; k < ring.Length; k++)
                {
                    int n = (k + 1) % ring.Length;
                    if (!Geo.OnBounds(raw[k], raw[n])) DrawLine(ring[k], ring[n], Coast, 1.3f, true);
                }
            }
            foreach (var lake in g.Lakes) DrawColoredPolygon(Screen(lake), Sea);
        }

        // Territories
        foreach (var c in w.Communes)
        {
            var fill = Fill(w, c);
            if (c.Id == _hover) fill = fill.Lightened(0.12f);
            foreach (var piece in screen[c.Id]) DrawColoredPolygon(piece, fill);
        }

        if (_geoLayout is { } g2)
        {
            float riverWidth = 1.4f * Mathf.Sqrt(_zoom);
            foreach (var (_, points) in g2.Rivers) DrawPolyline(Screen(points), River, riverWidth, true);
            foreach (var border in g2.Borders)
            {
                var b = Screen(border);
                for (int k = 0; k + 1 < b.Length; k++) DrawDashedLine(b[k], b[k + 1], CountryBorder, 1.2f, 5);
            }
        }

        foreach (var c in w.Communes)
            foreach (var piece in screen[c.Id]) Outline(piece, Border, 1.5f);

        // Overlays on the borders: fronts, secession, selection
        foreach (var c in w.Communes)
            foreach (var piece in screen[c.Id])
            {
                if (c.OccupiedBy is int by) Outline(piece, new Color(EnemyColor(w, by), 0.7f), 1.5f);
                else if (c.Front) Outline(piece, EnemyColor(w, c.Fronts.Min()), 3);
                if (!c.Occupied && !c.Federated) DashedOutline(piece, Warn, 2);
                if (w.PendingSecessions.Any(s => s.Commune == c.Id))
                    Outline(piece, new Color(Decision, 0.55f + 0.45f * Mathf.Sin((float)_time * 5)), 3);
            }
        if (Secondary is int sec) foreach (var piece in screen[sec]) DashedOutline(piece, Highlight, 2);
        if (Selected is int sel) foreach (var piece in screen[sel]) Outline(piece, Highlight, 3.5f);

        if (ShowTrustLinks)
            foreach (var (a, b) in w.Trust.Edges())
            {
                var ca = w.Communes[a];
                var cb = w.Communes[b];
                if (ca.Occupied || cb.Occupied) continue;
                float t = (float)w.Trust[a, b];
                var color = new Color(TrustColor(t), ca.Federated && cb.Federated ? 0.75f : 0.3f);
                DrawLine(ToScreen(ca), ToScreen(cb), color, 1 + 3 * t, true);
            }

        // Towns: every marker, but only the labels that fit. The selected and hovered commune
        // win, then larger towns; zooming in makes room for the rest.
        foreach (var c in w.Communes)
        {
            var p = ToScreen(c);
            float r = Marker(c);
            if (c.Occupied)
            {
                var occupier = c.OccupiedBy is int by ? EnemyColor(w, by) : Danger;
                DrawLine(p + new Vector2(-r, -r), p + new Vector2(r, r), occupier, 2, true);
                DrawLine(p + new Vector2(-r, r), p + new Vector2(r, -r), occupier, 2, true);
            }
            else
            {
                DrawCircle(p, r + 1.5f, Border);
                DrawCircle(p, r, c.Kind == CommuneKind.Industrial ? new Color("e9e4d8") : new Color("cfd6c4"));
                if (c.Starving) DrawCircle(p + new Vector2(r + 2, -r - 2), 3.5f, Warn);
            }
        }
        var taken = new List<Rect2>();
        var order = w.Communes.OrderByDescending(c => c.Id == Selected || c.Id == _hover).ThenByDescending(c => c.Population);
        foreach (var c in order)
        {
            var p = ToScreen(c);
            float r = Marker(c);
            var labelAt = p + new Vector2(r + 5, 4);
            var size = font.GetStringSize(c.Name, HorizontalAlignment.Left, -1, 12);
            var rect = new Rect2(labelAt - new Vector2(0, size.Y - 3), size + new Vector2(0, c.Front && !c.Occupied ? 9 : 0));
            bool always = c.Id == Selected || c.Id == _hover;
            if (!always && taken.Any(t => t.Intersects(rect))) continue;
            taken.Add(rect);

            DrawStringOutline(font, labelAt, c.Name, HorizontalAlignment.Left, -1, 12, 4, Border);
            DrawString(font, labelAt, c.Name, HorizontalAlignment.Left, -1, 12, c.Occupied ? Muted : Label);
            if (c.Front && !c.Occupied)
            {
                // Control meter under the name: held part light, lost part in the enemy's colour.
                var bar = new Rect2(labelAt + new Vector2(0, 5), new Vector2(34, 4));
                DrawRect(bar.Grow(1), Border);
                DrawRect(bar, EnemyColor(w, c.Fronts.Min()));
                DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * (float)c.Control, bar.Size.Y)), new Color("e9e4d8"));
            }
        }

        DrawLegend(font);
        if (_hover is int h) DrawTooltip(font, w, w.Communes[h]);
        if (Banner is { } banner)
        {
            var size = font.GetStringSize(banner, HorizontalAlignment.Left, -1, 28);
            var at = new Vector2((Size.X - size.X) / 2, 48);
            DrawRect(new Rect2(at - new Vector2(16, 34), size + new Vector2(32, 20)), new Color(0, 0, 0, 0.75f));
            DrawString(font, at, banner, HorizontalAlignment.Left, -1, 28, Colors.White);
        }
    }

    private Vector2[] Screen(Vector2[] layout) => layout.Select(ToScreen).ToArray();

    /// <summary>Territory colour: the map mode over a tint for the kind of land; occupied land takes the occupier's colour.</summary>
    private Color Fill(World w, Commune c)
    {
        if (c.OccupiedBy is int by) return EnemyColor(w, by).Darkened(0.55f) with { A = 0.85f };
        var land = c.Kind switch { CommuneKind.Agrarian => FarmLand, CommuneKind.Industrial => Industry, _ => MixedLand };
        var color = land.Lerp(NodeColor(w, c), 0.75f);
        if (!c.Federated) color = color.Lerp(new Color(color.V, color.V, color.V), 0.7f).Darkened(0.25f); // seceded: greyed
        return color;
    }

    private void Outline(Vector2[] poly, Color color, float width)
    {
        var closed = new Vector2[poly.Length + 1];
        poly.CopyTo(closed, 0);
        closed[^1] = poly[0];
        DrawPolyline(closed, color, width, true);
    }

    private void DashedOutline(Vector2[] poly, Color color, float width)
    {
        for (int i = 0; i < poly.Length; i++) DrawDashedLine(poly[i], poly[(i + 1) % poly.Length], color, width, 6);
    }

    private Color NodeColor(World w, Commune c)
    {
        switch (Mode)
        {
            case MapMode.Trust:
                return TrustColor((float)w.Trust.AverageTrust(c.Id));
            case MapMode.Education:
                // Hue shows the balance (orange = experts ahead, violet = political ahead); brightness shows the total.
                float total = (float)(c.PracticalEducation + c.PoliticalEducation) / 2;
                float balance = (float)(c.PracticalEducation - c.PoliticalEducation);
                var hue = balance > 0 ? new Color(0.95f, 0.55f, 0.15f) : new Color(0.6f, 0.4f, 0.95f);
                var mix = new Color(0.6f, 0.6f, 0.6f).Lerp(hue, Mathf.Clamp(Mathf.Abs(balance) * 4, 0, 1));
                return mix.Darkened(0.6f * (1 - Mathf.Clamp(total * 1.6f, 0, 1)));
            case MapMode.Values:
                return new Color(
                    (float)CommuneValues.To01(c.Values.Collectivism),
                    (float)CommuneValues.To01(c.Values.Federalism),
                    (float)CommuneValues.To01(c.Values.Militancy));
            default:
                return TrustColor((float)c.Wellbeing);
        }
    }

    private static Color EnemyColor(World w, int enemy) =>
        Color.FromString(w.Enemies[enemy].Color, Danger);

    // Rust → sand → sage: a diverging scale that stays readable when blended over the land tint.
    private static readonly Color Low = new("b4533e");
    private static readonly Color Mid = new("cdb46e");
    private static readonly Color High = new("5f9e6a");

    /// <summary>Most trust and wellbeing values sit between 0.3 and 0.9, so stretch that range across the scale.</summary>
    private static Color TrustColor(float t)
    {
        t = Mathf.Clamp((t - 0.3f) / 0.6f, 0, 1);
        return t < 0.5f ? Low.Lerp(Mid, t * 2) : Mid.Lerp(High, (t - 0.5f) * 2);
    }

    private void DrawLegend(Font font)
    {
        string text = Mode switch
        {
            MapMode.Trust => "Trust — rust low, sand middling, green high. Lines: trust between neighbours.",
            MapMode.Education => "Education — orange: practical ahead (expert trap), violet: political ahead. Darker = less educated.",
            MapMode.Values => "Values — red: collectivist, green: federalist, blue: militant.",
            _ => "Wellbeing — rust low, sand middling, green high.",
        };
        DrawRect(new Rect2(0, Size.Y - LegendHeight, Size.X, LegendHeight), new Color(Background, 0.88f));
        DrawString(font, new Vector2(14, Size.Y - 34), text, HorizontalAlignment.Left, -1, 13, Muted);
        DrawString(font, new Vector2(14, Size.Y - 14),
            "Coloured border: front (the bar shows control). Enemy colour: occupied. Dashed yellow: seceded. Pulsing blue: leaving. T: trust links.",
            HorizontalAlignment.Left, -1, 13, Muted);
    }

    private void DrawTooltip(Font font, World w, Commune c)
    {
        string status = c.OccupiedBy is int by ? $"Occupied by the {w.Enemies[by].Name}" : c.Occupied ? "Occupied" : !c.Federated ? "Seceded" : c.Front ? $"Front — control {c.Control:P0}" : "Federated";
        var lines = new[]
        {
            $"{c.Name}",
            $"{c.Kind}, {c.Population:0}k · {status}",
            $"Trust {w.Trust.AverageTrust(c.Id):P0} · wellbeing {c.Wellbeing:P0}",
            $"Education: practical {c.PracticalEducation:0.00}, political {c.PoliticalEducation:0.00}",
            $"Food {Math.Max(0, c.Food):0} · goods {Math.Max(0, c.Goods):0}" + (c.Starving ? " · STARVING" : ""),
            $"Discontent {c.Discontent:0.00}" + (c.FreeRiding > 0.2 ? $" · free-riding {c.FreeRiding:P0}" : ""),
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

    // ── Projection ──────────────────────────────────────────────────────────
    // With geography: an equirectangular projection (longitude scaled by the cosine of the mean
    // latitude), fitted so the federation and a margin of its surroundings fill the pane.
    // Without: the normalised positions from the simulation, fitted the same way.

    private Vector2[]? _layout;
    private Vector2 _layoutSize;
    private Func<Vector2, Vector2> _project = v => v; // lon/lat (or normalised position) → layout space

    /// <summary>Real geography projected into layout space, cached with the layout.</summary>
    /// <param name="LandFill">Land as simple polygons that can be filled; <paramref name="Land"/> keeps the original coastline for outlines and clipping.</param>
    private sealed record GeoLayout(List<Vector2[]> Land, List<Vector2[]> LandFill, List<Vector2[]> Lakes, List<Vector2[]> Borders, List<(string Name, Vector2[] Points)> Rivers);
    private GeoLayout? _geoLayout;

    private Vector2 ToScreen(Commune c) => ToScreen(Layout()[c.Id]);

    // Fit for generated maps: their rounded territories reach beyond their towns, so after
    // computing them the map is scaled to fit. Geographic maps are fitted by the projection.
    private float _fitScale = 1;
    private Vector2 _fitOffset;

    private Vector2 ToScreen(Vector2 layout) => (layout * _fitScale + _fitOffset - Size / 2) * _zoom + Size / 2 + _pan;

    private Vector2 ToLayout(Vector2 screen) => ((screen - Size / 2 - _pan) / _zoom + Size / 2 - _fitOffset) / _fitScale;

    private Rect2 Drawable => new(16, 16, Size.X - 16 - 110, Size.Y - 16 - LegendHeight - 8);

    private Vector2[] Layout()
    {
        if (_layout != null && _layoutSize == Size) return _layout;
        var w = World!;
        _layoutSize = Size;
        _cells = null;
        _fitScale = 1;
        _fitOffset = Vector2.Zero;

        if (Geo is { } geo)
        {
            float k = Mathf.Cos(Mathf.DegToRad((float)w.Communes.Average(c => c.Latitude)));
            Vector2 Raw(Vector2 lonLat) => new(lonLat.X * k, -lonLat.Y);
            var anchors = w.Communes.Select(c => Raw(new Vector2((float)c.Longitude, (float)c.Latitude)))
                .Concat(geo.Territories.Values.SelectMany(t => t.Select(Raw))).ToArray();
            var (scale, offset) = FitBox(anchors, margin: geo.Territories.Count > 0 ? 0.06f : 0.22f);
            _project = v => (Raw(v) - offset) * scale + Drawable.Position;
            var positions = w.Communes.Select(c => _project(new Vector2((float)c.Longitude, (float)c.Latitude))).ToArray();
            var land = geo.Land.Select(r => r.Select(_project).ToArray()).ToList();
            _geoLayout = new GeoLayout(
                land,
                land.SelectMany(Simple).ToList(),
                geo.Lakes.Select(r => r.Select(_project).ToArray()).ToList(),
                geo.Borders.Select(r => r.Select(_project).ToArray()).ToList(),
                geo.Rivers.Select(r => (r.Name, r.Points.Select(_project).ToArray())).ToList());
            return _layout = positions;
        }

        _geoLayout = null;
        var home = w.Communes.Select(c => new Vector2((float)c.X, (float)c.Y)).ToArray();
        var (s, o) = FitBox(home, margin: 0.04f);
        home = home.Select(v => (v - o) * s + Drawable.Position).ToArray();
        // Generated maps have no geography to respect, so crowded towns are pushed apart.
        var p = (Vector2[])home.Clone();
        for (int iter = 0; iter < 300; iter++)
        {
            for (int i = 0; i < p.Length; i++)
                for (int j = i + 1; j < p.Length; j++)
                {
                    var d = p[j] - p[i];
                    float min = Radius(w.Communes[i]) + Radius(w.Communes[j]) + 44;
                    float len = d.Length();
                    if (len >= min) continue;
                    var dir = len < 0.01f ? Vector2.Right.Rotated(i * 2.4f) : d / len;
                    var push = dir * (min - len) * 0.5f;
                    p[i] -= push;
                    p[j] += push;
                }
            for (int i = 0; i < p.Length; i++) p[i] += (home[i] - p[i]) * 0.012f;
        }
        return _layout = p;
    }

    /// <summary>
    /// Simplified coastlines can cross themselves slightly, which the triangulator rejects.
    /// Clipping a polygon against a large rectangle runs it through Clipper, which returns
    /// simple polygons that always triangulate.
    /// </summary>
    private static IEnumerable<Vector2[]> Simple(Vector2[] ring)
    {
        var min = new Vector2(ring.Min(p => p.X), ring.Min(p => p.Y)) - Vector2.One;
        var max = new Vector2(ring.Max(p => p.X), ring.Max(p => p.Y)) + Vector2.One;
        var box = new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) };
        return Geometry2D.IntersectPolygons(ring, box).Where(p => p.Length >= 3);
    }

    /// <summary>Scale and origin that fit the points, plus a relative margin, into the drawable area.</summary>
    private (float Scale, Vector2 Origin) FitBox(Vector2[] points, float margin)
    {
        var min = new Vector2(points.Min(p => p.X), points.Min(p => p.Y));
        var max = new Vector2(points.Max(p => p.X), points.Max(p => p.Y));
        var pad = (max - min) * margin;
        min -= pad;
        max += pad;
        var extent = max - min;
        var area = Drawable;
        float scale = Mathf.Min(area.Size.X / Mathf.Max(extent.X, 1e-6f), area.Size.Y / Mathf.Max(extent.Y, 1e-6f));
        // Centre the box in the area.
        var origin = min - (area.Size / scale - extent) / 2;
        return (scale, origin);
    }

    private void FitToTerritories(Vector2[][][] cells)
    {
        var all = cells.SelectMany(c => c).SelectMany(p => p).ToArray();
        if (all.Length == 0) return;
        var min = new Vector2(all.Min(p => p.X), all.Min(p => p.Y));
        var max = new Vector2(all.Max(p => p.X), all.Max(p => p.Y));
        var area = Drawable;
        var extent = max - min;
        _fitScale = Mathf.Min(1, Mathf.Min(area.Size.X / extent.X, area.Size.Y / extent.Y));
        _fitOffset = area.Position + (area.Size - extent * _fitScale) / 2 - min * _fitScale;
    }

    // ── Territories ─────────────────────────────────────────────────────────

    private Vector2[][][]? _cells;

    /// <summary>
    /// Each commune's territory, as one or more polygons in layout space. Real boundaries are used
    /// where the scenario has them. Otherwise each starts as a disk around its commune and is
    /// clipped by the perpendicular bisector with every other commune, so neighbours share exact
    /// borders; with geography it is then clipped to the land and to the federation's outline
    /// (the communes' convex hull, rounded outward), so coasts are real and the edge is smooth.
    /// </summary>
    private Vector2[][][] Cells()
    {
        var pts = Layout();
        if (_cells != null) return _cells;
        var w = World!;
        var nearest = pts.Select((p, i) => pts.Where((_, j) => j != i).Min(q => p.DistanceTo(q))).OrderBy(d => d).ToArray();
        float median = nearest[nearest.Length / 2];
        float reach = median * (Geo != null ? 2.2f : 1.3f);

        Vector2[]? outline = null;
        if (Geo != null)
        {
            var hull = Geometry2D.ConvexHull(pts);
            var grown = Geometry2D.OffsetPolygon(hull, median * 0.9f, Geometry2D.PolyJoinType.Round);
            outline = grown.Count > 0 ? grown.OrderByDescending(Area).First() : hull;
        }

        var cells = new Vector2[pts.Length][][];
        for (int i = 0; i < pts.Length; i++)
        {
            if (Geo != null && Geo.Territories.TryGetValue(w.Communes[i].Name, out var real))
            {
                cells[i] = new[] { real.Select(_project).ToArray() };
                continue;
            }
            var poly = Enumerable.Range(0, 48).Select(k => pts[i] + Vector2.Right.Rotated(k * Mathf.Tau / 48) * reach).ToList();
            for (int j = 0; j < pts.Length && poly.Count > 0; j++)
                if (j != i) poly = ClipToward(poly, pts[i], pts[j]);
            var pieces = new List<Vector2[]> { poly.ToArray() };
            if (outline != null && _geoLayout != null)
            {
                pieces = pieces.SelectMany(p => Geometry2D.IntersectPolygons(p, outline)).ToList();
                pieces = pieces.SelectMany(p => _geoLayout.Land.SelectMany(land => Geometry2D.IntersectPolygons(p, land))).ToList();
                foreach (var lake in _geoLayout.Lakes)
                    pieces = pieces.SelectMany(p => Geometry2D.ClipPolygons(p, lake)).ToList();
                // Clipping can return holes (clockwise); with these shapes they are slivers, so drop them.
                pieces = pieces.Where(p => p.Length >= 3 && !Geometry2D.IsPolygonClockwise(p) && Area(p) > 4).ToList();
            }
            cells[i] = pieces.Where(p => p.Length >= 3).ToArray();
        }
        if (Geo == null) FitToTerritories(cells);
        return _cells = cells;
    }

    private static float Area(Vector2[] poly)
    {
        float a = 0;
        for (int i = 0; i < poly.Length; i++) a += poly[i].Cross(poly[(i + 1) % poly.Length]);
        return Mathf.Abs(a) / 2;
    }

    /// <summary>Sutherland–Hodgman: keep the part of the polygon closer to <paramref name="a"/> than to <paramref name="b"/>.</summary>
    private static List<Vector2> ClipToward(List<Vector2> poly, Vector2 a, Vector2 b)
    {
        var normal = b - a;
        var mid = (a + b) / 2;
        float Side(Vector2 p) => (p - mid).Dot(normal);
        var result = new List<Vector2>(poly.Count + 2);
        for (int k = 0; k < poly.Count; k++)
        {
            var cur = poly[k];
            var next = poly[(k + 1) % poly.Count];
            float sc = Side(cur), sn = Side(next);
            if (sc <= 0) result.Add(cur);
            if ((sc < 0 && sn > 0) || (sc > 0 && sn < 0)) result.Add(cur + (next - cur) * (sc / (sc - sn)));
        }
        return result;
    }

    /// <summary>Centres the map on a commune, keeping the current zoom.</summary>
    public void CenterOn(int commune)
    {
        Cells();
        _pan = -(Layout()[commune] * _fitScale + _fitOffset - Size / 2) * _zoom;
        QueueRedraw();
    }

    /// <summary>Centres the map on a commune at the given zoom.</summary>
    public void FocusOn(int commune, float zoom)
    {
        _zoom = Mathf.Clamp(zoom, 0.6f, 5f);
        Cells();
        _pan = -(Layout()[commune] * _fitScale + _fitOffset - Size / 2) * _zoom;
        QueueRedraw();
    }

    private void ZoomAt(Vector2 at, float factor)
    {
        float next = Mathf.Clamp(_zoom * factor, 0.6f, 5f);
        var anchor = (at - Size / 2 - _pan) / _zoom;
        _pan = at - Size / 2 - anchor * next;
        _zoom = next;
        QueueRedraw();
    }

    private static float Radius(Commune c) => 5 + Mathf.Sqrt((float)c.Population) * 1.2f;

    /// <summary>Town marker size: small, growing gently with population.</summary>
    private static float Marker(Commune c) => 2.5f + Mathf.Sqrt((float)c.Population) * 0.45f;

    /// <summary>The commune whose territory contains the point.</summary>
    private int? Hit(Vector2 at)
    {
        if (World is null) return null;
        var p = ToLayout(at);
        var cells = Cells();
        for (int i = 0; i < cells.Length; i++)
            if (cells[i].Any(piece => Geometry2D.IsPointInPolygon(p, piece))) return i;
        return null;
    }
}
