# SCJUMP — progression / scene-sequencing logic

`SCJUMP.BIN` (29,790 instructions) is the game's **progression state machine**: it decides "what
comes next" in the story. `tools/scjump_decode.py` decodes its decision logic faithfully into a
readable, VM-verified table.

## Mechanism
- **Top switch** on `0x3234` (`chapter_mode`, 1..9) dispatches to a per-chapter block
  (`1→0x81, 2→0x9f, 3→0x1ded, 4→0x1e6d, 5→0x54c1, 6→0x93c4, 7→0x1e868, 8→0x2aa42, 9→0x2b8a3`).
- Each chapter block is an **acyclic tree** (0 back-edges) of comparisons on progress counters
  (`scjump_progress_a/b/c/d` = `0x4dfbc`/`0x2052e`/`0x152618`/`0xe6c5d`), story flags, **and a native
  computed value** (see below).
- At each of **1755 decision sites** it writes `0x0 = 1` (decision-valid) and
  `0x62ccf = <decision value>` (847 distinct values, 0..1693).

## The decode
`scjump_decode.py` walks the CFG with a guarded DFS (a guard stack + a local→symbolic-value map, so
`mov local, global ; cmp local, imm` resolves back to a real global condition) and emits, per site,
`(chapter, path-condition guards) → decision`. Output (generated, disposable):
`build/scjump-decisions.json` (machine) and `build/scjump-decisions.md` (human, globals rendered by
their `vm-map/globals.toml` registry names). Regenerate: `py -3.11 -X utf8 tools/scjump_decode.py`.

### Native-gated decisions (a key finding)
**1732 of 1755 decisions have at least one guard on a native computed value** — op `0x60`
(`u0041A270(local, 0x64)`, e.g. `local = f(100)`, then `local < 25`). This is resolved at runtime by
the engine (it looks like a random/derived roll: `≈ rand()%100 < N`), so it is **not recoverable
statically** and is honestly marked `opaque` in the guards. The decode still captures every *global*
condition (chapter, counters, story flags) on each path; only the native term is opaque. Takeaway:
SCJUMP mixes deterministic story-state gating with native/random gating — many "what comes next"
choices are not pure story flags.

### Verification
`--verify` cross-checks the decode against the executing VM (`vm0.py`) two ways:
1. **static-witness** — for the fully-static decisions (no native/opaque guard), synthesize a state
   from the guards and confirm the VM emits that decision (3/3 exact).
2. **execution-driven** — seed thousands of random states, run SCJUMP (the VM executes the native op
   too), and for every realized decision assert the table has that site with the right value **and**
   no static guard is violated by the realizing state (279/279 consistent over 2000 seeds, 0
   failures). This is the oracle that covers native-gated paths.
Run: `py -3.11 -X utf8 tools/scjump_decode.py --verify`.

## The native decision→scene boundary (deferred)
Consumers do `lookup-array(ptr, 0x5f0ed, 0x62ccf)` then `u00428010(ptr)`. **Correction (2026-07-07,
via Ghidra):** `u00428010` (op `0x1a2`) is **not** the scene resolver — it's a **save/resource-file
op** (its handler formats `%s\SAVE%2.2d.DAT`), so that pattern **persists the "visited-decision" flag
into the save**, not loads a scene. See `docs/engine-re.md`. The actual decision→scene resolution is a
different, still-native mechanism — most likely call-script/script-load-adjacent (`name-resolution.md
§1`). Cracking it uses the engine-dump + Ghidra loop (the opcode-dispatch table found in
`engine-re.md` is the key); still a separate slice.

## See also
- `vm-map/globals.toml` — the named globals SCJUMP switches on (chapter_mode, progress counters, flags).
- `name-resolution.md §1` — call-script / native dispatch (the decision→scene boundary lives here too).
- `docs/superpowers/specs/2026-07-07-scjump-decision-decode-design.md` — the design.
