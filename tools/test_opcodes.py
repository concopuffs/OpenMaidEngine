#!/usr/bin/env python3
"""Standalone tests for the opcode reference tooling. Run: py -3.11 -X utf8 tools/test_opcodes.py"""
import os, sys, tempfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opcodes_model as M

FAILS = []
def check(cond, msg):
    print(("  ok  " if cond else " FAIL ") + msg)
    if not cond: FAILS.append(msg)

FIXTURE = '''
[meta]
opcodes_used_by_himegari = 2
[[opcode]]
op = 0x90
label = "u0041BEB0"
argc = 7
code_target_args = [5, 6, 7]
[opcode.semantics]
name = "hotspot-branch"
category = "input"
summary = "cursor hotspot hit-test"
noop_headless = true
source = "investigation"
confidence = "high"
depends_on = [0x1f4]
evidence = "301/301 uniform"
[[opcode.semantics.args]]
i = 1
role = "x"
observed_types = ["imm"]
[[opcode]]
op = 0x1f4
label = "u004160D0"
argc = 0
[opcode.semantics]
name = "stmt-begin"
category = "marker"
source = "investigation"
confidence = "high"
'''

def write_tmp(text):
    fd, p = tempfile.mkstemp(suffix=".toml"); os.close(fd)
    open(p, "w", encoding="utf-8").write(text)
    return p

def test_load():
    m = M.load(write_tmp(FIXTURE))
    check(set(m.opcodes) == {0x90, 0x1f4}, "loads both opcodes keyed by int")
    o = m.opcodes[0x90]
    check(o.argc == 7, "0x90 argc == 7")
    check(o.code_target_args == [5, 6, 7], "0x90 code_target_args parsed")
    check(o.semantics.name == "hotspot-branch", "0x90 semantics.name")
    check(o.semantics.depends_on == [0x1f4], "depends_on parsed as int list")
    check(o.semantics.args[0]["role"] == "x", "arg role parsed")
    rev = M.dependents(m)
    check(rev.get(0x1f4) == [0x90], "dependents: 0x1f4 depended on by 0x90")

def main():
    test_load()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0

if __name__ == "__main__":
    sys.exit(main())
