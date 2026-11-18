using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim.Global;

namespace Politicom.Game;

/// <summary>
/// A world map of one era for choosing where the movement rises: states you can play are in
/// their colours, the rest dimmed. Hover names a country, click chooses it; scroll or pinch
/// zooms and dragging pans, as on the main map.
/// </summary>
public partial class StatePicker : Control
{
    private static readonly Color Sea = new("16202b");
    private static readonly Color Coast = new("3d5670");
    private static readonly Color Border = new("8a8f96");
    private static readonly Color Unplayable = new("33373c");

    private Atlas? _atlas;
    private HashSet<string> _playable = new();
    private readonly List<(AtlasState State, ArrayMesh Mesh, Rect2 Box, Vector2[][] Rings)> _states = new();
    private ImageTexture? _relief;
    private ShaderMaterial? _seaMaterial;
    private ArrayMesh? _land;
    private float _zoom = 1;
    private Vector2 _pan;
    private Vector2? _dragFrom;
    private bool _dragged;

    public string? Hovered { get; private set; }
    public string? Selected { get; private set; }

    /// <summary>Raised when the pointer moves onto a country (or off, with null).</summary>
    public event Action<string?>? StateHovered;
    /// <summary>Raised when a playable country is clicked.</summary>
    public event Action<string>? StateChosen;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        _relief = MapView.LoadShade(MapView.ReliefDir + "world.shade");
        // The same animated sea as the main map, behind everything.
        _seaMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sea.gdshader") };
        if (ResourceLoader.Exists("res://map/coast.png")) _seaMaterial.SetShaderParameter("coast", GD.Load<Texture2D>("res://map/coast.png"));
        var sea = new ColorRect { Material = _seaMaterial, ShowBehindParent = true, MouseFilter = MouseFilterEnum.Ignore };
        sea.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(sea);
    }

    /// <summary>Shows an era's map; keeps the selection if that state exists in it.</summary>
    public void SetAtlas(Atlas atlas, IEnumerable<string> playable)
    {
        _atlas = atlas;
        _playable = playable.ToHashSet();
        _states.Clear();
        _land = Triangulate(atlas.Land.Select(r => r.Select(p => new Vector2((float)p[0], (float)p[1])).ToArray()));
        foreach (var s in atlas.States)
        {
            var rings = s.Rings.Select(r => r.Select(p => new Vector2((float)p[0], (float)p[1])).ToArray()).ToArray();
            if (Triangulate(rings) is not { } mesh) continue;
            var all = rings.SelectMany(r => r).ToArray();
            var min = new Vector2(all.Min(p => p.X), all.Min(p => p.Y));
            var max = new Vector2(all.Max(p => p.X), all.Max(p => p.Y));
            _states.Add((s, mesh, new Rect2(min, max - min), rings));
        }
        if (Selected != null && atlas.State(Selected) == null) Selected = null;
        QueueRedraw();
    }

    /// <summary>One white mesh for a set of outlines (modulated to any colour when drawn); null if none triangulate.</summary>
    private static ArrayMesh? Triangulate(IEnumerable<Vector2[]> rings)
    {
        var verts = new List<Vector2>();
        foreach (var ring in rings)
        {
            if (ring.Length < 3) continue;
            var tri = Geometry2D.TriangulatePolygon(ring);
            if (tri.Length > 0)
            {
                verts.AddRange(tri.Select(i => ring[i]));
                continue;
            }
            // Simplified coasts can cross themselves; clipping against a box returns simple polygons.
            var min = new Vector2(ring.Min(p => p.X), ring.Min(p => p.Y)) - Vector2.One;
            var max = new Vector2(ring.Max(p => p.X), ring.Max(p => p.Y)) + Vector2.One;
            foreach (var piece in Geometry2D.IntersectPolygons(ring, new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) }))
            {
                var t = Geometry2D.TriangulatePolygon(piece);
                verts.AddRange(t.Select(i => piece[i]));
            }
        }
        if (verts.Count == 0) return null;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = Enumerable.Repeat(Colors.White, verts.Count).ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    public void Select(string? id)
    {
        Selected = id;
        QueueRedraw();
    }

    // ── Projection: equirectangular, the world (to 60° S) fitted to the control, then zoom and pan ──
    private const float South = -60, North = 84;

    private float BaseScale => Mathf.Min(Size.X / 360f, Size.Y / (North - South));
    private new float Scale => BaseScale * _zoom;
    private Vector2 Origin => Size / 2 + _pan + new Vector2(0, (North + South) / 2 * Scale);

    private Vector2 ToScreen(Vector2 lonLat) => Origin + new Vector2(lonLat.X, -lonLat.Y) * Scale;
    private Vector2 ToLonLat(Vector2 screen) => (screen - Origin) / Scale * new Vector2(1, -1);

    private void Clamp()
    {
        _zoom = Mathf.Clamp(_zoom, 1, 12);
        // Keep the map covering the view: no edge may come inside the pane.
        var half = new Vector2(180 * Scale, (North - South) / 2 * Scale);
        var room = new Vector2(Mathf.Max(half.X - Size.X / 2, 0), Mathf.Max(half.Y - Size.Y / 2, 0));
        _pan = new Vector2(Mathf.Clamp(_pan.X, -room.X, room.X), Mathf.Clamp(_pan.Y, -room.Y, room.Y));
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                ZoomAt(wheel.Position, wheel.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f);
                AcceptEvent();
                break;
            case InputEventMagnifyGesture pinch:
                ZoomAt(pinch.Position, pinch.Factor);
                AcceptEvent();
                break;
            case InputEventPanGesture pan:
                ZoomAt(pan.Position, Mathf.Exp(-pan.Delta.Y * 0.08f));
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                if (click.Pressed)
                {
                    _dragFrom = click.Position;
                    _dragged = false;
                }
                else
                {
                    if (!_dragged && Hit(click.Position) is { } id && _playable.Contains(id))
                    {
                        Select(id);
                        StateChosen?.Invoke(id);
                    }
                    _dragFrom = null;
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion motion:
                if (_dragFrom is { } from && (motion.ButtonMask & MouseButtonMask.Left) != 0)
                {
                    if (!_dragged && from.DistanceTo(motion.Position) < 4) break;
                    _dragged = true;
                    _pan += motion.Relative;
                    Clamp();
                    QueueRedraw();
                    break;
                }
                var hit = Hit(motion.Position);
                if (hit != Hovered)
                {
                    Hovered = hit;
                    StateHovered?.Invoke(hit);
                    MouseDefaultCursorShape = hit != null && _playable.Contains(hit) ? CursorShape.PointingHand : CursorShape.Arrow;
                    QueueRedraw();
                }
                break;
        }
    }

    private void ZoomAt(Vector2 at, float factor)
    {
        var before = ToLonLat(at);
        _zoom *= factor;
        Clamp();
        _pan += at - ToScreen(before);
        Clamp();
        QueueRedraw();
    }

    /// <summary>Zooms to a state, so a chosen country can be seen.</summary>
    public void FocusOn(string id)
    {
        var entry = _states.FirstOrDefault(x => x.State.Id == id);
        if (entry.State is null) return;
        var box = entry.Rings[0];
        var min = new Vector2(box.Min(p => p.X), box.Min(p => p.Y));
        var max = new Vector2(box.Max(p => p.X), box.Max(p => p.Y));
        float span = Mathf.Max(max.X - min.X, (max.Y - min.Y) * Size.X / Mathf.Max(Size.Y, 1));
        _zoom = 360f / Mathf.Max(span * 3, 1) * (Size.X / 360f) / BaseScale;
        Clamp();
        _pan = Vector2.Zero;
        _pan += Size / 2 - ToScreen((min + max) / 2);
        Clamp();
        QueueRedraw();
    }

    private string? Hit(Vector2 screen)
    {
        var p = ToLonLat(screen);
        foreach (var (state, _, box, rings) in _states)
            if (box.Grow(0.01f).HasPoint(p) && rings.Any(r => Geometry2D.IsPointInPolygon(p, r)))
                return state.Id;
        return null;
    }

    public override void _Draw()
    {
        if (_seaMaterial != null)
        {
            var degPerPx = new Vector2(1 / Scale, -1 / Scale);
            _seaMaterial.SetShaderParameter("deg_per_px", degPerPx);
            _seaMaterial.SetShaderParameter("origin", -Origin * degPerPx);
            _seaMaterial.SetShaderParameter("size", Size);
            _seaMaterial.SetShaderParameter("paper", 0f);
        }
        else DrawRect(new Rect2(Vector2.Zero, Size), Sea);
        if (_atlas is null) return;
        var font = ThemeDB.FallbackFont;
        var xf = new Transform2D(new Vector2(Scale, 0), new Vector2(0, -Scale), Origin);

        if (_land != null) DrawMesh(_land, null, xf, Unplayable.Darkened(0.2f));
        foreach (var (state, mesh, _, _) in _states)
        {
            bool playable = _playable.Contains(state.Id);
            var color = playable ? Color.FromString(state.Color, Unplayable) : Unplayable;
            if (state.Id == Selected) color = new Color("c4473a");
            else if (state.Id == Hovered && playable) color = color.Lightened(0.3f);
            DrawMesh(mesh, null, xf, color);
        }
        if (_relief != null) DrawTextureRect(_relief, new Rect2(ToScreen(new Vector2(-180, 90)), new Vector2(360, 180) * Scale), false, new Color(1, 1, 1, 0.8f));
        foreach (var ring in _atlas.Land)
        {
            var pts = ring.Select(p => ToScreen(new Vector2((float)p[0], (float)p[1]))).ToArray();
            DrawPolyline(pts.Append(pts[0]).ToArray(), Coast, 1);
        }
        foreach (var line in _atlas.Borders)
            DrawPolyline(line.Select(p => ToScreen(new Vector2((float)p[0], (float)p[1]))).ToArray(), Border, 1);

        // Names of the playable states that have room for them, more as you zoom in; never overlapping.
        var taken = new List<Rect2>();
        foreach (var (state, _, box, rings) in _states.OrderByDescending(x => x.State.Id == Selected || x.State.Id == Hovered).ThenByDescending(x => x.Box.Size.X))
        {
            if (!_playable.Contains(state.Id) && state.Id != Selected) continue;
            bool focus = state.Id == Selected || state.Id == Hovered;
            float width = box.Size.X * Scale;
            var anchor = state.Capital is { } c && box.HasPoint(new Vector2((float)c.Lon, (float)c.Lat))
                ? new Vector2((float)c.Lon, (float)c.Lat)
                : rings[0].Aggregate(Vector2.Zero, (a, b) => a + b) / rings[0].Length;
            var at = ToScreen(anchor);
            string name = state.Name.ToUpperInvariant();
            int size = state.Id == Selected || state.Id == Hovered ? 13 : 11;
            var textSize = font.GetStringSize(name, HorizontalAlignment.Left, -1, size);
            if (!focus && textSize.X > width * 0.9f) continue; // no room
            var pos = at - new Vector2(textSize.X / 2, -4);
            var rect = new Rect2(pos - new Vector2(4, textSize.Y), textSize + new Vector2(8, 4));
            if (!focus && taken.Any(r => r.Intersects(rect))) continue;
            taken.Add(rect);
            DrawStringOutline(font, pos, name, HorizontalAlignment.Left, -1, size, 4, new Color(0, 0, 0, 0.75f));
            DrawString(font, pos, name, HorizontalAlignment.Left, -1, size, Colors.White);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            Clamp();
            QueueRedraw();
        }
    }
}
