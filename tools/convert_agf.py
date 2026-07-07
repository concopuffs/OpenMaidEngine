#!/usr/bin/env python3
"""Convert AGF images to BMP via AGF2BMP2AGF.exe, into build/textures/.

  py -3.11 -X utf8 tools/convert_agf.py EV052CA.AGF BG030A.AGF ...   # named assets
  py -3.11 -X utf8 tools/convert_agf.py --scene SC0000               # all .AGF in the scene's
                                                                     # SYS4INI section manifest

Searches every extracted/DATA* dir (DATA1 holds BG/CS/CB/CA/AE/EM sprites, DATA2 the EV CGs).
"""
import json
import os
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths

EXE = paths.EXTRACTED / "DATA1" / "AGF2BMP2AGF.exe"     # tool lives in extracted/DATA1
SRC_DIRS = [paths.EXTRACTED / d for d in ("DATA1", "DATA2", "DATA3", "DATA4", "DATA5")]
OUT = paths.BUILD / "textures"


def find(name):
    for d in SRC_DIRS:
        p = d / name
        if p.exists():
            return p
    return None


def scene_agfs(scene):
    """The .AGF asset names in a scene's SYS4INI section manifest."""
    secs = json.loads((paths.BUILD / "asset-sections.json").read_text(encoding="utf-8"))
    files = json.loads((paths.BUILD / "asset-index.json").read_text(encoding="utf-8"))["files"]
    base = secs["scene_base"][scene.upper().removesuffix(".BIN")]
    end = next(s["end"] for s in secs["sections"] if s["start"] == base)
    return [files[p]["name"] for p in range(base, end + 1)
            if files[p]["name"].upper().endswith(".AGF")]


def convert(name):
    src = find(name)
    if not src:
        print(f"NOT FOUND: {name}"); return False
    out_bmp = OUT / (os.path.splitext(name)[0] + ".BMP")
    if out_bmp.exists():
        return True
    tmp = OUT / name
    shutil.copy(src, tmp)
    r = subprocess.run([str(EXE), name], cwd=str(OUT), capture_output=True, text=True)
    tmp.unlink(missing_ok=True)
    if not out_bmp.exists():
        print(f"FAILED: {name} ({r.stdout.strip()[:60]})"); return False
    return True


def main(argv):
    OUT.mkdir(parents=True, exist_ok=True)
    if argv and argv[0] == "--scene":
        names = scene_agfs(argv[1])
        print(f"{argv[1]}: {len(names)} .AGF assets in manifest")
    else:
        names = argv
    ok = sum(convert(n) for n in names)
    print(f"converted {ok}/{len(names)} -> {OUT.relative_to(paths.REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
