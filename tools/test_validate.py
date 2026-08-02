#!/usr/bin/env python3
"""Pure tests for the layered validation driver."""
from __future__ import annotations

import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import validate


class EnvironmentResolutionTests(unittest.TestCase):
    def test_explicit_godot_wins_over_environment_and_path(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            explicit = root / "explicit-godot"
            explicit.write_bytes(b"")
            with mock.patch.object(validate.shutil, "which", return_value=str(root / "path-godot")):
                resolved = validate.resolve_godot(
                    str(explicit), {"AGE_GODOT_CONSOLE": str(root / "env-godot")}
                )
            self.assertEqual(explicit.resolve(), resolved)

    def test_invalid_explicit_godot_does_not_fall_through(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with mock.patch.object(validate.shutil, "which", return_value=str(root / "path-godot")):
                with self.assertRaisesRegex(ValueError, "not found"):
                    validate.resolve_godot(str(root / "missing"), {})

    def test_path_godot_is_used_after_empty_configuration(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            found = Path(directory) / "godot4"
            found.write_bytes(b"")
            with mock.patch.object(
                validate.shutil,
                "which",
                side_effect=lambda name: str(found) if name == "godot4" else None,
            ):
                self.assertEqual(found.resolve(), validate.resolve_godot(None, {}))

    def test_game_root_precedence_and_sys4ini_validation(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            explicit = root / "explicit"
            environment = root / "environment"
            conventional = root / "conventional"
            for candidate in (explicit, environment, conventional):
                candidate.mkdir()
                (candidate / "SYS4INI.BIN").write_bytes(b"SYS4")
            resolved = validate.resolve_game_root(
                str(explicit), {"AGE_GAME_ROOT": str(environment)}, conventional
            )
            self.assertEqual(explicit.resolve(), resolved)

    def test_invalid_explicit_game_root_does_not_fall_through(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            conventional = root / "conventional"
            conventional.mkdir()
            (conventional / "SYS4INI.BIN").write_bytes(b"SYS4")
            with self.assertRaisesRegex(ValueError, "not found"):
                validate.resolve_game_root(str(root / "missing"), {}, conventional)


class GatePlanTests(unittest.TestCase):
    def test_core_contains_no_workspace_or_runtime_gates(self) -> None:
        keys = {gate.key for gate in validate.build_gate_plan("core")}
        self.assertIn("engine-tests", keys)
        self.assertIn("python-test_validate", keys)
        self.assertNotIn("sys4-corpus-validate", keys)
        self.assertNotIn("godot-selftest", keys)
        self.assertNotIn("age-cli-sweep", keys)

    def test_workspace_extends_core(self) -> None:
        core = {gate.key for gate in validate.build_gate_plan("core")}
        workspace = {gate.key for gate in validate.build_gate_plan("workspace")}
        self.assertLess(core, workspace)
        self.assertIn("sys4-corpus-validate", workspace)
        self.assertNotIn("godot-selftest", workspace)

    def test_runtime_requires_resolved_paths(self) -> None:
        with self.assertRaisesRegex(ValueError, "resolved Godot"):
            validate.build_gate_plan("runtime")

    def test_full_contains_every_phase(self) -> None:
        fake_godot = Path("godot")
        fake_root = Path("game")
        state_root = Path("state")
        plan = validate.build_gate_plan("full", fake_godot, fake_root, state_root)
        keys = {gate.key for gate in plan}
        self.assertIn("sys4-corpus-validate", keys)
        self.assertIn("godot-selftest", keys)
        self.assertIn("age-cli-sweep", keys)
        self.assertEqual("diff-check", plan[-1].key)
        selftest = next(gate for gate in plan if gate.key == "godot-selftest")
        self.assertIn(str(state_root / "godot.log"), selftest.command)
        self.assertIn(("APPDATA", str(state_root / "appdata")), selftest.environment)


if __name__ == "__main__":
    unittest.main()
