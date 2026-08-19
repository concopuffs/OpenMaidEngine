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
                    "AGE_PROFILE": "himegari",
                    "AGE_GAME_ROOT": str(root / "wrong-game"),
                    "AGE_EXTRACTED_ROOT": str(root / "wrong-extract"),
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


if __name__ == "__main__":
    unittest.main()
