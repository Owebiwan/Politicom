using System;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

public enum MapMode { Trust, Education, Values, Wellbeing }

/// <summary>
/// Draws the communes and the trust network over them. Edge thickness and colour show trust,
/// rings show the front, secession and selection; the map mode recolours the communes.
/// </summary>
public partial class MapView : Control
{
    private const float Margin = 70;
    private static readonly Color Background = new(0.08f, 0.09f, 0.11f);
    private static readonly Color Label = new(0.85f, 0.86f, 0.88f);
    private static readonly Color Muted = new(0.45f, 0.47f, 0.5f);
    private static readonly Color Danger = new(0.92f, 0.3f, 0.25f);
    private static readonly Color Warn = new(0.95f, 0.75f, 0.25f);
    private static readonly Color Decision = new(0.3f, 0.85f, 0.95f);

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
            _layout = null;
            _zoom = 1;
            _pan = Vector2.Zero;
            _hover = null;
            QueueRedraw();
        }
    }

    public MapMode Mode { get; set; } = MapMode.Trust;
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
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        if (World is not { } w) return;
        var font = ThemeDB.FallbackFont;

        foreach (var (a, b) in w.Trust.Edges())
        {
            var ca = w.Communes[a];
            var cb = w.Communes[b];
            var pa = ToScreen(ca);
            var pb = ToScreen(cb);
            if (ca.Occupied || cb.Occupied)
            {
                DrawDashedLine(pa, pb, new Color(Muted, 0.35f), 1, 4);
                continue;
            }
            float t = (float)w.Trust[a, b];
            var color = TrustColor(t);
            if (!ca.Federated || !cb.Federated) color.A = 0.35f;
            DrawLine(pa, pb, color, 1 + 5 * t, true);
        }

        foreach (var c in w.Communes)
        {
            var p = ToScreen(c);
            float r = Radius(c);
            if (c.Occupied)
            {
                DrawCircle(p, r, new Color(0.22f, 0.22f, 0.24f));
                var occupier = c.OccupiedBy is int by ? EnemyColor(w, by) : Danger;
                DrawLine(p + new Vector2(-r, -r) * 0.6f, p + new Vector2(r, r) * 0.6f, occupier, 2, true);
                DrawLine(p + new Vector2(-r, r) * 0.6f, p + new Vector2(r, -r) * 0.6f, occupier, 2, true);
            }
            else
            {
                DrawCircle(p, r, NodeColor(w, c));
                DrawArc(p, r, 0, Mathf.Tau, 32, new Color(0, 0, 0, 0.6f), 1.5f, true);
            }

            if (c.Front && !c.Occupied)
            {
                // One ring per enemy faced; the filled arc shows how much of the commune is still held.
                int ring = 0;
                foreach (var k in c.Fronts.OrderBy(k => k))
                {
                    var color = EnemyColor(w, k);
                    float rr = r + 4 + ring++ * 4;
                    DrawArc(p, rr, 0, Mathf.Tau, 32, new Color(color, 0.25f), 3, true);
                    DrawArc(p, rr, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * (float)c.Control, 32, color, 3, true);
                }
            }
            if (!c.Federated && !c.Occupied)
                for (int i = 0; i < 8; i++)
                    DrawArc(p, r + 4, i * Mathf.Tau / 8, i * Mathf.Tau / 8 + Mathf.Tau / 16, 6, Warn, 2, true);
            if (w.PendingSecessions.Any(s => s.Commune == c.Id))
                DrawArc(p, r + 8 + 3 * Mathf.Sin((float)_time * 5), 0, Mathf.Tau, 32, Decision, 2, true);
            if (c.Starving && !c.Occupied)
                DrawCircle(p + new Vector2(r * 0.7f, -r * 0.7f), 3.5f, Warn);
            if (c.Id == Selected) DrawArc(p, r + 9, 0, Mathf.Tau, 40, Colors.White, 2.5f, true);
            if (c.Id == Secondary) DrawArc(p, r + 9, 0, Mathf.Tau, 40, new Color(1, 1, 1, 0.5f), 1.5f, true);

            DrawStringOutline(font, p + new Vector2(r + 4, 4), c.Name, HorizontalAlignment.Left, -1, 12, 4, Background);
            DrawString(font, p + new Vector2(r + 4, 4), c.Name, HorizontalAlignment.Left, -1, 12,
                c.Occupied ? Muted : Label);
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

    private static Color TrustColor(float t) =>
        t < 0.5f ? Danger.Lerp(Warn, t * 2) : Warn.Lerp(new Color(0.35f, 0.8f, 0.45f), (t - 0.5f) * 2);

    private void DrawLegend(Font font)
    {
        string text = Mode switch
        {
            MapMode.Trust => "Trust — red low, green high. Line width = trust between neighbours.",
            MapMode.Education => "Education — orange: practical ahead (expert trap), violet: political ahead. Darker = less educated.",
            MapMode.Values => "Values — red: collectivist, green: federalist, blue: militant.",
            _ => "Wellbeing — red low, green high.",
        };
        DrawString(font, new Vector2(14, Size.Y - 34), text, HorizontalAlignment.Left, -1, 13, Muted);
        DrawString(font, new Vector2(14, Size.Y - 14),
            "Rings: front, one per enemy (filled = control). Crosses: occupied. Dashed yellow: seceded. Pulsing blue: leaving. Shift-click: second commune.",
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
            $"Food {c.Food:0} · goods {c.Goods:0}" + (c.Starving ? " · STARVING" : ""),
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

    private (float Scale, Vector2 Offset) Fit()
    {
        if (World is not { } w) return (1, Vector2.Zero);
        float maxX = (float)w.Communes.Max(c => c.X);
        float maxY = (float)w.Communes.Max(c => c.Y);
        float scale = Mathf.Min((Size.X - 2 * Margin - 120) / maxX, (Size.Y - 2 * Margin) / maxY);
        var offset = new Vector2((Size.X - maxX * scale - 120) / 2, (Size.Y - maxY * scale) / 2);
        return (scale, offset);
    }

    private Vector2[]? _layout;
    private Vector2 _layoutSize;

    private Vector2 ToScreen(Commune c) => (Layout()[c.Id] - Size / 2) * _zoom + Size / 2 + _pan;

    /// <summary>Centres the map on a commune, keeping the current zoom.</summary>
    public void CenterOn(int commune)
    {
        _pan = -(Layout()[commune] - Size / 2) * _zoom;
        QueueRedraw();
    }

    /// <summary>Centres the map on a commune at the given zoom.</summary>
    public void FocusOn(int commune, float zoom)
    {
        _zoom = Mathf.Clamp(zoom, 0.6f, 5f);
        _pan = -(Layout()[commune] - Size / 2) * _zoom;
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

    /// <summary>
    /// Geographic positions, relaxed so dense clusters (Barcelona) don't overlap: overlapping
    /// communes push apart while a weak spring keeps each near its real location.
    /// </summary>
    private Vector2[] Layout()
    {
        if (_layout != null && _layoutSize == Size) return _layout;
        var w = World!;
        var (scale, offset) = Fit();
        var home = w.Communes.Select(c => offset + new Vector2((float)c.X, (float)c.Y) * scale).ToArray();
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
        _layoutSize = Size;
        return _layout = p;
    }

    private static float Radius(Commune c) => 5 + Mathf.Sqrt((float)c.Population) * 1.2f;

    private int? Hit(Vector2 at)
    {
        if (World is not { } w) return null;
        int? best = null;
        float bestDist = float.MaxValue;
        foreach (var c in w.Communes)
        {
            float d = at.DistanceTo(ToScreen(c));
            if (d <= Radius(c) + 6 && d < bestDist)
            {
                best = c.Id;
                bestDist = d;
            }
        }
        return best;
    }
}
