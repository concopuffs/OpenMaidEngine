#!/usr/bin/env python3
"""Validate paired platform artifacts and promote three assets to Gitea."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import tarfile
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path
from typing import Any, Callable, Protocol

import verify_windows_native


LINUX_ARCHIVE = "OpenMaidEngine-Himegari-linux-x64.tar.gz"
WINDOWS_ARCHIVE = "OpenMaidEngine-Himegari-windows-x64.zip"
RELEASE_CHECKSUMS = "RELEASE-SHA256SUMS"
LINUX_ARTIFACT_FILES = frozenset({
    LINUX_ARCHIVE,
    f"{LINUX_ARCHIVE}.sha256",
    "BUILD-INFO.json",
    "SHA256SUMS",
    "package-smoke.log",
})
WINDOWS_ARTIFACT_FILES = frozenset({
    WINDOWS_ARCHIVE,
    f"{WINDOWS_ARCHIVE}.sha256",
    "BUILD-INFO.json",
    "SHA256SUMS",
    "WINDOWS-VERIFICATION.json",
})
EXPECTED_RELEASE_ASSETS = frozenset({LINUX_ARCHIVE, WINDOWS_ARCHIVE, RELEASE_CHECKSUMS})
LINUX_PACKAGE_ROOT = "OpenMaidEngine-Himegari-linux-x64"
WINDOWS_PACKAGE_ROOT = "OpenMaidEngine-Himegari-windows-x64"
SMOKE_MARKER = "PACKAGE SMOKE OK: opcodes=548 ffmpeg-abi=3"
TAG_PATTERN = re.compile(r"v[0-9][0-9A-Za-z.+-]*\Z")
COMMIT_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
TOKEN_PATTERN = re.compile(r"[A-Za-z0-9._-]+\Z")
MAX_EVIDENCE_SIZE = 1024 * 1024


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
                "curl", "--config", "-", "--fail-with-body", "--silent", "--show-error",
                "--request", "POST", "--form", f"attachment=@{asset}", url,
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


def _validate_tag_target(tag: str, target: str) -> None:
    if not TAG_PATTERN.fullmatch(tag):
        raise ValueError(f"release tag must be v-prefixed and version-like: {tag}")
    if not COMMIT_PATTERN.fullmatch(target):
        raise ValueError(f"release target must be a lowercase SHA-1 commit: {target}")


def _artifact_files(directory: Path, expected: frozenset[str], label: str) -> dict[str, Path]:
    directory = directory.resolve()
    if not directory.is_dir():
        raise ValueError(f"{label} artifact directory was not found: {directory}")
    entries = list(directory.iterdir())
    invalid = [entry.name for entry in entries if not entry.is_file() or entry.is_symlink()]
    if invalid:
        raise ValueError(f"{label} artifact has invalid entries: {', '.join(sorted(invalid))}")
    by_name = {entry.name: entry for entry in entries}
    if set(by_name) != expected:
        missing = sorted(expected - set(by_name))
        extra = sorted(set(by_name) - expected)
        raise ValueError(f"unexpected {label} artifact set; missing={missing}, extra={extra}")
    empty = sorted(name for name, path in by_name.items() if path.stat().st_size <= 0)
    if empty:
        raise ValueError(f"{label} artifact has empty files: {', '.join(empty)}")
    return by_name


def _hash(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _validate_external_checksum(archive: Path, checksum: Path, label: str) -> str:
    try:
        line = checksum.read_text(encoding="ascii").strip()
    except UnicodeDecodeError as error:
        raise ValueError(f"{label} archive checksum is not ASCII") from error
    match = re.fullmatch(rf"([0-9a-f]{{64}})  {re.escape(archive.name)}", line)
    if match is None:
        raise ValueError(f"{label} archive checksum file has an unexpected format: {checksum}")
    actual = _hash(archive)
    if actual != match.group(1):
        raise ValueError(f"{label} archive does not match its external SHA-256 checksum")
    return actual


def _json_bytes(data: bytes, label: str) -> dict[str, Any]:
    try:
        parsed = json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{label} is not valid UTF-8 JSON") from error
    if not isinstance(parsed, dict):
        raise ValueError(f"{label} JSON root is not an object")
    return parsed


def _validate_build_info(data: bytes, target: str, rid: str, label: str) -> None:
    build_info = _json_bytes(data, label)
    expected = {
        "schema_version": 1,
        "source_commit": target,
        "source_dirty": False,
        "target": rid,
    }
    mismatches = {
        key: (build_info.get(key), value)
        for key, value in expected.items()
        if build_info.get(key) != value
    }
    if mismatches:
        raise ValueError(f"{label} does not match this promotion: {mismatches}")


def _tar_member(archive_path: Path, name: str) -> bytes:
    with tarfile.open(archive_path, mode="r:gz") as archive:
        try:
            member = archive.getmember(name)
        except KeyError as error:
            raise ValueError(f"Linux archive evidence was not found: {name}") from error
        if not member.isfile() or member.size <= 0 or member.size > MAX_EVIDENCE_SIZE:
            raise ValueError(f"Linux archive evidence has an invalid size or type: {name}")
        stream = archive.extractfile(member)
        if stream is None:
            raise ValueError(f"Linux archive evidence could not be read: {name}")
        return stream.read()


def _zip_member(archive_path: Path, name: str) -> bytes:
    with zipfile.ZipFile(archive_path) as archive:
        try:
            info = archive.getinfo(name)
        except KeyError as error:
            raise ValueError(f"Windows archive evidence was not found: {name}") from error
        if info.is_dir() or info.file_size <= 0 or info.file_size > MAX_EVIDENCE_SIZE:
            raise ValueError(f"Windows archive evidence has an invalid size or type: {name}")
        with archive.open(info) as stream:
            return stream.read()


def _validate_windows_verification(data: bytes) -> None:
    report = _json_bytes(data, "WINDOWS-VERIFICATION.json")
    native = report.get("native")
    if not isinstance(native, dict):
        raise ValueError("WINDOWS-VERIFICATION.json has no native report")
    machines = native.get("machines")
    expected_machines = {
        name: "AMD64"
        for name in (verify_windows_native.SHIM, *verify_windows_native.RUNTIME_DLLS)
    }
    expected = {
        "schema_version": 1,
        "target": "win-x64",
        "executable_machine": "AMD64",
    }
    mismatches = {
        key: (report.get(key), value)
        for key, value in expected.items()
        if report.get(key) != value
    }
    if native.get("schema_version") != 1 or native.get("target") != "win-x64":
        mismatches["native"] = ((native.get("schema_version"), native.get("target")), (1, "win-x64"))
    if native.get("bundle") != "data_Himegari_windows_x86_64":
        mismatches["bundle"] = (native.get("bundle"), "data_Himegari_windows_x86_64")
    if machines != expected_machines:
        mismatches["machines"] = (machines, expected_machines)
    exports_value = native.get("shim_exports")
    exports = (
        {name for name in exports_value if isinstance(name, str)}
        if isinstance(exports_value, list)
        else set()
    )
    if exports != verify_windows_native.REQUIRED_EXPORTS:
        mismatches["shim_exports"] = (exports_value, "required AGE ABI exports")
    imports_value = native.get("shim_imports")
    imports = (
        {name.lower() for name in imports_value if isinstance(name, str)}
        if isinstance(imports_value, list)
        else set()
    )
    required_imports = {name.lower() for name in verify_windows_native.RUNTIME_DLLS}
    if not required_imports.issubset(imports):
        mismatches["shim_imports"] = (sorted(imports), sorted(required_imports))
    forbidden_imports = imports & verify_windows_native.FORBIDDEN_RUNTIME_IMPORTS
    if forbidden_imports:
        mismatches["forbidden_imports"] = (sorted(forbidden_imports), [])
    if mismatches:
        raise ValueError(f"WINDOWS-VERIFICATION.json is not accepted: {mismatches}")


def prepare_release_assets(
    tag: str,
    target: str,
    linux_directory: Path,
    windows_directory: Path,
    output_directory: Path,
) -> list[Path]:
    _validate_tag_target(tag, target)
    linux = _artifact_files(linux_directory, LINUX_ARTIFACT_FILES, "Linux")
    windows = _artifact_files(windows_directory, WINDOWS_ARTIFACT_FILES, "Windows")
    linux_hash = _validate_external_checksum(
        linux[LINUX_ARCHIVE], linux[f"{LINUX_ARCHIVE}.sha256"], "Linux"
    )
    windows_hash = _validate_external_checksum(
        windows[WINDOWS_ARCHIVE], windows[f"{WINDOWS_ARCHIVE}.sha256"], "Windows"
    )

    linux_build_info = linux["BUILD-INFO.json"].read_bytes()
    windows_build_info = windows["BUILD-INFO.json"].read_bytes()
    linux_ledger = linux["SHA256SUMS"].read_bytes()
    windows_ledger = windows["SHA256SUMS"].read_bytes()
    windows_verification = windows["WINDOWS-VERIFICATION.json"].read_bytes()
    _validate_build_info(linux_build_info, target, "linux-x64", "Linux BUILD-INFO.json")
    _validate_build_info(windows_build_info, target, "win-x64", "Windows BUILD-INFO.json")
    if SMOKE_MARKER not in linux["package-smoke.log"].read_text(encoding="utf-8"):
        raise ValueError("package-smoke.log does not contain the accepted Linux package smoke result")
    _validate_windows_verification(windows_verification)

    internal_linux_build = _tar_member(
        linux[LINUX_ARCHIVE], f"{LINUX_PACKAGE_ROOT}/BUILD-INFO.json"
    )
    internal_linux_ledger = _tar_member(
        linux[LINUX_ARCHIVE], f"{LINUX_PACKAGE_ROOT}/SHA256SUMS"
    )
    internal_windows_build = _zip_member(
        windows[WINDOWS_ARCHIVE], f"{WINDOWS_PACKAGE_ROOT}/BUILD-INFO.json"
    )
    internal_windows_ledger = _zip_member(
        windows[WINDOWS_ARCHIVE], f"{WINDOWS_PACKAGE_ROOT}/SHA256SUMS"
    )
    internal_windows_verification = _zip_member(
        windows[WINDOWS_ARCHIVE], f"{WINDOWS_PACKAGE_ROOT}/WINDOWS-VERIFICATION.json"
    )
    comparisons = (
        (linux_build_info, internal_linux_build, "Linux BUILD-INFO.json"),
        (linux_ledger, internal_linux_ledger, "Linux SHA256SUMS"),
        (windows_build_info, internal_windows_build, "Windows BUILD-INFO.json"),
        (windows_ledger, internal_windows_ledger, "Windows SHA256SUMS"),
        (windows_verification, internal_windows_verification, "WINDOWS-VERIFICATION.json"),
    )
    for external, internal, label in comparisons:
        if external != internal:
            raise ValueError(f"external and packaged {label} do not match")

    if b"  Himegari.x86_64\n" not in linux_ledger:
        raise ValueError("Linux SHA256SUMS does not contain the packaged executable")
    if b"  Himegari.exe\n" not in windows_ledger:
        raise ValueError("Windows SHA256SUMS does not contain the packaged executable")

    output_directory = output_directory.resolve()
    output_directory.mkdir(parents=True, exist_ok=True)
    checksum_path = output_directory / RELEASE_CHECKSUMS
    content = f"{linux_hash}  {LINUX_ARCHIVE}\n{windows_hash}  {WINDOWS_ARCHIVE}\n"
    with checksum_path.open("w", encoding="ascii", newline="\n") as stream:
        stream.write(content)
    return [linux[LINUX_ARCHIVE], windows[WINDOWS_ARCHIVE], checksum_path]


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
    linux_directory: Path,
    windows_directory: Path,
    output_directory: Path,
) -> dict[str, Any]:
    assets = prepare_release_assets(
        tag, target, linux_directory, windows_directory, output_directory
    )
    if {asset.name for asset in assets} != EXPECTED_RELEASE_ASSETS:
        raise RuntimeError("prepared release asset set is invalid")
    title = f"OpenMaidEngine Himegari {tag}"
    release = api.get_release(tag)
    if release is None:
        release = api.create_release({
            "tag_name": tag,
            "target_commitish": target,
            "name": title,
            "body": (
                "Automated Linux and Windows x64 release built from `" + target + "`.\n\n"
                "The Linux archive passed the dynamic opcode/FFmpeg package smoke. The Windows archive "
                "passed structural AMD64 PE, payload, ABI export, and FFmpeg import verification without "
                "executing the EXE. Each archive contains its detailed build metadata and payload ledger."
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
    unexpected = sorted(set(existing_assets) - EXPECTED_RELEASE_ASSETS)
    if unexpected:
        raise ValueError("Gitea release has unexpected assets: " + ", ".join(unexpected))

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
    if set(final_assets) != EXPECTED_RELEASE_ASSETS:
        raise RuntimeError(f"release asset verification failed: {sorted(final_assets)}")
    for asset in assets:
        if final_assets[asset.name].get("size") != asset.stat().st_size:
            raise RuntimeError(f"release asset verification failed: {asset.name}")
    return release


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", required=True)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--target", required=True)
    parser.add_argument("--linux-artifact-directory", required=True, type=Path)
    parser.add_argument("--windows-artifact-directory", required=True, type=Path)
    parser.add_argument("--output-directory", required=True, type=Path)
    args = parser.parse_args(arguments)

    token = os.environ.get("GITEA_TOKEN", "")
    api = GiteaApi(args.server, args.repository, token)
    release = promote_release(
        api,
        api.upload_asset,
        args.tag,
        args.target,
        args.linux_artifact_directory,
        args.windows_artifact_directory,
        args.output_directory,
    )
    print(f"Gitea release ready: {release.get('html_url', args.tag)}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError, ValueError, tarfile.TarError, zipfile.BadZipFile) as error:
        print(f"release promotion failed: {error}", file=sys.stderr)
        raise SystemExit(1)
