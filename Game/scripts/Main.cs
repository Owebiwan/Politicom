using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// Root of the prototype UI: the map on the left, the movement's view of the society on the right.
/// Every button turns into a <see cref="PlayerAction"/>; the simulation decides what happens.
/// </summary>
public partial class Main : Control
{
    private static readonly double[] SecondsPerWeek = { 0, 1.0, 0.45, 0.15 };

    private World _world = null!;
    private MapView _map = null!;
    private int _speed;
    private double _accumulator;
    private int _seenEvents;

    private Label _header = null!, _influence = null!, _stage = null!, _scores = null!, _feedback = null!;
    private DriftMeter _driftMeter = null!;
    private HistoryChart _history = null!;
    private Control? _overlay;
    private ScenarioDefinition _scenario = null!;
    private List<ScenarioDefinition> _scenarios = new();
    private TechCatalog _techs = null!;
    private RichTextLabel _driftDetail = null!, _enemy = null!, _commune = null!, _log = null!;
    private VBoxContainer _decisions = null!, _projects = null!;
    private CheckButton _warCommittee = null!;
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
            _scenario = _scenarios.FirstOrDefault(s => s.Id == "catalonia_1936") ?? _scenarios[0];
            _world = World.Create(_scenario, _techs, seed: (ulong)DateTime.Now.Ticks);
        }
        catch (Exception e)
        {
            GD.PushError($"Could not load scenario: {e.Message}");
            AddChild(new Label { Text = $"Could not load scenario:\n{e.Message}" });
            return;
        }
        BuildUi();
        SetWorld(_world);
        var args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--screenshot") < 0 || Array.IndexOf(args, "--intro") >= 0) ShowIntro();
        HandleScreenshotArgs();
    }

    /// <summary>
    /// Dev hook: <c>godot --path Game -- --screenshot out.png [--scenario ID] [--weeks N] [--select ID] [--focus ID] [--zoom Z] [--intro]</c>
    /// simulates N weeks, saves a screenshot and quits. Used to check the UI without clicking.
    /// </summary>
    private async void HandleScreenshotArgs()
    {
        var args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--screenshot");
        if (at < 0 || at + 1 >= args.Length) return;
        string path = args[at + 1];
        int scenarioAt = Array.IndexOf(args, "--scenario");
        if (scenarioAt >= 0 && scenarioAt + 1 < args.Length && _scenarios.FirstOrDefault(s => s.Id == args[scenarioAt + 1]) is { } chosen)
        {
            _scenario = chosen;
            SetWorld(World.Create(chosen, _techs, seed: 1));
        }
        int weeksAt = Array.IndexOf(args, "--weeks");
        int weeks = weeksAt >= 0 && weeksAt + 1 < args.Length ? int.Parse(args[weeksAt + 1]) : 0;
        int selectAt = Array.IndexOf(args, "--select");
        if (selectAt >= 0 && selectAt + 1 < args.Length) _map.Selected = int.Parse(args[selectAt + 1]);
        for (int i = 0; i < weeks && _world.Outcome == Outcome.Ongoing; i++) _world.Step();
        Refresh();
        OnOutcome();
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        int focusAt = Array.IndexOf(args, "--focus");
        int zoomAt = Array.IndexOf(args, "--zoom");
        if (focusAt >= 0 && focusAt + 1 < args.Length)
            _map.FocusOn(int.Parse(args[focusAt + 1]), zoomAt >= 0 && zoomAt + 1 < args.Length ? float.Parse(args[zoomAt + 1], System.Globalization.CultureInfo.InvariantCulture) : 2.5f);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GetTree().Quit();
    }

    public override void _Process(double delta)
    {
        if (_speed == 0 || _world.Outcome != Outcome.Ongoing) return;
        _accumulator += delta;
        if (_accumulator < SecondsPerWeek[_speed]) return;
        _accumulator = 0;
        StepWeek();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_overlay != null || e is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.Space: SetSpeed(_speed == 0 ? 1 : 0); break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(2); break;
            case Key.Key3: SetSpeed(3); break;
            case Key.Period: StepWeek(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void StepWeek()
    {
        if (_world.Outcome != Outcome.Ongoing) return;
        _world.Step();
        // Like a Paradox popup: pause when the movement has a decision to make.
        if (_world.Log.Skip(_seenEvents).Any(e => e.Severity == Severity.Decision)) SetSpeed(0);
        Refresh();
        OnOutcome();
    }

    private void SetWorld(World world)
    {
        _world = world;
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
            var end = scenario.StartDate.AddDays(7 * scenario.EndTick);
            var text = new RichTextLabel
            {
                BbcodeEnabled = true, FitContent = true, ScrollActive = false,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(560, 0),
                Text = $"[b]{Esc(scenario.Name)}[/b]  [color=#9aa0a8]{scenario.StartDate.Year}–{end.Year} · {scenario.Communes.Count} communes · "
                       + $"against {Esc(string.Join(" and ", scenario.Enemies.Select(e => e.Name)))}[/color]\n{Esc(scenario.Description)}",
            };
            text.AddThemeFontSizeOverride("normal_font_size", 14);
            text.AddThemeFontSizeOverride("bold_font_size", 16);
            row.AddChild(text);
            var chosen = scenario;
            var play = AddButton(row, "Play", "", () => StartScenario(chosen));
            play.CustomMinimumSize = new Vector2(110, 40);
            play.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            list.AddChild(row);
            list.AddChild(new HSeparator());
        }

        ShowOverlay("Choose a revolution", """
            [b]You are the movement, not the government.[/b] You can't order a commune to do anything. You fund schools, mediate disputes, circulate ideas and propose projects; the communes decide for themselves.

            [b]The dilemma.[/b] Almost everything that helps you win the war (a war committee, government arms, conscription, appointed commanders) pushes [color=#f2c94c]hierarchy drift[/color] up. At 25, 50 and 75 drift, permanent councils, then commissars, then a police force take over, each with real bonuses. At 100 a state has crystallized and the revolution is over, however strong the society is. Political education, recall and rotation are the brakes.

            [b]Winning.[/b] Hold out as a free federation until the scenario ends. You're scored on survival, quality of life, spread of the idea across the lines, and freedom.

            [b]Controls.[/b] Click a commune to select it; shift-click a second one for mediation or railways. Space pauses, 1–3 set speed, [code].[/code] advances one week. Scroll or pinch to zoom, drag to pan. The game pauses itself when a decision is waiting.
            """, list);
    }

    private void StartScenario(ScenarioDefinition scenario)
    {
        _scenario = scenario;
        NewGame();
        Say($"{scenario.Name} begins.");
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
            _ => ("The federation has been destroyed", "Too few free communes remain for a federation."),
        };
        _map.Banner = title;
        var chart = new HistoryChart { World = w, FontSize = 13, CustomMinimumSize = new Vector2(0, 220) };
        ShowOverlay(title, $"""
            {verdict}

            [b]Survival[/b] {s.Survival:0}    [b]Quality of life[/b] {s.QualityOfLife:0}    [b]Spread[/b] {s.Spread:0}    [b]Freedom[/b] {s.Freedom:0}    —    [b]Total {s.Total:0}[/b]

            {w.Federation.Count()} of {w.InitialCommuneCount} communes free and federated · {w.Communes.Count(c => c.Occupied)} occupied · {w.Communes.Count(c => !c.Occupied && !c.Federated)} seceded · final drift {w.Drift.Value:0} ({DriftEffects.Name(w.Drift.Stage)})
            """, chart, ("Keep looking", CloseOverlay), ("Play again", NewGame), ("Choose scenario", ShowIntro));
    }

    private void ShowOverlay(string title, string body, Control? extra, params (string Label, Action OnPress)[] buttons)
    {
        CloseOverlay();
        SetSpeed(0);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.65f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(760, 0) };
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
        var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(704, 0), Text = body.Trim() };
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
        root.AddChild(_map);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(460, 0) };
        root.AddChild(panel);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{edge}", 12);
        scroll.AddChild(margin);
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        side.AddThemeConstantOverride("separation", 6);
        margin.AddChild(side);

        _header = AddLabel(side, 18);
        var speeds = new HBoxContainer();
        side.AddChild(speeds);
        foreach (var (label, speed) in new[] { ("❚❚", 0), ("▶", 1), ("▶▶", 2), ("▶▶▶", 3) })
        {
            var b = new Button { Text = label, ToggleMode = true, TooltipText = "Space pauses; 1–3 set speed" };
            b.Pressed += () => SetSpeed(speed);
            speeds.AddChild(b);
            _speedButtons.Add(b);
        }
        var step = new Button { Text = "+1 week", TooltipText = "Advance one week (.)" };
        step.Pressed += StepWeek;
        speeds.AddChild(step);
        AddButton(speeds, "Save", "Save to a single quick-save slot.", SaveGame);
        AddButton(speeds, "Load", "Load the quick-save.", LoadGame);
        _mapMode = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var mode in Enum.GetValues<MapMode>()) _mapMode.AddItem($"Map: {mode}", (int)mode);
        _mapMode.ItemSelected += index =>
        {
            _map.Mode = (MapMode)_mapMode.GetItemId((int)index);
            _map.QueueRedraw();
        };
        speeds.AddChild(_mapMode);

        _influence = AddLabel(side);
        Section(side, "Hierarchy drift");
        _stage = AddLabel(side, 15);
        _driftMeter = new DriftMeter();
        side.AddChild(_driftMeter);
        _driftDetail = AddRich(side);

        Section(side, "Outside pressure");
        _enemy = AddRich(side);
        _scores = AddLabel(side);

        Section(side, "So far");
        _history = new HistoryChart();
        side.AddChild(_history);

        _decisions = new VBoxContainer();
        side.AddChild(_decisions);

        Section(side, "The movement");
        var row1 = new HBoxContainer();
        side.AddChild(row1);
        _warCommittee = new CheckButton { Text = "War committee", TooltipText = "Defense +35% and cheaper coordination — drift every week it sits." };
        _warCommittee.Toggled += on => { if (on != _world.WarCommittee) Do(new SetWarCommittee(on)); };
        row1.AddChild(_warCommittee);
        AddButton(row1, "Recall delegates", "Drift down, experience lost for 4 weeks.", () => Do(new RecallDelegates()));
        AddButton(row1, "Congress", "Spread techniques across the federation. Leaves a secretariat behind (drift).", () => Do(new SponsorCongress()));

        var row2 = new HBoxContainer();
        side.AddChild(row2);
        _projectKind = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var p in ProjectCatalog.All) _projectKind.AddItem(p.Name, (int)p.Kind);
        row2.AddChild(_projectKind);
        AddButton(row2, "Propose", "Uses the selected commune (and the shift-selected one for railways).", ProposeProject);

        var row3 = new HBoxContainer();
        side.AddChild(row3);
        _idea = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        for (int i = 0; i < _world.Techs.All.Count; i++)
        {
            var t = _world.Techs.All[i];
            _idea.AddItem(t.Name, i);
            _idea.SetItemTooltip(i, $"+ {t.Perk}\n− {t.Risk}");
        }
        row3.AddChild(_idea);
        AddButton(row3, "Circulate idea", "Steer what communes discover and adopt for 20 weeks.",
            () => Do(new CirculateIdea(_world.Techs.All[_idea.GetSelectedId()].Id)));

        _feedback = AddLabel(side);

        Section(side, "Selected commune");
        _commune = AddRich(side);
        var row4 = new HFlowContainer();
        side.AddChild(row4);
        AddButton(row4, "Practical school", "Productivity and innovation. Watch the expert trap.", () => WithSelected(id => new FundSchool(id, EducationTrack.Practical)));
        AddButton(row4, "Political school", "Cheaper coordination, slower drift, more trust.", () => WithSelected(id => new FundSchool(id, EducationTrack.Political)));
        AddButton(row4, "Pamphlets", "Political education here and next door.", () => WithSelected(id => new CirculatePamphlets(id)));
        AddButton(row4, "Mediate", "Raise trust with the shift-selected neighbour.", () =>
        {
            if (_map.Selected is int a && _map.Secondary is int b) Do(new Mediate(a, b));
            else Say("Select one commune, then shift-click a neighbour.");
        });

        Section(side, "Projects");
        _projects = new VBoxContainer();
        side.AddChild(_projects);

        Section(side, "Events");
        _log = AddRich(side);

        SetSpeed(0);
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void Refresh()
    {
        var w = _world;
        _header.Text = $"{w.Scenario.Name} — {w.Date:d MMMM yyyy}  (week {w.Tick}/{w.Scenario.EndTick})";
        _influence.Text = $"Influence {w.Influence:0}  (+{w.InfluenceRegen:0.0}/week)    Federation {w.Federation.Count()}/{w.InitialCommuneCount}";

        _stage.Text = $"{DriftEffects.Name(w.Drift.Stage)} — {w.Drift.Value:0.0}" + (w.Drift.NextThreshold is { } t ? $"  (next stage at {t:0})" : "");
        _driftMeter.QueueRedraw();
        _history.QueueRedraw();
        var d = w.LastDrift;
        _driftDetail.Text = $"[i]{DriftEffects.Describe(w.Drift.Stage)}[/i]\nLast week {(d.Net >= 0 ? "+" : "")}{d.Net:0.00}: " + string.Join(", ",
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
                      + (w.Ally.MilitarizationTicks > 0 ? $"\nMilitarized for {w.Ally.MilitarizationTicks} more weeks." : "");
        var s = w.Scores;
        _scores.Text = $"Survival {s.Survival:0} · Quality of life {s.QualityOfLife:0} · Spread {s.Spread:0} · Freedom {s.Freedom:0}";

        _warCommittee.SetPressedNoSignal(w.WarCommittee);
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
                $"{o.Description}\n\n{o.Effects.Summary()}" + (i == def.DefaultOption ? "\n\n(happens if you don't answer)" : ""),
                (Action)(() => Do(new ChooseEventOption(def.Id, i))))).ToArray();
            AddDecision($"{def.Title}\n{def.Text}  (answer by week {pending.Expires})", options);
        }
        if (w.Ally.OfferPending)
        {
            AddDecision($"The {w.Ally.Name} offers arms if the militias accept militarization. (expires week {w.Ally.OfferExpires})",
                ("Accept arms", "+30% defense for 26 weeks; +5 drift now and more while it lasts.", () => Do(new AnswerAllyOffer(true))),
                ("Refuse", "The militias stay under their own control.", () => Do(new AnswerAllyOffer(false))));
        }
        foreach (var sec in w.PendingSecessions)
        {
            var c = w.Communes[sec.Commune];
            AddDecision($"{c.Name} votes to leave the federation in week {sec.Deadline}. Mediate with its neighbours, let it go, or hold it by force.",
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
            $"Food {c.Food:0} (+{c.FoodProduced:0.0}) · goods {c.Goods:0} (+{c.GoodsProduced:0.0}) · wellbeing {c.Wellbeing:P0}\n" +
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
                Text = $"#{p.Id} {p.Definition.Name}{target}: {p.Participants.Count}/{p.Invited.Count} joined, {p.LastOverhead:P0} lost to coordination, due week {p.Deadline}",
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
        Say($"Saved week {_world.Tick} ({_world.Date:d MMMM yyyy}).");
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
            _scenario = _scenarios.FirstOrDefault(s => s.Id == world.Scenario.Id) ?? _scenario;
            CloseOverlay();
            SetWorld(world);
            Say($"Loaded week {world.Tick} ({world.Date:d MMMM yyyy}).");
        }
        catch (Exception e)
        {
            Say($"Could not load the quick-save: {e.Message}");
        }
    }

    // ── Input helpers ─────────────────────────────────────────────────────────

    private void OnCommuneClicked(int id, bool shift)
    {
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
        var l = new Label { Text = title.ToUpperInvariant(), Modulate = new Color(0.6f, 0.75f, 0.95f) };
        l.AddThemeFontSizeOverride("font_size", 12);
        parent.AddChild(l);
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
