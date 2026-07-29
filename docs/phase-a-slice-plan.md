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
here. In brief: `0x215`'s real handler `FUN_0042a0b0` (Ghidra) records its generic 5-dword instruction length
and returns a **`std::map::find`** over the retained gfx-object registry populated by draw/geometry workers.
That return is **native retained-object state, not the VM global bank** → seeding
story-state **cannot** fix it. So this is **(b) a genuine native op**, *not* (a) the Phase-B state-divergence
problem. The prior conclusion in this doc — grounded in a 2/s `capture_gfx_objects.py` poll that mistook
script-context records for gfx objects — was wrong: it observed the wrong structure, not the lookup map.

**Resolution had TWO halves** (canonical decode in `docs/engine-re.md`, op `0x215` + "The render drift's
SECOND half"; don't duplicate here):
1. **Native gfx ops (b):** the retained-object command-buffer ops (`0x1f7`,`0x1fa`,`0x1ff`,`0x202`,`0x203`,
   `0x212`,`0x213`,`0x215`–`0x21a`) were reversed + implemented against a host-side `GfxState` (VM execution
   state; `engine/Age.Engine/Model/GfxState.cs`). `0x215` now returns distinct per-object slots. **Correction
   (2026-07-20):** `0x1a2` never belonged to this family; it is the shared `SAVE.DAT` integer-store half
   paired with `0x1a3`, and its inert `GfxState` approximation has been removed. See `docs/engine-re.md`.
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
carries anti-tamper + a generic 3-dword instruction-length write, neither needed host-side.) `0x20c` = `gfx_op_0x20c_present_frame`
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
  (looping row-major spritesheet cells + ping-pong color/glow), driven by the **`FrameClock`** (the "mach 5" pacing fix).
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
`draw-texture`; it does not query op `0x1a2`'s shared-profile integer table. The old host model returned -1 for CG
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
from frame time `retained-gfx owner+0xb550` (`EngineCtx+0x51b64`), performs an integer bytewise
current-`+0x60` to target-`+0x64` LERP, then
commits target, clears timing, writes target `-1`, and clears the active bit once all sibling one-shot
channels finish. This is ordinary presentation-clock coupling, not op `0x238`'s separate service clock.
`retained-gfx owner+0xb55c == 1` (`EngineCtx+0x51b70`) forces completion unless the object-local
`+0x2d0` override bit is set. `/v2` comments
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
channels, loads catalog-resolved WAV bytes, separates load from start, releases buffers deterministically,
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

### Audio-byte migration onto the asset VFS DONE (2026-07-11)

`ResourceMap.BgmPathById` and `ResourceMap.AudioPath` are removed. BGM direct-name resolution now returns a
catalog entry, while voice and SFX retain their scene-local resolution; all three flow through
`ResourceMap.ReadAudio` and the injected `IAssetStore`. `GodotAdvHost` passes owned byte arrays across the
existing deferred main-thread boundary, and `Main` decodes them with
`AudioStreamOggVorbis.LoadFromBuffer` / `AudioStreamWav.LoadFromBuffer`. The existing BGM loop and fade,
voice interruption, ten-channel SFX load/start/release, clock pacing, and gain behavior are unchanged.

Validation: engine **128/128** and both .NET/Godot builds pass. An archive-only SC0000 regression verifies
the Ogg signatures for `BGM005.OGG` and `MAN999.OGG` plus the RIFF/WAVE signature for `E0808.WAV`. With
`extracted/` physically moved aside, the CLI completed SC0000's 127-op audio trace and a windowed Godot run
crossed voice at `0xa0a`/`0xbf6`, BGM005 at `0x7fa`, and the established SFX load/start/preload sequence at
`0xc29`/`0xc2e`/`0xc31` on channels 0/0/4. Godot reported no OGG/WAV read, decode, or deferred-call errors;
`extracted/` was restored afterward. Movie `0x236`, AGF work, opcode semantics, and unrelated VM behavior
were untouched.

### Phase A — SC0000 movie opcode `0x236` DONE (2026-07-11)

Reversed and implemented only SC0000's existing `0x236@0x13c8`. Native ABI is
`(resource_id, surface_slot, movie_flags, sync_mask)`; the native site evaluates
`(0x33, 0, 2, 0)`. Native graph construction and asset open are synchronous, playback is asynchronous,
and the opcode itself does not park the interpreter: execution resumes at bytecode `0x13d1`. SC0000's
later `0x21c` presentation boundary sets run-state bit `0x400` and services the retained movie until EOF;
only the following cleanup stops and detaches playback.

Resource `0x33` resolves to the 8,194,052-byte `DATA1.ALF:CHAPTER.AGF` MPEG-1 program stream through the
catalog and `IAssetStore`. `ResourceMap.ReadMovie` owns that VFS read. The Godot Windows backend uses the
system DirectShow MPEG splitter/decoder, converts its bottom-up RGB32 samples to retained RGBA frames, and
presents them on the native slot-0 movie layer. The payload contains an audio stream, but its pin remains unrendered;
audio, AGF still-image work, generalized video APIs, other opcodes, and unrelated VM behavior remain out
of scope.

Ghidra `/v2` names/comments the `0x423ee0` handler and the movie ctor, graph-interface/open/play,
sound-route/volume, texture-renderer sample/media-type, stop/detach/release/destructor chain; the program is
saved. Validation covers the exact real-SC0000 operands and `0x13c8 -> 0x13d1` boundary, archive-only VFS
payload identity, actual 800x600 RGBA DirectShow decode, .NET/Godot builds, and the real Godot site opening
the same VFS byte count. Manual testing exposed and corrected an initial same-frame teardown: pre-yield
static loads no longer destroy the movie, `0x21c` waits for DirectShow completion, and a real-render run
records first frame 98 / stop frame 181. A follow-up compositor-only investigation found that the movie
object was present at the correct retained z-position, but the static AGF image cache keyed by
`(assetId,colorKey)` froze its first sample. A duplicate unconditional backbuffer copy of the current sample
was then covered by that same retained object. Dynamic movie surfaces now bypass the static-image cache and
publish only through their retained object. A windowed auto-play run recorded first frame 101 / stop frame
190, and the user manually confirmed that the video visibly played. The known lower white/textbox panel is
an unrelated retained-object issue. Movie audio remains deliberately unrendered and out of scope.
The decoder test
asserts two delivered MPEG frames differ. The two archive/decode tests
also pass with all of `extracted/` physically moved aside. The real-scene trace is intentionally separate:
the existing test bootstrap still finds root script fixtures through `Paths.Scripts()` under `extracted/`,
and changing that unrelated bootstrap was outside this movie slice. Final validation: engine **132/132**,
Godot build with zero warnings, threaded `SELFTEST OK`, and opcode-map lint clean.

**Pre-roll decode-warning follow-up (2026-07-21).** SC0000 could print
`AGF decode failed CHAPTER.AGF: expected an ACGF image` immediately before successful MPEG playback.
This was a port-side compositor race, not corrupt media or bad catalog resolution: op `0x236` exposed the
movie's `.AGF` resource binding while the VFS read/DirectShow graph setup was still synchronous, and the
first-frame resolver fell through to the still-image AGF decoder. Movie surfaces are now registered before
the VFS read, remain blank until their first dynamic frame, and the VM publishes the movie resource binding
only after synchronous host setup returns. The same pending-frame guard also covers modal movies. A focused
regression locks the blank-during-setup boundary; validation is **271/271** engine tests, a zero-warning Godot
build, and threaded `SELFTEST OK`. The headless screenshot harness did not terminate at its requested page;
the subsequent live SC0000 recheck passed with normal playback and no spurious AGF warning.

### Phase A — SC0000 textbox/control-strip one-shot blend correction DONE (2026-07-11)

The lower white panel noted after movie publication and the white-outline controls had one shared cause,
not a movie or AGF-alpha fault. SYSTEM4's inherited SO001 surface is cropped by ADV backing object `0xd2f0`
and control-strip object `0xd2f1`; both use a mode-0 `0x202` one-shot color transition. The port discarded
that one-shot provenance after sampling and interpreted the packed alpha as static tint strength. As a
result, full-strength white replaced the controls' yellow pixels, while zero strength exposed the raw
mostly-white backing crop instead of hiding it.

`GfxState` now distinguishes static mode-0 `0x203` state from mode-0 color state which has passed through
`0x202`. The latter retains ARGB-opacity plus multiplicative-RGB semantics after target commit. This keeps
the established `0x00ffffff` static CG initializer opaque, preserves yellow under white identity modulation,
and makes a committed alpha-zero ADV crop transparent. Focused model/raster tests cover the committed
endpoint, identity modulation, and zero-opacity output. Validation: engine **134/134**, Godot build with
zero warnings, matching windowed capture, and user manual confirmation that the box disappears fully and
the controls retain their normal color while visible.

### Godot window jitter / compositor performance investigation (2026-07-11)

The visible window jitter is main-thread compositor pressure, not `FrameClock.Speed`, coroutine pacing,
or GPU throughput. A bounded windowed run on the Vulkan backend (RTX 4080 SUPER, `--max-fps 60
--print-fps`) sustained only **23–25 FPS / 40–43 ms per frame** while SC0000 approached its first ADV
input wait. The retained frontend currently makes `ShouldRecomposite()` true throughout input waits,
explicit sleeps, foreground waits, and text reveal. Each recomposite clears the 800x600 canvas, then for
every visible layer calls `Image.GetData()`, runs the C# per-pixel inverse-affine rasterizer, calls
`Image.SetData()`, and finally uploads the full canvas with `ImageTexture.Update()`. An unchanged input-wait
screen therefore consumes the same expensive path every rendered frame; dragging the OS window stutters
because this work runs on Godot's main thread.

Continuous presentation itself is partly load-bearing: one-shot transitions, movies, and ambient cyclic
channels must continue sampling `FrameClock` while active. The *unconditional* redraw implied by
`IsWaiting`/`IsTextRevealing` is not. The existing gfx-change log showed real one-shot color changes through
render frame 121, then no retained-object changes, while the separate steady-state FPS probe remained near
23 FPS. A safe correction should preserve time-based presentation but distinguish dirty/static waits from
active visual channels, and should avoid copying the full Godot `Image` out and back once per layer. Likely
implementation boundaries are: (1) explicit compositor dirty generation plus an active-animation/movie
query, and (2) one CPU backbuffer acquisition/update per recomposite or migration of ordinary layers to
Godot/GPU-native drawing. Merely changing `--speed`, sleep/coroutine behavior, or the 60 FPS cap will not
remove the underlying frame cost.

**Quick win 1 implemented (2026-07-11).** `GfxState.HasActiveVisualPresentation(nowMs)` now reports
only retained pixels that can change without another VM mutation: finite surface/one-shot channels plus
visible spritesheet, color, and cyclic-rotation channels with positive periods. Godot consumes explicit
presentation dirtiness once and otherwise recomposites only while that query is true. Entering input wait
or `sleep` requests one publish so preceding retained writes remain visible; the wait/sleep state itself and
ADV text reveal no longer rebuild the background. Movie frames retain their existing per-sample dirty
publication. This preserves the clock, VM suspension model, active transitions, ambient animation, and
movie lifecycle.

Validation: the focused static-vs-ambient query regression brings the engine suite to **135/135**; Godot
builds with zero warnings and threaded `SELFTEST OK`. A matching hidden-window Vulkan run capped at 60 FPS,
using `--speed 8` only to reach the static state quickly, held **60 FPS / 16.66 ms per frame** for all eight
reported samples, versus the pre-change 23-25 FPS. Buffer batching/source-pixel caching and rasterizer fast
paths remain independent follow-ups.

**Quick win 2 implemented (2026-07-11).** The Godot compositor now owns one reusable 800x600 RGBA8
managed backbuffer. `Recomposite()` clears it once, every image/fill/transition layer mutates that same
array in z-order through the unchanged `SoftwareAffineRasterizer`, and only the completed frame crosses the
Godot boundary via one `Image.SetData()` plus one `ImageTexture.Update()`. The former per-layer screen
`Image.GetData()` / `Image.SetData()` round trip is gone. Static source images still call `GetData()` per
draw; source-pixel caching remains the next independent optimization.

Visual validation used the same SC0000 page-1 command before and after the refactor (`--speed 8 --shot-page
1 --shot-settle 3`). The two PNG files are byte-identical, both SHA-256
`E669355772D4D9118BE80AC93595F78BB467B088659DEA4CC2D2B7D0F05B62BE`. A 12-second normal-speed windowed
Vulkan probe now reported 53, 26, and 39 FPS through the initial heavier active intervals, then 59-60 FPS
through lighter/static intervals; the old compositor had remained around 23-25 FPS. Validation also holds
at engine **135/135**, zero-warning Godot build, and threaded `SELFTEST OK`. The general per-pixel affine
loop is now the clearest remaining active-frame cost.

The byte-identical page-1 capture is a **differential compositor oracle only**, not a claim that the
automated `--shot` path has the correct interactive presentation state. Both the pre- and post-refactor
captures show fallback `SO013A` rather than the expected first event CG. The matching gfx log proves this is
not resource-load failure: `EV049AA.AGF` resolves and loads into slot 3, but at capture its handle `0x0` is
positioned at `(-400,-600)` and the full-screen `0xcf08` copy has completed its fade to alpha zero. The user
confirmed that ordinary manual play displays the correct CGs. Keep this shot-path state discrepancy open
and do not use page 1 as an absolute scene-fidelity oracle; it predates and is pixel-identical across quick
win 2.

**Quick win 3 implemented (2026-07-11).** Static compositor sources no longer round-trip through Godot
`Image.CreateFromData()` / `Image.GetData()`. The cache now retains `(width,height,RGBA)` directly under
`(assetId,colorKey)`: unkeyed assets reuse their decoded `RgbaImage.Pixels`, while keyed variants clone and
bake transparency once so the canonical decoded asset remains reusable under other keys. Dynamic movie
samples never enter this cache; they use the decoder-owned newest-frame bytes directly, cloning only if a
color key must be applied.

The differential page-1 PNG remains byte-identical at SHA-256
`E669355772D4D9118BE80AC93595F78BB467B088659DEA4CC2D2B7D0F05B62BE` (subject to the shot-path oracle
caveat above). A bounded 220-frame sequence reached VFS movie publication at render frame 141 and captured
multiple distinct movie-frame hashes, confirming dynamic samples still change rather than freezing in the
static cache. Engine **135/135**, zero-warning Godot build, and threaded `SELFTEST OK` remain clean.

The normal-speed FPS samples were effectively unchanged from quick win 2 (54/26/38, then 58-60, versus
53/26/39, then 59-60). Source copying was therefore no longer a material bottleneck after the shared
backbuffer landed. The cleanup removes needless Godot objects/copies and helps future layer-heavy scenes,
but the measured next target is the general inverse-affine per-pixel path—especially identity-transform,
opaque-copy, and axis-aligned fill fast paths.

**Quick win 4 initial raster fast path implemented (2026-07-11).** `SoftwareAffineRasterizer` now detects
exact identity matrices with integral translation and bypasses inverse-matrix construction, floating-point
coordinate application, `Floor`, and per-pixel geometric rejection. Both `BlitRgba` and `FillRgba` use
direct clipped integer indexing for that case while retaining the exact existing tint, multiplicative tint,
source alpha, object opacity, destination blend, and alpha-accumulation arithmetic. Scale, rotation,
fractional translation, singular transforms, and all other geometry retain the original affine fallback.
Zero-opacity draws also return before scanning pixels.

Six new differential cases compare the fast paths byte-for-byte against a retained copy of the pre-fast-path
algorithm across positive/negative clipping, existing destination pixels, ordinary and multiplicative tint,
partial source/object alpha, and solid fills. The full engine suite is **141/141**; Godot builds with zero
warnings and threaded `SELFTEST OK`. The differential SC0000 page image retains SHA-256
`E669355772D4D9118BE80AC93595F78BB467B088659DEA4CC2D2B7D0F05B62BE`, subject to the automated-shot
presentation-state caveat above.

Because the console SC0000 state does not match the user's ordinary interactive CG presentation, no
whole-scene FPS claim is made for this slice. An isolated Release-mode 800x600 tinted/alpha identity-blit
kernel comparison measured **2.737 ms** for the new path versus **4.217 ms** for the retained general loop,
or **1.54x** for that layer type. Manual-path fade responsiveness is validated below. A later bounded step
may add a fully opaque/unmodulated row-copy path as optional incremental work.

**Manual validation:** the user tested the ordinary interactive path—the path whose CG state and fade
cadence differ from the console capture—and reported a **massive improvement**. This confirms the identity
blit/fill fast path materially improves real fade responsiveness, not merely the isolated kernel benchmark.

### SC0000 third-CG white screen and AE001D glow fixed (2026-07-11)

The page-14 screenshot path reproduced the user's all-white EV052DA page exactly. Synchronized compositor
and opcode evidence excluded the animated glow assets as the white owner: both AE001D layers load
successfully, retain their AGF alpha, rotate, and render in mode 1 at 6-8% opacity. Base handle `0xcb2a`
was correct through the
`0x223` EV052CA→EV052DA transition (`mode=2`, identity), then `0x203@0x12478` restores it to textured mode 0
with preserved `0xffffffff`. The old port resolved that endpoint as `tintStr=1.00`, whitening every EV052DA
pixel. Textured mode 0 now uses opaque multiplicative RGB modulation, while surfaceless mode-0 fills retain
their existing strength behavior. The transition cleanup regression covers mode 2 → mode 0 `(-1,-1)`.

The resulting non-white capture then proved the glow itself still contributed no visible pixels. Native RE
closed the causal gap: `0x1fd(handle,x%,y%,z%)` immediately writes the current scale matrix at `obj+0x6c`,
distinct from `0x21e`'s one-shot target. SC0000 scales the two AE001D circles to 210% and 240%; the stub left
them at 100%, almost entirely below the viewport. `GfxState.SetCurrentScale` and VM dispatch now reproduce
the native matrices, with focused geometry/dispatch tests. Validation: engine **145/145**, opcode build/lint,
Godot build/capture, Ghidra annotations saved, and user confirmation that the CG and animated glow are both
visible. This AE001D path is a retained scaled/rotating texture; the later `0x231` spritesheet-cell issue
was separately resolved by preserving draw-texture's 200x200 cell and interpreting `(8,4)` as total
frames/columns rather than a grid divisor.

### SC0000 ADV wait indicator implemented (2026-07-11)

The missing post-reveal bat is `SO000.AGF`, a 390x27 strip of thirteen 30x27 frames. SYSTEM4 loads raw
asset `0x337c` into surface slot 12 and configures layout 1 through op `0x73` as a marker at layout-relative
`(385,140)`, screen `(385,570)`, with terminal frame 12 and a 48 ms period. Native op `0x72` activates that
layout descriptor after reveal completion. The Phase-A bootstrap now injects SO000 into surface 12 and
forwards the exact op-`0x73` configuration through `IHost`; `WaitForInput` carries the selected layout and
starts the marker clock. Godot presents it as a separate 30x27 atlas overlay, advancing inclusive frames
0..12 every 48 ms only during the stable wait and hiding on release. This avoids recompositing the 800x600
software backbuffer during otherwise static waits. Validation: engine **147/147**, zero-warning Godot build,
threaded `SELFTEST OK`, and a real page-1 capture showing the transparent bat at `(385,570)`.

### A2b -- SC0000 post-movie AE001H spritesheet correction ✅ DONE (2026-07-11)

The reported missing cave spirit was the `0x231` path, independent of the earlier AE001D retained glow.
Native interpolation proved `(period, 8, 4)` means milliseconds per frame, total frames, and columns;
the existing 200x200 draw-texture rectangle is one cell and must not be divided again. The retained model
now cycles the eight AE001H cells row-major and wraps. Focused tests cover the exact 800x400/4x2 layout.
Validation: engine **146/146**, opcode sources regenerated, Godot zero-warning build and threaded selftest,
real-scene compositor trace across all eight cells, Ghidra `/v2` annotations saved, and user confirmation
on the normal client path. The automated-shot wrong-CG state remains the pre-existing separate gotcha.

### A2b -- SC0000 post-movie AE001H travel-path correction ✅ DONE (2026-07-11)

The remaining off-screen motion is a separate `0x228` query-model bug, not part of the spritesheet crop.
Native `0x228` decomposes and returns the retained object's target translation (`obj+0x1ac..+0x1b4`), while
the C# VM currently returns its static draw/base position `V24`. For AE001H this changes the first `0x220`
target from `(40,-20)` to `(400,0)`; because the compositor also retains the `(360,20)` draw base, the
sprite was displaced to approximately x=760 before cyclic rotation. `GfxState.TryQueryTranslationTarget`
now exposes the retained target independently of `V24`; VM dispatch matches the native 0/1 status convention
and preserves output operands on a miss. Bytecode-level tests cover the exact three targets `(40,-20)`,
`(50,-80)`, `(130,-100)` plus missing-object behavior. Validation: engine **149/149**, zero-warning Godot
build, and threaded `SELFTEST OK`. Current static SC0000 coverage is **94/129 distinct ops handled
(72.9%)**: 82 implemented + 12 safe no-ops, leaving 35 GAP ops / 88 GAP instructions. By instruction
frequency, 16,169/16,257 are handled (**99.5%**); this gauge does not replace manual end-to-end fidelity.

### A2b -- SC0000 AE001H white-pulse correction ✅ DONE (2026-07-11)

Manual movement confirmation exposed a separate color artifact. The sheet itself has no white frames.
SC0000 later calls `0x232(handle,1200,224,-1)`; native treats RGB `-1` as "preserve current static RGB,"
whereas the port masks it to white and drives mode-0 tint strength from the animated alpha. The compositor
log correspondingly cycles `tintStr` from 0 to about 0.82 and back. This identifies the white wash as a port
bug, not intended artwork. Native dataflow confirms fresh static color is `0xffffffff`; `0x232` samples
`0xffffffff ↔ 0xe0ffffff` and passes it through unchanged blend mode 0, whose alpha is inert and whose
white RGB is identity modulation. The port now resolves native default/static color and negative sentinels,
samples packed ARGB, and applies it through normal mode-specific blending rather than a separate tint-strength
convention. Focused tests cover exact AE001H invariance, mode-0 RGB modulation, and mode-1 alpha opacity.
Validation: engine **152/152**, zero-warning Godot build, and threaded `SELFTEST OK`; manual confirmation is
the remaining visual gate.

### ADV page-to-script locator implemented (2026-07-11)

Godot now turns each stable `wait-for-input` into a shared human/tool coordinate. A normal run recreates
`build/page-map-<SCENE>.jsonl` and records the run-relative page number, page-start location, canonical wait
script/offset, last show-text instruction and inline-string offsets, dialogue text, and nested call stack.
The optional HUD uses the compact form `SC0000 P014 · wait SC0000@0x… · text SC0000@0x…`; F2 toggles it
and F3 copies it. `tools/locate_page.py SC0000 14` resolves that record and prints authoritative disassembly
around the wait. Page number is deliberately only the friendly coordinate because state and branches can
shift ordinals; the script/offset remains authoritative. A live SC0000 run verified page 1 as
`show-text@0x834`, string `0x14963`, and `wait-for-input@0x83c`. Validation: focused Python tests,
engine **152/152**, zero-warning Godot build, threaded `SELFTEST OK`, and the live lookup all pass.

### SC0000 page-58 EV050EA background restored (2026-07-11)

The first user-supplied locator, `SC0000 P058 / wait@0x6545 / text@0x653d`, led directly to the missing
full-screen CG. Resource `0x7a` resolves to `EV050EA.AGF` and was loaded into slot 5, but retained handle
`0xcb2a` inherited an older `(-100,0)` translation, `-90 degree` rotation, and alpha-zero endpoint. Native
RE showed why cleanup diverged: `gfx_object_init_default` zeroes source slot `obj+4`, so op `0x215` returns
0 for a created-but-unbound object; only a missing handle returns -1. The port's `SourceSlot=-1` default made
the sign-tested cleanup skip that object. The model now uses native slot 0, allowing erase/recreate to restore
identity state. A lifecycle regression reproduces the transform-create, query-cleanup, and CG-reuse sequence.
Validation: engine **153/153**, zero-warning Godot build, and a synchronized real-scene compositor trace at
page 58 showing `EV050EA.AGF` at `(0,0)`, scale 1, translation 0, rotation 0, and opacity 1.

### SC0000 page-89 BG004D background restored (2026-07-11)

Locator `SC0000 P089 / wait@0x89e9 / text@0x89e1` identified `BG004D.AGF` as loaded but offscreen at
`(400,650)`. The prior BG001A animation intentionally leaves translation y=550; the erroneous extra
`(400,600)` came from `GodotAdvHost` retaining slot 0's seeded 800x600 dimensions after op `0x1fa` released
the surface. Native release frees and nulls the slot. Static releases now remove the host resource and size
state (while the existing in-flight movie retention remains unchanged), and the CLI graphics trace mirrors
that lifecycle. The loader consequently builds base `(0,-500)`, so the retained translation places BG004D
at `(0,50)`. Op `0x1ff` was also completed as the native immediate current-translation setter at obj+0x16c.
Validation: engine **155/155**, zero-warning Godot and CLI builds, and a full synchronized run reaching the
exact page-89 wait with `BG004D.AGF` drawn at `(0,50)` alongside the character sprite.

### ADV control-strip button investigation complete (2026-07-18)

Native RE and the shared SC0000 routine now establish the implementation contract for the five visible
lower-right controls: op `0x90` registers enter/leave/activation callbacks, SO001 supplies the base icons,
tooltips, generic alpha-0x80 hover overlay, and persistent state overlays, and the activation callbacks map
left-to-right to History, Auto message, all-message Skip, read-message Skip, and Hide window. Manual
correction confirms the controls are silent on hover, matching the absence of any audio call in the path.
Relevant input/state opcodes and `G[0x6c9..0x6cd]` are named in their canonical registries; the `/v2`
Ghidra image is annotated and saved. Full native evidence is in
[`engine-re.md`](engine-re.md#adv-control-strip-buttons-and-native-hotspot-callbacks-2026-07-18).

### ADV control-strip pointer callbacks + Auto bridge implemented (2026-07-18)

The first bounded runtime slice is now live. `ExecFrame` owns the native-style hotspot registry populated by
ops `0x90`, `0x94`, and `0x97`; Godot routes 800x600 virtual pointer motion into it and consumes an armed
hotspot click before the ordinary ADV page-advance path. Enter, leave, and activation targets execute as
temporary local callbacks on the VM thread while the enclosing `wait-for-input` remains parked. A dedicated
host wake channel services those callbacks without releasing the page semaphore. Inclusive bounds, direct
enter/leave dispatch, activation/re-arm, nested local calls, click consumption, and registry lifetime have
focused regressions. Callback completion requests one retained-frame publication, so static waits show the
new tooltip/glow immediately without continuously recompositing the 800x600 canvas.

This does not introduce duplicate Godot widgets or hover assets: the callbacks already present in every ADV
script redraw the retained SO001 tooltip/glow/state layers. Ops `0x1b6`/`0x1b7` now retain Auto-message state,
so the x=706 action toggles and redraws its active overlay. Auto's timed page advance is not part of this
slice; nor are keyboard/pad activation and the remaining four action services. Validation: engine 157/157,
zero-warning Godot build, threaded `SELFTEST OK`, and SC0000 static coverage 100/129 handled (77.5%;
88 implemented + 12 safe no-ops, 29 GAP ops / 70 instructions).

**Next:** manually validate hover and repeated Auto clicks in a normal SC0000 run, then add Auto timing and
the History/all-skip/read-skip/Hide service behavior one bounded action at a time. No hover-audio work is
required.

**Manual-validation correction (2026-07-18).** The first build displayed the SO001 strip but did not hover.
Its cold profile state took `jcc@0x8d` with `G[0x6c1]==0`, skipping the five visible registrations at
`0x94..0xd0`; later op `0x93@0x622` also canceled the one early registration pass before the first dialogue
wait. Native SYSTEM4 supplies `G[0x6c1]=1`, and its ADV coroutine republishes the shared definitions at the
stable wait. The single-scene bootstrap now supplies that exact inherited flag, retains canceled definitions
as inactive coroutine templates, and re-arms them at `wait-for-input`. Called-script frame exit also restores
the parent's armed registry. A real-SC0000 regression reaches the first wait, moves to History `(684,572)`,
executes `label_1a0`, observes `G[0x6c9]=1`, and verifies callback-frame publication. Validation is now engine
159/159, zero-warning Godot build, and threaded `SELFTEST OK`; manual recheck remains the visual gate.

### ADV Auto timing implemented (2026-07-18)

The follow-up manual check confirms hover and the script-owned toggles work; Auto previously changed only its
SO001 active overlay because timed release had not yet been implemented. Native RE closes that gap without a
scene-specific shortcut. Auto now uses the engine's two configuration channels: unvoiced pages wait
`AutoMessageTime1` (CONFIG default 2000 ms), while voiced pages wait for real voice completion and then apply
`AutoMessageTime0` (default 500 ms). Ops `0x1b8`/`0x1b9` read/write those delays, op `0xc4` marks a queued
voice, and op `0x1bc` resets the per-message voice state. A configured zero retains the native 100-ms
fallback.

The implementation keeps VM-owned settings separate from host-owned clock/audio state. The Godot wait loop
polls the existing monotonic frame clock and an interrupt-safe voice generation counter; it does not enqueue
a fake click or sleep the VM thread for a hard-coded duration. Focused regressions cover unvoiced and voiced
deadlines, live Auto disable/re-enable, zero fallback, the time-setting ABI, and voice-state reset. Validation:
engine 165/165, opcode and EngineCtx generators/tests/lints clean, Godot build has no compiler/analyzer
warnings, and threaded `SELFTEST OK`.

**Next:** manually validate Auto advancing both an unvoiced page and a voiced page, then take the remaining
History/all-message-skip/read-message-skip/Hide actions as separate bounded services.

### ADV all-message Skip implemented (2026-07-18)

The x=728 action now enables the native persistent Skip service through op `0x88`. The VM distinguishes
persistent button/display state (op `0x19a`) from the live skip query (op `0x1c7`, persistent state OR the
existing host fast-forward channel), and op `0x101` resets only transient input state. Godot completes text
reveal and stable waits while Skip is active; the existing skip-aware transition branches publish their final
endpoints. Voice calls follow the native latest-only queue: requests replace one deferred voice while Skip is
active, and the most recent request starts when op `0x88(0)` clears the service.

Native `set:CancelMesSkipOnClick` defaults to zero, so this slice deliberately does not turn arbitrary clicks
into a port-only cancel gesture. A future settings-profile implementation can expose the native nonzero
press/release policy without changing the Skip state seam. Regressions exercise the opcode lifecycle and the
real SC0000 x=728 callback. Validation: engine 167/167, zero-warning Godot build, opcode and EngineCtx
generators/tests/lints clean, and threaded `SELFTEST OK`.

**Next:** manually validate that x=728 fast-forwards text and transitions and that a later `0x88(0)` boundary
returns to normal pacing. Then implement Read-message Skip on the same service seam, adding the required
per-script-offset read-history model rather than treating it as another global all-skip toggle.

**Manual-validation correction (2026-07-18).** The initial Skip build collapsed each formerly blocking
message span into a free-running VM burst, so playback teleported to explicit sleeps and then teleported
again. Native still executes one opcode per interpreter tick during Skip. Godot now waits for one rendered
frame pulse at each `FrameYield` only while persistent message Skip is enabled, retaining normal free-running
bursts outside Skip. Validation: engine 168/168, zero-warning Godot build, threaded `SELFTEST OK`; repeat the
x=728 pacing check before starting Read-message Skip.

### ADV Read-message Skip persistence investigation (2026-07-18)

Native RE confirms that this service cannot be implemented as a second all-message toggle. AGE resolves the
current script code offset through its per-frame message-offset table, then tests a dword flag in an
engine-owned `ReadTextDB` keyed by the raw packed script resource id and per-script message index. Click,
Auto, and active-Skip completion queue `{script_resource_id,index,count}`; opcode `0x71`, whose layout-reset
sites are also the structural T1 targets,
commits those pending records and therefore is not a pure runtime no-op.

The database is profile-wide rather than slot-local. AGE loads and writes `RT.DAT` beside shared
`SAVE.DAT`, using `$$RT.DAT` and `RT.BAK` for replacement/backup; numbered game slots remain
`SAVE%2.2d.DAT`. Successful slot/context saves and normal shutdown persist the shared database. The full
native chain and file layout are recorded in
[`engine-re.md`](engine-re.md#adv-read-message-skip-and-shared-rtdat-history-2026-07-18), and `/v2` is
renamed/commented/saved.

**Next:** implement a profile-level read-history service behind the VM/host boundary, make click/Auto/Skip
completion queue the native tuple, make op `0x71` commit it, and use the same resolver for op `0x1cc`.
Persist it in a port-owned shared format first; raw `RT.DAT` import/export can remain a separate compatibility
slice.

### ADV remaining-button difficulty inventory (2026-07-18)

The remaining action buttons beyond Read-message Skip are History (`x=684`) and Hide Window (`x=772`).
They are not additional toggle services. `HIDEWIN.BIN` is 29/37 distinct opcodes covered (251/286
instructions) and mainly needs the real op-`0x199` coroutine transition plus AGE's per-frame mouse/joy
callback tables, button-state polling, and cursor-resource operations. Existing retained-object translation
and presentation support covers its scene-view/pan behavior, making this a medium-sized infrastructure slice.

`HISTORY.BIN` is 41/78 distinct opcodes covered (761/854 instructions) and needs a separate session-level
retained text-record model, navigation/render/metadata ops `0x1d0/0x1d1/0x1d3/0x1d4`, stored-voice replay,
the same generic input-callback layer, literal local-array initialization, and additional menu/text-surface
operations. The shared ReadTextDB is not a backlog: it stores only per-message read flags. History is
therefore the largest remaining control-strip action.

**Recommended next slice:** implement Hide Window first as the smallest clean remaining action and use it to
land the generic coroutine/input-callback service that History will later reuse. Read-message Skip remains
the next bounded persistence slice; History should follow after both foundations exist. Full native evidence
and the difficulty table live in
[`engine-re.md`](engine-re.md#remaining-adv-control-strip-actions-and-implementation-cost-2026-07-18).

### ADV Hide Window implemented (2026-07-18)

The real x=772 callback now yields through op `0x199`, runs the shipped `HIDEWIN.BIN`, re-enters the second
ADV coroutine handler, and resumes after the original yield via op `0x7c`. The VM owns frame-local timed
mouse and 32-entry input callback registrations; Godot supplies virtual coordinates, distinct left/right
button bits, directional/action indices, and release callbacks. Cursor ids `0x3318..0x331f` resolve from the
original asset catalog, decode from Windows CUR XOR/AND masks with native hotspots, and install as Godot
custom cursors.

`G[0x62425]` is now named `adv_hide_window_enabled`. It is not a temporary test seed or save-slot value: all
ordinary ADV scripts read it, none writes it, and the full VM-write capture misses it because AGE's native
scheduler owns the inherited state. The bounded Godot scene bootstrap mirrors the original enabled value 1.
Focused tests cover real SC0000 activation and HIDEWIN return, the two-handler coroutine sequence, timed
mouse callbacks, held input callbacks, cursor opcode forwarding, and decoding a real CUR asset.
Validation: engine 175/175, opcode/global generator tests and lints clean, zero-warning Godot build, and
threaded `SELFTEST OK`.

**Next:** manually validate x=772 hide/restore and edge cursors in a normal SC0000 run. The next development
slice should be Read-message Skip's profile-wide ReadTextDB model; History can then reuse both that
message-completion seam and the generic callback/input layer landed here.

**Manual-validation correction (2026-07-18).** The textbox faded through the shipped retained-graphics path,
but the port's separate ADV text Label and wait-indicator TextureRect stayed visible. Op `0x199` now suspends
those two page overlays for the lifetime of the yielded ADV coroutine, and op `0x7c` restores them with the
saved page. The failed restore interaction was a shared opcode error: `bit-set`/`bit-reset` take a bit index,
not a literal mask, so HIDEWIN's set-index-1/test-`0x2` raw right-button release never matched. Both the C# VM
and the Python oracle now use indexed-bit semantics. Follow-up original-game validation corrected an
over-narrow input conclusion: AGE separately maps physical left/right mouse buttons into logical callback
indices 4/5, and HIDEWIN registers those actions to restore as well. Godot now queues the primary action for
a left click while the ADV page is suspended and keeps that click from advancing the enclosing dialogue
wait. The real-script regression restores through the left-action callback without hitting the step limit
and records the overlay suspend/restore sequence.
Validation: engine 177/177, opcode tests/lints and vm0 RECOVER clean, zero-warning Godot build, and threaded
`SELFTEST OK`; `/v2` bit handlers are annotated and saved.

**Next:** manually recheck x=772 with the original left-click restore gesture, then continue with the
profile-wide ReadTextDB slice if the visual lifecycle now matches.

### ADV Read-message Skip deferred pending persistence architecture (2026-07-18)

The native behavior and data model established above remain the implementation contract: read state is
profile-wide, keyed by packed script resource id and per-script message index, with message completion
queued and committed through the native `0x71` boundary. Implementation is deliberately deferred, however,
until the port has a fuller picture of numbered saves, shared `SAVE.DAT` state, `RT.DAT`, configuration, and
other engine-owned profile/global data.

In particular, the earlier suggestion to persist ReadTextDB in a port-owned format first is not an adopted
storage decision. The port should not choose native `RT.DAT`, a port-owned replacement, or an import/export
split in isolation from the broader save/profile backend, lifecycle, migration, and interoperability design.
When that work resumes, the already-reversed semantic model can sit behind whichever backend that wider
architecture selects.

With Auto, all-message Skip, and Hide Window implemented and Read-message Skip consciously deferred,
History is the sole remaining unimplemented ADV control-strip action to investigate as a current slice.

### ADV History retained-record investigation (2026-07-18)

Native RE now bounds History as an in-memory engine service rather than an unknown menu subsystem. The text
manager retains 0x48-byte text/metadata/voice records plus 8-byte `{layout_slot,first_record_index}` logical
entries. Ops `0x70/0x71` establish group boundaries, `0x6e` writes styled text, `0xc4` writes voice pairs,
and op `0x1d2` writes typed metadata. That last opcode was incorrectly classified as a safe marker despite
17,323 corpus uses and is now corrected. Op `0x1bb` suppresses recursive recording while HISTORY runs; op
`0x85` clears both vectors at ordinary ADV block boundaries.

The other large-looking gaps are generic: `0x64` copies encoded inline literal arrays; `0xa1/0xa2/0xa3`
implement switch/case dispatch; `0x12e` performs rectangle-array hover tests. The existing Hide Window input
callbacks already supply HISTORY's actions. Font/color/layout/surface/presentation opcodes are individually
bounded, while `0xd3/0xd4/0xd5` remains the smooth scrollbar callback/interpolation detail.

Native context saves can serialize/deserialize the live backlog, but that path is distinct from `RT.DAT`.
The first implementation should deliberately stay in memory: add an engine/session-owned history service,
wire the native writers and `0x1d0..0x1d4` readers, then let the host render requested groups. Save/load
restoration remains a named integration seam for the later unified storage architecture rather than a new
backend decision in this slice. Full layout, lifecycle, and opcode evidence lives in
[`engine-re.md`](engine-re.md#retained-history-record-model-and-lifetime-2026-07-18).

**Recommended next implementation slice:** land the history record/index model and its ordinary ADV write
path first, with focused synthetic tests for grouping, metadata, voice pairs, suppression, and clear. Then
add generic literal-array/switch support and the HISTORY read/render path. This orders the work around the
data that must exist before clicking History can display anything.

### ADV History retained writer model implemented (2026-07-18)

The recommended first slice is complete. `AdvTextHistory` is an engine model rather than a Godot widget:
standalone VMs own it for their complete nested-script lifetime, and `GameSession` supplies one shared
instance to successive scene VMs. It stores logical `{layout_slot,first_record_index}` entries and semantic
text, typed-metadata, and voice-pair records with native flags, layout/cursor snapshots, and the active
font/color/effect style.

The ordinary native write path is live. Ops `0x70/0x71` configure/reset layouts and establish group
boundaries; `0x6e`, `0xc4`, and `0x1d2` append text, voice, and metadata records; `0x1bb` suppresses all of
those writers while HISTORY renders itself; and `0x85` clears the retained vectors at ordinary ADV block
boundaries. The already-reversed font, color, mode, effect, bold, and cursor setters update the snapshots
future history rendering will consume.

Five focused regressions cover grouping and first-record flags, metadata and voice fields, style/layout
snapshots, suppression/re-enable, clear lifetime, session sharing, and the real SC0000 first page reaching
its wait with string `0x14963` retained. Validation: engine 182/182, zero-warning Godot build, threaded
`SELFTEST OK`. Static HISTORY coverage is now 56/78 distinct opcodes and 798/854 instructions handled or
safe-noop.

**Next:** implement generic inline integer arrays (`0x64`) and value-switch dispatch (`0xa1/0xa2/0xa3`),
then wire History navigation/query ops `0x1d0`, `0x1d3`, and `0x1d4` against this model. Rendering op `0x1d1`
and its presentation operations can follow once those data-side script paths execute correctly.

### ADV History data-side navigation implemented (2026-07-18)

The second bounded slice is complete. `Script.BodyDwords` retains the original SYS4 body for runtime data
operands, and op `0x64` copies its plain count-prefixed footer values into consecutive global or local VM
cells. The native rotate/XOR sequence reverses AGE's loader-created in-memory representation; applying it
again to the file would corrupt HISTORY's visibly plain rectangle and coordinate arrays. Generic
`0xa1/0xa2/0xa3` value-switch sequences now use formatted keys and branch to their registered or default PC.

The retained model now exposes native navigation and query behavior. Layout boundaries update a latest-entry
anchor; op `0x1d0` resolves cumulative deltas from that anchor without mutating it, skipping duplicate first-
record offsets and navigation-filtered records. Ops `0x1d3/0x1d4` scan from a supplied record through one
group and return typed metadata or the voice pair with native defaults. Ghidra confirms both opcodes' third
operands reach unused helper parameters despite HISTORY passing 1.

Eight new focused cases cover signed inline values, real HISTORY footer retention, matched/default value
switches, cumulative group navigation, duplicate/filter skipping, metadata/voice queries, and missing-value
defaults. Validation: engine 190/190, opcode generator tests/lint clean, zero-warning Godot build, threaded
`SELFTEST OK`. HISTORY is now 63/78 distinct opcodes and 833/854 instructions handled or safe-noop; its 15
remaining effectful gaps total 21 instructions.

**Next:** implement History's display slice around `0x1d1`, layout origin `0x198`, message-window alpha
`0x131`, surface fill `0x20b`, and retained-object presentation `0x222`. After visible backlog rows render,
finish rectangle hover/interaction, voice replay, and the `0xd3/0xd4/0xd5` smooth-scroll scheduler details.

### ADV History visible display implemented (2026-07-18)

The third bounded slice is complete. Op `0x1d1` now selects one retained group and produces an engine-owned
render batch for the requested target text layout. `0x198` updates that layout's origin, `0x71` clears stale
host presentation, and the existing cursor/style setters provide the target baseline and retained font,
color, outline, and effect state. The live `HISTORY.BIN` call passes flags/colors as zero, so the port renders
its ordinary bound-text path without inventing behavior for unused native special modes.

Godot maintains per-layout History labels while the engine remains the canonical backlog owner. The surface-
text path was generalized from one string per surface to clipped lists, allowing all five speaker-name rows
drawn into surface `0xc1` to coexist. Op `0x20b` clears each reused 600x30 name region, op `0x131` reads a
host configuration seam with a backend-neutral default, and op `0x222` explicitly publishes the retained
rebuild. No persistence, RT.DAT, or port-owned profile format was introduced.

Four focused regressions cover target origin/cursor/style, alpha/fill/present operand shaping, layout reset,
and an end-to-end real-script path: real SC0000 page-one records are shared into unmodified `HISTORY.BIN`,
which produces a non-empty visible row and reaches `(0,60000)` presentation without a step-cap halt.
Validation: engine 194/194, zero-warning Godot build, threaded `SELFTEST OK`. HISTORY is now 68/78 distinct
opcodes and 840/854 instructions handled or safe-noop; its ten remaining effectful gaps total 14 instructions.

**Next:** implement the basic History interaction slice around rectangle hit/hover (`0x12e`) and clean exit
using the existing callback layer. Then add stored voice replay (`0x1bd`); leave `0xd3/0xd4/0xd5` smooth
scroll interpolation as a separate fidelity slice.

### ADV History basic interaction implemented (2026-07-19)

The fourth bounded slice is complete. Op `0x12e` now performs its native forward scan over addressable VM
arrays, including HISTORY's local rectangle and offset tables. It begins after the incoming index, applies
per-candidate offsets, and inclusively intersects `[left,right,top,bottom]` candidates with the supplied
reference rectangle. The shipped arrays resolve to scrollbar/control regions, bottom-right close candidate
8, and five 650x130 dialogue rows at candidates 9..13; the script's existing redraw owns hover visuals.

Raw input ownership is now frame-scoped. Registering a timed mouse callback through `0xcc` marks that script
frame as the input owner until return. Godot continues updating raw mouse bits and input callback indices,
but does not release the enclosing ADV page semaphore while that modal loop is active. This closes a real
scheduler hole without a HISTORY filename, scene offset, or synthetic button path.

Two regressions cover inclusive edges/forward scan and the complete live route. The latter reaches SC0000's
first wait, activates the real x=684 History hotspot, lets unmodified `HISTORY.BIN` render retained text,
presses/releases its real close region, and proves the script returns with recording restored while the same
single ADV wait remains parked. Validation: engine 196/196, zero-warning Godot build, threaded `SELFTEST OK`.
HISTORY is now 69/78 distinct opcodes and 841/854 instructions handled or safe-noop; its nine remaining
effectful gaps total 13 instructions.

**Next:** implement stored History voice replay (`0x1bd`) against the existing voice host path. Then take
`0xd3/0xd4/0xd5` smooth-scroll interpolation as a separate scheduler/fidelity slice.

### ADV History manual layout corrections (2026-07-19)

Manual original/port comparison found that History's fixed rail was correct, but the lookup-driven buttons
were clustered at the top-left, the hovered-row artwork disagreed with its hit region, and rendered text
lost its x origin. The common VM cause was op `0x61`: `HISTORY.BIN` uses local-int operands as the bases of
its copied coordinate arrays, while the port treated the values in those cells as global addresses. Local
pointers now retain their local/global address domain, with matching dereference and write-through behavior
in both the C# VM and Python oracle. A real-script regression fixes the seven button coordinates at
x=768/y=121..549 and confirms every visible row retains layout origin x=65.

Godot's dynamically created History/surface labels also no longer request full-rect anchors before they have
a parent. They use top-left absolute placement, matching the batch's origin+cursor coordinates and removing
the associated parent/layout diagnostic.

The next manual run showed that the sole bottom line was actually the enclosing ADV dialogue overlay, while
the genuine retained batches were clipped to one pixel. The backlog itself is healthy: a new real SC0000
six-message regression produces multiple non-empty History rows. The missing state was the exact
`SYSTEM4.BIN` prefix that defines nine shared text layouts before dispatching any scene; HISTORY only resets
and repositions slots 2..6. The single-scene Godot bootstrap now carries forward all nine definitions, with
History rows sized 650x150 at x=65 and y increments of 150, until full SYSTEM4 replay replaces this existing
class of inherited-state bridges.

Godot also suppresses the parked page's ordinary dialogue Label and wait indicator while any nested timed
raw-input frame owns the modal screen, then restores them on return. Root Control anchor presets are now
applied after parenting, removing the remaining occurrence of the associated runtime layout diagnostic.

A follow-up exit check found that the host retained the last History render batches after the script had
erased its objects and re-enabled recording. Op `0x1bb(1)` at `HISTORY.BIN@0x1100` is now the presentation
end boundary: it clears only the host's transient layout bindings, leaving the engine-owned backlog intact
for the next opening. The real interaction regression requires zero active History batches after return.

Validation: engine 198/198, zero-warning Godot build, threaded `SELFTEST OK`, and vm0 RECOVER clean.

**Next:** manually recheck the five History rows and overlay cleanup against the original. If they match, proceed
with stored History voice replay (`0x1bd`), followed by `0xd3/0xd4/0xd5` smooth-scroll fidelity.

### ADV History stored voice replay implemented (2026-07-19)

The manual row/cleanup recheck passed, and stored History voice replay now uses the existing voice resolver,
message-Skip replacement queue, and Auto voice-pending service. Native RE exposed a meaningful second value
that the earlier one-id host API erased: ordinary `0xc4` calls the indexed voice service and records
`{voice_id,0}`, while `0x1bd` uses and records `{voice_id,1}`. `IHost.PlayVoice` now carries this playback
variant explicitly; Godot preserves it in queued requests and diagnostics while continuing to play the same
resolved OGG until the native variant's audible interpretation is proven.

The generic opcode still appends `{voice_id,1}` when history capture is enabled, matching the native handler.
During the real History menu, the existing `0x1bb(0)` suppression boundary prevents that replay from becoming
a new backlog record. Focused tests cover the variant, Auto state, normal recording, and suppressed replay.
HISTORY is now 70/78 distinct opcodes and 842/854 instructions handled or safe-noop; its eight remaining
effectful gaps total 12 instructions.

Validation: 199 non-DirectShow engine cases pass, the isolated DirectShow movie case passes, opcode tests and
lint are clean, vm0 RECOVER passes, the Godot build has zero warnings, and threaded `SELFTEST OK`. The full
suite's DirectShow case still times out only when run among the complete suite; this pre-existing multimedia
test interaction is unrelated to the History voice path. The `/v2` Ghidra image names/comments the shared
indexed voice service and is saved.

**Next:** investigate and implement the `0xd3/0xd4/0xd5` smooth-scroll callback/interpolation family as a
separate fidelity slice, including the remaining scheduler support it depends upon.

### ADV History live voice gate correction (2026-07-19)

Manual replay remained silent, and the synchronized live trace localized the failure before the audio host:
the clicked row reached `HISTORY.BIN@0x9c7`, found retained voice `{0x24,0}`, then failed its type-2 metadata
query at `0x9e3/0x9ee` and returned without executing `0xc4` or `0x1bd`. The retained writer had reversed
op `0x1d2`'s operands. Native handler/helper dataflow and SC0000's concrete `0x1d2(2,0x11)` site prove the
ABI is `(metadata_type,value)`; the helper stores value at record `+0x14` and type at `+0x18`.

The VM now records operand 2 as the value and operand 1 as the type. The real-script regression clears the
voice request emitted by the original page, clicks the actual visible voiced History row, and requires a new
request for SC0000 `0x24`/`MAN999.OGG`; this prevents a pre-History voice from producing a false positive.
The `/v2` handler and helper comments are corrected and saved. Manual replay of that same retained row now
plays audibly, confirming the full click-to-Godot path.

### ADV History smooth-scroll scheduler implemented (2026-07-19)

Native RE resolves `0xd3/0xd4/0xd5` as a generic frame-local timed local-callback sequence. Op `0xd3`
initializes it, op `0xd4` appends repeated relative deadlines with separate primary and catch-up targets,
and op `0xd5` starts the elapsed timer and services callbacks through the penultimate entry. The final entry
is a look-ahead sentinel. The scheduler chooses the catch-up target when the next deadline is already late,
which lets HISTORY advance interpolation state without performing every intermediate redraw.

HISTORY's concrete schedule is 10/20/30/40/50/51/52 ms: five scrollbar interpolation steps, a no-op
terminal callback, and the look-ahead sentinel. The VM now stores this state on the active script frame,
uses the host's existing clock/sleep boundary, redirects into the shipped local callbacks, and resumes the
same `0xd5` after `ret`. Focused regressions cover on-time execution and forced catch-up behavior.

The canonical opcode and `EngineCtx` sources are updated and regenerated; the `/v2` handlers, vector append
helper, timer helper, and main-loop scheduler are named/commented, the expanded 56-field `EngineCtx` is
reapplied, and the program is saved. HISTORY is now 73/78 distinct opcodes and 847/854 instructions handled
or safe-noop. Its five remaining effectful gaps total seven instructions: `0x10d` and `0x8b` twice each,
plus one each of `0x1ce`, `0x1cb`, and `0x20a`.

**Next:** investigate the five remaining HISTORY support opcodes together, then select the smallest coherent
implementation slice from their actual responsibilities rather than treating them as one feature by count.

Validation: all 203 engine tests pass, opcode and EngineCtx tests/lints are clean, vm0 RECOVER passes, the
Godot build has zero warnings, and threaded `SELFTEST OK`.

### ADV History mouse-wheel navigation implemented (2026-07-19)

The five-gap investigation separated independent responsibilities instead of grouping them by proximity.
Native `age_main_window_proc@0x486320` handles `WM_MOUSEWHEEL` by sign-extending its high-word delta and
accumulating it at `EngineCtx+0x1c34`; op `0x10d` returns that signed total and clears it. HISTORY first
uses the opcode to discard stale motion, then polls it from its timed mouse callback and feeds the sign into
the already implemented smooth-scroll route.

Godot now translates wheel-up/down into signed 120-unit deltas, and the VM atomically accumulates and
consumes them. This extends the existing generic raw-input callback seam and introduces no script-specific
offsets, seeded state, or persistence choice. A focused regression proves accumulation/read-clear behavior;
a real SC0000-to-HISTORY regression opens after eight messages and proves wheel-up changes the retained row
selection without releasing the enclosing ADV wait.

The canonical opcode and EngineCtx sources are regenerated, and `/v2` now names/comments the recovered main
window procedure and the specific `0x10d` handler. HISTORY is 74/78 distinct opcodes and 849/854 instructions
handled or safe-noop. Remaining: `0x8b` twice, paired sprite-animation service ops `0x1ce`/`0x20a`, and the
deferred Read-message Skip setting getter `0x1cb`.

**Next:** reverse the common text-style block around `0x8b`; it is the smallest remaining non-persistence
gap. Keep `0x1cb` deferred, and treat `0x1ce`/`0x20a` together as their own sprite-animation slice.

Validation: all 205 engine tests pass, opcode and EngineCtx tests/lints are clean, vm0 RECOVER passes, the
Godot build has zero warnings, and threaded `SELFTEST OK`.

### ADV History text line spacing implemented (2026-07-19)

Native RE resolves the two remaining `0x8b` calls as text-manager line leading. The handler writes its
single pixel count to manager offset `+0x560` (`EngineCtx+0x14ea0`), whose initializer default is 6.
Horizontal newline and History rendering add that value to the primary font's positive pixel height; the
script's concrete style blocks use 8 pixels with their 22/24-pixel Mincho fonts and 9 with a 16-pixel
Gothic font.

The VM now retains that current style property, Godot applies it as the Label `line_spacing` theme
constant, and History rendering matches native ownership: line spacing comes from the text manager's
current state rather than from the retained record. A focused regression distinguishes a record retained
with spacing 9 from the current History spacing 8, and the real HISTORY route requires all non-empty rows
to render with its setup value of 8.

The canonical opcode and EngineCtx sources are regenerated. The `/v2` handler and all supporting
initializer/line-advance functions are named and commented, the expanded 58-field `EngineCtx` is reapplied,
and the program is saved. HISTORY is now 75/78 distinct opcodes and 851/854 instructions handled or
safe-noop. The remaining instructions are paired sprite-animation service ops `0x1ce`/`0x20a` and the
deferred Read-message Skip setting getter `0x1cb`.

**Next:** investigate and implement `0x1ce`/`0x20a` together as the final non-persistence History support
slice. Keep `0x1cb` deferred until the global save/profile storage boundary is chosen.

Validation: all 205 engine tests pass, including four focused History presentation cases; opcode and
EngineCtx tests/lints are clean, vm0 RECOVER passes, the Godot build has zero warnings, and threaded
`SELFTEST OK`.

### ADV History text publication / wait-indicator service implemented (2026-07-20)

Native RE corrects the provisional "sprite-animation service" description: `0x1ce` and `0x20a` are the
explicit control points for ADV text publication and its animated input-wait indicator. `0x1ce(enabled)`
starts or stops the indicator's run-state/timer/frame service; Himegari only calls it with zero when entering
modal scripts, including HISTORY. `0x20a(layout_slot)` republishes the selected retained text layout and,
when the service is active, binds its current indicator frame; HISTORY's shared redraw callback uses slot 1.
Normal ADV wait opcode `0x72` implicitly arms the same service.

The VM now forwards both operations through generic host methods. Godot gates its existing animated marker
on the explicit enabled state and requests recomposition for layout publication. Because the port keeps the
parent wait blocked while a nested raw-input callback executes, it restores a previously enabled parent
marker when that callback returns, matching the native path that re-enters the shared redraw/wait routine.
No script offsets, boot seeds, or persistence assumptions were introduced.

A focused synthetic regression verifies the two service calls, and the real SC0000-to-HISTORY regression
requires marker disable plus layout-slot-1 publication while preserving the same enclosing ADV wait and
cleaning the modal rows. The canonical opcode and EngineCtx sources are regenerated; the `/v2` handlers and
workers are named/commented, the expanded 63-field `EngineCtx` is applied, and the image is saved. HISTORY
is now 77/78 distinct opcodes and 853/854 instructions handled or safe-noop. The only remaining instruction
is the deliberately deferred `0x1cb` Read-message Skip setting getter, pending a global profile/save backend
decision.

Validation: all 206 engine tests pass, opcode and EngineCtx tests/lints are clean, vm0 RECOVER passes, the
Godot build has zero warnings, and threaded `SELFTEST OK`.

### SC0000 deferred SFX start and voice/BGM duck control implemented (2026-07-20)

Native RE and the saved SFX trace resolve two high-frequency audio gaps. Opcode `0x2bf` is the sound
facade's `SetDelay(channel,start_mode,delay_ms)`: SC0000's first `(4,0,100)` call starts the preloaded
secondary E0808 channel about 109 ms later through the same worker used by `0xb5`. The port now schedules
that start against the presentation clock and attaches it to a per-channel generation, so a later load or
release cancels an obsolete callback instead of starting replacement audio.

Opcode `0x1cf` replaces a transient voice/BGM-duck control mask; bit 0 suppresses automatic ducking.
The native engine registers ducking enabled with a 50-percent target, and its voice-start helper saves the
current BGM level before applying that target. Godot now follows that contract and restores the saved level
when voice playback completes or message Skip stops it. This is runtime audio state only: no profile/save
field, boot seed, or storage-backend decision was introduced.

The canonical opcode and EngineCtx sources are regenerated, the expanded 65-field `EngineCtx` is applied,
and the `/v2` handlers/workers are named, commented, and saved. SC0000 is now 119/129 distinct opcodes and
16,238/16,257 instructions handled or safe-noop (92.2% distinct; 99.9% instruction-weighted); its ten
remaining effectful gaps total 19 instructions.

**Next:** investigate the six-call `0x1ad` cluster first. It is now the largest remaining SC0000 gap and
should be classified before choosing between it and the smaller two-call support pairs.

Validation: all 206 engine tests pass; opcode and EngineCtx lints, vm0 RECOVER, the zero-warning Godot
build, and threaded `SELFTEST OK` are clean.

### Slice A2b-0x1ad investigation — numbered-save resume-frame marker (2026-07-20)

The six-call SC0000 `0x1ad` cluster is now classified at high confidence. The zero-operand handler
`0x416b70` stores the current script-context index in `ctx+0x9928c`. Numbered-save serializer layouts 2/3
use that index as the inclusive high frame, copy activations `0..mark`, and clear the marked activation's
saved return target so it becomes the top frame after load. Opcode `0x2` clears the mark when unwinding below
it. Across the corpus, 1,928 calls in 304 scripts place the marker at main-script entries and after modal
script returns; SC0000's sites are startup plus `HISTORY`/`MENU`/`HIDEWIN`/`INPUTNAME` return paths.

No runtime implementation landed. The current `GameSession` JSON format persists global banks only and has
no active `ExecFrame` chain or numbered-save lifecycle, so `0x1ad` cannot have its native observable effect
without choosing the unified save backend already being deliberately deferred. It remains an effectful gap,
now explicitly grouped with persistence work rather than UI support.

The investigation also corrected a foundational native-field label: `ctx+0x53d88 + curCtx*0x78` is the
decoded instruction length in dwords (`1 + 2*argc`) used by `adv_interpreter_tick` to advance the PC, not a
gfx command type. The canonical EngineCtx field, affected opcode prose, native RE, SCJUMP note, and legacy
Frida-tool description now reflect that distinction; no completed host gfx behavior changes as a result.

**Next:** leave `0x1ad` and `0x1cb` together behind the future save/profile boundary. Investigate the paired
zero-operand `0x1f6`/`0x20e` calls next: both bracket SC0000's ADV scene setup/teardown paths and are the
largest repeated non-persistence cluster still unclassified.

Validation: all 206 engine tests pass; opcode and EngineCtx tests/lints, vm0 RECOVER, and `git diff --check`
are clean. SC0000 coverage deliberately remains 119/129 distinct opcodes handled (92.2%), with `0x1ad`'s
six calls retained as a gap until numbered saves serialize active execution frames.

### Slice A2b-0x1a2/0x1a3 investigation — shared SAVE.DAT integer cells (2026-07-20)

The foundational field correction does not invalidate completed visible graphics/animation/UI behavior, but
the flagged `0x1a2` debt is now fully classified. `0x1a2` snapshots the selected global integer cell into a
shared-profile hash keyed by its resolved global-bank index; `0x1a3` restores the paired value or zero. Native
`shared_profile_save` serializes the table to shared `SAVE.DAT`, and `shared_profile_load` reconstructs it.
It is neither retained graphics state, numbered-slot state, nor `RT.DAT` ReadTextDB state. The full native
contract and provenance live in `docs/engine-re.md` and `vm-map/opcodes.toml`.

The inert `GfxState` approximation and its VM handler were removed. This intentionally changes coverage from
a false `impl` classification to an explicit effectful gap; it does not change visible runtime behavior because
the old set had no readers. `0x1a3` was already a default-stub gap. The port's whole-global-bank `GameSession`
snapshot can mask some persistence effects, but it is broader than AGE's selected-cell service and cannot
faithfully reproduce load-or-zero or lifecycle boundaries.

**Deferred implementation boundary:** add the pair only with a profile-owned cell-index→raw-int32 service
shared across VM runs and an explicit decision for how port profiles persist/migrate shared `SAVE.DAT` state.
Do not seed values, special-case script offsets, or silently fold this table into retained graphics or numbered
saves. This can be implemented independently of raw native file import/export once the port-owned profile
schema is chosen; `RT.DAT` and numbered-save frame state may share the owning profile service without sharing
their native on-disk formats.

The canonical opcode and EngineCtx sources are regenerated. The `/v2` handlers, shared-profile payload
reader/writer, and generic table helpers are named and commented; the expanded 69-field `EngineCtx` is
reapplied and the image is saved. Correcting the false implementation moves SC0000 from 119/129 to **118/129
distinct opcodes handled (91.5%)**: its 206 `0x1a2` instructions now appear honestly among 225 effectful-gap
instructions. This is a tracker correction, not a new runtime failure.

**Next:** keep `0x1a2`/`0x1a3`, `0x1ad`, and `0x1cb` grouped behind the future unified profile/save ownership
decision. Continue with the paired zero-operand `0x1f6`/`0x20e` scene setup/teardown investigation as the
largest repeated non-persistence SC0000 cluster.

Validation: all 206 engine tests pass; opcode and EngineCtx tests/lints, vm0 RECOVER, and `git diff --check`
are clean.

### Slice A2b retained-graphics lifecycle implemented (2026-07-20)

Native RE split the apparent `0x1f6` / `0x20e` pair into two separate services. `0x1f6` clears the retained
handle-to-object registry without releasing surface resources, and the commonly adjacent `0x23d` stops
movie bindings and releases transient surface slots 42 through 999. Independently, `0x20d(slot)` selects
an offscreen Direct3D render target (or the backbuffer for values at least 1000), and `0x20e` clears the
selected color target to black plus its depth buffer to 1.0.

The VM now models all four operations generically. `GfxState` owns the selected target and distinct
object/surface lifecycle; the host receives target clears and fixed-range surface release. Godot removes
modeled offscreen text pixels on clear, stops movies and clears slot-owned resources on the bulk release,
and continues to rebuild the main retained frame from black. Focused regressions prove object clear
preserves surfaces for rebinding, target selection reaches both an offscreen slot and the backbuffer, and
the bulk release preserves system-owned low slots while dropping slot 42.

The trace corrected an older EngineCtx-base assumption: object registry `+0x408`, frame timer `+0xb550`,
and nearby animation flags are relative to the embedded retained-gfx manager at `EngineCtx+0x46614`.
Their actual absolute addresses are `EngineCtx+0x46a1c` and `EngineCtx+0x51b64` onward. Existing visible
graphics and animation implementations remain valid because they model the recovered worker behavior and
observed timing rather than reading native process addresses.

The canonical opcode and EngineCtx sources are regenerated. `/v2` names/comments the four handlers and
five supporting workers, and the corrected structure is reapplied before saving. SC0000 is now **121/129
distinct opcodes handled (93.8%)** and **16,037/16,257 instructions handled or safe-noop (98.6%)**; 206 of
the 220 remaining effectful-gap instructions are the deliberately deferred `0x1a2` shared-profile writes.

**Next:** investigate/implement the two-call `0x242` retained-object field setter. It is already classified
as an object `+0x2d0` write and is the smallest coherent non-persistence gap; keep `0x1a2`/`0x1a3`, `0x1ad`,
and `0x1cb` behind the future profile/save ownership decision.

Validation: all 209 engine tests pass; opcode and EngineCtx tests/lints, vm0 RECOVER, the Godot build,
threaded self-test, and `git diff --check` are clean.

### Slice A2b detached finite object animation implemented (2026-07-20)

The `obj+0x2d0` consumer resolves `0x242(handle,flags)` as a finite-animation detachment control. Bit 0
makes that object's one-shot color/scale/rotation/translation/source-rectangle group ignore `0x243`'s
global force-complete request and prevents it from holding native blocking-presentation dirty state, while
leaving redraw dirty so it continues animating asynchronously. Native clears bit 0 after the finite group
finishes. SC0000's two CG loaders pass zero; 302 of 303 corpus calls do the same. The only value-one use is
an animated battle object in `BTL.BIN`.

`GfxState` now retains and clones the control word, excludes detached channels from blocking waits without
excluding them from visual recomposition, protects them from `0x243`, and clears bit 0 on natural completion.
The `0x243` model is correspondingly completed: it commits every unprotected finite channel as well as
resetting the separate global animation clock. Focused regressions cover get-or-create/clone behavior and
the protected-versus-ordinary completion split.

The canonical opcode source is refined and regenerated. EngineCtx adds the animation-service flags at
`0x51b80` and refines the two dirty-field roles; the expanded 76-field structure is reapplied to `/v2`.
The `0x242` handler/worker, `0x243`, and the shared channel consumer are named/commented and saved. SC0000
is now **122/129 distinct opcodes handled (94.6%)** and **16,039/16,257 instructions handled or safe-noop
(98.7%)**. Its remaining seven gaps total 218 instructions, 206 of them the deferred shared-profile write.

**Next:** investigate the four adjacent one-call unknowns `0x19b`, `0x19c`, `0x1ca`, and `0xae` together
before choosing an implementation. Their low frequency makes a shared native-service relationship more
important than any one opcode's count; persistence-bound `0x1a2`, `0x1ad`, and `0x1cb` remain deferred.

Validation: all 211 engine tests pass; opcode and EngineCtx tests/lints, vm0 RECOVER, the zero-warning
Godot build, threaded self-test, and `git diff --check` are clean.

### Slice A2b ADV skip lifecycle implemented; adjacent persistence gaps classified (2026-07-20)

Native RE resolves script `savemesskip`/`loadmesskip` opcodes `0x19b`/`0x19c` as an ADV presentation
lifecycle pair. Suspend clears active fast-forward and the service gate while preserving the persistent
all-message Skip toggle. Resume re-enables the gate and recomputes active fast-forward from that toggle or
the live read-skip channel. The VM now models persistent and active skip state separately, and focused tests
prove both persistent-toggle preservation and read-skip-only reactivation.

The same investigation classified the two adjacent one-call gaps without papering them over. Opcode `0x1ca`
is the already-known `message:ReadTextSkip` settings write and remains deferred with `0x1cb`, `RT.DAT`, and
the profile-owned read-state decision. Opcode `0xae` is normally inert but becomes the serialized save-stack
restoration rendezvous: it restores frame PCs and walks saved contexts while a flag set by the save-data
deserializer is active. Because the port has no numbered-save `ExecFrame` backend, an unconditional no-op
would be a false implementation; it remains grouped with `0x1ad`.

The canonical opcode source and expanded 78-field EngineCtx source are regenerated. `/v2` names/comments
the three handlers and save deserializer, applies `adv_skip_service_enabled` and
`save_load_stack_restore_active`, and is saved. SC0000 is now **124/129 distinct opcodes handled (96.1%)**
and **16,041/16,257 instructions handled or safe-noop (98.7%)**. Its five remaining gaps total 216
instructions: `0x1a2`×206, `0x1ad`×6, `0x1cb`×2, `0x1ca`×1, and `0xae`×1.

**Next:** keep all five remaining gaps behind the unified profile/read-state/numbered-save ownership work.
Choose the next implementation slice from a different scene or runtime subsystem rather than claiming
normal-path no-ops for `0xae` or a transient-only `ReadTextSkip` setting.

Validation: 213 engine tests pass; opcode and EngineCtx generators/tests/lints, SC0000 coverage, vm0
RECOVER, the zero-warning Godot build, threaded self-test, and `git diff --check` are clean.

### Slice A2b opcode `0x1fe` immediate rotation classified (2026-07-20)

Native RE resolves `0x1fe(handle, axis_x, axis_y, axis_z, angle_degrees)` as the retained-object
immediate-current axis-angle rotation setter. The handler converts its four numeric operands to floats;
the worker stores the current axis and degree angle, rebuilds the current rotation matrix, and raises the
ordinary retained-gfx redraw flag. It neither creates a timed transition nor writes `0x21f`'s target
fields: it is rotation's direct-current companion to the implemented `0x1fd` current-scale setter.

Corpus use reinforces the decode: 186 of 187 calls use Z axis `(0,0,1)`, with only a DEBUG call using Y
axis `(0,1,0)`. SC0010's five sites include immediate `5`, `-5`, and `0` degree Z rotations in a wobble
sequence; SC0020 and SC0030 add six and three sites respectively. The port already retains
`RotationCurrent` and composes it in native matrix order, so implementation should be a narrow generic
`GfxState` current-rotation setter plus VM dispatch and focused tests. No runtime implementation is claimed
by this investigation.

**Next:** implement `0x1fe` through the existing retained-transform model. It requires no new host API,
storage backend, seed, or script-specific behavior.

Validation: opcode source regenerated; opcode tests/lint and `git diff --check` are clean.

### Slice B1 SYSTEM4 logical input bindings investigated (2026-07-21)

Native RE resolves SYSTEM4's adjacent `0xfe`/`0x107`/`0x10b`/`0x10c` block as the configuration layer for
the logical action mask consumed by `0xff`/`0x100`. `0xfe` sets a ten-action dispatch bound. Native default
arrows use 0=up, 1=right, 2=down, 3=left; keyboard mappings then assign Z/Enter, Space, C/LeftCtrl, X,
PageUp, and PageDown to actions 4 through 9. Joystick axes use the same directional actions, six physical
buttons map to actions 4 through 9, left mouse defaults to action 4, and SYSTEM4 remaps right mouse to 7.

The port presently sends six Godot UI actions directly to logical indices in a different directional
order and lets `0x100` scan all 32 slots. The next implementation should therefore be one generic,
engine-owned physical-to-logical binding service: model native defaults, implement all four setters, bound
callback polling/dispatch by the configured count, and route Godot key/mouse/joy events through the model.
It requires no save/profile decision, seed, or Himegari-specific branch. No runtime implementation is
claimed by this investigation.

**Next:** implement that input-binding service as one coherent slice with focused mapping, bounds, and
simultaneous-input regressions, then manually exercise TITLE/ROOM/ADV keyboard and mouse paths.

### Slice B1 SYSTEM4 logical input bindings implemented (2026-07-21)

`InputBindings` now owns AGE's process-wide physical-to-logical map. Its constructor reproduces native
count 7 and the retained defaults Up/Right/Down/Left, Enter, Space, and Backspace. Opcodes
`0xfe`/`0x107`/`0x10b`/`0x10c` mutate the action bound, joystick slots, mouse slots, and DIK-translated
Win32-VK map. Godot supplies physical key, left/right mouse, standardized joy-button, and primary-axis
state instead of hardcoded `ui_*` indices; direct-scene diagnostics replay SYSTEM4's 16 immediate mapping
calls through a bounded bootstrap while natural boot executes them normally.

The implementation also corrects `0x100`'s release boundary after a final native trace: set bits dispatch
only below `input_action_count` and resume at `0x100` for simultaneous inputs, while an empty mask dispatches
callback slot `input_action_count` and resumes after the opcode. SYSTEM4's value 10 therefore reserves slot
10 as the no-input/release callback rather than exposing action bit 10. The old synthetic release-bit queue
is no longer used by Godot. Focused tests cover native defaults, all four setters, the real SYSTEM4 block,
empty-mask dispatch, simultaneous/bounded actions, and native range failures.

**Next:** manually exercise TITLE, ROOM, and ADV with keyboard, left/right mouse, and—if available—a
controller. A clean check should confirm native direction order, Z/Enter/Space behavior, right-click action
7, and release re-arming before selecting another reached Phase-B cluster.

### Slice B1 ADV held fast-forward implemented (2026-07-21)

Manual validation found the general input bindings sound but LeftCtrl did not accelerate ADV. Native tracing
resolves the missing consumer: `adv_interpreter_tick@0x410fb0` polls the configured logical mask and uses
action 6's bit `0x40` for transient fast-forward. It does not test Ctrl directly, so SYSTEM4's C/LeftCtrl
bindings and the engine's retained Backspace default intentionally share the behavior.

The VM now forwards held action 6 through an ADV-lifecycle-gated physical skip channel. Godot combines it
with, but stores it separately from, persistent op-`0x88` Skip; release therefore restores normal playback
only when the persistent channel is also off. The combined state reuses text completion, wait advance,
voice deferral/release, and skip pacing. Focused tests cover Backspace/C/Ctrl, activation on ADV resume, and
persistent-toggle survival across a physical press/release.

**Next:** manually hold LeftCtrl through text reveal and multiple waits in SC0000, then confirm release
returns to normal cadence and that the on-screen Skip toggle remains active if enabled during the test.

### Slice B1 ADV right-click system-menu path investigated (2026-07-21)

The native and corpus paths agree end to end. SYSTEM4 maps right mouse and X to logical action 7; every one
of the 136 SC-family scripts binds action 7 through op `0x97` to a dummy hotspot whose activation branch
calls `MENU.BIN`. Native `adv_input_service_poll` resolves these bindings against the live logical-action
mask before ordinary page advance. The port already stores the binding in `HotspotRegistry.Entry.InputBit`,
but nothing reads it, so action 7 stops after registration even though the physical input layer is correct.

`MENU.BIN` is 48/49 handled, and its INFO selector is 30/30. The only MENU gap, op `0x80`, is a default
retained graphics-object-slot selector consumed by op `0x1d9`; it is straightforward state but not the
launch blocker. Larger character/info detail screens are about 91.5–95% handled and should be treated as
follow-up discrepancies reached through manual use. SAVE/CONFIG remain separate, storage-sensitive work.

**Next:** implement one generic bound-action hotspot activation API, route pressed keyboard/mouse/controller
action masks through it before page advance, and implement op `0x80`'s engine-owned selector. Add a focused
synthetic bound-action test plus an SC0000 action-7 regression that proves the real `MENU.BIN` frame is
entered and the parent ADV wait is restored after return.

**Implemented:** the armed hotspot registry now scans op-`0x97` bindings in native registration order and
feeds matches through the existing activation callback lifecycle. Godot sends pressed keyboard, mouse, and
joystick action masks through that bridge before ADV page advance, so right mouse and X both reach the
script-owned MENU callback without a game-specific shortcut. Op `0x80` retains the selected default graphics
object slot. Focused coverage proves single-consumption/rearm behavior, the selector contract, and actual
SC0000 action-7 entry into release `MENU.BIN` followed by restoration of the parent ADV hotspots. Validation:
255 engine tests, zero-warning Godot build, and threaded selftest.

**Next:** manually exercise right-click and X from an SC0000 ADV wait, verify the MENU shell can be navigated
and closed without losing the underlying page, then classify only the secondary submenu gaps actually reached.

**Manual validation:** passed on 2026-07-21. The system menu opens from ADV and returns successfully through
the implemented script-owned action path; no underlying-page restoration discrepancy was reported.

### Slice B1 movie-surface stop-time query implemented (2026-07-21)

Native RE corrected op `0x23f` from the port's former retained-object existence test to
`query-surface-stop-time-ms(out, surface_slot)`. It calls `IMediaPosition::get_StopTime`, multiplies seconds
by 1000, truncates toward zero, and returns -1 only for an empty movie slot. All 23 Himegari sites are
associated with a preceding non-modal `0x236`; FIELD consumes the result as an effect lifetime.

The Windows decoder now obtains the stop time inside `0x236`'s synchronous graph-construction boundary and
hands it into engine-owned per-surface movie state before the VM advances. The already initialized decoder
is staged for main-thread adoption, so frame delivery remains asynchronous without reopening the graph.
`0x23f` returns the retained value. If a movie exists but its decoder supplies no finite signed-32-bit
timing value, the host reports a warning containing script/offset/surface context and returns -1; a genuinely
empty movie slot returns -1 silently. Focused tests cover all three outcomes and the real SC0000 DirectShow
payload exposes a positive stop time. Validation: 270 engine tests, zero-warning Godot build, and threaded
Godot selftest.

**Manual validation:** passed on 2026-07-21. A normal Game Start through SC0000's movie-backed opening
produced no `movie stop-time unavailable` warning, confirming the ordinary DirectShow timing handoff.

**Next:** continue with the next concrete discrepancy reached by manual testing.

## Performance tuning sidebar (started 2026-07-22)

This bounded effort addresses frame-time collapse during simultaneous retained effects (first observed in
SC0000 immediately before the CHAPTER movie) and in unit-heavy dungeon presentation. Earlier work already
removed redraws during static waits, batched the CPU backbuffer, cached decoded source pixels, and added an
integer-translation raster path. The remaining architecture still rebuilds an 800x600 RGBA backbuffer in
C# and uploads the complete image whenever any visual channel is active. Effect bursts multiply full-screen
and general-affine pixel work; a single ambient spritesheet channel also keeps that path active every Godot
frame even when its discrete source cell has not changed.

The effort will keep `GfxState` as the backend-neutral AGE truth and retain the current software renderer as
a fidelity oracle. Optimization claims require repeatable frame-time evidence from the real interactive
path, not screenshot-sequence runs whose PNG I/O dominates timing.

### Target workloads and success measures

Use three fixed workloads: a static ADV wait (control), the SC0000 pre-CHAPTER burst, and the
DEBUGMAP/FIELD camera-pan stress case as the current dungeon proxy. Save loading is not yet available, so
reaching a reproducibly unit-heavy story dungeon is disproportionate to this sidebar; add that validation
later when save loading or progression makes it practical. Capture the exact script/offset alongside each
frame. Compare equivalent Release/windowed runs by median, p95, and p99 frame time, recomposition time,
raster time, upload time, allocation rate, visible/time-varying object count, raster path, and candidate
pixel count. Track both presentation cadence and the cost of each recomposed frame: reducing how often an
unchanged discrete animation is drawn must not disguise an over-budget frame when it does change. The
immediate target is sustained 60 FPS where the native presentation has no intentional lower cadence; p95
must remain under 16.67 ms on the development machine, with no screenshot or behavioral regression at the
sampled coordinates.

### Execution slices

- [x] **P0 - frame/compositor performance log.** Add an opt-in `--perf-log <csv>` diagnostic. Record bounded,
  buffered per-frame phase timings, compositor workload counters, managed allocations/GC deltas, and the
  current VM script/offset/opcode. Document the schema and validate the writer independently of Godot.
- [x] **P1 - capture and attribution.** Record the static ADV control, SC0000 burst, and DEBUGMAP/FIELD
  dungeon proxy without `--gfx-log`, `--timeline-log`, or frame PNG capture. Use controlled compose/upload
  ablations only if the phase timings do not isolate the cost. Commit the baseline percentiles and exact
  canonical runtime coordinates here. A real unit-heavy story dungeon is deferred until it is practical
  to reach reproducibly.
- [x] **P2 - fidelity-neutral CPU wins.** P2a-P2f are implemented and measured. DEBUGMAP now meets its p95
  frame budget with near-zero steady allocation; the SC0000 exit capture remains 65.18/71.76 ms p50/p95 in
  the severe full-screen/additive band, so P2 closes and triggers P3 rather than more CPU special cases.
- [x] **P3 - GPU retained-renderer prototype.** Upload decoded/color-key texture variants once and mirror
  ordinary retained objects into GPU-native Godot drawing while preserving handle z-order, atlas regions,
  transforms, opacity/tint, and blend mode. Begin with dungeon sprites and ordinary translated textures;
  compare selected frames against the software oracle behind a backend switch.
- [x] **P4 - GPU special paths and backend decision.** Extend the prototype through affine effects,
  additive/tint channels, created surfaces, transitions, and movie surfaces. Adopt it as the default only
  after target-workload frame evidence and visual equivalence; otherwise retain documented CPU fallbacks
  for unsupported paths.

Do not optimize VM dispatch or investigate GPU stalls without evidence from P0/P1. `--trace-histogram`
remains the opcode-frequency tool; it is not a frame profiler. Godot's generic frame monitor is useful
corroboration, but the project log owns the AGE-object and raster-work attribution required by these slices.

### P0 frame/compositor performance log implemented (2026-07-22)

Godot now accepts `--perf-log <csv>`. The buffered writer records one row per `_Process` frame with the
VM script/offset/opcode at frame entry and the actual presentation boundary, Godot delta, measured main-loop
phases, managed allocations/GC deltas, and
the retained compositor's clear/snapshot/resolve/source-prep/raster/SetData/texture-upload split. Its workload counters
include actual object/layer visits, sampled time-varying objects, transition and blend categories, integer
versus affine paths, dynamic sources, source-pixel area, and clipped destination bounding-box candidates.
The writer and affine clipping calculation are Godot-independent and directly unit tested. `RenderObject`
now exposes whether its sampled object still has an active finite, spritesheet, color, rotation, surface, or
range-transform channel; this is observation only and does not alter scheduling.

The threaded selftest wrote 101 coordinate-bearing rows and remained `SELFTEST OK`. A five-frame headless
SC0000 schema smoke used isolated output paths and exercised one real recomposition, producing populated
snapshot/resolve/object/skip counters; its dummy-renderer viewport warnings are why headless numbers are not
performance evidence. Validation: 337 engine tests pass, including the new CSV/clipping and time-varying
object regressions, and the Godot build has zero warnings/errors. **Next:** P1 must capture windowed, speed-1
control/pre-CHAPTER/quiet-dungeon/unit-heavy-dungeon runs without the high-volume diagnostics, then record
their exact coordinates and percentile attribution here before selecting P2 changes.

For the duration of P1/P2 collection, `run-godot.cmd` now opts into timestamped performance capture on every
windowed run. The launcher creates `build/perf/run-yyyyMMdd-HHmmss-fff.csv` and prints its path, allowing a
normal play session to cover multiple reported passages without restarting solely for instrumentation.
Direct PowerShell launches remain opt-in through `-PerfLog`, and selftests remain unprofiled. Remove this
temporary batch-file default when the performance effort closes.

### P1 capture 1 - SC0000 through the pre-CHAPTER burst (2026-07-22)

`build/perf/run-20260722-105708-610.csv` is the first controlled interactive capture: 3,307 frames over
55.1 seconds, including 2,836 SC0000 frames and 1,273 SC0000 recompositions. Static SC0000 frames establish
the control inside the same run: across 1,563 non-recomposited frames, measured main-loop p50/p95/p99 was
0.014/0.024/0.032 ms. The active renderer is the problem: SC0000 recompositions measured main-loop
p50/p95/p99 21.80/55.35/82.69 ms; raster p95 was 54.57 ms, while snapshot p95 was 0.017 ms,
`Image.SetData` p95 0.456 ms, and texture upload p95 0.215 ms.

The reported simultaneous-effect lag is one contiguous 83-frame plateau (frames 1618-1700, virtual time
20,727-26,306 ms) parked at presentation boundary `SC0000@0x123de` (`0x21c`). Its main-loop p50/p95/max was
64.28/87.82/91.54 ms, equivalent to roughly 15.6 FPS at the median and 11.4 FPS at p95. Rasterization was
98.4% of mean main-loop time. The workload grows from 8 to 12 drawn layers and averages 3.85 million clipped
candidate pixels per frame (4.98 million maximum), with as many as three general-affine layers, five
additive layers, and ten viewport-covering layers. The worst frame spent 90.82 of 91.54 ms rasterizing
11 layers; snapshot, resolution, source prep, SetData, and upload together remained below one millisecond.

A secondary lead appears outside the severe plateau: recomposited allocation p95 is 1,961,448 bytes and
source-prep p95 is 2.81 ms, with 0.991 correlation between the two. The size closely matches one 800x600
RGBA clone, and the affected rows have three dynamic layers; this is strong evidence for the existing
dynamic-color-key clone path, but it is not the cause of the 80-90 ms plateau (whose source prep is about
0.003 ms). Treat clone removal as a later independent P2 win.

This capture confirms the CPU rasterizer as the SC0000 bottleneck and makes upload/snapshot optimization a
low priority. P1 remains open until quiet- and unit-heavy-dungeon captures establish whether discrete
spritesheet scheduling is enough for dungeons or whether their pixel workload independently requires the
GPU retained-renderer path.

### P1 capture 2 - DEBUGMAP/FIELD camera pan (2026-07-22)

`build/perf/run-20260722-112100-189.csv` is a 1,381-frame interactive DEBUGMAP stress run. The map portion
contains 800 recomposited frames over 39.8 seconds. It measured main-loop p50/p95/p99
49.17/64.59/68.39 ms and raster p50/p95 45.58/59.36 ms. Snapshot, resolution, source preparation,
`Image.SetData`, and texture upload again remain small in steady state; raster time correlates 0.987 with
main-loop time. The dominant idle/pan boundary is `FIELD.BIN@0x1029` (`0xc8`): 438 samples average roughly
962 visited objects, 961 submitted layers, 51 time-varying objects, and 2.16 million clipped candidate
pixels. Its main-loop p50/p95 is 49.05/59.03 ms.

Camera position materially changes work rather than eliminating the bottleneck. Across representative
50-frame windows, candidate pixels range from 1.12 to 3.10 million and average main-loop time from 28.1 to
63.8 ms; candidate count correlates 0.858 with main time. Object count alone is not enough to explain the
range. At the same approximately 962-object FIELD boundary, unusually light frames with 953 integer and
only seven affine layers render 1.20 million candidates in about 10.3 ms, while typical frames with roughly
432 integer and 526 affine layers render 1.6-2.5 million candidates in about 26-60 ms. General-affine pixel
sampling is therefore a major dungeon cost in addition to the persistent layer traversal.

The run exposes two independent secondary issues. Recomposited FIELD frames allocate about 3.59 MB at the
median and trigger 32 gen-0, ten gen-1, and five gen-2 collections. The compositor currently formats a
verbose outcome string for every visited object even when neither `--gfx-log` nor `--timeline-log` created
the decisions dictionary; gating that diagnostic formatting is the first low-risk P2 allocation fix. One
outlier (frame 1065) spends 37.99 ms resolving a resource and 48.69 ms rasterizing for a 90.53 ms total;
the following steady frames return to raster dominance, so resource-resolution/GC stalls should be tracked
separately from the continuous map cost.

DEBUGMAP is sufficient as an intentionally heavy retained-map stress case: it confirms that discrete-cell
scheduling can remove unnecessary frames from the 51 animated objects, but cannot make a recomposed frame
with hundreds of mostly affine layers fit a 16.67 ms budget by itself. P2 should gate unused diagnostic
strings, add discrete-cell scheduling, and benchmark affine/opaque fast paths. A representative real
unit-heavy dungeon remains a later validation workload once save loading or progression makes it practical;
DEBUGMAP is the accepted P1/P3 stress proxy for now.

### Evidence-driven performance action plan (2026-07-22)

The save-loading limitation makes DEBUGMAP/FIELD the accepted dungeon proxy for this effort, so P1 is
closed. The SC0000 plateau and DEBUGMAP steady map load exercise complementary failure modes: SC0000 has
few layers but extreme full-screen/additive pixel work, while DEBUGMAP has hundreds of retained layers,
many sampled through the affine path, and discrete animation that currently requests a recomposition every
Godot frame. Execute the following in order, retaining the software compositor as the pixel oracle.

1. [x] **P2a - remove diagnostic-only allocation from normal rendering.** Construct per-object outcome strings
   and final `z` decision strings only when `--gfx-log` or `--timeline-log` actually supplied a decisions
   dictionary. Preserve byte-for-byte diagnostic output when enabled. Re-run DEBUGMAP and require a large
   reduction from its approximately 3.59 MB median allocation per recomposition without worsening raster
   time; if less than 70% disappears, use an allocation trace to identify the remaining owners before
   doing speculative collection tuning.
2. [x] **P2b - measure the two ambiguous dirty/raster categories.** Extend the low-overhead counters only as
   needed to distinguish VM-requested, continuous-channel, and discrete-cell presentation, and to split
   pure fractional translation from scale/rotation/general affine work. The current log proves that the
   broad categories matter but cannot tell whether DEBUGMAP's roughly 526 apparent affine layers are
   camera-induced fractional translations or genuine scale/rotation. Do not change sampling semantics on
   that assumption alone.
3. [x] **P2c - reproduce the native shared dirty/cell cadence.** Native does not give each visible sprite a
   host-frame redraw timer. One shared current/previous millisecond frame-time pair feeds every channel;
   op `0x231` compares the source cell selected at those two samples and raises graphics dirty only when it
   changes. Retained mutations and genuinely continuous channels still redraw as required, while an
   opcode-`0xc8` poll iteration alone does not. Track retained mutation publication so FIELD's `sleep(1)`
   input loop stops forcing unchanged compositions, and replace “any spritesheet is active” with shared
   cell-change detection. FIELD's prototype `0x9c40` is configured once at 200 ms/four cells and cloned
   before first presentation, so the unit family is phase-locked at 5 Hz rather than 51 independent
   deadlines. Cover clone-before/after-first-sample, reconfiguration, differing periods, wraparound, and
   exact boundary cases. The DEBUGMAP acceptance metric is recompositions per second and total delivered
   frame time; the separately reported p50/p95 cost of frames that do recompose must remain visible.
4. [x] **P2d - specialize the measured axis-aligned scale work.** P2b disproved the fractional-translation
   hypothesis: the new DEBUGMAP capture reports zero fractional and zero general-affine layers, with every
   non-integer layer classified as axis-aligned scale. Implement the nearest-neighbor-equivalent scale path and prove it
   byte-for-byte against the existing inverse-mapped oracle across positive/negative coordinates,
   half-pixel boundaries, clipping, opacity/tint, and every blend mode. This is the highest-potential
   DEBUGMAP per-compose CPU win because the existing translated loop is much cheaper than a general matrix
   inverse and transform per destination pixel.
5. [x] **P2e - specialize the remaining hot pixel loops.** The measured axis-aligned scale specialization is
   complete. Benchmark opaque/full-opacity/unmodulated translated and scaled pixel loops next, using direct
   copies for fully opaque source texels and the existing blend arithmetic at transparent edges. General
   incremental inverse coordinates are low priority because the measured FIELD workload has no general-
   affine layers. Use randomized differential raster tests against the current implementation plus the SC0000
   and DEBUGMAP captures. Address the dynamic color-key full-frame clone separately because it explains an
   allocation/source-prep spike but neither primary raster plateau. Do not spend time on snapshot, clear,
   `Image.SetData`, texture upload, VM dispatch, or parallel rasterization while their measured contribution is
   small or a retained GPU renderer is the cleaner boundary.
6. [x] **P2f - remove the measured per-frame collection owners.** Replace `Transform2DMath.Build`'s heap
   4x4 matrices with an equivalent value-type affine-3D representation, and let the compositor reuse a
   caller-owned, handle-sorted `RenderObject` snapshot buffer. Preserve the allocating snapshot overload for
   callers which retain independent samples. Require exact matrix/sort differential coverage and direct
   zero-allocation assertions after warm-up; confirm the real path with the allocation-phase CSV columns.
7. **P2 exit gate and P3 trigger.** After the safe CPU batch, repeat equivalent SC0000 and DEBUGMAP runs.
   Report both end-to-end cadence and recomposed-frame p50/p95/p99. If either workload's required
   recompositions remain above 16.67 ms p95, begin P3 rather than accumulating increasingly complex CPU
   special cases. The expected P3 prototype mirrors ordinary translated/atlas dungeon objects into Godot
   canvas items behind a backend switch, uploads decoded/color-key variants once, and updates retained
   items only when their state changes. It must preserve handle order, transforms, tint/opacity, and blend
   behavior and compare selected output against the software oracle before expanding to SC0000's affine,
   additive, transition, created-surface, and movie special paths in P4.

Each landed optimization gets a before/after row in this document with capture path, canonical runtime
coordinate, p50/p95/p99, allocation, recomposition rate, and raster-work counters. Revert or leave behind a
disabled experiment when it does not produce a repeatable real-path improvement. The next capture validates
P2a-P2c together and uses P2b's new categories to choose P2d without another story-progression dependency.

### P2a-P2c implementation checkpoint (2026-07-22)

Normal runs no longer construct per-object compositor outcome or final z-decision strings unless
`--gfx-log` or `--timeline-log` requested that evidence. This removes the known DEBUGMAP diagnostic-only
allocation source while preserving the diagnostic path. The performance CSV now records five independent
presentation reasons (host request, legacy screen transition, retained mutation, continuous channel, and
discrete source-cell change) and separates fractional translation, axis-aligned scale, and general affine
layers while retaining the original aggregate affine count.

`GfxState` now publishes retained mutation generations once and samples one shared current/previous frame
time pair on every host tick. Continuously varying channels remain frame-driven. Visible op-`0x231`
spritesheets request composition only when their selected cells differ between the shared samples, and
clone-before-first-sample records seed together. VM object writes that previously escaped the model lock are
now applied through synchronized setters so publication cannot race ahead of the retained write. Opcode
`0xc8` sleep completion no longer requests presentation by itself; preceding graphics writes are covered by
the mutation generation instead.

Focused coverage includes single-publication mutation behavior, exact cell boundaries and wrap, differing
object-local periods, reconfiguration, clones made before and after the first sample, continuous-channel
behavior, and a 51-object phase-locked family producing five—not 255—source-cell publication events per
second. The complete engine suite, Godot build/selftest, and a new comparable windowed DEBUGMAP capture are
the closeout gates. Until that capture is analyzed, the allocation and recomposition improvements are
implemented expectations rather than measured before/after results.

### P2 capture 3 - DEBUGMAP after allocation/cadence changes (2026-07-22)

`build/perf/run-20260722-121910-248.csv` is the first post-P2a-P2c windowed DEBUGMAP run. Its FIELD interval
contains 1,028 frames over 53.5 seconds and 1,023 recompositions (19.12/s), so discrete-cell scheduling did
not reduce end-to-end cadence in this workload. The reason columns explain why rather than invalidating the
shared-cell implementation: 1,014 FIELD rows have a continuous-channel reason. FIELD creates one visible
op-`0x232` color pulse at `FIELD@0x9691` (handle `0xc802`, period 2,000 ms), which legitimately changes the
composed output between spritesheet boundaries. The 50 ms hover callback also produced 712 host-publication
requests, including idle callbacks; callback completion is now no longer treated as dirty, while actual
retained and host-surface mutations continue to publish themselves.

P2a was useful but missed its stated allocation gate. Recomposition allocation p50 fell from 3,593,400 to
3,172,128 bytes (-421,272, 11.7%); p95 fell from 3,629,816 to 3,178,544 bytes (-451,272, 12.4%). Raster p50
was effectively unchanged (45.43 versus 45.52 ms), as expected for an allocation-only edit. The next logger
schema splits remaining allocation among snapshot, composition, source preparation, `Image.SetData`, and UI
so the next capture supplies bounded allocation attribution rather than prompting collection tuning.

The new transform counters decisively redirect P2d. Recomposed FIELD frames average 447.7 integer layers and
506.3 axis-aligned-scale layers; fractional-translation and general-affine maxima are both zero. The busy
28-34 second interval averages about 2.35-2.39 million candidates and 54 ms main time, while the final
off-map idle interval falls to about 1.58 million and 26 ms. P2d therefore adds an axis-aligned inverse-map
path which caches the source column once per layer and computes the source row once per scanline, retaining
the existing center-sample/floor and blend arithmetic. Pooled lookup storage avoids per-layer garbage.
Five hundred deterministic randomized blit/fill cases compare byte-for-byte with the previous general
inverse-mapped oracle across positive/negative scales, fractional placement, clipping, opacity, tint,
multiplicative modulation, and alpha/additive/opaque modes. A repeat DEBUGMAP capture is still required to
measure the real-path raster win and the new allocation phases. All 345 engine tests pass and the Godot
project builds with zero warnings/errors.

An environment audit after this capture found four Godot game/console pairs still alive from July 11 and
July 21. Each game process consumed about 0.41 CPU-seconds during a two-second sample (roughly 80% of one
logical core in aggregate). They predate both P1 and this capture, so workload attribution, allocation deltas,
and the transform-category correction remain actionable, but its absolute frame-time acceptance numbers are
provisional. The eight processes were terminated with user authorization before the following capture.

### P2 capture 4 - DEBUGMAP after axis-aligned scale specialization (2026-07-22)

`build/perf/run-20260722-130024-542.csv` is the clean post-P2d windowed comparison, captured after the stale
Godot processes above were closed. The stable FIELD workload still visits about 962 objects and draws about
961 layers: approximately 433 integer translations, 526 axis-aligned scales, zero fractional translations,
and zero general affine layers. Around 900 layers are classified opaque. Camera position changes clipped
candidate pixels, but it does not materially reduce retained traversal, layer count, source pixels, or the
continuous-channel presentation cadence.

For equivalent 2.2-2.5 million candidate-pixel frames, recomposition p50/p95/p99 fell from
53.71/60.50/62.72 ms in capture 3 to 21.52/22.26/22.75 ms. Raster p50/p95 fell from 50.77/57.27 ms to
19.07/19.52 ms: a 62.4% median raster reduction with the same workload band. The final 1.5-1.9 million
candidate-pixel off-map band fell from 27.34/32.34 ms recomposition p50/p95 to 14.73/15.89 ms. Delivered
steady cadence consequently rises from about 19 recompositions/s to 44-48/s over the busy map and about
64/s off-map. P2d is a large, repeatable real-path win; busy-map p95 nevertheless remains above the
16.67 ms P2 exit target.

The new allocation phases attribute the remaining steady busy-map p50/p95 almost exactly: total
3,178,544/3,178,544 bytes, retained snapshot 961,600/961,600, compositor 2,216,664/2,216,664, source
preparation zero, `Image.SetData` zero, and UI 88/88. Camera position barely changes that total because the
snapshot and compositor collections still cover the full retained set. Allocation is now a clear GC/long-run
stability target, but raster remains the direct busy-frame budget blocker. P2e therefore starts with the
full-opacity/unmodulated hot pixel loops used by the roughly 900 opaque layers; after that measured capture,
reduce the two identified per-frame collection owners rather than tuning the GC.

### P2e unmodulated source-over specialization implemented (2026-07-22)

The translated and axis-aligned-scale raster loops now detect full object opacity, zero effective tint,
non-multiplicative color, and non-additive blending once per layer. In that common mode, alpha-zero texels
remain skipped, alpha-255 texels become exact four-byte copies, and partially transparent edge texels retain
the previous integer source-over arithmetic. The general tinted, faded, multiplicative, and additive paths
are unchanged. This targets the approximately 900 opaque FIELD layers measured in capture 4 without
assuming that their color-keyed source rectangles contain no transparent pixels.

Four hundred focused translated/scaled cases cover both opaque and alpha blend classifications with source
alpha values 0, 1, 254, and 255 against the retained pre-fast-path oracle. The complete engine suite passes
at 346 tests, the Godot build has zero warnings/errors, and threaded `SELFTEST OK`. **Next:** repeat the same
DEBUGMAP camera/idle/off-map workload. Retain P2e only if the busy 2.2-2.5 million candidate-pixel band shows
a repeatable win; then address the measured snapshot/compositor allocation owners before the P2 exit capture.

### P2 capture 5 - DEBUGMAP after unmodulated source-over specialization (2026-07-22)

`build/perf/run-20260722-132318-886.csv` is the comparable post-P2e windowed run. In the matched 2.2-2.5
million candidate-pixel band, recomposition p50/p95/p99 fell from 21.52/22.26/22.75 ms to
15.35/16.18/19.26 ms, while raster p50/p95 fell from 19.07/19.52 ms to 12.66/13.23 ms. That is a 28.7%
median recomposition reduction and a 33.6% median raster reduction; steady busy-map delivery rises from
about 44-48 to 61-66 recompositions/s. The 1.5-1.9 million candidate off-map band falls from
14.73/15.89 ms recomposition p50/p95 to 9.45/11.21 ms. P2e is retained: busy-map p95 now fits the 16.67 ms
target, although allocation/GC outliers leave p99 above it.

Allocation remains unchanged at 3,178,544 bytes p50/p95 in the busy band. Capture-wide gen-0/gen-1/gen-2
counts are 184/52/34 over 3,111 FIELD recompositions, versus 147/49/34 over only 2,274 recompositions in
capture 4; normalized collection rates therefore do not regress, but short-lived garbage remains the clear
tail-latency and long-run-stability target.

### P2f measured allocation owners removed (2026-07-22)

The compositor allocation phase was dominated by `Transform2DMath.Build`: it created about eleven
`double[16]` matrices for every rendered object. It now composes the same row-vector operations through a
twelve-double value-type affine-3D matrix and projects to `Affine2D` only at the boundary. Five hundred
randomized scale/translation/anchor/one-shot/cyclic-rotation cases match every output double bit-for-bit
against the former array implementation, and 10,000 warmed builds allocate zero bytes.

The retained snapshot phase no longer uses `Dictionary.OrderBy` or returns a newly grown list to the Godot
hot path. `GfxState` maintains a sorted handle index alongside its O(1) object dictionary, and fills a
compositor-owned reusable `List<RenderObject>` under the existing lock. The returning overload remains for
callers needing an independent snapshot. Ten warmed 1,000-object samples allocate zero bytes and preserve
ascending handle order. The complete engine suite passes at 349 tests, the Godot build has zero
warnings/errors, and threaded `SELFTEST OK`. **Next:** repeat the comparable DEBUGMAP run and verify the
snapshot/compositor allocation columns collapse without changing the capture-5 frame-time distribution;
then repeat SC0000 for the P2 exit/P3 decision.

### P2 capture 6 - DEBUGMAP after primary allocation removal (2026-07-22)

`build/perf/run-20260722-133412-146.csv` confirms P2f's primary allocation changes on the real path. In the
matched busy band, total allocation p50/p95 falls from 3,178,544/3,178,544 to 262,160/262,160 bytes
(-91.8%). Retained snapshot allocation is exactly zero; compositor allocation falls from 2,216,664 to
261,880 bytes. Capture-wide gen-0/gen-1/gen-2 collections fall from 184/52/34 to 21/8/5 despite broadly
similar duration and 2,709 FIELD recompositions. Snapshot p50/p95 falls from 0.165/0.328 ms to
0.097/0.141 ms.

Raster remains stable at 12.70/13.36 ms p50/p95 versus capture 5's 12.66/13.23 ms. End-to-end busy
recomposition improves slightly from 15.35/16.18/19.26 ms p50/p95/p99 to 15.10/15.87/16.25 ms; the much
tighter p99 is consistent with the measured GC reduction. Matched off-map recomposition improves from
9.45/11.21/13.20 ms to 9.04/10.02/11.35 ms. P2f therefore preserves the raster win while removing the
large short-lived collection owners.

The stable 261,880-byte compositor remainder scales almost exactly with FIELD's approximately 961 retained
objects. The first hypothesis was the per-object `SnapshotSurfaceText` array copy, so the compositor was
changed to reuse a caller-owned `List<SurfaceTextDraw>` under the existing text lock; the returning overload
remains available to callers needing an independent snapshot. Capture 7 below disproves that attribution.

### P2 capture 7 - residual allocation probe (2026-07-22)

`build/perf/run-20260722-134322-530.csv` is the requested short steady DEBUGMAP idle probe. Across 1,208
full FIELD recompositions, total allocation remains 259,952/261,728 bytes p50/p95, with snapshot allocation
zero and compositor allocation 259,672/261,448 bytes. The surface-text buffer therefore has no material
effect in this workload and is only a harmless general cleanup. Recomposition remains in the expected
camera-dependent range at 14.19/16.46 ms p50/p95 for an average 2.38 million candidate pixels.

The exact remaining owner is `MovieSurfaceRegistry.TryResolveResource`: every ordinary still-texture
fallback constructed a LINQ `Where` plus descending sort pipeline to look for a live movie frame, including
when the movie registry was empty. This occurred once per retained object and explains the stable roughly
270 bytes/object remainder. The registry now scans its live bindings directly while retaining the rule that
the newest published playback of a resource wins. A focused test covers matching/missing lookups and newest-
playback selection; 10,000 pairs allocate zero bytes after warm-up. All 350 engine tests, the zero-warning
Godot build, and threaded `SELFTEST OK` pass. **Next:** one final short DEBUGMAP idle probe confirms the
compositor remainder is gone, then SC0000 through the pre-CHAPTER burst supplies the P2 exit/P3 decision.

### P2 capture 8 - DEBUGMAP allocation closeout (2026-07-22)

`build/perf/run-20260722-140820-530.csv` confirms the movie-registry correction. Across 820 full steady
FIELD recompositions, total allocation is 4,416 bytes p50/p95/p99 and compositor allocation is 4,136 bytes;
snapshot, source preparation, and `Image.SetData` remain zero, while UI accounts for 88 bytes. This is a
99.86% reduction from capture 4's 3,178,544-byte steady total and a 98.3% reduction from capture 7's
259,952-byte residual. FIELD itself performs zero gen-0, gen-1, or gen-2 collections in this probe; every
capture-wide collection occurred during boot/map setup.

One FIELD row allocates 1,415,744 bytes during composition at `FIELD.BIN@0x1029` as a one-time reusable-
capacity/cache warm-up. It triggers no collection and completes in 14.78 ms, so it is neither a steady owner
nor a visible stall. At an average 2.32 million candidate pixels, recomposition p50/p95/p99 is
13.90/15.86/18.89 ms and raster p50/p95 is 11.32/13.22 ms, consistent with the post-P2e/P2f distribution.
DEBUGMAP therefore closes with busy p95 inside 16.67 ms and effectively allocation-free steady rendering.
**Next:** repeat SC0000 through the original pre-CHAPTER burst and compare against capture 1's exact
`SC0000@0x123de` plateau before deciding whether P2 exits or the full-screen/additive case triggers P3.

### P2 exit capture 9 - SC0000 pre-CHAPTER and post-movie text (2026-07-22)

`build/perf/run-20260722-141200-995.csv` repeats capture 1 through the CHAPTER movie and the following text.
Across every recomposition parked at `SC0000.BIN@0x123de`, recomposition p50/p95/p99 improves from
21.85/57.40/82.70 ms to 15.39/31.71/66.38 ms. Movie sampling itself remains small: twelve active rows have
`movie_ms` p50/p95/max 0.20/0.99/1.06 ms, so decoding/presentation is not the reported burst bottleneck.

The acceptance decision uses equivalent severe rows rather than the mixed coordinate aggregate. For frames
with at least 3.5 million candidate pixels, recomposition p50/p95/p99 falls from 76.43/88.42/90.11 ms to
65.15/71.74/72.03 ms; raster p50/p95 remains 64.11/71.15 ms. These 52 rows average 4.29 million candidate
pixels, 10.7 layers, 4.6 additive layers, 8.3 viewport-covering layers, two general-affine layers, and less
than 0.9 KB managed allocation. The worst comparable frame is 72.21 ms. P2's CPU fast paths therefore save
roughly 15-19% in this burst but leave it around 14-15 delivered frames/s, more than four times the
16.67 ms budget. No allocation or scheduler optimization can close that gap.

A separate later interval still clones about 1.92 MB per recomposition while applying a color key to a
dynamic frame. It is an independent GC issue already anticipated by the action plan, but it does not occur
in the severe additive plateau and cannot change the exit decision. Treat it as part of the GPU texture/
shader ownership work rather than delaying P3 for another software-raster special case.

**P2 exit decision:** close the fidelity-neutral CPU batch and begin P3. The prototype mirrors ordinary
retained texture/fill objects into GPU-native Godot drawing behind a backend switch, preserves handle z-order,
atlas source rectangles, colorkey, tint/opacity, additive blending, and the exact affine transform, and keeps
the software compositor as the pixel-parity oracle/fallback. First acceptance is the same
`SC0000@0x123de` severe band below 16.67 ms p95 without regressing DEBUGMAP presentation or movie/text
composition; only then make GPU rendering the default.

### P3 retained-GPU prototype implemented; workload acceptance pending (2026-07-22)

`--render-backend gpu` now mirrors the synchronized `GfxState` snapshot into a pooled Godot `Sprite2D`
stage. Static decoded/color-key variants upload once; mutable created surfaces and movie frames update a
surface-keyed dynamic texture, so concurrent playbacks of the same resource cannot overwrite each other.
Each retained item preserves handle/child order, atlas source rectangle, nearest filtering, the exact sampled
affine matrix, opacity, multiplicative tint, source/tint LERP, and source-alpha/additive canvas blending.
Surfaceless affine fills use a shared one-pixel texture. The software compositor remains the default for
direct launches and is selected whole-frame for legacy whole-screen transitions or the still-unobserved
additive LERP-tint combination; diagnostics which depend on software decision strings also stay on the oracle.

The performance CSV now records backend, GPU draw-item count, texture uploads, and upload CPU time while
retaining the common object/layer/transform/candidate-pixel workload columns. A real Vulkan smoke exercised
54 GPU recompositions without Godot errors after startup; warmed three-item frames required about
0.02-0.10 ms of main-thread synchronization, while the first texture publications took about 2-3 ms. This
is not the target burst and is only a plumbing measurement.

The first visual comparison initially produced a false black result because the direct `SC0000` validation
command omitted its required `--boot` SYSTEM4/INIT state. Repeating with `--boot` restored the opening event
CG in both backends. At the settled first narration page, the GPU and software 800x450 retained-art regions
match in geometry/content; every differing channel is at most one RGB value and the differences are confined
to the soft lower fade, consistent with Godot floating-point versus software integer blend rounding. The
automated `--shot` harness forces pre-page transition waits, so its abrupt fade is not cadence evidence.

For this effort `run-godot.cmd` temporarily enabled both timestamped perf capture and the GPU prototype;
that temporary launch behavior was removed at P4 closeout. **Next:** use the natural SYSTEM4 route
at speed 1 for the SC0000 pre-CHAPTER burst and DEBUGMAP camera/unit workload. Accept P3 only if the original
`SC0000@0x123de` severe band reaches less than 16.67 ms p95 and manual viewing finds no texture, ordering,
movie, text, fade-cadence, or dungeon-sprite regression. Then take P4's screen/range-transition GPU path and
backend-default decision.

### P3 capture 10 - SC0000 GPU target and P4 range-transition trigger (2026-07-22)

`build/perf/run-20260722-145338-127.csv` covers the natural SYSTEM4 route through the CHAPTER movie and
following text. The user reports the original burst is “much, much smoother.” The log confirms that the GPU
path removes the pixel-throughput wall: 199 matched `SC0000@0x123de` rows with at least 3.5 million candidate
pixels have main-thread p50/p95/p99/max 0.068/0.117/0.119/0.124 ms and recomposition
0.048/0.083/0.085/0.093 ms, versus the P2 software baseline's 65.15/71.74 ms p50/p95. All of those frames
arrive at the capture's 10.0 ms presentation cadence. Across 3,048 GPU recompositions, p95/p99/max is
3.23/4.43/13.14 ms; static reuse avoids texture uploads, while dynamic/movie publication remains bounded.

The mixed hotspot still contained 67 intentional software fallbacks (2.2% of recompositions), all for one
type-0 retained range-transition object rather than a legacy whole-screen transition. Fifty-one ordinary
eight-layer rows cost about 17-18 ms, twelve ten-layer affine/additive rows cost about 53-55 ms, and the final
four twelve-layer rows reached 64-66 ms. These sparse fallbacks dominate the aggregate p95 even though the
ordinary GPU frames are far below budget, so they trigger the narrow P4 range-transition slice.

### P4 type-0 retained range transition moved to GPU; recapture pending (2026-07-22)

The GPU compositor now handles the exact existing software-oracle rule: range A remains in normal retained
order, and when the transition placeholder is visited, range B is republished there with the sampled
transition progress multiplying each source object's opacity. Texture/fill resolution, affine transforms,
color key, tint, additive blend, dynamic-surface identity, and common perf counters use the same GPU paths as
ordinary objects. Whole-screen host transitions and additive LERP-tint remain bounded software fallbacks.

A bootstrapped SC0000 page-1 capture exercises the GPU range path and matches the saved software reference:
the complete 800x600 frame differs only by at most one RGB value, with 46,468 changed pixels confined to the
soft y=376..449 fade and 121 independently animated chrome pixels. The Godot build has zero warnings/errors,
the capture log has no runtime warning/error, threaded `SELFTEST OK`, and `git diff --check` passes.
**Next:** repeat the natural SC0000 pre-CHAPTER run once to prove `render_backend=0` disappears from this
coordinate and the mixed severe-band p95 is below 16.67 ms, then run the DEBUGMAP camera/unit acceptance case.

### P4 capture 11 - SC0000 range-transition closeout passes (2026-07-22)

`build/perf/run-20260722-150316-085.csv` repeats the natural SYSTEM4-to-CHAPTER route after the GPU range
implementation. All 3,111 recompositions use `render_backend=1`; there are zero software fallbacks anywhere
in the run. Across the original `SC0000@0x123de` severe band (215 rows with at least 3.5 million candidate
pixels), main-thread p50/p95/p99/max is 0.075/0.132/0.175/0.562 ms and recomposition is
0.051/0.091/0.124/0.533 ms. Every severe frame arrives in 10.0-10.42 ms and none exceeds the 16.67 ms
budget. The whole run's recomposition p95/p99/max is 3.10/3.57/9.03 ms; dynamic/movie upload p95 is
0.192 ms and movie sampling p95 is 0.005 ms.

This closes the original SC0000 performance defect: equivalent severe software frames were
65.15/71.74 ms p50/p95 after P2 and are now 0.075/0.132 ms on the GPU submission path, with the user's
manual run already reporting the burst as much smoother. The P4 range-transition fallback is accepted for
SC0000. **Next:** run DEBUGMAP with the established pan/idle/unit-group/off-map sequence. If its sprites,
camera, pulse effects, and frame delivery remain correct, accept P3 as the retained backend and decide the
small remaining whole-screen fallback/default-switch cleanup.

### P3 capture 12 - DEBUGMAP passes; performance effort accepted (2026-07-22)

`build/perf/run-20260722-152956-766.csv` follows the established slight-pan/idle/unit-group/pan/off-map
sequence. The user reports the result looks good visually. All 6,247 recompositions use the GPU, including
5,496 FIELD frames; there are zero software fallbacks. FIELD averages 962 visited objects, 961 actual draw
items, 51 time-varying objects, 434 integer layers, and 525 axis-aligned-scale layers per composition.

In the matched 2.2-2.5 million candidate-pixel band, main-thread p50/p95/p99 falls from the final software
baseline's 13.90/15.86/18.89 ms to 2.14/2.33/2.78 ms; recomposition is 2.03/2.19/2.66 ms. The >=2.5M band
remains 2.14/2.90/5.40 ms main-thread p50/p95/p99 with a 10.99 ms max. Off-map frames with no visible draws
fall to about 0.02 ms. The normal FIELD path allocates only a few KB of diagnostic/accounting state and the
matched band performs no collections.

One ordinary 961-item FIELD frame measured 22.04 ms without texture uploads, allocation growth, snapshot,
or resource-resolution cost; adjacent frames immediately returned to the normal distribution. It is one
scheduler/driver-like outlier among 5,496 FIELD recompositions, while FIELD p99 is 2.79 ms, so it does not
represent a recurring retained-renderer bottleneck. A separate first-capacity growth row uploads seven
textures, allocates 3.06 MB, and still completes in 10.99 ms.

**Acceptance:** P3/P4 close successfully. GPU retained rendering is now the ordinary backend; the software
compositor remains available via `--render-backend software`, owns high-volume decision diagnostics, and is
the bounded fallback for legacy whole-screen host transitions or an unobserved additive-LERP combination.
`run-godot.cmd` no longer forces `-PerfLog` or a renderer switch, fulfilling the temporary-launch cleanup;
`run-godot.ps1 -PerfLog` remains available for future targeted captures. The original SC0000 burst and
DEBUGMAP animated-unit workload are both visually accepted and comfortably within frame budget.

## Data-semantics sidebar: MAINIT/MAMES and message infrastructure (2026-07-23)

The current static-decoding slice closes the three remaining small MES-named targets. `extract_init.py`
now models MAINIT as eleven one-based magic/research/growth actions in a reserved 30-cell layout and joins
nine untitled MAMES descriptions; growth ritual ids 10 and 11 are explicitly unmatched. INFOMES is
classified as a text-free 32-by-4, first-handler-wins INFO extension registry initialized with
CIMES/EIMES/VIMES. MES is classified as the shared non-selecting modal renderer over caller-populated
line and optional annotation buffers. Their stable globals are curated in `vm-map/globals.toml`, and
structural regressions protect the registry initialization, indirect dispatch, buffer reads, and cleanup.

**Next:** return to reader-proven 2D tables, beginning with RECOVER's 30-wide status/recovery structures.

## Data-semantics sidebar: ILINIT condition matrix and RECOVER ABI (2026-07-23)

ILINIT is now a dedicated thirteen-record condition-definition schema inside the engine's reserved
30-condition by five-level layout. The generated JSON preserves every one of its 228 integer writes and
61 authored level-name strings, while adding a nested level view over duration, eleven-stat deltas, and
HP/SP/FS deltas. Condition ids 1..11, 13, and 14 are defined; id 12 and ids 15..29 remain explicit reserved
slots. Scalar metadata now identifies the effectiveness element, boss-immunity override, RECOVER policy,
and display icon for each condition. All raw address/stride/column keys remain beside their canonical
semantic joins.

The runtime side is one coherent four-table ABI. `entity_condition_levels[50][30]` holds current levels,
`entity_condition_baseline_levels[50][30]` is the equipment/passive minimum, and
`entity_condition_remaining_turns[50][30]` uses -1 for permanent/baseline, zero for inactive, and positive
turns for expiring effects. RECOVER first copies max HP/SP/FS from effective-stat columns 11..13 to the
three current resources. It then scans all 30 condition ids; for the six ILINIT-policy conditions
(charm, confusion, paralysis, poison, water-flow, and fear), it restores the current level to the passive
baseline and writes -1 when that baseline remains or zero when fully cleared. The extractor validates
those table bases, strides, loop bounds, and post-recovery CALCREVISE/DRAWCHP calls before publishing the
join.

Validation covers every ILINIT write, representative stat/resource matrices, the complete reserved id
space, level-aware semantic projection, the resource restore mapping, and the four-table reset protocol.
The global registry and generated reference now carry the condition id columns and all definition/runtime
table meanings.

**Next:** profile CNINIT as the next unresolved high-density name-mode table. Its current generic output
has 32 records but 242 address-derived fields, making it the clearest candidate for another dedicated
schema driven by reader-proven row/column ownership.

## Data-semantics sidebar: CNINIT unit names and voice families (2026-07-23)

CNINIT's former 32-record/242-field output was an ownership error, not a complex record. It is exactly two
parallel sparse arrays with a reserved span of 1,000, both keyed by EBINIT unit id. The string array holds
story/display names used throughout scene scripts, HISTORY, and DEBUGADV; INPUTNAME also scans it when
rejecting a player-entered familiar name that collides with an existing name. The integer array maps a unit
variant to a representative voice-family unit id before the shared voice-suppression lookup. All 277
integer ids join exactly to EBINIT, 274 carry authored names, and Lily's form ids 2..4 are the three
intentional unnamed rows.

The dedicated schema now emits 277 records with one raw string field and one raw integer field, their
canonical semantic names, the source unit's EBINIT definition name, the representative voice-family
definition name, and an explicit alias flag. Of the 277 rows, 175 normalize to another unit id; this
captures brainwashed, concealed-name, allied, boss, EX, corrupted, and trial variants without conflating
their story label with EBINIT's authoring label. Every CNINIT instruction is structurally accounted for,
and the generated profile has collapsed from 242 pseudo-fields to the two true consumer-visible arrays.

**Next:** audit CGINIT's large numeric output. Determine whether its apparent 1,304-field surface is a
legitimate gallery/resource matrix or another ownership artifact before assigning any new semantics.

## Data-semantics sidebar: CGINIT gallery registry (2026-07-23)

CGINIT's former 379-record/1,304-field numeric output was an ownership error. The script contains 3,941
static writes forming 851 sparse gallery-image rows, ids 1..855 with four gaps, inside a reserved
2,000-row layout. Every row has a full-size gallery image, a thumbnail sheet id, a thumbnail slot id,
and a variant ordinal; 537 rows additionally carry the optional preview used by SAVE and SELSTAGE.
The dedicated schema classifies every write and reduces the generated profile to these five real fields.

CGMODE proves the presentation model. INIT2 configures four sheets with `SO026A.AGF` through
`SO026D.AGF`; each resource is a 6-by-5 atlas of thirty 126-by-95 thumbnail cells. CGMODE compacts the
enabled sheets, groups gallery records by their one-based sheet and slot, and orders the full-size images
behind a slot by the one-based variant ordinal (1..23 in shipped data). It tests each primary image's
unlock state, reports unlocked and total variants per thumbnail, and displays the selected full-size
asset. SAVE and SELSTAGE independently scan the same primary image column and use the optional second
asset when it is a 112-by-84 preview, otherwise falling back to a captured frame.

The generated rows join all primary, preview, and sheet asset ids to SYS4INI-derived filenames while
retaining the raw `0x62cd1/2/{0,1}`, `0x63c71`, `0x64441`, and `0x64c11` keys. The curated global
registry names those arrays plus CGMODE's ten-cell configured sheet-resource vector. Regressions cover
the complete write accounting, sparse id range, four atlases, optional previews, representative image
joins, and raw-to-semantic field projection.

## Data-semantics sidebar: ALINIT alchemy recipe registry (2026-07-23)

ALINIT's former generic output—18 records and 853 address-derived fields—was another ownership error.
Its 914 writes form seven parallel structures indexed by sparse recipe id: three 1,000-cell arrays for
output item, minimum alchemy level, and point cost; two `1000 × 2` required/forbidden story-flag tables;
and two `1000 × 4` ingredient-item/quantity tables. There are 107 populated recipe ids from 1 through
467. The dedicated extractor classifies every write and exposes only the thirteen actually populated
columns.

ALCHEMY establishes the complete consumer contract. A nonzero output item marks a populated recipe.
Availability requires the current alchemy level, required and forbidden story flags, a point-capacity
condition, and sufficient quantities for each of four possible ingredient slots. Synthesis consumes
the paired ingredient quantities, adds one output item, deducts the recipe cost from the shared
spendable-point balance, and advances alchemy-level progress. All 107 output ids and all 286 ingredient
references resolve through ITINIT.

The generated records retain the raw addresses, add output and ingredient names plus a compact nested
ingredient view, and project all seven table bases through `vm-map/globals.toml`. The same semantic map
now names the shared spendable-point balance, alchemy level, and alchemy-level progress. Regressions
protect the full write accounting, sparse layout, item joins, story gates, paired ingredient cells, and
raw-to-semantic projection.

## Data-semantics sidebar: AFINIT affinity/tuning tables and CTINIT name palette (2026-07-23)

AFINIT and CTINIT were not malformed record tables; both were vocabulary/table initializers that the
generic name heuristic could not segment. AFINIT contains 27 Japanese element labels and 54
length-prefixed integer rows. Its affinity section is a reserved `20 × 20` defense-by-attack matrix
with thirteen authored defense rows and eighteen authored columns. The dedicated extractor converts
the footer's unsigned representations back to signed values, preserving the `-100` immunity cases,
and joins rows 1..12 to the physical/universal/elemental/divinity/demon/spirit/undead defense names.

AFINIT's remaining rows are two paired `20 × 11` item-tuning tables and one `3 × 7` facility
progression table. Curve ids 1..18 provide ten tuning-level stat bonuses and matching point costs;
row 19 is explicitly zero/reserved and the eleventh column remains reserved. TUNE, IMPROVE, DRAWTIP,
and CALCREVISE establish the bonus/cost contract. The three facility rows belong to equipment tuning,
alchemy, and magic/research respectively, each with six thresholds leading to the level-6 cap. The
registry now also names the equipment-tuning and magic facility level/progress counters.

CTINIT is INPUTNAME's complete `5 × 70` character palette. Its rows are hiragana, katakana, full-width
Latin letters, four numeral styles, and symbols; their populated counts are 56, 56, 52, 40, and 69,
for 273 authored cells total. INPUTNAME uses cursor slots 70..74 to select a page, rejects an empty
cell, and copies a selected character into its seven-character name buffer. The generated JSON keeps
all 350 positions so layout gaps remain explicit rather than collapsing into a flat character list.

Regressions account for every instruction in both scripts, signed affinity values, all tuning and
facility curves, representative characters and gaps, and raw-to-semantic projection.

## Data-semantics sidebar: CVINIT character-voice configuration (2026-07-23)

CVINIT's generic thirteen-row shape was close but incomplete: it grouped 35 of the script's 37 writes
and presented a twelve-entry inverse map as eight unrelated address-derived fields. The dedicated
schema accounts for all 38 instructions (37 static writes plus `exit`) as three coordinated arrays:
thirteen CONFIG preview-voice assets at `0x62cad`, twelve setting-slot-to-EBINIT-unit ids at
`0x62c8f + slot`, and twelve inverse unit-to-setting ids at `0x628a7 + unit`.

CONFIG proves the presentation contract. Slot 0 is a non-unit system voice preview; slots 1..12 map
to Lily, Sylphine, Sasune, Vidal, Estelle, Nelly, Tiofania, Colette, Bridget, Octavia, Fam, and
Deirdre. CONFIG resolves those unit ids through the shared story-display-name table, reloads each
unit's persisted speaker-seen flag to decide whether the row is available, plays the slot's preview
OGG, and edits the matching cell in the thirteen-entry `character_voice_suppressed` settings array.
All thirteen preview ids resolve to shipped audio assets.

The inverse map closes the runtime voice-filter chain rather than duplicating the CONFIG row map.
Story, history, field, and battle paths first normalize the active EBINIT unit through CNINIT's
voice-family unit table, then map that representative unit through CVINIT's inverse table and test
the selected suppression cell. The twelve forward and inverse entries round-trip exactly. Scene
scripts independently set and persist the unit-keyed speaker-seen flags when each speaker first
appears, explaining CONFIG's unlock gate.

Regressions protect complete instruction accounting, all asset and EBINIT joins, the system slot,
all twelve round trips, speaker-seen addresses, and raw-to-semantic projection.

## Data-semantics sidebar: MPINIT stage-terrain atlas (2026-07-23)

MPINIT's 1,472 footer copies are not independent map records. Every destination is column 1 of one
row in a single sparse table rooted at `0xccc93`: the row pitch is 53 cells, while each footer owns
the fifty authored columns 1..50. Destination arithmetic recovers grid Y directly, from the first
authored row at Y=2 through the last at Y=1600; 127 all-zero rows inside that range are omitted.
All 1,473 instructions are now classified as terrain-row copies plus `exit`.

FIELD proves the coordinate contract. STINIT2 supplies inclusive tile bounds in four 1,000-cell
arrays; FIELD multiplies each bound by two, clears a reserved `2000 × 53` current-stage grid at
`0x341ab`, and copies the selected rectangle from the immutable MPINIT atlas. DRAWMAP, CALCOCC,
MVSEEK, battle, occupancy, and movement providers consume the mutable grid. DRAWMINIMAP reads the
mutable grid inside the active rectangle but falls back to the source atlas outside it for border
context, while RESETLAND restores changed cells from the atlas.

The dedicated schema joins 66 named STINIT2 stage definitions to 53 unique atlas rectangles. Eight
rectangles are intentionally shared by 21 stage variants, including the chapter-3 three-route map
and repeated late-game arenas. The joined rectangles contain 17,079 of the atlas's 17,126 nonzero
cells; the remaining 47 are preserved as border-context provenance rather than mislabeled as dead
data. Every stage exposes tile/grid bounds, dimensions, terrain rows, and value populations.

LAINIT supplies the terrain vocabulary and the three fields needed to interpret the atlas:
terrain-to-texture-slot indices, room/area fill flags, and layout classes
(`0=blocked/boundary`, `1=open area`, `2=passage`, `3=hidden`). MPINIT uses ids 0..19; all named
ids join to passages, rooms, hidden spaces, bases, water, openings, altars, lava, and themed room
variants. The generated registry retains every raw row address/footer offset, the omitted-zero-row
list, all stage slices, LAINIT joins, and the FIELD/DRAWMINIMAP/RESETLAND consumer contract.

Regressions protect complete instruction accounting, row geometry, doubled coordinates, all 66
stage joins, shared rectangles, terrain definitions, and the exact nonzero/border-cell totals.

**Next:** finish LAINIT's remaining small terrain-definition surface by its FIELD/MVSEEK/DRAWMAP
consumers; MPINIT already proves its name, texture-slot, area-fill, and layout-class columns.

## Data-semantics sidebar: LAINIT terrain definitions (2026-07-23)

LAINIT is now a dedicated twenty-terrain definition registry inside a reserved thirty-row layout.
Its 98 instructions divide exactly into seventeen terrain names, five player-facing effect
descriptions, 75 numeric writes, and `exit`. Terrain ids 0, 5, and 6 are meaningful implicit-default
rows used by MPINIT; the other shipped ids 1..19 are authored sparsely. The schema retains raw
addresses while projecting all defaults explicitly.

The remaining row-major table at `0xe6afe` is a `30 × 10` signed combat-stat matrix with the same
column ABI as SKINIT's combat deltas: accuracy, evasion, physical attack/defense, magic
attack/defense, speed, luck, critical chance, and capture power. CALCBTPARAM reads the acting unit's
terrain from the doubled-coordinate current map and adds the selected row to its battle parameters.
INFOAF displays the rows. All thirteen populated cells agree exactly with LAINIT's five summaries:
the base raises accuracy/evasion/defenses, water and shallows lower accuracy/evasion/speed, the altar
raises accuracy/evasion, and the hall raises evasion.

The sparse array at `0xe6c2a` is a required traversal/reveal skill id. MVSEEK and FIELD reject
ordinary terrain unless the moving unit owns the referenced SKINIT skill; the dedicated hidden
terrain path enforces the same exploration requirement for hidden passages and rooms. All five
requirements resolve: flight for the open shaft, diving for the underground lake, exploration for
both hidden terrain types, and heat resistance for lava.

The final array at `0xe6c48` is indexed by the twenty stage texture slots rather than terrain id.
When STINIT's per-stage texture override is positive, FIELD loads it directly; zero disables the
slot; `-1` selects LAINIT's shared fallback. All ten authored fallbacks resolve through SYS4INI to
`MP000A/B/C/E/H/I/J/K/L/N.AGF`, and the terrain-to-slot table joins each terrain definition to the
applicable default.

Regressions protect all 98 instructions, the 20-of-30 row contract, every sparse string and numeric
population, all thirteen combat-stat cells, all five SKINIT skill joins, and all ten texture assets.

**Next:** audit SPINIT's 118 populated cells as the stride-15 HMODE resource matrix; the generic
numeric view currently fragments its eight logical rows into individual address-derived records.

## Data-semantics sidebar: SPINIT H-scene gallery (2026-07-23)

SPINIT's 118 numeric writes are a single `8 × 15` row-major registry at `0x6638b`, not 118
independent records. Each row is one HMODE gallery page and each column is one thumbnail slot. The
first seven rows are full; the eighth authors slots 0..12 and leaves slots 13..14 as meaningful
zero/default cells. The dedicated schema classifies all 119 instructions including `exit` and
preserves both complete rows and authored raw-cell provenance.

INIT2 supplies the parallel eight-page thumbnail-sheet registry at `0x66421`. All eight assets
resolve through SYS4INI as `SO027A.AGF` through `SO027H.AGF`; visual conversion confirms each is a
fifteen-thumbnail page matching SPINIT's row geometry. Every one of SPINIT's 118 nonzero values
resolves to an `SP*.BIN` script resource.

HMODE compacts the configured thumbnail sheets into its visible page list, iterates all fifteen
SPINIT slots for each page, passes every populated resource through still-unnamed opcode `0x19d`,
and marks rejected cells unavailable. Selecting an available thumbnail retrieves the same matrix
cell and executes it with indirect `call-script`. This proves the page, slot, resource, and dispatch
semantics without inventing a stronger name for opcode `0x19d`.

Regressions protect the 8-by-15 geometry, 118/120 population, exact two-cell gap, all eight INIT2
thumbnail joins, all 118 SYS4INI script joins, and complete instruction accounting.

**Next:** audit TRINIT's 365-instruction training-action registry by its direct consumers; it is the
next compact mixed string/numeric INIT surface.

## Data-semantics sidebar: TRINIT training actions (2026-07-23)

TRINIT is TRAIN's complete 21-row training/sexual-magic action registry. Its 75 string writes form
six slots per row at `0x453b`: three available-description/cost/reward lines followed by three
locked-condition hint lines. The numeric storage is one contiguous block from `0x155bbc` through
`0x1560e6`, divided into nineteen parallel or row-major families. The dedicated schema classifies
all 365 instructions exactly: 75 strings, 289 static integer writes, and `exit`.

TRAIN proves the eligibility half of the schema. Each action can require up to three story flags,
minimum/maximum unit level, signed familiar alignment thresholds encoded with a +100 bias,
minimum/maximum training progress, ten minimum/maximum combat stats, one inventory item, and one
acquired skill. The shipped data uses thirteen story-flag cells, one minimum-level gate, five
minimum and five maximum alignment gates, fourteen minimum-progress gates, and eight required
items; the forbidden-flag, maximum-level, maximum-progress, stat-bound, and required-skill families
are reserved but empty. All item gates resolve to ITINIT.

The effect half stores a negative 精気 delta, fourteen unit-stat deltas, signed alignment and
training-progress changes in hundredths, optional skill/item awards, and ten event ids. TRAIN checks
and deducts the spirit cost, applies the unit-stat growth ABI, carries fractional alignment/progress,
and grants the selected reward. All eight skill awards resolve to SKINIT and all three item awards
resolve to ITINIT. The new global names also identify current/maximum spirit, familiar alignment and
its fraction, training progress and its fraction, total execution count, and the 21 per-action counts.

The 75 populated event cells resolve through SCINIT to `SC0800`, `SC0810`, `SC0820`, `SC0830`,
`SC0840`, or `SC0850` paths. TRAIN indexes the row by the prior per-action execution count, copies
the selected value to the SCJUMP decision output, increments the count, and treats the first zero as
the execution cap. GAMESTART independently confirms the dual story-flag role: after restoring each
saved count it marks every preceding event id complete. Repeated ids intentionally reuse a scene.

Regressions protect the complete write accounting, all text/gate/effect populations, representative
signed thresholds and stat rows, every ITINIT/SKINIT join, all 75 SCINIT joins, and the observed
one/three/four/six-execution limit distribution.

## Data-semantics sidebar: CDINIT card-generation lists (2026-07-23)

CDINIT is not a 1,149-row numeric table. It is a nine-way dispatch on
`current_card_generation_list_id` (`0x152485`) that repopulates two parallel runtime buffers:
100 card-id slots at `0x1525b2` and 100 three-column weight rows at `0x152486`. The dedicated
extractor classifies all 1,565 instructions exactly: a three-instruction clear prelude, nine
selector tests and branches, 383 four-write candidate entries, nine terminal jumps, the missing-list
comment/marker, and `exit`. The selector ids are 1, 11, 31, 41, 55, 61, 71, 94, and 160, containing
11, 36, 38, 51, 26, 52, 64, 30, and 75 entries respectively.

FIELD supplies the selector from the active STINIT type-28 card object's primary payload. Seven
selectors are referenced by 246 such objects across 50 stage definitions; selectors 55 and 94 are
authored but unreferenced by the shipped STINIT corpus. All 383 candidate references resolve to the
81 CDINIT2 card definitions, including their display names and result text.

The three per-entry values are `base_weight`, `growth_interval_turns`, and `growth_weight`. For every
card that passes its CDINIT2 story-flag gates, FIELD computes
`base_weight + floor(current_stage_turn / growth_interval_turns) * growth_weight`; a zero interval
would select the fixed base path, although all shipped entries use a nonzero interval. It then chooses
one card by cumulative weighted random selection. FIELD tests only required-flag columns zero and one:
CDINIT2 authors a third required flag on eighteen cards, producing 54 occurrences across the generation
lists, but that column is engine-dead in the shipped eligibility loop and remains separately labeled.

Two runtime geometry facts are intentionally not normalized away. FIELD scans 100 candidate slots,
while CDINIT's prelude clears only the first 50 card ids and first 50 weight rows. Selector 160 then
authors 75 slots. The extracted schema records the scan capacity, clear prefix, and complete authored
extent so a reimplementation can reproduce or deliberately resolve that native stale-tail risk.

Regressions protect every selector and entry count, exact instruction accounting, CDINIT2 names and
effective/ignored flag joins, STINIT object joins, raw parallel addresses, the 100/50 scan-clear
asymmetry, and the original missing-list warning.

## Data-semantics sidebar: CDINIT2 card definitions and effects (2026-07-23)

CDINIT2 is FIELD's complete 81-card definition registry inside one reserved 100-row block. The
dedicated extractor classifies all 558 instructions exactly: 162 name/result-message strings,
395 numeric writes, and `exit`. Its numeric block is contiguous from `0x1519f9` through `0x152484`
and preserves the raw arrays for card type, required and forbidden story flags, awarded item,
event story flag, stage-clear point bonus, ranged HP/SP/FS recovery and damage, spirit recovery,
condition id/level, and visual asset.

The shipped type distribution is forty story events, twenty-four item awards, six resource
recoveries, three stage-clear point bonuses, seven traps, and one random warp. FIELD filters each
candidate through two required and three forbidden story-flag cells before selection. CDINIT2 also
authors eighteen values in a third required-flag column, but FIELD's eligibility loop never reads
that column; these remain explicit as engine-dead authoring data rather than being promoted into the
effective gate. All 81 visual ids resolve through SYS4INI to `MVS*.AGF` assets, all 24 item rewards
join through ITINIT, all 40 event ids join through SCINIT, and all three condition-bearing traps join
through ILINIT.

After selection, FIELD renders the card's visual and result message, then applies the type-specific
effect. Resource and spirit ranges use an authored minimum plus `random(maximum - minimum)` when the
maximum exceeds the minimum, so the stored maximum is exclusive for a non-fixed range and equality
means a fixed value. Resource recovery and trap damage address HP, SP, and FS independently; spirit
recovery clamps the current value into its valid range; condition traps call ADDILL with the authored
condition level; and the random-warp card chooses a valid destination tile. Story-event cards dispatch
their joined SCINIT event after the result message. Item cards call ADDITEM. Point cards accumulate
their award in `stage_card_spendable_point_bonus`; STAGECLEAR adds that accumulator to the stage award
before updating `shared_spendable_points`.

Representative definitions protect each path: card 1 restores HP/SP/FS by 2--4, card 4 restores
spirit by 2--4, card 7 awards five stage-clear points, card 10 dispatches SC0490, card 50 performs
ranged resource damage, card 52 applies level-one paralysis, card 55 inflicts fixed ten SP damage,
card 57 is the random warp, card 58 awards a bronze coin, and card 81 renders `MVS107.AGF`.
Regressions protect every string and numeric write, the 81-of-100 geometry, type distribution,
effective/ignored gate split, all four external joins, representative effect projections, and the
FIELD/STAGECLEAR consumer contract.

**Next:** audit BTANINIT and BTANINIT2 together. Their current generic outputs fragment the
battle-animation registry across address-derived fields, making the consumer-side row geometry the
next data-structure target.

## Data-semantics sidebar: BTANINIT/BTANINIT2 battle animations (2026-07-23)

BTANINIT2 is three parallel reserved 1,000-row arrays, not 28 records with 387 unrelated fields.
Its 1,017 writes define 122 sparse battle-animation ids: six effect slots at `0x15288e`, six paired
start-delay slots at `0x153ffe`, and one total-duration cell at `0x15576e`. The dedicated extractor
classifies all 1,018 instructions exactly and resolves all 573 populated effect references across
186 distinct BTANINIT definitions.

The shipped slot geometry is deliberate. Slots zero and one have 114 effect references each and
are actor-side when an effect requests combatant anchoring; slots two and three have 113 each and
are target-side; slot five has 119; slot four is never populated. Only slots two, three, and five
carry authored start delays. One hundred eleven rows are complete attack timelines with a
500--1,700 ms total duration. Eleven are auxiliary rows: two incomplete/unjoined authored attacks
and passive/defeat rows 801--809, which are invoked for isolated overlays and intentionally omit the
main duration.

CALCDMG selects rows 1--21 from the equipped item's `item_weapon_class` for normal attacks or reads
`skill_battle_animation_id` for skills. The join accounts for 101 populated skill references across
98 animation rows, including passive reactions 801--808. BTL selects hardcoded row 809 for defeat.
Four authored animation rows—205, 221, 222, and 224—have no shipped item, skill, or hardcoded
consumer and remain explicit rather than being discarded.

BTANINIT is the paired effect catalog and runtime materializer. It clears fourteen six-cell work
arrays plus one `6 × 3` hit-pulse table, reads the selected BTANINIT2 row, and dispatches each slot's
effect id through 203 cases: zero as the empty sentinel and 202 populated definitions. The dedicated
extractor classifies all 4,472 instructions, including 1,917 definition writes. The catalog contains
186 nonblocking MPEG-in-AGF movie effects, one green-colorkey sprite sheet, and fifteen opaque sprite
sheets. All 202 visual resources resolve to `MVB*.AGF` or `AM*.AGF`; all 199 populated sound ids
resolve to WAV files.

BTL proves the complete materialized work ABI: playback mode, additive blend, destination width and
height, actor/target or battlefield-center anchor, pixel offsets, sprite atlas columns/frame count
and finite duration, delayed sound, and up to three hit-pulse offsets. Shipped data populates only
the first hit-pulse column, always at 100 ms, on 26 effects. The sixteen authored sprite-atlas row
counts agree with the explicit frame/column values, but BTL never reads them. Sixteen complete
BTANINIT effect definitions are unreferenced by BTANINIT2 and remain preserved.

Regressions protect both scripts' complete instruction accounting, reserved geometry, every
effect/delay/duration population, all 573 definition joins, the SKINIT and ITINIT selections,
passive/defeat rows, visual/audio resolution, movie/sprite behavior, hit-pulse timing, and the
unreferenced authored surfaces.

**Next:** audit STINIT2's remaining generic 321-row stage-definition surface. MPINIT already proves
its tile-bound columns; the next pass should join the rest of its stage metadata to FIELD and the
stage-selection/initialization consumers.

## Data-semantics sidebar: STINIT2 stage definitions (2026-07-23)

STINIT2's former generic 321-record view was structurally wrong: 247 of those “records” were
description strings. The dedicated schema classifies all 1,634 instructions into 74 sparse stage
names, 296 strings in a reserved `1000 × 6` description matrix, 1,263 numeric writes, and one exit.
SELSTAGE indexes the text matrix as three uncleared lines followed by three cleared lines.

The 74 real rows divide into 66 mapped stages and eight event-only stages. The schema preserves
numbered, `EVENT`, and `EX` presentation; 49 main-progression flags; the eight EX flags; four
multi-stage unlock groups; and seven-column required/forbidden story-flag rows. FORT proves the
availability checks and descending main-stage auto-selection, while FIELD proves unlock-group
propagation.

Map rows join the four already-proven tile bounds to doubled terrain-atlas coordinates and the
minimap atlas Y origin. Clear rewards expose the performance-scaled base spendable-point award and
the three item-quantity columns for bronze, silver, and gold coins. Every populated entry, clear,
and failure transition resolves through SCINIT (174/174), and every stage-loader resource resolves
to the shared `STINIT.BIN` selector (74/74).

Regressions protect the corrected record geometry, complete instruction accounting, all six text
slots, display kinds, story gates, unlock groups, map/minimap coordinates, rewards, SCJUMP joins,
and loader joins. The 66 populated values at `0xedc4d` have no script consumer. Their ordinal 1..8
distribution tracks authored challenge/reward progression: late story maps are tier 6, EX maps use
tiers 5 and 7, and the final EX map alone is tier 8. Because the native engine can reach VM globals
only through script operands, the array is now named `stage_authoring_difficulty_tiers` at medium
confidence and explicitly classified as engine-dead authoring metadata.

**Next:** continue consumer-led naming of the remaining two-dimensional tables; STINIT2 is closed.

## Data-semantics sidebar: ADV layer surface-slot registry (2026-07-23)

The highest-use unnamed row table, `G[0x3239]`, is now closed as
`adv_layer_surface_slots`. Its eight rows contain three graphics surface banks:
`primary_surface_slot` 4..11, `alternate_surface_slot` 43..50, and
`transition_surface_slot` 51..58. The corpus contains 2,657 table-base accesses across 309 scripts,
and 143 scene scripts repeat the same initializer.

The shared CG loader proves the ABI. `adv_gfx_layer_index` selects a row and the corresponding
`adv_gfx_object_handles` cell. A fresh object loads into the primary slot; an already-bound layer
swaps between the primary and alternate slots; the third slot stages and cleans up transitions.
`adv_gfx_surface_slot_work` carries the op-`0x215` query result or fallback slot, while
`adv_gfx_resource_id` carries the transient SYS4INI resource id into `set-texture`. INIT2 seeds the
first nine cells of the 20-cell retained-object handle array; the first eight participate in the
row registry.

Registry tests protect all 24 initialized slot cells, use of all three columns in SC0000, the nine
INIT2 handle constants, and the canonical global/column names.

**Next:** rank the next unnamed row table by shipped-reader evidence.

## Data-semantics sidebar: selected movement-route grid (2026-07-23)

The highest-use remaining auto-shaped row table, `G[0xc6077]`, is now closed as
`selected_movement_route_steps`. It is a reserved `1000 × 27` row-major map work grid. MVRTN,
FIELD, and SETROUTE clear all 27,000 cells; the complete corpus has 51 two-dimensional lookups and
five clears across 21 scripts.

SETROUTE begins at `map_target_tile_x/y`, copies the matching
`pathfinding_remaining_route_steps` score into the selected-route grid, and follows the cardinal
neighbor whose score is one greater until it reaches the acting entity. This leaves a one-cell-wide
monotone trail from destination back to origin. FIELD uses that trail to draw directional arrows and
execute movement, SELACT tests whether an action target follows movement, and all seventeen providers
RTN_M002..RTN_M018 reject candidate endpoints with a zero route cell.

INIT2's paired five-cell arrays are now named `cardinal_tile_delta_x/y`: the exact values are
`[0, 0, -1, 0, 1]` and `[0, 1, 0, -1, 0]`, preserving the no-move cell plus four-neighbor order.
The target coordinate pair also drives LOOK's scripted battlefield-camera centering and is referenced
by 55 scripts, so its names deliberately describe shared map targets rather than only AI destinations.

Evidence regressions protect the exact corpus xrefs and clear geometry, all seventeen provider
readers, both SETROUTE flood-score copies, the INIT2 cardinal vectors, and every canonical name.

**Next:** investigate the remaining unnamed stride-300 table at `G[0x15261f]`.

## Data-semantics sidebar: battle triggered-passive matrix (2026-07-24)

The former stride-300 auto table at `G[0x15261f]` is the reserved `2 × 300`
`battle_triggered_passive_skill_flags` work matrix. Its first dimension is BTL's two battle sides,
and its second dimension is the SKINIT skill id. The complete corpus surface is compact and exact:
BTL and CALCDMG are its only readers/writers, with 37 two-dimensional lookups and three full
600-cell clears.

CALCDMG rebuilds the matrix for each exchange. A local eligibility surface first assigns the
actor/target roles of shipped passive ids 28..50 (with Seal/id 44 absent from this activation path).
The script then scans the four equipped skills on both battle entities, accepts category-4 passives,
applies the configured `skill_proc_chance_percent`, and filters surviving cells through the concrete
combat contract: species and boss slayers, anti-air/anti-sub movement skills, ordinary-versus-special
attack categories, role-specific defensive/offensive passives, and mutually exclusive reactions.
The result is an activation record for this exchange rather than the entity's persistent skill
ownership; `entity_skill_flags` remains the latter source.

BTL proves the output side. It indexes each participant's four equipped skills through the matrix
to draw the triggered-passive icons, and repeats the dynamic join for actor- and target-side passive
battle animations. Direct column tests then implement Roar, Counter, Reflect, Absorb, Pierce, Shield,
Parry, and Revive behavior. The adjacent `G[0x15261e]` is now
`battle_actor_hp_recovery`: CALCDMG adds Absorb's half-damage return and any level-scaled HP-absorption
condition amount, while BTL adds the result to the actor's HP and renders the green recovery number.

Evidence regressions protect the exact two-script/40-reference surface, all three `2 × 300` clears,
the passive-id eligibility range, category gate and dynamic filters, BTL's three skill-indexed
presentation consumers, and the actor-recovery producer/consumer pair.

**Next:** investigate the highest-use remaining auto-shaped row table, the stride-14 table at
`G[0x6f70]` (27 references across ADDEXP, EVOLVE, GAMECLEAR, GAMESTART, TRAIN, and UNITECH).

## Data-semantics sidebar: persistent unit stat-growth fractions (2026-07-24)

The former stride-14 auto table at `G[0x6f70]` is the `100 x 14`
`unit_stat_growth_fractions` table. It is the fractional half of the persistent playable-unit stat
record: the preceding `unit_current_stats` table has the same 100-row geometry, and both use the
standard accuracy-through-max-FS fourteen-column ABI. The table ends exactly where the following
100-by-4 `unit_skill_ids` block begins.

Three independent growth paths prove the value scale and role. ADDEXP adds the unit definition's
`unit_stat_growth_rates`; TRAIN adds the selected action's
`training_action_stat_growth_hundredths`; and UNITECH multiplies the definition growth rate by its
level catch-up factor. Each path divides the accumulated cell by 100, adds that quotient to the
matching `unit_current_stats` cell, and retains the remainder modulo 100. ADDEXP and TRAIN can also
convert the surviving positive fraction into a probabilistic extra point, while all three paths
clamp the integer result to the shared fourteen-stat caps.

The fractions are durable unit state rather than temporary growth work. GAMESTART serializes each
cell immediately after its matching current-stat cell. GAMECLEAR copies complete fourteen-cell
rows during its unit-record transfer and registers all 100 rows with the shared profile; EVOLVE
copies the same rows between Lily forms. The complete corpus surface is exactly 27 two-dimensional
lookups across these six scripts.

Evidence regressions protect all 27 offsets and their stride, the three source/add/divide/modulo
growth paths, the current-stat join, both save/load sites, both complete-row copy paths, and the
shared-profile registration.

**Next:** investigate the highest-use remaining auto-shaped row table, the stride-10 table at
`G[0x3ebe]` (21 references across CALCREVISE, CHMENU, DRAWTIP, GAMECLEAR, GAMESTART, IMPROVE, and
TUNE).

## SYS4INI startup-settings investigation (2026-07-28)

The post-directory SYS4INI data is now structurally and behaviorally cataloged. Himegari's decompressed
tail contains a `0x90`-byte VM-metadata block followed by a `0x2bb`-byte, 36-pair CP932 startup-settings
record. Native AGE first registers generic defaults, imports those pairs case-insensitively, applies three
special transforms (`SAVEVERSION`, `NOSETMUSIC`, and wheel-key duplication), then initializes the logical
canvas, text, graphics, audio, ADV input policy, persistence, and Windows registration integration from
the completed registry.

The investigation distinguishes active Himegari behavior from loaded compatibility metadata. Active core
results include retained renderer/draw mode, `800x600`, Mincho/antialias profile, movie-audio policy,
save layout 3.10, and AppData save-root selection. The important newly exposed ADV overrides are
`CANCELMESSKIPONCLICK=2`, `CONTROLDISIBLECURSOR=1`, `COEXISTMESSKIP=1`,
`REDRAWTEXTONKEY=0`, and wheel actions 8/9. The port does not currently parse/apply those ADV policy
values; in particular, the earlier click-cancel conclusion accidentally preserved AGE's zero fallback
instead of Himegari's override.

Legacy DirectDraw-only values, unused native-menu flags, and Windows registration/key metadata are now
classified separately so a later generic profile loader does not mistake every serialized setting for a
portable runtime requirement. No gameplay/runtime behavior changed in this investigation. The exact
serialized inventory is canonical in `sys4-format-notes.md`, native consumers and port relevance in
`engine-re.md`, multi-profile direction in `remake-architecture-and-roadmap.md`, and portability impact in
`platform-portability.md`. The `/v2` Ghidra image names/comments the importer and principal graphics,
ADV-toggle, audio, and registration consumers.

**Next:** keep the settings parser/application as a bounded generic-profile/configuration slice. Resume
the Phase-B gameplay spine unless one of the cataloged ADV policy differences becomes a visible gameplay
or input defect.

## SYS4INI logical-resolution implementation plan (planned 2026-07-28)

### Slice boundary

This first settings-application slice makes SYS4INI the runtime source of the game's logical canvas and
uses that same size as the default windowed client size:

```text
SYS4INI SCREENX/SCREENY
    -> effective logical canvas
    -> software backbuffer + retained presentation bounds + primary surface
    -> Godot content-scale base
    -> initial windowed client size
```

AGE's registered `640x480` values remain the per-dimension fallback when a key is absent or cannot
produce a positive integer. A present value that would overflow or exceed the runtime's explicitly
bounded RGBA allocation policy is a profile error with a useful diagnostic, not a request to attempt an
unbounded allocation.

The physical presentation size is deliberately not independently configurable in this slice. Slice two
will add `--window-width` and `--window-height`; their defaults will be the logical canvas size established
here. Those future parameters will change only the physical window, never VM coordinates, surface sizes,
layout, or the software backbuffer.

### Non-goals

- Do not apply the other 34 SYS4INI settings yet.
- Do not reproduce native fullscreen mode switching, `FULLSCREENBIT`, aspect/device selection, or
  DirectDraw behavior.
- Do not add the window-width/window-height boot parameters early.
- Do not turn Himegari-authored `800x600` operands, asset dimensions, or test fixtures into profile
  variables merely because they contain the same numbers.
- Do not add a second SYS4INI decoder or read the generated `build/asset-index.json` at runtime.

### Design

1. **Parse the settings record at the existing catalog boundary.**

   `Sys4AssetCatalog.Parse` already owns the decompressed S4IC blob and ends with `p` immediately after
   the raw file records. For a base S4IC catalog, continue from there through the bounded VM-metadata
   block and settings record documented in `sys4-format-notes.md`. Append S4AC catalogs do not provide
   the selected game's startup profile.

   Add a platform-neutral settings value on the base catalog, tentatively
   `Sys4StartupSettings`. Preserve the ordered CP932 key/value pairs for diagnostics and expose a
   case-insensitive last-value-wins lookup matching AGE's registry assignment behavior. Unknown keys are
   retained rather than rejected by the generic parser so a newer game can be inspected before every
   setting has a runtime consumer.

   The parser must bounds-check both length-prefixed blocks, the pair count, every terminating NUL, and
   exact settings-string consumption. A genuinely absent trailer yields an empty settings set and hence
   AGE defaults; a structurally present but truncated or inconsistent trailer is an
   `InvalidDataException` naming the source catalog and field.

2. **Resolve one immutable logical-canvas value.**

   Add a small engine-owned integer size type or settings projection so Godot does not become the owner
   of SYS4 semantics. Resolve `SCREENX` and `SCREENY` independently from the raw settings, using 640 and
   480 respectively when missing, nonnumeric, zero, or negative. Validate checked pixel count and byte
   count once at this boundary. All downstream systems receive the already-validated size and must not
   reinterpret the strings or invent their own fallback.

3. **Reorder Godot bootstrap around the selected profile.**

   Refactor `Main._Ready()` into the necessary startup phases:

   ```text
   read existing boot arguments
       -> load base catalog once
       -> obtain validated logical canvas
       -> configure root content-scale base and initial window
       -> allocate presentation nodes/buffers
       -> construct persistence, assets, host, and VM from the same catalog
   ```

   Remove the later duplicate normal-path `Sys4AssetCatalog.Load`. Keep `project.godot` dimensions only
   as a harmless pre-script bootstrap/fallback; they are not the selected game's resolution. In windowed
   mode, set the initial client size to the logical canvas. Configure the root viewport's content scale
   to that logical size with aspect preservation so slice two can vary the physical window without
   changing the game coordinate system. A pre-existing fullscreen launch retains the display's physical
   size while using the same logical base.

4. **Replace frontend canvas constants with runtime state.**

   Replace `Main.ScreenWidth`/`ScreenHeight` and the field-initialized pixel array with validated
   `_screenWidth`, `_screenHeight`, and a buffer allocated after profile loading. Carry those dimensions
   through:

   - immediate backbuffer creation/publication;
   - GPU/software compositor destination bounds and performance accounting;
   - whole-canvas fills and transition captures;
   - primary surface slot zero and native-sized movie surfaces;
   - fallback ADV layout bounds;
   - retained-object fallback geometry;
   - pointer-to-logical-coordinate conversion.

   Give `GodotAdvHost` the logical size explicitly rather than letting it duplicate defaults.
   `RetainedSurfaceRasterizer` already receives an `RgbaImage` destination; its null-source whole-canvas
   fill fallback should use `destination.Width/Height`, not a new global.

   Input conversion must have one scaling owner. Once Godot content scaling is active, derive logical
   coordinates through the root viewport/canvas transform (including letterbox exclusion) instead of
   applying the current whole-window `position * 800 / viewport` ratio a second time.

5. **Audit, do not mechanically replace, numeric fixtures.**

   Review every runtime `800`/`600` occurrence. Replace values that mean “current engine canvas” and leave
   values that are:

   - operands authored by Himegari scripts;
   - dimensions of installed Himegari assets;
   - focused regression inputs whose subject is unrelated to profile sizing;
   - opcode constants such as `0x8000`.

   Update comments that currently call input or slot zero specifically “800x600” when they mean the
   selected logical canvas.

### Verification

Engine tests:

- parse the installed SYS4INI and assert all 36 pairs plus `800x600`;
- parse a synthetic bounded trailer with `SCREENX=1024`, `SCREENY=576` and prove the projected canvas;
- prove absent keys fall back independently to `640x480`;
- reject truncated lengths, missing NULs, count/byte mismatches, and unsafe dimensions;
- prove duplicate/case-varied keys use the last value while unknown keys remain inspectable;
- composite a whole-canvas retained fill into a non-800 destination and prove its exact bounds;
- construct host/canvas-facing state with a non-800 size and prove slot zero and fallback ADV layout use it.

Godot gates:

- zero-warning build and threaded `SELFTEST OK`;
- selftest asserts/logs the selected logical canvas, root content-scale base, backbuffer dimensions, and
  default windowed size;
- ordinary Himegari boot reports `SYS4INI 800x600`, opens at 800x600, and reaches the existing SC0000
  presentation with unchanged input coordinates and compositor output;
- a diagnostic non-800 profile/settings fixture reaches presentation allocation without any 800x600
  fallback in buffer size, primary-surface size, compositor bounds, or pointer coordinates.

Static closeout:

- `rg` audit all runtime `800`/`600` occurrences and classify every survivor;
- update `engine-re.md`, `sys4-format-notes.md`, the roadmap/profile boundary, portability inventory,
  this slice's result, and status memory;
- update `PROJECT-STRUCTURE.md` only if the implementation introduces a new settings/profile source
  category, and `tools-reference.md` only if a tool changes.

### Slice-two handoff

After this slice, logical and physical size are equal by default but are represented separately. The next
slice may add:

```text
--window-width <pixels>
--window-height <pixels>
```

Those parameters will override only the physical window dimensions. Godot will scale the unchanged
SYS4INI-sized logical canvas into that window using the aspect-preserving/letterbox policy. Validation of
partial overrides, minimum sizes, fullscreen interaction, integer scaling, and alternate stretch/crop
policies belongs to that second slice's configuration design.

### Result (implemented 2026-07-28)

The first slice is complete. `Sys4AssetCatalog` now consumes the bounded base-S4IC trailer after its
existing directory parse and exposes ordered `Sys4StartupSettings`; lookup is case-insensitive and
last-value-wins while unknown keys remain available for future profiles. `Sys4LogicalCanvas` projects
`SCREENX`/`SCREENY` with independent `640x480` fallbacks and rejects dimensions above the bounded
dimension/pixel allocation policy.

Godot loads the catalog before presentation allocation and reuses that instance for normal script/asset
startup. The selected canvas owns the root content-scale base, initial windowed client request,
backbuffer/pixel allocation, GPU and software destination bounds, whole-screen fills, primary and movie
surface fallbacks, fallback ADV layout, and pointer coordinates. `GodotAdvHost` receives the validated
size explicitly, and the platform-neutral retained-surface fill now derives its zero-size fallback from
the destination image. `project.godot` retains only AGE's generic `640x480` bootstrap fallback. No
runtime production literal `800`/`600` remains; authored Himegari data and unrelated regression fixtures
were left unchanged.

Verification passed with 443 engine tests, including installed 36-pair/`800x600`, synthetic
`1024x576`, independent fallback, duplicate/unknown, malformed/unsafe, and non-800 retained-fill cases.
The Godot C# project builds with zero warnings, and the threaded headless selftest reports
`logical-canvas=800x600`. Its synthetic headless physical window is not a client-size oracle; ordinary
windowed startup directly requests the same validated dimensions. Physical window overrides remain the
second slice described above.

## ADV requested-face selection implemented (2026-07-28)

Opcode `0x1a5` is no longer a presentation no-op. The VM retains its requested face in
`AdvTextStyle`, so live text, retained backlog records, History replay, and surface strings all preserve
the active family. Godot resolves Himegari's two authored requests (`ＭＳ 明朝` and `ＭＳ ゴシック`) to
the corresponding Windows TTC files and caches regular/bold variants independently. If a requested face
is unavailable or unknown, presentation falls back to the existing best-effort Japanese font rather than
dropping the text or inventing a bundled font dependency.

Configurable defaults and per-face substitutions remain deferred to the general runtime configuration
design, especially for Linux/macOS where the proprietary Microsoft faces are not normally installed.
Focused regressions cover face changes flowing into both live and retained styles; the engine tests and
Godot C# build pass. User visual acceptance passed on 2026-07-28.

## ADV text effect modes separated (2026-07-28)

The shared Godot text-style applicator no longer treats every nonzero render mode as an outline. Mode 1
uses the Label shadow layer to draw one effect-color glyph at the configured displacement before the
primary glyph; this includes Himegari's 43 zero-offset uses. Mode 3 uses a symmetric outline sized from
the configured radii, matching the shape of all 164 paired Himegari sites because they uniformly request
`(1,1)`. Mode 0 clears both layers, while native mode 2 remains unused and unimplemented.

The mapping applies uniformly to live ADV runs, retained History batches, and text drawn onto retained
surfaces. A focused VM regression protects mode/color/offset propagation through live and retained style
records, and the Godot self-test applies both modes to a real Label and checks its shadow/outline theme
state. Validation: engine **431/431**, zero-warning Godot build, and threaded `SELFTEST OK` with
`text-effect-modes=ok`. User visual acceptance passed on 2026-07-28.

## ADV font weight and advance calibrated (2026-07-28)

A disposable Windows GDI oracle measured six representative strings and the grayscale `魔` glyph
against Godot's actual `msmincho.ttc`/`msgothic.ttc` presentation. Regular 24px Mincho and 16px Gothic
advances match exactly, so neither face receives speculative horizontal scaling. The former
`VariationEmbolden=1.2` overshot native weight-700 glyph coverage by 44% while remaining up to 9 pixels
narrow across a complete sample string.

The bold variation now uses measured `VariationEmbolden=0.53` plus one pixel of glyph spacing. Its
grayscale coverage is within 0.3% of GDI and the six measured string widths are within 0..3 pixels.
The shared style path also compensates for Godot reporting the two fonts' line boxes one pixel taller
than AGE's explicit requested heights, preserving native total line advance during wrapping. The Godot
self-test checks the calibrated variation and both 24px/16px spacing examples.

Validation: engine **431/431**, zero-warning Godot build, and threaded `SELFTEST OK` with
`font-calibration=ok`. The probes were deliberately disposable; the canonical measurements live in
`docs/engine-re.md` rather than adding a Windows-only runtime/tool dependency. User visual acceptance
was provisional on 2026-07-28. A later same-page native/port comparison retained exact placement and
horizontal bounds but found the port 45% lower in bright-pixel population with much softer edges. The
raw GDI/Godot coverage-sum comparison was therefore not a valid equivalence oracle. Follow-up Ghidra
work established that AGE uses a GDI-synthesized weight-700 `GGO_GRAY4_BITMAP` and its own integer
glyph/outline compositor, whereas the slice uses FreeType embolden and Godot's outline primitive.
The required fidelity follow-up is implementation of that decoded mask contract; screenshots remain
validation evidence, not the source of rendering behavior. That work is deferred until gameplay settles
and is scoped in `docs/remake-architecture-and-roadmap.md`; it is not the next Phase A/B slice.

## Persistence native-format reconnaissance complete (2026-07-24)

The deferred save/profile ownership question now has a compatibility-mode answer. The remaining native
opcode family is mapped: `0x19e` numbered save, unused data-only load `0x19f`, metadata query `0x1a0`,
full load/resume `0x1a1`, shared string cells `0x1a9`/`0x1aa`, pair delete/copy
`0x1ab`/`0x1ac`, resume-frame marker `0x1ad`, and thumbnail write/load `0x1ae`/`0x1af`.
Adjacent `0x19d` is unrelated resource/compatibility logic and is not classified by proximity.

The native persistence domains and their lifecycle are structurally complete enough for implementation:
shared `SAVE.DAT` uses atomic temp/backup replacement and stores both typed integer and string selected-cell
tables; `RT.DAT` remains the independent ReadTextDB file; numbered state uses `SAVE##.DAT` plus a separate
BMP-formatted `SAVE##.STH`. The common `.DAT` codec has a fixed `0x124`-byte S3SD/S4SD header, a mapped
20-byte length/dual-CRC/seed/multiplier frame, reversible per-DWORD expansion, and version-2 4 KiB LZSS.
Numbered logical layouts 1/2/3, active-frame cutoff/restoration, history, global banks, resources, and the
layout-3 retained surface/object state are now bounded in `docs/engine-re.md`.

**Decision:** implement the native binary formats and paired-file behavior for the 1.0 compatibility pass.
Keep them behind a profile/save service so JSON inspection/export, migrations, and mod-owned namespaced state
can arrive later as extended-mode additions. This supersedes the earlier open choice between native storage
and a port-owned replacement; it does not authorize folding profile-selected cells into the existing
whole-bank JSON session snapshot.

No runtime persistence code changed in this reconnaissance slice. The canonical opcode source and generated
references now describe the native ABI; the corresponding `/v2` handlers and codec helpers are named,
commented, and saved.

### Persistence implementation step 1 — native DAT codec and store boundary (2026-07-24)

The reusable compatibility floor is now implemented without prematurely inventing either shared-profile or
numbered-state payload objects. `NativeSaveContainerCodec` owns the exact `0x124` header and `0x14` codec
frame, Shift-JIS game identity, SYSTEMTIME/playtime/version fields, inner and outer CRC pairs, rolling
seed/odd-multiplier DWORD expansion, its exact-division inverse, and the SaveVersion2>=2 wrapper. The new
`LzssEncoder` shares the existing native 4096-byte-ring dialect and uses the native equal-length verbatim
fallback when compression is not beneficial.

`INativeDatStore` is the payload-agnostic runtime seam. Its directory implementation performs shared
`$$SAVE.DAT` → `SAVE.DAT` replacement with `SAVE.BAK` fallback and direct `SAVE##.DAT` reads/writes, while
validating generation, compatibility id, game id, and the native layout-version exception. It does not
serialize the VM's whole global bank, change `GameSession.ToJson`, wire persistence opcodes, or claim
`RT.DAT`/`.STH` support.

Seven focused tests cover independent standard CRC vectors, compressed and verbatim LZSS round trips,
S3SD/S4SD header/frame offsets, deterministic transform products, both codec-version branches, corruption
rejection after outer-CRC repair, layout-2 version compatibility, shared backup recovery, and direct numbered
files. The full engine suite passes 359 tests. The opcode table count regression now distinguishes 248
Himegari-observed opcodes from the mapped-but-unused shared ABI opcode `0x19f`.

**Next persistence step:** implement the typed shared `SAVE.DAT` logical payload and profile-owned selected
integer/string cell maps, then connect `0x1a2`/`0x1a3` and `0x1a9`/`0x1aa`. Add native `RT.DAT` and its
read-message lifecycle after that shared ownership is live; numbered active-frame layouts remain the later,
larger payload.

### Persistence implementation step 2 — shared SAVE.DAT payload and selected cells (2026-07-24)

The typed shared payload and profile ownership are now implemented. `SharedProfilePayloadCodec` reads and
writes the catalog block, fixed integer entries, DWORD-counted CP932 string blob, version-3.10 selector and
extra arrays, and native reserved tail. `SharedProfile` preserves the catalog/extended sections opaquely,
owns selected raw-int32 and string maps, and explicitly loads/saves through `INativeDatStore`.
`GameSession` shares that profile across fresh scene VMs without adding it to the existing whole-bank JSON
diagnostic snapshot.

The original AppData `SAVE.DAT` served as a read-only compatibility oracle and decodes completely:
13,210 catalog values, 24,070 integer cells, 605 string cells, selector 1 = 81, 83 extended values, and
nine reserved tail DWORDs. That check corrected three reconnaissance details before implementation: typed
keys begin with raw bytes `0x03`/`0x05` rather than ASCII digits; the string length is a DWORD count; and
the native writer's explicit terminator plus historical allocation slack leaves nine tail DWORDs.

VM opcodes `0x1a2`/`0x1a3` and `0x1a9`/`0x1aa` now implement insert-or-assign plus native zero/empty
miss defaults. Direct global cells and the shipped local-pointer-to-global idiom are covered, including
fresh-VM continuity through `GameSession`. Five focused shared-profile tests plus the seven container/store
tests cover binary shape, malformed input, native lifecycle, pointer lvalues, miss behavior, and JSON-domain
separation.

**Next persistence step:** add native `RT.DAT` and connect the already-mapped ReadTextDB queue/commit/query
lifecycle. Numbered active-frame saves and `.STH` remain the later, larger payload slice.

### Persistence implementation step 3 — native RT.DAT and ReadTextDB lifecycle (2026-07-24)

The profile-wide read-message domain is now implemented without folding it into shared SAVE.DAT or the
port's JSON session snapshot. `ReadTextDatabaseCodec` reads/writes the native `0x114`-byte S3RT header,
12-byte `{script_id,message_count,serialized_pointer}` table, and ordered DWORD flag arrays. Installed
native RT.DAT validation proves 192 records plus 18,543 flag DWORDs account for all 76,752 bytes; its
SC0000 id `0x22` carries 320 messages, exactly matching that script's F7/T1 table. Native heap-pointer
words are accepted on import and emitted as zero because AGE allocates and overwrites them on load.

`DirectoryNativeDatStore` now performs the native `$$RT.DAT` → `RT.DAT` / `RT.BAK` replacement alongside
the existing shared transaction. `SharedProfile` owns both ReadTextDB records and its pending queue, loads
RT.DAT independently, and writes it after SAVE.DAT. Script parsing retains the packed SYS4/AAI id plus T1
message-boundary offsets; the synthetic assembler emits the same table for behavioral tests.

The VM implements the native seam: wait completion queues the current `{packed_script_id,T1_index,T1_count}`;
the next op `0x71` commits it, and ops `0x6e`/`0x71`/`0x72` refresh previously-read eligibility.
`0x1ca`/`0x1cb` now set/get the profile-lifetime `message:ReadTextSkip` setting and `0x1cc` exposes the
current per-message state. Six focused tests cover exact bytes, malformed files, native pointer tolerance,
the installed native oracle, queue/growth behavior, paired filesystem transactions, and fresh-VM opcode
continuity.

**Next persistence step:** implement the numbered pair/thumbnail/frame-boundary layer, then use that proven
surface to implement the larger layout-3 active-frame/global payload. JSON inspection/export and mod-owned
namespaced state remain extended-mode work.

Validation: all 370 engine tests pass, including the installed read-only RT.DAT round-trip oracle;
opcode generator tests/lint and the Godot C# build are clean. SC0000 is now 127/129 distinct opcodes
handled (98.4%); its only remaining effectful gaps are numbered-save restore boundaries `0x1ad`×6 and
`0xae`×1.

### Persistence implementation step 4 — numbered pair, metadata, thumbnail, and frame boundary (2026-07-24)

The bounded outer numbered-save lifecycle is now implemented before the much larger logical state payload.
Himegari's numbered `.DAT` compatibility id is `0x42323234`, distinct from shared `SAVE.DAT`/`RT.DAT`
id `0x4a343234`; `NativeSaveIdentity` and the directory store now validate those domains independently.
Opcode `0x1a0` reads and validates only the fixed header, while `0x1ab`/`0x1ac` attempt both native
`SAVE%02d.DAT` and `SAVE%02d.STH` members and reproduce DAT-failure/STH-precedence statuses.

`NumberedThumbnailCodec` reproduces the installed native `.STH` dialect: bottom-up 24-bit BGR BMP,
four-byte row padding, 112x84 installed dimensions, and the historical `bfSize` value that omits the
physically present 14-byte file header. Host capture/replacement seams connect this to Godot surfaces;
`0x1ae` and `0x1af` expose the native status contract. Godot redirects the engine-owned save root to
isolated `user://SAVE` while retaining the native names and formats, so it never mutates the original
installation's AppData saves.

Opcode `0x1ad` now marks the current `ExecFrame` by identity. The boundary survives nested calls and clears
when its owning frame unwinds, matching AGE's single context-index marker and providing the exact cutoff
the next serializer slice needs. It is no longer counted as a no-op, but this step deliberately does not
claim load-compatible numbered `.DAT` bodies: layout-3's fixed state, six global banks, active frames,
history, resources, surfaces, and retained graphics remain to be serialized together.

Seven focused tests cover exact BMP bytes and round-trip, the installed read-only thumbnail oracle,
header-only metadata, separate compatibility ids, pair failure precedence, VM opcode integration, and both
resume-marker unwind cases.

Validation: all 377 engine tests pass; the Godot C# build has zero warnings and the threaded headless
self-test reports `SELFTEST OK`. SC0000 now has 128/129 distinct opcodes handled (99.2%); only the active
numbered-load restoration rendezvous `0xae` remains effectful and unimplemented there.

**Next persistence step:** implement complete numbered logical layout 3 and wire `0x19e`, `0x1a1`, and
the active branch of `0xae`; retain `0x19f` as the mapped but corpus-unused data-only load path. JSON
inspection/export remains an additive extended-mode feature.

### Persistence implementation step 5 — complete layout-3 numbered save/load (2026-07-24)

The numbered body and resume path are now implemented as native binary compatibility mode.
`NativeNumberedSaveCodec` reads/writes the `0x5718 + cutoff*0x414` fixed state, six separate native
global banks, CP932 string blob, 100 resource records, 1,000 surface records, retained `0x2d4`-byte gfx
objects, and range transform. `NativeTextHistoryCodec` owns the separate 12-byte-header LZSS history tail.
The installed read-only `SAVE00.DAT` oracle decodes end to end: its container/body boundary, cutoff,
`[402459,1,789,1,1,1]` banks, 211 retained objects, and 21-byte history tail all agree with the native
serializer allocation and write order.

SYS4 parsing now retains all three footer tables. T1 is the saved message/reset resume table, T2 is the
saved call-script table, and T3 maps intra-script returns. `ExecFrame.Pc` is kept current at opcode
boundaries so `0x19e` captures frames root-through-`0x1ad` cutoff accurately. Full `0x1a1` load restores
the global banks, history, reloadable surfaces, retained objects, and playtime, then deliberately unwinds
the obsolete C# recursion, executes `CALLBACK_LOAD.BIN` when available, and enters the restore rendezvous.
The active `0xae` path rebuilds the saved nested frames and resumes the terminal frame through T1;
inactive `0xae` stays a no-op. Corpus-unused `0x19f` shares the state decoder without
activating frame/history restoration. Successful `0x19e` appends history and flushes shared SAVE.DAT/RT.DAT.

The VM now carries integer, float, string, integer-pointer, and string-pointer banks separately across
`GameSession`, correcting the prior flat-bank approximation while keeping compatibility fallbacks for
older synthetic pointer tests. Focused coverage includes logical byte round-trips, CP932/LZSS history,
container-tail preservation, native installed-save import, opcode save integration, retained gfx/surface
restore, and a two-frame `0xae` reconstruction.

Validation: all 383 engine tests pass, including the installed read-only layout-3 oracle; SC0000 is now
129/129 distinct opcodes handled. Opcode sources were regenerated after recording the T1/T2/T3 and
implementation status. The remaining 1.0 persistence work is
gameplay validation through Himegari's real SAVE.BIN UI and any corrections that reveals; JSON inspection,
namespaced mod data, and migrations remain extended-mode work.

### Persistence implementation step 6 — real SAVE.BIN integration gate (2026-07-24)

The native persistence path is now exercised through Himegari's actual `SAVE.BIN` bytecode rather than
only synthetic opcode programs. A deterministic native pair proves the menu queries slot metadata,
rasterizes its timestamp/playtime fields, and decodes the paired 112x84 `.STH` thumbnail into the menu
surface. A scripted native mouse press/release on row zero then traverses the real load-mode control flow,
executes `0x1a1`, runs `CALLBACK_LOAD.BIN`, and reaches the saved script's active `0xae` rendezvous.

A second, read-only compatibility gate points the same real-script path at the installed Himegari
`SAVE00.DAT`. It decodes the complete native layout, resolves the installed save's first persisted script
through the mounted SYS4 catalogs, and enters that frame with `FrameCause.SaveRestore`; the test stops at
that boundary before gameplay continues and never writes to the original AppData tree.

The first visual acceptance run exposed an incomplete-fixture/lifecycle issue rather than a numbered
codec error. A numbered pair carries the fixed timestamp/playtime header and screenshot, but `SAVE.BIN`
loads its chapter, location, protagonist name, level, growth, personality, difficulty badge, 15-bit
cleared-ending/NG+ mask, and append-install mask from slot-indexed selected cells in shared `SAVE.DAT`.
Godot had injected the store without loading `SharedProfile`, so those `0x1a3`/`0x1aa` reads correctly
returned zero/empty. Startup now loads shared `SAVE.DAT` and `RT.DAT` before constructing the gameplay VM
(self-test remains isolated), and the real-script regression covers the three preview strings, numeric
glyph values, and one 17x17 badge per set ending bit.

The Godot profile remains intentionally isolated at `user://SAVE`. Visual compatibility therefore uses
copied `SAVE00.DAT`/`.STH`, `SAVE.DAT`, and `RT.DAT` files without exposing the original save directory
to writes. JSON inspection/export, namespaced mod state, migrations, and richer import UX remain
extended-mode work.

Validation: all 386 engine tests pass, global/opcode lint reports zero errors/warnings, the Godot C#
build has zero warnings, and the threaded headless run reports `SELFTEST OK`.

### Persistence documentation closeout — canonical file-format specification (2026-07-24)

`docs/sys4-format-notes.md` now contains the canonical byte-level persistence specification for
Himegari 3.10. It separates structural ownership from game semantics and documents the common
S3SD/S4SD container and transform, shared `SAVE.DAT` selected-cell payload, standalone `RT.DAT`
ReadTextDB, numbered layout-3 fixed/dynamic state and appended History stream, and paired `.STH` BMP.
Installed-file oracle counts and exact known limits are recorded alongside each layout.

Overlapping byte-layout prose was removed from `docs/engine-re.md`; that reference now retains native
function provenance, opcode behavior, save-root/lifecycle policy, and restoration mechanics while linking
to the format specification. Game-global meanings remain sourced from `vm-map/globals.toml` and its
generated reference. The spec explicitly leaves legacy numbered layouts 1/2, opaque shared sections,
and unnamed retained-gfx fields outside the confirmed Himegari compatibility claim.

This documentation-only closeout does not change runtime persistence. The next functional check remains
continuing from the installed slot through the restored gameplay frame.

### Persistence implementation step 7 — installed gameplay continuation correction (2026-07-24)

Interactive loading of slot 000 reached `SYSTEM4.BIN` as a restore but then played `LOGO.BIN` and
`OP.BIN` before returning to a broken TITLE state. The installed-save regression had stopped on frame
entry, before executing the saved root's `0xae`, while the synthetic two-frame test accidentally made
the same child call through its ordinary script path.

The restore state and decoded frames were correct (`SYSTEM4.BIN` root plus terminal `FORT.BIN`). The
actual integration error was the opcode registry: `0xae` had the implemented semantic case but retained
placeholder label `u00415130`, and VM dispatch selects cases by label. It therefore behaved as an
unhandled stub. The source label is now `continue-save-load-stack-restore` and generated opcode
references were rebuilt.

The synthetic regression now requires the child to enter with `FrameCause.SaveRestore` and rejects an
ordinary `CallScript` entry. A new read-only installed-save continuation gate traverses the real
`SAVE.BIN → CALLBACK_LOAD.BIN → SYSTEM4.BIN → FORT.BIN → CHMENU.BIN` path and stops at FORT's stable
gameplay poll; the original AppData files remain untouched.

Validation: all 387 engine tests pass, opcode lint reports zero errors/warnings, the Godot C# build has
zero warnings, and the threaded headless run reports `SELFTEST OK`. The next acceptance action is a
visual retry of slot 000 in the rebuilt port.

### Persistence implementation step 8 — preserve initialized banks and restore retained audio (2026-07-24)

The first successful FORT continuation exposed a second load-state boundary. CHMENU retained party-slot
shells but lacked names/definition data, SELSTAGE listed no stages, unit-management was blank, and the
TITLE BGM continued playing. Reaching FORT through the developer launcher populated the same consumers,
localizing the issue to restoration rather than their rendering opcodes.

Layout 3 stores mutable prefixes, not the complete initialized typed banks. The installed integer count
is `0x6241b`, before unit/stage definitions including `0x66716` and `0xe8275`; the installed string count
is `0x315`, exactly where `unit_story_display_names` begins. Native deserialization zeroes only the
counted prefixes. The port instead cleared whole dictionaries, erasing the initialization-authored tail.
Full load now removes and replaces only keys below each saved count, including saved zeroes, and preserves
every initialized key at or above the boundary.

Native RE also resolves the fixed audio words. Payload `+0x008` is the current direct-name BGM track;
the installed value `0x18` matches FORT's skipped BGM024 instruction. The following ten DWORDs are packed
SFX resource ids; the installed save retains SE015 and SE020 in channels 1 and 2. VM audio operations now
track these values, numbered saves emit them, and full restoration replaces the BGM plus reloads retained
SFX channels. Opcode `0xc0` is mapped as the current-BGM query. Ghidra `/v2` names/comments the BGM
getter/setter/player and saved-SFX reload worker, includes the two new EngineCtx fields, and is saved.

Regressions cover saved-zero replacement, preservation at integer/string prefix boundaries, BGM/SFX
round trips, the `0xc0` query, and installed SAVE00 continuation with its matching append-install mask.
Validation: all 387 engine tests pass, opcode and EngineCtx generation/lints are clean, the Godot C#
build has zero warnings, and the threaded headless run reports `SELFTEST OK`. Manual slot-000 acceptance
then confirmed that CHMENU, unit management, SELSTAGE, and FORT audio populate correctly. The remaining
visible load-specific discrepancy is the reusable choice-box frame losing its top winged flourish after
restoration; that is the next independent investigation slice.

### Persistence implementation step 9 — preserve flag-zero system surfaces (2026-07-24)

The post-load choice-box discrepancy came from the graphics half of the same ownership rule. BUNKI builds
the common modal frame from SYSTEM4's texture slot 15: resource `0x3383` supplies its corners, borders,
selection bar, and winged top ornament. The installed layout-3 record retains that resource id but stores
zero at record `+0x08`.

Native RE confirms `+0x08` is a reload flag, not general surface presence.
`save_data_deserialize_and_begin_restore@0x40fd10` calls `gfx_surface_load_asset` only for flag-one
records. Flag-zero system surfaces remain live unless a separate all-1,000 release path is enabled by
both `set:CreateObject` and `set:AutoFreeTex`. The port instead released every host surface and cleared
the complete managed surface map before applying only flag-one records. That removed slot 15, leaving
BUNKI's black backing but none of its atlas decoration.

Full load now preserves the current surface registry, overlays explicitly reloadable saved records, and
still replaces retained objects and the range transform. Regressions retain an initialized flag-zero
slot across a synthetic full load, prove that it is not redundantly reopened, and pin the installed
slot-15 resource/reload-flag oracle. The `/v2` deserializer annotation records the corrected ownership
boundary and the program is saved. Manual slot-000 acceptance confirms the stage-entry and later BUNKI
choices retain the complete winged frame.

The follow-up fidelity pass closes the two deferred policy gaps. `VmOptions` now carries the native
`CreateObject`/`AutoFreeTex` settings with Himegari's registered defaults (`true`/`false`), and enables
the all-1,000 host/model release only when both are true. Surface snapshots carry a named per-slot reload
bit; ordinary texture loads no longer manufacture it. Native encoding initializes unused resource ids to
`-1`, preserves resource/color/reload/created fields, and decoding distinguishes stored records from the
subset that must actually be reopened. Opcode `0x259`, formerly treated as a structural marker, is now
mapped and implemented as the script-entry clear of record `+0x08`/`+0x0c` (the port models `+0x08`).
Regressions cover native-default preservation, opt-in all-release, flag-for-flag port-authored output,
script-entry clearing, and the installed all-zero reload-bit oracle.

### Persistence implementation step 10 — publish selected offscreen render targets (2026-07-24)

The first port-authored dungeon save exposed two symptoms with one graphics cause: its 112x84 `.STH`
was completely black, and loading it showed the dungeon map for one frame before the map surface became
black while the UI/minimap remained live.

SAVE.BIN's bytecode makes the missing contract explicit. It renders the underlying gameplay handle range
into created 800x600 surface 2, binds that surface to handle `0x15f90`, scales it to 14% into created
112x84 surface 192, then asks opcode `0x1ae` to serialize surface 192. Both render passes use `0x20d`
target selection, `0x20e` clear, and `0x222` range publication. Dungeon map construction uses the same
offscreen path. Godot had modeled selection and black allocation, but `PresentObjectRange` only requested
an onscreen recomposition, so neither target ever received pixels.

Selected-target `0x222` and `0x20c` now publish through a platform-neutral retained-surface rasterizer.
It filters the requested handle range and reproduces affine/range transforms, scaling, tint, opacity,
blend mode, color keys, created/source surfaces, and black clears before storing the target pixels.
Backbuffer publication is unchanged. A focused regression reproduces SAVE.BIN's scaled texture capture
and proves objects outside the range cannot leak into the target. The original black SAVE01 thumbnail is
a preserved diagnostic artifact; a newly overwritten save is required for visual acceptance of the
fixed `.STH`. The subsequent immediate-resave test showed that a separate numbered-state round-trip defect
still prevented the rewritten `.DAT` from resuming interactively.

### Persistence implementation step 11 — repair port-authored numbered-state round trips (2026-07-24)

The clean comparison target was `SAVE03`: load native slot 0 and save immediately, without entering a
dungeon. Loading that rewrite was completely black, proving the remaining failure was not dungeon-map
construction. The source slot has two saved frames; the rewrite had four and incorrectly included the
currently open save-menu/helper frames. Full `0xae` restoration had not re-established the terminal saved
frame as opcode `0x1ad`'s ongoing save cutoff, so the later `0x19e` fell back to the deepest active helper.
The terminal restore now retains its `ExecFrame` as `_saveResumeFrame`. A regression reconstructs a
two-frame save, invokes `0x19e` from a nested helper, and proves the new slot still contains only the two
gameplay frames.

Binary comparison also corrected the retained-gfx writer. Native records are not tightly packed:
each handle plus meaningful `0x2d4`-byte record begins on a `0x2d5`-DWORD / `0xb54`-byte stride. The port
now emits that sparse layout and imports its earlier packed experimental files. New records start from
the exact `gfx_object_init_default` identity-matrix/color state; decoded native records retain an opaque
byte template so unnamed fields survive rewrites; and translation is encoded at row-vector matrix
entries 12..14 rather than over the matrix diagonal. Focused tests cover the native stride, all six
identity matrices, translation placement, opaque-byte preservation, and the restored save boundary.

The `/v2` serializer/deserializer annotations and canonical format notes are updated. The next acceptance
action is to rebuild, load native slot 0, immediately overwrite a fresh slot, and load that fresh rewrite;
older slot 1/2/3 files remain useful compatibility inputs but retain the already-written bad frame chain.

Validation: all 391 engine tests pass; opcode lint/tooling tests are clean; the Godot C# build has zero
warnings; the 211-object SAVE03 compatibility rewrite decodes with the native stride; and threaded
headless execution reports `SELFTEST OK`.

### Persistence implementation step 12 — preserve restored ancestor continuations (2026-07-24)

Fresh slot 004 passed the immediate load gate, but selecting a stage played the Eushully intro. Its frame
count was fixed at two, yet a decoded comparison found the decisive remaining difference:
native slot 000's SYSTEM4 parent stores `resume=8, call=8`, while slot 004 stored `-1,-1`.

Native `0xae` restores each parent context to its real T1/T2/T3 coordinates before activating the next
saved child. The managed reconstruction instead recursively runs FORT while its SYSTEM4 `ExecFrame` is
physically parked at the synthetic `0xae` instruction. Re-save capture derived coordinates from that
synthetic PC, found neither a T1 nor T2 table entry, and wrote `-1`. FORT could initially run, but the
later stage-launch unwind had no SYSTEM4 T2 continuation and fell into ordinary boot.

Restored frames now shadow their original serialized coordinates while a restored descendant remains
active. Re-saves use those T1/T2/T3 values; T3 rebuilds the live local-return stack; T1 initializes the
read-message coordinate; and the shadow clears only when the child genuinely returns to its parent.
Root-reload and process-exit results now propagate through this synthetic recursion as they do through
ordinary `call-script`. The existing two-frame load/re-save regression now asserts the ancestor's resume
and call indices and loads the rewrite again, rejecting any ordinary re-entry into the restored child.

The `0xae` opcode source/reference, engine RE, roadmap, status memory, and `/v2` plate comment record the
refinement. Existing slot 004 retains its already-written `-1,-1`; acceptance requires another fresh
slot created from native slot 000, followed by load and stage launch.

Validation: engine **392/392**, clean opcode lint/tooling, zero-warning Godot build, and threaded
`SELFTEST OK`. The remaining gate is interactive confirmation with a newly authored slot; the fix cannot
retroactively repair slot 004's serialized parent coordinates.

### Persistence implementation step 13 — run restored script prologues before `0xae` (2026-07-24)

Manual slot 005 acceptance passed both immediate base restoration and later stage launch. The next
dungeon-authored rewrite, slot 006, loaded with its UI present but a black, noninteractive map. Binary
and software-renderer inspection showed that the slot itself was healthy: it contained FIELD's saved
range, 704 retained objects, map textures, and a native-looking 0.8 transform. During restore, however,
FIELD overwrote the map objects with zero scale. Global zoom selector `G[0x7682]` was 3, while the
frame-local zoom table entry that should have returned 80 was zero.

Native `script_frame_restore_saved_layout@0x40f2d0` calls
`script_frame_load_resource@0x40e980`, which creates each saved script frame at its ordinary codebase.
The script runs its entry prologue and reaches `0xae` itself. The managed restore instead created frames
directly at `0xae`, skipping FIELD's inline initialization of `[40, 50, 64, 80, 100, 124, 156]`.

Restored frames now start at script offset zero while retaining the serialized T1/T2/T3 coordinates
consumed at `0xae`. Synthetic root and child tests require their pre-rendezvous prologues to execute.
The unchanged real slot 006 then replays with zoom 80, 0.8 object scale, a nonblack dungeon raster, and
the gameplay poll; the fix does not require rewriting that save.

The adjacent surface declaration gap is also closed. Native opcode `0x258` sets surface-record `+0x08`
from flags bit 0 and `+0x0c` from bit 1 after `0x259` clears both tables. The port now applies bit 0 as
the numbered-load reload policy; bit 1 remains identified but has no known consumer.

Validation: engine **393/393**, clean opcode lint/tooling, zero-warning Godot build, threaded
`SELFTEST OK`, and exact slot-006 software replay with zoom 80 and a nonblack dungeon raster.

Manual acceptance passed: the unchanged slot 006 restores its dungeon map and controls without a re-save.

### Persistence implementation step 14 — shared-profile shutdown lifecycle closeout (2026-07-24)

Shared `SAVE.DAT` and `RT.DAT` already loaded at Godot startup and flushed after successful numbered
opcode `0x19e`, but closing the port only disposed frontend resources. Profile-selected cells and
committed ReadTextDB flags could therefore be lost if the user exited without making a numbered save.

Native `age_main_window_proc@0x486320` resolves the policy exactly. Once `WM_CLOSE` is forced or accepted
by the normal confirmation path, it queries `set:NoSaveDat`; zero calls `shared_profile_save@0x40c950`
before teardown and nonzero skips it. `engine_settings_register_defaults@0x46be30` registers zero.
Successful numbered/context save calls the shared writer directly, so `NoSaveDat` gates shutdown only.
Both native functions are annotated in the saved `/v2` image.

Godot now requests a clean VM stop, releases the blocking host, waits up to five seconds for the worker
to leave its opcode boundary, and only then flushes the shared profile. This avoids serializing while the
VM can still mutate selected cells or read flags. The shutdown operation is idempotent across repeated
frontend notifications, reports expected I/O failures without throwing through teardown, and skips the
write rather than racing if the worker cannot stop. Self-test uses the native `NoSaveDat` seam and remains
filesystem-isolated.

`SharedProfile` now retains the accumulated-playtime value from the loaded `SAVE.DAT` header; a new VM
adds its elapsed process time to that baseline instead of resetting the shared header to the current
launch. Six focused regressions prove restart continuity without any numbered save, `NoSaveDat`
suppression, numbered-save independence from that switch, single-write teardown, playtime preservation,
and graceful I/O failure.

Validation: engine **399/399**, zero-warning Godot build, and threaded `SELFTEST OK`.

## Data-semantics sidebar: focused append EBINIT inspection (2026-07-24)

The static INIT surface now accepts a universal packed script id for focused append inspection.
`extract_init.py EBINIT --packed-id 0x01000001` locates selector 1's AAI catalog, bounds-checks and reads
`$1$EBINIT.BIN` from its ALF range, and parses the payload in memory. Because this script is an additive
fragment, extraction imports the base EBINIT name-array base, first id, and 1,000-row span rather than
misclassifying its sparse writes as an independent layout. The generated `APPEND01-EBINIT` artifact remains
separate from base EBINIT and records both packed-resource and base-layout provenance.

The fragment contains seven unit rows: 81 and 900..905. Their raw arrays and row-major cells project
through the existing, fully named EBINIT schema; selector-keyed graphics remain full packed ids. A new
`init_table_profile.py --record ID_OR_NAME` view prints a single unit's authored descriptions and
semantic-field/value/raw-coordinate table, with large resource values also rendered in hexadecimal.
Regressions cover packed loading, the complete sparse id set, inherited geometry, representative unit-81
level/base-stat/growth fields, and its packed battle-sprite id.

This closes the immediate “inspect an append unit semantically” need without asserting that the port
runtime has executed `$1$AUTORUN.BIN`. Native RE subsequently proved INIT2 op `0x143` as the
base-initializers → mounted record-zero scripts → TUNE boundary. The port now implements that natural
bootstrap: SYSTEM4 regression-proves `$1$AUTORUN.BIN` and `$1$EBINIT.BIN` execute between BTANINIT2 and
TUNE. Ordered whole-pack extraction/merged semantic projection remains separate tooling work; see
`docs/asset-resolution-re.md` and `docs/engine-re.md`.

## Data-semantics sidebar: battle experience rewards (2026-07-24)

BTL now proves the complete experience-award path. After each combat exchange it identifies a surviving
faction-1 participant, reads the opposing runtime entity's EBINIT definition and base
`unit_experience_reward`, and applies an enemy-minus-player runtime-level bracket. Surviving opponents use
40/30/20/10 percent for differences `>=3`/`2`/`1`/`<=0`; defeated opponents use
250/200/150/100/75/40/20 percent for `>=3`/`2`/`1`/`0`/`-1`/`-2`/`<=-3`. Integer division truncates.
ADDEXP then advances the persistent 0..99 progress cell, carries excess through level-ups, and stops at
the EBINIT level cap.

The append examples close both motivating questions. Unit 900 `ヘタレアースマン` has no reward write,
no base unit-900 definition, and no earlier writer for that cell, so its effective zero-initialized reward
is zero. Unit 901 `キングプテテット本体/BOSS` writes 50 and unit 905
`キングプテテット分身/BOSS` writes 20. Append stage 200 places one body and two splits with level
clamps 70..99, producing nonlethal body awards 20/15/10/5 and split awards 8/6/4/2 across the four level
brackets. The observed 6–10 per exchange is therefore ordinary table behavior, not a King-specific flag.

FIELD and SETEN establish the level input: eligible party levels are averaged (top five when necessary),
enemy definitions auto-scale upward from their starting level, and stage minimum/maximum clamps apply.
Difficulty's -5/0/+5 adjustment affects stat-growth iterations separately and does not change the runtime
level BTL compares. The global registry now names the runtime definition ids, runtime entity levels,
party reference level, persistent experience progress, and BTL outcome flags; evidence regressions protect
the formula constants, ADDEXP gates and modifiers, level construction, append rewards, and stage-200 spawn
composition.

**Next:** investigate the highest-use remaining auto-shaped row table, the stride-10 table at
`G[0x3ebe]` (21 references across CALCREVISE, CHMENU, DRAWTIP, GAMECLEAR, GAMESTART, IMPROVE, and
TUNE).
