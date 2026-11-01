"""
Builds Politicom's ships and quays (Game/models/ships/*.glb) in Blender, from code:

- galley: an ancient oared warship: a long dark hull with a bronze ram and a painted eye, a
  curling sternpost, banks of oars, one mast with a striped square sail.
- boat: a small fishing boat with a lateen sail and nets (antiquity to the eighteenth century).
- cog: a medieval cog: a deep clinker hull, castles fore and aft, one mast with a square sail
  bearing a cross, a crow's nest.
- galleon: a three-masted square-rigger: tiers of sails, a lateen mizzen, a bowsprit with a jib,
  gun ports along a yellow band, a stern gallery, flags (1450 to the age of steam).
- steamer: an iron screw steamer with a tall funnel and schooner masts (the nineteenth century).
- cargo: a twentieth-century cargo ship: bridge amidships, derricks and hatches, deck cargo.
- liner: an ocean liner: tiers of white decks with rows of windows, three funnels, lifeboats.
- trawler: a motor trawler with a wheelhouse, a funnel and a gantry.
- quay_wood / quay_stone: a wooden pier on piles with a treadwheel crane; a stone quay with an
  iron crane, a warehouse and bollards.

Ships lie along Y with the bow at -Y (+Z in Godot), the waterline at z = 0. Funnels have empties
named "Funnel..." at their tops for the game's smoke. Colours and ambient occlusion are baked into
vertex colours under one material.

Usage:
  blender --background --factory-startup --python tools/models/build_ships.py -- OUTPUT_DIR [--preview DIR] [--only NAME]
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else "Game/models/ships"
PREVIEW = ARGS[ARGS.index("--preview") + 1] if "--preview" in ARGS else None
ONLY = ARGS[ARGS.index("--only") + 1] if "--only" in ARGS else None

COLORS = {
    "Timber": (0.25, 0.16, 0.09), "Plank": (0.42, 0.29, 0.17), "Deck": (0.55, 0.43, 0.29),
    "Tar": (0.07, 0.06, 0.05), "Sail": (0.82, 0.77, 0.64), "Stripe": (0.55, 0.12, 0.08),
    "Ochre": (0.72, 0.55, 0.20), "Bronze": (0.55, 0.38, 0.16), "Rope": (0.30, 0.25, 0.18),
    "Black": (0.06, 0.06, 0.07), "Red": (0.45, 0.10, 0.07), "White": (0.86, 0.85, 0.80),
    "Grey": (0.38, 0.40, 0.42), "Buff": (0.72, 0.58, 0.36), "Glass": (0.13, 0.16, 0.19),
    "Stone": (0.58, 0.55, 0.50), "Iron": (0.20, 0.20, 0.22), "Brick": (0.48, 0.24, 0.17),
    "Slate": (0.27, 0.29, 0.33), "Flag": (0.55, 0.13, 0.12), "Blue": (0.16, 0.24, 0.40),
    "Crate": (0.55, 0.40, 0.24), "Green": (0.18, 0.30, 0.22),
}
MATS = list(COLORS)
M = {n: i for i, n in enumerate(MATS)}


class Part:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()

    def paint(self, verts, mat):
        for f in {f for v in verts for f in v.link_faces}:
            f.material_index = M[mat]

    def box(self, center, size, mat, rot=None):
        m = Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1))
        self.paint(bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)["verts"], mat)

    def tube(self, a, b, r1, r2, mat, seg=10, cap=True):
        a, b = Vector(a), Vector(b)
        d = b - a
        if d.length < 1e-6:
            return
        rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
        m = Matrix.Translation((a + b) / 2) @ rot
        res = bmesh.ops.create_cone(self.bm, cap_ends=cap, cap_tris=False, segments=seg, radius1=r1, radius2=r2,
                                    depth=d.length, matrix=m)
        self.paint(res["verts"], mat)

    def line(self, a, b, r, mat="Rope"):
        """Rigging: a thin square strand."""
        self.tube(a, b, r, r, mat, seg=4, cap=False)

    def ball(self, center, r, mat, sx=1.0, sy=1.0, sz=1.0, seg=10):
        m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((sx, sy, sz, 1))
        self.paint(bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=seg // 2, radius=r, matrix=m)["verts"], mat)

    def sheet(self, corners, mat, belly=0.0, nu=4, nv=3, thick=0.012, normal=None):
        """A sail: a grid between four corners (top-left, top-right, bottom-right, bottom-left), bellied along `normal`."""
        tl, tr, br, bl = (Vector(c) for c in corners)
        n = Vector(normal) if normal else (tr - tl).cross(bl - tl).normalized()
        grid = []
        for j in range(nv + 1):
            v = j / nv
            row = []
            for i in range(nu + 1):
                u = i / nu
                top = tl.lerp(tr, u)
                bot = bl.lerp(br, u)
                pt = top.lerp(bot, v) + n * belly * math.sin(math.pi * u) * (0.35 + 0.65 * math.sin(math.pi * min(1, v * 0.9 + 0.1)))
                row.append(self.bm.verts.new(pt))
            grid.append(row)
        faces = []
        for j in range(nv):
            for i in range(nu):
                faces.append(self.bm.faces.new((grid[j][i], grid[j + 1][i], grid[j + 1][i + 1], grid[j][i + 1])))
        res = bmesh.ops.solidify(self.bm, geom=faces, thickness=thick)
        verts = [v for row in grid for v in row] + [e for e in res["geom"] if isinstance(e, bmesh.types.BMVert)]
        self.paint(verts, mat)

    def tri_sheet(self, a, b, c, mat, belly=0.0, normal=(1, 0, 0), thick=0.012):
        """A triangular sail (a lateen or a jib)."""
        self.sheet((a, b, c, c), mat, belly=belly, nu=3, nv=3, thick=thick, normal=normal)

    def hull(self, L, B, D, H, sheer, top="Timber", bottom="Tar", band=None, deck="Deck", bow=0.3, stern=0.25,
             transom=0.55, n=18, m=6, rail=0.0):
        """
        A lofted hull, bow at -Y: `B` wide, `D` deep below the waterline, `H` freeboard amidships rising
        by `sheer` to the ends. Painted `bottom` below the waterline, `top` above, `band` (colour, z0, z1).
        Returns the deck height at a station y.
        """
        def width(t):  # t: 0 at the bow, 1 at the stern
            if t < bow:
                return max(0.02, math.sin(math.pi / 2 * t / bow) ** 0.9)
            if t > 1 - stern:
                s = (t - (1 - stern)) / stern
                return 1 - (1 - transom) * s ** 1.6
            return 1.0

        def height(t):
            return H + sheer * (2 * t - 1) ** 2

        rings = []
        for i in range(n + 1):
            t = i / n
            y = -L / 2 + L * t
            w = B / 2 * width(t)
            hz = height(t)
            d = D * (0.35 + 0.65 * width(t) ** 0.5)
            ring = []
            for k in range(-m, m + 1):
                th = abs(k) / m * math.pi / 2
                x = w * math.cos(th) ** 0.45 * (1 if k <= 0 else -1) * -1
                z = hz - (hz + d) * math.sin(th) ** 1.2
                ring.append(self.bm.verts.new((x if k != 0 else 0.0, y, z)))
            rings.append(ring)
        hull_faces = []
        for i in range(n):
            for k in range(2 * m):
                hull_faces.append(self.bm.faces.new((rings[i][k], rings[i][k + 1], rings[i + 1][k + 1], rings[i + 1][k])))
        hull_faces.append(self.bm.faces.new(rings[0]))  # bow (nearly closed)
        hull_faces.append(self.bm.faces.new(list(reversed(rings[-1]))))  # transom
        deck_faces = []
        for i in range(n):  # the deck, a little below the gunwale
            a0, a1 = rings[i][0], rings[i][-1]
            b0, b1 = rings[i + 1][0], rings[i + 1][-1]
            dz = Vector((0, 0, -0.03))
            q = [self.bm.verts.new(v.co + dz) for v in (a0, b0, b1, a1)]
            deck_faces.append(self.bm.faces.new(q))
        bmesh.ops.recalc_face_normals(self.bm, faces=hull_faces)
        for f in hull_faces:
            z = f.calc_center_median().z
            mat = bottom if z < 0.0 else top
            if band and band[1] <= z <= band[2]:
                mat = band[0]
            f.material_index = M[mat]
        for f in deck_faces:
            f.material_index = M[deck]
            if f.normal.z < 0:
                f.normal_flip()
        if rail:
            for i in range(n):
                for side in (0, -1):
                    a, b = rings[i][side].co, rings[i + 1][side].co
                    self.box(((a + b) / 2) + Vector((0, 0, rail / 2)), (0.025, (b - a).length + 0.01, rail), top,
                             Matrix.Rotation(math.atan2(b.z - a.z, b.y - a.y), 4, "X"))
        return lambda y: height((y + L / 2) / L) - 0.03

    def to_object(self, parent=None):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        for e in bm.edges:
            e.smooth = not (len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(40))
        for f in bm.faces:
            f.smooth = True
        occlusion(bm)
        ao = bm.verts.layers.float_color["Col"]
        col = bm.loops.layers.float_color.new("Color")
        for f in bm.faces:
            tint = COLORS[MATS[f.material_index]]
            for loop in f.loops:
                a = loop.vert[ao][0]
                loop[col] = (tint[0] * a, tint[1] * a, tint[2] * a, 1)
            f.material_index = 0
        bm.verts.layers.float_color.remove(ao)
        mesh = bpy.data.meshes.new(self.name)
        bm.to_mesh(mesh)
        bm.free()
        mesh.materials.append(bpy.data.materials["Detail"])
        mesh.color_attributes.active_color = mesh.color_attributes["Color"]
        mesh.color_attributes.render_color_index = mesh.color_attributes.active_color_index
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        if parent:
            obj.parent = parent
        return obj


def occlusion(bm):
    bm.normal_update()
    tree = BVHTree.FromBMesh(bm)
    n = 20
    dirs = []
    for i in range(n):
        z = 1 - (i + 0.5) / n
        r = math.sqrt(max(0.0, 1 - z * z))
        a = i * 2.39996
        dirs.append(Vector((math.cos(a) * r, math.sin(a) * r, z)))
    col = bm.verts.layers.float_color.new("Col")
    for v in bm.verts:
        nrm = v.normal if v.normal.length > 0 else Vector((0, 0, 1))
        rot = nrm.to_track_quat("Z", "Y")
        hits = sum(1 for d in dirs if tree.ray_cast(v.co + nrm * 0.004, rot @ d, 0.4)[0] is not None)
        c = 1 - 0.5 * (hits / n) ** 0.9
        v[col] = (c, c, c, 1)


def materials():
    mat = bpy.data.materials.new("Detail")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (1, 1, 1, 1)
    bsdf.inputs["Roughness"].default_value = 0.75


FUNNELS = []


def funnel(p, x, y, z0, h, r, colour, top="Black", rake=0.0):
    a = Vector((x, y, z0))
    b = Vector((x, y + rake * h, z0 + h))
    p.tube(a, b, r, r * 0.95, colour, seg=14)
    p.tube(b - (b - a).normalized() * 0.12 * h, b + Vector((0, 0, 0.005)), r * 0.96, r * 0.97, top, seg=14)
    FUNNELS.append(tuple(b + Vector((0, 0, 0.01))))


def mast(p, x, y, z0, h, r=0.03, mat="Timber"):
    p.tube((x, y, z0), (x, y, z0 + h), r, r * 0.6, mat, seg=8)


def yard(p, y, z, w, r=0.016, mat="Timber"):
    p.tube((-w / 2, y, z), (w / 2, y, z), r * 0.6, r * 0.6, mat, seg=6)
    p.ball((0, y, z), r, mat, seg=6)


def square_sail(p, y, z_top, z_bot, w_top, w_bot, belly=0.06, mat="Sail"):
    p.sheet(((-w_top / 2, y, z_top), (w_top / 2, y, z_top), (w_bot / 2, y, z_bot), (-w_bot / 2, y, z_bot)), mat,
            belly=belly, normal=(0, -1, 0))


def flag(p, x, y, z, w, h, mat="Flag"):
    p.sheet(((x, y, z), (x, y + w, z), (x, y + w, z - h), (x, y, z - h)), mat, belly=0.015, nu=3, nv=1, thick=0.006,
            normal=(1, 0, 0))


def portholes(p, side_x, ys, z, r=0.018, mat="Glass"):
    for s in (-1, 1):
        for y in ys:
            p.tube((s * side_x, y, z), (s * (side_x + 0.006), y, z), r, r, mat, seg=8)


# ── Ships ──

def galley(p):
    L, B = 4.0, 0.55
    deck = p.hull(L, B, 0.12, 0.2, 0.12, top="Timber", bottom="Tar", band=("Stripe", 0.13, 0.19), bow=0.25, stern=0.25,
                  transom=0.2)
    p.tube((0, -L / 2 + 0.05, 0.03), (0, -L / 2 - 0.3, 0.02), 0.07, 0.03, "Bronze", seg=8)  # ram
    for s in (-1, 1):
        p.ball((s * 0.14, -L / 2 + 0.3, 0.22), 0.04, "White", sx=0.5, seg=8)  # the painted eye
        p.ball((s * 0.162, -L / 2 + 0.3, 0.22), 0.02, "Black", sx=0.5, seg=8)
    # Sternpost curling up and forward
    pts = [(0, L / 2 - 0.05, 0.3), (0, L / 2 + 0.1, 0.45), (0, L / 2 + 0.15, 0.65), (0, L / 2 + 0.08, 0.8), (0, L / 2 - 0.02, 0.82)]
    for a, b in zip(pts, pts[1:]):
        p.tube(a, b, 0.04, 0.035, "Timber", seg=8)
    p.box((0, L / 2 - 0.35, deck(L / 2 - 0.35) + 0.08), (0.36, 0.4, 0.16), "Plank")  # helmsman's deck
    for s in (-1, 1):
        p.box((s * 0.22, L / 2 - 0.15, 0.2), (0.03, 0.04, 0.35), "Timber", Matrix.Rotation(0.5, 4, "X"))  # steering oars
    # Oars: two banks
    for k in range(14):
        y = -L / 2 + 0.6 + k * (L - 1.3) / 13
        for s in (-1, 1):
            for bank, (z, reach) in enumerate(((0.16, 0.75), (0.08, 0.6))):
                a = Vector((s * 0.27, y, z))
                b = Vector((s * (0.27 + reach), y + 0.05, -0.08))
                p.line(a, b, 0.008, "Plank")
                p.box(tuple(b), (0.02, 0.06, 0.03), "Plank")
    # Mast, yard and a striped square sail
    mast(p, 0, -0.1, deck(-0.1), 1.6, 0.035)
    yard(p, -0.12, deck(0) + 1.5, 1.4)
    square_sail(p, -0.14, deck(0) + 1.48, deck(0) + 0.55, 1.36, 1.2, belly=0.1)
    for x in (-0.36, 0.0, 0.36):
        p.sheet(((x - 0.06, -0.2, deck(0) + 1.47), (x + 0.06, -0.2, deck(0) + 1.47), (x + 0.06, -0.2, deck(0) + 0.57),
                 (x - 0.06, -0.2, deck(0) + 0.57)), "Stripe", belly=0.0, nu=1, nv=3, thick=0.004, normal=(0, -1, 0))
    p.line((0, -0.1, deck(0) + 1.6), (0, -L / 2 + 0.1, 0.3), 0.006)
    p.line((0, -0.1, deck(0) + 1.6), (0, L / 2 - 0.2, 0.4), 0.006)
    for s in (-1, 1):
        for k in range(8):  # shields along the rail
            p.tube((s * 0.27, -1.2 + k * 0.33, 0.24), (s * 0.29, -1.2 + k * 0.33, 0.24), 0.06, 0.06,
                   "Bronze" if k % 2 else "Stripe", seg=10)


def boat(p):
    L, B = 1.6, 0.5
    deck = p.hull(L, B, 0.12, 0.16, 0.06, top="Plank", bottom="Tar", band=("Blue", 0.12, 0.17), bow=0.35, stern=0.3, transom=0.25)
    for k in range(3):
        y = -0.4 + k * 0.4
        p.box((0, y, deck(y) - 0.02), (B * 0.8, 0.07, 0.025), "Plank")  # thwarts
    mast(p, 0, -0.25, deck(-0.25), 0.9, 0.022)
    # Lateen: a long slanting yard and a triangle of sail
    a, b = Vector((0.0, -0.95, deck(0) + 0.2)), Vector((0.0, 0.55, deck(0) + 1.05))
    p.tube(a, b, 0.012, 0.012, "Timber", seg=6)
    p.tri_sheet(a + Vector((0, 0.02, -0.01)), b + Vector((0, -0.02, -0.02)), Vector((0.0, 0.45, deck(0) + 0.15)), "Sail", belly=0.07)
    p.box((0.05, 0.45, deck(0.45) + 0.04), (0.25, 0.22, 0.06), "Rope")  # nets
    p.ball((-0.1, 0.5, deck(0.5) + 0.05), 0.05, "Crate", seg=8)  # a basket
    p.box((0, L / 2 - 0.05, 0.15), (0.03, 0.04, 0.3), "Timber", Matrix.Rotation(0.4, 4, "X"))  # rudder


def cog(p):
    L, B = 2.6, 0.95
    deck = p.hull(L, B, 0.25, 0.42, 0.14, top="Plank", bottom="Tar", band=("Timber", 0.2, 0.25), bow=0.3, stern=0.18,
                  transom=0.75, rail=0.06)
    for z in (0.05, 0.15, 0.3):  # clinker strakes
        for s in (-1, 1):
            p.box((s * (B / 2 + 0.005), 0, z), (0.012, L * 0.75, 0.012), "Timber")
    # Castles: a high aftcastle and a forecastle, crenellated
    for y0, y1, z in ((L / 2 - 0.6, L / 2 - 0.02, 0.3), (-L / 2 + 0.25, -L / 2 + 0.7, 0.24)):
        cy = (y0 + y1) / 2
        p.box((0, cy, deck(cy) + z / 2), (B * 0.85, y1 - y0, z), "Plank")
        p.box((0, cy, deck(cy) + z + 0.005), (B * 0.86, y1 - y0 + 0.01, 0.012), "Deck")
        for s in (-1, 1):
            for k in range(5):
                p.box((s * B * 0.42, y0 + (k + 0.5) * (y1 - y0) / 5, deck(cy) + z + 0.05), (0.03, 0.06, 0.08), "Plank")
        p.box((0, y1 - 0.01 if y0 > 0 else y0 + 0.01, deck(cy) + z * 0.4), (0.12, 0.012, 0.12), "Black")  # door
    mast(p, 0, -0.05, deck(0), 2.0, 0.045)
    p.tube((0, -0.05, deck(0) + 1.8), (0, -0.05, deck(0) + 1.92), 0.1, 0.12, "Plank", seg=10)  # crow's nest
    yard(p, -0.1, deck(0) + 1.7, 1.5)
    square_sail(p, -0.12, deck(0) + 1.68, deck(0) + 0.45, 1.46, 1.4, belly=0.16)
    p.sheet(((-0.08, -0.31, deck(0) + 1.45), (0.08, -0.31, deck(0) + 1.45), (0.08, -0.31, deck(0) + 0.65), (-0.08, -0.31, deck(0) + 0.65)),
            "Stripe", nu=1, nv=3, thick=0.004, normal=(0, -1, 0), belly=0.0)
    p.sheet(((-0.45, -0.3, deck(0) + 1.12), (0.45, -0.3, deck(0) + 1.12), (0.45, -0.3, deck(0) + 0.98), (-0.45, -0.3, deck(0) + 0.98)),
            "Stripe", nu=3, nv=1, thick=0.004, normal=(0, -1, 0), belly=0.0)
    p.tube((0, -L / 2 + 0.05, 0.45), (0, -L / 2 - 0.45, 0.75), 0.025, 0.018, "Timber", seg=6)  # bowsprit
    for s in (-1, 1):
        p.line((0, -0.05, deck(0) + 1.95), (s * B * 0.48, 0.25, deck(0.25) + 0.05), 0.006)  # shrouds
        p.line((0, -0.05, deck(0) + 1.95), (s * B * 0.48, -0.05, deck(0) + 0.05), 0.006)
    p.line((0, -0.05, deck(0) + 1.95), (0, -L / 2 - 0.4, 0.73), 0.006)
    flag(p, 0, -0.05, deck(0) + 2.2, 0.3, 0.16)
    p.box((0, L / 2 + 0.02, 0.2), (0.04, 0.08, 0.5), "Timber")  # stern rudder


def galleon(p):
    L, B = 4.2, 1.0
    deck = p.hull(L, B, 0.3, 0.42, 0.2, top="Timber", bottom="Tar", band=("Ochre", 0.18, 0.3), bow=0.28, stern=0.12,
                  transom=0.8, rail=0.05)
    for s in (-1, 1):  # gun ports
        for k in range(10):
            y = -1.3 + k * 0.28
            p.box((s * (B / 2 + 0.004), y, 0.24), (0.012, 0.07, 0.06), "Black")
    # Stern castle with windows, a gallery
    p.box((0, L / 2 - 0.45, deck(L / 2) + 0.18), (B * 0.88, 0.9, 0.36), "Timber")
    p.box((0, L / 2 - 0.45, deck(L / 2) + 0.37), (B * 0.9, 0.92, 0.02), "Deck")
    for k in range(4):
        p.box((-0.33 + k * 0.22, L / 2 - 0.0, deck(L / 2) + 0.2), (0.12, 0.012, 0.1), "Glass")
    p.box((0, L / 2 + 0.03, deck(L / 2) + 0.09), (B * 0.85, 0.05, 0.02), "Ochre")
    p.box((0, -L / 2 + 0.45, deck(-L / 2) + 0.12), (B * 0.7, 0.6, 0.24), "Timber")  # forecastle
    # Masts: fore, main, mizzen
    spars = ((-1.0, 2.6, 1.4), (0.15, 3.0, 1.6), (1.25, 2.1, 0))
    for y, h, w in spars:
        z0 = deck(y)
        mast(p, 0, y, z0, h, 0.04)
        p.tube((0, y, z0 + h * 0.55), (0, y, z0 + h * 0.58), 0.1, 0.1, "Plank", seg=10)  # top
        if w:
            for (zt, zb, wt, wb) in ((0.52, 0.12, w * 0.95, w), (0.88, 0.56, w * 0.75, w * 0.88), (0.99, 0.9, w * 0.5, w * 0.68)):
                yard(p, y - 0.03, z0 + h * zt, wt + 0.08)
                square_sail(p, y - 0.06, z0 + h * zt - 0.02, z0 + h * zb, wt, wb, belly=0.12)
        else:  # lateen mizzen
            a, b = Vector((0, y - 0.6, z0 + 0.4)), Vector((0, y + 0.55, z0 + h * 0.85))
            p.tube(a, b, 0.012, 0.012, "Timber", seg=6)
            p.tri_sheet(a, b, Vector((0, y + 0.5, z0 + 0.35)), "Sail", belly=0.06)
        for s in (-1, 1):
            p.line((0, y, z0 + h * 0.58), (s * B * 0.5, y + 0.15, deck(y) + 0.06), 0.006)
            p.line((0, y, z0 + h * 0.58), (s * B * 0.5, y - 0.1, deck(y) + 0.06), 0.006)
        flag(p, 0, y, z0 + h + 0.2, 0.28, 0.12, "Flag" if y < 1 else "Blue")
    p.tube((0, -L / 2 + 0.1, 0.55), (0, -L / 2 - 0.8, 1.0), 0.03, 0.02, "Timber", seg=6)  # bowsprit
    p.tri_sheet((0, -L / 2 - 0.75, 0.98), (0, -1.0, deck(-1.0) + 2.2), (0, -L / 2 + 0.05, 0.6), "Sail", belly=0.05)
    for y, h, w in spars[:2]:
        p.line((0, y, deck(y) + h), (0, -L / 2 - 0.7, 0.97), 0.006)
    p.ball((0, -L / 2 - 0.02, 0.38), 0.07, "Ochre", sx=0.6, seg=8)  # figurehead


def steamer(p):
    L, B = 4.6, 0.8
    deck = p.hull(L, B, 0.25, 0.32, 0.08, top="Black", bottom="Red", band=("White", 0.26, 0.3), bow=0.2, stern=0.15,
                  transom=0.6, rail=0.04)
    portholes(p, B / 2 + 0.002, [-1.4 + k * 0.25 for k in range(12)], 0.18)
    p.box((0, 0.2, deck(0) + 0.12), (B * 0.6, 1.4, 0.24), "White")  # deckhouse
    for k in range(7):
        for s in (-1, 1):
            p.box((s * B * 0.3, -0.4 + k * 0.2, deck(0) + 0.14), (0.012, 0.08, 0.07), "Glass")
    p.box((0, -0.55, deck(0) + 0.32), (B * 0.5, 0.25, 0.16), "White")  # wheelhouse
    p.box((0, -0.55, deck(0) + 0.41), (B * 0.56, 0.3, 0.02), "Plank")
    for k in range(3):
        p.box((-0.12 + k * 0.12, -0.68, deck(0) + 0.34), (0.08, 0.012, 0.07), "Glass")
    funnel(p, 0, 0.15, deck(0) + 0.24, 0.85, 0.11, "Buff", rake=0.18)
    for y, h in ((-1.4, 1.9), (1.3, 1.6)):  # schooner masts with furled sails
        z0 = deck(y)
        mast(p, 0, y, z0, h, 0.03)
        p.tube((0, y + 0.02, z0 + 0.35), (0, y + 0.65, z0 + 0.4), 0.014, 0.014, "Timber", seg=6)  # gaff boom
        p.tube((0, y + 0.03, z0 + 0.38), (0, y + 0.6, z0 + 0.42), 0.035, 0.03, "Sail", seg=6)  # furled
        yard(p, y, z0 + h * 0.75, 0.9)
        p.tube((-0.4, y + 0.01, z0 + h * 0.75 - 0.03), (0.4, y + 0.01, z0 + h * 0.75 - 0.03), 0.025, 0.025, "Sail", seg=6)
        for s in (-1, 1):
            p.line((0, y, z0 + h * 0.85), (s * B * 0.5, y + 0.15, z0 + 0.04), 0.005)
    p.line((0, -1.4, deck(-1.4) + 1.9), (0, 1.3, deck(1.3) + 1.6), 0.005)
    p.line((0, -1.4, deck(-1.4) + 1.9), (0, -L / 2 - 0.1, 0.4), 0.005)
    flag(p, 0, L / 2 - 0.05, deck(L / 2) + 0.5, 0.25, 0.14)
    p.line((0, L / 2 - 0.05, deck(L / 2)), (0, L / 2 - 0.05, deck(L / 2) + 0.5), 0.006, "Timber")
    for s in (-1, 1):  # lifeboats
        p.ball((s * B * 0.42, 0.9, deck(0.9) + 0.12), 0.07, "White", sx=0.9, sy=3.2, sz=0.6, seg=8)


def cargo(p):
    L, B = 5.0, 0.9
    deck = p.hull(L, B, 0.3, 0.36, 0.1, top="Grey", bottom="Red", band=("Black", 0.0, 0.08), bow=0.18, stern=0.14,
                  transom=0.65, rail=0.04)
    p.box((0, -L / 2 + 0.4, deck(-L / 2 + 0.4) + 0.08), (B * 0.8, 0.55, 0.16), "Grey")  # forecastle
    # Bridge amidships, tiers of white
    for k, (w, d, h) in enumerate(((0.75, 0.75, 0.2), (0.65, 0.55, 0.18), (0.55, 0.35, 0.16))):
        z = deck(0) + sum(x[2] for x in ((0.75, 0.75, 0.2), (0.65, 0.55, 0.18), (0.55, 0.35, 0.16))[:k])
        p.box((0, 0.1, z + h / 2), (B * w, d, h), "White")
        for s in (-1, 1):
            for j in range(int(d / 0.1)):
                p.box((s * B * w / 2, 0.1 - d / 2 + 0.06 + j * 0.1, z + h * 0.55), (0.012, 0.05, 0.06), "Glass")
        p.box((0, 0.1 - d / 2, z + h * 0.55), (B * w * 0.8, 0.012, 0.06), "Glass")
    funnel(p, 0, 0.45, deck(0) + 0.38, 0.6, 0.12, "Black", top="Red", rake=0.1)
    # Holds and derricks
    for y in (-1.5, -0.9, 1.0, 1.6):
        p.box((0, y, deck(y) + 0.04), (B * 0.55, 0.38, 0.08), "Green")
        p.box((0, y, deck(y) + 0.085), (B * 0.57, 0.4, 0.012), "Black")
    for y in (-1.2, 1.3):
        for s in (-1, 1):
            p.tube((s * 0.15, y, deck(y)), (s * 0.15, y, deck(y) + 0.9), 0.025, 0.02, "Grey", seg=8)  # kingposts
            p.tube((s * 0.15, y, deck(y) + 0.1), (s * 0.18, y + (0.35 if y < 0 else -0.35), deck(y) + 0.7), 0.012, 0.012, "Grey", seg=6)
        p.box((0, y, deck(y) + 0.88), (0.34, 0.03, 0.03), "Grey")
    for y in (-1.6, 1.7):
        mast(p, 0, y, deck(y), 1.2, 0.025, "Grey")
    p.line((0, -1.6, deck(-1.6) + 1.2), (0, 1.7, deck(1.7) + 1.2), 0.005)
    p.line((0, -1.6, deck(-1.6) + 1.2), (0, -L / 2 - 0.05, 0.5), 0.005)
    rng = random.Random(5)
    for k in range(5):  # deck cargo
        y = 0.75 + rng.uniform(-0.05, 0.05)
        p.box((rng.uniform(-0.25, 0.25), 0.6 + k * 0.05, deck(0.6) + 0.06), (0.15, 0.12, 0.12), "Crate")
    portholes(p, B / 2 + 0.002, [-0.1 + k * 0.12 for k in range(4)], 0.24)
    flag(p, 0, L / 2 - 0.05, deck(L / 2) + 0.45, 0.25, 0.14)
    p.line((0, L / 2 - 0.05, deck(L / 2)), (0, L / 2 - 0.05, deck(L / 2) + 0.45), 0.006, "Grey")


def liner(p):
    L, B = 7.0, 0.95
    deck = p.hull(L, B, 0.35, 0.45, 0.1, top="Black", bottom="Red", band=("White", 0.42, 0.47), bow=0.16, stern=0.16,
                  transom=0.55)
    portholes(p, B / 2 + 0.002, [-2.6 + k * 0.2 for k in range(27)], 0.3, r=0.016)
    portholes(p, B / 2 + 0.002, [-2.6 + k * 0.2 for k in range(27)], 0.2, r=0.014)
    z = deck(0)
    for k, (w, l) in enumerate(((0.92, 4.8), (0.86, 4.2), (0.78, 3.4), (0.6, 1.0))):  # tiers of decks
        h = 0.17
        cy = 0.1 if k < 3 else -1.3
        p.box((0, cy, z + h / 2), (B * w, l, h), "White")
        for s in (-1, 1):
            n = int(l / 0.11)
            p.box((s * B * w / 2, cy, z + h * 0.55), (0.008, l - 0.2, 0.05), "Glass")  # a continuous row of windows
            for j in range(n):
                p.box((s * (B * w / 2 + 0.002), cy - l / 2 + 0.1 + j * (l - 0.2) / max(1, n - 1), z + h * 0.55),
                      (0.008, 0.02, 0.055), "White")
        p.box((0, cy, z + h + 0.004), (B * w + 0.02, l + 0.02, 0.008), "Deck")
        if k == 2:
            p.box((0, cy - l / 2, z + h * 0.55), (B * w * 0.9, 0.008, 0.06), "Glass")
        if k < 3:
            z += h
    for i, y in enumerate((-0.9, 0.2, 1.3)):
        funnel(p, 0, y, z, 0.75, 0.15, "Red", top="Black", rake=0.15)
    for s in (-1, 1):  # lifeboats on davits
        for k in range(7):
            y = -1.8 + k * 0.55
            p.ball((s * B * 0.43, y, deck(0) + 0.53), 0.06, "White", sx=0.9, sy=3.0, sz=0.55, seg=8)
            p.tube((s * B * 0.38, y, deck(0) + 0.5), (s * B * 0.43, y, deck(0) + 0.62), 0.006, 0.006, "Grey", seg=4)
    for y in (-2.6, 2.6):
        mast(p, 0, y, deck(y), 1.6, 0.025, "Buff")
    p.line((0, -2.6, deck(-2.6) + 1.6), (0, 2.6, deck(2.6) + 1.6), 0.005)
    p.line((0, -2.6, deck(-2.6) + 1.6), (0, -L / 2, 0.6), 0.005)
    flag(p, 0, L / 2 - 0.05, deck(L / 2) + 0.5, 0.28, 0.15)
    p.line((0, L / 2 - 0.05, deck(L / 2)), (0, L / 2 - 0.05, deck(L / 2) + 0.5), 0.006, "Grey")


def trawler(p):
    L, B = 1.9, 0.55
    deck = p.hull(L, B, 0.16, 0.2, 0.08, top="Blue", bottom="Red", band=("White", 0.17, 0.21), bow=0.3, stern=0.2, transom=0.7,
                  rail=0.03)
    p.box((0, -0.15, deck(-0.15) + 0.14), (B * 0.6, 0.35, 0.28), "White")  # wheelhouse
    p.box((0, -0.15, deck(-0.15) + 0.29), (B * 0.66, 0.4, 0.02), "Grey")
    p.box((0, -0.33, deck(-0.15) + 0.2), (B * 0.5, 0.012, 0.08), "Glass")
    for s in (-1, 1):
        p.box((s * B * 0.3, -0.15, deck(-0.15) + 0.2), (0.012, 0.25, 0.07), "Glass")
    funnel(p, 0, 0.12, deck(0.12) + 0.05, 0.32, 0.05, "Black", top="Red")
    mast(p, 0, -0.45, deck(-0.45), 0.8, 0.018, "Grey")
    p.box((0, -0.45, deck(-0.45) + 0.6), (0.3, 0.02, 0.02), "Grey")
    for s in (-1, 1):  # stern gantry
        p.tube((s * 0.2, 0.8, deck(0.8)), (s * 0.2, 0.85, deck(0.8) + 0.45), 0.015, 0.015, "Grey", seg=6)
    p.tube((-0.2, 0.85, deck(0.8) + 0.45), (0.2, 0.85, deck(0.8) + 0.45), 0.015, 0.015, "Grey", seg=6)
    p.tube((0.0, 0.5, deck(0.5) + 0.06), (0, 0.6, deck(0.5) + 0.06), 0.06, 0.06, "Iron", seg=10)  # winch
    p.box((0.05, 0.6, deck(0.6) + 0.04), (0.3, 0.2, 0.06), "Rope")  # nets
    for k in range(3):
        p.box((-0.15 + k * 0.1, 0.35, deck(0.35) + 0.04), (0.08, 0.1, 0.07), "Crate")


# ── Quays (a 1 x 1 patch of shore, the water at -Y) ──

def quay_wood(p):
    for k in range(5):  # a pier on piles running out into the water
        y = -0.1 - k * 0.22
        for s in (-1, 1):
            p.tube((s * 0.13, y, -0.15), (s * 0.13, y, 0.1), 0.025, 0.025, "Timber", seg=6)
    for k in range(10):
        p.box((0, -0.05 - k * 0.11, 0.11), (0.34, 0.1, 0.02), "Plank")
    p.box((0, 0.25, 0.05), (0.9, 0.5, 0.1), "Plank")  # the landing
    for k in range(9):
        p.box((-0.4 + k * 0.1, 0.25, 0.101), (0.008, 0.5, 0.004), "Timber")
    # A treadwheel crane
    p.box((0.25, 0.25, 0.25), (0.08, 0.08, 0.3), "Timber")
    p.tube((0.25, 0.15, 0.25), (0.25, 0.35, 0.25), 0.15, 0.15, "Timber", seg=14, cap=False)
    p.tube((0.25, 0.25, 0.4), (0.25, -0.15, 0.62), 0.02, 0.016, "Timber", seg=6)
    p.line((0.25, -0.15, 0.62), (0.25, -0.15, 0.3), 0.004)
    for x, y in ((-0.25, 0.3), (-0.33, 0.2), (-0.18, 0.18)):
        p.tube((x, y, 0.1), (x, y, 0.19), 0.04, 0.04, "Crate", seg=10)  # barrels
        p.tube((x, y, 0.13), (x, y, 0.135), 0.043, 0.043, "Iron", seg=10)
    p.box((-0.05, 0.35, 0.15), (0.1, 0.1, 0.1), "Crate")
    for s in (-1, 1):
        p.tube((s * 0.17, -1.0, 0.1), (s * 0.17, -1.0, 0.17), 0.018, 0.02, "Timber", seg=6)  # bollards


def quay_stone(p):
    p.box((0, 0.1, -0.05), (1.4, 0.8, 0.3), "Stone")  # quay wall
    p.box((0, -0.3, 0.095), (1.4, 0.04, 0.01), "Stone")
    for k in range(4):  # steps down to the water
        p.box((-0.55, -0.32 - k * 0.04, 0.07 - k * 0.06), (0.15, 0.04, 0.03), "Stone")
    for x in (-0.3, 0.1, 0.5):
        p.tube((x, -0.27, 0.1), (x, -0.27, 0.15), 0.022, 0.026, "Iron", seg=8)  # bollards
        p.ball((x, -0.27, 0.155), 0.028, "Iron", seg=8)
    # Warehouse
    p.box((-0.25, 0.3, 0.27), (0.7, 0.35, 0.34), "Brick")
    rise = 0.12
    for s in (-1, 1):
        ang = math.atan2(rise, 0.18)
        p.box((-0.25, 0.3 + s * 0.09, 0.44 + rise / 2), (0.74, math.hypot(0.19, rise), 0.02), "Slate", Matrix.Rotation(-s * ang, 4, "X"))
    for k in range(3):
        p.box((-0.48 + k * 0.23, 0.12, 0.25), (0.14, 0.012, 0.22), "Timber")  # loading doors
    # An iron crane on rails
    p.box((0.4, 0.05, 0.12), (0.2, 0.2, 0.04), "Iron")
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.line((0.4 + sx * 0.08, 0.05 + sy * 0.08, 0.12), (0.4, 0.05, 0.55), 0.01, "Iron")
    p.box((0.4, 0.05, 0.6), (0.14, 0.14, 0.1), "Iron")
    p.tube((0.4, 0.05, 0.62), (0.4, -0.55, 0.85), 0.018, 0.012, "Iron", seg=6)  # jib
    p.line((0.4, 0.05, 0.75), (0.4, -0.55, 0.85), 0.004, "Iron")
    p.line((0.4, -0.55, 0.85), (0.4, -0.55, 0.3), 0.003, "Rope")
    p.box((0.4, -0.55, 0.28), (0.12, 0.1, 0.08), "Crate")
    for k in range(6):
        p.box((-0.1 + (k % 3) * 0.12, -0.12 + (k // 3) * 0.12, 0.15 + 0.0), (0.1, 0.1, 0.1), "Crate" if k % 2 else "Green")


MODELS = {
    "galley": galley, "boat": boat, "cog": cog, "galleon": galleon, "steamer": steamer, "cargo": cargo,
    "liner": liner, "trawler": trawler, "quay_wood": quay_wood, "quay_stone": quay_stone,
}


def build(name, fn, path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials()
    FUNNELS.clear()
    root = bpy.data.objects.new("Ship", None)
    bpy.context.scene.collection.objects.link(root)
    p = Part("Body")
    fn(p)
    obj = p.to_object(root)
    for i, at in enumerate(FUNNELS):
        e = bpy.data.objects.new(f"Funnel{i}", None)
        bpy.context.scene.collection.objects.link(e)
        e.parent = root
        e.location = at
    tris = sum(len(poly.vertices) - 2 for poly in obj.data.polygons)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_vertex_color="ACTIVE", export_yup=True,
                              export_animations=False)
    if PREVIEW:
        preview(os.path.join(PREVIEW, name + ".png"), obj)
    return tris


def preview(png, obj):
    mat = bpy.data.materials["Detail"]
    nt = mat.node_tree
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Color"
    nt.links.new(attr.outputs["Color"], nt.nodes.get("Principled BSDF").inputs["Base Color"])
    scene = bpy.context.scene
    lo = Vector([min(v.co[i] for v in obj.data.vertices) for i in range(3)])
    hi = Vector([max(v.co[i] for v in obj.data.vertices) for i in range(3)])
    centre, size = (lo + hi) / 2, (hi - lo).length
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.location = centre + Vector((-1.2, -0.7, 0.55)) * size * 0.95
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50
    scene.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.45, 0.5, 0.55, 1)
    scene.world = world
    sea = bpy.data.meshes.new("sea")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=size * 2)
    bm.to_mesh(sea)
    seaobj = bpy.data.objects.new("sea", sea)
    seamat = bpy.data.materials.new("sea")
    seamat.diffuse_color = (0.1, 0.2, 0.3, 1)
    seamat.use_nodes = True
    seamat.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (0.08, 0.17, 0.26, 1)
    sea.materials.append(seamat)
    scene.collection.objects.link(seaobj)
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 560, 400
    scene.render.filepath = png
    bpy.ops.render.render(write_still=True)


def main():
    n = 0
    for name, fn in MODELS.items():
        if ONLY and not name.startswith(ONLY):
            continue
        tris = build(name, fn, os.path.join(OUT, name + ".glb"))
        n += 1
        print(f"{name}: {tris} triangles")
    print(f"built {n} ships and quays")


main()
