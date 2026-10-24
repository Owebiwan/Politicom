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
    private Node3D? Model(Node3D parent, string path, Vector3 at, Vector3 scale, float turn = 0, Color? tint = null, Color? paint = null)
    {
        if (Scene(path)?.Instantiate() is not Node3D node) return null;
        node.Position = at;
        node.Scale = scale;
        node.RotationDegrees = new Vector3(0, turn, 0);
        if (tint is { } t) Tint(node, t);
        if (paint is { } p) Paint(node, p);
        parent.AddChild(node);
        return node;
    }

    private static readonly Dictionary<Color, StandardMaterial3D> Paints = new();

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
        _view.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -35, 0), LightEnergy = 0.95f, LightColor = new Color(1, 0.96f, 0.88f) });
        _flagShader = GD.Load<Shader>("res://shaders/flag3d.gdshader");
        if (FileAccess.FileExists(MapView.LandDir + "height.land"))
        {
            var image = new Image();
            if (image.LoadWebpFromBuffer(FileAccess.GetFileAsBytes(MapView.LandDir + "height.land")) == Error.Ok)
            {
                image.Convert(Image.Format.L8);
                _heightImage = image;
            }
        }
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

        // Armies
        var armies = new HashSet<(int, int)>();
        foreach (var (enemy, commune, at, strength, color, alpha, attacking) in Map.ArmySpots)
        {
            var key = (enemy, commune);
            armies.Add(key);
            int count = (int)Mathf.Clamp(Mathf.Round(1.5f + Mathf.Sqrt(strength) * 1.3f), 2, 7);
            if (!_armies.TryGetValue(key, out var army) || army.Count != count)
            {
                army.Node?.QueueFree();
                army = (BuildCompany(count, color.Lightened(0.15f), color, null, year), count);
                _armies[key] = army;
            }
            float scale = Mathf.Clamp(0.75f + Mathf.Sqrt(strength) * 0.35f, 0.75f, 2f) * Mathf.Sqrt(Map.Zoom);
            army.Node.Position = Ground(at, origin, deg, lift);
            army.Node.Scale = Vector3.One * 13 * scale;
            army.Node.Visible = alpha > 0.3f;
        }
        foreach (var (key, army) in _armies)
            if (!armies.Contains(key)) army.Node.Visible = false;

        // Militia at the fronts
        var fronts = new HashSet<int>();
        foreach (var (commune, at, defense) in Map.MilitiaSpots)
        {
            fronts.Add(commune);
            int count = (int)Mathf.Clamp(Mathf.Round(1 + defense * 0.8f), 1, 4);
            if (!_militia.TryGetValue(commune, out var band) || band.Count != count)
            {
                band.Node?.QueueFree();
                band = (BuildCompany(count, null, new Color(0.08f, 0.08f, 0.08f), new Color(0.75f, 0.12f, 0.1f), year), count);
                _militia[commune] = band;
            }
            float scale = Mathf.Clamp(0.8f + Mathf.Sqrt(defense) * 0.25f, 0.8f, 1.6f) * Mathf.Sqrt(Map.Zoom);
            band.Node.Position = Ground(at, origin, deg, lift);
            band.Node.Scale = Vector3.One * 15 * scale;
            band.Node.Visible = true;
        }
        foreach (var (key, band) in _militia)
            if (!fronts.Contains(key)) band.Node.Visible = false;
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

    private void House(Node3D root, Vector3 at, float u, float height, float turn, AgeKind age, int tone, Random rng)
    {
        float storeys = height > 0.11f ? 1.4f : 1f;
        if (age == AgeKind.Modern) storeys += 0.4f;
        Model(root, "town/wall-block", at, new Vector3(u, u * storeys * 0.8f, u), turn, paint: age == AgeKind.Ancient ? new Color("ebe2cf") : WallPaint[tone]);
        var top = at + new Vector3(0, u * storeys * 0.8f, 0);
        // Roman houses had low tiled roofs; later ones are steeper.
        Model(root, "town/roof-gable", top, new Vector3(u, u * (age == AgeKind.Ancient ? 0.55f : 0.9f), u), turn, paint: RoofPaint(age, tone));
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

    /// <summary>
    /// A company: figures in two ranks (tinted in the army's colour, or in their own clothes for
    /// militia), each with a rifle (a spear before 1700), and a standard. Units: one figure tall = 1.
    /// </summary>
    private Node3D BuildCompany(int count, Color? uniform, Color cloth, Color? lower, int year)
    {
        var root = new Node3D();
        _objects.AddChild(root);
        bool early = year < 1700;
        var arms = new StandardMaterial3D { AlbedoColor = early ? new Color(0.45f, 0.35f, 0.22f) : new Color(0.22f, 0.17f, 0.12f) };
        string[] people = { "male-a", "male-b", "male-c", "male-d", "male-e", "male-f", "female-a", "female-b" };
        for (int i = 0; i < count; i++)
        {
            int rank = i % 2, file = i / 2;
            var at = new Vector3((file - (count - 1) / 4f) * 0.42f + rank * 0.2f, 0, -rank * 0.38f);
            string who = uniform is null ? people[(i * 5 + count) % people.Length] : people[i % 6];
            Model(root, $"people/character-{who}", at, Vector3.One * 1.5f, (i % 3 - 1) * 10, uniform);
            // The weapon: a long thin shaft over the shoulder.
            var shaft = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.05f, early ? 1.5f : 0.9f, 0.05f), Material = arms },
                Position = at + new Vector3(0.2f, early ? 0.75f : 0.65f, 0.05f),
                RotationDegrees = new Vector3(0, 0, early ? 0 : -25),
            };
            root.AddChild(shaft);
        }
        // The standard, at the left of the front rank.
        var foot = new Vector3(-(count - 1) / 4f * 0.42f - 0.45f, 0, 0.1f);
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
