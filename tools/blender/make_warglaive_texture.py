# Builds one Warglaive of Azzinoth from warglaive_model.py, bakes one texture for it (fel-green blades,
# pale at their sharp edges and darkening toward the middle with faint fel wisps, a silver hub with an
# engraved rim, a gold star, a brown leather wrap between silver collars) and writes
# models/WarglaivesOfAzzinoth.obj/.mtl/.png. The game mirrors it for the left hand.
#
# The texture is computed in numpy from each texel's position, normal and colour zone (the preview
# material it came from), baked from Blender, so it is repeatable: change the shape in
# warglaive_model.py and re-run this.
#   blender -b --factory-startup --python tools/blender/make_warglaive_texture.py -- models 512 [preview.png]
# Arguments: output folder, texture size, optional preview render (not shipped).
import bpy, sys, os, math
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
outdir, size = os.path.abspath(argv[0]), int(argv[1])
preview = argv[2] if len(argv) > 2 else None
name = "WarglaivesOfAzzinoth"

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import warglaive_model as wg

bpy.ops.wm.read_factory_settings(use_empty=True)
wg.build()
print(f"RESULT model {sum(wg.triangles().values())} triangles")

# One object, so the whole model shares one UV layout
parts = list(bpy.data.collections[wg.COLLECTION].objects)
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active

# Colour zones, one per preview material
ZONES = {"WG_Fel": 0, "WG_Silver": 1, "WG_Gold": 2, "WG_Wrap": 3}
mats = list(obj.data.materials)
fel_index = next(i for i, m in enumerate(mats) if m.name == "WG_Fel")

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

# --- Procedural pattern (Blender space: X out of the blade's faces, Z along the grip, tips toward -Y) -
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

def outline_distance(py, pz):
    """Distance in the blade's plane from each point to the nearest edge of either blade's outline."""
    d = np.full(len(py), np.inf)
    for mirror in (1, -1):
        pts = np.array([[-v * wg.UNIT, mirror * u * wg.UNIT] for u, v in wg.BLADE_HALF])
        for a, b in zip(pts, np.roll(pts, -1, 0)):
            ab = b - a
            t = np.clip(((py - a[0]) * ab[0] + (pz - a[1]) * ab[1]) / max(ab @ ab, 1e-12), 0, 1)
            d = np.minimum(d, np.hypot(py - a[0] - t * ab[0], pz - a[1] - t * ab[1]))
    return d

p = P[valid]; n = N[valid]; z_ = zone[valid]
x, y, z = p[:, 0], p[:, 1], p[:, 2]
tone = (0.88 + 0.24 * fbm(p * 14, 50))[:, None]   # broad, soft variation
col = np.zeros((len(p), 3))

# Fel: pale yellow-green at the sharp edge, bright fel green over the bevel, deep green in the middle,
# with faint brighter wisps streaming along the blade. The game glows it by its own colour, so the
# edges glow most.
fel = z_ == 0
d = outline_distance(y[fel], z[fel])
wisp = fbm(np.stack([x[fel] * 0, y[fel] * 60, z[fel] * 18], -1), 11)
t = d / 0.022 - 0.6 * np.clip(wisp - 0.55, 0, 1)
col[fel] = ramp(np.clip(t, 0, 1), [(0.0, (0.88, 1.0, 0.55)), (0.12, (0.50, 0.98, 0.22)),
                                   (0.45, (0.22, 0.66, 0.10)), (1.0, (0.07, 0.30, 0.05))])

# Silver: hub, collars and posts. A ring of engraved notches round the hub's raised rim, and the well
# under the star a shade darker.
silver = z_ == 1
r_hub = np.hypot(y, z) / wg.UNIT
on_front = x > wg.BLADE_X + wg.BLADE_T + 0.002
col[silver] = ramp(np.clip(n[silver, 0] * 0.5 + 0.5, 0, 1), [(0.0, (0.60, 0.63, 0.70)), (1.0, (0.84, 0.86, 0.91))])
well = silver & on_front & (r_hub < 34)
col[well] = (0.42, 0.45, 0.52)
ang = np.arctan2(y, z) / (2 * math.pi) * 30 % 1.0
notch = silver & on_front & (r_hub > 37.5) & (r_hub < 44.5) & (np.abs(ang - 0.5) < 0.16)
col[notch] = (0.22, 0.24, 0.29)

# Gold: the star, deep at its base and pale toward its apex
gold = z_ == 2
h = np.clip((x - wg.BLADE_X - wg.BLADE_T) / wg.STAR_APEX, 0, 1)
col[gold] = ramp(h[gold], [(0.0, (0.55, 0.40, 0.18)), (0.5, (0.86, 0.70, 0.36)), (1.0, (0.98, 0.90, 0.62))])

# Wrap: brown leather wound round the bar, a dark gap between turns
wrap = z_ == 3
turn = (z / 0.016 + np.arctan2(y, x) / (2 * math.pi)) % 1.0
col[wrap] = ramp(np.abs(turn[wrap] - 0.5) * 2, [(0.0, (0.40, 0.26, 0.15)), (0.75, (0.30, 0.19, 0.10)), (1.0, (0.10, 0.06, 0.03))])

col[~fel] *= tone[~fel]
out = np.zeros((size, size, 4), np.float32); out[..., 3] = 1
out[..., :3] = (0.07, 0.30, 0.05)
out[valid, :3] = np.clip(col, 0, 1)
img = bpy.data.images.new(name, size, size, alpha=False)
img.pixels.foreach_set(out.ravel())
img.filepath_raw = os.path.join(outdir, name + ".png"); img.file_format = "PNG"
img.save()

# --- Export: the fel blades on WarglaivesOfAzzinoth_0 (glow), the rest on _1 -----------------------
me = obj.data
idx = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get("material_index", idx)
idx = (idx != fel_index).astype(np.int32)
srgb = lambda *c: tuple(v ** 2.2 for v in c) + (1,)
new_mats = []
for i, glow in enumerate((srgb(0.4, 1.0, 0.3), None)):
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
        bsdf.inputs["Emission Strength"].default_value = 1.5
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
    sun.data.energy = 3.0; sun.rotation_euler = (0.3, 1.2, 0.4); scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    scene.camera = cam
    scene.cycles.samples = 48
    scene.render.resolution_x, scene.render.resolution_y = 600, 1000
    base_path, ext = os.path.splitext(preview)
    for tag, direction, target, scale in (("front", Vector((-1, 0, 0)), Vector((0.08, -0.04, 0)), 1.0),
                                          ("angle", Vector((-0.75, 0.6, -0.3)), Vector((0.08, -0.04, 0)), 1.0),
                                          ("close", Vector((-0.9, 0.3, -0.2)), Vector((0.05, 0, 0)), 0.3)):
        cam.data.ortho_scale = scale
        cam.location = target - direction.normalized() * 3
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{base_path}_{tag}{ext}"
        bpy.ops.render.render(write_still=True)
