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
import re
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
    if data.get("mode") not in {
        "name", "numeric", "mixed", "rules", "dispatch", "banked"
    }:
        raise SystemExit(f"{name}: unsupported field-profiling mode {data.get('mode')!r}")
    return data


def value_key(value) -> str:
    if isinstance(value, dict):
        return json.dumps(value, ensure_ascii=False, sort_keys=True)
    return str(value)


def profile_columns(data: dict) -> list[dict]:
    records = data["records"]
    field_semantics = data.get("field_semantics", {})
    values: dict[str, list] = collections.defaultdict(list)
    examples: dict[str, list[dict]] = collections.defaultdict(list)
    identities: dict[str, dict] = {}

    def record_name(record: dict) -> str:
        if record.get("name"):
            return record["name"]
        return next(
            (text for text in record.get("string_fields", {}).values() if text),
            f"record {record['id']}",
        )

    def add(key: str, identity: dict, value, record: dict, **extra) -> None:
        identities[key] = identity
        values[key].append(value)
        if len(examples[key]) < 5:
            example = {
                "id": record["id"],
                "name": record_name(record),
                "value": value,
                **extra,
            }
            if message := record.get("message"):
                example["message_description"] = message.get("description", "")
            examples[key].append(example)

    for record in records:
        for address, value in record.get("fields", {}).items():
            base = int(address, 16)
            key = f"0x{base:x}"
            add(key, {
                "key": key,
                "kind": (
                    "scalar-field" if data.get("mode") == "mixed"
                    else "rule-output" if data.get("mode") == "rules"
                    else "dispatch-field" if data.get("mode") == "dispatch"
                    else "parallel-array"
                ),
                "base": key,
                "stride": None, "column": None,
                "semantic_name": field_semantics.get(key),
            }, value, record)
        for address, value in record.get("string_fields", {}).items():
            base = int(address, 16)
            key = f"0x{base:x}"
            add(key, {
                "key": key, "kind": "string-field", "base": key,
                "stride": None, "column": None,
                "semantic_name": field_semantics.get(key),
            }, value, record)
        for key, value in record.get("record_fields", {}).items():
            base_text, stride_text, column_text = key.split("/")
            base = int(base_text, 16)
            stride = int(stride_text)
            column = int(column_text)
            normalized_key = f"0x{base:x}/{stride}/{column}"
            add(normalized_key, {
                "key": normalized_key,
                "kind": "record-column",
                "base": f"0x{base:x}",
                "stride": stride,
                "column": column,
                "semantic_name": field_semantics.get(normalized_key),
            }, value, record)
        for kind, field_name in (
            ("array-cell", "array_fields"),
            ("footer-array", "footer_arrays"),
        ):
            for key, raw_value in record.get(field_name, {}).items():
                parts = key.split("/")
                base = int(parts[0], 16)
                index = int(parts[1]) if len(parts) == 2 else None
                layout = data.get("array_layouts", {}).get(f"0x{base:x}", {})
                stride = layout.get("stride")
                column = index % stride if index is not None and stride else None
                value = raw_value.get("values", []) if kind == "footer-array" else raw_value
                extra = (
                    {"footer_off": raw_value.get("footer_off")}
                    if kind == "footer-array" else {}
                )
                add(key, {
                    "key": key,
                    "kind": kind,
                    "base": f"0x{base:x}",
                    "index": index,
                    "stride": stride,
                    "column": column,
                    "semantic_name": field_semantics.get(key),
                }, value, record, **extra)

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


def profile_rules(data: dict) -> dict:
    """Summarize predicates and joined effects for conditional rule programs."""
    if data.get("mode") != "rules":
        return {}
    records = data["records"]
    return {
        "unit_count": len({record["unit_id"] for record in records}),
        "titled_rule_count": sum(bool(record.get("title")) for record in records),
        "level_independent_rule_count": sum(
            "minimum_level" not in record for record in records
        ),
        "minimum_levels": dict(sorted(collections.Counter(
            str(record["minimum_level"])
            for record in records if "minimum_level" in record
        ).items(), key=lambda item: int(item[0]))),
        "class_change_slot_indices": dict(sorted(collections.Counter(
            str(record["class_change_slot_index"])
            for record in records if "class_change_slot_index" in record
        ).items(), key=lambda item: int(item[0]))),
        "skill_award_count": sum(
            len(record.get("skill_awards", [])) for record in records
        ),
    }


def profile_dispatch(data: dict) -> dict:
    """Summarize SCINIT's final registry and preserved assignment history."""
    if data.get("mode") != "dispatch":
        return {}
    return {
        "assignment_count": data.get("assignment_count", 0),
        "overwritten_record_count": data.get("overwritten_record_count", 0),
        "conflicting_chapter_record_count": data.get(
            "conflicting_chapter_record_count", 0
        ),
        "resolved_script_count": data.get("resolved_script_count", 0),
        "scjump_joined_record_count": data.get("scjump_joined_record_count", 0),
        "scjump_chapter_match_count": data.get("scjump_chapter_match_count", 0),
        "scjump_chapter_mismatch_count": len(
            data.get("scjump_chapter_mismatches", [])
        ),
    }


def profile_banked(data: dict) -> dict:
    """Summarize RTINIT's routine banks, steps, and overwrite history."""
    if data.get("mode") != "banked":
        return {}
    layouts = data.get("bank_layouts", {})
    return {
        "assignment_count": data.get("assignment_count", 0),
        "populated_cell_count": data.get("populated_cell_count", 0),
        "overwritten_cell_count": data.get("overwritten_cell_count", 0),
        "conflicting_overwrite_count": data.get("conflicting_overwrite_count", 0),
        "populated_bank_count": sum(
            not layout.get("reserved_empty", False)
            for layout in layouts.values()
        ),
        "reserved_bank_count": sum(
            layout.get("reserved_empty", False)
            for layout in layouts.values()
        ),
        "movement_step_count": data.get("movement_step_count", 0),
        "battle_step_count": data.get("battle_step_count", 0),
        "movement_provider_count": len(
            data.get("used_movement_provider_selectors", [])
        ),
        "battle_provider_count": len(
            data.get("used_battle_provider_selectors", [])
        ),
        "available_movement_provider_count": len(
            data.get("movement_provider_scripts", {})
        ),
        "available_battle_provider_count": len(
            data.get("battle_provider_scripts", {})
        ),
    }


def profile_messages(data: dict) -> dict:
    """Summarize the joined player-facing message evidence."""
    records = data["records"]
    with_message = [record for record in records if "message" in record]
    with_furigana = [
        record for record in with_message if record["message"].get("furigana")
    ]
    return {
        "population": len(with_message),
        "coverage": len(with_message) / len(records) if records else 0.0,
        "furigana_records": len(with_furigana),
        "examples": [
            {
                "id": record["id"],
                "name": record.get("name", ""),
                "title": record["message"]["title"],
                "description": record["message"]["description"],
            }
            for record in with_message[:5]
        ],
    }


def find_message_matches(data: dict, pattern: str) -> list[dict]:
    """Return records whose name/title/description matches a regular expression."""
    regex = re.compile(pattern, re.IGNORECASE)
    return [
        record
        for record in data["records"]
        if regex.search("\n".join([
            record.get("name", ""),
            record.get("message", {}).get("title", ""),
            record.get("message", {}).get("description", ""),
        ]))
    ]


def render_message_matches(data: dict, pattern: str) -> str:
    """Render message hits beside every populated INIT field for correlation."""
    matches = find_message_matches(data, pattern)
    escaped_pattern = pattern.replace("`", "\\`")
    lines = [
        f"# {data['table']} message matches",
        "",
        f"- query: `{escaped_pattern}`",
        f"- matches: {len(matches)}",
        "",
        "| id | name | player-facing description | populated fields |",
        "|---:|---|---|---|",
    ]
    for record in matches:
        fields = {**record.get("fields", {}), **record.get("record_fields", {})}
        rendered_fields = ", ".join(
            f"`{data.get('field_semantics', {}).get(key, key)}` (`{key}`)={value}"
            for key, value in sorted(fields.items())
        )
        name = record.get("name", "").replace("|", "\\|")
        description = record.get("message", {}).get("description", "").replace("|", "\\|")
        lines.append(
            f"| {record['id']} | {name} | {description} | {rendered_fields} |"
        )
    lines.append("")
    return "\n".join(lines)


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
    message_profile = profile_messages(data)
    lines = [
        f"# {data['table']} field profile",
        "",
        "> Generated by `tools/init_table_profile.py` — do not hand-edit.",
        "> This is evidence for investigation; confirmed names live in `vm-map/globals.toml`.",
        "",
        f"- records: {data['record_count']}",
        f"- populated fields: {len(rows)}",
    ]
    if rule_profile := profile_rules(data):
        lines.extend([
            f"- covered units: {rule_profile['unit_count']}",
            f"- titled rules: {rule_profile['titled_rule_count']}/{data['record_count']}",
            f"- level-independent rules: {rule_profile['level_independent_rule_count']}",
            f"- awarded skills: {rule_profile['skill_award_count']}",
        ])
    elif dispatch_profile := profile_dispatch(data):
        lines.extend([
            f"- source assignments: {dispatch_profile['assignment_count']}",
            f"- overwritten decision ids: {dispatch_profile['overwritten_record_count']}",
            f"- ids whose assignment history crosses chapter tags: "
            f"{dispatch_profile['conflicting_chapter_record_count']}",
            f"- packed script ids resolved: {dispatch_profile['resolved_script_count']}/"
            f"{data['record_count']}",
            f"- SCJUMP decisions joined: {dispatch_profile['scjump_joined_record_count']}",
            f"- final authored chapters matching SCJUMP: "
            f"{dispatch_profile['scjump_chapter_match_count']}/"
            f"{dispatch_profile['scjump_joined_record_count']}",
            f"- explicit chapter mismatches: "
            f"{dispatch_profile['scjump_chapter_mismatch_count']}",
        ])
    elif banked_profile := profile_banked(data):
        lines.extend([
            f"- source assignments: {banked_profile['assignment_count']}",
            f"- final populated cells: {banked_profile['populated_cell_count']}",
            f"- overwritten cells: {banked_profile['overwritten_cell_count']} "
            f"({banked_profile['conflicting_overwrite_count']} change value)",
            f"- banks: {banked_profile['populated_bank_count']} populated, "
            f"{banked_profile['reserved_bank_count']} reserved/empty",
            f"- joined movement steps/used providers: "
            f"{banked_profile['movement_step_count']}/"
            f"{banked_profile['movement_provider_count']} "
            f"({banked_profile['available_movement_provider_count']} dispatchable)",
            f"- joined battle steps/used providers: "
            f"{banked_profile['battle_step_count']}/"
            f"{banked_profile['battle_provider_count']} "
            f"({banked_profile['available_battle_provider_count']} dispatchable)",
        ])
    else:
        lines.extend([
            f"- player-facing messages: {message_profile['population']}/{data['record_count']} "
            f"({message_profile['coverage']:.0%})",
            f"- messages with furigana spans: {message_profile['furigana_records']}",
        ])
    lines.extend([
        f"- rows shown: {len(shown)} (ranked by record coverage, then consumer references)",
        "",
        "| field | meaning | populated | distinct | range | direct refs | readers | common values | examples |",
        "|---|---|---:|---:|---|---:|---|---|---|",
    ])
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
            f"| `{row['key']}` | {row.get('semantic_name') or '—'} | "
            f"{row['population']}/{data['record_count']} "
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
    parser.add_argument(
        "--message-query",
        metavar="REGEX",
        help="show matching names/player-facing messages beside all populated fields",
    )
    args = parser.parse_args()

    name = args.table.upper().removesuffix(".JSON").removesuffix(".BIN")
    data = load_table(name)
    rows = profile_columns(data)
    messages = profile_messages(data)
    add_direct_references(rows, data["source"])
    output = {
        "table": data["table"],
        "source": data["source"],
        "record_count": data["record_count"],
        "field_column_count": len(rows),
        "parallel_array_count": sum(row["kind"] == "parallel-array" for row in rows),
        "record_column_count": sum(row["kind"] == "record-column" for row in rows),
        "scalar_field_count": sum(row["kind"] == "scalar-field" for row in rows),
        "string_field_count": sum(row["kind"] == "string-field" for row in rows),
        "array_cell_count": sum(row["kind"] == "array-cell" for row in rows),
        "footer_array_count": sum(row["kind"] == "footer-array" for row in rows),
        "rule_output_count": sum(row["kind"] == "rule-output" for row in rows),
        "dispatch_field_count": sum(row["kind"] == "dispatch-field" for row in rows),
        "message_profile": messages,
        "rule_profile": profile_rules(data),
        "dispatch_profile": profile_dispatch(data),
        "banked_profile": profile_banked(data),
        "columns": sorted(rows, key=lambda row: (
            int(row["base"], 16), row["stride"] or 0, row["column"] or 0
        )),
    }
    markdown = render_markdown(data, rows, args.limit)
    if args.message_query:
        print(render_message_matches(data, args.message_query))
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
