#!/usr/bin/env python3
"""Generates the Infinity Engine branding icons.

The engine icon has a rounded dark background; the project icon is the same mark on transparency.
Outputs the PNG/ICO files the editor and player use, plus the multi-size raw RGBA resource the window
loader reads from the entry assembly.
"""
from __future__ import annotations

import math
import struct
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
BRANDING = ROOT / "Prowl.Editor" / "Resources" / "Branding"
SIZE = 1024
ICO_SIZES = (16, 24, 32, 48, 64, 128, 256)


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def mix(c1: tuple[int, int, int], c2: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(round(lerp(a, b, t)) for a, b in zip(c1, c2))


def rounded_background() -> Image.Image:
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    radius = 220
    draw.rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), radius=radius, fill=(11, 16, 32, 255))

    # Vertical navy gradient.
    gradient = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    gd = ImageDraw.Draw(gradient)
    for y in range(SIZE):
        t = y / (SIZE - 1)
        top = mix((12, 20, 42), (6, 10, 22), t)
        gd.line((0, y, SIZE, y), fill=(*top, 255))
    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), radius=radius, fill=255)
    image.paste(gradient, (0, 0), mask)

    # Soft cyan glow behind the mark.
    glow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse((190, 250, 834, 780), fill=(55, 210, 255, 60))
    glow = glow.filter(ImageFilter.GaussianBlur(110))
    image.alpha_composite(glow)

    # A thin, bright inner rim.
    rim = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(rim).rounded_rectangle((10, 10, SIZE - 11, SIZE - 11), radius=radius - 10,
                                         outline=(120, 225, 255, 80), width=3)
    image.alpha_composite(rim)
    return image


def infinity_points(steps: int = 900) -> list[tuple[float, float]]:
    points = []
    for i in range(steps + 1):
        t = math.tau * i / steps
        x = math.cos(t) / (1 + math.sin(t) ** 2)
        y = math.sin(t) * math.cos(t) / (1 + math.sin(t) ** 2)
        points.append((SIZE * 0.5 + x * SIZE * 0.335, SIZE * 0.5 + y * SIZE * 0.67))
    return points


def draw_mark(background: Image.Image | None, with_shadow: bool) -> Image.Image:
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    if background is not None:
        image.alpha_composite(background)

    points = [(int(round(x)), int(round(y))) for x, y in infinity_points()]
    width = 96

    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).line(points, fill=255, width=width, joint="curve")

    if with_shadow:
        shadow_mask = mask.filter(ImageFilter.GaussianBlur(26))
        shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        shadow.putalpha(shadow_mask.point(lambda value: value * 0.55))
        image.alpha_composite(shadow)

    glow_mask = mask.filter(ImageFilter.GaussianBlur(46))
    glow = Image.new("RGBA", (SIZE, SIZE), (54, 226, 255, 0))
    glow.putalpha(glow_mask.point(lambda value: value * 0.5))
    image.alpha_composite(glow)

    gradient = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    gd = ImageDraw.Draw(gradient)
    for x in range(SIZE):
        t = x / (SIZE - 1)
        color = mix((78, 235, 255), (146, 112, 255), t)
        gd.line((x, 0, x, SIZE), fill=(*color, 255))
    gradient.putalpha(mask)
    image.alpha_composite(gradient)

    return image

def save_png(image: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, "PNG", optimize=True)


def save_ico(image: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, "ICO", sizes=[(s, s) for s in ICO_SIZES])


def write_raw_icon(image: Image.Image, path: Path) -> None:
    frames = []
    for size in ICO_SIZES:
        frame = image.resize((size, size), Image.Resampling.LANCZOS).convert("RGBA")
        frames.append((size, frame.tobytes()))
    with path.open("wb") as file:
        file.write(struct.pack("<i", len(frames)))
        for size, pixels in frames:
            file.write(struct.pack("<ii", size, size))
            file.write(pixels)


def main() -> None:
    background = rounded_background()
    engine = draw_mark(background, with_shadow=True)
    project = draw_mark(None, with_shadow=False)

    save_png(engine, BRANDING / "InfinityEngine.png")
    save_ico(engine, BRANDING / "InfinityEngine.ico")
    save_png(project, BRANDING / "InfinityProject.png")
    save_ico(project, BRANDING / "InfinityProject.ico")
    write_raw_icon(engine, ROOT / "Prowl.Editor" / "Resources" / "prowl-icon.rgba")

    # The launcher shows the project mark at 512 too.
    save_png(project.resize((512, 512), Image.Resampling.LANCZOS), BRANDING / "InfinityProject-512.png")
    print("Wrote engine and project icons to", BRANDING)


if __name__ == "__main__":
    main()
