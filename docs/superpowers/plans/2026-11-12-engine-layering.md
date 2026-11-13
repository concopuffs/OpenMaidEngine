# Engine layering — implementation plan

**Date:** 2026-11-12
**Spec:** `docs/superpowers/specs/2026-11-12-engine-layering-design.md` (cited as "spec §n"). The spec stays the
design's home. This plan keeps the census of milestone 2 and what it settles about the later milestones.
**Status:** milestones 0, 1, 3, 4, 5, 6, 7, 8, 9, 10 and 11 are built (spec §15). Milestone 2, the census below, was measured at
`18d4d1b` on `feature/engine-layering`; since milestone 4 the host it names `GodotAdvHost` is `EngineHost`, in
`engine/Ome.Engine/Hosting/`, and its file names below drop the prefix (`AdvText` is `EngineHost.AdvText.cs`). Since
milestone 5 the skip and auto fields of §4 are the private state of `engine/Ome.Engine/Natives/SkipAndAuto*.cs`
(spec §5.2, "As built"). Since milestone 6 the audio fields are the audio native's (`engine/Ome.Engine/Natives/Audio*.cs`)
and the engine tick's are `engine/Ome.Engine/Natives/EngineTick.cs`'s, except the host's
`_presentationWithheldByStatementBlock`, which stays in the host (spec §5.3, "As built"). Since milestone 7 the graphics
fields are the graphics native's (`engine/Ome.Engine/Natives/Graphics*.cs`; spec §5.2, "As built"), except the fields
of the waits that block on them (`_presentationBarrier`, `_presentationRequestConsumed`,
`_synchronizeExplicitPresentation`, the two request generations, `_lastSynchronizedPresentationTimestamp`,
`_foregroundGfx`, `_foregroundTransitionWaitBypassed` and `_transitionStartedAtMs`), which stay in the host until
milestone 11, and the VM's `_tickPresentationEnabled`, which only its two ops read. Since milestone 8 the movie fields
(`_movieSurfaces`, `_movieMaskLock` and the two mask maps) are the movie native's (`engine/Ome.Engine/Natives/Movies*.cs`;
spec §5.2, "As built"), except op `0x20f`'s three modal wait fields, which stay in the host until milestone 11; the
host's copy of the script stack (`_scriptContexts`, `_rootScene`) is gone, since the VM names the script with each
movie op. Since milestone 9 the fields of §4's script memory, saves and settings, and shell services are the private
state of `engine/Ome.Engine/Natives/ScriptMemory*.cs`, `SavesAndSettings*.cs` and `ShellServices*.cs` (spec §5.2,
"As built"), except the five banks, which the VM keeps as properties that return the native's, and `ScriptError`,
which stays on the VM as the run's outcome; the host's six dialog fields became the shell native's dialog requests.
Since milestone 10 the text fields of §4 are the private state of `engine/Ome.Engine/Natives/TextManager*.cs` (spec
§5.2, "As built"), except the VM's `_presentedMessage`, and `Emitted` and `TextHistory`, which the VM keeps as
properties that return the native's; the host's `_advText` and `_liveText` were dead and are gone, and its
`IsTextRevealing` and the VM's `_advTextRevealing` are one flag, the native's. Since milestone 11 the host's input and
waits fields of §4, with the fields milestones 7 and 8 left there for the waits, are the private state of
`engine/Ome.Engine/Natives/InputAndWaits*.cs` and `StopAndReload.cs` (spec §5.2, "As built"). The VM's 24 input rows
stay at the VM as the interpreter's, which the native reaches through `IInterpreterInput`, and `_interactiveLock` is
gone with the input thread (spec §5.3). Next: milestone 12.

Paths are relative to `age-reimpl/`.

## 1. How the census was taken

- **Fields.** Every instance field and auto-property declared in `VirtualMachine`
  (`engine/Ome.Engine.Vm/VirtualMachine*.cs`) and in `GodotAdvHost` (`godot/GodotAdvHost*.cs`). Constants, statics,
  computed properties and the members of nested types are not counted. The VM has 129 (115 fields and 14
  auto-properties; the spec said "about 120") and the host 118 (112 and 6).
- **Owner.** Assigned by hand from each field's uses and comments. The owners are spec §5.2's natives and four more:
  *Interpreter* (stays in `Ome.Engine.Vm`), *Engine tick* (the tick function of spec §5.3, part 1; see §3.2), *Wiring*
  (dependencies and options given at construction, which become each native's constructor) and *Front end* (stays in
  `godot/` or in the tool that reads it).
- **Lifetime.** *scene*: the root-reload reset (`ResetSceneContextForRootReload` in the VM, `ResetSceneContext` in the
  host, and the methods they call) assigns or clears it. *reset in part*: the reset only calls a method on it.
  *construction*: set once and never replaced (read-only fields, locks, wait handles and dependencies; *drained at a
  reset* when the reset empties a wait handle). *run*: everything else.
- **Saved.** *numbered save*: written into a numbered save (its payload, history tail or playtime). *load only*: used
  to restore a save, never written. *shared profile* and *SYS4REG.INI*: kept in those files.
- **Read / written in.** References per file, as reads/writes. A call that changes its target (`Add`, `Clear`, `Set…`)
  counts as a write.
- **Tests.** Test methods (`[Fact]` or `[Theory]`) that read the field through a VM property or query method on a
  VM-typed receiver (`vm`, `Vm`, `run.Vm`, …), in their own body or through a helper of their class. The host has no
  unit tests; *self-test* marks a field that `godot/Main.SelfTest.cs` reaches through a host member.

All counts are grep counts. A test that holds its VM under another name is missed, so the test counts are lower
bounds.

## 2. Owners at a glance

| Owner | VM | Host | Saved | Scene | Locks and wait handles | Built in |
|---|---|---|---|---|---|---|
| Script memory | 12 | 0 | 5 | 6 | 1 | milestone 9 (§3.2, addition 3) |
| Skip and auto | 19 | 6 | 4 | 12 | 2 | milestone 5 |
| Engine tick | 2 | 2 | 0 | 3 | 0 | milestone 6 (§3.2, addition 1) |
| Audio | 5 | 6 | 3 | 4 | 1 | milestone 6 |
| Graphics | 2 | 26 | 1 | 7 | 4 | milestone 7 |
| Movies | 0 | 7 | 0 | 3 | 1 | milestone 8 |
| Saves and settings | 10 | 1 | 8 | 3 | 1 | milestone 9 |
| Shell services | 9 | 6 | 0 | 7 | 4 | milestone 9 |
| Text | 10 | 41 | 7 | 18 | 1 | milestone 10 |
| Input and waits | 24 | 11 | 0 | 17 | 5 | milestone 11 |
| Interpreter | 32 | 3 | 3 | 16 | 2 | stays |
| Wiring | 4 | 3 | 0 | 0 | 0 | each native's constructor (spec §5.1, rule 3) |
| Front end | 0 | 6 | 0 | 0 | 0 | stays |
| **Total** | **129** | **118** | **31** | **96** | **22** | |

Lifetimes over both classes: 96 scene, 91 run, 49 construction, 6 construction and drained at a reset, 5 run and
reset in part.

## 3. What the census settles

### 3.1 How the order was read

An opcode handler may call several natives, so a handler that reads two owners' state constrains nothing. What
constrains the order is service code that will move into a native (a host method, or a VM method that is not an `Op…`
handler) and reads another owner's state: the reader's native needs the other one first, or a call across to it. The
census found 64 such methods; the ones that decide the order are named below.

### 3.2 The order of milestones 6 to 11: confirmed, with four additions

The order stands: 5 skip and auto, 6 audio, 7 graphics, 8 movies, 9 saves, settings and shell services, 10 text,
11 input, waits and the engine loop.

- **Skip and auto first (milestone 5).** Its state is read by the audio code (`PlayVoice`, `StopVoice`), graphics
  (`RequestSynchronizedPresentation`, `WaitForForegroundTransition`), the waits (`Sleep`, the VM's
  `WaitAtAdvInputPoll` and `WaitInHiddenWindow`), text (`ShowText`) and the shell (`MenuState`). This confirms spec
  §5.2. One thing goes the other way: when skip starts, the host's `SetMessageSkipChannel` completes the reveal
  (`_advTextForceComplete`) and wakes the waits, and when it ends, it plays the voice that skip held back
  (`_queuedSkippedVoice`). Milestone 5's owner therefore tells the lifted host when skip changes, and the host keeps
  those effects until their natives exist.
- **Addition 1: the statement block goes with the tick (milestone 6).** Ops `0x1f4`/`0x1f5` keep the open block twice:
  the VM's `_statementBlockActive` and `_statementBlockDepth`, and the host's `_statementBlockActive` with
  `_presentationWithheldByStatementBlock`. While a block is open, the delayed voice start waits (`PulseFrame`),
  presentation is withheld (`ConsumePresentationReasons`, `Sleep`), and callback scripts are deferred at both of the
  VM's service points (`ServiceInstructionBoundary`, `ServiceWaitSettingCallback`). Audio needs it in milestone 6, and
  every reader is a step of native's tick, so it belongs to the tick function that milestone 6 builds. It is the
  census's one owner that spec §5.2 does not list.
- **Audio (milestone 6) needs nothing later.** Besides skip and auto and the statement block it reads only the voice
  duck settings, from `SYS4REG.INI` (`VoiceBgmDuckSettings` reads `_messageSettingsStore`). The settings store is
  already in `Ome.Engine/Persistence/`, so the audio native takes it in its constructor; milestone 9 need not come
  first.
- **Graphics before movies and text (milestones 7, 8, 10).** Text draws into graphics' surfaces (`DrawStringPixels`,
  `PrepareRetainedTextRun`, `RedrawRetainedTextGroup` write `_surfaceImages` and `_slotDims`), and so do movies
  (`StartMovie`, `PublishMaskedCapture`). **Addition 2: the reverse reads.** Releasing or replacing a surface
  (`ReleaseSurface`, `ReleaseSurfaceRange`, `ReplaceSurfacePixels`) drops the text layouts retained on it
  (`_retainedTextLayouts`, `_retainedHistoryLayouts`) under `_textLock`. Milestone 7's graphics native tells a
  listener that a surface was released or replaced, and the lifted host's text part listens until milestone 10.
- **Saves and settings before text (milestones 9, 10).** Text reads `SYS4REG.INI` through `_messageSettingsStore`
  (`UseMessageSettingsStore`, `FlushMessageSettings`) and the shared profile. The other way, a save writes two text
  records: the history tail and the source glyph objects (`SourceTextObjects`). The save native reads them through an
  interface, as it reads `_host.SourceTextObjects` today.
- **Addition 3: script memory is built in milestone 9.** Spec §5.2 lists a script memory native, and no milestone
  builds it. Its five banks are five sections of a numbered save (`CaptureNumberedState`, `ApplyNumberedState`), and
  spec §5.1 rule 6 has each native write its own records, so it comes in milestone 9, before the save native. It has
  no devices. Op `0x60`'s deterministic draws (`_deterministicRandom…`) go with it.
- **Addition 4: the shell's composed reads stay compositions (milestone 9).** `MenuState` reads five owners (skip and
  auto, input and waits, saves, the interpreter, the shell) and `MessageSettings` reads text and skip and auto. They
  stay at the VM's `IVmMenuServices`, which asks each owner, and the shell native owns only its own fields: the
  system-menu state and key mask, the menu toggle queue, the callback requests, and the diagnostic and error dialogs.
  That fits milestone 9's done-when.
- **Input and waits last (milestone 11).** Every native's service code wakes or reads the waits: `_frameSignal` is set
  from text, movies and graphics, and `IsWaiting` is read by the wait indicator, the page layout and movies. These are
  the thread model's handles, which milestone 11 replaces.

### 3.3 State held twice

The VM and the host each keep these facts, and one pushes its copy to the other. Each pair becomes one field in the
milestone that builds its owner.

| Fact | VM | Host | Milestone |
|---|---|---|---|
| Message skip is active | `_messageSkipServiceActive` | `_scriptMessageSkipActive`, `_messageSkipActive` | 5 |
| Auto message times | `_autoMessageTime0Ms`, `_autoMessageTime1Ms` | `_autoMessageTime0Milliseconds`, `_autoMessageTime1Milliseconds` | 5 |
| A statement block is open | `_statementBlockActive` | `_statementBlockActive` | 6 |
| The op `0xc2` BGM fade | `_bgmFadeTarget` (its target) | `_bgmFadeDeadlineMs` (its end) | 6 |
| The script stack | `_cur`, `_activeFrameNames` | `_scriptContexts`, `_rootScene` | 8 (movies resolve against its top) |
| A reveal is running | `_advTextRevealing` | `IsTextRevealing` | 10 |
| Message speed and window alpha | `_messageGlyphDelayMilliseconds`, `_messageWindowAlphaSetting` | the same names | 10 |
| The shown text, for tools | `Emitted` | `Captured` | 10 |
| Stop and reload requests | `_stopRequested`, `_rootReloadRequested` | `_stopRequested`, `_rootReloadInterrupt` | 11 |

### 3.4 Tests (spec §12)

At `18d4d1b` the suite has 1,350 test methods (1,855 test cases, counting theory rows); 432 of them, in 85 classes,
build a VM.

- **311 test methods read VM state that moves into a native**, through the VM's public members.
- **242 of them read script memory** (`Globals` in 234, `GlobalStrings` in 22). Script memory has no devices, so these
  tests keep reading through the VM's public face (spec §3.4) with the real native behind it.
- **152 read another native's state** and need that native over headless devices, as spec §12 says. A test can read
  more than one:

| Native | Test methods | Milestone | Most in |
|---|---|---|---|
| Skip and auto | 14 | 5 | `HotspotInputTests` 8, `MessageSkipClickCancelTests` 5, `PhysicalFastForwardTests` 1 |
| Audio | 0 | 6 | audio is asserted through the host fakes' recorded calls |
| Graphics | 68 | 7 | `GfxAnimationTests` 9, `MovieOpcodeTests` 8, `AnimChannelTests` 7, `NumberedSaveVmTests` 6 |
| Movies | 0 | 8 | as audio |
| Saves and settings | 5 | 9 | `SharedProfileLifecycleTests` 5 |
| Shell services | 14 | 9 | `SystemMenuGuardOpcodeTests` 3, `SystemMenuShowDelayOpcodeTests` 3, `CallbackOwnershipTests` 2 |
| Script memory | 242 | 9 | `HotspotInputTests` 31, `MenuServicesTests` 11, `AnimChannelTests` 9, `GfxAnimationTests` 9 |
| Text | 33 | 10 | `PresentedHistoryTests` 8, `AdvTextOpsTests` 7, `AdvTextHistoryTests` 4, `HistoryInteractionOpsTests` 4 |
| Input and waits | 26 | 11 | `InputLatchTests` 12, `HistoryInteractionOpsTests` 4, `HotspotInputTests` 4 |

Graphics' 68 read `vm.Gfx`, which is a `GfxState`: that class is already the native's state and needs no device, so
those tests change least. Seven test methods, all in `FastForwardPaceTests`, hand the VM a pace clock; milestone 5
moves them to the new owner, as its done-when says. Tests that assert on a host fake's recorded calls keep their
fakes,
which become fake natives (spec §3.4).

## 4. The census

One table per owner, in the order of §2. *Tests* counts the test methods of §3.4 that read the field.

### Script memory (12 in the VM, 0 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| VM `_deterministicRandomLock` | construction | — | DeterministicRandom 2/0 | 1 | op 0x60; the driver's `rand-seed` |
| VM `_deterministicRandomSeed` | run | — | DeterministicRandom 2/1 | 1 | op 0x60; the driver's `rand-seed` |
| VM `_deterministicRandomDraws` | run | — | DeterministicRandom 1/2 | 1 | op 0x60; the driver's `rand-seed` |
| VM `_deterministicRandomSites` | run | — | DeterministicRandom 2/2 | 1 | op 0x60; the driver's `rand-seed` |
| VM `_nativeStringEncoding` | construction | — | VirtualMachine 1/1, Values 2/0 | 0 |  |
| VM `_intQueues` | run | — | MemoryCollections 5/1 | 0 |  |
| VM `_intStacks` | scene | — | MemoryCollections 5/1, VirtualMachine 1/1 | 0 |  |
| VM `Globals` | scene | numbered save | VirtualMachine 9/8 | 234 |  |
| VM `GlobalFloats` | scene | numbered save | VirtualMachine 4/3 | 3 |  |
| VM `GlobalStrings` | scene | numbered save | VirtualMachine 5/4, TextStores 3/0 | 22 |  |
| VM `GlobalPointers` | scene | numbered save | VirtualMachine 3/2 | 2 |  |
| VM `GlobalStringPointers` | scene | numbered save | VirtualMachine 3/2 | 1 |  |

### Skip and auto (19 in the VM, 6 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| VM `_paceClock` | construction | — | FastForwardPace 2/0, VirtualMachine 0/1 | 0 | device (`IPaceClock`, milestone 5) |
| VM `_fastForwardPacing` | run | — | FastForwardPace 1/2 | 0 |  |
| VM `_fastForwardOwedMs` | run | — | FastForwardPace 1/3 | 0 |  |
| VM `_fastForwardStartMs` | run | — | FastForwardPace 1/2 | 0 |  |
| VM `_fastForwardPagePassed` | run | — | FastForwardPace 1/1, Input 0/1 | 0 |  |
| VM `_advSkipServiceEnabled` | scene | — | MenuServices 3/0, AdvServices 0/2, VirtualMachine 0/1 | 0 |  |
| VM `_physicalFastForwardLock` | construction | — | PhysicalFastForward 5/0, AdvServices 1/0 | 0 | lock |
| VM `_physicalSkipActionHeld` | run | — | PhysicalFastForward 1/1 | 0 |  |
| VM `_revealPhysicalSkipSuppressed` | run | — | PhysicalFastForward 2/2, AdvServices 0/1 | 0 |  |
| VM `_skipClickCancelState` | scene | — | SkipClickCancel 2/3, VirtualMachine 0/1, AdvServices 0/1 | 0 |  |
| VM `_skipClickCancelTriggers` | scene | — | SkipClickCancel 0/3, VirtualMachine 0/1, AdvServices 0/1 | 0 |  |
| VM `_autoMessageEnabled` | scene | — | MenuServices 2/3, VirtualMachine 1/1, AdvServices 1/1, Input 1/0 | 4 |  |
| VM `_autoMessageTime0Ms` | run | SYS4REG.INI | MenuServices 1/1, VirtualMachine 0/1, AdvServices 1/0, Input 1/0 | 2 | copy of the host's, which persists |
| VM `_autoMessageTime1Ms` | run | SYS4REG.INI | MenuServices 1/1, VirtualMachine 0/1, AdvServices 1/0, Input 1/0 | 2 | copy of the host's, which persists |
| VM `_autoVoicePending` | scene | — | Audio 0/2, VirtualMachine 0/1, Input 1/0, AdvServices 0/1 | 0 | set when a voice starts |
| VM `_messageSkipEnabled` | scene | — | MenuServices 5/3, AdvServices 5/1, VirtualMachine 2/1, SkipClickCancel 0/1 | 14 |  |
| VM `_messageSkipServiceActive` | scene | — | Presentation 6/0, AdvServices 2/2, VirtualMachine 0/2, SkipClickCancel 1/1, Input 1/0, Timing 1/0, Audio 1/0 | 0 |  |
| VM `_messageSkipInjected` | scene | — | AdvServices 1/4, VirtualMachine 1/1, MenuServices 0/2, SkipClickCancel 0/1, ControlFlow 1/0 | 0 |  |
| VM `_advReadSkipState` | scene | — | VirtualMachine 1/4, AdvServices 3/0, SkipClickCancel 0/1, MenuServices 0/1 | 0 |  |
| Host `_autoMessageTime0Milliseconds` | run | SYS4REG.INI | AdvText 0/2 | 0 | the VM keeps a copy |
| Host `_autoMessageTime1Milliseconds` | run | SYS4REG.INI | AdvText 0/2 | 0 | the VM keeps a copy |
| Host `_messageSkipLock` | construction | — | Audio 2/0, PresentationInput 2/0 | 0 | lock |
| Host `_scriptMessageSkipActive` | scene | — | PresentationInput 1/2 | 0 |  |
| Host `_physicalMessageSkipActive` | scene | — | PresentationInput 1/2 | 0 |  |
| Host `_messageSkipActive` | scene | — | PresentationInput 6/2, AdvText 2/0, Audio 1/0, Movies 1/0 | 0 |  |

### Engine tick (2 in the VM, 2 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| VM `_statementBlockActive` | scene | — | ScriptLifecycle 2/2, VirtualMachine 0/1, SkipClickCancel 1/0, MenuServices 1/0 | 0 | also defers callback scripts; copy in the host |
| VM `_statementBlockDepth` | scene | — | ScriptLifecycle 1/2, VirtualMachine 0/1 | 0 |  |
| Host `_statementBlockActive` | scene | — | Audio 1/1, PresentationInput 0/1 | 0 | delays starts and the render wait; the VM keeps a copy |
| Host `_presentationWithheldByStatementBlock` | run | — | PresentationInput 1/1 | 0 |  |

### Audio (5 in the VM, 6 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| VM `_audioMixerSettings` | construction | SYS4REG.INI | Audio 4/0, VirtualMachine 0/1 | 0 | shared with `Main`, which persists it |
| VM `_currentBgmTrackId` | run | numbered save | Audio 5/4, VirtualMachine 3/1 | 0 |  |
| VM `_bgmFadeTarget` | run | — | Audio 1/2 | 0 |  |
| VM `_loadedSoundEffectResourceIds` | run | numbered save | Audio 2/2, VirtualMachine 2/1 | 0 |  |
| VM `_voiceMuteNextFlags` | scene | — | Audio 4/4, VirtualMachine 0/1, AdvServices 0/1 | 0 |  |
| Host `_sfxNames` | run | — | Audio 9/2 | 0 |  |
| Host `_voiceBgmDuckControl` | scene | — | Audio 2/2, PresentationInput 0/1 | 0 |  |
| Host `_queuedSkippedVoice` | scene | — | Audio 1/2, PresentationInput 1/2 | 0 |  |
| Host `_scheduledVoiceLock` | construction | — | PresentationInput 2/0, Audio 1/0 | 0 | lock |
| Host `_scheduledVoice` | scene | — | PresentationInput 1/3, Audio 0/1 | 0 | a delayed start |
| Host `_bgmFadeDeadlineMs` | run | — | Audio 1/2 | 0 |  |

### Graphics (2 in the VM, 26 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| VM `_tickPresentationEnabled` | run | — | Presentation 1/1 | 0 | stored and read back only (ops 0x21b/0x247) |
| VM `Gfx` | run, reset in part | numbered save | RetainedObjects 10/12, Animation 1/16, Presentation 13/3, Surface 5/10, AdvText 6/3, VirtualMachine 2/6, Movie 3/2, TextHistory 3/0, ControlFlow 2/0 | 68 | `GfxState`, already a native's state (§3.4) |
| Host `_presentationBarrier` | run | — | PresentationInput 3/3 | 0 |  |
| Host `_presentationRequestConsumed` | construction | — | PresentationInput 0/2 | 0 |  |
| Host `_synchronizeExplicitPresentation` | construction | — | GodotAdvHost 0/1, PresentationInput 1/0 | 0 |  |
| Host `_explicitPresentationRequestGeneration` | run | — | PresentationInput 0/2 | 0 |  |
| Host `_consumedPresentationRequestGeneration` | run | — | PresentationInput 0/2 | 0 |  |
| Host `_lastSynchronizedPresentationTimestamp` | run | — | PresentationInput 1/1 | 0 |  |
| Host `_foregroundGfx` | scene | — | PresentationInput 3/4 | 0 |  |
| Host `_foregroundTransitionWaitBypassed` | run | — | PresentationInput 1/3 | 0 |  |
| Host `_screenTransitionLock` | construction | — | PresentationInput 15/0, Surfaces 3/0, Movies 1/0 | self-test | lock |
| Host `_renderTargetSnapshots` | run | — | PresentationInput 5/1, Surfaces 0/3 | self-test |  |
| Host `_backbufferRangeLock` | construction | — | PresentationInput 4/0, Surfaces 1/0 | self-test | lock |
| Host `_backbufferRange` | scene | — | PresentationInput 1/3 | self-test |  |
| Host `_backbufferPublicationPreservesExistingPixels` | scene | — | PresentationInput 1/3 | self-test |  |
| Host `_backbufferClearPending` | scene | — | PresentationInput 1/2, Surfaces 0/1 | self-test |  |
| Host `_screenTransition` | scene | — | PresentationInput 18/3, Movies 1/0 | 0 |  |
| Host `_presentRequested` | scene | — | AdvText 0/10, PresentationInput 1/7, Movies 0/2, Surfaces 0/2 | self-test |  |
| Host `_terminalSample` | run | — | PresentationInput 1/1 | 0 |  |
| Host `_transitionStartedAtMs` | scene | — | PresentationInput 0/6 | 0 |  |
| Host `_imageLock` | construction | — | Surfaces 17/0, AdvText 5/0, Movies 3/0 | self-test | lock |
| Host `_images` | run | — | Surfaces 1/3 | 0 |  |
| Host `_surfaceImages` | run | — | Surfaces 6/10, AdvText 0/5, Movies 0/2 | self-test |  |
| Host `_surfaceColorKeys` | run | — | Surfaces 1/5, Movies 0/1 | self-test |  |
| Host `_surfaceResources` | run | — | Surfaces 2/5 | self-test |  |
| Host `_slotDims` | run | — | Surfaces 5/5, AdvText 4/2, Movies 1/3, GodotAdvHost 0/1 | self-test |  |
| Host `_screenWidth` | construction | — | GodotAdvHost 2/1, Movies 2/0, PresentationInput 1/0 | self-test |  |
| Host `_screenHeight` | construction | — | GodotAdvHost 2/1, Movies 2/0, PresentationInput 1/0 | self-test |  |

### Movies (0 in the VM, 7 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| Host `_movieSurfaces` | run, reset in part | — | Movies 10/2, Surfaces 4/2 | self-test |  |
| Host `_movieMaskLock` | construction | — | Movies 4/0, Surfaces 1/0 | 0 | lock |
| Host `_movieMasksByPlayback` | run, reset in part | — | Movies 1/2 | 0 |  |
| Host `_movieMaskPlaybackBySurface` | run, reset in part | — | Movies 1/2, Surfaces 1/0 | 0 |  |
| Host `_modalMovieWaiting` | scene | — | PresentationInput 2/1, Movies 1/2 | 0 |  |
| Host `_modalMovieCancelled` | scene | — | Movies 4/2, PresentationInput 0/2 | 0 |  |
| Host `_modalMovieCancelPending` | scene | — | PresentationInput 0/2, Movies 1/1 | 0 |  |

### Saves and settings (10 in the VM, 1 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| VM `_sharedProfile` | run | shared profile | Persistence 2/4, VirtualMachine 4/1, MenuServices 4/0, AdvServices 2/0, AdvText 1/0, Input 1/0, SkipClickCancel 1/0 | 7 |  |
| VM `_nativeDatStore` | construction | — | Persistence 9/0, VirtualMachine 6/2 | 5 |  |
| VM `_sharedProfileShutdownLock` | construction | — | VirtualMachine 1/0 | 5 | lock |
| VM `_loadedNumberedState` | run | load only | VirtualMachine 3/4, Persistence 4/1 | 0 |  |
| VM `_retainedNativeNumberedState` | run | numbered save | VirtualMachine 1/1 | 0 | the records the port does not model, carried through |
| VM `_playClock` | construction | numbered save | VirtualMachine 2/1 | 0 | playtime |
| VM `_playBaseSeconds` | scene | numbered save | VirtualMachine 1/2 | 5 | playtime |
| VM `_playStartSeconds` | scene | numbered save | VirtualMachine 1/2 | 5 | playtime |
| VM `_pendingPlaySeconds` | scene | load only | VirtualMachine 1/3 | 0 | playtime |
| VM `_sharedProfileShutdownHandled` | run | — | VirtualMachine 1/1 | 5 |  |
| Host `_messageSettingsStore` | run | SYS4REG.INI | AdvText 6/1, Audio 1/0 | 0 | also read for the voice duck settings |

### Shell services (9 in the VM, 6 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| VM `_menuToggles` | scene | — | MenuServices 0/2, VirtualMachine 0/1 | 0 | queued from the front-end thread |
| VM `_menuTogglesPending` | scene | — | MenuServices 1/2, VirtualMachine 0/1 | 0 |  |
| VM `_windowCallbackPending` | scene | — | ScreenMode 1/1, MenuServices 0/1, VirtualMachine 0/1 | 0 | a request to the interpreter (§5.4) |
| VM `_settingCallbackPending` | scene | — | ScreenMode 1/1, VirtualMachine 0/1, SkipClickCancel 0/1, MenuServices 0/1 | 0 | a request to the interpreter (§5.4) |
| VM `_diagnosticOutput` | run | — | VirtualMachine 2/1, Diagnostics 0/3 | 1 |  |
| VM `_systemMenuActionsEnabled` | scene | — | VirtualMachine 1/1, MenuServices 1/0, RuntimeSettings 0/1 | 5 |  |
| VM `_systemMenuShowDelayMilliseconds` | scene | — | VirtualMachine 1/1, RuntimeSettings 1/1 | 3 |  |
| VM `_systemKeyDisableMask` | scene | — | VirtualMachine 1/1, RuntimeSettings 0/1 | 1 |  |
| VM `ScriptError` | run | — | VirtualMachine 0/1 | 4 | the error box's message |
| Host `_diagnosticMessageCompleted` | construction, drained at a reset | — | PresentationInput 0/5 | 0 | a modal dialog |
| Host `_scriptErrorCompleted` | construction, drained at a reset | — | PresentationInput 0/5 | 0 | a modal dialog |
| Host `_scriptErrorChoice` | run | — | PresentationInput 1/1 | 0 | a modal dialog |
| Host `_fullwidthTextEditCompleted` | construction, drained at a reset | — | PresentationInput 0/5 | 0 | a modal dialog |
| Host `_fullwidthTextEditLock` | construction | — | PresentationInput 3/0 | 0 | lock; a modal dialog |
| Host `_fullwidthTextEditResult` | run | — | PresentationInput 1/2 | 0 | a modal dialog |

### Text (10 in the VM, 41 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| VM `_presenter` | run | — | AdvText 8/1, HistoryPresentation 1/0, TextStores 1/0 | 1 | the localization presenter; set on a language switch |
| VM `_presentedMessage` | run | — | AdvText 4/4 | 0 | the message whose runs are being shown |
| VM `_advTextRevealing` | run | — | AdvText 0/2, PhysicalFastForward 2/0 | 0 | copy of the host's `IsTextRevealing` |
| VM `_stringStores` | scene | — | TextStores 1/4 | 0 | recorded on every string-bank write |
| VM `_recordsStringStores` | run | — | TextStores 3/1 | 0 |  |
| VM `_advTextStyle` | scene | — | AdvText 17/10, VirtualMachine 2/1, TextHistory 3/0, Audio 2/0 | 0 |  |
| VM `_messageWindowAlphaSetting` | run | SYS4REG.INI | MenuServices 2/1, RuntimeSettings 2/1, VirtualMachine 0/1 | 2 | copy of the host's, which persists |
| VM `_messageGlyphDelayMilliseconds` | run | SYS4REG.INI | AdvText 7/1, MenuServices 2/1, VirtualMachine 0/1, InputLatch 1/0 | 2 | copy of the host's, which persists |
| VM `Emitted` | run | — | AdvText 0/1 | 6 | the shown text, for `GameSession` |
| VM `TextHistory` | run, reset in part | numbered save | AdvText 12/15, TextHistory 8/3, VirtualMachine 1/4, HistoryPresentation 3/0, Audio 0/2, RetainedObjects 1/0, Persistence 1/0 | 27 | the history tail |
| Host `_textLock` | construction | — | AdvText 21/0, PresentationInput 12/0, Surfaces 7/0 | self-test | lock; also taken by the surface and presentation code |
| Host `_surfaceTextMaskCache` | run | — | AdvText 4/1, GodotAdvHost 1/1 | self-test |  |
| Host `_surfaceTextBackendInfo` | construction | — | GodotAdvHost 2/1, AdvText 2/0 | self-test |  |
| Host `_sourceText` | construction | — | AdvText 6/0, GodotAdvHost 1/1 | 0 |  |
| Host `_presentedText` | construction | — | AdvText 6/0, GodotAdvHost 0/1 | 0 |  |
| Host `_presentsText` | run | — | AdvText 3/1 | 0 |  |
| Host `_surfaceTextRasterizer` | construction | — | AdvText 1/0, GodotAdvHost 0/1 | 0 |  |
| Host `_presentedTextRasterizer` | run | — | AdvText 1/0, GodotAdvHost 0/1 | 0 |  |
| Host `_presentedFonts` | run | — | AdvText 1/1, GodotAdvHost 1/0 | 0 |  |
| Host `_retainedHistoryLayouts` | scene | — | AdvText 2/3, Surfaces 0/3, PresentationInput 0/1 | self-test |  |
| Host `_liveText` | scene | — | AdvText 0/3, PresentationInput 0/1 | self-test |  |
| Host `_retainedTextLayouts` | scene | numbered save | AdvText 7/5, Surfaces 4/3, PresentationInput 3/1 | self-test | its source glyph objects (`SourceTextObjects`) |
| Host `_activeLiveText` | scene | — | AdvText 2/3, PresentationInput 0/1 | self-test |  |
| Host `_suspendedRetainedTextLayoutSlot` | scene | — | PresentationInput 4/3, AdvText 1/1 | self-test |  |
| Host `_advText` | scene | — | AdvText 0/3, PresentationInput 0/2 | self-test |  |
| Host `_layoutCursors` | scene | — | AdvText 1/2, PresentationInput 0/1 | 0 |  |
| Host `_currentAdvLayout` | run | — | AdvText 3/2, PresentationInput 2/0 | self-test |  |
| Host `_glyphTimer` | run | — | AdvText 2/1 | 0 | ops 0x6e/0x196 |
| Host `_messageGlyphDelayMilliseconds` | run | SYS4REG.INI | AdvText 1/2 | 0 | the VM keeps a copy |
| Host `_messageWindowAlphaSetting` | run | SYS4REG.INI | AdvText 2/2 | 0 | the VM keeps a copy |
| Host `_textAntialias` | run | SYS4REG.INI | AdvText 13/2 | 0 | read from it |
| Host `_advTextForceComplete` | scene | — | AdvText 2/3, PresentationInput 0/4 | self-test |  |
| Host `_revealsFinishedUntilWait` | scene | — | PresentationInput 1/4, AdvText 1/0 | 0 |  |
| Host `_waitIndicators` | scene | — | AdvText 2/1, PresentationInput 0/1 | 0 |  |
| Host `_waitIndicatorFollowLastGlyph` | run | — | AdvText 1/1 | 0 |  |
| Host `_waitIndicatorBindings` | scene | — | AdvText 1/1, PresentationInput 0/1 | 0 |  |
| Host `_retainedWaitIndicators` | scene | — | AdvText 2/2, PresentationInput 3/1 | self-test |  |
| Host `_activeWaitLayout` | scene | — | PresentationInput 3/2, AdvText 3/0 | self-test |  |
| Host `_waitIndicatorStartedMs` | run | — | AdvText 2/1, PresentationInput 1/2 | self-test |  |
| Host `_waitIndicatorEnabled` | scene | — | PresentationInput 3/4, AdvText 2/1 | self-test |  |
| Host `IsTextRevealing` | scene | — | AdvText 3/4, PresentationInput 1/1, Movies 1/0, GodotAdvHost 1/0 | self-test | the VM keeps a copy |
| Host `Captured` | run | — | AdvText 0/2, Movies 1/0 | self-test | the shown text, for the self-test |
| Host `_advPagePresentationSuspended` | scene | — | PresentationInput 1/2, AdvText 1/0, Movies 1/0 | self-test |  |
| Host `_fontFamilies` | construction | — | GodotAdvHost 6/1 | 0 |  |
| Host `_fontRealization` | construction | — | GodotAdvHost 1/1 | 0 |  |
| Host `RubyRules` | construction | — | AdvText 6/0 | 0 |  |
| Host `FontFamilySource` | construction | — | GodotAdvHost 2/0 | 0 |  |
| Host `FontMetricsProbe` | construction | — | GodotAdvHost 2/0 | 0 |  |
| Host `BlankExtentMode` | construction | — | AdvText 2/0 | 0 |  |
| Host `PresentedTextRasterizer` | construction | — | GodotAdvHost 1/0 | 0 |  |
| Host `PresentedTextMaskCacheCapacity` | construction | — | GodotAdvHost 1/0 | 0 |  |

### Input and waits (24 in the VM, 11 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| VM `_coroutineYieldActive` | scene | — | ControlFlow 1/3 | 0 | read by the input thread |
| VM `_hiddenWindowWaiting` | run | — | ControlFlow 1/2, MenuServices 2/0, VirtualMachine 1/0 | 2 |  |
| VM `_hiddenWindowWheelTriggers` | run | — | ControlFlow 0/2, VirtualMachine 1/0 | 0 |  |
| VM `_advWaitCoroutineYieldRequested` | run | — | VirtualMachine 0/1, Input 0/1, ControlFlow 0/1 | 0 | a request to the interpreter (§5.4) |
| VM `_mouseCallbackTarget` | scene | — | Input 2/1, VirtualMachine 0/1 | 0 | op 0xcc callback |
| VM `_mouseCallbackOwner` | scene | — | Input 1/1, VirtualMachine 0/1 | 0 | op 0xcc callback |
| VM `_mouseCallbackIntervalMs` | run | — | Input 1/1 | 0 | op 0xcc callback |
| VM `_mouseCallbackArmedAtMs` | run | — | Input 1/2 | 0 | op 0xcc callback |
| VM `_heldTriggers` | scene | — | InputLatch 2/2 | 14 | the action latch |
| VM `_timedCallbacks` | scene | — | Timing 7/2 | 0 | ops 0xd3-0xd5 |
| VM `_timedCallbackCursor` | scene | — | Timing 5/2 | 0 | ops 0xd3-0xd5 |
| VM `_timedCallbackStartedAtMs` | scene | — | Timing 2/2 | 0 | ops 0xd3-0xd5 |
| VM `_timedCallbackAbortOffset` | scene | — | Timing 2/2 | 0 | ops 0xd3-0xd5 |
| VM `_timedCallbackGuard` | scene | — | Timing 1/2 | 0 | ops 0xd3-0xd5 |
| VM `_interactiveLock` | construction | — | VirtualMachine 12/0, Input 7/0, InputLatch 1/0, ControlFlow 1/0 | 3 | lock: input state and the callback frames |
| VM `_pointerX` | run | — | VirtualMachine 3/1, Input 3/0 | 0 |  |
| VM `_pointerY` | run | — | VirtualMachine 3/1, Input 3/0 | 0 |  |
| VM `_mouseButtonState` | scene | — | VirtualMachine 0/2, Input 1/0 | 0 |  |
| VM `_mouseWheelDelta` | scene | — | VirtualMachine 0/3, Input 0/1 | 0 |  |
| VM `_advTextWaitPolling` | run | — | MenuServices 4/2, VirtualMachine 3/2, Input 1/2, ControlFlow 1/0, PhysicalFastForward 1/0 | 3 |  |
| VM `_advInputPollPageless` | run | — | Input 1/2, VirtualMachine 1/0 | 0 |  |
| VM `_heldInputCallbackMask` | scene | — | VirtualMachine 0/2, Input 1/0 | 0 |  |
| VM `_queuedInputCallbackMask` | scene | — | VirtualMachine 1/2, Input 0/1, SkipClickCancel 0/1 | 0 |  |
| VM `InputBindings` | run | — | VirtualMachine 4/4, Input 5/1, SkipClickCancel 1/0, PhysicalFastForward 1/0 | 11 |  |
| Host `_gate` | construction, drained at a reset | — | PresentationInput 4/6 | 0 | wait handle |
| Host `_inputCallbackSignal` | construction, drained at a reset | — | PresentationInput 2/4 | 0 | wait handle |
| Host `_frameSignal` | construction | — | PresentationInput 2/12, Movies 0/3, AdvText 0/1 | 0 | wait handle |
| Host `_stopRequested` | run | — | PresentationInput 3/1 | 0 | ends every wait |
| Host `_rootReloadInterrupt` | scene | — | PresentationInput 2/2 | 0 | ends every wait |
| Host `IsWaiting` | scene | — | PresentationInput 4/3, AdvText 2/0, Movies 1/0 | self-test |  |
| Host `IsTransitionWaiting` | scene | — | PresentationInput 4/5, GodotAdvHost 1/0, Movies 1/0 | 0 |  |
| Host `IsSleeping` | scene | — | PresentationInput 0/3, GodotAdvHost 1/0, Movies 1/0 | 0 |  |
| Host `_frontendModalRelease` | construction | — | PresentationInput 2/4 | 0 | wait handle; a front-end modal hold |
| Host `_frontendModalHolds` | run | — | PresentationInput 1/2 | 0 | a front-end modal hold |
| Host `SleepScale` | run | — | PresentationInput 1/0 | 0 | the driver's `speed` |

### Interpreter (32 in the VM, 3 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| VM `_coroutineYieldEntered` | scene | — | ControlFlow 2/3 | 0 | op 0x199 yield: resume state |
| VM `_coroutineResumePc` | scene | — | ControlFlow 1/3 | 0 | op 0x199 yield: resume state |
| VM `_coroutineYieldOwner` | scene | — | ControlFlow 1/3 | 0 | op 0x199 yield: resume state |
| VM `_coroutineResumesTextWait` | scene | — | ControlFlow 1/3 | 0 | op 0x199 yield: resume state |
| VM `_s` | construction | — | VirtualMachine 3/1 | 0 |  |
| VM `_t` | construction | — | VirtualMachine 7/1 | 0 |  |
| VM `_handlers` | construction | — | VirtualMachine 2/1 | 0 |  |
| VM `_provider` | construction | — | ScriptLifecycle 7/0, VirtualMachine 3/1, SkipClickCancel 1/0 | 0 |  |
| VM `_cur` | run | — | VirtualMachine 63/3, Input 32/0, ControlFlow 27/0, AdvText 13/0, Persistence 8/0, Timing 5/0, MemoryCollections 4/0, TextStores 3/0, DeterministicRandom 1/0, MenuServices 1/0 | 2 |  |
| VM `_depth` | run | — | VirtualMachine 6/2, ScriptLifecycle 3/0, SkipClickCancel 1/0 | 0 |  |
| VM `_debugControlLock` | construction | — | VirtualMachine 9/0, Persistence 2/0, FrameLocations 1/0, AdvText 1/0 | 10 | lock |
| VM `_activeFrameNames` | run | — | VirtualMachine 3/2, AdvText 1/0 | 3 |  |
| VM `_activeExecutionFrames` | run | numbered save | VirtualMachine 4/2, FrameLocations 1/0 | 5 | the saved frames |
| VM `_saveResumeFrame` | scene | numbered save | VirtualMachine 5/2, Persistence 0/2 | 2 | where the saved frames stop (op 0x1ad) |
| VM `_restoreFrameIndex` | run | load only | Persistence 4/3, VirtualMachine 0/3 | 0 |  |
| VM `_debugActiveFrame` | scene | — | VirtualMachine 6/3 | 5 | debug control |
| VM `_debugActiveFrameId` | scene | — | VirtualMachine 3/3 | 3 | debug control |
| VM `_debugNextFrameId` | run | — | VirtualMachine 0/1 | 0 | debug control |
| VM `_debugFrameReturnRequest` | scene | — | VirtualMachine 5/4 | 0 | debug control |
| VM `_hostedSceneEntry` | scene | — | VirtualMachine 1/3 | 0 |  |
| VM `_hostedScenePackedId` | scene | — | VirtualMachine 1/4 | 0 |  |
| VM `_interactiveFrame` | scene | — | VirtualMachine 12/4, Input 1/3, InputLatch 1/0, ControlFlow 1/0 | 0 | the frame a callback runs in |
| VM `_rawInputFrame` | scene | — | VirtualMachine 4/2, Input 0/1 | 3 | the frame a callback runs in |
| VM `_initialRootRun` | scene | — | VirtualMachine 0/2, ScriptLifecycle 1/0 | 0 |  |
| VM `_stopRequested` | run | — | VirtualMachine 2/1, ControlFlow 1/0 | 0 |  |
| VM `_rootReloadRequested` | scene | — | VirtualMachine 2/2, ControlFlow 1/0 | 0 |  |
| VM `_valueSwitchTargets` | scene | — | ControlFlow 2/1, VirtualMachine 0/1 | 0 | string switch (ControlFlow) |
| VM `_preloadedScriptSlots` | scene | — | ScriptLifecycle 1/1, VirtualMachine 0/1 | 0 | ops 0x06/0x08 |
| VM `CallScriptDispatches` | run | — | ScriptLifecycle 0/3, SkipClickCancel 0/1 | 5 |  |
| VM `HaltReason` | run | — | MemoryCollections 0/17, VirtualMachine 1/14, ScriptLifecycle 0/10, AdvText 0/4, Persistence 0/4, Input 0/3, AdvServices 0/1, ControlFlow 0/1, Timing 0/1, SkipClickCancel 0/1 | 166 |  |
| VM `CompatibilityFailure` | run | — | VirtualMachine 0/1 | 13 |  |
| VM `Steps` | run | — | VirtualMachine 3/2 | 2 |  |
| Host `_scriptContextLock` | construction | — | PresentationInput 3/0 | 0 | lock; the host's copy of the script stack |
| Host `_scriptContexts` | run | — | PresentationInput 1/2 | 0 | the host's copy of the script stack; movies resolve against its top |
| Host `_rootScene` | construction | — | GodotAdvHost 0/1, PresentationInput 1/0 | 0 | the bottom of that copy |

### Wiring (4 in the VM, 3 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| VM `_host` | construction, drained at a reset | — | VirtualMachine 19/11, Audio 15/10, AdvText 11/9, Presentation 14/1, Surface 7/7, Input 8/3, PhysicalFastForward 0/7, Movie 6/1, Timing 6/1, AdvServices 3/3, ControlFlow 2/3, MenuServices 1/4, InputLatch 3/2, TextHistory 2/1, SkipClickCancel 2/1, ScriptLifecycle 0/2, Persistence 2/0, Values 1/0, RetainedObjects 1/0, Diagnostics 1/0, RuntimeSettings 0/1, Animation 1/0, ScreenMode 1/0 | 0 | becomes the natives |
| VM `_o` | construction | — | VirtualMachine 14/1, FastForwardPace 4/0, SkipClickCancel 4/0, ScriptLifecycle 4/0, MenuServices 2/0, AdvServices 1/0, AdvText 1/0, Input 1/0 | 5 |  |
| VM `_rules` | construction | — | VirtualMachine 0/1, RetainedObjects 1/0, AdvText 1/0 | 0 | each native takes its own rules (§5.1, rule 3) |
| VM `_sink` | construction | — | VirtualMachine 9/1, ScriptLifecycle 4/0, ControlFlow 2/0, SkipClickCancel 1/0 | 0 | trace sink |
| Host `TraceOps` | run | — | Surfaces 2/0 | self-test | `--gfx-log` |
| Host `_res` | construction | — | Audio 5/0, Movies 5/0, Surfaces 3/0, PresentationInput 2/0, GodotAdvHost 0/1 | 0 | the resource map |
| Host `_clock` | construction | — | PresentationInput 31/0, AdvText 6/0, Audio 4/0, Movies 1/0, GodotAdvHost 0/1, Surfaces 1/0 | self-test | time device |

### Front end (0 in the VM, 6 in the host)

| Field | Lifetime | Saved | Read / written in (reads/writes) | Tests | Note |
|---|---|---|---|---|---|
| Host `_runGeometry` | run | — | AdvText 2/4 | 0 | the page locator |
| Host `_runCut` | run | — | AdvText 1/2 | 0 | the page locator |
| Host `Pages` | run | — | PresentationInput 2/1 | 0 | the driver's page counter |
| Host `_main` | construction | — | Audio 17/0, PresentationInput 13/0, Movies 3/0, Surfaces 2/0, GodotAdvHost 0/1 | self-test | milestone 4's typed interface |
| Host `_timeline` | construction | — | PresentationInput 37/0, AdvText 19/0, Audio 15/0, Surfaces 7/0, Movies 5/0, GodotAdvHost 0/1 | self-test | `--timeline-log` |
| Host `_locator` | run | — | AdvText 4/0, PresentationInput 0/1, GodotAdvHost 0/1 | 0 | the page locator |
