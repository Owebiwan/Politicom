using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

internal static class Palette
{
    public static readonly Color Free = new(0.4f, 0.8f, 0.5f);
    public static readonly Color Councils = new(0.95f, 0.78f, 0.3f);
    public static readonly Color Commissars = new(0.95f, 0.55f, 0.3f);
    public static readonly Color Police = new(0.9f, 0.32f, 0.28f);
    public static readonly Color Federation = new(0.45f, 0.7f, 0.95f);
    public static readonly Color Trust = new(0.75f, 0.6f, 0.95f);
    public static readonly Color Grid = new(1, 1, 1, 0.12f);
    public static readonly Color Text = new(0.75f, 0.77f, 0.8f);

    public static Color Stage(DriftStage s) => s switch
    {
        DriftStage.Free => Free,
        DriftStage.PermanentCouncils => Councils,
        DriftStage.Commissars => Commissars,
        _ => Police,
    };
}

/// <summary>Drift bar split into the four stage zones, with the threshold that ends the game at the right.</summary>
public partial class DriftMeter : Control
{
    public World? World { get; set; }

    public override void _Ready() => CustomMinimumSize = new Vector2(0, 34);

    public override void _Draw()
    {
        if (World is not { } w) return;
        var font = ThemeDB.FallbackFont;
        float barH = 16;
        float width = Size.X;
        float x(double v) => (float)(v / 100) * width;

        double[] bounds = { 0, 25, 50, 75, 100 };
        for (int i = 0; i < 4; i++)
        {
            var zone = new Rect2(x(bounds[i]), 0, x(bounds[i + 1]) - x(bounds[i]), barH);
            DrawRect(zone, new Color(Palette.Stage((DriftStage)i), 0.18f));
        }
        DrawRect(new Rect2(0, 0, x(w.Drift.Value), barH), Palette.Stage(w.Drift.Stage));
        foreach (var t in DriftTracker.Thresholds)
        {
            DrawLine(new Vector2(x(t), 0), new Vector2(x(t), barH + 3), new Color(1, 1, 1, 0.6f), 1);
            string label = t >= 100 ? "state" : t.ToString("0");
            var size = font.GetStringSize(label, HorizontalAlignment.Left, -1, 11);
            DrawString(font, new Vector2(Mathf.Min(x(t) - size.X / 2, width - size.X), barH + 15), label, HorizontalAlignment.Left, -1, 11, Palette.Text);
        }
    }
}

/// <summary>
/// Line chart of the weekly history: drift (coloured by stage zone lines), communes still
/// federated, and mean trust. Answers "how did we get here?" at a glance.
/// </summary>
public partial class HistoryChart : Control
{
    public World? World { get; set; }
    public int FontSize { get; set; } = 11;

    public override void _Ready()
    {
        if (CustomMinimumSize.Y < 1) CustomMinimumSize = new Vector2(0, 130);
    }

    public override void _Draw()
    {
        if (World is not { } w) return;
        var font = ThemeDB.FallbackFont;
        float legendH = FontSize + 8;
        var plot = new Rect2(0, legendH, Size.X, Size.Y - legendH - FontSize - 6);
        DrawRect(plot, new Color(0, 0, 0, 0.25f));
        foreach (var t in DriftTracker.Thresholds.Take(3))
        {
            float y = plot.End.Y - (float)(t / 100) * plot.Size.Y;
            DrawDashedLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), Palette.Grid, 1, 4);
        }

        int end = w.Scenario.EndTick;
        Vector2 At(int tick, double value01) => new(
            plot.Position.X + plot.Size.X * tick / end,
            plot.End.Y - plot.Size.Y * (float)Mathf.Clamp(value01, 0, 1));

        var h = w.History;
        if (h.Count >= 2)
        {
            Line(h.Select(r => At(r.Tick, r.Federated / (double)w.InitialCommuneCount)), Palette.Federation, 2);
            Line(h.Select(r => At(r.Tick, r.MeanTrust)), Palette.Trust, 1.5f);
            for (int i = 1; i < h.Count; i++)
                DrawLine(At(h[i - 1].Tick, h[i - 1].Drift / 100), At(h[i].Tick, h[i].Drift / 100),
                    Palette.Stage(StageOf(h[i].Drift)), 2.5f, true);
        }

        // Offensives as shaded bands.
        foreach (var o in w.Enemies.SelectMany(e => e.Offensives))
        {
            var from = At(o.Tick, 1);
            var to = At(System.Math.Min(o.Tick + o.Duration, end), 0);
            DrawRect(new Rect2(from, to - from), new Color(Palette.Police, 0.12f));
        }

        float lx = 0;
        foreach (var (label, color) in new[] { ("drift", Palette.Councils), ("communes federated", Palette.Federation), ("trust", Palette.Trust), ("offensives", new Color(Palette.Police, 0.5f)) })
        {
            DrawRect(new Rect2(lx, 3, 10, 10), color);
            DrawString(font, new Vector2(lx + 14, FontSize + 1), label, HorizontalAlignment.Left, -1, FontSize, Palette.Text);
            lx += 22 + font.GetStringSize(label, HorizontalAlignment.Left, -1, FontSize).X;
        }
        string format = w.Scenario.DaysPerTick < 7 ? "d MMM yyyy" : "MMM yyyy";
        DrawString(font, new Vector2(0, Size.Y - 2), w.Scenario.StartDate.ToString(format), HorizontalAlignment.Left, -1, FontSize, Palette.Text);
        string last = w.DateAt(end).ToString(format);
        DrawString(font, new Vector2(Size.X - font.GetStringSize(last, HorizontalAlignment.Left, -1, FontSize).X, Size.Y - 2), last, HorizontalAlignment.Left, -1, FontSize, Palette.Text);
    }

    private void Line(IEnumerable<Vector2> points, Color color, float width)
    {
        var arr = points.ToArray();
        if (arr.Length >= 2) DrawPolyline(arr, color, width, true);
    }

    private static DriftStage StageOf(double drift) =>
        drift >= 75 ? DriftStage.Police : drift >= 50 ? DriftStage.Commissars : drift >= 25 ? DriftStage.PermanentCouncils : DriftStage.Free;
}
