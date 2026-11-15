"""Plain tests for base_mod_build.py and the registry's aliases: synthesized registries, no game content."""
import json
import sys
import tempfile
from pathlib import Path

import base_mod_build as build
import globals_build

FAILS = []


def check(condition, message):
    (FAILS.append(message) or print("FAIL:", message)) if not condition else print("ok:", message)


REGISTRY = '''
[[global]]
address = "0x10"
name = "current_unit_id"
category = "index-pointer"
type = "int"
source = "investigation"
confidence = "high"

[[global]]
address = "0x100"
name = "unit_current_levels"
category = "data-table"
type = "int[100]"
source = "investigation"
confidence = "high"

[[global]]
address = "0x200"
name = "unit_condition_levels"
category = "data-table"
type = "int[unit][30]"
columns = { "6" = "charm", "7" = "confusion" }
source = "investigation"
confidence = "high"

[[global]]
address = "0x0"
name = "unit_names"
category = "string-table"
type = "string[1000x2]"
source = "investigation"
confidence = "high"

[[global]]
address = "0x900"
category = "unknown"
type = "int"
source = "inference"
confidence = "low"
'''


def workspace(directory: Path, registry: str = REGISTRY, version: str = "1.0.0") -> tuple[Path, Path]:
    mods = directory / "mods"
    (mods / "test-base").mkdir(parents=True, exist_ok=True)
    (mods / "test-base" / "manifest.toml").write_text(f'version = "{version}"\n', encoding="utf-8")
    source = directory / "globals-test.toml"
    source.write_text(registry, encoding="utf-8")
    return mods, source


def generate(mods: Path, source: Path):
    return build.generate("test", mods=mods, registry=source, points_registry=source.parent / "none.toml")


def write(files: dict):
    for path, text in files.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")


def layout_of(files: dict) -> dict:
    return {value["name"]: value for value in json.loads(next(text for path, text in files.items()
                                                              if path.name == "globals.json"))["globals"]}


def test_shapes():
    check(build.dimensions("int") == ("int", []), "a scalar has no dimensions")
    check(build.dimensions("string[210]") == ("string", [210]), "a 1-D array")
    check(build.dimensions("int[1000][14]") == ("int", [1000, 14]), "a 2-D array")
    check(build.dimensions("int[1000x14]") == ("int", [1000, 14]), "the RxC form is a 2-D array")
    check(build.dimensions("int[][27]") == ("int", [None, 27]), "an open first dimension")
    check(build.dimensions("int[unit][30]") == ("int", [None, 30]), "a symbolic first dimension is open")
    check(build.dimensions("bool") is None and build.dimensions("int[1.5]") is None, "anything else is no shape")


def test_layout():
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory))
        files, errors = generate(mods, source)
        check(not errors, f"a well-formed registry generates ({errors})")
        layout = layout_of(files)
        check(list(layout) == sorted(layout), "globals are ordered by name")
        check("0x900" not in json.dumps(layout), "an unnamed global is not exported")
        check(layout["unit_names"] == {"name": "unit_names", "bank": "GS", "address": "0x0", "dims": [1000, 2]},
              "a string global is in the string bank, with its shape")
        check(layout["unit_condition_levels"]["dims"] == [None, 30]
              and layout["unit_condition_levels"]["columns"] == {"charm": 6, "confusion": 7},
              "columns map a name to its index in the last dimension")
        exports = next(text for path, text in files.items() if path.name == "exports.txt")
        check(build.parse_exports(exports) == (1, {"current_unit_id", "unit_current_levels", "unit_condition_levels",
                                                   "unit_names"}), "exports.txt lists every name and the major version")
        points = json.loads(next(text for path, text in files.items() if path.name == "points.json"))
        check(points["points"] == [] and points["profile"] == "test", "a profile without points gets an empty copy")


def test_shape_errors():
    with tempfile.TemporaryDirectory() as directory:
        bad = REGISTRY + '''
[[global]]
address = "0x300"
name = "bad_columns"
category = "data-table"
type = "int[4]"
columns = { "4" = "past_the_end" }
source = "investigation"
confidence = "high"

[[global]]
address = "0x400"
name = "bad_shape"
category = "data-table"
type = "int[4][]"
source = "investigation"
confidence = "high"
'''
        mods, source = workspace(Path(directory), bad)
        _, errors = generate(mods, source)
        check(any("bad_columns: column 4 (past_the_end) is outside" in error for error in errors),
              "a column past the last dimension is an error")
        check(any("bad_shape" in error and "only the first dimension may be open" in error for error in errors),
              "an open inner dimension is an error")


def test_rename_rule():
    with tempfile.TemporaryDirectory() as directory:
        mods, source = workspace(Path(directory))
        files, _ = generate(mods, source)
        write(files)
        renamed = REGISTRY.replace('name = "unit_current_levels"', 'name = "unit_levels"')
        source.write_text(renamed, encoding="utf-8")
        _, errors = generate(mods, source)
        check(any("exports 'unit_current_levels', which the registry no longer has" in error for error in errors),
              "a rename without an alias in the same major version is refused")
        source.write_text(renamed.replace('name = "unit_levels"', 'name = "unit_levels"\naliases = ["unit_current_levels"]'),
                          encoding="utf-8")
        files, errors = generate(mods, source)
        check(not errors, f"a rename that keeps the old name as an alias passes ({errors})")
        exports = next(text for path, text in files.items() if path.name == "exports.txt")
        check("unit_current_levels = unit_levels" in exports.splitlines(), "exports.txt lists an alias and its global")
        check(layout_of(files)["unit_levels"]["aliases"] == ["unit_current_levels"], "the layout carries the alias")
        source.write_text(renamed, encoding="utf-8")
        (mods / "test-base" / "manifest.toml").write_text('version = "2.0.0"\n', encoding="utf-8")
        files, errors = generate(mods, source)
        check(not errors and build.parse_exports(next(text for path, text in files.items()
                                                      if path.name == "exports.txt"))[0] == 2,
              "a rename with a raised major version passes and starts the new list")
        write(files)
        (mods / "test-base" / "manifest.toml").write_text('version = "1.4.0"\n', encoding="utf-8")
        _, errors = generate(mods, source)
        check(any("lower than the released 2" in error for error in errors), "the major version cannot go down")


def test_alias_lint():
    registry = REGISTRY.replace('name = "unit_names"', 'name = "unit_names"\naliases = ["current_unit_id", "Bad"]')
    with tempfile.TemporaryDirectory() as directory:
        path = Path(directory) / "globals.toml"
        path.write_text(registry, encoding="utf-8")
        entries, _ = globals_build.load_toml(path)
        errors = globals_build.lint_aliases(entries)
        check(any("alias current_unit_id is also the name of a global" in error for error in errors),
              "an alias may not be another global's name")
        check(any("alias 'Bad' must be a lowercase identifier" in error for error in errors),
              "an alias is a lowercase identifier")
        twice = REGISTRY.replace('name = "unit_names"', 'name = "unit_names"\naliases = ["old"]').replace(
            'name = "unit_current_levels"', 'name = "unit_current_levels"\naliases = ["old"]')
        path.write_text(twice, encoding="utf-8")
        entries, _ = globals_build.load_toml(path)
        check(any("alias old is also an alias of" in error for error in globals_build.lint_aliases(entries)),
              "one alias names one global")


def main():
    test_shapes()
    test_layout()
    test_shape_errors()
    test_rename_rule()
    test_alias_lint()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
