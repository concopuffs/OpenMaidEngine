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
    │   ├── globals_build.py                    generator/linter: vm-map/globals[-<game>].toml (+auto map) -> build/games/<game>/globals.json, docs/global-reference[-<game>].md
    │   ├── modding_games_build.py              generator: profile manifests + registries + oms/<game>/ -> docs/modding/games/README.md
    │   ├── patch_points_build.py               validator/generator: vm-map/patch-points-<game>.toml -> build/games/<game>/patch-points.json, docs/patch-point-reference[-<game>].md
    │   ├── base_mod_build.py                   generator: global and patch-point registries -> mods/<game>-base/layout/ (rename rule against exports.txt)
    │   ├── story_flags.py                      static story-flag miner (branch-condition) -> build/games/<game>/story-flags-candidates.json; --bootstrap
    │   ├── scjump_decode.py                    decode SCJUMP progression logic -> build/games/<game>/scjump-decisions.* ; --verify (VM cross-check)
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
    │   ├── drive/                              the driver's data: <game>.toml (points, sandbox settings,
    │   │                                       native engine pointer / polling / dialog policy, hook values
    │   │                                       per AGE.EXE build), native-agent.js (Frida agent),
    │   │                                       scenarios/*.steps (replayable runs on both targets),
    │   │                                       scenarios/port/*.steps (port-only runs), scenarios/geometry/
    │   │                                       (native-vs-port pairs of localization gate 12)
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
    │   ├── patch-points-<game>.toml         ★ CANONICAL named patch points (stable fragment site names, one site per
    │   │                                        script revision; mod loader spec §5); patch_points_build.py generates the rest
    │   └── opcode-leads.json, small-script-listings.md
    │
    ├── oms/<game>/*.oms                   hand-written Open Maid Script source modules (preludes): `module
    │   │                                       <game>.<name>` units of declarations, templates and library
    │   │                                       subs that the compiler links and the idiomatic decompiler lifts
    │   ├── himegari/adv.oms                    Himegari ADV prelude: scene library, `@dialogue line`, speaker enum
    │   ├── kamidori/adv.oms                    Kamidori ADV prelude (same parts, plus the name_form enum)
    │   └── examples/                           example scripts, not modules (gate 6: himegari-gate6/,
    │                                           kamidori-gate6/; gate 7 fragment: himegari-patch/; mod loader
    │                                           gate 2 packages: mods/)
    │
    ├── mods/<game>-base/                  the base mods the engine ships (Lua scripting spec §9; embedded in
    │   │                                       Ome.Mods, mounted when an enabled mod requires one): manifest.toml,
    │   │                                       README.md, events/ and lua/ hand-written; layout/ generated by base_mod_build.py
    │   ├── events/                             the game events the base mod declares (*.toml: name, point, typed
    │   │                                       fields over named cells; spec §9.2)
    │   ├── lua/                                init.lua: the helpers require gives (give_item), each calling a game
    │   │                                       script its manifest's [lua] calls lists (spec §9.3, §10)
    │   └── layout/                             ⚙ globals.json (every named global's bank, address, shape, columns and
    │                                           aliases), points.json (the patch points' copy), exports.txt (the
    │                                           exported names, the API the rename rule checks)
    │
    ├── docs/                                 all documentation
    │   ├── PROJECT-STRUCTURE.md                this file (where things live)
    │   ├── tools-reference.md                  every tool: purpose, usage, I/O (operational companion)
    │   ├── modding/                            modder-facing guides (README, first-mod, packages, oms-language,
    │   │                                         patching, new-scenes, translating, languages, cli, troubleshooting);
    │   │                                         English, plain Markdown;
    │   │                                         generic only: game facts live in modding/games/
    │   │   └── games/                          README.md GENERATED (supported games + identity blocks); <game>.md per game
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
    │   ├── games/<game>/disasm/         <NAME>.asm — human-readable disassembly, one per script
    │   ├── games/<game>/text/           extracted text:
    │   │   ├── <NAME>.strings.txt               all inline strings in a script
    │   │   ├── dialogue.jsonl                   show-text lines only (the translation corpus)
    │   │   └── strings.jsonl                    every string, tagged by source opcode
    │   ├── games/<game>/data/           parsed data tables (*INIT → JSON)
    │   ├── games/<game>/scripts-json/   machine-readable full dumps (on demand via --json)
    │   ├── games/<game>/manifest.json   extract_phase2.py's per-script manifest
    │   ├── games/<game>/engine-dump/    profile-scoped UNPACKED engine dump: hashed ranges + provenance manifest
    │   │                                       (Kamidori also keeps engine-dump-translated/, the translation's 4.33F
    │   │                                       dump that Ghidra's /kamidori image came from)
    │   ├── games/<game>/asset-index.json, asset-sections.json   asset diagnostics (parse_sys4ini / resolve_asset)
    │   ├── games/<game>/textures/       AGF-to-BMP stills (convert_agf.py) - diagnostic pixel oracle only
    │   ├── games/<game>/global-var-map.{json,md}   partial global-variable name map (auto shape inference; feeds the globals merge)
    │   ├── games/<game>/globals.json    GENERATED from the profile's globals registry merged over global-var-map (sys4load labels + C# VM)
    │   ├── games/<game>/story-flags-candidates.json   GENERATED by story_flags.py (ranked story-flag review surface)
    │   ├── games/<game>/scjump-decisions.{json,md}    GENERATED by scjump_decode.py (progression decision table)
    │   ├── games/himegari/captures-2026-07/   July 2026 Frida/VM traces (optrace pair, global writes, scene-entry
    │   │                                       state) kept after newer runs overwrote the per-profile copies
    │   ├── engines/<reference-id>/            a reference-only install's outputs: native-scratch/ (native_scratch.py
    │   │                                       --reference), engine-dump/ + dump-sandbox/ (dump_engine.py --reference
    │   │                                       --spawn), operand-access.json (operand_access.py --engine)
    │   ├── revision-diff/<a>-<b>.{json,md}    revision_diff.py's opcode-by-opcode comparison of two engine builds
    │   ├── opcodes.json                        GENERATED from opcodes.toml (machine view for the C# VM)
    │   ├── opcode-flow.json                    opcode_flow.py's derived control facts, with the methods behind each
    │   └── opcode-coverage.md                  GENERATED from opcodes.toml
    │
    ├── engine/                              DELIVERABLE — the .NET VM/runtime projects and tests (OmeEngine.sln)
    │   ├── Ome.Engine/Profiles/             embedded content-free game manifests plus immutable catalog-
    │                                       identity selection, persistence, runtime seeds, and diagnostic policy
    │   ├── Ome.Engine/Model/                composable ABI-layer/opcode definitions plus retained graphics state and
    │                                       separately navigable render, transition, diagnostic, and persistence contracts
    │   ├── Ome.Engine/Sys4/                 runtime game-root selection, catalog/parser and structured opcode-
    │                                       decode failures, loose-first bounded ALF asset store, script provider,
    │                                       explicit overlay/codec launch policy, AGF/LZSS/BMP/PNG and Windows CUR
    │                                       decoders, the language-asset store, and resource facade
    │   ├── Ome.Engine/Text/                 deterministic AGE compositor/layout, retained live/History glyph
    │                                       and wait-atlas publication, bounded font/glyph cache primitives (the
    │                                       cache narrows masks), and presented text's line breaking, alignment
    │                                       and fit
    │   ├── Ome.Engine/Localization/         text catalogs (docs/superpowers/specs/2026-11-10-localization-
    │                                       design.md): placeholder templates, entries, the catalog's global/script/
    │                                       site scopes, the message segmenter (spec §6.2), the presenter with its
    │                                       pattern matcher and overlap check, half-cell measurement, line-break
    │                                       opportunities, history's text split (spec §8), the pseudo-language
    │                                       and the miss log
    │   ├── Ome.Engine/Modding/              the Lua runtime seam (superpowers/specs/2026-11-10-lua-scripting-
    │                                       design.md §14.2): IModRuntime and IModSession, the stack and its
    │                                       sources, the hook plan and each script's table, the hook context,
    │                                       IScriptAccess, the checked face each mod's Lua reaches the engine
    │                                       through, a base mod's layout and the accessors' rules (GameLayout,
    │                                       LayoutCells), and game events with their field locations (GameEvents)
    │   ├── Ome.Engine/Persistence/          native S3SD/S4SD + S3RT codecs, shared payload/ReadTextDB,
    │                                       layout-3 numbered state/history/gfx, DAT/STH pair + BMP codec,
    │                                       screenshot BMP naming, profile-owned state, identity, and
    │                                       serialized bank dimensions
    │   ├── Ome.Engine/Natives/              engine natives (engine layering spec §5.2), each the one owner of a
    │                                       group of engine state: SkipAndAuto (Skip, Auto, read skip, fast-forward
    │                                       and its pace), Audio (the 13 channels, the BGM track and envelope, the
    │                                       voice, delayed starts, the mixer), EngineTick (the tick and the
    │                                       statement block), InputAndWaits (the waits, the routing of the front
    │                                       end's input, the engine thread's frame turns and the front end's
    │                                       requests) with StopAndReload, Graphics (the retained objects, the surface table
    │                                       and its pixels, publication, the screen transition, the frame lowering)
    │                                       Movies (the bindings of movies to surfaces, the decoders, their
    │                                       sound, completion, the mask transitions), ScriptMemory (the five
    │                                       banks, the queues and stacks, op 0x60's seeded draws), SavesAndSettings
    │                                       (the shared profile, the numbered saves, the playtime, SYS4REG.INI),
    │                                       ShellServices (the system-menu fields, the menu and callback requests,
    │                                       the diagnostic text, the dialogs, the screen mode), TextManager (the
    │                                       layouts and History, the style, the reveal, the wait indicator, the
    │                                       message settings, the fonts, the presenter, the string stores) and
    │                                       Scripting (the Lua session, the hook dispatch's handler calls, budgets
    │                                       and the error policy, lua.log and the notices, the console, the mods'
    │                                       state beside each save)
    │   ├── Ome.Engine.Vm/                   the handler layer (superpowers/specs/2026-11-12-engine-layering-design.md
    │                                       §3.3): the interpreter, the handler table and handlers, and the engine
    │                                       builds; references Ome.Engine only and cannot see Ome.Engine.Devices
    │   ├── Ome.Engine.Devices/              the device layer: interfaces of what an operating system or front end
    │                                       does and the plain data they carry (RgbaImage, glyph-mask requests and
    │                                       results, the font family source, the movie decoder, the pace clock, the
    │                                       audio device and the audio stream device, the frame description and its
    │                                       renderer and presenter, the window);
    │                                       references nothing
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
    │                                       package hash and layout classification, a base mod's layout read at open,
    │                                       and the base mods the engine ships (ShippedPackages) (Packages/), manifest.toml reader
    │                                       on Tomlyn plus semantic versions and ranges (Manifest/), and the linker:
    │                                       stack resolution and load order, replacements, plan composition, the
    │                                       spec §7 report, selector allocation, per-mod append catalogs, asset
    │                                       overrides, the stack's text, fonts, languages and language assets, the
    │                                       Lua link (each mod's checked sources, handlers, events and the hook
    │                                       plan, ModLuaLinker), offset
    │                                       tables for patched scripts and the link cache, and --lua-reload's
    │                                       check of a mod's changed lua/ (ModLuaReload) (Linking/), startup
    │                                       discovery, flags, save stamps and --lua-reload's folder watcher
    │                                       (LuaReloadWatcher) (Launch/), and the omp and omp text commands (Tools/);
    │                                       the localization catalog file reader and writer, the text source loader
    │                                       and site binder, the script message index, the installed-script text
    │                                       scanner, the template extractor, the font cmap reader and the gettext
    │                                       PO reader and writer (Localization/) and `omp text import-scripts`
    │                                       (Localization/Import/); referenced by
    │                                       Ome.Cli, the Godot port (godot/Main.Mods.cs) and the tests
    │   ├── Ome.Engine.Frontend/             platform-neutral frontend support: diagnostics/options, page
    │                                       location, performance logging, FFmpeg movie decoding (the
    │                                       decoder the movie native opens), WAV sanitization, the
    │                                       game-driving control protocol (Control/), and the engine
    │                                       shell's model (Shell/: its commands, menu tree with
    │                                       prune/refresh, state, options, string tables in
    │                                       Strings/*.json with the mods' engine text in front, the 終了
    │                                       dialog's texts, AGE's boxes and the name entry
    │                                       (ShellBoxes), IShellHost (the
    │                                       frontend services its commands reach), the options panel's
    │                                       layout, draft and 情報 rows, the engine identity, and the
    │                                       port options with their OME.INI store)
    │   ├── Ome.Engine.Text.Windows/         optional exact Windows GDI gray-4 reference backend; kept
    │                                       outside the platform-neutral VM/runtime core
    │   ├── Ome.Scripting.Lua/               the Lua runtime (Lua scripting spec §14.1) over Lua-CSharp, the only
    │                                       project that names the library: the check (parse only), the session
    │                                       with one environment per mod, the sandbox, the module loader, the maid
    │                                       bindings and read-only views, the accessors over a base mod's
    │                                       layout (NameViews: g, table, rows), game event fields and
    │                                       maid.events.emit, the calling rules and budgets (a nested call is its own)
    │                                       (superpowers/plans/2026-11-14-lua-scripting.md §3.1), LuaStateCodec
    │                                       (maid.state to the state file's JSON and back), and LuaSource, the
    │                                       library's hexadecimal-numeral workaround; references Ome.Engine only
    │   └── Ome.Scripting.Lua.Spike/         milestone 0 of the Lua design (superpowers/plans/2026-11-14-lua-
    │                                       scripting.md §1): a console program that checks LuaCSharp and
    │                                       MoonSharp against spec §5.1's criteria, one child process per case;
    │                                       results to disposable build/lua-spike/; and the conformance run (§5):
    │                                       the official Lua 5.2 test suite and a probe, results to disposable
    │                                       build/lua-conformance/; not in OmeEngine.sln
    ├── launcher/                            the launcher (superpowers/specs/2026-11-15-launcher-design.md)
    │   └── Ome.Launcher.Spike/              milestone 0 of the launcher plan (superpowers/plans/2026-11-15-
    │                                       launcher.md §2): a bare Avalonia window plus --check-like, --capture
    │                                       and --lock modes; its own global.json picks the .NET 10 SDK; measure/
    │                                       holds the scripts that publish and time it on Windows and in WSL and
    │                                       probe the release engine, results to disposable build/launcher-m0/;
    │                                       in no solution, never shipped
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
`build/games/<game>/`: catalog indexes, call-script names, disassembly/text corpora, parsed data,
global/progression maps, native engine dumps, traces, page maps, coverage reports, and validation logs. This is an ownership
boundary, not just a naming convention; tools resolve it from the selected immutable profile context.
Native runs live there too: `native-scratch/` (the copied install), and `drive/<native|port>/`. Each target's
`drive/` folder holds the driver session, its logs and screenshots, and `sandbox/<name>/` (native: redirected
LocalAppData/Desktop with the game's saves and `SYS4REG.INI`; port: the `--data-root` profile in the same layout).
`drive/native/backups/` holds copies of the live save folder.
`engine/Ome.Engine/Sys4/Sys4CorpusScanner.cs` owns the production-store script decode/histogram pass;
`engine/Ome.Cli` exposes it as `catalog-scan` for validation and diagnostic reports, and exposes the
`Ome.Script` round trips as `oms-roundtrip` (with `oms-residue` for its literal leftovers and `oms-temps` for its
temporary-allocator residue), decompiled source as `oms-decompile`, and fragments (spec §10) as `oms-patch`. The source modules in `oms/<game>/` are
authored, like `vm-map/`, not derived: the compiler links them, and `--level idiomatic` lifts their library subs
and templates.

The Godot deliverable includes `OME.sln` because Godot's .NET exporter requires a solution,
`export_presets.cfg` for Linux and Windows x86_64, and `build/.gdignore` so disposable capture frames below
the project do not enter its import or export scan. Every `godot/*.cs` has a tracked `.cs.uid` sidecar (Godot's
script id): add, move or delete it with its source, since `tools/test_release_workflow.py` checks that the two sets
match. A headless editor scan (`<Godot console> --headless --editor --quit --path godot`) writes a missing one.
`tools/build-linux-x64.sh` and
`tools/build-windows-x64.sh` produce their complete disposable artifacts under `build/export/`; the older
Windows-hosted `tools/export-linux-x64.ps1` remains a Linux-export development path.

`godot/Main.cs` retains the front-end's startup and runtime coordination. Behavior-neutral partial-class
companions keep cohesive surfaces independently navigable without changing the Godot node type or invocation
paths: `godot/Main.SelfTest.cs` owns the synthetic threaded/headless regression harness, while
`godot/Main.Audio.cs` is the audio device (`IAudioDevice`: one stream player per channel, the buses, the ended
reports) and persists the mixer settings, and
`godot/GodotAudioStreams.cs` is the stream device the movie native plays a movie's sound through
(`IAudioStreamDevice`: an `AudioStreamGenerator` player per stream, on its bus); the decoder is
`FfmpegMovieDecoderFactory` (`engine/Ome.Engine.Frontend/`), and `Main.cs` runs the movie native's frame service.
`godot/Main.Compositor.cs` draws each frame: the graphics native lowers it to a frame description, the GPU renderer
(`godot/GpuRetainedRenderer.cs`, an `IFrameRenderer`) draws it or declines it, and otherwise the reference renderer
(`engine/Ome.Engine/Hosting/ReferenceRenderer.cs`, below) draws it and Main, the `IFramePresenter`, uploads its pixels.
It also runs the driver's `reference-diff`, which draws every frame with the reference too and compares the two.
`godot/Main.FrontEnd.cs` is Main's cursor device (`ICursorDevice`) and its dialogs (`IShellDialogs`).
`godot/Main.Input.cs` owns
Godot input routing, locator/debug hotkeys, debug-scene dispatch, cursor control, native alerts, and
full-width text entry. `godot/Main.Drive.cs` owns the `--control` driving backend: the control server's lifetime,
injected input events, exclusive input, and the port side of the protocol (state, screenshots, speed, close).
`godot/Main.LuaReload.cs` owns `--lua-reload`: the watcher over the unpacked Lua mods' `lua/` folders and the hand-over
of their checked files to the scripting native through the waits native's requests.
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
`GodotTraceSink`, `StepLimitDiagnosticFormatter`, `GodotVmOptions` and `WindowLaunchOptions`; the logs the host and
the renderers write (`PageLocatorState`, `TimelineLog`, `PerformanceFrameLog`, and `GfxDecisionLog` for `--gfx-log`)
are in `engine/Ome.Engine/Diagnostics/`. It also owns the RIFF/WAVE sanitizer (the movie runtime and the
movie sound's alignment are the movie native's since engine layering milestone 8; the decoder interfaces are in
`Ome.Engine.Devices`); only the
`OME` and `Ome.Engine.Tests` assemblies receive explicit friend access to that internal API. FFmpeg native interop and the
asynchronous/paced decoder also live here; the `Ome.MovieCorpusGate` tool receives friend access to the native
frame-source seam and references this project rather than compiling source links. Reusable movie-corpus discovery,
decode-gate, and report contracts live here as well; `tools/movie-corpus-gate/Program.cs` remains only the
command-line entry point and output writer. `Control/` holds the game-driving protocol
(`docs/superpowers/specs/2026-11-03-game-driving-design.md`): its JSON model and conditions, the dispatcher that
turns requests into timed input over an `IDriveBackend`, and the loopback `DriveServer`.

The VM reaches the input and waits native through `engine/Ome.Engine/Natives/IInputAndWaits.cs`, which the native
implements (`Natives/InputAndWaits.Interpreter.cs`): the diagnostics and script errors, the script context, sleeps and
the frame yield, the scene reset, the modal movie, the presentation waits and fades, the ADV waits and callback
servicing, the input clock and the cursor. Engine layering milestone 12 removed the lifted host (`EngineHost`) and its
interfaces (`docs/superpowers/specs/2026-11-12-engine-layering-design.md`, §5.2). `Hosting/HeadlessWaits.cs` stands
in for the native where nothing waits (the CLI, the self-test, the tests): it records what it is asked and blocks on
nothing. `Hosting/IScriptProvider.cs` is the scripts the interpreter loads, with `ScriptMap` for a fixed set.
`Natives/PresentationReason.cs` names why a frame is drawn again. `Hosting/ReferenceRenderer.cs` is the reference
renderer (spec §6.3): it draws a frame description into its own RGBA buffer, which the front end shows, and defines
what each frame item means; a render-target publication draws with its rules. `Hosting/BlurRangeSources.cs` is the
blur-range rule the lowering uses. The save thumbnails go through `engine/Ome.Engine/Persistence/
NumberedSaveThumbnails.cs`, over the graphics native's surfaces.

The neutral
`engine/Ome.Engine/Model/MovieMaskTransitionRequest.cs` record carries movie mask work between the VM, host, and
retained graphics without making `Model` depend on `Hosting`.
`engine/Ome.Engine.Devices/Model/RgbaImage.cs` owns the format-independent packed RGBA image transported between
SYS4 decoders, host surfaces, renderers, persistence, and tests; consumers do not depend on `Sys4` merely to exchange
decoded pixels.
`engine/Ome.Engine/Model/SurfaceContracts.cs` owns neutral surface fill/copy requests and the black-fade direction
shared by VM presentation, graphics hosts, Godot, and tests.
`engine/Ome.Engine/Model/InputContracts.cs` owns neutral ADV auto-wait state and fullwidth text-edit request/result
records shared by input hosts, timing policy, VM dispatch, Godot, and tests.
`engine/Ome.Engine/Model/AdvPresentationContracts.cs` owns neutral ADV wait-indicator configuration shared by VM
dispatch, retained presentation, hosts, Godot, and tests. `engine/Ome.Engine/Model/DiagnosticContracts.cs` owns
the neutral synchronous diagnostic message transported between VM dispatch, diagnostic hosts, Godot, and tests.
`engine/Ome.Engine/Model/AgeBoxText.cs` owns the keyed Japanese of AGE's own boxes (the interpreter error box and op
`0x1b4`'s box) and composes a box from its parts in any language; the shell's tables take its keys (localization
spec §11).

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

`engine/Ome.Engine.Vm/` is the handler layer's project: it references `Ome.Engine` only, with
`DisableTransitiveProjectReferences`, so handler code cannot name a device type (`ProjectBoundaryTests`).
`engine/Ome.Engine.Vm/VirtualMachine.cs` retains VM execution lifecycle, cross-domain state, the instruction loops
and dispatch: a step indexes the handlers bound to the table's opcodes at construction and calls one, which returns a
`StepResult` (`StepResult.cs`). `VirtualMachine.Handlers.cs` is the handler table, each opcode's name (its registry
semantic name, else its label) to its handler; it is the only list of implemented opcodes. The handlers (`Op…`
methods) sit in the partials by family. `engine/Ome.Engine/Diagnostics/UnsupportedOpcodeDiagnostic.cs` is the
structured diagnostic for an opcode with no handler. `EngineBuilds.cs` declares every engine
build the port reproduces (4.24J, 4.33G) as its parent plus changes, and the VM takes the build's rules; the rule
types sit beside the natives that read them (`Text/TextRules.cs`, `Model/GfxRules.cs`, `Persistence/SettingsRules.cs`,
`Persistence/SaveRules.cs`; `superpowers/specs/2026-11-12-engine-layering-design.md` §7).
Its partial-class companion `engine/Ome.Engine.Vm/VirtualMachine.Audio.cs` holds the BGM, voice, SFX and mixer
handlers, each a call to the audio native. `engine/Ome.Engine.Vm/VirtualMachine.Movie.cs` owns modal/asynchronous/
positioned movie playback, movie surface metadata/activity queries, and movie-mask transition dispatch; `Step`
likewise retains and routes the movie labels. `engine/Ome.Engine.Vm/VirtualMachine.Surface.cs` owns per-script
reload-policy reset/configuration, surface allocation/loading, texture binding and sizing, mutable surface
fill/copy, render-target control, and individual/transient surface release; its labels remain at their existing
dispatcher positions around the retained numeric-glyph and object cases.
`engine/Ome.Engine.Vm/VirtualMachine.RetainedObjects.cs` owns retained-object registry queries,
default-slot and geometry mutation, direct and range transforms, clone, and erase dispatch; animation, surface,
ADV-binding, and presentation labels remain in their respective dispatcher groups.
`engine/Ome.Engine.Vm/VirtualMachine.Animation.cs` owns retained spritesheet and color channels, timed and cyclic
transforms, per-object animation control, frame-time sampling, and the shared animation-clock opcode handler.
`engine/Ome.Engine.Vm/VirtualMachine.Presentation.cs` owns queued surface-alpha transitions, frame and object-range
publication, skip-aware blocking fades/crossfades, foreground-transition waits, and graphics command-queue clear.
`engine/Ome.Engine.Vm/VirtualMachine.AdvText.cs` holds the text handlers: the show-text ops, layouts, cursors, style,
glyph delay, surface strings, the wait indicator's ops and the message a run belongs to (the VM scene's `PresentedMessage`); each
calls the text native. `engine/Ome.Engine.Vm/VirtualMachine.TextHistory.cs` holds the History handlers (recording
control, metadata append/navigation, rendering a group, voice lookup, clearing the backlog).
`engine/Ome.Engine.Vm/VirtualMachine.TextStores.cs` resolves the global string cell an operand names, for the string
stores the text native keeps (localization spec §5.2).
The text native `engine/Ome.Engine/Natives/TextManager.cs` owns the layouts and History (`AdvTextHistory`), the style,
the glyph rasterizers and caches over its devices (`TextDevices`), the installed fonts, MessageSpeed, MesWinAlpha and
UseAntiFont, op `0x9`'s reset and its records of a numbered save (the history tail and the source glyph objects). Its
partials draw runs, strings and History groups into the layouts' surfaces (`Layout`), run the show-text runs and the
reveal (`Reveal`; `IsRevealing` is run-state `0x20000000`), keep the wait indicator and the page's suspension
(`WaitIndicator`), and hold the presenter, the string stores and History presented again in the current language
(`Presentation`, localization spec §8). `ITextListener.cs` is what a listener hears it show and draw.
`engine/Ome.Engine.Vm/VirtualMachine.AdvServices.cs` owns persistent/active message-skip control, read-skip
settings and queries, auto-message state/timing, and per-message voice/skip reset opcode dispatch; shared state
and refresh helpers remain in the VM coordinator because live text and input paths also consume them.
`engine/Ome.Engine.Vm/VirtualMachine.Input.cs` owns blocking ADV waits, hotspot registration/arming, cursor
resources and virtual position, raw mouse/joystick callback registration and dispatch, action polling, and
physical-input mapping; public host-thread input entry points and shared synchronization remain in the coordinator.
`engine/Ome.Engine.Vm/VirtualMachine.Timing.cs` owns the monotonic-time query, host sleep, relative timed-callback
schedule construction, deadline/catch-up selection, and callback resumption opcode handler.
`engine/Ome.Engine.Vm/VirtualMachine.Persistence.cs` owns catalog-unlock lookup, numbered save/load and nested
restore continuation, metadata/copy/delete, thumbnail persistence, and shared-profile integer/string opcode
dispatch; capture/apply helpers and persistent coordinator state remain in `VirtualMachine.cs`.
`engine/Ome.Engine/Persistence/NativeGfxSaveLayout.cs` owns the supported native graphics-record layouts
used by the shared numbered-payload and semantic graphics codecs. The profile selects the layout through
`NativeSaveIdentity`, including first-time saves; exact format details live in
[SYS4 format notes](sys4-format-notes.md#kamidori-320-layout).
`engine/Ome.Engine.Vm/VirtualMachine.MemoryCollections.cs` owns string byte length, addressed lookup/copy,
inline arrays, rectangle search and stable index sorting, bounded integer queues/stacks, bit/range operations,
and native-style random-modulo dispatch; shared storage/address helpers remain in the coordinator.
`engine/Ome.Engine.Vm/VirtualMachine.ControlFlow.cs` owns local jumps/calls/returns, value-switch construction,
ADV coroutine handler save/yield/resume, and bounded labeled-yield dispatch; process/root exit and cross-script
lifecycle are routed separately. `engine/Ome.Engine.Vm/VirtualMachine.InputLatch.cs` owns native's input latch: the
triggers held while nothing polls (effects under `EffectSkipOnClick` 0, the ADV coroutine yield), the services that
take them, and the discard points. The skip-and-auto native `engine/Ome.Engine/Natives/SkipAndAuto.cs` owns Skip,
Auto, read skip and run-state `0x08000000`; its partials own fast-forward from a held action 6 (`PhysicalInput`: the
two start paths, op `0x101`'s reveal suppression and the clears), the click cancel (`ClickCancel`) and the
fast-forward pace (`Pace`). Composition builds it and hands the same instance to the VM and to the natives that
listen for fast-forward changes (`IFastForwardListener`: the audio native and the input and waits native). It reads
the Auto delays from `SYS4REG.INI` at composition (`ReadSettings`).
The audio native `engine/Ome.Engine/Natives/Audio.cs` owns the 13 channels' playing state and its tick step; its
partials own the retained BGM track and envelope, the op `0xc2` fade and the voice duck with its release (`Bgm`),
the voice that fast-forward holds back, the mute request and the delayed voice (`Voice`), the sound effect channels
and their delayed starts (`SoundEffects`), and the mixer levels, routes and a numbered load's audio (`Mixer`). It
finds sounds through `IAudioAssets.cs` (`ResourceAudioAssets` reads the catalog) and drives an `IAudioDevice`
(`engine/Ome.Engine.Devices/Audio/`, with the headless `SilentAudioDevice`). `engine/Ome.Engine/Natives/EngineTick.cs`
is the engine tick: it runs the natives' timed work at each instruction boundary and inside the host's waits, and
owns the statement block of ops `0x1f4`/`0x1f5`.
The graphics native `engine/Ome.Engine/Natives/Graphics.cs` owns the retained objects (its `GfxState`, which the VM's
`Gfx` returns), the presentation request, and the partners it asks and tells (`IGraphicsMovies`, `ISurfaceListener`).
Its partials own the surface table (`Surfaces`: pixels as immutable `RgbaImage` snapshots that each change replaces,
colour keys, loaded resources, sizes, the decoded-image cache, texture resolution and the op trace the `gfx` command
prints), publication and the screen transition (`Transitions`: what ops `0x222` and `0x20c` publish into a render
target or the backbuffer, the render-target snapshots, and the `0x21`/`0x22`/`0x24`/`0x25` transition and its timing),
and the frame lowering (`Frame`: the one traversal of the objects, which lowers each frame, effects included, to a
`FrameDescription`, and a published range to plain items). The frame description and the renderer and presenter
interfaces are `engine/Ome.Engine.Devices/Graphics/FrameDescription.cs`.
The movie native `engine/Ome.Engine/Natives/Movies.cs` owns the movie ops (`0x236`, `0x241`, `0x23a`, `0x246`,
`0x24d`, and the start of op `0x20f`'s modal movie), which playback is bound to which surface (`MovieSurfaceRegistry.cs`)
and the op `0x24d` mask transitions; it is the graphics native's `IGraphicsMovies` and one of its surface listeners.
Its partial `Movies.Playback.cs` owns the decoders: the engine thread opens one per playback (`MovieRuntime.cs`, its
start delay and watchdog) and the front end's frame runs `ServiceFrame`, which stops, adopts, applies rates,
publishes frames and completes them. `MovieAudioFeed.cs` feeds a playback's PCM to an `IAudioStream` at its
presentation times (`MovieAudioTimeline.cs`) and paces the decoder by what has been heard. It finds movies through
`IMovieAssets.cs` (`ResourceMovieAssets` reads the catalog); the decoder is `IMovieDecoderFactory` and the sound
`IAudioStreamDevice` (`engine/Ome.Engine.Devices/IMovieDecoder.cs`, `Audio/IAudioStreamDevice.cs`).
The script memory native `engine/Ome.Engine/Natives/ScriptMemory.cs` owns the five global banks (the VM's `Globals` and
its siblings return them), the engine's string encoding, the queues and stacks of ops `0x132`-`0x139`, op `0x9`'s bank
reset and the banks' sections of a numbered save; its partial `ScriptMemory.Random.cs` owns op `0x60`'s seeded
per-call-site streams, which the drive step `rand-seed` turns on, and each Lua mod's stream under the same seed
(`DrawForMod`, Lua scripting spec §5.6).
The save and settings native `engine/Ome.Engine/Natives/SavesAndSettings.cs` owns the shared profile, the save store,
`SYS4REG.INI` (the front end's shell reads and writes the keys AGERC owned through it), the playtime clock, the
shutdown flush (`set:NoSaveDat`) and the shutdown write of the message settings (`FlushMessageSettingsOnShutdown`);
its partial `SavesAndSettings.Numbered.cs` owns ops `0x19e`-`0x1a1` and `0x1ab`-`0x1af`
and decides a numbered save's order: each native writes and reads its own records (`NumberedSaveRecords`; audio's in
`Audio.Mixer.cs`, graphics' in `Graphics.Saves.cs`).
The shell-services native `engine/Ome.Engine/Natives/ShellServices.cs` owns ops `0x142`, `0x148`/`0x149` and `0x10f`'s
system-menu fields, the menu commands queued for the interpreter (`TakeMenuRequests`), the callback scripts they and a
screen-mode switch launch (`TakeCallbackScript`), and ops `0x1b2`-`0x1b4`'s text. Its partials own the dialogs
(`Dialogs`: each a request the front end completes through `IShellDialogs`; a stop or a reset cancels it) and the
screen mode (`ScreenMode`: every switch, `display:ScreenMode` and `ForceScreen`, op `0x2cf`'s request), which the front
end shows through `engine/Ome.Engine.Devices/Window/IWindowDevice.cs`.
The input and waits native `engine/Ome.Engine/Natives/InputAndWaits.cs` owns native's waits and the wait flags (run-state
`0x80000000`, `0x400`, a sleep, op `0x20f`'s modal movie), and the engine thread (spec §5.3, "As built (milestone
11)"). Its partials: `Blocking` is the one place a wait blocks (each wait that is not modal runs the engine tick on
every wake); `Events` routes the front end's input as `age_main_window_proc` does, to the interpreter through
`Natives/IInterpreterInput.cs`; `TextWait` holds the ADV input poll and the reveal's wait; `Sleep` holds the sleeps,
the timed-callback deadline, the instruction boundary's `FrameYield` and the front end's modal hold; `Transitions`
holds the effect and screen transition waits; `Presentation` holds the synchronized presentation and where the engine
thread may present; `Modal` holds op `0x20f`'s movie and the dialog waits; `Turns` holds the engine thread
(`RunEngine`), the front end's frame turn (`RunFrameTurn`, which returns the frame description to draw) and its
requests (`Invoke`, `TryInvoke`). `Natives/StopAndReload.cs` holds the stop and the return to title, one copy for the
waits and the interpreter. The front end reads the text waits' pages by polling `PageNotices` and `Pages`. The VM
calls the native through `Natives/IInputAndWaits.cs` (above), and the native shows the cursor through the device
`engine/Ome.Engine.Devices/Window/ICursorDevice.cs`.
The scripting native `engine/Ome.Engine/Natives/Scripting.cs` (Lua scripting spec §14.1) owns the Lua session, which
its `Scene` holds, so op `0x9`'s reset ends it and the interpreter builds the next one (`StartSession`). It also owns
each script's hook table (`HooksFor`, which a frame takes when it is entered), the handler calls of an instruction's
`before` hooks (`RunBefore`) and of the engine events, the nesting cap, the error policy (`--lua-errors`), the
failure count that switches a handler off, `diagnostics/lua.log` and the notices the front end polls. It creates the
runtime only when a mod ships Lua or the console asks (`LazyModRuntime`). Its partials: `Access` builds each mod's
`IScriptAccess` face over the script memory native and the interpreter (`IInterpreterAccess`, which `VirtualMachine.Scripting.cs`
implements, `maid.vm.call` included: spec §10), and checks a call's list and where it is made; while a call runs, the
VM calls the waits through `engine/Ome.Engine.Vm/ModCallWaits.cs`, which refuses each member that waits; `Console` queues the driver's `lua` chunks and runs them at an instruction boundary or inside a wait
(`ServiceRequests`, which `InputAndWaits.ServiceAfterRequests` calls after each batch of requests); `State` is the
mods' state and the session's life across loads: the save record the save native asks for at op `0x19e`
(`SAVEnn.modstate.json`, which the port's `SidecarNativeDatStore` writes beside the save), the load's report, each
mod's last good block, `maid.session.restoring`, and the build and `ome.session.resumed` the interpreter asks for at
a boundary (`BoundaryPending`, `ServiceBoundary`); `Reload` is `--lua-reload`'s rebuild (spec §13): the files the front
end hands over (`RequestReload`, `RefuseReload`) and the session rebuilt at a boundary outside any wait
(`IInterpreterAccess.InsideWait`), keeping each mod's state; `Events` runs the game events' subscribers; `Watches` keeps the
watch records (spec §7.3): which running instruction owns each write to a watched cell, and their delivery when it
returns, over the bitmap of watched cells that the script memory native's setters test (`WatchedCells.cs`); `Trace`
writes `--lua-trace`'s `diagnostics/lua-trace.jsonl`. The runtime it calls is `engine/Ome.Scripting.Lua/`; the link builds its stack and plan in `engine/Ome.Mods/Linking/ModLuaLinker.cs`.
`engine/Ome.Engine.Vm/VirtualMachine.ScriptLifecycle.cs` owns process/frame/root
exit, native run-state compatibility, initial-root-run query, ordinary cross-script calls, mounted append autoruns,
and preloaded script-slot load/call dispatch; frame execution, script-provider access, and shared lifecycle state
remain in `VirtualMachine.cs`.
`engine/Ome.Engine.Vm/VirtualMachine.Values.cs` owns integer arithmetic, bitwise and comparison operations,
string comparison/concatenation/conversion/move, native byte-length and CP932 operations, and the host-backed
fullwidth string editor; shared operand storage, addressing, and native-string encoding remain in the coordinator.
`engine/Ome.Engine.Vm/VirtualMachine.Diagnostics.cs` owns diagnostic value/newline accumulation and synchronous
show-and-clear opcode dispatch; shared diagnostic state and native-context formatting remain in the coordinator.
`engine/Ome.Engine.Vm/VirtualMachine.RuntimeSettings.cs` owns message-window alpha and system-menu enable/show
delay opcode dispatch; reset/default initialization, public menu-state accessors, and host state remain centralized.

The disposable `build/games/<game>/page-map-<SCENE>.jsonl` files are produced by editor/development Godot runs
and map runtime ADV page ordinals to their authoritative script offsets for `tools/locate_page.py`, and hold each
page's text for `tools/page_map_compare.py` (localization gate 11). Packaged exports
have no repository output tree and write their automatic maps below
`user://games/<game>/diagnostics/page-maps` instead.

A profile's `mods/` folder in its data root (`user://games/<game>/mods/`, or the `--data-root` sandbox) holds
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
  `--game`, `--game-root`, and `--extracted-root` options (or matching `OME_*` variables), with
  conventional Himegari defaults. Switching profiles or relocating the tree requires no source edits.
- **Path references in docs** are `age-reimpl/`-relative (e.g. `tools/sys4load.py`,
  `build/games/himegari/text/dialogue.jsonl`) unless they name a game/extracted path explicitly.
- **Authoritative script copies:** where a script exists both as a loose `.BIN` in the game folder
  and under `extracted/DATA1/`, the game-folder copy (patch v1.03) wins. `paths.scripts()` resolves
  this automatically (overrides win).
- **`build/` and extracted roots are disposable.** Shared engine ABI artifacts live directly under
  `build/`; selected game outputs regenerate under `build/games/<game>/` without colliding.
  Extracted roots regenerate from the `.ALF` files with a separately obtained local extractor.
  `bin/README.md` records the optional local convention; no extractor binary is tracked. Safe to delete
  and rebuild; do not hand-edit.
- **Opcode knowledge is edited ONLY in `vm-map/opcodes.toml`** (catalog framing, per-profile observation,
  evidence-scoped ABI snapshots/layers, revision-scoped structured evidence, semantics, and dependencies).
  Current revision snapshots resolve directly; the schema supports future parent/add/replace/remove composition
  without asserting a base SYS4 lineage. What the port runs is the engine's handler table
  (`engine/Ome.Engine.Vm/VirtualMachine.Handlers.cs`), not registry data. Run `tools/opcodes_build.py --build` to regenerate both Python views
  (`tools/age_opcodes.py`, `tools/age_opcode_semantics.py`), machine JSON (`build/opcodes.json`), reference
  (`docs/opcode-reference.md`), and union/per-profile coverage. `--lint` also checks profile/revision/evidence
  references, dangling deps, confidence ceilings, and vocabulary.
  Both Python modules are generated and must not be hand-edited.
- **Global-variable knowledge is edited ONLY in the profile's registry** — `vm-map/globals.toml` for
  Himegari, `vm-map/globals-<game>.toml` for any other game (name + category + value_domain +
  provenance). Run `tools/globals_build.py [--game ID] --build` to regenerate that profile's
  `build/games/<game>/globals.json` (sys4load labels) and `docs/global-reference[-<game>].md`; `--lint` checks vocabulary / auto-shape≠high / dangling deps. Curated entries
  override the auto shape map (`build/games/<game>/global-var-map.json`). Discover story-flag candidates with `tools/story_flags.py`.
- **Named patch points are edited ONLY in `vm-map/patch-points-<game>.toml`.** Run
  `tools/patch_points_build.py [--game ID] --build` to validate them against the installed base and regenerate
  `build/games/<game>/patch-points.json` (read by `Ome.Cli oms-patch`) and `docs/patch-point-reference[-<game>].md`.
- **Encoding:** all generated text is UTF-8 (source strings are cp932/Shift-JIS, decoded on
  extraction). Run Python as `py -3.11 -X utf8`.
- **The game install is a runnable unit** — do not relocate `AGE.EXE`/`*.ALF`/DLLs relative to each
  other, or the game (and any Frida work) breaks.
