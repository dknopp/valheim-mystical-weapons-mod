# Builds Truthguard's low-poly model in Blender, one object per part, with simple per-colour materials.
# Blender space: metres, shield upright (point down -Z, disc up +Z), width along X, front facing -Y
# (Blender's front view). Origin at the shield's centre. Proportions follow references/truthguard.md:
# 1.00 m from the top piece's edge to the point, about 0.57 m wide (Stephen's head-on capture).
# Run inside Blender (Text editor or the MCP), or import and call build().
import bpy, bmesh, math
from mathutils import Vector

COLLECTION = "TG_Model"

# sRGB colours for the preview materials; the bake script turns them into one texture
COLOURS = {
    "TG_Stone":  (0.30, 0.31, 0.33),
    "TG_Runes":  (0.22, 0.30, 0.38),
    "TG_Gold":   (0.78, 0.62, 0.30),
    "TG_Bronze": (0.55, 0.40, 0.22),
    "TG_Silver": (0.82, 0.82, 0.80),
    "TG_Inlay":  (0.36, 0.20, 0.52),
    "TG_Copper": (0.62, 0.32, 0.20),
    "TG_Glow":   (0.70, 0.88, 1.00),
}

def material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    c = tuple(v ** 2.2 for v in COLOURS[name]) + (1,)
    m.diffuse_color = c
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = c
        if name == "TG_Glow":
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

def loft(bm, rings, cap_start=True, cap_end=False, tip=None, mats=None, seg_mats=None, closed=False,
         start_tip=None):
    """Join rings of vertex positions (same count each) into quads; optional end caps and tip points
    (start_tip closes the first ring to a point instead of a cap, tip the last).
    closed: also join the last ring back to the first (a ring-shaped sweep, no caps).
    mats: material index per band between rings (default all 0); the cap and tip use the end bands'.
    seg_mats: material index per segment around the ring instead (segment i runs from point i to i+1)."""
    vs = [[bm.verts.new(p) for p in r] for r in rings]
    n = len(rings[0])
    mats = mats or [0] * (len(rings) - (0 if closed else 1))
    pairs = list(zip(vs, vs[1:])) + ([(vs[-1], vs[0])] if closed else [])
    for k, (a, b) in enumerate(pairs):
        for i in range(n):
            f = bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
            f.material_index = seg_mats[i] if seg_mats else mats[k]
    if start_tip is not None:
        t = bm.verts.new(start_tip)
        for i in range(n):
            bm.faces.new((vs[0][(i + 1) % n], vs[0][i], t)).material_index = mats[0]
    elif cap_start:
        bm.faces.new(list(reversed(vs[0]))).material_index = mats[0]
    if cap_end:
        bm.faces.new(vs[-1]).material_index = mats[-1]
    if tip is not None:
        t = bm.verts.new(tip)
        for i in range(n):
            bm.faces.new((vs[-1][i], vs[-1][(i + 1) % n], t)).material_index = mats[-1]
    return vs

# The whole shield bows forward along its length: the middle sits in front, the top and the point
# curve back. Every part adds bow(z) to its depth so they stay seated on each other.
BOW = 0.08
def bow(z):
    return BOW * (z / 0.5) ** 2

# --- Disc (Ironaya's stone) -----------------------------------------------------------------------
# Two separate 16-sided pieces that turn in opposite directions in the original: an outer rune ring
# and an inner stone face. Each object's origin is the disc's centre and it spins about its local Y,
# so either can be turned on its own later. About half the shield's width, a quarter of the way down.
DISC_Z, DISC_SIDES = 0.22, 16

def disc_centre():
    return Vector((0, bow(DISC_Z), DISC_Z))

def disc_ring(r, y):
    return [Vector((r * math.cos(a), y, r * math.sin(a)))
            for a in (2 * math.pi * (i + 0.5) / DISC_SIDES for i in range(DISC_SIDES))]

# Rune ring: a swept 4-point section (radius, y), y negative = in front. Its sloping front face carries
# the glowing runes (TG_Runes); the rest is stone.
RING_SECTION = [
    (0.098,  0.012),   # inner back
    (0.143,  0.012),   # outer back
    (0.145, -0.048),   # outer front
    (0.100, -0.064),   # inner front: the slope up to here is the rune face
]
RING_BANDS = [0, 0, 1, 0]   # back, outer wall, rune face, inner wall

def build_ring():
    bm = bmesh.new()
    loft(bm, [disc_ring(r, y) for r, y in RING_SECTION], cap_start=False, closed=True, mats=RING_BANDS)
    o = new_object("TG_Ring", bm, ["TG_Stone", "TG_Runes"])
    o.location = disc_centre()
    return o

# Stone face: a puck just inside the ring (a small gap so it can turn), sitting back from the ring's
# front with a shallow dome, and standing out behind the shield.
FACE_ROWS = [(0.094, 0.040), (0.095, -0.050)]   # back, front edge
FACE_DOME = -0.058

def build_face():
    bm = bmesh.new()
    loft(bm, [disc_ring(r, y) for r, y in FACE_ROWS], tip=Vector((0, FACE_DOME, 0)))
    o = new_object("TG_Face", bm, ["TG_Stone"])
    o.location = disc_centre()
    return o

# Collars: each piece has a raised gold band (braided in the texture) hugging the disc just outside
# the ring, then flaring out along the cross-beam to the shield's edge. Built from paired points on
# its inner edge (by the disc and the beam) and outer edge, in the disc's frame (x, z from its
# centre); a 4-point section between each pair, with these depths:
COLLAR_R_IN, COLLAR_R_OUT = 0.150, 0.192
# back, inner front, outer front; the fronts stand proud of the halves' bars where they cross them
COLLAR_Y_BACK, COLLAR_Y_IN, COLLAR_Y_OUT = 0.012, -0.050, -0.044
BEAM_Z, BEAM_HW = 0.025, 0.010     # the cross-beam's height above the disc centre, half its width
COLLAR_ARM_X, COLLAR_ARM_W = 0.265, 0.040   # where the arms end (the shield's edge), their width

def arc_pt(r, deg):
    return (r * math.cos(math.radians(deg)), r * math.sin(math.radians(deg)))

def edge_meet(r, z):
    """Where a circle of radius r meets the horizontal line at height z (right side)."""
    return (math.sqrt(r * r - z * z), z)

def top_collar_half():
    """Inner and outer edges from the top centre to the right arm's end."""
    zi, zo = BEAM_Z + BEAM_HW, BEAM_Z + BEAM_HW + COLLAR_ARM_W
    inner = [arc_pt(COLLAR_R_IN, a) for a in (90, 60, 30)] + [edge_meet(COLLAR_R_IN, zi), (COLLAR_ARM_X, zi)]
    outer = [arc_pt(COLLAR_R_OUT, a) for a in (90, 60, 30)] + [edge_meet(COLLAR_R_OUT, zo), (COLLAR_ARM_X, zo)]
    return inner, outer

def half_collar():
    """Inner and outer edges from the right arm's end, under the cross-beam, round to the centre slot."""
    zi, zo = BEAM_Z - BEAM_HW, BEAM_Z - BEAM_HW - COLLAR_ARM_W
    inner = [(COLLAR_ARM_X, zi), edge_meet(COLLAR_R_IN, zi)] + [arc_pt(COLLAR_R_IN, a) for a in (-20, -45, -70, -84)]
    outer = [(COLLAR_ARM_X, zo), edge_meet(COLLAR_R_OUT, zo)] + [arc_pt(COLLAR_R_OUT, a) for a in (-20, -45, -70, -84)]
    return inner, outer

def collar(bm, inner, outer, side=1, mat=0):
    c = disc_centre()
    rings = [[Vector((side * x, c.y + y, c.z + z)) for (x, z), y in
              ((pi, COLLAR_Y_BACK), (pi, COLLAR_Y_IN), (po, COLLAR_Y_OUT), (po, COLLAR_Y_BACK))]
             for pi, po in zip(inner, outer)]
    loft(bm, rings, cap_end=True, mats=[mat] * (len(rings) - 1))

# --- Top piece ------------------------------------------------------------------------------------
# A separate piece behind the disc's upper half, ending at the cross-beam's gap just above the disc's
# centre: a plain bronze backing plate with layered gold claw plates fanned over it from behind the
# collar (a hook curling up, out and slightly forward at each top corner, two lower tiers per side
# reaching out toward the beam, and a peak in the middle), plus the collar. Grooves go in the texture.
# Everything follows the bow, so the piece curls back toward the top.
# Backing plate: half outline (x, z) from the bottom centre (hidden behind the disc) round to the top
# centre, mirrored for the other side; a flat slab TOP_T either side of TOP_Y.
TOP_OUTLINE = [
    (0.000, 0.255),
    (0.235, 0.255),   # bottom corner by the beam's gap
    (0.260, 0.310),
    (0.220, 0.400),
    (0.120, 0.450),
    (0.000, 0.440),
]
TOP_Y, TOP_T = 0.030, 0.016   # plate centre plane behind the disc; half thickness

# Claw plates: (spine [(x, z, half width)] from the root (behind the collar) out, tip (x, z), front y,
# tip y). A lens section across the spine (edges, front ridge, back), SLAT_T thick, closing to the tip.
# Right side; mirrored for the left. The middle peak (x = 0) is built once.
CLAWS = [
    ([(0.06, 0.30, 0.070), (0.12, 0.42, 0.062), (0.19, 0.485, 0.040), (0.245, 0.470, 0.022)],
     (0.270, 0.420), -0.010, -0.030),   # top-corner hook, curling forward
    ([(0.12, 0.30, 0.055), (0.21, 0.37, 0.045), (0.27, 0.40, 0.026)],
     (0.310, 0.385), -0.004, -0.012),   # middle tier
    ([(0.16, 0.27, 0.045), (0.24, 0.29, 0.036), (0.29, 0.30, 0.020)],
     (0.320, 0.275), 0.002, -0.004),    # lower tier, out past the collar's arm
]
PEAK = ([(0.0, 0.33, 0.075), (0.0, 0.43, 0.048)], (0.0, 0.500), -0.012, -0.016)

def mirrored_loop(half):
    """Full outline from a half that starts and ends on the centre line (x = 0)."""
    return half + [(-x, z) for x, z in reversed(half[1:-1])]

def claw(bm, spine, tip, front, tip_y, side=1, mat=0):
    rings = []
    for k, (x, z, hw) in enumerate(spine):
        nxt = spine[k + 1][:2] if k + 1 < len(spine) else tip
        prv = spine[k - 1][:2] if k > 0 else (x, z)
        d = Vector((nxt[0] - prv[0], nxt[1] - prv[1])).normalized()
        n = Vector((-d.y, d.x)) * hw
        rings.append([Vector((side * px, py + bow(pz), pz)) for px, py, pz in
                      ((x - n.x, front + SLAT_T / 2, z - n.y), (x, front, z),
                       (x + n.x, front + SLAT_T / 2, z + n.y), (x, front + SLAT_T, z))])
    loft(bm, rings, tip=Vector((side * tip[0], tip_y + SLAT_T / 2 + bow(tip[1]), tip[1])),
         mats=[mat] * (len(rings) - 1) if len(rings) > 1 else [mat])

def build_top():
    outline = mirrored_loop(TOP_OUTLINE)
    bm = bmesh.new()
    loft(bm, [[Vector((x, TOP_Y + t + bow(z), z)) for x, z in outline] for t in (-TOP_T, TOP_T)],
         cap_end=True)
    for spine, tip, front, tip_y in CLAWS:
        for side in (1, -1):
            claw(bm, spine, tip, front, tip_y, side, mat=1)
    claw(bm, *PEAK, mat=1)
    inner, outer = top_collar_half()   # over the disc and out along the cross-beam to both edges
    mirror = lambda pts: [(-x, z) for x, z in reversed(pts[1:])]
    collar(bm, mirror(inner) + inner, mirror(outer) + outer, mat=1)
    return new_object("TG_Top", bm, ["TG_Bronze", "TG_Gold"])

# --- Halves ---------------------------------------------------------------------------------------
# Two separate long pieces, mirror images, from under the disc down to the point, with a narrow gap
# between them for the centre beam. Each: a riveted gold bar on the inner edge that ends in a barbed
# shoulder and half the arrowhead point; three silver feather slats outside it, each a step further
# back, their top tips flaring out by the disc and their bottom tips pointed (outer ones shorter); a
# purple cloth band pinned under a round boss, wrapping round the outer edge to the back. Rivets go in
# the texture.
# Built for the right half (x > 0); side=-1 mirrors it. The bar's and inner slat's tops run under the
# disc, inside the ring and face, so they're hidden from both sides (dy shifts a row's depth if needed).
HALF_GAP = 0.012                     # half the gap between the bars (the beam's slot)

# Gold bar rows: (z, outer x, dy); section: inner edge at HALF_GAP, a front face sloping back to the
# outer edge, a flat back
BAR_ROWS = [
    (0.20, 0.055, 0.0),    # under the disc: inside its volume, hidden front and back
    (0.06, 0.058, 0.0),
    (-0.30, 0.065, 0.0),
    (-0.32, 0.090, 0.0),   # barbed shoulder of the point
]
BAR_TIP = (0.006, -0.50)
BAR_FRONT, BAR_EDGE_FRONT, BAR_BACK = -0.040, -0.032, 0.008
# Copper studs down the bar's middle between the boss and the shoulder: low five-sided domes, no base
STUD_X, STUD_ZS = 0.036, (-0.01, -0.084, -0.18, -0.26)
STUD_R, STUD_BASE, STUD_TOP = 0.012, -0.034, -0.045

# Feather slats: (top tip (x, z), rows [(z, centre x, half width, dy)], bottom tip (x, z), front y)
# Top tips end under the collar's arm, below the cross-beam.
SLATS = [
    ((0.12, 0.232), [(0.19, 0.100, 0.036, 0.0), (0.00, 0.095, 0.036, 0.0), (-0.22, 0.088, 0.030, 0.0)],
     (0.105, -0.34), -0.030),
    ((0.19, 0.232), [(0.19, 0.168, 0.040, 0.0), (-0.16, 0.140, 0.035, 0.0)],
     (0.165, -0.27), -0.018),
    ((0.255, 0.232), [(0.19, 0.238, 0.040, 0.0), (-0.10, 0.198, 0.035, 0.0)],
     (0.225, -0.20), -0.006),
]
SLAT_T = 0.012   # front to back

# Cloth band: pinned under the front boss (just below the collar's end), out and down across the
# slats' fronts, round the outer edge and back along the slats' backs to a matching boss behind. Path points (x, z, front y); a flat
# strip of width 2 * STRAP_HW and thickness STRAP_T. Its gold trim and swirl go in the texture.
STRAP = [
    (0.072, 0.005, -0.046),   # under the boss
    (0.190, -0.120, -0.022),  # across the slats' fronts
    (0.250, -0.160, 0.004),   # round the outer edge
    (0.072, 0.005, 0.014),    # back up the slats' backs (clear of them) to behind the front boss
]
STRAP_HW, STRAP_T = 0.022, 0.010
# Bosses pinning each end of the band: low five-sided domes, sunk into the band, open underneath
BOSS_R, BOSS_H, BOSS_DOME = 0.028, 0.022, 0.008   # radius, height off the band, extra at the centre

def half_v(side, x, y, z):
    return Vector((side * x, y + bow(z), z))

def build_half(side, name):
    bm = bmesh.new()
    # gold bar
    rings = [[half_v(side, x, y + dy, z) for x, y in
              ((HALF_GAP, BAR_BACK), (HALF_GAP, BAR_FRONT), (w, BAR_EDGE_FRONT), (w, BAR_BACK))]
             for z, w, dy in BAR_ROWS]
    tx, tz = BAR_TIP
    loft(bm, rings, tip=half_v(side, tx, (BAR_FRONT + BAR_BACK) / 2, tz))
    for sz in STUD_ZS:
        base = [half_v(side, STUD_X + STUD_R * math.cos(t), STUD_BASE, sz + STUD_R * math.sin(t))
                for t in (2 * math.pi / 5 * i for i in range(5))]
        loft(bm, [base], cap_start=False, tip=half_v(side, STUD_X, STUD_TOP, sz), mats=[3])
    # slats: a lens section (inner edge, front ridge, outer edge, back), silver front, gold back
    for (top, rows, bottom, front) in SLATS:
        rings = [[half_v(side, x, y + dy, z) for x, y in
                  ((cx - hw, front + SLAT_T / 2), (cx, front), (cx + hw, front + SLAT_T / 2), (cx, front + SLAT_T))]
                 for z, cx, hw, dy in rows]
        mid = front + SLAT_T / 2
        loft(bm, rings, start_tip=half_v(side, top[0], mid + rows[0][3], top[1]),
             tip=half_v(side, bottom[0], mid, bottom[1]), seg_mats=[1, 1, 0, 0], mats=[1, 1])
    # cloth band: a strip swept along its path, all cloth; its width runs across the first leg
    (ax, az, ay), (bx, bz, _) = STRAP[:2]
    d = Vector((bx - ax, bz - az)).normalized()
    n = Vector((-d.y, d.x)) * STRAP_HW
    rings = [[half_v(side, x + s * n.x, yy, z + s * n.y) for s, yy in ((1, y), (-1, y), (-1, y + STRAP_T), (1, y + STRAP_T))]
             for x, z, y in STRAP]
    loft(bm, rings, cap_end=True, mats=[2] * (len(rings) - 1))
    # bosses: one on the front at the band's start, one on the back at its end (facing back)
    (ex, ez, ey) = STRAP[-1]
    for x, z, y0, facing in ((ax, az, ay + STRAP_T, -1), (ex, ez, ey, 1)):
        rings = [[half_v(side, x + BOSS_R * math.cos(t), y0 + facing * h, z + BOSS_R * math.sin(t))
                  for t in (2 * math.pi / 5 * i for i in range(5))] for h in (0, BOSS_H)]
        loft(bm, rings, cap_start=False, tip=half_v(side, x, y0 + facing * (BOSS_H + BOSS_DOME), z))
    collar(bm, *half_collar(), side)   # in along the cross-beam, then round the disc to the slot
    return new_object(name, bm, ["TG_Gold", "TG_Silver", "TG_Inlay", "TG_Copper"])

def build_half_right():
    return build_half(1, "TG_HalfR")

def build_half_left():
    return build_half(-1, "TG_HalfL")

# --- Glow beams ----------------------------------------------------------------------------------
# The blue-white energy filling the gaps between the pieces: one beam down the centre slot between the
# halves to the point, one across in the gap between the collar arms. Thin bars on their own glow
# material, so in game they can take a see-through glow material with a scrolling flow texture. Their
# hidden ends inside the disc are left open.
CENTRE_BEAM = [0.10, -0.20, -0.49]       # z rows, from under the disc to the point (rows follow the bow)
CENTRE_BEAM_HW, CENTRE_BEAM_Y = 0.008, (-0.030, 0.000)
CROSS_BEAM_X, CROSS_BEAM_Y = 0.30, (-0.035, -0.010)   # out to just past both edges; depth in the disc's frame

def build_beams():
    bm = bmesh.new()
    y0, y1 = CENTRE_BEAM_Y
    loft(bm, [[Vector((x, y + bow(z), z)) for x, y in
               ((-CENTRE_BEAM_HW, y0), (CENTRE_BEAM_HW, y0), (CENTRE_BEAM_HW, y1), (-CENTRE_BEAM_HW, y1))]
              for z in CENTRE_BEAM], cap_start=False, cap_end=True)
    c = disc_centre()
    zc, (y0, y1) = c.z + BEAM_Z, CROSS_BEAM_Y
    hw = BEAM_HW * 0.7
    loft(bm, [[Vector((x, c.y + y, zc + dz)) for dz, y in ((-hw, y0), (hw, y0), (hw, y1), (-hw, y1))]
              for x in (-CROSS_BEAM_X, CROSS_BEAM_X)], cap_end=True)
    return new_object("TG_Beams", bm, ["TG_Glow"])

# --- Build ----------------------------------------------------------------------------------------
PARTS = [build_ring, build_face, build_top, build_half_right, build_half_left, build_beams]

def triangles(objs):
    return sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons)

def build():
    col = bpy.data.collections.get(COLLECTION)
    for o in list(col.objects) if col else []:   # clear parts that no longer exist
        bpy.data.objects.remove(o)
    objs = [f() for f in PARTS]
    return {o.name: triangles([o]) for o in objs} | {"total": triangles(objs)}

if __name__ == "__main__":
    print(build())
