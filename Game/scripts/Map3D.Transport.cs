using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Politicom.Game;

/// <summary>
/// Railways and harbours on the 3D map, as Victoria 3 lays its rails and moors its ships:
/// track (Kenney Train Kit, CC0) laid tie by tie over the terrain between linked communes, with a
/// steam train running back and forth on each line that isn't cut; and at every coastal town a
/// jetty with a ship of its age riding at anchor beside it: sailing boats and galleys before 1450,
/// a square-rigger until the steamers, then cargo steamers, and a liner off the larger ports
/// (Pirate Kit and Watercraft Pack, CC0).
/// </summary>
public partial class Map3D
{
    private MultiMeshInstance3D? _track;
    private readonly Dictionary<(int, int), Node3D[]> _trains = new();
    private readonly Dictionary<int, Node3D> _ports = new();
    /// <summary>Per commune: the direction (in lon/lat) toward open water, or null when inland.</summary>
    private readonly Dictionary<int, Vector2?> _seaward = new();
    private object? _seawardFor;

    private void SetUpTransport()
    {
        Mesh? mesh = null;
        if (Scene("rail/track-single")?.Instantiate() is Node3D node)
        {
            if (node.FindChildren("*", "MeshInstance3D", true, false) is { Count: > 0 } found && found[0] is MeshInstance3D mi) mesh = mi.Mesh;
            node.Free();
        }
        _track = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh } };
        _objects.AddChild(_track);
    }

    private void SyncTransport(float lift)
    {
        if (!Map.ObjectsShown || Map.World is not { } w) return;
        var (origin, deg) = Map.LonLatFrame;
        float zoom = Mathf.Clamp(Mathf.Sqrt(Map.Zoom), 0.7f, 1.6f);
        float tile = 4.5f * zoom; // one sleeper's spacing, in map pixels
        var sleepers = new List<Transform3D>();
        var lines = new HashSet<(int, int)>();
        double time = Time.GetTicksMsec() / 1000.0;

        foreach (var (a, b, from, to, cut) in Map.RailSpots)
        {
            var d = to - from;
            float length = d.Length();
            if (length < 1) continue;
            float yaw = Mathf.Atan2(d.X, d.Y); // models run along +Z
            var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(tile * 1.1f, tile, tile));
            // Leave the towns' own ground clear.
            float skip = 14 * zoom;
            for (float s = skip; s < length - skip; s += tile)
                sleepers.Add(new Transform3D(basis, Ground(from + d * (s / length), origin, deg, lift)));
            if (cut) continue;

            // A train: locomotive and two wagons, running out and back along the line.
            var key = (a, b);
            lines.Add(key);
            if (!_trains.TryGetValue(key, out var cars))
            {
                cars = new[] { "rail/train-locomotive-a", "rail/train-carriage-coal", "rail/train-carriage-box" }
                    .Select(path => Model(_objects, path, Vector3.Zero, Vector3.One)!).ToArray();
                _trains[key] = cars;
            }
            float carLength = 2.75f * tile * 0.85f;
            float run = length - 2 * skip - 3 * carLength;
            if (run <= 0)
            {
                foreach (var car in cars) car.Visible = false;
                continue;
            }
            // Out and back, pausing a moment at each end; each line on its own schedule.
            double period = 2 * (run / (18 * zoom) + 2);
            double phase = ((time + (a * 7 + b * 13) % 11) % period) / period;
            bool outward = phase < 0.5;
            double p = outward ? phase * 2 : (1 - phase) * 2;
            float head = skip + 3 * carLength + run * Mathf.Clamp((float)(p * 1.1 - 0.05), 0, 1);
            if (!outward) head = length - head + 3 * carLength; // reversed: the locomotive leads from the other end
            for (int i = 0; i < cars.Length; i++)
            {
                float s = outward ? head - i * carLength : head - 3 * carLength + i * carLength;
                var at = Ground(from + d * (s / length), origin, deg, lift);
                cars[i].Position = at + new Vector3(0, tile * 0.25f, 0);
                cars[i].Rotation = new Vector3(0, yaw + (outward ? 0 : Mathf.Pi), 0);
                cars[i].Scale = Vector3.One * tile * 0.85f;
                cars[i].Visible = true;
            }
        }
        var mm = _track!.Multimesh;
        mm.InstanceCount = sleepers.Count;
        for (int i = 0; i < sleepers.Count; i++) mm.SetInstanceTransform(i, sleepers[i]);
        foreach (var (key, cars) in _trains)
            if (!lines.Contains(key))
                foreach (var car in cars) car.Visible = false;

        SyncPorts(w, origin, deg, lift, time);
    }

    /// <summary>Which way the open water lies from a commune, from the elevation (sea is zero), or null inland.</summary>
    private Vector2? Seaward(Politicom.Sim.Commune c)
    {
        if (!ReferenceEquals(_seawardFor, Map.World))
        {
            _seaward.Clear();
            _seawardFor = Map.World;
        }
        if (_seaward.TryGetValue(c.Id, out var known)) return known;
        Vector2? found = null;
        if (_heightImage is { } h && Map.RealGeography)
        {
            var home = new Vector2((float)c.Longitude, (float)c.Latitude);
            float k = Mathf.Cos(Mathf.DegToRad(home.Y));
            float best = 0;
            // Look out to about 12 km in sixteen directions; the most open one wins.
            for (int i = 0; i < 16; i++)
            {
                var dir = Vector2.Right.Rotated(i * Mathf.Tau / 16);
                int water = 0;
                for (int step = 1; step <= 4; step++)
                {
                    var ll = home + new Vector2(dir.X / k, dir.Y) * (0.018f * step); // 2 to 8 km out
                    int x = (int)Mathf.PosMod((ll.X + 180) / 360 * h.GetWidth(), h.GetWidth());
                    int y = Mathf.Clamp((int)((90 - ll.Y) / 180 * h.GetHeight()), 0, h.GetHeight() - 1);
                    if (h.GetPixel(x, y).R <= 0) water++;
                }
                if (water > best) { best = water; found = new Vector2(dir.X / k, dir.Y); }
            }
            if (best < 3) found = null; // the sea must be right there, not a few valleys away
        }
        return _seaward[c.Id] = found;
    }

    private void SyncPorts(Politicom.Sim.World w, Vector2 origin, Vector2 deg, float lift, double time)
    {
        var shown = new HashSet<int>();
        foreach (var (id, at, size) in Map.TownSpots)
        {
            var c = w.Communes[id];
            if (c.Occupied || Seaward(c) is not { } sea) continue;
            shown.Add(id);
            if (!_ports.TryGetValue(id, out var port))
            {
                port = BuildPort(c, w.Year);
                _ports[id] = port;
            }
            // The water side of the town, in the map's pixels.
            var screenDir = new Vector2(sea.X / deg.X, sea.Y / deg.Y).Normalized();
            var dock = at + screenDir * size * 0.6f;
            port.Position = Ground(dock, origin, deg, lift) with { Y = 0 };
            port.Rotation = new Vector3(0, Mathf.Atan2(screenDir.X, screenDir.Y), 0);
            port.Scale = Vector3.One * size;
            port.Visible = true;
            // The ships ride the swell.
            foreach (var child in port.GetChildren())
                if (child is Node3D ship && ship.HasMeta("ship"))
                {
                    float phase = (float)(time * 1.3 + ship.GetMeta("ship").AsDouble());
                    ship.Position = ship.Position with { Y = Mathf.Sin(phase) * 0.01f };
                    ship.Rotation = new Vector3(Mathf.Sin(phase * 0.8f) * 0.04f, ship.Rotation.Y, Mathf.Sin(phase) * 0.05f);
                }
        }
        foreach (var (id, port) in _ports)
            if (!shown.Contains(id)) port.Visible = false;
    }

    /// <summary>A jetty and its ships, in town units (1 = the town's size), the land behind -Z, the water ahead +Z.</summary>
    private Node3D BuildPort(Politicom.Sim.Commune c, int year)
    {
        var root = new Node3D();
        _objects.AddChild(root);
        Model(root, "sail/structure-platform-dock-small", new Vector3(0, 0, 0.05f), Vector3.One * 0.11f);
        (string Model, float Length)[] ships = year < 1450 ? new[] { ("harbour/boat-sail-a", 3.8f), ("harbour/boat-fishing-small", 3.9f) }
            : year < 1840 ? new[] { ("sail/ship-medium", 10.6f), ("harbour/boat-fishing-small", 3.9f) }
            : year < 1914 ? new[] { ("harbour/ship-cargo-a", 10.5f), ("sail/ship-small", 9f) }
            : c.Population >= 30 ? new[] { ("harbour/ship-cargo-a", 10.5f), ("harbour/ship-ocean-liner-small", 15.2f) }
            : new[] { ("harbour/ship-cargo-a", 10.5f), ("harbour/boat-fishing-small", 3.9f) };
        var rng = new Random(c.Id * 17 + 3);
        for (int i = 0; i < ships.Length; i++)
        {
            var (model, length) = ships[i];
            float size = (i == 0 ? 0.95f : 0.6f) / length; // the main ship about as long as the town is wide
            var holder = new Node3D { Position = new Vector3(i == 0 ? 0.35f : -0.4f, 0, i == 0 ? 0.75f : 1.0f), Rotation = new Vector3(0, Mathf.Pi / 2 + (float)(rng.NextDouble() - 0.5) * 0.6f, 0) };
            holder.SetMeta("ship", rng.NextDouble() * 6);
            root.AddChild(holder);
            Model(holder, model, Vector3.Zero, Vector3.One * size);
        }
        return root;
    }
}
