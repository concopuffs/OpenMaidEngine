#!/usr/bin/env python3
"""Partial global-variable map — label raw global offsets by evidence (static, read-only).

Combines three static signals (see docs/name-resolution.md #2):
  1. *INIT writers   — build/data/*.json name/desc/field bases ARE labelable global addresses.
  2. string anchors  — set-string targets across the corpus = string tables.
  3. access shape    — how each global is used: 2D-table base (+stride), 1D-array base,
                       row-index (=> "current entity" pointer), or scalar.

Emits build/global-var-map.json (all evidence) + build/global-var-map.md (labelled subset).
Usage: py -3.11 -X utf8 tools/global_map.py
"""
from __future__ import annotations
import json
import sys
import collections
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
import paths
import sys4load

CORPUS = paths.DATA1
DATA = paths.BUILD / "data"
OUT = paths.BUILD

LOOKUP = 0x61            # lookup-array:    (dst, base1d, idx)
LOOKUP2D = 0x12c        # lookup-array-2d: (dst, base2d, rowidx, stride, col)
SET_STRING = 0x192      # (strbase, text)
GLOBAL_TYPES = {3: "int", 4: "float", 5: "string", 6: "ptr", 8: "string-ptr"}

INIT_IDENTITY = {
    "SKINIT": "skill", "ITINIT": "item", "EBINIT": "unit", "CGINIT": "cg-gallery",
    "MPINIT": "map", "STINIT": "stage",
}


def from_init_tables():
    """Label name/desc/field base addresses from the extracted *INIT JSONs."""
    labels = {}                              # addr -> (label, table, confidence, kind)
    for jf in sorted(DATA.glob("*.json")):
        try:
            d = json.loads(jf.read_text(encoding="utf-8"))
        except Exception:
            continue
        tbl = d.get("table", jf.stem)
        ent = INIT_IDENTITY.get(tbl, tbl.lower())
        if d.get("name_array_base"):
            a = int(d["name_array_base"], 16)
            labels[a] = (f"{ent}-name-table", tbl, "high", "string-table")
        for k, v in (d.get("desc_array_bases") or {}).items():
            labels[int(v, 16)] = (f"{ent}-{k}-table", tbl, "high", "string-table")
        # field columns: classify dense (shared) vs sparse (per-entity extras)
        recs = d.get("records", [])
        freq = collections.Counter()
        for r in recs:
            for c in r.get("fields", {}):
                freq[c] += 1
        n = max(1, len(recs))
        for col, c in freq.items():
            a = int(col, 16)
            if a in labels:
                continue
            dense = c >= 0.5 * n
            labels[a] = (f"{ent}-field" + ("" if dense else "?"), tbl,
                         "med" if dense else "low", "entity-field-array")
    return labels


def scan_corpus():
    scrs = []
    for p in sorted(CORPUS.glob("*.BIN")):
        try:
            scrs.append(sys4load.load(p))
        except Exception:
            pass
    roles = collections.defaultdict(collections.Counter)   # addr -> role -> count
    strides = collections.defaultdict(collections.Counter)  # addr -> stride -> count
    gtype = {}                                              # addr -> global type name
    rowidx = collections.Counter()                         # addr -> times used as 2D row index
    strtable_writers = collections.defaultdict(collections.Counter)  # straddr -> script -> count
    for scr in scrs:
        sname = scr.path.stem
        for ins in scr.instructions:
            op = ins.opcode
            for pos, (t, v) in enumerate(ins.args):
                if t not in GLOBAL_TYPES:
                    continue
                gtype[v] = GLOBAL_TYPES[t]
                if op == LOOKUP2D and pos == 1:
                    roles[v]["2d-base"] += 1
                    if len(ins.args) > 3 and ins.args[3][0] == 0:      # stride immediate
                        strides[v][ins.args[3][1]] += 1
                elif op == LOOKUP2D and pos == 2:
                    roles[v]["row-index"] += 1
                    rowidx[v] += 1
                elif op == LOOKUP and pos == 1:
                    roles[v]["1d-base"] += 1
                elif op == SET_STRING and pos == 0:
                    roles[v]["string-table"] += 1
                    strtable_writers[v][sname] += 1
                else:
                    roles[v]["scalar"] += 1
    return roles, strides, gtype, rowidx, strtable_writers, len(scrs)


def main() -> int:
    init_labels = from_init_tables()
    roles, strides, gtype, rowidx, strwriters, nscr = scan_corpus()
    all_addrs = set(roles) | set(init_labels)

    # merge into per-address records
    entries = {}
    for a in sorted(all_addrs):
        r = dict(roles.get(a, {}))
        total = sum(r.values())
        st = sorted(strides.get(a, {}), key=lambda s: -strides[a][s])
        label = conf = kind = None
        if a in init_labels:
            label, tbl, conf, kind = init_labels[a]
        elif r.get("string-table"):
            w = strwriters.get(a, {})
            top = max(w, key=w.get) if w else "?"
            label, conf, kind = f"string-table (written by {top})", "med", "string-table"
        elif r.get("2d-base"):
            stride = st[0] if st else "?"
            label, conf, kind = f"record-table[stride {stride}]", "med", "record-table-2d"
        elif r.get("1d-base"):
            label, conf, kind = "array", "low", "array-1d"
        elif r.get("row-index"):        # used as a 2D row index but never a base -> an index var
            ri, sc = r["row-index"], r.get("scalar", 0)
            pure = ri / (ri + sc) if (ri + sc) else 0
            if pure >= 0.3:
                label, conf, kind = "current-entity-index?", "med", "index"
            else:
                label, conf, kind = "index/counter?", "low", "index"
        entries[a] = dict(addr=f"0x{a:x}", type=gtype.get(a, "?"), uses=total,
                          roles=r, stride_candidates=[f"0x{s:x}" for s in st[:3]] or None,
                          label=label, confidence=conf, kind=kind, table=(init_labels.get(a, (None, None))[1]))

    # "current entity" pointers: globals used as 2D row index, ranked by purity
    def purity(a):
        ri, sc = rowidx[a], roles[a].get("scalar", 0)
        return ri / (ri + sc) if (ri + sc) else 0
    current_idx = [dict(addr=f"0x{a:x}", used_as_row_index=rowidx[a],
                        also_scalar=roles[a].get("scalar", 0), purity=round(purity(a), 2))
                   for a in sorted(rowidx, key=lambda a: (-round(purity(a), 2), -rowidx[a]))
                   if rowidx[a] >= 10][:15]

    labeled = {k: v for k, v in entries.items() if v["label"]}
    out = {
        "generated_from": f"extracted/DATA1 ({nscr} scripts) + build/data/*.json",
        "note": "Static partial map. Labels ending '?' are low-confidence. Column addresses "
                "are raw engine globals; Frida can confirm the ambiguous ones (see docs/name-resolution.md).",
        "totals": {"distinct_globals_seen": len(all_addrs), "labelled": len(labeled),
                   "from_init_tables": len(init_labels)},
        "current_entity_index_candidates": current_idx,
        "globals": {v["addr"]: {k: val for k, val in v.items() if k != "addr"}
                    for v in entries.values()},
    }
    (OUT / "global-var-map.json").write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")

    # readable markdown of the labelled subset
    md = ["# Partial global-variable map", "",
          f"Static map from {nscr} scripts + `build/data/*.json`. "
          f"**{len(labeled)} of {len(all_addrs)} distinct globals labelled.** "
          "Regenerate: `py -3.11 -X utf8 tools/global_map.py`. See `docs/name-resolution.md`.", "",
          "## 'Current entity' index globals (dominant 2D row-index)", "",
          "The row index into per-entity tables — the VM's \"which unit/entity are we on\" pointers. "
          "Ranked by purity (fraction of uses that are row-index vs. general scalar); high purity = "
          "a dedicated index pointer, low = a general-purpose var reused as an index.", "",
          "| global | row-index uses | also scalar | purity |", "|---|---|---|---|"]
    for c in current_idx:
        md.append(f"| `{c['addr']}` | {c['used_as_row_index']} | {c['also_scalar']} | {c['purity']} |")
    for kind, title in [("string-table", "String tables (names / descriptions / messages)"),
                        ("entity-field-array", "Per-entity data-field arrays (from *INIT)"),
                        ("record-table-2d", "Row-major record tables (from access shape)")]:
        rows = sorted((v for v in labeled.values() if v["kind"] == kind), key=lambda v: -v["uses"])
        md += ["", f"## {title}  ({len(rows)})", "", "| global | type | label | conf | uses | stride |",
               "|---|---|---|---|---|---|"]
        for v in rows[:40]:
            md.append(f"| `{v['addr']}` | {v['type']} | {v['label']} | {v['confidence']} | "
                      f"{v['uses']} | {(v['stride_candidates'] or ['—'])[0]} |")
        if len(rows) > 40:
            md.append(f"| … | | +{len(rows)-40} more (see JSON) | | | |")
    (OUT / "global-var-map.md").write_text("\n".join(md), encoding="utf-8")

    print(f"distinct globals seen: {len(all_addrs)};  labelled: {len(labeled)} "
          f"({len(init_labels)} from *INIT)")
    print(f"top 'current entity' index globals: " +
          ", ".join(f"{c['addr']}({c['used_as_row_index']})" for c in current_idx[:5]))
    print(f"-> build/global-var-map.json, build/global-var-map.md")
    return 0


if __name__ == "__main__":
    sys.exit(main())
