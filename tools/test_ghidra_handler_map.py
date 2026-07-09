"""Unit tests for the FUN_00413860 dispatch-table parser (tools/ghidra_handler_map.py).

Run: py -3.11 -X utf8 tools/test_ghidra_handler_map.py   (plain runner, no pytest dependency).
"""
import sys
from ghidra_handler_map import parse_overrides

FAILS = []


def check(cond, msg):
    if not cond:
        FAILS.append(msg)
        print("FAIL:", msg)
    else:
        print("ok:", msg)


def test_parses_known_override_anchors():
    # Lines in the exact shape captured from disassemble_function(0x413860).
    # disp = 0x9b24c + op*4  =>  0x9b8fc = 0x9b24c + 0x1ac*4 (op 0x1ac), 0x9b56c = op 0xc8.
    lines = [
        "0041476a: MOV dword ptr [ESI + 0x9b8fc],0x427fb0",   # op 0x1ac
        "004148e6: MOV dword ptr [ESI + 0x9b56c],0x420ec0",   # op 0xc8
        "00414738: MOV dword ptr [ESI + 0x9b8d4],0x42d360",   # op 0x1a2
        "00414d1e: MOV dword ptr [ESI + 0x9baa0],0x42a0b0",   # op 0x215
    ]
    m = parse_overrides(lines)
    check(m.get(0x1ac) == 0x427fb0, "anchor op 0x1ac -> 0x427fb0")
    check(m.get(0xc8) == 0x420ec0, "anchor op 0xc8 -> 0x420ec0")
    check(m.get(0x1a2) == 0x42d360, "anchor op 0x1a2 -> 0x42d360")
    check(m.get(0x215) == 0x42a0b0, "anchor op 0x215 -> 0x42a0b0")


def test_excludes_default_handler_and_out_of_window():
    lines = [
        "MOV EAX,0x4162b0",                                  # STOSD seed, not a store -> ignored
        "MOV dword ptr [ESI + 0x9b24c],0x4162b0",            # op 0x0 default handler -> excluded
        "MOV dword ptr [ESI + 0x5f2e8],0x570410",            # disp below table window -> ignored
        "MOV dword ptr [ESI + 0x992c0],EAX",                 # register source, no immediate -> ignored
        "MOV dword ptr [ESI],0x570fec",                      # no displacement -> ignored
    ]
    check(parse_overrides(lines) == {}, "default handler + out-of-window + non-immediate all excluded")


def test_ignores_misaligned_displacement():
    # A store into the table window but not on a 4-byte boundary is not a real slot.
    lines = ["MOV dword ptr [ESI + 0x9b24e],0x401234"]       # 0x9b24e-0x9b24c = 2, not %4
    check(parse_overrides(lines) == {}, "misaligned displacement in window excluded")


def main():
    test_parses_known_override_anchors()
    test_excludes_default_handler_and_out_of_window()
    test_ignores_misaligned_displacement()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0


if __name__ == "__main__":
    sys.exit(main())
