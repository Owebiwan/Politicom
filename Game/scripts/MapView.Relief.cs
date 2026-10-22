using System.Collections.Generic;
using Godot;

namespace Politicom.Game;

/// <summary>
/// Shaded relief over the land colours: mountains in shadow and light, flat ground and sea
/// untouched. From Natural Earth's shaded relief (tools/geo/build_relief.py): a world overview
/// for zoomed-out views, and 30° tiles, loaded as needed, for closer ones.
/// </summary>
public partial class MapView
{
    public bool ShowRelief { get; set; } = true;

    private const string ReliefDir = "res://data/relief/";
    private const int OverviewWidth = 5400, TileWidth = 900, TileDeg = 30;
    private static readonly Color ReliefTint = new(1, 1, 1, 0.8f);
    private static ImageTexture? _reliefWorld;
    private static bool _reliefWorldTried;
    private static readonly Dictionary<(int Col, int Row), ImageTexture?> ReliefTiles = new();

    private static ImageTexture? LoadShade(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        var image = new Image();
        if (image.LoadPngFromBuffer(FileAccess.GetFileAsBytes(path)) != Error.Ok) return null;
        image.GenerateMipmaps(); // smooth when shrunk
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Screen rectangle of a longitude/latitude box (the projection is affine).</summary>
    private Rect2 LonLatRect(float west, float south, float east, float north)
    {
        var a = ToScreen(_project(new Vector2(west, north)));
        var b = ToScreen(_project(new Vector2(east, south)));
        return new Rect2(a, b - a);
    }

    /// <summary>Real geography only: a generated region's invented coasts have no relief.</summary>
    private void DrawRelief()
    {
        if (!ShowRelief || Geo is null || Generated) return;
        var view = new Rect2(Vector2.Zero, Size);
        var world = LonLatRect(-180, -90, 180, 90);
        // The overview while each of its pixels covers no more than about one and a half on screen; tiles closer in.
        if (world.Size.X / OverviewWidth <= 1.5f)
        {
            if (!_reliefWorldTried)
            {
                _reliefWorld = LoadShade(ReliefDir + "world.shade");
                _reliefWorldTried = true;
            }
            if (_reliefWorld != null) DrawTextureRect(_reliefWorld, world, false, ReliefTint);
            return;
        }
        for (int row = 0; row < 180 / TileDeg; row++)
            for (int col = 0; col < 360 / TileDeg; col++)
            {
                float west = -180 + col * TileDeg, north = 90 - row * TileDeg;
                var rect = LonLatRect(west, north - TileDeg, west + TileDeg, north);
                if (!rect.Intersects(view)) continue;
                if (!ReliefTiles.TryGetValue((col, row), out var tile))
                    ReliefTiles[(col, row)] = tile = LoadShade($"{ReliefDir}t_{col}_{row}.shade"); // missing = open sea
                // Each tile carries a one-pixel border from its neighbours: draw only the inside, so edges blend.
                if (tile != null) DrawTextureRectRegion(tile, rect, new Rect2(1, 1, TileWidth, TileWidth), ReliefTint);
            }
    }
}
