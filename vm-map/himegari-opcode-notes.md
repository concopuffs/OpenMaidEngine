# Himegari opcode inference notes

Classification of the top ~20 unnamed opcodes (Phase 3.0, inference-only, 2026-07-06).
Machine-readable form: `tools/age_opcodes_himegari.py` (`INFERRED`). Evidence gathered with
`tools/opcode_context.py` over the 481-script `Output/DATA1` corpus (1,502,986 instructions).

Baseline: named opcodes = **72.62%** of instruction volume. Classifying the top 20 unnamed
ops covers to **~96.7%** cumulative. Kelebek labels (`u004xxxx`) are engine addresses from a
*later* AGE title — opcode number + argc are validated for Himegari (481/481 clean decode);
the meanings below are inferred and flagged by confidence + confirmation method.

## Buckets

### A. Statement/scope scaffolding — zero-arg, no operands, safe VM no-ops (HIGH)
The compiler brackets statements/blocks with operand-less markers. They carry no data and
sit at structural boundaries, so the VM v1 skips them; the Phase-4 dialogue harness confirms.

- **0x1f4 `stmt-begin`** (60,297; 4.01%) — opens scripts (first instr of ADDEN/ADDEXP/…), and
  `stmt-end 0x1f5 → 0x1f4` pairs 25,899×. Successor is the statement's first real op (eq/mov/call).
- **0x1f5 `stmt-end`** (60,297; 4.01%) — equal count to 0x1f4; precedes `exit`/next statement.
  `... → 0x1f5 → stmt-begin/lt/show-text`.
- **0x1d5 `cond-block`** (34,238; 2.28%) — **always** preceded by `jcc`; marks the fall-through
  (condition-true) block entry. `jcc cond -1 label; 0x1d5; <body>`.
- **0x1bc `block-mark`** (26,865; 1.79%) — `jcc → 0x1bc → mov` (26,413×): another block boundary.
- **0x1bf `call-end`** (8,752; 0.58%) — `call → 0x1bf → stmt-end` (8,600×): end-of-call statement.

### B. Statement metadata with an id — tentative no-op, confirm via harness (MED/LOW)
Same scaffolding family but carrying an immediate; likely line numbers / statement descriptors.
Treated as no-ops in v1 but flagged `method=harness` because a wrong no-op here could drop state.

- **0x21b `line-id?`** (27,016; 1.80%) — `mov → 0x21b <imm> → stmt-end`. Arg is 0/1/small. Sits
  near `savemesskip`/`loadmesskip` dev-comments (MENU/BUNKI) — **verify it isn't a message-control
  toggle** before trusting the no-op.
- **0x1d2 `stmt-desc?`** (17,323; 1.15%) — always immediately after `stmt-begin 0x1f4`; 2 imm args
  (e.g. `0x2 0x5`) then a `mov`. Reads like a per-statement descriptor.
- **0x258 `decl?`** (7,224; 0.48%) — runs in a chain right after script-entry `0x259`, enumerating
  ids (`0x4 0x1`, `0x5 0x1`, `0x6 0x1`, …). Prologue declaration/registration table.

### C. Structural (HIGH)
- **0x71 `label-def`** (26,445; 1.76%) — its corpus count **exactly equals the T1 table size**, so
  this is the instruction T1 (tag 0x71) indexes: a label/anchor definition. Arg is a small id.
  Preceded by `end-text-line`/`call`. VM v1 = no-op; **revisit if menu/callback dispatch resolves
  entries by this id** (T1 may be a named-entry-point table).

### D. Computational — exact behaviour via Unicorn/inference (MED/LOW)
- **0x215 `count?`** (5,675; 0.38%) — writes its first operand (a global) which is then tested
  `gre …, 0` / `lt` and branched on. Signature (global,global). Looks like a count / search-returns
  -index helper. Micro-exec candidate.
- **0x1a2 `resolve-handle?`** (17,585; 1.17%) — takes a `local-ptr` just filled by `lookup-array`,
  then `create-texture` follows. Resolves a looked-up resource/handle. Borderline effectful → Frida.

### E. Effectful — draw / UI / audio / ADV; need Frida to confirm (see shortlist)
All in the 0x420–0x421 graphics family or 0x41D audio family, clustered with named draw/audio ops.
Not semantically no-ops; VM must implement (or knowingly stub) them.

- **0x7a `text-param?`** (17,324; 1.15%) — `sub` computes a value, then `0x7a <imm> <computed> <imm>`,
  then `show-text`. A text speed / auto-wait / window parameter for the following line. (The review
  guessed "computational"; context says ADV-text-effectful.)
- **0x202 `draw-blit?`** (9,223) / **0x203 `draw?`** (9,149) — 4–5 coord/size args, preceded by
  coordinate arithmetic, chained with `draw-texture`/`u00420950`. Blit/positioned-draw.
- **0x1f7 `ui-elem?`** (7,735) / **0x1fa `ui-clear?`** (6,126) — paired (`0x1f7 → 0x1fa` 5,014×);
  create/begin a UI element then show/hide/clear it by id. From MENU: `0x1f7 0x11170 0xc80`, `0x1fa 0x49/0x4a/0x4b`.
- **0x217 / 0x218 / 0x21a `gfx-geom?`** (~5,000 each) — a chain each taking 4 global-ints
  (`0x21a → 0x218`, `0x217 → 0x1ff`); rect/coordinate/geometry transforms in the draw library.
- **0x1ff `draw?`** (3,926) — 4 args then `call`; follows `0x217`.
- **0xb6 `snd-ctrl?`** (4,805) — 0x41D family, self-chains, near `play-sound-effect`/`0xb5`; sound
  channel / volume / stop control.

### F. Input / UI hotspot branch — op 0x90 & 0x97 (A0-slice deep-dive, 2026-07-06 s2, HIGH)
Solved while investigating the A0 EMPTY scenes. Kelebek: `{0x90,"u0041BEB0",0x7} // args 5,6,7 =
code locations` (they left it "ukn"). Corpus evidence (`scratchpad/analyze_0x90.py` over all scripts):

- **op 0x90 = pointer/input hotspot hit-test branch.** Encoding `0x90 x y w h tgt_a tgt_b tgt_c`
  (argc 7; targets `0xffffffff` = unused). Branches to one of 3 targets per interaction outcome and
  **falls through to pc+1 when nothing matches** — design-confirmed: a 0x90 is 15 dwords, and the
  instruction after the last one lands exactly on the fall-through statement (`0xd0+15 = 0xdf`).
- **Two forms, both only in one shared subroutine:**
  - *Mode A* (1505 = 5×301): all-immediate, `w=h=20`. The five on-screen ADV buttons — identical in
    all 301 scripts at `(x,y) = (684|706|728|750|772, 572)`, each setting one of `G[0x6c9..0x6cd]` to
    1 (target a) / 0 (target b) / 0+run-action (target c). Reads as hover-enter / hover-leave / click.
  - *Mode B* (903 = 3×301): local-int operands, `w=h=1`, only target c real — a keyed/degenerate
    2-way input test (fall through, or jump to c). Operands are `0-1000`-based sentinels (key codes?).
- **Distribution is unanimous: every one of the 301 ADV scripts has EXACTLY 8 sites (5 A + 3 B).**
  Zero scene-specific use — it is boilerplate ADV chrome (the system button row + input polling), not
  gameplay branching.
- **VM handling:** headless (no cursor/input) ⇒ fall through ⇒ **our stub is already correct**; proven
  safe by all 279 CLEAN dialogue scenes (which contain these same 8 sites). So 0x90 is NOT a headless
  correctness hole. It must be modelled for real in **A2** (Godot input backend) as a live hotspot test;
  confirm the exact target→interaction-state mapping via input capture / Frida then.
- **op 0x97** (`u0041C150`, argc 5: `v1 v2 1 1 <action-id>`, NO code targets) interleaves with 0x90 in
  the same subroutine → companion *register-hotspot / set-widget-action* call (trailing imm = action id
  0x0/0x7/0x8). Not control flow; part of the `0x90/0x91/0x92/0x95/0x97` widget cluster.

**Consequence for A0/A1:** the 12 EMPTY scenes are gated by **state + interactive input-wait loops**
(this chrome polls hotspots until a click), NOT by unmodelled 0x90 semantics. A1 can keep 0x90 as
fall-through with confidence; unlocking the EMPTY family is a Phase-A2/B task (seed state + input).

## Coverage after classification
See `build/opcode-coverage.md`. Roughly: named 72.6% + no-op scaffolding & label-def (A+C, high
conf) ≈ 14.4% + tentative-no-op metadata (B) ≈ 3.4% → **~90% handleable without live tools**;
the effectful/computational remainder (D+E) ≈ 6–7% is classified but needs Frida/Unicorn for
exact behaviour.

## Frida / Unicorn shortlist (feeds Phase 3.1/3.2)
- **Frida (effectful):** 0x7a (adv text param), 0x202/0x203/0x1f7/0x1fa/0x217/0x218/0x21a/0x1ff
  (draw/UI), 0xb6 (audio), 0x1a2 (resource resolve). Also **Frida-confirm already-named effectful
  ops the VM will rely on**: `play-voice 0xc4`, `play-bgm 0xbf`, `play-sound-effect 0xb4`,
  `draw-texture 0x1fb`, `draw-string 0x204`, `create-texture 0x1f8`, `set-texture 0x1f9`.
- **Unicorn (computational):** 0x215 (count/search).
- **Harness (no live tools):** 0x21b / 0x1d2 / 0x258 — confirm no-op assumption by diffing the
  VM's `show-text` sequence per scene against `build/text/dialogue.jsonl`.
