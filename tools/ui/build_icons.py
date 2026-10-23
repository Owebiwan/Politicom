#!/usr/bin/env python3
"""
Fetches the game's icons from game-icons.net (https://game-icons.net, CC BY 3.0) into
Game/icons/: the black background square is removed and the glyph is tinted, so each icon
is a coloured emblem on a transparent ground that Godot imports as a texture.

Usage: tools/ui/build_icons.py [OUTPUT_DIR]     (default: Game/icons)
"""
import os
import re
import sys
import urllib.request

BASE = "https://raw.githubusercontent.com/game-icons/icons/master/"

# key: (author/name, colour). Authors are credited in Data/geo/ATTRIBUTION.md.
ICONS = {
    "influence": ("delapouite/scroll-quill", "#e6d3a3"),
    "drift": ("lorc/crossed-chains", "#e07a5f"),
    "food": ("lorc/wheat", "#e9c46a"),
    "goods": ("lorc/anvil", "#b8c1cc"),
    "militia": ("delapouite/pikeman", "#e05d5d"),
    "gold": ("delapouite/two-coins", "#f4c542"),
    "enthusiasm": ("carl-olsen/flame", "#f4845f"),
    "science": ("lorc/fizzing-flask", "#7fc8f8"),
    "culture": ("lorc/drama-masks", "#c77dff"),
    "trust": ("delapouite/shaking-hands", "#8fc98f"),
    "wellbeing": ("lorc/sun", "#ffd166"),
    "enemy": ("lorc/crossed-swords", "#e63946"),
    "revolution": ("lorc/fist", "#e63946"),
    "decision": ("lorc/scroll-unfurled", "#e6d3a3"),
    "world": ("lorc/world", "#8ecae6"),
    "overview": ("lorc/compass", "#e6d3a3"),
    "movement": ("lorc/tattered-banner", "#e05d5d"),
    "commune": ("delapouite/village", "#e9c46a"),
    "log": ("lorc/open-book", "#e6d3a3"),
    "tech": ("lorc/gears", "#b8c1cc"),
    "organizational": ("delapouite/round-table", "#e6d3a3"),
    "agricultural": ("delapouite/plow", "#a7c957"),
    "industrial": ("lorc/gears", "#b8c1cc"),
    "military": ("delapouite/barricade", "#e05d5d"),
    "communication": ("delapouite/megaphone", "#8ecae6"),
    "state": ("lorc/crown", "#f4c542"),
    "time": ("lorc/hourglass", "#e6d3a3"),
    "good": ("lorc/laurel-crown", "#a7c957"),
    "danger": ("lorc/hazard-sign", "#e63946"),
    "warning": ("lorc/ringing-bell", "#e9c46a"),
    "party": ("delapouite/hammer-sickle", "#c1121f"),
    "rail": ("delapouite/steam-locomotive", "#b8c1cc"),
    "treasury": ("delapouite/bank", "#f4c542"),
    "project": ("delapouite/stake-hammer", "#e6d3a3"),
    "pressure": ("lorc/broken-shield", "#e63946"),
    "scores": ("lorc/laurels", "#f4c542"),
    "chart": ("lorc/hourglass", "#9ab0c8"),
}


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "..", "Game", "icons")
    os.makedirs(out, exist_ok=True)
    for key, (path, colour) in ICONS.items():
        svg = urllib.request.urlopen(BASE + path + ".svg").read().decode("utf-8")
        svg = re.sub(r'<path d="M0 0h512v512H0z"\s*/>', "", svg)       # the black square behind the glyph
        svg = svg.replace('fill="#fff"', f'fill="{colour}"')
        # A thin dark outline keeps the emblem legible on light map colours.
        svg = svg.replace("<path ", '<path stroke="#0b0c0e" stroke-width="10" paint-order="stroke" ', 1) if 'fill="' in svg else svg
        with open(os.path.join(out, key + ".svg"), "w", encoding="utf-8") as f:
            f.write(svg)
    print(f"{len(ICONS)} icons")


if __name__ == "__main__":
    main()
