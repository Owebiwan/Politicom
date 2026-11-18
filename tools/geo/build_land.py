#!/usr/bin/env python3
"""
Builds Data/land/ (mirrored into Game/data/land/ by the build): the ground under the map, from NASA's Blue Marble Next Generation
(July 2004, with topography and bathymetry, 21600 x 10800, about 1.85 km a pixel) and the
GEBCO-derived elevation that comes with it (gebco_08_rev_elev_21600x10800.png). Both are
NASA Earth Observatory images, public domain.

The satellite colours are graded toward a painted map (a little less saturated, warmer, with
a gentle S-curve and a light smoothing that takes off the satellite grain), and lit from the
north-west by hill shading from the elevation, so mountains stand out.
Along the coasts the water's pixels take the colour of the nearest land: the game's own
coastlines decide where land ends (the land is drawn inside the map's polygons), so there's no
blue fringe; lakes inside the land keep their water.

- world.land: the whole world, 4096 x 2048, for zoomed-out views.
- height.land: elevation, 4096 x 2048 grey (0 at sea level, white at Everest), for the 3D map.
- t_<col>_<row>.land: 30 x 30 degree tiles at full resolution (1800 x 1800), assembled in the
  game into one texture around the scenario's region. Column 0 starts at 180 W, row 0 at 90 N.
  Tiles with no land are left out.

The files are WebP with another extension, so Godot ships them as data rather than importing
them as textures.

Usage:
  tools/geo/build_land.py BLUE_MARBLE.jpg ELEVATION.png OUTPUT_DIR      (needs numpy, scipy, Pillow)
"""
import os
import sys

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

Image.MAX_IMAGE_PIXELS = None
TILE_DEG = 30
PX_PER_DEG = 60
EXAGGERATION = 5.0       # hill shading is subtle at 1.85 km a pixel without it
METRES_PER_STEP = 8848 / 255
BLEED = 8                # pixels of land colour carried over the water along coasts


def grade(rgb):
    """Satellite colours toward a painted map: float RGB 0..1 in and out."""
    lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
    rgb = lum[..., None] + (rgb - lum[..., None]) * 0.82            # less saturated
    rgb = rgb * np.array([1.05, 1.0, 0.9], np.float32)             # warmer: ochre plains, olive forests
    rgb = np.clip(rgb, 0, 1) ** 0.82                               # lighter midtones
    rgb = rgb + (rgb - 0.5) * 0.12 * (1 - np.abs(rgb - 0.5) * 2)   # a soft S-curve
    return np.clip(rgb, 0, 1)


def hillshade(elev):
    """North-west light at 40 degrees, as a factor around 1 (1 on flat ground)."""
    z = elev.astype(np.float32) * METRES_PER_STEP * EXAGGERATION
    lat = np.linspace(90, -90, elev.shape[0], dtype=np.float32)[:, None]
    dx = 1852 * np.cos(np.radians(lat)).clip(0.05) * (60 / PX_PER_DEG)  # metres per pixel east-west
    dy = 1852 * (60 / PX_PER_DEG)
    gy, gx = np.gradient(ndimage.gaussian_filter(z, 1.2))
    sx, sy = gx / dx, -gy / dy
    az, alt = np.radians(315), np.radians(40)
    lx, ly, lz = np.sin(az) * np.cos(alt), np.cos(az) * np.cos(alt), np.sin(alt)
    shade = (-sx * lx - sy * ly + lz) / np.sqrt(1 + sx * sx + sy * sy)
    flat = lz
    return 1 + np.clip((shade - flat) * 0.95, -0.5, 0.3)


def finish(rgb_u8, elev):
    """Bleed land over the sea, grade, shade: one piece (with its margin) at a time."""
    raw = rgb_u8.astype(np.float32) / 255
    r, g, b = raw[..., 0], raw[..., 1], raw[..., 2]
    # Land is what doesn't look like water in the picture (the elevation misses low coasts and
    # polders); a pixel's width is shaved off so coastal pixels half full of sea don't spread.
    water = ((b > r + 0.05) & (b > g - 0.03)) | ((r + g + b < 0.36) & (elev == 0))
    land = ndimage.binary_erosion(~water | (elev > 3), iterations=1)
    rgb = np.asarray(Image.fromarray(rgb_u8).filter(ImageFilter.MedianFilter(3)), np.float32) / 255
    if land.any() and not land.all():
        # Only a narrow strip along the coast takes the land's colour (the game's coastlines differ
        # a little from the picture's); lakes and seas further out keep their water.
        dist, (iy, ix) = ndimage.distance_transform_edt(~land, return_indices=True)
        # Water further out (lakes the coastlines enclose, like the Caspian) in the game's sea colours.
        lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
        lake = np.array([0.12, 0.24, 0.29], np.float32) * (0.75 + 0.8 * lum)[..., None]
        rgb = np.where((dist <= BLEED)[..., None], rgb[iy, ix], lake)
    rgb = grade(rgb) * hillshade(elev)[..., None]
    return (np.clip(rgb, 0, 1) * 255).astype(np.uint8)


def main():
    bm_path, elev_path, out = sys.argv[1:4]
    os.makedirs(out, exist_ok=True)
    elev = np.asarray(Image.open(elev_path).convert("L"))
    bm = Image.open(bm_path).convert("RGB")
    W, H = bm.size

    # The world overview: shrunk first, then finished at its own scale.
    small = np.asarray(bm.resize((4096, 2048), Image.LANCZOS))
    small_elev = np.asarray(Image.fromarray(elev).resize((4096, 2048), Image.BOX))
    global PX_PER_DEG
    PX_PER_DEG, full = 4096 / 360, PX_PER_DEG
    Image.fromarray(finish(small, small_elev)).save(os.path.join(out, "world.land"), "WEBP", quality=84, method=6)
    PX_PER_DEG = full

    # Elevation for the 3D map's relief: 0 at sea level, 1 at Everest.
    Image.fromarray(elev).resize((4096, 2048), Image.BOX).save(os.path.join(out, "height.land"), "WEBP", lossless=True)

    tile, margin, n = TILE_DEG * PX_PER_DEG, 96, 0
    for row in range(180 // TILE_DEG):
        for col in range(360 // TILE_DEG):
            y, x = row * tile, col * tile
            if not (elev[y:y + tile, x:x + tile] > 0).any():
                continue
            y0, y1 = max(0, y - margin), min(H, y + tile + margin)
            x0, x1 = max(0, x - margin), min(W, x + tile + margin)
            done = finish(np.asarray(bm.crop((x0, y0, x1, y1))), elev[y0:y1, x0:x1])
            piece = done[y - y0: y - y0 + tile, x - x0: x - x0 + tile]
            Image.fromarray(piece).save(os.path.join(out, f"t_{col}_{row}.land"), "WEBP", quality=80, method=6)
            n += 1
            print(f"tile {col},{row}", flush=True)
    print(f"world and {n} tiles written")


if __name__ == "__main__":
    main()
