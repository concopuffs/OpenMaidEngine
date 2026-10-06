#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parent.parent
WORKFLOW = REPO / ".github/workflows/release-build.yml"
WORKFLOW_DIRECTORY = REPO / ".github/workflows"
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
WINDOWS_FFMPEG_BOOTSTRAP_PS1 = REPO / "native/age_movie_ffmpeg/bootstrap-win64.ps1"
FFMPEG_SOURCE_MANIFEST = REPO / "native/age_movie_ffmpeg/ffmpeg-source.json"


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

    def test_ffmpeg_inputs_are_the_pinned_minimal_lgpl_build(self) -> None:
        source = json.loads(FFMPEG_SOURCE_MANIFEST.read_text(encoding="utf-8"))
        manifests = {
            "linux-x64": json.loads(LINUX_FFMPEG_MANIFEST.read_text(encoding="utf-8")),
            "win64": json.loads(WINDOWS_FFMPEG_MANIFEST.read_text(encoding="utf-8")),
        }
        release = (
            "https://github.com/concopuffs/OpenMaidEngine/releases/download/"
            f"deps-ffmpeg-{source['mirror_version']}"
        )
        self.assertRegex(source["mirror_version"], r"^ome-\d+\.\d+\.\d+-mpeg1-r\d+$")
        self.assertEqual(f"ome-{source['version']}-mpeg1-r{source['sdk_revision']}", source["mirror_version"])
        self.assertEqual(f"https://ffmpeg.org/releases/{source['archive']}", source["url"])
        self.assertRegex(source["sha256"], r"^[0-9a-f]{64}$")
        self.assertEqual("LGPL-2.1-or-later", source["license"])
        flags = source["configure_flags"] + [
            flag for target_flags in source["target_configure_flags"].values() for flag in target_flags
        ]
        for required in ("--disable-everything", "--disable-autodetect", "--disable-programs", "--enable-shared"):
            self.assertIn(required, flags)
        for forbidden in ("--enable-gpl", "--enable-nonfree", "--enable-version3"):
            self.assertNotIn(forbidden, flags)
        for target, manifest in manifests.items():
            extension = "tar.xz" if target == "linux-x64" else "zip"
            archive = f"ome-ffmpeg-{source['mirror_version'].removeprefix('ome-')}-{target}-lgpl-shared.{extension}"
            self.assertEqual(source["mirror_version"], manifest["mirror_version"])
            self.assertEqual(archive, manifest["archive"])
            self.assertEqual(f"{release}/{archive}", manifest["url"])
            self.assertEqual(source["version"], manifest["ffmpeg_version"])
            self.assertEqual(source["license"], manifest["license"])
            self.assertEqual(source["archive"], manifest["source_archive"])
            self.assertEqual(f"{release}/{source['archive']}", manifest["source_url"])
            self.assertEqual(source["sha256"], manifest["source_sha256"])
            self.assertEqual(source["url"], manifest["upstream_url"])
            self.assertRegex(manifest["sha256"], r"^[0-9a-f]{64}$")
            # A floor catches a truncated upload; the ceiling catches an accidental full FFmpeg build.
            self.assertGreater(manifest["size"], 500_000)
            self.assertLess(manifest["size"], 10_000_000)
        self.assertEqual("2.28", manifests["linux-x64"]["minimum_glibc"])
        for bootstrap in (LINUX_FFMPEG_BOOTSTRAP, WINDOWS_FFMPEG_BOOTSTRAP, WINDOWS_FFMPEG_BOOTSTRAP_PS1):
            bootstrap_text = bootstrap.read_text(encoding="utf-8")
            self.assertIn("ffversion.h", bootstrap_text)
            self.assertNotIn("ffmpeg.exe", bootstrap_text)
            self.assertNotIn("bin/ffmpeg", bootstrap_text)
        for bootstrap in (LINUX_FFMPEG_BOOTSTRAP, WINDOWS_FFMPEG_BOOTSTRAP):
            bootstrap_text = bootstrap.read_text(encoding="utf-8")
            self.assertIn("manifest_value size", bootstrap_text)
            self.assertIn("--remove-on-error", bootstrap_text)

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
        self.assertIn("runs-on: ubuntu-24.04", self.linux)
        self.assertIn("run: ./tools/build-linux-x64.sh", self.linux)
        self.assertIn("runs-on: ubuntu-24.04", self.windows)
        self.assertIn("run: ./tools/build-windows-x64.sh", self.windows)
        self.assertNotIn("contents: write", self.linux + self.windows)
        self.assertNotIn("secrets.", self.text)
        self.assertNotIn("GITHUB_TOKEN", self.linux + self.windows)

    def test_windows_job_provisions_and_caches_only_its_cross_inputs(self) -> None:
        self.assertRegex(
            self.windows,
            r"gcc-mingw-w64-x86-64 binutils-mingw-w64-x86-64",
        )
        self.assertIn("binutils-mingw-w64-x86-64", self.windows)
        self.assertIn("ome-ffmpeg-*-win64-lgpl-shared.zip", self.windows)
        self.assertIn("ome-ffmpeg-*-linux-x64-lgpl-shared.tar.xz", self.linux)
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
        self.assertIn("if: startsWith(github.ref, 'refs/tags/v')", self.publish)
        self.assertRegex(
            self.publish,
            r"(?m)^    needs:\n      - linux-release\n      - windows-release$",
        )
        self.assertRegex(self.publish, r"(?m)^    permissions:\n(?:      #.*\n)*      contents: write$")
        self.assertIn("GITHUB_TOKEN: ${{ github.token }}", self.publish)
        self.assertIn("tools/publish_github_release.py", self.publish)
        self.assertIn('--tag "${{ github.ref_name }}"', self.publish)
        self.assertIn('--target "${{ github.sha }}"', self.publish)
        self.assertNotIn("--server", self.publish)
        self.assertIn("OME-linux-x64-${{ github.sha }}", self.publish)
        self.assertIn("OME-windows-x64-${{ github.sha }}", self.publish)
        self.assertIn("path: build/release-assets/linux", self.publish)
        self.assertIn("path: build/release-assets/windows", self.publish)
        self.assertIn("--linux-artifact-directory build/release-assets/linux", self.publish)
        self.assertIn("--windows-artifact-directory build/release-assets/windows", self.publish)
        self.assertIn("--output-directory build/release-assets/prepared", self.publish)
        self.assertNotIn("--asset", self.publish)

    def test_all_workflows_pin_actions_and_runner_image(self) -> None:
        workflows = sorted(WORKFLOW_DIRECTORY.glob("*.yml"))
        self.assertIn(WORKFLOW, workflows)
        self.assertFalse((REPO / ".gitea").exists())
        for workflow in workflows:
            text = workflow.read_text(encoding="utf-8")
            uses = re.findall(r"(?m)^\s*(?:- )?uses:\s*(\S+)(.*)$", text)
            self.assertTrue(uses, workflow.name)
            for action, comment in uses:
                self.assertRegex(action, r"^[\w.-]+/[\w.-]+@[0-9a-f]{40}$", workflow.name)
                self.assertRegex(comment, r"^ # v\d+\.\d+\.\d+$", f"{workflow.name}: {action}")
            runners = re.findall(r"(?m)^\s*runs-on:\s*(\S+)\s*$", text)
            self.assertTrue(runners, workflow.name)
            self.assertEqual({"ubuntu-24.04"}, set(runners), workflow.name)
            self.assertNotIn("pull_request_target", text)


if __name__ == "__main__":
    unittest.main()
