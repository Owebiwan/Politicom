using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// A battle scene: one turn's fighting at a front commune (a <see cref="BattleReport"/>), staged
/// as a small diorama with the map's own models. The town and its works stand at the centre, the
/// militia's line before it, the enemy's companies coming from the west; how they stand tells
/// how the battle went. Repulsed, the attackers fall back and leave their dead in front of the
/// line; held, the lines trade volleys; ground lost, the attackers are at the works, shells
/// fall among the militia and houses burn; fallen, the enemy's flag stands in the ruins.
/// Muskets and rifles flash and smoke from the age of gunpowder, guns shell the line from the
/// age of artillery. Units: the town's size on the map is 1.
/// </summary>
public partial class Map3D
{
    /// <summary>Builds the scene (not added anywhere) and returns it with a callback that animates it each frame.</summary>
    public (Node3D Scene, Vector3 Focus, Action<double> Animate, Action Dispose) BuildBattle(World w, BattleReport r)
    {
        var root = new Node3D();
        var c = w.Communes[r.Commune];
        int year = w.Year;
        var age = Age(year);
        var rng = new Random(r.Tick * 31 + r.Commune);
        var companies = new List<Node3D>();
        bool guns = year >= 1450, artillery = year >= 1820;

        // The ground: fields and earth, cratered where shells have fallen.
        root.AddChild(Ground(year, w.Date.Month));

        // The town, and its works if any were standing.
        var town = BuildTown(c, year);
        Adopt(root, town);
        int level = (int)Math.Ceiling(Math.Max(r.FortBefore, r.FortAfter) * 4);
        if (level > 0) Adopt(root, BuildWalls(level, age));

        // A few trees about the fields.
        for (int i = 0; i < 60; i++)
        {
            float a = (float)(rng.NextDouble() * Mathf.Tau), d = 1.0f + (float)rng.NextDouble() * 3.5f;
            var at = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
            if (at.X < -0.5f && Mathf.Abs(at.Z) < 1.1f) continue; // keep the field of battle clear
            BattleTree(root, at, 0.13f + (float)rng.NextDouble() * 0.06f, year < 1450 || Mathf.Abs(c.Latitude) > 50 ? "spruce" : rng.Next(3) == 0 ? "pine" : "oak");
        }

        var enemyColor = MapView.EnemyColorOf(w, r.Enemy);
        float ratio = (float)(r.Attack / Math.Max(1e-6, r.Defense));
        int enemyCompanies = Mathf.Clamp((int)Mathf.Round(2 + ratio * 1.2f), 2, 5);
        int militiaCompanies = Mathf.Clamp((int)Mathf.Round(1 + r.Militia / 2), 1, 3);
        const float s = 0.11f; // one figure's height, in town units

        // Where each side stands, by how the battle went.
        float enemyX = r.Result switch
        {
            BattleResult.Repulsed => -2.15f,
            BattleResult.Held => -1.55f,
            BattleResult.LostGround => -1.15f,
            _ => -0.15f,
        };
        float militiaX = r.Result == BattleResult.LostGround ? -0.72f : -0.86f;
        string enemyDoing = r.Result switch { BattleResult.Repulsed => "march", BattleResult.Fell => "stand", _ => "fight" };
        string militiaDoing = r.Result == BattleResult.Repulsed ? "guard" : "fight";

        for (int i = 0; i < enemyCompanies; i++)
        {
            float z = (i - (enemyCompanies - 1) / 2f) * 0.42f;
            float x = enemyX - (i % 2) * 0.18f + (float)(rng.NextDouble() - 0.5) * 0.06f;
            if (r.Result == BattleResult.Fell) { x = -0.15f + (i % 2) * 0.3f; z *= 0.6f; }
            var company = Company(root, 5, enemyColor.Lightened(0.1f), enemyColor, null, year, false, new Vector3(x, 0, z), s,
                r.Result == BattleResult.Repulsed ? -Mathf.Pi / 2 : Mathf.Pi / 2); // falling back: they turn away
            companies.Add(company);
            Drill(company, enemyDoing, year);
        }
        if (r.Result != BattleResult.Fell)
            for (int i = 0; i < militiaCompanies; i++)
            {
                float z = (i - (militiaCompanies - 1) / 2f) * 0.48f;
                var company = Company(root, 4, null, new Color(0.08f, 0.08f, 0.08f), new Color(0.75f, 0.12f, 0.1f), year, true,
                    new Vector3(militiaX, 0, z), s, -Mathf.Pi / 2);
                companies.Add(company);
                Drill(company, militiaDoing, year);
            }

        // The fallen: attackers in front of the line when they were thrown back, defenders when they gave way.
        int fallenEnemy = r.Result switch { BattleResult.Repulsed => 5, BattleResult.Held => 3, _ => 1 };
        int fallenMilitia = r.Result switch { BattleResult.LostGround => 3, BattleResult.Fell => 4, BattleResult.Held => 1, _ => 0 };
        string era = FigureEra(year);
        for (int i = 0; i < fallenEnemy; i++)
            Fallen(root, $"{era}_soldier_{i % 3}", new Vector3(-1.05f - (float)rng.NextDouble() * 0.6f, 0, (float)(rng.NextDouble() - 0.5) * 1.4f), s, enemyColor, rng);
        for (int i = 0; i < fallenMilitia; i++)
            Fallen(root, $"{era}_militia_{i % 3}", new Vector3(militiaX - 0.05f + (float)rng.NextDouble() * 0.5f, 0, (float)(rng.NextDouble() - 0.5) * 1.2f), s, null, rng);

        // Fire and smoke: volleys along both lines, shells on the defenders, burning houses.
        var flashes = new List<CpuParticles3D>();
        if (r.Result != BattleResult.Fell && guns)
        {
            if (enemyDoing == "fight")
            {
                flashes.Add(Volley(root, new Vector3(enemyX + 0.12f, s * 0.75f, 0), enemyCompanies * 0.42f));
                Gunsmoke(root, new Vector3(enemyX + 0.16f, s * 0.6f, 0), enemyCompanies * 0.42f);
            }
            flashes.Add(Volley(root, new Vector3(militiaX - 0.12f, s * 0.75f, 0), militiaCompanies * 0.48f));
            Gunsmoke(root, new Vector3(militiaX - 0.16f, s * 0.6f, 0), militiaCompanies * 0.48f);
        }
        if (artillery && r.Result is BattleResult.Held or BattleResult.LostGround)
            for (int i = 0; i < 3; i++)
                Shell(root, new Vector3(militiaX + (float)(rng.NextDouble() - 0.3) * 0.5f, 0, (float)(rng.NextDouble() - 0.5) * 1.2f), 1.3f + i * 0.45f);
        int fires = r.Result switch { BattleResult.LostGround => 2, BattleResult.Fell => 4, _ => 0 };
        for (int i = 0; i < fires; i++)
        {
            float a = (float)(rng.NextDouble() * Mathf.Tau), d = (float)rng.NextDouble() * 0.4f;
            Fire(root, new Vector3(Mathf.Cos(a) * d - 0.1f, 0.12f, Mathf.Sin(a) * d * 0.75f));
        }
        if (r.Result == BattleResult.Fell) Smoke(root, new Vector3(0, 0.3f, 0), 0.5f);

        double clock = 0;
        void Animate(double delta)
        {
            clock += delta;
            // The muzzle flashes come in ragged volleys, not a steady glow.
            foreach (var f in flashes) f.Emitting = Mathf.Sin((float)clock * 2.1f + f.Position.X * 3) > -0.2f;
        }
        void Dispose()
        {
            foreach (var company in companies) _drill.Remove(company);
        }
        // The camera watches the field between the lines, or the town itself once it has fallen.
        var focus = r.Result == BattleResult.Fell ? new Vector3(-0.15f, 0.06f, 0) : new Vector3((enemyX + militiaX) / 2 + 0.25f, 0.06f, 0);
        return (root, focus, Animate, Dispose);
    }

    /// <summary>Moves a node built into the map's object layer into the scene.</summary>
    private static void Adopt(Node3D scene, Node3D node)
    {
        node.GetParent()?.RemoveChild(node);
        node.Position = Vector3.Zero;
        node.Scale = Vector3.One;
        scene.AddChild(node);
    }

    private Node3D Company(Node3D scene, int count, Color? uniform, Color cloth, Color? lower, int year, bool militia, Vector3 at, float size, float facing)
    {
        var company = BuildCompany(count, uniform, cloth, lower, year, militia);
        Adopt(scene, company);
        company.Position = at;
        company.Scale = Vector3.One * size;
        company.Rotation = new Vector3(0, facing, 0);
        return company;
    }

    /// <summary>A figure lying where it fell.</summary>
    private void Fallen(Node3D scene, string model, Vector3 at, float size, Color? uniform, Random rng)
    {
        var holder = new Node3D { Position = at + new Vector3(0, size * 0.14f, 0), Scale = Vector3.One * size };
        holder.Rotation = new Vector3(Mathf.Pi / 2 * (rng.Next(2) == 0 ? 1 : -1), (float)(rng.NextDouble() * Mathf.Tau), 0);
        scene.AddChild(holder);
        Soldier(holder, model, Vector3.Zero, uniform, rng, new List<AnimationPlayer>());
    }

    private void BattleTree(Node3D scene, Vector3 at, float size, string kind)
    {
        if (Scene($"trees/{kind}")?.Instantiate() is not Node3D tree) return;
        tree.Position = at;
        tree.Scale = Vector3.One * size;
        tree.Rotation = new Vector3(0, (float)GD.RandRange(0, Mathf.Tau), 0);
        foreach (var child in tree.FindChildren("*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mesh)
                mesh.MaterialOverride = mesh.Name == "Leaves"
                    ? new StandardMaterial3D { AlbedoColor = new Color(kind == "spruce" ? "2c4632" : "4e6b33"), VertexColorUseAsAlbedo = true, Roughness = 1, CullMode = BaseMaterial3D.CullModeEnum.Disabled }
                    : BuildingMaterial(Colors.White);
        scene.AddChild(tree);
    }

    /// <summary>A field of earth and grass in the season's colours, with relief in its texture, broad enough for the camera's sweep.</summary>
    private static MeshInstance3D Ground(int year, int month)
    {
        bool winter = month is 12 or 1 or 2;
        var colors = winter
            ? new[] { new Color("5e584c"), new Color("7a7466"), new Color("9a9686"), new Color("6c6656") }
            : new[] { new Color("44502a"), new Color("5f6a32"), new Color("7d7442"), new Color("54602e") };
        var noise = new FastNoiseLite { Frequency = 0.02f, FractalOctaves = 5, Seed = year };
        var albedo = new NoiseTexture2D
        {
            Width = 1024, Height = 1024, Seamless = true, Noise = noise,
            ColorRamp = new Gradient { Colors = colors, Offsets = new[] { 0f, 0.4f, 0.7f, 1f } },
        };
        var bumps = new NoiseTexture2D { Width = 512, Height = 512, Seamless = true, AsNormalMap = true, BumpStrength = 6,
            Noise = new FastNoiseLite { Frequency = 0.06f, FractalOctaves = 3, Seed = year + 1 } };
        return new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(14, 14) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = albedo, NormalEnabled = true, NormalTexture = bumps, NormalScale = 0.8f,
                Uv1Scale = new Vector3(5, 5, 5), Roughness = 1,
            },
        };
    }

    /// <summary>Muzzle flashes along a firing line `width` long (Z), facing the enemy.</summary>
    private static CpuParticles3D Volley(Node3D scene, Vector3 at, float width)
    {
        var flash = new CpuParticles3D
        {
            Position = at, Amount = 18, Lifetime = 0.12f, Explosiveness = 0, Randomness = 1,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.03f, 0.01f, width / 2),
            Gravity = Vector3.Zero, InitialVelocityMin = 0, InitialVelocityMax = 0.02f,
            Mesh = new SphereMesh { Radius = 0.012f, Height = 0.024f, RadialSegments = 6, Rings = 3,
                Material = new StandardMaterial3D { AlbedoColor = new Color(1, 0.8f, 0.4f), EmissionEnabled = true, Emission = new Color(1, 0.6f, 0.2f), EmissionEnergyMultiplier = 4, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } },
        };
        scene.AddChild(flash);
        return flash;
    }

    /// <summary>A bank of white powder smoke along a firing line, drifting off on the wind.</summary>
    private static void Gunsmoke(Node3D scene, Vector3 at, float width)
    {
        scene.AddChild(new CpuParticles3D
        {
            Position = at, Amount = 26, Lifetime = 3.5f, Preprocess = 3.5f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.04f, 0.01f, width / 2 + 0.05f),
            Direction = new Vector3(0.3f, 0.25f, 0.4f), Spread = 25, InitialVelocityMin = 0.02f, InitialVelocityMax = 0.05f,
            Gravity = Vector3.Zero, ScaleAmountCurve = Ramp(0.4f, 1.8f),
            ColorRamp = new Gradient { Colors = new[] { new Color(0.95f, 0.94f, 0.9f, 0.32f), new Color(0.85f, 0.84f, 0.8f, 0) } },
            Mesh = new SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 8, Rings = 4,
                Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } },
        });
    }

    /// <summary>A shell bursting again and again at one spot: a flash, earth thrown up, smoke.</summary>
    private static void Shell(Node3D scene, Vector3 at, float every)
    {
        var dirt = new CpuParticles3D
        {
            Position = at, Amount = 40, Lifetime = every, Explosiveness = 1, OneShot = false,
            Direction = Vector3.Up, Spread = 35, InitialVelocityMin = 0.35f, InitialVelocityMax = 0.7f, Gravity = new Vector3(0, -1.4f, 0),
            ScaleAmountMin = 0.6f, ScaleAmountMax = 1.4f,
            Mesh = new BoxMesh { Size = Vector3.One * 0.008f, Material = new StandardMaterial3D { AlbedoColor = new Color("4a3b2a"), Roughness = 1 } },
        };
        scene.AddChild(dirt);
        var burst = new CpuParticles3D
        {
            Position = at + new Vector3(0, 0.03f, 0), Amount = 8, Lifetime = every, Explosiveness = 1,
            Direction = Vector3.Up, Spread = 60, InitialVelocityMin = 0.05f, InitialVelocityMax = 0.15f, Gravity = new Vector3(0, 0.05f, 0),
            ScaleAmountCurve = Ramp(0.5f, 2.4f),
            ColorRamp = new Gradient { Colors = new[] { new Color(1, 0.75f, 0.35f, 1), new Color(0.35f, 0.33f, 0.3f, 0.7f), new Color(0.4f, 0.4f, 0.4f, 0) }, Offsets = new[] { 0f, 0.08f, 1f } },
            Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 8, Rings = 4,
                Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } },
        };
        scene.AddChild(burst);
    }

    /// <summary>A burning house: flames and dark smoke rising.</summary>
    private static void Fire(Node3D scene, Vector3 at)
    {
        scene.AddChild(new CpuParticles3D
        {
            Position = at, Amount = 24, Lifetime = 0.7f, Direction = Vector3.Up, Spread = 15,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.04f,
            InitialVelocityMin = 0.08f, InitialVelocityMax = 0.16f, Gravity = Vector3.Zero, ScaleAmountCurve = Ramp(1f, 0.2f),
            ColorRamp = new Gradient { Colors = new[] { new Color(1, 0.85f, 0.4f, 1), new Color(1, 0.35f, 0.1f, 0.9f), new Color(0.4f, 0.1f, 0.05f, 0) } },
            Mesh = new SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 6, Rings = 3,
                Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    EmissionEnabled = true, Emission = new Color(1, 0.45f, 0.1f), EmissionEnergyMultiplier = 2 } },
        });
        scene.AddChild(new CpuParticles3D
        {
            Position = at + new Vector3(0, 0.08f, 0), Amount = 14, Lifetime = 4, Direction = new Vector3(0.3f, 1, 0), Spread = 10,
            InitialVelocityMin = 0.07f, InitialVelocityMax = 0.12f, Gravity = Vector3.Zero, ScaleAmountCurve = Ramp(0.6f, 3f),
            ColorRamp = new Gradient { Colors = new[] { new Color(0.12f, 0.11f, 0.1f, 0.75f), new Color(0.3f, 0.3f, 0.3f, 0) } },
            Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 8, Rings = 4,
                Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } },
        });
    }

    private static Curve Ramp(float from, float to)
    {
        var curve = new Curve { MinValue = 0, MaxValue = Math.Max(1, Math.Max(from, to)) };
        curve.AddPoint(new Vector2(0, from));
        curve.AddPoint(new Vector2(1, to));
        return curve;
    }
}
