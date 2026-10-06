#!/usr/bin/env python3
"""Kamidori item / skill / combat-rule tables.

  ITINIT     items: fixed parallel arrays over item id (span 2000); multi-column arrays key
             by divmod(dst - base, stride). ITMES joins as `message`.
  SKINIT     skills: the same fixed layout over skill id (span 1000). SKMES joins as `message`.
  ILINIT     30 conditions x 5 levels: level names, scalar arrays, per-level duration/deltas.
  AFINIT     record-less tuning bank: species names, affinity scale, susceptibility,
             equipment enhancements, progression thresholds (one record per sub-table).
  RTINIT     enemy routine banks: [1000 routines][50 steps], banks 50000 cells apart.
  BTANINIT2  battle animation rows: six effect ids/delays plus a duration.
  BTANINIT   battle effect dispatch: `eq (local) (local) EFFECT` blocks filling work arrays.

Decoded attributes live under `attributes` (semantic names, not raw global keys). Layout
evidence: docs/kamidori-data-tables.md.
"""
from __future__ import annotations

import extract_init as E
from init_kamidori_common import (
    T_GLOBAL_INT, T_IMM, T_LOCAL_INT, T_LOCAL_PTR,
    inline_arrays, int_writes, label, string_writes,
)

# --------------------------------------------------------------------------------------
# ITINIT / SKINIT: fixed-layout name tables
# --------------------------------------------------------------------------------------
ITEM_SPAN = 2000
ITEM_STRINGS = {0x4228: "name", 0x49F8: "short_description"}
# base -> (semantic name, stride, column names or None)
ITEM_LAYOUT = {
    0x2D2848: ("handler_script_id", 1, None),
    0x2D3018: ("costume_graphic_asset_ids", 17, None),
    0x2DB4E8: ("icon_asset_id", 1, None),
    0x2DBCB8: ("furniture_sprite_asset_id", 1, None),
    0x2DC488: ("sort_key", 1, None),
    0x2DCC58: ("rank", 1, None),
    0x2DD428: ("category", 1, None),
    0x2DDBF8: ("min_range", 1, None),
    0x2DE3C8: ("max_range", 1, None),
    0x2DEB98: ("attack_element", 1, None),
    0x2DF368: ("costume_battle_variant", 1, None),
    0x2DFB38: ("affinity_level_deltas", 8, ["none", "physical", "earth", "ice", "fire",
                                            "lightning", "holy", "dark"]),
    0x2E39B8: ("character_whitelist", 5, None),
    0x2E60C8: ("species_restriction_mask", 1, None),
    0x2E6898: ("sex_restriction_mask", 1, None),
    0x2E7068: ("cursed_flag", 1, None),
    0x2E7838: ("condition_level_deltas", 30, None),
    0x2F6298: ("equipped_condition_levels", 30, None),
    0x304CF8: ("granted_skill_id", 1, None),
    0x3054C8: ("stat_modifiers", 14, ["accuracy", "evasion", "physical_attack",
                                      "physical_defense", "magic_attack", "magic_defense",
                                      "agility", "luck", "movement", "customer_service",
                                      "max_hp", "max_sp", "max_fs", "skill_capacity"]),
    0x30C228: ("critical_rate_bonus", 1, None),
    0x30C9F8: ("resource_recovery_amounts", 3, ["hp", "sp", "fs"]),
    0x30E168: ("recovery_variance", 1, None),
    0x30E938: ("battle_animation_id", 1, None),
    0x30F108: ("prices", 3, ["buy", "sell", "retail_base"]),
    0x310878: ("furniture_area", 1, None),
    0x311048: ("furniture_exclusive_group", 1, None),
    0x311818: ("furniture_width", 1, None),
    0x311FE8: ("furniture_height", 1, None),
    0x3127B8: ("garden_production_period", 1, None),
    0x312F88: ("garden_product_item_id", 1, None),
    0x313758: ("furniture_sales_count_bonus", 1, None),
    0x313F28: ("furniture_purchase_desire_bonus", 1, None),
    0x3146F8: ("furniture_visit_rate_bonus", 1, None),
    0x314EC8: ("furniture_high_price_bonus", 1, None),
    0x315698: ("furniture_equipment_bonus", 1, None),
    0x315E68: ("furniture_information_bonus", 1, None),
    0x316638: ("furniture_mutation_bonus", 1, None),
    0x316E08: ("furniture_knowledge_bonus", 1, None),
    0x3175D8: ("furniture_staff_count_bonus", 1, None),
    0x317DA8: ("furniture_garden_bonus", 1, None),
    0x318578: ("field_use_target_kind", 1, None),
}

SKILL_SPAN = 1000
SKILL_STRINGS = {0x5998: "name", 0x5D80: "short_description"}
SKILL_LAYOUT = {
    0x3A10C8: ("icon_asset_id", 1, None),
    0x3A14B0: ("sort_key", 1, None),
    0x3A1898: ("upgrade_family_id", 1, None),
    0x3A1C80: ("category", 1, None),
    0x3A2068: ("catalog_visible", 1, None),
    0x3A2450: ("equip_cost", 1, None),
    0x3A2838: ("bundled_skill_ids", 5, None),
    0x3A3BC0: ("min_range_encoded", 1, None),
    0x3A3FA8: ("max_range_encoded", 1, None),
    0x3A4390: ("required_weapon_category_mask", 1, None),
    0x3A4778: ("area_target_mode", 1, None),
    0x3A4B60: ("area_shape_id", 1, None),
    0x3A4F48: ("area_cooldown_turns", 1, None),
    0x3A5330: ("attack_element_encoded", 1, None),
    0x3A5718: ("condition_levels", 30, None),
    0x3ACC48: ("combat_stat_deltas", 10, ["accuracy", "evasion", "physical_attack",
                                          "physical_defense", "magic_attack", "magic_defense",
                                          "agility", "luck", "movement", "customer_service"]),
    0x3AF358: ("critical_rate_bonus", 1, None),
    0x3AF740: ("resource_deltas", 3, ["hp", "sp", "fs"]),
    0x3B02F8: ("proc_chance_percent", 1, None),
    0x3B06E0: ("battle_animation_id", 1, None),
    0x3B0AC8: ("combo_partner_character_ids", 2, None),
    0x3B1298: ("handler_script_id", 1, None),
    0x3B1680: ("area_effect_texture_asset_id", 1, None),
    0x3B1A68: ("area_effect_sound_asset_id", 1, None),
    0x3B1E50: ("area_effect_flash_flag", 1, None),
}


def extract_fixed_layout(scr, span, strings_layout, layout, table):
    """Key every write by divmod(dst - base, stride) against absolute array bases."""
    records: dict[int, dict] = {}
    unclassified = []

    def rec(rid):
        return records.setdefault(rid, {"id": rid, "attributes": {}})

    for dst, text in string_writes(scr):
        for base, field in strings_layout.items():
            if base <= dst < base + span:
                rec(dst - base)[field] = text
                break
        else:
            unclassified.append(("string", hex(dst), text))
    for dst, value in int_writes(scr):
        for base, (name, stride, cols) in layout.items():
            if base <= dst < base + span * stride:
                rid, col = divmod(dst - base, stride)
                attributes = rec(rid)["attributes"]
                if stride == 1:
                    attributes[name] = value
                else:
                    key = cols[col] if cols and col < len(cols) else str(col)
                    attributes.setdefault(name, {})[key] = value
                break
        else:
            unclassified.append(("int", hex(dst), value))
    if unclassified:
        raise ValueError(
            f"{table}: {len(unclassified)} unclassified writes, e.g. {unclassified[:5]}"
        )
    out = [records[k] for k in sorted(records)]
    return out, {
        "schema": f"kamidori-{table.lower()}-fixed-layout",
        "record_span": span,
        "string_arrays": {hex(b): n for b, n in strings_layout.items()},
        "arrays": {hex(b): {"name": n, "stride": s, "columns": c}
                   for b, (n, s, c) in layout.items()},
    }


def extract_items(scr):
    return extract_fixed_layout(scr, ITEM_SPAN, ITEM_STRINGS, ITEM_LAYOUT, "ITINIT")


def extract_skills(scr):
    return extract_fixed_layout(scr, SKILL_SPAN, SKILL_STRINGS, SKILL_LAYOUT, "SKINIT")


# --------------------------------------------------------------------------------------
# ILINIT: 30-condition x 5-level definitions
# --------------------------------------------------------------------------------------
COND_SPAN, COND_LEVELS = 30, 5
COND_NAME_BASE = 0x6168            # GStr[base + id*5 + level-1]
COND_SCALARS = {
    0x3B223A: "susceptibility_group",      # index into AFINIT species susceptibility row
    0x3B2258: "immunity_skill_id",         # unit holding this SKINIT skill is immune
    0x3B2276: "can_affect_bosses",
    0x3B2294: "cleared_by_recover",
    0x3B22B2: "icon_id",
    0x3B28AC: "critical_rate_delta_at_level5",  # scaled level*v/5 into unit crit
}
COND_DURATION_BASE = 0x3B22D0      # [30][5]   -1 = until cleared
COND_STAT_DELTA_BASE = 0x3B2366    # [30][5*9]
COND_STAT_COLUMNS = ("accuracy", "evasion", "physical_attack", "physical_defense",
                     "magic_attack", "magic_defense", "agility", "luck", "movement")
COND_RESOURCE_DELTA_BASE = 0x3B28CA  # [30][5*3]
COND_RESOURCE_COLUMNS = ("hp", "sp", "fs")


def extract_condition_definitions(scr):
    recs: dict[int, dict] = {}

    def rec(cid):
        return recs.setdefault(cid, {"id": cid, "level_names": [None] * COND_LEVELS,
                                     "attributes": {}, "levels": {}})

    for dst, text in string_writes(scr):
        cid, lvl = divmod(dst - COND_NAME_BASE, COND_LEVELS)
        if not 1 <= cid < COND_SPAN:
            raise ValueError(f"ILINIT: unexpected string destination {dst:#x}")
        rec(cid)["level_names"][lvl] = text
    for dst, value in int_writes(scr):
        for base, name in COND_SCALARS.items():
            if 0 <= dst - base < COND_SPAN:
                rec(dst - base)["attributes"][name] = value
                break
        else:
            for base, per_level, cols in (
                (COND_DURATION_BASE, 1, ("duration_turns",)),
                (COND_STAT_DELTA_BASE, len(COND_STAT_COLUMNS), COND_STAT_COLUMNS),
                (COND_RESOURCE_DELTA_BASE, len(COND_RESOURCE_COLUMNS), COND_RESOURCE_COLUMNS),
            ):
                cid, col = divmod(dst - base, per_level * COND_LEVELS)
                if 0 <= dst - base and 1 <= cid < COND_SPAN:
                    lvl, k = divmod(col, per_level)
                    rec(cid)["levels"].setdefault(lvl + 1, {})[cols[k]] = value
                    break
            else:
                raise ValueError(f"ILINIT: unclassified write {dst:#x}={value}")
    out = []
    for cid in sorted(recs):
        r = recs[cid]
        named = {i + 1: n for i, n in enumerate(r["level_names"]) if n}
        r["name"] = E._condition_family_name(named) if named else None
        r["levels"] = [dict(level=k, **v) for k, v in sorted(r["levels"].items())]
        out.append(r)
    return out, {"schema": "kamidori-condition-definitions", "record_span": COND_SPAN,
                 "level_count": COND_LEVELS, "level_name_base": hex(COND_NAME_BASE),
                 "scalar_arrays": {hex(b): n for b, n in COND_SCALARS.items()},
                 "duration_base": hex(COND_DURATION_BASE),
                 "stat_delta_base": hex(COND_STAT_DELTA_BASE), "stat_columns": COND_STAT_COLUMNS,
                 "resource_delta_base": hex(COND_RESOURCE_DELTA_BASE)}


# --------------------------------------------------------------------------------------
# AFINIT: record-less tuning bank; one record per sub-table
# --------------------------------------------------------------------------------------
AF = {
    "species_name_base": 0x61FF,          # GStr[0x61ff + species_index] (16)
    "affinity_level_name_base": 0x621A,   # GStr[0x621a + level] level 0..11
    "affinity_damage_percent_base": 0x3B2A8C,  # int[12]
    "susceptibility_base": 0x3B2A98,      # int[species][10]; inline rows at +10*s+1
    "enh_type_base": 0x3A0E8D,            # int[30][3]  1 affinity, 2 stat, 3 gem socket
    "enh_target_base": 0x3A0EE7,          # int[30][3]  column index into affinity/stat
    "enh_amount_base": 0x3A0F41,          # int[30][3]
    "enh_tier_base": 0x3A0F9B,            # int[30]
    "enh_cost_base": 0x3A0FB9,            # int[30]
    "enh_material_item_base": 0x3A0FD7,   # int[30][4]
    "enh_material_qty_base": 0x3A104F,    # int[30][4]
}
AF_PROGRESSION = {
    0x3B2B60: "weapon_rank_exp_thresholds",
    0x3B2B67: "studio_level_exp_thresholds",
    0x3B2B6E: "studio_level_up_point_rewards",
    0x3B2B75: "alchemy_exp_by_item_rank",
    0x3B2B90: "difficulty_percent_table",
    0x3B2B96: "shop_level_table",
}
AF_EVENT_SCALARS = (0x3B2B7D, 0x3B2B89)
AF_REGIONS = [(0x3A0E8D, 0x3A10C7), (0x3B2A8C, 0x3B2A98), (0x3B2A98, 0x3B2B42),
              (0x3B2B60, 0x3B2B7C), AF_EVENT_SCALARS, (0x3B2B90, 0x3B2BAA)]
AF_EFFECT_KINDS = {1: "affinity", 2: "stat", 3: "gem_socket"}


def extract_affinity_tuning(scr):
    strings = dict(string_writes(scr))
    ints = dict(int_writes(scr))
    for dst, values in inline_arrays(scr):
        if values is None:
            raise ValueError(f"AFINIT: unreadable inline array for {dst:#x}")
        for i, v in enumerate(values):
            ints[dst + i] = v
    stray = [hex(a) for a in ints if not any(lo <= a < hi for lo, hi in AF_REGIONS)]
    if stray:
        raise ValueError(f"AFINIT: unclassified writes {stray[:8]}")
    species = {i: strings[AF["species_name_base"] + i] for i in range(16)
               if AF["species_name_base"] + i in strings}
    scale = [{"level": lv, "name": strings.get(AF["affinity_level_name_base"] + lv),
              "damage_percent": ints.get(AF["affinity_damage_percent_base"] + lv),
              "display_delta": lv - 5} for lv in range(12)]
    suscept = {}
    for s in range(1, 17):
        row = {g: ints[AF["susceptibility_base"] + 10 * s + g] for g in range(1, 10)
               if AF["susceptibility_base"] + 10 * s + g in ints}
        if row:
            suscept[s] = row
    enhancements = []
    for e in range(1, 30):
        r = {"id": e, "effects": []}
        for c in range(3):
            kind = ints.get(AF["enh_type_base"] + 3 * e + c)
            if kind:
                r["effects"].append({
                    "type": AF_EFFECT_KINDS.get(kind, kind),
                    "target_column": ints.get(AF["enh_target_base"] + 3 * e + c),
                    "amount": ints.get(AF["enh_amount_base"] + 3 * e + c),
                })
        r["tier"] = ints.get(AF["enh_tier_base"] + e)
        r["cost"] = ints.get(AF["enh_cost_base"] + e)
        materials = [(ints.get(AF["enh_material_item_base"] + 4 * e + c),
                      ints.get(AF["enh_material_qty_base"] + 4 * e + c)) for c in range(4)]
        r["materials"] = [{"item_id": i, "quantity": q} for i, q in materials if i]
        if r["effects"] or r["cost"]:
            enhancements.append(r)
    progression = {}
    for base, name in AF_PROGRESSION.items():
        values, i = [], 0
        while base + i in ints and (i == 0 or base + i not in AF_PROGRESSION):
            values.append(ints[base + i])
            i += 1
        progression[name] = {"base": hex(base), "values": values}
    scalars = {hex(a): ints[a] for a in range(*AF_EVENT_SCALARS) if a in ints}
    records = [{"id": "species_names", "values": species},
               {"id": "affinity_scale", "values": scale},
               {"id": "species_condition_susceptibility_percent", "values": suscept},
               {"id": "equipment_enhancements", "values": enhancements},
               {"id": "progression_tables", "values": progression},
               {"id": "event_delta_scalars", "values": scalars}]
    return records, {"schema": "kamidori-affinity-tuning",
                     "layout": {k: hex(v) for k, v in AF.items()}}


# --------------------------------------------------------------------------------------
# RTINIT: enemy routine banks [1000 routines][50 steps], bank spacing 50000 cells
# --------------------------------------------------------------------------------------
RT_ROOT, RT_BANK_SPAN, RT_STEPS, RT_ROUTINES = 0x14D09E, 50000, 50, 1000
RT_BANKS = {   # bank index -> (name, columns-per-step)
    0: ("map_provider_selector", 1),          # -> RTN_M{n:03}.BIN
    1: ("map_activation_percent", 1),
    2: ("map_parameter_1", 1),
    3: ("map_parameter_2", 1),
    4: ("map_parameter_3", 1),
    5: ("map_parameter_4", 1),
    7: ("map_use_limit", 1),
    8: ("map_required_story_flag_ids", 3),    # occupies banks 8..10
    11: ("map_forbidden_story_flag_ids", 3),  # occupies banks 11..13
    14: ("battle_provider_selector", 1),      # -> RTN_B{n:03}.BIN
    15: ("battle_activation_percent", 1),
    16: ("battle_parameter_1", 1),
    22: ("battle_use_limit", 1),
    23: ("battle_required_story_flag_ids", 3),
    26: ("battle_forbidden_story_flag_ids", 3),
}


def extract_routine_banks(scr):
    routines: dict[int, dict] = {}
    for dst, value in int_writes(scr):
        off = dst - RT_ROOT
        if off < 0:
            raise ValueError(f"RTINIT: write below root {dst:#x}")
        bank = off // RT_BANK_SPAN
        owner = max((b for b in RT_BANKS if b <= bank < b + RT_BANKS[b][1]), default=None)
        if owner is None:
            raise ValueError(f"RTINIT: write {dst:#x} in unmapped bank {bank}")
        name, width = RT_BANKS[owner]
        rid, cell = divmod(off - owner * RT_BANK_SPAN, RT_STEPS * width)
        step, k = divmod(cell, width)
        r = routines.setdefault(rid, {"id": rid, "steps": {}})
        s = r["steps"].setdefault(step, {"step": step})
        if width == 1:
            s[name] = value
        else:
            s.setdefault(name, [0] * width)[k] = value
    out = []
    for rid in sorted(routines):
        r = routines[rid]
        r["steps"] = [r["steps"][k] for k in sorted(r["steps"])]
        out.append(r)
    return out, {"schema": "kamidori-routine-banks", "root": hex(RT_ROOT),
                 "bank_span": RT_BANK_SPAN, "steps": RT_STEPS, "routine_capacity": RT_ROUTINES,
                 "banks": {b: {"base": hex(RT_ROOT + b * RT_BANK_SPAN), "name": n,
                               "columns_per_step": w}
                           for b, (n, w) in RT_BANKS.items()}}


# --------------------------------------------------------------------------------------
# BTANINIT2 animation rows / BTANINIT effect dispatch
# --------------------------------------------------------------------------------------
BTAN_EFFECT_BASE, BTAN_SLOTS, BTAN_CAP = 0x3B3B63, 6, 1000
BTAN_DELAY_BASE = BTAN_EFFECT_BASE + BTAN_SLOTS * BTAN_CAP      # 0x3b52d3
BTAN_DURATION_BASE = BTAN_DELAY_BASE + BTAN_SLOTS * BTAN_CAP    # 0x3b6a43
BTAN_WORK = {  # Himegari BATTLE_EFFECT_WORK_ARRAYS shifted by 0x3b6e2b - 0x155b56
    0x3B6E2B: "visual_asset_id", 0x3B6E31: "visual_mode_id", 0x3B6E37: "additive_blend_flag",
    0x3B6E3D: "width", 0x3B6E43: "height", 0x3B6E49: "atlas_column_count",
    0x3B6E4F: "atlas_row_count", 0x3B6E55: "atlas_frame_count", 0x3B6E5B: "duration_ms",
    0x3B6E61: "combatant_anchor_flag", 0x3B6E67: "offset_x", 0x3B6E6D: "offset_y",
    0x3B6E73: "sound_asset_id", 0x3B6E79: "sound_delay_ms",
}
BTAN_HIT_PULSES = 0x3B6E7F  # [6][3]


def extract_battle_animations(scr):
    rows: dict[int, dict] = {}
    for dst, value in int_writes(scr):
        for base, name, stride in ((BTAN_EFFECT_BASE, "effect_ids", BTAN_SLOTS),
                                   (BTAN_DELAY_BASE, "effect_start_delays_ms", BTAN_SLOTS),
                                   (BTAN_DURATION_BASE, "duration_ms", 1)):
            if base <= dst < base + stride * BTAN_CAP:
                aid, col = divmod(dst - base, stride)
                r = rows.setdefault(aid, {"id": aid})
                if stride == 1:
                    r[name] = value
                else:
                    r.setdefault(name, [0] * BTAN_SLOTS)[col] = value
                break
        else:
            raise ValueError(f"BTANINIT2: unclassified write {dst:#x}")
    return [rows[k] for k in sorted(rows)], {"schema": "kamidori-battle-animations",
                                              "effect_base": hex(BTAN_EFFECT_BASE),
                                              "delay_base": hex(BTAN_DELAY_BASE),
                                              "duration_base": hex(BTAN_DURATION_BASE)}


def extract_battle_effect_definitions(scr):
    """Dispatch body: `eq (local 2) (local 0) EFFECT_ID; jcc` then per-field
    `lookup-array (local-ptr 0) (global WORK) (local 1)` + `mov (local-ptr 0) V`."""
    effects: dict[int, dict] = {}
    cur = None
    pending = None
    for ins in scr.instructions:
        op, a = label(ins), ins.args
        if op == "eq" and len(a) >= 3 and a[1][0] == T_LOCAL_INT and a[2][0] == T_IMM:
            cur = effects.setdefault(a[2][1], {"id": a[2][1]}) if a[2][1] else None
            pending = None
        elif (op == "lookup-array" and cur is not None and a[1][0] == T_GLOBAL_INT
              and a[1][1] in BTAN_WORK):
            pending = (BTAN_WORK[a[1][1]], None)
        elif (op == "lookup-array-2d" and cur is not None
              and a[1] == (T_GLOBAL_INT, BTAN_HIT_PULSES)):
            pending = ("hit_pulse_offsets_ms", a[4][1])
        elif op == "mov" and pending and a[0][0] == T_LOCAL_PTR and a[1][0] == T_IMM:
            name, col = pending
            if col is None:
                cur[name] = a[1][1]
            else:
                cur.setdefault(name, [0, 0, 0])[col] = a[1][1]
            pending = None
    return [effects[k] for k in sorted(effects)], {
        "schema": "kamidori-battle-effect-definitions",
        "work_arrays": {hex(k): v for k, v in BTAN_WORK.items()},
        "hit_pulse_base": hex(BTAN_HIT_PULSES),
    }
