# AGE Remake — Architecture & Roadmap (option 3: remake / enhance / mod)

**Decided goal (2026-07-06):** not a translation patch (a translated build is already playable),
not a bare cross-platform port — but an **open reimplementation of the AGE engine that runs the
original games and makes modding a first-class feature.** Himegari (SYS4) is the first target;
the design must extend to other AGE games and engine versions (SYS3/SYS5).

The right mental model is **ScummVM / OpenMW for Eushully's AGE engine**: we ship an *engine*;
the user supplies the *original game data* they own; mods layer on top. Those projects prove this
shape is feasible — and that it's a large, long-lived effort. Our decoding work (done) is the
foundation; the runtime + backends + mod system is the bulk of the remaining work.

---

## 1. Guiding principles

1. **Bytecode-faithful logic, pragmatic presentation.** Run the original `.BIN` scripts on a
   reimplemented VM — you get every gameplay rule (damage, dungeon, battle, recovery) correct by
   construction, without re-deriving them. Reimplement the *effectful* ops (draw/text/audio/input)
   against a clean modern backend that looks right, not byte-identical to D3D9.
2. **One engine, many profiles.** Do NOT fork per game or per version. A shared VM core, with
   *version front-ends* (parser/codec/opcode table) and *per-game profiles* (maps, data schemas,
   asset rules) selected by a manifest.
3. **Modding is architecture, not an afterthought.** The data model, content loading, and script
   dispatch are designed so mods can override assets, edit data, patch scripts, and inject
   host-language hooks. The engine already hints at this: 49 loose root script `.BIN` files natively
   shadow their archived copies — a built-in override mechanism VFS-A generalizes.
4. **The original owns the content; we own the engine.** Users provide their AGE install; the
   runtime imports/loads it. This keeps us on the right side of distribution and mirrors ScummVM.
5. **De-risk with vertical slices.** Prove "run one scene end-to-end" before breadth. Nothing is
   real until a script executes and renders.

---

## 2. Target architecture — the split

Three layers, cleanly separated:

```
┌──────────────────────────────────────────────────────────────────────┐
│ RUNTIME  (Godot + C#)  — ships to players & modders                    │
│  ├─ AGE VM core (C#)      fetch/execute, typed var banks, control flow  │
│  ├─ Version front-ends    SYS3 / SYS4 / SYS5: header, string codec,     │
│  │                        opcode table, archive format                  │
│  ├─ Backend adapters      render(Godot 2D) · text/msg · audio · input   │
│  │                        — the effectful opcodes call into these       │
│  ├─ Content loader        ALF archives + loose overrides + mod folders  │
│  ├─ Data layer            game data from moddable files (bootstrapped    │
│  │                        from *INIT extraction)                         │
│  └─ Mod system            override resolution · hook API · patch loader  │
├──────────────────────────────────────────────────────────────────────┤
│ PROFILE / MANIFEST  (per game)  — data, not code                        │
│   engine_version, archives, string_codec, opcode_table_ref,             │
│   global_var_map, callscript_map, data_schemas, boot_entry,             │
│   asset_conversion rules                                                │
├──────────────────────────────────────────────────────────────────────┤
│ TOOLCHAIN  (Python — what we've already built)  — offline, for modders  │
│   disassembler · assembler · extractors · global-map builder ·          │
│   data exporters · mod packaging                                        │
└──────────────────────────────────────────────────────────────────────┘
```

- **Runtime language.** VM core in **C#** (Godot's C# support) — a 1.5M-instruction fetch/execute
  loop is too hot for GDScript. Presentation, UI, mod tooling, and export targets use Godot. This
  is why **Godot now fits**: under the earlier "faithful port" framing it was overkill (you'd use
  ~10% of it); under *remake/enhance/mod* its editor, UI toolkit, asset pipeline, GDScript modding,
  and multi-platform export all earn their keep. Current OS dependencies and the gates for future exports
  are tracked in `docs/platform-portability.md`; they do not expand the active Phase A scope.
- **Toolchain vs runtime.** The runtime owns the canonical parser+VM (C#). The Python tools remain
  the offline analysis/authoring chain; they were the reference implementation and stay useful for
  modders. They share the *format spec* (documented), not code — acceptable for a small, stable
  container format.
- **Native content foundation status (2026-07-11).** VFS-A runtime-parses the base SYS4 catalog and
  applies loose-first bounded ALF reads; VFS-B mounts selector-keyed S4AC append catalogs and resolves the
  native high-byte/low-24-bit id split; VFS-C decodes AGF directly to platform-neutral RGBA8 and feeds Godot
  without pre-extracted or pre-converted texture files. BGM, voice, and SFX now open catalog entries through
  the same store and decode OGG/WAV from bytes in Godot, so runtime scripts, textures, and audio no longer
  depend on `extracted/`.
- **Profile = manifest.** Adding a game = a new profile + its maps. Adding an engine version = a new
  front-end plugin + profiles that reference it. See §5.

---

## 3. How modding works with a bytecode VM

Modding is tiered from trivial to deep. The first two tiers cover the large majority of "proper
modding" and need **no decompilation**.

**Tier 1 — assets & data (easy, no tools needed beyond a text/image editor).**
- *Asset overrides:* drop replacement textures/CGs/voices/BGM into a mod folder; the content loader
  resolves mod → loose-override → archive (generalizing the engine's native override behavior).
- *Data edits:* game data (skills/items/units/maps/stages) is **externalized to editable files**
  (JSON) that the runtime loads, bootstrapped from our `*INIT` extraction. Rebalancing, new items,
  new skills = editing JSON. No bytecode involved.

**Tier 2 — logic (medium; asm-level or host-language).**
- *Script patches:* disassemble → edit the `.age-asm` → reassemble to `.BIN` (Kelebek's project has
  a reassembler to adapt). Mods ship patched/replacement scripts; the VM runs them unmodified.
- *Host hooks:* a mod API lets mods register callbacks in **GDScript/C#** — fire before/after a
  script, intercept an opcode, replace a script by id, react to events, add UI. Original scripts run
  as-is; mods augment. (This is the BepInEx/script-extender model and avoids a bytecode compiler for
  most behavioral mods.)

**Tier 3 — a friendly modding language (stretch, later).**
- A high-level decompiled DSL + a compiler back to bytecode, so mods are written in readable source.
  This is a real compiler project and its quality is bounded by how complete the global-var map and
  call-script resolution are. Realistic as a *later* milestone, not near-term.

**Readability, concretely:** annotated disassembly is achievable today (opcode names + global-var
aliases + eventually call-script names). Pseudo-decompilation for *reading* is feasible (demonstrated
on RECOVER). Clean round-trippable *source* is Tier 3. So near-term "how readable" = well-annotated
assembly + external data/assets + host hooks; the read-like-C dream is a stretch goal.

**Two enablers become load-bearing under this goal** (they were "polish" for a port):
- **Global-var map** — modders must know what game state a global is to touch it safely.
- **Call-script resolution** — needed both to *run* scripts and to *add/replace* scenes. **✅ SOLVED
  (2026-07-07):** `call-script <id>` = a raw index into the SYS4INI file table (native-RE via Ghidra;
  `docs/engine-re.md`, `name-resolution.md §1`), and the C# VM now **executes** it (loads the target
  `.BIN` as a nested subroutine frame). Resolution + execution both done; see the status memory.

---

## 4. Roadmap — high-level progression

Each phase ends with something demonstrable. The documented side-tasks map into these phases (noted).

### Phase A — Prove the VM (vertical slice)  ⟵ the immediate priority
Goal: **run one ADV scene end-to-end** in the new runtime — background image + dialogue + a choice +
a voice line — and match its `show-text` sequence to `build/text/dialogue.jsonl`.
Forces, and thereby de-risks, every core unknown at once:
- Port the container parser + VM core to C#.
- Implement the ADV effectful ops against Godot: `show-text`, `end-text-line`, `wait-for-input`,
  `set-font`, `play-voice`, `play-bgm`, `draw-texture`/`create-texture`/`draw-string`, choices.
- ~~Resolve **just enough `call-script`** to enter/leave a scene~~ **✅ DONE** — full call-script
  resolution + execution landed (nested subroutine frames sharing globals). *Scene→scene chaining*
  (decision→scene) still needs the SCJUMP decision→scene-id native hop.
- **AGF → texture** for the one scene's art (side-task; `AGF2BMP2AGF.exe` already on disk).
- Treat the classified no-op markers as skips; validate the tentative-no-op ops via the dialogue diff.

### Phase B — Broaden coverage (playable ADV, then systems)
Execution order and decision gates are tracked in `docs/phase-b-framework.md`; Phase A completion remains
the entry condition.
- Implement the remaining effectful ops; Frida sessions for the opaque ones (the shortlist in
  `build/opcode-coverage.md`); Unicorn for `0x215`-style computational ops.
- Grow the **global-var map** (side-task 2.5: `*MES` writers → record-table readers → Frida field
  naming) — now a core enabler, not polish.
- Get a full chapter of ADV playable; then the dungeon/battle/menu systems (they run as bytecode —
  we implement the ops they use, not the rules).
- Side-tasks absorbed here: `STINIT` parser, remaining data schemas, save-file format (needed for a
  real playthrough — reversible struct work).

### Phase C — Externalize & modding foundation
- Move game data from bytecode-embedded tables to **editable external files** the runtime loads.
- Generalize the **override/mod-loading** (mod folders, load order) from the engine's native
  loose-file mechanism.
- Asset pipeline: AGF↔PNG, audio, packaging. → Tier-1 modding works.

### Phase D — Logic modding
- Integrate the **assembler** (Tier-2 bytecode-patch mods) and ship the **host hook API**
  (GDScript/C#). → Tier-2 modding works.
- Optionally invest in decompiler quality toward Tier 3.

### Phase E — Enhance, polish, productize
- Enhancements the VM unlocks: higher/wide resolution, faster text, QoL, save-anywhere, new-content
  mods. Modding docs + tools. Save/UX polish.

---

## 5. The VM as our analysis instrument — and the correctness bootstrap

Beyond being the runtime, the VM is the best analysis tool we can build — **but only once it is
validated-correct, and that ordering is load-bearing.**

**Upside: owning the VM turns static RE into dynamic observation.** Instrumenting the original
packed `AGE.EXE` means fighting an anti-debug binary with Frida; instrumenting *our* interpreter is
one line in a handler. So a class of documented side-tasks become built-in debugger views instead of
separate investigations:
- **Live named-global watch** — with the global-var map, watch state change in real time; take damage
  → see which global moved → *that is* the "name which stat" step (`name-resolution.md` → Future),
  now a debugger feature, not a Frida session.
- **Call-graph / dispatch trace** — watch `call-script` resolve live, helping crack the entry-point
  registry dynamically.
- **Breakpoints, single-step, var-bank inspection, opcode/script/global coverage**; and because VM
  state is just variable banks + a program counter, cheap **snapshot / rewind** (time-travel
  debugging nearly falls out of the design).
- **State-divergence differ** *(backlog — the payoff consumer of the trace facility).* Builds on the
  landed diagnostics seam (`Age.Engine/Diagnostics/ITraceSink`; spec/plan `docs/superpowers/{specs,
  plans}/2026-07-07-engine-diagnostics*`): align our per-scene trace against a reference stream (a Frida
  capture of the real engine's pc/branch sequence, a known-good seeded run, or the same scene ±a
  candidate seed) and report the **first instruction where control forks** plus the **global that fed
  the branch** — turning "the opening loops/​drifts somewhere in state" into "fork at `jcc g[0x…]`; seed
  this flag." The fine-grained successor to `Age.Cli sweep 0xADDR=VAL` (which only reports *which* scenes
  change ±seed, not *where/why*); it directly attacks the state-divergence root cause behind the 13
  STEP-LIMIT scenes, the gfx geometry drift, and the form-gated silences. Requires: `--trace-steps`
  granularity (have it), robust sequence alignment (LCS-style, not naive zip — the real work), and
  treating native-nondeterministic ops (rand-like `0x60` in SCJUMP) as expected-to-differ. Enables the
  "differential checks against known-correct behavior" named in the bootstrap order below.

**The hard caveat: the instrument is only as trustworthy as the VM is correct.** A VM that executes
*wrong* produces *wrong* observations — and circularly so: you would "learn" false facts about game
state from a broken interpreter and bake them into the global map, the dispatch model, and everything
downstream. **The analysis power is unlocked by correctness, not a substitute for achieving it.** You
cannot debug the unknown with an instrument you have not first validated. Until the VM is running
mostly correctly, using it for analysis is meaningless.

**Therefore the bootstrap order matters:**
1. Build the VM.
2. **Validate it against ground truth we already hold, by *independent* means** — chiefly the dialogue
   oracle (`build/text/dialogue.jsonl`: the VM's `show-text` sequence per scene must match), plus
   differential checks against known-correct behavior. This *external* oracle certifies the instrument.
3. Only then use the validated core's observability to understand the *adjacent unknown* — which
   globals mean what, dispatch, effectful-op behavior. **Correctness propagates outward from validated
   anchors.**

**Two refinements that bound the trust:**
- **Trust is per-subsystem, and only as strong as the oracle covering it.** The dialogue diff strongly
  validates the ADV/text layer. But computational/battle logic has *weaker* oracles — a wrong damage
  number can look plausible and pass unnoticed. So "mostly working" must mean *demonstrated correct per
  subsystem*; where oracles are weak (battle math), the VM-as-instrument is correspondingly less
  trustworthy, and **Frida/Unicorn cross-checks retain their value there** (exactly why Unicorn stayed
  on the list for computational ops). A green ADV oracle does not imply the battle math is right.
- **Coverage-limited, plus a chicken-and-egg.** Runtime tools only observe what a playthrough
  exercises (rare branches stay dark), so they complement rather than replace static analysis. And
  bootstrapping the VM needs *just enough* `call-script` dispatch to run before observation can help
  refine dispatch — hence Phase A hardcodes a minimal dispatch first.

**Net effect on sequencing:** the runtime debugger makes *most* of the Frida/Unicorn side-tasks
cheaper or obsolete — but only after the VM earns trust on a validated core. So the priority is to
reach *validated* correctness on the ADV layer first (via the dialogue oracle); that trusted anchor is
what makes the instrument usable for everything else. The debugger is a force multiplier on a correct
VM and dead weight on an incorrect one.

---

## 6. Extending to other AGE games and versions

### Other AGE games, same version (e.g. Kamidori, also SYS4)
**Reused for free:** container parser, VM core, backend adapters, the opcode table (the engine ABI
is shared across the family), disassembler/assembler, the whole extraction methodology.
**Per-game (inherent content work):** the **global-var map** (globals are game-specific), the
**data-table layouts** (each game's `*INIT` differs), assets, and any game-specific effectful behavior.
The **call-script registry is no longer a per-game long pole** — it's a raw index into that game's
SYS4INI file table, parsed directly by the runtime catalog (and exported by `parse_sys4ini.py` as
`build/callscript-names.json` for tooling); the resolver is generic. So the remaining long pole is really just the **global-var map**. Process: point
the toolchain at the new game's archives, re-run extraction, rebuild its global map, author a profile.
**This is the core payoff of the VM approach:** the *engine* cost amortizes across all AGE games; only
content-mapping recurs — far less than re-coding each game's logic bespoke.

### Other engine versions (SYS3 / SYS5) — one app, not many
Versions differ in: header (SYS4 `0x3C` vs SYS5 `0x44`), string codec (SYS4 cp932^0xFF vs SYS5
UTF-16^0xFFFF), opcode set (overlapping, version-specific; Kelebek's table already spans the family
and notes version-gated ops), operand types (SYS5 adds `0x8003+`), and archive magic (`S3IC`/`S4IC`/
`S5IN`). **Architecture answer: version-parameterize, don't fork.** One runtime with:
- a **version-detection** step (magic → SYS3/4/5),
- **pluggable front-ends** (header parser, string decoder, opcode table, archive reader per version),
- the **shared VM core** (the execution model — opcode+typed operands, var banks, control flow — is
  the same engine evolving),
- **per-game profiles** that name the version + game-specific maps.

So: **not a separate application per version — a manifest/profile selects the front-end + game data.**
SYS5 is well-covered (it's Kelebek's target); SYS3 is older and less documented and would need more
front-end work (Kelebek's parser only does SYS4/SYS5), but the plug-in shape accommodates it. Result:
one "AGE Engine" app that, given a profile, runs Himegari, Kamidori, a SYS5 title, etc., each moddable
through the same system.

---

## 7. Feasibility, risks, open questions

**Feasible? Yes — but it is the largest phase of the whole effort**, on the scale of a small ScummVM
target. The decoding groundwork substantially de-risks it (we understand the format, 97% of opcodes,
the data, a partial global map). Biggest risks, with mitigations:
- ~~**`call-script` dispatch entangled in the packed AGE.EXE**~~ **✅ RESOLVED** — cracked statically
  via the Ghidra dispatch table (no Frida): `call-script <id>` = a raw SYS4INI file index; the C# VM
  executes it. (SCJUMP was *not* the registry, as first guessed.)
- **Effectful-op surface is large and quirk-laden** (esp. SRPG battle/dungeon UI) → ADV-first; defer
  SRPG; lean on the Frida shortlist.
- **AGF graphics** → low risk; `AGF2BMP2AGF.exe` (asmodean) already present.
- **Save format** → reversible struct work; needed before a full playthrough.
- **Toolchain (Python) vs runtime (C#) drift** → share the documented format spec; runtime is
  canonical.

**Open questions to resolve early:** ~~exact `call-script` mechanism~~ (solved); how scenes *chain*
(the SCJUMP decision→scene-id native hop — needed to sequence and to *add* content); how much the SRPG
layer's rendering diverges from ADV; save layout.

---

## 8. Immediate next step
Start **Phase A, the vertical slice** — it converts all of the above from architecture into evidence
and tells us fast whether option 3 is as feasible as it looks. Concretely: pick one small ADV scene,
stand up the C# VM core + Godot ADV backend, resolve just-enough `call-script`, convert that scene's
AGF art, and get its dialogue rendering and matching `build/text/dialogue.jsonl`. Everything else in
this roadmap is sequenced behind that proof.
