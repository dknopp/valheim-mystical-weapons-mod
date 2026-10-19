# Extracts the Shardplate helm (+collar) from Krajkoa's Sketchfab model and prepares it for the mod.
#
# Download "Shardplate" (glTF) from https://sketchfab.com/3d-models/shardplate-5a4fca1f665b469c9669f387aa879b34
# then run:
#   blender -b --factory-startup --python tools/blender/make_shardplate_helm.py -- <scene.gltf> models 8000 1024
# Arguments: source glTF, output folder, triangle budget (only decimates above it), texture size.
import bpy, sys, os
import numpy as np
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]
src, outdir, target_tris, tex_size = argv[0], argv[1], int(argv[2]), int(argv[3])
name = "ShardplateHelm"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
helm = next(o for o in bpy.data.objects if o.type == "MESH" and any(s.material and s.material.name == "helmet" for s in o.material_slots))
mat = helm.material_slots[0].material
bpy.context.view_layer.objects.active = helm
for o in bpy.context.view_layer.objects: o.select_set(o == helm)
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
for o in list(bpy.data.objects):
    if o != helm: bpy.data.objects.remove(o)
helm.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# Keep loose parts of the head + collar: above the shoulders, minus the spine segments down the back
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
def bounds(objs):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    return (Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))),
            Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))))
parts = [o for o in bpy.data.objects if o.type == "MESH"]
keep = [o for o in parts if bounds([o])[0].z >= 425 and bounds([o])[1].y >= 230]
for o in parts:
    if o not in keep: bpy.data.objects.remove(o)
bpy.ops.object.select_all(action="SELECT")
bpy.context.view_layer.objects.active = keep[0]
bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
obj.name = name

# Centre on the origin and convert to metres (helm ends up ~0.26 m wide)
lo, hi = bounds([obj])
obj.location -= (lo + hi) / 2
bpy.ops.object.transform_apply(location=True)
obj.scale = (0.001, 0.001, 0.001)
bpy.ops.object.transform_apply(scale=True)

before = sum(len(p.vertices) - 2 for p in obj.data.polygons)
# Only decimate when over budget: it melts hard edges, and the original custom normals are kept otherwise
if before > target_tris:
    mod = obj.modifiers.new("decimate", "DECIMATE")
    mod.ratio = target_tris / before
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
after = sum(len(p.vertices) - 2 for p in obj.data.polygons)

# One texture: base colour with the emissive glyphs added on top, resized for the game
def image_for(socket_name):
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    sock = bsdf.inputs.get(socket_name)
    def find(s):
        for l in s.links:
            n = l.from_node
            if n.type == "TEX_IMAGE": return n.image
            for i in n.inputs:
                r = find(i)
                if r: return r
        return None
    return find(sock) if sock else None
base = image_for("Base Color"); glow = image_for("Emission Color") or image_for("Emission")
print("TEXTURES base:", base and base.name, base and tuple(base.size), "glow:", glow and glow.name)
def pixels(img):
    img = img.copy(); img.scale(tex_size, tex_size)
    return np.array(img.pixels[:], dtype=np.float32).reshape(tex_size, tex_size, 4)
px = pixels(base)
if glow:
    g = pixels(glow)
    px[..., :3] = np.clip(px[..., :3] + g[..., :3], 0, 1)
out_img = bpy.data.images.new(name, tex_size, tex_size, alpha=False)
out_img.pixels = px.ravel()
out_img.filepath_raw = os.path.join(outdir, name + ".png"); out_img.file_format = "PNG"
out_img.save()

# Single material named <prefab>_0 using that texture
new_mat = bpy.data.materials.new(name + "_0"); new_mat.use_nodes = True
tex_node = new_mat.node_tree.nodes.new("ShaderNodeTexImage"); tex_node.image = out_img
bsdf = new_mat.node_tree.nodes["Principled BSDF"]
new_mat.node_tree.links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
obj.data.materials.clear(); obj.data.materials.append(new_mat)

bpy.ops.wm.obj_export(filepath=os.path.join(outdir, name + ".obj"), export_selected_objects=True,
                      export_uv=True, export_normals=True, export_materials=True, export_triangulated_mesh=True,
                      path_mode="STRIP", forward_axis="NEGATIVE_Z", up_axis="Y")
lo, hi = bounds([obj])
print(f"RESULT tris {before} -> {after}, size m = {tuple(round(v, 3) for v in hi - lo)}")
