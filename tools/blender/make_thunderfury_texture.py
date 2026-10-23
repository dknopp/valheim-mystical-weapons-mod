# Builds Thunderfury from thunderfury_model.py, bakes one texture for it (silver blade and base with
# deep blue faces along the slot and round the orb's hole, dark gunmetal blocks, a blue grip with a
# silver spiral, pale gold spikes, a white-blue orb) and writes models/Thunderfury.obj/.mtl/.png.
#
# The texture is computed in numpy from each texel's position, normal and colour zone (the preview
# material it came from), baked from Blender, so it is repeatable: change the shape in
# thunderfury_model.py and re-run this.
#   blender -b --factory-startup --python tools/blender/make_thunderfury_texture.py -- models 512 [preview.png]
# Arguments: output folder, texture size, optional preview render (not shipped).
import bpy, bmesh, sys, os, math
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
outdir, size = os.path.abspath(argv[0]), int(argv[1])
preview = argv[2] if len(argv) > 2 else None
name = "Thunderfury"

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import thunderfury_model as tf

bpy.ops.wm.read_factory_settings(use_empty=True)
parts = tf.build()
print(f"RESULT model {tf.triangles(parts)} triangles")

# One object, so the whole model shares one UV layout
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active

# Colour zones, one per preview material
ZONES = {"TF_Silver": 0, "TF_Gunmetal": 1, "TF_Wrap": 2, "TF_Gold": 3, "TF_Orb": 4}
mats = list(obj.data.materials)
orb_index = next(i for i, m in enumerate(mats) if m.name == "TF_Orb")

bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02, scale_to_bounds=False)
bpy.ops.uv.pack_islands(margin=0.01, rotate=True)
bpy.ops.object.mode_set(mode="OBJECT")

# --- Bake each texel's position, normal and zone (via emission) -----------------------------------
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 1
scene.cycles.device = "CPU"
bake_img = bpy.data.images.new("bake", size, size, alpha=True, float_buffer=True)

def bake(kind):
    for m in mats:
        m.use_nodes = True
        nt = m.node_tree; nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(em.outputs[0], out.inputs[0])
        if kind == "zone":
            v = (ZONES[m.name] + 1) / 10
            em.inputs["Color"].default_value = (v, v, v, 1)
        else:
            geo = nt.nodes.new("ShaderNodeNewGeometry")
            add = nt.nodes.new("ShaderNodeVectorMath"); add.operation = "ADD"
            add.inputs[1].default_value = (2.0, 2.0, 2.0)  # keep values positive; 0 means "no texel"
            nt.links.new(geo.outputs["Position" if kind == "pos" else "Normal"], add.inputs[0])
            nt.links.new(add.outputs[0], em.inputs["Color"])
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bake_img
        nt.nodes.active = tex
    bake_img.pixels.foreach_set(np.zeros(size * size * 4, np.float32))
    bpy.ops.object.bake(type="EMIT", margin=4, margin_type="EXTEND")
    px = np.empty(size * size * 4, np.float32); bake_img.pixels.foreach_get(px)
    return px.reshape(size, size, 4)[..., :3]

pos_b = bake("pos")
valid = pos_b.max(-1) > 0.5
P = pos_b - 2.0
N = bake("nrm") - 2.0
zone = np.rint(bake("zone")[..., 0] * 10 - 1).astype(int)

# --- Procedural pattern (Blender space: X across the blade, Y its thickness, Z along it) -----------
def hash3(c, seed):
    c = c.astype(np.int64)
    h = (c[..., 0] * 73856093) ^ (c[..., 1] * 19349663) ^ (c[..., 2] * 83492791) ^ (seed * 2654435761)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xFFFFFF) / float(0xFFFFFF)

def value_noise(p, seed):
    i = np.floor(p); f = p - i; f = f * f * (3 - 2 * f)
    r = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[..., 0] if dx else 1 - f[..., 0]) * (f[..., 1] if dy else 1 - f[..., 1]) * (f[..., 2] if dz else 1 - f[..., 2])
                r = r + w * hash3(i + np.array([dx, dy, dz]), seed)
    return r

def fbm(p, seed, octaves=3):
    return sum(value_noise(p * 2 ** o, seed + o) * 0.5 ** o for o in range(octaves)) / sum(0.5 ** o for o in range(octaves))

def ramp(t, stops):
    """stops: [(t, (r, g, b)), ...] in sRGB."""
    ts = [s[0] for s in stops]
    return np.stack([np.interp(t, ts, [s[1][k] for s in stops]) for k in range(3)], -1)

p = P[valid]; n = N[valid]; z_ = zone[valid]
x, y, z = p[:, 0], p[:, 1], p[:, 2]
ax = np.abs(x)
tone = (0.88 + 0.24 * fbm(p * 14, 50))[:, None]   # broad, soft variation; the game adds its grain
col = np.zeros((len(p), 3))

# Silver: blade prongs and base. Blue along the slot: from deep navy at the slot to bright blue, a pale
# rim, then silver to the outer edge (as on the Classic model).
silver = z_ == 0
rows = tf.PRONG_ROWS + [(tf.PRONG_TIP[1], tf.PRONG_TIP[0], 0, tf.PRONG_TIP[0])]
rz = [r[0] for r in rows]
w = np.interp(z, rz, [r[1] for r in rows])
g = np.interp(z, rz, [r[3] for r in rows])
on_prong = silver & (z > rz[0] - 0.005)
f = np.clip((ax - g) / np.maximum(w - g, 1e-4), 0, 1)
SILVER = [(0.0, (0.66, 0.70, 0.78)), (1.0, (0.86, 0.89, 0.94))]
BLUE = [(0.0, (0.04, 0.07, 0.26)), (0.35, (0.12, 0.30, 0.78)), (0.47, (0.62, 0.82, 1.0)), (0.52, (0.80, 0.86, 0.95))]
metal = ramp(np.clip(f + 0.3 * (fbm(p * 30, 7) - 0.5), 0, 1), SILVER)
blade_blue = ramp(f, BLUE)
is_blue = on_prong & (f < 0.5)
col[silver] = metal[silver]
col[is_blue] = blade_blue[is_blue]

# Base: a blue socket round the orb's hole, fading out into the silver plate
base = silver & ~on_prong
e = np.sqrt((x / 0.040) ** 2 + ((z - tf.ORB_Z) / 0.053) ** 2)
socket = np.clip((e - 1.0) / 0.75, 0, 1)                 # 0 at the hole's edge, 1 a bit out
sock_col = ramp(socket, [(0.0, (0.05, 0.09, 0.30)), (0.6, (0.14, 0.34, 0.85)), (0.8, (0.62, 0.82, 1.0)), (1.0, (0.80, 0.86, 0.95))])
in_sock = base & (socket < 0.95)
col[in_sock] = sock_col[in_sock]

# Gunmetal: collar and pommel blocks, a touch lighter on their flat faces than in the grooves
gun = z_ == 1
r_ = np.hypot(x, y)
col[gun] = ramp(np.clip((r_[gun] - 0.022) / 0.01, 0, 1), [(0.0, (0.12, 0.13, 0.16)), (1.0, (0.25, 0.26, 0.31))])

# Wrap: deep blue; on the grip, a silver band spiralling round it with a dark line either side
wrap = z_ == 2
turn = (z / 0.026 + np.arctan2(y, x) / (2 * math.pi)) % 1.0
band = (turn < 0.34) & (np.abs(z) < 0.1)
edge = (np.abs(turn - 0.34) < 0.04) | (turn > 0.96)
blue = np.array([0.08, 0.14, 0.42])
col[wrap] = blue
col[wrap & band] = (0.70, 0.75, 0.84)
col[wrap & edge & (np.abs(z) < 0.1)] = (0.03, 0.05, 0.14)

# Gold: deep where a spike meets its block, pale at its point
gold = z_ == 3
t_side = np.clip((r_ - 0.026) / 0.05, 0, 1)
t_end = np.clip((tf.POMMEL_CONE[0] - z) / (tf.POMMEL_CONE[0] - tf.POMMEL_CONE[2]), 0, 1)
t_gold = np.where(z < tf.POMMEL_CONE[0] + 0.002, t_end, t_side)
col[gold] = ramp(t_gold[gold], [(0.0, (0.62, 0.48, 0.26)), (0.6, (0.90, 0.80, 0.56)), (1.0, (0.98, 0.94, 0.80))])

# Orb: white-blue, whitest facing out of the blade's faces (it glows in game)
orb = z_ == 4
col[orb] = ramp(np.abs(n[orb, 1]), [(0.0, (0.55, 0.72, 1.0)), (1.0, (0.92, 0.97, 1.0))])

col[~orb] *= tone[~orb]
out = np.zeros((size, size, 4), np.float32); out[..., 3] = 1
out[..., :3] = (0.66, 0.70, 0.78)
out[valid, :3] = np.clip(col, 0, 1)
img = bpy.data.images.new(name, size, size, alpha=False)
img.pixels.foreach_set(out.ravel())
img.filepath_raw = os.path.join(outdir, name + ".png"); img.file_format = "PNG"
img.save()

# --- Export: everything on Thunderfury_0 (grain in game), the orb on Thunderfury_1 (glow) ----------
me = obj.data
idx = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get("material_index", idx)
idx = (idx == orb_index).astype(np.int32)
srgb = lambda *c: tuple(v ** 2.2 for v in c) + (1,)
new_mats = []
for i, glow in enumerate((None, srgb(0.6, 0.8, 1.0))):
    m = bpy.data.materials.new(f"{name}_{i}"); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    o = nt.nodes.new("ShaderNodeOutputMaterial"); bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs[0], o.inputs[0])
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Metallic"].default_value = 0.6 if glow is None else 0.0
    bsdf.inputs["Roughness"].default_value = 0.45
    if glow:  # as in game: glow = texture x the material's emission colour
        mul = nt.nodes.new("ShaderNodeVectorMath"); mul.operation = "MULTIPLY"
        mul.inputs[1].default_value = glow[:3]
        nt.links.new(tex.outputs["Color"], mul.inputs[0])
        nt.links.new(mul.outputs[0], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 3.0
    new_mats.append(m)
me.materials.clear()
for m in new_mats: me.materials.append(m)
me.polygons.foreach_set("material_index", idx)
me.update()

obj.select_set(True)
bpy.ops.wm.obj_export(filepath=os.path.join(outdir, name + ".obj"), export_selected_objects=True,
                      export_uv=True, export_normals=False, export_materials=True, export_triangulated_mesh=True,
                      path_mode="STRIP", forward_axis="NEGATIVE_Z", up_axis="Y")
print(f"RESULT {len(me.polygons)} faces, {valid.sum()} textured texels "
      f"({100 * valid.mean():.0f}% of the texture)")

# --- Optional preview render (not shipped) --------------------------------------------------------
if preview:
    from mathutils import Vector
    world = bpy.data.worlds.new("w"); scene.world = world
    world.use_nodes = True; world.node_tree.nodes["Background"].inputs[0].default_value = (0.03, 0.03, 0.035, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0; sun.rotation_euler = (0.9, 0.3, 0.6); scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 1.4
    scene.camera = cam
    scene.cycles.samples = 48
    scene.render.resolution_x, scene.render.resolution_y = 560, 1000
    base_path, ext = os.path.splitext(preview)
    for tag, direction, target, scale in (("front", Vector((0, 1, 0)), Vector((0, 0, 0.36)), 1.4),
                                          ("angle", Vector((-0.6, 0.75, -0.3)), Vector((0, 0, 0.36)), 1.4),
                                          ("close", Vector((-0.35, 0.94, -0.1)), Vector((0, 0, 0.30)), 0.5)):
        cam.data.ortho_scale = scale
        cam.location = target - direction.normalized() * 3
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{base_path}_{tag}{ext}"
        bpy.ops.render.render(write_still=True)
