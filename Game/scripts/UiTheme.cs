using System.Collections.Generic;
using Godot;

namespace Politicom.Game;

/// <summary>
/// One theme for the whole interface, in the manner of Victoria 3: windows of dark grained
/// lacquer in engraved gold frames with flourished corners, title plates and section bars,
/// embossed bronze buttons and tabs, medallions behind emblems, and a book serif (Crimson Pro)
/// throughout — semibold gilt titles, regular cream body text, bold figures. The ornaments are
/// textures from tools/ui/build_ornaments.py; painted art of the same size can replace any.
/// Semantic colours (good/bad, enemies, drift stages) stay where they are used.
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

    /// <summary>Rich-text markup for a name in the title face, gilt.</summary>
    public static string DisplayText(string text, int size = 18) =>
        $"[font=res://fonts/CrimsonPro.ttf][b][font_size={size}][color=#e3bd6b]{text}[/color][/font_size][/b][/font]";

    /// <summary>Crimson Pro at a weight (it's a variable font: 200 light to 900 black).</summary>
    private static Font Weight(FontFile face, int weight) => new FontVariation
    {
        BaseFont = face,
        VariationOpentype = new Godot.Collections.Dictionary { { TextServerManager.GetPrimaryInterface().NameToTag("wght"), weight } },
    };

    private static FontFile? _roman, _italic;
    private static FontFile Roman => _roman ??= LoadFont("CrimsonPro.ttf");
    private static FontFile ItalicFace => _italic ??= LoadFont("CrimsonPro-Italic.ttf");
    private static Font? _body, _bodyMedium, _bodyBold, _bodyItalic, _display, _displayBold;
    /// <summary>Reading text: regular.</summary>
    public static Font Body => _body ??= Weight(Roman, 420);
    public static Font BodyMedium => _bodyMedium ??= Weight(Roman, 520);
    /// <summary>Figures and emphasis.</summary>
    public static Font BodyBold => _bodyBold ??= Weight(Roman, 700);
    public static Font BodyItalic => _bodyItalic ??= Weight(ItalicFace, 420);
    /// <summary>Titles, tabs and section headings: semibold, in title case.</summary>
    public static Font Display => _display ??= Weight(Roman, 600);
    public static Font DisplayBold => _displayBold ??= Weight(Roman, 760);

    // ── Icons ──
    private static readonly Dictionary<string, Texture2D?> IconCache = new();

    /// <summary>Interface keys drawn by another key's sprite.</summary>
    private static readonly Dictionary<string, string> SpriteFor = new()
    {
        ["commune"] = "town", ["movement"] = "t_flag", ["log"] = "t_book", ["overview"] = "world", ["tech"] = "t_gear",
        ["organizational"] = "t_council", ["agricultural"] = "t_plough", ["industrial"] = "t_gear", ["military"] = "t_sword",
        ["communication"] = "t_press", ["rail"] = "t_train", ["scores"] = "good", ["time"] = "chart",
    };

    private static string SpritePath(string key) => $"res://pixel/{SpriteFor.GetValueOrDefault(key, key)}.png";

    /// <summary>A pixel-art sprite (see tools/ui/build_pixel_art.py), or null if there's none for the key.</summary>
    public static Texture2D? Icon(string key)
    {
        if (IconCache.TryGetValue(key, out var cached)) return cached;
        string path = SpritePath(key);
        return IconCache[key] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    /// <summary>A sprite inline in rich text. Sizes that are multiples of 16 keep its pixels square.</summary>
    public static string Img(string key, int size = 16) =>
        ResourceLoader.Exists(SpritePath(key)) ? $"[img={size}x{size}]{SpritePath(key)}[/img]" : "";

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

    // ── Ornaments ──
    private static readonly Dictionary<string, Texture2D> Textures = new();
    private static Texture2D Tex(string file) => Textures.TryGetValue(file, out var t) ? t : Textures[file] = GD.Load<Texture2D>($"res://ui/{file}");

    /// <summary>A nine-slice ornament. Tiling keeps the lacquer grain and gold rules from stretching.</summary>
    private static StyleBoxTexture Ornament(string file, int margin, int contentH, int contentV, bool tile = false, int? marginV = null)
    {
        int mv = marginV ?? margin;
        return new StyleBoxTexture
        {
            Texture = Tex(file),
            TextureMarginLeft = margin, TextureMarginRight = margin, TextureMarginTop = mv, TextureMarginBottom = mv,
            ContentMarginLeft = contentH, ContentMarginRight = contentH, ContentMarginTop = contentV, ContentMarginBottom = contentV,
            AxisStretchHorizontal = tile ? StyleBoxTexture.AxisStretchMode.Tile : StyleBoxTexture.AxisStretchMode.Stretch,
            AxisStretchVertical = tile ? StyleBoxTexture.AxisStretchMode.Tile : StyleBoxTexture.AxisStretchMode.Stretch,
        };
    }

    /// <summary>Tooltips, menus, cards and the tab page: lacquer with a fine gold rim.</summary>
    public static StyleBoxTexture FramePanel(int content = 14) => Ornament("orn_small.png", 12, content, content, tile: true);
    /// <summary>Windows (pop-ups, the scenario picker, the tech tree) and the dashboard: engraved frame, flourished corners.</summary>
    public static StyleBoxTexture FrameStrong(int content = 28) => Ornament("orn_window.png", 44, content, content, tile: true);
    /// <summary>A plate behind a window title.</summary>
    public static StyleBoxTexture TitlePlateStyle() => Ornament("orn_title.png", 40, 44, 6, marginV: 16);
    private static StyleBoxTexture SectionBar() => Ornament("orn_section.png", 0, 10, 4, marginV: 8) is var sb ? Fade(sb) : null!;

    /// <summary>The section bar keeps its left gold edge and its right-hand fade fixed; the middle stretches.</summary>
    private static StyleBoxTexture Fade(StyleBoxTexture sb)
    {
        sb.TextureMarginLeft = 8;
        sb.TextureMarginRight = 140;
        return sb;
    }

    public static Theme Build()
    {
        var t = new Theme { DefaultFont = Body, DefaultFontSize = 16 };
        // Custom-drawn controls (map labels, the tech tree, charts) use the fallback font.
        ThemeDB.FallbackFont = Body;
        t.SetFont("normal_font", "RichTextLabel", Body);
        t.SetFont("bold_font", "RichTextLabel", BodyBold);
        t.SetFont("italics_font", "RichTextLabel", BodyItalic);
        t.SetFont("bold_italics_font", "RichTextLabel", new FontVariation { BaseFont = BodyItalic, VariationEmbolden = 0.6f });

        foreach (var type in new[] { "Label", "Button", "CheckButton", "OptionButton", "LineEdit", "TabContainer", "Tree", "PopupMenu", "TooltipLabel" })
            t.SetColor("font_color", type, Text);
        t.SetColor("default_color", "RichTextLabel", Text);
        t.SetConstant("line_separation", "RichTextLabel", 1);

        // Panels and tooltips: gilt frames.
        t.SetStylebox("panel", "PanelContainer", Box(Panel, radius: 0));
        t.SetStylebox("panel", "TooltipPanel", FramePanel(14));
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetFontSize("font_size", "TooltipLabel", 15);

        // Buttons: dark lacquer with a gilt edge that brightens on hover; red when on.
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            t.SetFont("font", type, Display);
            t.SetFontSize("font_size", type, 15);
            t.SetStylebox("normal", type, Ornament("orn_button.png", 8, 11, 5));
            t.SetStylebox("hover", type, Ornament("orn_button_hover.png", 8, 11, 5));
            t.SetStylebox("pressed", type, Ornament("orn_button_pressed.png", 8, 11, 5));
            t.SetStylebox("hover_pressed", type, Ornament("orn_button_pressed.png", 8, 11, 5));
            t.SetStylebox("disabled", type, Ornament("orn_button_disabled.png", 8, 11, 5));
            t.SetStylebox("focus", type, Box(new Color(0, 0, 0, 0), border: new Color(GoldBright, 0.4f), radius: 4, margin: 11, vmargin: 5));
            t.SetColor("font_color", type, new Color("eadfc8"));
            t.SetColor("font_hover_color", type, new Color("fff4dc"));
            t.SetColor("font_pressed_color", type, Colors.White);
            t.SetColor("font_disabled_color", type, new Color(Muted, 0.6f));
            t.SetColor("font_outline_color", type, new Color(0, 0, 0, 0.6f));
            t.SetConstant("outline_size", type, 2);
            t.SetConstant("icon_max_width", type, 18);
            t.SetConstant("h_separation", type, 6);
        }
        t.SetFont("font", "CheckButton", BodyMedium);
        t.SetFontSize("font_size", "CheckButton", 15);
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

        // Tabs: plates with semibold captions; the open one lit.
        t.SetFont("font", "TabContainer", Display);
        t.SetFontSize("font_size", "TabContainer", 15);
        t.SetStylebox("panel", "TabContainer", FramePanel(12));
        t.SetStylebox("tab_selected", "TabContainer", Ornament("orn_tab_selected.png", 8, 9, 5));
        t.SetStylebox("tab_unselected", "TabContainer", Ornament("orn_tab.png", 8, 9, 5));
        t.SetStylebox("tab_hovered", "TabContainer", Ornament("orn_tab_hover.png", 8, 9, 5));
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
        t.SetFontSize("title_button_font_size", "Tree", 14);
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

    /// <summary>A section heading on a shaded bar with fine gold rules: an emblem and the title, gilt, in title case.</summary>
    public static Control SectionTitle(string text, string? icon = null)
    {
        var bar = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("panel", SectionBar());
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        icon ??= SectionIcons.GetValueOrDefault(text.ToLowerInvariant());
        if (icon != null && Icon(icon) != null) row.AddChild(IconRect(icon, 16));
        var l = new Label { Text = text };
        l.AddThemeFontOverride("font", Display);
        l.AddThemeColorOverride("font_color", GoldBright);
        l.AddThemeFontSizeOverride("font_size", 16);
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        l.AddThemeConstantOverride("shadow_offset_y", 1);
        row.AddChild(l);
        bar.AddChild(row);
        return bar;
    }

    /// <summary>An emblem in a medallion: a beaded gold bezel around a dark enamel disc.</summary>
    public static Control Medallion(string key, int size = 56)
    {
        var holder = new Control { CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var ring = new TextureRect
        {
            Texture = Tex("orn_medallion.png"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ring.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        holder.AddChild(ring);
        // The emblem fills the middle half of the disc.
        var icon = IconRect(key, 0);
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        icon.OffsetLeft = icon.OffsetTop = size / 4f;
        icon.OffsetRight = icon.OffsetBottom = -size / 4f;
        holder.AddChild(icon);
        return holder;
    }

    /// <summary>A window title on a plate with scroll terminals.</summary>
    public static Control TitlePlate(string text, int size = 24)
    {
        var plate = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", TitlePlateStyle());
        var l = Title(text, size);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        plate.AddChild(l);
        return plate;
    }

    /// <summary>A large title (overlays, pop-ups): semibold serif, gilt, with a shadow.</summary>
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

/// <summary>A tapered gold rule that fades at both ends: the divider under titles.</summary>
public partial class Ornament : Control
{
    private static Texture2D? _divider;

    public override void _Draw()
    {
        _divider ??= GD.Load<Texture2D>("res://ui/orn_divider.png");
        DrawTextureRect(_divider, new Rect2(0, Size.Y / 2 - 4, Size.X, 8), false);
    }
}
