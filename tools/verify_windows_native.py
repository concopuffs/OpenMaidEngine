#!/usr/bin/env python3
"""Verify the project-owned Windows x64 FFmpeg bundle without executing it."""
from __future__ import annotations

import argparse
import json
import re
import struct
import subprocess
import sys
from pathlib import Path
from typing import Any


SHIM = "age_movie_ffmpeg.dll"
RUNTIME_DLLS = (
    "avformat-62.dll",
    "avcodec-62.dll",
    "avutil-60.dll",
    "swscale-9.dll",
    "swresample-6.dll",
)
REQUIRED_FILES = (SHIM, *RUNTIME_DLLS, "FFmpeg-LICENSE.txt")
REQUIRED_EXPORTS = frozenset({
    "age_movie_abi_version",
    "age_movie_open",
    "age_movie_seek",
    "age_movie_decode_video",
    "age_movie_decode_audio",
    "age_movie_last_error",
    "age_movie_close",
})
FORBIDDEN_RUNTIME_IMPORTS = frozenset({"cygwin1.dll", "msys-2.0.dll"})
PE_MACHINE_AMD64 = 0x8664


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


def parse_objdump(output: str) -> tuple[set[str], set[str]]:
    imports = {
        match.group(1).strip()
        for match in re.finditer(r"^\s*DLL Name:\s*(\S+)\s*$", output, re.MULTILINE)
    }
    exports = {
        name for name in REQUIRED_EXPORTS
        if re.search(rf"(?<![A-Za-z0-9_]){re.escape(name)}(?![A-Za-z0-9_])", output)
    }
    return imports, exports


def verify_bundle(bundle: Path, objdump: str) -> dict[str, Any]:
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
    if unexpected_dlls:
        raise ValueError("Windows native bundle has unexpected DLLs: " + ", ".join(unexpected_dlls))

    machines: dict[str, str] = {}
    for name in (SHIM, *RUNTIME_DLLS):
        machine = pe_machine(bundle / name)
        if machine != PE_MACHINE_AMD64:
            raise ValueError(f"Windows native file is not AMD64 PE (0x{machine:04x}): {name}")
        machines[name] = "AMD64"

    process = subprocess.run(
        (objdump, "-p", str(bundle / SHIM)),
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if process.returncode != 0:
        raise RuntimeError(f"objdump failed for {SHIM}: {process.stderr.strip()}")
    imports, exports = parse_objdump(process.stdout)
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
