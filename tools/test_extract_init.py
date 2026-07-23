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
    test_real_message_tables()
    test_message_join()
    test_field_semantics()
    if FAILS:
        raise SystemExit(f"{len(FAILS)} failed checks")
    print("all extract_init checks passed")
