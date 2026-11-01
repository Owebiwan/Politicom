"""
Builds Politicom's soldiers and militia (Game/models/figures/<era>_<side>[_n].glb) in Blender,
from code: low-poly figures with natural proportions, one unit tall, facing +Z in Godot (-Y in
Blender), each limb its own object hinged at its joint, with four animations:

  Idle    standing at ease, weapon shouldered
  March   walking, weapon shouldered
  Attack  firing and reloading (muskets, rifles) or thrusting a spear (antiquity, middle ages)
  Guard   taking aim, or shield raised

Eras: ancient (legionary; tunic and spear), medieval (kettle helmet, tabard, kite shield; hood and
bill), earlymodern (tricorn, long coat, musket; slouch hat), industrial (kepi, rifle; cap and smock),
modern (steel helmet, greatcoat; overalls, cap and red-and-black scarf). A soldier's "Uniform"
material is tinted with the army's colour in the game ("UniformDark" a darker shade); militia
come in several colour variants and keep their own clothes.

Usage:
  blender --background --factory-startup --python tools/models/build_soldiers.py -- OUTPUT_DIR [--preview DIR]
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else "Game/models/figures"
PREVIEW = ARGS[ARGS.index("--preview") + 1] if "--preview" in ARGS else None
FPS = 24

MATS = ["Uniform", "UniformDark", "Cloth", "Skin", "Leather", "Metal", "Wood", "Accent", "Belts", "Dark", "Hair"]
DEFAULT = {
    "Uniform": (0.45, 0.47, 0.52), "UniformDark": (0.28, 0.29, 0.32), "Cloth": (0.36, 0.33, 0.30),
    "Skin": (0.80, 0.62, 0.50), "Leather": (0.36, 0.24, 0.15), "Metal": (0.55, 0.56, 0.58),
    "Wood": (0.45, 0.30, 0.17), "Accent": (0.70, 0.13, 0.10), "Belts": (0.86, 0.84, 0.78),
    "Dark": (0.10, 0.09, 0.09), "Hair": (0.20, 0.14, 0.10),
}
M = {name: i for i, name in enumerate(MATS)}


# ── Geometry helpers (world coordinates, Blender Z up, front is -Y) ──

def _assign(res, mat):
    faces = {f for v in res["verts"] for f in v.link_faces}
    for f in faces:
        f.material_index = M[mat]


def box(bm, center, size, mat, rot=None):
    m = Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1))
    _assign(bmesh.ops.create_cube(bm, size=1.0, matrix=m), mat)


def tube(bm, a, b, r1, r2, mat, seg=8, sy=1.0):
    """A tapered cylinder from point a (radius r1) to point b (radius r2); sy flattens it front to back."""
    a, b = Vector(a), Vector(b)
    d = b - a
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    m = Matrix.Translation((a + b) / 2) @ rot @ Matrix.Diagonal((1, sy, 1, 1))
    _assign(bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r1, radius2=r2,
                                  depth=d.length, matrix=m), mat)


def ball(bm, center, r, mat, sx=1.0, sy=1.0, sz=1.0, seg=10, half=False):
    m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((sx, sy, sz, 1))
    res = bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=max(4, seg // 2 + 1), radius=r, matrix=m)
    if half:  # a dome: drop the lower half
        cz = center[2]
        bmesh.ops.delete(bm, geom=[v for v in res["verts"] if v.co.z < cz - 1e-4], context="VERTS")
        res = {"verts": [v for v in res["verts"] if v.is_valid]}
    _assign(res, mat)


def part(name, joint, build, parent=None, parent_joint=None):
    """An object hinged at `joint`: build(bm) draws in world coordinates; the mesh is moved to the joint."""
    bm = bmesh.new()
    build(bm)
    bmesh.ops.translate(bm, verts=bm.verts, vec=-Vector(joint))
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for m in MATS:
        mesh.materials.append(bpy.data.materials[m])
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
        obj.location = Vector(joint) - Vector(parent_joint)
    else:
        obj.location = Vector(joint)
    obj.rotation_mode = "XYZ"
    return obj


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


# ── The figure ──

SHOULDER_Z, ELBOW_Z, WRIST_Z = 0.775, 0.615, 0.47
HIP_Z, KNEE_Z, ANKLE_Z = 0.50, 0.275, 0.045


def figure(era, side):
    """Builds one figure; returns its parts by name."""
    soldier = side == "soldier"
    coat = "Uniform" if soldier or era == "modern" else "Cloth"  # the 1936 militia's blue overalls are "Uniform" too (never tinted)
    legs = {"ancient": "Skin", "medieval": "Cloth", "earlymodern": "Belts" if soldier else "Cloth",
            "industrial": "Accent" if soldier else "Cloth", "modern": coat}[era]
    if era == "modern" and not soldier:
        legs = "Uniform"  # the militia's overalls: one colour top to bottom
    P = {}

    def hips(bm):
        box(bm, (0, 0, HIP_Z + 0.01), (0.17, 0.11, 0.08), coat if era in ("ancient", "modern") or not soldier else legs)
        box(bm, (0, 0, HIP_Z + 0.045), (0.175, 0.115, 0.022), "Leather")  # belt
        if era == "ancient":  # tunic skirt and the leather strips over it
            tube(bm, (0, 0, HIP_Z + 0.03), (0, 0, HIP_Z - 0.11), 0.088, 0.11, coat, seg=10, sy=0.75)
            if soldier:
                for i in range(5):
                    box(bm, (-0.06 + i * 0.03, -0.075, HIP_Z - 0.05), (0.018, 0.008, 0.1), "Leather")
        if era == "medieval":  # the tabard's skirt
            tube(bm, (0, 0, HIP_Z + 0.03), (0, 0, HIP_Z - 0.15), 0.09, 0.105, coat, seg=8, sy=0.7)
        if era == "earlymodern" and soldier:  # the long coat's skirts, front open
            tube(bm, (0, 0, HIP_Z + 0.03), (0, 0, HIP_Z - 0.2), 0.092, 0.12, coat, seg=10, sy=0.75)
        if era == "modern" and soldier:  # greatcoat
            tube(bm, (0, 0, HIP_Z + 0.03), (0, 0, HIP_Z - 0.23), 0.092, 0.125, coat, seg=10, sy=0.8)
        if era == "industrial" and not soldier:  # a smock
            tube(bm, (0, 0, HIP_Z + 0.03), (0, 0, HIP_Z - 0.1), 0.09, 0.1, coat, seg=8, sy=0.75)
    P["Hips"] = part("Hips", (0, 0, HIP_Z), hips)

    def torso(bm):
        tube(bm, (0, 0, HIP_Z + 0.03), (0, 0, 0.80), 0.082, 0.11, coat, seg=8, sy=0.62)
        tube(bm, (0, 0, 0.80), (0, 0, 0.845), 0.03, 0.028, "Skin", seg=6)  # neck
        if era == "ancient" and soldier:  # segmented armour bands
            for z in (0.62, 0.68, 0.74):
                tube(bm, (0, 0, z), (0, 0, z + 0.035), 0.1, 0.106, "Metal", seg=8, sy=0.66)
        if era == "earlymodern" and soldier:  # crossbelts and turned-back lapels
            for s in (-1, 1):
                rot = Matrix.Rotation(math.radians(38 * s), 4, "Y")
                box(bm, (0, -0.064, 0.67), (0.02, 0.006, 0.3), "Belts", rot)
            box(bm, (0, -0.066, 0.71), (0.07, 0.005, 0.14), "Accent")
        if era == "medieval" and soldier:  # a cross on the tabard
            box(bm, (0, -0.066, 0.68), (0.02, 0.005, 0.12), "Belts")
            box(bm, (0, -0.066, 0.70), (0.08, 0.005, 0.02), "Belts")
        if era in ("industrial", "modern") and soldier:  # pack and blanket roll
            box(bm, (0, 0.085, 0.68), (0.14, 0.06, 0.15), "Leather")
            tube(bm, (-0.08, 0.085, 0.775), (0.08, 0.085, 0.775), 0.025, 0.025, "UniformDark", seg=6)
        if era == "industrial" and soldier:  # buttons down the front
            for z in (0.6, 0.65, 0.7, 0.75):
                ball(bm, (0, -0.068, z), 0.007, "Belts", seg=4)
        if era == "modern" and not soldier:  # the red-and-black scarf
            tube(bm, (0, 0, 0.79), (0, 0, 0.815), 0.05, 0.045, "Accent", seg=8)
            box(bm, (0.025, -0.05, 0.765), (0.03, 0.01, 0.05), "Dark")
        if era == "modern" and soldier:  # ammunition pouches
            for s in (-1, 1):
                box(bm, (0.05 * s, -0.07, 0.575), (0.04, 0.02, 0.035), "Leather")
    P["Torso"] = part("Torso", (0, 0, HIP_Z + 0.03), torso, P["Hips"], (0, 0, HIP_Z))

    def head(bm):
        ball(bm, (0, 0, 0.89), 0.06, "Skin", sy=1.05, sz=1.12)
        ball(bm, (0, -0.058, 0.88), 0.012, "Skin", seg=6)  # nose
        hat = {
            ("ancient", "soldier"): "legion", ("ancient", "militia"): "hair",
            ("medieval", "soldier"): "kettle", ("medieval", "militia"): "hood",
            ("earlymodern", "soldier"): "tricorn", ("earlymodern", "militia"): "slouch",
            ("industrial", "soldier"): "kepi", ("industrial", "militia"): "cap",
            ("modern", "soldier"): "helmet", ("modern", "militia"): "garrison",
        }[(era, side)]
        if hat == "hair":
            ball(bm, (0, 0.006, 0.905), 0.062, "Hair", sy=1.05, sz=0.95, half=True)
        elif hat == "legion":
            ball(bm, (0, 0, 0.905), 0.067, "Metal", sy=1.05, half=True)
            box(bm, (0, 0.06, 0.885), (0.1, 0.012, 0.04), "Metal")  # neck guard
            for s in (-1, 1):
                box(bm, (0.062 * s, -0.01, 0.875), (0.008, 0.04, 0.05), "Metal")  # cheek guards
            box(bm, (0, 0.0, 0.985), (0.012, 0.11, 0.04), "Accent")  # crest
        elif hat == "kettle":
            ball(bm, (0, 0, 0.905), 0.066, "Metal", sy=1.05, half=True)
            tube(bm, (0, 0, 0.9), (0, 0, 0.906), 0.11, 0.105, "Metal", seg=12)
        elif hat == "hood":
            ball(bm, (0, 0.005, 0.895), 0.072, "Cloth", sy=1.08, sz=1.12, half=True)
            tube(bm, (0, 0.02, 0.82), (0, 0.02, 0.86), 0.085, 0.07, "Cloth", seg=8)
        elif hat == "tricorn":
            tube(bm, (0, 0, 0.93), (0, 0, 0.965), 0.064, 0.058, "Dark", seg=10)
            for i in range(3):
                a = math.radians(90 + i * 120)
                rot = Matrix.Rotation(-a + math.pi / 2, 4, "Z") @ Matrix.Rotation(math.radians(-60), 4, "X")
                box(bm, (math.cos(a) * 0.075, -math.sin(a) * 0.075, 0.95), (0.11, 0.008, 0.045), "Dark", rot)
            ball(bm, (0, -0.08, 0.965), 0.012, "Belts", seg=4)  # cockade
        elif hat == "slouch":
            tube(bm, (0, 0, 0.93), (0, 0, 0.94), 0.11, 0.1, "Leather", seg=12)
            ball(bm, (0, 0, 0.935), 0.06, "Leather", half=True)
        elif hat == "kepi":
            rot = Matrix.Rotation(math.radians(-10), 4, "X")
            tube(bm, (0, 0.005, 0.925), (0, -0.008, 0.99), 0.06, 0.054, "Accent", seg=10)
            tube(bm, (0, 0.005, 0.925), (0, 0.004, 0.94), 0.062, 0.062, "UniformDark", seg=10)
            box(bm, (0, -0.066, 0.928), (0.08, 0.035, 0.006), "Dark", rot)
        elif hat == "cap":
            ball(bm, (0, -0.01, 0.935), 0.07, "Dark", sy=1.15, sz=0.55, half=True)
            box(bm, (0, -0.07, 0.932), (0.08, 0.04, 0.006), "Dark")
        elif hat == "helmet":
            ball(bm, (0, 0, 0.91), 0.074, "UniformDark", sy=1.08, sz=0.85, half=True)
            tube(bm, (0, 0, 0.905), (0, 0, 0.912), 0.088, 0.084, "UniformDark", seg=12)
        elif hat == "garrison":
            box(bm, (0, 0, 0.955), (0.05, 0.13, 0.04), "Accent")
            box(bm, (0, 0, 0.978), (0.03, 0.12, 0.012), "Dark")
    P["Head"] = part("Head", (0, 0, 0.83), head, P["Torso"], (0, 0, HIP_Z + 0.03))

    for s, n in ((1, "R"), (-1, "L")):
        x = 0.125 * s * -1  # Blender -X is the figure's right when it faces -Y
        x = -0.125 if n == "R" else 0.125
        sleeve = "Skin" if era == "ancient" else coat

        def upper(bm, x=x, sleeve=sleeve):
            ball(bm, (x, 0, SHOULDER_Z), 0.042, coat, seg=8)
            tube(bm, (x, 0, SHOULDER_Z), (x, 0, ELBOW_Z), 0.035, 0.03, sleeve)
            if era == "earlymodern" and soldier:
                tube(bm, (x, 0, ELBOW_Z + 0.03), (x, 0, ELBOW_Z - 0.01), 0.034, 0.034, "Accent")  # cuffs
        P["UpperArm." + n] = part("UpperArm." + n, (x, 0, SHOULDER_Z), upper, P["Torso"], (0, 0, HIP_Z + 0.03))

        def fore(bm, x=x, sleeve=sleeve):
            tube(bm, (x, 0, ELBOW_Z), (x, 0, WRIST_Z), 0.03, 0.025, sleeve)
            ball(bm, (x, 0, WRIST_Z - 0.02), 0.026, "Skin", seg=8)
        P["Forearm." + n] = part("Forearm." + n, (x, 0, ELBOW_Z), fore, P["UpperArm." + n], (x, 0, SHOULDER_Z))

        hx = -0.055 if n == "R" else 0.055

        def thigh(bm, hx=hx):
            tube(bm, (hx, 0, HIP_Z), (hx, 0, KNEE_Z), 0.056, 0.044, legs, seg=8)
        P["Thigh." + n] = part("Thigh." + n, (hx, 0, HIP_Z), thigh, P["Hips"], (0, 0, HIP_Z))

        def shin(bm, hx=hx):
            boots = "Dark" if era in ("earlymodern", "industrial", "modern") else "Leather"
            if era == "earlymodern" and soldier:
                tube(bm, (hx, 0, KNEE_Z + 0.02), (hx, 0, 0.12), 0.044, 0.036, "Belts", seg=8)  # gaiters
                tube(bm, (hx, 0, 0.12), (hx, 0, ANKLE_Z), 0.036, 0.032, "Dark", seg=8)
            elif era == "modern" and soldier:
                tube(bm, (hx, 0, KNEE_Z), (hx, 0, 0.16), 0.043, 0.036, legs, seg=8)
                tube(bm, (hx, 0, 0.16), (hx, 0, ANKLE_Z), 0.037, 0.033, "UniformDark", seg=8)  # puttees
            else:
                tube(bm, (hx, 0, KNEE_Z), (hx, 0, ANKLE_Z), 0.043, 0.032, legs, seg=8)
            box(bm, (hx, -0.022, ANKLE_Z / 2 + 0.002), (0.058, 0.11, 0.045), boots)
        P["Shin." + n] = part("Shin." + n, (hx, 0, KNEE_Z), shin, P["Thigh." + n], (hx, 0, HIP_Z))

    # Weapons: in the right hand, along +Z with the grip at the origin (pointing up when the arm hangs).
    grip = (-0.125, 0, WRIST_Z - 0.02)
    firearm = era in ("earlymodern", "industrial", "modern")

    def weapon(bm):
        gx, gy, gz = grip
        if firearm:
            long = {"earlymodern": 0.72, "industrial": 0.62, "modern": 0.6}[era]
            box(bm, (gx, gy, gz - 0.12), (0.03, 0.05, 0.22), "Wood")  # stock
            box(bm, (gx, gy - 0.005, gz + long * 0.3), (0.026, 0.03, long * 0.55), "Wood")  # fore-end
            tube(bm, (gx, gy - 0.012, gz), (gx, gy - 0.012, gz + long * 0.66), 0.008, 0.007, "Metal", seg=6)
            if soldier or era == "earlymodern":  # bayonet
                tube(bm, (gx, gy - 0.02, gz + long * 0.6), (gx, gy - 0.02, gz + long * 0.6 + 0.16), 0.006, 0.0, "Metal", seg=4)
        elif era == "ancient":
            tube(bm, (gx, gy, gz - 0.35), (gx, gy, gz + 0.55), 0.012, 0.011, "Wood", seg=6)  # pilum / spear
            tube(bm, (gx, gy, gz + 0.55), (gx, gy, gz + 0.66), 0.012, 0.0, "Metal", seg=6)
        else:
            tube(bm, (gx, gy, gz - 0.4), (gx, gy, gz + 0.6), 0.012, 0.011, "Wood", seg=6)
            if soldier:
                tube(bm, (gx, gy, gz + 0.6), (gx, gy, gz + 0.72), 0.015, 0.0, "Metal", seg=6)
            else:  # a bill hook
                box(bm, (gx, gy - 0.02, gz + 0.62), (0.012, 0.05, 0.1), "Metal")
    P["Weapon"] = part("Weapon", grip, weapon, P["Forearm.R"], (-0.125, 0, ELBOW_Z))

    if era in ("ancient", "medieval") or (era == "ancient"):
        lx = 0.125

        def shield(bm):
            if era == "ancient" and soldier:  # scutum: tall, curved, with a boss
                for i in range(5):
                    a = math.radians(-36 + i * 18)
                    rot = Matrix.Rotation(a, 4, "Z")
                    box(bm, (lx + math.sin(a) * 0.12 + 0.02, -0.11 + (1 - math.cos(a)) * 0.12, 0.6), (0.045, 0.012, 0.36), "Uniform", rot)
                ball(bm, (lx + 0.02, -0.125, 0.6), 0.03, "Metal", seg=8)
                box(bm, (lx + 0.02, -0.122, 0.6), (0.2, 0.006, 0.025), "Accent")
            elif era == "ancient":  # a round wooden shield
                tube(bm, (lx + 0.02, -0.085, 0.58), (lx + 0.02, -0.1, 0.58), 0.13, 0.13, "Wood", seg=14)
                ball(bm, (lx + 0.02, -0.105, 0.58), 0.025, "Metal", seg=8)
            elif soldier:  # a kite shield
                bm2 = bmesh.new()
                verts = [bm.verts.new(v) for v in [
                    (lx + 0.02 - 0.1, -0.1, 0.72), (lx + 0.02 + 0.1, -0.1, 0.72), (lx + 0.02 + 0.09, -0.1, 0.6),
                    (lx + 0.02, -0.1, 0.38), (lx + 0.02 - 0.09, -0.1, 0.6)]]
                f = bm.faces.new(verts)
                f.material_index = M["Uniform"]
                ext = bmesh.ops.extrude_face_region(bm, geom=[f])
                bmesh.ops.translate(bm, verts=[v for v in ext["geom"] if isinstance(v, bmesh.types.BMVert)], vec=(0, 0.014, 0))
                bm2.free()
                box(bm, (lx + 0.02, -0.102, 0.62), (0.018, 0.004, 0.2), "Accent")
                box(bm, (lx + 0.02, -0.102, 0.66), (0.14, 0.004, 0.018), "Accent")
        if not (era == "medieval" and not soldier):
            P["Shield"] = part("Shield", (lx, 0, ELBOW_Z), shield, P["Forearm.L"], (lx, 0, ELBOW_Z))
    return P


# ── Animation ──

def pose(P, frame, **rot):
    """Keys rotations (degrees, XYZ) for the named parts at a frame; '_' in names stands for '.'."""
    for key, value in rot.items():
        name = key.replace("_", ".")
        if name == "HipsZ":
            P["Hips"].location.z = HIP_Z + value
            P["Hips"].keyframe_insert("location", frame=frame)
            continue
        if name not in P:
            continue
        P[name].rotation_euler = [math.radians(v) for v in value]
        P[name].keyframe_insert("rotation_euler", frame=frame)


ANIMATED = ["Hips", "Torso", "Head", "UpperArm.R", "UpperArm.L", "Forearm.R", "Forearm.L",
            "Thigh.R", "Thigh.L", "Shin.R", "Shin.L", "Weapon", "Shield"]


def rest_pose(firearm):
    """Weapon shouldered: right arm down, forearm a little forward, weapon upright."""
    return dict(Hips=(0, 0, 0), Torso=(0, 0, 0), Head=(0, 0, 0),
                UpperArm_R=(-8, 0, 0), Forearm_R=(-38, 0, 0), Weapon=(46, 0, 0),
                UpperArm_L=(4, 0, -4), Forearm_L=(-12, 0, 0), Shield=(0, 0, 0),
                Thigh_R=(0, 0, 0), Thigh_L=(0, 0, 0), Shin_R=(0, 0, 0), Shin_L=(0, 0, 0))


def aim_pose(firearm):
    """Weapon levelled forward at the shoulder; the left hand under it (or the shield up)."""
    p = rest_pose(firearm)
    p.update(UpperArm_R=(-62, 0, 12), Forearm_R=(-28, 0, 0), Weapon=(180, 0, 0), Head=(6, 0, 0), Torso=(4, 0, 0))
    if firearm:
        p.update(UpperArm_L=(-78, 0, -28), Forearm_L=(-20, 0, 0))
    else:
        # Shield up before the body: the arm raised, the shield turned back upright and facing out.
        p.update(UpperArm_L=(-42, 0, -10), Forearm_L=(-50, 0, 0), Shield=(92, 0, 0))
    return p


def animate(P, era):
    firearm = era in ("earlymodern", "industrial", "modern")
    clips = {}

    def clip(name, keys, length):
        """keys: list of (frame, pose dict)."""
        # Each clip keys a fresh action per part (the previous clip's went into its NLA track).
        for frame, p in keys:
            pose(P, frame, **p)
        for n in ANIMATED:
            if n not in P or P[n].animation_data is None or P[n].animation_data.action is None:
                continue
            ad = P[n].animation_data
            action = ad.action
            action.name = f"{n}|{name}"
            track = ad.nla_tracks.new()
            track.name = name
            strip = track.strips.new(name, 1, action)
            strip.frame_end = length + 1
            ad.action = None
        clips[name] = length

    rest = rest_pose(firearm)

    # Idle: breathing, a little sway.
    k = []
    for f, t in ((1, 0), (25, 1), (49, 0)):
        p = dict(rest)
        p.update(Torso=(1.5 * t, 0, 0), HipsZ=0.002 * t, Head=(-2 + 3 * t, 4 * t, 0))
        k.append((f, p))
    clip("Idle", k, 48)

    # March: a one-second stride.
    def step(phase):
        s = math.sin(phase * 2 * math.pi)
        c = math.cos(phase * 2 * math.pi)
        p = dict(rest)
        p.update(
            Thigh_L=(-26 * s, 0, 0), Thigh_R=(26 * s, 0, 0),
            Shin_L=(max(0, 40 * -c) + 5, 0, 0) if s < 0 else (6, 0, 0),
            Shin_R=(max(0, 40 * c) + 5, 0, 0) if s > 0 else (6, 0, 0),
            UpperArm_L=(22 * s, 0, -4), Forearm_L=(-15 - 10 * max(0, -s), 0, 0),
            Torso=(4, 0, 0), HipsZ=0.008 * abs(c),
        )
        return p
    clip("March", [(1 + int(i * 3), step(i / 8)) for i in range(9)], 24)

    aim = aim_pose(firearm)
    if firearm:
        # Attack: aim, fire (recoil), lower and reload, aim again.
        recoil = dict(aim)
        recoil.update(Torso=(-4, 0, 0), UpperArm_R=(-70, 0, 12), Head=(0, 0, 0))
        reload = dict(rest)
        reload.update(UpperArm_R=(-28, 0, 6), Forearm_R=(-70, 0, 0), Weapon=(150, 0, 0),
                      UpperArm_L=(-40, 0, -20), Forearm_L=(-75, 0, 0), Torso=(8, 0, 0), Head=(14, 0, 0))
        clip("Attack", [(1, aim), (6, aim), (8, recoil), (12, aim), (20, reload), (34, reload), (44, aim), (49, aim)], 48)
    else:
        # Attack: spear thrusts.
        back = dict(aim)
        back.update(UpperArm_R=(-30, 0, 12), Forearm_R=(-40, 0, 0), Torso=(-2, 0, 0))
        thrust = dict(aim)
        thrust.update(UpperArm_R=(-85, 0, 8), Forearm_R=(-5, 0, 0), Torso=(10, 0, 0), Thigh_L=(-18, 0, 0), Shin_L=(10, 0, 0))
        clip("Attack", [(1, back), (10, thrust), (16, thrust), (30, back), (37, back)], 36)

    # Guard: aiming (or shield up), a slight sway.
    sway = dict(aim)
    sway.update(Torso=(5, 2, 0))
    clip("Guard", [(1, aim), (25, sway), (49, aim)], 48)
    return clips


# ── Build, preview, export ──

MILITIA = {  # civilian colours per era: a few variants each
    "ancient": [{"Cloth": (0.62, 0.55, 0.42)}, {"Cloth": (0.45, 0.36, 0.26)}, {"Cloth": (0.55, 0.5, 0.45)}],
    "medieval": [{"Cloth": (0.40, 0.33, 0.22)}, {"Cloth": (0.36, 0.40, 0.28)}, {"Cloth": (0.52, 0.42, 0.30)}],
    "earlymodern": [{"Cloth": (0.42, 0.30, 0.20)}, {"Cloth": (0.32, 0.33, 0.30)}, {"Cloth": (0.52, 0.44, 0.33)}],
    "industrial": [{"Cloth": (0.30, 0.38, 0.52)}, {"Cloth": (0.40, 0.34, 0.27)}, {"Cloth": (0.33, 0.33, 0.33)}],
    "modern": [{"Uniform": (0.22, 0.32, 0.50)}, {"Uniform": (0.28, 0.36, 0.52)}, {"Uniform": (0.38, 0.33, 0.25)}],
}


def build(era, side, colors, path, preview_this=True):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    materials(colors)
    P = figure(era, side)
    clips = animate(P, era)
    if PREVIEW and preview_this:
        os.makedirs(PREVIEW, exist_ok=True)
        render_clips(P, clips, os.path.join(PREVIEW, os.path.basename(path)[:-4]))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_animation_mode="NLA_TRACKS",
                              export_apply=False, export_yup=True)
    return P


def render_clips(P, clips, stem):
    """Renders the built figure in each clip (one frame), from the front-left, with Workbench."""
    scene = bpy.context.scene
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.location = (-0.75, -1.9, 0.8)
    target = Vector((0, 0, 0.5))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50
    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.render.resolution_x, scene.render.resolution_y = 360, 520
    frames = {"Idle": 1, "March": 7, "Attack": 10, "Guard": 1}
    for clip in clips:
        for obj in P.values():
            if obj.animation_data:
                for t in obj.animation_data.nla_tracks:
                    t.mute = t.name != clip
        scene.frame_set(frames.get(clip, 1))
        scene.render.filepath = f"{stem}_{clip.lower()}.png"
        bpy.ops.render.render(write_still=True)
    for obj in P.values():
        if obj.animation_data:
            for t in obj.animation_data.nla_tracks:
                t.mute = False
    bpy.data.objects.remove(cam)


def main():
    paths = []
    for era in ("ancient", "medieval", "earlymodern", "industrial", "modern"):
        path = os.path.join(OUT, f"{era}_soldier.glb")
        build(era, "soldier", {}, path)
        paths.append(path)
        for i, colors in enumerate(MILITIA[era]):
            mpath = os.path.join(OUT, f"{era}_militia_{i}.glb")
            build(era, "militia", colors, mpath, preview_this=i == 0)
            if i == 0:
                paths.append(mpath)
    print("built", len(paths), "lineup figures")


main()
