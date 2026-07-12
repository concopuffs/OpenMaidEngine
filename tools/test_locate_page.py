"""Plain tests for locate_page.py."""
import json
import sys
import tempfile
from pathlib import Path

from locate_page import context_window, find_page, load_records

FAILS = []


def check(condition, message):
    (FAILS.append(message) or print("FAIL:", message)) if not condition else print("ok:", message)


def test_load_and_find():
    rows = [
        {"root_scene": "SC0000", "page": 1, "wait_offset": "0x83c"},
        {"root_scene": "SC0000", "page": 2, "wait_offset": "0x871"},
    ]
    with tempfile.TemporaryDirectory() as directory:
        path = Path(directory) / "pages.jsonl"
        path.write_text("\n".join(json.dumps(r) for r in rows) + "\n", encoding="utf-8")
        loaded = load_records(path)
    check(len(loaded) == 2, "JSONL page records load")
    check(find_page(loaded, "sc0000.bin", 2)["wait_offset"] == "0x871",
          "scene matching is case-insensitive and accepts .BIN")
    check(find_page(loaded, "SC0000", 3) is None, "missing page returns None")


def test_context_window():
    check(context_window([0, 5, 10, 15, 20], 10, 1) == (1, 4),
          "context window includes target and requested neighbors")
    check(context_window([0, 5, 10], 0, 8) == (0, 3), "context window clamps at script bounds")
    try:
        context_window([0, 5], 3, 1)
        raised = False
    except ValueError:
        raised = True
    check(raised, "non-instruction target is rejected")


def main():
    test_load_and_find()
    test_context_window()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
