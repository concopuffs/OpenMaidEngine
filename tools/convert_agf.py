#!/usr/bin/env python3
"""Convert named AGF files (in extracted/DATA2 or DATA5) to BMP via AGF2BMP2AGF.exe,
into build/textures/. Run: py -3.11 -X utf8 tools/convert_agf.py EV001AA.AGF ..."""
import os, sys, shutil, subprocess
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths

EXE = paths.EXTRACTED / "DATA1" / "AGF2BMP2AGF.exe"  # tool lives in extracted/DATA1
SRC_DIRS = [paths.EXTRACTED / "DATA2", paths.EXTRACTED / "DATA5"]
OUT = paths.BUILD / "textures"

def find(name):
    for d in SRC_DIRS:
        p = d / name
        if p.exists():
            return p
    return None

def main(argv):
    OUT.mkdir(parents=True, exist_ok=True)
    for name in argv:
        src = find(name)
        if not src:
            print(f"NOT FOUND: {name}"); continue
        tmp = OUT / name
        shutil.copy(src, tmp)
        subprocess.run([str(EXE), name], cwd=str(OUT), check=True)
        tmp.unlink(missing_ok=True)  # drop the copied .AGF, keep the .BMP
        print(f"converted {name} -> {OUT / (os.path.splitext(name)[0] + '.BMP')}")
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
