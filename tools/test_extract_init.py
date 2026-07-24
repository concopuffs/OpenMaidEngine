#!/usr/bin/env python3
"""Regression tests for INIT table extraction.

Run: py -3.11 -X utf8 tools/test_extract_init.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import extract_init
import extract_message_table
import paths
import sys4load


FAILS: list[str] = []


def check(condition: bool, message: str) -> None:
    print(("  ok  " if condition else " FAIL ") + message)
    if not condition:
        FAILS.append(message)


def test_real_name_tables() -> None:
    expected = {
        "SKINIT.BIN": (300, 131),
        "ITINIT.BIN": (1000, 287),
        "EBINIT.BIN": (1000, 277),
        "OBINIT.BIN": (100, 46),
    }
    scripts = paths.scripts()
    for name, (span, count) in expected.items():
        records, meta = extract_init.extract_name(sys4load.load(scripts[name]))
        check(meta["record_span"] == span, f"{name}: record span is {span}")
        check(len(records) == count, f"{name}: extracts {count} named records")
        check(len({record["id"] for record in records}) == count,
              f"{name}: record ids are unique")

    items, _ = extract_init.extract_name(sys4load.load(scripts["ITINIT.BIN"]))
    by_id = {record["id"]: record for record in items}
    check(by_id[1]["name"] == "銅の鍵", "ITINIT item 1 is the copper key")
    check(len(by_id[1]["fields"]) == 5, "ITINIT item 1 owns only its five fields")
    check("desc" not in by_id[1], "ITINIT item 1 has no fabricated description")
    check(by_id[101]["desc"] == "ＨＰ３０回復", "ITINIT item 101 keeps its description")
    check(by_id[1]["fields"]["0x8c879"] == 10,
          "ITINIT columns use the runtime lookup base")
    check(len({key for record in items for key in record["fields"]}) == 13,
          "ITINIT has thirteen parallel-array fields")
    check(len({key for record in items for key in record.get("record_fields", {})}) == 44,
          "ITINIT linked row-major tables expose 44 populated columns")
    check(by_id[101]["record_fields"]["0xa5301/3/0"] == 30,
          "ITINIT item 101 stores HP recovery in row-major column zero")
    check(by_id[108]["record_fields"]["0x906f9/30/8"] == -5,
          "ITINIT item 108 preserves its paralysis-removal delta")

    units, _ = extract_init.extract_name(sys4load.load(scripts["EBINIT.BIN"]))
    unit_by_id = {record["id"]: record for record in units}
    check(len({
        key
        for record in units
        for key in {
            **record.get("fields", {}),
            **record.get("record_fields", {}),
        }
    }) == 108, "EBINIT exposes 108 genuine populated fields")
    check(len({
        key for record in units for key in record.get("record_fields", {})
    }) == 82, "EBINIT exposes 82 genuine linked row-major columns")
    check(
        unit_by_id[456]["fields"]["0x6fb86"] == 12585
        and "0x4e693/300/91" not in unit_by_id[456].get("record_fields", {}),
        "EBINIT unit 456 keeps its battle sprite in the parallel asset column",
    )
    check(
        unit_by_id[600]["fields"]["0x7a37e"] == 80
        and "0x4e693/300/35" not in unit_by_id[600].get("record_fields", {}),
        "EBINIT unit 600 keeps its starting level in the parallel level column",
    )

    objects, _ = extract_init.extract_name(sys4load.load(scripts["OBINIT.BIN"]))
    object_by_id = {record["id"]: record for record in objects}
    check(object_by_id[17]["name"] == "針"
          and object_by_id[17]["desc"] == "ＨＰ-2",
          "OBINIT object 17 preserves its name and effect description")
    check(object_by_id[28]["name"] == "カード取得",
          "OBINIT object ids provide authoritative STINIT type labels")

    vocabulary, vocabulary_meta = extract_init.extract_vocabulary(
        sys4load.load(scripts["VIINIT.BIN"])
    )
    vocabulary_by_id = {record["id"]: record for record in vocabulary}
    check(
        vocabulary_meta["record_span"] == 200
        and len(vocabulary) == 65
        and vocabulary[-1]["id"] == 120,
        "VIINIT extracts 65 sparse glossary topics from its 200-row table",
    )
    check(
        vocabulary_by_id[1]["name"] == "【迷宮】占有率"
        and vocabulary_by_id[1]["record_fields"]["0x15a2a9/3/0"] == 201,
        "VIINIT associates pre-name unlock writes with the correct topic",
    )


def test_character_profiles() -> None:
    scripts = paths.scripts()
    records, meta = extract_init.extract_character_profiles(
        sys4load.load(scripts["CIINIT.BIN"])
    )
    by_id = {record["id"]: record for record in records}
    check(len(records) == 24, "CIINIT extracts all 24 character profiles")
    check(
        meta["record_span"] == 100
        and meta["unit_id_array_base"] == "0x15a118"
        and meta["portrait_asset_array_base"] == "0x15a17c",
        "CIINIT exposes the four 100-cell profile columns",
    )
    check(
        meta["implicit_defaults"]
        == {"0x15a1e0": 0, "0x15a244": 0},
        "CIINIT records its two unwritten portrait-placement defaults",
    )
    check(
        by_id[1]["name"] == "エミリオ"
        and by_id[1]["fields"]["0x15a118"] == 1
        and by_id[1]["fields"]["0x15a17c"] == 0x2C88,
        "CIINIT profile 1 joins Emilio to unit and portrait resources",
    )
    check(
        by_id[16]["fields"]["0x15a118"] == 0x5F
        and "0x15a17c" not in by_id[16]["fields"],
        "CIINIT preserves the portrait-fallback profiles",
    )


def test_magic_actions() -> None:
    scripts = paths.scripts()
    records, meta = extract_init.extract_magic_actions(
        sys4load.load(scripts["MAINIT.BIN"])
    )
    by_id = {record["id"]: record for record in records}
    check(len(records) == 11, "MAINIT extracts all 11 magic/research actions")
    check(
        meta["record_span"] == 30
        and meta["name_array_base"] == "0x45b9"
        and meta["handler_script_array_base"] == "0x1561f6",
        "MAINIT exposes its reserved span and handler column",
    )
    check(
        by_id[1]["name"] == "闇の治癒"
        and by_id[1]["fields"]["0x156142"] == 10,
        "MAINIT action 1 keeps its name and authored cost-like field",
    )
    check(
        all(
            record["fields"]["0x1561f6"] == 0x31A6
            for record in records
        ),
        "MAINIT routes every action to the MAMES information handler",
    )
    check(
        "0x1561d8" not in by_id[9]["fields"]
        and by_id[10]["fields"]["0x1561d8"] == 15
        and by_id[11]["fields"]["0x1561d8"] == 40,
        "MAINIT preserves the sparse growth-ritual threshold column",
    )


def test_static_negative_write() -> None:
    class Instruction:
        opcode = extract_init.SUB
        args = [(extract_init.T_GLOBAL_INT, 0x123),
                (extract_init.T_IMM, 0), (extract_init.T_IMM, 7)]

    check(extract_init._static_global_write(Instruction()) == (0x123, -7),
          "INIT subtraction writes preserve negative values")


def test_output_name_validation() -> None:
    check(extract_init.normalize_outname("ITINIT.json") == "ITINIT",
          "INIT output names tolerate one JSON suffix")
    try:
        extract_init.normalize_outname("build/data/ITINIT.json")
    except ValueError:
        rejected = True
    else:
        rejected = False
    check(rejected, "INIT output names reject nested paths")


def test_real_mixed_table() -> None:
    script = sys4load.load(extract_init.resolve("STINIT"))
    check(extract_init.detect_mode(script) == "mixed",
          "STINIT auto-detects as a mixed selector table")
    records, meta = extract_init.extract_mixed(script)
    check(len(records) == 74, "STINIT extracts all 74 sparse stage records")
    check(records[0]["id"] == 1 and records[-1]["id"] == 170,
          "STINIT preserves sparse runtime stage ids")
    check(meta["selector_global"] == "0x4dfbc",
          "STINIT records are keyed by scjump_progress_a")
    check(meta["array_layouts"]["0xe74b5"] == {
        "length": 350, "stride": 7, "rows": 50,
    }, "STINIT preamble recovers a consumer-confirmed row buffer")
    stage1 = records[0]
    check(list(stage1["string_fields"].values()) == [
        "オークの撃破", "", "自軍拠点の制圧", "５０ターン経過",
    ], "STINIT stage 1 preserves all four condition strings")
    check(stage1["fields"]["0xe7302"] == 12
          and stage1["fields"]["0xe730c"] == 50
          and stage1["fields"]["0xe730d"] == 0,
          "STINIT stage 1 preserves BGM and turn-limit scalars")
    check(stage1["array_fields"]["0xe7305/3"] == -2,
          "STINIT fixed-buffer cells preserve negative values")
    check(stage1["footer_arrays"]["0xe7889/3"]["values"] == [1, 1, 1],
          "STINIT length-prefixed footer arrays retain their destination")
    check(sum(len(record.get("footer_arrays", {})) for record in records) == 1396,
          "STINIT accounts for every footer-array copy")
    extract_init.attach_stage_object_placements(records)
    first_object = records[0]["object_placements"][0]
    check(first_object == {
        "slot": 1,
        "type_id": 1,
        "type_name": "拠点",
        "type_description": "▲命中・回避・防御",
        "tile_x": 13,
        "tile_y": 1,
        "difficulty_mask": 7,
        "initial_faction_id": 1,
    }, "STINIT joins confirmed object buffers into one placement record")
    stage1_slot6 = next(
        obj for obj in records[0]["object_placements"] if obj["slot"] == 6
    )
    check(stage1_slot6["item_id"] == 222
          and stage1_slot6["item_quantity"] == 1,
          "STINIT object payloads decode by object type")
    stage1_slot5 = next(
        obj for obj in records[0]["object_placements"] if obj["slot"] == 5
    )
    check(stage1_slot5["reinforcement_interval_turns"] == 10
          and stage1_slot5["reinforcement_spawn_limit"] == 3,
          "STINIT object placements expose reinforcement schedules")
    stage1_slot8 = next(
        obj for obj in records[0]["object_placements"] if obj["slot"] == 8
    )
    check(stage1_slot8["card_generation_list_id"] == 1,
          "STINIT card objects expose their generation-list id")
    check(stage1_slot8["type_name"] == "カード取得",
          "STINIT object placements join OBINIT type names")
    stage1_slot9 = next(
        obj for obj in records[0]["object_placements"] if obj["slot"] == 9
    )
    check(stage1_slot9["destination_tile_x"] == 13
          and stage1_slot9["destination_tile_y"] == 7,
          "STINIT teleport payloads expose destination coordinates")
    stage2_slot3 = next(
        obj for obj in records[1]["object_placements"] if obj["slot"] == 3
    )
    check(stage2_slot3["required_story_flags"] == [902],
          "STINIT object placements join positive story prerequisites")
    all_objects = [
        obj for record in records for obj in record["object_placements"]
    ]
    check(sum("non_triggering_faction_id" in obj for obj in all_objects) == 104,
          "STINIT hazard/barrier payloads expose their FIELD-proven faction gate")
    check({
        obj["non_triggering_faction_id"]
        for obj in all_objects
        if "non_triggering_faction_id" in obj
    } == {1, 2, 3}, "STINIT faction gates retain all observed faction ids")
    state_objects = [
        obj for obj in all_objects if "initial_object_state_id" in obj
    ]
    check(len(state_objects) == 78,
          "STINIT state-row metadata decodes every remaining initialized object state")
    check({
        obj["type_id"] for obj in state_objects
    } == {11, 17, 26}, "STINIT state-row payloads remain scoped to proven object types")
    deployment_flags = [
        obj for obj in state_objects if obj["type_id"] == 26
    ]
    check(len(deployment_flags) == 61
          and {obj["initial_object_state_id"] for obj in deployment_flags} == {2},
          "STINIT deployment flags expose their initial object state")
    spike = next(
        obj for obj in state_objects if obj["type_id"] == 17
    )
    check(spike["initial_object_state_id"] == 2
          and "non_triggering_faction_id" not in spike,
          "STINIT spikes expose state without inventing the excluded faction gate")
    ignored_objects = [
        obj for obj in all_objects if "ignored_payload_fields" in obj
    ]
    check(len(ignored_objects) == 3
          and {obj["type_id"] for obj in ignored_objects} == {27}
          and {tuple(obj["ignored_payload_fields"].items()) for obj in ignored_objects}
          == {(("0xe73bb", 2),)},
          "STINIT preserves explicitly written but engine-ignored type-27 payloads")
    unknown_objects = [obj for obj in all_objects if "unknown_fields" in obj]
    check(not unknown_objects,
          "STINIT has no unresolved populated tagged object payloads")
    extract_init.attach_stage_enemy_spawns(records)
    first_spawn = records[0]["enemy_spawns"][0]
    check(first_spawn == {
        "slot": 1,
        "unit_id": 205,
        "faction_id": 2,
        "difficulty_mask": 7,
        "min_level": 1,
        "max_level": 10,
        "auto_level_scale_divisor": 1,
        "object_slot": 2,
        "movement_routine_set_ids": [1, 1, 1],
        "forbidden_story_flags": [11],
        "first_clear_only": True,
    }, "STINIT joins confirmed enemy buffers into one spawn record")
    stage1_slot2 = records[0]["enemy_spawns"][1]
    check(stage1_slot2["random_selection_weight"] == 1
          and stage1_slot2["object_slot"] == 5,
          "STINIT preserves weighted object-linked enemy alternatives")
    check(sum(len(record["enemy_spawns"]) for record in records) == 1378,
          "STINIT assembles every populated enemy spawn slot")
    linked_deployment_spawns = []
    for record in records:
        objects_by_slot = {
            obj["slot"]: obj for obj in record["object_placements"]
        }
        linked_deployment_spawns.extend(
            spawn
            for spawn in record["enemy_spawns"]
            if (object_slot := spawn.get("object_slot")) in objects_by_slot
            and objects_by_slot[object_slot]["type_id"] == 26
        )
    check(len(linked_deployment_spawns) == 63
          and {spawn["faction_id"] for spawn in linked_deployment_spawns} == {2},
          "STINIT deployment flags correlate with all linked enemy-faction spawns")
    check(sum(
        spawn.get("first_clear_only", False)
        for record in records
        for spawn in record["enemy_spawns"]
    ) == 485, "STINIT exposes every first-clear-only enemy gate")


def test_real_class_change_rules() -> None:
    script = sys4load.load(extract_init.resolve("CCINIT"))
    check(extract_init.detect_mode(script) == "rules",
          "CCINIT auto-detects as a conditional rule program")
    records, meta = extract_init.extract_class_change_rules(script)
    check(len(records) == 71, "CCINIT extracts all 71 class-change rules")
    check(len({record["unit_id"] for record in records}) == 33,
          "CCINIT rules cover 33 unit definitions")
    check(meta["selector_global"] == "0x66715"
          and meta["persistent_state_table"] == "0x573bb",
          "CCINIT exposes its unit selector and persistent state table")
    check(records[0]["unit_name"] == "リリィ：少女時代"
          and records[0]["selected_level"] == -1
          and records[0]["excluded_when_unit_equals_global"] == "0x32f0",
          "CCINIT preserves Lily's level-independent form rule")
    sylphine = records[2]
    check(sylphine["unit_id"] == 5
          and sylphine["minimum_level"] == 50
          and sylphine["class_change_slot_index"] == 0
          and sylphine["title"] == "聖王女",
          "CCINIT decodes unit, level, slot, and awarded title")
    check(sylphine["deployment_cost_delta"] == 2
          and sylphine["stat_bonuses"]["physical_attack"] == 3,
          "CCINIT decodes cost and named stat bonuses")
    check(sylphine["skill_awards"] == [{
        "skill_slot": 3, "skill_id": 202, "skill_name": "光燐衝撃",
    }], "CCINIT joins awarded skill ids to SKINIT names")
    semantics = extract_init.field_semantics(records, meta["array_layouts"])
    check(semantics["0x26b4"] == "class_change_title_output"
          and semantics["0xab8e9/2"] == "class_change_stat_bonuses.physical_attack"
          and semantics["0xab8f7/2"] == "class_change_skill_awards.skill_slot_3",
          "CCINIT raw outputs join to canonical global and column names")
    extract_init.attach_semantic_fields(records, semantics)
    check(sylphine["semantic_fields"]["class_change_title_output"] == "聖王女"
          and sylphine["semantic_fields"][
              "class_change_stat_bonuses.physical_attack"
          ] == 3,
          "CCINIT rules expose a single semantic field view")
    check(all(
        record["minimum_level"] == record["selected_level"]
        for record in records[2:]
    ), "CCINIT normal promotion thresholds match their selected levels")
    check(sum(len(record.get("skill_awards", [])) for record in records) == 30,
          "CCINIT accounts for all 30 awarded skills")


def test_real_scene_dispatch() -> None:
    script = sys4load.load(extract_init.resolve("SCINIT"))
    check(extract_init.detect_mode(script) == "dispatch",
          "SCINIT auto-detects as paired scene dispatch arrays")
    records, meta = extract_init.extract_dispatch(script)
    by_id = {record["id"]: record for record in records}
    check(len(records) == 1209 and meta["assignment_count"] == 2179,
          "SCINIT preserves all assignments and 1,209 final decision rows")
    check(meta["script_resource_array_base"] == "0x87a57"
          and meta["authored_chapter_array_base"] == "0x8a167"
          and meta["reserved_array_span"] == 10000,
          "SCINIT exposes its paired 10,000-cell array layout")
    check(by_id[0]["script_resource_id"] == 34
          and by_id[0]["script_name"] == "SC0000.BIN"
          and by_id[0]["authored_chapter"] == 1,
          "SCINIT joins packed resource ids to scene names and chapter metadata")
    check(by_id[1]["assignment_count"] == 3
          and [entry["authored_chapter"] for entry in by_id[1]["assignments"]]
          == [1, 5, 5],
          "SCINIT retains source-ordered overwrites rather than only the final cell")
    check(meta["resolved_script_count"] == 1209
          and len({record["script_resource_id"] for record in records}) == 135,
          "every final SCINIT row resolves to one of 135 numbered scene scripts")
    check(meta["scjump_joined_record_count"] == 847
          and meta["scjump_chapter_match_count"] == 844
          and [row["decision_id"] for row in meta["scjump_chapter_mismatches"]]
          == [250, 1001, 1005],
          "SCINIT chapter tags cross-check against every live SCJUMP decision")
    semantics = extract_init.field_semantics(records)
    check(semantics["0x87a57"] == "scjump_scene_script_resource_ids"
          and semantics["0x8a167"] == "scjump_authored_chapters",
          "SCINIT's paired columns join to canonical semantic names")


def test_real_routine_banks() -> None:
    script = sys4load.load(extract_init.resolve("RTINIT"))
    check(extract_init.detect_mode(script) == "banked",
          "RTINIT auto-detects as parallel routine-step banks")
    records, meta = extract_init.extract_banked(script)
    by_id = {record["id"]: record for record in records}
    check(len(records) == 172
          and meta["first_record_id"] == 1
          and meta["last_record_id"] == 176
          and meta["missing_record_ids"] == [150, 151, 152, 153],
          "RTINIT preserves its sparse one-based routine-set ids")
    check(meta["bank_root_base"] == "0xeff78"
          and meta["bank_span"] == 20000
          and meta["bank_count"] == 20
          and meta["record_stride"] == 20
          and meta["reserved_record_span"] == 1000,
          "RTINIT exposes twenty parallel 1000-by-20 banks")
    check(meta["assignment_count"] == 3336
          and meta["populated_cell_count"] == 3307
          and meta["overwritten_cell_count"] == 29
          and meta["conflicting_overwrite_count"] == 11,
          "RTINIT preserves source assignments and final overwrite accounting")
    check(meta["movement_step_count"] == 1043
          and meta["battle_step_count"] == 14
          and len(meta["used_movement_provider_selectors"]) == 19
          and len(meta["used_battle_provider_selectors"]) == 4
          and len(meta["record_field_columns"]) == 117,
          "RTINIT assembles every populated movement and battle step")
    check(meta["decoded_movement_provider_count"] == 19
          and meta["decoded_movement_step_count"] == 1043
          and meta["decoded_movement_parameter_count"] == 974
          and meta["decoded_movement_defaulted_parameter_count"] == 13
          and meta["ignored_movement_parameter_count"] == 3,
          "RTINIT reports selector-specific semantic coverage")
    check([
        layout["bank_index"]
        for layout in meta["bank_layouts"].values()
        if layout["reserved_empty"]
    ] == [6, 13, 14, 15, 16, 17],
          "RTINIT keeps all six reserved empty banks in its structural layout")
    movement = by_id[1]["movement_steps"][0]
    battle = by_id[1]["battle_steps"][0]
    check(movement["movement_provider_selector"] == 1
          and movement["movement_activation_percent"] == 100
          and movement["provider_script"] == "RTN_M001.BIN",
          "RTINIT joins movement selectors and activation percentages")
    check(battle["battle_provider_selector"] == 1
          and battle["battle_activation_percent"] == 100
          and battle["provider_script"] == "RTN_B001.BIN",
          "RTINIT joins battle selectors and activation percentages")
    check(by_id[2]["battle_steps"][0]["battle_parameter_1"] == 219
          and by_id[2]["battle_steps"][0]["provider_script"] == "RTN_B004.BIN",
          "RTINIT retains provider-specific battle parameters")
    check(by_id[173]["movement_steps"][0]["movement_parameter_1"] == 2
          and by_id[173]["movement_steps"][0]["movement_parameter_2"] == 158,
          "RTINIT final rows reflect source-ordered conflicting overwrites")
    provider_5 = by_id[5]["movement_steps"][2]
    check(provider_5["movement_provider_selector"] == 5
          and provider_5["provider_behavior"] == "approach_destination_tile"
          and provider_5["destination_tile_x"] == 24
          and provider_5["destination_tile_y"] == 123
          and provider_5["movement_parameter_1"] == 24
          and provider_5["movement_parameter_2"] == 123,
          "RTINIT joins RTN_M005 destination semantics without replacing raw banks")
    provider_11 = by_id[87]["movement_steps"][2]
    check(provider_11["movement_provider_selector"] == 11
          and provider_11["provider_behavior"] == "cycle_destination_waypoints"
          and provider_11["destination_tile_x"] == 3
          and provider_11["destination_tile_y"] == 499
          and provider_11["waypoint_ordinal"] == 1
          and provider_11["path_cost_limit_override"] == 2,
          "RTINIT joins all four RTN_M011 waypoint parameters")
    provider_7 = by_id[33]["movement_steps"][4]
    check(provider_7["movement_provider_selector"] == 7
          and provider_7["provider_behavior"] == "approach_injured_ally"
          and provider_7["maximum_target_route_steps"] == 5
          and provider_7["maximum_target_hp_percent"] == 80,
          "RTINIT joins RTN_M007 injured-ally search semantics")
    provider_10 = by_id[99]["movement_steps"][0]
    check(provider_10["movement_provider_selector"] == 10
          and provider_10["provider_behavior"] == "approach_healing_feather"
          and provider_10["resource_index"] == 0
          and provider_10["maximum_resource_percent"] == 50
          and "movement_parameter_1" not in provider_10,
          "RTINIT projects RTN_M010's implicit HP default beside the raw banks")
    provider_12 = by_id[168]["movement_steps"][0]
    check(provider_12["movement_provider_selector"] == 12
          and provider_12["provider_behavior"]
          == "approach_destination_tile_avoiding_foreign_entities"
          and provider_12["destination_tile_x"] == 11
          and provider_12["destination_tile_y"] == 68,
          "RTINIT joins RTN_M012's route-mode destination semantics")
    provider_4 = by_id[5]["movement_steps"][0]
    check(provider_4["movement_provider_selector"] == 4
          and provider_4["provider_behavior"] == "approach_stage_object_slot"
          and provider_4["stage_object_slot_index"] == 1,
          "RTINIT joins RTN_M004's stage-object slot semantics")
    provider_4_default = by_id[17]["movement_steps"][1]
    check(provider_4_default["movement_provider_selector"] == 4
          and provider_4_default["stage_object_slot_index"] == 0
          and "movement_parameter_1" not in provider_4_default,
          "RTINIT projects RTN_M004's implicit stage-object slot zero")
    provider_6 = by_id[6]["movement_steps"][0]
    check(provider_6["movement_provider_selector"] == 6
          and provider_6["provider_behavior"] == "approach_nearest_enemy"
          and provider_6["maximum_target_route_steps"] == 5,
          "RTINIT joins RTN_M006's enemy-search radius")
    provider_15 = by_id[2]["movement_steps"][0]
    check(provider_15["movement_provider_selector"] == 15
          and provider_15["provider_behavior"] == "approach_foreign_magic_pillar"
          and provider_15["maximum_target_route_steps"] == 3,
          "RTINIT joins RTN_M015's foreign Magic Pillar search radius")
    provider_1_residue = by_id[173]["movement_steps"][2]
    check(provider_1_residue["movement_provider_selector"] == 1
          and provider_1_residue["provider_behavior"] == "advance_step_progress"
          and provider_1_residue["ignored_movement_parameters"] == {
              "movement_parameter_1": 10,
              "movement_parameter_2": 711,
          },
          "RTINIT marks RTN_M001's authored but unread parameter cells")
    provider_8_residue = by_id[112]["movement_steps"][6]
    check(provider_8_residue["movement_provider_selector"] == 8
          and provider_8_residue["provider_behavior"]
          == "approach_nearest_foreign_magic_pillar"
          and provider_8_residue["ignored_movement_parameters"]
          == {"movement_parameter_1": 1},
          "RTINIT marks RTN_M008's authored but unread parameter cell")
    provider_13 = by_id[104]["movement_steps"][3]
    check(provider_13["movement_provider_selector"] == 13
          and provider_13["provider_behavior"]
          == "approach_faction_traversable_tile"
          and provider_13["target_faction_filter"] == 1,
          "RTINIT joins RTN_M013's explicit faction filter")
    provider_13_default = by_id[23]["movement_steps"][1]
    check(provider_13_default["movement_provider_selector"] == 13
          and provider_13_default["target_faction_filter"] == 0
          and "movement_parameter_1" not in provider_13_default,
          "RTINIT projects RTN_M013's any-foreign-faction default")
    provider_14 = by_id[130]["movement_steps"][5]
    check(provider_14["movement_provider_selector"] == 14
          and provider_14["provider_behavior"] == "retreat_from_nearby_enemies"
          and provider_14["maximum_threat_route_steps"] == 6,
          "RTINIT joins RTN_M014's threat-detection radius")
    provider_3 = by_id[3]["movement_steps"][3]
    check(provider_3["movement_provider_selector"] == 3
          and provider_3["provider_behavior"]
          == "route_toward_reachable_normal_attack_target"
          and "movement_parameter_1" not in provider_3,
          "RTINIT joins RTN_M003's parameterless attack-route behavior")
    provider_51 = by_id[3]["movement_steps"][1]
    check(provider_51["movement_provider_selector"] == 51
          and provider_51["provider_behavior"]
          == "select_effective_attack_target_and_action"
          and "movement_parameter_1" not in provider_51,
          "RTINIT joins RTN_M051's parameterless attack-selection behavior")
    provider_17 = by_id[3]["movement_steps"][2]
    check(provider_17["movement_provider_selector"] == 17
          and provider_17["provider_behavior"]
          == "route_toward_lowest_hp_reachable_normal_attack_target"
          and "movement_parameter_1" not in provider_17,
          "RTINIT joins RTN_M017's low-HP attack-route behavior")
    provider_52 = by_id[3]["movement_steps"][0]
    check(provider_52["movement_provider_selector"] == 52
          and provider_52["provider_behavior"]
          == "select_lowest_hp_effective_attack_target_and_action"
          and "movement_parameter_1" not in provider_52,
          "RTINIT joins RTN_M052's low-HP attack-selection behavior")
    provider_2 = by_id[2]["movement_steps"][1]
    check(provider_2["movement_provider_selector"] == 2
          and provider_2["provider_behavior"]
          == "roam_to_random_reachable_tile"
          and "movement_parameter_1" not in provider_2,
          "RTINIT joins RTN_M002's randomized roaming behavior")
    provider_9 = by_id[98]["movement_steps"][1]
    check(provider_9["movement_provider_selector"] == 9
          and provider_9["provider_behavior"]
          == "approach_collectible_treasure"
          and "movement_parameter_1" not in provider_9,
          "RTINIT joins RTN_M009's collectible-treasure behavior")
    provider_61 = by_id[33]["movement_steps"][0]
    check(provider_61["movement_provider_selector"] == 61
          and provider_61["provider_behavior"]
          == "select_lowest_hp_ally_and_healing_skill"
          and "movement_parameter_1" not in provider_61,
          "RTINIT joins RTN_M061's immediate healing behavior")
    check(all(
        "provider_behavior" in step
        for record in records
        for step in record.get("movement_steps", [])
    ), "RTINIT joins behavior semantics onto every shipped movement step")
    check(
        meta["movement_provider_parameter_schemas"]["11"]["parameter_fields"]
        == {
            "movement_parameter_1": "destination_tile_x",
            "movement_parameter_2": "destination_tile_y",
            "movement_parameter_3": "waypoint_ordinal",
            "movement_parameter_4": "path_cost_limit_override",
        },
        "RTINIT publishes the reusable RTN_M011 parameter schema",
    )
    check(
        meta["movement_provider_parameter_schemas"]["10"]["parameter_defaults"]
        == {"movement_parameter_1": 0},
        "RTINIT publishes RTN_M010's implicit resource-index default",
    )
    semantics = extract_init.field_semantics(records)
    check(
        semantics["0xeff78/20/0"]
        == "movement_routine_provider_selectors.column_0"
        and semantics["0x125ad8/20/0"]
        == "battle_routine_activation_percents.column_0",
        "RTINIT raw banks join to canonical structural field names",
    )


def test_real_message_tables() -> None:
    scripts = paths.scripts()
    expected = {
        "ITMES.BIN": (0x8C877, 287, "fallthrough"),
        "SKMES.BIN": (0xA6E59, 131, "fallthrough"),
        "VIMES.BIN": (0x15A2A8, 65, "branch-target"),
        "EIMES.BIN": (0x15A759, 192, "branch-target"),
        "CIMES.BIN": (0x15A117, 24, "branch-target"),
        "MAMES.BIN": (0x1560E7, 9, "fallthrough"),
    }
    for name, (selector, count, dispatch_layout) in expected.items():
        records, meta = extract_message_table.extract_messages(
            sys4load.load(scripts[name])
        )
        check(meta["selector_global"] == f"0x{selector:x}",
              f"{name}: discovers selector global 0x{selector:x}")
        check(len(records) == count, f"{name}: extracts {count} messages")
        check(len(records) == meta["dispatch_guard_count"],
              f"{name}: every dispatch guard yields a message")
        check(len({record['id'] for record in records}) == len(records),
              f"{name}: message ids are unique")
        check(meta["dispatch_layout"] == dispatch_layout,
              f"{name}: recognizes its {dispatch_layout} dispatch layout")

    item_messages, _ = extract_message_table.extract_messages(
        sys4load.load(scripts["ITMES.BIN"])
    )
    items = {record["id"]: record for record in item_messages}
    check(items[1]["title"] == "【重要：銅の鍵】　　　　　LEVEL-E",
          "ITMES item 1 keeps its display title")
    check(items[1]["description"] == "　銅の扉を開閉することが可能",
          "ITMES item 1 keeps its player-facing behavior")
    check("濃緑色" in items[32]["title"],
          "ITMES reconstructs furigana surface text inside a title")
    check(items[32]["furigana"][0]["reading"] == "のうりょくしょく",
          "ITMES preserves furigana readings")

    skill_messages, _ = extract_message_table.extract_messages(
        sys4load.load(scripts["SKMES.BIN"])
    )
    skills = {record["id"]: record for record in skill_messages}
    check(skills[1]["title"] == "【移動スキル：飛行】",
          "SKMES skill 1 keeps its display title")
    check(skills[1]["description"] == "　床のない地形を移動可能になる",
          "SKMES skill 1 keeps its player-facing behavior")

    vocabulary_messages, vocabulary_meta = extract_message_table.extract_messages(
        sys4load.load(scripts["VIMES.BIN"])
    )
    vocabulary = {record["id"]: record for record in vocabulary_messages}
    check(vocabulary[1]["title"] == "『【迷宮】占有率』"
          and "各勢力の占領度合" in vocabulary[1]["description"],
          "VIMES follows branch targets and reconstructs glossary help text")
    check(vocabulary_meta["message_layout"] == "title-description",
          "VIMES retains the title/description message layout")

    enemy_messages, enemy_meta = extract_message_table.extract_messages(
        sys4load.load(scripts["EIMES.BIN"])
    )
    enemies = {record["id"]: record for record in enemy_messages}
    check(enemies[101]["summary"] == "高い能力を秘めた隣国の姫騎士"
          and enemies[101]["strategy"] == "初遭遇時にはまず勝てない",
          "EIMES exposes its two lines as enemy summary and strategy")
    check(enemy_meta["message_layout"] == "enemy-commentary"
          and "title" not in enemies[101],
          "EIMES does not mislabel its first commentary line as a title")

    character_messages, character_meta = extract_message_table.extract_messages(
        sys4load.load(scripts["CIMES.BIN"])
    )
    characters = {record["id"]: record for record in character_messages}
    check(
        characters[1]["biography"].startswith("かつては人々を恐怖に陥れた")
        and "title" not in characters[1],
        "CIMES exposes its complete untitled character biography",
    )
    check(
        character_meta["message_layout"] == "character-biography",
        "CIMES records the biography-only message layout",
    )

    magic_messages, magic_meta = extract_message_table.extract_messages(
        sys4load.load(scripts["MAMES.BIN"])
    )
    magic = {record["id"]: record for record in magic_messages}
    check(
        magic_meta["message_layout"] == "description"
        and "title" not in magic[1]
        and len(magic[1]["description"]) > 0,
        "MAMES exposes complete untitled action descriptions",
    )


def test_message_infrastructure() -> None:
    scripts = paths.scripts()
    info = sys4load.load(scripts["INFOMES.BIN"])
    lookups = [
        instruction
        for instruction in info.instructions
        if sys4load.display_label(instruction.opcode) == "lookup-array-2d"
    ]
    check(
        not info.strings
        and len(lookups) == 2
        and all(
            (extract_init.T_GLOBAL_INT, 0x15A097) in instruction.args
            and (extract_init.T_GLOBAL_INT, 0x15A095) in instruction.args
            and (extract_init.T_IMM, 4) in instruction.args
            for instruction in lookups
        ),
        "INFOMES is a text-free 32x4 tab-handler registry walker",
    )
    check(
        any(
            sys4load.display_label(instruction.opcode) == "call-script"
            and instruction.args[0][0] != extract_init.T_IMM
            for instruction in info.instructions
        ),
        "INFOMES invokes registry entries through an indirect call-script",
    )
    init2 = sys4load.load(scripts["INIT2.BIN"])
    initial_handlers = {
        destination: value
        for instruction in init2.instructions
        if (write := extract_init._static_global_write(instruction)) is not None
        for destination, value in [write]
        if 0x15A097 <= destination <= 0x15A099
    }
    check(
        initial_handlers
        == {0x15A097: 0x334A, 0x15A098: 0x334B, 0x15A099: 0x334C},
        "INIT2 installs CIMES, EIMES, and VIMES in handler row zero",
    )
    check(
        any(
            instruction.args
            and instruction.args[0] == (extract_init.T_GLOBAL_INT, 0x15A096)
            and extract_init._static_global_write(instruction) == (0x15A096, 0)
            for instruction in info.instructions
        ),
        "INFOMES clears the first-handler-wins completion flag",
    )

    modal = sys4load.load(scripts["MES.BIN"])
    labels = {
        sys4load.display_label(instruction.opcode)
        for instruction in modal.instructions
    }
    references = {
        operand
        for instruction in modal.instructions
        for operand in instruction.args
    }
    check(
        "show-text" not in labels
        and "draw-string" in labels
        and {
            (extract_init.T_GLOBAL_STRING, 0x7DB),
            (extract_init.T_GLOBAL_INT, 0x665D6),
        } <= references,
        "MES is a generic renderer for caller-populated modal lines",
    )
    check(
        {
            (extract_init.T_GLOBAL_STRING, 0x7E5),
            (extract_init.T_GLOBAL_INT, 0x665E2),
            (extract_init.T_GLOBAL_INT, 0x665E3),
            (extract_init.T_GLOBAL_INT, 0x66647),
        } <= references,
        "MES retains the optional annotation text and placement ABI",
    )
    modal_writes = {
        write
        for instruction in modal.instructions
        if (write := extract_init._static_global_write(instruction)) is not None
    }
    check(
        (0x665D6, 0) in modal_writes and (0x665E2, 0) in modal_writes,
        "MES clears both modal buffers after dismissal",
    )


def test_message_join() -> None:
    scripts = paths.scripts()
    expected = {
        "IT": (287, "　銅の扉を開閉することが可能"),
        "SK": (131, "　床のない地形を移動可能になる"),
    }
    joined = {}
    for prefix, (count, _) in expected.items():
        records, _ = extract_init.extract_name(
            sys4load.load(scripts[f"{prefix}INIT.BIN"])
        )
        meta = extract_init.join_messages(
            records, sys4load.load(scripts[f"{prefix}MES.BIN"])
        )
        check(meta["joined_count"] == count,
              f"{prefix}INIT joins all {count} {prefix}MES messages")
        check(not meta["init_ids_without_message"] and not meta["message_ids_without_init"],
              f"{prefix}INIT and {prefix}MES ids match exactly")
        joined[prefix] = {record["id"]: record for record in records}
    check(joined["IT"][1]["message"]["description"] == expected["IT"][1],
          "INIT/MES join uses the shared runtime id")

    vocabulary, _ = extract_init.extract_vocabulary(
        sys4load.load(scripts["VIINIT.BIN"])
    )
    vocabulary_meta = extract_init.join_messages(
        vocabulary, sys4load.load(scripts["VIMES.BIN"])
    )
    check(
        vocabulary_meta["joined_count"] == 65
        and not vocabulary_meta["init_ids_without_message"]
        and not vocabulary_meta["message_ids_without_init"],
        "VIINIT and VIMES form a complete 65-topic runtime-id join",
    )

    units, _ = extract_init.extract_name(sys4load.load(scripts["EBINIT.BIN"]))
    enemy_meta = extract_init.join_messages(
        units, sys4load.load(scripts["EIMES.BIN"])
    )
    unit_by_id = {record["id"]: record for record in units}
    check(
        enemy_meta["joined_count"] == 192
        and len(enemy_meta["init_ids_without_message"]) == 85
        and not enemy_meta["message_ids_without_init"],
        "EIMES joins 192 sparse enemy-commentary rows to EBINIT",
    )
    check(
        unit_by_id[101]["message"]["strategy"] == "初遭遇時にはまず勝てない",
        "EBINIT records expose EIMES strategy text by unit id",
    )

    characters, _ = extract_init.extract_character_profiles(
        sys4load.load(scripts["CIINIT.BIN"])
    )
    character_meta = extract_init.join_messages(
        characters, sys4load.load(scripts["CIMES.BIN"])
    )
    character_by_id = {record["id"]: record for record in characters}
    check(
        character_meta["joined_count"] == 24
        and not character_meta["init_ids_without_message"]
        and not character_meta["message_ids_without_init"],
        "CIINIT and CIMES form a complete 24-profile runtime-id join",
    )
    check(
        character_by_id[1]["message"]["biography"].startswith(
            "かつては人々を恐怖に陥れた"
        ),
        "CIINIT records expose CIMES biography text by profile id",
    )

    magic_actions, _ = extract_init.extract_magic_actions(
        sys4load.load(scripts["MAINIT.BIN"])
    )
    magic_meta = extract_init.join_messages(
        magic_actions, sys4load.load(scripts["MAMES.BIN"])
    )
    magic_by_id = {record["id"]: record for record in magic_actions}
    check(
        magic_meta["joined_count"] == 9
        and magic_meta["init_ids_without_message"] == [10, 11]
        and not magic_meta["message_ids_without_init"],
        "MAINIT and MAMES form the expected sparse 9-of-11 action join",
    )
    check(
        magic_by_id[1]["message"]["description"]
        and "title" not in magic_by_id[1]["message"],
        "MAINIT records expose MAMES descriptions by action id",
    )


def test_character_names() -> None:
    scripts = paths.scripts()
    records, meta = extract_init.extract_character_names(
        sys4load.load(scripts["CNINIT.BIN"])
    )
    by_id = {record["id"]: record for record in records}
    check(
        len(records) == 277
        and meta["record_span"] == 1000
        and meta["integer_write_count"] == 277
        and meta["string_write_count"] == 274,
        "CNINIT extracts its complete sparse 1,000-row pair of arrays",
    )
    check(
        meta["joined_unit_definition_count"] == 277
        and not meta["cninit_ids_without_unit_definition"]
        and not meta["unit_definition_ids_without_cninit"],
        "CNINIT ids join exactly to all 277 EBINIT definitions",
    )
    check(
        meta["unnamed_record_ids"] == [2, 3, 4]
        and all(by_id[unit_id]["name"] is None for unit_id in (2, 3, 4)),
        "CNINIT preserves Lily's three deliberately unnamed form rows",
    )
    check(
        by_id[1]["name"] == "エミリオ"
        and by_id[1]["fields"]["0x624bf"] == 1
        and by_id[1]["string_fields"]["0x315"] == "エミリオ",
        "CNINIT keeps display names and voice-family ids as parallel fields",
    )
    check(
        meta["voice_family_alias_count"] == 175
        and by_id[21]["canonical_voice_unit_id"] == 5
        and by_id[21]["unit_definition_name"] == "シルフィーヌ（洗脳状態）"
        and by_id[21]["canonical_voice_unit_name"] == "シルフィーヌ",
        "CNINIT joins variant rows to their canonical voice-family definitions",
    )
    check(
        by_id[800]["name"] == "アセンブリア"
        and by_id[800]["canonical_voice_unit_id"] == 240,
        "CNINIT preserves late EX/BOSS aliases without collapsing sparse ids",
    )

    semantics = extract_init.field_semantics(records)
    check(
        semantics == {
            "0x315": "unit_story_display_names",
            "0x624bf": "unit_voice_family_unit_ids",
        },
        "CNINIT's two raw arrays join to their canonical semantic names",
    )
    extract_init.attach_semantic_fields(records, semantics)
    check(
        by_id[21]["semantic_fields"]["unit_story_display_names"] == "シルフィーヌ"
        and by_id[21]["semantic_fields"]["unit_voice_family_unit_ids"] == 5,
        "CNINIT records retain raw arrays beside one semantic view",
    )


def test_gallery_definitions() -> None:
    scripts = paths.scripts()
    script = sys4load.load(scripts["CGINIT.BIN"])
    check(
        extract_init.detect_mode(script) == "numeric",
        "CGINIT remains compatible with numeric-mode auto-detection",
    )
    records, meta = extract_init.extract_gallery_definitions(script)
    by_id = {record["id"]: record for record in records}
    check(
        len(records) == 851
        and meta["record_span"] == 2000
        and meta["populated_id_range"] == [1, 855]
        and meta["id_gaps_within_populated_range"] == [205, 585, 603, 604],
        "CGINIT extracts 851 sparse gallery rows from its reserved 2,000 ids",
    )
    check(
        meta["static_write_count"] == 3941
        and meta["classified_static_write_count"] == 3941
        and meta["preview_asset_count"] == 537,
        "CGINIT classifies every integer write and all optional previews",
    )
    check(
        [sheet["asset_name"] for sheet in meta["thumbnail_sheets"]]
        == ["SO026A.AGF", "SO026B.AGF", "SO026C.AGF", "SO026D.AGF"]
        and all(sheet["slot_count"] == 30 for sheet in meta["thumbnail_sheets"]),
        "CGINIT joins the four configured 6x5 thumbnail atlases",
    )
    check(
        by_id[1]["gallery_image_asset_id"] == 5407
        and by_id[1]["gallery_image_asset_name"] == "EV001AA.AGF"
        and by_id[1]["thumbnail_sheet_id"] == 1
        and by_id[1]["thumbnail_slot_id"] == 1
        and by_id[1]["variant_ordinal"] == 3,
        "CGINIT row 1 joins its full image and thumbnail placement",
    )
    check(
        by_id[4]["save_stage_preview_asset_id"] == 11266
        and by_id[4]["save_stage_preview_asset_name"] == "EVM001BA.AGF"
        and "save_stage_preview_asset_id" not in by_id[1],
        "CGINIT preserves optional SAVE/SELSTAGE preview assets",
    )

    semantics = extract_init.field_semantics(records)
    check(
        semantics == {
            "0x62cd1/2/0": "gallery_image_assets.gallery_image_asset_id",
            "0x62cd1/2/1": "gallery_image_assets.save_stage_preview_asset_id",
            "0x63c71": "gallery_thumbnail_sheet_ids",
            "0x64441": "gallery_thumbnail_slot_ids",
            "0x64c11": "gallery_variant_ordinals",
        },
        "CGINIT raw arrays join to their canonical semantic names",
    )
    extract_init.attach_semantic_fields(records, semantics)
    check(
        by_id[4]["semantic_fields"][
            "gallery_image_assets.save_stage_preview_asset_id"
        ] == 11266
        and by_id[4]["semantic_fields"]["gallery_thumbnail_slot_ids"] == 1,
        "CGINIT retains raw addresses beside one semantic field view",
    )


def test_alchemy_recipes() -> None:
    scripts = paths.scripts()
    script = sys4load.load(scripts["ALINIT.BIN"])
    check(
        extract_init.detect_mode(script) == "numeric",
        "ALINIT remains compatible with numeric-mode auto-detection",
    )
    records, meta = extract_init.extract_alchemy_recipes(script)
    by_id = {record["id"]: record for record in records}
    check(
        len(records) == 107
        and meta["record_span"] == 1000
        and meta["populated_id_range"] == [1, 467],
        "ALINIT extracts 107 sparse recipes from its reserved 1,000 ids",
    )
    check(
        meta["static_write_count"] == 914
        and meta["classified_static_write_count"] == 914
        and len(meta["record_field_columns"]) == 10,
        "ALINIT classifies every scalar and populated row-table write",
    )
    check(
        meta["output_item_join_count"] == 107
        and meta["ingredient_reference_count"] == 286
        and meta["joined_ingredient_reference_count"] == 286,
        "ALINIT resolves every output and ingredient reference through ITINIT",
    )
    check(
        by_id[1]["output_item_id"] == 2
        and by_id[1]["output_item_name"] == "白銀の鍵"
        and by_id[1]["minimum_alchemy_level"] == 2
        and by_id[1]["point_cost"] == 30
        and by_id[1]["required_story_flag_ids"] == [1902]
        and by_id[1]["forbidden_story_flag_ids"] == [1903],
        "ALINIT recipe 1 exposes its output, gating, and point cost",
    )
    check(
        [
            (ingredient["slot"], ingredient["item_id"], ingredient["quantity"])
            for ingredient in by_id[1]["ingredients"]
        ] == [(1, 608, 1), (2, 625, 1)]
        and by_id[14]["output_item_name"] == "シルバーコイン"
        and [
            (ingredient["slot"], ingredient["item_id"], ingredient["quantity"])
            for ingredient in by_id[14]["ingredients"]
        ] == [(0, 91, 5)],
        "ALINIT preserves sparse ingredient slots and their paired quantities",
    )

    semantics = extract_init.field_semantics(records)
    check(
        semantics == {
            "0x156214": "alchemy_recipe_output_item_ids",
            "0x1565fc": "alchemy_recipe_minimum_levels",
            "0x1569e4/2/0": (
                "alchemy_recipe_required_story_flags.required_flag_1"
            ),
            "0x1571b4/2/0": (
                "alchemy_recipe_forbidden_story_flags.forbidden_flag_1"
            ),
            "0x157d6c": "alchemy_recipe_point_costs",
            "0x158154/4/0": (
                "alchemy_recipe_ingredient_item_ids.ingredient_1"
            ),
            "0x158154/4/1": (
                "alchemy_recipe_ingredient_item_ids.ingredient_2"
            ),
            "0x158154/4/2": (
                "alchemy_recipe_ingredient_item_ids.ingredient_3"
            ),
            "0x158154/4/3": (
                "alchemy_recipe_ingredient_item_ids.ingredient_4"
            ),
            "0x1590f4/4/0": (
                "alchemy_recipe_ingredient_quantities.ingredient_1"
            ),
            "0x1590f4/4/1": (
                "alchemy_recipe_ingredient_quantities.ingredient_2"
            ),
            "0x1590f4/4/2": (
                "alchemy_recipe_ingredient_quantities.ingredient_3"
            ),
            "0x1590f4/4/3": (
                "alchemy_recipe_ingredient_quantities.ingredient_4"
            ),
        },
        "ALINIT raw arrays join to their canonical semantic names",
    )
    extract_init.attach_semantic_fields(records, semantics)
    check(
        by_id[1]["semantic_fields"][
            "alchemy_recipe_required_story_flags.required_flag_1"
        ] == 1902
        and by_id[14]["semantic_fields"][
            "alchemy_recipe_ingredient_quantities.ingredient_1"
        ] == 5,
        "ALINIT retains raw cells beside one semantic recipe view",
    )


def test_affinity_definitions() -> None:
    scripts = paths.scripts()
    script = sys4load.load(scripts["AFINIT.BIN"])
    check(
        extract_init.detect_mode(script) == "name",
        "AFINIT remains compatible with name-mode auto-detection",
    )
    records, meta = extract_init.extract_affinity_definitions(script)
    by_id = {record["id"]: record for record in records}
    check(
        len(records) == 13
        and meta["string_write_count"] == 27
        and meta["footer_array_count"] == 54
        and meta["classified_instruction_count"] == 82,
        "AFINIT classifies every vocabulary, footer-array, and exit instruction",
    )
    check(
        [entry["id"] for entry in meta["attack_element_names"]]
        == [*range(1, 9), *range(11, 18)]
        and [entry["id"] for entry in meta["defense_element_names"]]
        == list(range(1, 13)),
        "AFINIT preserves its sparse attack and defense element vocabularies",
    )
    check(
        by_id[3]["name"] == "火炎"
        and by_id[3]["attack_effectiveness"][3]["percent"] == -100
        and by_id[3]["attack_effectiveness"][4]["percent"] == 150
        and by_id[11]["attack_effectiveness"][1]["percent"] == 1
        and by_id[11]["attack_effectiveness"][7]["percent"] == 200,
        "AFINIT exposes signed elemental immunities, weaknesses, and resistances",
    )

    tuning = {
        curve["curve_id"]: curve for curve in meta["item_tuning_curves"]
    }
    check(
        len(tuning) == 19
        and tuning[1]["level_bonuses"] == [1, 0, 0, 0, 0, 0, 0, 0, 0, 0]
        and tuning[9]["level_bonuses"] == list(range(1, 11))
        and tuning[18]["level_bonuses"] == list(range(3, 31, 3))
        and tuning[18]["level_costs"]
        == [10, 25, 45, 70, 100, 140, 190, 250, 320, 400]
        and tuning[19]["level_bonuses"] == [0] * 10
        and tuning[19]["level_costs"] == [0] * 10
        and meta["usable_item_tuning_curve_ids"] == list(range(1, 19))
        and meta["reserved_item_tuning_curve_ids"] == [19],
        "AFINIT pairs all nineteen item-tuning bonus and point-cost curves",
    )
    check(
        [
            row["level_progress_thresholds"]
            for row in meta["facility_level_thresholds"]
        ] == [
            [40, 80, 120, 160, 200, 300],
            [20, 40, 60, 90, 120, 200],
            [20, 50, 100, 150, 200, 400],
        ],
        "AFINIT exposes the tuning, alchemy, and magic progression rows",
    )

    semantics = extract_init.field_semantics(
        records, meta["array_layouts"]
    )
    check(
        len(semantics) == 14
        and semantics["0x26a4"] == "defense_element_names"
        and semantics["0xab5ba/0"]
        == "attack_element_effectiveness_percent.row_0"
        and semantics["0xab5ba/240"]
        == "attack_element_effectiveness_percent.row_12",
        "AFINIT raw vocabulary and matrix rows join to canonical semantics",
    )
    extract_init.attach_semantic_fields(records, semantics)
    check(
        by_id[3]["semantic_fields"]["defense_element_names"] == "火炎"
        and by_id[3]["semantic_fields"][
            "attack_element_effectiveness_percent.row_3"
        ][3] == -100,
        "AFINIT retains raw footer provenance beside signed semantic rows",
    )


def test_name_entry_palette() -> None:
    scripts = paths.scripts()
    script = sys4load.load(scripts["CTINIT.BIN"])
    check(
        extract_init.detect_mode(script) == "name",
        "CTINIT remains compatible with name-mode auto-detection",
    )
    records, meta = extract_init.extract_name_entry_palette(script)
    by_id = {record["id"]: record for record in records}
    check(
        len(records) == 5
        and meta["reserved_shape"] == [5, 70]
        and meta["string_write_count"] == 273
        and meta["classified_instruction_count"] == 274,
        "CTINIT classifies all five reserved palette pages and every instruction",
    )
    check(
        meta["row_names"]
        == ["hiragana", "katakana", "latin", "numerals", "symbols"]
        and meta["populated_cells_per_row"] == [56, 56, 52, 40, 69],
        "CTINIT names each page and preserves its authored cell population",
    )
    check(
        by_id[0]["characters"][0] == "あ"
        and by_id[0]["characters"][17] is None
        and by_id[1]["characters"][50] == "ァ"
        and by_id[2]["characters"][0] == "Ａ"
        and by_id[2]["characters"][30] == "ａ"
        and by_id[3]["characters"][20] == "Ⅰ"
        and by_id[3]["characters"][30] == "①"
        and by_id[4]["characters"][68] == "ω"
        and by_id[4]["characters"][69] is None,
        "CTINIT retains representative characters and intentional empty slots",
    )

    semantics = extract_init.field_semantics(records)
    check(
        len(semantics) == 69
        and semantics["0x43dd/70/0"]
        == "name_entry_character_palette.column_0"
        and semantics["0x43dd/70/68"]
        == "name_entry_character_palette.column_68",
        "CTINIT raw palette slots join to one canonical table name",
    )
    extract_init.attach_semantic_fields(records, semantics)
    check(
        by_id[0]["semantic_fields"][
            "name_entry_character_palette.column_0"
        ] == "あ"
        and by_id[4]["semantic_fields"][
            "name_entry_character_palette.column_68"
        ] == "ω",
        "CTINIT retains raw cells beside the semantic palette view",
    )


def test_voice_configuration() -> None:
    scripts = paths.scripts()
    script = sys4load.load(scripts["CVINIT.BIN"])
    check(
        extract_init.detect_mode(script) == "numeric",
        "CVINIT remains compatible with numeric-mode auto-detection",
    )
    records, meta = extract_init.extract_voice_configuration(script)
    by_id = {record["id"]: record for record in records}
    check(
        len(records) == 13
        and meta["static_write_count"] == 37
        and meta["classified_static_write_count"] == 37
        and meta["classified_instruction_count"] == 38,
        "CVINIT classifies every preview, forward-map, inverse-map, and exit instruction",
    )
    check(
        [
            record["preview_voice_asset_name"]
            for record in records
        ] == [
            "EUA0065.OGG",
            "LILA1424.OGG",
            "SYL0675.OGG",
            "SAS0328.OGG",
            "VID0345.OGG",
            "EST0562.OGG",
            "NEL0192.OGG",
            "TIO0209.OGG",
            "COL0836.OGG",
            "BRI0687.OGG",
            "OKT0357.OGG",
            "FEM0328.OGG",
            "DEI0501.OGG",
        ],
        "CVINIT joins all thirteen preview clips to shipped OGG assets",
    )
    check(
        [by_id[slot]["unit_id"] for slot in range(1, 13)]
        == [2, 5, 6, 7, 12, 14, 13, 10, 8, 9, 15, 11]
        and meta["unit_join_count"] == 12,
        "CVINIT joins its twelve named configuration slots to EBINIT units",
    )
    check(
        all(
            by_id[slot]["array_fields"][
                f"0x628a7/{by_id[slot]['unit_id']}"
            ] == slot
            for slot in range(1, 13)
        )
        and meta["round_trip_mapping_count"] == 12,
        "CVINIT's slot-to-unit and unit-to-setting maps round-trip exactly",
    )
    check(
        by_id[0]["slot_kind"] == "system"
        and "unit_id" not in by_id[0]
        and by_id[1]["speaker_seen_flag_address"] == "0x56225"
        and by_id[12]["speaker_seen_flag_address"] == "0x5622e",
        "CVINIT preserves the non-unit system slot and speaker-seen joins",
    )

    semantics = extract_init.field_semantics(
        records, meta["array_layouts"]
    )
    check(
        len(semantics) == 14
        and semantics["0x62cad"] == "character_voice_preview_asset_ids"
        and semantics["0x62c8f"] == "character_voice_setting_unit_ids"
        and semantics["0x628a7/11"]
        == "unit_voice_suppression_flag_ids.index_11",
        "CVINIT raw arrays join to their canonical voice-setting semantics",
    )
    extract_init.attach_semantic_fields(records, semantics)
    check(
        by_id[12]["semantic_fields"][
            "character_voice_preview_asset_ids"
        ] == 11412
        and by_id[12]["semantic_fields"][
            "character_voice_setting_unit_ids"
        ] == 11
        and by_id[12]["semantic_fields"][
            "unit_voice_suppression_flag_ids.index_11"
        ] == 12,
        "CVINIT retains raw coordinates beside its joined semantic view",
    )


def test_map_terrain_atlas() -> None:
    scripts = paths.scripts()
    script = sys4load.load(scripts["MPINIT.BIN"])
    check(
        extract_init.detect_mode(script) == "footer",
        "MPINIT remains compatible with footer-mode auto-detection",
    )
    records, meta = extract_init.extract_map_terrain_atlas(script)
    by_y = {record["grid_y"]: record for record in records}
    check(
        len(records) == 1472
        and meta["footer_array_count"] == 1472
        and meta["classified_instruction_count"] == 1473,
        "MPINIT classifies every terrain-row footer copy and exit instruction",
    )
    check(
        meta["atlas_base"] == "0xccc93"
        and meta["row_stride"] == 53
        and meta["first_authored_column"] == 1
        and meta["authored_column_count"] == 50
        and meta["tile_to_grid_scale"] == 2,
        "MPINIT exposes its 53-cell row pitch and doubled tile coordinate system",
    )
    check(
        meta["authored_grid_y_min"] == 2
        and meta["authored_grid_y_max"] == 1600
        and meta["implicit_zero_row_count"] == 127
        and all(record["length"] == 50 for record in records),
        "MPINIT preserves all authored rows and the omitted all-zero row gaps",
    )
    check(
        by_y[2]["global_addr"] == "0xcccfe"
        and by_y[1600]["global_addr"] == "0xe17d4"
        and by_y[1199]["terrain_ids_used"] == [6]
        and by_y[1199]["nonzero_cell_count"] == 25,
        "MPINIT row coordinates recover directly from destination addresses",
    )

    definitions = {row["id"]: row for row in meta["terrain_definitions"]}
    check(
        definitions[1]["name"] == "通路"
        and definitions[1]["layout_class_name"] == "passage"
        and definitions[2]["name"] == "部屋"
        and definitions[2]["area_fill_flag"] == 1
        and definitions[3]["name"] == "隠し通路"
        and definitions[3]["layout_class_name"] == "hidden"
        and definitions[15]["name"] == "溶岩流"
        and definitions[15]["texture_slot_index"] == 9,
        "MPINIT terrain ids join to LAINIT names and layout/render classes",
    )

    stage_maps = {stage["id"]: stage for stage in meta["stage_maps"]}
    stage1 = stage_maps[1]
    check(
        meta["stage_map_count"] == 66
        and meta["unique_atlas_rectangle_count"] == 53
        and stage1["name"] == "『庭園の地下空洞』"
        and stage1["tile_bounds"]
        == {"min_x": 9, "max_x": 17, "min_y": 1, "max_y": 8}
        and stage1["grid_bounds"]
        == {"min_x": 18, "max_x": 34, "min_y": 2, "max_y": 16},
        "STINIT2 bounds join 66 stage definitions to their doubled atlas rectangles",
    )
    check(
        stage1["grid_width"] == 17
        and stage1["grid_height"] == 15
        and stage1["terrain_ids_used"] == [1, 2, 4, 12]
        and stage1["terrain_id_counts"]
        == {"0": 212, "1": 12, "2": 13, "4": 2, "12": 16},
        "stage joins expose complete terrain grids and value populations",
    )
    check(
        any(
            shared["stage_ids"] == [32, 33, 34]
            for shared in meta["shared_atlas_rectangles"]
        )
        and any(
            shared["stage_ids"] == [101, 104, 106, 107, 108]
            for shared in meta["shared_atlas_rectangles"]
        ),
        "MPINIT preserves intentional atlas sharing across stage variants",
    )
    check(
        meta["nonzero_cell_count"] == 17126
        and meta["stage_rectangle_nonzero_cell_count"] == 17079
        and meta["outside_stage_rectangle_nonzero_cell_count"] == 47,
        "MPINIT accounts for stage terrain and the raw border-context cells",
    )


def test_terrain_definitions() -> None:
    scripts = paths.scripts()
    script = sys4load.load(scripts["LAINIT.BIN"])
    check(
        extract_init.detect_mode(script) == "name",
        "LAINIT remains compatible with name-mode auto-detection",
    )
    records, meta = extract_init.extract_terrain_definitions(script)
    by_id = {record["id"]: record for record in records}
    check(
        len(records) == 20
        and meta["reserved_record_span"] == 30
        and meta["authored_terrain_ids"]
        == [1, 2, 3, 4, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19]
        and meta["implicit_default_terrain_ids"] == [0, 5, 6],
        "LAINIT exposes the shipped terrain ids and reserved definition span",
    )
    check(
        meta["string_write_count"] == 22
        and meta["static_write_count"] == 75
        and meta["classified_static_write_count"] == 75
        and meta["classified_instruction_count"] == 98,
        "LAINIT classifies every string, numeric, and exit instruction",
    )
    check(
        by_id[1]["name"] == "通路"
        and by_id[1]["texture_slot_index"] == 1
        and by_id[1]["layout_class_name"] == "passage"
        and by_id[2]["name"] == "部屋"
        and by_id[2]["area_fill_flag"] == 1
        and by_id[3]["name"] == "隠し通路"
        and by_id[3]["layout_class_name"] == "hidden",
        "LAINIT preserves terrain names, texture slots, fill flags, and classes",
    )
    check(
        by_id[4]["effect_description"] == "▲命中・回避・防御"
        and by_id[4]["combat_stat_deltas"]
        == {
            "accuracy": 5,
            "evasion": 5,
            "physical_defense": 1,
            "magic_defense": 1,
        }
        and by_id[7]["combat_stat_deltas"]
        == {"accuracy": -5, "evasion": -5, "speed": -3}
        and by_id[10]["combat_stat_deltas"]
        == {"accuracy": 10, "evasion": 10}
        and meta["combat_stat_cell_count"] == 13,
        "LAINIT effect text agrees with every populated combat-stat delta",
    )
    check(
        by_id[3]["required_skill_id"] == 3
        and by_id[3]["required_skill_name"] == "探索"
        and by_id[7]["required_skill_name"] == "潜水"
        and by_id[9]["required_skill_name"] == "飛行"
        and by_id[15]["required_skill_name"] == "耐熱"
        and meta["required_skill_count"] == 5,
        "LAINIT traversal gates resolve through SKINIT skill definitions",
    )
    authored_slots = [
        slot for slot in meta["texture_slots"] if slot["authored"]
    ]
    check(
        len(authored_slots) == 10
        and authored_slots[0]["id"] == 1
        and authored_slots[0]["default_asset_name"] == "MP000A.AGF"
        and authored_slots[-1]["id"] == 13
        and authored_slots[-1]["default_asset_name"] == "MP000N.AGF"
        and by_id[15]["default_texture_asset_name"] == "MP000I.AGF"
        and meta["default_texture_asset_join_count"] == 10,
        "LAINIT texture slots join every fallback asset to SYS4INI",
    )


def test_h_scene_gallery() -> None:
    scripts = paths.scripts()
    script = sys4load.load(scripts["SPINIT.BIN"])
    check(
        extract_init.detect_mode(script) == "numeric",
        "SPINIT remains compatible with numeric-mode auto-detection",
    )
    records, meta = extract_init.extract_h_scene_gallery(script)
    check(
        len(records) == 8
        and meta["page_count"] == 8
        and meta["slots_per_page"] == 15
        and meta["registry_capacity"] == 120,
        "SPINIT exposes HMODE's eight-by-fifteen page geometry",
    )
    check(
        meta["static_write_count"] == 118
        and meta["classified_static_write_count"] == 118
        and meta["classified_instruction_count"] == 119
        and meta["empty_cells"]
        == [{"page": 7, "slot": 13}, {"page": 7, "slot": 14}],
        "SPINIT classifies every scene cell and the two implicit empty slots",
    )
    check(
        records[0]["thumbnail_sheet_asset_name"] == "SO027A.AGF"
        and records[7]["thumbnail_sheet_asset_name"] == "SO027H.AGF"
        and meta["resolved_thumbnail_sheet_count"] == 8,
        "SPINIT pages join to all eight INIT2 HMODE thumbnail sheets",
    )
    check(
        records[0]["scenes"][0]
        == {
            "slot": 0,
            "script_resource_id": 0x151E,
            "script_name": "SP0800.BIN",
        }
        and records[2]["scenes"][3]["script_name"] == "SP1200.BIN"
        and records[7]["scenes"][-1]["script_name"] == "SP0179.BIN"
        and meta["resolved_scene_script_count"] == 118,
        "SPINIT resolves every populated cell to its call-script resource",
    )
    check(
        len(records[7]["script_resource_ids"]) == 15
        and records[7]["script_resource_ids"][-2:] == [0, 0]
        and len(records[7]["record_fields"]) == 13,
        "SPINIT preserves complete rows beside authored raw-cell provenance",
    )


def test_training_actions() -> None:
    scripts = paths.scripts()
    script = sys4load.load(scripts["TRINIT.BIN"])
    records, meta = extract_init.extract_training_actions(script)
    by_id = {record["id"]: record for record in records}
    check(
        len(records) == 21
        and meta["reserved_record_count"] == 21
        and meta["string_stride"] == 6
        and meta["numeric_block_start"] == "0x155bbc"
        and meta["numeric_block_end_exclusive"] == "0x1560e7",
        "TRINIT exposes its 21-row text and contiguous numeric geometry",
    )
    check(
        meta["string_write_count"] == 75
        and meta["static_write_count"] == 289
        and meta["classified_static_write_count"] == 289
        and meta["classified_instruction_count"] == 365,
        "TRINIT classifies every text, numeric, and exit instruction",
    )
    check(
        by_id[0]["description_lines"]
        == [
            "使い魔と性魔術を行い、能力を高める。",
            "精気20必要。『捕獲攻撃』獲得。",
        ]
        and by_id[1]["locked_hint_lines"]
        == [
            "使い魔の成長や特別なアイテムが必要の",
            "ようだ……。",
            "作る為の方法と材料は……。",
        ]
        and by_id[15]["description_lines"][2]
        == "さらに最大精気+2。",
        "TRINIT preserves the available and locked three-line text families",
    )
    check(
        by_id[0]["eligibility"]["minimum_unit_level"] == 3
        and by_id[4]["eligibility"]["minimum_alignment"] == 15
        and by_id[13]["eligibility"]["maximum_alignment"] == -75
        and by_id[13]["eligibility"]["minimum_training_progress"] == 45
        and by_id[1]["eligibility"]["required_item_name"]
        == "マタタビの媚薬",
        "TRINIT decodes level, alignment, training, and ITINIT gates",
    )
    check(
        by_id[0]["effects"]["spirit_cost"] == 20
        and by_id[0]["effects"]["unit_stat_deltas"]
        == {
            "physical_attack": 7,
            "physical_defense": 4,
            "speed": 8,
            "luck": 2,
            "max_hp": 15,
            "max_sp": 12,
            "max_fs": 6,
        }
        and by_id[0]["effects"]["awarded_skill_name"] == "捕獲攻撃"
        and by_id[13]["effects"]["alignment_delta_hundredths"] == -2000
        and by_id[13]["effects"]["awarded_item_name"] == "死王の喚石",
        "TRINIT joins spirit, stat, alignment, skill, and item effects",
    )
    check(
        meta["event_cell_count"] == 75
        and len(meta["distinct_event_story_flag_ids"]) == 38
        and meta["resolved_event_dispatch_count"] == 75
        and by_id[0]["execution_limit"] == 6
        and by_id[0]["event_story_flag_ids"]
        == [800, 830, 830, 830, 830, 830, 0, 0, 0, 0]
        and by_id[0]["events"][0]["script_name"] == "SC0800.BIN"
        and by_id[0]["events"][1]["script_name"] == "SC0830.BIN",
        "TRINIT event slots join to SCINIT and retain repeat-scene limits",
    )
    check(
        meta["required_item_join_count"] == 8
        and meta["awarded_item_join_count"] == 3
        and meta["awarded_skill_join_count"] == 8
        and meta["authored_numeric_cell_counts"]["unit_stat_deltas"] == 95
        and meta["authored_numeric_cell_counts"]["event_story_flag_ids"]
        == 75,
        "TRINIT accounts for every definition join and populated field family",
    )


def test_card_generation_lists() -> None:
    scripts = paths.scripts()
    script = sys4load.load(scripts["CDINIT.BIN"])
    check(
        extract_init.detect_mode(script) == "numeric",
        "CDINIT remains compatible with numeric-mode auto-detection",
    )
    records, meta = extract_init.extract_card_generation_lists(script)
    by_id = {record["id"]: record for record in records}
    check(
        list(by_id) == [1, 11, 31, 41, 55, 61, 71, 94, 160]
        and [record["entry_count"] for record in records]
        == [11, 36, 38, 51, 26, 52, 64, 30, 75],
        "CDINIT exposes all nine selector branches and their candidate counts",
    )
    check(
        meta["entry_count"] == 383
        and len(meta["distinct_card_ids"]) == 81
        and meta["resolved_card_reference_count"] == 383
        and meta["classified_instruction_count"] == 1565,
        "CDINIT classifies every instruction and resolves every card reference",
    )
    first = by_id[1]["entries"][0]
    check(
        first["slot"] == 1
        and first["card_id"] == 1
        and first["card_name"] == "癒しのカード・小"
        and first["base_weight"] == 25
        and first["growth_interval_turns"] == 5
        and first["growth_weight"] == 1
        and first["source_addresses"]
        == {
            "card_id": "0x1525b3",
            "base_weight": "0x152489",
            "growth_interval_turns": "0x15248a",
            "growth_weight": "0x15248b",
        },
        "CDINIT retains the parallel card-id and weight-table provenance",
    )
    gated = next(
        entry
        for entry in by_id[11]["entries"]
        if entry["card_id"] == 14
    )
    check(
        gated["card_name"] == "使い魔のカード"
        and gated["required_story_flag_ids"] == [901]
        and gated["forbidden_story_flag_ids"] == [861],
        "CDINIT entries join CDINIT2 names and FIELD story-flag gates",
    )
    ignored_gate = next(
        entry
        for entry in by_id[41]["entries"]
        if entry["card_id"] == 18
    )
    check(
        meta["ignored_required_story_flag_definition_count"] == 18
        and meta["ignored_required_story_flag_entry_count"] == 54
        and ignored_gate["required_story_flag_ids"] == [901, 863]
        and ignored_gate["ignored_required_story_flag_ids"] == [51],
        "CDINIT distinguishes FIELD's two live required flags from column three",
    )
    check(
        meta["used_selector_ids"] == [1, 11, 31, 41, 61, 71, 160]
        and meta["unreferenced_selector_ids"] == [55, 94]
        and meta["stage_definition_reference_count"] == 50
        and meta["stage_object_reference_count"] == 246
        and by_id[1]["stage_object_references"]
        == [{"stage_id": 1, "object_slots": [8]}],
        "CDINIT joins every used list back to STINIT type-28 stage objects",
    )
    check(
        meta["runtime_scan_capacity"] == 100
        and meta["cleared_entry_prefix"] == 50
        and by_id[160]["entry_count"] == 75
        and meta["fallback_comment"]
        == "カード発生リストの設定が不足しています",
        "CDINIT preserves its 100-slot scan, 50-slot clear, and fallback warning",
    )


def test_condition_definitions() -> None:
    scripts = paths.scripts()
    script = sys4load.load(scripts["ILINIT.BIN"])
    check(
        extract_init.detect_mode(script) == "name",
        "ILINIT remains compatible with name-mode auto-detection",
    )
    records, meta = extract_init.extract_condition_definitions(script)
    by_id = {record["id"]: record for record in records}
    check(
        list(by_id) == [*range(1, 12), 13, 14],
        "ILINIT extracts all thirteen authored condition ids",
    )
    check(
        meta["record_span"] == 30
        and meta["level_count"] == 5
        and meta["reserved_condition_ids"] == [12, *range(15, 30)],
        "ILINIT exposes its complete 30-by-5 reserved layout",
    )
    check(
        meta["static_write_count"] == 228
        and meta["classified_static_write_count"] == 228,
        "ILINIT classifies every static integer write",
    )
    check(
        by_id[1]["name"] == "即死"
        and by_id[2]["name"] == "ＨＰ吸"
        and by_id[14]["condition"] == "exaltation",
        "condition records preserve authored names and canonical ids",
    )
    check(
        by_id[2]["string_fields"]["0x25fa/5/0"] == "ＨＰ吸1"
        and by_id[2]["string_fields"]["0x25fa/5/4"] == "ＨＰ吸5",
        "condition level names retain raw base/stride/column provenance",
    )
    check(
        [level["duration_turns"] for level in by_id[6]["levels"]]
        == [2, 3, 4, 5, 6]
        and by_id[6]["fields"]["0xaacb4"] == 1
        and by_id[6]["fields"]["0xaacd2"] == 10,
        "charm exposes level durations, RECOVER policy, and icon id",
    )
    check(
        by_id[5]["levels"][4]["stat_deltas"]["accuracy"] == -25
        and by_id[14]["levels"][4]["stat_deltas"]["physical_attack"] == 10
        and by_id[14]["levels"][4]["stat_deltas"]["physical_defense"] == -10,
        "curse and exaltation expose their five-level stat matrices",
    )
    check(
        by_id[9]["levels"][4]["resource_deltas"]["hp"] == -5
        and by_id[13]["levels"][4]["resource_deltas"]["hp"] == 5,
        "poison and regeneration expose opposing periodic HP deltas",
    )

    recovery = meta["recovery_protocol"]
    check(
        recovery["resource_restore"]["source_columns"]
        == ["max_hp", "max_sp", "max_fs"]
        and recovery["resource_restore"]["destination_columns"]
        == ["current_hp", "current_sp", "current_fs"],
        "RECOVER joins max-stat columns to current HP/SP/FS",
    )
    check(
        recovery["condition_reset"]["current_level_table"] == "0x52383"
        and recovery["condition_reset"]["baseline_level_table"] == "0x52f3b"
        and recovery["condition_reset"]["remaining_turns_table"] == "0x5295f"
        and recovery["condition_reset"]["recovery_policy_table"] == "0xaacb4",
        "RECOVER publishes the four-table condition reset protocol",
    )

    semantics = extract_init.field_semantics(records)
    check(
        semantics["0xaacf0/5/4"]
        == "condition_duration_turns_by_level.level_5_turns"
        and semantics["0x25fa/5/4"] == "condition_level_names.level_5"
        and semantics["0xaad86/55/46"]
        == "condition_stat_deltas.level_5_physical_attack"
        and semantics["0xab3f8/15/12"]
        == "condition_resource_deltas.level_5_hp",
        "ILINIT raw cells join to level-aware condition semantics",
    )
    extract_init.attach_semantic_fields(records, semantics)
    check(
        by_id[6]["semantic_fields"]["condition_cleared_by_recover"] == 1
        and by_id[14]["semantic_fields"][
            "condition_stat_deltas.level_5_physical_attack"
        ] == 10,
        "condition definitions retain raw addresses beside the semantic view",
    )


def test_field_semantics() -> None:
    scripts = paths.scripts()
    items, _ = extract_init.extract_name(sys4load.load(scripts["ITINIT.BIN"]))
    semantics = extract_init.field_semantics(items)
    check(semantics["0x8c879"] == "item_sort_key",
          "parallel INIT fields expose canonical semantic names")
    check(semantics["0x9f541/14/8"] == "item_stat_modifiers.critical_chance",
          "row-table columns expose canonical semantic names")
    extract_init.attach_semantic_fields(items, semantics)
    check(items[0]["semantic_fields"]["item_sort_key"] == 10,
          "records expose a joined semantic field view")

    units, _ = extract_init.extract_name(sys4load.load(scripts["EBINIT.BIN"]))
    unit_semantics = extract_init.field_semantics(units)
    check(
        unit_semantics["0x73236/4/0"]
        == "unit_sally_action_unlock_requirements.contract",
        "SALLY unlock columns expose their action semantics",
    )
    check(
        unit_semantics["0x741d6/8/6"] == "unit_sally_event_ids.sacrifice",
        "SALLY event columns expose their dispatched action semantics",
    )
    check(
        unit_semantics["0x741d6/8/2"] == "unit_sally_event_ids.reserved_action",
        "the unreachable SALLY action remains explicit rather than speculative",
    )
    check(
        unit_semantics["0x66716/30/19"] == "unit_voice_asset_ids.damage_reaction_1",
        "BTL target-owned damage voice columns expose their semantics",
    )
    check(
        unit_semantics["0x66716/30/23"] == "unit_voice_asset_ids.finishing_blow",
        "BTL actor-owned finishing-blow voice exposes its semantics",
    )
    check(
        unit_semantics["0x66716/30/17"] == "unit_voice_asset_ids.unused_slot_17",
        "populated but unreachable voice slots remain explicit",
    )
    check(
        unit_semantics["0x7843e"] == "unit_power_tier",
        "EBINIT's authoring-only power tier joins by semantic name",
    )
    check(
        unit_semantics["0x72296"] == "unit_boss_class",
        "EBINIT's signed boss class joins by semantic name",
    )
    extract_init.attach_semantic_fields(units, unit_semantics)
    unit_by_id = {record["id"]: record for record in units}
    check(
        unit_by_id[5]["semantic_fields"]["unit_sally_event_ids.release"] == 1360,
        "EBINIT records join SALLY release events by semantic field name",
    )
    check(
        unit_by_id[5]["semantic_fields"]["unit_voice_asset_ids.defeated"] == 11523,
        "EBINIT records join voice assets by semantic field name",
    )
    check(
        [
            unit_by_id[unit_id]["semantic_fields"]["unit_power_tier"]
            for unit_id in (2, 3, 4)
        ] == [2, 4, 6],
        "Lily's three forms preserve the correlated 2/4/6 power tiers",
    )
    check(
        [
            unit_by_id[unit_id]["semantic_fields"]["unit_boss_class"]
            for unit_id in (110, 115, 755, 756)
        ] == [1, -1, 4, -4],
        "paired story and final-boss records preserve victory-target sign",
    )
    check(
        unit_by_id[456]["semantic_fields"]["unit_battle_sprite_asset_id"] == 12585
        and unit_by_id[600]["semantic_fields"]["unit_starting_level"] == 80,
        "overlapping EBINIT writes retain their established parallel semantics",
    )

    stages, meta = extract_init.extract_mixed(
        sys4load.load(extract_init.resolve("STINIT"))
    )
    stage_semantics = extract_init.field_semantics(
        stages, meta["array_layouts"]
    )
    check(stage_semantics["0xe7325/1"] == "stage_object_tile_x.index_1",
          "mixed buffer cells expose canonical semantic names")
    check(
        stage_semantics["0xe74b5/21"]
        == "stage_object_required_story_flags.row_3.required_flag_1",
        "mixed row buffers expose row and column semantics",
    )
    check(
        stage_semantics["0xe7889/3"]
        == "stage_enemy_movement_routine_set_ids.row_1",
        "enemy footer copies expose whole-row semantics",
    )
    extract_init.attach_semantic_fields(stages, stage_semantics)
    check(
        stages[0]["semantic_fields"][
            "stage_enemy_movement_routine_set_ids.row_1"
        ] == [1, 1, 1],
        "semantic footer fields expose row values without provenance wrappers",
    )


if __name__ == "__main__":
    test_real_name_tables()
    test_character_profiles()
    test_magic_actions()
    test_static_negative_write()
    test_output_name_validation()
    test_real_mixed_table()
    test_real_class_change_rules()
    test_real_scene_dispatch()
    test_real_routine_banks()
    test_real_message_tables()
    test_message_infrastructure()
    test_message_join()
    test_character_names()
    test_gallery_definitions()
    test_alchemy_recipes()
    test_affinity_definitions()
    test_name_entry_palette()
    test_voice_configuration()
    test_terrain_definitions()
    test_h_scene_gallery()
    test_training_actions()
    test_card_generation_lists()
    test_map_terrain_atlas()
    test_condition_definitions()
    test_field_semantics()
    if FAILS:
        raise SystemExit(f"{len(FAILS)} failed checks")
    print("all extract_init checks passed")
