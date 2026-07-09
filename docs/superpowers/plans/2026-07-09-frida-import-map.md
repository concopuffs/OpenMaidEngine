# Frida Import-Map — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Name the dynamically-resolved Win32 APIs at their call sites in the `/v2` Ghidra image by mapping the packer's resolved import pointers (`RVA → dll!Func`) from the live process and applying clean labels.

**Architecture:** One Frida tool (`tools/frida/map_imports.py`) attaches to the live game, builds `{runtime_addr → dll!Func}` from loaded-module exports, scans the `0x400000` module for aligned DWORDs matching those addresses, and (a) `--recon` reports clustering [the hard gate] or (b) writes `build/import-map.json`. A `run_script_inline` Java script then applies `imp_<dll>_<func>` labels to `/v2`. The scan/match/cluster logic is pure Python (unit-tested); the Frida attach is exercised live.

**Tech Stack:** Python 3.11 (`py -3.11 -X utf8`), Frida 17.x (`tools/frida/` pattern — see `dump_engine.py`), ghidra-mcp `run_script_inline` (Java; `GHIDRA_MCP_ALLOW_SCRIPTS=1`).

**Spec:** `docs/superpowers/specs/2026-07-09-frida-import-map-design.md`.

## Global Constraints

- Run Python as `py -3.11 -X utf8 tools/<name>.py …` (utf8 mandatory on Windows). Frida scripts: `py -3.11 -u -X utf8 tools/frida/<name>.py`.
- Tools import `tools/paths.py`; never hard-code paths. `build/` is disposable/gitignored.
- **ASLR safety:** build the export map and scan the module in the SAME live process; carry back only **RVA** (offset into the fixed `0x400000` main module). Never scan the old `/v2` dump for current-process pointer values.
- **Read-only Frida only** — `enumerateModules`/`enumerateExports` + memory reads (the safe plain-JS pattern; no spawn, no `GetProcAddress` hook, no patching).
- **Dump range:** the `/v2` image covers `0x400000`..`0x660000` (size `0x260000`). A match at RVA `≥ 0x260000` is heap/out-of-dump and NOT labelable.
- **Recon is a HARD GATE (Task 1):** if matches are not module-resident and in-range, STOP and document; do not build the labeler on heap-resident pointers.
- **Ghidra:** confirm the active program is `/v2/range_00400000.bin` (base `0x400000`, 4308 fns) before any script — the two-program gotcha (`docs/engine-re.md` runbook). End image mutation with `save_program`.
- Preserve annotations: never clobber an existing `USER_DEFINED` symbol.

---

## Task 1: Recon probe — export map, module scan, clustering report (THE GATE)

**Files:**
- Create: `tools/frida/map_imports.py` (Frida attach + export enum + scan + report; `--recon`)
- Create: `tools/frida/test_map_imports.py` (unit tests for the pure scan/match/cluster logic)
- Reference: `tools/frida/dump_engine.py` (attach + module-read boilerplate to mirror), `tools/frida/README.md`

**Interfaces:**
- Produces (pure, importable): `build_export_index(exports) -> dict[int,str]`; `scan_pointer_matches(mem: bytes, base_va: int, index: dict[int,str]) -> list[tuple[int,int,str]]` returning `(rva, value, name)`; `cluster_runs(rvas: list[int], stride=4) -> list[tuple[int,int]]` returning `(start_rva, count)`.
- Produces (Task 2 consumes): the `--recon` verdict — whether matches are module-resident and in-range.

- [ ] **Step 1: Write failing tests for the pure scan/cluster logic**

```python
# tools/frida/test_map_imports.py  (plain runner, project convention — no pytest)
import sys, struct
from map_imports import build_export_index, scan_pointer_matches, cluster_runs

FAILS = []
def check(c, m):
    (FAILS.append(m) or print("FAIL:", m)) if not c else print("ok:", m)

def test_export_index_canonicalizes():
    idx = build_export_index([
        {"address": 0x76d80e70, "name": "LoadLibraryA", "module": "kernel32.dll"},
        {"address": 0x76d7f7f0, "name": "GetProcAddress", "module": "kernel32.dll"},
    ])
    check(idx[0x76d80e70] == "kernel32.dll!LoadLibraryA", "index maps addr->dll!func")

def test_scan_matches_little_endian_aligned():
    # base 0x400000; two import pointers at RVA 0x10 and 0x14, junk elsewhere.
    idx = {0x76d80e70: "kernel32.dll!LoadLibraryA", 0x76d7f7f0: "kernel32.dll!GetProcAddress"}
    mem = bytearray(0x20)
    struct.pack_into("<I", mem, 0x10, 0x76d80e70)
    struct.pack_into("<I", mem, 0x14, 0x76d7f7f0)
    struct.pack_into("<I", mem, 0x18, 0x12345678)          # not an export -> no match
    m = scan_pointer_matches(bytes(mem), 0x400000, idx)
    check([(r, v) for r, v, _ in m] == [(0x10, 0x76d80e70), (0x14, 0x76d7f7f0)],
          "scan finds aligned LE pointer matches, skips non-exports")

def test_scan_ignores_unaligned():
    idx = {0x76d80e70: "kernel32.dll!LoadLibraryA"}
    mem = bytearray(0x10)
    struct.pack_into("<I", mem, 0x2, 0x76d80e70)           # unaligned -> ignored
    check(scan_pointer_matches(bytes(mem), 0x400000, idx) == [], "unaligned pointer ignored")

def test_cluster_runs_groups_contiguous():
    # a run of 3 at 0x100/104/108, a singleton at 0x200
    check(cluster_runs([0x100, 0x104, 0x108, 0x200]) == [(0x100, 3), (0x200, 1)],
          "cluster groups contiguous aligned runs, isolates singleton")

def main():
    test_export_index_canonicalizes(); test_scan_matches_little_endian_aligned()
    test_scan_ignores_unaligned(); test_cluster_runs_groups_contiguous()
    print("FAILURES:", len(FAILS)); return 1 if FAILS else 0

if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `py -3.11 -X utf8 tools/frida/test_map_imports.py`
Expected: FAIL — `ModuleNotFoundError: No module named 'map_imports'`.

- [ ] **Step 3: Implement the pure logic + Frida recon in `map_imports.py`**

Pure functions (exact):

```python
import struct

def build_export_index(exports):
    """[{address,name,module}] -> {int addr: 'module!name'} (first name wins per addr)."""
    idx = {}
    for e in exports:
        a = int(e["address"])
        if a and a not in idx:
            idx[a] = f"{e['module']}!{e['name']}"
    return idx

def scan_pointer_matches(mem, base_va, index):
    """Aligned little-endian DWORD scan; return [(rva, value, name)] for values in index."""
    out = []
    n = len(mem) & ~3
    for off in range(0, n, 4):
        v = struct.unpack_from("<I", mem, off)[0]
        name = index.get(v)
        if name is not None:
            out.append((off, v, name))
    return out

def cluster_runs(rvas, stride=4):
    """Group sorted RVAs into contiguous aligned runs -> [(start_rva, count)]."""
    runs = []
    for r in sorted(rvas):
        if runs and r == runs[-1][0] + runs[-1][1] * stride:
            s, c = runs[-1]; runs[-1] = (s, c + 1)
        else:
            runs.append((r, 1))
    return runs
```

Frida driver (mirror `dump_engine.py`'s attach/rpc; JS enumerates exports + returns the module range bytes). Adapt names to the existing helper if `dump_engine.py` factors one out:

```python
import sys, frida, json, paths
MODULE = "AGE.EXE"; BASE = 0x400000

_JS = r"""
rpc.exports = {
  exports: function () {
    var out = [];
    Process.enumerateModules().forEach(function (m) {
      m.enumerateExports().forEach(function (e) {
        out.push({address: e.address.toString(), name: e.name, module: m.name});
      });
    });
    return out;
  },
  modulemem: function (base, size) {
    return Memory.readByteArray(ptr(base), size);
  },
  modulesize: function (name) {
    var m = Process.getModuleByName(name); return [m.base.toString(), m.size];
  }
};
"""

def attach():
    dev = frida.get_local_device()
    procs = [p for p in dev.enumerate_processes() if p.name.lower().startswith("age")]
    if not procs:
        sys.exit("AGE.EXE not found — launch the game first.")
    return dev.attach(procs[0].pid)

def collect():
    session = attach()
    script = session.create_script(_JS); script.load()
    api = script.exports_sync
    exports = [{"address": int(e["address"], 16), "name": e["name"], "module": e["module"]}
               for e in api.exports()]
    base_s, size = api.modulesize(MODULE)
    mem = bytes(api.modulemem(int(base_s, 16), size))
    session.detach()
    return exports, int(base_s, 16), mem

def recon():
    exports, base, mem = collect()
    idx = build_export_index(exports)
    matches = scan_pointer_matches(mem, base, idx)
    runs = cluster_runs([r for r, _, _ in matches])
    inrange = [r for r, _, _ in matches if r < 0x260000]
    print(f"exports mapped={len(idx)}  module bytes={len(mem)}  matches={len(matches)}  in-range(<0x260000)={len(inrange)}")
    print(f"clusters(runs>=3)={[hex(s)+':'+str(c) for s,c in runs if c>=3]}")
    print(f"singletons={sum(1 for _,c in runs if c==1)}")
    verdict = "GATE-PASS: module-resident table present" if any(c>=3 for _,c in runs) and inrange else \
              "GATE-FAIL: no in-module import table — STOP, pointers may be heap-resident"
    print(verdict)

if __name__ == "__main__":
    if "--recon" in sys.argv[1:]:
        recon()
    else:
        from map_imports_full import build   # Task 2 adds the writer; keep recon self-contained
        build()
```

*(If `dump_engine.py` already exposes an attach/read helper, import and reuse it instead of duplicating `attach()`.)*

- [ ] **Step 4: Run the unit tests to verify they pass**

Run: `py -3.11 -X utf8 tools/frida/test_map_imports.py`
Expected: PASS (4 tests, `FAILURES: 0`).

- [ ] **Step 5: Run the live recon against the running game (THE GATE)**

Ensure AGE.EXE is running, then:
Run: `py -3.11 -u -X utf8 tools/frida/map_imports.py --recon`
Expected: a report line with `exports mapped=` (thousands), `matches=`, `in-range=`, `clusters(runs>=3)=`, and a `GATE-PASS`/`GATE-FAIL` verdict.
- **GATE-PASS** (a contiguous in-module run exists, matches in-range) → proceed to Task 2.
- **GATE-FAIL** (no in-module table; matches heap-resident/out-of-range) → **STOP.** Record the finding in `docs/engine-re.md` (the approach can't label the dump; a launch-keyed heap dump would be needed) and end the slice. Do not build Tasks 2–4.

- [ ] **Step 6: Commit**

```bash
git add tools/frida/map_imports.py tools/frida/test_map_imports.py
git commit -m "re(frida): import-map recon — export scan + clustering gate"
```

---

## Task 2: Full mapper — write `build/import-map.json`

**Precondition:** Task 1 GATE-PASS.

**Files:**
- Create: `tools/frida/map_imports_full.py` (the writer invoked by `map_imports.py` default mode)
- Modify: `tools/frida/map_imports.py` (default mode already calls `build()`)
- Test: extend `tools/frida/test_map_imports.py`

**Interfaces:**
- Consumes: `scan_pointer_matches`, `cluster_runs` (Task 1).
- Produces: `select_table_matches(matches, runs, min_run=3) -> (table_map, singletons)` — split matches into those inside a contiguous run of ≥ `min_run` (auto-apply) vs isolated (review); and `build/import-map.json` (`{ "0xRVA": "dll!Func" }`), `build/import-map-singletons.json`.

- [ ] **Step 1: Write the failing split test**

```python
def test_select_table_matches_splits_runs_from_singletons():
    from map_imports_full import select_table_matches
    matches = [(0x100,0,"a!f"),(0x104,0,"b!g"),(0x108,0,"c!h"),(0x200,0,"d!i")]
    runs = [(0x100,3),(0x200,1)]
    table, singles = select_table_matches(matches, runs, min_run=3)
    # asserts table has the 3 run entries keyed by hex RVA, singles has the lone one
    check(set(table) == {"0x100","0x104","0x108"} and set(singles) == {"0x200"},
          "run members auto-apply; singleton set aside")
```
Add its call to `main()`. Run → FAIL (`select_table_matches` missing).

- [ ] **Step 2: Implement `select_table_matches` + `build()` in `map_imports_full.py`**

```python
import json, paths
from map_imports import scan_pointer_matches, cluster_runs   # reuse
# collect() lives in map_imports; import it too
from map_imports import collect, build_export_index

def select_table_matches(matches, runs, min_run=3):
    run_rvas = set()
    for start, count in runs:
        if count >= min_run:
            run_rvas.update(start + i*4 for i in range(count))
    table = {hex(r): n for r, _, n in matches if r in run_rvas}
    singles = {hex(r): n for r, _, n in matches if r not in run_rvas}
    return table, singles

def build():
    exports, base, mem = collect()
    idx = build_export_index(exports)
    matches = scan_pointer_matches(mem, base, idx)
    runs = cluster_runs([r for r, _, _ in matches])
    table, singles = select_table_matches(matches, runs)
    (paths.BUILD / "import-map.json").write_text(json.dumps(table, indent=2)+"\n", encoding="utf-8")
    (paths.BUILD / "import-map-singletons.json").write_text(json.dumps(singles, indent=2)+"\n", encoding="utf-8")
    print(f"wrote {len(table)} table imports -> build/import-map.json; {len(singles)} singletons set aside")
```

- [ ] **Step 3: Run unit tests → PASS**

Run: `py -3.11 -X utf8 tools/frida/test_map_imports.py`
Expected: PASS (5 tests).

- [ ] **Step 4: Generate the real map (live) and sanity-check**

Run: `py -3.11 -u -X utf8 tools/frida/map_imports.py`
Expected: `build/import-map.json` written with a sane count (dozens+); spot-check that recognizable APIs appear (e.g. a `kernel32.dll!ReadFile`, `winmm.dll!timeGetTime`, `d3d9.dll!*`). If the count is ~17 or clearly noise, revisit the run-selection `min_run` / range filter before proceeding.

- [ ] **Step 5: Commit**

```bash
git add tools/frida/map_imports_full.py tools/frida/test_map_imports.py
git commit -m "re(frida): write import-map.json (in-table matches; singletons set aside)"
```

---

## Task 3: Ghidra labeler — apply `imp_<dll>_<func>` labels to `/v2`

**Precondition:** Task 2 produced `build/import-map.json`.

**Files:**
- No new Python — driven via ghidra-mcp `run_script_inline` (Java), like Task A.

- [ ] **Step 1: Confirm the correct program is active (two-program gotcha)**

Via MCP `get_current_program_info` (or a probe script): assert base `0x400000` / 4308 functions. If not, `open_program /v2/range_00400000.bin` + `switch_program`.
Expected: active program path `/v2/range_00400000.bin`.

- [ ] **Step 2: Dry-run report — how many labels, how many collide**

`run_script_inline` (Java): read `build/import-map.json`, for each `RVA → name` compute `addr = 0x400000 + RVA`; report counts: would-create vs already-has-a-USER_DEFINED-symbol. Do not mutate.

```java
import java.nio.file.*; import java.util.*;
import ghidra.program.model.symbol.*;
String txt = new String(Files.readAllBytes(Paths.get("S:/Game Hacking/Eushully/Himegari/age-reimpl/build/import-map.json")));
java.util.regex.Matcher m = java.util.regex.Pattern
    .compile("\"0x([0-9a-fA-F]+)\":\\s*\"([^\"]+)\"").matcher(txt);
int create=0, collide=0, total=0;
SymbolTable st = currentProgram.getSymbolTable();
while (m.find()) {
    total++;
    long rva = Long.parseLong(m.group(1),16);
    ghidra.program.model.address.Address a = toAddr(0x400000L + rva);
    Symbol s = st.getPrimarySymbol(a);
    if (s != null && s.getSource()==SourceType.USER_DEFINED) collide++; else create++;
}
println("total="+total+" wouldCreate="+create+" collideUserDefined="+collide);
```
Expected: `wouldCreate` ≈ total; `collide` small (only if an import RVA overlaps an existing user symbol).

- [ ] **Step 3: Apply the labels (mutate + save)**

`run_script_inline` (Java) in one transaction: for each `RVA → "dll!Func"`, `sanitize` → `imp_<dll>_<func>` (dll without extension, non-alnum → `_`); skip if a `USER_DEFINED` symbol already exists at the addr; else `createLabel(addr, name, true)` (or `st.createLabel(addr, name, SourceType.USER_DEFINED)`). Print applied/skipped counts. Then `save_program`.

```java
// name sanitize: "kernel32.dll!ReadFile" -> "imp_kernel32_ReadFile"
String raw = m.group(2);                       // dll!Func
String dll = raw.substring(0, raw.indexOf('!')).replaceAll("\\.[^.]*$","");
String fn  = raw.substring(raw.indexOf('!')+1);
String nm  = ("imp_"+dll+"_"+fn).replaceAll("[^A-Za-z0-9_]","_");
```
Expected: applied ≈ `wouldCreate`; 0 clobbers; save succeeds.

- [ ] **Step 4: Commit (docs only — image saved in Ghidra project)**

*(No repo files change here; the Ghidra image lives in the project, not git. Proceed to Task 4 for the doc commit.)*

---

## Task 4: Validate, document, record

**Files:**
- Modify: `docs/engine-re.md` (mechanism + result under the IAT-reconstruction runbook entry)
- Modify: `docs/tools-reference.md` (add `map_imports.py`)
- Modify: `~/.claude/…/memory/himegari-port-status.md` (milestone)

- [ ] **Step 1: Validate against known hand-identified APIs**

Via MCP: decompile/inspect the call sites we already RE'd and confirm they now read named:
- `FUN_0044f390` / `FUN_0040e980` (resolver chain) → `CreateFileA` / `SetFilePointer` / `ReadFile`.
- `sleep_op_0xc8` → its `DAT_0056f3d4` timer source → a `timeGetTime`/`GetTickCount`-class import.
- A `d3d9` `Present` / `Direct3DCreate9` site.
Record which anchors resolved (need ≥2 of 3 for acceptance).

- [ ] **Step 2: Update docs**

`docs/engine-re.md`: in the runbook's "the right approach (queued, Frida-based)" note, replace "queued" with the result — mechanism confirmed, N labels applied, validated anchors, tool name. `docs/tools-reference.md`: add a `map_imports.py` row under "Runtime capture (Frida)".

- [ ] **Step 3: Update the status memory**

Add a milestone entry: Frida import-map DONE (or GATE-FAIL finding), N imports labeled, validated anchors, tool + map paths.

- [ ] **Step 4: Commit**

```bash
git add docs/engine-re.md docs/tools-reference.md
git commit -m "re(frida): import-map applied to /v2 — N Win32 APIs named at call sites; validated"
```

---

## Self-Review

**Spec coverage:** recon gate → Task 1 (Steps 5–6, explicit STOP on GATE-FAIL); export map + scan → Task 1 Step 3; `import-map.json` + singleton split → Task 2; clean labels on `/v2` (no type archive) → Task 3; ASLR-safe (same-process map+scan, RVA-only) → Global Constraints + `collect()`; validation anchors → Task 4 Step 1; docs/memory → Task 4. All covered.

**Placeholder scan:** the Frida `attach()`/JS is concrete but flagged "mirror/reuse `dump_engine.py`" — intentional (match the existing frida helper rather than duplicate); not a hidden TODO. No TBDs.

**Type consistency:** `scan_pointer_matches` returns `(rva, value, name)` and every consumer (`recon`, `build`, `select_table_matches`) unpacks that triple; `cluster_runs` returns `(start_rva, count)` used identically in `recon`/`select_table_matches`; `build/import-map.json` is `{hex_rva: "dll!func"}` produced by `build()` and consumed by the Task 3 regex `"0x..":"..!.."`. Consistent.
