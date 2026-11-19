#!/usr/bin/env python3
"""Tests for tools/decomp_pair_diff.py. Run: py -3.11 -X utf8 tools/test_decomp_pair_diff.py"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import decomp_pair_diff as PD

FAILS = []


def check(cond, msg):
    print(("  ok  " if cond else " FAIL ") + msg)
    if not cond:
        FAILS.append(msg)


BUILD_A = """// ===== 0x455130 text_history_clear
/* a plate comment
   over two lines */
void __fastcall text_history_clear(int param_1)
{
  int iVar1;
  undefined4 local_8 [4];

  iVar1 = *(int *)(param_1 + 0xd28);
  FUN_00453180(iVar1,*(int *)(param_1 + 0x9f700));
  if (iVar1 == 3) {
    text_history_records_resize(0);
  }
  return;
}
// ===== 0x4230c0 op_0x258
void __fastcall op_0x258(int param_1)
{
  *(undefined4 *)(param_1 + 0x720) = 0;
  return;
}
"""

BUILD_B = """// ===== 0x45a0b0 FUN_0045a0b0
void __fastcall FUN_0045a0b0(int param_1)
{
  int iVar3;

  iVar3 = *(int *)(param_1 + 0xd28);
  FUN_00458e30(iVar3,*(int *)(param_1 + 0xa4b98));
  if (iVar3 == 4) {
    text_history_records_resize(0);
  }
  return;
}
// ===== 0x424a60 op_0x258
void __fastcall op_0x258(int param_1)
{
  *(undefined4 *)(param_1 + 0x720) = 0;
  return;
}
"""


def test_mask():
    check(PD.mask("FUN_00453180(iVar1,local_8[2]);") == "FUN_X(iVar,local[2]);",
          "a default name loses its address and a variable its number")
    check(PD.mask("*(int *)(param_1 + 0x9f700) = 0x1c;") == "*(int *)(param + 0xN) = 0x1c;",
          "a context offset is masked and a small constant kept")
    check(PD.mask("s_set_SaveWinPos_005a5294") == "s_set_SaveWinPos_X", "a string label keeps its text")
    check(PD.mask("text_history_clear();") == "text_history_clear();", "a given name is kept")


def test_pairs():
    a, b = PD.functions(BUILD_A), PD.functions(BUILD_B)
    check([head for head, _ in a] == ["0x455130 text_history_clear", "0x4230c0 op_0x258"], "headings are read in order")
    check(not any("plate" in line or line.startswith("int iVar") or line.startswith("undefined4 local")
                  for line in a[0][1]), "plate comments and plain declarations are dropped")
    changed, diff = PD.compare(a[0][1], b[0][1])
    check(changed == 4, f"the renamed function and the changed compare are the four changed lines (got {changed})")
    check(any(line.startswith("-if (iVar == 3)") for line in diff) and any(line.startswith("+if (iVar == 4)") for line in diff),
          "a changed small constant shows")
    check(not any("0xN" in line and line.startswith(("+", "-")) for line in diff), "a moved context offset does not")
    check(PD.compare(a[1][1], b[1][1])[0] == 0, "the same code at another address has no changed line")


if __name__ == "__main__":
    test_mask()
    test_pairs()
    print(f"\n{'FAILED' if FAILS else 'OK'}: {len(FAILS)} failure(s)")
    sys.exit(1 if FAILS else 0)
