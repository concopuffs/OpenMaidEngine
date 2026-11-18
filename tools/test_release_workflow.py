#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

import package_linux_x64


REPO = Path(__file__).resolve().parent.parent
WORKFLOW = REPO / ".github/workflows/release-build.yml"
FFMPEG_WORKFLOW = REPO / ".github/workflows/ffmpeg-sdk.yml"
WORKFLOW_DIRECTORY = REPO / ".github/workflows"
EXPORT_PRESETS = REPO / "godot/export_presets.cfg"
GODOT_PROJECT = REPO / "godot/project.godot"
MANAGED_PROJECT = REPO / "godot/OME.csproj"
# The opcode metadata every linker reads is embedded in Ome.Engine (launcher spec §5.9).
ENGINE_PROJECT = REPO / "engine/Ome.Engine/Ome.Engine.csproj"
MANAGED_SOLUTION = REPO / "godot/OME.sln"
LINUX_BUILD = REPO / "tools/build-linux-x64.sh"
WINDOWS_BUILD = REPO / "tools/build-windows-x64.sh"
LINUX_FFMPEG_MANIFEST = REPO / "native/ome_movie_ffmpeg/dependency-linux-x64.json"
WINDOWS_FFMPEG_MANIFEST = REPO / "native/ome_movie_ffmpeg/dependency-win64.json"
LINUX_FFMPEG_BOOTSTRAP = REPO / "native/ome_movie_ffmpeg/bootstrap-linux-x64.sh"
WINDOWS_FFMPEG_BOOTSTRAP = REPO / "native/ome_movie_ffmpeg/bootstrap-win64.sh"
WINDOWS_FFMPEG_BOOTSTRAP_PS1 = REPO / "native/ome_movie_ffmpeg/bootstrap-win64.ps1"
FFMPEG_SOURCE_MANIFEST = REPO / "native/ome_movie_ffmpeg/ffmpeg-source.json"


def run_scripts(text: str) -> list[str]:
    """Every step's `run:` script: an inline value, or a block scalar's lines indented past the key."""
    scripts = []
    lines = text.splitlines()
    for index, line in enumerate(lines):
        match = re.match(r"^(\s*)(?:- )?run:\s*(.*)$", line)
        if not match:
            continue
        indent, value = len(match.group(1)), match.group(2)
        if value not in ("|", ">", "|-", ">-"):
            scripts.append(value)
            continue
        block = []
        for following in lines[index + 1:]:
            if following.strip() and len(following) - len(following.lstrip()) <= indent:
                break
            block.append(following)
        scripts.append("\n".join(block))
    return scripts


def job(text: str, name: str, next_name: str | None) -> str:
    start = text.index(f"  {name}:\n")
    end = len(text) if next_name is None else text.index(f"  {next_name}:\n", start + 1)
    return text[start:end]


class ReleaseWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.text = WORKFLOW.read_text(encoding="utf-8")
        cls.linux_engine = job(cls.text, "linux-engine", "windows-engine")
        cls.windows_engine = job(cls.text, "windows-engine", "linux-release")
        cls.linux = job(cls.text, "linux-release", "windows-release")
        cls.windows = job(cls.text, "windows-release", "publish-release")
        cls.publish = job(cls.text, "publish-release", None)
        cls.export_presets = EXPORT_PRESETS.read_text(encoding="utf-8")
        cls.godot_project = GODOT_PROJECT.read_text(encoding="utf-8")
        cls.managed_project = MANAGED_PROJECT.read_text(encoding="utf-8")
        cls.engine_project = ENGINE_PROJECT.read_text(encoding="utf-8")
        cls.managed_solution = MANAGED_SOLUTION.read_text(encoding="utf-8")
        cls.linux_build = LINUX_BUILD.read_text(encoding="utf-8")
        cls.windows_build = WINDOWS_BUILD.read_text(encoding="utf-8")

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

    def test_an_exported_engine_flushes_its_output(self) -> None:
        # Launcher plan §4.12: Godot flushes standard output after a print only in debug builds unless the project
        # says so; without it a Linux release engine's lines reach a pipe late, and a killed engine loses them.
        application = self.godot_project.split("[application]", 1)[1].split("\n[", 1)[0]
        self.assertIn("run/flush_stdout_on_print=true", application.splitlines())
        self.assertIn('package_linux_x64.py" smoke "$package_root"', self.linux_build)

    def test_the_linux_build_requires_every_smoke_marker(self) -> None:
        # Launcher plan §9.4: the build checks the smoke log for the lines the publisher later requires.
        loop = re.search(r'^for marker in (.+); do$', self.linux_build, re.MULTILINE)
        self.assertIsNotNone(loop)
        self.assertEqual(list(package_linux_x64.SMOKE_MARKERS), re.findall(r'"([^"]+)"', loop.group(1)))

    def test_the_launcher_is_published_after_the_export_never_inside_it(self) -> None:
        # Launcher plan §3.7, §9.2: the launcher's publish fits the runner on its own, not beside Godot's export, so
        # each build runs it once the export has exited and its payload is staged, and before the payload is verified.
        for script, runtime, verify in (
            (self.linux_build, "linux-x64", 'package_linux_x64.py" verify'),
            (self.windows_build, "win-x64", 'package_windows_x64.py" verify'),
        ):
            with self.subTest(runtime):
                self.assertEqual(1, script.count("publish_launcher.py"))
                publish = script.index(f'publish_launcher.py" --runtime {runtime} --into "$export_directory"')
                self.assertLess(script.index("export_status=$?"), publish)
                self.assertLess(script.index('cp -a -- "$engine_publish_directory/." "$managed_export_directory/"'),
                                publish)
                self.assertLess(publish, script.index(verify))

    def test_the_engine_library_is_compiled_on_its_own_system(self) -> None:
        # NativeAOT compiles for its own system only (docs/platform-portability.md, "Fewer files in a package"): each
        # library has a job on its system, and the package job takes the library that job uploaded.
        for engine, package, script, runtime in (
            (self.linux_engine, self.linux, self.linux_build, "linux-x64"),
            (self.windows_engine, self.windows, self.windows_build, "win-x64"),
        ):
            with self.subTest(runtime):
                self.assertIn(f"tools/publish_engine.py --runtime {runtime}\n", engine)
                self.assertIn(f"name: engine-{runtime}-${{{{ github.sha }}}}", engine)
                self.assertIn("path: build/engine-publish/\n", engine)
                self.assertNotIn("contents: write", engine)
                self.assertRegex(package, rf"(?m)^    needs: {re.escape(engine.split(':', 1)[0].strip())}$")
                download = package.index(f"name: engine-{runtime}-${{{{ github.sha }}}}")
                self.assertIn("path: build/engine-publish\n", package[download:])
                self.assertLess(download, package.index("run: ./tools/build-"))
                # The build takes only a library built from its own commit, before Godot's export starts.
                check = script.index(f'publish_engine.py" --runtime {runtime} --check')
                self.assertLess(check, script.index("--export-release"))
                self.assertNotIn("dotnet publish", script)
        # Linked on the package's glibc floor, in the image the FFmpeg SDK is built in.
        image = re.search(r"(?m)^      image: (\S+)$", self.linux_engine)
        self.assertIsNotNone(image)
        self.assertIn(f"image: {image.group(1)}\n", FFMPEG_WORKFLOW.read_text(encoding="utf-8"))
        self.assertIn("manylinux_2_28_x86_64", image.group(1))
        # The container's root user does not own the runner's checkout: without its trust, git cannot name the commit
        # the publish is stamped with.
        trust = self.linux_engine.index('git config --global --add safe.directory "$GITHUB_WORKSPACE"')
        self.assertLess(self.linux_engine.index("uses: actions/checkout@"), trust)
        self.assertLess(trust, self.linux_engine.index("tools/publish_engine.py --runtime linux-x64"))
        self.assertIn('OME_PUBLISH_ASSEMBLY="OME.so"', self.linux_build)
        self.assertIn('OME_PUBLISH_ASSEMBLY="OME.dll"', self.windows_build)
        # Checked out as stored, so the Windows library embeds the bytes the Linux one does.
        self.assertLess(self.windows_engine.index("git config --global core.autocrlf false"),
                        self.windows_engine.index("uses: actions/checkout@"))

    def test_public_build_outputs_use_ome_branding(self) -> None:
        self.assertIn('export_path="../build/export/linux-x64/OME"', self.export_presets)
        self.assertIn('export_path="../build/export/windows-x64/OME.exe"', self.export_presets)
        # OME.exe carries the OME icon: Godot draws the SVG into the program's icon at export.
        self.assertIn('application/icon="res://assets/ome-icon.svg"', self.export_presets)
        # Every export's window starts with it: the engine loads the project icon before any script runs.
        self.assertIn('config/icon="res://assets/ome-icon.svg"', self.godot_project)
        self.assertIn('project/assembly_name="OME"', self.godot_project)
        self.assertIn('LogicalName="Ome.Engine.Runtime.opcodes.json"', self.engine_project)
        self.assertNotIn("opcodes.json", self.managed_project)
        self.assertIn('= "OME", "OME.csproj"', self.managed_solution)
        self.assertFalse((REPO / "godot/Himegari.csproj").exists())
        self.assertFalse((REPO / "godot/Himegari.sln").exists())
        active_surfaces = "\n".join(
            (
                self.text,
                self.export_presets,
                self.godot_project,
                self.linux_build,
                self.windows_build,
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
        self.assertNotIn("GITHUB_TOKEN", self.linux + self.windows + self.linux_engine + self.windows_engine)
        # Only the Windows engine library needs a Windows runner: NativeAOT links it with the MSVC tools there.
        self.assertIn("runs-on: windows-2025", self.windows_engine)
        self.assertIn("runs-on: ubuntu-24.04", self.linux_engine)

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
        self.assertIn("RELEASE_TAG: ${{ github.ref_name }}", self.publish)
        self.assertIn("RELEASE_TARGET: ${{ github.sha }}", self.publish)
        self.assertIn('--tag "$RELEASE_TAG"', self.publish)
        self.assertIn('--target "$RELEASE_TARGET"', self.publish)
        self.assertNotIn("--server", self.publish)
        self.assertIn("OME-linux-x64-${{ github.sha }}", self.publish)
        self.assertIn("OME-windows-x64-${{ github.sha }}", self.publish)
        self.assertIn("path: build/release-assets/linux", self.publish)
        self.assertIn("path: build/release-assets/windows", self.publish)
        self.assertIn("--linux-artifact-directory build/release-assets/linux", self.publish)
        self.assertIn("--windows-artifact-directory build/release-assets/windows", self.publish)
        self.assertIn("--output-directory build/release-assets/prepared", self.publish)
        self.assertNotIn("--asset", self.publish)

    def test_no_run_script_interpolates_an_expression(self) -> None:
        # An expression in run: is spliced into the shell text before it runs; values go through env: instead.
        workflows = sorted(WORKFLOW_DIRECTORY.glob("*.yml"))
        for workflow in workflows:
            scripts = run_scripts(workflow.read_text(encoding="utf-8"))
            self.assertTrue(scripts, workflow.name)
            for script in scripts:
                self.assertNotIn("${{", script, workflow.name)
        self.assertTrue(any("$RELEASE_TAG" in script for script in run_scripts(self.text)))

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
            # The Windows engine library's job is the one Windows runner (test_the_engine_library_is_compiled_on...).
            expected = {"ubuntu-24.04", "windows-2025"} if workflow == WORKFLOW else {"ubuntu-24.04"}
            self.assertEqual(expected, set(runners), workflow.name)
            self.assertEqual(1 if workflow == WORKFLOW else 0, runners.count("windows-2025"), workflow.name)
            self.assertNotIn("pull_request_target", text)

    def test_dotnet_setup_installs_only_the_pinned_sdk(self) -> None:
        # global.json pins SDK 10 (launcher plan §5.1); every project targets net10.0, so its runtime runs the tests.
        sdk = json.loads((REPO / "global.json").read_text(encoding="utf-8"))["sdk"]
        self.assertRegex(sdk["version"], r"^10\.0\.4\d\d$")
        self.assertEqual("latestPatch", sdk["rollForward"])
        steps = 0
        for workflow in sorted(WORKFLOW_DIRECTORY.glob("*.yml")):
            text = workflow.read_text(encoding="utf-8")
            for match in re.finditer(r"(?m)^\s*uses: actions/setup-dotnet@\S+.*\n\s*with:\n((?:\s+(?:#.*|[\w-]+:.*)\n)+)", text):
                steps += 1
                inputs = match.group(1)
                self.assertIn("global-json-file: global.json", inputs, workflow.name)
                self.assertNotIn("dotnet-version:", inputs, workflow.name)
        self.assertEqual(5, steps)

    def test_every_project_targets_net10(self) -> None:
        # .NET 8 left support on 2026-11-10; the exports bundle the runtime the projects target.
        projects = [path for root in ("engine", "godot", "launcher", "tools") for path in (REPO / root).rglob("*.csproj")
                    if not {"bin", "obj"} & set(path.relative_to(REPO).parts)]
        self.assertGreater(len(projects), 15)
        for project in projects:
            frameworks = re.findall(r"<TargetFrameworks?[^>]*>([^<$]+)<", project.read_text(encoding="utf-8-sig"))
            self.assertTrue(frameworks, project.name)
            self.assertEqual({"net10.0"}, set(frameworks), project.name)


if __name__ == "__main__":
    unittest.main()
