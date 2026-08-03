#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from typing import Any

import publish_gitea_release


TARGET = "0123456789abcdef0123456789abcdef01234567"
TAG = "v0.1.0"


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
        "html_url": "https://gitea.invalid/releases/tag/v0.1.0",
    }


def create_assets(root: Path) -> list[Path]:
    archive = root / "OpenMaidEngine-Himegari-linux-x64.tar.gz"
    archive.write_bytes(b"synthetic archive")
    checksum = root / "OpenMaidEngine-Himegari-linux-x64.tar.gz.sha256"
    checksum.write_text(
        f"{hashlib.sha256(archive.read_bytes()).hexdigest()}  {archive.name}\n",
        encoding="ascii",
    )
    build_info = root / "BUILD-INFO.json"
    build_info.write_text(json.dumps({
        "schema_version": 1,
        "source_commit": TARGET,
        "source_dirty": False,
        "target": "linux-x64",
    }), encoding="utf-8")
    ledger = root / "SHA256SUMS"
    ledger.write_text(f"{'0' * 64}  Himegari.x86_64\n", encoding="ascii")
    smoke = root / "package-smoke.log"
    smoke.write_text("PACKAGE SMOKE OK: opcodes=548 ffmpeg-abi=3\n", encoding="utf-8")
    return [archive, checksum, build_info, ledger, smoke]


class PublishGiteaReleaseTests(unittest.TestCase):
    def test_creates_release_and_uploads_verified_asset_set(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            assets = create_assets(Path(temporary))
            api = FakeApi()
            result = publish_gitea_release.promote_release(api, api.upload, TAG, TARGET, assets)
            self.assertEqual(17, result["id"])
            self.assertEqual(TAG, api.created_payload["tag_name"])
            self.assertEqual(TARGET, api.created_payload["target_commitish"])
            self.assertEqual({asset.name for asset in assets}, {asset["name"] for asset in api.assets})

    def test_retry_keeps_matching_assets_and_uploads_only_missing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            assets = create_assets(Path(temporary))
            api = FakeApi(matching_release())
            api.assets.append({"name": assets[0].name, "size": assets[0].stat().st_size})
            uploaded: list[str] = []

            def upload(release_id: int, asset: Path) -> dict[str, Any]:
                uploaded.append(asset.name)
                return api.upload(release_id, asset)

            publish_gitea_release.promote_release(api, upload, TAG, TARGET, assets)
            self.assertNotIn(assets[0].name, uploaded)
            self.assertEqual(len(assets) - 1, len(uploaded))
            self.assertIsNone(api.created_payload)

    def test_refuses_mismatched_release_and_asset_collision(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            assets = create_assets(Path(temporary))
            wrong_release = matching_release()
            wrong_release["target_commitish"] = "f" * 40
            api = FakeApi(wrong_release)
            with self.assertRaisesRegex(ValueError, "does not match"):
                publish_gitea_release.promote_release(api, api.upload, TAG, TARGET, assets)

            api = FakeApi(matching_release())
            api.assets.append({"name": assets[0].name, "size": 999})
            with self.assertRaisesRegex(ValueError, "will not be overwritten"):
                publish_gitea_release.promote_release(api, api.upload, TAG, TARGET, assets)

    def test_refuses_mismatched_build_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            assets = create_assets(Path(temporary))
            by_name = {asset.name: asset for asset in assets}
            by_name["OpenMaidEngine-Himegari-linux-x64.tar.gz"].write_bytes(b"changed")
            with self.assertRaisesRegex(ValueError, "external SHA-256"):
                publish_gitea_release.validate_inputs(TAG, TARGET, assets)

            assets = create_assets(Path(temporary))
            by_name = {asset.name: asset for asset in assets}
            build_info = json.loads(by_name["BUILD-INFO.json"].read_text(encoding="utf-8"))
            build_info["source_commit"] = "f" * 40
            by_name["BUILD-INFO.json"].write_text(json.dumps(build_info), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "does not match"):
                publish_gitea_release.validate_inputs(TAG, TARGET, assets)


if __name__ == "__main__":
    unittest.main()
