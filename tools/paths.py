"""Shared profile-aware path context for repository tools.

Every path is derived from this file. Tools may be invoked with the common options
``--profile``, ``--game-root``, and ``--extracted-root``; this module consumes those
options once and exposes one immutable :class:`ToolContext`. With no options or
environment variables, the historical Himegari workspace remains the default.

Engine-wide generated ABI artifacts live directly in ``build/``. Game-derived
catalogs, corpora, maps, traces, and reports live in ``build/games/<profile-id>/``.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Mapping, Sequence

REPO = Path(__file__).resolve().parent.parent
WORKSPACE = REPO.parent
VM_MAP = REPO / "vm-map"
BIN = REPO / "bin"
SHARED_BUILD = REPO / "build"
PROFILE_MANIFEST_DIR = REPO / "engine" / "Age.Engine" / "Profiles"

_CONVENTIONAL_GAME_ROOTS = {
    "himegari": WORKSPACE / "Himegari_Game",
    "kamidori": WORKSPACE / "Kamidori",
}
_CONVENTIONAL_EXTRACTED_ROOTS = {
    "himegari": WORKSPACE / "extracted",
    "kamidori": WORKSPACE / "extracted-kamidori",
}


def available_profiles() -> tuple[str, ...]:
    """Return authored profile ids from the same embedded manifests used by the runtime."""
    ids = []
    for manifest_path in sorted(PROFILE_MANIFEST_DIR.glob("*.json")):
        try:
            profile_id = json.loads(manifest_path.read_text(encoding="utf-8"))["id"]
        except (OSError, KeyError, TypeError, json.JSONDecodeError) as error:
            raise ValueError(f"invalid game profile manifest {manifest_path}: {error}") from error
        if not isinstance(profile_id, str) or not profile_id:
            raise ValueError(f"invalid game profile id in {manifest_path}")
        ids.append(profile_id)
    if not ids:
        raise ValueError(f"no game profile manifests found in {PROFILE_MANIFEST_DIR}")
    return tuple(ids)


@dataclass(frozen=True)
class ToolContext:
    """One resolved game/tooling identity. Construction does not require installed content."""

    profile_id: str
    game_root: Path
    extracted_root: Path
    shared_build: Path = SHARED_BUILD

    @property
    def game_build(self) -> Path:
        return self.shared_build / "games" / self.profile_id

    @property
    def data1(self) -> Path:
        return self.extracted_root / "DATA1"

    @property
    def sys4ini(self) -> Path:
        return self.game_root / "SYS4INI.BIN"

    @property
    def age_exe(self) -> Path:
        return self.game_root / "AGE.EXE"

    def subprocess_environment(self) -> dict[str, str]:
        """Environment that preserves this exact context in child Python tools."""
        return {
            "AGE_PROFILE": self.profile_id,
            "AGE_GAME_ROOT": str(self.game_root),
            "AGE_EXTRACTED_ROOT": str(self.extracted_root),
        }

    @classmethod
    def resolve(
        cls,
        profile_id: str | None = None,
        game_root: str | os.PathLike[str] | None = None,
        extracted_root: str | os.PathLike[str] | None = None,
        environ: Mapping[str, str] | None = None,
    ) -> "ToolContext":
        env = os.environ if environ is None else environ
        selected = profile_id or env.get("AGE_PROFILE") or "himegari"
        profiles = available_profiles()
        if selected not in profiles:
            raise ValueError(
                f"unknown profile '{selected}'; available profiles: {', '.join(profiles)}"
            )

        selected_game_root = (
            game_root
            or env.get("AGE_GAME_ROOT")
            or _CONVENTIONAL_GAME_ROOTS.get(selected)
            or WORKSPACE / selected
        )
        selected_extracted_root = (
            extracted_root
            or env.get("AGE_EXTRACTED_ROOT")
            or _CONVENTIONAL_EXTRACTED_ROOTS.get(selected)
            or WORKSPACE / f"extracted-{selected}"
        )
        return cls(
            selected,
            Path(selected_game_root).expanduser().resolve(strict=False),
            Path(selected_extracted_root).expanduser().resolve(strict=False),
        )


def _context_from_process(argv: Sequence[str]) -> tuple[ToolContext, list[str]]:
    """Consume common context flags while leaving every tool-specific argument untouched."""
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--profile")
    parser.add_argument("--game-root")
    parser.add_argument("--extracted-root")
    arguments, remaining = parser.parse_known_args(list(argv))
    try:
        context = ToolContext.resolve(
            arguments.profile, arguments.game_root, arguments.extracted_root
        )
    except ValueError as error:
        parser.error(str(error))
    return context, remaining


CONTEXT, _remaining_arguments = _context_from_process(sys.argv[1:])
sys.argv[1:] = _remaining_arguments

# Compatibility aliases. They are immutable projections of CONTEXT, not editable selection state.
GAME_DIR = CONTEXT.game_root
EXTRACTED = CONTEXT.extracted_root
DATA1 = CONTEXT.data1
GAME_BUILD = CONTEXT.game_build
BUILD = GAME_BUILD
AGE_EXE = CONTEXT.age_exe


def add_self_to_syspath() -> None:
    """Let a standalone script import sibling modules from tools/."""
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def scripts(context: ToolContext | None = None) -> dict[str, Path]:
    """Legacy extracted/loose script map; catalog-aware scanners use ``script_corpus``.

    Loose files shadow extracted DATA1 copies. This compatibility view cannot represent
    archive-only or append scripts, so new corpus tooling must not use it.
    """
    selected = context or CONTEXT
    files: dict[str, Path] = {}
    if selected.data1.is_dir():
        for path in sorted(selected.data1.glob("*.BIN")):
            files[path.name.upper()] = path
    if selected.game_root.is_dir():
        for path in sorted(selected.game_root.glob("*.BIN")):
            files[path.name.upper()] = path
    return files
