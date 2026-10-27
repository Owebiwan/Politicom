using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;
using Politicom.Sim.Sandbox;
using Politicom.Sim.Open;

namespace Politicom.Game;

/// <summary>
/// Root of the prototype UI: the map on the left, the movement's view of the society on the right.
/// Every button turns into a <see cref="PlayerAction"/>; the simulation decides what happens.
/// </summary>
public partial class Main : Control
{
    private static readonly double[] SecondsPerTurn = { 0, 1.0, 0.45, 0.15, 0.04 }; // 4: campaign speed (key only)

    private World _world = null!;
    private MapView _map = null!;
    private int _speed;
    private double _accumulator;
    private int _seenEvents;

    private Label _header = null!, _stage = null!, _scores = null!, _feedback = null!;
    private readonly Dictionary<string, RichTextLabel> _status = new();
    private DriftMeter _driftMeter = null!;
    private Button _stepButton = null!;
    private HistoryChart _history = null!;
    private Control? _overlay;
    private Map3D _map3d = null!;
    private Button _fortifyButton = null!, _raidButton = null!;

    private static bool LoadTiltSetting()
    {
        var config = new ConfigFile();
        return config.Load("user://settings.cfg") != Error.Ok || config.GetValue("map", "tilt", true).AsBool();
    }

    private static void SaveTiltSetting(bool tilt)
    {
        var config = new ConfigFile();
        config.Load("user://settings.cfg");
        config.SetValue("map", "tilt", tilt);
        config.Save("user://settings.cfg");
    }
    /// <summary>The start menu, while it's open; and whether a game has been started or loaded since launch.</summary>
    private Control? _menu;
    private bool _inGame;
    private CommuneList _communeList = null!;
    private TabContainer _tabs = null!;
    private const int CommuneTab = 2;
    private const int WorldTab = 3;
    private RichTextLabel _worldSummary = null!, _stateDetail = null!;
    private Button _appealButton = null!, _delegatesButton = null!, _agitateButton = null!, _supportButton = null!;
    private VBoxContainer _stateList = null!;
    private string? _selectedState;
    private VBoxContainer _stateLore = null!;
    private string? _loreShown;

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
        string Names(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => Esc(w.States.FirstOrDefault(s => s.Id == id)?.Name ?? id)));
        foreach (var war in w.Wars.Where(x => !x.Over).Take(4))
            parts.Add($"[color=#e0a040]{Esc(war.Name)}[/color]: {Names(war.SideA)} against {Names(war.SideB)}");
        _worldSummary.Text = string.Join("\n", parts);
        _appealButton.Text = $"International appeal ({w.Balance.CostAppeal:0})";

        var selected = _selectedState is { } sid ? w.States.FirstOrDefault(s => s.Id == sid) : null;
        _delegatesButton.Disabled = _agitateButton.Disabled = selected is null || selected.Sister;
        _supportButton.Visible = selected is { Uprising: > 0 };
        _supportButton.Text = $"Support the rising ({w.Balance.CostSupportUprising:0})";
        _delegatesButton.Text = $"Send emissaries ({w.Balance.CostDelegates:0})";
        _agitateButton.Text = $"Agitate workers ({w.Balance.CostAgitate:0})";
        // The selected country's lore, rebuilt only when the selection changes.
        string? loreKey = selected is null ? null : $"{_map.Globe?.Era}:{selected.Id}";
        if (loreKey != _loreShown)
        {
            _loreShown = loreKey;
            foreach (var child in _stateLore.GetChildren()) child.QueueFree();
            if (selected != null && _map.Globe is { } atlas && atlas.State(selected.Id) is { } st)
                _stateLore.AddChild(Lore.Panel(atlas, st, 330));
        }
        _stateDetail.Text = selected is null
            ? "[color=#808590]Click a country on the map, or one below.[/color]"
            : $"[b]{Esc(selected.Name)}[/b] — {Politicom.Sim.Global.StateText.Name(selected.Ideology)}, weight {selected.Weight:0.0} (power {selected.Power:0}, {(selected.Proximity >= 1 ? "a neighbour" : $"{selected.Proximity:P0} interest at this distance")})\n" +
              $"{Politicom.Sim.Global.StateText.Stance(selected.Stance)} toward you ([b]{selected.Stance:+0.00;-0.00}[/b]) — {Politicom.Sim.Global.StateText.Actions(selected.Actions)}\n" +
              $"Workers radicalized: {selected.Radicalization:P0}" + (selected.Repression > 0.05 ? $" · repression {selected.Repression:P0}" : "") +
              $" · technology {selected.TechLevel:P0} · its culture resists yours {selected.CulturalWeight:P0}\n" +
              (selected.Uprising > 0 ? $"[color=#f2c94c][b]Revolution under way[/b]: decided in {w.Turns(selected.Uprising)}, success chance {Politicom.Sim.Global.RevolutionSystem.SuccessChance(w, selected):P0}, of which a party seizes power {Politicom.Sim.Global.RevolutionSystem.AuthoritarianChance(w, selected):P0}.[/color]\n" : "") +
              (selected.Sister ? (selected.Federated ? "[color=#e07a6a]A sister federation, now part of yours.[/color]\n" : "[color=#e07a6a]A sister federation.[/color]\n") : "") +
              "[color=#808590]Thresholds: embargo below −0.30, arms above +0.35, volunteers above +0.50, intervention below −0.60. Order in the federation reassures governments and disappoints sympathizers.[/color]";

        foreach (var child in _stateList.GetChildren()) child.QueueFree();
        foreach (var s in w.States.OrderByDescending(s => s.Uprising > 0).ThenByDescending(s => s.Sister).ThenByDescending(s => s.Weight).Take(12))
        {
            var id = s.Id;
            var row = new Button
            {
                Text = $"{s.Name} — " + (s.Uprising > 0 ? $"REVOLUTION ({w.Turns(s.Uprising)})" : s.Sister ? "sister federation" : $"{Politicom.Sim.Global.StateText.Stance(s.Stance)} ({s.Stance:+0.00;-0.00}) · {Politicom.Sim.Global.StateText.Actions(s.Actions)}"),
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
    private CheckButton _reliefToggle = null!;
    private TechCatalog _techs = null!;
    private RichTextLabel _resources = null!;
    private RichTextLabel _driftDetail = null!, _enemy = null!, _commune = null!, _log = null!;
    private VBoxContainer _decisions = null!, _projects = null!;
    private CheckButton _warCommittee = null!, _trade = null!, _conscription = null!;
    private Button _seize = null!;
    private readonly List<Button> _buyButtons = new();
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
            const string randomEvents = "res://data/events/random_events.json";
            _sandbox = SandboxGenerator.Parse(ReadData("res://data/sandbox/event_templates.json"), _techs,
                FileAccess.FileExists(randomEvents) ? ReadData(randomEvents) : null);
            foreach (var s in _scenarios.Where(s => s.RandomEvents.Count == 0)) s.RandomEvents = _sandbox.RandomEvents.ToList();
            _world = World.Create(_scenario, _techs, seed: (ulong)DateTime.Now.Ticks);
        }
        catch (Exception e)
        {
            GD.PushError($"Could not load scenario: {e.Message}");
            AddChild(new Label { Text = $"Could not load scenario:\n{e.Message}" });
            return;
        }
        Theme = UiTheme.Build();
        AddChild(new Audio());
        Motion.Install(GetTree());
        Audio.Instance!.TrackStarted += (title, _) =>
        {
            if (_menu == null && _toasts != null) _toasts.Show($"Now playing: {title}", new Color(0.75f, 0.68f, 0.52f), 5);
        };
        BuildUi();
        SetWorld(_world);
        var args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--screenshot") < 0 || Array.IndexOf(args, "--menu") >= 0) ShowStartMenu();
        else if (Array.IndexOf(args, "--intro") >= 0) ShowIntro();
        HandleScreenshotArgs();
    }

    /// <summary>
    /// Dev hook: <c>godot --path Game -- --screenshot out.png [--scenario ID] [--weeks N] [--select ID] [--focus ID] [--zoom Z] [--intro] [--communes] [--tab N] [--techs [ID]] [--policy NAME] [--clock SECONDS] [--world] [--mode NAME] [--seed N]</c>
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
        // A short name ("catalonia") matches the scenario whose id starts with it.
        string? asked = scenarioAt >= 0 && scenarioAt + 1 < args.Length ? args[scenarioAt + 1] : null;
        if (asked != null && FindScenario(asked) is null && _scenarios.FirstOrDefault(s => s.Id.StartsWith(asked + "_")) is { } byPrefix)
            asked = byPrefix.Id;
        if (asked != null && FindScenario(asked) is null)
        {
            GD.PushError($"--scenario: no scenario '{asked}'. Known: {string.Join(", ", _scenarios.Select(s => s.Id))}, sandbox:SEED, open:ERA:STATE.");
            GetTree().Quit(1);
            return;
        }
        if (asked != null && FindScenario(asked) is { } chosen)
        {
            _scenario = chosen;
            int seedAt = Array.IndexOf(args, "--seed");
            ulong worldSeed = seedAt >= 0 && seedAt + 1 < args.Length ? ulong.Parse(args[seedAt + 1]) : 1;
            SetWorld(World.Create(chosen, _techs, seed: worldSeed));
        }
        PopupsEnabled = Array.IndexOf(args, "--popups") >= 0;
        int weeksAt = Array.IndexOf(args, "--weeks");
        int weeks = weeksAt >= 0 && weeksAt + 1 < args.Length ? int.Parse(args[weeksAt + 1]) : 0;
        int selectAt = Array.IndexOf(args, "--select");
        if (selectAt >= 0 && selectAt + 1 < args.Length) OnCommuneClicked(int.Parse(args[selectAt + 1]), false);
        int policyAt = Array.IndexOf(args, "--policy");
        var policy = policyAt >= 0 && policyAt + 1 < args.Length ? Sim.Ai.Policies.Get(args[policyAt + 1]) : null;
        if (Array.IndexOf(args, "--fortify") >= 0)
            foreach (var c in _world.Federation.Where(c => c.Front).ToList()) _world.Apply(new Fortify(c.Id));
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
        if (Array.IndexOf(args, "--tutorial") >= 0) StartTutorial();
        if (Array.IndexOf(args, "--ledger") >= 0) ShowLedger();
        if (Array.IndexOf(args, "--save") >= 0) QuickSave();
        if (Array.IndexOf(args, "--leaderboard") >= 0) ShowLeaderboard();
        if (Array.IndexOf(args, "--load") >= 0) ShowLoadDialog();
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
        if (Array.IndexOf(args, "--norelief") >= 0) _reliefToggle.ButtonPressed = false;
        int pickerAt = Array.IndexOf(args, "--picker");
        if (pickerAt >= 0 && pickerAt + 1 < args.Length)
            ShowCountryMap(OpenEras.Where(HasAtlas).ToArray(), Math.Max(0, Array.IndexOf(OpenEras.Where(HasAtlas).ToArray(), args[pickerAt + 1])), 1, 104);
        // --scroll X,Y (fractions of the window): a trackpad swipe there, for testing what it scrolls.
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] != "--scroll") continue;
            var xy = args[i + 1].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var scrollAt = GetViewport().GetFinalTransform() * (GetViewport().GetVisibleRect().Size * new Vector2(xy[0], xy[1]));
            for (int f = 0; f < 3; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().PushInput(new InputEventMouseMotion { Position = scrollAt, GlobalPosition = scrollAt });
            for (int k = 0; k < 6; k++) GetViewport().PushInput(new InputEventPanGesture { Position = scrollAt, Delta = new Vector2(0, 3) });
        }
        int focusAt = Array.IndexOf(args, "--focus");
        int zoomAt = Array.IndexOf(args, "--zoom");
        if (focusAt >= 0 && focusAt + 1 < args.Length)
            _map.FocusOn(int.Parse(args[focusAt + 1]), zoomAt >= 0 && zoomAt + 1 < args.Length ? float.Parse(args[zoomAt + 1], System.Globalization.CultureInfo.InvariantCulture) : 2.5f);
        // --hover X,Y (fractions of the window): the pointer rests there long enough for a tooltip.
        int hoverAt = Array.IndexOf(args, "--hover");
        if (hoverAt >= 0 && hoverAt + 1 < args.Length)
        {
            var xy = args[hoverAt + 1].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var hover = GetViewport().GetFinalTransform() * (GetViewport().GetVisibleRect().Size * new Vector2(xy[0], xy[1]));
            for (int f = 0; f < 6; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.WarpMouse(hover);
            GetViewport().PushInput(new InputEventMouseMotion { Position = hover, GlobalPosition = hover });
            await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
        }
        // --click X,Y (fractions of the window): a real mouse press and release, for testing input paths.
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] != "--click") continue;
            var xy = args[i + 1].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var clickAt = GetViewport().GetFinalTransform() * (GetViewport().GetVisibleRect().Size * new Vector2(xy[0], xy[1])); // window coordinates
            for (int f = 0; f < 12; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().PushInput(new InputEventMouseMotion { Position = clickAt, GlobalPosition = clickAt });
            GetViewport().PushInput(new InputEventMouseButton { Position = clickAt, GlobalPosition = clickAt, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left });
            GetViewport().PushInput(new InputEventMouseButton { Position = clickAt, GlobalPosition = clickAt, ButtonIndex = MouseButton.Left, Pressed = false });
        }
        int tabAt = Array.IndexOf(args, "--tab");
        if (tabAt >= 0 && tabAt + 1 < args.Length) _tabs.CurrentTab = int.Parse(args[tabAt + 1]);
        Refresh();
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
        UpdateTutorial();
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
        _header.Text = $"{w.Scenario.Name} · {w.Format(date, "d MMM yyyy")} · {w.Turn} {w.Tick}/{w.Scenario.EndTick}"
                       + (SandboxGenerator.TryParseId(ScenarioDefinition.SplitLength(w.Scenario.Id).BaseId, out var mapSeed, out _) ? $"  · seed {mapSeed}" : "");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (_overlay != null)
        {
            if (key.Keycode == Key.Escape && (_overlay != _menu || _inGame))
            {
                if (_overlay == _menu) ResumeGame();
                else CloseOverlay();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        switch (key.Keycode)
        {
            case Key.C: ToggleCommuneList(); break;
            case Key.K: ShowTechTree(); break;
            case Key.L: ShowLedger(); break;
            case Key.F5: QuickSave(); break;
            case Key.F9: QuickLoad(); break;
            case Key.W: ToggleWorldView(); break;
            case Key.R: _reliefToggle.ButtonPressed = !_reliefToggle.ButtonPressed; break;
            case Key.T:
                _map.ShowTrustLinks = !_map.ShowTrustLinks;
                _map.QueueRedraw();
                break;
            case Key.Escape when _communeList.Visible: ToggleCommuneList(); break;
            case Key.Escape: ShowStartMenu(); break;
            case Key.Space: SetSpeed(_speed == 0 ? 1 : 0); break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(2); break;
            case Key.Key3: SetSpeed(3); break;
            case Key.Key4: SetSpeed(4); break;
            case Key.Period: StepTurn(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void StepTurn()
    {
        _dayInTurn = 0;
        if (_world.Outcome != Outcome.Ongoing) return;
        int occupied = _world.Communes.Count(c => c.Occupied);
        _world.Step();
        if (_world.Communes.Count(c => c.Occupied) > occupied) Audio.Instance?.Play("sword", -6);
        Audio.Instance?.PlayAge(Audio.AgeOf(_world.Year));
        // Like a Paradox popup: pause when the movement has a decision to make.
        var fresh = _world.Log.Skip(_seenEvents).ToList();
        if (fresh.Any(e => e.StopsTheClock)) SetSpeed(0);
        foreach (var e in fresh.Where(e => e.Severity == Severity.Danger || e.StopsTheClock))
        {
            if (e.StopsTheClock && PopupsEnabled) QueuePopup(e);
            else _toasts.Show(Shorten(e.Text), e.Severity == Severity.Decision ? new Color(0.38f, 0.85f, 0.95f) : new Color(0.95f, 0.4f, 0.35f));
        }
        Refresh();
        ShowTips();
        OnOutcome();
    }

    private TechTreePanel ShowTechTree()
    {
        Did("techs");
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

    private static bool HasAtlas(string era) => FileAccess.FileExists($"res://data/world/{era}.json");

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
        if (_map.Globe is null)
        {
            Say("This game has no world map — it was saved before the world map was added. Start a new game to play on the world map.");
            return;
        }
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
        if (_inGame) Audio.Instance?.PlayAge(Audio.AgeOf(world.Year));
        _world = world;
        _selectedState = null;
        _dayInTurn = 0;
        // Saves from before the world map carry no era: use their scenario's.
        string? era = world.Scenario.World
                      ?? _scenarios.FirstOrDefault(s => s.Id == ScenarioDefinition.SplitLength(world.Scenario.Id).BaseId)?.World;
        _map.Globe = LoadAtlas(era)
                     ?? (world.Scenario.Geography is { Neighbours.Count: > 0 } g ? Politicom.Sim.Global.Atlas.FromGenerated(g) : null);
        _map.SelectedState = null;
        _worldButton.Disabled = _map.Globe is null;
        _worldButton.Text = "World";
        _worldButton.TooltipText = _map.Globe is null
            ? "This game has no world map (it was saved before the world map was added). Start a new game to play on the world map."
            : "Zoom out to the whole world, or back to the region (W).";
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

    /// <summary>Back from the start menu to the game under way, and to its music.</summary>
    private void ResumeGame()
    {
        CloseOverlay();
        Audio.Instance?.PlayAge(Audio.AgeOf(_world.Year));
    }

    /// <summary>Music and sound volumes (kept between sessions).</summary>
    private void ShowSettings()
    {
        var audio = Audio.Instance!;
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 14);
        HSlider Slider(string label, float value)
        {
            grid.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(120, 0) });
            var slider = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, Value = value * 100, CustomMinimumSize = new Vector2(320, 24), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            grid.AddChild(slider);
            var pct = new Label { Text = $"{value * 100:0}%", CustomMinimumSize = new Vector2(48, 0) };
            slider.ValueChanged += v => pct.Text = $"{v:0}%";
            grid.AddChild(pct);
            return slider;
        }
        var music = Slider("Music", audio.MusicVolume);
        var sound = Slider("Sounds", audio.SoundVolume);
        var mute = new CheckBox { Text = "Mute everything", ButtonPressed = audio.Muted };
        var tilt = new CheckBox { Text = "Tilt the map as you zoom in (3D)", ButtonPressed = _map3d.Tilt };
        tilt.Toggled += on =>
        {
            _map3d.Tilt = on;
            SaveTiltSetting(on);
        };
        void Apply() => audio.SetVolumes((float)music.Value / 100, (float)sound.Value / 100, mute.ButtonPressed);
        music.ValueChanged += _ => Apply();
        sound.ValueChanged += _ => { Apply(); audio.Play("click"); };
        mute.Toggled += _ => Apply();
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        box.AddChild(grid);
        box.AddChild(mute);
        box.AddChild(tilt);
        box.AddChild(new Label
        {
            Text = "Music from Wikimedia Commons (public domain and Creative Commons recordings, credited as each piece starts); interface sounds by Kenney (CC0).",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(540, 0), Modulate = new Color(1, 1, 1, 0.5f),
        });
        ShowOverlay("Settings", "", box, 620, ("Back", ShowStartMenu));
    }

    private static readonly string[] MenuPaintings =
        { "uprising_paris", "uprising_catalonia", "uprising_ukraine", "uprising_industrial", "uprising_medieval", "assembly_paris", "uprising_earlymodern" };

    /// <summary>
    /// The start menu: a period painting, the title, and the ways in: continue the quick-save, a
    /// historical revolution, the open world, or back to the game under way.
    /// </summary>
    private void ShowStartMenu()
    {
        CloseOverlay(quiet: true);
        SetSpeed(0);
        var dim = Motion.Backdrop(darken: 0.72f);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.AddChild(center);
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 16);
        column.Modulate = new Color(1, 1, 1, 0);
        column.Ready += () => column.CreateTween().TweenProperty(column, "modulate:a", 1f, 0.35f);
        center.AddChild(column);

        var paintings = MenuPaintings.Where(n => ResourceLoader.Exists($"res://art/events/{n}.jpg")).ToArray();
        if (paintings.Length > 0)
        {
            var name = paintings[Random.Shared.Next(paintings.Length)];
            var frame = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
            frame.AddThemeStyleboxOverride("panel", UiTheme.FramePanel(5));
            frame.AddChild(new TextureRect
            {
                Texture = GD.Load<Texture2D>($"res://art/events/{name}.jpg"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(640, 267),
            });
            column.AddChild(frame);
        }

        var title = new Label { Text = "POLITICOM", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontOverride("font", new FontVariation { BaseFont = UiTheme.DisplayBold, SpacingGlyph = 10 });
        title.AddThemeFontSizeOverride("font_size", 64);
        title.AddThemeColorOverride("font_color", new Color("e3bd6b"));
        title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        title.AddThemeConstantOverride("shadow_offset_y", 3);
        column.AddChild(title);
        var subtitle = new Label { Text = "Free communes against the state, from antiquity to the modern age", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.75f) };
        subtitle.AddThemeFontOverride("font", UiTheme.BodyItalic);
        subtitle.AddThemeFontSizeOverride("font_size", 19);
        column.AddChild(subtitle);

        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        panel.AddThemeStyleboxOverride("panel", UiTheme.FrameStrong(26));
        column.AddChild(panel);
        var buttons = new VBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        panel.AddChild(buttons);
        void Item(string label, string tooltip, Action onPress)
        {
            var b = AddButton(buttons, label, tooltip, onPress);
            b.CustomMinimumSize = new Vector2(340, 46);
            b.AddThemeFontOverride("font", UiTheme.Display);
            b.AddThemeFontSizeOverride("font_size", 20);
        }
        if (_inGame) Item("Resume", "Back to the game under way.", ResumeGame);
        Item("Tutorial", "A guided first game in Revolutionary Catalonia, 1936: the movement, drift, schools, the front, trust, technologies.", StartTutorial);
        var saves = Saves();
        if (saves.Count > 0) Item("Continue", $"Load the latest save: {saves[0].Meta.Name}.", () => LoadFrom(saves[0].Path));
        Item("Historical revolutions", "Paris 1871, the Free Territory 1918 or Catalonia 1936.", ShowIntro);
        if (_sandbox != null && OpenEras.Any(HasAtlas))
            Item("Open world", "Any country, in any era from antiquity to today: choose it on the map. The movement rises in one region; a whole-country rising is an option.", () =>
            {
                var eras = OpenEras.Where(HasAtlas).ToArray();
                ShowCountryMap(eras, Math.Max(0, Array.IndexOf(eras, "1936")), NewSeed(), 104);
            });
        if (saves.Count > 0) Item("Load game", "Any saved game.", () => ShowLoadDialog(ShowStartMenu));
        Item("Leaderboard", "Your finished games, ranked by score.", () => ShowLeaderboard(ShowStartMenu));
        Item("Settings", "Music, sound and the map.", ShowSettings);
        Item("Quit", "", () => GetTree().Quit());

        var credit = new Label
        {
            Text = "Pictures from Wikimedia Commons · histories from Wikipedia (CC BY-SA) · maps from Natural Earth and historical-basemaps",
            HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.4f),
        };
        credit.AddThemeFontSizeOverride("font_size", 12);
        column.AddChild(credit);

        AddChild(dim);
        _overlay = _menu = dim;
        Audio.Instance?.PlayAge("menu"); // after the menu is up, so its songs aren't announced
    }

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
                Text = $"{UiTheme.DisplayText(Esc(scenario.Name))}  [color=#9aa0a8]{(end.Year == scenario.StartDate.Year ? $"{end.Year}" : $"{scenario.StartDate.Year}–{end.Year}")} · {scenario.Communes.Count} communes · "
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
        if (_sandbox != null && OpenEras.Any(HasAtlas)) list.AddChild(OpenWorldRow());

        // Four scenarios don't fit on a small screen with the rules above them: let the list scroll.
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(PickerWidth - 56, 470), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(list);

        ShowOverlay("Choose a revolution", """
            [b]You are the movement, not the government.[/b] You can't order a commune to do anything. You fund schools, mediate disputes, circulate ideas and propose projects; the communes decide for themselves.

            [b]The dilemma.[/b] Almost everything that helps you win the war (a war committee, government arms, conscription, appointed commanders) pushes [color=#f2c94c]hierarchy drift[/color] up. At 25, 50 and 75 drift, permanent councils, then commissars, then a police force take over, each with real bonuses. At 100 a state has crystallized and the revolution is over, however strong the society is. Political education, recall and rotation are the brakes.

            [b]Winning.[/b] Hold out as a free federation until the scenario ends; you're scored on survival, quality of life, spread and freedom. [b]Controls:[/b] click a commune, shift-click a second; Space pauses, 1–3 set speed; scroll or pinch to zoom, drag to move the map; Esc opens the menu.
            """, scroll, PickerWidth, ("Main menu", ShowStartMenu));
    }

    private const float PickerWidth = 1040;

    /// <summary>A scenario by id, including generated maps and longer games ("…@300").</summary>
    private ScenarioDefinition? FindScenario(string id)
    {
        var (baseId, length) = ScenarioDefinition.SplitLength(id);
        if (SandboxGenerator.TryParseId(baseId, out var seed, out var size) && _sandbox != null)
            return _sandbox.Generate(seed, size, length: length);
        if (OpenStartGenerator.TryParseId(baseId, out var era, out var stateId, out var openSeed, out var scope) && _sandbox != null && LoadAtlas(era) is { } atlas)
        {
            try { return OpenStartGenerator.Generate(atlas, _sandbox, stateId, openSeed, length: length, scope: scope); }
            catch (ArgumentException e) { GD.PushWarning(e.Message); return null; }
        }
        var found = _scenarios.FirstOrDefault(s => s.Id == baseId);
        return found is null || length is not int turns ? found : found.WithLength(turns);
    }

    /// <summary>Historical length, or a longer war up to 500 turns.</summary>
    private static OptionButton LengthPicker(int historical, string turn, string tooltip, bool campaigns = false)
    {
        var picker = new OptionButton { TooltipText = tooltip };
        picker.AddItem($"Historical · {historical} {turn}s", historical);
        foreach (int n in new[] { 250, 500 })
            if (n > historical) picker.AddItem($"{n} {turn}s", n);
        // Campaigns: decades. Peace doesn't end them; treaties are made and broken.
        if (campaigns)
            foreach (var (years, n) in new[] { (10, 520), (25, 1300), (50, 2600) })
                picker.AddItem($"Campaign · {years} years", n);
        return picker;
    }

    private const string LongerTooltip =
        "After the historical events, the war goes on: the enemy launches renewed offensives, and drift keeps building. " +
        "Every line of technology has a free path that brakes drift; in a long game you will need them.";

    private static string[] OpenEras => OpenStartGenerator.Eras;

    /// <summary>The Open world row: an era, any country on its map, a seed and a length.</summary>
    private Control OpenWorldRow()
    {
        var box = new VBoxContainer();
        var text = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(820, 0),
            Text = UiTheme.DisplayText("Open world") + "  [color=#9aa0a8]any country · its own cities as communes · its government as the enemy[/color]\n"
                   + "The movement rises wherever you choose. Hostile neighbours may back the government; the rest of the world goes on around you.",
        };
        text.AddThemeFontSizeOverride("normal_font_size", 14);
        text.AddThemeFontSizeOverride("bold_font_size", 16);
        box.AddChild(text);

        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 8);
        var eras = OpenEras.Where(HasAtlas).ToArray(); // each map loads when its era is picked
        var era = new OptionButton();
        foreach (var e in eras) era.AddItem(OpenStartGenerator.EraName(e));
        controls.AddChild(era);
        var country = new OptionButton { CustomMinimumSize = new Vector2(260, 0), FitToLongestItem = false };
        var ids = new List<string>();
        void FillCountries()
        {
            country.Clear();
            ids.Clear();
            foreach (var s in OpenStartGenerator.Playable(LoadAtlas(eras[era.Selected])!))
            {
                country.AddItem(s.Name);
                ids.Add(s.Id);
            }
        }
        FillCountries();
        era.ItemSelected += _ => FillCountries();
        controls.AddChild(country);
        controls.AddChild(new Label { Text = "Seed" });
        var seed = new LineEdit { Text = NewSeed().ToString(), CustomMinimumSize = new Vector2(110, 0), MaxLength = 12 };
        controls.AddChild(seed);
        var openLength = LengthPicker(104, "week", LongerTooltip + " Campaigns run for decades: peace doesn't end them, treaties are broken, and the world moves on (press 4 for the fastest speed). Generation takes a few seconds.", campaigns: true);
        controls.AddChild(openLength);
        var scope = ScopePicker();
        controls.AddChild(scope);
        AddButton(controls, "On the map…", "Choose the country on a world map of the era.", () =>
            ShowCountryMap(eras, era.Selected, ulong.TryParse(seed.Text, out var mapSeed) ? mapSeed : NewSeed(), openLength.GetSelectedId(), (OpenScope)scope.Selected));
        controls.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var play = AddButton(controls, "Play", "", () =>
        {
            if (country.Selected < 0) return;
            ulong value = ulong.TryParse(seed.Text, out var v) ? v : NewSeed();
            int turns = openLength.GetSelectedId();
            StartScenario(OpenStartGenerator.Generate(LoadAtlas(eras[era.Selected])!, _sandbox!, ids[country.Selected], value, length: turns == 104 ? null : turns,
                scope: (OpenScope)scope.Selected));
        });
        play.CustomMinimumSize = new Vector2(110, 40);
        box.AddChild(controls);
        return box;
    }

    /// <summary>Whole country or a regional rising; item index = OpenScope.</summary>
    private static OptionButton ScopePicker(OpenScope selected = OpenScope.Region)
    {
        var picker = new OptionButton
        {
            TooltipText = "Whole country: the movement holds its largest cities, and the government only the capital.\n" +
                          "Regional rising: it holds a stronghold and the cities around it; the rest of the country is the government's, " +
                          "but its towns may rise and join as the movement's example spreads.",
        };
        picker.AddItem("Whole country", (int)OpenScope.Country);
        picker.AddItem("Regional rising", (int)OpenScope.Region);
        picker.Select((int)selected);
        return picker;
    }

    /// <summary>The open start's world map: hover a country to see it, click to choose it, then Play.</summary>
    private void ShowCountryMap(string[] eras, int eraIndex, ulong seed, int turns, OpenScope startScope = OpenScope.Region)
    {
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        content.AddChild(top);
        var era = new OptionButton();
        foreach (var e in eras) era.AddItem(OpenStartGenerator.EraName(e));
        era.Select(eraIndex);
        top.AddChild(era);
        var scope = ScopePicker(startScope);
        top.AddChild(scope);
        var lengthPick = LengthPicker(104, "week", LongerTooltip + " Campaigns run for decades: peace doesn't end them, treaties are broken (press 4 for the fastest speed).", campaigns: true);
        lengthPick.Select(Math.Max(0, lengthPick.GetItemIndex(turns)));
        lengthPick.ItemSelected += _ => turns = lengthPick.GetSelectedId();
        top.AddChild(lengthPick);
        var hint = new Label { Text = "Click a country in colour to start there. Grey ones have too few cities. Scroll or pinch to zoom.", Modulate = new Color(1, 1, 1, 0.6f) };
        top.AddChild(hint);

        // The map, with the chosen (or hovered) country's lore beside it.
        var middle = new HBoxContainer();
        middle.AddThemeConstantOverride("separation", 14);
        content.AddChild(middle);
        var picker = new StatePicker { CustomMinimumSize = new Vector2(840, 560) };
        middle.AddChild(picker);
        var loreScroll = new ScrollContainer { CustomMinimumSize = new Vector2(330, 560), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        middle.AddChild(loreScroll);
        void ShowLore(string? id)
        {
            foreach (var child in loreScroll.GetChildren()) child.QueueFree();
            if (id != null && Current().State(id) is { } st) loreScroll.AddChild(Lore.Panel(Current(), st, 314));
            else loreScroll.AddChild(new Label { Text = "Point at a country to read about it.", Modulate = new Color(1, 1, 1, 0.5f) });
        }

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 16);
        content.AddChild(bottom);
        var info = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        info.AddThemeFontSizeOverride("normal_font_size", 14);
        info.AddThemeFontSizeOverride("bold_font_size", 16);
        bottom.AddChild(info);
        var play = AddButton(bottom, "Play", "", () => { });
        play.CustomMinimumSize = new Vector2(150, 40);
        play.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        play.Disabled = true;

        Politicom.Sim.Global.Atlas Current() => LoadAtlas(eras[era.Selected])!;
        string Describe(string? id)
        {
            if (id is null || Current().State(id) is not { } st)
                return "[color=#9aa0a8]Point at a country.[/color]";
            var playable = OpenStartGenerator.Playable(Current()).Any(x => x.Id == id);
            var ideology = Politicom.Sim.Global.StateText.Name(Politicom.Sim.Global.WorldSetup.Parse(st.Ideology));
            return $"[b]{Esc(st.Name)}[/b]  [color=#9aa0a8]{ideology} · power {st.Power:0}" +
                   (st.Capital is { } c ? $" · capital {Esc(c.Name)}" : "") + "[/color]\n" +
                   (!playable ? "[color=#9aa0a8]Too few cities on this map to play here.[/color]"
                    : scope.Selected == (int)OpenScope.Region
                       ? "The movement rises in one region, around a stronghold far from the capital. The country's other towns may rise and join it."
                       : $"The movement holds its largest cities, from {Esc(string.Join(", ", st.Cities.Where(x => st.Capital is null || x.Name != st.Capital.Name).Take(4).Select(x => x.Name)))}… The government holds the capital.");
        }
        void Refresh()
        {
            var atlas = Current();
            picker.SetAtlas(atlas, OpenStartGenerator.Playable(atlas).Select(x => x.Id));
            play.Disabled = picker.Selected is null;
            play.Text = picker.Selected is { } id && atlas.State(id) is { } st ? $"Play {Politicom.Sim.Global.StateText.Country(st.Name)}" : "Play";
            info.Text = Describe(picker.Hovered ?? picker.Selected);
            ShowLore(picker.Hovered ?? picker.Selected);
        }
        picker.StateHovered += id =>
        {
            info.Text = Describe(id ?? picker.Selected);
            ShowLore(id ?? picker.Selected);
        };
        scope.ItemSelected += _ => info.Text = Describe(picker.Hovered ?? picker.Selected);
        picker.StateChosen += _ => Refresh();
        era.ItemSelected += _ => Refresh();
        play.Pressed += () =>
        {
            if (picker.Selected is not { } id) return;
            var atlas = Current();
            var scenario = OpenStartGenerator.Generate(atlas, _sandbox!, id, seed, length: turns == 104 ? null : turns, scope: (OpenScope)scope.Selected);
            StartScenario(scenario);
            if (atlas.State(id) is { } st) ShowOpening(atlas, st, scenario);
        };
        Refresh();

        ShowOverlay("Where does the movement rise?", "", content, 1240, ("Back", ShowIntro));
    }

    /// <summary>The opening of an open-world game: the country's history beside what the movement faces.</summary>
    private void ShowOpening(Politicom.Sim.Global.Atlas atlas, Politicom.Sim.Global.AtlasState st, ScenarioDefinition scenario)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 22);
        row.AddChild(Lore.Panel(atlas, st, 420));
        var text = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            CustomMinimumSize = new Vector2(380, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Text = $"{UiTheme.DisplayText(Esc(scenario.Name), 18)}\n\n{Esc(scenario.Description)}",
        };
        row.AddChild(text);
        ShowOverlay("The movement rises", "", row, 900, ("Begin", CloseOverlay));
    }

    /// <summary>A foreign city clicked on the map: send emissaries or agitators there, or back a rising.</summary>
    private void ShowMission(string stateId, string city)
    {
        var w = _world;
        if (w.States.FirstOrDefault(s => s.Id == stateId) is not { } state) return;
        var b = w.Balance;
        SelectState(stateId);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 22);
        if (_map.Globe is { } atlas && atlas.State(stateId) is { } st) row.AddChild(Lore.Panel(atlas, st, 300));
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(380, 0) };
        right.AddThemeConstantOverride("separation", 10);
        row.AddChild(right);
        var last = w.Missions.LastOrDefault(m => m.StateId == stateId && m.City == city);
        var text = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            Text = $"{UiTheme.DisplayText(Esc(city), 20)}\n[color=#c9a25b]{Esc(state.Name)} · {Politicom.Sim.Global.StateText.Name(state.Ideology)}[/color]\n\n" +
                   $"{Politicom.Sim.Global.StateText.Stance(state.Stance)} toward you ([b]{state.Stance:+0.00;-0.00}[/b]) — {Politicom.Sim.Global.StateText.Actions(state.Actions)}\n" +
                   $"Workers radicalized: [b]{state.Radicalization:P0}[/b]" + (state.Repression > 0.05 ? $" · repression {state.Repression:P0}" : "") + "\n" +
                   (state.Uprising > 0 ? $"[color=#f2c94c][b]Revolution under way[/b], decided in {w.Turns(state.Uprising)}.[/color]\n" : "") +
                   (last != null ? $"[color=#9aa0a8]Your {(last.Agitation ? "agitators" : "emissaries")} were here {w.Turns(w.Tick - last.Tick)} ago.[/color]\n" : "") +
                   $"\nInfluence in hand: [b]{w.Influence:0}[/b]",
        };
        right.AddChild(text);
        void Act(PlayerAction action)
        {
            var result = w.Apply(action);
            Audio.Instance?.Play(result.Ok ? "coins" : "error", result.Ok ? -8 : -4);
            CloseOverlay();
            _toasts.Show(result.Message, result.Ok ? new Color(0.5f, 0.85f, 0.55f) : new Color(0.95f, 0.45f, 0.4f), 8);
            Refresh();
        }
        Button Mission(string label, string tooltip, double cost, PlayerAction action)
        {
            var button = AddButton(right, $"{label} ({cost:0})", tooltip, () => Act(action));
            button.Disabled = w.Influence < cost;
            button.Alignment = HorizontalAlignment.Left;
            return button;
        }
        Mission("Send emissaries", $"Meetings, newspapers and unions in {city}: {state.Name} warms toward you. Fascist governments barely listen.", b.CostDelegates, new SendDelegates(stateId, city));
        Mission("Agitate the workers", $"Radicalize the workers of {city}. {state.Name}'s government notices and cools toward you.", b.CostAgitate, new AgitateWorkers(stateId, city));
        if (state.Uprising > 0)
            Mission("Support the rising", "Arms and organizers for the revolution under way: a better chance it wins, and a little drift at home.", b.CostSupportUprising, new SupportUprising(stateId));
        ShowOverlay($"A mission to {city}", "", row, 780, ("Close", CloseOverlay));
    }

    private static ulong NewSeed() => (ulong)Random.Shared.Next(1, 1_000_000);

    private void StartScenario(ScenarioDefinition scenario)
    {
        if (_tutorial != null) EndTutorial();
        _inGame = true;
        _scenario = scenario;
        NewGame();
        Say($"{scenario.Name} begins.");
        _toasts.Show("Tip: press Space or ▶ to start time. Click any commune to see it; press C for a list of all of them.", new Color(0.95f, 0.8f, 0.35f), 12);
    }

    private void OnOutcomeAgain()
    {
        CloseOverlay();
        OnOutcome();
    }

    private void OnOutcome()
    {
        if (_world.Outcome == Outcome.Ongoing || _overlay != null) return;
        SetSpeed(0);
        var w = _world;
        var s = w.Scores;
        (string title, string verdict) = Endings.Of(w);
        string epilogue = string.Join("\n", Endings.Epilogue(w).Select(line => $"· {Esc(line)}"));
        _map.Banner = title;
        Audio.Instance?.Play(w.Outcome == Outcome.Survived ? "good" : "drum");
        int? rank = RecordOutcome(title);
        string ranked = rank is int r ? $"\n\n[color=#e3bd6b]{(r == 1 ? "A new best" : $"Number {r}")} on the leaderboard for {Esc(w.Scenario.Name)}.[/color]" : "";
        var chart = new HistoryChart { World = w, FontSize = 13, CustomMinimumSize = new Vector2(0, 220) };
        ShowOverlay(title, $"""
            {verdict}

            [color=#c9a25b]{epilogue}[/color]

            [b]Survival[/b] {s.Survival:0}    [b]Quality of life[/b] {s.QualityOfLife:0}    [b]Spread[/b] {s.Spread:0}    [b]Freedom[/b] {s.Freedom:0}    —    [b]Total {s.Total:0}[/b]

            {w.Federation.Count()} of {w.InitialCommuneCount} communes free and federated · {w.Communes.Count(c => c.Occupied)} occupied · {w.Communes.Count(c => !c.Occupied && !c.Federated)} seceded · final drift {w.Drift.Value:0} ({DriftEffects.Name(w.Drift.Stage)}){(SandboxGenerator.TryParseId(ScenarioDefinition.SplitLength(w.Scenario.Id).BaseId, out var mapSeed, out var mapSize) ? $"\n\nSandbox map: seed {mapSeed}, {mapSize} communes." : "")}{ranked}
            """, chart, ("Keep looking", CloseOverlay), ("Leaderboard", () => ShowLeaderboard(OnOutcomeAgain, ScenarioDefinition.SplitLength(w.Scenario.Id).BaseId)), ("Play again", NewGame), ("Main menu", ShowStartMenu));
    }

    private void ShowOverlay(string title, string body, Control? extra, params (string Label, Action OnPress)[] buttons) =>
        ShowOverlay(title, body, extra, 760, buttons);

    private void ShowOverlay(string title, string body, Control? extra, float width, params (string Label, Action OnPress)[] buttons)
    {
        CloseOverlay(quiet: true);
        Audio.Instance?.Play("open", -6);
        SetSpeed(0);
        var dim = Motion.Backdrop();
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        panel.AddThemeStyleboxOverride("panel", UiTheme.FrameStrong(30));
        center.AddChild(Motion.PopIn(panel));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 14);
        panel.AddChild(v);

        v.AddChild(UiTheme.TitlePlate(title, 26));
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

    private void CloseOverlay() => CloseOverlay(quiet: false);

    private void CloseOverlay(bool quiet)
    {
        if (_overlay != null && !quiet) Audio.Instance?.Play("close", -8);
        Motion.FadeOutAndFree(_overlay);
        _overlay = _menu = null;
    }

    private void Do(PlayerAction action)
    {
        var result = _world.Apply(action);
        Audio.Instance?.Play(result.Ok ? "coins" : "error", result.Ok ? -8 : -4);
        if (result.Ok) Did(action is FundSchool school ? $"FundSchool:{school.Track}" : action.GetType().Name);
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
        _map.CityClicked += ShowMission;
        _map3d = new Map3D(_map) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3, Tilt = LoadTiltSetting() };
        root.AddChild(_map3d);

        // Top-right of the map, clear of the commune list (bottom-left). They fade after a few seconds.
        _toasts = new ToastStack
        {
            AnchorLeft = 1, AnchorRight = 1,
            OffsetLeft = -460, OffsetRight = -16, OffsetTop = 16, OffsetBottom = 300,
        };
        _map3d.AddChild(_toasts);
        _communeList = new CommuneList
        {
            Visible = false,
            AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = 16, OffsetRight = 1016, OffsetTop = -470, OffsetBottom = -56,
        };
        _communeList.CommuneChosen += id =>
        {
            _map.Selected = id;
            _map.Secondary = null;
            _map.CenterOn(id);
            Refresh();
        };
        _map3d.AddChild(_communeList);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(360, 0) };
        panel.AddThemeStyleboxOverride("panel", UiTheme.FrameStrong(14)); // clear of the corner ornaments
        root.AddChild(panel);
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (var edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{edge}", 12);
        panel.AddChild(margin);
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddChild(side);

        // ── Always visible: where and when, the clock, influence, drift and anything waiting on you ──
        _header = AddLabel(side, 15);
        _header.AddThemeFontOverride("font", UiTheme.Display);
        _header.AddThemeColorOverride("font_color", UiTheme.GoldBright);

        var clock = new HBoxContainer();
        side.AddChild(clock);
        foreach (var (label, speed) in new[] { ("pause", 0), ("play", 1), ("fast", 2), ("fastest", 3) })
        {
            var b = new Button
            {
                Icon = GD.Load<Texture2D>($"res://ui/{label}.svg"), ToggleMode = true, TooltipText = "Space pauses; 1–3 set speed; 4 is fastest, for campaigns",
                CustomMinimumSize = new Vector2(46, 30), IconAlignment = HorizontalAlignment.Center, ExpandIcon = true,
            };
            b.Pressed += () => SetSpeed(speed);
            clock.AddChild(b);
            _speedButtons.Add(b);
        }
        _stepButton = new Button { TooltipText = "Advance one turn (.)" };
        _stepButton.Pressed += StepTurn;
        clock.AddChild(_stepButton);
        clock.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        AddButton(clock, "Save", "Save the game under a name (F5 quick-saves).", ShowSaveDialog);
        AddButton(clock, "Load", "Load a saved game (F9 loads the quick-save).", () => ShowLoadDialog());

        var view = new HFlowContainer(); // wraps rather than widening the dashboard
        side.AddChild(view);
        _worldButton = AddButton(view, "World", "Zoom out to the whole world, or back to the region (W).", ToggleWorldView);
        AddButton(view, "Communes", "Sortable list of every commune (C).", ToggleCommuneList);
        AddButton(view, "Technologies", "The tech tree: what has spread, what it costs, and what to circulate (K).", () => ShowTechTree());
        AddButton(view, "Ledger", "The federation's history: stores, prices, wellbeing, enthusiasm, militia, gold, science, culture, drift (L).", ShowLedger);
        _mapMode = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "What the map colours show.", CustomMinimumSize = new Vector2(150, 0) };
        foreach (var mode in Enum.GetValues<MapMode>()) _mapMode.AddItem($"Map: {(mode == MapMode.Parties ? "Party cells" : mode.ToString())}", (int)mode);
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
        _reliefToggle = new CheckButton { Text = "Relief", ButtonPressed = true, TooltipText = "Shade mountains and hills on the map (R)." };
        _reliefToggle.Toggled += on =>
        {
            _map.ShowRelief = on;
            _map.QueueRedraw();
        };
        view.AddChild(_reliefToggle);

        // The status line: one item per resource, each with its own description on hover.
        var status = new HFlowContainer();
        status.AddThemeConstantOverride("h_separation", 16);
        side.AddChild(status);
        foreach (var (key, tip) in new[]
        {
            ("influence", "Influence: the movement's only spendable resource. It regenerates every turn from political education, wellbeing and the press, up to 100.\nIt pays for schools, pamphlets, mediation, projects, congresses and recall."),
            ("commune", "Communes in the federation: free and federated now, out of those you began with. Occupied or seceded communes don't count."),
            ("enthusiasm", "Enthusiasm: the revolution's spirit. It wears down over a long war, with hunger, lost communes, hierarchy and conscription, and is renewed by culture, towns rising and revolutions abroad.\nIt scales production, militias, how far your ideas spread, and how many answer a call for volunteers."),
            ("militia", "Militia: people under arms across the federation, in thousands. More fighters defend better but work less.\nCall for volunteers in a commune (Commune tab), or impose conscription (Movement tab)."),
            ("gold", "Gold: from the treasuries or from events. It buys arms, grain or machines abroad (Movement tab). While it sits, the committee guarding it adds drift every turn."),
        })
        {
            var item = new RichTextLabel
            {
                BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.Off,
                MouseFilter = MouseFilterEnum.Stop, TooltipText = tip,
            };
            status.AddChild(item);
            _status[key] = item;
        }
        _stage = AddLabel(side, 14);
        _stage.AddThemeFontOverride("font", UiTheme.Display);
        _driftMeter = new DriftMeter();
        side.AddChild(_driftMeter);

        _decisions = new VBoxContainer();
        side.AddChild(_decisions);
        _feedback = AddLabel(side, 13);

        // ── Everything else, one tab at a time ──
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _tabs.TabChanged += i =>
        {
            Audio.Instance?.Play("page", -12);
            if (_tabs.GetTabControl((int)i) is { } page) Motion.FadeIn(page);
        };
        side.AddChild(_tabs);

        var overview = Tab("Overview");
        _driftDetail = AddRich(overview);
        Section(overview, "Resources");
        _resources = AddRich(overview);
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
        var measures = new HFlowContainer(); // wraps in the narrow dashboard instead of squeezing the switches
        movement.AddChild(measures);
        _warCommittee = new CheckButton { Text = "War committee", TooltipText = "Defense +35% and cheaper coordination — drift every turn it sits." };
        _warCommittee.Toggled += on => { if (on != _world.WarCommittee) Do(new SetWarCommittee(on)); };
        measures.AddChild(_warCommittee);
        _trade = new CheckButton { Text = "Foreign trade", TooltipText = "Sell surplus grain abroad for arms. Buyers want currency, which brings markets and accounts back in (drift every turn). Embargoes grow as the idea spreads." };
        _trade.Toggled += on => { if (on != _world.ForeignTrade) Do(new SetForeignTrade(on)); };
        measures.AddChild(_trade);
        _conscription = new CheckButton { Text = "Conscription", TooltipText = "Every commune sends at least a fifth of its people to the militias. Far stronger fronts, fewer hands at work — drift every turn, and enthusiasm wanes." };
        _conscription.Toggled += on => { if (on != _world.Conscription) Do(new SetConscription(on)); };
        measures.AddChild(_conscription);

        var federation = new HBoxContainer();
        movement.AddChild(federation);
        AddButton(federation, "Recall delegates", "Drift down, experience lost for 4 turns.", () => Do(new RecallDelegates()));
        AddButton(federation, "Sponsor congress", "Spread techniques across the federation. Leaves a secretariat behind (drift).", () => Do(new SponsorCongress()));

        BuildFigures(movement);
        Section(movement, "Gold");
        var gold = new HFlowContainer();
        movement.AddChild(gold);
        _seize = AddButton(gold, "Seize the treasuries", "Gold and silver from the banks and churches, once: it buys abroad. +drift now, and the committee that guards it gathers power (drift every turn) until it's spent. Monarchies and churches resent it.",
            () => Do(new SeizeTreasuries()));
        foreach (var (what, label, tip) in new[]
        {
            (Purchase.Arms, "Buy arms", "Goods for the front communes."),
            (Purchase.Grain, "Buy grain", "Food for the hungriest communes."),
            (Purchase.Machines, "Buy machines", "Lasting output in the towns' workshops."),
        })
            _buyButtons.Add(AddButton(gold, label, tip + " Costs gold, a little drift, and needs a state willing to sell (prices worse under embargo).", () => Do(new BuyAbroad(what))));
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
            if (_world.CommonKnowledge(t)) continue;
            _idea.AddItem(t.Name, i);
            _idea.SetItemTooltip(_idea.ItemCount - 1, $"{t.Name}\n+ {t.Perk}\n− {t.Risk}\n\n" + string.Join("\n", t.Effects.Describe().Select(d => (d.Good ? "+ " : "− ") + d.Text)));
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
        AddButton(actions, "Expose party cells", "Debate a rival party's methods in the open: its cells here and next door lose ground. Its patrons abroad are displeased.", () => WithSelected(id => new ExposeCells(id)));
        foreach (var kind in Enum.GetValues<CulturalWorkKind>())
            AddButton(actions, CulturalWorks.Name(kind, _world.Year), CulturalWorks.Describe(kind) + $" Costs {_world.Balance.CostCulturalWork:0} influence; a commune takes {_world.Balance.CulturalWorkCooldown} weeks to take one up.",
                () => WithSelected(id => new FoundCulturalWork(id, kind)));
        AddButton(actions, "Call for volunteers", "More of its people, and some of its neighbours', join the militia: stronger here, less work done. The call carries further when enthusiasm is high.",
            () => WithSelected(id => new CallVolunteers(id)));
        _fortifyButton = AddButton(actions, "Fortify", "Dig in: earthworks and barricades over a few turns, from the commune's own goods. Up to +25% defence; worn down while the front gives way. Front communes only.",
            () => WithSelected(id => new Fortify(id)));
        _raidButton = AddButton(actions, "Raid", "Strike the enemy from here. Success tires the enemy and brings back arms; failure costs fighters and ground. The firmer the line holds, the better the odds. Front communes only.",
            () => WithSelected(id => new Raid(id)));
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
        _stateLore = new VBoxContainer();
        worldTab.AddChild(_stateLore);
        var stateActions = new HFlowContainer();
        worldTab.AddChild(stateActions);
        _delegatesButton = AddButton(stateActions, "Send emissaries", "Meetings, newspapers and unions: the state warms toward you. Fascist governments barely listen.", () =>
        {
            if (_selectedState is { } id) Do(new SendDelegates(id));
        });
        _agitateButton = AddButton(stateActions, "Agitate workers", "Radicalize the state's workers. Its government notices and cools toward you.", () =>
        {
            if (_selectedState is { } id) Do(new AgitateWorkers(id));
        });
        _supportButton = AddButton(stateActions, "Support the rising", "Arms and organizers for a revolution under way: a better chance it wins — and a little drift at home.", () =>
        {
            if (_selectedState is { } id) Do(new SupportUprising(id));
        });
        Section(worldTab, "The powers that matter most to you");
        _stateList = new VBoxContainer();
        worldTab.AddChild(_stateList);

        var log = Tab("Log");
        _log = AddRich(log);
        // The tabs are captions only: with emblems they wouldn't all fit the narrow dashboard.
        _tabs.ClipTabs = false;

        SetSpeed(0);
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void Refresh()
    {
        var w = _world;
        UpdateHeader();
        _stepButton.Text = $"+1 {w.Turn}";
        _status["influence"].Text = $"{UiTheme.Img("influence")} [b]{w.Influence:0}[/b] [color=#9a9283](+{w.InfluenceRegen:0.0}/{w.Turn})[/color]";
        _status["commune"].Text = $"{UiTheme.Img("commune")} [b]{w.Federation.Count()}[/b]" +
            (w.Communes.Count > w.InitialCommuneCount ? $" [color=#9a9283](began {w.InitialCommuneCount})[/color]" : $"/{w.InitialCommuneCount}");
        _status["enthusiasm"].Text = $"{UiTheme.Img("enthusiasm")} {w.Enthusiasm:P0}";
        _status["militia"].Text = $"{UiTheme.Img("militia")} {w.Fighters:0.0}k";
        _status["gold"].Text = $"{UiTheme.Img("gold")} {w.Gold:0}";
        _status["gold"].Visible = w.Gold > 0;

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
        RefreshFigures();
        // The front's buttons: only for a front commune, with the raid's odds.
        var sel = _map.Selected is int si && w.Communes[si].InFederation ? w.Communes[si] : null;
        bool front = sel is { Front: true };
        _fortifyButton.Visible = _raidButton.Visible = front;
        if (front)
        {
            _fortifyButton.Text = sel!.Fortifying > 0 ? $"Digging in ({w.Turns(sel.Fortifying)})" : $"Fortify ({w.Balance.CostFortify:0}) · works {sel.Fortification:P0}";
            _fortifyButton.Disabled = sel.Fortifying > 0 || sel.Fortification >= 0.95;
            int wait = w.Balance.RaidCooldown - (w.Tick - sel.LastRaid);
            _raidButton.Text = wait > 0 ? $"Raid (ready in {w.Turns(wait)})" : $"Raid ({w.Balance.CostRaid:0}) · {sel.RaidChance:P0} chance";
            _raidButton.Disabled = wait > 0;
        }
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
            return $"{UiTheme.Img("enemy")} [b][color={e.Color}]{Esc(e.Name)}[/color][/b]: strength {e.EffectiveStrength:0.0}"
                   + (e.CurrentOffensive is { } o ? $"  [color=#f07060][b]{Esc(o.Name)}[/b][/color]" : "")
                   + $" · exhaustion {e.Exhaustion:P0} · its workers radicalized {e.Radicalization:P0}"
                   + (Politicom.Sim.Global.TechLevelSystem.EnemyEdge(w, e) is var edge and > 1.005 ? $" · [color=#f07060]+{edge - 1:P0} from better weapons[/color]" : "")
                   + $"\nFront: {(fronts.Any() ? string.Join(", ", fronts) : "none")}";
        });
        _resources.Text = ResourcesText(w);
        _enemy.Text = string.Join("\n", lines)
                      + $"\nTrade: {(w.ForeignTrade ? $"selling {w.LastExports:0} food for {w.LastImports:0} goods per {w.Turn}" : "closed")} · embargo {w.Embargo:P0}"
                      + (w.Ally.MilitarizationTicks > 0 ? $"\nMilitarized for {w.Turns(w.Ally.MilitarizationTicks)} more." : "");
        var s = w.Scores;
        _scores.Text = $"★ Survival {s.Survival:0} · Quality of life {s.QualityOfLife:0} · Spread {s.Spread:0} · Freedom {s.Freedom:0}";

        RefreshWorld();
        _warCommittee.SetPressedNoSignal(w.WarCommittee);
        _conscription.SetPressedNoSignal(w.Conscription);
        _seize.Disabled = w.TreasuriesSeized;
        _seize.Text = w.TreasuriesSeized ? "Treasuries seized" : "Seize the treasuries";
        foreach (var b in _buyButtons)
        {
            b.Disabled = w.Gold < w.Balance.GoldPerPurchase || (w.States.Count > 0 && w.Seller is null);
            b.TooltipText = b.TooltipText.Split("\n\nNow:")[0] + $"\n\nNow: {w.Gold:0} gold, {w.Balance.GoldPerPurchase:0} a purchase; " +
                            (w.States.Count == 0 ? "smugglers sell, at a third off." : w.Seller is { } seller ? $"{seller} would sell." : "no state will sell.");
        }
        _trade.SetPressedNoSignal(w.ForeignTrade);
        RefreshDecisions();
        RefreshCommune();
        RefreshProjects();

        var recent = w.Log.TakeLast(30).Reverse().Select(ev =>
            $"[color=#808590]{_world.Format(ev.Date, "d MMM yyyy")}[/color] [color={SeverityColor(ev.Severity)}]{Esc(ev.Text)}[/color]");
        _log.Text = string.Join("\n", recent);
        _seenEvents = w.Log.Count;

        _map.QueueRedraw();
    }

    private void RefreshDecisions()
    {
        // A pop-up whose decision was answered in the side panel, or ran out of time, goes away.
        if (_popupDecision is { } open && _world.PendingEvents.All(p => p.Id != open)) ClosePopup();
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

    private void ShowLedger()
    {
        Did("ledger");
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(1080, 620), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(new LedgerView(_world));
        ShowOverlay("Ledger", "The federation week by week. Each chart runs from the first week to now.", scroll, 1140, ("Close", CloseOverlay));
    }

    /// <summary>The federation's food and goods: what it holds, makes and needs, and where it falls short.</summary>
    private static string ResourcesText(World w)
    {
        var members = w.Federation.ToList();
        if (members.Count == 0) return "";
        string Rate(double made, double need) =>
            $"[color={(made >= need ? "#70d080" : "#f07060")}]{(made >= need ? "+" : "")}{made - need:0.0}[/color]";
        string Names(IEnumerable<Commune> cs)
        {
            var list = cs.Select(c => Esc(c.Name)).ToList();
            return list.Count <= 4 ? string.Join(", ", list) : string.Join(", ", list.Take(4)) + $" and {list.Count - 4} more";
        }
        double food = members.Sum(c => Math.Max(0, c.Food)), foodMade = members.Sum(c => c.FoodProduced) + w.Scenario.FoodImports, foodNeed = members.Sum(w.FoodDemand);
        double goods = members.Sum(c => Math.Max(0, c.Goods)), goodsMade = members.Sum(c => c.GoodsProduced), goodsNeed = members.Sum(w.GoodsDemand);
        var starving = members.Where(c => c.Starving).ToList();
        var shortOfArms = members.Where(c => c.Front && c.GoodsCoverage < 0.9).ToList();
        string turn = w.Turn;
        var text = $"{UiTheme.Img("food")} [b]Food[/b] {food:0} in store, {foodMade:0} {(w.Scenario.FoodImports > 0 ? "grown and imported" : "grown")} against {foodNeed:0} eaten a {turn} ({Rate(foodMade, foodNeed)}) · " +
                   $"{(foodNeed > 0 ? food / foodNeed : 0):0} {turn}s in reserve\n" +
                   $"{UiTheme.Img("goods")} [b]Goods[/b] {goods:0} in store, {goodsMade:0} made against {goodsNeed:0} used a {turn} ({Rate(goodsMade, goodsNeed)})";
        string Dear(double p) => p > 1.3 ? "#f07060" : p < 0.8 ? "#70d080" : "#e4dccb";
        text += $"\n{UiTheme.Img("treasury")} [b]Prices[/b] food [color={Dear(w.FoodPrice)}]{w.FoodPrice:0.00}[/color] · goods [color={Dear(w.GoodsPrice)}]{w.GoodsPrice:0.00}[/color] [color=#9a9283](1 = four weeks in store; scarcity raises them)[/color]";
        var from = new List<string>();
        if (w.LastArms > 0.05) from.Add($"arms {w.LastArms:0.0}");
        if (w.ForeignTrade && w.LastImports > 0.05) from.Add($"goods {w.LastImports:0.0} for {w.LastExports:0.0} food");
        if (w.Scenario.FoodImports > 0) from.Add($"food {w.Scenario.FoodImports:0.0} (counted above)");
        if (from.Count > 0) text += $"\n{UiTheme.Img("world")} [b]From abroad[/b] {string.Join(" · ", from)} a {turn}";
        text += $"\n{UiTheme.Img("militia")} [b]Militia[/b] {w.Fighters:0.0} thousand under arms ({w.Fighters / Math.Max(0.1, members.Sum(c => c.Population)):P0} of the people)" +
                (w.Conscription ? " · [color=#f0a040]conscription[/color]" : "");
        var mood = w.Enthusiasm switch { >= 0.75 => "#70d080", >= 0.5 => "#e0c050", _ => "#f07060" };
        text += $"\n{UiTheme.Img("enthusiasm")} [b]Enthusiasm[/b] [color={mood}]{w.Enthusiasm:P0}[/color] (production and militias ×{w.EnthusiasmFactor:0.00})";
        if (w.Gold > 0 || w.TreasuriesSeized)
            text += $"\n{UiTheme.Img("gold")} [b]Gold[/b] {w.Gold:0}" + (w.Gold > 0 ? $" · its guards add {w.Balance.GoldHoldingDrift * w.Gold * w.Weeks:0.00} drift a {turn}" : " (spent)");
        text += $"\n{UiTheme.Img("science")} [b]Science[/b] {w.LastScience:0.0} a {turn} · {UiTheme.Img("culture")} [b]Culture[/b] {w.Mean(c => c.Culture):P0} on average";
        if (starving.Count > 0) text += $"\n{UiTheme.Img("danger")} [color=#f07060]Starving: {Names(starving)}[/color]";
        if (shortOfArms.Count > 0) text += $"\n{UiTheme.Img("pressure")} [color=#f0a040]Short of arms at the front: {Names(shortOfArms)}[/color]";
        return text;
    }

    // ── Pop-ups: decisions and big news, one at a time, in the middle of the screen ──
    private readonly Queue<GameEvent> _popups = new();
    private Control? _popup;
    private string? _popupDecision;
    /// <summary>Off for screenshot runs (unless --popups), which step many turns unattended.</summary>
    private bool PopupsEnabled { get; set; } = true;

    private void QueuePopup(GameEvent e)
    {
        _popups.Enqueue(e);
        if (_popup == null) ShowNextPopup();
    }

    private void ClosePopup()
    {
        Motion.FadeOutAndFree(_popup);
        _popup = null;
        _popupDecision = null;
        Refresh();
        ShowNextPopup();
    }

    private void ShowNextPopup()
    {
        if (_popup != null || _overlay != null || _world.Outcome != Outcome.Ongoing) return;
        while (_popups.Count > 0)
        {
            var e = _popups.Dequeue();
            // A scripted or random decision still waiting for an answer: its options, right here.
            var pending = e.Severity == Severity.Decision && e.Title.Length > 0
                ? _world.PendingEvents.Select(p => _world.EventDefinition(p.Id)).FirstOrDefault(d => d.Title == e.Title)
                : null;
            if (e.Severity == Severity.Decision && e.Title.Length > 0 && pending == null) continue; // answered meanwhile
            string heading = e.Title.Length > 0 ? e.Title : e.Severity switch
            {
                Severity.Good => "Good news",
                Severity.Danger => "Bad news",
                Severity.Warning => "A warning",
                Severity.Decision => "A decision",
                _ => "News",
            };
            string body = pending != null ? pending.Text : e.Text;
            // News often opens with its own headline ("Orléans rises! Its people…"): don't say it twice.
            if (heading.Length > 0 && body.StartsWith(heading))
                body = body[heading.Length..].TrimStart('!', '.', ':', ' ', '—');
            if (body.Length > 0) body = char.ToUpperInvariant(body[0]) + body[1..];
            var buttons = new List<(string Label, string Tooltip, Action OnPress)>();
            if (pending != null)
                buttons.AddRange(pending.Options.Select((o, i) => (o.Label,
                    $"{o.Description}\n\n{o.Effects.Summary(_world.Turn)}" + (i == pending.DefaultOption ? "\n\n(happens if you don't answer)" : ""),
                    (Action)(() =>
                    {
                        Do(new ChooseEventOption(pending.Id, i));
                        ClosePopup();
                    }))));
            else
            {
                if (e.Commune is int id)
                    buttons.Add(("Show me", "Select it on the map.", () =>
                    {
                        _map.Selected = id;
                        _map.FocusOn(id, 2.5f);
                        ClosePopup();
                    }));
                buttons.Add(("OK", "", ClosePopup));
            }
            _popupDecision = pending?.Id;
            _popup = BuildPopup(heading, body, e, buttons);
            AddChild(_popup);
            Audio.Instance?.Play(e.Severity == Severity.Decision ? "bell" : e.Severity == Severity.Danger ? "drum" : "page", e.Severity == Severity.Decision ? -8 : -4);
            return;
        }
    }

    private Control BuildPopup(string heading, string body, GameEvent e, List<(string Label, string Tooltip, Action OnPress)> buttons)
    {
        var dim = Motion.Backdrop(darken: 0.3f);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.AddChild(center);
        var accent = e.Severity switch
        {
            Severity.Good => new Color("6fbf7f"),
            Severity.Danger => new Color("e0705f"),
            Severity.Warning => new Color("f2c94c"),
            Severity.Decision => new Color("5fc2d9"),
            _ => new Color("9aa0a8"),
        };
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(700, 0) };
        panel.AddThemeStyleboxOverride("panel", UiTheme.FrameStrong(30));
        center.AddChild(Motion.PopIn(panel));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        panel.AddChild(v);
        if (EventArt.Slot(_world, heading, body) is { } art) v.AddChild(EventArt.Frame(art, 640));
        var date = new Label { Text = _world.Format(e.Date, "d MMMM yyyy").ToUpperInvariant(), Modulate = accent };
        date.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(date);
        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 14);
        string emblem = e.Severity switch
        {
            Severity.Good => e.Title.EndsWith(" rises") ? "revolution" : "good",
            Severity.Danger => "danger",
            Severity.Warning => e.Title.StartsWith("Hierarchy") ? "drift" : "warning",
            Severity.Decision => "decision",
            _ => "log",
        };
        titleRow.AddChild(UiTheme.Medallion(emblem, 64));
        var title = UiTheme.Title(heading, 24);
        title.CustomMinimumSize = new Vector2(520, 0);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleRow.AddChild(title);
        v.AddChild(titleRow);
        v.AddChild(new Ornament { CustomMinimumSize = new Vector2(0, 10) });
        var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(568, 0), Text = Esc(body) };
        text.AddThemeFontSizeOverride("normal_font_size", 15);
        v.AddChild(text);
        var options = new VBoxContainer();
        options.AddThemeConstantOverride("separation", 8);
        v.AddChild(options);
        foreach (var (label, tooltip, onPress) in buttons)
        {
            var b = AddButton(options, label, tooltip, onPress);
            b.CustomMinimumSize = new Vector2(0, 38);
            if (tooltip.Length > 0 && tooltip != "Select it on the map.")
            {
                // The consequences right under the choice, not only in a tooltip.
                var note = new Label { Text = tooltip.Split("\n\n").Skip(1).FirstOrDefault() ?? "", Modulate = new Color(1, 1, 1, 0.55f), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(568, 0) };
                note.AddThemeFontSizeOverride("font_size", 12);
                if (note.Text.Length > 0) options.AddChild(note);
            }
        }
        if (_popups.Count > 0)
        {
            var more = new Label { Text = $"{_popups.Count} more waiting", Modulate = new Color(1, 1, 1, 0.45f) };
            more.AddThemeFontSizeOverride("font_size", 12);
            v.AddChild(more);
        }
        return dim;
    }

    private void AddDecision(string text, params (string Label, string Tooltip, Action OnPress)[] options)
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiTheme.FramePanel(12));
        var v = new VBoxContainer();
        box.AddChild(v);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        head.AddChild(UiTheme.Medallion("decision", 44));
        head.AddChild(new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(260, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        v.AddChild(head);
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
            $"{Hint($"Food {Math.Max(0, c.Food):0}", FoodLines(w, c))} (+{c.FoodProduced:0.0}) · goods {Math.Max(0, c.Goods):0} (+{c.GoodsProduced:0.0}) · " +
            $"{Hint($"wellbeing {c.Wellbeing:P0}", PartLines(Explain.Wellbeing(w, c), "Wellbeing moves 15% of the way each turn toward", pct: true))}\n" +
            $"Aid given {c.AidGiven:0.0} / received {c.AidReceived:0.0} · free-riding {c.FreeRiding:P0} · " +
            $"{Hint($"discontent {c.Discontent:0.00}", PartLines(Explain.Discontent(w, c), "Discontent (a commune may vote to leave past about 0.6)"))}\n" +
            (c.Front && !c.Occupied ? DefenseLine(w, c) + "\n" : "") +
            $"Militia {c.Mobilization:P0} of its people ({c.Population * c.Mobilization:0.0} thousand) · culture {c.Culture:P0} · science {c.Science:0.00} a {w.Turn}" + (w.Tick - c.LastCulturalWork < CulturalWorks.Cooldown(w) ? " · taking up a cultural work" : "") + "\n" +
            (c.PartyInfluence > 0.01 ? $"[color=#e07a6a]Party cells: {c.PartyInfluence:P0}[/color] — a rival party's hold here, adding drift\n" : "") +
            $"Techs: {(c.Techs.Count == 0 ? "none" : string.Join(", ", c.Techs.Where(t => !w.CommonKnowledge(w.Techs[t])).Select(t => w.Techs[t].Name)))}\n" +
            $"Neighbours (trust): {neighbours}";
    }

    // ── Why a number is what it is: hover tooltips in rich text ──

    /// <summary>Rich text that shows `lines` as a tooltip on hover, dotted-underlined to say so.</summary>
    private static string Hint(string shown, IEnumerable<string> lines)
    {
        string tip = string.Join("\n", lines).Replace("\"", "'").Replace("[", "(").Replace("]", ")");
        return $"[hint=\"{tip}\"][u]{shown}[/u][/hint]";
    }

    private static IEnumerable<string> PartLines(IEnumerable<Part> parts, string title, bool pct = false)
    {
        var list = parts.ToList();
        string F(double v) => pct ? $"{v * 100:+0;-0} pts" : $"{v:+0.00;-0.00}";
        yield return $"{title}: {(pct ? $"{Explain.Sum(list):P0}" : $"{Explain.Sum(list):0.00}")}";
        foreach (var p in list.Where(p => Math.Abs(p.Value) > 0.0005).OrderByDescending(p => Math.Abs(p.Value)))
            yield return $"   {F(p.Value)}  {p.Label}";
    }

    private static IEnumerable<string> FoodLines(World w, Commune c)
    {
        double eaten = c.Population * w.Balance.FoodNeed;
        yield return $"Food in store: {Math.Max(0, c.Food):0} ({Math.Max(0, c.Food) / Math.Max(0.01, eaten):0.0} {w.Turn}s of eating)";
        yield return $"   +{c.FoodProduced:0.0}  grown this {w.Turn}";
        yield return $"   -{eaten:0.0}  eaten ({c.Population:0}k people)";
        if (c.AidReceived > 0.05 || c.AidGiven > 0.05) yield return $"   aid lately: received {c.AidReceived:0.0}, given {c.AidGiven:0.0}";
        if (c.Starving) yield return "Starving: wellbeing and trust suffer, and it may leave.";
    }

    private static string DefenseLine(World w, Commune c)
    {
        var (added, factors) = Explain.Defense(w, c);
        var lines = new List<string> { $"Defence: {c.Defense:0.00} against pressure {c.Pressure:0.00}" + (c.Pressure > c.Defense ? " (losing ground)" : " (holding)") };
        foreach (var p in added) lines.Add($"   +{p.Value:0.00}  {p.Label}");
        foreach (var f in factors.OrderByDescending(f => Math.Abs(f.Value - 1))) lines.Add($"   x{f.Value:0.00}  {f.Label}");
        string state = c.Pressure > c.Defense ? $"[color=#f07060]{Hint($"defence {c.Defense:0.0} vs pressure {c.Pressure:0.0}", lines)}[/color]"
                                              : $"[color=#70d080]{Hint($"defence {c.Defense:0.0} vs pressure {c.Pressure:0.0}", lines)}[/color]";
        return $"Front: {state}";
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
                CustomMinimumSize = new Vector2(300, 0),
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

    // ── Input helpers ─────────────────────────────────────────────────────────

    private void OnCommuneClicked(int id, bool shift)
    {
        Audio.Instance?.Play("tick", -4);
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
        for (int i = 0; i < _speedButtons.Count; i++) _speedButtons[i].SetPressedNoSignal(i == Math.Min(speed, 3));
    }

    private void Say(string text)
    {
        _feedback.Text = text;
        _feedback.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.4f));
    }

    private static Label AddLabel(Container parent, int size = 14)
    {
        var l = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) };
        l.AddThemeFontSizeOverride("font_size", size);
        parent.AddChild(l);
        return l;
    }

    private static RichTextLabel AddRich(Container parent)
    {
        var r = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(300, 0) };
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
