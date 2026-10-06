#!/usr/bin/env python3
"""Regression tests for native/age_movie_ffmpeg/write_ffmpeg_source_notice.py.

Run: py -3.11 -X utf8 tools/test_write_ffmpeg_source_notice.py
"""
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
HELPER = REPO / "native/age_movie_ffmpeg/write_ffmpeg_source_notice.py"
SPEC = importlib.util.spec_from_file_location("write_ffmpeg_source_notice", HELPER)
assert SPEC is not None and SPEC.loader is not None
notice = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(notice)


class WriteFfmpegSourceNoticeTests(unittest.TestCase):
    def test_real_manifests_produce_a_complete_source_offer(self) -> None:
        for name in ("dependency-linux-x64.json", "dependency-win64.json"):
            manifest = json.loads((REPO / "native/age_movie_ffmpeg" / name).read_text(encoding="utf-8"))
            text = notice.render(manifest, "Configure line:\n  ./configure --disable-everything\n")
            self.assertIn(f"FFmpeg (https://ffmpeg.org/) version {manifest['ffmpeg_version']}", text)
            self.assertIn("Lesser General Public License version 2.1 or later", text)
            self.assertIn("FFmpeg-LICENSE.txt", text)
            self.assertIn(manifest["source_url"], text)
            self.assertIn(manifest["source_sha256"], text)
            self.assertIn(manifest["upstream_url"], text)
            self.assertIn("unmodified", text)
            self.assertIn("./configure --disable-everything", text)
            self.assertNotIn("FFMPEG", text.replace("FFMPEG_VERSION", ""))
            self.assertNotIn("Ffmpeg", text)

    def test_rejects_missing_fields_and_other_licenses(self) -> None:
        manifest = json.loads((REPO / "native/age_movie_ffmpeg/dependency-win64.json").read_text(encoding="utf-8"))
        without_source = dict(manifest)
        del without_source["source_url"]
        with self.assertRaisesRegex(ValueError, "source_url"):
            notice.render(without_source, "config")
        with self.assertRaisesRegex(ValueError, "unexpected FFmpeg license"):
            notice.render(dict(manifest, license="LGPL-3.0-or-later"), "config")

    def test_cli_requires_the_sdk_build_config(self) -> None:
        manifest_path = REPO / "native/age_movie_ffmpeg/dependency-win64.json"
        with tempfile.TemporaryDirectory() as temporary:
            sdk = Path(temporary) / "sdk"
            sdk.mkdir()
            output = Path(temporary) / "FFmpeg-SOURCE.txt"
            self.assertEqual(1, notice.main([str(manifest_path), str(sdk), str(output)]))
            self.assertFalse(output.exists())
            (sdk / "BUILD-CONFIG.txt").write_text("Configure line:\n  ./configure\n", encoding="utf-8")
            self.assertEqual(0, notice.main([str(manifest_path), str(sdk), str(output)]))
            self.assertIn("./configure", output.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
