#!/usr/bin/env python3
"""Layouts of 創刻のアテリアル's `*INIT` data tables, inferred from how the scripts write them.

Arterial's tables are flat scripts of constant writes, record after record (docs/arterial-data-tables.md). Each
field of a record lives in an array of its own: a cell is `base + stride * id + column`. This tool finds those
arrays without a hand-written schema per table:

1. A record starts at each write into the table's *leader* array (LEADERS below: the array every record writes
   first, and its capacity). The record's id is the cell's address less the leader's base.
2. Every other write is tried at each stride, for the record before it and for the record after it (a record may
   write a field before its leader: the appends stamp their number so): its key is the address less stride times
   the id. The record and stride whose key the most records share are the field's, and the key is the field's
   base plus its column.
3. Keys of one bank and stride that lie within one stride of each other are one array. Every cell the boot
   wrote (the executed image, tools/init_image.py) is then placed in the array whose range holds it, so the
   append layers' edits of existing records are counted too.
4. A table's arrays lie back to back, each a whole number of capacities long, so their bases agree modulo the
   capacity. An array whose base does not agree with most of its bank's is marked off the grid (`†`): another
   table's cells lie between, or the array is not indexed by the record's id (one cell a card row, say) and its
   base and ids here are not to be trusted.

  py -3.11 -X utf8 tools/init_arterial.py --game arterial              # every table: summary lines
  py -3.11 -X utf8 tools/init_arterial.py --game arterial CHINIT       # one table's arrays, column by column
  py -3.11 -X utf8 tools/init_arterial.py --game arterial --write      # build/games/arterial/data/<T>.layout.json
  py -3.11 -X utf8 tools/init_arterial.py --game arterial CHINIT --markdown   # the arrays as a Markdown table
  py -3.11 -X utf8 tools/init_arterial.py --game arterial ARINIT --leader GS:0x1265c:13   # try a leader

It reads build/games/<game>/init-image.json and build/games/<game>/disasm/*.asm (tools/extract_phase2.py), and
counts each array's consumers from the corpus's lookup-array instructions. A table with no leader (bulk arrays, or
a shape the method does not fit) is listed with the cells it wrote and nothing more.

The three tables the game runs for an area (MPINIT, ROINIT, UNINIT) are not in the boot image. Each opens by
clearing its arrays, which gives their bases and sizes exactly, and then fills them in one branch per area; they
are listed from the listings alone, with the areas, slots and values each array gets:

  py -3.11 -X utf8 tools/init_arterial.py --game arterial MPINIT [--markdown]
"""
from __future__ import annotations

import json
import os
import re
import sys
from bisect import bisect_right
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from functools import cache

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths

IMAGE = paths.BUILD / "init-image.json"
DISASM = paths.BUILD / "disasm"
DATA = paths.BUILD / "data"

# The strides a field is tried at. A stride the list lacks reads as stride-1 fields that no two records share.
STRIDES = (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 15, 16, 20, 24, 25, 30, 32, 40, 50, 60, 64, 90, 100, 120, 150, 160, 200,
           300)
BANKS = {"global-int": "G", "global-string": "GS"}
WRITE = re.compile(r"^\s+0x[0-9a-f]+:\s+(\S+) \((global-int|global-string) 0x([0-9a-f]+)\)")
LOOKUP = re.compile(r"lookup-array \([^)]*\) \((global-int|global-string) 0x([0-9a-f]+)\)")
LOOKUP_2D = re.compile(r"lookup-array-2d \([^)]*\) \((global-int|global-string) 0x([0-9a-f]+)\) (?:\([^)]*\)|\S+) "
                       r"(0x[0-9a-f]+)")

# The tables the game runs for an area: 200 slots an array, cleared at the script's start and filled in the branch
# for the area in play (global 0x9883).
AREA_TABLES = ("MPINIT", "ROINIT", "UNINIT")
SLOTS = 0xc8
INSTRUCTION = re.compile(r"^\s+0x[0-9a-f]+:\s+(.*)$")
CLEAR = re.compile(r"copy-to-global \(global-int 0x([0-9a-f]+)\) (0x[0-9a-f]+|\(local-int 0x0\))$")
TIMES = re.compile(r"mul \(local-int 0x0\) 0x([0-9a-f]+) 0x([0-9a-f]+)$")
AREA = re.compile(r"eq \(local-int 0x0\) \(global-int 0x9883\) 0x([0-9a-f]+)$")
CONSTANT = re.compile(r"(mov|set-string) \((global-int|global-string) 0x([0-9a-f]+)\) (.+)$")


@dataclass(frozen=True)
class Leader:
    """The array each record of a table writes first: its bank and base, and how many ids the table's arrays
    hold (0 to capacity - 1). `group` says the id is a row and a slot of that many per row (a card is slot 1 to
    29 of a unit)."""
    bank: str
    base: int
    capacity: int
    group: int | None = None
    stride: int = 1   # the leader's own stride, where a record's first write is one of several cells per id
    also: tuple[int, ...] = ()   # other arrays that start a record, where records of several kinds share the ids

    @property
    def bases(self) -> tuple[int, ...]:
        return (self.base, *self.also)


# One entry per boot table, in INIT2's order. A table without one is reported by its cells only.
LEADERS: dict[str, Leader] = {
    "SCINIT": Leader("G", 0x214b71, 10000),   # scene ids start at 0 (SC0000); the scripts index this base
    "CHINIT": Leader("G", 0x297db9, 1000),
    "CFINIT": Leader("G", 0x266c91, 100000, group=100),
    "CCINIT": Leader("G", 0x29fa46, 100, stride=4),
    "CDINIT": Leader("GS", 0x7d7c, 6000, group=30),
    "TAINIT": Leader("GS", 0xdb3c, 1000),
    "COINIT": Leader("GS", 0xe30c, 100),
    "SKINIT": Leader("GS", 0xef28, 1000),
    "ITINIT": Leader("GS", 0xfec8, 2000),
    "ILINIT": Leader("GS", 0x11e08, 32),
    "AMINIT": Leader("G", 0x32fbc5, 300),
    "DEINIT": Leader("GS", 0x1201c, 1000),
    "REINIT": Leader("G", 0x3718e6, 100, stride=5),
    "ALINIT": Leader("G", 0x372ba6, 2000, also=(0x373376,)),   # a recipe makes an item, or a card (its row)
    "SHINIT": Leader("G", 0x3873c6, 1000, also=(0x3877ae,)),   # an entry sells an item, or a card (its row)
    "ARINIT": Leader("GS", 0x1268e, 1000),   # the opponents; the ten arenas' own arrays come out off the grid
    "FAINIT": Leader("GS", 0x29e4, 1000),
    "CVINIT": Leader("G", 0x2979d1, 1000),
    "CIINIT": Leader("GS", 0x6864, 200),
    "VIINIT": Leader("GS", 0x647c, 1000),
    "TIINIT": Leader("GS", 0x75ac, 1000),
    "CGINIT": Leader("G", 0x2283f1, 3000, stride=2),
    "SPINIT": Leader("G", 0x25fec6, 1000),
    "MUINIT": Leader("G", 0x260667, 50),
}


@dataclass
class Array:
    bank: str
    base: int
    stride: int
    columns: dict[int, dict] = field(default_factory=dict)
    on_grid: bool = True

    @property
    def key(self) -> str:
        return f"{self.bank}:{self.base:#x}"


def table_of(script: str) -> tuple[int, str]:
    """(layer, table) of a boot script name: `$3$CDINIT.BIN` is layer 3 of CDINIT."""
    match = re.fullmatch(r"(?:\$(\d)\$)?([A-Z0-9]+)\.BIN", script)
    return int(match.group(1) or 0), match.group(2)


@cache
def image() -> dict[str, dict[tuple[str, int], tuple[int, object]]]:
    """table -> {(bank, address): (layer, value)}: each cell's final value and the layer that wrote it last."""
    if not IMAGE.exists():
        raise SystemExit(f"missing {IMAGE}; run tools/init_image.py first")
    cells: dict[str, dict] = defaultdict(dict)
    for entry in json.loads(IMAGE.read_text(encoding="utf-8"))["scripts"]:
        layer, table = table_of(entry["script"])
        for bank, written in (("G", entry["ints"]), ("GS", entry["strings"])):
            for address, value in written.items():
                cells[table][(bank, int(address))] = (layer, value)
    return cells


def boot_tables() -> list[str]:
    """The tables of the executed image, in boot order."""
    order: list[str] = []
    for entry in json.loads(IMAGE.read_text(encoding="utf-8"))["scripts"]:
        _, table = table_of(entry["script"])
        if table not in order:
            order.append(table)
    return order


def write_order(table: str, layer: int) -> list[tuple[str, int]]:
    """The cells a layer's script names as an instruction's first operand, in code order."""
    path = DISASM / (f"{table}.asm" if layer == 0 else f"${layer}${table}.asm")
    if not path.exists():
        return []
    writes = []
    for line in path.read_text(encoding="utf-8").split("\n"):
        match = WRITE.match(line)
        if match:
            writes.append((BANKS[match.group(2)], int(match.group(3), 16)))
    return writes


@cache
def consumers() -> dict[tuple[str, int], dict]:
    """(bank, base) -> {strides: {stride: uses}, scripts: n}, from every lookup-array in the corpus."""
    if not DISASM.is_dir():
        raise SystemExit(f"missing {DISASM}; run tools/extract_phase2.py first")
    found: dict[tuple[str, int], dict] = {}
    for path in sorted(DISASM.glob("*.asm")):
        text = path.read_text(encoding="utf-8")
        seen = set()
        for bank, base in LOOKUP.findall(text):
            seen.add((BANKS[bank], int(base, 16), 1))
        for bank, base, stride in LOOKUP_2D.findall(text):
            seen.add((BANKS[bank], int(base, 16), int(stride, 16)))
        for bank, base, stride in seen:
            slot = found.setdefault((bank, base), {"strides": Counter(), "scripts": 0})
            slot["strides"][stride] += 1
        for key in {(bank, base) for bank, base, _ in seen}:
            found[key]["scripts"] += 1
    return found


def records_of(table: str, leader: Leader) -> tuple[list[tuple[int, int, list[tuple[str, int]]]],
                                                    dict[int, list[tuple[str, int]]]]:
    """([(layer, id, writes)], {layer: the writes before its first record}). A record's writes run from its leader
    to the next record's."""
    records, before = [], {}
    for layer in range(5):
        current = None
        for bank, address in write_order(table, layer):
            base = next((b for b in leader.bases if bank == leader.bank
                         and b <= address < b + leader.capacity * leader.stride), None)
            if base is not None:
                rid = (address - base) // leader.stride
                if current is None or current[1] != rid:   # another cell of the same id goes on with the record
                    current = (layer, rid, [])
                    records.append(current)
            elif current is None:
                before.setdefault(layer, []).append((bank, address))
            else:
                current[2].append((bank, address))
    return records, before


def infer(table: str, leader: Leader) -> tuple[list[Array], list[tuple[str, int]], dict]:
    """The table's arrays, the cells no array holds, and what the records looked like."""
    records, before = records_of(table, leader)
    arrays, loose = infer_arrays(leader, records, image()[table], consumers(), before)
    shape = {"records": len(records), "layers": dict(sorted(Counter(r[0] for r in records).items())),
             "ids": (min(r[1] for r in records), max(r[1] for r in records)) if records else None,
             "before_first_record": sum(len(writes) for writes in before.values())}
    return arrays, loose, shape


def infer_arrays(leader: Leader, records: list[tuple[int, int, list[tuple[str, int]]]],
                 cells: dict[tuple[str, int], tuple[int, object]],
                 indexed: dict[tuple[str, int], dict],
                 before: dict[int, list[tuple[str, int]]] | None = None) -> tuple[list[Array], list[tuple[str, int]]]:
    """Steps 2 and 3 of the method: the arrays the records' writes agree on, with every cell of `cells` (address
    -> (layer, value)) placed in them, and the cells none holds. `indexed` holds the bases the scripts index, and
    `before` each layer's writes ahead of its first record."""
    # The writes between two leaders, with the record before them and the record after them (None at a layer's end).
    runs: list[tuple[int | None, int | None, list[tuple[str, int]]]] = []
    for at, (layer, rid, writes) in enumerate(records):
        if at == 0 or records[at - 1][0] != layer:
            runs.append((None, rid, (before or {}).get(layer, [])))
        following = records[at + 1] if at + 1 < len(records) and records[at + 1][0] == layer else None
        runs.append((rid, following[1] if following else None, writes))
    shared: Counter = Counter()
    for earlier, later, writes in runs:   # a run counts once for a key, whichever of its writes and records give it
        shared.update({(bank, stride, address - stride * rid) for bank, address in set(writes)
                       for rid in {earlier, later} - {None} for stride in STRIDES})
    keys: Counter = Counter()
    for earlier, later, writes in runs:
        owners = [rid for rid in (earlier, later) if rid is not None]   # a tie goes to the record before
        for bank, address in writes:
            support, _, stride, rid = max((shared[(bank, s, address - s * rid)], -at, s, rid)
                                          for at, rid in enumerate(owners) for s in reversed(STRIDES))
            # max() prefers the larger stride on a tie; the smaller is wanted, as before.
            stride = min(s for s in STRIDES if shared[(bank, s, address - s * rid)] == support)
            if support >= 2:
                keys[(bank, stride, address - stride * rid)] += 1

    # Arrays from the keys two or more records share, widest support first, so a rare column joins the array its
    # neighbours made instead of founding one at another stride.
    arrays: list[Array] = [Array(leader.bank, base, leader.stride) for base in leader.bases]
    for (bank, stride, key), _ in sorted(keys.items(), key=lambda item: (-item[1], item[0])):
        if any(a.bank == bank and a.base <= key < a.base + a.stride for a in arrays):
            continue
        joined = next((a for a in arrays if a.bank == bank and a.stride == stride and key < a.base < key + stride), None)
        if joined:
            joined.base = key   # a lower column of an array found by a higher one
            continue
        if any(a.bank == bank and a.base < key + stride and key < a.base + a.stride * leader.capacity
               for a in arrays):
            continue            # inside another array's range: that array's cell at another id
        arrays.append(Array(bank, key, stride))
    # A consumer names the true base where column 0 is never written.
    for array in arrays:
        for (bank, base) in indexed:
            if bank == array.bank and array.base - array.stride < base < array.base \
                    and not any(o is not array and o.bank == bank and o.base <= base < o.base + o.stride for o in arrays):
                array.base = base
                break
    arrays.sort(key=lambda a: (a.bank, a.base))

    by_bank = {bank: [a for a in arrays if a.bank == bank] for bank in ("G", "GS")}
    loose = []
    for (bank, address), (layer, value) in sorted(cells.items()):
        candidates = by_bank[bank]
        at = bisect_right([a.base for a in candidates], address) - 1
        array = candidates[at] if at >= 0 else None
        if array is None or address >= array.base + array.stride * leader.capacity:
            loose.append((bank, address))
            continue
        rid, column = divmod(address - array.base, array.stride)
        slot = array.columns.setdefault(column, {"ids": [], "layers": Counter(), "values": []})
        slot["ids"].append(rid)
        slot["layers"][layer] += 1
        slot["values"].append(value)
    arrays = [a for a in arrays if a.columns]
    mark_grid(leader, arrays)
    return arrays, loose


def mark_grid(leader: Leader, arrays: list[Array]) -> None:
    """Step 4: in each bank, the bases most arrays agree on modulo the capacity are the grid, and an array that
    disagrees is off it. The leading arrays are not asked (a leader may lie among another table's arrays), and a
    bank where no two arrays agree has no grid."""
    for bank in ("G", "GS"):
        asked = [a for a in arrays if a.bank == bank and not (bank == leader.bank and a.base in leader.bases)]
        classes = Counter(a.base % leader.capacity for a in asked)
        if not classes:
            continue
        grid, members = max(classes.items(), key=lambda item: (item[1], -item[0]))
        if members >= 2:
            for a in asked:
                a.on_grid = a.base % leader.capacity == grid


def describe(values: list, bank: str) -> str:
    if bank == "GS":
        return " | ".join(str(v)[:18] for v in values[:3])
    numbers = [v for v in values if isinstance(v, int)]
    counts = Counter(numbers)
    text = f"{min(numbers)}..{max(numbers)}, {len(counts)} distinct"
    if len(counts) <= 6:
        text += " " + ", ".join(f"{value}×{n}" for value, n in sorted(counts.items()))
    return text


def uses_text(array: Array, group: int | None) -> str:
    use = consumers().get((array.bank, array.base))
    if not use:
        return "no lookup"
    strides = ", ".join(f"stride {stride}" + (f" ({n} scripts)" if len(use["strides"]) > 1 else "")
                        for stride, n in sorted(use["strides"].items()))
    return f"{use['scripts']} scripts, {strides}"


def by_consumers(table: str) -> list[Array]:
    """A table without a leader, by the arrays the scripts index: each cell goes to the indexed array with the
    greatest base at or below it, at the stride most scripts use. An array no script indexes with a variable is
    not seen, and its cells are counted in the one before it, so read large row numbers with care."""
    bases = {bank: sorted(base for (b, base) in consumers() if b == bank) for bank in ("G", "GS")}
    arrays: dict[tuple[str, int], Array] = {}
    for (bank, address), (layer, value) in sorted(image().get(table, {}).items()):
        at = bisect_right(bases[bank], address) - 1
        base = bases[bank][at] if at >= 0 else address
        stride = consumers()[(bank, base)]["strides"].most_common(1)[0][0] if at >= 0 else 1
        array = arrays.setdefault((bank, base), Array(bank, base, stride))
        rid, column = divmod(address - base, stride)
        slot = array.columns.setdefault(column, {"ids": [], "layers": Counter(), "values": []})
        slot["ids"].append(rid)
        slot["layers"][layer] += 1
        slot["values"].append(value)
    return sorted(arrays.values(), key=lambda a: (a.bank, a.base))


def cleared_arrays(lines: list[str]) -> list[tuple[int, int]]:
    """[(base, cells per slot)] from the range clears an area table opens with: `copy-to-global BASE 0xc8` clears
    one cell a slot, and `mul (local-int 0x0) 0xc8 N` before a clear of that many cells N a slot. The clears are
    the script's first instructions."""
    arrays, count = [], None
    for line in lines:
        instruction = INSTRUCTION.match(line)
        if not instruction:
            continue
        if times := TIMES.match(instruction.group(1)):
            count = int(times.group(1), 16) * int(times.group(2), 16)
            continue
        clear = CLEAR.match(instruction.group(1))
        if not clear:
            break
        cells = count if clear.group(2).startswith("(") else int(clear.group(2), 16)
        if cells is None or cells % SLOTS:
            raise ValueError(f"a clear of {cells} cells is not a whole number of {SLOTS} slots: {line.strip()}")
        arrays.append((int(clear.group(1), 16), cells // SLOTS))
        count = None
    return arrays


def area_writes(lines: list[str]) -> list[tuple[int, str, int, str]]:
    """[(area, bank, address, operand)]: each constant write after an area's test (`eq … (global-int 0x9883) N`),
    up to the next area's."""
    writes, area = [], None
    for line in lines:
        instruction = INSTRUCTION.match(line)
        if not instruction:
            continue
        if test := AREA.match(instruction.group(1)):
            area = int(test.group(1), 16)
        elif area is not None and (write := CONSTANT.match(instruction.group(1))):
            writes.append((area, BANKS[write.group(2)], int(write.group(3), 16), write.group(4)))
    return writes


def area_table(table: str) -> list[dict]:
    """An area table's arrays with what the base script and its appends write into them: for each, its cells per
    slot and the areas, slots and values it gets. The integer arrays are the ones the script clears; a string
    array is the one the scripts index at or below the strings' cells."""
    listing = (DISASM / f"{table}.asm").read_text(encoding="utf-8").split("\n")
    arrays = [{"bank": "G", "base": base, "cells": cells, "areas": set(), "slots": set(), "values": []}
              for base, cells in cleared_arrays(listing)]
    indexed = sorted(base for (bank, base) in consumers() if bank == "GS")
    strings: dict[int, dict] = {}
    for layer in range(5):
        path = DISASM / (f"{table}.asm" if layer == 0 else f"${layer}${table}.asm")
        if not path.is_file():
            continue
        for area, bank, address, operand in area_writes(path.read_text(encoding="utf-8").split("\n")):
            if bank == "GS":
                at = bisect_right(indexed, address) - 1
                base = indexed[at] if at >= 0 and address - indexed[at] < SLOTS else address
                array = strings.setdefault(base, {"bank": "GS", "base": base, "cells": 1, "areas": set(),
                                                  "slots": set(), "values": []})
                value = operand.strip('"')
            else:
                array = next((a for a in arrays if a["base"] <= address < a["base"] + SLOTS * a["cells"]), None)
                if array is None:
                    continue
                value = int(operand, 0) if re.fullmatch(r"-?0x[0-9a-f]+|-?\d+", operand) else None
            array["areas"].add(area)
            array["slots"].add((address - array["base"]) // array["cells"])
            if value is not None:
                array["values"].append(value)
    return arrays + [strings[base] for base in sorted(strings)]


def area_report(table: str, as_markdown: bool) -> None:
    arrays = area_table(table)
    if as_markdown:
        print("| array | cells per slot | areas | slots | values | holds |")
        print("|---|---|---|---|---|---|")
    else:
        print(f"{table}: run for an area; {len(arrays)} arrays of {SLOTS} slots")
    for array in arrays:
        name = f"{array['bank']}[{array['base']:#x}]"
        slots = f"{min(array['slots'])}..{max(array['slots'])}" if array["slots"] else "none"
        values = describe(array["values"], array["bank"]) if array["values"] else "never written"
        if as_markdown:
            print(f"| `{name}` | {array['cells']} | {len(array['areas'])} | {slots} | {values.replace('|', '/')} | |")
        else:
            print(f"  {name} x{array['cells']}: {len(array['areas'])} areas, slots {slots}: {values}")


def report(table: str) -> None:
    if table in AREA_TABLES:
        area_report(table, as_markdown=False)
        return
    leader = LEADERS.get(table)
    cells = image().get(table, {})
    if leader is None:
        ints = sum(1 for (bank, _) in cells if bank == "G")
        print(f"{table}: no leader; {ints} int cells and {len(cells) - ints} string cells, by the arrays the scripts "
              f"index:")
        for array in by_consumers(table):
            ids = sorted({rid for slot in array.columns.values() for rid in slot["ids"]})
            written = sum(len(slot["ids"]) for slot in array.columns.values())
            values = [v for slot in array.columns.values() for v in slot["values"]]
            columns = "" if array.stride == 1 else f", columns {min(array.columns)}..{max(array.columns)}"
            print(f"  {array.bank}[{array.base:#x}] stride {array.stride}: {uses_text(array, None)}; rows "
                  f"{ids[0]}..{ids[-1]} ({len(ids)}){columns}, {written} cells: {describe(values, array.bank)}")
        return
    arrays, loose, shape = infer(table, leader)
    group = f", {leader.group} slots a row" if leader.group else ""
    print(f"{table}: {shape['records']} records by layer {shape['layers']}, ids {shape['ids']}, capacity "
          f"{leader.capacity}{group}; {len(arrays)} arrays, {len(loose)} cells outside them, "
          f"{shape['before_first_record']} writes before a layer's first record")
    for array in arrays:
        off = "" if array.on_grid else " † off the grid"
        print(f"  {array.bank}[{array.base:#x}] stride {array.stride}{off}: {uses_text(array, leader.group)}")
        for column, slot in sorted(array.columns.items()):
            layers = " ".join(f"L{k}:{v}" for k, v in sorted(slot["layers"].items()))
            print(f"    +{column:<3} {len(slot['ids']):5} records [{layers}]  {describe(slot['values'], array.bank)}")
    if loose:
        print("  outside:", ", ".join(f"{bank}[{address:#x}]" for bank, address in loose[:12]),
              "…" if len(loose) > 12 else "")


def markdown(table: str) -> None:
    """The table's arrays as a Markdown table, for docs/arterial-data-tables.md: one row per array, with an empty
    cell for what it holds. `†` marks an array off the table's grid."""
    if table in AREA_TABLES:
        area_report(table, as_markdown=True)
        return
    leader = LEADERS.get(table)
    arrays = infer(table, leader)[0] if leader else by_consumers(table)
    print("| array | cells per id | columns written | ids | values | holds |")
    print("|---|---|---|---|---|---|")
    for array in arrays:
        columns = sorted(array.columns)
        written = "" if array.stride == 1 else (f"{columns[0]}" if len(columns) == 1 else f"{columns[0]}..{columns[-1]}")
        ids = len({rid for slot in array.columns.values() for rid in slot["ids"]})
        values = describe([v for column in columns for v in array.columns[column]["values"]], array.bank)
        mark = "" if array.on_grid else " †"
        print(f"| `{array.bank}[{array.base:#x}]`{mark} | {array.stride} | {written} | {ids} | "
              f"{values.replace('|', '/')} | |")


def write(table: str) -> None:
    leader = LEADERS.get(table)
    if leader is None:
        return
    arrays, loose, shape = infer(table, leader)
    DATA.mkdir(parents=True, exist_ok=True)
    document = {
        "table": table, "leader": {"bank": leader.bank, "base": f"{leader.base:#x}", "capacity": leader.capacity,
                                   "group": leader.group, "also": [f"{base:#x}" for base in leader.also]},
        "records": shape,
        "arrays": [{
            "bank": a.bank, "base": f"{a.base:#x}", "stride": a.stride, "on_grid": a.on_grid,
            "consumers": {"scripts": use["scripts"], "strides": dict(use["strides"])}
            if (use := consumers().get((a.bank, a.base))) else None,
            "columns": {str(column): {"records": len(slot["ids"]), "layers": dict(slot["layers"]),
                                      "cells": dict(zip(map(str, slot["ids"]), slot["values"]))}
                        for column, slot in sorted(a.columns.items())},
        } for a in arrays],
        "outside": [{"bank": bank, "address": f"{address:#x}"} for bank, address in loose],
    }
    path = DATA / f"{table}.layout.json"
    path.write_text(json.dumps(document, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"wrote {path}")


def main(argv: list[str]) -> int:
    # --leader BANK:BASE:CAPACITY[:GROUP[:STRIDE]] tries a leader for the one table named, without editing
    # LEADERS (GROUP 0 for none).
    if "--leader" in argv:
        at = argv.index("--leader")
        bank, base, capacity, *more = argv[at + 1].split(":")
        argv = argv[:at] + argv[at + 2:]
        table = next(a for a in argv if not a.startswith("--"))
        LEADERS[table] = Leader(bank, int(base, 16), int(capacity, 0), int(more[0], 0) or None if more else None,
                                int(more[1], 0) if len(more) > 1 else 1)
    tables = [a for a in argv if not a.startswith("--")] or boot_tables()
    for table in tables:
        if "--write" in argv:
            write(table)
        elif "--markdown" in argv:
            markdown(table)
        else:
            report(table)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
