# Game driving: one step language, two backends — design

**Date:** 2026-11-03
**Status:** accepted; M1 to M4 are built. §13 to §16 record where the build differs from the design.
**Home in the canonical map:** this file is the design. Tool usage stays in `docs/tools-reference.md` ("Game
driver"). Native engine findings stay in `docs/engine-re.md` ("Driving native AGE"). Directory layout
stays in `docs/PROJECT-STRUCTURE.md`.

## 1. Purpose

Agents and scripts must be able to play both native AGE and the port: click, press keys, read where the VM is, wait
for a state, take screenshots, and use sandboxed saves. One scenario file should run on either game, and the tools
should be reusable rather than built for one test.

Two things prompted this:

- **The port cannot be driven like native AGE.** `native_drive.py` replaces AGE's OS input calls with Frida.
  Applying the same idea to the port with posted window messages half works:
  - keys and button presses arrive (F2 toggled the locator HUD and a click skipped the opening movie);
  - the title menu ignored the clicks, because Godot uses the real cursor and window focus to route mouse events.

  The port is our own code, so input should enter inside the process, not through Windows.
- **One-off test parameters.** Diagnostic flags such as `--shot-page`, `--shot-settle`, `--transition-click-ms` and
  `--hold-message-skip` each script one situation. Scenarios over a general channel should replace them over time.

## 2. Decisions

1. **One step language, executed by the client.** The `native_drive` vocabulary (§6) is parsed and run by a shared
   Python core. Each backend implements only a small set of primitive operations (§4).
2. **One wire protocol, two servers.** JSON lines over loopback TCP with a token:
   - **native AGE:** a Python daemon holding the Frida session (today's daemon, moved onto this protocol);
   - **the port:** a control server inside the Godot process, enabled by `--control`.
3. **Input enters the port as Godot input events.** `Input.ParseInputEvent` produces the same events a real mouse
   does, so menus, hotspots and skip logic run unchanged. Only Godot's own OS-to-event layer is bypassed.
4. **Waits use VM state, not pixels.** The port publishes an execution snapshot. Tests wait for a script position or
   a blocking kind instead of sleeping or comparing screenshots.
5. **One location convention.** Every reported offset is the frame's resume pc: the next instruction to run. Native
   AGE already reports this (`engine-re.md`, wait convention). For example, SC0000 page 3 reads `SC0000@0x8a9` on both
   games, so `expect` lines are shared.
6. **Sandboxes are interchangeable.** Both backends keep a sandbox profile root with the native layout
   (`SYS4REG.INI`, `SAVE\`). A save written by one game seeds the other.
7. **MCP is a thin client, built last in this round** (§8). Nothing depends on it.

## 3. Architecture

```text
scenario.steps / CLI args / MCP tool calls
        │
   drive_core.py  — parse + validate steps, resolve points, run them as primitives
        │  JSON lines over 127.0.0.1:<port>, token
   ┌────┴─────────────────────────┐
drive_native.py (daemon)      Godot port --control
   Frida agent in AGE.EXE        ControlServer node + VM execution snapshot
```

The CLI `tools/drive.py` starts and stops sessions and sends steps. Sessions belong to the daemon or to the port
process, not to the CLI or the MCP server. A restarted client therefore reconnects to the same running game.

## 4. Wire protocol

**Transport.**
- UTF-8 JSON, one object per line, over TCP on `127.0.0.1`. The launcher picks no fixed port: the server binds
  port 0 and writes the chosen port to its session file.
- The launcher generates a random 128-bit token and passes it through the environment variable
  `OME_DRIVE_TOKEN`. The server rejects any connection whose first request is not `hello` with that token.
- One client at a time. A second connection waits until the first one closes.
- **Session file:** `build/games/<profile>/drive/<target>/session.json`, holding `pid`, `port`, `target`,
  `profile`, `started` and `log`.

**Messages.**
- Request: `{"id": n, "op": "...", ...}`.
- Response: `{"id": n, "ok": true, "result": {...}}` or `{"id": n, "ok": false, "error": "..."}`.
- `hello` returns `target`, `profile`, the protocol `version` and `capabilities`, a list of optional ops and
  condition kinds. The client checks a scenario against them before running its first step.

**Primitive operations.**

| op | arguments | result | both? |
|---|---|---|---|
| `hello` | `token` | target, profile, version, capabilities | yes |
| `state` | — | `frames` `[{script, offset}]` outermost first, `top`, `waiting` (§5.3; null on native) | yes |
| `pointer` | `x y` (native 800×600 coordinates) | — | yes |
| `click` | `x y button hold_ms` | moves, presses, holds, releases; `cursor_moved_to` when the game warped the cursor | yes |
| `key` | `vk hold_ms` | — (ESC and F1 refused on native) | yes |
| `wheel` | `notches` | — | yes |
| `wait` | `until` (condition), `timeout_ms`, optional `poke {x y every_ms}` | the matching `state`; error on timeout | yes |
| `settle` | `timeout_ms quiet_ms threshold` | `settled`, elapsed time | yes (§5.4) |
| `shot` | `path` | writes a PNG, returns `path width height` | yes |
| `close` | `timeout_ms` | the game's own quit | yes |
| `dialogs` / `answer` | `button` | open message boxes / press one | native |
| `speed` | `factor` (0.05–8) | sets the frame clock speed | port |

**Conditions** are objects whose keys all have to match:
- `at: "S@OFF"`: any frame is at that location;
- `top: "S@OFF"`: the innermost frame is;
- `script: "S"`: any frame runs script S;
- `waiting: KIND`: port only, §5.3.

The server evaluates conditions, so a `wait` costs one request, not a polling loop over the wire.

## 5. The port backend

### 5.1 Flags

- `--control` enables the server. Without it nothing listens and nothing changes.
- `--control-file PATH` is where the port writes its session file. `drive_port.py` passes its own path.
- `--data-root DIR` replaces `user://games/<namespace>` as the profile root for saves, `SYS4REG.INI` and
  diagnostics (`ResolvedGamePersistence`). Godot's own user folder (shader cache, logs) stays where it is. This also
  removes the `APPDATA` override the first experiment needed.

### 5.2 ControlServer

- **Threads.** A Godot node in `godot/` accepts connections on a background thread. It queues each request to the
  main thread, which drains the queue in `_Process` and sends the replies. A `wait` stays pending and is re-checked
  whenever the execution snapshot changes, and at least once per frame. The main thread never blocks.
- **Input.** Requests become `InputEventMouseMotion`, `InputEventMouseButton`, `InputEventKey` and wheel events with
  `Device` set to a reserved control id. Positions are native coordinates times the viewport's scale, the inverse of
  `ToNativeScreen`. Key events use the inverse of `Win32VirtualKeyTranslator`.
- **Exclusive input (the default).** While a client holds the session, `_Input` drops events that do not carry the
  control device id. Someone moving the real mouse over the window therefore cannot disturb a run. Closing the
  window still works.
- **Screenshots.** `shot` reads the viewport texture on the main thread and writes it as a PNG at the logical canvas
  size. It works when the window is covered, but not when it is minimized.
- **Protocol model.** Request and response records, JSON parsing and condition evaluation live in
  `engine/Age.Engine.Frontend/Control/` and are unit-tested in `Age.Engine.Tests`. The Godot node only moves bytes
  and calls into Main.

### 5.3 Execution snapshot

`Age.Engine` gains `VirtualMachine.ExecutionSnapshot`:
- an immutable record holding `sequence`, `frames` (script name and resume offset per frame, outermost first) and
  `waiting`;
- published by the VM thread whenever it enters or leaves a blocking host call, and readable from any thread;
- tested directly in `Age.Engine.Tests`.

`waiting` is one of:
- `page`: an ADV text wait;
- `input`: an interactive hotspot or input-callback wait;
- `movie`: a modal movie;
- `transition`: a blocking effect;
- `sleep`: a timed hold;
- `null`: running.

Script-polled menus such as TITLE poll in their own loop and never block for input. Scenarios wait for them by script
or location, as they do natively.

The trace seam (`GodotTraceSink`) already supplies the call stack and the latest step, but not per-frame pcs. The
snapshot supplies those, and the locator keeps its own job.

### 5.4 Settle

- **On the port**, "settled" means: `waiting` is `page` or `input`, and no transition or modal movie has been active
  for `quiet_ms`. Looping animations, such as the title's, do not prevent it, unlike the native pixel rule.
- **On native AGE**, settle keeps the screen-difference rule.

### 5.5 Launch

`drive_port.py`:
- launches the Godot editor binary (`AGE_GODOT_CONSOLE`) with `--path godot -- --game-root … --data-root <sandbox>
  --control --control-file …`;
- writes `sound.Volume0=0` into the sandbox's `SYS4REG.INI` unless `--sound` is given;
- waits for the session file, then connects.

Both backends start the game window without activating it and move it to the bottom of the z-order
(`SetWindowPos(HWND_BOTTOM, SWP_NOACTIVATE)`), so a run does not cover the user's work. `--foreground` skips this.

## 6. The step language

The vocabulary stays as `native_drive` has it:
- `move`, `click`, `rclick`, `key`, `wheel`, `wait`, `settle`, `advance`, `shot`, `state`, `expect`, `wait-state`,
  `point`, `dialogs`, `answer` and `close`, with the same options;
- `wait-state … click=POINT every=S` becomes a `wait` with `poke`.

Additions:
- `waiting=KIND` in `expect` and `wait-state` (port capability);
- `speed F` (port capability).

A step whose op or condition kind is missing from the target's capabilities fails validation before the scenario
starts, with the target named in the error.

Named points and `advance` live in the shared profile. Native coordinates are the same on both games, so one
`[points]` table serves both.

## 7. Files

| Path | Role |
|---|---|
| `tools/drive.py` | CLI: `drive.py [--profile ID] native\|port start\|do\|run\|status\|stop\|steps`; `native` also has `backup\|restore` |
| `tools/drive_core.py` | protocol client, step parsing/validation/execution, profiles, sandbox and seed helpers (pure, tested) |
| `tools/drive_native.py` | the native daemon: today's `native_drive.py` serving the protocol |
| `tools/drive_port.py` | the port launcher |
| `tools/drive_mcp.py` | the MCP server (§8) |
| `tools/drive/` | `<profile>.toml` (`[points]`, `[advance]`, `[sandbox.*]`, `[native.engine]`, `[[native.dialog]]`), `native-agent.js`, `scenarios/*.steps` |
| `tools/test_drive_core.py` | step executor against a fake protocol server; replaces `test_native_drive.py` in core CI |
| `engine/Age.Engine.Frontend/Control/` | protocol model and condition evaluation |
| `godot/ControlServer.cs` | transport and main-thread dispatch |

Notes on the files:
- `tools/native_drive.py` and `tools/native-drive/` are renamed into these. Both are two days old and unpushed, so
  no compatibility alias is kept.
- **Output:** `build/games/<profile>/drive/<native|port>/` holds `session.json`, the log, `events.jsonl`, `shots/`,
  `sandbox/<name>/` and (native) `backups/`.
- **Seeding:** `--seed` accepts `live`, a folder, or `native:NAME` / `port:NAME` for another sandbox's profile root.

## 8. MCP

**Decision: yes, as the last milestone of this round.** Most of a live session's tool calls go to piloting a game.
Through MCP:
- a screenshot is one call that returns the image, instead of a shot followed by a file read;
- step lists need no shell quoting;
- Claude Code and Codex both speak MCP.

The server stays a thin client: no logic the CLI does not have, and no scenario or test requires it.

**Design.**
- **Server.** `tools/drive_mcp.py` uses the Python `mcp` SDK over stdio. `mcp` becomes an optional dependency, like
  Frida.
- **Five tools**, each taking `target`:
  - `drive_start(sandbox, seed, sound, foreground)`;
  - `drive_steps(steps, screenshot=true)`: per-step results plus the final screenshot as an image;
  - `drive_screenshot()`;
  - `drive_state()`;
  - `drive_stop()`.
- **Registration.** `docs/tools-reference.md` documents
  `claude mcp add drive --scope local -- py -3.11 -X utf8 <repo>/tools/drive_mcp.py` and the Codex equivalent. A
  newly added server loads at the next agent session.

## 9. Acceptance

1. **Core tests (no game, Godot, Frida or Pillow):**
   - C# protocol, condition and snapshot tests;
   - snapshot resume offsets at ADV waits and in nested frames;
   - `test_drive_core.py`.
2. **The existing scenario on both games.** `tools/drive/scenarios/himegari-save-load.steps` passes unchanged on
   `native` and on `port`, with the same expected offsets.
3. **Saves across games:**
   - a port-written slot loads in native AGE at `SC0000@0x8a9` and reads on to `0x913`;
   - a native-written slot does the same in the port.

   This is the open follow-up "native AGE accepting port-authored saves". A failure is a finding, recorded in the
   status memory and the format docs.
4. **No disturbance.** A port run completes with its window covered while the real mouse moves over it.

## 10. Non-goals for this round

- **OS-level input for the port**, and recording or replaying human sessions.
- **Automatic differential runs** (both games plus a diff). The design allows them later; see
  `2026-07-09-differential-oracle-design.md`.
- **Game scenarios in CI.** They need the install and a GPU, so they stay local workspace gates.
- **A virtual clock or deterministic timing.**
- **Headless port runs** (no screenshots).
- **Kamidori points.** Kamidori needs only a profile file.
- **Removing the existing Godot diagnostic flags.** They retire as scenarios replace them.

## 11. Open questions

- **Native `waiting`.** Native AGE may expose an "awaiting input" state in `ctx`. If one is found, native gains
  `waiting`; until then it reports null and lacks the capability.
- **Frame stacks below the top frame.** The port's managed frames (for example a SYSTEM4 frame parked at synthetic
  `0xae`) may not match native AGE frame by frame. The contract is the top frame plus script membership.
  Acceptance 2 measures how close the full stacks are.

## 12. Milestones

1. **M1 Port control channel:** execution snapshot, protocol model, ControlServer, `--control`/`--data-root`,
   exclusive input, `shot`, `speed`, with tests.
2. **M2 Shared driver:** `drive_core`, the native daemon on the protocol, `drive.py`, the port launcher, the
   renames, then acceptance 2.
3. **M3 Saves across games (acceptance 3).**
4. **M4 MCP adapter.**

The 0x1d5 voice fix stays a separate task, independent of this round.

## 13. As built: M1 (2026-11-03)

M1 is implemented (`engine/Age.Engine.Frontend/Control/`, `engine/Age.Engine/Vm/VirtualMachine.FrameLocations.cs`,
`godot/Main.Drive.cs`). Where it differs from the sections above, this section wins:

- **Location convention (§2.5).** Only the innermost frame reports its resume offset. Native AGE keeps a caller
  frame's pc **on** its `call-script` instruction (`SYSTEM4@0x477 > SC0000@0x8a9`; with MENU open,
  `SC0000@0x3a2`). The port reports frames the same way (`DriveFrame.FromVm`), so full stacks match, not just the top
  frame (§11, second question).
- **Hotspot callbacks.** A callback such as SC0000's right-click → MENU handler runs inside the page wait. The VM
  now returns the frame's pc to the waiting instruction afterwards, so state, saves and `expect` see the page again
  (`SC0000@0x8a9`) rather than the callback's last `ret`.
- **Token and session file (§4).** `OME_DRIVE_TOKEN` is optional: without it the port generates a token. The session
  file then carries `token` along with the `hello` fields, `pid`, `port` and `started`. It is written atomically and
  deleted on exit.
- **Waiting kinds (§5.3).** They come from the host's diagnostic snapshot, not a new VM snapshot record:
  - `input`: an ADV page or hotspot wait, replacing the proposed `page`;
  - `menu`: a script-polled menu (TITLE, GAMESTART, MENU) sleeping in a raw input-callback frame;
  - `movie`, `transition`, `sleep`, or null.
  `busy` is true while text reveals or a screen transition runs.
- **Settle (§5.4).** `waiting` is `input` or `menu` and not busy, continuously for `quiet_ms` (default 700 ms; the
  timeout defaults to 10 s). The title settles, unlike under the native pixel rule.
- **Exclusive input (§5.2)** lasts for the whole `--control` run, not only while a client is connected.
- **`shot` (§4)** requires an absolute path; relative paths would resolve against the port's working directory,
  `godot/`.
- **Click timing found on the way.** Native AGE triggers clicks on button **release** under SYS4INI `CLICKONUP`
  (Himegari = 1), and the port triggered them on press. That made every MENU open after the first close at once, and
  made a movie-skip press activate the title's ゲーム開始 on release. The port now follows `CLICKONUP`
  (`engine-re.md`, "Driving native AGE"). Scenario pokes at `adv.next` are therefore safe on both targets.
- **Verified live on the port:** the save/load scenario's whole path (boot with pokes, new game, page 1/3/5
  offsets equal to native, save to slot 0, back to the page, read on, load, resume at `SC0000@0x8a9`). One
  difference remains open for M2: after the load, the port's caller frame reads `SYSTEM4@0x235`.

## 14. As built: M2 (2026-11-03)

M2 is implemented (`tools/drive.py`, `drive_core.py`, `drive_native.py`, `drive_port.py`, `tools/drive/`). Where it
differs from the sections above, this section wins. Usage is in `docs/tools-reference.md` ("Game driver").

- **The client runs more than §4 says.** `wait N` sleeps in the client, and `expect` is a `state` request matched in
  the client (`drive_core.condition_matches`). Only `wait-state` becomes the server-side `wait`, with `poke` for its
  clicks. Conditions combine: every key given has to match.
- **Capabilities (§4).** Native `hello` lists `dialogs`, `answer` and `detach`; the port lists `speed` and `waiting`.
  `drive.py` checks a scenario against them before its first step, so the committed scenario may use neither target's
  extras.
- **Events.** Native replies may carry an `events` list (dialogs answered, the window appearing); the client prints
  them with the step results. The daemon also logs them to `events.jsonl`.
- **Session file (§4).** Native `session.json` names the daemon as `pid` and AGE as `game_pid`, plus `port`, `token`,
  `mode` (spawn or attach) and the save folders. The port writes its own (§13).
- **`wheel`** without `x`/`y` turns at the driven cursor on both targets.
- **Native input polling.** Windowed AGE turns polling off when its window loses activation (`engine-re.md`,
  "Driving native AGE"), and a driven AGE runs inactive at the bottom of the z-order. The agent ignores that call and
  reopens the gate once the window exists (profile `[native.keep_polling]`). The 2026-11-01 native runs passed only
  because AGE happened to be active.
- **Full stacks match after a load (§11, second question; §13's open item).** A restored ancestor parked on `0xae`
  now reports its saved call site, so after the load both targets read `SYSTEM4@0x477 > SC0000@0x8a9`.
- **Launch options (§5.5).** The port launcher builds `godot/OME.csproj` first (`--no-build` skips it) and passes
  extra flags with `--port-arg`. It writes the profile's `[sandbox.settings]` (windowed) as well as the quiet volume.
  Native's in-process daemon flag is `--inline`.
- **Acceptance 2 passed.** `tools/drive/scenarios/himegari-save-load.steps` runs unchanged on the port (37 s) and on
  native AGE (32 s), at the same offsets: pages at `0x83f`, `0x8a9`, `0x913`, and the load resumes at `0x8a9`. Native
  stop reports `live-saves-unchanged`; both targets exit with code 0.

## 15. As built: M3 (2026-11-03)

- **Acceptance 3 passed in both directions** with two committed scenarios rather than one:
  `tools/drive/scenarios/himegari-save-page3.steps` (new game, save slot 0 on page 3, `close`) and
  `himegari-load-page3.steps` (title Data Load, slot 0, はい, expect `SC0000@0x8a9`, two clicks, expect `0x913`).
  The second target's sandbox is seeded from the first one's with `--seed port:fresh` or `--seed native:fresh`. Each
  file's header gives the full run.
- **The failures were findings, as §9 expected.** Native AGE listed port slots as empty (mojibake game id), then
  crashed loading them (header `+0xf0` length left zero). The port refused to boot on a native `SAVE.DAT` (padding
  residue), and it would have rejected native slots on their compatibility id, which is the writer's engine version
  and which AGE never checks. All are fixed in the port's persistence layer. `docs/sys4-format-notes.md` and
  `docs/engine-re.md` ("Save identity and native read checks") record them, and `docs/phase-a-slice-plan.md` has the
  list.
- **No driver changes were needed.** The steps, seeding and `close` from M2 covered the round trip.

## 16. As built: M4 (2026-11-03)

`tools/drive_mcp.py` implements §8; usage and setup are in `docs/tools-reference.md` ("MCP server").

- **SDK 2.x.** The server uses `mcp` 2.3 (`mcp.server.mcpserver.MCPServer`; 2.x renamed `FastMCP`). The 2.x stdio
  transport moves the wire off file descriptor 1 and points it at stderr, so stray prints and the CLI's child processes
  cannot corrupt the protocol.
- **One code path.** `drive.py` gained `execute_steps`, `session_status` and `steps_help`; the CLI and the server both
  use them. `drive_start` and `drive_stop` run the CLI as a subprocess.
- **Beyond §8's signatures.** `drive_steps` also takes `file` (a `.steps` file, as `run` does). `drive_start` also
  takes `build` (`--no-build`). `drive_screenshot` takes an optional `name`; `drive_stop` takes `timeout`. The native
  `--live-saves` and `--attach` options are not offered.
- **Failures.** A failing step comes back as a `FAILED at step N` result with its screenshot. A call that cannot run
  (no session, an unknown step, a missing capability) is a tool error.
- **Instructions.** The server's instructions carry `drive.py steps` (syntax, conditions, named points), so an agent
  does not need to look up the step language.
- **Environment.** MCP clients may start servers with a reduced environment, and the SDK's stdio client dropped
  `AGE_GODOT_CONSOLE`. Registration passes it explicitly. Codex's default 60 s tool timeout is too short for a port
  start with its build.
- **Validation.** `tools/test_drive_mcp.py` calls the tools through the SDK against the fake protocol server. It runs
  by hand, not in `validate.py`: no layer requires the optional `mcp`, and validation never turns a missing
  prerequisite into a skip. A live run over real stdio passed on both targets: start, boot to the
  title, new game to page 3 with `expect top=SC0000@0x8a9`, state (`SYSTEM4@0x477 > SC0000@0x8a9` on both), a named
  screenshot, a reported failing `expect`, and stop. - **Acceptance 4 passed (2026-11-03).** Through the MCP server, `himegari-save-load.steps` ran unchanged on the port
  (34 steps, 37 s, the same offsets) while a script moved the real cursor over the port window 3,654 times in 75 s.
  At every one of those points another window covered the port (`WindowFromPoint`). No real clicks were sent: they
  would have landed on the covering window.
