# EngineCtx field reference

> Generated from `vm-map/engine-ctx.toml` by `tools/engine_ctx_build.py --build`. Do not edit.

Struct `EngineCtx`, size `0xa1000`. Applied to the Ghidra `/v2` image (dispatch-handler `this` = `EngineCtx *`).

| offset | name | type | note |
|---|---|---|---|
| `0x814` | `input_action_count` | `int` | logical action count (0..31); op 0xfe sets it, op 0x100 scans actions below it and uses callback slot count itself when the polled mask is empty |
| `0x898` | `joystick_physical_button_count` | `int` | WinMM JOYCAPS physical button count returned by op 0x106 |
| `0x89c` | `joystick_button_map` | `int` | base of 32-entry logical button-slot to physical joystick-button table; op 0x107 writes it, slot N emits action N+4 |
| `0x135c` | `mouse_button_action_map` | `int` | base of physical mouse-button to logical button-slot table; op 0x10b writes it and polling adds 4 to obtain the action index |
| `0x13dc` | `message_skip_enabled` | `int` | persistent all-message Skip flag; op 0x88 writes it and adv_interpreter_tick injects input bit 0x40 while nonzero |
| `0x1428` | `keyboard_vk_action_map` | `int` | base of 256-entry Win32 virtual-key to logical action table consumed by keyboard polling |
| `0x1828` | `dik_to_vk_table` | `int` | base of 256-entry DirectInput DIK scan-code to Win32 virtual-key translation used by op 0x10c |
| `0x1c34` | `mouse_wheel_delta` | `int` | signed WM_MOUSEWHEEL delta accumulated by age_main_window_proc; op 0x10d returns and clears it |
| `0x4d7c` | `shared_profile_state` | `void*` | embedded shared SAVE.DAT state object; owns profile integer/settings tables and container timing metadata |
| `0x5190` | `shared_profile_int_table` | `int` | open-addressing 12-byte string-key to 32-bit value table; op 0x1a2 stores, 0x1a3 loads, shared SAVE.DAT serializes it |
| `0x144e0` | `sfx_channel_resource_ids` | `int` | base of ten packed resource ids retained by the sound-effect facade; op 0xb4 loads a channel, op 0xb6 clears it, and numbered-save layouts restore then reopen every positive id |
| `0x14d54` | `adv_text_layout_ptr_table` | `void*` | text_manager at ctx+0x14940 plus layout pointer table +0x414; ops 0x212/0x213 configure each layout's wait-indicator handle and retained glyph-object range at layout+0x64/+0x68/+0x6c |
| `0x14e08` | `gfx_default_object_slot` | `int` | op 0x80 selected slot; op 0x1d9 substitutes it when its explicit object-slot operand is zero |
| `0x14e0c` | `text_primary_font_height` | `int` | primary LOGFONTA.lfHeight at text-manager+0x4cc; stored as a negative pixel height and updated by op 0x75 |
| `0x14e10` | `text_primary_font_width` | `int` | primary LOGFONTA.lfWidth at text-manager+0x4d0; op 0x75 stores negative half the requested font size |
| `0x14e1c` | `text_primary_font_weight` | `int` | primary LOGFONTA.lfWeight at text-manager+0x4dc; op 0x2bd writes 700 for enabled or zero for disabled |
| `0x14e84` | `text_antialias_enabled` | `int` | text-manager+0x544; message:UseAntiFont selects GGO_GRAY4_BITMAP when nonzero and GGO_BITMAP when zero |
| `0x14e88` | `text_antialias_version` | `int` | text-manager+0x548 loaded from set:AntiFontVersion; Himegari SYS4INI supplies version 3 |
| `0x14e8c` | `text_primary_color_bgr` | `uint` | text-manager+0x54c internal GDI BGR/COLORREF primary glyph color; op 0x76 converts script RGB into it |
| `0x14e90` | `text_effect_color_bgr` | `uint` | text-manager+0x550 internal GDI BGR/COLORREF effect glyph color; op 0x77 converts script RGB into it |
| `0x14e98` | `text_render_mode` | `int` | text-manager+0x558; op 0x78 selects primary-only, displaced shadow, reduced-coverage repeat, or sampled ellipse outline |
| `0x14ea0` | `text_line_spacing` | `int` | extra pixel leading between text lines; defaults to 6, op 0x8b writes it |
| `0x14ea4` | `text_effect_offset_x` | `int` | text-manager+0x564; op 0x1a4 horizontal displacement for mode 1 or ellipse radius for mode 3 |
| `0x14ea8` | `text_effect_offset_y` | `int` | text-manager+0x568; op 0x1a4 vertical displacement for mode 1 or ellipse radius for mode 3 |
| `0x14f45` | `script_frame_index` | `int` | call-script frame index (0x1e-dword frames) |
| `0x46188` | `renderer_device_state` | `int` | embedded D3D9 device/render state; op 0x20e passes this base to the target/depth clear worker |
| `0x46598` | `d3d9_device` | `void*` | IDirect3DDevice9 pointer at renderer_device_state+0x410; op 0x20e invokes vtable Clear +0xac |
| `0x46614` | `retained_gfx_manager` | `int` | embedded retained graphics manager passed by gfx opcode handlers; worker offsets are relative to this base |
| `0x46a1c` | `gfx_obj_registry` | `int` | retained_gfx_manager+0x408 std::map handle->object; 0x1f6 clears, geometry/draw get-or-create, 0x215 queries, 0x1f7 erases |
| `0x46d14` | `query_table_46d14` | `void*` | stride-0x14 table read by op 0x216 |
| `0x50ba4` | `gfx_render_target_table` | `void*` | retained_gfx_manager+0xa590 array of per-slot D3D texture wrappers selected by op 0x20d |
| `0x51b44` | `gfx_current_render_target` | `int` | retained_gfx_manager+0xb530 selected surface slot; -1 denotes device backbuffer |
| `0x51b64` | `frame_timer_current` | `int` | current shared millisecond presentation timestamp at retained_gfx_manager+0xb550; active outer-engine ticks and op 0x23c sample timeGetTime, and every retained animation channel samples it |
| `0x51b68` | `frame_timer_previous` | `int` | previous shared presentation timestamp at retained_gfx_manager+0xb554; shifted from current before each new sample, and op 0x231 compares current/previous source cells to raise redraw dirty only at cell changes |
| `0x51b6c` | `gfx_dirty_a` | `int` | retained_gfx_manager+0xb558 redraw-dirty flag; retained mutations and continuous channels set it, while op 0x231 sets it only when the sampled source cell differs across the shared current/previous frame timestamps; op 0x1f6 clears it |
| `0x51b70` | `gfx_force_complete` | `int` | retained_gfx_manager+0xb55c requests forced completion of one-shot channels; op 0x1f6 clears it |
| `0x51b74` | `gfx_dirty_b` | `int` | retained_gfx_manager+0xb560 blocking-presentation dirty flag; active finite object channels set it unless obj+0x2d0 bit 0 detaches them |
| `0x51b78` | `anim_clock_start` | `int` | start timestamp for the separate finite-animation service window at retained_gfx_manager+0xb564; seeded from frame_timer_current by gfx_animation_service_poll, while op 0x238, op 0x243, and object reset 0x1f6 zero it |
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
| `0x54ff0` | `initial_root_run` | `int` | initialized to 1 at AGE context construction; queried by op 0x130 and cleared by op 0x9 before root script id 0 is reloaded |
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
| `0x6da6c` | `input_callback_scan_index` | `int` | op 0xff resets this to zero; op 0x100 advances it while scanning set action bits below input_action_count |
| `0x6da70` | `input_action_count_snapshot` | `int` | op 0xff copies input_action_count here when beginning a callback poll/dispatch pass |
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
| `0x6f89c` | `script_launch_queue` | `int` | embedded integer FIFO used for engine-level auxiliary/root script launches; data pointer and queue indices follow |
| `0x6f8a0` | `script_launch_queue_data` | `void*` | dword storage for packed script ids or negative frame-resume ids |
| `0x6f8a4` | `script_launch_queue_read_cursor` | `int` | next queued launch consumed by script_launch_queue_dispatch_next |
| `0x6f8a8` | `script_launch_queue_write_cursor` | `int` | one-past-last queued launch; op 0x143 appends mounted selector record-zero ids here |
| `0x6f8ac` | `script_launch_queue_capacity` | `int` | allocated dword capacity; initialized to 0x100 |
| `0x6f8b0` | `script_launch_queue_growth` | `int` | capacity growth quantum; initialized to 0x100 |
| `0x6f8b4` | `script_launch_queue_high_water` | `int` | highest consumed cursor retained across FIFO compaction |
| `0x6f8b8` | `script_launch_dispatch_active` | `int` | suppresses immediate dispatch while op 0x143 batches mounted append record-zero ids and while a queued script is active |
| `0x9928c` | `save_frame_boundary_index` | `int` | highest script-frame index included by numbered-save layouts 2/3; -1 falls back to cur_ctx_index; op 0x1ad marks current frame and op 0x2 clears after unwinding below it |
| `0x99290` | `gfx_draw_mode` | `int` | loaded from set:DrawMode during runtime initialization; Himegari uses mode 1, selecting the cached GetGlyphOutline text path |
| `0x9b24c` | `dispatch_table` | `void*` | opcode->handler table base [0x400]; handler(op) = *(0x9b24c + op*4) |
| `0x9c658` | `sys4ini_count` | `int` | SYS4INI record count at embedded FileDB+0x40c |
| `0x9c65c` | `archive_name_table` | `void*` | archive-name table base at embedded FileDB+0x410; arc_id*0x100 indexes it |
| `0x9c660` | `sys4ini_records` | `void*` | SYS4INI 80-byte record base at embedded FileDB+0x414; record = base + id*0x50 |
| `0x9f274` | `mounted_aai_catalogs` | `void*` | base of 256-entry selector-keyed AAI catalog-pointer table inside the embedded FileDB; op 0x143 scans slots 1..255 from +0x9f278 |
| `0x9f278` | `mounted_aai_catalog_selector_1` | `void*` | selector-one cell and op 0x143 scan start; subsequent dwords are selector 2..255 |
| `0xa0b84` | `current_bgm_track_id` | `int` | direct-name BGM track id retained by the music facade; op 0xbf starts/replaces it, ops 0xc0/0xc3 get/set it, and numbered-save layouts restore it |
| `0xa0cc0` | `screen_w` | `int` | logical screen width; constructor/default registry uses 640, then the SYS4INI SCREENX setting overrides it (Himegari 800) |
| `0xa0cc4` | `screen_h` | `int` | logical screen height; constructor/default registry uses 480, then the SYS4INI SCREENY setting overrides it (Himegari 600) |
| `0xa0cc8` | `screen_bpp` | `int` | screen bpp (8) |
| `0xa0ce4` | `run_state_flags` | `uint` | interpreter run-state flags (bit1 sleeping; 0x8000000 skip/fast-forward) |
| `0xa0ce8` | `timed_callback_abort_signal` | `int` | scheduler-polled abort/input signal cleared when op 0xd5 starts; redirects to abort PC when configured |
