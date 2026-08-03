#!/usr/bin/env python3
"""Stage an exact prepublished payload for Godot's redundant export-time publish.

All non-publish dotnet commands are delegated to AGE_REAL_DOTNET. A publish is
accepted only when its configuration, RID, self-contained flag, project, output
root, and prepublished assembly match the values supplied by the build driver.
"""
from __future__ import annotations

import os
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class PublishRequest:
    project: Path
    configuration: str
    runtime: str
    self_contained: str
    output: Path


def _option(arguments: list[str], *names: str) -> str | None:
    for index, argument in enumerate(arguments):
        if argument in names:
            if index + 1 >= len(arguments):
                raise ValueError(f"missing value after {argument}")
            return arguments[index + 1]
        for name in names:
            prefix = name + "="
            if argument.startswith(prefix):
                return argument[len(prefix):]
    return None


def parse_publish(arguments: list[str]) -> PublishRequest:
    if not arguments or arguments[0] != "publish":
        raise ValueError("arguments are not a dotnet publish command")
    project = next(
        (Path(argument) for argument in arguments[1:] if argument.lower().endswith(".csproj")),
        None,
    )
    configuration = _option(arguments, "-c", "--configuration")
    runtime = _option(arguments, "-r", "--runtime")
    self_contained = _option(arguments, "--self-contained")
    output = _option(arguments, "-o", "--output")
    missing = [
        name for name, value in (
            ("project", project),
            ("configuration", configuration),
            ("runtime", runtime),
            ("self-contained", self_contained),
            ("output", output),
        ) if value is None
    ]
    if missing:
        raise ValueError("publish command is missing: " + ", ".join(missing))
    assert project is not None and configuration is not None and runtime is not None
    assert self_contained is not None and output is not None
    return PublishRequest(project, configuration, runtime, self_contained, Path(output))


def _is_within(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def stage_publish(request: PublishRequest, environ: dict[str, str]) -> Path:
    expected_project = Path(environ["AGE_PUBLISH_PROJECT"]).resolve()
    source = Path(environ["AGE_PREPUBLISHED_OUTPUT"]).resolve()
    output_root = Path(environ["AGE_PUBLISH_OUTPUT_ROOT"]).resolve()
    assembly = environ["AGE_PUBLISH_ASSEMBLY"]
    output = request.output.resolve()

    if request.project.resolve() != expected_project:
        raise ValueError(f"unexpected publish project: {request.project}")
    if request.configuration != "ExportRelease":
        raise ValueError(f"unexpected publish configuration: {request.configuration}")
    if request.runtime != "linux-x64":
        raise ValueError(f"unexpected publish runtime: {request.runtime}")
    if request.self_contained.lower() != "true":
        raise ValueError(f"publish is not self-contained: {request.self_contained}")
    if output == output_root or not _is_within(output, output_root):
        raise ValueError(f"publish output is outside the reserved root: {output}")
    if not (source / assembly).is_file():
        raise ValueError(f"prepublished assembly was not found: {source / assembly}")

    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError(f"publish output directory is not empty: {output}")
    for child in source.iterdir():
        target = output / child.name
        if child.is_dir():
            shutil.copytree(child, target, symlinks=True)
        else:
            shutil.copy2(child, target, follow_symlinks=False)
    return output


def main(arguments: list[str] | None = None) -> int:
    args = list(sys.argv[1:] if arguments is None else arguments)
    real_dotnet = os.environ.get("AGE_REAL_DOTNET")
    if not real_dotnet:
        raise ValueError("AGE_REAL_DOTNET is required")
    if not args or args[0] != "publish":
        os.execv(real_dotnet, [real_dotnet, *args])
    request = parse_publish(args)
    output = stage_publish(request, dict(os.environ))
    print(f"publish proxy: staged {output}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (KeyError, OSError, ValueError) as error:
        print(f"dotnet publish proxy refused request: {error}", file=sys.stderr)
        raise SystemExit(2)
