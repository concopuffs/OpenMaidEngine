#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import io
import json
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

import install_godot_templates


class RangeReaderTests(unittest.TestCase):
    def test_seek_and_windowed_reads(self) -> None:
        source = bytes(range(251)) * 100
        requests: list[tuple[int, int]] = []

        def fetch(start: int, end: int) -> bytes:
            requests.append((start, end))
            return source[start:end + 1]

        reader = install_godot_templates.RangeReader(len(source), fetch, window_size=64)
        reader.seek(103)
        self.assertEqual(source[103:113], reader.read(10))
        self.assertEqual(source[113:123], reader.read(10))
        reader.seek(-8, io.SEEK_END)
        self.assertEqual(source[-8:], reader.read())
        self.assertEqual(2, len(requests))

    def test_installs_only_selected_member_and_reuses_verified_file(self) -> None:
        release = b"linux release template\0" * 200
        unused = b"other platform" * 100
        archive_buffer = io.BytesIO()
        with zipfile.ZipFile(archive_buffer, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("templates/linux_release.x86_64", release)
            archive.writestr("templates/windows_release_x86_64.exe", unused)
        archive_bytes = archive_buffer.getvalue()

        def reader() -> install_godot_templates.RangeReader:
            return install_godot_templates.RangeReader(
                len(archive_bytes),
                lambda start, end: archive_bytes[start:end + 1],
                window_size=128,
            )

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            manifest = root / "manifest.json"
            manifest.write_text(json.dumps({
                "templates": {
                    "url": "https://invalid.example/templates.tpz",
                    "size": len(archive_bytes),
                    "members": [{
                        "archive_path": "templates/linux_release.x86_64",
                        "install_name": "linux_release.x86_64",
                        "sha256": hashlib.sha256(release).hexdigest(),
                        "size": len(release),
                        "mode": "0755",
                    }],
                }
            }), encoding="utf-8")
            destination = root / "templates"
            with patch.object(install_godot_templates, "open_http_range_reader", return_value=reader()) as opened:
                installed = install_godot_templates.install_members(manifest, destination)
                opened.assert_called_once()
            self.assertEqual(release, installed[0].read_bytes())
            self.assertFalse((destination / "windows_release_x86_64.exe").exists())

            with patch.object(install_godot_templates, "open_http_range_reader") as opened:
                reused = install_godot_templates.install_members(manifest, destination)
                opened.assert_not_called()
            self.assertEqual(installed, reused)


if __name__ == "__main__":
    unittest.main()
