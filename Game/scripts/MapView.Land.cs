using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Politicom.Game;

/// <summary>
/// The land layer: the world's countries and the region's communes drawn over the real ground
/// (Data/land, from tools/geo/build_land.py) by shaders/land.gdshader, each colour a tint
/// that keeps the terrain's light and shade, as on Victoria 3's map. The layer sits behind the
/// map (over the sea) and holds its shapes in layout space; panning and zooming only move and
/// scale it, so its drawing is recorded again only when colours change.
/// </summary>
public partial class MapView
{
    internal const string LandDir = "res://data/land/";
    /// <summary>How strongly countries' and communes' colours tint the ground.</summary>
    private const float StateTint = 0.5f, CommuneTint = 0.62f;

    private sealed partial class LandLayer : Node2D
    {
        public MapView Map = null!;
        public override void _Draw() => Map.DrawLand(this);
    }

    private LandLayer? _land;
    private ShaderMaterial? _landMaterial;
    private static ImageTexture? _overview;
    private static bool _overviewTried;
    private ImageTexture? _detail;
    private object? _detailFor;

    /// <summary>The land layer draws the world and the communes (whenever there's a world map).</summary>
    private bool LandActive => _land != null && Globe_Layout() != null;

    /// <summary>
    /// The real ground is shown under real geography with relief on. Generated maps keep flat
    /// colours, and so do city maps (Paris): at two kilometres a pixel the ground is only a blur there.
    /// </summary>
    private bool TerrainShown => ShowRelief && !Generated && SeaShown && Overview() != null;

    private void EnsureLand()
    {
        if (_land != null) return;
        _landMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/land.gdshader") };
        _land = new LandLayer { Map = this, ShowBehindParent = true, Material = _landMaterial };
        AddChild(_land); // after the sea, so over it
    }

    private static Image? LoadLandImage(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        var image = new Image();
        return image.LoadWebpFromBuffer(FileAccess.GetFileAsBytes(path)) == Error.Ok ? image : null;
    }

    private static ImageTexture? Overview()
    {
        if (_overviewTried) return _overview;
        _overviewTried = true;
        if (LoadLandImage(LandDir + "world.land") is not { } image) return null;
        image.GenerateMipmaps();
        return _overview = ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Full-resolution ground around the region: the 30° tiles covering it and its surroundings
    /// (at most four by three), joined into one texture. Areas without a tile (open sea) are left
    /// transparent, so the shader falls back on the world overview there.
    /// </summary>
    private (ImageTexture Texture, Vector4 Rect)? DetailTexture()
    {
        if (Geo is null || Generated) return null;
        if (ReferenceEquals(_detailFor, Geo) && _detail != null) return (_detail, _detailRect);
        _detailFor = Geo;
        _detail = null;
        var b = Geo.Bounds; // lon/lat
        float cx = b.GetCenter().X, cy = b.GetCenter().Y;
        int c0 = Mathf.Clamp((int)Mathf.Floor((Mathf.Max(b.Position.X - 20, cx - 55) + 180) / 30), 0, 11);
        int c1 = Mathf.Clamp((int)Mathf.Floor((Mathf.Min(b.End.X + 20, cx + 55) + 180) / 30), 0, 11);
        int r0 = Mathf.Clamp((int)Mathf.Floor((90 - Mathf.Min(b.End.Y + 14, cy + 40)) / 30), 0, 5);
        int r1 = Mathf.Clamp((int)Mathf.Floor((90 - Mathf.Max(b.Position.Y - 14, cy - 40)) / 30), 0, 5);
        const int tile = 1800;
        var image = Image.CreateEmpty((c1 - c0 + 1) * tile, (r1 - r0 + 1) * tile, false, Image.Format.Rgba8);
        bool any = false;
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                if (LoadLandImage($"{LandDir}t_{c}_{r}.land") is not { } t) continue;
                t.Convert(Image.Format.Rgba8);
                image.BlitRect(t, new Rect2I(0, 0, tile, tile), new Vector2I((c - c0) * tile, (r - r0) * tile));
                any = true;
            }
        if (!any) return null;
        image.GenerateMipmaps();
        _detail = ImageTexture.CreateFromImage(image);
        _detailRect = new Vector4(-180 + c0 * 30, 90 - r0 * 30, -180 + (c1 + 1) * 30, 90 - (r1 + 1) * 30);
        return (_detail, _detailRect);
    }

    private Vector4 _detailRect;

    /// <summary>
    /// Set while the map draws; the land layer is redrawn from <see cref="_Process"/>, since a
    /// redraw asked for during another item's drawing is dropped. (Its placement, set while
    /// drawing, applies at once.)
    /// </summary>
    private bool _landDirty;

    private void RedrawLand()
    {
        if (!_landDirty || _land == null) return;
        _landDirty = false;
        _land.QueueRedraw();
    }

    /// <summary>Places the land layer under the view and tells its shader where the ground is.</summary>
    private void UpdateLand()
    {
        EnsureLand();
        _land!.Visible = LandActive;
        if (!LandActive) return;
        float s = _fitScale * _zoom;
        _land.Position = (_fitOffset - Size / 2) * _zoom + Size / 2 + _pan;
        _land.Scale = new Vector2(s, s);
        bool terrain = TerrainShown;
        _landMaterial!.SetShaderParameter("terrain", terrain);
        _landMaterial.SetShaderParameter("paper", PaperBlend());
        if (!terrain) return;
        _landMaterial.SetShaderParameter("overview", Overview());
        // Layout → lon/lat: the projection is affine.
        var l0 = _project(Vector2.Zero);
        float perLon = _project(new Vector2(1, 0)).X - l0.X, perLat = _project(new Vector2(0, 1)).Y - l0.Y;
        _landMaterial.SetShaderParameter("ll_per_unit", new Vector2(1 / perLon, 1 / perLat));
        _landMaterial.SetShaderParameter("ll_origin", new Vector2(-l0.X / perLon, -l0.Y / perLat));
        var detail = DetailTexture();
        _landMaterial.SetShaderParameter("has_detail", detail != null);
        if (detail is { } d)
        {
            _landMaterial.SetShaderParameter("detail", d.Texture);
            _landMaterial.SetShaderParameter("detail_rect", d.Rect);
        }
        _landDirty = true; // colours follow the turn, the map mode and the hover
    }

    /// <summary>The land layer's drawing, in layout space: the world's countries, then the region's communes.</summary>
    private void DrawLand(CanvasItem target)
    {
        if (Globe_Layout() is not { } g || World is not { } w) return;
        if (_meshVerts == null) BuildGlobeMesh(g);
        ColorGlobeMesh();
        target.DrawMesh(_globeMesh!, null);
        bool terrain = TerrainShown;
        if (Generated && _geoLayout is { } g0)
            foreach (var land in g0.LandFill) Fill(target, land, NeutralLand);
        var cells = Cells();
        foreach (var c in w.Communes)
        {
            var fill = Fill(w, c);
            if (c.Id == _hover) fill = fill.Lightened(0.12f);
            fill.A = terrain ? CommuneTint * fill.A : 1;
            foreach (var piece in cells[c.Id]) Fill(target, piece, fill);
        }
    }

    private static void Fill(CanvasItem target, Vector2[] poly, Color color)
    {
        if (poly.Length < 3 || Geometry2D.TriangulatePolygon(poly).Length == 0) return;
        target.DrawColoredPolygon(poly, color);
    }
}
