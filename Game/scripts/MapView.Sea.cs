using Godot;

namespace Politicom.Game;

/// <summary>
/// Water and land: an animated sea (lit ripples, depth from the
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
        Cover(_sea);
        _seaMaterial!.SetShaderParameter("deg_per_px", degPerPx);
        _seaMaterial.SetShaderParameter("origin", -(p0 - ViewRect.Position) * degPerPx);
        _seaMaterial.SetShaderParameter("size", ViewRect.Size);
        _seaMaterial.SetShaderParameter("has_coast", !Generated);
        _seaMaterial.SetShaderParameter("paper", PaperBlend());
    }

    private ColorRect? _grade;
    private ShaderMaterial? _gradeMaterial;

    /// <summary>The colour grade, vignette and clouds over the map (shaders/grade.gdshader), above everything drawn here.</summary>
    private void UpdateGrade()
    {
        if (_grade == null)
        {
            _gradeMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grade.gdshader") };
            _grade = new ColorRect { Material = _gradeMaterial, MouseFilter = MouseFilterEnum.Ignore };
            _grade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(_grade);
        }
        bool real = SeaShown && !Generated;
        _grade.Visible = real;
        if (!real) return;
        var p0 = ToScreen(_project(new Vector2(0, 0)));
        float perLon = ToScreen(_project(new Vector2(1, 0))).X - p0.X;
        float perLat = ToScreen(_project(new Vector2(0, 1))).Y - p0.Y;
        var degPerPx = new Vector2(1 / perLon, 1 / perLat);
        Cover(_grade);
        _gradeMaterial!.SetShaderParameter("deg_per_px", degPerPx);
        _gradeMaterial.SetShaderParameter("origin", -(p0 - ViewRect.Position) * degPerPx);
        _gradeMaterial.SetShaderParameter("size", ViewRect.Size);
        // With the 3D map the grade, vignette and snowfall belong to the screen, not the ground.
        _gradeMaterial.SetShaderParameter("grade", ExternalUi ? 0f : 1f);
        // Clouds gather as the view widens, and clear for the paper chart.
        float km = KmPerPixel();
        float clouds = Mathf.SmoothStep(4f, 9f, km) * (1 - PaperBlend());
        _gradeMaterial.SetShaderParameter("clouds", ShowRelief ? clouds : 0f);
        // Snow falls on a wintry view seen close in.
        float lat = (-p0.Y + Size.Y / 2) * degPerPx.Y; // the view's middle latitude
        float winter = lat >= 0 ? Winter(north: true) : Winter(north: false);
        float line = 66 - 12 * winter; // as the land shader's snow line
        float snowfall = ShowRelief ? winter * Mathf.SmoothStep(line - 6, line + 2, Mathf.Abs(lat)) * (1 - Mathf.SmoothStep(1.5f, 3f, km)) : 0f;
        Snowfall = snowfall;
        _gradeMaterial.SetShaderParameter("snowfall", ExternalUi ? 0f : snowfall);
    }

    /// <summary>How hard snow is falling on the view (for the 3D map's screen overlay).</summary>
    public float Snowfall { get; private set; }

    /// <summary>Stretches a full-rect child over the area the map draws into.</summary>
    private void Cover(Control c)
    {
        var r = ViewRect;
        c.OffsetLeft = r.Position.X;
        c.OffsetTop = r.Position.Y;
        c.OffsetRight = r.End.X - Size.X;
        c.OffsetBottom = r.End.Y - Size.Y;
    }

    private static void LoadTerrain()
    {
        if (_terrainTried) return;
        _terrainTried = true;
        if (Map3D.LandCover()?.Duplicate() is not Image image) return; // the file in the editor, the imported texture in an export
        image.GenerateMipmaps();
        _terrain = ImageTexture.CreateFromImage(image);
        Map3D.ReleaseLandCover();
        // The same shape in white, for washing the land with parchment at world zoom.
        var mask = (Image)image.Duplicate();
        mask.ClearMipmaps();
        for (int y = 0; y < mask.GetHeight(); y++)
            for (int x = 0; x < mask.GetWidth(); x++)
                mask.SetPixel(x, y, new Color(1, 1, 1, mask.GetPixel(x, y).A));
        mask.GenerateMipmaps();
        _landMask = ImageTexture.CreateFromImage(mask);
        image.Dispose();
        mask.Dispose();
    }

    /// <summary>Land cover over the colours, and the parchment wash when zoomed out to the world.</summary>
    private void DrawTerrain()
    {
        if (Geo is null || Generated) return;
        if (!TerrainShown) LoadTerrain();
        var world = LonLatRect(-180, -90, 180, 90);
        if (TerrainShown) return; // the land layer draws the ground and the paper chart itself
        if (_terrain != null && ShowRelief) DrawTextureRect(_terrain, world, false, new Color(1, 1, 1, TerrainAlpha));
        float paper = PaperBlend();
        if (_landMask != null && paper > 0) DrawTextureRect(_landMask, world, false, new Color(0.86f, 0.80f, 0.66f, 0.42f * paper));
    }
}
