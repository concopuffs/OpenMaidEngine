#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parent.parent
WORKFLOW = REPO / ".gitea/workflows/linux-release-build.yml"
EXPORT_PRESETS = REPO / "godot/export_presets.cfg"
GODOT_PROJECT = REPO / "godot/project.godot"
MANAGED_PROJECT = REPO / "godot/OME.csproj"
MANAGED_SOLUTION = REPO / "godot/OME.sln"
LINUX_BUILD = REPO / "tools/build-linux-x64.sh"
WINDOWS_BUILD = REPO / "tools/build-windows-x64.sh"
WINDOWS_HOSTED_EXPORT = REPO / "tools/export-linux-x64.ps1"
LINUX_FFMPEG_MANIFEST = REPO / "native/age_movie_ffmpeg/dependency-linux-x64.json"
WINDOWS_FFMPEG_MANIFEST = REPO / "native/age_movie_ffmpeg/dependency-win64.json"
LINUX_FFMPEG_BOOTSTRAP = REPO / "native/age_movie_ffmpeg/bootstrap-linux-x64.sh"
WINDOWS_FFMPEG_BOOTSTRAP = REPO / "native/age_movie_ffmpeg/bootstrap-win64.sh"


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
        cls.export_presets = EXPORT_PRESETS.read_text(encoding="utf-8")
        cls.godot_project = GODOT_PROJECT.read_text(encoding="utf-8")
        cls.managed_project = MANAGED_PROJECT.read_text(encoding="utf-8")
        cls.managed_solution = MANAGED_SOLUTION.read_text(encoding="utf-8")
        cls.linux_build = LINUX_BUILD.read_text(encoding="utf-8")
        cls.windows_build = WINDOWS_BUILD.read_text(encoding="utf-8")
        cls.windows_hosted_export = WINDOWS_HOSTED_EXPORT.read_text(encoding="utf-8")

    def test_ffmpeg_inputs_are_dated_size_and_hash_pinned(self) -> None:
        manifests = [
            json.loads(LINUX_FFMPEG_MANIFEST.read_text(encoding="utf-8")),
            json.loads(WINDOWS_FFMPEG_MANIFEST.read_text(encoding="utf-8")),
        ]
        self.assertEqual(1, len({manifest["release_tag"] for manifest in manifests}))
        for manifest in manifests:
            self.assertRegex(manifest["release_tag"], r"^autobuild-\d{4}-\d{2}-\d{2}-\d{2}-\d{2}$")
            self.assertNotEqual("latest", manifest["release_tag"])
            self.assertIn(f"/{manifest['release_tag']}/", manifest["url"])
            self.assertTrue(manifest["url"].endswith("/" + manifest["archive"]))
            self.assertIn(manifest["ffmpeg_commit"], manifest["archive"])
            self.assertGreater(manifest["size"], 50_000_000)
            self.assertRegex(manifest["sha256"], r"^[0-9a-f]{64}$")
        for bootstrap in (LINUX_FFMPEG_BOOTSTRAP, WINDOWS_FFMPEG_BOOTSTRAP):
            text = bootstrap.read_text(encoding="utf-8")
            self.assertIn("manifest_value size", text)
            self.assertIn("--remove-on-error", text)

    def test_public_build_outputs_use_ome_branding(self) -> None:
        self.assertIn('export_path="../build/export/linux-x64/OME"', self.export_presets)
        self.assertIn('export_path="../build/export/windows-x64/OME.exe"', self.export_presets)
        self.assertIn('project/assembly_name="OME"', self.godot_project)
        self.assertIn('LogicalName="OME.Runtime.opcodes.json"', self.managed_project)
        self.assertIn('= "OME", "OME.csproj"', self.managed_solution)
        self.assertIn("'OME.sln'", self.windows_hosted_export)
        self.assertFalse((REPO / "godot/Himegari.csproj").exists())
        self.assertFalse((REPO / "godot/Himegari.sln").exists())
        active_surfaces = "\n".join(
            (
                self.text,
                self.export_presets,
                self.godot_project,
                self.linux_build,
                self.windows_build,
                self.windows_hosted_export,
            )
        )
        for legacy_name in (
            "Himegari.exe",
            "Himegari.x86_64",
            "Himegari.dll",
            "Himegari.csproj",
            "Himegari.sln",
            "OpenMaidEngine-Himegari",
        ):
            self.assertNotIn(legacy_name, active_surfaces)

    def test_godot_csharp_uid_sidecars_match_sources(self) -> None:
        godot_directory = REPO / "godot"
        sources = {path.name for path in godot_directory.glob("*.cs")}
        sidecar_sources = {
            path.name.removesuffix(".uid")
            for path in godot_directory.glob("*.cs.uid")
        }
        self.assertEqual(sources, sidecar_sources)

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
            "OME-windows-x64.zip",
            "OME-windows-x64.zip.sha256",
            "BUILD-INFO.json",
            "SHA256SUMS",
            "WINDOWS-VERIFICATION.json",
        ):
            self.assertIn(name, self.windows)
        self.assertIn("if-no-files-found: error", self.windows)
        self.assertIn("retention-days: 30", self.windows)

    def test_tag_promotion_requires_and_downloads_both_platform_artifacts(self) -> None:
        self.assertIn("if: startsWith(gitea.ref, 'refs/tags/v')", self.publish)
        self.assertRegex(
            self.publish,
            r"(?m)^    needs:\n      - linux-release\n      - windows-release$",
        )
        self.assertIn("releases: write", self.publish)
        self.assertIn("OME-linux-x64-${{ gitea.sha }}", self.publish)
        self.assertIn("OME-windows-x64-${{ gitea.sha }}", self.publish)
        self.assertIn("path: build/release-assets/linux", self.publish)
        self.assertIn("path: build/release-assets/windows", self.publish)
        self.assertIn("--linux-artifact-directory build/release-assets/linux", self.publish)
        self.assertIn("--windows-artifact-directory build/release-assets/windows", self.publish)
        self.assertIn("--output-directory build/release-assets/prepared", self.publish)
        self.assertNotIn("--asset", self.publish)


if __name__ == "__main__":
    unittest.main()
