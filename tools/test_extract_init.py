#!/usr/bin/env python3
"""Regression tests for INIT table extraction.

Run: py -3.11 -X utf8 tools/test_extract_init.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import extract_init
import extract_message_table
import paths
import sys4load


FAILS: list[str] = []


def check(condition: bool, message: str) -> None:
    print(("  ok  " if condition else " FAIL ") + message)
    if not condition:
        FAILS.append(message)


def test_real_name_tables() -> None:
    expected = {
        "SKINIT.BIN": (300, 131),
        "ITINIT.BIN": (1000, 287),
        "EBINIT.BIN": (1000, 277),
    }
    scripts = paths.scripts()
    for name, (span, count) in expected.items():
        records, meta = extract_init.extract_name(sys4load.load(scripts[name]))
        check(meta["record_span"] == span, f"{name}: record span is {span}")
        check(len(records) == count, f"{name}: extracts {count} named records")
        check(len({record["id"] for record in records}) == count,
              f"{name}: record ids are unique")

    items, _ = extract_init.extract_name(sys4load.load(scripts["ITINIT.BIN"]))
    by_id = {record["id"]: record for record in items}
    check(by_id[1]["name"] == "銅の鍵", "ITINIT item 1 is the copper key")
    check(len(by_id[1]["fields"]) == 5, "ITINIT item 1 owns only its five fields")
    check("desc" not in by_id[1], "ITINIT item 1 has no fabricated description")
    check(by_id[101]["desc"] == "ＨＰ３０回復", "ITINIT item 101 keeps its description")
    check(by_id[1]["fields"]["0x8c879"] == 10,
          "ITINIT columns use the runtime lookup base")
    check(len({key for record in items for key in record["fields"]}) == 13,
          "ITINIT has thirteen parallel-array fields")
    check(len({key for record in items for key in record.get("record_fields", {})}) == 44,
          "ITINIT linked row-major tables expose 44 populated columns")
    check(by_id[101]["record_fields"]["0xa5301/3/0"] == 30,
          "ITINIT item 101 stores HP recovery in row-major column zero")
    check(by_id[108]["record_fields"]["0x906f9/30/8"] == -5,
          "ITINIT item 108 preserves its paralysis-removal delta")


def test_static_negative_write() -> None:
    class Instruction:
        opcode = extract_init.SUB
        args = [(extract_init.T_GLOBAL_INT, 0x123),
                (extract_init.T_IMM, 0), (extract_init.T_IMM, 7)]

    check(extract_init._static_global_write(Instruction()) == (0x123, -7),
          "INIT subtraction writes preserve negative values")


def test_real_message_tables() -> None:
    scripts = paths.scripts()
    expected = {
        "ITMES.BIN": (0x8C877, 287),
        "SKMES.BIN": (0xA6E59, 131),
    }
    for name, (selector, count) in expected.items():
        records, meta = extract_message_table.extract_messages(
            sys4load.load(scripts[name])
        )
        check(meta["selector_global"] == f"0x{selector:x}",
              f"{name}: discovers selector global 0x{selector:x}")
        check(len(records) == count, f"{name}: extracts {count} messages")
        check(len(records) == meta["dispatch_guard_count"],
              f"{name}: every dispatch guard yields a message")
        check(len({record['id'] for record in records}) == len(records),
              f"{name}: message ids are unique")

    item_messages, _ = extract_message_table.extract_messages(
        sys4load.load(scripts["ITMES.BIN"])
    )
    items = {record["id"]: record for record in item_messages}
    check(items[1]["title"] == "【重要：銅の鍵】　　　　　LEVEL-E",
          "ITMES item 1 keeps its display title")
    check(items[1]["description"] == "　銅の扉を開閉することが可能",
          "ITMES item 1 keeps its player-facing behavior")
    check("濃緑色" in items[32]["title"],
          "ITMES reconstructs furigana surface text inside a title")
    check(items[32]["furigana"][0]["reading"] == "のうりょくしょく",
          "ITMES preserves furigana readings")

    skill_messages, _ = extract_message_table.extract_messages(
        sys4load.load(scripts["SKMES.BIN"])
    )
    skills = {record["id"]: record for record in skill_messages}
    check(skills[1]["title"] == "【移動スキル：飛行】",
          "SKMES skill 1 keeps its display title")
    check(skills[1]["description"] == "　床のない地形を移動可能になる",
          "SKMES skill 1 keeps its player-facing behavior")


def test_message_join() -> None:
    scripts = paths.scripts()
    expected = {
        "IT": (287, "　銅の扉を開閉することが可能"),
        "SK": (131, "　床のない地形を移動可能になる"),
    }
    joined = {}
    for prefix, (count, _) in expected.items():
        records, _ = extract_init.extract_name(
            sys4load.load(scripts[f"{prefix}INIT.BIN"])
        )
        meta = extract_init.join_messages(
            records, sys4load.load(scripts[f"{prefix}MES.BIN"])
        )
        check(meta["joined_count"] == count,
              f"{prefix}INIT joins all {count} {prefix}MES messages")
        check(not meta["init_ids_without_message"] and not meta["message_ids_without_init"],
              f"{prefix}INIT and {prefix}MES ids match exactly")
        joined[prefix] = {record["id"]: record for record in records}
    check(joined["IT"][1]["message"]["description"] == expected["IT"][1],
          "INIT/MES join uses the shared runtime id")


def test_field_semantics() -> None:
    scripts = paths.scripts()
    items, _ = extract_init.extract_name(sys4load.load(scripts["ITINIT.BIN"]))
    semantics = extract_init.field_semantics(items)
    check(semantics["0x8c879"] == "item_sort_key",
          "parallel INIT fields expose canonical semantic names")
    check(semantics["0x9f541/14/8"] == "item_stat_modifiers.critical_chance",
          "row-table columns expose canonical semantic names")


if __name__ == "__main__":
    test_real_name_tables()
    test_static_negative_write()
    test_real_message_tables()
    test_message_join()
    test_field_semantics()
    if FAILS:
        raise SystemExit(f"{len(FAILS)} failed checks")
    print("all extract_init checks passed")
