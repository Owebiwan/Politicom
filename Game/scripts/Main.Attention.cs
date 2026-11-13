using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// "Needs attention", at the top of the Overview tab: the few communes that most need the
/// movement this turn (Sim/Attention.cs), each with what's wrong and one button for what would
/// help. The commune's name selects it and brings it into view.
/// </summary>
public partial class Main
{
    private VBoxContainer _attention = null!;
    private string _attentionKey = "";

    private void BuildAttention(Container overview)
    {
        _attention = new VBoxContainer();
        _attention.AddThemeConstantOverride("separation", 4);
        overview.AddChild(_attention);
        overview.MoveChild(_attention, 0);
    }

    private void RefreshAttention()
    {
        var list = Attention.List(_world);
        // Rebuilt only when something changed, so the buttons don't flicker under the pointer.
        string key = $"{_world.Tick}|{(int)_world.Influence}|" + string.Join(";", list.Select(x => $"{x.Commune}:{x.Problem}:{x.Remedy}"));
        if (key == _attentionKey) return;
        _attentionKey = key;
        foreach (var child in _attention.GetChildren()) child.QueueFree();
        if (list.Count == 0) return;

        _attention.AddChild(UiTheme.SectionTitle($"Needs attention ({list.Count})"));
        foreach (var x in list)
        {
            var c = _world.Communes[x.Commune];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            _attention.AddChild(row);

            var mark = new ColorRect
            {
                CustomMinimumSize = new Vector2(4, 0),
                Color = x.Urgency >= 80 ? new Color("e05040") : x.Urgency >= 50 ? new Color("e0b050") : new Color("8aa0b8"),
            };
            row.AddChild(mark);
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", 0);
            row.AddChild(text);
            var name = new LinkButton { Text = c.Name, Underline = LinkButton.UnderlineMode.OnHover, TooltipText = "Select it and bring it into view" };
            name.AddThemeColorOverride("font_color", UiTheme.GoldBright);
            name.Pressed += () =>
            {
                _map.Selected = c.Id;
                _map.CenterOn(c.Id);
                Refresh();
            };
            text.AddChild(name);
            var problem = new Label { Text = x.Problem, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(180, 0) };
            problem.AddThemeFontSizeOverride("font_size", 13);
            problem.AddThemeColorOverride("font_color", new Color(0.82f, 0.78f, 0.7f));
            text.AddChild(problem);

            if (x.Action is { } action)
            {
                var button = new Button
                {
                    Text = $"{x.Short} ({x.Cost:0})", CustomMinimumSize = new Vector2(150, 30),
                    SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = $"{x.Remedy}: {x.Cost:0} influence",
                    Disabled = _world.Influence < x.Cost,
                };
                if (button.Disabled) button.TooltipText += $" (you have {_world.Influence:0})";
                button.Pressed += () => Do(action);
                row.AddChild(button);
            }
            else
            {
                var note = new Label { Text = x.Remedy, CustomMinimumSize = new Vector2(150, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart, VerticalAlignment = VerticalAlignment.Center };
                note.AddThemeFontSizeOverride("font_size", 12);
                note.AddThemeColorOverride("font_color", new Color(0.6f, 0.58f, 0.52f));
                row.AddChild(note);
            }
        }
    }
}
