#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import re
import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import package_notices
import package_windows_x64
from test_package_notices import LAUNCHER_FIXTURE_PACKAGES, write_deps, write_launcher_deps
import verify_windows_native


def write_pe(path: Path, machine: int = verify_windows_native.PE_MACHINE_AMD64,
             subsystem: int = verify_windows_native.PE_SUBSYSTEM_WINDOWS_GUI) -> None:
    """A PE header: the signature at 64, the COFF machine, a PE32+ optional header with its Subsystem."""
    data = bytearray(256)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 64)
    data[64:68] = b"PE\0\0"
    struct.pack_into("<H", data, 68, machine)
    struct.pack_into("<H", data, 64 + 24, 0x20B)
    struct.pack_into("<H", data, 64 + 24 + 68, subsystem)
    path.write_bytes(data)


def launcher_deps(root: Path) -> Path:
    """The dependency list the launcher's publish kept, beside the fixture export."""
    return root / "launcher.deps.json"


def engine_deps(root: Path) -> Path:
    """The dependency list the engine's publish kept, beside the fixture export."""
    return root / "engine.deps.json"


ENGINE_LIBRARY = f"{package_windows_x64.DATA_DIRECTORY}/{verify_windows_native.ENGINE_LIBRARY}"


# THIRD_PARTY_NOTICES.md naming every package the fixtures ship.
NOTICES = ", ".join(f"`{name}`" for name in ("GodotSharp", "Tomlyn", *LAUNCHER_FIXTURE_PACKAGES)) + "\n"


def objdump_text(*, engine_export: bool = True, extra_import: str | None = None) -> str:
    """What objdump -p reports; one answer serves the shim and the engine's library."""
    lines = [f"DLL Name: {name}" for name in (*verify_windows_native.RUNTIME_DLLS, "KERNEL32.dll",
                                              "api-ms-win-crt-runtime-l1-1-0.dll")]
    lines.extend(f"[  0] {name}" for name in verify_windows_native.REQUIRED_EXPORTS)
    if engine_export:
        lines.append(f"[   1] {verify_windows_native.ENGINE_EXPORT}")
    if extra_import is not None:
        lines.append(f"DLL Name: {extra_import}")
    return "\n".join(lines)


def make_export(root: Path) -> Path:
    export = root / "export"
    for relative in package_windows_x64.REQUIRED_FILES:
        path = export / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(f"fixture:{relative}".encode())
    write_pe(export / "OME.exe")
    data = export / package_windows_x64.DATA_DIRECTORY
    for name in (verify_windows_native.ENGINE_LIBRARY, verify_windows_native.SHIM, *verify_windows_native.RUNTIME_DLLS):
        write_pe(data / name)
    write_deps(engine_deps(root), "win-x64")
    for name in (package_windows_x64.LAUNCHER.program, *package_windows_x64.LAUNCHER.natives):
        write_pe(export / name)
    write_launcher_deps(launcher_deps(root), "win-x64")
    return export


def verify(export: Path, root: Path, objdump: str | None = None) -> dict:
    completed = Mock(returncode=0, stdout=objdump if objdump is not None else objdump_text(), stderr="")
    with patch.object(verify_windows_native.subprocess, "run", return_value=completed):
        return package_windows_x64.verify_export(export, "objdump", launcher_deps(root), engine_deps(root))[1]


class PackageWindowsX64Tests(unittest.TestCase):
    def test_public_output_names_use_ome_branding(self) -> None:
        self.assertEqual("OME-windows-x64", package_windows_x64.PACKAGE_NAME)
        self.assertIn("OME.exe", package_windows_x64.REQUIRED_FILES)
        self.assertIn("OME.pck", package_windows_x64.REQUIRED_FILES)
        self.assertIn(ENGINE_LIBRARY, package_windows_x64.REQUIRED_FILES)
        # One NativeAOT library: no managed assembly, no .NET runtime, no dependency list in the data folder.
        data_files = set(package_windows_x64.DATA_FILES)
        self.assertEqual({"OME.dll", "ome_movie_ffmpeg.dll", *verify_windows_native.RUNTIME_DLLS},
                         {name for name in data_files if name.endswith(".dll")})
        self.assertEqual([], [name for name in data_files if name.endswith(".json")])

    def test_verify_rejects_missing_linux_and_wrong_machine_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export = make_export(root)
            verify(export, root)
            (export / package_windows_x64.REQUIRED_FILES[0]).unlink()
            with self.assertRaisesRegex(ValueError, "missing"):
                verify(export, root)
            write_pe(export / "OME.exe")
            linux = export / "stale.so"
            linux.write_bytes(b"linux")
            with self.assertRaisesRegex(ValueError, "Linux-only"):
                verify(export, root)
            linux.unlink()
            write_pe(export / "OME.exe", machine=0x014C)
            with self.assertRaisesRegex(ValueError, "not AMD64"):
                verify(export, root)

    def test_the_data_folder_holds_the_library_and_ffmpeg_only(self) -> None:
        # Any .NET runtime file sends Godot down its hostfxr route; a managed assembly, a dependency list or a stale
        # name has no place either.
        for name in ("hostfxr.dll", "coreclr.dll", "System.Private.CoreLib.dll", "Ome.Engine.dll", "OME.deps.json",
                     "OME.runtimeconfig.json", "Himegari.dll", "libome_movie_ffmpeg.so", "OME.pdb"):
            with self.subTest(name), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                export = make_export(root)
                (export / package_windows_x64.DATA_DIRECTORY / name).write_bytes(b"stray")
                with self.assertRaisesRegex(ValueError, "data folder holds files the NativeAOT engine does not ship: "
                                                        f"{re.escape(name)}$"):
                    verify(export, root)

    def test_the_engine_library_is_checked(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            report = verify(make_export(root), root)
        self.assertEqual("godotsharp_game_main_init", report["engine"]["export"])
        self.assertIn("api-ms-win-crt-runtime-l1-1-0.dll", report["engine"]["imports"])
        cases = (
            (objdump_text(engine_export=False), None, "does not export godotsharp_game_main_init"),
            # Linked against the dynamic Visual C++ runtime, which a player's Windows may lack.
            (objdump_text(extra_import="VCRUNTIME140.dll"), None, "lacks: VCRUNTIME140.dll"),
            (objdump_text(extra_import="hostfxr.dll"), None, "lacks: hostfxr.dll"),
            (objdump_text(), 0x014C, "engine library is not AMD64 PE \\(0x014c\\): OME.dll"),
        )
        for objdump, machine, message in cases:
            with self.subTest(message), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                export = make_export(root)
                if machine is not None:
                    write_pe(export / ENGINE_LIBRARY, machine=machine)
                with self.assertRaisesRegex(ValueError, message):
                    verify(export, root, objdump)

    def test_verify_checks_the_launcher_beside_the_engine(self) -> None:
        # Launcher spec §10, plan §9.3: the program, its natives and its notices, checked as PE files and never run.
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            report = verify(make_export(root), root)
        self.assertEqual({"program": "OME-Launcher.exe", "machine": "AMD64", "subsystem": "windows-gui",
                          "natives": {"av_libglesv2.dll": "AMD64", "libHarfBuzzSharp.dll": "AMD64",
                                      "libSkiaSharp.dll": "AMD64"}}, report["launcher"])
        cases = (
            (lambda export: (export / "OME-Launcher.exe").unlink(), "missing: OME-Launcher.exe"),
            (lambda export: (export / "OME-Launcher-notices/Avalonia.Angle.Windows.Natives-LICENSE").unlink(),
             "missing: OME-Launcher-notices/Avalonia.Angle.Windows.Natives-LICENSE"),
            (lambda export: (export / "OME-Launcher").write_bytes(b"linux"), "Linux-only files: OME-Launcher"),
            (lambda export: (export / "libSkiaSharp.so").write_bytes(b"linux"), "Linux-only files: libSkiaSharp.so"),
            (lambda export: (export / "libSkiaSharp.pdb").write_bytes(b"symbols"), "debug symbols: libSkiaSharp.pdb"),
            (lambda export: write_pe(export / "OME-Launcher.exe", subsystem=3), "not a windowed program"),
            (lambda export: write_pe(export / "OME-Launcher.exe", machine=0x014C), "launcher is not AMD64"),
            (lambda export: write_pe(export / "av_libglesv2.dll", machine=0xAA64),
             "native is not AMD64 PE \\(0xaa64\\): av_libglesv2.dll"),
        )
        for change, message in cases:
            with self.subTest(message), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                export = make_export(root)
                change(export)
                with self.assertRaisesRegex(ValueError, message):
                    verify(export, root)

    def test_verify_requires_notices_for_every_shipped_package(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = Path(temporary)
            export = make_export(temporary_root)
            notices = temporary_root / "THIRD_PARTY_NOTICES.md"
            notices.write_text("`GodotSharp`\n", encoding="utf-8")
            with patch.object(package_windows_x64, "REPO", temporary_root):
                with self.assertRaisesRegex(ValueError, "Microsoft.NETCore.App, Tomlyn"):
                    verify(export, temporary_root)
                notices.write_text("`GodotSharp` `Tomlyn` `Microsoft.NETCore.App`\n", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "launcher's shipped NuGet packages: Avalonia, "
                                                        "SkiaSharp.NativeAssets.Win32"):
                    verify(export, temporary_root)
                engine_deps(temporary_root).unlink()
                with self.assertRaisesRegex(ValueError, "engine's dependency list was not found"):
                    verify(export, temporary_root)

    def test_package_has_notices_reports_checksums_and_deterministic_zip(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = Path(temporary)
            export = make_export(temporary_root)
            output = temporary_root / "build/package/windows-x64"
            metadata = {"schema_version": 1, "source_commit": "fixture"}
            verification = {"schema_version": 1, "target": "win-x64"}
            godot_license = temporary_root / "notices/LICENSE.txt"
            godot_license.parent.mkdir()
            godot_license.write_bytes(b"godot\n")
            pinned = (
                package_notices.PinnedNotice(
                    godot_license, "Godot-LICENSE.txt", hashlib.sha256(b"godot\n").hexdigest()
                ),
            )
            with patch.object(package_windows_x64, "REPO", temporary_root), patch.object(
                package_windows_x64, "PROJECT_FILES", ("LICENSE", "THIRD_PARTY_NOTICES.md")
            ), patch.object(package_windows_x64, "PINNED_NOTICES", pinned):
                (temporary_root / "LICENSE").write_text("MIT\n", encoding="utf-8")
                (temporary_root / "THIRD_PARTY_NOTICES.md").write_text(
                    "`GodotSharp`, `Microsoft.NETCore.App`, `Tomlyn`\n", encoding="utf-8"
                )
                package_windows_x64.create_package(
                    export, output, metadata, verification, 123456789
                )
                archive = output / f"{package_windows_x64.PACKAGE_NAME}.zip"
                first = hashlib.sha256(archive.read_bytes()).hexdigest()
                package_root, archive = package_windows_x64.create_package(
                    export, output, metadata, verification, 123456789
                )
                second = hashlib.sha256(archive.read_bytes()).hexdigest()
            self.assertEqual(first, second)
            self.assertTrue((package_root / "LICENSE").is_file())
            self.assertEqual(b"godot\n", (package_root / "Godot-LICENSE.txt").read_bytes())
            self.assertTrue((package_root / "BUILD-INFO.json").is_file())
            self.assertTrue((package_root / "WINDOWS-VERIFICATION.json").is_file())
            checksums = (package_root / "SHA256SUMS").read_text(encoding="utf-8").splitlines()
            names = [line.split("  ", 1)[1] for line in checksums]
            for name in ("OME.exe", ENGINE_LIBRARY, "THIRD_PARTY_NOTICES.md", *package_windows_x64.LAUNCHER.files()):
                self.assertIn(name, names)


if __name__ == "__main__":
    unittest.main()
