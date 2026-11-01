"""
Builds Politicom's trains (Game/models/trains/<era>_<car>.glb) in Blender, from code:

- early (to about 1880): a 2-4-0 tender engine in Brunswick green with red frames and brass dome:
  tall flared chimney, open cab, outside coupling rods; four-wheeled goods vans, plank coal wagons
  and three-compartment carriages in varnished teak.
- late (1880 on): a 4-6-0 in black lined red: long boiler, short chimney, smoke deflectors, closed
  cab with windows, running boards; bogie goods vans in oxide red, steel coal wagons, long
  corridor carriages in maroon with rows of windows.

Each car runs along -Y in Blender (+Z in Godot), wheels on the ground at z = 0, about 1.4 units
wide (the locomotive and tender together about 3.6 long). Wheels turn and coupling rods go round
in a looping "Run" animation; an empty named "Chimney" marks where the game puffs smoke. Shading
(ambient occlusion) is baked with the colours into vertex colours.

Usage:
  blender --background --factory-startup --python tools/models/build_trains.py -- OUTPUT_DIR [--preview DIR]
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else "Game/models/trains"
PREVIEW = ARGS[ARGS.index("--preview") + 1] if "--preview" in ARGS else None
RUN_FRAMES = 24

COLORS = {
    "Green": (0.10, 0.26, 0.14), "Black": (0.06, 0.06, 0.065), "Red": (0.62, 0.10, 0.08),
    "Brass": (0.78, 0.60, 0.25), "Steel": (0.42, 0.43, 0.45), "Wheel": (0.08, 0.08, 0.08),
    "Teak": (0.52, 0.30, 0.15), "Maroon": (0.36, 0.08, 0.08), "Oxide": (0.42, 0.17, 0.10),
    "Grey": (0.42, 0.42, 0.40), "Wood": (0.40, 0.31, 0.22), "Coal": (0.05, 0.05, 0.05),
    "Roof": (0.30, 0.30, 0.31), "Glass": (0.20, 0.24, 0.28), "Cream": (0.82, 0.76, 0.62),
}
MATS = list(COLORS)
M = {n: i for i, n in enumerate(MATS)}


class Part:
    """One mesh object being built: boxes, tubes and balls in its own coordinates."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()

    def paint(self, verts, mat):
        for f in {f for v in verts for f in v.link_faces}:
            f.material_index = M[mat]

    def box(self, center, size, mat, rot=None):
        m = Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1))
        self.paint(bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)["verts"], mat)

    def tube(self, a, b, r1, r2, mat, seg=16, cap=True):
        a, b = Vector(a), Vector(b)
        d = b - a
        rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
        m = Matrix.Translation((a + b) / 2) @ rot
        res = bmesh.ops.create_cone(self.bm, cap_ends=cap, cap_tris=False, segments=seg, radius1=r1, radius2=r2,
                                    depth=d.length, matrix=m)
        self.paint(res["verts"], mat)

    def ball(self, center, r, mat, sx=1.0, sy=1.0, sz=1.0, seg=12, half=False):
        m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((sx, sy, sz, 1))
        verts = bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=seg // 2 + 1, radius=r, matrix=m)["verts"]
        if half:
            bmesh.ops.delete(self.bm, geom=[v for v in verts if v.co.z < center[2] - 1e-4], context="VERTS")
            verts = [v for v in verts if v.is_valid]
        self.paint(verts, mat)

    def roof(self, y0, y1, width, z, rise, mat, seg=8):
        """An arched roof from y0 to y1."""
        pts = [(-width / 2 + width * i / seg, z + rise * math.sin(math.pi * i / seg)) for i in range(seg + 1)]
        verts0 = [self.bm.verts.new((x, y0, zz)) for x, zz in pts]
        verts1 = [self.bm.verts.new((x, y1, zz)) for x, zz in pts]
        for i in range(seg):
            self.bm.faces.new((verts0[i], verts0[i + 1], verts1[i + 1], verts1[i]))
        self.bm.faces.new(verts0)
        self.bm.faces.new(list(reversed(verts1)))
        self.paint(verts0 + verts1, mat)

    def to_object(self, parent=None):
        bm = self.bm
        for f in bm.faces:
            f.smooth = True
        for e in bm.edges:
            if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(40):
                e.smooth = False
        occlusion(bm)
        # Colours are baked with the occlusion into corner colours under one "Detail" material:
        # Blender's glTF exporter writes vertex colours only for a mesh's first material.
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
        hits = sum(1 for d in dirs if tree.ray_cast(v.co + nrm * 0.004, rot @ d, 0.35)[0] is not None)
        c = 1 - 0.55 * (hits / n) ** 0.9
        v[col] = (c, c, c, 1)


def materials():
    for name, c in COLORS.items():
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (*c, 1)
        metal = name in ("Brass", "Steel")
        bsdf.inputs["Metallic"].default_value = 0.7 if metal else 0.0
        bsdf.inputs["Roughness"].default_value = 0.35 if metal else (0.2 if name == "Glass" else 0.7)
        mat.diffuse_color = (*c, 1)
    detail = bpy.data.materials.new("Detail")
    detail.use_nodes = True
    bsdf = detail.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (1, 1, 1, 1)
    bsdf.inputs["Roughness"].default_value = 0.6


# ── Running gear (animated) ──

def wheelset(root, y, r, gauge=0.5, spokes=10, crank=None, name="Wheels"):
    """Two wheels and their axle, centred at (0, y, r): a separate object, turned by the Run animation."""
    p = Part(f"{name}_{y:.2f}")
    for s in (-1, 1):
        x = gauge * s
        p.tube((x - 0.04 * s, 0, 0), (x + 0.04 * s, 0, 0), r, r, "Wheel", seg=24)  # tyre and centre
        p.tube((x + 0.045 * s, 0, 0), (x + 0.05 * s, 0, 0), r * 0.92, r * 0.92, "Steel", seg=24, cap=False)  # tyre face
        if spokes:
            for k in range(spokes):
                a = k * 2 * math.pi / spokes
                p.box((x + 0.048 * s, math.cos(a) * r * 0.45, math.sin(a) * r * 0.45), (0.012, 0.035, r * 0.85), "Red" if crank else "Steel",
                      Matrix.Rotation(a - math.pi / 2, 4, "X"))  # radial
            p.tube((x + 0.05 * s, 0, 0), (x + 0.06 * s, 0, 0), r * 0.16, r * 0.16, "Steel", seg=12)  # hub
        if crank:
            p.tube((x + 0.06 * s, 0, crank), (x + 0.08 * s, 0, crank), 0.03, 0.03, "Steel", seg=8)  # crankpin
    p.tube((-gauge, 0, 0), (gauge, 0, 0), 0.04, 0.04, "Steel", seg=8)
    obj = p.to_object(root)
    obj.location = (0, y, r)
    return obj


def coupling_rods(root, ys, r, crank, gauge=0.5):
    """Rods joining the crankpins of the coupled wheels: they go round in a circle as the wheels turn."""
    p = Part("Rods")
    for s in (-1, 1):
        x = (gauge + 0.095) * s
        p.box((x, (ys[0] + ys[-1]) / 2, 0), (0.025, abs(ys[-1] - ys[0]) + 0.12, 0.06), "Steel")
    obj = p.to_object(root)
    obj.location = (0, 0, r + crank)
    return obj


def animate_run(root):
    """One turn of the wheels a loop: every wheelset spins, the rods go round with the cranks."""
    prefs = bpy.context.preferences.edit
    prefs.keyframe_new_interpolation_type = "LINEAR"
    for obj in [o for o in root.children if o.name.startswith(("Wheels", "Rods"))]:
        if obj.name.startswith("Wheels"):
            for f, a in ((1, 0.0), (RUN_FRAMES // 2 + 1, -math.pi), (RUN_FRAMES + 1, -2 * math.pi)):
                obj.rotation_euler = (a, 0, 0)
                obj.keyframe_insert("rotation_euler", frame=f)
        else:
            base = obj.location.copy()
            crank = obj["crank"]
            for i in range(9):
                a = -2 * math.pi * i / 8
                obj.location = (base.x, base.y + crank * math.sin(a) * 0 + math.sin(-a) * crank * 0, base.z)
                obj.location = (base.x, base.y - math.sin(a) * crank, base.z - crank + math.cos(a) * crank)
                obj.keyframe_insert("location", frame=1 + i * RUN_FRAMES // 8)
        ad = obj.animation_data
        track = ad.nla_tracks.new()
        track.name = "Run"
        strip = track.strips.new("Run", 1, ad.action)
        strip.frame_end = RUN_FRAMES + 1
        ad.action = None


def buffers(p, y, z, toward):
    p.box((0, y, z), (1.35, 0.08, 0.2), "Red")  # buffer beam
    for s in (-1, 1):
        p.tube((0.5 * s, y, z), (0.5 * s, y + 0.14 * toward, z), 0.05, 0.05, "Steel", seg=10)
        p.tube((0.5 * s, y + 0.14 * toward, z), (0.5 * s, y + 0.16 * toward, z), 0.09, 0.09, "Steel", seg=12)
    p.box((0, y + 0.08 * toward, z - 0.02), (0.06, 0.12, 0.04), "Steel")  # coupling hook


# ── The cars ──

def chimney_marker(root, at):
    e = bpy.data.objects.new("Chimney", None)
    bpy.context.scene.collection.objects.link(e)
    e.parent = root
    e.location = at


def early_locomotive(root):
    p = Part("Body")
    # Engine: front at y = -1.75. Frames, boiler, smokebox, firebox.
    p.box((0, -0.55, 0.5), (0.9, 2.3, 0.22), "Red")  # frames
    p.box((0, -0.55, 0.64), (1.3, 2.3, 0.05), "Black")  # footplate
    p.tube((0, -1.45, 1.12), (0, 0.15, 1.12), 0.42, 0.42, "Green", seg=24)  # boiler
    for y in (-1.0, -0.55, -0.1):
        p.tube((0, y, 1.12), (0, y + 0.04, 1.12), 0.43, 0.43, "Brass", seg=24, cap=False)  # boiler bands
    p.tube((0, -1.7, 1.12), (0, -1.42, 1.12), 0.45, 0.45, "Black", seg=24)  # smokebox
    p.tube((0, -1.72, 1.12), (0, -1.7, 1.12), 0.2, 0.2, "Black", seg=16)  # smokebox door
    p.tube((0, -1.5, 1.5), (0, -1.5, 2.15), 0.11, 0.2, "Black", seg=16)  # tall flared chimney
    p.tube((0, -1.5, 2.15), (0, -1.5, 2.22), 0.22, 0.22, "Black", seg=16)
    p.ball((0, -0.55, 1.55), 0.17, "Brass", sz=1.3, half=True, seg=16)  # dome
    p.tube((0, -0.55, 1.5), (0, -0.55, 1.56), 0.19, 0.17, "Brass", seg=16)
    p.tube((0, 0.0, 1.5), (0, 0.0, 1.72), 0.05, 0.035, "Brass", seg=10)  # safety valve
    p.box((0, 0.3, 1.0), (0.95, 0.4, 0.75), "Green")  # firebox
    # Open cab: spectacle plate, side sheets, a roof on posts.
    p.box((0, 0.1, 1.55), (1.2, 0.05, 0.75), "Green")
    for s in (-1, 1):
        p.tube((0.3 * s, 0.07, 1.62), (0.3 * s, 0.07, 1.63), 0.1, 0.1, "Glass", seg=12)  # spectacles
        p.box((0.6 * s, 0.35, 0.9), (0.04, 0.5, 0.45), "Green")
        p.box((0.6 * s, 0.6, 1.55), (0.03, 0.03, 0.9), "Black")
    p.box((0, 0.35, 1.95), (1.3, 0.62, 0.04), "Black")
    p.box((0, -0.55, 0.69), (1.25, 2.2, 0.02), "Brass")  # valance edging
    buffers(p, -1.75, 0.55, -1)
    for s in (-1, 1):  # sandboxes and splashers over the driving wheels
        for y in (-0.55, 0.05):
            p.ball((0.55 * s, y, 0.7), 0.5, "Green", sx=0.18, sy=1.0, sz=1.0, half=True, seg=16)
            p.tube((0.55 * s, y - 0.5, 0.7), (0.55 * s, y + 0.5, 0.7), 0.02, 0.02, "Brass", seg=6)
    # Tender: behind, from y = 0.75.
    p.box((0, 1.4, 0.45), (0.9, 1.2, 0.2), "Red")
    p.box((0, 1.4, 0.98), (1.25, 1.2, 0.85), "Green")
    p.box((0, 1.4, 1.42), (1.28, 1.24, 0.04), "Black")
    p.ball((0, 1.25, 1.4), 0.45, "Coal", sx=1.3, sy=1.0, sz=0.35, half=True, seg=12)
    for s in (-1, 1):
        p.box((0.63 * s, 1.4, 0.98), (0.01, 1.0, 0.6), "Cream")  # lining panel
        p.box((0.635 * s, 1.4, 0.98), (0.01, 0.94, 0.54), "Green")
    buffers(p, 2.02, 0.55, 1)
    root_body = p.to_object(root)
    # Running gear: small leading wheels, two coupled drivers, tender wheels.
    wheelset(root, -1.3, 0.3, spokes=8)
    for y in (-0.55, 0.05):
        wheelset(root, y, 0.5, spokes=12, crank=0.18)
    rods = coupling_rods(root, (-0.55, 0.05), 0.5, 0.18)
    rods["crank"] = 0.18
    for y in (1.0, 1.8):
        wheelset(root, y, 0.3, spokes=8)
    chimney_marker(root, (0, -1.5, 2.25))
    return root_body


def late_locomotive(root):
    p = Part("Body")
    p.box((0, -0.45, 0.48), (0.95, 2.9, 0.22), "Black")  # frames
    p.box((0, -0.45, 0.68), (1.4, 2.9, 0.05), "Black")  # running board
    for s in (-1, 1):
        p.box((0.7 * s, -0.45, 0.68), (0.02, 2.9, 0.06), "Red")  # red lining on the valance
    p.tube((0, -1.75, 1.18), (0, 0.4, 1.18), 0.47, 0.5, "Black", seg=28)  # long boiler, tapering
    for y in (-1.2, -0.6, 0.0):
        p.tube((0, y, 1.18), (0, y + 0.03, 1.18), 0.5, 0.5, "Red", seg=28, cap=False)  # lined bands
    p.tube((0, -2.0, 1.18), (0, -1.72, 1.18), 0.5, 0.5, "Black", seg=28)  # smokebox
    p.tube((0, -2.02, 1.18), (0, -2.0, 1.18), 0.3, 0.3, "Steel", seg=20)
    p.tube((0, -1.8, 1.6), (0, -1.8, 1.82), 0.14, 0.15, "Black", seg=16)  # short chimney
    for s in (-1, 1):
        p.box((0.6 * s, -1.75, 1.25), (0.03, 0.5, 0.65), "Black")  # smoke deflectors
    p.ball((0, -0.9, 1.65), 0.16, "Black", sz=1.1, half=True, seg=16)  # dome
    p.tube((0, -0.2, 1.62), (0, -0.2, 1.78), 0.05, 0.04, "Brass", seg=10)  # whistle and valves
    for s in (-1, 1):  # outside cylinders
        p.tube((0.62 * s, -1.75, 0.68), (0.62 * s, -1.15, 0.68), 0.17, 0.17, "Black", seg=16)
        p.tube((0.4 * s, -1.0, 1.1), (0.4 * s, 0.3, 1.1), 0.03, 0.03, "Brass", seg=8)  # handrails
    # Closed cab with windows.
    p.box((0, 0.75, 1.3), (1.35, 0.7, 1.1), "Black")
    for s in (-1, 1):
        p.box((0.68 * s, 0.75, 1.55), (0.02, 0.4, 0.3), "Glass")
        p.box((0.69 * s, 0.75, 1.2), (0.01, 0.6, 0.02), "Red")
    p.box((0, 0.42, 1.65), (0.35, 0.02, 0.28), "Glass")
    p.roof(0.35, 1.15, 1.42, 1.85, 0.12, "Roof")
    buffers(p, -2.15, 0.55, -1)
    # A big bogie tender.
    p.box((0, 2.05, 0.5), (1.0, 1.6, 0.2), "Black")
    p.box((0, 2.05, 1.08), (1.38, 1.6, 0.95), "Black")
    for s in (-1, 1):
        p.box((0.695 * s, 2.05, 1.08), (0.01, 1.4, 0.7), "Red")
        p.box((0.7 * s, 2.05, 1.08), (0.01, 1.36, 0.66), "Black")
    p.ball((0, 1.9, 1.55), 0.55, "Coal", sx=1.15, sy=1.2, sz=0.32, half=True, seg=12)
    buffers(p, 2.88, 0.55, 1)
    for y in (1.7, 2.4):
        bogie(p, y)
    bogie(p, -1.65, span=0.4, r=0.3)
    body = p.to_object(root)
    wheelset(root, -1.85, 0.3, spokes=8)
    wheelset(root, -1.45, 0.3, spokes=8)
    for y in (-0.9, -0.3, 0.3):
        wheelset(root, y, 0.55, spokes=14, crank=0.2)
    rods = coupling_rods(root, (-0.9, 0.3), 0.55, 0.2, gauge=0.5)
    rods["crank"] = 0.2
    for y in (1.55, 1.85, 2.25, 2.55):
        wheelset(root, y, 0.27, spokes=0)
    chimney_marker(root, (0, -1.8, 1.85))
    return body


def bogie(p, y, span=0.3, r=0.27):
    """A bogie's side frames and axle boxes, round a pair of wheels at y +/- span / 2."""
    for s in (-1, 1):
        p.box((0.62 * s, y, r), (0.06, span + 0.5, 0.14), "Black")
        for k in (-1, 1):
            p.box((0.63 * s, y + k * span / 2, r), (0.08, 0.1, 0.12), "Steel")
        p.tube((0.6 * s, y - 0.12, r + 0.12), (0.6 * s, y + 0.12, r + 0.12), 0.04, 0.04, "Steel", seg=8)  # spring
    p.box((0, y, r + 0.12), (1.0, 0.25, 0.08), "Black")  # bolster


def wagon_frame(p, length, z=0.45, color="Black"):
    p.box((0, 0, z), (0.9, length, 0.16), color)
    buffers(p, -length / 2 - 0.05, z + 0.08, -1)
    buffers(p, length / 2 + 0.05, z + 0.08, 1)


def planks(p, y0, y1, z0, z1, x, mat, n):
    for i in range(1, n):
        z = z0 + (z1 - z0) * i / n
        p.box((x, (y0 + y1) / 2, z), (0.012, y1 - y0, 0.015), mat)


def goods_van(root, era):
    p = Part("Body")
    L = 1.9 if era == "early" else 2.6
    color = "Grey" if era == "early" else "Oxide"
    wagon_frame(p, L)
    p.box((0, 0, 1.08), (1.3, L, 1.1), color)
    for s in (-1, 1):
        planks(p, -L / 2, L / 2, 0.53, 1.63, 0.655 * s, "Black", 9)
        p.box((0.665 * s, 0, 1.08), (0.02, 0.7, 0.95), color)  # sliding door
        p.box((0.68 * s, 0, 1.6), (0.02, 1.1, 0.03), "Steel")  # door rail
        for y in (-L / 2 + 0.1, L / 2 - 0.1):
            p.box((0.665 * s, y, 1.08), (0.02, 0.06, 1.1), "Black")  # corner irons
        p.box((0.665 * s, -0.35, 1.08), (0.02, 0.04, 1.0), "Black")
        p.box((0.665 * s, 0.35, 1.08), (0.02, 0.04, 1.0), "Black")
    p.roof(-L / 2 - 0.04, L / 2 + 0.04, 1.36, 1.63, 0.14, "Roof")
    if era == "late":
        for y in (-0.8, 0.8):
            bogie(p, y)
    p.to_object(root)
    if era == "early":
        for y in (-0.6, 0.6):
            wheelset(root, y, 0.3, spokes=8)
    else:
        for y in (-0.95, -0.65, 0.65, 0.95):
            wheelset(root, y, 0.27, spokes=0)


def coal_wagon(root, era):
    p = Part("Body")
    L = 1.8 if era == "early" else 2.4
    side = "Wood" if era == "early" else "Grey"
    wagon_frame(p, L)
    p.box((0, 0, 0.55), (1.3, L, 0.06), side)  # floor
    for s in (-1, 1):
        p.box((0.63 * s, 0, 0.85), (0.05, L, 0.6), side)
        p.box((0, (L / 2 - 0.025) * s, 0.85), (1.3, 0.05, 0.6), side)
        planks(p, -L / 2, L / 2, 0.55, 1.15, 0.66 * s, "Black", 5 if era == "early" else 2)
        for y in (-L / 2 + 0.05, -L / 6, L / 6, L / 2 - 0.05):
            p.box((0.665 * s, y, 0.85), (0.02, 0.05, 0.62), "Black")  # strapping
    p.ball((0, 0, 1.05), 0.6, "Coal", sx=1.0, sy=L / 1.25, sz=0.38, half=True, seg=14)
    for k in range(6):  # lumps
        p.ball((((k * 37) % 7 - 3) * 0.12, ((k * 53) % 9 - 4) * 0.16, 1.18 + (k % 3) * 0.03), 0.09, "Coal", seg=6)
    p.to_object(root)
    if era == "early":
        for y in (-0.55, 0.55):
            wheelset(root, y, 0.3, spokes=8)
    else:
        for y in (-0.8, 0.8):
            wheelset(root, y, 0.3, spokes=0)


def carriage(root, era):
    p = Part("Body")
    L = 2.4 if era == "early" else 3.4
    body = "Teak" if era == "early" else "Maroon"
    wagon_frame(p, L)
    p.box((0, 0, 1.12), (1.36, L, 1.15), body)
    for s in (-1, 1):
        p.box((0.685 * s, 0, 0.75), (0.01, L - 0.1, 0.03), "Brass" if era == "early" else "Cream")  # waist moulding
        if era == "early":  # three compartments: a door with a window each, quarter lights between
            for k, y in enumerate((-0.75, 0.0, 0.75)):
                p.box((0.685 * s, y, 1.05), (0.012, 0.42, 0.75), body)
                p.box((0.69 * s, y, 1.35), (0.012, 0.28, 0.3), "Glass")
                p.box((0.69 * s, y + 0.18, 1.0), (0.012, 0.02, 0.06), "Brass")  # handle
            for y in (-0.375, 0.375):
                p.box((0.69 * s, y, 1.35), (0.012, 0.22, 0.28), "Glass")
        else:  # a row of windows and two end doors
            for k in range(8):
                y = -L / 2 + 0.55 + k * (L - 1.1) / 7
                p.box((0.69 * s, y, 1.38), (0.012, 0.26, 0.32), "Glass")
            for y in (-L / 2 + 0.22, L / 2 - 0.22):
                p.box((0.69 * s, y, 1.15), (0.012, 0.3, 0.85), body)
                p.box((0.695 * s, y, 1.42), (0.012, 0.18, 0.22), "Glass")
            p.box((0.69 * s, 0, 1.62), (0.01, L - 0.1, 0.03), "Cream")
    p.roof(-L / 2 - 0.04, L / 2 + 0.04, 1.4, 1.69, 0.18 if era == "late" else 0.12, "Roof")
    if era == "early":
        for y in (-0.6, 0.6):
            p.tube((0, y, 1.8), (0, y, 1.9), 0.06, 0.05, "Black", seg=8)  # roof lamps
    else:
        for y in (-1.2, 1.2):
            bogie(p, y)
    p.to_object(root)
    if era == "early":
        for y in (-0.8, 0.8):
            wheelset(root, y, 0.3, spokes=8)
    else:
        for y in (-1.35, -1.05, 1.05, 1.35):
            wheelset(root, y, 0.27, spokes=0)


CARS = {
    "locomotive": lambda root, era: (early_locomotive if era == "early" else late_locomotive)(root),
    "van": goods_van,
    "coal": coal_wagon,
    "carriage": carriage,
}


def build(era, car, path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 24
    materials()
    root = bpy.data.objects.new("Car", None)
    bpy.context.scene.collection.objects.link(root)
    if car == "locomotive":
        CARS[car](root, era)
    else:
        CARS[car](root, era)
    animate_run(root)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_animation_mode="NLA_TRACKS",
                              export_vertex_color="ACTIVE", export_yup=True)
    if PREVIEW:
        preview(root, os.path.join(PREVIEW, os.path.basename(path)[:-4] + ".png"))


def preview(root, png):
    os.makedirs(os.path.dirname(png), exist_ok=True)
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
    cam.location = (-5.2, -4.0, 3.0)
    cam.rotation_euler = (Vector((0, 0.2, 0.9)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50
    scene.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.4, 0.42, 0.44, 1)
    scene.world = world
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 700, 450
    scene.frame_set(5)
    scene.render.filepath = png
    bpy.ops.render.render(write_still=True)


def main():
    n = 0
    for era in ("early", "late"):
        for car in CARS:
            build(era, car, os.path.join(OUT, f"{era}_{car}.glb"))
            n += 1
    print(f"built {n} cars")


main()
