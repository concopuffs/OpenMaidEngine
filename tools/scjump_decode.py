#!/usr/bin/env python3
"""Static decoder for SCJUMP.BIN's progression decision logic. Guarded DFS over its acyclic CFG
extracts, per decision site, the (chapter_mode, path-condition guards) -> decision value rule.
Decision->scene resolution is native (u00428010) and out of scope. See
docs/superpowers/specs/2026-07-07-scjump-decision-decode-design.md.
  (no flag)   -> build/scjump-decisions.json + build/scjump-decisions.md
  --verify    synthesize a witness per decision, run SCJUMP in vm0, assert emitted 0x62ccf matches"""
from __future__ import annotations
import os, sys, json, argparse, collections
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import sys4load
from age_opcodes import is_label_argument

MOV, JMP, JCC = 0x55, 0x8c, 0xa0
CMP_OPS = {0x5a: "==", 0x5b: "!=", 0x5c: "<", 0x5d: "<=", 0x5e: ">", 0x5f: ">="}
LOGIC_OPS = {0x56: "and", 0x57: "or"}
GLOBAL_ATYPES = {3, 4, 5, 6, 8}
IMM = 0
FALLTHROUGH = 0xffffffff
CHAPTER_GLOBAL = 0x3234
DECISION_GLOBAL = 0x62ccf
VALID_GLOBAL = 0x0
END_TARGET = 0x2f78d


def load_scjump():
    return sys4load.load(paths.scripts()["SCJUMP.BIN"])


def _code_targets(ins):
    """Yield the code-target offsets of an instruction (excludes 0xffffffff fallthrough)."""
    for i, (t, v) in enumerate(ins.args):
        if is_label_argument(ins.opcode, i, v) and v != FALLTHROUGH:
            yield v


def forward_edges_only(scr) -> bool:
    by_off = {ins.offset: ins.offset for ins in scr.instructions}
    for ins in scr.instructions:
        for tgt in _code_targets(ins):
            if tgt in by_off and tgt <= ins.offset:
                return False        # a back-edge or self-loop
    return True


def chapter_dispatch(scr) -> dict:
    """Scan the top dispatch: eq(dest, 0x3234, N) immediately followed by jcc(dest, block, ...)."""
    out = {}
    code = scr.instructions
    for i, ins in enumerate(code):
        if ins.opcode == 0x5a and len(ins.args) >= 3 \
           and tuple(ins.args[1]) == (3, CHAPTER_GLOBAL) and ins.args[2][0] == IMM:
            n = ins.args[2][1]
            nxt = code[i + 1] if i + 1 < len(code) else None
            if nxt and nxt.opcode == JCC and nxt.args[1][1] != FALLTHROUGH:
                out[n] = nxt.args[1][1]
    return out


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--verify", action="store_true")   # implemented in Task 4
    args = ap.parse_args(argv)
    scr = load_scjump()
    if args.verify:
        return verify(scr)                             # Task 4
    return build(scr)                                  # Task 3


if __name__ == "__main__":
    sys.exit(main())
