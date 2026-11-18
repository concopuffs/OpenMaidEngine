#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import os
import re
import shlex
import struct
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import package_notices
import package_linux_x64
import parse_sys4ini
from test_package_notices import LAUNCHER_FIXTURE_PACKAGES, write_deps, write_launcher_deps

ENGINE_LIBRARY = f"{package_linux_x64.DATA_DIRECTORY}/OME.so"


def write_elf(path: Path, machine: int = package_linux_x64.ELF_MACHINE_X86_64) -> None:
    """An ELF header: 64-bit, little-endian, and the machine."""
    data = bytearray(64)
    data[:6] = b"\x7fELF\x02\x01"
    struct.pack_into("<H", data, 18, machine)
    path.write_bytes(data)


def make_export(root: Path) -> Path:
    export = root / "export"
    for relative in package_linux_x64.REQUIRED_FILES:
        path = export / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(f"fixture:{relative}".encode())
    write_elf(export / ENGINE_LIBRARY)
    (export / "OME").chmod(0o755)
    (export / package_linux_x64.LAUNCHER.program).chmod(0o755)
    write_deps(engine_deps(root), "linux-x64")
    write_launcher_deps(launcher_deps(root), "linux-x64")
    return export


def launcher_deps(root: Path) -> Path:
    """The dependency list the launcher's publish kept, beside the fixture export."""
    return root / "launcher.deps.json"


def engine_deps(root: Path) -> Path:
    """The dependency list the engine's publish kept, beside the fixture export."""
    return root / "engine.deps.json"


# What readelf reports for the engine's NativeAOT library (linux-x64, linked on glibc 2.28), by option.
READELF = {
    "--dyn-syms": "   240: 0000000000800690   361 FUNC    GLOBAL DEFAULT   14 godotsharp_game_main_init@@V1.0\n",
    "--dynamic": "".join(f" 0x0000000000000001 (NEEDED)             Shared library: [{name}]\n"
                         for name in ("libdl.so.2", "librt.so.1", "libm.so.6", "libpthread.so.0", "libc.so.6",
                                      "ld-linux-x86-64.so.2")),
    "--version-info": ("  0x0010:   Name: GLIBC_2.2.5  Flags: none  Version: 3\n"
                       "  0x0020:   Name: GLIBC_2.28  Flags: none  Version: 9\n"
                       "  0x0030:   Name: GLIBC_2.27  Flags: none  Version: 8\n"),
}


def fake_readelf(outputs: dict[str, str] | None = None):
    """subprocess.run as readelf answers it, with `outputs` in place of READELF's."""
    answers = {**READELF, **(outputs or {})}

    def run(arguments, **_):
        option = next(argument for argument in arguments if argument in answers)
        return Mock(returncode=0, stdout=answers[option], stderr="")
    return run


def verify(export: Path, root: Path, deps: Path | None = None, readelf: dict[str, str] | None = None) -> Path:
    with patch.object(package_linux_x64.subprocess, "run", side_effect=fake_readelf(readelf)):
        return package_linux_x64.verify_export(export, deps or launcher_deps(root), engine_deps(root))


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


PASSED_CHECKS = "".join(f"passed   {kind}: fine\n" for kind in package_linux_x64.LAUNCHER_CHECK_KINDS)


def fake_launcher(body: str) -> list[str]:
    """A stand-in for `OME-Launcher`: a Python program running `body`, with `home` the `--home` it was given."""
    return [sys.executable, "-c",
            "import os, sys\nfrom pathlib import Path\nhome = Path(sys.argv[sys.argv.index('--home') + 1])\n" + body]


class LauncherCheckSmokeTests(unittest.TestCase):
    """Launcher plan §9.4, gate 11: `OME-Launcher --check smoke --home HOME` with no display."""

    def check(self, body: str) -> tuple[str | None, str]:
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "package-smoke.log"
            log.write_text("PACKAGE SMOKE OK\n", encoding="utf-8")
            # The job's display, which the step must remove.
            with patch.dict(os.environ, {"DISPLAY": ":0", "WAYLAND_DISPLAY": "wayland-0"}):
                try:
                    package_linux_x64.run_launcher_check(fake_launcher(body), log, timeout=30.0)
                    error = None
                except package_linux_x64.SmokeError as failure:
                    error = str(failure)
            return error, log.read_text(encoding="utf-8")

    def test_six_passed_checks_with_no_display_pass(self) -> None:
        error, log = self.check(
            "assert 'DISPLAY' not in os.environ and 'WAYLAND_DISPLAY' not in os.environ\n"
            "assert sys.argv[1:4] == ['--check', 'smoke', '--home']\n"
            "assert (home / 'profiles/smoke/profile.toml').is_file()\n"
            "assert (home / 'installs/himegari/SYS4INI.BIN').is_file()\n"
            "assert Path(os.environ['HOME']).is_dir() and Path(os.environ['XDG_DATA_HOME']).is_dir()\n"
            f"print({PASSED_CHECKS!r}, end='')\n")
        self.assertIsNone(error)
        lines = log.splitlines()
        self.assertEqual("PACKAGE SMOKE OK", lines[0])  # the engine's step stays in the log
        self.assertTrue(lines[1].startswith("== the launcher's check, with no display: "))
        self.assertIn("--check smoke --home", log)
        self.assertEqual(package_linux_x64.LAUNCHER_CHECK_MARKER, lines[-1])
        self.assertEqual("LAUNCHER CHECK OK: checks=6", lines[-1])

    def test_a_failed_check_fails(self) -> None:
        error, log = self.check("print('FAILED   Install: no game there')\nprint('not run  Fingerprint: needs the install')\n"
                                "sys.exit(1)\n")
        self.assertIn("it exited with 1", error)
        self.assertIn("it printed: Fingerprint not run, Install FAILED", error)
        self.assertIn("FAILED   Install: no game there", log)
        self.assertNotIn(package_linux_x64.LAUNCHER_CHECK_MARKER, log)

    def test_a_check_that_needs_a_display_fails(self) -> None:
        error, log = self.check("if 'DISPLAY' not in os.environ:\n"
                                "    print('Unhandled exception: XOpenDisplay failed', file=sys.stderr)\n"
                                "    sys.exit(134)\n"
                                f"print({PASSED_CHECKS!r}, end='')\n")
        self.assertIn("it exited with 134", error)
        self.assertIn("XOpenDisplay failed", log)  # standard error reaches the log too

    def test_a_missing_or_warned_check_fails_even_on_exit_0(self) -> None:
        five = "".join(f"passed   {kind}: fine\n" for kind in package_linux_x64.LAUNCHER_CHECK_KINDS[:-1])
        error, _ = self.check(f"print({five!r}, end='')\n")
        self.assertIn("each passed; it printed: Fingerprint passed, Install passed, Link passed, Packages passed, "
                      "Root passed", error)
        warned = PASSED_CHECKS.replace("passed   Lock", "warning  Lock")
        error, _ = self.check(f"print({warned!r}, end='')\n")
        self.assertIn("Lock warning", error)

    def test_a_launcher_that_never_ends_fails(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "package-smoke.log"
            with self.assertRaisesRegex(package_linux_x64.SmokeError, "it exited past its timeout"):
                package_linux_x64.run_launcher_check(fake_launcher("import time\ntime.sleep(30)\n"), log, timeout=1.0)

    def test_the_home_holds_the_smallest_complete_catalog(self) -> None:
        identity = json.loads((package_linux_x64.REPO / "engine/Ome.Engine/Profiles/himegari.json")
                              .read_text(encoding="utf-8"))["catalogIdentities"][0]
        with tempfile.TemporaryDirectory() as directory:
            home = package_linux_x64.write_smoke_home(Path(directory) / "home")
            catalog = home / "installs/himegari/SYS4INI.BIN"
            raw = catalog.read_bytes()
            index = parse_sys4ini.parse(catalog)
            profile = (home / "profiles/smoke/profile.toml").read_text(encoding="utf-8")
        self.assertEqual(identity["catalogRevision"], raw[:8].decode("ascii").rstrip())
        self.assertEqual(identity["title"], raw[8:0x12C].split(b"\0", 1)[0].decode("cp932"))
        self.assertEqual(["DATA.ALF"], index["archives"])
        self.assertEqual((1, 0), (index["file_count"], index["entry_count"]))  # one '@' placeholder
        self.assertEqual(index["decompressed_size"], int.from_bytes(raw[0x12C:0x130], "little"))
        self.assertEqual('ome_profile = 1\ngame = "himegari"\nmode = "native"\n\n[install]\n'
                         'root = "../../installs/himegari"\n', profile)


def run(returncode: int | None, output: str = "") -> package_linux_x64.SmokeRun:
    return package_linux_x64.SmokeRun(("OME",), returncode, output)


class HandOverTests(unittest.TestCase):
    """Launcher plan §9.4, spec §5.6: an argument keeps the engine; with none it starts the launcher beside it."""

    KEPT = run(2, f"ERROR: [startup] --game-root x {package_linux_x64.NO_GAME_ERROR}\n")

    def test_the_verdicts(self) -> None:
        problems = package_linux_x64.hand_over_problems
        self.assertEqual([], problems(self.KEPT, run(0), [], ["0\t"]))
        self.assertEqual(["with an argument the engine must report its own error and exit with 2; it exited with 0"],
                         problems(run(0), run(0), [], ["0\t"]))
        self.assertEqual(["with an argument the engine must report its own error and exit with 2; it exited with 2"],
                         problems(run(2, "some other error"), run(0), [], ["0\t"]))
        self.assertEqual(["with an argument the engine started the launcher: an argument must keep the engine"],
                         problems(self.KEPT, run(0), ["0\t"], ["0\t"]))
        self.assertEqual(["with no argument the engine exited past its timeout"],
                         problems(self.KEPT, run(None), [], ["0\t"]))
        self.assertEqual(["with no argument the engine did not start the launcher beside it"],
                         problems(self.KEPT, run(0), [], []))
        self.assertEqual(["with no argument the engine started the launcher 2 times"],
                         problems(self.KEPT, run(0), [], ["0\t", "0\t"]))
        self.assertEqual(["the launcher started with arguments: --launch smoke"],
                         problems(self.KEPT, run(0), [], ["2\t--launch smoke"]))


def fake_package(root: Path, mode: str) -> Path:
    """A package whose `OME` is a Python program acting as the engine's start does (`mode`: `hand-over` as the engine;
    `never` starts nothing; `always` starts the launcher with an argument too; `arguments` starts it with some)."""
    package = root / "OME-linux-x64"
    (package / "data").mkdir(parents=True)
    (package / "data/OME.dll").write_bytes(b"managed")
    (package / "OME.pck").write_bytes(b"pck")
    (package / "OME-Launcher").write_text("the real launcher\n", encoding="utf-8")
    # A shebang cannot carry a path with a space, so `OME` is a shell script running `OME.py` beside it.
    engine = package / "OME"
    engine.write_text(f'#!/bin/sh\nexec {shlex.quote(sys.executable)} "$0.py" "$@"\n', encoding="utf-8", newline="\n")
    engine.chmod(0o755)
    (package / "OME.py").write_text(
        "import subprocess, sys\nfrom pathlib import Path\n"
        f"MODE = {mode!r}\n"
        "user = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []\n"
        "launcher = Path(sys.argv[0]).parent / 'OME-Launcher'\n"
        "if MODE == 'always' or (not user and MODE != 'never'):\n"
        "    subprocess.Popen([str(launcher), *(['--launch', 'smoke'] if MODE == 'arguments' else [])])\n"
        "    print(f'[startup] no game was given or found here: started {launcher}', flush=True)\n"
        "if user:\n"
        "    print(f'ERROR: [startup] --game-root {user[-1]} does not contain SYS4INI.BIN', flush=True)\n"
        "    sys.exit(2)\n",
        encoding="utf-8", newline="\n")
    return package


@unittest.skipIf(os.name == "nt", "the hand-over runs a POSIX package and a shell script for its launcher")
class HandOverSmokeTests(unittest.TestCase):
    def hand_over(self, mode: str, timeout: float = 30.0) -> tuple[str | None, str]:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            package = fake_package(root, mode)
            log = root / "package-smoke.log"
            try:
                with package_linux_x64.released_copy(package) as copy:
                    package_linux_x64.run_hand_over(copy, log, timeout)
                error = None
            except package_linux_x64.SmokeError as failure:
                error = str(failure)
            # The package is left as it was (the copy's launcher, a hard link to it, is replaced, not written through).
            self.assertEqual("the real launcher\n", (package / "OME-Launcher").read_text(encoding="utf-8"))
            self.assertEqual(["OME-linux-x64", "package-smoke.log"], sorted(path.name for path in root.iterdir()))
            return error, log.read_text(encoding="utf-8")

    def test_the_engine_hands_over_from_a_folder_with_a_space(self) -> None:
        error, log = self.hand_over("hand-over")
        self.assertIsNone(error)
        self.assertIn("/OME package/OME --headless -- --game-root ", log)
        self.assertIn("started: 0 times with an argument; 1 times with none (argc 0\t)", log)
        self.assertEqual(package_linux_x64.HAND_OVER_MARKER, log.splitlines()[-1])

    def test_an_engine_that_never_hands_over_fails(self) -> None:
        # The smoke waits its whole timeout for the launcher that never starts.
        error, log = self.hand_over("never", timeout=3.0)
        self.assertIn("with no argument the engine did not start the launcher beside it", error)
        self.assertNotIn(package_linux_x64.HAND_OVER_MARKER, log)

    def test_an_engine_that_hands_over_despite_an_argument_fails(self) -> None:
        error, _ = self.hand_over("always")
        self.assertIn("with an argument the engine started the launcher", error)

    def test_a_launcher_started_with_arguments_fails(self) -> None:
        error, _ = self.hand_over("arguments")
        self.assertIn("the launcher started with arguments: --launch smoke", error)


class SmokeStepsTests(unittest.TestCase):
    def test_the_smoke_runs_its_three_steps_in_order_and_stops_at_a_failure(self) -> None:
        calls: list[tuple[str, object]] = []

        def step(name: str, fail: bool = False):
            def run(*args, **kwargs):
                calls.append((name, args[0]))
                if fail:
                    raise package_linux_x64.SmokeError(f"{name} failed")
            return run

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "OME-linux-x64"
            root.mkdir()
            argv = ["package_linux_x64.py", "smoke", str(root), "--log", str(Path(directory) / "smoke.log")]
            for failing, expected in ((None, 0), ("check", 1)):
                calls.clear()
                with patch.object(sys, "argv", argv), \
                        patch.object(package_linux_x64, "run_smoke", step("engine")), \
                        patch.object(package_linux_x64, "run_launcher_check", step("check", failing == "check")), \
                        patch.object(package_linux_x64, "run_hand_over", step("hand-over")), \
                        patch("builtins.print"):
                    self.assertEqual(expected, package_linux_x64.main())
                names = [name for name, _ in calls]
                self.assertEqual(["engine", "check", "hand-over"] if failing is None else ["engine", "check"], names)
            # The engine's smoke runs where the package is; the launcher's steps on a released copy elsewhere.
            self.assertEqual(root.resolve() / "OME", Path(calls[0][1][0]))
            launcher = Path(calls[1][1][0])
            self.assertEqual(("OME-Launcher", "OME package"), (launcher.name, launcher.parent.name))
            self.assertNotIn(Path(directory).resolve(), launcher.parents)

    def test_a_released_copy_is_outside_the_repository_and_removed_afterwards(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "OME-linux-x64"
            (package / "data").mkdir(parents=True)
            (package / "data/OME.dll").write_bytes(b"managed")
            with package_linux_x64.released_copy(package) as copy:
                self.assertEqual("OME package", copy.name)
                self.assertNotIn(package_linux_x64.REPO, copy.parents)
                self.assertEqual(b"managed", (copy / "data/OME.dll").read_bytes())
            self.assertFalse(copy.exists())
            # A temporary folder inside the repository would put the copy back in its toolchain's reach.
            inner = Path(directory) / "repository" / "tmp"
            inner.mkdir(parents=True)
            with patch.object(package_linux_x64, "REPO", inner.parent.resolve()), \
                    patch.object(tempfile, "tempdir", str(inner)):
                with self.assertRaisesRegex(package_linux_x64.SmokeError, "is inside the repository: set TMPDIR"):
                    with package_linux_x64.released_copy(package):
                        pass


class PackageLinuxX64Tests(unittest.TestCase):
    def test_public_output_names_use_ome_branding(self) -> None:
        self.assertEqual("OME-linux-x64", package_linux_x64.PACKAGE_NAME)
        self.assertIn("OME", package_linux_x64.REQUIRED_FILES)
        self.assertIn("OME.pck", package_linux_x64.REQUIRED_FILES)
        self.assertIn(ENGINE_LIBRARY, package_linux_x64.REQUIRED_FILES)
        # One NativeAOT library: no managed assembly, no .NET runtime, no dependency list in the data folder.
        self.assertEqual([], [name for name in package_linux_x64.REQUIRED_FILES if name.endswith((".dll", ".json"))])

    def test_verify_rejects_missing_and_windows_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export = make_export(root)
            verify(export, root)
            (export / package_linux_x64.REQUIRED_FILES[0]).unlink()
            with self.assertRaisesRegex(ValueError, "missing"):
                verify(export, root)
            (export / package_linux_x64.REQUIRED_FILES[0]).write_bytes(b"restored")
            (export / "OME").chmod(0o755)
            forbidden = export / package_linux_x64.FORBIDDEN_FILES[0]
            forbidden.write_bytes(b"windows")
            with self.assertRaisesRegex(ValueError, "Windows-only"):
                verify(export, root)

    def test_the_data_folder_holds_the_library_and_ffmpeg_only(self) -> None:
        # Any .NET runtime file sends Godot down its hostfxr route; a managed assembly, a dependency list or a stale
        # name has no place either.
        for name in ("libhostfxr.so", "libcoreclr.so", "System.Private.CoreLib.dll", "Ome.Engine.dll",
                     "OME.deps.json", "OME.dll", "Himegari.dll", "ome_movie_ffmpeg.dll", "OME.so.dbg"):
            with self.subTest(name), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                export = make_export(root)
                (export / package_linux_x64.DATA_DIRECTORY / name).write_bytes(b"stray")
                with self.assertRaisesRegex(ValueError, f"data folder holds files the NativeAOT engine does not "
                                                        f"ship: {re.escape(name)}$"):
                    verify(export, root)

    def test_the_engine_library_is_checked(self) -> None:
        cases = (
            ({}, None),
            ({"--dyn-syms": "    12: 0000000000000000     0 FUNC    GLOBAL DEFAULT  UND godotsharp_game_main_init\n"},
             "does not export godotsharp_game_main_init: OME.so; it is not a NativeAOT build"),
            ({"--dynamic": READELF["--dynamic"] + " 0x1 (NEEDED)             Shared library: [libstdc++.so.6]\n"},
             "needs libraries outside glibc: libstdc\\+\\+.so.6"),
            # Linked on the runner's own glibc instead of in the manylinux_2_28 container.
            ({"--version-info": READELF["--version-info"] + "  0x0040:   Name: GLIBC_2.29  Flags: none  Version: 10\n"},
             "needs glibc 2.29, newer than the package's floor 2.28"),
            ({"--version-info": "  0x0010:   Name: V1.0  Flags: none  Version: 2\n"},
             "could not determine the glibc"),
        )
        for outputs, message in cases:
            with self.subTest(message), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                export = make_export(root)
                library = export / ENGINE_LIBRARY
                with patch.object(package_linux_x64.subprocess, "run", side_effect=fake_readelf(outputs)):
                    if message is None:
                        self.assertEqual({"export": "godotsharp_game_main_init", "glibc": "2.28",
                                          "needed": sorted(package_linux_x64.GLIBC_LIBRARIES)},
                                         package_linux_x64.verify_engine_library(library))
                        continue
                    with self.assertRaisesRegex(ValueError, message):
                        package_linux_x64.verify_engine_library(library)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export = make_export(root)
            for content in (b"fixture", None):
                if content is None:
                    write_elf(export / ENGINE_LIBRARY, machine=183)  # AArch64
                else:
                    (export / ENGINE_LIBRARY).write_bytes(content)
                with self.assertRaisesRegex(ValueError, "not an x86-64 ELF file: OME.so"):
                    verify(export, root)

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
                    verify(export, root)

    @unittest.skipIf(package_linux_x64.os.name == "nt", "executable-bit gate is POSIX-only")
    def test_verify_requires_executable_bit_on_posix(self) -> None:
        for program in ("OME", "OME-Launcher"):
            with self.subTest(program), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                export = make_export(root)
                (export / program).chmod(0o644)
                with self.assertRaisesRegex(ValueError, f"executable bit is not set: .*{program}"):
                    verify(export, root)

    def test_verify_requires_notices_for_every_shipped_package(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            temporary_root = Path(temporary)
            export = make_export(temporary_root)
            notices = temporary_root / "THIRD_PARTY_NOTICES.md"
            notices.write_text("`GodotSharp`\n", encoding="utf-8")
            with patch.object(package_linux_x64, "REPO", temporary_root):
                with self.assertRaisesRegex(ValueError, "Microsoft.NETCore.App, Tomlyn"):
                    verify(export, temporary_root)
                # The engine's packages named, the launcher's list is read too: from the copy its publish kept.
                notices.write_text("`GodotSharp` `Tomlyn` `Microsoft.NETCore.App`\n", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "launcher's shipped NuGet packages: Avalonia, "
                                                        "SkiaSharp.NativeAssets.Linux"):
                    verify(export, temporary_root)
                with self.assertRaisesRegex(ValueError, "launcher's dependency list was not found"):
                    verify(export, temporary_root, temporary_root / "elsewhere.deps.json")
                engine_deps(temporary_root).unlink()
                with self.assertRaisesRegex(ValueError, "engine's dependency list was not found"):
                    verify(export, temporary_root)

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
            ), patch.object(package_linux_x64, "PINNED_NOTICES", pinned), patch.object(
                package_linux_x64.subprocess, "run", side_effect=fake_readelf()
            ):
                (temporary_root / "LICENSE").write_text("MIT\n", encoding="utf-8")
                (temporary_root / "THIRD_PARTY_NOTICES.md").write_text(NOTICES, encoding="utf-8")
                package_linux_x64.create_package(export, output, metadata, 123456789, deps, engine_deps(temporary_root))
                first = hashlib.sha256(
                    (output / f"{package_linux_x64.PACKAGE_NAME}.tar.gz").read_bytes()
                ).hexdigest()
                package_root, archive = package_linux_x64.create_package(
                    export, output, metadata, 123456789, deps, engine_deps(temporary_root)
                )
                second = hashlib.sha256(archive.read_bytes()).hexdigest()
            self.assertEqual(first, second)
            self.assertTrue((package_root / "LICENSE").is_file())
            self.assertEqual(b"godot\n", (package_root / "Godot-LICENSE.txt").read_bytes())
            self.assertTrue((package_root / "BUILD-INFO.json").is_file())
            checksums = (package_root / "SHA256SUMS").read_text(encoding="utf-8").splitlines()
            names = [line.split("  ", 1)[1] for line in checksums]
            for name in ("OME", ENGINE_LIBRARY, "THIRD_PARTY_NOTICES.md", *package_linux_x64.LAUNCHER.files()):
                self.assertIn(name, names)


if __name__ == "__main__":
    unittest.main()
