#!/usr/bin/env python3
"""Draws the Infinity Engine mark and writes every icon format the editor and its shortcuts use.

Run from anywhere:  python3 Tools/InfinityLogo.py
Outputs:
  Prowl.Editor/Resources/Branding/InfinityLogo.png        tintable white mark for the editor UI
  Prowl.Editor/Resources/Branding/InfinityLogoColor.png   colour mark on transparency
  Prowl.Editor/Resources/Branding/InfinityEngine.ico      multi-size Windows icon
  Prowl.Editor/Resources/Branding/InfinityEngine.png      desktop / launcher icon
  Prowl.Editor/Resources/prowl-icon.rgba                  embedded window/taskbar icon
"""

from __future__ import annotations

import math
import struct
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Prowl.Editor" / "Resources" / "Branding"
RES = ROOT / "Prowl.Editor" / "Resources"
S = 2048  # working resolution; every output is downsampled from here


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def mix(c1: tuple[int, int, int], c2: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(int(round(lerp(a, b, t))) for a, b in zip(c1, c2))


def point(t: float) -> tuple[float, float]:
    # Lemniscate of Bernoulli, fitted to the canvas.
    s, c = math.sin(t), math.cos(t)
    d = 1.0 + s * s
    return c / d, s * c / d


def draw_ribbon(size: int, white: bool) -> Image.Image:
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    cx = cy = size / 2
    scale = size * 0.39
    steps = 2600
    base = size * 0.047

    # Back-to-front keeps the crossing readable as one ribbon passing over itself.
    samples = []
    for i in range(steps):
        t = i / steps * math.tau
        x, y = point(t)
        samples.append((x, t, y))
    samples.sort(key=lambda item: item[0])

    for x, t, y in samples:
        px, py = cx + x * scale, cy + y * scale
        taper = 0.38 + 0.62 * abs(math.cos(t))
        r = base * taper
        if white:
            col = (255, 255, 255, 255)
        else:
            k = (math.sin(t) + 1.0) * 0.5
            cyan = (86, 226, 255)
            violet = (126, 92, 255)
            magenta = (255, 92, 214)
            col = mix(cyan, violet, k * 2) if k < 0.5 else mix(violet, magenta, (k - 0.5) * 2)
            col = (*col, 255)
        d.ellipse((px - r, py - r, px + r, py + r), fill=col)

    # A soft inner highlight along the top of the ribbon.
    if not white:
        hi = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        hd = ImageDraw.Draw(hi)
        for x, t, y in samples:
            px, py = cx + x * scale, cy + y * scale - base * 0.34
            r = base * (0.16 + 0.22 * abs(math.cos(t)))
            hd.ellipse((px - r, py - r, px + r, py + r), fill=(255, 255, 255, 150))
        layer = Image.alpha_composite(layer, hi.filter(ImageFilter.GaussianBlur(size * 0.004)))

    return layer


def compose_mark(size: int, white: bool = False) -> Image.Image:
    mark = draw_ribbon(size, white)
    return mark.resize((size, size), Image.LANCZOS)


def compose_app_icon(size: int = 1024) -> Image.Image:
    icon = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(icon)

    # Rounded square with a vertical navy gradient.
    radius = int(size * 0.22)
    for y in range(size):
        k = y / max(1, size - 1)
        col = (
            int(lerp(18, 10, k)),
            int(lerp(20, 12, k)),
            int(lerp(34, 24, k)),
            255,
        )
        d.rounded_rectangle((0, y, size, y + 1), radius=radius, fill=col)

    # Accent ring.
    ring = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    rd = ImageDraw.Draw(ring)
    rd.rounded_rectangle((size * 0.012, size * 0.012, size * 0.988, size * 0.988),
                         radius=radius, outline=(104, 92, 255, 170), width=max(2, int(size * 0.008)))
    icon = Image.alpha_composite(icon, ring)

    mark = draw_ribbon(size, white=False)
    glow = mark.filter(ImageFilter.GaussianBlur(size * 0.035))
    icon = Image.alpha_composite(icon, glow)
    icon = Image.alpha_composite(icon, mark)
    return icon.resize((size, size), Image.LANCZOS)


def save_png(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, "PNG")


def save_ico(img: Image.Image, path: Path) -> None:
    sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    img.save(path, format="ICO", sizes=sizes)


def save_raw_icons(path: Path, images: list[Image.Image]) -> None:
    """Window.SetWindowIcon format: int32 count, then width, height and straight RGBA per image."""
    with path.open("wb") as f:
        f.write(struct.pack("<i", len(images)))
        for img in images:
            rgba = img.convert("RGBA")
            f.write(struct.pack("<ii", rgba.width, rgba.height))
            f.write(rgba.tobytes())


def legacy_main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)

    white = compose_mark(1024, white=True)
    colour = compose_mark(1024, white=False)
    app = compose_app_icon(1024)

    save_png(white, OUT / "InfinityLogo.png")
    save_png(colour, OUT / "InfinityLogoColor.png")
    save_png(app, OUT / "InfinityEngine.png")
    save_ico(app, OUT / "InfinityEngine.ico")

    # The editor looks for a manifest resource ending in ".prowl-icon.rgba".
    save_raw_icons(RES / "prowl-icon.rgba", [
        app.resize((16, 16), Image.LANCZOS),
        app.resize((32, 32), Image.LANCZOS),
        app.resize((64, 64), Image.LANCZOS),
        app.resize((256, 256), Image.LANCZOS),
    ])

    print(f"Infinity Engine logo written to {OUT}")


def main() -> None:
    """Draws the current Infinity Engine mark by delegating to Tools/Branding/generate_logo.py."""
    import runpy

    runpy.run_path(str(Path(__file__).with_name("Branding") / "generate_logo.py"), run_name="__main__")


if __name__ == "__main__":
    main()
