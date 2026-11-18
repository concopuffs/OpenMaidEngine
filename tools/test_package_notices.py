#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import package_notices

HERE = Path(__file__).resolve().parent


def write_deps(path: Path, rid: str = "linux-x64") -> Path:
    """The engine's dependency list as its publish keeps it (tools/publish_engine.py): projects, the runtime pack, an
    analyzer-only package and two packages with runtime assets."""
    target = f".NETCoreApp,Version=v10.0/{rid}"
    runtime_pack = f"runtimepack.Microsoft.NETCore.App.Runtime.{rid}/10.0.15"
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
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(deps), encoding="utf-8")
    return path


# The packages write_launcher_deps ships, as THIRD_PARTY_NOTICES.md must name them.
LAUNCHER_FIXTURE_PACKAGES = ("Avalonia", "Microsoft.NETCore.App", "SkiaSharp.NativeAssets.Linux",
                             "SkiaSharp.NativeAssets.Win32")


def write_launcher_deps(path: Path, rid: str = "linux-x64") -> Path:
    """A dependency list as the launcher's publish keeps it (net10.0, bundled into its single file): projects, the
    runtime pack, Avalonia, and SkiaSharp's native package for the target."""
    target = f".NETCoreApp,Version=v10.0/{rid}"
    runtime_pack = f"runtimepack.Microsoft.NETCore.App.Runtime.{rid}/10.0.15"
    natives = f"SkiaSharp.NativeAssets.{'Linux' if rid == 'linux-x64' else 'Win32'}/3.119.4"
    deps = {
        "runtimeTarget": {"name": target, "signature": ""},
        "targets": {
            target: {
                "OME-Launcher/1.0.0": {"runtime": {"OME-Launcher.dll": {}}},
                runtime_pack: {"runtime": {"System.Private.CoreLib.dll": {}}},
                "Avalonia/12.1.6": {"runtime": {"lib/net10.0/Avalonia.Base.dll": {}}},
                natives: {"native": {"libSkiaSharp": {}}},
                "Ome.Launcher.Core/1.0.0": {"runtime": {"Ome.Launcher.Core.dll": {}}},
            }
        },
        "libraries": {
            "OME-Launcher/1.0.0": {"type": "project", "serviceable": False, "sha512": ""},
            runtime_pack: {"type": "runtimepack", "serviceable": False, "sha512": ""},
            "Avalonia/12.1.6": {"type": "package", "serviceable": True, "sha512": "x"},
            natives: {"type": "package", "serviceable": True, "sha512": "x"},
            "Ome.Launcher.Core/1.0.0": {"type": "project", "serviceable": False, "sha512": ""},
        },
    }
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

    def test_committed_noto_notice_matches_its_pin_and_ships_in_both_packages(self) -> None:
        import package_linux_x64
        import package_windows_x64

        notices = package_notices.pinned_notices(package_notices.NOTO_NOTICES)
        self.assertEqual(["NotoSansJP-LICENSE.txt"], [n.package_name for n in notices])
        for notice in notices:
            self.assertEqual(notice.sha256, hashlib.sha256(notice.source.read_bytes()).hexdigest(), notice.source)
        tag = package_notices.notices_release_tag(package_notices.NOTO_NOTICES)
        with package_notices.NOTO_NOTICES.open(encoding="utf-8") as stream:
            sources = [entry["source"] for entry in json.load(stream)["files"]]
        self.assertTrue(all(f"/{tag}/" in source for source in sources), sources)
        for package in (package_windows_x64, package_linux_x64):
            self.assertIn("NotoSansJP-LICENSE.txt", [n.package_name for n in package.PINNED_NOTICES])
        # The font OME.dll embeds is the file THIRD_PARTY_NOTICES.md names.
        font = HERE.parent / "godot/assets/NotoSansJP-Regular.otf"
        digest = hashlib.sha256(font.read_bytes()).hexdigest()
        self.assertIn(digest, (HERE.parent / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8"))

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
            deps = write_deps(Path(temporary) / "win-x64.deps.json", "win-x64")
            self.assertEqual(
                ["GodotSharp", "Microsoft.NETCore.App", "Tomlyn"], package_notices.shipped_packages(deps)
            )

    def test_notices_must_name_every_shipped_package(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            # The NativeAOT library holds the packages, so the list is the one the engine's publish kept beside it.
            deps = write_deps(root / "engine-publish/linux-x64.deps.json")
            notices = root / "THIRD_PARTY_NOTICES.md"
            notices.write_text("`GodotSharp`, `Microsoft.NETCore.App`, `Tomlyn`\n", encoding="utf-8")
            self.assertEqual(
                ["GodotSharp", "Microsoft.NETCore.App", "Tomlyn"],
                package_notices.verify_dependency_notices(deps, notices),
            )
            notices.write_text("`GodotSharp`, `Microsoft.NETCore.App`, Tomlyn\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "does not name shipped NuGet packages: Tomlyn"):
                package_notices.verify_dependency_notices(deps, notices)
            with self.assertRaisesRegex(ValueError, "engine's dependency list was not found.*publish_engine"):
                package_notices.verify_dependency_notices(root / "elsewhere.deps.json", notices)

    def test_project_notices_name_the_packages_the_port_ships(self) -> None:
        # The NuGet packages a self-contained publish of godot/OME.csproj ships (2026-11-16, win-x64 on net10.0;
        # .NET 10 holds the code pages itself, so System.Text.Encoding.CodePages is no longer one of them).
        text = (package_notices.REPO / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8")
        for name in ("GodotSharp", "LuaCSharp", "LuaCSharp.Annotations", "Microsoft.NETCore.App", "Tomlyn"):
            self.assertIn(f"`{name}`", text)

    def test_the_launchers_list_is_checked_from_the_copy_its_publish_kept(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            deps = write_launcher_deps(root / "launcher-publish/linux-x64.deps.json")
            notices = root / "THIRD_PARTY_NOTICES.md"
            notices.write_text(" ".join(f"`{name}`" for name in LAUNCHER_FIXTURE_PACKAGES) + "\n", encoding="utf-8")
            self.assertEqual(["Avalonia", "Microsoft.NETCore.App", "SkiaSharp.NativeAssets.Linux"],
                             package_notices.verify_launcher_notices(deps, notices))
            notices.write_text("`Microsoft.NETCore.App` `SkiaSharp.NativeAssets.Linux`\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "does not name the launcher's shipped NuGet packages: Avalonia"):
                package_notices.verify_launcher_notices(deps, notices)
            with self.assertRaisesRegex(ValueError, "launcher's dependency list was not found.*publish_launcher"):
                package_notices.verify_launcher_notices(root / "missing.deps.json", notices)

    def test_project_notices_name_the_packages_the_launcher_ships(self) -> None:
        # The NuGet packages the launcher's release publish ships (launcher plan §9, 2026-11-16, both RIDs; the release
        # build checks its kept list, this keeps the file honest between releases).
        text = (package_notices.REPO / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8")
        for name in (
            "Avalonia", "Avalonia.Desktop", "Avalonia.FreeDesktop", "Avalonia.FreeDesktop.AtSpi", "Avalonia.HarfBuzz",
            "Avalonia.Native", "Avalonia.Remote.Protocol", "Avalonia.Skia", "Avalonia.Themes.Fluent", "Avalonia.Win32",
            "Avalonia.X11", "Avalonia.Angle.Windows.Natives", "HarfBuzzSharp", "HarfBuzzSharp.NativeAssets.Linux",
            "HarfBuzzSharp.NativeAssets.Win32", "SkiaSharp", "SkiaSharp.NativeAssets.Linux",
            "SkiaSharp.NativeAssets.Win32", "MicroCom.Runtime", "Tmds.DBus.Protocol", "LuaCSharp",
            "LuaCSharp.Annotations", "Tomlyn", "Microsoft.NETCore.App",
        ):
            self.assertIn(f"`{name}`", text)


if __name__ == "__main__":
    unittest.main()
