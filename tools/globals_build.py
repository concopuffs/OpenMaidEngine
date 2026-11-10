#!/usr/bin/env python3
"""Generator + linter for the living per-game global-variable registries.
Each profile owns its own registry because global addresses are per game: Himegari uses
vm-map/globals.toml -> docs/global-reference.md; any other profile uses
vm-map/globals-<profile>.toml -> docs/global-reference-<profile>.md.
  --build   merge curated over build/games/<profile>/global-var-map.json -> build/games/<profile>/globals.json
            + the profile's generated reference doc
  --lint    run the linter, print errors/warnings, exit nonzero on errors
  --check   lint, then exit nonzero if the reference doc differs from what --build would write (needs the auto map)
A registry may also declare `[[table]]` entries: Open Maid Script tables (record views over parallel
arrays, design spec §5.3), emitted to the JSON's `tables` list and the reference doc.
See docs/superpowers/specs/2026-07-07-globals-registry-and-story-flags-design.md."""
from __future__ import annotations
import os, re, sys, json, math, argparse, collections, tomllib
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths

TOML_DEFAULT = paths.CONTEXT.globals_toml
REFERENCE_DOC = paths.CONTEXT.global_reference_doc
AUTO_MAP     = paths.BUILD / "global-var-map.json"

CATEGORIES = {"story-flag", "index-pointer", "data-table", "string-table",
              "ui-toggle", "choice-output", "counter", "unknown"}
SOURCES    = {"investigation", "harness", "inference", "auto-shape"}
CONFIDENCE = {"low": 1, "med": 2, "high": 3}


def _parse_addr(v) -> int:
    return int(v, 16) if isinstance(v, str) else int(v)


def bank_of(type_: str | None) -> str:
    """The VM global bank an entry lives in. Integer and string globals are separate banks with
    separate address spaces, so `G[0x0]` and `GS[0x0]` are different cells; the type implies the bank."""
    return "GS" if (type_ or "").startswith("string") else "G"


def cell_key(bank: str, addr: int) -> str:
    return f"{bank}:0x{addr:x}"


class Registry(dict):
    """Entries keyed by (bank, address). A bare int address looks up the integer bank first and then the
    string bank, which keeps address-keyed callers working where banks do not collide."""

    def _resolve(self, key):
        if isinstance(key, int):
            for bank in ("G", "GS"):
                if dict.__contains__(self, (bank, key)):
                    return (bank, key)
        return key

    def __getitem__(self, key):
        return dict.__getitem__(self, self._resolve(key))

    def __contains__(self, key):
        return dict.__contains__(self, self._resolve(key))

    def get(self, key, default=None):
        return dict.get(self, self._resolve(key), default)

    def addresses(self) -> set[int]:
        return {addr for _, addr in self}


def load_toml(path) -> tuple[Registry, dict]:
    """Return ({(bank, addr) -> entry-dict (with _addr/_bank set)}, meta-dict). A repeated (bank, addr)
    keeps the last entry and records `_duplicates` for the linter."""
    data = tomllib.loads(Path(path).read_text(encoding="utf-8"))
    out = Registry()
    for e in data.get("global", []):
        addr = _parse_addr(e["address"])
        e = dict(e)
        e["_addr"] = addr
        e["_bank"] = bank_of(e.get("type"))
        key = (e["_bank"], addr)
        if key in out:
            e["_duplicates"] = out[key].get("_duplicates", 1) + 1
        out[key] = e
    return out, data.get("meta", {})


def lint(entries: dict[int, dict], all_addrs: set[int]) -> tuple[list[str], list[str]]:
    """Errors: bad vocabulary, auto-shape@high, dangling depends_on. Warnings: overlapping arrays."""
    errors: list[str] = []
    warnings: list[str] = []
    all_addrs = {key[1] if isinstance(key, tuple) else key for key in all_addrs}
    for key, e in sorted(entries.items(), key=lambda item: str(item[0])):
        bank, addr = key if isinstance(key, tuple) else (bank_of(e.get("type")), key)
        tag = f"0x{addr:x}" if bank == "G" else cell_key(bank, addr)
        if e.get("_duplicates"):
            errors.append(f"{tag}: defined {e['_duplicates']} times")
        cat, src, conf = e.get("category"), e.get("source"), e.get("confidence")
        if cat not in CATEGORIES:
            errors.append(f"{tag}: bad category {cat!r}")
        if src not in SOURCES:
            errors.append(f"{tag}: bad source {src!r}")
        if conf not in CONFIDENCE:
            errors.append(f"{tag}: bad confidence {conf!r}")
        if src == "auto-shape" and conf == "high":
            errors.append(f"{tag}: auto-shape source may not claim high confidence")
        columns = e.get("columns", {})
        if not isinstance(columns, dict):
            errors.append(f"{tag}: columns must be a table")
        else:
            for column, name in columns.items():
                try:
                    column_index = int(column)
                except (TypeError, ValueError):
                    errors.append(f"{tag}: bad column index {column!r}")
                    continue
                if column_index < 0:
                    errors.append(f"{tag}: negative column index {column_index}")
                if not isinstance(name, str) or not name:
                    errors.append(f"{tag}: column {column_index} has no semantic name")
        for dep in e.get("depends_on", []):
            if _parse_addr(dep) not in all_addrs:
                errors.append(f"{tag}: depends_on missing address {dep}")
    warnings.extend(overlap_warnings(entries))
    return errors, warnings


def dims_of(type_: str | None) -> list[int] | None:
    """The dimensions of a fully sized array (`int[1000][14]` -> [1000, 14]); None for a scalar, an open
    leading dimension (`int[][27]`) or a shape the OMS module loader would not read."""
    m = re.fullmatch(r"(?:int|string|float)((?:\[\d*\])+)", type_ or "")
    if not m:
        return None
    dims = re.findall(r"\[(\d*)\]", m.group(1))
    if "" in dims:
        return None
    return [int(d) for d in dims]


def extent_of(type_: str | None) -> int | None:
    """Cells a fully sized array covers (`int[1000][14]` -> 14000), or None (see dims_of)."""
    dims = dims_of(type_)
    return None if dims is None else math.prod(dims)


IDENTIFIER = re.compile(r"[a-z_][a-z0-9_]*")


def load_tables(path) -> list[dict]:
    """The registry's `[[table]]` entries, in order: Open Maid Script tables (design spec §5.3). Each names
    the parallel arrays that hold one kind of record; a field maps to a curated global by name."""
    data = tomllib.loads(Path(path).read_text(encoding="utf-8"))
    return [dict(t) for t in data.get("table", [])]


def lint_tables(tables: list[dict], entries: dict) -> list[str]:
    """Errors: a table or field name that is not a lowercase identifier, a repeated table, a table named like
    a global, and a field whose global is missing or is not a fully sized 1-D or 2-D array of `records` entries
    or rows."""
    errors: list[str] = []
    by_name: dict[str, dict] = {}
    for e in entries.values():
        if e.get("name"):
            by_name.setdefault(e["name"], e)
    seen: set[str] = set()
    for t in tables:
        name = t.get("name")
        tag = f"table {name!r}"
        if not isinstance(name, str) or not IDENTIFIER.fullmatch(name):
            errors.append(f"{tag}: name must be a lowercase identifier")
            continue
        if name in seen:
            errors.append(f"{tag}: defined twice")
        seen.add(name)
        if name in by_name:
            errors.append(f"{tag}: also names a global")
        records = t.get("records")
        if not isinstance(records, int) or isinstance(records, bool) or records <= 0:
            errors.append(f"{tag}: records must be a positive integer")
            continue
        fields = t.get("fields")
        if not isinstance(fields, dict) or not fields:
            errors.append(f"{tag}: needs a [table.fields] map")
            continue
        for field, global_name in fields.items():
            if not IDENTIFIER.fullmatch(field):
                errors.append(f"{tag}: field {field!r} must be a lowercase identifier")
            e = by_name.get(global_name) if isinstance(global_name, str) else None
            if e is None:
                errors.append(f"{tag}: field {field} names unknown global {global_name!r}")
                continue
            dims = dims_of(e.get("type"))
            if dims is None or len(dims) not in (1, 2) or dims[0] != records:
                errors.append(f"{tag}: field {field} needs a fully sized [{records}] or [{records}][n] array; "
                              f"{global_name} is {e.get('type')!r}")
    return errors


def overlap_warnings(entries: dict) -> list[str]:
    """Fully sized arrays of one bank whose cells overlap. The OMS decompiler names a cell by the smallest
    array containing it (spec §11.3), so a size error here misnames cells, though it never changes bytes."""
    spans = collections.defaultdict(list)
    for key, e in entries.items():
        bank, addr = key if isinstance(key, tuple) else (bank_of(e.get("type")), key)
        if (extent := extent_of(e.get("type"))) is not None:
            spans[bank].append((addr, addr + extent, e.get("name") or "?"))
    warnings = []
    for bank, arrays in sorted(spans.items()):
        arrays.sort()
        for i, (start, end, name) in enumerate(arrays):
            for other_start, other_end, other in arrays[i + 1:]:
                if other_start >= end:
                    break
                warnings.append(f"{cell_key(bank, start)}: array {name} [0x{start:x}, 0x{end:x}) overlaps "
                                f"{other} [0x{other_start:x}, 0x{other_end:x})")
    return warnings


GEN_JSON_META = {"generated_from": f"vm-map/{TOML_DEFAULT.name}",
                 "note": "DO NOT EDIT -- generated by tools/globals_build.py --build"}


def load_auto(path) -> dict:
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except Exception:
        return {}


def _auto_category(e: dict) -> str:
    """Best-effort category for an auto shape-map entry (unreliable; curation overrides)."""
    kind = e.get("kind") or ""
    if kind == "record-table":
        return "data-table"
    if kind == "array-1d":
        return "data-table"
    lbl = e.get("label") or ""
    if lbl.startswith("string-table"):
        return "string-table"
    return "unknown"


def merge(curated: dict, auto: dict) -> Registry:
    out = Registry()
    for addr_s, e in auto.get("globals", {}).items():
        if not e.get("label"):
            continue
        addr = int(addr_s, 16)
        bank = bank_of(e.get("type"))
        out[(bank, addr)] = {"address": f"0x{addr:x}", "bank": bank, "name": None,
                             "category": _auto_category(e),
                             "type": e.get("type"), "value_domain": None, "usage": e.get("label"),
                             "source": "auto-shape", "confidence": e.get("confidence") or "low",
                             "depends_on": [], "provenance": "auto"}
    for key, e in curated.items():
        bank, addr = key if isinstance(key, tuple) else (bank_of(e.get("type")), key)
        out[(bank, addr)] = {"address": f"0x{addr:x}", "bank": bank, "name": e.get("name"),
                     "category": e.get("category", "unknown"), "type": e.get("type"),
                     "value_domain": e.get("value_domain"), "usage": e.get("usage", ""),
                     "source": e.get("source", "inference"), "confidence": e.get("confidence", "low"),
                     "depends_on": [f"0x{_parse_addr(d):x}" for d in e.get("depends_on", [])],
                     "provenance": "curated"}
        if e.get("columns"):
            out[(bank, addr)]["columns"] = {
                str(column): name for column, name in e["columns"].items()
            }
    return out


def emit_json(merged: Registry, meta: dict, tables: list[dict] = ()) -> str:
    """`globals` is the legacy address-keyed view (the integer bank wins where a string global shares its
    address); `cells` keys every entry by bank and address (`G:0x0`, `GS:0x0`); `tables` lists the OMS
    tables in declaration order."""
    ordered = sorted(merged.items(), key=lambda item: (item[0][1], item[0][0] != "G"))
    legacy: dict[str, dict] = {}
    for _, e in ordered:
        legacy.setdefault(e["address"], e)
    out = {"meta": {**GEN_JSON_META, **meta},
           "globals": legacy,
           "cells": {cell_key(bank, addr): e for (bank, addr), e in sorted(merged.items())},
           "tables": [{"name": t["name"], "records": t["records"], "usage": t.get("usage", ""),
                       "fields": dict(t["fields"])} for t in tables]}
    return json.dumps(out, ensure_ascii=False, indent=2) + "\n"


def emit_reference_md(merged: Registry, tables: list[dict] = ()) -> str:
    source = f"vm-map/{TOML_DEFAULT.name}"
    L = [f"<!-- DO NOT EDIT -- generated from {source} by tools/globals_build.py --build -->",
         f"# Global Variable Reference — {paths.CONTEXT.profile_id} (generated)", "",
         f"{len(merged)} globals ({sum(1 for e in merged.values() if e['provenance']=='curated')} curated, "
         f"{sum(1 for e in merged.values() if e['provenance']=='auto')} auto shape-inferred). "
         f"Source of truth: `{source}`.", ""]
    if tables:
        by_name = {e["name"]: e for e in merged.values() if e.get("name")}
        L += ["## Tables", "",
              "Open Maid Script tables (design spec §5.3): `table[record].field` is the field's array element.", ""]
        for t in tables:
            L += [f"### {t['name']}[{t['records']}]", ""]
            if t.get("usage"):
                L += [t["usage"], ""]
            L += ["| field | global | address | type |", "|---|---|---|---|"]
            for field, global_name in t["fields"].items():
                e = by_name[global_name]
                address = e["address"] if e["bank"] == "G" else f"{e['bank']} {e['address']}"
                L.append(f"| {field} | {global_name} | `{address}` | `{e['type']}` |")
            L.append("")
    by_cat = collections.defaultdict(list)
    for key, e in merged.items():
        by_cat[e["category"]].append(key)
    for cat in sorted(by_cat):
        L += [f"## {cat}", "", "| address | name | conf | source | usage |", "|---|---|---|---|---|"]
        # curated first, then by address
        for key in sorted(by_cat[cat], key=lambda k: (merged[k]["provenance"] != "curated", k[1], k[0])):
            e = merged[key]
            name = e["name"] or "—"
            usage = (e["usage"] or "").replace("|", "\\|").replace("\n", " ")
            if columns := e.get("columns"):
                mapping = ", ".join(
                    f"{column}={column_name}"
                    for column, column_name in sorted(
                        columns.items(), key=lambda item: int(item[0])
                    )
                )
                usage += f" Columns: {mapping}."
            address = e["address"] if e["bank"] == "G" else f"{e['bank']} {e['address']}"
            L.append(f"| `{address}` | {name} | {e['confidence']} | {e['source']} | {usage} |")
        L.append("")
    return "\n".join(L).rstrip("\n") + "\n"


def _linted_merge(tp: Path, auto_map: Path) -> tuple[Registry, dict, list[dict]] | None:
    """Lint the registry against itself and the auto map, then merge; None (errors printed) when it does not lint."""
    curated, meta = load_toml(tp)
    auto = load_auto(auto_map)
    all_addrs = curated.addresses() | {int(a, 16) for a in auto.get("globals", {})}
    errors, warnings = lint(curated, all_addrs)
    tables = load_tables(tp)
    errors += lint_tables(tables, curated)
    for m in warnings: print("warn:", m)
    if errors:
        for m in errors: print("error:", m)
        return None
    return merge(curated, auto), meta, tables


def _shown(path: Path) -> str:
    """A path as messages show it: repository-relative when it lies in the repository."""
    return path.relative_to(paths.REPO).as_posix() if path.is_relative_to(paths.REPO) else str(path)


def check(tp: Path, auto_map: Path = AUTO_MAP, reference_doc: Path = REFERENCE_DOC) -> int:
    """Lint, then exit nonzero if the tracked reference doc differs from what --build would write. The doc merges
    the auto map, a workspace-derived input (global_map.py), so a missing map is an error, not an empty merge."""
    if not Path(auto_map).is_file():
        print(f"error: auto map {auto_map} not found; build it with global_map.py (the reference doc merges it)")
        return 1
    prepared = _linted_merge(tp, auto_map)
    if prepared is None:
        return 1
    merged, _, tables = prepared
    expected = emit_reference_md(merged, tables)
    current = reference_doc.read_text(encoding="utf-8") if reference_doc.is_file() else None
    if current != expected:
        print(f"stale: {_shown(reference_doc)}; run globals_build.py "
              f"--profile {paths.CONTEXT.profile_id} --build")
        return 1
    print(f"check: {_shown(reference_doc)} is current ({len(merged)} globals)")
    return 0


def build(tp: Path, auto_map: Path = AUTO_MAP) -> int:
    prepared = _linted_merge(tp, auto_map)
    if prepared is None:
        return 1
    merged, meta, tables = prepared
    paths.BUILD.mkdir(parents=True, exist_ok=True)
    (paths.BUILD / "globals.json").write_text(emit_json(merged, meta, tables), encoding="utf-8", newline="\n")
    REFERENCE_DOC.write_text(emit_reference_md(merged, tables), encoding="utf-8", newline="\n")
    print(f"build: {len(merged)} globals, {len(tables)} tables -> "
          f"{(paths.BUILD / 'globals.json').relative_to(paths.REPO)}, {REFERENCE_DOC.relative_to(paths.REPO)}")
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--build", action="store_true")
    ap.add_argument("--lint", action="store_true")
    ap.add_argument("--check", action="store_true",
                    help="lint, then fail if the profile's reference doc differs from what --build would write")
    ap.add_argument("--toml", default=str(TOML_DEFAULT))
    ap.add_argument("--auto-map", default=str(AUTO_MAP),
                    help="profile-specific global-var-map.json input")
    args = ap.parse_args(argv)
    tp = Path(args.toml)
    if args.lint:
        entries, _ = load_toml(tp)
        errors, warnings = lint(entries, entries.addresses())
        errors += lint_tables(load_tables(tp), entries)
        for m in warnings: print("warn:", m)
        for m in errors: print("error:", m)
        print(f"lint: {len(errors)} errors, {len(warnings)} warnings")
        return 1 if errors else 0
    if args.check:
        return check(tp, Path(args.auto_map))
    if args.build:
        return build(tp, Path(args.auto_map))
    ap.error("no action (expected --build/--check/--lint)")


if __name__ == "__main__":
    sys.exit(main())
