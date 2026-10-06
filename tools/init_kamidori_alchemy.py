#!/usr/bin/env python3
"""Kamidori alchemy, studio-economy and town tables.

  ALINIT  alchemy recipes: parallel arrays over recipe id (span 2000), 4 ingredient slots.
  BUINIT  studio extensions: per room (4) x level (20) cost/material/grid rows plus
          per-room level parameter arrays.
  IMINIT  equipment growth: per item id, growth flag, level thresholds, stat bonus rows.
  SHINIT  shop catalogue hooks: guarded writes into the 100-slot shop list.
  HAINIT  dungeon hall placements over hall id (span 1000).
  CSINIT  costume sprite substitutions: (shown sprite, costume selection) -> replacement.
  SIINIT  town site maps: `eq (global 0x3612) TOWN` blocks writing background/preview/BGM
          and per-site icon, position, cursor links and name.

All bases are absolute, so `$n$` layers decode with the same constants. Layout evidence:
docs/kamidori-data-tables.md.
"""
from __future__ import annotations

from init_kamidori_common import (
    T_GLOBAL_INT, T_GLOBAL_STRING, evaluated_writes, label, literal, selector_guards,
    static_global_write,
)


def _layout_cell(address, layout):
    """Map an address to (field, record_id, column, stride) via {field: (base, stride, span)}."""
    for name, (base, stride, span) in layout.items():
        rel = address - base
        if 0 <= rel < span * stride:
            rid, col = divmod(rel, stride)
            return name, rid, col, stride
    return None


def _tabulate(scr, layout, string_layout=None):
    records: dict[int, dict] = {}
    unclassified = []
    for kind, addr, val, _guard, off in evaluated_writes(scr):
        hit = _layout_cell(addr, (string_layout if kind == "string" else layout) or {})
        if hit is None:
            unclassified.append({"offset": off, "kind": kind, "address": hex(addr), "value": val})
            continue
        name, rid, col, stride = hit
        rec = records.setdefault(rid, {"id": rid})
        if stride == 1:
            rec[name] = val
        else:
            rec.setdefault(name, [0] * stride)[col] = val
    return records, unclassified


def _require_classified(scr, unclassified):
    if unclassified:
        raise ValueError(
            f"{scr.path.name}: {len(unclassified)} unclassified writes: {unclassified[:3]}"
        )


# --------------------------------------------------------------------------- ALINIT
ALCHEMY_RECIPE_SPAN = 2000
ALCHEMY_LAYOUT = {
    # field: (base, stride, record span) -- every base is a parallel array over recipe id
    "output_item_id":             (0x38C8D9, 1, 2000),
    "quest_recipe_marker":        (0x38D0A9, 1, 2000),
    "required_craft_rank":        (0x38D879, 1, 2000),   # read by ALCHEMY, never authored
    "required_story_flags":       (0x38E049, 5, 2000),   # read by ALCHEMY, never authored
    "forbidden_story_flags":      (0x390759, 5, 2000),
    "ingredient_item_ids":        (0x393639, 4, 2000),
    "ingredient_quantities":      (0x395579, 4, 2000),
    "mutation_chance_percent":    (0x3974B9, 1, 2000),
    "mutation_required_flags":    (0x397C89, 2, 2000),
    "mutation_forbidden_flag":    (0x398C29, 1, 2000),
    "mutation_item_id":           (0x3993F9, 1, 2000),
    "unlock_min_craftsman_rank":  (0x399BC9, 1, 2000),
    "unlock_min_synthesis_rank":  (0x39A399, 1, 2000),
    "unlock_grown_item_id":       (0x39AB69, 1, 2000),
    "unlock_prerequisite_recipe": (0x39B339, 1, 2000),
    "unlock_min_shop_level":      (0x39BB09, 1, 2000),
    "unlock_min_workshop_level":  (0x39C2D9, 1, 2000),
    "unlock_min_quarters_level":  (0x39CAA9, 1, 2000),
    "unlock_min_room3_level":     (0x39D279, 1, 2000),
    "unlock_min_knowledge":       (0x39DA49, 1, 2000),
    "unlock_min_sense":           (0x39E219, 1, 2000),
    "unlock_story_flags":         (0x39E9E9, 3, 2000),
    "first_craft_evaluation":     (0x3A0159, 1, 2000),
}
ALCHEMY_STRINGS = {"unlock_condition_text": (0x51C8, 1, 2000)}


def extract_alchemy_recipes(scr):
    records, unclassified = _tabulate(scr, ALCHEMY_LAYOUT, ALCHEMY_STRINGS)
    _require_classified(scr, unclassified)
    out = []
    for rid in sorted(records):
        r = records[rid]
        r["ingredients"] = [
            {"item_id": i, "quantity": q}
            for i, q in zip(r.pop("ingredient_item_ids", [0] * 4),
                            r.pop("ingredient_quantities", [0] * 4))
            if i
        ]
        for k in ("forbidden_story_flags", "required_story_flags", "unlock_story_flags",
                  "mutation_required_flags"):
            if k in r:
                r[k] = [f for f in r[k] if f]
        out.append(r)
    meta = {"record_span": ALCHEMY_RECIPE_SPAN,
            "layout": {k: [hex(b), s] for k, (b, s, _) in ALCHEMY_LAYOUT.items()},
            "string_base": "0x51c8",
            "flag_encoding": "1-based story-flag id; flag n lives at global 0x50d + n - 1"}
    return out, meta


# --------------------------------------------------------------------------- BUINIT
STUDIO_ROOMS = ["shop", "workshop", "quarters", "room3"]
STUDIO_LEVELS = 20
STUDIO_ROOM_LAYOUT = {
    # per room r (0..3) and level L (0..19, 0..9 authored); index = r*20 + L
    "required_craftsman_rank": (0x13EC64, 1, 80),
    "build_cost_money":        (0x13ECB4, 1, 80),
    "material_item_ids":       (0x13ED04, 4, 80),
    "material_quantities":     (0x13EE44, 4, 80),
    "upkeep_cost":             (0x13F754, 1, 80),
}
STUDIO_GRID_LAYOUT = {"placement_grid_5x5": (0x13EF84, 25, 80)}  # (r*20 + L) * 25
STUDIO_PARAM_ARRAYS = {
    # base: (room, output global, name) ; each is [20] indexed by that room's level
    0x13F7A4: (0, 0x13F86C, "shop_shelf_capacity"),
    0x13F7B8: (0, 0x13F86D, "shop_param_86d_percent"),
    0x13F7CC: (0, 0x13F86E, "shop_customer_rate_percent"),
    0x13F7E0: (0, 0x13F86F, "shop_sale_price_percent"),
    0x13F7F4: (1, 0x13F870, "material_saving_percent"),
    0x13F808: (1, 0x13F871, "synthesis_exp_bonus_percent"),
    0x13F81C: (1, 0x13F872, "mutation_rate_percent"),
    0x13F830: (2, 0x13F873, "knowledge"),
    0x13F844: (2, 0x13F874, "resident_capacity"),
    0x13F858: (3, 0x13F876, "sense"),
}


def extract_studio_extensions(scr):
    rows: dict[tuple, dict] = {}
    unclassified = []

    def row(room, level):
        return rows.setdefault((room, level), {
            "id": room * STUDIO_LEVELS + level, "room": room,
            "room_name": STUDIO_ROOMS[room], "level": level,
        })

    for _kind, addr, val, _g, off in evaluated_writes(scr):
        hit = _layout_cell(addr, STUDIO_ROOM_LAYOUT)
        if hit:
            name, idx, col, stride = hit
            rec = row(*divmod(idx, STUDIO_LEVELS))
            if stride == 1:
                rec[name] = val
            else:
                rec.setdefault(name, [0] * stride)[col] = val
            continue
        hit = _layout_cell(addr, STUDIO_GRID_LAYOUT)
        if hit:
            _, idx, col, _ = hit
            row(*divmod(idx, STUDIO_LEVELS)).setdefault("placement_grid_5x5", [0] * 25)[col] = val
            continue
        for base, (room, _out, name) in STUDIO_PARAM_ARRAYS.items():
            if 0 <= addr - base < STUDIO_LEVELS:
                row(room, addr - base)[name] = val
                break
        else:
            unclassified.append({"offset": off, "address": hex(addr), "value": val})
    _require_classified(scr, unclassified)
    out = []
    for key in sorted(rows):
        r = rows[key]
        r["materials"] = [
            {"item_id": i, "quantity": q}
            for i, q in zip(r.pop("material_item_ids", [0] * 4),
                            r.pop("material_quantities", [0] * 4))
            if i
        ]
        if "placement_grid_5x5" in r:
            grid = r.pop("placement_grid_5x5")
            r["placement_grid_5x5"] = [grid[i:i + 5] for i in range(0, 25, 5)]
        out.append(r)
    return out, {"rooms": STUDIO_ROOMS,
                 "level_index": ("id = room*20 + level (levels 0..9 authored; "
                                 "displayed level = level+1)")}


# --------------------------------------------------------------------------- IMINIT
EQUIPMENT_GROWTH_LAYOUT = {
    "growth_enabled":       (0x31A4B9, 1, 2000),
    "level_exp_thresholds": (0x31AC89, 11, 2000),
    "level_stat_bonus8":    (0x320279, 80, 2000),    # [level 0..9][8]; never authored in shipped data
    "level_stat_bonus14":   (0x347379, 140, 2000),   # [level 0..9][14]
    "mastery_reward_item":  (0x38B939, 1, 2000),     # never authored
    "mastery_story_flag":   (0x38C109, 1, 2000),     # never authored
}


def extract_equipment_growth(scr):
    records, unclassified = _tabulate(scr, EQUIPMENT_GROWTH_LAYOUT)
    _require_classified(scr, unclassified)
    out = []
    for iid in sorted(records):
        r = records[iid]
        r["item_id"] = iid
        thresholds = r.pop("level_exp_thresholds", [0] * 11)
        r["max_level"] = next((i for i, v in enumerate(thresholds) if v == 0), 11)
        r["level_exp_thresholds"] = thresholds[:r["max_level"]]
        for key, per in (("level_stat_bonus8", 8), ("level_stat_bonus14", 14)):
            if key in r:
                flat = r.pop(key)
                r[key] = [flat[i:i + per] for i in range(0, len(flat), per)][:r["max_level"]]
        out.append(r)
    return out, {"keyed_by": "item id (ITINIT)"}


# --------------------------------------------------------------------------- SHINIT
SHOP_CATALOGUE_BASE = 0x3C5B4C  # [100] item id per shop slot; cleared by SHOP before running hooks
SHOP_SLOTS = 100
CRAFTSMAN_RANK = 0x7EA49
STORY_FLAG_BASE = 0x50D


def extract_shop_catalogue(scr):
    out, bad = [], []
    for kind, addr, val, guard, off in evaluated_writes(scr):
        if kind == "int" and 0 <= addr - SHOP_CATALOGUE_BASE < SHOP_SLOTS:
            cond = None
            if guard:
                g = guard["global"]
                if g == CRAFTSMAN_RANK:
                    cond = ({"min_craftsman_rank": guard["value"]}
                            if guard["op"] == "gre" else {**guard, "global": hex(g)})
                elif guard["op"] == "eq" and guard["value"] == 1 and g >= STORY_FLAG_BASE:
                    cond = {"story_flag": g - STORY_FLAG_BASE + 1}
                else:
                    cond = {**guard, "global": hex(g)}
            out.append({"id": len(out), "slot": addr - SHOP_CATALOGUE_BASE, "item_id": val,
                        "condition": cond})
        else:
            bad.append((off, hex(addr), val))
    if bad:
        raise ValueError(f"{scr.path.name}: unclassified {bad[:3]}")
    return out, {"base": hex(SHOP_CATALOGUE_BASE), "span": SHOP_SLOTS,
                 "id": "write order within this layer"}


# --------------------------------------------------------------------------- HAINIT
DUNGEON_HALL_LAYOUT = {
    "texture_resource_id":   (0x2A4193, 1, 1000),
    "dungeon_id":            (0x2A457B, 1, 1000),
    "floor":                 (0x2A4963, 1, 1000),
    "grid_x":                (0x2A4D4B, 1, 1000),
    "grid_y":                (0x2A5133, 1, 1000),
    "required_story_flags":  (0x2A551B, 2, 1000),
    "forbidden_story_flags": (0x2A5CEB, 2, 1000),
}


def extract_dungeon_halls(scr):
    records, unclassified = _tabulate(scr, DUNGEON_HALL_LAYOUT)
    _require_classified(scr, unclassified)
    for r in records.values():
        for k in ("required_story_flags", "forbidden_story_flags"):
            if k in r:
                r[k] = [f for f in r[k] if f]
    return [records[k] for k in sorted(records)], {"record_span": 1000}


# --------------------------------------------------------------------------- CSINIT
SPRITE_SELECTOR = 0xFD41B
COSTUME_SELECTION_BASE = 0x642C9


def extract_costume_sprite_variants(scr):
    """CSINIT: (shown sprite id, costume selector global, value) -> replacement sprite id."""
    rules, cur, var = [], None, None
    tested = []
    for ins in scr.instructions:
        op, a = label(ins), ins.args
        if op == "eq" and len(a) == 3 and a[1] == (T_GLOBAL_INT, SPRITE_SELECTOR):
            cur = a[2][1]
            tested.append(cur)
        elif op == "eq" and len(a) == 3 and a[1][0] == T_GLOBAL_INT:
            var = (a[1][1], a[2][1])
        elif op == "mov" and a and a[0] == (T_GLOBAL_INT, SPRITE_SELECTOR):
            rules.append({"id": len(rules), "sprite_id": cur,
                          "costume_slot": var[0] - COSTUME_SELECTION_BASE,
                          "costume_value": var[1], "replacement_sprite_id": a[1][1]})
    substituted = {rule["sprite_id"] for rule in rules}
    return rules, {"selector": f"0x{SPRITE_SELECTOR:x}",
                   "costume_selection_base": f"0x{COSTUME_SELECTION_BASE:x}",
                   # sprite blocks that end the hook without a substitution
                   "unchanged_sprite_ids": sorted(set(tested) - substituted),
                   "id": "rule order within this layer"}


# --------------------------------------------------------------------------- SIINIT
TOWN_SELECTOR = 0x3612
TOWN_BACKGROUND = 0x13EACC
TOWN_PREVIEW = 0x13EACD        # stage-select preview image
TOWN_BGM = 0x13EACE
SITE_ICON_BASE = 0x13EACF      # int[30] site i -> icon asset
SITE_X_BASE = 0x13EAED         # int[30]
SITE_Y_BASE = 0x13EB0B         # int[30]
SITE_LINK_BASE = 0x13EB29      # int[30][4] cursor link per direction d (-1 none)
SITE_NAME_BASE = 0x2481        # string[30]
SITE_SPAN = 30
SITE_LINK_DIRECTIONS = 4
TOWN_SCALARS = {TOWN_BACKGROUND: "background_asset_id", TOWN_PREVIEW: "preview_asset_id",
                TOWN_BGM: "bgm_id"}
SITE_ARRAYS = {SITE_ICON_BASE: "icon_asset_id", SITE_X_BASE: "x", SITE_Y_BASE: "y"}


def extract_town_sites(scr):
    """SIINIT: one record per town id (`eq (global 0x3612) TOWN` block) with its sites."""
    records = []
    unclassified = []
    for town, start, end in selector_guards(scr, TOWN_SELECTOR):
        rec = {"id": town}
        sites: dict[int, dict] = {}

        def site(i):
            return sites.setdefault(i, {"site": i})

        for ins in scr.instructions[start:end]:
            op = label(ins)
            if op == "set-string":
                dst_type, dst = ins.args[0]
                if dst_type == T_GLOBAL_STRING and SITE_NAME_BASE <= dst < SITE_NAME_BASE + SITE_SPAN:
                    site(dst - SITE_NAME_BASE)["name"] = literal(scr, ins.args[1])
                else:
                    unclassified.append((hex(ins.offset), op))
                continue
            write = static_global_write(ins)
            if write is None:
                if op not in ("jmp", "exit"):
                    unclassified.append((hex(ins.offset), op))
                continue
            dst, value = write
            if dst in TOWN_SCALARS:
                rec[TOWN_SCALARS[dst]] = value
            elif SITE_LINK_BASE <= dst < SITE_LINK_BASE + SITE_SPAN * SITE_LINK_DIRECTIONS:
                i, d = divmod(dst - SITE_LINK_BASE, SITE_LINK_DIRECTIONS)
                site(i).setdefault("cursor_links", [-1] * SITE_LINK_DIRECTIONS)[d] = value
            else:
                for base, name in SITE_ARRAYS.items():
                    if base <= dst < base + SITE_SPAN:
                        site(dst - base)[name] = value
                        break
                else:
                    unclassified.append((hex(ins.offset), hex(dst)))
        rec["sites"] = [sites[i] for i in sorted(sites)]
        records.append(rec)
    if unclassified:
        raise ValueError(f"{scr.path.name}: unclassified SIINIT writes {unclassified[:5]}")
    return records, {
        "selector": f"0x{TOWN_SELECTOR:x}",
        "site_arrays": {"icon": hex(SITE_ICON_BASE), "x": hex(SITE_X_BASE),
                        "y": hex(SITE_Y_BASE), "cursor_links": f"{SITE_LINK_BASE:#x} [30][4]",
                        "name": hex(SITE_NAME_BASE)},
        "cursor_links": "site index per direction 0..3; -1 = no link",
        "cleared_before_dispatch": "the prologue zeroes the scalars and clears the site arrays",
    }
