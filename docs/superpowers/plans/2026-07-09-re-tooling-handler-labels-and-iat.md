# RE Tooling: Handler Labeling + IAT Reconstruction — Implementation Plan

> **STATUS (2026-07-09):** **Task A COMPLETE** (committed `97fb1d6` on `feat/re-tooling-handler-labels`) —
> whole-image dispatch-handler labeling landed; see the status memory + `docs/engine-re.md` "Materialized +
> applied image-wide". **Task B (pe-sieve IAT graft) ABANDONED — the premise fails on this packed binary**
> (zeroed IAT, `GetProcAddress`-resolved imports; pe-sieve produced ~17 genuine + 300+ spurious entries).
> See `docs/engine-re.md` runbook "IAT reconstruction — tried, DOESN'T WORK". **Task B is re-scoped to a
> Frida live import-map** (build `{runtime_addr→dll!Func}` from the live process's module exports, read the
> engine's resolved import-pointer storage → `RVA→name`, label the `/v2` image) — that gets its OWN
> spec/plan when picked up; the pe-sieve Task B below is retained only as the record of what was tried.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate two recurring native-RE taxes — Kelebek VA-drift and unnamed imports — by (A) auto-labeling every opcode dispatch handler in the Ghidra image and (B) grafting reconstructed IAT names onto that same image.

**Architecture:** Two independent workstreams against the existing annotated Ghidra program (`range_00400000.bin`, base `0x400000`), driven over the ghidra-mcp bridge. A is fully static (no running game). B needs a live `AGE.EXE` for `pe-sieve32`. A pure-Python parser does the mechanical work in both; MCP calls apply the results to the image; annotations are always preserved.

**Tech Stack:** Python 3.11 (`py -3.11 -X utf8`), ghidra-mcp bridge (Ghidra 12.1.2), `bin/pe-sieve32.exe`, `pytest`.

## Global Constraints

- Run Python as `py -3.11 -X utf8 tools/<name>.py …` (utf8 mandatory on Windows).
- Tools import `tools/paths.py`; never hard-code paths.
- Engine image: Raw Binary, `x86:LE:32:default`, Image Base `0x400000`; VA→file offset = `VA − 0x400000`.
- Dispatch identity: `handler(op) = ctx[0x26c93 + op] = *(ctx + 0x9b24c + op*4)`; overrides written in `FUN_00413860`; default handler = `FUN_004162b0`.
- **Never hand-edit generated files** (`build/*`); rebuild via the owning tool.
- **Preserve annotations** — never reimport the program in B; never rename an already-named handler in A.
- End every image-mutating task with `save_program` (MCP `mcp__ghidra__save_program`).
- `pe-sieve32.exe` runs from **PowerShell** (Git Bash mangles `/flags`).
- Deferred / out of scope: outer-loop RE (wider dump / live debugger), STL/CRT demangling.

---

## Task A: Auto-label every dispatch handler

**Files:**
- Create: `tools/ghidra_handler_map.py` (parser + JSON emitter + opcodes.toml cross-check)
- Create: `tools/test_ghidra_handler_map.py` (unit test for the parser)
- Create: `build/op-handler-map.json` (generated — do not hand-edit)
- Create: `scratchpad` fixture `FUN_00413860.disasm.txt` (captured disassembly, input to the parser)
- Modify: `docs/engine-re.md` (dispatch-table section links the generated map)
- Modify: `docs/tools-reference.md` (document `ghidra_handler_map.py`)

**Interfaces:**
- Produces: `parse_overrides(disasm_lines: list[str]) -> dict[int, int]` mapping `op → handler_va`, excluding the default handler `0x4162b0`.
- Produces: `build/op-handler-map.json` shape `{ "0x1ac": {"handler": "0x427fb0"}, ... }`.
- Consumes (Step A5): `vm-map/opcodes.toml` handler VAs for cross-check.

- [ ] **Step A1: Capture `FUN_00413860` disassembly as a parser fixture**

Via MCP against the open program, dump the registration routine's instructions to a fixture file:
Run: `mcp__ghidra__disassemble_function` with `address="0x413860"` (or `get_function_pcode` if disasm is cleaner), save raw text to `scratchpad/FUN_00413860.disasm.txt`.
Expected: a long list of `MOV dword ptr [<reg> + 0x9b...], 0x4?????` stores plus a default-fill loop.

- [ ] **Step A2: Write the failing parser test**

```python
# tools/test_ghidra_handler_map.py
from ghidra_handler_map import parse_overrides

def test_parses_known_override_anchors():
    # Minimal fixture lines in the exact shape captured in A1.
    lines = [
        "MOV dword ptr [EAX + 0x9b8fc],0x427fb0",   # 0x9b8fc = 0x9b24c + 0x1ac*4  -> op 0x1ac
        "MOV dword ptr [EAX + 0x9b688],0x420ec0",   # 0x9b688 = 0x9b24c + 0xc8*4   -> op 0xc8
        "MOV dword ptr [EAX + 0x100],0x4162b0",     # not in table window -> ignored
    ]
    m = parse_overrides(lines)
    assert m[0x1ac] == 0x427fb0
    assert m[0xc8] == 0x420ec0
    assert 0x4162b0 not in m.values()   # default handler excluded

def test_excludes_default_handler_writes():
    lines = ["MOV dword ptr [EAX + 0x9b24c],0x4162b0"]  # op 0x0 default fill
    assert parse_overrides(lines) == {}
```

- [ ] **Step A3: Run the test to verify it fails**

Run: `py -3.11 -X utf8 -m pytest tools/test_ghidra_handler_map.py -v`
Expected: FAIL — `ModuleNotFoundError`/`cannot import name 'parse_overrides'`.

- [ ] **Step A4: Implement the parser**

```python
# tools/ghidra_handler_map.py
import json, re, sys
import paths  # path anchor

TABLE_BASE = 0x9b24c            # byte offset of ctx[0x26c93]
TABLE_SPAN = 0x400 * 4         # 1024 word slots
DEFAULT_HANDLER = 0x4162b0

_STORE = re.compile(
    r"MOV\s+dword ptr\s+\[\w+\s*\+\s*(0x[0-9a-fA-F]+)\]\s*,\s*(0x[0-9a-fA-F]+)")

def parse_overrides(lines):
    out = {}
    for ln in lines:
        m = _STORE.search(ln)
        if not m:
            continue
        disp = int(m.group(1), 16)
        va = int(m.group(2), 16)
        if not (TABLE_BASE <= disp < TABLE_BASE + TABLE_SPAN):
            continue
        if (disp - TABLE_BASE) % 4 != 0:
            continue
        op = (disp - TABLE_BASE) // 4
        if va == DEFAULT_HANDLER:
            continue
        out[op] = va
    return out

def build(disasm_path, out_path):
    lines = disasm_path.read_text(encoding="utf-8").splitlines()
    m = parse_overrides(lines)
    data = {hex(op): {"handler": hex(va)} for op, va in sorted(m.items())}
    out_path.write_text(json.dumps(data, indent=2), encoding="utf-8")
    return m

if __name__ == "__main__":
    from pathlib import Path
    disasm = Path(sys.argv[1])
    out = paths.BUILD / "op-handler-map.json"
    m = build(disasm, out)
    print(f"{len(m)} overrides -> {out}")
```

- [ ] **Step A5: Run the test to verify it passes**

Run: `py -3.11 -X utf8 -m pytest tools/test_ghidra_handler_map.py -v`
Expected: PASS (both tests).

- [ ] **Step A6: Generate the real map and verify anchors**

Run: `py -3.11 -X utf8 tools/ghidra_handler_map.py scratchpad/FUN_00413860.disasm.txt`
Then inspect `build/op-handler-map.json` and assert the four known anchors:
- `0x1ac → 0x427fb0`, `0x1a2 → 0x42d360`, `0x215 → 0x42a0b0`, `0xc8 → 0x420ec0`.
Expected: all four present and exact; total override count is reported and plausible (record it in the doc). If any anchor is missing, the A1 capture missed stores (e.g. non-`MOV`/computed writes) — re-capture with `get_function_pcode` and widen the regex before proceeding.

- [ ] **Step A7: Cross-check against `opcodes.toml` (report only, no auto-edit)**

Extend `ghidra_handler_map.py` with a `--check` mode that loads each opcode's recorded handler VA from `vm-map/opcodes.toml` (where present) and prints every op where `toml_handler != derived_handler`.
Run: `py -3.11 -X utf8 tools/ghidra_handler_map.py scratchpad/FUN_00413860.disasm.txt --check`
Expected: a (possibly empty) disagreement list. **Do not edit opcodes.toml here** — capture the list in the task notes for human reconciliation (each disagreement is a latent VA-drift bug).

- [ ] **Step A8: Apply annotations to the image (preserve good names)**

For each `(op, va)` in the map, drive MCP:
1. `get_function_by_address(va)` — read the current name.
2. If the name is missing or matches `^(FUN_|LAB_)` → `create_function` (if absent) then `rename_function_by_address(va, f"op_0x{op:x}_handler")`.
3. If the name is already descriptive (e.g. `gfx_op_*`, `sleep_op_*`, `call_script*`) → **do not rename**.
4. Either way `set_plate_comment(va, f"opcode 0x{op:x} dispatch handler; ctx[0x26c93+op] in FUN_00413860")` — appending, not replacing an existing decode comment.
Expected: every raw handler gets an `op_0xNN_handler` name; every previously-named handler keeps its name.

- [ ] **Step A9: Verify no clobbers, then save**

Re-read the four anchors and 3 previously-named handlers via `get_function_by_address`; confirm named ones are unchanged and raw ones now carry `op_0x…`.
Run: `mcp__ghidra__save_program`.
Expected: save succeeds; spot-checks pass.

- [ ] **Step A10: Update docs and commit**

Edit `docs/engine-re.md` dispatch-table section to reference `build/op-handler-map.json` (the full op→handler map, generated) instead of listing ops by hand; note the total override count and any A7 disagreements. Add `ghidra_handler_map.py` to `docs/tools-reference.md`.

```bash
git add tools/ghidra_handler_map.py tools/test_ghidra_handler_map.py build/op-handler-map.json docs/engine-re.md docs/tools-reference.md
git commit -m "re: auto-label all opcode dispatch handlers from FUN_00413860"
```

---

## Task B: Reconstruct the IAT and graft named imports

**Prerequisite:** a running `AGE.EXE` instance (needed by `pe-sieve32`).

**Files:**
- Create: `tools/apply_imports.py` (parse pe-sieve import report → `{thunk_addr: "dll!Func"}`)
- Create: `tools/test_apply_imports.py` (unit test for the report parser)
- Create: `build/pe-sieve/` (pe-sieve output dir — reconstructed module + import report)
- Modify: `docs/engine-re.md` (IAT-reconstruction runbook + fallback usage, if taken)
- Modify: `docs/tools-reference.md` (document `apply_imports.py`)

**Interfaces:**
- Consumes: pe-sieve's import report text.
- Produces: `parse_import_report(text: str) -> dict[int, str]` mapping `thunk_va → "dll!Function"`.

- [ ] **Step B1: Run pe-sieve against the live game**

Find the PID, then from **PowerShell**:
```powershell
$p = Get-Process AGE
bin\pe-sieve32.exe /pid $p.Id /imp 3 /dmode 3 /dir build\pe-sieve
```
Expected: `build/pe-sieve/` contains the reconstructed module and an import report (`*.imports.txt`/tag file). Note the report's exact format for B2.

- [ ] **Step B2: Write the failing import-report parser test**

```python
# tools/test_apply_imports.py
from apply_imports import parse_import_report

def test_parses_thunk_to_named_import():
    # Fixture lines in the EXACT shape observed in B1 (adjust to pe-sieve's real format).
    text = "\n".join([
        "0x00456120: kernel32.dll.CreateFileA",
        "0x00456124: kernel32.dll.ReadFile",
    ])
    m = parse_import_report(text)
    assert m[0x456120] == "kernel32.dll!CreateFileA"
    assert m[0x456124] == "kernel32.dll!ReadFile"
```

- [ ] **Step B3: Run the test to verify it fails**

Run: `py -3.11 -X utf8 -m pytest tools/test_apply_imports.py -v`
Expected: FAIL — import error.

- [ ] **Step B4: Implement the report parser**

Implement `parse_import_report(text)` in `tools/apply_imports.py` to match the real B1 format (the fixture above is a placeholder shape — reconcile the regex to pe-sieve's actual columns). Return `{thunk_va: "dll!Func"}`.
Run: `py -3.11 -X utf8 -m pytest tools/test_apply_imports.py -v`
Expected: PASS.

- [ ] **Step B5: Graft names onto the existing program**

For each `(thunk_va, "dll!Func")`, drive MCP against the current annotated program (do **not** reimport): apply the name at the IAT thunk via the appropriate label/reference/external-location API (try `create_label`; if the thunk should be an external function, use the external-location API). Verify one call site reads as a named call:
- Save-path handler (`0x1ac` region) → named `CreateFileA`/`SetFilePointer`.
Expected: at least the save/resolver-chain sites (`FUN_0040e980`/`FUN_0044f390`) show named `CreateFileA`/`SetFilePointer`/`ReadFile`; the `sleep` timer source shows a `timeGetTime`-class name.

- [ ] **Step B6: Fallback branch (only if B5 is an ordeal)**

If thunk addresses don't line up or MCP can't set externals cleanly, stop grafting: import `build/pe-sieve/<module>` as a **second, clean Ghidra program** (Format PE), keep it open alongside ours, and use it purely as a name-lookup reference — port import names into our annotated program by hand as each function is touched. Document the two-program workflow in `docs/engine-re.md`'s runbook. (This preserves all annotations either way.)

- [ ] **Step B7: Apply the Win32 data-type archive**

Apply Ghidra's bundled Win32 type archive so named imports carry signatures (via the Data Type Manager / MCP `import_data_types` if exposed). Verify `CreateFileA` shows a typed prototype at a call site.
Expected: named imports carry parameter types.

- [ ] **Step B8: Save, verify annotations intact, update docs, commit**

Confirm a sample of pre-existing renames/plate comments still present (`get_function_by_address` on `gfx_op_0x215_register_query`, `sleep_op_0xc8`). Run `mcp__ghidra__save_program`.
Edit `docs/engine-re.md` (runbook: IAT reconstruction is done; note graft vs fallback) and `docs/tools-reference.md` (`apply_imports.py`).

```bash
git add tools/apply_imports.py tools/test_apply_imports.py docs/engine-re.md docs/tools-reference.md
git commit -m "re: reconstruct IAT via pe-sieve and graft named imports"
```

---

## Self-Review

**Spec coverage:**
- A (handler labeling): Task A1–A10 — parser, cross-check (A7), preserve-names (A8), anchors (A6), docs (A10). ✓
- B (IAT graft + fallback + type lib): B1–B8, fallback = B6, type archive = B7. ✓
- Sequencing (A first, static; B needs game): task order + B prerequisite. ✓
- Deferred outer-loop RE: Global Constraints. ✓

**Placeholder scan:** The B4/B2 note "reconcile regex to pe-sieve's actual format" is intentional — the exact report columns are only knowable after B1 runs; the fixture shows the target shape and B4 adjusts it. Not a hidden TODO. No other placeholders.

**Type consistency:** `parse_overrides(list[str]) -> dict[int,int]` and `parse_import_report(str) -> dict[int,str]` are each used consistently by their `build`/graft consumers. Handler-name pattern `op_0xNN_handler` used identically in A8/A9.
