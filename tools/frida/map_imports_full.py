#!/usr/bin/env python3
"""Task 2 of the frida import-map slice: write build/import-map.json from the live scan.

Splits the pointer matches into (a) those inside a contiguous in-module import-table run
(>= min_run) -> auto-apply, written to build/import-map.json; and (b) isolated singletons
(more likely a coincidental DWORD) -> build/import-map-singletons.json for review, NOT
auto-applied. Invoked by `map_imports.py` default mode (no --recon).

See docs/superpowers/plans/2026-07-09-frida-import-map.md (Task 2) and the design spec.
"""
import json

from map_imports import BUILD, build_export_index, cluster_runs, collect, scan_pointer_matches


def select_table_matches(matches, runs, min_run=3):
    """Split [(rva,val,name)] by whether each RVA falls in a contiguous run of >= min_run.

    Returns (table, singles) as {hex_rva: 'dll!func'} dicts.
    """
    run_rvas = set()
    for start, count in runs:
        if count >= min_run:
            run_rvas.update(start + i * 4 for i in range(count))
    table = {hex(r): n for r, _, n in matches if r in run_rvas}
    singles = {hex(r): n for r, _, n in matches if r not in run_rvas}
    return table, singles


def build():
    exports, base, mem = collect()
    idx = build_export_index(exports)
    matches = scan_pointer_matches(mem, base, idx)
    runs = cluster_runs([r for r, _, _ in matches])
    table, singles = select_table_matches(matches, runs)
    (BUILD / "import-map.json").write_text(json.dumps(table, indent=2) + "\n", encoding="utf-8")
    (BUILD / "import-map-singletons.json").write_text(json.dumps(singles, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {len(table)} table imports -> build/import-map.json; "
          f"{len(singles)} singletons -> build/import-map-singletons.json (review, not applied)")


if __name__ == "__main__":
    build()
