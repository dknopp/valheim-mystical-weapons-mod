# Fits the body of Krajkoa's "Shardplate" (Sketchfab, CC BY 4.0) onto Valheim's player skeleton and
# writes models/ShardplateBreastplate/ and models/ShardplateGreaves/: a skin.obj each, skinned to the
# player's bones (see write_skinned), which the mod attaches as Valheim's "attach_skin". With
# SKINNED = False it writes one rigid OBJ per bone instead ("attach_<Bone>").
#
# Download "Shardplate" (glTF) from https://sketchfab.com/3d-models/shardplate-5a4fca1f665b469c9669f387aa879b34
# then run (preview renders fit_front.png / fit_side.png into <out>; export writes the models):
#   blender -b --factory-startup --python tools/blender/fit_shardplate_body.py -- <scene.gltf> tools/blender/player_skeleton.json <out> preview
#   blender -b --factory-startup --python tools/blender/fit_shardplate_body.py -- <scene.gltf> tools/blender/player_skeleton.json models export
#
# player_skeleton.json is the player's rest pose, dumped by the mod (see DevTools.cs).
import bpy, sys, json, os
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
# Absolute paths: Blender saves images relative to its own location, not the working directory
src, skel_path, outdir = (os.path.abspath(a) for a in argv[:3])
MODE = argv[3] if len(argv) > 3 else "preview"

skel = json.load(open(skel_path))
B = {b["name"]: np.array(b["position"]) for b in skel["bones"]}
BQ = {b["name"]: np.array(b["rotation"]) for b in skel["bones"]}  # Unity quaternion x,y,z,w
body = np.array(skel["body"])

# ---------------------------------------------------------------- landmarks
# Vertical (Krajkoa z -> Valheim y). The torso keeps one scale from the faulds to the shoulders
# (~1.2e-3), which drops the hip plates over the top of the thighs; the legs are stretched to
# Valheim's longer legs, with the thigh plates reaching up under the faulds so no gap opens.
TORSO_VK = [(-260, 0.75), (140, 1.24), (430, B["LeftArm"][1]), (477, B["Neck"][1]), (700, 1.95)]
LEG_VK = [(-698, 0.0), (-600, B["LeftFoot"][1]), (-380, B["LeftLeg"][1]), (-98, 1.19), (0, 1.30)]


def vmap(z, knots):
    k = np.array(knots, dtype=float)
    return np.interp(z, k[:, 0], k[:, 1])


def torso_z(h):
    names = ["Hips", "Spine", "Spine1", "Spine2", "Neck"]
    return np.interp(h, [B[n][1] for n in names], [B[n][2] for n in names])


TORSO = dict(cx=6.0, cy=320.0, sx=1.15e-3, sz=1.12e-3)
LEGK = {"L": -110.0, "R": 118.0}
LEG = dict(cy=300.0, sx=1.4e-3, sz=1.3e-3)


def leg_line(side, h):
    p = "Left" if side == "L" else "Right"
    names = [p + "Foot", p + "Leg", p + "UpLeg"]
    ys = [B[n][1] for n in names]
    return np.interp(h, ys, [B[n][0] for n in names]), np.interp(h, ys, [B[n][2] for n in names])


def kj(x, y, z):  # Blender -> Unity axes
    return np.array([x, z, y], dtype=float)


ARMK = {"L": [kj(-175, 285, 430), kj(-283, 268, 190), kj(-372, 370, 60), kj(-395, 425, -80)],
        "R": [kj(181, 285, 430), kj(289, 268, 190), kj(378, 370, 60), kj(401, 425, -80)]}


def armv(side):
    p = "Left" if side == "L" else "Right"
    tip = (B[p + "HandMiddle3"] - B[p + "HandMiddle2"]) + B[p + "HandMiddle3"]
    return [B[p + "Arm"], B[p + "ForeArm"], B[p + "Hand"], tip]


ARM_GIRTH = [1.0e-3, 1.0e-3, 1.25e-3]  # perpendicular scale per segment (upper, fore, hand)
# Forward (+Z) nudge of the arm plates at shoulder, elbow, wrist and fingertips, blended along the
# arm. Zero: a forward nudge (up to 4 cm) was tried and made the arms poke out of the back instead.
ARM_FORWARD = [0.0, 0.0, -0.025, -0.025]
# Extra girth of the arm plates (not the gauntlets) at the same four knots, blended the same way:
# Valheim's wrist is thick front to back and pokes out of a vambrace that tapers like Krajkoa's.
ARM_FLARE = [1.0, 1.05, 1.25, 1.25]


def rot_between(a, b):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b)
    v = np.cross(a, b); c = float(np.dot(a, b))
    if np.linalg.norm(v) < 1e-9: return np.eye(3)
    vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + vx + vx @ vx * (1 / (1 + c))


def map_arm(P, side, flare=True):
    K = ARMK[side]; V = armv(side)
    F = ARM_FLARE if flare else [1.0] * 4
    out = np.empty_like(P)
    best = np.full(len(P), np.inf); seg = np.zeros(len(P), int); tpar = np.zeros(len(P))
    for i in range(3):
        a, b = K[i], K[i + 1]; ab = b - a
        t = np.clip(((P - a) @ ab) / (ab @ ab), -0.6 if i == 0 else 0.0, 1.0 if i < 2 else 1.4)
        d = np.linalg.norm(P - (a + t[:, None] * ab), axis=1)
        m = d < best; best[m] = d[m]; seg[m] = i; tpar[m] = t[m]
    for i in range(3):
        m = seg == i
        if not m.any(): continue
        a, b = K[i], K[i + 1]; va, vb = V[i], V[i + 1]
        R = rot_between(b - a, vb - va)
        t = tpar[m]
        tc = np.clip(t, 0, 1)
        fl = F[i] + tc * (F[i + 1] - F[i])
        perp = (P[m] - (a + t[:, None] * (b - a))) @ R.T * ARM_GIRTH[i] * fl[:, None]
        fwd = ARM_FORWARD[i] + tc * (ARM_FORWARD[i + 1] - ARM_FORWARD[i])
        out[m] = va + t[:, None] * (vb - va) + perp + np.stack([0 * fwd, 0 * fwd, fwd], 1)
    return out


def map_torso(P):
    h = vmap(P[:, 1], TORSO_VK)
    return np.stack([(P[:, 0] - TORSO["cx"]) * TORSO["sx"], h,
                     torso_z(h) + (P[:, 2] - TORSO["cy"]) * TORSO["sz"]], 1)


# Extra depth behind the thigh (hamstrings, glutes), growing from the knee to the top of the thigh
LEG_BACK = 1.4


def map_leg(P, side):
    h = vmap(P[:, 1], LEG_VK); lx, lz = leg_line(side, h)
    dz = (P[:, 2] - LEG["cy"]) * LEG["sz"]
    t = np.clip((h - B["LeftLeg"][1] - 0.05) / 0.25, 0, 1); t = t * t * (3 - 2 * t)
    dz = np.where(dz < 0, dz * (1 + (LEG_BACK - 1) * t), dz)
    return np.stack([lx + (P[:, 0] - LEGK[side]) * LEG["sx"], h, lz + dz], 1)


# ---------------------------------------------------------------- load + split into plates
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in [o for o in bpy.data.objects if o.type == "MESH"]:
    bpy.context.view_layer.objects.active = o
    for x in bpy.context.view_layer.objects: x.select_set(x == o)
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)

plates = []
for o in list(bpy.data.objects):
    mat = o.material_slots[0].material.name
    co = np.array([v.co[:] for v in o.data.vertices])
    lo, hi = co.min(0), co.max(0)
    if mat == "helmet" and lo[2] >= 425 and hi[1] >= 230:
        bpy.data.objects.remove(o); continue  # the helm is a separate item
    if mat == "skirt":
        bpy.data.objects.remove(o); continue  # the scale skirt is left out of the greaves
    plates.append((o, mat, "legs" if mat in ("material", "skirt") else "chest"))

# ---------------------------------------------------------------- map every plate
# Girth multipliers per region (x = width, z = depth). Chosen so the breastplate is in proportion
# with the greaves and the body stays inside the plates; the torso grows more in depth than width
# so it doesn't collide with the arms in the A-pose.
GIRTH = {"torso": (1.38, 1.62), "leg": (1.45, 1.45), "arm": (2.05, 2.05)}
HAND_GIRTH = 1.65  # gauntlets stay at their earlier, well-fitting size
for i, (o, mat, kind) in enumerate(plates):
    P = np.array([v.co[:] for v in o.data.vertices])[:, [0, 2, 1]]  # Unity axes, Krajkoa units
    c = P.mean(0)
    if mat in ("material_5", "MetalGlove"): region, side = "arm", ("L" if c[0] < 0 else "R")
    elif mat == "material": region, side = "leg", ("L" if c[0] < 0 else "R")
    else: region, side = "torso", None
    plates[i] = dict(obj=o, mat=mat, kind=kind, P=P, region=region, side=side)

def map_plate(pl):
    gx, gz = GIRTH[pl["region"]]
    if pl["region"] == "arm":
        saved = list(ARM_GIRTH)
        for k in range(3): ARM_GIRTH[k] = saved[k] * (gx if k < 2 else HAND_GIRTH)
        Q = map_arm(pl["P"], pl["side"], flare=pl["mat"] != "MetalGlove"); ARM_GIRTH[:] = saved
    elif pl["region"] == "leg":
        sx, sz = LEG["sx"], LEG["sz"]; LEG["sx"], LEG["sz"] = sx * gx, sz * gz
        Q = map_leg(pl["P"], pl["side"]); LEG["sx"], LEG["sz"] = sx, sz
    else:
        sx, sz = TORSO["sx"], TORSO["sz"]; TORSO["sx"], TORSO["sz"] = sx * gx, sz * gz
        Q = map_torso(pl["P"]); TORSO["sx"], TORSO["sz"] = sx, sz
        if is_groin(pl):
            # Krajkoa's codpiece would sit at belly height on Valheim's longer legs: keep its top
            # tucked under the abdominal bands and stretch it down to the groin
            top_old = vmap(GROIN_TOP_K, TORSO_VK)
            Q = Q.copy(); Q[:, 1] = GROIN_TOP - (top_old - Q[:, 1]) * GROIN_STRETCH
    return Q


GROIN_TOP_K = 133.0    # top of the codpiece plates, Krajkoa z
GROIN_TOP = 1.18       # where that top goes on Valheim (under the abdominal bands)
GROIN_STRETCH = 1.3    # bottom lands around 0.91, Valheim's groin


def is_groin(pl):
    """The narrow front plates (and their rivets) that hang from the belt over the groin."""
    P = pl["P"]  # Unity axes in Krajkoa units: x, z (up), y (forward)
    return (pl["mat"] == "core" and abs(P[:, 0].mean()) < 40 and P[:, 2].min() > 420
            and P[:, 1].min() < 20 and P[:, 1].max() < 140)

# Body points checked per region (the head and hands are covered by the helm and gauntlets)
BODY_SEL = {
    "torso": lambda p: (np.abs(p[:, 0]) < 0.17) & (p[:, 1] > 1.0) & (p[:, 1] < 1.62),
    "leg": lambda p: (np.abs(p[:, 0]) > 0.02) & (np.abs(p[:, 0]) < 0.25) & (p[:, 1] > 0.08) & (p[:, 1] < 0.95),
    "arm": lambda p: (np.abs(p[:, 0]) > 0.25) & (p[:, 1] > 1.12),
}
def outside_fraction(region, Qs):
    A = np.concatenate(Qs); P = body[BODY_SEL[region](body)]
    bad = n = 0
    for p in P:
        # compare against armour on the same side for limbs
        side_ok = A if region == "torso" else A[np.sign(A[:, 0]) == np.sign(p[0])]
        band = side_ok[np.abs(side_ok[:, 1] - p[1]) < 0.04]
        if len(band) < 10: continue
        c = band.mean(0)
        ang = np.arctan2(p[2] - c[2], p[0] - c[0])
        a2 = np.arctan2(band[:, 2] - c[2], band[:, 0] - c[0])
        near = band[np.abs(np.angle(np.exp(1j * (a2 - ang)))) < 0.35]
        if len(near) == 0: continue
        n += 1
        # the body must sit well inside (plates have thickness)
        if np.hypot(p[0] - c[0], p[2] - c[2]) > 0.88 * np.hypot(near[:, 0] - c[0], near[:, 2] - c[2]).max(): bad += 1
    return bad / max(n, 1), n

for region in ("torso", "leg", "arm"):
    frac, n = outside_fraction(region, [map_plate(pl) for pl in plates if pl["region"] == region])
    print(f"GIRTH {region}: {GIRTH[region]} -> {frac:.0%} of {n} body points poke through")

for pl in plates:
    Q = map_plate(pl)
    pl["Q"] = Q
    for v, q in zip(pl["obj"].data.vertices, Q): v.co = (q[0], q[2], q[1])  # Unity -> Blender axes
    pl["obj"].data.update()
print("PLATES", len(plates))

if MODE == "preview":
    colors = {"core": (0.9, 0.3, 0.3, 1), "material_5": (0.3, 0.8, 0.3, 1), "MetalGlove": (0.3, 0.4, 0.95, 1),
              "helmet": (0.95, 0.85, 0.2, 1), "skirt": (0.8, 0.3, 0.9, 1), "material": (0.3, 0.85, 0.9, 1)}
    for m in bpy.data.materials:
        if m.name in colors: m.diffuse_color = colors[m.name]
    bm_mat = bpy.data.materials.new("bodypt"); bm_mat.diffuse_color = (1.0, 0.75, 0.55, 1)
    bone_mat = bpy.data.materials.new("bone"); bone_mat.diffuse_color = (0.05, 0.05, 0.05, 1)
    for p in body:
        bpy.ops.mesh.primitive_cube_add(size=0.02, location=(p[0], p[2], p[1]))
        bpy.context.active_object.data.materials.append(bm_mat)
    for b in skel["bones"]:
        if b["parent"] not in B or any(f in b["name"] for f in ("Thumb", "Index", "Middle", "Ring", "Pinky", "Jaw")):
            continue
        a = B[b["parent"]]; c2 = B[b["name"]]; d = c2 - a; L = float(np.linalg.norm(d))
        if L < 1e-4: continue
        mid = (a + c2) / 2
        bpy.ops.mesh.primitive_cylinder_add(radius=0.01, depth=L, location=(mid[0], mid[2], mid[1]))
        cyl = bpy.context.active_object
        cyl.rotation_mode = "QUATERNION"
        cyl.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(Vector((d[0], d[2], d[1])))
        cyl.data.materials.append(bone_mat)
        cyl.show_in_front = True

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"; scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_xray = True; scene.display.shading.xray_alpha = 0.75
    scene.render.resolution_x = 600; scene.render.resolution_y = 800
    scene.world = bpy.data.worlds.new("w")
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 2.2; cam.data.clip_end = 50; scene.camera = cam
    centre = Vector((0, 0, 1.0))
    for label, d in [("front", Vector((0, 1, 0))), ("side", Vector((1, 0, 0)))]:
        cam.location = centre + d * 10
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{outdir}/fit_{label}.png"
        bpy.ops.render.render(write_still=True)

# ---------------------------------------------------------------- export: one OBJ per bone
MAT_INDEX = {"helmet": 0, "core": 1, "material_5": 2, "MetalGlove": 3, "material": 4, "skirt": 5}
BUDGET = {"chest": 20000, "legs": 10000}   # triangles per item


def bone_for(pl):
    Q = pl["Q"]; c = Q.mean(0); side = "Left" if c[0] < 0 else "Right"
    if pl["region"] == "arm":
        # nearest Valheim arm segment
        V = armv(pl["side"]); names = ["Arm", "ForeArm", "Hand"]
        d = []
        for i in range(3):
            a, b = V[i], V[i + 1]; ab = b - a
            t = np.clip((c - a) @ ab / (ab @ ab), 0, 1)
            d.append(np.linalg.norm(c - (a + t * ab)))
        return side + names[int(np.argmin(d))]
    if pl["region"] == "leg":
        if c[1] > B[side + "Leg"][1] + 0.02: return side + "UpLeg"
        if c[1] > B[side + "Foot"][1] + 0.03: return side + "Leg"
        return side + "Foot"
    if pl["mat"] == "skirt":
        return "Hips" if abs(c[0]) < 0.04 else side + "UpLeg"
    for name in ("Spine2", "Spine1", "Spine"):
        if c[1] >= B[name][1]: return name
    return "Hips"


def quat_matrix(q):  # Unity quaternion (x, y, z, w) -> 3x3 rotation
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def prepare_textures(folder, used, size=1024):
    """Base colour (+ emissive glyphs) per Krajkoa material, resized, as Shardplate_<index>.png."""
    files = {}
    for mat_name, idx in MAT_INDEX.items():
        if mat_name not in used: continue
        mat = bpy.data.materials.get(mat_name)
        bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        def find(sock):
            for l in sock.links:
                n = l.from_node
                if n.type == "TEX_IMAGE": return n.image
                for i in n.inputs:
                    r = find(i)
                    if r: return r
            return None
        base = find(bsdf.inputs["Base Color"])
        glow = find(bsdf.inputs["Emission Color"]) if bsdf.inputs.get("Emission Color") else None
        img = base.copy(); img.scale(size, size)
        px = np.array(img.pixels[:], dtype=np.float32).reshape(size, size, 4)
        if glow:
            g = glow.copy(); g.scale(size, size)
            px[..., :3] = np.clip(px[..., :3] + np.array(g.pixels[:], dtype=np.float32).reshape(size, size, 4)[..., :3], 0, 1)
        out = bpy.data.images.new(f"Shardplate_{idx}", size, size, alpha=False)
        out.pixels = px.ravel()
        out.filepath_raw = os.path.join(folder, f"Shardplate_{idx}.png"); out.file_format = "PNG"; out.save()
        files[idx] = f"Shardplate_{idx}.png"
    return files


def write_obj(path, bone, objs):
    """Writes objs (Blender axes, Valheim Visual space) in the bone's local frame for the mod."""
    R = quat_matrix(BQ[bone]); Pb = B[bone]
    lines = [f"# {bone} - Shardplate by Krajkoa (CC BY 4.0), fitted to Valheim's skeleton", "mtllib Shardplate.mtl", f"o {bone}"]
    vi = vti = vni = 0; faces = []
    tris = 0
    for o in objs:
        me = o.data
        me.calc_loop_triangles()
        uv = me.uv_layers.active.data if me.uv_layers.active else None
        base_v = vi; base_t = vti; base_n = vni
        for v in me.vertices:
            u = np.array([v.co[0], v.co[2], v.co[1]])                 # Blender -> Unity axes
            l = R.T @ (u - Pb)                                         # into the bone's frame
            lines.append(f"v {-l[0]:.6f} {l[1]:.6f} {l[2]:.6f}")      # OBJ is right-handed: mirror X
            vi += 1
        normals = me.corner_normals
        for tri in me.loop_triangles:
            idx = []
            for k in range(3):
                loop = tri.loops[k]
                t = uv[loop].uv if uv else (0, 0)
                lines.append(f"vt {t[0]:.5f} {t[1]:.5f}"); vti += 1
                n = normals[loop].vector
                nu = R.T @ np.array([n[0], n[2], n[1]])
                lines.append(f"vn {-nu[0]:.4f} {nu[1]:.4f} {nu[2]:.4f}"); vni += 1
                idx.append(f"{base_v + tri.vertices[k] + 1}/{vti}/{vni}")
            faces.append((o["mat_index"], f"f {idx[0]} {idx[2]} {idx[1]}"))
            tris += 1
    for m in sorted(set(f[0] for f in faces)):
        lines.append(f"usemtl Shardplate_{m}")
        lines += [f[1] for f in faces if f[0] == m]
    open(path, "w").write("\n".join(lines) + "\n")
    return tris


# ---------------------------------------------------------------- skinned export
# One mesh per item, each vertex blended between its nearest bones (inverse distance to the bone
# segments, limited to the plate's own region), so the armour bends with the body instead of riding
# on single bones. (Blender's bone-heat weighting fails on this many separate plates.) Written as skin.obj with two extras
# the mod reads: "bp <bone> <16 floats>" bind poses (Unity space, row-major, in the player's bone
# order) and "vw <bone index> <weight> x4" after each vertex.
SKINNED = True
BONE_ORDER = [b["name"] for b in skel["bones"]]
BS = {b["name"]: np.array(b["scale"]) for b in skel["bones"]}
PARENT = {b["name"]: b["parent"] for b in skel["bones"]}
CHILDREN = {}
for _n in BONE_ORDER: CHILDREN.setdefault(PARENT[_n], []).append(_n)
TAIL_PREF = ("Middle1", "Spine", "Neck", "Head", "ForeArm", "Hand", "Arm", "Leg", "Foot", "ToeBase", "2", "3")


def bone_segment(n):
    """A bone as a line segment from its joint towards its main child (leaves extend their parent)."""
    kids = CHILDREN.get(n, [])
    for pref in TAIL_PREF:
        k = next((k for k in kids if k.endswith(pref) or pref in k), None)
        if k and np.linalg.norm(B[k] - B[n]) > 1e-3: return B[n], B[k]
    par = PARENT[n]
    d = B[n] - B[par] if par in B else np.array([0.0, 1.0, 0.0])
    return B[n], B[n] + d / max(np.linalg.norm(d), 1e-6) * 0.04


SEGMENTS = {n: bone_segment(n) for n in BONE_ORDER}


def allowed_bones(pl):
    """Bones a plate may follow: its own region only, so e.g. arm swings can't drag the chest."""
    s = "Left" if pl["side"] == "L" or (pl["side"] is None and pl["Q"].mean(0)[0] < 0) else "Right"
    if pl["region"] == "arm":
        # No spine bones (plates near the armpit must go with the arm when it swings forward), and
        # no hand bones for the arm plates (turning the wrist must not twist the vambrace).
        # Gauntlet pieces are handled separately (one bone each, see rigid_hand_piece).
        c = pl["Q"].mean(0); V = armv(pl["side"])
        upper = np.linalg.norm(c - V[0]) + np.linalg.norm(c - V[1]) < np.linalg.norm(c - V[1]) + np.linalg.norm(c - V[2])
        return [s + "Shoulder", s + "Arm", s + "ForeArm"] if upper else [s + "Arm", s + "ForeArm"]
    if pl["region"] == "leg":
        return ["Hips", s + "UpLeg", s + "Leg", s + "Foot", s + "ToeBase"]
    return ["Hips", "Spine", "Spine1", "Spine2", "Neck", "LeftShoulder", "RightShoulder", "LeftUpLeg", "RightUpLeg"]


HAND_PIECE_MAX = 0.08   # finger pieces up to this size ride rigidly on one finger bone; larger ones bend along the finger
HAND_PLATE_MIN = 0.12   # gauntlet pieces larger than this (back of hand, knuckles) ride on the hand bone
FINGER_SNAP = 0.8       # how far a finger piece is pulled onto its finger bone (fraction of the gap)
FINGER_SNAP_MAX = 0.03  # but never moved more than this (metres)


def align_gauntlets(plates):
    """Krajkoa's gauntlet, fitted along the forearm, ends up a few cm forward of Valheim's fingers and
    a bit long, so each of its fingers lands on the neighbouring Valheim finger. Per hand, fit a
    scale (about the wrist) and shift that lines its finger pieces up with Valheim's finger bones
    (thumb excluded - it is off by a different amount), and apply it to the whole gauntlet."""
    for side in ("L", "R"):
        s = "Left" if side == "L" else "Right"
        pieces = [pl for pl in plates if pl["mat"] == "MetalGlove" and pl["side"] == side]
        if not pieces: continue
        fingers = [n for n in BONE_ORDER if n.startswith(s + "Hand") and n != s + "Hand" and "Thumb" not in n]
        small = [pl for pl in pieces if np.max(pl["Q"].max(0) - pl["Q"].min(0)) <= HAND_PIECE_MAX]
        W = B[s + "Hand"]
        # Scale from hand length: how far the fingertips reach from the wrist along the hand
        axis = SEGMENTS[s + "Hand"][1] - W; axis /= np.linalg.norm(axis)
        glove_reach = max(((pl["Q"] - W) @ axis).max() for pl in small)
        bone_reach = max((SEGMENTS[n][1] - W) @ axis for n in fingers)
        scale = float(np.clip(bone_reach / glove_reach, 0.8, 1.1))
        shift = np.zeros(3)

        def closest(c):
            best, foot = np.inf, None
            for n in fingers:
                a, b = SEGMENTS[n]; ab = b - a
                t = np.clip((c - a) @ ab / max(ab @ ab, 1e-9), 0, 1)
                p = a + t * ab; d = np.linalg.norm(c - p)
                if d < best: best, foot = d, p
            return foot, best

        for _ in range(12):
            C = np.array([W + scale * (pl["Q"].mean(0) - W) + shift for pl in small])
            pairs = [(c, *closest(c)) for c in C]
            resid = np.median([d for _, _, d in pairs])
            pairs = [(c, f) for c, f, d in pairs if d < max(2.5 * resid, 0.01)]  # ignore outliers
            # translation only: move the pieces by their average gap to the bones
            shift = shift + np.mean([f - c for c, f in pairs], axis=0)
            L = np.linalg.norm(shift)
            if L > 0.06: shift *= 0.06 / L
        print(f"GAUNTLET {s}: scale {scale:.3f} about the wrist, shift {np.round(shift, 3)} m, median gap {resid:.3f} m")
        for pl in pieces:
            Q = W + scale * (pl["Q"] - W) + shift
            pl["Q"] = Q
            for v, q in zip(pl["obj"].data.vertices, Q): v.co = (q[0], q[2], q[1])
            pl["obj"].data.update()


def rigid_hand_piece(pl):
    """A gauntlet piece follows its nearest bone - a finger segment, or the hand for the palm and
    back plates - like a real articulated gauntlet, so a closing fist carries each finger's plates
    with it. Small finger pieces ride rigidly on one bone and are pulled onto their finger, since
    Krajkoa's fingers sit a little off Valheim's (and his thumb well forward of it). Finger pieces
    longer than HAND_PIECE_MAX span a knuckle, so they are weighted along that finger's joints
    instead (they used to ride on the hand, which left e.g. the middle finger unable to bend)."""
    s = "Left" if pl["side"] == "L" else "Right"
    Q = pl["Q"]; c = Q.mean(0)
    best = np.inf
    for n in [s + "Hand"] + [n for n in BONE_ORDER if n.startswith(s + "Hand") and n != s + "Hand"]:
        a, b = SEGMENTS[n]; ab = b - a
        t = np.clip((c - a) @ ab / max(ab @ ab, 1e-9), 0, 1)
        d = np.linalg.norm(c - (a + t * ab))
        if d < best: best, bone, foot = d, n, a + t * ab
    size = np.max(Q.max(0) - Q.min(0))
    if size > HAND_PLATE_MIN:
        bone = s + "Hand"  # back-of-hand and knuckle plates: tearing them along the fingers looks wrong
    elif bone != s + "Hand" and size > HAND_PIECE_MAX:
        finger = bone[:-1]  # e.g. RightHandMiddle
        assign_weights(pl, power=8.0, names=[s + "Hand"] + [finger + str(k) for k in (1, 2, 3)])
        return bone
    if bone != s + "Hand":
        shift = (foot - c) * FINGER_SNAP
        L = np.linalg.norm(shift)
        if L > FINGER_SNAP_MAX: shift *= FINGER_SNAP_MAX / L
        Q = Q + shift; pl["Q"] = Q
        for v, q in zip(pl["obj"].data.vertices, Q): v.co = (q[0], q[2], q[1])
        pl["obj"].data.update()
    o = pl["obj"]
    g = o.vertex_groups.get(bone) or o.vertex_groups.new(name=bone)
    g.add(list(range(len(Q))), 1.0, "REPLACE")
    return bone


def assign_weights(pl, power=None, names=None):
    """Smooth inverse-distance weights to the nearest bone segments, top 4, as vertex groups.
    Arm plates use a sharper falloff so each follows its own segment and only blends at the joints."""
    if pl["mat"] == "MetalGlove" and names is None:
        return rigid_hand_piece(pl)
    if power is None: power = 6.0 if pl["region"] == "arm" else 4.0
    Q = pl["Q"]; names = names or allowed_bones(pl)
    D = np.empty((len(Q), len(names)))
    for j, n in enumerate(names):
        a, b = SEGMENTS[n]; ab = b - a
        t = np.clip((Q - a) @ ab / max(ab @ ab, 1e-9), 0, 1)
        D[:, j] = np.linalg.norm(Q - (a + t[:, None] * ab), axis=1)
    W = 1.0 / np.maximum(D, 0.01) ** power
    keep = np.argsort(-W, axis=1)[:, :4]
    o = pl["obj"]
    groups = {}
    for i in range(len(Q)):
        w = W[i, keep[i]]; w = w / w.sum()
        for j, wt in zip(keep[i], w):
            if wt < 0.01: continue
            n = names[j]
            if n not in groups: groups[n] = o.vertex_groups.get(n) or o.vertex_groups.new(name=n)
            groups[n].add([i], float(wt), "REPLACE")


def trs(p, q, sc):
    m = np.eye(4); m[:3, :3] = quat_matrix(q) @ np.diag(sc); m[:3, 3] = p
    return m


def to_blender(u): return Vector((u[0], u[2], u[1]))


def write_skinned(path, kind, pieces):
    bpy.ops.object.select_all(action="DESELECT")
    for o in pieces: o.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    if len(pieces) > 1: bpy.ops.object.join()
    mesh_obj = bpy.context.view_layer.objects.active

    me = mesh_obj.data
    me.calc_loop_triangles()
    groups = {g.index: g.name for g in mesh_obj.vertex_groups}
    index_of = {n: i for i, n in enumerate(BONE_ORDER)}
    lines = [f"# Shardplate by Krajkoa (CC BY 4.0), fitted and skinned to Valheim's player skeleton",
             "mtllib Shardplate.mtl", f"o {kind}"]
    for n in BONE_ORDER:
        bp = np.linalg.inv(trs(B[n], BQ[n], BS[n]))
        lines.append("bp " + n + " " + " ".join(f"{x:.7g}" for x in bp.ravel()))
    unweighted = 0
    for v in me.vertices:
        u = np.array([v.co[0], v.co[2], v.co[1]])
        lines.append(f"v {-u[0]:.6f} {u[1]:.6f} {u[2]:.6f}")
        ws = sorted(((g.weight, groups[g.group]) for g in v.groups if g.weight > 1e-4 and groups[g.group] in index_of),
                    reverse=True)[:4]
        total = sum(w for w, _ in ws)
        if total < 1e-6:
            ws, total = [(1.0, "Hips")], 1.0; unweighted += 1
        ws += [(0.0, ws[0][1])] * (4 - len(ws))
        lines.append("vw " + " ".join(f"{index_of[n]} {w / total:.4f}" for w, n in ws))
    uv = me.uv_layers.active.data if me.uv_layers.active else None
    normals = me.corner_normals
    faces = []; vt = 0
    for tri in me.loop_triangles:
        idx = []
        for k in range(3):
            loop = tri.loops[k]
            t = uv[loop].uv if uv else (0, 0)
            n = normals[loop].vector
            lines.append(f"vt {t[0]:.5f} {t[1]:.5f}")
            lines.append(f"vn {-n[0]:.4f} {n[2]:.4f} {n[1]:.4f}")
            vt += 1
            idx.append(f"{tri.vertices[k] + 1}/{vt}/{vt}")
        mat = mesh_obj.material_slots[tri.material_index].material.name
        faces.append((MAT_INDEX[mat], f"f {idx[0]} {idx[2]} {idx[1]}"))
    for m in sorted(set(f[0] for f in faces)):
        lines.append(f"usemtl Shardplate_{m}")
        lines += [f[1] for f in faces if f[0] == m]
    open(path, "w").write("\n".join(lines) + "\n")
    print(f"WEIGHTS {kind}: {len(me.vertices)} vertices, {unweighted} fell back to the nearest bone")
    return len(faces)


if MODE == "export":
    items = {"chest": os.path.join(outdir, "ShardplateBreastplate"), "legs": os.path.join(outdir, "ShardplateGreaves")}
    groups = {}
    align_gauntlets(plates)
    for pl in plates:
        if SKINNED: assign_weights(pl)
        pl["obj"]["mat_index"] = MAT_INDEX[pl["mat"]]; pl["obj"]["kind"] = pl["kind"]
        groups.setdefault((pl["kind"], bone_for(pl), pl["mat"]), []).append(pl["obj"])
    for kind, folder in items.items():
        os.makedirs(folder, exist_ok=True)
        tex = prepare_textures(folder, {m for (k, _, m) in groups if k == kind})
        with open(os.path.join(folder, "Shardplate.mtl"), "w") as f:
            for idx, fn in sorted(tex.items()):
                f.write(f"newmtl Shardplate_{idx}\nKd 1 1 1\nmap_Kd {fn}\n\n")
        total = sum(len(o.data.polygons) for (k, _, _), objs in groups.items() if k == kind for o in objs)
        ratio = min(1.0, BUDGET[kind] / total)
        per_bone = {}
        for (k, bone, mat), objs in groups.items():
            if k != kind: continue
            # join this bone's plates of one material, decimate, keep hard edges crisp
            bpy.ops.object.select_all(action="DESELECT")
            for o in objs: o.select_set(True)
            bpy.context.view_layer.objects.active = objs[0]
            if len(objs) > 1: bpy.ops.object.join()
            o = bpy.context.view_layer.objects.active
            if ratio < 1.0:
                mod = o.modifiers.new("dec", "DECIMATE"); mod.ratio = ratio; mod.use_collapse_triangulate = True
                bpy.ops.object.modifier_apply(modifier=mod.name)
            bpy.ops.object.shade_smooth()
            bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.mesh.set_sharpness_by_angle(angle=0.6)
            bpy.ops.object.mode_set(mode="OBJECT")
            per_bone.setdefault(bone, []).append(o)
        if SKINNED:
            pieces = [o for objs in per_bone.values() for o in objs]
            n = write_skinned(os.path.join(folder, "skin.obj"), kind, pieces)
            print(f"EXPORT {os.path.basename(folder)}/skin.obj: {n} tris")
        else:
            for bone, objs in sorted(per_bone.items()):
                n = write_obj(os.path.join(folder, f"{bone}.obj"), bone, objs)
                print(f"EXPORT {os.path.basename(folder)}/{bone}.obj: {n} tris")

    # ------------------------------------------------------------ icons, with the mod's default colouring
    # Mirrors ModelUtil.GradeTexture with Tint 9EADC7 x brightness 2.4 and Glow 408CFF x 1
    tint = np.array([0x9E, 0xAD, 0xC7]) / 255 * 2.4
    glow = np.array([0x40, 0x8C, 0xFF]) / 255
    for img in list(bpy.data.images):
        if not img.name.startswith("Shardplate_") or img.size[0] == 0: continue
        px = np.array(img.pixels[:], dtype=np.float32).reshape(-1, 4)
        grey = px[:, :3] @ np.array([0.299, 0.587, 0.114])
        t = np.clip((grey - 0.25) / 0.45, 0, 1); hl = t * t * (3 - 2 * t)
        px[:, :3] = np.clip(grey[:, None] * tint + glow * (0.15 + 0.85 * hl)[:, None], 0, 1)
        img.pixels = px.ravel()
    for o in bpy.data.objects:
        if o.type != "MESH": continue
        for slot in o.material_slots:
            m = slot.material
            idx = MAT_INDEX.get(m.name)
            if idx is None: continue
            m.use_nodes = True
            nt = m.node_tree
            tex_node = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
            tex_node.image = bpy.data.images.get(f"Shardplate_{idx}")
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"; scene.display.shading.color_type = "TEXTURE"
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = 256
    scene.world = bpy.data.worlds.new("w")
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.clip_end = 50; scene.camera = cam
    for kind, folder in items.items():
        objs = [o for o in bpy.data.objects if o.type == "MESH" and o.get("kind") == kind]
        for o in bpy.data.objects:
            if o.type == "MESH": o.hide_render = o not in objs
        pts = np.array([o.matrix_world @ v.co for o in objs for v in o.data.vertices])
        lo, hi = pts.min(0), pts.max(0); c = Vector(((lo + hi) / 2).tolist())
        cam.data.ortho_scale = float(max(hi - lo)) * 1.08
        d = Vector((0.45, 1.0, 0.15)).normalized()
        cam.location = c + d * 10; cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, "icon.png")
        bpy.ops.render.render(write_still=True)
        print(f"ICON {folder}")
