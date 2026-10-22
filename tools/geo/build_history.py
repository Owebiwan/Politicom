#!/usr/bin/env python3
"""
Builds Data/world/<era>.json for the ages before (and after) the hand-made eras, from
aourednik/historical-basemaps (GPL-3.0, https://github.com/aourednik/historical-basemaps).

Each snapshot's features are grouped by the state that rules them (SUBJECTO), so colonies and
provinces belong to their empire. Borders are traced from edges shared by different states;
coasts come from Natural Earth land. Cities are Natural Earth populated places inside each
state's outline, so they carry modern names (Istanbul, not Byzantion): an approximation the
README notes. Power is estimated from area; ideology from the era and the state's name.

Usage:
  tools/geo/build_history.py HISTORY_DIR NATURAL_EARTH_DIR OUTPUT_DIR

HISTORY_DIR holds world_<year>.geojson from historical-basemaps (see tools/geo/README.md);
NATURAL_EARTH_DIR holds ne_50m_land.geojson and ne_10m_populated_places_simple.geojson.
"""
import collections
import hashlib
import json
import math
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from build_geo import simplify, area, rings  # noqa: E402
from build_world import chain, key, PALETTE  # noqa: E402

TOL = 0.04
MIN_AREA = 0.02            # square degrees: drop specks (but keep a state's largest piece)
MIN_STATE_KM2 = 25_000     # smaller polities become neutral land, except famous ones
ALWAYS = {"Rome", "Etrurians", "Greek city-states", "Venice", "Genoa", "Papal States", "Swiss Confederation", "Switzerland",
          "Florence", "Milan", "Novgorod", "Hansa", "Teutonic Order", "Athens", "Sparta", "Dutch Republic", "Netherlands", "Portugal"}
MAX_STATES = 150
CITIES = 40

# era id (historical year, negative = BC) → (source file, description)
ERAS = {
    "-500": ("world_bc500", "500 BC: the Persian Empire at its height, Greek city-states and Rome a young republic, the kingdoms of the Ganges, Zhou China in its Spring and Autumn."),
    "1": ("world_bc1", "AD 1: Rome under Augustus, Parthia, the Han dynasty, the Kushan and Satavahana kingdoms."),
    "500": ("world_500", "AD 500: the West Roman Empire is gone; Byzantium, Sasanian Persia, the Gupta and the Northern and Southern dynasties of China."),
    "800": ("world_800", "AD 800: Charlemagne's empire, the Abbasid Caliphate, Tang China, the Byzantine Empire."),
    "1000": ("world_1000", "AD 1000: the Holy Roman Empire, Kievan Rus', the Fatimid and Abbasid caliphates, Song China, the Chola Empire."),
    "1200": ("world_1200", "AD 1200: the Angevin Empire, the crusader states, the Ayyubids, the Jin and Song, on the eve of the Mongol conquests."),
    "1400": ("world_1400", "AD 1400: after the Black Death; the Timurids, the Ming, the Ottomans rising, the Kingdom of France in the Hundred Years' War."),
    "1500": ("world_1500", "AD 1500: Spain and Portugal sail out, the Ottomans hold the Balkans, the Aztec and Inca empires, the Ming."),
    "1650": ("world_1650", "AD 1650: after the Thirty Years' War; the Qing conquest of China, the Mughals, the English Commonwealth."),
    "1783": ("world_1783", "AD 1783: the United States wins independence; the age of enlightened absolutism on the eve of the French Revolution."),
    "1815": ("world_1815", "AD 1815: the Congress of Vienna restores the old order after Napoleon; Latin America fights for independence."),
    "1960": ("world_1960", "AD 1960: the Cold War and decolonization; two blocs, and dozens of new states in Africa and Asia."),
    "2000": ("world_2000", "AD 2000: after the Cold War; a world of nation-states, markets and the internet."),
}

# Known slips in the source: (era, feature NAME) → the state that actually ruled it.
FIXES = {
    ("1400", "Great Khanate"): "Ming Chinese Empire",  # the Ming had ruled China since 1368
}

# Where the largest modern city inside an outline is clearly not the seat of power.
CAPITALS = {
    "Rome": "Rome", "Roman Empire": "Rome", "Achaemenid Empire": "Shiraz", "Byzantine Empire": "Istanbul",
    "Abbasid Caliphate": "Baghdad", "Holy Roman Empire": "Aachen", "Ottoman Empire": "Istanbul",
    "France": "Paris", "Kingdom of France": "Paris", "England": "London", "Kievan Rus": "Kiev",
    "Carthaginian Empire": "Tunis", "Parthian Empire": "Baghdad", "Sasanian Empire": "Baghdad",
    "Mughal Empire": "Delhi", "Spain": "Madrid", "Portugal": "Lisbon", "Venice": "Venice",
    "United States": "Washington, D.C.", "United States of America": "Washington, D.C.", "Inca Empire": "Cusco",
    "Aztec Empire": "Mexico City", "Russia": "Moscow", "Muscovy": "Moscow", "Russian Empire": "Saint Petersburg",
    "Han Empire": "Xian", "Tang Empire": "Xian", "Ming Chinese Empire": "Beijing", "Qing Empire": "Beijing",
    "Brazil": "Brasilia", "Australia": "Canberra", "Canada": "Ottawa", "India": "New Delhi", "China": "Beijing",
    "Denmark-Norway": "Copenhagen", "Denmark": "Copenhagen", "Sweden": "Stockholm", "UK": "London", "United Kingdom": "London",
    "Netherlands": "Amsterdam", "Dutch Republic": "Amsterdam", "Dutch East Indies": "Jakarta", "Poland": "Warsaw",
    "Poland-Lithuania": "Warsaw", "Prussia": "Berlin", "Bavaria": "Munich", "Korea": "Seoul", "Đại Việt": "Hanoi",
    "Zanzibar": "Zanzibar", "Algiers": "Algiers", "Hausa States": "Kano", "Mahratta states": "Pune", "Zhou states": "Luoyang",
    "Kara Khitai Khaganate": "Bishkek", "Principality of Galicia-Volhynia": "Lviv", "Armenia": "Yerevan", "Buwayhid Emirates": "Baghdad",
    "Austrian Empire": "Vienna", "Habsburg Empire": "Vienna", "Austria": "Vienna", "Kingdom of Hungary": "Budapest",
    "Mongol Empire": "Ulaanbaatar", "Golden Horde": "Volgograd", "Timurid Empire": "Samarkand", "Ayyubid Caliphate": "Cairo",
    "Fatimid Caliphate": "Cairo", "Mamluke Sultanate": "Cairo", "Mamluk Sultanate": "Cairo", "Umayyad Caliphate": "Damascus",
    "Gupta Empire": "Patna", "Maurya Empire": "Patna", "Magadha": "Patna", "Kushan Empire": "Peshawar", "Song Empire": "Kaifeng",
    "Frankish Kingdom": "Paris", "Carolingian Empire": "Aachen", "Angevin Empire": "London", "England and Ireland": "London",
    "Great Britain": "London", "Scotland": "Edinburgh", "Castilla": "Toledo", "Aragón": "Zaragoza", "Mali": "Timbuktu",
    "Songhai": "Gao", "Ethiopia": "Addis Ababa", "Japan": "Tokyo", "Imperial Japan (Fujiwara)": "Kyoto", "Delhi Sultanate": "Delhi",
}

# Peoples without a state, whose names carry no telling word.
PEOPLES = {"Khoisan", "Saami", "Sámi", "Ainu", "Paleo-Inuit", "Inupiaq", "Inuit", "Dorset", "Thule", "Athabaskan", "Athabascan",
           "Innu", "Boethuk", "Beothuk", "Taino", "Papuans", "Austronesians", "Malays", "Oromo", "Māori", "Maori", "Illyrians",
           "Celtic kingdoms", "Arawaks", "Caribs", "Guanches", "Bantu peoples", "Yup'ik & Cup'ik", "T'atsaot'ine", "Rupert's Land",
           "Mapuche", "Tupi", "Guarani", "Scythians", "Sarmatians", "Xiongnu", "Germanic tribes", "Slavs", "Huns", "Avars"}

COMMUNIST_1960 = {"USSR", "Union of Soviet Socialist Republics", "China", "People's Republic of China", "Mongolia", "North Korea",
                  "Korea, Democratic People's Republic of", "North Vietnam", "Vietnam, Democratic Republic of", "Cuba", "Poland", "Hungary",
                  "Czechoslovakia", "Romania", "Bulgaria", "Albania", "Yugoslavia", "East Germany", "German Democratic Republic"}
COMMUNIST_2000 = {"China", "Cuba", "Vietnam", "Laos", "North Korea", "Korea, Democratic People's Republic of"}
FASCIST_1960 = {"Spain", "Portugal", "Paraguay", "Dominican Republic", "Nicaragua", "South Africa"}
MONARCHY_MODERN = {"Saudi Arabia", "Morocco", "Jordan", "Oman", "Bhutan", "Nepal", "Brunei", "Swaziland", "Eswatini", "Kuwait",
                   "Qatar", "United Arab Emirates", "Bahrain", "Thailand", "Ethiopia", "Iran", "Libya", "Afghanistan", "Yemen", "Tonga"}
THEOCRACY_MODERN = {"Iran", "Vatican", "Vatican City"}


def region_people(year, lon, lat):
    """Stateless peoples by region and era, where names alone don't tell."""
    if year < 1850 and 110 <= lon <= 156 and -45 <= lat <= -10:      # Aboriginal Australia
        return True
    if year < 1500 and lon < -30:                                     # the Americas before contact
        return True
    if year < 1800 and lon < -50 and lat > 30:                        # North America beyond the colonies
        return True
    if year < 1700 and lat > 55 and lon > 60:                         # Siberia
        return True
    return False


def ideology(year, name, lon=0.0, lat=0.0):
    n = name.lower()
    if year >= 1950:
        if name in THEOCRACY_MODERN and year >= 1980:
            return "theocracy"
        if name in (COMMUNIST_1960 if year < 1990 else COMMUNIST_2000):
            return "communist"
        if year < 1990 and name in FASCIST_1960:
            return "fascist"
        if name in MONARCHY_MODERN:
            return "monarchy"
        return "liberal"
    if name in PEOPLES:
        return "tribal"
    if re.search(r"hunter|gatherer|forag|culture|peoples|tribes|farmers|pastoralists|fishers|fichers|nomad|herders|chiefdom|confederacy|clans", n):
        return "tribal"
    if re.search(r"papal|church|caliphate|imamate|teutonic|order|holy see|tibet|theocra|ecclesiastic|bishopric|archbishop", n):
        return "theocracy"
    if re.search(r"empire|khanate|sultanate|shogunate|dynasty|raj|mughal|qing|ming|tang|song|han\b|horde", n):
        return "empire"
    if region_people(year, lon, lat) and not re.search(r"spanish|british|french|dutch|portug|new |colony|viceroy|company|united states|mexic|inca|aztec|maya|chim", n):
        return "tribal"
    if re.search(r"republic|city-states|united states|commonwealth|confederation|swiss|venice|genoa|novgorod|hanse|netherlands|provinces", n):
        return "liberal" if year >= 1776 else "conservative"
    return "monarchy"


def slug(name, taken):
    words = [w for w in re.sub(r"[^A-Za-z ]", " ", name).upper().split()
             if w not in {"THE", "OF", "AND", "KINGDOM", "EMPIRE", "STATE", "STATES", "REPUBLIC", "CALIPHATE", "SULTANATE", "KHANATE", "DYNASTY"}]
    base = (words[0] if words else "STATE")[:10]
    if len(words) > 1 and base in taken:
        base = (words[0][:5] + words[1][:5])
    sid, i = base, 2
    while sid in taken:
        sid, i = f"{base}{i}", i + 1
    taken.add(sid)
    return sid


def km2(ring):
    lat = sum(y for _, y in ring) / len(ring)
    return area(ring) * 111.32 * 111.32 * math.cos(math.radians(lat))


def contains(ring, x, y):
    inside = False
    j = len(ring) - 1
    for i in range(len(ring)):
        xi, yi = ring[i]
        xj, yj = ring[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside = not inside
        j = i
    return inside


def color(name):
    return PALETTE[int(hashlib.md5(name.encode()).hexdigest(), 16) % len(PALETTE)]


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    hist, ne, out_dir = sys.argv[1:]
    os.makedirs(out_dir, exist_ok=True)
    land = json.load(open(os.path.join(ne, "ne_50m_land.geojson"), encoding="utf-8"))["features"]
    places = [p["properties"] for p in json.load(open(os.path.join(ne, "ne_10m_populated_places_simple.geojson"), encoding="utf-8"))["features"]]
    land_rings = []
    for f in land:
        for r in rings(f["geometry"]):
            s = simplify([tuple(p[:2]) for p in r], TOL)
            if max(y for _, y in s) < -55:
                continue
            if len(s) >= 3 and area(s) >= MIN_AREA:
                land_rings.append([[round(x, 3), round(y, 3)] for x, y in s])

    for era, (source, note) in ERAS.items():
        year = int(era)
        features = json.load(open(os.path.join(hist, source + ".geojson"), encoding="utf-8"))["features"]
        groups = collections.defaultdict(list)
        for f in features:
            p = f["properties"]
            name = (p.get("SUBJECTO") or p.get("NAME") or "").strip()
            name = FIXES.get((era, (p.get("NAME") or "").strip()), name)
            if not name or f["geometry"] is None:
                continue
            groups[name].extend(rings(f["geometry"]))
        # The largest polities, by area.
        sized = []
        for name, rs in groups.items():
            total = sum(km2(r) for r in rs if len(r) >= 3)
            if total >= MIN_STATE_KM2 or name in ALWAYS:
                sized.append((total, name, rs))
        sized.sort(reverse=True)
        sized = sized[:MAX_STATES]

        # Borders: edges shared by different states (the source is topologically clean in most places).
        owners = collections.defaultdict(set)
        for _, name, rs in sized:
            for r in rs:
                for a, b in zip(r, r[1:]):
                    owners[tuple(sorted([key(a), key(b)]))].add(name)
        border_edges, neighbours = [], collections.defaultdict(set)
        for edge, names in owners.items():
            if len(names) > 1:
                border_edges.append(edge)
                for a in names:
                    neighbours[a].update(b for b in names if b != a)
        # Neighbours across small gaps (rivers, slivers): vertices within ~0.15°.
        grid = collections.defaultdict(set)
        for _, name, rs in sized:
            for r in rs:
                for x, y in r[::3]:
                    grid[(int(x // 0.15), int(y // 0.15))].add(name)
        for (gx, gy), names in grid.items():
            near = set(names)
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    near |= grid.get((gx + dx, gy + dy), set())
            for a in names:
                neighbours[a].update(b for b in near if b != a)
        borders = [[[round(x, 3), round(y, 3)] for x, y in simplify(line, TOL)] for line in chain(border_edges)]
        borders = [b for b in borders if len(b) >= 2]

        taken, out_states = set(), []
        ids = {}
        for _, name, rs in sized:
            ids[name] = slug(name, taken)
        shapes = []
        for total, name, rs in sized:
            pieces = []
            for r in rs:
                simp = simplify([tuple(p[:2]) for p in r], TOL)
                if len(simp) >= 3:
                    pieces.append(simp)
            pieces.sort(key=area, reverse=True)
            pieces = [p for j, p in enumerate(pieces) if j == 0 or area(p) >= MIN_AREA]
            if pieces:
                shapes.append((total, name, pieces))

        # Cities: inside an outline, or (the source's coasts are coarse, so Lisbon and Copenhagen
        # fall just offshore) the nearest outline within ~35 km.
        boxes = [[(min(x for x, _ in p), min(y for _, y in p), max(x for x, _ in p), max(y for _, y in p)) for p in pieces] for _, _, pieces in shapes]
        vertex_grid = collections.defaultdict(list)
        for k, (_, _, pieces) in enumerate(shapes):
            for p in pieces:
                for x, y in p:
                    vertex_grid[(int(x // 0.35), int(y // 0.35))].append((x, y, k))
        city_lists = [[] for _ in shapes]
        for c in places:
            x, y = c["longitude"], c["latitude"]
            owner = next((k for k, (_, _, pieces) in enumerate(shapes)
                          if any(x0 <= x <= x1 and y0 <= y <= y1 and contains(p, x, y) for (x0, y0, x1, y1), p in zip(boxes[k], pieces))), None)
            if owner is None:
                gx, gy = int(x // 0.35), int(y // 0.35)
                best = 0.35 ** 2
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        for vx, vy, k in vertex_grid.get((gx + dx, gy + dy), ()):
                            d = (vx - x) ** 2 + (vy - y) ** 2
                            if d < best:
                                best, owner = d, k
            if owner is not None:
                city_lists[owner].append(c)

        for (total, name, pieces), cities in zip(shapes, city_lists):
            cities.sort(key=lambda c: -(c.get("pop_max") or 0))
            want = CAPITALS.get(name)
            capital = next((c for c in cities if want and (c["name"] == want or c.get("nameascii") == want)), cities[0] if cities else None)
            big = max(pieces, key=area)
            lon, lat = sum(x for x, _ in big) / len(big), sum(y for _, y in big) / len(big)
            kind = ideology(year, name, lon, lat)
            power = 1 + 1.6 * math.log2(max(total, 100_000) / 100_000)
            if kind == "tribal":
                power *= 0.4  # land is not armies
            power = max(1, min(10, round(power)))
            out_states.append({
                "id": ids[name],
                "name": name,
                "ideology": kind,
                "power": power,
                "neighbours": sorted(ids[n] for n in neighbours[name] if n in ids),
                "color": color(name),
                "capital": {"name": re.sub(r"\s+", " ", capital["name"]), "lon": round(capital["longitude"], 3), "lat": round(capital["latitude"], 3)} if capital else None,
                "cities": [{"name": re.sub(r"\s+", " ", c["name"]), "lon": round(c["longitude"], 3), "lat": round(c["latitude"], 3), "pop": int(c.get("pop_max") or 0)} for c in cities[:CITIES]],
                "rings": [[[round(x, 3), round(y, 3)] for x, y in p] for p in pieces],
            })
        present = {st["id"] for st in out_states}
        for st in out_states:
            st["neighbours"] = [n for n in st["neighbours"] if n in present]
        out = {"era": era, "note": note + " Borders from historical-basemaps (GPL-3.0); cities carry modern names.",
               "land": land_rings, "borders": borders, "states": out_states}
        path = os.path.join(out_dir, era + ".json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, separators=(",", ":"))
        kinds = collections.Counter(s["ideology"] for s in out_states)
        print(f"{era}: {len(out_states)} states, {len(borders)} border lines, {os.path.getsize(path) // 1024} KB, {dict(kinds)}")


if __name__ == "__main__":
    main()
