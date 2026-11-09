#!/usr/bin/env python3
"""Check that the toolchain code a MinGW link adds to a DLL is covered by the notices the Windows package ships.

MinGW-w64 GCC links runtime code into every DLL it builds: the CRT startup, printf and strtod (gdtoa), math
functions and GCC's helpers. The Windows movie shim (native/ome_movie_ffmpeg/build-win64.sh) and the FFmpeg DLLs
(build-ffmpeg-sdk.sh win64) ask ld for a link map (-Map), which lists every object file a link loaded and every
archive member it pulled in. This tool reads those maps and fails unless each toolchain object comes from a source
the package covers:
- the MinGW-w64 runtime archives: their notice-bearing parts (the ZPL-2.1 mingw-w64 notice, gdtoa) are all listed
  in COPYING.MinGW-w64-runtime.txt, which the Windows package ships as MinGW-w64-runtime-COPYING.txt; the rest is
  public domain;
- GCC's runtime (libgcc, crtbegin/crtend): GPLv3 with the GCC Runtime Library Exception, which puts no condition on
  the combined DLL;
- Windows import libraries: dlltool stubs that name a system DLL's exports.
Anything else, such as a static winpthreads, fails the check until its license is reviewed. It also fails when the
compiler's MinGW-w64 runtime is not the release the shipped notice is pinned to (notices/mingw-w64/notices.json).

Toolchain objects are the ones under the directories where the compiler finds libgcc and the MinGW-w64 runtime;
other inputs (the project's objects, FFmpeg's import libraries) are not the toolchain's.

    mingw_runtime_notices.py --compiler x86_64-w64-mingw32-gcc MAP [MAP...]
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

import package_notices

# Archives of the MinGW-w64 runtime whose members were reviewed (2026-11-06, v11.0.1): public domain except the
# parts COPYING.MinGW-w64-runtime.txt lists.
MINGW_RUNTIME_ARCHIVES = frozenset({"libmingw32.a", "libmingwex.a", "libmoldname.a", "libmsvcrt.a"})
MINGW_RUNTIME_OBJECTS = frozenset({"dllcrt2.o"})
# GCC's runtime, under the GCC Runtime Library Exception.
GCC_RUNTIME_ARCHIVES = frozenset({"libgcc.a", "libgcc_eh.a"})
GCC_RUNTIME_OBJECTS = frozenset({"crtbegin.o", "crtend.o"})

MINGW_RUNTIME = "MinGW-w64 runtime"
GCC_RUNTIME = "GCC runtime"
IMPORT_LIBRARY = "import library"

_MEMBER_LINE = re.compile(r"^(?P<archive>.+?\.a)\((?P<member>[^()]+)\)(?:\s+\S.*)?$")
_ARCHIVE_SECTION_END = ("Discarded input sections", "Allocating common symbols", "Memory Configuration")
_VERSION_PART = re.compile(r"^#define __MINGW64_VERSION_(MAJOR|MINOR|BUGFIX) (\d+)\s*$", re.M)


@dataclass
class LinkMap:
    """What one ld map says a link used: archive members (archive path, member name) and directly loaded objects."""

    members: list[tuple[str, str]] = field(default_factory=list)
    objects: list[str] = field(default_factory=list)


def parse_link_map(text: str) -> LinkMap:
    """Read a GNU ld -Map file. Archive members come from its first section ("Archive member included to satisfy
    reference by file (symbol)"): one 'archive(member)' line per member, with the referencing file on the next line
    (or after padding on the same line when the first part is short). Directly loaded objects are its LOAD lines."""
    link_map = LinkMap()
    in_members = False
    for line in text.splitlines():
        if line.startswith("Archive member included"):
            in_members = True
            continue
        if line.startswith(_ARCHIVE_SECTION_END):
            in_members = False
        if in_members and line and not line[0].isspace():
            match = _MEMBER_LINE.match(line.rstrip())
            if match:
                link_map.members.append((match["archive"], match["member"]))
        elif line.startswith("LOAD ") and line.endswith(".o"):
            link_map.objects.append(line[len("LOAD "):])
    return link_map


def _normalized(path: str) -> str:
    return os.path.normpath(path).replace("\\", "/")


def _under(path: str, roots: tuple[str, ...]) -> bool:
    normalized = _normalized(path)
    return any(normalized.startswith(root.rstrip("/") + "/") for root in roots)


def _import_stub(archive: str, member: str) -> bool:
    """dlltool's import-library members: <stem>h.o (head), <stem>t.o (tail), <stem>sNNNNN.o (one per export); the
    MinGW-w64 runtime's own import tables use the stem <stem>_def."""
    stem = re.escape(Path(archive).name.removesuffix(".a"))
    return re.fullmatch(rf"{stem}(?:_def)?(?:h|t|s\d+)\.o", member) is not None


def classify(link_map: LinkMap, toolchain_roots: tuple[str, ...]) -> tuple[dict[str, set[str]], list[str]]:
    """Sort a link's toolchain objects by the notice that covers them; return (covered by source, uncovered)."""
    covered: dict[str, set[str]] = {MINGW_RUNTIME: set(), GCC_RUNTIME: set(), IMPORT_LIBRARY: set()}
    uncovered: list[str] = []
    for archive, member in link_map.members:
        if not _under(archive, toolchain_roots):
            continue
        name = Path(_normalized(archive)).name
        label = f"{name}({member})"
        if name in MINGW_RUNTIME_ARCHIVES:
            covered[MINGW_RUNTIME].add(label)
        elif name in GCC_RUNTIME_ARCHIVES:
            covered[GCC_RUNTIME].add(label)
        elif _import_stub(name, member):
            covered[IMPORT_LIBRARY].add(label)
        else:
            uncovered.append(label)
    for path in link_map.objects:
        if not _under(path, toolchain_roots):
            continue
        name = Path(_normalized(path)).name
        if name in MINGW_RUNTIME_OBJECTS:
            covered[MINGW_RUNTIME].add(name)
        elif name in GCC_RUNTIME_OBJECTS:
            covered[GCC_RUNTIME].add(name)
        else:
            uncovered.append(name)
    return covered, uncovered


def _compiler_output(compiler: str, *arguments: str, stdin: str | None = None) -> str:
    process = subprocess.run(
        [compiler, *arguments], input=stdin, capture_output=True, text=True, check=False
    )
    if process.returncode != 0:
        raise RuntimeError(f"{compiler} {' '.join(arguments)} failed: {process.stderr.strip()}")
    return process.stdout


def toolchain_roots(compiler: str) -> tuple[str, ...]:
    """The directories that hold libgcc and the MinGW-w64 runtime for this compiler."""
    libgcc = _compiler_output(compiler, "-print-libgcc-file-name").strip()
    runtime = _compiler_output(compiler, "-print-file-name=libmingwex.a").strip()
    if not os.path.isabs(runtime):
        raise RuntimeError(f"{compiler} does not know the MinGW-w64 runtime (libmingwex.a)")
    return tuple(sorted({_normalized(os.path.dirname(path)) for path in (libgcc, runtime)}))


def runtime_version(compiler: str) -> str:
    """The MinGW-w64 runtime release the compiler links (__MINGW64_VERSION_MAJOR.MINOR.BUGFIX from _mingw.h)."""
    defines = _compiler_output(compiler, "-dM", "-E", "-x", "c", "-", stdin="#include <_mingw.h>\n")
    parts = dict(_VERSION_PART.findall(defines))
    if set(parts) != {"MAJOR", "MINOR", "BUGFIX"}:
        raise RuntimeError(f"{compiler} does not define the MinGW-w64 runtime version")
    return f"{parts['MAJOR']}.{parts['MINOR']}.{parts['BUGFIX']}"


def check(compiler: str, maps: list[Path], notices_manifest: Path = package_notices.MINGW_NOTICES) -> str:
    """Fail unless every map's toolchain objects are covered and the runtime is the pinned notice's; return a summary
    line."""
    pinned = package_notices.notices_release_tag(notices_manifest).removeprefix("v")
    version = runtime_version(compiler)
    if version != pinned:
        raise ValueError(
            f"{compiler} links MinGW-w64 runtime {version}, but the shipped notice is pinned to {pinned}: refresh "
            f"notices/mingw-w64/ from the v{version} tag and review the linked objects' licenses again"
        )
    roots = toolchain_roots(compiler)
    totals: dict[str, set[str]] = {MINGW_RUNTIME: set(), GCC_RUNTIME: set(), IMPORT_LIBRARY: set()}
    problems: list[str] = []
    for path in maps:
        covered, uncovered = classify(parse_link_map(path.read_text(encoding="utf-8", errors="replace")), roots)
        for source, labels in covered.items():
            totals[source] |= labels
        problems.extend(f"{path.name}: {label}" for label in uncovered)
    if problems:
        raise ValueError(
            "toolchain objects outside the reviewed MinGW-w64 and GCC runtimes were linked; review their licenses "
            "and the notices the Windows package ships (tools/mingw_runtime_notices.py):\n  " + "\n  ".join(problems)
        )
    return (
        f"MINGW RUNTIME NOTICES OK: MinGW-w64 {version}, maps={len(maps)}, "
        f"runtime={len(totals[MINGW_RUNTIME])} gcc={len(totals[GCC_RUNTIME])} "
        f"import-stubs={len(totals[IMPORT_LIBRARY])}"
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--compiler", required=True, help="the MinGW-w64 GCC that made the links")
    parser.add_argument("maps", nargs="+", type=Path, help="ld -Map files")
    arguments = parser.parse_args(argv)
    try:
        print(check(arguments.compiler, arguments.maps))
    except (RuntimeError, ValueError) as error:
        print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
