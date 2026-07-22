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

The selected movie path now uses the project-owned FFmpeg C ABI rather than a Windows multimedia API, but only a
Windows-x64 native bundle is built and staged today. The retired-live DirectShow implementation remains in-tree
until the corpus and manual gates pass. There are also softer Windows assumptions that should be tested or
replaced before claiming portable exports.

## Dependency inventory

| Area | Current dependency | Runtime impact | Portability status / future action |
|---|---|---|---|
| AGE movie decode (`0x236` scene movies; `0x20f` modal LOGO/OP/ED) | `FfmpegMovieDecoder` is the selected live factory over the project-owned `native/age_movie_ffmpeg` ABI; `DirectShowMovieDecoder` remains unselected pending deletion | Windows-x64 live playback now covers formerly rejected 280-wide effects, but other native targets and the full 213-payload gate remain | Run the corpus and windowed live gates, then delete DirectShow and add target-specific native builds |
| Movie integration | `MovieRuntime` owns `IMovieDecoder` from an injected factory; the FFmpeg worker paces PTS against a monotonic clock and supports cancellation/failure completion | Backend ownership is portable, while `Main` remains annotated Windows because only the win-x64 bundle is available | Add Linux/macOS builds and remove the Windows annotation after DirectShow is deleted |
| Movie audio | FFmpeg detects the audio stream but the current ABI returns video frames only | MPEG movie audio remains intentionally silent | Extend the ABI with timestamped PCM and select an audio/presentation clock; separate feature slice |
| ADV font discovery | `godot/Main.cs` probes `C:/Windows/Fonts` for Japanese fonts | Harmless fallback today, but appearance depends on host fonts | Bundle/configure a redistributable font or add platform-specific discovery |
| Filesystem semantics | Several filename and containment comparisons use `OrdinalIgnoreCase`; installed assets are conventionally uppercase | Needs validation on case-sensitive filesystems; may hide casing or containment mistakes | Add Linux/macOS tests with mixed-case synthetic roots and use filesystem-appropriate containment rules |
| Install/repository discovery | `engine/Age.Engine/Sys4/Paths.cs` finds `age-reimpl` above `AppContext.BaseDirectory` and assumes the current workspace sibling layout | Suitable for development, not packaged exports on any OS | Replace runtime discovery with a user-selected game root/profile; retain repository paths only for developer tools/tests |
| Archive parity oracle | One integration test launches `bin/BinExtractALF.exe` | Windows-only test helper, not a shipped runtime dependency | Skip/replace on non-Windows CI; runtime ALF/AAI readers do not depend on it |
| Native RE tools | Frida/Ghidra helpers target the original `AGE.EXE`; supporting utilities include Windows executables and Windows command conventions | Development/research only | Keep separate from export requirements; document platform prerequisites per tool |
| Python workflow | Operating guide uses Windows `py -3.11` invocation | Developer workflow only | Add equivalent `python3` instructions if non-Windows development becomes active |

No authored runtime code currently calls native DirectSound or Direct3D. Mentions of those APIs in
`docs/engine-re.md` describe the original AGE implementation. The port's ordinary audio and rendering use
Godot abstractions.

## Movie backend replacement seam

The live connection is now backend-neutral:

```
VM op 0x236 (non-modal) / op 0x20f (modal)
  -> IHost.PlayMovieToSurface / PlayModalMovieToSurface
  -> VFS-owned MoviePayload bytes
  -> IMovieDecoderFactory
     -> FfmpegMovieDecoder (current live selection)
        -> FfmpegMovieSession -> age_movie C ABI
     -> DirectShowMovieDecoder (unselected; retained only through acceptance)
  -> newest RGBA frame
  -> retained movie surface
  -> Godot compositor
```

Everything before and after the selected decoder is portable. The replacement decision is an in-process
FFmpeg backend behind a project-owned C ABI, not raw FFmpeg structs in Godot/C# and not a subprocess. FFmpeg
`n8.1.2-29-g703dcc25b9` is pinned by immutable release URL and SHA-256 in
`native/age_movie_ffmpeg/dependency-win64.json`; changing that pin requires rerunning the full installed-movie
gate. The shim dynamically links an LGPL build made without GPL or nonfree components and
uses only `libavformat`, `libavcodec`, `libavutil`, and `libswscale` for the video slice. `libswresample` and the
MPEG audio decoders may be packaged now, but PCM delivery remains a separate slice. Release artifacts must carry
the matching FFmpeg source/configuration and notices required by FFmpeg's
[license checklist](https://ffmpeg.org/legal.html).

The boundary has two layers:

1. A small native `age_movie` ABI owns all FFmpeg objects and version-sensitive calls. It accepts a borrowed
   byte span only for the duration of `open`, copies it into native-owned memory, creates a seekable custom
   `AVIOContext`, probes the MPEG program stream, and returns immutable video metadata. Sequential decode returns
   one top-down tightly packed RGBA8 frame plus its normalized presentation timestamp. Conversion occurs in an
   FFmpeg-owned aligned frame; only exact visible row bytes are copied into the caller's tightly packed buffer.
   Return statuses distinguish frame, EOF, invalid arguments, undersized output, and decoder failure; the open
   call returns a bounded UTF-8 diagnostic and an opened handle retains its last decode error. Close accepts a
   null handle, and managed `SafeHandle` ownership guarantees one close for each successfully opened handle. No
   FFmpeg pointer crosses the ABI.
2. Managed `IMovieDecoder` owns the native handle and the paced worker. Synchronous construction provides
   `StopTimeMs` before `0x236` returns. The worker uses a monotonic playback origin, decodes ahead by at most one
   frame, publishes frames when their timestamps become due, and retains newest-frame-wins behavior if Godot is
   late. EOF becomes completion only after the final frame's presentation interval/stop time, so surface cleanup
   cannot erase the last frame immediately. Disposal interrupts waits and joins without depending on Godot's main
   thread.

`StopTimeMs` performs a bounded packet scan over the seekable in-memory payload and takes the longest usable
FFmpeg format duration, video-stream duration, timestamp span, or constant-frame-rate packet-count duration. This
accounts for the final MPEG presentation interval that FFmpeg's stream duration can omit (`MVB908` is ten 30 fps
frames: 333 ms, not the raw stream field's 316 ms). The result is converted to integer milliseconds with the same
positive truncation used by native op `0x23f`, then the demuxer rewinds before playback. Streams with no usable
duration evidence fail initialization for the stock-content path rather than inventing timing. The existing host
safety rule still converts any backend initialization failure into an
explicitly completed zero-duration movie identity, and the presentation watchdog remains a last-resort guard for
an initialized backend that never reaches EOF.

The factory is injection for tests and future decoder replacement, not runtime codec roulette. FFmpeg is now the
selected Windows-x64 live backend; once its corpus and live gates pass, DirectShow is deleted rather than shipped
as a fallback. Modal
completion/cancel remains owned above the decoder by the existing `0x20f` host path. A future audio implementation
will consume timestamped PCM and may become the presentation clock; it must not change the VM-facing stop-time,
surface, or cancellation contracts.

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
DirectShow; the user subsequently confirmed both opening movies work in normal windowed playback. Full
export/source-offer packaging and non-Windows builds remain outstanding.

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
