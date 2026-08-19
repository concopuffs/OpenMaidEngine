#!/usr/bin/env python3
"""Standalone tests for the globals registry tooling. Run: py -3.11 -X utf8 tools/test_globals.py"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import globals_build as G
import sys4load

FAILS = []
def check(cond, msg):
    print(("  ok  " if cond else " FAIL ") + msg)
    if not cond: FAILS.append(msg)

def test_load_and_lint():
    entries, meta = G.load_toml(paths.VM_MAP / "globals.toml")
    check(0xa57 in entries, "0xa57 present in globals.toml")
    check(entries[0xa57]["category"] == "story-flag", "0xa57 is category story-flag")
    check(entries[0x3234]["category"] == "story-flag", "0x3234 is category story-flag")
    check({0xa57, 0xa58, 0xa59} <= set(entries), "Lily form flags A/B/C all present")
    check(entries[0x4e085]["columns"] == {
        "0": "current_hp", "1": "current_sp", "2": "current_fs"
    }, "entity current-resource columns are curated")
    check(entries[0x4e11b]["columns"]["10"] == "movement"
          and entries[0x4e11b]["columns"]["13"] == "max_fs",
          "entity effective-stat columns are curated")
    check(entries[0x5231f]["name"] == "entity_tile_x"
          and entries[0x52351]["name"] == "entity_tile_y",
          "entity map-coordinate arrays are curated")
    check(entries[0x4e021]["name"] == "entity_runtime_flags"
          and entries[0x522ed]["name"] == "entity_faction_ids",
          "entity activity and faction arrays are curated")
    check(entries[0x4e693]["name"] == "entity_skill_flags",
          "per-entity skill flags are curated")
    check(entries[0x6f70]["name"] == "unit_stat_growth_fractions"
          and entries[0x6f70]["type"] == "int[100][14]"
          and entries[0x6f70]["columns"]["13"] == "max_fs",
          "persistent unit stat-growth fractions are curated")
    check(entries[0x53e13]["name"] == "entity_carried_item_ids"
          and entries[0x53e77]["name"] == "entity_carried_item_counts",
          "per-entity carried-item slots are curated")
    check(entries[0xeff77]["name"] == "routine_execution_state"
          and entries[0xeff77]["value_domain"] == "0..3",
          "movement routine result states are curated")
    check(entries[0x56b20]["name"] == "entity_patrol_waypoint_indices",
          "RTN_M011 waypoint state is curated")
    check(entries[0xaba96]["name"] == "pathfinding_remaining_route_steps"
          and entries[0xcc9f1]["name"] == "movement_search_mode",
          "movement-search reachability state is curated")
    check(entries[0xbf6fe]["name"] == "pathfinding_filtered_route_scores"
          and entries[0xcc9f4]["name"] == "offensive_action_scope_masks",
          "movement target filtering and offensive scope are curated")
    check(entries[0xb8d86]["name"] == "action_range_distance_grid"
          and entries[0xcc9f2]["name"] == "usable_action_min_range"
          and entries[0xcc9f3]["name"] == "usable_action_max_range",
          "action-range search state is curated")
    check(entries[0x66713]["name"] == "acting_entity_index"
          and entries[0x66714]["name"] == "target_entity_index"
          and entries[0x52289]["name"] == "entity_selected_action_ids",
          "AI actor, target, and selected action state are curated")
    check(entries[0x4dfbd]["name"] == "entity_unit_definition_ids"
          and entries[0x4e053]["name"] == "entity_levels"
          and entries[0x6994]["name"] == "unit_experience_progress"
          and entries[0x204f8]["name"] == "party_reference_level"
          and entries[0x152618]["name"] == "battle_outcome_flags",
          "battle experience and outcome state are curated")
    check(entries[0xcca08]["name"] == "offensive_action_attack_elements"
          and entries[0xab5ba]["name"]
          == "attack_element_effectiveness_percent",
          "AI action-element eligibility state is curated")
    check(entries[0xcc9fe]["name"] == "healing_action_scope_masks",
          "AI healing-action eligibility state is curated")
    check(entries[0x15a2a8]["name"] == "current_glossary_topic_id"
          and entries[0x15a2a9]["name"]
          == "glossary_topic_unlock_seen_decision_ids"
          and entries[0x463b]["name"] == "glossary_topic_titles",
          "VIINIT/VIMES glossary state is curated")
    check(entries[0x15a759]["name"] == "current_enemy_encyclopedia_unit_id"
          and entries[0x56b85]["name"]
          == "enemy_encyclopedia_revealed_flags",
          "EBINIT/EIMES enemy-encyclopedia state is curated")
    check(entries[0x15a117]["name"] == "current_character_profile_id"
          and entries[0x15a118]["name"] == "character_profile_unit_ids"
          and entries[0x15a17c]["name"]
          == "character_profile_portrait_asset_ids"
          and entries[0x45d7]["name"] == "character_profile_names",
          "CIINIT/CIMES character-profile state is curated")
    check(entries[0x1560e7]["name"] == "current_magic_action_id"
          and entries[0x45b9]["name"] == "magic_action_names"
          and entries[0x1561f6]["name"]
          == "magic_action_information_handler_script_ids",
          "MAINIT/MAMES action state is curated")
    check(entries[0x453b]["name"] == "training_action_text"
          and entries[0x155bbc]["name"]
          == "training_action_required_story_flag_ids"
          and entries[0x155e9b]["columns"]["13"] == "max_fs"
          and entries[0x156015]["columns"]["9"] == "execution_10"
          and entries[0x6722]["name"] == "familiar_alignment"
          and entries[0x6727]["name"] == "training_action_execution_counts",
          "TRINIT/TRAIN action state is curated")
    check(entries[0x152485]["name"] == "current_card_generation_list_id"
          and entries[0x152486]["columns"]["2"] == "growth_weight"
          and entries[0x1525b2]["name"] == "card_generation_card_ids"
          and entries[0x1519f8]["name"] == "current_card_id"
          and entries[0x151a5d]["columns"]["2"]
          == "engine_dead_required_flag_3"
          and entries[0x204f4]["name"] == "current_stage_turn",
          "CDINIT/FIELD card-generation state is curated")
    check(entries[0x1519f9]["name"] == "card_definition_type_ids"
          and entries[0x151de1]["columns"]["2"] == "fs"
          and entries[0x15222d]["name"]
          == "card_definition_maximum_resource_damage"
          and entries[0x152359]["name"] == "card_definition_condition_ids"
          and entries[0x152421]["name"] == "card_definition_visual_asset_ids"
          and entries[0x4dfbb]["name"]
          == "stage_card_spendable_point_bonus",
          "CDINIT2/FIELD card-effect state is curated")
    check(entries[0x15288c]["name"] == "selected_battle_animation_id"
          and entries[0x15288e]["columns"]["4"]
          == "reserved_effect_slot_4"
          and entries[0x153ffe]["name"]
          == "battle_animation_effect_start_delays_ms"
          and entries[0x15576e]["name"] == "battle_animation_duration_ms"
          and entries[0x155b5c]["name"] == "battle_effect_visual_mode_ids"
          and entries[0x155b7a]["name"] == "battle_effect_atlas_row_counts"
          and entries[0x155baa]["columns"]["2"] == "pulse_3",
          "BTANINIT/BTANINIT2 battle-animation state is curated")
    check(entries[0x27bd]["name"] == "stage_display_names"
          and entries[0x2ba5]["columns"]["5"] == "cleared_line_3"
          and entries[0xe7e8d]["name"] == "stage_unlock_group_ids"
          and entries[0xe8275]["name"] == "stage_main_progression_flags"
          and entries[0xe865d]["columns"]["6"] == "forbidden_flag_7"
          and entries[0xea1b5]["columns"]["6"] == "required_flag_7",
          "STINIT2 stage text and availability state is curated")
    check(entries[0xebd0d]["name"] == "stage_display_number_major"
          and entries[0xec0f5]["name"] == "stage_display_number_minor"
          and entries[0xed47d]["name"] == "stage_minimap_atlas_origin_y"
          and entries[0xed865]["name"]
          == "stage_clear_base_spendable_point_rewards"
          and entries[0xedc4d]["name"]
          == "stage_authoring_difficulty_tiers",
          "STINIT2 numbering, minimap, points, and authoring tiers are curated")
    check(entries[0x3239]["columns"] == {
        "0": "primary_surface_slot",
        "1": "alternate_surface_slot",
        "2": "transition_surface_slot",
    } and entries[0x62424]["name"] == "adv_gfx_resource_id"
      and entries[0x62450]["name"] == "adv_gfx_layer_index"
      and entries[0x62452]["name"] == "adv_gfx_surface_slot_work"
      and entries[0x62455]["name"] == "adv_gfx_object_handles",
      "ADV graphics layer slots and companion work globals are curated")
    check(entries[0xee035]["columns"]
          == {"0": "entry", "1": "clear", "2": "failure"}
          and entries[0xeebed]["name"] == "stage_extra_dungeon_flags"
          and entries[0xeefd5]["columns"]["2"] == "gold_coin_item_93"
          and entries[0xefb8d]["name"] == "stage_loader_script_ids",
          "STINIT2 flow, EX, coin, and loader columns are curated")
    check(entries[0x15a095]["name"] == "information_tab_index"
          and entries[0x15a096]["name"] == "information_message_handled"
          and entries[0x15a097]["name"]
          == "information_message_handler_script_ids",
          "INFOMES handler registry state is curated")
    check(entries[0x7db]["name"] == "modal_message_lines"
          and entries[0x665d6]["name"] == "modal_message_line_count"
          and entries[0x7e5]["name"] == "modal_annotation_texts"
          and entries[0x665e3]["name"]
          == "modal_annotation_horizontal_cells",
          "MES modal-buffer state is curated")
    check(entries[0x5f0ed]["name"] == "scene_decision_seen_flags",
          "glossary prerequisite seen-state is curated")
    check(entries[0x20543]["name"] == "tile_faction_traversal_masks",
          "faction-specific tile traversal masks are curated")
    check(entries[0xaba64]["name"] == "stage_object_runtime_flags",
          "stage-object targeting flags are curated")
    check(entries[0xb240e]["name"] == "pathfinding_movement_costs",
          "movement-cost work grid is curated")
    check(entries[0xc6077]["name"] == "selected_movement_route_steps"
          and entries[0xc6077]["type"] == "int[1000][27]"
          and entries[0xccbcf]["name"] == "cardinal_tile_delta_x"
          and entries[0xccbd4]["name"] == "cardinal_tile_delta_y"
          and entries[0xccc0a]["name"] == "map_target_tile_x"
          and entries[0xccc0b]["name"] == "map_target_tile_y",
          "selected movement route grid and coordinate helpers are curated")
    check(entries[0x15261e]["name"] == "battle_actor_hp_recovery"
          and entries[0x15261f]["name"]
          == "battle_triggered_passive_skill_flags"
          and entries[0x15261f]["type"] == "int[2][300]",
          "battle recovery output and triggered-passive matrix are curated")
    # lint clean against a permissive address universe (curated addrs are self-consistent)
    errors, warnings = G.lint(entries, set(entries))
    check(errors == [], f"globals.toml lints clean (errors={errors})")

def test_lint_catches_bad_vocab():
    bad = {0x1: {"_addr": 0x1, "name": "x", "category": "bogus",
                 "source": "auto-shape", "confidence": "high",
                 "columns": {"not-an-index": "x", "-1": ""}}}
    errors, _ = G.lint(bad, {0x1})
    check(any("category" in e for e in errors), "lint flags bad category")
    check(any("confidence" in e for e in errors), "lint flags auto-shape claiming high confidence")
    check(any("column index" in e for e in errors), "lint flags nonnumeric column indices")
    check(any("negative column" in e for e in errors), "lint flags negative column indices")

def test_adv_layer_surface_slot_evidence():
    scene = sys4load.load(paths.scripts()["SC0000.BIN"])
    expected = {
        0x3239 + row * 3 + column: value + row
        for row in range(8)
        for column, value in enumerate((4, 43, 51))
    }
    initialized = {
        instruction.args[0][1]: instruction.args[1][1]
        for instruction in scene.instructions
        if 0x125bd <= instruction.offset <= 0x12630
        and len(instruction.args) == 2
        and instruction.args[0][0] == 3
        and instruction.args[0][1] in expected
        and instruction.args[1][0] == 0
    }
    check(initialized == expected,
          "SC0000 initializes all eight ADV layer surface-slot triplets")

    table_lookups = [
        instruction.args
        for instruction in scene.instructions
        if (3, 0x3239) in instruction.args
        and (0, 3) in instruction.args
    ]
    static_columns = {
        args[-1][1]
        for args in table_lookups
        if args[-1][0] == 0
    }
    check(static_columns == {0, 1, 2},
          "SC0000's common graphics paths read all three surface-slot columns")

    init2 = sys4load.load(paths.scripts()["INIT2.BIN"])
    handle_writes = {
        instruction.args[0][1]: instruction.args[1][1]
        for instruction in init2.instructions
        if len(instruction.args) == 2
        and instruction.args[0][0] == 3
        and 0x62455 <= instruction.args[0][1] <= 0x6245d
        and instruction.args[1][0] == 0
    }
    expected_handles = [
        0xcb20, 0xcb2a, 0xcb8e, 0xcb98, 0xcba2,
        0xcbac, 0xcbb6, 0xcbc0, 0xcf08,
    ]
    check([handle_writes.get(0x62455 + index) for index in range(9)]
          == expected_handles,
          "INIT2 seeds the nine ADV retained-object handles")

def test_selected_movement_route_grid_evidence():
    target = 0xc6077
    refs = []
    clears = []
    loaded = {}
    for name, path in paths.scripts().items():
        if path.read_bytes()[:8] != b"SYS4422 ":
            continue
        script = sys4load.load(path)
        loaded[name] = script
        for index, instruction in enumerate(script.instructions):
            if not any(arg[1] == target for arg in instruction.args):
                continue
            refs.append((name, instruction))
            if instruction.opcode == 0x6c:
                clears.append((script, index, instruction))

    check(len(refs) == 56
          and sum(instruction.opcode == 0x12c for _, instruction in refs) == 51
          and len(clears) == 5
          and len({name for name, _ in refs}) == 21,
          "selected route grid has the exact 51 lookups and five clears in 21 scripts")

    clear_geometry_ok = True
    for script, index, instruction in clears:
        previous = script.instructions[index - 1]
        clear_geometry_ok &= (
            previous.label == "mul"
            and previous.args[1:] == [(0, 1000), (0, 27)]
            and instruction.args[1] == previous.args[0]
        )
    check(clear_geometry_ok,
          "every selected route-grid clear covers the reserved 1000 by 27 cells")

    providers = {
        name for name, instruction in refs
        if name.startswith("RTN_M") and instruction.opcode == 0x12c
    }
    check(providers == {f"RTN_M{provider:03d}.BIN" for provider in range(2, 19)},
          "all seventeen RTN_M002..RTN_M018 providers test selected route endpoints")

    setroute = loaded["SETROUTE.BIN"].instructions
    copy_pairs = 0
    for index in range(len(setroute) - 2):
        first, second, third = setroute[index:index + 3]
        if (first.opcode == 0x12c
                and (3, 0xaba96) in first.args
                and second.opcode == 0x12c
                and (3, target) in second.args
                and third.label == "mov"
                and third.args == [(12, 1), (12, 0)]):
            copy_pairs += 1
    check(copy_pairs == 2,
          "SETROUTE copies flood-fill scores into the destination and each traced route tile")

    init2 = loaded["INIT2.BIN"]
    footer_refs = {
        instruction.args[0][1]: instruction.args[1][1]
        for instruction in init2.instructions
        if instruction.opcode == 0x64
        and instruction.args[0] in {(3, 0xccbcf), (3, 0xccbd4)}
    }
    vectors = {}
    for address, offset in footer_refs.items():
        count = init2.dwords[offset]
        vectors[address] = [
            value if value < 0x80000000 else value - 0x100000000
            for value in init2.dwords[offset + 1:offset + 1 + count]
        ]
    check(vectors == {
        0xccbcf: [0, 0, -1, 0, 1],
        0xccbd4: [0, 1, 0, -1, 0],
    }, "INIT2 defines the no-move plus four-cardinal-neighbor vectors")

def test_battle_triggered_passive_skill_flags_evidence():
    target = 0x15261f
    recovery = 0x15261e
    refs = []
    recovery_refs = []
    loaded = {}
    for name, path in paths.scripts().items():
        if path.read_bytes()[:8] != b"SYS4422 ":
            continue
        script = sys4load.load(path)
        loaded[name] = script
        for index, instruction in enumerate(script.instructions):
            if (3, target) in instruction.args:
                refs.append((name, script, index, instruction))
            if (3, recovery) in instruction.args:
                recovery_refs.append((name, instruction))

    check({name for name, _, _, _ in refs}
          == {"BTL.BIN", "CALCDMG.BIN"}
          and len(refs) == 40
          and sum(instruction.opcode == 0x12c
                  for _, _, _, instruction in refs) == 37
          and sum(instruction.opcode == 0x6c
                  for _, _, _, instruction in refs) == 3,
          "triggered-passive matrix has exactly 37 lookups and three clears in BTL/CALCDMG")

    clears_cover_both_rows = True
    for _, script, index, instruction in refs:
        if instruction.opcode != 0x6c:
            continue
        previous = script.instructions[index - 1]
        clears_cover_both_rows &= (
            previous.label == "mul"
            and previous.args[1:] == [(0, 2), (0, 300)]
            and instruction.args[1] == previous.args[0]
        )
    check(clears_cover_both_rows,
          "every triggered-passive clear covers both reserved 300-skill side rows")

    calcdmg = loaded["CALCDMG.BIN"].instructions
    eligibility_columns = {
        instruction.args[-1][1]
        for index, instruction in enumerate(calcdmg[:-1])
        if 0x1f1 <= instruction.offset <= 0x3b9
        and instruction.opcode == 0x12c
        and instruction.args[1] == (9, 0xf)
        and instruction.args[3] == (0, 300)
        and calcdmg[index + 1].label == "mov"
        and calcdmg[index + 1].args[1] == (0, 1)
    }
    check(eligibility_columns == set(range(28, 51)) - {44},
          "CALCDMG seeds the actor/target eligibility surface for shipped passive skill ids")

    category_gate = [
        instruction for instruction in calcdmg
        if instruction.offset == 0x4d6
    ]
    dynamic_writes = {
        instruction.offset
        for instruction in calcdmg
        if instruction.opcode == 0x12c
        and instruction.args[1] == (3, target)
        and instruction.args[3] == (0, 300)
        and instruction.args[-1] == (9, 8)
    }
    check(len(category_gate) == 1
          and category_gate[0].args == [
              (12, 0), (3, 0xa6f86), (9, 8)
          ]
          and dynamic_writes == {
              0x52e, 0x57c, 0x5d3, 0x612, 0x62a, 0x65b,
              0x688, 0x6bd, 0x725, 0x799, 0x7b5, 0x7fb,
              0x817, 0x85d,
          },
          "CALCDMG gates category-4 skills then filters the side/skill activation cells")

    btl_dynamic_consumers = {
        instruction.offset
        for instruction in loaded["BTL.BIN"].instructions
        if instruction.opcode == 0x12c
        and instruction.args[1] == (3, target)
        and instruction.args[3] == (0, 300)
        and instruction.args[-1] == (9, 0x12)
    }
    check(btl_dynamic_consumers == {0x1b8c, 0x2270, 0x2319},
          "BTL indexes triggered flags by equipped skill for icons and both-side animations")

    check({name for name, _ in recovery_refs}
          == {"BTL.BIN", "CALCDMG.BIN"}
          and len(recovery_refs) == 20
          and any(instruction.offset == 0xa3f
                  and instruction.label == "div"
                  and instruction.args == [
                      (3, recovery), (3, 0x15261d), (0, 2)
                  ]
                  for name, instruction in recovery_refs
                  if name == "CALCDMG.BIN")
          and any(instruction.offset == 0x943
                  and instruction.label == "add"
                  and instruction.args[-1] == (3, recovery)
                  for name, instruction in recovery_refs
                  if name == "BTL.BIN"),
          "CALCDMG computes actor recovery and BTL adds it to the acting entity's HP")

def test_unit_stat_growth_fractions_evidence():
    target = 0x6f70
    expected_offsets = {
        "ADDEXP.BIN": {0x1ea, 0x200, 0x212, 0x241, 0x24c, 0x263, 0x2a0},
        "EVOLVE.BIN": {0xa2a, 0xa3a},
        "GAMECLEAR.BIN": {0x331, 0x341, 0x417},
        "GAMESTART.BIN": {0x942, 0xac5},
        "TRAIN.BIN": {0x901, 0x917, 0x929, 0x958, 0x963, 0x975, 0x9a5, 0x9db},
        "UNITECH.BIN": {0x403, 0x40e, 0x420, 0x44f, 0x45a},
    }
    refs = []
    loaded = {}
    for name, path in paths.scripts().items():
        if path.read_bytes()[:8] != b"SYS4422 ":
            continue
        script = sys4load.load(path)
        loaded[name] = script
        refs.extend(
            (name, instruction)
            for instruction in script.instructions
            if (3, target) in instruction.args
        )

    actual_offsets = {}
    for name, instruction in refs:
        actual_offsets.setdefault(name, set()).add(instruction.offset)
    check(actual_offsets == expected_offsets
          and len(refs) == 27
          and all(instruction.opcode == 0x12c
                  and instruction.args[3] == (0, 14)
                  for _, instruction in refs),
          "stat-growth fractions have exactly 27 stride-14 lookups in six scripts")

    instructions = {
        name: {instruction.offset: instruction for instruction in loaded[name].instructions}
        for name in expected_offsets
    }
    growth_paths = {
        "ADDEXP.BIN": {
            "source": (0x1f5, 0x7e5e6),
            "add": 0x20b,
            "divide": 0x21d,
            "current": 0x224,
            "modulo": 0x257,
        },
        "TRAIN.BIN": {
            "source": (0x90c, 0x155e9b),
            "add": 0x922,
            "divide": 0x934,
            "current": 0x93b,
            "modulo": 0x96e,
        },
        "UNITECH.BIN": {
            "source": (0x3f1, 0x7e5e6),
            "add": 0x419,
            "divide": 0x42b,
            "current": 0x432,
            "modulo": 0x465,
        },
    }
    growth_mechanics_ok = True
    for name, path in growth_paths.items():
        script = instructions[name]
        source_offset, source_address = path["source"]
        growth_mechanics_ok &= (
            script[source_offset].opcode == 0x12c
            and script[source_offset].args[1] == (3, source_address)
            and script[path["add"]].label == "add"
            and script[path["divide"]].label == "div"
            and script[path["divide"]].args[-1] == (0, 100)
            and script[path["current"]].opcode == 0x12c
            and script[path["current"]].args[1] == (3, 0x69f8)
            and script[path["modulo"]].label == "mod"
            and script[path["modulo"]].args[-1] == (0, 100)
        )
    check(growth_mechanics_ok,
          "level-up, training, and catch-up growth carry hundredths into current stats")

    persistence_ok = all(
        instructions["GAMESTART.BIN"][offset + 0xb].opcode == 0x1a3
        for offset in expected_offsets["GAMESTART.BIN"]
    )
    for name, first, second, copy in (
        ("GAMECLEAR.BIN", 0x331, 0x341, 0x351),
        ("EVOLVE.BIN", 0xa2a, 0xa3a, 0xa4a),
    ):
        script = instructions[name]
        persistence_ok &= (
            script[first + 0xb].opcode == 0x63
            and script[second + 0xb].opcode == 0x63
            and script[copy].opcode == 0x1b0
            and script[copy].args[-1] == (0, 14)
        )
    persistence_ok &= (
        instructions["GAMECLEAR.BIN"][0x422].opcode == 0x1a2
    )
    check(persistence_ok,
          "save/load, game-clear, and evolution preserve the complete fractional rows")

def test_battle_experience_evidence():
    loaded = {
        name: {
            instruction.offset: instruction
            for instruction in sys4load.load(paths.scripts()[name]).instructions
        }
        for name in (
            "BTL.BIN", "ADDEXP.BIN", "FIELD.BIN", "GAMESTART.BIN", "SETEN.BIN"
        )
    }
    btl = loaded["BTL.BIN"]

    check(
        btl[0x3667].args[1] == (3, 0x4dfbd)
        and btl[0x366e].args[1] == (3, 0x83406)
        and btl[0x367a].args[1] == (3, 0x4e053)
        and btl[0x3681].args[1] == (3, 0x4e053)
        and btl[0x3688].label == "sub",
        "BTL selects the opponent definition reward and computes the runtime level gap",
    )
    check(
        btl[0x35ae].args[1] == (3, 0x4e085)
        and btl[0x35cf].args[1] == (3, 0x4e085)
        and btl[0x35c7].args == [(3, 0x152618), (0, 0)]
        and btl[0x35e8].args == [(3, 0x152618), (0, 1)]
        and btl[0x35f0].args == [(3, 0x152618), (0, 2)]
        and btl[0x368f].args == [(9, 49), (3, 0x152618), (0, 4)],
        "BTL derives defeated/survived outcome bits from both combatants' HP",
    )

    nonlethal = {
        0x36ab: 40,
        0x36c1: 30,
        0x36d7: 20,
        0x36df: 10,
    }
    defeated = {
        0x36f5: 250,
        0x370b: 200,
        0x3721: 150,
        0x3737: 100,
        0x3754: 75,
        0x3771: 40,
        0x3779: 20,
    }
    check(
        all(btl[offset].args == [(9, 7), (0, percent)]
            for offset, percent in nonlethal.items())
        and all(btl[offset].args == [(9, 7), (0, percent)]
                for offset, percent in defeated.items())
        and btl[0x377e].label == "mul"
        and btl[0x3785].args[-1] == (0, 100)
        and btl[0x3791].label == "call-script",
        "BTL's nonlethal and defeat percentage tables feed integer-divided ADDEXP awards",
    )

    addexp = loaded["ADDEXP.BIN"]
    check(
        addexp[0x52].args == [(9, 1), (3, 0x62ccb)]
        and addexp[0x57].args[-1] == (0, 0x10)
        and addexp[0x6d].args[-1] == (0, 0x20)
        and addexp[0x85].args[-1] == (0, 0x40)
        and loaded["GAMESTART.BIN"][0xf07].args == [(3, 0x3302), (0, 4)]
        and loaded["GAMESTART.BIN"][0xf0c].args == [(3, 0x3302), (0, 5)]
        and loaded["GAMESTART.BIN"][0xf11].args == [(3, 0x3302), (0, 6)]
        and addexp[0x9a].label == "gr"
        and addexp[0x148].args[1] == (3, 0x6994)
        and addexp[0x164].args[1] == (3, 0x6994)
        and addexp[0x156].args[-1] == (0, 100),
        "ADDEXP applies modifier bits, rejects zero, and advances the 0..99 progress cell",
    )

    field = loaded["FIELD.BIN"]
    seten = loaded["SETEN.BIN"]
    check(
        field[0x5e8].args == [(3, 0x204f8), (0, 0)]
        and field[0x63e].args[1] == (3, 0x6930)
        and field[0x705].args[1] == (3, 0x6930)
        and field[0x6b7].args[1] == (3, 0x204f8)
        and field[0x70c].args[1] == (3, 0x204f8)
        and field[0x6c6].args[-1] == (0, 5)
        and field[0x71b].args[1] == (3, 0x204f8)
        and seten[0x2be].args[1] == (3, 0x204f8)
        and seten[0x2e1].args[1] == (3, 0x204f8)
        and seten[0x2ef].args[1] == (3, 0x4e053),
        "FIELD builds the party reference level and SETEN uses it for enemy runtime levels",
    )

def test_merge_precedence():
    curated, _ = G.load_toml(paths.VM_MAP / "globals.toml")
    auto = G.load_auto(paths.BUILD / "global-var-map.json")
    merged = G.merge(curated, auto)
    check(merged[0xa57]["name"] == "lily_form_a", "curated 0xa57 name wins over auto label")
    check(merged[0xa57]["category"] == "story-flag", "curated 0xa57 category overrides auto string-table")
    check(merged[0xa57]["provenance"] == "curated", "0xa57 marked curated")
    check(merged[0x9f541]["columns"]["8"] == "critical_chance",
          "curated row-table column semantics survive the merge")
    # an address only in the auto map falls through as provenance=auto
    auto_only = next((a for a in auto.get("globals", {})
                      if int(a, 16) not in curated and auto["globals"][a].get("label")), None)
    check(auto_only is not None and merged[int(auto_only, 16)]["provenance"] == "auto",
          "auto-only address retained with provenance=auto")

def test_sys4load_labels_from_registry():
    import importlib, sys4load
    importlib.reload(sys4load)   # re-run _load_global_labels against current build/globals.json
    lbl = sys4load.GLOBAL_LABELS.get(0x3234, "")
    check("chapter_mode" in lbl, f"sys4load labels 0x3234 with curated name (got {lbl!r})")
    lbl2 = sys4load.GLOBAL_LABELS.get(0xa57, "")
    check("lily_form_a" in lbl2, f"sys4load labels 0xa57 with curated name (got {lbl2!r})")

def test_miner_finds_known_flags():
    import story_flags
    cands = story_flags.mine()
    check(0x3234 in cands, "miner surfaces chapter flag 0x3234")
    check(set(range(1, 9)) <= set(cands[0x3234]["consts"]), "0x3234 compared against 1..8 enum")
    check(cands[0x3234]["category"] == "story-flag", "0x3234 classified story-flag")
    check(0xa57 in cands, "miner surfaces Lily form flag 0xa57")
    check(cands[0xa57]["reach_scenes"] >= 70, "0xa57 high scene reach")
    check(cands[0xa57]["category"] == "story-flag", "0xa57 classified story-flag")

def test_bootstrap_is_additive_and_idempotent():
    import tempfile, pathlib, story_flags
    # Minimal fixture (one curated entry) so bootstrap always has candidates to add, independent
    # of how many the real globals.toml already holds.
    src = ('[meta]\nnote = "fixture"\n\n[[global]]\naddress = "0xa57"\nname = "lily_form_a"\n'
           'category = "story-flag"\ntype = "int"\nvalue_domain = "{0,1}"\nusage = "curated"\n'
           'source = "investigation"\nconfidence = "high"\ndepends_on = []\n')
    with tempfile.TemporaryDirectory() as d:
        tp = pathlib.Path(d) / "globals.toml"
        tp.write_text(src, encoding="utf-8")
        before, _ = G.load_toml(tp)
        cands = story_flags.mine()
        story_flags.bootstrap(cands, toml_path=tp)
        after, _ = G.load_toml(tp)
        check(len(after) > len(before), "bootstrap adds new skeleton entries")
        check(after[0xa57]["name"] == "lily_form_a", "bootstrap preserves curated 0xa57")
        # idempotent: second run adds nothing
        n1 = len(after)
        story_flags.bootstrap(story_flags.mine(), toml_path=tp)
        after2, _ = G.load_toml(tp)
        check(len(after2) == n1, "second bootstrap is a no-op (idempotent)")
        # every skeleton is auto-shape and not high-confidence
        added = set(after) - set(before)
        check(all(after[a]["source"] == "auto-shape" for a in added), "skeletons are source=auto-shape")
        check(all(after[a]["confidence"] != "high" for a in added), "skeletons never high confidence")

if __name__ == "__main__":
    test_load_and_lint()
    test_lint_catches_bad_vocab()
    test_adv_layer_surface_slot_evidence()
    test_selected_movement_route_grid_evidence()
    test_battle_triggered_passive_skill_flags_evidence()
    test_unit_stat_growth_fractions_evidence()
    test_battle_experience_evidence()
    test_merge_precedence()
    test_sys4load_labels_from_registry()
    test_miner_finds_known_flags()
    test_bootstrap_is_additive_and_idempotent()
    print(f"\n{len(FAILS)} failures")
    sys.exit(1 if FAILS else 0)
