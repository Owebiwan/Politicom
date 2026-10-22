using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// The technology tree: one band per line of development, one column per step in the chain,
/// arrows from prerequisites. Each node shows how widely the idea has spread and whether it
/// adds drift (▲) or brakes it (▼). The movement can't choose research; it can only circulate
/// an idea, which is what the button next to the details does.
/// </summary>
public partial class TechTreeView : Control
{
    private const float NodeW = 206, NodeH = 50, ColGap = 54, RowGap = 8, BandGap = 16, LabelW = 118, Top = 6;
    private static readonly Color Held = new("2b3340");
    private static readonly Color Unheld = new("1a1d22");
    private static readonly Color Locked = new("15171b");
    private static readonly Color Edge = new("ffffff26");
    private static readonly Color EdgeLit = new("f2c94c");
    private static readonly Color Bar = new("6fa8dc");
    private static readonly Color DriftUp = new("e0705f");
    private static readonly Color DriftDown = new("6fbf7f");
    private static readonly Color Muted = new("8b919b");

    private readonly Dictionary<string, Rect2> _nodes = new();
    private readonly List<(float Y, float H, string Name)> _bands = new();
    private string? _hover;

    public World? World { get; set; }
    public string? Selected { get; private set; }

    /// <summary>Raised when the player clicks a technology.</summary>
    public event Action<string>? TechChosen;

    /// <summary>Raised when the pointer moves onto a technology (or off all of them, with null).</summary>
    public event Action<string?>? TechHovered;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Layout();
    }

    public void Select(string? id)
    {
        Selected = id;
        QueueRedraw();
    }

    private void Layout()
    {
        if (World is not { } w) return;
        _nodes.Clear();
        _bands.Clear();
        float y = Top;
        foreach (var category in w.Techs.Categories)
        {
            var byDepth = w.Techs.All.Where(t => t.Category == category).GroupBy(t => w.Techs.Depth(t.Id)).ToList();
            int rows = byDepth.Max(g => g.Count());
            float bandH = rows * (NodeH + RowGap) - RowGap;
            foreach (var group in byDepth)
            {
                int i = 0;
                // Centre shorter columns in the band.
                float offset = (rows - group.Count()) * (NodeH + RowGap) / 2;
                foreach (var t in group)
                    _nodes[t.Id] = new Rect2(LabelW + group.Key * (NodeW + ColGap), y + offset + i++ * (NodeH + RowGap), NodeW, NodeH);
            }
            _bands.Add((y, bandH, category));
            y += bandH + BandGap;
        }
        float width = LabelW + (w.Techs.All.Max(t => w.Techs.Depth(t.Id)) + 1) * (NodeW + ColGap) - ColGap + 4;
        CustomMinimumSize = new Vector2(width, y - BandGap + Top);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion)
        {
            var hit = Hit(motion.Position);
            if (hit != _hover)
            {
                _hover = hit;
                TechHovered?.Invoke(hit);
                QueueRedraw();
            }
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click && Hit(click.Position) is { } id)
        {
            Select(id);
            TechChosen?.Invoke(id);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        if (World is not { } w) return;
        var font = ThemeDB.FallbackFont;

        foreach (var (y, h, name) in _bands)
        {
            DrawRect(new Rect2(0, y - 4, Size.X, h + 8), new Color(1, 1, 1, 0.025f));
            DrawString(font, new Vector2(6, y + h / 2 + 4), name.ToUpperInvariant(), HorizontalAlignment.Left, LabelW - 10, 11, UiTheme.Heading);
        }

        // The chain behind whatever is hovered or selected lights up.
        var lit = Chain(_hover ?? Selected);
        foreach (var t in w.Techs.All)
            foreach (var p in t.Prerequisites)
            {
                var from = _nodes[p];
                var to = _nodes[t.Id];
                var a = new Vector2(from.End.X, from.Position.Y + NodeH / 2);
                var b = new Vector2(to.Position.X, to.Position.Y + NodeH / 2);
                bool on = lit.Contains(t.Id) && lit.Contains(p);
                var curve = Enumerable.Range(0, 17).Select(i =>
                {
                    float s = i / 16f;
                    var c1 = a + new Vector2(ColGap * 0.6f, 0);
                    var c2 = b - new Vector2(ColGap * 0.6f, 0);
                    return a * (1 - s) * (1 - s) * (1 - s) + c1 * 3 * (1 - s) * (1 - s) * s + c2 * 3 * (1 - s) * s * s + b * s * s * s;
                }).ToArray();
                DrawPolyline(curve, on ? EdgeLit : Edge, on ? 2.2f : 1.4f, true);
                DrawColoredPolygon(new[] { b, b + new Vector2(-7, -4), b + new Vector2(-7, 4) }, on ? EdgeLit : Edge);
            }

        foreach (var t in w.Techs.All)
        {
            var r = _nodes[t.Id];
            double share = w.TechShare(t.Id);
            int holders = w.Communes.Count(c => !c.Occupied && c.Techs.Contains(t.Id));
            bool reachable = holders > 0 || w.Communes.Any(c => !c.Occupied && w.CanDiscover(c, t));
            var bg = holders > 0 ? Held : reachable ? Unheld : Locked;
            if (t.Id == _hover) bg = bg.Lightened(0.08f);
            DrawRect(r, bg);

            var border = t.Id == Selected ? EdgeLit : w.IdeaNudges.ContainsKey(t.Id) ? UiTheme.Accent : new Color(1, 1, 1, holders > 0 ? 0.18f : 0.08f);
            DrawRect(r, border, false, t.Id == Selected || w.IdeaNudges.ContainsKey(t.Id) ? 2 : 1);

            var text = holders > 0 ? UiTheme.Text : reachable ? new Color(UiTheme.Text, 0.75f) : Muted;
            DrawString(font, r.Position + new Vector2(8, 17), t.Name, HorizontalAlignment.Left, NodeW - 16, 13, text);

            // Spread across the federation
            var bar = new Rect2(r.Position + new Vector2(8, 26), new Vector2(NodeW - 66, 5));
            DrawRect(bar, new Color(0, 0, 0, 0.45f));
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * (float)share, bar.Size.Y)), Bar);
            DrawString(font, new Vector2(bar.End.X + 6, bar.Position.Y + 6), holders > 0 ? $"{share:P0}" : reachable ? "—" : "locked",
                HorizontalAlignment.Left, -1, 11, Muted);

            // Drift: the double edge, at a glance
            var e = t.Effects;
            string tag;
            Color tagColor;
            if (e.DriftPerTick > 0 && e.DriftBrake > 0) { tag = $"▲{e.DriftPerTick:0.00} ▼{e.DriftBrake:0.00}"; tagColor = Muted; }
            else if (e.DriftPerTick > 0) { tag = $"▲ drift {e.DriftPerTick:0.00}"; tagColor = DriftUp; }
            else if (e.DriftBrake > 0) { tag = $"▼ brake {e.DriftBrake:0.00}"; tagColor = DriftDown; }
            else { tag = "no drift"; tagColor = Muted; }
            DrawString(font, r.Position + new Vector2(8, 45), tag, HorizontalAlignment.Left, -1, 11, tagColor);
            string needs = Needs(t);
            if (needs.Length > 0)
                DrawString(font, new Vector2(r.Position.X + NodeW * 0.4f, r.Position.Y + 45), needs, HorizontalAlignment.Right, NodeW * 0.6f - 8, 11, Muted);
        }
    }

    /// <summary>The technology and everything leading to it.</summary>
    private HashSet<string> Chain(string? id)
    {
        var set = new HashSet<string>();
        if (id is null || World is null) return set;
        var stack = new Stack<string>();
        stack.Push(id);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (set.Add(cur)) foreach (var p in World.Techs[cur].Prerequisites) stack.Push(p);
        }
        return set;
    }

    private string? Hit(Vector2 at) => _nodes.FirstOrDefault(n => n.Value.HasPoint(at)).Key;

    public static string Needs(TechDefinition t)
    {
        var parts = new List<string>();
        if (t.MinPractical > 0) parts.Add($"prac {t.MinPractical:0.00}");
        if (t.MinPolitical > 0) parts.Add($"pol {t.MinPolitical:0.00}");
        return string.Join(" · ", parts);
    }

}

/// <summary>The tree with a details column: what the selected idea does, who has it, and Circulate.</summary>
public partial class TechTreePanel : HBoxContainer
{
    private readonly World _world;
    private readonly Func<PlayerAction, ActionResult> _apply;
    private TechTreeView _tree = null!;
    private RichTextLabel _details = null!;
    private Button _circulate = null!;
    private Label _result = null!;

    public TechTreePanel(World world, Func<PlayerAction, ActionResult> apply)
    {
        _world = world;
        _apply = apply;
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 18);
        _tree = new TechTreeView { World = _world };
        _tree.TechChosen += Show;
        // Hovering describes a technology at once; moving off returns to the one selected.
        _tree.TechHovered += id =>
        {
            if (id != null) Show(id, preview: true);
            else if (_tree.Selected is { } selected) Show(selected);
            else _details.Text = Intro;
        };
        AddChild(_tree);

        var side = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(side);
        _details = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(300, 0) };
        _details.AddThemeFontSizeOverride("normal_font_size", 13);
        _details.AddThemeFontSizeOverride("bold_font_size", 15);
        side.AddChild(_details);
        _circulate = new Button { Text = "Circulate this idea", Visible = false, TooltipText = "Pamphlets and speakers promote it for 20 turns: communes discover and adopt it faster." };
        _circulate.Pressed += () =>
        {
            if (_tree.Selected is not { } id) return;
            var r = _apply(new CirculateIdea(id));
            _result.Text = r.Message;
            _result.Modulate = r.Ok ? new Color(0.55f, 0.85f, 0.6f) : new Color(0.95f, 0.5f, 0.45f);
            Show(id);
            _tree.QueueRedraw();
        };
        side.AddChild(_circulate);
        _result = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) };
        side.AddChild(_result);

        _details.Text = Intro;
    }

    private const string Intro = "[color=#8b919b]Point at a technology to see what it does and who has it; click to keep it here.\n\n"
                        + "Communes discover ideas on their own, shaped by their education, needs and values, and pass them along trust links. "
                        + "The movement can't choose research — only circulate an idea so it spreads faster.\n\n"
                        + "Bars: share of the federation using it. ▲ adds drift, ▼ brakes it. Requirements are the education a commune needs to discover it (half that to adopt it from a neighbour).[/color]";

    /// <summary>Opens the details for a technology, as if it had been clicked.</summary>
    public void Select(string id)
    {
        _tree.Select(id);
        Show(id);
    }

    private void Show(string id) => Show(id, preview: false);

    /// <param name="preview">Hovering: describe it, but keep the Circulate button for the selected one.</param>
    private void Show(string id, bool preview)
    {
        var t = _world.Techs[id];
        var holders = _world.Communes.Where(c => !c.Occupied && c.Techs.Contains(id)).Select(c => c.Name).ToList();
        int canDiscover = _world.Communes.Count(c => !c.Occupied && !c.Techs.Contains(id) && _world.CanDiscover(c, t));
        string Esc(string s) => s.Replace("[", "[lb]");
        var effects = string.Join("\n", t.Effects.Describe().Select(d => $"[color={(d.Good ? "#6fbf7f" : "#e0705f")}]{(d.Good ? "+" : "−")}[/color] {Esc(d.Text)}"));
        string prereqs = t.Prerequisites.Length == 0 ? "none" : string.Join(", ", t.Prerequisites.Select(p => _world.Techs[p].Name));
        string unlocks = string.Join(", ", _world.Techs.Unlocks(id).Select(u => u.Name));
        _details.Text =
            $"[b]{Esc(t.Name)}[/b]\n[color=#8b919b]{Esc(t.Category)}[/color]\n\n" +
            $"[color=#6fbf7f]+[/color] {Esc(t.Perk)}\n[color=#e0705f]−[/color] {Esc(t.Risk)}\n\n" +
            $"{effects}\n[color=#8b919b](Holders' effects apply where it's used; the rest scale with how widely it has spread.)[/color]\n\n" +
            $"Needs: {(TechTreeView.Needs(t) is { Length: > 0 } n ? n : "nothing")} · after: {Esc(prereqs)}" +
            (unlocks.Length > 0 ? $"\nLeads to: {Esc(unlocks)}" : "") + "\n\n" +
            (holders.Count == 0
                ? $"No commune has it yet. {canDiscover} could discover it now."
                : $"Used by {holders.Count}: {Esc(string.Join(", ", holders.Take(8)))}{(holders.Count > 8 ? "…" : "")}") +
            (_world.IdeaNudges.TryGetValue(id, out int left) ? $"\n[color=#c4473a]Being circulated: {_world.Turns(left)} left.[/color]" : "");
        _circulate.Visible = !preview || _tree.Selected == id;
        _circulate.Text = $"Circulate this idea ({_world.Balance.CostCirculateIdea:0} influence)";
        if (!preview) _result.Text = "";
    }
}
