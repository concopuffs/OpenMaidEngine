#!/usr/bin/env python3
from __future__ import annotations

import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import verify_windows_native


def write_pe(path: Path, machine: int = verify_windows_native.PE_MACHINE_AMD64) -> None:
    data = bytearray(128)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 64)
    data[64:68] = b"PE\0\0"
    struct.pack_into("<H", data, 68, machine)
    path.write_bytes(data)


def objdump_text(*, omit_export: str | None = None, extra_import: str | None = None) -> str:
    lines = [*(f"DLL Name: {name}" for name in verify_windows_native.RUNTIME_DLLS)]
    lines.extend(
        f"[  0] {name}" for name in sorted(verify_windows_native.REQUIRED_EXPORTS)
        if name != omit_export
    )
    if extra_import is not None:
        lines.append(f"DLL Name: {extra_import}")
    return "\n".join(lines)


def create_bundle(root: Path) -> Path:
    for name in (verify_windows_native.SHIM, *verify_windows_native.RUNTIME_DLLS):
        write_pe(root / name)
    (root / "FFmpeg-LICENSE.txt").write_text("LGPL\n", encoding="utf-8")
    (root / "FFmpeg-SOURCE.txt").write_text("source\n", encoding="utf-8")
    return root


class VerifyWindowsNativeTests(unittest.TestCase):
    def test_accepts_exact_amd64_bundle_and_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            bundle = create_bundle(Path(temporary))
            completed = Mock(returncode=0, stdout=objdump_text(), stderr="")
            with patch.object(verify_windows_native.subprocess, "run", return_value=completed):
                report = verify_windows_native.verify_bundle(bundle, "objdump")
            self.assertEqual("win-x64", report["target"])
            self.assertEqual(len(verify_windows_native.REQUIRED_EXPORTS), len(report["shim_exports"]))

    def test_rejects_wrong_machine_and_unexpected_dll(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            bundle = create_bundle(Path(temporary))
            write_pe(bundle / verify_windows_native.RUNTIME_DLLS[0], machine=0x014C)
            with self.assertRaisesRegex(ValueError, "not AMD64"):
                verify_windows_native.verify_bundle(bundle, "objdump")

            write_pe(bundle / verify_windows_native.RUNTIME_DLLS[0])
            write_pe(bundle / "stale.dll")
            with self.assertRaisesRegex(ValueError, "unexpected DLL"):
                verify_windows_native.verify_bundle(bundle, "objdump")

    def test_rejects_missing_export_and_compatibility_runtime(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            bundle = create_bundle(Path(temporary))
            missing = next(iter(verify_windows_native.REQUIRED_EXPORTS))
            completed = Mock(
                returncode=0,
                stdout=objdump_text(omit_export=missing, extra_import="cygwin1.dll"),
                stderr="",
            )
            with patch.object(verify_windows_native.subprocess, "run", return_value=completed):
                with self.assertRaisesRegex(ValueError, "compatibility runtime"):
                    verify_windows_native.verify_bundle(bundle, "objdump")

            completed.stdout = objdump_text(omit_export=missing)
            with patch.object(verify_windows_native.subprocess, "run", return_value=completed):
                with self.assertRaisesRegex(ValueError, "missing AGE ABI export"):
                    verify_windows_native.verify_bundle(bundle, "objdump")


if __name__ == "__main__":
    unittest.main()
