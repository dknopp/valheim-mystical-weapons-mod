# Builds the Scroll of Hearth Portal (low-poly) in Blender: a hand-rolled parchment sheet pinched into a
# bowtie by a wide cross-wrapped cord band, its ends flaring and curling outward, a loose outer flap
# peeling up at one end, a knot with two tails, and a wax seal pressed where the cords cross.
# Silhouette inspired by "Scroll (game ready asset)" by Pixel Life (Sketchfab, CC BY 4.0); built from
# scratch here, none of its geometry is used.
# Blender space: metres, the roll's axis along +Z, centred on the origin. The OBJ export (-Z forward,
# Y up) turns this into the mod's convention: length along +Y.
# The sheet is a single surface: the mod draws every face from both sides.
# Run inside Blender (Text editor or the MCP), or import and call build().
import bpy, bmesh, math, random
from mathutils import Vector

COLLECTION = "PS_Model"
HALF = 0.105          # half the roll's length
R_WAIST, R_END = 0.012, 0.019   # outer radius under the band and at the ends
BAND = 0.023          # half the cord band's length
TURNS = 2.0           # turns of the spiral before the flap
INNER = 0.35          # innermost turn's radius as a fraction of the outer
FLAP_TURN = 0.22      # last part of a turn that lifts off as the loose flap
CURL = 0.0035         # how far the outer turns curl outward at the ends

COLOURS = {
    "PS_Parchment": (0.86, 0.78, 0.60),
    "PS_Cord":      (0.45, 0.30, 0.18),
    "PS_Wax":       (0.62, 0.10, 0.10),
}

def material(name, grey=False):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    c = (0.55, 0.55, 0.55) if grey else tuple(v ** 2.2 for v in COLOURS[name])
    m.diffuse_color = c + (1,)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = c + (1,)
        bsdf.inputs["Roughness"].default_value = 0.8
    return m

def new_object(name, bm, mat, grey, smooth=True):
    col = bpy.data.collections.get(COLLECTION)
    if not col:
        col = bpy.data.collections.new(COLLECTION)
        bpy.context.scene.collection.children.link(col)
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old)
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = smooth
    me.materials.append(material(mat, grey))
    obj = bpy.data.objects.new(name, me)
    col.objects.link(obj)
    return obj

def tube(bm, path, radius, sides, caps=True):
    """A tube along a list of points; radius is a number or one per point."""
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        a = t.orthogonal().normalized()
        b = t.cross(a)
        r = radius[i] if isinstance(radius, (list, tuple)) else radius
        rings.append([bm.verts.new(p + (a * math.cos(k * 2 * math.pi / sides) + b * math.sin(k * 2 * math.pi / sides)) * r)
                      for k in range(sides)])
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(sides):
            bm.faces.new((r0[k], r0[(k + 1) % sides], r1[(k + 1) % sides], r1[k]))
    if caps:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])

def profile(z):
    """Outer radius of the roll at height z: flat under the band, flaring to the ends."""
    t = max(0.0, (abs(z) - BAND) / (HALF - BAND))
    return R_WAIST + (R_END - R_WAIST) * t ** 0.8

# --- Parchment ------------------------------------------------------------------------------------
# A spiral sheet whose radius follows the bowtie profile. The outer turns curl outward at the ends, the
# cut edges wander, and the last part of the sheet is a flap that is held under the band and peels up
# toward the top end.
STEPS_PER_TURN = 12
ROWS = [-1.0, -0.82, -0.6, -0.38, -BAND / HALF, BAND / HALF, 0.38, 0.6, 0.82, 1.0]

def build_parchment(grey):
    rnd = random.Random(7)
    bm = bmesh.new()
    total = TURNS + FLAP_TURN
    n = int(total * STEPS_PER_TURN)
    wob = [(rnd.uniform(0, 6.28), rnd.uniform(0, 6.28)) for _ in range(2)]
    grid = []
    for i in range(n + 1):
        u = total * i / n
        ang = u * 2 * math.pi
        layer = min(1.0, INNER + (1 - INNER) * u / TURNS)   # fraction of the outer radius
        col = []
        for v in ROWS:
            z = v * HALF
            if abs(v) == 1.0:
                ph, ph2 = wob[0 if v < 0 else 1]
                # inner turns stand a little proud at the bottom end, sink at the top
                tele = (1 - u / total) * 0.005 * (-1 if v < 0 else 0.4)
                z += math.copysign(1, v) * (0.004 * math.sin(u * 1.7 + ph) + 0.002 * math.sin(u * 4.9 + ph2)
                                            + rnd.uniform(-0.0008, 0.0008)) + tele
            r = profile(z) * layer
            # curl: the outer turns roll outward over the last stretch of each end
            e = max(0.0, (abs(v) - 0.7) / 0.3)
            r += CURL * e * e * (u / total) ** 2
            if u > TURNS:
                f = (u - TURNS) / FLAP_TURN
                lift = min(1.0, max(0.0, (z - BAND) / 0.06))
                r += 0.006 * f ** 1.5 * lift
            col.append(bm.verts.new((r * math.cos(ang), r * math.sin(ang), z)))
        grid.append(col)
    for a, b in zip(grid, grid[1:]):
        for j in range(len(ROWS) - 1):
            bm.faces.new((a[j], b[j], b[j + 1], a[j + 1]))
    return new_object("PS_Parchment", bm, "PS_Parchment", grey)

# --- Cord -----------------------------------------------------------------------------------------
# The band is one sleeve with a ridge per wrap (a pitch on the wraps left a sawtooth seam at this low count).
# Over it, two cords cross in an X on the front; the knot sits at the band's lower edge with two tails.
CORD_R = 0.0028
WRAPS = 7
SLEEVE_SIDES = 10
FRONT = math.radians(-90)   # the side the X, seal and knot face (Blender -Y)

def build_cord(grey):
    bm = bmesh.new()
    rows = WRAPS * 2 + 1
    rings = []
    for j in range(rows):
        base = -BAND + 2 * BAND * j / (rows - 1)
        ridge = CORD_R * (1.0 if j % 2 else 0.35)
        ring = []
        for k in range(SLEEVE_SIDES):
            a = k * 2 * math.pi / SLEEVE_SIDES
            z = base
            r = R_WAIST + ridge
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(SLEEVE_SIDES):
            bm.faces.new((r0[k], r0[(k + 1) % SLEEVE_SIDES], r1[(k + 1) % SLEEVE_SIDES], r1[k]))

    # the X: two cords over the front half, corner to corner of the band
    for sgn in (1, -1):
        pts = []
        for k in range(7):
            t = k / 6
            a = FRONT + math.radians(-80 + 160 * t)
            rr = R_WAIST + CORD_R * (0.6 + 1.6 * math.sin(t * math.pi))   # ends tuck under the wraps
            pts.append(Vector((rr * math.cos(a), rr * math.sin(a), sgn * BAND * 0.95 * (1 - 2 * t))))
        tube(bm, pts, CORD_R * 1.1, 4)

    # knot at the band's lower edge, a little off the front, and its two tails down along the roll
    a = FRONT + math.radians(28)
    out = Vector((math.cos(a), math.sin(a), 0))
    side = Vector((0, 0, 1)).cross(out)
    knot = out * (R_WAIST + CORD_R * 2.6) + Vector((0, 0, -BAND - 0.002))
    tube(bm, [knot - side * 0.005 + Vector((0, 0, 0.002)), knot + out * 0.002, knot + side * 0.005 - Vector((0, 0, 0.002))],
         [0.0026, 0.004, 0.0026], 6)
    for sgn, ln in ((1, 0.048), (-1, 0.036)):
        pts = []
        for k in range(6):
            t = k / 5
            z = knot.z - ln * t
            r = profile(z) + CORD_R * 1.2 + 0.003 * math.sin(t * math.pi)   # lies on the flaring roll
            aa = a + sgn * (0.12 + 0.25 * t * t)
            pts.append(Vector((r * math.cos(aa), r * math.sin(aa), z)))
        pts[0] = knot + side * sgn * 0.002
        tube(bm, pts, [CORD_R * (0.95 + 0.4 * (k == 5)) for k in range(6)], 4)  # frayed tip
    return new_object("PS_Cord", bm, "PS_Cord", grey)

# --- Wax seal -------------------------------------------------------------------------------------
# A flat round of wax pressed over the X's crossing, in the middle of the band's front, bent round the
# band so its edge sits down on the cord. A raised, uneven rim around a flat stamped face; the rune
# (Othala, "home") is pressed into the face by the texture.
SEAL_R = 0.0135        # radius of the wax round: oversized so it reads in the 128 px inventory icon
SEAL_BASE = R_WAIST + CORD_R * 2.6   # radius it's bent around (over the X cords)
SEAL_H = 0.0022        # height of its rim above the base
STAMP = 0.66           # the stamped face's radius as a fraction of SEAL_R

def build_seal(grey):
    bm = bmesh.new()
    rnd = random.Random(3)
    def vert(s, t, h):
        """s across the band (along its tangent), t along the roll, h out from the base radius."""
        a = FRONT + s / SEAL_BASE
        r = SEAL_BASE + h
        return bm.verts.new((r * math.cos(a), r * math.sin(a), t))
    sides = 12
    rings = []
    for k in range(sides):
        a = k * 2 * math.pi / sides
        c, sn = math.cos(a), math.sin(a)
        wob = 1 + rnd.uniform(-0.1, 0.07)   # wax spreads unevenly
        rings.append([vert(c * SEAL_R * f * w, sn * SEAL_R * f * w, h) for f, w, h in (
            (1.0, wob, -0.0012),                   # sunk onto the cord
            (0.9, wob, SEAL_H),                    # rim top
            (STAMP + 0.06, 1.0, SEAL_H + 0.0003),  # rim's inner lip
            (STAMP, 1.0, SEAL_H - 0.0005))])       # stamped face
    mid = vert(0, 0, SEAL_H - 0.0005)
    for k in range(sides):
        a, b = rings[k], rings[(k + 1) % sides]
        for j in range(3):
            bm.faces.new((a[j], b[j], b[j + 1], a[j + 1]))
        bm.faces.new((a[3], b[3], mid))
    return new_object("PS_Seal", bm, "PS_Wax", grey)

def build(grey=True):
    objs = [build_parchment(grey), build_cord(grey), build_seal(grey)]
    return {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs}

if __name__ == "__main__":
    print(build())
