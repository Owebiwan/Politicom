using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// Models on the 3D map, as Victoria 3 stands cities and armies on its terrain (Kenney's CC0
/// kits in Game/models): each town built in the style of its age from the same plan the flat map
/// uses (houses with gabled roofs; a temple, a church tower or a domed tower; factories with
/// smoking chimneys; blocks and suburban houses in the twentieth century; broken walls once
/// occupied), and armies as companies of small figures in the enemy's colour under a standard,
/// facing the militia under the black-and-red flag. They stand on the raised ground and follow
/// the map as it pans and zooms; zoomed out, the flat map's emblems take over.
/// </summary>
public partial class Map3D
{
    private readonly Node3D _objects = new();
    private readonly Dictionary<int, (Node3D Node, string Key)> _towns = new();
    private readonly Dictionary<(int, int), (Node3D Node, int Count)> _armies = new();
    private readonly Dictionary<int, (Node3D Node, int Count)> _militia = new();
    /// <summary>Each company's soldiers' animation players and what they're doing now.</summary>
    private readonly Dictionary<Node3D, (List<AnimationPlayer> Players, string Doing)> _drill = new();
    private readonly Dictionary<string, PackedScene?> _scenes = new();
    private static Image? _heightImage;
    private Shader? _flagShader;

    private PackedScene? Scene(string path)
    {
        if (_scenes.TryGetValue(path, out var s)) return s;
        string res = $"res://models/{path}.glb";
        return _scenes[path] = ResourceLoader.Exists(res) ? GD.Load<PackedScene>(res) : null;
    }

    /// <summary>A model, placed (in its parent's units) and scaled; tinted if asked.</summary>
    private Node3D? Model(Node3D parent, string path, Vector3 at, Vector3 scale, float turn = 0, Color? tint = null, Color? paint = null, Color? tone = null)
    {
        if (Scene(path)?.Instantiate() is not Node3D node) return null;
        node.Position = at;
        node.Scale = scale;
        node.RotationDegrees = new Vector3(0, turn, 0);
        if (tint is { } t) Tint(node, t);
        if (paint is { } p) Paint(node, p);
        if (tone is { } t2) Recolor(node, t2);
        parent.AddChild(node);
        return node;
    }

    private static readonly Dictionary<Color, StandardMaterial3D> Paints = new();
    private static readonly Dictionary<(Texture2D, Color), ShaderMaterial> Recolors = new();
    private static Shader? _recolor;

    /// <summary>Recolours a model to one tone, keeping its texture's detail as light and shade (shaders/recolor.gdshader).</summary>
    private static void Recolor(Node node, Color tone)
    {
        _recolor ??= GD.Load<Shader>("res://shaders/recolor.gdshader");
        foreach (var child in node.FindChildren("*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mesh)
                for (int i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
                    if (mesh.Mesh.SurfaceGetMaterial(i) is BaseMaterial3D { AlbedoTexture: { } tex })
                    {
                        if (!Recolors.TryGetValue((tex, tone), out var material))
                        {
                            material = new ShaderMaterial { Shader = _recolor };
                            material.SetShaderParameter("tex", tex);
                            material.SetShaderParameter("tone", tone);
                            Recolors[(tex, tone)] = material;
                        }
                        mesh.SetSurfaceOverrideMaterial(i, material);
                    }
    }

    /// <summary>Paints a model one colour (the kits' own palettes are too bright for a period map).</summary>
    private static void Paint(Node node, Color color)
    {
        if (!Paints.TryGetValue(color, out var material))
            Paints[color] = material = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f };
        foreach (var child in node.FindChildren("*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mesh)
                for (int i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
                    mesh.SetSurfaceOverrideMaterial(i, material);
    }

    private static readonly Color[] WallPaint = { new("dccaa4"), new("d6c19a"), new("cdb894") };
    private static Color RoofPaint(AgeKind age, int tone) => age switch
    {
        AgeKind.Ancient => new[] { new Color("b0603f"), new Color("9c5236"), new Color("bb6b45") }[tone], // Roman tiles
        AgeKind.Medieval or AgeKind.EarlyModern => new[] { new Color("a3523a"), new Color("6c5848"), new Color("b0603f") }[tone],
        AgeKind.Industrial => new[] { new Color("94503c"), new Color("555b66"), new Color("85473a") }[tone],
        _ => new[] { new Color("8a4d3a"), new Color("6f7177"), new Color("9a5a40") }[tone],
    };

    private static void Tint(Node node, Color color)
    {
        foreach (var child in node.FindChildren("*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mesh)
                for (int i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
                    if (mesh.Mesh.SurfaceGetMaterial(i) is BaseMaterial3D m && m.Duplicate() is BaseMaterial3D copy)
                    {
                        copy.AlbedoColor = color;
                        mesh.SetSurfaceOverrideMaterial(i, copy);
                    }
    }

    private void SetUpObjects()
    {
        _view.AddChild(_objects);
        // Sunlight from the north-west, as the terrain is lit, with shadows between buildings and figures.
        _view.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-48, -35, 0), LightEnergy = 1.05f, LightColor = new Color(1, 0.95f, 0.86f),
            ShadowEnabled = true, DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = 5000, ShadowBlur = 1.5f,
        });
        _flagShader = GD.Load<Shader>("res://shaders/flag3d.gdshader");
        Height(); // loads the elevation image too
    }

    /// <summary>The ground under a point of the flat map, raised as the terrain shader raises it.</summary>
    private Vector3 Ground(Vector2 local, Vector2 origin, Vector2 deg, float lift)
    {
        var px = local + _offset;
        float y = 0;
        if (lift > 0 && _heightImage is { } h)
        {
            var ll = origin + px * deg;
            if (Mathf.Abs(ll.Y) < 89)
            {
                int x = (int)Mathf.PosMod((ll.X + 180) / 360 * h.GetWidth(), h.GetWidth());
                int yy = Mathf.Clamp((int)((90 - ll.Y) / 180 * h.GetHeight()), 0, h.GetHeight() - 1);
                y = h.GetPixel(x, yy).R * 8848 * lift;
            }
        }
        return new Vector3(px.X, y, px.Y);
    }

    private void SyncObjects(float lift)
    {
        bool shown = Map.ObjectsShown && Map.World is not null;
        _objects.Visible = shown;
        if (!shown) return;
        var w = Map.World!;
        var (origin, deg) = Map.LonLatFrame;
        int year = w.Year;

        // Towns
        var seen = new HashSet<int>();
        foreach (var (id, at, size) in Map.TownSpots)
        {
            var c = w.Communes[id];
            seen.Add(id);
            string key = $"{Age(year)}:{c.Occupied}:{c.Kind}:{Map.TownPlan(c).Count}";
            if (!_towns.TryGetValue(id, out var town) || town.Key != key)
            {
                town.Node?.QueueFree();
                town = (BuildTown(c, year), key);
                _towns[id] = town;
            }
            float s = size * 1.05f; // a little under the flat map's size: the near ground is enlarged
            town.Node.Position = Ground(at, origin, deg, lift);
            town.Node.Scale = Vector3.One * s;
            town.Node.Visible = true;
        }
        foreach (var (id, town) in _towns)
            if (!seen.Contains(id)) town.Node.Visible = false;

        // Fortifications: a ring of walls round a fortified town, taller as the works go up.
        foreach (var (id, at, size) in Map.TownSpots)
        {
            var c = w.Communes[id];
            int level = c.Occupied ? 0 : (int)Math.Ceiling(c.Fortification * 4); // 0..4
            if (!_forts.TryGetValue(id, out var fort) || fort.Level != level)
            {
                fort.Node?.QueueFree();
                fort = (level == 0 ? new Node3D() : BuildWalls(level), level);
                if (level == 0) _objects.AddChild(fort.Node);
                _forts[id] = fort;
            }
            fort.Node.Position = Ground(at, origin, deg, lift);
            fort.Node.Scale = Vector3.One * size * 1.05f;
            fort.Node.Visible = level > 0;
        }
        foreach (var (id, fort) in _forts)
            if (!seen.Contains(id)) fort.Node.Visible = false;

        // Armies
        var armies = new HashSet<(int, int)>();
        foreach (var (enemy, commune, at, strength, color, alpha, attacking, towards, moving) in Map.ArmySpots)
        {
            var key = (enemy, commune);
            armies.Add(key);
            int count = (int)Mathf.Clamp(Mathf.Round(1.5f + Mathf.Sqrt(strength) * 1.3f), 2, 7);
            if (!_armies.TryGetValue(key, out var army) || army.Count != count)
            {
                if (army.Node != null) { _drill.Remove(army.Node); army.Node.QueueFree(); }
                army = (BuildCompany(count, color.Lightened(0.1f), color, null, year, militia: false), count);
                _armies[key] = army;
            }
            float scale = Mathf.Clamp(0.75f + Mathf.Sqrt(strength) * 0.35f, 0.75f, 2f) * Mathf.Sqrt(Map.Zoom);
            army.Node.Position = Ground(at, origin, deg, lift);
            army.Node.Scale = Vector3.One * 28 * scale;
            army.Node.Visible = alpha > 0.3f;
            Face(army.Node, at, towards);
            Drill(army.Node, attacking ? "fight" : moving ? "march" : "stand", year);
        }
        foreach (var (key, army) in _armies)
            if (!armies.Contains(key)) army.Node.Visible = false;

        // Militia at the fronts
        var fronts = new HashSet<int>();
        foreach (var (commune, at, defense, facing, underAttack) in Map.MilitiaSpots)
        {
            fronts.Add(commune);
            int count = (int)Mathf.Clamp(Mathf.Round(1 + defense * 0.8f), 1, 4);
            if (!_militia.TryGetValue(commune, out var band) || band.Count != count)
            {
                if (band.Node != null) { _drill.Remove(band.Node); band.Node.QueueFree(); }
                band = (BuildCompany(count, null, new Color(0.08f, 0.08f, 0.08f), new Color(0.75f, 0.12f, 0.1f), year, militia: true), count);
                _militia[commune] = band;
            }
            float scale = Mathf.Clamp(0.8f + Mathf.Sqrt(defense) * 0.25f, 0.8f, 1.6f) * Mathf.Sqrt(Map.Zoom);
            band.Node.Position = Ground(at, origin, deg, lift);
            band.Node.Scale = Vector3.One * 28 * scale;
            band.Node.Visible = true;
            Face(band.Node, at, facing);
            Drill(band.Node, underAttack ? "guard" : "stand", year);
        }
        foreach (var (key, band) in _militia)
            if (!fronts.Contains(key)) band.Node.Visible = false;
    }

    private readonly Dictionary<int, (Node3D Node, int Level)> _forts = new();

    /// <summary>A ring of earth-coloured wall segments round a town (town units), higher with each level of works.</summary>
    private Node3D BuildWalls(int level)
    {
        var root = new Node3D();
        _objects.AddChild(root);
        const int segments = 12;
        const float radius = 0.62f;
        float length = Mathf.Tau * radius / segments * 1.05f;
        float height = 0.04f + 0.035f * level;
        for (int i = 0; i < segments; i++)
        {
            if (i == segments - 1) continue; // a gate
            float a = i * Mathf.Tau / segments;
            var at = new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius / 0.62f * 0.75f);
            // The wall model runs along X; turn each piece to the ring's tangent.
            Model(root, "castle/wall", at, new Vector3(length, height, 0.06f), -Mathf.RadToDeg(a) - 90, paint: new Color("8a7356"));
        }
        return root;
    }

    private enum AgeKind { Ancient, Medieval, EarlyModern, Industrial, Modern }

    private static AgeKind Age(int year) =>
        year < 500 ? AgeKind.Ancient : year < 1450 ? AgeKind.Medieval : year < 1789 ? AgeKind.EarlyModern : year < 1914 ? AgeKind.Industrial : AgeKind.Modern;

    /// <summary>A town of models from the flat map's plan, in town units (1 = the town's size on the map).</summary>
    private Node3D BuildTown(Commune c, int year)
    {
        var root = new Node3D();
        _objects.AddChild(root);
        var age = Age(year);
        var rng = new Random(c.Id * 131 + 7);
        foreach (var (offset, width, depth, height, kind, tone) in Map.TownPlan(c))
        {
            // The flat plan is squashed for its oblique view: here the ground is really receding.
            var at = new Vector3(offset.X, 0, offset.Y / 0.62f);
            float u = width * 1.7f; // one model unit is a building's width
            float turn = rng.Next(4) * 90 + (float)(rng.NextDouble() - 0.5) * 20;
            if (c.Occupied)
            {
                Model(root, "town/wall-broken", at, new Vector3(u, u * 0.8f, u), turn, new Color(0.45f, 0.4f, 0.36f));
                continue;
            }
            switch (kind)
            {
                case 1: Landmark(root, at, age); break;
                case 2: Factory(root, at, u, turn, age); break;
                case 3:
                    Model(root, $"commercial/low-detail-building-{(char)('a' + rng.Next(8))}", at, new Vector3(u * 1.3f, u * 0.9f, u * 1.3f), turn, tint: new Color("e6d6bc"));
                    break;
                default: House(root, at, u, height, turn, age, tone, rng); break;
            }
        }
        return root;
    }

    /// <summary>
    /// A house of the age: a core with a facade on each side (windows with shutters, small
    /// windows, a door; timber-framed fronts on some medieval houses), a tiled gable roof, a dormer
    /// on some, a chimney from the middle ages on. The pieces keep the kit's windows and frames and
    /// take the town's colours over them.
    /// </summary>
    private void House(Node3D root, Vector3 at, float u, float height, float turn, AgeKind age, int tone, Random rng)
    {
        var house = new Node3D { Position = at, RotationDegrees = new Vector3(0, turn, 0), Scale = Vector3.One * u };
        root.AddChild(house);
        float storeys = height > 0.11f ? 1.35f : 1f;
        if (age == AgeKind.Modern) storeys += 0.35f;
        float h = storeys * 0.8f;
        var wall = age == AgeKind.Ancient ? new Color("efe6d4") : WallPaint[tone];
        bool timber = age is AgeKind.Medieval or AgeKind.EarlyModern && rng.Next(3) == 0;
        // The core, inside the facades.
        Model(house, "town/wall-block", Vector3.Zero, new Vector3(0.82f, h, 0.82f), paint: wall.Darkened(0.08f));
        // Facades: the panels sit on the +X face of their cell; turned, they make the four sides.
        string window = age == AgeKind.Ancient ? "town/wall-window-small" : timber ? "town/wall-wood-window-shutters" : "town/wall-window-shutters";
        string door = timber ? "town/wall-wood-door" : "town/wall-door";
        for (int side = 0; side < 4; side++)
        {
            string piece = side == 0 ? door : side == 2 && rng.Next(2) == 0 ? "town/wall-window-small" : window;
            Model(house, piece, Vector3.Zero, new Vector3(1, h, 1), side * 90, tone: wall);
        }
        var top = new Vector3(0, h, 0);
        var roof = RoofPaint(age, tone);
        float pitch = age == AgeKind.Ancient ? 0.55f : 0.9f;
        bool dormer = age >= AgeKind.EarlyModern && rng.Next(3) == 0;
        Model(house, dormer ? "town/roof-window" : "town/roof-gable", top, new Vector3(1, pitch, 1), tone: roof.Lightened(0.1f));
        if (age >= AgeKind.Medieval && rng.Next(3) > 0)
            Model(house, "town/chimney-top", top + new Vector3(-0.05f, pitch * 0.25f, 0.18f), new Vector3(0.8f, 0.9f, 0.8f), paint: new Color("8c6a58"));
    }

    private void Landmark(Node3D root, Vector3 at, AgeKind age)
    {
        var lm = new Node3D { Position = at, Scale = Vector3.One * 1.7f };
        root.AddChild(lm);
        root = lm;
        at = Vector3.Zero;
        var stone = new Color("e2d6bc");
        switch (age)
        {
            case AgeKind.Ancient:
                // A temple: a ring of columns under a flat roof, on a stone base.
                Model(root, "town/wall-block-half", at, new Vector3(0.32f, 0.06f, 0.2f), paint: stone);
                for (int i = 0; i < 6; i++)
                {
                    float x = -0.13f + (i % 3) * 0.13f, z = i < 3 ? -0.08f : 0.08f;
                    Model(root, "town/pillar-stone", at + new Vector3(x, 0.03f, z), new Vector3(0.05f, 0.14f, 0.05f), paint: stone);
                }
                Model(root, "town/roof-flat", at + new Vector3(0, 0.17f, 0), new Vector3(0.36f, 0.12f, 0.24f), paint: RoofPaint(age, 2));
                break;
            case AgeKind.Medieval:
                // A church: a square tower with a high roof beside its nave.
                Model(root, "town/wall-block", at + new Vector3(0.07f, 0, 0), new Vector3(0.2f, 0.13f, 0.12f), paint: stone);
                Model(root, "town/roof-gable", at + new Vector3(0.07f, 0.13f, 0), new Vector3(0.2f, 0.12f, 0.12f), paint: RoofPaint(age, 1));
                Model(root, "castle/tower-square", at + new Vector3(-0.07f, 0, 0), new Vector3(0.08f, 0.16f, 0.08f), paint: stone);
                Model(root, "castle/tower-square-top-roof-high", at + new Vector3(-0.07f, 0.2f, 0), Vector3.One * 0.08f, paint: RoofPaint(age, 1));
                break;
            case AgeKind.EarlyModern:
                // A domed church.
                Model(root, "castle/tower-hexagon-base", at, new Vector3(0.18f, 0.12f, 0.18f), paint: stone);
                Model(root, "castle/tower-hexagon-roof", at + new Vector3(0, 0.12f, 0), new Vector3(0.18f, 0.14f, 0.18f), paint: new Color("8f9aa0"));
                break;
            default:
                // A town hall with a clock tower.
                Model(root, "castle/tower-square-mid-windows", at, new Vector3(0.22f, 0.14f, 0.14f), tint: stone);
                Model(root, "town/roof-high-point", at + new Vector3(0, 0.14f, 0), new Vector3(0.1f, 0.12f, 0.1f), paint: RoofPaint(age, 1));
                break;
        }
    }

    private void Factory(Node3D root, Vector3 at, float u, float turn, AgeKind age)
    {
        Model(root, $"industrial/building-{(char)('a' + Math.Abs((int)(at.X * 977 + at.Z * 131)) % 6)}", at, Vector3.One * u * 0.8f, turn, tint: new Color("d9b8a0"));
        var chimney = at + new Vector3(u * 0.45f, 0, -u * 0.2f);
        Model(root, "industrial/chimney-large", chimney, new Vector3(u * 0.35f, u * 1.4f, u * 0.35f), paint: new Color("8a4636"));
        Smoke(root, chimney + new Vector3(0, u * 2.4f, 0), u);
    }

    /// <summary>Smoke rising from a chimney and drifting east, fading as it goes.</summary>
    private static void Smoke(Node3D root, Vector3 at, float u)
    {
        var puff = new SphereMesh { Radius = 0.5f, Height = 1, RadialSegments = 8, Rings = 4 };
        puff.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.6f, 0.6f, 0.6f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
        };
        var particles = new CpuParticles3D
        {
            Mesh = puff, Amount = 10, Lifetime = 3.5f, Position = at, LocalCoords = true,
            Direction = new Vector3(0.4f, 1, 0), Spread = 12, InitialVelocityMin = u * 0.25f, InitialVelocityMax = u * 0.35f,
            Gravity = Vector3.Zero, ScaleAmountMin = u * 0.2f, ScaleAmountMax = u * 0.3f,
            ScaleAmountCurve = new Curve(), ColorRamp = new Gradient { Colors = new[] { new Color(1, 1, 1, 0.7f), new Color(1, 1, 1, 0) } },
        };
        particles.ScaleAmountCurve.AddPoint(new Vector2(0, 0.6f));
        particles.ScaleAmountCurve.AddPoint(new Vector2(1, 2.2f));
        root.AddChild(particles);
    }

    /// <summary>Turns a company to face a point of the flat map.</summary>
    private static void Face(Node3D company, Vector2 at, Vector2 towards)
    {
        var d = towards - at;
        if (d.LengthSquared() < 1) return;
        company.Rotation = new Vector3(0, Mathf.Atan2(d.X, d.Y), 0); // models face +Z
    }

    /// <summary>
    /// Sets what a company's soldiers are doing: "stand", "march", "fight" (an army attacking) or
    /// "guard" (militia holding a front under attack). Each soldier starts at a different moment,
    /// so the ranks don't move in lockstep.
    /// </summary>
    private void Drill(Node3D company, string doing, int year)
    {
        if (!_drill.TryGetValue(company, out var drill) || drill.Doing == doing) return;
        // The figures' own clips (tools/models/build_soldiers.py): firing or thrusting, aiming or shield up.
        string anim = doing switch
        {
            "march" => "March",
            "fight" => "Attack",
            "guard" => "Guard",
            _ => "Idle",
        };
        var rng = new Random(company.GetInstanceId().GetHashCode());
        foreach (var player in drill.Players)
        {
            if (!player.HasAnimation(anim)) continue;
            player.GetAnimation(anim).LoopMode = Animation.LoopModeEnum.Linear;
            player.Play(anim, customBlend: 0.3f);
            player.Seek(rng.NextDouble() * player.CurrentAnimationLength, true);
            player.SpeedScale = 0.9f + (float)rng.NextDouble() * 0.2f;
        }
        _drill[company] = (drill.Players, doing);
    }

    private static PlaneMesh? _shadowDisc;

    /// <summary>A flat disc, dark in the middle and fading out: a contact shadow.</summary>
    private static PlaneMesh ShadowDisc()
    {
        if (_shadowDisc != null) return _shadowDisc;
        var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float r = new Vector2(x - 31.5f, y - 31.5f).Length() / 32f;
                image.SetPixel(x, y, new Color(0, 0, 0, 0.5f * Mathf.Clamp(1 - r * r, 0, 1)));
            }
        var material = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };
        return _shadowDisc = new PlaneMesh { Size = Vector2.One, Material = material };
    }

    /// <summary>The figures' era: the clothes, headgear and arms they wear.</summary>
    private static string FigureEra(int year) =>
        year < 500 ? "ancient" : year < 1450 ? "medieval" : year < 1789 ? "earlymodern" : year < 1914 ? "industrial" : "modern";

    /// <summary>
    /// One figure (Game/models/figures, built by tools/models/build_soldiers.py), one unit tall, of its
    /// era: a soldier whose "Uniform" is the army's colour ("UniformDark" a darker shade for helmets and
    /// rolls), or a militia fighter in one of three sets of civilian clothes.
    /// </summary>
    private Node3D? Soldier(Node3D parent, string model, Vector3 at, Color? uniform, Random rng, List<AnimationPlayer> players)
    {
        if (Scene($"figures/{model}")?.Instantiate() is not Node3D node) return null;
        node.Position = at;
        node.RotationDegrees = new Vector3(0, (float)(rng.NextDouble() - 0.5) * 14, 0);
        if (uniform is { } u)
            foreach (var child in node.FindChildren("*", "MeshInstance3D", true, false))
                if (child is MeshInstance3D mesh)
                    for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
                        if (mesh.Mesh.SurfaceGetMaterial(i) is BaseMaterial3D m && m.ResourceName is "Uniform" or "UniformDark")
                        {
                            // The army's map colour, muted toward field drab so a uniform reads as cloth, not paint.
                            var cloth = u.Lerp(new Color(0.42f, 0.40f, 0.33f), 0.45f);
                            mesh.SetSurfaceOverrideMaterial(i, UniformMaterial(m.ResourceName == "Uniform" ? cloth : cloth.Darkened(0.35f)));
                        }
        parent.AddChild(node);
        // A soft shadow on the ground under each figure.
        parent.AddChild(new MeshInstance3D { Mesh = ShadowDisc(), Position = at + new Vector3(0.04f, 0.01f, 0.04f), Scale = new Vector3(0.36f, 1, 0.28f) });
        if (node.FindChildren("*", "AnimationPlayer", true, false).FirstOrDefault() is AnimationPlayer player) players.Add(player);
        return node;
    }

    private static readonly Dictionary<Color, StandardMaterial3D> Uniforms = new();

    private static StandardMaterial3D UniformMaterial(Color color)
    {
        if (!Uniforms.TryGetValue(color, out var m))
            Uniforms[color] = m = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f, VertexColorUseAsAlbedo = true }; // keeps the baked shading
        return m;
    }

    /// <summary>
    /// A company: soldiers in two ranks and a standard at the left of the front rank (plain in the
    /// army's colour, or black over red for the militia). Units: one soldier tall = 1.
    /// </summary>
    private Node3D BuildCompany(int count, Color? uniform, Color cloth, Color? lower, int year, bool militia)
    {
        var root = new Node3D();
        _objects.AddChild(root);
        var rng = new Random(count * 97 + (militia ? 1 : 0) + year);
        var players = new List<AnimationPlayer>();
        string era = FigureEra(year);
        for (int i = 0; i < count; i++)
        {
            int rank = i % 2, file = i / 2;
            var at = new Vector3((file - (count - 1) / 4f) * 0.5f + rank * 0.22f, 0, -rank * 0.45f);
            string model = $"{era}_{(militia ? "militia" : "soldier")}_{(i * 2 + count) % 3}"; // three builds and faces each
            Soldier(root, model, at, militia ? null : uniform, rng, players);
        }
        _drill[root] = (players, "");
        // The standard.
        var foot = new Vector3(-(count - 1) / 4f * 0.5f - 0.5f, 0, 0.1f);
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 1.9f, Material = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.22f, 0.14f) } },
            Position = foot + new Vector3(0, 0.95f, 0),
        });
        var flag = new ShaderMaterial { Shader = _flagShader };
        flag.SetShaderParameter("upper", cloth);
        flag.SetShaderParameter("lower", lower ?? cloth);
        flag.SetShaderParameter("split", lower != null);
        root.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(0.75f, 0.48f), Orientation = PlaneMesh.OrientationEnum.Z, SubdivideWidth = 10, SubdivideDepth = 2, Material = flag },
            Position = foot + new Vector3(0.375f, 1.6f, 0),
        });
        return root;
    }
}
