# Globals registry (`globals.toml`) + story-flag miner — design

**Date:** 2026-07-07
**Status:** approved (brainstorming) → ready for implementation plan
**Slice:** Phase B story-state layer, static-catalog sub-slice (#1 of the story-state work).

---

## 1. Problem & goal

The VM addresses one flat **global memory bank** by raw offset (`global-int 0x3234`). A subset of
those globals are **story-state flags** — scalar values that ADV scene logic *branches on* (chapter,
Lily forms, choices, UI toggles). Seeding one changes the executed dialogue (`0xa57=1` → SC0000
186→229 lines), so they are the lever behind cross-scene state divergence and the bg/sprite drift.

**Today there is no structured catalog and no curated semantic home for globals.** What exists:

- `build/global-var-map.json/md` — **auto-generated shape inference** (`tools/global_map.py`), from
  access patterns + which `*INIT` script wrote each address. Regenerated wholesale; not hand-editable;
  no curated semantics; no provenance model. **Actively wrong on story flags**: it labels `0xa57`
  (the Lily form-A flag) as `string-table (written by EBINIT)` because it saw `EBINIT` touch the
  address. Shape inference cannot recover branch-flag meaning — only curation can.
- Scattered prose mentions of the few known flags in `docs/name-resolution.md`, `phase-a-slice-plan.md`,
  the Phase B spec, and the status memory. No table, no single source.

**Goal.** Build the missing sibling to `vm-map/opcodes.toml`: a **hand-curated, single-source semantic
registry for globals**, generated into machine + human views and wired into `sys4load` labeling.
Story-flags are its first fully-populated category, discovered by a separate **static** miner that
proposes skeleton entries for human naming.

**Non-goals (this slice).**
- No dynamic validation in the catalog build. The existing `Age.Cli sweep 0xADDR=VAL` reach-explorer
  stays a **separate, on-demand** tool for confirming a flag moves dialogue. (Seed-sweeping every
  candidate × 297 scenes × 2 runs is far too expensive to bake into a build.)
- No SCJUMP decode and no `call-script` id resolution (both remain deferred, tracked in
  `name-resolution.md §1`). SCJUMP is only referenced as a *writer source* when hand-curating.
- No Frida / runtime observation (that layer is shelved; see `docs/global-memory-re.md`).
- Not attempting to hand-name all 16k auto-labelled globals — curation is incremental, hottest first.

---

## 2. Architecture — three pieces

| Piece | Role | Models after |
|---|---|---|
| `vm-map/globals.toml` | hand-edited **source of truth** for curated globals | `vm-map/opcodes.toml` |
| `tools/globals_build.py` | merge curated-over-auto → generate JSON/MD + lint | `tools/opcodes_build.py` |
| `tools/story_flags.py` | **static** miner: discover story-flag candidates + evidence + `--bootstrap` | `tools/opcode_context.py` + opcodes `--bootstrap` |

Data flow:

```
tools/global_map.py ──► build/global-var-map.json      (auto shape inference; existing, unchanged)
                                    │
vm-map/globals.toml ─────┐          │
   (curated source)      ├─ merge (curated wins) ─► tools/globals_build.py --build
tools/story_flags.py ────┘                                   │
   (--bootstrap seeds skeletons                               ├─► build/globals.json      (machine, merged, provenance-tagged)
    back into globals.toml)                                   └─► docs/global-reference.md (human, GENERATED)
                                                                        │
                                                       tools/sys4load.py reads build/globals.json for operand labels
```

---

## 3. `vm-map/globals.toml` schema

Array-of-tables, one `[[global]]` per **known/curated** address. Mirrors opcodes.toml's provenance
fields (`source`/`confidence`/`depends_on`).

```toml
# generated-consumers: build/globals.json, docs/global-reference.md  (never hand-edit those)

[[global]]
address = "0x3234"            # canonical key; hex string, lowercase 0x
name = "chapter_mode"         # snake_case identifier used in labels
category = "story-flag"       # see vocabulary below
type = "int"                  # int | string | float
value_domain = "1..9"         # free text; e.g. "{0,1}", "1..9", "resId"
usage = "Progression chapter/mode selector; SCJUMP's top-level switch keys on it."
source = "investigation"      # investigation | inference | harness | auto-shape
confidence = "high"           # high | med | low
depends_on = ["0x62ccf"]      # optional: related addresses (choice outputs, sibling forms)
```

**`category` vocabulary:** `story-flag`, `index-pointer`, `data-table`, `string-table`, `ui-toggle`,
`choice-output`, `counter`, `unknown`. (Extensible; validated by lint against this list.)

**`source` semantics:** `investigation` = confirmed by us (e.g. seed→divergence); `harness` =
confirmed by VM/test; `inference` = static reasoning, unconfirmed; `auto-shape` = machine-proposed
skeleton from the miner / shape map, awaiting human confirmation.

**Confidence ceiling (lint):** an `auto-shape` source may not claim `high` confidence.

---

## 4. `tools/story_flags.py` — the static miner

Purely static; walks the 481-script disassembly via `sys4load` (reuses the loaded `Script` +
opcode table; no re-decode). No sweep, no runtime.

### Candidate detection
A global address is a **story-flag candidate** when all hold:
1. **feeds a branch** — it is an operand of a comparison/logical opcode (`eq`/`ne`/`lt`/`gt`/`and`/`or`,
   per the opcode table's classification) or a direct operand of `jcc` (0xa0);
2. **scalar** — read/written as a plain global operand (argtype 3), *not* as a 2D `base[i*stride+col]`
   access (those are data-tables);
3. **not already a structural global** — excluded if `global_map.py`'s output classifies it as an
   index-pointer, 2D-table base, or string-table base (dedupe against the auto map).

### Evidence gathered per candidate
- **compared-against constants** — the immediate operands (argtype 0) it is compared with → infers
  `value_domain` (`{0,1}` boolean flag vs `1..n` enum like `0x3234`);
- **writer set** — which scripts `mov` (0x55) / assign the address as an lvalue. Classification signal:
  *written by progression/menu scripts (SCJUMP, SAVE, menu) but only read in SC/SP scenes* = externally
  set → strong `story-flag`; *written and read within the same scene* = local `choice-output`/scratch;
- **reach** — count of distinct SC/SP scenes with a branch-read → breadth of influence;
- **chrome-vs-scene-unique** — whether all read sites are inside the shared ADV-chrome subroutine
  (the byte-identical block carrying `0x6c9–0x6cd` toggles and `0x90` hotspots) or scene-specific.

From this it auto-assigns a **sub-category** (`story-flag` / `ui-toggle` / `choice-output`) and a
**confidence** (never `high`; `auto-shape` source).

### Outputs
- `build/story-flags-candidates.json` — always: ranked candidates with full evidence (read sites,
  writers, compared constants, reach, category, confidence). This is the review surface.
- `--bootstrap` — appends skeleton `[[global]]` entries **only for addresses not already in
  `globals.toml`**, `source = "auto-shape"`, low confidence, `usage` left as a `TODO:` stub for the
  human. Idempotent; never overwrites a curated entry. (Mirrors `opcodes_build.py --bootstrap`.)

---

## 5. `tools/globals_build.py` — merge & generate

- `--build` — merge, then write generated files:
  - **merge rule:** per address, a curated `globals.toml` entry wins; otherwise fall back to the
    `build/global-var-map.json` auto entry. Output tags each with provenance (`curated` vs `auto`).
  - `build/globals.json` — merged machine view (address → {name, category, type, value_domain, usage,
    source, confidence, provenance}). Consumed by `sys4load` and, later, the C# VM.
  - `docs/global-reference.md` — generated human view (header stamped "generated — do not edit"),
    grouped by category, curated entries first. Parallels `docs/opcode-reference.md`.
- `--lint` — dangling `depends_on` (address not present in either source), unknown `category`,
  confidence-ceiling violations (`auto-shape` claiming `high`), duplicate addresses. Mirrors
  opcodes lint.

Imports `tools/paths.py` for all paths (no hard-coding).

---

## 6. `sys4load` wiring

`sys4load` currently annotates global operands from `build/global-var-map.json`. Switch its label
source to the merged **`build/globals.json`** (curated names/categories win; auto shape labels remain
as fallback for the long tail). Degrades gracefully if the file is absent (same as today). Result:
curated globals render by name — e.g. `global-int 0x3234 =chapter_mode(story-flag)`,
`global-int 0xa57 =lily_form_a(story-flag)` — instead of the wrong auto shape label.

Regenerate the `.asm` corpus (`tools/extract_phase2.py`) to pick up new labels, same as the existing
global-map flow.

---

## 7. Docs & canonical-map

- **Extend `docs/name-resolution.md §2`** (already the "global-variable map" home): describe the
  curated `globals.toml` registry + `globals_build.py` build step + the merge-over-auto model, and add
  a **"Story-state flags"** subsection (what they are, the miner, how to curate). The existing auto-map
  narrative stays as the "auto shape inference" layer feeding the merge.
- **`docs/global-reference.md`** — new, but **generated** (owned by `globals_build.py`), so it is not a
  hand-authored doc; it is the human view of the registry.
- **CLAUDE.md canonical-documents map** — add a row:
  `Global variable semantics / addresses → vm-map/globals.toml` (source), paralleling the opcode row.
- **CLAUDE.md single-source-of-truth table** — add `vm-map/globals.toml` → `globals_build.py --build`
  → `build/globals.json`, `docs/global-reference.md` (generated; do not edit).
- **`docs/tools-reference.md`** — add `story_flags.py` and `globals_build.py` (purpose/usage/I-O).

---

## 8. First-pass registry content

Hand-author the flags we already know into `globals.toml` (so the registry is useful on day one and
the miner has regression anchors):

- `0x3234` `chapter_mode` (story-flag, `1..9`, SCJUMP switch) — high, investigation.
- `0xa57` / `0xa58` / `0xa59` `lily_form_a` / `_b` / `_c` (story-flag, `{0,1}`; exactly one set =
  current form; gates form-specific voiced dialogue) — high, investigation; `depends_on` each other.
- `0x6c9`–`0x6cd` `ui_toggle_*` (ui-toggle, `{0,1}`; the 5 ADV-chrome buttons) — med, investigation.
- `0x0`, `0x62ccf` choice outputs (choice-output) — med/inference, from SCJUMP recon.
- High-purity index pointers `0xeff75`, `0x152616` (index-pointer) — med, from the auto map's purity
  ranking (carries them over so the merged view is complete for the known-structural ones too).

Then run `story_flags.py --bootstrap` to propose the rest for review.

---

## 9. Testing — `tools/test_globals.py`

- **schema/lint:** `globals.toml` parses; `--lint` passes (no dangling `depends_on`, valid categories,
  no confidence-ceiling violations).
- **merge precedence:** curated `0xa57` in the merged `build/globals.json` reads `lily_form_a
  (story-flag)`, overriding the auto map's `string-table` label.
- **miner regression anchors:** running `story_flags.py` over the corpus surfaces the *known* flags
  `0x3234` and `0xa57` as story-flag candidates with nonzero reach (guards the detection heuristic).
- **generated == source:** `build/globals.json` reflects `globals.toml` (round-trip on a curated
  entry's fields).
- **loader intact:** `sys4load <corpus> --validate` still 481/481 clean with the new label source.

---

## 10. Implementation order (for the plan)

1. `globals.toml` skeleton + schema + hand-authored first-pass content (§8).
2. `globals_build.py` (`--build` merge + generate, `--lint`) + `build/globals.json` +
   `docs/global-reference.md`.
3. Wire `sys4load` to `build/globals.json`; regenerate a sample `.asm`; verify 481/481.
4. `story_flags.py` (static miner → candidates JSON; `--bootstrap`).
5. `test_globals.py`; run miner, review candidates, curate the strong ones into `globals.toml`.
6. Docs: `name-resolution.md §2`, CLAUDE.md two tables, `tools-reference.md`. Update status memory.
