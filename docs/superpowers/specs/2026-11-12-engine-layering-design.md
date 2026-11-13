# Engine layering — design

**Date:** 2026-11-12
**Status:** accepted 2026-11-12. Drafted on 2026-11-11 from the user's decisions of that day, revised after one
design review, and accepted with the decisions of 2026-11-12 (§2). Milestones 0 (engine builds), 1 (handler table),
2 (census), 3 (projects), 4 (the lift), 5 (skip and auto), 6 (audio), 7 (graphics), 8 (movies), 9 (saves,
settings and shell services), 10 (text) and 11 (input, waits and the engine loop) are done on
`feature/engine-layering`; §3.3, §4, §5.2, §5.3, §5.4, §6.2, §6.3, §7.5, §7.6, §8,
§11, §12 and §13 record what they settled. The census is in the implementation plan, `docs/superpowers/plans/2026-11-12-engine-layering.md`. §15
records the history.
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

**As built (milestone 5).** `IPaceClock` is `engine/Ome.Engine.Devices/Time/IPaceClock.cs`. It is the one moved type
that changed namespace, to `Ome.Engine.Devices`: its old one was the handler layer's (`Ome.Engine.Vm`), which a
device type cannot keep, and no code outside the VM and its tests named it. The system clock is a private class of
the skip-and-auto native (§5.2). `ProjectBoundaryTests` lists `IPaceClock` with the device types and checks that no
constructor of `VirtualMachine` takes one.

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

Names are suggestions. Each row is settled in the milestone that builds it, from the census (milestone 2). The
census (plan §2, §3.2) gives every field of `VirtualMachine` and `GodotAdvHost` an owner, and adds one the table
lacks: the tick function of §5.3 owns the statement block (ops `0x1f4`/`0x1f5`).

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

**As built (milestone 5).** The native is `Ome.Engine.Natives.SkipAndAuto` (`engine/Ome.Engine/Natives/`):
`SkipAndAuto.cs` holds Skip (op `0x88`, ctx `+0x550fc`), the injected action 6 (`+0x13dc`), read skip, Auto, the
auto-message times and the ADV skip service of ops `0x19b`/`0x19c`; `PhysicalInput` holds fast-forward from a held
action 6, `ClickCancel` the press/release machine of set:CancelMesSkipOnClick, and `Pace` the pacing.

- **Its state.** The census's 25 fields (19 in the VM, 6 in the host) are its private fields, and neither the VM nor
  `EngineHost` keeps one (`ProjectBoundaryTests`). Two pairs became one field each, as plan §3.3 said: run-state
  `0x08000000`, which the VM (`_messageSkipServiceActive`) and the host (`_messageSkipActive` with its two channel
  fields) both kept, is `_fastForward`, joined from a script and a physical channel under one lock; and the
  auto-message times, which the VM copied from the host, live only here.
- **Its rules.** `SkipAndAutoSettings` holds set:CancelMesSkipOnClick, COEXISTMESSKIP and the two pace costs, which
  left `VmOptions`. `GodotVmOptions.CreateSkipAndAuto` fills them from SYS4INI and native's costs.
- **Construction.** Composition (`godot/Main.cs`) builds one native over the system clock and hands it to
  `EngineHost` and `VirtualMachine`. The VM's constructor takes the native and no `IPaceClock`; without one it builds
  its own, with pacing off, as tests and headless tools had it.
- **Telling the host.** The native calls an `IFastForwardListener` after each channel change, on the thread that made
  it, outside its state lock (inside the input lock for a physical change, so a release and a reveal's first tick keep
  one order). `EngineHost` is the listener: when fast-forward starts it completes the reveal and wakes the waits, and
  when it ends it plays the voice that skip held back, as plan §3.2 says. The held voice stays in the host until the
  audio native (milestone 6); the host holds it under the native's lock (`WhileFastForward`), so a voice requested as
  fast-forward ends is either held and then played by the listener, or played at once. The host reads fast-forward
  and the auto-message times from the native; `IInputHost` lost its four skip members and `IAdvHost` the auto-message
  times.
- **What stays in the VM.** The waits (milestone 11); the read-text lookup, whose answer `ApplyReadTextSkip` is given;
  and the interpreter's part of the click cancel, which flushes the input triggers, clears message:ReadTextSkip and
  queues `CALLBACK_SETTING.BIN` (§5.4: a native never runs a script).
- **No interface yet.** The native is a sealed class: tests build the real one with settings and a virtual clock
  (`FastForwardPaceTests`, which also pace it without a VM), so no fake needs an interface.

**As built (milestone 6).** The audio native is `Ome.Engine.Natives.Audio` (`engine/Ome.Engine/Natives/Audio*.cs`).
`Audio.cs` keeps whether each of the 13 channels plays and the native's step of the tick. `Bgm` holds the retained
track and the envelope (the op `0xc2` fade, the voice duck and its release); `Voice` the op `0x1d5` mute request, the
voice that fast-forward holds back and the delayed voice of op `0x2c0`; `SoundEffects` channels 0..9 and their delayed
starts (op `0x2bf`); and `Mixer` the levels, the routes and a numbered load's audio part. It finds sounds through
`IAudioAssets` (`ResourceAudioAssets` reads the catalog).

- **Its state.** The census's 11 fields are its private fields or gone, and neither the VM nor `EngineHost` keeps one
  (`ProjectBoundaryTests`). The VM's pending fade target and the host's fade deadline are one `BgmFade` record, and the
  VM's mixer settings and the host's bus calls are the native's. The voice that fast-forward holds back
  (`_queuedSkippedVoice`) is the native's too, so the native, not the host, listens for the end of fast-forward
  (`IFastForwardListener`) and plays it.
- **Time.** The native reads the engine's `FrameClock`. Each time the tick runs, a fade's level is computed from the
  time since the fade was armed, in native's whole steps. So a fade advances only when the tick runs, and the first
  tick after a modal wait makes up the time, as native's period timer does (§13).
- **Construction.** Composition (`godot/Main.cs`) builds one native over the Godot device, the catalog's sounds, the
  frame clock, the skip native, the mixer settings and `SYS4REG.INI`, which it reads at each voice start for the duck
  settings (plan §3.2). It hands the native to `EngineHost` and the VM. A VM given none builds a silent one
  (`Audio.Silent`). `GameSession.RunScene` takes a factory, so the native it is given is built over the run's own skip
  native.
- **Changes in behaviour**, each toward native's:
  - One delayed sound effect start waits per channel, as native's `SetDelay` slot holds one; before, several could.
  - A delayed voice's delay counts from the first tick outside a statement block, up to a frame sooner than the next
    frame pulse that started it before.
  - The fade and the duck release run on the frame clock, so they follow the driver's `speed`; Godot's tween ran on
    Godot's own time.
  - A sound effect stored as OGG decodes; Godot decoded every sound effect as a wave before.
- **Not modelled, as before:** the delayed starts that native's tick runs inside a statement block while a transition
  parks the interpreter (run-state `0x400`; `docs/backlog.md`).

**As built (milestone 7).** The graphics native is `Ome.Engine.Natives.Graphics`
(`engine/Ome.Engine/Natives/Graphics*.cs`). `Graphics.cs` holds the retained objects (`State`, the `GfxState` that
the VM's `Gfx` returns), the presentation request and the two partners below; `Graphics.Surfaces.cs` the surface
table; `Graphics.Transitions.cs` what ops `0x222` and `0x20c` publish, the render-target snapshots and the
whole-screen transition with its timing; and `Graphics.Frame.cs` the lowering (§6.3, "As built").

- **Its state.** The census's 17 host fields of the surface table, the publication and the screen transition are its
  private fields, and neither the VM nor `EngineHost` keeps one (`ProjectBoundaryTests`). The image, screen-transition
  and backbuffer locks came with them. A surface's pixels are an `RgbaImage` that each change replaces: a fill, a
  copy, text drawn into the surface and a movie mask's capture each publish a new image, so a frame keeps the pixels
  it was lowered with.
- **What stays.** The host keeps the waits that block on a publication or a screen transition, and their fields,
  until milestone 11: the presentation barrier, the request generations and their event, `_foregroundGfx`,
  `_foregroundTransitionWaitBypassed` and `_transitionStartedAtMs`. `IGraphicsHost` keeps only the ops that block (the
  present of ops `0x222` and `0x20c`, the foreground transition wait and the four screen fades), and each is passed
  the native's state. The VM keeps `_tickPresentationEnabled`, which ops `0x21b`/`0x247` only store and read back.
- **Its handlers.** The VM's surface handlers (create, load, draw, release, size, fill, copy, the render-target clear
  and the thumbnails' capture and replace) call the native, which the VM's constructor takes. A VM given none builds
  `Graphics.Headless()`, with no catalog and no screen: a created surface has its size, and nothing loads.
- **Its partners (census addition 2).** Until the movie and text natives exist, the lifted host is both. It answers
  which surfaces and resources a movie shows (`IGraphicsMovies`, milestone 8), and it listens (`ISurfaceListener`):
  before a surface or a range is released it takes the movie off it, as native deletes the slot's movie object first;
  after, it stops that movie and drops the text layouts retained on the surface; and when op `0x1af` replaces a
  surface's pixels with a thumbnail, it drops them too.
- **Construction.** Composition (`godot/Main.cs`) builds one native over the catalog, the frame clock and the canvas,
  and hands it to `EngineHost` and the VM; the host attaches itself as both partners.

**As built (milestone 8).** The movie native is `Ome.Engine.Natives.Movies` (`engine/Ome.Engine/Natives/Movies.cs`
for the ops, the bindings and the mask transitions; `Movies.Playback.cs` for the decoders). What it runs came with
it and is internal to it: `MovieSurfaceRegistry` from `Hosting/`, `MovieRuntime` and `MovieAudioTimeline` from
`Ome.Engine.Frontend`, and `godot/MovieAudioOutput.cs` as `MovieAudioFeed`. It finds a movie's file through
`IMovieAssets` (`ResourceMovieAssets` reads the catalog).

- **Its state.** The census's four host fields (the bindings, and the mask lock and its two maps) are its private
  fields, and so is what `godot/Main.Movie.cs` held: the opened and playing decoders, their sound feeds, and the
  requested rates and stops. `ProjectBoundaryTests` checks that its fields are private, that neither the VM nor
  `EngineHost` keeps one, and that `godot/` names none of its parts.
- **Its handlers.** Ops `0x236`, `0x241`, `0x23a`, `0x246` and `0x24d` call the native, which the VM's constructor
  takes; a VM given none builds `Movies.Headless`, where no movie resolves. Op `0x9`'s reset has the native stop every
  movie, which the host did before.
- **What stays.** Op `0x20f`'s modal movie is a wait, so the host keeps it and its three fields until milestone 11.
  It starts the movie through the native (`ResolveModal`, `StartModal`) and waits until the surface's movie is no
  longer active. The native wakes the host's waits when a movie completes or a mask frame lands, and the effect wait
  asks the native whether a movie holds it (`HoldsServiceWait`).
- **The host's script stack is gone.** The host kept a copy of the VM's script stack only so that a movie's log line
  could name the scene (census, "The script stack"). The VM now passes the running script's name with each movie op,
  and `ILifecycleHost.ExitScriptContext` takes the name it leaves. The host's diagnostic snapshot lost `CurrentScene`.
- **Threads, as before.** The engine thread binds a playback and opens its decoder at once, since op `0x23f` reads the
  stop time right after op `0x236` (§6.2, rule 6). The front end's frame calls `ServiceFrame`, which stops what was
  stopped, takes the decoders that were opened, applies rates, publishes each decoder's newest frame, feeds the sound
  and completes the movies; `Main.Movie.cs` did the same on the same thread. Milestone 11 decides the threads. One
  thing changed: a stop, which `Main` applied in a `CallDeferred`, is applied at the start of the next frame service.
- **Construction.** Composition (`godot/Main.cs`) builds the native over the graphics native, the catalog's movies,
  `FfmpegMovieDecoderFactory` and the Godot stream device, and hands it to `EngineHost` and the VM; the native attaches
  itself to the graphics native as its movie partner and a surface listener. `IFrontEnd` lost its 3 movie members
  (`TryPlayMovie`, `QueueMoviePlaybackRate`, `StopMovie`).

**As built (milestone 9).** Three natives, in the order census addition 3 set: script memory first, then the save
and settings native, which writes the banks into a numbered save, then shell services.

- **Script memory** is `Ome.Engine.Natives.ScriptMemory` (`engine/Ome.Engine/Natives/ScriptMemory.cs`). It owns the
  five banks (integers, floats, strings and the two pointer banks), the engine's string encoding, the integer queues
  and stacks of ops `0x132`-`0x139`, op `0x9`'s reset of them, and the banks' sections of a numbered save. Its partial
  `ScriptMemory.Random.cs` owns op `0x60`'s seeded draws, which the driver's `rand-seed` turns on. The VM's `Globals`
  and its siblings return the native's banks.
- **Saves and settings** is `Ome.Engine.Natives.SavesAndSettings` (`SavesAndSettings.cs`, `SavesAndSettings.Numbered.cs`).
  It owns the shared profile, the save store, `SYS4REG.INI`, the save container's playtime clock and the shutdown
  flush. Native's `set:NoSaveDat`, which was a VM option, is an argument of its constructor. Ops `0x19e`-`0x1a1` and
  `0x1ab`-`0x1af` call it.
- **Each native writes its own records** (§5.1, rule 6). A numbered save holds script memory's banks, audio's track
  and channels (`Audio.Mixer.cs`) and graphics' surfaces and objects (`Graphics.Saves.cs`), and the save native
  decides their order. What is not a native yet (the interpreter's saved frames, text's history tail and source glyph
  objects) it reaches through `NumberedSaveRecords`, which the VM builds once and passes to each save and load. So the
  save native needs no other native to be built before it.
- **Shell services** is `Ome.Engine.Natives.ShellServices` (`ShellServices.cs`, `.Dialogs.cs`, `.ScreenMode.cs`). It
  owns the system-menu fields of ops `0x142`, `0x148`/`0x149` and `0x10f`; the menu toggles and 設定 values the shell
  queues for the interpreter; the two callback scripts they launch, each once, `CALLBACK_SETTING.BIN` before
  `CALLBACK_WINDOW.BIN`; ops `0x1b2`-`0x1b4`'s text; the dialogs; and the screen mode (§6.2, "As built"). Op `0x9`'s
  reset drops its requests and callbacks, restores the system-menu fields and cancels the dialogs. The diagnostic text
  is kept: it lives in native's `EngineCtx`, and a `GameSession` shares it across its scene VMs as before
  (`DiagnosticOutputState`).
- **What stays at the VM.** `MenuState` and `MessageSettings` read several natives, so the VM composes them (census
  addition 4). It applies the queued requests where it did, at an instruction boundary and inside a polling text
  wait, and then asks the shell native for `CALLBACK_SETTING.BIN`. `RequestMenuHideWindow` stays on
  `IVmMenuServices` because it answers at once. `ScriptError` stays on the VM as the run's outcome. The host still
  seeds the message settings at start and flushes them at shutdown, since their values are text's and skip's until
  milestone 10; it reads `SYS4REG.INI` through the save native.
- **Done when.** `IShellHost` lost `Windowed` and `SwitchScreenMode`, and `IVmMenuServices` its three toggles.
  `IShellHost` holds only front-end services (close, the panel, the screenshot and its folder, quit, the title, the
  hold), and `IVmMenuServices` the menu state and the hide request. `ProjectBoundaryTests` checks both lists, that the
  three natives' fields are private, and that the VM and `EngineHost` keep none of the 32 census fields that moved.
  The census's other six for these natives stay as the VM's properties: the five banks, which return the native's,
  and `ScriptError`.
- **Construction.** Composition (`godot/Main.cs`) builds the save native over the shared profile, the save store and
  `SYS4REG.INI`, and the shell native over `SYS4REG.INI` and the window and dialogs, which `Main` implements; it hands
  both to `EngineHost` and the VM. A VM given none builds an empty save native and `ShellServices.Headless`, since
  handler code cannot name a device type. `GameSession` builds both for each scene it runs, so each scene's playtime
  counts from its start, as before.

**As built (milestone 10).** The text native is `Ome.Engine.Natives.TextManager`
(`engine/Ome.Engine/Natives/TextManager*.cs`), named after native's text manager. `TextManager.cs` holds its
construction, the fonts, the message settings, op `0x9`'s reset and its save records; `TextManager.Layout.cs` draws
runs, strings and History groups into the layouts' surfaces and binds them; `TextManager.Reveal.cs` holds the
show-text runs and the reveal; `TextManager.WaitIndicator.cs` the wait indicator and the page's suspension; and
`TextManager.Presentation.cs` the presenter, the string stores and History presented again.

- **Its state.** 48 of the census's 51 text rows are the native's private state: the layouts and the backlog
  (`AdvTextHistory`), the style, the retained layouts and their cursors, the glyph timer and the reveal, the wait
  indicator, the page's suspension, MessageSpeed, MesWinAlpha and UseAntiFont, the glyph rasterizers and their caches,
  the installed fonts of SYS4433's ops `0x2dc`-`0x2de`, the presenter and the string stores. The VM keeps the other
  three: `_presentedMessage` (below), and `Emitted` and `TextHistory`, which return the native's. The host's `_advText`
  and `_liveText` were written and never read, so they are gone. The libraries the native lays out and presents with
  (`Ome.Engine/Text/`, `Ome.Engine/Localization/`) stay where they are.
- **One reveal flag.** Run-state `0x20000000` was two flags, the host's `IsTextRevealing` and the VM's copy of it. It
  is now the native's `IsRevealing`, which the VM, the host, the front end's input and the driver read. It is set as a
  run that may reveal starts (a delay, no fast-forward, and the page's reveals not finished), before the run's layout,
  as native's builder sets it. The host set it after the layout. So a click or a held skip key that arrives during a
  layout now reaches the reveal. That is this milestone's one change of behaviour, and no driven scenario shows it.
- **Its devices** (`TextDevices`): the gameplay glyph rasterizer and its coverage format, the rasterizer for presented
  text, the installed-font source and the font metrics probe. A VM given none builds `TextManager.Headless`, which
  draws nothing and keeps every layout, cursor, setting and indicator, as a host without text pixels did.
- **Its listener.** `ITextListener` hears what the native shows and draws: a run, a string, a History group or a
  redraw, a layout cleared or published, the wait indicator and the page's suspension. It changes nothing; the test
  hosts and `CaptureHost` see the text through it, and the VM attaches its host when the host implements it. Tests
  that need text laid out run the real native over grid glyphs (`TestText` in `Ome.Engine.Tests/TestSupport.cs`).
- **What stays at the VM.** `_presentedMessage`, the message whose runs it is showing, because it names the VM's
  frames and script. The VM also resolves which string cell an operand names; the native keeps the records. Op
  `0x72`'s page wait calls the native's `BeginPageWait` and `StopWaitIndicator` around the host's wait.
- **What stays at the host.** `IAdvHost` holds only `WaitForTextReveal`, the blocking wait that services the native's
  reveal until it ends; it stays until milestone 11. The host still seeds skip's Auto times from `SYS4REG.INI` and
  writes the message settings at shutdown; the text native reads and writes its own three.
- **Its save records** (§5.1, rule 6). `NumberedSaveRecords` takes the native, which writes and reads the history
  tail and the source glyph objects. Only the interpreter's saved frames are still a callback.
- **Done when.** `ProjectBoundaryTests` checks that the native's fields are private, that the VM and `EngineHost`
  keep none of the census's 38 field names and 9 property names, and that `IAdvHost` is the reveal's wait alone.
- **Construction.** Composition (`godot/Main.cs`) builds the native over the graphics native, skip and auto, the
  frame clock, the glyph rasterizers and the font list, the build's ruby rules and blank-extent mode, `SYS4REG.INI`,
  the page locator and the timeline. It hands the native to `EngineHost` and the VM. The native attaches itself to the
  graphics native as the surface listener that drops the layouts retained on a released surface (census addition 2).

**As built (milestone 11).** The input and waits native is `Ome.Engine.Natives.InputAndWaits`
(`engine/Ome.Engine/Natives/InputAndWaits*.cs`):

- `InputAndWaits.cs` holds its construction, the wait flags, the stop and the scene reset;
- `.Blocking.cs` is the one place where a wait blocks;
- `.Events.cs` routes the front end's input;
- `.TextWait.cs` holds the ADV input poll (ops `0x72` and `0xfa`) and the reveal's wait;
- `.Sleep.cs` holds the sleeps, the timed-callback deadline, the instruction boundary's `FrameYield` and the front
  end's modal hold;
- `.Transitions.cs` holds the effect and screen transition waits;
- `.Presentation.cs` holds the synchronized presentation and the rule that replaced `ScriptPresentationBarrier`;
- `.Modal.cs` holds op `0x20f`'s modal movie and the three dialog waits;
- `.Turns.cs` holds the engine thread, its frame turns and the front end's requests (§5.3).

`StopAndReload` (`Natives/StopAndReload.cs`) holds the stop and the return to title. The waits and the interpreter
share its one copy.

- **Its state.** The host's 11 census rows belong to the native: the three wait handles, the wait flags, the modal
  hold and its release, and `SleepScale`. The stop and the reload interrupt are `StopAndReload`'s. The native also has
  the fields that milestones 7 and 8 left in the host for the waits that block on graphics and movies:
  - the synchronized presentation's request generations and its timestamp;
  - the foreground transition's fields;
  - op `0x20f`'s three fields;
  - `_presentationWithheldByStatementBlock`.
- **What stays at the VM.** The census's 24 VM rows stay at the VM, because they are the interpreter's: the hotspot
  callback frames, the input latch, op `0xcc`'s callback and ops `0xd3`-`0xd5`'s, the pointer, buttons and wheel that
  ops `0x108` and `0x10d` read, the coroutine yield and the hidden-window wait. The native routes each input to them
  through `IInterpreterInput` (`Natives/IInterpreterInput.cs`), which the native defines and the VM implements (§5.4,
  rule 2). The native decides which of them an input reaches, as `age_main_window_proc@0x486320` and the ADV services
  do. `_interactiveLock` went when input moved to the engine thread (§5.3).
- **Input routing.** `godot/Main.Input.cs` passes each pointer, button, wheel, key and joystick event to the native.
  It keeps only its own controls: the menus, the dialogs and its hot keys. The rules it used to apply are now the
  native's: `CLICKONUP`, `message:AdvanceMesOnWheel`, `system:EffectSkipOnClick`, the advance at a wait, and the
  click that skips an effect or cancels a movie.
- **What stays at the host.** `EngineHost` keeps the `IHost` members the VM calls, each of which calls the native.
  It also keeps the cursor, the seed of the Auto delays and the shutdown write of the message settings. It holds no
  lock, no wait handle and no wait flag. Milestone 12 removes it.
- **Done when.** `ProjectBoundaryTests` checks three things: the native's fields are private; `EngineHost` keeps
  none of the census's wait fields and no lock, wait handle or semaphore; and no `godot/` file routes input to the
  interpreter. `InputAndWaitsTests` replaces `EngineHostWaitTests`. It keeps milestone 6's fade tests and fails on
  any blocking call in `InputAndWaits*.cs` or `EngineHost*.cs` outside `.Blocking.cs` and `.Turns.cs`. It also checks
  the routing and the two threads (§5.3).

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

**As built (milestone 6).** The tick is `Ome.Engine.Natives.EngineTick`. `Service()` runs the audio native's step,
which is all the timed work that has moved so far, and the tick owns the statement block (plan §3.2, addition 1).
`ServiceInstructionBoundary` calls it first. In the host every blocking call goes through `EngineHost.Waits.cs`:
`WaitAndTick` and `WaitAnyAndTick` block until one wake and then run the tick, and `WaitModal` blocks without it. The
table's rows hold: the three dialogs and the front-end modal hold (`FrameYield` and the hold inside the input poll) are
modal, and every other wait runs the tick. The modal movie runs it, since native services its main loop during one
(§13). The host keeps `_presentationWithheldByStatementBlock`, which only marks, on the front end's thread, where the
timeline's withheld stretches begin and end; it reads the block from the tick. `EngineHostWaitTests` is the test: it
fails on any blocking call in `EngineHost*.cs` outside the helper, and it checks that a fade goes on while the host
sleeps, stops under a modal hold, and catches up at the next tick.

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

**As built (milestone 11): the thread question.** The engine thread runs native's main loop, and the front end
drives the frames. This is part 2's design and its fallback together. The engine thread keeps its own loop, so
waits block as native's do. The front end asks for a frame turn each display frame, which keeps presentation on the
display's clock.

- **The engine thread** is the interpreter's task (`godot/Main.cs`), run through `InputAndWaits.RunEngine`. Every
  native is that thread's alone and takes no lock (§5.1, rule 4). The tick runs at each instruction boundary and
  each time a wait wakes (part 1).
- **A frame turn.** Each display frame, the front end's `_Process` asks for a turn (`RunFrameTurn`) and waits for it
  up to 4 ms (`Main.FrameTurnTimeoutMs`). The engine thread answers at its next instruction boundary (`FrameYield`)
  or at a wait, before the wait blocks and after it wakes. In the turn the engine thread:
  - routes the queued input;
  - runs the queued requests;
  - publishes the movie frames that the front end serviced;
  - if it may present, consumes the presentation reasons and lowers the frame description (§6.3).

  The front end draws the description that the turn published. Once a turn has started, the front end waits for it
  to end. A turn that has not started by the deadline stays asked for: the engine thread answers it later, and its
  frame is drawn on the next display frame, marked late in the perf log.
- **Where it may present.** The engine thread presents only where native renders: outside a script's burst
  (`_scriptDepth == 0`) or once the burst is suspended at a wait. A turn at an instruction boundary inside a burst
  ends with nothing to draw. A published frame that the front end has not taken holds the next one back, with its
  reasons still pending, so no publication is lost. This replaces `ScriptPresentationBarrier`, through which the
  render thread read `GfxState` while the VM wrote it.
- **The front end's thread** does what part 2 lists:
  - It queues input events, which the engine thread routes at its next boundary or wait (`.Events.cs`).
  - It asks for the frame turn.
  - It applies the device commands: audio, the window, the cursor, and the movie decoders with their sound, which
    stay on its thread because the stream device touches Godot nodes.
  - It draws the published description. The descriptions are double-buffered (`Graphics.Frame.cs`), and the pixels
    they name are immutable, since a surface's image is replaced, not written. So the engine thread can go on while
    the front end draws.
- **Requests.** What else the front end needs of the engine runs on the engine thread through one queue:
  - the options panel, the menu bar's state and commands (`EngineThreadMenuServices` in `Main.MenuBar.cs`), the
    language switch, the debug frame controls, and the driver's `state`, `rand-seed` and `reference-diff` use
    `Invoke`;
  - the stall diagnostic uses `TryInvoke` with a 500 ms limit, and says whether the engine answered;
  - a stop or a return to title sets `StopAndReload`'s flag and posts `WakeAllWaits` to the engine thread.

  While no engine thread runs (before the interpreter starts and after it returns), a request runs at once on its
  caller's thread. A request made on the engine thread also runs at once.
- **The deadlock rule of §6.2, rule 6, holds.** The front end waits for a turn that has started, but nothing on the
  engine thread waits for the front end. A device call from the engine thread is a command or a query of the
  device's last report. The front end's modal hold is the one handoff the engine thread waits on, and that wait is
  modal.
- **Blocking outside the waits.** The engine thread blocks in one other place: fast-forward pacing
  (`SkipAndAuto.PaceOp`) sleeps a few milliseconds per op or page through `IPaceClock`. The sleep models a cost per op and
  per page (§11, notes on the order), and the next turn is answered when it ends.

**The timing.** The tolerance was written before measuring (ten rules: the same step results, `SYS4REG.INI`, shots,
saves, log lines and reference differences; step seconds within 5 %, 2 % for the reveal clock and the read skip; frame
times; presentation latency; timed waits). Rules 1 to 7 compare 21 port runs with milestone 10's (`c478357`). Rules
8 to 10 compare the build after milestone 11's first two steps (the native, then the one loop; no thread changed)
with the finished build, under the same display.

- **Frame times and presentation latency are within tolerance** (rules 8 and 9: `--perf-log`, optimized, two runs
  each of SC0000's effect band and Kamidori's `FIELD`):
  - drawn frames in the band: 3110 and 3109 before, 3103 and 3103 after for the band; 4609 and 4614 before, 4615 and
    4616 after for `FIELD`;
  - main-thread time per drawn frame, which now includes the wait for the turn: median 0.030 to 0.048 ms and p95
    0.566 to 0.582 ms for the band; median 0.519 to 0.500 ms and p95 0.619 to 0.655 ms for `FIELD`;
  - the p95 of the frame delta is 10 ms on both;
  - the frames from a new presentation coordinate to the first frame drawn at it: mean 0.20 to 0.00 and largest 8 to
    0 for the band; mean 0.64 to 0.05 and largest 11 to 2 for `FIELD`.
- **Timed waits are outside rule 10 as it was written, and closer to native** (`--timeline-log`: the reveal clock,
  the latch sleep, the effect waits and Kamidori's opening). Three behaviours changed, all because a wait no longer
  waits for the front end's thread:
  - **Short sleeps.** A `sleep 1` in the title and new-game poll loops took a median 10 to 16 ms before, a display
    frame. The sleep did end at its deadline, but resuming waited for the barrier while the front end drew. Now it
    takes about 1 ms, as native's `kernel32!Sleep(1)` does (`docs/engine-re.md`, "Short sleeps do not wait for the
    display"). Each loop lasts as long as before, since its end is set by input or a fade, but runs 5 to 13 times
    as many iterations. So the sequence of states differs by those iterations.
  - **Transitions end at their length.** SC0000's four waits at `0x123de` after page 13's click took 154, 897, 164 and
    338 ms before, and 150.7, 890.5, 150.3 and 300.6 ms after. Native's Frida trace measured 150, 890, 150 and 300 ms
    (`docs/engine-re.md`, "Service waits end on the engine clock"). From the click to page 14's show-text took 1582 ms
    before, 1520 ms after, and 1505 ms on native.
  - **A reveal ends with its last glyph** instead of at the next frame. Page 14's reveal, which no click finishes in
    the effect waits, took 231.9 ms before, 156.4 ms after, and 156 ms on native. The median reveal moved 0.3 ms in
    the two runs that click through their reveals and 8 ms in the effect waits.
  - **What follows from them.** The drive scenarios click on a fixed period, so a click now falls at another point in
    the script: a modal movie that a click cancels ends at another time, page 14's reveal is finished by its click
    instead of by a held one, and the new-game loop runs a few iterations fewer. With the poll loops collapsed to one
    entry each and the cancelled movies left out, the three Himegari runs have the same sequence of waits. The 1000 and
    750 ms sleeps are within a millisecond.
- **The 21 port runs are within rules 1 to 7.** Against milestone 10's runs, they show:
  - the same step results, apart from where a poll caught the script;
  - the same `SYS4REG.INI`, shots and log lines;
  - saves equal under `save-diff`, apart from the timestamp and the play time.

  Step seconds are within 0.6 %: the reveal clock went from 93.88 to 93.93 s and the read skip from 37.77 to
  37.75 s. Himegari's `reference-diff` run is the exception, 3.1 % shorter (49.33 to 47.78 s), still within 5 %.
  Its two steps whose numbers move vary as much between two runs of milestone 10's build: page 14 had 1250 and 1425
  pixels over the tolerance there and 1216 here; page 15's mid-effect step had 51631 and 1246 there and 54649 here.
- **So the timing is within the tolerance on rules 1 to 9.** It is outside rule 10 as the rule was written, and in
  each case the new timing is closer to native's.

**The locks and wait handles that remain, one by one.** Milestone 11 removed 273 `lock` statements, which guarded
natives' state against the front end's thread. Graphics, movies, the movie surface registry, text, audio, skip and
auto, script memory, the saves native, the VM's interactive and debug-control state, `GfxState`, `InputBindings` and
`AudioMixerSettings` take no lock now. `ScriptPresentationBarrier` and its tests are deleted. What is left:

| Lock or handle | Where | Guards | Threads |
|---|---|---|---|
| `_turnLock` | `InputAndWaits.Turns.cs` | Which thread is the engine's, the turn's handshake and the diagnostics it carries, the published frame, and the request queue. Nothing runs under it except a turn or a request that the front end runs while no engine thread runs | Both. It is the one lock between them, and no other lock is taken under it |
| `_frameSignal` (`AutoResetEvent`) | `InputAndWaits.cs` | The engine thread's one wake: a turn, input, a request, a serviced movie frame, a stop | Set by both; only the engine thread waits on it |
| `_workQueued` (an int) | `InputAndWaits.Turns.cs` | That input, a request or a serviced movie frame is waiting. Read without a lock first, then taken by `Interlocked.Exchange`, which is a full fence, so a wake cannot be lost | Set by both, taken by the engine |
| `_input` (`ConcurrentQueue`), `_advanceQueued` | `InputAndWaits.Events.cs` | The front end's input events, and the one advance `SignalInput` may queue | Front end enqueues, engine routes |
| `Request.Done` (`ManualResetEventSlim`) | `InputAndWaits.Turns.cs` | A request's completion | Engine sets it, the asker waits |
| `_frontendModalRelease` (`ManualResetEventSlim`) | `InputAndWaits.Sleep.cs` | A front-end modal hold (the options panel, the 終了 dialog, the screenshot folder): while it is reset, the engine thread waits at `FrameYield`, modally, and still answers turns | Front end sets and resets it, engine waits |
| `_gate` (`SemaphoreSlim`), `_inputCallbackSignal` (`AutoResetEvent`) | `InputAndWaits.cs` | A click or Auto ends a text wait; input or a menu request reaches a wait's callbacks. They are handles because a wait blocks on one of them together with `_frameSignal` (`_blockHandles`) | `_gate`: engine only, since input is routed there. `_inputCallbackSignal`: also set by the shell native's menu requests from the front end |
| `StopAndReload`'s two flags (`volatile`) | `StopAndReload.cs` | The stop and the return to title | Front end writes, both read; asking posts the wake |
| The wait flags `_waiting`, `_transitionWaiting`, `_sleeping`, `_pages`, `_modalMovieWaiting` (`volatile`), `_transitionStartedAtMs` (`Interlocked`) | `InputAndWaits*.cs` | What the engine waits for. The front end's self-test and capture clickers (`--selftest`, `--shot`, `--shot-sequence`, `--transition-click-ms`) read them as status | Engine writes, front end reads |
| `ShellDialog.Sync`, `_closed` (`ManualResetEventSlim`), the open-dialog list | `ShellServices.Dialogs.cs` | A dialog's answer and state, its close, and the dialogs a stop cancels | Engine opens and cancels, front end answers |
| The menu queue (`ConcurrentQueue`, `volatile` pending flag) and `_windowed` | `ShellServices.cs`, `.ScreenMode.cs` | Menu toggles and screen-mode switches; the screen mode | Front end writes, engine takes |
| `SystemKeyDisableMask`, `SystemMenuShowDelayMilliseconds` | `ShellServices.cs` | Ops `0x10f` and `0x148`, read by the front end's key and menu-bar handling. Word-sized and written by one thread, so a read is at most a frame late | Engine writes, front end reads |
| `_serviced`, `_opened`, `_requestedRates`, `_requestedStops` (concurrent collections) | `Movies.Playback.cs` | The decoders' handoff: frames and completions the front end serviced, the movies it opened, and the rates and stops the engine asked for | Front end services, engine publishes and asks |
| The audio device's ended queue (`ConcurrentQueue`) | `godot/Main.Audio.cs`, `SilentAudioDevice` | Channels that ended, reported to the engine (§6.2, rule 3) | Device enqueues, engine drains |
| `FrameClock._speedLock` | `Hosting/FrameClock.cs` | The clock's speed scale and the sample it scales | The driver's `speed` sets it; both threads read the clock |
| `TimelineLog`, `PageLocatorState`, `TextMissLog`, `HistogramTraceSink` locks | `Diagnostics/`, `Localization/` | One writer, so events keep their order | Both: the engine records, and the front end records its own events and reads the locator (F2, F3) |
| `Sys4RegIniStore._lock` | `Persistence/` | `SYS4REG.INI`'s values and its write | The options panel writes from the front end, natives from the engine |
| `SharedProfile._catalogUnlockLock`, `ScreenshotBmpStore._lock`, `ResourceMap._diagnosticLock` | `Persistence/`, `Sys4/` | The unlocked catalog entries; the screenshot counter (F1 on the front end); reported diagnostics, once each | Callers on either thread |
| `BoundedLruCache._gate`, `AspectFontRealization._gate`, the glyph rasterizers' `_gate`, `GdiTextMetrics.Faces` | `Text/`, `godot/` | The glyph and metrics caches, and the TextServer rasterizer, which runs on its caller's thread (§6.2, rule 6) | The engine's text and the front end's own text (the shell, the options panel) |
| The decoder's events and locks; `FfmpegMovieNative._decodeLock` | `Ome.Engine.Frontend/FfmpegMovie*.cs` | The decode thread's frames and audio, cancel and first frame | The decoder's own thread and the front end |
| `GodotTraceSink._snapshotLock` and its queue; `DriveServer._calls` | `Ome.Engine.Frontend/` | The trace ring that the stall diagnostic reads; the driver's calls, which the front end takes each frame (`Pump`) | The engine's trace and the front end; the driver's socket and the front end |

The VM keeps a few `volatile` fields and `Interlocked` updates from the old input thread: `_advTextWaitPolling`, the
coroutine-yield flags, the input latch, the wheel delta and the queued callback mask. The skip-and-auto native
keeps its `volatile` toggles, the Auto times and the click-cancel count. The text native keeps its `volatile` settings
and reveal flag. Each of these is written and read on the engine thread now, so they are harmless and order nothing.
Backlog "The host's threads have no stated ownership" is closed by this table.

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

**As built (milestone 11).** One loop: `VirtualMachine.RunInstructions` (`Ome.Engine.Vm/VirtualMachine.cs`) runs a
frame's body and a wait's callback, which ends at the callback's return. Each instruction gets native's per-op
services in one order:

1. the stop and reload requests;
2. the step limit;
3. the boundary's services and requests (`ServiceInstructionBoundary`);
4. the handler;
5. a pending 無視 skip;
6. `FrameYield`, where the engine thread answers a frame turn and takes the front end's messages;
7. fast-forward's pace.

A frame's loop also takes a debug frame return. `RunFrame` and `RunWaitCallback` map `RunInstructions`' end to their
outcome, so a halt, an exit request or a root reload inside a callback ends the run as it would in a frame. The
requests of rule 3 reach the interpreter at the two points the rule names: the boundary, and each time a wait wakes.
In both, `InputAndWaits` first routes the queued input. So a click that arrives during a burst is routed at the next
boundary, against the state the script has reached. Native's main loop peeks its messages after each op in the same
way (`docs/engine-re.md`, "Fast-forward pace"). The front end's requests (§5.3, "As built (milestone 11)") are
not script requests: they read or set a native's state and never run a script. Backlog "Two instruction loops" is
closed.

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

**As built (milestone 4).**

- **The lifted host** is `Ome.Engine.Hosting.EngineHost` (`engine/Ome.Engine/Hosting/EngineHost*.cs`, the six files
  of `godot/GodotAdvHost*.cs` with their threading and locks unchanged). Its snapshot record is
  `EngineHostDiagnosticSnapshot`. The ten lines that printed a message print through the front end.
- **The typed interface** is `Hosting/IFrontEnd.cs`: the 30 members and two for the log (`Print`, `Warn`). `Main`
  implements it explicitly in `godot/Main.FrontEnd.cs`, each command with the `CallDeferred` the host made before.
  The count above was off by two: `ShowAgeDiagnostic` and `ShowAgeScriptError` were deferred too (through
  `Callable.From`, since a `DiagnosticMessage` is not a Variant), so 24 members are deferred commands and 6 run on
  the caller's thread (`QueueVoicePlayback`, `IsVoicePlaybackActive`, `IsSoundChannelPlaying`, `TryPlayMovie`,
  `QueueMoviePlaybackRate`, `RequestScreenModeFromScript`).
- **What else moved with it.** The movie surface registry (`Hosting/MovieSurfaceRegistry.cs`) and the three logs the
  host and the compositors write (`Diagnostics/PageLocatorState.cs`, `Diagnostics/TimelineLog.cs`, formerly
  `GodotTimelineLog`, and `Diagnostics/PerformanceFrameLog.cs`) came from `Ome.Engine.Frontend`. One device
  interface is new: `ILanguageFontRasterizer` (`Ome.Engine.Devices`), the TextServer rasterizer's language-font
  call. The installed-font fallback (Godot's system fonts) is the front end's: without a font source the host lists
  none. The click action that skips an effect (logical action 4), which the host tested through
  `VirtualMachine.AdvanceAction`, is `InputBindings.AdvanceAction`; the VM's constant names it.
- **Tests.** `EngineHostTests` runs the host over a front end that does nothing, with a catalog and no game files:
  its commands reach the front end in the order sent, and a filled surface drawn by an object over a background
  colour composes into the expected frame. `ProjectBoundaryTests.NoGodotTypeImplementsTheHost` reads every base list
  in `godot/` and fails on `IHost`, its interfaces or `ISurfacePixels`.
- **The timing is kept.** `kamidori-opening-pages.steps` and `himegari-read-skip.steps` ran on the build before the
  lift (`12dfda5`) and on the lifted one, with `--timeline-log` and the same temporary probe in both: an event where
  the host defers a call to `Main`, and one where `Main`'s method runs. Both builds made the same calls (649 and 174)
  and applied each in the frame that sent it or the next. On the Kamidori run 515 were applied in the same frame and
  134 in the next before the lift, 522 and 127 after; the median lag was 5.0 ms and 4.9 ms. No call waited more than
  one frame on either build.

**As built (milestone 6).**

- **The audio device** is `Ome.Engine.Devices.IAudioDevice` (`engine/Ome.Engine.Devices/Audio/`). Its commands load
  a clip on a channel and bus, play it (looping or once, at a volume, with a play id), stop it, unload it, and set a
  channel's volume or a bus's volume and mute. Its one report, `TryTakeEnded`, gives a play that ran out. A play with
  no clip, or with one the device cannot decode, ends at once; a stopped play is not reported. `SilentAudioDevice` is
  the headless one.
- **The native holds every answer** (rules 4 and 5). It sets a channel playing when it starts it and clears it when it
  stops it or takes the report, and it ignores a report of an earlier play. No query reaches the device.
- **The Godot device** is `godot/Main.Audio.cs`: one `AudioStreamPlayer` per channel, and the buses. Each command is
  deferred to the main thread in the order sent, as the host's calls were. Once a frame, before it wakes the engine
  thread, it reports the channels whose play ended. It runs no fade tween and no duck. The workaround at `:46-49` (a
  tween orphaned by a track change) has nothing left to guard. `IFrontEnd` lost its 16 audio members: 14 remain, with
  the two for the log.
- **Tests.** `AudioNativeTests` and the converted op tests run the real native over `RecordingAudioDevice`, which
  keeps every command, and a stepped clock; none needs Godot. `ProjectBoundaryTests` checks that `Main.Audio.cs` has
  no tween, fade or duck. The Godot self-test checks the device: a channel loads and plays once and looping, on its
  bus, takes a volume and unloads, and an empty play ends at once.
- **The timing is kept.** The tolerance was written down before measuring: against the build before, the medians of
  the start lags, fade ends and duck times within a frame and their maxima within two, the same counts, the same step
  results, and step seconds within 5 %. `kamidori-opening-pages.steps` and `himegari-read-skip.steps` ran on the build
  before (`4b376f6`) and the new one, with `--timeline-log` and the same temporary probe in both: at the start of each
  frame, an event for each channel whose playing state, volume or stream had changed.
  - Both builds started the same 7 BGM tracks, 79 voices and 27 sound effects. The probe saw 26 on the build before
    only because two starts on one channel fell in one frame there.
  - From the request to the channel playing, the median was 8.1 ms before and 8.4 ms after for Kamidori's 77 voices,
    and 11.7 and 9.6 ms for its BGM.
  - Kamidori's four fades took their first step a median 37 ms after the request before and 28 ms after. They ended
    a median 15.5 and 14.2 ms past their authored length, at most 57.6 and 19.4 ms, each at the same level or with the
    source released.
  - All 17 ducks fell to -10 dB in the frame their voice started. The 15 releases began a median 13.5 and 13.0 ms
    after the voice ended and took 988 and 995 ms. One release began a frame sooner on the new build: when op `0x1bc`
    stops a voice, the native knows at once, where the host waited for Godot's player to stop.
  - The step results were the same and the step seconds equal: 396.5 s for Kamidori, and 38.3 and 38.25 s for the
    read skip.

**As built (milestone 8).**

- **The decoder** is the one movie part a front end supplies: `IMovieDecoderFactory` and `IMovieDecoder`
  (`engine/Ome.Engine.Devices/IMovieDecoder.cs`), unchanged. The FFmpeg decoder stays in `Ome.Engine.Frontend`.
- **The stream device** is new: `Ome.Engine.Devices.IAudioStreamDevice` (`Audio/IAudioStreamDevice.cs`). It opens a
  stream at a sample rate on a bus, silent when the movie's route is off. A stream starts held, resumes, says how many
  frames it has room for, takes PCM or silence, reports how much has been heard and how often it ran dry, and plays at
  a rate. The movie native's feed queues each block at its presentation time and paces the decoder by what has been
  heard, as `godot/MovieAudioOutput.cs` did.
- **An exception to rule 1.** A stream is fed, and its heard position read, on the front end's thread, in the movie
  native's `ServiceFrame`, where `Main.Movie.cs` fed it before; the decoder's frames are taken there too. Milestone 11
  decides the threads. Opening a decoder stays on the engine thread under rule 6.
- **The Godot device** is `godot/GodotAudioStreams.cs`: an `AudioStreamPlayer` over an `AudioStreamGenerator` per
  stream, with a quarter second of buffer, on its bus, at -80 dB when silent. What has been heard is the player's
  position plus the time since the last mix, less the output's latency.
- **Tests.** `MoviesNativeTests` and the converted op tests run the real native over a scripted decoder, movie files
  for every id, a stream device that records what is queued, and a stepped clock (`MovieTestSupport.cs`): start
  delays, the watchdog, completion, stops on a release, a reset or a second movie, the sound's routes, its first block
  and its gaps, rates, the mask transition, and shutdown. None needs Godot.
- **The timing is kept.** The tolerance was written down before measuring: the same movies with the same playback
  ids, surfaces, flags, delays and stop times; each first frame within a frame of before; a completion within two
  frames and 50 ms; a stop within a frame; the modal wait ending the same way. 17 runs with `--timeline-log`: two
  probes that let each game's LOGO play to its end and cancel OP after 5 s, and the 15 port scenarios of milestone 7.
  - **One run is not repeatable to a frame.** On the build before alone, OP's first frame came 4 to 7 frames after its
    start. So, before the new build ran, the timing checks were changed to compare the 17 runs as a set: medians within
    a frame, ranges no wider.
  - **The display changed under the comparison.** This machine's display went from about 58 to 80-97 frames a second
    between the first runs of the build before and the runs of the new one. The build before (`2a45bc2`) was run again
    under the same display, and the figures below compare with that run.
  - The same 35 movies started in the same order, with the same ids, surfaces, flags, delays and stop times, and every
    modal wait ended the same way.
  - The first frame came a median 2 frames after the start for both LOGOs on both builds (about 20 ms). OP's came 7
    and 6 frames after for Himegari (70 and 61 ms), and 7.5 and 7 for Kamidori (75 and 70 ms).
  - The three movies that play to their end completed 2 frames (21 ms), 1 frame (8 ms) and 3 frames (17 ms) later.
    The last, SC0000's `CHAPTER.AGF`, is one frame outside the bound and inside its 50 ms. The build before varies by
    39 ms on it from run to run.
  - Every stop was applied one frame after the op that asked for it, where the build before applied it in the same
    frame. That was the known difference: `Main`'s `CallDeferred` ran in the same frame, and the frame service runs at
    the start of the next.

**As built (milestone 9).**

- **The window** is a device: `Ome.Engine.Devices.IWindowDevice` (`engine/Ome.Engine.Devices/Window/IWindowDevice.cs`),
  with one command, show a window or fullscreen. The shell native decides each switch (`SwitchScreenMode`). A switch
  to the current mode does nothing, and so does one to a window under `display:ForceScreen`; any other stores
  `display:ScreenMode`, tells the window and queues `CALLBACK_WINDOW.BIN`. Op `0x2cf` records a request, which the
  front end's next frame takes (`ServiceFrame`), as before. Godot's window (`godot/Main.ScreenMode.cs`) hides the menu
  strip, remembers the window's rectangle when it goes fullscreen, and lays the screen out. The size and the close
  request stay on `IShellHost`.
- **A dialog is a request.** The shell native opens each one, a `DiagnosticBox` (op `0x1b4`), a `ScriptErrorBox` or
  a `TextEntry` (op `0x144`), and hands it to the front end through `IShellDialogs`. The front end answers it from its
  own thread. The first answer wins, and an answer after a cancel is dropped. A stop or a scene reset cancels every
  open dialog, so a late answer cannot answer the next one. The host's six completion fields, which a stale signal
  could carry over, are gone.
- **`IShellDialogs` is not a device.** It carries AGE's vocabulary (the error box's three choices, the
  full-width text entry), so it is the native's seam in `Ome.Engine` (`Natives/ShellServices.Dialogs.cs`), as §6.4 has
  it for the save store.
- **The waits stay in the host** until milestone 11. It opens a box and waits on its `Closed` handle in a modal wait
  that a stop ends. Godot shows the boxes on its main thread (`godot/Main.FrontEnd.cs`, through `CallDeferred`), as
  before.
- **`IFrontEnd` lost four members**: the three `ShowAge*` and `RequestScreenModeFromScript`. `ILifecycleHost` lost
  `RequestScreenMode`.

**As built (milestone 11).**

- **Rule 1 holds, with milestone 8's exception kept.** The movie stream device and the decoder's frames stay on the
  front end's thread, because Godot's stream device touches Godot nodes. The movie native's `ServiceFrame` runs
  there each display frame, before the frame turn. It queues the frames and completions it serviced, and it wakes
  the engine thread once. The engine thread publishes them at its next turn or wake (`PublishServiced`), so what a
  movie shows is set on the engine thread. The ended-channel reports of the audio device reach the engine the same
  way (rule 3).
- **The dialog waits are the input and waits native's** (`InputAndWaits.Modal.cs`): it opens a box through the shell
  native and waits modally on its `Closed` handle. The front end answers on its own thread.
- **Rule 6 holds.** The front end waits for the engine thread in two places. It waits up to 4 ms for a frame turn,
  and without limit once the turn has started. It also waits for a request to finish. The engine thread does neither
  in a turn nor in a request: it takes no device call that needs the front end's thread. Under a front-end modal
  hold it waits for the front end, but modally, and it still answers turns and runs requests. So the options panel,
  which is open during the hold, can still read and set the natives.

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

**As built (milestone 4).** The software path is `Ome.Engine.Hosting.SoftwareCompositor`
(`Hosting/SoftwareCompositor.cs`). Its `Compose` is what `RecompositeSoftware` did up to the upload: it still reads
`RenderObject`s and the host's surfaces, and draws into its own RGBA buffer. `Main.RecompositeSoftware` calls it and
uploads the buffer (`Image.SetData`, `ImageTexture.Update`). The rules both paths use are shared, not copied: the
blur-range sources (`Hosting/BlurRangeSources.cs`), a blur's sample matrix (`SoftwareCompositor.BlurSampleAffine`)
and the patterned transition's crossfade (`SoftwareCompositor.CrossfadeApproximation`). Which path draws is
unchanged. Milestone 7 makes this class the reference renderer by giving it the frame description to read.

**As built (milestone 7).**

- **The frame description** is `Ome.Engine.Devices.FrameDescription`
  (`engine/Ome.Engine.Devices/Graphics/FrameDescription.cs`), with the renderer `IFrameRenderer` and the presenter
  `IFramePresenter`; `Affine2D` and `BlendKind` moved into `Ome.Engine.Devices` with it. A frame has a background
  colour, `PreserveExistingPixels`, `AppendLayers` (for a retained renderer: the items add to the previous frame's
  layers, as a partial publication does), the items bottom first, and an optional `FrameScreenTransition` (source
  items, target items, overlay colour, alpha 0..256). An item is a textured quad or a fill, with one matrix, an
  opacity, a tint (a multiply, or a LERP of a strength), a blend and a colour key. A texture says what a renderer may
  cache: a static image by its asset id, a dynamic surface by its key, whose pixels change only by replacement.
  Effects are lowered to items: a surface transition draws its target range at the sampled progress (the patterned
  one as the crossfade approximation both paths used), and a blur draws its samples. Only the whole-screen transition
  stays an effect, because its target is flattened before it is blended.
- **One traversal.** `Graphics.LowerFrame` lowers the backbuffer's visible objects, or a screen transition's source
  and target frames, once a frame. The decisions `--gfx-log` and `--timeline-log` print (`GfxDecisionLog`) and the perf
  counters come from it, with the same text as before. A published range (ops `0x222` and `0x20c` into a render
  target, a movie mask's capture) is lowered by the plain rules `Sys4/RetainedSurfaceRasterizer.cs` had, kept as they
  were and not yet checked against native: each object in the range as its texture or its fill, no effects, fills
  sized to the destination, nothing for a source rectangle that starts off its texture. That class is deleted, and
  `ProjectBoundaryTests` checks that only the lowering builds an object's matrix (`Transform2DMath.Build`; `GfxState`
  samples its range transform itself).
- **The reference renderer** is `Ome.Engine.Hosting.ReferenceRenderer`, the software compositor reading a frame
  description; its code defines what each field of an item means. A screen transition draws as native's mode 4: the
  source frame opaque, then the target flattened over a clear of the overlay colour and blended over it at the alpha.
- **The GPU renderer** (`godot/GpuRetainedRenderer.cs`) is an `IFrameRenderer`. It declines a frame with a screen
  transition, and `Main.Recomposite` (`godot/Main.Compositor.cs`) then has the reference renderer draw it and shows
  its pixels (`Main` is the `IFramePresenter`). Every frame of a `--gfx-log` or `--timeline-log` run is the
  reference's, as before. Neither file names `RenderObject` or `GfxState` (`ProjectBoundaryTests`).
- **Changes in behaviour:**
  - The GPU renderer draws the background colour of a frame that starts clean; it drew black. The reference drew it.
  - It draws an additive item with a LERP tint, with an additive copy of its LERP shader; before, any visible object
    with that tint sent the frame to the software path. No frame has one: only static colour mode 1 blends
    additively, and it tints by multiplying (`GfxState.Presentation.cs`), so the case was a guard that never fired.
  - A declined frame is lowered once; the snapshot was taken twice.
  - The perf counters `gpu_blur_scan`, `gpu_transform` and `gpu_object_lookup` time the lowering, for every frame.
- **Which thread renders.** The front end's main thread lowers each frame and draws it, under the presentation
  barrier, as before. The pixels a description refers to never change, since surfaces are replaced (§5.2, "As
  built"). The description itself is reused from frame to frame, which keeps the GPU path's allocations down, so a
  renderer on a thread of its own would need two descriptions handed between the threads. Nothing needs that yet, and
  milestone 11 decides the threads.
- **The tolerance**, written before measuring: every shot drawn by the GPU, unless the frame is one it declines by
  design; frames whose items are all whole-pixel moves within 3 levels per channel; frames with scaled, rotated or
  fractionally placed items with at most 1 % of the frame's pixels more than 3 apart, inside those items, and none
  more than 64 apart; and no missing, extra or misplaced layer on the difference image. The known sources of
  difference are blending arithmetic (the reference truncates integers, the GPU rounds floats), the GPU's own
  bilinear filtering, and the LERP-tint shader.
- **The comparison.** The port's driver step `reference-diff` (`docs/tools-reference.md`) has a second reference
  renderer draw every frame description too, and compares the screen with its drawing of the same frame. Two port
  scenarios run it, `tools/drive/scenarios/port/himegari-reference-diff.steps` and `kamidori-reference-diff.steps`.
  Every shot was drawn by the GPU:

  | Shot | Draws (scaled) | Over 3 | Largest |
  |---|---|---|---|
  | Himegari title, GAMESTART, SC0000 pages 1 and 17, the history | 10 to 103 (0 to 2) | 0 | 3 |
  | Himegari page 14, over the effect band's scaled CG | 40 (2) | 1,334 (0.3 %) | 5 |
  | Himegari page 15, mid-effect | 9 (2) | 49,268 (10.3 %) | 6 |
  | Kamidori title, GAMESTART, SC0000 | 11 to 50 (0) | 0 | 2 |
  | Kamidori's first field: its event, and the idle map | 249 and 278 (208 and 214) | 0 | 3 |

  A pass over SC0000's pages 1 to 25, two or three shots a page, gave the same: of 74 shots, all are within 3 levels
  except four on pages 14 and 15, which differ by up to 6 levels on 0.2 % to 5.9 % of the frame. Two of those and
  page 15 above exceed the 1 % bound; their difference images show fine stripes across one full-screen CG drawn
  scaled under additive light, and nothing else. That is the two renderers' filtering, which
  this milestone did not change: the GPU's pixels on page 14 are the same as before
  (`himegari-effect-waits.steps`). It is accepted here and is a backlog item ("The two renderers filter scaled
  layers differently"), to settle against native's output.
- **Speed.** `--perf-log` captures of the optimized build, two runs of each workload before (`1d819e9`) and after,
  per drawn frame of main-thread time, median, p95 and p99:
  - Kamidori's field at 蒼月の南湖畔 (5,562 frames of about 410 draws, 320 of them scaled): 0.461, 0.479 and
    0.533 ms before, 0.449, 0.470 and 0.516 ms after.
  - SC0000's effect band (3,896 and 3,899 frames): 0.019, 0.549 and 0.651 ms before, 0.019, 0.553 and 0.659 ms
    after.

  The allocations per frame are the same or fewer (448 bytes on the field; at p99 2,424 bytes before and 1,648
  after in the band). The lowering's work (blur scan, matrices, slot lookup) takes 0.07 ms of a field frame.

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

**As built (milestone 9).** The shell's commands (`Ome.Engine.Frontend/Shell/ShellCommands.cs`) run over
`IShellHost`, the shell-services native and `IVmMenuServices`. The ﾒｯｾｰｼﾞ toggles and the screen mode go to the
native, and the hide request to the VM. The options panel commits its message settings to the native too, and reads
and writes `SYS4REG.INI` through the store the save native holds. The driver seeds op `0x60`'s draws on script memory
(`ScriptMemory.SeedDeterministicRandom`) and reads the screen mode from the shell native.

**As built (milestone 10).** The presenter is the text native's (`TextManager.Presenter`). A language switch
(`godot/Main.Language.cs`) sets it, with the native's language fonts and its presented-text switch. The handlers ask
the native to present a run, a message or a length, and never hold the presenter. The options panel's UseAntiFont and
the driver's reveal state are the native's too.

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
- **The census confirms the order of milestones 6 to 11** (plan §3.2) and adds four things to their scope: the
  statement block goes with the tick function in milestone 6; milestone 7's graphics native tells a listener when a
  surface is released, because the text layouts retained on it go too; script memory, which no milestone built, comes
  in milestone 9 before the save native; and the shell's composed reads (`MenuState`, `MessageSettings`) stay at the
  VM in milestone 9.

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
  must switch to the real native over headless devices. The census counts them (plan §3.4): 311 of the 1,350 test
  methods read such state through the VM's public members. 242 of them read script memory's banks, which need no
  device; the other 152 read graphics (68), text (33), input and waits (26), skip and auto (14), shell services (14)
  or saves and settings (5) state.
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
| That native's BGM fade stops while a modal dialog is open | §5.3, §2 decision 10 | Before milestone 6, with a Frida trace of `bgm_fade_tick@0x464960` while the 終了 dialog was open: the fade stops, and the first tick after the dialog makes up the time, so the fade ends when it would have. The modal movie (op `0x20f`) keeps the services running (`docs/engine-re.md`, "A fade under the 終了 dialog stops, then catches up") |
| That an engine-thread loop keeps today's frame timing | §5.3, part 2 | Milestone 11: frame times and presentation latency are within the tolerance set before measuring. The timed waits now end when their time is up, as native's do, where they used to end at the next display frame (§5.3, "As built (milestone 11)") |
| How many tests assert on state that moves from the VM into a native | §12 | Milestone 2: 311 test methods, 152 beyond script memory (plan §3.4) |
| That `DisableTransitiveProjectReferences` keeps device types out of handler compiles | §3.3 | Milestone 3: a compile that is meant to fail |
| That the lift's typed calls keep `CallDeferred`'s timing | §12 | Milestone 4: they do (§6.2, "As built") |
| Whether the 22 operand-access rows need any code; the port may already read those operands correctly | §7.2 | Milestone 0: one does (below) |
| What the 125 revision differences are, and whether they come to about ten rules | §7.2 | Backlog, "Triage the opcode revision diff" |
| That building a frame description for every frame keeps the GPU path's frame times | §6.3 | Milestone 7: it does; `FIELD` and SC0000's effect band are as fast as before (§6.3, "As built") |
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
- "The host's threads have no stated ownership" — typed calls in milestone 4, the locks in milestone 11, which
  closed it (§5.3, "As built (milestone 11)").
- "Two instruction loops" — the typed step result in milestone 1; one loop in milestone 11, which closed it (§5.4).
- "The Godot front end has no unit tests" — milestone 4.
- "The render traversal is written three times" — milestone 7, which closed it.

Not covered:

- "Normal play still uses the software compositor", since milestone 7 "The GPU renderer declines screen transitions".
  Finishing the GPU path is a non-goal. Milestone 7 gave it the reference to match and the step that compares
  (`reference-diff`).
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
- **2026-11-12, milestone 2.** The census of `VirtualMachine`'s 129 fields and `GodotAdvHost`'s 118, measured at
  `18d4d1b`, is kept in the implementation plan (`docs/superpowers/plans/2026-11-12-engine-layering.md`). It confirms
  the order of milestones 6 to 11 with four additions to their scope (§11, notes on the order), adds the tick
  function's statement block to §5.2, and fills in §12's test count (§13).
- **2026-11-12, milestone 4.** The host lifted into `Ome.Engine` on `feature/engine-layering` (§6.2 and §6.3, "As
  built"). `GodotAdvHost` is `Ome.Engine.Hosting.EngineHost`; it reaches Godot only through `IFrontEnd`, which `Main`
  implements with the same `CallDeferred` calls, and the software compositor is `SoftwareCompositor`.
  `EngineHostTests` runs the host over a front end that does nothing and renders a frame, and `ProjectBoundaryTests`
  checks that no `godot/` type implements the host. A probed run of `kamidori-opening-pages` and `himegari-read-skip`
  before and after the lift shows that the typed calls keep `CallDeferred`'s timing (§13). The suite (1860) and
  validation at the core, workspace and runtime levels on both games pass. The runtime level's Godot self-test had
  failed since milestone 0, which gave the self-test's headless VM the profile's build over the catalog's table; it
  now runs the live VM's table. The ten port scenarios pass with equal `SYS4REG.INI`. Their shots are identical except
  for animation phase and the build label, and `himegari-read-skip` keeps its time (37.7 s). The saves of
  `himegari-save-load` and `kamidori-history-after-load` are equal under `save-diff` except for their timestamp and
  play time, and their thumbnails are byte-identical.
- **2026-11-12, milestone 5.** The skip and auto state has one owner, `Ome.Engine.Natives.SkipAndAuto` (§5.2, "As
  built"), and `IPaceClock` is a device (§3.3, "As built"). The VM's constructor takes the native and no clock;
  `FastForwardPaceTests` pace the native, with and without a VM, and `ProjectBoundaryTests` checks that its fields are
  private and that neither the VM nor `EngineHost` keeps a census field of skip and auto. The suite (1862) and
  validation at the core, workspace and runtime levels on both games pass. The ten port scenarios of milestone 4 and
  four more that drive skip and auto (`himegari-menu-bar`, `-hover-after-click`, `-effect-skip-on-click` and
  `-effect-waits`, run before and after from the same tree) give the same step results with equal `SYS4REG.INI`; their
  shots are identical except for animation phase and the build label. `himegari-read-skip` keeps its time (37.7 s),
  and its read skip runs from `SC0000@0x2ec` to `0xa15` in 30 ms (32 ms before). The saves of `himegari-save-load` and
  `kamidori-history-after-load` are equal under `save-diff` except for their timestamp and play time, and their
  thumbnails are byte-identical.
- **2026-11-12, milestone 6.** The audio pilot. The audio state has one owner, `Ome.Engine.Natives.Audio`, the tick
  is `Ome.Engine.Natives.EngineTick`, and the audio device is `IAudioDevice` (§5.2, §5.3 and §6.2, "As built").
  `godot/Main.Audio.cs` runs no fade tween and no duck, and its workaround is gone. Before the work began, a Frida trace
  settled §13's first row: native's fade stops under the 終了 dialog and catches up at the next tick, and the modal
  movie keeps the services running. `AudioNativeTests` test the native without Godot, `EngineHostWaitTests` holds the
  host's waits to §5.3's table, and `ProjectBoundaryTests` checks that the audio and tick state is private and that the
  Godot device runs no envelope. The suite (1887) and validation at the core, workspace and runtime levels on both
  games pass. A probed run of `kamidori-opening-pages` and `himegari-read-skip` before and after keeps the audio timing
  within the tolerance written down before measuring (§6.2). The 14 port scenarios of milestone 5 give the same step
  results with equal `SYS4REG.INI`. Their shots are identical except for animation phase and the build label, and
  `himegari-read-skip` keeps its time (37.7 s). The saves of `himegari-save-load` and `kamidori-history-after-load` are
  equal under `save-diff` except for their timestamp and play time, so the BGM and sound effect records the native now
  writes are unchanged, and their thumbnails are byte-identical.
- **2026-11-12, milestone 7.** Graphics. The surface table, the publication and the screen transition have one
  owner, `Ome.Engine.Natives.Graphics`, which lowers each frame to a frame description that the GPU renderer and the
  reference renderer both read (§5.2 and §6.3, "As built"). The object traversal is written once, and
  `RetainedSurfaceRasterizer` is gone; `ProjectBoundaryTests` checks that the graphics state is private, that only the
  lowering builds an object's matrix, and that the front end's drawing names neither `RenderObject` nor `GfxState`.
  The native tells a listener when a surface is released or replaced (census addition 2). The tolerance for the GPU
  against the reference was written before measuring; the new driver step `reference-diff` met it on every shot
  except frames of SC0000's effect band, where the renderers' filtering of a scaled CG differs by up to 6 levels on up
  to 10 % of the frame, as it did before (backlog). The `FIELD` and effect-band frame times are no worse (§13). The suite (1893) and
  validation at the core, workspace and runtime levels on both games pass. The 15 port scenarios of milestone 6 give
  the same step results with equal `SYS4REG.INI` (`himegari-script-error` run with its mod), and their shots are
  identical except for animation phase. `himegari-read-skip` takes 38.2 s on this build and on `1d819e9` alike, run
  on the same day; the machine had become about 0.5 s slower than milestone 6's runs. The saves of
  `himegari-save-load` and `kamidori-history-after-load` are equal under `save-diff` except for their timestamp and
  play time, and their thumbnails, which the native now captures, are byte-identical.
- **2026-11-13, milestone 8.** Movies. The bindings of movies to surfaces, the decoders, their sound, completion and
  the mask transitions have one owner, `Ome.Engine.Natives.Movies`, and a front end supplies only the decoder and a
  stream for the sound, `IAudioStreamDevice` (§5.2 and §6.2, "As built"). `godot/Main.Movie.cs` is gone, `IFrontEnd`
  lost its 3 movie members, and the host keeps only op `0x20f`'s modal wait until milestone 11. The host's copy of the
  script stack is gone too. `MoviesNativeTests` test the native without Godot, and `ProjectBoundaryTests` checks that
  its state is private and that `godot/` names none of its parts. The suite (1914) and validation at the core,
  workspace and runtime levels on both games pass. The tolerance was written before measuring. One run's movie timing
  proved not repeatable to a frame, so the timing checks compare the 17 runs as a set. The display's refresh rate
  changed between the first runs of the build before and the runs of the new one, so the build before was run again
  under the same display. Against that run the movies keep their timing within the tolerance, with two exceptions
  (§6.2). A stop lands one frame later, as expected. SC0000's `CHAPTER.AGF` completes 3 frames later, one over the
  bound, though its 17 ms is inside the 50 ms one and the build before varies by 39 ms on it. The 17 runs give the same step results, apart from where a
  poll happened to catch the script, which varies as much between two runs of the build before. Their `SYS4REG.INI`
  are equal and their shots identical. The saves of `himegari-save-load` and `kamidori-history-after-load` are equal
  under `save-diff` except for their timestamp and play time, and their thumbnails are byte-identical.
- **2026-11-13, milestone 9.** Saves, settings and shell services. Script memory (`Ome.Engine.Natives.ScriptMemory`),
  the save and settings native (`SavesAndSettings`) and the shell-services native (`ShellServices`) own the census's
  fields for them (§5.2, "As built"). Each native writes and reads its own records of a numbered save, in the order
  the save native decides. The shell-services native owns the menu and callback requests, the system-menu fields, the
  diagnostic text, the dialogs and the screen mode. The window is a device (`IWindowDevice`), and a dialog is a
  request that a stop or a reset cancels, so the host's six dialog fields are gone (§6.2, "As built"). `IShellHost`
  holds only front-end services and `IVmMenuServices` the menu state and the hide request; the shell and the driver
  call the natives (§8, "As built"). `ProjectBoundaryTests` checks the two contracts and the census's fields, and
  `ScriptMemoryTests`, `SavesAndSettingsTests` and `ShellServicesTests` test the natives without Godot. The suite
  (1935) and validation at the core, workspace and runtime levels on both games pass. The tolerance was written before
  measuring. The 20 port scenarios that use the menu bar, the shell, the dialogs, the saves and the settings ran on
  the build before and the new one under the same display. They give the same step results, apart from where a poll
  caught the script; equal `SYS4REG.INI`; identical shots, screenshots, thumbnails and `RT.DAT`; saves equal under
  `save-diff` except for their timestamp and play time; step seconds within 0.4 % (`himegari-read-skip` 37.74 and
  37.77 s); and the same display, persistence, settings and script-error log lines. The tolerance named only
  `wait-state` polls, and one other poll differed: after `himegari-exit-dialog` cancels the 終了 dialog,
  `expect script=TITLE` read the title's idle loop at `0x173` before and `0xe8` after, two offsets that one run of the
  build before shows in turn.
- **2026-11-13, milestone 10.** Text. The text native, `Ome.Engine.Natives.TextManager`, owns 48 of the census's 51
  text rows, with the presenter inside it (§5.2 and §8, "As built"). Run-state `0x20000000` is one flag, the native's,
  set before a run's layout as native's builder sets it; that is the milestone's one change of behaviour. `IAdvHost`
  holds only the reveal's wait, `EngineHost.Surfaces.cs` and the host's other text code are gone, and the native writes
  and reads its own records of a numbered save. The test hosts see the text through `ITextListener`, and the tests that
  faked a layout run the real native over grid glyphs. `ProjectBoundaryTests` checks the census's fields and
  `IAdvHost`. The suite (1936) and validation at the core, workspace and runtime levels on both games pass. The
  tolerance was written before measuring. 21 port runs ran on the build before and on the new one under the same
  display. They cover furigana, the reveal clock, read skip, the input latch, History, saves, the hide control, the 設定
  options, Kamidori's opening (once more in English through a language package, with a page map) and the two
  `reference-diff` scenarios. The runs give the same step results; equal `SYS4REG.INI`; pixel-identical shots; saves
  equal under `save-diff` except for their timestamp and play time; byte-identical page maps; step seconds within 0.5 %
  (`himegari-reveal-clock` 93.79 and 93.88 s, `himegari-read-skip` 37.78 and 37.77 s); and the same log lines. Two
  `reference-diff` steps, on SC0000's effect band (pages 14 and 15), counted other pixels over the tolerance. A second
  run of the new build differed from the first by more than the first did from the build before (page 15: 51,631 and
  1,246 pixels), so those counts follow the band's phase when the frame is caught.
- **2026-11-13, milestone 11.** Input, waits and the engine loop, built on `feature/engine-layering` in three
  steps:
  - the input and waits native, on the old threads;
  - one instruction loop;
  - the engine thread as native's main loop, with the front end asking for one frame turn each display frame (§5.2,
    §5.3 and §5.4, "As built").

  273 `lock` statements and `ScriptPresentationBarrier` are gone. Each remaining lock and wait handle is listed in
  §5.3 with what it guards and which threads touch it. The suite (1941), validation at the core, workspace and runtime
  levels on both games, and the 21 port runs of milestone 10 pass, and so do frame times and presentation latency. The timed waits are outside the tolerance's rule 10 as written: a `sleep 1`
  now takes about 1 ms, and a transition or a reveal ends at its length instead of at the next frame. Native's Frida
  measurements of the same waits agree with the new times. Backlog "Two instruction loops" and "The host's threads
  have no stated ownership" are closed.
