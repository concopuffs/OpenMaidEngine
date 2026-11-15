<!-- DO NOT EDIT -- generated from vm-map/patch-points-kamidori.toml by tools/patch_points_build.py --build -->
# Named patch points — kamidori (generated)

Stable site names for OMS fragments (`before site <name> { … }`; mod loader spec §5,
`docs/superpowers/specs/2026-11-03-mod-loader-design.md`). Each point lists its site per script revision;
a fragment compiles only against a revision its point lists.

## `kamidori.boot.data_ready`

Script: `INIT2.BIN`

INIT2's exit, once per run at boot. Every data table is built: the 28 base *INIT scripts, each mounted append's $n$AUTORUN and its $n$*INIT layers (op 0x143), and INIT2's own work after them. The tables lie above the saved prefix, so no load replaces them: an edit made here lasts the whole run.

| Revision | Site |
|---|---|
| `sha256:5e4932184f30e6d35d9ee9b259f2987bd62e443479fa2d207fde2edfbac5a65d` | `0x2cf` |
| `sha256:a60b27687b52d51c06ecfb1371e3aea7393b45a21612bb3715992ef2970925f8` | `0x2cf` |

## `kamidori.experience.after_calculation`

Script: `ADDEXP.BIN`

The award is final and not yet applied. L[0x1] holds the experience the character receives: G[0xfec0c], the amount the caller passes (COMMITBTL after a battle, FIELD), times the costume bonus (100, plus 25 for each of the four item_costume_gem_sockets of character_costume_item_id[current_character_id] that holds 0x118), over 100. current_unit_id is the unit and current_character_id its canonical character, whose character_levels, character_experience and unit_level_cap the award reads. Every path reaches this instruction, so `before site` sees every award and may rewrite L[0x1]; an amount of 0 or less, or a character at its level cap, gains no experience. Weapon experience (G[0xfec0d] and the equipped item) is decided after this point and does not read L[0x1].

| Revision | Site |
|---|---|
| `sha256:606b83e952a3ce45ee406a2383710734f1ea707288f5751b5614be50e3edd7b6` | `0x88` |
| `sha256:23bb6ac1e1aa83ef248bbacba4cadd71801fa07188aed3938c4768034f52b40b` | `0x88` |

## `kamidori.stage.after_close_handlers`

Script: `FIELD.BIN`

The floor ends and every layer's close handler has run (stage_close_handler_script_ids), after a won floor's rewards. stage_result says how it ended.

| Revision | Site |
|---|---|
| `sha256:e350246cc923622b92342a9377a75f597623c09c2b78b423d90fe067cde35f21` | `0xc785` |
| `sha256:0c93c6815a7b6903564000ff470d4c5854b5baac4c994cddcda81899e25cbfd4` | `0xc785` |

## `kamidori.stage.after_loader`

Script: `FIELD.BIN`

Every layer's map and stage loader has run (mpinit_layer_script_ids and stage_loader_script_ids: MPINIT and STINIT, then each append's) and FIELD has placed nothing yet: the floor's enemy slots, objects and rules hold the stage as authored. current_stage_id is the stage.

| Revision | Site |
|---|---|
| `sha256:e350246cc923622b92342a9377a75f597623c09c2b78b423d90fe067cde35f21` | `0x527` |
| `sha256:0c93c6815a7b6903564000ff470d4c5854b5baac4c994cddcda81899e25cbfd4` | `0x527` |

## `kamidori.stage.after_ready_handlers`

Script: `FIELD.BIN`

The floor's enemies are placed and every layer's ready handler has run (stage_ready_handler_script_ids: STAGEREADY, then each append's).

| Revision | Site |
|---|---|
| `sha256:e350246cc923622b92342a9377a75f597623c09c2b78b423d90fe067cde35f21` | `0xd58` |
| `sha256:0c93c6815a7b6903564000ff470d4c5854b5baac4c994cddcda81899e25cbfd4` | `0xd58` |

## `kamidori.stage.after_round_handlers`

Script: `FIELD.BIN`

The start of a player turn after the first (current_stage_turn > 1): every layer's round handler has run (stage_round_handler_script_ids).

| Revision | Site |
|---|---|
| `sha256:e350246cc923622b92342a9377a75f597623c09c2b78b423d90fe067cde35f21` | `0x933d` |
| `sha256:0c93c6815a7b6903564000ff470d4c5854b5baac4c994cddcda81899e25cbfd4` | `0x933d` |
