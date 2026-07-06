#!/usr/bin/env python3
"""Corpus-wide candidate-opcode histogram.

T3 entries point at [0x8F, 0, line#] records embedded in CODE. The dword right
after each 3-dword T3 record is the lead of the next statement -> candidate
opcode. Histogram those leads over all root-override + DATA1 scripts.
Also: histogram the dword immediately preceding each `0x02 <off>` string ref
(candidate "takes-a-string" opcodes), with example files.
"""
import os, sys, collections
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import sys4load

# authoritative set: game-dir overrides shadow extracted/DATA1
files = paths.scripts()
print(f"{len(files)} scripts (overrides win)")

lead_hist = collections.Counter()          # dword after each T3 record
lead_examples = collections.defaultdict(set)
pre_str = collections.Counter()            # dword before each 0x02 <off> ref
pre_str_ex = collections.defaultdict(set)
first_dword = collections.Counter()
t3_rec_shape_ok = 0
t3_rec_total = 0
bad = 0

for name, p in sorted(files.items()):
    try:
        scr = sys4load.load(p)
    except Exception as e:
        bad += 1
        continue
    dw = scr.dwords
    code_len = scr.code_len
    first_dword[dw[0]] += 1
    # statement leads via T3 records
    for e in scr.table_entries("T3"):
        if not (0 <= e < code_len):
            continue
        t3_rec_total += 1
        if e + 2 < code_len and dw[e] == 0x8F and dw[e+1] == 0:
            t3_rec_shape_ok += 1
            nxt = e + 3
            if nxt < code_len:
                v = dw[nxt]
                lead_hist[v] += 1
                if len(lead_examples[v]) < 3:
                    lead_examples[v].add(name)
    # dword preceding string refs (ref index i+1 holds offset; tag at i)
    for ref_idx in scr.string_refs:          # ref_idx points at the offset dword
        tag_idx = ref_idx - 1                # the 0x02
        if tag_idx - 1 >= 0:
            v = dw[tag_idx - 1]
            pre_str[v] += 1
            if len(pre_str_ex[v]) < 3:
                pre_str_ex[v].add(name)

print(f"parse failures: {bad}")
print(f"T3 records with shape [0x8F,0,*]: {t3_rec_shape_ok}/{t3_rec_total}")
print("\n== first body dword ==")
for v, c in first_dword.most_common(8):
    print(f"  {v:#06x}  {c}")
print(f"\n== statement leads after T3 records (top 40 of {len(lead_hist)}) ==")
total = sum(lead_hist.values())
for v, c in lead_hist.most_common(40):
    ex = ",".join(sorted(lead_examples[v]))
    print(f"  {v:#06x}  {c:>7} ({100*c/total:5.2f}%)  e.g. {ex}")
print(f"\n== dword preceding `0x02 <off>` string refs (top 25 of {len(pre_str)}) ==")
tot2 = sum(pre_str.values())
for v, c in pre_str.most_common(25):
    ex = ",".join(sorted(pre_str_ex[v]))
    print(f"  {v:#06x}  {c:>7} ({100*c/tot2:5.2f}%)  e.g. {ex}")
