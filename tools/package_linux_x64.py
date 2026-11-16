#!/usr/bin/env python3
"""Verify and package the Linux x64 Godot export, and run its package smoke."""
from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
import queue
import shutil
import stat
import subprocess
import tarfile
import threading
import time
from pathlib import Path

import package_notices

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
MANAGED_DIRECTORY = "data_OME_linuxbsd_x86_64"
PACKAGE_NAME = "OME-linux-x64"
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
)
FORBIDDEN_FILES = (
    f"{MANAGED_DIRECTORY}/Ome.Engine.Text.Windows.dll",
    f"{MANAGED_DIRECTORY}/ome_movie_ffmpeg.dll",
    f"{MANAGED_DIRECTORY}/avformat-62.dll",
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


def verify_export(export_directory: Path) -> Path:
    export_directory = _resolved_directory(export_directory, "Linux export")
    missing = [name for name in REQUIRED_FILES if not (export_directory / name).is_file()]
    if missing:
        raise ValueError("Linux export is incomplete; missing: " + ", ".join(missing))
    forbidden = [name for name in FORBIDDEN_FILES if (export_directory / name).exists()]
    if forbidden:
        raise ValueError("Linux export contains Windows-only files: " + ", ".join(forbidden))
    legacy = [name for name in LEGACY_FILES if (export_directory / name).exists()]
    if legacy:
        raise ValueError("Linux export contains legacy-branded files: " + ", ".join(legacy))
    package_notices.verify_dependency_notices(
        export_directory / MANAGED_DIRECTORY, REPO / "THIRD_PARTY_NOTICES.md"
    )
    executable = export_directory / "OME"
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
) -> tuple[Path, Path]:
    export_directory = verify_export(export_directory)
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


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    verify = subparsers.add_parser("verify")
    verify.add_argument("export_directory", type=Path)
    package = subparsers.add_parser("package")
    package.add_argument("export_directory", type=Path)
    package.add_argument("--output-directory", type=Path, default=REPO / "build/package")
    smoke = subparsers.add_parser("smoke")
    smoke.add_argument("package_root", type=Path)
    smoke.add_argument("--log", type=Path, required=True)
    args = parser.parse_args()

    if args.command == "verify":
        print(verify_export(args.export_directory))
        return 0
    if args.command == "smoke":
        try:
            run_smoke(smoke_command(args.package_root), SMOKE_MARKER, args.log)
        except SmokeError as error:
            print(f"package smoke: {error}", flush=True)
            return 1
        return 0
    epoch = source_date_epoch()
    package_root, archive_path = create_package(
        args.export_directory, args.output_directory, build_metadata(epoch), epoch
    )
    print(package_root)
    print(archive_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
