# Frida runtime-observation tools

Runtime instrumentation of the **live game** (`AGE.EXE`) to capture ground truth for the
subsystems that have **no static/machine oracle** — asset resolution, graphics, audio, and the
opaque effectful opcodes. Frida attaches to the *running* (unpacked-in-memory) process, so the
game's packer is bypassed for hooking.

Prereq: `py -3.11 -m pip install frida` (core only — `frida-tools` CLI is not needed and its
`pygments` shim fails to install in this env). Installed: frida 17.15.3.

## Standard workflow

1. Launch the game (via `AGE Patch.exe` to avoid the periodic system-check messagebox) to a safe
   point (e.g. the title). **Attach after launch**, not `frida -f` — spawning the patcher wouldn't
   hook its child `AGE.EXE`, and direct-spawn risks the messagebox.
2. Run the capture script (`py -3.11 -u -X utf8 tools/frida/<tool>.py`) — it attaches by process
   name and installs the hooks; run **unbuffered** (`-u`) so status prints appear immediately.
3. Drive the game through the target scene (SC0000's opening auto-plays on new game).
4. Stop the script; analyse the log in `build/` and **order-correlate** with the engine's op
   sequence (e.g. `vm0`/the C# recording host's `set-texture`/`play-voice` order for that scene).

## Tools

- **`dump_engine.py`** — ★ dumps the **unpacked engine code** from the live process for offline static
  RE (native op handlers). Both on-disk images are the same packed binary (`SYS4AB.BIN` = XOR-0xFF of
  `AGE.EXE`), so the real handler code exists only in memory: the `AGE.EXE` module (some code unpacked
  in-place, e.g. the AGF decoder `+0x74f1f`) plus the main VM interpreter in a large per-run heap `r-x`
  region (~30 MB, nonstable base). Attach → it enumerates ranges, dumps the module image + every r-x
  range ≥ 1 MB (chunked) → `build/engine-dump/{manifest.json,range_<base>.bin}`, and prints the
  landmark bytes at `AGE.EXE+0x74f1f` to validate. Then disassemble (capstone) and locate a handler
  (e.g. `0x215` → real handler `0x42a0b0`, resolved via the opcode dispatch table — Kelebek's `0x421160`
  is VA-drift, an unrelated fn). Attach by pid.
- **`capture_load_order.py`** — ★ the working asset-resolution capture. Hooks `ReadFile` on
  `DATA2.ALF`; each asset load starts with header reads **at its exact archive offset**, so exact-start
  reads give the clean per-asset **load order** (→ names via `build/asset-index.json`). `--analyze`
  prints the order + file_number. This **confirmed `resId == SYS4INI file_number`** (the load order
  matches our engine's `set-texture(resId)` order 1:1). Attach by pid, replay the scene, Ctrl-C, `--analyze`.
- `capture_graphics.py` — original `ReadFile` logger for `DATA2/DATA5` (`path<TAB>offset<TAB>size` →
  `build/frida-reads.log`); pair with `tools/resolve_frida_reads.py`.
- `locate_resource_load.py` — phase-1 locator (back-trace asset-opens → native load chain).
- `capture_resid_args.py` — phase-2 probe (decoder args/context; showed the loader carries only offsets).
- `find_globals_base.py`, `find_global_by_sequence.py` — **runtime global-variable RE (SHELVED)**. Full
  write-up, findings, memory landmarks, and the recommended resume plan: **`docs/global-memory-re.md`**.
  TL;DR: the VM global memory is structured/multi-store (not flat int32) and `G[0x62424]` is a transient
  arg-register — heuristic value scans can't pin it. Resume from a *stable* anchor (the name global
  `0x279`) and target the interpreter's address resolution, not scans.

## Runtime architecture (learned 2026-07-06 — read before writing new hooks)

- **Attach, don't spawn**, and **use the pid** (`frida.get_local_device().enumerate_processes()` — the
  module-level `frida.enumerate_processes()` was removed in frida 17.x). The process is `AGE.EXE`.
- **The game is packed.** Its main VM logic runs from a per-run heap `r-x` region (~30 MB, nonstable
  base). So you **cannot** hook the `set-texture`/VM handlers at a fixed `AGE.EXE+off` — only stable
  library code (e.g. the AGF decoder `AGE.EXE+0x74f1f`) keeps a fixed offset.
- **Archives are NOT memory-mapped.** No archive-sized region exists; the game streams them through a
  small heap **block-cache via `ReadFile`** (128 KB blocks + big reads). The earlier "memory-mapped"
  note was wrong — the 128 KB reads are the block cache, not OS paging.
- **The reliable oracle is the `ReadFile` offset stream** → names via the SYS4INI index
  (`tools/parse_sys4ini.py` → `build/asset-index.json`). Native-handler hooking is blocked by the packer.
