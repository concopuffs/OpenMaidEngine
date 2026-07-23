#!/usr/bin/env python3
"""Tests for INIT field profiling. Run: py -3.11 -X utf8 tools/test_init_table_profile.py"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import init_table_profile as profile


def main() -> int:
    fixture = {
        "table": "TEST",
        "field_semantics": {
            "0x10": "test_parallel",
            "0x30/3/0": "test_record.zero",
        },
        "records": [
            {"id": 1, "name": "one", "fields": {"0x10": 2, "0x20": 0},
             "record_fields": {"0x30/3/0": 9},
             "message": {"title": "One", "description": "First",
                         "furigana": [{"line": 0, "text": "One", "reading": "one"}]}},
            {"id": 3, "name": "three", "fields": {"0x10": 2}},
            {"id": 7, "name": "seven", "fields": {"0x10": 5},
             "record_fields": {"0x30/3/2": 4}},
        ]
    }
    rows = {row["key"]: row for row in profile.profile_columns(fixture)}
    assert rows["0x10"]["population"] == 3
    assert rows["0x10"]["coverage"] == 1.0
    assert rows["0x10"]["distinct_values"] == 2
    assert rows["0x10"]["min"] == 2 and rows["0x10"]["max"] == 5
    assert rows["0x10"]["common"][0] == {"value": "2", "count": 2}
    assert rows["0x10"]["semantic_name"] == "test_parallel"
    assert rows["0x10"]["examples"][0]["message_description"] == "First"
    assert rows["0x20"]["population"] == 1
    assert rows["0x20"]["examples"][0]["name"] == "one"
    assert rows["0x30/3/0"]["kind"] == "record-column"
    assert rows["0x30/3/0"]["base"] == "0x30"
    assert rows["0x30/3/0"]["stride"] == 3
    assert rows["0x30/3/0"]["semantic_name"] == "test_record.zero"
    assert rows["0x30/3/2"]["column"] == 2
    messages = profile.profile_messages(fixture)
    assert messages["population"] == 1
    assert messages["coverage"] == 1 / 3
    assert messages["furigana_records"] == 1
    assert messages["examples"][0]["description"] == "First"
    matches = profile.find_message_matches(fixture, "first|three")
    assert [record["id"] for record in matches] == [1, 3]
    rendered = profile.render_message_matches(fixture, "First")
    assert "| 1 | one | First |" in rendered
    assert "`test_record.zero` (`0x30/3/0`)=9" in rendered
    print("all init_table_profile checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
