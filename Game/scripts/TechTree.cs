using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// The technology tree: one band per line of development, columns grouped by age (and by step
/// in the chain within an age), arrows from prerequisites. Techniques of earlier ages that are
/// common knowledge by now are left out; ages not reached yet are shown dimmed, with their year. Each node shows how widely the idea has spread and whether it
/// adds drift (▲) or brakes it (▼). The movement can't choose research; it can only circulate
/// an idea, which is what the button next to the details does.
/// </summary>
public partial class TechTreeView : Control
{
    private const float NodeW = 196, NodeH = 50, ColGap = 44, RowGap = 8, BandGap = 16, LabelW = 118, Top = 34;
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
    private readonly List<(float X, float W, int From)> _ages = new();
    private List<TechDefinition> _shown = new();
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
        _ages.Clear();
        _shown = w.LiveTechs.ToList();
        var shown = _shown.Select(t => t.Id).ToHashSet();

        // Columns: each age in turn, and within an age one column per step of its chains.
        var local = new Dictionary<string, int>();
        int Local(TechDefinition t) => local.TryGetValue(t.Id, out int d) ? d : local[t.Id] = t.Prerequisites
            .Where(p => shown.Contains(p) && Ages.Index(w.Techs[p].Era) == Ages.Index(t.Era))
            .Select(p => Local(w.Techs[p]) + 1).DefaultIfEmpty(0).Max();
        var column = new Dictionary<string, int>();
        int next = 0;
        foreach (var age in _shown.GroupBy(t => Ages.Index(t.Era)).OrderBy(g => g.Key))
        {
            int width = age.Max(Local) + 1;
            foreach (var t in age) column[t.Id] = next + Local(t);
            _ages.Add((LabelW + next * (NodeW + ColGap), width * (NodeW + ColGap) - ColGap, age.Min(t => t.Era)));
            next += width;
        }

        float y = Top;
        foreach (var category in w.Techs.Categories)
        {
            var byColumn = _shown.Where(t => t.Category == category).GroupBy(t => column[t.Id]).ToList();
            if (byColumn.Count == 0) continue;
            int rows = byColumn.Max(g => g.Count());
            float bandH = rows * (NodeH + RowGap) - RowGap;
            foreach (var group in byColumn)
            {
                int i = 0;
                // Centre shorter columns in the band.
                float offset = (rows - group.Count()) * (NodeH + RowGap) / 2;
                foreach (var t in group.OrderBy(t => t.Era))
                    _nodes[t.Id] = new Rect2(LabelW + group.Key * (NodeW + ColGap), y + offset + i++ * (NodeH + RowGap), NodeW, NodeH);
            }
            _bands.Add((y, bandH, category));
            y += bandH + BandGap;
        }
        CustomMinimumSize = new Vector2(LabelW + next * (NodeW + ColGap) - ColGap + 4, y - BandGap + 6);
    }

    /// <summary>Techniques of earlier ages that everyone knows by now (not shown in the tree).</summary>
    public int CommonKnowledge => World is { } w ? w.Techs.All.Count(w.CommonKnowledge) : 0;

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

        // Age headers: the current age marked, later ones dimmed with the year they open.
        for (int i = 0; i < _ages.Count; i++)
        {
            var (x, width, from) = _ages[i];
            int index = Ages.Index(from);
            bool reached = from <= w.Year;
            bool current = Ages.Index(w.Year) == index;
            if (current) DrawRect(new Rect2(x - ColGap / 2, 0, width + ColGap, Size.Y), new Color(1, 1, 1, 0.025f));
            string label = Ages.All[index].Name.ToUpperInvariant() + (current ? " · NOW" : reached ? "" : $" · FROM {Ages.Year(from)}");
            DrawString(font, new Vector2(x, 20), label, HorizontalAlignment.Left, width, 12, reached ? UiTheme.Heading : Muted);
            if (i > 0) DrawLine(new Vector2(x - ColGap / 2, 4), new Vector2(x - ColGap / 2, Size.Y), new Color(1, 1, 1, 0.08f), 1);
        }

        foreach (var (y, h, name) in _bands)
        {
            DrawRect(new Rect2(0, y - 4, Size.X, h + 8), new Color(1, 1, 1, 0.025f));
            DrawString(font, new Vector2(6, y + h / 2 + 4), name.ToUpperInvariant(), HorizontalAlignment.Left, LabelW - 10, 11, UiTheme.Heading);
        }

        // The chain behind whatever is hovered or selected lights up.
        var lit = Chain(_hover ?? Selected);
        foreach (var t in _shown)
            foreach (var p in t.Prerequisites)
            {
                if (!_nodes.TryGetValue(p, out var from)) continue; // common knowledge
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

        foreach (var t in _shown)
        {
            var r = _nodes[t.Id];
            bool future = !w.Reached(t);
            double share = w.TechShare(t.Id);
            int holders = w.Communes.Count(c => !c.Occupied && c.Techs.Contains(t.Id));
            bool reachable = holders > 0 || w.Communes.Any(c => !c.Occupied && w.CanDiscover(c, t));
            var bg = holders > 0 ? Held : reachable ? Unheld : Locked;
            if (t.Id == _hover) bg = bg.Lightened(0.08f);
            DrawRect(r, bg);

            var border = t.Id == Selected ? EdgeLit : w.IdeaNudges.ContainsKey(t.Id) ? UiTheme.Accent : new Color(1, 1, 1, holders > 0 ? 0.18f : 0.08f);
            DrawRect(r, border, false, t.Id == Selected || w.IdeaNudges.ContainsKey(t.Id) ? 2 : 1);

            var text = holders > 0 ? UiTheme.Text : reachable ? new Color(UiTheme.Text, 0.75f) : Muted;
            if (future) text = new Color(Muted, 0.7f);
            DrawString(font, r.Position + new Vector2(8, 17), t.Name, HorizontalAlignment.Left, NodeW - 16, 13, text);

            // Spread across the federation
            var bar = new Rect2(r.Position + new Vector2(8, 26), new Vector2(NodeW - 66, 5));
            DrawRect(bar, new Color(0, 0, 0, 0.45f));
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * (float)share, bar.Size.Y)), Bar);
            DrawString(font, new Vector2(bar.End.X + 6, bar.Position.Y + 6), holders > 0 ? $"{share:P0}" : reachable ? "—" : future ? Ages.Year(t.Era) : "locked",
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
        // The tree is taller than a laptop screen: it scrolls, the details column stays put.
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(1060, 560),
        };
        scroll.AddChild(_tree);
        _tree.TechChosen += Show;
        // Hovering describes a technology at once; moving off returns to the one selected.
        _tree.TechHovered += id =>
        {
            if (id != null) Show(id, preview: true);
            else if (_tree.Selected is { } selected) Show(selected);
            else _details.Text = Intro;
        };
        AddChild(scroll);

        var side = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(side);

        // Science and the research focus: the one say the movement has over research.
        _science = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(300, 0) };
        _science.AddThemeFontSizeOverride("normal_font_size", 13);
        _science.AddThemeFontSizeOverride("bold_font_size", 14);
        side.AddChild(_science);
        var focusRow = new HBoxContainer();
        focusRow.AddChild(new Label { Text = "Research focus" });
        _focus = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Ask the communes to organize their science around one line. Breakthroughs come only with a focus, in that line, and communes lean toward it when they discover on their own.",
        };
        _focus.AddItem("None: no organized research");
        foreach (var line in _world.Techs.Categories) _focus.AddItem(line);
        _focus.Select(_world.ResearchFocus is { } current ? 1 + _world.Techs.Categories.ToList().IndexOf(current) : 0);
        _focus.ItemSelected += i =>
        {
            var r = _apply(new SetResearchFocus(i == 0 ? null : _world.Techs.Categories[(int)i - 1]));
            _result.Text = r.Message;
            ShowScience();
        };
        focusRow.AddChild(_focus);
        side.AddChild(focusRow);
        side.AddChild(new HSeparator());
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
        ShowScience();
    }

    private RichTextLabel _science = null!;
    private OptionButton _focus = null!;

    private void ShowScience()
    {
        _science.Text = $"[b]Science[/b] {_world.LastScience:0.0} a {_world.Turn} · next breakthrough {_world.Science:0}/{_world.BreakthroughCost:0} · {_world.Breakthroughs} so far\n" +
                        $"[color=#8b919b]Technology level: {_world.TechLevel:P0} of this age's techniques" +
                        ". " + string.Concat(_world.Enemies.Select(e => Politicom.Sim.Global.TechLevelSystem.EnemyEdge(_world, e) is var edge and > 1.005
                            ? $"[color=#e0705f]{e.Name} has modernized faster: +{edge - 1:P0} strength.[/color] "
                            : $"{e.Name} modernizes as the war goes on; keep up and it gains nothing from it. ")) +
                        $"{_tree.CommonKnowledge} techniques of earlier ages are common knowledge.[/color]";
    }

    private const string Intro = "[color=#8b919b]Point at a technology to see what it does and who has it; click to keep it here.\n\n"
                        + "Communes discover ideas on their own, shaped by their education, needs and values, and pass them along trust links. "
                        + "The movement can't choose research. It can set a focus for the communes' science, and circulate an idea so it spreads faster.\n\n"
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
        string unlocks = string.Join(", ", _world.Techs.Unlocks(id).Where(u => !_world.CommonKnowledge(u)).Select(u => u.Name));
        string era = $"{Ages.Name(t.Era)} age" + (t.Era > -10000 ? $", from {Ages.Year(t.Era)}" : "") + (t.Until is int until ? $"; common knowledge by {Ages.Year(until)}" : "");
        _details.Text =
            $"[b]{Esc(t.Name)}[/b]\n[color=#8b919b]{Esc(t.Category)} · {era}[/color]\n\n" +
            (_world.Reached(t) ? "" : $"[color=#f2c94c]Not yet: the world reaches it in {Ages.Year(t.Era)}.[/color]\n\n") +
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
