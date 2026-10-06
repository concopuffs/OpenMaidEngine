#!/usr/bin/env python3
"""Verify and package the Linux x64 Godot export."""
from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
import shutil
import stat
import subprocess
import tarfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
MANAGED_DIRECTORY = "data_OME_linuxbsd_x86_64"
PACKAGE_NAME = "OME-linux-x64"
REQUIRED_FILES = (
    "OME",
    "OME.pck",
    f"{MANAGED_DIRECTORY}/OME.dll",
    f"{MANAGED_DIRECTORY}/Age.Engine.dll",
    f"{MANAGED_DIRECTORY}/Age.Engine.Frontend.dll",
    f"{MANAGED_DIRECTORY}/libage_movie_ffmpeg.so",
    f"{MANAGED_DIRECTORY}/libavformat.so.62",
    f"{MANAGED_DIRECTORY}/libavcodec.so.62",
    f"{MANAGED_DIRECTORY}/libavutil.so.60",
    f"{MANAGED_DIRECTORY}/libswscale.so.9",
    f"{MANAGED_DIRECTORY}/libswresample.so.6",
    f"{MANAGED_DIRECTORY}/FFmpeg-LICENSE.txt",
    f"{MANAGED_DIRECTORY}/FFmpeg-SOURCE.txt",
)
FORBIDDEN_FILES = (
    f"{MANAGED_DIRECTORY}/Age.Engine.Text.Windows.dll",
    f"{MANAGED_DIRECTORY}/age_movie_ffmpeg.dll",
    f"{MANAGED_DIRECTORY}/avformat-62.dll",
)
LEGACY_FILES = (f"{MANAGED_DIRECTORY}/Himegari.dll",)
PROJECT_FILES = ("LICENSE", "README.md", "THIRD_PARTY_NOTICES.md")


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
    with (REPO / "native/age_movie_ffmpeg/dependency-linux-x64.json").open(
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


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    verify = subparsers.add_parser("verify")
    verify.add_argument("export_directory", type=Path)
    package = subparsers.add_parser("package")
    package.add_argument("export_directory", type=Path)
    package.add_argument("--output-directory", type=Path, default=REPO / "build/package")
    args = parser.parse_args()

    if args.command == "verify":
        print(verify_export(args.export_directory))
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
