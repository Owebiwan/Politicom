#!/usr/bin/env python3
"""
Builds the map's ambient sound (Game/audio/ambience/*.ogg, Data/audio/ambience.json):

- loops, each made seamless (its end crossfaded into its start) and loudness-matched: sea,
  wind, mountain wind, winter wind and snow, birdsong, cicadas, rain, a crowd for old towns,
  workshops for industrial ones, a street for modern ones;
- one-shots played now and then: church bells, a rooster, gulls, a steam whistle, a ship's
  horn, a foghorn;
- distant battle, mixed here from single recordings (Commons has no battle ambience under a
  free licence): for the age of firearms, scattered rifle shots in ragged volleys and the
  boom of guns, each filtered and given a long echo as if heard across the fields; for the
  age of muskets, fewer shots and the odd cannon; for older wars, the clash of iron, shouting
  and horses.

Sources are field recordings from Wikimedia Commons (public domain, CC0, CC BY or CC BY-SA;
each licence is checked when fetched, as for the music). Needs ffmpeg, numpy and scipy.

Usage:
  tools/audio/build_ambience.py
"""
import importlib.util
import json
import os
import subprocess
import time

import numpy as np
from scipy import signal

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("build_audio", os.path.join(HERE, "build_audio.py"))
ba = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ba)

ROOT = os.path.join(HERE, "..", "..")
OUT = os.path.join(ROOT, "Game", "audio", "ambience")
LIST = os.path.join(ROOT, "Data", "audio", "ambience.json")
CACHE = os.path.join(HERE, ".cache", "ambience")
RATE = 44100

# name: (Commons file, start s, seconds, loudness LUFS, kind)  kind: loop | shot
PICKS = {
    "sea": ("File:Ocean Waves on a Tropical Beach.ogg", 20, 70, -24, "loop"),
    "wind": ("File:Wind in Swedish pine forest at 25 mps.ogg", 0, 58, -27, "loop"),
    "mountain": ("File:Wind sounds 2020-05-10 1625.mp3", 60, 75, -26, "loop"),
    "winter": ("File:20221229 - Abisko Turiststation at night - Wind and snow.wav", 0, 42, -26, "loop"),
    "birds": ("File:Bourne woods Birdsong 2020-06-30 0811.mp3", 2, 95, -27, "loop"),
    "cicadas": ("File:Cicadas in Greece.ogg", 0, 27, -30, "loop"),
    "rain": ("File:Rain against the window.ogg", 2, 75, -26, "loop"),
    "crowd": ("File:442697-SBssa-Crowd Talking 003.wav", 0, 100, -30, "loop"),
    "workshop": ("File:WWS Eccentricpress.ogg", 2, 90, -31, "loop"),
    "street": ("File:Urban Street on a Rainy Afternoon.flac", 300, 90, -30, "loop"),
    "bells": ("File:Church bells - Leverkusen, 2007.oga", 0, 30, -24, "shot"),
    "rooster": ("File:Medium rooster crowing.ogg", 0, 8, -24, "shot"),
    "gulls": ("File:Gull 1.ogg", 0, 12, -26, "shot"),
    "whistle": ("File:WWS SteamWhistle.ogg", 0, 9, -25, "shot"),
    "horn": ("File:Cruise ship Albatros ship horn.ogg", 0, 12, -25, "shot"),
}
# Raw material for the battles (not shipped as they are).
PARTS = {
    "gunshots": "File:Gunshots 8.ogg",
    "explosion": "File:Explosion 10.ogg",
    "artillery": "File:Sexton artillery firing.oga",
    "clank": "File:Dag-Zali(Blacksmith tool).ogg",
    "shouting": "File:360703 eguobyte large-crowd-medium-distance-stereo.wav",
    "horses": "File:Six Horses Galloping By.ogg",
}


def source(file):
    """Downloads a Commons file (licence checked) and returns (path, credit, page)."""
    info = next(iter(ba.api({"action": "query", "titles": file, "prop": "imageinfo", "iiprop": "url|extmetadata"})["query"]["pages"].values()))
    if "imageinfo" not in info:
        raise SystemExit(f"missing on Commons: {file}")
    ii = info["imageinfo"][0]
    meta = ii.get("extmetadata", {})
    lic = ba.clean(meta.get("LicenseShortName", {}).get("value", ""))
    if not ba.allowed(lic):
        raise SystemExit(f"{file}: licence {lic!r} not allowed")
    url = ii["url"].split("?")[0]
    path = os.path.join(CACHE, ba.slug(file[5:]) + os.path.splitext(url)[1])
    if not os.path.exists(path):
        with open(path, "wb") as f:
            f.write(ba.fetch(url))
        time.sleep(8)
    artist = ba.clean(meta.get("Artist", {}).get("value", ""))[:90]
    return path, ", ".join(x for x in (artist, lic) if x) + " · Wikimedia Commons", ii.get("descriptionurl", "")


def read(path, start=0.0, seconds=None):
    """Decodes to stereo float32 at RATE."""
    cmd = ["ffmpeg", "-v", "error", "-ss", f"{start}", "-i", path]
    if seconds:
        cmd += ["-t", f"{seconds}"]
    cmd += ["-f", "f32le", "-ac", "2", "-ar", str(RATE), "-"]
    raw = subprocess.run(cmd, capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()


def write(name, audio, lufs, loop):
    """Loudness-matches and encodes; a loop gets its end crossfaded into its start."""
    if loop:
        fade = min(len(audio) // 4, RATE * 3)
        head, tail = audio[:fade], audio[-fade:]
        ramp = np.linspace(0, 1, fade, dtype=np.float32)[:, None]
        audio = np.concatenate([tail * (1 - ramp) + head * ramp, audio[fade:-fade]])
    else:
        n = min(len(audio), RATE // 10)
        audio[-n:] *= np.linspace(1, 0, n, dtype=np.float32)[:, None]
    tmp = os.path.join(CACHE, name + ".f32")
    audio.astype(np.float32).tofile(tmp)
    out = os.path.join(OUT, name + ".ogg")
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ac", "2", "-ar", str(RATE), "-i", tmp,
                    "-af", f"loudnorm=I={lufs}:TP=-3:LRA=15", "-ar", str(RATE), "-c:a", "libvorbis", "-q:a", "2", out], check=True)
    os.remove(tmp)
    return out


def lowpass(x, cutoff):
    """A gentle low-pass: distance takes the edge off."""
    b, a = signal.butter(2, cutoff / (RATE / 2))
    return signal.lfilter(b, a, x, axis=0).astype(np.float32)


def echo(x, seconds=1.6, mix=0.5, seed=1):
    """A long, diffuse echo across open country: convolution with decaying noise."""
    rng = np.random.default_rng(seed)
    n = int(seconds * RATE)
    tail = rng.standard_normal((n, 2)).astype(np.float32) * np.exp(-np.linspace(0, 6, n, dtype=np.float32))[:, None]
    tail /= np.sqrt((tail ** 2).sum(axis=0))
    wet = np.stack([signal.fftconvolve(x[:, c], tail[:, c])[: len(x)] for c in range(2)], axis=1).astype(np.float32)
    return x * (1 - mix) + wet * mix


def hits(sample, threshold=0.25, min_gap=0.15):
    """Cuts a recording of several shots or blows into single ones."""
    env = np.abs(sample).max(axis=1)
    peak = env.max()
    out, i = [], 0
    while i < len(env):
        if env[i] > threshold * peak:
            start = max(0, i - int(0.01 * RATE))
            end = min(len(env), i + int(0.9 * RATE))
            out.append(sample[start:end].copy())
            i = end + int(min_gap * RATE)
        else:
            i += 1
    return out or [sample]


def scatter(length, shots, rate, cluster, gain, seed, pan=0.8):
    """Places shots at random through `length` seconds, in clusters (ragged volleys)."""
    rng = np.random.default_rng(seed)
    bed = np.zeros((int(length * RATE), 2), dtype=np.float32)
    t = 0.0
    while t < length - 1:
        t += rng.exponential(1 / rate)
        for k in range(rng.integers(1, cluster + 1)):
            at = int((t + k * rng.uniform(0.04, 0.25)) * RATE)
            s = shots[rng.integers(len(shots))] * rng.uniform(0.25, 1.0) * gain
            p = rng.uniform(-pan, pan)
            s = s * np.array([1 - max(0, p), 1 + min(0, p)], dtype=np.float32)
            end = min(len(bed), at + len(s))
            if at < end:
                bed[at:end] += s[: end - at]
    return bed


def battles(parts):
    length = 60
    shots = hits(read(parts["gunshots"][0]))
    booms = [read(parts["explosion"][0])] + hits(read(parts["artillery"][0]), threshold=0.5, min_gap=1.0)
    rumble = np.resize(lowpass(read(PICKS_PATHS["wind"], 0, length), 300) * 0.5, (int(length * RATE), 2))

    # Rifles and machine guns, and guns: the battle a few miles off.
    rifles = scatter(length, shots, rate=1.8, cluster=5, gain=0.5, seed=1)
    guns = scatter(length, booms, rate=0.22, cluster=1, gain=1.0, seed=2)
    modern = echo(lowpass(rifles, 1800) + lowpass(guns, 500), 1.8, 0.55) + rumble
    # Muskets: fewer, in volleys, and the odd cannon.
    volleys = scatter(length, shots, rate=0.45, cluster=10, gain=0.45, seed=3)
    cannon = scatter(length, booms, rate=0.08, cluster=1, gain=0.9, seed=4)
    musket = echo(lowpass(volleys, 1500) + lowpass(cannon, 450), 1.6, 0.5) + rumble
    # Older wars: iron on iron, shouting, horses.
    clanks = hits(read(parts["clank"][0]), threshold=0.3, min_gap=0.05)
    iron = scatter(length, clanks, rate=3.0, cluster=3, gain=0.35, seed=5)
    shouting = read(parts["shouting"][0], 0, length)
    shouting = np.resize(shouting, (int(length * RATE), 2))
    horses = np.resize(read(parts["horses"][0]), (int(length * RATE), 2)) * 0.4
    ancient = echo(lowpass(iron, 2500) + lowpass(shouting, 1200) * 0.7 + lowpass(horses, 900), 1.2, 0.4) + rumble
    return {"battle_modern": modern, "battle_musket": musket, "battle_ancient": ancient}


PICKS_PATHS = {}


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(CACHE, exist_ok=True)
    credits = []
    for name, (file, start, seconds, lufs, kind) in PICKS.items():
        path, credit, page = source(file)
        PICKS_PATHS[name] = path
        audio = read(path, start, seconds)
        write(name, audio, lufs, kind == "loop")
        credits.append({"file": name + ".ogg", "kind": kind, "seconds": round(len(audio) / RATE), "credit": credit, "source": page})
        print(f"{name}: {len(audio) / RATE:.0f}s · {credit}")
    parts = {name: source(file) for name, file in PARTS.items()}
    for name, audio in battles(parts).items():
        write(name, audio, -27, True)
        used = ["gunshots", "explosion", "artillery"] if name != "battle_ancient" else ["clank", "shouting", "horses"]
        credits.append({"file": name + ".ogg", "kind": "loop", "seconds": 60,
                        "credit": "Mixed for Politicom from: " + "; ".join(parts[u][1] for u in used) + "; wind as above",
                        "source": " ".join(parts[u][2] for u in used)})
        print(f"{name}: mixed")
    with open(LIST, "w", encoding="utf-8") as f:
        json.dump(credits, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
