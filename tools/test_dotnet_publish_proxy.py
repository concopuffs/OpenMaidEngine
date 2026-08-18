#!/usr/bin/env python3
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

import dotnet_publish_proxy


class DotnetPublishProxyTests(unittest.TestCase):
    def test_stages_exact_expected_publish(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project = root / "OME.csproj"
            project.write_text("<Project />", encoding="utf-8")
            source = root / "prepublished"
            source.mkdir()
            (source / "OME.dll").write_bytes(b"managed")
            (source / "libhostfxr.so").write_bytes(b"runtime")
            output_root = root / "godot-publish-dotnet"
            output = output_root / "123-ExportRelease-linux-x64"
            request = dotnet_publish_proxy.parse_publish([
                "publish", str(project), "--self-contained", "true",
                "-c", "ExportRelease", "-r", "linux-x64", "-o", str(output),
                "-p:GodotTargetPlatform=linuxbsd",
            ])
            staged = dotnet_publish_proxy.stage_publish(request, {
                "AGE_PUBLISH_PROJECT": str(project),
                "AGE_PREPUBLISHED_OUTPUT": str(source),
                "AGE_PUBLISH_OUTPUT_ROOT": str(output_root),
                "AGE_PUBLISH_ASSEMBLY": "OME.dll",
                "AGE_PUBLISH_RUNTIME": "linux-x64",
            })
            self.assertEqual(output.resolve(), staged)
            self.assertEqual(b"managed", (staged / "OME.dll").read_bytes())
            self.assertEqual(b"runtime", (staged / "libhostfxr.so").read_bytes())

    def test_rejects_drift_and_output_escape(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project = root / "OME.csproj"
            project.write_text("<Project />", encoding="utf-8")
            source = root / "prepublished"
            source.mkdir()
            (source / "OME.dll").write_bytes(b"managed")
            environment = {
                "AGE_PUBLISH_PROJECT": str(project),
                "AGE_PREPUBLISHED_OUTPUT": str(source),
                "AGE_PUBLISH_OUTPUT_ROOT": str(root / "reserved"),
                "AGE_PUBLISH_ASSEMBLY": "OME.dll",
                "AGE_PUBLISH_RUNTIME": "linux-x64",
            }
            wrong_runtime = dotnet_publish_proxy.PublishRequest(
                project, "ExportRelease", "win-x64", "true", root / "reserved/output"
            )
            with self.assertRaisesRegex(ValueError, "runtime"):
                dotnet_publish_proxy.stage_publish(wrong_runtime, environment)
            escaped = dotnet_publish_proxy.PublishRequest(
                project, "ExportRelease", "linux-x64", "true", root / "outside"
            )
            with self.assertRaisesRegex(ValueError, "outside"):
                dotnet_publish_proxy.stage_publish(escaped, environment)

    def test_stages_windows_publish_when_explicitly_selected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project = root / "OME.csproj"
            project.write_text("<Project />", encoding="utf-8")
            source = root / "prepublished"
            source.mkdir()
            (source / "OME.dll").write_bytes(b"managed")
            output = root / "reserved/123-ExportRelease-win-x64"
            request = dotnet_publish_proxy.PublishRequest(
                project, "ExportRelease", "win-x64", "true", output
            )
            staged = dotnet_publish_proxy.stage_publish(request, {
                "AGE_PUBLISH_PROJECT": str(project),
                "AGE_PREPUBLISHED_OUTPUT": str(source),
                "AGE_PUBLISH_OUTPUT_ROOT": str(root / "reserved"),
                "AGE_PUBLISH_ASSEMBLY": "OME.dll",
                "AGE_PUBLISH_RUNTIME": "win-x64",
            })
            self.assertEqual(output.resolve(), staged)


if __name__ == "__main__":
    unittest.main()
