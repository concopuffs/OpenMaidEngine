#!/usr/bin/env python3
from __future__ import annotations

import re
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parent.parent
WORKFLOW = REPO / ".gitea/workflows/linux-release-build.yml"


def job(text: str, name: str, next_name: str | None) -> str:
    start = text.index(f"  {name}:\n")
    end = len(text) if next_name is None else text.index(f"  {next_name}:\n", start + 1)
    return text[start:end]


class ReleaseWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.text = WORKFLOW.read_text(encoding="utf-8")
        cls.linux = job(cls.text, "linux-release", "windows-release")
        cls.windows = job(cls.text, "windows-release", "publish-release")
        cls.publish = job(cls.text, "publish-release", None)

    def test_build_jobs_are_linux_hosted_read_only_and_target_separate(self) -> None:
        self.assertRegex(self.text, r"(?m)^permissions:\n  contents: read$")
        self.assertIn("runs-on: ubuntu-latest", self.linux)
        self.assertIn("run: ./tools/build-linux-x64.sh", self.linux)
        self.assertIn("runs-on: ubuntu-latest", self.windows)
        self.assertIn("run: ./tools/build-windows-x64.sh", self.windows)
        self.assertNotIn("releases: write", self.linux + self.windows)
        self.assertNotIn("secrets.", self.linux + self.windows)

    def test_windows_job_provisions_and_caches_only_its_cross_inputs(self) -> None:
        self.assertRegex(
            self.windows,
            r"gcc-mingw-w64-x86-64 binutils-mingw-w64-x86-64",
        )
        self.assertIn("binutils-mingw-w64-x86-64", self.windows)
        self.assertIn("ffmpeg-*-win64-lgpl-shared-*.zip", self.windows)
        self.assertIn("windows_release_x86_64.exe", self.windows)
        self.assertIn("dependency-win64.json", self.windows)
        self.assertNotIn("linux_release.x86_64", self.windows)
        self.assertNotIn("dependency-linux-x64.json", self.windows)
        self.assertNotRegex(self.windows.lower(), r"\bwine(?:32|64)?\b")
        self.assertNotIn("--package-smoke", self.windows)

    def test_windows_artifact_is_archive_plus_structural_evidence(self) -> None:
        for name in (
            "OpenMaidEngine-Himegari-windows-x64.zip",
            "OpenMaidEngine-Himegari-windows-x64.zip.sha256",
            "BUILD-INFO.json",
            "SHA256SUMS",
            "WINDOWS-VERIFICATION.json",
        ):
            self.assertIn(name, self.windows)
        self.assertIn("if-no-files-found: error", self.windows)
        self.assertIn("retention-days: 30", self.windows)

    def test_slice_three_keeps_linux_only_tag_promotion_boundary(self) -> None:
        self.assertIn("if: startsWith(gitea.ref, 'refs/tags/v')", self.publish)
        self.assertRegex(self.publish, r"(?m)^    needs: linux-release$")
        self.assertIn("releases: write", self.publish)
        self.assertNotIn("windows-release", self.publish)


if __name__ == "__main__":
    unittest.main()
