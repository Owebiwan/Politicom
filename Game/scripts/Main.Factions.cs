using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// Factions in the Overview tab: each faction that has formed, its demand, its communes and the
/// figure at its head, how close it is to making its demand, and a button to address it.
/// </summary>
public partial class Main
{
    private VBoxContainer _factions = null!;
    private string _factionsKey = "";

    private void BuildFactions(Container overview)
    {
        _factions = new VBoxContainer();
        _factions.AddThemeConstantOverride("separation", 4);
        overview.AddChild(_factions);
        overview.MoveChild(_factions, 1); // under Needs attention
    }

    private void RefreshFactions()
    {
        var w = _world;
        var shown = w.Factions.Where(f => f.Members.Count >= 3 || f.Strength >= 0.05).ToList();
        string key = string.Join(";", shown.Select(f => $"{f.Kind}{(int)(f.Strength * 50)}{f.Members.Count}{f.Leader}")) + $"/{(int)w.Influence}";
        if (key == _factionsKey) return;
        _factionsKey = key;
        foreach (var child in _factions.GetChildren()) child.QueueFree();
        if (shown.Count == 0) return;
        _factions.AddChild(UiTheme.SectionTitle("Factions"));
        foreach (var f in shown.OrderByDescending(f => f.Strength))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _factions.AddChild(row);
            var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(200, 0) };
            text.AddThemeFontSizeOverride("normal_font_size", 13);
            text.AddThemeFontSizeOverride("bold_font_size", 14);
            string color = f.Strength >= 0.75 ? "#f07060" : f.Strength >= 0.45 ? "#e0b050" : "#b8b0a0";
            string bar = new string('█', (int)System.Math.Round(f.Strength * 10)) + new string('░', 10 - (int)System.Math.Round(f.Strength * 10));
            string leader = f.Leader is int l ? $" · led by {Esc(w.Figures[l].Name)}" : "";
            text.Text = $"[b]{Faction.Name(f.Kind)}[/b] [color=#929aa6]({f.Members.Count} communes{leader})[/color]\n"
                        + $"want {Faction.Demand(f.Kind)}\n[color={color}]{bar} {f.Strength:P0}[/color]"
                        + (f.Strength > 0.05 ? $" [color=#929aa6]· drift +{w.Balance.FactionDrift * f.Strength * f.Strength * w.Weeks:0.00}/{w.Turn}[/color]" : "");
            text.TooltipText = $"{Faction.Name(f.Kind)}: {Faction.Why(f.Kind)}.\n"
                               + string.Join(", ", f.Members.Select(id => w.Communes[id].Name))
                               + "\n\nAt full strength they put their demand to the congress. Conceding buys peace at a price; refusing angers their communes"
                               + (f.Kind == FactionKind.Localists ? ", and the most aggrieved leave." : ".");
            text.MouseFilter = MouseFilterEnum.Pass;
            row.AddChild(text);
            var kind = f.Kind;
            var button = AddButton(row, $"Address ({w.Balance.CostAddressFaction:0})",
                $"Delegates meet their communes and answer them point by point: their strength falls by {w.Balance.AddressFactionRelief:P0}.",
                () => Do(new AddressFaction(kind)));
            button.Disabled = f.Strength < 0.05 || w.Influence < w.Balance.CostAddressFaction;
            button.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        }
    }
}
