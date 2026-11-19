using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// The Figures section of the Movement tab: each notable figure with their role, where they work,
/// their prestige and how ambitious they've grown, and buttons to send them to the selected
/// commune or rotate them back to the ranks.
/// </summary>
public partial class Main
{
    private VBoxContainer _figures = null!;
    private string _figuresShown = "";

    private void BuildFigures(VBoxContainer movement)
    {
        movement.AddChild(UiTheme.SectionTitle("Figures"));
        var help = new Label
        {
            Text = "Notable militants. Where they work they make a difference; their prestige grows, and with it their ambition. Rotate them back to the ranks before they ask for power.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.6f),
            CustomMinimumSize = new Vector2(300, 0), // wrapped text with no width wraps letter by letter, thousands of pixels tall
        };
        help.AddThemeFontSizeOverride("font_size", 12);
        movement.AddChild(help);
        _figures = new VBoxContainer();
        _figures.AddThemeConstantOverride("separation", 6);
        movement.AddChild(_figures);
    }

    private static string RoleEmblem(FigureRole role) => role switch
    {
        FigureRole.Commander => "t_sword",
        FigureRole.Orator => "t_press",
        FigureRole.Organizer => "t_gear",
        _ => "t_book",
    };

    /// <summary>A figure's card for a pop-up: their emblem, name, role, where they work, prestige and ambition.</summary>
    private Control FigureCard(Figure f)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UiTheme.FramePanel(10));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        card.AddChild(row);
        row.AddChild(UiTheme.Medallion(RoleEmblem(f.Role), 52));
        var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(520, 0) };
        text.AddThemeFontSizeOverride("normal_font_size", 14);
        text.AddThemeFontSizeOverride("bold_font_size", 15);
        string where = (f.Born != 0 ? $"aged {_world.Year - f.Born}, " : "") + (f.Commune is int id ? $"in {Esc(_world.Communes[id].Name)}" : f.Retired ? "retired" : "back in the ranks");
        string hunger = f.Hunger < 0.15 ? "#70d080" : f.Hunger < 0.3 ? "#e0c060" : "#f07060";
        text.Text = $"[b]{Esc(f.Name)}[/b] · [color=#c8aa72]{Figure.RoleName(f.Role)}[/color] {where}"
                    + (f.Empowered ? " · [color=#f07060]holds power[/color]" : "")
                    + $"\nPrestige {f.Prestige:P0} · [color={hunger}]{AmbitionWord(f.Hunger)}[/color]"
                    + $"\n{Character(f, memory: true)}"
                    + (f.Description.Length > 0 ? $"\n[i][color=#a8a090]{Esc(f.Description)}[/color][/i]" : "");
        row.AddChild(text);
        return card;
    }

    /// <summary>Their traits, how they feel about the movement, their friends and rivals, and what they last remember.</summary>
    private string Character(Figure f, bool memory)
    {
        var w = _world;
        string traits = f.Traits.Count == 0 ? "" : string.Join(", ", f.Traits.Select(Figure.TraitName));
        string regardColor = f.Regard >= 0.2 ? "#70d080" : f.Regard > -0.2 ? "#b8b0a0" : "#f07060";
        var friends = w.Figures.Where(o => o.Alive && o != f && f.FriendOf(o.Id)).Select(o => Esc(o.Name)).ToList();
        var rivals = w.Figures.Where(o => o.Alive && o != f && f.RivalOf(o.Id)).Select(o => Esc(o.Name)).ToList();
        var parts = new List<string>();
        if (traits.Length > 0) parts.Add($"[i]{traits}[/i]");
        parts.Add($"[color={regardColor}]{Figure.RegardWord(f.Regard)}[/color] toward the movement");
        if (friends.Count > 0) parts.Add($"[color=#70d080]friend of[/color] {string.Join(", ", friends)}");
        if (rivals.Count > 0) parts.Add($"[color=#f07060]rival of[/color] {string.Join(", ", rivals)}");
        string line = string.Join(" · ", parts);
        if (memory && f.Memories.LastOrDefault() is { } m)
            line += $"\n[color=#a8a090]Remembers: {Esc(f.Name.Split(' ')[0])} {Esc(m.Text)} ({w.Format(w.DateAt(m.Tick), "MMM yyyy")})[/color]";
        return line;
    }

    private static string AmbitionWord(double hunger) =>
        hunger < 0.15 ? "content" : hunger < 0.28 ? "restless" : hunger < 0.38 ? "ambitious" : "hungry for power";

    private void RefreshFigures()
    {
        var w = _world;
        // Rebuilt only when something shown changes.
        string key = string.Join("|", w.Figures.Select(f => $"{f.Alive}{f.Retired}{f.Commune}{f.Resting}{f.Empowered}{(int)(f.Prestige * 20)}{(int)(f.Hunger * 20)}{(int)(f.Regard * 10)}{f.Memories.Count}{string.Join(",", f.Opinions.Select(o => (int)(o.Value * 3)))}")) + $"/{_map.Selected}/{(int)w.Influence}";
        if (key == _figuresShown) return;
        _figuresShown = key;
        foreach (var child in _figures.GetChildren()) child.QueueFree();
        foreach (var f in w.Figures.OrderByDescending(f => f.Alive && !f.Retired).ThenByDescending(f => f.Alive))
        {
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", UiTheme.FramePanel(8));
            var box = new VBoxContainer();
            card.AddChild(box);
            var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(300, 0) };
            text.AddThemeFontSizeOverride("normal_font_size", 13);
            text.AddThemeFontSizeOverride("bold_font_size", 14);
            string age = f.Born != 0 ? $", {w.Year - f.Born}" : "";
            string where = !f.Alive ? "[color=#9aa0a8]dead[/color]"
                : f.Retired ? $"[color=#9aa0a8]retired{age}[/color]"
                : f.Resting > 0 ? $"[color=#9aa0a8]in the ranks, {w.Turns(f.Resting)} more[/color]"
                : f.Commune is int id ? $"in {Esc(w.Communes[id].Name)}" : "[color=#9aa0a8]between postings[/color]";
            string hunger = f.Hunger >= 0.38 ? "#f07060" : f.Hunger >= 0.28 ? "#e0a040" : "#9aa0a8";
            text.Text = $"[b]{Esc(f.Name)}[/b]{(f.Alive && !f.Retired ? $"[color=#929aa6]{age}[/color]" : "")} [color=#c8aa72]{Figure.RoleName(f.Role)}[/color] · {where}" +
                        (f.Empowered ? " · [color=#f07060]holds power[/color]" : "") +
                        (f.Alive ? $"\nPrestige {f.Prestige:P0} · [color={hunger}]{AmbitionWord(f.Hunger)}[/color] · {Figure.Does(f.Role)}\n{Character(f, memory: false)}" : "");
            text.TooltipText = f.Description + (f.Memories.Count > 0 ? "\n\nRemembers:\n" + string.Join("\n", f.Memories.Select(m => $"· {m.Text} ({w.Format(w.DateAt(m.Tick), "MMM yyyy")})")) : "")
                               + (f.Traits.Count > 0 ? "\n\nTraits: " + string.Join(", ", f.Traits.Select(Figure.TraitName)) : "");
            text.MouseFilter = MouseFilterEnum.Pass;
            box.AddChild(text);
            if (f.Alive && !f.Retired)
            {
                var row = new HFlowContainer(); // two long buttons wrap rather than widen the dashboard
                row.AddThemeConstantOverride("h_separation", 6);
                row.AddThemeConstantOverride("v_separation", 4);
                box.AddChild(row);
                int fid = f.Id;
                if (f.Resting == 0)
                {
                    var target = _map.Selected is int sel && w.Communes[sel].InFederation ? w.Communes[sel] : null;
                    var send = AddButton(row, target is null ? "Send to… (select a commune)" : $"Send to {target.Name} ({w.Balance.CostPostFigure:0})",
                        "Post them to the commune selected on the map.", () => { if (_map.Selected is int s) Do(new PostFigure(fid, s)); });
                    send.Disabled = target is null || target.Id == f.Commune;
                }
                if (f.Commune != null || f.Empowered)
                    AddButton(row, $"Rotate to the ranks ({w.Balance.CostRotateFigure:0})",
                        "Mandates are temporary: they lose much of their prestige and ambition, rest a while, and their followers grumble. Drift falls a little.",
                        () => Do(new RotateFigure(fid)));
            }
            _figures.AddChild(card);
        }
    }
}
