using System.Linq;
using Godot;
using Politicom.Sim;
using Politicom.Sim.Systems;

namespace Politicom.Game;

/// <summary>
/// The chapter in the Overview tab: the four legacy paths and their milestones (reached, and how
/// far along the next one is), the legacies of past chapters, and the crisis of the age when one
/// is under way, with its stage and the policies chosen.
/// </summary>
public partial class Main
{
    private RichTextLabel _legacy = null!;

    private void BuildLegacy(Container overview)
    {
        _legacy = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Stop };
        _legacy.AddThemeFontSizeOverride("normal_font_size", 13);
        _legacy.AddThemeFontSizeOverride("bold_font_size", 14);
        overview.AddChild(_legacy);
        overview.MoveChild(_legacy, 3); // under the struggle
    }

    private void RefreshLegacy()
    {
        var w = _world;
        var s = w.Legacy;
        if (s.ChapterEnd == 0) { _legacy.Visible = false; return; }
        _legacy.Visible = true;
        bool one = s.ChapterEnd >= w.Scenario.EndTick && s.Chapter == 1;
        var lines = new System.Collections.Generic.List<string>
        {
            one ? "[b]Legacy paths[/b] [color=#929aa6](two milestones on a path make it this game's legacy)[/color]"
                : $"[b]Chapter {s.Chapter}[/b] [color=#929aa6]to {w.Format(w.DateAt(s.ChapterEnd), "MMMM yyyy")} · two milestones on a path leave a legacy[/color]",
        };
        var tip = new System.Collections.Generic.List<string>
        {
            "Each chapter, three milestones on each of four paths. Each one reached lifts enthusiasm a little.",
            one ? "At the end, every path with two or three milestones is the game's legacy."
                : "At the chapter's end, every path with two milestones becomes a legacy, with three a lasting one, and it holds from then on.",
            "",
        };
        foreach (var p in LegacySystem.Paths)
        {
            var ms = LegacySystem.Milestones(w, p);
            string dots = string.Concat(Enumerable.Range(0, 3).Select(i => s.Has(p, i) ? "●" : "○"));
            int next = Enumerable.Range(0, 3).FirstOrDefault(i => !s.Has(p, i), -1);
            string color = s.Count(p) >= 2 ? "#70d080" : s.Count(p) == 1 ? "#e0b050" : "#b8b0a0";
            string level = s.Level(p) > 0 ? $" [color=#d9b866]★{s.Level(p)}[/color]" : "";
            lines.Add($"[color={color}]{dots}[/color] {LegacySystem.Name(p)}{level}"
                      + (next >= 0 ? $" [color=#929aa6]— {Esc(ms[next].Label.ToLowerInvariant())} ({System.Math.Min(0.99, ms[next].Progress):P0})[/color]" : " [color=#70d080]— all three[/color]"));
            tip.Add($"{LegacySystem.Name(p)} → {LegacySystem.LegacyName(p)}: "
                    + string.Join(", ", LegacySystem.LegacyEffects(p, 1).Describe().Select(d => d.Text.Replace(" (holders)", ""))) + " per level"
                    + (s.Level(p) > 0 ? $" (now level {s.Level(p)})" : ""));
            for (int i = 0; i < ms.Length; i++)
                tip.Add($"   {(s.Has(p, i) ? "●" : "○")} {ms[i].Label} ({System.Math.Min(1, ms[i].Progress):P0})");
        }
        if (s.Crisis is { Stage: > 0 } c)
        {
            lines.Add($"[color=#f07060][b]{Esc(c.Name)}[/b] — stage {c.Stage} of 3, until {w.Format(w.DateAt(c.End), "MMMM yyyy")}[/color]"
                      + (c.Kind == CrisisKind.Plague ? $" [color=#929aa6]· in {c.Afflicted.Count(id => !w.Communes[id].Occupied)} communes{(c.Dead > 0.05 ? $", {c.Dead:0.0}k dead" : "")}[/color]" : ""));
            if (c.Policies.Count > 0)
                lines.Add("[color=#929aa6]Policies: " + string.Join("; ", c.Policies.Select(id => CrisisSystem.Find(id)?.Label.ToLowerInvariant() ?? id)) + "[/color]");
            tip.Add("");
            tip.Add($"{c.Name}: a crisis of the age, building in three stages to the chapter's end. Each stage asks for a policy that holds until it ends.");
            foreach (var (label, e) in w.StandingEffects().Where(x => !x.Label.StartsWith("Legacy")))
                tip.Add($"   {label}: {string.Join(", ", e.Describe().Select(d => d.Text.Replace(" (holders)", "")))}");
            tip.Add("Come through it without losing a commune, and the assemblies take heart.");
        }
        else if (CrisisSystem.Enabled(w) && !one)
            tip.Add($"\nIn the last quarter of each chapter, a crisis of the age.");
        if (s.History.Count > 0)
        {
            tip.Add("");
            tip.AddRange(s.History);
        }
        _legacy.Text = string.Join("\n", lines);
        _legacy.TooltipText = string.Join("\n", tip);
    }
}
