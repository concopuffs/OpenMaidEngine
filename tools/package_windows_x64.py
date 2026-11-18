#!/usr/bin/env python3
"""Verify and package the Windows x64 Godot export without executing it."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import time
import zipfile
from pathlib import Path
from typing import Any

import package_notices
import publish_engine
import publish_launcher
import verify_windows_native


HERE = Path(__file__).resolve().parent
REPO = HERE.parent
DATA_DIRECTORY = "data_OME_windows_x86_64"
PACKAGE_NAME = "OME-windows-x64"
RUNTIME = "win-x64"
# The launcher beside the engine (launcher spec §10, plan §9.3): its program, natives and notices.
LAUNCHER = publish_launcher.LAYOUTS[RUNTIME]
# The data folder Godot loads the engine from, exactly: the engine's NativeAOT library with the notices of the .NET
# runtime it holds (tools/publish_engine.py), and the FFmpeg bundle. A .NET runtime file there would send Godot down its
# hostfxr route (docs/platform-portability.md, "Fewer files in a package").
DATA_FILES = (*publish_engine.publish_files(RUNTIME), *verify_windows_native.REQUIRED_FILES)
REQUIRED_FILES = (
    "OME.exe",
    "OME.pck",
    *(f"{DATA_DIRECTORY}/{name}" for name in DATA_FILES),
    *LAUNCHER.files(),
)
FORBIDDEN_FILES = (
    "OME",
    publish_launcher.LAYOUTS["linux-x64"].program,
)
PROJECT_FILES = ("LICENSE", "README.md", "THIRD_PARTY_NOTICES.md")
# Godot's notices, the MinGW-w64 runtime's, which the shim and FFmpeg DLLs link statically
# (tools/mingw_runtime_notices.py checks those links), and the license of the font OME.dll embeds.
PINNED_NOTICES = (
    package_notices.pinned_notices()
    + package_notices.pinned_notices(package_notices.MINGW_NOTICES)
    + package_notices.pinned_notices(package_notices.NOTO_NOTICES)
)


def _resolved_directory(path: Path, label: str) -> Path:
    resolved = path.resolve()
    if not resolved.is_dir():
        raise ValueError(f"{label} directory was not found: {resolved}")
    return resolved


def verify_export(
    export_directory: Path, objdump: str, launcher_deps: Path | None = None, engine_deps: Path | None = None
) -> tuple[Path, dict[str, Any]]:
    """Check the export's files and PE files, and that THIRD_PARTY_NOTICES.md names every package the engine and the
    launcher ship (`engine_deps` and `launcher_deps`: the dependency lists their publishes kept, by default
    publish_engine's and publish_launcher's)."""
    export_directory = _resolved_directory(export_directory, "Windows export")
    missing = [name for name in REQUIRED_FILES if not (export_directory / name).is_file()]
    if missing:
        raise ValueError("Windows export is incomplete; missing: " + ", ".join(missing))
    data_directory = export_directory / DATA_DIRECTORY
    extra = sorted(path.relative_to(data_directory).as_posix() for path in data_directory.rglob("*")
                   if path.relative_to(data_directory).as_posix() not in DATA_FILES)
    if extra:
        raise ValueError("Windows export's data folder holds files the NativeAOT engine does not ship: "
                         + ", ".join(extra))
    forbidden = [name for name in FORBIDDEN_FILES if (export_directory / name).exists()]
    forbidden.extend(
        path.relative_to(export_directory).as_posix()
        for path in export_directory.rglob("*")
        if path.is_file() and (path.name.endswith(".so") or ".so." in path.name)
    )
    if forbidden:
        raise ValueError("Windows export contains Linux-only files: " + ", ".join(sorted(set(forbidden))))
    symbols = sorted(path.relative_to(export_directory).as_posix() for path in export_directory.rglob("*.pdb"))
    if symbols:
        raise ValueError("Windows export contains debug symbols: " + ", ".join(symbols))
    package_notices.verify_dependency_notices(
        engine_deps or publish_engine.dependency_list(RUNTIME), REPO / "THIRD_PARTY_NOTICES.md"
    )
    package_notices.verify_launcher_notices(
        launcher_deps or publish_launcher.dependency_list(LAUNCHER.runtime), REPO / "THIRD_PARTY_NOTICES.md"
    )
    executable_machine = verify_windows_native.pe_machine(export_directory / "OME.exe")
    if executable_machine != verify_windows_native.PE_MACHINE_AMD64:
        raise ValueError(
            f"Windows executable is not AMD64 PE (0x{executable_machine:04x}): OME.exe"
        )
    native_report = verify_windows_native.verify_bundle(
        data_directory,
        objdump,
        exact_dlls=False,
    )
    native_report["bundle"] = DATA_DIRECTORY
    return export_directory, {
        "schema_version": 1,
        "target": "win-x64",
        "executable_machine": "AMD64",
        "engine": verify_windows_native.verify_engine_library(
            data_directory / verify_windows_native.ENGINE_LIBRARY, objdump),
        "native": native_report,
        "launcher": verify_windows_native.verify_launcher(export_directory, LAUNCHER.program, LAUNCHER.natives),
    }


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
    windows_template = next(
        member
        for member in godot["templates"]["members"]
        if member["install_name"] == "windows_release_x86_64.exe"
    )
    with (REPO / "native/ome_movie_ffmpeg/dependency-win64.json").open(
        encoding="utf-8"
    ) as stream:
        ffmpeg = json.load(stream)
    return {
        "schema_version": 1,
        "project": "Open Maid Engine",
        "target": "win-x64",
        "source_commit": _git("rev-parse", "HEAD"),
        "source_dirty": bool(_git("status", "--porcelain", "--untracked-files=normal")),
        "source_date_epoch": epoch,
        "godot": {
            "version": godot["godot_version"],
            "editor_archive_sha256": godot["editor"]["sha256"],
            "release_template_sha256": windows_template["sha256"],
        },
        "ffmpeg": {
            "provider": ffmpeg["provider"],
            "version": ffmpeg["ffmpeg_version"],
            "archive_sha256": ffmpeg["sha256"],
        },
    }


def _hash(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _write_text(path: Path, content: str) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        stream.write(content)


def _write_checksums(package_root: Path) -> None:
    checksum_path = package_root / "SHA256SUMS"
    files = sorted(
        path for path in package_root.rglob("*")
        if path.is_file() and path != checksum_path
    )
    lines = [f"{_hash(path)}  {path.relative_to(package_root).as_posix()}" for path in files]
    _write_text(checksum_path, "\n".join(lines) + "\n")


def _zip_timestamp(epoch: int) -> tuple[int, int, int, int, int, int]:
    minimum = 315532800  # 1980-01-01, the earliest ZIP timestamp.
    maximum = 4354819198  # 2107-12-31 23:59:58, the latest portable ZIP timestamp.
    stamp = time.gmtime(max(minimum, min(epoch, maximum)))[:6]
    return (*stamp[:5], stamp[5] - stamp[5] % 2)


def create_package(
    export_directory: Path,
    output_directory: Path,
    metadata: dict[str, object],
    verification: dict[str, Any],
    epoch: int,
) -> tuple[Path, Path]:
    export_directory = _resolved_directory(export_directory, "Windows export")
    output_directory = output_directory.resolve()
    expected_parent = (REPO / "build/package/windows-x64").resolve()
    if output_directory != expected_parent:
        raise ValueError(f"refusing to replace unexpected package directory: {output_directory}")
    staging_parent = output_directory / "staging"
    package_root = staging_parent / PACKAGE_NAME
    archive_path = output_directory / f"{PACKAGE_NAME}.zip"
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
    _write_text(
        package_root / "BUILD-INFO.json",
        json.dumps(metadata, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
    )
    _write_text(
        package_root / "WINDOWS-VERIFICATION.json",
        json.dumps(verification, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
    )
    _write_checksums(package_root)

    output_directory.mkdir(parents=True, exist_ok=True)
    temporary_archive = archive_path.with_suffix(archive_path.suffix + ".part")
    timestamp = _zip_timestamp(epoch)
    with zipfile.ZipFile(
        temporary_archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9
    ) as archive:
        for path in sorted(item for item in package_root.rglob("*") if item.is_file()):
            relative = path.relative_to(staging_parent).as_posix()
            info = zipfile.ZipInfo(relative, timestamp)
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            with path.open("rb") as source, archive.open(info, "w", force_zip64=True) as target:
                shutil.copyfileobj(source, target, length=1024 * 1024)
    os.replace(temporary_archive, archive_path)
    return package_root, archive_path


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    verify = subparsers.add_parser("verify")
    verify.add_argument("export_directory", type=Path)
    verify.add_argument("--objdump", default="x86_64-w64-mingw32-objdump")
    verify.add_argument("--launcher-deps", type=Path, help="the launcher's kept dependency list")
    verify.add_argument("--engine-deps", type=Path, help="the engine's kept dependency list")
    package = subparsers.add_parser("package")
    package.add_argument("export_directory", type=Path)
    package.add_argument("--objdump", default="x86_64-w64-mingw32-objdump")
    package.add_argument("--launcher-deps", type=Path, help="the launcher's kept dependency list")
    package.add_argument("--engine-deps", type=Path, help="the engine's kept dependency list")
    package.add_argument(
        "--output-directory", type=Path, default=REPO / "build/package/windows-x64"
    )
    args = parser.parse_args()

    export_directory, verification = verify_export(args.export_directory, args.objdump, args.launcher_deps,
                                                args.engine_deps)
    if args.command == "verify":
        print(export_directory)
        return 0
    epoch = source_date_epoch()
    package_root, archive_path = create_package(
        export_directory,
        args.output_directory,
        build_metadata(epoch),
        verification,
        epoch,
    )
    print(package_root)
    print(archive_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
