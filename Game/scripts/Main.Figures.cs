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
        string where = f.Commune is int id ? $"in {Esc(_world.Communes[id].Name)}" : "back in the ranks";
        string hunger = f.Hunger < 0.15 ? "#70d080" : f.Hunger < 0.3 ? "#e0c060" : "#f07060";
        text.Text = $"[b]{Esc(f.Name)}[/b] · [color=#c9a25b]{Figure.RoleName(f.Role)}[/color] {where}"
                    + (f.Empowered ? " · [color=#f07060]holds power[/color]" : "")
                    + $"\nPrestige {f.Prestige:P0} · [color={hunger}]{AmbitionWord(f.Hunger)}[/color]"
                    + (f.Description.Length > 0 ? $"\n[i][color=#a8a090]{Esc(f.Description)}[/color][/i]" : "");
        row.AddChild(text);
        return card;
    }

    private static string AmbitionWord(double hunger) =>
        hunger < 0.15 ? "content" : hunger < 0.28 ? "restless" : hunger < 0.38 ? "ambitious" : "hungry for power";

    private void RefreshFigures()
    {
        var w = _world;
        // Rebuilt only when something shown changes.
        string key = string.Join("|", w.Figures.Select(f => $"{f.Alive}{f.Commune}{f.Resting}{f.Empowered}{(int)(f.Prestige * 20)}{(int)(f.Hunger * 20)}")) + $"/{_map.Selected}/{(int)w.Influence}";
        if (key == _figuresShown) return;
        _figuresShown = key;
        foreach (var child in _figures.GetChildren()) child.QueueFree();
        foreach (var f in w.Figures.OrderByDescending(f => f.Alive))
        {
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", UiTheme.FramePanel(8));
            var box = new VBoxContainer();
            card.AddChild(box);
            var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false };
            text.AddThemeFontSizeOverride("normal_font_size", 13);
            text.AddThemeFontSizeOverride("bold_font_size", 14);
            string where = !f.Alive ? "[color=#9aa0a8]dead[/color]"
                : f.Resting > 0 ? $"[color=#9aa0a8]in the ranks, {w.Turns(f.Resting)} more[/color]"
                : f.Commune is int id ? $"in {Esc(w.Communes[id].Name)}" : "[color=#9aa0a8]between postings[/color]";
            string hunger = f.Hunger >= 0.38 ? "#f07060" : f.Hunger >= 0.28 ? "#e0a040" : "#9aa0a8";
            text.Text = $"[b]{Esc(f.Name)}[/b] [color=#c9a25b]{Figure.RoleName(f.Role)}[/color] · {where}" +
                        (f.Empowered ? " · [color=#f07060]holds power[/color]" : "") +
                        (f.Alive ? $"\nPrestige {f.Prestige:P0} · [color={hunger}]{AmbitionWord(f.Hunger)}[/color] · {Figure.Does(f.Role)}" : "");
            text.TooltipText = f.Description;
            text.MouseFilter = MouseFilterEnum.Pass;
            box.AddChild(text);
            if (f.Alive)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 6);
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
