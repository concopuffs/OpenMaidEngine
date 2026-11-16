"""Build the launcher's icon from the OME icon (godot/assets/ome-icon.svg, the single source).

    py -3.11 -X utf8 tools/launcher_icon_build.py --build   # -> launcher/Ome.Launcher/Assets/ome-icon.ico (+ .source.json)
    py -3.11 -X utf8 tools/launcher_icon_build.py --check   # fail if the icon was built from another SVG

The port draws the SVG itself (godot/ShellAssets.cs). The launcher needs a bitmap icon: Avalonia's window icon, and
the `.ico` the build embeds in OME-Launcher.exe (`ApplicationIcon`). One multi-size `.ico` serves both.

The SVG is drawn here with Pillow, at 8 times each size and then reduced, so only what the icon uses is understood:
`rect` (with `rx`), `circle` (a stroke), and `path` with absolute M, L, A and Z, each with a solid fill or stroke.
Anything else stops the build, so a changed icon cannot be drawn wrong in silence.

`--check` needs no Pillow: it compares the SVG's SHA-256 with the one the build recorded beside the icon
(`ome-icon.source.json`), so the core gate runs where Pillow is not installed.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SOURCE = REPO / "godot" / "assets" / "ome-icon.svg"
ICON = REPO / "launcher" / "Ome.Launcher" / "Assets" / "ome-icon.ico"
STAMP = ICON.with_name("ome-icon.source.json")
SIZES = (16, 20, 24, 32, 40, 48, 64, 256)
SUPERSAMPLE = 8
SVG = "{http://www.w3.org/2000/svg}"


def _shown(path: Path) -> str:
    return path.relative_to(REPO).as_posix()


def _digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _colour(text: str | None) -> tuple[int, int, int, int] | None:
    if text is None or text == "none":
        return None
    match = re.fullmatch(r"#([0-9a-fA-F]{6})", text)
    if match is None:
        raise ValueError(f"colour {text!r}: only #rrggbb and none are drawn")
    value = int(match.group(1), 16)
    return (value >> 16 & 0xFF, value >> 8 & 0xFF, value & 0xFF, 255)


def _arc(start, end, rx, ry, rotation, large, sweep, steps_per_radian=24.0):
    """The points of an SVG elliptical arc after `start`, through `end` (SVG 1.1 F.6.5, endpoint to centre)."""
    (x1, y1), (x2, y2) = start, end
    if rx == 0 or ry == 0:
        return [end]
    phi = math.radians(rotation)
    cos, sin = math.cos(phi), math.sin(phi)
    dx, dy = (x1 - x2) / 2, (y1 - y2) / 2
    xp, yp = cos * dx + sin * dy, -sin * dx + cos * dy
    rx, ry = abs(rx), abs(ry)
    scale = xp * xp / (rx * rx) + yp * yp / (ry * ry)
    if scale > 1:
        rx, ry = rx * math.sqrt(scale), ry * math.sqrt(scale)
    numerator = rx * rx * ry * ry - rx * rx * yp * yp - ry * ry * xp * xp
    denominator = rx * rx * yp * yp + ry * ry * xp * xp
    factor = math.sqrt(max(0.0, numerator / denominator)) * (-1 if large == sweep else 1)
    cxp, cyp = factor * rx * yp / ry, -factor * ry * xp / rx
    cx, cy = cos * cxp - sin * cyp + (x1 + x2) / 2, sin * cxp + cos * cyp + (y1 + y2) / 2
    theta = math.atan2((yp - cyp) / ry, (xp - cxp) / rx)
    delta = math.atan2((-yp - cyp) / ry, (-xp - cxp) / rx) - theta
    if sweep and delta < 0:
        delta += 2 * math.pi
    elif not sweep and delta > 0:
        delta -= 2 * math.pi
    steps = max(2, math.ceil(abs(delta) * steps_per_radian))
    points = []
    for step in range(1, steps + 1):
        angle = theta + delta * step / steps
        points.append((cx + rx * cos * math.cos(angle) - ry * sin * math.sin(angle),
                       cy + rx * sin * math.cos(angle) + ry * cos * math.sin(angle)))
    points[-1] = end
    return points


def _path(data: str) -> list[list[tuple[float, float]]]:
    """The closed outlines of a path with absolute M, L, A and Z."""
    tokens = re.findall(r"[A-Za-z]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", data)
    outlines: list[list[tuple[float, float]]] = []
    current: list[tuple[float, float]] = []
    command, at = None, 0

    def number() -> float:
        nonlocal at
        value = float(tokens[at])
        at += 1
        return value

    while at < len(tokens):
        if tokens[at].isalpha():
            command = tokens[at]
            at += 1
            if command == "Z":
                if current:
                    outlines.append(current)
                current = []
                continue
        if command == "M":
            if current:
                outlines.append(current)
            current = [(number(), number())]
            command = "L"   # later pairs are lines, as SVG says
        elif command == "L":
            current.append((number(), number()))
        elif command == "A":
            rx, ry, rotation, large, sweep, x, y = (number() for _ in range(7))
            current.extend(_arc(current[-1], (x, y), rx, ry, rotation, bool(large), bool(sweep)))
        else:
            raise ValueError(f"path command {command!r}: only absolute M, L, A and Z are drawn")
    if current:
        outlines.append(current)
    return outlines


def render(svg: bytes, size: int):
    """The icon at `size` pixels square, as a Pillow RGBA image."""
    from PIL import Image, ImageDraw   # only --build needs Pillow

    root = ET.fromstring(svg)
    view = [float(part) for part in root.get("viewBox", "").split()]
    if len(view) != 4 or view[0] != 0 or view[1] != 0 or view[2] != view[3]:
        raise ValueError("the icon's viewBox must be a square at 0 0")
    big = size * SUPERSAMPLE
    k = big / view[2]
    canvas = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)
    for element in root:
        tag = element.tag.removeprefix(SVG)   # the parser leaves comments out
        fill, stroke = _colour(element.get("fill")), _colour(element.get("stroke"))
        if tag == "rect":
            x, y, w, h = (float(element.get(name)) * k for name in ("x", "y", "width", "height"))
            radius = float(element.get("rx", "0")) * k
            draw.rounded_rectangle([x, y, x + w, y + h], radius=radius, fill=fill)
        elif tag == "circle":
            cx, cy, r = (float(element.get(name)) * k for name in ("cx", "cy", "r"))
            width = float(element.get("stroke-width", "0")) * k
            outer = r + (width / 2 if stroke else 0)
            box = [cx - outer, cy - outer, cx + outer, cy + outer]
            draw.ellipse(box, fill=fill, outline=stroke, width=round(width) if stroke else 0)
        elif tag == "path":
            if stroke is not None:
                raise ValueError("a path's stroke is not drawn")
            for outline in _path(element.get("d", "")):
                draw.polygon([(px * k, py * k) for px, py in outline], fill=fill)
        else:
            raise ValueError(f"<{tag}>: only rect, circle and path are drawn")
    # Reduced with premultiplied alpha, so the transparent corners do not darken the edge.
    return canvas.convert("RGBa").resize((size, size), Image.Resampling.LANCZOS).convert("RGBA")


def build() -> int:
    svg = SOURCE.read_bytes()
    images = [render(svg, size) for size in SIZES]
    ICON.parent.mkdir(parents=True, exist_ok=True)
    out = io.BytesIO()
    largest = images[-1]
    largest.save(out, format="ICO", sizes=[(size, size) for size in SIZES], append_images=images[:-1])
    ICON.write_bytes(out.getvalue())
    STAMP.write_text(json.dumps({
        "generated_by": "tools/launcher_icon_build.py --build (do not edit)",
        "source": _shown(SOURCE),
        "source_sha256": _digest(SOURCE),
        "sizes": list(SIZES),
    }, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"build: {_shown(ICON)} ({len(SIZES)} sizes, {ICON.stat().st_size} bytes) from {_shown(SOURCE)}")
    return 0


def check() -> int:
    if not ICON.is_file() or not STAMP.is_file():
        print(f"check: {_shown(ICON)} or its stamp is missing: run tools/launcher_icon_build.py --build", file=sys.stderr)
        return 1
    recorded = json.loads(STAMP.read_text(encoding="utf-8")).get("source_sha256")
    if recorded != _digest(SOURCE):
        print(f"check: {_shown(ICON)} was built from another {_shown(SOURCE)}: run tools/launcher_icon_build.py --build",
              file=sys.stderr)
        return 1
    print(f"check: {_shown(ICON)} is built from the current {_shown(SOURCE)}")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--build", action="store_true", help="draw the icon and record its source")
    mode.add_argument("--check", action="store_true", help="fail if the icon was built from another SVG")
    args = parser.parse_args(argv)
    return build() if args.build else check()


if __name__ == "__main__":
    sys.exit(main())
