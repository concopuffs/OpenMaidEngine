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

DANGLING = '''
[[opcode]]
op = 0x10
label = "x"
argc = 0
[opcode.semantics]
name = "a"
category = "compute"
source = "inference"
confidence = "low"
depends_on = [0x99]
'''

CEILING = '''
[[opcode]]
op = 0x10
label = "x"
argc = 0
[opcode.semantics]
name = "low-op"
category = "compute"
source = "kelebek"
confidence = "low"
[[opcode]]
op = 0x11
label = "y"
argc = 0
[opcode.semantics]
name = "high-op"
category = "compute"
source = "inference"
confidence = "high"
depends_on = [0x10]
'''

BADVOCAB = '''
[[opcode]]
op = 0x10
label = "x"
argc = 0
[opcode.semantics]
name = "a"
category = "bogus"
source = "inference"
confidence = "low"
'''

def test_lint():
    e, w = M.lint(M.load(write_tmp(DANGLING)))
    check(any("0x99" in m for m in e), "dangling depends_on is an error")
    e, w = M.lint(M.load(write_tmp(CEILING)))
    check(any("0x11" in m for m in w), "confidence-ceiling violation is a warning")
    check(e == [], "confidence-ceiling case has no errors")
    e, w = M.lint(M.load(write_tmp(BADVOCAB)))
    check(any("category" in m for m in e), "unknown category is an error")
    e, w = M.lint(M.load(write_tmp(FIXTURE)))
    check(e == [], "clean fixture has no lint errors")

def test_bootstrap():
    import opcodes_build as B
    from pathlib import Path
    fd, p = tempfile.mkstemp(suffix=".toml"); os.close(fd); os.remove(p)
    tp = Path(p)
    B.bootstrap(tp)                      # first run: meta + all skeletons
    m = M.load(tp)
    check(len(m.opcodes) >= 240, f"bootstrap seeded ~248 opcodes (got {len(m.opcodes)})")
    check(0x90 in m.opcodes and m.opcodes[0x90].argc == 7, "0x90 seeded with argc 7")
    n1 = len(m.opcodes)
    B.bootstrap(tp)                      # idempotent: appends nothing new
    check(len(M.load(tp).opcodes) == n1, "second bootstrap adds no duplicates")
    e, w = M.lint(m)
    check(e == [], f"bootstrapped file lints clean (errors: {e[:3]})")

def test_emit_inferred():
    import opcodes_build as B
    src = B.emit_inferred_py(M.load(write_tmp(FIXTURE)))
    check("INFERRED" in src and "hotspot-branch" in src, "shim contains INFERRED + our mnemonic")
    ns = {}
    exec(compile(src, "<gen>", "exec"), ns)
    inf = ns["INFERRED"]
    check(0x90 in inf and inf[0x90]["name"] == "hotspot-branch", "generated INFERRED[0x90]['name'] correct")
    check(0x1f4 in inf, "named marker 0x1f4 (name != label) included")

def test_emit_views():
    import opcodes_build as B, json as _json
    m = M.load(write_tmp(FIXTURE))
    j = _json.loads(B.emit_json(m))
    check(j["dependents"]["0x1f4"] == ["0x90"], "json dependents index correct")
    check(any(o["op"] == "0x90" for o in j["opcodes"]), "json lists opcode 0x90")
    md = B.emit_reference_md(m)
    check("hotspot-branch" in md and "depended on by" in md.lower(), "reference md has entry + dependents line")
    cov = B.emit_coverage_md(m)
    check("investigation" in cov, "coverage md breaks down by source")

def main():
    test_load()
    test_lint()
    test_bootstrap()
    test_emit_inferred()
    test_emit_views()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0

if __name__ == "__main__":
    sys.exit(main())
