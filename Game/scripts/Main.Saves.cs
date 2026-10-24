using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Politicom.Sim;
using FileAccess = Godot.FileAccess;

namespace Politicom.Game;

/// <summary>
/// Saved games and the leaderboard. Games are saved under user://saves/, each with a small
/// .meta.json beside it (name, scenario, date, turn) so the load screen needn't read whole worlds;
/// the quick-save (F5, F9) stays at user://quicksave.json. Every finished game goes on the local
/// leaderboard (user://leaderboard.json), ranked by its total score.
/// </summary>
public partial class Main
{
    private const string SaveDir = "user://saves";
    private const string LeaderboardPath = "user://leaderboard.json";

    private sealed record SaveMeta(string Name, string Scenario, string Date, int Tick, int EndTick, string Turn, DateTime Saved);

    public sealed record LeaderEntry(string Scenario, string ScenarioId, string Outcome, double Total, double Survival, double Quality,
        double Spread, double Freedom, int Turns, int EndTick, string Date, DateTime Played);

    private static readonly JsonSerializerOptions MetaJson = new() { WriteIndented = true };

    private static string MetaPath(string path) => path[..^".json".Length] + ".meta.json";

    // ── Saving ──

    private void SaveTo(string path, string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(SaveDir);
        using (var file = FileAccess.Open(path, FileAccess.ModeFlags.Write))
        {
            if (file is null)
            {
                Say($"Could not save: {FileAccess.GetOpenError()}");
                return;
            }
            file.StoreString(_world.SaveJson());
        }
        var w = _world;
        var meta = new SaveMeta(name, w.Scenario.Name, w.Format(w.Date, "d MMMM yyyy"), w.Tick, w.Scenario.EndTick, w.Turn, DateTime.Now);
        using (var file = FileAccess.Open(MetaPath(path), FileAccess.ModeFlags.Write))
            file?.StoreString(JsonSerializer.Serialize(meta, MetaJson));
        Say($"Saved “{name}”: {w.Turn} {w.Tick} ({w.Format(w.Date, "d MMMM yyyy")}).");
        _toasts.Show($"Saved “{name}”.", new Color(0.5f, 0.85f, 0.55f), 4);
    }

    private void QuickSave() => SaveTo(SavePath, "Quick-save");

    private void LoadFrom(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            Say("That save is gone.");
            return;
        }
        try
        {
            var world = World.LoadJson(FileAccess.GetFileAsString(path), _techs);
            // The world's own copy of the scenario has been mutated by play, so look up (or regenerate) the original.
            _scenario = FindScenario(world.Scenario.Id) ?? _scenario;
            CloseOverlay();
            _inGame = true;
            SetWorld(world);
            Say($"Loaded {world.Turn} {world.Tick} ({world.Format(world.Date, "d MMMM yyyy")}).");
        }
        catch (Exception e)
        {
            Say($"Could not load the save: {e.Message}");
            _toasts.Show($"Could not load the save: {e.Message}", new Color(0.95f, 0.45f, 0.4f), 8);
        }
    }

    private void QuickLoad()
    {
        if (FileAccess.FileExists(SavePath)) LoadFrom(SavePath);
        else Say("There is no quick-save yet.");
    }

    /// <summary>Every save, newest first: the quick-save and the named ones.</summary>
    private static List<(string Path, SaveMeta Meta)> Saves()
    {
        var paths = new List<string>();
        if (FileAccess.FileExists(SavePath)) paths.Add(SavePath);
        if (DirAccess.Open(SaveDir) is { } dir)
            paths.AddRange(dir.GetFiles().Where(f => f.EndsWith(".json") && !f.EndsWith(".meta.json")).Select(f => $"{SaveDir}/{f}"));
        var saves = new List<(string, SaveMeta)>();
        foreach (var path in paths)
        {
            SaveMeta? meta = null;
            try
            {
                if (FileAccess.FileExists(MetaPath(path))) meta = JsonSerializer.Deserialize<SaveMeta>(FileAccess.GetFileAsString(MetaPath(path)));
            }
            catch (JsonException) { }
            // A save from before metadata: name it after its file, dated by the file.
            meta ??= new SaveMeta(path == SavePath ? "Quick-save" : Path.GetFileNameWithoutExtension(path), "", "", 0, 0, "turn",
                DateTimeOffset.FromUnixTimeSeconds((long)FileAccess.GetModifiedTime(path)).LocalDateTime);
            saves.Add((path, meta));
        }
        return saves.OrderByDescending(s => s.Item2.Saved).ToList();
    }

    private static bool HasSaves => FileAccess.FileExists(SavePath) || Saves().Count > 0;

    private static string Slug(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var slug = new string(chars).Trim('_');
        return slug.Length == 0 ? "save" : slug.Length > 60 ? slug[..60] : slug;
    }

    /// <summary>The save screen: a name for a new save, or an existing one to overwrite.</summary>
    private void ShowSaveDialog()
    {
        var w = _world;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        var name = new LineEdit { Text = $"{w.Scenario.Name}, {w.Format(w.Date, "d MMM yyyy")}", CustomMinimumSize = new Vector2(560, 36), MaxLength = 80 };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(name);
        void Save()
        {
            var text = name.Text.Trim();
            if (text.Length == 0) return;
            SaveTo($"{SaveDir}/{Slug(text)}.json", text);
            CloseOverlay();
        }
        name.TextSubmitted += _ => Save();
        AddButton(row, "Save", "", Save).CustomMinimumSize = new Vector2(110, 36);
        box.AddChild(row);
        var existing = Saves().Where(s => s.Path != SavePath).ToList();
        if (existing.Count > 0)
        {
            box.AddChild(UiTheme.SectionTitle("Overwrite a save"));
            box.AddChild(SaveList(existing, (path, meta) => ("Overwrite", () => { SaveTo(path, meta.Name); CloseOverlay(); })));
        }
        ShowOverlay("Save the game", "", box, 760, ("Cancel", CloseOverlay));
        name.GrabFocus();
        name.SelectAll();
    }

    /// <summary>The load screen: every save, newest first, to load or delete.</summary>
    private void ShowLoadDialog(Action? back = null)
    {
        var saves = Saves();
        Control content = saves.Count == 0
            ? new Label { Text = "No saved games yet. Save from the bar at the top of a game (or press F5 to quick-save).", Modulate = new Color(1, 1, 1, 0.6f) }
            : SaveList(saves, (path, _) => ("Load", () => LoadFrom(path)), deletable: true, back);
        ShowOverlay("Load a game", "", content, 820, ("Back", back ?? CloseOverlay));
    }

    private Control SaveList(List<(string Path, SaveMeta Meta)> saves, Func<string, SaveMeta, (string Label, Action OnPress)> action, bool deletable = false, Action? back = null)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, Math.Min(440, saves.Count * 62 + 8)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(list);
        foreach (var (path, meta) in saves)
        {
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", UiTheme.FramePanel(8));
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            card.AddChild(row);
            var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.Text = $"{UiTheme.DisplayText(Esc(meta.Name), 16)}\n[color=#9aa0a8]" +
                        (meta.Scenario.Length > 0 ? $"{Esc(meta.Scenario)} · {meta.Date} · {meta.Turn} {meta.Tick}/{meta.EndTick} · " : "") +
                        $"saved {meta.Saved:d MMM yyyy, HH:mm}[/color]";
            row.AddChild(text);
            var (label, onPress) = action(path, meta);
            var go = AddButton(row, label, "", onPress);
            go.CustomMinimumSize = new Vector2(100, 34);
            go.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            if (deletable)
            {
                var delete = AddButton(row, "Delete", "Delete this save for good.", () => { });
                delete.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                delete.Pressed += () =>
                {
                    // Two presses: the first arms it.
                    if (delete.Text != "Sure?") { delete.Text = "Sure?"; return; }
                    DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
                    DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(MetaPath(path)));
                    ShowLoadDialog(back);
                };
            }
            list.AddChild(card);
        }
        return scroll;
    }

    // ── The leaderboard ──

    private World? _recorded;

    private static List<LeaderEntry> Leaderboard()
    {
        if (!FileAccess.FileExists(LeaderboardPath)) return new();
        try { return JsonSerializer.Deserialize<List<LeaderEntry>>(FileAccess.GetFileAsString(LeaderboardPath)) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Puts a finished game on the leaderboard (once) and returns its rank among games of its scenario.</summary>
    private int? RecordOutcome(string title)
    {
        var w = _world;
        if (ReferenceEquals(_recorded, w) || w.Outcome == Outcome.Ongoing) return null;
        _recorded = w;
        var s = w.Scores;
        var id = ScenarioDefinition.SplitLength(w.Scenario.Id).BaseId;
        var entry = new LeaderEntry(w.Scenario.Name, id, title, Math.Round(s.Total, 1), Math.Round(s.Survival, 1), Math.Round(s.QualityOfLife, 1),
            Math.Round(s.Spread, 1), Math.Round(s.Freedom, 1), w.Tick, w.Scenario.EndTick, w.Format(w.Date, "MMMM yyyy"), DateTime.Now);
        var board = Leaderboard();
        // A finished game loaded again isn't a second result.
        if (!board.Any(e => e.ScenarioId == id && e.Turns == entry.Turns && e.Total == entry.Total && e.Outcome == entry.Outcome))
        {
            board.Add(entry);
            board = board.OrderByDescending(e => e.Total).Take(500).ToList();
            using var file = FileAccess.Open(LeaderboardPath, FileAccess.ModeFlags.Write);
            file?.StoreString(JsonSerializer.Serialize(board, MetaJson));
        }
        return board.Where(e => e.ScenarioId == id).OrderByDescending(e => e.Total).ToList().FindIndex(e => e == entry) + 1;
    }

    /// <summary>The leaderboard: finished games ranked by total score, for all scenarios or one.</summary>
    private void ShowLeaderboard(Action? back = null, string? scenarioId = null)
    {
        var board = Leaderboard();
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        var filter = new OptionButton { CustomMinimumSize = new Vector2(320, 0) };
        var scenarios = board.GroupBy(e => e.ScenarioId).Select(g => (Id: g.Key, Name: g.First().Scenario)).OrderBy(x => x.Name).ToList();
        filter.AddItem("All scenarios");
        foreach (var (_, name) in scenarios) filter.AddItem(name);
        int selected = scenarioId is null ? 0 : scenarios.FindIndex(x => x.Id == scenarioId) + 1;
        filter.Select(Math.Max(0, selected));
        filter.ItemSelected += i => ShowLeaderboard(back, i == 0 ? null : scenarios[(int)i - 1].Id);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        top.AddChild(filter);
        top.AddChild(new Label { Text = "Kept on this computer. Total = the average of the four scores.", Modulate = new Color(1, 1, 1, 0.5f), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        box.AddChild(top);

        var rows = board.Where(e => scenarioId is null || e.ScenarioId == scenarioId).OrderByDescending(e => e.Total).Take(100).ToList();
        if (rows.Count == 0)
        {
            box.AddChild(new Label { Text = "No finished games yet. Play a game to its end and it will be ranked here.", Modulate = new Color(1, 1, 1, 0.6f) });
        }
        else
        {
            var grid = new GridContainer { Columns = 9, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 18);
            grid.AddThemeConstantOverride("v_separation", 6);
            void Cell(string text, bool head = false, Color? color = null, bool right = false)
            {
                var l = new Label { Text = text, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
                if (head)
                {
                    l.AddThemeFontOverride("font", UiTheme.Display);
                    l.AddThemeColorOverride("font_color", new Color("c9a25b"));
                }
                else if (color is { } c) l.AddThemeColorOverride("font_color", c);
                grid.AddChild(l);
            }
            foreach (var h in new[] { "#", "Total", "Scenario", "Outcome", "Survival", "Life", "Spread", "Freedom", "Played" }) Cell(h, head: true);
            for (int i = 0; i < rows.Count; i++)
            {
                var e = rows[i];
                var medal = i switch { 0 => new Color("e3bd6b"), 1 => new Color("c8ccd2"), 2 => new Color("c58a52"), _ => new Color(0.85f, 0.86f, 0.88f) };
                Cell($"{i + 1}", color: medal, right: true);
                Cell($"{e.Total:0.0}", color: medal, right: true);
                Cell(e.Scenario.Length > 34 ? e.Scenario[..33] + "…" : e.Scenario);
                Cell(e.Outcome, color: e.Outcome.StartsWith("The federation endured") ? new Color(0.5f, 0.85f, 0.55f) : new Color(0.95f, 0.55f, 0.45f));
                Cell($"{e.Survival:0}", right: true);
                Cell($"{e.Quality:0}", right: true);
                Cell($"{e.Spread:0}", right: true);
                Cell($"{e.Freedom:0}", right: true);
                Cell($"{e.Played:d MMM yyyy}", color: new Color(0.6f, 0.62f, 0.65f));
            }
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, Math.Min(460, rows.Count * 30 + 40)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            scroll.AddChild(grid);
            box.AddChild(scroll);
        }
        ShowOverlay("Leaderboard", "", box, 1000, ("Back", back ?? CloseOverlay));
    }
}
