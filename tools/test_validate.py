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
                    str(explicit), {"OME_GODOT_CONSOLE": str(root / "env-godot")}
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

    def test_game_root_requires_a_directory_with_sys4ini(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with self.assertRaisesRegex(ValueError, "not found"):
                validate.resolve_game_root(root / "missing")
            with self.assertRaisesRegex(ValueError, "SYS4INI.BIN"):
                validate.resolve_game_root(root)
            (root / "SYS4INI.BIN").write_bytes(b"SYS4")
            self.assertEqual(root.resolve(), validate.resolve_game_root(root))

    def test_selected_game_root_is_the_contexts_not_ome_game_root(self) -> None:
        # paths.py consumed --game-root into the context; OME_GAME_ROOT naming another install must not win.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            selected, environment = root / "selected", root / "environment"
            for candidate in (selected, environment):
                candidate.mkdir()
                (candidate / "SYS4INI.BIN").write_bytes(b"SYS4")
            context = validate.paths.ToolContext.resolve(
                "himegari", selected, root / "extract", environ={}
            )
            with mock.patch.dict(os.environ, {"OME_GAME_ROOT": str(environment)}):
                godot, game_root, errors = validate.resolve_selected_paths("workspace", None, context)
            self.assertEqual([], errors)
            self.assertIsNone(godot)
            self.assertEqual(selected.resolve(), game_root)
            plan = validate.build_gate_plan("workspace", game_root=game_root, tool_context=context)
            scan = next(gate for gate in plan if gate.key == "catalog-corpus-scan")
            self.assertIn(str(selected.resolve()), scan.command)
            self.assertIn(("OME_GAME_ROOT", str(context.game_root)), scan.environment)

    def test_core_resolves_no_game_root(self) -> None:
        context = validate.paths.ToolContext.resolve(
            "himegari", Path("missing-game"), Path("missing-extract"), environ={}
        )
        self.assertEqual((None, None, []), validate.resolve_selected_paths("core", None, context))


class GatePlanTests(unittest.TestCase):
    def test_core_contains_no_workspace_or_runtime_gates(self) -> None:
        keys = {gate.key for gate in validate.build_gate_plan("core")}
        self.assertIn("engine-restore", keys)
        self.assertIn("engine-cli-build", keys)
        self.assertIn("engine-tests", keys)
        self.assertIn("python-test_validate", keys)
        self.assertIn("python-test_sys4load", keys)
        self.assertNotIn("sys4-corpus-validate", keys)
        self.assertNotIn("godot-selftest", keys)
        self.assertNotIn("ome-cli-sweep", keys)
        engine = next(gate for gate in validate.build_gate_plan("core") if gate.key == "engine-tests")
        self.assertIn("Category!=Workspace", engine.command)
        self.assertIn("--no-restore", engine.command)
        self.assertIn("-m:1", engine.command)
        self.assertIn("-p:BuildInParallel=false", engine.command)
        self.assertEqual(
            validate.LOW_MEMORY_DOTNET_ENVIRONMENT,
            engine.environment[:len(validate.LOW_MEMORY_DOTNET_ENVIRONMENT)],
        )
        self.assertIn(
            "engine/Ome.Engine.Tests/Ome.Engine.Tests.csproj",
            engine.command,
        )
        cli = next(
            gate for gate in validate.build_gate_plan("core") if gate.key == "engine-cli-build"
        )
        self.assertIn("engine/Ome.Cli/Ome.Cli.csproj", cli.command)
        self.assertIn("--no-restore", cli.command)
        self.assertIn("-m:1", cli.command)
        self.assertEqual(
            validate.LOW_MEMORY_DOTNET_ENVIRONMENT,
            cli.environment[:len(validate.LOW_MEMORY_DOTNET_ENVIRONMENT)],
        )
        restore = next(
            gate for gate in validate.build_gate_plan("core") if gate.key == "engine-restore"
        )
        self.assertIn("--disable-parallel", restore.command)
        self.assertEqual(
            validate.LOW_MEMORY_DOTNET_ENVIRONMENT,
            restore.environment[:len(validate.LOW_MEMORY_DOTNET_ENVIRONMENT)],
        )

    def test_core_lints_every_profiles_registries_and_checks_generated_references(self) -> None:
        plan = validate.build_gate_plan("core")
        by_key = {gate.key: gate for gate in plan}
        for profile_id in validate.paths.available_profiles():
            registry = validate.paths.ToolContext.resolve(profile_id, environ={}).globals_toml
            if registry.is_file():
                lint = by_key[f"globals-lint-{profile_id}"]
                self.assertEqual(("--profile", profile_id, "--lint"), lint.command[-3:])
            if (validate.paths.VM_MAP / f"patch-points-{profile_id}.toml").is_file():
                points = by_key[f"patch-points-reference-{profile_id}"]
                self.assertEqual(("--profile", profile_id, "--check-reference"), points.command[-3:])
        self.assertIn("globals-lint-kamidori", by_key)          # vm-map/globals-kamidori.toml
        self.assertIn("patch-points-reference-himegari", by_key)
        self.assertEqual("--check", by_key["engine-ctx-check"].command[-1])
        self.assertNotIn("globals-check", by_key)                # merges the workspace auto map

    def test_workspace_checks_the_selected_global_reference_before_rebuilding_it(self) -> None:
        keys = [gate.key for gate in validate.build_gate_plan("workspace")]
        self.assertLess(keys.index("globals-check"), keys.index("globals-build"))
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            context = validate.paths.ToolContext.resolve(
                "kamidori", root / "game", root / "extract", environ={}
            )
            plan = validate.build_gate_plan("workspace", game_root=context.game_root, tool_context=context)
            check = next(gate for gate in plan if gate.key == "globals-check")
            self.assertIn(("OME_PROFILE", "kamidori"), check.environment)
            errors = validate.validate_prerequisites("workspace", context)
            self.assertTrue(any(str(context.game_build / "global-var-map.json") in error for error in errors)
                            or (context.game_build / "global-var-map.json").is_file())

    def test_workspace_extends_core(self) -> None:
        core = {gate.key for gate in validate.build_gate_plan("core")}
        workspace = {gate.key for gate in validate.build_gate_plan("workspace")}
        self.assertLess(core, workspace)
        self.assertIn("catalog-corpus-scan", workspace)
        self.assertIn("engine-workspace-tests", workspace)
        self.assertIn("oms-idiomatic-rebuild", workspace)   # Himegari has source modules in oms/himegari
        self.assertNotIn("godot-selftest", workspace)
        installed = next(
            gate for gate in validate.build_gate_plan("workspace")
            if gate.key == "engine-workspace-tests"
        )
        self.assertIn("Category=Workspace&Profile!=kamidori", installed.command)
        self.assertIn("--no-restore", installed.command)
        self.assertIn("-m:1", installed.command)
        self.assertEqual(
            validate.LOW_MEMORY_DOTNET_ENVIRONMENT,
            installed.environment[:len(validate.LOW_MEMORY_DOTNET_ENVIRONMENT)],
        )

    def test_kamidori_workspace_uses_only_selected_profile_gates_and_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            context = validate.paths.ToolContext.resolve(
                "kamidori", root / "game", root / "extract", environ={}
            )
            plan = validate.build_gate_plan(
                "workspace", game_root=context.game_root, tool_context=context
            )
        keys = {gate.key for gate in plan}
        self.assertIn("catalog-corpus-scan", keys)
        self.assertIn("oms-idiomatic-rebuild", keys)   # oms/kamidori/adv.oms
        self.assertNotIn("globals-build", keys)
        self.assertNotIn("python-test_extract_init", keys)
        self.assertNotIn("vm0-recover", keys)
        installed = next(gate for gate in plan if gate.key == "engine-workspace-tests")
        self.assertIn("Category=Workspace&Profile=kamidori", installed.command)
        scan = next(gate for gate in plan if gate.key == "catalog-corpus-scan")
        self.assertIn(str(context.game_build / "catalog-opcode-coverage.json"), scan.command)
        self.assertNotIn("himegari", str(context.game_build))

    def test_runtime_requires_resolved_paths(self) -> None:
        with self.assertRaisesRegex(ValueError, "resolved Godot"):
            validate.build_gate_plan("runtime")

    def test_full_contains_every_phase(self) -> None:
        fake_godot = Path("godot")
        fake_root = Path("game")
        state_root = Path("state")
        plan = validate.build_gate_plan("full", fake_godot, fake_root, state_root)
        keys = {gate.key for gate in plan}
        self.assertIn("catalog-corpus-scan", keys)
        self.assertIn("godot-selftest", keys)
        self.assertIn("ome-cli-sweep", keys)
        self.assertEqual("diff-check", plan[-1].key)
        selftest = next(gate for gate in plan if gate.key == "godot-selftest")
        self.assertIn(str(state_root / "godot.log"), selftest.command)
        self.assertIn(("APPDATA", str(state_root / "appdata")), selftest.environment)

    def test_kamidori_full_uses_natural_boot_acceptance_not_himegari_direct_sweep(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            context = validate.paths.ToolContext.resolve(
                "kamidori", root / "game", root / "extract", environ={}
            )
            plan = validate.build_gate_plan(
                "full", Path("godot"), context.game_root, root / "state", context
            )

        keys = {gate.key for gate in plan}
        self.assertIn("natural-boot-acceptance", keys)
        self.assertNotIn("ome-cli-sweep", keys)
        acceptance = next(gate for gate in plan if gate.key == "natural-boot-acceptance")
        self.assertIn(
            "FullyQualifiedName=NaturalBootIntegrationTests."
            "KamidoriSystem4Root_NewGameSelectionNaturallyCallsFirstAdvScene",
            acceptance.command,
        )


if __name__ == "__main__":
    unittest.main()
