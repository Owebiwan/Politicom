using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Politicom.Game;

/// <summary>
/// The Codex (J): what the game's ideas, practices and dangers come from, the histories behind
/// its scenarios and the people in them (Data/codex.json). Each entry says what it was, how it
/// appears in the game, and what to read next; entries link to one another. Historical events
/// open their entry from their pop-up, and the drift line opens the entry on drift.
/// </summary>
public partial class Main
{
    private sealed record CodexEntry(string Id, string Category, string Title, string Text, string Game,
                                     string[] Reading, string[] Related, string[] Events);

    private static List<string>? _codexCategories;
    private static Dictionary<string, CodexEntry>? _codex;

    private static Dictionary<string, CodexEntry> Codex()
    {
        if (_codex != null) return _codex;
        _codex = new();
        _codexCategories = new();
        const string path = "res://data/codex.json";
        if (!FileAccess.FileExists(path)) return _codex;
        var root = Json.ParseString(FileAccess.GetFileAsString(path)).AsGodotDictionary();
        foreach (var c in root["categories"].AsGodotArray()) _codexCategories.Add(c.AsString());
        foreach (var item in root["entries"].AsGodotArray())
        {
            var d = item.AsGodotDictionary();
            string S(string k) => d.TryGetValue(k, out var v) ? v.AsString() : "";
            string[] A(string k) => d.TryGetValue(k, out var v) ? v.AsGodotArray().Select(x => x.AsString()).ToArray() : System.Array.Empty<string>();
            var e = new CodexEntry(S("id"), S("category"), S("title"), S("text"), S("game"), A("reading"), A("related"), A("events"));
            _codex[e.Id] = e;
        }
        return _codex;
    }

    /// <summary>The entry about a scripted event, if there is one.</summary>
    private static CodexEntry? CodexForEvent(string eventId) =>
        Codex().Values.FirstOrDefault(e => e.Events.Any(pattern => pattern.EndsWith('*') ? eventId.StartsWith(pattern[..^1]) : pattern == eventId));

    /// <summary>Makes a dashboard label open a Codex entry when clicked (`entry` decides which, at the moment of the click).</summary>
    private void LinkCodex(Control label, System.Func<string> entry)
    {
        label.MouseFilter = MouseFilterEnum.Stop;
        label.MouseDefaultCursorShape = CursorShape.PointingHand;
        label.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) ShowCodex(entry());
        };
    }

    private void ShowCodex(string? id = null)
    {
        var all = Codex();
        if (all.Count == 0) return;
        var current = id != null && all.TryGetValue(id, out var found) ? found : all.Values.First();

        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 600) };
        row.AddThemeConstantOverride("separation", 18);

        // The index: entries by category.
        var indexScroll = new ScrollContainer { CustomMinimumSize = new Vector2(280, 600), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var index = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        index.AddThemeConstantOverride("separation", 2);
        indexScroll.AddChild(index);
        row.AddChild(indexScroll);
        foreach (var category in _codexCategories!)
        {
            var heading = new Label { Text = category.ToUpperInvariant() };
            heading.AddThemeFontOverride("font", UiTheme.Display);
            heading.AddThemeFontSizeOverride("font_size", 13);
            heading.AddThemeColorOverride("font_color", UiTheme.GoldBright);
            index.AddChild(heading);
            foreach (var e in all.Values.Where(x => x.Category == category))
            {
                var link = new Button { Text = e.Title, Flat = true, Alignment = HorizontalAlignment.Left, ToggleMode = true, ButtonPressed = e == current, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = e.Title };
                link.AddThemeFontSizeOverride("font_size", 14);
                string target = e.Id;
                link.Pressed += () => ShowCodex(target);
                index.AddChild(link);
            }
            index.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        }

        // The entry.
        var pageScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(640, 600), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var page = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(620, 0),
        };
        page.AddThemeFontSizeOverride("normal_font_size", 16);
        page.AddThemeFontSizeOverride("bold_font_size", 16);
        page.AddThemeFontSizeOverride("italics_font_size", 16);
        page.MetaClicked += meta => ShowCodex(meta.AsString());
        pageScroll.AddChild(page);
        row.AddChild(pageScroll);

        var text = new List<string>
        {
            $"[font_size=26][color=#{UiTheme.GoldBright.ToHtml(false)}]{Esc(current.Title)}[/color][/font_size]",
            $"[color=#929aa6]{Esc(current.Category)}[/color]",
            "",
            Esc(current.Text),
        };
        if (current.Game.Length > 0)
        {
            text.Add("");
            text.Add("[b]In the game[/b]");
            text.Add($"[color=#c9d7b8]{Esc(current.Game)}[/color]");
        }
        if (current.Reading.Length > 0)
        {
            text.Add("");
            text.Add("[b]Further reading[/b]");
            text.AddRange(current.Reading.Select(r => $"   · [i]{Esc(r)}[/i]"));
        }
        var related = current.Related.Where(all.ContainsKey).Select(r => all[r]).ToList();
        if (related.Count > 0)
        {
            text.Add("");
            text.Add("[b]See also[/b]   " + string.Join("  ·  ", related.Select(r => $"[url={r.Id}][color=#e8c66a]{Esc(r.Title)}[/color][/url]")));
        }
        page.Text = string.Join("\n", text);

        ShowOverlay("Codex", "", row, 1060, ("Close", () => CloseOverlay()));
    }
}
