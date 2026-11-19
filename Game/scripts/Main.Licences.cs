using Godot;

namespace Politicom.Game;

/// <summary>
/// Licences, from the start menu: the game's own notice (GNU GPL v3 or later), the credits for its
/// maps, pictures, sounds and models (each under its own licence), Godot's licence and the full
/// text of the GPL. Everything a copy of the game must carry, readable inside it.
/// </summary>
public partial class Main
{
    private const string GameNotice =
        "Politicom is free software: you can redistribute it and/or modify it under the terms of the [b]GNU General Public License[/b] " +
        "as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.\n\n" +
        "It is distributed in the hope that it will be useful, but [b]without any warranty[/b]; without even the implied warranty of " +
        "merchantability or fitness for a particular purpose. See the GNU General Public License below for more details.\n\n" +
        "Its source code comes with every copy (Politicom-source.zip, next to the game). The historical borders it draws on are " +
        "themselves under the GPL v3; its other data, pictures, sounds, fonts and models keep their own licences, credited below.";

    private void ShowLicences()
    {
        static string Read(string path) => FileAccess.FileExists(path) ? FileAccess.GetFileAsString(path) : "";
        var text = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false, SelectionEnabled = true,
            CustomMinimumSize = new Vector2(980, 0),
        };
        text.AddThemeFontSizeOverride("normal_font_size", 14);
        text.AddThemeFontSizeOverride("bold_font_size", 14);
        string credits = Esc(Read("res://data/geo/ATTRIBUTION.md")).Replace("**", "");
        text.Text = $"{UiTheme.DisplayText("Politicom", 20)}\n{GameNotice}\n\n"
                    + $"{UiTheme.DisplayText("Credits", 20)}\n{credits}\n\n"
                    + $"{UiTheme.DisplayText("Godot Engine", 20)}\n{Esc(Engine.GetLicenseText())}\n\n"
                    + $"{UiTheme.DisplayText("GNU General Public License, version 3", 20)}\n{Esc(Read("res://data/licenses/GPL-3.0.txt"))}";
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(1000, 620), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(text);
        ShowOverlay("Licences", "", scroll, 1080, ("Back", ShowStartMenu));
    }
}
