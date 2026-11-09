#!/usr/bin/env python3
"""Runtime global image of a profile's data-table boot, per script and per append layer.

Static INIT extraction (tools/extract_init.py) infers layouts from write shapes. This tool is the
execution oracle beside it: the C# VM (`Ome.Cli init-image`) runs every data-table script in native
boot order -- INIT2's base initializer calls, then each mounted append catalog's AUTORUN fragment
calls in ascending selector order (docs/asset-resolution-re.md) -- through one session and records
each script's global-bank delta (new or changed int/string cells).

  py -3.11 -X utf8 tools/init_image.py --profile kamidori            # build the image
  py -3.11 -X utf8 tools/init_image.py --profile kamidori --summary  # per-script cell counts

Writes build/games/<profile>/init-image.json:
  {profile, game_root, scripts: [{script, packed_id, halt, steps, ints:{addr: v}, strings:{addr: s}}]}
Deltas omit writes that store a cell's existing value.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
from functools import cache
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import sys4load

CALL_SCRIPT = 0x03
OUTPUT = paths.BUILD / "init-image.json"


def _callscript_names() -> dict[int, str]:
    path = paths.BUILD / "callscript-names.json"
    if not path.exists():
        raise SystemExit(f"missing {path}; run tools/parse_sys4ini.py first")
    return {int(k): v for k, v in json.loads(path.read_text(encoding="utf-8")).items()}


def _called_tables(script_path: Path, names: dict[int, str]) -> list[str]:
    """Return the `*INIT*.BIN` targets of a script's immediate call-script instructions, in order."""
    script = sys4load.load(script_path)
    out = []
    for ins in script.instructions:
        if ins.opcode == CALL_SCRIPT and ins.args and ins.args[0][0] == 0:
            name = names.get(ins.args[0][1], "")
            if "INIT" in name.upper():
                out.append(name)
    return out


def boot_sequence() -> list[str]:
    """INIT2's base data-table calls, then each append AUTORUN's calls in ascending selector order."""
    names = _callscript_names()
    scripts = paths.scripts()
    sequence = _called_tables(scripts["INIT2.BIN"], names)
    selectors = sorted({k >> 24 for k in names if k >> 24})
    for selector in selectors:
        autorun = names.get(selector << 24)
        if autorun and autorun.upper() in scripts:
            sequence += _called_tables(scripts[autorun.upper()], names)
    return sequence


def build() -> int:
    sequence = boot_sequence()
    root = paths.GAME_DIR
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    command = [
        "dotnet", "run", "--project", str(paths.REPO / "engine" / "Ome.Cli"), "-c", "Release", "--",
        "init-image", str(OUTPUT), *sequence,
        "--profile", paths.CONTEXT.profile_id, "--game-root", str(root),
    ]
    completed = subprocess.run(command, cwd=paths.REPO)
    return completed.returncode


@cache
def load() -> dict:
    """Load the built image for the selected profile."""
    if not OUTPUT.exists():
        raise SystemExit(f"missing {OUTPUT}; run tools/init_image.py first")
    return json.loads(OUTPUT.read_text(encoding="utf-8"))


def table_layers(table: str) -> list[dict]:
    """Every executed layer (base + `$n$` fragments) of one table, in boot order."""
    table = table.upper().removesuffix(".BIN")
    out = []
    for step in load()["scripts"]:
        stem = step["script"].upper().removesuffix(".BIN")
        if stem == table or (stem.startswith("$") and stem.split("$")[-1] == table):
            out.append(step)
    return out


def merged(table: str) -> tuple[dict[int, int], dict[int, str]]:
    """The table's final int and string cells after every layer has run."""
    ints: dict[int, int] = {}
    strings: dict[int, str] = {}
    for step in table_layers(table):
        ints.update({int(k): v for k, v in step["ints"].items()})
        strings.update({int(k): v for k, v in step["strings"].items()})
    return ints, strings


def project(table: str) -> dict:
    """Project a name-mode table's executed image through its static base-table layout.

    The base extraction (`build/<profile>/data/<TABLE>.json`) supplies the geometry: name array base,
    record span, description bases, parallel field bases, and row-table columns. Every id whose name
    cell holds a string after all layers ran becomes a record; `origin` names the layer that first
    wrote that name and `layers` every layer that wrote any of the record's cells.
    """
    table = table.upper().removesuffix(".BIN")
    layout = json.loads((paths.BUILD / "data" / f"{table}.json").read_text(encoding="utf-8"))
    if layout.get("mode") != "name" or "name_array_base" not in layout:
        raise SystemExit(f"{table}: projection needs a name-mode base extraction")
    name_base = int(layout["name_array_base"], 16)
    span = int(layout["record_span"])
    desc_bases = {key: int(base, 16) for key, base in layout.get("desc_array_bases", {}).items()}
    parallel = [int(key, 16) for key in layout.get("field_columns") or []]
    rows = [tuple(int(part, 0) for part in key.split("/"))
            for key in layout.get("record_field_columns") or []]

    layers = table_layers(table)
    writer_int: dict[int, str] = {}
    writer_str: dict[int, str] = {}
    first_name: dict[int, str] = {}
    for step in layers:
        for key in step["ints"]:
            writer_int[int(key)] = step["script"]
        for key in step["strings"]:
            address = int(key)
            writer_str[address] = step["script"]
            first_name.setdefault(address, step["script"])
    ints, strings = merged(table)

    records = []
    for record_id in range(1, span):
        name = strings.get(name_base + record_id)
        if name is None:
            continue
        record = {"id": record_id, "name": name, "origin": first_name[name_base + record_id]}
        touched = {writer_str[name_base + record_id]}
        for key, base in desc_bases.items():
            if (text := strings.get(base + record_id)) is not None:
                record[key] = text
                touched.add(writer_str[base + record_id])
        fields = {}
        for base in parallel:
            if (value := ints.get(base + record_id)) is not None:
                fields[f"0x{base:x}"] = value
                touched.add(writer_int[base + record_id])
        record_fields = {}
        for base, stride, column in rows:
            address = base + record_id * stride + column
            if (value := ints.get(address)) is not None:
                record_fields[f"0x{base:x}/{stride}/{column}"] = value
                touched.add(writer_int[address])
        record["fields"] = fields
        if record_fields:
            record["record_fields"] = record_fields
        record["layers"] = [step["script"] for step in layers if step["script"] in touched]
        records.append(record)
    return {
        "table": table,
        "source": "init-image",
        "mode": "merged",
        "record_count": len(records),
        "layers": [step["script"] for step in layers],
        "layout_source": f"{table}.json",
        "name_array_base": layout["name_array_base"],
        "record_span": span,
        "field_columns": layout.get("field_columns"),
        "record_field_columns": layout.get("record_field_columns"),
        "desc_array_bases": layout.get("desc_array_bases", {}),
        "records": records,
    }


def summary() -> int:
    for step in load()["scripts"]:
        print(f"  {step['script']:<18} {step['packed_id']:>11} {step['halt'] or '-':<6} "
              f"{len(step['ints']):>7} int {len(step['strings']):>6} string")
    return 0


def main(argv: list[str]) -> int:
    if "--summary" in argv:
        return summary()
    if "--project" in argv:
        index = argv.index("--project")
        if index + 1 >= len(argv):
            raise SystemExit("--project requires a TABLE")
        projected = project(argv[index + 1])
        out = paths.BUILD / "data" / f"MERGED-{projected['table']}.json"
        out.write_text(json.dumps(projected, ensure_ascii=False, indent=2), encoding="utf-8")
        origins: dict[str, int] = {}
        for record in projected["records"]:
            origins[record["origin"]] = origins.get(record["origin"], 0) + 1
        print(f"{projected['table']}: {projected['record_count']} records "
              f"({', '.join(f'{k} {v}' for k, v in origins.items())}) -> "
              f"{out.relative_to(paths.REPO).as_posix()}")
        return 0
    return build()


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
