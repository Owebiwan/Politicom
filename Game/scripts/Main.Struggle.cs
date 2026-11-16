using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>The struggle between allies in the Overview tab: its phase, its tension, and what moved it.</summary>
public partial class Main
{
    private RichTextLabel _struggle = null!;

    private void BuildStruggle(Container overview)
    {
        _struggle = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Stop };
        _struggle.AddThemeFontSizeOverride("normal_font_size", 13);
        _struggle.AddThemeFontSizeOverride("bold_font_size", 14);
        overview.AddChild(_struggle);
        overview.MoveChild(_struggle, 2); // under Factions
    }

    private void RefreshStruggle()
    {
        var w = _world;
        _struggle.Visible = w.Struggle != null;
        if (w.Struggle is not { } s) return;
        string color = s.Phase switch { StrugglePhase.Cooperation => "#70d080", StrugglePhase.Tension => "#e0b050", _ => "#f07060" };
        // A bar of 20 cells, the phase boundaries at 35 and 65 and the breaking point at 90.
        int filled = (int)System.Math.Round(s.Tension / 5);
        string bar = string.Concat(Enumerable.Range(0, 20).Select(i => i < filled ? "█" : i is 7 or 13 or 18 ? "┆" : "░"));
        _struggle.Text = $"[b]{Esc(s.Name)}[/b] [color=#929aa6]with the {Esc(s.With)}[/color]\n"
                         + $"[color={color}]{Struggle.PhaseName(s.Phase)}[/color]: {Struggle.PhaseEffect(s.Phase)}\n"
                         + $"[color={color}]{bar}[/color] tension {s.Tension:0}"
                         + (s.Recognized ? " · [color=#70d080]the collectives are recognized[/color]" : s.Tension >= 75 ? " · [color=#f07060]near breaking point (90)[/color]" : "");
        _struggle.TooltipText = "The struggle with the movement's allies of convenience. At 90 it breaks into open conflict; kept under 20 for half a year, they recognize the collectives.\n\n"
                                + $"Each {w.Turn}:\n" + string.Join("\n", s.LastChange.Select(p => $"  {p.Value:+0.00;-0.00}  {p.Label}"))
                                + (s.Catalysts.Count > 0 ? "\n\nChoices that moved it:\n" + string.Join("\n", s.Catalysts.Select(c => $"  {c.Amount:+0;-0}  {c.Why} ({w.Format(w.DateAt(c.Tick), "MMM yyyy")})")) : "");
    }
}
