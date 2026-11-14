#!/usr/bin/env python3
"""
The interface's ornament kit, drawn in code: engraved gold frames with flourished corners
over dark grained lacquer, title plates, section bars, embossed buttons and tabs, medallion
bezels and tapered dividers. Each piece is a PNG in Game/ui/ used as a nine-slice; any of them
can be replaced by painted art of the same size and margins (see UiTheme.cs).

Everything is drawn at 4x and scaled down, so edges are smooth. Needs Pillow and numpy.

Usage: tools/ui/build_ornaments.py [OUTPUT_DIR]     (default: Game/ui)
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SS = 4  # supersampling

# Brass over slate-blue lacquer, as Victoria 3 dresses its windows.
GOLD_TOP, GOLD_MID, GOLD_LOW = (236, 214, 160), (186, 156, 104), (104, 84, 54)
LACQUER = (22, 28, 38)


def hexc(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def grain(size, base, amount=7, seed=1, period=None):
    """Tileable fine grain: blurred noise around a base colour."""
    rng = np.random.default_rng(seed)
    p = period or size
    n = rng.normal(0, 1, (p, p))
    # Blur with wrap-around so the tile repeats seamlessly.
    for _ in range(5):
        n = (n + np.roll(n, 1, 0) + np.roll(n, -1, 0) + np.roll(n, 1, 1) + np.roll(n, -1, 1)) / 5
    streak = np.roll(n, 3, 1) * 0.6 + n  # a faint horizontal grain, like brushed lacquer
    streak = (streak - streak.mean()) / (streak.std() + 1e-9)
    reps = math.ceil(size / p)
    t = np.tile(streak, (reps, reps))[:size, :size]
    img = np.zeros((size, size, 4), np.float32)
    for c in range(3):
        img[..., c] = base[c] + t * amount
    img[..., 3] = 255
    return np.clip(img, 0, 255)


def gold_fill(mask, top=GOLD_TOP, mid=GOLD_MID, low=GOLD_LOW, bright=1.0):
    """Colour a mask (0..1, HxW) as engraved gold: a vertical sheen and a bevel from the mask's slope."""
    h, w = mask.shape
    y = np.linspace(0, 1, h)[:, None]
    # Three-stop gradient: light at the top, warm in the middle, dark at the foot.
    col = np.zeros((h, w, 3), np.float32)
    for c in range(3):
        upper = top[c] + (mid[c] - top[c]) * np.clip(y / 0.5, 0, 1)
        lower = mid[c] + (low[c] - mid[c]) * np.clip((y - 0.5) / 0.5, 0, 1)
        col[..., c] = np.where(y < 0.5, upper, lower) * np.ones((1, w))
    # Bevel: light from the upper left.
    m = mask.astype(np.float32)
    sh = (np.roll(m, SS, 0) + np.roll(m, SS, 1)) / 2
    light = np.clip(m - sh, 0, 1)
    dark = np.clip(sh - m, 0, 1)
    col += (light[..., None] * 70 - dark[..., None] * 0) * bright
    edge = np.clip(m - (np.roll(m, -SS, 0) + np.roll(m, -SS, 1)) / 2, 0, 1)
    col -= edge[..., None] * 60
    out = np.zeros((h, w, 4), np.float32)
    out[..., :3] = np.clip(col * bright, 0, 255)
    out[..., 3] = m * 255
    return out


def over(dst, src):
    """Alpha-composite src over dst (float RGBA arrays)."""
    a = src[..., 3:4] / 255
    dst[..., :3] = src[..., :3] * a + dst[..., :3] * (1 - a)
    dst[..., 3:4] = np.maximum(dst[..., 3:4], src[..., 3:4])
    return dst


def mask_from(draw_fn, w, h):
    im = Image.new("L", (w, h), 0)
    draw_fn(ImageDraw.Draw(im))
    return np.asarray(im, np.float32) / 255


def finish(arr, w, h):
    im = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA")
    return im.resize((w, h), Image.LANCZOS)


def scroll(d, cx, cy, r, turns, start, direction, width):
    """A spiral curl (the volute of a scroll), as a thick polyline."""
    pts = []
    steps = int(80 * turns)
    for i in range(steps + 1):
        t = i / steps
        ang = start + direction * t * turns * 2 * math.pi
        rad = r * (1 - 0.82 * t)
        pts.append((cx + rad * math.cos(ang), cy + rad * math.sin(ang)))
    d.line(pts, fill=255, width=width, joint="curve")


def corner_flourish(d, s):
    """Top-left corner ornament in a box of s px (supersampled): a bracket inside the border whose
    arms end in volutes, with a three-leaf palmette on the diagonal."""
    lw = max(2, int(s * 0.05))
    a = s * 0.27
    d.line([(a, a), (s * 0.78, a)], fill=255, width=lw)
    d.line([(a, a), (a, s * 0.78)], fill=255, width=lw)
    scroll(d, s * 0.80, a + s * 0.10, s * 0.10, 1.2, -math.pi / 2, 1, lw)
    scroll(d, a + s * 0.10, s * 0.80, s * 0.10, 1.2, math.pi, -1, lw)
    # Palmette: a leaf along the diagonal and two smaller ones beside it.
    def leaf(cx, cy, length, width, angle):
        pts = []
        for t in np.linspace(0, 2 * math.pi, 40):
            x, y = length * math.cos(t), width * math.sin(t) * (1 - 0.35 * math.cos(t))
            pts.append((cx + x * math.cos(angle) - y * math.sin(angle), cy + x * math.sin(angle) + y * math.cos(angle)))
        d.polygon(pts, fill=255)
    c = a + s * 0.17
    leaf(c, c, s * 0.12, s * 0.045, math.pi / 4)
    leaf(c - s * 0.05, c + s * 0.06, s * 0.08, s * 0.03, math.pi / 4 + 0.7)
    leaf(c + s * 0.06, c - s * 0.05, s * 0.08, s * 0.03, math.pi / 4 - 0.7)
    d.ellipse([a - s * 0.05, a - s * 0.05, a + s * 0.05, a + s * 0.05], fill=255)


def window_frame(path, size=160, margin=44, period=72, bg=LACQUER, ornate=True):
    """A nine-slice window: grained lacquer, an engraved gold double border and flourished corners."""
    S = size * SS
    base = grain(S, bg, amount=3.5, period=period * SS)
    # Align the tile so the centre region starts on a tile boundary (the centre repeats seamlessly).
    shift = (margin * SS) % (period * SS)
    base = np.roll(np.roll(base, shift, 0), shift, 1)
    # Soft inner shadow along the border.
    yy, xx = np.mgrid[0:S, 0:S]
    edge = np.minimum(np.minimum(xx, S - 1 - xx), np.minimum(yy, S - 1 - yy)) / SS
    base[..., :3] *= (0.78 + 0.22 * np.clip(edge / 18, 0, 1))[..., None]
    out = base

    def border(d):
        o = 3 * SS
        d.rounded_rectangle([o, o, S - o, S - o], radius=6 * SS, outline=255, width=3 * SS)
        i = 9 * SS
        d.rectangle([i, i, S - i, S - i], outline=255, width=max(1, SS))
    out = over(out, gold_fill(mask_from(border, S, S)))
    # A dark keyline outside the gold.
    key = mask_from(lambda d: d.rounded_rectangle([SS, SS, S - SS, S - SS], radius=7 * SS, outline=255, width=2 * SS), S, S)
    out = over(out, np.dstack([np.zeros((S, S, 3)), key * 230]))
    if ornate:
        c = margin * SS
        m = mask_from(lambda d: corner_flourish(d, c), c, c)
        full = np.zeros((S, S), np.float32)
        full[:c, :c] = np.maximum(full[:c, :c], m)
        full[:c, S - c:] = np.maximum(full[:c, S - c:], m[:, ::-1])
        full[S - c:, :c] = np.maximum(full[S - c:, :c], m[::-1, :])
        full[S - c:, S - c:] = np.maximum(full[S - c:, S - c:], m[::-1, ::-1])
        # A shadow under the ornament lifts it off the lacquer.
        shadow = np.asarray(Image.fromarray((full * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2 * SS)), np.float32) / 255
        out = over(out, np.dstack([np.zeros((S, S, 3)), np.roll(np.roll(shadow, SS, 0), SS, 1) * 160]))
        out = over(out, gold_fill(full))
    finish(out, size, size).save(path)


def small_frame(path, size=48, period=24):
    """Tooltips, menus and cards: lacquer with a single fine gold rim."""
    S = size * SS
    out = grain(S, (19, 24, 33), amount=3, period=period * SS)
    out = np.roll(np.roll(out, (10 * SS) % (period * SS), 0), (10 * SS) % (period * SS), 1)
    rim = mask_from(lambda d: d.rounded_rectangle([SS, SS, S - SS, S - SS], radius=4 * SS, outline=255, width=2 * SS), S, S)
    out = over(out, gold_fill(rim, bright=0.85))
    inner = mask_from(lambda d: d.rounded_rectangle([5 * SS, 5 * SS, S - 5 * SS, S - 5 * SS], radius=2 * SS, outline=255, width=SS), S, S)
    out = over(out, np.dstack([np.full((S, S, 3), 120.0) * np.array(GOLD_LOW) / 120, inner * 110]))
    finish(out, size, size).save(path)


def plate(path, w, h, top, bottom, rim_bright=1.0, radius=5, rim=2, inset=False, cap=False, open_bottom=False):
    """An embossed plate (buttons, tabs, title bars): a vertical gradient with a gold rim."""
    W, H = w * SS, h * SS
    y = np.linspace(0, 1, H)[:, None]
    body = np.zeros((H, W, 4), np.float32)
    for c in range(3):
        body[..., c] = (top[c] + (bottom[c] - top[c]) * y) * np.ones((1, W))
    shape = mask_from(lambda d: d.rounded_rectangle([0, 0, W - 1, (H + radius * SS * 2) if open_bottom else H - 1],
                                                     radius=radius * SS, fill=255), W, H)
    body[..., 3] = shape * 255
    # A sheen across the upper half, or a shadow at the top when pressed in.
    sheen = np.clip(1 - y / 0.5, 0, 1) * (-28 if inset else 18)
    body[..., :3] = np.clip(body[..., :3] + sheen[..., None], 0, 255)
    out = body
    r = rim * SS
    ring = mask_from(lambda d: d.rounded_rectangle([r // 2, r // 2, W - 1 - r // 2, (H + radius * SS * 2) if open_bottom else H - 1 - r // 2],
                                                    radius=radius * SS, outline=255, width=r), W, H) * shape
    out = over(out, gold_fill(ring, bright=rim_bright))
    if cap:
        # Scroll terminals at both ends of a title plate.
        def caps(d):
            lw = max(2, int(SS * 1.6))
            scroll(d, H * 0.55, H * 0.5, H * 0.34, 1.0, 0, -1, lw)
            scroll(d, W - H * 0.55, H * 0.5, H * 0.34, 1.0, math.pi, 1, lw)
        out = over(out, gold_fill(mask_from(caps, W, H), bright=rim_bright))
    finish(out, w, h).save(path)


def side_tab(path, top, bottom, rim_bright, w=36, h=96, radius=10):
    """A tab on a panel's edge: the left side rounded, the right flat (it meets the panel)."""
    W, H = w * SS, h * SS
    x = np.linspace(0, 1, W)[None, :]
    body = np.zeros((H, W, 4), np.float32)
    for c in range(3):
        body[..., c] = (top[c] + (bottom[c] - top[c]) * x) * np.ones((H, 1))
    shape = mask_from(lambda d: d.rounded_rectangle([0, 0, W + radius * SS * 2, H - 1], radius=radius * SS, fill=255), W, H)
    body[..., 3] = shape * 255
    r = 2 * SS
    ring = mask_from(lambda d: d.rounded_rectangle([r // 2, r // 2, W + radius * SS * 2, H - 1 - r // 2], radius=radius * SS, outline=255, width=r), W, H) * shape
    out = over(body, gold_fill(ring, bright=rim_bright))
    finish(out, w, h).save(path)


def section_bar(path, w=240, h=30):
    """Behind a section heading: a shaded band with fine gold rules, fading out to the right."""
    W, H = w * SS, h * SS
    x = np.linspace(0, 1, W)[None, :]
    fade = np.clip((1 - x) / 0.55, 0, 1) ** 1.3
    out = np.zeros((H, W, 4), np.float32)
    y = np.linspace(0, 1, H)[:, None]
    for c, (a, b) in enumerate(zip((40, 50, 66), (22, 28, 38))):
        out[..., c] = a + (b - a) * y
    out[..., 3] = 225 * fade
    rules = mask_from(lambda d: (d.line([(0, SS), (W, SS)], fill=255, width=SS), d.line([(0, H - 2 * SS), (W, H - 2 * SS)], fill=255, width=SS)), W, H)
    g = gold_fill(rules, bright=0.9)
    g[..., 3] *= fade
    out = over(out, g)
    # A gold bar at the left end.
    bar = mask_from(lambda d: d.rectangle([0, 0, 3 * SS, H], fill=255), W, H)
    out = over(out, gold_fill(bar))
    finish(out, w, h).save(path)


def divider(path, w=256, h=8):
    """A tapered gold rule that fades at both ends."""
    W, H = w * SS, h * SS
    x = np.linspace(-1, 1, W)
    thick = (1 - np.abs(x) ** 1.6) * 1.6 * SS + 0.5 * SS
    yy = np.arange(H)[:, None] - H / 2
    m = np.clip(thick[None, :] - np.abs(yy), 0, 1)
    g = gold_fill(m)
    g[..., 3] *= np.clip(1 - np.abs(x) ** 3, 0, 1)[None, :]
    finish(g, w, h).save(path)


def medallion(path, size=96):
    """A round bezel for an emblem: a beaded gold ring around a dark enamel disc."""
    S = size * SS
    yy, xx = np.mgrid[0:S, 0:S]
    r = np.hypot(xx - S / 2, yy - S / 2) / (S / 2)
    out = np.zeros((S, S, 4), np.float32)
    disc = r < 0.84
    shade = np.clip(1 - r, 0, 1)
    for c, v in enumerate((28, 34, 46)):
        out[..., c] = v + 26 * shade ** 0.7
    out[..., 3] = disc * 255
    ring = mask_from(lambda d: d.ellipse([S * 0.04, S * 0.04, S * 0.96, S * 0.96], outline=255, width=int(S * 0.09)), S, S)
    out = over(out, gold_fill(ring))
    beads = mask_from(lambda d: [d.ellipse([S / 2 + S * 0.44 * math.cos(a) - S * 0.022, S / 2 + S * 0.44 * math.sin(a) - S * 0.022,
                                            S / 2 + S * 0.44 * math.cos(a) + S * 0.022, S / 2 + S * 0.44 * math.sin(a) + S * 0.022], fill=255)
                                 for a in np.linspace(0, 2 * math.pi, 24, endpoint=False)], S, S)
    out = over(out, gold_fill(beads, top=(255, 236, 170), bright=1.1))
    inner = mask_from(lambda d: d.ellipse([S * 0.15, S * 0.15, S * 0.85, S * 0.85], outline=255, width=max(1, SS)), S, S)
    out = over(out, np.dstack([np.zeros((S, S, 3)), inner * 200]))
    finish(out, size, size).save(path)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "..", "Game", "ui")
    os.makedirs(out, exist_ok=True)
    p = lambda name: os.path.join(out, name)
    window_frame(p("orn_window.png"))
    small_frame(p("orn_small.png"))
    # Buttons: slate-blue enamel, brighter on hover, lit steel-blue when pressed or on, grey when disabled.
    plate(p("orn_button.png"), 64, 32, (58, 70, 88), (30, 37, 49), rim_bright=0.75, radius=4)
    plate(p("orn_button_hover.png"), 64, 32, (78, 94, 118), (40, 49, 64), rim_bright=1.05, radius=4)
    plate(p("orn_button_pressed.png"), 64, 32, (70, 110, 158), (34, 58, 92), rim_bright=1.1, radius=4, inset=True)
    plate(p("orn_button_disabled.png"), 64, 32, (44, 47, 52), (30, 32, 36), rim_bright=0.45, radius=4)
    # Tabs: the open one lit, the others dim; open at the bottom so they sit on the panel.
    plate(p("orn_tab.png"), 64, 30, (36, 44, 58), (24, 30, 40), rim_bright=0.55, radius=5, open_bottom=True)
    plate(p("orn_tab_hover.png"), 64, 30, (52, 63, 82), (30, 37, 49), rim_bright=0.8, radius=5, open_bottom=True)
    plate(p("orn_tab_selected.png"), 64, 30, (70, 86, 112), (36, 45, 60), rim_bright=1.1, radius=5, open_bottom=True)
    # A title plate with scroll terminals.
    plate(p("orn_title.png"), 240, 44, (46, 58, 78), (24, 30, 42), rim_bright=1.0, radius=6, cap=True)
    # Side tabs that fold a panel away: rounded on the outer side, flat against the panel's edge.
    side_tab(p("orn_side_tab.png"), (58, 70, 88), (30, 37, 49), 0.85)
    side_tab(p("orn_side_tab_hover.png"), (78, 94, 118), (40, 49, 64), 1.1)
    section_bar(p("orn_section.png"))
    divider(p("orn_divider.png"))
    medallion(p("orn_medallion.png"))
    print("ornaments written to", out)


if __name__ == "__main__":
    main()
