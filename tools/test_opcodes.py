#!/usr/bin/env python3
"""Standalone tests for the opcode reference tooling. Run: py -3.11 -X utf8 tools/test_opcodes.py"""
import os, sys, tempfile
from collections import Counter
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opcodes_model as M

FAILS = []
def check(cond, msg):
    print(("  ok  " if cond else " FAIL ") + msg)
    if not cond: FAILS.append(msg)

FIXTURE = '''
[meta]
profile_ids = ["himegari"]
inline_array_opcode = 0x64
[evidence_defaults.investigation]
method = "native-re"
artifact = "fixture artifact"
scope = "fixture SYS4422"
profile_ids = ["himegari"]
engine_revisions = ["SYS4422"]
[[observation]]
profile_id = "himegari"
catalog_revision = "S4IC422"
script_revision = "SYS4422"
engine_abi_id = "SYS4422"
method = "catalog-corpus"
artifact = "build/games/himegari/fixture-corpus.json"
script_count = 1
instruction_count = 2
opcodes = [0x90, 0x1f4]
[[abi_layer]]
id = "SYS4422"
kind = "complete-observed-snapshot"
composes = []
observation_profile_id = "himegari"
evidence_method = "catalog-corpus"
evidence_artifact = "build/games/himegari/fixture-corpus.json"
evidence_scope = "fixture SYS4422 snapshot"
members = []
contracts = []
removes = []
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
    check(o.observed_by == frozenset({"himegari"}), "per-profile observation parsed")
    check(o.observed_revisions == frozenset({"SYS4422"}), "revision observation parsed")
    check(o.semantics.evidence[0].method == "native-re", "evidence defaults resolve method")
    check(o.semantics.evidence[0].profile_ids == ("himegari",), "evidence profile scope parsed")
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

BAD_PROVENANCE = '''
[meta]
profile_ids = ["himegari"]
[evidence_defaults.investigation]
method = "made-up-method"
artifact = ""
scope = ""
profile_ids = ["not-a-profile"]
engine_revisions = ["SYS4999"]
[[observation]]
profile_id = "himegari"
catalog_revision = "S4IC422"
script_revision = "SYS4422"
engine_abi_id = "SYS4422"
method = "catalog-corpus"
artifact = "build/games/himegari/fixture.json"
script_count = 1
instruction_count = 1
opcodes = [0x10]
[[opcode]]
op = 0x10
label = "x"
argc = 0
[opcode.semantics]
name = "x"
category = "compute"
source = "investigation"
confidence = "low"
evidence = "claim"
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
    e, w = M.lint(M.load(write_tmp(BAD_PROVENANCE)))
    check(any("bad method" in message for message in e), "evidence method is linted")
    check(any("unknown profiles" in message for message in e), "evidence profile ids are linted")
    check(any("unknown revisions" in message for message in e), "evidence revisions are linted")
    check(any("missing artifact" in message for message in e), "evidence artifact is required")
    cycle = FIXTURE.replace('composes = []', 'composes = ["SYS4422"]', 1)
    e, w = M.lint(M.load(write_tmp(cycle)))
    check(any("composition cycle" in message for message in e), "ABI layer cycles are linted")

def test_bootstrap():
    import opcodes_build as B
    from pathlib import Path
    canonical = M.load(Path(__file__).resolve().parents[1] / "vm-map" / "opcodes.toml")
    observed = sorted(canonical.observations["himegari"].opcodes)
    synthetic_scan = (
        Counter({op: 1 for op in observed}),
        {op: {i: {0} for i in range(canonical.opcodes[op].argc)} for op in observed},
    )
    fd, p = tempfile.mkstemp(suffix=".toml"); os.close(fd); os.remove(p)
    tp = Path(p)
    B.bootstrap(tp, corpus_scan=synthetic_scan)  # first run: meta + synthetic observed skeletons
    m = M.load(tp)
    check(len(m.opcodes) == len(observed),
          f"bootstrap seeded all {len(observed)} synthetic observations (got {len(m.opcodes)})")
    check(0x90 in m.opcodes and m.opcodes[0x90].argc == 7, "0x90 seeded with argc 7")
    n1 = len(m.opcodes)
    B.bootstrap(tp, corpus_scan=synthetic_scan)  # idempotent: appends nothing new
    check(len(M.load(tp).opcodes) == n1, "second bootstrap adds no duplicates")
    e, w = M.lint(m)
    check(e == [], f"bootstrapped file lints clean (errors: {e[:3]})")
    B.bootstrap_age(tp, catalog_model=canonical)
    full = M.load(tp)
    check(len(full.opcodes) == len(canonical.opcodes), "bootstrap-age seeds the complete AGE catalog")
    check(all(not o.observed_by for o in full.opcodes.values()),
          "bootstrap does not invent a game observation record")
    check(all(not o.semantics.noop_headless for o in full.opcodes.values()
              if o.op not in m.opcodes),
          "compatibility stubs are not misclassified as semantically safe no-ops")
    e, w = M.lint(full)
    check(e == [], f"full catalog lints clean (errors: {e[:3]})")

def test_emit_semantics():
    import opcodes_build as B
    src = B.emit_semantics_py(M.load(write_tmp(FIXTURE)))
    check("SEMANTICS" in src and "hotspot-branch" in src, "semantic view contains our mnemonic")
    ns = {}
    exec(compile(src, "<gen>", "exec"), ns)
    inf = ns["SEMANTICS"]
    check(0x90 in inf and inf[0x90]["name"] == "hotspot-branch", "generated INFERRED[0x90]['name'] correct")
    check(0x1f4 in inf, "named marker 0x1f4 (name != label) included")

def test_emit_runtime():
    import opcodes_build as B
    from pathlib import Path
    canonical = M.load(Path(__file__).resolve().parents[1] / "vm-map" / "opcodes.toml")
    src = B.emit_runtime_py(canonical)
    ns = {}
    exec(compile(src, "<runtime-gen>", "exec"), ns)
    check(len(ns["OPCODES"]) == len(canonical.opcodes), "runtime view contains the complete catalog")
    check(ns["OPCODES"][0x90] == (canonical.opcodes[0x90].label, 7),
          "runtime view preserves opcode label and argc")
    check(ns["ARG_TYPES"][0] == "imm", "runtime view emits canonical argument labels")
    check(ns["is_label_argument"](0x90, 4, 0x123), "runtime view recognizes hotspot target arg")
    check(not ns["is_label_argument"](0x90, 3, 0x123), "runtime view rejects hotspot data arg")
    check(not ns["is_label_argument"](0x90, 4, 0xffffffff), "fallthrough sentinel is not a target")
    check(ns["ARRAY_OPCODE"] == 0x64, "runtime view emits the inline-array opcode")

def test_canonical_kamidori_revision_provenance():
    from pathlib import Path
    canonical = M.load(Path(__file__).resolve().parents[1] / "vm-map" / "opcodes.toml")
    for op in (0x27, 0x28, 0x250, 0x251, 0x2d8, 0x2da, 0x2db, 0x2de):
        revisions = {revision for evidence in canonical.opcodes[op].semantics.evidence
                     for revision in evidence.engine_revisions}
        check(revisions == {"SYS4433"},
              f"0x{op:x} native evidence remains scoped only to SYS4433")

    for op in (0x32, 0x82, 0x214, 0x227, 0x235, 0x246, 0x24f):
        shared = canonical.opcodes[op].semantics.evidence
        revisions = {revision for evidence in shared for revision in evidence.engine_revisions}
        profiles = {profile for evidence in shared for profile in evidence.profile_ids}
        check(revisions == {"SYS4422", "SYS4433"},
              f"0x{op:x} native evidence records both independently inspected revisions")
        check(profiles == {"himegari", "kamidori"},
              f"0x{op:x} native evidence records both source games")

def test_emit_views():
    import opcodes_build as B, json as _json
    m = M.load(write_tmp(FIXTURE))
    j = _json.loads(B.emit_json(m, frozenset({0x90})))
    check(j["dependents"]["0x1f4"] == ["0x90"], "json dependents index correct")
    snapshot = next(layer for layer in j["abi_layers"] if layer["id"] == "SYS4422")
    check(snapshot["kind"] == "complete-observed-snapshot"
          and snapshot["composes"] == []
          and snapshot["members"] == ["0x90", "0x1f4"],
          "json emits a direct evidence-scoped ABI snapshot with a composition seam")
    check(any(o["op"] == "0x90" for o in j["opcodes"]), "json lists opcode 0x90")
    hotspot = next(o for o in j["opcodes"] if o["op"] == "0x90")
    check(hotspot["observed_by"] == ["himegari"], "json exposes general observation list")
    check(hotspot["semantics"]["evidence"][0]["method"] == "native-re",
          "json emits structured semantic evidence")
    check(hotspot["handler_bindings"][0]["layer_id"] == "SYS4422"
          and hotspot["handler_bindings"][0]["semantic_evidence_status"] == "evidence-confirmed",
          "json separates snapshot handler binding from shared implementation identity")
    md = B.emit_reference_md(m)
    check("hotspot-branch" in md and "depended on by" in md.lower()
          and "ABI snapshots and composition" in md,
          "reference md has entries, dependencies, and ABI resolution provenance")
    cov = B.emit_coverage_md(m)
    check("investigation" in cov, "coverage md breaks down by source")

def main():
    test_load()
    test_lint()
    test_bootstrap()
    test_emit_semantics()
    test_emit_runtime()
    test_canonical_kamidori_revision_provenance()
    test_emit_views()
    print("FAILURES:", len(FAILS))
    return 1 if FAILS else 0

if __name__ == "__main__":
    sys.exit(main())
