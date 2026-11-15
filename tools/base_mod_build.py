#!/usr/bin/env python3
"""Generate each game's base mod layout (Lua scripting spec §9.1): mods/<profile>-base/layout/.

A base mod is the per-game package that names the game's cells for Lua mods. Its layout comes from the repository
alone, so no game content is read:

  vm-map/globals[-<profile>].toml      every named global: its bank, address, shape, columns and aliases
  vm-map/patch-points-<profile>.toml   the named patch points, copied so an exported build carries them
  mods/<profile>-base/manifest.toml    the base mod's version; its major version is the version of its API

For each profile with a base mod (a hand-written mods/<profile>-base/manifest.toml) it writes:

  layout/globals.json   the layout the engine builds a mod's accessors from (game.g, game.table)
  layout/points.json    the patch points, in the form of build/games/<profile>/patch-points.json
  layout/exports.txt    the names the base mod exports: its API, checked in, so a review sees a name change

The rename rule (spec §9.1): a name the checked-in exports.txt lists stays exported, as a global's name or as one of
its `aliases`, until the base mod's major version is raised. A rename that does neither is refused.

  --build   write the layouts; refuses a change the rename rule forbids
  --check   exit nonzero if a layout is stale or the rename rule is broken (the core validation gate)
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import tomllib
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths  # noqa: E402
import globals_build  # noqa: E402
import patch_points_build  # noqa: E402

MODS = paths.REPO / "mods"
LAYOUT_FORMAT = 1
IDENTIFIER = re.compile(r"[a-z_][a-z0-9_]*")
COLUMN = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")   # a column is reached as a Lua name; registries use capitals there
SHAPE = re.compile(r"(int|string|float)((?:\[[^\]]*\])*)")
BANKS = {"int": "G", "float": "GF", "string": "GS"}
VERSION = re.compile(r"(\d+)\.(\d+)\.(\d+)")


def base_mod_id(profile_id: str) -> str:
    return f"{profile_id}-base"


def base_mod_dir(profile_id: str, mods: Path = MODS) -> Path:
    return mods / base_mod_id(profile_id)


def globals_source(profile_id: str) -> Path:
    return paths.VM_MAP / ("globals.toml" if profile_id == "himegari" else f"globals-{profile_id}.toml")


def points_source(profile_id: str) -> Path:
    return paths.VM_MAP / f"patch-points-{profile_id}.toml"


def dimensions(type_: str) -> tuple[str, list[int | None]] | None:
    """A registry type as (element type, dimensions): `int[100]` is ("int", [100]); `int[1000][14]` and the
    Kamidori registry's `int[1000x14]` are ("int", [1000, 14]); an open or symbolic dimension (`int[][27]`,
    `int[unit][30]`) is None. None for a type that is no shape."""
    match = SHAPE.fullmatch(type_ or "")
    if not match:
        return None
    dims: list[int | None] = []
    for inner in re.findall(r"\[([^\]]*)\]", match.group(2)):
        if re.fullmatch(r"\d+", inner):
            dims.append(int(inner))
        elif re.fullmatch(r"\d+x\d+", inner):
            dims.extend(int(part) for part in inner.split("x"))
        elif inner == "" or IDENTIFIER.fullmatch(inner):
            dims.append(None)
        else:
            return None
    return match.group(1), dims


def layout_globals(entries: list[dict], source: str) -> tuple[list[dict], list[str]]:
    """The layout's globals, by name, and the errors: a type that is no shape, a dimension of zero, an open dimension
    after the first, a column map on a scalar or past two dimensions, a column out of range or named twice. A column
    map names the last dimension: a row's columns in a 2-D array, the elements of a 1-D one."""
    out: list[dict] = []
    errors: list[str] = []
    for entry in entries:
        name = entry.get("name")
        if not name:
            continue
        where = f"{source}: {name}"
        shape = dimensions(entry.get("type", ""))
        if shape is None:
            errors.append(f"{where}: type {entry.get('type')!r} is not a shape the layout maps")
            continue
        element, dims = shape
        if any(d == 0 for d in dims):
            errors.append(f"{where}: type {entry['type']!r} has a dimension of 0")
            continue
        if None in dims[1:]:
            errors.append(f"{where}: type {entry['type']!r}: only the first dimension may be open")
            continue
        value = {"name": name, "bank": BANKS[element], "address": f"0x{globals_build._parse_addr(entry['address']):x}",
                 "dims": dims}
        columns = entry.get("columns") or {}
        if columns:
            if len(dims) not in (1, 2):
                errors.append(f"{where}: columns name a 1-D array's elements or a 2-D array's columns; the type is "
                              f"{entry['type']!r}")
                continue
            by_name: dict[str, int] = {}
            for index, column in sorted(columns.items(), key=lambda item: int(item[0])):
                if not COLUMN.fullmatch(column):
                    errors.append(f"{where}: column {index} is named {column!r}, which is not a Lua name")
                elif not 0 <= int(index) < (dims[-1] if dims[-1] is not None else 1 << 31):
                    errors.append(f"{where}: column {index} ({column}) is outside its {dims[-1]}")
                elif column in by_name:
                    errors.append(f"{where}: column name {column} is given to columns {by_name[column]} and {index}")
                else:
                    by_name[column] = int(index)
            value["columns"] = by_name
        if entry.get("aliases"):
            value["aliases"] = sorted(entry["aliases"])
        out.append(value)
    out.sort(key=lambda value: value["name"])
    return out, errors


def render_globals(profile_id: str, values: list[dict], source: str) -> str:
    """The layout document, one global per line, so a registry change is a one-line diff."""
    def text(value) -> str:
        return json.dumps(value, ensure_ascii=False)
    lines = ["{",
             f' "note": {text(f"DO NOT EDIT -- generated from {source} by tools/base_mod_build.py --build")},',
             f' "layout": {LAYOUT_FORMAT},',
             f' "profile": {text(profile_id)},',
             ' "globals": [']
    lines += [f"  {text(value)}" + ("," if k + 1 < len(values) else "") for k, value in enumerate(values)]
    lines += [" ]", "}"]
    return "\n".join(lines) + "\n"


def render_points(profile_id: str, registry: Path) -> tuple[str, list[str]]:
    """The patch points as patch_points_build.py writes them, validated without the install."""
    source = f"vm-map/{registry.name}"
    points: list[dict] = []
    errors: list[str] = []
    if registry.is_file():
        points, errors, _ = patch_points_build.validate(patch_points_build.load_registry(registry), None,
                                                        profile=profile_id)
        errors = [f"{source}: {error}" for error in errors]
    document = {"generated_from": source if registry.is_file() else None,
                "note": "DO NOT EDIT -- generated by tools/base_mod_build.py --build",
                "profile": profile_id, "points": points}
    return json.dumps(document, ensure_ascii=False, indent=2) + "\n", errors


def exported_names(values: list[dict]) -> dict[str, str | None]:
    """Each exported name: None for a global's own name, the global's name for an alias."""
    names: dict[str, str | None] = {}
    for value in values:
        names[value["name"]] = None
        for alias in value.get("aliases", []):
            names[alias] = value["name"]
    return names


def render_exports(profile_id: str, major: int, names: dict[str, str | None], source: str) -> str:
    lines = [f"# DO NOT EDIT -- generated from {source} by tools/base_mod_build.py --build",
             f"# The names {base_mod_id(profile_id)} {major} exports (Lua scripting spec §9.1). Each stays until the major",
             "# version changes: a renamed global keeps its old name in its aliases.",
             f"major = {major}"]
    for name in sorted(names):
        lines.append(name if names[name] is None else f"{name} = {names[name]}")
    return "\n".join(lines) + "\n"


def parse_exports(text: str) -> tuple[int | None, set[str]]:
    """The major version and the names of an exports.txt."""
    major = None
    names: set[str] = set()
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        key, _, value = (part.strip() for part in line.partition("="))
        if key == "major":
            major = int(value)
        else:
            names.add(key)
    return major, names


def manifest_major(manifest: Path) -> tuple[int | None, str | None]:
    """The base mod's major version, or an error."""
    try:
        data = tomllib.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, tomllib.TOMLDecodeError) as error:
        return None, f"{manifest.name}: {error}"
    match = VERSION.match(str(data.get("version", "")))
    if not match:
        return None, f"{manifest.name}: version {data.get('version')!r} is not MAJOR.MINOR.PATCH"
    return int(match.group(1)), None


def rename_errors(profile_id: str, released: tuple[int | None, set[str]] | None, major: int,
                  names: dict[str, str | None]) -> list[str]:
    """The rename rule against the checked-in exports (spec §9.1)."""
    if released is None or released[0] is None:
        return []
    released_major, released_names = released
    if major < released_major:
        return [f"{base_mod_id(profile_id)}: the major version {major} is lower than the released {released_major}"]
    if major > released_major:
        return []
    return [f"{base_mod_id(profile_id)} {major} exports '{name}', which the registry no longer has: keep it as an alias "
            f"of the global's new name (aliases = [\"{name}\"]), or raise the base mod's major version"
            for name in sorted(released_names - names.keys())]


def generate(profile_id: str, mods: Path = MODS, registry: Path | None = None,
             points_registry: Path | None = None) -> tuple[dict[Path, str], list[str]]:
    """The files a base mod's layout should hold, and the errors that block them."""
    root = base_mod_dir(profile_id, mods)
    registry = registry or globals_source(profile_id)
    points_registry = points_registry or points_source(profile_id)
    source = f"vm-map/{registry.name}"
    errors: list[str] = []
    major, error = manifest_major(root / "manifest.toml")
    if error:
        return {}, [f"{base_mod_id(profile_id)}: {error}"]
    curated, _ = globals_build.load_toml(registry)
    lint_errors, _ = globals_build.lint(curated, curated.addresses())
    lint_errors += globals_build.lint_aliases(curated)
    errors += [f"{source}: {message}" for message in lint_errors]
    values, shape_errors = layout_globals(list(curated.values()), source)
    errors += shape_errors
    points, point_errors = render_points(profile_id, points_registry)
    errors += point_errors
    names = exported_names(values)
    exports = root / "layout" / "exports.txt"
    released = parse_exports(exports.read_text(encoding="utf-8")) if exports.is_file() else None
    errors += rename_errors(profile_id, released, major, names)
    files = {root / "layout" / "globals.json": render_globals(profile_id, values, source),
             root / "layout" / "points.json": points,
             exports: render_exports(profile_id, major, names, source)}
    return files, errors


def profiles_with_base_mods(mods: Path = MODS) -> list[str]:
    return [profile_id for profile_id in paths.available_profiles()
            if (base_mod_dir(profile_id, mods) / "manifest.toml").is_file()]


def _shown(path: Path) -> str:
    return path.relative_to(paths.REPO).as_posix() if path.is_relative_to(paths.REPO) else str(path)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--build", action="store_true", help="write every base mod's layout")
    mode.add_argument("--check", action="store_true", help="fail if a layout is stale or a rename breaks the rule")
    args = parser.parse_args(argv)
    failed = False
    profiles = profiles_with_base_mods()
    for profile_id in profiles:
        files, errors = generate(profile_id)
        for error in errors:
            print(f"error: {error}")
        if errors:
            failed = True
            continue
        for path, text in files.items():
            current = path.read_text(encoding="utf-8") if path.is_file() else None
            if current == text:
                continue
            if args.check:
                print(f"stale: {_shown(path)}; run base_mod_build.py --build")
                failed = True
            else:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text, encoding="utf-8", newline="\n")
                print(f"wrote {_shown(path)}")
    if not failed:
        print(f"{'check' if args.check else 'build'}: {len(profiles)} base mods current ({', '.join(profiles)})")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
