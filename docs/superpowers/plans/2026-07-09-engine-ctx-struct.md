# EngineCtx Struct — Implementation Plan

**Status:** completed 2026-07-09 in `3826064` and `0ecd776`. This is the archived execution plan;
checkboxes below are preserved as originally authored.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Type the engine context as a Ghidra `EngineCtx` struct sourced from a canonical `vm-map/engine-ctx.toml`, and retype the dispatch handlers' `this` so they decompile `ctx->field` instead of magic offsets.

**Architecture:** Single source of truth (`engine-ctx.toml`) → generator (`engine_ctx_build.py`) emits `build/engine-ctx.json` + `docs/engine-ctx-reference.md` → a `run_script_inline` pass builds the `EngineCtx` struct and retypes handler `this` params on `/v2`. Pure model/lint/emit logic is unit-tested; the applied result is validated by decompiling two handlers.

**Tech Stack:** Python 3.11 (`py -3.11 -X utf8`, `tomllib`), ghidra-mcp `run_script_inline` (Java; `GHIDRA_MCP_ALLOW_SCRIPTS=1`).

**Spec:** `docs/superpowers/specs/2026-07-09-engine-ctx-struct-design.md`.

## Global Constraints

- Run Python as `py -3.11 -X utf8 tools/<name>.py …`. Tools compute paths via `paths.py`; `build/` is disposable.
- **Never hand-edit generated files** (`build/engine-ctx.json`, `docs/engine-ctx-reference.md`): edit `engine-ctx.toml`, re-run `--build`.
- Scope = engine `ctx` (esi) fields ONLY; the VM global bank `G[…]` is a separate space (`globals.toml`), out of scope.
- **Ghidra:** confirm the active program is `/v2/range_00400000.bin` (base `0x400000`, 4400+ fns — the two-program gotcha) before any script; end mutation with `save_program`.
- Seed fields are 4-byte scalars (`int`/`uint`/`void*`) — names the offset without span-management; arrays/sub-structs are a later refinement.

---

## Task 1: Canonical source + generator (`engine-ctx.toml` → JSON + doc)

**Files:**
- Create: `vm-map/engine-ctx.toml` (canonical source)
- Create: `tools/engine_ctx_build.py` (model + lint + emit + CLI)
- Create: `tools/test_engine_ctx.py` (unit tests for lint + emit)
- Create: `build/engine-ctx.json` (generated), `docs/engine-ctx-reference.md` (generated)

**Interfaces:**
- Produces: `load(toml_text) -> dict` (`{"meta": {...}, "fields": [{"offset":int,"name","type","note"}...]}`); `lint(model) -> list[str]` (errors); `emit_json(model) -> dict`; `emit_reference_md(model) -> str`.
- `TYPE_SIZES = {"int":4, "uint":4, "void*":4}` (all seed fields 4 bytes).

- [ ] **Step 1: Create the canonical source `vm-map/engine-ctx.toml`**

```toml
# vm-map/engine-ctx.toml -- CANONICAL source for the EngineCtx struct (hand-edited).
# Generated: build/engine-ctx.json + docs/engine-ctx-reference.md via tools/engine_ctx_build.py --build.
# Applied to the Ghidra /v2 image via run_script_inline (see the plan). ctx = engine context (esi/thiscall this).
# The VM global bank G[...] is a SEPARATE space (vm-map/globals.toml), NOT ctx offsets.
[meta]
struct_name = "EngineCtx"
size = 0xa1000

[[field]]
offset = 0x3028
name = "alt_pack_table"
type = "int"
note = "call-script high-byte alternate pack table (unused by corpus)"
[[field]]
offset = 0x408
name = "gfx_obj_registry"
type = "int"
note = "retained gfx-object map (std::map handle->object); geometry/draw get-or-create, 0x215 returns obj+4 source slot, 0x1f7 erases"
[[field]]
offset = 0x40c
name = "sys4ini_count"
type = "int"
note = "SYS4INI record count"
[[field]]
offset = 0x410
name = "archive_name_table"
type = "void*"
note = "archive-name table base (arc_id*0x100 indexes it)"
[[field]]
offset = 0x414
name = "sys4ini_records"
type = "void*"
note = "SYS4INI 80-byte record base {name[64],arc_id,file_number,offset,size}; record = base + id*0x50"
[[field]]
offset = 0xb558
name = "gfx_dirty_a"
type = "int"
note = "gfx dirty flag (anim set raises)"
[[field]]
offset = 0xb560
name = "gfx_dirty_b"
type = "int"
note = "gfx dirty flag"
[[field]]
offset = 0x14d54
name = "gfx_obj_ptr_table"
type = "void*"
note = "per-object pointer table (ops 0x212/0x213 write obj+0x64/0x68/0x6c)"
[[field]]
offset = 0x14f45
name = "script_frame_index"
type = "int"
note = "call-script frame index (0x1e-dword frames)"
[[field]]
offset = 0x46d14
name = "query_table_46d14"
type = "void*"
note = "stride-0x14 table read by op 0x216"
[[field]]
offset = 0x51b64
name = "frame_timer"
type = "int"
note = "frame timer (present updates 0x51b64/0x51b68)"
[[field]]
offset = 0x51b78
name = "anim_clock_elapsed"
type = "int"
note = "global anim clock elapsed (op 0x238 zeroes)"
[[field]]
offset = 0x51b7c
name = "anim_clock_duration"
type = "int"
note = "global anim clock total duration (op 0x238 sets)"
[[field]]
offset = 0x52bd4
name = "surfaces"
type = "void*"
note = "surface array base [~1000 slots]; create/set-texture (0x1f8/0x1f9) allocate"
[[field]]
offset = 0x53d14
name = "cur_ctx_index"
type = "uint"
note = "current gfx-object / script-context index (curCtx); indexes 0x78-byte records"
[[field]]
offset = 0x53d28
name = "frame_codebase"
type = "void*"
note = "current frame codebase (PC = codebase + off*4)"
[[field]]
offset = 0x53d2c
name = "frame_pc"
type = "int"
note = "current frame PC column (op = *(0x53d2c + curCtx*0x78))"
[[field]]
offset = 0x53d60
name = "ctx_record_base"
type = "void*"
note = "0x78-byte context-record array base (coroutine/script contexts)"
[[field]]
offset = 0x53d64
name = "gfx_obj_record_array"
type = "void*"
note = "gfx object-record array (field[0]=0xffffffff free; cmd-type at rec+0x24)"
[[field]]
offset = 0x53d88
name = "cmd_type_table"
type = "int"
note = "per-object cmd-type column base (write *(0x53d88 + curCtx*0x78))"
[[field]]
offset = 0x55120
name = "anti_tamper_a"
type = "int"
note = "anti-tamper checksum operand"
[[field]]
offset = 0x55124
name = "anti_tamper_b"
type = "int"
note = "anti-tamper checksum operand"
[[field]]
offset = 0x5512c
name = "anti_tamper_fp"
type = "int"
note = "anti-tamper (import fn ptr / result)"
[[field]]
offset = 0x55248
name = "ret_stack_a"
type = "void*"
note = "per-frame return stack (op 0x8f call pushes)"
[[field]]
offset = 0x552e8
name = "ret_stack_b"
type = "void*"
note = "per-frame return stack (companion)"
[[field]]
offset = 0x5f304
name = "sleep_timer"
type = "int"
note = "sleep timer object (op 0xc8; +8 active, +0x14 start-ms, +0x18 duration)"
[[field]]
offset = 0x6da88
name = "coroutine_yield_a"
type = "void*"
note = "op 0x7b yield-state save (op1 -> +ctxidx*4)"
[[field]]
offset = 0x6db28
name = "coroutine_yield_b"
type = "void*"
note = "op 0x7b yield-state save (op2 -> +ctxidx*4)"
[[field]]
offset = 0x6dbc8
name = "coroutine_runstate"
type = "int"
note = "op 0x7c resume gate (run-state bit 0x2000000)"
[[field]]
offset = 0x6dbcc
name = "coroutine_resume_off"
type = "int"
note = "op 0x7c resume PC offset"
[[field]]
offset = 0x9b24c
name = "dispatch_table"
type = "void*"
note = "opcode->handler table base [0x400]; handler(op) = *(0x9b24c + op*4)"
[[field]]
offset = 0xa0cc0
name = "screen_w"
type = "int"
note = "screen width (640)"
[[field]]
offset = 0xa0cc4
name = "screen_h"
type = "int"
note = "screen height (480)"
[[field]]
offset = 0xa0cc8
name = "screen_bpp"
type = "int"
note = "screen bpp (8)"
[[field]]
offset = 0xa0ce4
name = "run_state_flags"
type = "uint"
note = "interpreter run-state flags (bit1 sleeping; 0x8000000 skip/fast-forward)"
```

- [ ] **Step 2: Write the failing tests**

```python
# tools/test_engine_ctx.py  (plain runner, no pytest)
import sys
from engine_ctx_build import load, lint, emit_json

FAILS = []
def check(c, m):
    (FAILS.append(m) or print("FAIL:", m)) if not c else print("ok:", m)

GOOD = '[meta]\nstruct_name="EngineCtx"\nsize=0x1000\n' \
       '[[field]]\noffset=0x10\nname="a"\ntype="int"\nnote="x"\n' \
       '[[field]]\noffset=0x20\nname="b"\ntype="void*"\nnote="y"\n'

def test_load_and_emit():
    m = load(GOOD)
    j = emit_json(m)
    check(j["fields"]["0x10"]["name"] == "a" and j["meta"]["struct_name"] == "EngineCtx",
          "emit_json keys fields by hex offset + carries meta")

def test_lint_clean():
    check(lint(load(GOOD)) == [], "clean model lints with no errors")

def test_lint_catches_overlap():
    bad = '[meta]\nstruct_name="E"\nsize=0x1000\n' \
          '[[field]]\noffset=0x10\nname="a"\ntype="int"\nnote=""\n' \
          '[[field]]\noffset=0x12\nname="b"\ntype="int"\nnote=""\n'   # 0x10+4 > 0x12 -> overlap
    check(any("overlap" in e.lower() for e in lint(load(bad))), "lint flags overlapping fields")

def test_lint_catches_oob_and_dupname():
    bad = '[meta]\nstruct_name="E"\nsize=0x14\n' \
          '[[field]]\noffset=0x10\nname="a"\ntype="int"\nnote=""\n' \
          '[[field]]\noffset=0x40\nname="a"\ntype="int"\nnote=""\n'   # 0x40 > size AND dup name
    errs = lint(load(bad))
    check(any("out of bounds" in e.lower() for e in errs) and any("duplicate" in e.lower() for e in errs),
          "lint flags out-of-bounds offset and duplicate name")

def main():
    test_load_and_emit(); test_lint_clean(); test_lint_catches_overlap(); test_lint_catches_oob_and_dupname()
    print("FAILURES:", len(FAILS)); return 1 if FAILS else 0

if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `py -3.11 -X utf8 tools/test_engine_ctx.py`
Expected: FAIL — `ModuleNotFoundError: No module named 'engine_ctx_build'`.

- [ ] **Step 4: Implement `tools/engine_ctx_build.py`**

```python
"""Build the EngineCtx struct artifacts from vm-map/engine-ctx.toml (single source of truth).

  py -3.11 -X utf8 tools/engine_ctx_build.py --build   # -> build/engine-ctx.json + docs/engine-ctx-reference.md
  py -3.11 -X utf8 tools/engine_ctx_build.py --lint     # checks only

Apply to the Ghidra /v2 image via run_script_inline (see docs/superpowers/plans/2026-07-09-engine-ctx-struct.md).
"""
import json, sys, tomllib
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
TYPE_SIZES = {"int": 4, "uint": 4, "void*": 4}


def load(toml_text):
    d = tomllib.loads(toml_text)
    fields = [{"offset": int(f["offset"]), "name": f["name"], "type": f["type"], "note": f.get("note", "")}
              for f in d.get("field", [])]
    fields.sort(key=lambda f: f["offset"])
    return {"meta": d["meta"], "fields": fields}


def lint(model):
    errs, size = [], int(model["meta"]["size"])
    seen_names, prev = {}, None
    for f in model["fields"]:
        sz = TYPE_SIZES.get(f["type"])
        if sz is None:
            errs.append(f"unknown type {f['type']!r} for {f['name']}")
            sz = 4
        if f["offset"] + sz > size:
            errs.append(f"field {f['name']} @0x{f['offset']:x} out of bounds (size 0x{size:x})")
        if f["name"] in seen_names:
            errs.append(f"duplicate name {f['name']!r}")
        seen_names[f["name"]] = True
        if prev is not None and f["offset"] < prev["end"]:
            errs.append(f"overlap: {f['name']} @0x{f['offset']:x} into {prev['name']} (ends 0x{prev['end']:x})")
        prev = {"name": f["name"], "end": f["offset"] + sz}
    return errs


def emit_json(model):
    return {"meta": model["meta"],
            "fields": {hex(f["offset"]): {"name": f["name"], "type": f["type"]} for f in model["fields"]}}


def emit_reference_md(model):
    lines = ["# EngineCtx field reference", "",
             "> Generated from `vm-map/engine-ctx.toml` by `tools/engine_ctx_build.py --build`. Do not edit.",
             "", f"Struct `{model['meta']['struct_name']}`, size `0x{int(model['meta']['size']):x}`.", "",
             "| offset | name | type | note |", "|---|---|---|---|"]
    for f in model["fields"]:
        lines.append(f"| `0x{f['offset']:x}` | `{f['name']}` | `{f['type']}` | {f['note']} |")
    return "\n".join(lines) + "\n"


def main():
    text = (REPO / "vm-map" / "engine-ctx.toml").read_text(encoding="utf-8")
    model = load(text)
    errs = lint(model)
    if errs:
        print("LINT ERRORS:")
        for e in errs:
            print("  " + e)
        return 1
    if "--lint" in sys.argv[1:]:
        print(f"lint clean: {len(model['fields'])} fields")
        return 0
    if "--build" in sys.argv[1:]:
        (REPO / "build" / "engine-ctx.json").write_text(json.dumps(emit_json(model), indent=2) + "\n", encoding="utf-8")
        (REPO / "docs" / "engine-ctx-reference.md").write_text(emit_reference_md(model), encoding="utf-8")
        print(f"built {len(model['fields'])} fields -> build/engine-ctx.json + docs/engine-ctx-reference.md")
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `py -3.11 -X utf8 tools/test_engine_ctx.py`
Expected: PASS (4 tests, `FAILURES: 0`).

- [ ] **Step 6: Build the real artifacts and lint-check the seed**

Run: `py -3.11 -X utf8 tools/engine_ctx_build.py --build`
Expected: `built 35 fields -> build/engine-ctx.json + docs/engine-ctx-reference.md`, no lint errors. Eyeball `docs/engine-ctx-reference.md` for the field table.

- [ ] **Step 7: Commit**

```bash
git add vm-map/engine-ctx.toml tools/engine_ctx_build.py tools/test_engine_ctx.py docs/engine-ctx-reference.md
git commit -m "re: EngineCtx field registry (engine-ctx.toml) + builder"
```

---

## Task 2: Apply the struct + retype handlers in Ghidra, validate

**Precondition:** `build/engine-ctx.json` + `build/op-handler-map.json` exist; active program `/v2`.

**Files:** none in-repo — driven via `run_script_inline` (Java).

- [ ] **Step 1: Confirm the correct program + capture BEFORE snippet**

Via MCP `get_current_program_info` — assert base `0x400000`, 4400+ fns (else `open_program`/`switch_program /v2/range_00400000.bin`). Then `decompile_function 0x44cff0` (`sleep_timer_arm`) and note the current `(*DAT_...)` / `param_1 + 0x5f304` rendering for the before/after.
Expected: confirmed program + a BEFORE snippet showing raw offsets.

- [ ] **Step 2: Create the `EngineCtx` struct from `build/engine-ctx.json`**

`run_script_inline` (Java): read the JSON; get/create a `StructureDataType("EngineCtx", size)` in the program's data type manager; for each `offset → {name,type}` map `type` to a Ghidra `DataType` (`int`/`uint` → `IntegerDataType`/`UnsignedIntegerDataType`; `void*` → `PointerDataType`) and `struct.replaceAtOffset(offset, dt, dt.getLength(), name, note)`; add via `dataTypeManager.addDataType(struct, REPLACE_HANDLER)`. One transaction. Print field count placed.

```java
import java.nio.file.*; import ghidra.program.model.data.*;
String txt = new String(Files.readAllBytes(Paths.get("S:/Game Hacking/Eushully/Himegari/age-reimpl/build/engine-ctx.json")));
// parse meta.size + each "0xoff":{"name":..,"type":..} with a regex; build the struct.
// map type: "void*" -> new PointerDataType(); "uint" -> UnsignedIntegerDataType.dataType; else IntegerDataType.dataType
// struct.replaceAtOffset(off, dt, 4, name, note);
```
Expected: `EngineCtx` struct created with 35 fields.

- [ ] **Step 3: Retype dispatch-handler `this` to `EngineCtx *`**

`run_script_inline` (Java): read `build/op-handler-map.json`; resolve `EngineCtx *` (`new PointerDataType(struct)`); for each handler VA, get the `Function`; if it has a `this`/first param whose usage is `ctx` (thiscall or first param), set that param's type to `EngineCtx *` (`func.getParameter(0).setDataType(ptr, SourceType.USER_DEFINED)`, or `func.setCallingConvention("__thiscall")` + this-type where needed). Count retyped; collect + print the VAs skipped (no first param / not ctx-shaped) for review. One transaction; `save_program`.
Expected: most handlers retyped; a small skip list.

- [ ] **Step 4: Validate — AFTER snippet**

`decompile_function 0x44cff0` (`sleep_timer_arm`) and `0x42a0b0` (`gfx_op_0x215_query_source_slot`).
Expected: `sleep_timer_arm` renders `ctx->sleep_timer` (was `param_1 + 0x5f304`); the 0x215 handler renders `ctx->cur_ctx_index` / `ctx->cmd_type_table` at `0x53d14`/`0x53d88`. Record before/after. If fields don't render, diagnose the this-type application (Step 3) before proceeding.

---

## Task 3: Docs + memory + close

**Files:**
- Modify: `CLAUDE.md` (canonical-documents map + single-source-of-truth table: add the `engine-ctx.toml` row)
- Modify: `docs/tools-reference.md` (add `engine_ctx_build.py`)
- Modify: `docs/engine-re.md` (link `docs/engine-ctx-reference.md` from the ctx-offsets discussion)
- Modify: `~/.claude/…/memory/himegari-port-status.md` (milestone)

- [ ] **Step 1: Update CLAUDE.md**

Add to the canonical-documents map: `| Engine ctx struct fields | age-reimpl/vm-map/engine-ctx.toml (generated → build/engine-ctx.json, docs/engine-ctx-reference.md) |`. Add to the single-source table: `| vm-map/engine-ctx.toml | py -3.11 -X utf8 tools/engine_ctx_build.py --build | build/engine-ctx.json, docs/engine-ctx-reference.md |`.

- [ ] **Step 2: Update tools-reference.md + engine-re.md**

`tools-reference.md`: add an `engine_ctx_build.py` row (source → JSON+doc; `--build`/`--lint`; applied to Ghidra via run_script_inline). `engine-re.md`: in the dispatch/ctx-offsets area, add a line pointing to `docs/engine-ctx-reference.md` as the field map, noting the struct is applied to the `/v2` image.

- [ ] **Step 3: Update the status memory**

Record: EngineCtx struct DONE — N fields, applied to /v2 (M handlers retyped), before/after validated; source `engine-ctx.toml`; grows incrementally.

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md docs/tools-reference.md docs/engine-re.md
git commit -m "re: apply EngineCtx struct to /v2 handlers; doc + canonical-map wiring"
```

---

## Self-Review

**Spec coverage:** source `engine-ctx.toml` → Task 1 Step 1; builder + lint + JSON + doc → Task 1 Steps 4/6; struct creation → Task 2 Step 2; handler `this` retype → Task 2 Step 3; validation decompiles → Task 2 Step 4; CLAUDE.md/tools-reference/memory → Task 3. Covered.

**Placeholder scan:** Task 2 Java bodies are described with the key API calls (`replaceAtOffset`, `setDataType`, `PointerDataType`) rather than full source — intentional: the exact DataTypeManager calls are pinned at implementation against the live API (same as Task A's iterative script bring-up), and the logic is fully specified. No TBDs in the Python (Task 1 is complete code).

**Type consistency:** `load` returns `{"meta","fields":[{offset,name,type,note}]}`; `lint`/`emit_json`/`emit_reference_md` all consume that shape; `emit_json` outputs `{meta, fields:{hex:{name,type}}}` which the Task 2 Step 2 Ghidra parser reads. `TYPE_SIZES` keys (`int`/`uint`/`void*`) match the toml `type` values and the Task 2 Ghidra type mapping. Consistent.
