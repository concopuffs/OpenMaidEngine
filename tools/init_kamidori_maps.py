#!/usr/bin/env python3
"""Kamidori map / stage / world tables.

  MPINIT   floor maps: `if lab==A && floor==F { width=W; height=H; copy 2H inline rows }`
           into the doubled-coordinate 53-wide template (tiles at even (x, y), edges between).
  STINIT2  stage catalogue: straight-line writes keyed by (addr - base) // stride.
  LAINIT   terrain definitions (span 40) and texture-slot default assets.
  STINIT   per-stage loader: `eq (global 0x378b) STAGE` blocks writing the current-stage
           scratch arrays (header, 50 objects, 30 enemies, 10 allies, strings).
  OBINIT   map object types (span 100).
  COINIT   costume skill boards: `eq (global 0x2d2846) ITEM` blocks with a 33x33 node/link
           grid and a 17x17 unlock-flag grid.
  AMINIT2  world areas (span 100); AMINIT area order keys and unlock conditions.
  DIINIT   dig (gathering) tables [200][10]; ARINIT arena battles (span 100).

Every layout is absolute, so `$n$` layers decode with the same constants. Layout evidence:
docs/kamidori-data-tables.md.
"""
from __future__ import annotations

from init_kamidori_common import (
    COPY_LOCAL_ARRAY, SET_STRING, T_GLOBAL_INT, T_GLOBAL_STRING, T_IMM,
    label, literal, read_footer_array, selector_guards, static_global_write, string_writes,
)

# ---------------------------------------------------------------- MPINIT ---
FLOOR_TEMPLATE_BASE = 0x378E      # int[53x53]; MPINIT zeroes 53*53 cells from here at @0x0
GRID_STRIDE = 53
LAB_SELECTOR = 0x428E             # current labyrinth id (-1 = non-labyrinth battle maps)
FLOOR_SELECTOR = 0x428F           # current floor number within the labyrinth
WIDTH_GLOBAL = 0x4287             # floor width in tiles
HEIGHT_GLOBAL = 0x4288            # floor height in tiles
CELL_KIND = {  # (grid_x even?, grid_y even?) -> cell role in the doubled-coordinate lattice
    (True, True): "tile",
    (False, True): "h_edge",      # between tile (x-1)/2 and (x+1)/2 on the same row
    (True, False): "v_edge",      # between tile rows
    (False, False): "corner",     # always 0 in shipped data
}
MPINIT_PASSTHROUGH = ("and", "jcc", "jmp", "exit", "mul", "copy-to-global", "eq", "mov")


def _signed(value):
    return value - (1 << 32) if value is not None and value >= 1 << 31 else value


def extract_floor_maps(scr):
    """MPINIT: `if lab==A && floor==F { width=W; height=H; copy 2H footer rows }` blocks.

    Each footer copy targets FLOOR_TEMPLATE_BASE + y*53 + 1 for y = 1..2H and carries 2W
    cells (grid columns 1..2W). Tiles live at even (x, y) = (2*tx, 2*ty), tx 1..W, ty 1..H.
    """
    locals_: dict[int, int] = {}
    cond: dict[int, int | None] = {}
    records: list[dict] = []
    cur: dict | None = None
    unclassified: list[str] = []
    for ins in scr.instructions:
        op, a = label(ins), ins.args
        if (op == "sub" and a[0][0] != T_GLOBAL_INT
                and a[1][0] == T_IMM and a[2][0] == T_IMM):
            locals_[a[0][1]] = a[1][1] - a[2][1]          # `sub local, 0, 1` -> -1 lab selector
            continue
        if op == "eq" and a[1][0] == T_GLOBAL_INT and a[1][1] in (LAB_SELECTOR, FLOOR_SELECTOR):
            value = a[2][1] if a[2][0] == T_IMM else locals_.get(a[2][1])
            cond[a[1][1]] = _signed(value)
            continue
        if op == "mov" and a[0][0] == T_GLOBAL_INT and a[0][1] in (WIDTH_GLOBAL, HEIGHT_GLOBAL):
            if a[0][1] == WIDTH_GLOBAL:
                if a[1][1] == 0 and not cond:             # prologue `mov width 0`
                    continue
                cur = {"labyrinth_id": cond.get(LAB_SELECTOR), "floor_no": cond.get(FLOOR_SELECTOR),
                       "width_tiles": a[1][1], "height_tiles": None,
                       "offset": f"0x{ins.offset:x}", "grid_rows": {}}
                records.append(cur)
            elif cur is not None:
                cur["height_tiles"] = a[1][1]
            continue
        if op == "copy-inline-int-array" and a[0][0] == T_GLOBAL_INT and cur is not None:
            values = read_footer_array(scr, a[1][1])
            gy, gx = divmod(a[0][1] - FLOOR_TEMPLATE_BASE, GRID_STRIDE)
            if gx != 1 or values is None:
                raise ValueError(f"{scr.path.name}@0x{ins.offset:x}: row at grid ({gx},{gy})")
            cur["grid_rows"][gy] = values
            continue
        if op in MPINIT_PASSTHROUGH:
            continue
        unclassified.append(f"0x{ins.offset:x}:{op}")
    for r in records:
        w, h = r["width_tiles"], r["height_tiles"]
        rows = r.pop("grid_rows")
        if sorted(rows) != list(range(1, 2 * h + 1)) or any(len(v) != 2 * w for v in rows.values()):
            raise ValueError(f"map {r['labyrinth_id']}/{r['floor_no']}: row shape mismatch")
        # full grid[y][x] for y,x in 0..2h / 0..2w (row/col 0 are the zero frame)
        grid = [[0] * (2 * w + 1) for _ in range(2 * h + 1)]
        for gy, vals in rows.items():
            for i, v in enumerate(vals):
                if gy <= 2 * h and i + 1 <= 2 * w:
                    grid[gy][i + 1] = v
        r.update({
            "id": f"{r['labyrinth_id']}:{r['floor_no']}",
            "grid_origin": "grid[0][0] == template cell 0x378e; tile (tx,ty) at grid[2ty][2tx]",
            "grid": grid,
            # terrain id of each tile, [ty-1][tx-1]
            "tiles": [[grid[2 * ty][2 * tx] for tx in range(1, w + 1)] for ty in range(1, h + 1)],
            # edge between (tx,ty) and (tx+1,ty): [ty-1][tx-1]
            "h_edges": [[grid[2 * ty][2 * tx + 1] for tx in range(1, w)] for ty in range(1, h + 1)],
            # edge between (tx,ty) and (tx,ty+1): [ty-1][tx-1]
            "v_edges": [[grid[2 * ty + 1][2 * tx] for tx in range(1, w + 1)] for ty in range(1, h)],
            "terrain_ids_used": sorted({v for row in grid for v in row} - {0}),
        })
    return records, {"unclassified": unclassified, "map_count": len(records),
                     "id": "labyrinth_id:floor_no"}


def render_ascii(rec) -> str:
    """Debug view of one extracted floor map's doubled grid."""
    out = [f"# labyrinth {rec['labyrinth_id']} floor {rec['floor_no']}  "
           f"({rec['width_tiles']}x{rec['height_tiles']} tiles)"]
    for row in rec["grid"]:
        out.append(" ".join(f"{v:2d}" if v else " ." for v in row))
    return "\n".join(out)


# ---------------------------------------------------------------- STINIT2 ---
STAGE_CAPACITY = 1000
STAGE_NAME_BASE = 0x24B3
STAGE_TABLES = {  # base -> (stride, field name)
    0x140D4C: (1, "select_background_asset_id"),
    0x141134: (1, "labyrinth_id"),
    0x14151C: (1, "floor_no"),
    0x141904: (10, "required_party_unit_ids"),          # cols 0-4 first run, 5-9 once cleared/boss
    0x144014: (1, "prerequisite_labyrinth_id"),
    0x1443FC: (1, "prerequisite_floor_no"),
    0x1447E4: (7, "required_story_flag_ids"),
    0x14633C: (7, "forbidden_story_flag_ids"),
    0x147E94: (1, "recommended_level"),
    0x14827C: (4, "objective_money_rewards"),
    0x14921C: (4, "objective_item_rewards"),
    0x14A1BC: (4, "objective_evaluation_rewards"),
    0x14B15C: (4, "objective_bonus_point_rewards"),
    0x14C0FC: (1, "unknown_14c0fc"),
    0x14C4E4: (1, "boss_stage_flag"),
    0x14C8CC: (1, "event_entry_flag"),
    0x14CCB4: (1, "select_sort_key"),
}


def _stage_slot(addr: int):
    for base, (stride, name) in STAGE_TABLES.items():
        if base <= addr < base + STAGE_CAPACITY * stride:
            rid, col = divmod(addr - base, stride)
            return name, stride, rid, col
    return None


def extract_stage_catalog(scr):
    """STINIT2 is straight-line `mov`/`set-string`; key every write by (addr - base) // stride.

    The generic name-mode grouping mis-keys labyrinth/floor: those writes come *before* each
    record's name, so instruction order cannot be used to assign them.
    """
    recs: dict[int, dict] = {}
    unclassified = []
    for ins in scr.instructions:
        op, a = label(ins), ins.args
        if op == "set-string" and a[0][0] != T_IMM:
            rid = a[0][1] - STAGE_NAME_BASE
            recs.setdefault(rid, {"id": rid})["name"] = literal(scr, a[1])
            continue
        write = static_global_write(ins)
        if write is None:
            if op != "exit":
                unclassified.append(f"0x{ins.offset:x}:{op}")
            continue
        addr, val = write
        slot = _stage_slot(addr)
        if slot is None:
            unclassified.append(f"0x{ins.offset:x}:0x{addr:x}")
            continue
        name, stride, rid, col = slot
        rec = recs.setdefault(rid, {"id": rid})
        if stride == 1:
            rec[name] = val
        else:
            rec.setdefault(name, [0] * stride)[col] = val
    return [recs[k] for k in sorted(recs)], {"unclassified": unclassified}


# ---------------------------------------------------------------- LAINIT ---
TERRAIN_CAPACITY = 40
TERRAIN_NAME_BASE = 0x289B
TERRAIN_PARALLEL = {
    0x2A3511: "texture_slot",
    0x2A3539: "room_join_flag",
    0x2A3561: "visibility_class",
    0x2A3589: "element_id",
    0x2A35B1: "move_cost",
}
TERRAIN_REQUIRED_SKILLS = 0x2A35D9    # int[40x2]
TEXTURE_DEFAULTS = 0x2A3629           # int[20] indexed by texture slot


def extract_terrain_definitions(scr):
    recs: dict[int, dict] = {}
    textures: dict[int, int] = {}
    unclassified = []
    for ins in scr.instructions:
        op, a = label(ins), ins.args
        if op == "set-string" and a[0][0] != T_IMM:
            tid = a[0][1] - TERRAIN_NAME_BASE
            recs.setdefault(tid, {"id": tid})["name"] = literal(scr, a[1])
            continue
        write = static_global_write(ins)
        if write is None:
            if op != "exit":
                unclassified.append(f"0x{ins.offset:x}:{op}")
            continue
        addr, val = write
        for base, name in TERRAIN_PARALLEL.items():
            if base <= addr < base + TERRAIN_CAPACITY:
                recs.setdefault(addr - base, {"id": addr - base})[name] = val
                break
        else:
            if TERRAIN_REQUIRED_SKILLS <= addr < TERRAIN_REQUIRED_SKILLS + 2 * TERRAIN_CAPACITY:
                tid, col = divmod(addr - TERRAIN_REQUIRED_SKILLS, 2)
                recs.setdefault(tid, {"id": tid}).setdefault("required_skill_ids", [0, 0])[col] = val
            elif TEXTURE_DEFAULTS <= addr < TEXTURE_DEFAULTS + 20:
                textures[addr - TEXTURE_DEFAULTS] = val
            else:
                unclassified.append(f"0x{ins.offset:x}:0x{addr:x}")
    return [recs[k] for k in sorted(recs)], {"texture_slot_default_asset_ids": textures,
                                              "unclassified": unclassified}


# ---------------------------------------------------------------- STINIT ---
STAGE_ID = 0x378B          # current stage id (STINIT selector)
# (base, slots, stride, field-name)
STINIT_INT_ARRAYS = [
    (0x140414, 2, 1, "background_texture_asset_ids"),
    (0x140416, 1, 1, "map_tint_color"),
    (0x140417, 1, 1, "bgm_id"),
    (0x140418, 2, 1, "turn_limit_outcome"),            # [revisit row]
    (0x14041A, 2, 1, "turn_limit"),                    # [revisit row]
    (0x14041C, 1, 1, "keep_conditions_on_revisit"),
    (0x14041D, 3, 1, "victory_capture_object_slots"),  # -1 in [0] = all bases
    (0x140420, 1, 1, "victory_defeat_unit"),           # -1 all hostiles, -2 all bosses, >0 unit id
    (0x140421, 1, 1, "victory_story_flag"),
    (0x140422, 1, 1, "defeat_base_loss_mode"),
    (0x140423, 1, 1, "defeat_story_flag"),
    (0x140424, 4, 2, "reward_row_required_story_flags"),
    (0x14042C, 5, 1, "faction_alliance"),
    (0x140431, 1, 1, "first_visit_party_restriction"),
    (0x140432, 20, 1, "map_texture_asset_overrides"),
]
OBJ = 50
OBJ_ARRAYS = [
    (0x140446, "tile_x"), (0x140478, "tile_y"), (0x1404AA, "type_id"),
    (0x1404DC, "base_value"), (0x14050E, "primary_param"), (0x140540, "secondary_param"),
    (0x140572, "reinforcement_interval_turns"), (0x1405A4, "reinforcement_spawn_limit"),
    (0x1405D6, "difficulty_mask"),
]
OBJ_TABLES = [
    (0x140608, 7, "required_story_flags"), (0x140766, 5, "forbidden_story_flags"),
    (0x140860, 5, "use_story_flags_by_faction"),
]
ENEMY = 30
ENEMY_ARRAYS = [
    (0x14095A, "tile_x"), (0x140978, "tile_y"), (0x140996, "spawn_object_slot"),
    (0x1409B4, "faction_id"), (0x1409D2, "difficulty_mask"), (0x1409F0, "first_visit_only"),
    (0x140A0E, "random_selection_weight"), (0x140A2C, "unit_id"), (0x140A4A, "min_level"),
    (0x140A68, "max_level"), (0x140A86, "auto_level_scale_divisor"),
]
ENEMY_TABLES = [
    (0x140AA4, 3, "movement_routine_set_ids"), (0x140AFE, 3, "battle_routine_set_ids"),
    (0x140B58, 7, "required_story_flags"), (0x140C2A, 5, "forbidden_story_flags"),
]
ALLY = 10
ALLY_ARRAYS = [
    (0x140CC0, "tile_x"), (0x140CCA, "tile_y"), (0x140CD4, "forced_unit_id"),
    (0x140CDE, "first_visit_only"),
]
ALLY_TABLES = [(0x140CE8, 5, "required_story_flags"), (0x140D1A, 5, "forbidden_story_flags")]
STAGE_STRINGS = [
    (0x249F, 12, "objective_texts"),        # [revisit row][6]: 3 victory + 3 defeat lines
    (0x24AB, 4, "description_lines"),
    (0x24AF, 4, "reward_condition_texts"),
]


def _locate(dest: int):
    for base, n, stride, name in STINIT_INT_ARRAYS:
        if base <= dest < base + n * stride:
            off = dest - base
            if n == 1:
                return ("header", name, None, None)
            if stride == 1:
                return ("header", name, off, None)
            return ("header", name, off // stride, off % stride)
    for group, count, arrays, tables in (
        ("objects", OBJ, OBJ_ARRAYS, OBJ_TABLES),
        ("enemies", ENEMY, ENEMY_ARRAYS, ENEMY_TABLES),
        ("allies", ALLY, ALLY_ARRAYS, ALLY_TABLES),
    ):
        for base, name in arrays:
            if base <= dest < base + count:
                return (group, name, dest - base, None)
        for base, stride, name in tables:
            if base <= dest < base + count * stride:
                slot, col = divmod(dest - base, stride)
                return (group, name, slot, col)
    return None


def _put(rec, loc, value):
    group, name, slot, col = loc
    if group == "header":
        header = rec.setdefault("header", {})
        if slot is None:
            header[name] = value
        elif col is None:
            header.setdefault(name, {})[str(slot)] = value
        else:
            header.setdefault(name, {}).setdefault(str(slot), {})[str(col)] = value
        return
    entry = rec.setdefault(group, {}).setdefault(str(slot), {})
    if col is None:
        entry[name] = value
    else:
        entry.setdefault(name, {})[str(col)] = value


def extract_stage_loader(scr):
    """STINIT / $n$STINIT: per-stage writes into the current-stage scratch arrays."""
    records, unmapped = [], []
    guards = selector_guards(scr, STAGE_ID)
    for sid, start, end in guards:
        rec = {"id": sid}
        empty = True
        for x in scr.instructions[start:end]:
            op = label(x)
            if x.opcode == SET_STRING and x.args and x.args[0][0] == T_GLOBAL_STRING:
                d = x.args[0][1]
                text = literal(scr, x.args[1])
                for base, n, name in STAGE_STRINGS:
                    if base <= d < base + n:
                        rec.setdefault("strings", {}).setdefault(name, {})[str(d - base)] = text
                        break
                else:
                    unmapped.append((sid, hex(x.offset), hex(d), text))
                empty = False
                continue
            if x.opcode == COPY_LOCAL_ARRAY and x.args[0][0] == T_GLOBAL_INT:
                d = x.args[0][1]
                for k, v in enumerate(read_footer_array(scr, x.args[1][1]) or []):
                    loc = _locate(d + k)
                    if loc:
                        _put(rec, loc, v)
                    else:
                        unmapped.append((sid, hex(x.offset), hex(d + k), v))
                empty = False
                continue
            write = static_global_write(x)
            if write is not None:
                d, v = write
                loc = _locate(d)
                if loc:
                    _put(rec, loc, v)
                else:
                    unmapped.append((sid, hex(x.offset), hex(d), v))
                empty = False
                continue
            if op in ("jmp", "exit"):
                continue
            unmapped.append((sid, hex(x.offset), op, None))
        if not empty:
            records.append(rec)
    meta = {"selector": f"0x{STAGE_ID:x}", "guards": len(guards),
            "records_with_writes": len(records), "unmapped": unmapped[:50],
            "unmapped_count": len(unmapped)}
    return records, meta


# ---------------------------------------------------------------- OBINIT ---
OBJECT_SPAN = 100
OBJECT_NAME_BASE = 0x28C3
OBJECT_DESC_BASE = 0x2927      # string[100][2] at 0x2927 + id*2 + line
OBJECT_PARALLEL = {
    0x2A363E: "sprite_asset_id", 0x2A36A2: "sprite_width", 0x2A3706: "sprite_height",
    0x2A376A: "animation_frame_count", 0x2A37CE: "state_mode", 0x2A3832: "available_glow_mode",
    0x2A3896: "interaction_class", 0x2A38FA: "use_limit", 0x2A395E: "use_count_resets_on_entry",
    0x2A39C2: "required_story_flag", 0x2A3A26: "player_only", 0x2A3BB6: "required_skill_id",
    0x2A412E: "action_command_id",
}
OBJECT_TABLES = {0x2A3A8A: (3, "allowed_unit_ids"), 0x2A3C1A: (3, "immunity_skill_ids"),
                 0x2A3D46: (7, "battle_stat_deltas"), 0x2A4002: (3, "turn_resource_deltas")}


def extract_object_types(scr):
    recs: dict[int, dict] = {}
    unmapped = []

    def rec(i):
        return recs.setdefault(i, {"id": i, "attributes": {}, "tables": {}, "desc": {}})

    for x in scr.instructions:
        if x.opcode == SET_STRING and x.args and x.args[0][0] == T_GLOBAL_STRING:
            d = x.args[0][1]
            text = literal(scr, x.args[1])
            if OBJECT_NAME_BASE < d < OBJECT_NAME_BASE + OBJECT_SPAN:
                rec(d - OBJECT_NAME_BASE)["name"] = text
            elif OBJECT_DESC_BASE <= d < OBJECT_DESC_BASE + 2 * OBJECT_SPAN:
                i, line = divmod(d - OBJECT_DESC_BASE, 2)
                rec(i)["desc"][str(line)] = text
            else:
                unmapped.append((hex(x.offset), hex(d), text))
            continue
        write = static_global_write(x)
        if write is None:
            continue
        d, v = write
        for base, name in OBJECT_PARALLEL.items():
            if base <= d < base + OBJECT_SPAN:
                rec(d - base)["attributes"][name] = v
                break
        else:
            for base, (stride, name) in OBJECT_TABLES.items():
                if base <= d < base + OBJECT_SPAN * stride:
                    i, c = divmod(d - base, stride)
                    rec(i)["tables"].setdefault(name, {})[str(c)] = v
                    break
            else:
                unmapped.append((hex(x.offset), hex(d), v))
    return sorted(recs.values(), key=lambda r: r["id"]), {"unmapped": unmapped}


# ---------------------------------------------------------------- COINIT ---
COSTUME_ITEM = 0x2D2846    # current costume item id (COINIT selector)
BOARD_ASSET = 0x3A0929
BOARD_GRID = 0x3A092A      # int[33x33] doubled-coordinate node/link grid
BOARD_GRID_STRIDE = 33
BOARD_UNLOCK = 0x3A0D6B    # int[17x17] node unlock story-flag grid
BOARD_UNLOCK_STRIDE = 17


def extract_costume_boards(scr):
    out = []
    for item, start, end in selector_guards(scr, COSTUME_ITEM):
        r = {"id": item, "item_id": item, "board_asset_id": None, "grid": {}, "unlock_flags": {}}
        for x in scr.instructions[start:end]:
            if x.opcode == COPY_LOCAL_ARRAY and x.args[0][0] == T_GLOBAL_INT:
                y, c = divmod(x.args[0][1] - BOARD_GRID, BOARD_GRID_STRIDE)
                r["grid"][str(y)] = {"x0": c, "cells": read_footer_array(scr, x.args[1][1])}
                continue
            write = static_global_write(x)
            if write is None:
                continue
            d, v = write
            if d == BOARD_ASSET:
                r["board_asset_id"] = v
            elif BOARD_UNLOCK <= d < BOARD_UNLOCK + BOARD_UNLOCK_STRIDE ** 2:
                y, c = divmod(d - BOARD_UNLOCK, BOARD_UNLOCK_STRIDE)
                r["unlock_flags"][f"{c},{y}"] = v
        out.append(r)
    return out, {"selector": f"0x{COSTUME_ITEM:x}"}


# ---------------------------------------------------------------- AMINIT2 / AMINIT ---
AREA_SPAN = 100
AREA_NAME = 0x241D              # string[100]  area id -> name (AMINIT2)
AREA_KIND = 0x13E93C            # 1 = labyrinth area (stage list), 2 = town/facility area
AREA_TRAVEL_DAYS = 0x13EA68     # days added to elapsed-day counter on entering the area
AREA_ICON = 0x13E874            # MIxxx.AGF resource id
AREA_MAP_X = 0x13E9A0           # world-map pixel X (0 = not drawn on world map)
AREA_MAP_Y = 0x13EA04           # world-map pixel Y
AREA_ORDER = 0x13E8D8           # AMINIT: world-map list order key
AREA_STATE = 0x3613             # runtime: bit0 discovered, bit1 currently enterable
AREA_ARRAYS = ((AREA_KIND, "kind"), (AREA_TRAVEL_DAYS, "travel_days"),
               (AREA_ICON, "icon_resource_id"), (AREA_MAP_X, "map_x"), (AREA_MAP_Y, "map_y"))


def _int_static_writes(scr):
    for ins in scr.instructions:
        write = static_global_write(ins)
        if write and isinstance(write[1], int):
            yield ins, write


def extract_world_areas(scr):
    """AMINIT2 (and $n$AMINIT2): area records keyed by name slot 0x241d+id."""
    recs = {}
    for dest, text in string_writes(scr):
        aid = dest - AREA_NAME
        if 0 < aid < AREA_SPAN:
            recs.setdefault(aid, {"id": aid})["name"] = text
    for _ins, (dest, val) in _int_static_writes(scr):
        for base, key in AREA_ARRAYS:
            if 0 < dest - base < AREA_SPAN:
                recs.setdefault(dest - base, {"id": dest - base})[key] = val
                break
    return [recs[k] for k in sorted(recs)], {"id_space": "1..99 (area id == labyrinth no 0x428e)"}


def extract_area_unlock_rules(scr):
    """AMINIT (runtime chain 0x3d41c0): per-area order key + story-flag condition setting state 3."""
    recs = {}
    cond = []
    for ins in scr.instructions:
        op, a = label(ins), ins.args
        if op in ("eq", "ne") and a[1][0] == T_GLOBAL_INT and a[2][0] == T_IMM:
            cond.append((f"0x{a[1][1]:x}", "==" if op == "eq" else "!=", a[2][1]))
            continue
        write = static_global_write(ins)
        if not write or not isinstance(write[1], int):
            continue
        dest, val = write
        if 0 < dest - AREA_ORDER < AREA_SPAN:
            aid = dest - AREA_ORDER
            recs.setdefault(aid, {"id": aid})["order_key"] = val
            cond = []
        elif 0 < dest - AREA_STATE < AREA_SPAN and val == 3:
            aid = dest - AREA_STATE
            recs.setdefault(aid, {"id": aid})["enable_when_all"] = [
                {"flag": f, "op": o, "value": v} for f, o, v in cond]
            cond = []
    return [recs[k] for k in sorted(recs)], {"note": "state bit0 kept, bit1 recomputed each run"}


# ---------------------------------------------------------------- DIINIT ---
DIG_THRESHOLD = 0x2A7B66        # int[200x10] cumulative % roll threshold
DIG_ITEM = 0x2A8336             # int[200x10] ITINIT item id
DIG_REQ_FLAG = 0x2A8B06         # int[200x10] 1-based story flag that must be set
DIG_FORBID_FLAG = 0x2A92D6      # int[200x10] 1-based story flag that must NOT be set
DIG_MES = 0x2A9AA6              # int[200]   DIMES message script id (no reader)
DIG_STRIDE = 10
DIG_CAP = 200
DIG_ARRAYS = {DIG_THRESHOLD: "threshold_pct", DIG_ITEM: "item_id",
              DIG_REQ_FLAG: "required_story_flag", DIG_FORBID_FLAG: "forbidden_story_flag"}


def extract_dig_tables(scr):
    tables = {}
    for ins, (dest, val) in _int_static_writes(scr):
        if 0 < dest - DIG_MES <= DIG_CAP:
            tid = dest - DIG_MES
            tables.setdefault(tid, {"id": tid, "slots": {}})["message_script_id"] = val
            continue
        for base, key in DIG_ARRAYS.items():
            off = dest - base
            if 0 <= off < DIG_CAP * DIG_STRIDE:
                tid, col = divmod(off, DIG_STRIDE)
                t = tables.setdefault(tid, {"id": tid, "slots": {}})
                t["slots"].setdefault(col, {"slot": col})[key] = val
                break
        else:
            raise ValueError(f"unclassified DIINIT write 0x{dest:x} @0x{ins.offset:x}")
    recs = []
    for tid in sorted(tables):
        t = tables[tid]
        t["slots"] = [t["slots"][c] for c in sorted(t["slots"])]
        t["object_type"] = 0x20 + tid // 100
        t["object_payload"] = tid % 100
        recs.append(t)
    return recs, {"key": "dig id = (object_type-0x20)*100 + stage_object_payload(0x14050e)",
                  "stride": DIG_STRIDE}


# ---------------------------------------------------------------- ARINIT ---
AR_NAME = 0xC01F                # string[100]
AR_TEXT = 0xC083                # string[100x2] col0 description, col1 reward text
AR_PARALLEL = {
    0x3C5122: "category",
    0x3C5186: "display_number",
    0x3C51EA: "party_size",
    0x3C524E: "level",
    0x3C52B2: "rank_index",
    0x3C5316: "prerequisite_battle_id",
    0x3C537A: "required_total_wins",
    0x3C53DE: "required_studio_rank",
    0x3C57C6: "entry_fee",
    0x3C5A82: "lab_no",
    0x3C5AE6: "floor_no",
}
AR_ROWS = {0x3C5442: ("required_unit_ids", 9), 0x3C582A: ("required_story_flags", 3),
           0x3C5956: ("forbidden_story_flags", 3)}
AR_CAP = 100


def extract_arena_battles(scr):
    recs = {}
    for dest, text in string_writes(scr):
        if 0 < dest - AR_NAME < AR_CAP:
            recs.setdefault(dest - AR_NAME, {"id": dest - AR_NAME})["name"] = text
        elif 0 <= dest - AR_TEXT < AR_CAP * 2:
            bid, col = divmod(dest - AR_TEXT, 2)
            recs.setdefault(bid, {"id": bid})["description" if col == 0 else "reward_text"] = text
    for ins, (dest, val) in _int_static_writes(scr):
        for base, key in AR_PARALLEL.items():
            if 0 < dest - base < AR_CAP:
                recs.setdefault(dest - base, {"id": dest - base})[key] = val
                break
        else:
            for base, (key, stride) in AR_ROWS.items():
                off = dest - base
                if 0 <= off < AR_CAP * stride:
                    bid, col = divmod(off, stride)
                    recs.setdefault(bid, {"id": bid}).setdefault(key, [0] * stride)[col] = val
                    break
            else:
                raise ValueError(f"unclassified ARINIT write 0x{dest:x} @0x{ins.offset:x}")
    return [recs[k] for k in sorted(recs)], {"id_space": "1..99",
                                              "lab_floor": "selects STINIT2 stage"}
