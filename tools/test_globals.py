#!/usr/bin/env python3
"""Standalone tests for the globals registry tooling. Run: py -3.11 -X utf8 tools/test_globals.py"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import globals_build as G

FAILS = []
def check(cond, msg):
    print(("  ok  " if cond else " FAIL ") + msg)
    if not cond: FAILS.append(msg)

def test_load_and_lint():
    entries, meta = G.load_toml(paths.VM_MAP / "globals.toml")
    check(0xa57 in entries, "0xa57 present in globals.toml")
    check(entries[0xa57]["category"] == "story-flag", "0xa57 is category story-flag")
    check(entries[0x3234]["category"] == "story-flag", "0x3234 is category story-flag")
    check({0xa57, 0xa58, 0xa59} <= set(entries), "Lily form flags A/B/C all present")
    # lint clean against a permissive address universe (curated addrs are self-consistent)
    errors, warnings = G.lint(entries, set(entries))
    check(errors == [], f"globals.toml lints clean (errors={errors})")

def test_lint_catches_bad_vocab():
    bad = {0x1: {"_addr": 0x1, "name": "x", "category": "bogus",
                 "source": "auto-shape", "confidence": "high"}}
    errors, _ = G.lint(bad, {0x1})
    check(any("category" in e for e in errors), "lint flags bad category")
    check(any("confidence" in e for e in errors), "lint flags auto-shape claiming high confidence")

def test_merge_precedence():
    curated, _ = G.load_toml(paths.VM_MAP / "globals.toml")
    auto = G.load_auto(paths.BUILD / "global-var-map.json")
    merged = G.merge(curated, auto)
    check(merged[0xa57]["name"] == "lily_form_a", "curated 0xa57 name wins over auto label")
    check(merged[0xa57]["category"] == "story-flag", "curated 0xa57 category overrides auto string-table")
    check(merged[0xa57]["provenance"] == "curated", "0xa57 marked curated")
    # an address only in the auto map falls through as provenance=auto
    auto_only = next((a for a in auto.get("globals", {})
                      if int(a, 16) not in curated and auto["globals"][a].get("label")), None)
    check(auto_only is not None and merged[int(auto_only, 16)]["provenance"] == "auto",
          "auto-only address retained with provenance=auto")

if __name__ == "__main__":
    test_load_and_lint()
    test_lint_catches_bad_vocab()
    test_merge_precedence()
    print(f"\n{len(FAILS)} failures")
    sys.exit(1 if FAILS else 0)
