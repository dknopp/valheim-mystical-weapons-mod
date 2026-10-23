# Builds one Warglaive of Azzinoth (low-poly) in Blender, one object per part, with simple per-colour
# materials. The game mirrors it for the left hand.
# Blender space: metres, hand at the origin, grip bar along +Z, fingers along +X. The blade is flat in
# the YZ plane, offset along +X in front of the knuckles, its tips curving toward -Y.
# The OBJ export (-Z forward, Y up) turns this into the mod's convention: grip +Y, fingers +X.
# Proportions follow the Sketchfab reference (shape only): 1.6 m tip to tip, a little shorter than the player.
# Run inside Blender (Text editor or the MCP), or import and call build().
import bpy, bmesh, math
from mathutils import Vector

COLLECTION = "WG_Model"

# sRGB colours for the preview materials; the bake script turns them into one texture
COLOURS = {
    "WG_Fel":    (0.36, 0.85, 0.22),
    "WG_Silver": (0.78, 0.80, 0.84),
    "WG_Gold":   (0.90, 0.74, 0.36),
    "WG_Wrap":   (0.32, 0.20, 0.12),
}

def material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    c = tuple(v ** 2.2 for v in COLOURS[name]) + (1,)
    m.diffuse_color = c
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = c
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

# --- Blade ----------------------------------------------------------------------------------------
# Two separate blades coming out of the hub, their roots hidden inside it. Each has a small horned ear
# on either side near the hub, then a crescent out to its tip with a serrated inner (concave) edge
# and two hooks on its back. Sharp at its outline (x = BLADE_X), raised to +-BLADE_T across its faces
# after a narrow bevel.
# Outline of one blade in drawing units (u along the span, v toward the tips' side), from the root's
# top round to its bottom; mirrored for the other blade. UNIT scales it to metres.
UNIT = 0.00327
BLADE_HALF = [
    (28, 30), (50, 34),                             # root, inside the hub
    (64, 42), (76, 62), (84, 36),                   # upper ear
    (100, 24), (116, 33), (122, 26), (138, 36),     # serrated inner edge
    (146, 28), (164, 35), (172, 30), (190, 39),
    (200, 42), (214, 58), (228, 80), (240, 107),    # up to the tip
    (245, 86), (241, 60), (231, 38), (214, 18),     # the back, from the tip
    (196, 2), (176, -9),
    (156, -24), (150, -11), (132, -8),              # small hook
    (124, -48), (110, -28), (96, -24),              # big hook
    (84, -46), (68, -34),                           # lower ear
    (50, -28), (28, -26),                           # root, inside the hub
]
BLADE_X = 0.077     # blade plane in front of the knuckles
BLADE_T = 0.008     # half thickness across the faces
BLADE_BEVEL = 0.005

def blade_point(u, v, x=BLADE_X):
    return Vector((x, -v * UNIT, u * UNIT))

def build_blade():
    bm = bmesh.new()
    for mirror in (1, -1):
        rim = [bm.verts.new(blade_point(mirror * u, v)) for u, v in BLADE_HALF]
        for verts in (rim, list(reversed(rim))):
            face = bm.faces.new(verts)
            face.normal_update()
            bmesh.ops.inset_individual(bm, faces=[face], thickness=BLADE_BEVEL, depth=BLADE_T)
    return new_object("WG_Blade", bm, ["WG_Fel"])

# --- Hub medallion --------------------------------------------------------------------------------
# A solid round silver boss that both blades come out of. The outer face (+X) has a raised rim, a
# sunken well and a faceted gold five-pointed star rising from it; the inner face (toward the hand)
# is a plain low disc the grip's posts stand on; a short wall joins the two round the edge.
# Rings of HUB_SEGMENTS points, every 4th one on a star point.
# Rows: (radius in drawing units, height above the blade's face in metres, material).
HUB_SEGMENTS = 20
HUB_FRONT = [
    (48, -0.001, 0),
    (46, 0.007, 0),
    (36, 0.009, 0),     # top of the rim
    (32, 0.004, 0),     # the well
]
STAR_OUTER, STAR_INNER = 31, 13
STAR_ROWS = [(1.0, 0.004), (0.92, 0.010)]   # (outline scale, height): the star's sides
STAR_APEX = 0.018
HUB_BACK = [(48, -0.001), (44, 0.006)]

def hub_ring(r, h, side):
    return [Vector((BLADE_X + side * (BLADE_T + h), -r * UNIT * math.sin(a), r * UNIT * math.cos(a)))
            for a in (2 * math.pi * i / HUB_SEGMENTS for i in range(HUB_SEGMENTS))]

def star_radius(i):
    """Star outline on the ring's points: tips on every 4th, valleys halfway, edge midpoints between."""
    k = i % 4
    return STAR_OUTER if k == 0 else STAR_INNER if k == 2 else (STAR_OUTER + STAR_INNER) / 2

def band(bm, a, b, mat):
    n = len(a)
    for i in range(n):
        bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i])).material_index = mat

def build_hub():
    bm = bmesh.new()
    rings = [[bm.verts.new(p) for p in hub_ring(r, h, 1)] for r, h, _ in HUB_FRONT]
    for scale, h in STAR_ROWS:
        ring = hub_ring(1, h, 1)
        rings.append([bm.verts.new(Vector((p.x, p.y * star_radius(i) * scale, p.z * star_radius(i) * scale)))
                      for i, p in enumerate(ring)])
    mats = [row[2] for row in HUB_FRONT] + [1, 1]
    for k, (a, b) in enumerate(zip(rings, rings[1:])):
        band(bm, a, b, mats[k])
    apex = bm.verts.new(Vector((BLADE_X + BLADE_T + STAR_APEX, 0, 0)))
    top = rings[-1]
    for i in range(HUB_SEGMENTS):
        bm.faces.new((top[i], top[(i + 1) % HUB_SEGMENTS], apex)).material_index = 1
    back = [[bm.verts.new(p) for p in hub_ring(r, h, -1)] for r, h in HUB_BACK]
    band(bm, back[0], back[1], 0)
    bm.faces.new(back[-1])
    band(bm, back[0], rings[0], 0)
    return new_object("WG_Hub", bm, ["WG_Silver", "WG_Gold"])

# --- Grip -----------------------------------------------------------------------------------------
# A straight bar across the fist (along Z, through the hand at the origin), wrapped in brown leather
# between silver collars, held off the hub's back by two short tapered silver posts outside the hand.
# Bar rows: (z, radius, material); six-sided all along.
GRIP_SIDES = 6
GRIP_ROWS = [
    (-0.074, 0.009, 0),
    (-0.070, 0.014, 0),
    (-0.054, 0.014, 0),
    (-0.050, 0.012, 1),     # leather wrap, where the hand goes
    (0.050, 0.012, 0),
    (0.054, 0.014, 0),
    (0.070, 0.014, 0),
    (0.074, 0.009, 0),
]
POST_Z = 0.062
POST_ROWS = [(0.006, 0.008), (0.040, 0.006), (BLADE_X - BLADE_T, 0.009)]   # (x, radius) bar to hub

def ring_around(axis, centre, r, sides):
    """Points round an axis ("x" or "z") at centre, one face flat toward the blade."""
    pts = []
    for i in range(sides):
        a = 2 * math.pi * (i + 0.5) / sides
        c, s = r * math.cos(a), r * math.sin(a)
        pts.append(centre + (Vector((c, s, 0)) if axis == "z" else Vector((0, s, c))))
    return pts

def build_grip():
    bm = bmesh.new()
    bar = [[bm.verts.new(p) for p in ring_around("z", Vector((0, 0, z)), r, GRIP_SIDES)]
           for z, r, _ in GRIP_ROWS]
    for k, (a, b) in enumerate(zip(bar, bar[1:])):
        band(bm, a, b, GRIP_ROWS[k][2])
    bm.faces.new(list(reversed(bar[0])))
    bm.faces.new(bar[-1])
    for z in (-POST_Z, POST_Z):
        post = [[bm.verts.new(p) for p in ring_around("x", Vector((x, 0, z)), r, GRIP_SIDES)]
                for x, r in POST_ROWS]
        for a, b in zip(post, post[1:]):
            band(bm, a, b, 0)
    return new_object("WG_Grip", bm, ["WG_Silver", "WG_Wrap"])

PARTS = [build_blade, build_hub, build_grip]

def build():
    for part in PARTS:
        part()

def triangles():
    col = bpy.data.collections[COLLECTION]
    return {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in col.objects}

if __name__ == "__main__":
    build()
