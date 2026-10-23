# Dev check (see docs/SHARDPLATE.md). Usage:
#   blender -b --factory-startup --python tools/blender/hand_check.py -- tools/blender/player_skeleton.json models/ShardplateBreastplate/skin.obj <out.png>
# Close-up of the fitted right forearm + gauntlet (from skin.obj, rest pose) with Valheim's
# forearm, hand and finger bones drawn in, coloured per finger, to check the mapping.
import bpy, sys, json
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
skel_path, skin_path, out = argv[0], argv[1], argv[2]
skel = json.load(open(skel_path))
B = {b["name"]: np.array(b["position"]) for b in skel["bones"]}
P = {b["name"]: b["parent"] for b in skel["bones"]}

verts, faces = [], []
for line in open(skin_path):
    p = line.split()
    if not p: continue
    if p[0] == "v": verts.append((-float(p[1]), float(p[2]), float(p[3])))
    elif p[0] == "f": faces.append([int(t.split("/")[0]) - 1 for t in p[1:]])
V = np.array(verts)
keep = V[:, 0] > 0.30                       # right forearm and hand
idx = -np.ones(len(V), int); idx[keep] = np.arange(keep.sum())
sel_faces = [[idx[i] for i in f] for f in faces if all(keep[i] for i in f)]
bpy.ops.wm.read_factory_settings(use_empty=True)
me = bpy.data.meshes.new("arm"); me.from_pydata([(v[0], v[2], v[1]) for v in V[keep]], [], sel_faces); me.update()
ob = bpy.data.objects.new("arm", me); bpy.context.scene.collection.objects.link(ob)
m = bpy.data.materials.new("arm"); m.diffuse_color = (0.75, 0.78, 0.85, 1); me.materials.append(m)

colors = {"Thumb": (1, 0.2, 0.2, 1), "Index": (1, 0.7, 0.1, 1), "Middle": (0.2, 0.9, 0.2, 1),
          "Ring": (0.2, 0.6, 1, 1), "Pinky": (0.8, 0.3, 1, 1)}
for n, par in P.items():
    if not n.startswith("Right") or par not in B: continue
    if not any(k in n for k in ("ForeArm", "Hand")): continue
    a, b = B[par], B[n]
    d = b - a; L = float(np.linalg.norm(d))
    if L < 1e-4: continue
    mid = (a + b) / 2
    bpy.ops.mesh.primitive_cylinder_add(radius=0.004, depth=L, location=(mid[0], mid[2], mid[1]))
    c = bpy.context.active_object
    c.rotation_mode = "QUATERNION"
    c.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(Vector((d[0], d[2], d[1])))
    mat = bpy.data.materials.new(n)
    mat.diffuse_color = next((v for k, v in colors.items() if k in n), (0.05, 0.05, 0.05, 1))
    c.data.materials.append(mat); c.show_in_front = True

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"; scene.display.shading.color_type = "MATERIAL"
scene.display.shading.show_xray = True; scene.display.shading.xray_alpha = 0.55
scene.render.resolution_x = scene.render.resolution_y = 700
scene.world = bpy.data.worlds.new("w")
centre = Vector((B["RightHand"][0], B["RightHand"][2], B["RightHand"][1] + 0.02))
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
cam.data.type = "ORTHO"; cam.data.ortho_scale = 0.45; cam.data.clip_end = 20; scene.camera = cam
for label, d in (("front", Vector((0, 1, 0))), ("side", Vector((1, 0, 0))), ("under", Vector((0.2, 0.3, -1)).normalized())):
    cam.location = centre + d * 5
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = out.replace(".png", f"_{label}.png")
    bpy.ops.render.render(write_still=True)
