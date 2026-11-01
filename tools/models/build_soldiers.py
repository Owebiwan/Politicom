"""
Builds Politicom's soldiers and militia (Game/models/figures/<era>_<side>_<n>.glb) in Blender, from
code. Each figure is one smooth, skinned body on a small skeleton (hips, spine, head, arms, legs, a
weapon bone in the right hand and a shield bone on the left forearm), about one unit tall, facing +Z
in Godot (-Y in Blender), with:

- a shaped torso, shoulders, elbows and knees that bend without gaps, hands that grip, a face (eyes,
  brows, nose, ears; moustaches and beards on some), and hair;
- the kit of its era (helmets and hats, belts, pouches, canteens, packs and bedrolls, scabbards,
  cartridge boxes, bandoliers, puttees and gaiters) and a detailed weapon (musket lock, barrel bands,
  ramrod and sling; rifle bolt; spear socket and blade; shield rims and bosses);
- ambient occlusion baked into its vertex colours (darker in creases, under brims, between the legs);
- four animations: Idle (at ease), March, Attack (fire and reload, or thrust) and Guard (aim, or
  shield up).

Eras: ancient, medieval, earlymodern, industrial, modern. Three variants of each side (height,
build, skin, hair, beards). A soldier's "Uniform" material (and "UniformDark") is replaced in the game
by the army's colour; militia keep their own clothes.

Usage:
  blender --background --factory-startup --python tools/models/build_soldiers.py -- OUTPUT_DIR [--preview DIR]
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else "Game/models/figures"
PREVIEW = ARGS[ARGS.index("--preview") + 1] if "--preview" in ARGS else None
FPS = 24
ERAS = ("ancient", "medieval", "earlymodern", "industrial", "modern")
VARIANTS = 3

MATS = ["Uniform", "UniformDark", "Cloth", "Skin", "Leather", "Metal", "Wood", "Accent", "Belts", "Dark", "Hair"]
DEFAULT = {
    "Uniform": (0.45, 0.47, 0.52), "UniformDark": (0.28, 0.29, 0.32), "Cloth": (0.36, 0.33, 0.30),
    "Skin": (0.80, 0.62, 0.50), "Leather": (0.36, 0.24, 0.15), "Metal": (0.58, 0.59, 0.61),
    "Wood": (0.45, 0.30, 0.17), "Accent": (0.70, 0.13, 0.10), "Belts": (0.86, 0.84, 0.78),
    "Dark": (0.10, 0.09, 0.09), "Hair": (0.20, 0.14, 0.10),
}
M = {name: i for i, name in enumerate(MATS)}

SHOULDER_Z, ELBOW_Z, WRIST_Z = 0.775, 0.615, 0.47
HIP_Z, KNEE_Z, ANKLE_Z = 0.50, 0.275, 0.045
GRIP = (-0.125, 0.0, WRIST_Z - 0.035)
BONES = [
    ("Hips", (0, 0, HIP_Z), (0, 0, HIP_Z + 0.1), None),
    ("Spine", (0, 0, HIP_Z + 0.03), (0, 0, 0.80), "Hips"),
    ("Head", (0, 0, 0.83), (0, 0, 0.97), "Spine"),
    ("UpperArm.R", (-0.125, 0, SHOULDER_Z), (-0.125, 0, ELBOW_Z), "Spine"),
    ("Forearm.R", (-0.125, 0, ELBOW_Z), (-0.125, 0, WRIST_Z), "UpperArm.R"),
    ("UpperArm.L", (0.125, 0, SHOULDER_Z), (0.125, 0, ELBOW_Z), "Spine"),
    ("Forearm.L", (0.125, 0, ELBOW_Z), (0.125, 0, WRIST_Z), "UpperArm.L"),
    ("Thigh.R", (-0.055, 0, HIP_Z), (-0.055, 0, KNEE_Z), "Hips"),
    ("Shin.R", (-0.055, 0, KNEE_Z), (-0.055, 0, ANKLE_Z), "Thigh.R"),
    ("Thigh.L", (0.055, 0, HIP_Z), (0.055, 0, KNEE_Z), "Hips"),
    ("Shin.L", (0.055, 0, KNEE_Z), (0.055, 0, ANKLE_Z), "Thigh.L"),
    ("Weapon", GRIP, (GRIP[0], GRIP[1], GRIP[2] + 0.1), "Forearm.R"),
    ("Shield", (0.125, 0, ELBOW_Z), (0.125, 0, ELBOW_Z - 0.1), "Forearm.L"),
]
GROUPS = [b[0] for b in BONES]


# ── The body: one bmesh with skin weights ──

class Body:
    def __init__(self):
        self.bm = bmesh.new()
        self.dl = self.bm.verts.layers.deform.verify()

    def finish(self, verts, mat, weight):
        for v in verts:
            w = weight(v.co) if callable(weight) else {weight: 1.0}
            total = sum(w.values())
            for g, val in w.items():
                if val > 1e-4:
                    v[self.dl][GROUPS.index(g)] = val / total
        for f in {f for v in verts for f in v.link_faces}:
            f.material_index = M[mat]
        return verts

    def tube(self, a, b, radii, mat, weight, seg=10, sy=1.0, cap=True, rings=None):
        """A tube from a to b; radii is (r1, r2) or one radius per ring; sy flattens it front to back."""
        a, b = Vector(a), Vector(b)
        radii = list(radii)
        if len(radii) == 2:
            radii = [radii[0], (radii[0] + radii[1]) / 2, radii[1]]
        rot = (b - a).to_track_quat("Z", "Y").to_matrix()
        loops = []
        for i, r in enumerate(radii):
            c = a.lerp(b, i / (len(radii) - 1))
            loops.append([self.bm.verts.new(c + rot @ Vector((math.cos(t) * r, math.sin(t) * r * sy, 0)))
                          for t in (k * 2 * math.pi / seg for k in range(seg))])
        for l0, l1 in zip(loops, loops[1:]):
            for k in range(seg):
                self.bm.faces.new((l0[k], l0[(k + 1) % seg], l1[(k + 1) % seg], l1[k]))
        if cap:
            self.bm.faces.new(list(reversed(loops[0])))
            self.bm.faces.new(loops[-1])
        return self.finish([v for l in loops for v in l], mat, weight)

    def ball(self, center, r, mat, weight, sx=1.0, sy=1.0, sz=1.0, seg=12, half=False):
        m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((sx, sy, sz, 1))
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=max(4, seg // 2 + 1), radius=r, matrix=m)
        verts = res["verts"]
        if half:
            cz = center[2]
            bmesh.ops.delete(self.bm, geom=[v for v in verts if v.co.z < cz - 1e-4], context="VERTS")
            verts = [v for v in verts if v.is_valid]
        return self.finish(verts, mat, weight)

    def box(self, center, size, mat, weight, rot=None):
        m = Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1))
        return self.finish(bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)["verts"], mat, weight)

    def cut(self, verts, pred):
        """Removes the vertices of a part that match pred (an opening for the face, say)."""
        bmesh.ops.delete(self.bm, geom=[v for v in verts if v.is_valid and pred(v.co)], context="VERTS")

    def plate(self, outline, depth, mat, weight):
        """A flat shape (its points facing -Y), extruded backwards by depth."""
        verts = [self.bm.verts.new(p) for p in outline]
        face = self.bm.faces.new(verts)
        ext = bmesh.ops.extrude_face_region(self.bm, geom=[face])
        new = [v for v in ext["geom"] if isinstance(v, bmesh.types.BMVert)]
        bmesh.ops.translate(self.bm, verts=new, vec=(0, depth, 0))
        return self.finish(verts + new, mat, weight)


def limb(bone, a, b, parent=None, child=None, bs=0.22, be=0.22):
    """Weights along a limb: blended with the parent bone near its start and the child near its end."""
    a, b = Vector(a), Vector(b)
    d = b - a
    l2 = max(1e-9, d.length_squared)

    def w(co):
        t = max(0.0, min(1.0, (co - a).dot(d) / l2))
        if parent and t < bs:
            k = 0.5 + 0.5 * t / bs
            return {bone: k, parent: 1 - k}
        if child and t > 1 - be:
            k = 0.5 + 0.5 * (1 - t) / be
            return {bone: k, child: 1 - k}
        return {bone: 1.0}
    return w


def skirt(top_z, length, base="Hips"):
    """Skirts and coat tails: they hang from the hips and follow the legs a little lower down."""
    def w(co):
        depth = max(0.0, min(1.0, (top_z - co.z) / length))
        k = 0.6 * depth * min(1.0, abs(co.x) / 0.05)
        side = "Thigh.R" if co.x < 0 else "Thigh.L"
        return {base: 1 - k, side: k} if k > 0 else {base: 1.0}
    return w


def mix(**w):
    return lambda co: {k.replace("_", "."): v for k, v in w.items()}


# ── The figure ──

def figure(era, side, n):
    rng = random.Random(f"{era}/{side}/{n}")
    soldier = side == "soldier"
    coat = "Uniform" if soldier or era == "modern" else "Cloth"
    legs = {"ancient": "Skin", "medieval": "Cloth", "earlymodern": "Belts" if soldier else "Cloth",
            "industrial": "Accent" if soldier else "Cloth", "modern": coat}[era]
    beard_chance = {"ancient": 0.6, "medieval": 0.55, "earlymodern": 0.1 if soldier else 0.4, "industrial": 0.45, "modern": 0.25}[era]
    moustache_chance = {"ancient": 0.1, "medieval": 0.2, "earlymodern": 0.1, "industrial": 0.6, "modern": 0.35}[era]
    beard = rng.random() < beard_chance
    moustache = not beard and rng.random() < moustache_chance
    width = 0.95 + rng.random() * 0.1
    B = Body()
    T = limb("Spine", (0, 0, HIP_Z), (0, 0, 0.80), parent="Hips", bs=0.2)

    # Head and face
    head = "Head"
    B.ball((0, 0.004, 0.898), 0.058, "Skin", head, sy=1.06, sz=1.12, seg=14)
    B.ball((0, -0.014, 0.866), 0.045, "Skin", head, sx=0.95, sy=1.0, sz=0.85, seg=12)  # jaw
    B.box((0, -0.061, 0.888), (0.016, 0.02, 0.03), "Skin", head, Matrix.Rotation(math.radians(-12), 4, "X"))  # nose
    for s in (-1, 1):
        B.ball((0.021 * s, -0.054, 0.902), 0.0075, "Dark", head, seg=6)  # eyes
        B.box((0.021 * s, -0.058, 0.914), (0.024, 0.006, 0.006), "Hair", head)  # brows
        B.ball((0.058 * s, 0.004, 0.893), 0.016, "Skin", head, sx=0.45, sz=1.2, seg=8)  # ears
    B.ball((0, 0.012, 0.906), 0.06, "Hair", head, sy=1.06, sz=1.0, half=True, seg=14)
    if moustache:
        B.ball((0, -0.058, 0.868), 0.016, "Hair", head, sx=1.9, sy=0.5, sz=0.45, seg=8)
    if beard:
        B.ball((0, -0.03, 0.855), 0.046, "Hair", head, sx=1.02, sy=0.95, sz=0.95, seg=12)
    B.tube((0, 0, 0.79), (0, 0, 0.855), (0.031, 0.03), "Skin", limb("Head", (0, 0, 0.79), (0, 0, 0.855), parent="Spine", bs=0.7))
    headgear(B, era, soldier)

    # Torso: waist, chest, shoulders
    profile = [0.083, 0.079, 0.084, 0.098, 0.108, 0.110, 0.098, 0.06]
    B.tube((0, 0, HIP_Z), (0, 0, 0.815), [r * width for r in profile], coat, T, seg=14, sy=0.62)
    for s, n_ in ((-1, "R"), (1, "L")):
        B.ball((0.116 * s, 0, 0.77), 0.04, coat, mix(**{f"UpperArm_{n_}": 0.65, "Spine": 0.35}), sy=0.95, seg=10)
    torso_kit(B, era, soldier, coat, T)

    # Arms and hands
    sleeve = "Skin" if era == "ancient" else coat
    for s, n_ in ((-1, "R"), (1, "L")):
        x = 0.125 * s
        sh, el, wr = (x, 0, SHOULDER_Z), (x, 0, ELBOW_Z), (x, 0, WRIST_Z)
        if era == "ancient":  # short tunic sleeves over bare arms
            B.tube(sh, (x, 0, SHOULDER_Z - 0.06), (0.043, 0.04), coat, f"UpperArm.{n_}", seg=10)
        B.tube(sh, el, [0.038, 0.037, 0.034, 0.031], sleeve, limb(f"UpperArm.{n_}", sh, el, parent="Spine", child=f"Forearm.{n_}", bs=0.12), seg=10)
        B.ball(el, 0.031, sleeve, mix(**{f"UpperArm_{n_}": 0.5, f"Forearm_{n_}": 0.5}), seg=8)
        B.tube(el, wr, [0.031, 0.029, 0.026, 0.024], sleeve, limb(f"Forearm.{n_}", el, wr, parent=f"UpperArm.{n_}"), seg=10)
        if era == "earlymodern" and soldier:  # turned-back cuffs
            B.tube((x, 0, WRIST_Z + 0.055), (x, 0, WRIST_Z + 0.005), (0.031, 0.03), "Accent", f"Forearm.{n_}", seg=10)
        if era in ("industrial", "modern") and soldier:
            B.tube((x, 0, WRIST_Z + 0.025), (x, 0, WRIST_Z + 0.005), (0.027, 0.027), "UniformDark", f"Forearm.{n_}", seg=10)
        hz = WRIST_Z - 0.033
        B.ball((x, -0.004, hz), 0.025, "Skin", f"Forearm.{n_}", sx=0.75, sy=1.05, sz=1.25, seg=8)  # the hand, closed
        B.tube((x - 0.012 * s, -0.016, hz + 0.012), (x - 0.016 * s, -0.026, hz - 0.006), (0.008, 0.007), "Skin", f"Forearm.{n_}", seg=6)  # thumb

    # Hips, belt and legs
    B.tube((0, 0, HIP_Z + 0.05), (0, 0, HIP_Z - 0.065), [0.085 * width, 0.09 * width, 0.08 * width],
           legs if era not in ("ancient", "medieval") else coat, "Hips", seg=14, sy=0.7)
    B.tube((0, 0, HIP_Z + 0.055), (0, 0, HIP_Z + 0.03), (0.09 * width, 0.09 * width), "Leather", "Hips", seg=14, sy=0.66)
    B.box((0, -0.06, HIP_Z + 0.043), (0.022, 0.006, 0.02), "Metal", "Hips")  # buckle
    for s, n_ in ((-1, "R"), (1, "L")):
        hx = 0.055 * s
        hip, knee, ankle = (hx, 0, HIP_Z), (hx, 0, KNEE_Z), (hx, 0, ANKLE_Z)
        B.tube(hip, knee, [0.058, 0.056, 0.05, 0.044], legs, limb(f"Thigh.{n_}", hip, knee, parent="Hips", child=f"Shin.{n_}", bs=0.12), seg=10)
        B.ball(knee, 0.043, legs, mix(**{f"Thigh_{n_}": 0.5, f"Shin_{n_}": 0.5}), seg=8)
        B.tube(knee, ankle, [0.043, 0.042, 0.035, 0.03], legs, limb(f"Shin.{n_}", knee, ankle, parent=f"Thigh.{n_}"), seg=10)
        lower_leg(B, era, soldier, hx, n_)
    lower_body(B, era, soldier, coat)

    weapon(B, era, soldier)
    shield(B, era, soldier)
    return B, rng.uniform(0.96, 1.04)


def headgear(B, era, soldier):
    h = "Head"
    kind = {
        ("ancient", True): "legion", ("ancient", False): "bare",
        ("medieval", True): "kettle", ("medieval", False): "hood",
        ("earlymodern", True): "tricorn", ("earlymodern", False): "slouch",
        ("industrial", True): "kepi", ("industrial", False): "cap",
        ("modern", True): "adrian", ("modern", False): "garrison",
    }[(era, soldier)]
    if kind == "legion":
        B.ball((0, 0.004, 0.905), 0.068, "Metal", h, sy=1.06, half=True, seg=16)
        B.tube((0, 0.004, 0.905), (0, 0.004, 0.913), (0.069, 0.069), "Metal", h, seg=16, sy=1.06)  # brow band
        B.box((0, -0.066, 0.925), (0.08, 0.012, 0.012), "Metal", h, Matrix.Rotation(math.radians(-20), 4, "X"))  # brow ridge
        B.box((0, 0.07, 0.885), (0.11, 0.05, 0.008), "Metal", h, Matrix.Rotation(math.radians(-28), 4, "X"))  # neck guard
        for s in (-1, 1):
            B.plate([(0.064 * s, -0.012, 0.9), (0.064 * s, -0.045, 0.89), (0.062 * s, -0.04, 0.845), (0.062 * s, -0.01, 0.85)], 0.004, "Metal", h)
        for i in range(5):
            B.box((0, -0.04 + i * 0.02, 0.985 - abs(i - 2) * 0.004), (0.014, 0.02, 0.045), "Accent", h)  # crest
        B.box((0, 0.0, 0.966), (0.01, 0.08, 0.01), "Metal", h)
    elif kind == "kettle":
        face = lambda co: co.y < -0.02 and co.z > 0.845
        B.cut(B.tube((0, 0, 0.8), (0, 0, 0.915), [0.07, 0.07, 0.066], "Metal", mix(Head=0.7, Spine=0.3), seg=16, sy=1.04, cap=False), face)  # mail coif, open at the face
        B.ball((0, 0, 0.912), 0.07, "Metal", h, sy=1.05, half=True, seg=16)
        B.tube((0, 0, 0.918), (0, 0, 0.926), [0.118, 0.072], "Metal", h, seg=18)  # the sloping brim
        B.tube((0, 0, 0.926), (0, 0, 0.93), (0.072, 0.07), "Leather", h, seg=16)
    elif kind == "hood":
        face = lambda co: co.y < -0.025 and 0.835 < co.z < 0.935
        B.cut(B.ball((0, 0.008, 0.9), 0.073, "Cloth", h, sy=1.1, sz=1.14, half=True, seg=16), face)
        B.cut(B.tube((0, 0.012, 0.79), (0, 0.012, 0.9), [0.1, 0.078, 0.074], "Cloth", mix(Head=0.7, Spine=0.3), seg=16, cap=False), face)
        B.tube((0, 0.07, 0.93), (0, 0.13, 0.78), (0.02, 0.008), "Cloth", h, seg=8)  # liripipe
    elif kind == "tricorn":
        B.tube((0, 0.004, 0.925), (0, 0.004, 0.968), (0.064, 0.058), "Dark", h, seg=14)
        for i in range(3):
            a = math.radians(90 + i * 120)
            rot = Matrix.Rotation(-a + math.pi / 2, 4, "Z") @ Matrix.Rotation(math.radians(-62), 4, "X")
            c = (math.cos(a) * 0.078, -math.sin(a) * 0.078, 0.948)
            B.box(c, (0.12, 0.008, 0.05), "Dark", h, rot)
            B.box((c[0] * 1.08, c[1] * 1.08, 0.97), (0.11, 0.004, 0.006), "Belts", h, Matrix.Rotation(-a + math.pi / 2, 4, "Z"))  # lace
        B.ball((-0.03, -0.075, 0.962), 0.013, "Belts", h, sy=0.5, seg=6)  # cockade
        B.tube((0, 0.06, 0.88), (0, 0.075, 0.82), (0.012, 0.01), "Hair", h, seg=6)  # the queue
    elif kind == "slouch":
        B.tube((0, 0, 0.928), (0, 0, 0.936), (0.115, 0.108), "Leather", h, seg=18)
        B.ball((0, 0, 0.932), 0.062, "Leather", h, sz=0.95, half=True, seg=14)
        B.tube((0, 0, 0.934), (0, 0, 0.948), (0.064, 0.064), "Dark", h, seg=14)
    elif kind == "kepi":
        B.tube((0, 0.006, 0.925), (0, -0.01, 0.995), [0.062, 0.059, 0.055], "Accent", h, seg=14)
        B.tube((0, 0.006, 0.925), (0, 0.004, 0.945), (0.063, 0.063), "UniformDark", h, seg=14)
        B.box((0, -0.068, 0.928), (0.085, 0.04, 0.006), "Dark", h, Matrix.Rotation(math.radians(-12), 4, "X"))  # visor
        B.box((0, -0.06, 0.92), (0.11, 0.004, 0.006), "Leather", h)  # chinstrap
        B.ball((0, -0.06, 0.965), 0.006, "Belts", h, seg=6)
    elif kind == "cap":
        B.ball((0, -0.006, 0.93), 0.072, "Dark", h, sy=1.18, sz=0.55, half=True, seg=14)
        B.tube((0, 0, 0.926), (0, 0, 0.934), (0.064, 0.064), "Dark", h, seg=14)
        B.box((0, -0.072, 0.928), (0.085, 0.042, 0.005), "Dark", h, Matrix.Rotation(math.radians(-8), 4, "X"))
    elif kind == "adrian":
        B.ball((0, 0, 0.912), 0.072, "UniformDark", h, sy=1.08, sz=0.88, half=True, seg=16)
        B.tube((0, 0, 0.908), (0, 0, 0.914), (0.09, 0.086), "UniformDark", h, seg=18, sy=1.18)  # brim, long front and back
        B.box((0, 0, 0.975), (0.012, 0.11, 0.022), "UniformDark", h)  # crest
        B.ball((0, -0.06, 0.94), 0.008, "Metal", h, seg=6)  # badge
    elif kind == "garrison":
        B.box((0, 0, 0.957), (0.052, 0.13, 0.042), "Accent", h)
        B.box((0, 0, 0.981), (0.032, 0.12, 0.014), "Dark", h)
        B.ball((0, -0.066, 0.97), 0.008, "Dark", h, seg=6)  # tassel


def torso_kit(B, era, soldier, coat, T):
    S = "Spine"
    rotY = lambda deg: Matrix.Rotation(math.radians(deg), 4, "Y")
    if era == "ancient" and soldier:
        for z in (0.6, 0.645, 0.69, 0.735):
            B.tube((0, 0, z), (0, 0, z + 0.038), (0.104, 0.108), "Metal", T, seg=14, sy=0.66)
        for s, n_ in ((-1, "R"), (1, "L")):
            for k in range(3):
                B.ball((0.12 * s, 0, 0.79 - k * 0.022), 0.05 - k * 0.003, "Metal", mix(**{f"UpperArm_{n_}": 0.65, "Spine": 0.35}), sx=1.05, half=True, seg=10)
        B.box((-0.03, 0.03, 0.72), (0.012, 0.005, 0.2), "Leather", S, rotY(35))  # baldric
    if era == "medieval" and soldier:  # tabard with a cross over the gambeson
        B.box((0, -0.068, 0.67), (0.15, 0.01, 0.26), "Uniform", S)
        B.box((0, 0.068, 0.67), (0.15, 0.01, 0.26), "Uniform", S)
        B.box((0, -0.074, 0.69), (0.022, 0.004, 0.14), "Belts", S)
        B.box((0, -0.074, 0.72), (0.09, 0.004, 0.022), "Belts", S)
        for z in (0.58, 0.62, 0.66, 0.70, 0.74):  # quilting seen at the sides
            B.tube((0, 0, z), (0, 0, z + 0.004), (0.103, 0.104), "UniformDark", T, seg=14, sy=0.64, cap=False)
    if era == "medieval" and not soldier:
        B.box((0.09, -0.04, HIP_Z), (0.07, 0.03, 0.08), "Leather", "Hips")  # satchel
        B.box((0.0, -0.045, 0.66), (0.012, 0.005, 0.26), "Leather", S, rotY(-38))
    if era == "earlymodern" and soldier:
        B.box((0, -0.066, 0.66), (0.06, 0.006, 0.22), "Belts", S)  # waistcoat
        for s in (-1, 1):
            B.box((0.04 * s, -0.064, 0.72), (0.03, 0.006, 0.13), "Accent", S, rotY(8 * s))  # lapels
            B.box((0, -0.071, 0.67), (0.02, 0.006, 0.32), "Belts", S, rotY(38 * s))  # crossbelts
        B.box((0, -0.073, 0.67), (0.022, 0.006, 0.022), "Metal", S)  # belt plate
        for z in (0.6, 0.64, 0.68, 0.72, 0.76):
            B.ball((0.012, -0.072, z), 0.005, "Metal", S, seg=4)
        B.box((-0.07, 0.07, HIP_Z + 0.02), (0.07, 0.04, 0.06), "Dark", "Hips")  # cartridge box
    if era == "earlymodern" and not soldier:
        B.box((0, -0.06, 0.66), (0.012, 0.005, 0.3), "Leather", S, rotY(-35))
        B.tube((0.08, -0.03, HIP_Z + 0.02), (0.1, -0.05, HIP_Z - 0.06), (0.018, 0.006), "Belts", "Hips", seg=8)  # powder horn
        B.tube((0, 0, HIP_Z + 0.06), (0, 0, HIP_Z + 0.03), (0.092, 0.092), "Accent", "Hips", seg=14, sy=0.68)  # sash
    if era == "industrial" and soldier:
        B.tube((0, 0, 0.79), (0, 0, 0.81), (0.045, 0.043), "UniformDark", S, seg=12)  # stand collar
        for z in (0.58, 0.62, 0.66, 0.70, 0.74):
            B.ball((0.0, -0.067, z), 0.006, "Metal", S, seg=4)
        for s in (-1, 1):
            B.box((0.11 * s, 0, 0.8), (0.05, 0.04, 0.01), "Accent", S)  # epaulettes
            B.box((0.05 * s, -0.07, HIP_Z + 0.06), (0.04, 0.02, 0.035), "Leather", "Hips")  # pouches
        pack(B, era)
        canteen(B)
    if era == "industrial" and not soldier:
        B.tube((0, 0, HIP_Z + 0.07), (0, 0, HIP_Z + 0.035), (0.092, 0.092), "Accent", "Hips", seg=14, sy=0.68)  # red sash
        B.box((0, 0, 0.8), (0.1, 0.07, 0.02), "Belts", S)  # shirt collar
    if era == "modern" and soldier:
        B.tube((0, 0, 0.78), (0, 0, 0.815), (0.05, 0.045), "UniformDark", S, seg=12)  # collar
        for s in (-1, 1):
            for z in (0.6, 0.65, 0.70, 0.75):
                B.ball((0.03 * s, -0.07, z), 0.006, "Metal", S, seg=4)
            B.box((0.055 * s, -0.073, HIP_Z + 0.06), (0.045, 0.022, 0.04), "Leather", "Hips")
        B.box((0, -0.07, 0.68), (0.012, 0.005, 0.3), "Leather", S, rotY(-36))
        B.tube((0.09, 0.0, HIP_Z + 0.03), (0.09, 0.0, HIP_Z - 0.04), (0.028, 0.028), "UniformDark", "Hips", seg=10)  # gas-mask tin
        pack(B, era)
        canteen(B)
    if era == "modern" and not soldier:
        B.tube((0, 0, 0.785), (0, 0, 0.815), (0.052, 0.047), "Accent", S, seg=12)  # scarf
        B.box((0.025, -0.05, 0.765), (0.03, 0.01, 0.05), "Dark", S)
        B.box((0, -0.066, 0.66), (0.018, 0.006, 0.34), "Leather", S, rotY(-38))  # bandolier
        for k in range(5):
            B.box((-0.06 + k * 0.03, -0.071, 0.61 + k * 0.035), (0.016, 0.01, 0.022), "Leather", S, rotY(-38))
        B.box((0, -0.066, 0.73), (0.08, 0.005, 0.04), coat, S)  # overall bib pocket


def pack(B, era):
    S = "Spine"
    B.box((0, 0.088, 0.68), (0.145, 0.065, 0.16), "Leather", S)
    B.box((0, 0.12, 0.68), (0.13, 0.004, 0.14), "Dark", S)  # flap
    B.tube((-0.085, 0.09, 0.785), (0.085, 0.09, 0.785), (0.028, 0.028), "UniformDark" if era == "modern" else "Belts", S, seg=10)  # bedroll
    for s in (-1, 1):
        B.box((0.06 * s, -0.02, 0.73), (0.014, 0.13, 0.008), "Leather", S, Matrix.Rotation(math.radians(-12), 4, "X"))  # straps


def canteen(B):
    B.ball((0.1, 0.03, HIP_Z - 0.01), 0.032, "UniformDark", "Hips", sx=0.55, seg=10)
    B.tube((0.1, 0.03, HIP_Z + 0.03), (0.1, 0.03, HIP_Z + 0.04), (0.008, 0.008), "Dark", "Hips", seg=6)


def lower_body(B, era, soldier, coat):
    if era == "ancient":
        B.tube((0, 0, HIP_Z + 0.04), (0, 0, HIP_Z - 0.115), [0.088, 0.1, 0.112], coat, skirt(HIP_Z + 0.04, 0.16), seg=14, sy=0.75)
        if soldier:
            for i in range(7):
                a = math.radians(-60 + i * 20)
                B.box((math.sin(a) * 0.09, -math.cos(a) * 0.065, HIP_Z - 0.05), (0.02, 0.008, 0.11), "Leather",
                      skirt(HIP_Z, 0.12), Matrix.Rotation(-a, 4, "Z"))  # pteruges
            B.box((0.08, -0.04, HIP_Z - 0.01), (0.022, 0.03, 0.13), "Leather", "Hips")  # gladius scabbard
            B.box((0.08, -0.04, HIP_Z + 0.07), (0.035, 0.01, 0.012), "Metal", "Hips")
    elif era == "medieval":
        B.tube((0, 0, HIP_Z + 0.04), (0, 0, HIP_Z - 0.15), [0.09, 0.1, 0.11], "Uniform" if soldier else coat,
               skirt(HIP_Z + 0.04, 0.2), seg=14, sy=0.72)
        if soldier:  # sword at the left hip
            B.box((0.095, 0.0, HIP_Z - 0.06), (0.02, 0.025, 0.22), "Leather", "Hips", Matrix.Rotation(math.radians(-12), 4, "X"))
            B.box((0.095, -0.02, HIP_Z + 0.06), (0.06, 0.012, 0.012), "Metal", "Hips")
            B.ball((0.095, -0.03, HIP_Z + 0.09), 0.01, "Metal", "Hips", seg=6)
    elif era == "earlymodern" and soldier:
        B.tube((0, 0, HIP_Z + 0.04), (0, 0, HIP_Z - 0.2), [0.092, 0.11, 0.125], coat, skirt(HIP_Z + 0.04, 0.24), seg=14, sy=0.78, cap=False)
        for s in (-1, 1):  # turned-back skirts in the facing colour
            B.box((0.07 * s, 0.05, HIP_Z - 0.12), (0.04, 0.012, 0.12), "Accent", skirt(HIP_Z, 0.2))
        B.box((0.1, 0.02, HIP_Z - 0.03), (0.018, 0.02, 0.16), "Leather", "Hips")  # bayonet scabbard
    elif era == "earlymodern":
        B.tube((0, 0, HIP_Z + 0.04), (0, 0, HIP_Z - 0.12), [0.09, 0.1, 0.11], coat, skirt(HIP_Z + 0.04, 0.16), seg=14, sy=0.75, cap=False)
    elif era == "industrial" and not soldier:
        B.tube((0, 0, HIP_Z + 0.04), (0, 0, HIP_Z - 0.1), [0.09, 0.1, 0.105], coat, skirt(HIP_Z + 0.04, 0.14), seg=14, sy=0.75, cap=False)  # smock
    elif era == "modern" and soldier:
        B.tube((0, 0, HIP_Z + 0.04), (0, 0, HIP_Z - 0.24), [0.094, 0.11, 0.13], coat, skirt(HIP_Z + 0.04, 0.28), seg=14, sy=0.8, cap=False)  # greatcoat
        B.box((0, 0.08, HIP_Z - 0.06), (0.004, 0.01, 0.2), "UniformDark", skirt(HIP_Z, 0.24))  # back vent


def lower_leg(B, era, soldier, hx, n_):
    shin = f"Shin.{n_}"

    def foot(mat):
        B.ball((hx, -0.028, 0.03), 0.034, mat, shin, sx=0.95, sy=1.8, sz=0.75, seg=10)
        B.box((hx, -0.03, 0.006), (0.06, 0.12, 0.012), "Dark", shin)

    if era == "ancient":  # sandals: soles and straps
        B.ball((hx, -0.028, 0.03), 0.03, "Skin", shin, sx=0.95, sy=1.8, sz=0.7, seg=10)
        B.box((hx, -0.03, 0.006), (0.06, 0.12, 0.012), "Leather", shin)
        for z in (0.05, 0.08, 0.11):
            B.tube((hx, 0, z), (hx, 0, z + 0.008), (0.033, 0.033), "Leather", shin, seg=10, cap=False)
    elif era == "medieval":
        B.tube((hx, 0, 0.14), (hx, 0, ANKLE_Z), (0.036, 0.033), "Leather", shin, seg=10)
        foot("Leather")
    elif era == "earlymodern" and soldier:
        B.tube((hx, 0, KNEE_Z + 0.02), (hx, 0, ANKLE_Z), [0.045, 0.04, 0.035], "Belts", shin, seg=10)  # gaiters
        for z in (0.09, 0.14, 0.19, 0.24):
            B.ball((hx + 0.03 * (1 if hx > 0 else -1), -0.012, z), 0.004, "Dark", shin, seg=4)
        foot("Dark")
    elif era == "industrial" and soldier:
        B.tube((hx, 0, 0.13), (hx, 0, ANKLE_Z), (0.035, 0.033), "Belts", shin, seg=10)  # short gaiters
        foot("Dark")
    elif era == "modern" and soldier:
        for k in range(6):  # puttees, wound
            z = 0.06 + k * 0.022
            B.tube((hx, 0, z), (hx, 0, z + 0.024), (0.035 + k * 0.0012, 0.036 + k * 0.0012), "UniformDark", shin, seg=10, cap=False)
        foot("Dark")
    elif era == "modern":  # rope-soled alpargatas
        B.ball((hx, -0.028, 0.028), 0.032, "Belts", shin, sx=0.95, sy=1.8, sz=0.7, seg=10)
        B.box((hx, -0.03, 0.006), (0.06, 0.12, 0.012), "Leather", shin)
    else:
        foot("Dark" if era == "industrial" else "Leather")


def weapon(B, era, soldier):
    w = "Weapon"
    gx, gy, gz = GRIP
    if era in ("earlymodern", "industrial", "modern"):
        long = {"earlymodern": 0.74, "industrial": 0.64, "modern": 0.62}[era]
        y = lambda dy: gy - dy  # the barrel at the back, the underside (guard, sling, ramrod) facing forward
        B.box((gx, y(0.004), gz - 0.13), (0.03, 0.055, 0.2), "Wood", w)  # butt
        B.box((gx, y(0.012), gz - 0.025), (0.026, 0.035, 0.05), "Wood", w)  # wrist of the stock
        B.box((gx, y(-0.004), gz + long * 0.3), (0.026, 0.03, long * 0.55), "Wood", w)  # fore-end
        B.tube((gx, y(-0.014), gz), (gx, y(-0.014), gz + long * 0.66), (0.008, 0.007), "Metal", w, seg=8)  # barrel
        B.tube((gx, y(0.008), gz + 0.02), (gx, y(0.008), gz + long * 0.58), (0.0025, 0.0025), "Metal", w, seg=4)  # ramrod
        for k in (0.18, 0.36, 0.52):
            B.tube((gx, y(-0.004), gz + long * k), (gx, y(-0.004), gz + long * k + 0.012), (0.017, 0.017), "Metal", w, seg=8)  # bands
        B.box((gx + 0.015, y(-0.006), gz + 0.03), (0.006, 0.02, 0.06), "Metal", w)  # lock plate
        B.box((gx, y(0.02), gz + 0.02), (0.008, 0.012, 0.045), "Metal", w)  # trigger guard
        B.box((gx, y(0.019), gz + long * 0.12), (0.004, 0.003, long * 0.45), "Leather", w)  # sling, close along the stock
        if era == "earlymodern":
            B.box((gx + 0.017, y(-0.008), gz + 0.06), (0.006, 0.012, 0.025), "Metal", w)  # the cock
        else:
            B.tube((gx, y(-0.01), gz + 0.04), (gx - 0.03, y(-0.012), gz + 0.04), (0.004, 0.004), "Metal", w, seg=6)  # bolt handle
            B.ball((gx - 0.032, y(-0.012), gz + 0.04), 0.007, "Metal", w, seg=6)
        if soldier or era == "earlymodern":
            top = gz + long * 0.66
            B.tube((gx, y(-0.022), top - 0.03), (gx, y(-0.022), top + 0.2), (0.007, 0.0), "Metal", w, seg=3)  # bayonet
    else:
        length = 0.95 if era == "ancient" else 1.0
        B.tube((gx, gy, gz - 0.35), (gx, gy, gz - 0.35 + length), (0.011, 0.01), "Wood", w, seg=8)
        top = gz - 0.35 + length
        if era == "ancient" and soldier:  # pilum: long iron shank and pyramid point
            B.tube((gx, gy, top - 0.02), (gx, gy, top + 0.16), (0.004, 0.004), "Metal", w, seg=4)
            B.tube((gx, gy, top + 0.16), (gx, gy, top + 0.2), (0.008, 0.0), "Metal", w, seg=4)
            B.box((gx, gy, top - 0.01), (0.02, 0.02, 0.03), "Wood", w)
        elif era == "medieval" and not soldier:  # a bill: hooked blade and spike
            B.tube((gx, gy, top - 0.04), (gx, gy, top), (0.014, 0.014), "Metal", w, seg=8)
            B.plate([(gx - 0.004, gy - 0.004, top), (gx - 0.004, gy - 0.004, top + 0.12), (gx - 0.004, gy - 0.05, top + 0.08),
                     (gx - 0.004, gy - 0.07, top + 0.03), (gx - 0.004, gy - 0.04, top + 0.04)], 0.008, "Metal", w)
            B.tube((gx, gy, top + 0.1), (gx, gy, top + 0.18), (0.006, 0.0), "Metal", w, seg=4)
        else:  # a socketed leaf blade
            B.tube((gx, gy, top - 0.04), (gx, gy, top + 0.01), (0.013, 0.012), "Metal", w, seg=8)
            B.ball((gx, gy, top + 0.08), 0.02, "Metal", w, sy=0.3, sz=4.0, seg=8)


def shield(B, era, soldier):
    s = "Shield"
    lx, cz = 0.145, 0.6
    if era == "ancient" and soldier:  # scutum: tall, curved, rimmed, with a boss and painted bands
        for i in range(7):
            a = math.radians(-36 + i * 12)
            rot = Matrix.Rotation(a, 4, "Z")
            pos = (lx + math.sin(a) * 0.13, -0.11 + (1 - math.cos(a)) * 0.13, cz)
            B.box(pos, (0.034, 0.012, 0.36), "Uniform", s, rot)
            B.box((pos[0], pos[1] - 0.004, cz + 0.178), (0.034, 0.016, 0.01), "Metal", s, rot)
            B.box((pos[0], pos[1] - 0.004, cz - 0.178), (0.034, 0.016, 0.01), "Metal", s, rot)
        B.ball((lx, -0.122, cz), 0.032, "Metal", s, sy=0.6, seg=10)
        for z in (0.09, -0.09):
            B.box((lx, -0.12, cz + z), (0.16, 0.004, 0.012), "Accent", s)
        B.box((lx, -0.12, cz), (0.012, 0.004, 0.3), "Accent", s)
    elif era == "ancient":
        B.tube((lx, -0.085, cz), (lx, -0.1, cz), (0.13, 0.13), "Wood", s, seg=18)
        B.tube((lx, -0.083, cz), (lx, -0.102, cz), (0.135, 0.135), "Leather", s, seg=18, cap=False)
        B.ball((lx, -0.104, cz), 0.026, "Metal", s, sy=0.6, seg=8)
    elif era == "medieval" and soldier:  # kite shield with a rim and a cross
        pts = [(lx - 0.1, -0.1, 0.72), (lx + 0.1, -0.1, 0.72), (lx + 0.095, -0.1, 0.62), (lx, -0.1, 0.38), (lx - 0.095, -0.1, 0.62)]
        B.plate(pts, 0.016, "Uniform", s)
        for a, b in zip(pts, pts[1:] + pts[:1]):
            B.tube((a[0], -0.103, a[2]), (b[0], -0.103, b[2]), (0.006, 0.006), "Metal", s, seg=6)
        B.box((lx, -0.105, 0.6), (0.02, 0.004, 0.22), "Accent", s)
        B.box((lx, -0.105, 0.66), (0.14, 0.004, 0.02), "Accent", s)
        B.ball((lx, -0.106, 0.64), 0.016, "Metal", s, sy=0.5, seg=8)


# ── Skeleton, weights, shading ──

def materials(colors):
    for name in MATS:
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        c = colors.get(name, DEFAULT[name])
        bsdf.inputs["Base Color"].default_value = (*c, 1)
        bsdf.inputs["Roughness"].default_value = 0.85 if name != "Metal" else 0.4
        if name == "Metal":
            bsdf.inputs["Metallic"].default_value = 0.6
        mat.diffuse_color = (*c, 1)


def bake_occlusion(bm):
    """Ambient occlusion per vertex, from rays into the hemisphere above it, as a grey vertex colour."""
    bm.normal_update()
    tree = BVHTree.FromBMesh(bm)
    n = 24
    dirs = []
    for i in range(n):  # a Fibonacci hemisphere
        z = 1 - (i + 0.5) / n
        r = math.sqrt(max(0.0, 1 - z * z))
        a = i * 2.39996
        dirs.append(Vector((math.cos(a) * r, math.sin(a) * r, z)))
    col = bm.verts.layers.float_color.new("Col")
    for v in bm.verts:
        nrm = v.normal if v.normal.length > 0 else Vector((0, 0, 1))
        rot = nrm.to_track_quat("Z", "Y")
        hits = sum(1 for d in dirs if tree.ray_cast(v.co + nrm * 0.002, rot @ d, 0.08)[0] is not None)
        c = 1 - 0.62 * (hits / n) ** 0.9
        v[col] = (c, c, c, 1)


def assemble(B, scale):
    bm = B.bm
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(50):
            e.smooth = False
    bake_occlusion(bm)
    mesh = bpy.data.meshes.new("Body")
    bm.to_mesh(mesh)
    bm.free()
    for m in MATS:
        mesh.materials.append(bpy.data.materials[m])
    body = bpy.data.objects.new("Body", mesh)
    scene = bpy.context.scene
    scene.collection.objects.link(body)
    for g in GROUPS:
        body.vertex_groups.new(name=g)

    arm_data = bpy.data.armatures.new("Skeleton")
    arm = bpy.data.objects.new("Figure", arm_data)
    scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for name, head, tail, parent in BONES:
        eb = arm_data.edit_bones.new(name)
        eb.head, eb.tail = head, tail
        eb.align_roll(Vector((0, -1, 0)) if tail[2] > head[2] else Vector((0, 1, 0)))
        if parent:
            eb.parent = arm_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    body.parent = arm
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    arm.scale = (scale, scale, scale)
    return arm, body


# ── Animation (rotations given in the parent's frame at rest, converted into each bone's own frame) ──

def key_pose(arm, frame, p, last):
    for key, value in p.items():
        if key == "HipsZ":
            b3 = arm.data.bones["Hips"].matrix_local.to_3x3()
            pb = arm.pose.bones["Hips"]
            pb.location = b3.inverted() @ Vector((0, 0, value))
            pb.keyframe_insert("location", frame=frame)
            continue
        name = key.replace("_", ".")
        if name not in arm.pose.bones:
            continue
        b3 = arm.data.bones[name].matrix_local.to_3x3()
        r = Euler([math.radians(v) for v in value], "XYZ").to_matrix()
        q = (b3.inverted() @ r @ b3).to_quaternion()
        if name in last and last[name].dot(q) < 0:
            q.negate()
        last[name] = q
        pb = arm.pose.bones[name]
        pb.rotation_quaternion = q
        pb.keyframe_insert("rotation_quaternion", frame=frame)


def rest_pose():
    return dict(HipsZ=0.0, Hips=(0, 0, 0), Spine=(0, 0, 0), Head=(0, 0, 0),
                UpperArm_R=(-8, 0, 0), Forearm_R=(-38, 0, 0), Weapon=(46, 0, 0),
                UpperArm_L=(4, 0, -4), Forearm_L=(-12, 0, 0), Shield=(0, 0, 0),
                Thigh_R=(0, 0, 0), Thigh_L=(0, 0, 0), Shin_R=(0, 0, 0), Shin_L=(0, 0, 0))


def aim_pose(firearm):
    p = rest_pose()
    p.update(UpperArm_R=(-62, 0, 12), Forearm_R=(-28, 0, 0), Weapon=(180, 0, 0), Head=(6, 0, 0), Spine=(4, 0, 0))
    if firearm:
        p.update(UpperArm_L=(-78, 0, -28), Forearm_L=(-20, 0, 0))
    else:
        p.update(UpperArm_L=(-42, 0, -10), Forearm_L=(-50, 0, 0), Shield=(92, 0, 0))
    return p


def animate(arm, era):
    firearm = era in ("earlymodern", "industrial", "modern")
    clips = {}

    def clip(name, keys, length):
        last = {}
        for frame, p in keys:
            full = rest_pose()
            full.update(p)
            key_pose(arm, frame, full, last)
        ad = arm.animation_data
        action = ad.action
        action.name = name
        track = ad.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, action)
        strip.frame_end = length + 1
        ad.action = None
        clips[name] = length

    rest = rest_pose()
    k = []
    for f, t in ((1, 0), (25, 1), (49, 0)):
        p = dict(rest)
        p.update(Spine=(1.5 * t, 0, 0), HipsZ=0.002 * t, Head=(-2 + 3 * t, 4 * t, 0))
        k.append((f, p))
    clip("Idle", k, 48)

    def step(phase):
        s = math.sin(phase * 2 * math.pi)
        c = math.cos(phase * 2 * math.pi)
        p = dict(rest)
        p.update(Thigh_L=(-26 * s, 0, 0), Thigh_R=(26 * s, 0, 0),
                 Shin_L=(max(0, 40 * -c) + 5, 0, 0) if s < 0 else (6, 0, 0),
                 Shin_R=(max(0, 40 * c) + 5, 0, 0) if s > 0 else (6, 0, 0),
                 UpperArm_L=(22 * s, 0, -4), Forearm_L=(-15 - 10 * max(0, -s), 0, 0),
                 Spine=(4, 0, 0), HipsZ=0.008 * abs(c))
        return p
    clip("March", [(1 + int(i * 3), step(i / 8)) for i in range(9)], 24)

    aim = aim_pose(firearm)
    if firearm:
        recoil = dict(aim)
        recoil.update(Spine=(-4, 0, 0), UpperArm_R=(-70, 0, 12), Head=(0, 0, 0))
        reload = dict(rest)
        reload.update(UpperArm_R=(-28, 0, 6), Forearm_R=(-70, 0, 0), Weapon=(150, 0, 0),
                      UpperArm_L=(-40, 0, -20), Forearm_L=(-75, 0, 0), Spine=(8, 0, 0), Head=(14, 0, 0))
        clip("Attack", [(1, aim), (6, aim), (8, recoil), (12, aim), (20, reload), (34, reload), (44, aim), (49, aim)], 48)
    else:
        back = dict(aim)
        back.update(UpperArm_R=(-30, 0, 12), Forearm_R=(-40, 0, 0), Spine=(-2, 0, 0))
        thrust = dict(aim)
        thrust.update(UpperArm_R=(-85, 0, 8), Forearm_R=(-5, 0, 0), Spine=(10, 0, 0), Thigh_L=(-18, 0, 0), Shin_L=(10, 0, 0))
        clip("Attack", [(1, back), (10, thrust), (16, thrust), (30, back), (37, back)], 36)

    sway = dict(aim)
    sway.update(Spine=(5, 2, 0))
    clip("Guard", [(1, aim), (25, sway), (49, aim)], 48)
    return clips


# ── Build, export, preview ──

SKIN = [(0.82, 0.64, 0.52), (0.74, 0.55, 0.42), (0.62, 0.45, 0.33), (0.86, 0.70, 0.58)]
HAIR = [(0.16, 0.11, 0.08), (0.30, 0.20, 0.12), (0.10, 0.08, 0.07), (0.45, 0.33, 0.20), (0.50, 0.48, 0.45)]
MILITIA = {
    "ancient": [(0.62, 0.55, 0.42), (0.45, 0.36, 0.26), (0.55, 0.50, 0.45)],
    "medieval": [(0.40, 0.33, 0.22), (0.36, 0.40, 0.28), (0.52, 0.42, 0.30)],
    "earlymodern": [(0.42, 0.30, 0.20), (0.32, 0.33, 0.30), (0.52, 0.44, 0.33)],
    "industrial": [(0.30, 0.38, 0.52), (0.40, 0.34, 0.27), (0.33, 0.33, 0.33)],
    "modern": [(0.22, 0.32, 0.50), (0.28, 0.36, 0.52), (0.38, 0.33, 0.25)],
}


def build(era, side, n, path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    rng = random.Random(f"colors/{era}/{side}/{n}")
    colors = {"Skin": rng.choice(SKIN), "Hair": rng.choice(HAIR)}
    if side == "militia":
        colors["Uniform" if era == "modern" else "Cloth"] = MILITIA[era][n]
    materials(colors)
    B, scale = figure(era, side, n)
    arm, body = assemble(B, scale)
    clips = animate(arm, era)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_animation_mode="NLA_TRACKS",
                              export_vertex_color="ACTIVE", export_yup=True, export_skins=True)
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    if PREVIEW and n == 0:
        render_clips(arm, clips, os.path.join(PREVIEW, os.path.basename(path)[:-4]))
    return tris


def shade_with_occlusion():
    """For previews: multiply each material's colour by the baked occlusion."""
    for mat in bpy.data.materials:
        nt = mat.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        if bsdf is None:
            continue
        attr = nt.nodes.new("ShaderNodeVertexColor")
        attr.layer_name = "Col"
        mixn = nt.nodes.new("ShaderNodeMix")
        mixn.data_type = "RGBA"
        mixn.blend_type = "MULTIPLY"
        mixn.inputs[0].default_value = 1.0
        mixn.inputs[6].default_value = bsdf.inputs["Base Color"].default_value
        nt.links.new(attr.outputs["Color"], mixn.inputs[7])
        nt.links.new(mixn.outputs[2], bsdf.inputs["Base Color"])


def render_clips(arm, clips, stem):
    os.makedirs(os.path.dirname(stem), exist_ok=True)
    shade_with_occlusion()
    scene = bpy.context.scene
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.location = (-0.75, -1.9, 0.85)
    cam.rotation_euler = (Vector((0, 0, 0.5)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50
    scene.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.36, 0.38, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    scene.world = world
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 440, 640
    frames = {"Idle": 1, "March": 7, "Attack": 10, "Guard": 1}
    for clip_name in clips:
        for t in arm.animation_data.nla_tracks:
            t.mute = t.name != clip_name
        scene.frame_set(frames.get(clip_name, 1))
        scene.render.filepath = f"{stem}_{clip_name.lower()}.png"
        bpy.ops.render.render(write_still=True)
    # A close-up of the head and shoulders.
    for t in arm.animation_data.nla_tracks:
        t.mute = t.name != "Idle"
    scene.frame_set(1)
    cam.location = (-0.25, -0.75, 0.9)
    cam.rotation_euler = (Vector((0, 0, 0.82)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = f"{stem}_closeup.png"
    bpy.ops.render.render(write_still=True)


def main():
    total = []
    for era in ERAS:
        for side in ("soldier", "militia"):
            for n in range(VARIANTS):
                total.append(build(era, side, n, os.path.join(OUT, f"{era}_{side}_{n}.glb")))
    print(f"built {len(total)} figures, {min(total)}-{max(total)} triangles")


main()
