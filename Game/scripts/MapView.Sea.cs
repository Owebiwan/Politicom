using Godot;

namespace Politicom.Game;

/// <summary>
/// Water and land in the manner of Victoria 3: an animated sea (lit ripples, depth from the
/// coast, foam) drawn by a shader behind the map; land cover tinting the political colours; and,
/// zoomed out to the world, a paper chart with engraved coastlines and a parchment wash over the
/// land. Built from Natural Earth by tools/geo/build_sea.py.
/// </summary>
public partial class MapView
{
    /// <summary>How strongly land cover tints the colours (0 = off).</summary>
    public const float TerrainAlpha = 0.28f;

    private ColorRect? _sea;
    private ShaderMaterial? _seaMaterial;
    private static Texture2D? _terrain, _landMask;
    private static bool _terrainTried;

    /// <summary>The sea is drawn behind the map wherever the map isn't land (real or generated geography).</summary>
    private bool SeaShown => Geo != null && Geo.Territories.Count == 0; // a city map (Paris) is inland: it sits on land

    private void EnsureSea()
    {
        if (_sea != null) return;
        _seaMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sea.gdshader") };
        if (ResourceLoader.Exists("res://map/coast.png")) _seaMaterial.SetShaderParameter("coast", GD.Load<Texture2D>("res://map/coast.png"));
        _sea = new ColorRect { Material = _seaMaterial, ShowBehindParent = true, MouseFilter = MouseFilterEnum.Ignore };
        _sea.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_sea);
    }

    /// <summary>Kilometres per screen pixel at the current zoom (real geography only).</summary>
    private float KmPerPixel()
    {
        var a = ToScreen(_project(new Vector2(0, 0)));
        var b = ToScreen(_project(new Vector2(1, 0)));
        return 111f / Mathf.Max(0.0001f, b.X - a.X);
    }

    /// <summary>0 close in, rising to 1 as the view widens to the whole world: the paper chart.</summary>
    private float PaperBlend() => Globe == null || Generated ? 0 : Mathf.Clamp((KmPerPixel() - 9f) / 9f, 0, 1);

    /// <summary>Tells the sea shader where the map is looking.</summary>
    private void UpdateSea()
    {
        EnsureSea();
        _sea!.Visible = SeaShown;
        if (!SeaShown) return;
        var p0 = ToScreen(_project(new Vector2(0, 0)));
        float perLon = ToScreen(_project(new Vector2(1, 0))).X - p0.X;
        float perLat = ToScreen(_project(new Vector2(0, 1))).Y - p0.Y;
        var degPerPx = new Vector2(1 / perLon, 1 / perLat);
        _seaMaterial!.SetShaderParameter("deg_per_px", degPerPx);
        _seaMaterial.SetShaderParameter("origin", -p0 * degPerPx);
        _seaMaterial.SetShaderParameter("size", Size);
        _seaMaterial.SetShaderParameter("has_coast", !Generated);
        _seaMaterial.SetShaderParameter("paper", PaperBlend());
    }

    private static void LoadTerrain()
    {
        if (_terrainTried) return;
        _terrainTried = true;
        const string path = "res://map/terrain.webp";
        if (!FileAccess.FileExists(path)) return;
        var image = new Image();
        if (image.LoadWebpFromBuffer(FileAccess.GetFileAsBytes(path)) != Error.Ok) return;
        image.GenerateMipmaps();
        _terrain = ImageTexture.CreateFromImage(image);
        // The same shape in white, for washing the land with parchment at world zoom.
        var mask = (Image)image.Duplicate();
        mask.ClearMipmaps();
        for (int y = 0; y < mask.GetHeight(); y++)
            for (int x = 0; x < mask.GetWidth(); x++)
                mask.SetPixel(x, y, new Color(1, 1, 1, mask.GetPixel(x, y).A));
        mask.GenerateMipmaps();
        _landMask = ImageTexture.CreateFromImage(mask);
    }

    /// <summary>Land cover over the colours, and the parchment wash when zoomed out to the world.</summary>
    private void DrawTerrain()
    {
        if (Geo is null || Generated) return;
        LoadTerrain();
        var world = LonLatRect(-180, -90, 180, 90);
        if (_terrain != null && ShowRelief) DrawTextureRect(_terrain, world, false, new Color(1, 1, 1, TerrainAlpha));
        float paper = PaperBlend();
        if (_landMask != null && paper > 0) DrawTextureRect(_landMask, world, false, new Color(0.86f, 0.80f, 0.66f, 0.42f * paper));
    }
}
