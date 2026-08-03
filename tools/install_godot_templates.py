#!/usr/bin/env python3
"""Install selected, pinned Godot export templates with HTTP range requests.

Godot 4.7 stores every platform template in one archive. This reader mirrors the
editor's selective downloader: ZIP metadata and only the requested compressed
members cross the network. Each installed member is then checked against the
project manifest's size and SHA-256.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import time
import urllib.error
import urllib.request
import zipfile
from collections.abc import Callable
from pathlib import Path

DEFAULT_WINDOW = 8 * 1024 * 1024
HTTP_HEADERS = {
    "User-Agent": "OpenMaidEngine-build/1.0 (+https://git.orfl.xyz/conco/OpenMaidEngine)",
    "Accept": "application/octet-stream,*/*;q=0.8",
}


class RangeReader(io.RawIOBase):
    """Seekable read-only view backed by a byte-range callback."""

    def __init__(
        self,
        length: int,
        fetch: Callable[[int, int], bytes],
        window_size: int = DEFAULT_WINDOW,
    ) -> None:
        if length <= 0:
            raise ValueError("range source length must be positive")
        self._length = length
        self._fetch = fetch
        self._window_size = window_size
        self._position = 0
        self._cache_start = 0
        self._cache = b""

    def readable(self) -> bool:
        return True

    def seekable(self) -> bool:
        return True

    def tell(self) -> int:
        return self._position

    def seek(self, offset: int, whence: int = io.SEEK_SET) -> int:
        if whence == io.SEEK_SET:
            position = offset
        elif whence == io.SEEK_CUR:
            position = self._position + offset
        elif whence == io.SEEK_END:
            position = self._length + offset
        else:
            raise ValueError(f"unsupported seek mode: {whence}")
        if position < 0:
            raise ValueError("negative seek position")
        self._position = position
        return position

    def read(self, size: int = -1) -> bytes:
        if self._position >= self._length:
            return b""
        if size is None or size < 0:
            size = self._length - self._position
        size = min(size, self._length - self._position)
        if size == 0:
            return b""

        cache_end = self._cache_start + len(self._cache)
        requested_end = self._position + size
        if not (
            self._cache_start <= self._position
            and requested_end <= cache_end
        ):
            fetch_size = max(size, self._window_size)
            fetch_end = min(self._length, self._position + fetch_size)
            self._cache_start = self._position
            self._cache = self._fetch(self._position, fetch_end - 1)
            expected = fetch_end - self._position
            if len(self._cache) != expected:
                raise OSError(
                    f"short range response: expected {expected} bytes, "
                    f"received {len(self._cache)}"
                )
            cache_end = fetch_end

        offset = self._position - self._cache_start
        data = self._cache[offset:offset + size]
        self._position += len(data)
        return data


def _open_with_retry(request: urllib.request.Request, attempts: int = 3):
    last_error: Exception | None = None
    for attempt in range(attempts):
        try:
            return urllib.request.urlopen(request, timeout=120)
        except (OSError, urllib.error.URLError) as error:
            last_error = error
            if attempt + 1 < attempts:
                time.sleep(2 ** attempt)
    assert last_error is not None
    raise last_error


def open_http_range_reader(url: str, expected_length: int) -> RangeReader:
    head = urllib.request.Request(url, headers=HTTP_HEADERS, method="HEAD")
    with _open_with_retry(head) as response:
        final_url = response.geturl()
        actual_length = int(response.headers.get("Content-Length", "0"))
    if actual_length != expected_length:
        raise ValueError(
            f"template archive size mismatch: expected {expected_length}, "
            f"server reported {actual_length}"
        )

    def fetch(start: int, end: int) -> bytes:
        request_headers = dict(HTTP_HEADERS)
        request_headers.update({
            "Range": f"bytes={start}-{end}",
            "Accept-Encoding": "identity",
        })
        request = urllib.request.Request(final_url, headers=request_headers)
        with _open_with_retry(request) as response:
            if response.status != 206:
                raise OSError(
                    f"template server ignored byte range {start}-{end}: "
                    f"HTTP {response.status}"
                )
            content_range = response.headers.get("Content-Range", "")
            expected_range = f"bytes {start}-{end}/{expected_length}"
            if content_range != expected_range:
                raise OSError(
                    f"unexpected Content-Range: expected {expected_range!r}, "
                    f"received {content_range!r}"
                )
            return response.read()

    return RangeReader(expected_length, fetch)


def file_matches(path: Path, expected_size: int, expected_hash: str) -> bool:
    if not path.is_file() or path.stat().st_size != expected_size:
        return False
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest() == expected_hash.lower()


def install_members(manifest_path: Path, destination: Path) -> list[Path]:
    with manifest_path.open(encoding="utf-8") as stream:
        manifest = json.load(stream)
    templates = manifest["templates"]
    members = templates["members"]
    destination.mkdir(parents=True, exist_ok=True)

    installed = [destination / member["install_name"] for member in members]
    if all(
        file_matches(path, member["size"], member["sha256"])
        for path, member in zip(installed, members, strict=True)
    ):
        return installed

    reader = open_http_range_reader(templates["url"], templates["size"])
    with zipfile.ZipFile(reader) as archive:
        for path, member in zip(installed, members, strict=True):
            if file_matches(path, member["size"], member["sha256"]):
                continue
            archive_path = member["archive_path"]
            info = archive.getinfo(archive_path)
            if info.file_size != member["size"]:
                raise ValueError(
                    f"template member size mismatch for {archive_path}: "
                    f"expected {member['size']}, archive reports {info.file_size}"
                )
            temporary = path.with_suffix(path.suffix + ".part")
            digest = hashlib.sha256()
            with archive.open(info) as source, temporary.open("wb") as target:
                for block in iter(lambda: source.read(1024 * 1024), b""):
                    target.write(block)
                    digest.update(block)
            if temporary.stat().st_size != member["size"]:
                temporary.unlink(missing_ok=True)
                raise ValueError(f"short extracted template member: {archive_path}")
            actual_hash = digest.hexdigest()
            if actual_hash != member["sha256"].lower():
                temporary.unlink(missing_ok=True)
                raise ValueError(
                    f"template member SHA-256 mismatch for {archive_path}: "
                    f"expected {member['sha256']}, got {actual_hash}"
                )
            os.chmod(temporary, int(member["mode"], 8))
            os.replace(temporary, path)
    return installed


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    args = parser.parse_args()
    for installed in install_members(args.manifest.resolve(), args.destination.resolve()):
        print(installed)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
