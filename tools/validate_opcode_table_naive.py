#!/usr/bin/env python3
"""Validate Kelebek1's AGE opcode table against Himegari SYS4 scripts.

Model (from Kelebek1 disassembler.cpp + age-shared.cpp):
  code stream = sequence of instructions.
  each instruction = <opcode:u32> then argument_count * <arg>, where each arg = <type:u32><value:u32>.
  => instruction length in dwords = 1 + 2*argument_count  (uniform; type-2/0x64 args seek elsewhere, don't consume inline)
  arg type 2 = inline string (value = dword offset into body). types: 0 imm,1 float,3 g-int,9 l-int, etc.
A clean decode consumes exactly code_len dwords with no unknown opcode and no arg overrun.
"""
import os, re, sys, collections
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import sys4load

CPP = paths.KELEBEK_CPP.read_text(encoding="utf-8")
# parse {0x1F4, "label", 0x0},
TABLE = {}
LABEL = {}
for m in re.finditer(r'\{\s*(0x[0-9A-Fa-f]+)\s*,\s*"([^"]*)"\s*,\s*(0x[0-9A-Fa-f]+)\s*\}', CPP):
    op = int(m.group(1), 16); lbl = m.group(2); argc = int(m.group(3), 16)
    TABLE[op] = argc; LABEL[op] = lbl
print(f"parsed {len(TABLE)} opcode defs from Kelebek1 table (max op 0x{max(TABLE):x})")

files = paths.scripts()

clean = dirty = parsefail = 0
unknown_ops = collections.Counter()
fail_examples = []
str_ok = str_bad = 0
opcode_use = collections.Counter()

for name, p in sorted(files.items()):
    try:
        scr = sys4load.load(p)
    except Exception:
        parsefail += 1; continue
    dw = scr.dwords; cl = scr.code_len
    i = 0; ok = True; reason = ""
    while i < cl:
        op = dw[i]
        if op not in TABLE:
            unknown_ops[op] += 1; ok = False; reason = f"unknown op 0x{op:x} @{i}"; break
        opcode_use[op] += 1
        argc = TABLE[op]
        # check each arg's type; resolve strings
        base = i + 1
        if base + 2*argc > cl:
            ok = False; reason = f"arg overrun op 0x{op:x} @{i} needs {argc} args"; break
        for a in range(argc):
            atype = dw[base + 2*a]; aval = dw[base + 2*a + 1]
            if atype == 2:  # inline string
                if 0 <= aval < scr.nbody:
                    txt, nd = sys4load._decode_string(dw, aval)
                    if txt is None: str_bad += 1
                    else: str_ok += 1
                else:
                    str_bad += 1
        i = base + 2*argc
    if ok and i == cl:
        clean += 1
    else:
        dirty += 1
        if len(fail_examples) < 15:
            fail_examples.append(f"  {name}: {reason} (stopped @{i}/{cl})")

print(f"\n== decode result over {len(files)} scripts ==")
print(f"  clean (fully consumed, all opcodes known): {clean}")
print(f"  dirty: {dirty}")
print(f"  parse-fail (container): {parsefail}")
print(f"  string args resolved ok / bad: {str_ok} / {str_bad}")
print(f"\ntop unknown opcodes (op: files affected):")
for op, c in unknown_ops.most_common(25):
    print(f"  0x{op:x}: {c}")
print(f"\nsample dirty files:")
print("\n".join(fail_examples))
print(f"\ntop 25 opcodes actually used in Himegari (op label count):")
for op, c in opcode_use.most_common(25):
    print(f"  0x{op:<4x} {LABEL.get(op,'?'):22} {c}")
