"""
Renders Politicom's own event pictures in Blender, from the game's models: scenes the period art
on Commons doesn't have, made for each age.

- celebration: a town square of the age's buildings dressed for the anniversary of the rising or
  for liberation: a crowd of militia and townsfolk, black-and-red flags, bunting and lanterns,
  long tables, in warm evening light.
- mourning: a funeral procession through the street, the coffin draped in the black-and-red flag,
  people lining the way, under a grey sky.
- barricades: a street barricade of paving stones, carts and barrels, militia firing over it
  (from the age of muskets on).

Each is rendered with EEVEE, then toned like the rest of the event art (process_event_art.py's
sepia duotone, grain and vignette) and written to Game/art/events/<theme>_<age>.jpg, with a
credit in Data/events/art_credits.json.

Usage:
  blender --background --factory-startup --python tools/art/render_event_art.py -- [--only NAME]
  (then the toning runs in Python with Pillow and numpy; see main())
"""
import math
import os
import random
import sys

import bpy
from mathutils import Vector

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ONLY = ARGS[ARGS.index("--only") + 1] if "--only" in ARGS else None
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
MODELS = os.path.join(ROOT, "Game", "models")
RAW = os.path.join(HERE, ".renders")

# Age names: the event art's, the buildings', the figures'.
AGES = [("ancient", "ancient", "ancient"), ("medieval", "medieval", "medieval"), ("earlymodern", "early", "earlymodern"),
        ("industrial", "industrial", "industrial"), ("modern", "modern", "modern")]
WALLS = [(0.86, 0.79, 0.64), (0.84, 0.76, 0.6), (0.8, 0.72, 0.58), (0.78, 0.7, 0.6)]
ROOFS = [(0.58, 0.28, 0.2), (0.42, 0.34, 0.28), (0.62, 0.32, 0.22), (0.3, 0.32, 0.36)]
BLACK, RED = (0.04, 0.04, 0.04), (0.62, 0.08, 0.06)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 2400, 1000
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    return scene


def material(name, color, emission=0.0, rough=0.8):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    if emission:
        b.inputs["Emission Color"].default_value = (*color, 1)
        b.inputs["Emission Strength"].default_value = emission
    return m


def tint_imported(objs, wall=None, roof=None):
    """The game's models carry their colours in vertex colours ("Col"/"Color"): multiply them in,
    and give Wall and Roof the town's colours."""
    for o in objs:
        if o.type != "MESH":
            continue
        layer = o.data.color_attributes[0].name if o.data.color_attributes else None
        for slot in o.material_slots:
            m = slot.material
            if m is None or not m.use_nodes:
                continue
            nt = m.node_tree
            bsdf = nt.nodes.get("Principled BSDF")
            if bsdf is None:
                continue
            base = m.name.split(".")[0]
            own = tuple(bsdf.inputs["Base Color"].default_value[:3])
            # The importer puts a material's own colour in a Mix node feeding Base Color (factor x vertex colour).
            for link in bsdf.inputs["Base Color"].links:
                if link.from_node.bl_idname == "ShaderNodeMix":
                    colors = [i.default_value[:3] for i in link.from_node.inputs if i.type == "RGBA" and not i.is_linked]
                    if colors:
                        own = tuple(colors[-1])
            color = wall if base == "Wall" and wall else roof if base == "Roof" and roof else own
            for link in list(bsdf.inputs["Base Color"].links):
                nt.links.remove(link)
            if layer:
                attr = nt.nodes.new("ShaderNodeVertexColor")
                attr.layer_name = layer
                mix = nt.nodes.new("ShaderNodeMix")
                mix.data_type = "RGBA"
                mix.blend_type = "MULTIPLY"
                mix.inputs[0].default_value = 1.0
                mix.inputs[6].default_value = (*color, 1)
                nt.links.new(attr.outputs["Color"], mix.inputs[7])
                nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
            else:
                bsdf.inputs["Base Color"].default_value = (*color, 1)
            bsdf.inputs["Roughness"].default_value = 0.85


def place(path, at, scale=1.0, turn=0.0, wall=None, roof=None, pose=None, frame=6):
    """Imports a model, puts it at `at`, turned `turn` radians about Z; returns its root."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    roots = [o for o in new if o.parent is None]
    holder = bpy.data.objects.new("holder", None)
    bpy.context.scene.collection.objects.link(holder)
    for r in roots:
        r.parent = holder
    holder.location = at
    holder.scale = (scale, scale, scale)
    holder.rotation_euler = (0, 0, turn)
    tint_imported(new, wall, roof)
    if pose:
        for o in new:
            if o.type == "ARMATURE" and o.animation_data:
                act = bpy.data.actions.get(pose) or next((a for a in bpy.data.actions if a.name.startswith(pose)), None)
                for t in o.animation_data.nla_tracks:
                    t.mute = not t.name.startswith(pose)
                if act:
                    o.animation_data.action = act
        holder["frame"] = frame
    return holder


def ground(color, size=80):
    bpy.ops.mesh.primitive_plane_add(size=size)
    g = bpy.context.object
    m = material("ground", color, rough=1)
    nt = m.node_tree
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 40
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*[c * 0.7 for c in color], 1)
    ramp.color_ramp.elements[1].color = (*[min(1, c * 1.15) for c in color], 1)
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    g.data.materials.append(m)


def flag(at, height=4.2, size=(1.6, 1.05), lean=0.0):
    """A pole and a black-and-red flag (split on the diagonal), rippling."""
    bpy.ops.mesh.primitive_cylinder_add(radius=0.04, depth=height, location=(at[0], at[1], height / 2))
    bpy.context.object.data.materials.append(material("pole", (0.25, 0.18, 0.12)))
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=16, y_subdivisions=8, size=1)
    f = bpy.context.object
    f.scale = (size[0], size[1], 1)
    f.rotation_euler = (math.pi / 2, 0, lean)
    f.location = (at[0] + size[0] / 2 * math.cos(lean), at[1] + size[0] / 2 * math.sin(lean), height - size[1] / 2 - 0.05)
    # The anarchist flag: black over red, split cleanly on the diagonal (in the material, from the UVs).
    m = bpy.data.materials.new("flag")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    uv = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    diff = nt.nodes.new("ShaderNodeMath")
    diff.operation = "GREATER_THAN"
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.inputs[6].default_value = (*RED, 1)
    mix.inputs[7].default_value = (*BLACK, 1)
    nt.links.new(uv.outputs["UV"], sep.inputs[0])
    nt.links.new(sep.outputs["Y"], diff.inputs[0])
    nt.links.new(sep.outputs["X"], diff.inputs[1])
    nt.links.new(diff.outputs[0], mix.inputs[0])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.9
    f.data.materials.append(m)
    for v in f.data.vertices:
        u = v.co.x + 0.5
        v.co.z = 0.12 * u * math.sin(u * 7 + v.co.y * 2)


def bunting(a, b, sag=0.6, n=16, colors=(BLACK, RED)):
    """A line of pennants between two points, sagging."""
    a, b = Vector(a), Vector(b)
    mats = [material(f"pennant{i}", c) for i, c in enumerate(colors)]
    for k in range(n):
        t = (k + 0.5) / n
        p = a.lerp(b, t) - Vector((0, 0, sag * math.sin(math.pi * t)))
        bpy.ops.mesh.primitive_cone_add(vertices=3, radius1=0.16, depth=0.01, location=p - Vector((0, 0, 0.14)))
        c = bpy.context.object
        c.rotation_euler = (math.pi / 2, 0, math.atan2(b.y - a.y, b.x - a.x))
        c.scale = (1, 1.6, 1)
        c.data.materials.append(mats[k % len(mats)])


def lanterns(a, b, sag=0.5, n=12):
    a, b = Vector(a), Vector(b)
    glow = material("lantern", (1.0, 0.72, 0.36), emission=5)
    for k in range(n):
        t = (k + 0.5) / n
        p = a.lerp(b, t) - Vector((0, 0, sag * math.sin(math.pi * t)))
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.06, location=p, segments=10, ring_count=6)
        bpy.context.object.data.materials.append(glow)
        light = bpy.data.lights.new("lantern", "POINT")
        light.energy = 25
        light.use_shadow = False
        light.color = (1, 0.75, 0.45)
        lo = bpy.data.objects.new("lantern_light", light)
        lo.location = p
        bpy.context.scene.collection.objects.link(lo)


def table(at, length=6, turn=0.0):
    cloth = material("cloth", (0.86, 0.82, 0.74))
    wood = material("wood", (0.36, 0.24, 0.14))
    bpy.ops.mesh.primitive_cube_add(location=(at[0], at[1], 0.75))
    t = bpy.context.object
    t.scale = (length / 2, 0.45, 0.03)
    t.rotation_euler = (0, 0, turn)
    t.data.materials.append(cloth)
    for s in (-1, 1):
        bpy.ops.mesh.primitive_cube_add(location=(at[0] + s * (length / 2 - 0.2) * math.cos(turn), at[1] + s * (length / 2 - 0.2) * math.sin(turn), 0.37))
        leg = bpy.context.object
        leg.scale = (0.05, 0.4, 0.37)
        leg.rotation_euler = (0, 0, turn)
        leg.data.materials.append(wood)
    for k in range(int(length / 0.7)):  # bottles and bread
        x = -length / 2 + 0.4 + k * 0.7
        p = (at[0] + x * math.cos(turn), at[1] + x * math.sin(turn), 0.84)
        bpy.ops.mesh.primitive_cylinder_add(radius=0.05, depth=0.2, location=p)
        bpy.context.object.data.materials.append(material("bottle", (0.12, 0.22, 0.12), rough=0.3))


def sky(top, horizon, strength=1.0):
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes["Background"]
    grad = nt.nodes.new("ShaderNodeTexGradient")
    coord = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    nt.links.new(coord.outputs["Generated"], sep.inputs[0])
    nt.links.new(sep.outputs["Z"], ramp.inputs["Fac"])
    ramp.color_ramp.elements[0].color = (*horizon, 1)
    ramp.color_ramp.elements[1].color = (*top, 1)
    nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = strength
    bpy.context.scene.world = world


def sun(energy, color, elevation, azimuth):
    s = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    s.data.energy = energy
    s.data.color = color
    s.data.angle = math.radians(4)
    s.rotation_euler = (math.radians(90 - elevation), 0, math.radians(azimuth))
    bpy.context.scene.collection.objects.link(s)


def camera(at, target, lens=32):
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    bpy.context.scene.collection.objects.link(cam)
    cam.location = at
    cam.rotation_euler = (Vector(target) - Vector(at)).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    bpy.context.scene.camera = cam


def building(build_age, i, at, turn, scale=4.6, landmark=False):
    name = f"landmark_{build_age}" if landmark else f"house_{build_age}_{i % 4}"
    return place(os.path.join(MODELS, "buildings", name + ".glb"), at, scale * (0.55 if landmark else 1), turn,
                 wall=WALLS[i % len(WALLS)], roof=ROOFS[i % len(ROOFS)])


def figure(fig_age, militia, i, at, turn, pose="Idle", frame=6):
    name = f"{fig_age}_{'militia' if militia else 'soldier'}_{i % 3}"
    return place(os.path.join(MODELS, "figures", name + ".glb"), at, 1.75, turn, pose=pose, frame=frame)


def square(build_age, seed):
    """Buildings round three sides of a square, a landmark at the back."""
    rng = random.Random(seed)
    building(build_age, 0, (0, 16, 0), 0, landmark=True)
    for k, x in enumerate((-13, -8.5, 8.5, 13)):
        building(build_age, k + 1, (x, 14 + rng.uniform(-0.5, 0.5), 0), 0)
    for k, y in enumerate((9, 3, -3)):
        building(build_age, k + 5, (-15, y, 0), math.pi / 2)
        building(build_age, k + 8, (15, y, 0), -math.pi / 2)


def celebration(art_age, build_age, fig_age, seed=1):
    rng = random.Random(seed)
    reset()
    ground((0.48, 0.42, 0.34))
    square(build_age, seed)
    # The crowd: people in knots of three to six, turned toward each other, talking and drinking.
    k = 0
    for g in range(9):
        cx, cy = rng.uniform(-10, 10), rng.uniform(-2, 11)
        for j in range(rng.randint(3, 6)):
            a = j * math.tau / 5 + rng.uniform(-0.3, 0.3)
            at = (cx + math.cos(a) * 0.9, cy + math.sin(a) * 0.9, 0)
            figure(fig_age, True, k, at, math.atan2(cy - at[1], cx - at[0]) + math.pi / 2, pose="Idle", frame=rng.randint(1, 20))
            k += 1
    for at in ((-9, 7.5), (9, 7.5), (-4, 12.5), (4, 12.5), (0, 3)):
        flag(at, lean=rng.uniform(-0.3, 0.3))
    bunting((-13, 12, 6.5), (13, 12, 6.5), sag=1.0, n=22)
    bunting((-13.5, 9, 5.5), (-4, 12.5, 4.2), sag=0.5, n=10)
    bunting((13.5, 9, 5.5), (4, 12.5, 4.2), sag=0.5, n=10)
    lanterns((-14, 2, 4.6), (14, 2, 4.6), sag=0.8, n=18)
    lanterns((-14, 6, 5.2), (14, 6, 5.2), sag=0.9, n=18)
    table((-5, -4.5), 7, 0.1)
    table((6, -3.5), 6, -0.2)
    sky((0.42, 0.46, 0.62), (0.98, 0.7, 0.45), 0.9)
    sun(3.2, (1.0, 0.72, 0.48), 12, 200)
    camera((0, -16, 3.2), (0, 6, 3.4), lens=30)


def mourning(art_age, build_age, fig_age, seed=2):
    rng = random.Random(seed)
    reset()
    ground((0.42, 0.4, 0.37))
    for k, y in enumerate((-6, 0, 6, 12, 18, 24)):  # a street, both sides
        building(build_age, k, (-8, y, 0), math.pi / 2)
        building(build_age, k + 3, (8, y, 0), -math.pi / 2)
    # The coffin under the flag, carried on shoulders.
    coffin = material("coffin", (0.18, 0.12, 0.08))
    bpy.ops.mesh.primitive_cube_add(location=(0, 2, 1.75))
    c = bpy.context.object
    c.scale = (0.45, 1.1, 0.25)
    c.data.materials.append(coffin)
    for s, col in ((1, BLACK), (-1, RED)):
        bpy.ops.mesh.primitive_cube_add(location=(s * 0.18, 2, 2.02))
        d = bpy.context.object
        d.scale = (0.28, 1.15, 0.03)
        d.data.materials.append(material("drape", col))
    for side in (-1, 1):  # bearers
        for y in (1.3, 2.7):
            figure(fig_age, True, int(y * 3) + side, (side * 0.75, y, 0), math.pi, pose="March", frame=4)
    for k in range(16):  # the procession behind
        figure(fig_age, True, k, ((k % 4) * 1.1 - 1.65, 4.5 + (k // 4) * 1.3, 0), math.pi, pose="March", frame=rng.randint(1, 20))
    for k in range(24):  # people lining the street
        side = -1 if k % 2 == 0 else 1
        figure(fig_age, True, k + 7, (side * rng.uniform(3.6, 5.2), rng.uniform(-6, 16), 0), side * math.pi / 2 + rng.uniform(-0.3, 0.3), pose="Idle", frame=rng.randint(1, 20))
    flag((-1.2, 7.5), height=4.6)
    flag((1.4, 8.5), height=4.6, lean=0.3)
    sky((0.5, 0.52, 0.55), (0.72, 0.72, 0.7), 0.8)
    sun(1.6, (0.85, 0.87, 0.9), 35, 160)
    camera((1.5, -12, 2.6), (0, 8, 1.8), lens=38)


def barricades(art_age, build_age, fig_age, seed=3):
    rng = random.Random(seed)
    reset()
    ground((0.44, 0.41, 0.37))
    for k, y in enumerate((-4, 3, 10, 17, 24)):
        building(build_age, k, (-7.5, y, 0), math.pi / 2)
        building(build_age, k + 3, (7.5, y, 0), -math.pi / 2)
    # Paving stones, barrels, carts and furniture across the street (in 1588 Paris built them of barrels).
    for k in range(6):
        place(os.path.join(MODELS, "works", "wall_industrial.glb"), (-5.25 + k * 2.1, 4, 0), 2.2, math.pi + rng.uniform(-0.08, 0.08))
    # The militia behind it, firing over it toward the viewer (the enemy's side); some up on the stones.
    for k in range(10):
        up = k % 3 == 0
        figure(fig_age, True, k, (rng.uniform(-5, 5), rng.uniform(4.6, 5.2), 0.5 if up else 0), math.pi, pose="Attack", frame=rng.randint(6, 12))
    for k in range(6):  # others behind, bringing stones and ammunition
        figure(fig_age, True, k + 4, (rng.uniform(-5, 5), rng.uniform(7, 10), 0), math.pi + rng.uniform(-0.5, 0.5), pose="March", frame=rng.randint(1, 20))
    flag((1.5, 4.6), height=5.2, lean=0.2)
    sky((0.48, 0.5, 0.55), (0.82, 0.74, 0.62), 0.9)
    sun(2.6, (1.0, 0.88, 0.72), 28, 210)
    camera((-1.8, -6.5, 2.0), (0.4, 6, 1.7), lens=30)


SCENES = {"celebration": (celebration, AGES), "mourning": (mourning, AGES), "barricades": (barricades, AGES[2:])}


def main():
    os.makedirs(RAW, exist_ok=True)
    for theme, (fn, ages) in SCENES.items():
        for art_age, build_age, fig_age in ages:
            name = f"{theme}_{art_age}"
            if ONLY and not name.startswith(ONLY):
                continue
            fn(art_age, build_age, fig_age)
            scene = bpy.context.scene
            # Show each figure in its pose: one frame for all (their clips loop).
            scene.frame_set(6)
            scene.render.filepath = os.path.join(RAW, name + ".png")
            bpy.ops.render.render(write_still=True)
            print("rendered", name)


main()
