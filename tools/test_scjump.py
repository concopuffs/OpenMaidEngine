#!/usr/bin/env python3
"""Standalone tests for the SCJUMP decoder. Run: py -3.11 -X utf8 tools/test_scjump.py"""
import os, sys, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import scjump_decode as S

FAILS = []
def check(cond, msg):
    print(("  ok  " if cond else " FAIL ") + msg)
    if not cond: FAILS.append(msg)

def test_cfg_acyclic_and_dispatch():
    scr = S.load_scjump()
    check(S.forward_edges_only(scr), "SCJUMP CFG is acyclic (all code edges forward)")
    disp = S.chapter_dispatch(scr)
    expected = {1:0x81, 2:0x9f, 3:0x1ded, 4:0x1e6d, 5:0x54c1, 6:0x93c4, 7:0x1e868, 8:0x2aa42, 9:0x2b8a3}
    check(disp == expected, f"chapter dispatch matches known offsets (got {disp})")

if __name__ == "__main__":
    test_cfg_acyclic_and_dispatch()
    print(f"\n{len(FAILS)} failures")
    sys.exit(1 if FAILS else 0)
