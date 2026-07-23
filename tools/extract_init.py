#!/usr/bin/env python3
"""Extract a *INIT data table to JSON. Auto-detects the table's shape.

*INIT scripts populate global arrays and work buffers with static game data. Eight shapes seen:

  name   — records keyed by a name string. Each record: set-string(name), static field writes,
           set-string(desc). Arrays indexed by record id in lockstep (+1/record).
           (SKINIT skills, ITINIT items, EBINIT units, OBINIT object definitions)
  numeric— column table with NO names: mov/copy-to-global into parallel int arrays, keyed
           by an incrementing index column. (CGINIT gallery)
  footer — copy-local-array (op 0x64) bulk-loads length-prefixed arrays from the file
           footer into per-record global arrays. The data lives in the footer. (MPINIT maps)
  mixed  — a sparse selector dispatch writes strings, scalars, fixed-buffer cells, and
           footer arrays for one runtime record. (STINIT stages)
  rules  — conditional blocks select a unit promotion and add effects to shared output
           buffers. (CCINIT class changes)
  dispatch—paired parallel arrays map a sparse decision id to a packed script resource id
           and authored chapter metadata. (SCINIT scene dispatch)
  banked —twenty parallel 1000-by-20 banks define sparse movement and battle routine
           step records, including provider joins and source overwrites. (RTINIT routines)

ILINIT is a special name-mode matrix: 30 reserved condition ids by five authored
levels, joined to the runtime condition-state ABI and RECOVER policy.

Records are {id, name?, desc?, fields:{"0x<col_base>": value}} or, for footer tables,
{id, global_addr, footer_off, values:[...]}. Column addresses are raw engine globals;
confirmed names come from the generated engine global registry while raw keys remain provenance.

Usage: py -3.11 -X utf8 tools/extract_init.py <TABLE> [OUTNAME] [--mode name|numeric|footer|mixed|rules|dispatch|banked]
"""
from __future__ import annotations
import collections
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

CURRENT_UNIT_ID = 0x66715
CURRENT_UNIT_LEVELS = 0x6930
UNIT_CLASS_CHANGE_STATE = 0x573BB
CLASS_CHANGE_TITLE_OUT = 0x26B4
CLASS_CHANGE_LEVEL_OUT = 0xAB8E7
CLASS_CHANGE_COST_OUT = 0xAB8E8
CLASS_CHANGE_STATS_OUT = 0xAB8E9
CLASS_CHANGE_SKILLS_OUT = 0xAB8F7
CLASS_CHANGE_FLAGS_OUT = 0xAB8FB

ROUTINE_BANK_ROOT = 0xEFF78
ROUTINE_BANK_SPAN = 20000
ROUTINE_BANK_COUNT = 20
ROUTINE_RECORD_STRIDE = 20
ROUTINE_RECORD_SPAN = 1000
ROUTINE_SET_ID = 0xEFF75
ROUTINE_STEP_INDEX = 0xEFF76
ROUTINE_EXECUTION_STATE = 0xEFF77

ROUTINE_BANK_ROLES = (
    "movement_provider_selector",
    "movement_activation_percent",
    "movement_parameter_1",
    "movement_parameter_2",
    "movement_parameter_3",
    "movement_parameter_4",
    "movement_reserved",
    "movement_minimum_progress_count",
    "movement_required_story_flag_id",
    "movement_forbidden_story_flag_id",
    "battle_provider_selector",
    "battle_activation_percent",
    "battle_parameter_1",
    "battle_reserved_1",
    "battle_reserved_2",
    "battle_reserved_3",
    "battle_reserved_4",
    "battle_reserved_5",
    "battle_required_story_flag_id",
    "battle_forbidden_story_flag_id",
)

MOVEMENT_PROVIDER_PARAMETER_SCHEMAS = {
    1: {
        "behavior": "advance_step_progress",
        "parameter_fields": {},
        "ignored_parameter_fields": {
            "movement_parameter_1": (
                "authored once (routine set 173, slot 2, value 10), but "
                "RTN_M001 never reads movement parameter bank 2"
            ),
            "movement_parameter_2": (
                "authored once in the same step (value 711), but RTN_M001 "
                "never reads movement parameter bank 3"
            ),
        },
        "completion": (
            "unconditionally advance the current step's progress counter and "
            "produce movement result state 1 without selecting a destination"
        ),
    },
    2: {
        "behavior": "roam_to_random_reachable_tile",
        "parameter_fields": {},
        "target_selection": (
            "build the acting entity's movement-limited reach grid, apply "
            "SETMVWORK filtering, and retain map tiles with a positive "
            "filtered route score whose movement cost is no greater than "
            "current FS; randomize the candidate order and use the first tile "
            "for which SETROUTE produces a route"
        ),
        "completion": (
            "advance the current step's progress counter and produce movement "
            "result state 1 after routing to a randomized reachable tile"
        ),
    },
    3: {
        "behavior": "route_toward_reachable_normal_attack_target",
        "parameter_fields": {},
        "target_selection": (
            "require a usable normal attack, build the acting entity's "
            "movement-limited reach grid, and retain active foreign-faction "
            "entities whose occupied tile remains element-effective after "
            "SETMVWORK filtering and costs no more than current FS; rank "
            "candidates by descending remaining-route score with randomized "
            "ties, then use the first candidate for which SETROUTE produces "
            "a route"
        ),
        "completion": (
            "advance the current step's progress counter and produce movement "
            "result state 1 after routing toward a reachable normal-attack "
            "target"
        ),
    },
    4: {
        "behavior": "approach_stage_object_slot",
        "parameter_fields": {
            "movement_parameter_1": "stage_object_slot_index",
        },
        "parameter_defaults": {
            "movement_parameter_1": 0,
        },
        "parameter_notes": {
            "stage_object_slot_index": (
                "zero-based index into the current stage's object arrays; "
                "shipped explicit values are 1 or 2, with three unwritten "
                "cells using the zero/slot-0 default"
            ),
        },
        "completion": (
            "advance the current step's progress counter after reaching the "
            "selected object's tile (or, for a type-6 stage object, its linked "
            "exit tile)"
        ),
    },
    5: {
        "behavior": "approach_destination_tile",
        "parameter_fields": {
            "movement_parameter_1": "destination_tile_x",
            "movement_parameter_2": "destination_tile_y",
        },
        "completion": (
            "advance the current step's progress counter after reaching the "
            "destination tile (or its linked type-6 stage-object exit tile)"
        ),
    },
    6: {
        "behavior": "approach_nearest_enemy",
        "parameter_fields": {
            "movement_parameter_1": "maximum_target_route_steps",
        },
        "parameter_notes": {
            "maximum_target_route_steps": (
                "inclusive route-step radius from the acting entity after "
                "SETMVWORK applies offensive-action eligibility; shipped "
                "values are 1..7, 10, or 20"
            ),
        },
        "target_selection": (
            "nearest active entity of another faction within the route-step "
            "radius; choose randomly among ties, then approach a reachable "
            "tile nearest that enemy; execution also requires the normal-"
            "attack bit in offensive_action_scope_masks[0]"
        ),
        "completion": (
            "advance the current step's progress counter after producing a "
            "valid movement destination toward the selected enemy"
        ),
    },
    7: {
        "behavior": "approach_injured_ally",
        "parameter_fields": {
            "movement_parameter_1": "maximum_target_route_steps",
            "movement_parameter_2": "maximum_target_hp_percent",
        },
        "parameter_notes": {
            "maximum_target_route_steps": (
                "maximum flood-fill step distance from the acting entity; "
                "shipped values are 5 or 10"
            ),
            "maximum_target_hp_percent": (
                "inclusive current-HP percentage cutoff; shipped values are "
                "50, 70, or 80"
            ),
        },
        "target_selection": (
            "nearest active non-self entity of the same faction whose current "
            "HP percentage is at or below the cutoff; choose randomly among "
            "ties, then approach a reachable tile nearest that ally"
        ),
        "completion": (
            "advance the current step's progress counter after producing a "
            "valid movement destination toward the selected ally"
        ),
    },
    8: {
        "behavior": "approach_nearest_foreign_magic_pillar",
        "parameter_fields": {},
        "ignored_parameter_fields": {
            "movement_parameter_1": (
                "authored once (routine set 112, slot 6, value 1), but "
                "RTN_M008 never reads movement parameter bank 2"
            ),
        },
        "target_selection": (
            "nearest reachable active stage object of OBINIT type 2, 3, or 4 "
            "(small, medium, or large Magic Pillar) whose runtime state/faction "
            "differs from the acting entity; unlike RTN_M015, no configured "
            "route-radius gate is applied"
        ),
        "completion": (
            "produce a movement result when a reachable foreign-controlled "
            "Magic Pillar exists"
        ),
    },
    9: {
        "behavior": "approach_collectible_treasure",
        "parameter_fields": {},
        "target_selection": (
            "select the nearest active unopened OBINIT type-7 chest when the "
            "acting entity has skill 22 (Unlock), or type-8 treasure without "
            "that skill gate; require at least one of the entity's two carried-"
            "item slots to be empty or already contain the object's item id, "
            "then approach a reachable tile nearest the selected object"
        ),
        "completion": (
            "advance the current step's progress counter and produce movement "
            "result state 1 after routing toward collectible treasure"
        ),
    },
    10: {
        "behavior": "approach_healing_feather",
        "parameter_fields": {
            "movement_parameter_1": "resource_index",
            "movement_parameter_2": "maximum_resource_percent",
        },
        "parameter_defaults": {
            "movement_parameter_1": 0,
        },
        "parameter_notes": {
            "resource_index": (
                "0=HP, 1=SP, 2=FS; all shipped RTINIT cells are unwritten and "
                "therefore use the zero/HP default"
            ),
            "maximum_resource_percent": (
                "inclusive current/max percentage cutoff; shipped values are "
                "30 or 50"
            ),
        },
        "target_selection": (
            "nearest active stage object of OBINIT type 15 (Healing Feather) "
            "or 16 (single-use red Healing Feather), then approach a reachable "
            "tile nearest that object"
        ),
        "completion": (
            "produce a movement result only when the selected resource's "
            "maximum is nonzero, its current percentage is at or below the "
            "cutoff, and a reachable Healing Feather exists"
        ),
    },
    11: {
        "behavior": "cycle_destination_waypoints",
        "parameter_fields": {
            "movement_parameter_1": "destination_tile_x",
            "movement_parameter_2": "destination_tile_y",
            "movement_parameter_3": "waypoint_ordinal",
            "movement_parameter_4": "path_cost_limit_override",
        },
        "parameter_notes": {
            "waypoint_ordinal": (
                "one-based; only the ordinal matching the entity's current "
                "zero-based waypoint index executes"
            ),
            "path_cost_limit_override": (
                "optional; zero/absent falls back to the entity's current FS"
            ),
        },
        "completion": (
            "advance the entity's waypoint index modulo the largest authored "
            "waypoint ordinal after reaching the destination tile (or its "
            "linked type-6 stage-object exit tile)"
        ),
    },
    12: {
        "behavior": "approach_destination_tile_avoiding_foreign_entities",
        "parameter_fields": {
            "movement_parameter_1": "destination_tile_x",
            "movement_parameter_2": "destination_tile_y",
        },
        "routing": (
            "same destination and completion logic as RTN_M005, but MVSEEK "
            "mode 2 masks the doubled-coordinate terrain cells occupied by "
            "active entities of another faction before its flood fill"
        ),
        "completion": (
            "advance the current step's progress counter after reaching the "
            "destination tile (or its linked type-6 stage-object exit tile)"
        ),
    },
    13: {
        "behavior": "approach_faction_traversable_tile",
        "parameter_fields": {
            "movement_parameter_1": "target_faction_filter",
        },
        "parameter_defaults": {
            "movement_parameter_1": 0,
        },
        "parameter_notes": {
            "target_faction_filter": (
                "zero means any faction other than the acting entity's faction; "
                "a nonzero value selects exactly that faction id. Only one "
                "shipped step explicitly writes value 1; three use default zero"
            ),
        },
        "target_selection": (
            "when the current tile is not traversable by the selected faction "
            "set, choose the nearest reachable tile whose "
            "tile_faction_traversal_masks value includes that set, then "
            "approach it"
        ),
        "completion": (
            "advance the current step's progress counter after producing a "
            "valid movement destination into the selected faction's traversable "
            "territory"
        ),
    },
    14: {
        "behavior": "retreat_from_nearby_enemies",
        "parameter_fields": {
            "movement_parameter_1": "maximum_threat_route_steps",
        },
        "parameter_notes": {
            "maximum_threat_route_steps": (
                "inclusive route-step radius used to collect active foreign-"
                "faction threats; shipped values are 3 or 6"
            ),
        },
        "target_selection": (
            "sum route-proximity scores from every active foreign-faction "
            "entity within the threat radius, exclude occupied tiles, and "
            "choose a reachable tile with the lowest positive aggregate score "
            "(farthest from the collected threats), randomizing ties"
        ),
        "completion": (
            "advance the current step's progress counter after producing a "
            "valid retreat destination"
        ),
    },
    15: {
        "behavior": "approach_foreign_magic_pillar",
        "parameter_fields": {
            "movement_parameter_1": "maximum_target_route_steps",
        },
        "parameter_notes": {
            "maximum_target_route_steps": (
                "inclusive route-step radius; shipped values are 2..6"
            ),
        },
        "target_selection": (
            "nearest active stage object of OBINIT type 2, 3, or 4 (small, "
            "medium, or large Magic Pillar) whose runtime state/faction differs "
            "from the acting entity; require it to be within the route-step "
            "radius, then approach a reachable tile nearest that object"
        ),
        "completion": (
            "produce a movement result only when a foreign-controlled Magic "
            "Pillar exists within the configured route-step radius"
        ),
    },
    17: {
        "behavior": "route_toward_lowest_hp_reachable_normal_attack_target",
        "parameter_fields": {},
        "target_selection": (
            "require a usable normal attack, build the acting entity's "
            "movement-limited reach grid, and retain active foreign-faction "
            "entities whose occupied tile remains element-effective after "
            "SETMVWORK filtering and costs no more than current FS; sort by "
            "current HP ascending, preserving source order among ties, then "
            "use the first candidate for which SETROUTE produces "
            "a route"
        ),
        "completion": (
            "advance the current step's progress counter and produce movement "
            "result state 1 after routing toward the lowest-current-HP "
            "reachable normal-attack target"
        ),
    },
    51: {
        "behavior": "select_effective_attack_target_and_action",
        "parameter_fields": {},
        "target_selection": (
            "require a usable nonzero attack-range band, scan active foreign-"
            "faction entities inside the ATSEEK range grid, and retain targets "
            "for which at least one allowed normal-attack/equipped-skill "
            "element has positive effectiveness against the target's defense "
            "element; encountering a lower range band clears earlier "
            "candidates, and the final target is randomized from the retained "
            "list"
        ),
        "action_selection": (
            "after choosing the target, collect the effective actions enabled "
            "in the tracked closest range band (0 means normal attack; nonzero "
            "values are equipped skill ids), choose randomly, and store both "
            "the target entity and selected action"
        ),
        "completion": (
            "advance the current step's progress counter and produce immediate-"
            "battle result state 2 when a target/action pair is selected; no "
            "movement route is produced"
        ),
    },
    52: {
        "behavior": "select_lowest_hp_effective_attack_target_and_action",
        "parameter_fields": {},
        "target_selection": (
            "require a usable nonzero attack-range band, scan active foreign-"
            "faction entities inside the ATSEEK range grid, and retain only "
            "the equal-lowest-current-HP targets for which at least one "
            "allowed normal-attack/equipped-skill element has positive "
            "effectiveness against the target's defense element; choose "
            "randomly among those HP ties"
        ),
        "action_selection": (
            "reload the chosen target's actual ATSEEK range band, collect the "
            "effective actions enabled there (0 means normal attack; nonzero "
            "values are equipped skill ids), choose randomly, and store both "
            "the target entity and selected action"
        ),
        "completion": (
            "advance the current step's progress counter and produce immediate-"
            "battle result state 2 when a target/action pair is selected; no "
            "movement route is produced"
        ),
    },
    61: {
        "behavior": "select_lowest_hp_ally_and_healing_skill",
        "parameter_fields": {},
        "target_selection": (
            "require at least one range-enabled healing skill, scan active "
            "same-faction entities inside the ATSEEK range grid, retain only "
            "targets tied at the lowest current-HP percentage whose range band "
            "enables a healing action, and choose randomly among those ties"
        ),
        "action_selection": (
            "among equipped healing skills enabled at the chosen target's "
            "range, compare current HP plus each skill's HP recovery against "
            "max HP, maximizing the projected result while it remains below "
            "max and minimizing it after reaching or exceeding max; store the "
            "chosen target entity and healing skill id"
        ),
        "completion": (
            "advance the current step's progress counter and produce immediate-"
            "support result state 3 when a target/healing-skill pair is "
            "selected; no movement route is produced"
        ),
    },
}

UNIT_STAT_COLUMNS = (
    "accuracy", "evasion", "physical_attack", "physical_defense",
    "magic_attack", "magic_defense", "speed", "luck", "critical_chance",
    "capture_power", "movement", "max_hp", "max_sp", "max_fs",
)

MESSAGE_TABLES = {
    "CIINIT": "CIMES",
    "EBINIT": "EIMES",
    "ITINIT": "ITMES",
    "MAINIT": "MAMES",
    "SKINIT": "SKMES",
    "VIINIT": "VIMES",
}

CHARACTER_PROFILE_NAME_ARRAY_BASE = 0x45D7
CHARACTER_PROFILE_UNIT_ARRAY_BASE = 0x15A118
CHARACTER_PROFILE_PORTRAIT_ARRAY_BASE = 0x15A17C
CHARACTER_PROFILE_PORTRAIT_X_ARRAY_BASE = 0x15A1E0
CHARACTER_PROFILE_PORTRAIT_Y_ARRAY_BASE = 0x15A244
CHARACTER_PROFILE_RECORD_SPAN = 100

MAGIC_ACTION_NAME_ARRAY_BASE = 0x45B9
MAGIC_ACTION_INTEGER_ARRAY_BASES = (
    0x1560E8,
    0x156106,
    0x156124,
    0x156142,
    0x156160,
    0x15617E,
    0x15619C,
    0x1561BA,
    0x1561D8,
    0x1561F6,
)
MAGIC_ACTION_HANDLER_ARRAY_BASE = 0x1561F6
MAGIC_ACTION_RECORD_SPAN = 30

VOCABULARY_NAME_ARRAY_BASE = 0x463B
VOCABULARY_RECORD_TABLE_BASE = 0x15A2A9
VOCABULARY_RECORD_STRIDE = 3
VOCABULARY_RECORD_SPAN = 200

CHARACTER_NAME_ARRAY_BASE = 0x315
CHARACTER_VOICE_FAMILY_ARRAY_BASE = 0x624BF
CHARACTER_NAME_RECORD_SPAN = 1000

CONDITION_RECORD_SPAN = 30
CONDITION_LEVEL_COUNT = 5
CONDITION_LEVEL_NAME_BASE = 0x25FA
CONDITION_COLUMNS = {
    1: "instant_death",
    2: "hp_drain",
    3: "sp_drain",
    4: "fs_drain",
    5: "curse",
    6: "charm",
    7: "confusion",
    8: "paralysis",
    9: "poison",
    10: "water_flow",
    11: "fear",
    12: "reserved",
    13: "regeneration",
    14: "exaltation",
}
CONDITION_SCALAR_ARRAYS = {
    0xAAC78: "effectiveness_element_id",
    0xAAC96: "can_affect_bosses",
    0xAACB4: "cleared_by_recover",
    0xAACD2: "icon_id",
}
CONDITION_DURATION_BASE = 0xAACF0
CONDITION_STAT_DELTA_BASE = 0xAAD86
CONDITION_RESOURCE_DELTA_BASE = 0xAB3F8
CONDITION_STAT_COLUMNS = (
    "accuracy",
    "evasion",
    "physical_attack",
    "physical_defense",
    "magic_attack",
    "magic_defense",
    "speed",
    "luck",
    "critical_chance",
    "capture_power",
    "movement",
)
CONDITION_RESOURCE_COLUMNS = ("hp", "sp", "fs")

GALLERY_ASSET_TABLE_BASE = 0x62CD1
GALLERY_RECORD_SPAN = 2000
GALLERY_ASSET_STRIDE = 2
GALLERY_THUMBNAIL_SHEET_ARRAY_BASE = 0x63C71
GALLERY_THUMBNAIL_SLOT_ARRAY_BASE = 0x64441
GALLERY_VARIANT_ORDINAL_ARRAY_BASE = 0x64C11
GALLERY_THUMBNAIL_SHEET_CONFIG_BASE = 0x66381
GALLERY_THUMBNAIL_SHEET_CONFIG_SPAN = 10

RECOVER_CURRENT_ENTITY = 0x152616
RECOVER_EFFECTIVE_STATS = 0x4E11B
RECOVER_CURRENT_RESOURCES = 0x4E085
RECOVER_CURRENT_LEVELS = 0x52383
RECOVER_REMAINING_TURNS = 0x5295F
RECOVER_BASELINE_LEVELS = 0x52F3B
RECOVER_POLICY = 0xAACB4


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


def _class_change_guards(scr) -> list[dict]:
    """Find CCINIT's source-ordered `current_unit_id == immediate` rule guards."""
    guards = []
    for index, ins in enumerate(scr.instructions):
        if (sys4load.display_label(ins.opcode) == "eq"
                and len(ins.args) >= 3
                and ins.args[0][0] == T_LOCAL_INT
                and ins.args[1] == (T_GLOBAL_INT, CURRENT_UNIT_ID)
                and ins.args[2][0] == T_IMM):
            guards.append({
                "index": index,
                "offset": ins.offset,
                "unit_id": ins.args[2][1],
            })
    return guards


def _paired_parallel_writes(scr) -> tuple[list[tuple], int] | None:
    """Recognize alternating writes to two equally indexed parallel arrays."""
    writes = []
    for ins in scr.instructions:
        write = _static_global_write(ins)
        if write is not None and isinstance(write[1], int):
            writes.append((ins.offset, *write))
        elif sys4load.display_label(ins.opcode) != "exit":
            return None
    if len(writes) < 200 or len(writes) % 2:
        return None
    span = writes[1][1] - writes[0][1]
    if span <= 0:
        return None
    for index in range(0, len(writes), 2):
        primary, secondary = writes[index:index + 2]
        if secondary[1] - primary[1] != span:
            return None
    return writes, span


def _routine_bank_writes(scr) -> list[tuple] | None:
    """Recognize RTINIT's twenty reserved 1000-by-20 routine-step banks."""
    writes = []
    for ins in scr.instructions:
        write = _static_global_write(ins)
        if write is not None and isinstance(write[1], int):
            destination, value = write
            relative = destination - ROUTINE_BANK_ROOT
            if not (0 <= relative < ROUTINE_BANK_COUNT * ROUTINE_BANK_SPAN):
                return None
            bank_index, cell = divmod(relative, ROUTINE_BANK_SPAN)
            record_id, slot = divmod(cell, ROUTINE_RECORD_STRIDE)
            if not (
                0 <= bank_index < ROUTINE_BANK_COUNT
                and 0 <= record_id < ROUTINE_RECORD_SPAN
                and 0 <= slot < ROUTINE_RECORD_STRIDE
            ):
                return None
            writes.append((
                ins.offset, destination, value, bank_index, record_id, slot
            ))
        elif sys4load.display_label(ins.opcode) != "exit":
            return None
    return writes if len(writes) >= 1000 else None


def detect_mode(scr):
    ops = [ins.opcode for ins in scr.instructions]
    has_str = any(ins.opcode == SET_STRING and ins.args and ins.args[0][0] == T_GLOBAL_STRING
                  for ins in scr.instructions)
    has_class_change_title = any(
        ins.opcode == SET_STRING
        and ins.args
        and ins.args[0] == (T_GLOBAL_STRING, CLASS_CHANGE_TITLE_OUT)
        for ins in scr.instructions
    )
    if has_class_change_title and len(_class_change_guards(scr)) >= 4:
        return "rules"
    if has_str and len(_mixed_guards(scr)) >= 4:
        return "mixed"
    if has_str:
        return "name"
    if _paired_parallel_writes(scr):
        return "dispatch"
    if _routine_bank_writes(scr):
        return "banked"
    n_footer = ops.count(COPY_LOCAL_ARRAY)
    n_int = ops.count(MOV) + ops.count(COPY_TO_GLOBAL)
    return "footer" if n_footer >= max(4, n_int) else "numeric"


@cache
def unit_definition_names() -> dict[int, str]:
    """Load EBINIT's authoritative unit names by definition id."""
    records, _ = extract_name(sys4load.load(resolve("EBINIT")))
    return {record["id"]: record["name"] for record in records}


@cache
def skill_definition_names() -> dict[int, str]:
    """Load SKINIT's authoritative skill names by skill id."""
    records, _ = extract_name(sys4load.load(resolve("SKINIT")))
    return {record["id"]: record["name"] for record in records}


def extract_class_change_rules(scr):
    """Extract CCINIT's promotion predicates and accumulator effects.

    CALCCC initializes the output block, invokes CCINIT, and applies the selected
    title, cost delta, fourteen stat deltas, and up to three skills to the unit.
    Each CCINIT block is therefore a rule rather than a row in a static table.
    """
    guards = _class_change_guards(scr)
    if not guards:
        return [], {}

    unit_names = unit_definition_names()
    skill_names = skill_definition_names()
    records = []
    instructions = scr.instructions
    for rule_index, guard in enumerate(guards):
        end = guards[rule_index + 1]["index"] if rule_index + 1 < len(guards) else len(instructions)
        block = instructions[guard["index"]:end]
        record = {
            "id": rule_index + 1,
            "guard_offset": f"0x{guard['offset']:x}",
            "unit_id": guard["unit_id"],
            "unit_name": unit_names.get(guard["unit_id"], ""),
            "fields": {},
            "string_fields": {},
            "array_fields": {},
        }

        for ins in block:
            label = sys4load.display_label(ins.opcode)
            if (label == "lookup-array"
                    and len(ins.args) >= 3
                    and ins.args[1] == (T_GLOBAL_INT, CURRENT_UNIT_LEVELS)
                    and ins.args[2] == (T_GLOBAL_INT, CURRENT_UNIT_ID)):
                record["level_table"] = f"0x{CURRENT_UNIT_LEVELS:x}"
            elif (label == "gre"
                    and len(ins.args) >= 3
                    and ins.args[1][0] == 12
                    and ins.args[2][0] == T_IMM
                    and "level_table" in record):
                record["minimum_level"] = ins.args[2][1]
            elif (label == "lookup-array-2d"
                    and len(ins.args) >= 5
                    and ins.args[1] == (T_GLOBAL_INT, UNIT_CLASS_CHANGE_STATE)
                    and ins.args[2] == (T_GLOBAL_INT, CURRENT_UNIT_ID)
                    and ins.args[3] == (T_IMM, 10)
                    and ins.args[4][0] == T_IMM):
                record["class_change_slot_index"] = ins.args[4][1]
            elif (label == "ne"
                    and len(ins.args) >= 3
                    and ins.args[1] == (T_GLOBAL_INT, CURRENT_UNIT_ID)
                    and ins.args[2][0] == T_GLOBAL_INT):
                record["excluded_when_unit_equals_global"] = f"0x{ins.args[2][1]:x}"
            elif (ins.opcode == SET_STRING
                    and len(ins.args) >= 2
                    and ins.args[0] == (T_GLOBAL_STRING, CLASS_CHANGE_TITLE_OUT)):
                title = scr.strings.get(ins.args[1][1], ("",))[0]
                record["title"] = title
                record["name"] = title
                record["string_fields"][f"0x{CLASS_CHANGE_TITLE_OUT:x}"] = title
            elif (write := _static_global_write(ins)) is not None:
                destination, value = write
                if destination == CLASS_CHANGE_LEVEL_OUT:
                    record["selected_level"] = value
                    record["fields"][f"0x{destination:x}"] = value
                elif CLASS_CHANGE_SKILLS_OUT <= destination < CLASS_CHANGE_SKILLS_OUT + 4:
                    record["array_fields"][
                        f"0x{CLASS_CHANGE_SKILLS_OUT:x}/{destination - CLASS_CHANGE_SKILLS_OUT}"
                    ] = value
                elif CLASS_CHANGE_FLAGS_OUT <= destination < CLASS_CHANGE_FLAGS_OUT + 10:
                    record["array_fields"][
                        f"0x{CLASS_CHANGE_FLAGS_OUT:x}/{destination - CLASS_CHANGE_FLAGS_OUT}"
                    ] = value
            elif (label == "add"
                    and len(ins.args) >= 3
                    and ins.args[0][0] == T_GLOBAL_INT
                    and ins.args[0] == ins.args[1]
                    and ins.args[2][0] == T_IMM):
                destination = ins.args[0][1]
                value = ins.args[2][1]
                if destination == CLASS_CHANGE_COST_OUT:
                    record["fields"][f"0x{destination:x}"] = value
                elif CLASS_CHANGE_STATS_OUT <= destination < CLASS_CHANGE_STATS_OUT + 14:
                    record["array_fields"][
                        f"0x{CLASS_CHANGE_STATS_OUT:x}/{destination - CLASS_CHANGE_STATS_OUT}"
                    ] = value

        stat_bonuses = {
            UNIT_STAT_COLUMNS[int(key.split("/")[1])]: value
            for key, value in record["array_fields"].items()
            if key.startswith(f"0x{CLASS_CHANGE_STATS_OUT:x}/")
        }
        if stat_bonuses:
            record["stat_bonuses"] = stat_bonuses
        record["deployment_cost_delta"] = record["fields"].get(
            f"0x{CLASS_CHANGE_COST_OUT:x}", 0
        )
        skill_awards = []
        for key, skill_id in record["array_fields"].items():
            if not key.startswith(f"0x{CLASS_CHANGE_SKILLS_OUT:x}/") or skill_id <= 0:
                continue
            skill_awards.append({
                "skill_slot": int(key.split("/")[1]) + 1,
                "skill_id": skill_id,
                "skill_name": skill_names.get(skill_id, ""),
            })
        if skill_awards:
            record["skill_awards"] = skill_awards
        record["state_flag_indices_set"] = [
            int(key.split("/")[1])
            for key, value in record["array_fields"].items()
            if key.startswith(f"0x{CLASS_CHANGE_FLAGS_OUT:x}/") and value
        ]
        records.append(record)

    array_columns = sorted({
        key for record in records for key in record["array_fields"]
    }, key=lambda key: tuple(int(part, 0) for part in key.split("/")))
    string_columns = sorted({
        key for record in records for key in record["string_fields"]
    }, key=lambda key: int(key, 0))
    return records, {
        "rule_kind": "unit-class-change",
        "selector_global": f"0x{CURRENT_UNIT_ID:x}",
        "unit_level_table": f"0x{CURRENT_UNIT_LEVELS:x}",
        "persistent_state_table": f"0x{UNIT_CLASS_CHANGE_STATE:x}",
        "selection_policy": "highest selected_level among eligible unapplied rules",
        "array_layouts": {
            f"0x{CLASS_CHANGE_STATS_OUT:x}": {"length": 14},
            f"0x{CLASS_CHANGE_SKILLS_OUT:x}": {"length": 4},
            f"0x{CLASS_CHANGE_FLAGS_OUT:x}": {"length": 10},
        },
        "string_field_columns": string_columns,
        "array_field_columns": array_columns,
    }


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


def _resolve_parallel_record_overlaps(records):
    """Prefer an established parallel column over a row-table range collision.

    The global bank is flat, so a sufficiently large row-major table can
    contain an address that another INIT schema reaches as `base + entity_id`.
    A parallel base repeated by other records is stronger ownership evidence
    than one accidental in-range row/column calculation.
    """
    parallel_records = {}
    for record in records:
        for key in record.get("fields", {}):
            parallel_records.setdefault(int(key, 0), set()).add(record["id"])
    for record in records:
        retained = {}
        for key, value in record.get("record_fields", {}).items():
            base, stride, column = (int(part, 0) for part in key.split("/"))
            destination = base + record["id"] * stride + column
            parallel_base = destination - record["id"]
            if any(
                other_id != record["id"]
                for other_id in parallel_records.get(parallel_base, ())
            ):
                _store_unique(
                    record["fields"], f"0x{parallel_base:x}", value, record["id"]
                )
            else:
                retained[key] = value
        record["record_fields"] = retained


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
    _resolve_parallel_record_overlaps(records)
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


def extract_vocabulary(scr):
    """Extract VIINIT's sparse glossary names and pre-name row-table writes."""
    records = []
    by_id = {}
    for ins in scr.instructions:
        if (
            ins.opcode != SET_STRING
            or len(ins.args) < 2
            or ins.args[0][0] != T_GLOBAL_STRING
        ):
            continue
        record_id = ins.args[0][1] - VOCABULARY_NAME_ARRAY_BASE
        if not (1 <= record_id < VOCABULARY_RECORD_SPAN):
            raise ValueError(
                f"{scr.path.name}: glossary name outside reserved id span: "
                f"0x{ins.args[0][1]:x}"
            )
        text = scr.strings.get(ins.args[1][1], (None,))[0]
        record = {
            "id": record_id,
            "name": text,
            "fields": {},
            "record_fields": {},
        }
        records.append(record)
        by_id[record_id] = record

    for ins in scr.instructions:
        write = _static_global_write(ins)
        if write is None:
            continue
        destination, value = write
        relative = destination - VOCABULARY_RECORD_TABLE_BASE
        if not (0 <= relative < VOCABULARY_RECORD_SPAN * VOCABULARY_RECORD_STRIDE):
            raise ValueError(
                f"{scr.path.name}: unexpected integer write 0x{destination:x}"
            )
        record_id, column = divmod(relative, VOCABULARY_RECORD_STRIDE)
        if record_id not in by_id:
            raise ValueError(
                f"{scr.path.name}: integer write for unnamed glossary id {record_id}"
            )
        _store_unique(
            by_id[record_id]["record_fields"],
            (
                f"0x{VOCABULARY_RECORD_TABLE_BASE:x}/"
                f"{VOCABULARY_RECORD_STRIDE}/{column}"
            ),
            value,
            record_id,
        )

    record_columns = sorted({
        key for record in records for key in record["record_fields"]
    }, key=lambda key: tuple(int(part, 0) for part in key.split("/")))
    return records, {
        "name_array_base": f"0x{VOCABULARY_NAME_ARRAY_BASE:x}",
        "name_write_base": f"0x{VOCABULARY_NAME_ARRAY_BASE + 1:x}",
        "first_record_id": 1,
        "record_span": VOCABULARY_RECORD_SPAN,
        "record_field_columns": record_columns,
    }


def extract_character_names(scr):
    """Extract CNINIT's unit-id keyed display-name and voice-family arrays."""
    by_id: dict[int, dict] = {}
    integer_writes = 0
    string_writes = 0

    for ins in scr.instructions:
        write = _static_global_write(ins)
        if write is not None:
            destination, canonical_unit_id = write
            record_id = destination - CHARACTER_VOICE_FAMILY_ARRAY_BASE
            if not (
                1 <= record_id < CHARACTER_NAME_RECORD_SPAN
                and isinstance(canonical_unit_id, int)
            ):
                raise ValueError(
                    f"{scr.path.name}: unexpected integer write 0x{destination:x}"
                )
            if record_id in by_id:
                raise ValueError(
                    f"{scr.path.name}: duplicate unit-name row {record_id}"
                )
            by_id[record_id] = {
                "id": record_id,
                "name": None,
                "canonical_voice_unit_id": canonical_unit_id,
                "string_fields": {},
                "fields": {
                    f"0x{CHARACTER_VOICE_FAMILY_ARRAY_BASE:x}": canonical_unit_id,
                },
            }
            integer_writes += 1
            continue

        if (
            ins.opcode == SET_STRING
            and len(ins.args) >= 2
            and ins.args[0][0] == T_GLOBAL_STRING
        ):
            record_id = ins.args[0][1] - CHARACTER_NAME_ARRAY_BASE
            if not (1 <= record_id < CHARACTER_NAME_RECORD_SPAN):
                raise ValueError(
                    f"{scr.path.name}: unexpected name write 0x{ins.args[0][1]:x}"
                )
            if record_id not in by_id:
                raise ValueError(
                    f"{scr.path.name}: name without unit mapping for row {record_id}"
                )
            text = scr.strings.get(ins.args[1][1], (None,))[0]
            by_id[record_id]["name"] = text
            by_id[record_id]["string_fields"][
                f"0x{CHARACTER_NAME_ARRAY_BASE:x}"
            ] = text
            string_writes += 1
            continue

        if sys4load.display_label(ins.opcode) != "exit":
            raise ValueError(
                f"{scr.path.name}: unexpected opcode "
                f"{sys4load.display_label(ins.opcode)} at 0x{ins.offset:x}"
            )

    unit_definitions = {
        record["id"]: record
        for record in extract_name(sys4load.load(resolve("EBINIT")))[0]
    }
    unit_ids = set(by_id)
    definition_ids = set(unit_definitions)
    for record in by_id.values():
        unit_definition = unit_definitions.get(record["id"])
        canonical_definition = unit_definitions.get(
            record["canonical_voice_unit_id"]
        )
        if unit_definition:
            record["unit_definition_name"] = unit_definition["name"]
        if canonical_definition:
            record["canonical_voice_unit_name"] = canonical_definition["name"]
        record["voice_family_alias"] = (
            record["canonical_voice_unit_id"] != record["id"]
        )

    records = [by_id[record_id] for record_id in sorted(by_id)]
    return records, {
        "schema": "unit-display-names",
        "record_span": CHARACTER_NAME_RECORD_SPAN,
        "first_record_id": 1,
        "name_array_base": f"0x{CHARACTER_NAME_ARRAY_BASE:x}",
        "canonical_voice_unit_array_base": (
            f"0x{CHARACTER_VOICE_FAMILY_ARRAY_BASE:x}"
        ),
        "integer_write_count": integer_writes,
        "string_write_count": string_writes,
        "named_record_count": sum(record["name"] is not None for record in records),
        "unnamed_record_ids": [
            record["id"] for record in records if record["name"] is None
        ],
        "voice_family_alias_count": sum(
            record["voice_family_alias"] for record in records
        ),
        "unit_definition_table": "EBINIT",
        "joined_unit_definition_count": len(unit_ids & definition_ids),
        "cninit_ids_without_unit_definition": sorted(unit_ids - definition_ids),
        "unit_definition_ids_without_cninit": sorted(definition_ids - unit_ids),
    }


def extract_recovery_protocol(scr) -> dict:
    """Validate and describe RECOVER's resource/condition reset ABI."""
    lookups_2d = {
        (ins.args[1][1], ins.args[3][1])
        for ins in scr.instructions
        if (
            sys4load.display_label(ins.opcode) == "lookup-array-2d"
            and len(ins.args) >= 5
            and ins.args[1][0] == T_GLOBAL_INT
            and ins.args[3][0] == T_IMM
        )
    }
    lookups_1d = {
        ins.args[1][1]
        for ins in scr.instructions
        if (
            sys4load.display_label(ins.opcode) == "lookup-array"
            and len(ins.args) >= 3
            and ins.args[1][0] == T_GLOBAL_INT
        )
    }
    loop_bounds = {
        ins.args[2][1]
        for ins in scr.instructions
        if (
            sys4load.display_label(ins.opcode) == "lt"
            and len(ins.args) >= 3
            and ins.args[2][0] == T_IMM
        )
    }
    calls = {
        ins.args[0][1]
        for ins in scr.instructions
        if sys4load.display_label(ins.opcode) == "call-script" and ins.args
    }
    required_2d = {
        (RECOVER_EFFECTIVE_STATS, 14),
        (RECOVER_CURRENT_RESOURCES, 3),
        (RECOVER_CURRENT_LEVELS, CONDITION_RECORD_SPAN),
        (RECOVER_REMAINING_TURNS, CONDITION_RECORD_SPAN),
        (RECOVER_BASELINE_LEVELS, CONDITION_RECORD_SPAN),
    }
    failures = []
    if not required_2d <= lookups_2d:
        failures.append(f"missing 2d lookups {sorted(required_2d - lookups_2d)}")
    if RECOVER_POLICY not in lookups_1d:
        failures.append("missing recovery-policy lookup")
    if not {3, CONDITION_RECORD_SPAN} <= loop_bounds:
        failures.append("missing resource or condition loop bound")
    if not {0x329D, 0x2ADE} <= calls:
        failures.append("missing CALCREVISE or DRAWCHP post-call")
    if failures:
        raise ValueError(f"{scr.path.name}: " + "; ".join(failures))

    return {
        "source": scr.path.name,
        "current_entity_selector": f"0x{RECOVER_CURRENT_ENTITY:x}",
        "resource_restore": {
            "source_table": f"0x{RECOVER_EFFECTIVE_STATS:x}",
            "source_columns": ["max_hp", "max_sp", "max_fs"],
            "destination_table": f"0x{RECOVER_CURRENT_RESOURCES:x}",
            "destination_columns": ["current_hp", "current_sp", "current_fs"],
        },
        "condition_reset": {
            "column_count": CONDITION_RECORD_SPAN,
            "current_level_table": f"0x{RECOVER_CURRENT_LEVELS:x}",
            "baseline_level_table": f"0x{RECOVER_BASELINE_LEVELS:x}",
            "remaining_turns_table": f"0x{RECOVER_REMAINING_TURNS:x}",
            "recovery_policy_table": f"0x{RECOVER_POLICY:x}",
            "policy": (
                "For each active condition whose policy cell is nonzero, copy "
                "the equipment/passive baseline into the current level and set "
                "remaining turns to -1 when that baseline is nonzero, otherwise 0."
            ),
        },
        "post_recovery_scripts": ["CALCREVISE.BIN", "DRAWCHP.BIN"],
    }


def _condition_family_name(level_names: dict[int, str]) -> str | None:
    """Collapse authored `name1`..`name5` strings to their shared family."""
    if not level_names:
        return None
    ordered = [level_names[level] for level in sorted(level_names)]
    prefixes = [
        text[:-1]
        for level, text in sorted(level_names.items())
        if text.endswith(str(level))
    ]
    if len(prefixes) == len(ordered) and len(set(prefixes)) == 1:
        return prefixes[0]
    return ordered[0]


def extract_condition_definitions(scr):
    """Extract ILINIT's sparse 30-condition, five-level definition matrix."""
    level_names: dict[int, dict[int, str]] = collections.defaultdict(dict)
    records_by_id: dict[int, dict] = {}
    classified_writes = 0
    static_write_count = 0

    def record_for(condition_id: int) -> dict:
        if not (1 <= condition_id < CONDITION_RECORD_SPAN):
            raise ValueError(
                f"{scr.path.name}: condition id outside reserved span: {condition_id}"
            )
        return records_by_id.setdefault(condition_id, {
            "id": condition_id,
            "condition": CONDITION_COLUMNS.get(condition_id, f"reserved_{condition_id}"),
            "string_fields": {},
            "fields": {},
            "record_fields": {},
        })

    for ins in scr.instructions:
        if (
            ins.opcode == SET_STRING
            and len(ins.args) >= 2
            and ins.args[0][0] == T_GLOBAL_STRING
        ):
            relative = ins.args[0][1] - CONDITION_LEVEL_NAME_BASE
            condition_id, level_index = divmod(relative, CONDITION_LEVEL_COUNT)
            if not (
                1 <= condition_id < CONDITION_RECORD_SPAN
                and 0 <= level_index < CONDITION_LEVEL_COUNT
            ):
                raise ValueError(
                    f"{scr.path.name}: unexpected condition name destination "
                    f"0x{ins.args[0][1]:x}"
                )
            text = scr.strings.get(ins.args[1][1], (None,))[0]
            level_names[condition_id][level_index + 1] = text
            _store_unique(
                record_for(condition_id)["string_fields"],
                (
                    f"0x{CONDITION_LEVEL_NAME_BASE:x}/"
                    f"{CONDITION_LEVEL_COUNT}/{level_index}"
                ),
                text,
                condition_id,
            )
            continue

        write = _static_global_write(ins)
        if write is None:
            continue
        static_write_count += 1
        destination, value = write
        matched = False
        for base in CONDITION_SCALAR_ARRAYS:
            condition_id = destination - base
            if 1 <= condition_id < CONDITION_RECORD_SPAN:
                _store_unique(
                    record_for(condition_id)["fields"],
                    f"0x{base:x}",
                    value,
                    condition_id,
                )
                matched = True
                break
        if not matched:
            layouts = (
                (CONDITION_DURATION_BASE, CONDITION_LEVEL_COUNT),
                (
                    CONDITION_STAT_DELTA_BASE,
                    CONDITION_LEVEL_COUNT * len(CONDITION_STAT_COLUMNS),
                ),
                (
                    CONDITION_RESOURCE_DELTA_BASE,
                    CONDITION_LEVEL_COUNT * len(CONDITION_RESOURCE_COLUMNS),
                ),
            )
            for base, stride in layouts:
                relative = destination - base
                condition_id, column = divmod(relative, stride)
                if 1 <= condition_id < CONDITION_RECORD_SPAN:
                    _store_unique(
                        record_for(condition_id)["record_fields"],
                        f"0x{base:x}/{stride}/{column}",
                        value,
                        condition_id,
                    )
                    matched = True
                    break
        if not matched:
            raise ValueError(
                f"{scr.path.name}: unclassified static write 0x{destination:x}"
            )
        classified_writes += 1

    records = []
    for condition_id in sorted(records_by_id):
        record = records_by_id[condition_id]
        names = level_names.get(condition_id, {})
        record["name"] = _condition_family_name(names)
        record["level_names"] = [
            names.get(level) for level in range(1, CONDITION_LEVEL_COUNT + 1)
        ]
        levels = []
        for level in range(1, CONDITION_LEVEL_COUNT + 1):
            level_record = {"level": level}
            if level in names:
                level_record["name"] = names[level]

            duration_key = (
                f"0x{CONDITION_DURATION_BASE:x}/{CONDITION_LEVEL_COUNT}/{level - 1}"
            )
            if duration_key in record["record_fields"]:
                level_record["duration_turns"] = record["record_fields"][duration_key]

            stat_deltas = {}
            for stat_index, stat_name in enumerate(CONDITION_STAT_COLUMNS):
                column = (level - 1) * len(CONDITION_STAT_COLUMNS) + stat_index
                key = (
                    f"0x{CONDITION_STAT_DELTA_BASE:x}/"
                    f"{CONDITION_LEVEL_COUNT * len(CONDITION_STAT_COLUMNS)}/{column}"
                )
                if key in record["record_fields"]:
                    stat_deltas[stat_name] = record["record_fields"][key]
            if stat_deltas:
                level_record["stat_deltas"] = stat_deltas

            resource_deltas = {}
            for resource_index, resource_name in enumerate(CONDITION_RESOURCE_COLUMNS):
                column = (level - 1) * len(CONDITION_RESOURCE_COLUMNS) + resource_index
                key = (
                    f"0x{CONDITION_RESOURCE_DELTA_BASE:x}/"
                    f"{CONDITION_LEVEL_COUNT * len(CONDITION_RESOURCE_COLUMNS)}/{column}"
                )
                if key in record["record_fields"]:
                    resource_deltas[resource_name] = record["record_fields"][key]
            if resource_deltas:
                level_record["resource_deltas"] = resource_deltas

            if len(level_record) > 1:
                levels.append(level_record)
        record["levels"] = levels
        records.append(record)

    defined_ids = [record["id"] for record in records]
    record_columns = sorted(
        {
            key
            for record in records
            for key in record.get("record_fields", {})
        },
        key=lambda key: tuple(int(part, 0) for part in key.split("/")),
    )
    return records, {
        "schema": "condition-definitions",
        "record_span": CONDITION_RECORD_SPAN,
        "level_count": CONDITION_LEVEL_COUNT,
        "condition_columns": {
            str(index): name for index, name in CONDITION_COLUMNS.items()
        },
        "defined_condition_ids": defined_ids,
        "reserved_condition_ids": [
            condition_id
            for condition_id in range(1, CONDITION_RECORD_SPAN)
            if condition_id not in defined_ids
        ],
        "level_name_table": {
            "base": f"0x{CONDITION_LEVEL_NAME_BASE:x}",
            "stride": CONDITION_LEVEL_COUNT,
        },
        "record_field_columns": record_columns,
        "static_write_count": static_write_count,
        "classified_static_write_count": classified_writes,
        "recovery_protocol": extract_recovery_protocol(
            sys4load.load(resolve("RECOVER"))
        ),
    }


def extract_character_profiles(scr):
    """Extract CIINIT's profile-id keyed character-information registry."""
    records = []
    by_id = {}
    for ins in scr.instructions:
        if (
            ins.opcode != SET_STRING
            or len(ins.args) < 2
            or ins.args[0][0] != T_GLOBAL_STRING
        ):
            continue
        record_id = ins.args[0][1] - CHARACTER_PROFILE_NAME_ARRAY_BASE
        if not (1 <= record_id < CHARACTER_PROFILE_RECORD_SPAN):
            raise ValueError(
                f"{scr.path.name}: character name outside reserved id span: "
                f"0x{ins.args[0][1]:x}"
            )
        text = scr.strings.get(ins.args[1][1], (None,))[0]
        record = {"id": record_id, "name": text, "fields": {}}
        records.append(record)
        by_id[record_id] = record

    integer_arrays = (
        CHARACTER_PROFILE_UNIT_ARRAY_BASE,
        CHARACTER_PROFILE_PORTRAIT_ARRAY_BASE,
        CHARACTER_PROFILE_PORTRAIT_X_ARRAY_BASE,
        CHARACTER_PROFILE_PORTRAIT_Y_ARRAY_BASE,
    )
    for ins in scr.instructions:
        write = _static_global_write(ins)
        if write is None:
            continue
        destination, value = write
        matched = False
        for base in integer_arrays:
            relative = destination - base
            if 0 <= relative < CHARACTER_PROFILE_RECORD_SPAN:
                if relative not in by_id:
                    raise ValueError(
                        f"{scr.path.name}: integer write for unnamed character "
                        f"profile id {relative}"
                    )
                _store_unique(
                    by_id[relative]["fields"],
                    f"0x{base:x}",
                    value,
                    relative,
                )
                matched = True
                break
        if not matched:
            raise ValueError(
                f"{scr.path.name}: unexpected integer write 0x{destination:x}"
            )

    return records, {
        "schema": "character-information-profiles",
        "name_array_base": f"0x{CHARACTER_PROFILE_NAME_ARRAY_BASE:x}",
        "name_write_base": f"0x{CHARACTER_PROFILE_NAME_ARRAY_BASE + 1:x}",
        "first_record_id": 1,
        "record_span": CHARACTER_PROFILE_RECORD_SPAN,
        "unit_id_array_base": f"0x{CHARACTER_PROFILE_UNIT_ARRAY_BASE:x}",
        "portrait_asset_array_base": (
            f"0x{CHARACTER_PROFILE_PORTRAIT_ARRAY_BASE:x}"
        ),
        "portrait_x_offset_array_base": (
            f"0x{CHARACTER_PROFILE_PORTRAIT_X_ARRAY_BASE:x}"
        ),
        "portrait_y_offset_array_base": (
            f"0x{CHARACTER_PROFILE_PORTRAIT_Y_ARRAY_BASE:x}"
        ),
        "implicit_defaults": {
            f"0x{CHARACTER_PROFILE_PORTRAIT_X_ARRAY_BASE:x}": 0,
            f"0x{CHARACTER_PROFILE_PORTRAIT_Y_ARRAY_BASE:x}": 0,
        },
    }


def extract_magic_actions(scr):
    """Extract MAINIT's action-id keyed magic/research/growth registry.

    MAINIT has only one consecutive string column, so the generic name-table
    span heuristic cannot see its reserved 30-cell stride.  Its ten integer
    columns are equally spaced consumers of the same action id.
    """
    records = []
    by_id = {}
    for ins in scr.instructions:
        if (
            ins.opcode != SET_STRING
            or len(ins.args) < 2
            or ins.args[0][0] != T_GLOBAL_STRING
        ):
            continue
        record_id = ins.args[0][1] - MAGIC_ACTION_NAME_ARRAY_BASE
        if not (1 <= record_id < MAGIC_ACTION_RECORD_SPAN):
            raise ValueError(
                f"{scr.path.name}: magic-action name outside reserved id span: "
                f"0x{ins.args[0][1]:x}"
            )
        text = scr.strings.get(ins.args[1][1], (None,))[0]
        record = {"id": record_id, "name": text, "fields": {}}
        records.append(record)
        by_id[record_id] = record

    for ins in scr.instructions:
        write = _static_global_write(ins)
        if write is None:
            continue
        destination, value = write
        matched = False
        for base in MAGIC_ACTION_INTEGER_ARRAY_BASES:
            record_id = destination - base
            if 0 <= record_id < MAGIC_ACTION_RECORD_SPAN:
                if record_id not in by_id:
                    raise ValueError(
                        f"{scr.path.name}: integer write for unnamed magic "
                        f"action id {record_id}"
                    )
                _store_unique(
                    by_id[record_id]["fields"],
                    f"0x{base:x}",
                    value,
                    record_id,
                )
                matched = True
                break
        if not matched:
            raise ValueError(
                f"{scr.path.name}: unexpected integer write 0x{destination:x}"
            )

    return records, {
        "schema": "magic-actions",
        "name_array_base": f"0x{MAGIC_ACTION_NAME_ARRAY_BASE:x}",
        "name_write_base": f"0x{MAGIC_ACTION_NAME_ARRAY_BASE + 1:x}",
        "first_record_id": 1,
        "record_span": MAGIC_ACTION_RECORD_SPAN,
        "integer_array_bases": [
            f"0x{base:x}" for base in MAGIC_ACTION_INTEGER_ARRAY_BASES
        ],
        "handler_script_array_base": (
            f"0x{MAGIC_ACTION_HANDLER_ARRAY_BASE:x}"
        ),
        "implicit_default": 0,
    }


@cache
def object_type_definitions() -> dict[int, dict]:
    """Load OBINIT's authoritative display and state-row metadata by object type id."""
    records, _ = extract_name(sys4load.load(resolve("OBINIT")))
    return {
        record["id"]: {
            "name": record["name"],
            **({"description": record["desc"]} if record.get("desc") else {}),
            "uses_runtime_state_sprite_row": (
                record.get("fields", {}).get("0xe6dee") == 1
            ),
        }
        for record in records
    }


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


def extract_gallery_definitions(scr):
    """Extract CGINIT's sparse gallery-image registry.

    CGINIT owns one 2,000-by-2 asset table and three parallel 2,000-cell
    classification arrays. CGMODE uses the latter as a thumbnail-sheet,
    30-cell atlas slot, and per-slot variant ordinal; SAVE and SELSTAGE use
    the optional second asset as a 112-by-84 preview of the first.
    """
    records_by_id: dict[int, dict] = {}
    static_write_count = 0
    classified_write_count = 0

    def record_for(record_id: int) -> dict:
        if not (1 <= record_id < GALLERY_RECORD_SPAN):
            raise ValueError(
                f"{scr.path.name}: gallery id {record_id} outside reserved span"
            )
        return records_by_id.setdefault(record_id, {
            "id": record_id,
            "fields": {},
            "record_fields": {},
        })

    for ins in scr.instructions:
        write = _static_global_write(ins)
        if write is None:
            if sys4load.display_label(ins.opcode) != "exit":
                raise ValueError(
                    f"{scr.path.name}: unclassified instruction at 0x{ins.offset:x}"
                )
            continue
        static_write_count += 1
        destination, value = write
        if not isinstance(value, int):
            raise ValueError(
                f"{scr.path.name}: non-static gallery value at 0x{ins.offset:x}"
            )

        relative = destination - GALLERY_ASSET_TABLE_BASE
        if 0 <= relative < GALLERY_RECORD_SPAN * GALLERY_ASSET_STRIDE:
            record_id, column = divmod(relative, GALLERY_ASSET_STRIDE)
            record = record_for(record_id)
            _store_unique(
                record["record_fields"],
                (
                    f"0x{GALLERY_ASSET_TABLE_BASE:x}/"
                    f"{GALLERY_ASSET_STRIDE}/{column}"
                ),
                value,
                record_id,
            )
            classified_write_count += 1
            continue

        scalar_arrays = (
            GALLERY_THUMBNAIL_SHEET_ARRAY_BASE,
            GALLERY_THUMBNAIL_SLOT_ARRAY_BASE,
            GALLERY_VARIANT_ORDINAL_ARRAY_BASE,
        )
        for base in scalar_arrays:
            record_id = destination - base
            if 1 <= record_id < GALLERY_RECORD_SPAN:
                _store_unique(
                    record_for(record_id)["fields"],
                    f"0x{base:x}",
                    value,
                    record_id,
                )
                classified_write_count += 1
                break
        else:
            raise ValueError(
                f"{scr.path.name}: unclassified gallery write "
                f"0x{destination:x} at 0x{ins.offset:x}"
            )

    names = callscript_names()
    sheet_asset_ids = gallery_thumbnail_sheet_assets()
    records = [records_by_id[record_id] for record_id in sorted(records_by_id)]
    required_scalar_keys = {
        f"0x{GALLERY_THUMBNAIL_SHEET_ARRAY_BASE:x}",
        f"0x{GALLERY_THUMBNAIL_SLOT_ARRAY_BASE:x}",
        f"0x{GALLERY_VARIANT_ORDINAL_ARRAY_BASE:x}",
    }
    primary_key = (
        f"0x{GALLERY_ASSET_TABLE_BASE:x}/{GALLERY_ASSET_STRIDE}/0"
    )
    preview_key = (
        f"0x{GALLERY_ASSET_TABLE_BASE:x}/{GALLERY_ASSET_STRIDE}/1"
    )
    for record in records:
        if set(record["fields"]) != required_scalar_keys:
            raise ValueError(
                f"{scr.path.name}: gallery id {record['id']} has incomplete scalars"
            )
        if primary_key not in record["record_fields"]:
            raise ValueError(
                f"{scr.path.name}: gallery id {record['id']} has no image asset"
            )
        sheet_id = record["fields"][
            f"0x{GALLERY_THUMBNAIL_SHEET_ARRAY_BASE:x}"
        ]
        slot_id = record["fields"][
            f"0x{GALLERY_THUMBNAIL_SLOT_ARRAY_BASE:x}"
        ]
        variant_ordinal = record["fields"][
            f"0x{GALLERY_VARIANT_ORDINAL_ARRAY_BASE:x}"
        ]
        if sheet_id not in sheet_asset_ids:
            raise ValueError(
                f"{scr.path.name}: gallery id {record['id']} has bad sheet {sheet_id}"
            )
        if not (1 <= slot_id <= 30 and variant_ordinal >= 1):
            raise ValueError(
                f"{scr.path.name}: gallery id {record['id']} has bad "
                f"slot/variant {slot_id}/{variant_ordinal}"
            )
        image_asset_id = record["record_fields"][primary_key]
        sheet_asset_id = sheet_asset_ids[sheet_id]
        record.update({
            "gallery_image_asset_id": image_asset_id,
            "gallery_image_asset_name": names.get(image_asset_id, ""),
            "thumbnail_sheet_id": sheet_id,
            "thumbnail_sheet_asset_id": sheet_asset_id,
            "thumbnail_sheet_asset_name": names.get(sheet_asset_id, ""),
            "thumbnail_slot_id": slot_id,
            "variant_ordinal": variant_ordinal,
        })
        if preview_asset_id := record["record_fields"].get(preview_key):
            record["save_stage_preview_asset_id"] = preview_asset_id
            record["save_stage_preview_asset_name"] = names.get(
                preview_asset_id, ""
            )

    populated_ids = set(records_by_id)
    populated_min = min(populated_ids)
    populated_max = max(populated_ids)
    sheet_definitions = []
    for sheet_id, asset_id in sheet_asset_ids.items():
        sheet_definitions.append({
            "id": sheet_id,
            "asset_id": asset_id,
            "asset_name": names.get(asset_id, ""),
            "atlas_columns": 6,
            "atlas_rows": 5,
            "slot_count": 30,
        })
    return records, {
        "record_span": GALLERY_RECORD_SPAN,
        "populated_id_range": [populated_min, populated_max],
        "id_gaps_within_populated_range": [
            record_id
            for record_id in range(populated_min, populated_max + 1)
            if record_id not in populated_ids
        ],
        "asset_table_base": f"0x{GALLERY_ASSET_TABLE_BASE:x}",
        "asset_table_stride": GALLERY_ASSET_STRIDE,
        "thumbnail_sheet_array_base": (
            f"0x{GALLERY_THUMBNAIL_SHEET_ARRAY_BASE:x}"
        ),
        "thumbnail_slot_array_base": (
            f"0x{GALLERY_THUMBNAIL_SLOT_ARRAY_BASE:x}"
        ),
        "variant_ordinal_array_base": (
            f"0x{GALLERY_VARIANT_ORDINAL_ARRAY_BASE:x}"
        ),
        "record_field_columns": [primary_key, preview_key],
        "static_write_count": static_write_count,
        "classified_static_write_count": classified_write_count,
        "preview_asset_count": sum(
            preview_key in record["record_fields"] for record in records
        ),
        "thumbnail_sheets": sheet_definitions,
        "thumbnail_sheet_configuration": {
            "source": "INIT2.BIN",
            "base": f"0x{GALLERY_THUMBNAIL_SHEET_CONFIG_BASE:x}",
            "reserved_span": GALLERY_THUMBNAIL_SHEET_CONFIG_SPAN,
        },
        "consumer_contract": {
            "gallery": (
                "CGMODE groups records by thumbnail sheet and one of its "
                "thirty atlas slots, orders variants by the one-based ordinal, "
                "tests the primary image's unlock state, and displays it."
            ),
            "save_stage_preview": (
                "SAVE and SELSTAGE match the current image against the primary "
                "asset and use the optional second asset as a 112x84 preview."
            ),
        },
    }


@cache
def gallery_thumbnail_sheet_assets() -> dict[int, int]:
    """Read CGMODE's enabled thumbnail-sheet assets from INIT2."""
    script = sys4load.load(resolve("INIT2"))
    assets = {}
    for ins in script.instructions:
        write = _static_global_write(ins)
        if write is None:
            continue
        destination, value = write
        index = destination - GALLERY_THUMBNAIL_SHEET_CONFIG_BASE
        if (
            0 <= index < GALLERY_THUMBNAIL_SHEET_CONFIG_SPAN
            and isinstance(value, int)
            and value
        ):
            assets[index + 1] = value
    if not assets:
        raise ValueError("INIT2.BIN: no configured CGMODE thumbnail sheets")
    return assets


@cache
def callscript_names() -> dict[int, str]:
    """Load the generated packed script-resource id join."""
    try:
        data = json.loads(
            (paths.BUILD / "callscript-names.json").read_text(encoding="utf8")
        )
    except (OSError, json.JSONDecodeError):
        return {}
    return {int(key): value for key, value in data.items()}


@cache
def scjump_decision_chapters() -> tuple[dict[int, set[int]], int]:
    """Load SCJUMP's generated decision sites as independent correlation evidence."""
    try:
        data = json.loads(
            (paths.BUILD / "scjump-decisions.json").read_text(encoding="utf8")
        )
    except (OSError, json.JSONDecodeError):
        return {}, 0
    chapters: dict[int, set[int]] = {}
    for decision in data.get("decisions", []):
        chapter = decision.get("chapter")
        if isinstance(chapter, int):
            chapters.setdefault(decision["decision"], set()).add(chapter)
    return chapters, len(data.get("decisions", []))


def extract_dispatch(scr):
    """Extract SCINIT's decision -> scene-script registry without losing overwrites."""
    paired = _paired_parallel_writes(scr)
    if paired is None:
        return [], {}
    writes, span = paired
    primary_base = writes[0][1]
    chapter_base = primary_base + span
    names = callscript_names()
    scjump_chapters, decision_site_count = scjump_decision_chapters()
    records_by_id: dict[int, dict] = {}
    assignment_count = 0

    for index in range(0, len(writes), 2):
        primary, chapter = writes[index:index + 2]
        decision_id = primary[1] - primary_base
        script_resource_id = primary[2]
        assignment = {
            "offset": f"0x{primary[0]:x}",
            "script_resource_id": script_resource_id,
            "script_name": names.get(script_resource_id, ""),
            "authored_chapter": chapter[2],
        }
        record = records_by_id.setdefault(decision_id, {
            "id": decision_id,
            "assignments": [],
        })
        record["assignments"].append(assignment)
        assignment_count += 1

    chapter_match_count = 0
    chapter_mismatches = []
    resolved_script_count = 0
    overwritten_record_count = 0
    conflicting_chapter_record_count = 0
    for decision_id, record in records_by_id.items():
        assignments = record["assignments"]
        final = assignments[-1]
        script_resource_id = final["script_resource_id"]
        authored_chapter = final["authored_chapter"]
        record.update({
            "name": final["script_name"],
            "script_resource_id": script_resource_id,
            "script_name": final["script_name"],
            "authored_chapter": authored_chapter,
            "assignment_count": len(assignments),
            "fields": {
                f"0x{primary_base:x}": script_resource_id,
                f"0x{chapter_base:x}": authored_chapter,
            },
        })
        if final["script_name"]:
            resolved_script_count += 1
        if len(assignments) > 1:
            overwritten_record_count += 1
        if len({assignment["authored_chapter"] for assignment in assignments}) > 1:
            conflicting_chapter_record_count += 1
        if decision_id in scjump_chapters:
            expected = sorted(scjump_chapters[decision_id])
            record["scjump_chapters"] = expected
            matches = authored_chapter in scjump_chapters[decision_id]
            record["authored_chapter_matches_scjump"] = matches
            if matches:
                chapter_match_count += 1
            else:
                chapter_mismatches.append({
                    "decision_id": decision_id,
                    "authored_chapter": authored_chapter,
                    "scjump_chapters": expected,
                })

    records = [records_by_id[key] for key in sorted(records_by_id)]
    return records, {
        "selector_global": "0x62ccf",
        "script_resource_array_base": f"0x{primary_base:x}",
        "authored_chapter_array_base": f"0x{chapter_base:x}",
        "reserved_array_span": span,
        "assignment_count": assignment_count,
        "overwritten_record_count": overwritten_record_count,
        "conflicting_chapter_record_count": conflicting_chapter_record_count,
        "resolved_script_count": resolved_script_count,
        "scjump_decision_site_count": decision_site_count,
        "scjump_distinct_decision_count": len(scjump_chapters),
        "scjump_joined_record_count": sum(
            record["id"] in scjump_chapters for record in records
        ),
        "scjump_chapter_match_count": chapter_match_count,
        "scjump_chapter_mismatches": sorted(
            chapter_mismatches, key=lambda row: row["decision_id"]
        ),
    }


def _movement_provider_names(names: dict[int, str]) -> dict[int, str]:
    providers = {
        selector: names.get(0x32FB + selector, "")
        for selector in range(1, 19)
    }
    providers.update({
        51: names.get(0x330E, ""),
        52: names.get(0x330F, ""),
        53: names.get(0x3310, ""),
        61: names.get(0x3311, ""),
    })
    return providers


def _join_movement_provider_semantics(step: dict) -> tuple[int, int, int]:
    """Add selector-specific RTN_M semantics while retaining every raw bank."""
    selector = step.get("movement_provider_selector")
    schema = MOVEMENT_PROVIDER_PARAMETER_SCHEMAS.get(selector)
    if schema is None:
        return 0, 0, 0
    step["provider_behavior"] = schema["behavior"]
    joined = 0
    defaulted = 0
    defaults = schema.get("parameter_defaults", {})
    for raw_field, semantic_field in schema["parameter_fields"].items():
        if raw_field in step:
            step[semantic_field] = step[raw_field]
            joined += 1
        elif raw_field in defaults:
            step[semantic_field] = defaults[raw_field]
            defaulted += 1
    ignored_fields = {
        raw_field: step[raw_field]
        for raw_field in schema.get("ignored_parameter_fields", {})
        if raw_field in step
    }
    if ignored_fields:
        step["ignored_movement_parameters"] = ignored_fields
    return joined, defaulted, len(ignored_fields)


def extract_banked(scr):
    """Extract RTINIT's sparse routine sets across twenty parallel step banks."""
    writes = _routine_bank_writes(scr)
    if writes is None:
        return [], {}

    names = callscript_names()
    movement_providers = _movement_provider_names(names)
    battle_providers = {
        selector: names.get(0x32F6 + selector, "")
        for selector in range(1, 5)
    }
    records_by_id: dict[int, dict] = {}
    cell_assignments: dict[tuple[int, int, int], list[int]] = collections.defaultdict(list)
    bank_cells: dict[int, set[tuple[int, int]]] = collections.defaultdict(set)
    decoded_movement_step_count = 0
    decoded_movement_parameter_count = 0
    decoded_movement_defaulted_parameter_count = 0
    ignored_movement_parameter_count = 0

    for offset, destination, value, bank_index, record_id, slot in writes:
        bank_base = ROUTINE_BANK_ROOT + bank_index * ROUTINE_BANK_SPAN
        key = f"0x{bank_base:x}/{ROUTINE_RECORD_STRIDE}/{slot}"
        assignment = {
            "offset": f"0x{offset:x}",
            "bank_index": bank_index,
            "bank_base": f"0x{bank_base:x}",
            "role": ROUTINE_BANK_ROLES[bank_index],
            "slot": slot,
            "value": value,
        }
        record = records_by_id.setdefault(record_id, {
            "id": record_id,
            "assignments": [],
            "record_fields": {},
        })
        record["assignments"].append(assignment)
        record["record_fields"][key] = value
        cell_assignments[(bank_index, record_id, slot)].append(value)
        bank_cells[bank_index].add((record_id, slot))

    for record in records_by_id.values():
        final_by_bank_slot = {}
        for assignment in record["assignments"]:
            final_by_bank_slot[
                (assignment["bank_index"], assignment["slot"])
            ] = assignment["value"]

        movement_steps = []
        battle_steps = []
        for slot in range(ROUTINE_RECORD_STRIDE):
            movement = {
                ROUTINE_BANK_ROLES[bank]: final_by_bank_slot[(bank, slot)]
                for bank in range(10)
                if (bank, slot) in final_by_bank_slot
            }
            if movement:
                selector = movement.get("movement_provider_selector")
                step = {
                    "slot": slot,
                    **movement,
                    **(
                        {"provider_script": movement_providers.get(selector, "")}
                        if selector is not None else {}
                    ),
                }
                (
                    joined_parameter_count,
                    defaulted_parameter_count,
                    ignored_parameter_count,
                ) = _join_movement_provider_semantics(step)
                if selector in MOVEMENT_PROVIDER_PARAMETER_SCHEMAS:
                    decoded_movement_step_count += 1
                    decoded_movement_parameter_count += joined_parameter_count
                    decoded_movement_defaulted_parameter_count += (
                        defaulted_parameter_count
                    )
                    ignored_movement_parameter_count += ignored_parameter_count
                movement_steps.append(step)

            battle = {
                ROUTINE_BANK_ROLES[bank]: final_by_bank_slot[(bank, slot)]
                for bank in range(10, 20)
                if (bank, slot) in final_by_bank_slot
            }
            if battle:
                selector = battle.get("battle_provider_selector")
                battle_steps.append({
                    "slot": slot,
                    **battle,
                    **(
                        {"provider_script": battle_providers.get(selector, "")}
                        if selector is not None else {}
                    ),
                })
        if movement_steps:
            record["movement_steps"] = movement_steps
        if battle_steps:
            record["battle_steps"] = battle_steps

    records = [records_by_id[key] for key in sorted(records_by_id)]
    record_ids = set(records_by_id)
    used_movement_providers = sorted({
        step["movement_provider_selector"]
        for record in records
        for step in record.get("movement_steps", [])
    })
    used_battle_providers = sorted({
        step["battle_provider_selector"]
        for record in records
        for step in record.get("battle_steps", [])
    })
    bank_layouts = {}
    for bank_index, role in enumerate(ROUTINE_BANK_ROLES):
        base = ROUTINE_BANK_ROOT + bank_index * ROUTINE_BANK_SPAN
        cells = bank_cells.get(bank_index, set())
        bank_layouts[f"0x{base:x}"] = {
            "bank_index": bank_index,
            "family": "movement" if bank_index < 10 else "battle",
            "role": role,
            "reserved_empty": not cells,
            "populated_cell_count": len(cells),
            "populated_record_count": len({record_id for record_id, _ in cells}),
            "populated_slots": sorted({slot for _, slot in cells}),
        }
    record_columns = sorted(
        {
            key
            for record in records
            for key in record.get("record_fields", {})
        },
        key=lambda key: tuple(int(part, 0) for part in key.split("/")),
    )
    return records, {
        "schema": "routine-step-banks",
        "selector_global": f"0x{ROUTINE_SET_ID:x}",
        "step_index_global": f"0x{ROUTINE_STEP_INDEX:x}",
        "execution_state_global": f"0x{ROUTINE_EXECUTION_STATE:x}",
        "bank_root_base": f"0x{ROUTINE_BANK_ROOT:x}",
        "bank_span": ROUTINE_BANK_SPAN,
        "bank_count": ROUTINE_BANK_COUNT,
        "record_stride": ROUTINE_RECORD_STRIDE,
        "reserved_record_span": ROUTINE_RECORD_SPAN,
        "first_record_id": min(record_ids),
        "last_record_id": max(record_ids),
        "missing_record_ids": sorted(
            set(range(min(record_ids), max(record_ids) + 1)) - record_ids
        ),
        "assignment_count": len(writes),
        "populated_cell_count": len(cell_assignments),
        "overwritten_cell_count": sum(
            len(values) > 1 for values in cell_assignments.values()
        ),
        "conflicting_overwrite_count": sum(
            len(set(values)) > 1 for values in cell_assignments.values()
        ),
        "movement_step_count": sum(
            len(record.get("movement_steps", [])) for record in records
        ),
        "battle_step_count": sum(
            len(record.get("battle_steps", [])) for record in records
        ),
        "movement_provider_scripts": {
            str(selector): name
            for selector, name in sorted(movement_providers.items())
        },
        "movement_provider_parameter_schemas": {
            str(selector): {
                "provider_script": movement_providers.get(selector, ""),
                **schema,
            }
            for selector, schema in sorted(
                MOVEMENT_PROVIDER_PARAMETER_SCHEMAS.items()
            )
        },
        "decoded_movement_provider_count": len(
            MOVEMENT_PROVIDER_PARAMETER_SCHEMAS
        ),
        "decoded_movement_step_count": decoded_movement_step_count,
        "decoded_movement_parameter_count": decoded_movement_parameter_count,
        "decoded_movement_defaulted_parameter_count": (
            decoded_movement_defaulted_parameter_count
        ),
        "ignored_movement_parameter_count": ignored_movement_parameter_count,
        "battle_provider_scripts": {
            str(selector): name
            for selector, name in sorted(battle_providers.items())
        },
        "used_movement_provider_selectors": used_movement_providers,
        "used_battle_provider_selectors": used_battle_providers,
        "bank_layouts": bank_layouts,
        "record_field_columns": record_columns,
    }


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
    footer_keys = {
        key
        for record in records
        for key in record.get("footer_arrays", {})
    }
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
                if key in footer_keys:
                    # A footer copy owns the complete row beginning at this offset.
                    name = f"{name}.row_{row}"
                else:
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
                    semantic_fields[semantic_name] = (
                        value["values"] if container == "footer_arrays" else value
                    )
        if semantic_fields:
            record["semantic_fields"] = semantic_fields
        else:
            record.pop("semantic_fields", None)


def attach_stage_object_placements(
    records: list[dict], definitions: dict[int, dict] | None = None
) -> None:
    """Assemble STINIT's parallel object buffers into modder-facing slot records."""
    if definitions is None:
        definitions = object_type_definitions()
    known_fields = {
        "type_id": "0xe7389",
        "tile_x": "0xe7325",
        "tile_y": "0xe7357",
        "difficulty_mask": "0xe7483",
        "reinforcement_interval_turns": "0xe741f",
        "reinforcement_spawn_limit": "0xe7451",
    }
    for record in records:
        fields = record.get("array_fields", {})
        objects = []
        for slot in range(1, 50):
            type_key = f"{known_fields['type_id']}/{slot}"
            if type_key not in fields:
                continue
            type_id = fields[type_key]
            obj = {"slot": slot, "type_id": type_id}
            if definition := definitions.get(type_id):
                obj["type_name"] = definition["name"]
                if description := definition.get("description"):
                    obj["type_description"] = description
            for semantic_name, base in known_fields.items():
                if semantic_name == "type_id":
                    continue
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
            payload = {
                base: fields[key]
                for base in ("0xe73bb", "0xe73ed")
                if (key := f"{base}/{slot}") in fields
            }
            if type_id in (1, 2, 3, 4) and "0xe73bb" in payload:
                obj["initial_faction_id"] = payload.pop("0xe73bb")
            elif type_id in (6, 36):
                if "0xe73bb" in payload:
                    obj["destination_tile_x"] = payload.pop("0xe73bb")
                if "0xe73ed" in payload:
                    obj["destination_tile_y"] = payload.pop("0xe73ed")
            elif type_id in (7, 8):
                if "0xe73bb" in payload:
                    obj["item_id"] = payload.pop("0xe73bb")
                if "0xe73ed" in payload:
                    obj["item_quantity"] = payload.pop("0xe73ed")
            elif type_id == 28 and "0xe73bb" in payload:
                obj["card_generation_list_id"] = payload.pop("0xe73bb")
            elif 18 <= type_id <= 25 and "0xe73bb" in payload:
                obj["non_triggering_faction_id"] = payload.pop("0xe73bb")
            elif (definition
                  and definition["uses_runtime_state_sprite_row"]
                  and "0xe73bb" in payload):
                obj["initial_object_state_id"] = payload.pop("0xe73bb")
            elif type_id == 27 and payload:
                # FIELD's dedicated otherworld-gate branch consumes the common
                # schedule and coordinates, then calls ADDEN's hard-coded slot-0
                # special-unit path. It never reads either tagged payload cell.
                obj["ignored_payload_fields"] = payload
                payload = {}
            unknown = payload
            if unknown:
                obj["unknown_fields"] = unknown
            objects.append(obj)
        record["object_placements"] = objects


def attach_stage_enemy_spawns(records: list[dict]) -> None:
    """Assemble STINIT's parallel enemy buffers into modder-facing slot records."""
    direct_fields = {
        "unit_id": "0xe7811",
        "faction_id": "0xe7799",
        "difficulty_mask": "0xe77b7",
        "min_level": "0xe782f",
        "max_level": "0xe784d",
        "auto_level_scale_divisor": "0xe786b",
    }
    optional_fields = {
        "tile_x": "0xe773f",
        "tile_y": "0xe775d",
        "object_slot": "0xe777b",
        "random_selection_weight": "0xe77f3",
    }
    routine_fields = {
        "movement_routine_set_ids": ("0xe7889", 3),
        "battle_routine_set_ids": ("0xe78e3", 3),
    }
    for record in records:
        fields = record.get("array_fields", {})
        footer_arrays = record.get("footer_arrays", {})
        spawns = []
        # Slot zero is reserved by ADDEN for its synthesized special-unit path.
        for slot in range(1, 30):
            unit_key = f"{direct_fields['unit_id']}/{slot}"
            if unit_key not in fields:
                continue
            spawn = {"slot": slot}
            for semantic_name, base in direct_fields.items():
                key = f"{base}/{slot}"
                # Faction zero is the buffer default and is meaningful to SETEN.
                if key in fields:
                    spawn[semantic_name] = fields[key]
                elif semantic_name == "faction_id":
                    spawn[semantic_name] = 0
            for semantic_name, base in optional_fields.items():
                if (key := f"{base}/{slot}") in fields:
                    spawn[semantic_name] = fields[key]
            for semantic_name, (base, stride) in routine_fields.items():
                key = f"{base}/{slot * stride}"
                if key in footer_arrays:
                    spawn[semantic_name] = footer_arrays[key]["values"]
            required = [
                fields[key]
                for column in range(7)
                if (key := f"0xe793d/{slot * 7 + column}") in fields
                and fields[key] > 0
            ]
            forbidden = [
                fields[key]
                for column in range(5)
                if (key := f"0xe7a0f/{slot * 5 + column}") in fields
                and fields[key] > 0
            ]
            if required:
                spawn["required_story_flags"] = required
            if forbidden:
                spawn["forbidden_story_flags"] = forbidden
            unknown = {}
            if (mode_key := f"0xe77d5/{slot}") in fields:
                mode = fields[mode_key]
                if mode == 2:
                    spawn["first_clear_only"] = True
                else:
                    unknown["0xe77d5"] = mode
            if unknown:
                spawn["unknown_fields"] = unknown
            spawns.append(spawn)
        record["enemy_spawns"] = spawns


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
        "layout-specific text (title/description, summary/strategy, biography, or description-only), furigana,",
        "and bytecode dispatch offset separately from the",
        "short description stored by the INIT script.",
        "Top-level `field_semantics` maps raw array/row-column keys to canonical machine-readable",
        "names from `vm-map/globals.toml`; each record's generated `semantic_fields` is the joined",
        "name-keyed convenience view. Complete footer copies expose their row values there while",
        "raw keys and footer metadata remain intact as bytecode provenance.",
        "",
        "ILINIT's dedicated condition schema exposes thirteen authored condition ids in the",
        "reserved 30-by-5 layout. Each record keeps its raw scalar and row-table cells while",
        "joining level names, durations, eleven-stat deltas, three-resource deltas, boss/recovery",
        "policies, and icon ids. Top-level `recovery_protocol` validates RECOVER.BIN and links",
        "current levels, equipment/passive baselines, remaining turns, and full resource restore.",
        "",
        "CNINIT's dedicated unit-name schema exposes 277 sparse EBINIT-keyed rows in two",
        "parallel 1,000-cell arrays: story/display names and canonical voice-family unit ids.",
        "Every row joins both its own EBINIT definition and the representative voice-family",
        "definition; deliberately empty names and variant aliases remain explicit.",
        "",
        "CGINIT's dedicated gallery schema exposes 851 sparse ids in a reserved 2,000-row",
        "layout. Each row joins its full-size image asset, one of four 30-cell thumbnail",
        "atlases, its atlas slot and variant ordinal, and the optional 112-by-84 preview",
        "used by SAVE and SELSTAGE. Raw global-array provenance remains beside these joins.",
        "",
        "Mixed-mode tables preserve the sparse selector id, branch offset, condition strings,",
        "scalar fields, cells within preallocated buffers, and length-prefixed footer arrays.",
        "STINIT additionally joins confirmed parallel buffers into per-slot `object_placements`",
        "and `enemy_spawns`. Its object type ids join to OBINIT's authoritative names and",
        "available descriptions; consumer-proven tagged payload variants receive semantic names while",
        "engine-dead tagged writes remain in `ignored_payload_fields` and unresolved",
        "type-specific/mode parameters remain in `unknown_fields`.",
        "",
        "Rule-mode tables preserve source-order rule ids and bytecode guard offsets while",
        "joining their predicates and shared-buffer effects. CCINIT exposes unit/level/applied-slot-index",
        "eligibility, titles, deployment-cost and named stat deltas, awarded SKINIT skills, and",
        "the persistent state slot set by each class change. Raw output addresses remain beside",
        "the joined EBINIT unit and SKINIT skill names.",
        "",
        "Dispatch-mode tables preserve SCINIT's complete source-ordered assignment history",
        "while exposing the final sparse decision-id registry. Packed resource ids join to",
        "SYS4INI script names, authored chapter tags correlate with SCJUMP's decoded decision",
        "sites, and legacy/stale chapter mismatches remain explicit.",
        "",
        "Banked-mode tables preserve RTINIT's twenty parallel 1000-by-20 routine banks,",
        "source-ordered overwrites, and final row/slot values. Joined movement and battle",
        "steps resolve provider selectors to RTN_M/RTN_B scripts while provider-specific",
        "parameter banks retain structural names until their individual consumers prove more.",
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
        "rules": extract_class_change_rules,
        "dispatch": extract_dispatch,
        "banked": extract_banked,
    }[mode]
    if mode == "name" and name == "VIINIT":
        extractor = extract_vocabulary
    elif mode == "name" and name == "CNINIT":
        extractor = extract_character_names
    elif mode == "name" and name == "CIINIT":
        extractor = extract_character_profiles
    elif mode == "name" and name == "MAINIT":
        extractor = extract_magic_actions
    elif mode == "name" and name == "ILINIT":
        extractor = extract_condition_definitions
    elif mode == "numeric" and name == "CGINIT":
        extractor = extract_gallery_definitions
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
        meta["object_definition_table"] = "OBINIT"
        attach_stage_object_placements(recs)
        attach_stage_enemy_spawns(recs)
    out = {"table": name, "source": scr.path.name, "magic": scr.magic, "mode": mode,
           "record_count": len(recs), **meta,
           "field_columns": cols if mode != "footer" else None,
           "field_semantics": semantics, "records": recs}
    outpath = paths.BUILD / "data" / f"{outname}.json"
    outpath.parent.mkdir(parents=True, exist_ok=True)
    outpath.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
    write_data_index(outpath.parent)
    print(f"{name}: mode={mode}, {len(recs)} records"
          + (
              f", {len(meta.get('record_field_columns', []))} record-columns"
              if mode == "banked"
              else f", {len(cols)} field-columns" if mode != "footer" else ""
          )
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
