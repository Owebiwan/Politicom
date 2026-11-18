"""
Fetches the CC0 assets the event art is rendered with from Poly Haven (polyhaven.com, CC0: no
attribution required, credited anyway in Game/art/events/ATTRIBUTION.md): sky HDRIs, PBR
textures for walls, roofs, paving and woodwork, and props (barrels, crates, tables, chairs,
benches, baskets, bottles, lamps). Into tools/art/.assets/ (not committed), skipping what is
already there.

Usage:
  python3 tools/art/fetch_assets.py
"""
import json
import os
import sys
import time
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, ".assets")
API = "https://api.polyhaven.com"

HDRIS = ["belfast_sunset_puresky", "qwantani_dusk_2_puresky", "kloppenheim_06_puresky", "kloofendal_overcast_puresky",
         "kloofendal_48d_partly_cloudy_puresky"]
TEXTURES = [
    # Walls
    "white_plaster_rough_01", "clay_plaster", "plastered_stone_wall", "old_stone_wall", "yellow_plaster", "worn_plaster_wall",
    "white_stucco", "red_brick_plaster_patch_02", "brick_wall_005", "grey_plaster", "painted_plaster_wall", "yellow_plaster_02",
    "large_sandstone_blocks",
    # Roofs
    "clay_roof_tiles", "clay_roof_tiles_02", "roof_slates_02", "roof_slates_03", "red_slate_roof_tiles_01",
    # Paving
    "red_sandstone_pavement", "cobblestone_floor_04", "cobblestone_square", "square_cobblestone", "cobblestone_pavement",
    # Woodwork
    "dark_wooden_planks", "wood_shutter", "rough_pine_door", "weathered_planks",
]
MODELS = ["wooden_barrels_01", "wine_barrel_01", "old_military_crate", "painted_wooden_table", "painted_wooden_bench",
          "painted_wooden_chair_01", "gallinera_table", "gallinera_chair", "wicker_basket_01", "wine_bottles_01",
          "street_lamp_01", "vintage_oil_lamp", "jug_01", "wooden_bucket_01", "round_wooden_table_01"]
MAPS = {"diff": "Diffuse", "nor_gl": "nor_gl", "rough": "Rough", "disp": "Displacement"}


def get(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for attempt in range(4):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "Politicom-art/1.0"})
            with urllib.request.urlopen(req, timeout=60) as r, open(path + ".part", "wb") as f:
                f.write(r.read())
            os.replace(path + ".part", path)
            return
        except Exception as e:  # network hiccups: back off and retry
            print("  retry", url, e, file=sys.stderr)
            time.sleep(2 + attempt * 3)
    raise RuntimeError(f"could not fetch {url}")


def files(asset):
    with urllib.request.urlopen(urllib.request.Request(f"{API}/files/{asset}", headers={"User-Agent": "Politicom-art/1.0"}), timeout=60) as r:
        return json.load(r)


def main():
    for h in HDRIS:
        f = files(h)
        get(f["hdri"]["2k"]["hdr"]["url"], os.path.join(OUT, "hdri", f"{h}.hdr"))
        print("hdri", h)
    for t in TEXTURES:
        f = files(t)
        res = "2k" if any(w in t for w in ("cobble", "pavement")) else "1k"
        for short, key in MAPS.items():
            if key in f:
                get(f[key][res]["jpg"]["url"], os.path.join(OUT, "textures", t, f"{short}.jpg"))
        print("texture", t)
    for m in MODELS:
        g = files(m)["gltf"]["1k"]
        base = os.path.join(OUT, "models", m)
        get(g["gltf"]["url"], os.path.join(base, f"{m}.gltf"))
        for rel, info in g["gltf"].get("include", {}).items():
            get(info["url"], os.path.join(base, rel))
        print("model", m)


main()
