"""Unit tests for the EngineCtx builder (tools/engine_ctx_build.py).

Run: py -3.11 -X utf8 tools/test_engine_ctx.py   (plain runner, no pytest).
"""
import sys

from engine_ctx_build import load, lint, emit_json

FAILS = []


def check(cond, msg):
    if not cond:
        FAILS.append(msg)
        print("FAIL:", msg)
    else:
        print("ok:", msg)


GOOD = ('[meta]\nstruct_name="EngineCtx"\nsize=0x1000\n'
        '[[field]]\noffset=0x10\nname="a"\ntype="int"\nnote="x"\n'
        '[[field]]\noffset=0x20\nname="b"\ntype="void*"\nnote="y"\n')


def test_load_and_emit():
    j = emit_json(load(GOOD))
    check(j["fields"]["0x10"]["name"] == "a" and j["meta"]["struct_name"] == "EngineCtx",
          "emit_json keys fields by hex offset + carries meta")


def test_lint_clean():
    check(lint(load(GOOD)) == [], "clean model lints with no errors")


def test_lint_catches_overlap():
    bad = ('[meta]\nstruct_name="E"\nsize=0x1000\n'
           '[[field]]\noffset=0x10\nname="a"\ntype="int"\nnote=""\n'
           '[[field]]\noffset=0x12\nname="b"\ntype="int"\nnote=""\n')   # 0x10+4 > 0x12 -> overlap
    check(any("overlap" in e.lower() for e in lint(load(bad))), "lint flags overlapping fields")


def test_lint_catches_oob_and_dupname():
    bad = ('[meta]\nstruct_name="E"\nsize=0x14\n'
           '[[field]]\noffset=0x10\nname="a"\ntype="int"\nnote=""\n'
           '[[field]]\noffset=0x40\nname="a"\ntype="int"\nnote=""\n')   # 0x40 > size AND dup name
    errs = lint(load(bad))
    check(any("out of bounds" in e.lower() for e in errs) and any("duplicate" in e.lower() for e in errs),
          "lint flags out-of-bounds offset and duplicate name")


def main():
    test_load_and_emit()
    test_lint_clean()
    test_lint_catches_overlap()
    test_lint_catches_oob_and_dupname()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
