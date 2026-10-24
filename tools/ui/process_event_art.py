#!/usr/bin/env python3
"""
Processes the chosen event pictures (tools/ui/event_art.json) into Game/art/events/<slot>.jpg:
downloaded from Wikimedia Commons, cropped to the event frame's shape (12:5, at the focus the
picks give), toned to one style (a warm sepia duotone with lifted blacks, gentle contrast, paper
grain and a vignette) so paintings, woodcuts and photographs read as one series. Credits go to
Data/events/art_credits.json, shown under each picture in the game.

Any picture can be added the same way: an entry in event_art.json with its Commons file
title (it must be public domain or CC0) and, optionally, a focus point.

Usage: tools/ui/process_event_art.py [--only SLOT ...]     (needs Pillow and numpy)
"""
import io
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

import numpy as np
from PIL import Image, ImageFilter, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
OUT = os.path.join(ROOT, "Game", "art", "events")
CREDITS = os.path.join(ROOT, "Data", "events", "art_credits.json")
UA = "PoliticomGame/1.0 (event art; contact via project repository)"
W, H = 1200, 500

SHADOW, MID, LIGHT = np.array([38, 28, 22]), np.array([150, 116, 78]), np.array([240, 224, 192])


def fetch(req, timeout=120):
    """urlopen with patience: Commons answers 429 when asked too quickly."""
    for attempt in range(6):
        try:
            return urllib.request.urlopen(req, timeout=timeout).read()
        except urllib.error.HTTPError as e:
            if e.code != 429 or attempt == 5:
                raise
            time.sleep(60 * (attempt + 1))


def api_info(title):
    q = urllib.parse.urlencode({"action": "query", "titles": title, "prop": "imageinfo", "format": "json",
                                "iiprop": "url|extmetadata|size", "iiurlwidth": 1920})
    req = urllib.request.Request("https://commons.wikimedia.org/w/api.php?" + q, headers={"User-Agent": UA})
    page = next(iter(json.loads(fetch(req, 30))["query"]["pages"].values()))
    return page["imageinfo"][0]


def tone(img, focus=(0.5, 0.45)):
    return tone_to(img, W, H, focus)


def tone_to(img, W, H, focus=(0.5, 0.45)):
    # Crop to 12:5 around the focus.
    img = ImageOps.exif_transpose(img).convert("RGB")
    # Scans carry borders, mounts and captions at the edges: trim them first.
    w, h = img.size
    img = img.crop((int(w * 0.03), int(h * 0.03), int(w * 0.97), int(h * 0.97)))
    w, h = img.size
    target = W / H
    if w / h > target:
        cw, ch = int(h * target), h
    else:
        cw, ch = w, int(w / target)
    x = int(min(max(focus[0] * w - cw / 2, 0), w - cw))
    y = int(min(max(focus[1] * h - ch / 2, 0), h - ch))
    img = img.crop((x, y, x + cw, y + ch)).resize((W, H), Image.LANCZOS)
    # Luminance, auto-levelled with a soft curve.
    g = np.asarray(ImageOps.autocontrast(img.convert("L"), cutoff=1), np.float32) / 255
    g = 0.5 + (g - 0.5) * 1.08
    g = np.clip(g, 0, 1) ** 0.95
    # Duotone through three stops: dark umber, sepia, warm paper.
    lo = SHADOW + (MID - SHADOW) * np.clip(g / 0.55, 0, 1)[..., None]
    hi = MID + (LIGHT - MID) * np.clip((g - 0.55) / 0.45, 0, 1)[..., None]
    rgb = np.where((g < 0.55)[..., None], lo, hi)
    # Paper grain and a vignette.
    rng = np.random.default_rng(7)
    grain = rng.normal(0, 1, (H, W)).astype(np.float32)
    grain = np.asarray(Image.fromarray(((grain * 18) + 128).clip(0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.7)), np.float32) - 128
    rgb += grain[..., None] * 0.45
    yy, xx = np.mgrid[0:H, 0:W]
    r = np.hypot((xx - W / 2) / (W / 2), (yy - H / 2) / (H / 2))
    rgb *= (1 - 0.38 * np.clip(r - 0.55, 0, 1) ** 1.4)[..., None]
    # Darker toward the foot, where the title sits.
    rgb *= (1 - 0.35 * np.clip((yy / H - 0.62) / 0.38, 0, 1))[..., None]
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))


def clean(v):
    import re
    v = re.sub(r"<[^>]+>", "", v or "")
    v = re.sub(r"date QS:\S+", "", v)  # Wikidata markup inside dates
    return re.sub(r"\s+", " ", v).strip()


def main():
    picks = json.load(open(os.path.join(HERE, "event_art.json")))
    only = sys.argv[sys.argv.index("--only") + 1:] if "--only" in sys.argv else None
    os.makedirs(OUT, exist_ok=True)
    credits = json.load(open(CREDITS)) if os.path.exists(CREDITS) else {}
    for slot, pick in picks.items():
        if only and slot not in only:
            continue
        if not only and "--force" not in sys.argv and slot in credits and os.path.exists(os.path.join(OUT, slot.replace("#", "-") + ".jpg")):
            continue  # done already
        try:
            process(slot, pick, credits)
        except Exception as e:  # one bad picture shouldn't stop the rest
            print(f"{slot}: failed ({e})")


def process(slot, pick, credits):
    if True:
        info = api_info(pick["file"])
        meta = info.get("extmetadata", {})
        lic = meta.get("LicenseShortName", {}).get("value", "")
        if not any(k in lic.lower() for k in ("public domain", "pd", "cc0", "cc by", "cc-by")) or "nc" in lic.lower() or "nd" in lic.lower().replace("and", ""):
            print(f"{slot}: skipped, licence is {lic!r}")
            return
        # Downloads are cached (tools/ui/.art_cache/, not committed), so re-toning needs no network.
        cache = os.path.join(HERE, ".art_cache", slot.replace("#", "-") + ".bin")
        if not os.path.exists(cache):
            url = info.get("thumburl") or info["url"]
            os.makedirs(os.path.dirname(cache), exist_ok=True)
            data = fetch(urllib.request.Request(url, headers={"User-Agent": UA}))
            Image.open(io.BytesIO(data)).verify()  # never cache an error page
            with open(cache, "wb") as f:
                f.write(data)
            time.sleep(30)
        img = Image.open(cache)
        tone(img, tuple(pick.get("focus", (0.5, 0.45)))).save(os.path.join(OUT, slot.replace("#", "-") + ".jpg"), quality=86, optimize=True)
        artist = clean(meta.get("Artist", {}).get("value"))
        half = len(artist) // 2
        if len(artist) % 2 == 0 and artist[:half] == artist[half:]:
            artist = artist[:half]  # Commons sometimes repeats the field
        if artist.lower().startswith(("unknown", "anonymous")):
            artist = ""
        date = clean(meta.get("DateTimeOriginal", {}).get("value"))
        credits[slot] = {
            "caption": pick.get("caption", ""),
            "credit": ", ".join(x for x in (artist[:80], date[:30]) if x) + f" · {lic} · Wikimedia Commons",
            "source": info.get("descriptionurl", ""),
        }
        print(slot, "ok")
        with open(CREDITS, "w", encoding="utf-8") as f:
            json.dump(credits, f, ensure_ascii=False, indent=1)
        time.sleep(4)


if __name__ == "__main__":
    main()
