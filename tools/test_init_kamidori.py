#!/usr/bin/env python3
"""Pure tests for the Kamidori *INIT specializations (synthetic scripts, no game content).

Run: py -3.11 -X utf8 tools/test_init_kamidori.py
"""
from __future__ import annotations

import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parent))
import init_kamidori
import init_kamidori_characters as characters
import init_kamidori_items as items
import init_kamidori_maps as maps
import init_kamidori_progression as progression
import sys4load
from sys4load import Instruction

IMM, STR, GINT, GSTR, LINT, LPTR = 0, 2, 3, 5, 9, 12
NONE_TARGET = 0xFFFFFFFF


def _opcodes() -> dict[str, int]:
    wanted = {"exit", "add", "sub", "mov", "and", "eq", "gre", "lookup-array",
              "lookup-array-2d", "copy-inline-int-array", "jmp", "jcc", "set-string"}
    found: dict[str, int] = {}
    for op in range(0x400):
        label = sys4load.display_label(op)
        if label in wanted:
            found.setdefault(label, op)
    missing = wanted - set(found)
    if missing:
        raise RuntimeError(f"opcode table lacks {sorted(missing)}")
    return found


OP = _opcodes()


class Script:
    """Builds a minimal stand-in for sys4load.Sys4Script: instructions, strings and a body
    holding length-prefixed inline arrays at known dword offsets."""

    def __init__(self, name: str = "SYNTH.BIN") -> None:
        self.instructions: list[Instruction] = []
        self.strings: dict[int, tuple[str, int]] = {}
        self.body: list[int] = [0] * 16
        self.offset = 0
        self.path = Path(name)

    def emit(self, label: str, *args) -> int:
        offset = self.offset
        self.instructions.append(Instruction(offset, OP[label], label, list(args)))
        self.offset += 1 + 2 * len(args)
        return offset

    def text(self, value: str) -> tuple[int, int]:
        start = 0x10000 + len(self.strings)
        self.strings[start] = (value, 1)
        return (STR, start)

    def array(self, values: list[int]) -> tuple[int, int]:
        start = len(self.body)
        self.body += [len(values), *[v & 0xFFFFFFFF for v in values]]
        return (IMM, start)

    def done(self):
        return SimpleNamespace(instructions=self.instructions, strings=self.strings,
                               dwords=tuple(self.body), nbody=len(self.body), path=self.path)


class FloorMapTests(unittest.TestCase):
    def test_doubled_grid_and_labyrinth_minus_one_via_local_sub(self) -> None:
        s = Script("MPINIT.BIN")
        s.emit("mov", (GINT, maps.WIDTH_GLOBAL), (IMM, 0))          # prologue, ignored
        s.emit("sub", (LINT, 0), (IMM, 0), (IMM, 1))                # local 0 = -1
        s.emit("eq", (LINT, 1), (GINT, maps.LAB_SELECTOR), (LINT, 0))
        s.emit("eq", (LINT, 2), (GINT, maps.FLOOR_SELECTOR), (IMM, 3))
        s.emit("and", (LINT, 3), (LINT, 1), (LINT, 2))
        s.emit("jcc", (LINT, 3), (IMM, NONE_TARGET), (IMM, 0x999))
        s.emit("mov", (GINT, maps.WIDTH_GLOBAL), (IMM, 2))
        s.emit("mov", (GINT, maps.HEIGHT_GLOBAL), (IMM, 2))
        rows = {gy: [gy * 10 + x for x in range(1, 5)] for gy in range(1, 5)}
        for gy, values in rows.items():
            dst = maps.FLOOR_TEMPLATE_BASE + gy * maps.GRID_STRIDE + 1
            s.emit("copy-inline-int-array", (GINT, dst), s.array(values))
        s.emit("exit")
        records, meta = maps.extract_floor_maps(s.done())
        self.assertEqual([], meta["unclassified"])
        self.assertEqual(1, len(records))
        rec = records[0]
        self.assertEqual((-1, 3, "-1:3"), (rec["labyrinth_id"], rec["floor_no"], rec["id"]))
        self.assertEqual((2, 2), (rec["width_tiles"], rec["height_tiles"]))
        self.assertEqual([0, 0, 0, 0, 0], rec["grid"][0])           # zero frame row
        self.assertEqual([0, 21, 22, 23, 24], rec["grid"][2])
        # tiles at even (x, y); h-edges at odd x on tile rows; v-edges at odd y
        self.assertEqual([[22, 24], [42, 44]], rec["tiles"])
        self.assertEqual([[23], [43]], rec["h_edges"])
        self.assertEqual([[32, 34]], rec["v_edges"])

    def test_misplaced_row_is_rejected(self) -> None:
        s = Script("MPINIT.BIN")
        s.emit("eq", (LINT, 1), (GINT, maps.LAB_SELECTOR), (IMM, 1))
        s.emit("mov", (GINT, maps.WIDTH_GLOBAL), (IMM, 1))
        s.emit("mov", (GINT, maps.HEIGHT_GLOBAL), (IMM, 1))
        s.emit("copy-inline-int-array", (GINT, maps.FLOOR_TEMPLATE_BASE + 55), s.array([1, 2]))
        with self.assertRaises(ValueError):
            maps.extract_floor_maps(s.done())


class StageCatalogTests(unittest.TestCase):
    def test_keys_by_base_and_stride_regardless_of_write_order(self) -> None:
        s = Script("STINIT2.BIN")
        # labyrinth/floor for stage 5 precede stage 4's name: order must not matter
        s.emit("mov", (GINT, 0x141134 + 5), (IMM, 7))
        s.emit("mov", (GINT, 0x14151C + 5), (IMM, 2))
        s.emit("set-string", (GSTR, maps.STAGE_NAME_BASE + 4), s.text("four"))
        s.emit("mov", (GINT, 0x141904 + 5 * 10 + 6), (IMM, 33))    # required party col 6
        s.emit("sub", (GINT, 0x147E94 + 4), (IMM, 0), (IMM, 3))   # negative via sub
        s.emit("set-string", (GSTR, maps.STAGE_NAME_BASE + 5), s.text("five"))
        s.emit("exit")
        records, meta = maps.extract_stage_catalog(s.done())
        self.assertEqual([], meta["unclassified"])
        by_id = {r["id"]: r for r in records}
        self.assertEqual({"id": 4, "name": "four", "recommended_level": -3}, by_id[4])
        self.assertEqual("five", by_id[5]["name"])
        self.assertEqual((7, 2), (by_id[5]["labyrinth_id"], by_id[5]["floor_no"]))
        self.assertEqual(33, by_id[5]["required_party_unit_ids"][6])
        self.assertEqual(10, len(by_id[5]["required_party_unit_ids"]))


class ItemLayoutTests(unittest.TestCase):
    def test_fixed_layout_divmod_keying(self) -> None:
        s = Script("$3$ITINIT.BIN")
        item = 1234                                                 # an append-layer id
        s.emit("set-string", (GSTR, 0x4228 + item), s.text("blade"))
        s.emit("mov", (GINT, 0x3054C8 + item * 14 + 2), (IMM, 15))  # stat_modifiers col 2
        s.emit("mov", (GINT, 0x2D3018 + item * 17 + 5), (IMM, 900)) # costume col 5 (no names)
        s.emit("sub", (GINT, 0x30F108 + item * 3 + 1), (IMM, 0), (IMM, 1))
        s.emit("mov", (GINT, 0x2DC488 + item), (IMM, 77))           # sort_key, stride 1
        s.emit("exit")
        records, meta = items.extract_items(s.done())
        self.assertEqual(2000, meta["record_span"])
        self.assertEqual(1, len(records))
        rec = records[0]
        self.assertEqual((item, "blade"), (rec["id"], rec["name"]))
        attrs = rec["attributes"]
        self.assertEqual({"physical_attack": 15}, attrs["stat_modifiers"])
        self.assertEqual({"5": 900}, attrs["costume_graphic_asset_ids"])
        self.assertEqual({"sell": -1}, attrs["prices"])
        self.assertEqual(77, attrs["sort_key"])

    def test_unclassified_write_raises(self) -> None:
        s = Script("ITINIT.BIN")
        s.emit("mov", (GINT, 0x10), (IMM, 1))
        with self.assertRaises(ValueError):
            items.extract_items(s.done())


class ClassChangeTests(unittest.TestCase):
    def test_rule_block_parsing(self) -> None:
        s = Script("CCINIT.BIN")
        cc = characters
        s.emit("eq", (LINT, 0), (GINT, cc.CC_UNIT), (IMM, 7))
        s.emit("jcc", (LINT, 0), (IMM, NONE_TARGET), (IMM, 0x400))
        s.emit("lookup-array-2d", (LPTR, 0), (GINT, cc.CC_STATE), (GINT, cc.CC_UNIT),
               (IMM, 20), (IMM, 3))
        s.emit("lookup-array", (LINT, 1), (GINT, cc.CC_LEVELS), (GINT, cc.CC_UNIT))
        s.emit("gre", (LINT, 2), (LINT, 1), (IMM, 30))
        exit_offset = 0x300
        s.emit("jcc", (LINT, 2), (IMM, exit_offset), (IMM, NONE_TARGET))
        s.emit("set-string", (GSTR, cc.CC_TITLE_OUT), s.text("Knight"))
        s.emit("mov", (GINT, cc.CC_TITLE_PRIORITY), (IMM, 30))
        s.emit("add", (GINT, cc.CC_STATS + 2), (GINT, cc.CC_STATS + 2), (IMM, 5))
        s.emit("sub", (GINT, cc.CC_RESIST + 4), (GINT, cc.CC_RESIST + 4), (IMM, 10))
        s.emit("add", (GINT, cc.CC_COST), (GINT, cc.CC_COST), (IMM, 2))
        s.emit("mov", (GINT, cc.CC_SKILL_SLOTS + 1), (IMM, 44))
        s.emit("mov", (GINT, cc.CC_SKILL_LEARN + 12), (IMM, 1))
        s.emit("eq", (LINT, 0), (GINT, cc.CC_UNIT), (IMM, 8))       # second rule
        s.emit("mov", (GINT, cc.CC_CAPS + 0), (IMM, 1))
        scr = s.done()
        scr.instructions.append(Instruction(exit_offset, OP["exit"], "exit", []))
        records, meta = cc.extract_class_change_rules(
            scr, unit_names={7: "Will"}, skill_names={12: "Slash"})
        self.assertEqual(2, len(records))
        rule = records[0]
        self.assertEqual((1, 7, "Will"), (rule["id"], rule["unit_id"], rule["unit_name"]))
        self.assertEqual(3, rule["state_column"])
        self.assertEqual(30, rule["minimum_level"])
        self.assertTrue(rule["event_gated"])
        self.assertEqual(("Knight", 30), (rule["title"], rule["title_priority"]))
        self.assertEqual({"physical_attack": 5}, rule["stat_deltas"])
        self.assertEqual({"fire": -10}, rule["resistance_deltas"])
        self.assertEqual(2, rule["party_cost_delta"])
        self.assertEqual({1: 44}, rule["skill_slot_replacements"])
        self.assertEqual([{"skill_id": 12, "skill_name": "Slash"}], rule["skills_learned"])
        self.assertEqual(8, records[1]["unit_id"])
        # an unrecognised mov in a rule block is reported, not silently dropped
        self.assertEqual(1, len(meta["unhandled_instructions"]))


class QuestTests(unittest.TestCase):
    def test_description_lines_rows_and_rewards(self) -> None:
        s = Script("QUINIT.BIN")
        base, stride = progression.QUEST_TEXT
        qid = 12
        s.emit("set-string", (GSTR, 0x625F + qid), s.text("Repair"))
        for line, text in ((0, "header"), (1, "o1"), (2, "o2"), (5, "clear"), (7, "fail")):
            s.emit("set-string", (GSTR, base + qid * stride + line), s.text(text))
        s.emit("mov", (GINT, 0x3BFB32 + qid * 2), (IMM, 4))         # reputation on success
        s.emit("mov", (GINT, 0x3C12A2 + qid * 4), (IMM, 101))       # reward item 0
        s.emit("mov", (GINT, 0x3C31E2 + qid * 4), (IMM, 2))         # its quantity
        s.emit("mov", (GINT, 0x3C12A2 + qid * 4 + 1), (IMM, 102))   # item without quantity
        s.emit("mov", (GINT, 0x3C0AD2 + 1000), (IMM, 500))          # a reward notice id
        s.emit("exit")
        records, meta = progression.extract_quests(s.done())
        self.assertEqual({"ints": [], "strings": []}, meta["unclaimed"])
        self.assertEqual([qid, 1000], [r["id"] for r in records])   # no per-line records
        quest, notice = records
        self.assertEqual("Repair", quest["name"])
        self.assertEqual({"header": "header", "overview": ["o1", "o2"],
                          "clear_condition": ["clear"], "failure_condition": ["fail"]},
                         quest["text"])
        self.assertEqual([4, 0], quest["reputation_rewards"])
        self.assertEqual([{"item_id": 101, "quantity": 2}], quest["reward_items"])
        self.assertEqual(("quest", "reward_notice"), (quest["kind"], notice["kind"]))
        self.assertEqual(500, notice["reward_money"])


class SpecializationTests(unittest.TestCase):
    def test_every_forced_table_has_its_extractor(self) -> None:
        spec = init_kamidori.specializations()
        self.assertEqual(
            {(mode, table) for table, mode in spec["forced_modes"].items()},
            set(spec["extractors"]),
        )
        for mode in spec["forced_modes"].values():
            self.assertIn(mode, {"name", "numeric", "mixed", "rules"})
        self.assertNotIn("EBINIT", spec["forced_modes"])


if __name__ == "__main__":
    unittest.main()
