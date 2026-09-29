"""Crop the fighter photographs into round 256px face sprites plus their normal maps.

Run from the repo root with Pillow, numpy and scipy installed:

    python Tools/make_faces.py            # every face in FACES
    python Tools/make_faces.py kim tong   # just those

Each source is cropped to a square around the head (the box is authored per photo below,
because a centre crop only works on a tight mugshot), resized to 256, given a circular alpha
with a ~2px soft edge, and written to Assets/Art/Sprites/Faces/face_<id>.png. The normal map is
a dome (so the head reads as round under the Light2D rig) plus a high-pass of the photo's own
luminance for the features, written to Assets/Art/Sprites/Normals/Faces/face_<id>_n.png.
See DOCS/RENDERING.md, "The faces are circular-cropped at import".
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageOps
from scipy import ndimage

SIZE = 256
SOURCE_DIR = Path("Tools/SourceArt/Faces")
FACE_DIR = Path("Assets/Art/Sprites/Faces")
NORMAL_DIR = Path("Assets/Art/Sprites/Normals/Faces")

# id -> (source file, centre x, centre y, square side), in the source's own pixels.
FACES = {
    "baily": ("FACE_Baily.png", 290, 150, 250),
    "decoy": ("FACE_Decoy.png", 285, 68, 126),
    "hegseth": ("FACE_Hegseth.webp", 537, 175, 330),
    "josh": ("FACE_Josh.png", 274, 320, 500),
    "kim": ("FACE_Kim.jpg", 1591, 1790, 760),
    "magamike": ("FACE_MagaMike.png", 290, 300, 520),
    "manzi": ("FACE_Manzi.jpg", 220, 285, 400),
    "sokol": ("FACE_Sokol.webp", 245, 285, 340),
    "trump": ("FACE_Trump.png", 262, 275, 420),
    "velez": ("FACE_Velez.jpg", 165, 175, 270),
    "tong": ("BODY_Tong3.png", 318, 160, 300),
}

DOME_STRENGTH = 4.3
DETAIL_STRENGTH = 10.0
DETAIL_SIGMA = 5.0


def crop(source, cx, cy, side):
    image = ImageOps.exif_transpose(Image.open(SOURCE_DIR / source)).convert("RGB")
    half = side / 2
    box = (round(cx - half), round(cy - half), round(cx + half), round(cy + half))
    return image.crop(box).resize((SIZE, SIZE), Image.LANCZOS)


def circle_alpha():
    centre = (SIZE - 1) / 2
    ys, xs = np.mgrid[0:SIZE, 0:SIZE]
    distance = np.hypot(xs - centre, ys - centre)
    radius = SIZE / 2 - 0.5
    return np.clip((radius - distance) / 2.0 + 0.5, 0, 1), distance / radius


def normal_map(rgb, radial):
    dome = np.sqrt(np.clip(1 - radial**2, 0, 1)) * DOME_STRENGTH
    dome = ndimage.gaussian_filter(dome, 2.0)
    luminance = rgb.astype(float).mean(axis=2) / 255
    detail = luminance - ndimage.gaussian_filter(luminance, DETAIL_SIGMA)
    height = dome * SIZE / 8 + ndimage.gaussian_filter(detail, 0.8) * DETAIL_STRENGTH
    # Image rows run downward, the tangent-space green channel runs up.
    dx = ndimage.sobel(height, axis=1) / 8
    dy = -ndimage.sobel(height, axis=0) / 8
    normal = np.dstack([-dx, -dy, np.ones_like(height)])
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    outside = radial > 1
    normal[outside] = (0, 0, 1)
    encoded = ((normal * 0.5 + 0.5) * 255).round().astype(np.uint8)
    return np.dstack([encoded, np.full((SIZE, SIZE), 255, np.uint8)])


def main(ids):
    FACE_DIR.mkdir(parents=True, exist_ok=True)
    NORMAL_DIR.mkdir(parents=True, exist_ok=True)
    alpha, radial = circle_alpha()
    for face_id in ids:
        source, cx, cy, side = FACES[face_id]
        rgb = np.asarray(crop(source, cx, cy, side))
        sprite = np.dstack([rgb, (alpha * 255).round().astype(np.uint8)])
        Image.fromarray(sprite, "RGBA").save(FACE_DIR / f"face_{face_id}.png")
        Image.fromarray(normal_map(rgb, radial), "RGBA").save(NORMAL_DIR / f"face_{face_id}_n.png")
        print(f"face_{face_id}")


if __name__ == "__main__":
    main(sys.argv[1:] or list(FACES))
