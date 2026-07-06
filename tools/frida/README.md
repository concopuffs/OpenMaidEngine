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

- `capture_graphics.py` — hooks `ReadFile` on the graphics archives (`DATA2/DATA5*.ALF`), resolving
  each handle→path via `GetFinalPathNameByHandleW` (cached) + the read offset. Log →
  `build/frida-reads.log` (`path<TAB>offset<TAB>size`).

## Known limitations (see docs/asset-resolution-re.md)

- **File-I/O offsets are noisy** — spans don't match extracted AGF sizes; the game likely
  **memory-maps** the archives (so `ReadFile` offsets are OS paging noise, not clean per-asset
  loads) and/or uses async/`OVERLAPPED` reads. The robust hook is the game's **internal
  load-by-id function** (find via the opcode dispatch), not file I/O — a future tool.
- Correlation still needs the **`SYS4INI` (S4IC422) asset index** parsed to turn an archive offset
  into a filename. That parser is the first foundational RE step.
