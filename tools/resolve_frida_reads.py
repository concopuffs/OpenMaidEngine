#!/usr/bin/env python3
"""Resolve Frida archive-read offsets -> asset names via build/asset-index.json.

The runtime capture (tools/frida/capture_graphics.py) logs raw ReadFile spans on the
DATA*.ALF archives as `<path>\t<offset>\t<size>` lines. On their own those offsets are
opaque and mixed with OS memory-map paging (the doc's "Frida file-I/O is noisy"). With
the SYS4INI asset index as the answer key we can turn each offset back into the *asset*
it belongs to, and thereby recover the real per-scene **asset load order** — the ground
truth for cracking resId->filename (see docs/asset-resolution-re.md, step 2).

Two read signals per asset (observed): a burst of tiny header reads starting exactly at
the asset's archive offset (delta 0), then one bulk read of the payload. Uniform 0x20000
(131072-byte) reads are memory-map paging and are dropped. We treat a read whose offset
*exactly* equals an index entry's offset as an unambiguous "asset-start" event; the
ordered, de-duplicated sequence of those is the load order.

Usage: py -3.11 -X utf8 tools/resolve_frida_reads.py [reads.log] [-o out.json]
  default reads.log = build/frida-reads.log ; default out = build/frida-asset-loads.json
"""
from __future__ import annotations
import bisect
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths

PAGING_SIZE = 131072   # 0x20000 uniform memory-map paging reads -> noise


def load_index() -> dict:
    idx = json.loads((paths.BUILD / "asset-index.json").read_text(encoding="utf-8"))
    by_arc: dict[str, list[tuple[int, int, str]]] = {}
    for f in idx["files"]:
        by_arc.setdefault(f["archive"], []).append((f["offset"], f["size"], f["name"]))
    for a in by_arc:
        by_arc[a].sort()
    return by_arc


def resolve(by_arc, arc, off):
    """Return (name, entry_offset, size, delta) for the asset whose range holds `off`."""
    arr = by_arc.get(arc)
    if not arr:
        return None
    i = bisect.bisect_right(arr, (off, float("inf"), "")) - 1
    if i < 0:
        return None
    o, s, n = arr[i]
    return (n, o, s, off - o) if off < o + s else None


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    out_flag = next((sys.argv[i + 1] for i, a in enumerate(sys.argv) if a == "-o"), None)
    log = Path(args[0]) if args else paths.BUILD / "frida-reads.log"
    out = Path(out_flag) if out_flag else paths.BUILD / "frida-asset-loads.json"
    if not log.exists():
        raise SystemExit(f"reads log not found: {log}")

    by_arc = load_index()
    # exact-offset -> name per archive (asset-start detector)
    exact = {a: {o: n for o, _, n in v} for a, v in by_arc.items()}

    per_arc: dict[str, int] = {}
    paging = unresolved = total = 0
    starts = []           # ordered (arc, name) asset-start events (deduped consecutively)
    contained = set()     # every distinct asset any read touched

    for ln in log.read_text(encoding="utf-8").splitlines():
        parts = ln.split("\t")
        if len(parts) != 3:
            continue
        path, off_s, size_s = parts
        arc = path.replace("\\", "/").rsplit("/", 1)[-1]
        off, size = int(off_s), int(size_s)
        total += 1
        per_arc[arc] = per_arc.get(arc, 0) + 1
        if size == PAGING_SIZE:
            paging += 1
            continue
        hit = resolve(by_arc, arc, off)
        if hit is None:
            unresolved += 1
            continue
        name = hit[0]
        contained.add((arc, name))
        if off in exact.get(arc, {}):                       # exact asset-start
            ev = (arc, exact[arc][off])
            if not starts or starts[-1] != ev:
                starts.append(ev)

    result = {
        "source_log": log.name,
        "asset_index": "asset-index.json",
        "total_reads": total,
        "paging_reads_dropped": paging,
        "unresolved_reads": unresolved,
        "reads_per_archive": per_arc,
        "distinct_assets_touched": len(contained),
        "load_order_count": len(starts),
        "load_order": [{"archive": a, "name": n} for a, n in starts],
    }
    out.write_text(json.dumps(result, ensure_ascii=False, indent=1), encoding="utf-8")

    print(f"{log.name}: {total} reads ({paging} paging dropped, {unresolved} unresolved)")
    print(f"reads/archive: {per_arc}")
    print(f"distinct assets touched: {len(contained)}; "
          f"asset-start load order: {len(starts)} events")
    for a, n in starts:
        print(f"  {a:<12} {n}")
    print(f"-> {out.relative_to(paths.REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
