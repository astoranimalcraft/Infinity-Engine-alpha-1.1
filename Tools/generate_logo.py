#!/usr/bin/env python3
"""Generates the Infinity Engine mark, tile, .ico files and the raw RGBA window icon.

Run from the repository root: python3 Tools/generate_logo.py
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import struct

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Prowl.Editor" / "Resources"
BRAND = OUT / "Branding"
BRAND.mkdir(parents=True, exist_ok=True)
SIZE = 1024
SIZES = [16, 24, 32, 48, 64, 128, 256]


def gradient(size, top, bottom):
    im = Image.new("RGB", (size, size))
    d = ImageDraw.Draw(im)
    for y in range(size):
        t = y / max(1, size - 1)
        d.line([(0, y), (size, y)], fill=tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return im


def infinity_mark(size, color=(255, 255, 255, 255), glow=(142, 102, 255)):
    """Two interlocking loops, drawn as a soft glow under a crisp stroke."""
    s = size
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    g = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    gd = ImageDraw.Draw(g)
    width = max(2, round(s * 0.085))
    pad = s * 0.14
    left = [pad, s * 0.30, s * 0.54, s * 0.70]
    right = [s * 0.46, s * 0.30, s - pad, s * 0.70]
    gd.ellipse(left, outline=(*glow, 170), width=width * 2)
    gd.ellipse(right, outline=(*glow, 170), width=width * 2)
    g = g.filter(ImageFilter.GaussianBlur(max(2, s * 0.035)))
    im.alpha_composite(g)

    d = ImageDraw.Draw(im)
    d.ellipse(left, outline=color, width=width)
    d.ellipse(right, outline=color, width=width)
    # Bright crossing point, the "spark" of the mark.
    r = s * 0.045
    d.ellipse([s * 0.5 - r, s * 0.5 - r, s * 0.5 + r, s * 0.5 + r], fill=(*glow, 255))
    d.ellipse([s * 0.5 - r * 0.55, s * 0.5 - r * 0.55, s * 0.5 + r * 0.55, s * 0.5 + r * 0.55], fill=(255, 255, 255, 255))
    return im


def tile(size=SIZE):
    base = gradient(size, (14, 12, 27), (30, 21, 52)).convert("RGBA")
    # Rounded mask.
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=round(size * 0.22), fill=255)
    base.putalpha(mask)

    # A violet halo behind the mark.
    halo = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    hd = ImageDraw.Draw(halo)
    for r in range(round(size * 0.42), 0, -max(2, size // 90)):
        a = max(0, 26 - int(r / (size * 0.018)))
        hd.ellipse([size / 2 - r, size / 2 - r, size / 2 + r, size / 2 + r], fill=(116, 78, 230, a))
    base.alpha_composite(halo.filter(ImageFilter.GaussianBlur(size * 0.035)))

    mark = infinity_mark(round(size * 0.78))
    base.alpha_composite(mark, (round(size * 0.11), round(size * 0.11)))
    return base


def transparent(size=SIZE, color=(255, 255, 255, 255)):
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    im.alpha_composite(infinity_mark(size, color=color), (0, 0))
    return im


def write_rgba(path, image):
    with open(path, "wb") as f:
        f.write(struct.pack("<i", len(SIZES)))
        for n in SIZES:
            frame = image.resize((n, n), Image.LANCZOS)
            f.write(struct.pack("<ii", n, n))
            f.write(frame.tobytes())


def main():
    icon_tile = tile()
    icon_tile.save(BRAND / "InfinityEngine.png", optimize=True)
    icon_tile.save(BRAND / "InfinityEngine.ico", sizes=[(n, n) for n in SIZES])

    mark = transparent()
    mark.save(BRAND / "InfinityLogoTransparent.png", optimize=True)
    mark.save(BRAND / "InfinityProject.png", optimize=True)
    mark.save(BRAND / "InfinityProject.ico", sizes=[(n, n) for n in SIZES])

    write_rgba(OUT / "prowl-icon.rgba", icon_tile)
    write_rgba(OUT / "prowl-project-icon.rgba", mark)
    print("Wrote Infinity Engine logo assets.")


if __name__ == "__main__":
    main()
