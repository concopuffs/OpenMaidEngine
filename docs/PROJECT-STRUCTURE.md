# Project Structure

Layout for the *姫狩りダンジョンマイスター* (Himegari) → open AGE-engine reimplementation.
The guiding rule is **source vs. derived vs. our work**: the shipped game is read-only
input, the extracted archives and everything our tools generate are disposable/reproducible,
and our code + docs live entirely apart from the game install. Nothing we produce is ever
written back into the game folder.

Workspace root: `S:\Game Hacking\Eushully\Himegari\`

```
S:\Game Hacking\Eushully\Himegari\           ← workspace root (three siblings)
│
├── Himegari_Game/                          ← SOURCE — pristine game install (read-only)
│   │   Never edit, move, or add to this folder. It holds ORIGINALS ONLY.
│   ├── AGE.EXE, AGERC.DLL, *.dll             shipped engine (packed). Stays intact and
│   │                                         runnable in place — Frida launches it if needed.
│   ├── DATA1-5.ALF, APPEND01.ALF/.AAI        shipped archives (~2.3 GB).
│   ├── *.BIN                                 49 loose patch-override scripts (v1.03) —
│   │                                         AUTHORITATIVE over DATA1. Plus two root-only
│   │                                         engine files (SYS4INI=S4IC, SYS4AB=S4AB).
│   └── *.exe (uninstallers), SAS0099.OGG …   other shipped files.
│
├── extracted/                               ← DERIVED (game-side) — extracted ALF contents,
│   │                                          ~3.9 GB, regenerable with a local archive extractor.
│   └── DATA1/ … DATA5/                         DATA1 = 481 .BIN scripts (the corpus we parse)
│                                               + AGF/BMP/WAV in the others.
│
└── age-reimpl/                              ← OUR WORK (everything we made lives here)
    │
    ├── README.md                             navigation-only repository front door; canonical facts stay in docs/
    ├── LICENSE                               holder-neutral MIT grant for authored project work
    ├── THIRD_PARTY_NOTICES.md                 incorporated-code licenses + prior-research acknowledgment
    ├── global.json                           pins the validated .NET 8 SDK feature band
    ├── .editorconfig / .gitattributes        UTF-8, indentation, text/EOL, and binary-file policy;
    │                                         tracked source and generated references use canonical LF
    ├── .gitea/workflows/core-validation.yml  read-only Ubuntu source-only core gate for the private Gitea remote
    ├── .gitea/workflows/linux-release-build.yml
    │                                       pinned Linux export/package/smoke artifact job; no release publication
    ├── run-godot.ps1 / run-godot.cmd         tracked development launcher + Windows wrapper;
    │                                          resolves Godot/game-root from parameters, environment,
    │                                          PATH, and the conventional sibling install
    ├── tools/                               Python tooling (parser/disassembler + extractors + VM)
    │   ├── paths.py                           ★ central path anchor — the ONLY place that knows
    │   │                                        where the game / extracted / build dirs are. All
    │   │                                        tools import it; relocatable with no other edits.
    │   ├── validate.py                        layered core/workspace/runtime/full validation driver
    │   ├── test_validate.py                   pure resolver + validation-plan regressions
    │   ├── build-linux-x64.sh                 one-command pinned Linux export/package/smoke pipeline
    │   ├── dotnet_publish_proxy.py            guarded low-memory Godot export-time publish staging
    │   ├── bootstrap-godot-linux-x64.sh       repo-local Godot 4.7 Linux editor/template bootstrap
    │   ├── godot-linux-x64.json               immutable Godot editor/template source + SHA-256 manifest
    │   ├── install_godot_templates.py         selective HTTP-range installer for pinned template members
    │   ├── package_linux_x64.py               export verifier + notice/checksum/archive packager
    │   ├── test_install_godot_templates.py, test_package_linux_x64.py,
    │   │   test_dotnet_publish_proxy.py        source-only bootstrap/package/publish regressions
    │   ├── sys4load.py                         loader + disassembler (opcode-decoding)
    │   ├── age_opcodes.py                      GENERATED complete Python ABI view (do not hand-edit)
    │   ├── opcodes_build.py                    generator/linter: vm-map/opcodes.toml -> the 5 artifacts below
    │   ├── opcodes_model.py                    load + lint (dangling-ref, confidence-ceiling, vocab) + dependents
    │   ├── age_opcodes_himegari.py             GENERATED from opcodes.toml (do not hand-edit)
    │   ├── globals_build.py                    generator/linter: vm-map/globals.toml (+auto map) -> build/globals.json, docs/global-reference.md
    │   ├── story_flags.py                      static story-flag miner (branch-condition) -> build/story-flags-candidates.json; --bootstrap
    │   ├── scjump_decode.py                    decode SCJUMP progression logic -> build/scjump-decisions.* ; --verify (VM cross-check)
    │   ├── test_globals.py, test_opcodes.py    unit tests for the globals / opcode tooling
    │   ├── vm0.py                              headless Python VM (Phase A0); `--test` = RECOVER unit test
    │   ├── extract_phase2.py                   batch: disasm + text + data extraction
    │   ├── extract_init.py, extract_message_table.py, global_map.py …
    │   │                                       *INIT / ID-dispatched message parsers,
    │   │                                       global-var map builder
    │   ├── validate_opcode_table*.py           decode-coverage validators
    │   ├── movie-corpus-gate/                  C# full-corpus FFmpeg decode/lifecycle acceptance tool
    │   └── probe_*.py                          format reverse-engineering probes (historical)
    │
    ├── bin/                                  ignored home for optional machine-local tools
    │   ├── README.md / .gitignore              tracked policy; binaries never enter Git
    │   └── BinExtractALF.exe + LzssCpp.dll     optional local archive oracle (unverified provenance)
    │
    ├── vm-map/                               VM / reverse-engineering reference artifacts
    │   ├── opcodes.toml                        ★ CANONICAL opcode reference (hand-edited: ABI + semantics
    │   │                                        + provenance + depends_on). Single source of truth for opcodes.
    │   ├── globals.toml                        ★ CANONICAL global-variable registry (hand-edited: name + category
    │   │                                        + value_domain + provenance). Single source of truth for globals/story-flags.
    │   └── opcode-leads.json, small-script-listings.md
    │
    ├── docs/                                 all documentation
    │   ├── PROJECT-STRUCTURE.md                this file (where things live)
    │   ├── tools-reference.md                  every tool: purpose, usage, I/O (operational companion)
    │   ├── asset-resolution-re.md              resId→file RE (graphics/audio); asset-index steering
    │   ├── global-memory-re.md                 runtime global observation RE (SHELVED; future starting point)
    │   ├── remake-architecture-and-roadmap.md  THE direction doc (phases A–E)
    │   ├── phase-a-slice-plan.md               the current slice (A0/A1/A2)
    │   ├── phase-b-framework.md                natural boot/menu/session → first-gameplay framework
    │   ├── platform-portability.md             OS dependencies + future cross-platform readiness
    │   ├── vm-mapping-plan.md                  the phased decode plan
    │   ├── himegari-port-reference.md          master reference + engine background
    │   ├── name-resolution.md                  call-script + global-var name recovery (+ globals.toml registry)
    │   ├── sys4-format-notes.md                byte-level container format
    │   ├── script-inventory.md                 what the 481 scripts are
    │   ├── opcode-reference.md                 GENERATED from opcodes.toml (human-readable opcode reference)
    │   ├── global-reference.md                 GENERATED from globals.toml (human-readable global registry)
    │   ├── scjump-progression.md               SCJUMP progression decode + SCINIT decision→scene registry
    │   └── engine-re.md                        native-engine RE (Ghidra+MCP): dispatch table, decompiled ops
    │
    ├── build/                               DERIVED (our-work-side) — generated by tools/; disposable
    │   ├── downloads/, toolchains/             immutable dependency caches and repo-local Godot toolchain
    │   ├── export/linux-x64/                   verified loose Godot Linux export
    │   ├── native/linux-x64/                   validated FFmpeg shim/shared-library bundle
    │   ├── package/                            staged release tree, smoke log, and `.tar.gz` artifact
    │   ├── disasm/                            <NAME>.asm — human-readable disassembly, one per script
    │   ├── text/                              extracted text:
    │   │   ├── <NAME>.strings.txt               all inline strings in a script
    │   │   ├── dialogue.jsonl                   show-text lines only (the translation corpus)
    │   │   └── strings.jsonl                    every string, tagged by source opcode
    │   ├── data/                              parsed data tables (*INIT → JSON)
    │   ├── scripts-json/                      machine-readable full dumps (on demand via --json)
    │   ├── textures/                          AGF-to-BMP stills (convert_agf.py) - diagnostic pixel oracle only
    │   ├── engine-dump/                       UNPACKED engine dump (frida/dump_engine.py): range_<base>.bin + manifest.json
    │   ├── asset-index.json, asset-sections.json   asset resolver data (parse_sys4ini / resolve_asset)
    │   ├── global-var-map.{json,md}           partial global-variable name map (auto shape inference; feeds globals.toml merge)
    │   ├── globals.json                        GENERATED from globals.toml merged over global-var-map (sys4load labels + C# VM)
    │   ├── story-flags-candidates.json         GENERATED by story_flags.py (ranked story-flag review surface)
    │   ├── scjump-decisions.{json,md}          GENERATED by scjump_decode.py (progression decision table)
    │   ├── opcodes.json                        GENERATED from opcodes.toml (machine view for the C# VM)
    │   └── manifest.json, opcode-coverage.md   (opcode-coverage.md GENERATED from opcodes.toml)
    │
    ├── engine/                              DELIVERABLE — the .NET VM/runtime projects and tests (AgeEngine.sln)
    │   ├── Age.Engine/Model/                retained graphics state plus separately navigable public
    │                                       render, transition, diagnostic, and persistence contracts
    │   ├── Age.Engine/Sys4/                 runtime game-root selection, catalog parser, loose-first bounded
    │                                       ALF asset store, script provider, AGF/LZSS and Windows CUR decoders,
    │                                       and resource facade
    │   ├── Age.Engine/Text/                 backend-neutral glyph-mask requests/results, deterministic AGE
    │                                       compositor/layout, retained live/History glyph and wait-atlas
    │                                       publication, and bounded font/glyph cache primitives
    │   ├── Age.Engine/Persistence/          native S3SD/S4SD + S3RT codecs, shared payload/ReadTextDB,
    │                                       layout-3 numbered state/history/gfx, DAT/STH pair + BMP codec,
    │                                       and profile-owned state
    │   ├── Age.Engine.Frontend/             platform-neutral frontend support: diagnostics/options, page
    │                                       location, performance logging, movie runtime/FFmpeg decoding,
    │                                       audio alignment, surface registry, and WAV sanitization
    │   └── Age.Engine.Text.Windows/         optional exact Windows GDI gray-4 reference backend; kept
    │                                       outside the platform-neutral VM/runtime core
    ├── native/                              authored native runtime boundaries
    │   └── age_movie_ffmpeg/                project-owned FFmpeg C ABI, immutable Windows/Linux dependency manifests,
    │                                       and target bootstrap/build scripts (outputs stay under disposable build/)
    ├── tools/frida/                         runtime-capture + engine-dump scripts (see tools/frida/README.md)
    └── godot/                               DELIVERABLE — the Godot/C# ADV front-end (references Age.Engine
                                            plus the optional exact Windows text adapter). Its assembly embeds
                                            generated build/opcodes.json through HimegariRuntimeMetadata, so
                                            exports do not need repository discovery. Also contains the portable
                                            TextServer glyph adapter, per-profile font-substitution configuration,
                                            and TITLE-only F4 debug scene launcher
```

The Godot deliverable includes `Himegari.sln` because Godot's .NET exporter requires a solution,
`export_presets.cfg` for the Linux x86_64 target, and `build/.gdignore` so disposable capture frames below
the project do not enter its import or export scan. `tools/export-linux-x64.ps1` produces and validates the
complete disposable artifact under `build/export/linux-x64/`.

`godot/Main.cs` retains the front-end's startup and runtime coordination. Behavior-neutral partial-class
companions keep cohesive surfaces independently navigable without changing the Godot node type or invocation
paths: `godot/Main.SelfTest.cs` owns the synthetic threaded/headless regression harness, while
`godot/Main.Audio.cs` owns BGM, voice, sound-effect, mixer-routing/persistence, and audio-bus control, and
`godot/Main.Movie.cs` owns decoder staging, movie frame/audio publication, completion, and teardown.
`godot/Main.Compositor.cs` owns retained/GPU/software composition state, texture resolution and caching,
surface-transition drawing, raster helpers, and compositor decision logging. `godot/Main.Input.cs` owns
Godot input routing, locator/debug hotkeys, debug-scene dispatch, cursor control, native alerts, and
full-width text entry.

`engine/Age.Engine.Frontend/` is the production owner of frontend helpers that do not depend on Godot types.
Both `engine/AgeEngine.sln` and `godot/Himegari.sln` include the project; Godot and `Age.Engine.Tests` consume it
through project references rather than compiling duplicate linked sources. The first owned cluster contains
`PageLocatorState`, `GodotTimelineLog`, `GodotTraceSink`, `StepLimitDiagnosticFormatter`, `GodotVmOptions`,
`PerformanceFrameLog`, and `WindowLaunchOptions`. It also owns the internal movie decoder boundary/runtime,
audio-timeline alignment, movie-surface registry, and RIFF/WAVE sanitizer; only the `Himegari` and
`Age.Engine.Tests` assemblies receive explicit friend access to that internal API. FFmpeg native interop and the
asynchronous/paced decoder also live here; the `Age.MovieCorpusGate` tool receives friend access to the native
frame-source seam and references this project rather than compiling source links. Reusable movie-corpus discovery,
decode-gate, and report contracts live here as well; `tools/movie-corpus-gate/Program.cs` remains only the
command-line entry point and output writer.

`godot/GodotAdvHost.cs` retains cross-domain host coordination. Its partial-class companion
`godot/GodotAdvHost.AdvText.cs` owns live/retained ADV text, surface glyph rasterization and caching,
history presentation, message-window alpha, and retained wait-indicator configuration/publication.
`godot/GodotAdvHost.PresentationInput.cs` owns script/presentation synchronization, waits and timing,
message-skip/input services, cursor and foreground waits, frame/backbuffer publication, transitions, and
scene-context lifecycle coordination. `godot/GodotAdvHost.Surfaces.cs` owns decoded-image caching, mutable
surface pixels/resources/dimensions, fill/copy/resolve operations, render-target publication, and surface/range
teardown. `godot/GodotAdvHost.Movies.cs` owns movie surface bindings, ordinary and modal playback, movie-mask
transitions, diagnostic snapshots, frame/completion publication, and mask teardown.
`godot/GodotAdvHost.Audio.cs` owns BGM, voice and SFX resolution/dispatch, delayed voice state, volume/routing
control, and blocking BGM fades; presentation/input retains the message-skip, reset, and frame-pulse consumers
of that state through the sealed partial class.

`engine/Age.Engine/Hosting/IHost.cs` remains the aggregate runtime host accepted by the VM and existing host
implementations. `engine/Age.Engine/Hosting/IDiagnosticHost.cs` owns recoverable-warning and modal-diagnostic
reporting contracts, while `engine/Age.Engine/Hosting/ILifecycleHost.cs` owns script-context entry/exit, sleep and
timed-deadline waiting, frame yield, and scene reset. `engine/Age.Engine/Hosting/IAudioHost.cs` owns BGM, voice,
SFX, fade, volume, and route-control contracts, including the compatibility overload/default chains used by
simple hosts. `engine/Age.Engine/Hosting/IMovieHost.cs` owns ordinary, positioned, mask-transition, activity-query,
and modal movie playback contracts. `engine/Age.Engine/Hosting/IGraphicsHost.cs` owns mutable surfaces,
retained-range/frame presentation, transitions, texture lifecycle, pixel capture/replace, draw, and size
contracts. `engine/Age.Engine/Hosting/IInputHost.cs` owns modal fullwidth entry, ADV waits/callback servicing,
input clock, cursor resources, and skip-state interaction. `engine/Age.Engine/Hosting/IAdvHost.cs` owns live and
surface text, history presentation, ADV layout publication, message presentation settings, wait indicators, and
page-presentation suspension. `IHost` inherits all seven focused contracts; every required member and
overload/default chain remains unchanged. The neutral
`engine/Age.Engine/Model/MovieMaskTransitionRequest.cs` record carries movie mask work between the VM, host, and
retained graphics without making `Model` depend on `Hosting`.
`engine/Age.Engine/Model/RgbaImage.cs` owns the format-independent packed RGBA image transported between SYS4
decoders, host surfaces, renderers, persistence, and tests; consumers do not depend on `Sys4` merely to exchange
decoded pixels.
`engine/Age.Engine/Model/SurfaceContracts.cs` owns neutral surface fill/copy requests and the black-fade direction
shared by VM presentation, graphics hosts, Godot, and tests.
`engine/Age.Engine/Model/InputContracts.cs` owns neutral ADV auto-wait state and fullwidth text-edit request/result
records shared by input hosts, timing policy, VM dispatch, Godot, and tests.
`engine/Age.Engine/Model/AdvPresentationContracts.cs` owns neutral ADV wait-indicator configuration shared by VM
dispatch, retained presentation, hosts, Godot, and tests. `engine/Age.Engine/Model/DiagnosticContracts.cs` owns
the neutral synchronous diagnostic message transported between VM dispatch, diagnostic hosts, Godot, and tests.

`engine/Age.Engine/Model/GfxState.cs` retains cross-domain retained-graphics coordination.
`engine/Age.Engine/Model/GfxState.Contracts.cs` owns its public render, transition, diagnostic, persistence,
animation, numeric-glyph, and handle-range contracts. `engine/Age.Engine/Model/GfxState.Surfaces.cs` owns surface
resource/color-key state, created/reloadable classification, movie stop-time metadata, render-target/tile
configuration, and surface lifecycle operations. `engine/Age.Engine/Model/GfxState.RetainedObjects.cs` owns the
retained-object record and registry/index, range-transform state, object creation/query/clone/erase and draw
binding, plus numeric-glyph object generation. `engine/Age.Engine/Model/GfxState.Animation.cs` owns the shared
animation clock, object color/source-cell/matrix/cyclic channels, animation control and forced completion, and
the interpolation helpers consumed by retained-scene sampling. `engine/Age.Engine/Model/GfxState.Presentation.cs`
owns surface/movie transition queues, presentation activity and click-skip logic, diagnostics and dirty-reason
accounting, and visible retained-scene snapshots.

`engine/Age.Engine/Vm/VirtualMachine.cs` retains VM execution lifecycle, cross-domain state, and the proven
top-level opcode dispatcher; `Step` now contains only label grouping, domain routing, and the unknown-op fallback.
Its partial-class companion `engine/Age.Engine/Vm/VirtualMachine.Audio.cs` owns VM audio
state, BGM restart semantics, and the BGM/voice/SFX/mixer opcode handler; `Step` retains the audio labels and
routes that family into the handler. `engine/Age.Engine/Vm/VirtualMachine.Movie.cs` owns modal/asynchronous/
positioned movie playback, movie surface metadata/activity queries, and movie-mask transition dispatch; `Step`
likewise retains and routes the movie labels. `engine/Age.Engine/Vm/VirtualMachine.Surface.cs` owns per-script
reload-policy reset/configuration, surface allocation/loading, texture binding and sizing, mutable surface
fill/copy, render-target control, and individual/transient surface release; its labels remain at their existing
dispatcher positions around the retained numeric-glyph and object cases.
`engine/Age.Engine/Vm/VirtualMachine.RetainedObjects.cs` owns retained-object registry queries,
default-slot and geometry mutation, direct and range transforms, clone, and erase dispatch; animation, surface,
ADV-binding, and presentation labels remain in their respective dispatcher groups.
`engine/Age.Engine/Vm/VirtualMachine.Animation.cs` owns retained spritesheet and color channels, timed and cyclic
transforms, per-object animation control, frame-time sampling, and the shared animation-clock opcode handler.
`engine/Age.Engine/Vm/VirtualMachine.Presentation.cs` owns queued surface-alpha transitions, frame and object-range
publication, skip-aware blocking fades/crossfades, foreground-transition waits, and graphics command-queue clear.
`engine/Age.Engine/Vm/VirtualMachine.AdvText.cs` owns live ADV text emission, layout/cursor/wait-indicator state,
text style and glyph-delay control, direct surface-string and retained numeric-glyph rendering, and retained
text/wait-object bindings; text history and input/skip/auto services remain separate.
`engine/Age.Engine/Vm/VirtualMachine.TextHistory.cs` owns history recording control, metadata append/navigation,
retained history rendering, metadata/voice lookup, and history backlog clearing.
`engine/Age.Engine/Vm/VirtualMachine.AdvServices.cs` owns persistent/active message-skip control, read-skip
settings and queries, auto-message state/timing, and per-message voice/skip reset opcode dispatch; shared state
and refresh helpers remain in the VM coordinator because live text and input paths also consume them.
`engine/Age.Engine/Vm/VirtualMachine.Input.cs` owns blocking ADV waits, hotspot registration/arming, cursor
resources and virtual position, raw mouse/joystick callback registration and dispatch, action polling, and
physical-input mapping; public host-thread input entry points and shared synchronization remain in the coordinator.
`engine/Age.Engine/Vm/VirtualMachine.Timing.cs` owns the monotonic-time query, host sleep, relative timed-callback
schedule construction, deadline/catch-up selection, and callback resumption opcode handler.
`engine/Age.Engine/Vm/VirtualMachine.Persistence.cs` owns catalog-unlock lookup, numbered save/load and nested
restore continuation, metadata/copy/delete, thumbnail persistence, and shared-profile integer/string opcode
dispatch; capture/apply helpers and persistent coordinator state remain in `VirtualMachine.cs`.
`engine/Age.Engine/Vm/VirtualMachine.MemoryCollections.cs` owns string byte length, addressed lookup/copy,
inline arrays, rectangle search and stable index sorting, bounded integer queues/stacks, bit/range operations,
and native-style random-modulo dispatch; shared storage/address helpers remain in the coordinator.
`engine/Age.Engine/Vm/VirtualMachine.ControlFlow.cs` owns local jumps/calls/returns, value-switch construction,
ADV coroutine handler save/yield/resume, and bounded labeled-yield dispatch; process/root exit and cross-script
lifecycle are routed separately. `engine/Age.Engine/Vm/VirtualMachine.ScriptLifecycle.cs` owns process/frame/root
exit, native run-state compatibility, initial-root-run query, ordinary cross-script calls, mounted append autoruns,
and preloaded script-slot load/call dispatch; frame execution, script-provider access, and shared lifecycle state
remain in `VirtualMachine.cs`.
`engine/Age.Engine/Vm/VirtualMachine.Values.cs` owns integer arithmetic, bitwise and comparison operations,
string comparison/concatenation/conversion/move, native byte-length and CP932 operations, and the host-backed
fullwidth string editor; shared operand storage, addressing, and native-string encoding remain in the coordinator.
`engine/Age.Engine/Vm/VirtualMachine.Diagnostics.cs` owns diagnostic value/newline accumulation and synchronous
show-and-clear opcode dispatch; shared diagnostic state and native-context formatting remain in the coordinator.
`engine/Age.Engine/Vm/VirtualMachine.RuntimeSettings.cs` owns message-window alpha and system-menu enable/show
delay opcode dispatch; reset/default initialization, public menu-state accessors, and host state remain centralized.

The disposable `build/page-map-<SCENE>.jsonl` files are produced by editor/development Godot runs and map
runtime ADV page ordinals to their authoritative script offsets for `tools/locate_page.py`. Packaged exports
have no repository output tree and write their automatic maps below `user://diagnostics/page-maps` instead.

## Conventions

- **Three-way separation.** `Himegari_Game/` = untouched originals; `extracted/` =
  game-derived data (regenerable, game-side); `age-reimpl/` = everything we authored. The first two
  are consumed, never modified.
- **Tools never hard-code paths.** `tools/paths.py` derives `GAME_DIR`, `EXTRACTED`, `DATA1`,
  `BUILD`, etc. from its own location. To point the tools at a different install, edit that one file.
  The whole tree can be relocated without touching any other tool.
- **Path references in docs** are `age-reimpl/`-relative (e.g. `tools/sys4load.py`,
  `build/text/dialogue.jsonl`) unless they name a game/extracted path explicitly.
- **Authoritative script copies:** where a script exists both as a loose `.BIN` in the game folder
  and under `extracted/DATA1/`, the game-folder copy (patch v1.03) wins. `paths.scripts()` resolves
  this automatically (overrides win).
- **`build/` and `extracted/` are disposable.** `build/` regenerates via `tools/extract_phase2.py`
  (or `sys4load.py`); `extracted/` regenerates from the `.ALF` files with a separately obtained local
  extractor. `bin/README.md` records the optional local convention; no extractor binary is tracked.
  Safe to delete and rebuild; do not hand-edit.
- **Opcode knowledge is edited ONLY in `vm-map/opcodes.toml`** (ABI + semantics + provenance +
  `depends_on`). Run `tools/opcodes_build.py --build` to regenerate both Python views
  (`tools/age_opcodes.py`, `tools/age_opcodes_himegari.py`), machine JSON (`build/opcodes.json`), reference
  (`docs/opcode-reference.md`), and coverage. `--lint` checks dangling deps / confidence-ceiling / vocabulary.
  Both Python modules are generated and must not be hand-edited.
- **Global-variable knowledge is edited ONLY in `vm-map/globals.toml`** (name + category + value_domain +
  provenance). Run `tools/globals_build.py --build` to regenerate `build/globals.json` (sys4load labels) and
  `docs/global-reference.md`; `--lint` checks vocabulary / auto-shape≠high / dangling deps. Curated entries
  override the auto shape map (`build/global-var-map.json`). Discover story-flag candidates with `tools/story_flags.py`.
- **Encoding:** all generated text is UTF-8 (source strings are cp932/Shift-JIS, decoded on
  extraction). Run Python as `py -3.11 -X utf8`.
- **The game install is a runnable unit** — do not relocate `AGE.EXE`/`*.ALF`/DLLs relative to each
  other, or the game (and any Frida work) breaks.
