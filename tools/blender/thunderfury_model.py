# Builds Thunderfury's low-poly model in Blender, one object per part, with simple per-colour materials.
# Blender space: metres, hand at the origin, grip along +Z, blade flat in X (width) and thin in Y.
# Proportions follow our reference sheet (1.30 m from pommel tip to blade tip).
# Run inside Blender (Text editor or the MCP), or import and call build().
import bpy, bmesh, math
from mathutils import Vector

COLLECTION = "TF_Model"

# sRGB colours for the preview materials; the bake script turns them into one texture
COLOURS = {
    "TF_Silver":   (0.78, 0.82, 0.88),
    "TF_Fuller":   (0.06, 0.12, 0.38),
    "TF_Gunmetal": (0.20, 0.21, 0.25),
    "TF_Wrap":     (0.08, 0.14, 0.40),
    "TF_Gold":     (0.93, 0.84, 0.62),
    "TF_Orb":      (0.80, 0.90, 1.00),
}

def material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    c = tuple(v ** 2.2 for v in COLOURS[name]) + (1,)
    m.diffuse_color = c
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = c
        if name == "TF_Orb":
            bsdf.inputs["Emission Color"].default_value = c
            bsdf.inputs["Emission Strength"].default_value = 3.0
    return m

def new_object(name, bm, mats):
    col = bpy.data.collections.get(COLLECTION)
    if not col:
        col = bpy.data.collections.new(COLLECTION)
        bpy.context.scene.collection.children.link(col)
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old)
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    for m in mats:
        me.materials.append(material(m))
    obj = bpy.data.objects.new(name, me)
    col.objects.link(obj)
    return obj

def loft(bm, rings, cap_start=True, tip=None, mat=0):
    """Join rings of vertex positions (same count each) into quads; optional end cap and tip point."""
    vs = [[bm.verts.new(p) for p in r] for r in rings]
    faces = []
    n = len(rings[0])
    for a, b in zip(vs, vs[1:]):
        for i in range(n):
            faces.append(bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i])))
    if cap_start:
        faces.append(bm.faces.new(list(reversed(vs[0]))))
    if tip is not None:
        t = bm.verts.new(tip)
        for i in range(n):
            faces.append(bm.faces.new((vs[-1][i], vs[-1][(i + 1) % n], t)))
    for f in faces:
        f.material_index = mat
    return vs

# --- Blade ----------------------------------------------------------------------------------------
# Two separate prongs with an open slot between them all the way to their points; the orb sits at
# the slot's root, in an open hole through the base. Each prong's section: a thin outer edge at w, a bevel up to a flat face (half
# thickness t, where the blue texture goes) and a flat inner wall at the slot's half width g.
# Rows: (z, w, t, g). The prongs come out of the base's wings at z ~0.474, a step narrower (the notch).
PRONG_ROWS = [
    (0.450, 0.060, 0.012, 0.020),
    (0.490, 0.054, 0.011, 0.010),
    (0.820, 0.030, 0.007, 0.006),
]
PRONG_TIP = (0.009, 1.007)                                    # (x, z) of each prong's point
def prong_ring(z, w, t, g, side):
    m = max(0.45 * w, g + 0.004)
    return [Vector((side * x, y, z)) for x, y in ((w, 0), (m, t), (g, t), (g, -t), (m, -t))]

def build_blade():
    bm = bmesh.new()
    for side in (1, -1):
        loft(bm, [prong_ring(*row, side) for row in PRONG_ROWS],
             tip=Vector((side * PRONG_TIP[0], 0, PRONG_TIP[1])))
    return new_object("TF_Blade", bm, ["TF_Silver"])

# --- Spiky base + orb -----------------------------------------------------------------------------
# A U-shaped plate around the blade's root: jagged points sweeping back toward the grip, a big wing
# on each side whose edge runs forward into the blade's notch, and an open oval hole in the middle
# (opening into the blade's slot) where the orb of crackling lightning floats.
# Thin at its edges (y = 0), thick across its faces (an inner loop at +-BASE_T).
# Half outline (x, z), from the collar round the wing and down the hole's edge; mirrored for the other
# side. BASE_INNER is the matching inner loop, point for point.
BASE_OUTLINE = [
    (0.022, 0.227),
    (0.046, 0.250),
    (0.090, 0.226),   # small spike
    (0.066, 0.270),
    (0.110, 0.258),   # middle spike
    (0.084, 0.300),
    (0.128, 0.296),   # wing tip
    (0.072, 0.474),   # wing meets the blade's notch
    (0.022, 0.455),   # the hole's mouth, where the blade's slot starts
    (0.036, 0.428),   # the hole's edge, down to its bottom
    (0.040, 0.398),
    (0.032, 0.368),
    (0.012, 0.352),
]
BASE_INNER = [
    (0.012, 0.240), (0.030, 0.262), (0.050, 0.258), (0.046, 0.280), (0.062, 0.286), (0.056, 0.306),
    (0.070, 0.320), (0.056, 0.440), (0.034, 0.450), (0.048, 0.428), (0.052, 0.398), (0.044, 0.358),
    (0.014, 0.338),
]
BASE_T = 0.018
ORB_Z, ORB_R, ORB_DEPTH = 0.402, 0.028, 0.028   # a round ball floating in the hole

def mirrored(half, y):
    pts = [Vector((x, y, z)) for x, z in half]
    return pts + [Vector((-p.x, y, p.z)) for p in reversed(pts)]

def build_base():
    bm = bmesh.new()
    outer_v = [bm.verts.new(p) for p in mirrored(BASE_OUTLINE, 0)]
    front_v = [bm.verts.new(p) for p in mirrored(BASE_INNER, BASE_T)]
    back_v = [bm.verts.new(p) for p in mirrored(BASE_INNER, -BASE_T)]
    n = len(outer_v)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((outer_v[i], outer_v[j], front_v[j], front_v[i]))
        bm.faces.new((outer_v[j], outer_v[i], back_v[i], back_v[j]))
    bm.faces.new(front_v)
    bm.faces.new(list(reversed(back_v)))
    return new_object("TF_Base", bm, ["TF_Silver"])

def build_orb():
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=5, radius=ORB_R)
    for v in bm.verts:
        v.co = Vector((v.co.x, v.co.y * ORB_DEPTH / ORB_R, v.co.z + ORB_Z))
    return new_object("TF_Orb", bm, ["TF_Orb"])

# --- Collar ---------------------------------------------------------------------------------------
# A chunky dark steel block in two segments (a groove between them), on an 8-sided section with flat
# faces front, back and on both edges; a short blue neck where it meets the base. Two pale gold horn
# spikes on its top edge (+X), angled forward toward the blade. Rows: (z, rx, ry, material).
def oct_ring(z, rx, ry, start=22.5):
    return [Vector((rx * math.cos(a), ry * math.sin(a), z))
            for a in (math.radians(start + 45 * i) for i in range(8))]

COLLAR_ROWS = [
    (0.097, 0.021, 0.019, 0),
    (0.106, 0.030, 0.027, 0),
    (0.152, 0.030, 0.027, 0),
    (0.160, 0.025, 0.023, 0),   # groove between the segments
    (0.168, 0.030, 0.027, 0),
    (0.208, 0.030, 0.027, 0),
    (0.214, 0.022, 0.020, 1),   # blue neck into the base
    (0.230, 0.022, 0.020, 1),
]
# Each spike: base centre z, tip (x, z). The base is a diamond sunk into the collar's top face.
COLLAR_SPIKES = [(0.128, (0.068, 0.144)), (0.186, (0.070, 0.202))]
SPIKE_R = 0.011

def horn(bm, base, tip, r, mat):
    """A 4-sided horn from a diamond base to a point, with a slight forward curve at its middle."""
    axis = (tip - base).normalized()
    side = Vector((0, 1, 0))
    up = axis.cross(side).normalized()
    mid = base.lerp(tip, 0.5) + Vector((0, 0, 0.006))
    ring = lambda c, s: [c + side * s, c + up * s, c - side * s, c - up * s]
    loft(bm, [ring(base, r), ring(mid, r * 0.6)], cap_start=False, tip=tip, mat=mat)

def build_collar():
    bm = bmesh.new()
    rows = COLLAR_ROWS
    vs = [[bm.verts.new(p) for p in oct_ring(z, rx, ry)] for z, rx, ry, _ in rows]
    for k, (a, b) in enumerate(zip(vs, vs[1:])):
        mat = rows[k + 1][3] if rows[k][3] == rows[k + 1][3] else rows[k][3]
        for i in range(8):
            f = bm.faces.new((a[i], a[(i + 1) % 8], b[(i + 1) % 8], b[i]))
            f.material_index = mat
    bm.faces.new(list(reversed(vs[0])))
    for z, (tx, tz) in COLLAR_SPIKES:
        horn(bm, Vector((0.024, 0, z)), Vector((tx, 0, tz)), SPIKE_R, 2)
    return new_object("TF_Collar", bm, ["TF_Gunmetal", "TF_Wrap", "TF_Gold"])

def lathe(bm, rows, start=22.5, cap=True):
    """Join 8-sided rings (z, rx, ry, material) into a solid; optional cap on the first ring."""
    vs = [[bm.verts.new(p) for p in oct_ring(z, rx, ry, start)] for z, rx, ry, _ in rows]
    for k, (a, b) in enumerate(zip(vs, vs[1:])):
        for i in range(8):
            f = bm.faces.new((a[i], a[(i + 1) % 8], b[(i + 1) % 8], b[i]))
            f.material_index = rows[k][3]
    if cap:
        bm.faces.new(list(reversed(vs[0])))
    return vs

# --- Grip -----------------------------------------------------------------------------------------
# A thin 8-sided bar, ends hidden in the collar and pommel. The silver spiral is in the texture.
GRIP_ROWS = [(-0.102, 0.019, 0.019, 0), (0.102, 0.019, 0.019, 0)]

def build_grip():
    bm = bmesh.new()
    lathe(bm, GRIP_ROWS, cap=False)
    return new_object("TF_Grip", bm, ["TF_Wrap"])

# --- Pommel ---------------------------------------------------------------------------------------
# A chunky dark steel block (corners at front, back and both edges, so it reads as a diamond end-on),
# a pale gold cone pointing straight back, and one gold horn angled back and out on the bottom edge
# (-X), opposite the collar's spikes.
POMMEL_ROWS = [
    (-0.098, 0.022, 0.020, 0),
    (-0.110, 0.038, 0.033, 0),
    (-0.186, 0.038, 0.033, 0),
    (-0.200, 0.022, 0.020, 0),
]
POMMEL_CONE = (-0.198, 0.019, -0.290)   # base z, base radius, tip z
POMMEL_SPIKE = (-0.150, (-0.084, -0.176))

def build_pommel():
    bm = bmesh.new()
    vs = lathe(bm, POMMEL_ROWS, start=0)
    bm.faces.new(vs[-1])
    cone = [Vector((POMMEL_CONE[1] * math.cos(a), POMMEL_CONE[1] * math.sin(a), POMMEL_CONE[0]))
            for a in (math.radians(45 * i) for i in range(8))]
    loft(bm, [cone], cap_start=False, tip=Vector((0, 0, POMMEL_CONE[2])), mat=1)
    z, (tx, tz) = POMMEL_SPIKE
    horn(bm, Vector((-0.030, 0, z)), Vector((tx, 0, tz)), 0.013, 1)
    return new_object("TF_Pommel", bm, ["TF_Gunmetal", "TF_Gold"])

PARTS = {"blade": build_blade, "base": build_base, "orb": build_orb, "collar": build_collar,
         "grip": build_grip, "pommel": build_pommel}

def build(names=None):
    return [PARTS[n]() for n in (names or PARTS)]

def triangles(objs=None):
    col = bpy.data.collections.get(COLLECTION)
    objs = objs or (col.objects if col else [])
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)
