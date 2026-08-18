#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import package_windows_x64
import verify_windows_native


def write_pe(path: Path, machine: int = verify_windows_native.PE_MACHINE_AMD64) -> None:
    data = bytearray(128)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 64)
    data[64:68] = b"PE\0\0"
    struct.pack_into("<H", data, 68, machine)
    path.write_bytes(data)


def objdump_text() -> str:
    lines = [f"DLL Name: {name}" for name in verify_windows_native.RUNTIME_DLLS]
    lines.extend(f"[  0] {name}" for name in verify_windows_native.REQUIRED_EXPORTS)
    return "\n".join(lines)


def make_export(root: Path) -> Path:
    export = root / "export"
    for relative in package_windows_x64.REQUIRED_FILES:
        path = export / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(f"fixture:{relative}".encode())
    write_pe(export / "OME.exe")
    managed = export / package_windows_x64.MANAGED_DIRECTORY
    for name in (verify_windows_native.SHIM, *verify_windows_native.RUNTIME_DLLS):
        write_pe(managed / name)
    return export


class PackageWindowsX64Tests(unittest.TestCase):
    def test_public_output_names_use_ome_branding(self) -> None:
        self.assertEqual("OME-windows-x64", package_windows_x64.PACKAGE_NAME)
        self.assertIn("OME.exe", package_windows_x64.REQUIRED_FILES)
        self.assertIn("OME.pck", package_windows_x64.REQUIRED_FILES)
        self.assertIn(
            f"{package_windows_x64.MANAGED_DIRECTORY}/OME.dll",
            package_windows_x64.REQUIRED_FILES,
        )
        self.assertIn(
            f"{package_windows_x64.MANAGED_DIRECTORY}/Himegari.dll",
            package_windows_x64.LEGACY_FILES,
        )

    def test_verify_rejects_missing_linux_and_wrong_machine_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            export = make_export(Path(temporary))
            completed = Mock(returncode=0, stdout=objdump_text(), stderr="")
            with patch.object(verify_windows_native.subprocess, "run", return_value=completed):
                package_windows_x64.verify_export(export, "objdump")
            (export / package_windows_x64.REQUIRED_FILES[-1]).unlink()
            with self.assertRaisesRegex(ValueError, "missing"):
                package_windows_x64.verify_export(export, "objdump")
            (export / package_windows_x64.REQUIRED_FILES[-1]).write_bytes(b"restored")
            linux = export / package_windows_x64.MANAGED_DIRECTORY / "stale.so"
            linux.write_bytes(b"linux")
            with self.assertRaisesRegex(ValueError, "Linux-only"):
                package_windows_x64.verify_export(export, "objdump")
            linux.unlink()
            legacy = export / package_windows_x64.LEGACY_FILES[0]
            legacy.write_bytes(b"legacy")
            with self.assertRaisesRegex(ValueError, "legacy-branded"):
                package_windows_x64.verify_export(export, "objdump")
            legacy.unlink()
            write_pe(export / "OME.exe", machine=0x014C)
            with self.assertRaisesRegex(ValueError, "not AMD64"):
                package_windows_x64.verify_export(export, "objdump")

    def test_package_has_notices_reports_checksums_and_deterministic_zip(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = Path(temporary)
            export = make_export(temporary_root)
            output = temporary_root / "build/package/windows-x64"
            metadata = {"schema_version": 1, "source_commit": "fixture"}
            verification = {"schema_version": 1, "target": "win-x64"}
            with patch.object(package_windows_x64, "REPO", temporary_root), patch.object(
                package_windows_x64, "PROJECT_FILES", ("LICENSE", "THIRD_PARTY_NOTICES.md")
            ):
                (temporary_root / "LICENSE").write_text("MIT\n", encoding="utf-8")
                (temporary_root / "THIRD_PARTY_NOTICES.md").write_text(
                    "notices\n", encoding="utf-8"
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
            self.assertTrue((package_root / "BUILD-INFO.json").is_file())
            self.assertTrue((package_root / "WINDOWS-VERIFICATION.json").is_file())
            checksums = (package_root / "SHA256SUMS").read_text(encoding="utf-8")
            self.assertIn("OME.exe", checksums)
            self.assertIn("THIRD_PARTY_NOTICES.md", checksums)


if __name__ == "__main__":
    unittest.main()
