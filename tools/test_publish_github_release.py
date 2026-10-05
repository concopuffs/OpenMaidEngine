#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import io
import json
import subprocess
import tarfile
import tempfile
import unittest
import zipfile
from pathlib import Path
from typing import Any
from unittest import mock

import publish_github_release
import verify_windows_native


TARGET = "0123456789abcdef0123456789abcdef01234567"
TAG = "v0.2.0"
TITLE = f"Open Maid Engine {TAG}"


class FakeApi:
    """In-memory GitHub releases API: drafts are invisible to get_release, as on GitHub."""

    def __init__(
        self,
        release: dict[str, Any] | None = None,
        *,
        tag_commit: str = TARGET,
        extra_drafts: int = 0,
    ) -> None:
        self.release = release
        self.extra_drafts = extra_drafts
        self.commit = tag_commit
        self.assets: list[dict[str, Any]] = []
        self.created_payload: dict[str, Any] | None = None
        self.deleted: list[int] = []
        self.published = False
        self.events: list[str] = []
        self.next_asset_id = 100

    def tag_commit(self, tag: str) -> str:
        self.events.append("tag_commit")
        return self.commit

    def get_release(self, tag: str) -> dict[str, Any] | None:
        if self.release is not None and not self.release["draft"]:
            return self.release
        return None

    def find_draft_releases(self, tag: str) -> list[dict[str, Any]]:
        drafts = [self.release] if self.release is not None and self.release["draft"] else []
        return drafts + [draft_release() for _ in range(self.extra_drafts)]

    def create_release(self, payload: dict[str, Any]) -> dict[str, Any]:
        self.events.append("create")
        self.created_payload = payload
        self.release = draft_release()
        return self.release

    def list_assets(self, release_id: int) -> list[dict[str, Any]]:
        return [dict(asset) for asset in self.assets]

    def delete_asset(self, asset_id: int) -> None:
        self.events.append(f"delete:{asset_id}")
        self.deleted.append(asset_id)
        self.assets = [asset for asset in self.assets if asset["id"] != asset_id]

    def publish_release(self, release_id: int) -> dict[str, Any]:
        self.events.append("publish")
        assert self.release is not None
        self.release = dict(self.release, draft=False)
        self.published = True
        return self.release

    def add_asset(self, name: str, size: int, state: str = "uploaded") -> None:
        self.assets.append({"id": self.next_asset_id, "name": name, "size": size, "state": state})
        self.next_asset_id += 1

    def upload(self, release_id: int, asset: Path) -> dict[str, Any]:
        self.events.append(f"upload:{asset.name}")
        self.add_asset(asset.name, asset.stat().st_size)
        return dict(self.assets[-1])


def draft_release() -> dict[str, Any]:
    return {
        "id": 17,
        "tag_name": TAG,
        "target_commitish": "develop",
        "name": TITLE,
        "draft": True,
        "prerelease": False,
        "html_url": "https://github.invalid/releases/tag/v0.2.0",
    }


def published_release() -> dict[str, Any]:
    return dict(draft_release(), draft=False)


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
            "bundle": "data_OME_windows_x86_64",
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
    linux_ledger = f"{'0' * 64}  OME\n".encode()
    linux_archive = linux / publish_github_release.LINUX_ARCHIVE
    with tarfile.open(linux_archive, "w:gz") as archive:
        add_tar_bytes(
            archive,
            f"{publish_github_release.LINUX_PACKAGE_ROOT}/BUILD-INFO.json",
            linux_info,
        )
        add_tar_bytes(
            archive,
            f"{publish_github_release.LINUX_PACKAGE_ROOT}/SHA256SUMS",
            linux_ledger,
        )
    (linux / "BUILD-INFO.json").write_bytes(linux_info)
    (linux / "SHA256SUMS").write_bytes(linux_ledger)
    (linux / "package-smoke.log").write_text(
        publish_github_release.SMOKE_MARKER + "\n", encoding="utf-8"
    )
    write_checksum(linux, linux_archive)

    windows_info = build_info("win-x64")
    windows_ledger = f"{'1' * 64}  OME.exe\n".encode()
    verification = windows_verification()
    windows_archive = windows / publish_github_release.WINDOWS_ARCHIVE
    with zipfile.ZipFile(windows_archive, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        root_name = publish_github_release.WINDOWS_PACKAGE_ROOT
        archive.writestr(f"{root_name}/BUILD-INFO.json", windows_info)
        archive.writestr(f"{root_name}/SHA256SUMS", windows_ledger)
        archive.writestr(f"{root_name}/WINDOWS-VERIFICATION.json", verification)
    (windows / "BUILD-INFO.json").write_bytes(windows_info)
    (windows / "SHA256SUMS").write_bytes(windows_ledger)
    (windows / "WINDOWS-VERIFICATION.json").write_bytes(verification)
    write_checksum(windows, windows_archive)
    return linux, windows, prepared


def promote(api: FakeApi, root: Path, upload: Any = None) -> dict[str, Any]:
    root.mkdir(parents=True, exist_ok=True)
    linux, windows, prepared = create_artifacts(root)
    return publish_github_release.promote_release(
        api, upload or api.upload, TAG, TARGET, linux, windows, prepared
    )


def prepared_sizes(root: Path) -> dict[str, int]:
    root.mkdir(parents=True, exist_ok=True)
    linux, windows, prepared = create_artifacts(root)
    assets = publish_github_release.prepare_release_assets(TAG, TARGET, linux, windows, prepared)
    return {asset.name: asset.stat().st_size for asset in assets}


class PublishGithubReleaseTests(unittest.TestCase):
    def test_creates_draft_uploads_three_verified_assets_then_publishes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            api = FakeApi()
            result = promote(api, Path(temporary))
            self.assertEqual(17, result["id"])
            self.assertFalse(result["draft"])
            self.assertTrue(api.created_payload["draft"])
            self.assertEqual(TAG, api.created_payload["tag_name"])
            self.assertEqual(TARGET, api.created_payload["target_commitish"])
            self.assertEqual(TITLE, api.created_payload["name"])
            self.assertIn("Windows archive", api.created_payload["body"])
            self.assertEqual(
                publish_github_release.EXPECTED_RELEASE_ASSETS,
                {asset["name"] for asset in api.assets},
            )
            self.assertEqual("tag_commit", api.events[0])
            self.assertEqual("publish", api.events[-1])
            self.assertEqual(3, sum(event.startswith("upload:") for event in api.events))
            checksums = (
                Path(temporary) / "prepared" / publish_github_release.RELEASE_CHECKSUMS
            ).read_text(encoding="ascii")
            self.assertIn(publish_github_release.LINUX_ARCHIVE, checksums)
            self.assertIn(publish_github_release.WINDOWS_ARCHIVE, checksums)

    def test_resumes_draft_and_uploads_only_missing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            sizes = prepared_sizes(Path(temporary) / "sizes")
            api = FakeApi(draft_release())
            api.add_asset(publish_github_release.LINUX_ARCHIVE, sizes[publish_github_release.LINUX_ARCHIVE])
            promote(api, Path(temporary) / "run")
            uploads = [event for event in api.events if event.startswith("upload:")]
            self.assertNotIn(f"upload:{publish_github_release.LINUX_ARCHIVE}", uploads)
            self.assertEqual(2, len(uploads))
            self.assertIsNone(api.created_payload)
            self.assertTrue(api.published)

    def test_replaces_incomplete_upload_placeholder_on_draft(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            api = FakeApi(draft_release())
            api.add_asset(publish_github_release.WINDOWS_ARCHIVE, 0, state="starter")
            promote(api, Path(temporary))
            self.assertEqual([100], api.deleted)
            delete_at = api.events.index("delete:100")
            upload_at = api.events.index(f"upload:{publish_github_release.WINDOWS_ARCHIVE}")
            self.assertLess(delete_at, upload_at)
            self.assertTrue(api.published)

    def test_completed_published_release_is_an_idempotent_success(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            sizes = prepared_sizes(Path(temporary) / "sizes")
            api = FakeApi(published_release())
            for name, size in sizes.items():
                api.add_asset(name, size)
            result = promote(api, Path(temporary) / "run")
            self.assertFalse(result["draft"])
            self.assertIsNone(api.created_payload)
            self.assertFalse(api.published)
            self.assertFalse(any(event.startswith("upload:") for event in api.events))

    def test_refuses_incomplete_published_release(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            api = FakeApi(published_release())
            api.add_asset(publish_github_release.LINUX_ARCHIVE, 1)
            with self.assertRaisesRegex(RuntimeError, "published GitHub release does not hold exactly"):
                promote(api, Path(temporary))
            self.assertFalse(any(event.startswith("upload:") for event in api.events))

    def test_refuses_tag_that_does_not_point_at_target(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            api = FakeApi(tag_commit="f" * 40)
            with self.assertRaisesRegex(ValueError, "points at"):
                promote(api, Path(temporary))
            self.assertIsNone(api.created_payload)

    def test_refuses_multiple_drafts_for_tag(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            api = FakeApi(draft_release(), extra_drafts=1)
            with self.assertRaisesRegex(ValueError, "more than one draft"):
                promote(api, Path(temporary))

    def test_refuses_mismatched_release_and_asset_collision(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            wrong_release = draft_release()
            wrong_release["name"] = "Something else"
            api = FakeApi(wrong_release)
            with self.assertRaisesRegex(ValueError, "does not match"):
                promote(api, Path(temporary) / "a")

            api = FakeApi(draft_release())
            api.add_asset(publish_github_release.LINUX_ARCHIVE, 999)
            with self.assertRaisesRegex(ValueError, "will not be overwritten"):
                promote(api, Path(temporary) / "b")

            api = FakeApi(draft_release())
            api.add_asset("stale.txt", 1)
            with self.assertRaisesRegex(ValueError, "unexpected assets"):
                promote(api, Path(temporary) / "c")

    def test_refuses_mismatched_archive_commit_and_structural_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            linux, windows, prepared = create_artifacts(Path(temporary))
            (linux / publish_github_release.LINUX_ARCHIVE).write_bytes(b"changed")
            with self.assertRaisesRegex(ValueError, "external SHA-256"):
                publish_github_release.prepare_release_assets(
                    TAG, TARGET, linux, windows, prepared
                )

        with tempfile.TemporaryDirectory() as temporary:
            linux, windows, prepared = create_artifacts(Path(temporary))
            info = json.loads((windows / "BUILD-INFO.json").read_text(encoding="utf-8"))
            info["source_commit"] = "f" * 40
            (windows / "BUILD-INFO.json").write_bytes(json_bytes(info))
            with self.assertRaisesRegex(ValueError, "does not match"):
                publish_github_release.prepare_release_assets(
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
                publish_github_release.prepare_release_assets(
                    TAG, TARGET, linux, windows, prepared
                )


class GithubApiTests(unittest.TestCase):
    TOKEN = "ghs_exampleTOKEN123"

    def test_rejects_malformed_repository_and_token(self) -> None:
        with self.assertRaisesRegex(ValueError, "owner/name"):
            publish_github_release.GithubApi("no-slash", self.TOKEN)
        with self.assertRaisesRegex(ValueError, "GITHUB_TOKEN"):
            publish_github_release.GithubApi("owner/name", "")
        with self.assertRaisesRegex(ValueError, "GITHUB_TOKEN"):
            publish_github_release.GithubApi("owner/name", "bad token\n")

    def test_upload_keeps_token_off_the_command_line(self) -> None:
        api = publish_github_release.GithubApi("concopuffs/OpenMaidEngine", self.TOKEN)
        with tempfile.TemporaryDirectory() as temporary:
            asset = Path(temporary) / publish_github_release.LINUX_ARCHIVE
            asset.write_bytes(b"payload")
            completed = subprocess.CompletedProcess(
                [], 0, stdout=json.dumps({"name": asset.name, "size": 7, "state": "uploaded"}), stderr=""
            )
            with mock.patch.object(publish_github_release.subprocess, "run", return_value=completed) as run:
                result = api.upload_asset(42, asset)
        self.assertEqual(asset.name, result["name"])
        command = run.call_args.args[0]
        stdin = run.call_args.kwargs["input"]
        self.assertTrue(all(self.TOKEN not in part for part in command))
        self.assertIn(f"Authorization: Bearer {self.TOKEN}", stdin)
        self.assertIn("Content-Type: application/octet-stream", stdin)
        self.assertEqual(["curl", "--config", "-"], command[:3])
        self.assertIn("--data-binary", command)
        self.assertEqual(
            "https://uploads.github.com/repos/concopuffs/OpenMaidEngine/releases/42/assets"
            f"?name={publish_github_release.LINUX_ARCHIVE}",
            command[-1],
        )

    def test_tag_commit_dereferences_annotated_tags(self) -> None:
        api = publish_github_release.GithubApi("owner/name", self.TOKEN)
        responses = {
            "/git/ref/tags/v1.0.0": {"object": {"type": "tag", "sha": "a" * 40}},
            f"/git/tags/{'a' * 40}": {"object": {"type": "commit", "sha": "b" * 40}},
        }
        with mock.patch.object(api, "_request", side_effect=lambda method, path, **_: responses[path]):
            self.assertEqual("b" * 40, api.tag_commit("v1.0.0"))
        with mock.patch.object(api, "_request", return_value=None):
            with self.assertRaisesRegex(ValueError, "does not exist"):
                api.tag_commit("v9.9.9")


if __name__ == "__main__":
    unittest.main()
