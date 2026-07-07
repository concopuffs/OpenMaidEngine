# Phase A — Vertical Slice Plan (the first build step)

Concrete execution plan for Phase A of `remake-architecture-and-roadmap.md`. Decided over the
alternative (fully decoding `SCJUMP.BIN`) after recon showed SCJUMP is not the gating unknown.

## Why the slice, and why headless-first

**SCJUMP recon (2026-07-06):** `SCJUMP.BIN` is a 29,796-instruction **progression state machine**,
not the `call-script` registry. Top level switches on `global 0x3234` (mode 1–9 → big blocks); each
block is nested `eq`/`ne`/`and`/`jcc` on flags, ending in `mov`s to output globals. Almost no
`call-script`. So it decides *what scene/branch comes next* via state, and does **not** resolve
`call-script id → code`. Consequence: the id→code registry stays engine-level (deferred), **but the
slice can stub `call-script`** — it is not gating for running one scene's dialogue.

**Correctness bootstrap (roadmap §5) drives the ordering:** the VM must be *validated-correct* before
it is trustworthy. Our strongest oracle is `build/text/dialogue.jsonl` (the `show-text` lines per
script). So the very first slice is **headless and text-only, validated by that oracle** — no Godot,
no AGF, no audio, no dispatch registry. Only once the VM reproduces dialogue do we add rendering.

Phase A therefore splits:
- **A0 — headless VM, dialogue-validated (Python prototype).** ← immediate, executable now.
- **A1 — port the validated model to C#** (the runtime's VM core).
- **A2 — Godot ADV backend** (render one scene with visuals + voice).

---

## A0 — Headless VM validated by the dialogue oracle

**Goal:** a Python interpreter that executes one ADV scene's bytecode and emits its `show-text`
sequence; that sequence is a coherent, in-order subsequence of the script's static `dialogue.jsonl`
lines. This proves the execution model — control flow, operand/pointer semantics, string handling,
and the no-op-marker assumptions — *before* any C#/Godot investment. Reuses `tools/sys4load.py` for
all parsing/decoding (no new parser).

### Execution model to implement
- **Memory:** one flat **global bank** = `dict[int,int]` (globals are raw offsets into one space;
  `global-int A` ⇒ `G[A]`, default 0). Per-call **local frame** with typed banks sized by header
  F0–F5 (`local_int[F0]`, `local_float[F1]`, `local_string[F2]`, …).
- **PC / control flow:** build `offset→instruction-index` map from `sys4load` instructions (each has
  `.offset` = dword index; jump targets are dword indices). `jmp t` → pc = map[t]. `jcc(cond, A, B)`
  → cond truthy ? goto A : goto B, where `0xffffffff` = fall through (confirmed model from RECOVER).
- **Operand resolution by type:** imm→value; global-int→`G[value]`; local-int→`frame.int[value]`;
  string(2)→decoded string at dword offset; float/global-string/etc. analogous.
- **⚠ Pointer/lvalue semantics — the key modeling task.** RECOVER proves `-ptr` operands are
  *lvalues*: `lookup-array(dst_ptr, base, idx)` yields a *reference* to `G[base+idx]`; `mov` through a
  ptr writes to the referenced cell; reading a ptr rvalue dereferences it. Model a ptr slot as holding
  an address into the global bank; nail this so the RECOVER array-copy produces correct results (unit
  test it directly).
- **Opcode handlers (~52 named ops):**
  - arithmetic/bit `add sub mul div mod and or sar shl` → `p1 = p2 ⊙ p3`.
  - compares `eq ne lt lte gr gre` → 0/1.
  - `mov` (incl. through ptr), `lookup-array` (`p1=mem[base+idx]`), `lookup-array-2d`
    (`p1=mem[base + i*stride + col]`), `copy-to-global`, `set-array-to`, `bit-set/reset`, `check-bit`.
  - control `jmp call jcc ret exit exit-script`.
  - string `set-string concat strlen toString`.
  - **ADV capture:** `show-text` → append (arg text) to the emitted list; `end-text-line`,
    `wait-for-input`, `set-font`, `comment` → capture/skip (no visible state).
- **Markers → no-op (this TESTS the classification):** `0x1f4 0x1f5 0x1d5 0x1bc 0x1bf` skip;
  tentative `0x21b 0x1d2 0x258` skip — if dialogue stays correct, the no-op assumption is validated.
- **`call-script` → STUB:** log `(id)`, return immediately. (Its dialogue belongs to other scripts;
  stubbing keeps the emitted set = this script's own lines.)
- **Effectful (draw/texture/audio/ui/input) → STUB:** log and ignore.
- **Unknown/other opcodes → log + no-op**, so a rare op doesn't halt the run (record coverage).

### Oracle & scene choice
- **Oracle:** with calls stubbed and default state, every emitted `show-text` line must be a real
  decoded string from the script's pool, and the sequence must be an **in-order subsequence** of that
  script's `dialogue.jsonl` lines (≈ equality for a linear scene). Catches: garbage strings (bad
  operand/ptr handling), impossible ordering (bad control flow), missing/extra lines.
- **Scene pick:** choose a **short, mostly-linear ADV scene** — high `show-text` count, low `jcc`
  density, few `call-script`. Selection step: rank `SC####`/`SP####` by
  `(show-text count) / (jcc + call-script count)`, small size. Known-good fallback: `SC0030.BIN`
  (dialogue verified). Also run a **RECOVER unit test** to validate pointer/array semantics independent
  of dialogue.

### Steps
1. `tools/vm0.py`: load a script via `sys4load`, build offset→index map, frame + global bank.
2. Implement operand resolution + the arithmetic/compare/mov/lookup/control handlers; unit-test on
   `RECOVER.BIN` (array copy + both loops must produce correct global writes).
3. Add ADV capture + markers-as-noop + call/effectful stubs; add opcode-coverage logging.
4. Run on the chosen linear scene; diff emitted `show-text` vs `dialogue.jsonl` (subsequence check);
   eyeball the first ~15 lines for coherence.
5. Iterate until several scenes pass; record which ops/markers were exercised and any surprises
   (esp. whether the tentative-no-op markers hold).

### Success criteria (A0 done)
- RECOVER unit test passes (pointer/array model correct).
- ≥3 ADV scenes: emitted `show-text` is a coherent in-order subsequence of their `dialogue.jsonl`,
  no garbage strings.
- Coverage report of which opcodes actually executed (drives A1/A2 priorities).
- The no-op-marker assumption is confirmed or corrected with evidence.

---

### A0 result (2026-07-06) — execution model VALIDATED

`tools/vm0.py` built (reuses `sys4load`; ~250 lines). Results:
- **RECOVER unit test PASSES** — all 7 checks (block-1 3-field copy, block-2 restore + flag, both
  skip-guards). The pointer/lvalue model, 2D stride indexing, both loops, and two-way `jcc` all
  execute correctly. **The core execution model is proven.**
- **Full SC/SP oracle sweep (`vm0.py --sweep`): 282 / 294 scenes DIALOGUE-VALID = 95.9%.** Every
  emitted `show-text` line is checked (by string offset) as an in-order subsequence of the script's
  static `dialogue.jsonl` lines. **Zero STRAY and zero ORDER violations across all 294 scenes** — the
  model never emits a garbage string and never emits dialogue out of order. 279 CLEAN (valid + natural
  `exit`); 3 OK/LOOP (valid subsequence, halted by the loop-guard); 12 EMPTY; 3 skipped (no static
  show-text). SC0000 = 326 static / clean; SP0062 = 220/220 CLEAN.

**A0-remainder work done (2026-07-06, session 2):**
- **Loop-guard added** (`EMIT_CAP=2`): halt a run once any single line is re-emitted a 3rd time —
  a semantic guard tied to the oracle (vs. a blind step limit), and it *classifies* the scene LOOPED
  instead of spewing garbage. The 3 zero-state spinners (SC0010/SC0600/SC0200) now terminate cleanly
  in <12k steps and their emitted lines are all valid.
- **SP0062 "stray" was a measurement artifact**, not a bug — the precise offset-based oracle shows it
  CLEAN (220/220, natural exit). Offset-match ⟹ text-match (VM decodes each string at the same offset
  the extractor did), so CLEAN is trustworthy.
- **`0x71` (label-def) folded into the no-op marker set** — structural, no runtime effect.
- **Sweep + single-scene diff harness** added to `vm0.py`: `--sweep [N]` (coverage table over all
  SC/SP), `--scene NAME` (detailed diff for one script), plus `load_oracle`/`subsequence_status`.

**op 0x90 investigated in depth — it is input chrome, NOT a correctness hole** (full evidence:
`vm-map/opcodes.toml` op 0x90 `details`). Kelebek left it "ukn"; corpus analysis resolves it:
`0x90 x y w h tgt_a tgt_b tgt_c` (argc 7) is a **cursor/input hotspot hit-test** that branches per
interaction outcome and **falls through to pc+1 when nothing matches** (design-confirmed: enc.len 15
lands the next instr on the fall-through statement). It occurs ONLY in a shared ADV-chrome subroutine
that is byte-identical in all 301 ADV scripts — **exactly 8 sites each** (5 immediate-rect buttons at
`(684..772, 572)` toggling `G[0x6c9..0x6cd]` + 3 local-operand keyed forms), **zero scene-specific
use**. Headless (no cursor/input) ⇒ fall through ⇒ **vm0's stub is already correct**, proven safe by
all 279 CLEAN scenes (which contain these same 8 sites). `op 0x97` (argc 5, no targets) is its
companion register-hotspot call. **So 0x90 stays as fall-through in A1 with confidence; it is modelled
as a live hotspot test only in A2** (Godot input backend), confirming target→state mapping via Frida.

**The 12 EMPTY scenes — state-gated interactive screens, not a model failure.** Traced SC0830: it
exits early because `G[0xaba5c]==1` gates the content; past that gate the dialogue sits behind the ADV
input-wait loop (the hotspot-polling chrome above), so with no seeded state and no input the scene
exits or spins before reaching text. Unlocking them = seed per-scene state + supply input →
**Phase A2/B**, not an A0 model fix.

**⚠ Honest scope of the 95.9%:** the subsequence oracle proves **no-garbage / in-order**, not a
*complete* path — inherent to a subsequence oracle run headlessly (interactive/state-gated branches
take the no-input path by design). That anti-garbage guarantee is exactly what A0 set out to prove.

**Confirmed by this run:** the classified no-op markers (`0x1f4/0x1f5/0x1d5/0x1bc/0x1bf` + tentative
`0x21b/0x1d2/0x258`, now + `0x71`) are safe as no-ops for ADV flow; `call-script` is stubbable;
effectful ops (`draw-texture`/`create-texture`/`play-voice`/`0x1f7`/`0x202`/`0x203`/…) stub cleanly.

**✅ A0 COMPLETE.** Success criteria met: RECOVER unit test green (pointer/array/control-flow model
proven); 282 ADV scenes emit clean in-order subsequences with zero garbage; coverage number recorded;
no-op-marker assumption confirmed at scale; `op 0x90` (the last big control-flow unknown) resolved as
input chrome whose fall-through stub is correct headless. Next = **A1** — port the model to the C# VM
core, differential-test against `vm0.py`. 0x90/0x97 stay stubbed (correct headless); the interactive
input path + per-scene state seeding land in **A2** (Godot backend) alongside the real hotspot model.

## A1 — Port the validated model to C# ✅ DONE (2026-07-06)
Reimplement the A0 execution model as the runtime VM core in C# (the language decision from the
roadmap; GDScript is too slow for the loop). A0 is the reference: differential-test C# against the
Python prototype's traces on the same scenes. Port the container parser too (or load via a shared
spec). Deliverable: headless C# VM reproducing A0's results.

**Result:** `engine/` .NET 8 solution (`Age.Engine` classlib w/ `Model`/`Vm`/`Sys4`/`Hosting` seams +
`Age.Cli` + xUnit tests). RECOVER passes; the C# `trace` is **byte-identical to `vm0.py --trace` across
all 297 SC/SP scenes** (offsets+halt+steps). Version-neutral `Script` contract enforced (VM core never
references `Sys4`). Spec/plan: `docs/superpowers/{specs,plans}/2026-07-06-a1-csharp-vm*.md`.

## A2 — Godot ADV backend (one scene, with visuals)
Wire the C# VM's effectful ops to Godot: `show-text`/message window (+ furigana via `display-furigana`),
`set-font`, `wait-for-input`, choices, `play-voice`/`play-bgm`, and `create-texture`/`set-texture`/
`draw-texture`/`draw-string` for the background + sprites. Convert the scene's AGF art with the
on-disk `AGF2BMP2AGF.exe`. Resolve just-enough `call-script`/state so the scene's setup runs (or
hand-set the preconditions). Deliverable: **the chosen scene playable in Godot** — bg + dialogue +
a choice + voice — matching A0's text.

### A2a — Interactive dialogue loop ✅ DONE (2026-07-06)
Godot 4.7 (.NET, `S:/Godot/Godot_v4.7-stable_mono_win64`) project in `godot/` referencing `Age.Engine`
in-process. VM gained one hook (`IHost.WaitForInput`, opcode 0x72); suspend/resume via a worker thread +
blocking `SemaphoreSlim` in `GodotAdvHost`, UI marshalled with `CallDeferred`. Plays SC0000 page-by-page,
pauses at wait-for-input, resumes on click/Enter. **Headless self-test** (`--headless -- --selftest`)
asserts the emitted 186-line offset sequence == `build/vm0-trace.json`; A1 engine tests stay 7/7.
Toolchain: `godot --headless --path godot --import` → `dotnet build godot/Himegari.csproj` →
`godot --headless --path godot [-- --selftest]`. Spec/plan:
`docs/superpowers/{specs,plans}/2026-07-06-a2a-godot-dialogue*.md`.
**Next = A2b:** background via `AGF2BMP2AGF.exe`, `play-voice`/`play-bgm`, choices → VM globals,
just-enough `call-script`/state (unlocks richer scenes).

### A2b-Background — FIRST-PASS RENDER LANDED (2026-07-06)
Resolution solved (`docs/asset-resolution-re.md`: `resId → files[section_base(scene)+resId]`) and wired
into a live render. **Shipped:** `Age.Engine/Sys4/ResourceMap.cs` (loads `build/asset-index.json` +
`build/asset-sections.json`; `Resolve(scene,resId) → AssetEntry`; `TexturePath` → pre-converted BMP);
`GodotAdvHost` implements `create/set/draw-texture` (slot → `TextureRect` composited behind the dialogue
in a `_stage` layer); `IHost.DrawTexture` + VM dispatch extended to pass the destination x/y (draw-texture
args 7/8); `project.godot` window = 800×600; `convert_agf.py` searches all archives + `--scene` batch.
Engine 8/8, C# `--selftest` still byte-matches the vm0 trace (VM behaviour unchanged). **Works end-to-end:**
the VM executes `set-texture(resId)` → ResourceMap resolves across archives → BMP loads → composite; the
full-screen **event-CG layer (`EV052*`) renders correctly** as the opening plays.

**Known first-pass limitations (all one subsystem = graphics geometry/blend, the next chunk):**
1. **Only the full-screen layer is correct.** Sprites/effects and `BG*` backgrounds routed through the
   CG-load subroutine (`label_12649`) derive width/height/position from native ops we still **stub** —
   `0x208` (get-texture-size) + the sprite position/registration/animation chain — so their `dst/size`
   are garbage (backgrounds land off-center, e.g. `BG030A dst=(300,300)`; sizes come out `0x0`). Only the
   *immediate* full-screen draws (`(0,0) 800×600`) render right.
2. **No alpha/blend.** `AE*` full-screen fade/flash effects draw **opaque and instant** (a static grey/white
   sheet over the CG) instead of alpha-animating. No chromakey either (sprites would show green boxes —
   moot until they position).
3. **Slot model is an approximation.** We use one `TextureRect` per slot, replace-on-draw; the game
   actually **blits onto slot 0 as an immediate-mode canvas** (everything composites into slot 0).
4. AGF is **pre-converted to BMP offline** (`convert_agf.py --scene`); a runtime C# AGF decoder is deferred.

**Next chunk — graphics-geometry/blend subsystem:** implement `0x208` (host returns the slot's real image
dims) + the sprite position/registration ops so geometry is correct; add alpha/additive blend for fades +
green chromakey; likely move to a proper canvas/blit compositor. Fixes sprites, background placement, and
fades together. (Superseded: the id-specific plan in `docs/superpowers/plans/2026-07-06-a2b-background.md`.)

---

## Risks / open questions for A0
- **Pointer/lvalue semantics** — the main modeling risk; RECOVER is the litmus test.
- **Initial global state** — a scene may assume preconditions from earlier flow (`SCJUMP`/prior
  scenes). Mitigation: default-zero globals + set the few a scene reads early; the subsequence oracle
  tolerates a shortened path.
- **Runtime vs static dialogue order** — static `dialogue.jsonl` is file-order (all lines); runtime is
  execution-order (branch taken). Hence *subsequence*, not equality; pick linear scenes to tighten it.
- **Hidden effect in a "stub"** — a stubbed effectful op that actually gates control flow could skew
  output. Watch for divergence; promote a stub to a real handler if a scene needs it.

## Immediate next action
Build `tools/vm0.py` and get the **RECOVER unit test** green (pointer/array/control-flow correctness),
then run the first linear ADV scene against the dialogue oracle. That single result tells us whether
the whole VM approach executes correctly — the load-bearing question behind option 3.
