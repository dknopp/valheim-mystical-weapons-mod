# Builds the Scroll of Hearth Portal from portal_scroll_model.py, bakes one texture for it (cream
# parchment browning toward its edges and darker on the inner turns, a Diablo II blue cord twisted and
# shaded by its wraps, a red wax seal with a raised rim and the rune Othala stamped in it) and writes
# models/PortalScroll.obj/.mtl/.png.
#
# The texture is computed in numpy from each texel's position and colour zone (the preview material it
# came from), baked from Blender, so it is repeatable: change the shape in portal_scroll_model.py and
# re-run this.
#   "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --factory-startup --python tools/blender/make_portal_scroll_texture.py -- models 512 [preview.png]
# Arguments: output folder, texture size, optional preview render (not shipped).
import bpy, sys, os, math
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
outdir, size = os.path.abspath(argv[0]), int(argv[1])
preview = argv[2] if len(argv) > 2 else None
name = "PortalScroll"

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import portal_scroll_model as ps

bpy.ops.wm.read_factory_settings(use_empty=True)
print(f"RESULT model {ps.build(grey=False)}")

# One object, so the whole model shares one UV layout
parts = list(bpy.data.collections[ps.COLLECTION].objects)
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active

# Colour zones, one per preview material; also the submesh each goes to (the mod's PortalScroll.Materials)
ZONES = {"PS_Parchment": 0, "PS_Wax": 1, "PS_Cord": 2}
mats = list(obj.data.materials)

bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02, scale_to_bounds=False)
bpy.ops.object.mode_set(mode="OBJECT")
# The seal's rune needs more texels than its size gives it: scale its UVs up before packing
uv = obj.data.uv_layers.active.data
wax_slot = next(i for i, m in enumerate(mats) if m.name == "PS_Wax")
for poly in obj.data.polygons:
    if poly.material_index == wax_slot:
        for li in poly.loop_indices:
            uv[li].uv = uv[li].uv * 4.0
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.pack_islands(margin=0.01, rotate=True, scale=True)
bpy.ops.object.mode_set(mode="OBJECT")

# --- Bake each texel's position and zone (via emission) -------------------------------------------
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
            nt.links.new(geo.outputs["Position"], add.inputs[0])
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
zone = np.rint(bake("zone")[..., 0] * 10 - 1).astype(int)

# --- Procedural pattern (Blender space: the roll along Z, the seal facing -Y) ---------------------
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

p = P[valid]; z_ = zone[valid]
x, y, z = p[:, 0], p[:, 1], p[:, 2]
r = np.hypot(x, y)
ang = np.arctan2(y, x)
col = np.zeros((len(p), 3))

# Parchment: cream with soft blotches, browning toward the cut ends, darker on the inner turns (deeper in
# the roll, as if in shadow and older), and a little darker along the flap's free edge
par = z_ == 0
pp = p[par]
prof = np.array([ps.profile(v) for v in z[par]])
depth = np.clip(1 - r[par] / prof, 0, 1) / (1 - ps.INNER)       # 0 on the outer turn, 1 deepest
blot = fbm(pp * 60, 11)
c = ramp(blot, [(0.0, (0.80, 0.70, 0.50)), (0.5, (0.90, 0.82, 0.64)), (1.0, (0.95, 0.89, 0.73))])
end = np.clip((np.abs(z[par]) - (ps.HALF - 0.02)) / 0.02, 0, 1) ** 1.5
c = c * (1 - 0.35 * end[:, None]) + np.array([0.45, 0.30, 0.15]) * 0.35 * end[:, None]
c *= (1 - 0.35 * np.clip(depth, 0, 1))[:, None]
c *= (0.94 + 0.06 * fbm(pp * 300, 12))[:, None]                 # fine fibre grain
col[par] = c

# Wax: deep red, the raised rim lighter, the stamped face flat and a little darker, with the rune Othala
# ("home") pressed into it: a dark groove with a lit lip. Coordinates on the seal: s across the band
# (the rune stands along it, upright when the scroll lies across the view), t along the roll.
wax = z_ == 1
pw = p[wax]
s_ = (ang[wax] - ps.FRONT) * ps.SEAL_BASE / ps.SEAL_R
t_ = z[wax] / ps.SEAL_R
d = np.hypot(s_, t_)                                   # 0 centre, ~1 at the edge
c = ramp(d, [(0.0, (0.55, 0.07, 0.06)), (ps.STAMP - 0.02, (0.55, 0.07, 0.06)), (ps.STAMP + 0.03, (0.36, 0.04, 0.04)),
             (ps.STAMP + 0.12, (0.74, 0.15, 0.11)), (0.9, (0.66, 0.11, 0.08)), (1.1, (0.42, 0.05, 0.04))])
# Othala in the stamp's units (up = -s): a diamond whose lower sides run on past their meeting into two legs
ru = ps.STAMP * 0.95
T, L, R = (0.62, 0.0), (0.18, -0.34), (0.18, 0.34)
X = (-0.12, 0.0)
legL, legR = (-0.55, -0.40), (-0.55, 0.40)
segs = [(T, L), (T, R), (L, legR), (R, legL)]
q = np.stack([-s_ / ru, t_ / ru], -1)   # +s is down in the front view
gd = np.full(len(q), np.inf)
for a0, b0 in segs:
    a0, b0 = np.array(a0), np.array(b0); ab = b0 - a0
    tt = np.clip(((q - a0) @ ab) / (ab @ ab), 0, 1)
    gd = np.minimum(gd, np.linalg.norm(q - a0 - tt[:, None] * ab, axis=1))
groove = np.clip(1 - (gd - 0.09) / 0.05, 0, 1) * (d < ps.STAMP)
lip = np.clip(1 - np.abs(gd - 0.15) / 0.04, 0, 1) * (d < ps.STAMP)
c = c * (1 - 0.6 * groove)[:, None] + np.array([0.80, 0.22, 0.16]) * (0.5 * lip * (1 - groove))[:, None]
col[wax] = c * (0.94 + 0.08 * fbm(pw * 400, 31))[:, None]

# Cord: Diablo II blue. The twist shows as diagonal stripes; the band's sleeve is lighter on each wrap's
# ridge and dark in the grooves between wraps
cord = z_ == 2
pc = p[cord]
twist = 0.5 + 0.5 * np.sin(ang[cord] * 8 + z[cord] * 2500)
c = ramp(twist, [(0.0, (0.13, 0.23, 0.62)), (1.0, (0.22, 0.37, 0.84))])
on_band = (np.abs(z[cord]) <= ps.BAND + 0.0015) & (r[cord] < ps.R_WAIST + ps.CORD_R * 1.2)
ridge = np.clip((r[cord] - ps.R_WAIST - ps.CORD_R * 0.35) / (ps.CORD_R * 0.65), 0, 1)
c = np.where(on_band[:, None], c * (0.55 + 0.55 * ridge)[:, None], c)
col[cord] = c * (0.93 + 0.1 * fbm(pc * 500, 41))[:, None]

out = np.zeros((size, size, 4), np.float32); out[..., 3] = 1
out[valid, :3] = np.clip(col, 0, 1)
img = bpy.data.images.new(name, size, size, alpha=False)
img.pixels.foreach_set(out.ravel())
img.filepath_raw = os.path.join(outdir, name + ".png"); img.file_format = "PNG"
img.save()

# --- Export: one submesh per zone, all sharing the texture -----------------------------------------
me = obj.data
idx = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get("material_index", idx)
remap = np.array([ZONES[m.name] for m in mats], np.int32)
idx = remap[idx]
new_mats = []
for i, rough in enumerate((0.85, 0.35, 0.7)):
    m = bpy.data.materials.new(f"{name}_{i}"); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    o = nt.nodes.new("ShaderNodeOutputMaterial"); bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs[0], o.inputs[0])
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = rough
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
    world.use_nodes = True; world.node_tree.nodes["Background"].inputs[0].default_value = (0.2, 0.2, 0.22, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0; sun.rotation_euler = (0.9, 0.3, -0.6); scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    scene.camera = cam
    scene.cycles.samples = 48
    scene.render.resolution_x, scene.render.resolution_y = 1000, 500
    base_path, ext = os.path.splitext(preview)
    for tag, direction, scale in (("front", Vector((0, 1, -0.15)), 0.26),
                                  ("seal", Vector((0, 1, 0)), 0.06),
                                  ("back", Vector((0.5, -1, -0.3)), 0.26),
                                  ("end", Vector((0.35, 0.5, -1)), 0.2),
                                  ("icon", Vector((0, 1, 0)), 0.2)):
        cam.data.ortho_scale = scale
        cam.location = -direction.normalized() * 3
        q = (-cam.location).to_track_quat("-Z", "X" if tag != "end" else "Y")
        if tag == "icon":  # as the mod draws it: seal side, turned 45 degrees, 128 px
            from mathutils import Quaternion
            q = q @ Quaternion((0, 0, 1), math.radians(45))
            scene.render.resolution_x = scene.render.resolution_y = 128
            world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.05, 0.05, 1)
        cam.rotation_euler = q.to_euler()
        scene.render.filepath = f"{base_path}_{tag}{ext}"
        bpy.ops.render.render(write_still=True)
