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

The current runtime's only direct native Windows API use is the movie decoder described below. There are
also softer Windows assumptions that should be tested or replaced before claiming portable exports.

## Dependency inventory

| Area | Current dependency | Runtime impact | Portability status / future action |
|---|---|---|---|
| AGE movie decode (`0x236` scene movies; `0x20f` modal LOGO/OP/ED) | `godot/DirectShowMovieDecoder.cs`: DirectShow COM objects plus `ole32.dll` `CoInitializeEx` / `CoUninitialize` | Movie playback cannot run outside Windows | Introduce a decoder interface and backend factory; retain DirectShow on Windows while adding a portable MPEG backend |
| Movie integration | `godot/Main.cs` directly constructs and stores `DirectShowMovieDecoder`; `Main` is marked `SupportedOSPlatform("windows")` | The frontend has no runtime fallback or OS-specific source selection | Type `MovieRuntime` against the decoder interface, select by platform/build, and move the Windows annotation to the DirectShow backend |
| Movie audio | DirectShow connects only the video pin to the sample grabber/null renderer | The MPEG audio stream is intentionally silent on every platform | Design a PCM/audio-clock contract or let a future backend own synchronized A/V; separate feature slice |
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

The existing connection is localized but one abstraction short of being replaceable without edits:

```
VM op 0x236 (non-modal) / op 0x20f (modal)
  -> IHost.PlayMovieToSurface / PlayModalMovieToSurface
  -> VFS-owned MoviePayload bytes
  -> DirectShowMovieDecoder
  -> newest RGBA frame
  -> retained movie surface
  -> Godot compositor
```

Everything before and after `DirectShowMovieDecoder` is portable. The backend currently exposes the right
conceptual operations (`StopTimeMs`, `TryTakeFrame`, `IsCompleted`, and `Dispose`) but they are not formalized as an
interface. A future cleanup should:

1. Add an `IMovieDecoder` contract for synchronous stop-time metadata, frame delivery, completion, failure,
   and disposal. Stop time must be available before `0x236` returns so an immediately following `0x23f`
   remains deterministic.
2. Add an injected factory that accepts `MoviePayload` and selects an available backend.
3. Keep DirectShow in a Windows-specific source set or assembly, with its platform annotation local to it.
4. Implement a portable MPEG program-stream backend that produces the same top-down RGBA8 frames.
5. Preserve newest-frame-wins delivery, asynchronous decode, synchronous open failure, EOF notification,
   and the retained-surface lifecycle already validated for opcode `0x236`; expose modal completion/cancel
   separately for `0x20f` rather than blocking inside the decoder backend.
6. Treat synchronized movie audio as a separate extension of the contract rather than coupling it to the
   compositor.

An OS-specific build is also viable: include DirectShow only in Windows exports and a different decoder in
other exports. The current plain `net8.0` project has no conditional backend selection. COM declarations may
compile on another OS, but `[SupportedOSPlatform]` is analyzer metadata rather than a runtime guard, and
loading `ole32.dll` or DirectShow CLSIDs will fail there.

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
