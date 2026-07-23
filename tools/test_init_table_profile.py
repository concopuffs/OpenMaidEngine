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

    mixed_fixture = {
        "table": "MIXED",
        "mode": "mixed",
        "array_layouts": {"0x100": {"length": 9, "stride": 3, "rows": 3}},
        "field_semantics": {"0x40": "condition", "0x50": "scalar"},
        "records": [
            {
                "id": 11,
                "string_fields": {"0x40": "Win"},
                "fields": {"0x50": 20},
                "array_fields": {"0x100/4": 7},
                "footer_arrays": {
                    "0x100/6": {"footer_off": "0x200", "values": [1, 2, 3]},
                },
            },
        ],
    }
    mixed = {row["key"]: row for row in profile.profile_columns(mixed_fixture)}
    assert mixed["0x40"]["kind"] == "string-field"
    assert mixed["0x40"]["semantic_name"] == "condition"
    assert mixed["0x50"]["kind"] == "scalar-field"
    assert mixed["0x100/4"]["kind"] == "array-cell"
    assert mixed["0x100/4"]["stride"] == 3 and mixed["0x100/4"]["column"] == 1
    assert mixed["0x100/6"]["kind"] == "footer-array"
    assert mixed["0x100/6"]["examples"][0]["footer_off"] == "0x200"
    assert mixed["0x100/6"]["common"][0]["value"] == "[1, 2, 3]"
    assert mixed["0x40"]["examples"][0]["name"] == "Win"

    rule_fixture = {
        "table": "RULES",
        "mode": "rules",
        "records": [
            {
                "id": 1, "unit_id": 3, "title": "", "fields": {"0x10": -1},
                "class_change_slot_index": 0, "skill_awards": [{"skill_id": 2}],
            },
            {
                "id": 2, "unit_id": 5, "title": "Promoted",
                "minimum_level": 50, "class_change_slot_index": 1,
                "fields": {"0x10": 50},
            },
        ],
    }
    rule_rows = {row["key"]: row for row in profile.profile_columns(rule_fixture)}
    assert rule_rows["0x10"]["kind"] == "rule-output"
    rule_summary = profile.profile_rules(rule_fixture)
    assert rule_summary["unit_count"] == 2
    assert rule_summary["titled_rule_count"] == 1
    assert rule_summary["level_independent_rule_count"] == 1
    assert rule_summary["minimum_levels"] == {"50": 1}
    assert rule_summary["class_change_slot_indices"] == {"0": 1, "1": 1}
    assert rule_summary["skill_award_count"] == 1

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
