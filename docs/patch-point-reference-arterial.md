<!-- DO NOT EDIT -- generated from vm-map/patch-points-arterial.toml by tools/patch_points_build.py --build -->
# Named patch points — arterial (generated)

Stable site names for OMS fragments (`before site <name> { … }`; mod loader spec §5,
`docs/superpowers/specs/2026-11-03-mod-loader-design.md`). Each point lists its site per script revision;
a fragment compiles only against a revision its point lists.

## `arterial.area.after_loaders`

Script: `SETMAPDATA.BIN`

SETMAPDATA's exit. The loaders of the area in play (current_area_id) have run, each layer's in its turn: for each slot from 0 (the base) to 31 ($n$ for append n), MPINIT (the points, area_point), ROINIT (the routes, area_route) and UNINIT (the units, area_unit), each of which clears its arrays before it fills them. After them SETMAPDATA has given area_unit[1], the player's unit, the pictures of map_agent_character_id, or of party_leader_id when no agent is set. ALLMAP runs SETMAPDATA when it sets an area up, before the READY chain; BTL runs it after a battle whose next scene is played on a map (G[0x36ca]), and INIT2 when a save made on a map is loaded. The tables are written afresh each time.

| Revision | Site |
|---|---|
| `sha256:513217fe2b878e32ff17617255303b4c2982b2a2bd91b955a68204469c037d2d` | `0xc1` |

## `arterial.area.after_ready_handlers`

Script: `READYEND.BIN`

READYEND's first instruction. ALLMAP calls READYEND only right after it walks the READY chain, every layer's READY handler (ready_open_script_ids[slot].ready, slot 0 the base READY and slot n $n$READY), and before SETFATE: when it sets an area up (unless G[0x9882] is 2) and in three places of its map loop, so control reaches here more than once in an area. The READY handlers set story cells (scene played cells) from the progress; READYEND sets a few more, then SETFATE counts the open goals.

| Revision | Site |
|---|---|
| `sha256:d206ff4c8da127fcb7d0c07bb82205abe80aebf176e2ada1c34e3e53c39b8bc8` | `0x0` |

## `arterial.battle.after_phases`

Script: `BTL.BIN`

The instruction after BTL's call of PHASECONTROLLER. The battle is decided and nothing of its outcome is applied: battle_result is 1 for a win and 2 for a loss, and G[0x4687aa] is 0x300 or more when the party withdrew or fled. BTL then shows the outcome, runs RESULT on a win (the rewards and the experience, arterial.experience.after_calculation), and offers a loss outside the arena again unless the party fled; after the last try it marks a beaten opponent and plays the battle's scene for its result.

| Revision | Site |
|---|---|
| `sha256:67fe6c6f4b8e2996b854b1acb856451b139b19dc3b16a982eef2d4d3a375ae08` | `0x521` |

## `arterial.battle.before_phases`

Script: `BTL.BIN`

BTL's call of PHASECONTROLLER, which plays the battle out. SETREGULATION has taken the battle's rule (battle_rule_id), SETBTSKILL its skills and DECKBUILD the decks, and the battle's music plays. battle_id is the battle (a DUINIT id) and current_opponent_id the arena opponent (0 outside the arena). battle_result is 0. BTL reaches here at each try: a lost battle that the player fights again (リトライ) comes back through DECKBUILD to here.

| Revision | Site |
|---|---|
| `sha256:67fe6c6f4b8e2996b854b1acb856451b139b19dc3b16a982eef2d4d3a375ae08` | `0x51e` |

## `arterial.boot.data_layered`

Script: `INIT2.BIN`

The instruction after op 0x143 (INIT2@0x17e). Every layer has written its data: the 29 base *INIT scripts, INIT2's slot 0 of each append chain (the base scripts' ids) and its constants, then each mounted append's $n$AUTORUN, which runs its $n$*INIT layers and sets its slot n of each chain. The boot has not used the tables yet. INIT2 goes on to fill each unset text colour setting (G[0x36ed]) from voice_setting_text_colors, to index the faces (face_character_ids and face_slots, from character.face_id), to walk the cards of the six saved decks (G[0x9581] on), to run COUNTSKILL, and, when a save made on an area's map is loaded (G[0x36ca]), to set the area up again (SETMAPDATA, then SETMAPTEX, SETMAPANIME, DRAWMAP and DRAWOBJ). That work reads an edit made here. The tables lie above the saved prefix (0x3fb81): a load zeroes them, the prologue (SYSTEM4) builds them again, and control reaches here again while the load restores.

| Revision | Site |
|---|---|
| `sha256:ffe0c8022233ec9bad8981799fd66003f9f01cbb48ee1a11f95e44a92dbaf778` | `0x17f` |

## `arterial.boot.data_ready`

Script: `INIT2.BIN`

INIT2's exit. Every data table is built: the 29 base *INIT scripts, each mounted append's $n$AUTORUN and its $n$*INIT layers (op 0x143), and INIT2's own work after them, which reads the tables (arterial.boot.data_layered); for a save made on an area's map that work has set the area up again. An edit made here comes after that work. The tables lie above the saved prefix (0x3fb81): a load zeroes them, the prologue (SYSTEM4) builds them again, and control reaches here again while the load restores.

| Revision | Site |
|---|---|
| `sha256:ffe0c8022233ec9bad8981799fd66003f9f01cbb48ee1a11f95e44a92dbaf778` | `0x3a4` |

## `arterial.experience.after_calculation`

Script: `RESULT.BIN`

One party character's award is final and not yet applied: the add of party_member_experience[character_arg_id] and the award. Its third operand (arg[3], a pointer into RESULT's array at L[0xf4bb]) holds the experience the character receives: the battle's award (L[0xf4b1]), half as much again for party_leader_id and party_ultimate_character_id, plus the experience skills' bonus (L[0xf4b2]) and one for each of the battle's cards of the character's rows. RESULT reaches here on a won battle once for each party character whose level is 1 or more and below the cap (9, or 35 once G[0x36d1] is set); a `before site` may rewrite the operand. The level-ups after this instruction read the new experience, and the card experience was given before it.

| Revision | Site |
|---|---|
| `sha256:41748fa07bc1cae951582ce52d419d8a5b3dd9dbe8962b81c3798487662937ec` | `0xb00` |
