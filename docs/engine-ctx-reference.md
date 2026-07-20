# EngineCtx field reference

> Generated from `vm-map/engine-ctx.toml` by `tools/engine_ctx_build.py --build`. Do not edit.

Struct `EngineCtx`, size `0xa1000`. Applied to the Ghidra `/v2` image (dispatch-handler `this` = `EngineCtx *`).

| offset | name | type | note |
|---|---|---|---|
| `0x40c` | `sys4ini_count` | `int` | SYS4INI record count |
| `0x410` | `archive_name_table` | `void*` | archive-name table base (arc_id*0x100 indexes it) |
| `0x414` | `sys4ini_records` | `void*` | SYS4INI 80-byte record base {name[64],arc_id,file_number,offset,size}; record = base + id*0x50 |
| `0x13dc` | `message_skip_enabled` | `int` | persistent all-message Skip flag; op 0x88 writes it and adv_interpreter_tick injects input bit 0x40 while nonzero |
| `0x1c34` | `mouse_wheel_delta` | `int` | signed WM_MOUSEWHEEL delta accumulated by age_main_window_proc; op 0x10d returns and clears it |
| `0x3028` | `alt_pack_table` | `int` | call-script high-byte alternate pack table (unused by corpus) |
| `0x4d7c` | `shared_profile_state` | `void*` | embedded shared SAVE.DAT state object; owns profile integer/settings tables and container timing metadata |
| `0x5190` | `shared_profile_int_table` | `int` | open-addressing 12-byte string-key to 32-bit value table; op 0x1a2 stores, 0x1a3 loads, shared SAVE.DAT serializes it |
| `0x14d54` | `gfx_obj_ptr_table` | `void*` | per-object pointer table (ops 0x212/0x213 write obj+0x64/0x68/0x6c) |
| `0x14ea0` | `text_line_spacing` | `int` | extra pixel leading between text lines; defaults to 6, op 0x8b writes it |
| `0x14f45` | `script_frame_index` | `int` | call-script frame index (0x1e-dword frames) |
| `0x46188` | `renderer_device_state` | `int` | embedded D3D9 device/render state; op 0x20e passes this base to the target/depth clear worker |
| `0x46598` | `d3d9_device` | `void*` | IDirect3DDevice9 pointer at renderer_device_state+0x410; op 0x20e invokes vtable Clear +0xac |
| `0x46614` | `retained_gfx_manager` | `int` | embedded retained graphics manager passed by gfx opcode handlers; worker offsets are relative to this base |
| `0x46a1c` | `gfx_obj_registry` | `int` | retained_gfx_manager+0x408 std::map handle->object; 0x1f6 clears, geometry/draw get-or-create, 0x215 queries, 0x1f7 erases |
| `0x46d14` | `query_table_46d14` | `void*` | stride-0x14 table read by op 0x216 |
| `0x50ba4` | `gfx_render_target_table` | `void*` | retained_gfx_manager+0xa590 array of per-slot D3D texture wrappers selected by op 0x20d |
| `0x51b44` | `gfx_current_render_target` | `int` | retained_gfx_manager+0xb530 selected surface slot; -1 denotes device backbuffer |
| `0x51b64` | `frame_timer` | `int` | frame timer at retained_gfx_manager+0xb550; present updates 0x51b64/0x51b68 and object animation samples it |
| `0x51b6c` | `gfx_dirty_a` | `int` | retained_gfx_manager+0xb558 redraw-dirty flag; active finite object channels set it even when detached by 0x242; op 0x1f6 clears it |
| `0x51b70` | `gfx_force_complete` | `int` | retained_gfx_manager+0xb55c requests forced completion of one-shot channels; op 0x1f6 clears it |
| `0x51b74` | `gfx_dirty_b` | `int` | retained_gfx_manager+0xb560 blocking-presentation dirty flag; active finite object channels set it unless obj+0x2d0 bit 0 detaches them |
| `0x51b78` | `anim_clock_elapsed` | `int` | global anim clock elapsed at retained_gfx_manager+0xb564; op 0x238 and object reset 0x1f6 zero it |
| `0x51b7c` | `anim_clock_duration` | `int` | global anim clock duration at retained_gfx_manager+0xb568; op 0x238 sets and object reset 0x1f6 zeroes it |
| `0x51b80` | `gfx_animation_service_flags` | `uint` | retained_gfx_manager+0xb56c service flags; bit 1 suppresses op 0x243 force-complete/clock-reset request |
| `0x52bd4` | `surfaces` | `void*` | surface array base [~1000 slots]; create/set-texture (0x1f8/0x1f9) allocate |
| `0x53d14` | `cur_ctx_index` | `uint` | current script-context index (curCtx); indexes 0x78-byte coroutine/frame records |
| `0x53d24` | `save_load_stack_restore_active` | `int` | set while a deserialized numbered-save script stack is being reconstructed; op 0xae restores saved frame PCs/contexts and clears it at the terminal saved context |
| `0x53d28` | `frame_codebase` | `void*` | current frame codebase (PC = codebase + off*4) |
| `0x53d2c` | `frame_pc` | `int` | current frame PC column (op = *(0x53d2c + curCtx*0x78)) |
| `0x53d60` | `ctx_record_base` | `void*` | 0x78-byte context-record array base (coroutine/script contexts) |
| `0x53d64` | `frame_script_resource_id` | `uint` | raw packed SYS4/AAI resource id for this 0x78-byte script frame; persisted ReadTextDB script key |
| `0x53d88` | `frame_instruction_word_count` | `int` | current decoded instruction length in dwords for each 0x78-byte script frame; interpreter advances PC by this value * 4 |
| `0x550fc` | `message_skip_display_enabled` | `int` | persistent all-message Skip state returned by op 0x19a for the ADV control-strip active overlay |
| `0x55100` | `adv_skip_service_enabled` | `int` | ADV presentation lifecycle gate: op 0x19b clears it while preserving the persistent Skip toggle; op 0x19c sets it before recomputing active message/read skip |
| `0x55104` | `auto_message_enabled` | `int` | ADV Auto mode flag; op 0x1b6 reads, op 0x1b7 writes, adv_input_service_poll consumes |
| `0x55110` | `text_history_recording_suppressed` | `uint` | high bit suppresses ADV retained-history index/text/metadata/voice recording; op 0x1bb writes 0x80000000 or zero, and HISTORY.BIN brackets itself with disable/enable |
| `0x55120` | `anti_tamper_a` | `int` | anti-tamper checksum operand |
| `0x55124` | `anti_tamper_b` | `int` | anti-tamper checksum operand |
| `0x5512c` | `anti_tamper_fp` | `int` | anti-tamper (import fn ptr / result) |
| `0x55248` | `ret_stack_a` | `void*` | per-frame return stack (op 0x8f call pushes) |
| `0x552e8` | `ret_stack_b` | `void*` | per-frame return stack (companion) |
| `0x5f304` | `sleep_timer` | `int` | sleep timer object (op 0xc8; +8 active, +0x14 start-ms, +0x18 duration) |
| `0x5f3ac` | `timed_callback_timer` | `int` | elapsed timer object armed by op 0xd5 and sampled by the timed callback scheduler |
| `0x5f64c` | `adv_wait_indicator_timer` | `int` | elapsed timer started by op 0x1ce and ADV wait setup to select the animated input-wait indicator frame |
| `0x5f690` | `timed_callback_abort_pc` | `int` | optional local fallback PC supplied to op 0xd5; -1 disables abort redirection |
| `0x5f694` | `timed_callback_entries_vector` | `int` | MSVC vector object base for 16-byte schedule entries; begin/end/capacity pointers follow |
| `0x5f698` | `timed_callback_entries_begin` | `void*` | begin pointer for 16-byte entries {deadline_ms, field_100, primary_pc, catchup_pc} |
| `0x5f69c` | `timed_callback_entries_end` | `void*` | end pointer for the timed callback entry vector |
| `0x5f6a0` | `timed_callback_entries_capacity` | `void*` | capacity-end pointer for the timed callback entry vector |
| `0x5f6a4` | `timed_callback_last_index` | `int` | last appended schedule index; op 0xd3 resets to -1 and op 0xd4 increments per entry |
| `0x5f6a8` | `timed_callback_cursor` | `int` | next schedule entry consumed by timed_callback_sequence_tick; op 0xd3 resets to zero |
| `0x5f6b4` | `timed_callback_script_resource_id` | `uint` | active frame script resource retained by op 0xd5 for local callback dispatch safety |
| `0x5f6b8` | `timed_callback_script_resource_guard` | `uint` | companion current-script identity checked before redirecting to a scheduled callback |
| `0x5f6c0` | `value_switch_table` | `int` | temporary formatted-value branch table reset by 0xa1, populated by 0xa2, queried by 0xa3; distinct from shared_profile_int_table |
| `0x5f72c` | `adv_wait_indicator_frame` | `int` | current animated input-wait indicator frame, reset to zero when the service is armed |
| `0x5f730` | `adv_wait_indicator_terminal_frame` | `int` | terminal frame returned by the indicator layout during ADV wait setup |
| `0x5f734` | `adv_wait_indicator_enabled_value` | `int` | raw enable operand last written by op 0x1ce; nonzero arms the ADV wait-indicator service |
| `0x6da78` | `message_skip_activation_guard` | `int` | set when op 0x88 enables Skip; suppresses the enabling click from immediately becoming a hotspot activation |
| `0x6da80` | `message_skip_cancel_click_phase` | `int` | press/release phase used only when set:CancelMesSkipOnClick is nonzero |
| `0x6da84` | `adv_wait_indicator_layout_slot` | `int` | ADV text layout whose wait indicator is armed or erased by input-wait setup and op 0x1ce |
| `0x6da88` | `coroutine_yield_a` | `void*` | op 0x7b yield-state save (op1 -> +ctxidx*4) |
| `0x6db28` | `coroutine_yield_b` | `void*` | op 0x7b yield-state save (op2 -> +ctxidx*4) |
| `0x6dbc8` | `coroutine_runstate` | `int` | op 0x7c resume gate (run-state bit 0x2000000) |
| `0x6dbcc` | `coroutine_resume_off` | `int` | op 0x7c resume PC offset |
| `0x6dbd4` | `adv_read_skip_state` | `int` | current ADV read/click-skip service state; queried by op 0x1cc, maintained from ReadTextSkip + per-PC read history |
| `0x6dbe4` | `adv_auto_voice_pending` | `int` | set by play-voice ops when voice service exists; Auto waits for voice completion then arms AutoMessageTime0; op 0x1bc resets |
| `0x6dbe8` | `voice_bgm_duck_saved_volume` | `int` | BGM volume saved by the voice-start duck helper before applying MusicFadeOnVoicePlayingVol |
| `0x6dbf0` | `voice_bgm_duck_control_flags` | `uint` | transient mask replaced by op 0x1cf; bit 0 suppresses automatic voice-triggered BGM attenuation |
| `0x6dbf4` | `message_skip_queued_voice_id` | `int` | latest voice resource deferred by op 0xc4 while run_state_flags message-skip bit is active |
| `0x6dbf8` | `message_skip_queued_voice_arg` | `int` | second argument retained with message_skip_queued_voice_id; Himegari op 0xc4 stores zero |
| `0x9928c` | `save_frame_boundary_index` | `int` | highest script-frame index included by numbered-save layouts 2/3; -1 falls back to cur_ctx_index; op 0x1ad marks current frame and op 0x2 clears after unwinding below it |
| `0x9b24c` | `dispatch_table` | `void*` | opcode->handler table base [0x400]; handler(op) = *(0x9b24c + op*4) |
| `0xa0cc0` | `screen_w` | `int` | screen width (640) |
| `0xa0cc4` | `screen_h` | `int` | screen height (480) |
| `0xa0cc8` | `screen_bpp` | `int` | screen bpp (8) |
| `0xa0ce4` | `run_state_flags` | `uint` | interpreter run-state flags (bit1 sleeping; 0x8000000 skip/fast-forward) |
| `0xa0ce8` | `timed_callback_abort_signal` | `int` | scheduler-polled abort/input signal cleared when op 0xd5 starts; redirects to abort PC when configured |
