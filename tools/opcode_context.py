#!/usr/bin/env python3
"""Evidence gatherer for classifying unnamed AGE opcodes.

For an opcode (or the top-N unnamed ones) prints: frequency + share + cumulative coverage,
argc, operand-type signature histogram, most common predecessor/successor opcodes, a few
real disassembly snippets, and Kelebek's inline comment (from the upstream cpp). Read-only.

Usage:
  py -3.11 -X utf8 tools/opcode_context.py --top 20        # ranked unnamed summary + coverage
  py -3.11 -X utf8 tools/opcode_context.py 0x1f4 0x71 0x7a # detailed evidence per opcode
"""
from __future__ import annotations
import re
import sys
import collections
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
import paths
import sys4load
import age_opcodes as ao

CORPUS = paths.DATA1
KELEBEK_CPP = ROOT / "vm-map" / "kelebek1-age-shared.cpp"


def is_unnamed(op: int) -> bool:
    lbl = ao.OPCODES.get(op, ("?", 0))[0]
    return bool(re.fullmatch(r"u[0-9A-Fa-f]{8}", lbl)) or lbl.lower() == f"{op:x}" or lbl.startswith("dev_ukn")


def label(op: int) -> str:
    return ao.OPCODES.get(op, (f"?{op:x}", 0))[0]


def kelebek_comments() -> dict[int, str]:
    out = {}
    if not KELEBEK_CPP.exists():
        return out
    for m in re.finditer(r"\{\s*(0x[0-9A-Fa-f]+)\s*,\s*\"[^\"]*\"\s*,\s*0x[0-9A-Fa-f]+\s*\}\s*,?\s*//\s*(.*)",
                         KELEBEK_CPP.read_text(encoding="utf-8")):
        out[int(m.group(1), 16)] = m.group(2).strip()
    return out


def load_corpus():
    scrs = []
    for p in sorted(CORPUS.glob("*.BIN")):
        try:
            scrs.append(sys4load.load(p))
        except Exception:
            pass
    return scrs


def fmt_instr(scr, ins) -> str:
    ops = " ".join(sys4load._fmt_operand(ins.opcode, i, t, v, scr.strings)
                   for i, (t, v) in enumerate(ins.args))
    return f"{label(ins.opcode)}{(' ' + ops) if ops else ''}"


def main() -> int:
    args = sys.argv[1:]
    scrs = load_corpus()
    freq = collections.Counter()
    total = 0
    for scr in scrs:
        for ins in scr.instructions:
            freq[ins.opcode] += 1
            total += 1
    named_vol = sum(c for op, c in freq.items() if not is_unnamed(op))
    comments = kelebek_comments()

    if args and args[0] == "--top":
        n = int(args[1]) if len(args) > 1 else 20
        unnamed = [(op, c) for op, c in freq.most_common() if is_unnamed(op)]
        print(f"corpus {len(scrs)} scripts, {total} instructions; "
              f"named coverage {100*named_vol/total:.2f}%; {len(unnamed)} distinct unnamed ops")
        print(f"{'#':>3} {'op':<7}{'argc':>5}{'count':>9}{'share':>8}{'cum-cov':>9}  kelebek-comment")
        cum = named_vol
        for i, (op, c) in enumerate(unnamed[:n]):
            cum += c
            argc = ao.OPCODES.get(op, ("", 0))[1]
            print(f"{i+1:>3} 0x{op:<5x}{argc:>5}{c:>9}{100*c/total:>7.2f}%{100*cum/total:>8.2f}%  {comments.get(op,'')[:48]}")
        return 0

    # detailed per-op evidence
    targets = [int(a, 16) for a in args] if args else [op for op, _ in
               [(op, c) for op, c in freq.most_common() if is_unnamed(op)][:8]]
    # precompute neighbour + signature stats
    pred = collections.defaultdict(collections.Counter)
    succ = collections.defaultdict(collections.Counter)
    sig = collections.defaultdict(collections.Counter)
    locs = collections.defaultdict(list)          # op -> [(scr, index)]
    for scr in scrs:
        ins = scr.instructions
        for i, x in enumerate(ins):
            if x.opcode in targets:
                if i > 0:
                    pred[x.opcode][label(ins[i-1].opcode)] += 1
                if i+1 < len(ins):
                    succ[x.opcode][label(ins[i+1].opcode)] += 1
                sig[x.opcode][tuple(ao.ARG_TYPES.get(t, hex(t)) for t, _ in x.args)] += 1
                if len(locs[x.opcode]) < 4:
                    locs[x.opcode].append((scr, i))

    for op in targets:
        c = freq.get(op, 0)
        argc = ao.OPCODES.get(op, ("", 0))[1]
        print(f"\n{'='*72}\nopcode 0x{op:x}  label={label(op)}  argc={argc}  "
              f"count={c} ({100*c/total:.2f}% of instrs)")
        if comments.get(op):
            print(f"  kelebek-comment: {comments[op]}")
        print(f"  operand-type signatures: " +
              ", ".join(f"{'/'.join(s) if s else 'none'}×{n}" for s, n in sig[op].most_common(4)))
        print(f"  top predecessors: " + ", ".join(f"{k}×{v}" for k, v in pred[op].most_common(5)))
        print(f"  top successors:   " + ", ".join(f"{k}×{v}" for k, v in succ[op].most_common(5)))
        print(f"  snippets:")
        for scr, i in locs[op]:
            lo, hi = max(0, i-2), min(len(scr.instructions), i+3)
            for j in range(lo, hi):
                mark = ">>" if j == i else "  "
                print(f"    {mark} [{scr.path.name}] {fmt_instr(scr, scr.instructions[j])}")
            print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
