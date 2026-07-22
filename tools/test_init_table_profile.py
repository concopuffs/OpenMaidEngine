#!/usr/bin/env python3
"""Tests for INIT field profiling. Run: py -3.11 -X utf8 tools/test_init_table_profile.py"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import init_table_profile as profile


def main() -> int:
    fixture = {
        "records": [
            {"id": 1, "name": "one", "fields": {"0x10": 2, "0x20": 0}},
            {"id": 3, "name": "three", "fields": {"0x10": 2}},
            {"id": 7, "name": "seven", "fields": {"0x10": 5}},
        ]
    }
    rows = {row["base"]: row for row in profile.profile_columns(fixture)}
    assert rows["0x10"]["population"] == 3
    assert rows["0x10"]["coverage"] == 1.0
    assert rows["0x10"]["distinct_values"] == 2
    assert rows["0x10"]["min"] == 2 and rows["0x10"]["max"] == 5
    assert rows["0x10"]["common"][0] == {"value": "2", "count": 2}
    assert rows["0x20"]["population"] == 1
    assert rows["0x20"]["examples"][0]["name"] == "one"
    print("all init_table_profile checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
