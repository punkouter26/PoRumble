"""512x512 Play store icons for PoRumble, PoOlympic, PoHillKing, in the house style of the
existing listings (PoBox, PoFootball, PoRacer): one flat bold symbol on a dark navy radial gradient.
Writes <project>/StoreAssets/PlayStoreIcon_512.png (RGB, no alpha - Play requires it).
Run: py -3 make_store_icons.py"""
import math
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = "C:/Users/punko/Downloads/CODE_MATT"
SIZE, SS = 512, 4
S = SIZE * SS

# Sampled from the live PoBox / PoFootball / PoRacer icons.
BG_CENTER, BG_EDGE = (20, 24, 31), (14, 17, 22)
WHITE = (241, 243, 247)
RED = (232, 62, 62)
BLUE = (64, 128, 240)
ORANGE = (240, 140, 50)
GOLD = (250, 204, 70)
GOLD_DARK = (214, 160, 40)
GREEN = (61, 190, 110)


def background():
    img = Image.new("RGB", (S, S))
    px = img.load()
    c, maxd = S / 2, math.hypot(S / 2, S / 2)
    step = SS * 2  # coarse radial fill; LANCZOS smooths the bands
    d = ImageDraw.Draw(img)
    for y in range(0, S, step):
        for x in range(0, S, step):
            t = min(1.0, math.hypot(x - c, y - c) / maxd)
            col = tuple(int(a + (b - a) * t) for a, b in zip(BG_CENTER, BG_EDGE))
            d.rectangle([x, y, x + step, y + step], fill=col)
    return img


def crown(d, cx, cy, w, h):
    """Three-point crown, base centred at (cx, cy+h/2)."""
    l, r, t, b = cx - w / 2, cx + w / 2, cy - h / 2, cy + h / 2
    d.polygon([(l, b), (l, t + h * 0.15), (cx - w * 0.25, cy), (cx, t),
               (cx + w * 0.25, cy), (r, t + h * 0.15), (r, b)], fill=GOLD)
    d.rectangle([l, b - h * 0.18, r, b], fill=GOLD_DARK)
    for x, y in [(l, t + h * 0.15), (cx, t), (r, t + h * 0.15)]:
        rr = w * 0.07
        d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=GOLD)


def porumble(d):
    # Top-down ring: ropes as a thick rounded square, corner posts red/blue, crown in the middle.
    m, lw = S * 0.20, S * 0.035
    d.rounded_rectangle([m, m, S - m, S - m], radius=S * 0.03, outline=WHITE, width=int(lw))
    inner = m + S * 0.07
    d.rounded_rectangle([inner, inner, S - inner, S - inner], radius=S * 0.02,
                        outline=(90, 98, 112), width=int(lw * 0.45))
    pr = S * 0.055
    for (x, y), col in zip([(m, m), (S - m, m), (S - m, S - m), (m, S - m)], [RED, BLUE, RED, BLUE]):
        d.ellipse([x - pr, y - pr, x + pr, y + pr], fill=col)
    crown(d, S / 2, S / 2, S * 0.30, S * 0.22)


def poolympic(d):
    # Gold medal on a red/white ribbon.
    cx, cy, r = S / 2, S * 0.62, S * 0.24
    S12 = S * 0.18  # ribbon top, inside the frame
    rw = S * 0.11
    d.polygon([(cx - S * 0.20, S12), (cx - S * 0.20 + rw, S12), (cx + rw * 0.4, cy - r * 0.6),
               (cx - rw * 0.6, cy - r * 0.6)], fill=RED)
    d.polygon([(cx + S * 0.20, S12), (cx + S * 0.20 - rw, S12), (cx - rw * 0.4, cy - r * 0.6),
               (cx + rw * 0.6, cy - r * 0.6)], fill=WHITE)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=GOLD)
    ri = r * 0.78
    d.ellipse([cx - ri, cy - ri, cx + ri, cy + ri], outline=GOLD_DARK, width=int(S * 0.018))
    star = [(cx + math.cos(-math.pi / 2 + i * math.pi / 5) * (r * 0.48 if i % 2 == 0 else r * 0.2),
             cy + math.sin(-math.pi / 2 + i * math.pi / 5) * (r * 0.48 if i % 2 == 0 else r * 0.2))
            for i in range(10)]
    d.polygon(star, fill=GOLD_DARK)


def pohillking(d, hill_too=True):
    # Green hill with a crown on the summit.
    hill = [-S * 0.25, S * 0.64, S * 1.25, S * 1.60]
    if hill_too:
        d.ellipse(hill, fill=GREEN)
        d.ellipse(hill, outline=(44, 150, 84), width=int(S * 0.012))
    crown(d, S / 2, S * 0.64 - S * 0.14 + S * 0.01, S * 0.38, S * 0.28)   # base sits on the summit
    w = int(S * 0.022)  # impact ticks, as on PoBox
    d.line([(S * 0.24, S * 0.30), (S * 0.30, S * 0.36)], fill=ORANGE, width=w)
    d.line([(S * 0.20, S * 0.42), (S * 0.28, S * 0.44)], fill=ORANGE, width=w)
    d.line([(S * 0.76, S * 0.30), (S * 0.70, S * 0.36)], fill=ORANGE, width=w)
    d.line([(S * 0.80, S * 0.42), (S * 0.72, S * 0.44)], fill=ORANGE, width=w)


def feature(draw, title, tagline, glow, bottom):
    """1024x500 in PoBox's layout: symbol left, title + tagline + accent bar + studio right, coloured glow
    bottom-right. bottom=True anchors the symbol to the lower edge (the hill grows out of it)."""
    W, H = 1024 * 2, 500 * 2
    img = Image.new("RGB", (W, H), BG_EDGE)
    d = ImageDraw.Draw(img)
    for i in range(60, 0, -1):  # soft glow, bottom-right
        t = i / 60
        col = tuple(int(e + (g - e) * 0.28 * (1 - t)) for e, g in zip(BG_EDGE, glow))
        r = W * 0.55 * t
        d.ellipse([W * 0.95 - r, H * 1.05 - r, W * 0.95 + r, H * 1.05 + r], fill=col)
    sym = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    if bottom:  # wide hill drawn on the canvas itself, so the symbol's square crop never shows
        hill = [-W * 0.10, H * 0.80, W * 0.58, H * 1.90]
        d.ellipse(hill, fill=GREEN)
        d.ellipse(hill, outline=(44, 150, 84), width=int(H * 0.008))
        draw(ImageDraw.Draw(sym), hill_too=False)
    else:
        draw(ImageDraw.Draw(sym))
    side = int(H * 0.62)
    sym = sym.resize((side, side), Image.LANCZOS)
    img.paste(sym, (int(W * 0.10), int(H * 0.80 - side * 0.61) if bottom else (H - side) // 2), sym)
    x = int(W * 0.50)
    def fit(face, size, text):  # shrink until it fits the right half, clear of the edge
        while ImageFont.truetype(face, size).getlength(text) > W * 0.46:
            size -= 2
        return ImageFont.truetype(face, size)
    d.text((x, H * 0.30), title, font=fit("segoeuib.ttf", int(H * 0.19), title), fill=WHITE)
    d.text((x, H * 0.53), tagline, font=fit("segoeui.ttf", int(H * 0.065), tagline), fill=(170, 176, 188))
    d.rounded_rectangle([x, H * 0.66, x + W * 0.06, H * 0.676], radius=H * 0.01, fill=glow)
    d.text((x, H * 0.74), "Punkouter Software", font=ImageFont.truetype("segoeui.ttf", int(H * 0.05)),
           fill=(130, 136, 148))
    return img.resize((1024, 500), Image.LANCZOS)


APPS = [("PoRumble", porumble, "PoRumble", "Sixteen fighters. One left standing.", RED, False),
        ("PoOlympic", poolympic, "PoOlympics", "Ragdoll athletes, learned from scratch", GOLD, False),
        ("PoHillKing", pohillking, "PoHillKing", "Robots fighting for the summit", GREEN, True)]

for project, draw, title, tagline, glow, bottom in APPS:
    img = background()
    draw(ImageDraw.Draw(img))
    out = os.path.join(ROOT, project, "StoreAssets", "PlayStoreIcon_512.png")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    img.resize((SIZE, SIZE), Image.LANCZOS).convert("RGB").save(out)
    feature(draw, title, tagline, glow, bottom).save(os.path.join(ROOT, project, "StoreAssets", "FeatureGraphic_1024x500.png"))
    print(project)
