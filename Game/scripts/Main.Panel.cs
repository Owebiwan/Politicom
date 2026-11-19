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
        // A round brass button seated on the dashboard's edge (on the window's edge when it's folded),
        // a chevron pointing the way the panel will go. An icon, not a letter, so it stays a circle.
        _panelToggle = new Button { FocusMode = FocusModeEnum.None };
        UiTheme.Round(_panelToggle, ToggleSize);
        _panelToggle.Size = new Vector2(ToggleSize, ToggleSize);
        _panelToggle.Pressed += () => SetPanel(!_sidePanel.Visible);
        AddChild(_panelToggle); // over the map and the dashboard both, so it can straddle the border

        // The clock while the dashboard is folded: the date, and pause, play, fast, fastest.
        _miniClock = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -195, OffsetRight = 195, OffsetTop = 10, OffsetBottom = 66,
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
        _miniSpeed.AddRange(BuildClockButtons(row));
        _map3d.AddChild(_miniClock);
        SetPanel(LoadPanelSetting(), save: false);
    }

    private void SetPanel(bool open, bool save = true)
    {
        _sidePanel.Visible = open;
        _miniClock.Visible = !open;
        _panelToggle.Icon = GD.Load<Texture2D>(open ? "res://ui/chevron_right.svg" : "res://ui/chevron_left.svg");
        PlacePanelToggle();
        _panelToggle.TooltipText = open ? "Fold the dashboard away and give the map the whole window (P)" : "Bring back the dashboard (P)";
        if (save)
        {
            var config = new ConfigFile();
            config.Load("user://settings.cfg");
            config.SetValue("ui", "panel", open);
            config.Save("user://settings.cfg");
        }
    }

    private const int ToggleSize = 40;

    /// <summary>Centres the fold button on the dashboard's left border, or just inside the window's edge when folded.</summary>
    private void PlacePanelToggle()
    {
        if (_panelToggle is null) return;
        float y = _map3d.GlobalPosition.Y + _map3d.Size.Y / 2 - ToggleSize / 2f;
        float x = _sidePanel.Visible
            ? _sidePanel.GlobalPosition.X - ToggleSize / 2f + 4 // a little over the frame, onto the panel's own border
            : GetViewportRect().Size.X - ToggleSize - 8;
        _panelToggle.Position = new Vector2(x, y);
        _panelToggle.Size = new Vector2(ToggleSize, ToggleSize);
    }

    private static bool LoadPanelSetting()
    {
        var config = new ConfigFile();
        return config.Load("user://settings.cfg") != Error.Ok || config.GetValue("ui", "panel", true).AsBool();
    }
}
