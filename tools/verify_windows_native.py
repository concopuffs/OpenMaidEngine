#!/usr/bin/env python3
"""Verify the project-owned Windows x64 FFmpeg bundle, the engine's NativeAOT library, and the launcher's PE files, without
executing them."""
from __future__ import annotations

import argparse
import json
import re
import struct
import subprocess
import sys
from pathlib import Path
from typing import Any


SHIM = "ome_movie_ffmpeg.dll"
RUNTIME_DLLS = (
    "avformat-62.dll",
    "avcodec-62.dll",
    "avutil-60.dll",
    "swscale-9.dll",
    "swresample-6.dll",
)
REQUIRED_FILES = (SHIM, *RUNTIME_DLLS, "FFmpeg-LICENSE.txt", "FFmpeg-SOURCE.txt")
REQUIRED_EXPORTS = frozenset({
    "ome_movie_abi_version",
    "ome_movie_open",
    "ome_movie_seek",
    "ome_movie_decode_video",
    "ome_movie_decode_audio",
    "ome_movie_last_error",
    "ome_movie_close",
})
FORBIDDEN_RUNTIME_IMPORTS = frozenset({"cygwin1.dll", "msys-2.0.dll"})
# The engine's NativeAOT library (tools/publish_engine.py): what Godot calls to start it, and what it must not need. It
# links the C runtime statically except for the Universal CRT, part of Windows since 10, so a player needs no Visual
# C++ redistributable; and it holds its .NET runtime, so it loads none.
ENGINE_LIBRARY = "OME.dll"
ENGINE_EXPORT = "godotsharp_game_main_init"
FORBIDDEN_ENGINE_IMPORTS = re.compile(r"^(vcruntime|msvcp|concrt|vccorlib|ucrtbased|hostfxr|hostpolicy|coreclr)",
                                      re.IGNORECASE)
PE_MACHINE_AMD64 = 0x8664
PE_SUBSYSTEM_WINDOWS_GUI = 2


def pe_machine(path: Path) -> int:
    with path.open("rb") as stream:
        header = stream.read(64)
        if len(header) != 64 or header[:2] != b"MZ":
            raise ValueError(f"file has no DOS/PE header: {path}")
        pe_offset = struct.unpack_from("<I", header, 0x3C)[0]
        stream.seek(pe_offset)
        pe_header = stream.read(6)
    if len(pe_header) != 6 or pe_header[:4] != b"PE\0\0":
        raise ValueError(f"file has no PE signature: {path}")
    return struct.unpack_from("<H", pe_header, 4)[0]


def pe_subsystem(path: Path) -> int:
    """The PE optional header's Subsystem: 2 a windowed program, 3 a console one. It sits 68 bytes into the optional
    header, which follows the 4-byte signature and the 20-byte COFF header, for PE32 and PE32+ alike."""
    with path.open("rb") as stream:
        header = stream.read(64)
        if len(header) != 64 or header[:2] != b"MZ":
            raise ValueError(f"file has no DOS/PE header: {path}")
        pe_offset = struct.unpack_from("<I", header, 0x3C)[0]
        stream.seek(pe_offset)
        pe_header = stream.read(24 + 70)
    if len(pe_header) != 94 or pe_header[:4] != b"PE\0\0":
        raise ValueError(f"file has no PE signature: {path}")
    if struct.unpack_from("<H", pe_header, 24)[0] not in (0x10B, 0x20B):
        raise ValueError(f"file has no PE32 or PE32+ optional header: {path}")
    return struct.unpack_from("<H", pe_header, 24 + 68)[0]


def verify_launcher(directory: Path, program: str, natives: tuple[str, ...]) -> dict[str, Any]:
    """The launcher beside the engine (launcher plan §9.3): an AMD64 windowed program, and its native libraries
    AMD64. It is checked, never run."""
    machine = pe_machine(directory / program)
    if machine != PE_MACHINE_AMD64:
        raise ValueError(f"Windows launcher is not AMD64 PE (0x{machine:04x}): {program}")
    subsystem = pe_subsystem(directory / program)
    if subsystem != PE_SUBSYSTEM_WINDOWS_GUI:
        raise ValueError(f"Windows launcher is not a windowed program (subsystem {subsystem}): {program}")
    machines: dict[str, str] = {}
    for name in natives:
        machine = pe_machine(directory / name)
        if machine != PE_MACHINE_AMD64:
            raise ValueError(f"Windows launcher native is not AMD64 PE (0x{machine:04x}): {name}")
        machines[name] = "AMD64"
    return {"program": program, "machine": "AMD64", "subsystem": "windows-gui", "natives": machines}


def parse_objdump(output: str, exports_of: frozenset[str] = REQUIRED_EXPORTS) -> tuple[set[str], set[str]]:
    """The DLLs `objdump -p` lists as imports, and which of `exports_of` it names."""
    imports = {
        match.group(1).strip()
        for match in re.finditer(r"^\s*DLL Name:\s*(\S+)\s*$", output, re.MULTILINE)
    }
    exports = {
        name for name in exports_of
        if re.search(rf"(?<![A-Za-z0-9_]){re.escape(name)}(?![A-Za-z0-9_])", output)
    }
    return imports, exports


def _objdump(objdump: str, path: Path) -> str:
    process = subprocess.run(
        (objdump, "-p", str(path)),
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if process.returncode != 0:
        raise RuntimeError(f"objdump failed for {path.name}: {process.stderr.strip()}")
    return process.stdout


def verify_engine_library(path: Path, objdump: str) -> dict[str, Any]:
    """Check the engine's NativeAOT library: an AMD64 PE that exports Godot's entry point and imports neither a Visual
    C++ redistributable nor a .NET runtime."""
    machine = pe_machine(path)
    if machine != PE_MACHINE_AMD64:
        raise ValueError(f"Windows engine library is not AMD64 PE (0x{machine:04x}): {path.name}")
    imports, exports = parse_objdump(_objdump(objdump, path), frozenset({ENGINE_EXPORT}))
    if ENGINE_EXPORT not in exports:
        raise ValueError(f"Windows engine library does not export {ENGINE_EXPORT}: {path.name}; "
                         "it is not a NativeAOT build")
    forbidden = sorted(name for name in imports if FORBIDDEN_ENGINE_IMPORTS.match(name))
    if forbidden:
        raise ValueError(f"Windows engine library imports what a player's Windows lacks: {', '.join(forbidden)}")
    return {"machine": "AMD64", "export": ENGINE_EXPORT, "imports": sorted(imports, key=str.lower)}


def verify_bundle(
    bundle: Path,
    objdump: str,
    *,
    exact_dlls: bool = True,
) -> dict[str, Any]:
    bundle = bundle.resolve()
    if not bundle.is_dir():
        raise ValueError(f"Windows native bundle directory was not found: {bundle}")
    missing = [name for name in REQUIRED_FILES if not (bundle / name).is_file()]
    if missing:
        raise ValueError("Windows native bundle is incomplete; missing: " + ", ".join(missing))

    expected_dlls = {name.lower() for name in (SHIM, *RUNTIME_DLLS)}
    unexpected_dlls = sorted(
        path.name
        for path in bundle.iterdir()
        if path.is_file() and path.suffix.lower() == ".dll" and path.name.lower() not in expected_dlls
    )
    if exact_dlls and unexpected_dlls:
        raise ValueError("Windows native bundle has unexpected DLLs: " + ", ".join(unexpected_dlls))

    machines: dict[str, str] = {}
    for name in (SHIM, *RUNTIME_DLLS):
        machine = pe_machine(bundle / name)
        if machine != PE_MACHINE_AMD64:
            raise ValueError(f"Windows native file is not AMD64 PE (0x{machine:04x}): {name}")
        machines[name] = "AMD64"

    imports, exports = parse_objdump(_objdump(objdump, bundle / SHIM))
    import_names = {name.lower() for name in imports}
    missing_imports = sorted(name for name in RUNTIME_DLLS if name.lower() not in import_names)
    if missing_imports:
        raise ValueError("Windows shim is missing FFmpeg imports: " + ", ".join(missing_imports))
    forbidden_imports = sorted(import_names & FORBIDDEN_RUNTIME_IMPORTS)
    if forbidden_imports:
        raise ValueError("Windows shim imports a non-native compatibility runtime: " + ", ".join(forbidden_imports))
    missing_exports = sorted(REQUIRED_EXPORTS - exports)
    if missing_exports:
        raise ValueError("Windows shim is missing AGE ABI exports: " + ", ".join(missing_exports))

    return {
        "schema_version": 1,
        "target": "win-x64",
        "bundle": str(bundle),
        "machines": machines,
        "shim_imports": sorted(imports, key=str.lower),
        "shim_exports": sorted(exports),
    }


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=Path)
    parser.add_argument("--objdump", default="x86_64-w64-mingw32-objdump")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args(arguments)
    report = verify_bundle(args.bundle, args.objdump)
    rendered = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    if args.report is not None:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        with args.report.open("w", encoding="utf-8", newline="\n") as stream:
            stream.write(rendered)
    print(
        f"WINDOWS NATIVE OK: dlls={len(report['machines'])} "
        f"exports={len(report['shim_exports'])} ffmpeg-imports={len(RUNTIME_DLLS)}"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError, ValueError) as error:
        print(f"Windows native verification failed: {error}", file=sys.stderr)
        raise SystemExit(1)
