#!/usr/bin/env python3
"""Build a scratch copy of the selected game install for native AGE experiments (spec §12 gate 6).

The pristine install is never modified or run in place. The scratch copy hard-links the large ``*.ALF`` archives
(AGE only reads them) and copies every other file, so nothing the native game or an experiment writes can reach
the originals through a shared link. Overlay files (loose script overrides, generated ``APPENDnn.AAI``/``.ALF``
catalogs) are copied into the scratch root last, replacing any hard link of the same name rather than writing
through it. A manifest of the pristine install (name, size, modification time)
is written beside the scratch copy; ``--check`` compares the install against it after a native run.

    py -3.11 -X utf8 tools/native_scratch.py --game himegari --overlay build/.../SC0000.BIN --overlay ...
    py -3.11 -X utf8 tools/native_scratch.py --game himegari --check
    py -3.11 -X utf8 tools/native_scratch.py --reference arterial      (a reference-only install, paths.py)
    py -3.11 -X utf8 tools/native_scratch.py --game kamidori --game-root ../games/kamidori-translated --name translated
                                                                        (another install of the game, its own copy)

Native AGE keeps saves under %LOCALAPPDATA% (docs/engine-re.md, "Save-root resolution"), shared with the real
install. Drive the copy with `tools/drive.py native`, which redirects them into a sandbox, or back that folder up
before a run that may save.
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import paths  # noqa: E402

HARD_LINKED = {".alf"}


REFERENCE: str | None = None   # set by --reference: a reference-only install instead of the profile's
NAME: str | None = None        # set by --name: a copy of its own, beside the profile's (another install of the game)


def install_root() -> Path:
    return paths.reference_install(REFERENCE) if REFERENCE else paths.GAME_DIR


def _build_root() -> Path:
    return paths.ENGINES_BUILD / REFERENCE if REFERENCE else paths.GAME_BUILD


def scratch_root() -> Path:
    return _build_root() / (f"native-scratch-{NAME}" if NAME else "native-scratch")


def manifest_path() -> Path:
    return _build_root() / (f"native-scratch-{NAME}-manifest.json" if NAME else "native-scratch-manifest.json")


def snapshot(root: Path) -> dict[str, list[int]]:
    """Name -> [size, mtime_ns] for every file in the install (recursively)."""
    return {
        str(path.relative_to(root)).replace("\\", "/"): [path.stat().st_size, path.stat().st_mtime_ns]
        for path in sorted(root.rglob("*")) if path.is_file()
    }


def build(overlays: list[Path], replace: bool) -> int:
    install, scratch = install_root(), scratch_root()
    if not (install / "SYS4INI.BIN").is_file():
        print(f"native_scratch: no install at {install}", file=sys.stderr)
        return 1
    if scratch.exists():
        if not replace:
            print(f"native_scratch: {scratch} exists; pass --replace to rebuild it", file=sys.stderr)
            return 1
        shutil.rmtree(scratch)
    before = snapshot(install)
    manifest_path().parent.mkdir(parents=True, exist_ok=True)
    manifest_path().write_text(json.dumps({"install": str(install), "files": before}, indent=1), encoding="utf-8")
    linked = copied = 0
    for name in before:
        source, target = install / name, scratch / name
        target.parent.mkdir(parents=True, exist_ok=True)
        if source.suffix.lower() in HARD_LINKED:
            os.link(source, target)
            linked += 1
        else:
            shutil.copy2(source, target)
            copied += 1
    for overlay in overlays:
        target = scratch / overlay.name
        # An overlay named like an install archive lands on its hard link: copying onto the link would write through
        # it into the pristine install, so the link is removed and the overlay becomes a file of its own.
        target.unlink(missing_ok=True)
        shutil.copy2(overlay, target)
    print(f"native_scratch: {scratch}: {linked} archives hard-linked, {copied} files copied, "
          f"{len(overlays)} overlays ({', '.join(overlay.name for overlay in overlays) or 'none'})")
    print(f"native_scratch: launch {scratch / 'AGE.EXE'}")
    return 0


def check() -> int:
    if not manifest_path().is_file():
        print("native_scratch: no manifest; build a scratch copy first", file=sys.stderr)
        return 1
    recorded = json.loads(manifest_path().read_text(encoding="utf-8"))
    now = snapshot(Path(recorded["install"]))
    changed = sorted(name for name in set(now) | set(recorded["files"])
                     if now.get(name) != recorded["files"].get(name))
    if changed:
        print(f"native_scratch: the install changed: {', '.join(changed)}", file=sys.stderr)
        return 1
    print(f"native_scratch: install unchanged ({len(now)} files)")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--overlay", action="append", type=Path, default=[],
                        help="file copied into the scratch root (loose script, AAI/ALF)")
    parser.add_argument("--replace", action="store_true", help="rebuild an existing scratch copy")
    parser.add_argument("--check", action="store_true", help="verify the install against the recorded manifest")
    parser.add_argument("--reference", choices=sorted(paths.REFERENCE_INSTALLS),
                        help="a reference-only install (paths.REFERENCE_INSTALLS) instead of the profile's; "
                             "the copy goes to build/engines/<id>/native-scratch")
    parser.add_argument("--name", help="a copy of its own, native-scratch-NAME, with its own manifest: for another "
                                       "install of the profile's game (--game-root), such as a translated one")
    arguments = parser.parse_args(argv)
    if arguments.name is not None and not arguments.name.replace("-", "").isalnum():
        parser.error("--name takes letters, digits and hyphens")
    global REFERENCE, NAME
    REFERENCE, NAME = arguments.reference, arguments.name
    return check() if arguments.check else build(arguments.overlay, arguments.replace)


if __name__ == "__main__":
    sys.exit(main())
