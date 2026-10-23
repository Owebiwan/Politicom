using System.Collections.Generic;
using Godot;

namespace Politicom.Game;

/// <summary>
/// One theme for the whole interface, in the manner of a strategy game: dark lacquered panels
/// with gilt borders and corner bosses, Cinzel capitals for titles, tabs and headings,
/// Alegreya Sans for reading, and coloured emblems (game-icons.net) beside resources and
/// headings. Semantic colours (good/bad, enemies, drift stages) stay where they are used.
/// </summary>
internal static class UiTheme
{
    public static readonly Color Background = new("121317");
    public static readonly Color Panel = new("18191e");
    public static readonly Color PanelRaised = new("22232a");
    public static readonly Color Border = new("3a3326");
    public static readonly Color Text = new("e4dccb");
    public static readonly Color Muted = new("9a9283");
    /// <summary>Headings: old gold.</summary>
    public static readonly Color Heading = new("c9a25b");
    public static readonly Color Gold = new("c9a25b");
    public static readonly Color GoldBright = new("e3bd6b");
    public static readonly Color GoldDim = new("6b5530");
    public static readonly Color Accent = new("b8382e");
    public static readonly Color AccentSoft = new("b8382e55");

    // ── Fonts ──
    /// <summary>Symbols the book faces lack (▶ ★ ◆ ✦ ❚) come from the system's symbol fonts.</summary>
    private static readonly SystemFont Symbols = new() { FontNames = new[] { "Apple Symbols", "Segoe UI Symbol", "Noto Sans Symbols 2", "DejaVu Sans", "Arial Unicode MS" } };

    private static FontFile LoadFont(string file)
    {
        var f = GD.Load<FontFile>($"res://fonts/{file}");
        f.Antialiasing = TextServer.FontAntialiasing.Gray;
        f.Hinting = TextServer.Hinting.Light;
        f.Fallbacks = new Godot.Collections.Array<Font> { Symbols };
        return f;
    }

    /// <summary>Rich-text markup for a name in the display face, gilt.</summary>
    public static string DisplayText(string text, int size = 17) =>
        $"[font=res://fonts/Cinzel.ttf][font_size={size}][color=#e3bd6b]{text}[/color][/font_size][/font]";

    private static Font? _body, _bodyMedium, _bodyBold, _bodyItalic, _display, _displayBold;
    /// <summary>Reading text.</summary>
    public static Font Body => _body ??= LoadFont("AlegreyaSans-Regular.ttf");
    public static Font BodyMedium => _bodyMedium ??= LoadFont("AlegreyaSans-Medium.ttf");
    public static Font BodyBold => _bodyBold ??= LoadFont("AlegreyaSans-Bold.ttf");
    public static Font BodyItalic => _bodyItalic ??= LoadFont("AlegreyaSans-Italic.ttf");
    /// <summary>Titles, tabs and headings: classical capitals.</summary>
    public static Font Display => _display ??= LoadFont("Cinzel.ttf");
    public static Font DisplayBold => _displayBold ??= new FontVariation { BaseFont = Display, VariationEmbolden = 0.6f };

    // ── Icons ──
    private static readonly Dictionary<string, Texture2D?> IconCache = new();

    /// <summary>A coloured emblem from res://icons (see tools/ui/build_icons.py), or null if missing.</summary>
    public static Texture2D? Icon(string key)
    {
        if (IconCache.TryGetValue(key, out var cached)) return cached;
        string path = $"res://icons/{key}.svg";
        return IconCache[key] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    /// <summary>An emblem inline in rich text.</summary>
    public static string Img(string key, int size = 18) =>
        ResourceLoader.Exists($"res://icons/{key}.svg") ? $"[img={size}x{size}]res://icons/{key}.svg[/img]" : "";

    /// <summary>An emblem as a control, sized for a line of text.</summary>
    public static TextureRect IconRect(string key, int size = 20) => new()
    {
        Texture = Icon(key),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size),
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>The emblem for a section title, by its words.</summary>
    private static readonly Dictionary<string, string> SectionIcons = new()
    {
        ["resources"] = "treasury", ["outside pressure"] = "enemy", ["so far"] = "chart", ["emergency measures"] = "danger",
        ["gold"] = "gold", ["propose a project"] = "project", ["circulate an idea"] = "decision", ["projects under way"] = "project",
        ["selected country"] = "state", ["the world"] = "world", ["communes"] = "commune", ["revolutions"] = "revolution",
    };

    // ── Frames ──
    private static StyleBoxTexture Frame(string file, int margin, int content)
    {
        var sb = new StyleBoxTexture
        {
            Texture = GD.Load<Texture2D>($"res://ui/{file}"),
            TextureMarginLeft = margin, TextureMarginRight = margin, TextureMarginTop = margin, TextureMarginBottom = margin,
            ContentMarginLeft = content, ContentMarginRight = content, ContentMarginTop = content, ContentMarginBottom = content,
        };
        return sb;
    }

    /// <summary>Gilt frame for panels and tooltips.</summary>
    public static StyleBoxTexture FramePanel(int content = 16) => Frame("frame.svg", 20, content);
    /// <summary>A heavier frame for pop-ups and the scenario picker.</summary>
    public static StyleBoxTexture FrameStrong(int content = 26) => Frame("frame_strong.svg", 22, content);

    public static Theme Build()
    {
        var t = new Theme { DefaultFont = Body, DefaultFontSize = 15 };
        // Custom-drawn controls (map labels, the tech tree, charts) use the fallback font.
        ThemeDB.FallbackFont = Body;
        t.SetFont("normal_font", "RichTextLabel", Body);
        t.SetFont("bold_font", "RichTextLabel", BodyBold);
        t.SetFont("italics_font", "RichTextLabel", BodyItalic);
        t.SetFont("bold_italics_font", "RichTextLabel", new FontVariation { BaseFont = BodyItalic, VariationEmbolden = 0.6f });

        foreach (var type in new[] { "Label", "Button", "CheckButton", "OptionButton", "LineEdit", "TabContainer", "Tree", "PopupMenu", "TooltipLabel" })
            t.SetColor("font_color", type, Text);
        t.SetColor("default_color", "RichTextLabel", Text);
        t.SetConstant("line_separation", "RichTextLabel", 2);

        // Panels and tooltips: gilt frames.
        t.SetStylebox("panel", "PanelContainer", Box(Panel, radius: 0));
        t.SetStylebox("panel", "TooltipPanel", FramePanel(14));
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetFontSize("font_size", "TooltipLabel", 14);

        // Buttons: dark lacquer with a gilt edge that brightens on hover; red when on.
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            t.SetFont("font", type, BodyMedium);
            t.SetStylebox("normal", type, Bevel(PanelRaised, GoldDim));
            t.SetStylebox("hover", type, Bevel(new Color("2b2b31"), Gold));
            t.SetStylebox("pressed", type, Bevel(Accent, GoldBright));
            t.SetStylebox("hover_pressed", type, Bevel(new Color("c84a3f"), GoldBright));
            t.SetStylebox("disabled", type, Bevel(new Color("1c1d22"), new Color("3a3528")));
            t.SetStylebox("focus", type, Box(new Color(0, 0, 0, 0), border: new Color(GoldBright, 0.5f), radius: 3, margin: 8, vmargin: 5));
            t.SetColor("font_hover_color", type, new Color("fff4dc"));
            t.SetColor("font_pressed_color", type, Colors.White);
            t.SetColor("font_disabled_color", type, new Color(Muted, 0.6f));
            t.SetConstant("icon_max_width", type, 18);
            t.SetConstant("h_separation", type, 6);
        }
        t.SetFont("font", "CheckButton", BodyMedium);
        t.SetConstant("icon_max_width", "CheckButton", 0); // switches keep their own size (Button's emblem limit would shrink them)
        // Gilt switches: red with a gold knob when on, dark with a grey knob when off — both legible on dark panels.
        var on = GD.Load<Texture2D>("res://ui/switch_on.svg");
        var off = GD.Load<Texture2D>("res://ui/switch_off.svg");
        t.SetIcon("checked", "CheckButton", on);
        t.SetIcon("unchecked", "CheckButton", off);
        t.SetIcon("checked_disabled", "CheckButton", on);
        t.SetIcon("unchecked_disabled", "CheckButton", off);
        t.SetStylebox("normal", "CheckButton", Box(new Color(0, 0, 0, 0), margin: 4, vmargin: 4));
        t.SetStylebox("hover", "CheckButton", Box(new Color("ffffff0d"), margin: 4, vmargin: 4));
        t.SetStylebox("pressed", "CheckButton", Box(new Color(0, 0, 0, 0), margin: 4, vmargin: 4));
        t.SetStylebox("hover_pressed", "CheckButton", Box(new Color("ffffff0d"), margin: 4, vmargin: 4));
        t.SetStylebox("focus", "CheckButton", Box(new Color(0, 0, 0, 0), margin: 4, vmargin: 4));
        t.SetColor("font_pressed_color", "CheckButton", GoldBright);
        t.SetColor("font_hover_pressed_color", "CheckButton", GoldBright);

        // Inputs and menus
        t.SetStylebox("normal", "LineEdit", Box(new Color("0f1013"), border: GoldDim, radius: 3, margin: 8, vmargin: 4));
        t.SetStylebox("focus", "LineEdit", Box(new Color("0f1013"), border: Gold, radius: 3, margin: 8, vmargin: 4));
        t.SetStylebox("panel", "PopupMenu", FramePanel(10));
        t.SetStylebox("hover", "PopupMenu", Box(AccentSoft, radius: 2));
        t.SetFont("font", "PopupMenu", Body);

        // Tabs: Cinzel captions, a gilt rule under the open tab.
        t.SetFont("font", "TabContainer", Display);
        t.SetFontSize("font_size", "TabContainer", 12);
        t.SetStylebox("panel", "TabContainer", Box(Panel, border: GoldDim, radius: 0, margin: 12));
        t.SetStylebox("tab_selected", "TabContainer", Box(Panel, radius: 0, margin: 7, vmargin: 6, underline: GoldBright));
        t.SetStylebox("tab_unselected", "TabContainer", Box(Background, radius: 0, margin: 7, vmargin: 6));
        t.SetStylebox("tab_hovered", "TabContainer", Box(new Color("1d1e23"), radius: 0, margin: 7, vmargin: 6, underline: GoldDim));
        t.SetColor("font_selected_color", "TabContainer", GoldBright);
        t.SetColor("font_unselected_color", "TabContainer", Muted);
        t.SetColor("font_hovered_color", "TabContainer", Text);
        t.SetConstant("icon_max_width", "TabContainer", 16);
        t.SetConstant("icon_separation", "TabContainer", 4);

        // Bars, separators, scrolling
        t.SetStylebox("background", "ProgressBar", Box(new Color("0f1013"), border: GoldDim, radius: 2));
        t.SetStylebox("fill", "ProgressBar", Box(new Color("5a8fd0"), radius: 2));
        t.SetStylebox("separator", "HSeparator", Line(new Color(GoldDim, 0.7f)));
        t.SetConstant("separation", "HSeparator", 14);
        t.SetStylebox("scroll", "VScrollBar", Box(new Color(0, 0, 0, 0), radius: 4));
        t.SetStylebox("grabber", "VScrollBar", Box(new Color("4a4234"), radius: 4));
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(GoldDim, radius: 4));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(Gold, radius: 4));
        t.SetStylebox("scroll", "HScrollBar", Box(new Color(0, 0, 0, 0), radius: 4));
        t.SetStylebox("grabber", "HScrollBar", Box(new Color("4a4234"), radius: 4));
        t.SetStylebox("grabber_highlight", "HScrollBar", Box(GoldDim, radius: 4));

        // Commune table
        t.SetStylebox("panel", "Tree", Box(new Color("0f1013"), border: GoldDim, radius: 2, margin: 2));
        t.SetStylebox("selected", "Tree", Box(AccentSoft, radius: 2));
        t.SetStylebox("selected_focus", "Tree", Box(AccentSoft, radius: 2));
        t.SetFont("title_button_font", "Tree", Display);
        t.SetFontSize("title_button_font_size", "Tree", 12);
        t.SetStylebox("title_button_normal", "Tree", Box(PanelRaised, radius: 0, margin: 6, vmargin: 4, underline: GoldDim));
        t.SetStylebox("title_button_hover", "Tree", Box(new Color("2b2b31"), radius: 0, margin: 6, vmargin: 4, underline: Gold));
        t.SetStylebox("title_button_pressed", "Tree", Box(Accent, radius: 0, margin: 6, vmargin: 4));
        t.SetColor("title_button_color", "Tree", Heading);
        t.SetColor("guide_color", "Tree", new Color("ffffff10"));

        t.SetConstant("separation", "VBoxContainer", 6);
        t.SetConstant("separation", "HBoxContainer", 6);
        t.SetConstant("h_separation", "HFlowContainer", 6);
        t.SetConstant("v_separation", "HFlowContainer", 6);
        return t;
    }

    /// <summary>
    /// A section heading: an emblem, the title in gilt capitals, and a thin gilt rule.
    /// </summary>
    public static Control SectionTitle(string text, string? icon = null)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        icon ??= SectionIcons.GetValueOrDefault(text.ToLowerInvariant());
        if (icon != null && Icon(icon) != null) row.AddChild(IconRect(icon, 20));
        var l = new Label { Text = text.ToUpperInvariant() };
        l.AddThemeFontOverride("font", Display);
        l.AddThemeColorOverride("font_color", Heading);
        l.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(l);
        row.AddChild(new Ornament { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(20, 14) });
        return row;
    }

    /// <summary>A large title (overlays, pop-ups): Cinzel, gilt.</summary>
    public static Label Title(string text, int size = 26)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontOverride("font", Display);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", GoldBright);
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        l.AddThemeConstantOverride("shadow_offset_y", 2);
        return l;
    }

    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 5, int margin = 0, int vmargin = -1, Color? underline = null)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = margin, ContentMarginRight = margin,
            ContentMarginTop = vmargin < 0 ? margin : vmargin, ContentMarginBottom = vmargin < 0 ? margin : vmargin,
        };
        if (border is { } b)
        {
            sb.BorderColor = b;
            sb.BorderWidthLeft = sb.BorderWidthRight = sb.BorderWidthTop = sb.BorderWidthBottom = 1;
        }
        if (underline is { } u)
        {
            sb.BorderColor = u;
            sb.BorderWidthBottom = 2;
        }
        return sb;
    }

    /// <summary>A raised button face: gilt edge, a darker lower lip, a faint top light.</summary>
    private static StyleBoxFlat Bevel(Color bg, Color edge)
    {
        var sb = Box(bg, border: edge, radius: 3, margin: 10, vmargin: 5);
        sb.BorderWidthBottom = 2;
        sb.ShadowColor = new Color(0, 0, 0, 0.35f);
        sb.ShadowSize = 2;
        sb.ShadowOffset = new Vector2(0, 1);
        return sb;
    }

    private static StyleBoxLine Line(Color color) => new() { Color = color, Thickness = 1 };
}

/// <summary>A thin gilt rule: the flourish after a section title.</summary>
public partial class Ornament : Control
{
    public override void _Draw()
    {
        float y = Size.Y / 2 + 1;
        DrawLine(new Vector2(2, y), new Vector2(Size.X - 2, y), new Color(UiTheme.Gold, 0.55f), 1);
    }
}
