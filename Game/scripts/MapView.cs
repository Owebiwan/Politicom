using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

public enum MapMode { Trust, Education, Values, Wellbeing, Ideology, Diplomacy, Revolution }

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
    private static readonly Color RailBed = new("17130f");
    private static readonly Color RailTies = new("cbbf9f");
    private static readonly Color Anarchist = new("c4473a");
    private static readonly Color ProvinceBorder = new("ffffff30");

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
        _armies.Clear();
    }

    // ── Armies ──────────────────────────────────────────────────────────────
    // The war is simulated as pressure on front communes, not as units. This layer draws that
    // pressure as armies: one per enemy per front commune, sized by the pressure it brings,
    // placed on the enemy's side. When a commune falls, the armies against its neighbours emerge
    // from it and slide forward, so an advance reads as movement.

    private sealed class Army
    {
        public Vector2 Position;
        public Vector2 Target;
        public float Strength;
        public float Alpha;
        public bool Alive;
    }

    private readonly Dictionary<(int Enemy, int Commune), Army> _armies = new();

    private void UpdateArmies(float delta)
    {
        if (World is not { } w) return;
        var pts = Layout();
        var members = w.Federation.ToList();
        var centroid = members.Count > 0 ? members.Aggregate(Vector2.Zero, (s, c) => s + pts[c.Id]) / members.Count : Size / 2;
        float spacing = MedianSpacing(pts);
        foreach (var a in _armies.Values) a.Alive = false;

        for (int k = 0; k < w.Enemies.Count; k++)
        {
            var e = w.Enemies[k];
            int enemy = k;
            var fronts = w.Active.Where(c => c.Fronts.Contains(enemy)).ToList();
            if (fronts.Count == 0 || e.EffectiveStrength <= 0.01) continue;
            float perFront = (float)(e.EffectiveStrength * w.Balance.PressureScale / fronts.Count);
            foreach (var c in fronts)
            {
                // Stand between the commune and the enemy: towards the land it has taken, or else outward.
                var taken = w.Trust.Neighbors(c.Id).Where(n => w.Communes[n].OccupiedBy == enemy).Select(n => pts[n]).ToList();
                var p = pts[c.Id];
                var away = (p - centroid).LengthSquared() < 1 ? Vector2.Right : (p - centroid).Normalized();
                var target = taken.Count > 0 ? p + (taken.Aggregate(Vector2.Zero, (s, v) => s + v) / taken.Count - p) * 0.5f : p + away * spacing * 0.55f;
                if (!_armies.TryGetValue((k, c.Id), out var army))
                {
                    // New front: the army comes out of the land it has just taken.
                    var from = taken.Count > 0 ? taken.OrderBy(v => v.DistanceSquaredTo(p)).First() : target;
                    army = _armies[(k, c.Id)] = new Army { Position = from };
                }
                army.Target = target;
                army.Strength = perFront;
                army.Alive = true;
            }
        }

        foreach (var key in _armies.Keys.ToList())
        {
            var a = _armies[key];
            a.Position += (a.Target - a.Position) * Mathf.Min(1, delta * 2.5f);
            a.Alpha = Mathf.MoveToward(a.Alpha, a.Alive ? 1 : 0, delta * 2);
            if (!a.Alive && a.Alpha <= 0) _armies.Remove(key);
        }
    }

    private static float MedianSpacing(Vector2[] pts)
    {
        if (pts.Length < 2) return 40;
        var nearest = pts.Select((p, i) => pts.Where((_, j) => j != i).Min(q => p.DistanceTo(q))).OrderBy(d => d).ToArray();
        return nearest[nearest.Length / 2];
    }

    /// <summary>NATO-style infantry symbol: a box with a cross, in the enemy's colour.</summary>
    private void DrawArmy(Vector2 at, float strength, Color color, float alpha, bool attacking, Vector2 towards, Font font)
    {
        float scale = Mathf.Clamp(0.75f + Mathf.Sqrt(strength) * 0.35f, 0.75f, 2f) * Mathf.Sqrt(_zoom);
        var size = new Vector2(22, 14) * scale;
        var box = new Rect2(at - size / 2, size);
        if (attacking)
        {
            // Pulse, and point at the commune under attack.
            float pulse = 0.5f + 0.5f * Mathf.Sin((float)_time * 6);
            var tip = at + (towards - at) * 0.8f;
            var dir = (towards - at).Normalized();
            DrawLine(at, tip, new Color(color, alpha * (0.5f + 0.5f * pulse)), 2.5f * scale, true);
            DrawColoredPolygon(new[] { tip + dir * 7 * scale, tip + dir.Orthogonal() * 4 * scale, tip - dir.Orthogonal() * 4 * scale },
                new Color(color, alpha * (0.5f + 0.5f * pulse)));
        }
        DrawRect(box, new Color(color.Darkened(0.6f), alpha * 0.9f));
        DrawRect(box, new Color(color, alpha), false, 1.5f);
        DrawLine(box.Position, box.End, new Color(color, alpha), 1.2f, true);
        DrawLine(new Vector2(box.Position.X, box.End.Y), new Vector2(box.End.X, box.Position.Y), new Color(color, alpha), 1.2f, true);
        DrawString(font, new Vector2(at.X - 20, box.End.Y + 11), $"{strength:0.0}", HorizontalAlignment.Center, 40, 10, new Color(color.Lightened(0.3f), alpha));
    }

    /// <summary>The federation's militia at a front commune: a black-and-red flag sized by its defense.</summary>
    private void DrawMilitia(Vector2 at, float defense, float alpha)
    {
        float scale = Mathf.Clamp(1.05f + Mathf.Sqrt(defense) * 0.35f, 1.05f, 2.1f) * Mathf.Sqrt(_zoom);
        var size = new Vector2(16, 11) * scale;
        var box = new Rect2(at - size / 2, size);
        DrawColoredPolygon(new[] { box.Position, new Vector2(box.End.X, box.Position.Y), new Vector2(box.Position.X, box.End.Y) }, new Color(Anarchist, alpha));
        DrawColoredPolygon(new[] { new Vector2(box.End.X, box.Position.Y), box.End, new Vector2(box.Position.X, box.End.Y) }, new Color(0.05f, 0.05f, 0.05f, alpha));
        DrawRect(box, new Color(1, 1, 1, alpha * 0.5f), false, 1);
    }

    public MapMode Mode { get; set; } = MapMode.Trust;
    public bool ShowTrustLinks { get; set; } = true;
    public int? Selected { get; set; }
    public int? Secondary { get; set; }
    public string? Banner { get; set; }

    /// <summary>Commune id, and whether shift was held (secondary selection).</summary>
    public event Action<int, bool>? CommuneClicked;

    /// <summary>A country clicked outside the federation.</summary>
    public event Action<string>? StateClicked;

    /// <summary>The country highlighted on the map (the one open in the World tab).</summary>
    public string? SelectedState { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (World is not { } w) return;
        UpdateArmies((float)delta);
        if (AtWorldZoom) QueueRedraw(); // the region marker pulses
        bool moving = _armies.Values.Any(a => a.Position.DistanceSquaredTo(a.Target) > 0.25f || (a.Alpha > 0 && a.Alpha < 1));
        bool marching = w.ActiveProjects.Any(p => p.Kind == ProjectKind.DefensiveCampaign) || w.OffensiveUnderway;
        if (moving || marching || w.PendingSecessions.Count > 0 || w.Ally.OfferPending) QueueRedraw();
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
                else if (button.ButtonIndex == MouseButton.Left && !AtWorldZoom && Hit(button.Position) is int id)
                    CommuneClicked?.Invoke(id, button.ShiftPressed);
                else if (button.ButtonIndex == MouseButton.Left && !button.DoubleClick && HitState(button.Position) is { } st)
                {
                    StateClicked?.Invoke(st.Id);
                    _dragging = true; // a click on a country can still start a drag
                    _dragFrom = button.Position;
                }
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
                var hit = AtWorldZoom ? null : Hit(motion.Position);
                var hoverState = hit is null ? HitState(motion.Position) : null;
                if (hoverState != _hoverState)
                {
                    _hoverState = hoverState;
                    QueueRedraw();
                }
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
        DrawRect(new Rect2(Vector2.Zero, Size), Geo == null ? Background : Globe != null || Geo.Territories.Count == 0 ? Sea : NeutralLand);
        if (World is not { } w) return;
        var font = ThemeDB.FallbackFont;
        // A generated map's land goes under its neighbouring countries; real maps' land comes from the world layer.
        if (Generated && _geoLayout is { } g0)
            foreach (var land in g0.LandFill) DrawColoredPolygon(Screen(land), NeutralLand);
        bool globe = DrawGlobe(font);
        bool detail = !AtWorldZoom;
        var cells = Cells();
        var screen = cells.Select(pieces => pieces.Select(piece => piece.Select(ToScreen).ToArray()).ToArray()).ToArray();

        if (_geoLayout is { } g)
        {
            if (!globe) foreach (var land in g.LandFill) DrawColoredPolygon(Screen(land), NeutralLand);
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

        // Railways: a dark bed with light cross-ties.
        if (detail) foreach (var (a, b) in w.Railways)
        {
            var pa = ToScreen(w.Communes[a]);
            var pb = ToScreen(w.Communes[b]);
            bool cut = w.Communes[a].Occupied || w.Communes[b].Occupied;
            DrawLine(pa, pb, new Color(RailBed, cut ? 0.4f : 0.95f), 4.5f, true);
            DrawDashedLine(pa, pb, new Color(RailTies, cut ? 0.25f : 0.8f), 2, 5);
        }

        if (_geoLayout is { } g2)
        {
            if (Geo!.DrawProvinces)
                for (int r = 0; r < g2.Provinces.Count; r++)
                {
                    var ring = Screen(g2.Provinces[r]);
                    var raw = Geo.Provinces[r].Ring;
                    for (int k = 0; k < ring.Length; k++)
                    {
                        int n = (k + 1) % ring.Length;
                        if (!Geo.OnBounds(raw[k], raw[n])) DrawDashedLine(ring[k], ring[n], ProvinceBorder, 1, 3);
                    }
                }
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

        if (!detail)
        {
            DrawRegionMarker();
            DrawGlobeLabels(font, new List<Rect2>());
            DrawLegend(font);
            if (_hoverState is { } hs) DrawStateTooltip(font, hs);
            DrawBanner(font);
            return;
        }
        if (globe && _zoom < 0.6f) DrawGlobeLabels(font, new List<Rect2>());

        // Militia columns marching to the front for each defensive campaign.
        foreach (var project in w.ActiveProjects.Where(p => p.Kind == ProjectKind.DefensiveCampaign))
        {
            var fronts = w.Federation.Where(c => c.Front).ToList();
            if (fronts.Count == 0) continue;
            foreach (var id in project.Participants)
            {
                var from = ToScreen(w.Communes[id]);
                var front = fronts.OrderBy(f => ToScreen(f).DistanceSquaredTo(from)).First();
                if (front.Id == id) continue;
                var to = ToScreen(front);
                DrawDashedLine(from, to, new Color(Anarchist, 0.35f), 1.5f, 4);
                for (int i = 0; i < 3; i++)
                {
                    float t = (float)((_time * 0.35 + i / 3.0 + id * 0.137) % 1.0);
                    var at = from.Lerp(to, t);
                    DrawCircle(at, 3.2f, new Color(0.05f, 0.05f, 0.05f));
                    DrawCircle(at, 2.2f, Anarchist);
                }
            }
        }

        // Armies on the enemy's side of each front, and the militia facing them.
        foreach (var ((enemy, commune), army) in _armies)
        {
            var c = w.Communes[commune];
            bool attacking = w.Enemies[enemy].CurrentOffensive != null && !c.Occupied;
            DrawArmy(ToScreen(army.Position), army.Strength, EnemyColor(w, enemy), army.Alpha, attacking, ToScreen(c), font);
        }
        var pts = Layout();
        var members = w.Federation.ToList();
        if (members.Count > 0)
        {
            var centroid = members.Aggregate(Vector2.Zero, (s, c) => s + pts[c.Id]) / members.Count;
            foreach (var c in w.Active.Where(c => c.Front))
            {
                var p = pts[c.Id];
                var toward = (centroid - p).LengthSquared() < 1 ? Vector2.Left : (centroid - p).Normalized();
                DrawMilitia(ToScreen(p + toward * MedianSpacing(pts) * 0.32f), (float)c.Defense, 0.95f);
            }
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
        else if (_hoverState is { } hs) DrawStateTooltip(font, hs);
        DrawBanner(font);
    }

    private void DrawBanner(Font font)
    {
        if (Banner is not { } banner) return;
        var size = font.GetStringSize(banner, HorizontalAlignment.Left, -1, 28);
        var at = new Vector2((Size.X - size.X) / 2, 48);
        DrawRect(new Rect2(at - new Vector2(16, 34), size + new Vector2(32, 20)), new Color(0, 0, 0, 0.75f));
        DrawString(font, at, banner, HorizontalAlignment.Left, -1, 28, Colors.White);
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
            case MapMode.Ideology:
                return Anarchist.Darkened(0.15f); // the federation itself
            case MapMode.Diplomacy or MapMode.Revolution:
                return new Color("a8946a"); // the federation: outside the scale of other states' attitudes
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
            MapMode.Ideology => "Ideology — blue liberal, khaki conservative, grey fascist, dark red communist, violet monarchy.",
            MapMode.Diplomacy => "Diplomacy — red hostile, grey neutral, green friendly toward the federation. Click a country for the World tab.",
            MapMode.Revolution => "Revolution — how radicalized each state's workers are: dark none, bright red near revolt.",
            _ => "Wellbeing — rust low, sand middling, green high.",
        };
        DrawRect(new Rect2(0, Size.Y - LegendHeight, Size.X, LegendHeight), new Color(Background, 0.88f));
        DrawString(font, new Vector2(14, Size.Y - 34), text, HorizontalAlignment.Left, -1, 13, Muted);
        DrawString(font, new Vector2(14, Size.Y - 14),
"Boxes: enemy armies (pressure). Black-red flags: militia (defense). Moving dots: militia columns. Rails: railways. Dashed yellow: seceded. T: trust links.",
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
    /// <param name="Region">The provinces merged into one shape (empty if the scenario has none).</param>
    private sealed record GeoLayout(List<Vector2[]> Land, List<Vector2[]> LandFill, List<Vector2[]> Lakes, List<Vector2[]> Borders,
        List<(string Name, Vector2[] Points)> Rivers, List<Vector2[]> Provinces, List<Vector2[]> Region);
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
        if (_layout != null && _layoutSize == Size && _layout.Length == World!.Communes.Count) return _layout;
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
                geo.Land.Select(r => geo.Extended(r).Select(_project).ToArray()).SelectMany(Simple).ToList(),
                geo.Lakes.Select(r => r.Select(_project).ToArray()).ToList(),
                geo.Borders.Select(r => r.Select(_project).ToArray()).ToList(),
                geo.Rivers.Select(r => (r.Name, r.Points.Select(_project).ToArray())).ToList(),
                geo.Provinces.Select(p => p.Ring.Select(_project).ToArray()).ToList(),
                Region(geo, positions.Take(w.InitialCommuneCount).ToArray()));
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

    /// <summary>
    /// The land the federation can hold: core provinces entire, plus contested provinces only
    /// within a rounded outline around the communes (the front).
    /// </summary>
    private List<Vector2[]> Region(GeoData geo, Vector2[] positions)
    {
        var core = geo.Provinces.Where(p => !p.Contested).Select(p => p.Ring.Select(_project).ToArray()).ToList();
        var contested = geo.Provinces.Where(p => p.Contested).Select(p => p.Ring.Select(_project).ToArray()).ToList();
        if (contested.Count == 0) return Union(core);
        var front = FrontOutline(positions);
        var held = Union(contested).SelectMany(p => Geometry2D.IntersectPolygons(p, front)).Where(p => !Geometry2D.IsPolygonClockwise(p));
        return Union(core.Concat(held));
    }

    /// <summary>The communes' convex hull grown outward and rounded: a smooth line just beyond the outermost towns.</summary>
    private static Vector2[] FrontOutline(Vector2[] positions)
    {
        var nearest = positions.Select((p, i) => positions.Where((_, j) => j != i).Min(q => p.DistanceTo(q))).OrderBy(d => d).ToArray();
        var hull = Geometry2D.ConvexHull(positions);
        var grown = Geometry2D.OffsetPolygon(hull, nearest[nearest.Length / 2] * 0.9f, Geometry2D.PolyJoinType.Round);
        return grown.Count > 0 ? grown.OrderByDescending(Area).First() : hull;
    }

    /// <summary>
    /// Merges provinces into one region. Each was simplified on its own, so neighbours no longer
    /// share exactly the same border; growing each by a pixel first closes those hairline gaps.
    /// </summary>
    private static List<Vector2[]> Union(IEnumerable<Vector2[]> polygons)
    {
        var merged = new List<Vector2[]>();
        foreach (var poly in polygons.SelectMany(Simple))
        {
            var grown = Geometry2D.OffsetPolygon(poly, 1.5f).Where(p => !Geometry2D.IsPolygonClockwise(p)).ToList();
            foreach (var piece in grown)
            {
                var current = piece;
                for (int i = merged.Count - 1; i >= 0; i--)
                {
                    var union = Geometry2D.MergePolygons(merged[i], current).Where(p => !Geometry2D.IsPolygonClockwise(p)).ToList();
                    if (union.Count == 1) // they touched: absorb the old shape
                    {
                        current = union[0];
                        merged.RemoveAt(i);
                    }
                }
                merged.Add(current);
            }
        }
        return merged;
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
    /// borders. With geography it is then clipped to the land, to the provinces the federation
    /// lies in (so its edge follows real borders), and to the communes' convex hull rounded
    /// outward (so the side facing the enemy is a smooth front rather than a provincial line).
    /// </summary>
    private Vector2[][][] Cells()
    {
        var pts = Layout();
        if (_cells != null && _cells.Length == pts.Length) return _cells;
        var w = World!;
        // Spacing and the federation's outline come from the original region; cities that joined
        // later (a federated sister) are elsewhere on the map.
        var home = pts.Take(w.InitialCommuneCount).ToArray();
        var nearest = home.Select((p, i) => home.Where((_, j) => j != i).Min(q => p.DistanceTo(q))).OrderBy(d => d).ToArray();
        float median = nearest[nearest.Length / 2];
        bool provinces = _geoLayout is { Region.Count: > 0 };
        // With provinces, real borders bound the federation and a territory may reach a fair way
        // to meet them; without, the hull outline does that job and the reach can be generous.
        float reach = median * (Geo == null ? 1.3f : provinces ? 4f : 2.2f);
        Vector2[]? outline = Geo != null && !provinces ? FrontOutline(home) : null;

        var cells = new Vector2[pts.Length][][];
        for (int i = 0; i < pts.Length; i++)
        {
            if (Geo != null && Geo.Territories.TryGetValue(w.Communes[i].Name, out var real))
            {
                cells[i] = new[] { real.Select(_project).ToArray() };
                continue;
            }
            // Federated cities are far apart: each reaches toward the others, so they share their country.
            float r = reach;
            if (i >= w.InitialCommuneCount)
            {
                var others = pts.Skip(w.InitialCommuneCount).Where((_, k) => k + w.InitialCommuneCount != i).ToList();
                r = others.Count == 0 ? median * 12 : Mathf.Max(median * 3, others.Min(q => q.DistanceTo(pts[i])) * 1.6f);
            }
            var poly = Enumerable.Range(0, 48).Select(k => pts[i] + Vector2.Right.Rotated(k * Mathf.Tau / 48) * r).ToList();
            for (int j = 0; j < pts.Length && poly.Count > 0; j++)
                if (j != i) poly = ClipToward(poly, pts[i], pts[j]);
            var pieces = new List<Vector2[]> { poly.ToArray() };
            if (i >= w.InitialCommuneCount)
            {
                // A federated city: its territory is its share of the country it came from.
                if (Globe_Layout() is { } gl && gl.StateRings.Where(r => r.Box.HasPoint(pts[i]) && Geometry2D.IsPointInPolygon(pts[i], r.Ring)).Select(r => r.Ring).FirstOrDefault() is { } country)
                    pieces = pieces.SelectMany(p => Geometry2D.IntersectPolygons(p, country)).Where(p => !Geometry2D.IsPolygonClockwise(p)).ToList();
                cells[i] = pieces.Where(p => p.Length >= 3).ToArray();
                continue;
            }
            if (_geoLayout != null)
            {
                if (outline != null) pieces = pieces.SelectMany(p => Geometry2D.IntersectPolygons(p, outline)).ToList();
                if (_geoLayout.Region.Count > 0)
                    pieces = pieces.SelectMany(p => _geoLayout.Region.SelectMany(region => Geometry2D.IntersectPolygons(p, region))).ToList();
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
        _zoom = ClampZoom(zoom);
        Cells();
        _pan = -(Layout()[commune] * _fitScale + _fitOffset - Size / 2) * _zoom;
        QueueRedraw();
    }

    private void ZoomAt(Vector2 at, float factor)
    {
        float next = ClampZoom(_zoom * factor);
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
