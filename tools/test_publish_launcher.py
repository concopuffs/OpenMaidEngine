#!/usr/bin/env python3
"""publish_launcher.py on synthetic publishes (launcher plan §9.2): no dotnet runs."""
from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path

import publish_launcher

LINUX = publish_launcher.LAYOUTS["linux-x64"]
WINDOWS = publish_launcher.LAYOUTS["win-x64"]


def write_publish(directory: Path, layout: publish_launcher.Layout, *extra: str) -> Path:
    for name in (*layout.files(), *extra):
        path = directory / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(f"fixture:{name}".encode())
    return directory


def export_with_engine(root: Path, layout: publish_launcher.Layout) -> Path:
    export = root / "export"
    export.mkdir()
    (export / layout.engine).write_bytes(b"engine")
    return export


class FakeDotnet:
    """Stands in for `dotnet publish`: records the command and writes `layout`'s files and a dependency list."""

    def __init__(self, layout: publish_launcher.Layout, *extra: str, returncode: int = 0, keep_list: bool = True):
        self.layout, self.extra, self.returncode, self.keep_list = layout, extra, returncode, keep_list
        self.commands: list[list[str]] = []
        self.environments: list[dict[str, str]] = []

    def __call__(self, command, cwd, env, check):
        self.commands.append(command)
        self.environments.append(env)
        output = Path(command[command.index("--output") + 1])
        if self.returncode == 0:
            write_publish(output, self.layout, *self.extra)
            if self.keep_list:
                listed = next(part for part in command if part.startswith("-p:OmeDependencyList="))
                Path(listed.split("=", 1)[1]).write_text("{}", encoding="utf-8")
        return subprocess.CompletedProcess(command, self.returncode)


class PublishLauncherTests(unittest.TestCase):
    def test_the_layouts_are_the_release_forms_files(self) -> None:
        self.assertEqual(("OME-Launcher", "libHarfBuzzSharp.so", "libSkiaSharp.so"), LINUX.files()[:3])
        self.assertEqual("OME-Launcher.exe", WINDOWS.program)
        self.assertIn("av_libglesv2.dll", WINDOWS.natives)
        self.assertIn("OME-Launcher-notices/Avalonia.Angle.Windows.Natives-LICENSE", WINDOWS.files())
        self.assertNotIn("OME-Launcher-notices/Avalonia.Angle.Windows.Natives-LICENSE", LINUX.files())
        for layout in (LINUX, WINDOWS):
            self.assertIn(f"OME-Launcher-notices/Microsoft.NETCore.App.Runtime.{layout.runtime}-LICENSE.TXT",
                          layout.files())

    def test_the_publish_is_the_projects_release_form_with_one_node(self) -> None:
        root = Path("/opt/build/launcher-publish")
        command = publish_launcher.publish_command("dotnet", "win-x64", root)
        self.assertEqual(["dotnet", "publish", str(publish_launcher.PROJECT)], command[:3])
        self.assertEqual("win-x64", command[command.index("--runtime") + 1])
        self.assertEqual("Release", command[command.index("--configuration") + 1])
        self.assertEqual(str(root / "win-x64"), command[command.index("--output") + 1])
        for flag in ("--disable-build-servers", "-nodeReuse:false", "-m:1", "-p:UseSharedCompilation=false"):
            self.assertIn(flag, command)
        self.assertIn(f"-p:OmeDependencyList={root / 'win-x64.deps.json'}", command)
        # The form is the project's: the tool adds no single-file, compression or ReadyToRun flag of its own.
        self.assertFalse([part for part in command if "SingleFile" in part or "ReadyToRun" in part
                          or "Compression" in part or "Trim" in part])

    def test_verify_accepts_exactly_the_launchers_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = write_publish(Path(temporary), LINUX)
            publish_launcher.verify_publish(directory, LINUX)
            # A notice the target's packages add later is accepted by its name.
            (directory / "OME-Launcher-notices/Tmds.DBus.Protocol-LICENSE.md").write_bytes(b"notice")
            publish_launcher.verify_publish(directory, LINUX)

    def test_verify_refuses_symbols_lists_other_targets_and_missing_files(self) -> None:
        cases = {
            "libSkiaSharp.pdb": "unexpected files: libSkiaSharp.pdb",
            "OME-Launcher.deps.json": "unexpected files: OME-Launcher.deps.json",
            "libSkiaSharp.dll": "unexpected files: libSkiaSharp.dll",
            "OME-Launcher-notices/readme.txt": "unexpected files: OME-Launcher-notices/readme.txt",
            "runtimes/linux-x64/native/libSkiaSharp.so": "unexpected files: runtimes/",
        }
        for extra, message in cases.items():
            with self.subTest(extra), tempfile.TemporaryDirectory() as temporary:
                directory = write_publish(Path(temporary), LINUX, extra)
                with self.assertRaisesRegex(ValueError, message):
                    publish_launcher.verify_publish(directory, LINUX)
        with tempfile.TemporaryDirectory() as temporary:
            directory = write_publish(Path(temporary), WINDOWS)
            (directory / "av_libglesv2.dll").unlink()
            with self.assertRaisesRegex(ValueError, "missing: av_libglesv2.dll"):
                publish_launcher.verify_publish(directory, WINDOWS)

    def test_publish_places_the_launcher_beside_the_engine(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export = export_with_engine(root, WINDOWS)
            dotnet = FakeDotnet(WINDOWS)
            copies = publish_launcher.publish("win-x64", export, output_root=root / "launcher-publish", run=dotnet)
            self.assertEqual(len(WINDOWS.files()), len(copies))
            for name in WINDOWS.files():
                self.assertEqual(f"fixture:{name}".encode(), (export / name).read_bytes())
            self.assertEqual(b"engine", (export / "OME.exe").read_bytes())
            self.assertEqual("9", dotnet.environments[0]["DOTNET_GCConserveMemory"])
            self.assertEqual("0", dotnet.environments[0]["DOTNET_CLI_USE_MSBUILD_SERVER"])
            self.assertTrue((root / "launcher-publish/win-x64.deps.json").is_file())

    def test_publish_refuses_before_it_runs_when_the_export_has_no_engine(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "export").mkdir()
            dotnet = FakeDotnet(LINUX)
            with self.assertRaisesRegex(ValueError, "no OME to put the launcher beside"):
                publish_launcher.publish("linux-x64", root / "export", output_root=root / "out", run=dotnet)
            self.assertEqual([], dotnet.commands)

    def test_publish_fails_on_a_failed_publish_a_lost_list_or_a_wrong_output(self) -> None:
        cases = (
            (FakeDotnet(LINUX, returncode=3), RuntimeError, "failed with exit code 3"),
            (FakeDotnet(LINUX, keep_list=False), ValueError, "kept no dependency list"),
            (FakeDotnet(LINUX, "libSkiaSharp.pdb"), ValueError, "unexpected files"),
        )
        for dotnet, error, message in cases:
            with self.subTest(message), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                export = export_with_engine(root, LINUX)
                with self.assertRaisesRegex(error, message):
                    publish_launcher.publish("linux-x64", export, output_root=root / "out", run=dotnet)
                self.assertFalse((export / "OME-Launcher").exists())

    def test_a_second_publish_starts_from_an_empty_folder(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export = export_with_engine(root, LINUX)
            stale = root / "out/linux-x64/libSkiaSharp.pdb"
            stale.parent.mkdir(parents=True)
            stale.write_bytes(b"old")
            publish_launcher.publish("linux-x64", export, output_root=root / "out", run=FakeDotnet(LINUX))
            self.assertFalse(stale.exists())


if __name__ == "__main__":
    unittest.main()
