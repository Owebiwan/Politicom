#!/usr/bin/env python3
"""
Finds candidate pictures for the event frames on Wikimedia Commons: for each slot (a theme in
an era) it searches the file namespace, keeps only images whose licence metadata says public
domain (or CC0) and that are landscape and large enough, and downloads small previews with
their metadata, for a person to choose from. The chosen ones go in tools/ui/event_art.json and
are processed by process_event_art.py.

Usage: tools/ui/find_event_art.py OUT_DIR [SLOT ...]
"""
import json
import os
import sys
import time
import urllib.parse
import urllib.request

API = "https://commons.wikimedia.org/w/api.php"
UA = "PoliticomGame/1.0 (event art research; contact via project repository)"

# slot: search queries, best first
SLOTS = {
    "war_ancient": ["Battle of Zama painting", "Battle of Issus mosaic", "Roman soldiers battle painting 19th century"],
    "war_medieval": ["Froissart battle miniature", "medieval battle manuscript illumination"],
    "war_earlymodern": ["Thirty Years War battle painting", "pikemen battle 16th century painting"],
    "war_industrial": ["Franco-Prussian War battle painting", "1848 barricade battle painting"],
    "war_modern": ["Russian Civil War 1919 soldiers photograph", "World War I infantry advance photograph"],
    "uprising_ancient": ["Spartacus slaves revolt illustration", "Death of Tiberius Gracchus", "secession plebs Mons Sacer", "Spartacus Hermann Vogel", "slave revolt ancient Rome painting"],
    "uprising_medieval": ["Jacquerie Froissart miniature", "Peasants Revolt 1381 Froissart"],
    "uprising_earlymodern": ["German Peasants War woodcut", "peasants war 1525 woodcut"],
    "uprising_industrial": ["Paris Commune barricade 1871 photograph", "barricade 1848 painting"],
    "uprising_modern": ["Petrograd demonstration 1917 photograph", "July Days 1917 Petrograd photograph"],
    "assembly_ancient": ["Roman senate Maccari", "Athenian assembly engraving"],
    "assembly_medieval": ["medieval town council miniature", "Swiss Landsgemeinde painting"],
    "assembly_earlymodern": ["Tennis Court Oath David", "Estates General 1789 painting"],
    "assembly_industrial": ["workers meeting 19th century engraving", "International Workingmen's Association congress"],
    "assembly_modern": ["Petrograd Soviet 1917 meeting photograph", "workers council meeting 1918 photograph"],
    "hunger_medieval": ["famine medieval miniature", "bread distribution medieval"],
    "hunger_industrial": ["Irish famine Illustrated London News engraving", "famine engraving 19th century"],
    "hunger_modern": ["breadline 1930s photograph Library of Congress", "soup kitchen 1919 photograph"],
    "harvest_ancient": ["Tomb of Menna harvest", "Nakht tomb agriculture painting", "Roman mosaic agricultural scenes"],
    "harvest_medieval": ["Tres Riches Heures harvest", "medieval harvest miniature"],
    "harvest_earlymodern": ["Bruegel The Harvesters", "Bruegel harvest painting"],
    "harvest_industrial": ["Millet The Gleaners", "harvest 19th century painting peasants"],
    "work_medieval": ["Mendel Housebook craftsman", "Hausbuch der Mendelschen Zwölfbrüderstiftung", "medieval masons building miniature"],
    "work_industrial": ["Menzel Iron Rolling Mill", "The Strike Robert Koehler"],
    "work_modern": ["factory workers 1930s photograph", "Lewis Hine factory photograph"],
    "faith_ancient": ["Roman sacrifice painting 19th century", "Delphi oracle painting Pythia", "Vestal virgins painting"],
    "faith_medieval": ["medieval pilgrims miniature", "medieval preaching miniature"],
    "faith_earlymodern": ["Savonarola preaching painting", "Inquisition auto da fe painting"],
    "plague_medieval": ["Burying plague victims Tournai", "Black Death Tournai Gilles Li Muisis", "plague 1349 burial miniature"],
    "plague_earlymodern": ["Plague of Ashdod Poussin", "plague 17th century painting"],
    "fire": ["Great Fire of London painting", "city fire 18th century painting"],
    "flood": ["Saint Elizabeth flood painting", "flood 19th century engraving"],
    "refugees": ["refugees 1914 photograph", "refugees engraving 19th century"],
    "festival_earlymodern": ["Bruegel Peasant Dance", "Bruegel Kermesse"],
    "festival_industrial": ["village festival 19th century painting", "Fête de la Fédération 1790"],
    "trade_earlymodern": ["Massys Moneylender and his Wife", "Dutch harbour 17th century painting"],
    "trade_industrial": ["stock exchange 19th century engraving", "harbour steamships 19th century painting"],
    "press_earlymodern": ["Buchdrucker Jost Amman Ständebuch", "Impressio librorum Stradanus", "Gutenberg printing press engraving 19th century"],
    "press_industrial": ["newspaper printing press 19th century engraving", "newsroom 1900 photograph"],
    "town_ancient": ["Roman forum reconstruction painting", "ancient Rome reconstruction painting", "Athens acropolis reconstruction painting"],
    "town_medieval": ["medieval town miniature", "Nuremberg Chronicle city woodcut"],
    "town_industrial": ["19th century city street engraving", "Paris street 1870 photograph"],
    "town_modern": ["Barcelona 1930s photograph street", "city street 1920s photograph"],
    "war_contemporary": ["nuclear test photograph", "Operation Crossroads photograph"],
    "press_contemporary": ["ENIAC computer photograph", "television studio 1950s photograph"],
    "uprising_contemporary": ["civil rights march 1963 photograph", "protest march 1960s photograph"],
}

# Free licences: public domain, CC0, and CC BY / CC BY-SA (any use, with the credit the game shows).
OK_LICENCES = ("public domain", "pd", "cc0", "cc by", "cc-by")


def api(params):
    url = API + "?" + urllib.parse.urlencode({**params, "format": "json"})
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.load(r)


def search(query, limit=12):
    data = api({
        "action": "query", "generator": "search", "gsrnamespace": 6, "gsrsearch": query, "gsrlimit": limit,
        "prop": "imageinfo", "iiprop": "url|size|extmetadata|mime", "iiurlwidth": 500,  # a standard thumbnail size (Commons asks for these)
    })
    pages = sorted(data.get("query", {}).get("pages", {}).values(), key=lambda p: p.get("index", 99))
    out = []
    for p in pages:
        info = (p.get("imageinfo") or [{}])[0]
        meta = info.get("extmetadata", {})
        lic = (meta.get("LicenseShortName", {}).get("value") or "").lower()
        if not any(lic.startswith(k) or k in lic for k in OK_LICENCES):
            continue
        if info.get("mime") not in ("image/jpeg", "image/png", "image/tiff"):
            continue
        w, h = info.get("width", 0), info.get("height", 1)
        if w < 800 or w / h < 1.15:
            continue
        def clean(v):
            import re
            return re.sub(r"<[^>]+>", "", v or "").strip()
        out.append({
            "title": p["title"],
            "page": info.get("descriptionurl"),
            "thumb": info.get("thumburl"),
            "url": info.get("url"),
            "width": w, "height": h,
            "licence": meta.get("LicenseShortName", {}).get("value"),
            "artist": clean(meta.get("Artist", {}).get("value")),
            "date": clean(meta.get("DateTimeOriginal", {}).get("value")),
            "description": clean(meta.get("ImageDescription", {}).get("value"))[:200],
        })
    return out


def main():
    out = sys.argv[1]
    slots = sys.argv[2:] or list(SLOTS)
    os.makedirs(out, exist_ok=True)
    index = os.path.join(out, "candidates.json")
    found = json.load(open(index)) if os.path.exists(index) else {}
    for slot in slots:
        cands = []
        for q in SLOTS[slot]:
            try:
                cands += [c for c in search(q) if c["title"] not in {x["title"] for x in cands}]
            except Exception as e:  # network hiccup: carry on with what there is
                print(slot, q, "failed:", e)
            time.sleep(4)  # Commons rate-limits quick searches
            if len(cands) >= 4:
                break
        cands = cands[:4]
        for i, c in enumerate(cands):
            path = os.path.join(out, f"{slot}_{i}.jpg")
            req = urllib.request.Request(c["thumb"], headers={"User-Agent": UA})
            try:
                with urllib.request.urlopen(req, timeout=60) as r, open(path, "wb") as f:
                    f.write(r.read())
                c["preview"] = path
            except Exception as e:
                print(slot, "download failed:", e)
            time.sleep(2)
        found[slot] = cands
        print(slot, len(cands))
        with open(index, "w") as f:  # after every slot, so an interrupted run keeps what it found
            json.dump(found, f, indent=1)


if __name__ == "__main__":
    main()
