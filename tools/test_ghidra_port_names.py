#!/usr/bin/env python3
"""Pure tests for ghidra_port_names.py: the names it writes and which function pairs it trusts."""
from __future__ import annotations

import collections
import unittest

import ghidra_port_names as G


class NameTests(unittest.TestCase):
    def test_a_ported_name_drops_the_script_revision_suffix(self) -> None:
        self.assertEqual("op_0x9_reset_scene_and_reload_root", G.ported_name("op_0x9_reset_scene_and_reload_root_sys4433"))
        self.assertEqual("text_blit_glyph_bitmap", G.ported_name("text_blit_glyph_bitmap_SYS4433"))
        self.assertEqual("vm_operand_fetch", G.ported_name("vm_operand_fetch"))

    def test_a_handler_is_named_from_the_registry(self) -> None:
        self.assertEqual("op_0x20c_present_frame", G.handler_name(0x20C, "present-frame"))
        self.assertEqual("op_0x216_query_gfx_field", G.handler_name(0x216, "query-gfx-field?"))

    def test_a_placeholder_label_is_not_a_name(self) -> None:
        self.assertEqual("op_0x320_handler", G.handler_name(0x320, "u0043AA20"))   # another build's address
        self.assertEqual("op_0x32a_handler", G.handler_name(0x32A, "32a"))         # the opcode again
        self.assertEqual("op_0x2e1_handler", G.handler_name(0x2E1, None))

    def test_the_port_note_names_its_source_and_whose_addresses_the_text_has(self) -> None:
        note = G.port_note("/kamidori/4.33G/image", "adv_text_define_layout", 0x453CD0, 0.914, 7, 1, "4.33G")
        self.assertIn("/kamidori/4.33G/image adv_text_define_layout@0x453cd0", note)
        self.assertIn("similarity 0.91, votes 7:1", note)
        self.assertIn("are 4.33G's", note)


class PairTests(unittest.TestCase):
    @staticmethod
    def votes(**entries: dict[int, int]) -> dict[int, collections.Counter]:
        return {int(key[1:], 16): collections.Counter(value) for key, value in entries.items()}

    def test_a_pair_needs_twice_the_runner_ups_votes(self) -> None:
        pairs, rejected = G.clear_pairs(self.votes(
            f10={0x110: 4, 0x120: 2},      # 4 against 2: clear
            f20={0x210: 3, 0x220: 2},      # 3 against 2: not clear
            f30={0x310: 1},                # one vote and no rival: clear
            f40={0x410: 2, 0x420: 2},      # a tie
        ))
        self.assertEqual({0x10: (0x110, 4, 2), 0x30: (0x310, 1, 0)}, pairs)
        self.assertIn("not clear (3:2)", rejected[0x20])
        self.assertIn("not clear (2:2)", rejected[0x40])

    def test_a_partner_goes_to_the_function_with_more_votes(self) -> None:
        pairs, rejected = G.clear_pairs(self.votes(f10={0x900: 5}, f20={0x900: 2}, f30={0x800: 3}, f40={0x800: 3}))
        self.assertEqual({0x10: (0x900, 5, 0)}, pairs)
        self.assertEqual("partner 0x900 belongs to 0x10", rejected[0x20])
        self.assertEqual("shares partner 0x800 with equal votes", rejected[0x30])
        self.assertEqual("shares partner 0x800 with equal votes", rejected[0x40])

    def test_a_string_pairs_two_functions_only_when_each_is_its_only_owner(self) -> None:
        reference = {"SYS4INI.BIN": {0x10}, "shared": {0x10, 0x20}, "gone": {0x30}, "both": {0x40}, "also": {0x40}}
        other = {"SYS4INI.BIN": {0xA10}, "shared": {0xA10}, "both": {0xA40}, "also": {0xA40}, "twice": {0xA50, 0xA60}}
        self.assertEqual({(0x10, 0xA10): 1, (0x40, 0xA40): 2}, dict(G.string_pairs(reference, other)))


if __name__ == "__main__":
    unittest.main()
