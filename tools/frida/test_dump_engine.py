"""Unit tests for profile-neutral engine-dump provenance helpers (Frida not required)."""
from __future__ import annotations

import hashlib
import sys
import tempfile
from pathlib import Path

from dump_engine import (
    PROFILE_LANDMARK_RVAS,
    build_manifest,
    read_catalog_identity,
    sha256_file,
)

FAILS: list[str] = []


def check(condition: bool, message: str) -> None:
    if condition:
        print("ok:", message)
    else:
        FAILS.append(message)
        print("FAIL:", message)


def test_hash_and_catalog_identity() -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        payload = root / "range.bin"
        payload.write_bytes(b"unpacked-age-image")
        check(
            sha256_file(payload) == hashlib.sha256(payload.read_bytes()).hexdigest(),
            "range hash is lowercase SHA-256 of exact bytes",
        )

        catalog = root / "SYS4INI.BIN"
        revision = b"S4IC433 "
        title = "神採りアルケミーマイスター".encode("cp932") + b"\0"
        catalog.write_bytes(revision + title.ljust(256, b"\0"))
        identity = read_catalog_identity(catalog)
        check(
            identity == {
                "catalog_revision": "S4IC433",
                "title": "神採りアルケミーマイスター",
            },
            "catalog identity is read from the fixed header without loading game content",
        )


def test_manifest_keeps_profile_runtime_and_range_provenance_separate() -> None:
    profile = {
        "profile_id": "kamidori",
        "engine_abi_id": "SYS4433",
        "packed_executable": {"size": 12, "sha256": "a" * 64},
    }
    runtime = {
        "age_path": "S:/Kamidori/age.exe",
        "age_base": "0x400000",
        "age_size": 0x280000,
        "landmark_rva": None,
        "landmark_bytes": None,
        "rx": [{"base": "0x400000", "size": 0x280000}],
        "rw": [],
    }
    ranges = [{
        "base": "0x400000", "tag": "age-module", "file": "range_00400000.bin",
        "declared_size": 0x280000, "captured_bytes": 0x280000, "gaps": [],
        "file_size": 0x280000, "sha256": "b" * 64,
    }]
    manifest = build_manifest(profile, runtime, ranges, "1234", "2026-08-19T12:00:00Z")
    check(
        manifest["schema_version"] == 2
        and manifest["profile"]["profile_id"] == "kamidori"
        and manifest["runtime_module"]["image_base"] == "0x400000"
        and manifest["dumped_ranges"][0]["sha256"] == "b" * 64,
        "manifest carries distinct packed-image, runtime-module, and dumped-range identities",
    )


def test_new_profiles_have_no_borrowed_landmark() -> None:
    check(
        PROFILE_LANDMARK_RVAS.get("himegari") == 0x74F1F
        and "kamidori" not in PROFILE_LANDMARK_RVAS,
        "Kamidori capture does not inherit Himegari's decoder landmark",
    )


def main() -> int:
    test_hash_and_catalog_identity()
    test_manifest_keeps_profile_runtime_and_range_provenance_separate()
    test_new_profiles_have_no_borrowed_landmark()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    raise SystemExit(main())
