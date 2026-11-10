#!/usr/bin/env python3
"""Content-free tests for native_scratch.py: a scratch copy of a synthetic install never writes into the install.

Run: py -3.11 -X utf8 tools/test_native_scratch.py
"""
from __future__ import annotations

import contextlib
import io
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import native_scratch


@contextlib.contextmanager
def synthetic_install():
    """A temporary install (SYS4INI.BIN, two archives, a loose script) with its scratch root and manifest beside it."""
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        install = root / "install"
        install.mkdir()
        (install / "SYS4INI.BIN").write_bytes(b"SYS4INI")
        (install / "APPEND01.ALF").write_bytes(b"pristine append archive")
        (install / "DATA1.ALF").write_bytes(b"pristine data archive")
        (install / "SC0000.BIN").write_bytes(b"pristine script")
        build_root = root / "build"
        with mock.patch.multiple(
            native_scratch,
            install_root=lambda: install,
            scratch_root=lambda: build_root / "native-scratch",
            manifest_path=lambda: build_root / "native-scratch-manifest.json",
        ):
            yield root, install, build_root / "native-scratch"


def quietly(function, *arguments):
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
        return function(*arguments)


class ScratchBuildTests(unittest.TestCase):
    def test_archives_are_hard_linked_and_other_files_copied(self) -> None:
        with synthetic_install() as (_, install, scratch):
            self.assertEqual(0, quietly(native_scratch.build, [], False))
            self.assertTrue(os.path.samefile(install / "DATA1.ALF", scratch / "DATA1.ALF"))
            self.assertFalse(os.path.samefile(install / "SC0000.BIN", scratch / "SC0000.BIN"))
            self.assertEqual(0, quietly(native_scratch.check))

    def test_overlay_named_like_a_linked_archive_leaves_the_install_unchanged(self) -> None:
        with synthetic_install() as (root, install, scratch):
            overlay = root / "export" / "APPEND01.ALF"
            overlay.parent.mkdir()
            overlay.write_bytes(b"generated mod archive")
            self.assertEqual(0, quietly(native_scratch.build, [overlay], False))
            self.assertEqual(b"pristine append archive", (install / "APPEND01.ALF").read_bytes())
            self.assertEqual(b"generated mod archive", (scratch / "APPEND01.ALF").read_bytes())
            self.assertFalse(os.path.samefile(install / "APPEND01.ALF", scratch / "APPEND01.ALF"))
            self.assertTrue(os.path.samefile(install / "DATA1.ALF", scratch / "DATA1.ALF"))
            self.assertEqual(0, quietly(native_scratch.check))

    def test_rebuild_with_replace_keeps_the_install(self) -> None:
        with synthetic_install() as (root, install, scratch):
            overlay = root / "APPEND01.ALF"
            overlay.write_bytes(b"first overlay")
            self.assertEqual(0, quietly(native_scratch.build, [overlay], False))
            self.assertEqual(1, quietly(native_scratch.build, [overlay], False))   # exists without --replace
            overlay.write_bytes(b"second overlay")
            self.assertEqual(0, quietly(native_scratch.build, [overlay], True))
            self.assertEqual(b"pristine append archive", (install / "APPEND01.ALF").read_bytes())
            self.assertEqual(b"second overlay", (scratch / "APPEND01.ALF").read_bytes())
            self.assertEqual(0, quietly(native_scratch.check))


if __name__ == "__main__":
    unittest.main()
