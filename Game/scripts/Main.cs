using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;
using Politicom.Sim.Sandbox;

namespace Politicom.Game;

/// <summary>
/// Root of the prototype UI: the map on the left, the movement's view of the society on the right.
/// Every button turns into a <see cref="PlayerAction"/>; the simulation decides what happens.
/// </summary>
public partial class Main : Control
{
    private static readonly double[] SecondsPerTurn = { 0, 1.0, 0.45, 0.15 };

    private World _world = null!;
    private MapView _map = null!;
    private int _speed;
    private double _accumulator;
    private int _seenEvents;

    private Label _header = null!, _influence = null!, _stage = null!, _scores = null!, _feedback = null!;
    private DriftMeter _driftMeter = null!;
    private Button _stepButton = null!;
    private HistoryChart _history = null!;
    private Control? _overlay;
    private CommuneList _communeList = null!;
    private TabContainer _tabs = null!;
    private const int CommuneTab = 2;
    private const int WorldTab = 3;
    private RichTextLabel _worldSummary = null!, _stateDetail = null!;
    private Button _appealButton = null!, _delegatesButton = null!, _agitateButton = null!;
    private VBoxContainer _stateList = null!;
    private string? _selectedState;

    private void SelectState(string id)
    {
        _selectedState = id;
        _map.SelectedState = id;
        _tabs.CurrentTab = WorldTab;
        Refresh();
    }

    private void RefreshWorld()
    {
        var w = _world;
        bool any = w.States.Count > 0;
        _tabs.SetTabHidden(WorldTab, !any);
        if (!any) return;
        string Who(Politicom.Sim.Global.StateActions a) =>
            string.Join(", ", w.States.Where(s => s.Does(a)).OrderByDescending(s => s.Weight).Take(5).Select(s => Esc(s.Name)));
        var parts = new List<string> { $"Embargo: [b]{w.Embargo:P0}[/b] of the outside world refuses to trade." };
        if (Who(Politicom.Sim.Global.StateActions.Intervention) is { Length: > 0 } i)
            parts.Add($"[color=#f07060]Intervening against you:[/color] {i}" + string.Concat(w.Enemies.Where(e => e.Intervention > 0).Select(e => $" (+{e.Intervention:0.0} to the {Esc(e.Name)})")));
        if (Who(Politicom.Sim.Global.StateActions.ArmsSales) is { Length: > 0 } a) parts.Add($"[color=#70d080]Arms from:[/color] {a} ({w.LastArms:0.0} goods per {w.Turn})");
        if (Who(Politicom.Sim.Global.StateActions.Volunteers) is { Length: > 0 } v) parts.Add($"[color=#70d080]Volunteers from:[/color] {v} (+{w.VolunteerDefense:P0} defense)");
        if (w.AppealTicks > 0) parts.Add($"International appeal: {w.Turns(w.AppealTicks)} left.");
        _worldSummary.Text = string.Join("\n", parts);
        _appealButton.Text = $"International appeal ({w.Balance.CostAppeal:0})";

        var selected = _selectedState is { } sid ? w.States.FirstOrDefault(s => s.Id == sid) : null;
        _delegatesButton.Disabled = _agitateButton.Disabled = selected is null;
        _delegatesButton.Text = $"Send delegates ({w.Balance.CostDelegates:0})";
        _agitateButton.Text = $"Agitate workers ({w.Balance.CostAgitate:0})";
        _stateDetail.Text = selected is null
            ? "[color=#808590]Click a country on the map, or one below.[/color]"
            : $"[b]{Esc(selected.Name)}[/b] — {Politicom.Sim.Global.StateText.Name(selected.Ideology)}, weight {selected.Weight:0.0} (power {selected.Power:0}, {(selected.Proximity >= 1 ? "a neighbour" : $"{selected.Proximity:P0} interest at this distance")})\n" +
              $"{Politicom.Sim.Global.StateText.Stance(selected.Stance)} toward you ([b]{selected.Stance:+0.00;-0.00}[/b]) — {Politicom.Sim.Global.StateText.Actions(selected.Actions)}\n" +
              $"Workers radicalized: {selected.Radicalization:P0}\n" +
              "[color=#808590]Thresholds: embargo below −0.30, arms above +0.35, volunteers above +0.50, intervention below −0.60. Order in the federation reassures governments and disappoints sympathizers.[/color]";

        foreach (var child in _stateList.GetChildren()) child.QueueFree();
        foreach (var s in w.States.OrderByDescending(s => s.Weight).Take(12))
        {
            var id = s.Id;
            var row = new Button
            {
                Text = $"{s.Name} — {Politicom.Sim.Global.StateText.Stance(s.Stance)} ({s.Stance:+0.00;-0.00}) · {Politicom.Sim.Global.StateText.Actions(s.Actions)}",
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                ToggleMode = true,
                ButtonPressed = id == _selectedState,
            };
            row.AddThemeColorOverride("font_color", s.Stance < -0.3 ? new Color(0.94f, 0.5f, 0.45f) : s.Stance > 0.3 ? new Color(0.55f, 0.85f, 0.6f) : UiTheme.Text);
            row.Pressed += () => SelectState(id);
            _stateList.AddChild(row);
        }
    }
    private ToastStack _toasts = null!;
    private readonly HashSet<string> _tipsShown = new();
    private ScenarioDefinition _scenario = null!;
    private List<ScenarioDefinition> _scenarios = new();
    private SandboxGenerator? _sandbox;
    private TechCatalog _techs = null!;
    private RichTextLabel _driftDetail = null!, _enemy = null!, _commune = null!, _log = null!;
    private VBoxContainer _decisions = null!, _projects = null!;
    private CheckButton _warCommittee = null!, _trade = null!;
    private OptionButton _projectKind = null!, _idea = null!, _mapMode = null!;
    private readonly List<Button> _speedButtons = new();

    public override void _Ready()
    {
        // Maximize in a standalone window. Inside the editor's Game tab the window is embedded
        // and must stay windowed; the canvas_items stretch scales the UI to whatever size it gets.
        if (!Engine.IsEmbeddedInEditor())
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);

        try
        {
            // Read through Godot's file system so this works from the editor and from an exported .pck.
            _techs = TechCatalog.Parse(ReadData("res://data/technologies.json"));
            _scenarios = DirAccess.GetFilesAt("res://data/scenarios")
                .Where(f => f.EndsWith(".json"))
                .Select(f => ScenarioDefinition.Parse(ReadData($"res://data/scenarios/{f}")))
                .OrderBy(s => s.StartDate)
                .ToList();
            if (_scenarios.Count == 0) throw new System.IO.FileNotFoundException("No scenarios in res://data/scenarios.");
            // The outside states come from the era's world map.
            foreach (var s in _scenarios)
                if (s.World is { } era && LoadAtlas(era) is { } atlas) Politicom.Sim.Global.WorldSetup.Attach(s, atlas);
            _scenario = _scenarios.FirstOrDefault(s => s.Id == "catalonia_1936") ?? _scenarios[0];
            _sandbox = SandboxGenerator.Parse(ReadData("res://data/sandbox/event_templates.json"), _techs);
            _world = World.Create(_scenario, _techs, seed: (ulong)DateTime.Now.Ticks);
        }
        catch (Exception e)
        {
            GD.PushError($"Could not load scenario: {e.Message}");
            AddChild(new Label { Text = $"Could not load scenario:\n{e.Message}" });
            return;
        }
        Theme = UiTheme.Build();
        BuildUi();
        SetWorld(_world);
        var args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--screenshot") < 0 || Array.IndexOf(args, "--intro") >= 0) ShowIntro();
        HandleScreenshotArgs();
    }

    /// <summary>
    /// Dev hook: <c>godot --path Game -- --screenshot out.png [--scenario ID] [--weeks N] [--select ID] [--focus ID] [--zoom Z] [--intro] [--communes] [--tab N] [--techs [ID]] [--policy NAME] [--clock SECONDS] [--world] [--mode NAME]</c>
    /// simulates N weeks, saves a screenshot and quits. Used to check the UI without clicking.
    /// </summary>
    private async void HandleScreenshotArgs()
    {
        var args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--screenshot");
        if (at < 0 || at + 1 >= args.Length) return;
        string path = args[at + 1];
        // Let the default map draw first, as it does behind the intro, so switching exercises the real path.
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        int scenarioAt = Array.IndexOf(args, "--scenario");
        if (scenarioAt >= 0 && scenarioAt + 1 < args.Length && FindScenario(args[scenarioAt + 1]) is { } chosen)
        {
            _scenario = chosen;
            SetWorld(World.Create(chosen, _techs, seed: 1));
        }
        int weeksAt = Array.IndexOf(args, "--weeks");
        int weeks = weeksAt >= 0 && weeksAt + 1 < args.Length ? int.Parse(args[weeksAt + 1]) : 0;
        int selectAt = Array.IndexOf(args, "--select");
        if (selectAt >= 0 && selectAt + 1 < args.Length) OnCommuneClicked(int.Parse(args[selectAt + 1]), false);
        int policyAt = Array.IndexOf(args, "--policy");
        var policy = policyAt >= 0 && policyAt + 1 < args.Length ? Sim.Ai.Policies.Get(args[policyAt + 1]) : null;
        for (int i = 0; i < weeks && _world.Outcome == Outcome.Ongoing; i++)
        {
            policy?.Act(_world);
            StepTurn();
        }
        for (int i = 0; i < 40; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); // let armies slide into place
        int clockAt = Array.IndexOf(args, "--clock");
        if (clockAt >= 0 && clockAt + 1 < args.Length)
        {
            CloseOverlay();
            SetSpeed(1);
            await ToSignal(GetTree().CreateTimer(double.Parse(args[clockAt + 1], System.Globalization.CultureInfo.InvariantCulture)), SceneTreeTimer.SignalName.Timeout);
            SetSpeed(0);
        }
        if (Array.IndexOf(args, "--communes") >= 0) ToggleCommuneList();
        int techAt = Array.IndexOf(args, "--techs");
        if (techAt >= 0)
        {
            var tree = ShowTechTree();
            if (techAt + 1 < args.Length && !args[techAt + 1].StartsWith("--")) tree.Select(args[techAt + 1]);
        }
        int modeAt = Array.IndexOf(args, "--mode");
        if (modeAt >= 0 && modeAt + 1 < args.Length && Enum.TryParse<MapMode>(args[modeAt + 1], true, out var mode))
        {
            _mapMode.Select(_mapMode.GetItemIndex((int)mode));
            _map.Mode = mode;
        }
        if (Array.IndexOf(args, "--world") >= 0) ToggleWorldView();
        int tabAt = Array.IndexOf(args, "--tab");
        if (tabAt >= 0 && tabAt + 1 < args.Length) _tabs.CurrentTab = int.Parse(args[tabAt + 1]);
        Refresh();
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        int focusAt = Array.IndexOf(args, "--focus");
        int zoomAt = Array.IndexOf(args, "--zoom");
        if (focusAt >= 0 && focusAt + 1 < args.Length)
            _map.FocusOn(int.Parse(args[focusAt + 1]), zoomAt >= 0 && zoomAt + 1 < args.Length ? float.Parse(args[zoomAt + 1], System.Globalization.CultureInfo.InvariantCulture) : 2.5f);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GetTree().Quit();
    }

    /// <summary>
    /// Days shown into the current turn. Turns stay a week long, but while the clock runs the
    /// date ticks forward a day at a time, at the same overall pace, and the turn resolves on
    /// its last day.
    /// </summary>
    private int _dayInTurn;

    public override void _Process(double delta)
    {
        if (_speed == 0 || _world.Outcome != Outcome.Ongoing) return;
        _accumulator += delta;
        double secondsPerDay = SecondsPerTurn[_speed] / _world.Scenario.DaysPerTick;
        while (_accumulator >= secondsPerDay && _speed != 0 && _world.Outcome == Outcome.Ongoing)
        {
            _accumulator -= secondsPerDay;
            if (++_dayInTurn >= _world.Scenario.DaysPerTick) StepTurn();
            else UpdateHeader();
        }
    }

    private void UpdateHeader()
    {
        var w = _world;
        var date = w.Date.AddDays(_dayInTurn);
        _header.Text = $"{w.Scenario.Name} · {date:d MMM yyyy} · {w.Turn} {w.Tick}/{w.Scenario.EndTick}"
                       + (SandboxGenerator.TryParseId(ScenarioDefinition.SplitLength(w.Scenario.Id).BaseId, out var mapSeed, out _) ? $"  · seed {mapSeed}" : "");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (_overlay != null)
        {
            if (key.Keycode == Key.Escape)
            {
                CloseOverlay();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        switch (key.Keycode)
        {
            case Key.C: ToggleCommuneList(); break;
            case Key.K: ShowTechTree(); break;
            case Key.W: ToggleWorldView(); break;
            case Key.T:
                _map.ShowTrustLinks = !_map.ShowTrustLinks;
                _map.QueueRedraw();
                break;
            case Key.Escape when _communeList.Visible: ToggleCommuneList(); break;
            case Key.Space: SetSpeed(_speed == 0 ? 1 : 0); break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(2); break;
            case Key.Key3: SetSpeed(3); break;
            case Key.Period: StepTurn(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void StepTurn()
    {
        _dayInTurn = 0;
        if (_world.Outcome != Outcome.Ongoing) return;
        _world.Step();
        // Like a Paradox popup: pause when the movement has a decision to make.
        var fresh = _world.Log.Skip(_seenEvents).ToList();
        if (fresh.Any(e => e.StopsTheClock)) SetSpeed(0);
        foreach (var e in fresh.Where(e => e.Severity == Severity.Danger || e.StopsTheClock))
            _toasts.Show(Shorten(e.Text), e.Severity == Severity.Decision ? new Color(0.38f, 0.85f, 0.95f) : new Color(0.95f, 0.4f, 0.35f));
        Refresh();
        ShowTips();
        OnOutcome();
    }

    private TechTreePanel ShowTechTree()
    {
        var panel = new TechTreePanel(_world, action =>
        {
            var result = _world.Apply(action);
            Refresh();
            return result;
        });
        ShowOverlay("Technologies",
            "Communes discover ideas from their own education, needs and values, and pass them along trust links. Most cut both ways.",
            panel, 1500, ("Close", CloseOverlay));
        return panel;
    }

    private void ToggleCommuneList()
    {
        _communeList.Visible = !_communeList.Visible;
        _communeList.World = _world;
        _communeList.Refresh();
    }

    /// <summary>One-time hints the first time something important happens in a game.</summary>
    private void ShowTips()
    {
        var w = _world;
        if (w.Outcome != Outcome.Ongoing) return;
        void Tip(string key, bool when, Func<string> text)
        {
            if (when && _tipsShown.Add(key)) _toasts.Show("Tip: " + text(), new Color(0.95f, 0.8f, 0.35f), 12);
        }
        var losing = w.Federation.FirstOrDefault(c => c.Front && c.Pressure > c.Defense);
        Tip("front", losing != null, () => $"{losing!.Name} is losing ground. Propose a defensive campaign, or form the war committee — it adds drift every {w.Turn} it sits.");
        var hungry = w.Federation.FirstOrDefault(c => c.Starving);
        Tip("famine", hungry != null, () => $"{hungry!.Name} is starving. Propose famine relief for it, or mediate with its neighbours so they share more.");
        var experts = w.Federation.FirstOrDefault(c => c.ExpertGap > 0.12);
        Tip("experts", experts != null, () => $"In {experts!.Name}, practical education is running ahead of political education. That expert trap adds drift; fund political schooling there.");
        Tip("leaving", w.PendingSecessions.Count > 0, () => "A commune is voting to leave. Select it and mediate with its neighbours to change its mind; holding it by force costs trust and drift.");
        Tip("councils", w.Drift.Stage >= DriftStage.PermanentCouncils, () => "Drift passed 25. Recall delegates to push it back down — a stage only reverts once drift falls well below its threshold.");
        Tip("influence", w.Influence >= w.Balance.InfluenceCap - 0.5, () => "Influence is full and anything more is wasted. Spend it: fund schools, mediate, circulate ideas.");
    }

    private static string Shorten(string text) => text.Length <= 160 ? text : text[..157].TrimEnd() + "…";

    private readonly Dictionary<string, GeoData?> _geo = new();
    private readonly Dictionary<string, Politicom.Sim.Global.Atlas?> _atlases = new();

    /// <summary>The world map of an era, if Data/world has it.</summary>
    private Politicom.Sim.Global.Atlas? LoadAtlas(string? era)
    {
        if (era is null) return null;
        if (_atlases.TryGetValue(era, out var cached)) return cached;
        string path = $"res://data/world/{era}.json";
        Politicom.Sim.Global.Atlas? atlas = null;
        if (FileAccess.FileExists(path))
        {
            try { atlas = Politicom.Sim.Global.Atlas.Parse(ReadData(path)); }
            catch (Exception e) { GD.PushError($"Could not read {path}: {e.Message}"); }
        }
        return _atlases[era] = atlas;
    }

    private Button _worldButton = null!;

    private void ToggleWorldView()
    {
        if (_map.Globe is null) return;
        if (_map.AtWorldZoom) _map.ShowRegion();
        else _map.ShowWorld();
        _worldButton.Text = _map.AtWorldZoom ? "Region" : "World";
    }

    /// <summary>Real geography for a scenario, if Data/geo has it (generated maps don't).</summary>
    private GeoData? LoadGeo(string scenarioId)
    {
        if (_geo.TryGetValue(scenarioId, out var cached)) return cached;
        string path = $"res://data/geo/{scenarioId}.json";
        GeoData? geo = null;
        if (FileAccess.FileExists(path))
        {
            try { geo = GeoData.Parse(ReadData(path)); }
            catch (Exception e) { GD.PushError($"Could not read {path}: {e.Message}"); }
        }
        return _geo[scenarioId] = geo;
    }

    private void SetWorld(World world)
    {
        _world = world;
        _selectedState = null;
        _dayInTurn = 0;
        _map.Globe = LoadAtlas(world.Scenario.World)
                     ?? (world.Scenario.Geography is { Neighbours.Count: > 0 } g ? Politicom.Sim.Global.Atlas.FromGenerated(g) : null);
        _map.SelectedState = null;
        _map.Geo = world.Scenario.Geography is { } generated
            ? GeoData.From(generated)
            : LoadGeo(ScenarioDefinition.SplitLength(world.Scenario.Id).BaseId);
        _tipsShown.Clear();
        _communeList.World = world;
        _map.World = _driftMeter.World = _history.World = world;
        _map.Selected = _map.Secondary = null;
        _map.Banner = null;
        SetSpeed(0);
        Refresh();
    }

    private void NewGame()
    {
        CloseOverlay();
        SetWorld(World.Create(_scenario, _techs, seed: (ulong)DateTime.Now.Ticks));
        Say("A new game begins.");
    }

    // ── Overlays ──────────────────────────────────────────────────────────────

    private void ShowIntro()
    {
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 10);
        foreach (var scenario in _scenarios)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 16);
            var end = scenario.StartDate.AddDays(scenario.DaysPerTick * scenario.EndTick);
            var text = new RichTextLabel
            {
                BbcodeEnabled = true, FitContent = true, ScrollActive = false,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(820, 0),
                Text = $"[b]{Esc(scenario.Name)}[/b]  [color=#9aa0a8]{(end.Year == scenario.StartDate.Year ? $"{end.Year}" : $"{scenario.StartDate.Year}–{end.Year}")} · {scenario.Communes.Count} communes · "
                       + $"against {Esc(string.Join(" and ", scenario.Enemies.Select(e => e.Name)))}[/color]\n{Esc(scenario.Description)}",
            };
            text.AddThemeFontSizeOverride("normal_font_size", 14);
            text.AddThemeFontSizeOverride("bold_font_size", 16);
            row.AddChild(text);
            var chosen = scenario;
            string turn = scenario.DaysPerTick switch { 7 => "week", 1 => "day", _ => "turn" };
            var length = LengthPicker(scenario.EndTick, turn, scenario.Id == "paris_commune_1871"
                ? LongerTooltip + " Paris has never outlasted Bloody Week: a longer game asks whether you can."
                : LongerTooltip);
            length.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(length);
            var play = AddButton(row, "Play", "", () =>
            {
                int turns = length.GetSelectedId();
                StartScenario(turns == chosen.EndTick ? chosen : chosen.WithLength(turns));
            });
            play.CustomMinimumSize = new Vector2(110, 40);
            play.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            list.AddChild(row);
            list.AddChild(new HSeparator());
        }
        if (_sandbox != null) list.AddChild(SandboxRow());

        // Four scenarios don't fit on a small screen with the rules above them: let the list scroll.
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(PickerWidth - 56, 470), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(list);

        ShowOverlay("Choose a revolution", """
            [b]You are the movement, not the government.[/b] You can't order a commune to do anything. You fund schools, mediate disputes, circulate ideas and propose projects; the communes decide for themselves.

            [b]The dilemma.[/b] Almost everything that helps you win the war (a war committee, government arms, conscription, appointed commanders) pushes [color=#f2c94c]hierarchy drift[/color] up. At 25, 50 and 75 drift, permanent councils, then commissars, then a police force take over, each with real bonuses. At 100 a state has crystallized and the revolution is over, however strong the society is. Political education, recall and rotation are the brakes.

            [b]Winning.[/b] Hold out as a free federation until the scenario ends; you're scored on survival, quality of life, spread and freedom. [b]Controls:[/b] click a commune, shift-click a second; Space pauses, 1–3 set speed; scroll to zoom, drag to pan.
            """, scroll, PickerWidth);
    }

    private const float PickerWidth = 1040;

    /// <summary>A scenario by id, including generated maps and longer games ("…@300").</summary>
    private ScenarioDefinition? FindScenario(string id)
    {
        var (baseId, length) = ScenarioDefinition.SplitLength(id);
        if (SandboxGenerator.TryParseId(baseId, out var seed, out var size) && _sandbox != null)
            return _sandbox.Generate(seed, size, length: length);
        var found = _scenarios.FirstOrDefault(s => s.Id == baseId);
        return found is null || length is not int turns ? found : found.WithLength(turns);
    }

    /// <summary>Historical length, or a longer war up to 500 turns.</summary>
    private static OptionButton LengthPicker(int historical, string turn, string tooltip)
    {
        var picker = new OptionButton { TooltipText = tooltip };
        picker.AddItem($"Historical · {historical} {turn}s", historical);
        foreach (int n in new[] { 250, 500 })
            if (n > historical) picker.AddItem($"{n} {turn}s", n);
        return picker;
    }

    private const string LongerTooltip =
        "After the historical events, the war goes on: the enemy launches renewed offensives, and drift keeps building. " +
        "Every line of technology has a free path that brakes drift; in a long game you will need them.";

    /// <summary>The Sandbox row: seed, size and Play. Generation (with calibration) takes well under a second.</summary>
    private Control SandboxRow()
    {
        var box = new VBoxContainer();
        var text = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(820, 0),
            Text = "[b]Sandbox[/b]  [color=#9aa0a8]a generated region · two years · one or two enemies[/color]\n"
                   + "An alternate-history map with its own communes, fronts and dilemmas, tuned to be winnable but hard. Same seed, same map.",
        };
        text.AddThemeFontSizeOverride("normal_font_size", 14);
        text.AddThemeFontSizeOverride("bold_font_size", 16);
        box.AddChild(text);

        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 8);
        controls.AddChild(new Label { Text = "Seed" });
        var seed = new LineEdit { Text = NewSeed().ToString(), CustomMinimumSize = new Vector2(140, 0), MaxLength = 12 };
        seed.TextChanged += t =>
        {
            string digits = new(t.Where(char.IsDigit).ToArray());
            if (digits != t)
            {
                seed.Text = digits;
                seed.CaretColumn = digits.Length;
            }
        };
        controls.AddChild(seed);
        AddButton(controls, "Random", "Pick a new seed.", () => seed.Text = NewSeed().ToString());
        controls.AddChild(new Label { Text = "Communes" });
        var size = new OptionButton();
        foreach (var n in SandboxGenerator.Sizes) size.AddItem(n.ToString(), n);
        size.Select(Array.IndexOf(SandboxGenerator.Sizes, SandboxGenerator.DefaultSize));
        controls.AddChild(size);
        var sandboxLength = LengthPicker(104, "week", LongerTooltip + " Longer maps take a few seconds to calibrate.");
        controls.AddChild(sandboxLength);
        controls.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var play = AddButton(controls, "Play", "", () =>
        {
            ulong value = ulong.TryParse(seed.Text, out var v) ? v : NewSeed();
            int turns = sandboxLength.GetSelectedId();
            StartScenario(_sandbox!.Generate(value, size.GetSelectedId(), length: turns == 104 ? null : turns));
        });
        play.CustomMinimumSize = new Vector2(110, 40);
        box.AddChild(controls);
        return box;
    }

    private static ulong NewSeed() => (ulong)Random.Shared.Next(1, 1_000_000);

    private void StartScenario(ScenarioDefinition scenario)
    {
        _scenario = scenario;
        NewGame();
        Say($"{scenario.Name} begins.");
        _toasts.Show("Tip: press Space or ▶ to start time. Click any commune to see it; press C for a list of all of them.", new Color(0.95f, 0.8f, 0.35f), 12);
    }

    private void OnOutcome()
    {
        if (_world.Outcome == Outcome.Ongoing || _overlay != null) return;
        SetSpeed(0);
        var w = _world;
        var s = w.Scores;
        (string title, string verdict) = w.Outcome switch
        {
            Outcome.Survived => ("The federation endured", $"Free communes still governed themselves in {w.Date:MMMM yyyy}."),
            Outcome.BecameState => ("You won the war and lost the revolution", "Hierarchy drift reached its final threshold. A state crystallized out of the councils, commissars and police."),
            _ when w.Communes.Count(c => !c.Occupied && !c.Federated) > w.Communes.Count(c => c.Occupied)
                => ("The federation has dissolved", "Most communes went their own way. They are still free, but there is no federation left to hold together."),
            _ => ("The federation has been destroyed", "Too few free communes remain for a federation."),
        };
        _map.Banner = title;
        var chart = new HistoryChart { World = w, FontSize = 13, CustomMinimumSize = new Vector2(0, 220) };
        ShowOverlay(title, $"""
            {verdict}

            [b]Survival[/b] {s.Survival:0}    [b]Quality of life[/b] {s.QualityOfLife:0}    [b]Spread[/b] {s.Spread:0}    [b]Freedom[/b] {s.Freedom:0}    —    [b]Total {s.Total:0}[/b]

            {w.Federation.Count()} of {w.InitialCommuneCount} communes free and federated · {w.Communes.Count(c => c.Occupied)} occupied · {w.Communes.Count(c => !c.Occupied && !c.Federated)} seceded · final drift {w.Drift.Value:0} ({DriftEffects.Name(w.Drift.Stage)}){(SandboxGenerator.TryParseId(ScenarioDefinition.SplitLength(w.Scenario.Id).BaseId, out var mapSeed, out var mapSize) ? $"\n\nSandbox map: seed {mapSeed}, {mapSize} communes." : "")}
            """, chart, ("Keep looking", CloseOverlay), ("Play again", NewGame), ("Choose scenario", ShowIntro));
    }

    private void ShowOverlay(string title, string body, Control? extra, params (string Label, Action OnPress)[] buttons) =>
        ShowOverlay(title, body, extra, 760, buttons);

    private void ShowOverlay(string title, string body, Control? extra, float width, params (string Label, Action OnPress)[] buttons)
    {
        CloseOverlay();
        SetSpeed(0);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.65f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.11f, 0.12f, 0.14f),
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 28, ContentMarginRight = 28, ContentMarginTop = 24, ContentMarginBottom = 24,
        });
        center.AddChild(panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 14);
        panel.AddChild(v);

        var heading = new Label { Text = title };
        heading.AddThemeFontSizeOverride("font_size", 26);
        v.AddChild(heading);
        var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(width - 56, 0), Text = body.Trim() };
        text.AddThemeFontSizeOverride("normal_font_size", 15);
        text.AddThemeFontSizeOverride("bold_font_size", 15);
        v.AddChild(text);
        if (extra != null) v.AddChild(extra);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 10);
        foreach (var (label, onPress) in buttons)
        {
            var b = AddButton(row, label, "", onPress);
            b.CustomMinimumSize = new Vector2(120, 36);
        }
        v.AddChild(row);

        AddChild(dim);
        _overlay = dim;
    }

    private void CloseOverlay()
    {
        _overlay?.QueueFree();
        _overlay = null;
    }

    private void Do(PlayerAction action)
    {
        var result = _world.Apply(action);
        _feedback.Text = result.Message;
        _feedback.AddThemeColorOverride("font_color", result.Ok ? new Color(0.5f, 0.85f, 0.55f) : new Color(0.95f, 0.45f, 0.4f));
        Refresh();
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        var root = new HBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        _map = new MapView { World = _world, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3 };
        _map.CommuneClicked += OnCommuneClicked;
        _map.StateClicked += SelectState;
        root.AddChild(_map);

        // Top-right of the map, clear of the commune list (bottom-left). They fade after a few seconds.
        _toasts = new ToastStack
        {
            AnchorLeft = 1, AnchorRight = 1,
            OffsetLeft = -460, OffsetRight = -16, OffsetTop = 16, OffsetBottom = 300,
        };
        _map.AddChild(_toasts);
        _communeList = new CommuneList
        {
            Visible = false,
            AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = 16, OffsetRight = 776, OffsetTop = -420, OffsetBottom = -56,
        };
        _communeList.CommuneChosen += id =>
        {
            _map.Selected = id;
            _map.Secondary = null;
            _map.CenterOn(id);
            Refresh();
        };
        _map.AddChild(_communeList);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(480, 0) };
        root.AddChild(panel);
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (var edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{edge}", 12);
        panel.AddChild(margin);
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddChild(side);

        // ── Always visible: where and when, the clock, influence, drift and anything waiting on you ──
        _header = AddLabel(side, 16);

        var clock = new HBoxContainer();
        side.AddChild(clock);
        foreach (var (label, speed) in new[] { ("❚❚", 0), ("▶", 1), ("▶▶", 2), ("▶▶▶", 3) })
        {
            var b = new Button { Text = label, ToggleMode = true, TooltipText = "Space pauses; 1–3 set speed", CustomMinimumSize = new Vector2(40, 0) };
            b.Pressed += () => SetSpeed(speed);
            clock.AddChild(b);
            _speedButtons.Add(b);
        }
        _stepButton = new Button { TooltipText = "Advance one turn (.)" };
        _stepButton.Pressed += StepTurn;
        clock.AddChild(_stepButton);
        clock.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        AddButton(clock, "Save", "Save to a single quick-save slot.", SaveGame);
        AddButton(clock, "Load", "Load the quick-save.", LoadGame);

        var view = new HBoxContainer();
        side.AddChild(view);
        _worldButton = AddButton(view, "World", "Zoom out to the whole world, or back to the region (W).", ToggleWorldView);
        AddButton(view, "Communes", "Sortable list of every commune (C).", ToggleCommuneList);
        AddButton(view, "Technologies", "The tech tree: what has spread, what it costs, and what to circulate (K).", () => ShowTechTree());
        _mapMode = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "What the map colours show." };
        foreach (var mode in Enum.GetValues<MapMode>()) _mapMode.AddItem($"Map: {mode}", (int)mode);
        _mapMode.ItemSelected += index =>
        {
            _map.Mode = (MapMode)_mapMode.GetItemId((int)index);
            _map.QueueRedraw();
        };
        view.AddChild(_mapMode);
        var links = new CheckButton { Text = "Trust links", ButtonPressed = true, TooltipText = "Draw the trust network over the map (T)." };
        links.Toggled += on =>
        {
            _map.ShowTrustLinks = on;
            _map.QueueRedraw();
        };
        view.AddChild(links);

        _influence = AddLabel(side);
        _influence.MouseFilter = MouseFilterEnum.Stop;
        _influence.TooltipText = "Influence is the movement's only resource. It regenerates every turn from political education, wellbeing and the press, up to 100.\nIt pays for schools, pamphlets, mediation, projects, congresses, recall.";
        _stage = AddLabel(side, 14);
        _driftMeter = new DriftMeter();
        side.AddChild(_driftMeter);

        _decisions = new VBoxContainer();
        side.AddChild(_decisions);
        _feedback = AddLabel(side, 13);

        // ── Everything else, one tab at a time ──
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        side.AddChild(_tabs);

        var overview = Tab("Overview");
        _driftDetail = AddRich(overview);
        Section(overview, "Outside pressure");
        _enemy = AddRich(overview);
        _scores = AddLabel(overview);
        _scores.MouseFilter = MouseFilterEnum.Stop;
        _scores.TooltipText = "Survival: how long, and how much of, the federation stays free.\nQuality of life: food, wellbeing and education across communes.\nSpread: how radicalized workers behind enemy lines become.\nFreedom: how low hierarchy drift has stayed, on average.";
        Section(overview, "So far");
        _history = new HistoryChart();
        overview.AddChild(_history);

        var movement = Tab("Movement");
        movement.AddChild(UiTheme.SectionTitle("Emergency measures"));
        var measures = new HBoxContainer();
        movement.AddChild(measures);
        _warCommittee = new CheckButton { Text = "War committee", TooltipText = "Defense +35% and cheaper coordination — drift every turn it sits." };
        _warCommittee.Toggled += on => { if (on != _world.WarCommittee) Do(new SetWarCommittee(on)); };
        measures.AddChild(_warCommittee);
        _trade = new CheckButton { Text = "Foreign trade", TooltipText = "Sell surplus grain abroad for arms. Buyers want currency, which brings markets and accounts back in (drift every turn). Embargoes grow as the idea spreads." };
        _trade.Toggled += on => { if (on != _world.ForeignTrade) Do(new SetForeignTrade(on)); };
        measures.AddChild(_trade);
        var federation = new HBoxContainer();
        movement.AddChild(federation);
        AddButton(federation, "Recall delegates", "Drift down, experience lost for 4 turns.", () => Do(new RecallDelegates()));
        AddButton(federation, "Sponsor congress", "Spread techniques across the federation. Leaves a secretariat behind (drift).", () => Do(new SponsorCongress()));

        Section(movement, "Propose a project");
        var propose = new HBoxContainer();
        movement.AddChild(propose);
        _projectKind = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var p in ProjectCatalog.All)
        {
            _projectKind.AddItem(p.Name, (int)p.Kind);
            _projectKind.SetItemTooltip(_projectKind.ItemCount - 1, p.Description);
        }
        propose.AddChild(_projectKind);
        AddButton(propose, "Propose", "Uses the selected commune (and the shift-selected one for railways).", ProposeProject);

        Section(movement, "Circulate an idea");
        var circulate = new HBoxContainer();
        movement.AddChild(circulate);
        _idea = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        for (int i = 0; i < _world.Techs.All.Count; i++)
        {
            var t = _world.Techs.All[i];
            _idea.AddItem(t.Name, i);
            _idea.SetItemTooltip(i, $"{t.Name}\n+ {t.Perk}\n− {t.Risk}\n\n" + string.Join("\n", t.Effects.Describe().Select(d => (d.Good ? "+ " : "− ") + d.Text)));
        }
        circulate.AddChild(_idea);
        AddButton(circulate, "Circulate", "Steer what communes discover and adopt for 20 turns.",
            () => Do(new CirculateIdea(_world.Techs.All[_idea.GetSelectedId()].Id)));

        Section(movement, "Projects under way");
        _projects = new VBoxContainer();
        movement.AddChild(_projects);

        var commune = Tab("Commune");
        _commune = AddRich(commune);
        var actions = new HFlowContainer();
        commune.AddChild(actions);
        AddButton(actions, "Practical school", "Productivity and innovation. Watch the expert trap.", () => WithSelected(id => new FundSchool(id, EducationTrack.Practical)));
        AddButton(actions, "Political school", "Cheaper coordination, slower drift, more trust.", () => WithSelected(id => new FundSchool(id, EducationTrack.Political)));
        AddButton(actions, "Pamphlets", "Political education here and next door.", () => WithSelected(id => new CirculatePamphlets(id)));
        AddButton(actions, "Mediate", "Raise trust with the shift-selected neighbour.", () =>
        {
            if (_map.Selected is int a && _map.Secondary is int b) Do(new Mediate(a, b));
            else Say("Select one commune, then shift-click a neighbour.");
        });

        var worldTab = Tab("World");
        _worldSummary = AddRich(worldTab);
        var appeal = new HBoxContainer();
        worldTab.AddChild(appeal);
        _appealButton = AddButton(appeal, "International appeal", "Appeal to the workers of the world: twice the volunteers for a while. Sympathetic states warm a little.", () => Do(new InternationalAppeal()));
        Section(worldTab, "Selected country");
        _stateDetail = AddRich(worldTab);
        var stateActions = new HFlowContainer();
        worldTab.AddChild(stateActions);
        _delegatesButton = AddButton(stateActions, "Send delegates", "Meetings, newspapers and unions: the state warms toward you. Fascist governments barely listen.", () =>
        {
            if (_selectedState is { } id) Do(new SendDelegates(id));
        });
        _agitateButton = AddButton(stateActions, "Agitate workers", "Radicalize the state's workers. Its government notices and cools toward you.", () =>
        {
            if (_selectedState is { } id) Do(new AgitateWorkers(id));
        });
        Section(worldTab, "The powers that matter most to you");
        _stateList = new VBoxContainer();
        worldTab.AddChild(_stateList);

        var log = Tab("Log");
        _log = AddRich(log);

        SetSpeed(0);
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void Refresh()
    {
        var w = _world;
        UpdateHeader();
        _stepButton.Text = $"+1 {w.Turn}";
        _influence.Text = $"Influence {w.Influence:0}  (+{w.InfluenceRegen:0.0}/{w.Turn})    Federation {w.Federation.Count()}/{w.InitialCommuneCount}";

        _stage.Text = $"{DriftEffects.Name(w.Drift.Stage)} — {w.Drift.Value:0.0}" + (w.Drift.NextThreshold is { } t ? $"  (next stage at {t:0})" : "");
        _driftMeter.QueueRedraw();
        _driftMeter.TooltipText =
            "Hierarchy drift: informal power piling up.\n" +
            "25 Permanent councils: +4% output, cheaper coordination; assemblies consulted less.\n" +
            "50 Commissars: +20% defense; trust and political education suffer.\n" +
            "75 Police force: +30% defense, free-riding ends by coercion; trust erodes everywhere.\n" +
            "100 A state: the revolution is over. Game lost.\n\n" +
            "A stage only reverts once drift falls 10 below its threshold. Brakes: political education, recall, mandates, rotation.";
        if (_communeList.Visible) _communeList.Refresh();
        _history.QueueRedraw();
        var d = w.LastDrift;
        _driftDetail.Text = $"[i]{DriftEffects.Describe(w.Drift.Stage)}[/i]\nLast {w.Turn} {(d.Net >= 0 ? "+" : "")}{d.Net:0.00}: " + string.Join(", ",
            d.Items().Where(i => Math.Abs(i.Value) > 0.005).OrderByDescending(i => Math.Abs(i.Value)).Take(5)
                .Select(i => $"{Esc(i.Label)} [color={(i.Value > 0 ? "#f07060" : "#70d080")}]{(i.Value > 0 ? "+" : "")}{i.Value:0.00}[/color]"));

        var lines = w.Enemies.Select(e =>
        {
            int k = w.Enemies.ToList().IndexOf(e);
            var fronts = w.Active.Where(c => c.Fronts.Contains(k))
                .Select(c => $"{Esc(c.Name)} [color={(c.Defense >= c.Pressure ? "#70d080" : "#f07060")}]{c.Control:P0}[/color]");
            return $"[b][color={e.Color}]■[/color] {Esc(e.Name)}[/b]: strength {e.EffectiveStrength:0.0}"
                   + (e.CurrentOffensive is { } o ? $"  [color=#f07060][b]{Esc(o.Name)}[/b][/color]" : "")
                   + $" · exhaustion {e.Exhaustion:P0} · its workers radicalized {e.Radicalization:P0}"
                   + $"\nFront: {(fronts.Any() ? string.Join(", ", fronts) : "none")}";
        });
        _enemy.Text = string.Join("\n", lines)
                      + $"\nTrade: {(w.ForeignTrade ? $"selling {w.LastExports:0} food for {w.LastImports:0} goods per {w.Turn}" : "closed")} · embargo {w.Embargo:P0}"
                      + (w.Ally.MilitarizationTicks > 0 ? $"\nMilitarized for {w.Turns(w.Ally.MilitarizationTicks)} more." : "");
        var s = w.Scores;
        _scores.Text = $"Survival {s.Survival:0} · Quality of life {s.QualityOfLife:0} · Spread {s.Spread:0} · Freedom {s.Freedom:0}";

        RefreshWorld();
        _warCommittee.SetPressedNoSignal(w.WarCommittee);
        _trade.SetPressedNoSignal(w.ForeignTrade);
        RefreshDecisions();
        RefreshCommune();
        RefreshProjects();

        var recent = w.Log.TakeLast(30).Reverse().Select(ev =>
            $"[color=#808590]{ev.Date:d MMM yy}[/color] [color={SeverityColor(ev.Severity)}]{Esc(ev.Text)}[/color]");
        _log.Text = string.Join("\n", recent);
        _seenEvents = w.Log.Count;

        _map.QueueRedraw();
    }

    private void RefreshDecisions()
    {
        foreach (var child in _decisions.GetChildren()) child.QueueFree();
        var w = _world;
        if (w.Outcome != Outcome.Ongoing) return;
        foreach (var pending in w.PendingEvents)
        {
            var def = w.EventDefinition(pending.Id);
            var options = def.Options.Select((o, i) => (
                $"{o.Label}",
                $"{o.Description}\n\n{o.Effects.Summary(w.Turn)}" + (i == def.DefaultOption ? "\n\n(happens if you don't answer)" : ""),
                (Action)(() => Do(new ChooseEventOption(def.Id, i))))).ToArray();
            AddDecision($"{def.Title}\n{def.Text}  (answer by {w.Turn} {pending.Expires})", options);
        }
        if (w.Ally.OfferPending)
        {
            AddDecision($"The {w.Ally.Name} offers arms if the militias accept militarization. (expires {w.Turn} {w.Ally.OfferExpires})",
                ("Accept arms", $"+30% defense for {w.Turns(w.Balance.MilitarizationTicks)}; +5 drift now and more while it lasts.", () => Do(new AnswerAllyOffer(true))),
                ("Refuse", "The militias stay under their own control.", () => Do(new AnswerAllyOffer(false))));
        }
        foreach (var sec in w.PendingSecessions)
        {
            var c = w.Communes[sec.Commune];
            AddDecision($"{c.Name} votes to leave the federation in {w.Turn} {sec.Deadline}. Mediate with its neighbours, let it go, or hold it by force.",
                ("Select it", "Select it, then shift-click a neighbour and Mediate.", () => { _map.Selected = c.Id; Refresh(); }),
                ("Hold by force", "Costs influence, its trust with every neighbour, and drift.", () => Do(new HoldCommune(c.Id))));
        }
    }

    private void AddDecision(string text, params (string Label, string Tooltip, Action OnPress)[] options)
    {
        var box = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = new Color(0.1f, 0.25f, 0.3f), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6 };
        box.AddThemeStyleboxOverride("panel", style);
        var v = new VBoxContainer();
        box.AddChild(v);
        v.AddChild(new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) });
        var row = new HFlowContainer();
        v.AddChild(row);
        foreach (var (label, tooltip, onPress) in options) AddButton(row, label, tooltip, onPress);
        _decisions.AddChild(box);
    }

    private void RefreshCommune()
    {
        var w = _world;
        if (_map.Selected is not int id)
        {
            _commune.Text = "[color=#808590]Click a commune on the map.[/color]";
            return;
        }
        var c = w.Communes[id];
        string status = c.Occupied ? "[color=#f07060]occupied[/color]" : !c.Federated ? "[color=#e0c050]seceded[/color]" : c.Front ? $"[color=#f07060]front, control {c.Control:P0}[/color]" : "federated";
        string neighbours = string.Join(", ", w.Trust.Neighbors(id).Select(n => $"{Esc(w.Communes[n].Name)} {w.Trust[id, n]:P0}"));
        _commune.Text =
            $"[b]{Esc(c.Name)}[/b] — {c.Kind}, {c.Population:0}k, {status}\n" +
            (c.Description.Length > 0 ? $"[i]{Esc(c.Description)}[/i]\n" : "") +
            $"Values: collectivism {c.Values.Collectivism:+0.0;-0.0}, militancy {c.Values.Militancy:+0.0;-0.0}, federalism {c.Values.Federalism:+0.0;-0.0}\n" +
            $"Education: practical {c.PracticalEducation:0.00} (schooling {c.PracticalSchooling:P0}), political {c.PoliticalEducation:0.00} (schooling {c.PoliticalSchooling:P0})" +
            (c.ExpertGap > 0.1 ? " [color=#f0a040]expert trap[/color]" : "") + "\n" +
            $"Food {Math.Max(0, c.Food):0} (+{c.FoodProduced:0.0}) · goods {Math.Max(0, c.Goods):0} (+{c.GoodsProduced:0.0}) · wellbeing {c.Wellbeing:P0}\n" +
            $"Aid given {c.AidGiven:0.0} / received {c.AidReceived:0.0} · free-riding {c.FreeRiding:P0} · discontent {c.Discontent:0.00}\n" +
            $"Techs: {(c.Techs.Count == 0 ? "none" : string.Join(", ", c.Techs.Select(t => w.Techs[t].Name)))}\n" +
            $"Neighbours (trust): {neighbours}";
    }

    private void RefreshProjects()
    {
        foreach (var child in _projects.GetChildren()) child.QueueFree();
        var w = _world;
        var active = w.ActiveProjects.ToList();
        if (active.Count == 0)
        {
            _projects.AddChild(new Label { Text = "None. Propose one above.", Modulate = new Color(1, 1, 1, 0.5f) });
            return;
        }
        foreach (var p in active)
        {
            var row = new VBoxContainer();
            string target = p.Target is int a ? $" — {w.Communes[a].Name}" + (p.Target2 is int b ? $" ↔ {w.Communes[b].Name}" : "") : "";
            row.AddChild(new Label
            {
                Text = $"#{p.Id} {p.Definition.Name}{target}: {p.Participants.Count}/{p.Invited.Count} joined, {p.LastOverhead:P0} lost to coordination, due {w.Turn} {p.Deadline}"
                       + (p.ProjectedFinish(w.Tick) is int t ? (t > p.Deadline ? $" — on pace for {w.Turn} {t}: LATE" : $" — on pace for {w.Turn} {t}") : " — stalled"),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(420, 0),
            });
            var bar = new HBoxContainer();
            bar.AddChild(new ProgressBar { MinValue = 0, MaxValue = p.Work, Value = p.Progress, SizeFlagsHorizontal = SizeFlags.ExpandFill, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 12) });
            int id = p.Id;
            AddButton(bar, "Persuade selected", "Make the case in the selected commune's assembly.", () => WithSelected(c => new Persuade(c, id)));
            row.AddChild(bar);
            _projects.AddChild(row);
        }
    }

    // ── Save / load ───────────────────────────────────────────────────────────

    private const string SavePath = "user://quicksave.json";

    private void SaveGame()
    {
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            Say($"Could not save: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(_world.SaveJson());
        Say($"Saved {_world.Turn} {_world.Tick} ({_world.Date:d MMMM yyyy}).");
    }

    private void LoadGame()
    {
        if (!FileAccess.FileExists(SavePath))
        {
            Say("There is no quick-save yet.");
            return;
        }
        try
        {
            var world = World.LoadJson(FileAccess.GetFileAsString(SavePath), _techs);
            // The world's own copy of the scenario has been mutated by play, so look up (or regenerate) the original.
            _scenario = FindScenario(world.Scenario.Id) ?? _scenario;
            CloseOverlay();
            SetWorld(world);
            Say($"Loaded {world.Turn} {world.Tick} ({world.Date:d MMMM yyyy}).");
        }
        catch (Exception e)
        {
            Say($"Could not load the quick-save: {e.Message}");
        }
    }

    // ── Input helpers ─────────────────────────────────────────────────────────

    private void OnCommuneClicked(int id, bool shift)
    {
        _tabs.CurrentTab = CommuneTab;
        if (shift && _map.Selected is int sel && sel != id) _map.Secondary = id;
        else
        {
            _map.Selected = id;
            _map.Secondary = null;
        }
        Refresh();
    }

    private void ProposeProject()
    {
        var kind = (ProjectKind)_projectKind.GetSelectedId();
        var def = ProjectCatalog.Get(kind);
        if (def.TargetsNeeded >= 1 && _map.Selected is null) { Say($"{def.Name} needs a selected commune."); return; }
        if (def.TargetsNeeded >= 2 && _map.Secondary is null) { Say($"{def.Name} needs a second commune: shift-click it."); return; }
        Do(new ProposeProject(kind,
            def.TargetsNeeded >= 1 ? _map.Selected : null,
            def.TargetsNeeded >= 2 ? _map.Secondary : null));
    }

    private void WithSelected(Func<int, PlayerAction> make)
    {
        if (_map.Selected is int id) Do(make(id));
        else Say("Select a commune on the map first.");
    }

    private void SetSpeed(int speed)
    {
        _speed = speed;
        _accumulator = 0;
        for (int i = 0; i < _speedButtons.Count; i++) _speedButtons[i].SetPressedNoSignal(i == speed);
    }

    private void Say(string text)
    {
        _feedback.Text = text;
        _feedback.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.4f));
    }

    private static Label AddLabel(Container parent, int size = 14)
    {
        var l = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) };
        l.AddThemeFontSizeOverride("font_size", size);
        parent.AddChild(l);
        return l;
    }

    private static RichTextLabel AddRich(Container parent)
    {
        var r = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(420, 0) };
        parent.AddChild(r);
        return r;
    }

    private static void Section(Container parent, string title)
    {
        parent.AddChild(new HSeparator());
        parent.AddChild(UiTheme.SectionTitle(title));
    }

    /// <summary>A scrolling tab page; TabContainer takes the title from the child's name.</summary>
    private VBoxContainer Tab(string title)
    {
        var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _tabs.AddChild(scroll);
        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(page);
        return page;
    }

    private static Button AddButton(Container parent, string text, string tooltip, Action onPress)
    {
        var b = new Button { Text = text, TooltipText = tooltip };
        b.Pressed += onPress;
        parent.AddChild(b);
        return b;
    }

    private static string SeverityColor(Severity s) => s switch
    {
        Severity.Good => "#80d890",
        Severity.Warning => "#e8c060",
        Severity.Danger => "#f07060",
        Severity.Decision => "#60d8f0",
        _ => "#c8ccd2",
    };

    private static string ReadData(string path)
    {
        string text = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
            throw new System.IO.FileNotFoundException($"{path} ({FileAccess.GetOpenError()}). Build the project once so Data/ is mirrored into res://data.");
        return text;
    }

    private static string Esc(string s) => s.Replace("[", "[lb]");
}
