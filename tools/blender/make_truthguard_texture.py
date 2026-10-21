# Builds Truthguard from truthguard_model.py, bakes one texture for it (worn Titan gold with braided
# collars, painted rivets and grooved claw plates, silver slats edged in gold, purple cloth with gold
# swirls, a bronze backing, dark stone, and a ring of glowing runes) and writes three models sharing it:
#   models/Truthguard.obj       the shield body (Truthguard_0, textured) and the beams (Truthguard_1,
#                               no texture: a straight UV strip for a scrolling flow texture in game)
#   models/Truthguard_Ring.obj  the rune ring (_0 stone, _1 runes: they glow, texture x emission colour)
#   models/Truthguard_Face.obj  the stone face (_0)
#   models/Truthguard.png
# The ring and face have their origin at the disc's centre so they can turn on their own in game.
# The texture is computed in numpy from each texel's position, normal, colour zone (the preview
# material it came from), detail tag (which sub-part) and ambient occlusion, baked from Blender, so it
# is repeatable: change the shape in truthguard_model.py and re-run this. Grain is added in game.
#   blender -b --factory-startup --python tools/blender/make_truthguard_texture.py -- models 1024 [preview.png]
# Arguments: output folder, texture size, optional preview render (not shipped).
import bpy, bmesh, sys, os, math
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
outdir, size = os.path.abspath(argv[0]), int(argv[1])
preview = argv[2] if len(argv) > 2 else None
name = "Truthguard"

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import truthguard_model as tg

bpy.ops.wm.read_factory_settings(use_empty=True)
print(f"RESULT model {tg.build()}")
objs = {o.name: o for o in bpy.data.collections[tg.COLLECTION].objects}
beams = objs.pop("TG_Beams")

# Which model each face ends up in, then one object so everything shares one UV layout
BODY, RING, FACE = 0, 1, 2
for o in objs.values():
    a = o.data.attributes.new("tg_obj", "INT", "FACE")
    a.data.foreach_set("value", [{"TG_Ring": RING, "TG_Face": FACE}.get(o.name, BODY)] * len(o.data.polygons))
bpy.ops.object.select_all(action="DESELECT")
for o in objs.values(): o.select_set(True)
bpy.context.view_layer.objects.active = objs["TG_Top"]   # origin at the shield's centre
bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active

ZONES = {"TG_Stone": 0, "TG_Runes": 1, "TG_Gold": 2, "TG_Bronze": 3, "TG_Silver": 4, "TG_Inlay": 5, "TG_Copper": 6}
STONE, RUNES, GOLD, BRONZE, SILVER, INLAY, COPPER = range(7)
mats = list(obj.data.materials)

# --- UVs: unwrap in groups, each at a set texel density, then pack together ------------------------
# The runes get three times the texture space and the braided collars 1.6 times, so their detail reads.
bpy.ops.object.mode_set(mode="EDIT")
bm = bmesh.from_edit_mesh(obj.data)
dl = bm.faces.layers.int["tg_detail"]
mat_zone = [ZONES[m.name] for m in mats]
groups = [
    (lambda f: mat_zone[f.material_index] == RUNES, 3.0),
    (lambda f: f[dl] == tg.COLLAR, 1.6),
    (lambda f: mat_zone[f.material_index] != RUNES and f[dl] != tg.COLLAR, 1.0),
]
for pick, boost in groups:
    bm = bmesh.from_edit_mesh(obj.data); dl = bm.faces.layers.int["tg_detail"]
    for f in bm.faces: f.select = pick(f)
    bmesh.update_edit_mesh(obj.data)
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02, scale_to_bounds=False)
    bm = bmesh.from_edit_mesh(obj.data); dl = bm.faces.layers.int["tg_detail"]
    uv = bm.loops.layers.uv.active
    sel = [f for f in bm.faces if pick(f)]
    area3d = sum(f.calc_area() for f in sel)
    def uv_area(f):
        pts = [l[uv].uv for l in f.loops]
        return abs(sum(pts[i].x * pts[i - 1].y - pts[i - 1].x * pts[i].y for i in range(len(pts)))) / 2
    k = boost * math.sqrt(area3d / max(sum(uv_area(f) for f in sel), 1e-9))   # UV units per metre x boost
    for f in sel:
        for l in f.loops: l[uv].uv = l[uv].uv * k
    bmesh.update_edit_mesh(obj.data)
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.pack_islands(margin=0.004, rotate=True)
bpy.ops.object.mode_set(mode="OBJECT")

# --- Bake each texel's position, normal, zone, detail tag and occlusion ----------------------------
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
beams.hide_render = True
bake_img = bpy.data.images.new("bake", size, size, alpha=True, float_buffer=True)

def bake(kind):
    for m in mats:
        m.use_nodes = True
        nt = m.node_tree; nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(em.outputs[0], out.inputs[0])
        if kind == "zone":
            v = (ZONES[m.name] + 1) / 100
            em.inputs["Color"].default_value = (v, v, v, 1)
        elif kind == "tag":
            at = nt.nodes.new("ShaderNodeAttribute"); at.attribute_name = "tg_detail"
            add = nt.nodes.new("ShaderNodeMath"); add.operation = "MULTIPLY_ADD"
            add.inputs[1].default_value = 0.01; add.inputs[2].default_value = 0.01
            nt.links.new(at.outputs["Fac"], add.inputs[0])
            nt.links.new(add.outputs[0], em.inputs["Color"])
        elif kind in ("pos", "nrm"):
            geo = nt.nodes.new("ShaderNodeNewGeometry")
            add = nt.nodes.new("ShaderNodeVectorMath"); add.operation = "ADD"
            add.inputs[1].default_value = (2.0, 2.0, 2.0)  # keep values positive; 0 means "no texel"
            nt.links.new(geo.outputs["Position" if kind == "pos" else "Normal"], add.inputs[0])
            nt.links.new(add.outputs[0], em.inputs["Color"])
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bake_img
        nt.nodes.active = tex
    bake_img.pixels.foreach_set(np.zeros(size * size * 4, np.float32))
    if kind == "ao":
        scene.cycles.samples = 32
        bpy.ops.object.bake(type="AO", margin=4, margin_type="EXTEND")
    else:
        scene.cycles.samples = 1
        bpy.ops.object.bake(type="EMIT", margin=4, margin_type="EXTEND")
    px = np.empty(size * size * 4, np.float32); bake_img.pixels.foreach_get(px)
    return px.reshape(size, size, 4)[..., :3]

pos_b = bake("pos")
valid = pos_b.max(-1) > 0.5
P = pos_b - 2.0
N = bake("nrm") - 2.0
zone = np.rint(bake("zone")[..., 0] * 100 - 1).astype(int)
tag = np.rint(bake("tag")[..., 0] * 100 - 1).astype(int)
AO = bake("ao")[..., 0]

# --- Procedural pattern (Blender space: X across, Y depth with the front at -Y, Z up) ---------------
def hash3(c, seed):
    c = c.astype(np.int64)
    h = (c[..., 0] * 73856093) ^ (c[..., 1] * 19349663) ^ (c[..., 2] * 83492791) ^ (seed * 2654435761)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xFFFFFF) / float(0xFFFFFF)

def value_noise(p, seed):
    i = np.floor(p); f = p - i; f = f * f * (3 - 2 * f)
    r = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[..., 0] if dx else 1 - f[..., 0]) * (f[..., 1] if dy else 1 - f[..., 1]) * (f[..., 2] if dz else 1 - f[..., 2])
                r = r + w * hash3(i + np.array([dx, dy, dz]), seed)
    return r

def fbm(p, seed, octaves=3):
    return sum(value_noise(p * 2 ** o, seed + o) * 0.5 ** o for o in range(octaves)) / sum(0.5 ** o for o in range(octaves))

def ramp(t, stops):
    """stops: [(t, (r, g, b)), ...] in sRGB."""
    ts = [s[0] for s in stops]
    return np.stack([np.interp(t, ts, [s[1][k] for s in stops]) for k in range(3)], -1)

def line(d, w):
    """1 on a line (|d| < w), fading to 0 over another w."""
    return np.clip(2 - np.abs(d) / w, 0, 1)

def seg_dist(q, a, b):
    """Distance from 2D points q to segment ab, and how far along it (0-1)."""
    ab = b - a
    t = np.clip(((q - a) * ab).sum(-1) / max((ab * ab).sum(), 1e-12), 0, 1)
    return np.linalg.norm(q - (a + t[:, None] * ab), axis=-1), t

p = P[valid]; n = N[valid]; zn = zone[valid]; tg_ = tag[valid]; ao = AO[valid]
x, y, z = p[:, 0], p[:, 1], p[:, 2]
ax = np.abs(x)
c = tg.disc_centre()
dz = z - c.z
r = np.hypot(x, dz)
ang = np.arctan2(dz, x)
front = n[:, 1] < -0.3
col = np.zeros((len(p), 3))

GOLD_RAMP = [(0.0, (0.28, 0.17, 0.05)), (0.45, (0.68, 0.47, 0.16)), (0.8, (0.88, 0.68, 0.30)), (1.0, (0.98, 0.88, 0.56))]
gold_t = 0.55 + 0.22 * (fbm(p * 9, 1) - 0.5) + 0.06 * (fbm(p * 60, 2) - 0.5)   # broad wear, fine pitting
gold_t = gold_t - 0.12 * (n[:, 1] > 0.3)           # backs a little duller
gold = ramp(np.clip(gold_t, 0, 1), GOLD_RAMP)
is_gold = zn == GOLD

# Collars: a braided band (herringbone strands) between two raised beads, round the disc and out
# along the cross-beam's arms
coll = is_gold & (tg_ == tg.COLLAR)
arm_t = (np.abs(dz - tg.BEAM_Z) - tg.BEAM_HW) / tg.COLLAR_ARM_W
in_arm = (ax > tg.COLLAR_R_IN + 0.004) & (arm_t > -0.15) & (arm_t < 1.15)
s_ = np.where(in_arm, ax, np.abs(ang) * (tg.COLLAR_R_IN + tg.COLLAR_R_OUT) / 2)
t_ = np.clip(np.where(in_arm, arm_t, (r - tg.COLLAR_R_IN) / (tg.COLLAR_R_OUT - tg.COLLAR_R_IN)), 0, 1)
strand = ((s_ / 0.011 + np.abs(t_ - 0.5) * 1.6) % 1.0)
braid = np.sin(np.pi * strand) ** 0.6                 # 1 mid-strand, 0 in the grooves between
bead = (t_ < 0.14) | (t_ > 0.86)
bead_line = line(np.minimum(np.abs(t_ - 0.14), np.abs(t_ - 0.86)), 0.025)
braid_t = np.where(bead, 0.8, 0.35 + 0.5 * braid) - 0.45 * bead_line
gold[coll] = ramp(np.clip(braid_t + 0.1 * (gold_t - 0.55), 0, 1), GOLD_RAMP)[coll]

# Claw plates: one engraved groove either side of a bright spine ridge, following each plate root to tip
claws = tg.CLAWS + [tg.PEAK]
for k, (spine, tip, *_rest) in enumerate(claws):
    m = is_gold & (tg_ == tg.CLAW + k)
    if not m.any(): continue
    q = np.stack([ax[m], z[m]], -1)
    pts = [np.array(s[:2]) for s in spine] + [np.array(tip)]
    hws = [s[2] for s in spine] + [0.004]
    best = np.full(len(q), 1e9); lat = np.zeros(len(q))
    for i in range(len(pts) - 1):
        d, t = seg_dist(q, pts[i], pts[i + 1])
        hw = hws[i] + (hws[i + 1] - hws[i]) * t
        closer = d < best
        best[closer] = d[closer]; lat[closer] = (d / np.maximum(hw, 1e-3))[closer]
    groove = line(lat - 0.55, 0.035)
    lip = line(lat - 0.62, 0.03)                       # catches the light on the groove's outer side
    ridge = line(lat, 0.08)
    t_c = np.clip(gold_t[m] + 0.15 * ridge + 0.2 * lip - 0.5 * groove, 0, 1)
    gold[m] = ramp(t_c, GOLD_RAMP)

# Bars: a bright bevel on the inner edge by the beam, and a row of painted rivets along the outer edge
bar = is_gold & (tg_ == tg.BAR)
rz = [row[0] for row in tg.BAR_ROWS][::-1]
outer = np.interp(z, rz, [row[1] for row in tg.BAR_ROWS][::-1])
rivet_x = outer - 0.009
rivet_zs = np.arange(0.04, -0.30, -0.034)
rd = np.min(np.hypot(ax[:, None] - rivet_x[:, None], z[:, None] - rivet_zs[None]), axis=1)
rivet = bar & front & (rd < 0.0055)
bevel = bar & front & (ax < tg.HALF_GAP + 0.004)
gold[bevel] = ramp(np.full(bevel.sum(), 0.92), GOLD_RAMP)
gold[rivet] = ramp(np.clip(1.0 - rd[rivet] / 0.0055 * 0.75, 0, 1), GOLD_RAMP)
rim = bar & front & (np.abs(rd - 0.0055) < 0.0012)
gold[rim] *= 0.45
# Rivets at the collar arms' ends, too
arm_end = coll & in_arm & (np.abs(ax - (tg.COLLAR_ARM_X - 0.012)) < 0.006) & (np.abs(t_ - 0.5) < 0.2) & front
gold[arm_end] = ramp(np.full(arm_end.sum(), 0.97), GOLD_RAMP)

# Bosses: a domed gold cap with a groove ring
boss = is_gold & (tg_ == tg.BOSS)
(bx, bz, _), (ex, ez, _) = tg.STRAP[0], tg.STRAP[-1]
br = np.hypot(ax - bx, z - bz)
gold[boss] = ramp(np.clip(0.95 - br[boss] / tg.BOSS_R * 0.4 - 0.5 * line(br[boss] - 0.018, 0.0015), 0, 1), GOLD_RAMP)
col[is_gold] = gold[is_gold]

# Silver: the slats' fronts, bright down the ridge, edged in gold with a dark line inside the edging
for k, (top, rows, bottom, _f) in enumerate(tg.SLATS):
    m = (zn == SILVER) & (tg_ == tg.SLAT + k)
    zs = [top[1]] + [row[0] for row in rows] + [bottom[1]]
    cxs = [top[0]] + [row[1] for row in rows] + [bottom[0]]
    hws = [0.002] + [row[2] for row in rows] + [0.002]
    cx_ = np.interp(z[m], zs[::-1], cxs[::-1]); hw_ = np.interp(z[m], zs[::-1], hws[::-1])
    f = np.abs(ax[m] - cx_) / np.maximum(hw_, 1e-3)
    sil = ramp(np.clip(0.55 + 0.35 * line(f, 0.12) + 0.25 * (fbm(p[m] * 20, 3) - 0.5), 0, 1),
               [(0.0, (0.55, 0.57, 0.62)), (0.6, (0.80, 0.81, 0.84)), (1.0, (0.95, 0.96, 0.97))])
    edge = f > 0.74
    sil[edge] = ramp(np.clip(0.75 + 0.2 * (fbm(p[m][edge] * 9, 1) - 0.5), 0, 1), GOLD_RAMP)
    sil *= (1 - 0.6 * line(f - 0.74, 0.03))[:, None]
    col[m] = sil

# Cloth: purple weave with gold trim at the edges and gold-thread swirls (curls alternating direction)
cloth = zn == INLAY
sp = [np.array([sx, sy, sz]) for sx, sz, sy in tg.STRAP]
sp = [np.array([v[0], v[1] + tg.bow(v[2]), v[2]]) for v in sp]
(axs, azs, _), (bxs, bzs, _) = tg.STRAP[:2]
wd = np.array([-(bzs - azs), 0, bxs - axs]) / np.hypot(bzs - azs, bxs - axs)   # across the band
q3 = np.stack([ax[cloth], y[cloth], z[cloth]], -1)
best = np.full(len(q3), 1e9); along = np.zeros(len(q3)); across = np.zeros(len(q3)); s0 = 0.0
for i in range(len(sp) - 1):
    a, b = sp[i], sp[i + 1]; ab = b - a; L = np.linalg.norm(ab)
    t = np.clip(((q3 - a) * ab).sum(-1) / (L * L), 0, 1)
    foot = a + t[:, None] * ab
    d = np.linalg.norm(q3 - foot, axis=-1)
    closer = d < best
    best[closer] = d[closer]; along[closer] = (s0 + t * L)[closer]
    across[closer] = ((q3 - foot) @ wd)[closer] / tg.STRAP_HW
    s0 += L
across = np.clip(across, -1, 1)
cell = 2 * tg.STRAP_HW * 1.1
j = np.floor(along / cell)
u = (along / cell - j - 0.5) * 2.2
v = across * 1.15
dirn = np.where(j % 2 == 0, 1.0, -1.0)
rr = np.hypot(u, v)
th = np.arctan2(v, u) * dirn
spiral = line(((th / (2 * np.pi) - rr * 1.3) % 1.0) - 0.5, 0.07) * (rr < 0.95) * (rr > 0.08)
weave = 0.9 + 0.1 * np.sign(np.sin(along * 900) * np.sin(across * 40))
purple = np.array([0.48, 0.26, 0.66]) * (0.85 + 0.3 * fbm(q3 * 15, 4))[:, None] * weave[:, None]
thread = ramp(np.full(len(q3), 0.78), GOLD_RAMP)
trim = np.abs(across) > 0.80
cl = purple * (1 - spiral[:, None]) + thread * spiral[:, None]
cl[trim] = ramp(np.full(trim.sum(), 0.72), GOLD_RAMP)
cl *= (1 - 0.5 * line(np.abs(across) - 0.80, 0.03))[:, None]
col[cloth] = cl

# Bronze backing plate: darker and warm
bron = zn == BRONZE
col[bron] = ramp(np.clip(0.5 + 0.3 * (fbm(p[bron] * 10, 5) - 0.5), 0, 1),
                 [(0.0, (0.28, 0.18, 0.09)), (1.0, (0.60, 0.43, 0.22))])

# Copper studs: brightest on top
cop = zn == COPPER
col[cop] = ramp(np.clip(-n[cop, 1], 0, 1), [(0.0, (0.40, 0.18, 0.10)), (1.0, (0.85, 0.50, 0.32))])

# Stone: dark grey with soft mottling and a few hairline cracks; a faint carved circle on the face
stone = zn == STONE
cr = fbm(p[stone] * 18, 6)
crack = line(cr - 0.5, 0.012) * (fbm(p[stone] * 4, 7) > 0.5)
sg = 0.5 + 0.35 * (fbm(p[stone] * 7, 8) - 0.5) - 0.35 * crack
carve = line(r[stone] - 0.068, 0.0025) * (n[stone, 1] < -0.5)
col[stone] = ramp(np.clip(sg - 0.3 * carve, 0, 1), [(0.0, (0.13, 0.14, 0.15)), (0.5, (0.30, 0.31, 0.33)), (1.0, (0.44, 0.45, 0.47))])

# Runes: angular glyphs round the ring's sloping front, between two thin glowing border lines. The
# rest of the rune face stays dark so only the runes glow (this material's emission = texture x glow).
runes = zn == RUNES
rng = np.random.default_rng(11)
GLYPHS = 20
pts9 = [(0, 0), (1, 0), (0, 1), (1, 1), (0, 2), (1, 2), (0.5, 0), (0.5, 2), (0.5, 1)]
glyphs = []
for g in range(GLYPHS):
    strokes = [(pts9[6], pts9[7])]                     # every glyph has a stem
    for _ in range(rng.integers(2, 4)):
        i0, i1 = rng.choice(len(pts9), 2, replace=False)
        strokes.append((pts9[i0], pts9[i1]))
    glyphs.append(strokes)
(r_in, _), (r_out, _) = tg.RING_SECTION[3], tg.RING_SECTION[2]
rq = r[runes]; aq = ang[runes] % (2 * np.pi)
rho = (rq - r_in) / (r_out - r_in)                     # 0 inner, 1 outer
cell_a = 2 * np.pi / GLYPHS
gi = np.floor(aq / cell_a).astype(int)
mid_r = (r_in + r_out) / 2
lu = (aq - (gi + 0.5) * cell_a) * mid_r               # metres along the ring from the glyph's centre
lv = (rho - 0.5) * 0.045                              # metres across (the slope is ~4.7 cm)
gw, gh = 0.014, 0.022
dist = np.full(len(rq), 1e9)
for g in range(GLYPHS):
    m = gi == g
    if not m.any(): continue
    q = np.stack([lu[m], lv[m]], -1)
    dg = np.full(m.sum(), 1e9)
    for (a0, a1), (b0, b1) in glyphs[g]:
        A = np.array([(a0 - 0.5) * gw, (a1 - 1) * gh / 2]); B = np.array([(b0 - 0.5) * gw, (b1 - 1) * gh / 2])
        d, _t = seg_dist(q, A, B)
        dg = np.minimum(dg, d)
    dist[m] = dg
core = np.clip(1 - (dist - 0.0012) / 0.0010, 0, 1)
halo = np.exp(-np.maximum(dist - 0.0012, 0) / 0.0025)
border = np.maximum(line(rho - 0.07, 0.025), line(rho - 0.93, 0.025)) * 0.6
glow = np.clip(np.maximum(core, border) + 0.35 * halo, 0, 1)
col[runes] = ramp(glow, [(0.0, (0.06, 0.08, 0.11)), (0.35, (0.20, 0.42, 0.75)), (0.75, (0.62, 0.85, 1.0)), (1.0, (0.92, 0.98, 1.0))])

# Light from the beams: metal right beside them picks up a blue-white sheen
metal = is_gold | (zn == SILVER) | bron
beam_d = np.where((z < tg.CENTRE_BEAM[0]) & (z > tg.CENTRE_BEAM[-1]), ax, 1.0)
beam_d = np.minimum(beam_d, np.where(ax < tg.CROSS_BEAM_X, np.abs(dz - tg.BEAM_Z), 1.0))
spill = (0.45 * np.exp(-np.maximum(beam_d - 0.008, 0) / 0.010) * (r > tg.COLLAR_R_IN))[:, None] * metal[:, None]
col = col * (1 - spill) + np.array([0.72, 0.88, 1.0]) * spill

# Occlusion: darker where pieces overlap and in the gaps (not on the glowing runes)
occ = np.where(runes, 1.0, 0.45 + 0.55 * np.clip(ao, 0, 1))
col *= occ[:, None]

out = np.zeros((size, size, 4), np.float32); out[..., 3] = 1
out[..., :3] = (0.45, 0.34, 0.16)
out[valid, :3] = np.clip(col, 0, 1)
img = bpy.data.images.new(name, size, size, alpha=False)
img.pixels.foreach_set(out.ravel())
img.filepath_raw = os.path.join(outdir, name + ".png"); img.file_format = "PNG"
img.save()
print(f"RESULT {valid.sum()} textured texels ({100 * valid.mean():.0f}% of the texture), "
      f"{(glow > 0.5).sum()} glowing rune texels")

# --- Split into the three models and export ------------------------------------------------------
srgb = lambda *c_: tuple(v ** 2.2 for v in c_) + (1,)
def export_material(mname, tex=None, colour=None, glow=None, metallic=0.6):
    m = bpy.data.materials.new(mname); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    o = nt.nodes.new("ShaderNodeOutputMaterial"); bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs[0], o.inputs[0])
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = 0.45
    src = None
    if tex:
        node = nt.nodes.new("ShaderNodeTexImage"); node.image = tex
        nt.links.new(node.outputs["Color"], bsdf.inputs["Base Color"]); src = node.outputs["Color"]
    else:
        bsdf.inputs["Base Color"].default_value = colour
    if glow:  # as in game: glow = texture x the material's emission colour
        if src is not None:
            mul = nt.nodes.new("ShaderNodeVectorMath"); mul.operation = "MULTIPLY"
            mul.inputs[1].default_value = glow[:3]
            nt.links.new(src, mul.inputs[0]); nt.links.new(mul.outputs[0], bsdf.inputs["Emission Color"])
        else:
            bsdf.inputs["Emission Color"].default_value = glow
        bsdf.inputs["Emission Strength"].default_value = 3.0
    return m

me_obj = obj.data.attributes["tg_obj"]
ids = np.empty(len(obj.data.polygons), np.int32); me_obj.data.foreach_get("value", ids)
src_idx = np.empty(len(obj.data.polygons), np.int32); obj.data.polygons.foreach_get("material_index", src_idx)
src_zone = np.array(mat_zone)[src_idx]
GLOW = srgb(0.6, 0.82, 1.0)

def split(which, oname, new_mats, mat_of, offset=None):
    """A copy of the joined object keeping only part `which`, its materials replaced; mat_of maps a
    face's zone to its new material index. offset moves its origin (the disc's centre for ring and face)."""
    me = obj.data.copy(); part = bpy.data.objects.new(oname, me)
    scene.collection.objects.link(part)
    me.materials.clear()   # before setting face materials: clearing resets them
    for m in new_mats: me.materials.append(m)
    bm_ = bmesh.new(); bm_.from_mesh(me)
    keep = set(np.nonzero(ids == which)[0].tolist())
    zl = [src_zone[f.index] for f in bm_.faces]
    for f in bm_.faces: f.material_index = mat_of(zl[f.index])
    bmesh.ops.delete(bm_, geom=[f for f in bm_.faces if f.index not in keep], context="FACES")
    if offset is not None:
        for v in bm_.verts: v.co -= offset
    bm_.to_mesh(me); bm_.free()
    return part

body = split(BODY, name, [export_material(name + "_0", tex=img)], lambda zz: 0)
ring = split(RING, name + "_Ring", [export_material(name + "_Ring_0", tex=img, metallic=0.0),
                                     export_material(name + "_Ring_1", tex=img, glow=GLOW, metallic=0.0)],
             lambda zz: 1 if zz == RUNES else 0, offset=c)
face = split(FACE, name + "_Face", [export_material(name + "_Face_0", tex=img, metallic=0.0)], lambda zz: 0, offset=c)

# Beams join the body as its second material
beams.data.materials.clear()
beams.data.materials.append(export_material(name + "_1", colour=GLOW, glow=GLOW, metallic=0.0))
bpy.ops.object.select_all(action="DESELECT")
beams.hide_render = False
bpy.context.view_layer.objects.active = body
body.select_set(True); beams.select_set(True)
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
obj.hide_render = True

def tris(o): return sum(len(pp.vertices) - 2 for pp in o.data.polygons)
for part in (body, ring, face):
    bpy.ops.object.select_all(action="DESELECT"); part.select_set(True)
    bpy.context.view_layer.objects.active = part
    bpy.ops.wm.obj_export(filepath=os.path.join(outdir, part.name + ".obj"), export_selected_objects=True,
                          export_uv=True, export_normals=False, export_materials=True, export_triangulated_mesh=True,
                          path_mode="STRIP", forward_axis="NEGATIVE_Z", up_axis="Y")
    print(f"RESULT {part.name}: {tris(part)} triangles")
# Disc centre in the mod's space (the loader mirrors X; Blender -Y forward becomes +Z)
print(f"RESULT disc centre in mod space: (0, {c.z:.4f}, {-c.y:.4f})")

# --- Optional preview render (not shipped) --------------------------------------------------------
if preview:
    from mathutils import Vector
    ring.location = c; face.location = c                # put the disc back for the picture
    world = bpy.data.worlds.new("w"); scene.world = world
    world.use_nodes = True; world.node_tree.nodes["Background"].inputs[0].default_value = (0.03, 0.03, 0.035, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0; sun.rotation_euler = (1.1, 0.25, -0.5); scene.collection.objects.link(sun)
    fill = bpy.data.objects.new("fill", bpy.data.lights.new("fill", "SUN"))
    fill.data.energy = 2.0; fill.rotation_euler = (-1.0, 0.0, 2.6); scene.collection.objects.link(fill)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    scene.camera = cam
    scene.cycles.samples = 48
    base_path, ext = os.path.splitext(preview)
    for tag_, direction, target, scale, res in (
            ("front", Vector((0, 1, 0)), Vector((0, 0, 0.0)), 1.15, (700, 1000)),
            ("angle", Vector((-0.55, 0.8, -0.25)), Vector((0, 0, 0.0)), 1.15, (700, 1000)),
            ("close", Vector((-0.3, 0.95, -0.15)), Vector((0, 0, 0.15)), 0.5, (800, 800)),
            ("back", Vector((0.5, -0.85, -0.2)), Vector((0, 0, 0.0)), 1.15, (700, 1000))):
        scene.render.resolution_x, scene.render.resolution_y = res
        cam.data.ortho_scale = scale
        cam.location = target - direction.normalized() * 3
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{base_path}_{tag_}{ext}"
        bpy.ops.render.render(write_still=True)
