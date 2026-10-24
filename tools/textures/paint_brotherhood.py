"""Paints the Brotherhood set's textures into models/textures/.

Each texture is painted on the layout of the vanilla texture it replaces: the vanilla texture's opaque
islands (one per part of the mesh) say where to paint, and each island gets our own colour. The game
multiplies our colours by the vanilla texture's shading at load (MysticalArmor), as much as our alpha says.
Cloth takes none of it (the vanilla cloth is blocky 128-pixel mottling): it gets our own soft folds instead.

Input: the vanilla textures, read from the game on your own machine (never committed), as
tex_<name>.png in the folder given as the first argument. A temporary probe in the mod can write them:
ModelUtil.ReadableCopy(texture).EncodeToPNG() for each texture of the bases' materials. Run with Python 3 and Pillow:
    python tools/textures/paint_brotherhood.py <folder with tex_*.png>
"""
import math
import os
import random
import sys
from collections import deque

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "models", "textures")
SIZE = 512

# The hero reference's colours
SLATE = (38, 48, 62)
ASH = (70, 74, 81)                               # the hood: mid-dark grey (the hero art is brightly lit)
CHARCOAL = (42, 44, 47)
LEATHER = (92, 61, 39)
DARK_LEATHER = (60, 40, 27)
RED = (142, 27, 27)
SILVER = (163, 167, 171)
# Edge trims: muted pewter, a step lighter than the cloth they edge, unshaded (vanilla edges are bright)
TRIM = (74, 82, 94)
HOOD_TRIM = (104, 108, 115)
# Silver thread: dimmer than the clasps' silver so it reads as embroidery, not metal
THREAD = (132, 137, 144)


def islands(tex, min_px=6):
    """Opaque 8-connected islands of a texture in scan order (tiny specks come back empty)."""
    w, h = tex.size
    px = tex.load()
    label = [[-1] * w for _ in range(h)]
    found = []
    for y in range(h):
        for x in range(w):
            if px[x, y][3] <= 127 or label[y][x] >= 0:
                continue
            queue, pts = deque([(x, y)]), []
            label[y][x] = len(found)
            while queue:
                cx, cy = queue.popleft()
                pts.append((cx, cy))
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        nx, ny = cx + dx, cy + dy
                        if 0 <= nx < w and 0 <= ny < h and px[nx, ny][3] > 127 and label[ny][nx] < 0:
                            label[ny][nx] = len(found)
                            queue.append((nx, ny))
            found.append(pts)
    return [set(p) if len(p) >= min_px else set() for p in found]


def grain_map(seed=7):
    """Leather grain over the whole texture (SIZE x SIZE factors around 1): broad mottling plus fine pores.
    Seeded, so repainting gives the same textures."""
    rnd = random.Random(seed)
    coarse = Image.new("L", (32, 32))
    coarse.putdata([int(128 + rnd.gauss(0, 40)) for _ in range(32 * 32)])
    coarse = coarse.resize((SIZE, SIZE), Image.BICUBIC).load()
    fine = [rnd.gauss(0, 1) for _ in range(SIZE * SIZE)]
    return [[1 + (coarse[x, y] - 128) / 128 * 0.22 + fine[y * SIZE + x] * 0.05 for x in range(SIZE)]
            for y in range(SIZE)]


GRAIN = grain_map()


def fold_map(seed=5):
    """Soft cloth folds over the whole texture (SIZE x SIZE factors around 1): broad streaks down the texture,
    the way cloth hangs, and a fainter finer layer. Seeded like the grain."""
    rnd = random.Random(seed)

    def layer(w, h, amount):
        small = Image.new("L", (w, h))
        small.putdata([int(128 + rnd.gauss(0, 50)) for _ in range(w * h)])
        return small.resize((SIZE, SIZE), Image.BICUBIC).load(), amount

    # Folds, then a soft mottle of lighter and darker patches, as Valheim's own cloth has but fainter
    layers = [layer(12, 3, 0.12), layer(28, 6, 0.05), layer(40, 40, 0.06), layer(96, 96, 0.035)]
    return [[1 + sum((l[x, y] - 128) / 128 * a for l, a in layers) for x in range(SIZE)] for y in range(SIZE)]


FOLDS = fold_map()


def damask_map(tile=48):
    """Tone-on-tone damask over the whole texture (SIZE x SIZE, 0-1): our own scroll motif (mirrored spirals
    round a stem and a small leaf), repeated in half-drop rows as woven damask is. Drawn large and shrunk, so
    its lines stay soft."""
    k = 4
    t = Image.new("L", (tile * k, tile * k), 0)
    d = ImageDraw.Draw(t)
    c, s, w = tile * k / 2, tile * k / 2, k
    d.line([c, c - s * 0.75, c, c + s * 0.6], fill=255, width=w)
    for side in (-1, 1):
        for oy, r in ((-s * 0.3, s * 0.32), (s * 0.3, s * 0.24)):
            pts = []
            for i in range(50):
                a = i / 49 * 2.2 * math.pi
                rr = r * (1 - a / (2.6 * math.pi))
                pts.append((c + side * (r - rr * math.cos(a)), c + oy - rr * math.sin(a) * 0.9))
            d.line(pts, fill=255, width=w)
    d.polygon([(c, c - s * 0.92), (c + s * 0.12, c - s * 0.75), (c, c - s * 0.6), (c - s * 0.12, c - s * 0.75)], fill=255)
    t = t.resize((tile, tile), Image.LANCZOS).load()
    return [[t[x % tile, (y + (tile // 2 if (x // tile) % 2 else 0)) % tile] / 255 for x in range(SIZE)] for y in range(SIZE)]


DAMASK = damask_map()


def leather(rgb, px, py, worn_edge):
    """Our leather: the grain, and edges darkened where it would wear."""
    k = GRAIN[py][px] * (0.72 if worn_edge else 1.0)
    return tuple(max(0, min(255, int(c * k))) for c in rgb)


def cloth(rgb, px, py, d=None):
    """Our cloth: the folds, a little darker towards a panel's edges (d: texels in from the edge, if known)."""
    k = FOLDS[py][px]
    if d is not None:
        k *= 0.86 + 0.14 * min(1.0, d / 4)
    return tuple(max(0, min(255, int(c * k))) for c in rgb)


def damask(rgb, px, py):
    """Cloth with the damask woven in: a lighter shade of itself, faint."""
    return mix(rgb, tuple(min(255, int(c * 1.22 + 6)) for c in rgb), DAMASK[py][px] * 0.55)


def mix(a, b, t):
    return tuple(int(x + (y - x) * t) for x, y in zip(a, b))


def edge(pts, x, y):
    """True if vanilla texel (x, y) is on its island's border."""
    x, y = int(x), int(y)
    return any((x + dx, y + dy) not in pts for dx in (-1, 0, 1) for dy in (-1, 0, 1))


def distance_field(pts, scale):
    """How far each painted pixel is in from an island's edge, in vanilla texels: (x0, y0, width, height, values)
    over the island's box in painted pixels. The outline is smoothed first, so lines drawn at a distance follow
    the shape and not the vanilla texels' stair steps."""
    if not pts:
        return None
    bx0, by0, bx1, by1 = bbox(pts)
    m = 3
    w, h = bx1 - bx0 + 1 + 2 * m, by1 - by0 + 1 + 2 * m
    mask = Image.new("L", (w, h), 0)
    mpx = mask.load()
    for x, y in pts:
        mpx[x - bx0 + m, y - by0 + m] = 255
    big = mask.resize((w * scale, h * scale), Image.BICUBIC).filter(ImageFilter.GaussianBlur(scale * 0.5))
    W, H = big.size
    d = [0.0 if v < 128 else 1e9 for v in big.tobytes()]
    r2 = math.sqrt(2)
    # Chamfer distance: a pass forwards, then one backwards
    for y in range(H):
        for x in range(W):
            i = y * W + x
            if d[i] == 0:
                continue
            v = d[i]
            if x > 0: v = min(v, d[i - 1] + 1)
            if y > 0:
                v = min(v, d[i - W] + 1)
                if x > 0: v = min(v, d[i - W - 1] + r2)
                if x < W - 1: v = min(v, d[i - W + 1] + r2)
            d[i] = v
    for y in range(H - 1, -1, -1):
        for x in range(W - 1, -1, -1):
            i = y * W + x
            if d[i] == 0:
                continue
            v = d[i]
            if x < W - 1: v = min(v, d[i + 1] + 1)
            if y < H - 1:
                v = min(v, d[i + W] + 1)
                if x < W - 1: v = min(v, d[i + W + 1] + r2)
                if x > 0: v = min(v, d[i + W - 1] + r2)
            d[i] = v
    return (bx0 - m) * scale, (by0 - m) * scale, W, H, [v / scale for v in d]


def distance_at(field, px, py):
    x0, y0, w, h, d = field
    x, y = px - x0, py - y0
    return d[y * w + x] if 0 <= x < w and 0 <= y < h else 0.0


def stitch(x, y, d):
    """A saddle stitch one texel in from a leather edge: short dashes whichever way the edge runs."""
    if not 0.8 <= d <= 1.1:
        return False
    p = 0.75                                     # stitch length, in texels
    return (int(x / p) + int(y / p)) % 2 == 0


def cord(x, y, d, inner, outer):
    """Embroidery between `inner` and `outer` texels in from the edge: a twisted cord, its threads slanting."""
    return inner <= d <= outer and math.sin(2 * math.pi * (x + y) / 0.9) > 0.1


def paint(vanilla, colour_of, background, measure=()):
    """Paints a SIZE x SIZE texture: colour_of(island index, pts, x, y, vanilla pixel, d) -> (rgb, shading 0-1),
    where x, y are in vanilla texels (fractional, so bands and lines stay smooth at the higher resolution) and
    d is how far in from the island's edge in texels (None unless the island is in `measure`). Leather gets
    its grain, and stitching along its edges where d is known."""
    w, h = vanilla.size
    scale = SIZE // w
    isl = islands(vanilla)
    fields = {i: distance_field(isl[i], scale) for i in measure}
    vpx = vanilla.load()
    label = [[-1] * w for _ in range(h)]
    for i, pts in enumerate(isl):
        for x, y in pts:
            label[y][x] = i
    # Grow each island a few texels outwards so filtering at its border doesn't pick up the background
    for _ in range(3):
        grown = [row[:] for row in label]
        for y in range(h):
            for x in range(w):
                if label[y][x] >= 0:
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and label[ny][nx] >= 0:
                        grown[y][x] = label[ny][nx]
                        break
        label = grown
    out = Image.new("RGBA", (SIZE, SIZE), background + (255,))
    opx = out.load()
    for py in range(SIZE):
        for px in range(SIZE):
            vx, vy = px // scale, py // scale
            i = label[vy][vx]
            if i < 0:
                continue
            x, y = (px + 0.5) / scale, (py + 0.5) / scale
            d = distance_at(fields[i], px, py) if fields.get(i) else None
            rgb, shading = colour_of(i, isl[i], x, y, vpx[vx, vy], d)
            if rgb in (LEATHER, DARK_LEATHER):
                stitched = d is not None and stitch(x, y, d)
                rgb = leather(rgb, px, py, d < 0.6 if d is not None else edge(isl[i], x, y))
                if stitched:
                    rgb = tuple(int(c * 0.6) for c in rgb)
            opx[px, py] = rgb + (int(shading * 255),)
    return out, scale, isl, fields


def brightness(c):
    return (0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]) / 255


def bbox(pts):
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def hood_and_boots(vanilla):
    """MageArmorRed_d: the hood, boots and clasps. Shared by the hood and the legs.
    Island numbers are in scan order of the vanilla texture (Valheim 1.0.20), checked in game."""
    scale = SIZE // vanilla.size[0]

    def colour(i, pts, x, y, c, d):
        if i == 7:                               # the hood: trim, a silver line and a fine cord inside it
            if d < 0.9:
                return HOOD_TRIM, 0.0
            if 1.3 <= d <= 1.55:
                return THREAD, 0.0
            px, py = int(x * scale), int(y * scale)
            if cord(x, y, d, 1.85, 2.45):
                return THREAD, 0.0
            base = cloth(ASH, px, py, d)
            return (damask(base, px, py) if d > 2.8 else base), 0.0
        if i in (1, 2, 3, 4):
            return DARK_LEATHER, 1.0             # rings: boot cuffs and straps
        if i != 0:
            return SILVER, 0.6                   # clasps and studs
        if is_clasp(c):                          # 0 mixes boots, soles and the throat clasp
            return SILVER, 0.5                   # as the belt buckle
        b = brightness(c)
        if b < 0.09:
            return CHARCOAL, 1.0
        return LEATHER, 1.0

    return paint(vanilla, colour, LEATHER, measure=(0, 1, 2, 3, 4, 7))[0]


def is_clasp(c):
    """True for MageArmorRed_d's bright texels in island 0: the throat clasp (the brooch)."""
    return brightness(c) > 0.35


def robe(vanilla):
    """AshlandsMageArmor_d: chest front (2) and back (4), front tail (1), side tails (10), bracers (7),
    belt with pouch and cords (0), buckle (3); the rest are rings and studs."""
    scale = SIZE // vanilla.size[0]

    def colour(i, pts, x, y, c, d):
        if i in (1, 2, 4, 10):
            if d < 0.9:
                return TRIM, 0.0
            # Embroidery as the hero reference's coat: a silver line and a cord inside the trim, damask within
            if 1.3 <= d <= 1.55 or cord(x, y, d, 1.85, 2.6):
                return THREAD, 0.0
            px, py = int(x * scale), int(y * scale)
            base = cloth(CHARCOAL if i == 10 else SLATE, px, py, d)
            return (damask(base, px, py) if d > 2.9 else base), 0.0
        if i in (0, 7):
            return LEATHER, 1.0
        return SILVER, 0.5

    img, scale, isl, fields = paint(vanilla, colour, SLATE, measure=(0, 1, 2, 4, 7, 10))
    crest(img, [v * scale for v in bbox(isl[3])])
    hem_scroll(img, isl[1], fields[1], scale)
    return img


def crest(img, box):
    """Our own buckle mark: a ring around a single feather (not any game's crest)."""
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    r = min(x1 - x0, y1 - y0) / 2 + 1
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=SILVER + (110,), outline=CHARCOAL + (255,), width=2)
    d.line([cx, cy - r * 0.7, cx, cy + r * 0.7], fill=CHARCOAL + (255,), width=2)
    for k in range(-2, 3):
        yy = cy + k * r * 0.22
        d.line([cx, yy, cx - r * 0.45, yy - r * 0.25], fill=CHARCOAL + (255,), width=1)
        d.line([cx, yy, cx + r * 0.45, yy - r * 0.25], fill=CHARCOAL + (255,), width=1)


def hem_scroll(img, pts, field, scale):
    """Our own scrollwork low on the front tail, kept inside the embroidered border."""
    x0, y0, x1, y1 = bbox(pts)
    row = int(y0 + (y1 - y0) * 0.72)
    xs = [x for x, y in pts if y == row]
    scroll(img, field, (min(xs) + max(xs) + 1) / 2 * scale, row * scale, (max(xs) - min(xs) + 1) / 2 * scale * 0.42, 2.9)


def scroll(img, field, cx, cy, s, inside):
    """Our scroll ornament in silver thread: a stem with mirrored spirals and a diamond at its point (towards
    smaller y), s its half-width in painted pixels; drawn only more than `inside` texels in from the edge."""
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    ink = THREAD + (255,)
    d.line([cx, cy - s * 2.6, cx, cy + s * 2.2], fill=ink, width=1)
    for side in (-1, 1):
        for oy, r in ((-s * 1.2, s * 0.9), (s * 0.9, s * 0.7)):
            spiral = []
            for k in range(60):
                t = k / 59 * 2.2 * math.pi
                rr = r * (1 - t / (2.6 * math.pi))
                spiral.append((cx + side * (r - rr * math.cos(t)), cy + oy - rr * math.sin(t) * 0.9))
            d.line(spiral, fill=ink, width=1)
    d.polygon([(cx, cy - s * 3.1), (cx + s * 0.35, cy - s * 2.6), (cx, cy - s * 2.1), (cx - s * 0.35, cy - s * 2.6)], fill=ink)
    lp, ip = layer.load(), img.load()
    for py in range(img.size[1]):
        for px in range(img.size[0]):
            if lp[px, py][3] and distance_at(field, px, py) > inside:
                ip[px, py] = THREAD + (0,)


def gloves_from(vanilla):
    """Row of the body paint where the sleeves end and the gloves begin."""
    return int(vanilla.size[1] * 0.40)


def chest_paint(vanilla):
    """AshlandsMageArmorChest_d, painted on the body: the undershirt (0), whose red shows as a sash between
    the vest and the belt, and the sleeves (1) with the gloves below them."""
    split = gloves_from(vanilla)
    scale = SIZE // vanilla.size[0]

    def colour(i, pts, x, y, c, d):
        px, py = int(x * scale), int(y * scale)
        if i == 0:
            # Its top rows are the neck and shoulders (read from the body mesh): a dark scarf under the hood
            return cloth(CHARCOAL if y < vanilla.size[1] * 24 / 256 else RED, px, py), 0.0
        if y >= split:
            return LEATHER, 0.8
        return cloth(SLATE, px, py), 0.0         # none of the chainmail's pattern

    return paint(vanilla, colour, SLATE)[0]


def legs_paint(vanilla):
    """MageArmorLegs_d, painted on the body: the trousers and the thigh strap."""
    scale = SIZE // vanilla.size[0]

    def colour(i, pts, x, y, c, d):
        return (cloth(CHARCOAL, int(x * scale), int(y * scale)), 0.0) if len(pts) > 1000 else (LEATHER, 1.0)

    return paint(vanilla, colour, CHARCOAL, measure=(0,))[0]


def cape_style():
    """CapeLinen's style texture, sampled with the cape's own UVs: plain slate. Its shoulder flaps share the
    hem's UVs, so there is no hem. Both sides share UVs too, so the red lining is a second material
    (BrotherhoodCape in Armor/Brotherhood.cs)."""
    return Image.new("RGBA", (SIZE, SIZE), SLATE + (255,))  # small ones tint it wrongly; keep the size


def normal_map(height, strength):
    """A SIZE x SIZE normal map from height(x, y), tiling."""
    h = [[height(x, y) for x in range(SIZE)] for y in range(SIZE)]
    img = Image.new("RGBA", (SIZE, SIZE))
    px = img.load()
    for y in range(SIZE):
        for x in range(SIZE):
            dx = (h[y][(x + 1) % SIZE] - h[y][x - 1]) * strength
            dy = (h[(y + 1) % SIZE][x] - h[y - 1][x]) * strength
            n = math.sqrt(dx * dx + dy * dy + 1)
            # Unity's normal maps: +y is up the texture (image rows run down), alpha 255 for its RG/AG decode
            px[x, y] = (int((-dx / n * 0.5 + 0.5) * 255), int((dy / n * 0.5 + 0.5) * 255), int((1 / n * 0.5 + 0.5) * 255), 255)
    return img


def linen_normal(vanilla, leather_from, period=2, strength=0.14, seed=11):
    """A linen weave as a normal map (SIZE x SIZE) on the body paint's layout: threads crossing over and under,
    each a little uneven. Flat on the gloves (island 1 from row `leather_from`), which are leather."""
    rnd = random.Random(seed)
    slub = [1 + rnd.gauss(0, 0.15) for _ in range(SIZE)]  # some threads thicker than others

    def height(x, y):
        warp = abs(math.sin(math.pi * x / period)) * slub[x % SIZE]
        weft = abs(math.sin(math.pi * y / period)) * slub[(y * 7) % SIZE]
        over = (x // period + y // period) % 2   # which thread is on top at this crossing
        return warp if over else weft

    img = normal_map(height, strength)
    px = img.load()
    scale = SIZE // vanilla.size[0]
    gloves = islands(vanilla)[1]
    for vx, vy in gloves:
        if vy >= leather_from:
            for k in range(scale * scale):
                px[vx * scale + k % scale, vy * scale + k // scale] = (128, 128, 255, 255)
    return img


def smooth_normal(vanilla):
    """A vanilla normal map (Unity's packing: x in alpha, y in green) at SIZE, softened: its big folds stay,
    its 128-pixel steps, which light the hood and vest in blocks, go."""
    scale = SIZE // vanilla.size[0]
    _, g, _, a = vanilla.split()
    nx, ny = (c.resize((SIZE, SIZE), Image.BICUBIC).filter(ImageFilter.GaussianBlur(scale * 0.9)) for c in (a, g))
    img = Image.new("RGBA", (SIZE, SIZE))
    px, xp, yp = img.load(), nx.load(), ny.load()
    for y in range(SIZE):
        for x in range(SIZE):
            vx, vy = xp[x, y] / 255 * 2 - 1, yp[x, y] / 255 * 2 - 1
            vz = math.sqrt(max(0.0, 1 - vx * vx - vy * vy))
            r = int((vx * 0.5 + 0.5) * 255)
            px[x, y] = (r, yp[x, y], int((vz * 0.5 + 0.5) * 255), r)  # x in red and alpha, for either decode
    return img


def flat(colour):
    """A tiny uniform texture: a flat normal map (no chainmail links) or a plain metal map."""
    return Image.new("RGBA", (4, 4), colour)


def metal_map(vanilla, metal, also=lambda i, c: False):
    """Metallic (red) and smoothness (alpha) map on a vanilla layout: only the islands in `metal` shine, and
    texels `also(island, vanilla pixel)` picks."""
    return paint(vanilla, lambda i, pts, x, y, c, d: ((200, 0, 0), 0.55) if i in metal or also(i, c) else ((0, 0, 0), 0.1), (0, 0, 0))[0]


def main():
    src = sys.argv[1]
    os.makedirs(OUT, exist_ok=True)

    def load(name):
        return Image.open(os.path.join(src, f"tex_{name}.png")).convert("RGBA")

    hb = hood_and_boots(load("MageArmorRed_d"))
    hb.save(os.path.join(OUT, "BrotherhoodHood_MainTex.png"))
    hb.save(os.path.join(OUT, "BrotherhoodLegs_MainTex.png"))
    robe(load("AshlandsMageArmor_d")).save(os.path.join(OUT, "BrotherhoodRobe_MainTex.png"))
    chest_paint(load("AshlandsMageArmorChest_d")).save(os.path.join(OUT, "BrotherhoodRobe_ChestTex.png"))
    legs_paint(load("MageArmorLegs_d")).save(os.path.join(OUT, "BrotherhoodLegs_LegsTex.png"))
    cape_style().save(os.path.join(OUT, "BrotherhoodCape_StyleTex.png"))
    hood_normal = smooth_normal(load("MageArmor_n"))
    hood_normal.save(os.path.join(OUT, "BrotherhoodHood_BumpMap.png"))
    hood_normal.save(os.path.join(OUT, "BrotherhoodLegs_BumpMap.png"))
    smooth_normal(load("AshlandsMageArmor_n")).save(os.path.join(OUT, "BrotherhoodRobe_BumpMap.png"))
    # The body paint's chainmail is in its normal and metal maps: the undershirt and sleeves are linen instead
    chest = load("AshlandsMageArmorChest_d")
    linen_normal(chest, gloves_from(chest)).save(os.path.join(OUT, "BrotherhoodRobe_ChestBumpMap.png"))
    flat((0, 0, 0, 25)).save(os.path.join(OUT, "BrotherhoodRobe_ChestMetal.png"))
    # Vanilla shines the whole mage hood like metal; only clasps, studs and the brooch should
    hood_metal = metal_map(load("MageArmorRed_d"), {5, 6, 8, 9}, lambda i, c: i == 0 and is_clasp(c))
    hood_metal.save(os.path.join(OUT, "BrotherhoodHood_MetallicGlossMap.png"))
    hood_metal.save(os.path.join(OUT, "BrotherhoodLegs_MetallicGlossMap.png"))
    robe_metal = metal_map(load("AshlandsMageArmor_d"), {3, 5, 6, 8, 9, 11, 12})
    robe_metal.save(os.path.join(OUT, "BrotherhoodRobe_MetallicGlossMap.png"))
    print("Painted into", os.path.abspath(OUT))


if __name__ == "__main__":
    main()
