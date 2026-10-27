using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim.Global;

namespace Politicom.Game;

/// <summary>
/// Other countries' cities on the map, from the era's atlas: each a small settlement ringed in its
/// country's colour, the capital ringed in gold, the largest named where there's room. Clicking
/// one opens the mission to it (emissaries, agitators). Of the federation's own country only the
/// seat of government is shown; the rest of its towns are communes.
/// </summary>
public partial class MapView
{
    private sealed record ForeignCity(AtlasState State, City City, Vector2 At, bool Capital);

    private List<ForeignCity>? _cityLayout;
    private GlobeLayout? _cityLayoutFor;
    private Politicom.Sim.World? _cityLayoutWorld;
    /// <summary>The cities drawn last frame, in screen space, for clicks and hovering.</summary>
    private readonly List<(ForeignCity City, Vector2 At)> _citiesShown = new();
    private readonly List<(ForeignCity City, Vector2 At)> _cityPick = new();
    private object? _cityPickKey;
    private (AtlasState State, City City)? _hoverCity;

    private List<ForeignCity> CityLayout(GlobeLayout g)
    {
        if (_cityLayout != null && ReferenceEquals(_cityLayoutFor, g) && ReferenceEquals(_cityLayoutWorld, World)) return _cityLayout;
        _cityLayoutFor = g;
        _cityLayoutWorld = World;
        string? home = World?.Scenario.HomeState;
        var communes = World?.Communes.Select(c => c.Name.ToLowerInvariant()).ToHashSet() ?? new HashSet<string>();
        return _cityLayout = Globe!.States
            .SelectMany(s => s.Cities.Select(c => new ForeignCity(s, c, _project(new Vector2((float)c.Lon, (float)c.Lat)), c.Name == s.Capital?.Name)))
            .Where(f => f.State.Id != home || f.Capital)
            .Where(f => !communes.Contains(f.City.Name.ToLowerInvariant()))
            .OrderByDescending(f => f.Capital).ThenByDescending(f => f.City.Pop)
            .ToList();
    }

    private void DrawForeignCities(Font font, List<Rect2> taken)
    {
        _citiesShown.Clear();
        if (Globe_Layout() is not { } g || World is not { } w) return;
        var layout = CityLayout(g);
        var view = ViewRect.Grow(-4);
        var communes = w.Communes.Select(c => ToScreen(c)).ToList();
        var recent = w.Missions.Where(m => m.Tick >= w.Tick - 26).ToList();
        string sprite = w.Year < 1750 ? "oldtown" : "town";
        var icon = UiTheme.Icon(sprite);
        var flag = UiTheme.Icon("t_flag");
        // Which cities show depends only on the view and where the communes are: worked out again
        // when those change, not on every frame of an animation.
        var key = (_pan, _zoom, Size, ViewRect, w.Communes.Count, g);
        if (!key.Equals(_cityPickKey))
        {
            _cityPickKey = key;
            _cityPick.Clear();
            foreach (var f in layout)
            {
                if (_cityPick.Count >= 160) break;
                var p = ToScreen(f.At);
                if (!view.HasPoint(p)) continue;
                if (communes.Any(c => c.DistanceSquaredTo(p) < 20 * 20)) continue;
                if (_cityPick.Any(s => s.At.DistanceSquaredTo(p) < 24 * 24)) continue;
                _cityPick.Add((f, p));
            }
        }
        foreach (var (f, p) in _cityPick)
        {
            _citiesShown.Add((f, p));

            float side = Mathf.Clamp(11 + 2.5f * Mathf.Log(Mathf.Max(f.City.Pop, 10_000) / 100_000f) / Mathf.Log(10), 10, 17);
            bool hovered = _hoverCity?.City == f.City;
            var ring = f.Capital ? new Color("d9b45a") : Color.FromString(f.State.Color, new Color("888888")).Lightened(0.15f);
            DrawCircle(p, side * 0.62f, new Color(0.06f, 0.06f, 0.07f, 0.85f));
            DrawArc(p, side * 0.62f, 0, Mathf.Tau, 24, ring, f.Capital ? 2f : 1.4f, true);
            if (hovered) DrawCircle(p, side * 0.8f, new Color(1, 1, 1, 0.16f));
            if (icon != null)
                DrawTextureRect(icon, new Rect2(p - new Vector2(side, side) * 0.45f, new Vector2(side, side) * 0.9f), false, new Color(0.82f, 0.8f, 0.74f));
            if (flag != null && recent.Any(m => m.StateId == f.State.Id && m.City == f.City.Name))
                DrawTextureRect(flag, new Rect2(p + new Vector2(side * 0.15f, -side * 1.25f), new Vector2(12, 12)), false, new Color("e0443a"));

            var labelAt = p + new Vector2(side * 0.62f + 3, 4);
            var line = ShapedLabel(f.City.Name, 11);
            var size = line.GetSize();
            var rect = new Rect2(labelAt - new Vector2(0, size.Y - 3), size);
            if (!hovered && taken.Any(t => t.Intersects(rect))) continue;
            taken.Add(rect);
            var top = labelAt - new Vector2(0, (float)line.GetLineAscent());
            line.DrawOutline(GetCanvasItem(), top, 4, Border);
            line.Draw(GetCanvasItem(), top, f.Capital ? new Color("e8d3a0") : new Color(0.72f, 0.72f, 0.7f));
        }
    }

    /// <summary>A window over the map takes the pointer: drop the hover tooltips.</summary>
    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit) return;
        _hover = null;
        _hoverCity = null;
        _hoverState = null;
        QueueRedraw();
    }

    private static readonly Dictionary<(string, int), TextLine> ShapedLabels = new();

    /// <summary>A label's shaped text, kept: shaping hundreds of names every redraw is the costly part of drawing them.</summary>
    private static TextLine ShapedLabel(string text, int size)
    {
        if (ShapedLabels.TryGetValue((text, size), out var line)) return line;
        line = new TextLine();
        line.AddString(text, ThemeDB.FallbackFont, size);
        return ShapedLabels[(text, size)] = line;
    }

    private (AtlasState State, City City)? HitCity(Vector2 at)
    {
        if (AtWorldZoom) return null;
        foreach (var (f, p) in _citiesShown)
            if (p.DistanceSquaredTo(at) < 11 * 11) return (f.State, f.City);
        return null;
    }

    private void DrawCityTooltip(Font font, AtlasState s, City city)
    {
        var sim = World?.States.FirstOrDefault(x => x.Id == s.Id);
        string pop = city.Pop >= 1_000_000 ? $"{city.Pop / 1e6:0.#} million" : city.Pop >= 1000 ? $"{city.Pop / 1000:0} thousand" : $"{city.Pop}";
        var lines = new List<string>
        {
            city.Name,
            (city.Name == s.Capital?.Name ? "Capital of " : "A city of ") + (sim?.Name ?? s.Name) + $" · about {pop} people",
        };
        if (sim != null)
        {
            lines.Add($"{char.ToUpperInvariant(StateText.Stance(sim.Stance)[0])}{StateText.Stance(sim.Stance)[1..]} toward you · workers radicalized {sim.Radicalization:P0}");
            var last = World!.Missions.LastOrDefault(m => m.StateId == s.Id && m.City == city.Name);
            if (last != null) lines.Add($"Your {(last.Agitation ? "agitators" : "emissaries")} were here {World.Turns(World.Tick - last.Tick)} ago");
            lines.Add("Click to send emissaries or agitators");
        }
        else lines.Add("The seat of your country's government");
        float width = lines.Max(l => font.GetStringSize(l, HorizontalAlignment.Left, -1, 13).X) + 20;
        float height = lines.Count * 17 + 12;
        var at = UiMouse + new Vector2(16, 16);
        if (at.X + width > Size.X) at.X = UiMouse.X - width - 8;
        if (at.Y + height > Size.Y) at.Y = UiMouse.Y - height - 8;
        Ui.DrawStyleBox(UiTheme.FramePanel(0), new Rect2(at, new Vector2(width, height)).Grow(4));
        for (int i = 0; i < lines.Count; i++)
            Ui.DrawString(font, at + new Vector2(10, 20 + i * 17), lines[i], HorizontalAlignment.Left, -1, i == 0 ? 15 : 13, i == 0 ? Colors.White : i == lines.Count - 1 && sim != null ? new Color("d9b45a") : Label);
    }
}
