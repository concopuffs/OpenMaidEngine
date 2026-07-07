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


FLIP = {"==": "==", "!=": "!=", "<": ">", "<=": ">=", ">": "<", ">=": "<="}
NEG  = {"==": "!=", "!=": "==", "<": ">=", ">=": "<", "<=": ">", ">": "<="}


def _cmp_expr(op_sym, a, b):
    """Build a Cmp guard from a comparison's operands (a <op> b). Global vs immediate only;
    anything else -> opaque."""
    if a[0] in GLOBAL_ATYPES and b[0] == IMM:
        return {"global": a[1], "op": op_sym, "value": b[1]}
    if a[0] == IMM and b[0] in GLOBAL_ATYPES:
        return {"global": b[1], "op": FLIP[op_sym], "value": a[1]}
    return {"opaque": f"cmp {op_sym} {a} {b}"}


def negate(g):
    if "opaque" in g:      return {"opaque": "!(" + g["opaque"] + ")"}
    if "and" in g:         return {"or": [negate(x) for x in g["and"]]}
    if "or" in g:          return {"and": [negate(x) for x in g["or"]]}
    return {"global": g["global"], "op": NEG[g["op"]], "value": g["value"]}


def _resolve(localmap, operand):
    """Resolve a jcc/logic operand (a local holding a condition) to its guard expression."""
    return localmap.get(operand[1], {"opaque": f"local {operand}"})


def chapter_of(guards):
    for g in guards:
        if g.get("global") == CHAPTER_GLOBAL and g.get("op") == "==":
            return g["value"]
    return None


def decode(scr):
    code = scr.instructions
    by_off = {ins.offset: i for i, ins in enumerate(code)}
    decisions = []
    # explicit-stack DFS: each item is (idx, localmap, guards). No back-edges -> terminates.
    stack = [(0, {}, [])]
    while stack:
        idx, localmap, guards = stack.pop()
        localmap = dict(localmap)
        while 0 <= idx < len(code):
            ins = code[idx]
            op, a = ins.opcode, ins.args
            if ins.offset == END_TARGET:
                break
            if op == MOV and a and tuple(a[0]) == (3, DECISION_GLOBAL) and a[1][0] == IMM:
                decisions.append({"site_offset": ins.offset, "chapter": chapter_of(guards),
                                  "decision": a[1][1], "guards": list(guards)})
                idx += 1
                continue
            if op in CMP_OPS and len(a) >= 3 and a[0][0] == 9:
                localmap[a[0][1]] = _cmp_expr(CMP_OPS[op], a[1], a[2])
                idx += 1
                continue
            if op in LOGIC_OPS and len(a) >= 3 and a[0][0] == 9:
                kind = LOGIC_OPS[op]
                localmap[a[0][1]] = {kind: [_resolve(localmap, a[1]), _resolve(localmap, a[2])]}
                idx += 1
                continue
            if op == JCC and len(a) >= 3:
                expr = _resolve(localmap, a[0])
                a_tgt, b_tgt = a[1][1], a[2][1]
                true_idx = idx + 1 if a_tgt == FALLTHROUGH else by_off.get(a_tgt)
                false_idx = idx + 1 if b_tgt == FALLTHROUGH else by_off.get(b_tgt)
                # explore false branch later; continue true branch inline
                if false_idx is not None:
                    stack.append((false_idx, dict(localmap), guards + [negate(expr)]))
                if true_idx is None:
                    break
                idx, guards = true_idx, guards + [expr]
                continue
            if op == JMP and a:
                tgt = a[0][1]
                if tgt == END_TARGET or tgt not in by_off:
                    break
                idx = by_off[tgt]
                continue
            idx += 1
    return decisions


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
