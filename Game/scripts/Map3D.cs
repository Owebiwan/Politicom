using System.Linq;
using Godot;

namespace Politicom.Game;

/// <summary>
/// The 3D map, in the manner of Victoria 3: the flat map (<see cref="MapView"/>, drawn off-screen)
/// laid over a grid raised by the real elevation and seen through a perspective camera that looks
/// straight down at the whole world and tilts toward the horizon as you zoom in, the distance
/// fading into haze.
///
/// The flat map keeps its own size and behaviour: it is placed at the foot of a larger canvas
/// (the area a tilted camera can see) and draws beyond its edges into it. Pointer input is cast
/// onto the ground and handed to it in its own coordinates; its legend, banner and tooltips are
/// drawn flat on an overlay so they never tilt. The colour grade, vignette and falling snow are
/// laid over the screen.
/// </summary>
public partial class Map3D : Control
{
    private const float Fov = 30f;
    private const float MaxTilt = 38f;
    private const float Exaggeration = 7f;

    public MapView Map { get; }

    private readonly SubViewport _flat = new() { RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Size2DOverrideStretch = true, HandleInputLocally = false };
    private readonly SubViewport _view = new() { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, HandleInputLocally = false };
    private readonly SubViewportContainer _container = new() { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Camera3D _camera = new() { Fov = Fov, Near = 5, Far = 200000 };
    private readonly MeshInstance3D _ground = new();
    private readonly ShaderMaterial _groundMaterial = new();
    private readonly ShaderMaterial _postMaterial = new();
    private readonly Hud _hud = new();
    private Vector2 _extent, _offset;
    private Vector2 _mouse;
    private float _tilt;
    private static ImageTexture? _height;
    private static bool _heightTried;

    /// <summary>Whether the camera may tilt (the Settings switch); off, it always looks straight down.</summary>
    public bool Tilt { get; set; } = true;

    private sealed partial class Hud : Control
    {
        public Map3D Owner = null!;
        public override void _Draw() => Owner.Map.DrawUi(this, Owner._mouse);
    }

    public Map3D(MapView map)
    {
        Map = map;
        map.ExternalUi = true;
        map.Objects3D = true;
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        _flat.AddChild(Map);
        AddChild(_flat);

        _container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_container);
        _container.AddChild(_view);
        _view.AddChild(_camera);
        _groundMaterial.Shader = GD.Load<Shader>("res://shaders/terrain3d.gdshader");
        _groundMaterial.SetShaderParameter("map_tex", _flat.GetTexture());
        if (Height() is { } h) _groundMaterial.SetShaderParameter("height", h);
        _ground.MaterialOverride = _groundMaterial;
        _view.AddChild(_ground);
        var haze = new Color(0.11f, 0.15f, 0.2f);
        _view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = haze,
                TonemapMode = Godot.Environment.ToneMapper.Linear,
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.78f, 0.8f, 0.86f), AmbientLightEnergy = 0.75f,
            },
        });
        _groundMaterial.SetShaderParameter("haze", haze);
        SetUpObjects();
        SetUpTrees();
        SetUpTransport();

        // The screen's colour grade, vignette and snowfall, then the flat legend and tooltips.
        AddChild(new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport });
        _postMaterial.Shader = GD.Load<Shader>("res://shaders/grade.gdshader");
        _postMaterial.SetShaderParameter("clouds", 0f);
        var post = new ColorRect { Material = _postMaterial, MouseFilter = MouseFilterEnum.Ignore };
        post.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(post);
        _hud.Owner = this;
        _hud.MouseFilter = MouseFilterEnum.Ignore;
        _hud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_hud);

        Resized += Layout;
        Layout();
    }

    private static ImageTexture? Height()
    {
        if (_heightTried) return _height;
        _heightTried = true;
        const string path = MapView.LandDir + "height.land";
        if (!FileAccess.FileExists(path)) return null;
        var image = new Image();
        if (image.LoadWebpFromBuffer(FileAccess.GetFileAsBytes(path)) != Error.Ok) return null;
        return _height = ImageTexture.CreateFromImage(image);
    }

    // ── The camera ──────────────────────────────────────────────────────────
    // The camera looks at a point on the ground from distance D, pitched `tilt` from straight
    // down. D is chosen so that, looking straight down, the screen shows exactly the flat map.

    private float Distance => Size.Y / 2 / Mathf.Tan(Mathf.DegToRad(Fov / 2));

    /// <summary>Where the ray through a screen point (-1..1 each way, +1 at the top) meets the ground, relative to the point looked at.</summary>
    private Vector3 Hit(float sx, float sy, float tilt)
    {
        float tanV = Mathf.Tan(Mathf.DegToRad(Fov / 2)), tanH = tanV * Size.X / Mathf.Max(1, Size.Y);
        var basis = new Basis(Vector3.Right, -(Mathf.Pi / 2 - tilt));
        var dir = basis * new Vector3(sx * tanH, sy * tanV, -1);
        var from = new Vector3(0, Distance * Mathf.Cos(tilt), Distance * Mathf.Sin(tilt));
        return from + dir * (-from.Y / dir.Y);
    }

    /// <summary>
    /// Sizes the canvas to what the most tilted camera sees, and places the flat map at its foot,
    /// centred: the camera keeps the map's bottom edge at the bottom of the screen.
    /// </summary>
    private void Layout()
    {
        if (Size.X < 4 || Size.Y < 4) return;
        float max = Mathf.DegToRad(MaxTilt);
        float bottom = Hit(0, -1, max).Z, top = -Hit(0, 1, max).Z, half = Hit(1, 1, max).X;
        _extent = new Vector2(Mathf.Ceil(Mathf.Max(Size.X, 2 * half) + 8), Mathf.Ceil(Mathf.Max(Size.Y, bottom + top) + 8));
        _offset = new Vector2((_extent.X - Size.X) / 2, _extent.Y - Size.Y - 4).Floor();
        // Render the flat map sharper than the screen (the near ground is enlarged), within texture limits.
        float scale = Mathf.Min(1.6f, 6144f / Mathf.Max(_extent.X, _extent.Y));
        _flat.Size = (Vector2I)(_extent * scale).Ceil();
        _flat.Size2DOverride = (Vector2I)_extent;
        Map.Position = _offset;
        Map.Size = Size;
        Map.ExtentOverride = new Rect2(-_offset, _extent);
        _ground.Mesh = new PlaneMesh { Size = _extent, SubdivideWidth = 220, SubdivideDepth = (int)(220 * _extent.Y / _extent.X) };
        _ground.Position = new Vector3(_extent.X / 2, 0, _extent.Y / 2);
        _groundMaterial.SetShaderParameter("extent", _extent);
        _postMaterial.SetShaderParameter("size", Size);
        Map.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_extent == Vector2.Zero) return;
        // Straight down over the world; tilting toward the horizon as the view closes in.
        float target = Tilt && !Map.AtWorldZoom ? Mathf.DegToRad(MaxTilt) * Mathf.SmoothStep(0.3f, 1.0f, Map.Zoom) : 0;
        _tilt = Mathf.Lerp(_tilt, target, 1 - Mathf.Exp(-(float)delta * 8));
        float bottom = Hit(0, -1, _tilt).Z;
        var look = new Vector3(_offset.X + Size.X / 2, 0, _offset.Y + Size.Y - bottom);
        var from = look + new Vector3(0, Distance * Mathf.Cos(_tilt), Distance * Mathf.Sin(_tilt));
        _camera.GlobalTransform = new Transform3D(new Basis(Vector3.Right, -(Mathf.Pi / 2 - _tilt)), from);

        // Raised relief (real geography only), haze in the distance when tilted.
        bool relief = Map.RealGeography && Map.ShowRelief && Height() != null;
        var (origin, deg) = Map.LonLatFrame;
        _groundMaterial.SetShaderParameter("origin", origin);
        _groundMaterial.SetShaderParameter("deg_per_px", deg);
        float lift = relief ? Exaggeration / Mathf.Max(0.05f, Map.KilometresPerPixel * 1000) : 0;
        lift = Mathf.Min(lift, 0.02f);
        _groundMaterial.SetShaderParameter("lift", lift);
        SyncObjects(lift);
        SyncTrees(lift);
        SyncTransport(lift);
        _groundMaterial.SetShaderParameter("haze_near", Distance * 1.05f);
        _groundMaterial.SetShaderParameter("haze_far", Distance * 2.4f);
        _groundMaterial.SetShaderParameter("haze_amount", Mathf.Clamp(_tilt / Mathf.DegToRad(MaxTilt), 0, 1) * 0.85f);

        bool real = Map.RealGeography;
        _postMaterial.SetShaderParameter("grade", real ? 1f : 0f);
        _postMaterial.SetShaderParameter("snowfall", real ? Map.Snowfall : 0f);
        _hud.QueueRedraw();
    }

    // ── Input: cast onto the ground, handed to the flat map in its coordinates ──

    private Vector2? ToMap(Vector2 screen)
    {
        var origin = _camera.ProjectRayOrigin(screen);
        var normal = _camera.ProjectRayNormal(screen);
        if (Mathf.Abs(normal.Y) < 1e-4f) return null;
        float t = -origin.Y / normal.Y;
        if (t < 0) return null;
        var p = origin + normal * t;
        return new Vector2(p.X, p.Z) - _offset;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouse mouse)
        {
            _mouse = mouse.Position;
            if (ToMap(mouse.Position) is not { } at) return;
            var copy = (InputEventMouse)mouse.Duplicate();
            copy.Position = at;
            copy.GlobalPosition = at;
            Map.Feed(copy);
            AcceptEvent();
        }
        else if (e is InputEventGesture gesture)
        {
            if (ToMap(gesture.Position) is not { } at) return;
            var copy = (InputEventGesture)gesture.Duplicate();
            copy.Position = at;
            Map.Feed(copy);
            AcceptEvent();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) Map.ClearHover();
    }
}
