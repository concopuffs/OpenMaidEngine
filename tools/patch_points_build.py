#!/usr/bin/env python3
"""Validator and generator for a profile's named patch points (mod loader spec §5).

Source: vm-map/patch-points-<profile>.toml (hand-edited). Generates
  build/games/<profile>/patch-points.json      read by the OMS fragment compiler (`site a.b.c`)
  docs/patch-point-reference[-<profile>].md    the human reference (Himegari's has no suffix)

  --build   validate, then write both outputs; exit nonzero on errors
  --check   validate only

Validation: names are dotted lowercase identifiers led by the profile id; the script is in the installed
catalog; every [[point.at]] fingerprint is well formed and listed once; for the installed revision of the
script (the bytes the catalog serves), the site is an instruction start. A point with no site for the
installed revision is a warning: it may describe another game revision.

Common flags: --profile, --game-root, --extracted-root (tools/paths.py)."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import tomllib
from pathlib import Path
from typing import Callable

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths  # noqa: E402
import sys4load  # noqa: E402

PROFILE = paths.CONTEXT.profile_id
REGISTRY = paths.VM_MAP / f"patch-points-{PROFILE}.toml"
OUTPUT_JSON = paths.CONTEXT.game_build / "patch-points.json"
REFERENCE_DOC = paths.REPO / "docs" / (
    "patch-point-reference.md" if PROFILE == "himegari" else f"patch-point-reference-{PROFILE}.md")

NAME = re.compile(r"^[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+$")
FINGERPRINT = re.compile(r"^sha256:[0-9a-f]{64}$")
POINT_KEYS = {"name", "script", "description", "at"}
AT_KEYS = {"fingerprint", "site"}


def fingerprint(data: bytes) -> str:
    return "sha256:" + hashlib.sha256(data).hexdigest()


def load_registry(path: Path) -> list[dict]:
    with path.open("rb") as handle:
        document = tomllib.load(handle)
    unknown = set(document) - {"point"}
    if unknown:
        raise SystemExit(f"{path.name}: unknown top-level keys {sorted(unknown)}")
    return document.get("point", [])


def installed_scripts() -> dict[str, Path]:
    return paths.scripts()


def instruction_offsets(data: bytes, name: str) -> set[int]:
    return {ins.offset for ins in sys4load.load_bytes(data, name).instructions}


def validate(points: list[dict], read_script: Callable[[str], bytes | None],
             decode: Callable[[bytes, str], set[int]] = instruction_offsets,
             profile: str = PROFILE) -> tuple[list[dict], list[str], list[str]]:
    """Returns the normalized points, the errors and the warnings. `read_script` returns a script's bytes as the
    catalog serves them, or None when the catalog has no such script; `decode` returns its instruction offsets."""
    errors: list[str] = []
    warnings: list[str] = []
    normalized: list[dict] = []
    seen: set[str] = set()
    decoded: dict[str, tuple[str, set[int]]] = {}

    for k, point in enumerate(points):
        where = f"point[{k}]"
        unknown = set(point) - POINT_KEYS
        if unknown:
            errors.append(f"{where}: unknown keys {sorted(unknown)}")
        name = point.get("name")
        if not isinstance(name, str) or not NAME.match(name):
            errors.append(f"{where}: name {name!r} is not a dotted lowercase identifier")
            continue
        where = name
        if name.split(".")[0] != profile:
            errors.append(f"{where}: the first segment must be the profile id '{profile}'")
        if name in seen:
            errors.append(f"{where}: defined twice")
        seen.add(name)

        script = point.get("script")
        if not isinstance(script, str) or not script.upper().endswith(".BIN"):
            errors.append(f"{where}: script {script!r} is not a NAME.BIN script")
            continue
        script = script.upper()
        description = point.get("description")
        if description is not None and not isinstance(description, str):
            errors.append(f"{where}: description must be a string")

        installed = decoded.get(script)
        if installed is None:
            data = read_script(script)
            if data is None:
                errors.append(f"{where}: {script} is not in the installed catalog")
            else:
                installed = decoded[script] = (fingerprint(data), decode(data, script))

        sites = []
        listed: set[str] = set()
        for j, at in enumerate(point.get("at", [])):
            at_where = f"{where}.at[{j}]"
            unknown = set(at) - AT_KEYS
            if unknown:
                errors.append(f"{at_where}: unknown keys {sorted(unknown)}")
            revision, site = at.get("fingerprint"), at.get("site")
            if not isinstance(revision, str) or not FINGERPRINT.match(revision):
                errors.append(f"{at_where}: fingerprint {revision!r} is not sha256:<64 lowercase hex>")
                continue
            if not isinstance(site, int) or site < 0:
                errors.append(f"{at_where}: site {site!r} is not a dword offset")
                continue
            if revision in listed:
                errors.append(f"{at_where}: {revision} is listed twice")
            listed.add(revision)
            if installed and revision == installed[0] and site not in installed[1]:
                errors.append(f"{at_where}: 0x{site:x} is not an instruction start in the installed {script}")
            sites.append({"fingerprint": revision, "site": site})
        if not sites:
            errors.append(f"{where}: needs at least one [[point.at]] entry")
        elif installed and installed[0] not in listed:
            warnings.append(f"{where}: no site for the installed {script} ({installed[0]})")
        normalized.append({"name": name, "script": script,
                           "description": " ".join((description or "").split()) or None, "at": sites})
    return normalized, errors, warnings


def render_reference(points: list[dict], source: str) -> str:
    lines = [f"<!-- DO NOT EDIT -- generated from {source} by tools/patch_points_build.py --build -->",
             f"# Named patch points — {PROFILE} (generated)", "",
             "Stable site names for OMS fragments (`before site <name> { … }`; mod loader spec §5,",
             "`docs/superpowers/specs/2026-11-03-mod-loader-design.md`). Each point lists its site per script revision;",
             "a fragment compiles only against a revision its point lists.", ""]
    if not points:
        lines.append("No points are defined yet.")
    for point in sorted(points, key=lambda p: p["name"]):
        lines += [f"## `{point['name']}`", "", f"Script: `{point['script']}`", ""]
        if point["description"]:
            lines += [point["description"], ""]
        lines += ["| Revision | Site |", "|---|---|"]
        lines += [f"| `{at['fingerprint']}` | `0x{at['site']:x}` |" for at in point["at"]]
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--build", action="store_true", help="validate and write the generated outputs")
    parser.add_argument("--check", action="store_true", help="validate only")
    args = parser.parse_args(argv)
    if not (args.build or args.check):
        parser.error("choose --build or --check")
    if not REGISTRY.exists():
        print(f"no registry at {REGISTRY}; nothing to do")
        return 0

    scripts = installed_scripts()
    points, errors, warnings = validate(
        load_registry(REGISTRY), lambda name: scripts[name].read_bytes() if name in scripts else None)
    for warning in warnings:
        print(f"warning: {warning}")
    for error in errors:
        print(f"error: {error}")
    if errors:
        return 1
    print(f"{REGISTRY.name}: {len(points)} points valid")
    if args.build:
        source = f"vm-map/{REGISTRY.name}"
        OUTPUT_JSON.parent.mkdir(parents=True, exist_ok=True)
        document = {"generated_from": source, "note": "DO NOT EDIT -- generated by tools/patch_points_build.py --build",
                    "profile": PROFILE, "points": points}
        OUTPUT_JSON.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        REFERENCE_DOC.write_text(render_reference(points, source), encoding="utf-8", newline="\n")
        print(f"wrote {OUTPUT_JSON.relative_to(paths.REPO)} and {REFERENCE_DOC.relative_to(paths.REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
