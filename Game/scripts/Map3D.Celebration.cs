using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// A celebration scene, as the battle scenes stage a battle: the federation's largest commune at
/// dusk, dressed in black-and-red flags and lanterns, and before it a fairground where people
/// dance in a ring (the sardana, the farandole, the khorovod: every age has one), stand talking
/// in knots, while fireworks burst over the roofs. Units: the town's size
/// on the map is 1, as in BuildTown.
/// </summary>
public partial class Map3D
{
    public (Node3D Scene, Vector3 Focus, Action<double> Animate, Action Dispose) BuildCelebration(World w)
    {
        var root = new Node3D();
        var c = w.Federation.OrderByDescending(x => x.Population).FirstOrDefault() ?? w.Communes[0];
        int year = w.Year;
        var rng = new Random(c.Id * 13 + year);
        var companies = new List<Node3D>();
        const float s = 0.11f; // one figure's height

        root.AddChild(Ground(year, w.Date.Month));
        var town = BuildTown(c, year);
        Adopt(root, town);
        Festoon(town, c);

        // The fairground in front of the town (toward the camera, +Z).
        var fair = new Vector3(0, 0, 0.85f);
        // A ring of dancers: one company laid out on a circle, turning round it.
        const int dancers = 18;
        const float radius = 0.3f;
        var ring = new Node3D { Position = fair };
        root.AddChild(ring);
        var ringPlayers = new List<AnimationPlayer>();
        string era = FigureEra(year);
        for (int i = 0; i < dancers; i++)
        {
            float a = i * Mathf.Tau / dancers;
            var holder = new Node3D { Position = new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius), Scale = Vector3.One * s };
            holder.Rotation = new Vector3(0, Mathf.Atan2(-Mathf.Cos(a), -Mathf.Sin(a)), 0); // facing in, hands joined
            ring.AddChild(holder);
            Soldier(holder, $"{era}_militia_{i % 3}", Vector3.Zero, null, rng, ringPlayers);
        }
        // Knots of people talking, and the musicians' corner.
        for (int g = 0; g < 6; g++)
        {
            float a = (float)(rng.NextDouble() * Mathf.Tau), d = 0.45f + (float)rng.NextDouble() * 0.25f;
            var company = BuildCompany(3 + rng.Next(3), null, new Color(0.08f, 0.08f, 0.08f), new Color(0.75f, 0.12f, 0.1f), year, militia: true);
            Adopt(root, company);
            company.Position = fair + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d * 0.7f);
            company.Scale = Vector3.One * s;
            company.Rotation = new Vector3(0, a + Mathf.Pi, 0);
            companies.Add(company);
        }
        // Lantern poles round the fairground, their light warm on the dancers.
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.Tau / 6 + 0.3f;
            var at = fair + new Vector3(Mathf.Cos(a) * 0.55f, 0.22f, Mathf.Sin(a) * 0.4f);
            root.AddChild(new OmniLight3D { Position = at, LightColor = new Color(1, 0.72f, 0.4f), LightEnergy = 0.45f, OmniRange = 0.45f, ShadowEnabled = false });
            root.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.014f, Height = 0.028f, Material = new StandardMaterial3D { AlbedoColor = new Color(1, 0.62f, 0.25f), EmissionEnabled = true, Emission = new Color(1, 0.7f, 0.35f), EmissionEnergyMultiplier = 4, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } },
                Position = at,
            });
        }

        // Fireworks over the roofs: bursts at random places and colours, one after another.
        var colors = new[] { new Color(1, 0.25f, 0.2f), new Color(1, 0.85f, 0.4f), new Color(1, 1, 0.95f), new Color(0.95f, 0.45f, 0.2f) };
        var bursts = new List<CpuParticles3D>();
        for (int i = 0; i < 4; i++)
        {
            var color = colors[i % colors.Length];
            var burst = new CpuParticles3D
            {
                Emitting = false, OneShot = true, Amount = 140, Lifetime = 1.8f, Explosiveness = 1,
                Direction = Vector3.Up, Spread = 180, InitialVelocityMin = 0.55f, InitialVelocityMax = 0.75f, Gravity = new Vector3(0, -0.25f, 0),
                DampingMin = 0.4f, DampingMax = 0.6f, ScaleAmountCurve = Ramp(1f, 0.2f),
                ColorRamp = new Gradient { Colors = new[] { color with { A = 1 }, color with { A = 0 } } },
                Mesh = new SphereMesh { Radius = 0.009f, Height = 0.018f, RadialSegments = 4, Rings = 2,
                    Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 5 } },
            };
            root.AddChild(burst);
            bursts.Add(burst);
        }

        double clock = 0, nextBurst = 0;
        int fired = 0;
        bool started = false;
        void Animate(double delta)
        {
            if (!started)
            {
                // The ring starts dancing once it is in the scene tree (players can't play before).
                started = true;
                foreach (var company in companies) Drill(company, "cheer", year);
                foreach (var p in ringPlayers.Where(p => p.HasAnimation("Dance")))
                {
                    p.GetAnimation("Dance").LoopMode = Animation.LoopModeEnum.Linear;
                    p.Play("Dance");
                    p.Seek(rng.NextDouble() * 0.1, true);
                    p.SpeedScale = 0.8f;
                }
            }
            clock += delta;
            ring.Rotation = new Vector3(0, (float)clock * 0.35f, 0); // the ring turns
            if (clock >= nextBurst)
            {
                var b = bursts[fired++ % bursts.Count];
                b.Position = new Vector3((float)(rng.NextDouble() - 0.5) * 1.2f, 0.5f + (float)rng.NextDouble() * 0.2f, -0.2f - (float)rng.NextDouble() * 0.4f);
                b.Restart();
                nextBurst = clock + (fired < 3 ? 0.05 : 0.6 + rng.NextDouble() * 0.9); // a volley to open the show
            }
        }
        void Dispose()
        {
            foreach (var company in companies) _drill.Remove(company);
        }
        return (root, fair + new Vector3(0, 0.08f, -0.25f), Animate, Dispose);
    }
}
