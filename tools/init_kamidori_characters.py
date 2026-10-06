#!/usr/bin/env python3
"""Kamidori character tables: CCINIT, CNINIT, CIINIT, CVINIT.

  CCINIT  class-change rule list (not a table): `character == N && !(state[N][k] & 1)
          [&& level >= L]` blocks writing a title and additive deltas into CALCCC's output block.
  CNINIT  per-unit voice family, face asset and five speaker-name forms (string[1000][5]).
  CIINIT  character-profile rows (span 200) keyed by profile id; CIMES joins as `message`.
  CVINIT  voice/text-colour setting slots plus the unit -> slot inverse map.

Every layout is absolute, so `$n$` layers decode with the same constants. Field evidence:
docs/kamidori-data-tables.md.
"""
from __future__ import annotations

from functools import cache

from init_kamidori_common import (
    NONE_TARGET, T_GLOBAL_INT, T_GLOBAL_STRING, T_IMM, T_LOCAL_INT, T_LOCAL_PTR,
    authored_strings, imm, label, literal,
)

# ---------------------------------------------------------------- CCINIT (class change)
CC_UNIT = 0x2D2845            # current canonical character id (selector)
CC_STATE = 0x7E269            # int[100][20] per-character rule state: bit0 applied, bit1 event-unlocked
CC_LEVELS = 0x6432D           # character_levels[100]
CC_TITLE_PRIORITY = 0x2D2014  # 0 none; level threshold of the chosen title; -1 = event title
CC_CAPACITY = 0x2D2015        # party-capacity delta (CALCCC adds to 0x7ea3b)
CC_COST = 0x2D2016            # party-cost delta (CALCCC adds to 0x6577d[c])
CC_RESIST = 0x2D2017          # int[8] element-resistance deltas (CALCCC adds to 0x643f5[c])
CC_STATS = 0x2D201F           # int[14] stat deltas (CALCCC adds to 0x64715[c])
CC_CAPS = 0x2D202D            # int[14] stat-cap deltas (CALCCC adds to 0x64c8d[c])
CC_SKILL_SLOTS = 0x2D203B     # int[8] skill-list replacements (>0 -> 0x657e1[c][k])
CC_SKILL_LEARN = 0x2D2043     # int[1000] learned-skill flags (-> 0x65b01[c][skill])
CC_TITLE_OUT = 0x4227         # string: new class title (-> 0x2b3[c])

UNIT_NAME_BASE = 0x2AB7       # EBINIT string[1000]: unit id -> name
SKILL_NAME_BASE = 0x5998      # SKINIT string[1000]: skill id -> name

STAT_COLUMNS = [
    "accuracy", "evasion", "physical_attack", "physical_defense", "magic_attack",
    "magic_defense", "agility", "luck", "movement", "customer_service",
    "max_hp", "max_sp", "max_fs", "capacity",
]
ELEMENTS = ["none", "physical", "earth", "cold", "fire", "lightning", "holy", "dark"]
COMPARISONS = ("gre", "gr", "lt", "lte", "eq", "ne")


@cache
def _unit_names() -> dict[int, str]:
    return {k: v for k, v in authored_strings("EBINIT", UNIT_NAME_BASE, 1000).items() if k}


@cache
def _skill_names() -> dict[int, str]:
    return {k: v for k, v in authored_strings("SKINIT", SKILL_NAME_BASE, 1000).items() if k}


def _guard_indices(scr):
    return [
        i for i, ins in enumerate(scr.instructions)
        if (label(ins) == "eq" and len(ins.args) == 3 and ins.args[0] == (T_LOCAL_INT, 0)
            and ins.args[1] == (T_GLOBAL_INT, CC_UNIT) and ins.args[2][0] == T_IMM)
    ]


def extract_class_change_rules(scr, unit_names=None, skill_names=None):
    """CCINIT is a rule list, not a table. CALCCC zeroes the output block, then calls every
    script id in 0x3d4260[0..31] (base CCINIT plus each `$n$CCINIT`). Each block:
    (character == N) && !(state[N][k] & 1) [&& level >= L] -> mark applied, optional event gate
    (state bit1 must be set or the WHOLE script exits), title selection (priority = level
    threshold, or -1 for event titles; higher wins), then additive deltas.
    """
    unit_names = _unit_names() if unit_names is None else unit_names
    skill_names = _skill_names() if skill_names is None else skill_names
    guards = _guard_indices(scr)
    exits = {ins.offset for ins in scr.instructions if label(ins) == "exit"}
    ins_all = scr.instructions
    records, unhandled = [], []
    for n, gi in enumerate(guards):
        end = guards[n + 1] if n + 1 < len(guards) else len(ins_all)
        block = ins_all[gi:end]
        unit_id = block[0].args[2][1]
        rec = {
            "id": n + 1,
            "guard_offset": f"0x{block[0].offset:x}",
            "unit_id": unit_id,
            "unit_name": unit_names.get(unit_id, ""),
            "state_column": None,
            "minimum_level": None,
            "event_gated": False,
            "extra_conditions": [],
            "title": None,
            "title_priority": None,
            "party_capacity_delta": 0,
            "party_cost_delta": 0,
            "stat_deltas": {},
            "stat_cap_deltas": {},
            "resistance_deltas": {},
            "skill_slot_replacements": {},
            "skills_learned": [],
        }
        pending_lookup = None
        for ins in block[1:]:
            op, a = label(ins), ins.args
            if op == "lookup-array-2d" and a[1] == (T_GLOBAL_INT, CC_STATE):
                if rec["state_column"] is None and a[4][0] == T_IMM:
                    rec["state_column"] = a[4][1]
                continue
            if (op == "lookup-array" and a[1] == (T_GLOBAL_INT, CC_LEVELS)
                    and a[2] == (T_GLOBAL_INT, CC_UNIT)):
                pending_lookup = ("level", a[0])
                continue
            if op in ("lookup-array", "lookup-array-2d") and a[1][0] == T_GLOBAL_INT:
                pending_lookup = ("global", a)
                continue
            if (op in COMPARISONS and len(a) == 3 and pending_lookup
                    and pending_lookup[0] == "level" and a[1] == pending_lookup[1]):
                if op == "gre" and a[2][0] == T_IMM:
                    rec["minimum_level"] = a[2][1]
                else:
                    rec["extra_conditions"].append({"op": op, "lhs": "level", "rhs": a[2]})
                pending_lookup = None
                continue
            if (op in COMPARISONS and pending_lookup and pending_lookup[0] == "global"
                    and a[1] == pending_lookup[1][0]):
                la = pending_lookup[1]
                rec["extra_conditions"].append({
                    "op": op,
                    "global": f"0x{la[1][1]:x}",
                    "index": [f"0x{x[1]:x}" if x[0] == T_GLOBAL_INT else x[1] for x in la[2:]],
                    "rhs": a[2][1] if a[2][0] == T_IMM else f"arg{a[2]}",
                })
                pending_lookup = None
                continue
            if op == "jcc":
                if a[1][1] != NONE_TARGET and a[1][1] in exits:
                    rec["event_gated"] = True
                continue
            if op == "set-string" and a[0] == (T_GLOBAL_STRING, CC_TITLE_OUT):
                rec["title"] = literal(scr, a[1])
                continue
            if op == "mov" and a[0] == (T_GLOBAL_INT, CC_TITLE_PRIORITY):
                rec["title_priority"] = imm(a[1])
                continue
            if (op == "sub" and a[0] == (T_GLOBAL_INT, CC_TITLE_PRIORITY)
                    and a[1][0] == T_IMM and a[2][0] == T_IMM):
                rec["title_priority"] = a[1][1] - a[2][1]
                continue
            if op in ("add", "sub") and a[0][0] == T_GLOBAL_INT and a[0] == a[1] and a[2][0] == T_IMM:
                d = a[0][1]
                v = a[2][1] if op == "add" else -a[2][1]
                if d == CC_CAPACITY:
                    rec["party_capacity_delta"] += v
                elif d == CC_COST:
                    rec["party_cost_delta"] += v
                elif CC_STATS <= d < CC_STATS + 14:
                    k = STAT_COLUMNS[d - CC_STATS]
                    rec["stat_deltas"][k] = rec["stat_deltas"].get(k, 0) + v
                elif CC_CAPS <= d < CC_CAPS + 14:
                    k = STAT_COLUMNS[d - CC_CAPS]
                    rec["stat_cap_deltas"][k] = rec["stat_cap_deltas"].get(k, 0) + v
                elif CC_RESIST <= d < CC_RESIST + 8:
                    k = ELEMENTS[d - CC_RESIST]
                    rec["resistance_deltas"][k] = rec["resistance_deltas"].get(k, 0) + v
                else:
                    unhandled.append((rec["guard_offset"], hex(ins.offset), op, str(a)))
                continue
            if op == "mov" and a[0][0] == T_GLOBAL_INT:
                d = a[0][1]
                if CC_SKILL_SLOTS <= d < CC_SKILL_SLOTS + 8:
                    rec["skill_slot_replacements"][d - CC_SKILL_SLOTS] = imm(a[1])
                    continue
                if CC_SKILL_LEARN <= d < CC_SKILL_LEARN + 1000:
                    sid = d - CC_SKILL_LEARN
                    rec["skills_learned"].append(
                        {"skill_id": sid, "skill_name": skill_names.get(sid, "")}
                    )
                    continue
            if op in ("and", "or", "eq", "lt", "bit-set", "sub") and a[0][0] in (T_LOCAL_INT, T_LOCAL_PTR):
                continue  # predicate plumbing / applied-bit set
            if op == "exit":
                continue
            unhandled.append((rec["guard_offset"], hex(ins.offset), op, str(a)))
        records.append(rec)
    meta = {
        "rule_kind": "kamidori-class-change",
        "selector_global": f"0x{CC_UNIT:x}",
        "state_table": f"0x{CC_STATE:x} int[100][20] (bit0 applied, bit1 event unlock)",
        "outputs": {
            "title": f"0x{CC_TITLE_OUT:x}", "title_priority": f"0x{CC_TITLE_PRIORITY:x}",
            "party_capacity": f"0x{CC_CAPACITY:x}", "party_cost": f"0x{CC_COST:x}",
            "resistances[8]": f"0x{CC_RESIST:x}", "stats[14]": f"0x{CC_STATS:x}",
            "stat_caps[14]": f"0x{CC_CAPS:x}", "skill_slots[8]": f"0x{CC_SKILL_SLOTS:x}",
            "skill_learn[1000]": f"0x{CC_SKILL_LEARN:x}",
        },
        "event_gate_semantics": (
            "if the rule matches but state bit1 is clear, the script exits "
            "(later rules are not evaluated in this call)"
        ),
        "unhandled_instructions": unhandled,
    }
    return records, meta


# ---------------------------------------------------------------- CNINIT
CN_FAMILY = 0xFDC6C     # int[1000] unit id -> voice-family representative unit id
CN_FACE = 0xFD49C       # int[1000] unit id -> CF*.AGF face asset (HISTORY)
CN_NAMES = 0x322        # string[1000][5] unit id x name form (scene speaker plate)
CN_FORMS = ["name", "alternate_name", "descriptor", "unused_3", "unknown_placeholder"]


def extract_character_names(scr):
    rows = {}
    for ins in scr.instructions:
        op, a = label(ins), ins.args
        if op == "mov" and a and a[0][0] == T_GLOBAL_INT:
            d = a[0][1]
            if CN_FAMILY <= d < CN_FAMILY + 1000:
                rows.setdefault(d - CN_FAMILY, {})["voice_family_unit_id"] = imm(a[1])
            elif CN_FACE <= d < CN_FACE + 1000:
                rows.setdefault(d - CN_FACE, {})["face_asset_id"] = imm(a[1])
            else:
                raise ValueError(f"{scr.path.name}: unexpected int write 0x{d:x}")
        elif op == "set-string" and a[0][0] == T_GLOBAL_STRING:
            r, c = divmod(a[0][1] - CN_NAMES, 5)
            rows.setdefault(r, {}).setdefault("names", {})[CN_FORMS[c]] = literal(scr, a[1])
        elif op != "exit":
            raise ValueError(f"{scr.path.name}: unexpected {op} at 0x{ins.offset:x}")
    recs = [{"id": k, **v} for k, v in sorted(rows.items())]
    return recs, {
        "key": "EBINIT unit id", "voice_family": f"0x{CN_FAMILY:x}",
        "face_asset": f"0x{CN_FACE:x}", "names": f"0x{CN_NAMES:x} string[1000][5]",
        "name_form_selector": "0x306f (scene scripts mov 0/1/2/4; 0x306d = speaker unit id)",
    }


# ---------------------------------------------------------------- CIINIT
CI_SPAN = 200
CI_ARRAYS = {
    0xD0EB: ("name", "s"), 0xD1B3: ("race_text", "s"), 0xD27B: ("sex_text", "s"),
    0xD343: ("epithet", "s"), 0x3D19D6: ("sort_key", "i"), 0x3D1A9E: ("unit_id", "i"),
    0x3D1DBE: ("portrait_x_offset", "i"), 0x3D1E86: ("portrait_y_offset", "i"),
    0x3D1F4E: ("portrait_asset_id", "i"), 0x3D2016: ("biography_script_id", "i"),
}
CI_UNLOCK = 0x3D1B66  # int[200][3] 1-based flag indices into 0xf35a3


def _ci_field(d):
    for base, (name, kind) in CI_ARRAYS.items():
        if base < d < base + CI_SPAN:
            return d - base, name, kind
    return None


def extract_character_profiles(scr):
    """CIINIT rows; the CIMES biography dispatch joins as each record's `message`."""
    rows = {}
    for ins in scr.instructions:
        op, a = label(ins), ins.args
        if op == "exit":
            continue
        d = a[0][1]
        if op == "set-string" or (op == "mov" and a[0][0] == T_GLOBAL_INT
                                  and not CI_UNLOCK <= d < CI_UNLOCK + 3 * CI_SPAN):
            hit = _ci_field(d)
            if hit is None:
                raise ValueError(f"{scr.path.name}: unexpected write 0x{d:x}")
            rid, name, kind = hit
            rows.setdefault(rid, {})[name] = literal(scr, a[1]) if kind == "s" else imm(a[1])
        elif op == "mov" and CI_UNLOCK <= d < CI_UNLOCK + 3 * CI_SPAN:
            r, c = divmod(d - CI_UNLOCK, 3)
            rows.setdefault(r, {}).setdefault("unlock_flag_indices", {})[c] = imm(a[1])
        elif op == "sub" and a[1][0] == T_IMM:  # negative ints
            if (hit := _ci_field(d)) is not None:
                rows.setdefault(hit[0], {})[hit[1]] = a[1][1] - a[2][1]
        else:
            raise ValueError(f"{scr.path.name}: unexpected {op} at 0x{ins.offset:x}")
    recs = [{"id": k, **v} for k, v in sorted(rows.items())]
    return recs, {
        "key": "profile id 1..199 (0x3d19d5 selector)", "span": CI_SPAN,
        "unlock_rule": ("any listed flag 0xf35a3[v-1] set (or none listed) AND, "
                        "if unit_id, 0xf5cb3[unit_id]"),
    }


# ---------------------------------------------------------------- CVINIT
CV_ARRAYS = {0xFD884: "default_text_color", 0xFE43C: "unit_id", 0xFE824: "preview_voice_asset_id"}
CV_INVERSE = 0xFE054
CV_STRINGS = 0x16AA  # string[...][3]: name, descriptor, voice actress
CV_STRING_COLUMNS = ["name", "descriptor", "voice_actress"]


def extract_voice_configuration(scr):
    rows, inverse = {}, {}
    for ins in scr.instructions:
        op, a = label(ins), ins.args
        if op == "exit":
            continue
        d = a[0][1]
        if op == "mov":
            if CV_INVERSE <= d < CV_INVERSE + 1000:
                inverse[d - CV_INVERSE] = imm(a[1])
                continue
            for base, name in CV_ARRAYS.items():
                if base <= d < base + 1000:
                    rows.setdefault(d - base, {})[name] = imm(a[1])
                    break
            else:
                raise ValueError(f"{scr.path.name}: unexpected 0x{d:x}")
        elif op == "set-string":
            r, c = divmod(d - CV_STRINGS, 3)
            rows.setdefault(r, {})[CV_STRING_COLUMNS[c]] = literal(scr, a[1])
        else:
            raise ValueError(f"{scr.path.name}: unexpected {op} at 0x{ins.offset:x}")
    recs = [{"id": k, **v} for k, v in sorted(rows.items())]
    return recs, {"inverse_unit_to_slot": inverse, "slot_0": "extra/system voice (no unit)"}
