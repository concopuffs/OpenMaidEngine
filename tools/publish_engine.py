#!/usr/bin/env python3
"""Publish the engine for a release target as one NativeAOT library (docs/platform-portability.md, "Fewer files in a
package").

Godot loads OME.dll or OME.so from its data folder when the folder holds no .NET runtime, so the publish gives that
library and nothing of a JIT runtime. NativeAOT compiles for the host's own system only: win-x64 publishes on Windows
(with the MSVC build tools) and linux-x64 on Linux, where gcc links it. A release links the Linux library against glibc
2.28, the engine's floor, in the manylinux_2_28 container (.github/workflows/release-build.yml); the Linux packager
refuses a library that needs a newer glibc.

The output goes to build/engine-publish/<runtime>/, with the build's dependency list beside it for the packagers'
notices check, and a stamp naming the commit it was built from, which the release build scripts compare with their
own checkout. It runs as a step of its own, with the release build's low-memory flags.
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
PROJECT = REPO / "godot/OME.csproj"
OUTPUT_ROOT = REPO / "build/engine-publish"
LOW_MEMORY_ENVIRONMENT = {
    "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
    "MSBUILDDISABLENODEREUSE": "1",
    "DOTNET_gcServer": "0",
    "DOTNET_GCConserveMemory": "9",
}
# The Visual Studio installer's folder, where vswhere.exe lives: NativeAOT finds the MSVC tools through it.
VSWHERE_FOLDER = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio/Installer"


@dataclass(frozen=True)
class Target:
    runtime: str
    godot_platform: str
    library: str
    host: str  # sys.platform's prefix for the system that can compile it

    def compiles_here(self, platform: str = sys.platform) -> bool:
        return platform.startswith(self.host)


TARGETS = {
    "win-x64": Target("win-x64", "windows", "OME.dll", "win32"),
    "linux-x64": Target("linux-x64", "linuxbsd", "OME.so", "linux"),
}


def runtime_notices(runtime: str) -> tuple[str, str]:
    """The .NET runtime's license and notices, which the publish keeps beside the library that holds the runtime."""
    return (f"Microsoft.NETCore.App.Runtime.{runtime}-LICENSE.TXT",
            f"Microsoft.NETCore.App.Runtime.{runtime}-THIRD-PARTY-NOTICES.TXT")


def publish_files(runtime: str) -> tuple[str, ...]:
    """Everything the publish holds: the library and the runtime's notices. Nothing of a JIT runtime (Godot takes its
    hostfxr route when its data folder holds one), no symbol files, no managed assemblies: the library holds them."""
    return (TARGETS[runtime].library, *runtime_notices(runtime))


def publish_directory(runtime: str, output_root: Path = OUTPUT_ROOT) -> Path:
    return output_root / runtime


def dependency_list(runtime: str, output_root: Path = OUTPUT_ROOT) -> Path:
    """The build's dependency list: the packagers' notices check reads it, since the library holds the packages."""
    return output_root / f"{runtime}.deps.json"


def stamp_file(runtime: str, output_root: Path = OUTPUT_ROOT) -> Path:
    return output_root / f"{runtime}.json"


def publish_command(dotnet: str, runtime: str, output_root: Path = OUTPUT_ROOT) -> list[str]:
    """The publish: the project's NativeAOT release form for `runtime`, one MSBuild node, no build servers."""
    target = TARGETS[runtime]
    command = [
        dotnet, "publish", str(PROJECT),
        "--configuration", "ExportRelease",
        "--runtime", runtime,
        "--self-contained", "true",
        "--output", str(publish_directory(runtime, output_root)),
        "--disable-build-servers",
        "-nodeReuse:false",
        "-m:1",
        f"-p:GodotTargetPlatform={target.godot_platform}",
        "-p:PublishAot=true",
        "-p:UseSharedCompilation=false",
        "-p:BuildInParallel=false",
        "-p:RestoreDisableParallel=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        f"-p:OmeDependencyList={dependency_list(runtime, output_root)}",
    ]
    if runtime == "linux-x64":
        command.append("-p:CppCompilerAndLinker=gcc")
    return command


def verify_publish(directory: Path, runtime: str) -> None:
    """Fail unless `directory` holds exactly the publish's files (publish_files)."""
    expected = publish_files(runtime)
    missing = [name for name in expected if not (directory / name).is_file()]
    if missing:
        raise ValueError(f"the engine publish is incomplete; missing: {', '.join(missing)} in {directory}")
    names = sorted(path.relative_to(directory).as_posix() for path in directory.rglob("*") if path.is_file())
    refused = [name for name in names if name not in expected]
    if refused:
        raise ValueError("the engine publish holds files a NativeAOT engine does not ship: " + ", ".join(refused))


def source_state(run=subprocess.run) -> dict[str, object]:
    """The checkout the library is built from: its commit and whether it has uncommitted changes."""
    def git(*arguments: str) -> str:
        return run(("git", *arguments), cwd=REPO, check=True, capture_output=True, text=True).stdout.strip()

    return {"commit": git("rev-parse", "HEAD"), "dirty": bool(git("status", "--porcelain", "--untracked-files=no"))}


def check_stamp(runtime: str, output_root: Path = OUTPUT_ROOT, run=subprocess.run) -> dict[str, object]:
    """Fail unless the publish for `runtime` exists, is complete and was built from this checkout's commit."""
    stamp_path = stamp_file(runtime, output_root)
    if not stamp_path.is_file():
        raise ValueError(f"no engine publish for {runtime}: run tools/publish_engine.py --runtime {runtime}")
    stamp = json.loads(stamp_path.read_text(encoding="utf-8"))
    here = source_state(run)
    if stamp.get("runtime") != runtime or stamp.get("commit") != here["commit"]:
        raise ValueError(f"the engine publish for {runtime} was built from {stamp.get('commit')}, "
                         f"not this checkout's {here['commit']}")
    if not dependency_list(runtime, output_root).is_file():
        raise ValueError(f"the engine publish kept no dependency list: {dependency_list(runtime, output_root)}")
    verify_publish(publish_directory(runtime, output_root), runtime)
    return stamp


def publish(runtime: str, dotnet: str = "dotnet", output_root: Path = OUTPUT_ROOT, run=subprocess.run,
            platform: str = sys.platform) -> Path:
    """Publish the engine for `runtime`, check the output, and stamp it with the checkout's commit."""
    target = TARGETS[runtime]
    if not target.compiles_here(platform):
        raise ValueError(f"NativeAOT compiles {runtime} on its own system only; this is {platform}")
    directory = publish_directory(runtime, output_root)
    if directory.resolve().parent != output_root.resolve():
        raise ValueError(f"refusing to replace an unexpected engine publish directory: {directory}")
    if directory.exists():
        shutil.rmtree(directory)
    for stale in (dependency_list(runtime, output_root), stamp_file(runtime, output_root)):
        stale.unlink(missing_ok=True)
    environment = {**os.environ, **LOW_MEMORY_ENVIRONMENT}
    if runtime == "win-x64" and VSWHERE_FOLDER.is_dir():
        environment["PATH"] = environment.get("PATH", "") + os.pathsep + str(VSWHERE_FOLDER)
    state = source_state(run)
    result = run(publish_command(dotnet, runtime, output_root), cwd=REPO, env=environment, check=False)
    if result.returncode != 0:
        raise RuntimeError(f"the engine publish for {runtime} failed with exit code {result.returncode}")
    if not dependency_list(runtime, output_root).is_file():
        raise ValueError(f"the engine publish kept no dependency list: {dependency_list(runtime, output_root)}")
    verify_publish(directory, runtime)
    stamp_file(runtime, output_root).write_text(
        json.dumps({"runtime": runtime, **state}, indent=2) + "\n", encoding="utf-8", newline="\n")
    return directory


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument("--runtime", required=True, choices=sorted(TARGETS))
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--check", action="store_true",
                        help="only check that a publish for this checkout's commit is in place")
    args = parser.parse_args(arguments)
    try:
        if args.check:
            check_stamp(args.runtime)
            print(f"engine publish for {args.runtime} matches this checkout", flush=True)
            return 0
        directory = publish(args.runtime, args.dotnet)
    except (ValueError, RuntimeError) as error:
        print(f"publish_engine: {error}", file=sys.stderr, flush=True)
        return 1
    print(f"engine published for {args.runtime}: {directory}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
