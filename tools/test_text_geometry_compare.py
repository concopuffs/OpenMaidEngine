#!/usr/bin/env python3
"""Content-free tests for text_geometry_compare.py: synthetic shots of a name plate and a text row.

Run: py -3.11 -X utf8 tools/test_text_geometry_compare.py
"""
from __future__ import annotations

import contextlib
import io
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import text_geometry_compare as compare

try:
    from PIL import Image
except ImportError:   # CI's core validation has no Pillow: the shot tests skip, the comparison tests run
    Image = None

GEOMETRY = compare.GEOMETRY["kamidori"]
NEEDS_PILLOW = unittest.skipIf(Image is None, "Pillow not installed")


def shot(path: Path, plate_end: int, glyphs: list[tuple[int, int]]) -> Path:
    """A canvas with the plate's bottom edge running from 105 to PLATE_END and white glyph runs in the first row."""
    image = Image.new("L", (1280, 720), 40)
    pixels = image.load()
    for x in range(105, plate_end + 1):
        pixels[x, GEOMETRY["plate"]["y"]] = 255
    top, bottom = GEOMETRY["rows"][0]
    for first, last in glyphs:
        for x in range(first, last + 1):
            for y in range(top + 2, bottom - 2):
                pixels[x, y] = 255
    image.save(path)
    return path


@NEEDS_PILLOW
class MeasureTests(unittest.TestCase):
    def test_the_plate_edge_and_each_run_of_text_columns_are_measured(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            measured = compare.measure(shot(Path(directory) / "a.png", 141, [(150, 156), (160, 169)]), GEOMETRY)
        self.assertEqual(141, measured["plate_end"])
        self.assertEqual([(150, 156), (160, 169)], measured["rows"][0])
        self.assertEqual([], measured["rows"][1])


class CompareTests(unittest.TestCase):
    def test_runs_a_column_off_pass_within_the_tolerance_only(self) -> None:
        a = {"plate_end": 141, "rows": [[(150, 156), (160, 169)], []]}
        b = {"plate_end": 141, "rows": [[(151, 156), (160, 168)], []]}
        self.assertEqual((True, 1), (compare.compare(a, b, 1)["ok"], compare.compare(a, b, 1)["worst"]))
        self.assertFalse(compare.compare(a, b, 0)["ok"])

    def test_a_plate_sized_differently_fails(self) -> None:
        a = {"plate_end": 141, "rows": [[], []]}
        b = {"plate_end": 164, "rows": [[], []]}
        result = compare.compare(a, b, 2)
        self.assertFalse(result["ok"])
        self.assertIn("plate ends at 141 in A, 164 in B", result["problems"])

    def test_glyphs_whose_ink_touches_in_one_shot_still_match(self) -> None:
        a = {"plate_end": None, "rows": [[(150, 156), (157, 162)], []]}
        b = {"plate_end": None, "rows": [[(150, 156), (158, 162)], []]}
        merged = {"plate_end": None, "rows": [[(150, 162)], []]}
        self.assertTrue(compare.compare(a, merged, 1)["ok"])
        self.assertTrue(compare.compare(a, b, 1)["ok"])

    def test_a_run_with_no_partner_fails(self) -> None:
        a = {"plate_end": None, "rows": [[(150, 156)], []]}
        b = {"plate_end": None, "rows": [[(150, 156), (200, 205)], []]}
        result = compare.compare(a, b, 1)
        self.assertFalse(result["ok"])
        self.assertEqual(["row 1: 1 of B's runs start where the other shot has none within 1 px: [200]"],
                         result["problems"])


@NEEDS_PILLOW
class CommandTests(unittest.TestCase):
    def test_shots_in_both_folders_are_compared_and_a_failure_exits_1(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            a, b = Path(directory) / "a", Path(directory) / "b"
            a.mkdir()
            b.mkdir()
            shot(a / "geometry-p1.png", 141, [(150, 156)])
            shot(b / "geometry-p1.png", 141, [(151, 157)])
            shot(a / "geometry-p2.png", 141, [(150, 156)])
            shot(b / "geometry-p2.png", 187, [(150, 156)])
            shot(a / "geometry-only-a.png", 141, [])
            with contextlib.redirect_stdout(io.StringIO()) as out:
                self.assertEqual(1, compare.main([str(a), str(b)]))
                self.assertEqual(0, compare.main([str(a), str(b), "geometry-p1"]))
        self.assertIn("ok   geometry-p1", out.getvalue())
        self.assertIn("FAIL geometry-p2", out.getvalue())
        self.assertNotIn("only-a", out.getvalue())


if __name__ == "__main__":
    unittest.main()
