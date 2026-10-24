using Godot;

namespace Politicom.Game;

/// <summary>
/// The interface's motion, in the manner of Victoria 3: windows ease in over a blurred map and
/// fade away, notices slide in, buttons glow under the pointer, tabs fade their pages in.
/// Everything is short (a fifth of a second or so), so it never slows play.
/// </summary>
public static class Motion
{
    private static Shader? _backdrop;

    /// <summary>
    /// A full-screen backdrop for a window: the map behind it blurred and darkened, easing in.
    /// It takes a fresh copy of the screen first (otherwise it would reuse the copy the map's
    /// grade made before the dashboard was drawn). Add the window's content to it.
    /// </summary>
    public static Control Backdrop(float darken = 0.55f)
    {
        _backdrop ??= GD.Load<Shader>("res://shaders/backdrop.gdshader");
        var material = new ShaderMaterial { Shader = _backdrop };
        material.SetShaderParameter("amount", 0f);
        material.SetShaderParameter("darken", darken);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport });
        var blur = new ColorRect { Material = material, MouseFilter = Control.MouseFilterEnum.Ignore };
        blur.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(blur);
        root.Ready += () => root.CreateTween().TweenMethod(Callable.From<float>(a => material.SetShaderParameter("amount", a)), 0f, 1f, 0.22f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        return root;
    }

    /// <summary>A window's panel: a soft shadow under it, and it eases in (a little larger, fading up).</summary>
    public static Control PopIn(Control panel)
    {
        var shadow = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        shadow.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0), ShadowColor = new Color(0, 0, 0, 0.55f), ShadowSize = 30, ShadowOffset = new Vector2(0, 10),
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        });
        shadow.AddChild(panel);
        shadow.Modulate = new Color(1, 1, 1, 0);
        shadow.Resized += () => shadow.PivotOffset = shadow.Size / 2;
        shadow.Ready += () =>
        {
            shadow.Scale = new Vector2(1.035f, 1.035f);
            var t = shadow.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            t.TweenProperty(shadow, "modulate:a", 1f, 0.2f);
            t.TweenProperty(shadow, "scale", Vector2.One, 0.24f);
        };
        return shadow;
    }

    /// <summary>Fades a control out and frees it; it stops taking input at once.</summary>
    public static void FadeOutAndFree(Control? c, float seconds = 0.14f)
    {
        if (c is null || !GodotObject.IsInstanceValid(c)) return;
        c.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var child in c.FindChildren("*", "Control", true, false))
            if (child is Control cc) cc.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (!c.IsInsideTree())
        {
            c.QueueFree();
            return;
        }
        var t = c.CreateTween();
        t.TweenProperty(c, "modulate:a", 0f, seconds);
        t.TweenCallback(Callable.From(c.QueueFree));
    }

    /// <summary>A notice eases in from the right: it fades up and grows from its right edge.</summary>
    public static void SlideIn(Control c)
    {
        c.Modulate = new Color(1, 1, 1, 0);
        c.Scale = new Vector2(0.9f, 0.9f);
        c.Resized += () => c.PivotOffset = new Vector2(c.Size.X, c.Size.Y / 2);
        c.Ready += () =>
        {
            var t = c.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            t.TweenProperty(c, "modulate:a", 1f, 0.22f);
            t.TweenProperty(c, "scale", Vector2.One, 0.26f);
        };
    }

    /// <summary>A page fading in (a tab just chosen).</summary>
    public static void FadeIn(Control c, float seconds = 0.16f)
    {
        c.Modulate = new Color(1, 1, 1, 0);
        c.CreateTween().TweenProperty(c, "modulate:a", 1f, seconds);
    }

    /// <summary>Every button glows a little under the pointer.</summary>
    public static void Install(SceneTree tree)
    {
        tree.NodeAdded += node =>
        {
            if (node is not BaseButton button || node is CheckBox || node is CheckButton) return;
            Tween? glow = null;
            void To(Color c, float seconds)
            {
                glow?.Kill();
                glow = button.CreateTween();
                glow.TweenProperty(button, "self_modulate", c, seconds);
            }
            button.MouseEntered += () => { if (!button.Disabled) To(new Color(1.18f, 1.12f, 0.96f), 0.1f); };
            button.MouseExited += () => To(Colors.White, 0.18f);
        };
    }
}
