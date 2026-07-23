#!/usr/bin/env python3
"""Regression tests for INIT table extraction.

Run: py -3.11 -X utf8 tools/test_extract_init.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import extract_init
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


if __name__ == "__main__":
    test_real_name_tables()
    test_static_negative_write()
    if FAILS:
        raise SystemExit(f"{len(FAILS)} failed checks")
    print("all extract_init checks passed")
