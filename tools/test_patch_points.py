"""Plain tests for patch_points_build.py: validation with synthesized scripts (no game content)."""
import sys

import patch_points_build as build

FAILS = []
SCRIPT = b"synthetic ADDEXP bytes"
OTHER = "sha256:" + "1" * 64


def check(condition, message):
    (FAILS.append(message) or print("FAIL:", message)) if not condition else print("ok:", message)


def run(points):
    return build.validate(points, lambda name: SCRIPT if name == "ADDEXP.BIN" else None,
                          decode=lambda data, name: {0, 7, 0x9a}, profile="himegari")


def point(**overrides):
    value = {"name": "himegari.experience.after_calculation", "script": "addexp.bin",
             "description": "Final award.\n  Not yet applied.",
             "at": [{"fingerprint": build.fingerprint(SCRIPT), "site": 0x9a}]}
    value.update(overrides)
    return value


def errors_of(points):
    return run(points)[1]


def test_valid_point():
    points, errors, warnings = run([point()])
    check(not errors and not warnings, "a point at an instruction start of the installed revision is valid")
    check(points[0]["script"] == "ADDEXP.BIN", "script names are upper-cased")
    check(points[0]["description"] == "Final award. Not yet applied.", "descriptions are reflowed to one line")


def test_rejections():
    cases = [
        (point(name="experience"), "is not a dotted lowercase identifier"),
        (point(name="kamidori.experience.x"), "the first segment must be the profile id 'himegari'"),
        (point(script="MISSING.BIN"), "MISSING.BIN is not in the installed catalog"),
        (point(script="ADDEXP.AGF"), "is not a NAME.BIN script"),
        (point(at=[{"fingerprint": "S4IC422", "site": 0x9a}]), "is not sha256:<64 lowercase hex>"),
        (point(at=[{"fingerprint": build.fingerprint(SCRIPT), "site": 0x9b}]), "0x9b is not an instruction start"),
        (point(at=[]), "needs at least one [[point.at]] entry"),
        (point(at=[{"fingerprint": OTHER, "site": 1}, {"fingerprint": OTHER, "site": 2}]), "is listed twice"),
        (point(extra=1), "unknown keys ['extra']"),
    ]
    for value, reason in cases:
        errors = errors_of([value])
        check(any(reason in error for error in errors), f"rejected: {reason} ({errors})")
    check(any("defined twice" in error for error in errors_of([point(), point()])), "rejected: a duplicate name")


def test_other_revisions_are_a_warning():
    points, errors, warnings = run([point(at=[{"fingerprint": OTHER, "site": 0x9b}])])
    check(not errors, "a site for a revision that is not installed is not checked")
    check(any("no site for the installed ADDEXP.BIN" in warning for warning in warnings),
          "a point without the installed revision warns")


def test_reference_lists_each_revision():
    text = build.render_reference(run([point()])[0], "vm-map/patch-points-himegari.toml")
    check(text.startswith("<!-- DO NOT EDIT"), "the reference is stamped generated")
    check("| `0x9a` |" in text and "## `himegari.experience.after_calculation`" in text, "the reference lists the site")


def main():
    test_valid_point()
    test_rejections()
    test_other_revisions_are_a_warning()
    test_reference_lists_each_revision()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
