# Living Opcode Reference Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `vm-map/opcodes.toml` the single hand-edited source of truth for opcode knowledge (ABI + semantics + provenance + dependencies), from which we generate the Python shim the tooling imports, a machine JSON, a human Markdown reference, and coverage.

**Architecture:** One canonical TOML file. A generator/linter (`tools/opcodes_build.py`) reads it (stdlib `tomllib`) through a small data model (`tools/opcodes_model.py`) and emits four artifacts. Bootstrap seeds all 248 used opcodes from the pristine Kelebek table (`tools/age_opcodes.py`) by *appending* skeleton text (no TOML writer dependency). Emitters are pure `(model) -> str` functions so the real files are only rewritten in the final migration task.

**Tech Stack:** Python 3.11 (`py -3.11 -X utf8`), stdlib only (`tomllib`, `json`, `dataclasses`). Reuses `tools/sys4load.py` + `tools/paths.py`. No new dependencies.

## Global Constraints

- Run all Python as `py -3.11 -X utf8` (Shift-JIS strings need utf8 mode on Windows).
- Stdlib only — do NOT add `pyyaml`/`tomli_w`/`pytest`. Tests are plain scripts run with `py -3.11`.
- `tools/age_opcodes.py` (Kelebek table) is PRISTINE — never edit it.
- `tools/paths.py` is the only place that knows filesystem locations; import paths from it, never hardcode.
- The generated `tools/age_opcodes_himegari.py` MUST keep exposing `INFERRED: dict[int, dict]` where each entry has a `name` key (the only field `sys4load` reads: `sys4load.py:84`). Do not change `sys4load.py`.
- **This workspace is not a git repo.** Treat every **Checkpoint** step as: if `git` is initialized, run the shown `git add/commit`; otherwise just confirm the named outputs exist and continue. Do not run `git init` unless the user asks.
- Controlled vocabularies (the linter enforces these):
  - `category ∈ {marker, structural, control, adv, draw, audio, input, compute, unknown}`
  - `source ∈ {kelebek, harness, investigation, frida, unicorn, inference}`
  - `confidence ∈ {low, med, high}` (ordered low<med<high)

---

## File Structure

- Create `tools/opcodes_model.py` — dataclasses + `load()` + `lint()` + `dependents()`. Pure, importable, testable.
- Create `tools/opcodes_build.py` — CLI: `--bootstrap`, `--build`, `--lint`. Corpus scan + pure emitters + file wiring.
- Create `tools/test_opcodes.py` — standalone test script (no pytest); `py -3.11 -X utf8 tools/test_opcodes.py` prints results and exits nonzero on failure.
- Create `vm-map/opcodes.toml` — canonical file (bootstrapped skeletons, then hand-migrated).
- Generated (written only in Task 6): `tools/age_opcodes_himegari.py` (overwrites the hand-version), `build/opcodes.json`, `docs/opcode-reference.md`, `build/opcode-coverage.md`.
- Retire in Task 7: `vm-map/opcodes-himegari.json`, `vm-map/himegari-opcode-notes.md`, hand-maintained `build/opcode-coverage.md`.
- Modify in Task 7: `docs/PROJECT-STRUCTURE.md`, memory (`himegari-port-status.md`, `MEMORY.md`).

---

## Task 1: Data model + loader (`opcodes_model.py`)

**Files:**
- Create: `tools/opcodes_model.py`
- Test: `tools/test_opcodes.py`

**Interfaces:**
- Produces: `load(path) -> Model`; `Model(meta: dict, opcodes: dict[int, Opcode])`; `Opcode(op, label, argc, code_target_args, abi_source, abi_note, semantics)`; `Semantics(name, category, summary, noop_headless, source, confidence, depends_on: list[int], evidence, details, confirm_by, args: list[dict])`; `dependents(model) -> dict[int, list[int]]`; constants `CATEGORIES`, `SOURCES`, `CONFIDENCE`.

- [ ] **Step 1: Write the failing test**

Create `tools/test_opcodes.py`:

```python
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `py -3.11 -X utf8 tools/test_opcodes.py`
Expected: FAIL — `ModuleNotFoundError: No module named 'opcodes_model'`.

- [ ] **Step 3: Write minimal implementation**

Create `tools/opcodes_model.py`:

```python
#!/usr/bin/env python3
"""In-memory model + loader + linter for vm-map/opcodes.toml (the canonical opcode reference).
Read-only: uses stdlib tomllib. See docs/superpowers/specs/2026-07-06-opcode-reference-design.md."""
from __future__ import annotations
import tomllib
from dataclasses import dataclass, field
from pathlib import Path

CATEGORIES = {"marker", "structural", "control", "adv", "draw", "audio", "input", "compute", "unknown"}
SOURCES = {"kelebek", "harness", "investigation", "frida", "unicorn", "inference"}
CONFIDENCE = {"low": 1, "med": 2, "high": 3}

@dataclass
class Semantics:
    name: str
    category: str = "unknown"
    summary: str = ""
    noop_headless: bool = False
    source: str = "kelebek"
    confidence: str = "low"
    depends_on: list[int] = field(default_factory=list)
    evidence: str = ""
    details: str = ""
    confirm_by: str = ""
    args: list[dict] = field(default_factory=list)

@dataclass
class Opcode:
    op: int
    label: str
    argc: int
    code_target_args: list[int] = field(default_factory=list)
    abi_source: str = "kelebek+decode-validated"
    abi_note: str = ""
    semantics: Semantics | None = None

@dataclass
class Model:
    meta: dict
    opcodes: dict[int, Opcode]

def load(path) -> Model:
    data = tomllib.loads(Path(path).read_text(encoding="utf-8"))
    ops: dict[int, Opcode] = {}
    for e in data.get("opcode", []):
        sem = None
        s = e.get("semantics")
        if s is not None:
            sem = Semantics(
                name=s.get("name", e.get("label", "")),
                category=s.get("category", "unknown"),
                summary=s.get("summary", ""),
                noop_headless=bool(s.get("noop_headless", False)),
                source=s.get("source", "kelebek"),
                confidence=s.get("confidence", "low"),
                depends_on=[int(x) for x in s.get("depends_on", [])],
                evidence=s.get("evidence", ""),
                details=s.get("details", ""),
                confirm_by=s.get("confirm_by", ""),
                args=list(s.get("args", [])),
            )
        ops[int(e["op"])] = Opcode(
            op=int(e["op"]), label=e.get("label", ""), argc=int(e["argc"]),
            code_target_args=[int(x) for x in e.get("code_target_args", [])],
            abi_source=e.get("abi_source", "kelebek+decode-validated"),
            abi_note=e.get("abi_note", ""), semantics=sem,
        )
    return Model(meta=data.get("meta", {}), opcodes=ops)

def dependents(model: Model) -> dict[int, list[int]]:
    """Reverse of depends_on: op -> [ops whose semantics depend on it]."""
    rev: dict[int, list[int]] = {op: [] for op in model.opcodes}
    for op, oc in model.opcodes.items():
        if oc.semantics:
            for dep in oc.semantics.depends_on:
                rev.setdefault(dep, []).append(op)
    for k in rev:
        rev[k].sort()
    return rev
```

- [ ] **Step 4: Run test to verify it passes**

Run: `py -3.11 -X utf8 tools/test_opcodes.py`
Expected: all `test_load` lines `ok`, `FAILURES: 0`, exit 0.

- [ ] **Step 5: Checkpoint**

If git initialized: `git add tools/opcodes_model.py tools/test_opcodes.py && git commit -m "feat(opcodes): data model + loader for opcodes.toml"`
Else: confirm `tools/opcodes_model.py` and `tools/test_opcodes.py` exist; continue.

---

## Task 2: Linter (`lint` in `opcodes_model.py`)

**Files:**
- Modify: `tools/opcodes_model.py` (add `lint`)
- Test: `tools/test_opcodes.py` (add `test_lint`)

**Interfaces:**
- Produces: `lint(model) -> tuple[list[str], list[str]]` returning `(errors, warnings)`.
- Rules: (1) `category`/`source`/`confidence` must be in the controlled vocab — else **error**. (2) every `depends_on` id must exist — else **error** (dangling-ref). (3) an entry's confidence may not exceed the min confidence among its dependencies — else **warning** (confidence-ceiling).

- [ ] **Step 1: Write the failing test**

Add to `tools/test_opcodes.py` (call `test_lint()` from `main` before the summary):

```python
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `py -3.11 -X utf8 tools/test_opcodes.py`
Expected: FAIL — `AttributeError: module 'opcodes_model' has no attribute 'lint'`.

- [ ] **Step 3: Write minimal implementation**

Add to `tools/opcodes_model.py`:

```python
def lint(model: Model) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []
    ops = model.opcodes
    for op, oc in sorted(ops.items()):
        s = oc.semantics
        if not s:
            continue
        tag = f"0x{op:x}"
        if s.category not in CATEGORIES:
            errors.append(f"{tag}: bad category {s.category!r}")
        if s.source not in SOURCES:
            errors.append(f"{tag}: bad source {s.source!r}")
        if s.confidence not in CONFIDENCE:
            errors.append(f"{tag}: bad confidence {s.confidence!r}")
        for dep in s.depends_on:
            if dep not in ops:
                errors.append(f"{tag}: depends_on missing opcode 0x{dep:x}")
        if s.confidence in CONFIDENCE:
            dep_confs = [CONFIDENCE[ops[d].semantics.confidence]
                         for d in s.depends_on
                         if d in ops and ops[d].semantics
                         and ops[d].semantics.confidence in CONFIDENCE]
            if dep_confs and CONFIDENCE[s.confidence] > min(dep_confs):
                warnings.append(f"{tag}: confidence {s.confidence!r} exceeds dependency ceiling")
    return errors, warnings
```

- [ ] **Step 4: Run test to verify it passes**

Run: `py -3.11 -X utf8 tools/test_opcodes.py`
Expected: `test_load` + `test_lint` all `ok`, `FAILURES: 0`.

- [ ] **Step 5: Checkpoint**

If git: `git add tools/opcodes_model.py tools/test_opcodes.py && git commit -m "feat(opcodes): linter (dangling-ref, confidence-ceiling, vocabulary)"`

---

## Task 3: Bootstrap (`opcodes_build.py --bootstrap`)

**Files:**
- Create: `tools/opcodes_build.py`
- Test: `tools/test_opcodes.py` (add `test_bootstrap`)

**Interfaces:**
- Consumes: `paths.scripts()`, `sys4load.load`, `age_opcodes.OPCODES`, `opcodes_model`.
- Produces: `scan_corpus() -> (used: Counter, argtypes: dict[int, dict[int, set[int]]])`; `skeleton_toml(op, label, argc, argtypes_for_op) -> str`; `bootstrap(toml_path: Path) -> None` (creates file with `[meta]` on first run, then appends a skeleton block for each used opcode not already present). CLI: `py -3.11 -X utf8 tools/opcodes_build.py --bootstrap [--toml PATH]`.

- [ ] **Step 1: Write the failing test**

Add to `tools/test_opcodes.py`:

```python
def test_bootstrap():
    import opcodes_build as B
    fd, p = tempfile.mkstemp(suffix=".toml"); os.close(fd); os.remove(p)
    from pathlib import Path
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `py -3.11 -X utf8 tools/test_opcodes.py`
Expected: FAIL — `ModuleNotFoundError: No module named 'opcodes_build'`.

- [ ] **Step 3: Write minimal implementation**

Create `tools/opcodes_build.py`:

```python
#!/usr/bin/env python3
"""Generator + linter for the living opcode reference (vm-map/opcodes.toml).
  --bootstrap  seed skeletons for every used opcode (append-only; preserves hand edits)
  --build      emit age_opcodes_himegari.py + build/opcodes.json + docs/opcode-reference.md + build/opcode-coverage.md
  --lint       run the linter, print errors/warnings, exit nonzero on errors
See docs/superpowers/specs/2026-07-06-opcode-reference-design.md."""
from __future__ import annotations
import os, sys, json, argparse, collections
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
import sys4load
import opcodes_model as M
from age_opcodes import OPCODES

TOML_DEFAULT = paths.VM_MAP / "opcodes.toml"

TYPE_NAMES = {0x0: "imm", 0x1: "float", 0x2: "string", 0x3: "g-int", 0x4: "g-float",
              0x5: "g-str", 0x6: "g-ptr", 0x8: "g-str-ptr", 0x9: "l-int", 0xa: "l-float",
              0xb: "l-str", 0xc: "l-ptr", 0xd: "l-float-ptr", 0xe: "l-str-ptr"}

META_TOML = '''# vm-map/opcodes.toml -- CANONICAL living opcode reference (hand-edited).
# Generated artifacts (age_opcodes_himegari.py, build/opcodes.json, docs/opcode-reference.md,
# build/opcode-coverage.md) come from this file via tools/opcodes_build.py --build. Do not edit those.
# Skeletons are appended by --bootstrap; enrich each [opcode.semantics] as we investigate.
[meta]
instruction_model = "code = seq of <opcode:u32> then argc*(<argtype:u32><value:u32>); len_dwords = 1 + 2*argc"
opcodes_used_by_himegari = 248

[meta.arg_types]
"0x0" = "immediate"
"0x1" = "float"
"0x2" = "string"
"0x3" = "global-int"
"0x4" = "global-float"
"0x5" = "global-string"
"0x6" = "global-ptr"
"0x8" = "global-string-ptr"
"0x9" = "local-int"
"0xa" = "local-float"
"0xb" = "local-string"
"0xc" = "local-ptr"
"0xd" = "local-float-ptr"
"0xe" = "local-string-ptr"

[meta.header_fields]
"F0" = "local_integer_1"
"F1" = "local_floats"
"F2" = "local_strings_1"
"F3" = "local_integer_2"
"F4" = "unknown_data"
"F5" = "local_strings_2"
"F6" = "sub_header_length(=0x1C)"
"F7" = "table_1_length"
"F8" = "table_1_offset(=code end)"
"F9" = "table_2_length"
"F10" = "table_2_offset"
"F11" = "table_3_length"
"F12" = "table_3_offset"
'''

def scan_corpus():
    """used[op] = count; argtypes[op][arg_index] = set(type-codes) across the corpus."""
    used = collections.Counter()
    argtypes: dict[int, dict[int, set]] = collections.defaultdict(lambda: collections.defaultdict(set))
    for name, path in paths.scripts().items():
        try:
            scr = sys4load.load(path)
        except Exception:
            continue
        for ins in scr.instructions:
            used[ins.opcode] += 1
            for i, (t, v) in enumerate(ins.args):
                argtypes[ins.opcode][i].add(t)
    return used, argtypes

def _is_named(label: str) -> bool:
    return not (label.startswith("u00") or label == "dev_ukn" or label.startswith("?"))

def skeleton_toml(op: int, label: str, argc: int, argtypes_for_op: dict) -> str:
    conf = "med" if _is_named(label) else "low"
    lines = ["[[opcode]]", f"op = 0x{op:x}", f'label = "{label}"', f"argc = {argc}",
             'abi_source = "kelebek+decode-validated"', "", "[opcode.semantics]",
             f'name = "{label}"', 'category = "unknown"', 'summary = ""',
             "noop_headless = false", 'source = "kelebek"', f'confidence = "{conf}"',
             "depends_on = []", 'evidence = ""']
    for i in range(argc):
        tnames = [TYPE_NAMES.get(t, "t%#x" % t) for t in sorted(argtypes_for_op.get(i, ()))]
        obs = ", ".join('"%s"' % n for n in tnames)
        lines += ["", "[[opcode.semantics.args]]", f"i = {i + 1}", 'role = ""',
                  f"observed_types = [{obs}]"]
    return "\n".join(lines) + "\n"

def bootstrap(toml_path: Path) -> None:
    used, argtypes = scan_corpus()
    present = set(M.load(toml_path).opcodes) if toml_path.exists() else set()
    blocks = []
    for op in sorted(used):
        if op in present:
            continue
        label, argc = OPCODES.get(op, ("0x%x" % op, 0))
        blocks.append(skeleton_toml(op, label, argc, argtypes[op]))
    if not toml_path.exists():
        toml_path.parent.mkdir(parents=True, exist_ok=True)
        toml_path.write_text(META_TOML + "\n", encoding="utf-8")
    with toml_path.open("a", encoding="utf-8") as f:
        f.write("\n".join(blocks))
    print(f"bootstrap: {len(used)} used opcodes; appended {len(blocks)} new skeletons -> {toml_path}")

def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--bootstrap", action="store_true")
    ap.add_argument("--build", action="store_true")
    ap.add_argument("--lint", action="store_true")
    ap.add_argument("--toml", default=str(TOML_DEFAULT))
    args = ap.parse_args(argv)
    tp = Path(args.toml)
    if args.bootstrap:
        bootstrap(tp)
        return 0
    ap.error("no action (expected --bootstrap/--build/--lint)")

if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 4: Run test to verify it passes**

Run: `py -3.11 -X utf8 tools/test_opcodes.py`
Expected: `test_bootstrap` lines `ok`, `FAILURES: 0`.

- [ ] **Step 5: Checkpoint**

If git: `git add tools/opcodes_build.py tools/test_opcodes.py && git commit -m "feat(opcodes): bootstrap seeds 248 skeletons from Kelebek + corpus arg-types"`

---

## Task 4: Emit the Python shim (`--build` → `age_opcodes_himegari.py`)

**Files:**
- Modify: `tools/opcodes_build.py` (add `emit_inferred_py`, wire `--build`)
- Test: `tools/test_opcodes.py` (add `test_emit_inferred`)

**Interfaces:**
- Produces: `emit_inferred_py(model) -> str`. Pure. Emits `INFERRED: dict[int, dict]` containing an entry ONLY for opcodes whose `semantics.name != label` (i.e., ops we've given a distinct mnemonic) — this reproduces the current `sys4load` behavior exactly (bare Kelebek skeletons add nothing, so they are omitted and untouched unnamed ops keep rendering from `OPCODES`). Each entry carries `name` (required by sys4load) plus `category/noop/confidence/source/summary`.

- [ ] **Step 1: Write the failing test**

Add to `tools/test_opcodes.py`:

```python
def test_emit_inferred():
    import opcodes_build as B
    src = B.emit_inferred_py(M.load(write_tmp(FIXTURE)))
    check("INFERRED" in src and "hotspot-branch" in src, "shim contains INFERRED + our mnemonic")
    ns = {}
    exec(compile(src, "<gen>", "exec"), ns)
    inf = ns["INFERRED"]
    check(0x90 in inf and inf[0x90]["name"] == "hotspot-branch", "generated INFERRED[0x90]['name'] correct")
    check(0x1f4 in inf, "named marker 0x1f4 (name != label) included")
```

- [ ] **Step 2: Run test to verify it fails**

Run: `py -3.11 -X utf8 tools/test_opcodes.py`
Expected: FAIL — `AttributeError: module 'opcodes_build' has no attribute 'emit_inferred_py'`.

- [ ] **Step 3: Write minimal implementation**

Add to `tools/opcodes_build.py` (above `main`):

```python
GEN_HEADER = "# DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build\n"

def emit_inferred_py(model: M.Model) -> str:
    lines = [GEN_HEADER, '"""Inferred Himegari opcode semantics (generated). sys4load reads INFERRED[op][\'name\']."""',
             "from __future__ import annotations", "", "INFERRED: dict[int, dict] = {"]
    for op, oc in sorted(model.opcodes.items()):
        s = oc.semantics
        if not s or s.name == oc.label:      # only ops we've given a distinct mnemonic
            continue
        lines.append("    0x%x: dict(name=%r, category=%r, noop=%r, confidence=%r, source=%r, summary=%r),"
                     % (op, s.name, s.category, s.noop_headless, s.confidence, s.source, s.summary))
    lines.append("}")
    return "\n".join(lines) + "\n"
```

And wire `--build` in `main` (replace the final `ap.error(...)` line):

```python
    if args.build:
        model = M.load(tp)
        errors, warnings = M.lint(model)
        for m in warnings:
            print("warn:", m)
        if errors:
            for m in errors:
                print("error:", m)
            return 1
        (paths.REPO / "tools" / "age_opcodes_himegari.py").write_text(emit_inferred_py(model), encoding="utf-8")
        print("build: wrote tools/age_opcodes_himegari.py")
        return 0
    if args.lint:
        errors, warnings = M.lint(M.load(tp))
        for m in warnings:
            print("warn:", m)
        for m in errors:
            print("error:", m)
        print(f"lint: {len(errors)} errors, {len(warnings)} warnings")
        return 1 if errors else 0
    ap.error("no action (expected --bootstrap/--build/--lint)")
```

- [ ] **Step 4: Run test to verify it passes**

Run: `py -3.11 -X utf8 tools/test_opcodes.py`
Expected: `test_emit_inferred` lines `ok`, `FAILURES: 0`.

- [ ] **Step 5: Checkpoint**

If git: `git add tools/opcodes_build.py tools/test_opcodes.py && git commit -m "feat(opcodes): emit drop-in age_opcodes_himegari.py shim; wire --build/--lint"`

---

## Task 5: Emit JSON + Markdown reference + coverage (`--build`)

**Files:**
- Modify: `tools/opcodes_build.py` (add `emit_json`, `emit_reference_md`, `emit_coverage_md`; wire into `--build`)
- Test: `tools/test_opcodes.py` (add `test_emit_views`)

**Interfaces:**
- Produces: `emit_json(model) -> str` (includes a `dependents` map), `emit_reference_md(model) -> str` (per-opcode section with a "depended on by" line), `emit_coverage_md(model) -> str` (counts by source/confidence/category). All pure.

- [ ] **Step 1: Write the failing test**

Add to `tools/test_opcodes.py`:

```python
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `py -3.11 -X utf8 tools/test_opcodes.py`
Expected: FAIL — `AttributeError: ... 'emit_json'`.

- [ ] **Step 3: Write minimal implementation**

Add to `tools/opcodes_build.py`:

```python
def emit_json(model: M.Model) -> str:
    rev = M.dependents(model)
    out = {"meta": model.meta, "opcodes": [],
           "dependents": {"0x%x" % k: ["0x%x" % d for d in v] for k, v in rev.items() if v}}
    for op, oc in sorted(model.opcodes.items()):
        e = {"op": "0x%x" % op, "label": oc.label, "argc": oc.argc,
             "code_target_args": oc.code_target_args, "abi_source": oc.abi_source}
        s = oc.semantics
        if s:
            e["semantics"] = {"name": s.name, "category": s.category, "summary": s.summary,
                              "noop_headless": s.noop_headless, "source": s.source,
                              "confidence": s.confidence, "depends_on": ["0x%x" % d for d in s.depends_on],
                              "evidence": s.evidence, "details": s.details, "args": s.args}
        out["opcodes"].append(e)
    return json.dumps(out, ensure_ascii=False, indent=2) + "\n"

def emit_reference_md(model: M.Model) -> str:
    rev = M.dependents(model)
    L = ["<!-- DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build -->",
         "# Opcode Reference (generated)", "",
         f"{len(model.opcodes)} opcodes used by Himegari. Source of truth: `vm-map/opcodes.toml`.", ""]
    by_cat = collections.defaultdict(list)
    for op, oc in model.opcodes.items():
        cat = oc.semantics.category if oc.semantics else "unknown"
        by_cat[cat].append(op)
    for cat in sorted(by_cat):
        L += [f"## {cat}", ""]
        for op in sorted(by_cat[cat]):
            oc = model.opcodes[op]
            s = oc.semantics
            name = s.name if s else oc.label
            L.append(f"### 0x{op:x} `{name}` ({oc.label}, argc {oc.argc})")
            if s:
                L.append(f"- **summary:** {s.summary}" if s.summary else "- **summary:** —")
                L.append(f"- **grounding:** source={s.source}, confidence={s.confidence}"
                         + (f", noop_headless={s.noop_headless}" if s.noop_headless else ""))
                if s.depends_on:
                    L.append("- **depends on:** " + ", ".join("0x%x" % d for d in s.depends_on))
                if rev.get(op):
                    L.append("- **depended on by:** " + ", ".join("0x%x" % d for d in rev[op]))
                if s.evidence:
                    L.append(f"- **evidence:** {s.evidence}")
                if s.details:
                    L += ["", s.details]
            L.append("")
    return "\n".join(L) + "\n"

def emit_coverage_md(model: M.Model) -> str:
    by_src = collections.Counter()
    by_conf = collections.Counter()
    by_cat = collections.Counter()
    named = 0
    for oc in model.opcodes.values():
        s = oc.semantics
        if s:
            by_src[s.source] += 1
            by_conf[s.confidence] += 1
            by_cat[s.category] += 1
            if s.name != oc.label:
                named += 1
    L = ["<!-- DO NOT EDIT -- generated from vm-map/opcodes.toml -->", "# Opcode Coverage (generated)", "",
         f"- opcodes: {len(model.opcodes)}", f"- given a distinct mnemonic: {named}", "",
         "## by source", ""]
    L += [f"- {k}: {v}" for k, v in sorted(by_src.items())]
    L += ["", "## by confidence", ""] + [f"- {k}: {by_conf[k]}" for k in ("high", "med", "low")]
    L += ["", "## by category", ""] + [f"- {k}: {v}" for k, v in sorted(by_cat.items())]
    return "\n".join(L) + "\n"
```

Extend the `--build` block in `main` (after writing the shim, before `return 0`):

```python
        (paths.BUILD).mkdir(parents=True, exist_ok=True)
        (paths.BUILD / "opcodes.json").write_text(emit_json(model), encoding="utf-8")
        (paths.REPO / "docs" / "opcode-reference.md").write_text(emit_reference_md(model), encoding="utf-8")
        (paths.BUILD / "opcode-coverage.md").write_text(emit_coverage_md(model), encoding="utf-8")
        print("build: wrote build/opcodes.json, docs/opcode-reference.md, build/opcode-coverage.md")
```

- [ ] **Step 4: Run test to verify it passes**

Run: `py -3.11 -X utf8 tools/test_opcodes.py`
Expected: `test_emit_views` lines `ok`, `FAILURES: 0`.

- [ ] **Step 5: Checkpoint**

If git: `git add tools/opcodes_build.py tools/test_opcodes.py && git commit -m "feat(opcodes): emit opcodes.json, opcode-reference.md, coverage"`

---

## Task 6: Real bootstrap + migrate legacy inferences + differential-verify

**Files:**
- Create: `vm-map/opcodes.toml` (via bootstrap, then hand-edit)
- Regenerate: `tools/age_opcodes_himegari.py`, `build/opcodes.json`, `docs/opcode-reference.md`, `build/opcode-coverage.md`

**Interfaces:**
- Consumes: everything above. No new code except a one-time migration helper (shown below; not committed as tooling).

- [ ] **Step 1: Snapshot current disassembly (regression baseline)**

Run (captures pre-change mnemonics for two representative scripts):
```bash
py -3.11 -X utf8 tools/sys4load.py ../../extracted/DATA1/SC0830.BIN > /tmp/sc0830.before.asm
py -3.11 -X utf8 tools/sys4load.py ../../extracted/DATA1/MENU.BIN   > /tmp/menu.before.asm
```
Expected: two files written (they contain `hotspot-branch`, `stmt-begin`, etc. from the current hand-written overlay).

- [ ] **Step 2: Bootstrap the real canonical file**

Run: `py -3.11 -X utf8 tools/opcodes_build.py --bootstrap`
Expected: `bootstrap: 248 used opcodes; appended 248 new skeletons -> ...opcodes.toml`. Confirm `vm-map/opcodes.toml` exists with a `[meta]` block and 248 `[[opcode]]` blocks.

- [ ] **Step 3: Generate migration suggestions from the legacy overlay**

Run this one-time helper (reads the CURRENT hand-written `age_opcodes_himegari.py` before it gets overwritten, and prints TOML `[opcode.semantics]` blocks to paste):
```bash
py -3.11 -X utf8 - <<'PY'
import sys, os
sys.path.insert(0, "tools")
from age_opcodes_himegari import INFERRED
MAP = {"structure": "investigation", "context": "inference", "harness": "harness",
       "frida": "frida", "unicorn": "unicorn"}
for op, e in sorted(INFERRED.items()):
    src = MAP.get(e.get("method", ""), "investigation")
    print(f"# --- 0x{op:x}: replace the seeded [opcode.semantics] with: ---")
    print("[opcode.semantics]")
    print(f'name = {e["name"]!r}')
    print(f'category = {e.get("category","unknown")!r}')
    print(f'summary = {e.get("note","")!r}')
    print(f'noop_headless = {str(bool(e.get("noop", False))).lower()}')
    print(f'source = {src!r}')
    print(f'confidence = {e.get("confidence","low")!r}')
    print("depends_on = []   # FILL: opcodes this reading rests on")
    print(f'evidence = {e.get("note","")!r}')
    print()
PY
```
Expected: ~26 TOML blocks printed (0x71, 0x7a, 0x90, 0x97, 0xb6, 0x1a2, 0x1bc, 0x1bf, 0x1d2, 0x1d5, 0x1f4, 0x1f5, 0x1f7, 0x1fa, 0x1ff, 0x202, 0x203, 0x215, 0x217, 0x218, 0x21a, 0x21b, 0x258).

- [ ] **Step 4: Hand-migrate into `vm-map/opcodes.toml`**

For each printed block, find that opcode's `[opcode.semantics]` in `vm-map/opcodes.toml` and replace the seeded fields with the printed ones. Then add `depends_on`.

**`depends_on` principle:** it tracks *inference-on-inference* chains — list an op here ONLY when our reading rests on another op whose meaning is itself uncertain (our inference), so a later correction cascades. Reliance on a **validated core op** (e.g. `jcc 0xa0`, `call 0x8f`, `mov 0x55` — Kelebek-named and harness/RECOVER-proven) is a solid root: put that reasoning in `evidence` text, NOT in `depends_on` (adding it would also trip a spurious confidence-ceiling warning, since core ops seed at `med`). Applying this:
- `0x90` (`hotspot-branch`): `depends_on = [0x1f4, 0x1f5]` (rests on our *inferred* stmt markers); set `details` to the multi-line evidence from `vm-map/himegari-opcode-notes.md` §F (paste the section body into a TOML `details = """ ... """`).
- `0x97` (`hotspot-reg?`): `depends_on = [0x90]` (its role was inferred from interleaving with our inferred 0x90).
- `0x1d5`/`0x1bc`/`0x1bf` (markers inferred from following `jcc`/`call`): `depends_on = []`; put "always follows jcc 0xa0" / "call 0x8f → 0x1bf" in `evidence` (jcc/call are validated roots).
- Leave `depends_on = []` for ops grounded directly (`0x1f4`/`0x1f5`/`0x71` structural, harness-confirmed ADV ops).

Also fold the cross-cutting evidence from `himegari-opcode-notes.md` (bucket intros, the coverage narrative) that you want to keep into the relevant entries' `details` or the `[meta]` block — everything that must survive the retirement of that file in Task 7.

- [ ] **Step 5: Lint, then build**

Run:
```bash
py -3.11 -X utf8 tools/opcodes_build.py --lint
py -3.11 -X utf8 tools/opcodes_build.py --build
```
Expected: lint prints `0 errors` (confidence-ceiling warnings are acceptable — review each; downgrade confidence or fix a dependency if a warning is legitimate). Build writes all four artifacts.

- [ ] **Step 6: Differential verification (the proof it's a faithful drop-in)**

Run:
```bash
py -3.11 -X utf8 tools/vm0.py --test                 # RECOVER unit test
py -3.11 -X utf8 tools/vm0.py --sweep | tail -2       # coverage number
py -3.11 -X utf8 tools/sys4load.py ../../extracted/DATA1/SC0830.BIN > /tmp/sc0830.after.asm
py -3.11 -X utf8 tools/sys4load.py ../../extracted/DATA1/MENU.BIN   > /tmp/menu.after.asm
diff /tmp/sc0830.before.asm /tmp/sc0830.after.asm && echo "SC0830 identical"
diff /tmp/menu.before.asm   /tmp/menu.after.asm   && echo "MENU identical"
```
Expected: `RECOVER unit test: PASS`; sweep still `282/294 = 95.9%`; both `diff`s empty (`... identical`). If a diff is non-empty, an opcode's `name` was migrated wrong — fix that entry in `opcodes.toml`, rebuild, re-diff.

- [ ] **Step 7: Checkpoint**

If git: `git add vm-map/opcodes.toml tools/age_opcodes_himegari.py build/opcodes.json docs/opcode-reference.md build/opcode-coverage.md && git commit -m "feat(opcodes): migrate to opcodes.toml as single source of truth; regenerate artifacts"`

---

## Task 7: Retire superseded files + update docs/memory

**Files:**
- Delete: `vm-map/opcodes-himegari.json`, `vm-map/himegari-opcode-notes.md`
- Modify: `docs/PROJECT-STRUCTURE.md`, `C:\Users\m\.claude\projects\S--Game-Hacking-Eushully-Himegari\memory\himegari-port-status.md`, `...\memory\MEMORY.md`

**Interfaces:** none (documentation).

- [ ] **Step 1: Confirm content is preserved before deleting**

Verify the retiring files' load-bearing content now lives in `vm-map/opcodes.toml` / `docs/opcode-reference.md`:
```bash
grep -c "hotspot-branch" docs/opcode-reference.md          # >=1
grep -c "instruction_model" vm-map/opcodes.toml            # ==1 (meta migrated from opcodes-himegari.json)
grep -ci "hotspot" vm-map/opcodes.toml                     # 0x90/0x97 details migrated from notes §F
```
Expected: all nonzero. Only proceed if the §F evidence and the JSON's meta really made it into `opcodes.toml`.

- [ ] **Step 2: Delete the superseded files**

```bash
rm vm-map/opcodes-himegari.json vm-map/himegari-opcode-notes.md
```
(The hand-maintained `build/opcode-coverage.md` is now overwritten by `--build`, so no delete needed — it's generated.)

- [ ] **Step 3: Update `docs/PROJECT-STRUCTURE.md`**

In the `vm-map/` and `tools/` sections, replace mentions of `opcodes-himegari.json` / `himegari-opcode-notes.md` and the hand-written `age_opcodes_himegari.py` with the new model:
```
    ├── vm-map/
    │   ├── opcodes.toml            ★ CANONICAL opcode reference (hand-edited: ABI + semantics
    │   │                             + provenance + depends_on). Source of truth for the opcode layer.
    │   ├── kelebek1-age-shared.cpp   upstream opcode-table source
    │   └── opcode-leads.json, small-script-listings.md
    ├── tools/
    │   ├── opcodes_build.py          generator/linter: opcodes.toml -> {age_opcodes_himegari.py,
    │   │                             build/opcodes.json, docs/opcode-reference.md, build/opcode-coverage.md}
    │   ├── opcodes_model.py          load + lint (dangling-ref, confidence-ceiling, vocab) + dependents
    │   ├── age_opcodes.py            Kelebek table, PRISTINE (ABI baseline; never edit)
    │   ├── age_opcodes_himegari.py   GENERATED from opcodes.toml (do not hand-edit)
```
Add a bullet under Conventions: *"Opcode knowledge is edited ONLY in `vm-map/opcodes.toml`; run `tools/opcodes_build.py --build` to regenerate the shim/JSON/reference/coverage. `docs/opcode-reference.md` and `build/opcodes.json` are generated."*

- [ ] **Step 4: Update memory**

In `himegari-port-status.md`, update the tooling/opcode paragraph: opcode work is now a single source of truth at `vm-map/opcodes.toml` (+ `opcodes_build.py`/`opcodes_model.py`), generating `age_opcodes_himegari.py` + `build/opcodes.json` + `docs/opcode-reference.md` + coverage; `himegari-opcode-notes.md` and `opcodes-himegari.json` retired (content folded in). In `MEMORY.md`, adjust the `[SYS4 script format]` / status hooks that referenced those files.

- [ ] **Step 5: Final verification**

```bash
py -3.11 -X utf8 tools/test_opcodes.py            # FAILURES: 0
py -3.11 -X utf8 tools/opcodes_build.py --lint     # 0 errors
py -3.11 -X utf8 tools/vm0.py --test               # PASS
```
Expected: all green. No remaining references to the deleted files in `tools/` or `docs/`:
```bash
grep -rl "opcodes-himegari.json\|himegari-opcode-notes" tools docs || echo "no stale references"
```
Expected: `no stale references`.

- [ ] **Step 6: Checkpoint**

If git: `git add -A && git commit -m "docs(opcodes): retire superseded opcode files; update structure + memory"`

---

## Self-Review

**Spec coverage** (each spec section → task):
- Single source of truth / data flow → Tasks 3–6 (bootstrap, build, migrate). ✓
- `[meta]` + `[[opcode]]` schema (ABI vs semantics) → Task 1 model + Task 3 skeleton. ✓
- Source vocabulary + confidence → Task 2 lint (vocabulary) + Task 6 migration mapping. ✓
- Generator subcommands (`--bootstrap/--build/--lint`) → Tasks 3, 4, 5. ✓
- Four generated artifacts → Task 4 (shim) + Task 5 (json/md/coverage). ✓
- Three+ lint checks (dangling-ref, dependents index, confidence-ceiling, vocab) → Task 2 (+ dependents in Task 1, rendered in Task 5). ✓
- Bootstrap auto-fills observed_types from corpus → Task 3 `scan_corpus`/`skeleton_toml`. ✓
- Migration of ~26 inferences + notes evidence → Task 6. ✓
- Retire 3 files; keep Kelebek pristine → Task 7 (+ Global Constraint). ✓
- Zero disruption to sys4load/vm0 → Task 4 emit rule (name != label) + Task 6 diff regression. ✓
- Testing: regression (disasm diff, --test, --sweep), lint fixtures, round-trip-ish load → Tasks 1,2,6. ✓

**Placeholder scan:** no "TBD/handle edge cases"; the only intentionally-manual step is Task 6 Step 4 (paste migration blocks + assign `depends_on`), which is inherent to a human judgement task and is spelled out per-opcode.

**Type consistency:** `Model`/`Opcode`/`Semantics` fields are used identically across `load`, `lint`, `dependents`, and every `emit_*`. `emit_inferred_py` writes `dict(name=...)` → `INFERRED[op]["name"]`, matching `sys4load.py:84`. CLI flags `--bootstrap/--build/--lint/--toml` consistent between Task 3 and Tasks 4–5.
