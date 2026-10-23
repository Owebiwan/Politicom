#!/usr/bin/env python3
"""
Builds the textures behind the map's water and terrain (Natural Earth, public domain):

- Game/map/coast.png: distance from the nearest coast over the sea, 8192 x 4096
  equirectangular (about 5 km a pixel). The value is sqrt(distance / 800 km), so the coast has
  fine resolution; land is 0. The sea shader uses it for depth (shallow near the coast) and foam.
- Game/map/terrain.webp: land cover (forest, grassland, desert, tundra, ice) from Natural Earth I,
  4096 x 2048, transparent over the sea, laid lightly under the political colours.

Usage:
  tools/geo/build_sea.py NE_50M_LAND.geojson NE1_HR_LC.tif OUTPUT_DIR     (needs numpy, scipy, Pillow)
"""
import json
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

Image.MAX_IMAGE_PIXELS = None
W, H = 8192, 4096
MAX_KM = 800


def land_mask(geojson, w, h):
    im = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(im)
    for f in json.load(open(geojson))["features"]:
        g = f["geometry"]
        polys = g["coordinates"] if g["type"] == "MultiPolygon" else [g["coordinates"]]
        for poly in polys:
            for k, ring in enumerate(poly):
                pts = [((x + 180) / 360 * w, (90 - y) / 180 * h) for x, y in ring]
                if len(pts) >= 3:
                    d.polygon(pts, fill=255 if k == 0 else 0)
    return np.asarray(im) > 127


def main():
    land_path, lc_path, out = sys.argv[1:4]
    land = land_mask(land_path, W, H)
    # Distance over the sea to the nearest land, in km. Pixels are narrower toward the poles;
    # an equator-scale distance is close enough for shading.
    km_per_px = 40075 / W
    dist = ndimage.distance_transform_edt(~land) * km_per_px
    v = np.sqrt(np.clip(dist / MAX_KM, 0, 1))
    v[land] = 0
    Image.fromarray((v * 255).astype(np.uint8), "L").save(f"{out}/coast.png", optimize=True)

    lc = Image.open(lc_path).convert("RGB").resize((W // 2, H // 2), Image.BOX)
    mask = Image.fromarray(land.astype(np.uint8) * 255).resize((W // 2, H // 2), Image.BOX)
    rgba = lc.copy()
    rgba.putalpha(mask)
    rgba.save(f"{out}/terrain.webp", quality=82, method=6)
    print("coast and terrain written")


if __name__ == "__main__":
    main()
