"""Central path anchor for the age-reimpl workspace.

Every path below is derived from this file's own location, so the whole tree can
be relocated without editing any tool -- there are no hard-coded drive paths.

Workspace layout (siblings under the workspace root):

    <workspace>/                         e.g. S:\\Game Hacking\\Eushully\\Himegari
      姫狩りダンジョンマイスター/         pristine game install (AGE.EXE, *.ALF,
                                          loose *.BIN patch-overrides, DLLs)
      extracted/                          extracted ALF data: DATA1 .. DATA5
      age-reimpl/                         our work (this repo)
        tools/  build/  docs/  vm-map/  godot/  bin/

To point the tools at a different install, change GAME_DIR / EXTRACTED here only.
"""
import os
import sys
from pathlib import Path

REPO        = Path(__file__).resolve().parent.parent   # age-reimpl/
WORKSPACE   = REPO.parent                               # workspace root
GAME_DIR    = WORKSPACE / "姫狩りダンジョンマイスター"  # pristine game install
EXTRACTED   = WORKSPACE / "extracted"                   # extracted ALF archives
DATA1       = EXTRACTED / "DATA1"                        # the .BIN script corpus
BUILD       = REPO / "build"                             # derived corpora (regenerable)
VM_MAP      = REPO / "vm-map"
BIN         = REPO / "bin"                               # 3rd-party tools (BinExtractALF, ...)
AGE_EXE     = GAME_DIR / "AGE.EXE"
KELEBEK_CPP = VM_MAP / "kelebek1-age-shared.cpp"


def add_self_to_syspath():
    """Let a standalone script `import paths` / `import sys4load` from tools/."""
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def scripts():
    """Authoritative {UPPERNAME.BIN -> path} map for the script corpus.

    Loose *.BIN patch-overrides in the game dir shadow their extracted/DATA1
    copies (runtime behaviour), so they win on name collision.
    """
    files = {}
    if DATA1.is_dir():
        for p in sorted(DATA1.glob("*.BIN")):
            files[p.name.upper()] = p
    for p in sorted(GAME_DIR.glob("*.BIN")):   # overrides win
        files[p.name.upper()] = p
    return files
