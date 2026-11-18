using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// Pictures for events: each event is matched to a theme by its words (war, uprising, assembly,
/// hunger, harvest, work, faith, plague, fire, flood, refugees, festival, trade, press, town)
/// and to the game's age, and shown in a gilt frame at the top of its pop-up. Pictures are
/// public-domain period art processed to one sepia style (tools/ui/process_event_art.py),
/// and scenes rendered for the game from its own models where no period art fits (the
/// anniversary, mourning, the barricades: tools/art/render_event_art.py), credited under the frame.
/// </summary>
internal static class EventArt
{
    private static readonly (string Theme, string[] Words)[] Themes =
    {
        // Scenes rendered for Politicom (tools/art/render_event_art.py), before the period art's themes.
        ("celebration", new[] { "of the revolution", "anniversary", "liberation", "festival of the free" }),
        ("mourning", new[] { " is dead", "has died", "mourns", "funeral", "did not come back", "has fallen at" }),
        ("barricades", new[] { "barricade" }),
        ("plague", new[] { "plague", "typhus", "epidemic", "pestilence", "disease", "cholera", "influenza", "sickness" }),
        ("fire", new[] { "fire " , "fire in", "burn" }),
        ("flood", new[] { "flood", "river burst" }),
        ("refugees", new[] { "refugee", "fleeing" }),
        ("festival", new[] { "festival", "feast", "sings", "songs" }),
        ("press", new[] { "press", "printer", "paper", "journalist", "radio", "television", "hacker", "rumour", "broadsheet" }),
        ("faith", new[] { "oracle", "priest", "temple", "relic", "heresy", "heretic", "excommunic", "crusade", "witch", "inquisition", "comet", "church", "tithe" }),
        ("trade", new[] { "bank", "gold", "trade", "merchant", "privateer", "sanction", "treasur", "sell", "arms for", "buy" }),
        ("hunger", new[] { "famine", "hunger", "starv", "requisition", "hoard", "drought", "bread", "dearth" }),
        ("harvest", new[] { "harvest", "commons", "fences", "granar", "grain", "seed", "manor" }),
        ("work", new[] { "workshop", "machine", "factory", "guild", "engineer", "mill", "general strike", "on strike", "strikers", "works" }),
        ("assembly", new[] { "assembly", "council", "elect", "congress", "committee", "ministr", "coalition", "constitution", "governor", "delegat", "federate", "kinship", "commissar", "counter-revolution" }),
        ("uprising", new[] { "revolt", "rises", "rising", "barricade", "revolution", "insurrect", "gladiator", "slave", "rolls burn" }),
        ("war", new[] { "offensive", "battle", "army", "front", "deserter", "mercenar", "levy", "conscript", "defect", "campaign", "raid", "squad", "spy", "war", "bomb", "intervenes", "falls to" }),
    };

    private static readonly string[] Eras = { "ancient", "medieval", "earlymodern", "industrial", "modern", "contemporary" };

    private static int EraIndex(int year) => year < 500 ? 0 : year < 1450 ? 1 : year < 1789 ? 2 : year < 1914 ? 3 : year < 1950 ? 4 : 5;

    private static Dictionary<string, (string Caption, string Credit)>? _credits;

    private static Dictionary<string, (string Caption, string Credit)> Credits()
    {
        if (_credits != null) return _credits;
        _credits = new();
        const string path = "res://data/events/art_credits.json";
        if (!FileAccess.FileExists(path)) return _credits;
        var json = Json.ParseString(FileAccess.GetFileAsString(path)).AsGodotDictionary();
        foreach (var (key, value) in json)
        {
            var d = value.AsGodotDictionary();
            _credits[key.AsString()] = (d.TryGetValue("caption", out var c) ? c.AsString() : "", d.TryGetValue("credit", out var cr) ? cr.AsString() : "");
        }
        return _credits;
    }

    private static bool Exists(string slot) => Credits().ContainsKey(slot) && ResourceLoader.Exists($"res://art/events/{Uri(slot)}.jpg");
    private static bool Has(string slot) => Exists(slot);
    /// <summary>File name for a slot: variants "war_industrial#2" are stored as "war_industrial-2".</summary>
    private static string Uri(string slot) => slot.Replace('#', '-');

    /// <summary>The slot or one of its variants (slot#2, slot#3…), the same one for the same event.</summary>
    private static string Variant(string slot, string key)
    {
        var all = new List<string> { slot };
        for (int n = 2; Exists($"{slot}#{n}"); n++) all.Add($"{slot}#{n}");
        uint h = 2166136261;
        foreach (char ch in key) h = (h ^ ch) * 16777619;
        return all[(int)(h % (uint)all.Count)];
    }

    /// <summary>The picture slot for an event, or null if there's none to show.</summary>
    public static string? Slot(World w, string title, string text) => Pick(w, title, text) is { } slot ? Variant(slot, title) : null;

    private static string? Pick(World w, string title, string text)
    {
        string words = $" {title} {text} ".ToLowerInvariant();
        var matched = Themes.FirstOrDefault(t => t.Words.Any(words.Contains)).Theme;
        int era = EraIndex(w.Year);
        // The theme in this age, then in the nearest ages (later first: a picture a little modern
        // beats one far too old), then the theme with no age. An event whose theme has no
        // picture gets a neutral town scene, never another theme's (no barricade for a fire).
        // The historical scenarios have pictures of their own history, checked first.
        string? place = w.Scenario.Id switch
        {
            var id when id.StartsWith("catalonia") => "catalonia",
            var id when id.StartsWith("paris") => "paris",
            var id when id.StartsWith("free_territory") => "ukraine",
            _ => null,
        };
        if (place != null && Has($"{matched ?? "uprising"}_{place}")) return $"{matched ?? "uprising"}_{place}";
        foreach (var t in matched is null ? new[] { "uprising", "town" } : new[] { matched, "town" })
        {
            foreach (int d in new[] { 0, 1, -1, 2, -2, 3, -3 })
            {
                int i = era + d;
                if (i >= 0 && i < Eras.Length && Has($"{t}_{Eras[i]}")) return $"{t}_{Eras[i]}";
            }
            if (Has(t)) return t;
        }
        return null;
    }

    /// <summary>The picture in a gilt frame with its credit, sized for the pop-up.</summary>
    public static Control Frame(string slot, float width)
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel", UiTheme.FramePanel(5));
        var picture = new TextureRect
        {
            Texture = GD.Load<Texture2D>($"res://art/events/{Uri(slot)}.jpg"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            CustomMinimumSize = new Vector2(width - 10, (width - 10) * 5 / 12),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(picture);
        box.AddChild(frame);
        var (caption, credit) = Credits()[slot];
        var line = new Label
        {
            Text = caption.Length > 0 ? $"{caption} — {credit}" : credit,
            HorizontalAlignment = HorizontalAlignment.Right, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(width, 0), Modulate = new Color(1, 1, 1, 0.45f),
        };
        line.AddThemeFontOverride("font", UiTheme.BodyItalic);
        line.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(line);
        return box;
    }
}
