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
2. **Resolve `resource_id → asset file`.** **NATIVE RULE CONFIRMED IN GHIDRA AND IMPLEMENTED
   (2026-07-21): resource operands are universal packed SYS4INI/AAI ids. There is no scene-relative path
   or fallback.**

   `asset_catalog_parse_base_tables@0x44e7e0` creates one flat base entry array in serialized SYS4INI
   order. `asset_open_indexed_entry@0x44f390` receives the operand unchanged. If its high byte is zero,
   native bounds-checks and directly indexes that base array. Otherwise the signed high byte selects a
   mounted AAI catalog and the low 24 bits index only that catalog. The selected record then follows exact
   loose-basename-first, bounded-ALF-second opening. The opener receives no executing-script identity,
   section base, or active manifest and contains no fallback branch.

   This one contract is shared by script loading, `set-texture` (`0x1f9`), mode-1 texture load (`0x249`),
   voice, SFX, cursor, and both movie paths. Type-specific facades should filter the selected packed record
   after lookup, not reinterpret the numeric id. `play-bgm` remains the separate direct-name exception:
   `BGM{id:03d}.OGG`.

   SC0010 provides the decisive static check that SC0000 could not because SC0000 begins at catalog zero.
   SC0010 bytecode executes `set-texture 0x21`; raw entry `0x21` is `SO013A.AGF`, while adding SC0010's
   catalog position `0x11e` lands on unrelated `COL0023.OGG`. Its first voices are `0x120`, `0x121`, and
   `0x122`, which directly name raw `LILA1414.OGG`, `LILB0053.OGG`, and `LILC0054.OGG`. Their
   `file_number` values 2/3/4 describe grouping inside the SC0010 run, but native bytecode already contains
   the absolute indexes `0x11e + file_number`.

   The former `files[section_base(scene)+resId]` model inverted that relationship. The 586/595 Frida
   correlation and the strong `file_number == position - group_start` pattern remain useful evidence about
   catalog construction/order, but they do not describe runtime resolution. SC0000 starting at zero hid
   the mistake, while later large ids often fell outside the invented scene range and happened to reach the
   port's former raw fallback. Low raw ids used from later scripts could instead be silently misresolved.
   `tools/resolve_asset.py` and `build/asset-sections.json` are therefore correlation/manifest-inventory
   diagnostics only; they must not drive runtime lookup.

   `0x1f9` and `0x249` also do not represent scene-local versus raw addressing. Both pass the same packed
   operand unchanged to the same opener and use the same colorkey/load path. Their native distinction is
   surface mode: `0x1f9` creates ordinary mode 0, while `0x249` creates the tiled large-image mode 1 wrapper.
   FIELD's `0x32da..0x32dd` map sheets and SYSTEM4's `0x337e` SO001 are ordinary examples of the universal
   base indexes used throughout the corpus, not special fallback cases.
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

   **System chrome ownership (resolved 2026-07-20).** The normal Godot entry now runs SYSTEM4 as the live
   root, so its SO000/SO001 loads, ADV-layout definitions, and retained slot initialization execute through
   `GodotAdvHost` before TITLE and child scenes. Asset lookup decodes VFS-owned AGF bytes directly and does
   not depend on a scene-limited converted BMP. `CALLBACK_WINDOW.BIN` consequently inherits slot `0x11`
   with SO001 and can crop its textbox/buttons normally. The explicit `--scene SC0000 --boot` diagnostic
   still injects the same known inherited layout and surface state because it intentionally bypasses
   SYSTEM4; that shortcut is no longer the shipped/default route.

   Resource lookup is independent of the active VM frame. Every texture surface retains the packed catalog
   id supplied by bytecode, and nested helpers/sibling scripts use the same global base/append catalogs.
   Script-context bracketing remains useful for diagnostics/page location, but it must not alter texture,
   voice, cursor, SFX, or movie resolution.
4. **Audio.** **BGM/voice/SFX wired; VFS bytes complete (2026-07-11); packed-raw SFX corrected 2026-07-20.** `IHost.PlayBgm/PlayVoice` +
   VM dispatch (`play-bgm` 0xbf / `play-voice` 0xc4, both argc 1); `ResourceMap.ReadAudio` opens the
   resolved catalog entry through `IAssetStore`; `GodotAdvHost` passes the bytes to `Main`'s players
   (`AudioStreamOggVorbis.LoadFromBuffer`; BGM loops, voice interrupt-on-new). Non-Godot hosts no-op it
   → `--selftest`/8-8 byte-identical. SC0000 fires 18 BGM + 198 voice. **By-ear VALIDATED
   (2026-07-06):** voices play on their lines (`play-voice` med→HIGH). **BUT the two audio ops use DIFFERENT
   addressing:**
   - **Voice** (`play-voice`) → the universal packed SYS4INI/AAI resource contract shared with textures
     and movies. Native passes the bytecode operand unchanged to `asset_open_indexed_entry`.
   - **BGM** (`play-bgm`) → **DIRECT LITERAL NAME**, `id → BGM{id:03d}.OGG` (DATA3), not the packed
     resource table. Confirmed by ear (`play-bgm 5→BGM005`, `8→BGM008`) and proven
     by `play-bgm 0x23→BGM035.OGG` — a real standalone track (BGM set skips 030-034) the old scene model mis-resolved
     to a graphics entry. Implemented as `ResourceMap.ResolveBgm(id)`; `GodotAdvHost.PlayBgm` uses it.
     The prior "Frida-confirmed play-bgm 5→BGM006" record was a mis-attribution.

   Lily silent = correct (form-gated on `G[0xa57/0xa58/0xa59]`, unseeded). SFX WAV entries use universal
   packed catalog ids through the same byte store while retaining the existing channel lifecycle. See
   `docs/phase-a-slice-plan.md`.
   Diagnostic: `Age.Cli audio <SCENE>`.
5. **Movies** (`OP`/`MVB`, MPEG) — `0x236` and `0x20f` receive universal packed resource ids and read the
   selected payload through `IAssetStore`; see the movie sections below.

## Validation reality (why this is the big haul)

Unlike the VM/dialogue work (byte-exact trace oracle), graphics + audio have **no machine oracle**.
Validation is: **Frida ground truth** (what the real game loads/plays for a scene) as the correctness
anchor, plus **human eyeball/ear**. Treat every mapping as provisional until Frida-confirmed; the
`resId→file` map is *data we curate against ground truth*, and the engine stays honest by only ever
rendering what the executed bytecode + the map produce (never a hardcoded image).

## Status

A2b-background: **steps 1–3 landed; step 2's packed resolver was corrected 2026-07-21.** Step 1 =
`build/asset-index.json`. Step 2 originally normalized through inferred per-scene sections; native RE proved
runtime operands are already universal packed ids, and every typed runtime facade now uses that contract.
`tools/resolve_asset.py` and
`build/asset-sections.json` remain grouping/correlation diagnostics, not runtime inputs. Step 3 = **first-pass
render** (ResourceMap + GodotAdvHost texture ops → TextureRect compositing): the full-screen event-CG
layer renders end-to-end from the bytecode. Remaining (next chunk): the **graphics geometry/blend
subsystem** — native geometry ops (`0x208` + sprite position/animation) so sprites/`BG*` position, plus
alpha/blend for fades + chromakey. See `docs/phase-a-slice-plan.md` (A2b). Audio (step 4): voice uses the
universal packed catalog; **`play-bgm` uses direct names** (`BGM{id:03d}.OGG`).

## Native SFX resource proof (2026-07-11; addressing corrected 2026-07-20)

SFX uses the universal packed catalog rule shared by graphics/voice/movies. A zero high byte directly
indexes SYS4INI; a nonzero high byte selects the matching AAI mount and uses the low 24-bit index. The
matching native trace at SC0000 `0xc29` captures raw id `0x28`, channel 0, which is
`DATA1/E0808.WAV`; SC0000 being the first section previously hid the distinction. TITLE makes it decisive:
`0x2aea` is raw `SE020.WAV` for hover, `0x3321` is raw `SE015.WAV` for activation, and neither fits TITLE's
14-entry inferred group. `play-bgm` remains the separate direct-name family.

The Phase-A backend now resolves the OGG/WAV catalog entry and opens it through `IAssetStore`; Godot decodes
the returned bytes into its existing BGM, voice, and fixed SC0000 SFX channel players. The earlier
`ResourceMap.AudioPath` extracted-file bootstrap is retired.

Godot's WAV loader adds one host-specific compatibility boundary. SC0000 `0xc29` starts `E0808.WAV` with
the EV052DA glow. Its PCM is valid, but its trailing RIFF `LIST/INFO` fields contain Japanese CP932 text
(`IPRD`, `IGNR`, and `ICMT`). Godot assumes INFO text is UTF-8 and formerly emitted one Unicode warning per
invalid CP932 byte each time the sound was loaded; the duplicated SC0000 burst came from loading raw id
`0x28` on two channels. A complete extracted-corpus scan found 238 RIFF/WAVE files, 61 INFO chunks, no other
LIST type, and no invalid RIFF containers, explaining the same warnings around combat SFX.

The correction is deliberately confined to the Godot frontend. `RiffWaveSanitizer` removes only `LIST` chunks
whose form type is `INFO` from the transient byte array passed to `AudioStreamWav.LoadFromBuffer`, updates the
RIFF length, and preserves every functional chunk (`fmt `, `data`, `smpl`, `cue `, and unknown chunks)
byte-for-byte, including padding. The shared engine, original loose/archive payloads, and
`ResourceMap.ReadAudio` output remain untouched. The real E0808 Godot input shrinks from 688,570 to 688,336
bytes while retaining an identical PCM data chunk and loads headlessly without a Unicode warning. Synthetic
chunk/padding tests and the installed E0808 regression cover the adapter.

## Runtime asset-VFS track (VFS-A/B/C complete 2026-07-11)

The pre-extracted tree and `build/textures/*.BMP` pipeline were a Phase-A bootstrap, not the desired final
runtime. The native-compatible target is a read-only virtual filesystem that preserves AGE's translation/mod
behavior:

> resolve the resource record → try a loose file with that record's name in the game/mod root → otherwise
> read exactly `offset..offset+size` from the record's ALF → decode the contained format in process.

Native evidence proves this ordering for scripts: `asset_open_indexed_entry@0x44f390` indexes the
80-byte SYS4 record and calls `CreateFileA(record.name)` before opening `record.archive`, seeking to
`record.offset`, and reading `record.size`. The same service is the correct common seam for scripts,
graphics, voice/SFX, and movie bytes. Resolution and opening remain separate only by concern: the universal
packed id selects one record, then the store applies loose-first/archive-second opening. There is no
scene-local numeric addressing mode.

### Proposed layers

1. **Catalog + read-only ALF store (VFS-A DONE).** `Sys4AssetCatalog` parses SYS4INI at runtime while preserving all 13208 raw records
   (including the two `@` placeholders), archive names, diagnostic scene groupings, universal packed ids,
   and direct-name lookup where an opcode family genuinely uses it.
   An ALF is a payload container at this layer: open the named archive and return a bounded stream/byte range
   at the indexed offset/size. Before that fallback, probe the configured loose override roots by the record's
   exact basename. `Sys4AssetStore` opens a separate read-only file handle per request and constrains archive
   seek/read operations to the record range. `Sys4ScriptProvider` now loads both root scenes and nested
   `call-script` targets through this seam; `ResourceMap` uses the same live catalog. `build/asset-index.json`,
   `build/asset-sections.json`, `build/callscript-names.json`, and `extracted/` are validation/temporary
   graphics-audio artifacts, not script-runtime dependencies.
2. **AAI append mount (VFS-B DONE).** `Sys4AssetCatalog` also parses the installed `APPEND01.AAI`
   (`S4AC422`) and mounts its paired `APPEND01.ALF`. This is a separate selector-keyed catalog, not a
   filename overlay: AGE scans `*.AAI`, reads the selector at AAI header offset `0x108`, and stores the
   successfully loaded catalog in `mounted_aai[selector]`. A resource id with a nonzero high byte selects
   that slot; its low 24 bits index only the selected append table. Installed `APPEND01` is selector 1.
   Within that catalog the ordinary exact loose-record-name then ALF-range precedence still applies, so the
   literal `$1$...` names are preserved. A later successfully enumerated AAI with the same selector replaces
   the earlier pointer in the native loop; no base/append name replacement occurs. Native extracts the
   selector with arithmetic `SAR 24`; the port rejects sign-bit selectors rather than guessing behavior for
   ids that would index before AGE's mount table.

   **Append bootstrap/data sequence (static content verified 2026-07-24; native launch boundary open).**
   Installed `APPEND01` contains 81 records, including 39 SYS4 scripts. Record zero is
   `$1$AUTORUN.BIN` (`0x01000000`), a clean 43-instruction script. Its first 22 instructions call packed
   records `0x01000001..0x01000016` in exact order: append fragments for EBINIT, CNINIT, ITINIT, SKINIT,
   ILINIT, AFINIT, TRINIT, MAINIT, ALINIT, CDINIT2, MPINIT, LAINIT, OBINIT, STINIT2, RTINIT, CGINIT,
   SPINIT, CTINIT, CVINIT, CIINIT, VIINIT, and SCINIT. It then marks append state and installs
   `$1$CCINIT.BIN` at `0x01000017`, `$1$SCJUMP.BIN` at `0x01000018`, and the packed
   READY/CLOSE/ROUND and message handlers through `0x01000029` into existing dispatch globals. The
   called INIT fragments populate references to the append graphics and six append scenario scripts.

   These INIT fragments are additive bytecode, not same-name catalog overlays. In particular,
   `$1$EBINIT.BIN` (`0x01000001`) writes directly into the established EBINIT global arrays and adds
   sparse unit-definition rows 81 and 900..905; `$1$CNINIT.BIN` adds their display names and voice-family
   mappings. Mounting an AAI therefore only makes its packed records addressable. Executing its AUTORUN
   is the separate operation that materializes append data and registers append control flow.

   The base/loose script corpus contains no reference to packed id `0x01000000`. The shipped game must
   therefore enter the append AUTORUN outside the visible base script call graph, but the exact native
   launcher and its ordering relative to SYSTEM4/INIT2 have not yet been reversed. The current port can
   resolve and execute append scripts when given their packed ids, but natural SYSTEM4 boot does not yet
   launch mounted AUTORUNs. Do not treat VFS-B mount completion as append gameplay/bootstrap completion.
3. **AGF decoder (VFS-C DONE).** `Age.Engine/Sys4/AgfDecoder.cs` decodes an opened AGF payload directly
   to a tightly packed, top-down width/height + RGBA8 surface. The MIT-licensed GARbro
   `ArcFormats/Eushully/ImageAGF.cs` provides a compact reference: `ACGF` (or zero) signature, type 1/2,
   LZSS-or-raw header section, 4/8/truecolor source pixels, LZSS-or-raw pixel section, bottom-up row/stride
   conversion, and optional `ACIF` LZSS alpha plane. Port only the algorithm and attribution into
   platform-neutral .NET code; do not carry GARbro's WPF/GameRes dependencies. Kelebek's extractor and the
   on-disk `BinExtractALF.exe` are validation references; the Kelebek repository exposes no clear license,
   so its code should not be copied without clarification. The focused `LzssDecoder` is shared with
   `Sys4AssetCatalog`; raw and compressed information/pixel/ACIF sections use the same bounded primitive.
4. **Runtime consumers (packed-id correction complete 2026-07-21).** Script loading, textures, voice,
   SFX, cursors, and modal/non-modal movies all select through `ResolvePacked` before type filtering. The VM
   retains bytecode operands unchanged instead of asking the host for scene normalization. Godot caches
   decoded RGBA and movie identities by the full packed id, preserving AAI selectors rather than colliding
   with equal low indexes in the base catalog. It supplies synchronous dimensions to opcode `0x208` and no
   longer reads `build/textures/*.BMP`. BGM remains direct-name. Focused regressions cover SC0010's low
   `0x21` texture and `0x120..0x122` voices plus append-pack identity. Extraction, grouping, and conversion
   tools remain diagnostics.

### Acceptance gates

- Catalog: 13208 raw slots / 13206 real base entries; every ALF range is in bounds; packed base/append
  selection matches native and `raw_index 0x337e` resolves to `SO001.AGF`.
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
controls. Texture runtime no longer consults `extracted/` or `build/textures/`.

Audio byte migration passes its bounded gate in `Sys4AssetStoreTests`: archive-only SC0000 reads identify
`BGM005.OGG` and `MAN999.OGG` as Ogg streams and `E0808.WAV` as RIFF/WAVE without consulting `extracted/`.
A windowed SC0000 run with `extracted/` moved aside crossed both voice sites and the first SFX sequence,
recording BGM005 plus `E0808.WAV` load/start/preload on channels 0/0/4 with no Godot OGG/WAV decode errors.
Channel, loop, interruption, timing, fade, load/start, and release behavior is unchanged.

### Windows cursor payloads (implemented 2026-07-21)

Cursor artwork is catalog-backed, not embedded in AGE.EXE. The eight ADV edge cursors at raw ids
`0x3318..0x331f` are 326-byte, uncompressed 1-bpp 32x32 Windows CUR payloads. FIELD's drag-pan callback
instead loads raw id `0x32ce`, catalog entry `CURSOR09.CUR` at `DATA1.ALF:611394962+766`. Its CUR directory
and DIB header specify one 32x32 image, hotspot `(16,14)`, 4 bits per pixel, no compression, and a default
16-entry BGRA palette. The 744-byte image consists of the 40-byte BITMAPINFOHEADER, 64-byte palette,
512-byte color/XOR bitmap, and 128-byte 1-bpp AND mask. The original runtime decoder accepted only 1-bpp
XOR data and therefore rejected this valid color cursor. `CurDecoder` now accepts the installed
uncompressed 1-bpp and 4-bpp variants, derives each palette size from the DIB metadata, calculates separate
DWORD-aligned XOR and AND strides, expands packed high-nibble-first 4-bpp indices through the BGRA palette,
and retains the separate 1-bpp AND transparency mask. The installed-asset regression checks CURSOR09's
dimensions, hotspot, transparent background, grayscale, and a non-gray palette pixel.

### SC0000 movie payload and presentation (2026-07-11)

The first implementation was validated at SC0000 `0x236@0x13c8`. Universal base-catalog id `0x33` resolves through the
authoritative catalog to `DATA1.ALF:CHAPTER.AGF` (archive offset 3,908,816; size 8,194,052). Despite the
`.AGF` name, its payload begins with MPEG program-stream pack start code `00 00 01 BA`; the installed asset
is MPEG-1 program stream video at 800x600, 29.97 fps, approximately 11.98 seconds, with video stream `E0`
and audio stream `C0`.

`ResourceMap.ReadMovie` accepts the resolved `AssetEntry`, reads it through the injected `IAssetStore`, and
rejects a non-MPEG-pack payload. Godot has no built-in MPEG decoder suitable for this asset, so the
Windows backend adapts those already-owned VFS bytes to the system DirectShow MPEG source using a private
temporary file. DirectShow decodes RGB32 on an MTA thread; the sample-grabber callback converts bottom-up
BGRA to top-down RGBA and publishes only the newest frame to the retained compositor surface. The temporary
file is an implementation adapter, never an alternate resolver or dependency on `extracted/`. The audio pin
is intentionally left unrendered for this bounded slice.

Archive-only tests verify the exact catalog entry, payload size/header, and an actual decoded 800x600 RGBA
frame; both still pass with the entire `extracted/` tree physically moved aside and restored afterward.
SC0000's native capture verifies operands `(0x33, 0, 2, 0)` and immediate VM continuation at `0x13d1`.
In Godot the real site opens the same 8,194,052 VFS bytes, publishes changing 800x600 frames, and retains
them across pre-yield static surface preparation. The later `0x21c` presentation service remains parked
until DirectShow EOF, then scene cleanup stops the movie. The initial invisible result was compositor-only:
the static `(assetId,colorKey)` image cache froze the first movie sample, while an extra current-sample
background copy was covered by the correctly positioned retained movie object. Dynamic movie surfaces now
bypass that cache and publish only at their retained z-position. A windowed run reached first frame 101 and
stop frame 190, and manual observation confirmed visible changing video. Movie audio remains intentionally
unrendered. The real-scene trace remains a separate extracted-present test
because the current `Paths.Scripts()` test bootstrap still locates its root `*.BIN` fixtures there; migrating
that test/bootstrap path is unrelated to movie asset loading and was not folded into this slice.

### BTL combat-effect movie resolution and decoder boundary (diagnosed 2026-07-21)

BTL's movie call at `BTL.BIN@0x2b21` is the same non-modal opcode `0x236` and, like every native resource
consumer, its table supplies universal packed SYS4INI ids. The first accepted combat run reached
five such ids:

| Raw id | Catalog asset | MPEG size | Sequence size |
|---:|---|---:|---:|
| `0x2bd8` | `MVB958.AGF` | 40,964 bytes | 280x352 |
| `0x2af1` | `MVB001.AGF` | 90,116 bytes | 280x352 |
| `0x2bde` | `MVB955.AGF` | 22,532 bytes | 280x352 |
| `0x2af5` | `MVB004.AGF` | 69,636 bytes | 280x352 |
| `0x2bca` | `MVB914.AGF` | 133,124 bytes | 400x400 |

All five archive payloads begin with MPEG program-stream pack code `00 00 01 BA`; none is a corrupt still
AGF. Universal packed resolution was implemented across texture, voice, and both movie paths on 2026-07-21.
The following manual combat run produced no `movie unresolved` warnings: the BTL ids reached
`ResourceMap.ResolveMovie`, VFS reads, and the movie backend as native requires.

Resolution is not the only blocker. An archive-backed probe of the actual Windows backend found that the
current DirectShow graph decodes `MVB914` and exposes a positive stop time, but rejects all four 280x352
assets while connecting the MPEG video decoder with HRESULT `0x80040217`. Wider probes establish the
boundary in the current filter stack: 208/288/304/400/800-pixel widths decode, while 280/360/520/600-pixel
widths fail; the latter are all 8 mod 16. This is not a rare content edge: 125 installed `MVB*.AGF` files
are 280x352. Requesting RGB24 instead of RGB32 does not change negotiation. The evidence localizes the
failure to DirectShow/filter compatibility with these non-16-aligned MPEG display widths; it does not prove
which internal stock filter imposes the restriction.

The resolver-only acceptance run makes that split visible in one exchange. `MVB961` (`0x2be3`, 112,644
bytes) and `MVB238` (`0x2b94`, 143,364 bytes) both declare 280x352 and fail at
`IGraphBuilder.Connect(sourceOut, sampleGrabberIn)` with `0x80040217`. In the same run `MVB908`
(`0x2bc2`, 126,980 bytes) declares 400x400, starts with a 333 ms stop time, publishes RGBA frames, reaches
completion, and is stopped by script cleanup. This is decisive backend evidence rather than a resolver,
VFS, signature, or corrupt-asset problem.

The user nevertheless observed the combat presentation stall after that sequence. Static BTL tracing rules
out `0x23f == -1` as an infinite-loop mechanism by itself. `local 0x44`, the total effect horizon, is first
set to `base_time + 1000` at `0x2230`; each effect can only extend that maximum with
`effect_start + duration` at `0x2b64..0x2b8b`. A failed duration of -1 can understate that one extension but
cannot make the callback count negative. After callbacks, `0x2492..0x2515` independently scans movie
surfaces 7..10 through `0x23a`, sleeping 16 ms only while one reports active. The successful
`movie stopped MVB908` line is emitted by the subsequent `0x2518` cleanup, proving that explicit movie wait
exited in the logged run. The final stall therefore was not localized; it may be after movie cleanup.

The port now nevertheless makes decoder failure safe and deterministic. A valid `0x236` movie whose host
backend cannot initialize is marked completed explicitly and receives stop time 0, meaning an immediate
effect, rather than the former diagnostic -1. Empty movie slots retain native `0x23f == -1`. Successfully
started decoders also carry a completion watchdog: the larger of five seconds or reported stop time plus
two seconds (capped at five minutes), with a 30-second default when timing is unavailable. Expiry forces the
same completed state so a backend that starts but never signals EOF cannot hold `0x21c` indefinitely. If the
reported combat stall survives this guard, capture the VM/service coordinate after `0x2518` and treat it as
a separate BTL timed-presentation bug.

The FFmpeg replacement and destination-surface work are recorded in `docs/platform-portability.md` and
`docs/phase-b-framework.md`. Movie identity also remains typed across the entire retained-surface lifecycle.
The packed catalog is immutable while mounted, so once an id enters the movie path its `.AGF`-named MPEG
payload cannot fall through to the ACGF still decoder before the first frame or after the last live binding
is detached. The latter guard closes a real cross-thread cleanup window: BTL clears `GfxState` and releases
the host surface in one VM instruction, while the compositor may hold a snapshot from immediately before
that pair. Without remembered movie typing, the snapshot briefly attempted to decode MVB955/MVB004/MVB914
as still images after successful playback and printed false `expected an ACGF image` warnings. This does not
retain a frame or playback instance; it only prevents an immutable movie resource from being reclassified.

### Modal startup/ending movie resources (implemented 2026-07-20)

Opcode `0x20f` uses the same universal packed resource contract as `0x236`. Its complete
corpus is `LOGO.BIN (0x335f,42,4)`, `OP.BIN (0x3364,42,4)`, and
`ED.BIN (0x3324,42,dynamic_flags)`. Base records `0x335f`, `0x3364`, and `0x3324` are respectively
`LOGO.AGF`, `OP.AGF`, and `ED.AGF`; all begin with MPEG pack code `00 00 01 BA`.
Native `0x20f` also arms modal run-state `0x2000`; unlike `0x236`, these six-instruction wrapper scripts
depend on the movie service itself to park until EOF/input cancellation before they release surface 42.

`ResourceMap.ResolveMovie` supplies that typed universal lookup, while `ReadMovie` remains the MPEG
signature gate. `IHost.PlayModalMovieToSurface` is distinct from the non-modal call for lifecycle only: Godot
reuses the asynchronous DirectShow frame decoder and retained compositor but parks the VM thread until EOF
or mouse/Accept/Cancel input. The wrapper's following release then tears down the completed/cancelled movie.
The decoder still intentionally leaves audio unrendered; OP/ED audio parity needs an explicit synchronized
movie-audio/backend contract rather than an unmanaged default-device side path.

VFS-B passes its bounded gates in `Sys4AssetStoreTests`: the installed AAI expands from the LZSS stream at
`0x118` (expanded size at `0x110`, packed size at `0x114`) to one `APPEND01.ALF` archive and 81 80-byte
records. All records carry selector 1 and literal `$1$` names. The full directory has stable SHA-256
`23F0C104A45C099CEFB7D333362716EDE6F20B9EC53E4C3705A8E3A87063708E` over its ordered record fields.
The integration gate runs `BinExtractALF.exe` into a disposable directory, compares all 81 names, validates
every range and size, and byte-compares all 81 payloads. `Sys4ScriptProvider` resolves a real append script
through `0x01xxxxxx`; direct base-name lookup deliberately does not see append records.

### Deliberate non-goals

- Writing/repacking ALF or AAI; loose overrides already provide the native mod/translation workflow.
- AGF encoding or movie audio. A decoder interface/software MPEG fallback is now required for the installed
  non-16-aligned MVB effects; synchronized audio remains a separate slice.
- A generalized multi-mod dependency manager. Start with native game-root loose overrides; configurable
  ordered mod roots can be layered onto the same store later.
- Removing the extraction/conversion tools immediately. They remain independent parity oracles until the
  runtime readers have broad corpus coverage.

Primary implementation reference: [GARbro's Eushully AGF reader](https://github.com/morkt/GARbro/blob/master/ArcFormats/Eushully/ImageAGF.cs)
and [ALF reader](https://github.com/morkt/GARbro/blob/master/ArcFormats/Eushully/ArcALF.cs), MIT licensed.
Secondary validation reference: [Kelebek's extractor](https://github.com/Kelebek1/Eushully-Decompiler/blob/master/extract_alf.py).
