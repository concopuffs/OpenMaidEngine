<!-- DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build -->
# Opcode Reference (generated)

248 opcodes used by Himegari. Source of truth: `vm-map/opcodes.toml`.

## adv

### 0x7a `set-adv-text-cursor` (set-adv-text-cursor, argc 3)
- **summary:** (layout_slot)(x)(y) - set the cursor in the selected ADV text layout's last 20-byte record. Slot 0 selects the current layout.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x7a_handler@0x41eba0 fetches operands 3,2,1 and calls adv_text_set_cursor@0x4530f0 on text manager ctx+0x14940. Slot 0 resolves manager+0x4c8; manager+0x414[slot] selects the layout; text_layout_set_cursor@0x452530 writes x/y to +4/+8 of its last 0x14-byte record. SC0000 0x9d3 computes slot 1, x=75, y=47 before voiced show-text.

### 0x204 `draw-string` (draw-string, argc 4)
- **summary:** (surface_slot)(x)(y)(string) - rasterize a CP932 string immediately into a numbered graphics surface using current font/color/effect state.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x204_handler@0x422a60 resolves operand 4 as a string, fetches surface/x/y, then calls draw_string_to_surface@0x450150 on text manager ctx+0x14940. The worker validates and locks gfx-manager surface table +0xa590[slot], chooses text_raster_string_uncached@0x459d90 or cached/effect path @0x45b600, rasterizes GDI GetGlyphOutlineA bitmaps through text_blit_glyph_bitmap@0x458c80 using font/color state +0x4d0/+0x458, then unlocks. SC0000 0x9b2 draws the speaker name into 400x30 surface 0xd at (1,1); following 0x1fb binds it to retained object 0xe678 at (74,444).

## audio

### 0xb4 `sfx-load` (play-sound-effect, argc 2)
- **summary:** (resource_id)(channel) — synchronously resolve/open the scene-manifest asset and replace the channel's decoded sound buffer without starting playback. Native manager supports channels 0..12; SC0000 uses 0..9.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra op 0xb4 handler 0x4201d0 -> sfx_channel_load@0x482500 -> asset_open@0x44f390 + sound_decode_channel@0x483360. Native trace: SC0000 0xc29 loads resource 0x28 into channel 0; resource resolves by section_base+id to E0808.WAV; completion precedes 0xb5 in the same millisecond.

### 0xb5 `sfx-start` (u0041D050, argc 1)
- **summary:** (channel) — start the already-loaded channel once (logical loop=false). DirectSound publishes synchronously through Play(0,0,DSBPLAY_LOOPING); the low-level flag loops only the streaming ring, while decoder EOF stops logical playback.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra op 0xb5 handler 0x420210 passes mode 0 to sfx_channel_start@0x4825d0; mode 1 belongs to op 0xba. sound_buffer_start@0x484270 primes four quarter-buffer notifications then calls IDirectSoundBuffer::Play with flag 1 before returning. Native trace at SC0000 0xc2e: E0808 channel 0 start enters/leaves in the same ms, preloaded 1->0 and playing 0->1.

### 0xb6 `sfx-release` (u0041D080, argc 1)
- **summary:** (channel) - stop/destroy the channel decoder and DirectSound buffer, clear its retained resource id, and leave the slot empty. Idempotent for an unused channel.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra op 0xb6 handler 0x420250 -> sfx_channel_release@0x482600 -> sound_buffer_destroy@0x4831a0, which releases the per-channel object under its critical section and clears the slot. Native trace captured SC0000's channels 0..9 release sweep in consecutive calls.

### 0xbf `play-bgm` (play-bgm, argc 1)
- **summary:** Play background music by id. BGM is addressed by DIRECT LITERAL NAME: id -> BGM{id:03d}.OGG (in DATA3), NOT the per-scene section manifest (that's voices/textures). E.g. play-bgm 5 -> BGM005.
- **grounding:** source=investigation, confidence=high
- **evidence:** By-ear confirmed (2026-07-06): SC0000 real game plays BGM005 for play-bgm 0x5 and BGM008 for play-bgm 0x8 (we initially mis-played BGM006/BGM009 via the manifest = off-by-one). Direct-name proven by play-bgm 0x23 -> BGM035.OGG, a real standalone track (BGM set skips 030-034) that the manifest mis-resolved to a graphics entry (EV049AA.AGF). CORRECTS the earlier 'unified manifest / Frida BGM006' claim, which was wrong by one. Voices/textures still use the manifest (files[base+id], offset 0). Diagnostic: `Age.Cli audio SC0000.BIN`.

### 0xc2 `fade-bgm` (u0041D2B0, argc 2)
- **summary:** (target_percent)(duration_ms) — block script service while linearly fading current BGM volume to 0..100%. Durations >=1000 ms use 100 steps; shorter fades use 10. Target 0 releases the current BGM source at completion.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra op 0xc2 handler 0x4204c0 sets run-state 0x200, arms the service timer, and calls bgm_fade_arm@0x464830; bgm_fade_tick@0x464960 interpolates current/target percent and applies volume, releasing at target 0. Native SC0000 trace at 0x7c1/0x126c shows target 0, duration 3000, 1% ticks at about 30 ms.

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


### 0x7b `coroutine-save-yield-handlers` (u0041ADB0, argc 2)
- **summary:** (handler1_pc)(handler2_pc) — scene-coroutine: save the two per-frame yield/resume handler PCs. Native writes op1→ctx[0x6da88+idx*4], op2→ctx[0x6db28+idx*4] (idx=ctx[0x53d14] script-context index) + gfx cmd-type 5. SC0000 0x79: `0x7b label_3c9 label_41e` registers the ADV per-frame render→poll→yield handlers. Part of the scene-coroutine framework (see engine-re.md §Scene-coroutine framework); pairs with 0x7c (resume) + 0x140 (loop iterator).
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra: handler FUN_0041ebf0 (dispatch ctx[0x26c93+0x7b]) = {*(ctx+0x53d88+ctx[0x53d14]*0x78)=5; ctx[0x6da88+idx*4]=op1; ctx[0x6db28+idx*4]=op2}. Both operands are code PCs (handler labels).

### 0x7c `coroutine-resume` (u00416A90, argc 0)
- **summary:** () — scene-coroutine RESUME point. Native requires run-state bit 0x2000000 (ctx[0x6dbc8]) set — THROWS (__CxxThrowException) if unset, so it is only ever reached on a scheduler-driven re-entry, NEVER on a cold first pass (cold flow jmps over it). Restores PC=ctx[0x53d28]+ctx[0x6dbcc]*4, clears the run-bit (ctx+0xa0ce4 &= ~0x2000000), resets input/line state. SC0000 0x443 (falls into the main loop label_444). See engine-re.md §Scene-coroutine framework.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra: handler FUN_00417cb0 (dispatch ctx[0x26c93+0x7c]). Guards on (ctx[0x6dbc8] & 0x2000000)==0 → throw; else restores PC = ctx[0x53d2c-slot] = ctx[0x53d28]+ctx[0x6dbcc]*4, ctx[0xa0ce4]=ctx[0x6dbc8]&0xfdffffff, clears input state (ctx[0x13bdc]/0xc6f8=-1 etc.).

### 0x8f `call` (call, argc 1)
- **summary:** intra-script subroutine call (local JSR): PC = frame.codebase + operand*4; pushes a return address on the per-frame return stack. NOT cross-script (that is call-script 0x03).
- **grounding:** source=investigation, confidence=high
- **evidence:** native-RE (Ghidra): handler FUN_0041fba0 (= ctx[0x26c93+0x8f]) sets [frame PC @+0x53d2c] = [frame codebase @+0x53d28] + operand*4 and pushes ((pc-base)>>2)+3 onto the per-frame return stack ([ctx+0x552e8]/[ctx+0x55248]). Target is a code OFFSET within the current script (matches header table T3 tag 0x8F = local call targets), confirming it is a local JSR, not a script load.

### 0xc8 `sleep` (sleep, argc 1)
- **summary:** Pause the current script for <duration> milliseconds while retained presentation continues.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra: dispatch ctx[0x26c93+0xc8]=0x420ec0; sleep_op_0xc8 + sleep_timer_arm decoded/annotated 2026-07-08. docs/engine-re.md sleep section.

Native handler sleep_op_0xc8 @0x420ec0 is NON-BLOCKING: it arms a timer (sleep_timer_arm @0x44cff0 at ctx+0x5f304 = active flag + start tick + duration) that the engine main loop polls, resuming the script when elapsed. Operand UNIT = MILLISECONDS (start = ms tick source DAT_0056f3d4, timeGetTime/GetTickCount class). duration<10 fast-paths via [0x56f0b8]; all real scene sleeps (100/750/1000) are >=10. The handler also writes gfx cmd-type 3 + runs anti-tamper checks, neither needed host-side. Port equivalent: the Godot host parks the VM thread for duration ms while the presentation compositor continues. Sleep is one proven presentation-capable service boundary; ordinary AE setup runs burst-fast to 0x21c and is not paced per opcode. Headless hosts no-op it (parity).

### 0xd9 `clear-run-state-0x1000` (u00415880, argc 0)
- **summary:** Clear native run/service bit 0x1000; if the secondary context is active, clear the same bit there. SC0000 executes it once after the initial SFX-channel reset, with no VM-visible result.
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **evidence:** Ghidra op 0xd9 handler 0x416da0: ctx->run_state_flags &= ~0x1000; when ctx+0x6f8b8 is nonzero, also clears bit 0x1000 at ctx+0x53d20. No operands, calls, or return value.

### 0x140 `coroutine-label-yield` (u0041F9C0, argc 4)
- **summary:** (out)(name_str)(sub_str)(in) — scene-coroutine LOOP ITERATOR / labeled yield. Handler copies name/sub strings + the int operand and calls the NATIVE video/transition service (*DAT_005c6018)(8, ctx[0x54fe8], &{name,sub,in}); writes the returned PC-like value to operand 1. In SC0000 label_462 'ループ開始' (@0x46d): `out=G[0x6be]=LABEL('J',G[0x6be])`; loop runs the intro-setup body (incl. call label_125bd = slot-table fill G[0x3239..0x324e]=4..11) and jmps back until out==G[0x6c3] (a per-scene exit-PC immediate) → mov aba5c 0 → content. The gate G[0xaba5c]==1 that opens this loop is NATIVE scene-entry state (no script sets it to 1). DAT_005c6018 is runtime-resolved (all xrefs READ) = SAME class as the DirectDraw workers we don't model. PORT = HOST-MODEL IMPLEMENTED: synthesize the ADV scene-entry gate, run the LABEL/J setup body once, then return the structurally discovered per-scene terminal; do not emulate the video service. See engine-re.md §Scene-coroutine framework.
- **grounding:** source=investigation, confidence=med
- **evidence:** Ghidra: handler 0x4299c0 (dispatch ctx[0x9b74c]=0x4299c0; created+typed EngineCtx*+annotated; Kelebek u0041F9C0 = VA-drift). Writes gfx cmd-type 9; op2→local_204, op3→local_104, op4→local_208; (*DAT_005c6018)(8, ctx[0x54fe8], &local_210) → FUN_00425fb0(1,ret). DAT_005c6018: 6 xrefs all READ, no static writer; FUN_00405740 (screen-fade) calls it w/ cmd 3, branches on ret 1/2 = transition progress = native video service.

### 0x1cc `get-adv-read-skip-state` (get-adv-read-skip-state, argc 1)
- **summary:** (out) - copy the current ADV read/click-skip service state from ctx+0x6dbd4. label_1235a ORs it with 0x1c7's Ctrl/message-skip bit: zero takes 0x21c's normal transition/yield path; nonzero resets the animation service and presents the completed endpoint through 0x20c.
- **grounding:** source=investigation, confidence=high
- **depended on by:** 0x20c, 0x20d, 0x21c, 0x223
- **evidence:** Ghidra handler 0x427330 calls vm_operand_write(1, ctx+0x6dbd4). Producer recovered 2026-07-10: adv_update_read_text_skip_state@0x406cd0 and op 0x6e/0x71/0x72 maintain the field from message_ReadTextSkip plus current-PC read-history lookup; adv_interpreter_tick consumes it in click/read-skip control. It is not the 0x223 surface-transition progress flag.

### 0x21c `mark-frame-yield` (mark-frame-yield, argc 0)
- **summary:** Set native run-state bit 0x400; in normal ADV playback this is the retained-presentation render/wait/resume boundary.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x223, 0x1c7, 0x1cc
- **depended on by:** 0x223
- **evidence:** Ghidra handler 0x417520 sets cmd-type 1 and ORs ctx+0xa0ce4 with 0x400. capture_presentation_trace.py: after 0x125a6 render, 0xcb8e/0xcb98 bind and 0xd5a/0xd63/0xd73/0xd8a mode+targets execute without render; repeated gfx_render_frame begins only at 0x21c. 2026-07-10.

SC0000 label_1235a reaches this when 0x1c7/0x1cc are zero. Native run-state bit 0x400 parks the interpreter while gfx_render_frame repeatedly samples finite one-shot object channels and queued surface commands; op 0x224 follows after dirty state clears. Native trace proves AE001D bind, mode-1 0x203, and 0x202 targets complete in one 5 ms batch with no render, then first compose here. The port publishes and waits for visible finite one-shot channels or 0x223 commands; click forcing remains limited to the latter.

## draw

### 0x1a2 `gfx-cmd-register` (gfx-cmd-register, argc 1)
- **summary:** 0x1a2 (value) — gfx cmd-type 3. Handler gfx_op_0x1a2_descriptor_register@0x42d360 builds a key from operand 1's lvalue descriptor and inserts its value into an open-addressing descriptor hash (vm_lvalue_descriptor_hash_insert@0x42cf70). This structure is separate from op 0x215's retained gfx-object map; op 0x215 does not query this hash. NOT save/scene.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra: handler 0x42d360 fetches operand 1's value and lvalue descriptor separately, formats the descriptor key, then calls FUN_0042cf70. By contrast op 0x215 passes ctx+0x46614 to gfx_object_query_source_slot@0x47f280, which searches the retained object map and returns obj+4.

### 0x1f7 `gfx-elem-erase` (gfx-elem-erase, argc 2)
- **summary:** 0x1f7 (handle)(count) — erase retained gfx objects. Handler 0x422270 calls gfx_object_erase_range@0x47d8b0 for [handle,handle+count) when count>1, else gfx_object_erase@0x47d850. This removes entries from the same object map queried by op 0x215, so erased objects stop compositing. SC0000 uses it before op 0x1fa releases the returned surface slot.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x422270; gfx_object_erase_range@0x47d8b0 loops gfx_object_erase@0x47d850. Both operate on owner+0x408, the retained-object map also used by gfx_object_get_or_create/draw and gfx_object_query_source_slot.

### 0x1f8 `create-texture` (create-texture, argc 4)
- **summary:** Allocate/prepare a texture slot: (slot, width, height, flag). e.g. `create-texture 0xd 0x190 0x1e 0x0` = slot 13, 400x30.
- **grounding:** source=investigation, confidence=med
- **evidence:** SC0000 CG/UI-draw path disasm; slot/w/h roles read off the operands (400x30 text bars, etc.).

### 0x1f9 `set-texture` (set-texture, argc 3)
- **summary:** Load asset #resId into texture slot: (resId, slot, flag=-1). resId resolves via the SYS4INI per-scene section manifest: files[section_base(scene)+resId] (same rule for play-bgm/play-voice). See docs/asset-resolution-re.md.
- **grounding:** source=frida, confidence=high
- **evidence:** SC0000 Frida-confirmed 17/17 (0x25->EV052CA, 0x2e->EV052DB, 0x36->BG030A background); resolution rule validated on 586/595 captured loads. Traced in CG-load subroutine label_12649 as `set-texture G[0x62424] <slot> -1`.

### 0x1fa `gfx-elem-release` (gfx-elem-release, argc 1)
- **summary:** 0x1fa (surface_slot) — release the surface at ctx+0x52bd4[slot] (virtual free, then null) and call FUN_00474e40(slot). It releases a surface slot, not a retained object handle. SC0000 feeds it the slot returned by op 0x215 after op 0x1f7 erases the associated object group.
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
- **summary:** 0x202 (handle)(delay_ms)(duration_ms)(alpha)(color) — arm the one-shot packed-ARGB channel. Worker gfx_op_0x202_worker_set_color_anim @0x47ea00 resets shared start obj+0x34, writes delay +0x38, duration +0x4c, and target +0x64. gfx_object_apply_transform_channels @0x472f00 linearly interpolates each byte from current +0x60 on frame clock ctx+0xb550, commits the target, clears timing, writes target -1, and clears the one-shot active bit when all sibling channels finish. Negative alpha/RGB independently preserve current bytes. Implemented in GfxState with synchronized current/target timeline evidence; draw-string 0x204/0x7a remains separate.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4228d0 packs operands 4/5 and calls worker 0x47ea00(handle,delay,duration,packed). Consumer 0x472f00: shared start +0x34; color delay/duration +0x38/+0x4c; current/target +0x60/+0x64; frame clock ctx+0xb550; bytewise integer LERP; natural or ctx+0xb55c forced completion. /v2 annotated and saved 2026-07-10.

### 0x203 `gfx-draw-color` (gfx-draw-color, argc 4)
- **summary:** 0x203 (handle)(mode)(alpha)(color) — gfx cmd-type 9. Worker stores the D3D blend selector at obj+0x30 and STATIC packed color at obj+0x60. Negative alpha/RGB preserve current static bytes. Mode 0 retains established opaque CG/tint/fill behavior; mode 1 is SRCALPHA/INVSRCALPHA with ARGB alpha opacity and multiplicative RGB modulation; mode 2 is the 0x223 transition-source identity path.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4229a0; negative operands read current obj+0x60, then worker 0x47e9b0 stores op2 at obj+0x30 and ARGB at +0x60. gfx_object_composite call-site 0x47f78f passes +0x30/+0x60 directly to gfx_object_blit_d3d9; mode 1 sets D3DRS SRCALPHA/INVSRCALPHA and the packed color is the device draw modulation. Mode 2 transition setup and synchronized pixels prove 0xffffffff is identity, not solid white.

### 0x208 `get-texture-size` (get-texture-size, argc 3)
- **summary:** 0x208 (slot)(out_w)(out_h) — writes the loaded texture's width/height into two output globals; keystone for bytecode-computed sprite/bg geometry (SC0000 label_12649)
- **grounding:** source=inference, confidence=med
- **evidence:** SC0000 label_12649: set-texture(resId,slot) then 0x208(slot)->w,h feeds w/2 horizontal-center + foot-anchor subtraction into draw-texture dst; stubbing yields 0x0 sizes / off-center draws

### 0x20c `present-frame` (present-frame, argc 0)
- **summary:** Present the composited frame; label_1235a uses this on the read/message-skip branch to expose the completed foreground endpoint immediately.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x223, 0x1c7, 0x1cc
- **depended on by:** 0x20d, 0x223
- **evidence:** Ghidra: dispatch table FUN_00413860 param_1[0x26e9f]=gfx_op_0x20c_present_frame; 0x26e9f-0x26c93=0x20c. 2026-07-08.

Native handler gfx_op_0x20c_present_frame -> gfx_render_frame @0x4820b0. This is an explicit retained-state publication boundary, not a continuously visible object-store mutation. The read/message-skip branch resets the animation service then presents; the port publishes and snaps pending 0x223 state here. Normal playback branches to 0x21c, which owns repeated render/wait/resume. Headless hosts remain non-blocking.

### 0x212 `set-gfx-field64` (set-gfx-field64, argc 2)
- **summary:** 0x212 (obj_idx)(val) — gfx cmd-type 5. Handler gfx_op_0x212_set_field64 @0x4230c0: obj=[ctx+0x14d54 + obj_idx*4]; if obj: *(obj+0x64)=val. Sets one per-object field. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4230c0 (dispatch ctx[0x26c93+0x212]); writes [obj+0x64]=operand2, obj from ctx+0x14d54[operand1*4].

### 0x213 `set-gfx-xy` (set-gfx-xy, argc 3)
- **summary:** 0x213 (obj_idx)(x)(y) — gfx cmd-type 7. Handler gfx_op_0x213_set_field68_6c @0x423110: obj=[ctx+0x14d54 + obj_idx*4]; if obj: *(obj+0x68)=x; *(obj+0x6c)=y (an (x,y) pair). See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x423110; writes obj+0x68/+0x6c from operands 2/3, obj from ctx+0x14d54[operand1*4].

### 0x215 `query-gfx-object?` (query-gfx-object?, argc 2)
- **summary:** 0x215 (out_slot)(handle) — query the retained gfx-object map. Handler gfx_op_0x215_query_source_slot@0x42a0b0 calls gfx_object_query_source_slot@0x47f280 with owner ctx+0x46614. The worker searches owner+0x408, the same map populated by geometry/draw workers, and returns obj+4: the live source-surface slot written by draw-texture, or -1 if absent. A geometry-only object remains unbound and returns -1. SC0000 uses a successful result for existing-object geometry and query-guarded teardown: op 0x1f7 erases the object group and op 0x1fa releases this slot. VM fix 2026-07-09 restores AE001H magic-circle cleanup.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra asm: handler 0x42a0b0 passes ECX=ctx+0x46614 to 0x47f280. That worker searches ECX+0x408 and returns resolved object+4. gfx_object_bind_draw@0x47e870, called with the same owner, get-or-creates in owner+0x408 and writes source slot to object+4. SC0000 post-effect cleanup 0x3321 queries G[0x62457], sign-tests, then executes 0x1f7(handle,10) + 0x1fa(returned_slot); AE001H res 0x37 was bound to that handle.

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

### 0x21d `clone-gfx-object` (clone-gfx-object, argc 2)
- **summary:** (source_handle)(destination_handle) - clone the complete retained gfx object record (0xb5 dwords / 0x2d4 bytes). SC0000 uses destination=source+1 as range A's old-frame snapshot immediately before 0x223 crossfades range B's updated source into target handle source+2.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x223
- **evidence:** Ghidra handler 0x423310 -> gfx_object_clone@0x47e4f0. Worker requires source in ctx+0x408 map, creates destination, copies exactly 0xb5 dwords from source object record, then marks ctx+0xb558 dirty. SC0000 site 0x128fc passes current handle and handle+1 before queueing 0x223 at 0x129e7.

### 0x21e `set-anim-transform-norm` (set-anim-transform-norm, argc 6)
- **summary:** (handle)(delay_ms)(duration_ms)(sx)(sy)(sz) — set the normalized SCALE-matrix channel (100=identity). Target obj+0xac is linearly sampled from current obj+0x6c by gfx_object_apply_transform_channels@0x472f00 on frame-time ctx+0xb550, after delay and for duration, then committed. Shares only start timestamp obj+0x34 with op 0x220; neither Z is opacity.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra 0x47eaa0 calls matrix builder 0x48af1d for target obj+0xac. Consumer 0x472f00 uses delay obj+0x3c, duration obj+0x50, current obj+0x6c, target obj+0xac, shared start obj+0x34, and frame-time ctx+0xb550.

### 0x21f `set-anim-rotation-axis-angle` (set-anim-rotation-axis-angle, argc 7)
- **summary:** (handle)(delay_ms)(duration_ms)(axis_x)(axis_y)(axis_z)(angle_deg) — set the delayed one-shot axis-angle rotation channel. Handler converts axis/angle integers to floats; worker stores target axis obj+0x1f8 and angle obj+0x208 and builds target matrix obj+0x12c. gfx_object_apply_transform_channels samples current axis/angle linearly on shared start obj+0x34 and composes T(-anchor)*scale*rotation*translation*T(anchor).
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x423410 -> gfx_object_set_rotation_channel@0x47eb70; consumer gfx_object_apply_transform_channels@0x472f00 uses delay +0x40, duration +0x54, current axis +0x1ec/angle +0x204, target axis +0x1f8/angle +0x208, current matrix +0xec and target +0x12c. Native SC0000 handle 0xcb8e sample at 11/390 of axis (0,0,1), 30deg matches matrix [0.9055,0.0134;-0.0134,0.9055] and translation (74.1449,47.3127).

### 0x220 `set-anim-transform-abs` (set-anim-transform-abs, argc 6)
- **summary:** (handle)(delay_ms)(duration_ms)(tx)(ty)(tz) — set the absolute TRANSLATION-matrix channel. Target obj+0x1ac is linearly sampled from current obj+0x16c by gfx_object_apply_transform_channels@0x472f00 on frame-time ctx+0xb550, after delay and for duration, then committed. Independent of op 0x21e scale; neither Z is opacity.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra 0x47ecc0 calls matrix builder 0x48afb1 for target obj+0x1ac. Consumer 0x472f00 uses delay obj+0x44, duration obj+0x58, current obj+0x16c, target obj+0x1ac, shared start obj+0x34, and frame-time ctx+0xb550.

### 0x223 `queue-surface-alpha-transition` (queue-surface-alpha-transition, argc 8)
- **summary:** (command_key)(target_slot)(range_a_start)(range_a_count)(range_b_start)(range_b_count)(delay_ms)(duration_ms) — queue a type-0 timed alpha transition command in the separate ctx+0x414 command map. This is render-target/surface presentation state, not an object affine matrix. The render frame composites the two handle ranges into target_slot and ramps alpha 0->1 after delay over duration.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x20c, 0x21c, 0x1c7, 0x1cc
- **depended on by:** 0x20c, 0x21c, 0x21d
- **evidence:** Ghidra handler 0x423620 -> gfx_queue_surface_alpha_transition@0x47f440. Record fields: type +0=0, start +4=0, delay +8=arg7, duration +0xc=arg8, slot +0x10=arg2, range A +0x14/+0x1c=args3/4, range B +0x18/+0x20=args5/6. gfx_render_frame@0x47fbc0 initializes start from ctx+0xb550 and consumes type 0 as an alpha ramp. SC0000 executes one shared-helper site at 0x129e7.

### 0x224 `clear-gfx-command-queue` (clear-gfx-command-queue, argc 0)
- **summary:** Clear the native gfx command queue rooted at ctx+0x418. Host-implicit because the port composites retained state directly.
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **evidence:** Ghidra handler 0x417550 -> gfx_command_queue_clear 0x47cb10, which destroys queued nodes and restores the sentinel links/count.

### 0x228 `u00421940` (u00421940, argc 5)
- **summary:** 0x228 query-position (succ)(handle)(outX)(outY)(outZ): read the object's current computed position into vars (worker FUN_0047cdd0). C# VM: writes V24 + success flag. See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=kelebek, confidence=low

### 0x229 `u004219E0` (u004219E0, argc 5)
- **summary:** 0x229 set-position2 (handle)(op2)(x)(y)(z): set object position/geometry directly (FUN_00472bb0/be0). C# VM: sets V24. See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=kelebek, confidence=low

### 0x22f `u00421DD0` (u00421DD0, argc 5)
- **summary:** 0x22f set-position (handle)(op2)(x)(y)(z): set the object base position (direct transform, not ping-pong). Worker gfx_worker_set_translation @0x472e90. C# VM: sets V24. See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=kelebek, confidence=low

### 0x231 `u00421EA0` (u00421EA0, argc 4)
- **summary:** 0x231 anim-srcrect (handle)(period)(gridW)(gridH): ping-pong the spritesheet cell across the grid over period ms. Worker gfx_worker_anim_srcrect @0x47eec0 -> interpolator SRC-RECT SCROLL channel. C# VM: GfxState.SetSrcRect. See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=kelebek, confidence=low

### 0x232 `u00421EF0` (u00421EF0, argc 4)
- **summary:** 0x232 anim-color (handle)(period)(alpha)(color): ping-pong the object color/alpha toward the packed target over period ms (pulsing GLOW). Worker gfx_worker_anim_color @0x47ef50 -> interpolator COLOR channel. C# VM: GfxState.SetColorAnim. See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=kelebek, confidence=low

### 0x234 `anim-start` (anim-start, argc 5)
- **summary:** (handle)(period_ms)(axis_x)(axis_y)(axis_z) — configure cyclic rotation. Worker stores period obj+0x228, start obj+0x214=0, and float axis obj+0x244; each frame uses integer degrees floor(((now-start)%period)*360/period). gfx_object_composite right-multiplies this separately anchored transform after the one-shot scale/rotation/translation product, so cyclic rotation also rotates the translation vector.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x423da0 converts axis ints to floats -> worker 0x47f060. gfx_object_anim_interpolate@0x473ed0 consumes obj+0x228/+0x214/+0x244 on ctx+0xb550 and matrix4_make_axis_angle@0x48b215. gfx_object_composite@0x47f650 calls one-shot transform first, cyclic animation second.

### 0x236 `play-movie-to-surface` (play-movie-to-surface, argc 4)
- **summary:** (resource_id)(surface_slot)(movie_flags)(sync_mask) - synchronously resolve/open an archive movie and construct its DirectShow graph, then start asynchronous frame delivery into the retained destination surface. The opcode itself is non-blocking: the VM advances to the next instruction. SC0000's native site evaluates (0x33, 0, 2, 0) at 0x13c8 and resumes at bytecode 0x13d1; its later 0x21c service boundary yields until movie EOF before cleanup.
- **grounding:** source=investigation, confidence=high
- **evidence:** Native handler 0x423ee0 and helpers 0x463c50/0x463aa0/0x463e20/0x4625e0; SC0000 native operand capture; exact 0x13c8->0x13d1 trace; archive-only changing-frame decoder and Godot lifecycle tests.

The handler requires an existing destination texture, allocates/reuses a 0x478-byte movie-to-texture object for the surface, opens operand 1 through the native indexed-asset reader, builds FilterGraph/IGraphBuilder/IMediaControl/IMediaPosition/IMediaEvent/IBasicAudio, and presents bottom-up RGB samples through the movie texture renderer. Operand 3 selects movie/sound routing policy: bits 0x10000/0x20000/0x40000/0x80000 force sound route 0/1/2/3, otherwise set:DependMovieSound is used; SC0000's low value 2 is retained as native movie mode state. Operand 4 is stored as the movie sync/device mask at object+0x42c; SC0000 passes 0. Static layer preparation after 0x236 does not terminate the retained movie; 0x21c services it through EOF and subsequent surface cleanup stops/detaches it.

### 0x238 `set-anim-clock` (set-anim-clock, argc 1)
- **summary:** (duration) — set the GLOBAL animation clock: native ctx+0x51b78=0 (elapsed), +0x51b7c=duration. cmd-type 3. NON-BLOCKING: only configures; the render loop advances it and interpolates all animating objects. SC0000 opening @0x123bd/@0x13858. Handler 0x4240e0; Kelebek VA 0x422390 is drift.
- **grounding:** source=investigation, confidence=high

### 0x239 `u004223C0` (u004223C0, argc 6)
- **summary:** 0x239 set-srcrect-cell (handle)(p3)(p4)(gridW)(gridH)(cell): set the spritesheet grid + static visible cell. Worker gfx_worker_set_srcrect_cell @0x47ed90. C# VM: GfxState.SetSrcRect (period 0). See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=kelebek, confidence=low

### 0x23f `u00422930` (u00422930, argc 2)
- **summary:** 0x23f query-object (out)(handle): return object status (FUN_0042a520; -1 if none). C# VM: 0 if the object exists else -1. See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=kelebek, confidence=low

### 0x243 `reset-anim-clock` (reset-anim-clock, argc 0)
- **summary:** Reset the native global animation-service elapsed and duration fields to zero when service flag bit 1 is clear.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4182d0: if !(ctx+0x51b80 & 2), set ctx+0x51b70=1 and zero ctx+0x51b78/+0x51b7c. Normal SC0000 label_1235a calls it before present-frame.

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

### 0x1c7 `get-message-skip` (get-message-skip, argc 1)
- **summary:** (out) - write 1 iff ADV message-skip run-state bit 0x08000000 is set, otherwise 0.
- **grounding:** source=investigation, confidence=high
- **depended on by:** 0x20c, 0x20d, 0x21c, 0x223
- **evidence:** Ghidra handler 0x4272b0 reads ctx+0xa0ce4 bit 0x08000000 and vm_operand_write(1, 1|0). SC0000 label_1235a ORs it with op 0x1cc.

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

### 0x1c8 `toString` (toString, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x1ca `u0041B9B0` (u0041B9B0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1cb `u00414FD0` (u00414FD0, argc 1)
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
- **depends on:** 0x20c, 0x1c7, 0x1cc

### 0x20e `u00416250` (u00416250, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20f `u00420E40` (u00420E40, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x222 `u004216C0` (u004216C0, argc 2)
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

### 0x230 `u00421E70` (u00421E70, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x233 `u00421FB0` (u00421FB0, argc 5)
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

### 0x241 `u00422B80` (u00422B80, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x242 `u00422D60` (u00422D60, argc 2)
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

