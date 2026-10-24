#!/usr/bin/env python3
"""
Lore and a picture for every civilisation on the world maps (Data/world/*.json): a short
history from Wikipedia (CC BY-SA 4.0, credited) and its lead image from Wikimedia Commons when
that image is freely licensed (public domain, CC0, CC BY, CC BY-SA; never non-free or NC/ND).

- Data/lore/lore.json: {name: {title, extract, url, image, credit}}
- Game/art/lore/<slug>.jpg: the picture, toned like the event pictures (3:2, 720 x 480)

Resumable: names already done are skipped; downloads are cached and paced for Wikimedia.

Usage: tools/ui/build_lore.py     (needs Pillow and numpy)
"""
import glob
import hashlib
import io
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from process_event_art import tone_to  # noqa: E402

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
OUT_JSON = os.path.join(ROOT, "Data", "lore", "lore.json")
OUT_IMG = os.path.join(ROOT, "Game", "art", "lore")
CACHE = os.path.join(HERE, ".lore_cache")
UA = "PoliticomGame/1.0 (lore for a strategy game; contact via project repository)"
FREE = ("public domain", "pd", "cc0", "cc by", "cc-by")

# Names in the map data that are groupings or have a better article.
ALIASES = {
    "Hindu kingdoms": "Middle kingdoms of India", "Zhou states": "Zhou dynasty", "Greek city-states": "Polis",
    "Han": "Han dynasty", "Song Empire": "Song dynasty", "Tang Empire": "Tang dynasty", "Ming Chinese Empire": "Ming dynasty",
    "Qing Empire": "Qing dynasty", "UK": "United Kingdom of Great Britain and Ireland", "USSR": "Soviet Union",
    "Rome": "Roman Republic", "Etrurians": "Etruscan civilization", "Carthaginian Empire": "Ancient Carthage",
    "Imperial Japan (Fujiwara)": "Heian period", "Mongol Empire": "Mongol Empire", "Byzantine Empire": "Byzantine Empire",
    "Saba": "Sabaeans", "Tiahuanaco Empire": "Tiwanaku", "Arctic marine mammal hunters": "Paleo-Eskimo",
    "Manchu Empire": "Later Jin (1616–1636)", "Persia": "Qajar Iran", "Milograd culture": "Milograd culture",
    "Alwa": "Alodia", "Oyo": "Oyo Empire", "Silia": "Silla", "Turingians": "Thuringii", "Jin Empire": "Jin dynasty (1115–1234)",
    "Pegu": "Hanthawaddy kingdom", "Papu": "Papuan people", "Glades Culture": "Glades culture", "Marksville Culture": "Marksville culture",
}


def get(url, binary=False, tries=6):
    for attempt in range(tries):
        try:
            data = urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=60).read()
            return data if binary else json.loads(data)
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None
            if e.code != 429 or attempt == tries - 1:
                raise
            time.sleep(30 * (attempt + 1))
    return None


# An article about a people, polity, culture or period: its short description names one of these.
FITS = re.compile(r"\b(people|peoples|ethnic|tribe|tribes|culture|cultures|civili[sz]ation|state|states|kingdom|empire|dynasty|"
                  r"caliphate|sultanate|khanate|khaganate|emirate|principality|duchy|republic|confederation|confederacy|country|"
                  r"nation|polity|realm|monarchy|territory|region|period|era|historical|indigenous|nomad|language|city|island|colony|union|federation)\b", re.I)
NOT = re.compile(r"\b(species|genus|food|dish|deer|plant|animal|film|album|song|band|novel|video game|company|software)\b", re.I)


def summary(title, check=True):
    s = get("https://en.wikipedia.org/api/rest_v1/page/summary/" + urllib.parse.quote(title.replace(" ", "_")))
    if not s or s.get("type") == "disambiguation" or not s.get("extract"):
        return None
    desc = (s.get("description") or "") + " " + s["extract"][:200]
    if check and (NOT.search(s.get("description") or "") or not FITS.search(desc)):
        return None
    return s


DESCRIPTIVE = re.compile(r"hunter|gatherer|forag|farmers|pastoral|herders|fishers|fichers|nomad|peoples|cultures$", re.I)


def find_early(name):
    """The pre-modern polity behind a modern country's name: the Mali Empire, the Kingdom of Benin."""
    for t in [f"{name} Empire", f"Kingdom of {name}", f"{name} Kingdom", f"Empire of {name}", f"Sultanate of {name}", f"History of {name}"]:
        s = summary(t)
        if s and not re.search(r"\b(country|sovereign state|republic)\b", s.get("description") or "", re.I):
            return s
    return find(name)


def find(name):
    """The best article for a civilisation's name: an alias, the name itself, then a search.
    Descriptive names ("Amazon hunter-gatherers") search for the peoples of that region."""
    for t in [ALIASES.get(name), None if DESCRIPTIVE.search(name) else name]:
        if t and (s := summary(t, check=t != ALIASES.get(name))):
            return s
    query = name + (" indigenous peoples" if DESCRIPTIVE.search(name) else " history")
    q = urllib.parse.urlencode({"action": "query", "list": "search", "srsearch": query, "srlimit": 5, "format": "json"})
    res = get("https://en.wikipedia.org/w/api.php?" + q) or {}
    for hit in res.get("query", {}).get("search", []):
        if (s := summary(hit["title"])):
            return s
        time.sleep(0.5)
    return None


def sentences(text, n=3, limit=480):
    parts = re.split(r"(?<=[.!?])\s+(?=[A-Z])", text.strip())
    out = ""
    for p in parts[:n]:
        if len(out) + len(p) > limit and out:
            break
        out += (" " if out else "") + p
    return out


def image_licence(url):
    """(licence, artist) for a Commons image URL, or None if it isn't on Commons or isn't free."""
    if "/wikipedia/commons/" not in url:
        return None  # hosted locally on English Wikipedia: non-free
    name = urllib.parse.unquote(url.split("/")[-1])
    if "px-" in name:
        name = name.split("px-", 1)[1]
    if name.lower().endswith(".svg.png"):
        name = name[:-4]  # a rendered SVG: the file is the SVG
    q = urllib.parse.urlencode({"action": "query", "titles": "File:" + name, "prop": "imageinfo", "iiprop": "extmetadata", "format": "json"})
    # Asked through Wikipedia, which serves Commons' metadata without Commons' rate limits.
    res = get("https://en.wikipedia.org/w/api.php?" + q) or {}
    pages = res.get("query", {}).get("pages", {})
    for p in pages.values():
        meta = (p.get("imageinfo") or [{}])[0].get("extmetadata", {})
        lic = meta.get("LicenseShortName", {}).get("value", "")
        low = lic.lower()
        if any(k in low for k in FREE) and " nc" not in low and "-nc" not in low and " nd" not in low and "-nd" not in low:
            artist = re.sub(r"<[^>]+>", "", meta.get("Artist", {}).get("value", "")).strip()
            return lic, re.sub(r"\s+", " ", artist)[:80]
    return None


def slug(name):
    s = re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")[:40]
    return s + "_" + hashlib.md5(name.encode()).hexdigest()[:6]


def images(lore):
    """The slow pass: pictures for entries that have lore but no picture yet."""
    todo = [n for n, e in lore.items() if e.get("src") and not e.get("image") and not e.get("noimage")]
    print(f"{len(todo)} pictures to fetch", flush=True)
    for i, name in enumerate(todo):
        e = lore[name]
        src = e["src"]
        try:
            if "flag_of" in src.lower() or src.lower().endswith(".gif") or not (lic := image_licence(src)):
                e["noimage"] = True
                continue
            thumb = e.get("thumb") or src
            thumb = re.sub(r"/\d+px-", "/960px-", thumb) if "/thumb/" in thumb else thumb
            cache = os.path.join(CACHE, slug(name) + ".bin")
            if not os.path.exists(cache):
                data = get(thumb, binary=True)
                Image.open(io.BytesIO(data)).verify()
                open(cache, "wb").write(data)
                time.sleep(8)
            tone_to(Image.open(cache), 720, 480).save(os.path.join(OUT_IMG, slug(name) + ".jpg"), quality=84, optimize=True)
            e["image"] = slug(name)
            e["credit"] = ", ".join(x for x in (lic[1],) if x) + f" · {lic[0]} · Wikimedia Commons"
        except Exception as ex:
            print(name, "image failed:", ex, flush=True)
        if i % 10 == 0:
            json.dump(lore, open(OUT_JSON, "w"), ensure_ascii=False, indent=0)
            print(f"{i + 1}/{len(todo)} {name}{' +image' if e.get('image') else ''}", flush=True)
    json.dump(lore, open(OUT_JSON, "w"), ensure_ascii=False, indent=0)
    print("pictures done:", sum(1 for v in lore.values() if v.get("image")), flush=True)


def main():
    names = {}
    eras = {}
    for f in sorted(glob.glob(os.path.join(ROOT, "Data", "world", "*.json"))):
        era = int(os.path.basename(f)[:-5])
        for s in json.load(open(f))["states"]:
            if len(s["cities"]) >= 7:
                names.setdefault(s["name"], 0)
                names[s["name"]] += 1
                eras.setdefault(s["name"], set()).add(era)
    # A name used both before 1800 and after (Mali, Benin) gets an early entry, "Name@early".
    for n, es in list(eras.items()):
        if min(es) < 1800 and max(es) >= 1800:
            names[n + "@early"] = names[n]
    os.makedirs(OUT_IMG, exist_ok=True)
    os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
    os.makedirs(CACHE, exist_ok=True)
    lore = json.load(open(OUT_JSON)) if os.path.exists(OUT_JSON) else {}
    if "--images" in sys.argv:
        return images(lore)
    todo = [n for n in sorted(names, key=lambda n: -names[n]) if n not in lore]
    print(f"{len(names)} civilisations, {len(todo)} to do", flush=True)
    for i, name in enumerate(todo):
        try:
            s = find_early(name[:-6]) if name.endswith("@early") else find(name)
        except Exception as e:
            print(name, "failed:", e, flush=True)
            continue
        entry = {"title": "", "extract": "", "url": "", "image": "", "credit": ""}
        if s:
            entry.update(title=s["title"], extract=sentences(s["extract"]), url=s.get("content_urls", {}).get("desktop", {}).get("page", ""))
            src = (s.get("originalimage") or s.get("thumbnail") or {}).get("source", "").split("?")[0]
            entry["src"] = src
            entry["thumb"] = (s.get("thumbnail") or {}).get("source", src).split("?")[0]
            if "--text" not in sys.argv and src and "flag_of" not in src.lower() and not src.lower().endswith(".gif"):
                try:
                    lic = image_licence(src)
                    if lic:
                        thumb = (s.get("thumbnail") or {}).get("source", src).split("?")[0]
                        thumb = re.sub(r"/\d+px-", "/960px-", thumb) if "/thumb/" in thumb else thumb
                        cache = os.path.join(CACHE, slug(name) + ".bin")
                        if not os.path.exists(cache):
                            data = get(thumb, binary=True)
                            Image.open(io.BytesIO(data)).verify()
                            open(cache, "wb").write(data)
                            time.sleep(6)
                        img = Image.open(cache)
                        tone_to(img, 720, 480).save(os.path.join(OUT_IMG, slug(name) + ".jpg"), quality=84, optimize=True)
                        entry["image"] = slug(name)
                        entry["credit"] = ", ".join(x for x in (lic[1],) if x) + f" · {lic[0]} · Wikimedia Commons"
                except Exception as e:
                    print(name, "image failed:", e, flush=True)
        lore[name] = entry
        if i % 10 == 0:
            json.dump(lore, open(OUT_JSON, "w"), ensure_ascii=False, indent=0)
            print(f"{i + 1}/{len(todo)} {name} -> {entry['title'] or '—'}{' +image' if entry['image'] else ''}", flush=True)
        time.sleep(0 if "--text" in sys.argv else 1)
    json.dump(lore, open(OUT_JSON, "w"), ensure_ascii=False, indent=0)
    print("done", sum(1 for v in lore.values() if v["extract"]), "with lore,", sum(1 for v in lore.values() if v["image"]), "with pictures", flush=True)


if __name__ == "__main__":
    main()
