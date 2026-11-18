using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// Models standing on the 3D map's terrain, towns and armies alike: each town built
/// in the style of its age from the same plan the flat map uses, out of Politicom's own buildings
/// (tools/models/build_buildings.py): houses, a landmark, factories with smoking chimneys, blocks
/// in the twentieth century, ruins once occupied; and armies as companies of figures of their era
/// (tools/models/build_soldiers.py) in the enemy's colour under a standard, facing the militia
/// under the black-and-red flag. They stand on the raised ground and follow the map as it pans
/// and zooms; zoomed out, the flat map's emblems take over.
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

    /// <summary>A model, placed (in its parent's units) and scaled.</summary>
    private Node3D? Model(Node3D parent, string path, Vector3 at, Vector3 scale, float turn = 0)
    {
        if (Scene(path)?.Instantiate() is not Node3D node) return null;
        node.Position = at;
        node.Scale = scale;
        node.RotationDegrees = new Vector3(0, turn, 0);
        parent.AddChild(node);
        return node;
    }

    private static readonly Color[] WallPaint = { new("dccaa4"), new("d6c19a"), new("cdb894") };
    private static Color RoofPaint(AgeKind age, int tone) => age switch
    {
        AgeKind.Ancient => new[] { new Color("b0603f"), new Color("9c5236"), new Color("bb6b45") }[tone], // Roman tiles
        AgeKind.Medieval or AgeKind.EarlyModern => new[] { new Color("a3523a"), new Color("6c5848"), new Color("b0603f") }[tone],
        AgeKind.Industrial => new[] { new Color("94503c"), new Color("555b66"), new Color("85473a") }[tone],
        _ => new[] { new Color("8a4d3a"), new Color("6f7177"), new Color("9a5a40") }[tone],
    };

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

    private object? _objectsFor;

    /// <summary>
    /// A new game or a loaded one: towns, works, armies, ports and trains are kept by commune id,
    /// so the last world's must go (a port of 1936 must not linger in 1650).
    /// </summary>
    private void ForgetOldWorld()
    {
        if (ReferenceEquals(_objectsFor, Map.World)) return;
        _objectsFor = Map.World;
        foreach (var t in _towns.Values) t.Node.QueueFree();
        foreach (var f in _forts.Values) f.Node.QueueFree();
        foreach (var a in _armies.Values) a.Node.QueueFree();
        foreach (var m in _militia.Values) m.Node.QueueFree();
        foreach (var p in _ports.Values) p.QueueFree();
        foreach (var cars in _trains.Values)
            foreach (var car in cars) car.QueueFree();
        _towns.Clear();
        _forts.Clear();
        _armies.Clear();
        _militia.Clear();
        _ports.Clear();
        _trains.Clear();
        _drill.Clear();
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
            bool festive = w.Celebrating && c.InFederation;
            string key = $"{Age(year)}:{c.Occupied}:{c.Kind}:{Map.TownPlan(c).Count}:{festive}";
            if (!_towns.TryGetValue(id, out var town) || town.Key != key)
            {
                town.Node?.QueueFree();
                town = (BuildTown(c, year), key);
                if (festive) Festoon(town.Node, c);
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
            int code = level * 10 + (int)Age(year); // rebuilt when the works go up or the age turns
            if (!_forts.TryGetValue(id, out var fort) || fort.Level != code)
            {
                fort.Node?.QueueFree();
                fort = (level == 0 ? new Node3D() : BuildWalls(level, Age(year)), code);
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

    /// <summary>
    /// A ring of works round a town (town units), higher with each level: a palisade on an earth
    /// bank, a battlemented stone wall, an earthen rampart, a barricade of paving stones, or a
    /// sandbagged trench, by age; at the gate and, as the works grow, round the ring, a strongpoint
    /// of the age (a gate tower, a round tower, a cannon, a field gun, a machine-gun nest). Built by
    /// tools/models/build_works.py.
    /// </summary>
    private Node3D BuildWalls(int level, AgeKind age)
    {
        var root = new Node3D();
        _objects.AddChild(root);
        string era = AgeName(age);
        const int segments = 12;
        const float radius = 0.62f;
        float length = Mathf.Tau * radius / segments * 1.05f;
        float height = 0.04f + 0.035f * level;
        Vector3 Ring(float a) => new(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius / 0.62f * 0.75f);
        for (int i = 0; i < segments - 1; i++) // the last is the gate
        {
            float a = i * Mathf.Tau / segments;
            // The wall runs along its X with the outside at +Z: turned to the ring's tangent, facing out.
            if (Building(root, $"works/wall_{era}", Ring(a), 1, 90 - Mathf.RadToDeg(a), Colors.White, Colors.White) is { } wall)
                wall.Scale = new Vector3(length, height * 1.2f, length * 0.45f);
        }
        // Strongpoints: either side of the gate, then round the ring as the works go up.
        var posts = new List<float> { (segments - 1.5f) * Mathf.Tau / segments, (segments - 0.5f) * Mathf.Tau / segments };
        if (level >= 3) posts.AddRange(new[] { 2.5f, 5.5f }.Select(k => k * Mathf.Tau / segments));
        if (level >= 4) posts.AddRange(new[] { 8.5f }.Select(k => k * Mathf.Tau / segments));
        foreach (float a in posts)
            Building(root, $"works/post_{era}", Ring(a) * 1.04f, 0.1f + 0.015f * level, 90 - Mathf.RadToDeg(a), Colors.White, Colors.White);
        return root;
    }

    private enum AgeKind { Ancient, Medieval, EarlyModern, Industrial, Modern }

    private static AgeKind Age(int year) =>
        year < 500 ? AgeKind.Ancient : year < 1450 ? AgeKind.Medieval : year < 1789 ? AgeKind.EarlyModern : year < 1914 ? AgeKind.Industrial : AgeKind.Modern;

    /// <summary>
    /// A town dressed for a celebration: black-and-red flags on poles round the square, and
    /// strings of lanterns between them glowing warm.
    /// </summary>
    private void Festoon(Node3D town, Commune c)
    {
        var rng = new Random(c.Id * 7 + 11);
        int poles = 5;
        var tops = new List<Vector3>();
        for (int i = 0; i < poles; i++)
        {
            float a = i * Mathf.Tau / poles + (float)rng.NextDouble() * 0.4f;
            var foot = new Vector3(Mathf.Cos(a) * 0.42f, 0, Mathf.Sin(a) * 0.32f);
            float h = 0.5f + (float)rng.NextDouble() * 0.1f;
            town.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.007f, Height = h, Material = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.22f, 0.14f) } },
                Position = foot + new Vector3(0, h / 2, 0),
            });
            var flag = new ShaderMaterial { Shader = _flagShader };
            flag.SetShaderParameter("upper", new Color(0.08f, 0.08f, 0.08f));
            flag.SetShaderParameter("lower", new Color(0.75f, 0.12f, 0.1f));
            flag.SetShaderParameter("split", true);
            town.AddChild(new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(0.2f, 0.13f), Orientation = PlaneMesh.OrientationEnum.Z, SubdivideWidth = 6, SubdivideDepth = 1, Material = flag },
                Position = foot + new Vector3(0.1f, h - 0.07f, 0),
            });
            tops.Add(foot + new Vector3(0, h - 0.1f, 0));
        }
        // Lanterns strung from pole to pole, sagging a little.
        var glow = new StandardMaterial3D
        {
            AlbedoColor = new Color(1, 0.8f, 0.45f), EmissionEnabled = true, Emission = new Color(1, 0.7f, 0.35f), EmissionEnergyMultiplier = 3,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var bulb = new SphereMesh { Radius = 0.013f, Height = 0.026f, RadialSegments = 6, Rings = 3, Material = glow };
        for (int i = 0; i < tops.Count; i++)
        {
            var (from, to) = (tops[i], tops[(i + 1) % tops.Count]);
            for (int k = 1; k < 8; k++)
            {
                float t = k / 8f;
                var at = from.Lerp(to, t) - new Vector3(0, 0.05f * Mathf.Sin(Mathf.Pi * t), 0);
                town.AddChild(new MeshInstance3D { Mesh = bulb, Position = at, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }
        }
    }

    private static string AgeName(AgeKind age) => age switch
    {
        AgeKind.Ancient => "ancient",
        AgeKind.Medieval => "medieval",
        AgeKind.EarlyModern => "early",
        AgeKind.Industrial => "industrial",
        _ => "modern",
    };

    /// <summary>
    /// A town of buildings from the flat map's plan, in town units (1 = the town's size on the map).
    /// The buildings are Politicom's own, made in Blender for each age (tools/models/build_buildings.py):
    /// houses, a landmark (temple, church, domed church, town hall, people's house), factories, blocks,
    /// and ruins once the town is occupied.
    /// </summary>
    private Node3D BuildTown(Commune c, int year)
    {
        var root = new Node3D();
        _objects.AddChild(root);
        var age = Age(year);
        string era = AgeName(age);
        var rng = new Random(c.Id * 131 + 7);
        foreach (var (offset, width, depth, height, kind, tone) in Map.TownPlan(c))
        {
            // The flat plan is squashed for its oblique view: here the ground is really receding.
            var at = new Vector3(offset.X, 0, offset.Y / 0.62f);
            float u = width * 1.7f; // one model unit is a building's width
            float turn = rng.Next(4) * 90 + (float)(rng.NextDouble() - 0.5) * 20;
            if (c.Occupied)
            {
                Building(root, age >= AgeKind.Industrial ? "ruin_brick" : "ruin_stone", at, u * 0.9f, turn, WallPaint[tone], RoofPaint(age, tone));
                continue;
            }
            var wall = age == AgeKind.Ancient ? new Color("efe6d4") : WallPaint[tone];
            switch (kind)
            {
                case 1:
                    Building(root, $"landmark_{era}", at, 0.24f, rng.Next(2) * 180 + 90 + (float)(rng.NextDouble() - 0.5) * 16, new Color("e2d6bc"), RoofPaint(age, 1));
                    break;
                case 2 when age >= AgeKind.Industrial:
                    if (Building(root, $"factory_{era}", at, u * 0.75f, turn, wall, RoofPaint(age, 1)) is { } works
                        && works.FindChild("Chimney", true, false) is Node3D chimney)
                        Smoke(chimney, Vector3.Zero, 1.3f);
                    break;
                case 3 when age == AgeKind.Modern:
                    Building(root, $"block_modern_{rng.Next(2)}", at, u * 0.85f, turn, wall, RoofPaint(age, tone));
                    break;
                default:
                    // Two smaller houses and two taller ones for each age; the plan says which. Half the
                    // houses are still those of the age before: towns are built up over centuries.
                    int variant = (height > 0.11f ? 2 : 0) + rng.Next(2);
                    var built = age > AgeKind.Ancient && rng.Next(2) == 0 ? age - 1 : age;
                    Building(root, $"house_{AgeName(built)}_{variant}", at, u * 0.85f, turn, wall, RoofPaint(built, tone).Lightened(0.1f));
                    break;
            }
        }
        return root;
    }

    /// <summary>Draws a model's baked vertex colours (trains: tools/models/build_trains.py).</summary>
    private static void Baked(Node node)
    {
        foreach (var child in node.FindChildren("*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mesh)
                for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
                    mesh.SetSurfaceOverrideMaterial(i, BuildingMaterial(Colors.White));
    }

    /// <summary>One of the buildings, its walls and roof in the town's colours.</summary>
    private Node3D? Building(Node3D parent, string name, Vector3 at, float scale, float turn, Color wall, Color roof)
    {
        if (Scene(name.Contains('/') ? name : $"buildings/{name}")?.Instantiate() is not Node3D node) return null;
        node.Position = at;
        node.Scale = Vector3.One * scale;
        node.RotationDegrees = new Vector3(0, turn, 0);
        foreach (var child in node.FindChildren("*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mesh)
                for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
                    if (mesh.Mesh.SurfaceGetMaterial(i) is { } m)
                        mesh.SetSurfaceOverrideMaterial(i, BuildingMaterial(m.ResourceName switch
                        {
                            "Wall" => wall,
                            "Roof" => roof,
                            _ => Colors.White, // "Detail": its colours are in the vertices
                        }));
        parent.AddChild(node);
        return node;
    }

    private static readonly Dictionary<Color, StandardMaterial3D> BuildingMaterials = new();

    /// <summary>A shared material per colour; the vertex colours carry the detail's colours and the baked shading.</summary>
    private static StandardMaterial3D BuildingMaterial(Color color)
    {
        if (!BuildingMaterials.TryGetValue(color, out var m))
            BuildingMaterials[color] = m = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.88f, VertexColorUseAsAlbedo = true };
        return m;
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
            "dance" => "Dance",
            "cheer" => "Cheer",
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
        foreach (var child in node.FindChildren("*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mesh)
                for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
                    if (mesh.Mesh.SurfaceGetMaterial(i) is BaseMaterial3D m)
                    {
                        // The army's map colour, muted toward field drab so a uniform reads as cloth, not paint;
                        // the militia's clothes and all the rest keep the colours baked into the figure.
                        var cloth = uniform is { } u ? u.Lerp(new Color(0.42f, 0.40f, 0.33f), 0.45f) : m.AlbedoColor;
                        mesh.SetSurfaceOverrideMaterial(i, UniformMaterial(m.ResourceName switch
                        {
                            "Uniform" => cloth,
                            "UniformDark" => uniform is null ? m.AlbedoColor : cloth.Darkened(0.35f),
                            _ => Colors.White,
                        }));
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
