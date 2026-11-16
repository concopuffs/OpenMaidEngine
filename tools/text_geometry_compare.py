"""Compare where text lands in two runs' screenshots: the localization geometry comparison (gate 12).

Usage:
  py -3.11 -X utf8 tools/text_geometry_compare.py A_SHOTS B_SHOTS [NAME ...] [--game ID] [--tolerance PX]
                                                  [--report OUT.json]

Run A is native AGE on an install translated in place, run B the port on the clean install with the package imported
from it (`omp text import-scripts`) and its language active: presented text is laid out as the same text in the source
would be (localization spec §7), so the two must put every glyph in the same place. The scenario pair
tools/drive/scenarios/geometry/<profile>-translated-{native,port}.steps shoots the same pages under the same names, in
the same face (run B starts from run A's saves); NAME defaults to every `geometry-*.png` in both folders.

For each shot the tool measures, in the profile's ADV window (GEOMETRY below):
- the name plate: the column its bottom edge ends at, which the script sizes from the name's `halve-strlen`;
- each text row: every run of text-coloured columns (a glyph, or glyphs whose ink touches).
AGE's GDI and the port's Godot TextServer antialias the same outlines differently, so pixels are not compared, a run
may start a column off, and two glyphs whose ink touches in one shot may not in the other. A shot passes when the plate
edges are within the tolerance and every run's first column in either shot has one in the other within the tolerance,
or lies inside a run there (the glyphs merged). The exit status is 1 when a shot fails or a named shot is missing.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

# Where each profile's ADV window puts its name plate and text rows, in canvas pixels. `plate`: the row through the
# plate's bottom edge, the columns searched, where its straight run may start, and the level an edge pixel exceeds.
# `rows`: each text row's top and bottom, the columns searched, and the level a text pixel exceeds (the white fill).
GEOMETRY = {
    "kamidori": {
        "plate": {"y": 424, "x0": 90, "x1": 600, "start": (100, 115), "level": 150},
        "rows": [(465, 490), (496, 519)],
        "row_x": (140, 1000),
        "text_level": 200,
    },
}


def runs(columns: list[int]) -> list[tuple[int, int]]:
    """Consecutive columns as (first, last) runs."""
    out: list[tuple[int, int]] = []
    for x in columns:
        if out and x == out[-1][1] + 1:
            out[-1] = (out[-1][0], x)
        else:
            out.append((x, x))
    return out


def measure(path: Path, geometry: dict) -> dict:
    from PIL import Image

    image = Image.open(path).convert("L")
    pixels = image.load()
    plate = geometry["plate"]
    edge = [x for x in range(plate["x0"], plate["x1"]) if pixels[x, plate["y"]] > plate["level"]]
    edges = [run for run in runs(edge) if plate["start"][0] <= run[0] <= plate["start"][1]]
    x0, x1 = geometry["row_x"]
    rows = []
    for top, bottom in geometry["rows"]:
        inked = [x for x in range(x0, x1) if any(pixels[x, y] > geometry["text_level"] for y in range(top, bottom + 1))]
        rows.append(runs(inked))
    return {"plate_end": edges[0][1] if edges else None, "rows": rows}


def compare(a: dict, b: dict, tolerance: int) -> dict:
    problems = []
    if (a["plate_end"] is None) != (b["plate_end"] is None):
        problems.append(f"plate: A {a['plate_end']}, B {b['plate_end']}")
    elif a["plate_end"] is not None and abs(a["plate_end"] - b["plate_end"]) > tolerance:
        problems.append(f"plate ends at {a['plate_end']} in A, {b['plate_end']} in B")
    worst = 0
    for index, (row_a, row_b) in enumerate(zip(a["rows"], b["rows"]), 1):
        for label, mine, other in (("A", row_a, row_b), ("B", row_b, row_a)):
            unmatched = []
            for start, _ in mine:
                nearest = min((abs(start - first) for first, _ in other), default=None)
                if nearest is not None and nearest <= tolerance:
                    worst = max(worst, nearest)
                elif not any(first < start <= last for first, last in other):
                    unmatched.append(start)
            if unmatched:
                problems.append(f"row {index}: {len(unmatched)} of {label}'s runs start where the other shot has "
                                f"none within {tolerance} px: {unmatched[:8]}")
    return {"ok": not problems, "worst": worst, "problems": problems}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0], allow_abbrev=False)
    parser.add_argument("a", type=Path, help="run A's shots: native AGE on the translated install")
    parser.add_argument("b", type=Path, help="run B's shots: the port on the clean install with the package")
    parser.add_argument("names", nargs="*", help="shot names (default: every geometry-*.png in both)")
    parser.add_argument("--game", "--profile", dest="game", default="kamidori", choices=sorted(GEOMETRY),
                        help="the game whose text geometry to compare (--profile is the same option)")
    parser.add_argument("--tolerance", type=int, default=1, help="pixels a column may differ by (default 1)")
    parser.add_argument("--report", type=Path, help="write every shot's measures and result as JSON")
    args = parser.parse_args(argv)

    names = args.names or sorted(path.stem for path in args.a.glob("geometry-*.png")
                                 if (args.b / path.name).is_file())
    if not names:
        print("text_geometry_compare: no shots to compare", file=sys.stderr)
        return 1
    geometry = GEOMETRY[args.game]
    results, failed = [], 0
    for name in names:
        shot_a, shot_b = args.a / f"{name}.png", args.b / f"{name}.png"
        if not (shot_a.is_file() and shot_b.is_file()):
            missing = [str(path) for path in (shot_a, shot_b) if not path.is_file()]
            print(f"FAIL {name}: missing {', '.join(missing)}")
            results.append({"name": name, "ok": False, "problems": [f"missing {', '.join(missing)}"]})
            failed += 1
            continue
        a, b = measure(shot_a, geometry), measure(shot_b, geometry)
        result = {"name": name, "a": a, "b": b, **compare(a, b, args.tolerance)}
        results.append(result)
        failed += not result["ok"]
        counts = "/".join(f"{len(row_a)}:{len(row_b)}" for row_a, row_b in zip(a["rows"], b["rows"]))
        print(f"{'ok  ' if result['ok'] else 'FAIL'} {name}: plate {a['plate_end']} / {b['plate_end']}, "
              f"runs per row (A:B) {counts}, worst column difference {result['worst']}")
        for problem in result["problems"]:
            print(f"     {problem}")
    print(f"{len(names) - failed} of {len(names)} shots within {args.tolerance} px")
    if args.report:
        args.report.write_text(json.dumps(results, ensure_ascii=False, indent=1), encoding="utf-8")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
