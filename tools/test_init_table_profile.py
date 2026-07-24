#!/usr/bin/env python3
"""Tests for INIT field profiling. Run: py -3.11 -X utf8 tools/test_init_table_profile.py"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import init_table_profile as profile


def main() -> int:
    fixture = {
        "table": "TEST",
        "field_semantics": {
            "0x10": "test_parallel",
            "0x30/3/0": "test_record.zero",
            "0x40/5/4": "test_names.level_5",
        },
        "records": [
            {"id": 1, "name": "one", "fields": {"0x10": 2, "0x20": 0},
             "string_fields": {"0x40/5/4": "One5"},
             "record_fields": {"0x30/3/0": 9},
             "message": {"title": "One", "description": "First",
                         "furigana": [{"line": 0, "text": "One", "reading": "one"}]}},
            {"id": 3, "name": "three", "fields": {"0x10": 2}},
            {"id": 7, "name": "seven", "fields": {"0x10": 5},
             "record_fields": {"0x30/3/2": 4}},
        ]
    }
    rows = {row["key"]: row for row in profile.profile_columns(fixture)}
    assert rows["0x10"]["population"] == 3
    assert rows["0x10"]["coverage"] == 1.0
    assert rows["0x10"]["distinct_values"] == 2
    assert rows["0x10"]["min"] == 2 and rows["0x10"]["max"] == 5
    assert rows["0x10"]["common"][0] == {"value": "2", "count": 2}
    assert rows["0x10"]["semantic_name"] == "test_parallel"
    assert rows["0x10"]["examples"][0]["message_description"] == "First"
    assert rows["0x20"]["population"] == 1
    assert rows["0x20"]["examples"][0]["name"] == "one"
    assert rows["0x30/3/0"]["kind"] == "record-column"
    assert rows["0x30/3/0"]["base"] == "0x30"
    assert rows["0x30/3/0"]["stride"] == 3
    assert rows["0x30/3/0"]["semantic_name"] == "test_record.zero"
    assert rows["0x30/3/2"]["column"] == 2
    assert rows["0x40/5/4"]["kind"] == "string-record-column"
    assert rows["0x40/5/4"]["stride"] == 5
    assert rows["0x40/5/4"]["column"] == 4
    assert rows["0x40/5/4"]["semantic_name"] == "test_names.level_5"

    mixed_fixture = {
        "table": "MIXED",
        "mode": "mixed",
        "array_layouts": {"0x100": {"length": 9, "stride": 3, "rows": 3}},
        "field_semantics": {"0x40": "condition", "0x50": "scalar"},
        "records": [
            {
                "id": 11,
                "string_fields": {"0x40": "Win"},
                "fields": {"0x50": 20},
                "array_fields": {"0x100/4": 7},
                "footer_arrays": {
                    "0x100/6": {"footer_off": "0x200", "values": [1, 2, 3]},
                },
            },
        ],
    }
    mixed = {row["key"]: row for row in profile.profile_columns(mixed_fixture)}
    assert mixed["0x40"]["kind"] == "string-field"
    assert mixed["0x40"]["semantic_name"] == "condition"
    assert mixed["0x50"]["kind"] == "scalar-field"
    assert mixed["0x100/4"]["kind"] == "array-cell"
    assert mixed["0x100/4"]["stride"] == 3 and mixed["0x100/4"]["column"] == 1
    assert mixed["0x100/6"]["kind"] == "footer-array"
    assert mixed["0x100/6"]["examples"][0]["footer_off"] == "0x200"
    assert mixed["0x100/6"]["common"][0]["value"] == "[1, 2, 3]"
    assert mixed["0x40"]["examples"][0]["name"] == "Win"

    rule_fixture = {
        "table": "RULES",
        "mode": "rules",
        "records": [
            {
                "id": 1, "unit_id": 3, "title": "", "fields": {"0x10": -1},
                "class_change_slot_index": 0, "skill_awards": [{"skill_id": 2}],
            },
            {
                "id": 2, "unit_id": 5, "title": "Promoted",
                "minimum_level": 50, "class_change_slot_index": 1,
                "fields": {"0x10": 50},
            },
        ],
    }
    rule_rows = {row["key"]: row for row in profile.profile_columns(rule_fixture)}
    assert rule_rows["0x10"]["kind"] == "rule-output"
    rule_summary = profile.profile_rules(rule_fixture)
    assert rule_summary["unit_count"] == 2
    assert rule_summary["titled_rule_count"] == 1
    assert rule_summary["level_independent_rule_count"] == 1
    assert rule_summary["minimum_levels"] == {"50": 1}
    assert rule_summary["class_change_slot_indices"] == {"0": 1, "1": 1}
    assert rule_summary["skill_award_count"] == 1

    dispatch_fixture = {
        "table": "DISPATCH",
        "mode": "dispatch",
        "assignment_count": 4,
        "overwritten_record_count": 1,
        "conflicting_chapter_record_count": 1,
        "resolved_script_count": 2,
        "scjump_joined_record_count": 2,
        "scjump_chapter_match_count": 1,
        "scjump_chapter_mismatches": [{"decision_id": 2}],
        "records": [
            {"id": 1, "name": "SC0000.BIN", "fields": {"0x100": 34, "0x200": 1}},
            {"id": 2, "name": "SC0010.BIN", "fields": {"0x100": 286, "0x200": 2}},
        ],
    }
    dispatch_rows = {
        row["key"]: row for row in profile.profile_columns(dispatch_fixture)
    }
    assert dispatch_rows["0x100"]["kind"] == "dispatch-field"
    dispatch_summary = profile.profile_dispatch(dispatch_fixture)
    assert dispatch_summary["assignment_count"] == 4
    assert dispatch_summary["overwritten_record_count"] == 1
    assert dispatch_summary["scjump_chapter_match_count"] == 1
    assert dispatch_summary["scjump_chapter_mismatch_count"] == 1

    banked_fixture = {
        "table": "BANKED",
        "mode": "banked",
        "assignment_count": 6,
        "populated_cell_count": 5,
        "overwritten_cell_count": 1,
        "conflicting_overwrite_count": 1,
        "movement_step_count": 2,
        "battle_step_count": 1,
        "decoded_movement_provider_count": 1,
        "decoded_movement_step_count": 1,
        "decoded_movement_parameter_count": 2,
        "decoded_movement_defaulted_parameter_count": 1,
        "ignored_movement_parameter_count": 1,
        "movement_provider_scripts": {"1": "RTN_M001.BIN"},
        "battle_provider_scripts": {"1": "RTN_B001.BIN"},
        "used_movement_provider_selectors": [1],
        "used_battle_provider_selectors": [1],
        "bank_layouts": {
            "0x100": {"reserved_empty": False},
            "0x200": {"reserved_empty": True},
        },
        "records": [
            {
                "id": 1,
                "record_fields": {
                    "0x100/20/0": 1,
                    "0x100/20/1": 2,
                },
            },
        ],
    }
    banked_rows = {
        row["key"]: row for row in profile.profile_columns(banked_fixture)
    }
    assert banked_rows["0x100/20/0"]["kind"] == "record-column"
    banked_summary = profile.profile_banked(banked_fixture)
    assert banked_summary["assignment_count"] == 6
    assert banked_summary["populated_bank_count"] == 1
    assert banked_summary["reserved_bank_count"] == 1
    assert banked_summary["movement_step_count"] == 2
    assert banked_summary["battle_provider_count"] == 1
    assert banked_summary["decoded_movement_provider_count"] == 1
    assert banked_summary["decoded_movement_step_count"] == 1
    assert banked_summary["decoded_movement_parameter_count"] == 2
    assert banked_summary["decoded_movement_defaulted_parameter_count"] == 1
    assert banked_summary["ignored_movement_parameter_count"] == 1

    map_fixture = {
        "table": "MAP",
        "mode": "footer",
        "schema": "stage-terrain-atlas",
        "record_count": 1472,
        "row_stride": 53,
        "authored_column_count": 50,
        "tile_to_grid_scale": 2,
        "authored_row_count": 1472,
        "implicit_zero_row_count": 127,
        "authored_grid_y_min": 2,
        "authored_grid_y_max": 1600,
        "nonzero_cell_count": 17126,
        "stage_rectangle_nonzero_cell_count": 17079,
        "outside_stage_rectangle_nonzero_cell_count": 47,
        "terrain_ids_used": [0, 1, 2, 3],
        "stage_map_count": 66,
        "unique_atlas_rectangle_count": 53,
        "shared_atlas_rectangles": [
            {"stage_ids": [32, 33, 34]},
            {"stage_ids": [35, 36]},
        ],
        "records": [],
    }
    map_summary = profile.profile_map_atlas(map_fixture)
    assert map_summary["row_stride"] == 53
    assert map_summary["authored_row_count"] == 1472
    assert map_summary["outside_stage_rectangle_nonzero_cell_count"] == 47
    assert map_summary["stage_map_count"] == 66
    assert map_summary["shared_rectangle_count"] == 2
    assert map_summary["shared_stage_definition_count"] == 5
    rendered_map = profile.render_markdown(map_fixture, [], 40)
    assert "- geometry: 50 authored cells inside a 53-cell row pitch" in rendered_map
    assert "- stage joins: 66 definitions over 53 unique rectangles" in rendered_map

    terrain_fixture = {
        "table": "LAND",
        "mode": "name",
        "schema": "terrain-definitions",
        "record_count": 20,
        "reserved_record_span": 30,
        "authored_terrain_ids": list(range(17)),
        "implicit_default_terrain_ids": [0, 5, 6],
        "combat_stat_cell_count": 13,
        "required_skill_count": 5,
        "map_texture_slot_count": 20,
        "default_texture_asset_count": 10,
        "default_texture_asset_join_count": 10,
        "records": [
            {
                "name": "通路" if index else None,
                "effect_description": "▲命中" if index < 5 else None,
                "required_skill_name": (
                    ("飛行", "潜水", "探索", "耐熱")[index]
                    if index < 4 else None
                ),
            }
            for index in range(20)
        ],
    }
    terrain_summary = profile.profile_terrain_definitions(terrain_fixture)
    assert terrain_summary["reserved_record_span"] == 30
    assert terrain_summary["shipped_record_count"] == 20
    assert terrain_summary["authored_record_count"] == 17
    assert terrain_summary["effect_description_count"] == 5
    assert terrain_summary["combat_stat_cell_count"] == 13
    assert terrain_summary["required_skill_names"] == ["探索", "潜水", "耐熱", "飛行"]
    rendered_terrain = profile.render_markdown(terrain_fixture, [], 40)
    assert "- shipped terrain ids: 20 inside a 30-row table" in rendered_terrain
    assert "- combat-stat cells: 13" in rendered_terrain

    messages = profile.profile_messages(fixture)
    assert messages["population"] == 1
    assert messages["coverage"] == 1 / 3
    assert messages["furigana_records"] == 1
    assert messages["examples"][0]["description"] == "First"
    matches = profile.find_message_matches(fixture, "first|three")
    assert [record["id"] for record in matches] == [1, 3]
    rendered = profile.render_message_matches(fixture, "First")
    assert "| 1 | one | First |" in rendered
    assert "`test_record.zero` (`0x30/3/0`)=9" in rendered

    enemy_fixture = {
        "table": "ENEMY",
        "records": [
            {
                "id": 101,
                "name": "boss",
                "message": {
                    "summary": "Powerful knight",
                    "strategy": "Avoid the first encounter",
                },
            }
        ],
    }
    enemy_messages = profile.profile_messages(enemy_fixture)
    assert enemy_messages["examples"][0]["title"] == "Powerful knight"
    assert enemy_messages["examples"][0]["description"] == "Avoid the first encounter"
    assert enemy_messages["examples"][0]["message_fields"] == {
        "summary": "Powerful knight",
        "strategy": "Avoid the first encounter",
    }
    assert profile.find_message_matches(enemy_fixture, "first encounter")

    character_fixture = {
        "table": "CHARACTER",
        "records": [
            {
                "id": 1,
                "name": "Emilio",
                "message": {
                    "biography": "Former demon king\nNow a familiar",
                },
            }
        ],
    }
    character_messages = profile.profile_messages(character_fixture)
    assert character_messages["examples"][0]["title"] == ""
    assert (
        character_messages["examples"][0]["description"]
        == "Former demon king\nNow a familiar"
    )
    assert character_messages["examples"][0]["message_fields"] == {
        "biography": "Former demon king\nNow a familiar",
    }
    assert profile.find_message_matches(character_fixture, "familiar")
    print("all init_table_profile checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
