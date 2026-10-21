# Renders an item's model from the front, in profile and at three-quarters, side by side in one
# picture, for the pull request that adds or changes it. Works on any OBJ: a custom model in models/,
# or a code-built item exported with ModelExporter (exports/<Prefab>.obj).
#   blender -b --factory-startup --python tools/blender/render_views.py -- models/Thunderfury.obj docs/images/Thunderfury-views.jpg [turn]
# Arguments: the OBJ, the picture to write, and optionally degrees to turn it so its front faces the
# camera (weapons need none; the Shardplate helm needs 180). The model stands upright as the mod
# builds it (length along the OBJ's Y). Shows the shipped shape and texture, not in-game
# extras such as laid-over grain, glows or particles.
import bpy, sys, os, math
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = os.path.abspath(argv[0]), os.path.abspath(argv[1])
turn = float(argv[2]) if len(argv) > 2 else 0.0

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
bpy.ops.wm.obj_import(filepath=src)
model = [o for o in sc.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in model: o.select_set(True)
bpy.context.view_layer.objects.active = model[0]
if len(model) > 1: bpy.ops.object.join()
item = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
# The mod ignores the .mtl's emission (Ke) and adds its own glows in game; here it would wash parts white
for mat in item.data.materials:
    for node in (mat.node_tree.nodes if mat and mat.use_nodes else []):
        if node.type == 'BSDF_PRINCIPLED':
            node.inputs["Emission Strength"].default_value = 0.0

# Centre on the origin so each copy turns about its own middle
corners = [Vector(c) for c in item.bound_box]
lo = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
item.data.transform(Matrix.Translation(-(lo + hi) / 2))
size = hi - lo
height, across = size.z, max(size.x, size.y)

views = [0, 90, 35]  # front, profile, three-quarter
gap = max(across * 1.6, height * 0.42)
for i, angle in enumerate(views):
    o = item if i == 0 else item.copy()
    if i: sc.collection.objects.link(o)
    o.rotation_euler = (0, 0, math.radians(turn + angle))
    o.location = ((i - 1) * gap, 0, 0)

cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
cam.data.type = 'ORTHO'
width = 2 * gap + across
r = sc.render
r.resolution_x = 1800
r.resolution_y = 1500 if width / height < 1.2 else round(1800 * height / width * 1.25)
# Ortho scale spans the picture's wider side; leave a margin round the item
cam.data.ortho_scale = max(width, height * r.resolution_x / r.resolution_y) * 1.12
cam.location = (0, -10, 0)
cam.rotation_euler = (math.radians(90), 0, 0)
sc.collection.objects.link(cam)
sc.camera = cam

for name, energy, rot, angle in (("Key", 3.5, (60, 0, -35), 0.3), ("Fill", 1.2, (70, 0, 140), 0.5),
                                 ("Rim", 2.0, (-30, 0, 180), 0.2)):
    light = bpy.data.lights.new(name, 'SUN')
    light.energy, light.angle = energy, angle
    o = bpy.data.objects.new(name, light)
    o.rotation_euler = [math.radians(d) for d in rot]
    sc.collection.objects.link(o)

sc.world = bpy.data.worlds.new("World")
sc.world.use_nodes = True
sc.world.node_tree.nodes["Background"].inputs[0].default_value = (0.06, 0.065, 0.075, 1)

r.engine = 'BLENDER_EEVEE'
sc.view_settings.view_transform = 'Standard'
if out.lower().endswith((".jpg", ".jpeg")):
    # A JPEG is a tenth of the PNG's size, small enough to commit to docs/images/
    r.image_settings.file_format, r.image_settings.quality = 'JPEG', 88
os.makedirs(os.path.dirname(out), exist_ok=True)
r.filepath = out
bpy.ops.render.render(write_still=True)
print(f"RESULT wrote {out}")
