"""
Builds Politicom's town buildings (Game/models/buildings/*.glb) in Blender, from code, in the style
of each age:

- ancient: Roman houses with low hipped tile roofs, porticoes and pergolas; insulae with shops,
  wooden balconies and shuttered windows; a temple on a podium with a ring of columns.
- medieval: thatched cottages; timber-framed houses with jettied upper floors and steep gables;
  stone houses with crow-stepped gables; a church with nave, aisles, apse and spired tower.
- early modern: plastered houses with quoins and dormers; brick gable houses with white-framed
  windows; tall town houses with iron balconies; a domed baroque church with bell towers.
- industrial: brick terraces with chimney pots; workers' cottages; Haussmann apartments with
  mansard roofs and wrought-iron balconies; tenements with shopfronts; a town hall with a clock
  tower; a brick factory with saw-tooth roof lights and a tall chimney.
- modern: suburban houses, flat-roofed villas with ribbon windows, apartment blocks with
  balconies, brick blocks over shops; a people's house with a tower; a concrete factory; office
  and department-store blocks.
- ruins in stone or brick for occupied towns.

Houses are about one unit square (the game scales them to the town plan), the front facing -Y in
Blender (+Z in Godot), standing on the ground at z = 0. The materials "Wall" and "Roof" take the
town's colours in the game; the rest keep their own. Factories have an empty named "Chimney" at
the top of the stack for the game's smoke. Shading (ambient occlusion) is baked into vertex colours.

Usage:
  blender --background --factory-startup --python tools/models/build_buildings.py -- OUTPUT_DIR [--preview DIR] [--only NAME]
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
OUT = ARGS[0] if ARGS else "Game/models/buildings"
PREVIEW = ARGS[ARGS.index("--preview") + 1] if "--preview" in ARGS else None
ONLY = ARGS[ARGS.index("--only") + 1] if "--only" in ARGS else None

COLORS = {
    "Wall": (0.80, 0.74, 0.62), "Roof": (0.55, 0.26, 0.18), "Trim": (0.86, 0.83, 0.76),
    "Stone": (0.62, 0.58, 0.51), "Brick": (0.50, 0.24, 0.17), "Timber": (0.22, 0.15, 0.10),
    "Wood": (0.42, 0.30, 0.19), "Door": (0.30, 0.18, 0.11), "Glass": (0.13, 0.16, 0.19),
    "Shutter": (0.30, 0.40, 0.32), "Thatch": (0.62, 0.52, 0.32), "Slate": (0.27, 0.29, 0.33),
    "Zinc": (0.47, 0.50, 0.54), "Iron": (0.07, 0.07, 0.08), "Concrete": (0.66, 0.65, 0.62),
    "Copper": (0.36, 0.56, 0.48), "Gold": (0.80, 0.63, 0.26), "Awning": (0.55, 0.15, 0.12),
    "Char": (0.09, 0.08, 0.07), "Rubble": (0.52, 0.48, 0.43), "Lead": (0.40, 0.42, 0.45),
}
MATS = list(COLORS)
TINTED = ["Wall", "Roof"]
M = {n: i for i, n in enumerate(MATS)}


class Part:
    """One mesh being built from boxes, tubes, prisms and roofs, each placed through the current frame `xf`."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.xf = Matrix.Identity(4)

    def paint(self, verts, mat):
        for f in {f for v in verts for f in v.link_faces}:
            f.material_index = M[mat]

    def frame(self, at=(0, 0, 0), turn=0.0):
        """Sets the frame: later parts are placed at `at`, turned `turn` radians about Z."""
        self.xf = Matrix.Translation(Vector(at)) @ Matrix.Rotation(turn, 4, "Z")
        return self

    def box(self, center, size, mat, rot=None):
        m = self.xf @ Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1))
        self.paint(bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)["verts"], mat)

    def tube(self, a, b, r1, r2, mat, seg=12, cap=True):
        a, b = Vector(a), Vector(b)
        d = b - a
        rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
        m = self.xf @ Matrix.Translation((a + b) / 2) @ rot
        res = bmesh.ops.create_cone(self.bm, cap_ends=cap, cap_tris=False, segments=seg, radius1=r1, radius2=r2,
                                    depth=d.length, matrix=m)
        self.paint(res["verts"], mat)

    def ball(self, center, r, mat, sz=1.0, seg=16, half=False):
        m = self.xf @ Matrix.Translation(Vector(center)) @ Matrix.Diagonal((1, 1, sz, 1))
        verts = bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=seg // 2, radius=r, matrix=m)["verts"]
        if half:
            cz = (self.xf @ Vector(center)).z
            bmesh.ops.delete(self.bm, geom=[v for v in verts if v.co.z < cz - 1e-4], context="VERTS")
            verts = [v for v in verts if v.is_valid]
        self.paint(verts, mat)

    def solid(self, bottom, top, mat):
        """A solid between two loops of points (counter-clockwise seen from above), e.g. a frustum or a roof."""
        vb = [self.bm.verts.new(self.xf @ Vector(p)) for p in bottom]
        vt = [self.bm.verts.new(self.xf @ Vector(p)) for p in top]
        n = len(vb)
        faces = [self.bm.faces.new(list(reversed(vb)))]
        if len({tuple(round(c, 5) for c in v.co) for v in vt}) > 2:
            faces.append(self.bm.faces.new(vt))
        for i in range(n):
            j = (i + 1) % n
            quad = [vb[i], vb[j], vt[j], vt[i]]
            if (vt[i].co - vt[j].co).length < 1e-6:
                quad = [vb[i], vb[j], vt[i]]
            faces.append(self.bm.faces.new(quad))
        bmesh.ops.remove_doubles(self.bm, verts=vb + vt, dist=1e-6)
        self.paint([v for v in vb + vt if v.is_valid], mat)

    def gable_end(self, x, half, z0, rise, mat, thick=0.02):
        """A triangular gable wall at x, spanning y = -half..half, apex `rise` above z0."""
        pts = [(x - thick / 2, -half, z0), (x - thick / 2, half, z0), (x - thick / 2, 0, z0 + rise)]
        top = [(x + thick / 2, -half, z0), (x + thick / 2, half, z0), (x + thick / 2, 0, z0 + rise)]
        vb = [self.bm.verts.new(self.xf @ Vector(p)) for p in pts]
        vt = [self.bm.verts.new(self.xf @ Vector(p)) for p in top]
        self.bm.faces.new(vb)
        self.bm.faces.new(list(reversed(vt)))
        for i in range(3):
            j = (i + 1) % 3
            self.bm.faces.new([vb[j], vb[i], vt[i], vt[j]])
        self.paint(vb + vt, mat)

    def gable(self, w, d, z0, rise, mat, over=0.06, thick=0.035, courses=4, ends=None, cx=0.0, cy=0.0):
        """A pitched roof, ridge along X: two slabs with tile courses; gable walls of `ends` under it."""
        half = d / 2 + over
        ang = math.atan2(rise, d / 2)
        slope = math.hypot(half, rise * half / (d / 2))
        for s in (-1, 1):
            rot = Matrix.Rotation(-s * ang, 4, "X")
            mid = Vector((cx, cy + s * half / 2, z0 + rise - rise * half / (d / 2) / 2)) + rot @ Vector((0, 0, thick / 2))
            self.box(mid, (w + 2 * over, slope, thick), mat, rot)
            for k in range(1, courses):  # tile courses: a shallow step every so often
                t = k / courses
                c = Vector((cx, cy + s * half * t, z0 + rise - rise * half / (d / 2) * t)) + rot @ Vector((0, 0, thick + 0.006))
                self.box(c, (w + 2 * over, 0.02, 0.012), mat, rot)
        self.box((cx, cy, z0 + rise + thick * 0.6), (w + 2 * over + 0.01, 0.05, 0.04), mat)  # ridge
        if ends:
            for x in (-w / 2, w / 2):
                self.gable_end(cx + x, d / 2, z0, rise, ends)

    def hip(self, w, d, z0, rise, mat, over=0.06, ridge=None):
        """A hipped roof over w x d, its ridge `ridge` long along X (0 for a pyramid)."""
        W, D = w / 2 + over, d / 2 + over
        r = (w - d) / 2 if ridge is None else ridge / 2
        r = max(r, 0.0)
        drop = rise * over / (d / 2)
        bottom = [(-W, -D, z0 - drop), (W, -D, z0 - drop), (W, D, z0 - drop), (-W, D, z0 - drop)]
        top = [(-r, 0, z0 + rise), (r, 0, z0 + rise), (r, 0, z0 + rise), (-r, 0, z0 + rise)]
        self.solid(bottom, top, mat)
        self.box((0, 0, z0 + rise), (2 * r + 0.04, 0.04, 0.03), mat)

    def mansard(self, w, d, z0, lower, upper, inset, mat_lower, mat_upper):
        """A mansard: steep lower slopes `lower` high stepping in `inset`, then a low hipped top."""
        bot = [(-w / 2, -d / 2, z0), (w / 2, -d / 2, z0), (w / 2, d / 2, z0), (-w / 2, d / 2, z0)]
        top = [(-w / 2 + inset, -d / 2 + inset, z0 + lower), (w / 2 - inset, -d / 2 + inset, z0 + lower),
               (w / 2 - inset, d / 2 - inset, z0 + lower), (-w / 2 + inset, d / 2 - inset, z0 + lower)]
        self.solid(bot, top, mat_lower)
        self.hip(w - 2 * inset, d - 2 * inset, z0 + lower, upper, mat_upper, over=0.0)

    def to_object(self, parent=None):
        bm = self.bm
        for e in bm.edges:
            e.smooth = not (len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(35))
        for f in bm.faces:
            f.smooth = True
        occlusion(bm)
        # Every colour but the town's (Wall, Roof) is baked into the vertex colours under one "Detail"
        # material, so a building draws in three calls.
        ao = bm.verts.layers.float_color["Col"]
        for v in bm.verts:
            if not v.link_faces:
                continue
            name = MATS[v.link_faces[0].material_index]
            tint = (1, 1, 1) if name in TINTED else COLORS[name]
            a = v[ao][0]
            v[ao] = (tint[0] * a, tint[1] * a, tint[2] * a, 1)
        for f in bm.faces:
            name = MATS[f.material_index]
            f.material_index = TINTED.index(name) if name in TINTED else len(TINTED)
        # One object per material: Blender's glTF exporter writes vertex colours only for a mesh's first material.
        objs = []
        for index, mat in enumerate(TINTED + ["Detail"]):
            part = bm.copy()
            bmesh.ops.delete(part, geom=[f for f in part.faces if f.material_index != index], context="FACES")
            if not part.faces:
                part.free()
                continue
            for f in part.faces:
                f.material_index = 0
            mesh = bpy.data.meshes.new(f"{self.name}_{mat}")
            part.to_mesh(mesh)
            part.free()
            mesh.materials.append(bpy.data.materials[mat])
            mesh.color_attributes.active_color = mesh.color_attributes["Col"]
            mesh.color_attributes.render_color_index = mesh.color_attributes.active_color_index
            obj = bpy.data.objects.new(f"{self.name}_{mat}", mesh)
            bpy.context.scene.collection.objects.link(obj)
            if parent:
                obj.parent = parent
            objs.append(obj)
        bm.free()
        return objs


def occlusion(bm):
    """Ambient occlusion per vertex, baked into the "Col" colour attribute; the ground counts as cover."""
    bm.normal_update()
    tree = BVHTree.FromBMesh(bm)
    n = 24
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
        hits = 0
        for d in dirs:
            w = rot @ d
            o = v.co + nrm * 0.004
            if tree.ray_cast(o, w, 0.4)[0] is not None or (w.z < 0 and o.z / -w.z < 0.4):
                hits += 1
        c = 1 - 0.6 * (hits / n) ** 0.85
        v[col] = (c, c, c, 1)


def materials():
    for name, c in COLORS.items():
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (*c, 1)
        metal = name in ("Gold", "Iron", "Zinc", "Lead")
        bsdf.inputs["Metallic"].default_value = 0.5 if metal else 0.0
        bsdf.inputs["Roughness"].default_value = 0.25 if name == "Glass" else (0.5 if metal else 0.85)
        mat.diffuse_color = (*c, 1)
    detail = bpy.data.materials.new("Detail")
    detail.use_nodes = True
    detail.node_tree.nodes.get("Principled BSDF").inputs["Roughness"].default_value = 0.85
    detail.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (1, 1, 1, 1)


# ── Facade pieces. In a face frame (Part.frame): the wall surface is y = 0, outward is -Y, X runs along it. ──

def window(p, x, z, w, h, frame="Trim", shutters=None, sill=True, cross=False, lintel=True, glass="Glass"):
    p.box((x, 0.004, z), (w, 0.02, h), glass)
    if frame:
        for s in (-1, 1):
            p.box((x + s * (w / 2 + 0.008), -0.006, z), (0.016, 0.02, h + 0.02), frame)
        p.box((x, -0.006, z + h / 2 + 0.008), (w + 0.03, 0.02, 0.016), frame)
    if sill:
        p.box((x, -0.016, z - h / 2 - 0.01), (w + 0.05, 0.04, 0.02), "Trim" if frame != "Timber" else "Timber")
    if lintel and frame:
        p.box((x, -0.012, z + h / 2 + 0.03), (w + 0.06, 0.03, 0.03), frame)
    if cross:
        p.box((x, -0.004, z), (0.01, 0.012, h), frame or "Trim")
        p.box((x, -0.004, z + h * 0.15), (w, 0.012, 0.01), frame or "Trim")
    if shutters:
        for s in (-1, 1):
            p.box((x + s * (w * 0.75 + 0.01), -0.01, z), (w / 2, 0.012, h), shutters)


def door(p, x, w, h, mat="Door", frame="Trim", step=True, fan=False, canopy=None):
    p.box((x, 0.0, h / 2), (w, 0.02, h), mat)
    if frame:
        for s in (-1, 1):
            p.box((x + s * (w / 2 + 0.012), -0.008, h / 2), (0.024, 0.024, h + 0.02), frame)
        p.box((x, -0.008, h + 0.012), (w + 0.05, 0.024, 0.024), frame)
    if fan:
        p.box((x, 0.002, h + 0.05), (w, 0.02, 0.06), "Glass")
        p.box((x, -0.008, h + 0.09), (w + 0.05, 0.024, 0.02), frame or "Trim")
    if step:
        p.box((x, -0.04, 0.015), (w + 0.1, 0.08, 0.03), "Stone")
    if canopy:
        p.box((x, -0.06, h + (0.13 if fan else 0.06)), (w + 0.12, 0.12, 0.02), canopy)


def timber_frame(p, x0, x1, z0, z1, braces=True):
    """Posts, rails and braces of a timber-framed storey, standing just proud of the wall."""
    n = max(2, round((x1 - x0) / 0.16))
    for i in range(n + 1):
        x = x0 + (x1 - x0) * i / n
        p.box((x, -0.008, (z0 + z1) / 2), (0.022, 0.016, z1 - z0), "Timber")
    for z in (z0 + 0.012, (z0 + z1) / 2 + 0.03, z1 - 0.012):
        p.box(((x0 + x1) / 2, -0.008, z), (x1 - x0 + 0.02, 0.016, 0.022), "Timber")
    if braces:
        span = (x1 - x0) / n
        ang = math.atan2((z1 - z0) / 2, span)
        for i in (0, n - 1):
            cx = x0 + span * (i + 0.5)
            s = 1 if i == 0 else -1
            p.box((cx, -0.009, z0 + (z1 - z0) * 0.27), (math.hypot(span, (z1 - z0) / 2), 0.016, 0.02), "Timber",
                  Matrix.Rotation(s * ang, 4, "Y"))


def quoins(p, x, z0, z1, mat="Trim"):
    n = max(2, int((z1 - z0) / 0.07))
    for i in range(n):
        z = z0 + (i + 0.5) * (z1 - z0) / n
        wide = 0.07 if i % 2 == 0 else 0.045
        p.box((x, -0.006, z), (wide, 0.016, (z1 - z0) / n - 0.01), mat)


def faces(w, d):
    """The four face frames of a w x d block centred at the origin: front, right, back, left (at, turn, width)."""
    return [((0, -d / 2, 0), 0.0, w), ((w / 2, 0, 0), math.pi / 2, d), ((0, d / 2, 0), math.pi, w), ((-w / 2, 0, 0), -math.pi / 2, d)]


def walls(p, w, d, h, mat="Wall", z0=0.0):
    p.frame()
    p.box((0, 0, z0 + h / 2), (w, d, h), mat)


def chimney(p, x, y, z0, h, mat="Brick", pots=0, w=0.09):
    p.frame()
    p.box((x, y, z0 + h / 2), (w, w * 0.8, h), mat)
    p.box((x, y, z0 + h + 0.012), (w + 0.025, w * 0.8 + 0.025, 0.024), mat)
    for k in range(pots):
        px = x + (k - (pots - 1) / 2) * 0.035
        p.tube((px, y, z0 + h + 0.02), (px, y, z0 + h + 0.07), 0.013, 0.011, "Brick", seg=8)


def storey_windows(p, width, z, n, w, h, **kw):
    for i in range(n):
        x = -width / 2 + width * (i + 0.5) / n
        window(p, x, z, w, h, **kw)


def column(p, x, y, z0, h, r, mat="Trim", seg=12):
    p.tube((x, y, z0), (x, y, z0 + 0.03), r * 1.35, r * 1.3, mat, seg=seg)  # base
    p.tube((x, y, z0 + 0.03), (x, y, z0 + h - 0.03), r, r * 0.86, mat, seg=seg)  # shaft
    p.box((x, y, z0 + h - 0.015), (r * 2.6, r * 2.6, 0.03), mat)  # capital


# ── Ancient ──

def house_ancient_0(p):
    """A domus: whitewashed walls, small high windows, a low hipped roof of tiles, a portico at the door."""
    w, d, h = 0.96, 0.82, 0.5
    p.box((0, 0, 0.02), (w + 0.04, d + 0.04, 0.04), "Stone")
    walls(p, w, d, h)
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        p.box((0, -0.006, 0.07), (fw, 0.012, 0.06), "Stone")  # dado
        for x in ((-0.3, 0.3) if fw > 0.9 else (-0.22, 0.22)):
            window(p, x, 0.38, 0.07, 0.09, frame=None, sill=False, shutters="Wood")
    p.frame(faces(w, d)[0][0], 0)
    door(p, 0, 0.14, 0.26, frame="Trim", step=False)
    for x in (-0.13, 0.13):
        column(p, x, -0.14, 0.04, 0.3, 0.022)
    p.box((0, -0.11, 0.35), (0.34, 0.24, 0.04), "Trim")  # entablature
    p.gable(0.34, 0.24, 0.37, 0.07, "Roof", over=0.02, courses=2, ends="Trim", cy=-0.11)
    p.frame()
    p.hip(w, d, h, 0.18, "Roof", over=0.07)


def house_ancient_1(p):
    """A small farmhouse with a pergola of vines along one side and storage jars by the door."""
    w, d, h = 0.72, 0.62, 0.44
    p.frame((-0.1, 0.08, 0))
    p.box((0, 0, h / 2), (w, d, h), "Wall")
    p.box((0, 0, 0.03), (w + 0.03, d + 0.03, 0.06), "Stone")
    p.frame((-0.1, 0.08 - d / 2, 0))
    door(p, 0.12, 0.12, 0.24, frame="Wood", step=False)
    window(p, -0.16, 0.33, 0.07, 0.08, frame=None, sill=False, shutters="Wood")
    p.frame((-0.1, 0.08, 0))
    p.hip(w, d, h, 0.16, "Roof", over=0.06)
    # Pergola
    for x in (0.32, 0.5):
        for y in (-0.22, 0.05, 0.32):
            p.frame()
            p.box((x, y, 0.19), (0.025, 0.025, 0.38), "Wood")
    p.frame()
    for y in (-0.22, 0.05, 0.32):
        p.box((0.41, y, 0.385), (0.24, 0.022, 0.022), "Wood")
    for x in (0.3, 0.37, 0.44, 0.51):
        p.box((x, 0.05, 0.405), (0.018, 0.62, 0.016), "Wood")
    for x, y in ((0.36, -0.1), (0.45, 0.18), (0.4, 0.3)):
        p.box((x, y, 0.42), (0.1, 0.12, 0.02), "Shutter")  # vines
    for x in (-0.38, -0.31):
        p.tube((x, -0.3, 0.0), (x, -0.3, 0.1), 0.03, 0.035, "Roof", seg=10)
        p.ball((x, -0.3, 0.11), 0.03, "Roof", seg=10)


def insula(p, w, d, storeys, balcony, brick_base):
    sh = 0.38
    h = storeys * sh
    p.frame()
    p.box((0, 0, sh / 2), (w, d, sh), "Brick" if brick_base else "Wall")
    p.box((0, 0, sh + (h - sh) / 2), (w, d, h - sh), "Wall")
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        n = 3 if fw > 0.8 else 2
        for i in range(n):  # shops: wide openings with wooden shutters half drawn
            x = -fw / 2 + fw * (i + 0.5) / n
            p.box((x, 0.002, 0.14), (fw / n - 0.08, 0.02, 0.24), "Char")
            p.box((x, -0.008, 0.28), (fw / n - 0.05, 0.024, 0.04), "Trim")
            p.box((x - (fw / n - 0.08) / 4, -0.006, 0.14), ((fw / n - 0.08) / 2, 0.014, 0.24), "Wood")
        p.box((0, -0.01, sh), (fw + 0.02, 0.02, 0.025), "Trim")
        for k in range(1, storeys):
            storey_windows(p, fw, sh * k + 0.2, n, 0.08, 0.12, frame=None, sill=True, shutters="Wood")
    if balcony:
        p.frame(faces(w, d)[0][0], 0)
        p.box((0, -0.06, sh + 0.02), (w * 0.8, 0.12, 0.02), "Wood")
        p.box((0, -0.115, sh + 0.08), (w * 0.8, 0.012, 0.012), "Wood")
        for i in range(9):
            p.box((-w * 0.4 + w * 0.8 * i / 8, -0.115, sh + 0.05), (0.012, 0.012, 0.07), "Wood")
        for x in (-w * 0.38, w * 0.38):
            p.box((x, -0.1, sh / 2), (0.022, 0.022, sh), "Wood")
    p.frame()
    return h


def house_ancient_2(p):
    h = insula(p, 0.96, 0.84, 3, balcony=True, brick_base=False)
    p.hip(0.96, 0.84, h, 0.16, "Roof", over=0.06)


def house_ancient_3(p):
    h = insula(p, 0.9, 0.86, 3, balcony=False, brick_base=True)
    p.gable(0.9, 0.86, h, 0.16, "Roof", over=0.06, ends="Wall")
    p.frame(faces(0.9, 0.86)[0][0], 0)
    p.box((0, -0.07, 0.31), (0.86, 0.14, 0.018), "Roof")  # a tiled pent over the shops
    p.frame()


# ── Medieval ──

def house_medieval_0(p):
    """A cottage: rubble walls, a deep thatch, a stone chimney."""
    w, d, h = 0.84, 0.62, 0.36
    walls(p, w, d, h, "Wall")
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        if fw > 0.7:
            window(p, -0.22, 0.22, 0.09, 0.1, frame="Timber", sill=False, cross=True)
            window(p, 0.24, 0.22, 0.09, 0.1, frame="Timber", sill=False, cross=True)
    p.frame(faces(w, d)[0][0], 0)
    door(p, 0, 0.13, 0.26, mat="Wood", frame="Timber", step=False)
    p.frame()
    p.gable(w, d, h, 0.36, "Thatch", over=0.09, thick=0.08, courses=2, ends="Wall")
    p.box((0, 0, h + 0.36 + 0.07), (w + 0.2, 0.09, 0.05), "Thatch")
    chimney(p, w / 2 - 0.08, 0.0, h, 0.48, mat="Stone", w=0.11)


def jettied(p, w, d, storeys, gable_front):
    """A timber-framed house on a stone ground floor, each storey jutting a little over the street."""
    sh = 0.34
    p.frame()
    p.box((0, 0, sh / 2), (w, d, sh), "Stone")
    p.frame(faces(w, d)[0][0], 0)
    door(p, -w * 0.25, 0.13, 0.25, mat="Wood", frame="Timber", step=False)
    window(p, w * 0.18, 0.18, 0.18, 0.13, frame="Timber", sill=True, cross=True)
    jut = 0.0
    for k in range(1, storeys):
        jut += 0.035
        z0 = sh * k
        p.frame()
        p.box((0, -jut / 2, z0 + sh / 2), (w + 0.02, d + jut, sh), "Wall")
        p.box((0, -jut / 2, z0 + 0.01), (w + 0.04, d + jut + 0.02, 0.02), "Timber")  # jetty beam
        p.frame((0, -d / 2 - jut, 0), 0)
        timber_frame(p, -w / 2, w / 2, z0, z0 + sh)
        storey_windows(p, w, z0 + sh * 0.55, 2, 0.12, 0.12, frame="Timber", sill=False, cross=True, lintel=False)
        for side in (1, -1):
            p.frame((side * (w / 2 + 0.01), -jut / 2, 0), side * math.pi / 2)
            timber_frame(p, -d / 2 - jut / 2, d / 2 + jut / 2, z0, z0 + sh, braces=False)
    top = sh * storeys
    p.frame((0, -jut / 2, 0), math.pi / 2 if gable_front else 0)
    dd, ww = (w + 0.02, d + jut) if gable_front else (d + jut, w + 0.02)
    p.gable(dd, ww, top, 0.42 if gable_front else 0.34, "Roof", over=0.06, courses=5, ends="Wall")
    if gable_front:
        p.frame((0, -d / 2 - jut - 0.01, 0), 0)
        p.box((0, 0, top + 0.12), (0.02, 0.02, 0.24), "Timber")
        window(p, 0, top + 0.12, 0.08, 0.09, frame="Timber", sill=False)
        p.box((0, -0.07, top + 0.32), (0.025, 0.14, 0.025), "Timber")  # hoist beam
    return top


def house_medieval_1(p):
    top = jettied(p, 0.9, 0.74, 2, gable_front=False)
    chimney(p, 0.25, 0.1, top + 0.1, 0.3, mat="Stone")


def house_medieval_2(p):
    jettied(p, 0.74, 0.86, 3, gable_front=True)


def house_medieval_3(p):
    """A stone house with crow-stepped gables and paired windows."""
    w, d, h = 0.88, 0.72, 0.7
    walls(p, w, d, h, "Stone")
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        for z in (0.2, 0.52):
            for x in ((-0.2, 0.2) if fw > 0.8 else (0.0,)):
                window(p, x - 0.04, z, 0.055, 0.13, frame="Trim", sill=True, lintel=False)
                window(p, x + 0.04, z, 0.055, 0.13, frame="Trim", sill=False, lintel=False)
    p.frame(faces(w, d)[0][0], 0)
    door(p, 0, 0.14, 0.27, mat="Door", frame="Trim")
    p.frame()
    rise = 0.38
    p.gable(w, d, h, rise, "Roof", over=0.0, courses=5)
    for x in (-w / 2, w / 2):
        p.gable_end(x, d / 2, h, rise, "Stone", thick=0.05)
        for k in range(5):  # crow steps
            t = k / 5
            for s in (-1, 1):
                p.box((x, s * (d / 2) * (1 - t - 0.1), h + rise * t + 0.05), (0.07, d * 0.1, 0.06), "Stone")
        p.box((x, 0, h + rise + 0.05), (0.07, 0.08, 0.1), "Stone")
    chimney(p, -w / 2 + 0.02, 0.0, h + rise - 0.05, 0.2, mat="Stone", w=0.1)


# ── Early modern ──

def plastered(p, w, d, storeys, dormers, shutters="Shutter"):
    sh = 0.32
    h = storeys * sh
    walls(p, w, d, h)
    p.box((0, 0, 0.03), (w + 0.02, d + 0.02, 0.06), "Stone")
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        for x in (-fw / 2, fw / 2):
            quoins(p, x, 0.06, h)
        n = 3 if fw > 0.8 else 2
        for k in range(storeys):
            storey_windows(p, fw, sh * k + 0.17, n, 0.085, 0.15, shutters=shutters)
            if k:
                p.box((0, -0.008, sh * k), (fw, 0.016, 0.02), "Trim")  # string course
        p.box((0, -0.012, h - 0.01), (fw + 0.02, 0.03, 0.03), "Trim")  # cornice
    p.frame(faces(w, d)[0][0], 0)
    p.box((0, 0.002, 0.15), (0.16, 0.02, 0.28), "Door")
    p.box((0, -0.02, 0.315), (0.22, 0.04, 0.03), "Trim")  # pediment over the door
    p.gable(0.2, 0.04, 0.33, 0.04, "Trim", over=0.0, courses=1, cy=-0.02)
    p.frame()
    p.gable(w, d, h, 0.4, "Roof", over=0.05, courses=5, ends="Wall")
    for k in range(dormers):
        x = -w / 2 + w * (k + 0.5) / dormers
        p.frame((x, -d / 4 - 0.03, 0), 0)
        p.box((0, 0, h + 0.13), (0.12, 0.14, 0.14), "Wall")
        p.frame((x, -d / 4 - 0.1, 0), 0)
        window(p, 0, h + 0.12, 0.07, 0.09, sill=False, lintel=False)
        p.frame((x, -d / 4 - 0.03, 0), math.pi / 2)
        p.gable(0.15, 0.14, h + 0.2, 0.07, "Roof", over=0.02, courses=1)
    p.frame()
    for x in (-w / 2 + 0.07, w / 2 - 0.07):
        chimney(p, x, 0.0, h + 0.2, 0.3, mat="Brick")
    return h


def house_early_0(p):
    plastered(p, 0.94, 0.7, 2, dormers=2)


def house_early_1(p):
    """A brick house with its gable to the street, white-framed windows and a curved gable."""
    w, d, sh = 0.7, 0.86, 0.34
    h = sh * 2
    walls(p, w, d, h, "Brick")
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        n = 2
        for k in range(2):
            storey_windows(p, fw, sh * k + 0.19, n, 0.12, 0.18, frame="Trim", cross=True)
        p.box((0, -0.008, sh), (fw, 0.016, 0.02), "Trim")
    p.frame(faces(w, d)[0][0], 0)
    door(p, 0, 0.13, 0.26, fan=True)
    p.frame((0, 0, 0), math.pi / 2)
    p.gable(d, w, h, 0.42, "Roof", over=0.02, courses=5, ends="Brick")
    # The front gable stands above the roof line in steps and a curved top, with stone coping.
    p.frame((0, -d / 2, 0), 0)
    for k, (gw, gh) in enumerate(((0.66, 0.14), (0.46, 0.14), (0.28, 0.14))):
        z = h + k * 0.14
        p.box((0, 0.02, z + gh / 2), (gw, 0.05, gh), "Brick")
        p.box((0, 0.02, z + gh), (gw + 0.03, 0.065, 0.02), "Trim")
    p.ball((0, 0.02, h + 0.42), 0.13, "Brick", sz=0.7, half=True)
    p.box((0, 0.02, h + 0.53), (0.04, 0.04, 0.06), "Trim")
    window(p, 0, h + 0.2, 0.1, 0.12, cross=True)
    window(p, 0, h + 0.34, 0.06, 0.06, sill=False)
    p.frame()


def house_early_2(p):
    h = plastered(p, 0.9, 0.82, 3, dormers=2, shutters="Shutter")
    p.frame(faces(0.9, 0.82)[0][0], 0)
    for z in (0.32 + 0.02, 0.64 + 0.02):  # iron balconies on the upper floors
        p.box((0, -0.05, z), (0.5, 0.1, 0.015), "Stone")
        p.box((0, -0.095, z + 0.06), (0.5, 0.01, 0.01), "Iron")
        for i in range(11):
            p.box((-0.25 + 0.05 * i, -0.095, z + 0.03), (0.006, 0.006, 0.06), "Iron")


def house_early_3(p):
    """A merchant's house: three storeys of brick dressed in stone, a hipped roof with a lantern of dormers."""
    w, d, sh = 0.94, 0.84, 0.33
    h = sh * 3
    walls(p, w, d, h, "Wall")
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        p.box((0, -0.008, sh / 2), (fw, 0.016, sh), "Stone")  # rusticated ground floor
        for k in range(4):
            p.box((0, -0.018, 0.06 + k * 0.08), (fw, 0.008, 0.008), "Trim")
        for x in (-fw / 2, fw / 2):
            quoins(p, x, sh, h)
        for k in range(3):
            storey_windows(p, fw, sh * k + 0.18, 3, 0.09, 0.17 if k else 0.15, cross=k > 0)
        p.box((0, -0.015, h - 0.015), (fw + 0.03, 0.04, 0.04), "Trim")
    p.frame(faces(w, d)[0][0], 0)
    door(p, 0, 0.15, 0.27, fan=True)
    p.frame()
    p.hip(w, d, h, 0.3, "Roof", over=0.04)
    for at, turn, fw in faces(w * 0.7, d * 0.7)[:1] + faces(w * 0.7, d * 0.7)[2:3]:
        p.frame(at, turn)
        p.box((0, 0.04, h + 0.1), (0.1, 0.1, 0.1), "Wall")
        window(p, 0, h + 0.1, 0.06, 0.07, sill=False, lintel=False)
    p.frame()
    for x in (-0.3, 0.3):
        chimney(p, x, 0.0, h + 0.1, 0.3, mat="Brick", pots=2)


# ── Industrial ──

def terrace(p, w, d, units, storeys, slate=True):
    """Brick terrace houses side by side: sash windows under stone lintels, fanlit doors, chimney pots."""
    sh = 0.32
    h = sh * storeys
    walls(p, w, d, h, "Brick")
    uw = w / units
    for side in (0, 2):
        at, turn, fw = faces(w, d)[side]
        p.frame(at, turn)
        p.box((0, -0.006, 0.04), (fw, 0.012, 0.08), "Stone")
        for u in range(units):
            cx = -fw / 2 + uw * (u + 0.5)
            p.box((cx + uw / 2, -0.004, h / 2), (0.012, 0.008, h), "Trim") if u < units - 1 else None
            if side == 0:
                door(p, cx - uw * 0.22, 0.11, 0.23, mat="Shutter" if u % 2 else "Door", fan=True)
                window(p, cx + uw * 0.18, 0.17, 0.1, 0.16, frame="Trim", cross=True)
            else:
                window(p, cx, 0.17, 0.1, 0.16, frame="Trim", cross=True)
            for k in range(1, storeys):
                window(p, cx - uw * 0.2, sh * k + 0.17, 0.09, 0.16, frame="Trim", cross=True)
                window(p, cx + uw * 0.2, sh * k + 0.17, 0.09, 0.16, frame="Trim", cross=True)
    p.frame()
    p.gable(w, d, h, 0.26, "Roof", over=0.04, courses=4, ends="Brick")
    for u in range(units + 1):
        x = -w / 2 + uw * u
        x = max(-w / 2 + 0.05, min(w / 2 - 0.05, x))
        chimney(p, x, 0.0, h + 0.18, 0.18, pots=3, w=0.1)
    return h


def house_industrial_0(p):
    terrace(p, 1.0, 0.66, 2, 2)


def house_industrial_1(p):
    """A worker's cottage with a lean-to wash-house at the side."""
    h = terrace(p, 0.66, 0.6, 1, 2)
    p.frame((0.43, 0.08, 0))
    p.box((0, 0, 0.17), (0.2, 0.36, 0.34), "Brick")
    p.frame((0.43, -0.1, 0))
    door(p, 0, 0.1, 0.22, mat="Wood", step=False)
    p.frame()
    rise = 0.1
    ang = math.atan2(rise, 0.2)
    p.box((0.43, 0.08, 0.34 + rise / 2 + 0.01), (math.hypot(0.24, rise), 0.4, 0.03), "Roof", Matrix.Rotation(-ang, 4, "Y"))


def house_industrial_2(p):
    """A Haussmann apartment: limestone, iron balconies, a slate-and-zinc mansard with dormers."""
    w, d, sh = 1.0, 0.86, 0.27
    storeys = 4
    h = sh * storeys
    walls(p, w, d, h, "Wall")
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        for k in range(5):
            p.box((0, -0.012, 0.03 + k * 0.05), (fw, 0.008, 0.006), "Trim")  # rusticated ground floor
        p.box((0, -0.01, sh), (fw + 0.01, 0.025, 0.025), "Trim")
        n = 4 if fw > 0.9 else 3
        for i in range(n):
            x = -fw / 2 + fw * (i + 0.5) / n
            p.box((x, 0.002, 0.11), (0.13, 0.02, 0.2), "Glass")  # shopfront or carriage door
            p.box((x, -0.01, 0.22), (0.15, 0.02, 0.02), "Trim")
        for k in range(1, storeys):
            storey_windows(p, fw, sh * k + 0.13, n, 0.085, 0.17, cross=True)
        for z in (sh + 0.03, sh * 3 + 0.03):  # continuous balconies on the second and fifth floors
            p.box((0, -0.045, z), (fw, 0.09, 0.016), "Trim")
            p.box((0, -0.088, z + 0.05), (fw, 0.008, 0.008), "Iron")
            p.box((0, -0.088, z + 0.025), (fw, 0.003, 0.045), "Iron")  # railing
        p.box((0, -0.018, h - 0.012), (fw + 0.04, 0.05, 0.035), "Trim")
    p.frame()
    p.mansard(w, d, h, 0.22, 0.1, 0.07, "Slate", "Zinc")
    for at, turn, fw in faces(w - 0.06, d - 0.06):
        n = 3 if fw > 0.8 else 2
        for i in range(n):
            x = -fw / 2 + fw * (i + 0.5) / n
            p.frame(at, turn)
            p.box((x, 0.02, h + 0.1), (0.09, 0.06, 0.13), "Zinc")
            window(p, x, h + 0.1, 0.055, 0.08, frame="Trim", sill=False, lintel=False)
            p.ball((x, 0.0, h + 0.165), 0.05, "Zinc", sz=0.6, seg=10, half=True)
    p.frame()
    for x in (-0.36, 0.0, 0.36):
        chimney(p, x, 0.12, h + 0.25, 0.14, pots=4, w=0.12)


def house_industrial_3(p):
    """A tenement over shops: rendered upper floors, a shopfront with an awning, a hipped slate roof."""
    w, d, sh = 0.92, 0.84, 0.29
    storeys = 4
    h = sh * storeys
    walls(p, w, d, h, "Wall")
    p.box((0, 0, sh / 2), (w + 0.006, d + 0.006, sh), "Brick")
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        for k in range(1, storeys):
            storey_windows(p, fw, sh * k + 0.14, 3, 0.09, 0.17, frame="Trim", cross=True)
            p.box((0, -0.006, sh * k), (fw, 0.012, 0.016), "Trim")
        p.box((0, -0.015, h - 0.015), (fw + 0.03, 0.04, 0.035), "Trim")
    p.frame(faces(w, d)[0][0], 0)
    p.box((0.1, 0.002, 0.13), (0.5, 0.02, 0.17), "Glass")  # shop window
    p.box((0.1, -0.012, 0.235), (0.56, 0.025, 0.05), "Shutter")  # fascia board
    p.box((0.1, -0.08, 0.255), (0.56, 0.14, 0.012), "Awning", Matrix.Rotation(-0.35, 4, "X"))
    door(p, -0.3, 0.11, 0.23, fan=True)
    p.frame()
    p.hip(w, d, h, 0.22, "Roof", over=0.03)
    for x in (-0.3, 0.3):
        chimney(p, x, 0.0, h + 0.06, 0.24, pots=3, w=0.12)


# ── Modern ──

def house_modern_0(p):
    """A suburban house: rendered walls, a bay window, a porch, a hipped tile roof."""
    w, d, sh = 0.9, 0.72, 0.32
    h = sh * 2
    walls(p, w, d, h, "Wall")
    p.box((0, 0, sh / 2), (w + 0.006, d + 0.006, sh), "Brick")
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        storey_windows(p, fw, sh + 0.16, 2, 0.13, 0.15, frame="Trim", cross=True)
    p.frame(faces(w, d)[0][0], 0)
    p.box((-0.2, -0.07, 0.16), (0.3, 0.14, 0.3), "Brick")  # bay
    p.frame((-0.2, -d / 2 - 0.14, 0), 0)
    window(p, 0, 0.17, 0.22, 0.15, frame="Trim", cross=True)
    p.frame((-0.2, -d / 2 - 0.07, 0), 0)
    p.hip(0.3, 0.14, 0.32, 0.07, "Roof", over=0.02, ridge=0.2)
    p.frame(faces(w, d)[0][0], 0)
    door(p, 0.22, 0.12, 0.24, mat="Shutter", fan=False, step=True)
    for x in (0.12, 0.32):
        p.box((x, -0.12, 0.14), (0.02, 0.02, 0.28), "Trim")
    p.box((0.22, -0.07, 0.29), (0.26, 0.16, 0.02), "Roof")
    p.frame()
    p.hip(w, d, h, 0.26, "Roof", over=0.06)
    chimney(p, 0.25, 0.12, h + 0.08, 0.26, pots=2)


def house_modern_1(p):
    """A white villa of the 1930s: flat roof behind a parapet, ribbon windows, a rounded stair tower."""
    w, d, sh = 0.84, 0.7, 0.32
    h = sh * 2
    walls(p, w, d, h, "Wall")
    p.box((0, 0, h + 0.03), (w + 0.02, d + 0.02, 0.06), "Trim")  # parapet
    p.box((0, 0, h + 0.035), (w - 0.04, d - 0.04, 0.06), "Concrete")
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        for k in range(2):
            p.box((0.05, 0.004, sh * k + 0.18), (fw * 0.66, 0.02, 0.11), "Glass")  # ribbon window
            for i in range(5):
                p.box((0.05 - fw * 0.33 + fw * 0.66 * i / 4, -0.004, sh * k + 0.18), (0.012, 0.012, 0.11), "Iron")
            p.box((0.05, -0.02, sh * k + 0.12), (fw * 0.68, 0.04, 0.012), "Trim")
    p.frame()
    p.tube((-w / 2 + 0.05, -d / 2 + 0.05, 0), (-w / 2 + 0.05, -d / 2 + 0.05, h + 0.14), 0.13, 0.13, "Wall", seg=20)
    p.box((-w / 2 + 0.05, -d / 2 + 0.05, h * 0.55), (0.03, 0.27, h * 0.8), "Glass")
    p.frame(faces(w, d)[0][0], 0)
    door(p, 0.25, 0.12, 0.24, mat="Iron", step=True, canopy="Concrete")
    p.box((0.1, -0.08, sh), (0.5, 0.16, 0.025), "Concrete")  # balcony
    p.box((0.1, -0.155, sh + 0.06), (0.5, 0.008, 0.008), "Iron")
    p.box((0.1, -0.155, sh + 0.035), (0.5, 0.008, 0.008), "Iron")
    p.frame()


def block(p, w, d, storeys, sh, brick):
    h = storeys * sh
    walls(p, w, d, h, "Brick" if brick else "Wall")
    p.box((0, 0, sh / 2), (w + 0.006, d + 0.006, sh), "Concrete" if brick else "Brick")
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        n = 4 if fw > 0.9 else 3
        for k in range(1, storeys):
            storey_windows(p, fw, sh * k + sh * 0.5, n, 0.11, sh * 0.5, frame="Trim", sill=True, lintel=False)
            if brick:
                p.box((0, -0.005, sh * k + 0.008), (fw, 0.01, 0.016), "Concrete")
        p.box((0, 0.0, h + 0.04), (fw + 0.02, 0.03, 0.08), "Concrete")
        for i in range(n):  # shops
            x = -fw / 2 + fw * (i + 0.5) / n
            p.box((x, 0.002, sh * 0.45), (fw / n - 0.06, 0.02, sh * 0.6), "Glass")
        p.box((0, -0.008, sh * 0.85), (fw, 0.02, sh * 0.16), "Shutter" if brick else "Awning")
    p.frame()
    p.box((0, 0, h + 0.01), (w - 0.02, d - 0.02, 0.02), "Concrete")
    p.box((w * 0.2, d * 0.1, h + 0.1), (0.2, 0.18, 0.2), "Concrete")  # lift housing
    p.tube((-w * 0.25, 0.15, h), (-w * 0.25, 0.15, h + 0.08), 0.02, 0.02, "Iron", seg=6)
    p.tube((-w * 0.25, 0.15, h + 0.08), (-w * 0.25, 0.15, h + 0.18), 0.07, 0.07, "Wood", seg=12)  # water tank
    return h


def house_modern_2(p):
    """An apartment block with concrete balconies and a glazed stair."""
    w, d, sh = 1.0, 0.8, 0.26
    h = block(p, w, d, 4, sh, brick=False)
    p.frame(faces(w, d)[0][0], 0)
    for k in range(1, 4):
        for x in (-0.3, 0.3):
            p.box((x, -0.06, sh * k + 0.01), (0.26, 0.12, 0.02), "Concrete")
            p.box((x, -0.118, sh * k + 0.06), (0.26, 0.008, 0.08), "Concrete")
    p.box((0, -0.004, h / 2 + sh / 2), (0.1, 0.02, h - sh - 0.04), "Glass")
    p.frame()


def house_modern_3(p):
    block(p, 0.94, 0.84, 4, 0.28, brick=True)


# ── Landmarks (about 2.4 house units long) ──

def landmark_ancient(p):
    """A temple on a podium: steps, a ring of columns, entablature, pediments, a tiled roof."""
    L, W = 2.2, 1.3
    p.box((0, 0, 0.09), (W, L, 0.18), "Stone")
    for k in range(4):
        p.box((0, -L / 2 - 0.04 * k - 0.02, 0.18 - 0.045 * (k + 1) + 0.0225), (W * 0.8, 0.08, 0.045), "Stone")
    p.box((0, 0.25, 0.18 + 0.32), (W * 0.62, L * 0.55, 0.64), "Wall")  # cella
    cols_x, cols_y = 6, 9
    for i in range(cols_x):
        x = -W * 0.42 + W * 0.84 * i / (cols_x - 1)
        for y in (-L * 0.43, L * 0.43):
            column(p, x, y, 0.18, 0.72, 0.045)
    for j in range(1, cols_y - 1):
        y = -L * 0.43 + L * 0.86 * j / (cols_y - 1)
        for x in (-W * 0.42, W * 0.42):
            column(p, x, y, 0.18, 0.72, 0.045)
    p.box((0, 0, 0.94), (W * 0.95, L * 0.95, 0.1), "Trim")  # architrave and frieze
    p.box((0, 0, 1.0), (W * 0.98, L * 0.98, 0.03), "Trim")
    for i in range(24):  # triglyphs
        y = -L * 0.47 + L * 0.94 * i / 23
        for x in (-W * 0.476, W * 0.476):
            p.box((x, y, 0.94), (0.012, 0.03, 0.07), "Stone")
    p.frame((0, 0, 0), math.pi / 2)
    p.gable(L * 0.98, W * 0.98, 1.015, 0.26, "Roof", over=0.03, courses=6, ends="Trim")
    p.frame()
    for y in (-L * 0.49, L * 0.49):
        p.ball((0, y, 1.3), 0.05, "Gold", seg=10)
    p.frame((0, -L * 0.49 - 0.005, 0), 0)
    p.box((0, 0.0, 1.12), (0.3, 0.01, 0.06), "Stone")  # relief in the tympanum
    p.frame()


def landmark_medieval(p):
    """A church: nave and lower aisles, buttresses, lancets, an apse, a west tower with a spire."""
    L, W = 1.9, 0.62
    nave_h, aisle_h = 0.9, 0.5
    p.box((0, 0, nave_h / 2), (W, L, nave_h), "Stone")
    for s in (-1, 1):
        p.box((s * (W / 2 + 0.17), 0.05, aisle_h / 2), (0.34, L * 0.9, aisle_h), "Stone")
        p.frame((s * (W / 2 + 0.17), 0.05, aisle_h), 0)
        ang = math.atan2(0.18, 0.34)
        p.box((0, 0, 0.09), (0.36, L * 0.92, 0.03), "Slate", Matrix.Rotation(-s * ang, 4, "Y"))
        p.frame()
        for k in range(6):
            y = -L * 0.38 + k * L * 0.16
            p.frame((s * (W / 2 + 0.34), y, 0), -s * math.pi / 2)
            p.box((0, -0.04, aisle_h / 2), (0.05, 0.08, aisle_h), "Stone")  # buttress
            window(p, 0.07, aisle_h * 0.5, 0.05, 0.2, frame="Stone", sill=False, lintel=False)
            p.ball((0.07, 0.0, aisle_h * 0.5 + 0.1), 0.025, "Glass", seg=8)
            p.frame((s * W / 2, y + 0.08, 0), -s * math.pi / 2)
            window(p, 0, aisle_h + 0.2, 0.05, 0.18, frame="Stone", sill=False, lintel=False)
            p.ball((0, 0.0, aisle_h + 0.29), 0.025, "Glass", seg=8)
    p.frame((0, 0, 0), math.pi / 2)
    p.gable(L, W, nave_h, 0.42, "Slate", over=0.03, courses=6, ends="Stone")
    p.frame()
    # Apse
    p.tube((0, L / 2, 0), (0, L / 2, nave_h * 0.85), W / 2, W / 2, "Stone", seg=16)
    p.tube((0, L / 2, nave_h * 0.85), (0, L / 2, nave_h * 0.85 + 0.3), W / 2 + 0.04, 0.02, "Slate", seg=16)
    p.frame((0, L / 2 + W / 2, 0), math.pi)
    for a in (-0.6, 0, 0.6):
        p.frame((math.sin(a) * W / 2 * 1.0, L / 2 + math.cos(a) * W / 2, 0), math.pi - a)
        window(p, 0, nave_h * 0.45, 0.05, 0.24, frame="Stone", sill=False, lintel=False)
    # West tower
    T = 0.52
    ty = -L / 2 - T / 2 + 0.06
    th = 1.75
    p.frame()
    p.box((0, ty, th / 2), (T, T, th), "Stone")
    for z in (0.6, 1.2):
        p.box((0, ty, z), (T + 0.03, T + 0.03, 0.03), "Stone")
    for at, turn, fw in faces(T, T):
        p.frame((at[0], ty + at[1], 0), turn)
        for x in (-T / 2 + 0.03, T / 2 - 0.03):
            p.box((x, -0.03, th * 0.4), (0.06, 0.06, th * 0.8), "Stone")  # corner buttresses
        window(p, -0.06, th - 0.25, 0.06, 0.24, frame="Stone", glass="Char", sill=False, lintel=False)
        window(p, 0.06, th - 0.25, 0.06, 0.24, frame="Stone", glass="Char", sill=False, lintel=False)
        for k in range(5):
            p.box((-T / 2 + T * (k + 0.5) / 5, -0.01, th + 0.04), (0.05, 0.03, 0.08), "Stone")  # battlements
    p.frame((0, ty - T / 2, 0), 0)
    door(p, 0, 0.16, 0.32, mat="Door", frame="Stone", step=True)
    p.ball((0, -0.005, 0.32), 0.09, "Stone", sz=1.2, seg=12, half=True)
    p.tube((0, -0.02, 0.85), (0, 0.0, 0.85), 0.1, 0.1, "Glass", seg=16)  # rose window
    p.tube((0, -0.025, 0.85), (0, -0.015, 0.85), 0.12, 0.12, "Stone", seg=16, cap=False)
    p.frame()
    # Spire with pinnacles
    p.solid([(-T / 2 + 0.04, ty - T / 2 + 0.04, th), (T / 2 - 0.04, ty - T / 2 + 0.04, th),
             (T / 2 - 0.04, ty + T / 2 - 0.04, th), (-T / 2 + 0.04, ty + T / 2 - 0.04, th)],
            [(0, ty, th + 1.05)] * 4, "Slate")
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.tube((sx * T * 0.45, ty + sy * T * 0.45, th), (sx * T * 0.45, ty + sy * T * 0.45, th + 0.22), 0.035, 0.0, "Stone", seg=6)
    p.box((0, ty, th + 1.12), (0.012, 0.012, 0.14), "Gold")
    p.box((0, ty, th + 1.14), (0.08, 0.012, 0.012), "Gold")


def landmark_early(p):
    """A baroque church: a pedimented front between bell towers, a drum and a ribbed dome with a lantern."""
    L, W = 1.9, 0.76
    h = 0.8
    p.box((0, 0, h / 2), (W, L, h), "Trim")
    p.box((0, 0.25, 0.32), (W + 0.6, 0.5, 0.64), "Trim")  # transepts
    p.frame((0, 0.25, 0), 0)
    p.gable(W + 0.6, 0.5, 0.64, 0.2, "Lead", over=0.02, courses=3, ends="Trim")
    p.frame((0, 0, 0), math.pi / 2)
    p.gable(L, W, h, 0.26, "Lead", over=0.02, courses=4, ends="Trim")
    p.frame()
    for s in (-1, 1):
        for k in range(5):
            y = -L * 0.4 + k * L * 0.2
            if abs(y - 0.25) < 0.3:
                continue
            p.frame((s * W / 2, y, 0), -s * math.pi / 2)
            window(p, 0, 0.45, 0.1, 0.24, sill=True)
            p.box((0.15, -0.012, h / 2), (0.04, 0.024, h), "Stone")  # pilaster
    # Front between two bell towers
    p.frame((0, -L / 2, 0), 0)
    for x in (-0.25, -0.1, 0.1, 0.25):
        column(p, x, -0.05, 0.0, 0.62, 0.03)
    p.box((0, -0.05, 0.66), (0.62, 0.1, 0.08), "Trim")
    p.gable(0.62, 0.1, 0.7, 0.18, "Trim", over=0.0, courses=1, cy=-0.05, ends="Trim")
    door(p, 0, 0.16, 0.36, mat="Door", frame="Stone", step=True)
    window(p, 0, 0.52, 0.1, 0.12, sill=False)
    for x in (-W / 2 - 0.08, W / 2 + 0.08):
        p.frame((x, -L / 2 + 0.12, 0), 0)
        p.box((0, 0, 0.6), (0.26, 0.26, 1.2), "Trim")
        p.box((0, 0, 1.21), (0.3, 0.3, 0.04), "Stone")
        p.box((0, 0, 1.36), (0.2, 0.2, 0.26), "Trim")
        for at, turn, fw in faces(0.2, 0.2):
            p.frame((x + at[0], -L / 2 + 0.12 + at[1], 0), turn)
            window(p, 0, 1.37, 0.07, 0.14, glass="Char", sill=False, lintel=False)
        p.frame((x, -L / 2 + 0.12, 0), 0)
        p.ball((0, 0, 1.49), 0.11, "Copper", sz=1.1, seg=12, half=True)
        p.tube((0, 0, 1.6), (0, 0, 1.72), 0.03, 0.0, "Gold", seg=6)
    p.frame()
    # Drum, dome, lantern over the crossing
    cy = 0.25
    p.tube((0, cy, h), (0, cy, h + 0.34), 0.34, 0.34, "Trim", seg=24)
    for k in range(12):
        a = k * math.tau / 12
        p.frame((math.cos(a) * 0.34, cy + math.sin(a) * 0.34, 0), a + math.pi / 2)
        window(p, 0, h + 0.17, 0.05, 0.16, sill=False, lintel=False)
        p.box((0.09, -0.02, h + 0.17), (0.03, 0.04, 0.34), "Stone")
    p.frame()
    p.tube((0, cy, h + 0.34), (0, cy, h + 0.37), 0.37, 0.37, "Stone", seg=24)
    p.ball((0, cy, h + 0.37), 0.34, "Copper", sz=1.15, seg=24, half=True)
    for k in range(12):  # ribs
        a = k * math.tau / 12
        for t in range(6):
            b0, b1 = t / 6 * math.pi / 2, (t + 1) / 6 * math.pi / 2
            p0 = Vector((math.cos(a) * 0.345 * math.cos(b0), cy + math.sin(a) * 0.345 * math.cos(b0), h + 0.37 + 0.345 * 1.15 * math.sin(b0)))
            p1 = Vector((math.cos(a) * 0.345 * math.cos(b1), cy + math.sin(a) * 0.345 * math.cos(b1), h + 0.37 + 0.345 * 1.15 * math.sin(b1)))
            if t < 5:
                p.tube(p0, p1, 0.012, 0.012, "Lead", seg=4)
    top = h + 0.37 + 0.34 * 1.15
    p.tube((0, cy, top - 0.03), (0, cy, top + 0.14), 0.07, 0.07, "Trim", seg=10)
    p.ball((0, cy, top + 0.14), 0.07, "Copper", seg=10, half=True)
    p.tube((0, cy, top + 0.2), (0, cy, top + 0.34), 0.012, 0.012, "Gold", seg=6)
    p.ball((0, cy, top + 0.22), 0.025, "Gold", seg=8)


def landmark_industrial(p):
    """A town hall: three storeys of stone, pavilions with tall mansards at the ends, a clock tower."""
    L, W, sh = 2.2, 0.8, 0.3
    h = sh * 3
    for x0, x1, roof_h in ((-L / 2, -L / 2 + 0.5, 0.36), (-L / 2 + 0.5, L / 2 - 0.5, 0.2), (L / 2 - 0.5, L / 2, 0.36)):
        cx, wx = (x0 + x1) / 2, x1 - x0
        dy = 0.06 if roof_h > 0.3 else 0.0
        p.frame((cx, 0, 0), 0)
        p.box((0, -dy / 2, h / 2), (wx, W + dy, h), "Wall")
        p.frame((cx, -W / 2 - dy, 0), 0)
        for k in range(5):
            p.box((0, -0.01, 0.03 + k * 0.055), (wx, 0.008, 0.006), "Trim")
        n = max(2, round(wx / 0.2))
        for k in range(3):
            storey_windows(p, wx, sh * k + 0.15, n, 0.08, 0.17, cross=k > 0)
        p.box((0, -0.015, sh), (wx, 0.03, 0.025), "Trim")
        p.box((0, -0.02, h - 0.015), (wx + 0.03, 0.05, 0.04), "Trim")
        p.frame((cx, -dy / 2, 0), 0)
        if roof_h > 0.3:
            p.mansard(wx, W + dy, h, 0.26, 0.12, 0.08, "Slate", "Zinc")
            p.box((0, 0, h + 0.38 + 0.04), (0.2, 0.03, 0.06), "Iron")  # cresting
        else:
            p.mansard(wx, W, h, 0.18, 0.06, 0.06, "Slate", "Zinc")
    # Portico and the clock tower over it
    p.frame((0, -W / 2, 0), 0)
    for x in (-0.18, -0.06, 0.06, 0.18):
        column(p, x, -0.12, 0.05, 0.55, 0.025)
    p.box((0, -0.08, 0.025), (0.5, 0.2, 0.05), "Stone")
    p.box((0, -0.08, 0.63), (0.46, 0.18, 0.06), "Trim")
    p.gable(0.46, 0.18, 0.66, 0.1, "Trim", over=0.0, courses=1, cy=-0.08, ends="Trim")
    door(p, 0, 0.14, 0.3, mat="Door", frame="Trim", step=False, fan=True)
    p.frame((0, 0.05, 0), 0)
    T = 0.3
    p.box((0, 0, h + 0.45), (T, T, 0.9), "Wall")
    p.box((0, 0, h + 0.91), (T + 0.05, T + 0.05, 0.03), "Trim")
    for at, turn, fw in faces(T, T):
        p.frame((at[0], 0.05 + at[1], 0), turn)
        p.tube((0, -0.02, h + 0.7), (0, 0.0, h + 0.7), 0.09, 0.09, "Trim", seg=20)  # clock face
        p.box((0, -0.025, h + 0.73), (0.008, 0.006, 0.07), "Iron")
        p.box((0.02, -0.025, h + 0.7), (0.05, 0.006, 0.008), "Iron")
        window(p, 0, h + 0.33, 0.07, 0.18, cross=True)
    p.frame((0, 0.05, 0), 0)
    p.box((0, 0, h + 1.02), (0.2, 0.2, 0.2), "Trim")
    p.ball((0, 0, h + 1.12), 0.12, "Zinc", sz=1.4, seg=16, half=True)
    p.tube((0, 0, h + 1.28), (0, 0, h + 1.5), 0.008, 0.008, "Iron", seg=6)
    p.box((0.05, 0, h + 1.45), (0.1, 0.004, 0.06), "Awning")  # flag
    p.frame()


def landmark_modern(p):
    """A people's house of the 1920s-30s: stepped brick blocks, vertical window strips, a tall tower with a clock."""
    L, W = 2.2, 0.85
    for x0, x1, hh in ((-L / 2, -0.4, 0.75), (-0.4, 0.4, 1.0), (0.4, L / 2, 0.75)):
        cx, wx = (x0 + x1) / 2, x1 - x0
        p.frame((cx, 0, 0), 0)
        p.box((0, 0, hh / 2), (wx, W, hh), "Brick")
        p.box((0, 0, hh + 0.025), (wx + 0.02, W + 0.02, 0.05), "Concrete")
        for at, turn, fw in faces(wx, W):
            p.frame((cx + at[0], at[1], 0), turn)
            n = max(2, round(fw / 0.14))
            for i in range(n):
                x = -fw / 2 + fw * (i + 0.5) / n
                p.box((x, 0.004, hh * 0.55), (0.06, 0.02, hh * 0.7), "Glass")
                p.box((x + fw / n / 2, -0.012, hh * 0.55), (0.025, 0.025, hh * 0.8), "Brick")  # piers
            p.box((0, -0.01, 0.06), (fw, 0.02, 0.12), "Concrete")
    p.frame((0, -W / 2, 0), 0)
    p.box((0, -0.1, 0.33), (0.6, 0.2, 0.02), "Concrete")  # entrance canopy
    for x in (-0.15, 0.0, 0.15):
        door(p, x, 0.1, 0.27, mat="Glass", frame="Concrete", step=False)
    p.box((0, -0.008, 0.6), (0.36, 0.02, 0.07), "Gold")  # lettering band
    p.frame()
    T, th = 0.34, 2.1
    p.box((0, 0.1, th / 2), (T, T, th), "Brick")
    for k, z in enumerate((th, th + 0.18)):
        tw = T - 0.07 * (k + 1)
        p.box((0, 0.1, z + 0.09), (tw, tw, 0.18), "Brick")
        p.box((0, 0.1, z + 0.185), (tw + 0.02, tw + 0.02, 0.02), "Concrete")
    for at, turn, fw in faces(T, T):
        p.frame((at[0], 0.1 + at[1], 0), turn)
        p.box((0, 0.004, th * 0.6), (0.06, 0.02, th * 0.55), "Glass")
        p.tube((0, -0.02, th - 0.15), (0, 0.0, th - 0.15), 0.1, 0.1, "Concrete", seg=20)
        p.box((0, -0.025, th - 0.12), (0.008, 0.006, 0.07), "Iron")
        p.box((0.025, -0.025, th - 0.15), (0.055, 0.006, 0.008), "Iron")
    p.frame()
    p.tube((0, 0.1, th + 0.38), (0, 0.1, th + 0.7), 0.01, 0.01, "Iron", seg=6)
    p.box((0.07, 0.1, th + 0.64), (0.14, 0.004, 0.09), "Awning")


# ── Factories and blocks ──

def factory(p, modern):
    """A works: a long hall with saw-tooth roof lights, an office front, a tall chimney."""
    L, W, h = 1.5, 1.0, 0.5
    wall = "Concrete" if modern else "Brick"
    p.box((0, 0, h / 2), (L, W, h), wall)
    for at, turn, fw in faces(L, W):
        p.frame(at, turn)
        n = max(3, round(fw / 0.2))
        for i in range(n):
            x = -fw / 2 + fw * (i + 0.5) / n
            if modern:
                p.box((x, 0.004, h * 0.55), (fw / n - 0.04, 0.02, h * 0.6), "Glass")
                for k in range(3):
                    p.box((x, -0.004, h * 0.25 + k * h * 0.2 + 0.06), (fw / n - 0.04, 0.01, 0.008), "Iron")
            else:
                window(p, x, h * 0.48, 0.1, h * 0.5, frame="Trim", sill=True, lintel=False, cross=True)
                p.ball((x, -0.002, h * 0.73), 0.055, "Brick", sz=0.5, seg=10, half=True)
                p.box((x + fw / n / 2, -0.02, h / 2), (0.04, 0.04, h), wall) if i < n - 1 else None
        p.box((0, -0.01, h - 0.02), (fw + 0.02, 0.025, 0.04), "Concrete" if modern else "Trim")
    p.frame()
    teeth = 5
    tl = L / teeth
    for k in range(teeth):  # saw-tooth: a glazed upright facing north (+Y here: the long axis is X), a sloping roof
        x = -L / 2 + tl * (k + 0.5)
        p.box((x - tl / 2 + 0.01, 0, h + 0.11), (0.02, W, 0.22), "Glass")
        ang = math.atan2(0.22, tl)
        p.box((x, 0, h + 0.11 + 0.012), (math.hypot(tl, 0.22), W + 0.02, 0.022), "Slate" if not modern else "Zinc",
              Matrix.Rotation(-ang, 4, "Y"))
    for y in (-W / 2, W / 2):
        for k in range(teeth):
            x = -L / 2 + tl * (k + 0.5)
            pts = [(x - tl / 2, y - 0.01, h), (x + tl / 2, y - 0.01, h), (x - tl / 2, y - 0.01, h + 0.22)]
            top = [(x - tl / 2, y + 0.01, h), (x + tl / 2, y + 0.01, h), (x - tl / 2, y + 0.01, h + 0.22)]
            p.solid(pts, top, wall)
    # Office in front
    p.frame((0, -W / 2 - 0.15, 0), 0)
    p.box((-0.35, 0, 0.3), (0.6, 0.3, 0.6), "Concrete" if modern else "Wall")
    p.frame((-0.35, -W / 2 - 0.3, 0), 0)
    storey_windows(p, 0.6, 0.17, 3, 0.09, 0.14, frame="Trim", cross=True)
    storey_windows(p, 0.6, 0.45, 3, 0.09, 0.14, frame="Trim", cross=True)
    p.box((0, -0.015, 0.59), (0.63, 0.03, 0.035), "Trim")
    p.frame((-0.35, -W / 2 - 0.15, 0), 0)
    if not modern:
        p.hip(0.6, 0.3, 0.6, 0.12, "Slate", over=0.03)
    else:
        p.box((0, 0, 0.62), (0.62, 0.32, 0.04), "Concrete")
    # Gate and yard wall
    p.frame((0.35, -W / 2 - 0.15, 0), 0)
    for x in (-0.15, 0.15):
        p.box((x, -0.15, 0.13), (0.06, 0.06, 0.26), wall)
    p.box((0, -0.15, 0.06), (0.26, 0.012, 0.12), "Iron")
    p.frame()
    # Chimney
    cx, cy = L / 2 - 0.12, W / 2 + 0.16
    p.box((cx, cy, 0.12), (0.24, 0.24, 0.24), wall)
    p.tube((cx, cy, 0.24), (cx, cy, 2.0), 0.1, 0.065, "Concrete" if modern else "Brick", seg=16)
    for z in (0.9, 1.5, 1.94):
        p.tube((cx, cy, z), (cx, cy, z + 0.05), 0.1 - 0.035 * (z - 0.24) / 1.76 + 0.012, 0.1 - 0.035 * (z - 0.24) / 1.76 + 0.012,
               "Iron" if z < 1.9 else ("Concrete" if modern else "Brick"), seg=16)
    if modern:
        # A gantry and a cylindrical silo beside the hall
        p.tube((-L / 2 - 0.18, 0.2, 0), (-L / 2 - 0.18, 0.2, 0.9), 0.15, 0.15, "Concrete", seg=16)
        p.ball((-L / 2 - 0.18, 0.2, 0.9), 0.15, "Concrete", sz=0.4, seg=16, half=True)
        p.box((-L / 2 - 0.08, 0.2, 0.8), (0.2, 0.03, 0.03), "Iron")
    else:
        p.tube((-L / 2 - 0.15, 0.25, 0.0), (-L / 2 - 0.15, 0.25, 0.55), 0.03, 0.03, "Iron", seg=6)  # water tower legs
        for sx in (-1, 1):
            for sy in (-1, 1):
                p.box((-L / 2 - 0.15 + sx * 0.09, 0.25 + sy * 0.09, 0.3), (0.02, 0.02, 0.6), "Iron")
        p.tube((-L / 2 - 0.15, 0.25, 0.6), (-L / 2 - 0.15, 0.25, 0.8), 0.13, 0.13, "Iron", seg=14)
        p.ball((-L / 2 - 0.15, 0.25, 0.8), 0.13, "Iron", sz=0.4, seg=14, half=True)
    return (cx, cy, 2.0)


def block_modern(p, n):
    """A commercial block: an office or department store over shopfronts; or a concrete slab of flats."""
    w, d, sh = 1.2, 1.0, 0.3
    storeys = 6
    h = sh * storeys
    if n == 0:
        walls(p, w, d, h, "Wall")
        for at, turn, fw in faces(w, d):
            p.frame(at, turn)
            p.box((0, 0.004, sh * 0.45), (fw - 0.1, 0.02, sh * 0.65), "Glass")
            p.box((0, -0.06, sh * 0.85), (fw - 0.06, 0.12, 0.02), "Awning", Matrix.Rotation(-0.3, 4, "X"))
            nn = 5 if fw > 1.1 else 4
            for k in range(1, storeys):
                storey_windows(p, fw, sh * k + sh * 0.5, nn, 0.14, sh * 0.55, frame="Trim", sill=True, lintel=False)
            for i in range(nn + 1):
                p.box((-fw / 2 + fw * i / nn, -0.012, (h + sh) / 2), (0.03, 0.024, h - sh), "Trim")  # piers
            p.box((0, -0.025, h - 0.02), (fw + 0.06, 0.06, 0.05), "Trim")
            p.box((0, -0.008, sh), (fw, 0.02, 0.03), "Trim")
        p.frame()
        p.box((0, 0, h + 0.06), (w - 0.1, d - 0.1, 0.12), "Wall")
        p.box((0.3, 0.2, h + 0.16), (0.24, 0.2, 0.24), "Concrete")
        p.tube((-0.3, 0.2, h + 0.12), (-0.3, 0.2, h + 0.5), 0.008, 0.008, "Iron", seg=6)  # radio mast
    else:
        h = block(p, w, d, storeys, sh, brick=False)
        p.frame(faces(w, d)[0][0], 0)
        for k in range(1, storeys):
            for x in (-0.4, 0.0, 0.4):
                p.box((x, -0.05, sh * k + 0.01), (0.28, 0.1, 0.02), "Concrete")
                p.box((x, -0.098, sh * k + 0.06), (0.28, 0.006, 0.08), "Concrete")
        p.frame()


def ruin(p, brick):
    """What war leaves of a house: broken walls, fallen beams, rubble."""
    wall = "Brick" if brick else "Stone"
    rng = random.Random(7 if brick else 3)
    w, d = 0.9, 0.75
    for at, turn, fw in faces(w, d):
        p.frame(at, turn)
        n = 7
        for i in range(n):
            x = -fw / 2 + fw * (i + 0.5) / n
            hh = rng.uniform(0.1, 0.7) if i not in (2, 4) else rng.uniform(0.25, 0.75)
            p.box((x, 0.02, hh / 2), (fw / n + 0.002, 0.05, hh), wall)
            if hh > 0.45 and i % 2:
                p.box((x, 0.0, hh * 0.6), (0.06, 0.06, 0.12), "Char")  # a window hole, burnt
    p.frame()
    for k in range(4):
        a = rng.uniform(-0.6, 0.6)
        p.box((rng.uniform(-0.3, 0.3), rng.uniform(-0.2, 0.2), 0.14), (0.04, 0.6, 0.04), "Char",
              Matrix.Rotation(a, 4, "Z") @ Matrix.Rotation(rng.uniform(0.2, 0.5), 4, "X"))
    for k in range(26):
        s = rng.uniform(0.04, 0.12)
        p.box((rng.uniform(-0.45, 0.45), rng.uniform(-0.45, 0.4), s / 3), (s, s * 0.8, s * 0.6), rng.choice([wall, "Rubble", "Rubble"]),
              Matrix.Rotation(rng.uniform(0, 3), 4, "Z") @ Matrix.Rotation(rng.uniform(-0.4, 0.4), 4, "X"))
    p.solid([(-0.35, -0.3, 0), (0.3, -0.32, 0), (0.35, 0.3, 0), (-0.3, 0.33, 0)],
            [(-0.12, -0.05, 0.12), (0.1, -0.08, 0.14), (0.12, 0.08, 0.1), (-0.08, 0.1, 0.13)], "Rubble")


MODELS = {}
for _age, _fns in (("ancient", (house_ancient_0, house_ancient_1, house_ancient_2, house_ancient_3)),
                   ("medieval", (house_medieval_0, house_medieval_1, house_medieval_2, house_medieval_3)),
                   ("early", (house_early_0, house_early_1, house_early_2, house_early_3)),
                   ("industrial", (house_industrial_0, house_industrial_1, house_industrial_2, house_industrial_3)),
                   ("modern", (house_modern_0, house_modern_1, house_modern_2, house_modern_3))):
    for _i, _fn in enumerate(_fns):
        MODELS[f"house_{_age}_{_i}"] = _fn
MODELS.update({
    "landmark_ancient": landmark_ancient, "landmark_medieval": landmark_medieval, "landmark_early": landmark_early,
    "landmark_industrial": landmark_industrial, "landmark_modern": landmark_modern,
    "factory_industrial": lambda p: factory(p, False), "factory_modern": lambda p: factory(p, True),
    "block_modern_0": lambda p: block_modern(p, 0), "block_modern_1": lambda p: block_modern(p, 1),
    "ruin_stone": lambda p: ruin(p, False), "ruin_brick": lambda p: ruin(p, True),
})


def build(name, fn, path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials()
    root = bpy.data.objects.new("Building", None)
    bpy.context.scene.collection.objects.link(root)
    p = Part("Body")
    top = fn(p)
    objs = p.to_object(root)
    if name.startswith("factory") and top:
        e = bpy.data.objects.new("Chimney", None)
        bpy.context.scene.collection.objects.link(e)
        e.parent = root
        e.location = top
    tris = sum(len(poly.vertices) - 2 for obj in objs for poly in obj.data.polygons)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_vertex_color="ACTIVE", export_yup=True,
                              export_animations=False)
    if PREVIEW:
        preview(os.path.join(PREVIEW, name + ".png"), objs)
    return tris


def preview(png, objs):
    for mat in bpy.data.materials:
        nt = mat.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        attr = nt.nodes.new("ShaderNodeVertexColor")
        attr.layer_name = "Col"
        mixn = nt.nodes.new("ShaderNodeMix")
        mixn.data_type = "RGBA"
        mixn.blend_type = "MULTIPLY"
        mixn.inputs[0].default_value = 1.0
        mixn.inputs[6].default_value = bsdf.inputs["Base Color"].default_value
        nt.links.new(attr.outputs["Color"], mixn.inputs[7])
        nt.links.new(mixn.outputs[2], bsdf.inputs["Base Color"])
    scene = bpy.context.scene
    lo = Vector([min(v.co[i] for o in objs for v in o.data.vertices) for i in range(3)])
    hi = Vector([max(v.co[i] for o in objs for v in o.data.vertices) for i in range(3)])
    centre, size = (lo + hi) / 2, (hi - lo).length
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.location = centre + Vector((-0.75, -1.25, 0.8)) * size * 1.15
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50
    scene.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.45, 0.47, 0.5, 1)
    scene.world = world
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 420, 420
    scene.render.filepath = png
    bpy.ops.render.render(write_still=True)


def main():
    total = 0
    for name, fn in MODELS.items():
        if ONLY and not name.startswith(ONLY):
            continue
        tris = build(name, fn, os.path.join(OUT, name + ".glb"))
        total += 1
        print(f"{name}: {tris} triangles")
    print(f"built {total} buildings")


main()
