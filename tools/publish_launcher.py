#!/usr/bin/env python3
"""Publish the launcher for a release target and place it beside the engine in an export (launcher plan §9.2).

The release form is the project's (launcher/Ome.Launcher/Ome.Launcher.csproj, plan §9.1): a self-contained single
file with Avalonia's native libraries beside it and the shipped packages' notice files in OME-Launcher-notices/.
This tool runs that publish as a step of its own, never inside Godot's export, with the release build's low-memory
flags (plan §3.7). It keeps the dependency list the single file bundles, refuses an output that is not exactly the
launcher's files, and copies them beside OME / OME.exe. The packagers name the same files through LAYOUTS.

On Linux it prints the publish's peak memory: the largest process of its tree, and the job's memory cgroup peak where
the cgroup reports one (plan §9.2; the runner kills at about 1 GiB, docs/platform-portability.md).
"""
from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
PROJECT = REPO / "launcher/Ome.Launcher/Ome.Launcher.csproj"
OUTPUT_ROOT = REPO / "build/launcher-publish"
NOTICE_FOLDER = "OME-Launcher-notices"
# The notice file names the shared PublishPackageNotices target writes: <package id>-<file name> of a package root's
# LICENSE*, THIRD-PARTY-NOTICES* or NOTICE* (Directory.Build.targets).
NOTICE_NAME = re.compile(r"^[A-Za-z0-9.-]+-(?:LICENSE|THIRD-PARTY-NOTICES|NOTICE)[^/\\]*$")
LOW_MEMORY_ENVIRONMENT = {
    "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
    "MSBUILDDISABLENODEREUSE": "1",
    "DOTNET_gcServer": "0",
    "DOTNET_GCConserveMemory": "9",
}


@dataclass(frozen=True)
class Layout:
    """The launcher's files in one target's archive, beside the engine."""

    runtime: str
    engine: str
    program: str
    natives: tuple[str, ...]
    notices: tuple[str, ...]

    def files(self) -> tuple[str, ...]:
        """Every file the publish must give, relative to the archive root."""
        return (self.program, *self.natives, *(f"{NOTICE_FOLDER}/{name}" for name in self.notices))


def _notices(runtime: str, natives_suffix: str, *extra: str) -> tuple[str, ...]:
    return (
        f"Microsoft.NETCore.App.Runtime.{runtime}-LICENSE.TXT",
        f"Microsoft.NETCore.App.Runtime.{runtime}-THIRD-PARTY-NOTICES.TXT",
        "SkiaSharp-LICENSE.txt",
        f"SkiaSharp.NativeAssets.{natives_suffix}-LICENSE.txt",
        f"SkiaSharp.NativeAssets.{natives_suffix}-THIRD-PARTY-NOTICES.txt",
        "HarfBuzzSharp-LICENSE.txt",
        f"HarfBuzzSharp.NativeAssets.{natives_suffix}-LICENSE.txt",
        f"HarfBuzzSharp.NativeAssets.{natives_suffix}-THIRD-PARTY-NOTICES.txt",
        *extra,
    )


LAYOUTS = {
    "linux-x64": Layout(
        "linux-x64", "OME", "OME-Launcher", ("libHarfBuzzSharp.so", "libSkiaSharp.so"), _notices("linux-x64", "Linux")
    ),
    "win-x64": Layout(
        "win-x64",
        "OME.exe",
        "OME-Launcher.exe",
        ("av_libglesv2.dll", "libHarfBuzzSharp.dll", "libSkiaSharp.dll"),
        # ANGLE's BSD licence, which Avalonia.Angle.Windows.Natives ships as LICENSE (launcher plan §3.10).
        _notices("win-x64", "Win32", "Avalonia.Angle.Windows.Natives-LICENSE"),
    ),
}


def publish_directory(runtime: str, output_root: Path = OUTPUT_ROOT) -> Path:
    return output_root / runtime


def dependency_list(runtime: str, output_root: Path = OUTPUT_ROOT) -> Path:
    """Where the publish keeps its bundled dependency list: the packagers' notices check reads it."""
    return output_root / f"{runtime}.deps.json"


def publish_command(dotnet: str, runtime: str, output_root: Path = OUTPUT_ROOT) -> list[str]:
    """The publish: the project's release form for `runtime`, one MSBuild node, no build servers."""
    return [
        dotnet, "publish", str(PROJECT),
        "--configuration", "Release",
        "--runtime", runtime,
        "--output", str(publish_directory(runtime, output_root)),
        "--disable-build-servers",
        "-nodeReuse:false",
        "-m:1",
        "-p:UseSharedCompilation=false",
        "-p:BuildInParallel=false",
        "-p:RestoreDisableParallel=true",
        f"-p:OmeDependencyList={dependency_list(runtime, output_root)}",
    ]


def verify_publish(directory: Path, layout: Layout) -> None:
    """Fail unless `directory` holds exactly the launcher's files: the program, the target's natives, and notice files
    in the notices folder, among them the ones `layout` requires. A symbol file, a loose dependency list, another
    target's native or anything else is refused."""
    if not directory.is_dir():
        raise ValueError(f"launcher publish directory was not found: {directory}")
    present = sorted(path.relative_to(directory).as_posix() for path in directory.rglob("*") if path.is_file())
    missing = [name for name in layout.files() if name not in present]
    if missing:
        raise ValueError("launcher publish is incomplete; missing: " + ", ".join(missing))
    top = {layout.program, *layout.natives}
    unexpected = [
        name for name in present
        if name not in top and not (
            name.startswith(f"{NOTICE_FOLDER}/") and NOTICE_NAME.match(name.removeprefix(f"{NOTICE_FOLDER}/"))
        )
    ]
    if unexpected:
        raise ValueError("launcher publish has unexpected files: " + ", ".join(unexpected))
    folders = sorted(path.relative_to(directory).as_posix() for path in directory.rglob("*") if path.is_dir())
    if folders != [NOTICE_FOLDER]:
        raise ValueError(f"launcher publish has unexpected folders: {folders}")


def place(directory: Path, layout: Layout, export_directory: Path) -> list[Path]:
    """Copy the launcher's files beside the engine in `export_directory`; return the copies."""
    if not (export_directory / layout.engine).is_file():
        raise ValueError(f"the export has no {layout.engine} to put the launcher beside: {export_directory}")
    copies = []
    for path in sorted(item for item in directory.rglob("*") if item.is_file()):
        target = export_directory / path.relative_to(directory)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
        copies.append(target)
    return copies


def _peaks() -> list[str]:
    """The publish's peak memory on Linux: the largest process of its waited tree (getrusage reports kibibytes), and
    the job's memory cgroup peak where it has one. Nothing elsewhere."""
    lines = []
    try:
        import resource
    except ImportError:
        return lines
    largest = resource.getrusage(resource.RUSAGE_CHILDREN).ru_maxrss
    lines.append(f"launcher publish: largest process peaked at {largest / 1024:.0f} MiB")
    peak = Path("/sys/fs/cgroup/memory.peak")
    if peak.is_file():
        try:
            value = int(peak.read_text(encoding="ascii").strip())
            lines.append(f"launcher publish: the job's memory cgroup has peaked at {value / 1024 / 1024:.0f} MiB so far")
        except (OSError, ValueError):
            pass
    return lines


def publish(runtime: str, export_directory: Path, dotnet: str = "dotnet", output_root: Path = OUTPUT_ROOT,
            run=subprocess.run) -> list[Path]:
    """Publish the launcher for `runtime`, check its files and place them in `export_directory`."""
    layout = LAYOUTS[runtime]
    export_directory = export_directory.resolve()
    if not (export_directory / layout.engine).is_file():
        raise ValueError(f"the export has no {layout.engine} to put the launcher beside: {export_directory}")
    directory = publish_directory(runtime, output_root)
    if directory.resolve().parent != output_root.resolve():
        raise ValueError(f"refusing to replace an unexpected launcher publish directory: {directory}")
    if directory.exists():
        shutil.rmtree(directory)
    dependency_list(runtime, output_root).unlink(missing_ok=True)
    environment = {**os.environ, **LOW_MEMORY_ENVIRONMENT}
    result = run(publish_command(dotnet, runtime, output_root), cwd=REPO, env=environment, check=False)
    if result.returncode != 0:
        raise RuntimeError(f"the launcher publish for {runtime} failed with exit code {result.returncode}")
    for line in _peaks():
        print(line, flush=True)
    if not dependency_list(runtime, output_root).is_file():
        raise ValueError(f"the launcher publish kept no dependency list: {dependency_list(runtime, output_root)}")
    verify_publish(directory, layout)
    return place(directory, layout, export_directory)


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument("--runtime", required=True, choices=sorted(LAYOUTS))
    parser.add_argument("--into", required=True, type=Path, help="the export directory that holds the engine")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args(arguments)
    try:
        copies = publish(args.runtime, args.into, args.dotnet)
    except (ValueError, RuntimeError) as error:
        print(f"publish_launcher: {error}", file=sys.stderr, flush=True)
        return 1
    print(f"launcher placed beside the engine: {len(copies)} files in {args.into.resolve()}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
