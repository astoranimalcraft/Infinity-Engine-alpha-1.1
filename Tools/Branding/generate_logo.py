#!/usr/bin/env python3
"""Draw the Infinity Engine mark and write every icon the editor and player read.

Run from the repository root:
    python Tools/Branding/generate_logo.py

The engine icon is a thick infinity ribbon on pure black. The project icon is the
same ribbon on a transparent background, so a game can use it as its own icon.
Both are written as multi-size .ico and .png, plus the RGBA blob the runtime
window uses for its title bar and taskbar button.
"""

from __future__ import annotations

import math
import struct
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
BRANDING = ROOT / "Prowl.Editor" / "Resources" / "Branding"
SIZES = (16, 24, 32, 48, 64, 128, 256)
MASTER = 1024
SS = 4

CYAN = (35, 217, 255)
BLUE = (59, 130, 246)
VIOLET = (168, 85, 247)
BLACK = (0, 0, 0)


def lerp(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def infinity_points(scale: float, width: float) -> list[tuple[float, float]]:
    """A horizontal lemniscate: two round lobes joined at the centre.

    Gerono's curve, sampled densely. It gives a true infinity silhouette instead
    of the diagonal cross a rotated Bernoulli lemniscate produces.
    """
    cx = MASTER * 0.5 * scale
    cy = MASTER * 0.5 * scale
    out: list[tuple[float, float]] = []
    steps = 720
    for i in range(steps + 1):
        t = i / steps * math.tau
        x = math.sin(t)
        y = math.sin(t) * math.cos(t) * 0.78
        out.append((cx + x * width * scale, cy + y * width * scale))
    return out


def gradient(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size))
    draw = ImageDraw.Draw(image)
    for x in range(size):
        t = x / max(1, size - 1)
        color = lerp(CYAN, BLUE, t * 2) if t < 0.5 else lerp(BLUE, VIOLET, (t - 0.5) * 2)
        draw.line([(x, 0), (x, size)], fill=color + (255,))
    return image


def black_background(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size), BLACK + (255,))
    # A very subtle inner edge keeps the black tile readable on a black taskbar.
    draw = ImageDraw.Draw(image)
    radius = int(size * 0.20)
    draw.rounded_rectangle((1, 1, size - 2, size - 2), radius=max(1, radius - 1),
                           outline=(42, 47, 62, 255), width=max(1, size // 220))
    return image


def stroke_mask(size: int, pts: list[tuple[float, float]], width: float) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    # Pillow's joint="curve" leaves gaps with dense samples, so draw the core line
    # and cover every vertex with a circle. This gives a clean round stroke.
    draw.line(pts, fill=255, width=int(width))
    radius = width * 0.5
    for x, y in pts:
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=255)
    return mask


def draw_mark(size: int, width: float, with_background: bool) -> Image.Image:
    pts = infinity_points(size / MASTER, width)
    tint = gradient(size)
    canvas = black_background(size) if with_background else Image.new("RGBA", (size, size), (0, 0, 0, 0))

    # Soft glow, then the solid ribbon, then a narrow specular line along the top.
    glow_mask = stroke_mask(size, pts, width * 0.46).filter(ImageFilter.GaussianBlur(size * 0.020))
    glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    glow.paste(tint, (0, 0), glow_mask.point(lambda v: int(v * 0.55)))
    canvas = Image.alpha_composite(canvas, glow)

    if not with_background:
        rim_mask = stroke_mask(size, pts, width * 0.13).filter(ImageFilter.GaussianBlur(size * 0.003))
        layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        layer.paste((8, 10, 20, 90), (0, 0), rim_mask)
        canvas = Image.alpha_composite(canvas, layer)

    body = stroke_mask(size, pts, width * 0.260)
    tube = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    tube.paste(tint, (0, 0), body)
    canvas = Image.alpha_composite(canvas, tube)

    core = stroke_mask(size, pts, width * 0.060).point(lambda v: int(v * 0.50))
    highlight = Image.new("RGBA", (size, size), (255, 255, 255, 255))
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    layer.paste(highlight, (0, 0), core)
    canvas = Image.alpha_composite(canvas, layer)
    return canvas


def downsample(master: Image.Image, edge: int) -> Image.Image:
    return master.resize((edge, edge), Image.LANCZOS)


def write_rgba(master: Image.Image, file_name: str) -> None:
    chunks = []
    for edge in SIZES:
        frame = downsample(master, edge)
        chunks.append(struct.pack("<ii", edge, edge) + frame.tobytes())
    data = struct.pack("<i", len(chunks)) + b"".join(chunks)
    (ROOT / "Prowl.Editor" / "Resources" / file_name).write_bytes(data)


def main() -> None:
    BRANDING.mkdir(parents=True, exist_ok=True)

    engine = draw_mark(MASTER * SS, 340, with_background=True).resize((MASTER, MASTER), Image.LANCZOS)
    project = draw_mark(MASTER * SS, 340, with_background=False).resize((MASTER, MASTER), Image.LANCZOS)

    engine.save(BRANDING / "InfinityEngine.png", optimize=True)
    engine.save(BRANDING / "InfinityEngine.ico", sizes=[(s, s) for s in SIZES], format="ICO")
    engine.save(BRANDING / "InfinityLogoColor.png", optimize=True)

    project.save(BRANDING / "InfinityProject.png", optimize=True)
    project.save(BRANDING / "InfinityProject.ico", sizes=[(s, s) for s in SIZES], format="ICO")
    project.save(BRANDING / "InfinityLogoTransparent.png", optimize=True)
    downsample(project, 512).save(BRANDING / "InfinityProject-512.png", optimize=True)

    mask = Image.new("RGBA", engine.size, (255, 255, 255, 0))
    mask.putalpha(project.getchannel("A"))
    mask.save(BRANDING / "InfinityLogo.png", optimize=True)

    write_rgba(engine, "prowl-icon.rgba")
    write_rgba(project, "prowl-project-icon.rgba")
    print("Wrote infinity .png/.ico (black and transparent), tintable logo and both window icon blobs")


if __name__ == "__main__":
    main()
