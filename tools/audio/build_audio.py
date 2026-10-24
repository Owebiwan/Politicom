#!/usr/bin/env python3
"""
Builds the game's sound:

- Game/audio/music/<slug>.ogg: music for each age, from Wikimedia Commons (public domain,
  CC0, CC BY or CC BY-SA only; the licence is checked when each file is fetched). Each piece
  is loudness-matched, faded in and out, trimmed to at most four minutes, and encoded as Ogg
  Vorbis. The menu plays songs of the movement (Hijos del pueblo, A las barricadas, the black
  Internationale); a game plays its age's music.
- Data/audio/music.json: the playlist with each piece's ages and its credit.
- Game/audio/ui/<name>.ogg: interface sounds, chosen from Kenney's RPG Audio, UI Audio and
  Interface Sounds packs (CC0, kenney.nl), plus a bell and a drum made here.

Downloads are cached in tools/audio/.cache/ (not committed).

Usage:
  tools/audio/build_audio.py KENNEY_DIR        (the unzipped Kenney packs; needs ffmpeg and numpy)
"""
import json
import math
import os
import re
import subprocess
import sys
import time
import urllib.parse
import urllib.error
import urllib.request
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
MUSIC = os.path.join(ROOT, "Game", "audio", "music")
UI = os.path.join(ROOT, "Game", "audio", "ui")
LIST = os.path.join(ROOT, "Data", "audio", "music.json")
CACHE = os.path.join(HERE, ".cache")
UA = "PoliticomGame/1.0 (game music; private project)"
MAX_SECONDS = 240

# (Commons file, title shown in the game, ages)
PICKS = [
    ("File:Hijos del pueblo (Coral).ogg", "Hijos del pueblo", ["menu"]),
    ("File:A Las Barricadas (piano-vocals).opus", "A las barricadas", ["menu"]),
    ("File:Internationale Noire.opus", "L'Internationale noire", ["menu"]),
    ("File:Whirlwinds of Danger from Lansbury's Labour Weekly.ogg", "Whirlwinds of Danger (Warszawianka)", ["menu"]),

    ("File:Ancient Rite by Kevin MacLeod.ogg", "Ancient Rite", ["ancient"]),
    ("File:Temple of the Manes (ISRC USUAN1100053).mp3", "Temple of the Manes", ["ancient"]),
    ("File:Hidden Past (ISRC USUAN1600030).mp3", "Hidden Past", ["ancient"]),
    ("File:Teller of the Tales (ISRC USUAN1400020).mp3", "Teller of the Tales", ["ancient", "medieval"]),
    ("File:Ο Επιτάφιος του Σεῖκιλου - Epitaph of Seikilos.ogg", "The Epitaph of Seikilos", ["ancient"]),
    ("File:Guqin-Yangguan Sandie.ogg", "Yangguan Sandie (guqin)", ["ancient"]),

    ("File:Machaut - Missa Notre Dame - Kyrie.ogg", "Machaut, Messe de Nostre Dame: Kyrie", ["medieval"]),
    ("File:Veni.sancte.spiritus.ogg", "Veni Sancte Spiritus", ["medieval"]),
    ("File:Santa Maria.ogg", "Cantigas de Santa Maria", ["medieval"]),
    ("File:Estampie Retrove Robertsbridge.ogg", "Estampie (Robertsbridge Codex)", ["medieval"]),
    ("File:Village Consort (ISRC USUAN1700007).mp3", "Village Consort", ["medieval", "earlymodern"]),
    ("File:Lord of the Land (ISRC USUAN1400022).mp3", "Lord of the Land", ["medieval"]),

    ("File:Flow, my tears.ogg", "Dowland, Flow, my tears", ["earlymodern"]),
    ("File:Bach, Goldberg Variations, Aria (Musopen version).ogg", "Bach, Goldberg Variations: Aria", ["earlymodern"]),
    ("File:Handel - Suites for Harpsichord - No.5 in E major - The Harmonious Blacksmith.ogg", "Handel, The Harmonious Blacksmith", ["earlymodern"]),
    ("File:Vivaldi - Four Seasons 2 Summer mvt 2 Adagio - John Harrison violin.oga", "Vivaldi, Summer: Adagio", ["earlymodern"]),
    ("File:11 - Vivaldi Winter mvt 2 Largo - John Harrison violin.ogg", "Vivaldi, Winter: Largo", ["earlymodern"]),
    ("File:H. Purcell - March from Music for the funeral of Queen Mary.oga", "Purcell, March for Queen Mary", ["earlymodern"]),

    ("File:Ludwig van Beethoven - symphony no. 5 in c minor, op. 67 - ii. andante con moto.ogg", "Beethoven, Symphony No. 5: Andante con moto", ["industrial"]),
    ("File:Ludwig van Beethoven - symphony no. 4 in b flat major, op. 60 - iv. allegro ma non troppo.ogg", "Beethoven, Symphony No. 4: Finale", ["industrial"]),
    ("File:Chopin, Nocturne in C-sharp minor, Op. Posth.ogg", "Chopin, Nocturne in C-sharp minor", ["industrial"]),
    ("File:Chopin - Nocturne No. 19 in E minor, Op. 72 No. 1 (Luke Faulkner).flac", "Chopin, Nocturne in E minor", ["industrial"]),
    ("File:Dvořák, Antonin — Symphony No. 8, Op. 88 — 3. allegretto grazioso.ogg", "Dvořák, Symphony No. 8: Allegretto grazioso", ["industrial", "modern"]),

    ("File:Antonin Dvorak - symphony no. 9 in e minor 'from the new world', op. 95 - ii. largo.ogg", "Dvořák, From the New World: Largo", ["modern"]),
    ("File:Erik Satie - gymnopedies - la 1 ere. lent et douloureux.ogg", "Satie, Gymnopédie No. 1", ["modern"]),
    ("File:Satie Gymnopedie No 3 performed by Michael Laucke.flac", "Satie, Gymnopédie No. 3", ["modern"]),
    ("File:Clair de Lune by Claude Debussy (1905, piano solo).opus", "Debussy, Clair de lune", ["modern"]),
]

# Interface sounds: game name -> Kenney file.
UI_SOUNDS = {
    "click": "click_003", "open": "bookOpen", "close": "bookClose", "page": "bookFlip2",
    "coins": "handleCoins", "error": "error_004", "toggle": "toggle_001", "speed": "switch_002",
    "sword": "drawKnife2", "good": "confirmation_002", "tick": "tick_002",
}


def api(params):
    q = urllib.parse.urlencode({**params, "format": "json"})
    req = urllib.request.Request("https://commons.wikimedia.org/w/api.php?" + q, headers={"User-Agent": UA})
    for attempt in range(6):
        try:
            return json.loads(urllib.request.urlopen(req, timeout=60).read())
        except urllib.error.HTTPError as e:
            if e.code != 429 or attempt == 5:
                raise
            time.sleep(30 * (attempt + 1))


def fetch(url):
    for attempt in range(8):
        try:
            return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=600).read()
        except urllib.error.HTTPError as e:
            if e.code != 429 or attempt == 7:
                raise
            time.sleep(60 * (attempt + 1))


def clean(v):
    v = re.sub(r"<[^>]+>", "", v or "")
    return re.sub(r"\s+", " ", v).strip()


def allowed(lic):
    l = lic.lower()
    if "nc" in l.split() or "-nc" in l or " nc" in l or "nd" in l.replace("and", "").split("-"):
        return False
    return any(k in l for k in ("public domain", "pd", "cc0", "cc by"))


def slug(title):
    return re.sub(r"[^a-z0-9]+", "_", title.lower()).strip("_")[:48]


def duration(path):
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                         capture_output=True, text=True).stdout.strip()
    return float(out)


def music():
    os.makedirs(MUSIC, exist_ok=True)
    os.makedirs(CACHE, exist_ok=True)
    tracks = []
    for file, title, ages in PICKS:
        info = next(iter(api({"action": "query", "titles": file, "prop": "imageinfo", "iiprop": "url|extmetadata"})["query"]["pages"].values()))
        if "imageinfo" not in info:
            print("missing:", file)
            continue
        ii = info["imageinfo"][0]
        meta = ii.get("extmetadata", {})
        lic = clean(meta.get("LicenseShortName", {}).get("value", ""))
        if not allowed(lic):
            print(f"skipped {file}: licence {lic!r}")
            continue
        name = slug(title)
        src = os.path.join(CACHE, name + os.path.splitext(ii["url"])[1])
        if not os.path.exists(src):
            data = fetch(ii["url"])
            with open(src, "wb") as f:
                f.write(data)
            time.sleep(20)  # Commons asks for patience with downloads
        out = os.path.join(MUSIC, name + ".ogg")
        length = min(duration(src), MAX_SECONDS)
        fade_out = 6 if length >= MAX_SECONDS else 2
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", src, "-t", f"{length:.2f}", "-vn",
                        "-af", f"loudnorm=I=-20:TP=-2:LRA=11,afade=t=in:d=1.5,afade=t=out:st={length - fade_out:.2f}:d={fade_out}",
                        "-ac", "2", "-ar", "44100", "-c:a", "libvorbis", "-q:a", "0", out], check=True)
        artist = clean(meta.get("Artist", {}).get("value", ""))[:90]
        tracks.append({"file": name + ".ogg", "title": title, "ages": ages, "seconds": round(length),
                       "credit": ", ".join(x for x in (artist, lic) if x) + " · Wikimedia Commons",
                       "source": ii.get("descriptionurl", "")})
        print(f"{title}: {length:.0f}s, {lic}")
    os.makedirs(os.path.dirname(LIST), exist_ok=True)
    with open(LIST, "w", encoding="utf-8") as f:
        json.dump(tracks, f, ensure_ascii=False, indent=1)


def write_wav(path, samples, rate=44100):
    data = (np.clip(samples, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(data.tobytes())


def synth():
    """A small bell for events and a low drum for war, made here (no licence to keep)."""
    rate = 44100
    t = np.arange(int(rate * 2.2)) / rate
    bell = sum(a * np.sin(2 * math.pi * f * t) * np.exp(-t * d)
               for f, a, d in ((660, 0.5, 2.2), (1320 * 1.002, 0.22, 3.5), (1980 * 0.998, 0.12, 5), (2640, 0.06, 7)))
    bell *= np.minimum(1, t / 0.004) * 0.6
    t2 = np.arange(int(rate * 1.4)) / rate
    pitch = 70 * np.exp(-t2 * 3) + 48
    drum = np.sin(2 * math.pi * np.cumsum(pitch) / rate) * np.exp(-t2 * 4.5)
    noise = np.random.default_rng(3).normal(0, 1, len(t2)) * np.exp(-t2 * 30) * 0.15
    drum = (drum + noise) * np.minimum(1, t2 / 0.003) * 0.85
    for name, s in (("bell", bell), ("drum", drum)):
        tmp = os.path.join(CACHE, name + ".wav")
        write_wav(tmp, s, rate)
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", tmp, "-c:a", "libvorbis", "-q:a", "5", os.path.join(UI, name + ".ogg")], check=True)


def ui(kenney):
    os.makedirs(UI, exist_ok=True)
    os.makedirs(CACHE, exist_ok=True)
    found = {}
    for root, _, files in os.walk(kenney):
        for f in files:
            if f.endswith(".ogg"):
                found.setdefault(f[:-4], os.path.join(root, f))
    for name, src in UI_SOUNDS.items():
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", found[src], "-c:a", "libvorbis", "-q:a", "5",
                        os.path.join(UI, name + ".ogg")], check=True)
    synth()
    print("interface sounds written")


if __name__ == "__main__":
    ui(sys.argv[1])
    music()
