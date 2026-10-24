using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Politicom.Game;

/// <summary>
/// Music and interface sounds (tools/audio/build_audio.py). The menu plays songs of the
/// movement; a game plays music of its age, shuffled, crossfading from one piece to the next
/// and into the new age's music when the age changes. Interface sounds come from a small pool
/// of players so they can overlap. Volumes are kept in user://settings.cfg.
/// </summary>
public partial class Audio : Node
{
    public static Audio? Instance { get; private set; }

    private sealed record Track(string File, string Title, string[] Ages, string Credit);

    private const string SettingsPath = "user://settings.cfg";
    private const float Fade = 2.5f;

    private readonly List<Track> _tracks = new();
    private readonly AudioStreamPlayer[] _music = new AudioStreamPlayer[2];
    private int _current;
    private readonly List<AudioStreamPlayer> _sfx = new();
    private readonly Dictionary<string, AudioStream?> _sounds = new();
    private string? _age;
    private readonly Queue<Track> _queue = new();
    private Track? _playing;
    private Tween? _tween;

    /// <summary>0..1 each; Muted silences both.</summary>
    public float MusicVolume { get; private set; } = 0.55f;
    public float SoundVolume { get; private set; } = 0.7f;
    public bool Muted { get; private set; }

    /// <summary>Raised when a piece starts: its title and credit.</summary>
    public event Action<string, string>? TrackStarted;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        LoadSettings();
        // Every button clicks.
        GetTree().NodeAdded += node =>
        {
            if (node is BaseButton button) button.Pressed += () => Play("click", -6);
        };
        for (int i = 0; i < 2; i++)
        {
            _music[i] = new AudioStreamPlayer { VolumeDb = -80 };
            int index = i;
            _music[i].Finished += () => { if (index == _current) Next(); };
            AddChild(_music[i]);
        }
        for (int i = 0; i < 6; i++)
        {
            var p = new AudioStreamPlayer();
            AddChild(p);
            _sfx.Add(p);
        }
        const string path = "res://data/audio/music.json";
        if (!FileAccess.FileExists(path)) return;
        foreach (var item in Json.ParseString(FileAccess.GetFileAsString(path)).AsGodotArray())
        {
            var d = item.AsGodotDictionary();
            var file = d["file"].AsString();
            if (!ResourceLoader.Exists($"res://audio/music/{file}")) continue;
            _tracks.Add(new Track(file, d["title"].AsString(), d["ages"].AsStringArray(), d.TryGetValue("credit", out var c) ? c.AsString() : ""));
        }
    }

    /// <summary>The age of a year, as the music knows it.</summary>
    public static string AgeOf(int year) => year < 500 ? "ancient" : year < 1450 ? "medieval" : year < 1789 ? "earlymodern" : year < 1914 ? "industrial" : "modern";

    /// <summary>Plays the music of an age ("menu" for the start menu); nothing changes if it's already playing.</summary>
    public void PlayAge(string age)
    {
        if (age == _age) return;
        _age = age;
        _queue.Clear();
        Next();
    }

    private void Next()
    {
        if (_age is null) return;
        if (_queue.Count == 0)
        {
            var pool = _tracks.Where(t => t.Ages.Contains(_age)).OrderBy(_ => Random.Shared.Next()).ToList();
            // Don't start a new round with the piece that just ended.
            if (pool.Count > 1 && pool[0] == _playing) (pool[0], pool[^1]) = (pool[^1], pool[0]);
            foreach (var t in pool) _queue.Enqueue(t);
        }
        if (_queue.Count == 0) return;
        var track = _queue.Dequeue();
        var stream = GD.Load<AudioStream>($"res://audio/music/{track.File}");
        if (stream is AudioStreamOggVorbis ogg) ogg.Loop = false;
        var from = _music[_current];
        _current = 1 - _current;
        var to = _music[_current];
        to.Stream = stream;
        to.VolumeDb = -60;
        to.Play();
        _playing = track;
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(to, "volume_db", MusicDb, Fade);
        if (from.Playing)
        {
            _tween.TweenProperty(from, "volume_db", -60f, Fade);
            _tween.Chain().TweenCallback(Callable.From(from.Stop));
        }
        TrackStarted?.Invoke(track.Title, track.Credit);
    }

    private float MusicDb => Muted || MusicVolume <= 0.001f ? -80 : Mathf.LinearToDb(MusicVolume) - 6;
    private float SoundDb => Muted || SoundVolume <= 0.001f ? -80 : Mathf.LinearToDb(SoundVolume);

    /// <summary>Plays an interface sound (Game/audio/ui/NAME.ogg), a little varied in pitch so repeats don't grate.</summary>
    public void Play(string name, float volumeDb = 0, float pitchJitter = 0.04f)
    {
        if (Muted || SoundVolume <= 0.001f) return;
        if (!_sounds.TryGetValue(name, out var stream))
            _sounds[name] = stream = ResourceLoader.Exists($"res://audio/ui/{name}.ogg") ? GD.Load<AudioStream>($"res://audio/ui/{name}.ogg") : null;
        if (stream is null) return;
        var player = _sfx.FirstOrDefault(p => !p.Playing) ?? _sfx[0];
        player.Stream = stream;
        player.VolumeDb = SoundDb + volumeDb;
        player.PitchScale = 1 + (float)(Random.Shared.NextDouble() * 2 - 1) * pitchJitter;
        player.Play();
    }

    public void SetVolumes(float music, float sound, bool muted)
    {
        MusicVolume = Mathf.Clamp(music, 0, 1);
        SoundVolume = Mathf.Clamp(sound, 0, 1);
        Muted = muted;
        _tween?.Kill();
        _music[_current].VolumeDb = MusicDb;
        _music[1 - _current].Stop();
        SaveSettings();
    }

    private void LoadSettings()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsPath) != Error.Ok) return;
        MusicVolume = (float)config.GetValue("audio", "music", MusicVolume).AsDouble();
        SoundVolume = (float)config.GetValue("audio", "sound", SoundVolume).AsDouble();
        Muted = config.GetValue("audio", "muted", false).AsBool();
    }

    private void SaveSettings()
    {
        var config = new ConfigFile();
        config.Load(SettingsPath);
        config.SetValue("audio", "music", MusicVolume);
        config.SetValue("audio", "sound", SoundVolume);
        config.SetValue("audio", "muted", Muted);
        config.Save(SettingsPath);
    }
}
