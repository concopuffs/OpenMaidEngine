# Asset Resolution — foundational RE (graphics + audio)

**The problem.** The bytecode loads assets by a small numeric **resource id** (`set-texture 0x23`,
`play-voice N`, …). To render/play the *real* asset — driven by the bytecode, not hardcoded — the
engine must resolve `resId → asset file`. This is **foundational** (nearly all visuals + all audio
depend on it) and **not machine-verifiable** (no pixel/audio oracle), which makes it the largest,
highest-risk area of the port. This doc is the steering state; it feeds the A2b render/audio slices.

## What's already landed

- **Graphics ops wired** (A2b-background, engine-driven): `create-texture 0x1f8` `(slot,w,h)`,
  `set-texture 0x1f9` `(resId,slot)`, `draw-texture 0x1fb` `(slot,x,y,w,h)` promoted from VM stubs to
  typed `IHost` methods; `CaptureHost` no-ops them (A1 trace-diff/A2a selftest stay green). The VM
  now *drives* graphics; only resolution + backend rendering remain.
- **Audio ops WIRED (2026-07-06):** `play-bgm 0xbf` / `play-voice 0xc4` → `IHost.PlayBgm/PlayVoice` →
  same resolver → OGG via Godot `AudioStreamPlayer`. See step 4 below.
- **Tools:** `tools/convert_agf.py` (AGF→BMP for *stills* via `AGF2BMP2AGF.exe`); `tools/frida/`
  (runtime capture harness — see its README); Frida core installed (17.15.3).

## Findings (2026-07-06)

- **SC0000 background = a slot-0 full-screen slideshow.** The intro loads ~30 distinct full-screen
  images into slot 0 in order (`set-texture 0x23→0`, `0x25→0`, `0x27→0`, …), each drawn 800×600.
  Res `0x23` is the first. (There is also a persistent full-screen **slot 3** set *cross-context*,
  not in SC0000 — inherited from the parent/system scene.)
- **The opening mixes movies + stills.** `AGF2BMP2AGF` reports `OP.AGF`/`MVB*.AGF` as
  "unsupported type (possibly MPEG)" → DATA5 `MVB*` (210) and `OP`/`ED` are **movies**, not stills.
  The opening's visible background did **not** match any `EV001*` still (confirmed by eye), so res
  `0x23`'s file is not obvious from the name space alone — resolution is required.
- **Asset name spaces:** DATA2 = `EV*`/`EVM*` stills (985). DATA5 = `MVB*`/`OP`/`ED` movies (210).
  DATA3 = `.OGG` audio (`BGM*`, `ANA*` voice).
- **The resolution chain is opaque statically.** `CGINIT` (`build/data/CGINIT.json`) is a
  925-column *numeric* record table (row-major, sparse) — **not** an id→filename map.
  **`SYS4INI.BIN` (magic `S4IC422`) is the authoritative asset index** the game + `BinExtractALF`
  use (name ↔ archive ↔ offset ↔ size). Filenames aren't plain ASCII because the whole directory
  is **LZSS-compressed** (not encrypted). **DONE (2026-07-06):** `tools/parse_sys4ini.py` parses it
  → `build/asset-index.json` (13206 entries). See step 1 below.
- **Frida file-I/O is noisy.** `ReadFile` hooks on `DATA2.ALF` capture reads during the opening, but
  the offsets/spans don't line up with extracted AGF sizes → the game likely **memory-maps** the
  archives (so `ReadFile` offsets are OS paging, not clean per-asset loads) and/or uses async reads.
  The robust hook is the game's **internal load-by-id function**, not file I/O.

## The RE plan (ordered)

1. **Parse `SYS4INI` (S4IC422) → an asset index** `{name, archive, offset, size}`. **✅ DONE
   (2026-07-06).** `tools/parse_sys4ini.py` → `build/asset-index.json`: 5 archives (DATA1–5),
   13206 real entries (2 `@` placeholders skipped). **Format:** `uint32 packed_size @0x134`, then an
   LZSS stream at `0x138` running to EOF (GARbro-style: 0x1000 zero-filled ring buffer, init pos
   0xFEE, control bits LSB→MSB, 1=literal / 0=two-byte backref `off=(hi&0xf0)<<4|lo`, `len=3+(hi&0xf)`).
   Decompresses to `uint32 arc_count`, `arc_count × char[256]` archive names, `uint32 file_count`,
   then `file_count ×` 80-byte records `{char name[64]; u32 arc_id, file_number, offset, size}`.
   **Validated:** decompressed length (1058783) equals the stored size dword at `0x12c`; per-archive
   counts match the `extracted/` ground truth exactly (DATA2=985, DATA3=39, DATA4=9733, DATA5=210);
   all 13206 `offset+size` fit inside their real `.ALF`; 837 name-matched files → 0 size mismatches.
   `files[]` preserves directory order (feeds step 2's order-correlation). Re-run:
   `py -3.11 -X utf8 tools/parse_sys4ini.py --check`. (Ref: asmodean's `exs4alf` / GARbro Eushully `ArcALF.cs`.)
2. **Resolve `resId → asset file`.** **✅ SOLVED for scene-manifest references (2026-07-06);
   system/global raw ids are a separate path identified 2026-07-10.**

   **The rule:** SYS4INI's file list is organized into **SECTIONS, one per scene** — each is a
   `SCxxxx.BIN` script entry followed by that scene's **asset MANIFEST**: every asset it references,
   across *all* archives and types (EV/BG/CS/AE graphics **and** OGG/WAV audio), interleaved in usage
   order. `file_number` is the **0-based index within the section**. So:

   > **`resId → files[ section_base(scene) + resId ]`**, where `section_base` = the start of the SYS4INI
   > section containing the scene's `SCxxxx.BIN`.

   Manifest rule holds for `set-texture(resId)` and `play-voice(id)`. **⚠ `play-bgm` is the EXCEPTION —
   it does NOT use the manifest; it uses direct literal names `BGM{id:03d}.OGG` (see step 4, by-ear
   corrected 2026-07-06).** **Tool:** `tools/resolve_asset.py --build` → `build/asset-sections.json`
   (359 sections, 136 scenes); `resolve_asset.py <SCENE> [resId]` resolves. **Validated:** `file_number ==
   position − section_base` for 12848/13206 files (97%); SC0000 resolves 17/17 across archives vs the Frida
   capture (`0x25→EV052CA`, `0x36→BG030A` background, `0x6c→EM* effect`); 586/595 distinct captured loads
   (all sections) satisfy `files[base+fn]==name`. This is the derivable rule that generalizes to any
   AGE game with the same container — **the "scope" was just which SYS4INI section the scene lives in.**
   (The old `play-bgm 5→BGM006` validation point was a mis-attribution — the real game plays BGM005.)

   **System/global-id exception (identified 2026-07-10; not yet implemented).** Some SYSTEM4 loads use
   the SYS4INI record's universal `raw_index` directly, including the two `@` placeholder records, rather
   than a scene-local manifest index. `SYSTEM4.BIN` writes `G[0x69b]=0x337e`, then
   `set-texture(G[0x69b], slot=0x11)`. SYS4INI `raw_index 0x337e` is `DATA1/SO001.AGF`, the shared
   800×300 RGBA system-chrome sheet. The filtered `files[]` list omits placeholders, so treating `0x337e`
   as a `files[]` position currently mis-resolves it to `SETROUTE.BIN`. This path needs a distinct
   `raw_index → entry` lookup; the scene-manifest rule above remains correct for SC texture/voice ids.

   *How we got here (condensed):* first confirmed `resId == file_number` via Frida load-order correlation
   for SC0000's opening, but `file_number` is not globally unique so a per-scene "scope" was needed. A long
   hunt for the selector (thought it was native scene state; even tried reading `G[0x62424]` live — the
   VM global memory is structured/packed, see `docs/global-memory-re.md`) missed the real structure until a
   **full multi-archive capture** (user domain tip: DATA1 holds BG/CS/CB/CA/CP graphics by name prefix, not
   just DATA2 EV CGs) revealed `file_number == SYS4INI position` inside per-scene sections. Superseded tools:
   `tools/correlate_scope.py`, `vm0.py --settex` (VM set-texture trace; still useful, but vm0 diverges on
   branchy non-opening scenes — use the C# VM to trace those). Runtime note for future work: the game is
   **packed** (main VM logic in a per-run heap `r-x` region) and streams archives through a heap block-cache
   via `ReadFile` (not mmap); the stable AGF decoder is `AGE.EXE+0x74f1f`.
3. **Wire the backend.** **✅ FIRST-PASS RENDER LANDED (2026-07-06).** `Age.Engine/Sys4/ResourceMap.cs`
   (Resolve + BMP path) + `GodotAdvHost` texture ops → `TextureRect` compositing behind the dialogue;
   `IHost.DrawTexture` extended with dst x/y; 800×600 window; `convert_agf.py --scene` pre-converts a
   scene's manifest AGFs → BMP. The full-screen **event-CG layer renders end-to-end** from the executed
   bytecode. **Historical limitations at first landing (subsequently resolved in the Phase-A graphics
   slices):** sprites + `BG*` (routed through the CG-load subroutine) had garbage geometry because native
   graphics ops were stubbed (`0x208` get-texture-size + the sprite position/animation chain); fades
   (`AE*`) drew opaque (no alpha); the slot
   model approximated the game's immediate-mode blit-onto-slot-0 canvas. See `docs/phase-a-slice-plan.md`
   (A2b section) for the implementation history and current retained-object model.

   **Current system-chrome shortcut/gap (confirmed 2026-07-10).** `Main --boot` executes only
   `INITCONFIG/INIT2/INIT` through `CaptureHost` and copies their globals into the scene VM; it does not
   replay SYSTEM4's graphics side effects through `GodotAdvHost`. `convert_agf.py --scene SC0000` also
   converts only SC0000's manifest, so `build/textures/SO001.BMP` is absent. `CALLBACK_WINDOW.BIN` expects
   slot `0x11` to already contain SO001, draws the 800×227 textbox from `(0,0)`, and crops the lower-right
   buttons from the same sheet. In the port slot 17 is unpopulated, so retained handle `0xd2f0` resolves as
   a colored surfaceless object and the compositor draws the observed opaque black fill. A temporary decode
   verified SO001 is 800×300, 32-bpp, with substantial per-pixel alpha; the current rasterizer already
   consumes source alpha. The missing prerequisites are system-asset resolution/conversion and retained
   slot initialization, not new textbox drawing or button interaction.
4. **Audio.** **✅ WIRED (2026-07-06) — no Frida needed.** Same rule as textures:
   `play-bgm(id)`/`play-voice(id)` → `files[section_base(scene)+id]` → OGG. `IHost.PlayBgm/PlayVoice` +
   VM dispatch (`play-bgm` 0xbf / `play-voice` 0xc4, both argc 1); `ResourceMap.AudioPath` → loose
   `extracted/DATA{n}/{name}.OGG`; `GodotAdvHost` → `Main`'s two `AudioStreamPlayer` nodes
   (`AudioStreamOggVorbis.LoadFromBuffer`; BGM loops, voice interrupt-on-new). Non-Godot hosts no-op it
   → `--selftest`/8-8 byte-identical. SC0000 fires 18 BGM + 198 voice. **By-ear VALIDATED
   (2026-07-06):** voices play on their lines (`play-voice` med→HIGH). **BUT the two audio ops use DIFFERENT
   addressing — the earlier "unified graphics+audio manifest" claim was WRONG for BGM:**
   - **Voice** (`play-voice`) → per-scene manifest, `files[base+id]`, **offset 0** (same as textures). Proven:
     the manifest interleaves graphics/voice (`files[35]=EV049AA`, `[36]=MAN999`, `[37]=EV052CA`, `[38]=SYL0001`),
     so `id-1` would land voices on `.AGF` (silent) — they play, so offset is exactly 0.
   - **BGM** (`play-bgm`) → **DIRECT LITERAL NAME**, `id → BGM{id:03d}.OGG` (DATA3), NOT the manifest.
     Confirmed by ear (`play-bgm 5→BGM005`, `8→BGM008`; the manifest gave BGM006/009 = off-by-one) and proven
     by `play-bgm 0x23→BGM035.OGG` — a real standalone track (BGM set skips 030-034) the manifest mis-resolved
     to a graphics entry. Implemented as `ResourceMap.BgmPathById(id)`; `GodotAdvHost.PlayBgm` uses it.
     The prior "Frida-confirmed play-bgm 5→BGM006" record was a mis-attribution.

   Lily silent = correct (form-gated on `G[0xa57/0xa58/0xa59]`, unseeded). `play-sound-effect` (0xb4, argc 2)
   left stubbed — arg roles unconfirmed. See `docs/phase-a-slice-plan.md` (A2b-Audio). Diagnostic: `Age.Cli
   audio <SCENE>`.
5. **Movies** (`OP`/`MVB`, MPEG) — a separate video-playback path; deferred.

## Validation reality (why this is the big haul)

Unlike the VM/dialogue work (byte-exact trace oracle), graphics + audio have **no machine oracle**.
Validation is: **Frida ground truth** (what the real game loads/plays for a scene) as the correctness
anchor, plus **human eyeball/ear**. Treat every mapping as provisional until Frida-confirmed; the
`resId→file` map is *data we curate against ground truth*, and the engine stays honest by only ever
rendering what the executed bytecode + the map produce (never a hardcoded image).

## Status

A2b-background: **steps 1–3 landed.** Step 1 = `build/asset-index.json`. Step 2 = **`resId →
files[section_base(scene) + resId]`** via SYS4INI per-scene sections (`tools/resolve_asset.py` +
`build/asset-sections.json`) — no runtime capture, all archives/types + audio. Step 3 = **first-pass
render** (ResourceMap + GodotAdvHost texture ops → TextureRect compositing): the full-screen event-CG
layer renders end-to-end from the bytecode. Remaining (next chunk): the **graphics geometry/blend
subsystem** — native geometry ops (`0x208` + sprite position/animation) so sprites/`BG*` position, plus
alpha/blend for fades + chromakey. See `docs/phase-a-slice-plan.md` (A2b). Audio (step 4): **`play-voice`
uses the manifest** (`files[base+id]`); **`play-bgm` uses direct names** (`BGM{id:03d}.OGG`) — NOT unified.

## Native SFX resource proof (2026-07-11)

SFX uses the same scene-local rule as graphics and voice: `files[section_base(scene)+resource_id]`.
The matching native trace at SC0000 `0xc29` captures resource `0x28`, channel 0; static resolution yields
`DATA1/E0808.WAV`, and the port trace resolves the same file. The following `0xc31` preload uses the same
resource on native secondary channel 4. `play-bgm` remains the separate direct-name exception.

The current Phase-A backend deliberately continues through the extracted-file bootstrap: `ResourceMap.AudioPath`
accepts both OGG and WAV and Godot loads the WAV bytes into its fixed SC0000 channel pool. This does not change
the scoped VFS plan below: ALF/AAI mounting and in-process asset reads remain a separate foundation track.

## Runtime asset-VFS track (VFS-A and VFS-C complete 2026-07-11)

The pre-extracted tree and `build/textures/*.BMP` pipeline were a Phase-A bootstrap, not the desired final
runtime. The native-compatible target is a read-only virtual filesystem that preserves AGE's translation/mod
behavior:

> resolve the resource record → try a loose file with that record's name in the game/mod root → otherwise
> read exactly `offset..offset+size` from the record's ALF → decode the contained format in process.

Native evidence already proves this ordering for scripts: `resource_open_by_raw_id@0x44f390` indexes the
80-byte SYS4 record and calls `CreateFileA(record.name)` before opening `record.archive`, seeking to
`record.offset`, and reading `record.size`. The same service is the correct common seam for scripts,
graphics, voice/SFX, and later movie bytes. Resolution and opening must remain separate: scene-local ids and
universal `raw_index` ids select a record differently, but both records flow through the same loose-first
store.

### Proposed layers

1. **Catalog + read-only ALF store (VFS-A DONE).** `Sys4AssetCatalog` parses SYS4INI at runtime while preserving all 13208 raw records
   (including the two `@` placeholders), archive names, scene sections, and the existing three lookup modes:
   universal raw id, scene-local manifest id, and direct name where the opcode family genuinely uses one.
   An ALF is a payload container at this layer: open the named archive and return a bounded stream/byte range
   at the indexed offset/size. Before that fallback, probe the configured loose override roots by the record's
   exact basename. `Sys4AssetStore` opens a separate read-only file handle per request and constrains archive
   seek/read operations to the record range. `Sys4ScriptProvider` now loads both root scenes and nested
   `call-script` targets through this seam; `ResourceMap` uses the same live catalog. `build/asset-index.json`,
   `build/asset-sections.json`, `build/callscript-names.json`, and `extracted/` are validation/temporary
   graphics-audio artifacts, not script-runtime dependencies.
2. **AAI append mount.** Parse the installed `APPEND01.AAI` (`S4AC422`) and its paired `APPEND01.ALF` with
   the same catalog abstractions. First prove whether Himegari joins append records by a separate pack/tag,
   by name replacement, or by another table selected by the native high-byte-id path; do not invent mount
   precedence. Validate every parsed append entry against `BinExtractALF.exe` output before exposing it to
   the runtime.
3. **AGF decoder (VFS-C DONE).** `Age.Engine/Sys4/AgfDecoder.cs` decodes an opened AGF payload directly
   to a tightly packed, top-down width/height + RGBA8 surface. The MIT-licensed GARbro
   `ArcFormats/Eushully/ImageAGF.cs` provides a compact reference: `ACGF` (or zero) signature, type 1/2,
   LZSS-or-raw header section, 4/8/truecolor source pixels, LZSS-or-raw pixel section, bottom-up row/stride
   conversion, and optional `ACIF` LZSS alpha plane. Port only the algorithm and attribution into
   platform-neutral .NET code; do not carry GARbro's WPF/GameRes dependencies. Kelebek's extractor and the
   on-disk `BinExtractALF.exe` are validation references; the Kelebek repository exposes no clear license,
   so its code should not be copied without clarification. The focused `LzssDecoder` is shared with
   `Sys4AssetCatalog`; raw and compressed information/pixel/ACIF sections use the same bounded primitive.
4. **Runtime consumers.** Script and texture loading are complete. `ResourceMap.ResolveTexture` preserves
   scene-local resolution and falls back to universal raw ids for SYSTEM4 assets; `GodotAdvHost` caches
   decoded RGBA surfaces by catalog identity and supplies synchronous dimensions to opcode `0x208`.
   Godot no longer reads `build/textures/*.BMP`. OGG/WAV byte migration remains a separate follow-up;
   retain extraction/conversion tools as diagnostics until parity is established.

### Acceptance gates

- Catalog: 13208 raw slots / 13206 real base entries; every ALF range is in bounds; scene-local mappings
  remain identical to the current resolver and `raw_index 0x337e` resolves to `SO001.AGF`.
- Store: representative base reads are byte-identical to `extracted/`; a temporary loose file with the same
  record name wins, and removing it deterministically reveals the archive bytes. Root path traversal is
  rejected and archive reads are bounded/thread-safe.
- AAI: entry names/counts/ranges and representative bytes match a disposable `BinExtractALF APPEND01.AAI`
  extraction; the native mount/selection rule is documented before integration.
- AGF: a sample matrix covers compressed/uncompressed sections, 4/8/24-bit source pixels, type 1/2, and
  alpha/no-alpha. Decoded dimensions and RGBA hashes/pixels match GARbro or `AGF2BMP2AGF`; `SO001.AGF`
  specifically decodes as 800×300 with its alpha plane intact.
- End to end: SC0000 can run without consulting `extracted/` or `build/textures`; slot 17 receives SO001,
  the translucent textbox/button chrome appears, root `.BIN` overrides still win, and the standard VM/Godot
  validation matrix remains green.

VFS-A passes these bounded gates in `Sys4AssetStoreTests`: every catalog field matches the generated
diagnostic index, all 136 scene views match the generated section oracle without cross-section spill, all
13206 archive ranges fit, `raw_index 0x337e` is `SO001.AGF`, representative payloads from every base archive
are byte-identical to `extracted/`, and synthetic removal of a loose override reveals the bounded ALF bytes.
Traversal, past-range seek/read, and concurrent reads are covered. Installed override enumeration corrected
an older inventory error: this tree contains 51 loose root BINs, comprising **49 archive-backed v1.03 script
overrides** (all byte-proven to win and differ from DATA1) plus root-only `SYS4INI.BIN` and `SYS4AB.BIN`.
There are not 52 archive copies available to shadow.

VFS-C passes its bounded gates in `AgfDecoderTests`: synthesized fixtures cover raw/compressed sections,
4/8-bit palettes, 24/32-bit truecolor expansion, padded bottom-up rows, type 1/2, and ACIF/no-ACIF alpha.
Five installed assets spanning raw/compressed metadata and pixel/alpha combinations match the existing
`AGF2BMP2AGF` BMP oracle pixel-for-pixel. SO001 resolves through universal raw id `0x337e`, decodes to
800×300 with intermediate alpha values, and is inherited in surface slot 17 before SC0000. A windowed
page-1 capture with `build/textures/` moved aside showed the translucent textbox edge and bottom-right
controls. Texture runtime no longer consults `extracted/` or `build/textures/`; current audio consumers
still use extracted OGG/WAV paths by design. APPEND01/AAI, audio migration, and movie `0x236` remain
unimplemented by this slice.

### Deliberate non-goals

- Writing/repacking ALF or AAI; loose overrides already provide the native mod/translation workflow.
- AGF encoding, movie/video decoding for MPEG-like `OP/MVB*.AGF`, or SFX channel semantics.
- A generalized multi-mod dependency manager. Start with native game-root loose overrides; configurable
  ordered mod roots can be layered onto the same store later.
- Removing the extraction/conversion tools immediately. They remain independent parity oracles until the
  runtime readers have broad corpus coverage.

Primary implementation reference: [GARbro's Eushully AGF reader](https://github.com/morkt/GARbro/blob/master/ArcFormats/Eushully/ImageAGF.cs)
and [ALF reader](https://github.com/morkt/GARbro/blob/master/ArcFormats/Eushully/ArcALF.cs), MIT licensed.
Secondary validation reference: [Kelebek's extractor](https://github.com/Kelebek1/Eushully-Decompiler/blob/master/extract_alf.py).
