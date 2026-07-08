<!-- DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build -->
# Opcode Reference (generated)

248 opcodes used by Himegari. Source of truth: `vm-map/opcodes.toml`.

## adv

### 0x7a `text-param?` (u0041AD70, argc 3)
- **summary:** 3 args (imm/computed/imm); sub computes a value then 0x7a then show-text — text speed/wait/window param
- **grounding:** source=inference, confidence=med
- **evidence:** confirm via frida

## audio

### 0xb6 `snd-ctrl?` (u0041D080, argc 1)
- **summary:** 1 imm; self-chains, 0x41D family near play-sound-effect/0xb5 — sound channel/volume/stop control
- **grounding:** source=inference, confidence=low
- **evidence:** confirm via frida

### 0xbf `play-bgm` (play-bgm, argc 1)
- **summary:** Play background music by id. BGM is addressed by DIRECT LITERAL NAME: id -> BGM{id:03d}.OGG (in DATA3), NOT the per-scene section manifest (that's voices/textures). E.g. play-bgm 5 -> BGM005.
- **grounding:** source=investigation, confidence=high
- **evidence:** By-ear confirmed (2026-07-06): SC0000 real game plays BGM005 for play-bgm 0x5 and BGM008 for play-bgm 0x8 (we initially mis-played BGM006/BGM009 via the manifest = off-by-one). Direct-name proven by play-bgm 0x23 -> BGM035.OGG, a real standalone track (BGM set skips 030-034) that the manifest mis-resolved to a graphics entry (EV049AA.AGF). CORRECTS the earlier 'unified manifest / Frida BGM006' claim, which was wrong by one. Voices/textures still use the manifest (files[base+id], offset 0). Diagnostic: `Age.Cli audio SC0000.BIN`.

### 0xc4 `play-voice` (play-voice, argc 1)
- **summary:** Play a voice clip by id; id resolves via the SYS4INI section manifest -> files[section_base(scene)+id] (voice OGG in DATA1/DATA4). Same rule as set-texture (NOT play-bgm, which is direct-name BGM{id:03d}).
- **grounding:** source=investigation, confidence=high
- **evidence:** By-ear confirmed (2026-07-06): SC0000 prologue voices play on their lines via Godot AudioStreamPlayer. Off-by-one disproven structurally: manifest interleaves graphics/voice (files[35]=EV049AA, [36]=MAN999, [37]=EV052CA, [38]=SYL0001), so files[base+id] lands voices on OGGs while files[base+id-1] would land them on .AGF graphics (silent) -- and they play, so the offset is exactly 0. Lily's lines are correctly form-gated (G[0xa57/0xa58/0xa59]) and stay silent when no form flag is seeded -- not a bug.

## control

### 0x3 `call-script` (call-script, argc 1)
- **summary:** load & call another SYS4 script by id; id = RAW index into the SYS4INI file table (asset-index). Pushes a script frame; returns to caller when the callee ends.
- **grounding:** source=investigation, confidence=high
- **evidence:** native-RE (Ghidra): handler FUN_0041bc90 -> loader FUN_0040e980 -> resolver FUN_0044f390 indexes an 80-byte record table (base [ctx+0x414], count [ctx+0x40c]) at base+id*0x50 = the SYS4INI record layout {name[64],arc_id@0x40,file_number@0x44,offset@0x48,size@0x4c}. Confirmed statically: all 297 distinct corpus call-script ids resolve to a .BIN script with a semantically-exact name (0x1ab->ADDITEM, 0x2ae7->MES, 0x143->BUNKI, 0x329d->CALCREVISE), 0 out-of-range, 0 pack-branch. See docs/engine-re.md + name-resolution.md #1.

op 0x03 (call-script, argc 1): `call-script <id>`. RESOLVED — the id is a direct RAW index into
the SYS4INI global file table (the same table parse_sys4ini.py reads, but indexed WITHOUT skipping
'@' placeholders; SYS4INI has 13208 records / 2 placeholders). No separate on-disk id->code registry
exists; SYS4INI *is* the call-script registry.
Native mechanism (dispatch table `handler(op)=ctx[0x26c93+op]`, op 0x03 -> FUN_0041bc90):
  1. FUN_0041bc90 fetches operand 1 (id), bounds-checks call depth (<=0x26), pushes a frame.
  2. FUN_0040e980 (loader): opens the resource by id, reads the 0x20-byte SYS4 header, checks magic,
     allocates per-frame code/local buffers from the header var-counts, reads the bytecode body,
     pushes a script frame (stride 0x1e = 30 dwords, indexed by ctx[0x14f45]).
  3. FUN_0044f390 (resolver): record = [ctx+0x414] + id*0x50. Tries a LOOSE OVERRIDE first
     (CreateFileA on record.name -> mod/patch hook point), else opens archive [record.arc_id*0x100 +
     ctx+0x410], SetFilePointer to record.offset, size = record.size.
     (High-byte-tagged ids `id & 0xff000000` select an alternate pack via [ctx+0x3028]; UNUSED by the
     corpus -- 0/297 ids have a high byte.)
Companion op 0x8f `call` is INTRA-script (a local JSR), not cross-script -- see its entry.
This also names the whole call graph statically (build/callscript-names.json).


### 0x8f `call` (call, argc 1)
- **summary:** intra-script subroutine call (local JSR): PC = frame.codebase + operand*4; pushes a return address on the per-frame return stack. NOT cross-script (that is call-script 0x03).
- **grounding:** source=investigation, confidence=high
- **evidence:** native-RE (Ghidra): handler FUN_0041fba0 (= ctx[0x26c93+0x8f]) sets [frame PC @+0x53d2c] = [frame codebase @+0x53d28] + operand*4 and pushes ((pc-base)>>2)+3 onto the per-frame return stack ([ctx+0x552e8]/[ctx+0x55248]). Target is a code OFFSET within the current script (matches header table T3 tag 0x8F = local call targets), confirming it is a local JSR, not a script load.

### 0xc8 `sleep` (sleep, argc 1)
- **summary:** Pause the script for <duration> milliseconds while rendering continues (frame pacing).
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra: dispatch ctx[0x26c93+0xc8]=0x420ec0; sleep_op_0xc8 + sleep_timer_arm decoded/annotated 2026-07-08. docs/engine-re.md sleep section.

Native handler sleep_op_0xc8 @0x420ec0 is NON-BLOCKING: it arms a timer (sleep_timer_arm @0x44cff0 at ctx+0x5f304 = active flag + start tick + duration) that the engine main loop polls, resuming the script when elapsed. Operand UNIT = MILLISECONDS (start = ms tick source DAT_0056f3d4, timeGetTime/GetTickCount class). duration<10 fast-paths via [0x56f0b8]; all real scene sleeps (100/750/1000) are >=10. The handler also writes gfx cmd-type 3 + runs anti-tamper checks, neither needed host-side. Port equivalent: the Godot host blocks the VM background thread <duration> ms while the per-frame compositor keeps presenting -> correctly reproduces the explicit one-shot dramatic pauses. NOTE: does NOT pace the rapid opening AE* burst (those draws have no sleep between them; their real pacer is unknown). Headless hosts no-op it (parity).

## draw

### 0x1a2 `gfx-cmd-register` (gfx-cmd-register, argc 1)
- **summary:** 0x1a2 (handle) — gfx cmd-type 3. Handler gfx_op_0x1a2_registry_insert @0x42d360: builds key '%c%8.8x'(3, operand-desc) and INSERTS operand 1 into the op-0x215 query registry (FUN_0042cf70, open-addressing hash; native stores map[handle]=handle). This is the SOLE populator of the registry op 0x215 queries — the geometry SET/draw ops (0x217/0x219/0x1ff/0x1fb/0x202/...) do NOT register. VM impl: GfxState.Register(handle) (a separate set from the geometry object store). Conflating the two (registering on every GetOrCreate) was the retained-mode geometry bug: CG handles wrongly read back as 'existing' and collapsed to (-400,-600). NOT save/scene (raw Kelebek VA 0x428010 drifted to op 0x1ac save handler). See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra: real handler FUN_0042d360 (via dispatch table ctx[0x26c93+op]); sets *(ctx+0x53d88+ctx[0x53d14]*0x78)=3, sprintf("%c%8.8x",3,op1), FUN_0042cf70 (hash insert; counterpart of op 0x215 find). NOT save/scene (raw Kelebek VA 0x428010 drifted to op 0x1ac save handler). See docs/engine-re.md

### 0x1f7 `gfx-elem-erase` (gfx-elem-erase, argc 2)
- **summary:** 0x1f7 (handle)(count) — gfx cmd-type 5. Handler gfx_op_0x1f7_elem_erase @0x422270: ERASES registry handles — if count>1 → gfx_registry_erase_range(handle,count) [erase [handle, handle+count)], else gfx_registry_erase(handle). It is a TEARDOWN/erase, NOT a create (corrects the earlier 'gfx-elem-create' reading). In label_12649 it runs after a 0x215 slot-query, before 0x1fa releases the slot. Objects are created lazily by the geometry SET ops (gfx_object_get_or_create). See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x422270 (dispatch ctx[0x26c93+0x1f7]); count>1 → gfx_registry_erase_range @0x47d8b0 (loops gfx_registry_erase @0x47d850 over [op1,op1+op2)), else gfx_registry_erase(op1). gfx_registry_erase does map.find+erase on the ctx+0x408 registry.

### 0x1f8 `create-texture` (create-texture, argc 4)
- **summary:** Allocate/prepare a texture slot: (slot, width, height, flag). e.g. `create-texture 0xd 0x190 0x1e 0x0` = slot 13, 400x30.
- **grounding:** source=investigation, confidence=med
- **evidence:** SC0000 CG/UI-draw path disasm; slot/w/h roles read off the operands (400x30 text bars, etc.).

### 0x1f9 `set-texture` (set-texture, argc 3)
- **summary:** Load asset #resId into texture slot: (resId, slot, flag=-1). resId resolves via the SYS4INI per-scene section manifest: files[section_base(scene)+resId] (same rule for play-bgm/play-voice). See docs/asset-resolution-re.md.
- **grounding:** source=frida, confidence=high
- **evidence:** SC0000 Frida-confirmed 17/17 (0x25->EV052CA, 0x2e->EV052DB, 0x36->BG030A background); resolution rule validated on 586/595 captured loads. Traced in CG-load subroutine label_12649 as `set-texture G[0x62424] <slot> -1`.

### 0x1fa `gfx-elem-release` (gfx-elem-release, argc 1)
- **summary:** 0x1fa (idx) — gfx cmd-type 3. Handler gfx_op_0x1fa_elem_release @0x4224a0: releases the element at [ctx+0x52bd4 + idx*4] (virtual free, then nulls the slot) + FUN_00474e40(idx). In label_12649 it clears the working slot G[0x62452] after a 0x215/0x1f7 pair. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4224a0 (dispatch ctx[0x26c93+0x1fa]); frees ctx+0x52bd4[operand1*4] via vtbl, then FUN_00474e40(operand1).

### 0x1fb `draw-texture` (draw-texture, argc 8)
- **summary:** Blit a texture slot to screen. Observed 8 args: (handle, slot, srcx, srcy, w, h, dstx, dsty). e.g. `draw-texture 0xcf08 0x3 0 0 0x320 0x258 0 0` = full-screen (800x600) slot 3 at (0,0).
- **grounding:** source=investigation, confidence=med
- **evidence:** SC0000 CG-load subroutine label_12649: `draw-texture (ptr) (slot) 0 0 (w) (h) (dstx) (dsty)`; full-screen slot-3 draws use 0x320x0x258 (800x600).

### 0x1ff `set-gfx-geom3-c` (set-gfx-geom3-c, argc 4)
- **summary:** 0x1ff (handle)(a)(b)(c) — gfx cmd-type 9. Handler gfx_op_0x1ff_set_geom3 @0x4227b0: SETS a 3-vector (int→float a,b,c) on object `handle` via native worker FUN_0047e800 (sibling of 0x217/0x219, a distinct per-object vector). See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4227b0 (dispatch ctx[0x26c93+0x1ff]); FUN_0047e800(op1,(float)op2,(float)op3,(float)op4).

### 0x202 `gfx-blit-color` (gfx-blit-color, argc 5)
- **summary:** 0x202 (handle)(x)(y)(alpha)(color) — gfx cmd-type 0xb. Handler gfx_op_0x202_blit_color @0x4228d0: worker gfx_op_0x202_worker_set_color_anim @0x47ea00 sets an ANIMATED color/alpha target (obj+0x64) + anim bit; packs ARGB from alpha(op4, ≥0x100→0xff, <0→FUN_0047f3e0) and color(op5, <0→FUN_0047f3e0). C# VM (2026-07-08 blend slice): routes through GfxState.SetObjectColor → the compositor applies STATIC alpha/tint (BlendKind.Alpha); smooth color-anim interpolation deferred. See docs/engine-re.md §Blend & transparency.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4228d0 (dispatch ctx[0x26c93+0x202]); packs (alpha<<24|rgb) from operands 4/5, FUN_0047ea00(op1,op2,op3,packed).

### 0x203 `gfx-draw-color` (gfx-draw-color, argc 4)
- **summary:** 0x203 (handle)(v)(alpha)(color) — gfx cmd-type 9. Handler gfx_op_0x203_draw_color @0x4229a0: worker gfx_op_0x203_worker_set_color @0x47e9b0 sets a STATIC color/alpha (obj+0x60), no anim bit; packs ARGB from alpha(op3)/color(op4). Sibling of 0x202 (one fewer positional arg). C# VM (2026-07-08 blend slice): routes through GfxState.SetObjectColor → compositor applies static alpha/tint (BlendKind.Alpha). See docs/engine-re.md §Blend & transparency.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4229a0 (dispatch ctx[0x26c93+0x203]); packs color from operands 3/4, FUN_0047e9b0(op1,op2,packed).

### 0x208 `get-texture-size` (get-texture-size, argc 3)
- **summary:** 0x208 (slot)(out_w)(out_h) — writes the loaded texture's width/height into two output globals; keystone for bytecode-computed sprite/bg geometry (SC0000 label_12649)
- **grounding:** source=inference, confidence=med
- **evidence:** SC0000 label_12649: set-texture(resId,slot) then 0x208(slot)->w,h feeds w/2 horizontal-center + foot-anchor subtraction into draw-texture dst; stubbing yields 0x0 sizes / off-center draws

### 0x20c `present-frame` (present-frame, argc 0)
- **summary:** Present the composited frame (native gfx_render_frame). Host-implicit: our compositor presents every frame.
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **evidence:** Ghidra: dispatch table FUN_00413860 param_1[0x26e9f]=gfx_op_0x20c_present_frame; 0x26e9f-0x26c93=0x20c. 2026-07-08.

Native handler gfx_op_0x20c_present_frame (dispatch ctx[0x26c93+0x20c]) -> gfx_render_frame @0x4820b0 flips the composited buffers. Our Godot host runs a continuous per-frame compositor (Main.Recomposite in _Process), so an explicit present is redundant and the VM can skip it. noop_headless=true -> scene coverage classifies it safe-noop. Kelebek label u00416200 was VA-drift (unrelated fn); real handler resolved via the dispatch table.

### 0x212 `set-gfx-field64` (set-gfx-field64, argc 2)
- **summary:** 0x212 (obj_idx)(val) — gfx cmd-type 5. Handler gfx_op_0x212_set_field64 @0x4230c0: obj=[ctx+0x14d54 + obj_idx*4]; if obj: *(obj+0x64)=val. Sets one per-object field. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4230c0 (dispatch ctx[0x26c93+0x212]); writes [obj+0x64]=operand2, obj from ctx+0x14d54[operand1*4].

### 0x213 `set-gfx-xy` (set-gfx-xy, argc 3)
- **summary:** 0x213 (obj_idx)(x)(y) — gfx cmd-type 7. Handler gfx_op_0x213_set_field68_6c @0x423110: obj=[ctx+0x14d54 + obj_idx*4]; if obj: *(obj+0x68)=x; *(obj+0x6c)=y (an (x,y) pair). See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x423110; writes obj+0x68/+0x6c from operands 2/3, obj from ctx+0x14d54[operand1*4].

### 0x215 `query-gfx-object?` (query-gfx-object?, argc 2)
- **summary:** 0x215 (out)(handle_id) — native graphics command-buffer op. Real handler FUN_0042a0b0 (Ghidra-resolved via the dispatch table ctx[0x26c93+op]; Kelebek's 0x421160 is VA-drift, lands in an unrelated fn). Does TWO things: (1) writes cmd-type 5 into the CURRENT gfx-object record `[ctx+0x53d88 + ctx[0x53d14]*0x78]` (a command-buffer registration, parallel to op 0x1a2→type 3); (2) returns `out = map.find(handle_id)` over an engine-internal associative registry (found value, else 0xffffffff=not-found), sign-tested (gre/lt 0) to drive label_12649's slot-select branch + set working slot G[0x62452]. So `out` is NATIVE COMMAND-BUFFER STATE (the registry is populated by sibling gfx ops — op 0x1a2→FUN_0042cf70 is the hash insert), NOT the VM global bank → seeding story-state CANNOT reproduce it. Stubbed → constant return → every draw collapses to slot 0 → anchor-preserve reads foreign-sized textures → the cumulative bg/sprite drift. SETTLES the drift as (b) a genuine native op, NOT (a) state-divergence. Faithful fix = model the gfx command-buffer (record array + handle→object registry) and run the gfx ops instead of stubbing — static/Frida-free (handlers now readable; inserts are bytecode-driven). Full decode + verdict: docs/engine-re.md (op 0x215 section). RETAINED-MODE FIX (2026-07-07): the registry MUST be separate from the geometry object store — it is populated ONLY by op 0x1a2, never by the geometry SET/draw ops. VM: QuerySlot returns the registered value (=handle) or -1, NOT a fabricated per-object slot. CG handles are never 0x1a2-registered → query returns -1 → label_12649 takes its FRESH branch (anchor from the INIT2 arrays) → dst=(0,0). The prior GfxState.GetOrCreate-assigns-AcquireSlot model made CG handles read as 'existing' → existing branch called get-texture-size on the wrong slot (0) → dst=(-400,-600) off-screen (the '2nd CG off-screen' bug).
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra: real handler FUN_0042a0b0 = {*(ctx+0x53d88+ctx[0x53d14]*0x78)=5; out=FUN_0047f280(FUN_0041b940(2))}. FUN_0047f280 = std::map::find (returns mapped value or 0xffffffff); FUN_0041b940(2) = operand-fetch of operand 2 (the handle key); FUN_00425fb0(1,val) = operand-write to `out`. Registry populated by op 0x1a2 handler FUN_0042d360 → FUN_0042cf70 (open-addressing hash insert). Bytecode sites: SC0000 label_12649 (0x12670) + label_123ef (0x12419/0x12450), handle-ids from 0x62455[idx] (±offset); result gre/lt 0 branches slot-select. Record table 0x3239 (label_125bd @0x0050f) assigns per-object slots 4..13.

### 0x216 `query-gfx-field?` (query-gfx-field?, argc 2)
- **summary:** 0x216 (out)(idx) — gfx cmd-type 5. Handler gfx_op_0x216_query_table46d14 @0x42a0f0: out = *(ctx+0x46d14 + idx*0x14). A per-object field query over a stride-0x14 table. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x42a0f0; reads ctx+0x46d14[operand2 * 0x14], writes operand1 via FUN_00425fb0(1,·).

### 0x217 `set-gfx-geom3` (set-gfx-geom3, argc 4)
- **summary:** 0x217 (handle)(a)(b)(c) — gfx cmd-type 9. Handler gfx_op_0x217_set_geom3 @0x4231b0: SETS a 3-vector (int→float a,b,c) on object `handle` via native worker FUN_0047e960. In SC0000 label_12649 it writes the anchor vector G[0x6249b/c/d] INTO the object; op 0x218 reads it back. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4231b0 (dispatch ctx[0x26c93+0x217]); FUN_0047e960(op1,(float)op2,(float)op3,(float)op4). label_12649 sites e.g. 0x00c67 handle=G[0x62457], vec=G[0x6249b/c/d].

### 0x218 `get-gfx-geom3?` (get-gfx-geom3?, argc 4)
- **summary:** 0x218 (handle)(out_a)(out_b)(out_c) — gfx cmd-type 9. Handler gfx_op_0x218_query_geom3 @0x42a130: GETS a stored 3-vector from object `handle` (FUN_0047f360) into out_a/b/c. In label_12649 it reads the object's anchor vector back into G[0x6249b/c/d] — a stubbed DRIVER of the render drift (stale anchor → bad centering). See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x42a130; FUN_0047f360(obj op1) + 3x FUN_00550850→FUN_00425fb0(2/3/4). label_12649 site 0x00c8f handle=G[0x62457] → G[0x6249b/c/d].

### 0x219 `set-gfx-geom3-b` (set-gfx-geom3-b, argc 4)
- **summary:** 0x219 (handle)(a)(b)(c) — gfx cmd-type 9. Handler gfx_op_0x219_set_geom3 @0x423240: SETS a 3-vector (int→float) on object `handle` via native worker FUN_0047e910 (sibling of 0x217, a different per-object vector). See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x423240 (was unanalyzed; function created this session; dispatch ctx[0x26c93+0x219]); FUN_0047e910(op1,(float)op2,(float)op3,(float)op4).

### 0x21a `get-gfx-geom3-b?` (get-gfx-geom3-b?, argc 4)
- **summary:** 0x21a (handle)(out_a)(out_b)(out_c) — gfx cmd-type 9. Handler gfx_op_0x21a_query_geom3 @0x42a1b0: GETS a stored 3-vector from object `handle` (FUN_0047f2e0) into out_a/b/c. In label_12649 it reads the object's position vector into G[0x62498/9/a] — a stubbed DRIVER of the render drift. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x42a1b0; FUN_0047f2e0(obj op1) + 3x→FUN_00425fb0(2/3/4). label_12649 site 0x00c86 handle=G[0x62457] → G[0x62498/9/a].

### 0x21e `set-anim-transform-norm` (set-anim-transform-norm, argc 6)
- **summary:** (handle)(p1)(p2)(x)(y)(z) — set sprite transform channel, NORMALIZED (float operands /_DAT_00571c28 ~percent); cmd-type 0xd, worker gfx_anim_set_channel@0x47eaa0. SC0000 opening @0xf73+ on INIT2 CG handles. Cluster 0x21c-0x243. Handler 0x423350; Kelebek VA 0x421450 is drift.
- **grounding:** source=investigation, confidence=high

### 0x220 `set-anim-transform-abs` (set-anim-transform-abs, argc 6)
- **summary:** (handle)(p1)(p2)(x)(y)(z) — set sprite transform channel, ABSOLUTE (raw floats); cmd-type 0xd, worker 0x47ecc0. Twin of 0x21e. SC0000 opening @0x18a5+ on INIT2 CG handles. Cluster 0x21c-0x243. Handler 0x4234e0; Kelebek VA 0x4215D0 is drift.
- **grounding:** source=investigation, confidence=high

### 0x234 `anim-start` (anim-start, argc 5)
- **summary:** (handle)(duration)(x)(y)(z) — animate object toward target vec3 (x,y,z) over the GLOBAL clock; cmd-type 0xb, worker gfx_anim_start. op2=this object's duration (label_1235a maxes into the clock). SC0000 opening @0xdaf on INIT2 CG handles. Handler 0x423da0; Kelebek VA 0x422060 is drift.
- **grounding:** source=investigation, confidence=high

### 0x238 `set-anim-clock` (set-anim-clock, argc 1)
- **summary:** (duration) — set the GLOBAL animation clock: native ctx+0x51b78=0 (elapsed), +0x51b7c=duration. cmd-type 3. NON-BLOCKING: only configures; the render loop advances it and interpolates all animating objects. SC0000 opening @0x123bd/@0x13858. Handler 0x4240e0; Kelebek VA 0x422390 is drift.
- **grounding:** source=investigation, confidence=high

## input

### 0x90 `hotspot-branch` (u0041BEB0, argc 7)
- **summary:** cursor/input hotspot hit-test: rect (x,y,w,h) -> 3-way branch on interaction, else fall through to pc+1
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **depends on:** 0x1f4, 0x1f5
- **depended on by:** 0x97
- **evidence:** all 301 ADV scripts contain the identical 8 sites; enc.len 15 lands pc+1 on the fall-through stmt (design-confirmed); fall-through = correct headless no-input path, proven by 279 CLEAN dialogue scenes

op 0x90 (u0041BEB0, argc 7): `0x90 x y w h tgt_a tgt_b tgt_c`. Kelebek left it "ukn" noting args
5-7 are code locations. Corpus analysis (all 301 ADV scripts) resolves it:
- Two forms, both ONLY in one shared ADV-chrome subroutine copied into every ADV script:
  * Mode A (1505 = 5x301): immediate x,y,w,h with w=h=20; the five on-screen buttons at
    (684|706|728|750|772, 572), each setting one of G[0x6c9..0x6cd] to 1 / 0 / 0+run-action
    (reads as hover-enter / hover-leave / click). All 3 targets real.
  * Mode B (903 = 3x301): local-int operands, w=h=1, only tgt_c real -- a keyed 2-way input test.
- Every one of the 301 scripts has EXACTLY 8 sites (5 A + 3 B); zero scene-specific use.
- Falls through (pc+1) when nothing matches -- design-confirmed (0xd0 + 15 dwords = 0xdf = label_df).
- Headless (no cursor/input) => fall through => vm0 stub already correct; the 12 EMPTY sweep scenes
  are gated by state + this input-wait chrome, NOT by unmodelled 0x90. Model live in A2.


### 0x97 `hotspot-reg?` (u0041C150, argc 5)
- **summary:** companion register-hotspot / set-widget-action (argc5: v1 v2 1 1 <action-id>; NO code targets)
- **grounding:** source=inference, confidence=med, noop_headless=True
- **depends on:** 0x90
- **evidence:** interleaves with 0x90 in the shared ADV-chrome subroutine; trailing imm = action id 0x0/0x7/0x8; same widget cluster as 0x90/0x91/0x92/0x95; confirm via frida

## marker

### 0x1bc `block-mark` (u00415670, argc 0)
- **summary:** zero-arg; follows jcc/mov, precedes mov/ret — block boundary
- **grounding:** source=inference, confidence=high, noop_headless=True

### 0x1bf `call-end` (u004156C0, argc 0)
- **summary:** zero-arg; call->0x1bf->stmt-end — end-of-call-statement marker
- **grounding:** source=inference, confidence=med, noop_headless=True

### 0x1d2 `stmt-desc?` (u0041BB40, argc 2)
- **summary:** 2 imm; immediately after stmt-begin 0x1f4 — statement descriptor?
- **grounding:** source=harness, confidence=med, noop_headless=True

### 0x1d5 `cond-block` (u00415700, argc 0)
- **summary:** zero-arg; ALWAYS follows jcc — marks conditional body entry
- **grounding:** source=inference, confidence=high, noop_headless=True

### 0x1f4 `stmt-begin` (u004160D0, argc 0)
- **summary:** zero-arg; opens scripts, pairs with stmt-end 0x1f5
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **depended on by:** 0x90

### 0x1f5 `stmt-end` (u00416120, argc 0)
- **summary:** zero-arg; precedes exit/next-stmt, pairs with 0x1f4
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **depended on by:** 0x90

### 0x21b `line-id?` (u004213E0, argc 1)
- **summary:** 1 imm; mov->0x21b->stmt-end; near save/load-messkip — likely line/stmt id, verify not msg-control
- **grounding:** source=harness, confidence=med, noop_headless=True

### 0x258 `decl?` (u00422FE0, argc 2)
- **summary:** 2 imm; runs in a chain right after script-entry 0x259, enumerating ids — prologue declaration/registration?
- **grounding:** source=harness, confidence=low, noop_headless=True

### 0x259 `script-entry` (u00416410, argc 0)
- **summary:** zero-arg; the first instruction of a script (offset 0), opens the decl chain that 0x258 continues — script/prologue entry marker, structural
- **grounding:** source=harness, confidence=low, noop_headless=True
- **evidence:** SC0000 offset 0x0 = op 0x259 (argc 0); 0x258's summary names it 'script-entry 0x259'; VM treats it as no-op (default stub) across all 279 CLEAN A0 scenes

## structural

### 0x71 `label-def` (u0041A7B0, argc 1)
- **summary:** 1 imm; count == T1 table size -> the label/anchor T1 indexes. v1 no-op; revisit if menu/callback dispatch looks up by id
- **grounding:** source=investigation, confidence=high, noop_headless=True

## unknown

### 0x1 `u004149C0` (u004149C0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2 `exit` (exit, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5 `ret` (ret, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x6 `u00417E80` (u00417E80, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x8 `u00417FC0` (u00417FC0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x9 `exit-script` (exit-script, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x21 `u00418860` (u00418860, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x22 `u00418920` (u00418920, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x25 `u00418B40` (u00418B40, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x50 `add` (add, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x51 `sub` (sub, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x52 `mul` (mul, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x53 `div` (div, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x54 `mod` (mod, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x55 `mov` (mov, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x56 `and` (and, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x57 `or` (or, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x58 `sar` (sar, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x59 `shl` (shl, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5a `eq` (eq, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5b `ne` (ne, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5c `lt` (lt, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5d `lte` (lte, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5e `gr` (gr, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5f `gre` (gre, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x60 `u0041A270` (u0041A270, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x61 `lookup-array` (lookup-array, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x63 `u00414A60` (u00414A60, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x64 `copy-local-array` (copy-local-array, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x6c `copy-to-global` (copy-to-global, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x6e `show-text` (show-text, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x6f `end-text-line` (end-text-line, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x70 `u0041A750` (u0041A750, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x72 `wait-for-input` (wait-for-input, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x73 `u0041AB30` (u0041AB30, argc 10)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x75 `u0041AC30` (u0041AC30, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x76 `u0041AC60` (u0041AC60, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x77 `u0041ACB0` (u0041ACB0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x78 `u0041AD00` (u0041AD00, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x79 `u0041AD30` (u0041AD30, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x7b `u0041ADB0` (u0041ADB0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x7c `u00416A90` (u00416A90, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x7f `u00414C60` (u00414C60, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x80 `u0041AF00` (u0041AF00, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x85 `u00414CF0` (u00414CF0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x86 `u0041B210` (u0041B210, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x87 `u00414D10` (u00414D10, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x88 `u0041B290` (u0041B290, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x8b `u0041B3D0` (u0041B3D0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x8c `jmp` (jmp, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x93 `u00415040` (u00415040, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x94 `u00415090` (u00415090, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xa0 `jcc` (jcc, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xa1 `u00427C00` (u00427C00, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xa2 `u00427FD0` (u00427FD0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xa3 `u004244D0` (u004244D0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xae `u00415130` (u00415130, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xb4 `play-sound-effect` (play-sound-effect, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xb5 `u0041D050` (u0041D050, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xb7 `u0041D0E0` (u0041D0E0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xb8 `u00415520` (u00415520, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xb9 `u0041D140` (u0041D140, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xba `u0041D0B0` (u0041D0B0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xc0 `u00415620` (u00415620, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xc2 `u0041D2B0` (u0041D2B0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xc5 `u0041D4A0` (u0041D4A0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xc6 `u0041D5D0` (u0041D5D0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xc7 `u0041D760` (u0041D760, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xcc `mouse_callback` (mouse_callback, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xcd `get-input-type` (get-input-type, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xd0 `u00415830` (u00415830, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xd3 `u00425960` (u00425960, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xd4 `u004266F0` (u004266F0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xd5 `u004262C0` (u004262C0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xd9 `u00415880` (u00415880, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xfb `joy_callback` (joy_callback, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xfe `u0041E360` (u0041E360, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xff `u00415A10` (u00415A10, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x100 `u00415A60` (u00415A60, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x101 `u00415BF0` (u00415BF0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x107 `u0041E500` (u0041E500, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x108 `u00415E70` (u00415E70, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x109 `u00415EC0` (u00415EC0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x10a `u0041E540` (u0041E540, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x10b `u0041E5A0` (u0041E5A0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x10c `u0041E5E0` (u0041E5E0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x10d `u00415F10` (u00415F10, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x12c `lookup-array-2d` (lookup-array-2d, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x12e `u0041E940` (u0041E940, argc 8)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x12f `u0041ECB0` (u0041ECB0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x130 `u00415F40` (u00415F40, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x131 `u00415F70` (u00415F70, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x132 `u0041EF00` (u0041EF00, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x133 `u0041EFF0` (u0041EFF0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x134 `u0041F050` (u0041F050, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x135 `bit-set` (bit-set, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x136 `bit-reset` (bit-reset, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x137 `u0041F1C0` (u0041F1C0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x13a `u0041F3A0` (u0041F3A0, argc 6)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x13f `check-bit` (check-bit, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x140 `u0041F9C0` (u0041F9C0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x141 `u0041FAA0` (u0041FAA0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x142 `u0041FB10` (u0041FB10, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x143 `u00415FB0` (u00415FB0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x144 `u004259D0` (u004259D0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x149 `u0041FCE0` (u0041FCE0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x191 `u0041A4A0` (u0041A4A0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x192 `set-string` (set-string, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x193 `concat` (concat, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x194 `u00425480` (u00425480, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x195 `u00425580` (u00425580, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x196 `display-furigana` (display-furigana, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x197 `u0041B510` (u0041B510, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x198 `u0041B540` (u0041B540, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x199 `u00414D50` (u00414D50, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x19a `u00414E50` (u00414E50, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x19b `u00414E80` (u00414E80, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x19c `u00414EC0` (u00414EC0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x19d `u0041C680` (u0041C680, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x19e `u0041C6E0` (u0041C6E0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1a0 `u0041C9B0` (u0041C9B0, argc 9)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1a1 `u0041CB40` (u0041CB40, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1a3 `string-lookup-set` (string-lookup-set, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x1a4 `u0041B580` (u0041B580, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1a5 `set-font` (set-font, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x1a6 `halve-strlen` (halve-strlen, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x1a7 `comment` (comment, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x1a8 `dev_ukn` (dev_ukn, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1a9 `u00428090` (u00428090, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1aa `u00425920` (u00425920, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ab `u0041CCA0` (u0041CCA0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ac `u0041CD80` (u0041CD80, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ad `u004154F0` (u004154F0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ae `u0041CED0` (u0041CED0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1af `u004245C0` (u004245C0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b0 `u0041A510` (u0041A510, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b2 `u00425790` (u00425790, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b3 `u004257D0` (u004257D0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b4 `u004237C0` (u004237C0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b5 `u0041B5F0` (u0041B5F0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b6 `u00414F60` (u00414F60, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b7 `u0041B640` (u0041B640, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b8 `u0041B670` (u0041B670, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b9 `u0041B710` (u0041B710, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ba `u0041D850` (u0041D850, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1bb `u0041B7B0` (u0041B7B0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1bd `u0041D910` (u0041D910, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1c1 `u0041B820` (u0041B820, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1c7 `u00414F90` (u00414F90, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1c8 `toString` (toString, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x1ca `u0041B9B0` (u0041B9B0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1cb `u00414FD0` (u00414FD0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1cc `u00415010` (u00415010, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ce `u0041B9F0` (u0041B9F0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1cf `u0041DA10` (u0041DA10, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1d0 `u0041BA80` (u0041BA80, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1d1 `u0041BAE0` (u0041BAE0, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1d3 `u0041BB90` (u0041BB90, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1d4 `u0041BC00` (u0041BC00, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1f6 `u00416170` (u00416170, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1fd `u00420620` (u00420620, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1fe `u004206C0` (u004206C0, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x204 `draw-string` (draw-string, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x205 `u00420A60` (u00420A60, argc 6)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x207 `u00420B00` (u00420B00, argc 8)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20a `u00420CE0` (u00420CE0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20b `u00420D50` (u00420D50, argc 7)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20d `u00420E10` (u00420E10, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20e `u00416250` (u00416250, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20f `u00420E40` (u00420E40, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x21c `u00416270` (u00416270, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x21d `u00421410` (u00421410, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x21f `u00421510` (u00421510, argc 7)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x222 `u004216C0` (u004216C0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x223 `u00421700` (u00421700, argc 8)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x224 `u00416290` (u00416290, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x228 `u00421940` (u00421940, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x229 `u004219E0` (u004219E0, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x22a `u00421A90` (u00421A90, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x22c `u00421BD0` (u00421BD0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x22d `u00421C60` (u00421C60, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x22f `u00421DD0` (u00421DD0, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x230 `u00421E70` (u00421E70, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x231 `u00421EA0` (u00421EA0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x232 `u00421EF0` (u00421EF0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x233 `u00421FB0` (u00421FB0, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x236 `u004221A0` (u004221A0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x239 `u004223C0` (u004223C0, argc 6)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x23a `u00422420` (u00422420, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x23b `u00422460` (u00422460, argc 7)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x23c `u004162B0` (u004162B0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x23d `u004162F0` (u004162F0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x23f `u00422930` (u00422930, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x241 `u00422B80` (u00422B80, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x242 `u00422D60` (u00422D60, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x243 `u00417070` (u00417070, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x248 `u00422E80` (u00422E80, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x249 `u00422EB0` (u00422EB0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x24d `u00422E90` (u00422E90, argc 12)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x24e `u00422EA0` (u00422EA0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2bd `u00423100` (u00423100, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2bf `u00423180` (u00423180, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2c0 `u004231C0` (u004231C0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2c5 `strlen` (strlen, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x2c6 `u0042B5E0` (u0042B5E0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2c8 `u0042B610` (u0042B610, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

