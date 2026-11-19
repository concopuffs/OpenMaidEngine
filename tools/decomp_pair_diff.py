#!/usr/bin/env python3
"""Compare the decompiled bodies of function pairs from two engine builds, with each build's own numbers masked.

The revision diff (tools/revision_diff.py) says which opcodes and helpers differ; this tool shows how. Decompile the
same functions, in the same order, from both Ghidra images with tools/ghidra/DecompileToFile.java, then:

    py -3.11 -X utf8 tools/decomp_pair_diff.py A.c B.c            one summary line per pair
    py -3.11 -X utf8 tools/decomp_pair_diff.py A.c B.c 3 7        the masked diff of pairs 3 and 7

A pair is the Nth function of each file. Before comparing, a line loses what differs between any two builds of the
same code: addresses in Ghidra's default names (FUN_, DAT_, LAB_, string labels), numbers of four hex digits or
more (context offsets and addresses), the decompiler's variable numbering, plate comments and plain local
declarations. What is left differs where the code does, and also where the decompiler typed a local differently or
one image names a function the other does not: read a diff before calling a pair changed.
"""
from __future__ import annotations

import difflib
import re
import sys
from pathlib import Path

_DECLARATION = re.compile(
    r"(undefined\d*|int|uint|float|char|byte|bool|short|ushort|code|float10|double|longlong|ulonglong|undefined)"
    r"\s*\**\s*\w+(\s*\[\d+\])*;")
_DEFAULT_NAME = re.compile(r"\b(FUN|DAT|LAB|PTR_FUN|PTR_DAT|PTR_LAB|_DAT|s_[A-Za-z0-9_]*?)_00[0-9a-f]{6}\b")
_LONG_NUMBER = re.compile(r"\b0x[0-9a-f]{4,}\b")
_VARIABLE = re.compile(
    r"\b(local|param|uVar|iVar|puVar|piVar|pcVar|cVar|bVar|fVar|sVar|ppuVar|pbVar|uStack|iStack|puStack|auStack|"
    r"ppuStack|pppbStack|piStack|in_stack|unaff|extraout)_?[0-9a-fA-F_]*\b")


def mask(line: str) -> str:
    """One decompiled line with the build's own numbers replaced."""
    line = _DEFAULT_NAME.sub(r"\1_X", line)
    line = _LONG_NUMBER.sub("0xN", line)
    return _VARIABLE.sub(r"\1", line)


def functions(text: str) -> list[tuple[str, list[str]]]:
    """DecompileToFile's output as (heading, masked lines) per function, in file order."""
    result = []
    for part in re.split(r"^// ===== ", text, flags=re.M)[1:]:
        heading, _, body = part.partition("\n")
        body = re.sub(r"/\*.*?\*/", "", body, flags=re.S)
        lines = []
        for line in body.splitlines():
            stripped = line.strip()
            if stripped and not _DECLARATION.fullmatch(stripped):
                lines.append(mask(stripped))
        result.append((heading, lines))
    return result


def compare(lines_a: list[str], lines_b: list[str]) -> tuple[int, list[str]]:
    """The number of changed lines and the unified diff, one line of context."""
    diff = [line for line in difflib.unified_diff(lines_a, lines_b, lineterm="", n=1)
            if not line.startswith(("---", "+++"))]
    return sum(1 for line in diff if line.startswith(("+", "-"))), diff


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 2
    a = functions(Path(argv[0]).read_text(encoding="utf-8"))
    b = functions(Path(argv[1]).read_text(encoding="utf-8"))
    if len(a) != len(b):
        print(f"warning: {argv[0]} has {len(a)} functions and {argv[1]} has {len(b)}; pairing the first "
              f"{min(len(a), len(b))}", file=sys.stderr)
    wanted = {int(arg, 0) for arg in argv[2:]}
    for index, ((head_a, lines_a), (head_b, lines_b)) in enumerate(zip(a, b)):
        if wanted and index not in wanted:
            continue
        changed, diff = compare(lines_a, lines_b)
        print(f"#### [{index}] {head_a}  |  {head_b}   ({len(lines_a)} / {len(lines_b)} lines, {changed} changed)")
        if wanted:
            for line in diff:
                print("   " + line[:170])
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
