#!/usr/bin/env python3
"""Differential offset-path oracle (docs/engine-re.md): diff the real engine's executed script-offset
path against our C# VM's, on the deterministic opening, and report the FIRST divergence — the exact
branch/opcode we modeled wrong.

Both run the same bytecode, so the opcode at each script offset is static (from disassembly); we diff the
*sequence of executed offsets* (control flow), not opcodes or effects. A path divergence is a jcc that
went the other way, an opcode length we got wrong, or a native context (coroutine) the VM doesn't model.

Inputs (both under build/, disposable):
  build/engine-optrace.jsonl  — {"codebase":int,"offset":int} per executed op, in order (trace_engine_ops.py)
  build/vm-optrace.json       — {"scene":"SC0000","offsets":[int,...]}  (Age.Cli trace --trace-json)

    py -3.11 -X utf8 tools/diff_optrace.py SC0000 [--engine PATH] [--vm PATH]

The pure core (first_divergence / pick_scene_codebase) is unit-tested in tools/test_diff_optrace.py.
"""
import json
import re
import sys
from pathlib import Path

import paths  # noqa: E402  (sibling import; tools dir is on sys.path when run directly)

ENGINE_TRACE = paths.BUILD / "engine-optrace.jsonl"
VM_TRACE = paths.BUILD / "vm-optrace.json"


# ---- pure core (unit-tested) -------------------------------------------------

def first_divergence(a: list, b: list) -> dict:
    """First index where offset sequences `a` (engine) and `b` (VM) differ.

    Returns {"agreed": n, "index": i, "a": a[i]|None, "b": b[i]|None}, or
    {"agreed": len, "index": None} when one is a prefix of the other / they are equal.
    When they agree over the whole common prefix but differ in length, the shorter ended early: the
    divergence index is that common length, with the missing side reported as None."""
    n = min(len(a), len(b))
    for i in range(n):
        if a[i] != b[i]:
            return {"agreed": i, "index": i, "a": a[i], "b": b[i]}
    if len(a) == len(b):
        return {"agreed": n, "index": None, "a": None, "b": None}
    return {"agreed": n, "index": n,
            "a": a[n] if n < len(a) else None,
            "b": b[n] if n < len(b) else None}


def operand_filter(offsets: list, argc_by_off: dict) -> list:
    """Keep only offsets whose instruction has >=1 operand — the subsequence an OPERAND-mode engine
    capture (vm_operand_fetch hook, deduped per-pc) can see. Zero-operand ops (stmt-begin/end and other
    markers) never trigger an operand fetch, so they are absent from the engine trace; filtering the VM
    trace the same way makes the two directly comparable. (Markers don't branch, so control flow is
    preserved.) Offsets not in the map (shouldn't happen for a valid scene) are dropped."""
    return [o for o in offsets if argc_by_off.get(o, 0) >= 1]


def pick_scene_codebase(entries: list, vm_offsets: list):
    """The codebase whose in-order offset sequence shares the longest common prefix with `vm_offsets`
    — identifies which loaded-script instance in the engine trace is the scene we ran in the VM. Returns
    the codebase id, or None if no codebase shares even one leading offset (nothing matches)."""
    seqs, order = {}, []
    for e in entries:
        cb = e["codebase"]
        if cb not in seqs:
            seqs[cb] = []
            order.append(cb)
        seqs[cb].append(e["offset"])

    def lcp(seq):
        n = 0
        for x, y in zip(seq, vm_offsets):
            if x != y:
                break
            n += 1
        return n

    best, best_len = None, 0
    for cb in order:
        length = lcp(seqs[cb])
        if length > best_len:
            best, best_len = cb, length
    return best


# ---- CLI: load traces, decode opcode context, report -------------------------

def _load_engine(path: Path) -> list:
    entries = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                entries.append(json.loads(line))
    return entries


def _load_scene_disasm(scene: str):
    """offset -> (opcode:int, rendered_line:str) for the scene, straight from sys4load (authoritative)."""
    import sys4load
    key = scene.upper()
    if not key.endswith(".BIN"):
        key += ".BIN"
    scr = sys4load.load(paths.scripts()[key])
    sys4load.decode_code(scr)
    op_by_off = {ins.offset: ins.opcode for ins in scr.instructions}
    argc_by_off = {ins.offset: len(ins.args) for ins in scr.instructions}
    line_by_off = {}
    for line in sys4load.render_listing(scr).splitlines():
        m = re.match(r"\s*0x([0-9a-fA-F]+):\s*(.*)", line)
        if m:
            line_by_off[int(m.group(1), 16)] = m.group(2).rstrip()
    return op_by_off, argc_by_off, line_by_off


def _fmt(off, op_by_off, line_by_off) -> str:
    if off is None:
        return "(none — trace ended)"
    op = op_by_off.get(off)
    line = line_by_off.get(off, "?")
    return f"0x{off:05x}: {line}" + (f"   [op 0x{op:x}]" if op is not None else "   [op ?]")


def report(scene, entries, vm_offsets, operand_mode=True):
    op_by_off, argc_by_off, line_by_off = _load_scene_disasm(scene)
    # An operand-hook engine capture only sees ops with >=1 operand; filter the VM trace to match so the
    # two are the same subsequence. --full turns this off (for a tick-mode engine capture, which sees all).
    if operand_mode:
        vm_offsets = operand_filter(vm_offsets, argc_by_off)

    cb = pick_scene_codebase(entries, vm_offsets)
    if cb is None:
        print(f"[!] could not identify {scene}'s codebase in the engine trace "
              f"({len(entries)} entries, {len({e['codebase'] for e in entries})} codebases) — "
              f"no shared leading offset with the VM trace"
              f"{' (operand-filtered)' if operand_mode else ''}.")
        return 2
    engine_seq = [e["offset"] for e in entries if e["codebase"] == cb]

    d = first_divergence(engine_seq, vm_offsets)
    print(f"=== differential offset-path oracle: {scene} ===")
    print(f"engine codebase 0x{cb:x}: {len(engine_seq)} executed offsets "
          f"(of {len(entries)} total across {len({e['codebase'] for e in entries})} codebases)")
    print(f"VM: {len(vm_offsets)} executed offsets")

    if d["index"] is None:
        print(f"\nNO DIVERGENCE over {d['agreed']} steps — the VM path matches the engine exactly. ✓")
        return 0

    i = d["index"]
    print(f"\nAGREED {d['agreed']} steps, then DIVERGE at index {i}:")
    if i > 0:
        shared = engine_seq[i - 1]
        print(f"  last agreed op   {_fmt(shared, op_by_off, line_by_off)}")
        print(f"                   ^ this instruction's successor differs — the mis-modeled branch/op")
    print(f"  engine went ->   {_fmt(d['a'], op_by_off, line_by_off)}")
    print(f"  VM     went ->   {_fmt(d['b'], op_by_off, line_by_off)}")

    def ctx(seq, label):
        lo, hi = max(0, i - 3), min(len(seq), i + 4)
        print(f"\n  {label} context [{lo}..{hi}):")
        for j in range(lo, hi):
            mark = " <-- diverge" if j == i else ("  (last agreed)" if j == i - 1 else "")
            print(f"    [{j}] {_fmt(seq[j], op_by_off, line_by_off)}{mark}")

    ctx(engine_seq, "engine")
    ctx(vm_offsets, "VM")
    return 0


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    scene = next((a for a in argv if not a.startswith("-")), "SC0000")

    def opt(flag, default):
        return argv[argv.index(flag) + 1] if flag in argv and argv.index(flag) + 1 < len(argv) else default

    engine_path = Path(opt("--engine", ENGINE_TRACE))
    vm_path = Path(opt("--vm", VM_TRACE))

    if not engine_path.exists():
        print(f"[!] engine trace not found: {engine_path}\n    Capture it first: "
              f"py -3.11 -u -X utf8 tools/frida/trace_engine_ops.py --hook tick")
        return 1
    if not vm_path.exists():
        print(f"[!] VM trace not found: {vm_path}\n    Produce it first: "
              f"dotnet run --project engine/Age.Cli -- trace {scene}.BIN --boot --trace-json {vm_path}")
        return 1

    entries = _load_engine(engine_path)
    vm_offsets = json.loads(vm_path.read_text(encoding="utf-8"))["offsets"]
    return report(scene, entries, vm_offsets, operand_mode="--full" not in argv)


if __name__ == "__main__":
    paths.add_self_to_syspath()
    sys.exit(main())
