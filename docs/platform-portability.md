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
- Native-compatible persistence uses managed streams behind `INativeDatStore`; Godot redirects AGE's
  related save/settings profile to `user://`, yielding `user://SAVE` and `user://SYS4REG.INI` while
  retaining native filenames and formats.
- The Himegari Godot assembly embeds generated `build/opcodes.json` as profile runtime metadata. Exported
  builds never search for an `age-reimpl` repository ancestor; their automatic page-map diagnostics live
  under `user://diagnostics/page-maps`, while editor/development runs retain the workspace `build/` handoff.
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
teardown paths. Slight A/V desynchronization also occurs on Windows and remains a shared runtime follow-up rather
than a Linux-only portability blocker. A complete combined game/export installation copied to WSL ext4 passed
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
20.04 WSL proof passed using isolated Python 3.11/.NET 8.0.408 toolchains; the eventual Gitea job can provide those
through its existing setup actions.

The hosted artifact wrapper is now `.gitea/workflows/linux-release-build.yml`. It invokes that exact local
command on `develop`, manual dispatch, and `v*` tags while the independent source-only core workflow remains the
fast integration gate. The cache key is derived from both immutable dependency manifests and has no broad restore
prefix; cached archives/templates are still size/hash checked by their owning bootstrap before use. A successful
job retains the archive, external build/checksum manifests, and smoke log for 30 days. It has only read access to
repository contents and does not use private data, secrets, registry credentials, or release-write permission.
The first hosted execution reached Godot's managed publish but was killed with status 137. Local stage-level
measurement reproduced the pressure: the resident Godot export peaks at 772,476 KiB and an isolated managed
publish peaks at 223,764 KiB, so nesting the latter under the former can cross a roughly 1 GiB runner cgroup.
The build now serializes those peaks: it creates the exact self-contained publish first with compiler/build
servers disabled, lets the real Godot export consume a strictly validated one-assembly proxy publish, and stages
the complete external managed payload only after Godot exits. The complete revised path passes locally, including
payload verification and the packaged opcode/FFmpeg smoke gate. The hosted retry at `400f431` then completed
successfully on 2026-08-03, accepting the mitigation and the complete build/smoke/artifact-upload path on the
target runner. The workflow reports both host memory and cgroup limits for future diagnostics. Release
publication remains a separate tag-only promotion step; it should consume this already-verified artifact rather
than rebuilding it.

## Dependency inventory

| Area | Current dependency | Runtime impact | Portability status / future action |
|---|---|---|---|
| SYS4INI per-game startup profile | `Sys4AssetCatalog` parses and retains the bounded ordered startup trailer; `Sys4LogicalCanvas` applies `SCREENX`/`SCREENY` with AGE's independent `640x480` fallbacks. Native SYS4INI also carries text, ADV input/skip, save ABI/path, audio, legacy renderer, and Windows registration settings | Godot uses the selected canvas for content scaling, backbuffers/compositor bounds, primary surfaces, layout/input fallbacks, and the default windowed client. `--window-width`/`--window-height` vary only the physical client while preserving the canvas and letterbox policy. The port still does not source `CancelMesSkipOnClick`, `CoexistMesSkip`, cursor/redraw policy, or wheel action ids from the trailer | Validate the existing presentation policy on Linux/macOS window managers. Apply further semantic keys explicitly, translate save roots through the host, and classify DirectDraw/fullscreen-bit and registration/key settings as native compatibility metadata. Canonical inventory and consumers: `sys4-format-notes.md` and `engine-re.md` |
| Retained graphics presentation | Backend-neutral `GfxState`; accepted default Godot `Sprite2D` GPU stage plus the retained software pixel oracle, using runtime `ImageTexture`, canvas transforms/materials, and no native graphics API | GPU backend caches static/color-key variants, updates dynamic surfaces, handles retained range transitions, and falls back whole-frame for the legacy host screen-transition path | Godot owns D3D/Vulkan/Metal/OpenGL selection; validate shader/blend/filter behavior per target rather than adding a platform renderer |
| AGE movie decode (`0x236` scene movies; `0x20f` modal LOGO/OP/ED; `0x24d` movie masks) | `FfmpegMovieDecoder` is the sole factory over the project-owned `native/age_movie_ffmpeg` ABI | Windows-x64 and Linux-x64 both pass the complete 213-payload installed video/audio corpus gate. The Linux library bundle is present in a validated Godot export; Windows also passes audible LOGO/OP/CHAPTER playback and real TEST.AGF green-mask decode | Add macOS native builds, distributable source-offer packaging, and real Linux/macOS window/audio movie gates |
| Movie integration | Each surface owns a unique playback-instance id; `MovieRuntime` owns `IMovieDecoder` from an injected factory; video-only streams use monotonic pacing while audio-bearing streams use the Godot output clock. Opcode `0x24d` redirects decoded green bytes through the platform-neutral retained rasterizer and exact managed packed-alpha mask helper | Concurrent/restarted uses of one asset have independent frame/audio/completion/teardown state. Ordinary and mask movies share VFS/FFmpeg ownership; the mask result is a backend-neutral dynamic RGBA surface consumed by either Godot renderer. Managed code is no longer Windows-annotated, and target-aware builds stage either the win-x64 or Linux-x64 native bundle | Add macOS native builds and Linux/macOS in-engine smoke gates |
| Movie audio | ABI v2 returns timestamped stereo float PCM; bounded managed buffering feeds a per-playback Godot `AudioStreamGenerator` and routes native movie flags to engine buses | All 29 installed audio-bearing streams decode with signal; synchronized LOGO/OP/CHAPTER playback is audibly accepted | Treat absent, distorted, or unsynchronized audio from an audio-bearing movie as a runtime bug |
| ADV font discovery/raster fidelity | `Age.Engine.Text` remains OS-neutral. On compatible ACP-932 Windows, `auto` selects the separate exact-to-request `Age.Engine.Text.Windows` GDI gray-4 backend. Otherwise Godot TextServer supplies Unicode masks and metrics from ordered system-font substitutions in `godot/config/himegari-text-rendering.json`; this policy is explicitly not GDI pixel-exact. Immediate `0x204`/`0x205`, live ADV, retained History, and the wait atlas all enter ordinary RGBA surfaces/retained handles | Three Mincho/Gothic regular/bold samples byte-match an independent Unicode GDI oracle. A live `SC0000@0x869` trace proves that the `AGE Patch.exe`/`jprun.dll` wrapper overrides authored Mincho to MS Gothic: 13 captured masks match Gothic byte-for-byte and Mincho 0/13. This is wrapper behavior, not the true-native default. Forced exact and forced portable self-tests cover immediate, live, and History rasterization, bounded caching, complete-line materialization, retained ordering, alpha/tint/fade, transforms, clipping, capture, HIDEWIN suspension, partial erase/republication, reset, and teardown. Gameplay `Label` pools and overlay projection are gone | Optionally expose realized-face overrides through future launch profiles when wrapper compatibility is desired; independently produce and smoke-test Linux/macOS exports, confirm installed family availability/fallback selection there, and keep the GDI project out of non-Windows deliverables |
| Filesystem semantics | Several filename and containment comparisons use `OrdinalIgnoreCase`; installed assets are conventionally uppercase | Needs validation on case-sensitive filesystems; may hide casing or containment mistakes | Add Linux/macOS tests with mixed-case synthetic roots and use filesystem-appropriate containment rules |
| Save/profile/settings storage | `Sys4PersistencePaths` models AGE's independent `SAVEPATH` and `REGFILEPATH` resolutions. Godot replaces Himegari's related profile directory with `user://`, yielding `user://SAVE` for native S3SD/S4SD/S3RT files and thumbnails plus `user://SYS4REG.INI` for the BOM-less CP932 options file; the preserving writer changes only its nine `[sound]` keys | Save payloads and engine options are isolated together without changing either native format. Native/drop-in resolution remains available through `USEAPPDATAFOLDER` plus both SYS4INI paths. A single-root override is rejected when `SAVEPATH` is not beneath `REGFILEPATH`, preventing cross-profile guesses | Expose explicit profile/native selection through the future launcher and allow independent overrides for profiles whose two native paths are unrelated. Validate CP932 availability, replace/flush, case, permissions, and interrupted-write behavior on each export target |
| Game-install, runtime metadata, and repository discovery | `GameRootSelection` accepts `--game-root`, then probes the executable directory and current working directory for `SYS4INI.BIN`; on Unix the frontend prefers inherited shell `PWD` because Godot may change the process directory during project startup. Godot injects the selected root into its catalog and loose-first ALF store. `Himegari.csproj` embeds generated `build/opcodes.json`, and the frontend loads that assembly resource rather than `Paths.OpcodesJson`. Development/test `Paths.cs` finds the repository by tracked `global.json`, solution, and opcode-map markers rather than a checkout-directory name | Installed game data and packaged VM metadata no longer depend on the workspace sibling layout. The Linux export passed an explicit absolute game root outside the repository and read the installed archives; a source-only core proof also passes from an arbitrary repository directory name. Exported automatic page maps use `user://diagnostics/page-maps`; editor/development runs preserve `build/page-map-<SCENE>.jsonl` | Let the future profile launcher pass `--game-root`, and run executable-directory/CWD plus case/permission smoke gates on Linux and macOS |
| Archive parity oracle | One integration test can launch a machine-local `bin/BinExtractALF.exe`; the binary and matching DLL are ignored and have unverified redistribution terms | Windows-only installed-data helper, not a shipped runtime dependency | Keep it in an explicit private workspace gate; runtime ALF/AAI readers do not depend on it |
| Native RE tools | Frida/Ghidra helpers target the original `AGE.EXE`; PE-sieve was tried and replaced by the Frida import-map workflow | Development/research only; no native RE executable is tracked | Keep separate from export requirements; document platform prerequisites per tool |
| Python workflow and core CI | The operating guide uses Windows `py -3.11`; `validate.py` launches child tools through `sys.executable` and has POSIX process-group cleanup plus `ps`-based leak auditing | The 590 engine cases are explicitly split into 502 source-only core cases and 88 `Workspace` installed-data/native-oracle cases; both selections pass with zero skips. The opcode-bootstrap unit injects synthetic observations while production bootstrap retains real-corpus scanning. `.gitea/workflows/core-validation.yml` uses the target server's demonstrated `ubuntu-latest`, checkout/setup-dotnet v4, setup-python v6, and Gitea artifact action baseline. A standalone arbitrary-name, corpus-free repository passes the complete core driver. The sanitized history is published on the private remote's default `develop` branch; the first hosted Linux core run succeeded on 2026-08-03 before sanitation at the tree now represented by `524ea74`, and the hosted gate succeeded again on the sanitized lineage at `d673652` | Retain Windows-native GDI parity and private installed-game/runtime checks as separate trusted gates; keep the hosted core gate required for future integration policy |

No authored runtime code currently calls native DirectSound, Direct3D, Vulkan, Metal, or OpenGL. Mentions of
those APIs in `docs/engine-re.md` describe the original AGE implementation. Both the software-oracle display
and retained GPU prototype use Godot abstractions, so accepting the GPU backend does not add an OS graphics
dependency.

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

Everything before and after the selected decoder is portable. The replacement decision is an in-process
FFmpeg backend behind a project-owned C ABI, not raw FFmpeg structs in Godot/C# and not a subprocess. FFmpeg
`n8.1.2-29-g703dcc25b9` is pinned by immutable release URL and SHA-256 in each target manifest under
`native/age_movie_ffmpeg/`; changing either pin requires rerunning the full installed-movie gate. The shim
dynamically links an LGPL build made without GPL or nonfree components and uses
`libavformat`, `libavcodec`, `libavutil`, `libswscale`, and `libswresample`. Release artifacts must carry
the matching FFmpeg source/configuration and notices required by FFmpeg's
[license checklist](https://ffmpeg.org/legal.html).

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
   stop time, audio decode EOF, and PCM submission. Disposal interrupts waits, joins both workers, and stops the
   generator without depending on native default-device playback.

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

Movie startup was corrected on 2026-07-28 after a first-frame latch exposed a second, visible 600 ms freeze.
`LOGO.AGF` and `OP.AGF` begin audio at 0 ms on the shim's shared mux timeline, while their first decoded video
frames carry PTS 600 ms and 601 ms. Signal-bearing PCM in OP's first 600 ms initially looked like an intentional
audio lead, but native OP advances video and audio immediately. A real-asset regression proves the decoder is not
losing opening data: the first 20 LOGO/OP video frames are consecutive 33/34 ms samples, change within that window,
and contain alpha 255 at every pixel. The mux offset is therefore not a presentation deadline.

The paced decoder still latches frame zero and Godot still waits to start its `AudioStreamGenerator` until that
image has been published. It now schedules every later video frame from
`source_video_pts - first_video_pts`, including audio-bearing movies, while submitting the complete audio stream
from its own timestamp zero. This preserves every decoded sample and immediately advances frame cadence instead
of freezing frame zero until the audio clock reaches the program-stream offset. Startup logs and F6 snapshots
retain the raw first video PTS for diagnosis.

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
shared libraries, and license under disposable `build/native/win-x64`. The managed resolver accepts
`AGE_FFMPEG_NATIVE_DIR` for the isolated gate and otherwise reserves application-local and
`runtimes/<rid>/native` lookup. Representative VFS results are `MVB961` 280x352/500 ms, `MVB238`
280x352/866 ms, `MVB908` 400x400/333 ms, and `CHAPTER` 800x600/12016 ms, all with changing frames and
nondecreasing timestamps. Malformed input and repeated teardown are covered. This is not yet distributable
packaging. `FfmpegMovieDecoder` now adds cancellable timestamp pacing and is the live selection. The Godot build
copies the shim, five required shared libraries, and FFmpeg license beside `Himegari.dll`; a natural SYSTEM4
smoke completed 7288 ms `LOGO.AGF`, opened 106919 ms `OP.AGF`, and published frames from both without loading
DirectShow; the user subsequently confirmed both opening movies work in normal windowed playback. DirectShow
was deleted after the later synchronized-audio gate. Full export/source-offer packaging, Linux Godot runtime
smoke coverage, and macOS native builds remain outstanding.

The Linux-x64 bundle landed on 2026-07-31 from the matching pinned BtbN LGPL shared build. Its bootstrap verifies
the immutable archive hash and reported FFmpeg commit; its build emits an ELF64 x86-64 ABI v3 shim plus only the
five required SONAME libraries and license under disposable `build/native/linux-x64`. The shim records
`RUNPATH=$ORIGIN`, every FFmpeg dependency resolves beside it, and the bundle's newest required glibc symbol is
2.28, matching the provider baseline enforced by the build script. A self-contained Linux corpus-gate executable
under Ubuntu 20.04 decoded all 213 installed payloads with 0 failures in 10.696 seconds. A zero-warning
`ExportRelease` Linux-target assembly build staged the shim, five libraries, and license beside `Himegari.dll`
without the Windows GDI assembly or DLLs.

The Godot Linux-x64 artifact pipeline landed the same day. `godot/export_presets.cfg` and the .NET-export-required
`godot/Himegari.sln` are committed; `tools/export-linux-x64.ps1` replaces only its exact disposable output,
performs a bounded Godot shutdown, verifies the complete artifact, and rejects Windows-only runtime files.
`godot/build/.gdignore` prevents historical capture frames from being imported and packed. The exported ELF64
x86-64 executable, PCK, self-contained .NET runtime, and local FFmpeg bundle passed `--selftest` under Ubuntu/WSL
against an explicit installed-game path using the portable text backend, with a clean exit and no Godot errors or
leaked-object warning. Minimal Ubuntu required `fonts-noto-cjk`; before installation the exact same artifact
failed explicitly on Japanese glyph U+59EB. After WSLg was enabled, the same artifact also opened windowed,
accepted normal title/menu input, displayed both opening movies, and reached SC0000 through the natural startup
path. Both movies produced audible output; the slight A/V desynchronization also reproduces on Windows and is
tracked as a shared runtime follow-up rather than a Linux-only defect. This interactive acceptance establishes
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
