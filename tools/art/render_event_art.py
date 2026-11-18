"""
Renders Politicom's own event pictures in Blender: scenes the period art on Commons doesn't have,
made for each age.

- celebration: a town square of the age dressed for the anniversary of the rising or for
  liberation, at dusk: a ring dance hand in hand in the middle, people cheering and talking, long
  tables with bottles, jugs and baskets, black-and-red flags, bunting and lanterns, lit windows.
- mourning: a funeral procession through the street, the coffin draped in the black-and-red flag,
  people lining the way, under an overcast sky.
- barricades: a street barricade of paving stones, barrels, crates and furniture, militia firing
  over it (from the age of muskets on).

The town is built here, detailed, not taken from the game: houses of the age with plinths,
string courses and cornices, framed windows with sills, mullions, shutters and balconies, doors,
half-timbering in the middle ages, tiled or slated roofs with chimneys, all in Poly Haven's CC0
photographic textures (plaster, stucco, brick, stone, roof tiles, slates, cobbles, wood). The props
are Poly Haven's CC0 models (barrels, crates, tables, chairs, benches, baskets, bottles, jugs,
lamps) and the skies its CC0 HDRIs; tools/art/fetch_assets.py downloads them. The people are the
game's figures (tools/models/build_soldiers.py), smoothed with subdivision and posed (Dance,
Cheer, Idle, March, Attack). Rendered with Cycles, then toned like the rest of the event art
(tone_event_art.py) and written to Game/art/events/<theme>_<age>.jpg.

Usage:
  python3 tools/art/fetch_assets.py
  blender --background --factory-startup --python tools/art/render_event_art.py -- [--only NAME] [--samples N]
  tools/art/tone_event_art.py
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ONLY = ARGS[ARGS.index("--only") + 1] if "--only" in ARGS else None
SAMPLES = int(ARGS[ARGS.index("--samples") + 1]) if "--samples" in ARGS else 160
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
MODELS = os.path.join(ROOT, "Game", "models")
ASSETS = os.path.join(HERE, ".assets")
RAW = os.path.join(HERE, ".renders")

# Age names: the event art's and the figures'.
AGES = [("ancient", "ancient"), ("medieval", "medieval"), ("earlymodern", "earlymodern"), ("industrial", "industrial"), ("modern", "modern")]
BLACK, RED = (0.03, 0.03, 0.03), (0.55, 0.05, 0.04)

# How each age builds: wall finishes, roofs, paving, storeys, roof pitch and the details.
STYLES = {
    "ancient": dict(walls=["white_plaster_rough_01", "clay_plaster", "large_sandstone_blocks"], roofs=["clay_roof_tiles"],
                    pave="red_sandstone_pavement", plinth="large_sandstone_blocks", floors=(1, 2), pitch=0.28, window=(0.7, 1.0),
                    glass=False, shutters=True, balcony=0, timber=False, cornice=True, courses=False, shops=False, lamps=False),
    "medieval": dict(walls=["white_plaster_rough_01", "plastered_stone_wall", "old_stone_wall", "clay_plaster"], roofs=["roof_slates_02", "clay_roof_tiles_02"],
                     pave="cobblestone_floor_04", plinth="old_stone_wall", floors=(2, 3), pitch=1.0, window=(0.8, 1.2),
                     glass=False, shutters=True, balcony=0, timber=True, cornice=False, courses=False, shops=False, lamps=False),
    "earlymodern": dict(walls=["yellow_plaster", "worn_plaster_wall", "white_stucco", "clay_plaster"], roofs=["clay_roof_tiles_02", "clay_roof_tiles"],
                        pave="cobblestone_square", plinth="large_sandstone_blocks", floors=(3, 4), pitch=0.5, window=(1.0, 1.8),
                        glass=True, shutters=True, balcony=0.4, timber=False, cornice=True, courses=True, shops=False, lamps=False),
    "industrial": dict(walls=["red_brick_plaster_patch_02", "brick_wall_005", "grey_plaster", "yellow_plaster", "worn_plaster_wall"], roofs=["roof_slates_03", "roof_slates_02"],
                       pave="square_cobblestone", plinth="large_sandstone_blocks", floors=(4, 5), pitch=0.55, window=(1.0, 1.9),
                       glass=True, shutters=True, balcony=0.6, timber=False, cornice=True, courses=True, shops=True, lamps=True),
    "modern": dict(walls=["painted_plaster_wall", "yellow_plaster_02", "worn_plaster_wall", "brick_wall_005", "white_stucco"], roofs=["red_slate_roof_tiles_01", "clay_roof_tiles_02"],
                   pave="cobblestone_pavement", plinth="large_sandstone_blocks", floors=(4, 6), pitch=0.35, window=(1.1, 2.0),
                   glass=True, shutters=True, balcony=0.7, timber=False, cornice=True, courses=True, shops=True, lamps=True),
}
# Plaster and shutter colours, to vary the houses.
TINTS = [(1, 1, 1), (1, 0.93, 0.8), (1, 0.86, 0.72), (0.92, 0.95, 1), (1, 0.9, 0.86), (0.95, 0.9, 0.78)]
SHUTTERS = [(0.35, 0.5, 0.38), (0.32, 0.42, 0.55), (0.55, 0.42, 0.3), (0.6, 0.6, 0.56), (0.5, 0.25, 0.2)]


# ── Scene, materials ──

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "METAL"
        prefs.get_devices()
        for d in prefs.devices:
            d.use = True
        scene.cycles.device = "GPU"
    except Exception:
        pass
    scene.render.resolution_x, scene.render.resolution_y = 2400, 1000
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    _cache.clear()
    return scene


_cache = {}


def material(name, color, emission=0.0, rough=0.8, metal=0.0):
    key = ("flat", name, tuple(color), emission, rough)
    if key in _cache:
        return _cache[key]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emission:
        b.inputs["Emission Color"].default_value = (*color, 1)
        b.inputs["Emission Strength"].default_value = emission
    _cache[key] = m
    return m


def pbr(texture, scale=0.4, tint=(1, 1, 1), bump=1.0):
    """A Poly Haven texture set, box-projected on the object's own metres (`scale` repeats per metre)."""
    key = ("pbr", texture, scale, tuple(tint))
    if key in _cache:
        return _cache[key]
    folder = os.path.join(ASSETS, "textures", texture)
    m = bpy.data.materials.new(texture)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    coord = nt.nodes.new("ShaderNodeTexCoord")
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (scale, scale, scale)
    nt.links.new(coord.outputs["Object"], mapping.inputs["Vector"])

    def image(name, data):
        path = os.path.join(folder, name + ".jpg")
        if not os.path.exists(path):
            return None
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(path, check_existing=True)
        if not data:
            node.image.colorspace_settings.name = "Non-Color"
        node.projection = "BOX"
        node.projection_blend = 0.25
        nt.links.new(mapping.outputs["Vector"], node.inputs["Vector"])
        return node

    diff = image("diff", True)
    if diff:
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs[0].default_value = 1.0
        mix.inputs[7].default_value = (*tint, 1)
        nt.links.new(diff.outputs["Color"], mix.inputs[6])
        nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    rough = image("rough", False)
    if rough:
        nt.links.new(rough.outputs["Color"], bsdf.inputs["Roughness"])
    nor = image("nor_gl", False)
    if nor:
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = bump
        nt.links.new(nor.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    _cache[key] = m
    return m


def link(obj, parent=None):
    bpy.context.scene.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    return obj


def box(parent, center, size, mat, rot=(0, 0, 0)):
    """A box in metres, centred at `center` in the parent's frame."""
    mesh = bpy.data.meshes.new("box")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(mat)
    o = link(bpy.data.objects.new("box", mesh), parent)
    o.location = center
    o.rotation_euler = rot
    return o


def prism_roof(parent, width, depth, base, rise, overhang, mat):
    """A gabled roof along the house's width, ridge at the middle of its depth."""
    w, d = width / 2 + overhang, depth / 2 + overhang
    verts = [(-w, -d, base), (w, -d, base), (w, d, base), (-w, d, base), (-w, 0, base + rise), (w, 0, base + rise)]
    faces = [(0, 1, 5, 4), (2, 3, 4, 5), (0, 4, 3), (1, 2, 5), (0, 3, 2, 1)]
    mesh = bpy.data.meshes.new("roof")
    mesh.from_pydata(verts, [], faces)
    mesh.materials.append(mat)
    o = link(bpy.data.objects.new("roof", mesh), parent)
    o.location = (0, depth / 2, 0)
    sol = o.modifiers.new("thick", "SOLIDIFY")
    sol.thickness = 0.12
    return o


# ── The town ──

def house(at, turn, width, floors, style, rng, depth=10.0, dusk=False):
    """A house of the age, its front on local -Y at y=0, `width` metres wide."""
    s = STYLES[style]
    root = link(bpy.data.objects.new("house", None))
    root.location = at
    root.rotation_euler = (0, 0, turn)
    ground_h, floor_h = (3.4 if style in ("ancient", "medieval") else 4.0), (3.0 if style in ("ancient", "medieval") else 3.3)
    H = ground_h + (floors - 1) * floor_h
    wall = pbr(rng.choice(s["walls"]), 0.4, rng.choice(TINTS))
    trim = pbr("white_stucco", 0.5, (0.95, 0.92, 0.86))
    wood = pbr("dark_wooden_planks", 0.6)
    shutter = pbr("wood_shutter", 0.7, rng.choice(SHUTTERS))
    box(root, (0, depth / 2, H / 2), (width, depth, H), wall)
    box(root, (0, -0.04, 0.35), (width + 0.02, 0.1, 0.7), pbr(s["plinth"], 0.5))
    if s["cornice"]:
        box(root, (0, -0.15, H - 0.15), (width + 0.1, 0.35, 0.3), trim)
        box(root, (0, -0.25, H - 0.02), (width + 0.2, 0.5, 0.12), trim)
    if s["courses"]:
        for f in range(1, floors):
            box(root, (0, -0.06, ground_h + (f - 1) * floor_h - 0.05), (width + 0.02, 0.14, 0.16), trim)
    bays = max(2, int(width / 2.6))
    ww, wh = s["window"]
    glass = material("glass", (0.03, 0.04, 0.05), rough=0.08)
    lit = material("lit", (1.0, 0.55, 0.22), emission=2.2)
    for b in range(bays):
        x = -width / 2 + (b + 0.5) * width / bays
        for f in range(floors):
            z0 = 0 if f == 0 else ground_h + (f - 1) * floor_h
            door = f == 0 and b == bays // 2
            shop = f == 0 and s["shops"] and not door and rng.random() < 0.7
            balcony = f >= 1 and rng.random() < s["balcony"] and (f == 1 or rng.random() < 0.5)
            if door:
                w_, h_, sill = 1.3, 2.6 if style != "ancient" else 2.2, 0.0
            elif shop:
                w_, h_, sill = width / bays - 0.7, 2.8, 0.3
            elif balcony:
                w_, h_, sill = ww, wh + 0.6, 0.05
            else:
                w_, h_, sill = ww, wh, (0.9 if f > 0 else 1.0)
            zc = z0 + sill + h_ / 2
            # The opening: dark glass or a dark void, a window lit at dusk, a door of planks.
            is_lit = dusk and not door and rng.random() < 0.3
            pane = lit if is_lit else (pbr("rough_pine_door", 0.6) if door else glass if s["glass"] else material("void", (0.02, 0.018, 0.016)))
            box(root, (x, -0.01, zc), (w_, 0.04, h_), pane)
            # Frame and sill.
            t = 0.12
            fm = trim if style not in ("medieval", "ancient") else wood
            box(root, (x, -0.06, zc + h_ / 2 + t / 2), (w_ + 2 * t, 0.14, t), fm)
            for sx in (-1, 1):
                box(root, (x + sx * (w_ / 2 + t / 2), -0.06, zc), (t, 0.14, h_), fm)
            if not door and not shop:
                box(root, (x, -0.12, zc - h_ / 2 - 0.04), (w_ + 0.3, 0.26, 0.08), fm)
            if s["glass"] and not door and not is_lit:  # mullions
                box(root, (x, -0.035, zc), (0.05, 0.03, h_), material("mullion", (0.85, 0.83, 0.78)))
                box(root, (x, -0.035, zc + h_ * 0.18), (w_, 0.03, 0.05), material("mullion", (0.85, 0.83, 0.78)))
            if s["courses"] and f > 0 and not balcony and rng.random() < 0.6:  # a pediment over the window
                box(root, (x, -0.1, zc + h_ / 2 + t + 0.08), (w_ + 0.5, 0.22, 0.12), trim)
            if s["shutters"] and not door and not shop:
                for sx in (-1, 1):
                    swing = rng.uniform(0.2, 1.2)
                    box(root, (x + sx * (w_ / 2 + t + w_ / 4 * math.cos(swing)), -0.06 - w_ / 4 * math.sin(swing), zc),
                        (w_ / 2, 0.04, h_), shutter, rot=(0, 0, -sx * swing))
            if shop:  # an awning over the shop front
                box(root, (x, -0.6, zc + h_ / 2 + 0.3), (w_ + 0.4, 1.2, 0.05), material("awning", rng.choice([(0.5, 0.15, 0.1), (0.2, 0.3, 0.2), (0.55, 0.5, 0.4)])),
                    rot=(-0.3, 0, 0))
            if balcony:
                slab_z = z0 + 0.05
                box(root, (x, -0.45, slab_z), (w_ + 1.0, 0.9, 0.14), trim)
                iron = material("iron", (0.05, 0.05, 0.05), rough=0.5, metal=0.6)
                box(root, (x, -0.88, slab_z + 1.0), (w_ + 1.0, 0.04, 0.04), iron)
                for k in range(int((w_ + 1.0) / 0.14) + 1):
                    box(root, (x - (w_ + 1.0) / 2 + k * 0.14, -0.88, slab_z + 0.5), (0.02, 0.02, 1.0), iron)
                for sx in (-1, 1):
                    box(root, (x + sx * (w_ + 1.0) / 2, -0.45, slab_z + 1.0), (0.04, 0.9, 0.04), iron)
    if s["timber"]:  # half-timbering: posts, rails and braces in dark oak
        beam = pbr("dark_wooden_planks", 0.8, (0.6, 0.5, 0.4))
        for f in range(floors + 1):
            z = 0.7 if f == 0 else ground_h + (f - 1) * floor_h
            box(root, (0, -0.07, z), (width, 0.14, 0.2), beam)
        for b in range(bays + 1):
            box(root, (-width / 2 + b * width / bays, -0.07, H / 2 + 0.35), (0.2, 0.14, H - 0.7), beam)
        for f in range(1, floors):
            z = ground_h + (f - 1) * floor_h + floor_h / 2
            for b in range(bays):
                x = -width / 2 + (b + 0.08) * width / bays
                box(root, (x, -0.075, z - 0.4), (0.14, 0.14, 1.3), beam, rot=(0, 0.7, 0))
    # The roof, its eaves and chimneys.
    roof_mat = pbr(rng.choice(s["roofs"]), 0.5, rng.choice([(1, 1, 1), (0.9, 0.85, 0.82), (1, 0.92, 0.85)]))
    prism_roof(root, width, depth, H, depth / 2 * s["pitch"], 0.5 if style != "ancient" else 0.7, roof_mat)
    brick = pbr("brick_wall_005", 0.6)
    for k in range(rng.randint(1, 3)):
        cx = rng.uniform(-width / 2 + 0.8, width / 2 - 0.8)
        box(root, (cx, depth * rng.uniform(0.3, 0.7), H + depth / 2 * s["pitch"] * 0.8), (0.7, 0.5, 2.2), brick)
    return root


def row(style, points, along, fixed, turn, rng, dusk, floors=None):
    """A row of houses side by side, each as wide as the gap to the next."""
    pts = sorted(points)
    for k in range(len(pts) - 1):
        a, b = pts[k], pts[k + 1]
        mid, width = (a + b) / 2, b - a - 0.05
        f = rng.randint(*STYLES[style]["floors"]) if floors is None else floors
        at = (mid, fixed, 0) if along == "x" else (fixed, mid, 0)
        house(at, turn, width, f, style, rng, dusk=dusk)


def ground(texture, size=120, scale=0.25):
    mesh = bpy.data.meshes.new("ground")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=size / 2)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(pbr(texture, scale, bump=1.4))
    link(bpy.data.objects.new("ground", mesh))


def sky_hdri(name, strength=1.0, rotate=0.0):
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes["Background"]
    env = nt.nodes.new("ShaderNodeTexEnvironment")
    env.image = bpy.data.images.load(os.path.join(ASSETS, "hdri", name + ".hdr"))
    coord = nt.nodes.new("ShaderNodeTexCoord")
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.inputs["Rotation"].default_value = (0, 0, rotate)
    nt.links.new(coord.outputs["Generated"], mapping.inputs["Vector"])
    nt.links.new(mapping.outputs["Vector"], env.inputs["Vector"])
    nt.links.new(env.outputs["Color"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = strength
    bpy.context.scene.world = world


def sun(energy, color, elevation, azimuth, angle=3):
    s = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    s.data.energy = energy
    s.data.color = color
    s.data.angle = math.radians(angle)
    s.rotation_euler = (math.radians(90 - elevation), 0, math.radians(azimuth))
    link(s)


def camera(at, target, lens=32):
    cam = link(bpy.data.objects.new("cam", bpy.data.cameras.new("cam")))
    cam.location = at
    cam.rotation_euler = (Vector(target) - Vector(at)).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    cam.data.dof.use_dof = True
    cam.data.dof.focus_distance = (Vector(target) - Vector(at)).length * 0.6
    cam.data.dof.aperture_fstop = 5.6
    bpy.context.scene.camera = cam


# ── Imported things: Poly Haven props, the game's figures ──

_props = {}


def prop(name, at, turn=0.0, scale=1.0):
    """A Poly Haven model, imported once and then instanced."""
    if name not in _props:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=os.path.join(ASSETS, "models", name, name + ".gltf"))
        new = [o for o in bpy.data.objects if o not in before]
        coll = bpy.data.collections.new("prop_" + name)
        for o in new:
            for c in list(o.users_collection):
                c.objects.unlink(o)
            coll.objects.link(o)
        _props[name] = coll
    e = link(bpy.data.objects.new(name, None))
    e.instance_type = "COLLECTION"
    e.instance_collection = _props[name]
    e.location = at
    e.rotation_euler = (0, 0, turn)
    e.scale = (scale, scale, scale)
    return e


def tint_imported(objs):
    """The game's figures carry their colours in vertex colours: multiply them into each material's own."""
    for o in objs:
        if o.type != "MESH":
            continue
        layer = o.data.color_attributes[0].name if o.data.color_attributes else None
        for slot in o.material_slots:
            m = slot.material
            if m is None or not m.use_nodes or m.get("tinted"):
                continue
            m["tinted"] = True
            nt = m.node_tree
            bsdf = nt.nodes.get("Principled BSDF")
            if bsdf is None:
                continue
            own = tuple(bsdf.inputs["Base Color"].default_value[:3])
            for l in bsdf.inputs["Base Color"].links:
                if l.from_node.bl_idname == "ShaderNodeMix":
                    colors = [i.default_value[:3] for i in l.from_node.inputs if i.type == "RGBA" and not i.is_linked]
                    if colors:
                        own = tuple(colors[-1])
            for l in list(bsdf.inputs["Base Color"].links):
                nt.links.remove(l)
            if layer:
                attr = nt.nodes.new("ShaderNodeVertexColor")
                attr.layer_name = layer
                mix = nt.nodes.new("ShaderNodeMix")
                mix.data_type = "RGBA"
                mix.blend_type = "MULTIPLY"
                mix.inputs[0].default_value = 1.0
                mix.inputs[6].default_value = (*own, 1)
                nt.links.new(attr.outputs["Color"], mix.inputs[7])
                nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
            else:
                bsdf.inputs["Base Color"].default_value = (*own, 1)
            bsdf.inputs["Roughness"].default_value = 0.85
            bsdf.inputs["Sheen Weight"].default_value = 0.3  # cloth


FRAME = 30  # the scene frame every figure is shown at (each clip's strip is shifted to vary the phase)


def figure(fig_age, i, at, turn, pose="Idle", phase=0, scale=1.75):
    """One of the game's figures, smoothed, in `pose`, `phase` frames into its clip."""
    path = os.path.join(MODELS, "figures", f"{fig_age}_militia_{i % 3}.glb")
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    holder = link(bpy.data.objects.new("figure", None))
    for o in new:
        if o.parent is None:
            o.parent = holder
        if o.type == "MESH":
            sub = o.modifiers.new("smooth", "SUBSURF")
            sub.levels = sub.render_levels = 1
        if o.type == "ARMATURE" and o.animation_data:
            o.animation_data.action = None
            for t in o.animation_data.nla_tracks:
                t.mute = t.name != pose
                if t.name == pose:
                    for strip in t.strips:
                        strip.frame_start_ui = FRAME - 1 - phase
    holder.location = at
    holder.scale = (scale, scale, scale)
    holder.rotation_euler = (0, 0, turn)
    tint_imported(new)
    return holder


# ── Festive dressing ──

def flag_material():
    if "flag" in _cache:
        return _cache["flag"]
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
    bsdf.inputs["Sheen Weight"].default_value = 0.5
    _cache["flag"] = m
    return m


def flag(at, height=4.2, size=(1.6, 1.05), lean=0.0, base=0.0):
    """A pole and the black-and-red flag, split on the diagonal, in folds."""
    box(None, (at[0], at[1], base + height / 2), (0.07, 0.07, height), pbr("dark_wooden_planks", 1.0))
    mesh = bpy.data.meshes.new("flag")
    bm = bmesh.new()
    bm.loops.layers.uv.new("UVMap")
    bmesh.ops.create_grid(bm, x_segments=24, y_segments=12, size=0.5, calc_uvs=True)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(flag_material())
    f = link(bpy.data.objects.new("flag", mesh))
    f.scale = (size[0], size[1], 1)
    f.rotation_euler = (math.pi / 2, 0, lean)
    f.location = (at[0] + size[0] / 2 * math.cos(lean), at[1] + size[0] / 2 * math.sin(lean), base + height - size[1] / 2 - 0.05)
    for v in mesh.vertices:
        u = v.co.x + 0.5
        v.co.z = 0.14 * u * math.sin(u * 8 + v.co.y * 2.5) + 0.04 * math.sin(v.co.y * 9)
    for p in mesh.polygons:
        p.use_smooth = True


def bunting(a, b, sag=0.6, n=16, colors=(BLACK, RED)):
    a, b = Vector(a), Vector(b)
    string = material("string", (0.6, 0.55, 0.45))
    mats = [material(f"pennant{i}", c, rough=0.9) for i, c in enumerate(colors)]
    prev = None
    for k in range(n + 1):
        t = k / n
        p = a.lerp(b, t) - Vector((0, 0, sag * math.sin(math.pi * t)))
        if prev is not None:
            d = p - prev
            o = box(None, (p + prev) / 2, (0.015, 0.015, d.length), string)
            o.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
        prev = p
        if k < n:
            q = a.lerp(b, t + 0.5 / n) - Vector((0, 0, sag * math.sin(math.pi * (t + 0.5 / n))))
            mesh = bpy.data.meshes.new("pennant")
            mesh.from_pydata([(-0.17, 0, 0), (0.17, 0, 0), (0, 0, -0.42)], [], [(0, 1, 2)])
            mesh.materials.append(mats[k % len(mats)])
            o = link(bpy.data.objects.new("pennant", mesh))
            o.location = q
            o.rotation_euler = (0, 0, math.atan2(b.y - a.y, b.x - a.x))


def lanterns(a, b, sag=0.5, n=12, energy=20):
    a, b = Vector(a), Vector(b)
    glow = material("lantern", (1.0, 0.6, 0.25), emission=5)
    paper = material("paper", (0.9, 0.3, 0.1), emission=2.5)
    for k in range(n):
        t = (k + 0.5) / n
        p = a.lerp(b, t) - Vector((0, 0, sag * math.sin(math.pi * t)))
        mesh = bpy.data.meshes.new("lantern")
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=0.13)
        bm.to_mesh(mesh)
        bm.free()
        mesh.materials.append(paper if k % 2 else glow)
        o = link(bpy.data.objects.new("lantern", mesh))
        o.location = p
        o.scale = (1, 1, 1.25)
        light = bpy.data.lights.new("lantern", "POINT")
        light.energy = energy
        light.shadow_soft_size = 0.15
        light.color = (1, 0.66, 0.36)
        lo = link(bpy.data.objects.new("lantern_light", light))
        lo.location = p - Vector((0, 0, 0.25))


def long_table(at, turn, length, rng, dusk):
    """Trestle tables end to end, chairs along them, bottles, jugs, baskets and lamps on top."""
    c, s = math.cos(turn), math.sin(turn)

    def along(d, side=0.0, z=0.0):
        return (at[0] + d * c - side * s, at[1] + d * s + side * c, z)

    n = max(1, int(length / 2.1))
    for k in range(n):
        d = -length / 2 + (k + 0.5) * length / n
        prop("painted_wooden_table", along(d), turn, 0.85)
        for side in (-0.75, 0.75):
            for dd in (-0.5, 0.5):
                if rng.random() < 0.6:
                    prop("painted_wooden_chair_01", along(d + dd + rng.uniform(-0.15, 0.15), side), turn + (0 if side > 0 else math.pi) + rng.uniform(-0.3, 0.3))
        top = 0.81
        prop("wine_bottles_01", along(d + rng.uniform(-0.4, 0.4), rng.uniform(-0.15, 0.15), top), rng.uniform(0, 6))
        if rng.random() < 0.6:
            prop("jug_01", along(d + rng.uniform(-0.5, 0.5), rng.uniform(-0.2, 0.2), top), rng.uniform(0, 6))
        if rng.random() < 0.5:
            prop("wicker_basket_01", along(d + rng.uniform(-0.5, 0.5), rng.uniform(-0.2, 0.2), top), rng.uniform(0, 6), 0.8)
        if dusk and k % 2 == 0:
            prop("vintage_oil_lamp", along(d, 0, top), 0)
            l = link(bpy.data.objects.new("lamp", bpy.data.lights.new("lamp", "POINT")))
            l.data.energy = 25
            l.data.color = (1, 0.62, 0.3)
            l.data.shadow_soft_size = 0.05
            l.location = along(d, 0, top + 0.25)


# ── The scenes ──

def square(style, rng, dusk):
    """A square closed on three sides by rows of houses, the camera on the open side."""
    row(style, [-16, -11.5, -6.5, -1.5, 4, 9, 13.5, 18], "x", 16, 0, rng, dusk)
    row(style, [-6, -0.5, 5, 10.5, 16.05], "y", -15, math.pi / 2, rng, dusk)
    row(style, [-6, -0.5, 5, 10.5, 16.05], "y", 15, -math.pi / 2, rng, dusk)


def celebration(art_age, fig_age, seed=1):
    rng = random.Random(seed)
    reset()
    style = art_age
    ground(STYLES[style]["pave"])
    square(style, rng, dusk=True)
    # The ring dance in the middle, hands joined.
    centre, radius, n = Vector((0, 5.5, 0)), 2.3, 13
    for k in range(n):
        a = k * math.tau / n
        at = centre + Vector((math.cos(a) * radius, math.sin(a) * radius, 0))
        figure(fig_age, k, at, a - math.pi / 2, pose="Dance", phase=(k * 5) % 20)  # facing in
    # Knots of people round it, cheering, talking, drinking.
    k = 0
    knots = [(-8.5, 2.5), (8, 3), (-5, 10.5), (5.5, 11), (-10, 8.5), (10.5, 9), (0, 12.8), (-3.5, -0.5), (3.8, -1),
             (-6.5, 6), (6.5, 6.5), (-11.5, 4.5), (11.5, 5), (-2.5, 14), (2.8, 14.2), (-8, 13), (8.5, 13), (-1, 1.5)]
    for cx, cy in knots:
        for j in range(rng.randint(3, 6)):
            a = j * math.tau / 4.5 + rng.uniform(-0.4, 0.4)
            at = (cx + math.cos(a) * 0.85, cy + math.sin(a) * 0.85, 0)
            face = math.atan2(cy - at[1], cx - at[0]) + math.pi / 2
            if rng.random() < 0.45:
                face = math.atan2(centre.y - at[1], centre.x - at[0]) + math.pi / 2  # watching the dance
            figure(fig_age, k, at, face, pose=rng.choice(["Cheer", "Cheer", "Idle"]), phase=rng.randint(0, 20))
            k += 1
    long_table((-6.5, -3.5), 0.12, 6.5, rng, True)
    long_table((7, -3), -0.15, 6.5, rng, True)
    for at in ((-11.5, 1), (11, 2), (-12, 11), (12, 12)):
        prop("wine_barrel_01", (*at, 0), rng.uniform(0, 6))
        prop("wine_barrel_01", (at[0] + 0.8, at[1] + 0.3, 0), rng.uniform(0, 6))
    prop("old_military_crate", (-10.2, -1.5, 0), 0.3)
    if STYLES[style]["lamps"]:
        for x in (-12.5, 12.5):
            prop("street_lamp_01", (x, -2, 0), math.pi / 2 if x < 0 else -math.pi / 2)
    for at in ((-9, 13), (9, 13), (-3, 14.5), (3, 14.5), (-12.5, 4), (12.5, 4)):
        flag(at, height=5.5, lean=rng.uniform(-0.4, 0.4))
    # From the balconies and across the square.
    bunting((-15, 9, 7.5), (15, 9, 7.5), sag=1.2, n=36)
    bunting((-15, 1, 6.8), (-2, 16, 8.0), sag=1.0, n=26)
    bunting((15, 1, 6.8), (2, 16, 8.0), sag=1.0, n=26)
    lanterns((-15, 4, 5.8), (15, 4, 5.8), sag=1.0, n=16)
    lanterns((-15, 11, 6.4), (15, 11, 6.4), sag=1.1, n=16)
    sky_hdri("belfast_sunset_puresky", 1.0)
    sun(2.2, (1.0, 0.62, 0.38), 8, 205)
    camera((1.0, -17, 2.0), (0, 6, 5.6), lens=20)


def mourning(art_age, fig_age, seed=2):
    rng = random.Random(seed)
    reset()
    style = art_age
    ground(STYLES[style]["pave"])
    ys = [-12, -6.5, -1, 4.5, 10, 15.5, 21, 27, 33]
    row(style, ys, "y", -6.5, math.pi / 2, rng, False)
    row(style, ys, "y", 6.5, -math.pi / 2, rng, False)
    row(style, [-8, -2.5, 3, 8.5], "x", 34, 0, rng, False)
    # The coffin under the flag, carried on shoulders.
    wood = pbr("dark_wooden_planks", 0.8, (0.7, 0.55, 0.45))
    box(None, (0, 2, 1.62), (0.6, 2.0, 0.42), wood)
    for sx, col in ((1, BLACK), (-1, RED)):
        o = box(None, (sx * 0.17, 2, 1.86), (0.36, 2.1, 0.04), material("drape", col, rough=0.9))
        o.rotation_euler = (0, sx * 0.08, 0)
    for side in (-1, 1):  # bearers
        for y in (1.2, 2.8):
            figure(fig_age, int(y * 3) + side, (side * 0.55, y, 0), 0, pose="March", phase=4 if side > 0 else 14)
    for k in range(20):  # the procession behind, four abreast
        figure(fig_age, k, ((k % 4) * 0.95 - 1.42 + rng.uniform(-0.1, 0.1), 4.6 + (k // 4) * 1.25, 0), 0, pose="March", phase=rng.randint(0, 20))
    for k in range(30):  # people lining the street, turned toward it
        side = -1 if k % 2 == 0 else 1
        at = (side * rng.uniform(3.6, 5.4), rng.uniform(-8, 22), 0)
        figure(fig_age, k + 7, at, -side * math.pi / 2 + rng.uniform(-0.3, 0.3), pose="Idle", phase=rng.randint(0, 20))
    flag((-1.0, 8.0), height=4.6)
    flag((1.2, 9.5), height=4.6, lean=0.3)
    sky_hdri("kloofendal_overcast_puresky", 1.0)
    camera((1.6, -11, 2.2), (0, 8, 2.0), lens=34)


def barricades(art_age, fig_age, seed=3):
    rng = random.Random(seed)
    reset()
    style = art_age
    ground(STYLES[style]["pave"])
    ys = [-9, -3.5, 2, 7.5, 13, 18.5, 24]
    row(style, ys, "y", -6.5, math.pi / 2, rng, False)
    row(style, ys, "y", 6.5, -math.pi / 2, rng, False)
    row(style, [-8, -2.5, 3, 8.5], "x", 25, 0, rng, False)
    # The barricade: paving stones torn up and heaped, barrels, crates, chairs and a table across the street.
    stones = [pbr("old_stone_wall", 1.5, t) for t in ((0.75, 0.73, 0.7), (0.6, 0.58, 0.55), (0.85, 0.8, 0.72))]
    for k in range(520):
        x = rng.uniform(-6.3, 6.3)
        y = 4 + rng.gauss(0, 0.35)
        h = 1.25 * (1 - min(1, abs(y - 4) / 1.2))
        box(None, (x, y, max(0.07, rng.uniform(0, h))), (0.24, 0.16, 0.13), rng.choice(stones), rot=(rng.uniform(-0.6, 0.6), rng.uniform(-0.6, 0.6), rng.uniform(0, 3)))
    for x in (-5.2, -1.4, 2.8, 5.5):
        prop("wooden_barrels_01", (x, 3.7, 0), rng.uniform(0, 6))
    for x in (-3.5, 0.6, 4.2):
        o = prop("old_military_crate", (x, 3.5, 1.1), rng.uniform(0, 6))
        o.rotation_euler = (rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), rng.uniform(0, 6))
    prop("wine_barrel_01", (-2.6, 3.4, 0), 0.5)
    prop("wine_barrel_01", (-2.3, 3.6, 0.85), 1.2)
    for x in (1.8, -0.5):
        c = prop("painted_wooden_chair_01", (x, 3.3, 1.3), rng.uniform(0, 6))
        c.rotation_euler = (rng.uniform(1.0, 2.0), 0, rng.uniform(0, 6))
    prop("painted_wooden_bench", (3.4, 3.5, 1.35), 0.1)
    # The militia behind it, firing over it toward the viewer; others bringing stones.
    for k in range(11):
        up = k % 3 == 0
        figure(fig_age, k, (rng.uniform(-5.5, 5.5), rng.uniform(4.7, 5.2), 0.7 if up else 0.3), rng.uniform(-0.15, 0.15), pose="Attack", phase=rng.randint(4, 10))
    for k in range(7):
        figure(fig_age, k + 4, (rng.uniform(-5, 5), rng.uniform(7, 11), 0), rng.uniform(-0.5, 0.5), pose="March", phase=rng.randint(0, 20))
    flag((1.5, 4.4), height=5.4, lean=0.2, base=0.8)
    sky_hdri("kloofendal_48d_partly_cloudy_puresky", 0.9, rotate=math.radians(40))
    sun(3.0, (1.0, 0.9, 0.78), 30, 215)
    camera((-1.6, -6.5, 1.7), (0.4, 6, 1.7), lens=30)


SCENES = {"celebration": (celebration, AGES), "mourning": (mourning, AGES), "barricades": (barricades, AGES[2:])}


def main():
    os.makedirs(RAW, exist_ok=True)
    for theme, (fn, ages) in SCENES.items():
        for art_age, fig_age in ages:
            name = f"{theme}_{art_age}"
            if ONLY and not name.startswith(ONLY):
                continue
            _props.clear()
            fn(art_age, fig_age)
            scene = bpy.context.scene
            scene.frame_set(FRAME)
            scene.render.filepath = os.path.join(RAW, name + ".png")
            bpy.ops.render.render(write_still=True)
            print("rendered", name)


main()
