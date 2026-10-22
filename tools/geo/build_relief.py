#!/usr/bin/env python3
"""
Builds Data/relief/: shaded relief for the maps, from Natural Earth's "Shaded Relief, High
Resolution" (SR_HR.tif, 21600 x 10800, public domain, naturalearthdata.com).

The relief is turned into a *shade*: a grey-and-alpha image (PNG, colour type 4) that is black
where slopes face away from the light and white where they face it, and transparent on flat
ground and sea. Laid over the political colours it shows mountains without hiding anything.

- world.shade: the whole world at a quarter resolution (5400 x 2700), for zoomed-out views.
- t_<col>_<row>.shade: 30° x 30° tiles at half resolution (900 x 900 plus a one-pixel border
  from the neighbouring tiles, so they join without seams; about 3.7 km a pixel),
  loaded when the view is close enough to need them. Tiles that are all flat (open sea) are
  left out. Column 0 starts at 180° W, row 0 at 90° N.

The files are PNGs with a different extension, so Godot loads them as data instead of
importing them as textures.

Usage:
  tools/geo/build_relief.py SR_HR.tif OUTPUT_DIR

Needs numpy. SR_HR.tif is an uncompressed 8-bit TIFF, read directly. The relief is averaged
down to half resolution and lightly blurred, so it shades smoothly when stretched.
"""
import os
import struct
import sys
import zlib

import numpy as np  # pip install numpy

TILE_DEG = 30
WORLD_STEP = 4      # the world overview keeps every 4th pixel
GAIN = 2.2          # alpha per step away from flat
DEAD = 6            # steps from flat that stay transparent: plains read as plains, not grain
MAX_SHADOW = 200    # alpha caps: shadows can be fairly strong,
MAX_LIGHT = 90      # highlights stay subtle so colours don't wash out


def read_tiff(path):
    with open(path, "rb") as f:
        data = f.read()
    bo = "<" if data[:2] == b"II" else ">"
    ifd = struct.unpack(bo + "I", data[4:8])[0]
    n = struct.unpack(bo + "H", data[ifd:ifd + 2])[0]
    tags = {}
    for i in range(n):
        t, ty, c, v = struct.unpack(bo + "HHII", data[ifd + 2 + 12 * i: ifd + 14 + 12 * i])
        tags[t] = (ty, c, v)
    width, height = tags[256][2], tags[257][2]
    if tags[258][2] != 8 or tags[259][2] != 1:
        sys.exit("Expected an uncompressed 8-bit TIFF.")
    ty, count, at = tags[273]
    size = 4 if ty == 4 else 2
    offsets = struct.unpack(bo + ("I" if size == 4 else "H") * count, data[at: at + size * count])
    rows_per_strip = tags.get(278, (3, 1, height))[2]
    rows = []
    for off in offsets:
        for r in range(rows_per_strip):
            if len(rows) == height:
                break
            start = off + r * width
            rows.append(data[start: start + width])
    return width, height, rows


def png_la(path, width, rows_l, rows_a):
    raw = bytearray()
    for l, a in zip(rows_l, rows_a):
        line = bytearray(2 * width)
        line[0::2] = l
        line[1::2] = a
        raw.append(0)
        raw += line

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, len(rows_l), 8, 4, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
        f.write(chunk(b"IEND", b""))


def smooth(d):
    """A light 3 x 3 blur, so the relief shades instead of speckling when the map zooms in."""
    p = np.pad(d, 1, mode="edge")
    return sum(p[i:i + d.shape[0], j:j + d.shape[1]] * k for (i, j), k in
               {(0, 0): 1, (0, 1): 2, (0, 2): 1, (1, 0): 2, (1, 1): 4, (1, 2): 2, (2, 0): 1, (2, 1): 2, (2, 2): 1}.items()) / 16


def halve(d):
    """Average 2 x 2 blocks (not every other pixel, which aliases)."""
    return (d[0::2, 0::2] + d[1::2, 0::2] + d[0::2, 1::2] + d[1::2, 1::2]) / 4


def shade(d):
    """Signed difference from flat → (grey, alpha): black shadows, white light, clear on flat ground."""
    a = np.where(d < 0, np.clip((-d - DEAD) * GAIN, 0, MAX_SHADOW), np.clip((d - DEAD) * GAIN * 0.6, 0, MAX_LIGHT))
    l = np.where(d < 0, 0, 255)
    return l.astype(np.uint8), a.astype(np.uint8)


def write(path, d):
    l, a = shade(d)
    png_la(path, d.shape[1], [r.tobytes() for r in l], [r.tobytes() for r in a])


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    src, out = sys.argv[1:]
    os.makedirs(out, exist_ok=True)
    width, height, rows = read_tiff(src)
    img = np.frombuffer(b"".join(rows), dtype=np.uint8).reshape(height, width)
    flat = int(np.bincount(img[::50].ravel(), minlength=256).argmax())  # flat ground and sea share the commonest value
    print(f"{width} x {height}, flat at {flat}")
    d = smooth(halve(img.astype(np.float32) - flat))  # 10800 x 5400, ~3.7 km a pixel

    write(os.path.join(out, "world.shade"), smooth(halve(d)))  # 5400 x 2700 overview

    px = d.shape[1] * TILE_DEG // 360
    padded = np.pad(d, 1, mode="edge")
    kept = 0
    for row in range(d.shape[0] // px):
        for col in range(d.shape[1] // px):
            # One pixel of the neighbours around each tile, so filtering blends across tile edges.
            block = padded[row * px:(row + 1) * px + 2, col * px:(col + 1) * px + 2]
            if not (np.abs(block) > DEAD).any():
                continue  # open sea
            write(os.path.join(out, f"t_{col}_{row}.shade"), block)
            kept += 1
    total = sum(os.path.getsize(os.path.join(out, f)) for f in os.listdir(out))
    print(f"{kept} tiles of {px} px, {total // 1024 // 1024} MB")


if __name__ == "__main__":
    main()
