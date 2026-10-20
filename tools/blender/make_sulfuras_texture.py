# Bakes Sulfuras's textures (deep red elementium, flame-shaped swirls, a glowing haft, a ring of
# runes on the head) and a heat gradient for the gold spikes (white-hot at the tips), and writes
# models/Sulfuras.obj/.mtl/.png/_hot.png.
#
# The texture is computed in numpy from each texel's position and normal on the model (baked from
# Blender), so it is repeatable: change the shape in Sulfuras.cs, re-export, re-run this.
#   tools/ModelExporter/bin/Release/net48/ModelExporter.exe exports
#   blender -b --factory-startup --python tools/blender/make_sulfuras_texture.py -- exports/Sulfuras.obj models 512 [preview.png]
# Arguments: code-built export, output folder, texture size, optional preview render.
import bpy, bmesh, sys, os
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
src, outdir, size = argv[0], os.path.abspath(argv[1]), int(argv[2])
preview = argv[3] if len(argv) > 3 else None
name = "Sulfuras"
HEAD_Y = 0.93  # matches HeadY in Sulfuras.cs

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=src)
obj = next(o for o in bpy.data.objects if o.type == "MESH")
bpy.context.view_layer.objects.active = obj
obj.select_set(True)

# Shared vertices so faces join into islands; the mesh stays flat-shaded (no normals are exported)
bpy.ops.object.mode_set(mode="EDIT")
bm = bmesh.from_edit_mesh(obj.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
molten = [f for f in bm.faces if obj.material_slots[f.material_index].material.name.startswith(name + "_0")]
for f in bm.faces: f.select = f in molten
bmesh.update_edit_mesh(obj.data)
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02, scale_to_bounds=False)

# Give the head's front and back faces (where the runes go) three times the texture space, then repack
bm = bmesh.from_edit_mesh(obj.data)
uv = bm.loops.layers.uv.active
def to_mod(co):  # Blender (x, y, z) -> mod space (OBJ x is mirrored by the mod's importer)
    return np.array([-co.x, co.z, -co.y])
def rune_face(f):
    c = to_mod(f.calc_center_median())
    return abs(f.normal.y) > 0.95 and abs(c[0]) < 0.12 and abs(c[1] - HEAD_Y) < 0.16
for f in bm.faces:
    if f.select and rune_face(f):
        pts = [l[uv].uv.copy() for l in f.loops]
        mid = sum(pts, pts[0] * 0) / len(pts)
        for l in f.loops: l[uv].uv = mid + (l[uv].uv - mid) * 3
bmesh.update_edit_mesh(obj.data)
bpy.ops.uv.pack_islands(margin=0.015, rotate=True)
# Gold faces stay out of the bake: park their UVs in one corner (they get the heat gradient below)
bm = bmesh.from_edit_mesh(obj.data)
uv = bm.loops.layers.uv.active
for f in bm.faces:
    if not f.select:
        for l in f.loops: l[uv].uv = (0.0, 0.0)
bmesh.update_edit_mesh(obj.data)
bpy.ops.object.mode_set(mode="OBJECT")

# --- Bake each texel's position and normal (mod space, via emission) -------------------------------
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 1
scene.cycles.device = "CPU"
gold_mat = next(m for m in obj.data.materials if not m.name.startswith(name + "_0"))
molten_mat = next(m for m in obj.data.materials if m.name.startswith(name + "_0"))
molten_index = list(obj.data.materials).index(molten_mat)
bake_img = bpy.data.images.new("bake", size, size, alpha=True, float_buffer=True)

def bake(kind):
    for m in (molten_mat, gold_mat):
        m.use_nodes = True
        nt = m.node_tree; nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(em.outputs[0], out.inputs[0])
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        add = nt.nodes.new("ShaderNodeVectorMath"); add.operation = "ADD"
        add.inputs[1].default_value = (2.0, 2.0, 2.0)  # keep values positive; 0 means "no texel"
        nt.links.new(geo.outputs["Position" if kind == "pos" else "Normal"], add.inputs[0])
        nt.links.new(add.outputs[0], em.inputs["Color"])
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bake_img
        nt.nodes.active = tex
    bake_img.pixels.foreach_set(np.zeros(size * size * 4, np.float32))
    # Only the molten faces have real UVs; the gold faces bake into the parked corner, which we mask out
    bpy.ops.object.bake(type="EMIT", margin=6, margin_type="EXTEND")
    px = np.empty(size * size * 4, np.float32); bake_img.pixels.foreach_get(px)
    return px.reshape(size, size, 4)[..., :3] - 2.0, px.reshape(size, size, 4)[..., :3].max(-1) > 0.5

pos_b, valid = bake("pos")
nrm_b, _ = bake("nrm")
valid[:4, :4] = False  # the parked gold corner
P = np.stack([-pos_b[..., 0], pos_b[..., 2], -pos_b[..., 1]], -1)  # mod space: X across, Y along the haft, Z out
N = np.stack([-nrm_b[..., 0], nrm_b[..., 2], -nrm_b[..., 1]], -1)

# --- Procedural pattern ---------------------------------------------------------------------------
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

p = P[valid]; n = N[valid]

# Flame swirls: contour lines of a twice-warped noise field, stretched along each part's long axis so the
# flames run along the head (X) and down the haft (Y), as on the WoW model
on_head = np.clip((p[:, 1] - 0.72) / 0.08, 0, 1)[:, None]
along = on_head * np.array([0.35, 1, 1]) + (1 - on_head) * np.array([1, 0.3, 1])
q = p * along * 6
w1 = np.stack([fbm(q * 1.3 + 7 * k, 10 + k) for k in range(3)], -1) - 0.5
w2 = np.stack([fbm(q + 1.6 * w1 + 13 * k, 20 + k) for k in range(3)], -1) - 0.5
field = fbm(q + 1.8 * w2, 1)
band = 1 - np.abs((field * 3) % 1 - 0.5) * 2           # 1 on each contour line, 0 between
# Lines thicken into tongues in some places and thin out and break in others
width = 0.12 + 0.3 * np.clip((fbm(q * 0.7, 30) - 0.3) * 2.5, 0, 1)
flame = np.clip((band - (1 - width)) / np.maximum(width, 1e-4), 0, 1) ** 1.3
# The haft is a glowing bar in the reference: hotter all over, with brighter streaks
haft = ((p[:, 1] > -0.12) & (p[:, 1] < 0.34) & (np.hypot(p[:, 0], p[:, 2]) < 0.03)).astype(float)

# Runes: a ring of angular glyphs framing the stud on the centre block's front and back faces
rng = np.random.default_rng(7)
def glyph():
    pts = [(0, 0), (1, 0), (0, 1), (1, 1), (0, 2), (1, 2), (0.5, 0), (0.5, 2), (0.5, 1)]
    strokes = [(pts[6], pts[7])]  # every glyph has a stem
    for _ in range(rng.integers(2, 4)):
        a, b = rng.choice(len(pts), 2, replace=False)
        strokes.append((pts[a], pts[b]))
    return strokes
gw, gh = 0.018, 0.03  # glyph size (m)
ring = []  # glyph centres around a rectangle inside the face
hx, hy = 0.11 - 0.03, 0.15 - 0.035
for x in np.linspace(-hx, hx, 5): ring += [(x, hy), (x, -hy)]
for y in np.linspace(-hy, hy, 7)[1:-1]: ring += [(-hx, y), (hx, y)]
segs = []
for cx, cy in ring:
    for (ax, ay), (bx, by) in glyph():
        segs.append(((cx + (ax - 0.5) * gw, HEAD_Y + cy + (ay - 1) * gh / 2),
                     (cx + (bx - 0.5) * gw, HEAD_Y + cy + (by - 1) * gh / 2)))
segs = np.array(segs)
face = (np.abs(n[:, 2]) > 0.95) & (np.abs(p[:, 0]) < 0.11) & (np.abs(p[:, 1] - HEAD_Y) < 0.15)
q = np.stack([p[:, 0] * np.sign(n[:, 2]), p[:, 1]], -1)[face]  # mirrored on the back so both read the same way
a, b = segs[:, 0], segs[:, 1]
ab = b - a
t = np.clip(((q[:, None] - a) * ab).sum(-1) / (ab * ab).sum(-1), 0, 1)
dist = np.linalg.norm(q[:, None] - (a + t[..., None] * ab), axis=-1).min(-1)
rune = np.zeros(len(p))
rune[face] = 0.72 * np.clip(1 - (dist - 0.0018) / 0.0018, 0, 1)  # orange-gold, not white
# Keep the swirls clear of the runes so they read
flame *= 1 - 0.85 * face

# Yellow streaks running along the haft
streak = haft * np.clip((fbm(p * np.array([90, 6, 90]) + 1.5 * w1, 60) - 0.55) * 4, 0, 1)
heat = np.maximum(np.maximum(flame * (1 - haft), streak), rune)

# Colours (sRGB): deep red body (orange-red on the haft), swirls going orange -> yellow at their hottest
tone = (0.8 + 0.25 * fbm(p * 12, 50))[:, None]
body = (np.array([0.34, 0.045, 0.02]) * (1 - haft[:, None]) + np.array([0.85, 0.22, 0.04]) * haft[:, None]) * tone
ramp_t = np.array([0.0, 0.3, 0.65, 1.0])
ramp_c = np.array([[0.0, 0.0, 0.0], [0.75, 0.16, 0.02], [1.0, 0.45, 0.05], [1.0, 0.85, 0.35]])
hot = np.stack([np.interp(heat, ramp_t, ramp_c[:, k]) for k in range(3)], -1)
col = np.maximum(body, hot)

out = np.zeros((size, size, 4), np.float32); out[..., 3] = 1
out[..., :3] = np.array([0.34, 0.045, 0.02])
out[valid, :3] = np.clip(col, 0, 1)
img = bpy.data.images.new(name, size, size, alpha=False)
img.pixels.foreach_set(out.ravel())
img.filepath_raw = os.path.join(outdir, name + ".png"); img.file_format = "PNG"
img.save()

# --- Gold parts: a heat gradient, hottest at the spike tips ---------------------------------------
# Each gold vertex's U is how far it stands off the molten body, so a spike runs from deep gold where it
# meets the body to pale yellow-white at its tip; rims and studs sit on the body and stay at the cool end.
from mathutils.bvhtree import BVHTree
bm = bmesh.new(); bm.from_mesh(obj.data)
body = bm.copy()
bmesh.ops.delete(body, geom=[f for f in body.faces if f.material_index != molten_index], context="FACES")
tree = BVHTree.FromBMesh(body)
uv = bm.loops.layers.uv.active
for f in bm.faces:
    if f.material_index == molten_index: continue
    for l in f.loops:
        dist = tree.find_nearest(l.vert.co)[3] or 0.0
        l[uv].uv = (0.02 + 0.96 * min(dist / 0.13, 1.0), 0.5)
bm.to_mesh(obj.data); bm.free(); body.free()
hot_size = 64
u = np.linspace(0, 1, hot_size)
stops_t = [0.0, 0.35, 0.75, 1.0]
stops_c = [(0.9, 0.38, 0.05), (1.0, 0.6, 0.12), (1.0, 0.82, 0.4), (1.0, 0.97, 0.78)]  # sRGB
grad = np.stack([np.interp(u, stops_t, [c[k] for c in stops_c]) for k in range(3)] + [np.ones(hot_size)], -1)
hot_img = bpy.data.images.new(name + "_hot", hot_size, 4, alpha=False)
hot_img.pixels.foreach_set(np.tile(grad, (4, 1, 1)).astype(np.float32).ravel())
hot_img.filepath_raw = os.path.join(outdir, name + "_hot.png"); hot_img.file_format = "PNG"
hot_img.save()

# --- Export: the molten texture on Sulfuras_0, the gradient on Sulfuras_1 --------------------------
for m in (molten_mat, gold_mat): m.node_tree.nodes.clear()
def principled(m, tex=None, color=None, glow=None):
    nt = m.node_tree
    out = nt.nodes.new("ShaderNodeOutputMaterial"); bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    if tex:
        node = nt.nodes.new("ShaderNodeTexImage"); node.image = tex
        nt.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        # As in game: glow = texture x the material's emission colour
        mul = nt.nodes.new("ShaderNodeVectorMath"); mul.operation = "MULTIPLY"
        mul.inputs[1].default_value = glow[:3]
        nt.links.new(node.outputs["Color"], mul.inputs[0])
        nt.links.new(mul.outputs[0], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 1.0
    else:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Metallic"].default_value = 0.0
        bsdf.inputs["Roughness"].default_value = 0.3
        bsdf.inputs["Emission Color"].default_value = glow
        bsdf.inputs["Emission Strength"].default_value = 1.0

# Preview glow matches Sulfuras.cs's emission colours (env vars to try others), converted from sRGB to linear
srgb = lambda *c: tuple(v ** 2.2 for v in c) + (1,)
env = lambda k, d: srgb(*(float(v) for v in os.environ.get(k, d).split()))
principled(molten_mat, tex=img, glow=env("BODY_GLOW", "1 0.45 0.1"))
principled(gold_mat, tex=hot_img, glow=srgb(*(float(v) for v in os.environ.get("SPIKE_GLOW", "0.9 0.8 0.6").split())))
molten_mat.name = name + "_0"; gold_mat.name = name + "_1"
bpy.ops.wm.obj_export(filepath=os.path.join(outdir, name + ".obj"), export_selected_objects=True,
                      export_uv=True, export_normals=False, export_materials=True, export_triangulated_mesh=True,
                      path_mode="STRIP", forward_axis="NEGATIVE_Z", up_axis="Y")
print(f"RESULT {len(obj.data.polygons)} faces, {valid.sum()} textured texels, {face.sum()} rune-face texels")

# --- Optional preview render (not shipped) --------------------------------------------------------
if preview:
    from mathutils import Vector
    world = bpy.data.worlds.new("w"); scene.world = world
    world.use_nodes = True; world.node_tree.nodes["Background"].inputs[0].default_value = (0.03, 0.03, 0.035, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 2.5; sun.rotation_euler = (0.9, 0.3, 0.6); scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 1.45
    scene.camera = cam
    scene.cycles.samples = 48
    scene.render.resolution_x, scene.render.resolution_y = 700, 900
    scene.render.film_transparent = False
    base, ext = os.path.splitext(preview)
    for tag, direction in (("front", Vector((0, -1, 0))), ("angle", Vector((0.7, -0.7, 0.35)))):
        target = Vector((0, 0, 0.45))
        cam.location = target - direction.normalized() * 3
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{base}_{tag}{ext}"
        bpy.ops.render.render(write_still=True)
