#!/usr/bin/env python3
"""Promote one verified Linux workflow artifact to a matching Gitea release."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any, Callable, Protocol


EXPECTED_ASSETS = frozenset({
    "OpenMaidEngine-Himegari-linux-x64.tar.gz",
    "OpenMaidEngine-Himegari-linux-x64.tar.gz.sha256",
    "BUILD-INFO.json",
    "SHA256SUMS",
    "package-smoke.log",
})
TAG_PATTERN = re.compile(r"v[0-9][0-9A-Za-z.+-]*\Z")
COMMIT_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
TOKEN_PATTERN = re.compile(r"[A-Za-z0-9._-]+\Z")


class ReleaseApi(Protocol):
    def get_release(self, tag: str) -> dict[str, Any] | None: ...

    def create_release(self, payload: dict[str, Any]) -> dict[str, Any]: ...

    def list_assets(self, release_id: int) -> list[dict[str, Any]]: ...


class GiteaApi:
    def __init__(self, server: str, repository: str, token: str) -> None:
        parsed = urllib.parse.urlsplit(server)
        if parsed.scheme not in {"http", "https"} or not parsed.netloc:
            raise ValueError(f"invalid Gitea server URL: {server}")
        if parsed.username or parsed.password or parsed.query or parsed.fragment:
            raise ValueError("Gitea server URL must not contain credentials, a query, or a fragment")
        parts = repository.split("/")
        if len(parts) != 2 or not all(parts):
            raise ValueError(f"repository must be owner/name: {repository}")
        if not TOKEN_PATTERN.fullmatch(token):
            raise ValueError("GITEA_TOKEN is missing or malformed")
        owner, name = (urllib.parse.quote(part, safe="") for part in parts)
        self.base_url = f"{server.rstrip('/')}/api/v1/repos/{owner}/{name}"
        self.token = token

    def _request(
        self,
        method: str,
        path: str,
        *,
        payload: dict[str, Any] | None = None,
        allow_not_found: bool = False,
    ) -> dict[str, Any] | list[dict[str, Any]] | None:
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        request = urllib.request.Request(
            self.base_url + path,
            data=data,
            method=method,
            headers={
                "Accept": "application/json",
                "Authorization": f"token {self.token}",
                "Content-Type": "application/json",
                "User-Agent": "OpenMaidEngine-release-promotion",
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if allow_not_found and error.code == 404:
                return None
            detail = error.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"Gitea API {method} {path} failed ({error.code}): {detail}") from error

    def get_release(self, tag: str) -> dict[str, Any] | None:
        result = self._request(
            "GET",
            "/releases/tags/" + urllib.parse.quote(tag, safe=""),
            allow_not_found=True,
        )
        assert result is None or isinstance(result, dict)
        return result

    def create_release(self, payload: dict[str, Any]) -> dict[str, Any]:
        result = self._request("POST", "/releases", payload=payload)
        if not isinstance(result, dict):
            raise RuntimeError("Gitea create-release response was not an object")
        return result

    def list_assets(self, release_id: int) -> list[dict[str, Any]]:
        result = self._request("GET", f"/releases/{release_id}/assets")
        if not isinstance(result, list):
            raise RuntimeError("Gitea release-assets response was not an array")
        return result

    def upload_asset(self, release_id: int, asset: Path) -> dict[str, Any]:
        url = (
            f"{self.base_url}/releases/{release_id}/assets?"
            + urllib.parse.urlencode({"name": asset.name})
        )
        curl_config = (
            f'header = "Authorization: token {self.token}"\n'
            'header = "Accept: application/json"\n'
        )
        process = subprocess.run(
            [
                "curl",
                "--config", "-",
                "--fail-with-body",
                "--silent",
                "--show-error",
                "--request", "POST",
                "--form", f"attachment=@{asset}",
                url,
            ],
            input=curl_config,
            text=True,
            encoding="utf-8",
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        if process.returncode != 0:
            raise RuntimeError(
                f"Gitea asset upload failed for {asset.name}: "
                f"{process.stderr.strip()} {process.stdout.strip()}".strip()
            )
        try:
            result = json.loads(process.stdout)
        except json.JSONDecodeError as error:
            raise RuntimeError(f"Gitea asset upload returned invalid JSON for {asset.name}") from error
        if not isinstance(result, dict):
            raise RuntimeError(f"Gitea asset upload response was not an object for {asset.name}")
        return result


def validate_inputs(tag: str, target: str, assets: list[Path]) -> None:
    if not TAG_PATTERN.fullmatch(tag):
        raise ValueError(f"release tag must be v-prefixed and version-like: {tag}")
    if not COMMIT_PATTERN.fullmatch(target):
        raise ValueError(f"release target must be a lowercase SHA-1 commit: {target}")
    names = [asset.name for asset in assets]
    if len(names) != len(set(names)):
        raise ValueError("release asset names must be unique")
    if set(names) != EXPECTED_ASSETS:
        missing = sorted(EXPECTED_ASSETS - set(names))
        extra = sorted(set(names) - EXPECTED_ASSETS)
        raise ValueError(f"unexpected release asset set; missing={missing}, extra={extra}")
    for asset in assets:
        if not asset.is_file():
            raise ValueError(f"release asset was not found: {asset}")
        if asset.stat().st_size <= 0:
            raise ValueError(f"release asset is empty: {asset}")

    by_name = {asset.name: asset for asset in assets}
    try:
        build_info = json.loads(by_name["BUILD-INFO.json"].read_text(encoding="utf-8"))
    except (json.JSONDecodeError, UnicodeDecodeError) as error:
        raise ValueError("BUILD-INFO.json is not valid UTF-8 JSON") from error
    expected_build_info = {
        "schema_version": 1,
        "source_commit": target,
        "source_dirty": False,
        "target": "linux-x64",
    }
    build_mismatches = {
        key: (build_info.get(key), value)
        for key, value in expected_build_info.items()
        if build_info.get(key) != value
    }
    if build_mismatches:
        raise ValueError(f"BUILD-INFO.json does not match this promotion: {build_mismatches}")

    archive = by_name["OpenMaidEngine-Himegari-linux-x64.tar.gz"]
    checksum_path = by_name["OpenMaidEngine-Himegari-linux-x64.tar.gz.sha256"]
    checksum_line = checksum_path.read_text(encoding="ascii").strip()
    checksum_match = re.fullmatch(
        r"([0-9a-f]{64})  OpenMaidEngine-Himegari-linux-x64\.tar\.gz",
        checksum_line,
    )
    if checksum_match is None:
        raise ValueError(f"archive checksum file has an unexpected format: {checksum_path}")
    digest = hashlib.sha256()
    with archive.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    if digest.hexdigest() != checksum_match.group(1):
        raise ValueError("Linux archive does not match its external SHA-256 checksum")

    smoke_log = by_name["package-smoke.log"].read_text(encoding="utf-8")
    if "PACKAGE SMOKE OK: opcodes=548 ffmpeg-abi=3" not in smoke_log:
        raise ValueError("package-smoke.log does not contain the accepted package smoke result")


def _validate_release(release: dict[str, Any], tag: str, target: str, title: str) -> int:
    expected = {
        "tag_name": tag,
        "target_commitish": target,
        "name": title,
        "draft": False,
        "prerelease": False,
    }
    mismatches = {
        key: (release.get(key), value)
        for key, value in expected.items()
        if release.get(key) != value
    }
    if mismatches:
        raise ValueError(f"existing Gitea release does not match this promotion: {mismatches}")
    release_id = release.get("id")
    if not isinstance(release_id, int) or release_id <= 0:
        raise ValueError("Gitea release has no valid numeric id")
    return release_id


def promote_release(
    api: ReleaseApi,
    upload: Callable[[int, Path], dict[str, Any]],
    tag: str,
    target: str,
    assets: list[Path],
) -> dict[str, Any]:
    validate_inputs(tag, target, assets)
    title = f"OpenMaidEngine Himegari {tag}"
    release = api.get_release(tag)
    if release is None:
        release = api.create_release({
            "tag_name": tag,
            "target_commitish": target,
            "name": title,
            "body": (
                "Automated Linux x64 release built from `" + target + "`.\n\n"
                "The attached archive passed the packaged opcode-metadata and FFmpeg ABI smoke gate. "
                "BUILD-INFO.json, SHA256SUMS, and package-smoke.log provide the external build evidence."
            ),
            "draft": False,
            "prerelease": False,
        })
    release_id = _validate_release(release, tag, target, title)

    existing_assets: dict[str, dict[str, Any]] = {}
    for existing in api.list_assets(release_id):
        name = existing.get("name")
        if isinstance(name, str):
            if name in existing_assets:
                raise ValueError(f"Gitea release has duplicate asset names: {name}")
            existing_assets[name] = existing

    for asset in assets:
        existing = existing_assets.get(asset.name)
        if existing is not None:
            if existing.get("size") != asset.stat().st_size:
                raise ValueError(
                    f"existing release asset differs in size and will not be overwritten: {asset.name}"
                )
            continue
        uploaded = upload(release_id, asset)
        if uploaded.get("name") != asset.name or uploaded.get("size") != asset.stat().st_size:
            raise RuntimeError(f"Gitea reported an unexpected uploaded asset: {asset.name}")

    final_assets = {asset.get("name"): asset for asset in api.list_assets(release_id)}
    for asset in assets:
        published = final_assets.get(asset.name)
        if published is None or published.get("size") != asset.stat().st_size:
            raise RuntimeError(f"release asset verification failed: {asset.name}")
    return release


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", required=True)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--target", required=True)
    parser.add_argument("--asset", action="append", required=True, type=Path)
    args = parser.parse_args(arguments)

    token = os.environ.get("GITEA_TOKEN", "")
    api = GiteaApi(args.server, args.repository, token)
    release = promote_release(api, api.upload_asset, args.tag, args.target, args.asset)
    print(f"Gitea release ready: {release.get('html_url', args.tag)}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError, ValueError) as error:
        print(f"release promotion failed: {error}", file=sys.stderr)
        raise SystemExit(1)
