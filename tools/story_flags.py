#!/usr/bin/env python3
"""Static story-state flag miner. Scans the 481-script corpus for global variables that feed
branch conditions (comparisons / jcc), gathers evidence, classifies candidates, and emits a
ranked review surface. 100% static -- no runtime, no sweep. See
docs/superpowers/specs/2026-07-07-globals-registry-and-story-flags-design.md.
  (no flag)     -> build/story-flags-candidates.json
  --bootstrap   append skeleton [[global]] entries to vm-map/globals.toml (Task 5)"""
from __future__ import annotations
import os, sys, re, json, argparse, collections
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import sys4load

CMP_OPS   = {0x5a, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f}   # eq ne lt lte gr gre  (operands at arg idx 1,2)
LOGIC_OPS = {0x56, 0x57}                            # and or               (operands at arg idx 1,2)
JCC_OP    = 0xa0                                    # condition at arg idx 0
ASSIGN_OP = 0x55                                    # mov -> writes arg0
GLOBAL_ATYPES = {3, 4, 5, 6, 8}
SCENE_RE = re.compile(r"^S[CP]\d{4}\.BIN$")
KNOWN_UI_TOGGLES = {0x6c9, 0x6ca, 0x6cb, 0x6cd, 0x6cc}
NEAR_UNIVERSAL = 250       # scene-reach at/above this = ADV-chrome-wide, not a story flag


def _excluded_addrs() -> set[int]:
    """Addresses the auto shape map classifies as genuine tables/index pointers -- excluded from
    story-flag candidacy. Deliberately does NOT exclude the auto 'string-table' *label* guesses
    (those are unreliable: 0xa57, a real story flag, is mislabelled string-table)."""
    try:
        data = json.loads((paths.BUILD / "global-var-map.json").read_text(encoding="utf-8"))
    except Exception:
        return set()
    excl = {int(c["addr"], 16) for c in data.get("current_entity_index_candidates", [])}
    for addr_s, e in data.get("globals", {}).items():
        if e.get("kind") == "record-table" and e.get("stride_candidates"):
            excl.add(int(addr_s, 16))
    return excl


def assign_category(ev: dict) -> tuple[str, str]:
    """(category, confidence) from evidence. Never 'high' -- auto-shape."""
    addr = int(ev["address"], 16)
    consts = set(ev["consts"])
    domain_bool = consts <= {0, 1}
    enum_like = len(consts) >= 3 and (max(consts) if consts else 0) <= 32
    has_prog_writer = bool(ev["writers_progression"])
    scene_only_writers = bool(ev["writers"]) and not has_prog_writer
    if addr in KNOWN_UI_TOGGLES or (ev["near_universal"] and domain_bool):
        cat = "ui-toggle"
    elif enum_like and has_prog_writer:
        cat = "story-flag"                       # chapter-like (0x3234)
    elif has_prog_writer and ev["reach_total"] >= 1:
        cat = "story-flag"
    elif not ev["writers"] and ev["reach_scenes"] >= 3 and domain_bool:
        cat = "story-flag"                       # externally/natively set form-like (0xa57)
    elif scene_only_writers:
        cat = "choice-output"
    elif ev["reach_scenes"] >= 2 or ev["reach_total"] >= 2:
        cat = "story-flag"
    else:
        cat = "unknown"
    conf = "med" if (has_prog_writer and ev["reach_total"] >= 3) or ev["reach_scenes"] >= 10 else "low"
    return cat, conf


def mine() -> dict[int, dict]:
    reach_total = collections.defaultdict(set)
    reach_scenes = collections.defaultdict(set)
    consts = collections.defaultdict(set)
    writers = collections.defaultdict(set)
    atypes = collections.defaultdict(set)
    for name, path in paths.scripts().items():
        try:
            scr = sys4load.load(path)
        except Exception:
            continue
        is_scene = bool(SCENE_RE.match(name))
        for ins in scr.instructions:
            op, a = ins.opcode, ins.args
            if op in CMP_OPS or op in LOGIC_OPS:
                idxs = [1, 2]
            elif op == JCC_OP:
                idxs = [0]
            else:
                idxs = []
            for i in idxs:
                if i < len(a) and a[i][0] in GLOBAL_ATYPES:
                    addr = a[i][1]
                    reach_total[addr].add(name)
                    atypes[addr].add(a[i][0])
                    if is_scene:
                        reach_scenes[addr].add(name)
                    for j in idxs:                      # compared-against immediates
                        if j != i and j < len(a) and a[j][0] == 0:
                            consts[addr].add(a[j][1])
            if op == ASSIGN_OP and len(a) >= 1 and a[0][0] in GLOBAL_ATYPES:
                writers[a[0][1]].add(name)
    excl = _excluded_addrs()
    out: dict[int, dict] = {}
    for addr in reach_total:
        if addr in excl:
            continue
        w = writers.get(addr, set())
        w_prog = {n for n in w if not SCENE_RE.match(n)}
        ev = {"address": f"0x{addr:x}",
              "reach_total": len(reach_total[addr]),
              "reach_scenes": len(reach_scenes.get(addr, set())),
              "consts": sorted(consts.get(addr, set())),
              "writers": sorted(w),
              "writers_progression": sorted(w_prog),
              "atypes": sorted(atypes[addr]),
              "near_universal": len(reach_scenes.get(addr, set())) >= NEAR_UNIVERSAL}
        ev["category"], ev["confidence"] = assign_category(ev)
        out[addr] = ev
    return out


def write_candidates(cands: dict[int, dict]) -> Path:
    ranked = sorted(cands.values(), key=lambda e: (-e["reach_scenes"], -e["reach_total"]))
    paths.BUILD.mkdir(parents=True, exist_ok=True)
    p = paths.BUILD / "story-flags-candidates.json"
    p.write_text(json.dumps({"meta": {"note": "static branch-condition mining; review surface, "
                                              "not ground truth", "count": len(ranked)},
                             "candidates": ranked}, ensure_ascii=False, indent=2) + "\n",
                 encoding="utf-8")
    return p


import globals_build as _gb


def skeleton_toml(ev: dict) -> str:
    consts = ", ".join(str(c) for c in ev["consts"][:8])
    domain = "{0,1}" if set(ev["consts"]) <= {0, 1} and ev["consts"] else (f"one of {{{consts}}}" if consts else "?")
    usage = (f"TODO: confirm. Branch-read in {ev['reach_scenes']} scenes / {ev['reach_total']} scripts; "
             f"compared against [{consts}]; "
             f"writers={ev['writers'][:4] or 'none (external/native?)'}.")
    lines = ["[[global]]",
             f'address = "{ev["address"]}"',
             'name = ""',
             f'category = "{ev["category"]}"',
             'type = "int"',
             f'value_domain = "{domain}"',
             f'usage = "{usage}"',
             'source = "auto-shape"',
             f'confidence = "{ev["confidence"]}"',
             "depends_on = []"]
    return "\n".join(lines) + "\n"


def bootstrap(cands: dict[int, dict], toml_path=None) -> int:
    toml_path = Path(toml_path) if toml_path else (paths.VM_MAP / "globals.toml")
    present = set(_gb.load_toml(toml_path)[0]) if toml_path.exists() else set()
    # only story-flag / ui-toggle / choice-output candidates are worth seeding for curation
    seedable = {a: e for a, e in cands.items()
                if e["category"] in ("story-flag", "ui-toggle", "choice-output") and a not in present}
    if not seedable:
        print(f"bootstrap: nothing new to add ({len(present)} already present).")
        return 0
    blocks = [skeleton_toml(seedable[a]) for a in sorted(seedable)]
    with toml_path.open("a", encoding="utf-8") as f:
        f.write("\n" + "\n".join(blocks))
    print(f"bootstrap: appended {len(blocks)} skeletons -> {toml_path}")
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--bootstrap", action="store_true")   # implemented in Task 5
    args = ap.parse_args(argv)
    cands = mine()
    if args.bootstrap:
        return bootstrap(cands)                           # Task 5
    p = write_candidates(cands)
    story = sum(1 for e in cands.values() if e["category"] == "story-flag")
    print(f"mined {len(cands)} branch-read globals ({story} story-flag candidates) -> {p}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
