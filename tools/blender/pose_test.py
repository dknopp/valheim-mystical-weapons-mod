# Dev check (see docs/SHARDPLATE.md). Usage:
#   blender -b --factory-startup --python tools/blender/pose_test.py -- tools/blender/player_skeleton.json "<chest skin.obj>;<legs skin.obj>" <out.png> [twohand]
# Deforms the exported skin.obj files with linear blend skinning, exactly as Unity will
# (vertex = sum w_i * boneWorld_i * bindpose_i * v), for a test pose, and renders rest vs posed.
import bpy, sys, json, math
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
skel_path, files, out = argv[0], argv[1].split(";"), argv[2]
skel = json.load(open(skel_path))
order = [b["name"] for b in skel["bones"]]
parent = {b["name"]: b["parent"] for b in skel["bones"]}


def qm(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def trs(p, q, s):
    m = np.eye(4); m[:3, :3] = qm(q) @ np.diag(s); m[:3, 3] = p; return m


def rot(axis, deg):
    a = np.radians(deg); axis = np.array(axis, float) / np.linalg.norm(axis)
    x, y, z = axis; c, s = np.cos(a), np.sin(a)
    r = np.eye(4)
    r[:3, :3] = [[c + x * x * (1 - c), x * y * (1 - c) - z * s, x * z * (1 - c) + y * s],
                 [y * x * (1 - c) + z * s, c + y * y * (1 - c), y * z * (1 - c) - x * s],
                 [z * x * (1 - c) - y * s, z * y * (1 - c) + x * s, c + z * z * (1 - c)]]
    return r


M = {b["name"]: trs(b["position"], b["rotation"], b["scale"]) for b in skel["bones"]}
# Test pose: rotations in Visual space about each joint (easier to reason about than bone axes)
POSE = {"LeftForeArm": ((0, 0, 1), -75), "RightArm": ((0, 0, 1), -60), "RightForeArm": ((1, 0, 0), -60),
        "LeftUpLeg": ((1, 0, 0), -55), "LeftLeg": ((1, 0, 0), 80), "Spine1": ((0, 1, 0), 15)}
if len(argv) > 3 and argv[3] == "twohand":
    # both arms forward and in, elbows bent, as when holding a two-handed weapon; plus a stride
    POSE = {"LeftArm": ((-1, 0, 0.6), 60), "LeftForeArm": ((-1, 0, 0), 70),
            "RightArm": ((-1, 0, -0.6), 60), "RightForeArm": ((-1, 0, 0), 70),
            "LeftUpLeg": ((1, 0, 0), -35), "RightUpLeg": ((1, 0, 0), 25), "LeftLeg": ((1, 0, 0), 30)}
FIST = len(argv) > 3 and argv[3] == "fist"
FIST_DEG = float(argv[4]) if len(argv) > 4 else 55.0
if FIST:
    # Close both hands: every finger joint (not the thumb) bends 55 degrees about the axis across the
    # knuckles, so each finger's gauntlet plates must follow their own finger
    POSE = {}
    for s in ("Left", "Right"):
        pos = {n: M[n][:3, 3] for n in M}
        across = pos[s + "HandIndex1"] - pos[s + "HandPinky1"]
        for f in ("Index", "Middle", "Ring", "Pinky"):
            for k in (1, 2, 3): POSE[s + "Hand" + f + str(k)] = (tuple(across * (1 if s == "Right" else -1)), FIST_DEG)
G = {}
for n in order:
    p = parent[n]
    if p in G:
        local = np.linalg.inv(M[p]) @ M[n]
        G[n] = G[p] @ local
    else:
        G[n] = M[n].copy()
    if n in POSE:
        axis, deg = POSE[n]
        pivot = G[n][:3, 3]
        T = np.eye(4); T[:3, 3] = pivot; Ti = np.eye(4); Ti[:3, 3] = -pivot
        G[n] = T @ rot(axis, deg) @ Ti @ G[n]

bpy.ops.wm.read_factory_settings(use_empty=True)
for k, path in enumerate(files):
    bp, verts, weights, faces = {}, [], [], []
    for line in open(path):
        p = line.split()
        if not p: continue
        if p[0] == "bp": bp[p[1]] = np.array([float(x) for x in p[2:18]]).reshape(4, 4)
        elif p[0] == "v": verts.append([-float(p[1]), float(p[2]), float(p[3]), 1.0])
        elif p[0] == "vw": weights.append([(order[int(p[i])], float(p[i + 1])) for i in range(1, 9, 2)])
        elif p[0] == "f": faces.append([int(t.split("/")[0]) - 1 for t in p[1:]])
    V = np.array(verts)
    skin = {n: G[n] @ bp[n] for n in bp}
    posed = np.zeros_like(V)
    for i, ws in enumerate(weights):
        for n, w in ws:
            if w > 0: posed[i] += w * (skin[n] @ V[i])
    shots = (("posed", posed, 0.0),) if FIST else (("rest", V, -0.75), ("posed", posed, 0.75))
    for label, P, dx in shots:
        me = bpy.data.meshes.new(f"{label}{k}")
        me.from_pydata([(q[0] + dx, q[2], q[1]) for q in P], [], faces); me.update()
        ob = bpy.data.objects.new(me.name, me); bpy.context.scene.collection.objects.link(ob)
        mat = bpy.data.materials.new(me.name); mat.diffuse_color = ((0.55, 0.65, 0.85, 1) if k == 0 else (0.45, 0.75, 0.8, 1))
        me.materials.append(mat)
    print(f"POSED {path}: {len(V)} vertices")

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"; scene.display.shading.color_type = "MATERIAL"
scene.render.resolution_x = 1000; scene.render.resolution_y = 700
scene.world = bpy.data.worlds.new("w")
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
cam.data.type = "ORTHO"; cam.data.ortho_scale = 3.2; cam.data.clip_end = 50; scene.camera = cam
centre = Vector((0, 0, 1.0))
views = (("front", Vector((0, 1, 0))), ("side", Vector((1, 0.15, 0)).normalized()))
if FIST:  # close-up of the posed right hand
    h = G["RightHandMiddle1"][:3, 3]; centre = Vector((h[0], h[2], h[1]))
    cam.data.ortho_scale = 0.4
    views = (("front", Vector((0, 1, 0))), ("back", Vector((0, -1, 0))), ("side", Vector((1, 0, 0))))
for label, d in views:
    cam.location = centre + d * 10
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = out.replace(".png", f"_{label}.png")
    bpy.ops.render.render(write_still=True)
