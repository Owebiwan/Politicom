using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim.Global;

namespace Politicom.Game;

/// <summary>
/// Lore for the civilisations on the world maps (tools/ui/build_lore.py): a short history from
/// Wikipedia (CC BY-SA 4.0) and a freely licensed picture, with a line on what the state is in
/// the era being played, from the map's own data.
/// </summary>
internal static class Lore
{
    public sealed record Entry(string Title, string Extract, string Url, string Image, string Credit);

    private static Dictionary<string, Entry>? _all;

    private static Dictionary<string, Entry> All()
    {
        if (_all != null) return _all;
        _all = new();
        const string path = "res://data/lore/lore.json";
        if (!FileAccess.FileExists(path)) return _all;
        foreach (var (key, value) in Json.ParseString(FileAccess.GetFileAsString(path)).AsGodotDictionary())
        {
            var d = value.AsGodotDictionary();
            string S(string k) => d.TryGetValue(k, out var v) ? v.AsString() : "";
            _all[key.AsString()] = new Entry(S("title"), S("extract"), S("url"), S("image"), S("credit"));
        }
        return _all;
    }

    public static Entry? For(string name) => All().TryGetValue(name, out var e) && e.Extract.Length > 0 ? e : null;

    /// <summary>The entry for a state in an era: before 1800 a modern country's name means its older polity (the Mali Empire).</summary>
    public static Entry? For(string name, int year) => (year < 1800 ? For(name + "@early") : null) ?? For(name);

    public static Texture2D? Picture(Entry e) =>
        e.Image.Length > 0 && ResourceLoader.Exists($"res://art/lore/{e.Image}.jpg") ? GD.Load<Texture2D>($"res://art/lore/{e.Image}.jpg") : null;

    /// <summary>"In 1200: a monarchy with its capital at Paris, power 4 of 10, bordering …"</summary>
    public static string EraLine(Atlas atlas, AtlasState s)
    {
        string era = Politicom.Sim.Open.OpenStartGenerator.EraName(atlas.Era);
        string regime = StateText.Name(WorldSetup.Parse(s.Ideology)).ToLowerInvariant();
        string article = "aeiou".Contains(regime[0]) ? "an" : "a";
        var neighbours = s.Neighbours.Select(n => atlas.State(n)?.Name).Where(n => n != null).Take(4).ToList();
        return $"In {era}: {article} {regime}" + (s.Capital is { } c ? $" with its capital at {c.Name}" : "") +
               $", power {s.Power:0} of 10" + (neighbours.Count > 0 ? $", bordering {string.Join(", ", neighbours)}." : ".");
    }

    /// <summary>A lore panel: the picture in a gilt frame, the history, the era line and the credits.</summary>
    public static Control Panel(Atlas atlas, AtlasState s, float width)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        box.AddThemeConstantOverride("separation", 6);
        var entry = For(s.Name, int.TryParse(atlas.Era, out var y) ? y : 1900);
        if (entry != null && Picture(entry) is { } tex)
        {
            var frame = new PanelContainer();
            frame.AddThemeStyleboxOverride("panel", UiTheme.FramePanel(4));
            frame.AddChild(new TextureRect
            {
                Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(width - 8, (width - 8) * 2 / 3),
            });
            box.AddChild(frame);
        }
        var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(width, 0) };
        string Esc(string t) => t.Replace("[", "[lb]");
        text.Text = $"{UiTheme.DisplayText(Esc(s.Name), 18)}\n[color=#c9a25b]{Esc(EraLine(atlas, s))}[/color]" +
                    (entry != null ? $"\n\n{Esc(entry.Extract)}" : "");
        box.AddChild(text);
        if (entry != null)
        {
            var credit = new Label
            {
                Text = $"From Wikipedia, “{entry.Title}” (CC BY-SA 4.0)" + (entry.Credit.Length > 0 ? $" · picture: {entry.Credit}" : ""),
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(width, 0), Modulate = new Color(1, 1, 1, 0.45f),
            };
            credit.AddThemeFontOverride("font", UiTheme.BodyItalic);
            credit.AddThemeFontSizeOverride("font_size", 12);
            box.AddChild(credit);
        }
        return box;
    }
}
