using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Politicom.Sim;

namespace Politicom.Game;

/// <summary>
/// The map's soundscape (Game/audio/ambience, tools/audio/build_ambience.py), following what is
/// on screen. Zoomed out, only a faint wind and sea; closing in, the place itself: waves where
/// the view is over the sea, wind on high ground, birdsong in spring and summer, cicadas in a
/// Mediterranean summer, winter wind and snow in the cold, rain on rainy weeks; the town nearest
/// the middle of the view (a crowd in the old towns, workshops in industrial ones, a street in
/// the twentieth century); and, near a front fought over last turn, the battle a few miles off,
/// in the sounds of its age. Now and then church bells, a rooster, gulls, a steam whistle or a
/// ship's horn. Each layer fades smoothly toward where it should be.
/// </summary>
public partial class Map3D
{
    private static readonly string[] Layers =
    {
        "sea", "wind", "mountain", "winter", "birds", "cicadas", "rain", "crowd", "workshop", "street",
        "battle_ancient", "battle_musket", "battle_modern",
    };

    private readonly Dictionary<string, (AudioStreamPlayer Player, float Gain, float Target)> _layers = new();
    private readonly AudioStreamPlayer _oneShot = new() { Bus = "Master" };
    private double _soundClock, _nextShot = 8;
    private readonly Random _soundRng = new();

    /// <summary>Set while a window covers the map: the soundscape steps back.</summary>
    public bool Ducked { get; set; }

    private void SetUpSound()
    {
        foreach (var name in Layers)
        {
            string path = $"res://audio/ambience/{name}.ogg";
            if (!ResourceLoader.Exists(path)) continue;
            var stream = GD.Load<AudioStream>(path);
            if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
            var player = new AudioStreamPlayer { Stream = stream, VolumeDb = -80 };
            AddChild(player);
            _layers[name] = (player, 0, 0);
        }
        AddChild(_oneShot);
    }

    private void SyncSound(double delta)
    {
        if (_layers.Count == 0) return;
        _soundClock += delta;
        // Where the soundscape should be: worked out four times a second, faded toward every frame.
        if (_soundClock >= 0.25)
        {
            _soundClock = 0;
            var targets = SoundTargets();
            foreach (var name in _layers.Keys.ToList())
                _layers[name] = _layers[name] with { Target = targets.GetValueOrDefault(name) };
            OneShots(targets);
        }
        float master = Audio.Instance is { } a ? a.AmbienceGain : 0;
        if (Ducked) master *= 0.25f;
        float step = (float)delta * 0.6f;
        foreach (var name in _layers.Keys.ToList())
        {
            var (player, gain, target) = _layers[name];
            gain = Mathf.MoveToward(gain, target, step);
            float level = gain * master;
            if (level > 0.003f)
            {
                if (!player.Playing) player.Play((float)(_soundRng.NextDouble() * 20)); // start somewhere in the loop
                player.VolumeDb = Mathf.LinearToDb(level);
            }
            else if (player.Playing) player.Stop();
            _layers[name] = (player, gain, target);
        }
    }

    private Dictionary<string, float> SoundTargets()
    {
        var t = new Dictionary<string, float>();
        if (Map.World is not { } w || _heightImage is not { } h || _extent == Vector2.Zero) return t;
        var (origin, deg) = Map.LonLatFrame;

        // What the view looks over: sea and land, high ground, and its latitude (a 5 x 5 sample).
        int sea = 0, land = 0;
        float elevation = 0, lat = 0;
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
            {
                var screen = new Vector2(Size.X * (i + 0.5f) / 5, Size.Y * (j + 0.5f) / 5);
                if (ToMap(screen) is not { } local) continue;
                var ll = origin + (local + _offset) * deg;
                lat += ll.Y;
                int x = (int)Mathf.PosMod((ll.X + 180) / 360 * h.GetWidth(), h.GetWidth());
                int y = Mathf.Clamp((int)((90 - ll.Y) / 180 * h.GetHeight()), 0, h.GetHeight() - 1);
                float e = h.GetPixel(x, y).R;
                if (e <= 0) sea++;
                else { land++; elevation += e; }
            }
        if (sea + land == 0) return t;
        float seaShare = sea / (float)(sea + land), landShare = 1 - seaShare;
        lat /= sea + land;
        elevation = land > 0 ? elevation / land : 0;

        // Close in, the place is heard; far out, only a faint wind and sea.
        float close = Mathf.Clamp((Map.Zoom - 0.4f) / 2.6f, 0, 1);
        float nature = 0.25f + 0.75f * close;

        int month = w.Date.Month;
        if (lat < 0) month = (month + 5) % 12 + 1;
        bool tropics = Mathf.Abs(lat) < 23;
        float winter = tropics ? 0 : month switch { 1 => 1f, 2 => 0.85f, 12 => 0.75f, 3 => 0.35f, 11 => 0.3f, _ => 0f };
        winter *= Mathf.Clamp((Mathf.Abs(lat) - 30) / 20, 0, 1); // little winter in the south
        bool summer = month is >= 6 and <= 8;
        bool greenSeason = month is >= 4 and <= 9 || tropics;
        // Some weeks it rains: more in autumn and winter.
        uint weather = (uint)(w.Tick / 2) * 2654435761u + (uint)(Mathf.Abs(lat) * 7);
        bool rain = weather % 100 < (winter > 0.2f || month is 10 or 11 ? 40 : 18);

        t["sea"] = Mathf.Pow(seaShare, 0.7f) * nature * 0.9f;
        t["wind"] = 0.25f + 0.2f * (1 - close);
        t["mountain"] = landShare * Mathf.Clamp((elevation - 0.06f) / 0.15f, 0, 1) * nature;
        t["winter"] = landShare * winter * nature;
        t["birds"] = landShare * (greenSeason ? 1 : 0.2f) * (1 - winter) * (rain ? 0.3f : 1) * close * 0.8f;
        t["cicadas"] = landShare * (summer && Mathf.Abs(lat) is > 30 and < 45 ? 1 : 0) * (rain ? 0 : 1) * close * 0.55f;
        t["rain"] = rain ? 0.4f + 0.5f * close : 0;

        // The town nearest the middle of the view.
        var middle = Size / 2;
        float town = 0;
        Commune? heard = null;
        foreach (var (id, at, _) in Map.TownSpots)
            if (OnScreen(at) is { } p)
            {
                float near = Mathf.Clamp(1 - p.DistanceTo(middle) / (Size.Length() * 0.3f), 0, 1);
                if (near > town) { town = near; heard = w.Communes[id]; }
            }
        town *= close;
        if (heard != null)
        {
            int year = w.Year;
            bool works = heard.Kind == CommuneKind.Industrial && year >= 1800;
            if (heard.Occupied) town *= 0.3f;
            if (w.Celebrating && !heard.Occupied) town = Mathf.Min(1, town * 1.6f + 0.15f * close); // the squares are full
            if (year < 1900) t["crowd"] = town * 0.9f;
            else
            {
                t["street"] = town * 0.7f;
                t["crowd"] = town * 0.4f;
            }
            if (works) t["workshop"] = town * 0.6f;
        }

        // Last turn's battles, near the middle of the view.
        float battle = 0;
        foreach (var r in w.Battles.Where(b => b.Tick == w.Tick - 1))
        {
            var spot = Map.TownSpots.FirstOrDefault(s => s.Id == r.Commune);
            if (spot.Size == 0 || OnScreen(spot.At) is not { } p) continue;
            float near = Mathf.Clamp(1 - p.DistanceTo(middle) / (Size.Length() * 0.45f), 0, 1);
            float fierce = Mathf.Clamp((float)(r.Attack / Math.Max(1e-6, r.Defense)), 0.4f, 1.5f) / 1.5f;
            battle = Mathf.Max(battle, near * fierce);
        }
        if (battle > 0)
        {
            string kind = w.Year < 1450 ? "battle_ancient" : w.Year < 1820 ? "battle_musket" : "battle_modern";
            t[kind] = battle * (0.35f + 0.65f * close);
        }
        _soundTown = heard;
        _soundSea = seaShare;
        _soundTownLevel = town;
        return t;
    }

    /// <summary>Each layer's level now, for checking the mix without ears (--sound).</summary>
    public string SoundReport() =>
        string.Join("  ", SoundTargets().Where(kv => kv.Value > 0.01f).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0.00}"))
        + (_soundTown != null ? $"  [town: {_soundTown.Name}]" : "");

    private Commune? _soundTown;
    private float _soundSea, _soundTownLevel;

    /// <summary>Now and then a sound of the place: bells, a rooster, gulls, a whistle, a ship's horn.</summary>
    private void OneShots(Dictionary<string, float> targets)
    {
        _nextShot -= 0.25;
        if (_nextShot > 0 || _oneShot.Playing || Map.World is not { } w) return;
        _nextShot = 10 + _soundRng.NextDouble() * 20;
        int year = w.Year;
        var choices = new List<(string Name, float Weight, float Level)>();
        if (_soundTown is { Occupied: false } town && _soundTownLevel > 0.25f)
        {
            if (year is > 600 and < 1960) choices.Add(("bells", w.Celebrating ? 3 : 1, _soundTownLevel * 0.7f));
            if (town.Kind == CommuneKind.Agrarian && year < 1960) choices.Add(("rooster", 1, _soundTownLevel * 0.6f));
        }
        if (_soundSea > 0.2f) choices.Add(("gulls", 1.2f, _soundSea * 0.6f));
        if (_soundSea > 0.15f && year >= 1860 && Map.TownSpots.Count > 0) choices.Add(("horn", 0.4f, _soundSea * 0.5f));
        if (year is >= 1830 and < 1965 && Map.RailSpots.Any(r => !r.Cut) && Map.Zoom > 1) choices.Add(("whistle", 0.6f, 0.45f));
        if (choices.Count == 0 || _soundRng.NextDouble() < 0.35) return; // silence is part of it
        float total = choices.Sum(c => c.Weight), pick = (float)_soundRng.NextDouble() * total;
        var chosen = choices.First(c => (pick -= c.Weight) <= 0 || c == choices[^1]);
        string path = $"res://audio/ambience/{chosen.Name}.ogg";
        if (!ResourceLoader.Exists(path)) return;
        float level = chosen.Level * (Audio.Instance?.AmbienceGain ?? 0) * (Ducked ? 0.25f : 1);
        if (level < 0.01f) return;
        _oneShot.Stream = GD.Load<AudioStream>(path);
        _oneShot.VolumeDb = Mathf.LinearToDb(level);
        _oneShot.PitchScale = 0.96f + (float)_soundRng.NextDouble() * 0.08f;
        _oneShot.Play();
    }
}
