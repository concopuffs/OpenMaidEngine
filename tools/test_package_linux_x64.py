#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import package_notices
import package_linux_x64
from test_package_notices import LAUNCHER_FIXTURE_PACKAGES, write_deps, write_launcher_deps


def make_export(root: Path) -> Path:
    export = root / "export"
    for relative in package_linux_x64.REQUIRED_FILES:
        path = export / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(f"fixture:{relative}".encode())
    (export / "OME").chmod(0o755)
    (export / package_linux_x64.LAUNCHER.program).chmod(0o755)
    write_deps(export / package_linux_x64.MANAGED_DIRECTORY, "linux-x64")
    write_launcher_deps(launcher_deps(root), "linux-x64")
    return export


def launcher_deps(root: Path) -> Path:
    """The dependency list the launcher's publish kept, beside the fixture export."""
    return root / "launcher.deps.json"


# THIRD_PARTY_NOTICES.md naming every package the fixtures ship.
NOTICES = ", ".join(f"`{name}`" for name in ("GodotSharp", "Tomlyn", *LAUNCHER_FIXTURE_PACKAGES)) + "\n"


def fake_engine(body: str) -> list[str]:
    """A stand-in for `OME --headless -- --package-smoke --hold`: a Python program running `body`."""
    return [sys.executable, "-c", "import sys\n" + body]


class PackageSmokeTests(unittest.TestCase):
    """Launcher plan §4.12: the marker must reach the pipe while the engine runs."""

    MARKER = package_linux_x64.SMOKE_MARKER

    def run_smoke(self, body: str, timeout: float = 30.0) -> tuple[str | None, str]:
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "package-smoke.log"
            try:
                package_linux_x64.run_smoke(fake_engine(body), self.MARKER, log, timeout)
                error = None
            except package_linux_x64.SmokeError as failure:
                error = str(failure)
            return error, log.read_text(encoding="utf-8")

    def test_a_flushed_line_while_the_engine_holds_passes(self) -> None:
        error, log = self.run_smoke(f"print('banner', flush=True)\nprint({self.MARKER!r}, flush=True)\n"
                                    "sys.stdin.read()\nprint('after', flush=True)")
        self.assertIsNone(error)
        self.assertEqual(["banner", self.MARKER, "after"], log.splitlines())  # every line, the last too

    def test_a_line_held_in_the_engines_buffer_fails(self) -> None:
        # Into a pipe, Python's print is block-buffered, as a release Godot's was before flush_stdout_on_print: the
        # line waits while the engine holds, so it never arrives in time.
        error, _ = self.run_smoke(f"print({self.MARKER!r})\nsys.stdin.read()", timeout=2.0)
        self.assertIn("is not flushed", error)

    def test_a_failed_exit_fails(self) -> None:
        error, log = self.run_smoke(f"print({self.MARKER!r}, flush=True)\nsys.stdin.read()\nsys.exit(3)")
        self.assertEqual("the engine exited with 3", error)
        self.assertIn(self.MARKER, log)

    def test_output_without_the_marker_fails(self) -> None:
        error, log = self.run_smoke("print('PACKAGE SMOKE FAILED', flush=True)\nsys.exit(1)")
        self.assertIn("ended (1) before", error)
        self.assertIn("PACKAGE SMOKE FAILED", log)

    def test_the_smoke_holds_the_packaged_engine(self) -> None:
        command = package_linux_x64.smoke_command(Path("/opt/OME-linux-x64"))
        self.assertEqual(["--headless", "--", "--package-smoke", "--hold"], command[1:])
        self.assertEqual(Path("/opt/OME-linux-x64/OME"), Path(command[0]))


class PackageLinuxX64Tests(unittest.TestCase):
    def test_public_output_names_use_ome_branding(self) -> None:
        self.assertEqual("OME-linux-x64", package_linux_x64.PACKAGE_NAME)
        self.assertIn("OME", package_linux_x64.REQUIRED_FILES)
        self.assertIn("OME.pck", package_linux_x64.REQUIRED_FILES)
        self.assertIn(
            f"{package_linux_x64.MANAGED_DIRECTORY}/OME.dll",
            package_linux_x64.REQUIRED_FILES,
        )
        self.assertIn(
            f"{package_linux_x64.MANAGED_DIRECTORY}/Himegari.dll",
            package_linux_x64.LEGACY_FILES,
        )

    def test_verify_rejects_missing_and_windows_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export = make_export(root)
            package_linux_x64.verify_export(export, launcher_deps(root))
            (export / package_linux_x64.REQUIRED_FILES[0]).unlink()
            with self.assertRaisesRegex(ValueError, "missing"):
                package_linux_x64.verify_export(export, launcher_deps(root))
            (export / package_linux_x64.REQUIRED_FILES[0]).write_bytes(b"restored")
            (export / "OME").chmod(0o755)
            forbidden = export / package_linux_x64.FORBIDDEN_FILES[0]
            forbidden.write_bytes(b"windows")
            with self.assertRaisesRegex(ValueError, "Windows-only"):
                package_linux_x64.verify_export(export, launcher_deps(root))
            forbidden.unlink()
            legacy = export / package_linux_x64.LEGACY_FILES[0]
            legacy.write_bytes(b"legacy")
            with self.assertRaisesRegex(ValueError, "legacy-branded"):
                package_linux_x64.verify_export(export, launcher_deps(root))

    def test_verify_requires_the_launcher_beside_the_engine(self) -> None:
        # Launcher spec §10, plan §9.3: the program, its natives and its notices; not the Windows launcher; no symbols.
        cases = (
            (lambda export: (export / "OME-Launcher").unlink(), "missing: OME-Launcher"),
            (lambda export: (export / "libSkiaSharp.so").unlink(), "missing: libSkiaSharp.so"),
            (lambda export: (export / "OME-Launcher-notices/SkiaSharp.NativeAssets.Linux-THIRD-PARTY-NOTICES.txt")
             .unlink(), "missing: OME-Launcher-notices/SkiaSharp.NativeAssets.Linux-THIRD-PARTY-NOTICES.txt"),
            (lambda export: (export / "OME-Launcher.exe").write_bytes(b"windows"), "Windows-only files: OME-Launcher.exe"),
            (lambda export: (export / "libSkiaSharp.dll").write_bytes(b"windows"), "Windows-only files: libSkiaSharp.dll"),
            (lambda export: (export / "libSkiaSharp.pdb").write_bytes(b"symbols"), "debug symbols: libSkiaSharp.pdb"),
        )
        for change, message in cases:
            with self.subTest(message), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                export = make_export(root)
                change(export)
                with self.assertRaisesRegex(ValueError, message):
                    package_linux_x64.verify_export(export, launcher_deps(root))

    @unittest.skipIf(package_linux_x64.os.name == "nt", "executable-bit gate is POSIX-only")
    def test_verify_requires_executable_bit_on_posix(self) -> None:
        for program in ("OME", "OME-Launcher"):
            with self.subTest(program), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                export = make_export(root)
                (export / program).chmod(0o644)
                with self.assertRaisesRegex(ValueError, f"executable bit is not set: .*{program}"):
                    package_linux_x64.verify_export(export, launcher_deps(root))

    def test_verify_requires_notices_for_every_shipped_package(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = Path(temporary)
            export = make_export(temporary_root)
            notices = temporary_root / "THIRD_PARTY_NOTICES.md"
            notices.write_text("`GodotSharp`\n", encoding="utf-8")
            with patch.object(package_linux_x64, "REPO", temporary_root):
                with self.assertRaisesRegex(ValueError, "Microsoft.NETCore.App, Tomlyn"):
                    package_linux_x64.verify_export(export, launcher_deps(temporary_root))
                # The engine's packages named, the launcher's list is read too: from the copy its publish kept.
                notices.write_text("`GodotSharp` `Tomlyn` `Microsoft.NETCore.App`\n", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "launcher's shipped NuGet packages: Avalonia, "
                                                        "SkiaSharp.NativeAssets.Linux"):
                    package_linux_x64.verify_export(export, launcher_deps(temporary_root))
                with self.assertRaisesRegex(ValueError, "launcher's dependency list was not found"):
                    package_linux_x64.verify_export(export, temporary_root / "elsewhere.deps.json")

    def test_package_has_notices_checksums_and_deterministic_envelope(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = Path(temporary)
            export = make_export(temporary_root)
            output = temporary_root / "build/package"
            metadata = {"schema_version": 1, "source_commit": "fixture"}
            godot_license = temporary_root / "notices/LICENSE.txt"
            godot_license.parent.mkdir()
            godot_license.write_bytes(b"godot\n")
            pinned = (
                package_notices.PinnedNotice(
                    godot_license, "Godot-LICENSE.txt", hashlib.sha256(b"godot\n").hexdigest()
                ),
            )
            deps = launcher_deps(temporary_root)
            with patch.object(package_linux_x64, "REPO", temporary_root), patch.object(
                package_linux_x64, "PROJECT_FILES", ("LICENSE", "THIRD_PARTY_NOTICES.md")
            ), patch.object(package_linux_x64, "PINNED_NOTICES", pinned):
                (temporary_root / "LICENSE").write_text("MIT\n", encoding="utf-8")
                (temporary_root / "THIRD_PARTY_NOTICES.md").write_text(NOTICES, encoding="utf-8")
                package_linux_x64.create_package(export, output, metadata, 123456789, deps)
                first = hashlib.sha256(
                    (output / f"{package_linux_x64.PACKAGE_NAME}.tar.gz").read_bytes()
                ).hexdigest()
                package_root, archive = package_linux_x64.create_package(
                    export, output, metadata, 123456789, deps
                )
                second = hashlib.sha256(archive.read_bytes()).hexdigest()
            self.assertEqual(first, second)
            self.assertTrue((package_root / "LICENSE").is_file())
            self.assertEqual(b"godot\n", (package_root / "Godot-LICENSE.txt").read_bytes())
            self.assertTrue((package_root / "BUILD-INFO.json").is_file())
            checksums = (package_root / "SHA256SUMS").read_text(encoding="utf-8").splitlines()
            names = [line.split("  ", 1)[1] for line in checksums]
            for name in ("OME", "THIRD_PARTY_NOTICES.md", *package_linux_x64.LAUNCHER.files()):
                self.assertIn(name, names)


if __name__ == "__main__":
    unittest.main()
