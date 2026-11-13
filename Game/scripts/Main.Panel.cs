using System.Collections.Generic;
using Godot;

namespace Politicom.Game;

/// <summary>
/// The dashboard folds away: a tab on the map's right edge (or P) hides it and gives the map
/// the whole window; a small clock at the top of the map keeps the date and the speed at hand
/// while it's folded. Clicking a commune brings it back. Remembered between sessions.
/// </summary>
public partial class Main
{
    private PanelContainer _sidePanel = null!;
    private Button _panelToggle = null!;
    private PanelContainer _miniClock = null!;
    private Label _miniHeader = null!;
    private readonly List<Button> _miniSpeed = new();

    private void BuildPanelToggle()
    {
        _panelToggle = new Button
        {
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -26, OffsetRight = 0, OffsetTop = -44, OffsetBottom = 44,
            FocusMode = FocusModeEnum.None,
        };
        _panelToggle.AddThemeFontSizeOverride("font_size", 20);
        _panelToggle.Pressed += () => SetPanel(!_sidePanel.Visible);
        _map3d.AddChild(_panelToggle);

        // The clock while the dashboard is folded: the date, and pause, play, fast, fastest.
        _miniClock = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -230, OffsetRight = 230, OffsetTop = 12, OffsetBottom = 60,
            Visible = false,
        };
        _miniClock.AddThemeStyleboxOverride("panel", UiTheme.FrameStrong(8));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 6);
        _miniClock.AddChild(row);
        _miniHeader = new Label { VerticalAlignment = VerticalAlignment.Center };
        _miniHeader.AddThemeFontOverride("font", UiTheme.Display);
        _miniHeader.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        row.AddChild(_miniHeader);
        foreach (var (label, speed) in new[] { ("pause", 0), ("play", 1), ("fast", 2), ("fastest", 3) })
        {
            var b = new Button
            {
                Icon = GD.Load<Texture2D>($"res://ui/{label}.svg"), ToggleMode = true, FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(40, 28), IconAlignment = HorizontalAlignment.Center, ExpandIcon = true,
            };
            b.Pressed += () => SetSpeed(speed);
            row.AddChild(b);
            _miniSpeed.Add(b);
        }
        _map3d.AddChild(_miniClock);
        SetPanel(LoadPanelSetting(), save: false);
    }

    private void SetPanel(bool open, bool save = true)
    {
        _sidePanel.Visible = open;
        _miniClock.Visible = !open;
        _panelToggle.Text = open ? "›" : "‹";
        _panelToggle.TooltipText = open ? "Fold the dashboard away and give the map the whole window (P)" : "Bring back the dashboard (P)";
        if (save)
        {
            var config = new ConfigFile();
            config.Load("user://settings.cfg");
            config.SetValue("ui", "panel", open);
            config.Save("user://settings.cfg");
        }
    }

    private static bool LoadPanelSetting()
    {
        var config = new ConfigFile();
        return config.Load("user://settings.cfg") != Error.Ok || config.GetValue("ui", "panel", true).AsBool();
    }
}
