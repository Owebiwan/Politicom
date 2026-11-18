using System;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// The celebration scene in the anniversary and liberation pop-ups: the federation's largest town
/// at dusk, live in 3D (Map3D.Celebration.cs), framed where the event's picture would be.
/// </summary>
public partial class Main
{
    private static bool IsCelebration(string? id) => id != null && (id.StartsWith("anniversary_") || id == "liberation");

    private Control CelebrationFrame(float width)
    {
        var frame = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel", UiTheme.FramePanel(5));
        frame.AddChild(new CelebrationStage(_map3d, _world) { CustomMinimumSize = new Vector2(width - 10, (width - 10) * 5 / 12) });
        return frame;
    }

    private sealed partial class CelebrationStage : SubViewportContainer
    {
        private readonly SubViewport _view = new() { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, Size = new Vector2I(1260, 525) };
        private readonly Camera3D _camera = new() { Fov = 36 };
        private readonly Action<double> _animate;
        private readonly Action _dispose;
        private readonly Vector3 _focus;
        private double _clock;

        public CelebrationStage(Map3D map, World w)
        {
            Stretch = true;
            MouseFilter = MouseFilterEnum.Ignore;
            AddChild(_view);
            var (scene, focus, animate, dispose) = map.BuildCelebration(w);
            _focus = focus;
            _animate = animate;
            _dispose = dispose;
            _view.AddChild(scene);
            _view.AddChild(_camera);
            // A low sun behind the town, the last of the day.
            _view.AddChild(new DirectionalLight3D
            {
                RotationDegrees = new Vector3(-14, 160, 0), LightEnergy = 0.55f, LightColor = new Color(1, 0.66f, 0.42f),
                ShadowEnabled = true, DirectionalShadowMaxDistance = 10,
            });
            var sky = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color("141a33"), SkyHorizonColor = new Color("b0603a"),
                GroundHorizonColor = new Color("3a2e2a"), GroundBottomColor = new Color("141012"),
                SunAngleMax = 12,
            };
            _view.AddChild(new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = sky },
                    AmbientLightSource = Godot.Environment.AmbientSource.Sky, AmbientLightEnergy = 0.3f,
                    TonemapMode = Godot.Environment.ToneMapper.Filmic,
                    GlowEnabled = true, GlowIntensity = 0.9f, GlowBloom = 0.1f,
                    FogEnabled = true, FogLightColor = new Color("3c3040"), FogDensity = 0.025f,
                },
            });
            Place(0);
            // The crowd in the square, and the bells rung once.
            if (Audio.Instance is { AmbienceGain: > 0.001f } audio)
                foreach (var (kind, loop, gain) in new[] { ("crowd", true, 1.4f), ("bells", false, 1.1f) })
                {
                    if (!ResourceLoader.Exists($"res://audio/ambience/{kind}.ogg")) continue;
                    var stream = GD.Load<AudioStream>($"res://audio/ambience/{kind}.ogg");
                    if (stream is AudioStreamOggVorbis ogg) ogg.Loop = loop;
                    var player = new AudioStreamPlayer { Stream = stream, VolumeDb = Mathf.LinearToDb(audio.AmbienceGain * gain), Autoplay = true };
                    AddChild(player);
                }
        }

        public override void _Process(double delta)
        {
            _clock += delta;
            _animate(delta);
            Place(_clock);
        }

        public override void _ExitTree() => _dispose();

        /// <summary>The camera, circling slowly over the fairground with the town behind.</summary>
        private void Place(double t)
        {
            float a = Mathf.DegToRad(90 + 28 * Mathf.Sin((float)t * 0.12f));
            _camera.LookAtFromPosition(_focus + new Vector3(Mathf.Cos(a) * 1.7f, 0.55f, Mathf.Sin(a) * 1.7f), _focus + new Vector3(0, 0.12f, 0), Vector3.Up);
        }
    }
}
