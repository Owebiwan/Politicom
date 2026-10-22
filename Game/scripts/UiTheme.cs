using Godot;

namespace Politicom.Game;

/// <summary>
/// One theme for the whole interface: dark panels, a warm red accent for the movement, and
/// consistent buttons, inputs and tabs. Semantic colours (good/bad, enemies, drift stages) stay
/// where they are used; everything structural comes from here.
/// </summary>
internal static class UiTheme
{
    public static readonly Color Background = new("14161a");
    public static readonly Color Panel = new("1b1e24");
    public static readonly Color PanelRaised = new("23272f");
    public static readonly Color Border = new("2f343d");
    public static readonly Color Text = new("d9dce1");
    public static readonly Color Muted = new("8b919b");
    public static readonly Color Heading = new("9db4d6");
    public static readonly Color Accent = new("c4473a");
    public static readonly Color AccentSoft = new("c4473a55");

    public static Theme Build()
    {
        var t = new Theme { DefaultFont = Font(), DefaultFontSize = 14 };
        // Rich text needs its own bold and italic faces, or [b] and [i] render as plain text.
        t.SetFont("normal_font", "RichTextLabel", Font());
        t.SetFont("bold_font", "RichTextLabel", Font(weight: 700));
        t.SetFont("italics_font", "RichTextLabel", Font(italic: true));
        t.SetFont("bold_italics_font", "RichTextLabel", Font(weight: 700, italic: true));

        foreach (var type in new[] { "Label", "Button", "CheckButton", "OptionButton", "LineEdit", "TabContainer", "Tree", "PopupMenu", "TooltipLabel" })
            t.SetColor("font_color", type, Text);
        t.SetColor("default_color", "RichTextLabel", Text);

        // Panels
        t.SetStylebox("panel", "PanelContainer", Box(Panel, radius: 0));
        t.SetStylebox("panel", "TooltipPanel", Box(new Color("0d0f12f2"), border: Border, margin: 10));
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetFontSize("font_size", "TooltipLabel", 13);

        // Buttons: flat dark, red when pressed or toggled on
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            t.SetStylebox("normal", type, Box(PanelRaised, border: Border, margin: 7, vmargin: 4));
            t.SetStylebox("hover", type, Box(new Color("2c313b"), border: new Color("3d434e"), margin: 7, vmargin: 4));
            t.SetStylebox("pressed", type, Box(Accent, border: Accent, margin: 7, vmargin: 4));
            t.SetStylebox("hover_pressed", type, Box(new Color("d4574a"), border: Accent, margin: 7, vmargin: 4));
            t.SetStylebox("disabled", type, Box(new Color("1e2127"), border: new Color("262a31"), margin: 7, vmargin: 4));
            t.SetStylebox("focus", type, Box(new Color(0, 0, 0, 0), border: AccentSoft, margin: 7, vmargin: 4));
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Colors.White);
            t.SetColor("font_disabled_color", type, Muted);
        }
        t.SetStylebox("normal", "CheckButton", Box(new Color(0, 0, 0, 0), margin: 4, vmargin: 4));
        t.SetStylebox("hover", "CheckButton", Box(new Color("ffffff0d"), margin: 4, vmargin: 4));
        t.SetStylebox("pressed", "CheckButton", Box(new Color(0, 0, 0, 0), margin: 4, vmargin: 4));
        t.SetStylebox("focus", "CheckButton", Box(new Color(0, 0, 0, 0), margin: 4, vmargin: 4));

        // Inputs
        t.SetStylebox("normal", "LineEdit", Box(new Color("111317"), border: Border, margin: 8, vmargin: 4));
        t.SetStylebox("focus", "LineEdit", Box(new Color("111317"), border: Accent, margin: 8, vmargin: 4));
        t.SetStylebox("panel", "PopupMenu", Box(new Color("15181d"), border: Border, margin: 6));
        t.SetStylebox("hover", "PopupMenu", Box(AccentSoft, radius: 3));

        // Tabs
        t.SetStylebox("panel", "TabContainer", Box(Panel, radius: 0, margin: 10));
        t.SetStylebox("tab_selected", "TabContainer", Box(Panel, radius: 0, margin: 12, vmargin: 6, underline: Accent));
        t.SetStylebox("tab_unselected", "TabContainer", Box(Background, radius: 0, margin: 12, vmargin: 6));
        t.SetStylebox("tab_hovered", "TabContainer", Box(new Color("1f2329"), radius: 0, margin: 12, vmargin: 6));
        t.SetColor("font_selected_color", "TabContainer", Colors.White);
        t.SetColor("font_unselected_color", "TabContainer", Muted);
        t.SetColor("font_hovered_color", "TabContainer", Text);

        // Bars, separators, scrolling
        t.SetStylebox("background", "ProgressBar", Box(new Color("111317"), radius: 3));
        t.SetStylebox("fill", "ProgressBar", Box(new Color("5a8fd0"), radius: 3));
        t.SetStylebox("separator", "HSeparator", Line(Border));
        t.SetConstant("separation", "HSeparator", 12);
        t.SetStylebox("scroll", "VScrollBar", Box(new Color(0, 0, 0, 0), radius: 4));
        t.SetStylebox("grabber", "VScrollBar", Box(new Color("3a3f49"), radius: 4));
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(new Color("4a505c"), radius: 4));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(Accent, radius: 4));

        // Commune table
        t.SetStylebox("panel", "Tree", Box(new Color("111317"), border: Border, radius: 4, margin: 2));
        t.SetStylebox("selected", "Tree", Box(AccentSoft, radius: 2));
        t.SetStylebox("selected_focus", "Tree", Box(AccentSoft, radius: 2));
        t.SetStylebox("title_button_normal", "Tree", Box(PanelRaised, radius: 0, margin: 6, vmargin: 4));
        t.SetStylebox("title_button_hover", "Tree", Box(new Color("2c313b"), radius: 0, margin: 6, vmargin: 4));
        t.SetStylebox("title_button_pressed", "Tree", Box(Accent, radius: 0, margin: 6, vmargin: 4));
        t.SetColor("title_button_color", "Tree", Text);
        t.SetColor("guide_color", "Tree", new Color("ffffff10"));

        t.SetConstant("separation", "VBoxContainer", 6);
        t.SetConstant("separation", "HBoxContainer", 6);
        t.SetConstant("h_separation", "HFlowContainer", 6);
        t.SetConstant("v_separation", "HFlowContainer", 6);
        return t;
    }

    private static readonly SystemFont Regular = new()
    {
        FontNames = new[] { "Helvetica Neue", "Inter", "Segoe UI", "Arial" },
        Antialiasing = TextServer.FontAntialiasing.Lcd,
    };

    /// <summary>
    /// The regular face, or synthetic bold/italic variations of it. (SystemFont's own weight and
    /// italic lookup isn't reliable across platforms; a variation of the same face always works.)
    /// </summary>
    private static Font Font(int weight = 400, bool italic = false)
    {
        if (weight <= 400 && !italic) return Regular;
        return new FontVariation
        {
            BaseFont = Regular,
            VariationEmbolden = weight > 400 ? 0.7f : 0,
            VariationTransform = italic ? new Transform2D(new Vector2(1, 0), new Vector2(0.2f, 1), Vector2.Zero) : Transform2D.Identity,
        };
    }

    /// <summary>A small caps section heading.</summary>
    public static Label SectionTitle(string text)
    {
        var l = new Label { Text = text.ToUpperInvariant() };
        l.AddThemeColorOverride("font_color", Heading);
        l.AddThemeFontSizeOverride("font_size", 11);
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

    private static StyleBoxLine Line(Color color) => new() { Color = color, Thickness = 1 };
}
