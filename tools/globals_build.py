#!/usr/bin/env python3
"""Generator + linter for the living per-game global-variable registries.
Each profile owns its own registry because global addresses are per game: Himegari uses
vm-map/globals.toml -> docs/global-reference.md; any other profile uses
vm-map/globals-<profile>.toml -> docs/global-reference-<profile>.md.
  --build   merge curated over build/games/<profile>/global-var-map.json -> build/games/<profile>/globals.json
            + the profile's generated reference doc
  --lint    run the linter, print errors/warnings, exit nonzero on errors
See docs/superpowers/specs/2026-07-07-globals-registry-and-story-flags-design.md."""
from __future__ import annotations
import os, sys, json, argparse, collections, tomllib
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
    """Errors: bad vocabulary, auto-shape@high, dangling depends_on. Warnings: (reserved)."""
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
    return errors, warnings


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


def emit_json(merged: Registry, meta: dict) -> str:
    """`globals` is the legacy address-keyed view (the integer bank wins where a string global shares its
    address); `cells` keys every entry by bank and address (`G:0x0`, `GS:0x0`)."""
    ordered = sorted(merged.items(), key=lambda item: (item[0][1], item[0][0] != "G"))
    legacy: dict[str, dict] = {}
    for _, e in ordered:
        legacy.setdefault(e["address"], e)
    out = {"meta": {**GEN_JSON_META, **meta},
           "globals": legacy,
           "cells": {cell_key(bank, addr): e for (bank, addr), e in sorted(merged.items())}}
    return json.dumps(out, ensure_ascii=False, indent=2) + "\n"


def emit_reference_md(merged: Registry) -> str:
    source = f"vm-map/{TOML_DEFAULT.name}"
    L = [f"<!-- DO NOT EDIT -- generated from {source} by tools/globals_build.py --build -->",
         f"# Global Variable Reference — {paths.CONTEXT.profile_id} (generated)", "",
         f"{len(merged)} globals ({sum(1 for e in merged.values() if e['provenance']=='curated')} curated, "
         f"{sum(1 for e in merged.values() if e['provenance']=='auto')} auto shape-inferred). "
         f"Source of truth: `{source}`.", ""]
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


def build(tp: Path, auto_map: Path = AUTO_MAP) -> int:
    curated, meta = load_toml(tp)
    auto = load_auto(auto_map)
    all_addrs = curated.addresses() | {int(a, 16) for a in auto.get("globals", {})}
    errors, warnings = lint(curated, all_addrs)
    for m in warnings: print("warn:", m)
    if errors:
        for m in errors: print("error:", m)
        return 1
    merged = merge(curated, auto)
    paths.BUILD.mkdir(parents=True, exist_ok=True)
    (paths.BUILD / "globals.json").write_text(emit_json(merged, meta), encoding="utf-8", newline="\n")
    REFERENCE_DOC.write_text(emit_reference_md(merged), encoding="utf-8", newline="\n")
    print(f"build: {len(merged)} globals -> {(paths.BUILD / 'globals.json').relative_to(paths.REPO)}, "
          f"{REFERENCE_DOC.relative_to(paths.REPO)}")
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--build", action="store_true")
    ap.add_argument("--lint", action="store_true")
    ap.add_argument("--toml", default=str(TOML_DEFAULT))
    ap.add_argument("--auto-map", default=str(AUTO_MAP),
                    help="profile-specific global-var-map.json input")
    args = ap.parse_args(argv)
    tp = Path(args.toml)
    if args.lint:
        entries, _ = load_toml(tp)
        errors, warnings = lint(entries, entries.addresses())
        for m in warnings: print("warn:", m)
        for m in errors: print("error:", m)
        print(f"lint: {len(errors)} errors, {len(warnings)} warnings")
        return 1 if errors else 0
    if args.build:
        return build(tp, Path(args.auto_map))
    ap.error("no action (expected --build/--lint)")


if __name__ == "__main__":
    sys.exit(main())
