"""Plain tests for page_map_compare.py."""
import contextlib
import io
import json
import sys
import tempfile
from pathlib import Path

import page_map_compare
from page_map_compare import flat, joined, rows

FAILS = []


def check(condition, message):
    (FAILS.append(message) or print("FAIL:", message)) if not condition else print("ok:", message)


def page(number, dialogue=None, breaks=(), presented=None, cut=False, offset="0x10"):
    record = {"root_scene": "SC0000", "page": number, "wait_script": "SC0000", "wait_offset": offset,
              "dialogue": dialogue, "breaks": list(breaks)}
    if presented is not None:
        record["presented"] = presented
    if cut:
        record["cut"] = True
    return record


def test_joined():
    check(joined(page(1, "It was a long line  \nthat wrapped.\nA new line.", ["wrap", "break"]))
          == "It was a long line that wrapped.\nA new line.",
          "a wrap joins with one space after the line's trailing spaces, a break stays a newline")
    check(joined(page(1, "王政が\n崩壊した", ["wrap"])) == "王政が崩壊した", "two wide characters join with nothing")
    check(joined(page(1, None)) is None, "a page without text has none")
    try:
        joined(page(1, "a\nb", []))
        raised = False
    except SystemExit:
        raised = True
    check(raised, "a break count that does not match the newlines is refused")


def test_normalized():
    check(flat("  Hello,\n  world.  ") == "Hello, world.", "whitespace collapses to one space")
    check(flat("王政 が\n崩壊") == "王政が崩壊", "no space is kept between two wide characters")
    check(rows("Line one  \nLine two\n") == "Line one\nLine two", "rows drop trailing spaces and blank lines")


def run(a, b):
    with tempfile.TemporaryDirectory() as directory:
        paths = []
        for name, records in (("a.jsonl", a), ("b.jsonl", b)):
            path = Path(directory) / name
            path.write_text("".join(json.dumps(r, ensure_ascii=False) + "\n" for r in records), encoding="utf-8")
            paths.append(str(path))
        output = io.StringIO()
        argv = sys.argv
        sys.argv = ["page_map_compare.py", *paths, "--show", "5"]
        try:
            with contextlib.redirect_stdout(output):
                status = page_map_compare.main()
        finally:
            sys.argv = argv
        return status, output.getvalue()


def test_main():
    a = [page(1, "Wil\nIt was a long line\nthat wrapped.", ["break", "wrap"], offset="0x20"),
         page(2, "A short page.")]
    b = [page(1, "ウィル\n長い行", ["break"], "Wil\nIt was a long line that wrapped."),
         page(2, "短い", [], "A short page.", cut=True)]
    status, output = run(a, b)
    check(status == 0 and "2 show the same text, 0 differ" in output and "1 pages of run B stopped" in output,
          "pages whose presented text matches pass, offsets aside, and a cut page is listed")

    b[1]["presented"] = "Another page."
    status, output = run(a, b)
    check(status == 1 and "1 differ" in output and "Another page." in output, "a page whose text differs fails")

    b[1]["presented"] = "A short\npage."
    status, output = run(a, b)
    check(status == 0 and "1 pages with the same text break their lines differently" in output,
          "a page that breaks its lines elsewhere is reviewed, not failed")

    status, output = run(a, b[:1])
    check(status == 1 and "2 and 1 pages" in output, "runs with different page counts fail")

    try:
        run(a, [page(1, "ウィル")])
        raised = False
    except SystemExit:
        raised = True
    check(raised, "a run B without presented text is refused")


def main():
    test_joined()
    test_normalized()
    test_main()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
