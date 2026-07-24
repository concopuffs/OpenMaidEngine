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


def message_heading_body(message: dict) -> tuple[str, str]:
    """Return presentation-neutral heading/body text for supported MES layouts."""
    return (
        message.get("title", message.get("summary", "")),
        message.get(
            "description", message.get("strategy", message.get("biography", ""))
        ),
    )


def load_table(name: str) -> dict:
    path = paths.BUILD / "data" / f"{name}.json"
    if not path.exists():
        raise SystemExit(f"missing extracted table: {path}")
    data = json.loads(path.read_text(encoding="utf8"))
    if data.get("mode") not in {
        "name", "numeric", "footer", "mixed", "rules", "dispatch", "banked"
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
                _, body = message_heading_body(message)
                example["message_description"] = body
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
            parts = address.split("/")
            base = int(parts[0], 16)
            if len(parts) == 1:
                stride = column = None
                key = f"0x{base:x}"
                kind = "string-field"
            elif len(parts) == 3:
                stride = int(parts[1])
                column = int(parts[2])
                key = f"0x{base:x}/{stride}/{column}"
                kind = "string-record-column"
            else:
                raise ValueError(f"bad string-field key: {address}")
            add(key, {
                "key": key, "kind": kind, "base": f"0x{base:x}",
                "stride": stride, "column": column,
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
        "decoded_movement_provider_count": data.get(
            "decoded_movement_provider_count", 0
        ),
        "decoded_movement_step_count": data.get(
            "decoded_movement_step_count", 0
        ),
        "decoded_movement_parameter_count": data.get(
            "decoded_movement_parameter_count", 0
        ),
        "decoded_movement_defaulted_parameter_count": data.get(
            "decoded_movement_defaulted_parameter_count", 0
        ),
        "ignored_movement_parameter_count": data.get(
            "ignored_movement_parameter_count", 0
        ),
        "available_battle_provider_count": len(
            data.get("battle_provider_scripts", {})
        ),
    }


def profile_map_atlas(data: dict) -> dict:
    """Summarize MPINIT's sparse terrain rows and STINIT2 rectangle join."""
    if data.get("schema") != "stage-terrain-atlas":
        return {}
    shared = data.get("shared_atlas_rectangles", [])
    return {
        "row_stride": data.get("row_stride", 0),
        "authored_column_count": data.get("authored_column_count", 0),
        "tile_to_grid_scale": data.get("tile_to_grid_scale", 0),
        "authored_row_count": data.get("authored_row_count", 0),
        "implicit_zero_row_count": data.get("implicit_zero_row_count", 0),
        "authored_grid_y_min": data.get("authored_grid_y_min", 0),
        "authored_grid_y_max": data.get("authored_grid_y_max", 0),
        "nonzero_cell_count": data.get("nonzero_cell_count", 0),
        "stage_rectangle_nonzero_cell_count": data.get(
            "stage_rectangle_nonzero_cell_count", 0
        ),
        "outside_stage_rectangle_nonzero_cell_count": data.get(
            "outside_stage_rectangle_nonzero_cell_count", 0
        ),
        "terrain_ids_used": data.get("terrain_ids_used", []),
        "stage_map_count": data.get("stage_map_count", 0),
        "unique_atlas_rectangle_count": data.get(
            "unique_atlas_rectangle_count", 0
        ),
        "shared_rectangle_count": len(shared),
        "shared_stage_definition_count": sum(
            len(row.get("stage_ids", [])) for row in shared
        ),
    }


def profile_terrain_definitions(data: dict) -> dict:
    """Summarize LAINIT's terrain rows and shared texture-slot assets."""
    if data.get("schema") != "terrain-definitions":
        return {}
    records = data.get("records", [])
    return {
        "reserved_record_span": data.get("reserved_record_span", 0),
        "shipped_record_count": len(records),
        "authored_record_count": len(data.get("authored_terrain_ids", [])),
        "implicit_default_record_count": len(
            data.get("implicit_default_terrain_ids", [])
        ),
        "named_record_count": sum(
            bool(record.get("name")) for record in records
        ),
        "effect_description_count": sum(
            bool(record.get("effect_description")) for record in records
        ),
        "combat_stat_cell_count": data.get("combat_stat_cell_count", 0),
        "required_skill_count": data.get("required_skill_count", 0),
        "required_skill_names": sorted({
            record["required_skill_name"]
            for record in records
            if record.get("required_skill_name")
        }),
        "map_texture_slot_count": data.get("map_texture_slot_count", 0),
        "default_texture_asset_count": data.get(
            "default_texture_asset_count", 0
        ),
        "default_texture_asset_join_count": data.get(
            "default_texture_asset_join_count", 0
        ),
    }


def profile_h_scene_gallery(data: dict) -> dict:
    """Summarize SPINIT's HMODE page/slot script registry."""
    if data.get("schema") != "h-scene-gallery-pages":
        return {}
    return {
        "page_count": data.get("page_count", 0),
        "slots_per_page": data.get("slots_per_page", 0),
        "registry_capacity": data.get("registry_capacity", 0),
        "populated_scene_count": data.get("populated_scene_count", 0),
        "empty_cells": data.get("empty_cells", []),
        "resolved_scene_script_count": data.get(
            "resolved_scene_script_count", 0
        ),
        "resolved_thumbnail_sheet_count": data.get(
            "resolved_thumbnail_sheet_count", 0
        ),
    }


def profile_training_actions(data: dict) -> dict:
    """Summarize TRINIT's training-action gates, effects, and events."""
    if data.get("schema") != "training-action-definitions":
        return {}
    records = data.get("records", [])
    return {
        "action_count": len(records),
        "string_line_count": data.get("string_write_count", 0),
        "description_line_count": sum(
            len(record.get("description_lines", []))
            for record in records
        ),
        "locked_hint_line_count": sum(
            len(record.get("locked_hint_lines", []))
            for record in records
        ),
        "required_story_flag_cell_count": data.get(
            "authored_numeric_cell_counts", {}
        ).get("required_story_flag_ids", 0),
        "required_item_count": sum(
            "required_item_id" in record.get("eligibility", {})
            for record in records
        ),
        "minimum_alignment_gate_count": sum(
            "minimum_alignment" in record.get("eligibility", {})
            for record in records
        ),
        "maximum_alignment_gate_count": sum(
            "maximum_alignment" in record.get("eligibility", {})
            for record in records
        ),
        "minimum_training_gate_count": sum(
            "minimum_training_progress"
            in record.get("eligibility", {})
            for record in records
        ),
        "stat_delta_cell_count": data.get(
            "authored_numeric_cell_counts", {}
        ).get("unit_stat_deltas", 0),
        "awarded_item_count": sum(
            "awarded_item_id" in record.get("effects", {})
            for record in records
        ),
        "awarded_skill_count": sum(
            "awarded_skill_id" in record.get("effects", {})
            for record in records
        ),
        "event_cell_count": data.get("event_cell_count", 0),
        "distinct_event_count": len(
            data.get("distinct_event_story_flag_ids", [])
        ),
        "resolved_event_dispatch_count": data.get(
            "resolved_event_dispatch_count", 0
        ),
        "execution_limits": dict(sorted(collections.Counter(
            str(record.get("execution_limit", 0))
            for record in records
        ).items(), key=lambda item: int(item[0]))),
    }


def profile_card_generation_lists(data: dict) -> dict:
    """Summarize CDINIT's weighted, selector-dispatched card lists."""
    if data.get("schema") != "card-generation-lists":
        return {}
    entries = [
        entry
        for record in data.get("records", [])
        for entry in record.get("entries", [])
    ]
    return {
        "list_count": len(data.get("records", [])),
        "entry_count": data.get("entry_count", 0),
        "distinct_card_count": len(data.get("distinct_card_ids", [])),
        "resolved_card_reference_count": data.get(
            "resolved_card_reference_count", 0
        ),
        "used_selector_count": len(data.get("used_selector_ids", [])),
        "unreferenced_selector_ids": data.get(
            "unreferenced_selector_ids", []
        ),
        "stage_definition_reference_count": data.get(
            "stage_definition_reference_count", 0
        ),
        "stage_object_reference_count": data.get(
            "stage_object_reference_count", 0
        ),
        "story_flag_gated_entry_count": sum(
            bool(entry.get("required_story_flag_ids"))
            or bool(entry.get("forbidden_story_flag_ids"))
            for entry in entries
        ),
        "ignored_required_story_flag_entry_count": data.get(
            "ignored_required_story_flag_entry_count", 0
        ),
        "runtime_scan_capacity": data.get("runtime_scan_capacity", 0),
        "cleared_entry_prefix": data.get("cleared_entry_prefix", 0),
        "list_entry_counts": {
            str(record["id"]): record.get("entry_count", 0)
            for record in data.get("records", [])
        },
    }


def profile_messages(data: dict) -> dict:
    """Summarize the joined player-facing message evidence."""
    records = data["records"]
    with_message = [record for record in records if "message" in record]
    with_furigana = [
        record for record in with_message if record["message"].get("furigana")
    ]
    examples = []
    for record in with_message[:5]:
        heading, body = message_heading_body(record["message"])
        examples.append({
            "id": record["id"],
            "name": record.get("name", ""),
            "title": heading,
            "description": body,
            "message_fields": {
                key: record["message"][key]
                for key in (
                    "title", "description", "summary", "strategy", "biography"
                )
                if key in record["message"]
            },
        })
    return {
        "population": len(with_message),
        "coverage": len(with_message) / len(records) if records else 0.0,
        "furigana_records": len(with_furigana),
        "examples": examples,
    }


def find_message_matches(data: dict, pattern: str) -> list[dict]:
    """Return records whose name or supported message text matches a regex."""
    regex = re.compile(pattern, re.IGNORECASE)
    return [
        record
        for record in data["records"]
        if regex.search("\n".join([
            record.get("name", ""),
            record.get("message", {}).get("title", ""),
            record.get("message", {}).get("description", ""),
            record.get("message", {}).get("summary", ""),
            record.get("message", {}).get("strategy", ""),
            record.get("message", {}).get("biography", ""),
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
        _, body = message_heading_body(record.get("message", {}))
        description = body.replace("|", "\\|")
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
    if card_profile := profile_card_generation_lists(data):
        lines.extend([
            f"- card-generation lists: {card_profile['list_count']}",
            f"- weighted entries: {card_profile['entry_count']} across "
            f"{card_profile['distinct_card_count']} distinct cards "
            f"({card_profile['resolved_card_reference_count']} CDINIT2 "
            f"references resolved)",
            f"- selector usage: {card_profile['used_selector_count']} used; "
            f"unreferenced {card_profile['unreferenced_selector_ids']}",
            f"- STINIT joins: "
            f"{card_profile['stage_object_reference_count']} type-28 objects "
            f"across {card_profile['stage_definition_reference_count']} "
            f"stage definitions",
            f"- story-flag-gated entries: "
            f"{card_profile['story_flag_gated_entry_count']} effective; "
            f"{card_profile['ignored_required_story_flag_entry_count']} "
            f"carry an engine-dead third required flag",
            f"- runtime scan/clear prefix: "
            f"{card_profile['runtime_scan_capacity']}/"
            f"{card_profile['cleared_entry_prefix']} slots",
            f"- entries by selector: {card_profile['list_entry_counts']}",
        ])
    elif training_profile := profile_training_actions(data):
        lines.extend([
            f"- training actions: {training_profile['action_count']}",
            f"- display text lines: "
            f"{training_profile['description_line_count']} available + "
            f"{training_profile['locked_hint_line_count']} locked",
            f"- eligibility cells: "
            f"{training_profile['required_story_flag_cell_count']} required "
            f"story flags, {training_profile['required_item_count']} items, "
            f"{training_profile['minimum_alignment_gate_count']} minimum + "
            f"{training_profile['maximum_alignment_gate_count']} maximum "
            f"alignment gates, "
            f"{training_profile['minimum_training_gate_count']} training gates",
            f"- effect cells: {training_profile['stat_delta_cell_count']} stat "
            f"deltas, {training_profile['awarded_skill_count']} skill awards, "
            f"{training_profile['awarded_item_count']} item awards",
            f"- event slots: {training_profile['event_cell_count']} across "
            f"{training_profile['distinct_event_count']} distinct story flags "
            f"({training_profile['resolved_event_dispatch_count']} dispatches "
            f"resolved)",
            f"- execution limits: {training_profile['execution_limits']}",
        ])
    elif h_gallery_profile := profile_h_scene_gallery(data):
        lines.extend([
            f"- geometry: {h_gallery_profile['page_count']} pages × "
            f"{h_gallery_profile['slots_per_page']} slots",
            f"- populated scenes: "
            f"{h_gallery_profile['populated_scene_count']}/"
            f"{h_gallery_profile['registry_capacity']}",
            f"- resolved scripts: "
            f"{h_gallery_profile['resolved_scene_script_count']}/"
            f"{h_gallery_profile['populated_scene_count']}",
            f"- resolved thumbnail sheets: "
            f"{h_gallery_profile['resolved_thumbnail_sheet_count']}/"
            f"{h_gallery_profile['page_count']}",
            f"- empty cells: {h_gallery_profile['empty_cells']}",
        ])
    elif terrain_profile := profile_terrain_definitions(data):
        lines.extend([
            f"- shipped terrain ids: "
            f"{terrain_profile['shipped_record_count']} inside a "
            f"{terrain_profile['reserved_record_span']}-row table",
            f"- authored/default rows: "
            f"{terrain_profile['authored_record_count']}/"
            f"{terrain_profile['implicit_default_record_count']}",
            f"- named/effect rows: {terrain_profile['named_record_count']}/"
            f"{terrain_profile['effect_description_count']}",
            f"- combat-stat cells: "
            f"{terrain_profile['combat_stat_cell_count']}",
            f"- required traversal skills: "
            f"{terrain_profile['required_skill_count']} "
            f"{terrain_profile['required_skill_names']}",
            f"- texture fallbacks: "
            f"{terrain_profile['default_texture_asset_join_count']}/"
            f"{terrain_profile['default_texture_asset_count']} assets resolved "
            f"across {terrain_profile['map_texture_slot_count']} slots",
        ])
    elif map_profile := profile_map_atlas(data):
        lines.extend([
            f"- geometry: {map_profile['authored_column_count']} authored cells "
            f"inside a {map_profile['row_stride']}-cell row pitch",
            f"- coordinate scale: one tile = "
            f"{map_profile['tile_to_grid_scale']} grid cells",
            f"- authored rows: {map_profile['authored_row_count']} across grid Y "
            f"{map_profile['authored_grid_y_min']}.."
            f"{map_profile['authored_grid_y_max']} "
            f"({map_profile['implicit_zero_row_count']} omitted zero rows)",
            f"- nonzero cells: {map_profile['nonzero_cell_count']} "
            f"({map_profile['stage_rectangle_nonzero_cell_count']} inside stage "
            f"rectangles, "
            f"{map_profile['outside_stage_rectangle_nonzero_cell_count']} border cells)",
            f"- terrain ids: {map_profile['terrain_ids_used']}",
            f"- stage joins: {map_profile['stage_map_count']} definitions over "
            f"{map_profile['unique_atlas_rectangle_count']} unique rectangles",
            f"- shared rectangles: {map_profile['shared_rectangle_count']} used by "
            f"{map_profile['shared_stage_definition_count']} stage definitions",
        ])
    elif rule_profile := profile_rules(data):
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
            f"- selector-specific movement semantics: "
            f"{banked_profile['decoded_movement_step_count']} steps, "
            f"{banked_profile['decoded_movement_parameter_count']} populated "
            f"parameters + "
            f"{banked_profile['decoded_movement_defaulted_parameter_count']} "
            f"explicit defaults + "
            f"{banked_profile['ignored_movement_parameter_count']} "
            f"authored-but-unread parameters "
            f"across {banked_profile['decoded_movement_provider_count']} providers",
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
        "footer_array_count": (
            data.get("footer_array_count", 0)
            if data.get("schema") == "stage-terrain-atlas"
            else sum(row["kind"] == "footer-array" for row in rows)
        ),
        "rule_output_count": sum(row["kind"] == "rule-output" for row in rows),
        "dispatch_field_count": sum(row["kind"] == "dispatch-field" for row in rows),
        "message_profile": messages,
        "rule_profile": profile_rules(data),
        "dispatch_profile": profile_dispatch(data),
        "banked_profile": profile_banked(data),
        "map_atlas_profile": profile_map_atlas(data),
        "terrain_definition_profile": profile_terrain_definitions(data),
        "h_scene_gallery_profile": profile_h_scene_gallery(data),
        "training_action_profile": profile_training_actions(data),
        "card_generation_profile": profile_card_generation_lists(data),
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
