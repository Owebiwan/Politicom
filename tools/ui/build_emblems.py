#!/usr/bin/env python3
"""
The game's emblems, engraved: silhouettes from game-icons.net (CC BY 3.0, credited in
Data/geo/ATTRIBUTION.md) given a metal finish as SVG, so Godot renders them crisp at any size.

- Interface and technology emblems: struck gold (a vertical gradient, a dark keyline and a
  shadow, as if engraved).
- Settlements: ivory emblems on dark map pins with a gold rim (red for ruins).
- Armies: pale emblems the enemy's colour tints when drawn.
- The militia: a black-and-red flag, drawn for the game.

Usage: tools/ui/build_emblems.py [OUTPUT_DIR]     (default: Game/emblems)
"""
import os
import re
import sys
import urllib.request

BASE = "https://raw.githubusercontent.com/game-icons/icons/master/"

GOLD = ("#f6dc92", "#d1a85c", "#7d5a2a")
IVORY = ("#fbf3df", "#e2d3b0", "#a99372")
PALE = ("#ffffff", "#e8e8e8", "#a8a8a8")
COLD = ("#e9eef2", "#b9c4cc", "#6d7880")

# name: (icon path, finish, kind) — kind: "emblem", "pin" (map settlement), "plain"
EMBLEMS = {
    # Interface
    "influence": ("delapouite/scroll-quill", GOLD, "emblem"),
    "drift": ("lorc/crossed-chains", COLD, "emblem"),
    "food": ("lorc/wheat", GOLD, "emblem"),
    "goods": ("lorc/anvil", COLD, "emblem"),
    "militia_icon": ("delapouite/pikeman", GOLD, "emblem"),
    "gold": ("delapouite/two-coins", GOLD, "emblem"),
    "enthusiasm": ("carl-olsen/flame", ("#ffe08a", "#f08a3c", "#a8321e"), "emblem"),
    "science": ("lorc/fizzing-flask", ("#e6f6ff", "#8fd3f4", "#2c6f9a"), "emblem"),
    "culture": ("lorc/drama-masks", ("#f0d9ff", "#b27fd6", "#5a2f80"), "emblem"),
    "trust": ("delapouite/shaking-hands", GOLD, "emblem"),
    "wellbeing": ("lorc/sun", GOLD, "emblem"),
    "enemy": ("lorc/crossed-swords", ("#ffd2c8", "#e0705f", "#8e2b25"), "emblem"),
    "revolution": ("lorc/fist", ("#ffd2c8", "#d1453b", "#7a1f1a"), "emblem"),
    "decision": ("lorc/scroll-unfurled", IVORY, "emblem"),
    "world": ("lorc/world", ("#e3f2fb", "#8ecae6", "#2c5f86"), "emblem"),
    "state": ("lorc/crown", GOLD, "emblem"),
    "good": ("lorc/laurel-crown", ("#e9f5c8", "#a7c957", "#4f6f2a"), "emblem"),
    "danger": ("lorc/hazard-sign", ("#ffd2c8", "#e63946", "#7a1f1a"), "emblem"),
    "warning": ("lorc/ringing-bell", GOLD, "emblem"),
    "party": ("delapouite/hammer-sickle", ("#ffd2c8", "#c1121f", "#5e0a10"), "emblem"),
    "treasury": ("delapouite/bank", GOLD, "emblem"),
    "project": ("delapouite/stake-hammer", GOLD, "emblem"),
    "pressure": ("lorc/broken-shield", ("#ffd2c8", "#e0705f", "#8e2b25"), "emblem"),
    "chart": ("lorc/hourglass", GOLD, "emblem"),
    # Technologies
    "t_council": ("delapouite/round-table", GOLD, "emblem"),
    "t_hands": ("delapouite/discussion", GOLD, "emblem"),
    "t_book": ("lorc/open-book", IVORY, "emblem"),
    "t_ledger": ("delapouite/abacus", GOLD, "emblem"),
    "t_plough": ("delapouite/plow", GOLD, "emblem"),
    "t_sprout": ("delapouite/seedling", ("#e9f5c8", "#8cc152", "#3f6b22"), "emblem"),
    "t_water": ("lorc/droplet-splash", ("#e6f6ff", "#8fd3f4", "#2c6f9a"), "emblem"),
    "t_mill": ("delapouite/windmill", GOLD, "emblem"),
    "t_gear": ("lorc/gears", COLD, "emblem"),
    "t_bolt": ("lorc/lightning-branches", ("#fff6c8", "#f7d65a", "#a8822c"), "emblem"),
    "t_train": ("delapouite/steam-locomotive", COLD, "emblem"),
    "t_sword": ("lorc/broadsword", COLD, "emblem"),
    "t_tower": ("lorc/stone-tower", COLD, "emblem"),
    "t_rifle": ("sbed/rifle", COLD, "emblem"),
    "t_tank": ("cathelineau/great-war-tank", COLD, "emblem"),
    "t_plane": ("delapouite/plane-pilot", COLD, "emblem"),
    "t_press": ("delapouite/megaphone", GOLD, "emblem"),
    "t_radio": ("delapouite/radio-tower", COLD, "emblem"),
    "t_computer": ("skoll/pc", COLD, "emblem"),
    "t_tractor": ("delapouite/farm-tractor", GOLD, "emblem"),
    "t_flag": ("lorc/tattered-banner", ("#ffd2c8", "#d1453b", "#7a1f1a"), "emblem"),
    "t_star": ("delapouite/star-medal", GOLD, "emblem"),
    # Settlements (map pins)
    "village": ("delapouite/huts-village", IVORY, "pin"),
    "workshop": ("delapouite/blacksmith", IVORY, "pin"),
    "oldtown": ("delapouite/castle", IVORY, "pin"),
    "farm": ("delapouite/barn", IVORY, "pin"),
    "factory": ("delapouite/factory", IVORY, "pin"),
    "town": ("delapouite/church", IVORY, "pin"),
    "ruins": ("delapouite/castle-ruins", ("#c9b8a6", "#8a7a6c", "#4a3f36"), "ruin"),
    # Armies (tinted by the enemy's colour)
    "spearman": ("delapouite/centurion-helmet", PALE, "plain"),
    "rifleman": ("skoll/brodie-helmet", PALE, "plain"),
}
FALLBACKS = {"lorc/lightning-branches": ["lorc/lightning-arc", "delapouite/lightning-dome"], "lorc/broadsword": ["delapouite/ancient-sword"],
             "delapouite/plane-pilot": ["delapouite/jet-fighter"], "caro-asercion/warlord-helmet": ["delapouite/custodian-helmet"]}


def fetch(path):
    for p in [path] + FALLBACKS.get(path, []):
        try:
            return urllib.request.urlopen(BASE + p + ".svg", timeout=60).read().decode("utf-8"), p
        except Exception:
            continue
    raise SystemExit(f"no icon {path}")


def glyph(svg):
    """The glyph's path data, without the black background square."""
    paths = re.findall(r'<path[^>]*d="([^"]+)"', svg)
    return [d for d in paths if not d.startswith("M0 0h512v512H0z")]


def gradient(gid, finish):
    top, mid, low = finish
    return (f'<linearGradient id="{gid}" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="{top}"/><stop offset="0.5" stop-color="{mid}"/><stop offset="1" stop-color="{low}"/></linearGradient>')


def emblem_svg(ds, finish, kind):
    paths = "".join(f'<path d="{d}"/>' for d in ds)
    defs = gradient("g", finish)
    struck = (f'<g fill="#0b0907" opacity="0.55" transform="translate(10 14)">{paths}</g>'  # shadow
              f'<g fill="url(#g)" stroke="#120e0a" stroke-width="16" paint-order="stroke" stroke-linejoin="round">{paths}</g>'
              f'<g fill="none" stroke="#fff8e0" stroke-width="3" stroke-opacity="0.35" transform="translate(-2 -3)">{paths}</g>')  # top light
    if kind in ("pin", "ruin"):
        rim = "#d9b46a" if kind == "pin" else "#c0392b"
        defs += gradient("rim", ("#f6dc92", rim, "#5c4320") if kind == "pin" else ("#f3a19a", rim, "#5e1712"))
        plate = (f'<circle cx="256" cy="262" r="236" fill="#0b0907" opacity="0.55"/>'
                 f'<circle cx="256" cy="256" r="230" fill="#1f1b17" stroke="url(#rim)" stroke-width="22"/>'
                 f'<circle cx="256" cy="256" r="200" fill="none" stroke="#000" stroke-opacity="0.5" stroke-width="4"/>')
        return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><defs>{defs}</defs>{plate}'
                f'<g transform="translate(256 256) scale(0.66) translate(-256 -256)">{struck}</g></svg>')
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><defs>{defs}</defs>{struck}</svg>'


MILITIA_FLAG = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
<defs><linearGradient id="pole" x1="0" x2="1"><stop offset="0" stop-color="#f6dc92"/><stop offset="1" stop-color="#7d5a2a"/></linearGradient>
<linearGradient id="red" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e2574c"/><stop offset="1" stop-color="#8e2b25"/></linearGradient>
<linearGradient id="black" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3a3a3e"/><stop offset="1" stop-color="#0c0c0e"/></linearGradient></defs>
<g transform="translate(10 14)" opacity="0.55"><path d="M120 40 h22 v440 h-22z M142 60 C230 40 300 90 440 66 L440 286 C300 310 230 260 142 280z" fill="#0b0907"/></g>
<rect x="116" y="36" width="26" height="448" rx="8" fill="url(#pole)" stroke="#120e0a" stroke-width="10"/>
<circle cx="129" cy="34" r="20" fill="url(#pole)" stroke="#120e0a" stroke-width="10"/>
<path d="M142 60 C230 40 300 90 440 66 L142 280z" fill="url(#red)" stroke="#120e0a" stroke-width="12" stroke-linejoin="round"/>
<path d="M440 66 L440 286 C300 310 230 260 142 280z" fill="url(#black)" stroke="#120e0a" stroke-width="12" stroke-linejoin="round"/>
</svg>"""


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "..", "Game", "emblems")
    os.makedirs(out, exist_ok=True)
    used = set()
    for name, (path, finish, kind) in EMBLEMS.items():
        svg, actual = fetch(path)
        used.add(actual.split("/")[0])
        with open(os.path.join(out, name + ".svg"), "w", encoding="utf-8") as f:
            f.write(emblem_svg(glyph(svg), finish, kind))
    with open(os.path.join(out, "militia.svg"), "w", encoding="utf-8") as f:
        f.write(MILITIA_FLAG)
    print(f"{len(EMBLEMS) + 1} emblems; icon authors: {', '.join(sorted(used))}")


if __name__ == "__main__":
    main()
