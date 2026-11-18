using System;
using System.Collections.Generic;
using Godot;

namespace Politicom.Game;

/// <summary>
/// Forests on the 3D map, trees across the wooded land: wherever Natural
/// Earth's land cover marks forest, around the region and its neighbours, clumps of trees stand
/// on the raised terrain, of the climate: spruce, pine and birch in the north and on high ground;
/// oak, elm, birch and pine in temperate lands; oak, umbrella pine and cypress round the
/// Mediterranean; palms and evergreen broadleaf in the tropics (tools/models/build_trees.py).
/// They follow the seasons: fresh green in spring, gold and brown in autumn, the grey-brown of
/// bare twigs in winter, snow on the conifers. Placed once in longitude and latitude and drawn as two
/// multimeshes per kind (trunks; leaves, coloured per tree), moved with the view.
/// </summary>
public partial class Map3D
{
    private enum TreeKind { Oak, Elm, Birch, Pine, Spruce, Cypress, Palm }
    private static readonly string[] TreeModels = { "oak", "elm", "birch", "pine", "spruce", "cypress", "palm" };
    private static bool Deciduous(TreeKind k) => k is TreeKind.Oak or TreeKind.Elm or TreeKind.Birch;

    private sealed record Tree(Vector2 LonLat, float Size, float Turn, Color Shade);

    private readonly MultiMeshInstance3D[] _trunks = new MultiMeshInstance3D[TreeModels.Length];
    private readonly MultiMeshInstance3D[] _leaves = new MultiMeshInstance3D[TreeModels.Length];
    private readonly StandardMaterial3D[] _leafMaterials = new StandardMaterial3D[TreeModels.Length];
    private List<Tree>[]? _trees;
    private object? _treesKey;
    private object? _treesFor;

    private void SetUpTrees()
    {
        for (int k = 0; k < TreeModels.Length; k++)
        {
            Mesh? trunk = null, leaves = null;
            if (Scene($"trees/{TreeModels[k]}")?.Instantiate() is Node3D node)
            {
                foreach (var child in node.FindChildren("*", "MeshInstance3D", true, false))
                    if (child is MeshInstance3D mi)
                    {
                        if (mi.Name == "Leaves") leaves = mi.Mesh;
                        else trunk = mi.Mesh;
                    }
                node.Free();
            }
            _leafMaterials[k] = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, Roughness = 1, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            _trunks[k] = Forest(trunk, BuildingMaterial(Colors.White), colors: false);
            _leaves[k] = Forest(leaves, _leafMaterials[k], colors: true);
        }
    }

    private MultiMeshInstance3D Forest(Mesh? mesh, Material material, bool colors)
    {
        var forest = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colors, Mesh = mesh },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        _objects.AddChild(forest);
        return forest;
    }

    /// <summary>
    /// The season's foliage for a kind, at the region's latitude (southern seasons turned by six
    /// months, none in the tropics), and whether its crown is shown (always, for now).
    /// </summary>
    private static (Color Leaves, bool Shown) Foliage(TreeKind kind, int month, float lat)
    {
        var summer = kind switch
        {
            TreeKind.Oak => new Color("4e6b33"),
            TreeKind.Elm => new Color("47653a"),
            TreeKind.Birch => new Color("6a8a3e"),
            TreeKind.Pine => new Color("35503a"),
            TreeKind.Spruce => new Color("2c4632"),
            TreeKind.Cypress => new Color("2e4630"),
            _ => new Color("5a7a36"),
        };
        if (Mathf.Abs(lat) < 23) return (summer, true);
        if (lat < 0) month = (month + 5) % 12 + 1;
        float winter = month switch { 1 => 1f, 2 => 0.85f, 12 => 0.7f, 3 => 0.35f, 11 => 0.25f, _ => 0f };
        if (!Deciduous(kind))
        {
            // Snow lies on the conifers where the land shader lays it.
            float line = 66 - 12 * winter;
            float snow = winter * Mathf.SmoothStep(line - 10, line + 2, Mathf.Abs(lat));
            return (summer.Lerp(new Color(0.9f, 0.92f, 0.95f), snow * 0.55f), true);
        }
        return month switch
        {
            12 or 1 or 2 => (new Color("5d5248"), true), // bare: a grey-brown haze of twigs, as winter woods look from afar
            3 => (new Color("7c9a4a"), true),
            4 or 5 => (summer.Lightened(0.15f), true),
            10 => (kind == TreeKind.Birch ? new Color("c9a23a") : kind == TreeKind.Elm ? new Color("a08530") : new Color("a8682c"), true),
            11 => (new Color("7a4a2a"), true),
            _ => (summer, true),
        };
    }

    private static Image? _landCover;

    /// <summary>Natural Earth's land cover (Game/map/terrain.webp): forest, grassland, cropland, desert, tundra, ice.</summary>
    /// <summary>Frees the land cover image (32 MB) when nothing needs it until the next map.</summary>
    internal static void ReleaseLandCover()
    {
        _landCover?.Dispose();
        _landCover = null;
    }

    internal static Image? LandCover()
    {
        if (_landCover != null) return _landCover;
        const string path = "res://map/terrain.webp";
        // In the editor the file itself is there; an export has only the imported texture.
        if (FileAccess.FileExists(path))
        {
            var raw = new Image();
            if (raw.LoadWebpFromBuffer(FileAccess.GetFileAsBytes(path)) == Error.Ok) return _landCover = raw;
        }
        if (!ResourceLoader.Exists(path) || GD.Load<Texture2D>(path)?.GetImage() is not { } image) return null;
        if (image.IsCompressed()) image.Decompress();
        image.ClearMipmaps();
        return _landCover = image;
    }

    /// <summary>Scatters the trees over woodland around the region (once per map).</summary>
    private void PlantForests()
    {
        if (ReferenceEquals(_treesFor, Map.Geo) && _trees != null) return;
        _treesFor = Map.Geo;
        _trees = new List<Tree>[TreeModels.Length];
        for (int k = 0; k < _trees.Length; k++) _trees[k] = new List<Tree>();
        if (Map.Geo is not { } geo || LandCover() is not { } ground || _heightImage is null) return;
        var b = geo.Bounds;
        float margin = Mathf.Max(b.Size.X, b.Size.Y) * 0.6f + 2;
        float west = b.Position.X - margin, east = b.End.X + margin, south = b.Position.Y - margin, north = b.End.Y + margin;
        // A tree every few kilometres where it's thickly wooded; the grid shrinks with the region.
        float step = Mathf.Clamp(Mathf.Max(east - west, north - south) / 260f, 0.02f, 0.12f);
        var rng = new Random(7);
        int gw = ground.GetWidth(), gh = ground.GetHeight(), hw = _heightImage.GetWidth(), hh = _heightImage.GetHeight();
        for (float lat = south; lat < north; lat += step)
            for (float lon = west; lon < east; lon += step)
            {
                float x = lon + (float)(rng.NextDouble() - 0.5) * step, y = lat + (float)(rng.NextDouble() - 0.5) * step;
                if (Mathf.Abs(y) > 72) continue;
                float h = _heightImage.GetPixel((int)Mathf.PosMod((x + 180) / 360 * hw, hw), Mathf.Clamp((int)((90 - y) / 180 * hh), 0, hh - 1)).R;
                if (h <= 0) continue; // sea
                var c = ground.GetPixel((int)Mathf.PosMod((x + 180) / 360 * gw, gw), Mathf.Clamp((int)((90 - y) / 180 * gh), 0, gh - 1));
                // Woodland in Natural Earth's land cover is a sage green, clearly greener than it is red;
                // cropland, steppe and desert are cream (summer fields would fool the satellite picture).
                if (c.A < 0.5f) continue;
                float wood = Mathf.Clamp((c.G - c.R - 0.03f) / 0.07f, 0, 1);
                if (rng.NextDouble() > wood) continue;
                var kind = Climate(Mathf.Abs(y), h, rng);
                // A clump: a few trees close together, as woods are drawn on a map.
                int clump = 2 + rng.Next(3);
                for (int t = 0; t < clump; t++)
                {
                    var at = new Vector2(x, y) + new Vector2((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * step * 0.6f;
                    // Each tree a little lighter or darker, warmer or cooler than its neighbours.
                    float light = 0.82f + (float)rng.NextDouble() * 0.3f, warm = (float)(rng.NextDouble() - 0.5) * 0.12f;
                    var shade = new Color(light * (1 + warm), light, light * (1 - warm));
                    _trees[(int)kind].Add(new Tree(at, 0.75f + (float)rng.NextDouble() * 0.55f, (float)rng.NextDouble() * Mathf.Tau, shade));
                }
            }
        ReleaseLandCover();
    }

    /// <summary>Which tree grows here, by latitude and height (0..1 of Everest).</summary>
    private static TreeKind Climate(float lat, float h, Random rng)
    {
        double r = rng.NextDouble();
        if (lat < 23) return r < 0.6 ? TreeKind.Palm : TreeKind.Elm;
        if (lat > 55 || h > 0.16f) return r < 0.45 ? TreeKind.Spruce : r < 0.8 ? TreeKind.Pine : TreeKind.Birch;
        if (lat < 42) return r < 0.5 ? TreeKind.Oak : r < 0.75 ? TreeKind.Pine : TreeKind.Cypress;
        if (lat > 50) return r < 0.35 ? TreeKind.Birch : r < 0.6 ? TreeKind.Pine : r < 0.8 ? TreeKind.Spruce : TreeKind.Oak;
        return r < 0.45 ? TreeKind.Oak : r < 0.75 ? TreeKind.Elm : r < 0.88 ? TreeKind.Birch : TreeKind.Pine;
    }

    private void SyncTrees(float lift)
    {
        bool shown = Map.ObjectsShown && Map.RealGeography && Map.ShowRelief;
        foreach (var f in _trunks) f.Visible = shown;
        foreach (var f in _leaves) f.Visible = shown;
        if (!shown) return;
        PlantForests();
        var (origin, deg) = Map.LonLatFrame;
        // The season, at the middle of the region.
        float regionLat = Map.Geo is { } g ? g.Bounds.GetCenter().Y : 45;
        int month = Map.World?.Date.Month ?? 6;
        for (int k = 0; k < TreeModels.Length; k++)
        {
            var (leaves, leafy) = Foliage((TreeKind)k, month, regionLat);
            _leafMaterials[k].AlbedoColor = leaves;
            _leaves[k].Visible = leafy;
        }
        // Only when the view, the relief or the towns change: not every frame.
        var key = (origin, deg, lift, _offset, Map.Zoom, Map.TownSpots.Count, _trees);
        if (key.Equals(_treesKey)) return;
        _treesKey = key;
        // A tree is about a third the height of a town's houses.
        float size = 14 * Mathf.Clamp(Mathf.Sqrt(Map.Zoom), 0.7f, 1.6f);
        var view = new Rect2(-_offset, _extent).Grow(20);
        var towns = new List<Vector2>();
        foreach (var t in Map.TownSpots) towns.Add(t.At);
        float clear = 34 * Mathf.Clamp(Mathf.Sqrt(Map.Zoom), 0.7f, 1.6f);
        var xforms = new List<Transform3D>();
        var shades = new List<Color>();
        for (int k = 0; k < _trees!.Length; k++)
        {
            xforms.Clear();
            shades.Clear();
            foreach (var tree in _trees[k])
            {
                var px = (tree.LonLat - origin) / deg; // the extent's pixels
                var local = px - _offset;
                if (!view.HasPoint(local)) continue;
                bool nearTown = false;
                foreach (var t in towns)
                    if (t.DistanceSquaredTo(local) < clear * clear) { nearTown = true; break; }
                if (nearTown) continue;
                var at = Ground(local, origin, deg, lift);
                xforms.Add(new Transform3D(new Basis(Vector3.Up, tree.Turn).Scaled(Vector3.One * size * tree.Size), at));
                shades.Add(tree.Shade);
                if (xforms.Count >= 6000) break;
            }
            var trunks = _trunks[k].Multimesh;
            var leafy = _leaves[k].Multimesh;
            trunks.InstanceCount = leafy.InstanceCount = xforms.Count;
            for (int i = 0; i < xforms.Count; i++)
            {
                trunks.SetInstanceTransform(i, xforms[i]);
                leafy.SetInstanceTransform(i, xforms[i]);
                leafy.SetInstanceColor(i, shades[i]);
            }
        }
    }
}
