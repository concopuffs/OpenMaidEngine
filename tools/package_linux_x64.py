#!/usr/bin/env python3
"""Verify and package the Linux x64 Godot export, and run its package smoke."""
from __future__ import annotations

import argparse
import contextlib
import gzip
import hashlib
import json
import os
import queue
import re
import shlex
import shutil
import stat
import struct
import subprocess
import tarfile
import tempfile
import threading
import time
from collections.abc import Iterator
from dataclasses import dataclass
from pathlib import Path

import package_notices
import publish_launcher

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
MANAGED_DIRECTORY = "data_OME_linuxbsd_x86_64"
PACKAGE_NAME = "OME-linux-x64"
# The launcher beside the engine (launcher spec §10, plan §9.3): its program, natives and notices.
LAUNCHER = publish_launcher.LAYOUTS["linux-x64"]
REQUIRED_FILES = (
    "OME",
    "OME.pck",
    f"{MANAGED_DIRECTORY}/OME.dll",
    f"{MANAGED_DIRECTORY}/Ome.Engine.dll",
    f"{MANAGED_DIRECTORY}/Ome.Engine.Devices.dll",
    f"{MANAGED_DIRECTORY}/Ome.Engine.Vm.dll",
    f"{MANAGED_DIRECTORY}/Ome.Engine.Frontend.dll",
    f"{MANAGED_DIRECTORY}/{package_notices.DEPS_FILE}",
    f"{MANAGED_DIRECTORY}/Microsoft.NETCore.App.Runtime.linux-x64-LICENSE.TXT",
    f"{MANAGED_DIRECTORY}/Microsoft.NETCore.App.Runtime.linux-x64-THIRD-PARTY-NOTICES.TXT",
    f"{MANAGED_DIRECTORY}/libome_movie_ffmpeg.so",
    f"{MANAGED_DIRECTORY}/libavformat.so.62",
    f"{MANAGED_DIRECTORY}/libavcodec.so.62",
    f"{MANAGED_DIRECTORY}/libavutil.so.60",
    f"{MANAGED_DIRECTORY}/libswscale.so.9",
    f"{MANAGED_DIRECTORY}/libswresample.so.6",
    f"{MANAGED_DIRECTORY}/FFmpeg-LICENSE.txt",
    f"{MANAGED_DIRECTORY}/FFmpeg-SOURCE.txt",
    *LAUNCHER.files(),
)
FORBIDDEN_FILES = (
    f"{MANAGED_DIRECTORY}/Ome.Engine.Text.Windows.dll",
    f"{MANAGED_DIRECTORY}/ome_movie_ffmpeg.dll",
    f"{MANAGED_DIRECTORY}/avformat-62.dll",
    publish_launcher.LAYOUTS["win-x64"].program,
    *publish_launcher.LAYOUTS["win-x64"].natives,
)
LEGACY_FILES = (f"{MANAGED_DIRECTORY}/Himegari.dll",)
PROJECT_FILES = ("LICENSE", "README.md", "THIRD_PARTY_NOTICES.md")
SMOKE_MARKER = "PACKAGE SMOKE OK: opcodes=548 ffmpeg-abi=3"
SMOKE_TIMEOUT_S = 120.0
# Godot's notices and the license of the font OME.dll embeds.
PINNED_NOTICES = package_notices.pinned_notices() + package_notices.pinned_notices(package_notices.NOTO_NOTICES)


def _resolved_directory(path: Path, label: str) -> Path:
    resolved = path.resolve()
    if not resolved.is_dir():
        raise ValueError(f"{label} directory was not found: {resolved}")
    return resolved


def verify_export(export_directory: Path, launcher_deps: Path | None = None) -> Path:
    """Check the export's files, and that THIRD_PARTY_NOTICES.md names every package the engine and the launcher ship
    (`launcher_deps`: the dependency list the launcher's publish kept, by default publish_launcher's)."""
    export_directory = _resolved_directory(export_directory, "Linux export")
    missing = [name for name in REQUIRED_FILES if not (export_directory / name).is_file()]
    if missing:
        raise ValueError("Linux export is incomplete; missing: " + ", ".join(missing))
    forbidden = [name for name in FORBIDDEN_FILES if (export_directory / name).exists()]
    if forbidden:
        raise ValueError("Linux export contains Windows-only files: " + ", ".join(forbidden))
    symbols = sorted(path.relative_to(export_directory).as_posix() for path in export_directory.rglob("*.pdb"))
    if symbols:
        raise ValueError("Linux export contains debug symbols: " + ", ".join(symbols))
    legacy = [name for name in LEGACY_FILES if (export_directory / name).exists()]
    if legacy:
        raise ValueError("Linux export contains legacy-branded files: " + ", ".join(legacy))
    package_notices.verify_dependency_notices(
        export_directory / MANAGED_DIRECTORY, REPO / "THIRD_PARTY_NOTICES.md"
    )
    package_notices.verify_launcher_notices(
        launcher_deps or publish_launcher.dependency_list(LAUNCHER.runtime), REPO / "THIRD_PARTY_NOTICES.md"
    )
    for executable in (export_directory / "OME", export_directory / LAUNCHER.program):
        if os.name != "nt" and not os.access(executable, os.X_OK):
            raise ValueError(f"Linux export executable bit is not set: {executable}")
    return export_directory


def _git(*arguments: str) -> str:
    result = subprocess.run(
        ("git", *arguments), cwd=REPO, check=True, capture_output=True, text=True
    )
    return result.stdout.strip()


def source_date_epoch() -> int:
    configured = os.environ.get("SOURCE_DATE_EPOCH")
    value = configured or _git("show", "-s", "--format=%ct", "HEAD")
    try:
        epoch = int(value)
    except ValueError as error:
        raise ValueError(f"invalid SOURCE_DATE_EPOCH: {value!r}") from error
    if epoch < 0:
        raise ValueError("SOURCE_DATE_EPOCH must not be negative")
    return epoch


def build_metadata(epoch: int) -> dict[str, object]:
    with (HERE / "godot-linux-x64.json").open(encoding="utf-8") as stream:
        godot = json.load(stream)
    with (REPO / "native/ome_movie_ffmpeg/dependency-linux-x64.json").open(
        encoding="utf-8"
    ) as stream:
        ffmpeg = json.load(stream)
    dirty = bool(_git("status", "--porcelain", "--untracked-files=normal"))
    return {
        "schema_version": 1,
        "project": "Open Maid Engine",
        "target": "linux-x64",
        "source_commit": _git("rev-parse", "HEAD"),
        "source_dirty": dirty,
        "source_date_epoch": epoch,
        "godot": {
            "version": godot["godot_version"],
            "editor_archive_sha256": godot["editor"]["sha256"],
            "release_template_sha256": godot["templates"]["members"][0]["sha256"],
        },
        "ffmpeg": {
            "provider": ffmpeg["provider"],
            "version": ffmpeg["ffmpeg_version"],
            "archive_sha256": ffmpeg["sha256"],
            "minimum_glibc": ffmpeg["minimum_glibc"],
        },
    }


def _hash(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _write_checksums(package_root: Path) -> None:
    checksum_path = package_root / "SHA256SUMS"
    files = sorted(
        path for path in package_root.rglob("*")
        if path.is_file() and path != checksum_path
    )
    lines = [f"{_hash(path)}  {path.relative_to(package_root).as_posix()}" for path in files]
    checksum_path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def _tar_info(info: tarfile.TarInfo, epoch: int) -> tarfile.TarInfo:
    info.uid = 0
    info.gid = 0
    info.uname = "root"
    info.gname = "root"
    info.mtime = epoch
    if info.isdir():
        info.mode = 0o755
    elif info.isfile():
        info.mode = 0o755 if info.mode & stat.S_IXUSR else 0o644
    return info


def create_package(
    export_directory: Path,
    output_directory: Path,
    metadata: dict[str, object],
    epoch: int,
    launcher_deps: Path | None = None,
) -> tuple[Path, Path]:
    export_directory = verify_export(export_directory, launcher_deps)
    output_directory = output_directory.resolve()
    expected_parent = (REPO / "build/package").resolve()
    if output_directory != expected_parent:
        raise ValueError(
            f"refusing to replace unexpected package directory: {output_directory}"
        )
    staging_parent = output_directory / "staging"
    package_root = staging_parent / PACKAGE_NAME
    archive_path = output_directory / f"{PACKAGE_NAME}.tar.gz"
    if staging_parent.exists():
        shutil.rmtree(staging_parent)
    staging_parent.mkdir(parents=True)
    shutil.copytree(export_directory, package_root, copy_function=shutil.copy2)
    for name in PROJECT_FILES:
        source = REPO / name
        if not source.is_file():
            raise ValueError(f"package notice file was not found: {source}")
        shutil.copy2(source, package_root / name)
    package_notices.copy_pinned_notices(PINNED_NOTICES, package_root)
    (package_root / "BUILD-INFO.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    _write_checksums(package_root)

    output_directory.mkdir(parents=True, exist_ok=True)
    temporary_archive = archive_path.with_suffix(archive_path.suffix + ".part")
    with temporary_archive.open("wb") as raw:
        with gzip.GzipFile(fileobj=raw, mode="wb", filename="", mtime=epoch) as compressed:
            with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as archive:
                paths = [package_root, *sorted(package_root.rglob("*"))]
                for path in paths:
                    arcname = path.relative_to(staging_parent).as_posix()
                    archive.add(
                        path,
                        arcname=arcname,
                        recursive=False,
                        filter=lambda info, fixed_epoch=epoch: _tar_info(info, fixed_epoch),
                    )
    os.replace(temporary_archive, archive_path)
    return package_root, archive_path


class SmokeError(RuntimeError):
    pass


def run_smoke(command: list[str], marker: str, log: Path, timeout: float = SMOKE_TIMEOUT_S) -> None:
    """Run a package smoke that holds until its standard input closes (`--package-smoke --hold`). Its marker must reach
    the pipe while it still runs: the launcher captures an engine's output live, and an engine that flushes only at
    exit loses its lines when it is killed (launcher plan §4.12: project.godot's flush_stdout_on_print). Then its
    standard input is closed and it must exit 0. Everything it printed goes to `log`."""
    process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    lines: queue.Queue[str | None] = queue.Queue()

    def read() -> None:
        assert process.stdout is not None
        for raw in process.stdout:
            lines.put(raw.decode("utf-8", errors="replace").replace("\r\n", "\n"))
        lines.put(None)

    reader = threading.Thread(target=read, daemon=True)
    reader.start()
    output: list[str] = []
    try:
        deadline = time.monotonic() + timeout
        while True:
            try:
                line = lines.get(timeout=max(0.0, deadline - time.monotonic()))
            except queue.Empty:
                raise SmokeError(f"no '{marker}' line reached the pipe within {timeout:.0f} s while the engine ran: "
                                 "its output is not flushed") from None
            if line is None:
                raise SmokeError(f"the engine's output ended ({process.wait()}) before '{marker}'")
            output.append(line)
            if marker in line:
                if process.poll() is not None:
                    raise SmokeError(f"'{marker}' arrived only as the engine exited ({process.returncode})")
                break
        assert process.stdin is not None
        process.stdin.close()
        try:
            returncode = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            raise SmokeError(f"the engine did not exit within {timeout:.0f} s of its standard input closing") from None
        if returncode != 0:
            raise SmokeError(f"the engine exited with {returncode}")
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()
        reader.join(timeout)
        # Whatever reached the pipe goes to the log, on every path.
        while not lines.empty():
            if (line := lines.get_nowait()) is not None:
                output.append(line)
        for stream in (process.stdin, process.stdout):
            if stream is not None and not stream.closed:
                stream.close()
        log.write_text("".join(output), encoding="utf-8", newline="\n")


def smoke_command(package_root: Path) -> list[str]:
    return [str(package_root / "OME"), "--headless", "--", "--package-smoke", "--hold"]


# The smoke's launcher steps (launcher plan §9.4): the launcher's command-line check with no display (gate 11), and the
# engine's hand-over to the launcher beside it (launcher spec §5.6).
LAUNCHER_CHECK_KINDS = ("Install", "Fingerprint", "Root", "Packages", "Link", "Lock")
LAUNCHER_CHECK_MARKER = f"LAUNCHER CHECK OK: checks={len(LAUNCHER_CHECK_KINDS)}"
HAND_OVER_MARKER = "HAND-OVER OK"
# Every line an accepted smoke log holds; the release publisher requires them all.
SMOKE_MARKERS = (SMOKE_MARKER, LAUNCHER_CHECK_MARKER, HAND_OVER_MARKER)
SMOKE_PROFILE = "smoke"
SMOKE_GAME = "himegari"
# One line of `OME-Launcher --check`: the status padded to eight, then `Kind: message`.
CHECK_LINE = re.compile(r"^(passed|warning|FAILED|not run)\s+([A-Za-z]+): ")
# What the engine reports when an argument keeps it and names a folder with no game (GameRootSelection).
NO_GAME_ERROR = "does not contain SYS4INI.BIN"


@dataclass(frozen=True)
class SmokeRun:
    """One program a smoke step ran: what it printed (standard error merged), and its exit code, None on a timeout."""

    command: tuple[str, ...]
    returncode: int | None
    output: str

    def exit(self) -> str:
        return "past its timeout" if self.returncode is None else f"with {self.returncode}"


def _cp932_field(text: str, size: int) -> bytes:
    raw = text.encode("cp932")
    if len(raw) > size:
        raise ValueError(f"{text!r} does not fit in {size} bytes")
    return raw + bytes(size - len(raw))


def write_smoke_home(home: Path, game: str = SMOKE_GAME) -> Path:
    """An OME home for `OME-Launcher --check smoke --home HOME`: `profiles/smoke/` in native mode on
    `installs/<game>/`. Its SYS4INI.BIN is the smallest complete catalog:
    - the header of the game's first catalog identity (`engine/Ome.Engine/Profiles/<game>.json`);
    - a directory of one archive and one placeholder entry, then an empty trailer as the engine reads one (no
      metadata, a zero-length settings block, nothing after it), packed as LZSS literals.
    A header alone is refused, since its expanded size 0 fails (launcher plan §9). Returns the home."""
    identity = json.loads((REPO / f"engine/Ome.Engine/Profiles/{game}.json").read_text(encoding="utf-8"))
    identity = identity["catalogIdentities"][0]
    directory = (struct.pack("<I", 1) + _cp932_field("DATA.ALF", 256) + struct.pack("<I", 1)
                 + _cp932_field("@", 64) + bytes(16) + struct.pack("<II", 0, 0))
    packed = bytearray()
    for offset in range(0, len(directory), 8):
        chunk = directory[offset:offset + 8]
        packed.append((1 << len(chunk)) - 1)  # every flag bit set: eight literal bytes follow
        packed += chunk
    header = bytearray(0x138)
    header[0:8] = identity["catalogRevision"].ljust(8).encode("ascii")
    header[8:0x12C] = _cp932_field(identity["title"], 0x12C - 8)
    struct.pack_into("<I", header, 0x12C, len(directory))
    struct.pack_into("<I", header, 0x134, len(packed))
    install = home / "installs" / game
    install.mkdir(parents=True)
    (install / "SYS4INI.BIN").write_bytes(bytes(header) + bytes(packed))
    profile = home / "profiles" / SMOKE_PROFILE
    profile.mkdir(parents=True)
    (profile / "profile.toml").write_text(
        f'ome_profile = 1\ngame = "{game}"\nmode = "native"\n\n[install]\nroot = "../../installs/{game}"\n',
        encoding="utf-8", newline="\n")
    return home


def smoke_environment(scratch: Path) -> dict[str, str]:
    """The environment of the launcher steps: the job's, with no display, and HOME and the XDG folders inside
    `scratch`. So a step neither reads nor writes this machine's own state: the launcher's machine folder and the
    engine's logs live in Godot's user folder, under XDG_DATA_HOME."""
    user = scratch / "user"
    environment = {key: value for key, value in os.environ.items() if key not in ("DISPLAY", "WAYLAND_DISPLAY")}
    environment.update(HOME=str(user), XDG_DATA_HOME=str(user / ".local/share"),
                       XDG_CONFIG_HOME=str(user / ".config"), XDG_CACHE_HOME=str(user / ".cache"))
    for name in ("HOME", "XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_CACHE_HOME"):
        Path(environment[name]).mkdir(parents=True, exist_ok=True)
    return environment


def _run_step(command: list[str], cwd: Path, environment: dict[str, str], timeout: float) -> SmokeRun:
    try:
        completed = subprocess.run(command, cwd=cwd, env=environment, stdin=subprocess.DEVNULL,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout, check=False)
        returncode, output = completed.returncode, completed.stdout
    except subprocess.TimeoutExpired as error:
        returncode, output = None, error.output or b""
    return SmokeRun(tuple(command), returncode,
                    output.decode("utf-8", errors="replace").replace("\r\n", "\n"))


def _append(log: Path, *parts: str) -> None:
    with log.open("a", encoding="utf-8", newline="\n") as stream:
        for part in parts:
            stream.write(part if part.endswith("\n") or not part else part + "\n")


def launcher_check_problems(run: SmokeRun) -> list[str]:
    """What is wrong with a `--check` run: it must exit 0 with each of the six checks passed, once."""
    problems = []
    if run.returncode != 0:
        problems.append(f"it exited {run.exit()}")
    checks = sorted((match.group(2), match.group(1)) for line in run.output.splitlines()
                    if (match := CHECK_LINE.match(line)))
    if checks != sorted((kind, "passed") for kind in LAUNCHER_CHECK_KINDS):
        found = ", ".join(f"{kind} {status}" for kind, status in checks) or "none"
        problems.append(f"its checks are not {', '.join(LAUNCHER_CHECK_KINDS)}, each passed; it printed: {found}")
    return problems


def run_launcher_check(launcher: list[str], log: Path, timeout: float = SMOKE_TIMEOUT_S) -> None:
    """The smoke's second step (gate 11): `launcher --check smoke --home HOME` on write_smoke_home's home, with no
    display. Appends what it printed to `log`, then LAUNCHER_CHECK_MARKER."""
    with tempfile.TemporaryDirectory(prefix="ome-launcher-check-") as temporary:
        scratch = Path(temporary)
        home = write_smoke_home(scratch / "home")
        run = _run_step([*launcher, "--check", SMOKE_PROFILE, "--home", str(home)], scratch,
                        smoke_environment(scratch), timeout)
    _append(log, f"== the launcher's check, with no display: {' '.join(run.command)}", run.output)
    if problems := launcher_check_problems(run):
        _append(log, "the launcher's check failed: " + "; ".join(problems))
        raise SmokeError("the launcher's check failed: " + "; ".join(problems))
    _append(log, LAUNCHER_CHECK_MARKER)


def write_launcher_stub(path: Path, record: Path) -> None:
    """The hand-over's stand-in for the launcher: it appends a line to `record` each time it runs, its argument count
    and its arguments, tab-separated."""
    path.write_text(
        "#!/bin/sh\n# The package smoke's stand-in for OME-Launcher (launcher plan §9.4): it records how it started.\n"
        f"printf '%s\\t%s\\n' \"$#\" \"$*\" >> {shlex.quote(str(record))}\n", encoding="utf-8", newline="\n")
    path.chmod(0o755)


def _link_or_copy(source: str, destination: str) -> None:
    try:
        os.link(source, destination)
    except OSError:
        shutil.copy2(source, destination)


def hand_over_problems(with_argument: SmokeRun, without: SmokeRun, started_with_argument: list[str],
                       started_without: list[str]) -> list[str]:
    """What is wrong with the hand-over: an argument keeps the engine, which reports its own error and exits 2; with
    none the engine starts the launcher once, with no arguments, and exits 0. The `started_*` lists are the stub's
    records each run added."""
    problems = []
    if with_argument.returncode != 2 or NO_GAME_ERROR not in with_argument.output:
        problems.append(f"with an argument the engine must report its own error and exit with 2; it exited "
                        f"{with_argument.exit()}")
    if started_with_argument:
        problems.append("with an argument the engine started the launcher: an argument must keep the engine")
    if without.returncode != 0:
        problems.append(f"with no argument the engine exited {without.exit()}")
    if not started_without:
        problems.append("with no argument the engine did not start the launcher beside it")
    elif len(started_without) > 1:
        problems.append(f"with no argument the engine started the launcher {len(started_without)} times")
    elif started_without[0].split("\t", 1)[0] != "0":
        problems.append(f"the launcher started with arguments: {started_without[0].split(chr(9), 1)[-1]}")
    return problems


def _launcher_runs(record: Path, deadline: float) -> list[str]:
    """The stub's records, waiting until `deadline` for a first one."""
    while not record.is_file() and time.monotonic() < deadline:
        time.sleep(0.05)
    return record.read_text(encoding="utf-8").splitlines() if record.is_file() else []


@contextlib.contextmanager
def released_copy(package_root: Path) -> Iterator[Path]:
    """The package as a player has it: copied to `OME package`, a folder whose name has a space, in a temporary
    folder outside the repository. A program inside a checkout links with the repository's toolchain
    (`ModLinkBase.ForGame`), so the launcher's steps run on this copy, not where the package was staged. Its files are
    hard links where the filesystem allows; the copy is removed afterwards."""
    package_root = _resolved_directory(package_root, "package")
    with tempfile.TemporaryDirectory(prefix="ome smoke ") as temporary:
        scratch = Path(temporary).resolve()
        if scratch == REPO or REPO in scratch.parents:
            raise SmokeError(f"the temporary folder {scratch} is inside the repository: set TMPDIR to a folder "
                             "outside it")
        copy = scratch / "OME package"
        shutil.copytree(package_root, copy, symlinks=True, copy_function=_link_or_copy)
        yield copy


def run_hand_over(folder: Path, log: Path, timeout: float = SMOKE_TIMEOUT_S) -> None:
    """The smoke's third step (launcher spec §5.6), on a released_copy, whose launcher it replaces with
    write_launcher_stub's stub; both runs start from a folder with no game.
    - `OME --headless -- --game-root <that folder>` must keep the engine: it reports its error and exits 2.
    - `OME --headless` must start the launcher once, with no arguments, and exit 0.
    Appends both runs and the stub's records to `log`, then HAND_OVER_MARKER."""
    folder = _resolved_directory(folder, "package copy")
    launcher = folder / LAUNCHER.program
    launcher.unlink(missing_ok=True)  # a hard link to the package's own launcher, never written through
    with tempfile.TemporaryDirectory(prefix="ome hand-over ") as temporary:
        scratch = Path(temporary)
        record = scratch / "launcher-runs.txt"
        write_launcher_stub(launcher, record)
        elsewhere = scratch / "no game here"
        elsewhere.mkdir()
        # Godot can change the directory before the engine starts; the engine reads the caller's from PWD.
        environment = dict(smoke_environment(scratch), PWD=str(elsewhere))
        engine = str(folder / "OME")
        with_argument = _run_step([engine, "--headless", "--", "--game-root", str(elsewhere)], elsewhere,
                                  environment, timeout)
        # The engine starts the launcher and exits without waiting, so the run with none waits for its record.
        started_with_argument = record.read_text(encoding="utf-8").splitlines() if record.is_file() else []
        without = _run_step([engine, "--headless"], elsewhere, environment, timeout)
        started_without = _launcher_runs(record, time.monotonic() + timeout)[len(started_with_argument):]
    _append(log, f"== the hand-over, with an argument: {' '.join(with_argument.command)} (exited "
            f"{with_argument.exit()})", with_argument.output,
            f"== the hand-over, with none: {' '.join(without.command)} (exited {without.exit()})", without.output,
            "the launcher's stand-in started: " + "; ".join(
                f"{len(runs)} times {label}" + "".join(f" (argc {run})" for run in runs)
                for label, runs in (("with an argument", started_with_argument), ("with none", started_without))))
    if problems := hand_over_problems(with_argument, without, started_with_argument, started_without):
        _append(log, "the hand-over failed: " + "; ".join(problems))
        raise SmokeError("the hand-over failed: " + "; ".join(problems))
    _append(log, HAND_OVER_MARKER)


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    verify = subparsers.add_parser("verify")
    verify.add_argument("export_directory", type=Path)
    verify.add_argument("--launcher-deps", type=Path, help="the launcher's kept dependency list")
    package = subparsers.add_parser("package")
    package.add_argument("export_directory", type=Path)
    package.add_argument("--launcher-deps", type=Path, help="the launcher's kept dependency list")
    package.add_argument("--output-directory", type=Path, default=REPO / "build/package")
    smoke = subparsers.add_parser("smoke")
    smoke.add_argument("package_root", type=Path)
    smoke.add_argument("--log", type=Path, required=True)
    args = parser.parse_args()

    if args.command == "verify":
        print(verify_export(args.export_directory, args.launcher_deps))
        return 0
    if args.command == "smoke":
        # Three steps, each adding to the log (launcher plan §9.4): the engine, the launcher's check, the hand-over.
        package_root = args.package_root.resolve()  # the launcher's steps run from scratch folders
        try:
            run_smoke(smoke_command(package_root), SMOKE_MARKER, args.log)
            # The launcher's steps run on the package as a player has it: outside the repository.
            with released_copy(package_root) as copy:
                run_launcher_check([str(copy / LAUNCHER.program)], args.log)
                run_hand_over(copy, args.log)
        except SmokeError as error:
            print(f"package smoke: {error}", flush=True)
            return 1
        return 0
    epoch = source_date_epoch()
    package_root, archive_path = create_package(
        args.export_directory, args.output_directory, build_metadata(epoch), epoch, args.launcher_deps
    )
    print(package_root)
    print(archive_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
