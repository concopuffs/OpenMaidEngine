#!/usr/bin/env python3
"""Generate the modding guide's supported-games page (docs/modding/games/README.md).

Every fact on the page comes from a repository source, so the page does not depend on which game installs exist:

  engine/Age.Engine/Profiles/<id>.json       profile id, titles, engine ABI, catalog revision, boot script, saves
  tools/paths.py                             the conventional install directory
  vm-map/globals[-<id>].toml  [[table]]      the OMS tables and their record counts
  vm-map/patch-points-<id>.toml              named patch points
  oms/<id>/*.oms                             source modules, and the speakers of `enum speaker`

Each game's hand-written guide is docs/modding/games/<id>.md; the page links it, and a profile without one is a
warning.

  --build   write the page
  --check   exit nonzero if the page is missing or out of date (the core validation gate)
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
from globals_build import load_tables  # noqa: E402

GAMES_DIR = paths.REPO / "docs" / "modding" / "games"
OUTPUT = GAMES_DIR / "README.md"
MODULE = re.compile(r"^module\s+([A-Za-z0-9_.]+)", re.M)
SPEAKER_ENUM = re.compile(r"^enum\s+speaker\s*\{(.*?)^\}", re.M | re.S)
ENUM_MEMBER = re.compile(r"^\s*[^\s/=]+\s*=", re.M)


def manifest(profile_id: str) -> dict:
    for path in sorted(paths.PROFILE_MANIFEST_DIR.glob("*.json")):
        data = json.loads(path.read_text(encoding="utf-8"))
        if data["id"] == profile_id:
            return data
    raise KeyError(profile_id)


def globals_toml(profile_id: str) -> Path:
    return paths.VM_MAP / ("globals.toml" if profile_id == "himegari" else f"globals-{profile_id}.toml")


def global_reference(profile_id: str) -> str:
    return "global-reference.md" if profile_id == "himegari" else f"global-reference-{profile_id}.md"


def patch_point_reference(profile_id: str) -> str:
    return "patch-point-reference.md" if profile_id == "himegari" else f"patch-point-reference-{profile_id}.md"


def patch_points(profile_id: str) -> list[str]:
    path = paths.VM_MAP / f"patch-points-{profile_id}.toml"
    if not path.exists():
        return []
    return [point["name"] for point in tomllib.loads(path.read_text(encoding="utf-8")).get("point", [])]


def source_modules(profile_id: str) -> list[tuple[str, str, int | None]]:
    """(module name, repository path, speaker count or None) for each source module."""
    modules = []
    for path in sorted((paths.REPO / "oms" / profile_id).glob("*.oms")):
        text = path.read_text(encoding="utf-8")
        name = MODULE.search(text)
        if not name:
            continue
        enum = SPEAKER_ENUM.search(text)
        speakers = len(ENUM_MEMBER.findall(enum.group(1))) if enum else None
        modules.append((name.group(1), path.relative_to(paths.REPO).as_posix(), speakers))
    return modules


def identity(profile_id: str) -> list[str]:
    data = manifest(profile_id)
    catalog = data["catalogIdentities"][0]
    persistence = data.get("persistence", {})
    install = paths._CONVENTIONAL_GAME_ROOTS.get(profile_id)
    tables = load_tables(globals_toml(profile_id)) if globals_toml(profile_id).exists() else []
    points = patch_points(profile_id)
    modules = source_modules(profile_id)
    guide = GAMES_DIR / f"{profile_id}.md"

    module_cells = [f"`{profile_id}.globals`, `{profile_id}.scripts` (generated)"]
    module_cells += [f"`{name}` (`{path}`)" for name, path, _ in modules]
    speakers = [f"{count} in `{name}`'s `enum speaker`" for name, _, count in modules if count is not None]
    table_cell = ", ".join(f"`{t['name']}[{t['records']}]` ({len(t.get('fields', {}))} fields)" for t in tables)
    points_cell = (", ".join(f"`{p}`" for p in points) + f" ([reference](../../{patch_point_reference(profile_id)}))"
                   if points else "none yet (fragments use offsets)")
    if persistence.get("writesEnabled"):
        saves = f"enabled, in `games/{persistence.get('storageNamespace', profile_id)}/`"
    else:
        saves = "disabled (read-only profile)"

    rows = [
        ("Profile id", f"`{profile_id}` (`--profile {profile_id}`)"),
        ("Title", f"{data['displayTitle']} ({catalog['title']})"),
        ("Engine ABI", f"`{data['engineAbiId']}`"),
        ("Catalog revision", f"`{catalog['catalogRevision']}`"),
        ("Boot script", f"`{data['naturalBootScript']}`"),
        ("Conventional install", f"`{install.name}/` beside the repository" if install else "none (pass `--game-root`)"),
        ("Port saves", saves),
        ("OMS modules", "; ".join(module_cells)),
        ("Speakers", "; ".join(speakers) if speakers else "no dialogue prelude yet"),
        ("Tables", f"{table_cell} ([reference](../../{global_reference(profile_id)}))" if tables else "none yet"),
        ("Named patch points", points_cell),
        ("Guide", f"[{profile_id}.md]({profile_id}.md)" if guide.exists() else "none yet"),
    ]
    lines = [f"## {data['displayTitle']}", "", "| Fact | Value |", "|---|---|"]
    lines += [f"| {fact} | {value} |" for fact, value in rows]
    return lines + [""]


def render() -> str:
    profiles = paths.available_profiles()
    lines = [
        "<!-- DO NOT EDIT -- generated by tools/modding_games_build.py --build from the profile manifests, the",
        "     global registries, the patch-point registries and oms/<profile>/ -->",
        "# Supported games",
        "",
        "The modding guides are written for every game. Each game's facts live in two places: the generated identity",
        "block below, and the game's own guide, which covers its install options, worked-example values and quirks.",
        "Use the profile id wherever a guide writes `<profile>`.",
        "",
        "| Game | Profile | Engine ABI | Guide |",
        "|---|---|---|---|",
    ]
    for profile_id in profiles:
        data = manifest(profile_id)
        guide = f"[{profile_id}.md]({profile_id}.md)" if (GAMES_DIR / f"{profile_id}.md").exists() else "none yet"
        lines.append(f"| {data['displayTitle']} | `{profile_id}` | `{data['engineAbiId']}` | {guide} |")
    lines.append("")
    for profile_id in profiles:
        lines += identity(profile_id)
    lines += [
        "## Adding a game",
        "",
        "A new profile manifest (`engine/Age.Engine/Profiles/<id>.json`) adds a row and an identity block here when",
        "this page is regenerated. Write its guide as `games/<id>.md`, following the existing ones, and keep game",
        "facts out of the generic guides.",
        "",
    ]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--build", action="store_true")
    mode.add_argument("--check", action="store_true")
    arguments, _ = parser.parse_known_args()
    for profile_id in paths.available_profiles():
        if not (GAMES_DIR / f"{profile_id}.md").exists():
            print(f"warning: profile '{profile_id}' has no guide docs/modding/games/{profile_id}.md", file=sys.stderr)
    text = render()
    if arguments.check:
        current = OUTPUT.read_text(encoding="utf-8") if OUTPUT.exists() else None
        if current != text:
            print(f"{OUTPUT.relative_to(paths.REPO).as_posix()} is out of date; run modding_games_build.py --build",
                  file=sys.stderr)
            return 1
        print("modding games page is current")
        return 0
    GAMES_DIR.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(text, encoding="utf-8", newline="\n")
    print(f"wrote {OUTPUT.relative_to(paths.REPO).as_posix()} ({len(paths.available_profiles())} games)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
