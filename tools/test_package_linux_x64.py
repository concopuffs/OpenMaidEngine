#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import package_linux_x64


def make_export(root: Path) -> Path:
    export = root / "export"
    for relative in package_linux_x64.REQUIRED_FILES:
        path = export / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(f"fixture:{relative}".encode())
    (export / "Himegari.x86_64").chmod(0o755)
    return export


class PackageLinuxX64Tests(unittest.TestCase):
    def test_verify_rejects_missing_and_windows_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            export = make_export(Path(temporary))
            package_linux_x64.verify_export(export)
            (export / package_linux_x64.REQUIRED_FILES[-1]).unlink()
            with self.assertRaisesRegex(ValueError, "missing"):
                package_linux_x64.verify_export(export)
            (export / package_linux_x64.REQUIRED_FILES[-1]).write_bytes(b"restored")
            forbidden = export / package_linux_x64.FORBIDDEN_FILES[0]
            forbidden.write_bytes(b"windows")
            with self.assertRaisesRegex(ValueError, "Windows-only"):
                package_linux_x64.verify_export(export)

    @unittest.skipIf(package_linux_x64.os.name == "nt", "executable-bit gate is POSIX-only")
    def test_verify_requires_executable_bit_on_posix(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            export = make_export(Path(temporary))
            (export / "Himegari.x86_64").chmod(0o644)
            with self.assertRaisesRegex(ValueError, "executable bit"):
                package_linux_x64.verify_export(export)

    def test_package_has_notices_checksums_and_deterministic_envelope(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = Path(temporary)
            export = make_export(temporary_root)
            output = temporary_root / "build/package"
            metadata = {"schema_version": 1, "source_commit": "fixture"}
            with patch.object(package_linux_x64, "REPO", temporary_root), patch.object(
                package_linux_x64, "PROJECT_FILES", ("LICENSE", "THIRD_PARTY_NOTICES.md")
            ):
                (temporary_root / "LICENSE").write_text("MIT\n", encoding="utf-8")
                (temporary_root / "THIRD_PARTY_NOTICES.md").write_text("notices\n", encoding="utf-8")
                package_linux_x64.create_package(export, output, metadata, 123456789)
                first = hashlib.sha256(
                    (output / f"{package_linux_x64.PACKAGE_NAME}.tar.gz").read_bytes()
                ).hexdigest()
                package_root, archive = package_linux_x64.create_package(
                    export, output, metadata, 123456789
                )
                second = hashlib.sha256(archive.read_bytes()).hexdigest()
            self.assertEqual(first, second)
            self.assertTrue((package_root / "LICENSE").is_file())
            self.assertTrue((package_root / "BUILD-INFO.json").is_file())
            checksums = (package_root / "SHA256SUMS").read_text(encoding="utf-8")
            self.assertIn("Himegari.x86_64", checksums)
            self.assertIn("THIRD_PARTY_NOTICES.md", checksums)


if __name__ == "__main__":
    unittest.main()
