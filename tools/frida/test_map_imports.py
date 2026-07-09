"""Unit tests for the pure scan/match/cluster logic in map_imports.py.

Run: py -3.11 -X utf8 tools/frida/test_map_imports.py   (plain runner, no pytest; frida not needed).
"""
import struct
import sys

from map_imports import build_export_index, scan_pointer_matches, cluster_runs

FAILS = []


def check(cond, msg):
    if not cond:
        FAILS.append(msg)
        print("FAIL:", msg)
    else:
        print("ok:", msg)


def test_export_index_canonicalizes():
    idx = build_export_index([
        {"address": 0x76d80e70, "name": "LoadLibraryA", "module": "kernel32.dll"},
        {"address": 0x76d7f7f0, "name": "GetProcAddress", "module": "kernel32.dll"},
    ])
    check(idx[0x76d80e70] == "kernel32.dll!LoadLibraryA", "index maps addr -> dll!func")


def test_export_index_first_name_wins_and_skips_zero():
    idx = build_export_index([
        {"address": 0x1000, "name": "First", "module": "a.dll"},
        {"address": 0x1000, "name": "Second", "module": "a.dll"},   # alias -> ignored
        {"address": 0x0, "name": "Nope", "module": "a.dll"},         # null addr -> skipped
    ])
    check(idx[0x1000] == "a.dll!First" and 0x0 not in idx, "first name wins; null addr skipped")


def test_scan_matches_little_endian_aligned():
    idx = {0x76d80e70: "kernel32.dll!LoadLibraryA", 0x76d7f7f0: "kernel32.dll!GetProcAddress"}
    mem = bytearray(0x20)
    struct.pack_into("<I", mem, 0x10, 0x76d80e70)
    struct.pack_into("<I", mem, 0x14, 0x76d7f7f0)
    struct.pack_into("<I", mem, 0x18, 0x12345678)          # not an export -> no match
    m = scan_pointer_matches(bytes(mem), 0x400000, idx)
    check([(r, v) for r, v, _ in m] == [(0x10, 0x76d80e70), (0x14, 0x76d7f7f0)],
          "scan finds aligned LE pointer matches, skips non-exports")


def test_scan_ignores_unaligned():
    idx = {0x76d80e70: "kernel32.dll!LoadLibraryA"}
    mem = bytearray(0x10)
    struct.pack_into("<I", mem, 0x2, 0x76d80e70)           # unaligned -> ignored
    check(scan_pointer_matches(bytes(mem), 0x400000, idx) == [], "unaligned pointer ignored")


def test_cluster_runs_groups_contiguous():
    check(cluster_runs([0x100, 0x104, 0x108, 0x200]) == [(0x100, 3), (0x200, 1)],
          "cluster groups contiguous aligned runs, isolates singleton")


def main():
    test_export_index_canonicalizes()
    test_export_index_first_name_wins_and_skips_zero()
    test_scan_matches_little_endian_aligned()
    test_scan_ignores_unaligned()
    test_cluster_runs_groups_contiguous()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
