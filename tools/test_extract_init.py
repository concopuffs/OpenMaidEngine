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
    check(meta["decoded_movement_provider_count"] == 2
          and meta["decoded_movement_step_count"] == 252
          and meta["decoded_movement_parameter_count"] == 685,
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
    check("provider_behavior" not in movement
          and "destination_tile_x" not in movement,
          "RTINIT does not leak provider-specific meanings onto undecoded selectors")
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
        "ITMES.BIN": (0x8C877, 287),
        "SKMES.BIN": (0xA6E59, 131),
    }
    for name, (selector, count) in expected.items():
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
    test_static_negative_write()
    test_output_name_validation()
    test_real_mixed_table()
    test_real_class_change_rules()
    test_real_scene_dispatch()
    test_real_routine_banks()
    test_real_message_tables()
    test_message_join()
    test_field_semantics()
    if FAILS:
        raise SystemExit(f"{len(FAILS)} failed checks")
    print("all extract_init checks passed")
