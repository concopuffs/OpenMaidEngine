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

def test_decode_anchor_and_count():
    scr = S.load_scjump()
    decs = S.decode(scr)
    check(len({d["site_offset"] for d in decs}) == 1755, f"1755 distinct decision sites (got {len({d['site_offset'] for d in decs})})")
    # anchor: chapter 1, decision 0, guarded by 0x6d3 != 1
    anchor = [d for d in decs if d["chapter"] == 1 and d["decision"] == 0]
    check(len(anchor) >= 1, "chapter-1 decision 0 exists")
    a = anchor[0]
    has_chapter = any(g.get("global") == 0x3234 and g.get("op") == "==" and g.get("value") == 1 for g in a["guards"])
    has_flag = any(g.get("global") == 0x6d3 and g.get("op") == "!=" and g.get("value") == 1 for g in a["guards"])
    check(has_chapter, "anchor guarded by chapter_mode==1")
    check(has_flag, "anchor guarded by 0x6d3!=1")

def test_emit_json_shape():
    scr = S.load_scjump()
    decs = S.decode(scr)
    names = S.load_names()
    obj = json.loads(S.emit_json(decs, names))
    check(obj["meta"]["decision_sites"] == 1755, "json meta reports 1755 sites")
    check(all(set(("site_offset", "chapter", "decision", "guards")) <= set(d) for d in obj["decisions"]), "each decision has required keys")
    # rendering uses registry names when present
    check(S.render_guard({"global": 0x3234, "op": "==", "value": 7}, {0x3234: "chapter_mode"}) == "chapter_mode==7",
          "render_guard uses registry name")

if __name__ == "__main__":
    test_cfg_acyclic_and_dispatch()
    test_decode_anchor_and_count()
    test_emit_json_shape()
    print(f"\n{len(FAILS)} failures")
    sys.exit(1 if FAILS else 0)
