#!/usr/bin/env python3
"""Definitive raw-walker test for the canonical opcode registry and data boundary."""
import os, sys, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import sys4load
from age_opcodes import OPCODES

TABLE={op: entry[1] for op, entry in OPCODES.items()}

files=paths.scripts()

clean=dirty=0; parsefail=0
still_unknown=collections.Counter(); examples={}
instr_total=0
for name,p in sorted(files.items()):
    try: scr=sys4load.load(p)
    except Exception: parsefail+=1; continue
    dw=scr.dwords; cl=scr.code_len
    end=cl                     # data_array_end, starts at F8, shrinks to first string/array off
    i=0; ok=True; reason=""
    n_instr=0
    while i<end:
        op=dw[i]
        if op not in TABLE:
            ok=False; reason=f"unknown 0x{op:x}@{i}"
            if op<0x1000: still_unknown[op]+=1; examples.setdefault(op,name)
            break
        argc=TABLE[op]
        base=i+1
        if base+2*argc>end:
            ok=False; reason=f"overrun 0x{op:x}@{i}"; break
        for a in range(argc):
            atype=dw[base+2*a]; aval=dw[base+2*a+1]
            if atype==2 and 0<=aval<end:      # inline string -> shrink code end
                end=min(end,aval)
            if op==0x64 and a==1 and 0<=aval<end:  # copy-local-array footer ref
                end=min(end,aval)
        i=base+2*argc; n_instr+=1
    if ok and i==end:
        clean+=1; instr_total+=n_instr
    else:
        dirty+=1
        if len(examples)<20 and reason: pass

print(f"scripts: {len(files)}  parsefail(container): {parsefail}")
print(f"CLEAN decode (canonical table + string-pool boundary): {clean}")
print(f"DIRTY: {dirty}")
print(f"total instructions decoded in clean files: {instr_total}")
print(f"\nremaining small unknown opcodes (genuine gaps):")
for op,c in still_unknown.most_common():
    print(f"  0x{op:x}  files={c}  e.g. {examples.get(op)}  known={op in TABLE}")
if not still_unknown:
    print("  (none)")
