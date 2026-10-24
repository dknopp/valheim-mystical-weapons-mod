"""Paints the Brotherhood set's textures into models/textures/.

Each texture is painted on the layout of the vanilla texture it replaces: the vanilla texture's opaque
islands (one per part of the mesh) say where to paint, and each island gets our own colour. The game
multiplies our colours by the vanilla texture's shading at load (MysticalArmor), as much as our alpha says.

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

from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "models", "textures")
SIZE = 512

# The hero reference's colours
SLATE = (50, 58, 68)
ASH = (108, 111, 115)
CHARCOAL = (42, 44, 47)
LEATHER = (92, 61, 39)
DARK_LEATHER = (60, 40, 27)
RED = (142, 27, 27)
SILVER = (163, 167, 171)
# Edge trims: muted pewter, a step lighter than the cloth they edge, unshaded (vanilla edges are bright)
TRIM = (84, 90, 98)
HOOD_TRIM = (124, 127, 131)


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


def leather(rgb, px, py, worn_edge):
    """Our leather: the grain, and edges darkened where it would wear."""
    k = GRAIN[py][px] * (0.72 if worn_edge else 1.0)
    return tuple(max(0, min(255, int(c * k))) for c in rgb)


def edge(pts, x, y):
    """True if vanilla texel (x, y) is on its island's border."""
    x, y = int(x), int(y)
    return any((x + dx, y + dy) not in pts for dx in (-1, 0, 1) for dy in (-1, 0, 1))


def paint(vanilla, colour_of, background):
    """Paints a SIZE x SIZE texture: colour_of(island index, pts, x, y, vanilla pixel) -> (rgb, shading 0-1),
    where x, y are in vanilla texels (fractional, so bands and lines stay smooth at the higher resolution)."""
    w, h = vanilla.size
    scale = SIZE // w
    isl = islands(vanilla)
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
            rgb, shading = colour_of(i, isl[i], x, y, vpx[vx, vy])
            if rgb in (LEATHER, DARK_LEATHER):
                rgb = leather(rgb, px, py, edge(isl[i], x, y))
            opx[px, py] = rgb + (int(shading * 255),)
    return out, scale, isl


def brightness(c):
    return (0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]) / 255


def bbox(pts):
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def hood_and_boots(vanilla):
    """MageArmorRed_d: the hood, boots and clasps. Shared by the hood and the legs.
    Island numbers are in scan order of the vanilla texture (Valheim 1.0.20), checked in game."""
    def colour(i, pts, x, y, c):
        if i == 7:
            return (HOOD_TRIM, 0.0) if edge(pts, x, y) else (ASH, 0.3)    # the hood
        if i in (1, 2, 3, 4):
            return DARK_LEATHER, 1.0             # rings: boot cuffs and straps
        if i != 0:
            return SILVER, 0.6                   # clasps and studs
        b = brightness(c)                        # 0 mixes boots, soles and the throat clasp
        if b > 0.35:
            return SILVER, 0.6
        if b < 0.09:
            return CHARCOAL, 1.0
        return LEATHER, 1.0

    return paint(vanilla, colour, LEATHER)[0]


def robe(vanilla):
    """AshlandsMageArmor_d: chest front (2) and back (4), front tail (1), side tails (10), bracers (7),
    belt with pouch and cords (0), buckle (3); the rest are rings and studs."""
    def colour(i, pts, x, y, c):
        if i in (1, 2, 4, 10) and edge(pts, x, y):
            return TRIM, 0.0
        if i in (1, 2, 4):
            return SLATE, 0.7
        if i == 10:
            return CHARCOAL, 0.7
        if i in (0, 7):
            return LEATHER, 1.0
        return SILVER, 0.5

    img, scale, isl = paint(vanilla, colour, SLATE)
    crest(img, [v * scale for v in bbox(isl[3])])
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


def gloves_from(vanilla):
    """Row of the body paint where the sleeves end and the gloves begin."""
    return int(vanilla.size[1] * 0.40)


def chest_paint(vanilla):
    """AshlandsMageArmorChest_d, painted on the body: the undershirt (0), whose red shows as a sash between
    the vest and the belt, and the sleeves (1) with the gloves below them."""
    split = gloves_from(vanilla)

    def colour(i, pts, x, y, c):
        if i == 0:
            # Its top rows are the neck and shoulders (read from the body mesh): a dark scarf under the hood
            return (CHARCOAL, 0.3) if y < vanilla.size[1] * 24 / 256 else (RED, 0.3)
        if y >= split:
            return LEATHER, 0.8
        return SLATE, 0.1                        # hide the chainmail's pattern

    return paint(vanilla, colour, SLATE)[0]


def legs_paint(vanilla):
    """MageArmorLegs_d, painted on the body: the trousers and the thigh strap."""
    def colour(i, pts, x, y, c):
        return (CHARCOAL, 0.8) if len(pts) > 1000 else (LEATHER, 1.0)

    return paint(vanilla, colour, CHARCOAL)[0]


def cape_style():
    """CapeLinen's style texture, sampled with the cape's own UVs: plain slate. Its shoulder flaps share the
    hem's UVs, so there is no hem. Both sides share UVs too, so the red lining is a second material
    (BrotherhoodCape in Armor/Brotherhood.cs)."""
    return Image.new("RGBA", (SIZE, SIZE), SLATE + (255,))  # small ones tint it wrongly; keep the size


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
    scale = SIZE // vanilla.size[0]
    gloves = islands(vanilla)[1]
    for vx, vy in gloves:
        if vy >= leather_from:
            for k in range(scale * scale):
                px[vx * scale + k % scale, vy * scale + k // scale] = (128, 128, 255, 255)
    return img


def flat(colour):
    """A tiny uniform texture: a flat normal map (no chainmail links) or a plain metal map."""
    return Image.new("RGBA", (4, 4), colour)


def metal_map(vanilla, metal):
    """Metallic (red) and smoothness (alpha) map on a vanilla layout: only the islands in `metal` shine."""
    return paint(vanilla, lambda i, pts, x, y, c: ((200, 0, 0), 0.55) if i in metal else ((0, 0, 0), 0.1), (0, 0, 0))[0]


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
    # The body paint's chainmail is in its normal and metal maps: the undershirt and sleeves are linen instead
    chest = load("AshlandsMageArmorChest_d")
    linen_normal(chest, gloves_from(chest)).save(os.path.join(OUT, "BrotherhoodRobe_ChestBumpMap.png"))
    flat((0, 0, 0, 25)).save(os.path.join(OUT, "BrotherhoodRobe_ChestMetal.png"))
    # Vanilla shines the whole mage hood like metal; only clasps and studs should
    hood_metal = metal_map(load("MageArmorRed_d"), {5, 6, 8, 9})
    hood_metal.save(os.path.join(OUT, "BrotherhoodHood_MetallicGlossMap.png"))
    hood_metal.save(os.path.join(OUT, "BrotherhoodLegs_MetallicGlossMap.png"))
    robe_metal = metal_map(load("AshlandsMageArmor_d"), {3, 5, 6, 8, 9, 11, 12})
    robe_metal.save(os.path.join(OUT, "BrotherhoodRobe_MetallicGlossMap.png"))
    print("Painted into", os.path.abspath(OUT))


if __name__ == "__main__":
    main()
