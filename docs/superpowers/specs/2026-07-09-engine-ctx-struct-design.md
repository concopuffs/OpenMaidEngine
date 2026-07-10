# EngineCtx struct — typing the engine context — design

**Date:** 2026-07-09
**Status:** implemented 2026-07-09 (`3826064`, `0ecd776`)
**Home in the canonical map:** a NEW canonical source (`vm-map/engine-ctx.toml`) + a generated
reference (`docs/engine-ctx-reference.md`); add a row to the CLAUDE.md canonical-documents map and the
single-source-of-truth table. RE narrative stays in `docs/engine-re.md` (which already documents the
offsets); this struct is the machine-readable + applied form of that knowledge.

## Motivation

The recurring RE friction that survives the handler-labeling and import-map work is **re-decoding the
same `ctx` (esi) offsets by hand** — every handler RE means reading `*(int*)(param_1 + 0x53d14)`,
`+0x53d88`, `+0x9b24c`, `+0x408`, `+0x52bd4`, `+0x5f304`, `+0xa0ce4`… and recalling what each means. We
have already documented ~30 of these in `docs/engine-re.md`. Defining them **once** as a Ghidra struct
and applying it makes every handler decompile with named fields (`ctx->cur_gfx_idx`), front-loading the
"what's this offset" lookup so it never recurs. This is the same shape of win as Task A (handler
labeling): a one-time pass that pays out on every future look at the decomp.

Scope note: this is strictly the **engine context** struct (`esi`/thiscall `this`). The VM **global bank**
`G[…]` is a *separate* address space (not `ctx` offsets) and is out of scope here (it has its own map,
`vm-map/globals.toml`).

## Architecture

Single source of truth → generated machine view + doc → applied to the Ghidra image. Mirrors the
`opcodes.toml`/`globals.toml` pattern exactly.

### 1. `vm-map/engine-ctx.toml` (canonical source — hand-edited)

```toml
[meta]
struct_name = "EngineCtx"
size = 0xa1000              # >= max field end; sparse (gaps = undefined). Grows as needed.

[[field]]
offset = 0x53d14
name = "cur_ctx_index"
type = "uint"
note = "current gfx-object / script-context index (curCtx); indexes the 0x78-byte context records"
source = "native-RE"
confidence = "high"
# … one [[field]] per documented offset …
```

Seeded with the ~30 fields already documented in `engine-re.md`, e.g.: `0x408` retained gfx-object map
(`std::map`; geometry/draw get-or-create, `0x215` queries obj+4 source slot, `0x1f7` erases), `0x40c`
SYS4INI record count, `0x410` archive-name table, `0x414` SYS4INI 80-byte record
base, `0x14d54` obj-pointer table, `0x14f45` script-frame index, `0x46d14` stride-0x14 query table,
`0x51b78/0x51b7c` anim clock elapsed/duration, `0x52bd4` surfaces[1000], `0x53d14` cur-ctx-index,
`0x53d28/0x53d2c` frame codebase/PC, `0x53d60` context-record base (stride 0x78), `0x53d64` gfx
object-record array, `0x53d88` per-object cmd-type table (stride 0x78), `0x55120/0x55124/0x5512c`
anti-tamper checksum, `0x55248/0x552e8` per-frame return stack, `0x5f304` sleep-timer object, `0x6da88/
0x6db28` coroutine yield-state, `0x6dbc8/0x6dbcc` coroutine resume-state, `0x9b24c` dispatch handler
table[0x400], `0xa0cc0/0xa0cc4/0xa0cc8` screen w/h/bpp, `0xa0ce4` run-state flags, `0xb558/0xb560` gfx
dirty flags.

**Types kept simple:** `int`/`uint`/`void*`; arrays where clearly arrays (`dispatch[0x400]` as
`void*[0x400]`, `surfaces[1000]`); a field may be a plain scalar even if it's the head of a table (the
struct only needs the offset named — the array modelling is optional polish).

### 2. `tools/engine_ctx_build.py` (generator + linter)

- `--build` → `build/engine-ctx.json` (`{ "0x53d14": {"name": "...", "type": "uint"}, ... }` + meta) and
  regenerates `docs/engine-ctx-reference.md` (offset / name / type / note table, grouped).
- `--lint` → fail on: overlapping fields (offset+size collisions), a field whose end exceeds `meta.size`,
  an unknown type, or a duplicate name. Run in `--build`.
- Model + lint in the tool (or a small `engine_ctx_model.py` if it grows); unit-tested (`test_engine_ctx.py`).

### 3. Ghidra apply (`run_script_inline`, Java — the Task A pattern)

Confirm the active program is `/v2/range_00400000.bin` (base `0x400000`, 4400+ fns — the two-program
gotcha). Then:
1. Read `build/engine-ctx.json`; create (or replace) a struct `EngineCtx` of `meta.size` bytes with each
   field placed at its offset (`struct.replaceAtOffset` / `insertAtOffset` per the Ghidra API), gaps left
   undefined.
2. Read `build/op-handler-map.json`; for each dispatch handler, set its `this`/first-parameter type to
   `EngineCtx *` (via the thiscall `this` type, or parameter 0 where the convention lacks a `this`).
   Skip functions that don't take `ctx` as first arg (report them).
3. One transaction; `save_program`.

## Data flow

`vm-map/engine-ctx.toml` ──`--build`──▶ `build/engine-ctx.json` + `docs/engine-ctx-reference.md`
`build/engine-ctx.json` + `build/op-handler-map.json` ──`run_script_inline`──▶ `EngineCtx` struct +
retyped handler `this` on `/v2` ──▶ handlers decompile `ctx->field`.

## Validation

- `--lint` passes (no overlaps/OOB/dup).
- Decompile **`sleep_timer_arm`** — expect `ctx->sleep_timer` (offset `0x5f304`) instead of
  `param_1 + 0x5f304`; decompile **`gfx_op_0x215_query_source_slot`** — expect `ctx->cur_ctx_index` /
  `ctx->cmd_type_table[...]` style rendering at `0x53d14`/`0x53d88`. Record a before/after snippet.
- Handler count retyped reported; functions skipped (no `ctx` first-arg) listed for review.
- `save_program` succeeds.

## Scope & boundaries

- **In:** the ~30 documented `ctx` fields; struct creation; retyping dispatch-handler `this` params.
- **Out:** the VM global bank (`globals.toml`); worker functions (`FUN_0047xxxx`) — retype later as we
  touch them; exhaustive field discovery (grows incrementally via the toml, not a big-bang sweep);
  nested sub-structs (e.g. modelling the 0x78-byte context record as its own type — a later refinement).
- **Regenerable:** `build/engine-ctx.json` + `docs/engine-ctx-reference.md` are generated — never
  hand-edit; edit `engine-ctx.toml` and re-run `--build`.

## Acceptance criteria

- `vm-map/engine-ctx.toml` holds the ~30 seed fields; `--build` regenerates the JSON + doc; `--lint` clean.
- `EngineCtx` struct exists in the `/v2` image; dispatch-handler `this` params retyped (count reported).
- The two validation decompiles render named `ctx->…` fields; `save_program` OK.
- CLAUDE.md canonical-map + single-source table gain the `engine-ctx.toml` row; `tools-reference.md`
  gains `engine_ctx_build.py`; status memory records the milestone.
