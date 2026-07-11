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
opening-path anim ops — `0x21e`, `0x220`, `0x234`, and `0x238` — were first retained in
the port here. **Superseded by the 2026-07-10 matrix slice below:** the original shared-target interpretation was
wrong; `0x21e` is scale, `0x220` is translation, and `0x234` is cyclic rotation.

**Historical compositor:** this slice temporarily used transform Z as alpha. The 2026-07-10 follow-up removes
that approximation and applies the native separate scale/translation channels instead.

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
retained-object channel was the correct architectural seam, but the opening explosion needs **frame-pacing** —
modeling the scene-coroutine / `sleep 0xc8`
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

> **Later resolution (2026-07-10):** the matching native presentation trace below established that ordinary
> mutations run burst-fast and first become visible at `0x20c`, `0x21c`, sleep, or stable input boundaries.

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

**Historical implementation, superseded on 2026-07-10 by the native retained-presentation trace below.**
The validation recorded here was accurate for that slice, but the inferred native per-op cadence was not:
service waits had been folded into an operand-fetch average. Commit `85fc07d` removed the production opcode
throttle; ordinary work now runs burst-fast to proven publication/pacing boundaries.

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
   resume marker: the host supplies service-boundary suspension and retained presentation, so it
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

### A2b — native scale/translation channel split ✅ IMPLEMENTED; exact visual math provisional (2026-07-10)

Replaced the legacy shared `AnimTarget` / transform-Z-as-opacity approximation with the native channel
split proven in Ghidra. Op `0x21e` now owns normalized scale (100 = identity; current `obj+0x6c`,
target `obj+0xac`, delay/duration `obj+0x3c/+0x50`). Op `0x220` owns absolute translation
(current `obj+0x16c`, target `obj+0x1ac`, delay/duration `obj+0x44/+0x58`). Consumer
`gfx_object_apply_transform_channels` (`0x472f00`) establishes a shared first-frame start
timestamp, independent delayed linear interpolation, and target commit on completion.

`GfxState` samples both channels from `FrameClock`; Godot applies scale around V18 plus independent
translation, including nearest-neighbor scaling/flips in the software blitter. Op `0x234` no longer
overwrites either matrix: the completed consumer RE corrected it to a separate cyclic rotation period+axis
channel (retained now; affine rendering deferred). No transform Z value contributes to opacity.

**Focused validation:** six matrix/rotation tests cover channel independence, VM dispatch, shared-start
delay/duration sampling, target commit, and non-opacity Z values. Engine **86/86** and Godot build pass.
Booted SC0000 with `--shot-sequence` + `--gfx-log`: 180/180 PNGs, 152 log lines, and zero
unresolved/error/NaN/Infinity outcomes. The visible circle expands around its anchor through sampled scales
`1.00 → 1.22 → 1.44 → 1.66` while remaining `op=1.00`, then the retained object is logged
`GONE`. This validates removal of the transform-Z opacity shortcut and shows the scale channel affecting
geometry in the capture. It does **not** prove the port's exact matrix calculation, anchor interpretation,
multiplication order, or 2D projection: normal playback still races past these sections too quickly for a
reliable visual judgment. Treat that math as provisional until the pacing slice enables slow normal playback
and a native-versus-port frame comparison.

### A2b — animation pacing + matrix validation ✅ (2026-07-10; pacing model superseded)

This slice initially interpreted roughly 1,788 `vm_operand_fetch` calls/s as evidence for a
refresh-independent 200 completed-opcode/s allowance. That scheduler interpretation is **superseded**:
the probe counted operand reads and mixed burst execution with time parked in native services. The matching
presentation trace below proves ordinary work is burst-fast; `--speed` now scales sleeps and retained
presentation clocks, not opcode throughput. Input remains ignored unless the VM is actually at
`wait-for-input`.

Live native capture recorded the complete `0xcbc0` scale ramp (1→5 over 1,890 ms), including the exact
composed matrices. The 215/s and 200/s port replays were useful historical diagnostics showing that object
lifetime had been coupled incorrectly to script progress, but those rates are not native scheduler constants.
Native matrix terms and the port's focused tests agree on row-vector
`anchor + (point-anchor)*scale + translation`; for base `(0,600)`, anchor `(400,1000)`,
scale 5, both project to `(-1600,-1000)`. Exact axis-aligned anchor/order/projection is validated;
cyclic-rotation rasterization remains the next affine-rendering slice.

Verification: engine **92/92** after the opcode-clock reset test, corpus sweep unchanged at
**284 exit / 13 STEP-LIMIT**, Godot build and threaded `SELFTEST OK`. The transform capture tool and
transform-aware `--gfx-log` are documented in `docs/tools-reference.md`.

### A2b — ADV transition/lifecycle diagnosis plan ✅ COMPLETED BELOW (2026-07-10)

This is the next SC0000 correctness slice. It is driven by live A/B observations, not by static opcode
coverage alone. The direct SC0000 histogram is currently **80/129 distinct ops handled (62.0%)** and
**96.2% instruction-weighted**, but a rare query, scheduler op, or render worker can still control an
entire visible section. Coverage also excludes called scripts; this slice follows only callees actually
entered on the failing path rather than expanding into blanket subscript completion.

**Observed native ADV contract (client behavior):** foreground presentation changes are transitions even
when they are only in-place fades. SC0000 begins black and fades into the first CG; the message window then
fades in before text reveals. During a CG swap, the window transitions out, one or more CG transitions run,
then the window transitions back in. A click during an active foreground transition completes it immediately
and lets the next presentation step start; a click at a stable `wait-for-input` advances the script. Ambient
retained animation is a separate class and must not all be completed by that click.

**Current port failures (2026-07-10):**

- The initial black state holds and then the first CG pops in instead of fading. This is consistent with the
  port applying op `0x202`'s endpoint as a static tint while its native animated-color consumer remains
  unmodeled.
- The retained textbox artwork appears to begin fading in, then disappears. The Godot `Label` shortcut stays
  visible; it is deliberately out of scope for this slice because native `draw-string`/text presentation will
  replace it before SC0000 is called complete. Diagnose the **box object's** lifetime, not the shortcut text.
- The late animation burst before the first music change is badly wrong, then the screen becomes white and
  interactive play does not proceed to the music change.

**Concrete failing boundary.** `BGM005` begins at SC0000 offset `0x7fa`; the expected first change to
`BGM008` is `play-bgm 0x8` at **`0x1728`**. The immediately preceding block `0x133f..0x1725` exercises
repeated `0x202/0x203` color operations and the implemented geometry/scale family, but also directly executes
three still-stubbed gfx ops: **`0x236`** at `0x13c8`, **`0x1fd`** at `0x14f3`, and **`0x21f`** at `0x159a`.
It calls the shared animation finalizer `label_1235a` several times, including at `0x1725`; that finalizer
sets op `0x238`'s duration, then reads still-unimplemented **`0x1c7 get-message-skip`** and
**`0x1cc get-adv-service-state`** to choose its present/yield path. Therefore the white stall could be an
object/compositor error, an unmodeled foreground-transition gate, or wrong control flow caused by a stubbed
output—not safely assumed to be “just interpolation.”

#### Investigation order

1. **Make the failure boundary deterministic before changing semantics.** Reproduce from `--boot` with
   auto-input and a long enough `--shot-sequence`, plus `--gfx-log`. Add a single synchronized diagnostic
   timeline if the existing logs cannot answer the boundary: frame/virtual time; active script + PC/opcode;
   VM state (`running`, sleep, transition, input wait, halt); BGM event; and every changed visible object's
   handle, surface slot/resId, tint/alpha, transform, and lifecycle event. Use `play-bgm 0x8 @ 0x1728` as the
   reachability sentinel. Do not judge progress from the white pixels alone.

2. **Classify before fixing.** If the VM reaches/passes `0x1728` while the frame stays white, identify the
   topmost white/fill object and whether its handle remains visible, loses/rebinds its live surface, or has a
   stuck color endpoint. If the VM never reaches `0x1728`, record the last PC and whether it is sleeping,
   input-waiting, transition-waiting, polling, halted, or still executing. If the executed path itself is
   suspect, capture the same native passage with `trace_engine_ops.py` and use `diff_optrace.py` to find the
   first engine/port offset divergence.

3. **Track the textbox artwork as an AGE object.** From its first visible frame, identify its retained handle
   and follow bind, color/animation, present, erase, release, and surface-rebind events through the first CG
   swap. The key result is one of: `GONE` (premature erase), still present but covered (z/lifecycle input),
   still present but transparent/tinted (color channel), or bound to a replaced slot (surface lifetime).
   Compare only those corresponding native object events; do not spend this slice synchronizing the Godot
   text overlay.

4. **Recover the foreground ADV transition contract.** Reverse/capture the producer behind
   `get-adv-service-state` (`0x1cc`), implement the already-known `get-message-skip` output (`0x1c7`), and
   observe what a click changes during the initial fade and the pre-`0x1728` burst. Establish an explicit
   host-level foreground transition with `start → per-frame progress → natural/forced completion → resume`.
   Click completes and consumes the active foreground transition; only a stable input wait advances content.
   The wall clock remains the progress source and opcode pacing remains a guard within runnable bursts, not
   the mechanism that decides how long a presentation state lives.

5. **Reverse only the executed missing gfx dependency that remains causal.** Triage the three direct gaps in
   failing-order: `0x236` (timed/animated-surface worker), `0x1fd` (scaled vector/animation setter), and
   `0x21f` (affine/matrix channel). For each, capture native inputs, retained fields, and sampled output at the
   exact SC0000 site; implement it with a focused VM/state/compositor test. Do not declare a stub harmless
   merely because it is rare, and do not implement the whole remaining opcode list without evidence.

6. **Validate as presentation checkpoints.** Native/manual observation remains the final visual oracle, but
   each check should first have machine evidence (PC reached, object identity/lifetime, transition progress,
   and final state). Required checkpoints: black visibly ramps into the first CG; the textbox artwork survives
   until its intended transition-out; a CG swap orders window-out → CG transition(s) → window-in; clicking an
   active transition snaps to its endpoint without also advancing a stable page; the late burst has no stuck
   white owner; and execution reaches `BGM008 @ 0x1728`. Re-run engine tests, corpus sweep, Godot threaded
   self-test, and the SC0000 coverage report after each landed opcode or scheduler change.

**Stop conditions / scope guard:** this slice is complete when the port reaches `0x1728` interactively and
the above foreground transitions have correct lifecycle/click behavior. Native glyph rendering, configurable
text reveal speed, and unrelated subscript opcode completeness remain separate work. Any called script proven
to own the first divergence becomes an explicit dependency of this slice; otherwise it stays out of scope.

#### Investigation 1 result — deterministic boundary classification (2026-07-10)

Added the observe-only Godot `--timeline-log <jsonl>` diagnostic so VM steps (real byte offsets), virtual
time/frame, host state, BGM events, and changed visible-object outcomes share one ordered stream. The
reproduction was `SC0000 --boot`, stable-wait auto-input via a long `--shot-sequence`, `--gfx-log`, and a
uniform diagnostic `--speed 8`; speed scales VM, sleeps, and animation clocks together and does not inject
input outside `wait-for-input`.

**Classification: not a VM/control-flow stall on the deterministic path.** The run executed all three direct
gaps (`0x236 @ 0x13c8`, `0x1fd @ 0x14f3`, `0x21f @ 0x159a`), called the finalizer at `0x1725`, then executed
`play-bgm 0x8 @ 0x1728` in `running` state at frame 818 / virtual `70,262 ms`. The BGM event resolved to
`BGM008.OGG` in the same synchronized event and execution continued through `0x172b` and beyond; the full
1,800-frame run reached page 80 and five BGM events. Therefore a native offset-path diff is not warranted for
this boundary unless a separately reproducible manual-input path fails to reach the sentinel.

The full-screen fill owner is retained handle **`0xcf08`**, but it is not stuck at this boundary. It was a
transparent white `800x600` fill (`a=0.00`) when `0x1728` executed. Later, `0x203 @ 0x1337f` made it solid
white for one sampled diagnostic frame (frame 911); the following `label_1235a` path executed `0x1c7`,
`0x1cc`, and `0x21c`, and the compositor sampled the same handle back at `a=0.00` on frame 912. This is
evidence of a likely incorrect flash/color presentation contract, not evidence for the reported pre-BGM
infinite stall. No opcode, scheduler, or compositor semantic fix was made in this investigation step.

Validation after adding the diagnostic: engine **92/92**, Godot build clean, threaded `SELFTEST OK`, and
`git diff --check` clean. The headless `--shot-sequence` PNG capture path emits dummy-renderer `GetImage`
errors, but the CPU compositor/timeline completed and the same path already had this limitation; use a
windowed sequence when pixel files rather than object-state evidence are required.

### A2b — cyclic rotation and affine rasterization ✅ DONE (2026-07-10)

This bounded slice followed the non-reproduced white-stall classification above; it did not resume that
investigation and does not claim the interactive symptom is fixed.

**Native contracts.** Op `0x21f` is a delayed one-shot axis-angle rotation, not a generic matrix row:
`(handle,delay,duration,axisX,axisY,axisZ,angleDegrees)`. It shares `obj+0x34`'s start with scale/translation,
uses delay/duration `+0x40/+0x54`, and linearly samples current axis/angle `+0x1ec/+0x204` to target
`+0x1f8/+0x208`. Op `0x234` is separately anchored cyclic rotation with integer-degree phase
`floor(((now-start)%period)*360/period)`. The native call order reduces to
`T(-anchor)*scale*oneShotRotation*translation*cyclicRotation*T(anchor)`, so the cycle rotates translation.

Op `0x223` was also closed out but deliberately not implemented here: it inserts a type-0 timed-alpha record
in the surface command map, containing a target surface slot and two object ranges. SC0000's shared site
`0x129e7` passes `(handle+2, slot, handle+1,1,handle,1,delay,duration)`. This belongs to render-target/
foreground-transition presentation, not affine object state, and remains a visible GAP rather than receiving
an uncertain approximation.

**Native matrix oracle.** The retained trace's handle `0xcb8e` sample (anchor `(700,600)`, scale from 0.9,
axis `(0,0,1)`, 30° target, sampled 11 ms into a 390 ms ramp after 500 ms delay) is
`[0.9055,0.0134;-0.0134,0.9055]`, translation `(74.1449,47.3127)`; the focused port test matches it.
The two executed SC0000 cycle sites use periods 9000/13000 ms and axes `+Z/-Z`. A windowed port capture
advanced 563 ms from their first sample to phase angles `22°/15°`, exactly the native integer formula.

**Port result.** `GfxState` now retains/samples the one-shot rotation and cyclic start/phase. `Transform2DMath`
composes a row-vector 4×4 matrix and projects it to an invertible 2D affine transform. The Godot compositor
uses a pure inverse-mapped nearest-neighbour RGBA8 rasterizer for both textures and solid fills, preserving
the existing colorkey, tint-strength, opacity, clipping, flipping, and z-order paths. Native D3D9 filtering
can still differ at subpixels; the matrix/order is oracle-backed rather than approximated.

The windowed `--shot-sequence` run wrote 454 PNGs with 102 pixel-state transitions; the first cyclic passage
produced distinct affine frames as `0xcb8e/0xcb98` advanced. There is no corresponding native PNG sequence in
the workspace, so validation is matrix/phase exact plus port-pixel coverage—not a false claim of pixel-perfect
native frame equality.

**Ghidra.** Renamed/commented `gfx_object_set_rotation_channel` (`0x47eb70`),
`gfx_queue_surface_alpha_transition` (`0x47f440`), the surface-command map helpers, and
`matrix4_make_axis_angle` (`0x48b215`); corrected comments on the one-shot consumer, cyclic interpolator,
and composite call order; named useful parameters; saved `/v2`.

**Validation:** engine **97/97**; full sweep unchanged at **284 exit / 13 STEP-LIMIT**; Godot build clean
apart from the pre-existing nullable warning and threaded `SELFTEST OK`; opcode tooling and focused Python
tests clean; transform tool compiles; SC0000 coverage **81/129 handled (62.8%)**, 48 GAP ops / 602 GAP
instructions; windowed affine capture clean. Final whitespace/diff validation is recorded with the handoff.

### A2b — ADV foreground transition/click lifecycle ✅ DONE (2026-07-10)

**Native recovery and control-flow correction.** `0x1c7` is the message-skip run-state query.
`0x1cc` reads `ctx+0x6dbd4`, now `adv_read_skip_state`; `adv_refresh_read_skip_state@0x406cd0` plus the
text/label/wait handlers maintain it from `message_ReadTextSkip` and the current-PC read-history lookup.
It is not surface progress. The earlier reading of `label_1235a`'s branch was reversed: zero OR-state
(normal playback) reaches `0x21c` and sets scheduler bit `0x400`; nonzero skip/read state reaches
`0x243 + 0x20c` to present the endpoint. Synchronized execution caught and corrected this before validation.

The executed missing producer for `0x223` range A was `0x21d`: `gfx_object_clone@0x47e4f0` copies the full
`0x2d4`-byte retained-object record from the current CG handle to `handle+1`. SC0000 then rebinds the source
handle to the new CG and queues `0x223` with old clone range A, new source range B, and target presenter
`handle+2`. Op `0x203` mode 2 is the companion transition-source alpha/identity path; its `0xffffffff`
is opaque identity, not generic solid-white tint, and negative alpha/RGB preserve current static bytes.

**Port lifecycle.** `GfxState` owns explicit queue/start/wall-clock progress/natural-or-forced completion.
Normal `0x21c` parks only the VM thread until the foreground command completes while Godot continues
per-frame compositing. A click during that wait forces only the foreground transition to progress 1, is
consumed, and resumes the VM; stable `wait-for-input` remains a separate gate. Tests verify that forcing a
transition leaves an independent cyclic retained rotation active. The `0x20c` skip/read path starts and
snaps a pending foreground transition to its endpoint without the normal wait.

**Synchronized window evidence.** At normal speed, the second-to-third CG transition produced seven
successive distinct PNG hashes while progress advanced `0.133, 0.297, 0.442, 0.592, 0.742, 0.891, 1.000`;
the user independently observed that fade working in the live window. A timed click landed at progress
`0.157`, the next frame was the endpoint, and the stable page count did not advance. The accelerated
windowed run reached `BGM008 @ 0x1728` on frame 137 with non-white changing pixels (2,469 sampled colors at
the sentinel) and continued; its earlier white interval was finite and released before that boundary.
This is windowed pixel/object/timeline evidence, not the earlier headless reachability inference.

The retained dialogue-text surface `0xe678` still has no pixels because native `draw-string 0x204` remains
a separate GAP; the Godot `Label` shortcut remains outside this slice. Its create/erase events are explicit
in the object timeline and are not being mistaken for foreground CG transition correctness.

**Ghidra.** Renamed/commented `adv_refresh_read_skip_state` (`0x406cd0`),
`op_0x1cc_get_adv_read_skip_state` (`0x427330`), `op_0x21d_clone_gfx_object` (`0x423310`), and
`gfx_object_clone` (`0x47e4f0`); added `EngineCtx.adv_read_skip_state`; saved `/v2`.

**Validation:** engine **102/102**; full sweep unchanged at **284 exit / 13 STEP-LIMIT**; Godot build clean
apart from the pre-existing nullable warning and threaded `SELFTEST OK`; all six Python test scripts,
opcode/ctx lint, 481-script decode validation, RECOVER, and Windows CR-aware `git diff --check` clean.
The plain check reports only the generated reference's CRLF on its newly added row. SC0000 coverage is
**85/129 handled (65.9%)**, 44 GAP ops / 598 GAP instructions. This slice was later committed as `c9be9c5`.

### A2b — op 0x202 one-shot color/presentation ✅ DONE (2026-07-10)

**Native contract.** `gfx_op_0x202_worker_set_color_anim@0x47ea00` writes target packed ARGB at
`obj+0x64`, delay/duration at `+0x38/+0x4c`, and resets the one-shot family's shared start `+0x34`.
The missing consumer is the bit-1 path in `gfx_object_apply_transform_channels@0x472f00`: it seeds start
from frame time `ctx+0xb550`, performs an integer bytewise current-`+0x60` to target-`+0x64` LERP, then
commits target, clears timing, writes target `-1`, and clears the active bit once all sibling one-shot
channels finish. This is ordinary presentation-clock coupling, not op `0x238`'s separate service clock.
`ctx+0xb55c == 1` forces completion unless the object-local `+0x2d0` override bit is set. `/v2` comments
were corrected and saved.

**Port and endpoint diagnosis.** `GfxState` now retains current/target/delay/duration separately, shares
the start timestamp with scale/rotation/translation, and exposes synchronized `color=current->target` plus
progress in the compositor/timeline log. This fixes the initial black-to-EV049AA cut: windowed frames sample
the full-screen `0xcf08` black owner through 11 monotonically decreasing states from `1.00` to `0.00` instead
of installing the transparent endpoint immediately. The ADV textbox backing `0xd2f0` is a real 800×227 black
fill at `(0,373)`; repeated show/hide passages now produce intermediate samples (for example
`0.00, 0.26, 0.54, 0.82, 1.00` and the reverse), synchronized to the executing `0x202` sites around
`0x12283..0x12342`.

**Predecessor diagnosis, resolved by the next subsection.** The remaining full-white runs were not a stuck
color clock or d2f0 endpoint: pixel windows place them after
d2f0 is transparent, while object timelines show `AE*` handles first becoming visible in mode 0 with default
`0xffffffff` before their later mode-1 initializer. Native call-site disassembly proves op `0x203`'s
`obj+0x30` is passed directly as the D3D blend selector and mode 1 uses `SRCALPHA/INVSRCALPHA`; the port now
uses ARGB alpha as opacity and RGB as multiplicative modulation for that mode. The long white intervals did
not disappear, which localizes the residual to when intermediate retained state is presented/batched, not to
0x202 interpolation. Altering that scheduler boundary without a matching native present trace would exceed
this bounded slice. `draw-string 0x204`/`0x7a` was not pulled in.

Windowed evidence: 340 PNGs, 107 distinct pixel states; black-to-first-CG and d2f0 ramps agree with synchronized
object progress. Focused tests cover exact integer midpoint math, delay, natural completion field cleanup,
negative sentinel resolution, the 0x202-then-0x203 current/target pattern, shared color/matrix clock start,
mode-1 opacity/modulation, and the software rasterizer.

**Validation:** engine **108/108**; full sweep unchanged at **284 exit / 13 STEP-LIMIT**; Godot build clean
apart from the pre-existing nullable warning and threaded `SELFTEST OK`; all seven Python test scripts,
opcode/ctx lint, 481-script decode validation, and RECOVER clean. SC0000 coverage remains **85/129 handled
(65.9%)**, 44 GAP ops / 598 GAP instructions. The differential oracle retains its prior branch-state
divergence after `0x12031`; it agrees through the executed `0x202`/`0x203` sequence and does not implicate
this slice. Windows CR-aware whitespace validation is clean.

### A2b — retained presentation batching / native scheduler boundary ✅ DONE (2026-07-10)

The synchronized native trace resolves the white-hold residual. At the AE001D passage, the native engine
binds `0xcb8e/0xcb98`, applies mode 1 at `0xd5a/0xd63`, and arms `0x202` at `0xd73/0xd8a` within one
roughly 5 ms opcode burst. No `gfx_render_frame` occurs between those mutations; the first composition is
the following `0x21c` service loop. The preceding explicit `0x20c`/`0x125a6` mode-0 white frame lasts only
about 10 ms. The port's 200-completed-op/s throttle had stretched that between-present burst across multiple
window frames, making retained intermediate state look like a long white stall.

Godot now leaves ordinary opcode `FrameYield` unthrottled and publishes retained state only at proven
presentation-capable boundaries: `0x20c`, `0x21c`, sleep, and input wait. `0x21c` renders while visible finite
one-shot channels or `0x223` commands remain active; hidden stale records and ambient cyclic channels cannot
hold it open. The first implementation incorrectly included hidden records and parked at the first CG; the
visible-only correction was re-run through 14 pages and the full AE burst.

Windowed before/after evidence at the same SC0000 sites: the old capture exposed AE001D in mode 0 for six
compositor frames and held one identical white PNG state for 25 frames. The corrected capture executes bind,
mode 1, and color targets in frame 64 and first publishes AE001D already in mode 1 at `0x21c`; the old mode-0
AE object state is absent from the object/pixel timeline. Remaining short white flashes are explicit native
present/color effects, not the prior between-op hold. Draw-string `0x204/0x7a`, movie `0x236`, and SFX remain
separate slices.

**Validation:** engine **109/109**; full sweep unchanged at **284 exit / 13 STEP-LIMIT**; Godot build and
threaded `SELFTEST OK`; all seven Python suites, opcode/ctx lint, 481-script decode, and RECOVER clean.
Normal-speed windowed capture wrote 220 PNGs and progressed through the complete AE sequence; at the target
frame, bind + mode-1 + `0x202` setup share one VM frame and the first published object state is mode 1.
Ghidra `/v2` comments were updated and saved. This slice was committed as `85fc07d`.

### A2b — native ADV retained text (`0x7a` / `0x204`) ✅ DONE (2026-07-10)

The prerequisite input check passed before text work: the unmodified port reached page-1
`wait-for-input@0x83c` in about 2.0 seconds and a manual run released 14 consecutive waits through page 15.
Clicks were independently healthy, so no scheduler/input workaround was folded into this slice.

Native `/v2` dispatch resolves `0x7a` to `op_0x7a_handler@0x41eba0` and `0x204` to
`op_0x204_handler@0x422a60`. Op `0x7a` selects layout slot 1 and writes the last 20-byte record's cursor;
SC0000 voiced pages compute `(75,47)`, while narration resets to `(100,47)`. Layout origin is `(0,430)`,
bounds are `(720,147)`, retained glyph handles begin at `0xd6d8`, and glyph source surface is 21.
Op `0x204` locks a numbered D3D surface and GDI-rasterizes its CP932 string with retained font/color/effect
state. SC0000 `0x9b2` draws `"魔王"` to 400x30 surface 13 at `(1,1)`; `0x1fb@0x9bb` binds that surface to
object `0xe678` at `(74,444)`. Show-text builds all glyph records, then publishes one every 50 ms; a click
completes and consumes an active reveal before the VM can reach the following stable wait.

The port now handles both opcodes and string-pointer types 8/14. Blank-surface text is retained by slot and
drawn through the bound object's transform (the SC0000 speaker-name path), while ADV text retains cursor,
origin, 50 ms/glyph progress, and forced completion. The prior top-left dev Label/status presentation is
gone. Page-1 window pixels show the black 800x227 textbox with text at native `(100,477)`; voiced page 7
has separate name/dialogue bands at y=447–468, 478–500, and 507–530. Final manual validation progressed
14 pages, with 11 completion clicks consumed during reveal and 14 later clicks releasing 14 waits.

Ghidra `/v2` was renamed/commented throughout the handler, layout, raster, surface-lock, and publication
chain and saved. The new low-frequency `capture_adv_text_trace.py` records matching native offsets/state;
an initial experimental version combining a D3D device scan with hot per-glyph/render hooks crashed in
`frida-agent.dll` at teardown, so those hooks were removed and the safe narrowed rerun left AGE alive.

**Validation:** engine **110/110**; corpus sweep unchanged at **284 exit / 13 STEP-LIMIT**; Godot build
clean apart from the pre-existing nullable warning and threaded `SELFTEST OK`; all seven Python suites,
opcode/ctx lint, 481-script decode, RECOVER, and `git diff --check` clean. SC0000 coverage rises from
**85/129 to 87/129 handled (67.4%)**, with 42 GAP ops / 188 GAP instructions. Movie `0x236` and SFX remain
separate slices. No implementation commit was made.

### Foundation track — native asset VFS + ALF/AAI/AGF readers (VFS-A/B/C DONE)

This is an optional high-leverage detour before movie `0x236` or SFX. It replaces the Phase-A
pre-extracted/pre-converted asset bootstrap with the native loose-override/archive-fallback model and removes
the runtime dependency on `extracted/` plus `build/textures/`. Canonical format/architecture detail and source
references live in `docs/asset-resolution-re.md` §“Runtime asset-VFS track”; this section defines
slice boundaries only.

Land it as three bounded slices, not one archive/codec rewrite:

VFS-A is the common prerequisite. VFS-B and VFS-C may be swapped afterward: choose A→C→B for the fastest
SC0000 textbox/chrome payoff, or A→B→C when complete base+append archive mounting is the priority.

1. **VFS-A — base SYS4 catalog + ALF byte reads.** Runtime-parse SYS4INI into raw-id, scene-local, and name
   lookup views; preserve placeholders; expose bounded asset streams. Apply native precedence
   `loose root/<record.name> → indexed ALF range`. Convert `Sys4Loader`/`Sys4ScriptProvider` to consume bytes
   from this seam first, proving the existing 52 patched root BINs still shadow archive copies. Gate: sampled
   and corpus-size/range validation against `extracted/`, plus synthetic loose-override tests.
2. **VFS-B — APPEND01 AAI mount.** Parse `S4AC422` and read `APPEND01.ALF`. Use Ghidra/native observation
   only as needed to settle pack selection/mount precedence; compare the entire parsed directory and sampled
   payloads with `BinExtractALF.exe`. Gate: no guessed name override or high-byte-id behavior reaches runtime.
3. **VFS-C — in-process AGF → RGBA8.** Port the MIT GARbro decode algorithm into platform-neutral engine
   code with a focused LZSS primitive, palette/truecolor expansion, stride/orientation handling, and optional
   ACIF alpha. Replace BMP-path texture loading with decoded pixel surfaces. Gate: codec sample matrix plus
   exact SO001 dimensions/alpha, then windowed SC0000 textbox/button pixels with `extracted/` and
   `build/textures/` unavailable to the runtime.

After those gates, move OGG/WAV consumers onto the same byte store as a small follow-up. That prepares SFX
without conflating its channel/timing semantics with archive access. Movie `0x236` may likewise consume raw
AGF/MPEG bytes later, but video decoding remains explicitly outside this track.

Expected authored seams (names provisional): `Sys4AssetCatalog`, `IAssetStore`/`Sys4AssetStore`, bounded
archive stream/reader, `LzssDecoder`, and `AgfDecoder`; focused tests belong in `Age.Engine.Tests`. Runtime
caches should key decoded assets by catalog identity plus loose-file timestamp, while tests use synthetic
fixtures and installed-game integration checks rather than committing proprietary assets.

**Decision point:** this track is worthwhile before broadening beyond SC0000 because it establishes the
modding contract and benefits scripts, UI chrome, SFX, and movies. It is not required to continue opcode
coverage immediately, so choosing movie/SFX next remains valid.

### VFS-A — base SYS4 catalog + ALF byte reads DONE (2026-07-11)

`Sys4AssetCatalog` now runtime-parses `SYS4INI.BIN` into all 13208 raw slots (including two `@`
placeholders), 13206 real/name records, bounded scene sections, and universal raw-id lookup.
`Sys4AssetStore` applies native `loose exact-basename -> ALF offset/size` precedence with traversal rejection,
per-open file handles, and a seek/read boundary. `Sys4ScriptProvider` and all CLI/Godot root-script paths use
store bytes; `ResourceMap` consumes the live catalog instead of generated asset JSON.

Validation: 116 engine tests; every field against `build/asset-index.json`; all 136 generated scene views;
all 13206 archive ranges; representative bytes across DATA1..5 against `extracted/`; synthetic precedence,
range, traversal, and concurrency tests; CLI SC0000 run; Godot build/selftest. The installed root has 49
archive-backed script overrides and two root-only BIN engine files, not the historically reported 52
shadowing scripts; all 49 were byte-proven to win and differ from their archive payload. VFS-B APPEND01/AAI,
VFS-C AGF, audio migration, and movie `0x236` remain separate.

### VFS-C — in-process AGF to RGBA8 + SC0000 system chrome DONE (2026-07-11)

`AgfDecoder` now consumes `IAssetStore` bytes and emits a platform-neutral, tightly packed top-down RGBA8
surface. Its shared 4 KiB-ring `LzssDecoder` covers raw/compressed information, pixel, and optional ACIF
alpha sections; expansion covers 4/8-bit palettes, 24/32-bit truecolor, DIB row padding, and bottom-up
orientation. Synthetic fixtures exercise the whole format matrix. Installed `AE000A`, `AE001A`,
`BG030A`, `EV052CA`, and `SO001` match the existing converter's pixels exactly; SO001 is 800×300
with non-binary per-pixel alpha.

`ResourceMap` now resolves texture ids through the scene manifest with a universal raw-id fallback,
then decodes through the loose-first VFS. Godot and the CLI no longer use `BmpHeader`, `TexturePath`, or
pre-converted BMPs. SYSTEM4's inherited SO001 surface is seeded into slot `0x11` before SC0000, allowing
the existing callback-window crops to render the textbox and control icons. Windowed page-1 validation
passed with `build/textures/` temporarily unavailable; source alpha produced the translucent upper edge
and the bottom-right control row was visible.

Validation: 124/124 engine tests, CLI/Godot builds, Godot threaded selftest, pixel parity for five installed
assets, and the windowed no-BMP capture. VFS-B APPEND01/AAI, OGG/WAV migration, movie `0x236`, and unrelated
opcode work remain separate.

### VFS-B — APPEND01 AAI mount DONE (2026-07-11)

`Sys4AssetCatalog.Load` now discovers installed `*.AAI` files beside `SYS4INI.BIN`, parses the `S4AC`
directory at its native offsets, and mounts each catalog by the selector stored at header offset `0x108`.
For Himegari, `APPEND01.AAI` mounts selector 1, names one `APPEND01.ALF`, and contains 81 records whose
literal names all begin `$1$`. `AssetEntry` retains the pack id, `ResolvePacked` applies the evidenced
`high byte -> mounted catalog; low 24 bits -> record` rule, and `Sys4ScriptProvider` routes append call ids
through the existing `IAssetStore`. Base direct-name lookup remains base-only: append records are not guessed
name replacements. The selected catalog still probes the record's exact loose basename before its ALF range.

Native `/v2` now names/comments the AAI constructor/load/table/open/get chain and
`asset_mount_append_catalogs@0x44f120`; `asset_open_indexed_entry@0x44f390` documents the packed-id split.
The native scan stores each successful catalog directly into its selector slot, so a later enumerated AAI
with the same selector replaces the earlier pointer. Its `SAR 24` selector extraction makes sign-bit high
bytes negative table indexes, so the port rejects them rather than inventing `0x80..0xff` behavior. Only
selector 1 is installed and exercised here.

Validation: 127/127 engine tests; stable complete-directory digest; all 81 ranges fit; a disposable
`BinExtractALF.exe APPEND01.AAI` extraction matches all 81 names, sizes, and payload bytes; real
`$1$SC1260.BIN` loads through its packed id; CLI/Godot builds and threaded `SELFTEST OK`. Audio migration,
movie `0x236`, AGF work, and unrelated opcodes were untouched.

### Phase A — native SC0000 SFX family (`0xb4`/`0xb5`/`0xb6`/`0xc2`/`0xd9`) DONE (2026-07-11)

Native RE and the matching trace resolve the bounded family. `0xb4(resource,channel)` synchronously loads
and retains a scene-manifest sound; `0xb5(channel)` starts that loaded sound once; `0xb6(channel)` destroys
and clears it. The manager supports 13 native slots, while SC0000 uses a fixed `0..9` pool. Native playback
is a notification-fed DirectSound ring: `Play(...,DSBPLAY_LOOPING)` keeps the ring alive, but logical mode 0
stops at decoder EOF. Mode 1 belongs to adjacent op `0xba` and remains outside this slice. These five ops do
not set pan or SFX volume; SC0000 inherits centered pan and configured SFX volume. The captured loads both
apply DirectSound attenuation `-2377`; because native audio-preference import is outside this slice, the
bounded extracted-WAV port uses unity gain and centered pan instead of treating that user setting as opcode
semantics.

At the requested first site, `0xb4@0xc29` resolves `0x28` to `E0808.WAV` and loads channel 0;
`0xb5@0xc2e` publishes playback in the same native millisecond. `0xb4@0xc31` preloads the same WAV into
engine-owned secondary channel 4. `G[0x6242d]` is native/profile-owned rather than script-written, so the
port exposes value 4 through the new external-global seam for SC0000. The matching port timeline now records
the same `(load ch0, start ch0, preload ch4)` sequence in one frame. Startup `0x62b..0x646` and the later
`0x120d..0x1228` both release channels 0..9 in order.

`0xc2(target,duration)` is the adjacent blocking BGM fade: 100 linear steps for durations at least 1000 ms,
10 steps below that, with target zero releasing the source. `0xd9` only clears native service bit `0x1000`;
the isolated VM recognizes it with no host-visible effect. The Godot backend retains ten `AudioStreamPlayer`
channels, loads the existing extracted WAV bytes, separates load from start, releases buffers deterministically,
and parks BGM fades on the unified virtual clock. ALF/AAI/AGF VFS work and movie `0x236` remain separate.

Ghidra `/v2` now names/comments all five handlers plus the asset-open, decode, DirectSound start/refill/stop/
volume/release, and BGM fade workers; the program is saved. Evidence is
`build/native-sfx-trace.jsonl` and `godot/build/sfx-port-timeline4.jsonl` (the latter is the final rebuilt
channel-4 trace; earlier diagnostic reruns captured the missing external-state mismatch).

**Automated validation:** engine **111/111**; corpus sweep unchanged at **284 exit / 13 STEP-LIMIT**;
Godot build and threaded `SELFTEST OK`; all seven Python suites, opcode/ctx lint, 481-script decode, RECOVER,
and tracer bytecode compilation clean. SC0000 coverage rises from **87/129 to 92/129 handled (71.3%)**,
leaving 37 GAP ops / 101 GAP instructions. Headless sequence capture still emits the known dummy-renderer
`GetImage` diagnostics while completing successfully; it is not an audio failure.

**Manual validation:** the normal-speed windowed port advanced through `wait-for-input@0x1a58` at 45.6 s,
well past the first effects and dialogue pages, with no audio-related stall. The user confirmed the effects
were audible and sounded good.
