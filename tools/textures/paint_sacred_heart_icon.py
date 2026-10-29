"""Paints the Sacred Heart's icon layers into models/textures/.

The icon is the game's Morgen heart, shrunk into the lower part of the frame (SacredHeart.cs), with our own flame and
cross around it: SacredHeart_IconBack.png goes behind the heart (the flame rising from its vessels),
SacredHeart_IconFront.png over it (the flame's front licks and a gold cross on its face). Painted at 4x, then shrunk to
the game's 64-pixel icon size. HEART_* below must match HeartRect in SacredHeart.cs.

Optional input: the Morgen heart icon, read from the game on your own machine (never committed), as MorgenHeart.png in
the folder given as the first argument; the script then writes preview.png there. Run with Python 3 and Pillow:
    python tools/textures/paint_sacred_heart_icon.py [<folder with MorgenHeart.png>]
"""
import math
import os
import random
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "models", "textures")
SIZE = 64
K = 4                       # paint at 4x, then shrink
N = SIZE * K
# Where the heart sits, in icon pixels from the top left
HEART_SCALE = 0.74
HEART_LEFT = (SIZE - SIZE * HEART_SCALE) / 2 + 2
HEART_TOP = SIZE - SIZE * HEART_SCALE - 1


def blob(draw, cx, cy, w, h, fill):
    draw.ellipse([cx - w, cy - h, cx + w, cy + h], fill=fill)


def tongue(cx, base_y, height, width, lean, seed):
    """One flame tongue as a soft mask: a teardrop with a ragged, leaning tip."""
    rnd = random.Random(seed)
    m = Image.new("L", (N, N), 0)
    d = ImageDraw.Draw(m)
    steps = 40
    for i in range(steps):
        t = i / steps
        y = base_y - t * height
        r = width * (1 - t) ** 0.8 * (0.6 + 0.4 * math.sin(math.pi * min(1, t * 1.6 + 0.2)))
        x = cx + lean * t * t * height + math.sin(t * 7 + seed) * width * 0.15 * t
        r *= 0.85 + 0.3 * rnd.random()
        blob(d, x, y, r, r * 1.2, 255)
    return m.filter(ImageFilter.GaussianBlur(K * 0.6))


def mottle(layer, low, seed):
    """Multiplies the colour by soft noise, for the painted look of the game's icons."""
    rnd = random.Random(seed)
    cells = N // (2 * K)
    noise = Image.new("L", (cells, cells))
    noise.putdata([int(low + (255 - low) * rnd.random()) for _ in range(cells * cells)])
    noise = noise.resize((N, N), Image.BICUBIC)
    r, g, b, a = layer.split()
    return Image.merge("RGBA", tuple(ImageChops.multiply(c, noise) for c in (r, g, b)) + (a,))


def flame(cx, base_y, height, width, seed, tongues):
    """Painted fire like the game's torch: dark-red rim, orange body, yellow core, a few embers."""
    outer = Image.new("L", (N, N), 0)
    for i, (ox, hh, ww, lean) in enumerate(tongues):
        outer = ImageChops.lighter(outer, tongue(cx + ox * width, base_y, height * hh, width * ww, lean, seed + i))
    mid = tongue(cx + 0.05 * width, base_y, height * 0.78, width * 0.72, 0.2, seed + 7)
    core = tongue(cx + 0.05 * width, base_y + 0.02 * height, height * 0.45, width * 0.42, 0.1, seed + 9)
    layer = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    layer.paste((150, 28, 10, 235), (0, 0), outer)
    layer.paste((235, 100, 20, 245), (0, 0), mid)
    layer.paste((255, 215, 110, 255), (0, 0), core)
    layer = mottle(layer, 200, seed)
    rnd = random.Random(seed)
    d = ImageDraw.Draw(layer)
    for _ in range(5):
        x = cx + (rnd.random() - 0.5) * width * 1.8
        y = max(3 * K, base_y - height * (0.7 + rnd.random() * 0.3))
        blob(d, x, y, K * 0.7, K * 0.7, (255, 170, 60, 230))
    return layer


def cross(cx, cy, arm, bar):
    """A worn gold cross, bevelled (lit top left) with a shadow cast down right."""
    m = Image.new("L", (N, N), 0)
    d = ImageDraw.Draw(m)
    d.rectangle([cx - bar, cy - arm * 0.85, cx + bar, cy + arm * 1.25], fill=255)
    d.rectangle([cx - arm * 0.8, cy - bar, cx + arm * 0.8, cy + bar], fill=255)
    m = m.filter(ImageFilter.GaussianBlur(K * 0.3))
    layer = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    layer.paste((25, 5, 5, 200), (K, K), m)
    layer.paste((120, 70, 20, 255), (0, 0), m)
    inner = m.filter(ImageFilter.MinFilter(2 * K - 1))
    layer.paste((215, 160, 60, 255), (0, 0), ImageChops.multiply(inner, m))
    edge = ImageChops.subtract(inner, ImageChops.offset(inner, K, K))
    layer.paste((255, 230, 150, 255), (0, 0), edge.filter(ImageFilter.GaussianBlur(K * 0.4)))
    return mottle(layer, 175, 5)


def shrink(layer):
    return layer.resize((SIZE, SIZE), Image.LANCZOS)


def main():
    back = flame(N * 0.53, N * 0.33, N * 0.27, N * 0.13, 3,
                 [(0, 1.0, 1.0, 0.35), (-0.55, 0.6, 0.55, -0.4), (0.6, 0.68, 0.5, 0.5),
                  (-0.2, 0.8, 0.6, -0.15), (0.3, 0.88, 0.55, 0.6)])
    front = flame(N * 0.53, N * 0.34, N * 0.18, N * 0.09, 11,
                  [(0, 1.0, 1.0, 0.35), (-0.55, 0.6, 0.55, -0.4), (0.6, 0.68, 0.5, 0.5)])
    front.putalpha(front.getchannel("A").point(lambda v: int(v * 0.9)))
    front.alpha_composite(cross(N * 0.5, N * 0.67, N * 0.12, N * 0.03))
    back, front = shrink(back), shrink(front)
    os.makedirs(OUT, exist_ok=True)
    back.save(os.path.join(OUT, "SacredHeart_IconBack.png"))
    front.save(os.path.join(OUT, "SacredHeart_IconFront.png"))
    print("wrote SacredHeart_IconBack.png and SacredHeart_IconFront.png to", os.path.abspath(OUT))

    if len(sys.argv) > 1:
        heart = Image.open(os.path.join(sys.argv[1], "MorgenHeart.png")).convert("RGBA")
        s = round(SIZE * HEART_SCALE)
        icon = back.copy()
        icon.alpha_composite(heart.resize((s, s), Image.LANCZOS), (round(HEART_LEFT), round(HEART_TOP)))
        icon.alpha_composite(front)
        icon.resize((256, 256), Image.LANCZOS).save(os.path.join(sys.argv[1], "preview.png"))


if __name__ == "__main__":
    main()
