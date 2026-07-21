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
