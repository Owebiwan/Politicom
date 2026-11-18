using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// The battle window: click a battlefield on the map and see how last turn's fighting there went
/// (or an earlier turn's, from the strip below): a 3D scene of the battle (Map3D.Battle.cs) and
/// a report of the attack, the defence and why each was what it was.
/// </summary>
public partial class Main
{
    private void ShowBattle(int commune, int? tick = null)
    {
        var reports = _world.Battles.Where(b => b.Commune == commune).OrderBy(b => b.Tick).ToList();
        if (reports.Count == 0) return;
        var r = reports.LastOrDefault(b => tick is null || b.Tick == tick) ?? reports[^1];
        var c = _world.Communes[r.Commune];

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        content.AddChild(row);
        row.AddChild(new BattleStage(_map3d, _world, r) { CustomMinimumSize = new Vector2(720, 440) });

        var side = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        side.AddThemeConstantOverride("separation", 8);
        row.AddChild(side);
        var headline = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(400, 0) };
        headline.AddThemeFontOverride("normal_font", UiTheme.DisplayBold);
        headline.AddThemeFontSizeOverride("normal_font_size", 20);
        headline.Text = $"[color=#{MapView.ResultColor(r.Result).ToHtml(false)}]{Esc(Headline(r, c))}[/color]";
        side.AddChild(headline);
        side.AddChild(new StrengthBars(r) { CustomMinimumSize = new Vector2(400, 58) });
        var report = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(400, 0) };
        report.AddThemeFontSizeOverride("normal_font_size", 14);
        report.AddThemeFontSizeOverride("bold_font_size", 14);
        report.Text = Report(r, c);
        side.AddChild(report);

        // Earlier battles here, oldest first: each a button coloured by how it went.
        if (reports.Count > 1)
        {
            var strip = new HFlowContainer();
            strip.AddThemeConstantOverride("h_separation", 4);
            strip.AddChild(new Label { Text = "Battles here:", VerticalAlignment = VerticalAlignment.Center });
            foreach (var b in reports)
            {
                var button = new Button
                {
                    Text = _world.Format(_world.DateAt(b.Tick), "d MMM"), ToggleMode = true, ButtonPressed = b == r,
                    TooltipText = Headline(b, c), CustomMinimumSize = new Vector2(64, 28),
                };
                button.AddThemeColorOverride("font_color", MapView.ResultColor(b.Result));
                button.AddThemeColorOverride("font_pressed_color", Colors.White);
                int at = b.Tick;
                button.Pressed += () => ShowBattle(commune, at);
                strip.AddChild(button);
            }
            content.AddChild(strip);
        }
        ShowOverlay($"The battle for {c.Name}", "", content, 1180, ("Close", () => CloseOverlay()));
    }

    private static string Losses(BattleReport r, Commune c)
    {
        static string N(double thousands) => thousands >= 1 ? $"{thousands:0.0} thousand" : $"{thousands * 1000:0}";
        string s = $"Killed: [color=#f07060]{N(r.Dead)}[/color] of the militia";
        if (r.CiviliansDead > 0.0005) s += $" and [color=#f07060]{N(r.CiviliansDead)}[/color] townspeople";
        if (r.Fled > 0.0005) s += $"; [color=#e0b050]{N(r.Fled)}[/color] fled {Esc(c.Name)}";
        return s + ".";
    }

    private static string Headline(BattleReport r, Commune c) => r.Result switch
    {
        BattleResult.Repulsed => "Repulsed: the attack broke against the militia's line",
        BattleResult.Held => "Held: the line bent, but it did not break",
        BattleResult.LostGround => $"Ground lost: {c.Name} gave up {r.ControlBefore - r.ControlAfter:P0} of its ground",
        _ => $"{c.Name} has fallen to the {r.EnemyName}",
    };

    private string Report(BattleReport r, Commune c)
    {
        string when = _world.Format(_world.DateAt(r.Tick), "d MMMM yyyy");
        var lines = new List<string>
        {
            $"[i]{when}{(r.Offensive.Length > 0 ? $" · {Esc(r.Offensive)}" : "")}[/i]",
            $"The {Esc(r.EnemyName)} attacked {Esc(c.Name)} with a strength of [b]{r.Attack:0.00}[/b] against a defence of [b]{r.Defense:0.00}[/b]"
            + (r.Attack > r.Defense ? $": {r.Attack / r.Defense:0.0} to 1 against the militia." : $": the militia held the stronger hand, {r.Defense / r.Attack:0.0} to 1."),
            $"Ground held: [b]{r.ControlBefore:P0} → {r.ControlAfter:P0}[/b]"
            + (r.FortBefore > 0 || r.FortAfter > 0 ? $" · works {r.FortBefore:P0} → {r.FortAfter:P0}" : ""),
            $"{r.Militia:0.0} thousand under arms in {Esc(c.Name)}"
            + (r.Campaign > 0 ? $"; a defensive campaign's columns added {r.Campaign:0.00}." : "."),
            Losses(r, c),
            "",
            "[b]The defence[/b]",
        };
        lines.AddRange(r.DefenseAdded.Where(p => Math.Abs(p.Value) > 0.005).Select(p => $"   + {Esc(p.Label)} [color=#70d080]{p.Value:0.00}[/color]"));
        lines.AddRange(r.DefenseFactors.Select(p => $"   × {Esc(p.Label)} [color={(p.Value >= 1 ? "#70d080" : "#f07060")}]{p.Value:0.00}[/color]"));
        lines.Add("");
        lines.Add("[b]The attack[/b] [i](the whole army, shared along its fronts)[/i]");
        lines.AddRange(r.AttackAdded.Where(p => Math.Abs(p.Value) > 0.005).Select(p => $"   + {Esc(p.Label)} [color=#f07060]{p.Value:0.00}[/color]"));
        lines.AddRange(r.AttackFactors.Select(p => $"   × {Esc(p.Label)} [color={(p.Value <= 1 ? "#70d080" : "#f07060")}]{p.Value:0.00}[/color]"));
        return string.Join("\n", lines);
    }

    /// <summary>The 3D battle: its own little world, a camera slowly sweeping round it.</summary>
    private sealed partial class BattleStage : SubViewportContainer
    {
        private readonly SubViewport _view = new() { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, Size = new Vector2I(1440, 880) };
        private readonly Camera3D _camera = new() { Fov = 34 };
        private readonly Action<double> _animate;
        private readonly Action _dispose;
        private readonly Vector3 _focus;
        private double _clock;

        public BattleStage(Map3D map, World w, BattleReport r)
        {
            Stretch = true;
            AddChild(_view);
            var (scene, focus, animate, dispose) = map.BuildBattle(w, r);
            _focus = focus;
            _animate = animate;
            _dispose = dispose;
            _view.AddChild(scene);
            _view.AddChild(_camera);
            _view.AddChild(new DirectionalLight3D
            {
                RotationDegrees = new Vector3(-42, -40, 0), LightEnergy = 1.1f, LightColor = new Color(1, 0.94f, 0.84f),
                ShadowEnabled = true, DirectionalShadowMaxDistance = 12,
            });
            var sky = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color("5f7690"), SkyHorizonColor = new Color("c9c2ad"),
                GroundHorizonColor = new Color("8a8270"), GroundBottomColor = new Color("4a4436"),
            };
            _view.AddChild(new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = sky },
                    AmbientLightSource = Godot.Environment.AmbientSource.Sky, AmbientLightEnergy = 0.45f,
                    TonemapMode = Godot.Environment.ToneMapper.Filmic,
                    FogEnabled = true, FogLightColor = new Color("b8b2a2"), FogDensity = 0.05f,
                },
            });
            Place(0);
            // The battle's sound, close: the soundscape's battle loop for its age.
            string kind = w.Year < 1450 ? "battle_ancient" : w.Year < 1820 ? "battle_musket" : "battle_modern";
            if (ResourceLoader.Exists($"res://audio/ambience/{kind}.ogg") && Audio.Instance is { AmbienceGain: > 0.001f } audio)
            {
                var stream = GD.Load<AudioStream>($"res://audio/ambience/{kind}.ogg");
                if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
                float fierce = r.Result is BattleResult.Repulsed ? 0.6f : r.Result is BattleResult.Fell ? 0.5f : 1f;
                var player = new AudioStreamPlayer { Stream = stream, VolumeDb = Mathf.LinearToDb(audio.AmbienceGain * fierce * 1.6f), Autoplay = true };
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

        /// <summary>The camera, swaying slowly to and fro over the field from the defenders' side.</summary>
        private void Place(double t)
        {
            // From the flank, so both lines and the town behind them are in view.
            float a = Mathf.DegToRad(258 + 22 * Mathf.Sin((float)t * 0.16f));
            var target = _focus;
            _camera.LookAtFromPosition(target + new Vector3(Mathf.Cos(a) * 1.9f, 0.85f, Mathf.Sin(a) * 1.9f), target, Vector3.Up);
        }
    }

    /// <summary>Attack against defence, as two bars on one scale.</summary>
    private sealed partial class StrengthBars : Control
    {
        private readonly BattleReport _r;
        public StrengthBars(BattleReport r) => _r = r;

        public override void _Draw()
        {
            var font = UiTheme.BodyMedium;
            double top = Math.Max(_r.Attack, _r.Defense) * 1.1;
            void Bar(float y, string label, double value, Color color)
            {
                float w = Size.X - 110;
                DrawString(font, new Vector2(0, y + 15), label, HorizontalAlignment.Left, -1, 14, new Color(0.9f, 0.86f, 0.76f));
                DrawRect(new Rect2(80, y + 2, w, 18), new Color(0, 0, 0, 0.35f));
                DrawRect(new Rect2(80, y + 2, (float)(w * value / top), 18), color);
                DrawString(font, new Vector2(86 + w, y + 15), $"{value:0.00}", HorizontalAlignment.Left, -1, 13, new Color(0.9f, 0.86f, 0.76f));
            }
            Bar(4, "Attack", _r.Attack, new Color("c0503c"));
            Bar(32, "Defence", _r.Defense, new Color("5fa86a"));
        }
    }
}
