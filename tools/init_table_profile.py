#!/usr/bin/env python3
"""Profile extracted INIT columns and find their direct script consumers.

The extractor tells us which global-array bases are populated with each named
record.  This tool adds the next layer of evidence: population/value shape and
every corpus instruction that refers to the array base directly.  The output is
an investigation surface, not a semantic source of truth; confirmed field names
belong in vm-map/globals.toml.

Usage:
  py -3.11 -X utf8 tools/init_table_profile.py ITINIT
  py -3.11 -X utf8 tools/init_table_profile.py ITINIT --build
  py -3.11 -X utf8 tools/init_table_profile.py ITINIT --limit 80

With --build, writes build/data/<TABLE>-field-profile.{json,md}.
"""
from __future__ import annotations

import argparse
import collections
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths
import sys4load


GLOBAL_OPERAND_TYPES = {3, 4, 5, 6, 8}


def load_table(name: str) -> dict:
    path = paths.BUILD / "data" / f"{name}.json"
    if not path.exists():
        raise SystemExit(f"missing extracted table: {path}")
    data = json.loads(path.read_text(encoding="utf8"))
    if data.get("mode") not in {"name", "numeric"}:
        raise SystemExit(f"{name}: field profiling requires name/numeric mode")
    return data


def value_key(value) -> str:
    if isinstance(value, dict):
        return json.dumps(value, ensure_ascii=False, sort_keys=True)
    return str(value)


def profile_columns(data: dict) -> list[dict]:
    records = data["records"]
    values: dict[str, list] = collections.defaultdict(list)
    examples: dict[str, list[dict]] = collections.defaultdict(list)
    identities: dict[str, dict] = {}
    for record in records:
        for address, value in record.get("fields", {}).items():
            base = int(address, 16)
            key = f"0x{base:x}"
            identities[key] = {
                "key": key, "kind": "parallel-array", "base": key,
                "stride": None, "column": None,
            }
            values[key].append(value)
            if len(examples[key]) < 5:
                examples[key].append({
                    "id": record["id"],
                    "name": record.get("name", ""),
                    "value": value,
                })
        for key, value in record.get("record_fields", {}).items():
            base_text, stride_text, column_text = key.split("/")
            base = int(base_text, 16)
            stride = int(stride_text)
            column = int(column_text)
            normalized_key = f"0x{base:x}/{stride}/{column}"
            identities[normalized_key] = {
                "key": normalized_key,
                "kind": "record-column",
                "base": f"0x{base:x}",
                "stride": stride,
                "column": column,
            }
            values[normalized_key].append(value)
            if len(examples[normalized_key]) < 5:
                examples[normalized_key].append({
                    "id": record["id"],
                    "name": record.get("name", ""),
                    "value": value,
                })

    rows = []
    for key, vals in values.items():
        common = collections.Counter(value_key(value) for value in vals).most_common(6)
        numeric = vals and all(isinstance(value, int) for value in vals)
        rows.append({
            **identities[key],
            "population": len(vals),
            "coverage": len(vals) / len(records) if records else 0.0,
            "distinct_values": len({value_key(value) for value in vals}),
            "min": min(vals) if numeric else None,
            "max": max(vals) if numeric else None,
            "common": [{"value": value, "count": count} for value, count in common],
            "examples": examples[key],
            "references": 0,
            "reader_scripts": [],
            "reference_ops": [],
        })
    return rows


def add_direct_references(rows: list[dict], source_name: str) -> None:
    by_base: dict[int, list[dict]] = collections.defaultdict(list)
    for row in rows:
        by_base[int(row["base"], 16)].append(row)
    scripts: dict[int, collections.Counter] = {
        base: collections.Counter() for base in by_base
    }
    ops: dict[int, collections.Counter] = {
        base: collections.Counter() for base in by_base
    }
    for name, path in paths.scripts().items():
        if name.upper() == source_name.upper():
            continue
        try:
            script = sys4load.load(path)
        except sys4load.Sys4Error:
            continue
        for ins in script.instructions:
            for arg_index, (arg_type, value) in enumerate(ins.args):
                if arg_type not in GLOBAL_OPERAND_TYPES or value not in by_base:
                    continue
                scripts[value][name] += 1
                ops[value][f"{sys4load.display_label(ins.opcode)}:arg{arg_index + 1}"] += 1

    for base, base_rows in by_base.items():
        for row in base_rows:
            row["references"] = sum(scripts[base].values())
            row["reader_scripts"] = [
                {"script": script, "count": count}
                for script, count in scripts[base].most_common()
            ]
            row["reference_ops"] = [
                {"operation": operation, "count": count}
                for operation, count in ops[base].most_common()
            ]


def render_markdown(data: dict, rows: list[dict], limit: int) -> str:
    ranked = sorted(rows, key=lambda row: (-row["population"], -row["references"], row["key"]))
    shown = ranked[:limit]
    lines = [
        f"# {data['table']} field profile",
        "",
        "> Generated by `tools/init_table_profile.py` — do not hand-edit.",
        "> This is evidence for investigation; confirmed names live in `vm-map/globals.toml`.",
        "",
        f"- records: {data['record_count']}",
        f"- populated fields: {len(rows)}",
        f"- rows shown: {len(shown)} (ranked by record coverage, then consumer references)",
        "",
        "| field | populated | distinct | range | direct refs | readers | common values | examples |",
        "|---|---:|---:|---|---:|---|---|---|",
    ]
    for row in shown:
        value_range = "—" if row["min"] is None else f"{row['min']}..{row['max']}"
        readers = ", ".join(entry["script"].removesuffix(".BIN")
                            for entry in row["reader_scripts"][:5]) or "—"
        common = ", ".join(f"{entry['value']}×{entry['count']}" for entry in row["common"][:4])
        examples = ", ".join(
            f"{entry['id']}:{entry['name']}={value_key(entry['value'])}"
            for entry in row["examples"][:3]
        ).replace("|", "\\|")
        lines.append(
            f"| `{row['key']}` | {row['population']}/{data['record_count']} "
            f"({row['coverage']:.0%}) | {row['distinct_values']} | {value_range} | "
            f"{row['references']} | {readers} | {common} | {examples} |"
        )
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("table", help="extracted table name, e.g. ITINIT")
    parser.add_argument("--build", action="store_true", help="write JSON and Markdown profiles")
    parser.add_argument("--limit", type=int, default=40, help="Markdown/console row limit")
    args = parser.parse_args()

    name = args.table.upper().removesuffix(".JSON").removesuffix(".BIN")
    data = load_table(name)
    rows = profile_columns(data)
    add_direct_references(rows, data["source"])
    output = {
        "table": data["table"],
        "source": data["source"],
        "record_count": data["record_count"],
        "field_column_count": len(rows),
        "parallel_array_count": sum(row["kind"] == "parallel-array" for row in rows),
        "record_column_count": sum(row["kind"] == "record-column" for row in rows),
        "columns": sorted(rows, key=lambda row: (
            int(row["base"], 16), row["stride"] or 0, row["column"] or 0
        )),
    }
    markdown = render_markdown(data, rows, args.limit)
    print(markdown)
    if args.build:
        stem = paths.BUILD / "data" / f"{name}-field-profile"
        stem.with_suffix(".json").write_text(
            json.dumps(output, ensure_ascii=False, indent=2), encoding="utf8"
        )
        stem.with_suffix(".md").write_text(markdown, encoding="utf8")
        print(f"wrote {stem.relative_to(paths.REPO)}.json/.md")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
