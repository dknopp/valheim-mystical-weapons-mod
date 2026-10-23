# Builds one Warglaive of Azzinoth (low-poly) in Blender, one object per part, with simple per-colour
# materials. The game mirrors it for the left hand.
# Blender space: metres, hand at the origin, grip bar along +Z, fingers along +X. The blade is flat in
# the YZ plane, offset along +X in front of the knuckles, its tips curving toward -Y.
# The OBJ export (-Z forward, Y up) turns this into the mod's convention: grip +Y, fingers +X.
# Proportions follow the Sketchfab reference (shape only): 1.6 m tip to tip, a little shorter than the player.
# Run inside Blender (Text editor or the MCP), or import and call build().
import bpy, bmesh, math
from mathutils import Vector, geometry

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
# and two hooks on its back. Sharp at its outline (x = BLADE_X); a wide bevel rises to a flat plateau
# at +-BLADE_T down the middle of each face, with a V groove along it. Where the blade is narrower
# than two bevels (ears, hooks, tip) the plateau shrinks to a lower ridge.
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
# The groove's centre line in drawing units, from inside the hub to short of the tip
GROOVE = [(48, 2), (80, 1), (110, 3), (140, 7), (170, 12), (196, 21), (214, 34), (226, 52)]
BLADE_X = 0.060     # blade plane in front of the knuckles
BLADE_T = 0.010     # half thickness at the plateau
BEVEL_W = 12        # bevel width in drawing units, edge to plateau
GROOVE_W = 1.4      # groove half width in drawing units
GROOVE_D = 0.0035   # groove depth below the plateau

def blade_point(u, v, x=BLADE_X):
    return Vector((x, -v * UNIT, u * UNIT))

def plateau_outline(rim):
    """Each outline point moved inward along its corner's bisector by the bevel width, or by less
    where the blade is narrow; returns (points, height fraction) per outline point."""
    n = len(rim)
    out = []
    for i, p in enumerate(rim):
        a, b = rim[i - 1], rim[(i + 1) % n]
        e1 = (p - a).normalized(); e2 = (b - p).normalized()
        d = Vector((e1.y + e2.y, -(e1.x + e2.x)))   # right normals summed; inward for a clockwise loop
        if d.length < 1e-6:
            d = Vector((e1.y, -e1.x))
        d.normalize()
        # Distance across the blade from here, by a ray against the other edges
        width = math.inf
        for j in range(n):
            q, r = rim[j], rim[(j + 1) % n]
            if i in (j, (j + 1) % n):
                continue
            s = r - q
            den = d.x * s.y - d.y * s.x
            if abs(den) < 1e-9:
                continue
            t = ((q.x - p.x) * s.y - (q.y - p.y) * s.x) / den
            k = ((q.x - p.x) * d.y - (q.y - p.y) * d.x) / den
            if t > 1e-6 and 0 <= k <= 1:
                width = min(width, t)
        inset = min(BEVEL_W, 0.42 * width)
        out.append((p + d * inset, inset / BEVEL_W))
    return out

def groove_lines():
    """The groove's centre line and its two shoulders, the shoulders closing to it at both ends."""
    pts = [Vector(g) for g in GROOVE]
    centre, left, right = [], [], []
    for i, p in enumerate(pts):
        a, b = pts[max(i - 1, 0)], pts[min(i + 1, len(pts) - 1)]
        t = (b - a).normalized()
        side = Vector((-t.y, t.x)) * GROOVE_W * (0 if i == len(pts) - 1 else 1)
        centre.append(p); left.append(p + side); right.append(p - side)
    return centre, left, right

def blade_face(bm, mirror, side, rim_verts):
    """One face of one blade: a constrained triangulation of the outline, plateau and groove, each
    point raised to its height, sharing the outline's verts with the other face."""
    rim = [Vector(p) for p in BLADE_HALF]
    if signed_area(rim) > 0:
        rim.reverse()   # clockwise in (u, v) so plateau_outline's normals point inward
        flipped = True
    else:
        flipped = False
    plateau = plateau_outline(rim)
    centre, left, right = groove_lines()
    coords = rim + [p for p, _ in plateau] + centre + left[:-1] + right[:-1]
    heights = [None] * len(rim) + [BLADE_T * f for _, f in plateau] + \
              [BLADE_T - GROOVE_D] * len(centre) + [BLADE_T] * (2 * (len(left) - 1))
    nr, nc = len(rim), len(centre)
    edges = [(nr + i, nr + (i + 1) % nr) for i in range(nr)]
    c0 = 2 * nr; l0 = c0 + nc; r0 = l0 + nc - 1
    for line in (range(c0, c0 + nc), list(range(l0, l0 + nc - 1)) + [c0 + nc - 1],
                 list(range(r0, r0 + nc - 1)) + [c0 + nc - 1]):
        line = list(line)
        edges += list(zip(line, line[1:]))
    vs, _, fs, orig, _, _ = geometry.delaunay_2d_cdt(
        [Vector((c.x, c.y)) for c in coords], edges, [list(range(nr))], 1, 1e-6)
    made = []
    for k, v in enumerate(vs):
        src = orig[k][0] if orig[k] else None
        if src is not None and src < nr:
            i = src if not flipped else nr - 1 - src
            made.append(rim_verts[i])
        else:
            h = heights[src] if src is not None else BLADE_T
            made.append(bm.verts.new(blade_point(mirror * v.x, v.y, BLADE_X + side * h)))
    for f in fs:
        bm.faces.new([made[i] for i in f])

def signed_area(poly):
    return sum(a.x * b.y - b.x * a.y for a, b in zip(poly, poly[1:] + poly[:1])) / 2

def build_blade():
    bm = bmesh.new()
    for mirror in (1, -1):
        rim = [bm.verts.new(blade_point(mirror * u, v)) for u, v in BLADE_HALF]
        for side in (1, -1):
            blade_face(bm, mirror, side, rim)
    return new_object("WG_Blade", bm, ["WG_Fel"])

# --- Hub medallion --------------------------------------------------------------------------------
# A solid shield-shaped boss that both blades come out of, its top toward the blades' tips and its
# point toward their backs. The outer face (+X) has a raised gold rim, a sunken well, a faceted gold
# five-pointed star rising from it (one point up) and three silver spikes radiating from behind the
# star's valleys: two up to the shield's top corners, one down to its point. The inner face (toward
# the hand) is a plain low plate the grip's posts stand on; a short wall joins the two round the edge.
# Rings of HUB_SEGMENTS points, 18 degrees apart from the blade's +Z; ring 5 points at the top.
# Rows: (radius in drawing units, height above the blade's face in metres, material).
HUB_SEGMENTS = 20
HUB_TOP = 5
HUB_FRONT = [
    (48, -0.001, 1),
    (46, 0.007, 1),
    (36, 0.009, 1),     # top of the rim
    (32, 0.004, 0),     # the well
]
# Shield outline: radius scale on each ring point, from the top (0) round to the point (10)
# (about as wide as it is tall: square shoulders at the top, straight sides, a short point)
SHIELD = [1.08, 0.94, 1.22, 1.17, 1.05, 1.00, 1.00, 0.99, 1.00, 1.03, 1.15]
STAR_OUTER, STAR_INNER = 31, 13
STAR_ROWS = [(1.0, 0.004), (0.92, 0.010)]   # (outline scale, height): the star's sides
STAR_APEX = 0.018
HUB_BACK = [(48, -0.001), (44, 0.006)]
# Spikes: the ring points they run along (the star's valleys), and their profile along that line:
# (distance from the centre in drawing units, half width, height of the ridge, height of the sides).
# They start in the valley, clear of the star's points, and their tips rest on the rim.
SPIKE_AT = [HUB_TOP - 2, HUB_TOP + 2, HUB_TOP + 10]
SPIKE_ROWS = [(15, 0, 0.007, 0.007), (25, 7, 0.016, 0.005), (36, 5, 0.017, 0.010)]
SPIKE_TIP = 0.94    # tip at this share of the shield's outer radius, height 0.012

def shield(i):
    j = (i - HUB_TOP) % HUB_SEGMENTS
    return SHIELD[min(j, HUB_SEGMENTS - j)]

def ring_angle(i):
    return 2 * math.pi * i / HUB_SEGMENTS

def hub_point(r, h, side, a):
    return Vector((BLADE_X + side * (BLADE_T + h), -r * UNIT * math.sin(a), r * UNIT * math.cos(a)))

def hub_ring(r, h, side, shaped=True):
    return [hub_point(r * (shield(i) if shaped else 1), h, side, ring_angle(i)) for i in range(HUB_SEGMENTS)]

def star_radius(i):
    """Star outline on the ring's points: tips on every 4th (one at the top), valleys halfway, edge
    midpoints between."""
    k = (i - HUB_TOP) % 4
    return STAR_OUTER if k == 0 else STAR_INNER if k == 2 else (STAR_OUTER + STAR_INNER) / 2

def build_spike(bm, i):
    a = ring_angle(i)
    side_dir = Vector((0, -math.cos(a), -math.sin(a))) * UNIT   # across the spike, in the face
    rows = []
    for s, w, ridge, low in SPIKE_ROWS:
        c = hub_point(s, 0, 1, a)
        rows.append((c + Vector((ridge, 0, 0)), c + Vector((low, 0, 0)) + side_dir * w,
                     c + Vector((low, 0, 0)) - side_dir * w))
    inner = bm.verts.new(rows[0][0])
    tip = bm.verts.new(hub_point(48 * shield(i) * SPIKE_TIP, 0.012, 1, a))
    rows = [[bm.verts.new(p) for p in row] for row in rows[1:]]
    # Three long strips (two sloping sides and the underside) from the inner point to the tip
    for ridge, edge in ((0, 1), (2, 0), (1, 2)):
        line_a = [inner] + [r[ridge] for r in rows] + [tip]
        line_b = [inner] + [r[edge] for r in rows] + [tip]
        bm.faces.new((line_b[0], line_b[1], line_a[1]))
        for k in range(1, len(rows)):
            bm.faces.new((line_b[k], line_b[k + 1], line_a[k + 1], line_a[k]))
        bm.faces.new((line_b[-2], tip, line_a[-2]))

def band(bm, a, b, mat):
    n = len(a)
    for i in range(n):
        bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i])).material_index = mat

def build_hub():
    bm = bmesh.new()
    rings = [[bm.verts.new(p) for p in hub_ring(r, h, 1)] for r, h, _ in HUB_FRONT]
    for scale, h in STAR_ROWS:
        ring = hub_ring(1, h, 1, shaped=False)
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
    for i in SPIKE_AT:
        build_spike(bm, i)
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
POST_ROWS = [(0.006, 0.008), ((BLADE_X - BLADE_T) / 2, 0.006), (BLADE_X - BLADE_T, 0.009)]   # (x, radius) bar to hub

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
