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
            project = root / "Himegari.csproj"
            project.write_text("<Project />", encoding="utf-8")
            source = root / "prepublished"
            source.mkdir()
            (source / "Himegari.dll").write_bytes(b"managed")
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
                "AGE_PUBLISH_ASSEMBLY": "Himegari.dll",
            })
            self.assertEqual(output.resolve(), staged)
            self.assertEqual(b"managed", (staged / "Himegari.dll").read_bytes())
            self.assertEqual(b"runtime", (staged / "libhostfxr.so").read_bytes())

    def test_rejects_drift_and_output_escape(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project = root / "Himegari.csproj"
            project.write_text("<Project />", encoding="utf-8")
            source = root / "prepublished"
            source.mkdir()
            (source / "Himegari.dll").write_bytes(b"managed")
            environment = {
                "AGE_PUBLISH_PROJECT": str(project),
                "AGE_PREPUBLISHED_OUTPUT": str(source),
                "AGE_PUBLISH_OUTPUT_ROOT": str(root / "reserved"),
                "AGE_PUBLISH_ASSEMBLY": "Himegari.dll",
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


if __name__ == "__main__":
    unittest.main()
