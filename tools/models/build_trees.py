"""
Builds Politicom's trees (Game/models/trees/*.glb) in Blender, from code:

- oak: a broad crown of lumpy clusters over a short thick trunk with spreading boughs.
- elm: a taller, rounder crown (also the evergreen broadleaf of warm countries).
- birch: a white, dark-marked trunk under a narrow open crown.
- pine: a Scots or umbrella pine: a red-brown trunk under broad, flat-topped clumps of needles.
- spruce: tiers of jagged cones down a straight trunk.
- cypress: a dark Mediterranean column.
- palm: a curving ringed trunk under a fan of drooping fronds.

Each tree has two meshes: "Trunk" (trunk and boughs, coloured) and "Leaves" (the foliage, in grey
light and shade only: the game colours it for the species and the season, and hides it on bare
winter trees, whose boughs then show). Trees stand on the ground at z = 0, about 1 unit tall.
Ambient occlusion is baked into vertex colours.

Usage:
  blender --background --factory-startup --python tools/models/build_trees.py -- OUTPUT_DIR [--preview DIR]
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
OUT = ARGS[0] if ARGS else "Game/models/trees"
PREVIEW = ARGS[ARGS.index("--preview") + 1] if "--preview" in ARGS else None

BARK = {"Bark": (0.24, 0.17, 0.11), "Birch": (0.80, 0.78, 0.72), "BirchMark": (0.12, 0.11, 0.10),
        "PineBark": (0.42, 0.22, 0.13), "PalmBark": (0.40, 0.32, 0.22), "Leaf": (1.0, 1.0, 1.0)}


class Part:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mat = self.bm.faces.layers.int.new("mat")

    def _tag(self, verts, mat):
        names = list(BARK)
        for f in {f for v in verts for f in v.link_faces}:
            f[self.mat] = names.index(mat)

    def tube(self, a, b, r1, r2, mat="Bark", seg=6, cap=False):
        a, b = Vector(a), Vector(b)
        d = b - a
        rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
        res = bmesh.ops.create_cone(self.bm, cap_ends=cap, cap_tris=False, segments=seg, radius1=r1, radius2=r2,
                                    depth=d.length, matrix=Matrix.Translation((a + b) / 2) @ rot)
        self._tag(res["verts"], mat)

    def blob(self, center, r, rng, sx=1.0, sy=1.0, sz=1.0, rough=0.18, subdiv=1):
        """A lumpy cluster of foliage."""
        m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((sx, sy, sz, 1))
        verts = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=r, matrix=m)["verts"]
        c = Vector(center)
        for v in verts:
            d = v.co - c
            v.co = c + d * (1 + rng.uniform(-rough, rough))
        self._tag(verts, "Leaf")

    def cone(self, z0, z1, r, rng, seg=10, mat="Leaf"):
        """A jagged tier of a spruce: the rim alternates in and out, drooping a little."""
        rim = []
        for k in range(seg):
            a = k * math.tau / seg + rng.uniform(-0.1, 0.1)
            rr = r * (1.0 if k % 2 == 0 else 0.72) * rng.uniform(0.9, 1.1)
            rim.append(self.bm.verts.new((math.cos(a) * rr, math.sin(a) * rr, z0 - (0.02 if k % 2 == 0 else 0.0))))
        top = self.bm.verts.new((0, 0, z1))
        under = self.bm.verts.new((0, 0, z0 + (z1 - z0) * 0.15))
        for k in range(seg):
            j = (k + 1) % seg
            self.bm.faces.new((rim[k], rim[j], top))
            self.bm.faces.new((rim[j], rim[k], under))
        self._tag(rim + [top, under], mat)

    def frond(self, base, direction, length, width, droop, mat="Leaf"):
        """A palm frond: a folded, drooping strip (one-sided: the game draws leaves from both sides)."""
        d = Vector(direction).normalized()
        side = d.cross(Vector((0, 0, 1))).normalized()
        n = 5
        mids, lefts, rights = [], [], []
        for i in range(n + 1):
            t = i / n
            p = Vector(base) + d * length * t + Vector((0, 0, -droop * t * t + 0.05 * t))
            w = width * math.sin(math.pi * min(1, t * 1.15)) + 0.004
            mids.append(self.bm.verts.new(p + Vector((0, 0, 0.012))))
            lefts.append(self.bm.verts.new(p + side * w - Vector((0, 0, 0.02 * t))))
            rights.append(self.bm.verts.new(p - side * w - Vector((0, 0, 0.02 * t))))
        for i in range(n):
            self.bm.faces.new((mids[i], lefts[i], lefts[i + 1], mids[i + 1]))
            self.bm.faces.new((rights[i], mids[i], mids[i + 1], rights[i + 1]))
        self._tag(mids + lefts + rights, mat)

    def bough(self, start, direction, length, r, rng, depth=2, mat="Bark"):
        """A branch and its forks, tapering: the winter outline of a broadleaf tree."""
        a = Vector(start)
        d = Vector(direction).normalized()
        b = a + d * length
        self.tube(a, b, r, r * 0.6, mat, seg=5 if r > 0.02 else 4)
        if depth > 0:
            for k in range(2):
                turn = Matrix.Rotation(rng.uniform(0.4, 0.8) * (1 if k else -1), 4, Vector((d.y, -d.x, 0.3)).normalized()
                                       if abs(d.z) < 0.99 else Vector((1, 0, 0)))
                nd = (turn @ d.to_4d()).to_3d()
                nd.z = abs(nd.z) * 0.7 + 0.3
                self.bough(b, nd, length * 0.62, r * 0.6, rng, depth - 1, mat)


def occlusion(bm, others=None):
    """Ambient occlusion per vertex (both meshes shade each other), into a "Col" layer."""
    bm.normal_update()
    tree = BVHTree.FromBMesh(bm)
    trees = [tree] + ([BVHTree.FromBMesh(o) for o in others] if others else [])
    n = 16
    dirs = []
    for i in range(n):
        z = 1 - (i + 0.5) / n
        r = math.sqrt(max(0.0, 1 - z * z))
        a = i * 2.39996
        dirs.append(Vector((math.cos(a) * r, math.sin(a) * r, z)))
    ao = {}
    for v in bm.verts:
        nrm = v.normal if v.normal.length > 0 else Vector((0, 0, 1))
        rot = nrm.to_track_quat("Z", "Y")
        hits = 0
        for d in dirs:
            w = rot @ d
            o = v.co + nrm * 0.004
            if any(t.ray_cast(o, w, 0.35)[0] is not None for t in trees) or (w.z < 0 and o.z / -w.z < 0.35):
                hits += 1
        ao[v.index] = 1 - 0.6 * (hits / n) ** 0.8
    return ao


def finish(part, other, root, height_light=True):
    """Bakes colour x occlusion (and, for foliage, a little more light toward the top) into corner colours."""
    bm = part.bm
    bm.verts.index_update()
    ao = occlusion(bm, [other.bm] if other else None)
    col = bm.loops.layers.float_color.new("Color")
    names = list(BARK)
    zs = [v.co.z for v in bm.verts] or [0]
    z0, z1 = min(zs), max(zs)
    for f in bm.faces:
        tint = BARK[names[f[part.mat]]]
        for loop in f.loops:
            a = ao[loop.vert.index]
            if height_light and names[f[part.mat]] == "Leaf":
                a *= 0.8 + 0.3 * (loop.vert.co.z - z0) / max(1e-3, z1 - z0)
            loop[col] = (tint[0] * a, tint[1] * a, tint[2] * a, 1)
    for f in bm.faces:
        f.smooth = True
    mesh = bpy.data.meshes.new(part.name)
    bm.to_mesh(mesh)
    mesh.materials.append(bpy.data.materials[part.name])
    mesh.color_attributes.active_color = mesh.color_attributes["Color"]
    mesh.color_attributes.render_color_index = mesh.color_attributes.active_color_index
    obj = bpy.data.objects.new(part.name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = root
    return obj


# ── The trees: (trunk, leaves) ──

def oak(t, l, rng):
    t.tube((0, 0, 0), (0, 0, 0.36), 0.065, 0.05, seg=7)
    t.tube((0, 0, -0.02), (0, 0, 0.06), 0.09, 0.065, seg=7)  # root flare
    for k in range(5):
        a = k * math.tau / 5 + rng.uniform(-0.3, 0.3)
        t.bough((0, 0, 0.3 + rng.uniform(0, 0.08)), (math.cos(a), math.sin(a), 0.75), 0.24, 0.035, rng, depth=1)
    # A broad dome with clusters swelling from it, not spokes: from above it must read as one crown.
    l.blob((0, 0, 0.72), 0.29, rng, sz=0.72, rough=0.1, subdiv=2)
    for k in range(4):
        a = k * math.tau / 4 + 0.4
        l.blob((math.cos(a) * 0.12, math.sin(a) * 0.12, 0.64 + rng.uniform(-0.03, 0.03)), 0.19, rng, sz=0.75, rough=0.12)


def elm(t, l, rng):
    t.tube((0, 0, 0), (0, 0, 0.5), 0.05, 0.035, seg=6)
    for k in range(4):
        a = k * math.tau / 4 + rng.uniform(-0.3, 0.3)
        t.bough((0, 0, 0.42), (math.cos(a) * 0.6, math.sin(a) * 0.6, 1), 0.22, 0.026, rng, depth=1)
    l.blob((0, 0, 0.82), 0.22, rng, sz=1.15)
    for k in range(3):
        a = k * math.tau / 3
        l.blob((math.cos(a) * 0.13, math.sin(a) * 0.13, 0.7 + rng.uniform(-0.04, 0.04)), 0.17, rng, sz=1.0)


def birch(t, l, rng):
    t.tube((0, 0, 0), (0.02, 0, 0.75), 0.03, 0.016, "Birch", seg=6)
    for k in range(6):  # dark marks on the bark
        z = 0.08 + k * 0.1
        t.tube((0.002, 0, z), (0.002, 0, z + 0.018), 0.0315 - z * 0.018, 0.031 - z * 0.018, "BirchMark", seg=6)
    for k in range(4):
        a = k * math.tau / 4 + 0.5
        t.bough((0.01, 0, 0.45 + k * 0.06), (math.cos(a) * 0.6, math.sin(a) * 0.6, 1), 0.16, 0.012, rng, depth=1, mat="Birch")
    l.blob((0.02, 0, 0.74), 0.19, rng, sx=0.95, sy=0.95, sz=1.45, rough=0.22)
    l.blob((0.12, 0.05, 0.56), 0.14, rng, sz=1.2, rough=0.22)
    l.blob((-0.1, -0.05, 0.58), 0.13, rng, sz=1.2, rough=0.22)


def pine(t, l, rng):
    t.tube((0, 0, 0), (0.03, 0.01, 0.78), 0.045, 0.022, "PineBark", seg=6)
    for h, a in ((0.6, 0.3), (0.66, 2.4), (0.7, 4.4)):  # boughs up into the crown
        d = (math.cos(a), math.sin(a))
        t.tube((0.025, 0.008, h), (0.025 + d[0] * 0.16, 0.008 + d[1] * 0.16, h + 0.1), 0.016, 0.01, "PineBark", seg=4)
    # One broad, flat-topped dome of needles and a lower clump to one side.
    l.blob((0.03, 0.01, 0.84), 0.27, rng, sz=0.45, rough=0.08, subdiv=2)
    l.blob((-0.12, 0.08, 0.74), 0.15, rng, sz=0.5, rough=0.1)


def spruce(t, l, rng):
    t.tube((0, 0, 0), (0, 0, 1.0), 0.035, 0.008, seg=6)
    tiers = 5
    for k in range(tiers):
        z0 = 0.12 + k * 0.17
        r = 0.27 * (1 - k / (tiers + 0.6))
        l.cone(z0, z0 + 0.32, r, rng)


def cypress(t, l, rng):
    t.tube((0, 0, 0), (0, 0, 0.2), 0.03, 0.025, seg=5)
    l.blob((0, 0, 0.55), 0.15, rng, sx=1.0, sy=1.0, sz=3.2, rough=0.12, subdiv=2)


def palm(t, l, rng):
    pts = []
    lean = Vector((0.18, 0.05, 0))
    for i in range(7):
        s = i / 6
        pts.append(Vector((0, 0, s * 0.85)) + lean * s * s)
    for i in range(6):
        t.tube(pts[i], pts[i + 1], 0.035 - i * 0.003, 0.033 - i * 0.003, "PalmBark", seg=6)
        t.tube(pts[i + 1] - Vector((0, 0, 0.012)), pts[i + 1], 0.04 - i * 0.003, 0.034 - i * 0.003, "PalmBark", seg=6)  # rings
    crown = pts[-1]
    for k in range(8):
        a = k * math.tau / 8 + rng.uniform(-0.15, 0.15)
        l.frond(crown, (math.cos(a), math.sin(a), 0.35), 0.4 + rng.uniform(-0.05, 0.05), 0.07, 0.3)
    for k in range(3):
        a = k * math.tau / 3
        t.tube(crown + Vector((math.cos(a) * 0.035, math.sin(a) * 0.035, -0.04)),
               crown + Vector((math.cos(a) * 0.035, math.sin(a) * 0.035, -0.0)), 0.022, 0.022, "PalmBark", seg=5, cap=True)  # coconuts


TREES = {"oak": oak, "elm": elm, "birch": birch, "pine": pine, "spruce": spruce, "cypress": cypress, "palm": palm}


def build(name, fn, path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for mat_name in ("Trunk", "Leaves"):
        mat = bpy.data.materials.new(mat_name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (1, 1, 1, 1) if mat_name == "Trunk" else (0.25, 0.38, 0.17, 1)
        bsdf.inputs["Roughness"].default_value = 0.95
    root = bpy.data.objects.new("Tree", None)
    bpy.context.scene.collection.objects.link(root)
    rng = random.Random(name)
    trunk, leaves = Part("Trunk"), Part("Leaves")
    fn(trunk, leaves, rng)
    a = finish(trunk, leaves, root, height_light=False)
    b = finish(leaves, trunk, root)
    trunk.bm.free()
    leaves.bm.free()
    tris = sum(len(p.vertices) - 2 for o in (a, b) for p in o.data.polygons)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_vertex_color="ACTIVE", export_yup=True,
                              export_animations=False)
    if PREVIEW:
        preview(os.path.join(PREVIEW, name + ".png"))
    return tris


def preview(png):
    for mat in bpy.data.materials:
        nt = mat.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        attr = nt.nodes.new("ShaderNodeVertexColor")
        attr.layer_name = "Color"
        mixn = nt.nodes.new("ShaderNodeMix")
        mixn.data_type = "RGBA"
        mixn.blend_type = "MULTIPLY"
        mixn.inputs[0].default_value = 1.0
        mixn.inputs[6].default_value = bsdf.inputs["Base Color"].default_value
        nt.links.new(attr.outputs["Color"], mixn.inputs[7])
        nt.links.new(mixn.outputs[2], bsdf.inputs["Base Color"])
    scene = bpy.context.scene
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.location = (-1.3, -1.9, 1.3)
    cam.rotation_euler = (Vector((0, 0, 0.5)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50
    scene.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.52, 0.55, 1)
    scene.world = world
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 300, 340
    scene.render.filepath = png
    bpy.ops.render.render(write_still=True)


def main():
    for name, fn in TREES.items():
        print(f"{name}: {build(name, fn, os.path.join(OUT, name + '.glb'))} triangles")
    print(f"built {len(TREES)} trees")


main()
