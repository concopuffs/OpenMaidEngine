#!/usr/bin/env python3
"""Shared static-write walkers for the Kamidori (SYS4433) *INIT specializations.

The domain modules (`init_kamidori_<domain>.py`) decode fixed absolute layouts: a write's
destination minus an array base gives the record id, so the same extractor reads a base table
and any `$n$` append layer without rebasing. Table layouts are documented in
docs/kamidori-data-tables.md.
"""
from __future__ import annotations

from functools import cache

import extract_init as E
import paths
import sys4load

T_IMM = E.T_IMM
T_GLOBAL_INT = E.T_GLOBAL_INT
T_GLOBAL_STRING = E.T_GLOBAL_STRING
T_LOCAL_INT = E.T_LOCAL_INT
T_LOCAL_PTR = E.T_LOCAL_PTR
SET_STRING = E.SET_STRING
COPY_LOCAL_ARRAY = E.COPY_LOCAL_ARRAY
NONE_TARGET = 0xFFFFFFFF
STUB_SIZE = 64  # an append catalog's empty `$n$TABLE.BIN` placeholder


def label(ins) -> str:
    return sys4load.display_label(ins.opcode)


def literal(scr, arg):
    """Decoded inline string for a set-string source operand."""
    return scr.strings.get(arg[1], (None,))[0]


def imm(arg):
    return arg[1] if arg[0] == T_IMM else None


def static_global_write(ins):
    return E._static_global_write(ins)


def read_footer_array(scr, off):
    return E.read_footer_array(scr, off)


def string_writes(scr):
    """Yield (destination, text) for every set-string into the global string bank."""
    for ins in scr.instructions:
        if ins.opcode == SET_STRING and ins.args and ins.args[0][0] == T_GLOBAL_STRING:
            text = literal(scr, ins.args[1]) if len(ins.args) > 1 else None
            yield ins.args[0][1], text


def int_writes(scr):
    """Yield (destination, value) for every statically evaluable global-int write."""
    for ins in scr.instructions:
        write = static_global_write(ins)
        if write is not None:
            yield write


def inline_arrays(scr):
    """Yield (destination, values) for `copy-inline-int-array (global-int DST) OFF`."""
    for ins in scr.instructions:
        if label(ins) == "copy-inline-int-array" and ins.args and ins.args[0][0] == T_GLOBAL_INT:
            yield ins.args[0][1], read_footer_array(scr, ins.args[1][1])


def final_writes(scr):
    """(ints, strings): one layer's final static global writes (last write wins)."""
    ints, strs = {}, {}
    for ins in scr.instructions:
        if ins.opcode == SET_STRING and ins.args and ins.args[0][0] == T_GLOBAL_STRING:
            strs[ins.args[0][1]] = literal(scr, ins.args[1]) if len(ins.args) > 1 else None
        elif (write := static_global_write(ins)) is not None:
            ints[write[0]] = write[1]
    return ints, strs


def evaluated_writes(scr):
    """Yield (kind, address, value, guard, offset) for every statically resolvable write.

    Beyond `final_writes`, this tracks local constant arithmetic (mov/add/sub/mul), resolves
    `lookup-array-2d (local-ptr p) (global base) row stride col` + `mov (local-ptr p) v`, and
    expands length-prefixed inline arrays. `guard` is the latest `cmp local (global g) imm`.
    """
    locals_: dict[int, int] = {}
    ptrs: dict[int, int] = {}
    guard = None

    def ev(arg):
        kind, value = arg
        if kind == T_IMM:
            return value
        if kind == T_LOCAL_INT:
            return locals_.get(value)
        return None

    for ins in scr.instructions:
        op = label(ins)
        a = ins.args
        if (op in ("eq", "gre", "ne", "gr", "lt", "lte") and len(a) == 3
                and a[1][0] == T_GLOBAL_INT and a[2][0] == T_IMM):
            guard = {"op": op, "global": a[1][1], "value": a[2][1]}
            continue
        if op == "set-string" and a and a[0][0] == T_GLOBAL_STRING:
            text = literal(scr, a[1]) if len(a) > 1 else None
            yield "string", a[0][1], text, guard, ins.offset
            continue
        if op == "copy-inline-int-array" and a and a[0][0] == T_GLOBAL_INT:
            for i, v in enumerate(read_footer_array(scr, a[1][1]) or []):
                yield "int", a[0][1] + i, v - (1 << 32) if v >= 1 << 31 else v, guard, ins.offset
            continue
        if op == "lookup-array-2d" and a[0][0] == T_LOCAL_PTR and a[1][0] == T_GLOBAL_INT:
            row, stride, col = ev(a[2]), ev(a[3]), ev(a[4])
            if None not in (row, stride, col):
                ptrs[a[0][1]] = a[1][1] + row * stride + col
            continue
        if op in ("mov", "add", "sub", "mul") and a:
            dst_t, dst = a[0]
            if op == "mov":
                val = ev(a[1])
            else:
                x, y = ev(a[1]), ev(a[2])
                val = (None if None in (x, y)
                       else x + y if op == "add" else x - y if op == "sub" else x * y)
            if dst_t == T_LOCAL_INT:
                if val is None:
                    locals_.pop(dst, None)
                else:
                    locals_[dst] = val
            elif dst_t == T_GLOBAL_INT and val is not None:
                yield "int", dst, val, guard, ins.offset
            elif dst_t == T_LOCAL_PTR and dst in ptrs and val is not None:
                yield "int", ptrs[dst], val, guard, ins.offset


def selector_guards(scr, selector):
    """[(id, first_body_index, end_index)] for `eq local, (global selector), K; jcc; ...` chains."""
    ins = scr.instructions
    out = []
    for i, x in enumerate(ins):
        if (label(x) == "eq" and len(x.args) >= 3
                and x.args[1] == (T_GLOBAL_INT, selector) and x.args[2][0] == T_IMM):
            out.append([x.args[2][1], i + 2, None])
    for k, guard in enumerate(out):
        guard[2] = (out[k + 1][1] - 2) if k + 1 < len(out) else len(ins)
    return [tuple(guard) for guard in out]


def layer_scripts(name: str) -> list:
    """Base script plus every non-stub extracted `$n$` layer, in ascending selector order."""
    scripts = [sys4load.load(E.resolve(name))]
    for directory in sorted(paths.EXTRACTED.glob("APPEND[0-9][0-9]")):
        selector = int(directory.name[6:])
        try:
            path = E.resolve_append(name, selector)
        except SystemExit:
            continue
        if path.stat().st_size > STUB_SIZE:
            scripts.append(sys4load.load(path))
    return scripts


@cache
def authored_strings(name: str, base: int, span: int) -> dict[int, str]:
    """{index: text} for one string array written across a table's base + append layers."""
    out = {}
    for scr in layer_scripts(name):
        for dst, text in string_writes(scr):
            if base <= dst < base + span:
                out[dst - base] = text
    return out
