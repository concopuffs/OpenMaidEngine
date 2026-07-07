#!/usr/bin/env python3
"""Correlate the VM's set-texture(resId) trace with the game's Frida load order to LOCALIZE the
asset-resolution scope selector (docs/asset-resolution-re.md step 2, scope-selector hunt).

Inputs:
  build/settex-<SCENE>.json          our VM's ordered set-texture(resId) trace + bytecode offsets
                                     (produce with: py -3.11 -X utf8 tools/vm0.py --settex <SCENE>)
  build/frida-load-order-result.json the game's ordered (name, file_number=resId) loads
                                     (produce with tools/frida/capture_load_order.py --analyze)
  build/asset-index.json             for segmenting DATA2 into packages

Method: `resId == file_number`, and each loaded file belongs to exactly one DATA2 "package"
(a monotonic-file_number run). So the game's load order is a readout of the active package over
time. We greedily align each Frida load to the next same-resId set-texture in the VM trace, which
pins it to a bytecode offset. Where the active package CHANGES between consecutive loads, some
instruction in the bytecode span between their offsets flipped the scope -- the scope selector.
This report lists the aligned loads, flags package transitions, and points at the byte spans to
inspect (dump them with the disassembly in build/disasm/<SCENE>.asm).

Usage: py -3.11 -X utf8 tools/correlate_scope.py <SCENE>   (e.g. SC0000)
"""
from __future__ import annotations
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths


def packages():
    """Segment DATA2 (directory order) into monotonic-file_number runs; return name->pkg index."""
    idx = json.loads((paths.BUILD / "asset-index.json").read_text(encoding="utf-8"))
    d2 = [f for f in idx["files"] if f["archive"] == "DATA2.ALF"]
    name2pkg, pkg, prev = {}, 0, None
    for f in d2:
        fn = f["file_number"]
        if prev is not None and fn <= prev:
            pkg += 1
        name2pkg[f["name"]] = pkg
        prev = fn
    return name2pkg


# boilerplate ops to hide when dumping the between-loads span (leave the structural/effectful ones)
NOISE = {"mov", "add", "sub", "mul", "div", "mod", "and", "or", "sar", "shl", "xor",
         "eq", "ne", "lt", "lte", "gr", "gre", "jcc", "jmp", "set-string",
         "show-text", "end-text-line", "wait-for-input", "stmt-begin", "stmt-end",
         "block-mark", "cond-block", "label-def", "line-id?", "comment", "set-font",
         "lookup-array", "lookup-array-2d", "stmt-desc?", "bit-set", "bit-reset",
         "check-bit", "text-param?", "gfx-geom?", "count?", "resolve-handle?",
         "create-texture", "draw-texture", "draw-string", "draw?", "draw-blit?"}


def disasm_map(scene):
    """offset(int) -> stripped disasm line, from build/disasm/<SCENE>.asm."""
    path = paths.BUILD / "disasm" / f"{scene}.asm"
    out = {}
    if not path.exists():
        return out
    for ln in path.read_text(encoding="utf-8").splitlines():
        s = ln.strip()
        if s[:2] == "0x" and ":" in s:
            off = int(s.split(":", 1)[0], 16)
            out[off] = s.split(":", 1)[1].strip()
    return out


def dump_span(trace, ta, tb, dis):
    """Print the significant (non-NOISE) ops executed between trace indices ta..tb."""
    seen = 0
    for t in range(ta, min(tb, len(trace))):
        off = int(trace[t], 16)
        line = dis.get(off, "")
        mnem = line.split()[0] if line else ""
        if mnem and mnem not in NOISE:
            print(f"       {trace[t]:>8}  {line}")
            seen += 1
    if not seen:
        print("       (no structural/effectful ops in span — only boilerplate)")


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    if not args:
        raise SystemExit(__doc__)
    scene = args[0].upper().removesuffix(".BIN")

    settex_path = paths.BUILD / f"settex-{scene}.json"
    loads_path = paths.BUILD / "frida-load-order-result.json"
    if not settex_path.exists():
        raise SystemExit(f"missing {settex_path.name} — run: tools/vm0.py --settex {scene}")
    if not loads_path.exists():
        raise SystemExit(f"missing {loads_path.name} — run tools/frida/capture_load_order.py --analyze")

    sx = json.loads(settex_path.read_text(encoding="utf-8"))
    vm, trace = sx["settex"], sx.get("trace", [])                             # [{i,off,resId,slot,trace_i}]
    loads = json.loads(loads_path.read_text(encoding="utf-8"))["load_order"]  # [{name,file_number}]
    name2pkg = packages()
    dis = disasm_map(scene)

    # greedy align: each Frida load -> next same-resId set-texture in VM order
    aligned, j, unmatched = [], 0, 0
    for ld in loads:
        fn = ld["file_number"]
        k = j
        while k < len(vm) and vm[k]["resId"] != fn:
            k += 1
        if k < len(vm):
            aligned.append({"vm": vm[k], "resId": fn, "name": ld["name"], "pkg": name2pkg.get(ld["name"])})
            j = k + 1
        else:
            aligned.append({"vm": None, "resId": fn, "name": ld["name"], "pkg": name2pkg.get(ld["name"])})
            unmatched += 1

    print(f"scene {scene}: {len(vm)} VM set-textures, {len(loads)} Frida loads, "
          f"{len(loads)-unmatched} aligned ({unmatched} unmatched)\n")
    print(f"{'off':>8}  {'resId':>5}  {'pkg':>4}  name")
    prev = None
    transitions = []
    for a in aligned:
        off = a["vm"]["off"] if a["vm"] else None
        flag = ""
        if prev is not None and a["pkg"] != prev["pkg"]:
            flag = f"   <<< PACKAGE {prev['pkg']} -> {a['pkg']}"
            transitions.append((prev, a))
        print(f"{str(off):>8}  {a['resId']:>5}  {str(a['pkg']):>4}  {a['name']}{flag}")
        prev = a

    print()
    if not transitions:
        print("No package transitions in this capture — play deeper into the scene to cross one.")
        return 0
    print(f"{len(transitions)} package transition(s). Significant ops executed across each "
          f"(the scope selector should be here):\n")
    for pa, pb in transitions:
        print(f"  === pkg {pa['pkg']} ({pa['name']}) -> pkg {pb['pkg']} ({pb['name']}) ===")
        if pa["vm"] and pb["vm"]:
            dump_span(trace, pa["vm"]["trace_i"], pb["vm"]["trace_i"], dis)
        else:
            print("       (unaligned — cannot pin the span)")
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
