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
  ~10% of it); under *remake/enhance/mod* its editor, UI toolkit, asset pipeline, mod workbench,
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

## 3. Future modding architecture

> **Status and scope:** this section is design guidance for Phases C–E, not an active implementation
> task. The immediate priority remains a faithful, validated runtime. Preserve the seams this design
> will need, but do not build the mod platform before the base game works.

The faithful VM is the **compatibility floor, not the feature ceiling**. Original scripts must keep
their historical behavior, but mods targeting our runtime need not remain constrained to what the
proprietary `AGE.EXE` could load. Extended mode may add virtual resources and scripts, namespaced mod
state, host hooks and new services without changing the interpretation of unmodified AGE bytecode.

### 3.1 Two execution modes

- **Compatibility mode** runs original content with the selected game/version profile's historical
  semantics. A mod limited to replacement AGE scripts and original-format assets may also remain usable
  with the proprietary engine, although that is not a product requirement.
- **Extended mode** layers engine-owned services on top: mod manifests and load order, virtual asset and
  script identities, runtime callbacks, semantic events, extra saved state, modern UI and optional new
  script facilities. These mods target Open Maid Engine and are not expected to run under `AGE.EXE`.

The modes share one VM. Extended mode is an additive dispatcher and content layer, not a forked
interpreter or a second implementation of the game rules.

### 3.2 Modding surfaces and realistic boundaries

Use the least invasive surface that can express a change:

| Surface | Intended use | Relative cost |
|---|---|---|
| Declarative assets/text/data | replacements, translations, balance values, load order | low |
| Semantic events | common gameplay and presentation changes through named typed contexts | low–medium |
| Named bytecode patch points | game-specific inline behavior for which no general event exists | medium |
| AGE assembly patches | direct changes to original control flow and script behavior | medium–high |
| Raw VM/opcode hooks | expert escape hatch and discovery tool | high/brittle |
| New engine services | genuinely new UI, state or mechanics beyond the AGE ABI | engine-extension work |

**High-confidence uses once the full runtime exists:** layered asset/text/audio/font replacement;
presentation and accessibility changes; balance edits; bug fixes; cheats and challenge modes;
randomizers; replacement dialogue and event branches; debugging overlays; and new ADV scenes built
from existing operations. These are where the reimplementation improves most over the native loose-file
and translation workflows: mods can be small, ordered, validated and composed without repacking the
original archives.

**Feasible but subsystem-sized:** adding rather than replacing skills/items/units; new maps or routes;
new battle effects; larger rosters or inventories; cross-game content ports; and narrative total
conversions that retain the original game systems. The VM can grow its storage, but original scripts
also encode table strides, loop bounds, switches, UI capacity and save assumptions. Expanding a table
therefore requires auditing all of its consumers; increasing a C# dictionary's capacity alone does not
teach the bytecode that a new entry exists.

**Effectively an engine/game fork:** real-time combat, multiplayer, a 3D conversion, or replacement of
the SRPG rules with an unrelated genre. Open source makes these possible in the literal sense, but they
are not ordinary VM mods and should not distort the base mod API.

For data edits, prefer named **post-initialization patches** over replacing whole `*INIT` scripts where
possible. The original initialization remains the compatibility oracle; the profile applies declarative
changes to named globals/records afterward. Replacement is easy, while expansion retains the capacity
caveats above. Longer-term external JSON data remains useful, but it should have an explicit mapping to
the VM state rather than becoming an unsynchronized second source of truth.

### 3.3 A layered authoring stack

No single language should serve every layer:

| Layer | Preferred representation |
|---|---|
| Mod identity, dependencies, load order, assets, data patches and hook selectors | declarative manifest/patch files |
| Runtime behavior, event callbacks and UI glue | embedded scripting language; **Lua is the leading candidate**, pending a later integration spike |
| Direct modification of original scripts | annotated AGE assembly + assembler |
| Substantial new narrative/content authoring | optional future **Open Maid Script** scenario DSL |

Lua and Open Maid Script solve different problems. Lua would run **alongside** the AGE VM and talk to a
controlled engine API; it would not need to compile into AGE bytecode. Open Maid Script, if justified
later, should be a narrow content language for scenes, dialogue, choices and common presentation rather
than an attempt to replace Lua as a general-purpose language. Its eventual backend could be AGE
bytecode, an engine-owned extended representation, or generated Lua coroutines; defer that choice until
the ADV and hook APIs expose stable abstractions.

AGE assembly remains important. It is the most exact route for changing original logic and the only
route likely to preserve native-engine compatibility, but it should not be the normal requirement for
behavioral mods.

### 3.4 Hook selectors: identify inline code once

Script-level hooks are useful (`before/after ADDEXP.BIN`) but insufficient because much AGE logic is
inlined. A global `before opcode 0x55` callback followed by several global checks is technically
powerful and ergonomically unacceptable. The profile/toolchain must pay the identification cost once
so every mod author does not rediscover the same context.

Every executable site exposed to mods needs a stable **original-site identity** containing at least:

```text
game/profile + script catalog identity + base-script fingerprint + original dword offset
```

Use the instruction's original body/dword offset, not its decoded-list index. Filename alone is not
enough because loose patches, append content and game revisions may collide. The base fingerprint lets
the loader reject an unsupported revision explicitly instead of silently attaching a hook to the wrong
instruction.

Profiles then map those sites or regions to durable names:

```text
himegari.experience.before_award
himegari.experience.after_calculation
himegari.battle.before_damage
himegari.unit.on_level_up
himegari.scene.before_transition
```

A named patch point records the ugly game-specific facts: exact scripts/sites, supported fingerprints,
captured operands or globals, expected inputs/results and any revision variants. A mod consumes a typed
event or named point and should not need to know that its value currently lives at `G[0x152616]`.

When one implementation is repeated inline, resolve it **statically at mod load**, not on every VM
step. A selector may match a short normalized instruction sequence with typed-operand constraints,
wildcards/captures, nearby strings/comments/calls/labels and an expected match count. Resolution produces
an O(1) runtime table:

```text
(script identity, original offset) -> patch-point id(s)
```

Start with exact/syntactic patterns. Only add local-number normalization or control-flow-aware matching
when real mods demonstrate the need; a general semantic decompiler is not a prerequisite. Zero or
ambiguous matches are hard validation errors. Never guess a nearby site.

Many useful changes concern a calculation **region**, not one opcode. A region patch point can expose
entry/exit sites, captured inputs and a result lvalue, enabling `before`, `after`, or `override result`
behavior. Result override is safer and more composable than skipping an arbitrary instruction range.
Whole-region replacement remains an advanced operation because omitted side effects or temporary-state
updates can violate script assumptions.

### 3.5 Preserve identities and detect conflicts

Resolve all profile selectors against the pristine decoded base script before applying mod transforms:

```text
load and fingerprint base script
    -> decode and assign immutable original-site ids
    -> resolve profile and mod selectors
    -> apply ordered declarative transforms
    -> attach callbacks to original-site ids
    -> execute
```

Inserted instructions receive mod-owned identities; later insertions do not move another mod's target.
A full replacement `.BIN` is intentionally coarse-grained: two mods replacing the same script generally
cannot be merged and should produce a clear conflict. Named hooks, data patches and constrained
instruction transforms are the composable path. Load order must be deterministic, visible and recorded
in saves/diagnostics.

The compatibility contract is deliberately limited:

> Open Maid Engine detects structural conflicts it can prove, reports interactions it can observe and
> makes composition order deterministic. Absence of a detected conflict does **not** imply that mods are
> behaviorally compatible.

Classify interactions rather than returning one misleading compatible/incompatible bit:

- **Hard conflict:** overlapping replacement/deletion, incompatible whole-script replacements, an
  unsatisfied dependency or a selector that is invalid for the installed base.
- **Ordered interaction:** multiple inserts at one anchor, callbacks on the same hook point, or multiple
  data operations whose declared order determines the result.
- **Override:** more than one package supplies one logical asset/script and the visible load order chooses
  a winner.
- **Potential semantic interaction:** different sites touch the same named state, connected control-flow
  regions or related events. Report when known, but do not pretend it proves incompatibility.
- **Unknown/opaque behavior:** Lua or other extension code whose effects cannot be established statically,
  especially code using the raw `maid.vm` surface.

Two mods can edit disjoint instructions yet still invalidate each other's assumptions, redirect flow
away from a hook, or mutate the same state through unrelated sites. Conversely, two callbacks at the same
event may compose intentionally. Mechanical overlap is valuable evidence, not a compatibility proof.

Improve runtime diagnosis by attributing normal-API behavior to the responsible mod: registered hook
points and callback order, named globals/state read or changed, event values before/after each callback,
asset/script resolution, cancellation/control-flow overrides and exceptions. An optional development
trace should make chains such as `base 50 -> level-gap mod 25 -> double-exp mod 50` explicit. Raw VM
access remains less observable and should be marked advanced/opaque rather than falsely analyzed.

Raw opcode hooks remain available as the expert escape hatch, but their filters should be declarative
and compiled into site sets—for example opcode + script + offset range + named global read/write + call
context. Lua/C# callbacks should run only for resolved matches, never receive every arithmetic opcode and
discard 99.99% themselves.

### 3.6 Open Maid Pack (`.omp`)

Use **Open Maid Pack (OMP)** as the canonical distribution and mount unit. An OMP is a specification and
manifest layered on a standard ZIP container, not a new compression algorithm. Existing ZIP tools,
checksums and libraries remain usable; already-compressed/streamed media can be stored without additional
compression. The same layout must work unpacked as a development mod for source control and hot reload.

Conceptual layout:

```text
example-mod.omp
├── manifest.toml
├── assets/
├── data/
├── text/
├── lua/
├── selectors/
├── patches/
├── open-maid-script/
├── config/
├── README.md
└── LICENSE
```

The manifest owns mod id/name/version, OMP format version, engine-API range, supported game/profile and
base fingerprints, dependencies/conflicts/load-order hints, requested capabilities, Lua entry points,
selectors/transforms, virtual assets/scripts, configuration schema and saved-state schema/version.
Package paths and ids are namespaced; no OMP writes into the original install or scatters loose files
beside `AGE.EXE`.

The content resolver mounts packages as ordered layers above the native loose/append/base sources and
can show the full provider/override chain for every logical resource. Multiple asset providers are an
explicit ordered override, not automatically a hard conflict. Runtime script hooks normally attach to
the dispatcher without inserting AGE instructions; structural bytecode patches follow the pristine-base
resolution and one-time link pipeline in §3.5.

Patch operations should express intent (`insert before/after site`, `replace operand`, `replace/delete
region`, `redirect branch`, `override result`) rather than shipping adjusted byte offsets. Inserts at one
anchor can be deterministically ordered; edits to the same operand or overlapping replacement/deletion
are conflicts. A patch that intentionally targets code introduced by another mod must declare that
dependency and target an exported mod-owned site identity/version.

Loading is transactional: validate archive paths/sizes, resolve dependencies and a deterministic order,
verify base fingerprints, resolve all selectors, build the combined transform plan, report interactions,
link final scripts/catalogs and only then start the game. Cache the linked result by base/profile plus the
ordered package hashes and configuration; never cache by mutating game files.

Record the resolved OMP ids, versions, hashes, order and relevant configuration with each save. A changed
stack produces a clear warning, not a claim that the save is safe or unsafe. Packages are untrusted input:
reject path traversal/case collisions/decompression abuse, do not load native binaries by default and
grant filesystem/network/process/.NET capabilities only through explicit user approval. Signatures and a
repository can come later; deterministic builds, hashes and strict validation come first.

### 3.7 GUI-assisted discovery and authoring

The future GUI should let an author work from either direction:

1. Pick a curated profile event/patch point and generate a manifest entry plus handler stub.
2. Browse annotated disassembly or the best supported pseudo-decompilation, select an instruction or
   region, and have the tool generate a fingerprinted selector.
3. Start from live gameplay: break on named-global reads/writes, inspect the current script/offset and
   call stacks, record a trace region, search the corpus for similar regions, then export a selector
   draft with expected match counts.
4. Assemble assets/data/scripts, validate them against a chosen base and mod stack, preview the merged
   script/resource plan, then build a deterministic `.omp` (or run the unpacked form directly).

The ADV page locator already demonstrates the core vocabulary: translate a visible game location into a
stable bytecode coordinate. A mod workbench generalizes that workflow. The GUI must show when a selector
is exact, pattern-based, revision-specific, ambiguous or invalid; generated configuration remains plain
text and reviewable rather than becoming an opaque editor project. Its conflict view should distinguish
hard overlap, ordered callbacks/patches, overrides, potential semantic interactions and opaque runtime
code, and show a merged-script view where practical.

### 3.8 Runtime scripting API

Lua is attractive for runtime mods because it is familiar, embeddable, dynamically loadable and already
has mature editor support. It avoids requiring users to compile C# assemblies and keeps drop-in mods
behind an explicit API boundary. The integration choice (managed interpreter versus native runtime) is a
later portability/performance spike, not a decision to make during VM bring-up.

Keep the selector outside the handler language so it can be validated before executing mod code:

```toml
[[hooks]]
point = "himegari.experience.after_calculation"
handler = "scripts/double_exp.lua:on_experience"
priority = 100
```

Conceptual handler:

```lua
function on_experience(event)
    event.amount = event.amount * 2
end
```

The normal API should be named and capability-oriented:

- `maid.events` — typed semantic events.
- `maid.hooks` — curated and custom bytecode patch points.
- `maid.game` — profile-defined named game-state access.
- `maid.assets`, `maid.audio`, `maid.ui` — controlled host services.
- `maid.state` — namespaced serializable mod state.
- `maid.log` — attribution-aware diagnostics.
- `maid.vm` — explicitly advanced raw scripts/globals/operands/call-stack access.

Do not expose the VM dictionaries, arbitrary .NET reflection or raw Godot nodes as the normal API.
Semantic events may be implemented using opaque globals underneath, but each mod should receive named
values and controlled mutation points. The profile pays that reverse-engineering cost once.

Runtime constraints are part of the contract:

- VM-triggered callbacks execute on the VM thread; Godot/UI work crosses the host boundary to the main
  thread. A callback never keeps a raw Godot object.
- Arbitrary opcode/region hooks cannot yield. Only explicitly suspendable operations such as dialogue,
  choices and waits may use coroutine-style scripting.
- Each mod owns a serializable namespace. Initially persist only simple scalars, arrays and string-keyed
  tables—not functions, engine handles or arbitrary object graphs.
- Callback order is deterministic. Errors identify the mod, handler and bytecode context; time/instruction
  budgets prevent accidental infinite callbacks.
- Treat scripting as a capability boundary, not a perfect security sandbox. File/network/process/.NET
  access is absent unless the user explicitly grants it.
- Version the API and generated editor type stubs independently of game profiles.

### 3.9 Load-bearing prerequisites already identified

- **Global-var map** — named state access, selector captures and semantic events all depend on knowing
  what hot globals mean.
- **Call-script resolution — ✅ SOLVED (2026-07-07).** `call-script <id>` is a raw index into the
  SYS4INI file table (`docs/engine-re.md`, `name-resolution.md §1`), and the C# VM executes it as a
  nested frame. This supplies stable script/catalog identity and makes virtual script dispatch feasible.
- **Trace/diagnostic seam — LANDED.** Frame enter/exit and per-instruction diagnostics already carry the
  beginnings of script/site context. The future mod dispatcher should evolve beside this seam, not turn
  diagnostics themselves into a mutable public API.
- **Validated subsystem behavior.** Semantic hooks are trustworthy only where the underlying VM/profile
  behavior has an independent correctness oracle. Modding does not relax the validation discipline in §5.

### 3.10 Deferred implementation sequence

When Phase C/D actually begins, proceed incrementally:

1. Define OMP's language-independent manifest/layout, dependencies/load order and selector schema; support
   both packed `.omp` and unpacked development directories.
2. Preserve immutable script/catalog identity and original dword offsets throughout execution.
3. Generalize diagnostics into a read-only, prefiltered execution observer.
4. Implement profile-owned named patch points and strict fingerprint validation.
5. Add `before`/`after` contexts, then safe result/write override; test the contracts directly in C#.
6. Add load-time pattern resolution, transactional combination and classified interaction reporting.
7. Put user scripting behind an `IModRuntime`-style boundary and spike Lua as the first runtime.
8. Add typed semantic events where actual mod use cases justify them.
9. Add namespaced saved state, virtual assets/scripts and controlled UI/audio services.
10. Integrate AGE assembly/reassembly and instruction-level transforms.
11. Consider Open Maid Script only after the stable ADV/event vocabulary is known.

This ordering avoids baking Lua or a speculative DSL into the VM core. It also leaves room for another
runtime later while making the selectors, event contracts and mod packages language-independent.

**Readability, concretely:** annotated disassembly is available now (opcode names, raw offsets,
call-script names and a growing global map). Pseudo-decompilation for reading is feasible and has been
demonstrated on RECOVER. Clean round-trippable high-level source remains a compiler project and a stretch
goal; none of the Tier-2 hook design depends on solving it.

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

**Persistence compatibility floor (decided 2026-07-24):** the 1.0 path uses AGE's native binary
domains and lifecycle—shared `SAVE.DAT`/`SAVE.BAK`, `RT.DAT`/`RT.BAK`, numbered `SAVE##.DAT`, and
paired BMP `.STH` thumbnails. Keep the codec behind a profile/save-service boundary. Human-readable
JSON inspection/export, migrations, and namespaced mod state are additive extended-mode work, not a
replacement for compatibility-mode import/export. The recovered native contract lives in
`docs/engine-re.md`.

### Phase C — Externalize & modding foundation
- Add **editable named data overlays** mapped explicitly onto the VM's `*INIT`-produced state; external
  files must not become an unsynchronized second source of truth.
- Generalize the **override/mod-loading** from the engine's native loose-file mechanism into ordered OMP
  mounts, deterministic dependency resolution and unpacked development packages (§3.6).
- Asset pipeline: AGF↔PNG, audio and deterministic `.omp` packaging. → Tier-1 modding works.

### Phase D — Logic modding
- Integrate the **assembler** (Tier-2 bytecode-patch mods) and ship the **host hook API**
  behind a language-independent runtime boundary; spike Lua as the first user-facing runtime per §3.
  → Tier-2 modding works.
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
**Reused for free:** container parser, VM core, backend adapters, the engine-version opcode ABI,
disassembler/assembler, and the whole extraction methodology. The ABI is shared across the family,
but semantic and implementation coverage still grows as another game's corpus exercises services that
the first game did not use.
**Per-game (inherent content work):** the **global-var map** (globals are game-specific), the
**data-table layouts** (each game's `*INIT` differs), assets, and any game-specific effectful behavior.
The **call-script registry is no longer a per-game long pole** — it's a raw index into that game's
SYS4INI file table, parsed directly by the runtime catalog (and exported by `parse_sys4ini.py` as
`build/callscript-names.json` for tooling); the resolver is generic. So the remaining long pole is really just the **global-var map**. Process: point
the toolchain at the new game's archives, re-run extraction, rebuild its global map, author a profile.
**This is the core payoff of the VM approach:** the *engine* cost amortizes across all AGE games; only
content-mapping recurs — far less than re-coding each game's logic bespoke.

#### Opcode ABI registry must be independent of per-game coverage

A cursory Kamidori boot probe on 2026-07-20 validated much of this boundary: its own `SYS4INI.BIN` and
archives loaded, `SYSTEM4.BIN` completed the initializer chain, call-script resolution entered
`TITLE.BIN`, and title graphics/resource commands resolved. The first hard stop was not a different
container or catalog. Kamidori's title loop reached opcode `0x1be` (`u0041D9D0`, argc 2) at bytecode
offset `0xd7`. That opcode exists in Kelebek's full AGE/SYS4 table, but it is absent from the generated
`build/opcodes.json` because that runtime artifact currently contains only opcodes observed in Himegari.
`Sys4Loader` therefore stopped decoding at the unknown opcode; `TITLE.BIN` returned as if it had ended,
and Godot displayed its ordinary `— end —` marker instead of reporting an incompatibility.

The future multi-game design must keep three separate concepts:

1. A **version ABI registry** containing every known opcode number, operand count/shape, and version gate
   needed to decode that SYS generation, regardless of whether the active game uses it.
2. **Per-game observed coverage**, used for prioritization, provenance, and regression reporting but never
   to decide which otherwise-known instructions the runtime parser is allowed to decode.
3. **Semantic/runtime implementation coverage**, which may remain incomplete and should report a precise
   unsupported-service error containing game/profile, script, opcode, and bytecode offset.

An opcode that is absent even from the selected version ABI should likewise be a structured decode error,
not a synthetic final instruction or natural script return. Supporting a new same-version game then means
selecting the complete shared ABI, measuring its corpus against existing semantics, and implementing only
the newly exercised services. It should not require cloning the VM or manufacturing a new parser table
from that game's corpus.

The probe also exposed a separate profile/presentation concern: Kamidori creates a `1024x576` render
target while the current Himegari frontend assumes an `800x600` presentation. Logical canvas geometry,
scaling, and any game-specific defaults therefore belong in the selected game profile or script-driven
surface state rather than in a forked frontend. This experiment was diagnostic only; no Kamidori support
or `0x1be` semantics were implemented.

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
