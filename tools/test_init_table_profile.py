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
        "records": [{}, {}, {}],
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

    h_gallery_fixture = {
        "table": "SP",
        "mode": "numeric",
        "schema": "h-scene-gallery-pages",
        "record_count": 8,
        "page_count": 8,
        "slots_per_page": 15,
        "registry_capacity": 120,
        "populated_scene_count": 118,
        "empty_cells": [{"page": 7, "slot": 13}, {"page": 7, "slot": 14}],
        "resolved_scene_script_count": 118,
        "resolved_thumbnail_sheet_count": 8,
        "records": [],
    }
    h_gallery_summary = profile.profile_h_scene_gallery(h_gallery_fixture)
    assert h_gallery_summary["page_count"] == 8
    assert h_gallery_summary["populated_scene_count"] == 118
    assert h_gallery_summary["resolved_thumbnail_sheet_count"] == 8
    rendered_h_gallery = profile.render_markdown(h_gallery_fixture, [], 40)
    assert "- geometry: 8 pages × 15 slots" in rendered_h_gallery
    assert "- populated scenes: 118/120" in rendered_h_gallery

    card_fixture = {
        "table": "CARDS",
        "mode": "numeric",
        "schema": "card-generation-lists",
        "record_count": 2,
        "entry_count": 3,
        "distinct_card_ids": [1, 14],
        "resolved_card_reference_count": 3,
        "used_selector_ids": [1],
        "unreferenced_selector_ids": [55],
        "stage_definition_reference_count": 1,
        "stage_object_reference_count": 2,
        "runtime_scan_capacity": 100,
        "cleared_entry_prefix": 50,
        "records": [
            {
                "id": 1,
                "entry_count": 2,
                "entries": [
                    {
                        "card_id": 1,
                        "required_story_flag_ids": [],
                        "forbidden_story_flag_ids": [],
                    },
                    {
                        "card_id": 14,
                        "required_story_flag_ids": [901],
                        "forbidden_story_flag_ids": [861],
                    },
                ],
            },
            {
                "id": 55,
                "entry_count": 1,
                "entries": [
                    {
                        "card_id": 1,
                        "required_story_flag_ids": [],
                        "forbidden_story_flag_ids": [],
                    },
                ],
            },
        ],
    }
    card_summary = profile.profile_card_generation_lists(card_fixture)
    assert card_summary["list_count"] == 2
    assert card_summary["entry_count"] == 3
    assert card_summary["story_flag_gated_entry_count"] == 1
    assert card_summary["list_entry_counts"] == {"1": 2, "55": 1}
    rendered_cards = profile.render_markdown(card_fixture, [], 40)
    assert "- card-generation lists: 2" in rendered_cards
    assert "- runtime scan/clear prefix: 100/50 slots" in rendered_cards

    definition_fixture = {
        "table": "CARDDEFS",
        "mode": "name",
        "schema": "card-definitions",
        "record_count": 3,
        "reserved_record_count": 100,
        "type_counts": {
            "item_award": 1,
            "resource_recovery": 1,
            "trap": 1,
        },
        "awarded_item_join_count": 1,
        "event_dispatch_join_count": 0,
        "condition_join_count": 1,
        "visual_asset_join_count": 3,
        "ignored_required_story_flag_count": 1,
        "records": [{}, {}, {}],
    }
    definition_summary = profile.profile_card_definitions(
        definition_fixture
    )
    assert definition_summary["card_count"] == 3
    assert definition_summary["reserved_record_count"] == 100
    assert definition_summary["visual_asset_join_count"] == 3
    rendered_definitions = profile.render_markdown(
        definition_fixture, [], 40
    )
    assert "- card definitions: 3/100 rows" in rendered_definitions
    assert "- engine-dead third required flags: 1" in rendered_definitions

    effect_fixture = {
        "table": "EFFECTS",
        "mode": "numeric",
        "schema": "battle-effect-definitions",
        "record_count": 4,
        "effect_definition_count": 4,
        "runtime_work_slot_count": 6,
        "visual_mode_counts": {"movie": 3, "opaque_sprite_sheet": 1},
        "resolved_visual_asset_count": 4,
        "sound_effect_count": 3,
        "resolved_sound_asset_count": 3,
        "sprite_sheet_effect_count": 1,
        "hit_pulse_effect_count": 2,
        "referenced_effect_definition_count": 3,
        "unreferenced_effect_definition_ids": [999],
        "engine_dead_atlas_row_count": 1,
        "records": [{}, {}, {}, {}],
    }
    effect_summary = profile.profile_battle_effect_definitions(
        effect_fixture
    )
    assert effect_summary["effect_definition_count"] == 4
    assert effect_summary["visual_mode_counts"]["movie"] == 3
    rendered_effects = profile.render_markdown(
        effect_fixture, [], 40
    )
    assert "- effect definitions: 4 for 6 runtime slots" in rendered_effects
    assert "- sound assets resolved: 3/3" in rendered_effects

    animation_fixture = {
        "table": "ANIMATIONS",
        "mode": "numeric",
        "schema": "battle-animation-timelines",
        "record_count": 3,
        "authored_record_count": 3,
        "reserved_record_count": 1000,
        "effect_reference_count": 12,
        "distinct_effect_id_count": 8,
        "resolved_effect_reference_count": 12,
        "effect_slot_populations": {
            "0": 2, "1": 2, "2": 2, "3": 2, "4": 0, "5": 4,
        },
        "delay_slot_populations": {
            "0": 0, "1": 0, "2": 2, "3": 2, "4": 0, "5": 2,
        },
        "complete_timeline_count": 2,
        "auxiliary_timeline_count": 1,
        "skill_reference_count": 2,
        "skill_animation_count": 2,
        "weapon_class_animation_count": 21,
        "unjoined_authored_animation_ids": [205],
        "records": [{}, {}, {}],
    }
    animation_summary = profile.profile_battle_animations(
        animation_fixture
    )
    assert animation_summary["authored_record_count"] == 3
    assert animation_summary["auxiliary_timeline_count"] == 1
    rendered_animations = profile.render_markdown(
        animation_fixture, [], 40
    )
    assert "- battle animations: 3/1000 rows" in rendered_animations
    assert "- full/auxiliary timelines: 2/1" in rendered_animations

    stage_fixture = {
        "table": "STAGES",
        "mode": "name",
        "schema": "stage-definitions",
        "record_count": 5,
        "reserved_record_count": 1000,
        "description_line_count": 18,
        "mapped_stage_count": 4,
        "event_only_stage_count": 1,
        "main_progression_stage_count": 3,
        "extra_dungeon_stage_count": 1,
        "story_flag_gated_stage_count": 5,
        "scjump_reference_count": 12,
        "resolved_scjump_reference_count": 12,
        "resolved_loader_script_count": 5,
        "clear_coin_reward_cell_count": 4,
        "unresolved_parameter_population": 4,
        "records": [{}, {}, {}, {}, {}],
    }
    stage_summary = profile.profile_stage_definitions(stage_fixture)
    assert stage_summary["mapped_stage_count"] == 4
    assert stage_summary["resolved_scjump_reference_count"] == 12
    rendered_stages = profile.render_markdown(
        stage_fixture, [], 40
    )
    assert "- stage definitions: 5/1000 rows" in rendered_stages
    assert "- mapped/event-only stages: 4/1" in rendered_stages
    assert "- SCJUMP joins: 12/12" in rendered_stages

    training_fixture = {
        "table": "TRAINING",
        "mode": "name",
        "schema": "training-action-definitions",
        "record_count": 3,
        "string_write_count": 7,
        "authored_numeric_cell_counts": {
            "required_story_flag_ids": 2,
            "unit_stat_deltas": 4,
        },
        "event_cell_count": 6,
        "distinct_event_story_flag_ids": [800, 801, 830],
        "resolved_event_dispatch_count": 6,
        "records": [
            {
                "description_lines": ["one", "cost"],
                "locked_hint_lines": [],
                "eligibility": {},
                "effects": {"awarded_skill_id": 1},
                "execution_limit": 3,
            },
            {
                "description_lines": ["two", "cost"],
                "locked_hint_lines": ["locked"],
                "eligibility": {
                    "required_item_id": 35,
                    "minimum_alignment": 10,
                    "minimum_training_progress": 5,
                },
                "effects": {"awarded_item_id": 51},
                "execution_limit": 2,
            },
            {
                "description_lines": ["three"],
                "locked_hint_lines": ["locked", "more"],
                "eligibility": {"maximum_alignment": -20},
                "effects": {},
                "execution_limit": 1,
            },
        ],
    }
    training_summary = profile.profile_training_actions(training_fixture)
    assert training_summary["action_count"] == 3
    assert training_summary["description_line_count"] == 5
    assert training_summary["locked_hint_line_count"] == 3
    assert training_summary["required_item_count"] == 1
    assert training_summary["minimum_alignment_gate_count"] == 1
    assert training_summary["maximum_alignment_gate_count"] == 1
    assert training_summary["stat_delta_cell_count"] == 4
    assert training_summary["execution_limits"] == {"1": 1, "2": 1, "3": 1}
    rendered_training = profile.render_markdown(training_fixture, [], 40)
    assert "- training actions: 3" in rendered_training
    assert "- event slots: 6 across 3 distinct story flags" in rendered_training

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
