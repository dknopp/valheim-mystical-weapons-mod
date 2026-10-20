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
# A flat hexagonal section: thin edges at +-w, a flat ridge (where the fuller texture goes) of half
# width 0.45w and half thickness t. Rows: (z, w, t). The step at z 0.47-0.49 is the notch in the
# outer edge just past the orb.
BLADE_ROWS = [
    (0.340, 0.050, 0.010),
    (0.390, 0.062, 0.010),
    (0.470, 0.066, 0.009),
    (0.490, 0.054, 0.008),
    (0.600, 0.050, 0.007),
    (0.820, 0.030, 0.005),
]
BLADE_TIP = 1.007

def blade_ring(z, w, t):
    r = 0.45
    return [Vector(p) for p in ((w, 0, z), (r * w, t, z), (-r * w, t, z),
                                (-w, 0, z), (-r * w, -t, z), (r * w, -t, z))]

def build_blade():
    bm = bmesh.new()
    loft(bm, [blade_ring(*row) for row in BLADE_ROWS], tip=Vector((0, 0, BLADE_TIP)))
    return new_object("TF_Blade", bm, ["TF_Silver"])

PARTS = {"blade": build_blade}

def build(names=None):
    return [PARTS[n]() for n in (names or PARTS)]

def triangles(objs=None):
    col = bpy.data.collections.get(COLLECTION)
    objs = objs or (col.objects if col else [])
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)
