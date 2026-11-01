"""
Builds Politicom's fortifications (Game/models/works/*.glb) in Blender, from code: the works a
commune throws up round a town, one length of wall and one strongpoint for each age.

- ancient: an earth bank topped with a palisade of sharpened stakes; a timber gate tower.
- medieval: a stone curtain wall with battlements and a wall-walk; a round tower with a conical roof.
- early: an earthen rampart faced with stone, with a parapet and gabions; a cannon on its carriage.
- industrial: a barricade of paving stones, barrels, carts and furniture, a red flag; a field gun.
- modern: a trench line: sandbagged parapet, duckboards, barbed wire on stakes; a machine-gun nest.

A wall runs along X from -0.5 to 0.5, the outside at -Y, standing 1 high (the game scales it to
the works' height). Strongpoints are about one unit across. Colours and ambient occlusion are baked
into vertex colours under one material.

Usage:
  blender --background --factory-startup --python tools/models/build_works.py -- OUTPUT_DIR [--preview DIR]
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
OUT = ARGS[0] if ARGS else "Game/models/works"
PREVIEW = ARGS[ARGS.index("--preview") + 1] if "--preview" in ARGS else None

COLORS = {
    "Earth": (0.36, 0.28, 0.18), "Grass": (0.30, 0.34, 0.18), "Stake": (0.40, 0.29, 0.17),
    "Timber": (0.26, 0.17, 0.10), "Stone": (0.60, 0.57, 0.51), "StoneDark": (0.46, 0.43, 0.38),
    "Slate": (0.30, 0.31, 0.35), "Roof": (0.50, 0.25, 0.17), "Iron": (0.13, 0.13, 0.14),
    "Bronze": (0.50, 0.36, 0.16), "Cobble": (0.45, 0.43, 0.40), "Barrel": (0.45, 0.30, 0.16),
    "Cloth": (0.55, 0.50, 0.42), "Red": (0.62, 0.10, 0.08), "Black": (0.05, 0.05, 0.05),
    "Sand": (0.62, 0.55, 0.40), "Wire": (0.25, 0.24, 0.22), "Concrete": (0.58, 0.57, 0.54),
    "Wicker": (0.48, 0.38, 0.22), "Wheel": (0.30, 0.20, 0.12),
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
        res = bmesh.ops.create_cone(self.bm, cap_ends=cap, cap_tris=False, segments=seg, radius1=r1, radius2=r2,
                                    depth=d.length, matrix=Matrix.Translation((a + b) / 2) @ rot)
        self.paint(res["verts"], mat)

    def ball(self, center, r, mat, sx=1.0, sy=1.0, sz=1.0, seg=10):
        m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((sx, sy, sz, 1))
        self.paint(bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=seg // 2, radius=r, matrix=m)["verts"], mat)

    def profile(self, x0, x1, pts, mat):
        """A bank: the cross-section `pts` [(y, z), ...] (clockwise seen from +X) extruded from x0 to x1."""
        a = [self.bm.verts.new((x0, y, z)) for y, z in pts]
        b = [self.bm.verts.new((x1, y, z)) for y, z in pts]
        n = len(pts)
        self.bm.faces.new(list(reversed(a)))
        self.bm.faces.new(b)
        for i in range(n):
            j = (i + 1) % n
            self.bm.faces.new((a[i], a[j], b[j], b[i]))
        self.paint(a + b, mat)

    def sandbag(self, x, y, z, turn=0.0, mat="Sand"):
        m = Matrix.Translation(Vector((x, y, z))) @ Matrix.Rotation(turn, 4, "Z") @ Matrix.Diagonal((0.075, 0.04, 0.03, 1))
        self.paint(bmesh.ops.create_uvsphere(self.bm, u_segments=8, v_segments=4, radius=1, matrix=m)["verts"], mat)

    def to_object(self, parent=None):
        bm = self.bm
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
    """Ambient occlusion per vertex; the ground counts as cover."""
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
        hits = 0
        for d in dirs:
            w = rot @ d
            o = v.co + nrm * 0.004
            if tree.ray_cast(o, w, 0.3)[0] is not None or (w.z < 0 and o.z / -w.z < 0.3):
                hits += 1
        c = 1 - 0.55 * (hits / n) ** 0.9
        v[col] = (c, c, c, 1)


def materials():
    mat = bpy.data.materials.new("Detail")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (1, 1, 1, 1)
    bsdf.inputs["Roughness"].default_value = 0.85


# ── Walls (X -0.5..0.5, outside -Y, 1 high) ──

def wall_ancient(p):
    p.profile(-0.5, 0.5, [(-0.3, 0.0), (-0.12, 0.4), (0.12, 0.4), (0.3, 0.0)], "Earth")
    p.profile(-0.5, 0.5, [(-0.13, 0.398), (-0.12, 0.41), (0.12, 0.41), (0.13, 0.398)], "Grass")
    rng = random.Random(1)
    for k in range(14):  # stakes, sharpened
        x = -0.48 + k * (0.96 / 13)
        h = rng.uniform(0.5, 0.6)
        p.tube((x, -0.03, 0.38), (x, -0.03, 0.38 + h), 0.034, 0.03, "Stake", seg=6)
        p.tube((x, -0.03, 0.38 + h), (x, -0.03, 0.38 + h + 0.08), 0.03, 0.0, "Stake", seg=6)
    p.box((0, 0.0, 0.62), (1.0, 0.03, 0.04), "Timber")  # a binding rail behind
    p.box((0, 0.08, 0.6), (1.0, 0.12, 0.02), "Timber")  # fighting step


def wall_medieval(p):
    p.box((0, 0, 0.4), (1.0, 0.26, 0.8), "Stone")
    p.box((0, 0, 0.04), (1.0, 0.32, 0.08), "StoneDark")  # plinth
    for k in range(6):
        p.box((-0.5 + (k + 0.5) / 6, -0.11, 0.86), (0.09, 0.05, 0.12), "Stone")  # merlons
    p.box((0, -0.11, 0.81), (1.0, 0.05, 0.02), "Stone")
    p.box((0, 0.11, 0.84), (1.0, 0.04, 0.08), "Stone")  # rear parapet
    for k in range(3):
        p.box((-0.33 + k * 0.33, -0.131, 0.5), (0.02, 0.012, 0.12), "Black")  # arrow slits
    for z in (0.25, 0.55):
        p.box((0, -0.131, z), (1.0, 0.006, 0.006), "StoneDark")  # courses


def wall_early(p):
    # Glacis, stone-faced scarp, a parapet of earth, gabions
    p.profile(-0.5, 0.5, [(-0.45, 0.0), (-0.22, 0.12), (-0.18, 0.12), (-0.15, 0.5), (0.25, 0.5), (0.4, 0.0)], "Earth")
    p.profile(-0.5, 0.5, [(-0.23, 0.11), (-0.19, 0.12), (-0.16, 0.5), (-0.2, 0.5)], "Stone")
    p.profile(-0.5, 0.5, [(-0.45, 0.0), (-0.44, 0.012), (-0.22, 0.13), (-0.23, 0.11)], "Grass")
    p.profile(-0.5, 0.5, [(-0.17, 0.49), (-0.12, 0.62), (0.05, 0.62), (0.08, 0.49)], "Earth")  # parapet
    p.box((0, -0.18, 0.5), (1.0, 0.04, 0.03), "Stone")  # cordon
    for k in range(5):
        x = -0.4 + k * 0.2
        p.tube((x, 0.15, 0.5), (x, 0.15, 0.62), 0.05, 0.05, "Wicker", seg=8)  # gabions
        p.tube((x, 0.15, 0.62), (x, 0.15, 0.63), 0.05, 0.04, "Earth", seg=8)
    p.box((0, 0.2, 0.505), (1.0, 0.1, 0.01), "Timber")  # firing step boards


def wall_industrial(p):
    rng = random.Random(4)
    # Paving stones stacked in a sloping heap
    for k in range(70):
        x = rng.uniform(-0.48, 0.48)
        y = rng.uniform(-0.14, 0.12)
        z = rng.uniform(0.0, 0.42 - abs(y) * 1.2)
        p.box((x, y, z + 0.03), (0.08, 0.06, 0.05), rng.choice(["Cobble", "Cobble", "StoneDark"]),
              Matrix.Rotation(rng.uniform(-0.3, 0.3), 4, "Z") @ Matrix.Rotation(rng.uniform(-0.3, 0.3), 4, "X"))
    p.profile(-0.5, 0.5, [(-0.2, 0.0), (-0.08, 0.36), (0.08, 0.36), (0.18, 0.0)], "Cobble")
    for x in (-0.32, 0.12):  # barrels
        p.tube((x, -0.05, 0.36), (x, -0.05, 0.56), 0.065, 0.065, "Barrel", seg=12)
        for z in (0.4, 0.52):
            p.tube((x, -0.05, z), (x, -0.05, z + 0.012), 0.068, 0.068, "Iron", seg=12)
    # An overturned cart, a wheel up
    p.box((-0.05, 0.02, 0.48), (0.3, 0.16, 0.12), "Timber", Matrix.Rotation(0.4, 4, "Y"))
    p.tube((-0.15, -0.08, 0.58), (-0.15, -0.1, 0.58), 0.1, 0.1, "Wheel", seg=12)
    for k in range(6):
        a = k * math.pi / 3
        p.box((-0.15, -0.09, 0.58), (0.01, 0.012, 0.19), "Wheel", Matrix.Rotation(a, 4, "Y"))
    p.box((0.35, 0.0, 0.42), (0.2, 0.08, 0.14), "Timber", Matrix.Rotation(-0.25, 4, "Y"))  # a wardrobe on its side
    p.box((0.28, 0.06, 0.44), (0.12, 0.12, 0.012), "Cloth")  # a mattress
    p.tube((0.42, -0.02, 0.45), (0.42, -0.02, 0.95), 0.008, 0.008, "Timber", seg=6)  # flagpole
    p.box((0.49, -0.02, 0.88), (0.14, 0.006, 0.1), "Red")


def wall_modern(p):
    # Trench: a parapet of sandbags in front, a parados behind, duckboards between
    p.profile(-0.5, 0.5, [(-0.4, 0.0), (-0.16, 0.22), (-0.08, 0.22), (-0.06, 0.0)], "Earth")
    p.profile(-0.5, 0.5, [(0.12, 0.0), (0.16, 0.15), (0.3, 0.15), (0.45, 0.0)], "Earth")
    p.box((0, 0.03, 0.01), (1.0, 0.16, 0.02), "Timber")  # duckboards
    for k in range(10):
        p.box((-0.45 + k * 0.1, 0.03, 0.022), (0.02, 0.16, 0.006), "Earth")
    for row in range(3):
        for k in range(7):
            x = -0.45 + k * 0.15 + (0.075 if row % 2 else 0)
            if x > 0.48:
                continue
            p.sandbag(x, -0.12, 0.25 + row * 0.055, 0.0)
    rng = random.Random(9)
    for x in (-0.4, -0.13, 0.14, 0.41):  # barbed wire on stakes out in front
        p.tube((x, -0.55, 0.0), (x, -0.55, 0.3), 0.01, 0.01, "Wire", seg=4)
    for z in (0.1, 0.2, 0.28):
        for k in range(10):
            x = -0.5 + k * 0.1
            p.tube((x, -0.55 + rng.uniform(-0.02, 0.02), z), (x + 0.1, -0.55 + rng.uniform(-0.02, 0.02), z + rng.uniform(-0.02, 0.02)),
                   0.004, 0.004, "Wire", seg=3, cap=False)


# ── Strongpoints ──

def post_ancient(p):
    """A timber gate tower on four posts with a fighting platform and a thatched roof."""
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.tube((sx * 0.2, sy * 0.2, 0), (sx * 0.2, sy * 0.2, 1.0), 0.04, 0.035, "Stake", seg=6)
    p.box((0, 0, 0.75), (0.5, 0.5, 0.04), "Timber")
    for s in (-1, 1):
        p.box((0, s * 0.24, 0.84), (0.5, 0.03, 0.16), "Timber")
        p.box((s * 0.24, 0, 0.84), (0.03, 0.5, 0.16), "Timber")
    p.tube((0, 0, 1.0), (0, 0, 1.35), 0.42, 0.0, "Wicker", seg=8)
    for sx in (-1, 1):
        p.box((sx * 0.2, 0, 0.35), (0.02, 0.4, 0.02), "Timber", Matrix.Rotation(0.9 * sx, 4, "X"))  # braces


def post_medieval(p):
    """A round tower with machicolations and a conical slate roof."""
    p.tube((0, 0, 0), (0, 0, 1.2), 0.36, 0.33, "Stone", seg=18)
    p.tube((0, 0, 0), (0, 0, 0.12), 0.4, 0.38, "StoneDark", seg=18)
    p.tube((0, 0, 1.2), (0, 0, 1.32), 0.38, 0.38, "Stone", seg=18)
    for k in range(9):
        a = k * math.tau / 9
        p.box((math.cos(a) * 0.37, math.sin(a) * 0.37, 1.16), (0.05, 0.05, 0.06), "StoneDark")
        if k % 3 == 0:
            p.box((math.cos(a) * 0.335, math.sin(a) * 0.335, 0.7), (0.02, 0.02, 0.14), "Black",
                  Matrix.Rotation(a, 4, "Z"))
    p.tube((0, 0, 1.32), (0, 0, 1.9), 0.42, 0.0, "Slate", seg=18)
    p.tube((0, 0, 1.9), (0, 0, 2.05), 0.008, 0.008, "Iron", seg=4)
    p.box((0.05, 0, 2.0), (0.1, 0.004, 0.06), "Red")


def post_early(p):
    """A cannon on a wooden field carriage, with shot and a powder barrel."""
    p.box((0, 0.05, 0.12), (0.2, 0.5, 0.06), "Timber", Matrix.Rotation(0.15, 4, "X"))
    for s in (-1, 1):
        p.box((s * 0.1, 0.05, 0.16), (0.03, 0.5, 0.1), "Timber", Matrix.Rotation(0.15, 4, "X"))
        p.tube((s * 0.16, -0.1, 0.14), (s * 0.2, -0.1, 0.14), 0.14, 0.14, "Wheel", seg=14)
        p.tube((s * 0.165, -0.1, 0.14), (s * 0.21, -0.1, 0.14), 0.03, 0.03, "Iron", seg=8)
    p.tube((-0.22, -0.1, 0.14), (0.22, -0.1, 0.14), 0.02, 0.02, "Iron", seg=6)
    p.tube((0, 0.15, 0.26), (0, -0.5, 0.24), 0.07, 0.05, "Bronze", seg=12)
    p.ball((0, 0.17, 0.26), 0.07, "Bronze", seg=10)
    p.tube((0, -0.48, 0.24), (0, -0.52, 0.24), 0.06, 0.06, "Bronze", seg=12)
    for k in range(6):
        p.ball((0.25 + (k % 3) * 0.06, 0.25 + (k // 3) * 0.06, 0.03), 0.03, "Iron", seg=6)
    p.tube((-0.28, 0.25, 0), (-0.28, 0.25, 0.15), 0.06, 0.06, "Barrel", seg=10)


def post_industrial(p):
    """A breech-loading field gun with steel wheels, a shield, and a limber box."""
    p.tube((0, 0.0, 0.22), (0, -0.6, 0.24), 0.045, 0.035, "Iron", seg=12)
    p.box((0, 0.08, 0.22), (0.12, 0.18, 0.1), "Iron")
    p.box((0, -0.05, 0.3), (0.42, 0.02, 0.28), "Iron", Matrix.Rotation(-0.1, 4, "X"))  # shield
    for s in (-1, 1):
        p.tube((s * 0.2, -0.02, 0.18), (s * 0.24, -0.02, 0.18), 0.18, 0.18, "Wheel", seg=16)
        for k in range(6):
            p.box((s * 0.22, -0.02, 0.18), (0.012, 0.012, 0.33), "Wheel", Matrix.Rotation(k * math.pi / 6, 4, "X"))
    p.box((0, 0.35, 0.12), (0.06, 0.55, 0.06), "Iron", Matrix.Rotation(-0.2, 4, "X"))  # trail
    p.box((0.3, 0.45, 0.1), (0.2, 0.25, 0.18), "Timber")  # limber box
    p.box((0.3, 0.45, 0.2), (0.21, 0.26, 0.02), "Iron")


def post_modern(p):
    """A machine-gun nest: a ring of sandbags round a gun on its tripod, a corrugated roof."""
    for row in range(4):
        n = 12
        for k in range(n):
            a = k * math.tau / n + (0.13 if row % 2 else 0)
            if row >= 2 and -0.9 < a - 4.712 < 0.9:
                continue  # the firing slit
            p.sandbag(math.cos(a) * 0.36, math.sin(a) * 0.36, 0.03 + row * 0.055, a + math.pi / 2)
    p.tube((0, -0.05, 0.18), (0, -0.55, 0.2), 0.02, 0.016, "Iron", seg=8)  # barrel
    p.box((0, 0.02, 0.18), (0.08, 0.16, 0.08), "Iron")
    p.tube((0, -0.03, 0.14), (0, -0.07, 0.18), 0.05, 0.05, "Iron", seg=8)
    for a in (0.5, 2.6, 4.7):
        p.tube((0, 0, 0.15), (math.cos(a) * 0.12, math.sin(a) * 0.12 + 0.05, 0.0), 0.01, 0.01, "Iron", seg=4)
    p.box((0, 0.12, 0.32), (0.7, 0.45, 0.012), "Iron")  # roof
    for sx in (-1, 1):
        p.tube((sx * 0.3, 0.3, 0), (sx * 0.3, 0.3, 0.32), 0.015, 0.015, "Timber", seg=4)
    p.box((0.2, 0.25, 0.04), (0.14, 0.08, 0.08), "Concrete")  # ammunition box


MODELS = {f"wall_{a}": globals()[f"wall_{a}"] for a in ("ancient", "medieval", "early", "industrial", "modern")}
MODELS.update({f"post_{a}": globals()[f"post_{a}"] for a in ("ancient", "medieval", "early", "industrial", "modern")})


def build(name, fn, path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials()
    root = bpy.data.objects.new("Works", None)
    bpy.context.scene.collection.objects.link(root)
    p = Part("Body")
    fn(p)
    obj = p.to_object(root)
    tris = sum(len(poly.vertices) - 2 for poly in obj.data.polygons)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_vertex_color="ACTIVE", export_yup=True,
                              export_animations=False)
    if PREVIEW:
        preview(os.path.join(PREVIEW, name + ".png"), obj)
    return tris


def preview(png, obj):
    nt = bpy.data.materials["Detail"].node_tree
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Color"
    nt.links.new(attr.outputs["Color"], nt.nodes.get("Principled BSDF").inputs["Base Color"])
    scene = bpy.context.scene
    lo = Vector([min(v.co[i] for v in obj.data.vertices) for i in range(3)])
    hi = Vector([max(v.co[i] for v in obj.data.vertices) for i in range(3)])
    centre, size = (lo + hi) / 2, (hi - lo).length
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.location = centre + Vector((-0.8, -1.2, 0.75)) * size * 1.1
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
    scene.render.resolution_x, scene.render.resolution_y = 420, 320
    scene.render.filepath = png
    bpy.ops.render.render(write_still=True)


def main():
    for name, fn in MODELS.items():
        print(f"{name}: {build(name, fn, os.path.join(OUT, name + '.glb'))} triangles")
    print(f"built {len(MODELS)} works")


main()
