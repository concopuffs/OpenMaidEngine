# EngineCtx field reference

> Generated from `vm-map/engine-ctx.toml` by `tools/engine_ctx_build.py --build`. Do not edit.

Struct `EngineCtx`, size `0xa1000`. Applied to the Ghidra `/v2` image (dispatch-handler `this` = `EngineCtx *`).

| offset | name | type | note |
|---|---|---|---|
| `0x408` | `gfx_obj_registry` | `int` | retained gfx-object map (std::map handle->object); geometry/draw get-or-create, 0x215 returns obj+4 source slot, 0x1f7 erases |
| `0x40c` | `sys4ini_count` | `int` | SYS4INI record count |
| `0x410` | `archive_name_table` | `void*` | archive-name table base (arc_id*0x100 indexes it) |
| `0x414` | `sys4ini_records` | `void*` | SYS4INI 80-byte record base {name[64],arc_id,file_number,offset,size}; record = base + id*0x50 |
| `0x13dc` | `message_skip_enabled` | `int` | persistent all-message Skip flag; op 0x88 writes it and adv_interpreter_tick injects input bit 0x40 while nonzero |
| `0x3028` | `alt_pack_table` | `int` | call-script high-byte alternate pack table (unused by corpus) |
| `0xb558` | `gfx_dirty_a` | `int` | gfx dirty flag (anim set raises) |
| `0xb560` | `gfx_dirty_b` | `int` | gfx dirty flag |
| `0x14d54` | `gfx_obj_ptr_table` | `void*` | per-object pointer table (ops 0x212/0x213 write obj+0x64/0x68/0x6c) |
| `0x14f45` | `script_frame_index` | `int` | call-script frame index (0x1e-dword frames) |
| `0x46d14` | `query_table_46d14` | `void*` | stride-0x14 table read by op 0x216 |
| `0x51b64` | `frame_timer` | `int` | frame timer (present updates 0x51b64/0x51b68) |
| `0x51b78` | `anim_clock_elapsed` | `int` | global anim clock elapsed (op 0x238 zeroes) |
| `0x51b7c` | `anim_clock_duration` | `int` | global anim clock total duration (op 0x238 sets) |
| `0x52bd4` | `surfaces` | `void*` | surface array base [~1000 slots]; create/set-texture (0x1f8/0x1f9) allocate |
| `0x53d14` | `cur_ctx_index` | `uint` | current gfx-object / script-context index (curCtx); indexes 0x78-byte records |
| `0x53d28` | `frame_codebase` | `void*` | current frame codebase (PC = codebase + off*4) |
| `0x53d2c` | `frame_pc` | `int` | current frame PC column (op = *(0x53d2c + curCtx*0x78)) |
| `0x53d60` | `ctx_record_base` | `void*` | 0x78-byte context-record array base (coroutine/script contexts) |
| `0x53d64` | `gfx_obj_record_array` | `void*` | gfx object-record array (field[0]=0xffffffff free; cmd-type at rec+0x24) |
| `0x53d88` | `cmd_type_table` | `int` | per-object cmd-type column base (write *(0x53d88 + curCtx*0x78)) |
| `0x550fc` | `message_skip_display_enabled` | `int` | persistent all-message Skip state returned by op 0x19a for the ADV control-strip active overlay |
| `0x55104` | `auto_message_enabled` | `int` | ADV Auto mode flag; op 0x1b6 reads, op 0x1b7 writes, adv_input_service_poll consumes |
| `0x55120` | `anti_tamper_a` | `int` | anti-tamper checksum operand |
| `0x55124` | `anti_tamper_b` | `int` | anti-tamper checksum operand |
| `0x5512c` | `anti_tamper_fp` | `int` | anti-tamper (import fn ptr / result) |
| `0x55248` | `ret_stack_a` | `void*` | per-frame return stack (op 0x8f call pushes) |
| `0x552e8` | `ret_stack_b` | `void*` | per-frame return stack (companion) |
| `0x5f304` | `sleep_timer` | `int` | sleep timer object (op 0xc8; +8 active, +0x14 start-ms, +0x18 duration) |
| `0x6da78` | `message_skip_activation_guard` | `int` | set when op 0x88 enables Skip; suppresses the enabling click from immediately becoming a hotspot activation |
| `0x6da80` | `message_skip_cancel_click_phase` | `int` | press/release phase used only when set:CancelMesSkipOnClick is nonzero |
| `0x6da88` | `coroutine_yield_a` | `void*` | op 0x7b yield-state save (op1 -> +ctxidx*4) |
| `0x6db28` | `coroutine_yield_b` | `void*` | op 0x7b yield-state save (op2 -> +ctxidx*4) |
| `0x6dbc8` | `coroutine_runstate` | `int` | op 0x7c resume gate (run-state bit 0x2000000) |
| `0x6dbcc` | `coroutine_resume_off` | `int` | op 0x7c resume PC offset |
| `0x6dbd4` | `adv_read_skip_state` | `int` | current ADV read/click-skip service state; queried by op 0x1cc, maintained from ReadTextSkip + per-PC read history |
| `0x6dbe4` | `adv_auto_voice_pending` | `int` | set by play-voice ops when voice service exists; Auto waits for voice completion then arms AutoMessageTime0; op 0x1bc resets |
| `0x6dbf4` | `message_skip_queued_voice_id` | `int` | latest voice resource deferred by op 0xc4 while run_state_flags message-skip bit is active |
| `0x6dbf8` | `message_skip_queued_voice_arg` | `int` | second argument retained with message_skip_queued_voice_id; Himegari op 0xc4 stores zero |
| `0x9b24c` | `dispatch_table` | `void*` | opcode->handler table base [0x400]; handler(op) = *(0x9b24c + op*4) |
| `0xa0cc0` | `screen_w` | `int` | screen width (640) |
| `0xa0cc4` | `screen_h` | `int` | screen height (480) |
| `0xa0cc8` | `screen_bpp` | `int` | screen bpp (8) |
| `0xa0ce4` | `run_state_flags` | `uint` | interpreter run-state flags (bit1 sleeping; 0x8000000 skip/fast-forward) |
