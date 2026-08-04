#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import io
import json
import tarfile
import tempfile
import unittest
import zipfile
from pathlib import Path
from typing import Any

import publish_gitea_release
import verify_windows_native


TARGET = "0123456789abcdef0123456789abcdef01234567"
TAG = "v0.2.0"


class FakeApi:
    def __init__(self, release: dict[str, Any] | None = None) -> None:
        self.release = release
        self.assets: list[dict[str, Any]] = []
        self.created_payload: dict[str, Any] | None = None

    def get_release(self, tag: str) -> dict[str, Any] | None:
        self.requested_tag = tag
        return self.release

    def create_release(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.created_payload = payload
        self.release = matching_release()
        return self.release

    def list_assets(self, release_id: int) -> list[dict[str, Any]]:
        self.requested_release_id = release_id
        return list(self.assets)

    def upload(self, release_id: int, asset: Path) -> dict[str, Any]:
        uploaded = {"name": asset.name, "size": asset.stat().st_size}
        self.assets.append(uploaded)
        return uploaded


def matching_release() -> dict[str, Any]:
    return {
        "id": 17,
        "tag_name": TAG,
        "target_commitish": TARGET,
        "name": f"OpenMaidEngine Himegari {TAG}",
        "draft": False,
        "prerelease": False,
        "html_url": "https://gitea.invalid/releases/tag/v0.2.0",
    }


def json_bytes(value: dict[str, Any]) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()


def build_info(rid: str) -> bytes:
    return json_bytes({
        "schema_version": 1,
        "source_commit": TARGET,
        "source_dirty": False,
        "target": rid,
    })


def windows_verification() -> bytes:
    return json_bytes({
        "schema_version": 1,
        "target": "win-x64",
        "executable_machine": "AMD64",
        "native": {
            "schema_version": 1,
            "target": "win-x64",
            "bundle": "data_Himegari_windows_x86_64",
            "machines": {
                name: "AMD64"
                for name in (verify_windows_native.SHIM, *verify_windows_native.RUNTIME_DLLS)
            },
            "shim_exports": sorted(verify_windows_native.REQUIRED_EXPORTS),
            "shim_imports": [*verify_windows_native.RUNTIME_DLLS, "KERNEL32.dll"],
        },
    })


def add_tar_bytes(archive: tarfile.TarFile, name: str, data: bytes) -> None:
    info = tarfile.TarInfo(name)
    info.size = len(data)
    archive.addfile(info, io.BytesIO(data))


def write_checksum(directory: Path, archive: Path) -> None:
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    (directory / f"{archive.name}.sha256").write_text(
        f"{digest}  {archive.name}\n", encoding="ascii"
    )


def create_artifacts(root: Path) -> tuple[Path, Path, Path]:
    linux = root / "linux"
    windows = root / "windows"
    prepared = root / "prepared"
    linux.mkdir()
    windows.mkdir()

    linux_info = build_info("linux-x64")
    linux_ledger = f"{'0' * 64}  Himegari.x86_64\n".encode()
    linux_archive = linux / publish_gitea_release.LINUX_ARCHIVE
    with tarfile.open(linux_archive, "w:gz") as archive:
        add_tar_bytes(
            archive,
            f"{publish_gitea_release.LINUX_PACKAGE_ROOT}/BUILD-INFO.json",
            linux_info,
        )
        add_tar_bytes(
            archive,
            f"{publish_gitea_release.LINUX_PACKAGE_ROOT}/SHA256SUMS",
            linux_ledger,
        )
    (linux / "BUILD-INFO.json").write_bytes(linux_info)
    (linux / "SHA256SUMS").write_bytes(linux_ledger)
    (linux / "package-smoke.log").write_text(
        publish_gitea_release.SMOKE_MARKER + "\n", encoding="utf-8"
    )
    write_checksum(linux, linux_archive)

    windows_info = build_info("win-x64")
    windows_ledger = f"{'1' * 64}  Himegari.exe\n".encode()
    verification = windows_verification()
    windows_archive = windows / publish_gitea_release.WINDOWS_ARCHIVE
    with zipfile.ZipFile(windows_archive, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        root_name = publish_gitea_release.WINDOWS_PACKAGE_ROOT
        archive.writestr(f"{root_name}/BUILD-INFO.json", windows_info)
        archive.writestr(f"{root_name}/SHA256SUMS", windows_ledger)
        archive.writestr(f"{root_name}/WINDOWS-VERIFICATION.json", verification)
    (windows / "BUILD-INFO.json").write_bytes(windows_info)
    (windows / "SHA256SUMS").write_bytes(windows_ledger)
    (windows / "WINDOWS-VERIFICATION.json").write_bytes(verification)
    write_checksum(windows, windows_archive)
    return linux, windows, prepared


class PublishGiteaReleaseTests(unittest.TestCase):
    def test_creates_release_and_uploads_three_verified_assets(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            linux, windows, prepared = create_artifacts(Path(temporary))
            api = FakeApi()
            result = publish_gitea_release.promote_release(
                api, api.upload, TAG, TARGET, linux, windows, prepared
            )
            self.assertEqual(17, result["id"])
            self.assertEqual(TAG, api.created_payload["tag_name"])
            self.assertEqual(TARGET, api.created_payload["target_commitish"])
            self.assertIn("Windows archive", api.created_payload["body"])
            self.assertEqual(
                publish_gitea_release.EXPECTED_RELEASE_ASSETS,
                {asset["name"] for asset in api.assets},
            )
            checksums = (prepared / publish_gitea_release.RELEASE_CHECKSUMS).read_text(
                encoding="ascii"
            )
            self.assertIn(publish_gitea_release.LINUX_ARCHIVE, checksums)
            self.assertIn(publish_gitea_release.WINDOWS_ARCHIVE, checksums)

    def test_retry_keeps_matching_assets_and_uploads_only_missing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            linux, windows, prepared = create_artifacts(Path(temporary))
            assets = publish_gitea_release.prepare_release_assets(
                TAG, TARGET, linux, windows, prepared
            )
            api = FakeApi(matching_release())
            api.assets.append({"name": assets[0].name, "size": assets[0].stat().st_size})
            uploaded: list[str] = []

            def upload(release_id: int, asset: Path) -> dict[str, Any]:
                uploaded.append(asset.name)
                return api.upload(release_id, asset)

            publish_gitea_release.promote_release(
                api, upload, TAG, TARGET, linux, windows, prepared
            )
            self.assertNotIn(assets[0].name, uploaded)
            self.assertEqual(2, len(uploaded))
            self.assertIsNone(api.created_payload)

    def test_refuses_mismatched_release_and_asset_collision(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            linux, windows, prepared = create_artifacts(Path(temporary))
            wrong_release = matching_release()
            wrong_release["target_commitish"] = "f" * 40
            api = FakeApi(wrong_release)
            with self.assertRaisesRegex(ValueError, "does not match"):
                publish_gitea_release.promote_release(
                    api, api.upload, TAG, TARGET, linux, windows, prepared
                )

            api = FakeApi(matching_release())
            api.assets.append({"name": publish_gitea_release.LINUX_ARCHIVE, "size": 999})
            with self.assertRaisesRegex(ValueError, "will not be overwritten"):
                publish_gitea_release.promote_release(
                    api, api.upload, TAG, TARGET, linux, windows, prepared
                )

            api = FakeApi(matching_release())
            api.assets.append({"name": "stale.txt", "size": 1})
            with self.assertRaisesRegex(ValueError, "unexpected assets"):
                publish_gitea_release.promote_release(
                    api, api.upload, TAG, TARGET, linux, windows, prepared
                )

    def test_refuses_mismatched_archive_commit_and_structural_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            linux, windows, prepared = create_artifacts(Path(temporary))
            (linux / publish_gitea_release.LINUX_ARCHIVE).write_bytes(b"changed")
            with self.assertRaisesRegex(ValueError, "external SHA-256"):
                publish_gitea_release.prepare_release_assets(
                    TAG, TARGET, linux, windows, prepared
                )

        with tempfile.TemporaryDirectory() as temporary:
            linux, windows, prepared = create_artifacts(Path(temporary))
            info = json.loads((windows / "BUILD-INFO.json").read_text(encoding="utf-8"))
            info["source_commit"] = "f" * 40
            (windows / "BUILD-INFO.json").write_bytes(json_bytes(info))
            with self.assertRaisesRegex(ValueError, "does not match"):
                publish_gitea_release.prepare_release_assets(
                    TAG, TARGET, linux, windows, prepared
                )

        with tempfile.TemporaryDirectory() as temporary:
            linux, windows, prepared = create_artifacts(Path(temporary))
            report = json.loads(
                (windows / "WINDOWS-VERIFICATION.json").read_text(encoding="utf-8")
            )
            report["executable_machine"] = "I386"
            (windows / "WINDOWS-VERIFICATION.json").write_bytes(json_bytes(report))
            with self.assertRaisesRegex(ValueError, "not accepted"):
                publish_gitea_release.prepare_release_assets(
                    TAG, TARGET, linux, windows, prepared
                )


if __name__ == "__main__":
    unittest.main()
