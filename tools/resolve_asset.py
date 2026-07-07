#!/usr/bin/env python3
"""Static, general asset resolver: (scene, resId) -> asset file. Solves asset resolution
(docs/asset-resolution-re.md) with NO runtime capture.

Mechanism (proven): SYS4INI's file list is organized into SECTIONS, one per scene -- each is a
`SCxxxx.BIN` script entry followed by that scene's asset MANIFEST (all assets it references, across
every archive and type: EV/BG/CS/AE event & sprite graphics, OGG/WAV audio, ...). `file_number` is
the 0-based index within the section. So a bytecode resId resolves as:

    resId  ->  files[ section_base(scene) + resId ]

where section_base(scene) is the start of the SYS4INI section containing the scene's script.
This is the same rule for set-texture(resId), play-bgm(id), play-voice(id) -- one unified manifest.
Validated: 97% of files fit `fn == position - section_base`; SC0000's opening resolves 17/17 vs
Frida ground truth; 586/595 captured loads across all sections satisfy `files[base+fn] == name`.

Usage:
  py -3.11 -X utf8 tools/resolve_asset.py --build            # emit build/asset-sections.json
  py -3.11 -X utf8 tools/resolve_asset.py <SCENE> [resId]    # resolve one, or dump the manifest
"""
from __future__ import annotations
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths


def load_index():
    return json.loads((paths.BUILD / "asset-index.json").read_text(encoding="utf-8"))["files"]


def sections(files):
    """Split the SYS4INI file list into sections at each file_number reset (fn <= prev).
    Returns (section_start_per_position[list], sections[list of (start, end, scene_name|None])."""
    base_of, secs, start, prev = [], [], 0, -1
    for i, f in enumerate(files):
        if f["file_number"] <= prev:
            secs.append((start, i - 1))
            start = i
        base_of.append(start)
        prev = f["file_number"]
    secs.append((start, len(files) - 1))
    # attach the scene script (SCxxxx.BIN) that owns each section, if any
    out = []
    for s, e in secs:
        scene = next((files[k]["name"] for k in range(s, e + 1)
                      if re.match(r"SC\d+\.BIN$", files[k]["name"])), None)
        out.append({"start": s, "end": e, "scene": scene})
    return base_of, out


def scene_base(files, base_of, scene):
    key = scene.upper()
    if not key.endswith(".BIN"):
        key += ".BIN"
    pos = next((i for i, f in enumerate(files) if f["name"].upper() == key), None)
    if pos is None:
        raise SystemExit(f"scene not in SYS4INI: {scene}")
    return base_of[pos]


def resolve(files, base, resid):
    p = base + resid
    return files[p] if 0 <= p < len(files) else None


def main() -> int:
    files = load_index()
    base_of, secs = sections(files)

    if "--build" in sys.argv:
        scene_bases = {}
        for sec in secs:
            if sec["scene"]:
                scene_bases[sec["scene"].removesuffix(".BIN")] = sec["start"]
        out = paths.BUILD / "asset-sections.json"
        out.write_text(json.dumps(
            {"note": "SYS4INI sections; resolve resId -> files[section_base + resId]. "
                     "See docs/asset-resolution-re.md.",
             "section_count": len(secs),
             "scene_base": scene_bases,
             "sections": secs}, ensure_ascii=False, indent=1), encoding="utf-8")
        print(f"{len(secs)} sections, {len(scene_bases)} scenes -> {out.relative_to(paths.REPO)}")
        return 0

    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    if not args:
        raise SystemExit(__doc__)
    scene = args[0]
    base = scene_base(files, base_of, scene)
    sec = next(s for s in secs if s["start"] == base)
    print(f"{scene}: section [{sec['start']}..{sec['end']}] base {base} "
          f"({sec['end']-sec['start']+1} entries)")

    if len(args) > 1:
        resid = int(args[1], 0)
        f = resolve(files, base, resid)
        print(f"  resId {resid} -> {f['archive']} {f['name']} (offset {f['offset']}, size {f['size']})"
              if f else f"  resId {resid} -> out of range")
        return 0

    # dump the scene's manifest (graphics + audio), skipping the leading script entry
    print("  manifest (resId -> asset):")
    for p in range(base, sec["end"] + 1):
        resid = p - base
        f = files[p]
        print(f"   {resid:>4}  {f['archive'][:5]}  {f['name']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
