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

## The decision→scene boundary (resolved 2026-07-20)
A FIELD snippet does `lookup-array(ptr, 0x5f0ed, 0x62ccf)` then `u00428010(ptr)`, which the spec
guessed was the scene resolver. **Correction (resolved 2026-07-20, via Ghidra):** `u00428010` (op `0x1a2`)
snapshots that selected global integer array cell into the shared `SAVE.DAT` profile table, keyed by its
resolved global-bank index. Paired op `0x1a3` restores a selected cell or zero. Its write of 3 at
`ctx+0x53d88` is only the instruction length. So this is **profile persistence, not decision→scene
dispatch**. The separate boundary is now located in `SYSTEM4.BIN`: on the normal path it indexes the global
resource-id table rooted at `G[0x87a57]` by `scjump_decision_out` (`G[0x62ccf]`), copies the selected raw
SYS4INI id into `G[0x699]`, substitutes `0x22` (`SC0000.BIN`) when the result is zero, and executes
`call-script G[0x699]` at SYSTEM4 offset `0x477`. The called scene returns to the still-live SYSTEM4 frame.

The captured initial New Game path sets decision zero in `GAMESTART.BIN`; SYSTEM4 consequently takes the
`0x22` fallback and enters SC0000. Later decisions use the same mapping/call boundary. See
`docs/engine-re.md` §Natural boot and New Game control spine for the native trace and complete boot chain.

## See also
- `vm-map/globals.toml` — the named globals SCJUMP switches on (chapter_mode, progress counters, flags).
- `name-resolution.md §1` — call-script / native dispatch (the decision→scene boundary lives here too).
- `docs/superpowers/specs/2026-07-07-scjump-decision-decode-design.md` — the design.
