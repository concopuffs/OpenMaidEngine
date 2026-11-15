#!/usr/bin/env python3
"""Tests for tools/opcode_flow.py: the C# reading it rests on, and the facts it derives from the port's handlers."""
from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import opcode_flow as F  # noqa: E402


class StripCodeTests(unittest.TestCase):
    def test_braces_and_names_in_literals_and_comments_do_not_count(self):
        code = F.strip_code('var s = $"{a} {{x}} }"; // RunFrame( {\n/* { */ char c = \'{\'; var v = @"a""{";')
        self.assertNotIn("RunFrame", code)
        self.assertEqual(code.count("{"), 0)
        self.assertEqual(code.count("}"), 0)

    def test_an_interpolation_hole_with_a_string_inside_ends_where_its_string_does(self):
        code = F.strip_code('x($"{(ok ? "a" : "b")} tail"); Next();')
        self.assertIn("Next()", code)
        self.assertNotIn("tail", code)


class MethodsTests(unittest.TestCase):
    def methods(self, source: str) -> dict:
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "VirtualMachine.Sample.cs"
            path.write_text(source, encoding="utf-8")
            return F.methods_of([path])

    def test_block_and_expression_bodies_with_their_return_types(self):
        found = self.methods('''
            private StepResult OpJmp(Instruction ins, int pc)
            {
                if (x) { return StepResult.Jump(1); }
                return StepResult.Next;
            }
            private bool Helper() => Other(1, $"{y}");
            void IInputAndWaits.PresentFrame(GfxState gfx) => PresentFrame();
            private int Field;
        ''')
        self.assertEqual(found["OpJmp"][0].returns, "StepResult")
        self.assertIn("StepResult.Jump(", found["OpJmp"][0].body)
        self.assertIn("StepResult.Next", found["OpJmp"][0].body)
        self.assertIn("Other(", found["Helper"][0].body)
        self.assertIn("PresentFrame", found)
        self.assertNotIn("Field", found)


class DerivedFactsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.derived, _ = F.derive()

    def facts(self, name: str) -> tuple[str, list[str]]:
        entry = self.derived[name]
        return entry["waits"], entry["leaves"]

    def test_control_flow(self):
        self.assertEqual(self.facts("jmp"), ("never", ["jumps"]))
        self.assertEqual(self.facts("jcc"), ("never", ["jumps"]))
        self.assertEqual(self.facts("exit-script"), ("never", ["ends"]))
        self.assertIn("calls", self.facts("call-script")[1])

    def test_waits_reach_the_wait_native_and_its_callbacks(self):
        waits, leaves = self.facts("wait-for-input")
        self.assertEqual(waits, "may")
        self.assertIn("calls", leaves)        # the ADV menu's callbacks run inside the wait
        self.assertEqual(self.facts("show-and-clear-diagnostic")[0], "always")
        self.assertEqual(self.facts("mov")[0], "never")

    def test_a_present_blocks_and_is_not_a_wait(self):
        # Spec §10: the port waits for the display at a present, and a script call passes it (ModCallWaits.NotWaits).
        self.assertTrue({"PresentFrame", "PresentObjectRange", "FrameYield"} <= F.not_waits())
        self.assertEqual(self.facts("present-frame")[0], "never")
        self.assertEqual(self.facts("present-gfx-object-range")[0], "never")
        self.assertEqual(self.facts("sleep")[0], "may")

    def test_the_save_native(self):
        self.assertIn("saves", self.facts("save-numbered-slot")[1])
        self.assertIn("loads", self.facts("load-numbered-slot-and-resume")[1])
        self.assertIn("restores", self.facts("continue-save-load-stack-restore")[1])

    def test_every_handler_is_derived(self):
        self.assertEqual(set(self.derived), set(F.handler_table()))


if __name__ == "__main__":
    unittest.main()
