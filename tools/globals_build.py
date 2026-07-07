#!/usr/bin/env python3
"""Generator + linter for the living global-variable registry (vm-map/globals.toml).
  --build   merge curated over build/global-var-map.json -> build/globals.json + docs/global-reference.md
  --lint    run the linter, print errors/warnings, exit nonzero on errors
See docs/superpowers/specs/2026-07-07-globals-registry-and-story-flags-design.md."""
from __future__ import annotations
import os, sys, json, argparse, collections, tomllib
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths

TOML_DEFAULT = paths.VM_MAP / "globals.toml"
AUTO_MAP     = paths.BUILD / "global-var-map.json"

CATEGORIES = {"story-flag", "index-pointer", "data-table", "string-table",
              "ui-toggle", "choice-output", "counter", "unknown"}
SOURCES    = {"investigation", "harness", "inference", "auto-shape"}
CONFIDENCE = {"low": 1, "med": 2, "high": 3}


def _parse_addr(v) -> int:
    return int(v, 16) if isinstance(v, str) else int(v)


def load_toml(path) -> tuple[dict[int, dict], dict]:
    """Return ({addr:int -> entry-dict (with _addr set)}, meta-dict)."""
    data = tomllib.loads(Path(path).read_text(encoding="utf-8"))
    out: dict[int, dict] = {}
    for e in data.get("global", []):
        addr = _parse_addr(e["address"])
        e = dict(e)
        e["_addr"] = addr
        out[addr] = e
    return out, data.get("meta", {})


def lint(entries: dict[int, dict], all_addrs: set[int]) -> tuple[list[str], list[str]]:
    """Errors: bad vocabulary, auto-shape@high, dangling depends_on. Warnings: (reserved)."""
    errors: list[str] = []
    warnings: list[str] = []
    for addr, e in sorted(entries.items()):
        tag = f"0x{addr:x}"
        cat, src, conf = e.get("category"), e.get("source"), e.get("confidence")
        if cat not in CATEGORIES:
            errors.append(f"{tag}: bad category {cat!r}")
        if src not in SOURCES:
            errors.append(f"{tag}: bad source {src!r}")
        if conf not in CONFIDENCE:
            errors.append(f"{tag}: bad confidence {conf!r}")
        if src == "auto-shape" and conf == "high":
            errors.append(f"{tag}: auto-shape source may not claim high confidence")
        for dep in e.get("depends_on", []):
            if _parse_addr(dep) not in all_addrs:
                errors.append(f"{tag}: depends_on missing address {dep}")
    return errors, warnings


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--build", action="store_true")
    ap.add_argument("--lint", action="store_true")
    ap.add_argument("--toml", default=str(TOML_DEFAULT))
    args = ap.parse_args(argv)
    tp = Path(args.toml)
    if args.lint:
        entries, _ = load_toml(tp)
        errors, warnings = lint(entries, set(entries))
        for m in warnings: print("warn:", m)
        for m in errors: print("error:", m)
        print(f"lint: {len(errors)} errors, {len(warnings)} warnings")
        return 1 if errors else 0
    if args.build:
        return build(tp)          # implemented in Task 2
    ap.error("no action (expected --build/--lint)")


if __name__ == "__main__":
    sys.exit(main())
