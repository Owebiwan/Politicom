using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// Towns drawn as small settlements, the way Victoria 3 shows cities as clusters of buildings:
/// houses in an oblique view lit from the north-west (as the terrain is), more of them the more
/// people live there, in the style of the age (flat roofs and a temple in antiquity; steep roofs,
/// a spire and walls in the middle ages; domes after; chimneys with drifting smoke once industry
/// comes; blocks in the twentieth century). Farming communes sit among their fields. An occupied
/// town is a scorched ruin. Each town's layout is fixed by its id, so it never shuffles.
/// </summary>
public partial class MapView
{
    private enum Age { Ancient, Medieval, EarlyModern, Industrial, Modern }

    private static Age AgeOf(int year) =>
        year < 500 ? Age.Ancient : year < 1450 ? Age.Medieval : year < 1789 ? Age.EarlyModern : year < 1914 ? Age.Industrial : Age.Modern;

    private sealed record Building(Vector2 Offset, float Width, float Depth, float Height, int Kind, int Tone);

    private readonly Dictionary<(int Id, int Count, Age Age, CommuneKind Kind), List<Building>> _plans = new();

    /// <summary>A town's buildings for the 3D map: offsets (in town sizes), footprint, height, kind and tone.</summary>
    public IReadOnlyList<(Vector2 Offset, float Width, float Depth, float Height, int Kind, int Tone)> TownPlan(Commune c)
    {
        var plan = Plan(c, AgeOf(World?.Year ?? 1900));
        return plan.Select(b => (b.Offset, b.Width, b.Depth, b.Height, b.Kind, b.Tone)).ToList();
    }

    /// <summary>Building kinds: 0 house, 1 landmark (temple, church, cathedral, town hall), 2 chimney, 3 block.</summary>
    private List<Building> Plan(Commune c, Age age)
    {
        int count = (int)Mathf.Clamp(3 + Mathf.Sqrt((float)c.Population) * 1.6f, 4, 16);
        var key = (c.Id, count, age, c.Kind);
        if (_plans.TryGetValue(key, out var plan)) return plan;
        var rng = new Random(c.Id * 7919 + 17);
        plan = new List<Building>();
        bool industrial = c.Kind == CommuneKind.Industrial && age >= Age.Industrial;
        float spread = c.Kind == CommuneKind.Agrarian ? 1.25f : 1f;
        for (int i = 0; i < count; i++)
        {
            // A sunflower spiral, jittered: dense in the middle, looser at the edge.
            float r = 0.16f * Mathf.Sqrt(i + 0.6f) * spread;
            float a = i * 2.39996f + (float)rng.NextDouble() * 0.6f;
            var offset = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.62f);
            int kind = i == 0 ? 1
                : industrial && i % 4 == 2 ? 2
                : age == Age.Modern && c.Kind != CommuneKind.Agrarian && i % 3 == 1 ? 3
                : 0;
            float w = 0.13f + (float)rng.NextDouble() * 0.08f, d = 0.1f + (float)rng.NextDouble() * 0.06f;
            float h = kind == 3 ? 0.2f + (float)rng.NextDouble() * 0.12f : 0.08f + (float)rng.NextDouble() * 0.05f;
            plan.Add(new Building(offset, w, d, h, kind, rng.Next(3)));
        }
        plan.Sort((x, y) => x.Offset.Y.CompareTo(y.Offset.Y)); // back to front
        return _plans[key] = plan;
    }

    private static readonly Color[] Walls = { new("dcc8a0"), new("d2bf9a"), new("c9b58f") };
    private static Color RoofOf(Age age, int tone) => age switch
    {
        Age.Ancient => new[] { new Color("c8ad80"), new Color("bea372"), new Color("d3b98a") }[tone],
        Age.Medieval => new[] { new Color("9a4c36"), new Color("6f5a46"), new Color("a85a3c") }[tone],
        Age.EarlyModern => new[] { new Color("a3523a"), new Color("525866"), new Color("b0603f") }[tone],
        Age.Industrial => new[] { new Color("8c4a38"), new Color("4f5560"), new Color("7a4234") }[tone],
        _ => new[] { new Color("6d6e72"), new Color("85503c"), new Color("5d6168") }[tone],
    };

    /// <summary>Draws a town as a settlement at p, about `size` pixels across.</summary>
    private void DrawSettlement(Commune c, Vector2 p, float size, bool highlight, bool buildings = true)
    {
        int year = World?.Year ?? 1900;
        var age = AgeOf(year);
        var plan = Plan(c, age);
        bool ruined = c.Occupied;
        float s = size * 1.25f;
        // Ground: a soft shadow, then the town's beaten earth (or its fields).
        DrawEllipse(p + new Vector2(s * 0.04f, s * 0.05f), s * 0.52f, s * 0.3f, new Color(0, 0, 0, 0.28f));
        if (c.Kind == CommuneKind.Agrarian && !ruined) DrawFields(c, p, s);
        DrawEllipse(p, s * 0.42f, s * 0.24f, ruined ? new Color(0.18f, 0.15f, 0.13f, 0.75f) : new Color(0.62f, 0.55f, 0.42f, 0.55f));
        if (highlight) DrawEllipse(p, s * 0.55f, s * 0.33f, new Color(1, 0.95f, 0.75f, 0.22f));
        if (age == Age.Medieval && plan.Count >= 10 && !ruined)
            DrawArc(p, s * 0.47f, 0, Mathf.Tau, 40, new Color(0.55f, 0.5f, 0.42f, 0.9f), Mathf.Max(1.5f, s * 0.035f), true); // town walls
        // Earthworks: a dark palisade ring, fuller as the works go up (the 3D map raises walls instead).
        if (buildings && c.Fortification > 0.05 && !ruined)
        {
            int posts = (int)(10 + 30 * c.Fortification);
            for (int i = 0; i < posts; i++)
            {
                float a = i * Mathf.Tau / posts;
                var q = p + new Vector2(Mathf.Cos(a) * s * 0.55f, Mathf.Sin(a) * s * 0.33f);
                DrawLine(q, q + new Vector2(0, -s * 0.06f), new Color(0.3f, 0.22f, 0.14f), Mathf.Max(1.5f, s * 0.025f));
            }
        }
        if (!buildings) return; // the 3D map stands its models here
        var dim = c.Federated || ruined ? Colors.White : new Color(0.72f, 0.72f, 0.72f);
        foreach (var b in plan)
        {
            var at = p + b.Offset * s;
            if (ruined) { DrawRuin(at, b, s); continue; }
            switch (b.Kind)
            {
                case 1: DrawLandmark(at, b, s, age, dim); break;
                case 2: DrawChimney(at, b, s, dim); break;
                case 3:
                    // A block of flats: grey render, flat roof, rows of windows.
                    DrawHouse(at, b.Width * s, b.Depth * s, b.Height * s, new Color("c9c2b4") * dim, new Color("6a6b70") * dim, flat: true);
                    for (float y = b.Height * s * 0.2f; y < b.Height * s * 0.9f; y += s * 0.045f)
                        for (float x = -b.Width * s * 0.35f; x < b.Width * s * 0.4f; x += s * 0.04f)
                            DrawRect(new Rect2(at + new Vector2(x, -y), new Vector2(Mathf.Max(1, s * 0.016f), Mathf.Max(1, s * 0.018f))), new Color(0.25f, 0.27f, 0.3f, 0.8f));
                    break;
                default: DrawHouse(at, b.Width * s, b.Depth * s, b.Height * s, Walls[b.Tone] * dim, RoofOf(age, b.Tone) * dim, flat: age == Age.Ancient); break;
            }
        }
        if (ruined) DrawSmoke(p + new Vector2(0, -s * 0.15f), s, 0.55f, c.Id);
    }

    private void DrawEllipse(Vector2 c, float rx, float ry, Color color)
    {
        var pts = new Vector2[24];
        for (int i = 0; i < pts.Length; i++)
        {
            float a = i * Mathf.Tau / pts.Length;
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(pts, color);
    }

    /// <summary>A house in oblique view: front wall, the shaded side, and a pitched (or flat) roof lit from the north-west.</summary>
    private void DrawHouse(Vector2 at, float w, float d, float h, Color wall, Color roof, bool flat)
    {
        var dx = new Vector2(d * 0.55f, -d * 0.4f); // depth runs up and to the right
        var fl = at + new Vector2(-w / 2, 0);
        var fr = fl + new Vector2(w, 0);
        var up = new Vector2(0, -h);
        DrawColoredPolygon(new[] { fl, fr, fr + up, fl + up }, wall);                                  // front, lit
        DrawColoredPolygon(new[] { fr, fr + dx, fr + dx + up, fr + up }, wall.Darkened(0.32f));       // side, in shade
        if (flat)
        {
            DrawColoredPolygon(new[] { fl + up, fr + up, fr + up + dx, fl + up + dx }, roof.Lightened(0.08f));
        }
        else
        {
            var ridge = new Vector2(0, -h * 0.55f - d * 0.15f);
            var a = fl + up; var b = fr + up;
            var ra = a + dx * 0.5f + ridge; var rb = b + dx * 0.5f + ridge;
            DrawColoredPolygon(new[] { a, b, rb, ra }, roof.Lightened(0.12f));                       // front slope, facing the light
            DrawColoredPolygon(new[] { b, b + dx, rb }, roof.Darkened(0.25f));                         // gable end
            DrawColoredPolygon(new[] { ra, rb, b + dx, a + dx }, roof.Darkened(0.12f));               // back slope
        }
        DrawLine(fl, fr, new Color(0, 0, 0, 0.35f), 1);
    }

    private void DrawLandmark(Vector2 at, Building b, float s, Age age, Color dim)
    {
        var stone = new Color("e2d6bc") * dim;
        switch (age)
        {
            case Age.Ancient:
            {
                // A temple: a wide stepped base, a row of columns, a low pediment.
                float w = s * 0.26f, h = s * 0.13f;
                DrawHouse(at, w, s * 0.12f, s * 0.025f, stone.Darkened(0.1f), stone, flat: true);
                for (int i = 0; i < 5; i++)
                {
                    float x = at.X - w * 0.42f + i * w * 0.21f;
                    DrawRect(new Rect2(x - s * 0.008f, at.Y - s * 0.025f - h, s * 0.016f, h), stone.Lightened(0.1f));
                }
                var top = at + new Vector2(0, -s * 0.025f - h);
                DrawColoredPolygon(new[] { top + new Vector2(-w * 0.55f, 0), top + new Vector2(w * 0.55f, 0), top + new Vector2(0, -h * 0.4f) }, RoofOf(age, 0).Lightened(0.15f) * dim);
                break;
            }
            case Age.Medieval:
            {
                // A church: nave, and a tower with a spire.
                DrawHouse(at + new Vector2(s * 0.05f, 0), s * 0.18f, s * 0.11f, s * 0.12f, stone, RoofOf(age, 1) * dim, flat: false);
                var tower = at + new Vector2(-s * 0.07f, 0);
                DrawHouse(tower, s * 0.06f, s * 0.06f, s * 0.24f, stone, stone, flat: true);
                var t = tower + new Vector2(0, -s * 0.24f);
                DrawColoredPolygon(new[] { t + new Vector2(-s * 0.034f, 0), t + new Vector2(s * 0.05f, -s * 0.02f), t + new Vector2(0.008f * s, -s * 0.17f) }, RoofOf(age, 1).Darkened(0.1f) * dim);
                break;
            }
            case Age.EarlyModern:
            {
                // A domed church or cathedral.
                DrawHouse(at, s * 0.2f, s * 0.13f, s * 0.13f, stone, RoofOf(age, 0) * dim, flat: true);
                var top = at + new Vector2(s * 0.03f, -s * 0.16f);
                DrawCircle(top, s * 0.065f, new Color("8f9aa0") * dim);
                DrawCircle(top + new Vector2(-s * 0.02f, -s * 0.02f), s * 0.03f, new Color("b9c2c6") * dim);
                DrawRect(new Rect2(top + new Vector2(-s * 0.006f, -s * 0.1f), new Vector2(s * 0.012f, s * 0.04f)), new Color("8f9aa0") * dim);
                break;
            }
            default:
            {
                // A town hall with a clock tower.
                DrawHouse(at, s * 0.22f, s * 0.12f, s * 0.14f, stone, RoofOf(age, 1) * dim, flat: age == Age.Modern);
                var tower = at + new Vector2(0, -s * 0.14f);
                DrawHouse(tower, s * 0.05f, s * 0.04f, s * 0.1f, stone.Lightened(0.05f), RoofOf(age, 1) * dim, flat: false);
                DrawCircle(tower + new Vector2(0, -s * 0.07f), s * 0.012f, new Color(0.2f, 0.2f, 0.2f));
                break;
            }
        }
    }

    private void DrawChimney(Vector2 at, Building b, float s, Color dim)
    {
        // A factory shed with a tall brick chimney, smoking.
        DrawHouse(at, b.Width * s * 1.2f, b.Depth * s, b.Height * s * 0.9f, new Color("b79a82") * dim, new Color("5d5f66") * dim, flat: false);
        var foot = at + new Vector2(b.Width * s * 0.35f, -b.Height * s * 0.5f);
        float ch = s * 0.3f, cw = Mathf.Max(2, s * 0.03f);
        DrawRect(new Rect2(foot + new Vector2(-cw / 2, -ch), new Vector2(cw, ch)), new Color("7d3f30") * dim);
        DrawRect(new Rect2(foot + new Vector2(-cw / 2, -ch), new Vector2(cw * 0.4f, ch)), new Color("9a5442") * dim);
        DrawSmoke(foot + new Vector2(0, -ch), s, 0.35f, (int)(at.X * 13 + at.Y));
    }

    /// <summary>Smoke: soft puffs rising and drifting east, fading as they go.</summary>
    private void DrawSmoke(Vector2 from, float s, float strength, int seed)
    {
        for (int i = 0; i < 5; i++)
        {
            float t = (float)((_time * 0.25 + i / 5.0 + (seed & 255) / 255.0) % 1.0);
            var pos = from + new Vector2(t * s * 0.25f + Mathf.Sin(t * 6 + seed) * s * 0.02f, -t * s * 0.45f);
            float r = s * (0.025f + t * 0.07f);
            DrawCircle(pos, r, new Color(0.55f, 0.55f, 0.55f, strength * (1 - t) * 0.6f));
        }
    }

    private void DrawRuin(Vector2 at, Building b, float s)
    {
        // Broken walls, scorched: two stubs of a house.
        var wall = new Color("4b4038");
        DrawColoredPolygon(new[] { at + new Vector2(-b.Width * s / 2, 0), at + new Vector2(-b.Width * s * 0.1f, 0), at + new Vector2(-b.Width * s * 0.15f, -b.Height * s * 0.7f), at + new Vector2(-b.Width * s / 2, -b.Height * s * 0.4f) }, wall);
        DrawColoredPolygon(new[] { at + new Vector2(b.Width * s * 0.1f, 0), at + new Vector2(b.Width * s / 2, 0), at + new Vector2(b.Width * s / 2, -b.Height * s * 0.5f), at + new Vector2(b.Width * s * 0.2f, -b.Height * s * 0.25f) }, wall.Darkened(0.25f));
    }

    private void DrawFields(Commune c, Vector2 p, float s)
    {
        var rng = new Random(c.Id * 31 + 5);
        var colors = new[] { new Color(0.62f, 0.6f, 0.3f, 0.55f), new Color(0.45f, 0.55f, 0.28f, 0.55f), new Color(0.7f, 0.62f, 0.38f, 0.5f) };
        for (int i = 0; i < 7; i++)
        {
            float a = i * Mathf.Tau / 7 + (float)rng.NextDouble() * 0.5f;
            var centre = p + new Vector2(Mathf.Cos(a) * s * 0.62f, Mathf.Sin(a) * s * 0.38f);
            var u = new Vector2(s * 0.13f, -s * 0.04f);
            var v = new Vector2(s * 0.06f, s * 0.07f);
            DrawColoredPolygon(new[] { centre - u - v, centre + u - v, centre + u + v, centre - u + v }, colors[rng.Next(colors.Length)]);
        }
    }
}
