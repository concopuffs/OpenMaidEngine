<!-- DO NOT EDIT -- generated from vm-map/opcodes.toml by tools/opcodes_build.py --build -->
# Opcode Reference (generated)

248 opcodes used by Himegari. Source of truth: `vm-map/opcodes.toml`.

## adv

### 0x7a `text-param?` (u0041AD70, argc 3)
- **summary:** 3 args (imm/computed/imm); sub computes a value then 0x7a then show-text — text speed/wait/window param
- **grounding:** source=inference, confidence=med
- **evidence:** confirm via frida

## audio

### 0xb6 `snd-ctrl?` (u0041D080, argc 1)
- **summary:** 1 imm; self-chains, 0x41D family near play-sound-effect/0xb5 — sound channel/volume/stop control
- **grounding:** source=inference, confidence=low
- **evidence:** confirm via frida

### 0xbf `play-bgm` (play-bgm, argc 1)
- **summary:** Play background music by id. BGM is addressed by DIRECT LITERAL NAME: id -> BGM{id:03d}.OGG (in DATA3), NOT the per-scene section manifest (that's voices/textures). E.g. play-bgm 5 -> BGM005.
- **grounding:** source=investigation, confidence=high
- **evidence:** By-ear confirmed (2026-07-06): SC0000 real game plays BGM005 for play-bgm 0x5 and BGM008 for play-bgm 0x8 (we initially mis-played BGM006/BGM009 via the manifest = off-by-one). Direct-name proven by play-bgm 0x23 -> BGM035.OGG, a real standalone track (BGM set skips 030-034) that the manifest mis-resolved to a graphics entry (EV049AA.AGF). CORRECTS the earlier 'unified manifest / Frida BGM006' claim, which was wrong by one. Voices/textures still use the manifest (files[base+id], offset 0). Diagnostic: `Age.Cli audio SC0000.BIN`.

### 0xc4 `play-voice` (play-voice, argc 1)
- **summary:** Play a voice clip by id; id resolves via the SYS4INI section manifest -> files[section_base(scene)+id] (voice OGG in DATA1/DATA4). Same rule as set-texture (NOT play-bgm, which is direct-name BGM{id:03d}).
- **grounding:** source=investigation, confidence=high
- **evidence:** By-ear confirmed (2026-07-06): SC0000 prologue voices play on their lines via Godot AudioStreamPlayer. Off-by-one disproven structurally: manifest interleaves graphics/voice (files[35]=EV049AA, [36]=MAN999, [37]=EV052CA, [38]=SYL0001), so files[base+id] lands voices on OGGs while files[base+id-1] would land them on .AGF graphics (silent) -- and they play, so the offset is exactly 0. Lily's lines are correctly form-gated (G[0xa57/0xa58/0xa59]) and stay silent when no form flag is seeded -- not a bug.

## control

### 0x3 `call-script` (call-script, argc 1)
- **summary:** load & call another SYS4 script by id; id = RAW index into the SYS4INI file table (asset-index). Pushes a script frame; returns to caller when the callee ends.
- **grounding:** source=investigation, confidence=high
- **evidence:** native-RE (Ghidra): handler FUN_0041bc90 -> loader FUN_0040e980 -> resolver FUN_0044f390 indexes an 80-byte record table (base [ctx+0x414], count [ctx+0x40c]) at base+id*0x50 = the SYS4INI record layout {name[64],arc_id@0x40,file_number@0x44,offset@0x48,size@0x4c}. Confirmed statically: all 297 distinct corpus call-script ids resolve to a .BIN script with a semantically-exact name (0x1ab->ADDITEM, 0x2ae7->MES, 0x143->BUNKI, 0x329d->CALCREVISE), 0 out-of-range, 0 pack-branch. See docs/engine-re.md + name-resolution.md #1.

op 0x03 (call-script, argc 1): `call-script <id>`. RESOLVED — the id is a direct RAW index into
the SYS4INI global file table (the same table parse_sys4ini.py reads, but indexed WITHOUT skipping
'@' placeholders; SYS4INI has 13208 records / 2 placeholders). No separate on-disk id->code registry
exists; SYS4INI *is* the call-script registry.
Native mechanism (dispatch table `handler(op)=ctx[0x26c93+op]`, op 0x03 -> FUN_0041bc90):
  1. FUN_0041bc90 fetches operand 1 (id), bounds-checks call depth (<=0x26), pushes a frame.
  2. FUN_0040e980 (loader): opens the resource by id, reads the 0x20-byte SYS4 header, checks magic,
     allocates per-frame code/local buffers from the header var-counts, reads the bytecode body,
     pushes a script frame (stride 0x1e = 30 dwords, indexed by ctx[0x14f45]).
  3. FUN_0044f390 (resolver): record = [ctx+0x414] + id*0x50. Tries a LOOSE OVERRIDE first
     (CreateFileA on record.name -> mod/patch hook point), else opens archive [record.arc_id*0x100 +
     ctx+0x410], SetFilePointer to record.offset, size = record.size.
     (High-byte-tagged ids `id & 0xff000000` select an alternate pack via [ctx+0x3028]; UNUSED by the
     corpus -- 0/297 ids have a high byte.)
Companion op 0x8f `call` is INTRA-script (a local JSR), not cross-script -- see its entry.
This also names the whole call graph statically (build/callscript-names.json).


### 0x8f `call` (call, argc 1)
- **summary:** intra-script subroutine call (local JSR): PC = frame.codebase + operand*4; pushes a return address on the per-frame return stack. NOT cross-script (that is call-script 0x03).
- **grounding:** source=investigation, confidence=high
- **evidence:** native-RE (Ghidra): handler FUN_0041fba0 (= ctx[0x26c93+0x8f]) sets [frame PC @+0x53d2c] = [frame codebase @+0x53d28] + operand*4 and pushes ((pc-base)>>2)+3 onto the per-frame return stack ([ctx+0x552e8]/[ctx+0x55248]). Target is a code OFFSET within the current script (matches header table T3 tag 0x8F = local call targets), confirming it is a local JSR, not a script load.

## draw

### 0x1a2 `gfx-cmd-register` (u00428010, argc 1)
- **summary:** graphics command-buffer op: sets current gfx-object cmd-type=3 and registers a '%c%8.8x' key from operand 1
- **grounding:** source=investigation, confidence=med
- **evidence:** Ghidra: real handler FUN_0042d360 (via dispatch table ctx[0x26c93+op]); sets *(ctx+0x53d88+ctx[0x53d14]*0x78)=3, sprintf("%c%8.8x",3,op1), FUN_0042cf70. NOT save/scene (raw Kelebek VA 0x428010 drifted to op 0x1ac save handler). See docs/engine-re.md

### 0x1f7 `ui-elem?` (u00420270, argc 2)
- **summary:** 2 args; 0x420 family, pairs with 0x1fa — create/begin a UI element
- **grounding:** source=inference, confidence=med
- **evidence:** confirm via frida

### 0x1f8 `create-texture` (create-texture, argc 4)
- **summary:** Allocate/prepare a texture slot: (slot, width, height, flag). e.g. `create-texture 0xd 0x190 0x1e 0x0` = slot 13, 400x30.
- **grounding:** source=investigation, confidence=med
- **evidence:** SC0000 CG/UI-draw path disasm; slot/w/h roles read off the operands (400x30 text bars, etc.).

### 0x1f9 `set-texture` (set-texture, argc 3)
- **summary:** Load asset #resId into texture slot: (resId, slot, flag=-1). resId resolves via the SYS4INI per-scene section manifest: files[section_base(scene)+resId] (same rule for play-bgm/play-voice). See docs/asset-resolution-re.md.
- **grounding:** source=frida, confidence=high
- **evidence:** SC0000 Frida-confirmed 17/17 (0x25->EV052CA, 0x2e->EV052DB, 0x36->BG030A background); resolution rule validated on 586/595 captured loads. Traced in CG-load subroutine label_12649 as `set-texture G[0x62424] <slot> -1`.

### 0x1fa `ui-clear?` (u00420480, argc 1)
- **summary:** 1 arg (element id); follows 0x1f7 — show/hide/clear UI element by id
- **grounding:** source=inference, confidence=med
- **evidence:** confirm via frida

### 0x1fb `draw-texture` (draw-texture, argc 8)
- **summary:** Blit a texture slot to screen. Observed 8 args: (handle, slot, srcx, srcy, w, h, dstx, dsty). e.g. `draw-texture 0xcf08 0x3 0 0 0x320 0x258 0 0` = full-screen (800x600) slot 3 at (0,0).
- **grounding:** source=investigation, confidence=med
- **evidence:** SC0000 CG-load subroutine label_12649: `draw-texture (ptr) (slot) 0 0 (w) (h) (dstx) (dsty)`; full-screen slot-3 draws use 0x320x0x258 (800x600).

### 0x1ff `draw?` (u00420770, argc 4)
- **summary:** 4 args (global+imms); follows 0x217, then call
- **grounding:** source=inference, confidence=low
- **evidence:** confirm via frida

### 0x202 `draw-blit?` (u00420880, argc 5)
- **summary:** 5 args (coords/sizes); preceded by coord arithmetic, near draw ops
- **grounding:** source=inference, confidence=med
- **evidence:** confirm via frida

### 0x203 `draw?` (u00420950, argc 4)
- **summary:** 4 args; chains with 0x202/draw-texture
- **grounding:** source=inference, confidence=med
- **evidence:** confirm via frida

### 0x208 `get-texture-size` (get-texture-size, argc 3)
- **summary:** 0x208 (slot)(out_w)(out_h) — writes the loaded texture's width/height into two output globals; keystone for bytecode-computed sprite/bg geometry (SC0000 label_12649)
- **grounding:** source=inference, confidence=med
- **evidence:** SC0000 label_12649: set-texture(resId,slot) then 0x208(slot)->w,h feeds w/2 horizontal-center + foot-anchor subtraction into draw-texture dst; stubbing yields 0x0 sizes / off-center draws

### 0x215 `query-gfx-object?` (u00421160, argc 2)
- **summary:** 0x215 (out)(handle_id) — queries the native graphics-object manager by element handle-id (the value in 0x62455[idx], often +1/+2 for a sub-element); writes the object's slot/status into `out`, sign-tested (gre/lt 0) to drive label_12649's slot-select branch and set the working slot G[0x62452]. KEYSTONE for per-object slot selection — stubbing it collapses every draw onto slot 0, so the anchor-preserve geometry reads foreign-sized textures → cumulative bg/sprite drift (see docs/phase-a-slice-plan.md A2b-Geometry). Reads native object-manager state (NOT VM-computable). Exact return semantics: RE via unicorn (native handler @0x421160).
- **grounding:** source=investigation, confidence=med
- **evidence:** SC0000 label_12649 (0x12670) + label_123ef (0x12419/0x12450): called with 0x62455[idx] handle-ids (±offset); result gre/lt 0 branches slot-select and feeds ui-elem?(0x1f7)/set-texture slot. Record table 0x3239 (label_125bd @0x0050f) assigns per-object slots 4..13. Handles are the 0xcf08/0xe678/0xd6d8 element-id family.

### 0x217 `gfx-geom?` (u004211E0, argc 4)
- **summary:** 4 global-ints; part of a 0x217/0x218/0x21a geometry chain
- **grounding:** source=inference, confidence=low
- **evidence:** confirm via frida

### 0x218 `gfx-geom?` (u00421270, argc 4)
- **summary:** 4 global-ints; chains with 0x21a/0x217
- **grounding:** source=inference, confidence=low
- **evidence:** confirm via frida

### 0x21a `gfx-geom?` (u00421370, argc 4)
- **summary:** 4 global-ints; chains with 0x218/0x217
- **grounding:** source=inference, confidence=low
- **evidence:** confirm via frida

## input

### 0x90 `hotspot-branch` (u0041BEB0, argc 7)
- **summary:** cursor/input hotspot hit-test: rect (x,y,w,h) -> 3-way branch on interaction, else fall through to pc+1
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **depends on:** 0x1f4, 0x1f5
- **depended on by:** 0x97
- **evidence:** all 301 ADV scripts contain the identical 8 sites; enc.len 15 lands pc+1 on the fall-through stmt (design-confirmed); fall-through = correct headless no-input path, proven by 279 CLEAN dialogue scenes

op 0x90 (u0041BEB0, argc 7): `0x90 x y w h tgt_a tgt_b tgt_c`. Kelebek left it "ukn" noting args
5-7 are code locations. Corpus analysis (all 301 ADV scripts) resolves it:
- Two forms, both ONLY in one shared ADV-chrome subroutine copied into every ADV script:
  * Mode A (1505 = 5x301): immediate x,y,w,h with w=h=20; the five on-screen buttons at
    (684|706|728|750|772, 572), each setting one of G[0x6c9..0x6cd] to 1 / 0 / 0+run-action
    (reads as hover-enter / hover-leave / click). All 3 targets real.
  * Mode B (903 = 3x301): local-int operands, w=h=1, only tgt_c real -- a keyed 2-way input test.
- Every one of the 301 scripts has EXACTLY 8 sites (5 A + 3 B); zero scene-specific use.
- Falls through (pc+1) when nothing matches -- design-confirmed (0xd0 + 15 dwords = 0xdf = label_df).
- Headless (no cursor/input) => fall through => vm0 stub already correct; the 12 EMPTY sweep scenes
  are gated by state + this input-wait chrome, NOT by unmodelled 0x90. Model live in A2.


### 0x97 `hotspot-reg?` (u0041C150, argc 5)
- **summary:** companion register-hotspot / set-widget-action (argc5: v1 v2 1 1 <action-id>; NO code targets)
- **grounding:** source=inference, confidence=med, noop_headless=True
- **depends on:** 0x90
- **evidence:** interleaves with 0x90 in the shared ADV-chrome subroutine; trailing imm = action id 0x0/0x7/0x8; same widget cluster as 0x90/0x91/0x92/0x95; confirm via frida

## marker

### 0x1bc `block-mark` (u00415670, argc 0)
- **summary:** zero-arg; follows jcc/mov, precedes mov/ret — block boundary
- **grounding:** source=inference, confidence=high, noop_headless=True

### 0x1bf `call-end` (u004156C0, argc 0)
- **summary:** zero-arg; call->0x1bf->stmt-end — end-of-call-statement marker
- **grounding:** source=inference, confidence=med, noop_headless=True

### 0x1d2 `stmt-desc?` (u0041BB40, argc 2)
- **summary:** 2 imm; immediately after stmt-begin 0x1f4 — statement descriptor?
- **grounding:** source=harness, confidence=med, noop_headless=True

### 0x1d5 `cond-block` (u00415700, argc 0)
- **summary:** zero-arg; ALWAYS follows jcc — marks conditional body entry
- **grounding:** source=inference, confidence=high, noop_headless=True

### 0x1f4 `stmt-begin` (u004160D0, argc 0)
- **summary:** zero-arg; opens scripts, pairs with stmt-end 0x1f5
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **depended on by:** 0x90

### 0x1f5 `stmt-end` (u00416120, argc 0)
- **summary:** zero-arg; precedes exit/next-stmt, pairs with 0x1f4
- **grounding:** source=investigation, confidence=high, noop_headless=True
- **depended on by:** 0x90

### 0x21b `line-id?` (u004213E0, argc 1)
- **summary:** 1 imm; mov->0x21b->stmt-end; near save/load-messkip — likely line/stmt id, verify not msg-control
- **grounding:** source=harness, confidence=med, noop_headless=True

### 0x258 `decl?` (u00422FE0, argc 2)
- **summary:** 2 imm; runs in a chain right after script-entry 0x259, enumerating ids — prologue declaration/registration?
- **grounding:** source=harness, confidence=low, noop_headless=True

## structural

### 0x71 `label-def` (u0041A7B0, argc 1)
- **summary:** 1 imm; count == T1 table size -> the label/anchor T1 indexes. v1 no-op; revisit if menu/callback dispatch looks up by id
- **grounding:** source=investigation, confidence=high, noop_headless=True

## unknown

### 0x1 `u004149C0` (u004149C0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2 `exit` (exit, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5 `ret` (ret, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x6 `u00417E80` (u00417E80, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x8 `u00417FC0` (u00417FC0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x9 `exit-script` (exit-script, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x21 `u00418860` (u00418860, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x22 `u00418920` (u00418920, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x25 `u00418B40` (u00418B40, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x50 `add` (add, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x51 `sub` (sub, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x52 `mul` (mul, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x53 `div` (div, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x54 `mod` (mod, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x55 `mov` (mov, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x56 `and` (and, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x57 `or` (or, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x58 `sar` (sar, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x59 `shl` (shl, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5a `eq` (eq, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5b `ne` (ne, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5c `lt` (lt, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5d `lte` (lte, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5e `gr` (gr, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x5f `gre` (gre, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x60 `u0041A270` (u0041A270, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x61 `lookup-array` (lookup-array, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x63 `u00414A60` (u00414A60, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x64 `copy-local-array` (copy-local-array, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x6c `copy-to-global` (copy-to-global, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x6e `show-text` (show-text, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x6f `end-text-line` (end-text-line, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x70 `u0041A750` (u0041A750, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x72 `wait-for-input` (wait-for-input, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x73 `u0041AB30` (u0041AB30, argc 10)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x75 `u0041AC30` (u0041AC30, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x76 `u0041AC60` (u0041AC60, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x77 `u0041ACB0` (u0041ACB0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x78 `u0041AD00` (u0041AD00, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x79 `u0041AD30` (u0041AD30, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x7b `u0041ADB0` (u0041ADB0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x7c `u00416A90` (u00416A90, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x7f `u00414C60` (u00414C60, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x80 `u0041AF00` (u0041AF00, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x85 `u00414CF0` (u00414CF0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x86 `u0041B210` (u0041B210, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x87 `u00414D10` (u00414D10, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x88 `u0041B290` (u0041B290, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x8b `u0041B3D0` (u0041B3D0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x8c `jmp` (jmp, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x93 `u00415040` (u00415040, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x94 `u00415090` (u00415090, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xa0 `jcc` (jcc, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xa1 `u00427C00` (u00427C00, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xa2 `u00427FD0` (u00427FD0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xa3 `u004244D0` (u004244D0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xae `u00415130` (u00415130, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xb4 `play-sound-effect` (play-sound-effect, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xb5 `u0041D050` (u0041D050, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xb7 `u0041D0E0` (u0041D0E0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xb8 `u00415520` (u00415520, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xb9 `u0041D140` (u0041D140, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xba `u0041D0B0` (u0041D0B0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xc0 `u00415620` (u00415620, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xc2 `u0041D2B0` (u0041D2B0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xc5 `u0041D4A0` (u0041D4A0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xc6 `u0041D5D0` (u0041D5D0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xc7 `u0041D760` (u0041D760, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xc8 `sleep` (sleep, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xcc `mouse_callback` (mouse_callback, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xcd `get-input-type` (get-input-type, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xd0 `u00415830` (u00415830, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xd3 `u00425960` (u00425960, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xd4 `u004266F0` (u004266F0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xd5 `u004262C0` (u004262C0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xd9 `u00415880` (u00415880, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xfb `joy_callback` (joy_callback, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0xfe `u0041E360` (u0041E360, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0xff `u00415A10` (u00415A10, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x100 `u00415A60` (u00415A60, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x101 `u00415BF0` (u00415BF0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x107 `u0041E500` (u0041E500, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x108 `u00415E70` (u00415E70, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x109 `u00415EC0` (u00415EC0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x10a `u0041E540` (u0041E540, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x10b `u0041E5A0` (u0041E5A0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x10c `u0041E5E0` (u0041E5E0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x10d `u00415F10` (u00415F10, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x12c `lookup-array-2d` (lookup-array-2d, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x12e `u0041E940` (u0041E940, argc 8)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x12f `u0041ECB0` (u0041ECB0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x130 `u00415F40` (u00415F40, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x131 `u00415F70` (u00415F70, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x132 `u0041EF00` (u0041EF00, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x133 `u0041EFF0` (u0041EFF0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x134 `u0041F050` (u0041F050, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x135 `bit-set` (bit-set, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x136 `bit-reset` (bit-reset, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x137 `u0041F1C0` (u0041F1C0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x13a `u0041F3A0` (u0041F3A0, argc 6)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x13f `check-bit` (check-bit, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x140 `u0041F9C0` (u0041F9C0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x141 `u0041FAA0` (u0041FAA0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x142 `u0041FB10` (u0041FB10, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x143 `u00415FB0` (u00415FB0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x144 `u004259D0` (u004259D0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x149 `u0041FCE0` (u0041FCE0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x191 `u0041A4A0` (u0041A4A0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x192 `set-string` (set-string, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x193 `concat` (concat, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x194 `u00425480` (u00425480, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x195 `u00425580` (u00425580, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x196 `display-furigana` (display-furigana, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x197 `u0041B510` (u0041B510, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x198 `u0041B540` (u0041B540, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x199 `u00414D50` (u00414D50, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x19a `u00414E50` (u00414E50, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x19b `u00414E80` (u00414E80, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x19c `u00414EC0` (u00414EC0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x19d `u0041C680` (u0041C680, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x19e `u0041C6E0` (u0041C6E0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1a0 `u0041C9B0` (u0041C9B0, argc 9)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1a1 `u0041CB40` (u0041CB40, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1a3 `string-lookup-set` (string-lookup-set, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x1a4 `u0041B580` (u0041B580, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1a5 `set-font` (set-font, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x1a6 `halve-strlen` (halve-strlen, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x1a7 `comment` (comment, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x1a8 `dev_ukn` (dev_ukn, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1a9 `u00428090` (u00428090, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1aa `u00425920` (u00425920, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ab `u0041CCA0` (u0041CCA0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ac `u0041CD80` (u0041CD80, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ad `u004154F0` (u004154F0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ae `u0041CED0` (u0041CED0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1af `u004245C0` (u004245C0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b0 `u0041A510` (u0041A510, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b2 `u00425790` (u00425790, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b3 `u004257D0` (u004257D0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b4 `u004237C0` (u004237C0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b5 `u0041B5F0` (u0041B5F0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b6 `u00414F60` (u00414F60, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b7 `u0041B640` (u0041B640, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b8 `u0041B670` (u0041B670, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1b9 `u0041B710` (u0041B710, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ba `u0041D850` (u0041D850, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1bb `u0041B7B0` (u0041B7B0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1bd `u0041D910` (u0041D910, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1c1 `u0041B820` (u0041B820, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1c7 `u00414F90` (u00414F90, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1c8 `toString` (toString, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x1ca `u0041B9B0` (u0041B9B0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1cb `u00414FD0` (u00414FD0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1cc `u00415010` (u00415010, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1ce `u0041B9F0` (u0041B9F0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1cf `u0041DA10` (u0041DA10, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1d0 `u0041BA80` (u0041BA80, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1d1 `u0041BAE0` (u0041BAE0, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1d3 `u0041BB90` (u0041BB90, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1d4 `u0041BC00` (u0041BC00, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1f6 `u00416170` (u00416170, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1fd `u00420620` (u00420620, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x1fe `u004206C0` (u004206C0, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x204 `draw-string` (draw-string, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x205 `u00420A60` (u00420A60, argc 6)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x207 `u00420B00` (u00420B00, argc 8)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20a `u00420CE0` (u00420CE0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20b `u00420D50` (u00420D50, argc 7)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20c `u00416200` (u00416200, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20d `u00420E10` (u00420E10, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20e `u00416250` (u00416250, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x20f `u00420E40` (u00420E40, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x212 `u00421090` (u00421090, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x213 `u004210D0` (u004210D0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x216 `u004211A0` (u004211A0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x219 `u004212E0` (u004212E0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x21c `u00416270` (u00416270, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x21d `u00421410` (u00421410, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x21e `u00421450` (u00421450, argc 6)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x21f `u00421510` (u00421510, argc 7)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x220 `u004215D0` (u004215D0, argc 6)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x222 `u004216C0` (u004216C0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x223 `u00421700` (u00421700, argc 8)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x224 `u00416290` (u00416290, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x228 `u00421940` (u00421940, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x229 `u004219E0` (u004219E0, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x22a `u00421A90` (u00421A90, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x22c `u00421BD0` (u00421BD0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x22d `u00421C60` (u00421C60, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x22f `u00421DD0` (u00421DD0, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x230 `u00421E70` (u00421E70, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x231 `u00421EA0` (u00421EA0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x232 `u00421EF0` (u00421EF0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x233 `u00421FB0` (u00421FB0, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x234 `u00422060` (u00422060, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x236 `u004221A0` (u004221A0, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x238 `u00422390` (u00422390, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x239 `u004223C0` (u004223C0, argc 6)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x23a `u00422420` (u00422420, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x23b `u00422460` (u00422460, argc 7)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x23c `u004162B0` (u004162B0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x23d `u004162F0` (u004162F0, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x23f `u00422930` (u00422930, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x241 `u00422B80` (u00422B80, argc 5)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x242 `u00422D60` (u00422D60, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x243 `u00417070` (u00417070, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x248 `u00422E80` (u00422E80, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x249 `u00422EB0` (u00422EB0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x24d `u00422E90` (u00422E90, argc 12)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x24e `u00422EA0` (u00422EA0, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x259 `u00416410` (u00416410, argc 0)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2bd `u00423100` (u00423100, argc 1)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2bf `u00423180` (u00423180, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2c0 `u004231C0` (u004231C0, argc 3)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2c5 `strlen` (strlen, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=med

### 0x2c6 `u0042B5E0` (u0042B5E0, argc 2)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

### 0x2c8 `u0042B610` (u0042B610, argc 4)
- **summary:** —
- **grounding:** source=kelebek, confidence=low

