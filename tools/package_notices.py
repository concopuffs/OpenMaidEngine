#!/usr/bin/env python3
"""Third-party notices the release packages carry (THIRD_PARTY_NOTICES.md).

Both packagers use this module:
- the pinned notice texts under notices/, checked against their pins and copied to the package root: Godot's
  LICENSE.txt and COPYRIGHT.txt for the Godot release the packages are exported with (both packages), and the
  MinGW-w64 runtime's COPYING.MinGW-w64-runtime.txt for the runtime the Windows DLLs link statically (Windows only;
  tools/mingw_runtime_notices.py checks the links against it);
- the check that THIRD_PARTY_NOTICES.md names every NuGet package a publish ships, read from the engine publish's
  OME.deps.json and from the dependency list the launcher's publish kept (its single file bundles its own). A runtime
  pack is named by its framework (Microsoft.NETCore.App).
The NuGet packages' own license files reach the package through the publish itself (Directory.Build.targets,
target PublishPackageNotices): the engine's into its managed directory, the launcher's into OME-Launcher-notices/.
"""
from __future__ import annotations

import hashlib
import json
import re
import shutil
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
GODOT_NOTICES = REPO / "notices/godot/notices.json"
MINGW_NOTICES = REPO / "notices/mingw-w64/notices.json"
NOTO_NOTICES = REPO / "notices/noto-sans-jp/notices.json"
DEPS_FILE = "OME.deps.json"
_ASSET_KINDS = ("runtime", "native", "runtimeTargets", "resources")


@dataclass(frozen=True)
class PinnedNotice:
    source: Path
    package_name: str
    sha256: str


def pinned_notices(manifest: Path = GODOT_NOTICES) -> tuple[PinnedNotice, ...]:
    """The notice files a manifest pins, with their sources beside it."""
    with manifest.open(encoding="utf-8") as stream:
        data = json.load(stream)
    return tuple(
        PinnedNotice(manifest.parent / entry["path"], entry["package_name"], entry["sha256"])
        for entry in data["files"]
    )


def notices_release_tag(manifest: Path = GODOT_NOTICES) -> str:
    with manifest.open(encoding="utf-8") as stream:
        return json.load(stream)["release_tag"]


def copy_pinned_notices(notices: tuple[PinnedNotice, ...], package_root: Path) -> None:
    """Copy each pinned notice to the package root under its package name; fail when one is missing or differs from
    its pin."""
    for notice in notices:
        if not notice.source.is_file():
            raise ValueError(f"package notice file was not found: {notice.source}")
        digest = hashlib.sha256(notice.source.read_bytes()).hexdigest()
        if digest != notice.sha256:
            raise ValueError(f"package notice file does not match its pin: {notice.source}")
        shutil.copy2(notice.source, package_root / notice.package_name)


def shipped_packages(deps_file: Path) -> list[str]:
    """The NuGet packages whose files a publish ships: package and runtime-pack libraries with runtime, native or
    resource assets for the publish's runtime target. Analyzer-only packages ship nothing and are left out."""
    with deps_file.open(encoding="utf-8") as stream:
        deps = json.load(stream)
    target = deps["targets"][deps["runtimeTarget"]["name"]]
    names: set[str] = set()
    for key, library in deps["libraries"].items():
        kind = library.get("type")
        if kind not in ("package", "runtimepack"):
            continue
        assets = target.get(key, {})
        if not any(assets.get(asset) for asset in _ASSET_KINDS):
            continue
        name = key.split("/", 1)[0]
        if kind == "runtimepack":
            name = re.sub(r"\.Runtime\.[^.]+$", "", name.removeprefix("runtimepack."))
        names.add(name)
    return sorted(names)


def verify_dependency_notices(managed_directory: Path, notices_file: Path) -> list[str]:
    """Fail unless the notices file names every NuGet package the engine's publish ships in backticks (`Tomlyn`);
    return them."""
    deps_file = managed_directory / DEPS_FILE
    if not deps_file.is_file():
        raise ValueError(f"publish dependency file was not found: {deps_file}")
    return _named(shipped_packages(deps_file), notices_file, "shipped NuGet packages")


def verify_launcher_notices(deps_file: Path, notices_file: Path) -> list[str]:
    """The same for the launcher, whose single file bundles its dependency list: `deps_file` is the copy its publish
    kept (tools/publish_launcher.py, launcher plan §9.2)."""
    if not deps_file.is_file():
        raise ValueError(f"the launcher's dependency list was not found: {deps_file}; run tools/publish_launcher.py")
    return _named(shipped_packages(deps_file), notices_file, "the launcher's shipped NuGet packages")


def _named(packages: list[str], notices_file: Path, what: str) -> list[str]:
    text = notices_file.read_text(encoding="utf-8")
    missing = [name for name in packages if f"`{name}`" not in text]
    if missing:
        raise ValueError(f"{notices_file.name} does not name {what}: " + ", ".join(missing))
    return packages
