"""Tests for tools/launcher_icon_build.py: the path reading, and the check that needs no Pillow."""
from __future__ import annotations

import json
import math
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import launcher_icon_build as icon  # noqa: E402


class PathTests(unittest.TestCase):
    def test_lines_and_arcs_close_on_their_end_points(self) -> None:
        outline = icon._path("M10,0 L20,0 A10,10 0 0 1 20,20 Z")
        self.assertEqual(1, len(outline))
        points = outline[0]
        self.assertEqual([(10.0, 0.0), (20.0, 0.0)], points[:2])
        self.assertEqual((20.0, 20.0), points[-1])
        # A half circle about (20, 10) bulging right: every point sits on the radius, none left of the centre.
        for x, y in points[2:]:
            self.assertAlmostEqual(10.0, math.hypot(x - 20, y - 10), places=6)
            self.assertGreaterEqual(x, 20 - 1e-9)

    def test_the_icons_own_path_reads(self) -> None:
        import xml.etree.ElementTree as ET
        root = ET.fromstring(icon.SOURCE.read_bytes())
        path = next(element for element in root if element.tag.endswith("path"))
        outline = icon._path(path.get("d"))
        self.assertEqual(1, len(outline))
        self.assertGreater(len(outline[0]), 50)

    def test_an_unknown_command_stops(self) -> None:
        with self.assertRaises(ValueError):
            icon._path("M0,0 C1,1 2,2 3,3 Z")


class CheckTests(unittest.TestCase):
    def test_the_check_compares_the_recorded_hash_with_the_svg(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            svg, ico, stamp = root / "icon.svg", root / "icon.ico", root / "icon.source.json"
            svg.write_text("<svg/>", encoding="utf-8")
            ico.write_bytes(b"ico")
            with mock.patch.multiple(icon, REPO=root, SOURCE=svg, ICON=ico, STAMP=stamp):
                self.assertEqual(1, icon.check())                       # no stamp yet
                stamp.write_text(json.dumps({"source_sha256": icon._digest(svg)}), encoding="utf-8")
                self.assertEqual(0, icon.check())
                svg.write_text("<svg></svg>", encoding="utf-8")         # the SVG changed after the build
                self.assertEqual(1, icon.check())

    def test_the_checked_in_icon_is_current(self) -> None:
        self.assertEqual(0, icon.check())


if __name__ == "__main__":
    unittest.main()
