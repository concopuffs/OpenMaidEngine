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

### Phase A — Prove the VM (vertical slice) — completed
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
`docs/engine-re.md`. The common container codec/store and typed shared `SAVE.DAT` payload landed on
2026-07-24. Profile-owned selected integer/string cells now survive across scene VMs and are wired to
their four native opcodes. The base/append catalog sections are now decoded as encrypted resource-seen
markers: opcode `0x19d` queries them, successful VFS opens add them, and compatibility saves write
native-decodable tables. Native `RT.DAT`
import/export and the packed-script/T1 ReadTextDB queue/commit/query lifecycle are also implemented,
including `message:ReadTextSkip` ops `0x1ca`/`0x1cb` and state query `0x1cc`. Numbered active-frame state
is now implemented in native layout 3: metadata query, paired `.DAT`/`.STH` lifecycle, exact native BMP
thumbnail I/O, six global banks, retained surface/gfx state, history, and nested frame restoration through
the `0xae` rendezvous. The real `SAVE.BIN` script is covered end to end for listing and loading; the
read-only installed-save gate now continues through `CALLBACK_LOAD`, reconstructs
`SYSTEM4.BIN → FORT.BIN`, and reaches FORT's `CHMENU` gameplay poll. Full restoration replaces only the
serialized mutable bank prefixes (preserving initialized unit/stage/string definitions), restores the
retained BGM/SFX state, preserves initialized flag-zero system surfaces while overlaying explicit saved
reload records, reproduces the native configuration-gated all-surface release when explicitly enabled,
and writes native per-slot reload/created metadata rather than treating every texture as reloadable.
Opcode `0x259` supplies its real script-entry reload-policy clear. Selected offscreen render targets now
receive actual retained-object pixels, closing the black-thumbnail half of the first port-authored-save
failure. The following immediate-resave oracle exposed and fixed two separate numbered-state defects:
`0xae` now re-establishes the saved `0x1ad` gameplay-frame boundary before a later save UI opens, and
retained objects use AGE's sparse stride plus native-initialized matrix/default bytes while preserving
unnamed fields. The next fresh rewrite also proved that reconstructed managed ancestor frames must retain
their original T1/T2/T3 coordinates while parked at synthetic `0xae`; preserving those coordinates fixes
the later SYSTEM4 unwind that otherwise re-entered the Eushully intro. Slot 005 subsequently passed the
base-load and stage-launch round trip. Dungeon-authored slot 006 exposed one further native-ordering
requirement: restored scripts must begin at their ordinary entry, run frame-local prologues, and reach
`0xae` themselves. Matching that order restores FIELD's 80% zoom table entry and produces the dungeon
map from the unchanged slot; interactive slot-006 confirmation passed. The shared-profile lifecycle is
also closed: accepted frontend shutdown stops and joins the VM worker before writing `SAVE.DAT` plus
`RT.DAT`, honors native `NoSaveDat=0` shutdown policy without suppressing numbered-save flushes, preserves
loaded accumulated playtime, and handles repeated teardown and I/O failure safely.
Repeated live saving and loading through the early dungeon playthrough is now user-accepted: numbered saves
restore into active gameplay and remain usable across repeated cycles without a newly observed persistence
failure. Treat native-layout gameplay save/load as confirmed for the current Phase-B slice; future concrete
edge cases can reopen it without retaining a generic acceptance blocker.
JSON inspection/export,
namespaced mod data, and migrations remain additive extended-mode work rather than 1.0 compatibility
requirements.

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

#### Planned maintenance slice — codebase consolidation

**Status (2026-08-02): step 1 complete; step 2 underway.** The runtime and tooling now have enough independent
regression coverage to support behavior-preserving cleanup: 590 engine tests, eleven directly runnable Python
tool suites, the Godot build/self-test, and the installed-corpus gates. The project layout itself is sound,
but a few files have become navigation and ownership bottlenecks: `VirtualMachine.cs`, `GfxState.cs`,
`Main.cs`, `GodotAdvHost.cs`, and `extract_init.py`. Repository entry points are also partly machine-local,
and the test project recompiles frontend/tool sources by link instead of consuming one production assembly.

This is a maintenance effort, not a runtime redesign. Preserve bytecode behavior, native-format
compatibility, current command paths, generated-file ownership, and the one-engine/many-profiles direction.
Each step should land in bounded commits with the full validation level appropriate to the touched boundary;
do not mix mechanical moves with semantic changes.

1. **Create a reproducible project front door — completed 2026-08-02.** Replace the locally excluded,
   machine-path-specific
   `run-godot.ps1`/`.cmd` workflow with a tracked launcher whose explicit parameters and documented
   environment fallbacks select Godot and the game root. Implement the already-listed one-command
   validation driver from `docs/tools-reference.md`, with named levels that distinguish hermetic/core,
   workspace-corpus, Godot runtime, and complete validation rather than silently skipping unavailable
   prerequisites. Add a navigation-only repository README that links canonical references instead of
   copying their facts. Keep personal defaults in ignored local state, never in the tracked launcher.

   **Gate:** from a clean checkout, a developer can discover prerequisites, generate required runtime
   metadata, build/test the engine, and launch or self-test Godot through tracked commands. Every validation
   level exits nonzero on a required failure, summarizes pass/fail/skip with reasons, and detects leaked
   child processes. No tracked script contains this workspace's absolute paths.

2. **Perform behavior-neutral physical splits.** Keep public types, namespaces, commands, and dispatch
   behavior stable while dividing the large runtime files by existing domains. Move `Main` self-test code
   first, then Godot audio/movie/compositor/input sections; split `GodotAdvHost` into ADV text,
   presentation/input, surfaces, movies, and audio; split `GfxState` contracts, surfaces, retained objects,
   animation, and presentation; and thin `VirtualMachine.Step` through domain handler methods without
   replacing the proven dispatcher. Keep `tools/extract_init.py` as the documented CLI while moving its
   stage, battle, card, routine, gallery, and general table implementations plus tests into importable
   modules.

   **Progress (2026-08-02):** five bounded splits completed the planned `Main` decomposition: its synthetic
   scene builder/threaded
   self-test harness into `godot/Main.SelfTest.cs`, then its BGM, voice, sound-effect, mixer, and bus-control
   surface into `godot/Main.Audio.cs`, then decoder staging, movie frame/audio publication, completion, and
   teardown into `godot/Main.Movie.cs`, then retained GPU/software composition, texture caching, transitions,
   raster helpers, and decision logging into `godot/Main.Compositor.cs`, and finally input routing, locator/debug
   hotkeys, debug-scene dispatch, cursor/dialog handling, and full-width text entry into `godot/Main.Input.cs`.
   The partial class retains the same node type, fields, signatures, execution order, and call sites; runtime
   validation, including all 590 engine tests and the Godot threaded self-test, remains green after each move.

   The first bounded `GodotAdvHost` split moved live/retained ADV text, surface glyph rasterization and cache
   state, history presentation, message-window alpha, and retained wait-indicator publication into
   `godot/GodotAdvHost.AdvText.cs`. The host remains a sealed `IHost` implementation, and presentation/input,
   reset, and surface-lifecycle consumers retain direct partial-class access to the moved state. Runtime
   validation remains green.

   The second bounded `GodotAdvHost` split moved script/presentation barriers, diagnostic and text-entry waits,
   input/message-skip services, cursor/foreground waits, frame and backbuffer publication, legacy transitions,
   scene reset/stop, frame pulse, and timed waits into `godot/GodotAdvHost.PresentationInput.cs`. Shared surface,
   movie, audio, and retained-text state remains directly accessible through the sealed partial class; runtime
   validation remains green.

   The third bounded `GodotAdvHost` split moved decoded-image caching, mutable surface pixel/resource/dimension
   state, allocation and binding, fill/copy/resolve operations, render-target publication, release/range teardown,
   and texture decode caching into `godot/GodotAdvHost.Surfaces.cs`. Movie publication and teardown retain direct
   access to the surface store through the sealed partial class; runtime validation remains green.

   The fourth bounded `GodotAdvHost` split moved movie surface bindings, ordinary and modal playback,
   movie-mask capture/publication and teardown, movie diagnostics, and frame/completion publication into
   `godot/GodotAdvHost.Movies.cs`. Presentation waits and mutable surface storage retain direct access through
   the sealed partial class; runtime validation remains green.

   The fifth bounded `GodotAdvHost` split moved BGM, voice and SFX resolution/dispatch, delayed voice state,
   volume/routing control, and blocking BGM fades into `godot/GodotAdvHost.Audio.cs`. Message-skip release,
   scene reset, and frame-pulse consumers retain direct access through the sealed partial class. The planned
   host decomposition is complete; runtime validation remains green.

   The first bounded `GfxState` split moved its public render, transition, diagnostic, persistence, animation,
   numeric-glyph, and handle-range value contracts into `engine/Age.Engine/Model/GfxState.Contracts.cs`.
   `GfxState` remains the same sealed runtime type, and every moved declaration is textually unchanged; runtime
   validation remains green.

   The second bounded `GfxState` split converted the sealed runtime type to a sealed partial class and moved
   surface resource/color-key state, created/reloadable classification, movie stop-time metadata,
   render-target/tile configuration, and surface lifecycle operations into
   `engine/Age.Engine/Model/GfxState.Surfaces.cs`. Reset, persistence, retained-object sampling, and transition
   teardown retain direct access through the partial class; runtime validation remains green.

   The third bounded `GfxState` split moved the retained-object record and registry/index, range-transform
   state and operations, object creation/query/clone/erase and draw binding, and numeric-glyph object generation
   into `engine/Age.Engine/Model/GfxState.RetainedObjects.cs`. Cross-domain reset/persistence, animation, and
   presentation sampling retain direct access through the sealed partial class; runtime validation remains green.

   The fourth bounded `GfxState` split moved shared animation-clock state, object color/source-cell/matrix/cyclic
   channels, animation control and forced completion, and interpolation helpers into
   `engine/Age.Engine/Model/GfxState.Animation.cs`. Presentation diagnostics, dirty-reason accounting, transition
   queues, and visible-scene sampling consume the moved state through the sealed partial class; runtime validation
   remains green.

   The fifth bounded `GfxState` split moved surface/movie transition queues, presentation activity and click-skip
   handling, presentation diagnostics and dirty-reason accounting, and visible retained-scene snapshots into
   `engine/Age.Engine/Model/GfxState.Presentation.cs`. `GfxState.cs` now retains only cross-domain mutation
   accounting, scene reset, persistence, and locking. The planned `GfxState` decomposition is complete; runtime
   validation remains green.

   The first bounded `VirtualMachine.Step` extraction converted the sealed VM type to a sealed partial class and
   moved VM-owned audio state, BGM restart semantics, and the existing BGM/voice/SFX/mixer case bodies into
   `engine/Age.Engine/Vm/VirtualMachine.Audio.cs`. The top-level dispatcher retains the exact opcode labels and
   routes only that family through `StepAudio`; public VM behavior and case-body logic remain unchanged. Runtime
   validation remains green.

   The second bounded `VirtualMachine.Step` extraction moved modal, asynchronous, and positioned movie playback,
   movie surface stop-time/activity queries, and movie-mask transition case bodies into
   `engine/Age.Engine/Vm/VirtualMachine.Movie.cs`. The top-level dispatcher retains all movie labels at their
   existing positions and routes them through `StepMovie`; the original instruction remains available for the
   two bytecode-offset compatibility paths. Runtime validation remains green.

   The third bounded `VirtualMachine.Step` extraction moved surface allocation/loading, texture binding and
   sizing, mutable surface fill/copy, render-target control, and transient-surface release into
   `engine/Age.Engine/Vm/VirtualMachine.Surface.cs`. The top-level dispatcher retains these labels at their
   existing positions, including the separate routing groups around numeric-glyph and retained-object cases;
   public VM behavior and case-body logic remain unchanged. Runtime validation remains green.

   The fourth bounded `VirtualMachine.Step` extraction moved retained-object registry queries, default-slot and
   geometry mutation, direct and embedded-range transforms, clone, and erase dispatch into
   `engine/Age.Engine/Vm/VirtualMachine.RetainedObjects.cs`. The top-level dispatcher retains all labels at their
   existing positions and routes the separated groups through `StepRetainedObject`; timed/cyclic animation,
   color, surface release, ADV binding, and presentation behavior remain outside this handler. Runtime validation
   remains green.

   The fifth bounded `VirtualMachine.Step` extraction moved retained spritesheet and color channels, timed and
   cyclic transforms, per-object animation control, frame-time sampling, and shared animation-clock dispatch into
   `engine/Age.Engine/Vm/VirtualMachine.Animation.cs`. The top-level dispatcher retains all labels at their
   existing positions and routes the separated groups through `StepAnimation`; presentation transitions and movie
   masking remain outside the guarded handler. Runtime validation remains green.

   The sixth bounded `VirtualMachine.Step` extraction moved queued surface-alpha transitions, frame and object-
   range publication, skip-aware blocking fades/crossfades, foreground-transition waits, and graphics command-
   queue clear into `engine/Age.Engine/Vm/VirtualMachine.Presentation.cs`. The top-level dispatcher retains all
   labels and aliases at their existing positions and routes them through guarded `StepPresentation`; movie-mask
   transition dispatch remains with the movie handler. Runtime validation remains green.

   The seventh bounded `VirtualMachine.Step` extraction moved live ADV text emission, layout/cursor/wait-indicator
   state, text style and glyph-delay control, direct surface-string and retained numeric-glyph rendering, and
   retained text/wait-object bindings into `engine/Age.Engine/Vm/VirtualMachine.AdvText.cs`. The top-level
   dispatcher retains all labels at their existing positions and routes the separated groups through guarded
   `StepAdvText`; the complete instruction remains available for emission and layout-reset bytecode offsets.
   Text-history and input/skip/auto services remain outside the handler. Runtime validation remains green.

   The eighth bounded `VirtualMachine.Step` extraction moved text-history recording control, metadata append and
   navigation, retained history rendering, metadata/voice lookup, and backlog clearing into
   `engine/Age.Engine/Vm/VirtualMachine.TextHistory.cs`. The top-level dispatcher retains all labels and aliases
   at their existing positions and routes them through guarded `StepTextHistory`; live ADV text remains with
   `StepAdvText`, while skip/auto-message services remain outside both handlers. Runtime validation remains green.

   The ninth bounded `VirtualMachine.Step` extraction moved persistent/active message-skip control, read-skip
   settings and queries, auto-message state/timing, and per-message voice/skip reset dispatch into
   `engine/Age.Engine/Vm/VirtualMachine.AdvServices.cs`. The top-level dispatcher retains all labels and aliases
   at their existing positions and routes them through guarded `StepAdvService`; shared state and refresh helpers
   remain in the VM coordinator because live-text and input paths also consume them. Runtime validation remains
   green.

   The tenth bounded `VirtualMachine.Step` extraction moved blocking ADV waits, hotspot registration/arming,
   cursor resources and virtual position, raw mouse/joystick callback registration and dispatch, action polling,
   and physical-input mapping into `engine/Age.Engine/Vm/VirtualMachine.Input.cs`. The top-level dispatcher
   retains all labels and aliases at their existing positions and routes them through guarded `StepInput`; public
   host-thread input entry points, shared synchronization, and callback service helpers remain in the coordinator.
   Runtime validation remains green.

   The eleventh bounded `VirtualMachine.Step` extraction moved the monotonic-time query, host sleep, relative
   timed-callback schedule construction, deadline/catch-up selection, and callback resumption into
   `engine/Age.Engine/Vm/VirtualMachine.Timing.cs`. The top-level dispatcher retains all labels and aliases at
   their existing positions and routes the separated groups through guarded `StepTiming`; animation-frame
   sampling remains with `StepAnimation`. Runtime validation remains green.

   The twelfth bounded `VirtualMachine.Step` extraction moved catalog-unlock lookup, numbered save/load and
   nested restore continuation, metadata/copy/delete, thumbnail persistence, and shared-profile integer/string
   dispatch into `engine/Age.Engine/Vm/VirtualMachine.Persistence.cs`. The top-level dispatcher retains all
   labels at their existing positions and routes them through guarded `StepPersistence`; capture/apply helpers
   and persistent coordinator state remain in `VirtualMachine.cs`. Runtime validation remains green.

   The thirteenth bounded `VirtualMachine.Step` extraction moved string byte length, addressed lookup/copy,
   inline arrays, rectangle search and stable index sorting, bounded integer queues/stacks, bit/range operations,
   and native-style random-modulo dispatch into
   `engine/Age.Engine/Vm/VirtualMachine.MemoryCollections.cs`. The top-level dispatcher retains all labels and
   aliases at their existing positions and routes them through guarded `StepMemoryCollection`; shared storage and
   address-resolution helpers remain in the coordinator. Runtime validation remains green.

   The fourteenth bounded `VirtualMachine.Step` extraction moved local jumps/calls/returns, value-switch
   construction, ADV coroutine handler save/yield/resume, and bounded labeled-yield dispatch into
   `engine/Age.Engine/Vm/VirtualMachine.ControlFlow.cs`. The top-level dispatcher retains all labels and aliases
   at their existing positions and routes them through guarded `StepControlFlow`; the complete instruction remains
   available for labeled-yield opcode/offset diagnostics. Process/root exit and cross-script lifecycle remain in
   the coordinator. Runtime validation remains green.

   The fifteenth bounded `VirtualMachine.Step` extraction moved process/frame/root exit, ordinary cross-script
   calls, mounted append autoruns, and preloaded script-slot load/call dispatch into
   `engine/Age.Engine/Vm/VirtualMachine.ScriptLifecycle.cs`. The top-level dispatcher retains all labels and
   aliases at their existing positions and routes them through guarded `StepScriptLifecycle`; frame execution,
   provider access, call-depth enforcement, and shared lifecycle state remain in the coordinator. Runtime
   validation remains green.

   The sixteenth bounded `VirtualMachine.Step` extraction moved integer arithmetic, bitwise and comparison
   operations, string comparison/concatenation/conversion/move, native byte-length and CP932 operations, and
   the host-backed fullwidth string editor into `engine/Age.Engine/Vm/VirtualMachine.Values.cs`. The top-level
   dispatcher retains all labels at their existing positions and routes the two groups around the timing query
   through guarded `StepValue`; shared operand storage, addressing, and native-string encoding remain in the
   coordinator. Runtime validation remains green.

   The seventeenth bounded `VirtualMachine.Step` extraction moved diagnostic operand/newline accumulation and
   synchronous show-and-clear prompt dispatch into `engine/Age.Engine/Vm/VirtualMachine.Diagnostics.cs`. The
   top-level dispatcher retains all labels and aliases at their existing positions and passes the complete
   instruction through guarded `StepDiagnostic`; shared accumulator state and native-context formatting remain
   in the coordinator. Runtime validation remains green.

   The eighteenth bounded `VirtualMachine.Step` extraction moved message-window alpha get/set, system-menu
   enable state, and system-menu show-delay get/set into
   `engine/Age.Engine/Vm/VirtualMachine.RuntimeSettings.cs`. The top-level dispatcher retains all labels and
   aliases at their existing positions and routes them through guarded `StepRuntimeSetting`; reset/default
   initialization, public menu-state accessors, and host state remain centralized. Runtime validation remains
   green.

   The nineteenth bounded `VirtualMachine.Step` routing closeout moved the final five recognized inline bodies:
   script-entry surface-policy reset, surface-persistence flags, individual surface release, native run-state
   compatibility, and initial-root-run query. They now route through the existing guarded surface and
   script-lifecycle handlers. `Step` contains only opcode-label grouping, domain-handler routing, and its proven
   unknown-op trace/fallback; the planned behavior-neutral physical decomposition is complete. Runtime validation
   remains green.

   **Gate:** no externally visible behavior or command changes; generated artifacts are byte-identical where
   deterministic, and the corresponding engine, Python, Godot, and corpus validations remain green after
   each domain move.

3. **Clarify runtime contracts.** Split the broad `IHost` surface into ADV, graphics, audio, movie, input,
   diagnostics, and lifecycle contracts while retaining one aggregate host accepted by the VM. Move neutral
   transport records such as RGBA images and movie/surface requests out of format-specific or host-specific
   namespaces so `Model`, `Hosting`, and `Sys4` no longer form avoidable dependency cycles. Preserve explicit
   headless defaults and diagnostics; interface cleanup must not turn intentionally unsupported presentation
   into false success.

   **Progress (2026-08-03):** the first bounded contract slice introduced `IDiagnosticHost` for recoverable
   warnings and modal diagnostic messages and `ILifecycleHost` for script-context entry/exit, sleep and timed
   deadline waiting, frame yield, and scene reset. `IHost` inherits both contracts and remains the aggregate VM
   entry point; all required members, default implementations, existing hosts, and transport-record locations
   are unchanged. Runtime validation remains green.

   The second bounded contract slice introduced `IAudioHost` for BGM, voice, sound-effect, fade, volume, and
   route-control operations. `IHost` inherits the focused contract; all fifteen members, required implementations,
   compatibility overloads/default chains, and existing host classes remain behaviorally unchanged. Runtime
   validation remains green.

   The third bounded contract slice introduced `IMovieHost` for ordinary, positioned, mask-transition, activity
   queries for movie surfaces, and modal playback. `IHost` inherits the focused contract; all five default
   members and existing host classes remain behaviorally unchanged. `GfxState` and
   `MovieMaskTransitionRequest` remain in their prior locations pending a separate dependency-cleanup slice.
   Runtime validation remains green.

   The fourth bounded contract slice moved the unchanged `MovieMaskTransitionRequest` record from `Hosting` to
   `Model`. VM, Godot, tests, and the movie host contract already consumed `Model`, so no call sites changed;
   removing the two now-unused imports eliminates `GfxState`'s reverse dependency on the host namespace. Runtime
   validation remains green.

   The fifth bounded contract slice moved the unchanged, format-independent `RgbaImage` record from the SYS4 AGF
   decoder file into `Model`. Decoders, renderers, persistence, Godot, tests, and the aggregate host now consume
   the neutral image contract; image-only format imports were removed while genuine SYS4 consumers retained
   theirs. Decoder and image behavior remain unchanged, and runtime validation remains green.

   The sixth bounded contract slice introduced `IGraphicsHost` for mutable surface fill/copy, retained-range and
   frame presentation, transition waits/fades, texture lifecycle, pixel capture/replace, drawing, and size
   queries. `IHost` inherits the focused contract; all seventeen members, required implementations, default
   fallbacks, and existing host classes remain behaviorally unchanged. Surface request records and the fade
   direction remain in their prior locations pending a separate transport cleanup. Runtime validation remains
   green.

   The seventh bounded contract slice moved the unchanged `SurfaceRectFill`, `SurfaceRectCopy`, and
   `SurfaceBlackFadeDirection` declarations from `Hosting` into `Model/SurfaceContracts.cs`. Every consumer already
   imported `Model`, so no call sites changed; surface request shape, enum values, and graphics behavior remain
   unchanged. Runtime validation remains green.

   The eighth bounded contract slice introduced `IInputHost` for modal fullwidth entry, ADV wait overloads and
   callback servicing, input clock, cursor resources, and skip-state interaction. `IHost` inherits the focused
   contract; all fourteen members, the required base wait, overload/default chains, and existing host classes
   remain behaviorally unchanged. Input transport records remain in their prior locations pending a separate
   cleanup. Runtime validation remains green.

   The ninth bounded contract slice moved the unchanged `FullwidthTextEditRequest`, `FullwidthTextEditResult`, and
   `AdvAutoWaitState` declarations from `Hosting` into `Model/InputContracts.cs`. Only the auto-advance timer and
   its focused tests required new imports; record shape, editor defaults, wait overloads, and all runtime behavior
   remain unchanged. Runtime validation remains green.

   The tenth bounded contract slice introduced `IAdvHost` for live and surface text, text-history presentation,
   ADV layout publication, message presentation settings, wait-indicator control, and page-presentation
   suspension. `IHost` now contains no members of its own and inherits all seven focused host contracts; all
   twenty-three ADV members, required implementations, overload/default chains, existing hosts, and transport
   record locations remain behaviorally unchanged. Runtime validation remains green.

   The eleventh bounded contract slice moved the unchanged `AdvWaitIndicatorConfig` and `DiagnosticMessage`
   declarations from `Hosting` into domain-specific `Model` contract files. The retained wait-indicator presenter
   no longer imports `Hosting`; two host-local consumers gained `Model` imports, and one test dropped its obsolete
   fully qualified hosting name. Record shape, frame selection, diagnostic presentation, and runtime behavior
   remain unchanged. This completes the planned runtime-contract split and neutral transport cleanup. Runtime
   validation remains green.

4. **Make the build graph express source ownership.** Stop linking production `.cs` files from `godot/` and
   `tools/movie-corpus-gate/` into `Age.Engine.Tests`. Extract the platform-neutral frontend/movie/diagnostic
   code into a small production project referenced by Godot, tests, and the corpus gate. Retain both existing
   solution files because Godot export requires the frontend-local solution.

   **Progress (2026-08-03):** the first bounded build-graph slice introduced the platform-neutral
   `Age.Engine.Frontend` production project and added it to both existing solutions. Page locator, timeline/trace
   and step-limit diagnostics, VM/window launch options, and performance-frame logging moved unchanged from
   `godot/` into the new owner. Godot and `Age.Engine.Tests` now use project references, eliminating seven direct
   test source links without changing public types, namespaces, behavior, or export configuration. Both solution
   builds and runtime validation remain green.

   The second bounded build-graph slice moved the unchanged internal movie decoder boundary/runtime, audio
   alignment policy, movie-surface registry, and RIFF/WAVE sanitizer from `godot/` into `Age.Engine.Frontend`.
   Five more direct test source links are gone. Explicit friend access is limited to `Himegari` and
   `Age.Engine.Tests`, so the move preserves the intentionally internal API instead of widening it; FFmpeg remains
   in Godot while implementing the moved boundary through that friend access. Both solution builds and runtime
   validation remain green.

   The third bounded build-graph slice moved the unchanged FFmpeg native interop and asynchronous/paced decoder
   from `godot/` into `Age.Engine.Frontend`. The two remaining movie test source links and the corpus tool's native
   source link were replaced by project references. Friend access now also names only the `Age.MovieCorpusGate`
   assembly required by the native frame-source seam; native ABI resolution, decoder pacing, seek/audio behavior,
   failure handling, and teardown remain unchanged. Engine, Godot, and corpus-gate builds plus runtime validation
   remain green.

   The fourth bounded build-graph slice moved the unchanged reusable movie-corpus discovery, decode-gate, and
   report implementation from `tools/movie-corpus-gate/` into `Age.Engine.Frontend`. The tool retains only its
   command-line entry point and consumes the internal gate through its existing friend/project boundary; tests use
   the same production implementation through their project reference. The final cross-project production `.cs`
   source link is gone, completing the planned build-graph ownership cleanup. Engine, Godot, and corpus-gate
   builds plus runtime validation remain green.

5. **Close repository-policy and storage gaps.** Add an explicit project license, third-party notices and
   checksums/provenance for committed binaries, consistent editor/build policy, a pinned .NET SDK, and a
   CI entry point built on the same validation driver. Remove the empty workspace-root `.git` directory and
   obsolete ignored output nests after separately verifying their exact contents; compact the real repository
   only after active work is committed. Configure an off-machine remote or equivalent backup before relying
   on local history as the sole recovery path.

   **Read-only audit (2026-08-03):**

   - **License/provenance:** the repository has no project `LICENSE`, `NOTICE`, third-party notice, or artifact
     manifest. The three committed binaries are unsigned and have no dedicated artifact records; audit hashes are
     `BinExtractALF.exe` 0.8
     (`46167cdf3da1f02733ce1e7f0da99d155e934ede0987ca79c8927f48feb25fe2`), metadata-free `LzssCpp.dll`
     (`596b9ddb07ca5c29cee609e846f1512654edec56310a90d6c2a6951be1e39fe1`), and PE-sieve 0.4.1.1
     (`ee684b34b37af24d1c1e9ca80ceeee6878cc80bb24c0c3793840c9acf905bd59`). PE-sieve's upstream is
     [BSD-2-Clause](https://github.com/hasherezade/pe-sieve/blob/master/LICENSE), but that license is not carried
     here; the extractor/DLL provenance and redistribution terms remain unverified. The two committed Kelebek
     source snapshots and transcribed opcode data come from the
     [upstream repository](https://github.com/Kelebek1/Eushully-Decompiler), where no license file was found, so
     their redistribution status also remains unresolved. FFmpeg is the good model: Windows/Linux manifests pin
     provider, release, URL, version, variant, and SHA-256, while build/export scripts stage its LGPL license.
     NuGet/Godot dependencies are versioned in project files but have no consolidated notice inventory.
   - **Editor/build/CI policy:** there is no `.editorconfig`, `global.json`, `Directory.Build.*`, package lock,
     Python environment manifest, or CI configuration. `.gitattributes` only fixes LF for shell scripts and
     whitespace exceptions for generated references. Projects target `net8.0`; the Godot SDK is pinned to 4.7.0
     and NuGet versions are explicit. The locally validated tools are .NET SDK 8.0.408 and Python 3.11.4, but only
     the Python 3.11 invocation is documented. `tools/validate.py --level core` is already the asset-independent
     CI entry point; workspace/runtime/full levels deliberately require the private game corpus and/or Godot.
   - **Repository/storage:** `age-reimpl/` is the sole real worktree. The workspace parent contains an empty
     `.git` directory, and the repository has an empty `.godot/logs` nest plus an accidental ignored
     `godot/godot/build` nest (3.7 MB). All inspected ignored outputs total 6.43 GiB across 12,190 files: 4.38 GB
     under `build/`, 1.29 GB under `godot/.godot`, 1.12 GB under historical `godot/build`, and the remainder in
     .NET/tool caches. The real `.git` holds 6,100 loose objects (71 MiB), no packs and no reported garbage; defer
     compaction until backup and cleanup decisions are complete.
   - **Recovery:** `main` is the only ref and has no remote, upstream, additional worktree, or repository/parent
     bundle. An unrelated off-machine backup cannot be disproved from repository state, but none is configured or
     evidenced here. Do not publish, delete ignored data, remove the empty parent `.git`, or run Git compaction
     until the user chooses the corresponding legal, retention, and backup policy.

   **Bounded execution order:** (1) add tracked SDK/editor/line-ending policy without reformatting existing files;
   (2) add an asset-independent CI workflow that invokes the existing core validation driver; (3) obtain the
   user's project-license choice and resolve, replace, or exclude every unverified third-party artifact before
   adding the project license/notice/manifest set; (4) configure a user-selected private/off-machine backup or
   remote; (5) after explicit retention approval, remove only the verified empty/obsolete ignored nests and any
   selected regenerable caches; (6) compact the real repository only after a clean committed state and verified
   backup. Public distribution remains blocked on item 3; destructive cleanup and compaction remain blocked on
   item 4 plus explicit target approval.

   **Policy baseline (2026-08-03):** item 1 is complete. `global.json` pins the validated .NET 8.0.408 feature
   band to stable patch releases; `.editorconfig` establishes UTF-8, final-newline, trailing-whitespace, and
   indentation defaults; and `.gitattributes` classifies repository text, Windows command wrappers, and binary
   assets. Generator-owned references that already commit CRLF are explicit byte-preserving exceptions with
   textual diffs, so rebuilding them does not create whole-file normalization churn. No existing source or
   project file was reformatted or renormalized. Both solution builds and the asset-independent core validation
   gate remain green under the tracked policy. Item 2, a CI workflow calling that same core gate, is the next
   safe slice.

   **Linux CI preparation (2026-08-03):** the eight legacy text blobs that prevented a clean Linux checkout or
   generated-reference comparison are now deliberately normalized to canonical LF: the engine solution, three
   engine project files, three generated Markdown references, and the generated Himegari opcode module. The
   generated files are once again ordinary text under the repository EOL policy; their existing whitespace
   exceptions do not disable normalization. Regeneration through all three owning tools is clean, Windows core
   validation remains green, and a Linux Git comparison reports no unstaged EOL drift. The first hosted CI job
   can therefore target Linux rather than carrying forward a Windows-runner dependency.

   **Linux core workflow (2026-08-03):** item 2 is implemented as `.github/workflows/core-validation.yml`. One
   read-only `ubuntu-24.04` job provisions Python 3.11 and the SDK selected by `global.json`, reports both resolved
   versions, and invokes the existing asset-independent core driver. Pull requests, `main` pushes, and manual
   dispatch share the job; branch-local concurrency cancels superseded runs, and seven-day validation-log upload
   occurs only after failure. The workflow has no cache, secrets, private corpus, Godot runtime, packaging, or
   deployment access. All four official actions are pinned to full immutable release SHAs. `actionlint` accepts
   the workflow and the underlying Python 3.11 core gate remains green locally. Because no remote is configured,
   the tracked workflow is dormant and its first hosted Linux execution remains a publication-time confirmation,
   not a condition hidden by this commit.

   **Private-repository preflight and binary purge (2026-08-03):** a clean source-only checkout disproved the
   earlier asset-independent claim: after opcode generation, 501/590 engine cases pass while 89 installed-corpus
   or native-oracle cases fail, and `test_opcodes.py` still scans the external script corpus. The target Gitea
   server demonstrates a working `ubuntu-latest` runner with checkout/setup-dotnet v4 and Gitea-specific artifact
   upload. CI must explicitly separate those workspace cases and inject a synthetic opcode fixture before Actions
   is enabled; missing private data will not be converted into green skips.

   The unverified `bin/BinExtractALF.exe`, `bin/LzssCpp.dll`, and obsolete `bin/pe-sieve32.exe` are removed from
   tracking and purged from every reachable commit before the first remote is added. A verified full bundle made
   immediately before the rewrite is the recovery boundary. `bin/` now tracks policy only and ignores optional
   machine-local extractor files; PE-sieve remains documented solely as a failed historical experiment. This
   resolves the committed-binary portion of item 3, but Kelebek-derived source/data and the user-owned project
   license choice remain unresolved for wider distribution.

**Not cleanup targets:** generated `build/` output, the two intentional solution files, historical
`docs/superpowers/` plans/specifications, and fidelity-specific complexity that is directly covered by the
native ABI. Reorganization is successful when ownership and reproduction become clearer, not when the raw
file count is minimized.

#### Planned polish slice — AGE-exact retained glyph-mask text renderer

**Scheduling (2026-07-30):** resume this as the next bounded presentation-polish subsystem, while still
allowing urgent playthrough blockers to preempt it. The original deferral gate has been met: the natural
gameplay spine and first-dungeon loop are playable, numbered save/load is provisionally accepted, the shipped
corpus has no remaining decoded effectful-opcode gaps, and representative system/menu presentation is live.
More importantly, the detached `Label` representation now causes behavioral defects rather than only
cosmetic native-parity differences: labels sit above the complete retained canvas, surface-drawn text does
not inherit its object's alpha/tint, and modal/publication paths need explicit visibility and replacement
exceptions. The current backend remains the migration fallback, not a target for further screenshot-driven
font tuning.

**Behavioral source:** implement the native contract decoded in `docs/engine-re.md`; screenshots are
integration/regression evidence, not tuning inputs. AGE selects an exact `LOGFONTA` into a
`CreateICA("DISPLAY")` information DC, obtains per-glyph metrics and 0..16 coverage through
`GetGlyphOutlineA(GGO_GRAY4_BITMAP)`, and applies its own integer coverage, outline-sampling, and surface
compositor. Godot currently substitutes FreeType `FontFile`/`FontVariation` masks and the `Label` outline
primitive, which cannot be made equivalent by choosing another embolden constant.

**Target architecture:**

1. Add a platform-neutral glyph request/result contract. The request carries the authored face, height,
   derived width, weight, character/code point, and raster policy. The result carries coverage bytes,
   dimensions/stride, glyph origin/bearings, and cell advance.
2. Move AGE's coverage conversion, mode-1 displacement, mode-3 rounded ellipse samples, clipping, and
   integer surface compositing into shared managed code. This layer must not know whether the mask came
   from GDI or FreeType.
3. Add a Windows GDI rasterizer using the decoded `CreateICA`/`CreateFontIndirectA`/
   `GetGlyphOutlineA` calls. Keep it as the shipped highest-fidelity Windows backend and as the independent
   reference oracle for the shared contract; it is not disposable calibration scaffolding.
4. Add an explicitly defined portable rasterizer behind the same contract. A FreeType/Godot
   implementation may produce different hinted pixels, especially when the proprietary requested face is
   unavailable; profile/configuration must select requested-face substitutions and the default fallback
   rather than claiming GDI pixel equivalence.
5. Route live ADV, retained text, History, immediate surface strings, and later ruby/furigana presentation
   through one cached glyph service. Retain the existing `Label` path as a temporary portable fallback until
   that integration is complete, then remove synthetic embolden/glyph-spacing constants from the exact path.

**Execution sequence:**

1. **Close the retained-record contract and pin baselines.** Before changing presentation, record the
   current semantic coordinates and native-backed checks for SC0000 voiced/unvoiced pages, STUDY/MAMES,
   HISTORY, BUNKI, and save/load restoration. Confirm the remaining implementation-sensitive native details:
   overflow/kinsoku behavior, object-range exhaustion, layout reset/republication, and restoration of live
   layout surfaces. The 2026-07-30 baseline closes those questions: the 20-byte glyph record is
   `{publication-chain flag, left, top, right, bottom}`; publication binds
   `layout_first_handle + reveal_index` from surface `layout_slot + 0x14`, using those four edges as the
   native source rectangle and `layout_origin + (left,top)` as the destination. Overflow compares glyph
   right/bottom strictly against the configured bounds. Horizontal wrapping leaves CP932 `、`, `。`, and
   `」` attached to the preceding line. Timed reveal stops at the configured handle capacity; all nine
   Himegari layouts reserve 500 handles, while the largest individual static corpus string has 68 characters.
   Reset clears the complete layout surface and handle interval. Op `0x20a` erases that interval and republishes
   through the current reveal index, clamping to record count but not capacity. Numbered saves persist the
   History backlog, not live layout surfaces or glyph records; initialized layout bindings survive history-tail
   decode and the ordinary saved-frame prologue/republication path reconstructs current-page presentation.
   `AdvRetainedTextContract` and focused engine tests pin the platform-neutral record, overflow, punctuation,
   capacity, SYSTEM4 binding, and restore boundaries before presentation changes.
2. **Land the backend-neutral mask and compositor core.** Completed 2026-07-30 without changing live
   presentation. `Age.Engine.Text` now owns immutable `GlyphRasterRequest`/`GlyphMask` contracts and the
   `IGlyphMaskRasterizer` seam outside the VM. Requests preserve Unicode scalar plus optional original CP932
   code and explicitly select native-CP932 or portable-Unicode policy; results normalize every backend to
   0..16 coverage, stride, GDI-style origin, cell extent, and cell advance. `AgeGlyphMaskCompositor`
   implements AGE's coverage conversion, modes 0–3, clipping, transparent-destination rule, maximum-alpha
   and integer RGB blend. `RetainedGlyphLayoutEngine` returns native-edge records, wrapping/kinsoku decisions,
   overflow state, and final cursor while rasterizing into a backend-neutral RGBA surface. A generic bounded
   LRU supports disposable backend font resources and `CachedGlyphMaskRasterizer` bounds masks. Fifteen
   synthetic-mask tests, independent of OS fonts, pin coverage rounding, bearings/clipping, mode-1 displacement,
   mode-2 quarter coverage, mode-3 sampling/overlap, wrapping, punctuation, vertical overflow, cursor results,
   and cache eviction.
3. **Add the independent Windows GDI reference backend.** Completed 2026-07-30 as the separate
   `Age.Engine.Text.Windows` library; the VM and platform-neutral `Age.Engine` still call no OS APIs.
   `WindowsGdiGlyphMaskRasterizer` owns a `CreateICA("DISPLAY")` information context, bounded/safely disposed
   `CreateFontIndirectA` handles, CP932 `GetTextExtentPoint32A`, identity `MAT2`, and
   `GetGlyphOutlineA(GGO_GRAY4_BITMAP)`. It accepts only explicit native-CP932 requests and reports backend id,
   policy, exactness, and call-chain detail through `GlyphRasterizerBackendInfo`. Availability requires Windows
   system ACP 932; another OS/ACP reports a diagnostic reason instead of silently claiming parity. Three
   representative regular/bold Mincho and bold Gothic requests byte-match an independent Unicode GDI oracle
   for coverage, stride, `GLYPHMETRICS`, extent, and advance. A later live capture resolved the apparent
   lighter-weight discrepancy: the `AGE Patch.exe`/`jprun.dll` reference launch realizes AGE's authored
   24px bold Mincho request as `MS Gothic`. Thirteen intercepted masks match an independent Gothic request
   byte-for-byte and Mincho 0/13. That result is specific to the wrapper and does not supersede the true
   native Mincho target; a realized-face override is optional launch-profile compatibility, not an
   acceptance requirement for the exact backend.
   This reference backend remains opt-in and is not yet connected to live presentation; the existing Label path
   therefore remains the current fallback.
4. **Move immediate surface strings first.** Completed 2026-07-30 for the exact Windows path. Ops
   `0x204`/`0x205` now convert the complete Unicode string to explicit CP932 glyph identities before changing
   any pixels, derive AGE's rebuilt negative height/half-width and weight, and composite the exact masks into
   a cloned numbered `RgbaImage` snapshot. Successful draws publish no `SurfaceTextDraw`, so both retained
   renderers consume the same pixels in ordinary handle order and naturally apply object alpha/tint, affine
   transforms, source clipping, capture, fill/copy, and backbuffer preservation. Synthetic integration tests
   pin later-handle occlusion, source clipping, scale/translation, static alpha/tint, animated fade, capture,
   copy, and clear. The Godot self-test additionally exercises the GDI backend, 2,048-entry mask-cache bound,
   dynamic GPU upload, and surface mutation path. Backend absence, an unrepresentable glyph, or an unresolved
   surface falls back atomically to the prior metadata/Label projection with an explicit diagnostic; that
   projection remains only for this temporary unavailable-backend case until the portable backend lands.
5. **Materialize live ADV glyphs as ordinary retained objects.** Completed 2026-07-30 for the exact Windows
   path. The VM passes `GfxState` plus the transient layout binding into presentation and accepts the measured
   final cursor in return; the persisted History ABI is unchanged. `RetainedAdvTextLayoutPresentation`
   retains each native edge record with the layout origin active for that run, builds the complete line into
   the layout surface once, and binds `first_handle + glyph_index` in reveal order. Consecutive
   literal/dynamic runs therefore begin at GDI's returned cursor instead of relying on Label concatenation.
   Timed reveal stops at capacity, while click/Skip publishes the remaining available handles through the
   existing consumed-click branch. Partial `0x1f7` erases ordinary handles; `0x20a` recreates all records
   through the current reveal count. HIDEWIN suspension erases that range and restoration republishes it.
   Layout reset erases the interval, clears its RGBA surface and transient records, and saved-frame-style
   reconstruction deterministically rebuilds the same bindings. Exact runs are excluded from the live Label
   snapshot; unavailable-backend runs retain it unchanged. Focused tests cover handle order/capacity,
   per-run origins, partial erase/republication, suspension, measured cursor handoff, reset, and
   reconstruction. The synthesized Godot VM self-test exercises timed exact reveal and reports
   `live-adv-text=retained-glyphs`; real GPU captures of SC0000 pages 1 and 7 confirm ordinary and voiced
   dialogue placement/wrapping alongside the immediate speaker-name surface. A 2026-07-30 clipping
   follow-up keeps those native measured-cell records intact but gives transient placements a separate
   vertically ink-safe source crop, because backend glyph black boxes and mode-3 outlines can extend below
   the measured cell; horizontal crops remain cell-bounded to preserve reveal isolation.
6. **Migrate History and the ADV wait indicator through the same retained path. — Completed
   2026-07-30.** Op `0x1d1` now rasterizes each HISTORY row into its target layout surface and publishes its
   native glyph interval through ordinary `GfxState` bindings. Row reset, wheel redraw, the broad native exit
   erase, recording re-enable, and surface release all remove the transient presentation without changing
   the persisted backlog. The configured wait atlas now binds at op `0x212`'s handle, advances source cells
   only on the configured frame boundary, participates in op-`0x20a` republication and HIDEWIN suspension,
   and disappears when the wait service stops. The standalone Godot wait `TextureRect` and its raw-callback
   visibility exception are gone. At this checkpoint exact History no longer entered the layout-keyed Label
   pool; step 7 subsequently supplied the portable backend and removed every remaining gameplay Label pool.
   Focused lifecycle tests, all 579 engine tests, the exact History
   Godot self-test, and a real SC0000 Vulkan capture cover the retained path.
7. **Complete and select the portable backend — completed 2026-07-30.** Godot's TextServer atlas is the
   portable mask/metrics source, avoiding another native dependency. Profile data in
   `godot/config/himegari-text-rendering.json` owns ordered Mincho/Gothic substitutions and bounded cache
   sizes. `auto` selects exact GDI when its ACP-932 gate passes and otherwise selects the explicitly
   non-pixel-exact Unicode backend; development runs can force either with `--text-backend`. Both modes pass
   the same immediate/live/History retained-glyph self-test. Gameplay `Label` pools and surface-text overlay
   projection are removed, so text always participates in the ordinary surface compositor. Publication uses
   the transient ink-safe crop established in step 5 in both backends.

**Acceptance gates:**

- Fixed synthetic-mask tests byte-match AGE's decoded 16-/32-bit compositor, including overlapping mode-3
  neighbors, clipping, alpha, and RGB integer rounding.
- On Windows, representative CP932 glyph masks, `GLYPHMETRICS`, and advances match a direct invocation of
  the decoded GDI request; tests compare returned data, not screenshot histograms.
- If wrapper compatibility is requested, a launch profile may override the realized face ahead of the
  raster request. Himegari's `AGE Patch.exe`/`jprun.dll` path realizes 24px authored Mincho as MS Gothic,
  but the true-native acceptance target remains the authored Mincho request.
- Surface-string tests prove text is occluded by later handles and inherits the bound object's alpha, tint,
  affine transform, source clipping, offscreen capture, and transition behavior in both render backends.
- Retained-layout tests prove per-glyph handle order, capacity bounds, partial/full erase, reset/republication,
  final cursor, reveal/Skip click consumption, and save/load reconstruction without Label visibility shims.
- SC0000, STUDY/MAMES, HISTORY, HIDEWIN, BUNKI, and save/load checks retain placement, wrapping, line advance,
  reveal timing, colors, effects, publication behavior, and native-backed lifetime after the backend swap.
- The portable backend starts without Windows fonts or GDI, reports/uses its selected substitution policy,
  and passes layout/legibility tests without being labeled pixel-identical to native AGE.
- Glyph/font caches and retained node/surface pools remain bounded. A complete line is rasterized once before
  reveal, so revealing another glyph does not rerasterize or upload the whole text surface and introduces no
  visible frame stalls.

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

The first runtime install-selection boundary is now in place. Godot receives one normalized game root,
defaulting to the executable directory with a current-working-directory fallback and accepting an
explicit `--game-root` override; the selected root is injected into the generic SYS4 catalog/store
instead of inferred from the Himegari repository layout. A future multi-profile launcher can therefore
own install discovery and pass the chosen profile's absolute root through the same stable argument.
Generated engine/profile metadata still needs an export-owned bundle before this constitutes complete
drop-in packaging; invocation details live in `tools-reference.md`.

#### Opcode ABI registry must be independent of per-game coverage

A cursory Kamidori boot probe on 2026-07-20 validated much of this boundary: its own `SYS4INI.BIN` and
archives loaded, `SYSTEM4.BIN` completed the initializer chain, call-script resolution entered
`TITLE.BIN`, and title graphics/resource commands resolved. The first hard stop was not a different
container or catalog. Kamidori's title loop reached opcode `0x1be` (`u0041D9D0`, argc 2) at bytecode
offset `0xd7`. That opcode existed in Kelebek's full AGE/SYS4 table, but was absent from the generated
`build/opcodes.json` because the runtime artifact contained only opcodes observed in Himegari.
`Sys4Loader` therefore stopped decoding at the unknown opcode; `TITLE.BIN` returned as if it had ended,
and Godot displayed its ordinary `— end —` marker instead of reporting an incompatibility.

**ABI-registry floor completed 2026-07-28.** `vm-map/opcodes.toml` and generated `build/opcodes.json`
now contain the complete 548-entry Kelebek AGE catalog: 248 instructions observed in Himegari and 300
catalog-only compatibility entries (including the already mapped but unused persistence opcode `0x19f`).
Each entry carries `observed_in_himegari`; per-game observation no longer limits decoding. A recognized
but unimplemented opcode reaches the VM's traced stub-and-advance fallback, so a probe continues beyond
it instead of losing the rest of the script. Catalog-only entries deliberately remain
`noop_headless=false`: skipping them is a temporary compatibility-probe behavior, not evidence that their
native effects are semantically safe to omit.

The multi-game design keeps three separate concepts:

1. A **version ABI registry** containing every known opcode number, operand count/shape, and version gate
   needed to decode that SYS generation, regardless of whether the active game uses it.
2. **Per-game observed coverage**, used for prioritization, provenance, and regression reporting but never
   to decide which otherwise-known instructions the runtime parser is allowed to decode.
3. **Semantic/runtime implementation coverage**, which may remain incomplete and should report a precise
   unsupported-service error containing game/profile, script, opcode, and bytecode offset.

An opcode that is absent even from the selected version ABI should still become a structured decode error,
not a synthetic final instruction or natural script return; that diagnostic hardening remains open.
Supporting a new same-version game now means selecting the complete shared ABI, measuring its corpus
against existing semantics, and implementing only the newly exercised services. It does not require
cloning the VM or manufacturing a new parser table from that game's corpus.

The probe also exposed a separate profile/presentation concern: Kamidori creates a `1024x576` render
target while the current Himegari frontend assumes an `800x600` presentation. Logical canvas geometry,
scaling, and other game-specific defaults therefore belong in the selected game profile or script-driven
surface state rather than in a forked frontend. The source is now known: after its asset directory and
VM-bank metadata, SYS4INI carries a typed per-game startup-settings record. Himegari uses it for the
canvas, text face/raster mode, ADV input/skip policy, save ABI/path, audio initialization, and native
Windows compatibility metadata. The runtime now parses that record once and preserves unknown/profile-
specific data for diagnostics; semantic settings are applied through explicit cross-platform seams while
legacy renderer/registration switches remain classified rather than blindly emulated. The first bounded
application slice is complete: `SCREENX`/`SCREENY` select the validated logical canvas, all presentation
allocations and primary bounds consume it, and it is the default windowed size. Other settings remain on
their existing paths while gameplay is the priority. The second slice is also complete:
independent physical `--window-width`/`--window-height` overrides change only the windowed client while
the logical canvas, VM coordinates, and surface geometry remain fixed. The executable task plan and gates
live in `phase-a-slice-plan.md`. This experiment was
diagnostic only; no Kamidori support or `0x1be` semantics were implemented.

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
Continue step 5 of the **codebase consolidation** maintenance slice by making the Linux core gate genuinely
source-only: classify the installed-corpus/native-oracle engine tests as an explicit workspace suite, inject a
synthetic corpus seam into the opcode-bootstrap unit test, and align the workflow with the target Gitea server's
known-good `ubuntu-latest` action baseline. Then make the user-owned project-license and Kelebek-derived-material
decisions required before wider distribution. Do not infer a license choice or publish/configure a remote.
Concrete playthrough blockers may still preempt this bounded maintenance work; the consolidation effort does
not replace Phase B gameplay validation or the open cross-platform gates.
