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
  engine-owned root to `user://SAVE` while retaining its fixed portable DAT/STH filenames.

The sole movie path now uses the project-owned FFmpeg C ABI rather than a Windows multimedia API, but only a
Windows-x64 native bundle is built and staged today. The accepted DirectShow fallback was deleted after the
corpus and audible LOGO/OP/CHAPTER gates passed. There are also softer Windows assumptions that should be tested
or replaced before claiming portable exports.

## Dependency inventory

| Area | Current dependency | Runtime impact | Portability status / future action |
|---|---|---|---|
| Retained graphics presentation | Backend-neutral `GfxState`; accepted default Godot `Sprite2D` GPU stage plus the retained software pixel oracle, using runtime `ImageTexture`, canvas transforms/materials, and no native graphics API | GPU backend caches static/color-key variants, updates dynamic surfaces, handles retained range transitions, and falls back whole-frame for the legacy host screen-transition path | Godot owns D3D/Vulkan/Metal/OpenGL selection; validate shader/blend/filter behavior per target rather than adding a platform renderer |
| AGE movie decode (`0x236` scene movies; `0x20f` modal LOGO/OP/ED) | `FfmpegMovieDecoder` is the sole factory over the project-owned `native/age_movie_ffmpeg` ABI | Windows-x64 passes the complete 213-payload installed video/audio corpus gate plus audible LOGO/OP/CHAPTER playback | Add target-specific native builds and export packaging |
| Movie integration | Each surface owns a unique playback-instance id; `MovieRuntime` owns `IMovieDecoder` from an injected factory; video-only streams use monotonic pacing while audio-bearing streams use the Godot output clock | Concurrent/restarted uses of one asset have independent frame/audio/completion/teardown state; managed code is no longer Windows-annotated, while only the win-x64 native bundle exists today | Add Linux/macOS native builds and smoke gates |
| Movie audio | ABI v2 returns timestamped stereo float PCM; bounded managed buffering feeds a per-playback Godot `AudioStreamGenerator` and routes native movie flags to engine buses | All 29 installed audio-bearing streams decode with signal; synchronized LOGO/OP/CHAPTER playback is audibly accepted | Treat absent, distorted, or unsynchronized audio from an audio-bearing movie as a runtime bug |
| ADV font discovery | `godot/Main.cs` probes `C:/Windows/Fonts` for Japanese fonts | Harmless fallback today, but appearance depends on host fonts | Bundle/configure a redistributable font or add platform-specific discovery |
| Filesystem semantics | Several filename and containment comparisons use `OrdinalIgnoreCase`; installed assets are conventionally uppercase | Needs validation on case-sensitive filesystems; may hide casing or containment mistakes | Add Linux/macOS tests with mixed-case synthetic roots and use filesystem-appropriate containment rules |
| Save/profile storage | Managed `DirectoryNativeDatStore` under Godot `user://SAVE`; native S3SD/S4SD/S3RT files and 24-bit BMP thumbnails | No Win32 path API at runtime; port saves remain isolated from the original installation | Validate replace/flush, case, permissions, and interrupted-write behavior on each export target |
| Install/repository discovery | `engine/Age.Engine/Sys4/Paths.cs` finds `age-reimpl` above `AppContext.BaseDirectory` and assumes the current workspace sibling layout | Suitable for development, not packaged exports on any OS | Replace runtime discovery with a user-selected game root/profile; retain repository paths only for developer tools/tests |
| Archive parity oracle | One integration test launches `bin/BinExtractALF.exe` | Windows-only test helper, not a shipped runtime dependency | Skip/replace on non-Windows CI; runtime ALF/AAI readers do not depend on it |
| Native RE tools | Frida/Ghidra helpers target the original `AGE.EXE`; supporting utilities include Windows executables and Windows command conventions | Development/research only | Keep separate from export requirements; document platform prerequisites per tool |
| Python workflow | Operating guide uses Windows `py -3.11` invocation | Developer workflow only | Add equivalent `python3` instructions if non-Windows development becomes active |

No authored runtime code currently calls native DirectSound, Direct3D, Vulkan, Metal, or OpenGL. Mentions of
those APIs in `docs/engine-re.md` describe the original AGE implementation. Both the software-oracle display
and retained GPU prototype use Godot abstractions, so accepting the GPU backend does not add an OS graphics
dependency.

## Movie backend replacement seam

The live connection is now backend-neutral:

```
VM op 0x236 (non-modal) / op 0x20f (modal)
  -> IHost.PlayMovieToSurface / PlayModalMovieToSurface
  -> VFS-owned MoviePayload bytes
  -> IMovieDecoderFactory
     -> FfmpegMovieDecoder
        -> FfmpegMovieSession -> age_movie C ABI
  -> newest due RGBA frame -> retained movie surface -> Godot compositor
  -> timestamped stereo float PCM -> per-playback AudioStreamGenerator
```

Everything before and after the selected decoder is portable. The replacement decision is an in-process
FFmpeg backend behind a project-owned C ABI, not raw FFmpeg structs in Godot/C# and not a subprocess. FFmpeg
`n8.1.2-29-g703dcc25b9` is pinned by immutable release URL and SHA-256 in
`native/age_movie_ffmpeg/dependency-win64.json`; changing that pin requires rerunning the full installed-movie
gate. The shim dynamically links an LGPL build made without GPL or nonfree components and uses
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
arbitrary system FFmpeg. The first implementation gate is Windows x64 because that is the current runnable
target, but the C ABI and loader paths must reserve Windows x64, Linux x64, macOS x64, and macOS arm64 from the
start. Builds use shared libraries, `$ORIGIN`/`@loader_path`-style local lookup on Unix targets, recorded source
hashes and configure arguments, and no committed original-game data. Packaging automation is part of completing
the backend, not a prerequisite for the first native decode spike.

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
was deleted after the later synchronized-audio gate. Full
export/source-offer packaging and non-Windows builds remain outstanding.

The Windows-x64 installed-corpus gate passed on 2026-07-22 with the pinned dependency: all 213 signature-selected
MPEG payloads opened and reached EOF under a 30-second per-item bound. It decoded all 15,788 frames across twelve
display sizes (120x120 through 800x600), independently matched sequence-header dimensions, found positive
duration and frame-rate metadata, verified tightly packed RGBA size and nondecreasing timestamps, observed
changing imagery in every asset, and closed every session. The set includes all 184 video-only and 29 audio-bearing
streams; at that video-only milestone audio presence was detected but PCM remained intentionally undelivered.
The later ABI-v2 gate above supersedes that limitation. The 263-second `ED.AGF` was the
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
