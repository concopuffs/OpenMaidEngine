"""Compare two port runs' page maps page by page: the localization run comparison (gate 11).

Usage:
  py -3.11 -X utf8 tools/page_map_compare.py A.jsonl B.jsonl [--report OUT.json] [--show N]

Run A is an install whose scripts were translated in place, with no catalog; run B is the clean install with the
catalog imported from it (`omp text import-scripts`), driven over the same scenarios with the same `rand-seed`. The
port writes each run's page map (`--page-map`); see docs/tools-reference.md, "Runtime page locator".

Page N of run A must show the text page N of run B presents (localization spec §18.1):
- required: A's `dialogue`, its wraps joined (one space, none between two wide characters) and its deliberate breaks
  kept, equals B's `presented` once each run of whitespace is one space (none between two wide characters);
- reviewed: the same comparison with the line breaks kept, which shows where the page map's break classes and the
  catalog's disagree, and B's pages whose drawn run stopped at its layout's bottom (`cut`).
Offsets are not compared: the translated scripts carry extra instructions. The exit status is 1 when a required check
fails or the runs have different page counts.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


def load(path: Path) -> list[dict]:
    records = []
    with path.open(encoding="utf-8") as f:
        for number, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                records.append(json.loads(line))
            except json.JSONDecodeError as exc:
                raise SystemExit(f"{path}:{number}: invalid JSON: {exc}")
    return records


def wide(c: str) -> bool:
    """A double-byte character of CP932, as the importer and the page map count it."""
    code = ord(c)
    return code >= 0x80 and not 0xFF61 <= code <= 0xFF9F


def joined(record: dict) -> str | None:
    """Run A's page as the catalog holds it: wraps joined, deliberate breaks kept as newlines."""
    dialogue = record.get("dialogue")
    if dialogue is None:
        return None
    lines = dialogue.split("\n")
    breaks = record.get("breaks") or []
    if len(breaks) != len(lines) - 1:
        raise SystemExit(f"page {record.get('page')}: {len(lines) - 1} line breaks but {len(breaks)} break classes")
    text = lines[0]
    for kind, line in zip(breaks, lines[1:]):
        text = text.rstrip(" \t")
        if kind == "wrap":
            text += "" if text and line and wide(text[-1]) and wide(line[0]) else " "
        else:
            text += "\n"
        text += line
    return text.strip(" \t\n")


def flat(text: str | None) -> str:
    """Each run of whitespace one space, and none between two wide characters."""
    text = re.sub(r"\s+", " ", text or "").strip()
    return re.sub(r"(?<=[^\x00-\x7f｡-ﾟ]) (?=[^\x00-\x7f｡-ﾟ])", "", text)


def rows(text: str | None) -> str:
    """Line breaks kept: each line's trailing spaces dropped, leading and trailing blank lines too."""
    return "\n".join(line.rstrip(" \t") for line in (text or "").split("\n")).strip("\n")


def where(record: dict) -> str:
    return f"{record.get('wait_script')}@{record.get('wait_offset')}"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("a", type=Path, help="run A's page map: the translated install, no catalog")
    parser.add_argument("b", type=Path, help="run B's page map: the clean install with the imported catalog")
    parser.add_argument("--report", type=Path, help="write every page's result as JSON")
    parser.add_argument("--show", type=int, default=10, help="mismatches to print per check (default 10)")
    args = parser.parse_args()

    run_a, run_b = load(args.a), load(args.b)
    if run_b and not any("presented" in record for record in run_b):
        raise SystemExit(f"{args.b} has no presented text: run B needs a language active")
    pages = []
    for index, (a, b) in enumerate(zip(run_a, run_b)):
        expected = joined(a)
        presented = b.get("presented")
        if presented is None and b.get("dialogue") is not None:
            presented = b.get("dialogue")   # no language on this page's runs: what was drawn is the source
        pages.append({
            "page": a.get("page"),
            "b_page": b.get("page"),
            "a_wait": where(a),
            "b_wait": where(b),
            "a_text": expected,
            "b_text": presented,
            "text": flat(expected) == flat(presented),
            "breaks": rows(expected) == rows(presented),
            "cut": bool(b.get("cut")),
        })

    text_mismatches = [page for page in pages if not page["text"]]
    break_mismatches = [page for page in pages if page["text"] and not page["breaks"]]
    cut = [page for page in pages if page["cut"]]
    with_text = sum(1 for page in pages if flat(page["a_text"]) or flat(page["b_text"]))
    print(f"run A {args.a}: {len(run_a)} pages; run B {args.b}: {len(run_b)} pages")
    print(f"compared {len(pages)} pages ({with_text} with text): {len(pages) - len(text_mismatches)} show the same "
          f"text, {len(text_mismatches)} differ")
    print(f"reviewed: {len(break_mismatches)} pages with the same text break their lines differently; "
          f"{len(cut)} pages of run B stopped a run at its layout's bottom")

    def show(title: str, items: list[dict]) -> None:
        if not items or args.show <= 0:
            return
        print(f"\n{title}:")
        for page in items[:args.show]:
            print(f"  page {page['page']} (A {page['a_wait']}, B {page['b_wait']})")
            print(f"    A: {json.dumps(page['a_text'], ensure_ascii=False)}")
            print(f"    B: {json.dumps(page['b_text'], ensure_ascii=False)}")
        if len(items) > args.show:
            print(f"  ... and {len(items) - args.show} more")

    show("text differs", text_mismatches)
    show("line breaks differ", break_mismatches)
    show("cut at the layout's bottom", cut)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps({
            "a": str(args.a), "b": str(args.b), "a_pages": len(run_a), "b_pages": len(run_b), "pages": pages,
        }, ensure_ascii=False, indent=1), encoding="utf-8")
        print(f"\nreport: {args.report}")
    failed = bool(text_mismatches) or len(run_a) != len(run_b)
    if len(run_a) != len(run_b):
        print(f"\nFAIL: the runs have {len(run_a)} and {len(run_b)} pages")
    print("FAIL" if failed else "PASS")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
