#!/usr/bin/env python3
"""Extract a *INIT data table to JSON. Auto-detects the table's shape.

*INIT scripts populate parallel global arrays with static game data. Three shapes seen:

  name   — records keyed by a name string. Each record: set-string(name), mov(fields..),
           set-string(desc). Arrays indexed by record id in lockstep (+1/record).
           (SKINIT skills, ITINIT items, EBINIT units)
  numeric— column table with NO names: mov/copy-to-global into parallel int arrays, keyed
           by an incrementing index column. (CGINIT gallery)
  footer — copy-local-array (op 0x64) bulk-loads length-prefixed arrays from the file
           footer into per-record global arrays. The data lives in the footer. (MPINIT maps)

Records are {id, name?, desc?, fields:{"0x<col_base>": value}} or, for footer tables,
{id, global_addr, footer_off, values:[...]}. Column addresses are raw engine globals;
naming them (attack, cost, …) needs the engine global-var map — later work.

Usage: py -3.11 -X utf8 tools/extract_init.py <TABLE> [OUTNAME] [--mode name|numeric|footer]
"""
from __future__ import annotations
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths
import sys4load

SET_STRING = 0x192
MOV = 0x55
COPY_TO_GLOBAL = 0x6C
COPY_LOCAL_ARRAY = 0x64
T_GLOBAL_INT = 3
T_GLOBAL_STRING = 5
T_IMM = 0


def resolve(name: str) -> Path:
    for cand in (paths.GAME_DIR / f"{name}.BIN", paths.DATA1 / f"{name}.BIN"):
        if cand.exists():
            return cand
    raise SystemExit(f"not found: {name}.BIN")


def _val(arg):
    """Render an operand as an int (immediate) or a {type,value} ref."""
    t, v = arg
    return v if t == T_IMM else {"type": f"0x{t:x}", "value": f"0x{v:x}"}


def read_footer_array(scr, off):
    """Read a length-prefixed Data_Array at dword `off`: [length][v0..v_{length-1}]."""
    dw = scr.dwords
    if not (0 <= off < scr.nbody):
        return None
    length = dw[off]
    if length > scr.nbody or off + 1 + length > scr.nbody:
        return None
    return list(dw[off + 1: off + 1 + length])


def detect_mode(scr):
    ops = [ins.opcode for ins in scr.instructions]
    has_str = any(ins.opcode == SET_STRING and ins.args and ins.args[0][0] == T_GLOBAL_STRING
                  for ins in scr.instructions)
    if has_str:
        return "name"
    n_footer = ops.count(COPY_LOCAL_ARRAY)
    n_int = ops.count(MOV) + ops.count(COPY_TO_GLOBAL)
    return "footer" if n_footer >= max(4, n_int) else "numeric"


def extract_name(scr):
    name_base = None
    for ins in scr.instructions:
        if ins.opcode == SET_STRING and ins.args and ins.args[0][0] == T_GLOBAL_STRING:
            name_base = ins.args[0][1]; break
    records, cur, prev_gstr, desc_slot, desc_bases = [], None, None, 0, {}
    for ins in scr.instructions:
        if ins.opcode == SET_STRING and ins.args and ins.args[0][0] == T_GLOBAL_STRING:
            addr = ins.args[0][1]
            txt = scr.strings.get(ins.args[1][1], (None,))[0] if len(ins.args) > 1 else None
            if prev_gstr is None or addr < prev_gstr:
                cur = {"id": addr - name_base, "name": txt, "fields": {}}
                records.append(cur); desc_slot = 0
            elif cur is not None:
                key = "desc" if desc_slot == 0 else f"desc{desc_slot}"
                cur[key] = txt; desc_bases.setdefault(key, addr - cur["id"]); desc_slot += 1
            prev_gstr = addr
        elif ins.opcode == MOV and cur is not None and ins.args and ins.args[0][0] == T_GLOBAL_INT:
            cur["fields"][f"0x{ins.args[0][1] - cur['id']:x}"] = _val(ins.args[1])
    return records, {"name_array_base": f"0x{name_base:x}",
                     "desc_array_bases": {k: f"0x{v:x}" for k, v in sorted(desc_bases.items())}}


def _int_writes(scr):
    """Ordered (addr, value_arg) for global-int mov / copy-to-global."""
    out = []
    for ins in scr.instructions:
        if ins.opcode in (MOV, COPY_TO_GLOBAL) and ins.args and ins.args[0][0] == T_GLOBAL_INT:
            out.append((ins.args[0][1], ins.args[1]))
    return out


def _longest_stride1_column(addrs):
    """Pick the primary index array: the stride-1 arithmetic run covering the most records."""
    seen = set(addrs)
    best_base, best_len = None, 0
    for a in sorted(seen):
        if a - 1 in seen:
            continue                       # only start at a run's base
        n = 0
        while a + n in seen:
            n += 1
        if n > best_len:
            best_base, best_len = a, n
    return best_base, best_len


def extract_numeric(scr):
    writes = _int_writes(scr)
    base, n = _longest_stride1_column([a for a, _ in writes])
    if base is None:
        return [], {}
    primary = set(range(base, base + n))
    records, buf = [], []
    for addr, varg in writes:
        buf.append((addr, varg))
        if addr in primary:               # primary write closes the record
            rid = addr - base
            fields = {f"0x{a - rid:x}": _val(v) for a, v in buf}
            records.append({"id": rid, "fields": fields})
            buf = []
    return records, {"primary_index_base": f"0x{base:x}", "record_span": n}


def extract_footer(scr):
    records = []
    for i, ins in enumerate(scr.instructions):
        if ins.opcode == COPY_LOCAL_ARRAY and ins.args and ins.args[0][0] == T_GLOBAL_INT:
            addr = ins.args[0][1]
            foff = ins.args[1][1]
            vals = read_footer_array(scr, foff)
            records.append({"id": i, "global_addr": f"0x{addr:x}",
                            "footer_off": f"0x{foff:x}",
                            "length": len(vals) if vals else 0,
                            "values": vals if vals else []})
    return records, {}


def main() -> int:
    argv = [a for a in sys.argv[1:] if not a.startswith("--")]
    mode_arg = next((sys.argv[i + 1] for i, a in enumerate(sys.argv) if a == "--mode"), None)
    if not argv:
        raise SystemExit(__doc__)
    name = argv[0].upper().removesuffix(".BIN")
    outname = argv[1] if len(argv) > 1 else name
    scr = sys4load.load(resolve(name))

    mode = mode_arg or detect_mode(scr)
    extractor = {"name": extract_name, "numeric": extract_numeric, "footer": extract_footer}[mode]
    recs, meta = extractor(scr)

    cols = sorted({c for r in recs for c in r.get("fields", {})}, key=lambda h: int(h, 16))
    out = {"table": name, "source": scr.path.name, "magic": scr.magic, "mode": mode,
           "record_count": len(recs), **meta,
           "field_columns": cols if mode != "footer" else None, "records": recs}
    outpath = paths.BUILD / "data" / f"{outname}.json"
    outpath.parent.mkdir(parents=True, exist_ok=True)
    outpath.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{name}: mode={mode}, {len(recs)} records"
          + (f", {len(cols)} field-columns" if mode != 'footer' else "")
          + f" -> build/data/{outname}.json")
    for r in recs[:4]:
        if mode == "footer":
            print(f"  id {r['id']:>4}  {r['global_addr']} <- footer {r['footer_off']} "
                  f"len {r['length']}  head={r['values'][:8]}")
        else:
            f4 = {k: r['fields'][k] for k in list(r['fields'])[:4]}
            print(f"  id {r['id']:>4}  {r.get('name','')!r:12} desc={r.get('desc','')!r} {f4}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
