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
    check(entries[0x4e085]["columns"] == {
        "0": "current_hp", "1": "current_sp", "2": "current_fs"
    }, "entity current-resource columns are curated")
    check(entries[0x4e11b]["columns"]["10"] == "movement"
          and entries[0x4e11b]["columns"]["13"] == "max_fs",
          "entity effective-stat columns are curated")
    check(entries[0x5231f]["name"] == "entity_tile_x"
          and entries[0x52351]["name"] == "entity_tile_y",
          "entity map-coordinate arrays are curated")
    check(entries[0x4e021]["name"] == "entity_runtime_flags"
          and entries[0x522ed]["name"] == "entity_faction_ids",
          "entity activity and faction arrays are curated")
    check(entries[0x56b20]["name"] == "entity_patrol_waypoint_indices",
          "RTN_M011 waypoint state is curated")
    check(entries[0xaba96]["name"] == "pathfinding_remaining_route_steps"
          and entries[0xcc9f1]["name"] == "movement_search_mode",
          "movement-search reachability state is curated")
    check(entries[0xb240e]["name"] == "pathfinding_movement_costs",
          "movement-cost work grid is curated")
    # lint clean against a permissive address universe (curated addrs are self-consistent)
    errors, warnings = G.lint(entries, set(entries))
    check(errors == [], f"globals.toml lints clean (errors={errors})")

def test_lint_catches_bad_vocab():
    bad = {0x1: {"_addr": 0x1, "name": "x", "category": "bogus",
                 "source": "auto-shape", "confidence": "high",
                 "columns": {"not-an-index": "x", "-1": ""}}}
    errors, _ = G.lint(bad, {0x1})
    check(any("category" in e for e in errors), "lint flags bad category")
    check(any("confidence" in e for e in errors), "lint flags auto-shape claiming high confidence")
    check(any("column index" in e for e in errors), "lint flags nonnumeric column indices")
    check(any("negative column" in e for e in errors), "lint flags negative column indices")

def test_merge_precedence():
    curated, _ = G.load_toml(paths.VM_MAP / "globals.toml")
    auto = G.load_auto(paths.BUILD / "global-var-map.json")
    merged = G.merge(curated, auto)
    check(merged[0xa57]["name"] == "lily_form_a", "curated 0xa57 name wins over auto label")
    check(merged[0xa57]["category"] == "story-flag", "curated 0xa57 category overrides auto string-table")
    check(merged[0xa57]["provenance"] == "curated", "0xa57 marked curated")
    check(merged[0x9f541]["columns"]["8"] == "critical_chance",
          "curated row-table column semantics survive the merge")
    # an address only in the auto map falls through as provenance=auto
    auto_only = next((a for a in auto.get("globals", {})
                      if int(a, 16) not in curated and auto["globals"][a].get("label")), None)
    check(auto_only is not None and merged[int(auto_only, 16)]["provenance"] == "auto",
          "auto-only address retained with provenance=auto")

def test_sys4load_labels_from_registry():
    import importlib, sys4load
    importlib.reload(sys4load)   # re-run _load_global_labels against current build/globals.json
    lbl = sys4load.GLOBAL_LABELS.get(0x3234, "")
    check("chapter_mode" in lbl, f"sys4load labels 0x3234 with curated name (got {lbl!r})")
    lbl2 = sys4load.GLOBAL_LABELS.get(0xa57, "")
    check("lily_form_a" in lbl2, f"sys4load labels 0xa57 with curated name (got {lbl2!r})")

def test_miner_finds_known_flags():
    import story_flags
    cands = story_flags.mine()
    check(0x3234 in cands, "miner surfaces chapter flag 0x3234")
    check(set(range(1, 9)) <= set(cands[0x3234]["consts"]), "0x3234 compared against 1..8 enum")
    check(cands[0x3234]["category"] == "story-flag", "0x3234 classified story-flag")
    check(0xa57 in cands, "miner surfaces Lily form flag 0xa57")
    check(cands[0xa57]["reach_scenes"] >= 70, "0xa57 high scene reach")
    check(cands[0xa57]["category"] == "story-flag", "0xa57 classified story-flag")

def test_bootstrap_is_additive_and_idempotent():
    import tempfile, pathlib, story_flags
    # Minimal fixture (one curated entry) so bootstrap always has candidates to add, independent
    # of how many the real globals.toml already holds.
    src = ('[meta]\nnote = "fixture"\n\n[[global]]\naddress = "0xa57"\nname = "lily_form_a"\n'
           'category = "story-flag"\ntype = "int"\nvalue_domain = "{0,1}"\nusage = "curated"\n'
           'source = "investigation"\nconfidence = "high"\ndepends_on = []\n')
    with tempfile.TemporaryDirectory() as d:
        tp = pathlib.Path(d) / "globals.toml"
        tp.write_text(src, encoding="utf-8")
        before, _ = G.load_toml(tp)
        cands = story_flags.mine()
        story_flags.bootstrap(cands, toml_path=tp)
        after, _ = G.load_toml(tp)
        check(len(after) > len(before), "bootstrap adds new skeleton entries")
        check(after[0xa57]["name"] == "lily_form_a", "bootstrap preserves curated 0xa57")
        # idempotent: second run adds nothing
        n1 = len(after)
        story_flags.bootstrap(story_flags.mine(), toml_path=tp)
        after2, _ = G.load_toml(tp)
        check(len(after2) == n1, "second bootstrap is a no-op (idempotent)")
        # every skeleton is auto-shape and not high-confidence
        added = set(after) - set(before)
        check(all(after[a]["source"] == "auto-shape" for a in added), "skeletons are source=auto-shape")
        check(all(after[a]["confidence"] != "high" for a in added), "skeletons never high confidence")

if __name__ == "__main__":
    test_load_and_lint()
    test_lint_catches_bad_vocab()
    test_merge_precedence()
    test_sys4load_labels_from_registry()
    test_miner_finds_known_flags()
    test_bootstrap_is_additive_and_idempotent()
    print(f"\n{len(FAILS)} failures")
    sys.exit(1 if FAILS else 0)
