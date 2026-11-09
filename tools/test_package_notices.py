#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import package_notices

HERE = Path(__file__).resolve().parent


def write_deps(directory: Path, rid: str = "linux-x64") -> Path:
    """An OME.deps.json as a self-contained publish writes it: projects, the runtime pack, an analyzer-only package and
    two packages with runtime assets."""
    target = f".NETCoreApp,Version=v8.0/{rid}"
    runtime_pack = f"runtimepack.Microsoft.NETCore.App.Runtime.{rid}/8.0.15"
    deps = {
        "runtimeTarget": {"name": target, "signature": ""},
        "targets": {
            target: {
                "OME/1.0.0": {"dependencies": {"Tomlyn": "2.10.1"}, "runtime": {"OME.dll": {}}},
                runtime_pack: {"runtime": {"System.Private.CoreLib.dll": {}}, "native": {"coreclr.dll": {}}},
                "Godot.SourceGenerators/4.7.0": {},
                "GodotSharp/4.7.0": {"runtime": {"lib/net8.0/GodotSharp.dll": {}}},
                "Tomlyn/2.10.1": {"runtime": {"lib/net8.0/Tomlyn.dll": {}}},
                "Ome.Engine/1.0.0": {"runtime": {"Ome.Engine.dll": {}}},
            }
        },
        "libraries": {
            "OME/1.0.0": {"type": "project", "serviceable": False, "sha512": ""},
            runtime_pack: {"type": "runtimepack", "serviceable": False, "sha512": ""},
            "Godot.SourceGenerators/4.7.0": {"type": "package", "serviceable": True, "sha512": "x"},
            "GodotSharp/4.7.0": {"type": "package", "serviceable": True, "sha512": "x"},
            "Tomlyn/2.10.1": {"type": "package", "serviceable": True, "sha512": "x"},
            "Ome.Engine/1.0.0": {"type": "project", "serviceable": False, "sha512": ""},
        },
    }
    path = directory / package_notices.DEPS_FILE
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(deps), encoding="utf-8")
    return path


class PackageNoticesTests(unittest.TestCase):
    def test_committed_godot_notices_match_their_pins_and_the_pinned_godot_release(self) -> None:
        notices = package_notices.pinned_notices()
        self.assertEqual(
            ["Godot-COPYRIGHT.txt", "Godot-LICENSE.txt"], sorted(n.package_name for n in notices)
        )
        for notice in notices:
            self.assertEqual(notice.sha256, hashlib.sha256(notice.source.read_bytes()).hexdigest(), notice.source)
        with (HERE / "godot-linux-x64.json").open(encoding="utf-8") as stream:
            godot = json.load(stream)
        # A Godot bump must refresh notices/godot/ from the new tag.
        self.assertEqual(godot["release_tag"], package_notices.notices_release_tag())
        with package_notices.GODOT_NOTICES.open(encoding="utf-8") as stream:
            sources = [entry["source"] for entry in json.load(stream)["files"]]
        self.assertTrue(all(f"/{godot['release_tag']}/" in source for source in sources), sources)

    def test_committed_mingw_notice_matches_its_pin_and_ships_only_in_the_windows_package(self) -> None:
        import package_linux_x64
        import package_windows_x64

        notices = package_notices.pinned_notices(package_notices.MINGW_NOTICES)
        self.assertEqual(["MinGW-w64-runtime-COPYING.txt"], [n.package_name for n in notices])
        for notice in notices:
            self.assertEqual(notice.sha256, hashlib.sha256(notice.source.read_bytes()).hexdigest(), notice.source)
        tag = package_notices.notices_release_tag(package_notices.MINGW_NOTICES)
        with package_notices.MINGW_NOTICES.open(encoding="utf-8") as stream:
            sources = [entry["source"] for entry in json.load(stream)["files"]]
        self.assertTrue(all(f"/{tag}/" in source for source in sources), sources)
        self.assertIn("MinGW-w64-runtime-COPYING.txt", [n.package_name for n in package_windows_x64.PINNED_NOTICES])
        self.assertNotIn("MinGW-w64-runtime-COPYING.txt", [n.package_name for n in package_linux_x64.PINNED_NOTICES])

    def test_copy_checks_each_pin(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "LICENSE.txt"
            source.write_bytes(b"license\n")
            package = root / "package"
            package.mkdir()
            good = package_notices.PinnedNotice(source, "Godot-LICENSE.txt", hashlib.sha256(b"license\n").hexdigest())
            package_notices.copy_pinned_notices((good,), package)
            self.assertEqual(b"license\n", (package / "Godot-LICENSE.txt").read_bytes())
            with self.assertRaisesRegex(ValueError, "does not match its pin"):
                package_notices.copy_pinned_notices(
                    (package_notices.PinnedNotice(source, "Godot-LICENSE.txt", "0" * 64),), package
                )
            with self.assertRaisesRegex(ValueError, "was not found"):
                package_notices.copy_pinned_notices(
                    (package_notices.PinnedNotice(root / "missing.txt", "x.txt", good.sha256),), package
                )

    def test_shipped_packages_are_the_nuget_libraries_with_assets(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            deps = write_deps(Path(temporary), "win-x64")
            self.assertEqual(
                ["GodotSharp", "Microsoft.NETCore.App", "Tomlyn"], package_notices.shipped_packages(deps)
            )

    def test_notices_must_name_every_shipped_package(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            write_deps(root / "managed")
            notices = root / "THIRD_PARTY_NOTICES.md"
            notices.write_text("`GodotSharp`, `Microsoft.NETCore.App`, `Tomlyn`\n", encoding="utf-8")
            self.assertEqual(
                ["GodotSharp", "Microsoft.NETCore.App", "Tomlyn"],
                package_notices.verify_dependency_notices(root / "managed", notices),
            )
            notices.write_text("`GodotSharp`, `Microsoft.NETCore.App`, Tomlyn\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "does not name shipped NuGet packages: Tomlyn"):
                package_notices.verify_dependency_notices(root / "managed", notices)
            with self.assertRaisesRegex(ValueError, "dependency file was not found"):
                package_notices.verify_dependency_notices(root / "elsewhere", notices)

    def test_project_notices_name_the_packages_the_port_ships(self) -> None:
        # The NuGet packages a self-contained publish of godot/OME.csproj ships (2026-11-06, both RIDs).
        text = (package_notices.REPO / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8")
        for name in ("GodotSharp", "Microsoft.NETCore.App", "System.Text.Encoding.CodePages", "Tomlyn"):
            self.assertIn(f"`{name}`", text)


if __name__ == "__main__":
    unittest.main()
