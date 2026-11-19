using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>The selected commune's focus, in the Commune tab: one button per focus, the current one pressed.</summary>
public partial class Main
{
    private readonly Dictionary<CommuneFocus, Button> _focusButtons = new();

    private void BuildFocus(Container commune)
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 4);
        commune.AddChild(row);
        var title = new Label { Text = "Focus:", VerticalAlignment = VerticalAlignment.Center, TooltipText = "What the commune puts its effort into. Click to read about fields, factories and workshops (Codex)." };
        row.AddChild(title);
        LinkCodex(title, () => "specialization");
        foreach (var f in Foci.All)
        {
            var focus = f;
            var button = AddButton(row, Foci.Name(f),
                $"{Foci.Why(f)}\n" + string.Join("\n", Foci.Describe(f).Select(d => "  " + d))
                + (_world.Balance.CostFocus > 0 ? $"\n\nAsking the assembly costs {_world.Balance.CostFocus:0} influence." : "\n\nThe assembly decides; asking costs nothing.")
                + $" A commune keeps a focus at least {Foci.SettleWeeks} weeks.",
                () => WithSelected(id => new SetFocus(id, focus)));
            button.ToggleMode = true;
            _focusButtons[f] = button;
        }
    }

    private void RefreshFocus()
    {
        var c = _map.Selected is int id ? _world.Communes[id] : null;
        foreach (var (f, button) in _focusButtons)
        {
            button.SetPressedNoSignal(c != null && c.Focus == f);
            button.Disabled = c == null || c.Occupied || !c.InFederation;
        }
    }
}
