# Project Structure

Layout for the *姫狩りダンジョンマイスター* (Himegari) → open AGE-engine reimplementation.
The guiding rule is **source vs. derived vs. our work**: the shipped game is read-only
input, the extracted archives and everything our tools generate are disposable/reproducible,
and our code + docs live entirely apart from the game install. Nothing we produce is ever
written back into the game folder.

Workspace root: `S:\Game Hacking\Eushully\Himegari\`

The conventional read-only install roots are `Himegari_Game/` and `Kamidori/`. Himegari's legacy
extracted archive tree is `extracted/`; Kamidori's is `extracted-kamidori/` (needed by the Python
data-table tooling; production catalog scans read archives directly). Additional extracted roots may be
selected explicitly.

```
S:\Game Hacking\Eushully\Himegari\           ← workspace root
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
├── Kamidori/                                ← SOURCE — clean Kamidori install with the official updates and
│                                               appends (read-only; AGE 4.33G). Root .BIN files, 17 `$n$`
│                                               append records included, are the updates' loose records.
│
├── Kamidori_Translated/                     ← SOURCE — Kamidori with the English fan translation (read-only;
│                                               `patch/`, AGE 4.33F, English `$n$*.bin`). Used only by the
│                                               port's translation tests and translated runs
│                                               (docs/asset-resolution-re.md).
│
├── Arterial/創刻のアテリアル/                 ← SOURCE — 創刻のアテリアル (read-only; AGE 4.38D). No port profile: a
│                                               reference install for comparing engine builds only
│                                               (paths.REFERENCE_INSTALLS, docs/engine-re.md "Comparing engine builds").
│
├── extracted-kamidori/                      ← DERIVED — Kamidori archives (~4.5 GB), regenerable with
│   │                                          `bin/BinExtractALF.exe SYS4INI.BIN <dir>` / `APPENDnn.AAI <dir>`.
│   ├── DATA1/ … DATA5/                         DATA1 = 542 base .BIN scripts + AGF/WAV/CUR
│   └── APPEND01/ … APPEND03/                   append catalogs, literal `$n$NAME` record names
│
└── age-reimpl/                              ← OUR WORK (everything we made lives here)
    │
    ├── README.md                             navigation-only repository front door; canonical facts stay in docs/
    ├── LICENSE                               holder-neutral MIT grant for authored project work
    ├── THIRD_PARTY_NOTICES.md                 incorporated-code licenses + prior-research acknowledgment
    ├── notices/godot/                        Godot's LICENSE.txt + COPYRIGHT.txt for the pinned release, unmodified;
    │                                         notices.json pins each (packages ship them as Godot-*.txt)
    ├── notices/mingw-w64/                    the MinGW-w64 runtime's COPYING.MinGW-w64-runtime.txt (v11.0.1), unmodified;
    │                                         notices.json pins it (Windows packages ship MinGW-w64-runtime-COPYING.txt)
    ├── notices/noto-sans-jp/                 noto-cjk's OFL LICENSE (Sans2.004) as LICENSE.txt, unmodified; notices.json
    │                                         pins it (packages ship NotoSansJP-LICENSE.txt for the embedded panel font)
    ├── global.json                           pins the validated .NET 8 SDK feature band
    ├── Directory.Build.targets               makes a Debug build recompile when Optimize changes
    │                                         (drive.py port start --optimize)
    ├── .editorconfig / .gitattributes        UTF-8, indentation, text/EOL, and binary-file policy;
    │                                         tracked source and generated references use canonical LF
    ├── .github/workflows/core-validation.yml read-only, SHA-pinned Ubuntu 24.04 source-only core gate (GitHub Actions)
    ├── .github/dependabot.yml                weekly grouped GitHub Actions pin bumps against develop
    ├── .github/workflows/ffmpeg-sdk.yml      builds the minimal FFmpeg SDKs; dispatch drafts the deps-ffmpeg release
    ├── .github/workflows/release-build.yml   SHA-pinned Ubuntu 24.04 Linux/Windows x64 artifact jobs + tag-only promotion
    ├── run-godot.ps1 / run-godot.cmd         tracked development launcher + Windows wrapper;
    │                                          resolves Godot/game-root from parameters, environment,
    │                                          PATH, and the selected profile's conventional sibling install;
    │                                          forwards explicit ordered asset overlays and codec compatibility
    ├── tools/                               Python tooling (parser/disassembler + extractors + VM)
    │   ├── paths.py                           ★ immutable profile-aware context: selected game,
    │   │                                        extracted roots, and shared/game output ownership
    │   ├── validate.py                        layered core/workspace/runtime/full validation driver
    │   ├── test_validate.py                   pure resolver + validation-plan regressions
    │   ├── build-linux-x64.sh                 one-command pinned Linux export/package/smoke pipeline
    │   ├── build-windows-x64.sh               Linux-hosted Windows cross-export/package pipeline
    │   ├── dotnet_publish_proxy.py            guarded low-memory Godot export-time publish staging
    │   ├── publish_github_release.py          guarded/retry-safe draft-first GitHub release promotion
    │   ├── verify_windows_native.py           static AMD64 PE/native-bundle contract verifier
    │   ├── bootstrap-godot-linux-x64.sh       repo-local Godot 4.7 Linux editor/template bootstrap
    │   ├── godot-linux-x64.json               immutable Godot editor/template source + SHA-256 manifest
    │   ├── install_godot_templates.py         selective HTTP-range installer for pinned template members
    │   ├── package_linux_x64.py,
    │   │   package_windows_x64.py             target-specific export verifiers and normalized packagers
    │   ├── package_notices.py                 shared notice checks: pinned Godot/MinGW-w64 notices, NuGet packages named
    │   ├── mingw_runtime_notices.py           MinGW link-map check: linked runtime objects covered by the shipped notices
    │   ├── test_install_godot_templates.py, test_package_notices.py, test_package_linux_x64.py,
    │   │   test_package_windows_x64.py, test_mingw_runtime_notices.py,
    │   │   test_dotnet_publish_proxy.py,
    │   │   test_publish_github_release.py, test_write_ffmpeg_source_notice.py,
    │   │   test_release_workflow.py,
    │   │   test_verify_windows_native.py       source-only bootstrap/package/publish/native regressions
    │   ├── sys4load.py                         loader + disassembler (opcode-decoding)
    │   ├── age_opcodes.py                      GENERATED complete Python ABI view (do not hand-edit)
    │   ├── opcodes_build.py                    generator/linter: vm-map/opcodes.toml -> derived runtime/reference views
    │   ├── opcodes_model.py                    load + lint (dangling-ref, confidence-ceiling, vocab) + dependents
    │   ├── age_opcode_semantics.py             GENERATED investigated semantic-name view (do not hand-edit)
    │   ├── globals_build.py                    generator/linter: vm-map/globals[-<profile>].toml (+auto map) -> build/games/<profile>/globals.json, docs/global-reference[-<profile>].md
    │   ├── modding_games_build.py              generator: profile manifests + registries + oms/<profile>/ -> docs/modding/games/README.md
    │   ├── patch_points_build.py               validator/generator: vm-map/patch-points-<profile>.toml -> build/games/<profile>/patch-points.json, docs/patch-point-reference[-<profile>].md
    │   ├── story_flags.py                      static story-flag miner (branch-condition) -> build/games/<profile>/story-flags-candidates.json; --bootstrap
    │   ├── scjump_decode.py                    decode SCJUMP progression logic -> build/games/<profile>/scjump-decisions.* ; --verify (VM cross-check)
    │   ├── test_globals.py, test_opcodes.py    unit tests for the globals / opcode tooling
    │   ├── vm0.py                              headless Python VM (Phase A0); `--test` = RECOVER unit test
    │   ├── extract_phase2.py                   batch: disasm + text + data extraction
    │   ├── extract_init.py (+ init_kamidori*.py), init_image.py, extract_message_table.py, global_map.py …
    │   │                                       *INIT / ID-dispatched message parsers (Kamidori
    │   │                                       specializations: init_kamidori_<domain>.py),
    │   │                                       global-var map builder
    │   ├── native_scratch.py (+ test_native_scratch.py)  gate-6 scratch install (hard-linked archives) + pristine check
    │   ├── revision_diff.py                    compare two engine builds' native code opcode by opcode (+ test_revision_diff.py)
    │   ├── drive.py, drive_core.py (+ test_drive_core.py)  game driver for native AGE and the port: one step
    │   │                                       language, input, screenshots, VM position, sandboxed saves
    │   ├── drive_native.py, drive_port.py      the two targets: Frida daemon for AGE; launcher for the port's
    │   │                                       --control channel
    │   ├── frida_runtime.py                    `frida` with shutdown at exit (removes its %TEMP% folder);
    │   │                                       every Frida tool imports Frida through it
    │   ├── drive_mcp.py (+ test_drive_mcp.py)  MCP server: the driver's operations as five agent tools
    │   ├── drive/                              the driver's data: <profile>.toml (points, sandbox settings,
    │   │                                       native engine pointer / polling / dialog policy),
    │   │                                       native-agent.js (Frida agent), scenarios/*.steps (replayable runs)
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
    │   ├── globals.toml                        ★ CANONICAL Himegari global-variable registry (hand-edited: name + category
    │   │                                        + value_domain + provenance). Single source of truth for globals/story-flags.
    │   ├── globals-kamidori.toml               ★ CANONICAL Kamidori global registry (same schema; addresses are per game)
    │   ├── patch-points-<profile>.toml         ★ CANONICAL named patch points (stable fragment site names, one site per
    │   │                                        script revision; mod loader spec §5); patch_points_build.py generates the rest
    │   └── opcode-leads.json, small-script-listings.md
    │
    ├── oms/<profile>/*.oms                   hand-written Open Maid Script source modules (preludes): `module
    │   │                                       <profile>.<name>` units of declarations, templates and library
    │   │                                       subs that the compiler links and the idiomatic decompiler lifts
    │   ├── himegari/adv.oms                    Himegari ADV prelude: scene library, `@dialogue line`, speaker enum
    │   ├── kamidori/adv.oms                    Kamidori ADV prelude (same parts, plus the name_form enum)
    │   └── examples/                           example scripts, not modules (gate 6: himegari-gate6/,
    │                                           kamidori-gate6/; gate 7 fragment: himegari-patch/; mod loader
    │                                           gate 2 packages: mods/)
    │
    ├── docs/                                 all documentation
    │   ├── PROJECT-STRUCTURE.md                this file (where things live)
    │   ├── tools-reference.md                  every tool: purpose, usage, I/O (operational companion)
    │   ├── modding/                            modder-facing guides (README, first-mod, packages, oms-language,
    │   │                                         patching, new-scenes, cli, troubleshooting); English, plain Markdown;
    │   │                                         generic only: game facts live in modding/games/
    │   │   └── games/                          README.md GENERATED (supported games + identity blocks); <profile>.md per game
    │   ├── backlog.md                          open work items (fidelity gaps, deferred follow-ups, accepted ideas)
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
    │   ├── global-reference-kamidori.md        GENERATED from globals-kamidori.toml
    │   ├── kamidori-data-tables.md             Kamidori *INIT data tables: layering, layouts, semantics
    │   ├── localization-import-test.md         localization import test results (spec §18.1, gates 10/11)
    │   ├── scjump-progression.md               SCJUMP progression decode + SCINIT decision→scene registry
    │   └── engine-re.md                        native-engine RE (Ghidra+MCP): dispatch table, decompiled ops
    │
    ├── build/                               DERIVED (our-work-side) — generated by tools/; disposable
    │   ├── downloads/, toolchains/             immutable dependency caches and repo-local Godot toolchain
    │   ├── export/linux-x64/                   verified loose Godot Linux export
    │   ├── export/windows-x64/                 structurally verified loose Godot Windows export
    │   ├── native/linux-x64/                   validated Linux FFmpeg shim/shared-library bundle
    │   ├── native/win-x64/                     validated Windows FFmpeg shim/DLL bundle
    │   ├── package/                            Linux staged tree/smoke/`.tar.gz` plus Windows x64 staged tree/ZIP
    │   ├── games/<profile-id>/disasm/         <NAME>.asm — human-readable disassembly, one per script
    │   ├── games/<profile-id>/text/           extracted text:
    │   │   ├── <NAME>.strings.txt               all inline strings in a script
    │   │   ├── dialogue.jsonl                   show-text lines only (the translation corpus)
    │   │   └── strings.jsonl                    every string, tagged by source opcode
    │   ├── games/<profile-id>/data/           parsed data tables (*INIT → JSON)
    │   ├── games/<profile-id>/scripts-json/   machine-readable full dumps (on demand via --json)
    │   ├── games/<profile-id>/manifest.json   extract_phase2.py's per-script manifest
    │   ├── games/<profile-id>/engine-dump/    profile-scoped UNPACKED engine dump: hashed ranges + provenance manifest
    │   │                                       (Kamidori also keeps engine-dump-translated/, the translation's 4.33F
    │   │                                       dump that Ghidra's /kamidori image came from)
    │   ├── games/<profile-id>/asset-index.json, asset-sections.json   asset diagnostics (parse_sys4ini / resolve_asset)
    │   ├── games/<profile-id>/textures/       AGF-to-BMP stills (convert_agf.py) - diagnostic pixel oracle only
    │   ├── games/<profile-id>/global-var-map.{json,md}   partial global-variable name map (auto shape inference; feeds the globals merge)
    │   ├── games/<profile-id>/globals.json    GENERATED from the profile's globals registry merged over global-var-map (sys4load labels + C# VM)
    │   ├── games/<profile-id>/story-flags-candidates.json   GENERATED by story_flags.py (ranked story-flag review surface)
    │   ├── games/<profile-id>/scjump-decisions.{json,md}    GENERATED by scjump_decode.py (progression decision table)
    │   ├── games/himegari/captures-2026-07/   July 2026 Frida/VM traces (optrace pair, global writes, scene-entry
    │   │                                       state) kept after newer runs overwrote the per-profile copies
    │   ├── engines/<reference-id>/            a reference-only install's outputs: native-scratch/ (native_scratch.py
    │   │                                       --reference), engine-dump/ + dump-sandbox/ (dump_engine.py --reference
    │   │                                       --spawn), operand-access.json (operand_access.py --engine)
    │   ├── revision-diff/<a>-<b>.{json,md}    revision_diff.py's opcode-by-opcode comparison of two engine builds
    │   ├── opcodes.json                        GENERATED from opcodes.toml (machine view for the C# VM)
    │   └── opcode-coverage.md                  GENERATED from opcodes.toml
    │
    ├── engine/                              DELIVERABLE — the .NET VM/runtime projects and tests (OmeEngine.sln)
    │   ├── Ome.Engine/Profiles/             embedded content-free game manifests plus immutable catalog-
    │                                       identity selection, persistence, runtime seeds, and diagnostic policy
    │   ├── Ome.Engine/Model/                composable ABI-layer/opcode definitions plus retained graphics state and
    │                                       separately navigable render, transition, diagnostic, and persistence contracts
    │   ├── Ome.Engine/Sys4/                 runtime game-root selection, catalog/parser and structured opcode-
    │                                       decode failures, loose-first bounded ALF asset store, script provider,
    │                                       explicit overlay/codec launch policy, AGF/LZSS/BMP and Windows CUR
    │                                       decoders, and resource facade
    │   ├── Ome.Engine/Text/                 backend-neutral glyph-mask requests/results, deterministic AGE
    │                                       compositor/layout, retained live/History glyph and wait-atlas
    │                                       publication, and bounded font/glyph cache primitives
    │   ├── Ome.Engine/Localization/         text catalogs (docs/superpowers/specs/2026-11-10-localization-
    │                                       design.md): placeholder templates, entries, the catalog's global/script/
    │                                       site scopes, the message segmenter (spec §6.2), the presenter with its
    │                                       pattern matcher and overlap check, half-cell measurement, line-break
    │                                       opportunities, history's text split (spec §8), the pseudo-language
    │                                       and the miss log
    │   ├── Ome.Engine/Persistence/          native S3SD/S4SD + S3RT codecs, shared payload/ReadTextDB,
    │                                       layout-3 numbered state/history/gfx, DAT/STH pair + BMP codec,
    │                                       screenshot BMP naming, profile-owned state, identity, and
    │                                       serialized bank dimensions
    │   ├── Ome.Script/                      Open Maid Script toolchain (docs/superpowers/specs/2026-10-06-
    │                                       open-maid-script-design.md): operand contracts, intrinsics (Abi/), intermediate
    │                                       form and byte-exact decoder/encoder (Ir/), lexer/parser/AST (Syntax/),
    │                                       compiler, template expander, expression lowering, temporary allocator and canonical
    │                                       layout rules (Compile/), layout sidecar
    │                                       (Layout/), generated and source modules (Modules/), raw, core and idiomatic decompilers
    │                                       with library/template patterns (Text/), and fragment plans, their compiler,
    │                                       applier and gate-7 probe (Patch/); referenced by Ome.Cli and the tests,
    │                                       not by the Godot runtime
    │   ├── Ome.Mods/                        mod loader (docs/superpowers/specs/2026-11-03-mod-loader-design.md):
    │                                       Open Maid Pack containers (.omp ZIP or folder) with path and size checks,
    │                                       package hash and layout classification (Packages/), manifest.toml reader
    │                                       on Tomlyn plus semantic versions and ranges (Manifest/), and the linker:
    │                                       stack resolution and load order, replacements, plan composition, the
    │                                       spec §7 report, selector allocation, per-mod append catalogs, asset
    │                                       overrides and the link cache (Linking/), startup discovery, flags and
    │                                       save stamps (Launch/), and the omp commands (Tools/); the
    │                                       localization catalog file reader and writer, the text root loader
    │                                       and the script message index (Localization/) and
    │                                       `omp text import-scripts` (Localization/Import/); referenced by
    │                                       Ome.Cli, the Godot port (godot/Main.Mods.cs) and the tests
    │   ├── Ome.Engine.Frontend/             platform-neutral frontend support: diagnostics/options, page
    │                                       location, performance logging, movie runtime/FFmpeg decoding,
    │                                       audio alignment, surface registry, WAV sanitization, the
    │                                       game-driving control protocol (Control/), and the engine
    │                                       shell's model (Shell/: its commands, menu tree with
    │                                       prune/refresh, state, options, string tables in
    │                                       Strings/*.json, the 終了 dialog's texts, IShellHost (the
    │                                       frontend services its commands reach), the options panel's
    │                                       layout, draft and 情報 rows, the engine identity, and the
    │                                       port options with their OME.INI store)
    │   └── Ome.Engine.Text.Windows/         optional exact Windows GDI gray-4 reference backend; kept
    │                                       outside the platform-neutral VM/runtime core
    ├── native/                              authored native runtime boundaries
    │   └── ome_movie_ffmpeg/                project-owned FFmpeg C ABI, immutable Windows/Linux dependency manifests,
    │                                       and native/MSVC plus Linux-hosted MinGW target bootstrap/build scripts;
    │                                       ffmpeg-source.json + build-ffmpeg-sdk.sh build the minimal LGPL FFmpeg SDK;
    │                                       write_ffmpeg_source_notice.py emits each bundle's FFmpeg-SOURCE.txt
    │                                       (outputs stay under disposable build/)
    ├── tools/frida/                         runtime-capture + engine-dump scripts (see tools/frida/README.md)
    └── godot/                               DELIVERABLE — the Godot/C# ADV front-end (references Ome.Engine
                                            plus the optional exact Windows text adapter). Its assembly embeds
                                            generated build/opcodes.json through OmeRuntimeMetadata, so
                                            exports do not need repository discovery. Also contains the portable
                                            TextServer glyph adapter, profile-selected Japanese font policy,
                                            and TITLE-only F4 debug scene launcher
```

Within disposable `build/`, engine-wide generated ABI/reference artifacts (`opcodes.json`, opcode
coverage, and `engine-ctx.json`) remain shared. Every content-derived artifact belongs under
`build/games/<profile-id>/`: catalog indexes, call-script names, disassembly/text corpora, parsed data,
global/progression maps, native engine dumps, traces, page maps, coverage reports, and validation logs. This is an ownership
boundary, not just a naming convention; tools resolve it from the selected immutable profile context.
Native runs live there too: `native-scratch/` (the copied install), and `drive/<native|port>/`. Each target's
`drive/` folder holds the driver session, its logs and screenshots, and `sandbox/<name>/` (native: redirected
LocalAppData/Desktop with the game's saves and `SYS4REG.INI`; port: the `--data-root` profile in the same layout).
`drive/native/backups/` holds copies of the live save folder.
`engine/Ome.Engine/Sys4/Sys4CorpusScanner.cs` owns the production-store script decode/histogram pass;
`engine/Ome.Cli` exposes it as `catalog-scan` for validation and diagnostic reports, and exposes the
`Ome.Script` round trips as `oms-roundtrip` (with `oms-residue` for its literal leftovers and `oms-temps` for its
temporary-allocator residue), decompiled source as `oms-decompile`, and fragments (spec §10) as `oms-patch`. The source modules in `oms/<profile>/` are
authored, like `vm-map/`, not derived: the compiler links them, and `--level idiomatic` lifts their library subs
and templates.

The Godot deliverable includes `OME.sln` because Godot's .NET exporter requires a solution,
`export_presets.cfg` for Linux and Windows x86_64, and `build/.gdignore` so disposable capture frames below
the project do not enter its import or export scan. `tools/build-linux-x64.sh` and
`tools/build-windows-x64.sh` produce their complete disposable artifacts under `build/export/`; the older
Windows-hosted `tools/export-linux-x64.ps1` remains a Linux-export development path.

`godot/Main.cs` retains the front-end's startup and runtime coordination. Behavior-neutral partial-class
companions keep cohesive surfaces independently navigable without changing the Godot node type or invocation
paths: `godot/Main.SelfTest.cs` owns the synthetic threaded/headless regression harness, while
`godot/Main.Audio.cs` owns BGM, voice, sound-effect, mixer-routing/persistence, and audio-bus control, and
`godot/Main.Movie.cs` owns decoder staging, movie frame/audio publication, completion, and teardown.
`godot/Main.Compositor.cs` owns retained/GPU/software composition state, texture resolution and caching,
surface-transition drawing, raster helpers, and compositor decision logging. `godot/Main.Input.cs` owns
Godot input routing, locator/debug hotkeys, debug-scene dispatch, cursor control, native alerts, and
full-width text entry. `godot/Main.Drive.cs` owns the `--control` driving backend: the control server's lifetime,
injected input events, exclusive input, and the port side of the protocol (state, screenshots, speed, close).
`godot/Main.MenuBar.cs` attaches the shell's menu bar (`ShellMenu`, the tree of AGERC's menu 110) above the game
canvas and runs its commands (`ShellCommands`), reaching the frontend's services through `godot/Main.ShellHost.cs`,
Main's `IShellHost`; `godot/ExitConfirmationDialog.cs` is the shell's 終了 dialog;
`godot/NativeMenuBar.cs` draws the bar and its popups and runs its keyboard mode, `godot/WindowSystemMenu.cs`
supplies the window menu at the end of its keyboard cycle, and `godot/GdiTextMetrics.cs` supplies the GDI advances
that the bar and the `godot/Win32Dialog.cs` dialogs lay their labels out with. `godot/Main.ScreenMode.cs` switches
between the window and fullscreen and runs fullscreen's top-edge strip, which `godot/SystemMenuStrip.cs` draws
around a second `NativeMenuBar`. `godot/Main.Screenshot.cs` saves the
F1 and 表示画面を保存 screenshot BMPs and runs 画像の保存場所 through `godot/ScreenshotFolderDialog.cs`, the shell's
folder picker. `godot/Main.OptionsPanel.cs` opens and closes the shell's options panel (設定, ﾊﾞｰｼﾞｮﾝ情報 and
動作環境's replacement), commits its draft and the port options, loads `OME.INI`, and gathers 情報's about and
diagnostics rows; `godot/OptionsPanel.cs` draws the panel. `godot/ShellAssets.cs` loads the shell's own assets, which
`godot/assets/` holds (Godot ignores the folder) and `godot/OME.csproj` embeds in `OME.dll`: Noto Sans JP for the
panel and the OME icon (`ome-icon.svg`) for the window, the strip and 情報. The 終了 dialog and the script error box are
classic Win32 look-alikes (`godot/Win32Dialog.cs`, `Win32DialogFrame.cs`, `Win32ClassicButton.cs`,
`Win32ClassicRadio.cs`, `Win32StaticText.cs`, `Win32DialogIcon.cs`).

`engine/Ome.Engine.Frontend/` is the production owner of frontend helpers that do not depend on Godot types.
Both `engine/OmeEngine.sln` and `godot/OME.sln` include the project; Godot and `Ome.Engine.Tests` consume it
through project references rather than compiling duplicate linked sources. The first owned cluster contains
`PageLocatorState`, `GodotTimelineLog`, `GodotTraceSink`, `StepLimitDiagnosticFormatter`, `GodotVmOptions`,
`PerformanceFrameLog`, and `WindowLaunchOptions`. It also owns the internal movie decoder boundary/runtime,
audio-timeline alignment, movie-surface registry, and RIFF/WAVE sanitizer; only the `Himegari` and
`Ome.Engine.Tests` assemblies receive explicit friend access to that internal API. FFmpeg native interop and the
asynchronous/paced decoder also live here; the `Ome.MovieCorpusGate` tool receives friend access to the native
frame-source seam and references this project rather than compiling source links. Reusable movie-corpus discovery,
decode-gate, and report contracts live here as well; `tools/movie-corpus-gate/Program.cs` remains only the
command-line entry point and output writer. `Control/` holds the game-driving protocol
(`docs/superpowers/specs/2026-11-03-game-driving-design.md`): its JSON model and conditions, the dispatcher that
turns requests into timed input over an `IDriveBackend`, and the loopback `DriveServer`.

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
control, and the background BGM fade's deadline and completion; presentation/input retains the message-skip, reset, and frame-pulse consumers
of that state through the sealed partial class.

`engine/Ome.Engine/Hosting/IHost.cs` remains the aggregate runtime host accepted by the VM and existing host
implementations. `engine/Ome.Engine/Hosting/IDiagnosticHost.cs` owns recoverable-warning and modal-diagnostic
reporting contracts, while `engine/Ome.Engine/Hosting/ILifecycleHost.cs` owns script-context entry/exit, sleep and
timed-deadline waiting, frame yield, and scene reset. `engine/Ome.Engine/Hosting/IAudioHost.cs` owns BGM, voice,
SFX, fade, volume, and route-control contracts, including the compatibility overload/default chains used by
simple hosts. `engine/Ome.Engine/Hosting/IMovieHost.cs` owns ordinary, positioned, mask-transition, activity-query,
and modal movie playback contracts. `engine/Ome.Engine/Hosting/IGraphicsHost.cs` owns mutable surfaces,
retained-range/frame presentation, transitions, texture lifecycle, pixel capture/replace, draw, and size
contracts. `engine/Ome.Engine/Hosting/IInputHost.cs` owns modal fullwidth entry, ADV waits/callback servicing,
input clock, cursor resources, and skip-state interaction. `engine/Ome.Engine/Hosting/IAdvHost.cs` owns live and
surface text, history presentation, ADV layout publication, message presentation settings, wait indicators, and
page-presentation suspension. `IHost` inherits all seven focused contracts; every required member and
overload/default chain remains unchanged. The neutral
`engine/Ome.Engine/Model/MovieMaskTransitionRequest.cs` record carries movie mask work between the VM, host, and
retained graphics without making `Model` depend on `Hosting`.
`engine/Ome.Engine/Model/RgbaImage.cs` owns the format-independent packed RGBA image transported between SYS4
decoders, host surfaces, renderers, persistence, and tests; consumers do not depend on `Sys4` merely to exchange
decoded pixels.
`engine/Ome.Engine/Model/SurfaceContracts.cs` owns neutral surface fill/copy requests and the black-fade direction
shared by VM presentation, graphics hosts, Godot, and tests.
`engine/Ome.Engine/Model/InputContracts.cs` owns neutral ADV auto-wait state and fullwidth text-edit request/result
records shared by input hosts, timing policy, VM dispatch, Godot, and tests.
`engine/Ome.Engine/Model/AdvPresentationContracts.cs` owns neutral ADV wait-indicator configuration shared by VM
dispatch, retained presentation, hosts, Godot, and tests. `engine/Ome.Engine/Model/DiagnosticContracts.cs` owns
the neutral synchronous diagnostic message transported between VM dispatch, diagnostic hosts, Godot, and tests.

`engine/Ome.Engine/Model/GfxState.cs` retains cross-domain retained-graphics coordination.
`engine/Ome.Engine/Model/GfxState.Contracts.cs` owns its public render, transition, diagnostic, persistence,
animation, numeric-glyph, and handle-range contracts. `engine/Ome.Engine/Model/GfxState.Surfaces.cs` owns surface
resource/color-key state, created/reloadable classification, movie stop-time metadata, render-target/tile
configuration, and surface lifecycle operations. `engine/Ome.Engine/Model/GfxState.RetainedObjects.cs` owns the
retained-object record and registry/index, range-transform state, object creation/query/clone/erase and draw
binding, plus numeric-glyph object generation. `engine/Ome.Engine/Model/GfxState.Animation.cs` owns the shared
animation clock, object color/source-cell/matrix/cyclic channels, animation control and forced completion, and
the interpolation helpers consumed by retained-scene sampling. `engine/Ome.Engine/Model/GfxState.Presentation.cs`
owns surface/movie transition queues, presentation activity and click-skip logic, diagnostics and dirty-reason
accounting, and visible retained-scene snapshots.

`engine/Ome.Engine/Vm/VirtualMachine.cs` retains VM execution lifecycle, cross-domain state, and the proven
top-level opcode dispatcher; `Step` now contains only label grouping, domain routing, strict compatibility
failure, and an internal coverage/dispatcher consistency guard. `OpcodeRuntimeCoverage.cs` owns runtime
implementation membership independently of ABI-table membership and corpus observation;
`VmCompatibilityContext.cs` owns selected-profile execution context and structured unsupported-op diagnostics.
Its partial-class companion `engine/Ome.Engine/Vm/VirtualMachine.Audio.cs` owns VM audio
state, BGM restart semantics, and the BGM/voice/SFX/mixer opcode handler; `Step` retains the audio labels and
routes that family into the handler. `engine/Ome.Engine/Vm/VirtualMachine.Movie.cs` owns modal/asynchronous/
positioned movie playback, movie surface metadata/activity queries, and movie-mask transition dispatch; `Step`
likewise retains and routes the movie labels. `engine/Ome.Engine/Vm/VirtualMachine.Surface.cs` owns per-script
reload-policy reset/configuration, surface allocation/loading, texture binding and sizing, mutable surface
fill/copy, render-target control, and individual/transient surface release; its labels remain at their existing
dispatcher positions around the retained numeric-glyph and object cases.
`engine/Ome.Engine/Vm/VirtualMachine.RetainedObjects.cs` owns retained-object registry queries,
default-slot and geometry mutation, direct and range transforms, clone, and erase dispatch; animation, surface,
ADV-binding, and presentation labels remain in their respective dispatcher groups.
`engine/Ome.Engine/Vm/VirtualMachine.Animation.cs` owns retained spritesheet and color channels, timed and cyclic
transforms, per-object animation control, frame-time sampling, and the shared animation-clock opcode handler.
`engine/Ome.Engine/Vm/VirtualMachine.Presentation.cs` owns queued surface-alpha transitions, frame and object-range
publication, skip-aware blocking fades/crossfades, foreground-transition waits, and graphics command-queue clear.
`engine/Ome.Engine/Vm/VirtualMachine.AdvText.cs` owns live ADV text emission, layout/cursor/wait-indicator state,
text style and glyph-delay control, direct surface-string and retained numeric-glyph rendering, and retained
text/wait-object bindings; text history and input/skip/auto services remain separate.
`engine/Ome.Engine/Vm/VirtualMachine.TextHistory.cs` owns history recording control, metadata append/navigation,
retained history rendering, metadata/voice lookup, and history backlog clearing.
`engine/Ome.Engine/Vm/VirtualMachine.HistoryPresentation.cs` presents a history group again in the current language
(localization spec §8): by the lookups recorded this session, else by splitting the records' text against unscoped
entries. `engine/Ome.Engine/Vm/VirtualMachine.TextStores.cs` records which instruction stored each global string, so
a lookup can try the store's site (spec §5.2).
`engine/Ome.Engine/Vm/VirtualMachine.AdvServices.cs` owns persistent/active message-skip control, read-skip
settings and queries, auto-message state/timing, and per-message voice/skip reset opcode dispatch; shared state
and refresh helpers remain in the VM coordinator because live text and input paths also consume them.
`engine/Ome.Engine/Vm/VirtualMachine.Input.cs` owns blocking ADV waits, hotspot registration/arming, cursor
resources and virtual position, raw mouse/joystick callback registration and dispatch, action polling, and
physical-input mapping; public host-thread input entry points and shared synchronization remain in the coordinator.
`engine/Ome.Engine/Vm/VirtualMachine.Timing.cs` owns the monotonic-time query, host sleep, relative timed-callback
schedule construction, deadline/catch-up selection, and callback resumption opcode handler.
`engine/Ome.Engine/Vm/VirtualMachine.Persistence.cs` owns catalog-unlock lookup, numbered save/load and nested
restore continuation, metadata/copy/delete, thumbnail persistence, and shared-profile integer/string opcode
dispatch; capture/apply helpers and persistent coordinator state remain in `VirtualMachine.cs`.
`engine/Ome.Engine/Persistence/NativeGfxSaveLayout.cs` owns the supported native graphics-record layouts
used by the shared numbered-payload and semantic graphics codecs. The profile selects the layout through
`NativeSaveIdentity`, including first-time saves; exact format details live in
[SYS4 format notes](sys4-format-notes.md#kamidori-320-layout).
`engine/Ome.Engine/Vm/VirtualMachine.MemoryCollections.cs` owns string byte length, addressed lookup/copy,
inline arrays, rectangle search and stable index sorting, bounded integer queues/stacks, bit/range operations,
and native-style random-modulo dispatch; shared storage/address helpers remain in the coordinator.
`engine/Ome.Engine/Vm/VirtualMachine.ControlFlow.cs` owns local jumps/calls/returns, value-switch construction,
ADV coroutine handler save/yield/resume, and bounded labeled-yield dispatch; process/root exit and cross-script
lifecycle are routed separately. `engine/Ome.Engine/Vm/VirtualMachine.InputLatch.cs` owns native's input latch: the
triggers held while nothing polls (effects under `EffectSkipOnClick` 0, the ADV coroutine yield), the services that
take them, and the discard points. `engine/Ome.Engine/Vm/VirtualMachine.PhysicalFastForward.cs` owns fast-forward
from a held action 6 (Ctrl): its two start paths, op `0x101`'s reveal suppression and the clears.
`engine/Ome.Engine/Vm/VirtualMachine.DeterministicRandom.cs` owns op `0x60`'s seeded per-call-site streams, which
the drive step `rand-seed` turns on.
`engine/Ome.Engine/Vm/VirtualMachine.ScriptLifecycle.cs` owns process/frame/root
exit, native run-state compatibility, initial-root-run query, ordinary cross-script calls, mounted append autoruns,
and preloaded script-slot load/call dispatch; frame execution, script-provider access, and shared lifecycle state
remain in `VirtualMachine.cs`.
`engine/Ome.Engine/Vm/VirtualMachine.Values.cs` owns integer arithmetic, bitwise and comparison operations,
string comparison/concatenation/conversion/move, native byte-length and CP932 operations, and the host-backed
fullwidth string editor; shared operand storage, addressing, and native-string encoding remain in the coordinator.
`engine/Ome.Engine/Vm/VirtualMachine.Diagnostics.cs` owns diagnostic value/newline accumulation and synchronous
show-and-clear opcode dispatch; shared diagnostic state and native-context formatting remain in the coordinator.
`engine/Ome.Engine/Vm/VirtualMachine.RuntimeSettings.cs` owns message-window alpha and system-menu enable/show
delay opcode dispatch; reset/default initialization, public menu-state accessors, and host state remain centralized.

The disposable `build/games/<profile>/page-map-<SCENE>.jsonl` files are produced by editor/development Godot runs
and map runtime ADV page ordinals to their authoritative script offsets for `tools/locate_page.py`. Packaged exports
have no repository output tree and write their automatic maps below
`user://games/<profile-id>/diagnostics/page-maps` instead.

A profile's `mods/` folder in its data root (`user://games/<profile-id>/mods/`, or the `--data-root` sandbox) holds
the user's packages, `selectors.toml` (mod id → append-catalog selector, kept until `--forget-mod`), and `.cache/<key>/`,
the disposable link result: `loose/` (patched and replaced scripts, asset overrides; first loose root),
`catalogs/APPENDnn.AAI`/`.ALF` (one per mod with new scripts or assets, mounted after the install's own), `link.json`
(order, selectors, which mod supplied each file and record) and `mod-link.txt` (the report). See the mod loader
spec §6.2–§6.5.

## Conventions

- **Three-way separation.** `Himegari_Game/` = untouched originals; `extracted/` =
  game-derived data (regenerable, game-side); `age-reimpl/` = everything we authored. The first two
  are consumed, never modified.
- **Tools never hard-code paths.** `tools/paths.py` resolves an immutable context from common
  `--profile`, `--game-root`, and `--extracted-root` options (or matching `OME_*` variables), with
  conventional Himegari defaults. Switching profiles or relocating the tree requires no source edits.
- **Path references in docs** are `age-reimpl/`-relative (e.g. `tools/sys4load.py`,
  `build/games/himegari/text/dialogue.jsonl`) unless they name a game/extracted path explicitly.
- **Authoritative script copies:** where a script exists both as a loose `.BIN` in the game folder
  and under `extracted/DATA1/`, the game-folder copy (patch v1.03) wins. `paths.scripts()` resolves
  this automatically (overrides win).
- **`build/` and extracted roots are disposable.** Shared engine ABI artifacts live directly under
  `build/`; selected game outputs regenerate under `build/games/<profile-id>/` without colliding.
  Extracted roots regenerate from the `.ALF` files with a separately obtained local extractor.
  `bin/README.md` records the optional local convention; no extractor binary is tracked. Safe to delete
  and rebuild; do not hand-edit.
- **Opcode knowledge is edited ONLY in `vm-map/opcodes.toml`** (catalog framing, per-profile observation,
  evidence-scoped ABI snapshots/layers, revision-scoped structured evidence, semantics, and dependencies).
  Current revision snapshots resolve directly; the schema supports future parent/add/replace/remove composition
  without asserting a base SYS4 lineage. Runtime implementation remains owned by
  `OpcodeRuntimeCoverage.cs`. Run `tools/opcodes_build.py --build` to regenerate both Python views
  (`tools/age_opcodes.py`, `tools/age_opcode_semantics.py`), machine JSON (`build/opcodes.json`), reference
  (`docs/opcode-reference.md`), and union/per-profile coverage. `--lint` also checks profile/revision/evidence
  references, dangling deps, confidence ceilings, and vocabulary.
  Both Python modules are generated and must not be hand-edited.
- **Global-variable knowledge is edited ONLY in the profile's registry** — `vm-map/globals.toml` for
  Himegari, `vm-map/globals-<profile>.toml` for any other game (name + category + value_domain +
  provenance). Run `tools/globals_build.py [--profile ID] --build` to regenerate that profile's
  `build/games/<profile>/globals.json` (sys4load labels) and `docs/global-reference[-<profile>].md`; `--lint` checks vocabulary / auto-shape≠high / dangling deps. Curated entries
  override the auto shape map (`build/games/<profile>/global-var-map.json`). Discover story-flag candidates with `tools/story_flags.py`.
- **Named patch points are edited ONLY in `vm-map/patch-points-<profile>.toml`.** Run
  `tools/patch_points_build.py [--profile ID] --build` to validate them against the installed base and regenerate
  `build/games/<profile>/patch-points.json` (read by `Ome.Cli oms-patch`) and `docs/patch-point-reference[-<profile>].md`.
- **Encoding:** all generated text is UTF-8 (source strings are cp932/Shift-JIS, decoded on
  extraction). Run Python as `py -3.11 -X utf8`.
- **The game install is a runnable unit** — do not relocate `AGE.EXE`/`*.ALF`/DLLs relative to each
  other, or the game (and any Frida work) breaks.
