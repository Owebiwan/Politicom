using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// The tutorial: a guided first game in Revolutionary Catalonia. A panel at the top left of the
/// map gives one step at a time; each step waits until the player has done what it asks (selected
/// a commune, funded a school, let time run, backed a campaign, mediated, opened the tree and the
/// ledger), then moves on. Steps that only explain have a Next button. It can be left at any time.
/// </summary>
public partial class Main
{
    private sealed record Step(string Title, string Text, Func<bool>? Done, int? Tab = null);

    private List<Step>? _tutorial;
    private int _tutorialStep;
    private PanelContainer? _tutorialPanel;
    private int _tutorialStartTick;
    /// <summary>What the player has done during the tutorial: action names, and "techs" / "ledger" when those open.</summary>
    private readonly HashSet<string> _done = new();

    private void Did(string what)
    {
        if (_tutorial != null) _done.Add(what);
    }

    private void StartTutorial()
    {
        var scenario = FindScenario("catalonia_1936");
        if (scenario is null)
        {
            Say("The tutorial needs the Catalonia scenario.");
            return;
        }
        StartScenario(scenario);
        _done.Clear();
        _tutorialStartTick = _world.Tick;
        int Commune(string name) => _world.Communes.FirstOrDefault(c => c.Name == name)?.Id ?? 0;
        int lleida = Commune("Lleida"), balaguer = Commune("Balaguer");
        _tutorial = new List<Step>
        {
            new("Welcome to the revolution",
                "July 1936. The military uprising has been beaten in Barcelona, and the towns of Catalonia and Aragon run themselves.\n\n" +
                "[b]You are the movement, not a government.[/b] You can't order a commune to do anything. You spend [b]influence[/b] to fund schools, mediate, propose projects and spread ideas; the communes decide.",
                null),
            new("Your communes",
                "Each town on the map is a self-governing commune. Click [b]Lleida[/b] on the map to see it.\n\nThe Commune tab on the right shows its people, food, education and values.",
                () => _map.Selected == lleida, CommuneTab),
            new("Hierarchy drift",
                "The bar under the clock is [b]hierarchy drift[/b]: how far the revolution is sliding toward a new state.\n\n" +
                "Almost everything that wins the war (a war committee, conscription, appointed commanders) pushes it up. At 25, 50 and 75 new stages take hold; at 100 a state has crystallized and you've lost, however strong you are. " +
                "Political education, recall and rotation push it back down.",
                null),
            new("Fund a school",
                "With Lleida selected, press [b]Political school[/b] in the Commune tab.\n\nPolitical education is your main brake on drift. Practical schooling raises production, but if it runs too far ahead of political education experts start to take charge: the [b]expert trap[/b].",
                () => _done.Contains("FundSchool:Political"), CommuneTab),
            new("Let time run",
                "Press [b]Space[/b] or ▶ at the top to start the clock, and let a week or two pass. The game pauses itself when a decision needs you.\n\nKeys 1 to 3 set the speed; Space pauses.",
                () => _world.Tick >= _tutorialStartTick + 2),
            new("The front",
                "The communes in red outline in Aragon face the Nationalist Army. When one is losing ground, back a [b]defensive campaign[/b]: in the Movement tab choose it under Propose and press Propose, or press [b]Back the proposal[/b] when a commune asks for one.\n\nMilitias from behind the lines march to reinforce it.",
                () => _world.ActiveProjects.Any(p => p.Kind == ProjectKind.DefensiveCampaign), 1),
            new("Trust between communes",
                "Communes cooperate as far as they trust each other; the lines on the map show it. Click [b]Lleida[/b], then [b]shift-click Balaguer[/b] next to it, and press [b]Mediate[/b] in the Commune tab.",
                () => _done.Contains("Mediate"), CommuneTab),
            new("Technologies",
                "Press [b]Technologies[/b] (or K). Techniques spread between communes through trust; you can't pick them, but you can circulate an idea and set where research goes.\n\nEvery line has a free path that slows drift. Close the tree to go on.",
                () => _done.Contains("techs")),
            new("The ledger",
                "Press [b]Ledger[/b] (or L) to see the federation's history: food, prices, wellbeing, militia, drift and more. Close it to go on.",
                () => _done.Contains("ledger")),
            new("You're on your own",
                "That's the heart of it. Hold out as a free federation until 1939; you're scored on survival, quality of life, spread and freedom.\n\n" +
                "Tips will keep appearing at the top right when something needs you. Hover anything for an explanation. Good luck.",
                null),
        };
        _ = balaguer;
        _tutorialStep = 0;
        ShowTutorialStep();
    }

    private void EndTutorial()
    {
        Motion.FadeOutAndFree(_tutorialPanel);
        _tutorialPanel = null;
        _tutorial = null;
    }

    private void ShowTutorialStep()
    {
        if (_tutorial is null) return;
        if (_tutorialStep >= _tutorial.Count)
        {
            EndTutorial();
            return;
        }
        var step = _tutorial[_tutorialStep];
        Motion.FadeOutAndFree(_tutorialPanel);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(380, 0), Position = new Vector2(16, 16), MouseFilter = MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", UiTheme.FrameStrong(26));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        var head = new Label { Text = $"Tutorial · {_tutorialStep + 1} of {_tutorial.Count}", Modulate = new Color(1, 1, 1, 0.55f) };
        head.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(head);
        var text = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(380, 0),
            Text = $"{UiTheme.DisplayText(step.Title, 19)}\n{step.Text}",
        };
        text.AddThemeFontSizeOverride("normal_font_size", 14);
        text.AddThemeFontSizeOverride("bold_font_size", 14);
        box.AddChild(text);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        box.AddChild(row);
        AddButton(row, "Leave the tutorial", "Keep playing this game on your own.", EndTutorial);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        if (step.Done is null)
            AddButton(row, _tutorialStep == _tutorial.Count - 1 ? "Play on" : "Next", "", NextTutorialStep);
        else
        {
            var waiting = new Label { Text = "Do it to go on…", Modulate = new Color(0.95f, 0.8f, 0.35f) };
            waiting.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(waiting);
        }
        Motion.SlideIn(panel);
        _map3d.AddChild(panel);
        _tutorialPanel = panel;
        if (step.Tab is int tab) _tabs.CurrentTab = tab;
    }

    private void NextTutorialStep()
    {
        _tutorialStep++;
        Audio.Instance?.Play("good", -10);
        ShowTutorialStep();
    }

    /// <summary>Checked every frame: moves on when the step's task is done.</summary>
    private void UpdateTutorial()
    {
        if (_tutorial is null || _tutorialStep >= _tutorial.Count) return;
        if (_tutorial[_tutorialStep].Done is { } done && done()) NextTutorialStep();
    }
}
