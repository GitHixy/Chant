"""
Generates Chant's icon (icon.png, 512x512) from one set of constants.

It keeps the panel language of FateLimit, LootView, HitSpark and MoogleMap - deep-navy glass and
brass - so the plugins read as siblings. The subject is the Sigil preset itself: a brass-framed ring
filling clockwise in the hold-still red, the last slice of it the green slidecast window with a bright
gate where it opens, and an aether crystal in the middle where the spell's icon would sit.

Run from the repository root:  python tools/make_icon.py
"""
import math

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

# ---------------------------------------------------------------- palette ------
INK_TOP = (0x1A, 0x24, 0x3A)
INK_BOTTOM = (0x05, 0x08, 0x0F)
GLASS = (0x0B, 0x13, 0x22)
GOLD = (0xD6, 0xB0, 0x68)
GOLD_BRIGHT = (0xF6, 0xDE, 0xA2)
GOLD_DEEP = (0x8A, 0x6A, 0x2E)
PARCHMENT = (0xF4, 0xEB, 0xD6)

LOCK = (0xEB, 0x42, 0x3D)
LOCK_HOT = (0xFF, 0x9A, 0x7A)
SAFE = (0x66, 0xF2, 0x99)
CRYSTAL = (0x6F, 0xB8, 0xE0)
CRYSTAL_HOT = (0xD8, 0xF2, 0xFF)

# ---------------------------------------------------------------- geometry -----
SIZE = 512
CORNER = 112
CX, CY = 256, 262
RING_R = 166          # centre line of the band
BAND = 40             # band thickness
FILL = 0.70           # how far round the fill has got
SLIDE = 0.82          # where the slidecast window opens


def layer(n):
    return Image.new('RGBA', (n, n), (0, 0, 0, 0))


def fade(img, alpha):
    r, g, b, a = img.split()
    return Image.merge('RGBA', (r, g, b, a.point(lambda v: int(v * alpha))))


def arc_box(r, k):
    return [(CX - r) * k, (CY - r) * k, (CX + r) * k, (CY + r) * k]


def deg(u):
    """PIL angle (clockwise from three o'clock) for a fraction of the way round from twelve o'clock."""
    return -90 + 360 * u


def point(u, r):
    a = math.radians(deg(u))
    return CX + math.cos(a) * r, CY + math.sin(a) * r


def band(n, k, u0, u1, colour, alpha=255, width=BAND):
    img = layer(n)
    ImageDraw.Draw(img).arc(arc_box(RING_R, k), start=deg(u0), end=deg(u1), fill=colour + (alpha,), width=int(width * k))
    return img


def render_png(path, size=SIZE, ss=4):
    n = size * ss
    k = n / SIZE
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    dist = np.sqrt((xs - CX * k) ** 2 + (ys - CY * k) ** 2) / k

    # --- panel ----------------------------------------------------------------
    ramp = np.linspace(0.0, 1.0, n, dtype=np.float32)[:, None]
    base = np.zeros((n, n, 3), dtype=np.float32)
    for c in range(3):
        base[:, :, c] = INK_TOP[c] * (1 - ramp) + INK_BOTTOM[c] * ramp
    halo = np.clip(1 - np.abs(dist - RING_R) / 70, 0, 1) ** 2
    for c in range(3):
        base[:, :, c] += LOCK[c] * halo * 0.07
    panel = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8), 'RGB').convert('RGBA')
    panel_mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(panel_mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=CORNER * k, fill=255)
    panel.putalpha(panel_mask)
    out = panel

    # --- glass disc inside the ring, lit from the crystal ---------------------------
    inner_r = RING_R - BAND / 2 - 6
    inside = np.clip((inner_r - dist) * k, 0, 1)
    lift = np.clip(1 - dist / inner_r, 0, 1) ** 1.6
    glass = np.zeros((n, n, 3), dtype=np.float32)
    for c in range(3):
        glass[:, :, c] = GLASS[c] + CRYSTAL[c] * 0.22 * lift
    disc = Image.fromarray(np.clip(glass, 0, 255).astype(np.uint8), 'RGB').convert('RGBA')
    disc.putalpha(Image.fromarray((inside * 255).astype(np.uint8), 'L'))
    out = Image.alpha_composite(out, disc)

    # --- the band: dark track, green window, red fill ------------------------------
    out = Image.alpha_composite(out, band(n, k, 0, 1, (0x03, 0x05, 0x0A), 235))
    out = Image.alpha_composite(out, band(n, k, SLIDE, 1, SAFE, 95))

    glow = band(n, k, 0, FILL, LOCK, 255, BAND + 26)
    out = Image.alpha_composite(out, fade(glow.filter(ImageFilter.GaussianBlur(16 * k)), 0.55))
    out = Image.alpha_composite(out, band(n, k, 0, FILL, LOCK))

    # A hotter inner stripe sells the fill as light, not paint.
    stripe = layer(n)
    ImageDraw.Draw(stripe).arc(arc_box(RING_R + BAND * 0.18, k), start=deg(0.01), end=deg(FILL),
                               fill=LOCK_HOT + (150,), width=int(BAND * 0.28 * k))
    out = Image.alpha_composite(out, stripe.filter(ImageFilter.GaussianBlur(1.5 * k)))

    # The gate where the window opens, and the bright head of the fill.
    gate = layer(n)
    gd = ImageDraw.Draw(gate)
    (x0, y0), (x1, y1) = point(SLIDE, RING_R - BAND / 2 - 8), point(SLIDE, RING_R + BAND / 2 + 8)
    gd.line([(x0 * k, y0 * k), (x1 * k, y1 * k)], fill=SAFE + (255,), width=int(7 * k))
    out = Image.alpha_composite(out, fade(gate.filter(ImageFilter.GaussianBlur(8 * k)), 0.8))
    out = Image.alpha_composite(out, gate)

    hx, hy = point(FILL, RING_R)
    head = layer(n)
    ImageDraw.Draw(head).ellipse([(hx - 26) * k, (hy - 26) * k, (hx + 26) * k, (hy + 26) * k], fill=(255, 255, 255, 255))
    out = Image.alpha_composite(out, fade(head.filter(ImageFilter.GaussianBlur(12 * k)), 0.75))
    dot = layer(n)
    ImageDraw.Draw(dot).ellipse([(hx - 13) * k, (hy - 13) * k, (hx + 13) * k, (hy + 13) * k], fill=(255, 0xF0, 0xE6, 255))
    out = Image.alpha_composite(out, dot)

    # --- brass edges either side of the band, with half-second ticks ------------------
    ring = layer(n)
    rd = ImageDraw.Draw(ring)
    outer, inner = RING_R + BAND / 2 + 5, RING_R - BAND / 2 - 5
    rd.ellipse(arc_box(outer, k), outline=GOLD + (255,), width=int(7 * k))
    rd.ellipse(arc_box(inner, k), outline=GOLD_DEEP + (230,), width=int(3.5 * k))
    for i in range(1, 12):
        major = i % 2 == 0
        (ax, ay), (bx, by) = point(i / 12, outer + 4), point(i / 12, outer + (16 if major else 11))
        rd.line([(ax * k, ay * k), (bx * k, by * k)], fill=(GOLD_BRIGHT if major else GOLD_DEEP) + (255,),
                width=int((4 if major else 2.5) * k))
    out = Image.alpha_composite(out, fade(ring.filter(ImageFilter.GaussianBlur(5 * k)), 0.4))
    out = Image.alpha_composite(out, ring)

    shine = layer(n)
    ImageDraw.Draw(shine).arc(arc_box(outer, k), start=200, end=250, fill=GOLD_BRIGHT + (255,), width=int(4 * k))
    out = Image.alpha_composite(out, fade(shine.filter(ImageFilter.GaussianBlur(1.5 * k)), 0.9))

    # --- the aether crystal in the middle ---------------------------------------------
    cx, cy = CX, CY
    h, w = 74, 44
    top, bottom, left, right, mid = (cx, cy - h), (cx, cy + h), (cx - w, cy - 6), (cx + w, cy - 6), (cx, cy - 6)

    aura = layer(n)
    ImageDraw.Draw(aura).ellipse([(cx - 70) * k, (cy - 80) * k, (cx + 70) * k, (cy + 70) * k], fill=CRYSTAL + (255,))
    out = Image.alpha_composite(out, fade(aura.filter(ImageFilter.GaussianBlur(30 * k)), 0.45))

    gem = layer(n)
    gd = ImageDraw.Draw(gem)
    facets = [
        ((top, left, mid), CRYSTAL_HOT),
        ((top, mid, right), (0xA9, 0xDD, 0xF7)),
        ((left, bottom, mid), (0x3E, 0x86, 0xB8)),
        ((mid, bottom, right), (0x5E, 0xA9, 0xD8)),
    ]
    for pts, colour in facets:
        gd.polygon([(x * k, y * k) for x, y in pts], fill=colour + (255,))
    gd.line([(x * k, y * k) for x, y in (top, left, bottom, right, top)], fill=GOLD_BRIGHT + (255,), width=int(4 * k), joint='curve')
    gd.line([(x * k, y * k) for x, y in (left, mid, right)], fill=GOLD + (200,), width=int(2 * k))
    gd.line([(x * k, y * k) for x, y in (top, mid, bottom)], fill=GOLD + (160,), width=int(2 * k))
    out = Image.alpha_composite(out, gem)

    spark = layer(n)
    sd = ImageDraw.Draw(spark)
    sx, sy = cx - 14, cy - 40
    sd.polygon([(sx * k, (sy - 14) * k), ((sx + 3) * k, (sy - 3) * k), ((sx + 14) * k, sy * k), ((sx + 3) * k, (sy + 3) * k),
                (sx * k, (sy + 14) * k), ((sx - 3) * k, (sy + 3) * k), ((sx - 14) * k, sy * k), ((sx - 3) * k, (sy - 3) * k)],
               fill=(255, 255, 255, 235))
    out = Image.alpha_composite(out, spark.filter(ImageFilter.GaussianBlur(0.6 * k)))

    out.putalpha(Image.fromarray(np.minimum(np.asarray(out.getchannel('A')), np.asarray(panel_mask)), 'L'))
    icon = out.resize((size, size), Image.LANCZOS)
    icon.save(path)
    return icon


if __name__ == '__main__':
    render_png('icon.png')
    print('icon.png written')
