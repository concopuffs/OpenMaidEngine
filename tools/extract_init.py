#!/usr/bin/env python3
"""Extract a *INIT data table to JSON. Auto-detects the table's shape.

*INIT scripts populate global arrays and work buffers with static game data. Four shapes seen:

  name   — records keyed by a name string. Each record: set-string(name), static field writes,
           set-string(desc). Arrays indexed by record id in lockstep (+1/record).
           (SKINIT skills, ITINIT items, EBINIT units)
  numeric— column table with NO names: mov/copy-to-global into parallel int arrays, keyed
           by an incrementing index column. (CGINIT gallery)
  footer — copy-local-array (op 0x64) bulk-loads length-prefixed arrays from the file
           footer into per-record global arrays. The data lives in the footer. (MPINIT maps)
  mixed  — a sparse selector dispatch writes strings, scalars, fixed-buffer cells, and
           footer arrays for one runtime record. (STINIT stages)

Records are {id, name?, desc?, fields:{"0x<col_base>": value}} or, for footer tables,
{id, global_addr, footer_off, values:[...]}. Column addresses are raw engine globals;
naming them (attack, cost, …) needs the engine global-var map — later work.

Usage: py -3.11 -X utf8 tools/extract_init.py <TABLE> [OUTNAME] [--mode name|numeric|footer|mixed]
"""
from __future__ import annotations
import json
import sys
from functools import cache
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths
import extract_message_table
import sys4load

SET_STRING = 0x192
MOV = 0x55
SUB = 0x51
COPY_TO_GLOBAL = 0x6C
COPY_LOCAL_ARRAY = 0x64
T_GLOBAL_INT = 3
T_GLOBAL_STRING = 5
T_IMM = 0
T_LOCAL_INT = 9

MESSAGE_TABLES = {
    "ITINIT": "ITMES",
    "SKINIT": "SKMES",
}


def resolve(name: str) -> Path:
    for cand in (paths.GAME_DIR / f"{name}.BIN", paths.DATA1 / f"{name}.BIN"):
        if cand.exists():
            return cand
    raise SystemExit(f"not found: {name}.BIN")


def normalize_outname(value: str) -> str:
    """Accept a generated-file stem, not a path; tolerate one `.json` suffix."""
    if not value or Path(value).name != value or "/" in value or "\\" in value:
        raise ValueError("OUTNAME must be a file stem, not a path")
    outname = value.removesuffix(".json")
    if not outname or outname in {".", ".."}:
        raise ValueError("OUTNAME must be a nonempty file stem")
    return outname


def _val(arg):
    """Render an operand as an int (immediate) or a {type,value} ref."""
    t, v = arg
    return v if t == T_IMM else {"type": f"0x{t:x}", "value": f"0x{v:x}"}


def _static_global_write(ins):
    """Return (destination, value) for statically evaluable global-int writes.

    The shipped name-mode INIT scripts encode positive values with `mov` and
    negative values with `sub destination, 0, magnitude`.  Ignoring the latter
    silently drops costs and penalties from the extracted schema.
    """
    if not ins.args or ins.args[0][0] != T_GLOBAL_INT:
        return None
    if ins.opcode == MOV and len(ins.args) >= 2:
        return ins.args[0][1], _val(ins.args[1])
    if (ins.opcode == SUB and len(ins.args) >= 3
            and ins.args[1][0] == T_IMM and ins.args[2][0] == T_IMM):
        return ins.args[0][1], ins.args[1][1] - ins.args[2][1]
    return None


def read_footer_array(scr, off):
    """Read a length-prefixed Data_Array at dword `off`: [length][v0..v_{length-1}]."""
    dw = scr.dwords
    if not (0 <= off < scr.nbody):
        return None
    length = dw[off]
    if length > scr.nbody or off + 1 + length > scr.nbody:
        return None
    return list(dw[off + 1: off + 1 + length])


def _mixed_guards(scr):
    """Find the dominant `eq local, selector-global, record-id; jcc` dispatch."""
    candidates = []
    instructions = scr.instructions
    for index, ins in enumerate(instructions[:-1]):
        if (sys4load.display_label(ins.opcode) != "eq"
                or len(ins.args) < 3
                or ins.args[0][0] != T_LOCAL_INT
                or ins.args[1][0] != T_GLOBAL_INT
                or ins.args[2][0] != T_IMM):
            continue
        branch = instructions[index + 1]
        if (sys4load.display_label(branch.opcode) != "jcc"
                or not branch.args
                or branch.args[0] != ins.args[0]):
            continue
        candidates.append({
            "index": index,
            "offset": ins.offset,
            "selector": ins.args[1][1],
            "id": ins.args[2][1],
        })
    if not candidates:
        return []
    selector_counts = {}
    for guard in candidates:
        selector = guard["selector"]
        selector_counts[selector] = selector_counts.get(selector, 0) + 1
    selector = max(selector_counts, key=lambda value: (selector_counts[value], -value))
    return [guard for guard in candidates if guard["selector"] == selector]


def detect_mode(scr):
    ops = [ins.opcode for ins in scr.instructions]
    has_str = any(ins.opcode == SET_STRING and ins.args and ins.args[0][0] == T_GLOBAL_STRING
                  for ins in scr.instructions)
    if has_str and len(_mixed_guards(scr)) >= 4:
        return "mixed"
    if has_str:
        return "name"
    n_footer = ops.count(COPY_LOCAL_ARRAY)
    n_int = ops.count(MOV) + ops.count(COPY_TO_GLOBAL)
    return "footer" if n_footer >= max(4, n_int) else "numeric"


def _eval_static_arg(arg, locals_: dict[int, int]):
    arg_type, value = arg
    if arg_type == T_IMM:
        return value
    if arg_type == T_LOCAL_INT:
        return locals_.get(value)
    return None


def _mixed_array_layouts(scr, first_guard_index: int) -> dict[int, dict]:
    """Recover fixed global-buffer lengths initialized before the dispatch."""
    locals_: dict[int, int] = {}
    layouts: dict[int, dict] = {}
    for ins in scr.instructions[:first_guard_index]:
        label = sys4load.display_label(ins.opcode)
        if ins.args and ins.args[0][0] == T_LOCAL_INT:
            destination = ins.args[0][1]
            operands = [_eval_static_arg(arg, locals_) for arg in ins.args[1:]]
            value = None
            if label == "mov" and operands:
                value = operands[0]
            elif len(operands) >= 2 and None not in operands[:2]:
                left, right = operands[:2]
                if label == "add":
                    value = left + right
                elif label == "sub":
                    value = left - right
                elif label == "mul":
                    value = left * right
                elif label == "div" and right:
                    value = left // right
            if value is None:
                locals_.pop(destination, None)
            else:
                locals_[destination] = value
        if (ins.opcode == COPY_TO_GLOBAL
                and len(ins.args) >= 2
                and ins.args[0][0] == T_GLOBAL_INT):
            length = _eval_static_arg(ins.args[1], locals_)
            if isinstance(length, int) and length > 0:
                layouts[ins.args[0][1]] = {"length": length}

    known = dict(_known_record_tables())
    for base, layout in layouts.items():
        if stride := known.get(base):
            layout["stride"] = stride
            if layout["length"] % stride == 0:
                layout["rows"] = layout["length"] // stride
    return layouts


def _mixed_buffer_key(destination: int, layouts: dict[int, dict]) -> str | None:
    matches = [
        (base, destination - base)
        for base, layout in layouts.items()
        if base <= destination < base + layout["length"]
    ]
    if len(matches) > 1:
        raise ValueError(f"ambiguous mixed-table destination 0x{destination:x}: {matches}")
    if not matches:
        return None
    base, index = matches[0]
    return f"0x{base:x}/{index}"


def _store_unique(target: dict, key: str, value, record_id: int) -> None:
    if key in target and target[key] != value:
        raise ValueError(f"mixed record {record_id}: conflicting writes to {key}")
    target[key] = value


def extract_mixed(scr):
    """Extract selector-dispatched records that populate a shared runtime buffer."""
    guards = _mixed_guards(scr)
    if not guards:
        return [], {}
    layouts = _mixed_array_layouts(scr, guards[0]["index"])
    records = []
    instructions = scr.instructions
    for guard_index, guard in enumerate(guards):
        end = guards[guard_index + 1]["index"] if guard_index + 1 < len(guards) else len(instructions)
        record = {
            "id": guard["id"],
            "guard_offset": f"0x{guard['offset']:x}",
            "string_fields": {},
            "fields": {},
            "array_fields": {},
            "footer_arrays": {},
        }
        for ins in instructions[guard["index"] + 2:end]:
            if (ins.opcode == SET_STRING
                    and len(ins.args) >= 2
                    and ins.args[0][0] == T_GLOBAL_STRING):
                text = scr.strings.get(ins.args[1][1], (None,))[0]
                _store_unique(
                    record["string_fields"], f"0x{ins.args[0][1]:x}", text, record["id"]
                )
                continue
            if (ins.opcode == COPY_LOCAL_ARRAY
                    and len(ins.args) >= 2
                    and ins.args[0][0] == T_GLOBAL_INT
                    and ins.args[1][0] == T_IMM):
                destination = ins.args[0][1]
                footer_off = ins.args[1][1]
                values = read_footer_array(scr, footer_off)
                if values is None:
                    raise ValueError(
                        f"mixed record {record['id']}: invalid footer array 0x{footer_off:x}"
                    )
                key = _mixed_buffer_key(destination, layouts) or f"0x{destination:x}"
                _store_unique(record["footer_arrays"], key, {
                    "footer_off": f"0x{footer_off:x}",
                    "values": values,
                }, record["id"])
                continue
            if (write := _static_global_write(ins)) is not None:
                destination, value = write
                key = _mixed_buffer_key(destination, layouts)
                target = record["array_fields"] if key else record["fields"]
                _store_unique(target, key or f"0x{destination:x}", value, record["id"])
        for key in ("string_fields", "fields", "array_fields", "footer_arrays"):
            if not record[key]:
                del record[key]
        records.append(record)

    layouts_json = {
        f"0x{base:x}": layout for base, layout in sorted(layouts.items())
    }
    key_sort = lambda key: tuple(int(part, 0) for part in key.split("/"))
    return records, {
        "selector_global": f"0x{guards[0]['selector']:x}",
        "array_layouts": layouts_json,
        "string_field_columns": sorted({
            key for record in records for key in record.get("string_fields", {})
        }, key=lambda key: int(key, 16)),
        "array_field_columns": sorted({
            key for record in records for key in record.get("array_fields", {})
        }, key=key_sort),
        "footer_array_columns": sorted({
            key for record in records for key in record.get("footer_arrays", {})
        }, key=key_sort),
    }


def _infer_record_span(string_addrs):
    """Infer the reserved width of one parallel string-array column.

    The shipped INIT tables reserve a fixed number of ids per column (300 for
    SKINIT and 1000 for ITINIT/EBINIT).  A populated record commonly writes its
    name and then its description, so that column stride is the dominant large
    positive delta between consecutive string destinations.
    """
    counts = {}
    for left, right in zip(string_addrs, string_addrs[1:]):
        delta = right - left
        if delta >= 32:
            counts[delta] = counts.get(delta, 0) + 1
    if not counts:
        raise ValueError("cannot infer name-table record span")
    return max(counts, key=lambda delta: (counts[delta], delta))


@cache
def _known_record_tables():
    """Return corpus-observed (base, stride) pairs used by lookup-array-2d.

    INIT scripts often populate linked row-major tables while defining an
    entity.  Treating every such write as `destination - entity_id` invents a
    different one-off parallel column for every row.  Consumer bytecode gives
    us the unambiguous table base and stride instead.
    """
    tables = set()
    for path in paths.scripts().values():
        try:
            script = sys4load.load(path)
        except sys4load.Sys4Error:
            continue
        for ins in script.instructions:
            if (sys4load.display_label(ins.opcode) == "lookup-array-2d"
                    and len(ins.args) >= 5
                    and ins.args[1][0] in (T_GLOBAL_INT, 6)
                    and ins.args[3][0] == T_IMM
                    and ins.args[3][1] > 0):
                tables.add((ins.args[1][1], ins.args[3][1]))
    return tuple(sorted(tables))


def _record_table_cell(destination, record_id):
    matches = []
    for base, stride in _known_record_tables():
        column = destination - (base + record_id * stride)
        if 0 <= column < stride:
            matches.append((base, stride, column))
    if len(matches) > 1:
        raise ValueError(
            f"ambiguous record-table destination 0x{destination:x} for id {record_id}: {matches}"
        )
    return matches[0] if matches else None


def extract_name(scr):
    string_addrs = [
        ins.args[0][1]
        for ins in scr.instructions
        if ins.opcode == SET_STRING and ins.args and ins.args[0][0] == T_GLOBAL_STRING
    ]
    if not string_addrs:
        return [], {}
    name_write_base = string_addrs[0]
    # AGE's shipped entity ids are one-based.  Array lookups use the cell just
    # before the first populated destination as their base, then add the id.
    first_record_id = 1
    name_base = name_write_base - first_record_id
    record_span = _infer_record_span(string_addrs)
    records, cur, desc_slot, desc_bases = [], None, 0, {}
    for ins in scr.instructions:
        if ins.opcode == SET_STRING and ins.args and ins.args[0][0] == T_GLOBAL_STRING:
            addr = ins.args[0][1]
            txt = scr.strings.get(ins.args[1][1], (None,))[0] if len(ins.args) > 1 else None
            # Names occupy column zero.  Do not use an address decrease as the
            # boundary: ITINIT begins with 101 consecutive name-only records,
            # which the old heuristic collapsed into item zero.
            if name_write_base <= addr < name_write_base + record_span:
                cur = {"id": addr - name_base, "name": txt, "fields": {}, "record_fields": {}}
                records.append(cur); desc_slot = 0
            elif cur is not None:
                key = "desc" if desc_slot == 0 else f"desc{desc_slot}"
                cur[key] = txt; desc_bases.setdefault(key, addr - cur["id"]); desc_slot += 1
        elif cur is not None and (write := _static_global_write(ins)) is not None:
            destination, value = write
            cell = _record_table_cell(destination, cur["id"])
            if cell is None:
                cur["fields"][f"0x{destination - cur['id']:x}"] = value
            else:
                base, stride, column = cell
                cur["record_fields"][f"0x{base:x}/{stride}/{column}"] = value
    for record in records:
        if not record["record_fields"]:
            del record["record_fields"]
    record_columns = sorted(
        {key for record in records for key in record.get("record_fields", {})},
        key=lambda key: tuple(int(part, 0) for part in key.split("/")),
    )
    return records, {"name_array_base": f"0x{name_base:x}",
                     "name_write_base": f"0x{name_write_base:x}",
                     "first_record_id": first_record_id,
                     "record_span": record_span,
                     "record_field_columns": record_columns,
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


def join_messages(records: list[dict], message_scr) -> dict:
    """Join a message-dispatch script to INIT records by runtime id."""
    messages, message_meta = extract_message_table.extract_messages(message_scr)
    by_id = {message["id"]: message for message in messages}
    joined = 0
    for record in records:
        if message := by_id.get(record["id"]):
            record["message"] = {
                key: value for key, value in message.items() if key != "id"
            }
            joined += 1
    init_ids = {record["id"] for record in records}
    message_ids = set(by_id)
    return {
        "source": message_scr.path.name,
        **message_meta,
        "joined_count": joined,
        "init_ids_without_message": sorted(init_ids - message_ids),
        "message_ids_without_init": sorted(message_ids - init_ids),
    }


@cache
def _global_registry() -> dict:
    path = paths.BUILD / "globals.json"
    try:
        return json.loads(path.read_text(encoding="utf8")).get("globals", {})
    except (OSError, json.JSONDecodeError):
        return {}


def field_semantics(
    records: list[dict], array_layouts: dict[str, dict] | None = None
) -> dict[str, str]:
    """Map raw extracted field keys to canonical semantic names when available."""
    keys = {
        key
        for record in records
        for key in (
            *record.get("string_fields", {}),
            *record.get("fields", {}),
            *record.get("array_fields", {}),
            *record.get("footer_arrays", {}),
            *record.get("record_fields", {}),
        )
    }
    registry = _global_registry()
    semantics = {}
    for key in sorted(keys, key=lambda value: tuple(
            int(part, 0) for part in value.split("/")
    )):
        parts = key.split("/")
        entry = registry.get(f"0x{int(parts[0], 16):x}", {})
        name = entry.get("name")
        if not name:
            continue
        if len(parts) == 2:
            index = int(parts[1])
            layout = (array_layouts or {}).get(f"0x{int(parts[0], 16):x}", {})
            if stride := layout.get("stride"):
                row, column = divmod(index, stride)
                column_name = entry.get("columns", {}).get(
                    str(column), f"column_{column}"
                )
                name = f"{name}.row_{row}.{column_name}"
            else:
                index_name = entry.get("columns", {}).get(str(index), f"index_{index}")
                name = f"{name}.{index_name}"
        elif len(parts) == 3:
            column = parts[2]
            column_name = entry.get("columns", {}).get(column, f"column_{column}")
            name = f"{name}.{column_name}"
        semantics[key] = name
    return semantics


def attach_semantic_fields(records: list[dict], semantics: dict[str, str]) -> None:
    """Add a generated name-keyed view while retaining raw address provenance."""
    containers = (
        "string_fields", "fields", "array_fields", "footer_arrays", "record_fields"
    )
    for record in records:
        semantic_fields = {}
        for container in containers:
            for key, value in record.get(container, {}).items():
                if semantic_name := semantics.get(key):
                    if semantic_name in semantic_fields:
                        raise ValueError(
                            f"record {record['id']}: duplicate semantic field {semantic_name}"
                        )
                    semantic_fields[semantic_name] = value
        if semantic_fields:
            record["semantic_fields"] = semantic_fields
        else:
            record.pop("semantic_fields", None)


def attach_stage_object_placements(records: list[dict]) -> None:
    """Assemble STINIT's parallel object buffers into modder-facing slot records."""
    known_fields = {
        "type_id": "0xe7389",
        "tile_x": "0xe7325",
        "tile_y": "0xe7357",
        "difficulty_mask": "0xe7483",
    }
    unknown_bases = ("0xe73bb", "0xe73ed", "0xe741f", "0xe7451")
    for record in records:
        fields = record.get("array_fields", {})
        objects = []
        for slot in range(1, 50):
            type_key = f"{known_fields['type_id']}/{slot}"
            if type_key not in fields:
                continue
            obj = {"slot": slot}
            for semantic_name, base in known_fields.items():
                if (key := f"{base}/{slot}") in fields:
                    obj[semantic_name] = fields[key]
            required = [
                fields[key]
                for column in range(7)
                if (key := f"0xe74b5/{slot * 7 + column}") in fields
                and fields[key] > 0
            ]
            forbidden = [
                fields[key]
                for column in range(5)
                if (key := f"0xe7613/{slot * 5 + column}") in fields
                and fields[key] > 0
            ]
            if required:
                obj["required_story_flags"] = required
            if forbidden:
                obj["forbidden_story_flags"] = forbidden
            unknown = {
                base: fields[f"{base}/{slot}"]
                for base in unknown_bases
                if f"{base}/{slot}" in fields
            }
            if unknown:
                obj["unknown_fields"] = unknown
            objects.append(obj)
        record["object_placements"] = objects


def write_data_index(data_dir: Path) -> None:
    """Regenerate the disposable build/data index from current table JSONs."""
    tables = []
    for path in sorted(data_dir.glob("*.json")):
        if path.name.endswith("-field-profile.json"):
            continue
        try:
            data = json.loads(path.read_text(encoding="utf8"))
        except (OSError, json.JSONDecodeError):
            continue
        if "table" in data and "record_count" in data:
            tables.append((path.name, data))
    lines = [
        "<!-- DO NOT EDIT -- generated by tools/extract_init.py -->",
        "# Parsed INIT data tables",
        "",
        "The JSON files in this directory are generated from SYS4 `*INIT` scripts. Raw global-array",
        "bases remain available in every record; confirmed field meanings live in",
        "`vm-map/globals.toml` and the generated `docs/global-reference.md`.",
        "",
        "| file | mode | records | messages | scalar/array fields | strings | buffer cells | footer arrays | record columns |",
        "|---|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for filename, data in tables:
        columns = len(data.get("field_columns") or [])
        record_columns = len(data.get("record_field_columns") or [])
        message_count = data.get("message_table", {}).get(
            "joined_count", data.get("message_count", 0)
        )
        lines.append(
            f"| `{filename}` | {data['mode']} | {data['record_count']} | "
            f"{message_count} | {columns} | {len(data.get('string_field_columns') or [])} | "
            f"{len(data.get('array_field_columns') or [])} | "
            f"{len(data.get('footer_array_columns') or [])} | {record_columns} |"
        )
    lines += [
        "",
        "Name-mode tables expose one-based runtime `id` values, the lookup `name_array_base`,",
        "the first populated `name_write_base`, and the reserved `record_span`. Fields are keyed",
        "by the runtime lookup base used by `lookup-array`, not merely the first written cell.",
        "Linked row-major fields are stored separately in `record_fields`, keyed as",
        "`base/stride/column` from corpus-observed `lookup-array-2d` consumers.",
        "Where a matching `*MES` dispatcher exists, `message` preserves its player-facing",
        "title, description, furigana, and bytecode dispatch offset separately from the",
        "short description stored by the INIT script.",
        "Top-level `field_semantics` maps raw array/row-column keys to canonical machine-readable",
        "names from `vm-map/globals.toml`; each record's generated `semantic_fields` is the joined",
        "name-keyed convenience view. Raw keys remain intact as bytecode provenance.",
        "",
        "Mixed-mode tables preserve the sparse selector id, branch offset, condition strings,",
        "scalar fields, cells within preallocated buffers, and length-prefixed footer arrays.",
        "STINIT additionally joins the confirmed parallel object buffers into per-slot",
        "`object_placements`; unresolved type-specific parameters remain in `unknown_fields`.",
        "Use `tools/init_table_profile.py <TABLE> --build` to generate value/population and",
        "direct-consumer evidence.",
        "",
    ]
    (data_dir / "README.md").write_text("\n".join(lines), encoding="utf8")


def main() -> int:
    argv = []
    mode_arg = None
    index = 1
    while index < len(sys.argv):
        arg = sys.argv[index]
        if arg == "--mode":
            if index + 1 >= len(sys.argv):
                raise SystemExit("--mode requires a value")
            mode_arg = sys.argv[index + 1]
            index += 2
            continue
        if arg.startswith("--"):
            raise SystemExit(f"unknown option: {arg}")
        argv.append(arg)
        index += 1
    if not argv:
        raise SystemExit(__doc__)
    name = argv[0].upper().removesuffix(".BIN")
    try:
        outname = normalize_outname(argv[1]) if len(argv) > 1 else name
    except ValueError as error:
        raise SystemExit(str(error)) from error
    scr = sys4load.load(resolve(name))

    mode = mode_arg or detect_mode(scr)
    extractor = {
        "name": extract_name,
        "numeric": extract_numeric,
        "footer": extract_footer,
        "mixed": extract_mixed,
    }[mode]
    recs, meta = extractor(scr)
    if mode == "name" and name in MESSAGE_TABLES:
        message_name = MESSAGE_TABLES[name]
        meta["message_table"] = join_messages(
            recs, sys4load.load(extract_message_table.resolve(message_name))
        )

    cols = sorted({c for r in recs for c in r.get("fields", {})}, key=lambda h: int(h, 16))
    semantics = field_semantics(recs, meta.get("array_layouts"))
    attach_semantic_fields(recs, semantics)
    if mode == "mixed" and name == "STINIT":
        attach_stage_object_placements(recs)
    out = {"table": name, "source": scr.path.name, "magic": scr.magic, "mode": mode,
           "record_count": len(recs), **meta,
           "field_columns": cols if mode != "footer" else None,
           "field_semantics": semantics, "records": recs}
    outpath = paths.BUILD / "data" / f"{outname}.json"
    outpath.parent.mkdir(parents=True, exist_ok=True)
    outpath.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
    write_data_index(outpath.parent)
    print(f"{name}: mode={mode}, {len(recs)} records"
          + (f", {len(cols)} field-columns" if mode != 'footer' else "")
          + f" -> build/data/{outname}.json")
    for r in recs[:4]:
        if mode == "footer":
            print(f"  id {r['id']:>4}  {r['global_addr']} <- footer {r['footer_off']} "
                  f"len {r['length']}  head={r['values'][:8]}")
        else:
            fields = r.get("fields", {})
            f4 = {k: fields[k] for k in list(fields)[:4]}
            print(f"  id {r['id']:>4}  {r.get('name','')!r:12} desc={r.get('desc','')!r} {f4}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
