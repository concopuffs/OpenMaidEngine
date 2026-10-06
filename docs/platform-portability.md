# Platform Portability

Living inventory of operating-system dependencies in the reimplementation and the work needed for future
non-Windows builds. This is a tracking reference, not a commitment to expand the current Phase A slice.
Native-engine reverse engineering remains Windows-oriented because the original game is a Windows program;
that does not by itself make the authored runtime Windows-only.

## Current portability boundary

The VM and content pipeline are already mostly platform-neutral:

- `Age.Engine` uses managed .NET for script loading, bytecode execution, SYS4 catalog parsing, bounded
  ALF/AAI reads, LZSS, AGF-to-RGBA8 decode, and retained graphics state.
- Godot owns ordinary graphics presentation, input, BGM, voice, and SFX playback.
- Effectful bytecode operations cross `Hosting/IHost.cs`; the VM does not call native OS APIs.
- Movie payloads arrive from `IAssetStore` as owned bytes and decoded frames enter the compositor as the
  platform-neutral `RgbaImage` type.
- Native-compatible persistence uses managed streams behind `INativeDatStore`; the selected manifest owns
  identity/layout policy and Godot places native files below `user://games/<profile-id>/`, retaining native
  filenames and formats. Himegari and Kamidori both support read/write persistence with profile-selected
  numbered-save graphics layouts. Probes and identity/version mismatches remain read-only.
- The Himegari Godot assembly embeds generated `build/opcodes.json` as profile runtime metadata. Exported
  builds never search for an `age-reimpl` repository ancestor; their automatic page-map diagnostics live
  under `user://games/<profile-id>/diagnostics/page-maps`, while editor/development runs retain the workspace
  `build/` handoff.
- `GodotTargetPlatform`, not the build host OS, selects the optional Windows GDI adapter. `AgeNativeRid`
  follows an explicit export `RuntimeIdentifier` and otherwise supplies the current x64 development default;
  mutually exclusive `win-x64` DLL and `linux-x64` shared-object groups stage to build and publish output.
  The managed FFmpeg resolver derives reserved Windows, Linux, and macOS x64/arm64 RIDs from the actual
  process and rejects unreserved architectures instead of silently looking in the wrong directory.

The sole movie path now uses the project-owned FFmpeg C ABI rather than a Windows multimedia API. Reproducible
Windows-x64 and Linux-x64 native bundles are built and staged. A committed Godot 4.7 .NET Linux-x64 preset and
guarded exporter produce a self-contained artifact whose headless self-test passes under Ubuntu/WSL with the
portable TextServer backend and installed Noto CJK fonts. A subsequent WSLg interactive run created the Linux
window, accepted title/menu input, rendered both opening movies with sound, and progressed naturally into SC0000.
This establishes the packaged loader, archive, font, window/input, movie audio/presentation, managed host, and
teardown paths. The shared Windows/Linux A/V follow-up identified skipped opening video preroll in the FFmpeg shim
rather than a Linux-only clock defect; the fix now awaits a manual LOGO/OP recheck. A complete combined game/export
installation copied to WSL ext4 passed
hash verification plus headless and windowed executable-directory runs; mixed-case fixtures and fresh interactive
profile/save behavior remain to validate. The accepted DirectShow fallback was deleted after the corpus and audible
LOGO/OP/CHAPTER gates passed. There are also softer Windows assumptions that should be tested or replaced before
claiming portable exports.

The distributable Linux build path was completed on 2026-08-03. `tools/godot-linux-x64.json` pins the official
Godot 4.7 .NET Linux editor and release template independently; the bootstrap range-fetches only the required
73,591,000-byte template member instead of transferring the 1,200,753,503-byte all-platform archive. The single
`tools/build-linux-x64.sh` entry point regenerates runtime metadata, builds the pinned FFmpeg bundle, performs a
real release export, rejects incomplete/cross-target payloads, and creates a normalized `.tar.gz` carrying the
project license, third-party notices, build provenance, and a complete SHA-256 ledger. The final staged runtime
passes an asset-independent headless gate before packaging success is reported: the embedded 548-opcode table
loads and the project-owned shim resolves its local FFmpeg libraries at ABI 3. This proves artifact assembly and
native loading without weakening the separate installed-game/font/render/audio acceptance gates. A local Ubuntu
20.04 WSL proof passed using isolated Python 3.11/.NET 8.0.408 toolchains; the hosted job provides those through
its pinned setup actions.

The hosted artifact wrapper is `.github/workflows/release-build.yml` on GitHub Actions' pinned `ubuntu-24.04` image
(until 2026-10-05 it ran as `.gitea/workflows/linux-release-build.yml` on the former Gitea host, where the
acceptance runs recorded below took place). It invokes that exact local command on `develop`, manual dispatch, and
`v*` tags while the independent source-only core workflow remains the fast integration gate. The cache key is
derived from both immutable dependency manifests and has no broad restore prefix; cached archives/templates are
still size/hash checked by their owning bootstrap before use. A successful job retains the archive, external
build/checksum manifests, and smoke log for 30 days. It has only read access to repository contents and does not use
private data, secrets, registry credentials, or release-write permission. The first hosted execution reached Godot's
managed publish but was killed with status 137. Local stage-level measurement reproduced the pressure: the resident
Godot export peaks at 772,476 KiB and an isolated managed publish peaks at 223,764 KiB, so nesting the latter under
the former can cross a roughly 1 GiB runner cgroup. The build now serializes those peaks: it creates the exact
self-contained publish first with compiler/build servers disabled, lets the real Godot export consume a strictly
validated one-assembly proxy publish, and stages the complete external managed payload only after Godot exits. The
complete revised path passes locally, including payload verification and the packaged opcode/FFmpeg smoke gate. The
hosted retry at `400f431` then completed successfully on 2026-08-03, accepting the mitigation and the complete
build/smoke/artifact-upload path on the target runner. The workflow reports both host memory and cgroup limits for
future diagnostics.

Tag-only release promotion is now part of the same workflow without changing that build trust boundary. The build
job always retains read-only contents permission and uploads one flat five-file artifact: the archive, its external
SHA-256, build metadata, payload checksum ledger, and smoke log. A dependent job runs only after a successful `v*`
tag build, downloads that exact artifact without rebuilding, and is the only job granted `contents: write` (which
covers releases on GitHub). It uses the run's built-in `github.token` with the GitHub Releases API; branch and
manual runs never receive write authority, and no personal secret or third-party release action is introduced.
Promotion rechecks the clean source commit, archive checksum, and accepted smoke result after artifact download, and
confirms through the git-ref API that the tag names that commit. It is retry-safe but fail-closed: assets are
uploaded to a draft that is published only after the exact asset set verifies, an interrupted draft resumes with
only its missing assets, and a published release, a mismatched release identity, or a same-name/different-size asset
is never edited, deleted, or overwritten. On the former Gitea host, the first hosted develop run containing
promotion completed successfully at `f0f5f12` on 2026-08-03 and reported the tag-only job as skipped, accepting the
non-tag permission boundary. The remaining acceptance gate was a deliberate first version tag and inspection of its
five release attachments. That gate passed with the lightweight `v0.1.0` tag at `5fe3cd6` on 2026-08-03: the tag
build and promotion job completed successfully, and that Gitea release carries the archive, archive checksum, build
metadata, payload ledger, and smoke log. The Linux CI/CD path is therefore accepted end to end.

## Windows x64 CI/CD artifact

The Windows artifact will be cross-built on the existing Linux runner; neither a Windows runner nor Wine is part
of its build contract. The .NET SDK publishes `win-x64`, the Linux Godot editor exports through the pinned Windows
x86-64 template, and MinGW-w64 compiles the one project-owned native component. The pinned project-built Windows
FFmpeg SDK carries the required headers, runtime DLLs, MinGW `.dll.a` and MSVC `.lib` import libraries. The conditional
`Age.Engine.Text.Windows` project contains managed P/Invoke declarations and therefore compiles on Linux without
loading GDI. Wine would add only an execution test, which is explicitly outside the requested CI gate.

The MinGW native foundation was completed and locally accepted on 2026-08-03. The Bash bootstrap resolves the
exact manifest-named SDK instead of an arbitrary previously extracted Windows archive; the matching build script
uses MinGW-w64, suppresses the PE timestamp, statically links its GCC support, and stages only the shim, five
pinned FFmpeg DLLs, and LGPL text. The source-only verifier checks all six images as AMD64 PE, requires the seven
AGE movie ABI exports and five FFmpeg imports, and rejects extra DLLs plus Cygwin/MSYS imports without executing
the result. Both the new MinGW output and the existing MSVC output pass that same contract. Two consecutive
MinGW builds produced the identical shim SHA-256
`9852680ca32bc37200d0b63ad89acb5453ad913bdcf4441a15c8ac9356d4da8c`. This is a compiler-independent contract
comparison, not an expectation that MSVC and MinGW produce byte-identical binaries.

The cross-export/package contract was completed and locally accepted on 2026-08-03. The shared Godot manifest
now pins both release-template members while each build range-fetches only its selected target; Windows adds the
109,405,184-byte `windows_release_x86_64.exe` member with independently recorded SHA-256. The guarded publish
proxy requires its driver-selected RID, so the same low-memory sequence produces exact self-contained
`ExportRelease/linux-x64` and `ExportRelease/win-x64` payloads without weakening the project/configuration/RID/
output-root checks. `tools/build-windows-x64.sh` composes the pinned editor/template, MinGW native bundle, .NET
publish, real Godot export, static verifier, and normalized ZIP. Its verifier requires the executable/PCK,
self-contained runtime, three project assemblies plus `Age.Engine.Text.Windows.dll`, native FFmpeg bundle and
license; it rejects Linux shared objects and checks the Godot EXE plus all six native DLLs as AMD64 PE. The ZIP
contains project notices, clean-source/dependency metadata, a complete payload checksum ledger, and the static
verification report under one stable root. A WSL cross-build completed in 91.3 seconds and two package passes
were byte-identical; the resulting packaged EXE also reported
`PACKAGE SMOKE OK: opcodes=548 ffmpeg-abi=3` when optionally run on the Windows host. CI remains structural and
will not execute that EXE.

The Windows build command mirrors the accepted Linux entry point while keeping target checks separate. It
regenerates opcode metadata, selectively installs the Windows template from the existing pinned Godot TPZ,
bootstrap/hash-checks the pinned Windows FFmpeg SDK, cross-builds and inspects `age_movie_ffmpeg.dll`, performs an
exact self-contained `ExportRelease/win-x64` publish, and runs the real Godot Windows export. The accepted Linux
path retains its target selection and dynamic package-smoke behavior.

CI acceptance is structural because the produced executable does not run on the Linux host. The verifier
requires the Windows executable/PCK/self-contained managed directory, `Age.Engine.Text.Windows.dll`, the project
shim, all five pinned FFmpeg DLLs, and the FFmpeg license; rejects Linux shared objects and incomplete/cross-target
payloads; and inspects the executable plus native DLLs as AMD64. The portable ZIP carries the project license,
third-party notices, clean source/dependency metadata, and a complete payload checksum ledger. A manual packaged
`--package-smoke` command remains available for real-Windows acceptance, but its result is not fabricated or
reported as a hosted CI gate.

The hosted Windows artifact job was added on 2026-08-03 beside the accepted Linux job. Develop, manual, and tag
events now run independent `ubuntu-latest` builds, each with inherited read-only repository permission and its
own manifest-keyed cache/workflow artifact. Windows installs Ubuntu MinGW-w64 GCC/binutils, reports the compiler
and PE inspector, calls only `tools/build-windows-x64.sh`, and retains the ZIP plus its external hash, build
metadata, payload ledger, and static verification report for 30 days. It has no secrets, Wine, EXE execution, or
release authority. Source-only workflow regressions pin those rules and preserve the current Linux-only
`publish-release` dependency. The first hosted dual-job develop run at `9d3ab30` succeeded on 2026-08-03: both
platform artifact jobs completed and the tag-only promotion job was skipped. The Windows hosted artifact path is
therefore accepted; dual-platform promotion is the remaining CI/CD slice.

Dual-platform promotion was implemented on 2026-08-03. The tag-only job now requires both successful build jobs and
downloads their retained artifacts into separate directories without rebuilding. The release helper rechecks both
external archive hashes, exact clean tag-commit metadata and target RID, matching build metadata/payload ledgers
inside each archive, Linux's accepted dynamic package smoke, and Windows's complete static AMD64/ABI/ import report.
It creates or resumes only a matching release, rejects unexpected or colliding assets, and exposes exactly two
distributable archives: `OME-linux-x64.tar.gz` and `OME-windows-x64.zip`. A third small `RELEASE-SHA256SUMS`
attachment authenticates both archives (the three-asset shape was originally chosen to stay below the former Gitea
host's observed five-attachment boundary and is kept unchanged on GitHub); detailed build metadata, payload ledgers,
and logs remain inside each archive and in the retained workflow artifacts. The release notes will state that Linux
received the dynamic package smoke and Windows received structural cross-target verification only. Source-only
paired creation/resume/refusal tests pass. The hosted develop run at `d657c63` completed both platform builds and
correctly skipped tag promotion on 2026-08-03, accepting the paired non-tag path. Its core job initially failed
before checkout on a transient runner DNS lookup for GitHub, then passed unchanged on retry; this was infrastructure
availability rather than a validation failure. The lightweight `v0.2.0` tag at `93d8236` then completed both
platform jobs and promotion on 2026-08-03. That historical release retains its then-current
`OpenMaidEngine-Himegari-*` archive names; future builds and releases use the `OME-*` names above. The resulting
Gitea release exposed exactly the Linux archive, Windows archive, and `RELEASE-SHA256SUMS`, accepting the
dual-platform CI/CD path end to end; `v0.1.0` remains unchanged.

Code signing, an installer, Windows-on-Linux execution, and retroactively modifying the accepted Linux-only
`v0.1.0` release are outside this effort. A future signing slice can use `osslsigncode` and protected credentials
without changing how the unsigned portable ZIP is assembled.

### Deferred Linux font-bundling follow-up (2026-08-11)

The current Linux package deliberately retains its explicit Japanese-capable system-font prerequisite; no font
binary or package behavior changes in this follow-up record. When this work resumes, the preferred first candidate
is an unmodified, pinned upstream set of four region-specific Noto JP static OTFs: Serif Regular/Bold for authored
Mincho requests and Sans Regular/Bold for Gothic requests. Bundled files should be loaded ahead of the existing
`SystemFont` substitutions while retaining system fallback for additional mod-authored Unicode.

The implementation slice must pin upstream versions and hashes, include the SIL Open Font License and attribution,
extend package membership/build provenance, and smoke both weights/families plus missing-glyph fallback from the
final archive. Variable fonts remain a size-optimization alternative only after TextServer weight-axis behavior is
validated. Game-corpus-only subsetting is not the default because it would constrain first-class modding and name
entry. The system-font-only policy remains supported until the bundled-font option is deliberately selected.

## Dependency inventory

| Area | Current dependency | Runtime impact | Portability status / future action |
|---|---|---|---|
| SYS4INI per-game startup profile | `Sys4AssetCatalog` parses and retains the bounded ordered startup trailer; `Sys4LogicalCanvas` applies `SCREENX`/`SCREENY` with AGE's independent `640x480` fallbacks. Native SYS4INI also carries text, ADV input/skip, save ABI/path, audio, legacy renderer, and Windows registration settings | Godot uses the selected canvas for content scaling, backbuffers/compositor bounds, primary surfaces, layout/input fallbacks, and the default windowed client. `--window-width`/`--window-height` vary only the physical client while preserving the canvas and letterbox policy. The port still does not source `CancelMesSkipOnClick`, `CoexistMesSkip`, cursor/redraw policy, or wheel action ids from the trailer | Validate the existing presentation policy on Linux/macOS window managers. Apply further semantic keys explicitly, translate save roots through the host, and classify DirectDraw/fullscreen-bit and registration/key settings as native compatibility metadata. Canonical inventory and consumers: `sys4-format-notes.md` and `engine-re.md` |
| Retained graphics presentation | Backend-neutral `GfxState`; accepted default Godot `Sprite2D` GPU stage plus the retained software pixel oracle, using runtime `ImageTexture`, canvas transforms/materials, and no native graphics API | GPU backend caches static/color-key variants, updates dynamic surfaces, handles retained range transitions, and falls back whole-frame for the legacy host screen-transition path | Godot owns D3D/Vulkan/Metal/OpenGL selection; validate shader/blend/filter behavior per target rather than adding a platform renderer |
| AGE movie decode (`0x236` scene movies; `0x20f` modal LOGO/OP/ED; `0x24d` movie masks) | `FfmpegMovieDecoder` is the sole factory over the project-owned `native/age_movie_ffmpeg` ABI | Windows-x64 and Linux-x64 both pass the complete 213-payload installed video/audio corpus gate. The Linux library bundle is present in a validated Godot export; Windows also passes audible LOGO/OP/CHAPTER playback and real TEST.AGF green-mask decode | Add macOS native builds, distributable source-offer packaging, and real Linux/macOS window/audio movie gates |
| Movie integration | Each surface owns a unique playback-instance id; `MovieRuntime` owns `IMovieDecoder` from an injected factory; video-only streams use monotonic pacing while audio-bearing streams use the Godot output clock. Opcode `0x24d` redirects decoded green bytes through the platform-neutral retained rasterizer and exact managed packed-alpha mask helper | Concurrent/restarted uses of one asset have independent frame/audio/completion/teardown state. Ordinary and mask movies share VFS/FFmpeg ownership; the mask result is a backend-neutral dynamic RGBA surface consumed by either Godot renderer. Managed code is no longer Windows-annotated, and target-aware builds stage either the win-x64 or Linux-x64 native bundle | Add macOS native builds and Linux/macOS in-engine smoke gates |
| Movie audio | ABI v3 returns timestamped stereo float PCM; bounded managed buffering feeds a per-playback Godot `AudioStreamGenerator` and routes native movie flags to engine buses | All 29 installed audio-bearing streams decode with signal. Video follows the output-latency-compensated hardware clock; video and audio both retain the common zero-based media timeline, and completion holds the terminal image through the audibly drained graph endpoint | Manually recheck the 2026-08-11 LOGO/OP synchronization fix on Windows and Linux; treat any remaining absent, distorted, or unsynchronized audio as a runtime bug |
| ADV font discovery/raster fidelity | `Age.Engine.Text` remains OS-neutral. On compatible ACP-932 Windows, `auto` selects the separate exact-to-request `Age.Engine.Text.Windows` GDI gray-4 backend. Otherwise Godot TextServer supplies Unicode masks and metrics from the selected profile's `portableTextRendering` metadata reference, falling back to the shared Japanese policy in `godot/config/japanese-text-rendering.json` when none is specified; portable rendering is explicitly not GDI pixel-exact. Immediate `0x204`/`0x205`, live ADV, retained History, and the wait atlas all enter ordinary RGBA surfaces/retained handles | Three Mincho/Gothic regular/bold samples byte-match an independent Unicode GDI oracle. A live `SC0000@0x869` trace proves that the `AGE Patch.exe`/`jprun.dll` wrapper overrides authored Mincho to MS Gothic: 13 captured masks match Gothic byte-for-byte and Mincho 0/13. This is wrapper behavior, not the true-native default, and reproducing it is outside the compatibility target. Forced exact and forced portable self-tests cover immediate, live, and History rasterization, bounded caching, complete-line materialization, retained ordering, alpha/tint/fade, transforms, clipping, capture, HIDEWIN suspension, partial erase/republication, reset, and teardown. Gameplay `Label` pools and overlay projection are gone | Select and smoke-test a redistributable Linux font-bundling policy or retain the explicit system-font prerequisite; add macOS runtime coverage and keep the GDI project out of non-Windows deliverables |
| Filesystem semantics | Several filename and containment comparisons use `OrdinalIgnoreCase`; installed assets are conventionally uppercase | Needs validation on case-sensitive filesystems; may hide casing or containment mistakes | Add Linux/macOS tests with mixed-case synthetic roots and use filesystem-appropriate containment rules |
| Save/profile/settings storage | `GamePersistencePolicy` owns the native identity, expected SYS4INI `SAVEVERSION`, numbered-bank dimensions, graphics-record size, capability, and stable namespace. `Sys4PersistencePaths` still models AGE's independent `SAVEPATH` and `REGFILEPATH`; Godot preserves their relationship below `user://games/<profile-id>/`, so Himegari uses `games/himegari/SAVE` plus `games/himegari/SYS4REG.INI`. Diagnostics share that profile root. The preserving INI writer changes only its nine `[sound]` keys | Existing Himegari save payloads and engine options remain isolated without changing native formats. Cross-profile regressions exercise open/save/copy/delete against disjoint Himegari and Kamidori roots. Passing no store makes every VM save/delete/copy/thumbnail operation fail without a write and suppresses settings persistence; probes and mismatches use that path. Native/drop-in resolution remains available through `USEAPPDATAFOLDER` plus both SYS4INI paths | Validate CP932 availability, replace/flush, case, permissions, and interrupted-write behavior on each export target. Both numbered graphics layouts pass managed persistence tests; native-application acceptance of port-authored Kamidori slots remains untested |
| Game-install, runtime metadata, and repository discovery | `GameRootSelection` accepts `--game-root`, then probes the executable directory and current working directory for `SYS4INI.BIN`; on Unix the frontend prefers inherited shell `PWD` because Godot may change the process directory during project startup. Before catalog decompression, `GameProfileSelection` reads the fixed SYS4INI revision/title header and uniquely auto-selects an embedded Himegari or Kamidori manifest; explicit `--profile` is authoritative, and `--probe` is read-only. Godot then injects the selected root into its catalog and loose-first ALF store. `OME.csproj` embeds generated `build/opcodes.json`, while `Age.Engine` embeds authored content-free profiles whose runtime seeds and diagnostic boot/resource workarounds are isolated per game. Selected profiles choose evidence-scoped ABI snapshots; handler implementations remain engine-owned. Development/test `Paths.cs` is an explicitly Himegari-oriented installed fixture and is not used by packaged startup | Installed game data and packaged VM/profile metadata no longer depend on the workspace sibling layout. Himegari and Kamidori installed-data gates prove their catalog identities select direct `SYS4422` and `SYS4433` snapshots respectively. Kamidori's runtime/diagnostic policy is empty, so normal boot receives no Himegari global seed or resource bootstrap; its profile selects the supported extended save layout. The Linux export has separately passed an explicit absolute Himegari root outside the repository. Exported automatic page maps use `user://games/<profile-id>/diagnostics/page-maps`; editor/development runs preserve `build/page-map-<SCENE>.jsonl` | Run automatic/explicit/probe startup plus executable-directory/CWD and case/permission smoke gates on Linux and macOS; continue live Kamidori route and save/load UI testing |
| Archive parity oracle | One integration test can launch a machine-local `bin/BinExtractALF.exe`; the binary and matching DLL are ignored and have unverified redistribution terms | Windows-only installed-data helper, not a shipped runtime dependency | Keep it in an explicit private workspace gate; runtime ALF/AAI readers do not depend on it |
| Native RE tools | Frida/Ghidra helpers target the original `AGE.EXE`; PE-sieve was tried and replaced by the Frida import-map workflow | Development/research only; no native RE executable is tracked | Keep separate from export requirements; document platform prerequisites per tool |
| Python workflow and core CI | The operating guide uses Windows `py -3.11`; `validate.py` launches child tools through `sys.executable` and has POSIX process-group cleanup plus `ps`-based leak auditing | The 613 engine cases are explicitly split into 522 source-only core cases and 91 `Workspace` installed-data/native-oracle cases; both selections pass with zero skips. The opcode-bootstrap unit injects synthetic observations while production bootstrap retains real-corpus scanning. `.github/workflows/core-validation.yml` runs the core driver on GitHub Actions' pinned `ubuntu-24.04` image with SHA-pinned official actions (checkout, setup-python, setup-dotnet, upload-artifact) kept current by Dependabot. A standalone arbitrary-name, corpus-free repository passes the complete core driver. The history is published on GitHub's default `develop` branch; the gate previously ran on the former Gitea host from 2026-08-03 (see the roadmap for that record) | Retain Windows-native GDI parity and private installed-game/runtime checks as separate trusted gates; keep the hosted core gate required for future integration policy |

Kamidori persistence now uses the shared profile-selected codecs; see the [supported layout and validation limits](sys4-format-notes.md#kamidori-320-layout).

No authored runtime code currently calls native DirectSound, Direct3D, Vulkan, Metal, or OpenGL. Mentions of
those APIs in `docs/engine-re.md` describe the original AGE implementation. Both the software-oracle display
and retained GPU prototype use Godot abstractions, so accepting the GPU backend does not add an OS graphics
dependency.

## Windows Vulkan startup and injected overlays

On 2026-09-21, Godot 4.7 stable (`5b4e0cb0f`) on the local RTX 4080 SUPER crashed with
signal 11 during Vulkan device creation, before the managed runtime started. The same failure occurred
in an empty project containing only a `Node`, excluding AGE scripts, translation assets, and the FPS
label as necessary triggers. Closing the overlay applications did not remove their implicit Vulkan
layers: loader diagnostics still showed RTSS and Overwolf in the device chain.

Controlled startup runs with the Kamidori translation overlay and FPS counter produced:

| Process-local change | Result |
|---|---|
| None | Access violation; RTSS and Overwolf layers loaded |
| `DISABLE_RTSS_LAYER=1` | Same crash; RTSS still loaded, so this did not isolate its absence |
| `DISABLE_VULKAN_OW_OVERLAY_LAYER=1` | Vulkan startup passed, retained GPU runtime initialized, exit 0 |
| Both variables | Startup passed, exit 0 |
| Godot `--rendering-driver d3d12` | Confirmed D3D12 startup and retained GPU initialization, exit 0 |

The empty project also passed with only Overwolf's overlay disabled. This identifies an interaction
involving the installed `VK_LAYER_OW_OVERLAY` (Overwolf `0.310.0.16`) as the reproducible trigger;
it does not establish sole vendor fault, the underlying memory error, or which update introduced it.
RTSS remained active in the successful Vulkan runs. These were bounded startup checks, not full
gameplay or renderer-equivalence validation. Disposable verbose logs and the minimal project are under
`build/validation/vulkan-startup/`.

For this failure, launch from PowerShell in the repository with:

```powershell
$env:DISABLE_VULKAN_OW_OVERLAY_LAYER = '1'
.\run-godot.cmd -Kamidori -TranslationPatch -FpsCounter
```

The variable is declared by the installed Overwolf layer manifest and affects the current terminal
and its child processes; closing that terminal restores normal inheritance. It does not change the
registry, uninstall overlays, or change the project's rendering settings. The launcher's
`-SoftwareRenderer` selects the AGE compositor oracle, not Godot's graphics driver, so it does not
bypass graphics-device initialization.

## Movie backend replacement seam

The live connection is now backend-neutral:

```
VM op 0x236 (non-modal) / op 0x20f (modal) / op 0x24d (green-mask transition)
  -> IHost.PlayMovieToSurface / PlayModalMovieToSurface / PlayMovieMaskTransition
  -> VFS-owned MoviePayload bytes
  -> IMovieDecoderFactory
     -> FfmpegMovieDecoder
        -> FfmpegMovieSession -> age_movie C ABI
  -> newest due RGBA frame -> retained movie surface -> Godot compositor
     or green byte -> exact captured-range alpha mask -> dynamic scratch surface
  -> timestamped stereo float PCM -> per-playback AudioStreamGenerator
```

Everything before and after the selected decoder is portable. The replacement decision is an in-process FFmpeg
backend behind a project-owned C ABI, not raw FFmpeg structs in Godot/C# and not a subprocess. Since 2026-10-06 the
shim links a **project-built minimal FFmpeg 8.1.3** (step 6 of
`docs/superpowers/plans/2026-10-05-github-cicd-refit.md`): `build-ffmpeg-sdk.sh` builds the official,
signature-checked release tarball pinned in `ffmpeg-source.json`, unmodified, with every component disabled except
the MPEG program-stream demuxer, the raw `mpegvideo` demuxer that labels PES video during probing, the MPEG-1 video
and Layer I/II audio decoders, their parsers, `swscale`, and `swresample` — no external libraries, no programs, LGPL
version 2.1 or later. Each target manifest pins its SDK by exact URL, byte size, and SHA-256; this repository's
release `deps-ffmpeg-ome-8.1.3-mpeg1-r1` holds both SDKs, the exact source tarball they were built from, and each
target's `BUILD-CONFIG.txt` (configure line and toolchain). The Linux SDK rebuilds byte-identically; the Windows SDK
differs between builds only in the PE timestamps and checksums MinGW stamps at link time. The five runtime libraries
total about 4.3 MB, against 109 MB for the previous BtbN `lgpl-shared` build (FFmpeg `n8.1.2-44-g7c533d0f86` plus
about 50 bundled external libraries under LGPL v3), which the project used until 2026-10-06. Changing the pin
requires a new `deps-ffmpeg-*` release and a rerun of the complete installed-movie gate for both profiles. The shim
dynamically links `libavformat`, `libavcodec`, `libavutil`, `libswscale`, and `libswresample`. Release artifacts
must carry the matching FFmpeg source/configuration and notices required by FFmpeg's [license
checklist](https://ffmpeg.org/legal.html).

**FFmpeg license checklist walk-through (2026-10-06).** Each item of <https://ffmpeg.org/legal.html> against the
project-built SDK and the release packages:

| # | Item | Status |
|---|---|---|
| 1 | No `--enable-gpl` / `--enable-nonfree` | Met — neither flag (nor `--enable-version3`) is configured; `test_release_workflow.py` forbids all three. |
| 2 | Dynamic linking | Met — the shim links the five shared libraries; the packagers and `verify_windows_native.py` require them as separate files. |
| 3 | Distribute FFmpeg's source | Met — the exact tarball is an asset of `deps-ffmpeg-ome-8.1.3-mpeg1-r1`; every package's `FFmpeg-SOURCE.txt` and every release description point to it. |
| 4 | Source corresponds exactly | Met — the build verifies the pinned size and SHA-256 and builds the tarball unmodified; the hosted copy carries the same hash. |
| 5 | Provide `changes.diff` | Met — there are no changes; `BUILD-CONFIG.txt` states that the diff would be empty. |
| 6 | Explain how it was compiled | Met — `BUILD-CONFIG.txt` (configure line, compiler, glibc ceiling or DLL imports) ships in the SDK, on the release, and inside `FFmpeg-SOURCE.txt`. |
| 7 | Tarball or zip | Met — the official `.tar.xz` release tarball. |
| 8 | Host source beside the binary | Met — the source and the binaries are both hosted on this repository's GitHub releases. |
| 9 | Attribution on download pages | Met — `publish_github_release.py` appends the FFmpeg credit and source link to every `v*` release description; the deps release states the license. |
| 10 | Mention FFmpeg in the "about box" | Met as far as applicable — the program has no about screen; `README.md` credits FFmpeg and every package ships `FFmpeg-SOURCE.txt`. Any future credits/about screen must name FFmpeg. |
| 11–14 | EULA wording | Not applicable — no EULA; the MIT-licensed project neither claims FFmpeg nor prohibits reverse engineering. |
| 15 | Spell "FFmpeg" correctly | Met — `test_write_ffmpeg_source_notice.py` rejects misspellings in the generated notice. |
| 16 | Do not rename the DLLs | Met — FFmpeg's own names (`avcodec-62.dll`, `libavcodec.so.62`, …). |
| 17 | Repeat for LGPL external libraries | Not applicable — `--disable-autodetect` and no `--enable-lib*`: no external library is compiled in. |
| 18 | No GPL libraries | Met — no external libraries at all. |

The boundary has two layers:

1. A small native `age_movie` ABI owns all FFmpeg objects and version-sensitive calls. It accepts a borrowed
   byte span only for the duration of `open`, copies it once into native-owned memory, and creates independent
   seekable custom `AVIOContext` views for video and audio so either stream can be pulled without duplicating the
   payload or sharing a demux cursor. ABI v2 returns immutable video/audio metadata. Sequential video decode
   returns one top-down tightly packed RGBA8 frame; sequential audio decode returns one interleaved stereo float32
   PCM block. Both carry timestamps normalized to a common media origin. Video conversion occurs in an
   FFmpeg-owned aligned frame; only exact visible row bytes are copied into the caller's tightly packed buffer.
   `libswresample` converts the source channel/sample layout to the Godot generator contract.
   Return statuses distinguish frame, EOF, invalid arguments, undersized output, and decoder failure; the open
   call returns a bounded UTF-8 diagnostic and an opened handle retains its last decode error. Close accepts a
   null handle, and managed `SafeHandle` ownership guarantees one close for each successfully opened handle. No
   FFmpeg pointer crosses the ABI.
2. Managed `IMovieDecoder` owns the native handle and cancellable video/audio workers. Synchronous construction
   provides `StopTimeMs` and audio format before `0x236` returns. Video remains at most one due frame ahead; PCM
   uses a bounded roughly half-second queue, so the 263-second ending never becomes a whole-file allocation.
   Video-only movies use the monotonic playback origin. Audio-bearing movies feed a per-instance
   `AudioStreamGenerator`; the monotonic-clamped Godot sound-hardware position
   (`playback + time-since-mix - cached output latency`) advances due video frames. Completion requires video
   endpoint, audio decode EOF, PCM submission, and audio-clock arrival at the common presentation endpoint. Disposal
   interrupts waits, joins both workers, and stops the generator without depending on native default-device
   playback.

`StopTimeMs` performs a bounded packet scan over the seekable in-memory payload and takes the longest usable
FFmpeg format duration, video-stream duration, timestamp span, or constant-frame-rate packet-count duration. This
accounts for the final MPEG presentation interval that FFmpeg's stream duration can omit (`MVB908` is ten 30 fps
frames: 333 ms, not the raw stream field's 316 ms). The result is converted to integer milliseconds with the same
positive truncation used by native op `0x23f`, then the demuxer rewinds before playback. Streams with no usable
duration evidence fail initialization for the stock-content path rather than inventing timing. The existing host
safety rule still converts any backend initialization failure into an
explicitly completed zero-duration movie identity, and the presentation watchdog remains a last-resort guard for
an initialized backend that never reaches EOF.

The factory is injection for tests and future decoder replacement, not runtime codec roulette. FFmpeg is the
sole backend; DirectShow was deleted rather than shipped as a fallback after the live gate passed. Modal
completion/cancel remains owned above the decoder by the existing `0x20f` host path and tears down its generator
with the video instance. Native movie flag overrides select mute/music/SE/voice audio buses; the ordinary path
uses the movie bus.

This replacement is now also required for Windows gameplay parity. Archive-backed probes on 2026-07-21
show that the current DirectShow graph accepts tested MPEG widths divisible by 16 (208, 288, 304, 400, and
800) but fails `Connect` with `0x80040217` for tested widths that are 8 mod 16 (280, 360, 520, and 600).
The dominant combat-effect family is 280x352 (125 installed `MVB` assets), so retaining DirectShow as the
only Windows decoder is insufficient even though SC0000's 800x600 `CHAPTER.AGF` works.

An archive scan on the same date classified all 213 installed movie payloads as MPEG-1 program streams with
MPEG-1 video and no MPEG-2 sequence extensions. Of those, 184 are video-only and 29 contain MPEG audio. The
large LOGO/OP/ED family uses MPEG Audio Layer II, while 25 small 120x120 `MVS` movies use Layer I. That inventory
is why the narrower `pl_mpeg` library was rejected as the primary backend: it fits every installed video stream
but only decodes Layer II audio and explicitly ignores program-stream PTS in its high-level synchronization.
FFmpeg covers the complete installed codec set and leaves the mod/profile boundary open without selecting a
different decoder per effect.

**Cross-profile codec inventory (2026-10-06).** A header-level scan of every catalog asset that begins with an MPEG
pack header, regardless of extension, re-confirmed Himegari exactly (213 `.AGF` streams: 184 video-only, 25 MPEG-1
Layer I, 4 Layer II, all 44.1 kHz stereo) and inventoried Kamidori for the first time: 280 `.MPG`-named streams, all
MPEG-1 system streams with MPEG-1 video (including the four 1024x576 titles, which carry no MPEG-2 extension
start codes), with 248 Layer II, 20 Layer I, and 12 video-only. Neither corpus contains MPEG-2 video, an MPEG-2
pack header, Layer III, or private/AC-3/LPCM streams. The complete decoder requirement for both supported profiles
is therefore the MPEG program-stream demuxer, the MPEG-1 video decoder, and the MPEG-1 Layer I/II audio decoders
(plus the matching parsers, `swscale`, and `swresample`) — the component set for the project-built FFmpeg in step 6
of `docs/superpowers/plans/2026-10-05-github-cicd-refit.md`. Enabling any other format later means enabling another
of FFmpeg's internal decoders. The installed-movie gate now discovers movies by pack header and takes
`--game-root`/`--overlay-root`, so it covers both corpora: on 2026-10-06 the BtbN build and the project-built minimal
FFmpeg (step 6) each passed Himegari 213/213 and Kamidori 280/280, with identical results on every non-timing field
(dimensions, frame counts and rates, timestamps, changed-frame counts, and audio counts/signal; 268 Kamidori titles
carry audio, 19 of them silent).

ABI v2 and MPEG audio playback landed on 2026-07-25. The expanded installed-corpus gate passes all 213 assets:
184 remain strictly video-only and all 29 audio-bearing streams decode to finite 44.1 kHz stereo PCM with
monotonic timestamps and clean EOF/teardown. The gate decoded 17,537 audio blocks / 18,185,856 stereo PCM
frames in the same eight-second unpaced run. A headless natural boot completed audio-clocked `LOGO.AGF` at
7,288 ms, released it without generator errors, and opened `OP.AGF`. A first windowed run confirmed audible
LOGO/OP output but exposed crackle/warble. The decoded PCM was continuous; the sink had treated each whole-
millisecond PTS rounding error (up to 44 samples at 44.1 kHz) as a real boundary and inserted or removed samples
from every MPEG block. The sink now ignores established-timeline discrepancies within 2 ms while still honoring
the first timestamp and material later gaps/overlaps. A 4,093-block OP regression pins splice-free continuity.
The clean audible recheck passed for LOGO/OP and CHAPTER. DirectShow and the managed Windows platform annotations
were then deleted; missing, distorted, or unsynchronized audio on any of the 29 audio-bearing assets is now a bug.

Movie startup was first corrected on 2026-07-28 after a first-frame latch exposed a visible 600 ms freeze. That
pass correctly stopped pacing the first surviving frame against its raw PTS, but it misclassified the 600/601 ms
LOGO/OP video start as a harmless mux offset. The shim's initial seek had already skipped the actual PTS-0 opening
frames. The opacity portion of the old real-asset regression remains valid—returned frames are consecutive,
changing, and alpha 255—but completeness now has its own PTS-0 assertion and native-content fingerprint evidence.

The paced decoder still latches frame zero and Godot still waits to start its `AudioStreamGenerator` until that
image has been published. The defect was the native shim's initial video rewind: it sought to the video stream's
declared 600/601 ms `start_time`, skipping valid opening packets while audio began at the common zero origin.
LOGO consequently decoded 200 frames from PTS 600 and moved picture roughly 600 ms ahead. Rewinding video to the
format's common media origin recovers 218 consecutive LOGO frames beginning at PTS 0. Native DirectShow traces
show both output pins start at zero, native video fingerprints match FFmpeg at equal timestamps, and native PCM
first becomes non-silent around 2,351 ms. The corrected asset relation places FFmpeg audio onset around 2,380 ms
before the first `E` at video PTS 2,502 ms, rather than 478 ms after the prematurely displayed cue. Completion
retains the full common endpoint. Startup logs and F6 snapshots retain the raw first video PTS for diagnosis.

A separate compositor error amplified the symptom. MPEG-1 has no transparency and FFmpeg's RGBA conversion
produces opaque pixels, but the VM had replaced the existing mutable movie target with an invented RGB color key
of zero. Exact black became transparent and exposed the Godot clear color, producing the previously reported grey
MPEG speckles. Both movie opcodes now retain the existing surface's no-key state; the host's instance binding
supplies changing movie pixels without changing surface resource identity. Native `0x20f` likewise attaches its
DirectShow renderer to the existing D3D target without assigning an RGB key. The annotated native details and the
DirectShow preroll/stream-origin inference are recorded in `docs/engine-re.md`.

Native deliverables are RID-specific and bundled with the Godot export; the runtime must not discover an
arbitrary system FFmpeg. Windows x64 and Linux x64 now have pinned builds; the C ABI and loader paths also reserve
macOS x64 and macOS arm64. Builds use shared libraries, `$ORIGIN`/`@loader_path`-style local lookup on Unix
targets, recorded source hashes and configure arguments, and no committed original-game data. Packaging
automation is part of completing the backend, including the corresponding-source/configuration offer.

The current movie ABI is version 3. Its position-seek entry point resets both independent demux/decoder
pipelines before managed keyframe/audio preroll, so opcode `0x241` has the same bounded, synchronized behavior
on every future native target rather than relying on a Windows-only DirectShow cursor.

The Windows-x64 spike is now complete. `bootstrap-win64.ps1` verifies the immutable archive SHA before
extraction, and `build-win64.ps1` builds the shim with MSVC and places the DLL, import artifacts, required LGPL
shared libraries, and license under disposable `build/native/win-x64`. The Linux-hosted
`bootstrap-win64.sh`/`build-win64.sh` path builds the same exported ABI with MinGW and applies the shared static
PE/bundle verifier. The managed resolver accepts
`AGE_FFMPEG_NATIVE_DIR` for the isolated gate and otherwise reserves application-local and
`runtimes/<rid>/native` lookup. Representative VFS results are `MVB961` 280x352/500 ms, `MVB238`
280x352/866 ms, `MVB908` 400x400/333 ms, and `CHAPTER` 800x600/12016 ms, all with changing frames and
nondecreasing timestamps. Malformed input and repeated teardown are covered. This is not yet distributable
packaging. `FfmpegMovieDecoder` now adds cancellable timestamp pacing and is the live selection. The Godot build
copies the shim, five required shared libraries, and FFmpeg license beside `OME.dll`; a natural SYSTEM4
smoke completed 7288 ms `LOGO.AGF`, opened 106919 ms `OP.AGF`, and published frames from both without loading
DirectShow; the user subsequently confirmed both opening movies work in normal windowed playback. DirectShow
was deleted after the later synchronized-audio gate. Full export/source-offer packaging, Linux Godot runtime
smoke coverage, and macOS native builds remain outstanding.

The Linux-x64 bundle landed on 2026-07-31 from the matching pinned BtbN LGPL shared build. Its bootstrap verified
the immutable archive hash and reported FFmpeg commit (since 2026-10-06 it verifies the project-built SDK's hash and
the version in `include/libavutil/ffversion.h`, because that SDK ships no programs); its build emits an ELF64 x86-64
ABI v3 shim plus only the five required SONAME libraries and license under disposable `build/native/linux-x64`. The
shim records `RUNPATH=$ORIGIN`, every FFmpeg dependency resolves beside it, and the bundle's newest required glibc
symbol is 2.28, matching the provider baseline enforced by the build script. A self-contained Linux corpus-gate
executable under Ubuntu 20.04 decoded all 213 installed payloads with 0 failures in 10.696 seconds. A zero-warning
`ExportRelease` Linux-target assembly build staged the shim, five libraries, and license beside `OME.dll` without
the Windows GDI assembly or DLLs.

The Godot Linux-x64 artifact pipeline landed the same day. `godot/export_presets.cfg` and the .NET-export-required
`godot/OME.sln` are committed; `tools/export-linux-x64.ps1` replaces only its exact disposable output,
performs a bounded Godot shutdown, verifies the complete artifact, and rejects Windows-only runtime files.
`godot/build/.gdignore` prevents historical capture frames from being imported and packed. The exported ELF64
x86-64 executable, PCK, self-contained .NET runtime, and local FFmpeg bundle passed `--selftest` under Ubuntu/WSL
against an explicit installed-game path using the portable text backend, with a clean exit and no Godot errors or
leaked-object warning. Minimal Ubuntu required `fonts-noto-cjk`; before installation the exact same artifact
failed explicitly on Japanese glyph U+59EB. After WSLg was enabled, the same artifact also opened windowed,
accepted normal title/menu input, displayed both opening movies, and reached SC0000 through the natural startup
path. Both movies produced audible output; the slight A/V desynchronization also reproduced on Windows and was
traced to skipped opening video preroll corrected on 2026-08-11. Manual confirmation of that correction
remains a cross-platform follow-up. This interactive acceptance establishes
WSLg window/input/movie/audio presentation, but not native-desktop compositor/driver behavior or case-sensitive
install/profile behavior.

The ext4 installation gate copies the complete 67-file pristine game installation and the three-part Linux export
into one combined directory. All source files SHA-256-match after copying, the destination reports ext4, and the
packaged headless selftest discovers `SYS4INI.BIN` from the executable directory without `--game-root`, reads the
installed archives, realizes portable Japanese text, and exits cleanly. This establishes exact-casing installed
content and automatic root discovery on a case-sensitive filesystem. The same combined directory was subsequently
accepted windowed by the user. Deliberately mixed-case synthetic roots and fresh interactive profile/save writes
remain separate gates.

The Windows-x64 installed-corpus gate passed on 2026-07-22 with the pinned dependency: all 213 signature-selected
MPEG payloads opened and reached EOF under a 30-second per-item bound. It decoded all 15,788 frames across twelve
display sizes (120x120 through 800x600), independently matched sequence-header dimensions, found positive
duration and frame-rate metadata, verified tightly packed RGBA size and nondecreasing timestamps, observed
changing imagery in every asset, and closed every session. The set includes all 184 video-only and 29 audio-bearing
streams; at that video-only milestone audio presence was detected but PCM remained intentionally undelivered.
The later audio-delivery gate above supersedes that limitation. The 263-second `ED.AGF` was the
slowest decode at 4.4 seconds in the deliberately unpaced gate, so its prior 30-second failure was test-harness
whole-frame hashing overhead rather than a decoder incompatibility. The disposable machine report is
`build/movie-corpus-ffmpeg.json`; invocation and report semantics are canonical in `docs/tools-reference.md`.

## Cross-platform validation gates

Before advertising a platform as supported:

1. Build and export the Godot C# project for that target without compiling an unusable native backend.
2. Run the platform-neutral engine tests, with Windows-only parity oracles explicitly classified.
3. Exercise SYS4/AAI/ALF loose override precedence on a case-sensitive filesystem.
4. Decode representative raw/compressed AGF variants and compare RGBA output with established fixtures.
5. Play SC0000 through texture, BGM, voice, SFX, and movie publication using only the original archives.
6. Verify Japanese font discovery/rendering and user-selected game-root handling outside the repository.
7. If movie audio is implemented, validate A/V synchronization, interruption, EOF, and teardown separately
   from video visibility.

## Update rule

Add an entry whenever authored runtime code gains an OS API, native library, platform-specific path,
filesystem assumption, conditional build rule, or platform-specific test oracle. Record whether it affects
the shipped runtime, only development tooling, or only native-engine research. Cross-link detailed subsystem
semantics to their canonical document instead of duplicating them here.
