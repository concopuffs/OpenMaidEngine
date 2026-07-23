#!/usr/bin/env python3
"""Extract an ID-dispatched SYS4 message table.

ITMES and SKMES are long chains of:

    eq <temporary>, <selected id global>, <record id>
    jcc ...
    show-text ...
    ...
    jmp <shared exit>

This tool discovers the selector global from that repeated shape, reconstructs
the displayed lines (including the surface text of furigana spans), and emits a
reusable ID-to-message JSON table.

Usage:
  py -3.11 -X utf8 tools/extract_message_table.py ITMES
  py -3.11 -X utf8 tools/extract_message_table.py SKMES [OUTNAME]
"""
from __future__ import annotations

import collections
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths
import sys4load


T_IMM = 0
T_INLINE_STRING = 2
T_GLOBAL_INT = 3


def resolve(name: str) -> Path:
    normalized = name.upper().removesuffix(".BIN")
    scripts = paths.scripts()
    try:
        return scripts[f"{normalized}.BIN"]
    except KeyError:
        raise SystemExit(f"not found: {normalized}.BIN") from None


def _dispatch_comparison(ins):
    """Return (selector global, record id) for an eq global/int-immediate guard."""
    if sys4load.display_label(ins.opcode) != "eq" or len(ins.args) != 3:
        return None
    left, right = ins.args[1:]
    if left[0] == T_GLOBAL_INT and right[0] == T_IMM:
        return left[1], right[1]
    if right[0] == T_GLOBAL_INT and left[0] == T_IMM:
        return right[1], left[1]
    return None


def discover_selector(scr) -> tuple[int, int]:
    """Return the most frequently ID-compared global and its guard count."""
    counts = collections.Counter(
        selector
        for ins in scr.instructions
        if (pair := _dispatch_comparison(ins)) is not None
        for selector in [pair[0]]
    )
    if not counts:
        raise ValueError(f"{scr.path.name}: no global/immediate eq dispatch guards")
    return counts.most_common(1)[0]


def _strings(ins, scr) -> list[str]:
    return [
        scr.strings[value][0]
        for arg_type, value in ins.args
        if arg_type == T_INLINE_STRING and value in scr.strings
    ]


def _message_body(scr, start: int, stop: int) -> dict | None:
    fragments: list[str] = []
    lines: list[str] = []
    furigana: list[dict] = []
    for ins in scr.instructions[start:stop]:
        operation = sys4load.display_label(ins.opcode)
        if operation == "jmp":
            break
        values = _strings(ins, scr)
        if operation == "show-text":
            fragments.extend(values)
        elif operation == "display-furigana" and values:
            surface = values[0]
            fragments.append(surface)
            annotation = {"line": len(lines), "text": surface}
            if len(values) > 1:
                annotation["reading"] = values[1]
            furigana.append(annotation)
        elif operation == "end-text-line":
            lines.append("".join(fragments))
            fragments = []
    if fragments:
        lines.append("".join(fragments))
    if not lines:
        return None
    message = {
        "title": lines[0],
        "description": "\n".join(lines[1:]),
    }
    if furigana:
        message["furigana"] = furigana
    return message


def extract_messages(scr, selector: int | None = None) -> tuple[list[dict], dict]:
    """Extract ordered message records and dispatch metadata from a script."""
    discovered_selector, comparison_count = discover_selector(scr)
    selector = discovered_selector if selector is None else selector
    guards = [
        (index, pair[1])
        for index, ins in enumerate(scr.instructions)
        if (pair := _dispatch_comparison(ins)) is not None and pair[0] == selector
    ]
    records: list[dict] = []
    seen: set[int] = set()
    for guard_index, (instruction_index, record_id) in enumerate(guards):
        stop = guards[guard_index + 1][0] if guard_index + 1 < len(guards) else len(scr.instructions)
        message = _message_body(scr, instruction_index + 1, stop)
        if message is None:
            continue
        if record_id in seen:
            raise ValueError(f"{scr.path.name}: duplicate message id {record_id}")
        seen.add(record_id)
        records.append({
            "id": record_id,
            "dispatch_offset": f"0x{scr.instructions[instruction_index].offset:x}",
            **message,
        })
    return records, {
        "selector_global": f"0x{selector:x}",
        "dispatch_guard_count": len(guards),
        "message_count": len(records),
        "selector_discovery_count": comparison_count,
    }


def main() -> int:
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    name = sys.argv[1].upper().removesuffix(".BIN")
    outname = sys.argv[2] if len(sys.argv) > 2 else name
    scr = sys4load.load(resolve(name))
    records, meta = extract_messages(scr)
    output = {
        "table": name,
        "source": scr.path.name,
        "magic": scr.magic,
        "mode": "message-dispatch",
        "record_count": len(records),
        **meta,
        "records": records,
    }
    outpath = paths.BUILD / "data" / f"{outname}.json"
    outpath.parent.mkdir(parents=True, exist_ok=True)
    outpath.write_text(
        json.dumps(output, ensure_ascii=False, indent=2), encoding="utf8"
    )
    print(
        f"{name}: selector={meta['selector_global']}, {len(records)} messages "
        f"-> build/data/{outname}.json"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
