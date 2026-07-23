#!/usr/bin/env python3
"""Extract an ID-dispatched SYS4 message table.

The shipped tables use two equivalent control-flow shapes:

    eq <temporary>, <selected id global>, <record id>
    jcc <next guard>
    <message body>
    jmp <shared exit>

or a compact guard block whose successful branches target message bodies stored
later in the script. This tool discovers the selector global, follows either
layout, reconstructs the displayed lines (including the surface text of
furigana spans), and emits a reusable ID-to-message JSON table.

Usage:
  py -3.11 -X utf8 tools/extract_message_table.py ITMES
  py -3.11 -X utf8 tools/extract_message_table.py SKMES [OUTNAME]
  py -3.11 -X utf8 tools/extract_message_table.py VIMES
  py -3.11 -X utf8 tools/extract_message_table.py EIMES
  py -3.11 -X utf8 tools/extract_message_table.py CIMES
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
BRANCH_SENTINEL = 0xFFFFFFFF

MESSAGE_LAYOUTS = {
    "CIMES.BIN": "character-biography",
    "EIMES.BIN": "enemy-commentary",
}


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


def _message_body(
    scr, start: int, stop: int, layout: str = "title-description"
) -> dict | None:
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
    if layout == "character-biography":
        message = {
            "biography": "\n".join(lines),
        }
    elif layout == "enemy-commentary":
        message = {
            "summary": lines[0],
            "strategy": "\n".join(lines[1:]),
        }
    else:
        message = {
            "title": lines[0],
            "description": "\n".join(lines[1:]),
        }
    if furigana:
        message["furigana"] = furigana
    return message


def _branch_target_index(scr, guard_index: int, offsets: dict[int, int]) -> int | None:
    """Resolve the conditional branch immediately following a dispatch guard."""
    if guard_index + 1 >= len(scr.instructions):
        return None
    branch = scr.instructions[guard_index + 1]
    if sys4load.display_label(branch.opcode) != "jcc":
        return None
    for arg_type, value in branch.args[1:]:
        if arg_type == T_IMM and value != BRANCH_SENTINEL and value in offsets:
            return offsets[value]
    return None


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
    offsets = {
        instruction.offset: index
        for index, instruction in enumerate(scr.instructions)
    }
    message_layout = MESSAGE_LAYOUTS.get(scr.path.name.upper(), "title-description")
    dispatch_layout_counts = collections.Counter()
    for guard_index, (instruction_index, record_id) in enumerate(guards):
        stop = guards[guard_index + 1][0] if guard_index + 1 < len(guards) else len(scr.instructions)
        message_start = instruction_index + 2
        message = _message_body(scr, message_start, stop, message_layout)
        dispatch_layout = "fallthrough"
        if message is None:
            branch_target = _branch_target_index(scr, instruction_index, offsets)
            if branch_target is not None:
                message_start = branch_target
                message = _message_body(
                    scr, message_start, len(scr.instructions), message_layout
                )
                dispatch_layout = "branch-target"
        if message is None:
            continue
        if record_id in seen:
            raise ValueError(f"{scr.path.name}: duplicate message id {record_id}")
        seen.add(record_id)
        dispatch_layout_counts[dispatch_layout] += 1
        records.append({
            "id": record_id,
            "dispatch_offset": f"0x{scr.instructions[instruction_index].offset:x}",
            "message_offset": f"0x{scr.instructions[message_start].offset:x}",
            **message,
        })
    dispatch_layout = (
        next(iter(dispatch_layout_counts))
        if len(dispatch_layout_counts) == 1
        else "mixed"
    )
    return records, {
        "selector_global": f"0x{selector:x}",
        "dispatch_guard_count": len(guards),
        "message_count": len(records),
        "selector_discovery_count": comparison_count,
        "dispatch_layout": dispatch_layout,
        "message_layout": message_layout,
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
