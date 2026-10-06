#!/usr/bin/env python3
"""Kamidori progression tables: quests, objectives, vocabulary, galleries, cards, scenes.

  QUINIT   quests (span 2000): name/requester/category strings, a nine-line description
           row, flag rows, reputation and item rewards. Ids >= 1000 are reward notices.
  FAINIT   chapter objectives (span 2000); FAMES dispatches their messages.
  VIINIT   vocabulary entries (span 1000); VIMES joins as `message`.
  CGINIT   event-CG gallery (span 3000) plus [page][slot] thumbnails.
  SPINIT   H-scene gallery [heroine page][slot] replay script / thumbnail.
  CDINIT2  card definitions (span 100); CDINIT selector-dispatched weighted card lists.
  SCINIT   scene dispatch (span 10000) annotated with the SCJUMP decision contexts that emit
           each scene id.
  EVINIT   event-CG substitutions per source CG; EUINIT CG/H-mode portrait rows.

All decoders use parallel-base arithmetic on static writes (destination - base = record id),
never instruction grouping: Kamidori INIT scripts interleave neighbouring records' fields.
Layout evidence: docs/kamidori-data-tables.md.
"""
from __future__ import annotations

from functools import cache

from init_kamidori_common import (
    NONE_TARGET, T_GLOBAL_INT, T_IMM, T_LOCAL_INT, authored_strings, final_writes, label,
    layer_scripts, static_global_write,
)


# --------------------------------------------------------------------------- helpers
def _decode(ints, strs, capacity, singles_i, singles_s, rows, first_id=0):
    """Generic parallel decoder.

    singles_i/singles_s: {field: base}; rows: {field: (base, stride)}. Claimed cells are
    tracked so leftovers can be reported (a nonempty leftover list means an unknown array).
    """
    used_i, used_s, recs = set(), set(), {}
    for rid in range(first_id, capacity):
        r = {}
        for f, b in singles_s.items():
            if b + rid in strs:
                r[f] = strs[b + rid]
                used_s.add(b + rid)
        for f, b in singles_i.items():
            if b + rid in ints:
                r[f] = ints[b + rid]
                used_i.add(b + rid)
        for f, (b, stride) in rows.items():
            cells = [b + rid * stride + c for c in range(stride)]
            if any(a in ints or a in strs for a in cells):
                src = strs if any(a in strs for a in cells) else ints
                r[f] = [src.get(a) if src is strs else src.get(a, 0) for a in cells]
                (used_s if src is strs else used_i).update(a for a in cells if a in src)
        if r:
            r["id"] = rid
            recs[rid] = r
    left = {"ints": sorted(f"0x{a:x}" for a in ints if a not in used_i),
            "strings": sorted(f"0x{a:x}" for a in strs if a not in used_s)}
    return [recs[k] for k in sorted(recs)], left


def _strip_empty_rows(records, fields):
    for r in records:
        for f in fields:
            if f in r and not any(v for v in r[f] if v):
                del r[f]


# --------------------------------------------------------------------------- QUINIT
QUEST_CAPACITY = 2000
QUEST_STRINGS = {"name": 0x625F, "requester": 0xB07F, "category": 0xB84F}
QUEST_TEXT = (0x6A2F, 9)  # 0 header, 1-4 overview, 5-6 clear condition, 7-8 failure condition
QUEST_INTS = {"requester_icon_asset_id": 0x3B6E92, "journal_sort_key": 0x3B7662,
              "deadline_days": 0x3B7E32, "prerequisite_quest_id": 0x3B8602,
              "failure_result_flag_id": 0x3BEB92, "difficulty_stars": 0x3BF362,
              "reward_money": 0x3C0AD2}
QUEST_ROWS = {"appear_required_flag_ids": (0x3B8DD2, 3), "appear_blocking_flag_ids": (0x3BA542, 3),
              "clear_flag_ids": (0x3BBCB2, 3), "failure_trigger_flag_ids": (0x3BD422, 3),
              "reputation_rewards": (0x3BFB32, 2),  # [on success, on failure]
              "reward_item_ids": (0x3C12A2, 4), "reward_item_quantities": (0x3C31E2, 4)}
QUEST_DISABLED_FLAG = 10000  # only ever set by DEBUGAM


def extract_quests(scr):
    ints, strs = final_writes(scr)
    rows = dict(QUEST_ROWS)
    rows["text"] = QUEST_TEXT
    records, left = _decode(ints, strs, QUEST_CAPACITY, QUEST_INTS, QUEST_STRINGS, rows)
    _strip_empty_rows(records, list(QUEST_ROWS))
    for r in records:
        if "reward_item_ids" in r or "reward_item_quantities" in r:
            ids = r.pop("reward_item_ids", [0] * 4)
            qty = r.pop("reward_item_quantities", [0] * 4)
            # RECEIPT/INFO2 skip a slot unless BOTH item>0 and qty>0
            r["reward_items"] = [{"item_id": i, "quantity": q}
                                 for i, q in zip(ids, qty) if i > 0 and q > 0]
        r["kind"] = "reward_notice" if r["id"] >= 1000 else "quest"
        if r.get("appear_required_flag_ids", [0])[0] == QUEST_DISABLED_FLAG:
            r["disabled"] = True
        if "text" in r:
            t = r["text"]
            r["text"] = {"header": t[0], "overview": [s for s in t[1:5] if s],
                         "clear_condition": [s for s in t[5:7] if s],
                         "failure_condition": [s for s in t[7:9] if s]}
    meta = {"capacity": QUEST_CAPACITY,
            "journal_ids": "0..999 (INFO2 sorts 1000 by journal_sort_key)",
            "reward_notice_ids": "1000..1999 (auto-complete, settled by RECEIPT)",
            "unclaimed": left}
    return records, meta


# --------------------------------------------------------------------------- FAINIT
OBJECTIVE_CAPACITY = 2000
OBJECTIVE_STRINGS = {"chapter_label": 0xC14B, "objective": 0xC91B}
OBJECTIVE_INTS = {"sort_key": 0x3CAC75, "message_script_id": 0x3D1205}
OBJECTIVE_ROWS = {"required_flag_ids": (0x3CB445, 3), "forbidden_flag_ids": (0x3CCBB5, 3),
                  "complete_flag_ids": (0x3CE325, 3),
                  "incomplete_override_flag_ids": (0x3CFA95, 3)}


def extract_objectives(scr):
    ints, strs = final_writes(scr)
    records, left = _decode(ints, strs, OBJECTIVE_CAPACITY, OBJECTIVE_INTS,
                            OBJECTIVE_STRINGS, OBJECTIVE_ROWS)
    _strip_empty_rows(records, list(OBJECTIVE_ROWS))
    for r in records:
        r["reserved"] = not r.get("objective")  # INFO1 skips empty objective strings
    return records, {"capacity": OBJECTIVE_CAPACITY,
                     "message_dispatch": "FAMES (selector 0x3cac74)", "unclaimed": left}


# --------------------------------------------------------------------------- VIINIT
VOCABULARY_CAPACITY = 1000
VOCABULARY_STRINGS = {"name": 0xD40B}
VOCABULARY_INTS = {"sort_key": 0x3D20DF, "message_script_id": 0x3D3C37}
VOCABULARY_ROWS = {"unlock_scene_ids_plus_one": (0x3D24C7, 3)}


def extract_vocabulary(scr):
    ints, strs = final_writes(scr)
    records, left = _decode(ints, strs, VOCABULARY_CAPACITY, VOCABULARY_INTS,
                            VOCABULARY_STRINGS, VOCABULARY_ROWS)
    _strip_empty_rows(records, list(VOCABULARY_ROWS))
    for r in records:
        name = r.get("name") or ""
        if name.startswith("【") and "】" in name:
            r["category"] = name[1:name.index("】")]
        unlock = r.pop("unlock_scene_ids_plus_one", None)
        # INFO8: col0 == 0 -> always listed; else listed when ANY viewed_scene[v-1] is set
        r["unlock_scene_ids"] = [v - 1 for v in (unlock or []) if v > 0]
    return records, {"capacity": VOCABULARY_CAPACITY,
                     "message_selector": "0x3d20de", "unclaimed": left}


# --------------------------------------------------------------------------- CGINIT
CG_CAPACITY = 3000
CG_INTS = {"page_id": 0x1003E6, "slot_id": 0x100F9E, "variant_ordinal": 0x101B56}
CG_ROWS = {"image": (0xFEC76, 2)}  # [gallery image asset, save-preview asset]
CG_THUMBS = (0x103E7E, 100)        # [page][slot] thumbnail asset
CG_THUMB_PAGES = 30


def extract_gallery(scr):
    ints, strs = final_writes(scr)
    thumbs, used = [], set()
    base, stride = CG_THUMBS
    for a, v in sorted(ints.items()):
        if base + stride <= a < base + stride * CG_THUMB_PAGES:
            page, slot = divmod(a - base, stride)
            thumbs.append({"page_id": page, "slot_id": slot, "thumbnail_asset_id": v})
            used.add(a)
    rest = {a: v for a, v in ints.items() if a not in used}
    records, left = _decode(rest, strs, CG_CAPACITY, CG_INTS, {}, CG_ROWS)
    for r in records:
        image = r.pop("image", [0, 0])
        r["image_asset_id"], r["save_preview_asset_id"] = image[0], image[1]
        r["listed"] = "variant_ordinal" in r  # CGMODE gates on variant_ordinal != 0
    return records, {"capacity": CG_CAPACITY, "thumbnails": thumbs, "unclaimed": left}


# --------------------------------------------------------------------------- SPINIT
H_SCENE_SLOTS = 50
H_SCENE_PAGES = 20
H_SCENE_ARRAYS = (("replay_script_id", 0x136331), ("thumbnail_asset_id", 0x136719))


def extract_h_scene_gallery(scr):
    ints, _ = final_writes(scr)
    out, left = {}, []
    for a, v in ints.items():
        for field, base in H_SCENE_ARRAYS:
            if base + H_SCENE_SLOTS <= a < base + H_SCENE_SLOTS * H_SCENE_PAGES:
                page, slot = divmod(a - base, H_SCENE_SLOTS)
                out.setdefault((page, slot), {"id": a - base, "heroine_page": page,
                                              "slot": slot})[field] = v
                break
        else:
            left.append(f"0x{a:x}")
    return [out[k] for k in sorted(out)], {"pages": "1..19 (HMODE loops <20)", "slots": "1..49",
                                           "id": "heroine_page*50 + slot", "unclaimed": left}


# --------------------------------------------------------------------------- CDINIT2
CARD_CAPACITY = 100
CARD_NAME_BASE = 0x29EF
CARD_STRINGS = {"name": CARD_NAME_BASE, "result_message": 0x2A53}
CARD_INTS = {"type_id": 0x2A64BC, "awarded_item_id": 0x2A6778, "money_min": 0x2A6840,
             "money_max": 0x2A68A4, "visual_asset_id": 0x2A7970}
CARD_ROWS = {"required_flag_ids": (0x2A6520, 3), "forbidden_flag_ids": (0x2A664C, 3),
             "recovery_min": (0x2A6908, 3), "recovery_max": (0x2A6A34, 3),
             "damage_min": (0x2A6B60, 3), "damage_max": (0x2A6C8C, 3),
             "condition_levels": (0x2A6DB8, 30)}


def extract_card_definitions(scr):
    ints, strs = final_writes(scr)
    records, left = _decode(ints, strs, CARD_CAPACITY, CARD_INTS, CARD_STRINGS, CARD_ROWS)
    _strip_empty_rows(records, list(CARD_ROWS))
    for r in records:
        if "condition_levels" in r:
            r["condition_levels"] = {str(c): v for c, v in enumerate(r["condition_levels"]) if v}
    return records, {"capacity": CARD_CAPACITY, "unclaimed": left}


# --------------------------------------------------------------------------- CDINIT
CARD_LIST_SELECTOR = 0x2A79D4
CARD_LIST_WEIGHT_BASE = 0x2A79D5   # int[100][3]: base weight, growth interval, growth weight
CARD_LIST_WEIGHT_STRIDE = 3
CARD_LIST_CARD_ID_BASE = 0x2A7B01  # int[100]: candidate slot -> CDINIT2 card id
CARD_LIST_CAPACITY = 100
CARD_LIST_WEIGHT_COLUMNS = ("base_weight", "growth_interval_turns", "growth_weight")
CARD_LIST_PRELUDE = ("mul", "copy-to-global")  # clears both arrays before dispatch (base only)


@cache
def _card_names() -> dict[int, str]:
    return authored_strings("CDINIT2", CARD_NAME_BASE, CARD_CAPACITY)


def extract_card_generation_lists(scr):
    """CDINIT: `eq (global 0x2a79d4) SELECTOR; jcc` blocks filling 1-based candidate slots.

    Himegari's layout (tools/extract_init.py `extract_card_generation_lists`) at Kamidori
    addresses; append layers carry selector blocks without the clearing prelude.
    """
    lists: dict[int, dict[int, dict]] = {}
    selector = None
    unclassified = []
    for ins in scr.instructions:
        op, a = label(ins), ins.args
        if (op == "eq" and len(a) == 3 and a[0][0] == T_LOCAL_INT
                and a[1] == (T_GLOBAL_INT, CARD_LIST_SELECTOR) and a[2][0] == T_IMM):
            selector = a[2][1]
            if selector in lists:
                raise ValueError(f"{scr.path.name}: duplicate selector {selector}")
            lists[selector] = {}
            continue
        if op == "mov":
            write = static_global_write(ins)
            if selector is None or write is None or not isinstance(write[1], int):
                unclassified.append(f"0x{ins.offset:x}")
                continue
            dst, value = write
            if CARD_LIST_CARD_ID_BASE < dst < CARD_LIST_CARD_ID_BASE + CARD_LIST_CAPACITY:
                slot, field = dst - CARD_LIST_CARD_ID_BASE, "card_id"
            elif (CARD_LIST_WEIGHT_BASE <= dst
                  < CARD_LIST_WEIGHT_BASE + CARD_LIST_CAPACITY * CARD_LIST_WEIGHT_STRIDE):
                slot, column = divmod(dst - CARD_LIST_WEIGHT_BASE, CARD_LIST_WEIGHT_STRIDE)
                field = CARD_LIST_WEIGHT_COLUMNS[column]
            else:
                unclassified.append(f"0x{ins.offset:x}")
                continue
            if slot == 0:
                raise ValueError(f"{scr.path.name}: selector {selector} writes reserved slot 0")
            lists[selector].setdefault(slot, {"slot": slot})[field] = value
            continue
        if op in ("jcc", "jmp", "exit", "instruction-marker-noop", "comment"):
            continue
        if op in CARD_LIST_PRELUDE and selector is None:
            continue
        unclassified.append(f"0x{ins.offset:x}")
    if unclassified:
        raise ValueError(f"{scr.path.name}: unclassified CDINIT instructions {unclassified[:5]}")
    names = _card_names()
    records = []
    for sel in sorted(lists):
        slots = lists[sel]
        if sorted(slots) != list(range(1, len(slots) + 1)):
            raise ValueError(f"{scr.path.name}: selector {sel} has non-contiguous slots")
        candidates = []
        for slot in sorted(slots):
            entry = slots[slot]
            if "card_id" in entry:
                entry["card_name"] = names.get(entry["card_id"], "")
            candidates.append(entry)
        records.append({"id": sel, "candidates": candidates})
    return records, {
        "selector_global": f"0x{CARD_LIST_SELECTOR:x}",
        "card_id_base": f"0x{CARD_LIST_CARD_ID_BASE:x}",
        "weight_base": f"0x{CARD_LIST_WEIGHT_BASE:x} [100][3]",
        "card_definition_table": "CDINIT2",
    }


# --------------------------------------------------------------------------- SCINIT
SC_CAPACITY = 10000
SC_INTS = {"script_resource_id": 0x1372DD, "scjump_context": 0x1399ED, "queue_priority": 0x13C0FD}
SCENE_SELECTOR = 0xFEC10
SCJUMP_CONTEXT = 0x3070


def scjump_decision_contexts(scr):
    """scene id -> {0x3070 context}: SCJUMP heads with `eq (l) (g 0x3070) N; jcc (l) -1 label`."""
    starts = []
    ins = scr.instructions
    for i, x in enumerate(ins[:64]):
        if (label(x) == "eq" and len(x.args) == 3 and x.args[1] == (T_GLOBAL_INT, SCJUMP_CONTEXT)
                and i + 1 < len(ins) and label(ins[i + 1]) == "jcc"):
            targets = [v for t, v in ins[i + 1].args[1:] if t == T_IMM and v != NONE_TARGET]
            if targets:
                starts.append((targets[0], x.args[2][1]))
    starts.sort()
    out = {}
    for x in ins:
        if (label(x) == "mov" and x.args and x.args[0] == (T_GLOBAL_INT, SCENE_SELECTOR)
                and x.args[1][0] == T_IMM):
            ctx = None
            for start, context in starts:
                if x.offset >= start:
                    ctx = context
            out.setdefault(x.args[1][1], set()).add(ctx)
    return out


@cache
def _scjump_contexts() -> dict[int, frozenset]:
    contexts: dict[int, set] = {}
    for scr in layer_scripts("SCJUMP"):
        for scene, seen in scjump_decision_contexts(scr).items():
            contexts.setdefault(scene, set()).update(seen)
    return {scene: frozenset(seen) for scene, seen in contexts.items()}


def extract_scene_dispatch(scr, scjump_scripts=None):
    """SCINIT records; each lists the SCJUMP contexts (base + `$n$` layers) emitting its id."""
    ints, strs = final_writes(scr)
    records, left = _decode(ints, strs, SC_CAPACITY, SC_INTS, {}, {})
    if scjump_scripts is None:
        contexts = _scjump_contexts()
    else:
        contexts = {}
        for sj in scjump_scripts:
            for k, v in scjump_decision_contexts(sj).items():
                contexts.setdefault(k, set()).update(v)
    for r in records:
        if contexts:
            seen = sorted(contexts.get(r["id"], ()), key=lambda c: (c is None, c))
            r["scjump_contexts_emitting"] = seen
            if "scjump_context" in r and seen:
                r["context_matches_scjump"] = r["scjump_context"] in seen
    return records, {"selector_global": f"0x{SCENE_SELECTOR:x}",
                     "scjump_sources": "SCJUMP base + every non-stub $n$SCJUMP",
                     "unclaimed": left}


# --------------------------------------------------------------------------- EVINIT
EV_SELECTOR = 0xFD41B
EV_FLAGS_BASE, EV_FORM_BASE = 0x64201, 0x642C9


def extract_event_cg_variants(scr):
    """EVINIT: per source event-CG id, ordered (character, form value) -> replacement CG rules.

    Every rule tests `(G[0x64201+k] & 2) == 2 && G[0x642c9+k] == form`; rules run in order and
    the LAST matching assignment wins (no early exit inside a source block).
    """
    blocks, cur, ch, form = [], None, None, None
    for x in scr.instructions:
        op, a = label(x), x.args
        if op == "eq" and len(a) == 3 and a[1] == (T_GLOBAL_INT, EV_SELECTOR):
            cur = {"id": len(blocks), "source_cg_asset_id": a[2][1], "rules": []}
            blocks.append(cur)
        elif (op == "and" and len(a) == 3 and a[1][0] == T_GLOBAL_INT
              and EV_FLAGS_BASE <= a[1][1] < EV_FLAGS_BASE + 100):
            ch = a[1][1] - EV_FLAGS_BASE
        elif (op == "eq" and len(a) == 3 and a[1][0] == T_GLOBAL_INT
              and EV_FORM_BASE <= a[1][1] < EV_FORM_BASE + 100):
            form = (a[1][1] - EV_FORM_BASE, a[2][1])
        elif op == "mov" and a and a[0] == (T_GLOBAL_INT, EV_SELECTOR) and cur:
            cur["rules"].append({"character_index": ch, "form_character_index": form and form[0],
                                 "form_id": form and form[1], "target_cg_asset_id": a[1][1]})
    return blocks, {"selector_global": f"0x{EV_SELECTOR:x}", "hook_chain": "0x3d4240",
                    "id": "source block order within this layer"}


# --------------------------------------------------------------------------- EUINIT
EU_ROWS = 20
EU_CG_MODE_BASE = 0xFEC12      # int[20][2] CGMODE portrait / sprite asset per row
EU_COSTUME_SLOT_BASE = 0xFEC3A  # int[20] costume slot per row
EU_H_MODE_BASE = 0xFEC4E       # int[20][2] HMODE portrait / sprite asset per row
EU_ASSET_COLUMNS = ("portrait_asset_id", "sprite_asset_id")


def extract_gallery_portraits(scr):
    """EUINIT: per row k, CGMODE and HMODE portrait/sprite assets and the costume slot."""
    rows: dict[int, dict] = {}
    unclassified = []
    for x in scr.instructions:
        write = static_global_write(x)
        if write is None:
            if label(x) != "exit":
                unclassified.append(f"0x{x.offset:x}")
            continue
        dst, value = write
        if EU_CG_MODE_BASE <= dst < EU_CG_MODE_BASE + 2 * EU_ROWS:
            k, c = divmod(dst - EU_CG_MODE_BASE, 2)
            rows.setdefault(k, {"id": k}).setdefault("cg_mode", {})[EU_ASSET_COLUMNS[c]] = value
        elif EU_COSTUME_SLOT_BASE <= dst < EU_COSTUME_SLOT_BASE + EU_ROWS:
            k = dst - EU_COSTUME_SLOT_BASE
            rows.setdefault(k, {"id": k})["costume_slot"] = value
        elif EU_H_MODE_BASE <= dst < EU_H_MODE_BASE + 2 * EU_ROWS:
            k, c = divmod(dst - EU_H_MODE_BASE, 2)
            rows.setdefault(k, {"id": k}).setdefault("h_mode", {})[EU_ASSET_COLUMNS[c]] = value
        else:
            unclassified.append(f"0x{x.offset:x}:0x{dst:x}")
    if unclassified:
        raise ValueError(f"{scr.path.name}: unclassified EUINIT writes {unclassified[:5]}")
    return [rows[k] for k in sorted(rows)], {
        "cg_mode": f"0x{EU_CG_MODE_BASE:x} [20][2]",
        "costume_slot": f"0x{EU_COSTUME_SLOT_BASE:x} [20]",
        "h_mode": f"0x{EU_H_MODE_BASE:x} [20][2]",
    }
