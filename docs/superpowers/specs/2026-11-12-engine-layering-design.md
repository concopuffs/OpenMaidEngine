# Engine layering — design

**Date:** 2026-11-12
**Status:** accepted 2026-11-12. Drafted on 2026-11-11 from the user's decisions of that day, revised after one
design review, and accepted with the decisions of 2026-11-12 (§2). Milestones 0 (engine builds), 1 (handler table)
and 3 (projects) are implemented on `feature/engine-layering`; §3.3, §4, §7.5, §7.6 and §13 record what they
settled. §15 records the history.
**Home in the canonical map:** this file is the design of the engine's layering (handlers, natives, devices), its
projects, and how engine builds are declared. Native findings stay in `docs/engine-re.md`, opcode facts in
`vm-map/opcodes.toml`, and the open work it covers in `docs/backlog.md` (§14), which points here.

- **Measured at:** `6fc8a18` on `develop`. Counts are grep counts.
- **Replaces:** `vm-host-main-restructure-design-2026-11-10.md` (an earlier draft at the workspace root, not
  adopted) for `VirtualMachine` and `GodotAdvHost`. That draft let opcode code call the host directly; this design
  puts a layer between them. Its section on `Main` is not carried over (§10).
- **Behaviour.** Every milestone is meant to be behaviour-neutral, with one possible exception: under a modal
  dialog, timed behaviour stops, as native's does (§5.3). A native trace decides it before milestone 6.

**Not verified:** §13 lists what is still to be measured, and the milestone that measures each.

Paths are relative to `age-reimpl/`.

## 1. Purpose

The port runs 2 of more than 20 AGE games. Each new game brings its own build of the engine. Three things in the
code today will not hold up to that.

**1. Revision behaviour is chosen by name, in several places.** `EngineRevisions.IsSys4433` is read at five sites:
`Persistence/Sys4RegIniStore.cs:97`, `Text/AgeGlyphMaskCompositor.cs:31`, `Text/RubyLayoutRules.cs:46` and `:52`,
and `Vm/VirtualMachine.cs:213`. Its comment says "every other profile runs SYS4422's text code". A third engine
cannot be expressed as a yes/no, and nothing tells us which side it falls on.

**2. Opcode code and engine state are one class.** `VirtualMachine` is 29 partial files with about 120 instance
fields, and any opcode can write any of them. It calls 95 different host members.

**3. Much of the engine's behaviour lives in the front end.** `IHost` mixes two kinds of call:

- device calls, such as `CreateTexture` and `DrawTexture`;
- engine behaviour, such as `RenderTextHistory`, `BindAdvWaitIndicator`, `FadeSurfaceWithBlack` and
  `ArmVoiceBgmDuckRelease`.

The engine behaviour is implemented in three places outside `Ome.Engine`:

- **`GodotAdvHost`** (6 files, 3,471 lines). 13 of those lines name a Godot type: 10 print a message, 2 defer a call
  and 1 lists system fonts. Everything else it needs from Godot goes through 30 members of `Main`.
- **`godot/Main.Compositor.cs`** (887 lines): the software compositor, the transitions and the blurs. The software
  path writes a `byte[]` and touches Godot once, to upload the finished frame (`:444`). `Main.Audio.cs` runs BGM
  fades and voice ducking on Godot's thread.
- **`Ome.Engine.Frontend`**: the movie surface registry and movie timing (`MovieSurfaceRegistry.cs`,
  `MovieRuntime.cs`, `MovieAudioTimeline.cs`).

A second front end would have to rewrite the Godot part, and none of that part can be tested without Godot.

One operation shows the split. For a BGM fade (op `0xc2`):

- the VM keeps the fade's target (`_bgmFadeTarget`) and the current track;
- `GodotAdvHost.Audio.cs` turns the duration into native's step count and keeps the deadline that the VM polls
  through `IsBgmFadeActive`;
- `Main.Audio.cs` runs the fade as a Godot tween and releases the source at a zero target.

The VM's deadline starts at once and the tween starts on the next Godot frame, so `Main.Audio.cs:46-49` carries a
workaround for a fade that the VM sees as ended before the tween reaches its endpoint.

**What this design sets up:** three layers with one-way dependencies, checked by the build, and one place where each
engine build is declared.

## 2. Decisions

### 2.1 The user, 2026-11-11

1. **Three layers.** Handlers hold the logic of each opcode. Natives are the engine's own services, which opcodes
   talk to ("play sound", "draw text", "display graphic"). Devices hide the operating system and the front end from
   the natives. The engine keeps its own bookkeeping; drawing and playing are handed to Godot, SDL or another front
   end.
2. **Three projects**, so the build checks the layering.
3. **A build's choices are written in C#**, in one file (§7.4). The registry (`vm-map/opcodes.toml`) keeps the facts
   about native AGE: numbering, operands, names and evidence. It does not name C# code.
4. **No explicit revision checks** in engine code.
5. **Not doing:** opcode sets as separately downloaded DLLs. Every build ships in the one binary.

### 2.2 The user, 2026-11-12, on acceptance

6. **Graphics: a frame description and a reference renderer** (§6.3). The graphics native lowers retained objects
   into a frame description; the software compositor becomes the engine's reference renderer; play runs on a GPU
   renderer that must match it. The alternative, keeping `RenderObject` as what the front end draws, was rejected:
   every front end would have to know AGE's object model.
7. **Lift the host first** (milestone 4). `GodotAdvHost` moves into `Ome.Engine` unchanged and is divided into
   natives there, so moving code and changing threads land separately.
8. **One engine build per profile** (§7.5). Kamidori is always 4.33G, whichever executable the player has.
9. **No file device until a platform needs one** (§6.4).
10. **Modal waits run no tick** (§5.3), as native's main loop stops under a modal dialog. A native trace confirms it
    before milestone 6; if native keeps its fade running there, modal waits run the tick too.
11. **The engine tick moves to the engine thread** in milestone 11 (§5.3), measured on its own.
12. **Engine facts leave the profile manifests in milestone 0** (§7.5).
13. **The native interfaces are renamed at the end** (milestone 12). "Host" stops being the right word once natives
    implement `IAudioHost` and the others; the rename is mechanical and touches most tests, so it waits until the
    interfaces have settled.
14. **The 22 operand-access rows of §7.2 are checked in milestone 0.**

## 3. The layers

### 3.1 What each layer holds

| Layer | Holds | May not | Test |
|---|---|---|---|
| **Handler** | Reading operands, calling natives, writing results, steering control flow | Keep state; name a device; name a revision | Could a Lua mod or the options panel want the same action? Then it is a native. |
| **Native** | All state a script can read back or a save records; all timing (reveals, fades, waits) | Call a handler; name a front end; name a revision | Does it run with no front end, on headless devices? |
| **Device** | What an operating system or front end can do: showing a frame, sound output, input, window, glyph rasterizing, decoding | Carry AGE vocabulary or AGE timing | Would a second front end have to reimplement AGE behaviour to satisfy this method? Then it is a native in the wrong layer. |

Calls go one way: handlers → natives → devices.

- A device reports back only through data it returns or events it queues (§6.2).
- A native reaches the interpreter only through requests that the interpreter collects (§5.4).

A device's state must be rebuildable from native state at any time. There is no exception: the pixels that scripts
draw into surfaces are engine data already, and they become the graphics native's (§6.3).

### 3.2 Native AGE is built this way

- **A native handler is thin.** `op_0x1d1_render_text_history@0x41f950` forwards its five operands and the text
  manager to the worker `text_history_render_records@0x4526c0` (`vm-map/opcodes.toml`, op `0x1d1`).
- **The subsystems are regions of `EngineCtx`:** the text manager and the retained graphics manager
  (`vm-map/engine-ctx.toml`), and the 13-slot sound manager that op `0x1be` queries.
- **Timed behaviour runs in the main loop**, switched on by run-state bits: `0x200` for a BGM fade, `0x400` for a
  foreground transition, `0x2000` for a scene movie, `0x80000000` for a polling text wait (`docs/engine-re.md`).
- **The main loop dispatches one opcode per iteration and runs the services in between.**
  `engine_main_tick_with_exception_policy@0x411840` dispatches through `adv_interpreter_tick@0x410fb0`, which
  executes exactly one opcode per call and runs the skip, click and auto-advance logic each time. The loop renders
  at the top of the next iteration when the frame is dirty, and it advances a BGM fade from its own timer while
  opcodes keep dispatching (`docs/engine-re.md`, "Short sleeps do not wait for the display" and "Fast-forward
  pace"; the comment at `godot/GodotAdvHost.Audio.cs:215-217`).

So a port native should line up with a native subsystem, and the engine tick of §5.3 is native's own structure.

### 3.3 Projects

| Project | Layer | Contents | References |
|---|---|---|---|
| `Ome.Engine.Devices` (new) | Device interfaces | The interfaces of §6 and the plain data they carry (images, glyph masks, audio data, input events, the frame description) | nothing |
| `Ome.Engine` (exists) | Natives | Today's `Model/`, `Sys4/`, `Text/`, `Persistence/`, `Localization/`, `Profiles/`, plus the natives of §5 and the reference renderer (§6.3) | `Ome.Engine.Devices` |
| `Ome.Engine.Vm` (new) | Handlers | Today's `Ome.Engine/Vm/`: the interpreter, the handler table, the handlers, and the build definitions of §7 | `Ome.Engine` only |
| `Ome.Engine.Text.Windows` (exists) | Device implementation | The GDI glyph rasterizer and font list | as today |
| `Ome.Engine.Frontend` (exists) | Shared front-end code | Device implementations that are not Godot's (the ffmpeg movie decoder, the headless devices), the shell's own model, the driver, the logs | all three |
| `godot/`, `Ome.Cli` | Device implementations and composition | As today, plus device implementations | all three |

**How the build enforces it:**

- **`Ome.Engine` cannot call a handler:** a reference back to `Ome.Engine.Vm` would be circular.
- **`Ome.Engine.Vm` cannot name a device type.** SDK project references are transitive, and no `.csproj` here turns
  that off, so as drawn `Ome.Engine.Vm` would see `Ome.Engine.Devices` through `Ome.Engine`.
  `Ome.Engine.Vm.csproj` therefore sets `DisableTransitiveProjectReferences`. Milestone 3 proves the setting with a
  compile that is meant to fail on a device type.
- **The rule that follows: a native's interface names no device type.** Otherwise correct handler code would fail
  to compile (CS0012). `IGraphicsHost.cs:33` and `:36` break the rule today: `CaptureSurfacePixels` and
  `ReplaceSurfacePixels` carry `RgbaImage`. Their only callers in `Vm/` are the thumbnail save and load at
  `VirtualMachine.Persistence.cs:160` and `:194`. Milestone 3 puts those two behind one native method each
  ("write this surface as a thumbnail", "load a thumbnail into this surface"), so the image never reaches a
  handler.
- **Backstop:** a test fails if the `Ome.Engine.Vm` assembly references `Ome.Engine.Devices`.
- **Namespaces stay.** `Ome.Engine.Vm` keeps its name, so the `using` lines of the 88 test files that name it do not
  change. Types that move to `Ome.Engine.Devices` keep their namespaces too.

**Moving `Vm/` out looks cheap:**

- One code reference points into `Vm/` from the rest of `Ome.Engine`: `Diagnostics/TraceEvent.cs:22` and `:34` use
  `Vm.UnsupportedOpcodeDiagnostic`. That record moves out of `Vm/` in milestone 3, or the reference would be
  circular.
- The other mentions are comments (`Hosting/ILifecycleHost.cs`, `Hosting/IMovieHost.cs`,
  `Sys4/Sys4StartupSettings.cs:100`).
- `Ome.Engine` has 3 internal types outside `Vm/`.
- `Ome.Script` and `Ome.Mods` do not use the VM namespace.
- `IHost`, its seven interfaces, `GfxState` and `RenderObject` are in `Hosting/` and `Model/`, not in `Vm/`.

**What moves into `Ome.Engine.Devices`:**

| Type | Now in | What it needs first |
|---|---|---|
| `IGlyphMaskRasterizer`, `GlyphRasterRequest`, `GlyphMask` | `Text/GlyphMaskContracts.cs` | Nothing: the file uses no other engine type, and `Vm/` does not use these |
| `IFontFamilySource`, `EnumeratedFontFamily` | `Text/FontFamilyCatalog.cs` | Nothing; the `FontFamilyCatalog` class stays |
| `RgbaImage` | `Model/RgbaImage.cs` | A record of width, height and bytes. `Vm/` never names it, but two handlers hold one through `var` (see above) |
| `IMovieDecoder`, `IMovieDecoderFactory` | `Ome.Engine.Frontend/IMovieDecoder.cs` (internal, global namespace) | The factory takes an engine type (`MoviePayload`); it takes bytes instead |
| `IPaceClock` | `Vm/IPaceClock.cs` | Fast-forward pacing must leave the VM first: `VirtualMachine`'s public constructor takes an `IPaceClock` (`Vm/VirtualMachine.cs:202`) and `VirtualMachine.FastForwardPace.cs` uses it. Pacing moves with the skip and auto state, so `IPaceClock` moves in milestone 5, not milestone 3 |
| New: audio, input, window, dialogs, frame presenter and renderer | — | Written as their natives are built |

`FrameClock` stays in `Ome.Engine`: it is the engine's own clock (speed scaling, and a stepped mode for tests) over
a time source. `INativeDatStore` and `IAssetStore` stay too (§6.4).

**As built (milestone 3).**

- **The projects.** `engine/Ome.Engine.Vm/` holds what `Ome.Engine/Vm/` held, in the same namespace, and its project
  sets `DisableTransitiveProjectReferences`. `engine/Ome.Engine.Devices/` holds the types of the table above except
  `IPaceClock`, in their old namespaces. The movie decoder's interfaces and their two audio records became public,
  and the factory takes a name and the file's bytes. The solution, the Godot project and the release packages
  include both assemblies.
- **The diagnostic** is `Ome.Engine.Diagnostics.UnsupportedOpcodeDiagnostic` (`Diagnostics/`).
- **One internal type** of `Ome.Engine` was used by the VM: `NativeGfxPersistenceCodec`, the save's graphics
  records. It is public now.
- **The thumbnails.** `CaptureSurfacePixels` and `ReplaceSurfacePixels` left `IGraphicsHost` for
  `Hosting/ISurfacePixels.cs`, which `IHost` does not include. `Persistence/NumberedSaveThumbnails` (`Write`, `Load`)
  is the native that ops `0x1ae` and `0x1af` call; it returns the script's result. The Godot host and the tests'
  recording host implement `ISurfacePixels`. A host without it has no pixels, as the interface's defaults gave
  before.
- **Proved once.** Before the thumbnail change, the handlers failed with CS0012 at the two sites named above. A file
  in `Ome.Engine.Vm` that names `RgbaImage` or `IGlyphMaskRasterizer` fails with CS0234. A reference from
  `Ome.Engine` to `Ome.Engine.Vm` fails restore with MSB4006 (a circular dependency).
- **The backstop** is `ProjectBoundaryTests`:
  - each layer is its own assembly, and the device types are in `Ome.Engine.Devices`;
  - the handler assembly references `Ome.Engine` and not `Ome.Engine.Devices`;
  - `Ome.Engine` does not reference the handler assembly, and `Ome.Engine.Devices` references no `Ome.*` assembly;
  - each of the three project files references only the layer below, and `Ome.Engine.Vm`'s sets
    `DisableTransitiveProjectReferences`;
  - `IHost` and its seven interfaces name no device type.

### 3.4 What today's types become

- **`IHost` and its seven interfaces become the natives' interfaces.** They are already written at the right level
  ("play this voice", "show this text run"). What is missing is an implementation outside Godot, and a device
  interface below it.
- **`GodotAdvHost` is lifted into `Ome.Engine` as it stands** (milestone 4), and is then divided into natives. What
  stays in `godot/` is device implementations.
- **`VirtualMachine` keeps its public face.** Tests construct it 349 times, with fakes of `IHost`. Those fakes
  become fake natives, so a handler test still reads as it does now.
- **`GfxState` is already a native's state.** `GpuRetainedRenderer`'s comment calls it "the backend-neutral source
  of truth".

## 4. Handlers

- **One table.** At construction every opcode of the build's ABI table is bound to one handler, in an array indexed
  by opcode. A step is an index and a call. Today each instruction runs two string switches, kept in step by hand
  with a third list (`OpcodeRuntimeCoverage.ImplementedOpcodes`, a set of opcode numbers).
- **Coverage is derived.** "Implemented" means "has a handler". The hand list and the regex in
  `tools/opcodes_build.py` that reads it both go.
- **Handlers are grouped by family** in classes whose constructor lists the natives they use. A handler group has
  no fields of its own beyond those references.
- **A typed step result** replaces the `int.MinValue + n` sentinels (`HALT`, `FRAME_RETURN`, `HOTSPOT_RETURN`,
  `ROOT_RELOAD`).
- **The interpreter stays here:** the frame stack, `call-script`, coroutines and the instruction loop are not
  natives. They take part in saves through an interface the save native defines, and they take script requests
  from natives (§5.4).

**As built (milestone 1).**

- **The table** is `Ome.Engine.Vm/VirtualMachine.Handlers.cs`. It maps each opcode's name to its handler. The name
  is the opcode's `CanonicalLabel`: its registry semantic name, or its label when it has none. A VM binds its table's
  opcodes into an array indexed by opcode when it is constructed. The 120 `u004xxxxx` alias labels are gone.
- **Names are unique.** The registry lint and `OpcodeHandlerTableTests` both reject two opcodes with one name.
  Op `0x19a` shared `get-message-skip` with `0x1c7` and is now `get-message-skip-enabled`. The shared name had also
  kept both opcodes from an Open Maid Script name.
- **A variant can be bound by name.** A revision whose opcode differs can give it another name in its ABI layer;
  the opcode is then unimplemented until a handler binds that name. Whether builds also override bindings is decided
  with the first variant, op `0x1fd`'s float read (backlog "Float operands"). The ABI layers'
  `handler_implementation_id` field is gone: nothing set it.
- **Coverage is derived.** `VirtualMachine.Implements(table, opcode)` answers it, and `Ome.Cli catalog-scan` reports
  a corpus's opcodes without a handler. `opcodes_build.py` reads no C#, and the registry's generated outputs no
  longer report runtime coverage. `tools/scene_opcode_coverage.py`, which parsed the switch's case labels, is
  retired.
- **The test** (acceptance 3): every opcode of each game's ABI table has a handler; every bound name is an opcode's
  name in the catalog; no two opcodes share a name; an opcode without a handler halts with its diagnostic.
- **Handlers are `Op…` methods** of `VirtualMachine`, in the partials by family, until there are natives to give
  them (milestones 3 to 11).
- **`StepResult`** (`Ome.Engine.Vm/StepResult.cs`) is `Next`, `Jump(index)`, `Halt`, `FrameReturn`,
  `HotspotReturn` or `RootReload`. A wait's callback still pushes a marker on the frame's local call stack; `ret`
  turns it into `HotspotReturn`.

The BGM fade after the change (names illustrative):

```csharp
// Handler: operands in, one native call.
private StepResult FadeBgm(in OpCall c)
{
    audio.FadeBgm(targetBasisPoints: (int)memory.Read(c.Args[0]), durationMs: memory.Read(c.Args[1]));
    return StepResult.Next;
}

// Native: state and timing.
public void FadeBgm(int targetBasisPoints, long durationMs)
{
    if (skip.FastForwardActive) durationMs = 0;     // native applies the endpoint at once under fast-forward
    _fade = new BgmFade(targetBasisPoints, durationMs, startedAtMs: clock.NowMs);
    Service(clock.NowMs);                           // a zero-length fade ends here
}

public void Service(long nowMs)
{
    // Step the fade as native's tick does. At the end, commit the level; a zero target releases the source and
    // clears the retained track.
    // Device calls only: device.SetVolume(BgmChannel, level); device.Stop(BgmChannel);
}
```

The fade reads the skip state, which is why that state gets an owner before the audio native is built (§5.2).

## 5. Natives

### 5.1 Rules

1. **A native owns its state.** Fields are private; other code calls methods named for what native AGE does.
2. **One interface per subsystem.** There is no single engine interface.
3. **The constructor is the dependency list:** the rules for this build (§7), the devices it drives, and the other
   natives it needs.
4. **Engine thread only, once milestone 11 is done.** Until then the lifted host keeps its locks. Anything another
   thread needs is handed over as immutable data (§5.3).
5. **Grouped by lifetime.** Scene state is rebuilt on a root reload, not cleared field by field. Today
   `ResetSceneContextForRootReload` clears 38 fields by hand, and the host has a matching method.
6. **Each native reads and writes its own save records.** The save native decides the order.
7. **No revision names.** A native reads the rules it was given.
8. **No device types in its interface** (§3.3).
9. **A native never runs a script** (§5.4).

### 5.2 The subsystems

Names are suggestions. Each row is settled in the milestone that builds it, from the census (milestone 2).

| Native | Owns | Comes from | Devices |
|---|---|---|---|
| Script memory | The global banks, string encoding, address helpers | `VirtualMachine.cs` | none |
| Skip and auto | The skip, auto and fast-forward state, and fast-forward pacing | The VM's skip and fast-forward files, including `VirtualMachine.FastForwardPace.cs`; `IsMessageSkipActive` on the host | time (`IPaceClock`) |
| Audio | 13 channels, the BGM track and fade, voice ducking, delayed starts, mixer levels | `VirtualMachine.Audio.cs`, `GodotAdvHost.Audio.cs`, `Main.Audio.cs` | audio |
| Graphics | Retained objects, the surface table and its pixels, animation, transitions, the frame description | `GfxState` (already there), the VM's surface, object, animation and presentation files, `GodotAdvHost.Surfaces.cs`, the transitions in `GodotAdvHost.PresentationInput.cs`, `godot/Main.Compositor.cs`, `Sys4/RetainedSurfaceRasterizer.cs` | frame presenter, renderer |
| Text | Layouts, style, reveal, the wait indicator, history, `draw-string`, the localization presenter | `Ome.Engine/Text/`, `Localization/`, the VM's text files, `GodotAdvHost.AdvText.cs` | glyph rasterizer, font list |
| Movies | Binding a movie to a surface, start delay, the modal movie, mask transitions | `VirtualMachine.Movie.cs`, `GodotAdvHost.Movies.cs`, and `MovieSurfaceRegistry.cs`, `MovieRuntime.cs` and `MovieAudioTimeline.cs` in `Ome.Engine.Frontend` | movie decoder |
| Input and waits | The action latch, callbacks, text waits, sleeps, frame yield | The VM's input and timing files, the waits in `GodotAdvHost.PresentationInput.cs` | input, time |
| Saves and settings | Numbered saves, the shared profile, `SYS4REG.INI`, the read-text database | `Persistence/` (already there), `VirtualMachine.Persistence.cs` | none (§6.4) |
| Shell services | Screen mode, the error box, the name editor, the menu state a script can read or change | The VM's menu, screen-mode and diagnostics files; `VmMenuState` | window, dialogs |

**Skip and auto state moves first.** Other subsystems read it:

- `VirtualMachine.Audio.cs:139`: a BGM fade jumps to its endpoint when `_messageSkipServiceActive ||
  _host.IsMessageSkipActive`;
- `:84` and `:92`: starting a voice sets `_autoVoicePending`;
- `_messageSkipServiceActive` is used in 7 of the VM's files.

So that state gets one owner in milestone 5, before any native that reads it. Fast-forward pacing goes with it,
because it reads only the fast-forward state and the clock. The waits themselves move last (milestone 11).

### 5.3 Time

**Today.** The VM runs on a task thread (`godot/Main.cs:616`) and blocks inside host calls. Godot's main thread
advances fades, reveals and transitions on its own frame, and wakes the VM (`GodotAdvHost.PulseFrame`). The render
thread reads `GfxState` directly, behind `ScriptPresentationBarrier`. To make that safe `GodotAdvHost` holds nine
lock objects (107 `lock` statements), six `AutoResetEvent`s, a `SemaphoreSlim` and a `ManualResetEventSlim`.

The change has two parts. They land in different milestones, so each is measured on its own.

**Part 1: one tick function** (arrives with the first native, milestone 6). No thread changes.

- Each native with timed behaviour has a `Service(nowMs)` method. One function calls them, in native's order.
- It is called at the instruction boundary (`ServiceInstructionBoundary`, `Vm/VirtualMachine.SkipClickCancel.cs:49`,
  called from both instruction loops), and on each frame inside every wait that blocks the VM thread.

Today only the input poll services anything while it waits (`serviceInputCallback`,
`godot/GodotAdvHost.PresentationInput.cs:314`). Once timed behaviour is a native's, every other wait must run the
tick too. Otherwise a BGM fade or a voice-duck release stalls during a sleep or a transition, where today's Godot
tween keeps running. The waits, at `6fc8a18` (the lines move with milestone 4's lift; a row that names no file is
in `GodotAdvHost.PresentationInput.cs`):

| Wait | Where (`godot/`) | Runs the tick |
|---|---|---|
| The input poll (text waits, ops `0x72` and `0xfa`) | `GodotAdvHost.PresentationInput.cs`, `WaitAtAdvInputPoll` (`:270`) | yes |
| A sleep | `Sleep` (`:1139`) | yes |
| The timed-callback deadline | `WaitForTimedCallbackDeadline` (`:1175`) | yes |
| A foreground transition | `WaitForForegroundTransition` (`:624`) | yes |
| A whole-screen transition | `RunLegacyScreenTransition` (`:874`, `:886`) | yes |
| A synchronized presentation | `RequestSynchronizedPresentation` (`:190`) | yes |
| A blocking text reveal | `GodotAdvHost.AdvText.cs`, `ShowText` (`:435`) | yes |
| The modal movie (op `0x20f`) | `GodotAdvHost.Movies.cs`, `PlayModalMovieToSurface` (`:136`) | yes (§13) |
| The diagnostic box, the script-error box, the full-width text editor | `ShowDiagnosticMessage` (`:88`), `ShowScriptError` (`:105`), `EditFullwidthString` (`:129`) | no: modal |
| A front-end modal hold (the options panel, the 終了 dialog) | `FrameYield` (`:1112`); inside the input poll (`:303`) | no: modal |

**Modal waits run no tick** (§2, decision 10). Native's modal loops freeze its main loop (`docs/engine-re.md`,
"The dialog is modal, so native's main loop is frozen while it is open", and "Freeze"), and native advances a BGM
fade in that loop, so under a modal dialog a fade stops where it is. Today's tween keeps fading, so this is the one
behaviour change the design allows. That native's fade stops is inferred from those two facts. Before milestone 6
lands, a native trace of the fade tick under the 終了 dialog confirms it (§13). If native keeps fading, modal waits
run the tick too, and nothing changes.

**A test keeps the list whole.** Every wait in the host blocks through one helper that runs the tick each frame,
unless it is told the wait is modal. A test fails on any other blocking call (`WaitOne`, `Wait`) in the host.

**Part 2: the engine thread is native's main loop** (milestone 11).

- A wait is a loop on the engine thread: run the tick, publish a frame, then block until the device signals the next
  frame or an input event.
- The front end's thread does four things only: queue input events, signal each frame, apply the device commands
  the engine sent (§6.2), and draw the last frame the engine published.

**Why:** natives then have a single thread, so they need no locks, and a headless run executes the same loop as
Godot does. `Ome.Cli` already runs with a simulated clock (`SimulatedClockHost`). §3.2 shows this is how native
runs.

**Not verified:** that frames published this way keep today's timing. Milestone 11 measures it with
`--timeline-log` runs of the drive scenarios, against a tolerance set before measuring. If pacing suffers, the
fallback keeps the code and changes who drives the loop: the front end's frame callback gives the engine thread a
turn and waits for it to yield.

### 5.4 Requests to the interpreter

Some natives must make script work happen:

- an input or hotspot callback during a wait (`serviceInputCallback` today);
- the callback scripts: `CALLBACK_TEXT.BIN`, `CALLBACK_LOAD.BIN`, and `CALLBACK_WINDOW.BIN` after a screen-mode
  switch;
- a fast-forward click that moves the PC (`docs/engine-re.md`, `adv_interpreter_tick`);
- a root reload, as タイトルに戻る asks for.

The rule:

1. **A native never runs a script.** It records a request: run this callback, reload the root, move to the next
   page.
2. **The native defines the request interface** in `Ome.Engine`, and the interpreter implements it. Project
   references still point one way.
3. **The interpreter takes requests at two points:** at the next instruction boundary, and inside a wait each time
   the wait runs the tick (the waits of §5.3 that run it). The second point is needed because a handler blocked in a wait never reaches the next
   boundary. It is today's `serviceInputCallback`.
4. **A request that cannot run inside a wait** (a root reload) ends the wait and is taken at the boundary.

Where the script itself polls, no request is needed: op `0xcd` fires the op-`0xcc` callback, so its handler asks
the native whether the callback is due and the interpreter runs it.

This also gives "Two instruction loops" (backlog) its shape: one loop takes requests, whether it was entered from a
frame or from a wait.

## 6. Devices

### 6.1 The interfaces

Names are suggestions.

| Device | Does | Today |
|---|---|---|
| Renderer | Draw one frame description (§6.3) with the GPU. Every front end meant for play needs one | `GpuRetainedRenderer` |
| Frame presenter | Show one finished RGBA frame from the reference renderer | The upload at `godot/Main.Compositor.cs:444` |
| Audio | Load, play, stop and set the level of a channel; report when a channel ends | Mixed into `IAudioHost`; `Main.Audio.cs` |
| Glyph rasterizer, font list | Turn a glyph request into a coverage mask; list font families | Exist: `IGlyphMaskRasterizer`, `IFontFamilySource` |
| Movie decoder | Decode frames and audio from bytes | Exists, internal: `IMovieDecoder` in `Ome.Engine.Frontend` |
| Input | Queue pointer, button, key and wheel events; set and move the cursor | Mixed into `IInputHost` |
| Time | Report the time; sleep; signal the next frame | Exists in part: `IPaceClock` |
| Window | Window or fullscreen, size, close request | `IShellHost`, `ILifecycleHost.RequestScreenMode` |
| Dialogs | A message box with buttons, a text entry, a folder picker | Mixed into `IDiagnosticHost` and `IInputHost` |

### 6.2 The device contract

`GodotAdvHost` reaches Godot through 30 members of `Main` today. 22 are `CallDeferred` commands, which take effect
on the next Godot frame. The other 8 are direct calls across threads (`IsSoundChannelPlaying`,
`IsVoicePlaybackActive`, `QueueVoicePlayback`, `QueueMoviePlaybackRate`, `TryPlayMovie`,
`RequestScreenModeFromScript`, `ShowAgeDiagnostic`, `ShowAgeScriptError`). The lag between a command and a query is
what the workaround at `Main.Audio.cs:46-49` is for.

The contract:

1. **Natives call devices from the engine thread.**
2. **A device call is a command.** The device applies it when it can; the native does not depend on when.
3. **A device reports by events** that it queues for the engine thread: a channel ended, a movie frame is ready, an
   input arrived, a frame is due. The tick delivers them.
4. **A query answers from the device's last report.** A native never expects a query to reflect a command it sent
   in the same tick.
5. **Where a native needs the answer at once, it holds the state itself.** The BGM fade's level and deadline are
   the audio native's, not the device's.
6. **One exception: calls that return data and change nothing** may run on the engine thread and return at once.
   These are rasterizing a glyph, decoding an image, and opening a movie for its metadata (`IMovieDecoder`'s comment:
   "Construction is synchronous so metadata needed by the VM is available before op 0x236 returns").
   **Such a call never needs the front end's thread.** Under §5.3's fallback the front end waits for the engine
   thread, so a call that needed the front end's thread would deadlock. Today's three meet this: the TextServer
   rasterizer runs on its caller's thread under its own lock (`godot/GodotTextServerGlyphMaskRasterizer.cs:51`),
   images are decoded by engine code, and movie metadata comes from ffmpeg.

A dialog is a request object that the device completes. A reset cancels outstanding requests, so a signal cannot
outlive its wait.

### 6.3 Graphics: a frame description and a reference renderer

**`RenderObject` is not a device's vocabulary** (`Model/GfxState.Contracts.cs:117`). It carries AGE's four
transform channels (`TransformState` and the rotation, scale and translation cycles), AGE's tint paths (`Tint`,
`TintStrength`, `MultiplyTint`), `BlendKind`, colour keys, and the surface, colour, blur and patterned transition
states. It is sampled for one instant, but a front end still has to combine the channels into a matrix, pick the
tint path, and draw AGE's transitions and blurs. That code is written three times today: the GPU path and the
software path in `godot/Main.Compositor.cs`, and `Sys4/RetainedSurfaceRasterizer.cs`.

The design has four parts.

1. **The graphics native lowers `RenderObject`s into a frame description.** It is immutable, and nothing in it
   needs `GfxState` or a clock to interpret:
   - the background colour, and whether the previous frame's pixels are kept;
   - an ordered list of draw items, each with a texture, a source rectangle, one affine matrix, an opacity, a tint
     (mode, colour, strength), a blend mode and a colour key;
   - the effect ranges, with their progress already sampled: a surface transition, a radial or directional blur, a
     whole-screen transition (source items, target items, overlay colour, alpha).

   Each blend mode, tint mode and effect is defined once, by the reference renderer's code.
2. **The reference renderer lives in `Ome.Engine`.** It is today's software path (`RecompositeSoftware`,
   `CompositeVisibleObjects`, `BlitLayer` and the transition and blur drawing), reading a frame description and
   writing an RGBA frame. `RetainedSurfaceRasterizer` shares the lowering, or documents why its rules differ.
3. **Two graphics devices, with different jobs.**
   - *Renderer:* draw this frame description with the GPU. This is the path for play, and `GpuRetainedRenderer`
     becomes one. It must match the reference renderer within a stated tolerance, and a test compares the two on a
     driven scenario.
   - *Frame presenter:* show a finished frame from the reference renderer. It serves headless runs, tests, the logs
     that need the software path's decisions (`--gfx-log`, `--timeline-log`), a new front end before its renderer
     exists, and any frame the renderer cannot draw. Today those frames are whole-screen transitions and an additive
     LERP tint.
4. **Surface pixels are the graphics native's.** They are `RgbaImage` snapshots today (`_surfaceImages`,
   `CaptureSurfacePixels` and `ReplaceSurfacePixels` at `godot/GodotAdvHost.Surfaces.cs:16`, `:89` and `:97`). A
   device receives uploads and can be rebuilt from them.

**The reference renderer is too slow for play.** That is measured, and it is why GPU rendering became the ordinary
backend on 2026-07-22 (`docs/phase-a-slice-plan.md`, P1 to P4):

| Workload | Software path, first capture | Software path, after the CPU optimizations | GPU path |
|---|---|---|---|
| The dungeon map (`FIELD`, about 962 objects) | 49 ms median, 65 ms p95 | 13.9 ms median, 18.9 ms p99 | 2.1 ms median, 2.8 ms p99 |
| SC0000's heaviest effects (`SC0000@0x123de`, 8 to 12 layers at 800×600) | 64 ms median, 88 ms p95 | 65 ms median, 72 ms p95 | 0.08 ms median, 0.13 ms p95 |

Times are main-thread time per recomposited frame. Rasterizing was 98% of the software frame in the SC0000
capture. Taking the snapshot was 0.017 ms at p95 in the SC0000 capture, so the
lowering is not where the time goes, and it can live in the engine.

So a second front end does have to write a renderer. What the design changes is what that renderer has to know: a
fixed list of primitives, each defined by the reference renderer and checked against it, instead of AGE's object
model. The tint modes and the effects are still engine-defined terms, which makes the renderer the one device that
carries engine vocabulary. The frame description keeps that vocabulary as small as it can, and a renderer can leave
an effect to the reference renderer until it implements it.

**Which thread renders.** The reference renderer is a function of a published frame, so the front end may run it on
its own thread. That requires a published frame to be immutable, including the pixels it refers to. How the native
keeps that cheap (copying the surfaces that changed when it publishes, or versioned buffers) is settled in
milestone 7.

**What this gives:** `Ome.Cli` and the tests can produce the frames that scenarios such as `kamidori-furigana.steps`
and `kamidori-history-after-load.steps` compare with native, without Godot.

**Not included:** finishing the GPU path (whole-screen transitions through an offscreen target, the additive
LERP-tint variant). That stays a backlog item; this design gives it the reference to match.

### 6.4 Files

There is no file device in this design.

- `INativeDatStore` reads and writes save documents by role, and `IAssetStore` opens catalog entries. Both carry
  AGE vocabulary, so they are natives' seams, not devices. They stay in `Ome.Engine`.
- Their implementations (`DirectoryNativeDatStore`, `Sys4AssetStore`) use .NET's file API, which already hides the
  operating system.
- A device that reads and writes bytes by path is added below them when a platform has no file system that .NET can
  use (§2, decision 9). `docs/platform-portability.md` records that trigger.

### 6.5 Headless devices

One set of devices that presents and plays nothing serves `Ome.Cli` and the tests. `CaptureHost` and the CLI's three
hosts stand in for the whole host today. Afterwards a headless run uses the real natives over these devices, so it
runs the same engine code as Godot does, and the reference renderer gives it real frames.

### 6.6 The Godot front end afterwards

`godot/` implements device interfaces and builds the engine. It holds no class that implements a native's
interface, and no rule about AGE's objects.

## 7. Engine builds

### 7.1 What a build is

An engine build is one build of native AGE whose behaviour the port reproduces. It is finer than the script ABI:
Kamidori's 4.33F and 4.33G both run `SYS4433` scripts (`docs/engine-re.md`, "Kamidori engine builds").

**The engine names its own build.** It keeps a four-character version at `ctx+0x53b74` and writes it into every
save (`docs/engine-re.md`, "The compatibility id is the engine version"):

| Build | Stamp | Game | Script ABI |
|---|---|---|---|
| 4.24J | `424J` | Himegari | `SYS4422` |
| 4.33G | `433G` | Kamidori, clean install | `SYS4433` |
| 4.33F | `433F` (inferred from the version; not read from a file) | Kamidori, the translated install | `SYS4433` |

The profiles already carry these stamps as `compatibilityId`: 1244934708 is `424J` and 1194537780 is `433G`, read
as little-endian bytes. So a build's id is its stamp, and `compatibilityId` is the build's id written a second
time (§7.5).

Himegari's build is 4.24J. The engine table in `docs/engine-re.md` and the revision diff's header called it 4.22,
after the ABI name; milestone 0 found `FileVersion` `4.24J` in the dump's own version resource and corrected both.

A build's definition names three things:

- the ABI table it uses, from the registry;
- its handlers;
- the rules each native gets.

It is resolved once, where the engine is put together. Below that point nothing knows the build's name.

### 7.2 Kinds of difference

`tools/revision_diff.py himegari kamidori` marks 125 opcodes as differing. By where the strong differences sit (37
of the 125 score 6 or less, which is usually noise):

| Where the difference is | Opcodes | How it is expressed |
|---|---:|---|
| An operand is read differently (integer against float) | 22 | The registry's operand contract; a handler variant if that is not enough |
| Only in the handler | 19 | A handler variant, bound in the build's table |
| Only in shared code below the handlers | 71 | A rule given to a native |
| Both | 13 | Both |
| In code the port does not model (file loaders, Direct3D device handling) | — | Nothing: the row is closed with that reason |

Also: 17 opcodes exist only in Kamidori's engine. Those are handlers that only its build binds.

**Count helpers, not opcodes.** The rows below the handlers come from 26 differing shared helpers, and each helper
is used by many opcodes. So the number of rules is bounded by the helpers:

- The two strongest (`0x45b600` and `0x459d90`, scores 172 and 160) are text layout. Both carry
  `set:BlankExtentMode`, and the second carries `div 17`, the glyph blend. One or two rules explain every opcode
  that uses them.
- Some are outside what the port models: `0x483360` (score 45) probes an audio container for `AOGG`, `OggS` and
  `RIFF`, and `0x4713b0` (score 20) is Direct3D's `ResetDevice`.

Read from the helper table, the 71 rows come to about ten rules. That is an estimate, not a triage.

### 7.3 Rules

A rule is a named behaviour that a native is given. It takes one of three forms, by the size of the difference:

1. **A number or a table differs.** The rule is a value, and there is no branch. Glyph edge blending has 16 steps
   in Himegari's build and 17 in Kamidori's. The `SYS4REG.INI` key bindings are a table.
2. **One step differs.** The rule is a named option, and the one site branches on the behaviour. Op `0x215` already
   works this way: `Gfx.QuerySlot(handle, unboundReadsAbsent: …)`.
3. **A whole algorithm differs.** Two implementations sit behind a small interface, and the build picks one.
   `RubyLayoutRules` is already a rules object of this kind.

Three rules about rules:

- **Name a rule by the behaviour, never by the revision.**
- **One rule per independent change.** A lumped "text generation 2" breaks when a third engine takes half of it.
- **Each rule's comment points to its evidence** in the registry or `docs/engine-re.md`.

### 7.4 A build is its parent plus changes

One file, `EngineBuilds.cs` in `Ome.Engine.Vm`, declares every build (names illustrative):

```csharp
public sealed record EngineBuild(string Id, string AbiId, HandlerSet Handlers, EngineRules Rules);

public sealed record EngineRules(TextRules Text, GfxRules Gfx, SettingsRules Settings, SaveRules Saves /* … */);

static readonly EngineBuild Age424J = new(
    Id: "4.24J",
    AbiId: "SYS4422",
    Handlers: HandlerSet.Core,
    Rules: new EngineRules(
        Text: new TextRules(GlyphBlendSteps: 16, Ruby: RubyRules.Fixed),
        Gfx: new GfxRules(UnboundObjectReadsAbsent: false),
        Settings: SettingsRules.Age424,
        Saves: new SaveRules(VersionStamp: "424J", NumberedGfxRecordSize: 724)));

static readonly EngineBuild Age433G = Age424J with
{
    Id = "4.33G",
    AbiId = "SYS4433",
    Handlers = Age424J.Handlers.Plus(Handlers433.All),
    Rules = Age424J.Rules with
    {
        Text = Age424J.Rules.Text with { GlyphBlendSteps = 17, Ruby = RubyRules.ShiftModes },
        Gfx = Age424J.Rules.Gfx with { UnboundObjectReadsAbsent = true },
        Settings = SettingsRules.Age433,
        Saves = new SaveRules(VersionStamp: "433G", NumberedGfxRecordSize: 740),
    },
};
```

The rule types live in `Ome.Engine`, next to the natives that read them. The build definitions live in
`Ome.Engine.Vm`, because they also name handlers.

Change lists should stay short. Measured with `revision_diff.py`: 4.33F to 4.33G differs in 1 opcode, Kamidori to
Arterial (4.38D) in 36, Himegari to Kamidori in 125.

### 7.5 How a build is chosen

- **The profile manifest names its build** in an `engineBuild` field (`4.24J`, `4.33G`). It keeps `engineAbiId`,
  because the Python tools read manifests and have no build table; `EngineBuilds.ForProfile` rejects a profile whose
  ABI is not its build's. Composition loads the opcode table by the build's ABI.
- **One build per profile** (§2, decision 8). Kamidori's translated install runs 4.33F and its clean
  install runs 4.33G. The port reproduces 4.33G for the profile, whichever executable the player has:
  - the port never runs the executable;
  - the reference moved to the clean install on 2026-11-08, and the profile's stamp is `433G`;
  - the two builds differ in one opcode.

  If 4.33F is ever wanted, it is a third definition (4.33G with that one change), chosen where the engine is put
  together from the install fingerprints the mod loader already keeps. Nothing else would change.
- **Engine facts leave the profile in milestone 0** (§2, decision 12). `compatibilityId` is the build's
  id. `numberedGfxRecordSize` (724 and 740) is a constant inside the save handlers, per the revision diff. Both
  became the build's `SaveRules` (version stamp and record size), which composition hands to persistence.
  `expectedPackedSaveVersion` (310 and 320) is the game's and stays: AGE reads `SAVEVERSION` from the game's
  `SYS4INI.BIN`, and the profile value only checks the install.

### 7.6 Enforcement

**By structure first.** Nothing below the point where the engine is put together takes an id string (the profile
id that labels an unsupported-opcode diagnostic travels in `VmOptions.ProfileId`; no behaviour reads it):

- `VmCompatibilityContext` (`ProfileId`, `EngineAbiId`, `ProbeMode`) is today's seam. The build replaces it; it is
  not kept beside the build.
- `AgeGlyphMaskCompositor.CoverageAlphaFor(string engineAbiId)` takes a rule value.
- `RubyLayoutRules.ForEngine(EngineAbiId)` (`Vm/VirtualMachine.AdvText.cs:130`) becomes the build's ruby rule.
- The check that the build's ABI matches the opcode table's (`Vm/VirtualMachine.cs:209`) moves to where the engine
  is put together.

As built: the VM takes an `EngineBuild` and keeps only its rules. A VM given none (tests and tools that run a bare
table) takes the first build declared for its table's ABI, or 4.24J for the opcode catalog; the lookup and the ABI
check are `EngineBuilds.ForVm`, in the build file.

**A test is the backstop.** It fails when anything other than these reads the ABI id or a build id:

- `EngineBuilds.cs`;
- profile loading and the registry loader (`Profiles/`, `Sys4/Sys4Loader.cs`, `Model/OpcodeTable.cs`);
- the script toolchain: `Ome.Script` (the compiler, the decompiler and the patch code) and `Ome.Mods/Linking/`
  (`ModLinkBase.cs`, `ModCatalogBuilder.cs`) pass the table's ABI to choose opcodes and write the script header's
  magic, which is a fact about the file;
- composition: `godot/Main.cs`, `godot/Main.Mods.cs`, and the command-line tools (`Ome.Cli`), which put the engine
  together for their own runs;
- diagnostics that print the id: `Sys4/Sys4OpcodeDecodeException.cs`, `Diagnostics/UnsupportedOpcodeDiagnostic.cs`,
  `Ome.Engine.Frontend/GodotTraceSink.cs`, and the error messages at `Localization/MessageSegmenter.cs:14` and
  `Ome.Mods/Localization/Import/ComposedStrings.cs:40`.

The test is `EngineBuildTests.NothingBelowCompositionReadsAnAbiOrBuildId`. It strips comments and fails on an ABI or
build id read, a `SYS4nnn` or build-id literal, or a build lookup anywhere else in `engine/` and `godot/`.
`EveryAllowedReaderExists` fails when a file or folder on the list is gone, as a move would leave it.

`Vm/OpcodeRuntimeCoverage.cs`, which read the catalog's ABI id, went away with milestone 1.

One thing the test would not have seen: `Sys4/ScriptAssembler.cs:69` wrote the header magic as the literal
`"SYS4422 "`. Milestone 0 made it take the ABI from its caller: `Assemble` writes its table's ABI, and a table of no
script ABI (the opcode catalog) gets the bare family magic `SYS4`. It is on the list as script toolchain.

### 7.7 One implementation where builds agree

A difference in native code is a difference for the port only when the same state gives a different result. Where
one implementation is right for every input either build can produce, keep one implementation and record why in the
registry. Ops `0x13a` and `0x23b` are handled this way today: 4.33's numeric glyph record has two more fields, and
4.33's `0x13a` writes them as zero.

### 7.8 Adding a game

1. Add the profile and dump its engine.
2. Run `revision_diff.py` against the nearest build.
3. Triage each row: noise, outside what the port models, the same in practice (§7.7), or a real difference. Start
   from the shared helpers (§7.2).
4. Put each real difference where §7.2 says: registry, handler variant or rule.
5. Declare the build as the nearest build plus those changes.

## 8. Other callers of natives

Handlers are not the only callers. These reach engine behaviour by their own paths today, and should call the same
natives:

- **The shell.** `Ome.Engine.Frontend/Shell/` holds the shell's own model: its commands, menus, strings and the
  options panel. That stays front-end code and calls natives. What a script can read or change (`VmMenuState`, the
  message toggles, the settings) is the shell-services native's. Milestone 9 draws the line field by field.
- **The driver.** `IDriveBackend` captures the VM's state and injects input.
- **Localization.** The presenter belongs inside the text native; the handlers do not know about it.
- **Lua mods.** The Lua draft's `maid.vm` (its §8.1) is mostly the interpreter: frames, locals and calls. Those
  bind to a facade that `Ome.Engine.Vm` offers. Its script memory (`vm.g`, `vm.gf`, `vm.gs`) binds to the
  script-memory native. That is for the Lua spec to settle; this design only makes it possible.

## 9. Acceptance

1. **Layering.**
   - The build fails on a reference from `Ome.Engine` to `Ome.Engine.Vm`.
   - Handler code that names a device type does not compile, and a test fails if the `Ome.Engine.Vm` assembly
     references `Ome.Engine.Devices`.
   - A test fails if a native's interface names a device type, or if `godot/` implements a native's interface.
2. **No revision names.** The test of §7.6 passes.
3. **One handler table.** A test over every ABI table fails on an implemented opcode with no handler, or a handler
   bound to an unknown name. No hand-kept opcode list remains.
4. **Behaviour-neutral.** After each milestone, these give the results they gave before (milestone 6 excepts what
   the modal change of §5.3 alters, if the native trace confirms it):
   - the suite, `validate.py`, and the `oms-rebuild` trace gate;
   - the drive scenarios on both games;
   - saves: `Ome.Cli save-diff` finds no difference between saves the same scenario wrote before and after;
   - timing, for a milestone that moves timed behaviour: `--timeline-log` runs of the drive scenarios, within a
     tolerance set before measuring;
   - frame time, for a milestone that touches rendering: `--perf-log` captures of the dungeon map (`FIELD`) and of
     SC0000's effect band (`SC0000@0x123de`) on the GPU path, no worse than captures taken before the milestone.
5. **Natives run without Godot.** Each moved behaviour has tests in `Ome.Engine.Tests` over headless devices. A
   headless run produces frames with the reference renderer.
6. **No hand reset.** A root reload contains no per-field assignments, and a test shows a reload keeps no scene
   state.
7. **A third build costs a definition.** When the third game's build is added, no site that reads a rule changes,
   except to add a rule that is new.

## 10. Non-goals

- Downloadable opcode sets (§2).
- Splitting `Ome.Engine` further, for example a separate project for the model and the loader.
- Rewriting the interpreter so that it can be suspended and resumed.
- Changing a save format, or any behaviour.
- Restructuring `Main` beyond what moving device code needs. Its modal dialogs and its launch options stay backlog
  items.
- Finishing the GPU path (§6.3).
- A file device (§6.4).
- Putting native rules in a registry file. The registry keeps facts; the build file keeps choices (§2).

## 11. Milestones

Each is behaviour-neutral and lands on its own.

| # | Milestone | Done when |
|---|---|---|
| 0 | **Engine builds.** `EngineBuild` and the rule records. The five `IsSys4433` selections move into the build file. The build replaces `VmCompatibilityContext`, and no method below composition takes an id string. Profiles name their build; `compatibilityId` and `numberedGfxRecordSize` come from it. | `EngineRevisions` is deleted; the test of §7.6 passes; saves are equal under `save-diff`. |
| 1 | **Handler table.** One opcode-to-handler table, coverage derived from it, the typed step result. State stays where it is. | Acceptance 3. `himegari-read-skip.steps` gives the same result. |
| 2 | **Census.** For each instance field of `VirtualMachine` (about 120) and of `GodotAdvHost`: the native that will own it, the files that read and write it, its lifetime, whether a save records it, and the tests that assert on it. | The table exists, kept with the implementation plan. The order of milestones 6 to 11 is confirmed or changed from it. §12's test count is filled in. |
| 3 | **Projects.** Create `Ome.Engine.Vm` and move `Vm/`; move `UnsupportedOpcodeDiagnostic` out of it. Create `Ome.Engine.Devices` with the interfaces that are devices already (§3.3), except `IPaceClock` (milestone 5); put the two thumbnail handlers behind native methods. | The build fails on a reference from `Ome.Engine` to `Ome.Engine.Vm`. A compile that names a device type from handler code fails. |
| 4 | **Lift the host**, in two steps. (a) `GodotAdvHost`'s six files move into `Ome.Engine` with their threading and locks unchanged; the 30 `Main` members they call become one typed interface that `Main` implements. (b) The software compositor moves into `Ome.Engine`, drawing into a buffer that the front end uploads. | `godot/` has no class that implements `IHost`. The lifted host runs in `Ome.Engine.Tests` over a front end that does nothing, and a test renders a frame. A drive scenario shows the typed calls keep `CallDeferred`'s timing. |
| 5 | **Skip and auto state.** One owner for the skip, auto and fast-forward state the census lists, and for fast-forward pacing (`VirtualMachine.FastForwardPace.cs`). `IPaceClock` moves into `Ome.Engine.Devices`. The waits stay where they are. | Those fields are private to one type. `VirtualMachine`'s constructor takes no `IPaceClock`, and `FastForwardPaceTests` drive the new owner. The drive scenarios that use skip and auto are unchanged, and `himegari-read-skip.steps` keeps its time. |
| 6 | **Audio pilot.** The audio native, the audio device, and the tick function (§5.3, part 1). First use of the device contract (§6.2). Before it lands, the native trace of §13 settles the modal waits. | Audio behaviour is tested without Godot. `Main.Audio.cs` runs no fade tween and no ducking, and its workaround at `:46-49` is gone. Every wait in §5.3's table runs the tick or is marked modal, and the test of §5.3 passes. BGM fade and voice timings are within tolerance. |
| 7 | **Graphics.** The lowering and the frame description; the reference renderer and `GpuRetainedRenderer` both read it; surface pixels and transition timing move into the native. | The traversal is written once. The front end reads frame descriptions, not `GfxState`. GPU frames match the reference within the stated tolerance. GPU frame times on `FIELD` and SC0000's effect band are no worse than before. |
| 8 | **Movies.** | The decoder is the only movie code a front end supplies. |
| 9 | **Saves, settings and shell services.** The shell and the driver call natives. | `IShellHost` and `IVmMenuServices` hold nothing a native now owns. |
| 10 | **Text.** | The text part of the lifted host is a native with its own state, and the presenter is inside it. |
| 11 | **Input, waits and the engine loop.** Waits become a native; the thread model of §5.3, part 2. | The host's locks and wait handles are gone or documented one by one. Timing is within tolerance. The thread question is answered in writing. |
| 12 | **Close.** Remove the lift's interface and remove `IHost` from the front end; rename the native interfaces after their natives (§2, decision 13). | Acceptance 1, 5 and 6 in full. No native interface is named "host". |

Notes on the order:

- **Milestone 0 is what a third game needs first.** Milestones 0 to 3 do not depend on each other and can be done in
  any order.
- **Milestone 1 needs no fast-forward re-timing.** Pacing is a wall-clock cost per op and per page
  (`VmOptions.FastForwardOpMicroseconds` and `FastForwardPageMicroseconds`), and the VM sleeps until the deadline.
  Faster dispatch only lengthens the sleep.
- **Milestone 4 moves code before anything changes threads.** It gives "natives run without Godot" early and with
  no timing risk. Two things about its interface:
  - It is provisional. It carries AGE vocabulary (`ArmVoiceBgmDuckRelease`, `StopVoiceForMessageSkip`), so it lives
    in `Ome.Engine` and is exempt from §3.1's device test. Each of milestones 6 to 11 replaces its part with a real
    device interface, and milestone 12 deletes what is left.
  - The seam has a second side. `Main` calls 52 distinct members of the host (`SnapshotBackbufferObjects`,
    `SignalInput`, `PresentFrame`, `PublishMovieFrame` and others; `grep -ohE '_host\??\.[A-Za-z]+' godot/Main*.cs |
    sort -u`). Those stay calls on the lifted class.
- **Milestone 4 does not change which path draws.** Normal play stays on the GPU path. Step (b) only moves the
  software path's code.
- **Milestone 6 does not settle the thread question.** It runs the tick at the service points of §5.3, part 1, on
  today's threads. Milestone 11 changes the
  threads, on its own.
- **Text is late because it is the largest subsystem and the one where fidelity is hardest to hold**
  (`GodotAdvHost.AdvText.cs` is 1,147 lines). The pattern should be proven on smaller ones first.

## 12. Costs and risks

- **Fidelity regressions.** This is the main risk. One subsystem per milestone, with the gates of acceptance 4,
  keeps each change reviewable.
- **Frame timing.** Moving timed behaviour off Godot's thread can shift when a frame shows. Milestone 6 measures it
  for audio, and milestone 11 for the thread model.
- **Rendering speed.** Building a frame description adds a step to every GPU frame, and the GPU path's allocations
  were brought down on purpose (a few KB per `FIELD` frame). The lowering reuses its buffers, and milestone 7 is
  gated on `--perf-log` captures.
- **Saves.** Once each native writes its own records, a missed field changes a save. `save-diff` after every
  milestone is the check.
- **A missed wait.** A wait left out of §5.3's table stalls a fade or a duck release while it blocks. The test of
  §5.3 is the check.
- **Test changes.** Handler tests keep using fake natives. A test that asserts on state which moves into a native
  must switch to the real native over headless devices. The census counts them.
- **The lift's typed calls.** They must keep `CallDeferred`'s timing. Milestone 4 checks that on a drive scenario.
- **Work in flight.** The Lua draft assumes today's VM; the Lua spec should be written against §8.
- **Wiring.** Each native's constructor and the code that puts the engine together are new, in exchange for
  dependencies the compiler can see. One factory builds the engine for the nine places outside the tests that
  construct a VM.
- **Build files.** Two solutions (`engine/OmeEngine.sln`, `godot/OME.sln`), `tools/validate.py` and CI gain two
  projects. `docs/PROJECT-STRUCTURE.md` replaces its per-file ownership paragraphs with the layer and native lists.
- **Length.** Thirteen milestones. Each one lands on its own, and the work can stop after any of them with the code
  in a better state than before.
- **Speed.** An array index and a call replace two string switches, so dispatch should be faster.

## 13. Still to measure

Every design question is decided (§2). These facts are not verified yet; each names the milestone that measures it.

| What | Where it matters | Measured by |
|---|---|---|
| That native's BGM fade stops while a modal dialog is open | §5.3, §2 decision 10 | A native trace before milestone 6 lands: hook native's fade tick (Himegari's `bgm_fade_tick@0x464960`) under Frida, start a fade, and open the 終了 dialog (`close-request`) while it runs. The same run checks whether native's modal movie (op `0x20f`) keeps its main loop's services running. |
| That an engine-thread loop keeps today's frame timing | §5.3, part 2 | Milestone 11: `--timeline-log` runs of the drive scenarios, against a tolerance set before measuring |
| How many tests assert on state that moves from the VM into a native | §12 | The census, milestone 2 |
| That `DisableTransitiveProjectReferences` keeps device types out of handler compiles | §3.3 | Milestone 3: a compile that is meant to fail |
| Whether the 22 operand-access rows need any code; the port may already read those operands correctly | §7.2 | Milestone 0: one does (below) |
| What the 125 revision differences are, and whether they come to about ten rules | §7.2 | Backlog, "Triage the opcode revision diff" |
| That building a frame description for every frame keeps the GPU path's frame times | §6.3 | Milestone 7: `--perf-log` captures of `FIELD` and SC0000's effect band |
| Whether Himegari's engine dump is 4.22 or 4.24J | §7.1 | Milestone 0: 4.24J |
| Whether `expectedPackedSaveVersion` is the engine's or the game's | §7.5 | Milestone 0: the game's |

**The operand-access rows (milestone 0).** The revision diff lists 24 opcodes whose operands 4.33G reads or writes
as floats where 4.24J uses integers (`0x1cd`, `0x1fd`–`0x1ff`, `0x217`–`0x21a`, `0x21e`–`0x220`, `0x226`–`0x22f`,
`0x233`–`0x235`); the registry already records each as `access_by_abi`. The port reads all of them with the integer
accessor. That is right wherever the operand is an integer form, and in both corpora only two sites pass anything
else: Kamidori's `ALLMAP@0x5d9` and `SAVE@0xcad` give op `0x1fd` the float immediate 37.5 for the x and y scale of a
save thumbnail. So one row needs code: a 4.33G handler for op `0x1fd` that reads those operands as floats
(`docs/backlog.md`, "Float operands").

## 14. Backlog items this covers

From `docs/backlog.md`, `## Architecture`:

- "Opcode dispatch is kept in three hand-written lists" — milestone 1.
- "Revision behaviour is one boolean" — milestone 0.
- "VM state has no owners" — the census in milestone 2, then milestones 5 to 11.
- "Scene reset clears fields one by one" — as each native takes its state; closed at milestone 12.
- "The host's threads have no stated ownership" — typed calls in milestone 4, the locks in milestone 11.
- "Two instruction loops" — the typed step result in milestone 1; one loop in milestone 11 (§5.4).
- "The Godot front end has no unit tests" — milestone 4.
- "The render traversal is written three times" — milestone 7.

Not covered:

- "Normal play still uses the software compositor". Finishing the GPU path is a non-goal. Milestone 7 gives it the
  reference to match and the test that compares.
- "`Main` has no seams" (§10).
- "No single error path".
- "Modal dialogs are separate fields". The dialogs device gives the host's side a request object (§6.2); `Main`'s
  fields stay.

## 15. History

- **2026-11-11.** Drafted from the user's decisions of that day (§2.1), measured at `9ff9661` on
  `feature/localization`.
- **2026-11-12, review.** A design review checked the draft against the code at `6fc8a18`
  (`engine-layering-design-review-2026-11-12.md`, at the workspace root, outside this repository). The revision
  that answered it added:
  - the frame description, the reference renderer and the two graphics devices (§6.3);
  - the list of types that move into `Ome.Engine.Devices`, and the rule against device types in native
    interfaces (§3.3);
  - the device contract (§6.2) and the requests to the interpreter (§5.4);
  - the census, skip and auto state before audio, and the lift of the host (§11);
  - helpers counted instead of opcodes (§7.2), `compatibilityId` as the build's id (§7.1), and enforcement by
    structure (§7.6);
  - counts re-measured at `6fc8a18`.
- **2026-11-12, response.** A second pass found two more problems, fixed in the accepted text: `IPaceClock`
  cannot move before fast-forward pacing leaves the VM (§3.3, milestone 5), and every blocking wait must run the
  tick or be marked modal (§5.3).
- **2026-11-12, accepted** with the decisions in §2.2.
- **2026-11-12, milestone 0.** Engine builds implemented on `feature/engine-layering`: `Vm/EngineBuilds.cs` declares
  4.24J and 4.33G with text, graphics, settings and save rules; profiles name their build; `EngineRevisions` and
  `VmCompatibilityContext` are gone; the test of §7.6 passes. Saves written by the port on `develop` and on the
  branch are equal under `save-diff` for both games. The text, settings and save scenarios (`himegari-furigana`,
  `-antifont-off`, `-settings-dialog`, `kamidori-furigana`, `-config-redraw`, `-shell`, `-history-after-load`) pass
  on the port before and after with the same `SYS4REG.INI`. Their shots are identical except for animation phase
  (Himegari's wisp, Kamidori's wait indicator) and the build label's uncommitted `+`. §7.1, §7.5, §7.6 and §13 were
  updated with what it found.
- **2026-11-12, milestone 1.** The handler table, derived coverage and `StepResult` implemented on
  `feature/engine-layering` (§4, "As built"). Op `0x19a` renamed `get-message-skip-enabled`. The test of acceptance 3
  passes on both games' ABI tables (SYS4422 248 opcodes, SYS4433 269), and the catalog has 265 opcodes without a
  handler. `himegari-read-skip.steps` gives the same result in the same time (37.8 s before and after), and nine more
  port scenarios pass on both builds: the seven of milestone 0, `himegari-save-load` and `kamidori-opening-pages`. Their
  `SYS4REG.INI` are equal, and their shots are identical except for animation phase and the build label. The saves of
  `himegari-save-load` and `kamidori-history-after-load` are equal under `save-diff` except for their timestamp and
  play time.
- **2026-11-12, milestone 3.** `Ome.Engine.Vm` and `Ome.Engine.Devices` created on `feature/engine-layering` (§3.3,
  "As built"). As §3.3 predicted, only the two thumbnail handlers and one internal type stood in the way. The two
  compiles meant to fail do fail, and `ProjectBoundaryTests` is the backstop. The suite (1855), core validation and
  workspace validation on both games (`oms-rebuild` included) pass. The ten port scenarios of milestone 1 pass on the
  new build with equal `SYS4REG.INI`; their shots are identical except for animation phase and the build label, and
  `himegari-read-skip` keeps its time (37.8 s). The saves of `himegari-save-load` and
  `kamidori-history-after-load` are equal under `save-diff` except for their timestamp, and their thumbnails are
  byte-identical.
