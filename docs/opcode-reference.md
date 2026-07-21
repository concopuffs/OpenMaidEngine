<!-- DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build -->
# Opcode Reference (generated)

248 opcodes used by Himegari. Source of truth: `vm-map/opcodes.toml`.

## adv

### 0x70 `define-adv-text-layout` (define-adv-text-layout, argc 5)
- **summary:** (layout_slot)(width)(height)(x)(y) - configure an ADV text layout/surface and, when history recording is enabled, append its logical history index and arm the next retained record as a group start.
- **grounding:** source=investigation, confidence=high
- **depended on by:** 0x79, 0x1c1
- **evidence:** Ghidra /v2: op_0x70_handler@0x41e4e0 forwards the five operands and ctx+0x55110 to adv_text_layout_define@0x4550e0. The worker stores width/height/origin, creates or resizes surface slot layout+0x14, appends {layout_slot,current_record_count} to the 8-byte history index unless suppression bit31 is set, and arms manager+0xd40[slot] so the next 0x48-byte record receives group-start flag bit0.

### 0x71 `reset-adv-text-layout` (reset-adv-text-layout, argc 1)
- **summary:** (layout_slot) - clear/reset an ADV text layout, append a retained-history boundary when recording is enabled, snapshot the current code/text position, and commit pending ReadTextDB records. T1 entries target these structural reset sites.
- **grounding:** source=investigation, confidence=high
- **depended on by:** 0x79, 0x1c1
- **evidence:** Corpus: T1 targets op-0x71 records, but the operand is a layout slot (SC0000 uses 1; HISTORY computes 2..6), not an anchor id. Ghidra /v2: op_0x71_handler@0x41e540 calls adv_text_layout_reset@0x455210 with ctx+0x55110; the worker clears the selected layout and appends {slot,current_record_count}/arms group-start unless suppressed. The handler also snapshots (frame_pc-frame_codebase)/4 and text state and calls read_text_db_commit_pending@0x46ae20.

### 0x72 `wait-for-input` (wait-for-input, argc 1)
- **summary:** (layout_slot) - arm the ADV input wait after text reveal completes; activate the configured wait indicator and, while Auto is enabled, arm the appropriate Auto-message timer.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x72_handler@0x41e690 fetches operand 1 and calls FUN_00453120(text_manager, layout_slot, -1, &state), then sets the input-wait run-state flags. FUN_00453120 resolves layout slot 0 as current and consumes the indicator descriptor at layout+0x3c configured by op 0x73. SYSTEM4 layout 1 uses SO000's bat strip; the click that completes show-text is consumed before this opcode is reached. The handler also checks ctx+0x55104 (Auto enabled): when ctx+0x6dbe4 has no pending voice it arms the timer with message:AutoMessageTime1, substituting 100 ms for configuration value zero. adv_input_service_poll@0x411230 waits for an active voice to finish and then arms AutoMessageTime0, likewise with a 100-ms zero fallback. The same click/Auto completion path calls read_text_db_queue_message@0x469340 with the current script id, resolved per-script message index, and message count; an already-skipped wait queues it directly in op 0x72. Op 0x71 later commits the pending records.

### 0x73 `configure-adv-wait-indicator` (configure-adv-wait-indicator, argc 10)
- **summary:** (layout_slot)(dst_x)(dst_y)(surface_slot)(src_x)(src_y)(cell_w)(cell_h)(terminal_frame)(frame_period_ms) - configure the animated marker shown while the selected ADV layout waits for input.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x73_configure_wait_indicator@0x41e900 passes operands 1..9 to adv_text_configure_wait_indicator@0x44ff60 and operand 10 to adv_indicator_set_frame_period@0x44d060. The worker writes surface/source rect, layout-relative destination, enabled=1, and terminal/column values to the selected layout at +0x3c..+0x60. SYSTEM4 executes `set-texture 0x337c 0xc 0xff00` (raw id 0x337c = SO000.AGF, a 390x27 strip of thirteen 30x27 bat frames), then `0x73 1 385 140 12 0 0 30 27 12 48`; layout 1 begins at y=430, placing the 30x27 marker at screen (385,570), matching the original. Op 0x72 activates this descriptor only after reveal completion.

### 0x75 `set-font-size` (set-font-size, argc 1)
- **summary:** (pixels) - set the primary text font height and rebuild its native rasterization state.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x75_handler@0x41ea10 calls text_set_primary_font_size@0x415bd0 on ctx+0x14940. The worker writes the negated pixel height to both primary LOGFONT states, derives width, and rebuilds font resources. HISTORY.BIN selects 22 pixels while active and restores 24 on exit.

### 0x76 `set-text-color` (set-text-color, argc 1)
- **summary:** (rgb) - set the current primary text color.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x76_handler@0x41ea50 byte-swaps operand RGB into text-manager+0x54c and rebuilds text raster state. Retained 0x48-byte history records copy this field to record+0x24; HISTORY.BIN uses white.

### 0x77 `set-text-effect-color` (set-text-effect-color, argc 1)
- **summary:** (rgb) - set the secondary outline/shadow color used by the current text raster mode.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x77_handler@0x41eab0 byte-swaps operand RGB into text-manager+0x550 and rebuilds text raster state. The rasterizer consumes it beside the primary color and retained history copies it to record+0x1c; HISTORY.BIN uses dark 0x333e52 while active.

### 0x78 `set-text-render-mode` (set-text-render-mode, argc 1)
- **summary:** (mode) - select the current text raster/effect mode.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x78_handler@0x41eb10 writes operand 1 to text-manager+0x558 and rebuilds font state. Direct draw and text_history_render_records branch on this field (including a distinct mode-3 offset path). HISTORY.BIN selects mode 3 and restores mode 3 with the normal ADV font preset.

### 0x79 `set-adv-text-reset-cursor` (set-adv-text-reset-cursor, argc 3)
- **summary:** (layout_slot)(x)(y) - configure the cursor coordinates restored by a later reset of the selected ADV text layout. Slot 0 selects the current layout. This does not move the live cursor immediately; opcode 0x7a does that.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x70, 0x71
- **evidence:** Ghidra /v2: op_0x79_set_adv_text_reset_cursor@0x41eb50 reads three operands and calls adv_text_layout_set_reset_cursor@0x44fed0. Slot 0 resolves through the manager current-layout field at +0x4c8; the selected layout pointer is manager+0x414[slot], and x/y are stored at layout +0x1c/+0x20. adv_text_layout_reset_cursor_and_bounds_record@0x455070 later copies those configured values into the initial retained record on layout reset. SYSTEM4 has all nine corpus sites, paired with 0x1c1 after defining/resetting layouts 1..9; slot 1 configures (100,47), layouts 2..6 configure (45,42), layouts 7/8 (53,10), and layout 9 (10,10). Port implementation stores the configured cursor separately from the live cursor, restores it on 0x71, and replays the SYSTEM4 configuration in the direct-scene bootstrap.

### 0x7a `set-adv-text-cursor` (set-adv-text-cursor, argc 3)
- **summary:** (layout_slot)(x)(y) - set the cursor in the selected ADV text layout's last 20-byte record. Slot 0 selects the current layout.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x7a_handler@0x41eba0 fetches operands 3,2,1 and calls adv_text_set_cursor@0x4530f0 on text manager ctx+0x14940. Slot 0 resolves manager+0x4c8; manager+0x414[slot] selects the layout; text_layout_set_cursor@0x452530 writes x/y to +4/+8 of its last 0x14-byte record. SC0000 0x9d3 computes slot 1, x=75, y=47 before voiced show-text.

### 0x85 `clear-text-history` (clear-text-history, argc 0)
- **summary:** Clear both the retained ADV text-record vector and its logical layout/message index.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x85_handler@0x4163d0 calls text_history_clear@0x455d70 on the text manager. The worker destroys the 0x48-byte record vector and clears the 8-byte logical index vector. Corpus: two sites in each of 143 normal ADV scripts bracket the retained backlog lifetime.

### 0x8b `set-text-line-spacing` (u0041B3D0, argc 1)
- **summary:** (pixels) - set the extra pixel spacing added between text lines.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x8b_set_text_line_spacing@0x41f270 stores operand 1 at text manager +0x560 (ctx+0x14ea0). adv_text_manager_initialize@0x456800 defaults it to 6. Horizontal newline/history paths at 0x456fd0/0x452970 advance y by (+0x560 - primary LOGFONT.lfHeight); lfHeight is negative, so this is font pixel height plus configured leading. Corpus arguments are 8 for 22/24px Mincho blocks and 9 for 16px Gothic blocks.

### 0x197 `set-ruby-font-size` (set-ruby-font-size, argc 1)
- **summary:** (pixels) - set the secondary/ruby text font height and rebuild its native rasterization state.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x197_handler@0x41f3b0 calls the parallel secondary-font size worker at 0x415c40 on ctx+0x14940, which updates the second LOGFONT pair and rebuilds it. UI font preset sequences consistently set primary size through 0x75 and the smaller ruby size through 0x197; HISTORY.BIN uses 22/8.

### 0x198 `set-adv-text-layout-origin` (set-adv-text-layout-origin, argc 3)
- **summary:** (layout_slot)(x)(y) - set the selected ADV text layout's presentation origin; slot 0 selects the current layout.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x198_handler@0x41f3f0 passes all three operands to adv_text_layout_set_origin@0x44ff30 on ctx+0x14940. The worker resolves slot 0 to current and writes x/y to layout+0x0c/+0x10. HISTORY.BIN positions its dynamically selected backlog layout with this opcode.

### 0x1a4 `set-text-effect-offset` (set-text-effect-offset, argc 2)
- **summary:** (x)(y) - set the horizontal and vertical extent/offset used by the current text effect mode.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1a4_handler@0x41f440 writes operands 1/2 to text-manager+0x564/+0x568. Native raster/bounds paths expand or shift text by these values when an effect mode is active; mode 3 subtracts both. HISTORY.BIN uses (1,1).

### 0x1bb `set-text-history-recording` (set-text-history-recording, argc 1)
- **summary:** (enabled) - enable or suppress retained ADV text-history recording. Zero suppresses recording; one enables it.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1bb_handler@0x41f650 writes 0x80000000 to ctx+0x55110 for operand 0 and zero for operand 1, rejecting other values. Text/layout/metadata/voice paths test or propagate that high bit before appending retained history. HISTORY.BIN disables recording at entry and reenables it at exit so the backlog UI does not record itself.

### 0x1c1 `set-adv-text-bounds` (set-adv-text-bounds, argc 3)
- **summary:** (layout_slot)(right)(bottom) - configure the selected ADV text layout's right and bottom overflow boundaries in layout-local coordinates. Slot 0 selects the current layout.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x70, 0x71
- **evidence:** Ghidra /v2: op_0x1c1_set_adv_text_bounds@0x41f6c0 reads three operands and calls adv_text_layout_set_bounds@0x44ff00. That helper stores right/bottom at layout +0x24/+0x28; adv_text_layout_reset_cursor_and_bounds_record@0x455070 copies them into the initial retained record, and adv_text_layout_check_overflow@0x45efc0 compares glyph end coordinates against them. SYSTEM4 has all nine sites, paired with 0x79. Slot 1 computes (720,147), layouts 2..6 use (645,135), layout 7 (495,180), layout 8 (495,60), and layout 9 (250,368), proving these are layout-local boundary coordinates rather than width/height deltas. Port implementation retains the configured bounds in engine-owned layout snapshots and sizes ordinary/history Godot labels from right-cursor_x and bottom-cursor_y.

### 0x1ce `set-adv-wait-indicator-enabled` (u0041B9F0, argc 1)
- **summary:** (enabled) - explicitly start or stop the animated ADV input-wait indicator service.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra op_0x1ce_set_adv_wait_indicator_enabled@0x41f8c0 writes enabled to ctx+0x5f734. A nonzero value sets run bit 0x40000000, resets ctx+0x5f72c to frame zero, and starts the ctx+0x5f64c elapsed timer. Zero erases the indicator through adv_text_publish_wait_indicator_frame@0x453120 using ctx+0x6da84 and frame -2 when necessary, then clears the run bit. The corpus uses 0x1ce(0) on modal/menu entry, including HISTORY.BIN@0x3.

### 0x1d0 `step-text-history` (step-text-history, argc 3)
- **summary:** (out_layout_slot)(out_record_index)(delta) - resolve a cumulative delta from the latest retained ADV boundary and return its layout slot and first record index, or -1 outputs at a boundary.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1d0_step_text_history@0x427360 calls text_history_step_group@0x4537c0 with operand 3 and mode mask 2, then writes the index entry's first field (layout slot) and second field (record index). Layout define/reset set manager+0xd6c to the newest entry; the helper reads but does not mutate that anchor, so deltas are cumulative. It skips duplicate record offsets and record-flag bit1, and returns -1 outputs at a boundary. HISTORY.BIN uses negative cumulative deltas to count/page backward.

### 0x1d1 `render-text-history` (u0041BAE0, argc 5)
- **summary:** (layout_slot)(record_index)(flags)(color_a)(color_b) - render retained ADV text records into a selected text layout/surface.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1d1_render_text_history@0x41f950 forwards all five operands plus the text manager at ctx+0x14508 to text_history_render_records@0x4526c0. That helper walks 0x48-byte retained text records, applies record flags/colors/font state, measures strings, and rasterizes/binds them to the chosen layout. HISTORY.BIN uses it to draw each visible backlog line.

### 0x1d2 `append-text-history-metadata` (append-text-history-metadata, argc 2)
- **summary:** (metadata_type)(value) - append a typed metadata record to the current retained ADV message group when history recording is enabled.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1d2_append_text_history_metadata@0x41f9c0 tests ctx+0x55110, fetches operand 2 followed by operand 1, and calls text_history_append_typed_metadata@0x455f00 as (layout=0, type=operand1, value=operand2). The helper appends a 0x48-byte record with flag 0x20000000, value at +0x14, and type at +0x18, consuming the pending group-start flag if set. SC0000's voiced page at 0x96d writes (type=2, value=0x11), and HISTORY.BIN later queries type 2 before replay. The corpus has 17,323 uses. This opcode is effectful, not a statement marker.

### 0x1d3 `find-text-history-value` (find-text-history-value, argc 5)
- **summary:** (out_found)(out_value)(direction)(record_index)(value_type) - find typed metadata within one retained ADV message group.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1d3_find_text_history_value@0x4273c0 calls text_history_find_typed_value@0x450840 and writes its boolean result plus returned value. The helper scans forward from operand 4 until the next group-start bit for flag 0x20000000 and matching type in record+0x18, returning the last match's record+0x14. Operand 3 reaches an unused helper parameter. HISTORY.BIN passes 1 and queries types 1 and 2 for line decoration/name metadata.

### 0x1d4 `find-text-history-pair` (find-text-history-pair, argc 4)
- **summary:** (out_a)(out_b)(direction)(record_index) - find paired metadata within one retained ADV message group.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1d4_find_text_history_pair@0x427430 calls text_history_find_pair@0x4509f0 and writes two outputs. The helper scans forward from operand 4 until the next group-start bit and returns the last 0x40000000 record's +0x14/+0x18 pair, defaulting both outputs to -1. Operand 3 reaches an unused helper parameter. HISTORY.BIN passes 1 and uses the pair to expose voice replay for a backlog entry.

### 0x204 `draw-string` (draw-string, argc 4)
- **summary:** (surface_slot)(x)(y)(string) - rasterize a CP932 string immediately into a numbered graphics surface using current font/color/effect state.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x204_handler@0x422a60 resolves operand 4 as a string, fetches surface/x/y, then calls draw_string_to_surface@0x450150 on text manager ctx+0x14940. The worker validates and locks gfx-manager surface table +0xa590[slot], chooses text_raster_string_uncached@0x459d90 or cached/effect path @0x45b600, rasterizes GDI GetGlyphOutlineA bitmaps through text_blit_glyph_bitmap@0x458c80 using font/color state +0x4d0/+0x458, then unlocks. SC0000 0x9b2 draws the speaker name into 400x30 surface 0xd at (1,1); following 0x1fb binds it to retained object 0xe678 at (74,444).

### 0x20a `publish-adv-text-layout` (u00420CE0, argc 1)
- **summary:** (layout_slot) - republish one ADV text layout and, while active, its current wait-indicator frame.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra op_0x20a_publish_adv_text_layout@0x422ce0 passes the requested slot to adv_text_publish_layout@0x450c80, which rebinds the layout's retained text records. If ADV run-state bit 0x40000000 is active, it also calls adv_text_publish_wait_indicator_frame@0x453120 with ctx+0x5f72c. Slot zero resolves the text manager's current layout; the corpus always uses slot one, including HISTORY.BIN@0x13ab in its shared ADV redraw callback.

### 0x2bd `set-font-bold` (set-font-bold, argc 1)
- **summary:** (enabled) - set the current primary text font weight to 700 when enabled or 0 when disabled, then rebuild the native font state.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x2bd_handler@0x4251c0 writes enabled?700:0 to LOGFONT weight at text-manager+0x4dc and calls the font rebuild worker. It immediately follows set-font throughout the UI corpus; HISTORY.BIN selects the Mincho face with bold enabled.

## audio

### 0xb4 `sfx-load` (play-sound-effect, argc 2)
- **summary:** (packed_raw_resource_id)(channel) — synchronously open the universal SYS4INI/AAI catalog entry and replace the channel's decoded sound buffer without starting playback. A zero high byte is a raw SYS4INI index; a nonzero high byte selects an append catalog and uses the low 24-bit index. Native manager supports channels 0..12; SC0000 uses 0..9.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xb4_sfx_load@0x4201d0 -> sfx_channel_load@0x482500 -> asset_open_indexed_entry@0x44f390 + sound_decode_channel@0x483360. The opener indexes the base catalog directly for a zero high byte and selects mounted_aai[high_byte] otherwise. SC0000 0xc29 uses raw 0x28 = E0808.WAV. TITLE independently proves the addressing rule: raw 0x2aea = SE020.WAV is preloaded on channel 2 and started on every hover-selection change; raw 0x3321 = SE015.WAV is loaded/started on activation. The pre-fix port trace reported file=null under scene-manifest resolution; after switching Godot to ResolveSoundEffect/ResolvePacked, a synchronized TITLE->GAMESTART->TITLE trace resolves and starts SE020/SE015/SE013, confirmed audible by the user.

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
- **summary:** Play a voice clip by id with native playback/history variant 0. SC scripts resolve through their SYS4INI section manifest; frontend scripts without an SC section use the id as a universal raw-catalog index. While all-message Skip is active, retain/replace the queued voice request instead of starting it; playback resumes from the latest queued request after Skip clears. Same scene-first/raw-fallback resolver family as set-texture (NOT play-bgm, which is direct-name BGM{id:03d}).
- **grounding:** source=investigation, confidence=high
- **evidence:** By-ear confirmed (2026-07-06): SC0000 prologue voices play on their lines via Godot AudioStreamPlayer. Off-by-one disproven structurally: manifest interleaves graphics/voice (files[35]=EV049AA, [36]=MAN999, [37]=EV052CA, [38]=SYL0001), so files[base+id] lands voices on OGGs while files[base+id-1] would land them on .AGF graphics (silent) -- and they play, so the SC-section offset is exactly 0. ROOM supplies raw ids 0x3365..0x3376 from a non-SC frontend script; raw 0x3365 is EUA0016.OGG. A scene-only resolver returned null there despite the opcode executing, while the established texture path already required the same raw fallback for frontend resources. Lily's lines are correctly form-gated (G[0xa57/0xa58/0xa59]) and stay silent when no form flag is seeded -- not a bug. Ghidra /v2 op_0xc4_handler@0x420610: when run_state_flags bit 0x08000000 is clear it calls voice_play_indexed_asset@0x488330 with variant 0 and records pair {id,0}; while Skip is active it stores the latest id/zero variant at ctx+0x6dbf4/+0x6dbf8. adv_interpreter_tick starts and clears that deferred voice when Skip/read-skip input is no longer active.

### 0x1bd `play-history-voice` (u0041D910, argc 1)
- **summary:** Replay a voice id selected from retained ADV text history using native playback/history variant 1, preserving normal Skip and Auto-voice state behavior.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1bd_play_history_voice@0x420920 stops/replaces the active voice and calls voice_play_indexed_asset@0x488330 with operand 1 plus playback variant 1 when Skip is inactive (or queues that pair while Skip is active). When text-history recording is enabled it appends voice pair {id,1}; HISTORY.BIN suppresses recording with op 0x1bb while its UI is active, preventing replay clicks from recursively entering the backlog. The handler also sets adv_auto_voice_pending when playback exists. voice_play_indexed_asset retains the variant in sound-buffer state before starting channel 12; its exact audible meaning remains unproven.

### 0x1cf `set-voice-bgm-duck-control` (set-voice-bgm-duck-control, argc 1)
- **summary:** (flags) - replace the transient voice/BGM-duck control mask. Bit 0 suppresses automatic BGM attenuation when voice playback starts; zero permits the configured duck behavior.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1cf_set_voice_bgm_duck_control@0x4209f0 fetches operand 1 and writes it verbatim to ctx+0x6dbf0. The voice-start helper voice_bgm_duck_begin@0x406de0 tests bit 0 before arming BGM attenuation; native settings defaults register MusicFadeOnVoicePlaying=1 and MusicFadeOnVoicePlayingVol=50. SC0000 uses values 0 and 1.

### 0x2bf `schedule-sfx-start` (schedule-sfx-start, argc 3)
- **summary:** (channel)(start_mode)(delay_ms) - schedule the already-loaded SFX channel to enter the ordinary start worker after the requested delay. Channels are limited to 0..9.
- **grounding:** source=frida, confidence=high
- **evidence:** Ghidra /v2: op_0x2bf_schedule_sfx_start@0x425240 calls sfx_set_delay@0x482720 on the ctx+0x14024 sound facade. The worker's native error text names Function:SetDelay, validates channel<=9, and stores active/progress/delay/start-mode state. Existing SC0000 trace: SetDelay(channel 4, mode 0, 100) is followed about 109 ms later by the ordinary sfx_channel_start worker on channel 4 with mode 0 and no intervening 0xb5.

## compute

### 0x60 `random-modulo` (u0041A270, argc 2)
- **summary:** (destination)(bound) — write CRT rand() % bound. A bound of zero first writes zero and then raises the engine's script error.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x60_handler@0x426970 calls imported CRT rand(), fetches operand 2, takes signed remainder, and writes operand 1. The zero-bound branch writes zero then throws error 0x10005. ROOM@0x5 calls (local 0x110,4), and its following branches select one of four character surface sets; variants 0..2 also select voice-id sextets. /v2 annotated and saved 2026-07-21.

### 0x61 `lookup-array` (lookup-array, argc 3)
- **summary:** Take a typed reference to base[index], preserving whether the base belongs to local or global storage.
- **grounding:** source=investigation, confidence=high
- **evidence:** HISTORY.BIN copies x/y tables into local-int cells 0x4 and 0x68, then lookup-array local-ptr <- local-int base supplies every right-side button and hovered-row draw coordinate. Treating the local operand's current value as a global base collapses those draws to (0,0); retaining the local address yields the native x=768/y=121..549 positions. RECOVER.BIN independently exercises the same pointer destination with global bases.

Operand 2 names the base cell itself: a global-bank operand produces a global reference and a local-bank operand produces a local reference. Operand 3 is added as the element offset. Pointer destinations retain that address domain; reading or writing the pointer dereferences the corresponding bank. Non-pointer destinations receive the addressed value. The same domain-preserving address model applies to lookup-array-2d (0x12c).

### 0x63 `take-address` (take-address, argc 2)
- **summary:** (destination_pointer)(source) - store the underlying typed storage address of source in destination_pointer; a pointer source aliases its existing target.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2 op_0x63_take_address@0x426ac0 calls vm_operand_resolve_address@0x425a50 for operand 2 with unsubscripted indices -1/-1, then vm_pointer_operand_write@0x416090 for operand 1. The resolver returns a backing-cell address for direct global/local integer or string operands and the already-stored target for pointer operands. Corpus: 92 calls; destination is always local-ptr, while sources are local-ptr x81, local-int x7, and global-int x4. The C# VM preserves the local/global address domain and supports the native string-pointer destination forms; the traced natural boot reaches the UNITECH/CALCCC pair without fallback.

### 0x64 `copy-inline-int-array` (copy-inline-int-array, argc 2)
- **summary:** (destination)(inline_blob_offset) - decode the count-prefixed integer literal blob at codebase + offset*4 and copy its values to consecutive VM integer cells beginning at destination.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x64_handler@0x426b00 resolves operand 1 as a writable VM address, reads a count dword followed by values from frame_codebase + operand2*4, reverses the native loader's rotate/XOR in-memory representation with anti_tamper_a, and writes consecutive dwords. The original SYS4 file footer stores the count and values plainly; HISTORY.BIN uses 15 blobs to initialize its rectangle, coordinate, and lookup arrays.

### 0x6c `zero-int-range` (copy-to-global, argc 2)
- **summary:** (destination)(count) — write logical integer zero to count consecutive VM cells beginning at the resolved destination.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x6c_handler@0x426d90 resolves operand 1 as a writable VM pointer and fills operand-2 consecutive dwords with ctx->anti_tamper_b, the native encoded representation of logical integer zero. INITCONFIG@0x30 clears G[0x2e49..0x2e55] before registering those 13 profile cells; treating operand 2 as a scalar source incorrectly wrote 13 to G[0x2e49] and disabled ROOM's character greeting/farewell voices. /v2 annotated and saved 2026-07-21.

### 0x135 `bit-set` (bit-set, argc 2)
- **summary:** (value)(bit_index) - set the indexed bit in the destination integer.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x135_handler@0x4296c0 fetches operand 2 as an unsigned bit index, rejects values >=32 through the native script-error path, fetches operand 1, and writes value | (1 << index). HIDEWIN.BIN sets index 1 at 0x13d and later tests mask 0x2 at 0x146.

### 0x136 `bit-reset` (bit-reset, argc 2)
- **summary:** (value)(bit_index) - clear the indexed bit in the destination integer.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x136_handler@0x429730 fetches operand 2 as an unsigned bit index, rejects values >=32 through the native script-error path, fetches operand 1, and writes value & ~(1 << index). HIDEWIN.BIN clears index 1 at 0x154 after testing mask 0x2.

### 0x194 `string-equals` (string-equals, argc 3)
- **summary:** (out)(left)(right) - compare two complete SYS4 strings and write 1 when equal, otherwise 0.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2 op_0x194_string_equals@0x426e20 fetches operands 2 and 3 through the string resolver, compares their byte ranges through FUN_004017a0, and writes compare_result==0 to integer operand 1. INIT2 and GAMESTART use it as a branch predicate for INPUTNAME/default-name handling; the natural Game Start diagnostic reached one GAMESTART call at 0x134c. The C# VM implements ordinal equality through the shared string resolver, covering literal, global, local, global-string-pointer, and local-string-pointer operands; the traced natural-boot regression proves the reached GAMESTART call no longer falls back.

### 0x1b0 `copy-dwords` (copy-dwords, argc 3)
- **summary:** (source)(destination)(count) - copy count consecutive 32-bit cells from source to destination.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2 op_0x1b0_copy_dwords@0x427060 fetches operand 3, resolves addressable operands 1 and 2 through vm_operand_resolve_address@0x425a50, and calls memcpy(destination, source, count*4). Corpus: 65 calls across direct global/local spans and local pointers; 43 are immediately preceded by take-address 0x63. The C# VM copies resolved integer-cell spans while retaining local/global address domains; focused tests cover direct spans and aliased pointers, and the traced natural boot reaches the UNITECH/CALCCC pair without fallback.

## control

### 0x1 `throw-exit-request` (throw-exit-request, argc 0)
- **summary:** () - raise Command_Exit_Exception with result value 1. The outer engine loop catches it, exits without advancing the VM PC, and returns control to the frontend. TITLE uses it for the fifth main-menu action; SYSTEM4 uses it after reporting an invalid execution mode.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1_throw_exit_request@0x4162e0 constructs a four-byte payload with value 1 and calls __CxxThrowException_8 with ThrowInfo 0x5a9710. Its CatchableTypeArray resolves to RTTI TypeDescriptor 0x5b13e8, `.?AVCommand_Exit_Exception@@`. The outer engine_main_tick_with_exception_policy@0x411840 has MSVC FuncInfo 0x5a9750; its typed catch entry maps Command_Exit_Exception to catch_CommandExitException_set_exit_result@0x412648, which forces result 1 and returns continuation 0x412961 for loop teardown/return. The dispatcher advances frame_pc only after a handler returns, and this catch never advances it. A separate generic error-dialog result-4 path at 0x412928 explicitly advances the PC, proving fall-through is opt-in and does not apply here. Corpus has exactly two sites: TITLE@0x393 after the fifth menu action's sound/sleep, and SYSTEM4@0x5b9 after printing 'invalid execution mode'. TITLE bytecode following 0x1 builds a developer debug menu and is unreachable in the retail native path. The frontend full-exit/return-to-title prompt remains a separate unimplemented boundary.

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


### 0x9 `exit-script` (exit-script, argc 0)
- **summary:** () - discard the complete active script stack, reset scene-owned engine services, and load raw script resource 0 as the new root. The global VM banks and process-owned configuration survive; the initial-root-run flag queried by op 0x130 is cleared so LOGO/OP are not replayed.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x9_reset_scene_and_reload_root@0x418f50 clears EngineCtx+0x54ff0, disposes all 40 interpreter-frame slots with script_frame_dispose@0x40e610, aborts timed/input callback state, calls scene_context_init_reset@0x40b3b0, resets hotspot/input services, optionally releases AutoFreeTex surfaces, then calls script_frame_load_resource(...,0). The scene reset clears interpreter/ADV/input/retained-gfx state and normally releases the 1000 surface/movie slots; it does not clear the VM global bank or engine configuration. Corpus sites are terminal scene/control exits rather than ordinary local returns.

Implemented as a whole-stack root-reload boundary in the persistent VM. A request propagates through every nested call-script frame without resuming caller instructions, clears VM/host scene presentation and input state, cancels deferred SFX starts while preserving active/process-owned audio, preserves globals/external globals and process-owned host configuration/caches, then resolves raw resource 0 through the script provider and starts it at offset zero. The retained history backlog is deliberately preserved pending a separate proof of its native lifetime; recording suppression is reset.

### 0x7b `coroutine-save-yield-handlers` (u0041ADB0, argc 2)
- **summary:** (handler1_pc)(handler2_pc) — scene-coroutine: save the two per-frame yield/resume handler PCs. Native writes op1→ctx[0x6da88+idx*4], op2→ctx[0x6db28+idx*4] (idx=ctx[0x53d14] script-context index); its generic handler prologue records the 5-dword instruction length. SC0000 0x79: `0x7b label_3c9 label_41e` registers the ADV per-frame render→poll→yield handlers. Part of the scene-coroutine framework (see engine-re.md §Scene-coroutine framework); pairs with 0x7c (resume) + 0x140 (loop iterator).
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra: handler FUN_0041ebf0 (dispatch ctx[0x26c93+0x7b]) = {frame_instruction_word_count[idx]=5; ctx[0x6da88+idx*4]=op1; ctx[0x6db28+idx*4]=op2}. Both operands are code PCs (handler labels).

### 0x7c `coroutine-resume` (u00416A90, argc 0)
- **summary:** () — scene-coroutine RESUME point. Native requires run-state bit 0x2000000 (ctx[0x6dbc8]) set — THROWS (__CxxThrowException) if unset, so it is only ever reached on a scheduler-driven re-entry, NEVER on a cold first pass (cold flow jmps over it). Restores PC=ctx[0x53d28]+ctx[0x6dbcc]*4, clears the run-bit (ctx+0xa0ce4 &= ~0x2000000), resets input/line state. SC0000 0x443 (falls into the main loop label_444). See engine-re.md §Scene-coroutine framework.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra: handler FUN_00417cb0 (dispatch ctx[0x26c93+0x7c]). Guards on (ctx[0x6dbc8] & 0x2000000)==0 → throw; else restores PC = ctx[0x53d2c-slot] = ctx[0x53d28]+ctx[0x6dbcc]*4, ctx[0xa0ce4]=ctx[0x6dbc8]&0xfdffffff, clears input state (ctx[0x13bdc]/0xc6f8=-1 etc.).

### 0x8f `call` (call, argc 1)
- **summary:** intra-script subroutine call (local JSR): PC = frame.codebase + operand*4; pushes a return address on the per-frame return stack. NOT cross-script (that is call-script 0x03).
- **grounding:** source=investigation, confidence=high
- **evidence:** native-RE (Ghidra): handler FUN_0041fba0 (= ctx[0x26c93+0x8f]) sets [frame PC @+0x53d2c] = [frame codebase @+0x53d28] + operand*4 and pushes ((pc-base)>>2)+3 onto the per-frame return stack ([ctx+0x552e8]/[ctx+0x55248]). Target is a code OFFSET within the current script (matches header table T3 tag 0x8F = local call targets), confirming it is a local JSR, not a script load.

### 0xa1 `begin-value-switch` (begin-value-switch, argc 0)
- **summary:** Clear and initialize the current value-to-PC dispatch table used by the following case and switch-jump opcodes.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xa1_handler@0x42bd60 resets the engine's open-addressing value-dispatch table with capacity 0xfff. Corpus has 12 canonical sequences of 0xa1, one or more 0xa2 cases, then 0xa3 dispatch.

### 0xa2 `add-value-switch-case` (add-value-switch-case, argc 2)
- **summary:** (case_value)(target_pc) - format the VM value as a dispatch key and add its branch target to the current switch table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xa2_handler@0x42d310 formats operand 1 through vm_value_format_string@0x418860 and inserts operand 2 into value_dispatch_insert_case@0x42cf70. HISTORY.BIN maps action values 0,3,4,5,6 to callback branches; it is generic switch/case dispatch, not menu registration.

### 0xa3 `value-switch-jump` (value-switch-jump, argc 2)
- **summary:** (selector)(default_target) - jump to the target registered for selector, or to the supplied default target when no case matches.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xa3_handler@0x420060 formats operand 1, queries value_dispatch_lookup@0x419290, writes the matched or operand-2 default PC into the current frame, and clears the command type. Corpus pairs it with 0xa1/0xa2 in 12 generic switch sequences.

### 0xae `continue-save-load-stack-restore` (u00415130, argc 0)
- **summary:** () - during serialized save restoration, replace the current frame PC with its saved resume/call target and advance through the saved script-context stack; otherwise a no-op.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xae_continue_save_load_stack_restore@0x416790 first tests ctx+0x53d24 (set by save_data_deserialize_and_begin_restore@0x40fd10). When clear it returns. When set, it selects the serialized frame layout through set:SaveVersion1/2, restores the current PC from that layout's saved return/call target, advances through contexts with FUN_0040f2d0, and clears the restore flag on reaching the saved terminal context. Its 305 corpus sites overwhelmingly follow coroutine-resume/call boundaries, which provide the rendezvous points used while reconstructing the stack.

### 0xc8 `sleep` (sleep, argc 1)
- **summary:** Pause the current script for <duration> milliseconds while retained presentation continues.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra: dispatch ctx[0x26c93+0xc8]=0x420ec0; sleep_op_0xc8 + sleep_timer_arm decoded/annotated 2026-07-08. docs/engine-re.md sleep section.

Native handler sleep_op_0xc8 @0x420ec0 is NON-BLOCKING: it arms a timer (sleep_timer_arm @0x44cff0 at ctx+0x5f304 = active flag + start tick + duration) that the engine main loop polls, resuming the script when elapsed. Operand UNIT = MILLISECONDS (start = ms tick source DAT_0056f3d4, timeGetTime/GetTickCount class), and the native arm helper clamps duration to a minimum of 1 ms. ROOM's input loop deliberately uses sleep(0), making it a one-engine-tick yield rather than a no-op. The handler also records its generic 3-dword instruction length and runs anti-tamper checks, neither needed host-side. Port equivalent: the Godot host parks the VM thread for max(1, scaled duration) ms while the presentation compositor continues. Sleep is one proven presentation-capable service boundary; ordinary AE setup runs burst-fast to 0x21c and is not paced per opcode. Headless hosts no-op it (parity).

### 0xd3 `begin-timed-callback-sequence` (u00425960, argc 0)
- **summary:** Clear and initialize the current script frame's timed local-callback sequence.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xd3_handler@0x41a430 clears the 16-byte-entry vector at ctx+0x5f694, sets last index ctx+0x5f6a4 and abort target ctx+0x5f690 to -1, and resets playback cursor ctx+0x5f6a8 to zero. HISTORY.BIN@0xbf9 begins its seven-event scrollbar interpolation schedule.

### 0xd4 `append-relative-timed-callbacks` (u004266F0, argc 4)
- **summary:** Append repeated relative deadlines and their on-time/catch-up local callback targets to the current frame's timed sequence.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xd4_handler@0x428890 loops operand 2 times, accumulates operand 1 onto the preceding deadline, stores operand 3 at entry+8 and operand 4 at entry+0xc, and appends each 16-byte entry. HISTORY.BIN@0xbfa/0xc03/0xc0c produces deadlines 10,20,30,40,50,51,52 ms.

### 0xd5 `run-timed-callback-sequence` (u004262C0, argc 1)
- **summary:** Start or service the frame-local timed sequence, dispatching scheduled local callbacks through the penultimate entry and selecting the catch-up target when the following deadline is already late.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xd5_handler@0x421090 retains the current script resource, starts the ctx+0x5f3ac timer, sorts the schedule, and keeps run-state bit 0x40 active only while cursor < last_index, making the final entry a non-dispatched look-ahead sentinel. timed_callback_sequence_tick@0x408170 waits to each deadline, compares the following entry deadline with elapsed time to select entry+8 versus entry+0xc, redirects the frame PC, and advances ctx+0x5f6a8. Operand 1 is an optional abort/fallback PC; HISTORY passes 0xffffffff.

### 0xd9 `clear-run-state-0x1000` (u00415880, argc 0)
- **summary:** Clear native run/service bit 0x1000; if the secondary context is active, clear the same bit there. SC0000 executes it once after the initial SFX-channel reset, with no VM-visible result.
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **evidence:** Ghidra op 0xd9 handler 0x416da0: ctx->run_state_flags &= ~0x1000; when ctx+0x6f8b8 is nonzero, also clears bit 0x1000 at ctx+0x53d20. No operands, calls, or return value.

### 0x130 `get-initial-root-run` (get-initial-root-run, argc 1)
- **summary:** (out) - write the engine's initial-root-run flag. It is initialized to 1 when the AGE context is constructed and cleared by op 0x9 before that opcode resets state and reloads root script id 0. SYSTEM4 uses the value to call LOGO.BIN and OP.BIN only on the process's initial root run.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x130_get_initial_root_run@0x4295b0 copies EngineCtx+0x54ff0 to operand 1. FUN_00413860 initializes +0x54ff0 to 1 at 0x413cc7/0x413d15; op_0x9_reset_scene_and_reload_root@0x418f50 is its only later writer and clears it before scene_context_init_reset plus script_frame_load_resource(...,0). Corpus: sole site SYSTEM4@0x29a branches to LOGO.BIN then OP.BIN only when the returned value is nonzero.

Implemented as process-lifecycle state owned by the persistent VM: it begins at one and op 0x9 clears it. It is not a script global, save/profile value, command-line seed, or script-name special case.

### 0x140 `coroutine-label-yield` (u0041F9C0, argc 4)
- **summary:** (out)(name_str)(sub_str)(in) — scene-coroutine LOOP ITERATOR / labeled yield. Handler copies name/sub strings + the int operand and calls the NATIVE video/transition service (*DAT_005c6018)(8, ctx[0x54fe8], &{name,sub,in}); writes the returned PC-like value to operand 1. In SC0000 label_462 'ループ開始' (@0x46d): `out=G[0x6be]=LABEL('J',G[0x6be])`; loop runs the intro-setup body (incl. call label_125bd = slot-table fill G[0x3239..0x324e]=4..11) and jmps back until out==G[0x6c3] (a per-scene exit-PC immediate) → mov aba5c 0 → content. The gate G[0xaba5c]==1 that opens this loop is NATIVE scene-entry state (no script sets it to 1). DAT_005c6018 is runtime-resolved (all xrefs READ) = SAME class as the DirectDraw workers we don't model. PORT = HOST-MODEL IMPLEMENTED: synthesize the ADV scene-entry gate, run the LABEL/J setup body once, then return the structurally discovered per-scene terminal; do not emulate the video service. See engine-re.md §Scene-coroutine framework.
- **grounding:** source=investigation, confidence=med
- **evidence:** Ghidra: handler 0x4299c0 (dispatch ctx[0x9b74c]=0x4299c0; created+typed EngineCtx*+annotated; Kelebek u0041F9C0 = VA-drift). Records the generic 9-dword instruction length; op2→local_204, op3→local_104, op4→local_208; (*DAT_005c6018)(8, ctx[0x54fe8], &local_210) → FUN_00425fb0(1,ret). DAT_005c6018: 6 xrefs all READ, no static writer; FUN_00405740 (screen-fade) calls it w/ cmd 3, branches on ret 1/2 = transition progress = native video service.

### 0x199 `yield-adv-coroutine` (u00414D50, argc 0)
- **summary:** Yield/re-enter the registered ADV coroutine handler. The fifth standard chrome button uses this transition to enter the HIDEWIN/window-hidden flow.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x199_yield_adv_coroutine@0x416440 selects the registered coroutine yield-A or yield-B PC according to ctx+0x6dbc8, saves the current resume offset/state, and redirects the current frame PC. SC0000's x=772 ADV button invokes it; the SO001 tooltip at source x=528 reads Window hide, and the surrounding coroutine calls HIDEWIN.BIN.

### 0x1a2 `store-shared-profile-int` (store-shared-profile-int, argc 1)
- **summary:** 0x1a2 (cell) — snapshot the selected global integer cell into AGE's shared SAVE.DAT profile table. The key is `3%08x`, where the address is resolved through global/local pointer operands; the stored value is the cell's current raw 32-bit value. Insert-or-assign semantics replace an existing entry.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1a2_store_shared_profile_int@0x42d360 reads operand 1 with vm_operand_fetch, resolves its cell index with vm_operand_lvalue (accepted types 3/6/0xc), formats `3%08x`, and calls hash_table_insert_or_assign with ECX=ctx+0x5190. shared_profile_payload_write@0x430a20 serializes each 12-byte key plus 32-bit value to SAVE.DAT. Corpus: 17,585 calls in 315 scripts; 17,539 use local-ptr after lookup-array and 46 use global-int.

Paired reader 0x1a3 restores the same cell, returning zero when the key is absent. The table is embedded at ctx+0x5190, explicitly enumerated by shared_profile_payload_write into shared SAVE.DAT, and reconstructed by shared_profile_payload_read. It is not the retained gfx map, numbered SAVE##.DAT state, or RT.DAT ReadTextDB. Port status (2026-07-20): deliberately unimplemented pending the unified shared-profile storage boundary; the former inert GfxState HashSet approximation was removed so coverage reports the gap.

### 0x1a3 `load-shared-profile-int` (load-shared-profile-int, argc 1)
- **summary:** 0x1a3 (cell) — restore the selected global integer cell from AGE's shared SAVE.DAT profile table. It resolves the same `3%08x` cell-address key as 0x1a2 and overwrites the operand with the stored raw 32-bit value, or zero when the key is absent.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1a3_load_shared_profile_int@0x427e90 resolves operand 1 with vm_operand_lvalue, calls shared_profile_int_lookup@0x4199d0 with the shared-profile object at ctx+0x4d7c, then vm_operand_write stores the result. The wrapper searches the table at object+0x414 = ctx+0x5190 and returns zero on a miss. DATA1 corpus: 73 calls in 12 scripts, 42 local-ptr and 31 global-int.

This is the read half of the shared-profile integer service, not a string operation. LOADCONFIG.BIN restores configuration globals with it; GAMESTART/GAMECLEAR and array-pointer call sites restore other selected profile-wide values. Port status (2026-07-20): deliberately unimplemented with 0x1a2 pending the unified shared-profile storage boundary.

### 0x1ad `mark-save-resume-frame` (mark-save-resume-frame, argc 0)
- **summary:** Mark the current script context as the highest frame serialized by numbered-save layouts 2/3. The native serializer saves frames 0 through this boundary and strips the boundary frame's return target so loading resumes it as the top frame. This opcode performs no file I/O itself.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1ad_mark_save_resume_frame@0x416b70 writes decoded instruction size 1 and ctx+0x9928c=cur_ctx_index. context_state_serialize@0x40d320 uses that field (or cur_ctx_index when -1) as the inclusive frame cutoff for save layouts 2/3, serializes frames 0..cutoff, and forces the cutoff frame's saved return entry to -1. op_0x2_exit_or_return_frame@0x417940 clears the mark when unwinding below it. Corpus: 1,928 calls in 304 scripts; SC0000's six calls are at startup and immediately after HISTORY/MENU/HIDEWIN/INPUTNAME returns.

### 0x1cc `get-adv-read-skip-state` (get-adv-read-skip-state, argc 1)
- **summary:** (out) - copy the current ADV read/click-skip service state from ctx+0x6dbd4. label_1235a ORs it with 0x1c7's Ctrl/message-skip bit: zero takes 0x21c's normal transition/yield path; nonzero resets the animation service and presents the completed endpoint through 0x20c.
- **grounding:** source=investigation, confidence=high
- **depended on by:** 0x20c, 0x20d, 0x21c, 0x223
- **evidence:** Ghidra handler 0x427330 calls vm_operand_write(1, ctx+0x6dbd4). adv_refresh_read_skip_state@0x406cd0 and op 0x6e/0x71/0x72 maintain the field from message:ReadTextSkip plus read_text_db_find_message_index@0x468f50 and read_text_db_is_message_read@0x469930. The database is engine-owned shared RT.DAT state keyed by raw packed script resource id and per-script message index, not VM globals or slot-local SAVE##.DAT data. adv_interpreter_tick consumes the result in click/read-skip control; it is not op 0x223 surface-transition progress.

### 0x21c `mark-frame-yield` (mark-frame-yield, argc 0)
- **summary:** Set native run-state bit 0x400; in normal ADV playback this is the retained-presentation render/wait/resume boundary.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x223, 0x1c7, 0x1cc
- **depended on by:** 0x223
- **evidence:** Ghidra handler 0x417520 records the 1-dword instruction length and ORs ctx+0xa0ce4 with 0x400. capture_presentation_trace.py: after 0x125a6 render, 0xcb8e/0xcb98 bind and 0xd5a/0xd63/0xd73/0xd8a mode+targets execute without render; repeated gfx_render_frame begins only at 0x21c. 2026-07-10.

SC0000 label_1235a reaches this when 0x1c7/0x1cc are zero. Native run-state bit 0x400 parks the interpreter while gfx_render_frame repeatedly samples finite one-shot object channels and queued surface commands; op 0x224 follows after dirty state clears. Native trace proves AE001D bind, mode-1 0x203, and 0x202 targets complete in one 5 ms batch with no render, then first compose here. The port publishes and waits for visible finite one-shot channels or 0x223 commands; click forcing remains limited to the latter.

## draw

### 0x25 `crossfade-surfaces` (u00418B40, argc 3)
- **summary:** (source_surface)(target_surface)(interval_argument) — blocking legacy full-frame mode-4 alpha transition. The native engine composites the captured target surface over the captured source while an 8-bit alpha accumulator advances from 0 to 256.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x20c, 0x20d
- **evidence:** Ghidra /v2: dispatch handler op_0x25_handler@0x41ce00 sets run-state bit 8, converts operand 3 to timer interval/alpha step (<=64: interval=arg ms and step=16; >64: interval=arg/16 ms and step=1), calls screen_transition_begin@0x439da0 with mode 4, and later screen_transition_finalize@0x4399c0. engine_main_tick_with_exception_policy polls interval_timer_poll_elapsed_steps@0x44d080 and calls screen_transition_tick@0x43a7a0; mode 4 draws source then target with progress alpha and commits target at 0x100. Thus ROOM argument 10 is about 160 ms and argument 30 about 480 ms. ROOM sites: 0x12f,0x7c6,0x856 use (1,2,10); 0x8dc uses (1,2,30). The step branch was corrected after manual timing validation; /v2 annotated and saved 2026-07-21.

The handler uses an alpha step of 1 and timer interval=argument when argument <=64. Above 64 it uses step=16 and interval=argument/16. The main loop polls that timer, advances by skipped intervals plus the current interval, and does not resume the script until the target endpoint has been presented. ROOM uses (1,2,10) for button/character entry and exit fades and (1,2,30) for the final fade before returning to TITLE. The port captures retained-frame snapshots whenever op 0x20c presents to a selected offscreen render target, then reproduces this blocking alpha lifecycle in the interactive host.

### 0x80 `set-default-gfx-object-slot` (u0041AF00, argc 1)
- **summary:** (slot) - select the retained graphics-object slot used when op 0x1d9 receives explicit slot zero.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x80_set_default_gfx_object_slot@0x41ed40 writes operand 1 to EngineCtx+0x14e08. op_0x1d9_handler@0x420a30 reads that field only when its operand-1 object slot is zero, otherwise it indexes the explicit slot; both paths write object+0x70. MENU-family scripts select working slots 7/8/9 on entry and restore slot 1 during teardown.

### 0x131 `get-message-window-alpha` (get-message-window-alpha, argc 1)
- **summary:** (out) - read the configured `message:MesWinAlpha` value used to alpha-modulate the ADV chrome.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x131_handler@0x4295e0 calls the settings getter with `message:MesWinAlpha` and writes the result. HISTORY.BIN and the shared ADV redraw path compute (16-value)<<4 for the control-strip alpha.

### 0x1f6 `clear-retained-gfx-objects` (clear-retained-gfx-objects, argc 0)
- **summary:** Clear the complete retained gfx-object registry while preserving allocated surface resources. Subsequent object queries return absent until draw/geometry operations recreate records.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1f6_clear_retained_gfx_objects@0x417430 passes ctx+0x46614 to retained_gfx_objects_clear@0x47cab0. The worker destroys every node in owner+0x408, resets its sentinel/count, and clears owner+0xb558/+0xb55c/+0xb564/+0xb568 plus bit 0x10000 state. DATA1: 293 calls in 146 scripts; 146 continue to 0x23d and 143 to 0x20e.

The handler clears the map embedded at retained-gfx owner+0x408, resets its count and transient dirty/force flags, but does not release the 1000 surface slots or clear the separate queued-surface-command list. Corpus call sites pair it either with 0x23d for a full transient-resource reset or with 0x20e for a backbuffer clear.

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

### 0x1fd `gfx-set-scale-current` (u00420620, argc 4)
- **summary:** (handle)(scale_x_percent)(scale_y_percent)(scale_z_percent) — immediately replace the retained object's current scale matrix at obj+0x6c. The handler divides each integer operand by 100.0 before calling matrix4_make_scale; this is distinct from 0x21e's delayed one-shot target scale.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: handler gfx_op_0x1fd_set_vec_scaled@0x422650 fetches operands 2..4, divides each by the 100.0 constant, and calls gfx_object_set_scale_current@0x47e6b0. The worker gets/creates the object, marks obj+0x68, and calls matrix4_make_scale on obj+0x6c. SC0000 sets AE001D handles to 210/210/100 and 240/240/100; without this setter their 800x800 alpha circles remain below the viewport. Both functions annotated and /v2 saved 2026-07-11.

### 0x1fe `set-current-rotation-axis-angle` (u004206C0, argc 5)
- **summary:** (handle)(axis_x)(axis_y)(axis_z)(angle_degrees) — immediately replace the retained object's current axis-angle rotation matrix. This is the direct-current companion to op 0x21f's delayed one-shot rotation target.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1fe_set_rotation_current@0x422700 converts operands 2..5 to float and calls gfx_object_set_rotation_current@0x47e720. The worker gets/creates the handle, marks transform state at obj+0x68, stores current axis obj+0x1ec..0x1f4 and angle degrees obj+0x204, converts degrees to radians with pi/180, builds the current matrix at obj+0xec through matrix4_make_axis_angle, and sets retained-gfx redraw dirty owner+0xb558 (EngineCtx+0x51b6c). The finite-channel consumer composes this current matrix between scale and translation. Corpus: 186/187 calls use Z axis (0,0,1); the lone DEBUG call uses Y axis (0,1,0).

### 0x1ff `set-current-translation` (set-gfx-geom3-c, argc 4)
- **summary:** 0x1ff (handle)(x)(y)(z) — immediately replace the retained object's current translation matrix at obj+0x16c. This is the direct-current companion to 0x220's delayed target at obj+0x1ac.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: handler gfx_op_0x1ff_set_geom3@0x4227b0 converts operands 2..4 to float and calls gfx_object_set_translation_current@0x47e800. The worker gets/creates the object, marks obj+0x168, and calls matrix4_make_translation on obj+0x16c. SC0000 uses (0,0,0) before animating BG001A. Annotated and saved 2026-07-11.

### 0x202 `gfx-blit-color` (gfx-blit-color, argc 5)
- **summary:** 0x202 (handle)(delay_ms)(duration_ms)(alpha)(color) — arm the one-shot packed-ARGB channel. Worker gfx_op_0x202_worker_set_color_anim @0x47ea00 resets shared start obj+0x34, writes delay +0x38, duration +0x4c, and target +0x64. gfx_object_apply_transform_channels @0x472f00 linearly interpolates each byte from current +0x60 on retained-gfx owner+0xb550 (EngineCtx+0x51b64), commits the target, clears timing, writes target -1, and clears the one-shot active bit when all sibling channels finish. Negative alpha/RGB independently preserve current bytes. Implemented in GfxState with synchronized current/target timeline evidence; draw-string 0x204/0x7a remains separate.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4228d0 packs operands 4/5 and calls worker 0x47ea00(handle,delay,duration,packed). Consumer 0x472f00: shared start +0x34; color delay/duration +0x38/+0x4c; current/target +0x60/+0x64; frame clock retained-gfx owner+0xb550 (EngineCtx+0x51b64); bytewise integer LERP; natural or owner+0xb55c (EngineCtx+0x51b70) forced completion. /v2 annotated and saved 2026-07-10.

### 0x203 `gfx-draw-color` (gfx-draw-color, argc 4)
- **summary:** 0x203 (handle)(mode)(alpha)(color) — worker stores the D3D blend selector at obj+0x30 and STATIC packed color at obj+0x60; the handler's ctx+0x53d88 write is the generic 9-dword instruction length. Negative alpha/RGB preserve current static bytes. Mode 0 is the default textured path: preserved 0xffffffff is identity (the alpha byte is not tint strength). Mode 1 is SRCALPHA/ONE additive glow with ARGB alpha scaling the source contribution and RGB providing multiplicative modulation. Mode 2 conditionally forces ONE/ZERO for the selected render target and is used by 0x223 transition sources; mode 3 selects a subtractive special path. Surfaceless mode-0 fill consumption remains distinct.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4229a0; negative operands read current obj+0x60, then worker 0x47e9b0 stores op2 at obj+0x30 and ARGB at +0x60. gfx_object_composite call-site 0x47f78f passes +0x30/+0x60 directly to gfx_object_blit_d3d9. Correct D3D9 constants at 0x4774c0 prove mode 1 writes SRCBLEND=5/SRCALPHA and DESTBLEND=2/ONE, not INVSRCALPHA. TITLE draws two opaque-black, no-colorkey SO022 flame sprites with mode 1; additive composition removes black and preserves the blue glow, verified in a windowed capture. Mode 2 transition setup and synchronized pixels prove 0xffffffff is identity, not solid white. SC0000 page 14 adds the mode-0 endpoint proof: after the EV052CA->EV052DA 0x223 crossfade, 0x203@0x12478 restores the base CG to mode 0 with preserved 0xffffffff; native keeps EV052DA visible while the port's tint-strength interpretation turns every texel white.

### 0x208 `get-texture-size` (get-texture-size, argc 3)
- **summary:** 0x208 (slot)(out_w)(out_h) — writes the loaded texture's width/height into two output globals; keystone for bytecode-computed sprite/bg geometry (SC0000 label_12649)
- **grounding:** source=inference, confidence=med
- **evidence:** SC0000 label_12649: set-texture(resId,slot) then 0x208(slot)->w,h feeds w/2 horizontal-center + foot-anchor subtraction into draw-texture dst; stubbing yields 0x0 sizes / off-center draws

### 0x20b `fill-surface-rect` (fill-surface-rect, argc 7)
- **summary:** (surface_slot)(x)(y)(width)(height)(alpha)(rgb) - fill a clipped rectangle on a graphics surface with the supplied color and alpha.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x20b_handler@0x422d50 converts x/y/width/height to an exclusive rectangle, clamps alpha to 255, repacks RGB to native ARGB, and calls the surface manager's rectangle-fill path at 0x4790e0. HISTORY.BIN clears each 600x30 name strip on temporary surface 0xc1 before draw-string.

### 0x20c `present-frame` (present-frame, argc 0)
- **summary:** Present the composited frame; label_1235a uses this on the read/message-skip branch to expose the completed foreground endpoint immediately.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x223, 0x1c7, 0x1cc
- **depended on by:** 0x25, 0x20d, 0x223
- **evidence:** Ghidra: dispatch table FUN_00413860 param_1[0x26e9f]=gfx_op_0x20c_present_frame; 0x26e9f-0x26c93=0x20c. 2026-07-08.

Native handler gfx_op_0x20c_present_frame -> gfx_render_frame @0x4820b0. This is an explicit retained-state publication boundary, not a continuously visible object-store mutation. The read/message-skip branch resets the animation service then presents; the port publishes and snaps pending 0x223 state here. Normal playback branches to 0x21c, which owns repeated render/wait/resume. Headless hosts remain non-blocking.

### 0x20d `select-render-target` (select-render-target, argc 1)
- **summary:** Select an offscreen surface slot as Direct3D render target 0, or restore the device backbuffer when the operand is at least 1000.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x20c, 0x1c7, 0x1cc
- **depended on by:** 0x25
- **evidence:** Ghidra /v2: op_0x20d_select_render_target@0x422e10 passes operand 1 and retained-gfx owner ctx+0x46614 to retained_gfx_select_render_target@0x479660. Its D3D calls resolve texture level 0 or backbuffer 0, then invoke device vtable +0x94 SetRenderTarget and store selected slot at owner+0xb530. DATA1: 113 calls in 24 scripts.

For slots below 1000 the native worker obtains that surface's level-0 D3D texture surface and calls IDirect3DDevice9::SetRenderTarget(0,...). The >=1000 path obtains backbuffer 0 and records current target -1. Himegari commonly follows a freshly created blank surface selection with 0x20e before drawing into it.

### 0x20e `clear-render-target` (clear-render-target, argc 0)
- **summary:** Clear the currently selected render target to black and reset its depth buffer to 1.0.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x20e_clear_render_target@0x4174f0 passes renderer ctx+0x46188 to d3d_clear_render_target_black@0x471460. The worker calls device pointer +0x410, vtable +0xac with (0,NULL,3,0,1.0f,0). DATA1: 345 calls in 167 scripts; 57 directly follow 0x20d and 143 directly follow 0x1f6.

This is a target-pixel operation, not retained-object teardown. It invokes IDirect3DDevice9::Clear with zero rectangles, flags D3DCLEAR_TARGET|D3DCLEAR_ZBUFFER (3), color 0, depth 1.0, and stencil 0. The port tracks 0x20d target ownership and forwards the clear to the host; its retained compositor already rebuilds the backbuffer from black.

### 0x20f `play-modal-movie-to-surface` (play-modal-movie-to-surface, argc 3)
- **summary:** (raw_resource_id)(surface_slot)(movie_flags) - open a universal raw-catalog MPEG asset into an existing retained surface, start its native movie graph, and arm modal run-state bit 0x2000 so script execution remains parked until the movie completes or is skipped. This is the LOGO/OP/ED whole-movie path, distinct from non-modal scene movie op 0x236.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x20f_play_modal_movie_to_surface@0x422e50 shares the movie-object allocation, DirectShow graph open, audio-route, and volume setup used by 0x236, then calls movie_start_modal_playback@0x463280, ORs EngineCtx+0xa0ce4 with 0x2000, and marks movie presentation dirty. The main loop and window procedure special-case run-state 0x2000. Corpus has exactly three sites: LOGO (0x335f,42,4), OP (0x3364,42,4), and ED (0x3324,42,dynamic flags). Those ids are universal raw SYS4INI indexes for MPEG-pack LOGO.AGF, OP.AGF, and ED.AGF; each script releases its surface only after 0x20f resumes.

Implemented through IHost.PlayModalMovieToSurface. ResourceMap.ResolveRawMovie deliberately bypasses scene manifests while retaining MPEG signature validation in ReadMovie. Godot reuses the asynchronous decoder/retained-surface compositor, parks only the VM thread until EOF, and treats mouse click or Accept/Cancel input as completion before wrapper cleanup releases the decoder. MPEG audio remains a separate backend/audio-clock contract.

### 0x212 `set-gfx-field64` (set-gfx-field64, argc 2)
- **summary:** 0x212 (obj_idx)(val) — handler gfx_op_0x212_set_field64 @0x4230c0: obj=[ctx+0x14d54 + obj_idx*4]; if obj: *(obj+0x64)=val. The generic instruction length is 5 dwords. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4230c0 (dispatch ctx[0x26c93+0x212]); writes [obj+0x64]=operand2, obj from ctx+0x14d54[operand1*4].

### 0x213 `set-gfx-xy` (set-gfx-xy, argc 3)
- **summary:** 0x213 (obj_idx)(x)(y) — handler gfx_op_0x213_set_field68_6c @0x423110: obj=[ctx+0x14d54 + obj_idx*4]; if obj: *(obj+0x68)=x; *(obj+0x6c)=y (an (x,y) pair). The generic instruction length is 7 dwords. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x423110; writes obj+0x68/+0x6c from operands 2/3, obj from ctx+0x14d54[operand1*4].

### 0x215 `query-gfx-object?` (query-gfx-object?, argc 2)
- **summary:** 0x215 (out_slot)(handle) — query the retained gfx-object map. Handler gfx_op_0x215_query_source_slot@0x42a0b0 calls gfx_object_query_source_slot@0x47f280 with owner ctx+0x46614. The worker searches owner+0x408, the same map populated by geometry/draw workers, and returns obj+4: the live source-surface slot written by draw-texture, or -1 if absent. gfx_object_init_default zeroes obj+4, so a geometry/animation-created but draw-unbound object returns slot 0, not -1. SC0000 uses a successful result for query-guarded teardown: op 0x1f7 erases the object group and op 0x1fa releases this slot. Matching the zero default prevents stale transforms from surviving into the EV050EA CG at SC0000 page 58.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra: handler 0x42a0b0 passes ECX=ctx+0x46614 to 0x47f280, which searches ECX+0x408 and returns object+4. gfx_object_bind_draw@0x47e870 writes the bound slot there; gfx_object_init_default@0x472810 explicitly writes zero to dword index 1. SC0000 page-58 trace: the old -1 default skipped cleanup of transform-created handle 0xcb2a, then EV050EA inherited translation (-100,0), rotation -90, and alpha 0; the native zero default makes the guard succeed and the reused object is identity/opaque.

### 0x216 `query-gfx-field?` (query-gfx-field?, argc 2)
- **summary:** 0x216 (out)(idx) — handler gfx_op_0x216_query_table46d14 @0x42a0f0: out = *(ctx+0x46d14 + idx*0x14). The generic instruction length is 5 dwords. A per-object field query over a stride-0x14 table. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x42a0f0; reads ctx+0x46d14[operand2 * 0x14], writes operand1 via FUN_00425fb0(1,·).

### 0x217 `set-gfx-geom3` (set-gfx-geom3, argc 4)
- **summary:** 0x217 (handle)(a)(b)(c) — handler gfx_op_0x217_set_geom3 @0x4231b0 SETS a 3-vector (int→float a,b,c) on object `handle` via native worker FUN_0047e960; its generic instruction length is 9 dwords. In SC0000 label_12649 it writes the anchor vector G[0x6249b/c/d] INTO the object; op 0x218 reads it back. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x4231b0 (dispatch ctx[0x26c93+0x217]); FUN_0047e960(op1,(float)op2,(float)op3,(float)op4). label_12649 sites e.g. 0x00c67 handle=G[0x62457], vec=G[0x6249b/c/d].

### 0x218 `get-gfx-geom3?` (get-gfx-geom3?, argc 4)
- **summary:** 0x218 (handle)(out_a)(out_b)(out_c) — handler gfx_op_0x218_query_geom3 @0x42a130 GETS a stored 3-vector from object `handle` (FUN_0047f360) into out_a/b/c; its generic instruction length is 9 dwords. In label_12649 it reads the object's anchor vector back into G[0x6249b/c/d] — a stubbed DRIVER of the render drift (stale anchor → bad centering). See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x42a130; FUN_0047f360(obj op1) + 3x FUN_00550850→FUN_00425fb0(2/3/4). label_12649 site 0x00c8f handle=G[0x62457] → G[0x6249b/c/d].

### 0x219 `set-gfx-geom3-b` (set-gfx-geom3-b, argc 4)
- **summary:** 0x219 (handle)(a)(b)(c) — handler gfx_op_0x219_set_geom3 @0x423240 SETS a 3-vector (int→float) on object `handle` via native worker FUN_0047e910 (sibling of 0x217, a different per-object vector); its generic instruction length is 9 dwords. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x423240 (was unanalyzed; function created this session; dispatch ctx[0x26c93+0x219]); FUN_0047e910(op1,(float)op2,(float)op3,(float)op4).

### 0x21a `get-gfx-geom3-b?` (get-gfx-geom3-b?, argc 4)
- **summary:** 0x21a (handle)(out_a)(out_b)(out_c) — handler gfx_op_0x21a_query_geom3 @0x42a1b0 GETS a stored 3-vector from object `handle` (FUN_0047f2e0) into out_a/b/c; its generic instruction length is 9 dwords. In label_12649 it reads the object's position vector into G[0x62498/9/a] — a stubbed DRIVER of the render drift. See docs/engine-re.md gfx op-contract table.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x42a1b0; FUN_0047f2e0(obj op1) + 3x→FUN_00425fb0(2/3/4). label_12649 site 0x00c86 handle=G[0x62457] → G[0x62498/9/a].

### 0x21d `clone-gfx-object` (clone-gfx-object, argc 2)
- **summary:** (source_handle)(destination_handle) - clone the complete retained gfx object record (0xb5 dwords / 0x2d4 bytes). SC0000 uses destination=source+1 as range A's old-frame snapshot immediately before 0x223 crossfades range B's updated source into target handle source+2.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x223
- **evidence:** Ghidra handler 0x423310 -> gfx_object_clone@0x47e4f0. Worker requires source in retained-gfx owner+0x408 (EngineCtx+0x46a1c), creates destination, copies exactly 0xb5 dwords from source object record, then marks owner+0xb558 (EngineCtx+0x51b6c) dirty. SC0000 site 0x128fc passes current handle and handle+1 before queueing 0x223 at 0x129e7.

### 0x21e `set-anim-transform-norm` (set-anim-transform-norm, argc 6)
- **summary:** (handle)(delay_ms)(duration_ms)(sx)(sy)(sz) — set the normalized SCALE-matrix channel (100=identity). Target obj+0xac is linearly sampled from current obj+0x6c by gfx_object_apply_transform_channels@0x472f00 on retained-gfx frame time owner+0xb550 (EngineCtx+0x51b64), after delay and for duration, then committed. Shares only start timestamp obj+0x34 with op 0x220; neither Z is opacity.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra 0x47eaa0 calls matrix builder 0x48af1d for target obj+0xac. Consumer 0x472f00 uses delay obj+0x3c, duration obj+0x50, current obj+0x6c, target obj+0xac, shared start obj+0x34, and retained-gfx frame time owner+0xb550 (EngineCtx+0x51b64).

### 0x21f `set-anim-rotation-axis-angle` (set-anim-rotation-axis-angle, argc 7)
- **summary:** (handle)(delay_ms)(duration_ms)(axis_x)(axis_y)(axis_z)(angle_deg) — set the delayed one-shot axis-angle rotation channel. Handler converts axis/angle integers to floats; worker stores target axis obj+0x1f8 and angle obj+0x208 and builds target matrix obj+0x12c. gfx_object_apply_transform_channels samples current axis/angle linearly on shared start obj+0x34 and composes T(-anchor)*scale*rotation*translation*T(anchor).
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x423410 -> gfx_object_set_rotation_channel@0x47eb70; consumer gfx_object_apply_transform_channels@0x472f00 uses delay +0x40, duration +0x54, current axis +0x1ec/angle +0x204, target axis +0x1f8/angle +0x208, current matrix +0xec and target +0x12c. Native SC0000 handle 0xcb8e sample at 11/390 of axis (0,0,1), 30deg matches matrix [0.9055,0.0134;-0.0134,0.9055] and translation (74.1449,47.3127).

### 0x220 `set-anim-transform-abs` (set-anim-transform-abs, argc 6)
- **summary:** (handle)(delay_ms)(duration_ms)(tx)(ty)(tz) — set the absolute TRANSLATION-matrix channel. Target obj+0x1ac is linearly sampled from current obj+0x16c by gfx_object_apply_transform_channels@0x472f00 on retained-gfx frame time owner+0xb550 (EngineCtx+0x51b64), after delay and for duration, then committed. Independent of op 0x21e scale; neither Z is opacity.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra 0x47ecc0 calls matrix builder 0x48afb1 for target obj+0x1ac. Consumer 0x472f00 uses delay obj+0x44, duration obj+0x58, current obj+0x16c, target obj+0x1ac, shared start obj+0x34, and retained-gfx frame time owner+0xb550 (EngineCtx+0x51b64).

### 0x222 `present-gfx-object-range` (present-gfx-object-range, argc 2)
- **summary:** (first_handle)(count) - flush/present retained graphics objects in the selected handle range and clear their pending update flags.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x222_handler@0x4235e0 calls gfx_present_object_range@0x482230. The worker enters the graphics service, walks the retained-object map, processes flagged objects whose handles fall in [first,first+count), clears pending flags, and finalizes the render batch. HISTORY.BIN uses (0,60000) after rebuilding its retained presentation.

### 0x223 `queue-surface-alpha-transition` (queue-surface-alpha-transition, argc 8)
- **summary:** (command_key)(target_slot)(range_a_start)(range_a_count)(range_b_start)(range_b_count)(delay_ms)(duration_ms) — queue a type-0 timed alpha transition command in the separate ctx+0x414 command map. This is render-target/surface presentation state, not an object affine matrix. The render frame composites the two handle ranges into target_slot and ramps alpha 0->1 after delay over duration.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x20c, 0x21c, 0x1c7, 0x1cc
- **depended on by:** 0x20c, 0x21c, 0x21d
- **evidence:** Ghidra handler 0x423620 -> gfx_queue_surface_alpha_transition@0x47f440. Record fields: type +0=0, start +4=0, delay +8=arg7, duration +0xc=arg8, slot +0x10=arg2, range A +0x14/+0x1c=args3/4, range B +0x18/+0x20=args5/6. gfx_render_frame@0x47fbc0 initializes start from retained-gfx owner+0xb550 (EngineCtx+0x51b64) and consumes type 0 as an alpha ramp. SC0000 executes one shared-helper site at 0x129e7.

### 0x224 `clear-gfx-command-queue` (clear-gfx-command-queue, argc 0)
- **summary:** Clear the native gfx command queue rooted at ctx+0x418. Host-implicit because the port composites retained state directly.
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **evidence:** Ghidra handler 0x417550 -> gfx_command_queue_clear 0x47cb10, which destroys queued nodes and restores the sentinel links/count.

### 0x228 `u00421940` (u00421940, argc 5)
- **summary:** 0x228 query translation target (succ)(handle)(outX)(outY)(outZ): clone the retained object, decompose its target translation matrix at obj+0x17c, and return matrix translation obj+0x1ac/+0x1b0/+0x1b4. Returns succ=0 when found; when absent, writes succ=1 and leaves outputs untouched. The C# VM queries TranslationTarget independently of V24. See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2 handler gfx_op_0x228_query_position@0x42a3a0 calls gfx_object_query_translation_target@0x47cdd0. The worker copies the complete 0xb5-dword object record, passes copied obj+0x17c to matrix4_decompose_affine@0x48d7c8, and returns its translation outputs; the decomposition reads matrix elements +0x30/+0x34/+0x38, corresponding to obj+0x1ac/+0x1b0/+0x1b4. SC0000 AE001H queries this before each 0x220 leg. C# regression covers targets (40,-20), (50,-80), (130,-100), plus the missing-object output-preservation path.

### 0x229 `u004219E0` (u004219E0, argc 5)
- **summary:** 0x229 set-position2 (handle)(op2)(x)(y)(z): set object position/geometry directly (FUN_00472bb0/be0). C# VM: sets V24. See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=kelebek, confidence=low

### 0x22f `u00421DD0` (u00421DD0, argc 5)
- **summary:** 0x22f set-position (handle)(op2)(x)(y)(z): set the object base position (direct transform, not ping-pong). Worker gfx_worker_set_translation @0x472e90. C# VM: sets V24. See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=kelebek, confidence=low

### 0x231 `u00421EA0` (u00421EA0, argc 4)
- **summary:** (handle)(frame_period_ms)(frame_count)(column_count) — loop row-major through the spritesheet. Every frame preserves draw-texture's source-rectangle width/height; frame=floor(elapsed/frame_period)%frame_count, src offset=(frame%columns*width, frame/columns*height). Worker gfx_worker_anim_srcrect @0x47eec0; consumer gfx_object_anim_interpolate @0x473ed0.
- **grounding:** source=investigation, confidence=high
- **evidence:** Native /v2 decompile: worker stores period at obj+0x230, frame_count at +0x238, columns at +0x23c. Interpolator computes ((now-start)/period)%frame_count, then offsets both source-rect X bounds by rect_width*(frame%columns) and Y bounds by rect_height*(frame/columns). SC0000 uses (100,8,4) with AE001H's eight 200x200 cells in a 4x2 800x400 sheet.

### 0x232 `u00421EF0` (u00421EF0, argc 4)
- **summary:** 0x232 anim-color (handle)(period)(alpha)(color): ping-pong the temporary packed ARGB passed to the normal object blit. Handler resolves negative alpha/RGB from static color obj+0x60 and clamps alpha above 255. Blend selector obj+0x30 is unchanged: mode 0 keeps default blending (animated alpha is inert; RGB is vertex modulation), while mode 1 uses sampled ARGB alpha as the SRCALPHA scale for additive composition. Fresh static color is 0xffffffff. The C# VM resolves sentinels and consumes sampled ARGB through the unchanged mode-specific path. See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: gfx_op_0x232_anim_color@0x423c30 resolves sentinels then calls gfx_worker_anim_color@0x47ef50; gfx_object_anim_interpolate@0x473ed0 samples static obj+0x60 toward target obj+0x240 into a temporary packed color; gfx_object_composite@0x47f650 passes that color plus unchanged selector obj+0x30 to gfx_object_blit_d3d9@0x4774c0. Blit mode 0 leaves the default path and passes RGB as modulation; mode 1 sets SRCALPHA/ONE additive composition. gfx_object_init_default@0x472810 initializes obj+0x60=0xffffffff. SC0000 0x1a0e (handle,1200,224,-1) is therefore 0xffffffff<->0xe0ffffff with inert alpha and identity RGB in mode 0: no visible pulse. C# regressions cover exact AE001H visual invariance, negative-RGB preservation, mode-0 RGB modulation, and mode-1 additive scaling.

### 0x234 `anim-start` (anim-start, argc 5)
- **summary:** (handle)(period_ms)(axis_x)(axis_y)(axis_z) — configure cyclic rotation. Worker stores period obj+0x228, start obj+0x214=0, and float axis obj+0x244; each frame uses integer degrees floor(((now-start)%period)*360/period). gfx_object_composite right-multiplies this separately anchored transform after the one-shot scale/rotation/translation product, so cyclic rotation also rotates the translation vector.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra handler 0x423da0 converts axis ints to floats -> worker 0x47f060. gfx_object_anim_interpolate@0x473ed0 consumes obj+0x228/+0x214/+0x244 on retained-gfx owner+0xb550 (EngineCtx+0x51b64) and matrix4_make_axis_angle@0x48b215. gfx_object_composite@0x47f650 calls one-shot transform first, cyclic animation second.

### 0x236 `play-movie-to-surface` (play-movie-to-surface, argc 4)
- **summary:** (resource_id)(surface_slot)(movie_flags)(sync_mask) - synchronously resolve/open an archive movie and construct its DirectShow graph, then start asynchronous frame delivery into the retained destination surface. The opcode itself is non-blocking: the VM advances to the next instruction. SC0000's native site evaluates (0x33, 0, 2, 0) at 0x13c8 and resumes at bytecode 0x13d1; its later 0x21c service boundary yields until movie EOF before cleanup.
- **grounding:** source=investigation, confidence=high
- **evidence:** Native handler 0x423ee0 and helpers 0x463c50/0x463aa0/0x463e20/0x4625e0; SC0000 native operand capture; exact 0x13c8->0x13d1 trace; archive-only changing-frame decoder and Godot lifecycle tests.

The handler requires an existing destination texture, allocates/reuses a 0x478-byte movie-to-texture object for the surface, opens operand 1 through the native indexed-asset reader, builds FilterGraph/IGraphBuilder/IMediaControl/IMediaPosition/IMediaEvent/IBasicAudio, and presents bottom-up RGB samples through the movie texture renderer. Operand 3 selects movie/sound routing policy: bits 0x10000/0x20000/0x40000/0x80000 force sound route 0/1/2/3, otherwise set:DependMovieSound is used; SC0000's low value 2 is retained as native movie mode state. Operand 4 is stored as the movie sync/device mask at object+0x42c; SC0000 passes 0. Static layer preparation after 0x236 does not terminate the retained movie; 0x21c services it through EOF and subsequent surface cleanup stops/detaches it.

### 0x238 `set-anim-clock` (set-anim-clock, argc 1)
- **summary:** (duration) — set the GLOBAL animation clock: native ctx+0x51b78=0 (elapsed), +0x51b7c=duration. The generic instruction length is 3 dwords. NON-BLOCKING: only configures; the render loop advances it and interpolates all animating objects. SC0000 opening @0x123bd/@0x13858. Handler 0x4240e0; Kelebek VA 0x422390 is drift.
- **grounding:** source=investigation, confidence=high

### 0x239 `u004223C0` (u004223C0, argc 6)
- **summary:** (handle)(delay_ms)(duration_ms)(frame_count)(column_count)(target_frame) — one-shot row-major source-rectangle cell channel. Worker gfx_worker_set_srcrect_cell @0x47ed90 stores timing at obj+0x48/+0x5c, layout at +0x238/+0x23c, and target at +0x234. C# currently retains the endpoint cell immediately.
- **grounding:** source=investigation, confidence=high
- **evidence:** Native /v2 worker and gfx_object_apply_transform_channels decompile. The consumer advances target_frame cells over duration after delay, preserves the existing source-rect dimensions, and commits the endpoint.

### 0x23d `release-transient-surfaces` (release-transient-surfaces, argc 0)
- **summary:** Stop movie bindings and release transient gfx surface slots 42 through 999 inclusive, preserving system-owned slots 0 through 41.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x23d_release_transient_surfaces@0x4175c0 loops EDI=42 while EDI<1000, stops/releases ctx+0x52bd4[EDI] movie objects, and calls retained_gfx_release_surface@0x474e40 on owner ctx+0x46614. DATA1: 149 calls in 146 scripts; 146 directly follow 0x1f6.

For each fixed slot in [42,1000), the handler stops/releases the movie-to-texture object at ctx+0x52bd4[slot], then invokes the ordinary retained-gfx surface-release worker. Protected/externally owned slots may be retained by the worker's per-slot guard. This is the resource half of the common 0x1f6/0x23d full-reset sequence.

### 0x23f `u00422930` (u00422930, argc 2)
- **summary:** 0x23f query-object (out)(handle): return object status (FUN_0042a520; -1 if none). C# VM: 0 if the object exists else -1. See docs/engine-re.md §SC0000 anim cluster.
- **grounding:** source=kelebek, confidence=low

### 0x242 `set-object-animation-detached` (set-object-animation-detached, argc 2)
- **summary:** Replace the retained object's animation-control word at obj+0x2d0. Bit 0 detaches finite one-shot channels from blocking presentation and protects them from 0x243 forced completion until they finish naturally.
- **grounding:** source=investigation, confidence=high
- **depended on by:** 0x243
- **evidence:** Ghidra /v2: op_0x242_set_object_animation_detached@0x4249d0 calls gfx_object_set_animation_control@0x47f1a0. Consumer gfx_object_apply_transform_channels@0x472f00 tests obj+0x2d0 bit 0 to suppress manager+0xb55c forced completion and manager+0xb560 blocking-dirty publication, while still setting manager+0xb558 redraw dirty; it clears bit 0 when the finite group completes. DATA1: 303 calls; values zero x302, one x1.

The setter get-or-creates the object and writes the complete operand. During retained composition, bit 0 makes the one-shot color/scale/rotation/translation/source-rectangle group ignore the manager's force-complete request and suppresses the blocking-dirty flag while leaving the redraw-dirty flag active. When no finite channel remains, native clears bit 0 and the shared start time. SC0000's two common CG loaders explicitly write zero. Across DATA1, 302 calls write zero and the sole nonzero call is BTL.BIN's animated battle-object setup, which writes one.

### 0x243 `reset-anim-clock` (reset-anim-clock, argc 0)
- **summary:** When animation-service flag bit 1 is clear, request forced completion of ordinary finite one-shot object channels and reset the global animation-service elapsed/duration fields. Objects detached by 0x242 bit 0 ignore the force request.
- **grounding:** source=investigation, confidence=high
- **depends on:** 0x242
- **evidence:** Ghidra handler 0x4182d0: if !(ctx+0x51b80 & 2), set retained-gfx owner+0xb55c (EngineCtx+0x51b70)=1 and zero owner+0xb564/+0xb568. gfx_object_apply_transform_channels treats force value 1 as immediate completion unless obj+0x2d0 bit 0 is set. SC0000 label_1235a calls it before present-frame.

## input

### 0x86 `set-cursor-resource` (u0041B210, argc 1)
- **summary:** (resource_id) - load an indexed cursor asset and install it as the active custom cursor.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x86_set_cursor_resource@0x41f0a0 opens operand 1 through asset_open_indexed_entry, extracts the asset payload, passes it to the cursor installer at 0x485ce0, releases the asset, and refreshes the OS cursor when the window is active. HIDEWIN.BIN selects resources 0x3318..0x331f according to the pointer's screen-edge region.

### 0x87 `clear-cursor-resource` (u00414D10, argc 0)
- **summary:** Clear the active custom cursor and refresh the OS cursor when the game window is active.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x87_clear_cursor_resource@0x416400 calls the cursor clear/release helper at 0x4856b0 and then the same active-window cursor refresh used by op 0x86. HIDEWIN.BIN calls it when leaving an edge region and on exit.

### 0x88 `set-message-skip` (u0041B290, argc 1)
- **summary:** (enabled) - set persistent all-message Skip state. Nonzero makes the interpreter inject ADV fast-forward input every tick; zero stops injection and clears the transient skip run-state bit.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x88_set_message_skip@0x41f130 writes operand 1 to ctx+0x13dc and ctx+0x550fc; nonzero also sets ctx+0x6da78=1, while zero clears run-state bit 0x08000000. adv_interpreter_tick injects input bit 0x40 on every tick while ctx+0x13dc is nonzero. engine_settings_register_defaults@0x46be30 initializes set:CancelMesSkipOnClick to 0; only a nonzero override enables its press/release cancellation path. All 301 ADV button callbacks pass 1; CALLBACK_LOAD.BIN is the sole corpus 0x88(0) reset.

### 0x90 `register-hotspot-callbacks` (u0041BEB0, argc 7)
- **summary:** (x)(y)(w)(h)(on_enter_pc)(on_leave_pc)(on_activate_pc) - append an inclusive cursor rectangle and three callback PCs to the current script frame's native input registry. This opcode only registers; the ADV input service dispatches callbacks later.
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **depended on by:** 0x97
- **evidence:** Ghidra /v2: op_0x90_handler@0x41fc80 fetches x/y/w/h, forms x+w/y+h, fetches operands 5/6/7, and calls input_hotspot_register_rect_callbacks@0x403d70 on the current frame registry. The worker appends rect + callbacks to arrays and returns without changing PC. input_hotspot_poll_hover_callback@0x4040b0 returns operand 5 on entry and operand 6 on exit; input_hotspot_take_click_callback@0x404330 resets the registry and returns operand 7 on activation. All 301 ADV scripts contain the identical five control-strip records plus three keyed records.

op 0x90 (u0041BEB0, argc 7): `0x90 x y w h tgt_a tgt_b tgt_c`. Kelebek left it "ukn" noting args
5-7 are code locations. Native RE plus corpus analysis resolves it:
- The opcode APPENDS a record; it is not an immediate branch. Rect bounds are stored as
  `(x,y,x+w,y+h)` and the cursor hit-test compares them inclusively.
- The later input service maps targets exactly: target A = pointer enter, target B = pointer leave,
  target C = activation/click. Moving directly between records emits leave first, then enter on the
  next service poll. Activation consumes/resets the registry before dispatching target C.
- Two forms, both ONLY in one shared ADV-chrome subroutine copied into every ADV script:
  * Mode A (1505 = 5x301): immediate x,y,w,h with w=h=20; the five on-screen buttons at
    (684|706|728|750|772, 572): History, Auto message, Message skip, Read-message skip, and
    Hide window. Their enter/leave callbacks set G[0x6c9..0x6cd] to 1/0 and redraw SO001's
    tooltip + generic 20x20 translucent hover overlay; target C runs the per-button action.
  * Mode B (903 = 3x301): local-int operands, w=h=1, only tgt_c real. Companion op 0x97 binds
    input-bit ids 0, 8, and 7 to these records for keyboard/pad activation.
- Every one of the 301 scripts has EXACTLY 8 sites (5 A + 3 B); zero scene-specific use.
- Registration returns normally to pc+1; later callbacks temporarily redirect execution.
- Headless (no cursor/input) can ignore registration; the 12 EMPTY sweep scenes
  are gated by state + this input-wait chrome, NOT by unmodelled 0x90. Model live in A2.


### 0x93 `cancel-hotspot-wait` (u00415040, argc 0)
- **summary:** Reset the current frame's hotspot registry/input wait and clear native run-state bit 0x00800000. Used before opening History, Menu, or HIDEWIN flows.
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **evidence:** Ghidra /v2: op_0x93_cancel_hotspot_wait@0x416670 clears run-state bit 0x00800000, calls input_hotspot_reset@0x404130, and clears the active flag at ctx+0xc6e4 (or sets the pending flag at +0xc6e0 when already inactive).

### 0x94 `arm-hotspot-wait` (u00415090, argc 0)
- **summary:** Arm native hotspot input processing after the script has registered its rectangles.
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **evidence:** Ghidra /v2: op_0x94_arm_hotspot_wait@0x4166d0 sets ctx+0xc6e4=1 and calls input_hotspot_arm_cursor_tracking@0x404230 with 10000. SC0000 executes it immediately after the five control-strip and three keyed registrations.

### 0x97 `bind-hotspot-key` (u0041C150, argc 5)
- **summary:** (x)(y)(w)(h)(logical_action) - find the already-registered rectangle with identical bounds and bind a logical input action to its activation callback.
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **depends on:** 0x90
- **evidence:** Ghidra /v2: op_0x97_bind_hotspot_key@0x41ff30 builds the same inclusive rect as op 0x90 and calls input_hotspot_bind_key_bit@0x403f50. That worker searches registered rects for exact equality and stores operand 5 in the record's logical-action field. adv_input_service_poll@0x411230 passes the configured live action mask to input_hotspot_poll_bound_action_callback@0x403fb0, which returns the first matching record's activation PC. All 136 SC-family scripts bind action 7 to the callback that calls MENU.BIN; SYSTEM4 maps both right mouse and X to action 7.

### 0xcc `register-mouse-callback` (mouse_callback, argc 2)
- **summary:** (poll_interval_ms)(target_pc) - register a timed per-frame mouse callback in the current script.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xcc_register_mouse_callback@0x420f70 stores operand 2 as the callback PC and the current frame_script_resource_id as its owner, then arms operand 1 as the poll interval. HIDEWIN.BIN and HISTORY.BIN both register a 0x10-ms callback. Op 0xcd performs the timed dispatch.

### 0xcd `dispatch-mouse-callback` (get-input-type, argc 0)
- **summary:** Dispatch the registered mouse callback when its polling interval elapses.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xcd_dispatch_mouse_callback@0x417e10 compares timeGetTime against the registered interval, pushes the following PC, verifies the callback's saved script resource id matches the current frame, and jumps to the registered callback target. It writes no operand, so the upstream get-input-type label was incorrect.

### 0xfb `register-joy-callback` (joy_callback, argc 2)
- **summary:** (input_index)(target_pc) - register one of 32 per-frame joy/input callback targets.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xfb_register_joy_callback@0x421270 bounds-checks operand 1 to 0..31 and stores operand 2 in the current script frame's 33-entry callback table. Ops 0xff/0x100 poll and dispatch this table; HISTORY.BIN and HIDEWIN.BIN register indices 0..10.

### 0xfe `set-input-action-count` (u0041E360, argc 1)
- **summary:** (count) - set the logical action scan bound; op 0x100 uses callback slot count itself when the polled mask is empty.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xfe_set_input_action_count@0x421390 accepts unsigned values below 32, stores the count at EngineCtx+0x814, and throws script error 0x10005 otherwise. op_0x100_dispatch_joy_callbacks scans set mask bits only below this count; when op 0xff produces an empty mask, it instead dispatches the callback table entry at index count and resumes after op 0x100. SYSTEM4@0x1cf sets 10, enabling held logical actions 0..9 and reserving callback slot 10 for the no-input/release path before configuring joystick, mouse, and keyboard mappings.

### 0xff `poll-joy-callback-input` (u00415A10, argc 0)
- **summary:** Poll the configured keyboard/mouse/joystick action mask and initialize the op 0x100 dispatch scan.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0xff_poll_joy_callback_input@0x416eb0 clears the pending input mask, fills it through input_poll_action_mask@0x4608b0, resets EngineCtx+0x6da6c input_callback_scan_index, and copies input_action_count to +0x6da70. The poller combines configured keyboard, mouse-action, and joystick bits; the joystick button-slot loop uses input_action_count, while op 0x100 applies the final action scan bound. input_poll_mouse_action_bits@0x460240 maps VK_LBUTTON/VK_RBUTTON through mouse_button_action_map and adds 4; initialization leaves both slots zero, so left defaults to action 4, while SYSTEM4 op 0x10b remaps right to action 7.

### 0x100 `dispatch-joy-callbacks` (u00415A60, argc 0)
- **summary:** Dispatch each set logical action below the configured count, or callback slot count when no action is held.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x100_dispatch_joy_callbacks@0x416f00 scans the bitmask captured by op 0xff from EngineCtx+0x6da6c up to but excluding EngineCtx+0x814 input_action_count. Each set action resumes at op 0x100 so simultaneous bits continue dispatching. If the mask is empty, it dispatches callback table slot input_action_count and resumes after op 0x100. Callback targets come from the current frame's table populated by op 0xfb; SYSTEM4's count 10 therefore makes slot 10 the native no-input/release callback rather than an eleventh action bit.

### 0x101 `reset-message-skip-input` (u00415BF0, argc 0)
- **summary:** Reset transient message-skip/input service state after an ADV chrome action without clearing op 0x88's persistent all-message Skip flag.
- **grounding:** source=investigation, confidence=med
- **evidence:** Ghidra /v2: op_0x101_reset_message_skip_input@0x4170a0 resets the input state rooted at ctx+0xa0ce8, clears run-state bit 0x08000000, zeroes ctx+0xa0ce8, and writes ctx+0x6da74=1 / ctx+0x6da80=0. It does not touch ctx+0x13dc or ctx+0x550fc, so adv_interpreter_tick re-injects Skip on the following tick while persistent state remains enabled. The Auto, Message-skip, Read-skip, and Hide-window callbacks invoke it after their 100 ms cursor re-arm sequence.

### 0x107 `map-joystick-button` (u0041E500, argc 2)
- **summary:** (button_slot)(physical_button) - map a logical button slot to a physical joystick button; slot N emits logical action N+4.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x107_map_joystick_button@0x421550 writes operand 2 to EngineCtx+0x89c+operand1*4 when unsigned operand1<32, otherwise it is a no-op. input_poll_joystick_action_bits@0x460380 emits axes as actions 0=up, 1=right, 2=down, 3=left and mapped button slot N as action N+4. SYSTEM4 maps slots 0..5 to physical buttons 0,3,2,1,6,7, producing actions 4..9.

### 0x108 `get-mouse-button-state` (u00415E70, argc 1)
- **summary:** (out) - return the current mouse-button state bitmask.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x108_get_mouse_button_state@0x428b60 fills a local through input_poll_raw_mouse_buttons@0x4602e0 and writes it to operand 1. The raw mapping is VK_LBUTTON -> 0x1 and VK_RBUTTON -> 0x2. This is distinct from op 0xff's configured logical action mask: left defaults to action 4, while SYSTEM4 maps right to action 7 with op 0x10b. HIDEWIN registers both logical actions to its close/restore callback.

### 0x109 `get-cursor-virtual` (u00415EC0, argc 2)
- **summary:** (out_x)(out_y) - read the OS cursor and convert it into AGE's virtual-screen coordinates.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x109_get_cursor_virtual@0x428bb0 calls the cursor-position helper, converts client/display coordinates through the active VirtualFullScreen transform, and writes x/y to operands 1/2. The ADV chrome callbacks preserve x and then move y by alternating -1/+1 before op 0x10a.

### 0x10a `set-cursor-virtual` (u0041E540, argc 2)
- **summary:** (x)(y) - convert AGE virtual-screen coordinates to client/screen coordinates and move the OS cursor.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x10a_set_cursor_virtual@0x421590 maps virtual coordinates through the active VirtualFullScreen geometry and calls SetCursorPos. SC0000 alternates the cursor by one vertical pixel after state-changing ADV button clicks so the hover state re-enters cleanly.

### 0x10b `map-mouse-button` (u0041E5A0, argc 2)
- **summary:** (button_slot)(physical_button) - map a physical mouse button to a logical button slot; polling emits the slot as logical action slot+4.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x10b_map_mouse_button@0x4216f0 writes operand 1 to EngineCtx+0x135c+operand2*4 when unsigned operand1<32; it does not validate the physical-button operand. input_poll_mouse_action_bits@0x460240 checks left/right physical indices 0/1, reads this map, adds 4, and sets that logical action bit. Initialization zeroes the map, so left defaults to action 4. SYSTEM4@0x218 uses (3,1), mapping right mouse to action 7.

### 0x10c `map-keyboard-scancode` (u0041E5E0, argc 2)
- **summary:** (action)(dik_scan_code) - map a DirectInput keyboard scan code to a logical input action.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x10c_map_keyboard_scancode@0x421730 requires unsigned operand1<32 or throws script error 0x10005. It translates operand 2 through EngineCtx+0x1828, the DIK-to-Win32-VK table built by input_initialize_dik_to_vk_table@0x45fc60, then writes the action to EngineCtx+0x1428[VK]. input_poll_keyboard_action_bits@0x4601a0 polls 256 virtual keys and emits the configured action bits. input_manager_initialize_defaults@0x460630 starts with count 7 and maps 0=Up, 1=Right, 2=Down, 3=Left, 4=Enter, 5=Space, and 6=Backspace. SYSTEM4 raises the count to 10 and adds Z to action 4, C/LeftCtrl to 6, X to 7, PageUp to 8, and PageDown to 9 without erasing the earlier mappings.

### 0x10d `consume-mouse-wheel-delta` (u00415F10, argc 1)
- **summary:** (out) - return the accumulated signed mouse-wheel delta and clear it.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x10d_consume_mouse_wheel_delta@0x428cf0 copies ctx+0x1c34 to operand 1 and immediately clears the field. AGE's window procedure at 0x486925 handles WM_MOUSEWHEEL by sign-extending the high word of wParam and accumulating it at ctx+0x1c34; HISTORY.BIN polls this value and branches on its sign to select one scroll direction.

### 0x12e `find-hit-rectangle` (find-hit-rectangle, argc 8)
- **summary:** (index_inout)(reference_rect)(pointer_x)(pointer_y)(rect_array)(x_offsets)(y_offsets)(count) - scan after the incoming index for the next inclusive rectangle intersection, or return -1.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x12e_find_hit_rectangle@0x428ff0 resolves the reference rectangle and three array operands as VM pointers, decodes rectangle/offset values with anti_tamper_a, and starts at incoming_index+1. Each candidate is [left,right,top,bottom]; the worker subtracts its per-entry x/y offsets from the pointer, inclusively intersects it with the reference rectangle, and writes the matched array index or -1 to operand 1. HISTORY.BIN uses decoded local arrays for scrollbar/control regions, close region 8, and visible text rows 9..13.

### 0x19a `get-message-skip` (u00414E50, argc 1)
- **summary:** (out) - return the current all-message skip state set by op 0x88.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x19a_get_message_skip@0x4271b0 writes ctx+0x550fc to operand 1. The shared ADV redraw routine uses it to select SO001's active Message-skip icon at x=728.

### 0x19b `suspend-adv-skip-service` (u00414E80, argc 0)
- **summary:** () - suspend active ADV fast-forward while preserving the user's persistent all-message Skip toggle.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x19b_suspend_adv_skip_service@0x416560 clears run-state bit 0x08000000, ctx+0x13dc active message skip, ctx+0x55100 ADV skip-service enable, and the click-cancel phase, but deliberately leaves ctx+0x550fc persistent all-message Skip unchanged. Corpus comments call it `savemesskip`; ADV-end/branch and CALLBACK_LOAD paths use it before leaving ADV presentation.

### 0x19c `resume-adv-skip-service` (u00414EC0, argc 0)
- **summary:** () - enable the ADV skip service and recompute active fast-forward from persistent all-message Skip or the live read-skip channel.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x19c_resume_adv_skip_service@0x4165a0 sets ctx+0x55100, clears the click-cancel phase, and normally activates run-state bit 0x08000000 when ctx+0x550fc persistent all-message Skip or ctx+0x6dbd4 adv_read_skip_state is nonzero. Otherwise it clears ctx+0x13dc and the run-state bit. A separate startup fast-forward guard at ctx+0x6f86c suppresses reactivation while active. Corpus comments call it `loadmesskip`; ADV setup, branch, and CALLBACK_LOAD paths pair it with op 0x19b.

### 0x1b6 `get-auto-message` (u00414F60, argc 1)
- **summary:** (out) - return whether automatic message advance is enabled.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1b6_get_auto_message@0x4271e0 writes `(ctx+0x55104 != 0)` to operand 1. FUN_00411230 consumes the same field to arm AutoMessageTime0/1 timers; the shared ADV redraw routine uses it for the active Auto icon.

### 0x1b7 `set-auto-message` (u0041B640, argc 1)
- **summary:** (enabled) - enable or disable automatic message advance.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1b7_set_auto_message@0x41f560 stores `(operand1 != 0)` at ctx+0x55104. SC0000's x=706 Auto button reads the current value through 0x1b6, toggles modulo 2, then writes it through this opcode.

### 0x1b8 `get-auto-message-time` (u0041B670, argc 2)
- **summary:** (selector)(out) - read an Auto-message delay from engine configuration: selector 0 = post-voice AutoMessageTime0, selector 1 = unvoiced AutoMessageTime1.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1b8_handler@0x427210 dispatches selector 0 to config key message:AutoMessageTime0 and selector 1 to message:AutoMessageTime1, then writes the result to operand 2. CONFIG.BIN uses it to bound the two UI settings.

### 0x1b9 `set-auto-message-time` (u0041B710, argc 2)
- **summary:** (selector)(milliseconds) - write an Auto-message delay to engine configuration: selector 0 = post-voice AutoMessageTime0, selector 1 = unvoiced AutoMessageTime1.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1b9_handler@0x41f5a0 dispatches selector 0/1 to config setters for message:AutoMessageTime0/1. CONFIG.BIN initializes them to 500/2000 ms and adjusts each in 500-ms steps within 500..9500.

### 0x1bc `reset-message-voice-state` (u00415670, argc 0)
- **summary:** Reset the per-message queued-voice flag used by ADV Auto timing.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1bc_handler@0x416c20 writes zero to ctx+0x6dbe4 (adv_auto_voice_pending). Opcode 0xc4 sets that field when voice playback is queued; adv_input_service_poll consumes it to select the post-voice AutoMessageTime0 path.

### 0x1c7 `get-message-skip` (get-message-skip, argc 1)
- **summary:** (out) - write 1 iff ADV message-skip run-state bit 0x08000000 is set, otherwise 0.
- **grounding:** source=investigation, confidence=high
- **depended on by:** 0x20c, 0x20d, 0x21c, 0x223
- **evidence:** Ghidra /v2: handler 0x4272b0 reads ctx+0xa0ce4 bit 0x08000000 and vm_operand_write(1, 1|0). adv_interpreter_tick@0x410fb0 first polls the configured logical-action mask; action 6 is bit 0x40 and therefore drives this transient run state from SYSTEM4's C/LeftCtrl bindings or the retained native Backspace default. Persistent op-0x88 state injects the same bit. SC0000 label_1235a ORs the result with op 0x1cc.

### 0x1ca `set-read-message-skip` (u0041B9B0, argc 1)
- **summary:** (enabled) - set the engine setting `message:ReadTextSkip`, which skips only previously read text.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1ca_set_read_message_skip@0x41f880 calls the engine setting interface's setter for `message:ReadTextSkip` with operand 1. SC0000's x=750 Read-message-skip button toggles the value read by op 0x1cb.

### 0x1cb `get-read-message-skip` (u00414FD0, argc 1)
- **summary:** (out) - read the engine setting `message:ReadTextSkip`.
- **grounding:** source=investigation, confidence=high
- **evidence:** Ghidra /v2: op_0x1cb_get_read_message_skip@0x4272f0 calls the engine setting interface's getter for `message:ReadTextSkip` and writes the result to operand 1. The shared ADV redraw routine uses it for the active Read-message-skip icon.

## marker

### 0x1bf `call-end` (u004156C0, argc 0)
- **summary:** zero-arg; call->0x1bf->stmt-end — end-of-call-statement marker
- **grounding:** source=inference, confidence=med, noop_headless=True

### 0x1d5 `cond-block` (u00415700, argc 0)
- **summary:** zero-arg; ALWAYS follows jcc — marks conditional body entry
- **grounding:** source=inference, confidence=high, noop_headless=True

### 0x1f4 `stmt-begin` (u004160D0, argc 0)
- **summary:** zero-arg; opens scripts, pairs with stmt-end 0x1f5
- **grounding:** source=investigation, confidence=high, noop_headless=True

### 0x1f5 `stmt-end` (u00416120, argc 0)
- **summary:** zero-arg; precedes exit/next-stmt, pairs with 0x1f4
- **grounding:** source=investigation, confidence=high, noop_headless=True

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

## unknown

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

### 0x21 `u00418860` (u00418860, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x22 `u00418920` (u00418920, argc 2)
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

### 0x6e `show-text` (show-text, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x6f `end-text-line` (end-text-line, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x7f `u00414C60` (u00414C60, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x8c `jmp` (jmp, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xa0 `jcc` (jcc, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

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

### 0xd0 `u00415830` (u00415830, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x12c `lookup-array-2d` (lookup-array-2d, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x12f `u0041ECB0` (u0041ECB0, argc 4)
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

### 0x195 `u00425580` (u00425580, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x196 `display-furigana` (display-furigana, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

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

### 0x1ae `u0041CED0` (u0041CED0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1af `u004245C0` (u004245C0, argc 3)
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

### 0x1ba `u0041D850` (u0041D850, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1c8 `toString` (toString, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x205 `u00420A60` (u00420A60, argc 6)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x207 `u00420B00` (u00420B00, argc 8)
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

### 0x241 `u00422B80` (u00422B80, argc 5)
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

