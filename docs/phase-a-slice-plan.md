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
all 297 SC/SP scenes** (offsets+halt+steps). *(Historical: this parity held while call-script was
stubbed; once call-script execution landed [2026-07-07], vm0.py was retired from oracle duty and
`TraceDiffTests` removed — see the call-script EXECUTION section.)* Version-neutral `Script` contract enforced (VM core never
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
*(Historical: the selftest was later rewritten [2026-07-07] to run a SYNTHESIZED scene through the
plumbing and match a live headless run — full call-script handling, no vm0/frozen golden — see the
call-script EXECUTION section.)*
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
3. **Slot model is an approximation.** We use one `TextureRect` per slot, replace-on-draw. (⚠ Earlier this
   line claimed "the game blits onto slot 0 as an immediate-mode canvas" — **DISPROVEN 2026-07-08**: the engine
   is RETAINED — `draw-texture` binds a retained object by handle (`gfx_object_bind_draw`), objects have distinct
   handles + per-object source slots, composited each frame. See engine-re.md "opening render path is RETAINED".)
4. AGF is **pre-converted to BMP offline** (`convert_agf.py --scene`); a runtime C# AGF decoder is deferred.

**Next chunk — graphics-geometry/blend subsystem:** implement `0x208` (host returns the slot's real image
dims) + the sprite position/registration ops so geometry is correct; add alpha/additive blend for fades +
green chromakey; likely move to a proper canvas/blit compositor. Fixes sprites, background placement, and
fades together. (Superseded: the id-specific plan in `docs/superpowers/plans/2026-07-06-a2b-background.md`.)

### A2b-Audio — WIRED, plays end-to-end (2026-07-06)
Audio wired, OGG plays natively in Godot (no Frida, no decode/geometry work). **KEY FINDING — the two audio
ops use DIFFERENT addressing (the initial "unified manifest" assumption was WRONG for BGM):**
- **`play-voice`** → per-scene manifest `files[base+id]`, **offset 0** (same as `set-texture`).
- **`play-bgm`** → **DIRECT LITERAL NAME** `id → BGM{id:03d}.OGG` (DATA3), NOT the manifest.

**Shipped:** `IHost.PlayBgm/PlayVoice`; VM dispatch routes `play-bgm`(0xbf)/`play-voice`(0xc4) (both argc 1);
the three non-Godot hosts (`CaptureHost`, test `RecHost`/`CountHost`) no-op them so `--selftest` + engine 8/8
stay byte-identical (audio ops still `pc+1`, step count unchanged); `ResourceMap.BgmPathById(id)` (direct
name) for BGM + `ResourceMap.AudioPath(AssetEntry)` (manifest `Resolve`) for voice; `Main` loads via
`AudioStreamOggVorbis.LoadFromBuffer` into two `AudioStreamPlayer` nodes (BGM `Loop=true`; voice `Loop=false`,
interrupt-on-new). Headless run: 0 OGG-load failures, selftest byte-parity OK.

**BY-EAR VALIDATED (2026-07-06, systematic-debugging).** User confirmed voices play on their lines
(`play-voice` med→HIGH). Two reports root-caused:
- **BGM off-by-one → FIXED (real root cause, resolver changed for BGM only).** Real game plays BGM005 for
  `play-bgm 0x5` and BGM008 for `0x8`; we mis-played BGM006/009 because we resolved BGM via the manifest
  (`files[5]=BGM006`). BGM is actually addressed by **direct name** `BGM{id:03d}.OGG`. **Proof:** `play-bgm
  0x23 → BGM035.OGG`, a real standalone track (the BGM set skips 030-034) that the manifest mis-resolved to a
  graphics entry (`files[35]=EV049AA.AGF`). Voices are NOT off-by-one — the manifest interleaves graphics/
  voice (`files[35]=EV049AA`, `[36]=MAN999`, `[37]=EV052CA`, `[38]=SYL0001`), so `id-1` would land voices on
  `.AGF` (silent) but they play ⇒ voice offset is exactly 0. So the fix is BGM-specific; voices/textures
  unchanged. Corrects the earlier "Frida-confirmed play-bgm 5→BGM006" record (a mis-attribution).
- **Lily silent = correct, form-gated (NOT a bug).** Her lines use a 3-way dispatch on form flags
  `G[0xa57]`(A)/`G[0xa58]`(B)/`G[0xa59]`(C): exactly one is 1 in the real game (her current form), else the
  line `jmp`s past with no voice. Our harness seeds no globals → all zero → every Lily line skipped. Proven
  by seeding: `audio SC0000.BIN 0xa57=1` → 35 LILA clips fire in order (form B→LILB, C→LILC). Left unseeded
  by user choice (no dummy state); Lily stays silent until real cross-scene state flow (Phase B) exists.

**Diagnostic tool added:** `Age.Cli audio <SCENE.BIN> [0xADDR=VAL ...]` — runs a scene and dumps executed
`play-bgm`/`play-voice` ops in order (BGM direct-name, voice manifest), optional global seeding. Used for all
of the above. **Watch-items:** BGM looping is whole-file for now (Eushully OGGs may carry `LOOPSTART`/
`LOOPLENGTH` Vorbis comments — refine later); `play-sound-effect`(0xb4, argc 2) left stubbed (arg roles
unconfirmed).

### A2b-Geometry — `0x208` keystone + blit compositor (2026-07-06)

Spec/plan: `docs/superpowers/{specs,plans}/2026-07-06-a2b-graphics-geometry*.md`. **Shipped & verified:**
the CG-load subroutine (`SC0000.asm` `label_12649`) computes all sprite/background geometry **in
bytecode** (`add`/`sub`/`div`/`lookup-array`); the only missing native primitive was **`0x208 =
get-texture-size(slot) → (out_w, out_h)`**. Implemented as a real VM op (`IHost.GetTextureSize`, writes
the two output globals); non-Godot hosts return `(0,0)` so trace/selftest parity holds (engine 11/11,
`--selftest` byte-identical). Replaced the TextureRect-per-slot approximation with a faithful **800×600
immediate-mode blit compositor** (`Main.BlitSlot`: `_screen.BlitRect(src rect → dst)` in execution order,
one displayed `TextureRect`; source dims read from the pre-converted BMP header on the VM thread via
`BmpHeader.ReadDims`, so the bytecode's geometry math sees real sizes synchronously). New diagnostics:
`Age.Cli gfx <SCENE>` (headless numeric oracle — dumps per-draw resolved file + computed geometry) and
`godot … -- --shot <png> [--shot-page N]` (page-gated screenshot capture). **The opening event-CG sequence
renders correctly** — full-screen CG at `(0,0)` with dialogue over it (verified by screenshot, SC0000
pages 1/3).

**Slot-0 seed (bug found & fixed via the gfx oracle + user eyeball):** slot 0 is the **primary/screen
surface** (800×600), normally created by engine-boot init the single-scene harness skips. Cold, `0x208`
measured `0×0`, and the anchor-preserve math (`base' = center − (w_new/2, h_new)`) then wrote a corrupted
`(−400,−600)` into the **persistent base globals** — so the first CG was grey and CG2 inherited the
corruption. Fix: seed `_slotDims[0] = (800,600)` (and record `create-texture(w,h)` dims) so the first CG's
anchor stays an identity. This is the faithful stand-in for the skipped boot-time primary-surface creation.

**Post-opening bg/sprite drift — ✅ RESOLVED (2026-07-07): native gfx ops + missing INIT2 boot state.**
⚠ Corrects an earlier wrong "state-divergence-only" verdict here. Symptom (screenshot
`Screenshot 2026-07-06 211353.png`): everything blits through slot 0 as an immediate-mode canvas; the
anchor-preserve base globals **accumulate drift** across differently-sized textures (`BG030A→(300,500)`,
next→`(450,100)`, →`(800,350)`… marching bottom-right; the background ends up pinned off-centre / bottom-right
with the rest of the screen grey). Root cause = the stubbed native op **`0x215`** collapsing every draw onto
slot 0 (its return drives `label_12649`'s slot-select).

**The canonical decode + verdict now lives in `docs/engine-re.md` (op `0x215` section)** — don't duplicate it
here. In brief: `0x215`'s real handler `FUN_0042a0b0` (Ghidra) writes cmd-type 5 into the current gfx-object
record and returns a **`std::map::find`** over an engine-internal command-buffer registry (populated by sibling
gfx ops like `0x1a2`). That return is **native command-buffer state, not the VM global bank** → seeding
story-state **cannot** fix it. So this is **(b) a genuine native op**, *not* (a) the Phase-B state-divergence
problem. The prior conclusion in this doc — grounded in a 2/s `capture_gfx_objects.py` poll of the object-*record*
array — was wrong: it observed the wrong structure (not the lookup map) and can't rule out transient records.

**Resolution had TWO halves** (canonical decode in `docs/engine-re.md`, op `0x215` + "The render drift's
SECOND half"; don't duplicate here):
1. **Native gfx ops (b):** all 14 command-buffer ops (`0x1a2`,`0x1f7`,`0x1fa`,`0x1ff`,`0x202`,`0x203`,
   `0x212`,`0x213`,`0x215`–`0x21a`) reversed + implemented against a host-side `GfxState` (VM execution
   state; `engine/Age.Engine/Model/GfxState.cs`). `0x215` now returns distinct per-object slots.
2. **Missing system-boot state (a):** the CG handle array `G[0x62455..]` is set by boot script **INIT2**
   (via entrypoint `SYSTEM4.BIN`), which a cold single-scene run skips → all CGs collapsed onto object 0.
   Supplied via **`Age.Cli gfx --boot`** and **Godot `--boot`** (run `INITCONFIG/INIT2/INIT` through
   `GameSession` first). So the drift needed BOTH — not story flags, and not native-ops-alone.

**Result: with `--boot`, the opening event CGs render correctly** — screenshot-verified live in Godot
(`--path godot -- --boot`; the CGs that were entirely missing now fill the frame). **Residual (deferred, not
regressions):** `AE*` fade/flash effects draw opaque (alpha/blend deferred — a white "explosion" glow that
should fade stays); some object-slot CGs start with a zero anchor (cold gfx objects vs the real game's warm
ones — default object geometry is confirmed `(0,0)` in `gfx_object_init_default`, so not a missing default).
Next visual chunk = **alpha/blend + effect fading** (`0x202/0x203` already store the packed color) + per-frame
compositing. NOTE the two-boot gap: our Phase-B `--boot` runs *data* `*INIT` scripts; this added the *system*
boot — a "full boot" should run both.

### A2b — Scene completeness gauge (opcode coverage tracker, 2026-07-07)

To stop guessing how "done" a rendered scene is, `tools/scene_opcode_coverage.py` histograms a scene's
static opcodes and classifies each against the C# VM: **impl** (VM has a handler arm — real or a deliberate
no-op like `set-font`), **safe-noop** (no arm, but `opcodes.toml` marks it `noop_headless` — a statement /
block marker, correct to skip), or **GAP** (no arm and effectful → the VM silently `pc+1`s past it). The
implemented set is parsed from `VirtualMachine.cs`'s `case` arms (single source of truth, no drift); output is
`build/scene-opcode-coverage/<SCENE>.md`. This makes a half-rendered scene legible: *"N ops still stubbed"*,
not *"something's wrong and we thought everything ran."*

**SC0000 baseline:** 129 distinct opcodes / 16257 instrs. Instruction-weighted the VM already covers **~94.8%**
(impl 12368 + safe-noop 3053); the holes are **68 GAP opcodes / 836 instrs (5.1%)**. The GAP list clusters into
concrete backlog buckets (drives the rendering roadmap below):
- **ADV on-screen text** — `draw-string`(0x204)×205 + `0x7a` text-param×205 (1:1 paired). Dialogue text is
  currently surfaced via `IHost.ShowText` → Godot `Label`; the engine's *native* glyph/window draw path is
  unmodeled (cosmetic for now, but it owns text layout/speed).
- **Unmodeled gfx-range cluster** — `0x21c–0x243` + `0x2bd/0x2bf` (e.g. 0x220×66, 0x22f×34, 0x228×33, 0x21e×25):
  siblings of the `0x212–0x21a` command-buffer family we implemented, **not yet reversed** → the biggest single
  rendering unknown (likely sprite/effect/blend geometry). RE these next before more compositor work.
- **Timing** — `sleep`(0xc8)×20: animation pacing; fades/effects can't *animate* (only snap) until this exists.
- **Audio/SFX** — `play-sound-effect`(0xb4)×24 + `0xb5/0xb6/0xc2/0xd9` (channel/volume/stop control) — stubbed.
- **Scene coroutine** — `0x7b`×6 / `0x7c`×2 / `0x140`×1: the scene-coroutine framework backlog (multi-object
  scene setup routes through it; see the "SECOND latent gap" note in the status memory).
- **Misc VM-support ops** — a long tail (`0x75-0x77`, `0x85/0x88/0x8b`, `0x93/0x94`, `0x197-0x1a4`, `0x1c7-0x1cf`,
  `0x1fd`, `0x20a/0x20c/0x20e`, …), 1–2 sites each; mixed markers vs effectful — triage per-op as the VM reaches them.

Re-run per scene (`scene_opcode_coverage.py SC0240 …`) to gauge any target. The tracker also cross-checks
`opcodes.toml` metadata against VM behavior — it already surfaced `0x259` (script-entry marker) missing its
`noop_headless` flag (now reconciled).

### A2b — Sprite transform/animation subsystem, opening slice (2026-07-07)

Spec `docs/superpowers/specs/2026-07-07-gfx-animation-subsystem-design.md`; plan
`docs/superpowers/plans/2026-07-07-gfx-animation-subsystem.md`; RE in `docs/engine-re.md`
("0x21c–0x243 sprite transform / ANIMATION cluster"). First slice of the `0x21c–0x243` cluster the
completeness gauge flagged.

**Implemented (VM records, engine 50/50, full parity — sweep 284 exit/13 STEP-LIMIT unchanged):** the four
opening-path anim ops — `0x21e set-anim-transform-norm` / `0x220 set-anim-transform-abs` (per-object transform
channel: target vec3 + 2 params + enable), `0x234 anim-start` (animate toward a target vec3 over the clock),
`0x238 set-anim-clock` (**GLOBAL, non-blocking** duration). `GfxState` gained the per-object anim channel + a
global clock (passive — non-Godot hosts read none of it, so trace parity holds); `RenderObject.AnimState` carries
it to the compositor. **RE correction:** the clock is global 1-operand (not per-object), and `anim-start` carries
the target — the `set_anim_clock` handler's own comment confirms the wall-clock design ("drive animation in the
host's per-frame loop while the VM is parked at wait-for-input").

**Compositor (Godot):** a per-handle wall-clock alpha tween over the global clock + an alpha-aware `BlitLayer`
(3rd vec component = opacity), plus `--shot-settle <frames>` to capture mid-tween. **Non-regressing:** SC0000
opening page 2 is pixel-identical to baseline at settle 3 and 300; Godot selftest OK.

**Tracker delta (`scene_opcode_coverage.py SC0000`):** GAP 68→**64** ops (836→**741** instrs), impl 49→**53**,
correctly-handled 60→**65/129 (50.4%)**.

**⚠ Honest scope — the opening `AE*` explosion does NOT visibly animate from this chunk.** The opening's `AE*`
effect (`AE001D → AE002B → AE003B`) is a **`sleep`-paced sequence of RETAINED-object loads/draws** — verified
2026-07-08 from native code + the raw bytecode (NOT our `gfx` oracle, which mis-reported these as "slot 0"; see
engine-re.md "opening render path is RETAINED"). `draw-texture` binds a retained object by handle
(`gfx_object_bind_draw`@`0x47e870`); the SC0000 CG loader supplies `handle = CG_array[G[0x62450]]` (the INIT2
handle array `G[0x62455..]`) and a per-object working slot `G[0x62452]`, with `sleep` (`0x64`/`0x3e8`/`0x2ee`)
between steps. Our VM executes the whole load/draw/`sleep` burst **instantly** (no timing, no per-frame present),
so we only ever see the *final* retained state; the intermediate `AE*` frames never get a frame to display. This
per-object alpha channel is the correct foundation for **retained-sprite** animation (character fades/scales via
the handle system), but the opening explosion needs **frame-pacing** — modeling the scene-coroutine / `sleep 0xc8`
timing (`0x7b`/`0x140`/`0xc8`, still GAP) so the burst is *not* collapsed. That is the clearly-scoped next chunk
for the visible opening animation, and a genuinely different subsystem than the transform/alpha channel landed
here. (**Correction:** an earlier draft of this note called it "immediate-mode slot-0 blits" — wrong; the engine
is retained, per the native `draw-texture → gfx_object_bind_draw` bind.)

### A2b — Frame-paced `sleep` (2026-07-08) — ⚠ did NOT make the opening animate (corrected)

The chunk the animation-subsystem note above flagged as "the clearly-scoped next chunk." Spec/plan
`docs/superpowers/{specs,plans}/2026-07-08-frame-paced-sleep{-design,}.md`; RE `docs/engine-re.md` ("sleep (op
0xc8)").

> **⚠ CORRECTION (2026-07-08).** The original heading here ("the opening animates ✅") and the "Verified
> visually" claim below were **WRONG** — a misread. The `sleep` op is correctly decoded + implemented and the
> **one-shot dramatic pauses now work**, but the **rapid opening CG/AE\* burst is NOT sleep-paced** and did not
> start animating. Execution trace (via the new `--trace-histogram`) shows the back-to-back
> `set-texture→draw-texture` swaps run with **no** `sleep`/`wait`/`present`/coroutine between them; what actually
> paces them is still **unknown**. The `--shot-sequence` frames I read (arcane → Lily → maid → sky) were the game
> holding on key CGs via the **sparse one-shot sleeps** (slowed further by per-frame PNG-IO), which I mistook for
> the burst stepping. How the mistake happened: I inherited "the opening is sleep-paced" from this repo's own docs
> and treated it as verified instead of tracing execution first. What IS solid: the `sleep` seam, the one-shot
> pauses, the `GfxState` race fix, and the tooling. Related: the "493k sleeps" that confused me were a **headless
> artifact** (the name-entry poll loop), since fixed — see the "Headless divergence" note below.

**What `sleep` actually is (RE-confirmed, correct):** the Godot compositor (`Main.Recomposite` in `_Process`)
presents live `GfxState` every frame; `sleep` (`0xc8`) was a GAP so the VM ran the whole burst in microseconds.
Implementing it makes the **explicit one-shot sleeps** (1000/750/200 ms) pause correctly — but those are the
dramatic holds, not the rapid burst's pacer.

**RE (Ghidra):** `sleep_op_0xc8`@`0x420ec0` is **non-blocking** — it arms a main-loop-polled timer
(`sleep_timer_arm`@`0x44cff0`; start = ms tick, duration = operand). **Operand unit = milliseconds.** (Also
carries anti-tamper + a gfx cmd-type-3 write, neither needed host-side.) `0x20c` = `gfx_op_0x20c_present_frame`
→ host-implicit (our compositor presents continuously) → `noop_headless`.

**Implemented:** `IHost.Sleep(long)` + VM `case "sleep"` forwarding the raw operand; the 9 non-Godot hosts no-op
it → **headless/CLI parity held** (sweep unchanged 284 exit/13 STEP-LIMIT, emitted offsets/steps identical,
selftest OK). `GodotAdvHost.Sleep` blocks the VM background thread `duration` ms (capped 10s) — the WaitForInput
suspend pattern, time-based — so the main-thread compositor presents each intermediate frame. Behaviorally
equivalent to the native non-blocking timer given our threading model.

**Race closed:** once the VM runs concurrently for seconds, the main-thread `SnapshotVisibleObjects` truly
overlaps VM-thread `_objects`/`_registry` writes. `GetOrCreate`/`Register`/`Release` were unlocked → serialized
them on the existing (re-entrant) `_lock`. New `GfxStateConcurrencyTests` (deterministic repro of the
"Destination array is not long enough" crash) + `SleepDispatchTests`; **engine 52/52**.

**New tool** `--shot-sequence <dir> [--frames N]` (one PNG per frame, auto-advancing past input waits — a
time-based effect can't be captured by a single `--shot`). The frames it produced showed the game holding on
distinct CGs (arcane `AE*` → Lily → maid → sky) — but per the correction above, those holds are the **sparse
one-shot sleeps**, not the rapid burst stepping. (Residual, unrelated: intermediate frames show the cold-object
anchor doubling — a geometry issue independent of timing.)

**Tracker delta (`scene_opcode_coverage.py SC0000`):** GAP 64→**62** ops (741→**714** instrs), impl 53→**54**
(`sleep`), safe-noop 12→**13** (`present-frame`), correctly-handled 65→**67/129 (51.9%)**.

**Open (the real burst pacer):** what advances the rapid opening CG/AE\* burst frame-to-frame is **unknown** —
not `sleep`, not `present-frame` (only 2× in the whole scene), not the coroutine ops (absent from the burst).
Next: profile the **real Godot run** (`--trace-histogram`) of SC0000's `0x3958–0x3973` loop + gfx-op sequence.
The scene-coroutine framework was deferred here; the bounded host model is completed below (2026-07-09).

### Diagnostics framework extended (2026-07-08)

Motivated by the misread above (a `--trace-steps` dump was 2.5M lines → grep/awk). Added, all observe-only
(parity preserved): **`HistogramTraceSink`** (op + call-site `script:pc` execution counts + sample operand),
**`TraceSinkBase`** (per-script step attribution across call-script frames), **`TextTraceSink`** op-filter
(`--trace-ops`), **`CompositeTraceSink`**, `OpcodeTable.ByLabel`; CLI `--trace-histogram`/`--trace-ops`; Godot
`--trace-histogram <file>` (profiles the REAL run) + `--sleep-scale`. See `docs/tools-reference.md`.

### Headless divergence FIXED — faithful halt-at-wait (2026-07-08)

The histogram pinned the goose-chase root cause: op `0x72 wait-for-input` was a **no-op headless**, so a run
plowed past all 166 of a scene's prompts into the name-entry poll loop (`INPUTNAME.BIN`) and spun `sleep 1`
**493,182×** to STEP-LIMIT — a path no real playthrough reaches. Fix = **`VmOptions.HaltAtWaitForInput`**: the VM
halts (reason `wait-for-input`) at `0x72`. **`run`/`play` faithful by default** (SC0000 → ~402 steps / 0 sleeps,
matching the real path to the first prompt; `--plow` = old walk-every-page); **`sweep` plow by default** (dialogue
oracle, 284/13 unchanged) with `--halt-at-wait` → all 297 scenes halt cleanly (0 STEP-LIMIT). Godot unaffected
(really blocks on input). The corpus's 13 STEP-LIMIT scenes were all this artifact, not VM bugs. `HaltAtWaitTests`.

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

---

## ✅ call-script EXECUTION (2026-07-07) — subroutines now run in the C# VM

Spec `docs/superpowers/specs/2026-07-07-callscript-vm-execution-design.md`; plan
`docs/superpowers/plans/2026-07-07-callscript-vm-execution.md`. Enabled by the native-RE finding that
`call-script <id>` is a raw SYS4INI file index (`docs/engine-re.md`).

**Shipped (engine 25/25 green):**
- **`IScriptProvider`** (Hosting) + **`Sys4ScriptProvider`** (Sys4): `id → build/callscript-names.json →
  name → Paths.Scripts() → Sys4Loader.Load`, cached. Injected into the VM so `Vm` never references `Sys4`.
- **`ExecFrame` refactor** of `VirtualMachine`: per-script state (script, pc, locals, intra-call stack,
  per-frame emit-guard) moved into `ExecFrame`, run by a recursive `RunFrame`. Globals/Emitted/Steps stay
  VM-level (shared). Emitted lines now carry their source script name.
- **`call-script` executes:** loads the child, runs it as a nested frame sharing globals, returns to the
  caller at `pc+1`. `exit`/`exit-script` and empty-stack `ret` return from the frame (top frame → HALT).
  Depth-capped (`VmOptions.CallDepthCap=64`; native limit 38). Shared globals are the return channel;
  per-call locals are discarded on return.
- **Product paths** (`Age.Cli run/play/sweep`, `GameSession.RunScene`) inject the provider. `trace` +
  the `audio`/`gfx` diagnostics stay provider-less (base-ISA oracle / built against stub behavior).

**Design decision (refines the spec):** a VM with **no provider** falls back to the prior stub
(`host.CallScript(id); pc+1`), not a halt. This keeps every existing base-ISA test byte-identical
(they construct provider-less VMs) and needs no golden-fixture regeneration; **vm0.py retires from
oracle duty gracefully** — `trace`/`TraceDiffTests`/`WaitForInputTests` stay on the stub path as the
base-ISA guard, with zero lockstep maintenance.

**Validation:** 6 new tests (fake-provider unit tests: return-to-caller, callee `exit` returns not
halts, shared-global visibility, per-frame local isolation, unresolved-id halt, no-provider stub;
integration: ADDILL executes ADDILLSUB+CALCREVISE and reaches its own exit; BUNKI's top-level `ret`
returns cleanly). **Corpus sweep (execution on): 284/297 exit clean, 13 STEP-LIMIT, 0 depth-cap, 0
unresolved, 0 crashes.** The 13 STEP-LIMITs are input/state-gated ADV scenes (SC0000 etc.): executing
subroutines makes their global-writes drive caller loops that headless can't break (no input;
`WaitForInput` is a no-op) — the known state-divergence, not a call-script bug (loops hit STEP-LIMIT,
not the depth cap → recursion is bounded correctly).

**✅ Godot wired + testing approach corrected (2026-07-07, follow-up).** The provider is now injected
**everywhere** — no path runs with call-script disabled:
- **Godot play path** gets `Sys4ScriptProvider`, so subroutines execute live on screen (and the
  headless input-wait loops break on real player input, which is why headless-only scenes STEP-LIMIT).
- **Testing principle (user-directed): synthesize test data; never disable a feature to keep a real
  scene matching a frozen number.** New `Age.Engine/Sys4/ScriptAssembler` (code+strings → `Script`;
  also Phase-D modding-assembler groundwork). `SyntheticSceneTests` runs an assembled scene (show-text
  + wait-for-input + real nested call-script + shared-global return) with full handling and asserts its
  exact output. `WaitForInputTests` reworked onto a synthesized two-page scene. `RecoverTests` keeps its
  ISA-litmus role with call-script handling ON via a no-op subroutine **double** (isolates the ISA from
  the real subroutines' state deps). **`TraceDiffTests` retired** (it matched the C# VM to vm0.py's
  stubbed trace; vm0 is off oracle duty and we don't gate handling to keep it matching).
- **Godot `--selftest` rewritten:** was "run SC0000 stubbed, match vm0-trace(186)"; now runs a
  synthesized scene through the Godot thread/semaphore/CallDeferred plumbing and asserts it matches a
  **live headless run** of the same scene — full handling, no frozen golden, no vm0 dependency. Verified:
  `godot --headless -- --selftest` → "threaded host matches headless (3 lines, full handling)".

**Verified live in Godot (2026-07-07):** added `--scene <NAME>` to the frontend and a scene-end report
of the call-scripts executed as nested frames (collected thread-safely — Godot drops `GD.Print` from the
VM background thread). **SC0240 executes 29 call-scripts** (RESETLAND, SETEN, ADDEN, RENDERMAP, SETOBJ,
DRAWOBJ, CALCREVISE, LOOK) live in the real runtime; SC0000 renders the opening event CG (windowed).

**Remaining follow-ups:** optionally give the `audio`/`gfx` CLI diagnostics a provider (they still run
provider-less); **engine-level diagnostics** (the next pivot — the engine, not the frontend, should
surface script/scene execution + call-script dispatch); `decision→scene` (scene chaining) rides this
same loader once the SCJUMP decision→scene-id native hop is reversed.

## A2b — opening speed-through: the pacing lead is ENGINE CADENCE (2026-07-08)

> ⛔ **Correction.** An earlier draft of this section claimed "there is no missing pacer — all opening
> pacing primitives are already shipped." That was **retracted**: it was inferred from *headless op-counts*
> (which cannot render) and it contradicts the direct eyes-on observation that the opening **visibly speeds
> through**. Ground truth = it speeds through; the pacer is real and the cause is still open. What survives
> below is only the mechanically-verified part plus the corrected hypothesis.

**Verified (mechanical).**
1. The "rapid burst" seen in *plow* traces is a headless fiction (input-plow). The real back-to-back
   `set-texture→draw-texture` swaps are the **per-page compositor** `label_1235a` (instruction indices
   `0x3958–0x3973` = dword `0x123e8–0x12497`): a loop over ≤8 gfx object slots that per slot queries state
   (`0x215`), erases/releases (`0x1f7`/`0x1fa`), and draws with alpha (`0x203`).
2. Op `0xcd get-input-type` is **unmodeled** in the VM (no `case`; only `0x72 wait-for-input` is handled —
   two input mechanisms, one modeled). `INPUTNAME.BIN:0x1c1` name-entry is a `get-input-type→jcc→sleep 1→jmp`
   poll that spins unfed. This is a **real but separate** gap (interactive blocker), **not** the speed-through.

**The corrected hypothesis (engine cadence, not bytecode).** Many opening draws are **back-to-back with no
`sleep` between them in the bytecode** (e.g. resId `0x29` twice at `0xc45`/`0xc52`; `0x34`→`0x35` at
`0x1467`/`0x1479`), yet the real game paces them. So the pace comes from the **engine's execution cadence**,
not a script primitive. Corroboration: holding **Ctrl fast-forwards ADV** in the real game (faster, not
instant) — a global engine speed governor over interpreter advancement. **Suspected cause in our port:** the
Godot VM runs on a **free-running background thread** that is not synced to the 60fps compositor, so it blasts
an entire page's draws in microseconds and the main-thread `Recomposite` only ever samples the final gfx-state
→ the intermediate CGs collapse. The native engine is a cooperatively-scheduled main loop where the interpreter
yields per frame under vsync (consistent with `sleep` already being RE'd as a *non-blocking, main-loop-polled*
timer — which only makes sense if the interpreter yields back to that loop).

**Next (open).** (1) Determine what actually landed re 60fps/vsync and the VM threading model in `godot/`.
(2) RE the native engine main loop in Ghidra: how it ticks the interpreter per frame, the frame cap/vsync, and
the Ctrl speed governor. (3) Frame-lock our VM to the render loop (a per-frame step budget or a per-frame
yield/sync point) instead of the free-running thread. Confirm any fix against **pixels** (windowed
`--shot-sequence` → real PNGs), not op-counts.

### A2b — Frame-stepped VM ✅ DONE & MERGED (2026-07-08)

Executed the "Next (open)" list above (option 3: a per-frame yield/sync point). Spec
`docs/superpowers/specs/2026-07-08-frame-stepped-vm-design.md`, plan
`docs/superpowers/plans/2026-07-08-frame-stepped-vm.md`; merged to `main` (`74a4221`).

**What landed (3 TDD tasks):**
1. `engine/Age.Engine/Hosting/FrameClock.cs` — pure, threadless virtual clock: `NowMs`, `Speed` (field,
   1.0), `OpsPerFrame` (30), `Advance(realΔseconds)`, `EffectiveBudget` (= `OpsPerFrame*Speed`, min 1).
2. `IHost.FrameYield()` — the VM (`VirtualMachine.RunFrame`) calls it **once per executed opcode** (right
   after `Step`). **No-op in all 9 headless hosts** (`CaptureHost`, the CLI trace hosts, every test host) →
   headless output byte-identical. This is the parity guarantee.
3. `godot/GodotAdvHost.cs` + `godot/Main.cs` — the throttle. `Main` owns a `FrameClock`; each `_Process`
   it `Advance(delta)`s the clock and `PulseFrame()`s. `GodotAdvHost.FrameYield()` counts ops and, once
   `EffectiveBudget` is reached, **blocks the VM background thread** on an `AutoResetEvent` until the next
   `_Process` advances the clock — throttling the interpreter to ≈`OpsPerFrame` ops per rendered frame
   (≈30 → ≈1800 ops/sec, the native rate-limited cadence). `Sleep` now waits on the **same** clock (not
   `Thread.Sleep`), and the anim tween reads `_lastDelta * _clock.Speed`, so the single `Speed` factor
   scales throttle + sleep + tween coherently. `Speed` is the future **Ctrl fast-forward** hook, left
   **unwired at 1.0** (wiring it needs ADV-mode-scope RE; the seam is ready).

**Verified.** Engine 62/62; `sweep` exit=284/STEP-LIMIT=13 (parity); Godot `SELFTEST OK`; windowed
`--boot --shot-sequence` = **16 distinct paced visual states across 120 frames** (event-CG → fade/transition
→ onward) instead of an instant jump to the final frame — the throttle demonstrably steps the opening over
real time.

**⚠ Residual (NOT this slice — the graphics geometry/blend subsystem).** By-eye the opening is still hard to
judge because the **graphics are still wrong** (AE* fades draw opaque with no alpha/blend; cold-object
anchors double; multi-surface compositing approximate). Pacing is fixed and mechanically confirmed against
pixels, but visual correctness is a **separate open chunk** the user has deferred. Do not conflate "pacing
fixed" with "opening looks right."

**Next candidates (deferred).** (a) Graphics geometry/blend fidelity — AE* alpha/blend + per-frame
compositing + cold-object anchors (the thing that makes the paced opening actually *look* right). (b) Wire
the Ctrl `Speed` multiplier (ADV-mode-scope RE). (c) Full scene-coroutine framework (`0x7b`/`0x7c`/`0x140`)
for interactive multi-object scenes (**completed below, 2026-07-09**). (d) Model `0xcd get-input-type` (name-entry interactivity, the separate
input gap noted above).

### A2b — Blend & transparency (slice A) ✅ DONE (2026-07-08)

First of three graphics-fidelity slices (A blend/transparency, B geometry/anchors, C render-targets).
Spec `docs/superpowers/specs/2026-07-08-blend-transparency-design.md`, plan
`docs/superpowers/plans/2026-07-08-blend-transparency.md`, branch `feat/blend-transparency` (engine 69/69,
sweep exit=284/STEP-LIMIT=13 parity, `SELFTEST OK`).

**Landed (hybrid: engine resolves, host blits):**
- `Age.Engine/Model/BlendMath.cs` — pure colorkey match + ARGB unpack (colorkey format reversed:
  op arg 3 `<0` = none, else `0xRRGGBB` exact-match, `0` = key black; baked at surface-load, see
  engine-re.md §Blend).
- `RenderObject` gains `Alpha`/`Tint`/`Blend` (`BlendKind` Opaque|Alpha|Additive); `GfxObject.HasColor`;
  `GfxState.SetObjectColor`; `SnapshotVisibleObjects` resolves them. `0x202`/`0x203` route through
  `SetObjectColor` (the stale "alpha deferred" trace stub is gone — alpha is now consumed).
- Godot compositor: colorkey-baked image cache (keyed by `(path, colorKey)`), `BlitLayer` applies
  colorkey + object alpha + RGB-tint modulate, and **surfaceless colored objects fill a tint×alpha quad**
  (the fades) instead of being skipped.

**Result (pixels):** page-1 event-CG composites cleanly (dialogue + prompt, no opaque boxes); the opening's
mid-fade frames now alpha-blend (dark bg + light-ray burst, then a blended dark transition) instead of the
pre-change **full-screen opaque grey wall** that ate the CG. Verified via `--shot`/`--shot-sequence` on
`SC0000 --boot`.

**Deferred (documented, NOT built — confirmed by a stalled interp RE pass, engine-re.md §Blend):**
smooth color-animation *interpolation* (the fade ramps snap to the correct end-state rather than gliding —
the `0x202` color channel's blit consumer + clock coupling is a dedicated dig) and **additive/glow blend**
(`local_2c` mode 2/3; its object field isn't pinned). `BlendKind.Additive` is an unused seam. Next graphics
slices unchanged: B (geometry/anchors — sprite *placement*) and C (render-targets).

### A2b — SC0000 anim/transform/spritesheet cluster (partial) ✅ DONE (2026-07-08)

The `0x21c`–`0x243` gfx cluster, scoped to a **tractable subset** after Task-1 RE revealed it's heterogeneous
(setters + queries + matrix/scale + a movie op). Spec `docs/superpowers/specs/2026-07-08-sc0000-anim-transform-cluster-design.md`,
plan `.../plans/2026-07-08-sc0000-anim-transform-cluster.md`, branch `feat/anim-transform-cluster`
(engine 79/79, sweep exit=284/STEP-LIMIT=13 parity, `SELFTEST OK`, coverage SC0000 67→74/129 handled).

**Key RE unlock:** the dispatch table `handler(op)=ctx[0x26c93+op]` recovered statically from `FUN_00413860`
(the `opcodes.toml` `u004xxxx` labels are Kelebek drift). Full op→field map in `engine-re.md` §SC0000 anim cluster.

**Built (hybrid: engine resolves, host blits):**
- `GfxObject` gains src-rect (spritesheet) + animated-color channels; `GfxState.SetSrcRect` / `SetColorAnim`;
  `BlendMath.PingPong`; `SnapshotVisibleObjects(long nowMs)` = a port of `gfx_object_anim_interpolate`
  (ping-pong the spritesheet cell + color/glow), driven by the **`FrameClock`** (the "mach 5" pacing fix).
- Wired 7 ops: `0x22f`/`0x229` position (direct V24 set), `0x239`/`0x231` spritesheet (static cell / animate),
  `0x232` color glow (ping-pong), `0x228`/`0x23f` queries (geometry back to script vars).

**Deferred (own follow-ups, per scope decision):** `0x21f`/`0x223` matrix/scale (need affine rendering),
`0x236` timed/movie op, and the rare `0x21c/0x21d/0x224/0x242/0x243/0x23d/0x20a/0x20e` tail. Adjacent
non-cluster gaps remain: `draw-string 0x204`×205 (on-screen text) and `play-sound-effect`. **Whole-scene
visual validation is the user's call** (they deferred confirmation until the scene is coherent).

### A2b -- Scene-coroutine host model (2026-07-09) -- DONE

The native mechanism is fully reversed in `docs/engine-re.md` under Scene-coroutine framework.
The port deliberately models its observable ADV lifecycle instead of emulating the runtime-resolved
video service behind op `0x140`.

**Bounded model for this slice:**

1. Detect only the corpus-wide ADV form `0x140 out "LABEL" "J" in`. `TITLE.BIN`'s unrelated
   `"BIN" "SC????.BIN"` use remains unmodeled and must not acquire ADV scene-entry behavior.
2. On a top-level entry at offset zero, synthesize native scheduler state `G[0xaba5c]=1` for a script
   containing that ADV form. A captured global-write snapshot cannot supply it because the native scene
   loader does not set it through the script operand-write helper.
3. At each ADV labeled-yield site, force exactly one setup-body iteration, then return the terminal value
   encoded by the site's following `mov terminal, immediate` + `eq terminal, out` sequence. This handles a
   stale prior-scene `out` value and avoids hardcoding SC0000's `0x45e`; all 138 corpus ADV sites share the
   same shape.
4. Record op `0x7b`'s two saved handler PCs as frame metadata. Consume op `0x7c` as the host-scheduler
   resume marker: the port's existing `IHost.FrameYield`/`FrameClock` path supplies per-frame pacing, so it
   does not recursively execute the native render/poll/yield bytecode handlers.

**Acceptance gates:** a synthetic stale-terminal scene runs its setup body once and reaches content; a
non-ADV `0x140` remains unchanged/stub-reported; real SC0000 executes op `0x140` twice, clears
`G[0xaba5c]`, and fills the slot-table columns (`G[0x3239..0x324e] = 4..11`) before content. Then run the engine
suite, corpus sweep, Godot self-test, and live/pixel validation of the previously grey multi-layer page.

**Result.** The bounded model landed in `VirtualMachine`/`ExecFrame` with four focused tests. SC0000 now
executes `0x140` twice, runs `label_125bd` once, clears the native entry gate, and loads resource `0x23`
into assigned slot 5 rather than the broken slot 0. Static corpus validation found 138 ADV sites and zero
shape mismatches; the one non-ADV `TITLE.BIN` site remains stub-reported.

**Verified:** engine **85/85**; full sweep unchanged at **284 exit / 13 STEP-LIMIT**; Godot threaded
self-test `SELFTEST OK` (3 lines, full handling). The native transition timing remains host-approximated.
SC0000 coverage is now **77/129 handled (59.7%)**, with 52 GAP ops / 613 GAP instructions.

**Follow-up — magic-circle teardown fixed (2026-07-09).** Ghidra caller analysis corrected op `0x215`:
it queries the retained gfx-object map and returns `obj+4`, the source surface slot written by
`draw-texture`; it does not query op `0x1a2`'s descriptor hash. The old host model returned -1 for CG
handles, so SC0000 skipped its explicit `0x1f7(handle,10)` + `0x1fa(slot)` cleanup and left
`AE001H.AGF` (resource `0x37`) visible. `GfxState.QuerySlot` now returns the bound source slot,
`0x1f7` erases retained objects, and `0x1fa` clears the surface. A booted SC0000 integration regression
asserts no visible resource `0x37` remains; engine suite **86/86**. **Live clicked-path validation
confirmed the fix on 2026-07-10:** the magic circle now disappears at the intended transition.
