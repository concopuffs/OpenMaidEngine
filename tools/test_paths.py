#!/usr/bin/env python3
"""Pure tests for the profile-aware tool path context."""
from __future__ import annotations

import os
import tempfile
import unittest
from pathlib import Path

import paths


class ToolContextTests(unittest.TestCase):
    def test_himegari_defaults_preserve_conventional_workspace(self) -> None:
        context = paths.ToolContext.resolve(environ={})
        self.assertEqual("himegari", context.profile_id)
        self.assertEqual(paths.WORKSPACE / "Himegari_Game", context.game_root)
        self.assertEqual(paths.WORKSPACE / "extracted", context.extracted_root)
        self.assertEqual(paths.REPO / "build/games/himegari", context.game_build)

    def test_explicit_profile_and_roots_win_over_environment(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            context = paths.ToolContext.resolve(
                "kamidori",
                root / "game",
                root / "extract",
                {
                    "OME_PROFILE": "himegari",
                    "OME_GAME_ROOT": str(root / "wrong-game"),
                    "OME_EXTRACTED_ROOT": str(root / "wrong-extract"),
                },
            )
            self.assertEqual("kamidori", context.profile_id)
            self.assertEqual((root / "game").resolve(), context.game_root)
            self.assertEqual((root / "extract").resolve(), context.extracted_root)
            self.assertEqual(paths.REPO / "build/games/kamidori", context.game_build)

    def test_unknown_profile_is_rejected_before_path_use(self) -> None:
        with self.assertRaisesRegex(ValueError, "unknown profile"):
            paths.ToolContext.resolve("not-a-game", environ={})

    def test_child_environment_round_trips_identity(self) -> None:
        context = paths.ToolContext.resolve("kamidori", environ={})
        restored = paths.ToolContext.resolve(environ=context.subprocess_environment())
        self.assertEqual(context, restored)

    def test_engine_and_game_outputs_cannot_collide(self) -> None:
        himegari = paths.ToolContext.resolve("himegari", environ={})
        kamidori = paths.ToolContext.resolve("kamidori", environ={})
        self.assertEqual(himegari.shared_build, kamidori.shared_build)
        self.assertNotEqual(himegari.game_build, kamidori.game_build)
        self.assertTrue(himegari.game_build.is_relative_to(himegari.shared_build / "games"))
        self.assertTrue(kamidori.game_build.is_relative_to(kamidori.shared_build / "games"))

    def test_global_registries_are_per_game(self) -> None:
        himegari = paths.ToolContext.resolve("himegari", environ={})
        kamidori = paths.ToolContext.resolve("kamidori", environ={})
        self.assertEqual(paths.VM_MAP / "globals.toml", himegari.globals_toml)
        self.assertEqual(paths.REPO / "docs/global-reference.md", himegari.global_reference_doc)
        self.assertEqual(paths.VM_MAP / "globals-kamidori.toml", kamidori.globals_toml)
        self.assertEqual(
            paths.REPO / "docs/global-reference-kamidori.md", kamidori.global_reference_doc
        )

    def _script_layout(self, root: Path) -> None:
        (root / "game").mkdir()
        (root / "extract" / "DATA1").mkdir(parents=True)
        (root / "extract" / "APPEND01").mkdir()
        (root / "extract" / "DATA1" / "EBINIT.BIN").write_bytes(b"archive")
        (root / "extract" / "DATA1" / "ITINIT.BIN").write_bytes(b"archive")
        (root / "extract" / "APPEND01" / "$1$EBINIT.BIN").write_bytes(b"append")
        (root / "game" / "ITINIT.BIN").write_bytes(b"official update")
        (root / "game" / "$1$EBINIT.bin").write_bytes(b"loose append")

    def test_scripts_include_append_catalogs_with_loose_shadowing(self) -> None:
        for profile in ("himegari", "kamidori"):
            with self.subTest(profile=profile), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                self._script_layout(root)
                context = paths.ToolContext.resolve(profile, root / "game", root / "extract", {})
                scripts = paths.scripts(context)
                self.assertEqual(root.resolve() / "extract/DATA1/EBINIT.BIN", scripts["EBINIT.BIN"])
                self.assertEqual(root.resolve() / "game/ITINIT.BIN", scripts["ITINIT.BIN"])
                # Root-loose `$n$` files shadow their append records, as native AGE mounts them.
                self.assertEqual(root.resolve() / "game/$1$EBINIT.bin", scripts["$1$EBINIT.BIN"])


if __name__ == "__main__":
    unittest.main()
