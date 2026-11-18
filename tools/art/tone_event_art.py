#!/usr/bin/env python3
"""
Finishes the event pictures rendered by render_event_art.py (tools/art/.renders/*.png) in the
style of the rest of the event art: a light ink line and flattened tones, as a period print,
then process_event_art.py's sepia duotone, paper grain and vignette, cropped to the 12:5 frame.
Writes Game/art/events/<theme>_<age>.jpg and their credits in Data/events/art_credits.json.

Usage: tools/art/tone_event_art.py      (needs Pillow and numpy)
"""
import glob
import importlib.util
import json
import os

import numpy as np
from PIL import Image, ImageFilter, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
spec = importlib.util.spec_from_file_location("process_event_art", os.path.join(ROOT, "tools", "ui", "process_event_art.py"))
pea = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pea)

CAPTIONS = {
    "celebration": "The anniversary of the rising, in the square",
    "mourning": "The funeral of a comrade",
    "barricades": "On the barricades",
}
AGES = {"ancient": "antiquity", "medieval": "the middle ages", "earlymodern": "the early modern age",
        "industrial": "the industrial age", "modern": "the twentieth century"}


def print_look(img):
    """Flattened tones and an ink line along the edges, as a print rather than a render."""
    img = img.convert("RGB").filter(ImageFilter.ModeFilter(5))
    edges = ImageOps.invert(img.convert("L").filter(ImageFilter.FIND_EDGES).filter(ImageFilter.MaxFilter(3)))
    ink = np.asarray(edges, np.float32) / 255
    rgb = np.asarray(img, np.float32) * (0.55 + 0.45 * ink[..., None])
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))


def main():
    out = os.path.join(ROOT, "Game", "art", "events")
    credits_path = os.path.join(ROOT, "Data", "events", "art_credits.json")
    with open(credits_path, encoding="utf-8") as f:
        credits = json.load(f)
    for path in sorted(glob.glob(os.path.join(HERE, ".renders", "*.png"))):
        name = os.path.basename(path)[:-4]
        theme, age = name.split("_", 1)
        img = pea.tone_to(print_look(Image.open(path)), pea.W, pea.H, focus=(0.5, 0.5))
        img.save(os.path.join(out, name + ".jpg"), quality=88)
        credits[name] = {
            "caption": f"{CAPTIONS[theme]}, {AGES[age]}",
            "credit": "Rendered for Politicom in Blender from the game's own models (tools/art/render_event_art.py)",
            "source": "",
        }
        print("toned", name)
    with open(credits_path, "w", encoding="utf-8") as f:
        json.dump(credits, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
