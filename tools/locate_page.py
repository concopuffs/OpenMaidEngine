"""Resolve a runtime ADV page number to authoritative script offsets and disassembly.

Usage:
  py -3.11 -X utf8 tools/locate_page.py SC0000 14
  py -3.11 -X utf8 tools/locate_page.py SC0000 14 --map path/to/page-map.jsonl --context 10

The Godot runtime writes build/page-map-<SCENE>.jsonl at each wait-for-input. Page numbers are
run-relative conveniences; wait_script + wait_offset is the stable bytecode coordinate.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

import paths
import sys4load


def load_records(path: Path) -> list[dict]:
    records = []
    with path.open(encoding="utf-8") as f:
        for number, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                records.append(json.loads(line))
            except json.JSONDecodeError as exc:
                raise ValueError(f"{path}:{number}: invalid JSON: {exc}") from exc
    return records


def find_page(records: list[dict], scene: str, page: int) -> dict | None:
    scene = scene.upper().removesuffix(".BIN")
    return next((r for r in records
                 if str(r.get("root_scene", "")).upper().removesuffix(".BIN") == scene
                 and r.get("page") == page), None)


def context_window(offsets: list[int], target: int, radius: int) -> tuple[int, int]:
    try:
        index = offsets.index(target)
    except ValueError as exc:
        raise ValueError(f"offset 0x{target:x} is not an instruction boundary") from exc
    return max(0, index - radius), min(len(offsets), index + radius + 1)


def disasm_context(script: str, target: int, radius: int) -> list[tuple[int, str, bool]]:
    key = script.upper()
    if not key.endswith(".BIN"):
        key += ".BIN"
    scripts = paths.scripts()
    if key not in scripts:
        raise ValueError(f"script {key} is not present in the override-aware corpus")
    scr = sys4load.load(scripts[key])
    sys4load.decode_code(scr)
    line_by_offset = {}
    for line in sys4load.render_listing(scr).splitlines():
        match = re.match(r"\s*0x([0-9a-fA-F]+):\s*(.*)", line)
        if match:
            line_by_offset[int(match.group(1), 16)] = match.group(2).rstrip()
    offsets = [ins.offset for ins in scr.instructions]
    begin, end = context_window(offsets, target, radius)
    return [(off, line_by_offset.get(off, "?"), off == target) for off in offsets[begin:end]]


def parse_hex(value: object, field: str) -> int:
    if not isinstance(value, str):
        raise ValueError(f"page record has no {field}")
    return int(value, 0)


def report(record: dict, radius: int) -> str:
    scene, page = record["root_scene"], record["page"]
    wait_script = record["wait_script"]
    wait_offset = parse_hex(record.get("wait_offset"), "wait_offset")
    lines = [f"{scene} P{page:03d}",
             f"page start: {record.get('page_start_script') or '?'}@{record.get('page_start_offset') or '?'}",
             f"last text:  {record.get('text_script') or '?'}@{record.get('text_offset') or '?'}",
             f"wait:       {wait_script}@0x{wait_offset:x}"]
    stack = record.get("call_stack") or []
    if stack:
        lines.append("call stack: " + " > ".join(stack))
    if record.get("text"):
        lines.append("text: " + str(record["text"]).replace("\r", " ").replace("\n", " "))
    lines += ["", f"disassembly around {wait_script}@0x{wait_offset:x}:"]
    for offset, instruction, selected in disasm_context(wait_script, wait_offset, radius):
        lines.append(f"{'>>>' if selected else '   '} 0x{offset:05x}: {instruction}")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("scene", help="root scene, e.g. SC0000")
    parser.add_argument("page", type=int, help="one-based runtime page number")
    parser.add_argument("--map", type=Path, dest="map_path", help="page-map JSONL (default: build/page-map-SCENE.jsonl)")
    parser.add_argument("--context", type=int, default=8, help="instructions on either side of the wait (default: 8)")
    args = parser.parse_args()
    scene = args.scene.upper().removesuffix(".BIN")
    map_path = args.map_path or paths.BUILD / f"page-map-{scene}.jsonl"
    if not map_path.is_file():
        parser.error(f"page map not found: {map_path} (run the Godot scene to the desired page first)")
    if args.context < 0:
        parser.error("--context must be non-negative")
    try:
        record = find_page(load_records(map_path), scene, args.page)
        if record is None:
            raise ValueError(f"{scene} page {args.page} is not present in {map_path}")
        print(report(record, args.context))
    except (OSError, ValueError, KeyError) as exc:
        parser.error(str(exc))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
