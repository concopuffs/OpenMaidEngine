<!-- DO NOT EDIT -- generated from vm-map/patch-points-himegari.toml by tools/patch_points_build.py --build -->
# Named patch points — himegari (generated)

Stable site names for OMS fragments (`before site <name> { … }`; mod loader spec §5,
`docs/superpowers/specs/2026-11-03-mod-loader-design.md`). Each point lists its site per script revision;
a fragment compiles only against a revision its point lists.

## `himegari.boot.data_ready`

Script: `INIT2.BIN`

INIT2's exit, once per run at boot. Every data table is built: the base *INIT scripts, each mounted append's $n$AUTORUN and its $n$*INIT layers (op 0x143), and TUNE's derived item data after them. The tables lie above the saved prefix (0x6241b), so no load replaces them: an edit made here lasts the whole run.

| Revision | Site |
|---|---|
| `sha256:00ec969c7f369f28943b0c7272c7a0b58bf145e5993e5d39e62a3d80498407fa` | `0x183` |

## `himegari.experience.after_calculation`

Script: `ADDEXP.BIN`

The award is final and not yet applied. L[0x1] holds the experience the unit receives: G[0x62ccb] after the G[0x3302] multiplier bits (0x10 zero, 0x20 double, 0x40 tenfold). current_unit_id is the canonical unit. All three multiplier paths jump here, so `before site` sees every award and may rewrite L[0x1].

| Revision | Site |
|---|---|
| `sha256:b4f38c43f2c500012be48517ce773d3c9e81c12deeb4d712cab4b99749661e1c` | `0x9a` |

## `himegari.stage.after_close_handlers_lost`

Script: `FIELD.BIN`

The stage is lost (stage_result 2) and every layer's close handler has run (column 1: CLOSE, then $1$CLOSE).

| Revision | Site |
|---|---|
| `sha256:8e29795883e1abcf723425324f2f1db58c22607a691977fa6dc037dbbba4c103` | `0xbabd` |

## `himegari.stage.after_close_handlers_won`

Script: `FIELD.BIN`

The stage is won (stage_result 1) and every layer's close handler has run (column 1: CLOSE, then $1$CLOSE); STAGECLEAR runs next.

| Revision | Site |
|---|---|
| `sha256:8e29795883e1abcf723425324f2f1db58c22607a691977fa6dc037dbbba4c103` | `0xbaa1` |

## `himegari.stage.after_loader`

Script: `FIELD.BIN`

The stage's loader has run (stage_loader_script_ids[current_stage_id]: STINIT, or an append's $1$STINIT) and FIELD has placed nothing yet: the 30 stage_enemy_* slots, the objects and the rules hold the stage as authored, and FIELD reads them next to place the enemies (slots 1..29, through SETEN). FIELD skips the loader, and this point, when G[0x767d] is 1.

| Revision | Site |
|---|---|
| `sha256:8e29795883e1abcf723425324f2f1db58c22607a691977fa6dc037dbbba4c103` | `0x20a` |

## `himegari.stage.after_ready_handlers`

Script: `FIELD.BIN`

The enemies are placed and every layer's ready handler has run (column 0 of stage_event_handler_script_ids: READY, then $1$READY). current_stage_id is the stage.

| Revision | Site |
|---|---|
| `sha256:8e29795883e1abcf723425324f2f1db58c22607a691977fa6dc037dbbba4c103` | `0xa27` |

## `himegari.stage.after_round_handlers`

Script: `FIELD.BIN`

The start of a player turn after the first (current_stage_turn > 1, current_faction_id 1): every layer's round handler has run (column 2: ROUND, then $1$ROUND).

| Revision | Site |
|---|---|
| `sha256:8e29795883e1abcf723425324f2f1db58c22607a691977fa6dc037dbbba4c103` | `0x764a` |
