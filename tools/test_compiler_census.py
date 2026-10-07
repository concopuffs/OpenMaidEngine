#!/usr/bin/env python3
"""Pure tests for tools/compiler_census.py on synthetic instruction streams (no game content).
Run: py -3.11 -X utf8 tools/test_compiler_census.py"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import compiler_census as C
from sys4load import Instruction

FAILS = []
NONE = C.NONE


def check(cond, msg):
    print(("  ok  " if cond else " FAIL ") + msg)
    if not cond:
        FAILS.append(msg)


def program(*ops):
    """Lay out (opcode, args) pairs at their real dword offsets; labels are written as ('L', name)."""
    offsets, at = {}, 0
    for item in ops:
        if item[0] == "label":
            offsets[item[1]] = at
            continue
        at += 1 + 2 * len(item[1])
    out, at = [], 0
    for item in ops:
        if item[0] == "label":
            continue
        op, args = item
        resolved = [(0, offsets[v[1]]) if isinstance(v, tuple) and v[0] == "L" else v for v in args]
        out.append(Instruction(at, op, None, resolved))
        at += 1 + 2 * len(resolved)
    return out


def shapes(ins):
    index = {i.offset: k for k, i in enumerate(ins)}
    return [C.jcc_shape(ins, k, index) for k, i in enumerate(ins) if i.opcode == C.OP_JCC]


COND = (9, 3)
NOP = (0x55, [(9, 1), (0, 0)])


def test_shapes():
    plain_if = program((C.OP_JCC, [COND, (0, NONE), ("L", "end")]), NOP, ("label", "end"), NOP)
    check(shapes(plain_if) == ["if"], "false-skip forward with no trailing jmp is an if")
    if_else = program((C.OP_JCC, [COND, (0, NONE), ("L", "else")]), NOP, (C.OP_JMP, [("L", "end")]),
                      ("label", "else"), NOP, ("label", "end"), NOP)
    check(shapes(if_else) == ["if-else"], "then-block ending in a forward jmp is if-else")
    loop = program(("label", "head"), (0x5C, [(9, 3), (9, 0), (0, 10)]),
                   (C.OP_JCC, [COND, (0, NONE), ("L", "end")]), NOP, (C.OP_JMP, [("L", "head")]),
                   ("label", "end"), NOP)
    check(shapes(loop) == ["while"], "back edge to the condition is a while loop")
    for_loop = program((0x55, [(9, 0), (0, 0)]), (C.OP_JMP, [("L", "cond")]),
                       ("label", "inc"), (0x50, [(9, 0), (9, 0), (0, 1)]),
                       ("label", "cond"), (0x5C, [(9, 3), (9, 0), (0, 10)]),
                       (C.OP_JCC, [COND, (0, NONE), ("L", "end")]), NOP, (C.OP_JMP, [("L", "inc")]),
                       ("label", "end"), NOP)
    check(shapes(for_loop) == ["for"], "init; jmp cond; inc; cond is a for loop")
    goto = program((C.OP_JCC, [COND, ("L", "far"), (0, NONE)]), NOP, ("label", "far"), NOP)
    check(shapes(goto) == ["conditional-goto"], "jump-if-true forward is a conditional goto")
    both = program((C.OP_JCC, [COND, ("L", "a"), ("L", "b")]), ("label", "a"), NOP, ("label", "b"), NOP)
    check(shapes(both) == ["conditional-goto-else"], "two-target jcc is goto-else")
    braced = program((C.OP_JCC, [COND, (0, NONE), ("L", "skip")]), (C.OP_JMP, [("L", "far")]),
                     ("label", "skip"), NOP, ("label", "far"), NOP)
    index = {i.offset: k for k, i in enumerate(braced)}
    check(C.if_goto_bodies(braced, index) == 1, "if { goto } is counted as the braced lowering")


def test_blocks():
    good = program((C.OP_BLOCK_BEGIN, []), (C.OP_JCC, [COND, (0, NONE), ("L", "in")]), NOP,
                   ("label", "in"), (C.OP_BLOCK_END, []), (0x7B, [("L", "out"), ("L", "out")]),
                   ("label", "out"), NOP)
    b = C.block_structure(good)
    check(b["unbalanced"] == 0 and b["max_depth"] == 1, "balanced single block")
    check(b["crossing_transfer_edges"] == 0 and b["transfer_edges"] == 1, "inner jcc stays inside")
    crossing = program((C.OP_BLOCK_BEGIN, []), (C.OP_JMP, [("L", "out")]), (C.OP_BLOCK_END, []),
                       ("label", "out"), NOP)
    check(C.block_structure(crossing)["crossing_transfer_edges"] == 1, "a jmp out of a block is a crossing")
    check(C.block_structure(program((C.OP_BLOCK_END, [])))["unbalanced"] == 1, "a stray end is unbalanced")


def test_terminators_and_helpers():
    ins = program((C.OP_CALL, [("L", "sub")]), (C.OP_EXIT, []),
                  ("label", "sub"), (C.OP_JCC, [COND, (0, NONE), ("L", "r")]), NOP, ("label", "r"), (C.OP_RET, []))
    index = {i.offset: k for k, i in enumerate(ins)}
    check(C.terminators(ins, index, ins[2].offset) == frozenset({"ret"}), "subroutine body reaches ret")

    class Script:
        strings = {}
    body = [("label", "sub"), (C.OP_JCC, [COND, (0, NONE), ("L", "r")]), NOP, ("label", "r"), (C.OP_RET, [])]
    a = program((C.OP_CALL, [("L", "sub")]), (C.OP_EXIT, []), *body)
    b = program(NOP, NOP, (C.OP_CALL, [("L", "sub")]), (C.OP_EXIT, []), *body)
    ka = C.subroutine_key(Script, a, {i.offset: k for k, i in enumerate(a)}, a[2].offset)
    kb = C.subroutine_key(Script, b, {i.offset: k for k, i in enumerate(b)}, b[4].offset)
    check(ka == kb, "helper fingerprints are position independent")


def main():
    test_shapes()
    test_blocks()
    test_terminators_and_helpers()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
