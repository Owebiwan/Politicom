using System;
using System.Collections.Generic;
using Godot;

namespace Politicom.Game;

/// <summary>
/// Forests on the 3D map, as Victoria 3 plants trees across its wooded land: wherever Natural
/// Earth's land cover marks forest, around the region and its
/// neighbours, clumps of trees stand on the raised terrain. Pines in the north and on high
/// ground, palms in the tropics, broadleaf trees elsewhere (Kenney's Nature Kit, CC0). They are
/// placed once in longitude and latitude and drawn as one multimesh per kind, moved with the view.
/// </summary>
public partial class Map3D
{
    private enum TreeKind { Oak, OakDark, Pine, PineRound, Palm }
    private static readonly string[] TreeModels = { "nature/tree_oak", "nature/tree_oak_dark", "nature/tree_pineDefaultA", "nature/tree_pineRoundA", "nature/tree_palm" };

    private sealed record Tree(Vector2 LonLat, float Size, float Turn);

    private readonly MultiMeshInstance3D[] _forests = new MultiMeshInstance3D[TreeModels.Length];
    private List<Tree>[]? _trees;
    private object? _treesFor;

    private void SetUpTrees()
    {
        for (int k = 0; k < TreeModels.Length; k++)
        {
            Mesh? mesh = null;
            if (Scene(TreeModels[k])?.Instantiate() is Node3D node)
            {
                if (node.FindChildren("*", "MeshInstance3D", true, false) is { Count: > 0 } found && found[0] is MeshInstance3D mi) mesh = Repaint(mi.Mesh, k);
                node.Free();
            }
            _forests[k] = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            _objects.AddChild(_forests[k]);
        }
    }

    /// <summary>Foliage in the deep, slightly grey greens of a painted map (the kit's are bright mint); trunks darker.</summary>
    private static Mesh Repaint(Mesh source, int kind)
    {
        var mesh = (Mesh)source.Duplicate();
        var leaves = kind switch
        {
            (int)TreeKind.OakDark => new Color("3f5a2c"),
            (int)TreeKind.Pine or (int)TreeKind.PineRound => new Color("2f4a33"),
            (int)TreeKind.Palm => new Color("5a7a36"),
            _ => new Color("4e6b33"),
        };
        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
            if (mesh.SurfaceGetMaterial(i) is BaseMaterial3D m && m.Duplicate() is BaseMaterial3D copy)
            {
                var c = m.AlbedoColor;
                copy.AlbedoColor = c.G > c.R ? leaves : new Color("5a4030");
                copy.Roughness = 1;
                if (mesh is ArrayMesh am) am.SurfaceSetMaterial(i, copy);
            }
        return mesh;
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
                var kind = Mathf.Abs(y) < 23 ? TreeKind.Palm
                    : Mathf.Abs(y) > 52 || h > 0.16f ? (rng.Next(2) == 0 ? TreeKind.Pine : TreeKind.PineRound)
                    : rng.Next(3) == 0 ? TreeKind.OakDark : TreeKind.Oak;
                // A clump: a few trees close together, as woods are drawn on a map.
                int clump = 2 + rng.Next(3);
                for (int t = 0; t < clump; t++)
                {
                    var at = new Vector2(x, y) + new Vector2((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * step * 0.6f;
                    _trees[(int)kind].Add(new Tree(at, 0.75f + (float)rng.NextDouble() * 0.55f, (float)rng.NextDouble() * Mathf.Tau));
                }
            }
        ReleaseLandCover();
    }

    private void SyncTrees(float lift)
    {
        bool shown = Map.ObjectsShown && Map.RealGeography && Map.ShowRelief;
        foreach (var f in _forests) f.Visible = shown;
        if (!shown) return;
        PlantForests();
        var (origin, deg) = Map.LonLatFrame;
        // A tree is about a third the height of a town's houses.
        float size = 11 * Mathf.Clamp(Mathf.Sqrt(Map.Zoom), 0.7f, 1.6f);
        var view = new Rect2(-_offset, _extent).Grow(20);
        var towns = new List<Vector2>();
        foreach (var t in Map.TownSpots) towns.Add(t.At);
        float clear = 34 * Mathf.Clamp(Mathf.Sqrt(Map.Zoom), 0.7f, 1.6f);
        var xforms = new List<Transform3D>();
        for (int k = 0; k < _trees!.Length; k++)
        {
            xforms.Clear();
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
                if (xforms.Count >= 6000) break;
            }
            var mm = _forests[k].Multimesh;
            mm.InstanceCount = xforms.Count;
            for (int i = 0; i < xforms.Count; i++) mm.SetInstanceTransform(i, xforms[i]);
        }
    }
}
