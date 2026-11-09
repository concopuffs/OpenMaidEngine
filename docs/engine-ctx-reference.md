# EngineCtx field reference

> Generated from `vm-map/engine-ctx.toml` by `tools/engine_ctx_build.py --build`. Do not edit.

Struct `EngineCtx`, size `0xa1000`. Scope: profile `himegari`, catalog `S4IC422`, scripts `SYS4422`.

Executable identity: `AGE.EXE`, 1105920 bytes, SHA-256 `5c80a3a8c5b2e755647247c2195446385c5f1920db422bcb35cfd41cd77b5ee6`.

Applied only to Ghidra `/v2/range_00400000.bin` at image base `0x400000`; 2490368 bytes, SHA-256 `3d7543ba97e0fd3d59db05d387096dd3afa912b8ec8bcb3c916d9dd313496476`. Offsets must not be transferred to another executable without separately matching provenance.

| offset | name | type | note |
|---|---|---|---|
| `0x4` | `host_ui_interface` | `void*` | host-facing UI interface used by the ctx+4 virtual-dispatch thunks; default slot +4 logs and presents AGE message boxes |
| `0x814` | `input_action_count` | `int` | logical action count (0..31); op 0xfe sets it, op 0x100 scans actions below it and uses callback slot count itself when the polled mask is empty |
| `0x898` | `joystick_physical_button_count` | `int` | WinMM JOYCAPS physical button count returned by op 0x106 |
| `0x89c` | `joystick_button_map` | `int` | base of 32-entry logical button-slot to physical joystick-button table; op 0x107 writes it, slot N emits action N+4 |
| `0x135c` | `mouse_button_action_map` | `int` | base of physical mouse-button to logical button-slot table; op 0x10b writes it and polling adds 4 to obtain the action index |
| `0x13dc` | `message_skip_enabled` | `int` | the Skip a text wait acts on: adv_interpreter_tick and the triggered mask inject action 6 (0x40) while nonzero, so op 0x72 sets run-state 0x08000000. Op 0x88 writes it with ctx+0x550fc; op 0x19b clears it and keeps ctx+0x550fc; op 0x19c and the menu's Skip toggle copy ctx+0x550fc back into it (docs/engine-re.md, "Read skip and ctx+0x13dc") |
| `0x1428` | `keyboard_vk_action_map` | `int` | base of 256-entry Win32 virtual-key to logical action table consumed by keyboard polling |
| `0x1828` | `dik_to_vk_table` | `int` | base of 256-entry DirectInput DIK scan-code to Win32 virtual-key translation used by op 0x10c |
| `0x1c28` | `input_polling_enabled` | `int` | input manager (ctx+0x408) +0x1820, the gate every poller checks (GetAsyncKeyState buttons and keys, GetCursorPos); input_set_polling_enabled@0x460180 stores (disable == 0). age_main_window_proc clears it on Alt and WM_ENTERMENULOOP, and on WM_ACTIVATE(WA_INACTIVE) when display_windowed is set, so a windowed AGE ignores polled input while inactive (read 0 live, 2026-11-03) |
| `0x1c2c` | `input_hwnd` | `void*` | input manager +0x1824: the window input_poll_cursor_client passes to ScreenToClient after GetCursorPos (the main window, read live 2026-11-03) |
| `0x1c34` | `mouse_wheel_delta` | `int` | signed WM_MOUSEWHEEL delta accumulated by age_main_window_proc; op 0x10d returns and clears it |
| `0x4d7c` | `shared_profile_state` | `void*` | embedded shared SAVE.DAT state object; owns profile integer/settings tables and container timing metadata |
| `0x5190` | `shared_profile_int_table` | `int` | open-addressing 12-byte string-key to 32-bit value table; op 0x1a2 stores, 0x1a3 loads, shared SAVE.DAT serializes it |
| `0x5650` | `hotspot_count` | `int` | number of records in the native hotspot table at ctx+0x5248 (table +0x408; at most 100); input_hotspot_register_rect_callbacks appends |
| `0x6144` | `hotspot_hovered` | `int` | the hotspot record whose enter callback last ran, -1 none (table +0xefc). It follows hotspot_hit only when a wait polls (input_hotspot_poll_hover_callback@0x4040b0: leave first, then enter on the next poll); op 0x93's reset clears it, and ops 0x90/0x7c and clicks keep it |
| `0xc6dc` | `hotspot_owner_script_id` | `int` | packed script id of the frame that last registered a hotspot (table +0x7494; op 0x90 and the named-hotspot register). hotspot_check_owner_script@0x4079b0 compares it with the running frame before any hotspot callback dispatch and throws 'Depth が不正です <running> != <owner>' (flags 0x10007) on a mismatch |
| `0xc6e4` | `hotspot_wait_armed` | `int` | op 0x94 sets 1, op 0x93 clears; while nonzero adv_input_service_poll resolves op-0x97 bound actions and hover/click hotspots |
| `0xc6e8` | `hotspot_tracking_active` | `int` | table +0x74a0: input_hotspot_arm_cursor_tracking@0x404230 sets 1. A WM_MOUSEMOVE hit-tests the table while this or hotspot_wait_armed is set (input_mouse_move_update_hotspot_hits@0x485db0) |
| `0xc6f0` | `hotspot_pending_enter` | `int` | table +0x74a8: set when a poll returns a leave callback, so the next poll returns the hovered record's enter; ops 0x90 and 0x7c and the bound-action/Auto click dispatches clear it |
| `0xc6f8` | `hotspot_hit` | `int` | table +0x74b0: the record under the pointer at the last hit test (input_hotspot_update_cursor_hit), -1 none. Only a WM_MOUSEMOVE at a new point and the first op-0x94 arm after a registration hit-test; ops 0x90 and 0x7c and the bound-action/Auto click dispatches set -1. A click takes this record's callback (engine-re.md, 'The hit and the hovered record') |
| `0x144e0` | `sfx_channel_resource_ids` | `int` | base of ten packed resource ids retained by the sound-effect facade; op 0xb4 loads a channel, op 0xb6 clears it, and numbered-save layouts restore then reopen every positive id |
| `0x1493c` | `voice_mute_next_flags` | `uint` | voice-facade +0x434: op 0x1d5 sets 1; op 0xc4 promotes it to 0x10001 so voice_play_indexed_asset plays that voice at volume 0, and the following voice (or op 0x1bc) clears it |
| `0x14d54` | `adv_text_layout_ptr_table` | `void*` | text_manager at ctx+0x14940 plus layout pointer table +0x414; ops 0x212/0x213 configure each layout's wait-indicator handle and retained glyph-object range at layout+0x64/+0x68/+0x6c |
| `0x14e08` | `gfx_default_object_slot` | `int` | op 0x80 selected slot; op 0x1d9 substitutes it when its explicit object-slot operand is zero |
| `0x14e0c` | `text_primary_font_height` | `int` | primary LOGFONTA.lfHeight at text-manager+0x4cc; stored as a negative pixel height and updated by op 0x75 |
| `0x14e10` | `text_primary_font_width` | `int` | primary LOGFONTA.lfWidth at text-manager+0x4d0; op 0x75 stores negative half the requested font size |
| `0x14e1c` | `text_primary_font_weight` | `int` | primary LOGFONTA.lfWeight at text-manager+0x4dc; op 0x2bd writes 700 for enabled or zero for disabled |
| `0x14e84` | `text_antialias_enabled` | `int` | text-manager+0x544; message:UseAntiFont selects GGO_GRAY4_BITMAP when nonzero and GGO_BITMAP when zero. Constructed 0; startup applies UseAntiFont only under set:EnableAntiFont; with it 0 the ADV builders and History draw through text_draw_string_primary_or_ruby, offset by the effect offset |
| `0x14e88` | `text_antialias_version` | `int` | text-manager+0x548 loaded from set:AntiFontVersion; Himegari SYS4INI supplies version 3 |
| `0x14e8c` | `text_primary_color_bgr` | `uint` | text-manager+0x54c internal GDI BGR/COLORREF primary glyph color; op 0x76 converts script RGB into it |
| `0x14e90` | `text_effect_color_bgr` | `uint` | text-manager+0x550 internal GDI BGR/COLORREF effect glyph color; op 0x77 converts script RGB into it |
| `0x14e98` | `text_render_mode` | `int` | text-manager+0x558; op 0x78 selects primary-only, displaced shadow, reduced-coverage repeat, or sampled ellipse outline |
| `0x14ea0` | `text_line_spacing` | `int` | extra pixel leading between text lines; defaults to 6, op 0x8b writes it |
| `0x14ea4` | `text_effect_offset_x` | `int` | text-manager+0x564; op 0x1a4 horizontal displacement for mode 1 or ellipse radius for mode 3 |
| `0x14ea8` | `text_effect_offset_y` | `int` | text-manager+0x568; op 0x1a4 vertical displacement for mode 1 or ellipse radius for mode 3 |
| `0x14eac` | `adv_wait_indicator_follow_last_glyph` | `int` | text-manager+0x56c; op 0x1b1 selects fixed layout-origin anchoring (0) or adds the final retained glyph record's left/top to the wait-indicator destination (1) |
| `0x14f45` | `script_frame_index` | `int` | call-script frame index (0x1e-dword frames) |
| `0x46188` | `renderer_device_state` | `int` | embedded D3D9 device/render state; op 0x20e passes this base to the target/depth clear worker |
| `0x46598` | `d3d9_device` | `void*` | IDirect3DDevice9 pointer at renderer_device_state+0x410; op 0x20e invokes vtable Clear +0xac |
| `0x46614` | `retained_gfx_manager` | `int` | embedded retained graphics manager passed by gfx opcode handlers; worker offsets are relative to this base |
| `0x46a1c` | `gfx_obj_registry` | `int` | retained_gfx_manager+0x408 std::map handle->object; 0x1f6 clears, geometry/draw get-or-create, 0x215 queries, 0x1f7 erases |
| `0x46d14` | `surface_record_resource_id` | `int` | first resource-id word of the live inline stride-0x14 surface-record table (gfx manager +0x700; 1000 x {id, key, reload, policy, created}); op 0x216 reads base + slot*0x14, asset loads maintain resource IDs and empty/blank records use -1; op 0x71 copies it to surface_record_snapshot |
| `0x4bb34` | `surface_record_snapshot` | `int` | first word of the page snapshot of the surface-record table (gfx manager +0x5520, 1000 x 0x14); op_0x71_reset_adv_text_layout copies the live table here, context_state_serialize writes it to numbered saves, and ops 0x258/0x259 write +0x08/+0x0c in both tables |
| `0x50ba4` | `gfx_render_target_table` | `void*` | retained_gfx_manager+0xa590 array of per-slot D3D texture wrappers selected by op 0x20d |
| `0x51b44` | `gfx_current_render_target` | `int` | retained_gfx_manager+0xb530 selected surface slot; -1 denotes device backbuffer |
| `0x51b64` | `frame_timer_current` | `int` | current shared millisecond presentation timestamp at retained_gfx_manager+0xb550; active outer-engine ticks and op 0x23c sample timeGetTime, and every retained animation channel samples it |
| `0x51b68` | `frame_timer_previous` | `int` | previous shared presentation timestamp at retained_gfx_manager+0xb554; shifted from current before each new sample, and op 0x231 compares current/previous source cells to raise redraw dirty only at cell changes |
| `0x51b6c` | `gfx_dirty_a` | `int` | retained_gfx_manager+0xb558 redraw-dirty flag; retained mutations and continuous channels set it, while op 0x231 sets it only when the sampled source cell differs across the shared current/previous frame timestamps; op 0x1f6 clears it |
| `0x51b70` | `gfx_force_complete` | `int` | retained_gfx_manager+0xb55c requests forced completion of one-shot channels; op 0x1f6 clears it |
| `0x51b74` | `gfx_dirty_b` | `int` | retained_gfx_manager+0xb560 blocking-presentation dirty flag; gfx_render_frame clears it at the start of each render, and the one-shot channel setters, gfx_object_apply_transform_channels (a finite channel still running, unless obj+0x2d0 bit 0 detaches it) and the surface-command composite set it; gfx_animation_service_poll returns it once the anim clock window is over, so the run-state-0x400 wait (op 0x21c) ends on the first tick whose render leaves it 0 |
| `0x51b78` | `anim_clock_start` | `int` | start timestamp for the separate finite-animation service window at retained_gfx_manager+0xb564; seeded from frame_timer_current by gfx_animation_service_poll, while op 0x238, op 0x243, and object reset 0x1f6 zero it |
| `0x51b7c` | `anim_clock_duration` | `int` | global anim clock duration at retained_gfx_manager+0xb568; op 0x238 sets and object reset 0x1f6 zeroes it |
| `0x51b80` | `gfx_animation_service_flags` | `uint` | retained_gfx_manager+0xb56c service flags; bit 1 suppresses op 0x243 force-complete/clock-reset request |
| `0x52bd4` | `surface_movies` | `void*` | per-surface CMovieToTexture objects [1000 slots], allocated by ops 0x20f/0x236; ops 0x1fa/0x23d and scene_context_init_reset stop and delete them. The textures live in the gfx manager (+0xa590 of ctx+0x46614) |
| `0x53b74` | `engine_version` | `uint` | AGE.EXE four-character engine version (Himegari '424J', read as 0x4a343234; its 2019 exe wrote '422B'; Kamidori '433F'; the constructor copies it from 0x571218, which the packed exe's /v2 dump shows as placeholder '000 '); engine_initialize_subsystems_from_settings prints it as ARCGameEngine Ver %c.%c%c%c; every save writer (shared_profile_save, op 0x19e, RT.DAT) stores it as header +0x04 compatibility id, which no reader checks; engine_load_sys4ini_and_mount_append_catalogs compares its first three characters with sys4ini_required_engine_version |
| `0x53c7c` | `bank_size_int_globals` | `int` | live integer globals bank size used to allocate the VM bank (copied from sys4ini_int_globals_count by engine_load_sys4ini_and_mount_append_catalogs@0x4099f0); save_data_deserialize_and_begin_restore@0x40fd10 zeroes the whole bank to this size before copying a numbered save's prefix (layouts 2 and 3) |
| `0x53c80` | `bank_size_float_globals` | `int` | live float globals bank size used to allocate the VM bank (copied from sys4ini_float_globals_count by engine_load_sys4ini_and_mount_append_catalogs@0x4099f0); save_data_deserialize_and_begin_restore@0x40fd10 zeroes the whole bank to this size before copying a numbered save's prefix (layouts 2 and 3) |
| `0x53c84` | `bank_size_string_globals` | `int` | live string globals bank size used to allocate the VM bank (copied from sys4ini_string_globals_count by engine_load_sys4ini_and_mount_append_catalogs@0x4099f0); save_data_deserialize_and_begin_restore@0x40fd10 zeroes the whole bank to this size before copying a numbered save's prefix (layouts 2 and 3) |
| `0x53c88` | `bank_size_int_pointer_globals` | `int` | live integer-pointer globals bank size used to allocate the VM bank (copied from sys4ini_int_pointer_globals_count by engine_load_sys4ini_and_mount_append_catalogs@0x4099f0); save_data_deserialize_and_begin_restore@0x40fd10 zeroes the whole bank to this size before copying a numbered save's prefix (layouts 2 and 3) |
| `0x53c8c` | `bank_size_string_pointer_globals` | `int` | live string-pointer globals bank size used to allocate the VM bank (copied from sys4ini_string_pointer_globals_count by engine_load_sys4ini_and_mount_append_catalogs@0x4099f0); save_data_deserialize_and_begin_restore@0x40fd10 zeroes the whole bank to this size before copying a numbered save's prefix (layouts 2 and 3) |
| `0x53c90` | `bank_size_local_pointer_scratch` | `int` | live local pointer scratch bank size used to allocate the VM bank (copied from sys4ini_local_pointer_scratch_count by engine_load_sys4ini_and_mount_append_catalogs@0x4099f0); save_data_deserialize_and_begin_restore@0x40fd10 zeroes the whole bank to this size before copying a numbered save's prefix (layouts 2 and 3) |
| `0x53d14` | `cur_ctx_index` | `uint` | current script-context index (curCtx); indexes 0x78-byte coroutine/frame records |
| `0x53d24` | `save_load_stack_restore_active` | `int` | set while a deserialized numbered-save script stack is being reconstructed; op 0xae restores saved frame PCs/contexts and clears it at the terminal saved context |
| `0x53d28` | `frame_codebase` | `void*` | current frame codebase (PC = codebase + off*4) |
| `0x53d2c` | `frame_pc` | `int` | current frame PC column (op = *(0x53d2c + curCtx*0x78)) |
| `0x53d60` | `ctx_record_base` | `void*` | 0x78-byte context-record array base (coroutine/script contexts) |
| `0x53d64` | `frame_script_resource_id` | `uint` | raw packed SYS4/AAI resource id for this 0x78-byte script frame; persisted ReadTextDB script key |
| `0x53d88` | `frame_instruction_word_count` | `int` | current decoded instruction length in dwords for each 0x78-byte script frame; interpreter advances PC by this value * 4 |
| `0x54fe8` | `owner_window_handle` | `void*` | native owner HWND forwarded to host UI/message-box calls, including op 0x1b4 diagnostic presentation |
| `0x54ff0` | `initial_root_run` | `int` | initialized to 1 at AGE context construction; queried by op 0x130 and cleared by op 0x9 before root script id 0 is reloaded |
| `0x54ff4` | `message_pump_mask` | `uint` | engine_main_tick_with_exception_policy sets 4 on entry; the loop drains the Win32 message queue whenever message_pump_counter & this is nonzero, i.e. every third iteration |
| `0x550fc` | `message_skip_display_enabled` | `int` | persistent all-message Skip state returned by op 0x19a for the ADV control-strip active overlay |
| `0x55100` | `adv_skip_service_enabled` | `int` | ADV presentation lifecycle gate: scene_context_init_reset sets it, op 0x19b clears it while preserving the persistent Skip toggle, and op 0x19c sets it before recomputing active message/read skip; the menu bar's ﾒｯｾｰｼﾞｽｷｯﾌﾟ and ｵｰﾄﾒｯｾｰｼﾞ toggles (adv_toggle_skip_mode@0x406c20, adv_toggle_auto_mode@0x406b70) change the active fast-forward state only while it is set |
| `0x55104` | `auto_message_enabled` | `int` | ADV Auto mode flag; op 0x1b6 reads, op 0x1b7 writes, adv_input_service_poll consumes |
| `0x55110` | `text_history_recording_suppressed` | `uint` | high bit suppresses ADV retained-history index/text/metadata/voice recording; op 0x1bb writes 0x80000000 or zero, and HISTORY.BIN brackets itself with disable/enable |
| `0x5511c` | `system_menu_show_delay_ms` | `uint` | op 0x149 replaces the TIMER_SHOWMENU top-edge dwell threshold; reset defaults to 0 and SYSTEM4 sets 1000; op 0x148 reads it |
| `0x55120` | `anti_tamper_a` | `int` | anti-tamper checksum operand |
| `0x55124` | `anti_tamper_b` | `int` | anti-tamper checksum operand |
| `0x5512c` | `anti_tamper_fp` | `int` | anti-tamper (import fn ptr / result) |
| `0x55130` | `int_queue_slots` | `void*` | base of ten scene-context-owned integer FIFO pointers used by ops 0x132-0x134; their <=10 validation bug makes id 10 alias int_stack_slots[0] |
| `0x55158` | `int_stack_slots` | `void*` | base of ten scene-context-owned integer LIFO pointers used by ops 0x137-0x139; their <=10 validation bug makes id 10 alias numeric_glyph_styles[0] |
| `0x55180` | `numeric_glyph_styles` | `int` | base of ten 20-byte decimal-glyph atlas records used by ops 0x13a/0x23b; opcode validation also admits one out-of-range record |
| `0x55248` | `ret_stack_a` | `void*` | per-frame return stack (op 0x8f call pushes) |
| `0x552e8` | `ret_stack_b` | `void*` | per-frame return stack (companion) |
| `0x5f2fc` | `statement_block_active` | `int` | set by the outermost op 0x1f4 and cleared by its matching op 0x1f5; while set the outer tick defers delayed SFX/voice/movie service and its own gfx_render_frame (unless run_state_flags bit 0x400) plus the 0x1000 timer and 0x2000 modal service; explicit op 0x20c still renders and script_launch_queue_dispatch_next refuses to dispatch |
| `0x5f300` | `statement_block_depth` | `int` | nested op 0x1f4 count beyond the outermost block; op 0x1f5 decrements it before clearing statement_block_active |
| `0x5f304` | `sleep_timer` | `int` | sleep timer object (op 0xc8; +8 active, +0x14 start-ms, +0x18 duration) |
| `0x5f320` | `mouse_callback_timer` | `int` | op-0xcc poll interval timer object (same layout as sleep_timer); op 0xcd dispatches once +0x14 start-ms is +0x18 interval old, then restarts it |
| `0x5f33c` | `timed_catchup_callback_timer` | `int` | op-0xce interval timer object (same layout as sleep_timer), sampled by op 0xcf; unused in Himegari and Kamidori |
| `0x5f3ac` | `timed_callback_timer` | `int` | elapsed timer object armed by op 0xd5 and sampled by the timed callback scheduler |
| `0x5f400` | `bgm_fade_period_timer` | `int` | 10 ms period timer driving the run-state 0x200 BGM fade; the tick polls period_timer_poll_elapsed_periods@0x44d0f0 and feeds bgm_fade_tick@0x464960 (armed at 0x411e46 for the voice duck release) |
| `0x5f630` | `adv_glyph_reveal_timer` | `int` | ADV glyph-reveal period timer (timer 29 of the 32 0x1c-byte timers from sleep_timer; constructor 0x44cfb0: period 0, index 1). Only op 0x6e arms it (sleep_timer_arm, period = glyph delay); op 0x196 never does. In draw mode 1 adv_text_reveal_service_tick@0x406fa0 publishes one tick per successful period_timer_poll_elapsed_periods@0x44d0f0, so a tick waits a full delay and late polls drop boundaries |
| `0x5f64c` | `adv_wait_indicator_timer` | `int` | elapsed timer started by op 0x1ce and ADV wait setup to select the animated input-wait indicator frame |
| `0x5f684` | `mouse_callback_target_pc` | `int` | op-0xcc callback dword offset, one engine-wide registration; -1 makes op 0xcd pop its return pc and continue. scene_context_init_reset keeps it |
| `0x5f688` | `timed_catchup_callback_pc` | `int` | op-0xce on-time callback dword offset, dispatched by op 0xcf |
| `0x5f68c` | `timed_catchup_callback_catchup_pc` | `int` | op-0xce catch-up callback dword offset; op 0xcf pushes it once per further elapsed interval (at most 128) |
| `0x5f690` | `timed_callback_abort_pc` | `int` | optional local fallback PC supplied to op 0xd5; -1 disables abort redirection |
| `0x5f694` | `timed_callback_entries_vector` | `int` | MSVC vector object base for 16-byte schedule entries; begin/end/capacity pointers follow |
| `0x5f698` | `timed_callback_entries_begin` | `void*` | begin pointer for 16-byte entries {deadline_ms, field_100, primary_pc, catchup_pc} |
| `0x5f69c` | `timed_callback_entries_end` | `void*` | end pointer for the timed callback entry vector |
| `0x5f6a0` | `timed_callback_entries_capacity` | `void*` | capacity-end pointer for the timed callback entry vector |
| `0x5f6a4` | `timed_callback_last_index` | `int` | last appended schedule index; op 0xd3 resets to -1 and op 0xd4 increments per entry |
| `0x5f6a8` | `timed_callback_cursor` | `int` | next schedule entry consumed by timed_callback_sequence_tick; op 0xd3 resets to zero |
| `0x5f6ac` | `mouse_callback_owner_script_id` | `int` | packed script id of the frame that registered the op-0xcc mouse callback; op 0xcd throws the Depth error when another frame dispatches it |
| `0x5f6b0` | `timed_catchup_callback_owner_script_id` | `int` | packed script id of the frame that ran op 0xce; op 0xcf throws a Depth error (named, or raw ids via 0x5714c0) when another frame dispatches it |
| `0x5f6b4` | `timed_callback_script_resource_id` | `uint` | active frame script resource retained by op 0xd5 for local callback dispatch safety |
| `0x5f6b8` | `timed_callback_script_resource_guard` | `uint` | running script recorded by op 0xd5 when its cursor is 0; timed_callback_sequence_tick throws the Depth error when another script's frame would dispatch an entry |
| `0x5f6bc` | `coroutine_yield_owner_script_id` | `int` | packed script id of the frame whose ADV coroutine yielded (op 0x199 / input dispatch); op 0x7c throws the Depth error when another frame resumes it |
| `0x5f6c0` | `value_switch_table` | `int` | temporary formatted-value branch table reset by 0xa1, populated by 0xa2, queried by 0xa3; distinct from shared_profile_int_table |
| `0x5f72c` | `adv_wait_indicator_frame` | `int` | current animated input-wait indicator frame, reset to zero when the service is armed |
| `0x5f730` | `adv_wait_indicator_terminal_frame` | `int` | terminal frame returned by the indicator layout during ADV wait setup |
| `0x5f734` | `adv_wait_indicator_enabled_value` | `int` | raw enable operand last written by op 0x1ce; nonzero arms the ADV wait-indicator service |
| `0x6da6c` | `input_callback_scan_index` | `int` | op 0xff resets this to zero; op 0x100 advances it while scanning set action bits below input_action_count |
| `0x6da70` | `input_action_count_snapshot` | `int` | op 0xff copies input_action_count here when beginning a callback poll/dispatch pass |
| `0x6da74` | `reveal_physical_skip_suppressed` | `int` | op 0x101 sets it (0x4170df); while set, a held action 6 starts no fast-forward at a reveal tick. Only adv_text_reveal_service_tick clears it, at a tick with action 6 up (0x407101) |
| `0x6da78` | `message_skip_activation_guard` | `int` | set when op 0x88 enables Skip; suppresses the enabling click from immediately becoming a hotspot activation |
| `0x6da7c` | `system_key_disable_mask` | `int` | op 0x10f stores its operand here; bit 0 blocks the F1 screenshot (engine_save_screenshot_bmp) and bit 0x200 swallows WM_SYSKEYDOWN F10 in age_main_window_proc. The constructor and scene_context_init_reset clear it |
| `0x6da80` | `message_skip_cancel_click_phase` | `int` | adv_interpreter_tick CancelMesSkipOnClick state (setting nonzero): 0 idle, 1 armed after a tick with action 4 clear, 2 pressed; release in 2 cancels Skip/ReadTextSkip (value 2 also launches CALLBACK_SETTING.BIN). op 0x101 resets it to 0 |
| `0x6da84` | `adv_wait_indicator_layout_slot` | `int` | ADV text layout whose wait indicator is armed or erased by input-wait setup and op 0x1ce |
| `0x6da88` | `coroutine_yield_a` | `void*` | op 0x7b yield-state save (op1 -> +ctxidx*4) |
| `0x6db28` | `coroutine_yield_b` | `void*` | op 0x7b yield-state save (op2 -> +ctxidx*4) |
| `0x6dbc8` | `coroutine_runstate` | `int` | op 0x7c resume gate (run-state bit 0x2000000) |
| `0x6dbcc` | `coroutine_resume_off` | `int` | op 0x7c resume PC offset |
| `0x6dbd4` | `adv_read_skip_state` | `int` | current ADV read/click-skip service state; queried by op 0x1cc, maintained from ReadTextSkip + per-PC read history |
| `0x6dbe4` | `adv_auto_voice_pending` | `int` | set by play-voice ops when voice service exists; Auto waits for voice completion then arms AutoMessageTime0; op 0x1bc resets |
| `0x6dbe8` | `voice_bgm_duck_saved_volume` | `int` | BGM volume saved by the voice-start duck helper before applying MusicFadeOnVoicePlayingVol |
| `0x6dbec` | `voice_bgm_duck_release_armed` | `int` | armed by op 0x1bf and the tick's post-delayed-voice path unless voice_bgm_duck_control_flags bit 0 suppresses ducking; cleared by voice_bgm_duck_begin. The tick restores ducked BGM after the voice when set; op 0xbf skips re-applying the duck volume while set |
| `0x6dbf0` | `voice_bgm_duck_control_flags` | `uint` | transient mask replaced by op 0x1cf; bit 0 suppresses automatic voice-triggered BGM attenuation |
| `0x6dbf4` | `message_skip_queued_voice_id` | `int` | latest voice resource deferred by op 0xc4 while run_state_flags message-skip bit is active |
| `0x6dbf8` | `message_skip_queued_voice_arg` | `int` | second argument retained with message_skip_queued_voice_id; Himegari op 0xc4 stores zero |
| `0x6ddac` | `opcode_debug_metadata_table` | `void*` | base of 512 optional per-opcode debug-metadata pointers; Himegari's release image zero-initializes the table and has no writer, so diagnostic COMMAND names render as '-' |
| `0x6f798` | `script_source_line_table` | `void*` | base of 41 optional per-frame source-line-map pointers; Himegari's release image zero-initializes the table and has no writer, so diagnostic LINE values render as -1 |
| `0x6f86c` | `debug_reload_resume_active` | `int` | engine_reload_current_script_and_reset_input@0x42e930 (the debug menu's BIN reload) sets it with a resume line; op 0x19c keeps ctx+0x13dc and run-state 0x8000000 while it is set and nothing else restores them. The resume line comes from script_source_line_table, which release images never fill, so the reload clears it again: unreachable in Himegari and Kamidori. The constructor and scene_context_init_reset clear it |
| `0x6f870` | `debug_reload_resume_line` | `int` | the debug reload's resume source line, 10 before the current one (0 cancels the resume) |
| `0x6f874` | `debug_reload_resume_frame` | `int` | the frame index (cur_ctx_index) the debug reload resumes in |
| `0x6f880` | `diagnostic_text_buffer` | `int` | base of an embedded 28-byte MSVC string accumulator; op 0x1b2 appends formatted operand text, op 0x1b3 appends CRLF, and op 0x1b4 presents then clears it |
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
| `0x99294` | `tick_presentation_enabled` | `int` | op 0x21b stores (operand != 0); under gfx_draw_mode 1 the outer tick advances frame_timer_current and may render only when set or run-state 0x2400 is active; op 0x247 queries it |
| `0x9a298` | `display_windowed` | `int` | nonzero while AGE runs windowed (read 1 live with SYS4REG display.ScreenMode=1 and an 806x649 framed window, 2026-11-03). age_main_window_proc: WM_ACTIVATE(WA_INACTIVE) disables input polling only when set; when zero (fullscreen), WM_ACTIVATEAPP re-tops the window and centres the cursor at the next WM_PAINT; SC_KEYMENU+Enter toggles the mode through display_toggle_screen_mode_and_menu_bar@0x412df0, which attaches the native menu bar (menu 110) to a window and detaches it for fullscreen; zero also gates the top-edge system-menu strip, which only fullscreen reveals |
| `0x9b240` | `presentation_suspended_for_dialog` | `int` | draw mode 1 only: engine_suspend_presentation_for_dialog@0x405e10 (IAGEService +0x14, which agerc_menu_command calls before its dialogs only in fullscreen) sets 1 alongside run-state 0x200000; engine_resume_presentation_after_dialog@0x405fc0 (+0x18) clears both. The game frame stays as last presented, undimmed |
| `0x9b24c` | `dispatch_table` | `void*` | opcode->handler table base [0x400]; handler(op) = *(0x9b24c + op*4) |
| `0x9c658` | `sys4ini_count` | `int` | SYS4INI record count at embedded FileDB+0x40c |
| `0x9c65c` | `archive_name_table` | `void*` | archive-name table base at embedded FileDB+0x410; arc_id*0x100 indexes it |
| `0x9c660` | `sys4ini_records` | `void*` | SYS4INI 80-byte record base at embedded FileDB+0x414; record = base + id*0x50 |
| `0x9f274` | `mounted_aai_catalogs` | `void*` | base of 256-entry selector-keyed AAI catalog-pointer table inside the embedded FileDB; op 0x143 scans slots 1..255 from +0x9f278 |
| `0x9f278` | `mounted_aai_catalog_selector_1` | `void*` | selector-one cell and op 0x143 scan start; subsequent dwords are selector 2..255 |
| `0xa0b84` | `current_bgm_track_id` | `int` | direct-name BGM track id retained by the music facade; op 0xbf starts/replaces it, ops 0xc0/0xc3 get/set it, and numbered-save layouts restore it |
| `0xa0b8c` | `current_bgm_start_mode` | `int` | logical playback mode retained by the music facade and forwarded to its backend: zero stops at decoder EOF, nonzero rewinds; ordinary op 0xbf and forced op 0xb7 use one, forced op 0xb9 uses zero |
| `0xa0bb8` | `sys4ini_magic` | `uint` | SYS4INI header +0x00 (the loader reads the first 300 header bytes into ctx+0xa0bb8); save_container_read_and_validate_header expects S4SD save magic when it starts 'S4', else S3SD |
| `0xa0bbc` | `sys4ini_required_engine_version` | `uint` | SYS4INI header +0x04: engine version the game data needs; engine_load_sys4ini_and_mount_append_catalogs compares its first three characters with engine_version |
| `0xa0bc0` | `sys4ini_game_id` | `uint` | SYS4INI header +0x08: start of the NUL-terminated cp932 game id (Himegari 姫狩りダンジョンマイスター); save and RT.DAT writers store it at header +0x08 and every reader requires an exact match |
| `0xa0ca8` | `sys4ini_saved_int_globals_count` | `int` | SYS4INI header +0xf0 (the loader reads the first 300 header bytes into ctx+0xa0bb8): saved-prefix length of the integer globals bank that numbered layout-3 saves serialize; Himegari/Kamidori 402459/1037327 |
| `0xa0cac` | `sys4ini_saved_float_globals_count` | `int` | SYS4INI header +0xf4 (the loader reads the first 300 header bytes into ctx+0xa0bb8): saved-prefix length of the float globals bank that numbered layout-3 saves serialize; Himegari/Kamidori 1/1 |
| `0xa0cb0` | `sys4ini_saved_string_globals_count` | `int` | SYS4INI header +0xf8 (the loader reads the first 300 header bytes into ctx+0xa0bb8): saved-prefix length of the string globals bank that numbered layout-3 saves serialize; Himegari/Kamidori 789/802 |
| `0xa0cb4` | `sys4ini_saved_int_pointer_globals_count` | `int` | SYS4INI header +0xfc (the loader reads the first 300 header bytes into ctx+0xa0bb8): saved-prefix length of the integer-pointer globals bank that numbered layout-3 saves serialize; Himegari/Kamidori 1/1 |
| `0xa0cb8` | `sys4ini_saved_string_pointer_globals_count` | `int` | SYS4INI header +0x100 (the loader reads the first 300 header bytes into ctx+0xa0bb8): saved-prefix length of the string-pointer globals bank that numbered layout-3 saves serialize; Himegari/Kamidori 1/1 |
| `0xa0cbc` | `sys4ini_saved_local_pointer_scratch_count` | `int` | SYS4INI header +0x104 (the loader reads the first 300 header bytes into ctx+0xa0bb8): saved-prefix length of the local pointer scratch bank that numbered layout-3 saves serialize; Himegari/Kamidori 1/1 |
| `0xa0cc0` | `screen_w` | `int` | logical screen width; constructor/default registry uses 640 and the SYS4INI header +0x108 copy also holds 640, then the SYS4INI SCREENX setting overrides it (Himegari 800) |
| `0xa0cc4` | `screen_h` | `int` | logical screen height; constructor/default registry uses 480 and the SYS4INI header +0x10c copy also holds 480, then the SYS4INI SCREENY setting overrides it (Himegari 600) |
| `0xa0cc8` | `screen_bpp` | `int` | screen bpp; SYS4INI header +0x110 copy holds 16 in both Himegari and Kamidori |
| `0xa0ccc` | `sys4ini_int_globals_count` | `int` | SYS4INI header +0x114: full integer globals bank size, copied to bank_size_int_globals; Himegari/Kamidori 1419442/4014849 |
| `0xa0cd0` | `sys4ini_float_globals_count` | `int` | SYS4INI header +0x118: full float globals bank size, copied to bank_size_float_globals; Himegari/Kamidori 2/2 |
| `0xa0cd4` | `sys4ini_string_globals_count` | `int` | SYS4INI header +0x11c: full string globals bank size, copied to bank_size_string_globals; Himegari/Kamidori 18180/55284 |
| `0xa0cd8` | `sys4ini_int_pointer_globals_count` | `int` | SYS4INI header +0x120: full integer-pointer globals bank size, copied to bank_size_int_pointer_globals; Himegari/Kamidori 2/2 |
| `0xa0cdc` | `sys4ini_string_pointer_globals_count` | `int` | SYS4INI header +0x124: full string-pointer globals bank size, copied to bank_size_string_pointer_globals; Himegari/Kamidori 2/2 |
| `0xa0ce0` | `sys4ini_local_pointer_scratch_count` | `int` | SYS4INI header +0x128: full local pointer scratch bank size, copied to bank_size_local_pointer_scratch; Himegari/Kamidori 2/2 |
| `0xa0ce4` | `run_state_flags` | `uint` | interpreter run-state flags (bit1 sleeping; 0x1000000 minimized/suspended: WM_SYSCOMMAND SC_MINIMIZE sets it and SC_RESTORE clears it for a windowed game, and an unforced WM_CLOSE then quits without the 終了 dialog; 0x8000000 skip/fast-forward; 0x40000000 wait indicator shown; 0x80000000 ADV text wait polling) |
| `0xa0ce8` | `service_action_trigger_mask` | `uint` | triggered logical-action bits the blocking services poll (input_poll_triggered_action_mask ORs keys, clicks and joystick in); WM_MOUSEWHEEL under run-state 0x90100000 ORs in set:WheelKeyUp/Down's bit; cleared when op 0xd5 starts, and its abort test redirects to the abort PC |
| `0xa0cec` | `message_pump_counter` | `uint` | engine_main_tick_with_exception_policy's iteration counter: 1 after each PeekMessage drain, incremented per iteration; run-state 0 dispatches one op per iteration, so messages (a WM_MOUSEMOVE from op 0x10a's SetCursorPos) are handled within three ops |
| `0xa0d10` | `system_menu_actions_enabled` | `int` | op 0x142 replaces this AGERC/IAGEService reentrancy guard; scene reset defaults it to 1, while CONFIG brackets its scripted settings UI with 0 then 1 |
