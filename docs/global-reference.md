<!-- DO NOT EDIT -- generated from vm-map/globals.toml by tools/globals_build.py --build -->
# Global Variable Reference (generated)

5030 globals (189 curated, 4841 auto shape-inferred). Source of truth: `vm-map/globals.toml`.

## choice-output

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x0` | system_flow_request | high | investigation | Return-mode request shared by TITLE/GAMESTART and SYSTEM4. The natural New Game path writes 1 immediately after GAMESTART's TUNE call; TITLE returns and SYSTEM4 routes value 1 into its ADV scene loop. Values 2/3/5 route to FORT/FIELD/CAMP; other writers use the same system-level request channel. |
| `0x699` | next_script_resource_id | high | investigation | SYSTEM4's computed child-script resource id. On the normal ADV path SYSTEM4 copies G[0x87a57][scjump_decision_out] here, substitutes raw id 0x22 (SC0000.BIN) when zero, executes call-script through this cell at offset 0x477, then clears it after the child returns. |
| `0xa68` | — | med | auto-shape | TODO: confirm. Branch-read in 11 scenes / 12 scripts; compared against [0, 1]; writers=['SC0740.BIN', 'SC1580.BIN', 'SC1590.BIN']. |
| `0x62ccc` | scjump_decision_out2 | low | inference | Adjacent to scjump_decision_out (0x62ccf) in the 0x62ccc-0x62ccf progression decision-output cluster; same 136-scene reach, written by CAMP/CLOSE/DEBUGADV. INFERENCE from adjacency — confirm meaning before relying on it. |
| `0x62ccf` | scjump_decision_out | low | inference | One of SCJUMP's output/decision globals (progression state machine writes it). Related to chapter_mode. |

## counter

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x2052e` | scjump_progress_b | med | inference | Second SCJUMP switch input (1223 comparison reads) — progression counter/position. INFERENCE from SCJUMP usage. |
| `0x4dfbc` | scjump_progress_a | med | inference | Dominant SCJUMP switch input (1609 comparison reads) — a per-chapter story-progress counter/position the progression machine branches on. INFERENCE from SCJUMP usage; confirm exact meaning via a listing/playthrough. |
| `0xe6c5d` | scjump_progress_d | med | inference | SCJUMP switch input (168 comparison reads) — progression counter/position. INFERENCE from SCJUMP usage. |
| `0x152618` | scjump_progress_c | med | inference | Third SCJUMP switch input (530 comparison reads) — progression counter/position. INFERENCE from SCJUMP usage. |

## data-table

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x6d3` | story_event_flags | high | investigation | Shared one-based story/event flag bank. Progression, recruitment, item, and stage logic read or write individual cells; STINIT object prerequisite ids are decremented by one before SETOBJ tests this array. |
| `0x2e49` | character_voice_suppressed | high | investigation | Base of the per-character voice enable/suppress settings. INITCONFIG zero-fills all 13 cells and registers each with the shared profile service; LOADCONFIG restores them. CONFIG indexes the table to preview a character voice and write 0/1. ROOM reads cell 0 before assigning its selected greeter's greeting/farewell voice ids, so the port's former scalar interpretation of zero-int-range (writing 13 into the base cell) suppressed those voices on every natural boot. This names the script-visible setting array without choosing a persistence backend for op 0x1a2/0x1a3. |
| `0x65ce` | skill_acquired_flags | high | investigation | Persistent acquired-skill flags. ADDSKILL sets the selected skill after resolving the unit's equipped-skill slots; FORT checks the flag before granting a skill; CHMENU combines it with skill_change_catalog_eligible to build the available skill-change catalog. |
| `0x673c` | party_slot_flags | high | investigation | Per-party-slot state flags for slots 0..99. UNITECH creates the initial unit by setting slot 2 to 0x13; CALCARR counts slots whose flags intersect 0x6, and CHMENU includes slots with bit 1 set. Exact meanings of the remaining bits are not yet classified. |
| `0x67a0` | party_slot_character_id | high | investigation | Character/unit definition id stored for each party slot. UNITECH writes character id 2 into initial slot 2 on a natural New Game; CHMENU reads this table for every active party_slot_flags entry when constructing its roster. |
| `0x7684` | stage_clear_state | high | investigation | Persistent per-stage completion state indexed by stage id. STAGECLEAR sets the current stage cell to 1, while FORT, SELSTAGE, and FIELD use zero versus one to distinguish an uncleared mission from a cleared replay. |
| `0x141a4` | stage_object_runtime_state | high | investigation | Per-stage, per-object-slot runtime state. FIELD initializes mode-1 objects from STINIT's first tagged payload on a fresh stage and updates capturable-object ownership; DRAWOBJ uses it as the vertical sprite-row index. Other consumers interpret it by object type. |
| `0x5660b` | skill_info_revealed_flags | high | investigation | Persistent skill-information visibility flags. ADDSKILL sets the selected skill, BTL marks every equipped skill when it is observed in combat, and INFOIT suppresses a skill's icon/handler-driven details until this flag is nonzero. This is broader than skill_acquired_flags. |
| `0x66716` | unit_voice_asset_ids | high | investigation | EBINIT per-unit voice bank for 116 voiced characters and variants. Resolving the values through SYS4INI yields character OGG clips (for example Lily's row is LILA1381..1406); BTL and FIELD select mostly unresolved battle/map reaction slots, while SHOWGROW proves column 24 is the level-up reaction. Columns: 24=level_up. |
| `0x6dc46` | unit_status_art_asset_ids | high | investigation | Three-variant status/menu illustration table for 25 principal characters. The ids resolve to 456x420 CS character art; DRAWCHP selects column 0 above 50% HP, column 1 at 26..50%, and column 2 at 25% or below, then loads the art into the status-panel texture slot. Columns: 0=healthy, 1=wounded, 2=critical. |
| `0x6e7fe` | unit_map_sprite_asset_ids | high | investigation | Five-context unit sprite-sheet table for 251 units. Even columns 0/2/4 are compact CP*AA-style presentations for normal, alternate-condition, and special-condition states; columns 1/3 are the matching normal/alternate full directional CP*AB sheets used by FIELD. INFOCH/INFOEN and DRAWENP reuse the compact representation. Columns: 0=normal_compact, 1=normal_directional, 2=alternate_compact, 3=alternate_directional, 4=special_compact. |
| `0x6fb86` | unit_battle_sprite_asset_id | high | investigation | Battle figure for 248 units. Values resolve to transparent full-body CB character/monster art; BTL loads the selected combatant's figure and INFOEN uses the same asset for the enemy detail view. |
| `0x6ff6e` | unit_battle_portrait_asset_ids | high | investigation | Paired battle portrait assets: column 0 is the larger CA*A bust where available, and column 1 is the compact CA*D face portrait. BTL, DRAWENP, and EVOLVE select these assets for combat and unit-growth presentation. Columns: 0=large_bust, 1=compact_face. |
| `0x7073e` | unit_battle_cutin_asset_ids | high | investigation | Paired 800-pixel-wide battle cut-ins for 64 named characters. The values resolve to CIC cropped/action and CIN full-frame illustrations; BTL is the sole consumer. Columns: 0=cropped_action, 1=full_frame. |
| `0x70f0e` | unit_sort_key | high | investigation | EBINIT ordering key populated for 248 units. CHMENU, EXILE, SALLY, INFOEN, and SUMMON pass this array to op 0x12f's stable index sort or combine it with a unit id to build menu ordering keys, mirroring item_sort_key and skill_sort_key. |
| `0x712f6` | unit_enemy_info_listed | high | investigation | EBINIT eligibility flag for 183 enemy-information entries. INFOEN stable-sorts all unit ids, retains only rows whose value is nonzero, and then gates each retained entry's revealed state through the encounter/profile table. |
| `0x716de` | unit_icon_id | high | investigation | EBINIT icon selector populated for all 277 units. READICON returns it for unit ids; CHMENU, DRAWCHP, EXILE, SALLY, and SUMMON convert the value into icon-atlas page and cell coordinates. |
| `0x71ac6` | unit_species_category | med | investigation | Broad EBINIT species/allegiance category for 198 units: value 1 covers humans and allied heroines, 2 covers demons including Emilio and Lily as well as many demonic monsters, and 3 covers other monsters, dragons, and elementals. CHMENU uses it for equipment restrictions, CALCDMG for category-sensitive effects, and INFOCH/INFOEN for display. |
| `0x71eae` | unit_sex_category | high | investigation | EBINIT sex category for 251 units. Named character records establish the three values; CHMENU tests it against item_sex_restriction_mask, while INFOCH and INFOEN use the same category in unit presentation. |
| `0x72296` | unit_boss_class | med | investigation | Nonzero only for 112 bosses, special encounters, and map hazards. CALCDMG grants the boss adjustment, CALCILL uses it for condition immunity, and FIELD/MAGIC/USEMAGIC use it in special-unit targeting rules. Magnitude tracks increasing boss class; the distinction between positive and negative classes remains unresolved. |
| `0x7267e` | unit_canonical_id | high | investigation | Maps 106 alternate, boss, brainwashed, EX, and enemy variants back to their canonical character or recruitable archetype. For example all Estelle bosses map to 12, brainwashed Sylphine maps to 5, and ordinary/EX orcs map to recruitable orc 52. Setup, menus, inventory, dismissal, and combat normalize through this id. |
| `0x72a66` | unit_capturable | high | investigation | Capture eligibility for 74 ordinary enemy archetypes. SELACT and FIELD allow the capture skill only when this flag is nonzero and the acting unit's capture power meets the target-level check; successful resolution displays the explicit 'Captured the enemy' message. |
| `0x72e4e` | unit_roster_state_flag_base_index | high | investigation | Persistent roster-state block for seven recruitable heroines. SALLY's first three unit actions mark offsets 1, 2, and 0 respectively; UNITECH marks the offset selected by its recruitment mode; DELCH, REMOVECH, SALLY, and EXILE mark offset 3 when the unit is removed or released. The values are relative indices into the shared flag bank, not absolute global addresses. |
| `0x73236` | unit_sally_action_unlock_requirements | high | investigation | Availability requirements for SALLY's contract, brainwash, reserved, and sex-magic action slots. The SO012 button atlas and selection dispatch prove that UI choices 1, 2, and 3 select columns 0, 1, and 3; column 2 has a switch case but is deliberately skipped by both drawing and input, so it is reserved/unreachable in the shipped UI. While constructing the action mask, SALLY keeps a positive entry only when flag_bank[value - 1] equals 1, keeps -1 as unconditional, and clears an action whose entry is zero. Deploy, sacrifice, and release use separate eligibility logic. Columns: 0=contract, 1=brainwash, 2=reserved_action, 3=sex_magic. |
| `0x741d6` | unit_sally_event_ids | high | investigation | SALLY event ids for contract, brainwash, the unreachable reserved action, Lily's child/girl/adult sex-magic forms, sacrifice, and release. The action switch selects columns 0, 1, 6, and 7 directly; sex magic selects the current Lily unit id plus one, yielding columns 3..5 for Lily unit ids 2..4. A populated reachable slot is copied to scjump_decision_out and translated through the SCJUMP resource table at 0x87a57 into next_script_resource_id. Column 2 is paired with the UI-skipped reserved action and its shipped ids 1300..1306 have no SCJUMP resource mapping. EXILE also tests column 7 when deciding whether a unit can be released. Columns: 0=contract, 1=brainwash, 2=reserved_action, 3=sex_magic_lily_child, 4=sex_magic_lily_girl, 5=sex_magic_lily_adult, 6=sacrifice, 7=release. |
| `0x76116` | unit_roster_variant_ids | high | investigation | Roster-form mapping for 36 recruitable units. Column 0 maps the summonable archetypes and principal heroines to their normal unit id; column 1 maps seven heroines to EBINIT ids 21..27, whose names explicitly identify their brainwashed states. SALLY replaces the selected party unit with the action-selected form, and UNITECH recruitment modes 3/4 select the same two columns. Columns: 0=normal, 1=brainwashed. |
| `0x768e6` | unit_sex_magic_bonus_item_id | high | investigation | Item awarded by SALLY after the three form-dependent intimate-event actions for 22 eligible units. SALLY passes the value directly to USEITEM and then displays the selected character name followed by the literal message 'sex magic bonus' and the resolved item description. Principal heroines yield planetary stones; the remaining rows resolve to recovery items and rare materials. |
| `0x76cce` | unit_essence_yield | high | investigation | Essence returned by SALLY's unit-conversion/release actions for 187 units. The normal path adds this value to current essence and the special path adds five times it, clamps to the essence cap, then applies the corresponding alignment adjustment; it is distinct from unit_deployment_cost_base. |
| `0x770b6` | unit_summon_unlock_flag_index | high | investigation | Summoning-stone/unlock flag index for the 29 recruitable monster archetypes. SUMMON and SALLY subtract one and test the corresponding cell in the shared flag bank at 0x6d3 before listing or advancing the unit; GAMESTART uses presence of this mapping when restoring party slots. |
| `0x7749e` | unit_summon_knowledge_threshold | high | investigation | Per-archetype familiarity threshold for 19 summonable units. SUMMON divides accumulated progress by it for the displayed percentage and treats zero-threshold units as limited to one copy; SALLY awards the corresponding summoning item and 'deepened understanding' message when accumulated progress reaches the threshold. |
| `0x77886` | unit_summon_point_cost | high | investigation | Point cost for the 29 summonable unit archetypes. SUMMON compares it with the current point pool, emits 'Insufficient points' on failure, subtracts it on success, and displays the same number in each catalog row. |
| `0x77c6e` | unit_defense_element | high | investigation | EBINIT defensive affinity for 250 units. DRAWENP renders it through the same twelve-value defense-attribute vocabulary as item_defense_element; CALCBTPARAM, BTRTN, and route scripts consume it during battle resolution. |
| `0x78056` | unit_default_attack_item_id | high | investigation | EBINIT default attack for 250 units. Every populated value cross-resolves to ITINIT's innate-attack records; SELACT, BTL, CALCBTPARAM, CALCDMG, and CHMENU use it when no equipped weapon overrides the unit's natural attack. |
| `0x78826` | unit_weapon_item_category | high | investigation | EBINIT weapon/equipment-family restriction for 179 units. CHMENU compares an item's item_category directly with this value when deciding whether the selected unit can equip it; Lily's forms store -1 for unrestricted handling. |
| `0x78c0e` | unit_starting_equipment_item_id | high | investigation | Fixed equipment for 22 named/boss unit records. Values cross-resolve to weapons, shields, armor, and accessories in ITINIT. SETEN equips the item instead of unit_default_attack_item_id, UNITECH grants it on first recruitment, and INFOEN derives the displayed defense element from it when applicable. |
| `0x78ff6` | unit_starting_skill_ids | high | investigation | EBINIT four-slot starting skill record. The shipped table populates the first three columns. SETEN, UNITECH, and SALLY copy the row into each runtime unit's skill list; GAMESTART, FORT, FIELD, and DRAWENP inspect the same ids, all of which cross-resolve to SKINIT. Columns: 0=skill_slot_1, 1=skill_slot_2, 2=skill_slot_3, 3=skill_slot_4. |
| `0x79f96` | unit_deployment_cost_base | high | investigation | Base deployment/command cost for 204 units. SETCH, SETEN, and ADDEXP copy it plus the per-unit adjustment at 0x57357 into runtime cost 0x5f0bb; SALLY checks the prospective total against party capacity, while FIELD/READY add it to and REMOVECH subtracts it from the deployed-cost aggregate. |
| `0x7a37e` | unit_starting_level | high | investigation | EBINIT initial level for 242 units. Lily's three forms store levels 1, 20, and 40. SETEN and SALLY copy or compare it while constructing a runtime unit, and UNITECH uses it when synchronizing form and character state. |
| `0x7a766` | unit_level_cap | high | investigation | EBINIT maximum level for 243 units. ADDEXP permits level growth only while the runtime level is below this field, and SETEN uses it as the default upper bound when no scenario-specific enemy cap is supplied. |
| `0x7ab4e` | unit_auto_level_scale_divisor | high | investigation | Default level-scaling divisor for 192 enemy definitions. SETEN uses a scenario override when supplied, otherwise divides the difference between scenario level and unit_starting_level by this value, adds the result to the runtime level, and clamps it to unit_level_cap. |
| `0x7af36` | unit_base_stats | high | investigation | EBINIT fourteen-column base-stat record copied wholesale into each runtime unit by SETEN, UNITECH, and SALLY. Its layout matches item_stat_modifiers: columns 2 physical attack, 3 physical defense, 4 magic attack, 5 magic defense, 6 speed, 7 luck, 10 movement, 11 max HP, 12 max SP, and 13 max FS; accuracy/evasion and other unpopulated columns begin at zero. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power, 10=movement, 11=max_hp, 12=max_sp, 13=max_fs. |
| `0x7e5e6` | unit_stat_growth_rates | high | investigation | EBINIT per-level growth record with the same fourteen-column layout as unit_base_stats. ADDEXP adds each rate to a fractional accumulator, divides by 100 to award whole stat points, and retains the remainder; SETEN and UNITECH apply the same rates when materializing units above their starting level. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power, 10=movement, 11=max_hp, 12=max_sp, 13=max_fs. |
| `0x83406` | unit_experience_reward | high | investigation | Base experience award for 164 enemy records. BTL scales this value by the victor/defeated level difference and difficulty mode, stores the result in the ADDEXP input channel, and invokes ADDEXP for the winning unit. |
| `0x837ee` | unit_drop_item_ids | high | investigation | Enemy drop table. Values cross-resolve to ITINIT items (for example treasure puttetto drops bronze/silver/gold coins and planet stones); BTL rolls and awards the rows, while INFOEN displays the possible drops. Columns: 0=drop_1_item_id, 1=drop_2_item_id, 2=drop_3_item_id, 3=drop_4_item_id, 4=drop_5_item_id, 5=drop_6_item_id, 6=drop_7_item_id, 7=drop_8_item_id. |
| `0x8572e` | unit_drop_chance_percent | high | investigation | Per-slot drop chance paired with unit_drop_item_ids. BTL draws random-modulo 100 for each populated slot and awards the item when the result is below this value; only columns 0..4 are populated in shipped EBINIT. Columns: 0=drop_1_percent, 1=drop_2_percent, 2=drop_3_percent, 3=drop_4_percent, 4=drop_5_percent, 5=drop_6_percent, 6=drop_7_percent, 7=drop_8_percent. |
| `0x8766e` | unit_large_battle_sprite | high | investigation | Presentation flag for 33 large demons, dragons, gods, and their variants. BTL and INFOEN use it to anchor the CB battle figure at the lower screen edge and omit the ordinary-unit framing treatment required by smaller sprites. |
| `0x8c879` | item_sort_key | high | investigation | ITINIT field for all 287 populated item ids. CHMENU, IMPROVE, and INFOIT pass this array as the primary key to op 0x12f's stable index sort, establishing it as the catalog/display ordering key. The runtime lookup base is one cell before ITINIT's first write because item ids are one-based. |
| `0x8cc61` | item_random_tier | high | investigation | ITINIT field for all 287 items. ADDRANDOMITEM and LOSTRANDOMITEM bucket eligible item ids by this value before choosing a random gain/loss; IMPROVE compares it with the current progression rank. This is the random-item availability/rarity tier, distinct from item_category. |
| `0x8d049` | item_category | high | investigation | ITINIT field for all 287 items and ITMES's top-level behavior dispatch. Observed groups: 0 innate attacks, 1 key/story items, 2 consumables, 4 stat stones, 8 synthesis materials, 9 coins, 10..17 weapon families, 19 boots, 20 armor, 21 shields, 22 accessories, and 23 capture ropes. |
| `0x8d431` | item_min_range | high | investigation | Populated for the 12 bows/ranged innate attacks whose descriptions say range 2. CALCSCOPE reads it as the first range bound; the paired maximum is item_max_range. |
| `0x8d819` | item_max_range | high | investigation | Populated for the same 12 ranged items as item_min_range. CALCSCOPE reads it as the second range bound, matching every description's range-2 annotation. |
| `0x8dc01` | item_icon_id | high | investigation | ITINIT field for all 287 items. READICON sorts and indexes this array to select icon atlas entries; ALCHEMY, CHMENU, IMPROVE, and INFOIT use the same value for item-row presentation. |
| `0x8dfe9` | item_attack_element | high | investigation | Populated for 107 weapons/attacks. Values exactly match the AFINIT attack-attribute string table at GStr[0x2690 + value] and the Japanese item descriptions; combat scope/parameter code consumes the same array. |
| `0x8e3d1` | item_defense_element | high | investigation | Populated for the 13 shields. Values match descriptions such as physical, universal, holy, dark, spirit, and divinity defense; DRAWTIP renders them through AFINIT's defense string table at GStr[0x26a4 + value]. |
| `0x8e7b9` | item_character_whitelist | high | investigation | Sparse ITINIT row-major table with stride 5. CHMENU rejects an item when column 0 is populated and the selected party slot's character id is absent from the row. Unique accessories 471/472 allow one character each, while crossover accessories 480..485 allow character ids 2, 3, and 4. Unused trailing columns remain zero. Columns: 0=allowed_character_id_1, 1=allowed_character_id_2, 2=allowed_character_id_3, 3=allowed_character_id_4, 4=allowed_character_id_5. |
| `0x8ff29` | item_sex_restriction_mask | high | investigation | Sparse ITINIT equipment restriction. CHMENU uses unit_sex_category as a bit index and rejects an item when that bit is absent from this mask. Both populated items carry only bit 2; EBINIT value 2 is female, and ITMES explicitly describes item 419 as female-only. |
| `0x906f9` | item_status_delta_levels | high | investigation | Sparse ITINIT row-major table consumed by USEITEM and CALCILL when applying an item's effects. Positive values inflict or drain; -5 removes a condition (for example paralysis-removal item 108 stores -5 in column 8). Confirmed columns are 2 HP drain, 3 SP drain, 4 FS drain, 5 curse, 6 charm, 7 confusion, 8 paralysis, 9 poison, 10 water-flow, and 11 fear. Columns: 2=hp_drain, 3=sp_drain, 4=fs_drain, 5=curse, 6=charm, 7=confusion, 8=paralysis, 9=poison, 10=water_flow, 11=fear. |
| `0x97c29` | item_equipped_status_levels | high | investigation | Sparse ITINIT row-major table added to a unit's 30-column condition state by CALCREVISE. Item descriptions identify populated columns 9 poison, 11 fear, 13 regeneration, and 14 exaltation; these are passive equipped effects, distinct from item_status_delta_levels. Columns: 9=poison, 11=fear, 13=regeneration, 14=exaltation. |
| `0x9f159` | item_granted_skill_id | high | investigation | Populated for 85 equipment items. Values cross-resolve to SKINIT (for example flying bracelet=1 Flying, transfer bracelet=21 Transfer, thief key=22 Lockpick, ropes=156 Capture Attack); CALCREVISE applies the linked skill and UI scripts display it. |
| `0x9f541` | item_stat_modifiers | high | investigation | ITINIT row-major equipment modifiers added directly to the unit's 14-column stat record by CALCREVISE. Descriptions and consumers establish columns 0 accuracy, 1 evasion, 2 physical attack, 3 physical defense, 4 magic attack, 5 magic defense, 6 speed, 7 luck, 8 critical chance, 9 capture power, 10 movement, 11 max HP, 12 max SP, and 13 max FS. CALCBTPARAM adds columns 7 and 8 into the action's critical percentage, and CALCDMG compares the clamped result with a random-modulo-100 roll. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power, 10=movement, 11=max_hp, 12=max_sp, 13=max_fs. |
| `0xa2bf1` | item_tuning_curve_ids | high | investigation | ITINIT row-major table selecting an equipment-growth curve for each of the ten tunable fields. TUNE, IMPROVE, DRAWTIP, and CALCREVISE combine each nonzero curve id with the item's corresponding tuning level and index the shared curve-value table at 0xab6fa. Columns align with item_stat_modifiers columns 0..9. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power. |
| `0xa5301` | item_resource_recovery_amounts | high | investigation | Sparse ITINIT row-major consumable table. USEITEM applies columns 0, 1, and 2 to the matching three-column unit resource record; descriptions prove these are HP, SP, and FS respectively (for example item 101 stores HP 30, and item 107 stores 999/99/99 for full recovery). Columns: 0=hp, 1=sp, 2=fs. |
| `0xa62a1` | item_essence_recovery_amount | high | investigation | Sparse ITINIT consumable field. Item 106, Blood Price Healing Hand, describes essence recovery 50 and stores 50 here; USEITEM follows the dedicated essence-recovery path and scales the value before updating the selected unit. |
| `0xa6689` | item_weapon_class | high | investigation | Populated for 104 weapons/innate attacks. Stable values identify weapon families (1 unarmed, 2 staff, 3 claw, 4 dagger, 5 sword, 6 chain blade, 7 spear, 8 axe, 9 bow, 11 blade boots; later values are monster/natural attack classes). CALCDMG consumes it. |
| `0xa6a71` | item_handler_script_id | high | investigation | ITINIT field for all 287 items. Item menus look up this value and feed it directly to call-script; the packed id 0x319b resolves to ITMES.BIN, the shared per-item behavior/description dispatcher. |
| `0xa6e5a` | skill_sort_key | high | investigation | SKINIT field populated for 129 of 131 skills. ADDEXP, ADDSKILL, CHMENU, INFOIT, and SETCH combine it with skill_category to construct deterministic skill-list ordering keys; higher-level sort operations consume the resulting indices. |
| `0xa6f86` | skill_category | high | investigation | SKINIT category for all 131 skills. The records establish 1 movement/exploration, 2 defense/special attack, 3 utility/passive, 4 combat passive, 5 physical special, 6 offensive magic, and 7 healing magic. Combat and menu scripts dispatch directly on these values. |
| `0xa70b2` | skill_change_catalog_eligible | high | investigation | SKINIT inclusion flag for CHMENU's fourth (skill-change) catalog. CHMENU scans all 300 ids, keeps only nonzero rows, sorts acquired rows ahead of unavailable rows using skill_category and skill_sort_key, and copies the acquired prefix into catalog row 3. Eligible ids are 1, 2, 112, 130..133, 210, 228, 230, and 231. |
| `0xa71de` | skill_min_range_encoded | high | investigation | Populated for the 94 active skills. CALCSCOPE and CALCREVISE subtract one before using it as the lower range bound; SELACT and DRAWTIP use the same encoded bound. |
| `0xa730a` | skill_max_range_encoded | high | investigation | Populated for the same 94 active skills as skill_min_range_encoded. CALCSCOPE subtracts one to obtain the upper bound, exactly matching descriptions such as range 3 -> stored 4 and range 9 -> stored 10. |
| `0xa7436` | skill_attack_element | high | investigation | Populated for 60 elemental attacks and spells. Values match the Japanese descriptions and the same eight-value attack-element enum used by item_attack_element; CALCSCOPE, CALCBTPARAM, and SELACT consume it. |
| `0xa7562` | skill_status_levels | high | investigation | Sparse SKINIT row-major condition table consumed by CALCILL when applying a skill's effects to the target unit. Skill names and descriptions identify columns 1 instant death, 6 charm, 7 confusion, 8 paralysis, 9 poison, 10 water-flow, and 11 fear; the column layout matches item_status_delta_levels. Columns: 1=instant_death, 6=charm, 7=confusion, 8=paralysis, 9=poison, 10=water_flow, 11=fear. |
| `0xa988a` | skill_icon_id | high | investigation | SKINIT icon id for all 131 skills. CHMENU and INFOIT translate it into the shared icon atlas; the six ids group movement, defense, utility, passive, physical-special, and magic icons. |
| `0xa99b6` | skill_combat_stat_deltas | high | investigation | Sparse SKINIT row-major table applied by CALCBTPARAM for the selected action. Descriptions and combat consumers establish columns 0 accuracy, 1 evasion, 2 physical attack, 3 physical defense, 4 magic attack, 5 magic defense, 6 speed, 7 luck, 8 critical chance, and 9 capture power. Column 7 is unpopulated in shipped SKINIT; CALCBTPARAM adds any luck and critical modifiers into the critical percentage that CALCDMG rolls after the hit check. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power. |
| `0xaa56e` | skill_resource_deltas | high | investigation | Sparse SKINIT resource table used throughout skill selection and resolution. Column 0 is HP recovery for the three healing spells; column 1 is the negative SP cost for all 95 active skills, exactly matching each description. Column 2 is unpopulated in the shipped table. Columns: 0=hp_recovery, 1=sp_cost. |
| `0xaa8f2` | skill_proc_chance_percent | high | investigation | Probability for 14 passive skills. CALCDMG compares random-modulo 100 against this value; examples include Re-action 20, Double Action 100, Counter 10, and Resurrection 50. |
| `0xaaa1e` | skill_battle_animation_id | high | investigation | Populated for 101 combat skills. BTL and CALCDMG place this value in the battle-animation selector before calling BTANINIT; most skills reuse their own id, while related skills deliberately share an animation and passive reactions use ids 801..808. |
| `0xaab4a` | skill_handler_script_id | high | investigation | SKINIT field for all 131 skills. CHMENU and INFOIT look it up and pass it directly to call-script; packed id 0x31ca resolves to SKMES.BIN, the shared per-skill text/behavior dispatcher. |
| `0xe6dee` | object_sprite_state_row_mode | high | investigation | OBINIT metadata indexed by object type. FIELD copies the type-tagged initial payload into stage_object_runtime_state only for mode 1; DRAWOBJ likewise multiplies that runtime state by the object sprite height to select source Y only for mode 1. |
| `0xe7302` | stage_bgm_id | high | investigation | STINIT's per-stage scalar loaded for all 74 records. FIELD passes the value directly to play-bgm when starting the stage. |
| `0xe7303` | stage_target_clear_turns | high | investigation | STINIT's target/par turn count. STAGECLEAR divides elapsed turns by this value to derive a performance multiplier; when replaying an already-cleared ordinary stage, FIELD also uses it as the forced-retreat turn limit. |
| `0xe7304` | stage_clear_performance_bonus | med | investigation | Base stage-clear reward increment. STAGECLEAR multiplies it by the turn-performance percentage derived from stage_target_clear_turns, divides by 100, and adds the result to the capped persistent reward counter at 0x6719. The counter's player-facing resource name remains unresolved. |
| `0xe730b` | stage_replay_rules_disabled | high | investigation | FIELD's already-cleared-stage override gate. For a cleared stage with value 0, FIELD replaces the mission with an all-party retreat objective and a stage_target_clear_turns forced-retreat limit; value 1 suppresses that replay conversion and related revisit handling. |
| `0xe730c` | stage_turn_limit | high | investigation | STINIT's per-stage turn limit. DRAWCHP presents the value in the stage information, while FIELD compares the current turn against it when checking stage completion. |
| `0xe730d` | stage_turn_limit_outcome | high | investigation | STINIT mode paired with stage_turn_limit. Stage 1 stores 0 and describes 50-turn expiry as defeat; stage 2 stores 1 and explicitly describes 15-turn expiry as a forced-retreat clear. |
| `0xe7311` | stage_map_texture_asset_overrides | high | investigation | Twenty current-stage map texture slots. FIELD loads positive resource ids into tiled surface slots 0x52+index, skips zero, and substitutes the shared per-index fallback for -1; DRAWMAP selects and draws those surfaces through the terrain-to-texture-slot map. |
| `0xe7325` | stage_object_tile_x | high | investigation | X coordinate for each current-stage object slot. DRAWOBJ converts it to centered map-space pixels; FIELD and CALCOCC use it with stage_object_tile_y for object interaction and occupancy. |
| `0xe7357` | stage_object_tile_y | high | investigation | Y coordinate for each current-stage object slot. DRAWOBJ converts it to centered map-space pixels; FIELD and CALCOCC use it with stage_object_tile_x for object interaction and occupancy. |
| `0xe7389` | stage_object_type_id | high | investigation | Object-definition id for each current-stage object slot. SETOBJ decides whether the slot exists, while DRAWOBJ and FIELD use the id to select shared object graphics, dimensions, animation, collision, and behavior metadata. |
| `0xe741f` | stage_object_reinforcement_interval_turns | high | investigation | Per-object reinforcement schedule. FIELD compares the current turn and the object's runtime spawn count against this interval to calculate due weighted enemy spawns; object type 27 uses the same value as its one-shot trigger turn. |
| `0xe7451` | stage_object_reinforcement_spawn_limit | high | investigation | Maximum number of scheduled units materialized through a stage object. FIELD stops the object's reinforcement path once its runtime spawn counter reaches this value; object type 27 stores one for its one-shot special spawn. |
| `0xe7483` | stage_object_difficulty_mask | high | investigation | Per-object difficulty inclusion mask. SETOBJ uses check-bit with difficulty_index and rejects the object slot when the selected difficulty bit is absent. |
| `0xe74b5` | stage_object_required_story_flags | high | investigation | Seven positive prerequisites per stage object. SETOBJ subtracts one from every populated id and suppresses the object unless the corresponding story_event_flags cell equals 1. Columns: 0=required_flag_1, 1=required_flag_2, 2=required_flag_3, 3=required_flag_4, 4=required_flag_5, 5=required_flag_6, 6=required_flag_7. |
| `0xe7613` | stage_object_forbidden_story_flags | high | investigation | Five negative prerequisites per stage object. SETOBJ subtracts one from every populated id and suppresses the object when the corresponding story_event_flags cell equals 1. Columns: 0=forbidden_flag_1, 1=forbidden_flag_2, 2=forbidden_flag_3, 3=forbidden_flag_4, 4=forbidden_flag_5. |
| `0xe773f` | stage_enemy_spawn_tile_x | high | investigation | Direct X coordinate for a current-stage enemy slot. FIELD copies it into the runtime unit position when stage_enemy_spawn_object_slot is zero; object-linked alternatives instead inherit the referenced object's coordinates. |
| `0xe775d` | stage_enemy_spawn_tile_y | high | investigation | Direct Y coordinate paired with stage_enemy_spawn_tile_x. FIELD and ADDEN copy the pair into the runtime unit position; object-linked alternatives instead inherit the referenced object's coordinates. |
| `0xe777b` | stage_enemy_spawn_object_slot | high | investigation | Optional object-placement anchor for an enemy spawn. FIELD resolves nonzero entries through stage_object_type_id and uses the matching stage_object_tile_x/Y location; zero selects stage_enemy_spawn_tile_x/Y directly. |
| `0xe7799` | stage_enemy_faction_id | high | investigation | Per-spawn faction/team selector. FIELD and ADDEN copy it into the runtime unit's faction field, use it to choose the entity-index band, and test the same value against map occupancy masks. |
| `0xe77b7` | stage_enemy_difficulty_mask | high | investigation | Per-spawn difficulty inclusion mask. FIELD uses check-bit with difficulty_index and rejects a slot when the selected difficulty bit is absent. |
| `0xe77d5` | stage_enemy_first_clear_only | high | investigation | Per-spawn replay gate. FIELD rejects a populated enemy slot when this value is 2 and stage_clear_state for the current stage is 1; zero/default slots remain eligible on cleared-stage replays. |
| `0xe77f3` | stage_enemy_random_selection_weight | high | investigation | Weight for stage-enemy alternatives. FIELD groups eligible nonzero-weight slots by their stage_enemy_spawn_object_slot, sums the weights, makes a weighted random choice within each eligible group, and directly materializes zero-weight slots. |
| `0xe7811` | stage_enemy_unit_id | high | investigation | Unit-definition id for each current-stage enemy slot. FIELD rejects zero slots, while FIELD and ADDEN pass populated ids to SETEN to copy the corresponding EBINIT definition into a runtime entity. |
| `0xe782f` | stage_enemy_min_level | high | investigation | Scenario-specific minimum level for each stage enemy. After applying party-level auto-scaling, SETEN raises the runtime level to this floor before applying the maximum-level clamp. |
| `0xe784d` | stage_enemy_max_level | high | investigation | Scenario-specific maximum level for each stage enemy. SETEN uses a positive value as the upper clamp unless the debug/config override is active; otherwise it falls back to the unit definition's unit_level_cap. |
| `0xe786b` | stage_enemy_auto_level_scale_divisor | high | investigation | Scenario override for enemy auto-level scaling. SETEN divides the positive gap between the scenario base level and unit_starting_level by this value before adding it to the runtime level; zero would fall back to unit_auto_level_scale_divisor. |
| `0xe7889` | stage_enemy_movement_routine_set_ids | high | investigation | Three difficulty-specific movement-AI routine-set ids per stage enemy. SETEN copies the row into the runtime unit, and MVRTN selects the difficulty_index column as its movement routine table row. Columns: 0=difficulty_0, 1=difficulty_1, 2=difficulty_2. |
| `0xe78e3` | stage_enemy_battle_routine_set_ids | high | investigation | Optional three-column battle-AI routine override per stage enemy. SETEN copies a populated row into the runtime unit or falls back to the unit definition, and BTRTN selects the difficulty_index column as its battle routine table row. Columns: 0=difficulty_0, 1=difficulty_1, 2=difficulty_2. |
| `0xe793d` | stage_enemy_required_story_flags | high | investigation | Seven positive prerequisites per stage enemy. FIELD subtracts one from every populated id and suppresses the spawn unless the corresponding story_event_flags cell equals 1. Columns: 0=required_flag_1, 1=required_flag_2, 2=required_flag_3, 3=required_flag_4, 4=required_flag_5, 5=required_flag_6, 6=required_flag_7. |
| `0xe7a0f` | stage_enemy_forbidden_story_flags | high | investigation | Five negative prerequisites per stage enemy. FIELD subtracts one from every populated id and suppresses the spawn when the corresponding story_event_flags cell equals 1. Columns: 0=forbidden_flag_1, 1=forbidden_flag_2, 2=forbidden_flag_3, 3=forbidden_flag_4, 4=forbidden_flag_5. |
| `0x5` | — | low | auto-shape | array |
| `0xd2` | — | low | auto-shape | array |
| `0xd7` | — | low | auto-shape | array |
| `0x1a4` | — | low | auto-shape | array |
| `0x1a9` | — | low | auto-shape | array |
| `0x27e` | — | low | auto-shape | array |
| `0x2e2` | — | low | auto-shape | array |
| `0x315` | — | low | auto-shape | array |
| `0x41f` | — | low | auto-shape | array |
| `0x4f1` | — | low | auto-shape | array |
| `0x5c3` | — | low | auto-shape | array |
| `0x69e` | — | low | auto-shape | array |
| `0x6fe` | — | low | auto-shape | array |
| `0x712` | — | low | auto-shape | array |
| `0x7db` | — | low | auto-shape | array |
| `0x7e5` | — | low | auto-shape | array |
| `0x2690` | — | low | auto-shape | array |
| `0x26a4` | — | low | auto-shape | array |
| `0x26b5` | — | low | auto-shape | array |
| `0x26d3` | — | low | auto-shape | array |
| `0x27bd` | — | low | auto-shape | array |
| `0x3276` | — | low | auto-shape | array |
| `0x328a` | — | low | auto-shape | array |
| `0x329e` | — | low | auto-shape | array |
| `0x32b2` | — | low | auto-shape | array |
| `0x32c6` | — | low | auto-shape | array |
| `0x32da` | — | low | auto-shape | array |
| `0x3306` | — | low | auto-shape | array |
| `0x36ee` | — | low | auto-shape | array |
| `0x3ad6` | — | low | auto-shape | array |
| `0x4315` | — | low | auto-shape | array |
| `0x4379` | — | low | auto-shape | array |
| `0x45d7` | — | low | auto-shape | array |
| `0x463b` | — | low | auto-shape | array |
| `0x671c` | — | low | auto-shape | array |
| `0x671f` | — | low | auto-shape | array |
| `0x6727` | — | low | auto-shape | array |
| `0x6804` | — | low | auto-shape | array |
| `0x6868` | — | low | auto-shape | array |
| `0x6930` | — | low | auto-shape | array |
| `0x6994` | — | low | auto-shape | array |
| `0x7a6c` | — | low | auto-shape | array |
| `0x204fb` | — | low | auto-shape | array |
| `0x2052f` | — | low | auto-shape | array |
| `0x20533` | — | low | auto-shape | array |
| `0x20537` | — | low | auto-shape | array |
| `0x2053b` | — | low | auto-shape | array |
| `0x2053f` | — | low | auto-shape | array |
| `0x4dfbd` | — | low | auto-shape | array |
| `0x4dfef` | — | low | auto-shape | array |
| `0x4e021` | — | low | auto-shape | array |
| `0x4e053` | — | low | auto-shape | array |
| `0x521f3` | — | low | auto-shape | array |
| `0x52225` | — | low | auto-shape | array |
| `0x52257` | — | low | auto-shape | array |
| `0x52289` | — | low | auto-shape | array |
| `0x522bb` | — | low | auto-shape | array |
| `0x522ed` | — | low | auto-shape | array |
| `0x5231f` | — | low | auto-shape | array |
| `0x52351` | — | low | auto-shape | array |
| `0x53ede` | — | low | auto-shape | array |
| `0x55e3b` | — | low | auto-shape | array |
| `0x56223` | — | low | auto-shape | array |
| `0x56b20` | — | low | auto-shape | array |
| `0x56b52` | — | low | auto-shape | array |
| `0x56b85` | — | low | auto-shape | array |
| `0x56f6d` | — | low | auto-shape | array |
| `0x57357` | — | low | auto-shape | array |
| `0x577a3` | — | low | auto-shape | array |
| `0x5f0bb` | — | low | auto-shape | array |
| `0x5f0ed` | — | low | auto-shape | array |
| `0x617fe` | — | low | auto-shape | array |
| `0x61be6` | — | low | auto-shape | array |
| `0x61c4a` | — | low | auto-shape | array |
| `0x62032` | — | low | auto-shape | array |
| `0x6243a` | — | low | auto-shape | array |
| `0x6243c` | — | low | auto-shape | array |
| `0x62455` | — | low | auto-shape | array |
| `0x62469` | — | low | auto-shape | array |
| `0x6247d` | — | low | auto-shape | array |
| `0x624a3` | — | low | auto-shape | array |
| `0x624bf` | — | low | auto-shape | array |
| `0x628a7` | — | low | auto-shape | array |
| `0x62c8f` | — | low | auto-shape | array |
| `0x62cad` | — | low | auto-shape | array |
| `0x63c71` | — | low | auto-shape | array |
| `0x64441` | — | low | auto-shape | array |
| `0x64c11` | — | low | auto-shape | array |
| `0x653e1` | — | low | auto-shape | array |
| `0x65bb1` | — | low | auto-shape | array |
| `0x66381` | — | low | auto-shape | array |
| `0x66421` | — | low | auto-shape | array |
| `0x6642d` | — | low | auto-shape | array |
| `0x66442` | — | low | auto-shape | array |
| `0x6650a` | — | low | auto-shape | array |
| `0x665d8` | — | low | auto-shape | array |
| `0x665e3` | — | low | auto-shape | array |
| `0x66647` | — | low | auto-shape | array |
| `0x87a57` | — | low | auto-shape | array |
| `0x8fb41` | — | low | auto-shape | array |
| `0xa5eb9` | — | low | auto-shape | array |
| `0xaac78` | — | low | auto-shape | array |
| `0xaac96` | — | low | auto-shape | array |
| `0xaacb4` | — | low | auto-shape | array |
| `0xaacd2` | — | low | auto-shape | array |
| `0xab8c7` | — | low | auto-shape | array |
| `0xab8e9` | — | low | auto-shape | array |
| `0xab8f7` | — | low | auto-shape | array |
| `0xaba3b` | — | low | auto-shape | array |
| `0xaba43` | — | low | auto-shape | array |
| `0xaba4b` | — | low | auto-shape | array |
| `0xaba53` | — | low | auto-shape | array |
| `0xaba5f` | — | low | auto-shape | array |
| `0xaba64` | — | low | auto-shape | array |
| `0xcc9f4` | — | low | auto-shape | array |
| `0xcc9fe` | — | low | auto-shape | array |
| `0xcca3a` | — | low | auto-shape | array |
| `0xccb66` | — | low | auto-shape | array |
| `0xccbcf` | — | low | auto-shape | array |
| `0xccbd4` | — | low | auto-shape | array |
| `0xccbd9` | — | low | auto-shape | array |
| `0xccbf8` | — | low | auto-shape | array |
| `0xccc11` | — | low | auto-shape | array |
| `0xccc2f` | — | low | auto-shape | array |
| `0xe6aa4` | — | low | auto-shape | array |
| `0xe6ac2` | — | low | auto-shape | array |
| `0xe6ae0` | — | low | auto-shape | array |
| `0xe6c2a` | — | low | auto-shape | array |
| `0xe6c48` | — | low | auto-shape | array |
| `0xe6c5e` | — | low | auto-shape | array |
| `0xe6cc2` | — | low | auto-shape | array |
| `0xe6d26` | — | low | auto-shape | array |
| `0xe6d8a` | — | low | auto-shape | array |
| `0xe6e52` | — | low | auto-shape | array |
| `0xe6eb6` | — | low | auto-shape | array |
| `0xe6f1a` | — | low | auto-shape | array |
| `0xe6f7e` | — | low | auto-shape | array |
| `0xe710e` | — | low | auto-shape | array |
| `0xe7172` | — | low | auto-shape | array |
| `0xe7305` | — | low | auto-shape | array |
| `0xe730e` | — | low | auto-shape | array |
| `0xe73bb` | — | low | auto-shape | array |
| `0xe73ed` | — | low | auto-shape | array |
| `0xe770d` | — | low | auto-shape | array |
| `0xe7e8d` | — | low | auto-shape | array |
| `0xe8275` | — | low | auto-shape | array |
| `0xebd0d` | — | low | auto-shape | array |
| `0xec0f5` | — | low | auto-shape | array |
| `0xec4dd` | — | low | auto-shape | array |
| `0xec8c5` | — | low | auto-shape | array |
| `0xeccad` | — | low | auto-shape | array |
| `0xed095` | — | low | auto-shape | array |
| `0xed47d` | — | low | auto-shape | array |
| `0xed865` | — | low | auto-shape | array |
| `0xeebed` | — | low | auto-shape | array |
| `0xefb8d` | — | low | auto-shape | array |
| `0x1519f9` | — | low | auto-shape | array |
| `0x151cb5` | — | low | auto-shape | array |
| `0x151d19` | — | low | auto-shape | array |
| `0x151d7d` | — | low | auto-shape | array |
| `0x152039` | — | low | auto-shape | array |
| `0x15209d` | — | low | auto-shape | array |
| `0x152359` | — | low | auto-shape | array |
| `0x1523bd` | — | low | auto-shape | array |
| `0x152421` | — | low | auto-shape | array |
| `0x1525b2` | — | low | auto-shape | array |
| `0x152877` | — | low | auto-shape | array |
| `0x152879` | — | low | auto-shape | array |
| `0x15287b` | — | low | auto-shape | array |
| `0x15287d` | — | low | auto-shape | array |
| `0x15287f` | — | low | auto-shape | array |
| `0x152881` | — | low | auto-shape | array |
| `0x152883` | — | low | auto-shape | array |
| `0x152885` | — | low | auto-shape | array |
| `0x152887` | — | low | auto-shape | array |
| `0x152889` | — | low | auto-shape | array |
| `0x15576e` | — | low | auto-shape | array |
| `0x155b56` | — | low | auto-shape | array |
| `0x155b5c` | — | low | auto-shape | array |
| `0x155b62` | — | low | auto-shape | array |
| `0x155b68` | — | low | auto-shape | array |
| `0x155b6e` | — | low | auto-shape | array |
| `0x155b74` | — | low | auto-shape | array |
| `0x155b7a` | — | low | auto-shape | array |
| `0x155b80` | — | low | auto-shape | array |
| `0x155b86` | — | low | auto-shape | array |
| `0x155b8c` | — | low | auto-shape | array |
| `0x155b92` | — | low | auto-shape | array |
| `0x155b98` | — | low | auto-shape | array |
| `0x155b9e` | — | low | auto-shape | array |
| `0x155ba4` | — | low | auto-shape | array |
| `0x155c3a` | — | low | auto-shape | array |
| `0x155c4f` | — | low | auto-shape | array |
| `0x155c64` | — | low | auto-shape | array |
| `0x155c79` | — | low | auto-shape | array |
| `0x155c8e` | — | low | auto-shape | array |
| `0x155ca3` | — | low | auto-shape | array |
| `0x155e5c` | — | low | auto-shape | array |
| `0x155e71` | — | low | auto-shape | array |
| `0x155e86` | — | low | auto-shape | array |
| `0x155fc1` | — | low | auto-shape | array |
| `0x155fd6` | — | low | auto-shape | array |
| `0x155feb` | — | low | auto-shape | array |
| `0x156000` | — | low | auto-shape | array |
| `0x1560e8` | — | low | auto-shape | array |
| `0x156106` | — | low | auto-shape | array |
| `0x156124` | — | low | auto-shape | array |
| `0x156142` | — | low | auto-shape | array |
| `0x156160` | — | low | auto-shape | array |
| `0x15617e` | — | low | auto-shape | array |
| `0x15619c` | — | low | auto-shape | array |
| `0x1561ba` | — | low | auto-shape | array |
| `0x1561d8` | — | low | auto-shape | array |
| `0x1561f6` | — | low | auto-shape | array |
| `0x156214` | — | low | auto-shape | array |
| `0x1565fc` | — | low | auto-shape | array |
| `0x157d6c` | — | low | auto-shape | array |
| `0x15a118` | — | low | auto-shape | array |
| `0x15a17c` | — | low | auto-shape | array |
| `0x15a1e0` | — | low | auto-shape | array |
| `0x15a244` | — | low | auto-shape | array |
| `0x15a501` | — | low | auto-shape | array |
| `0x15a5c9` | — | low | auto-shape | array |
| `0x15a691` | — | low | auto-shape | array |

## index-pointer

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x32f1` | difficulty_index | high | investigation | GAMESTART's three-way difficulty selection. SETEN uses the zero-based index to select difficulty stat adjustments; FIELD indexes three-column stage-enemy arrays with it, and SETOBJ tests it against each object's difficulty mask. |
| `0x6718` | selected_party_slot | high | investigation | Current/selected slot in the 100-entry party-unit arrays. UNITECH chooses a free slot here before populating it; CHMENU replaces it with the selected sorted roster slot, then uses it to index party_slot_flags, party_slot_character_id, and companion per-slot tables. A natural New Game enters SC0000 with slot 2 selected. |
| `0x8c877` | current_item_id | high | investigation | Shared item-id argument/selection slot. Item menus and gameplay scripts write a chosen item id, use it to index ITINIT arrays, and dispatch through item_handler_script_id; ITMES compares it against all 287 item ids to select the matching player-facing title and description. |
| `0xa6e59` | current_skill_id | high | investigation | Shared skill-id argument/selection slot. Skill menus and combat scripts write the chosen skill id and use it to index SKINIT arrays; SKMES compares it against all 131 skill ids to select the matching player-facing title and description. |
| `0xeff75` | current_entity_index_hi | med | inference | High-purity current-entity row index (purity 0.95 in the auto shape map); dominant 2D-table row selector. |
| `0x152616` | current_entity_index | med | investigation | Primary current-entity row index (RECOVER-confirmed; purity 0.51, 363 row-index uses). |
| `0x152617` | current_stage_enemy_spawn_slot | high | investigation | Current STINIT enemy-template slot. FIELD selects slots 1..29 while materializing stage units, SETEN records the slot on the runtime entity and reads every parallel enemy buffer through it, and ADDEN uses slot 0 for its special generated unit. |

## story-flag

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0xa57` | lily_form_a | high | investigation | Lily current-form flag A. Exactly one of form A/B/C is 1; gates form-specific voiced dialogue (seeding 0xa57=1 -> SC0000 186->229 lines). Set externally (menu/save), no static writer. |
| `0xa58` | lily_form_b | high | investigation | Lily current-form flag B. See lily_form_a. |
| `0xa59` | lily_form_c | high | investigation | Lily current-form flag C. See lily_form_a. |
| `0xa99` | game_started | low | inference | New-game/playthrough gate: set to 1 by GAMESTART.BIN, then tested `==1` in story scenes (SC0200/SC0490, e.g. `0xa99==1 AND stat>3`). Marks that the game proper has begun. INFERENCE from the GAMESTART writer — confirm exact semantics before relying on it. |
| `0xd92` | — | med | auto-shape | TODO: confirm. Branch-read in 29 scenes / 29 scripts; compared against [1]; writers=none (external/native?). |
| `0x3231` | game_mode | med | inference | Game-mode/phase selector in the 0x3231-0x3234 progression-state cluster (chapter_mode is 0x3234). Enum 1..9, written by the gameplay scripts (AIM/ALCHEMY/BTL/BUNKI), branch-read in 136 scenes. Distinct from chapter; likely current sub-mode/screen. INFERENCE — confirm with a listing/sweep before relying on the exact meaning. |
| `0x3234` | chapter_mode | high | investigation | Progression chapter/mode selector. SCJUMP's top-level switch keys on it; branch-read by progression scripts (FIELD etc.), not directly by SC/SP scenes. |
| `0x3275` | — | med | auto-shape | TODO: confirm. Branch-read in 136 scenes / 143 scripts; compared against [0]; writers=['CAMP.BIN', 'DEBUGADV.BIN', 'DEBUGADV2.BIN', 'FIELD.BIN']. |
| `0x671b` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 5 scripts; compared against [999]; writers=['ALCHEMY.BIN', 'DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN']. |
| `0x671e` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against [5, 6]; writers=['USEMAGIC.BIN']. |
| `0x6722` | — | med | auto-shape | TODO: confirm. Branch-read in 13 scenes / 18 scripts; compared against [0, 1, 5, 99]; writers=['GAMESTART.BIN', 'SALLY.BIN', 'SC0010.BIN', 'SC0140.BIN']. |
| `0x6724` | — | med | auto-shape | TODO: confirm. Branch-read in 4 scenes / 10 scripts; compared against [0, 25, 50, 99]; writers=['DEBUGADV2.BIN', 'DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN']. |
| `0x7679` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 4 scripts; compared against [0, 1, 4, 8, 16, 256, 512, 1024]; writers=['FIELD.BIN']. |
| `0x767a` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 4 scripts; compared against [0]; writers=['DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN', 'FIELD.BIN']. |
| `0x767b` | — | med | auto-shape | TODO: confirm. Branch-read in 136 scenes / 150 scripts; compared against [0, 1]; writers=['DEBUGADV2.BIN', 'DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN']. |
| `0x767d` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 9 scripts; compared against [0, 1, 2, 3]; writers=['CAMP.BIN', 'DEBUGADV2.BIN', 'DEBUGMAP.BIN', 'DEBUGMAP2.BIN']. |
| `0x204f5` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against [1, 2, 4, 8, 16, 32, 64]; writers=['FIELD.BIN']. |
| `0x204f7` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against [1, 2, 4, 6]; writers=['DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN', 'GAMESTART.BIN']. |
| `0x20530` | — | med | auto-shape | TODO: confirm. Branch-read in 14 scenes / 22 scripts; compared against [0]; writers=['DEBUGADV2.BIN', 'DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN']. |
| `0x20534` | — | med | auto-shape | TODO: confirm. Branch-read in 14 scenes / 24 scripts; compared against [0, 10, 100, 999]; writers=['DEBUGADV2.BIN', 'DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN']. |
| `0x53ef4` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against [0]; writers=['CAMP.BIN']. |
| `0x55e37` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against [0]; writers=['EVOLVE.BIN', 'STUDY.BIN']. |
| `0x55e38` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 9 scripts; compared against [0, 1, 2]; writers=['CHMENU.BIN', 'INFO.BIN', 'INFOAF.BIN', 'INFOCH.BIN']. |
| `0x55e39` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 8 scripts; compared against [0, 1, 4]; writers=['ALCHEMY.BIN', 'EVOLVE.BIN', 'MAGIC2.BIN', 'SELSTAGE.BIN']. |
| `0x55e3a` | — | med | auto-shape | TODO: confirm. Branch-read in 136 scenes / 143 scripts; compared against [0]; writers=['FORT.BIN']. |
| `0x617fd` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 6 scripts; compared against [0, 25, 50]; writers=['GAMESTART.BIN', 'TUNE.BIN']. |
| `0x62428` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against [0]; writers=['CALLBACK_WINDOW.BIN', 'DEBUG.BIN', 'DEBUGADV.BIN', 'DEBUGADV2.BIN']. |
| `0x62439` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 6 scripts; compared against [0]; writers=['CHMENU.BIN', 'INFOAF.BIN', 'INFOCH.BIN', 'INFOEN.BIN']. |
| `0x624be` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 8 scripts; compared against [0]; writers=['MENU.BIN']. |
| `0x6642c` | route_branch | med | inference | Route/branch selector: sole writer is BUNKI.BIN (分岐 = branching), enum 0..7, branch-read in 36 scenes. Selects a story branch/route. INFERENCE from the BUNKI writer + enum — confirm exact routes via a listing/sweep. |
| `0x665d3` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 4 scripts; compared against [1, 2, 4, 8, 32, 64, 128, 65536]; writers=['ALCHEMY.BIN', 'BUNKI.BIN', 'DEBUGADV.BIN', 'EVOLVE.BIN']. |
| `0x665d6` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against []; writers=['MES.BIN', 'SBUNKI.BIN']. |
| `0x665d7` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 5 scripts; compared against [1, 3]; writers=['ADDSKILL.BIN', 'SBUNKI.BIN']. |
| `0x66714` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against []; writers=['DEBUGBTL.BIN', 'FIELD.BIN', 'RTN_M051.BIN', 'RTN_M052.BIN']. |
| `0xab8e7` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against [0, 10, 15, 20, 25, 30, 35, 40]; writers=['CALCCC.BIN', 'CCINIT.BIN']. |
| `0xaba5c` | — | med | auto-shape | TODO: confirm. Branch-read in 136 scenes / 149 scripts; compared against [0, 1]; writers=['DEBUGADV.BIN', 'DEBUGADV2.BIN', 'SC0000.BIN', 'SC0010.BIN']. |
| `0xaba5e` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 5 scripts; compared against [2]; writers=['DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN']. |
| `0xcc9f3` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 6 scripts; compared against []; writers=['CALCSCOPE.BIN']. |
| `0xccc21` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 4 scripts; compared against [0]; writers=['CHMENU.BIN']. |
| `0xeff77` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against [0, 2, 3]; writers=['BTRTN.BIN', 'MVRTN.BIN', 'RTN_B001.BIN', 'RTN_B002.BIN']. |
| `0x1560e7` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 4 scripts; compared against [0, 1, 2, 3, 4, 5, 6, 7]; writers=['MAGIC.BIN', 'SELACT.BIN', 'SUMMON.BIN']. |
| `0x15a094` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 6 scripts; compared against [0]; writers=['ALCHEMY.BIN', 'EVOLVE.BIN', 'SELSTAGE.BIN', 'STUDY.BIN']. |
| `0x15a095` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 6 scripts; compared against [0]; writers=['INFO.BIN', 'INFOCH.BIN', 'MENU.BIN']. |

## string-table

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x26f1` | object_type_names | high | investigation | OBINIT writes the authoritative object names. FIELD, SETOBJ, and DRAWOBJ use STINIT's object type id to select these definitions; extract_init joins the names to stage object placements. |
| `0x2755` | object_type_descriptions | high | investigation | OBINIT writes the short object descriptions displayed by the field object-information path. extract_init joins populated descriptions to STINIT object placements by type id. |
| `0x27b9` | stage_victory_condition_1 | high | investigation | STINIT writes one value for each of its 74 stage records. AIM renders this line first in the victory-condition section, and FIELD copies it into the current mission-condition display. |
| `0x27ba` | stage_victory_condition_2 | high | investigation | STINIT writes one value for each of its 74 stage records. AIM renders nonempty values after stage_victory_condition_1, and FIELD copies the slot into the current mission-condition display. |
| `0x27bb` | stage_defeat_condition_1 | high | investigation | STINIT writes one value for each of its 74 stage records. AIM renders this line first in the defeat-condition section, and FIELD copies it into the current mission-condition display. |
| `0x27bc` | stage_defeat_condition_2 | high | investigation | STINIT writes one value for each of its 74 stage records. AIM renders nonempty values after stage_defeat_condition_1, and FIELD copies the slot into the current mission-condition display. |
| `0x276` | — | med | auto-shape | string-table (written by SC0130) |
| `0x277` | — | med | auto-shape | string-table (written by SC0130) |
| `0x278` | — | med | auto-shape | string-table (written by FIELD) |
| `0x279` | — | med | auto-shape | string-table (written by INPUTNAME) |
| `0x27a` | — | med | auto-shape | string-table (written by READY) |
| `0x27b` | — | med | auto-shape | string-table (written by READY) |
| `0x27c` | — | med | auto-shape | string-table (written by READY) |
| `0x27d` | — | med | auto-shape | string-table (written by READY) |
| `0x316` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x317` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0x318` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0x319` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0x31a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x31b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x31c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x31d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x31e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x31f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x320` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x321` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x322` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x323` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x324` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x325` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x326` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x327` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x328` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x329` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x32a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x32b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x32c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x32d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x32e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x32f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x330` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x331` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x332` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x333` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x334` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x335` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x336` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x337` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x338` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x339` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x33a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x33b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x33c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x33d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x33e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x33f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x340` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x341` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x342` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x343` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x344` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x345` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x346` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x348` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x349` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x34b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x34c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x34d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x34e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x34f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x350` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x351` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x352` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x353` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x354` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x355` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x356` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x357` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x358` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x359` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x35a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x35b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x35c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x35d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x35e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x35f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x360` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x361` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x362` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x363` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x364` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x365` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x36f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x370` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x371` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x372` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x373` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x374` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x37a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x37b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x37c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x37d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x37e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x37f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x381` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x382` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x383` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x384` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x385` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x387` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x388` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x389` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x38a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x38c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x38d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x38e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x38f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x390` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x391` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x392` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x393` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x394` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x395` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x396` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x397` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x398` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x399` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x39a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x39c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x39d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x39e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x39f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3a0` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3a1` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3a2` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3a3` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3a4` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3aa` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0x3ab` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0x3ac` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0x3dd` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3de` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3df` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3e0` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3e2` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3e3` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3e4` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3ec` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3ed` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3ee` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3ef` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3f0` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3f1` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3f6` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3f7` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3f8` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3f9` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3fa` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x3fb` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x400` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x405` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x40f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x414` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x419` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x41e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x423` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x424` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x425` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x428` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x441` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x442` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x446` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x447` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x44b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x450` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x455` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x456` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x45a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x473` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x478` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x479` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x47d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x482` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4a5` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4a6` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4aa` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4ab` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4af` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4b4` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4d7` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4dc` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4dd` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4de` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4e1` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4e6` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x509` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x50a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x50e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x50f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x510` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x511` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x513` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x514` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x518` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x51d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x51e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x522` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x56d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x572` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x577` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x57c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x581` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x59f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5a4` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5a5` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5ae` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5af` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5b3` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5b4` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5b5` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5b6` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5bd` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5d1` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5d6` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5db` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5e0` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5e5` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5ea` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5ef` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5f4` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x603` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x608` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x609` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x60a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x60b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x635` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x636` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x637` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x638` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x639` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x63a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x63b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x63c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x63d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x63e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x63f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x640` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x641` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x642` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x643` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x644` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x645` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x646` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x647` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x648` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x649` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x64a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x64b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x64c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x64d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x64e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x64f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x650` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x651` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x652` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x653` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x654` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x655` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x656` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x657` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x658` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x667` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x668` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x669` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x66a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x66b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x66c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x66d` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x66e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x66f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x670` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x671` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x672` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x673` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x674` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x675` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x676` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x677` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x678` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x679` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x67a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x7da` | — | med | auto-shape | string-table (written by SC0100) |
| `0x849` | — | med | auto-shape | string-table (written by SAVE) |
| `0x84b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x84c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x84d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x84e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x84f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x850` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x851` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x852` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x853` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x854` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x855` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x856` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x857` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x858` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x859` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x85a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x85b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x85c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x85d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x85e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x85f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x860` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x861` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x862` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x863` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x864` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x865` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x866` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x867` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x868` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x869` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x86a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x86b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x86c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x86d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x86e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x86f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x870` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x871` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x872` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x873` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x874` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x875` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x876` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x877` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x878` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x879` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x87a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x87b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x87d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x87e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x880` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x881` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x882` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x883` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x884` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x885` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x886` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x887` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x888` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x889` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x88a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x88b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x88c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x88d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x88e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x88f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x890` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x891` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x892` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x893` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x894` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x895` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x896` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x897` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x898` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x899` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x89a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8a4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8a5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8a6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8a7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8a8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8a9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8af` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8b0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8b1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8b2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8b3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8b4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8b6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8b7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8b8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8b9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8ba` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8bc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8bd` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8be` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8bf` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8c1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8c2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8c3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8c4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8c5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8c6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8c7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8c8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8c9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8ca` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8cb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8cc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8cd` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8ce` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8cf` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8d1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8d2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8d3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8d4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8d5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8d6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8d7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8d8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8d9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8df` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8e0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x8e1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x912` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x913` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x914` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x915` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x917` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x918` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x919` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x921` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x922` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x923` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x924` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x925` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x926` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x92b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x92c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x92d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x92e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x92f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x930` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x935` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x93a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x944` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x949` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x94e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x953` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x958` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x959` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x95a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x95d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x976` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x977` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x97b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x97c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x980` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x985` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x98a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x98b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x98f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x9a8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x9ad` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x9ae` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x9b2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x9b7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x9da` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x9db` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x9df` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x9e0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x9e4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x9e9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa0c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa11` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa12` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa13` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa16` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa1b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa3e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa3f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa43` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa44` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa45` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa46` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa48` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa49` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa4d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa52` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xa53` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xaa2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xaa7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xaac` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xab1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xab6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xad4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xad9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xada` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xae3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xae4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xae8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xae9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xaea` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xaeb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xaf2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb06` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb0b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb10` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb15` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb1a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb1f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb24` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb29` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb38` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb3d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb3e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb3f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb40` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb6a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb6b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb6c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb6d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb6e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb6f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb70` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb71` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb72` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb73` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb74` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb75` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb76` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb77` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb78` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb79` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb7a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb7b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb7c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb7d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb7e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb7f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb80` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb81` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb82` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb83` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb84` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb85` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb86` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb87` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb88` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb89` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb8a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb8b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb8c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb8d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb9c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb9d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb9e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xb9f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xba0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xba1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xba2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xba3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xba4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xba5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xba6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xba7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xba8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xba9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xbaa` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xbab` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xbac` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xbad` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xbae` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xbaf` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc33` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc34` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0xc35` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0xc36` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0xc37` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc38` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc39` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc3a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc3b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc3c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc3d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc3e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc3f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc40` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc41` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc42` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc43` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc44` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc45` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc46` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc47` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc48` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc49` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc4a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc4b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc4c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc4d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc4e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc4f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc50` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc51` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc52` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc53` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc54` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc55` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc56` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc57` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc58` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc59` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc5a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc5b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc5c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc5d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc5e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc5f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc60` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc61` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc62` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc63` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc65` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc66` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc68` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc69` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc6a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc6b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc6c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc6d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc6e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc6f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc70` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc71` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc72` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc73` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc74` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc75` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc76` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc77` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc78` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc79` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc7a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc7b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc7c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc7d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc7e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc7f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc80` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc81` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc82` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc8c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc8d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc8e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc8f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc90` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc91` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc97` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc98` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc99` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc9a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc9b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc9c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc9e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xc9f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xca0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xca1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xca2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xca4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xca5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xca6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xca7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xca9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcaa` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcab` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcac` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcad` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcae` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcaf` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcb0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcb1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcb2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcb3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcb4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcb5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcb6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcb7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcb9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcba` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcbb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcbc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcbd` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcbe` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcbf` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcc0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcc1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcc7` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0xcc8` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0xcc9` | — | med | auto-shape | string-table (written by GAMESTART) |
| `0xcfa` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcfb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcfc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcfd` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xcff` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd00` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd01` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd09` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd0a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd0b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd0c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd0d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd0e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd13` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd14` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd15` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd16` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd17` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd18` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd1d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd22` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd2c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd31` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd36` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd3b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd40` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd41` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd42` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd45` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd5e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd5f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd63` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd64` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd68` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd6d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd72` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd73` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd77` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd90` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd95` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd96` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd9a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xd9f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xdc2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xdc3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xdc7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xdc8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xdcc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xdd1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xdf4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xdf9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xdfa` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xdfb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xdfe` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe03` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe26` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe27` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe2b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe2c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe2d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe2e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe30` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe31` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe35` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe3a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe3b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe3f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe8a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe8f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe94` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe99` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xe9e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xebc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xec1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xec2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xecb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xecc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xed0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xed1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xed2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xed3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xeda` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xeee` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xef3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xef8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xefd` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf02` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf07` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf0c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf11` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf20` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf25` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf26` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf27` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf28` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf52` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf53` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf54` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf55` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf56` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf57` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf58` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf59` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf5a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf5b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf5c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf5d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf5e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf5f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf60` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf61` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf62` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf63` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf64` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf65` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf66` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf67` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf68` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf69` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf6a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf6b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf6c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf6d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf6e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf6f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf70` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf71` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf72` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf73` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf74` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf75` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf84` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf85` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf86` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf87` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf88` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf89` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf8a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf8b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf8c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf8d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf8e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf8f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf90` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf91` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf92` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf93` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf94` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf95` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf96` | — | med | auto-shape | string-table (written by EBINIT) |
| `0xf97` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x101b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x101c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x101d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x101e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x101f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1020` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1021` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1022` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1023` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1024` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1025` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1026` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1027` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1028` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1029` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x102f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1030` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1031` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1032` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1033` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1034` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1035` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1041` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1042` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1043` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1044` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1045` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1046` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x104d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x104e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1050` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1051` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1052` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1053` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1054` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1055` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1056` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1057` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1058` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1059` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x105a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x105b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x105c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x105d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x105e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x105f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1060` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1061` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1062` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1063` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1064` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1065` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1066` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1067` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1068` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1069` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x106a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1079` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x107f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1080` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1081` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1082` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1083` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1084` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1086` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1087` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1088` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1089` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x108a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x108c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x108d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x108e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x108f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1091` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1092` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1093` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1094` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1095` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1096` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1097` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1098` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1099` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x109a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x109b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x109c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x109d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x109e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x109f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10a1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10a2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10a3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10a4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10a5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10a6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10a7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10a8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10a9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10af` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10b0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10b1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10e2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10e3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10e4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10e5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10e7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10e8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10e9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10f1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10f2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10f3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10f4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10f5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10f6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10fb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10fc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10fd` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10fe` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x10ff` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1100` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1105` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x110a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1114` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1119` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x111e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1123` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1128` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1129` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x112a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x112d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1146` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1147` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x114b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x114c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1150` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1155` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x115a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x115b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x115f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1178` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x117d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x117e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1182` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1187` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11aa` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11ab` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11af` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11b0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11b4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11b9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11dc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11e1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11e2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11e3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11e6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x11eb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x120e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x120f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1213` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1214` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1215` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1216` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1218` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1219` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x121d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1222` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1223` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1227` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1272` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1277` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x127c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1281` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1286` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12a4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12a9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12aa` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12b3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12b4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12b8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12b9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12ba` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12bb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12c2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12d6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12db` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12e0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12e5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12ea` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12ef` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12f4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x12f9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1308` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x130d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x130e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x130f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1310` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x133a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x133b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x133c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x133d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x133e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x133f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1340` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1341` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1342` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1343` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1344` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1345` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1346` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1347` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1348` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1349` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x134a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x134b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x134c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x134d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x134e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x134f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1350` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1351` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1352` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1353` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1354` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1355` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1356` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1357` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1358` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1359` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x135a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x135b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x135c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x135d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x136c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x136d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x136e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x136f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1370` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1371` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1372` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1373` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1374` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1375` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1376` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1377` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1378` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1379` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x137a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x137b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x137c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x137d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x137e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x137f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1592` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1593` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1596` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1597` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x159c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x159d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x159e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x159f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15a0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15a1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15b0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15b1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15b4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15b5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15b8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15b9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15ba` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15c4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15c5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15c6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15c7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15c8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15c9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15ca` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15cb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15cc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15cd` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15ce` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x15d8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1600` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1601` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x160a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x160b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1614` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1615` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x161e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x161f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1620` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1621` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1622` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1623` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1628` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1629` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x165a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x165b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x165c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1664` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1665` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1666` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1667` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x166e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x166f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1678` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1679` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1682` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1684` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x16be` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x16bf` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x16c8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x16c9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x16ca` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x16cb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x16d2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x16dc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x16dd` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1722` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1723` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1724` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1725` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x172c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x172d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x172e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x172f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1736` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1737` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1740` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1786` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1787` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1790` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1792` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1793` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1794` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1795` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x17ea` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x17eb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x17ec` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x17ed` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x17f4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x17f8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x17fe` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x17ff` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1800` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1801` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1808` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1809` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1812` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1814` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x18d0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x18d1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1916` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1917` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1920` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1921` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1922` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1923` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1934` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1935` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1936` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1937` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x193e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x193f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1940` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1941` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1942` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1943` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1944` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1945` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1952` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1953` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x197a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a44` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a45` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a46` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a47` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a48` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a49` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a4a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a4b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a56` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a57` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a58` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a5a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a5b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a5c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a5d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a5e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a62` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a63` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a66` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a67` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a6e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a6f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a70` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a72` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a73` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a74` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a76` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a77` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a78` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a79` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a7a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a7b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a7c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a7e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a80` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a81` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a82` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a83` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a84` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a85` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a86` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a87` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1a88` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1aa6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1aa7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1aa8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1aaa` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1aab` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1aac` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1aad` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1aae` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1aaf` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ab0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ab1` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ab2` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ab3` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ab4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ab5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ab6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ab7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ab8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ab9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1aba` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1abb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1abe` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1abf` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ac0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ac4` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ac5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ac6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1ac8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1aca` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1acc` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1acd` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x1bd3` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bd4` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bd5` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bd6` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bd7` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bd8` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bd9` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bda` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bdb` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bdc` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bde` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bdf` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1be4` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1be5` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1be6` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1be7` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1be8` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1be9` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bea` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1beb` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bec` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bed` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bf1` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bf2` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bf3` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bf4` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bf5` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bf6` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bf7` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bf8` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bf9` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bfa` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bfb` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1bfc` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c05` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c06` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c08` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c09` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c0a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c0b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c0c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c0d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c0e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c0f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c10` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c11` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c12` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c13` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c14` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c15` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c16` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c17` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c18` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c19` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c1a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c1b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c1c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c1d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c1e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c1f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c20` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c21` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c22` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c2d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c2e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c2f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c37` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c38` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c39` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c3a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c3c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c3d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c3e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c3f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c40` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c43` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c45` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c47` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c48` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c49` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c4a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c4b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c9b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c9d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1c9f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1ca0` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1caa` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cab` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cae` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1caf` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cb0` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cb2` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cb5` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cbf` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cc0` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cc2` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cc3` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cc5` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cc7` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cca` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cd0` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cd1` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cd5` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cd7` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cd9` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cdf` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1ce3` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1ce4` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1ce6` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1ce9` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cea` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1ceb` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cee` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cef` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cf1` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cf8` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cfa` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cfc` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1cff` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d01` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d03` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d04` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d0e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d10` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d12` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d13` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d14` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d17` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d19` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d20` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d22` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d25` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d27` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d29` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d2a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d2b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d2d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d36` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d39` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d4f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d51` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d54` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d5e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d5f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d61` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d62` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d63` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d66` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d68` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d6b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d6f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d72` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d75` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d77` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d78` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d79` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d7a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d7b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d7c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d84` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d85` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d86` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d87` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d88` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d89` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d8a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d8b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d8c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d8d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d8e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d8f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d90` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d91` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d92` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d93` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d94` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d95` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d96` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d97` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d98` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d99` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d9a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d9b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d9d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d9e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1d9f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1da0` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1da2` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1da3` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1da4` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1da5` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1da6` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1da8` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1da9` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1daa` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1dab` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1db2` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1db3` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1db4` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1db5` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1db6` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1db7` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1dc7` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1dc9` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1dcb` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e32` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e34` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e35` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e37` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e38` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e39` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e3a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e3b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e3e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e40` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e41` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e43` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e45` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e46` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e47` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e4a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e4b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e4c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e4e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e4f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e51` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e53` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e54` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e55` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e56` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e57` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e58` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e5b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e5c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e5d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e5e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e5f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e8f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e90` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e92` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e93` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e94` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e95` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e96` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e97` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e98` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1e99` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f57` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f58` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f59` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f5a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f5b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f5c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f5d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f5e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f5f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f60` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f61` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f62` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f63` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f64` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f68` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f69` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f6a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f6b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f6c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f6d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f6e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f6f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f70` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f71` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f72` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f73` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f74` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f75` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f76` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f77` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f78` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f79` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f7a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f7b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f7c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f7d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f7e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f7f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f80` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x1f81` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x201f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2020` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2021` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2022` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2024` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2025` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2026` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2027` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2028` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x202b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x202d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x202f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2030` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2031` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2032` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2033` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2083` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2085` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2087` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2088` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2092` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2093` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2096` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2097` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2098` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x209a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x209d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20a7` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20a8` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20aa` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20ab` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20ad` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20af` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20b2` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20b8` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20b9` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20bd` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20bf` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20c1` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20c7` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20cb` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20cc` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20ce` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20d1` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20d2` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20d3` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20d6` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20d7` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20d9` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20e0` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20e2` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20e4` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20e7` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20e9` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20eb` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20ec` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20f6` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20f8` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20fa` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20fb` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20fc` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x20ff` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2101` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2108` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x210a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x210d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x210f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2111` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2112` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2113` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2115` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x211e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2121` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2137` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2139` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x213c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2146` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2147` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2149` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x214a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x214b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x214e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2150` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2153` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2157` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x215a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x215d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x215f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2160` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2161` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2162` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2163` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2164` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x216c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x216d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x216e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x216f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2170` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2171` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2172` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2173` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2174` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2175` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2176` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2177` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2178` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2179` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x217a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x217b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x217c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x217d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x217e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x217f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2180` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2181` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2182` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2183` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2185` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2186` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2187` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2188` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x218a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x218b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x218c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x218d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x218e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2190` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2191` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2192` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2193` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x219a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x219b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x219c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x219d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x219e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x219f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x21af` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x21b1` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x21b3` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2277` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2278` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x227a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x227b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x227c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x227d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x227e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x227f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2280` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2281` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x233f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2340` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2341` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2342` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2343` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2344` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2345` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2346` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2347` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2348` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2349` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x234a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x234b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x234c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2350` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2351` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2352` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2353` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2354` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2355` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2356` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2357` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2358` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2359` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x235a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x235b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x235c` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x235d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x235e` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x235f` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2360` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2361` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2362` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2363` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2364` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2365` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2366` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2367` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2368` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x2369` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x23a3` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23a4` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23a5` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23a6` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23ad` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23b7` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23b8` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23b9` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23ba` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23bb` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23bd` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23be` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23bf` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23c0` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23c1` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23c2` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23c3` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23c4` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23c5` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23c6` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23c7` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23c8` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23c9` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23ca` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23cb` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23cc` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23cd` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23ce` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23cf` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23d0` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23d1` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23d2` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23d3` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23d4` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x23d5` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2407` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2408` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2409` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x240a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x240b` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x240c` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x240d` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x240e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x240f` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2410` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2411` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2412` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2413` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2414` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2415` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2416` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2417` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2418` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2419` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x241a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x241b` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x241c` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x241d` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x241e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x241f` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2420` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2421` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2422` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2423` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2424` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2425` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2426` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2427` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2428` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2429` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x242a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x242b` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x242c` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x242d` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x242e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x242f` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2430` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2432` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2438` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2439` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x243a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x243b` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x243c` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x243d` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x243e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x243f` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2440` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2441` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2442` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2443` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2469` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x246a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x246b` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x246c` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x246d` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x246e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x246f` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2470` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2471` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2472` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2473` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2474` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2475` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2476` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x247d` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x247e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2480` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2481` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2483` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2484` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2485` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2486` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2487` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2488` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2489` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x248a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x248c` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x248d` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x248e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x248f` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2490` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2491` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2492` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2494` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2495` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2496` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2497` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2498` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2499` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x249a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x249b` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24cf` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24d0` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24d1` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24d2` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24d9` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24e3` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24e4` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24e5` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24e6` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24e7` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24e9` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24ea` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24eb` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24ec` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24ed` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24ee` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24ef` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24f0` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24f1` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24f2` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24f3` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24f4` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24f5` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24f6` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24f7` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24f8` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24f9` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24fa` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24fb` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24fc` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24fd` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24fe` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x24ff` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2500` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2501` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2533` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2534` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2535` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2536` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2537` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2538` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2539` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x253a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x253b` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x253c` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x253d` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x253e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x253f` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2540` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2541` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2542` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2543` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2544` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2545` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2546` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2547` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2548` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2549` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x254a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x254b` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x254c` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x254d` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x254e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x254f` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2550` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2551` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2552` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2553` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2554` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2555` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2556` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2557` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2558` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2559` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x255a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x255b` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x255c` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x255e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2564` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2565` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2566` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2567` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2568` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2569` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x256a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x256b` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x256c` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x256d` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x256e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x256f` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2597` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2598` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x2599` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x259a` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x259b` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x259c` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x259d` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x259e` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x259f` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25a0` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25a1` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25a2` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25a9` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25aa` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25ac` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25ad` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25af` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25b0` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25b1` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25b2` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25b3` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25b4` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25b5` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25b6` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25b8` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25b9` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25ba` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25bb` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25bc` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25bd` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25be` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25c0` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25c1` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25c2` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25c3` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25c4` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25c5` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25c6` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25c7` | — | med | auto-shape | string-table (written by SKINIT) |
| `0x25ff` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2604` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2605` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2606` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2607` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2608` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2609` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x260a` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x260b` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x260c` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x260d` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x260e` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x260f` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2610` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2611` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2612` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2613` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2614` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2615` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2616` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2617` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2618` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2619` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x261a` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x261b` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x261c` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x261d` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x261e` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x261f` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2620` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2621` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2622` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2623` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2624` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2625` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2626` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2627` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2628` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2629` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x262a` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x262b` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x262c` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x262d` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x262e` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x262f` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2630` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2631` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2632` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2633` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2634` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2635` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x263b` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x263c` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x263d` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x263e` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x263f` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2640` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2641` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2642` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2643` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2644` | — | med | auto-shape | string-table (written by ILINIT) |
| `0x2691` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x2692` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x2693` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x2694` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x2695` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x2696` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x2697` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x2698` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x269b` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x269c` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x269d` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x269e` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x269f` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26a0` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26a1` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26a5` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26a6` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26a7` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26a8` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26a9` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26aa` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26ab` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26ac` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26ad` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26ae` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26af` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26b0` | — | med | auto-shape | string-table (written by AFINIT) |
| `0x26b4` | — | med | auto-shape | string-table (written by CCINIT) |
| `0x26b6` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26b7` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26b8` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26b9` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26bc` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26bd` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26be` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26bf` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26c0` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26c1` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26c2` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26c3` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26c4` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26c5` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26c6` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26c7` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26c8` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26d7` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26da` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26db` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26dd` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26de` | — | med | auto-shape | string-table (written by LAINIT) |
| `0x26f2` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26f3` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26f4` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26f5` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26f6` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26f7` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26f8` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26f9` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26fa` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26fb` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26fc` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26fd` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26fe` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x26ff` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2700` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2701` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2702` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2703` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2704` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2705` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2706` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2707` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2708` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2709` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x270a` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x270b` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x270c` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x270d` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x270e` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x270f` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2710` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2711` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2712` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2713` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2714` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2715` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2716` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2717` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2718` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2719` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x271a` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x271b` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x271c` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x271d` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x271e` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x271f` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2756` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2757` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2758` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2759` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x275a` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x275b` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x275e` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x275f` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2761` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2762` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2763` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2764` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2765` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2766` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2767` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2768` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2769` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x276a` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x276b` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x276c` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x276d` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x276e` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x276f` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2770` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2771` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2772` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2773` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2774` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2775` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2776` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2779` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x277a` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2782` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x2783` | — | med | auto-shape | string-table (written by OBINIT) |
| `0x27be` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27bf` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27c0` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27c8` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27c9` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27ca` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27cc` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27cd` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27d2` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27d3` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27d4` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27d5` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27d6` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27d7` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27d8` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27dc` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27dd` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27de` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27df` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27e0` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27e1` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27e2` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27e3` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27e4` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27e6` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27e7` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27e8` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27e9` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27ea` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27eb` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27f0` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27f1` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27f2` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27f3` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27f4` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27f5` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27fa` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27fb` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27fc` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x27fd` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2804` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2805` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x280e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x280f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2810` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2816` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2818` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2819` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x281a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x281b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x281c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x281d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x281e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x281f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2822` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2823` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2824` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2825` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2826` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2827` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2828` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2829` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x282a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x285d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x285e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x285f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2860` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2861` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2862` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2863` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2864` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2865` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2866` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2867` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bab` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bac` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bae` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2baf` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bb1` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bb2` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bb3` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bb4` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bb5` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bb6` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bb7` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bb8` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bba` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bbb` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2be7` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2be8` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bea` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bed` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bee` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bf0` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bf3` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bf4` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bf6` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2bff` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c00` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c02` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c05` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c06` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c08` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c23` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c24` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c25` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c26` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c27` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c29` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c2a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c2c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c2d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c2f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c30` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c32` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c35` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c36` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c38` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c39` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c3b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c3c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c3e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c3f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c41` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c42` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c44` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c47` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c48` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c4a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c4b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c5f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c60` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c62` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c65` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c66` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c67` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c68` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c6b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c6c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c6d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c6e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c71` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c72` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c73` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c74` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c77` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c78` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c79` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c7a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c7d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c7e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c7f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c80` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c83` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c84` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c86` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c89` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c8a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c8b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c8c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c8d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c8f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c90` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c92` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c93` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c9b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c9c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2c9e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ca1` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ca2` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ca4` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ca7` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ca8` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ca9` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2caa` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cad` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cae` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cb0` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cb1` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cb3` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cb4` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cb6` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cb7` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cb9` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cba` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cbc` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cbd` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cd7` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cd8` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cda` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cdd` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cde` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ce0` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ce3` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ce4` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ce6` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ce9` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cea` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cec` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cef` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cf0` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cf1` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cf2` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cf5` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cf6` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cf7` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cf8` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2cf9` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d13` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d14` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d16` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d17` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d19` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d1a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d1c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d1f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d20` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d22` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d25` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d26` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d28` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d29` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d4f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d50` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d52` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d55` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d56` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d58` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d8b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d8c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d8e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d91` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d92` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d93` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d94` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d97` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d98` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d99` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2d9a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dbb` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dbc` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dbe` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dbf` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dc7` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dc8` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dc9` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dca` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dcd` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dce` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dcf` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dd0` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dd3` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dd4` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dd5` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dd6` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dd9` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dda` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ddb` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ddc` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ddf` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2de0` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2de1` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2de2` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2de5` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2de6` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2de7` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2de8` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2deb` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dec` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2ded` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2dee` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2df1` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2df2` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2df3` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2df4` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e03` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e04` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e06` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e07` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e09` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e0a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e0b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e0c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e0d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e0e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e0f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e10` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e12` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e13` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e15` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e16` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e17` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e18` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e19` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e1a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e1b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e1c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e1d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e1e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e1f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e20` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e21` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e22` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e23` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e24` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e25` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e26` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e27` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e28` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e2a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e2b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e2d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e2e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e30` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e31` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e33` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e34` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e35` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e36` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e37` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2e38` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f65` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f66` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f67` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f68` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f6b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f6c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f6d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f6e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f71` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f72` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f74` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f77` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f78` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f79` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f7a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f7d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f7e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f7f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f80` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f83` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f84` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f85` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f86` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f89` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f8a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f8b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f8c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f8f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f90` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f91` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f92` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f95` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f96` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f97` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f98` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f99` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f9a` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f9b` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f9c` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f9d` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f9e` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2f9f` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2fa0` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2fa1` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2fa2` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2fa3` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2fa4` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2fa5` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x2fa6` | — | med | auto-shape | string-table (written by STINIT2) |
| `0x4316` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4317` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4318` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4319` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x431a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x431b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x431c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x431d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x431e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x431f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4320` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4321` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4322` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4323` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4324` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4325` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4326` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4327` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4328` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4329` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x432a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x432b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x432c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x432d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x432e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x432f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4330` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4331` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4332` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4333` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4334` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4335` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4336` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4337` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4338` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4339` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x433a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x433b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x433c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x433d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x433e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x433f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4340` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4341` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4342` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4343` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4344` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4345` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4346` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4347` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4348` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4349` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x434a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x434b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x434c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x434d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x434e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x434f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4350` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4351` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4352` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4353` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4354` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4355` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4356` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4357` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4358` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4359` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x435a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x435b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x435c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x435d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x435e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x435f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4360` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4361` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4362` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4363` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4364` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4365` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4366` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x437a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x437b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x437c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x437d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x437e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x437f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4380` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4381` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4382` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4383` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4384` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4385` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4386` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4387` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4388` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4389` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x438a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x438b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x438c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x438d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x438e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x438f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4390` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4391` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4392` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4393` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4394` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4395` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4396` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4397` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4398` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x4399` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x439a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x439b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x439c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x439d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x439e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x439f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43a0` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43a1` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43a2` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43a3` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43a4` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43a5` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43a6` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43a7` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43a8` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43a9` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43aa` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43ab` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43ac` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43ad` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43ae` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43af` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43b0` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43b1` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43b2` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43b3` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43b4` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43b5` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43b6` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43b7` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43b8` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43b9` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43ba` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43bb` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43bc` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43bd` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43be` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43bf` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43c0` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43c1` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43c2` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43c3` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43c4` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43c5` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43c6` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43c7` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43c8` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43c9` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43ca` | — | med | auto-shape | string-table (written by CDINIT2) |
| `0x43dd` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43de` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43df` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43e0` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43e1` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43e2` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43e3` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43e4` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43e5` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43e6` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43e7` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43e8` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43e9` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43ea` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43eb` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43ec` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43ed` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43ef` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43f1` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43f2` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43f3` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43f4` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43f5` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43f6` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43f7` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43f8` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43f9` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43fa` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43fb` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43fc` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43fd` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43fe` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x43ff` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4400` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4401` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4403` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4405` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4406` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4407` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4408` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4409` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x440a` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x440b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x440c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x440d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x440e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x440f` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4410` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4411` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4412` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4413` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4414` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4415` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4416` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4417` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4418` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4423` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4424` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4425` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4426` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4427` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4428` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4429` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x442a` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x442b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x442c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x442d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x442e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x442f` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4430` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4431` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4432` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4433` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4435` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4437` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4438` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4439` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x443a` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x443b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x443c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x443d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x443e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x443f` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4440` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4441` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4442` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4443` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4444` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4445` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4446` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4447` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4449` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x444b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x444c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x444d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x444e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x444f` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4450` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4451` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4452` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4453` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4454` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4455` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4456` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4457` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4458` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4459` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x445a` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x445b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x445c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x445d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x445e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4469` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x446a` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x446b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x446c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x446d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x446e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x446f` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4470` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4471` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4472` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4473` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4474` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4475` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4476` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4477` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4478` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4479` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x447a` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x447b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x447c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x447d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x447e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x447f` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4480` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4481` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4482` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4487` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4488` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4489` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x448a` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x448b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x448c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x448d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x448e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x448f` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4490` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4491` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4492` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4493` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4494` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4495` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4496` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4497` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4498` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4499` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x449a` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x449b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x449c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x449d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x449e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x449f` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44a0` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44af` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44b0` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44b1` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44b2` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44b3` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44b4` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44b5` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44b6` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44b7` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44b8` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44b9` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44ba` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44bb` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44bc` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44bd` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44be` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44bf` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44c0` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44c1` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44c2` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44c3` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44c4` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44c5` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44c6` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44c7` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44c8` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44c9` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44ca` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44cb` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44cc` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44cd` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44ce` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44cf` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44d0` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44d1` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44d2` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44d3` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44d4` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44d5` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44d6` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44f5` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44f6` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44f7` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44f8` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44f9` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44fa` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44fb` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44fc` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44fd` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44fe` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x44ff` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4500` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4501` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4502` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4503` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4504` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4505` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4506` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4507` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4508` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4509` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x450a` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x450b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x450c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x450d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x450e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x450f` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4510` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4511` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4512` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4513` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4514` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4515` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4516` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4517` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4518` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4519` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x451a` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x451b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x451c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x451d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x451e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x451f` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4520` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4521` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4522` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4523` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4524` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4525` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4526` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4527` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4528` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4529` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x452a` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x452b` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x452c` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x452d` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x452e` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x452f` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4530` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4531` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4532` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4533` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4534` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4535` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4536` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4537` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4538` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x4539` | — | med | auto-shape | string-table (written by CTINIT) |
| `0x453b` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x453c` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4541` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4542` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4544` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4545` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4546` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4547` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4548` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x454a` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x454b` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x454d` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x454e` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x454f` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4550` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4553` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4554` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4556` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4557` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4559` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x455a` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x455c` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x455f` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4560` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4562` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4565` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4566` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x456b` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x456c` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x456e` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4571` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4572` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4574` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4575` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4577` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4578` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x457a` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x457b` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x457d` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x457e` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4580` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4581` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4583` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4584` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4586` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4589` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x458a` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x458c` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x458d` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x458f` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4590` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4595` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4596` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4597` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x4598` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x459b` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x459c` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x459d` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x459e` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x459f` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45a1` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45a2` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45a4` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45a5` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45a7` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45a8` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45aa` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45ab` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45ad` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45ae` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45b0` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45b3` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45b4` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45b6` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45b7` | — | med | auto-shape | string-table (written by TRINIT) |
| `0x45ba` | — | med | auto-shape | string-table (written by MAINIT) |
| `0x45bb` | — | med | auto-shape | string-table (written by MAINIT) |
| `0x45bc` | — | med | auto-shape | string-table (written by MAINIT) |
| `0x45bd` | — | med | auto-shape | string-table (written by MAINIT) |
| `0x45be` | — | med | auto-shape | string-table (written by MAINIT) |
| `0x45bf` | — | med | auto-shape | string-table (written by MAINIT) |
| `0x45c0` | — | med | auto-shape | string-table (written by MAINIT) |
| `0x45c1` | — | med | auto-shape | string-table (written by MAINIT) |
| `0x45c2` | — | med | auto-shape | string-table (written by MAINIT) |
| `0x45c3` | — | med | auto-shape | string-table (written by MAINIT) |
| `0x45c4` | — | med | auto-shape | string-table (written by MAINIT) |
| `0x45d8` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45d9` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45da` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45db` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45dc` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45dd` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45de` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45df` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45e0` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45e1` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45e2` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45e3` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45e4` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45e5` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45e6` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45e7` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45e8` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45e9` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45ea` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45eb` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45ec` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45ed` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45ee` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x45ef` | — | med | auto-shape | string-table (written by CIINIT) |
| `0x463c` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x463d` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x463e` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x463f` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4640` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4641` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4642` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4643` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4644` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4645` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4646` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4647` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4648` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4649` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x464a` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x464b` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x464c` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x464d` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x464e` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x464f` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4650` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4651` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4652` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4653` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4654` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4655` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4656` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4657` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4658` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4659` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x465a` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x465b` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x465c` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x465d` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x465e` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x465f` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x466d` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x466e` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x466f` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4670` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4671` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4672` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4673` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4681` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4682` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4683` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4684` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4685` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4686` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4687` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x4688` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x469f` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46a0` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46a1` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46a2` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46a3` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46a4` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46a5` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46a6` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46a7` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46a8` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46a9` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46aa` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46ab` | — | med | auto-shape | string-table (written by VIINIT) |
| `0x46b3` | — | med | auto-shape | string-table (written by VIINIT) |

## ui-toggle

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x6be` | — | low | inference | ADV message-window / text-render state in the 0x6bx-0x6cx chrome cluster (passed to render helpers u0041F9C0/u00415F70 alongside 0x6c3). NOT a story flag — high scene-reach is from the shared render chrome. Branch-read in 146 scenes. |
| `0x6c1` | adv_chrome_enabled | high | investigation | Inherited SYSTEM4 UI-boot flag. Standard ADV scripts register the five visible SO001 control-strip pointer rectangles only while nonzero; zero skips directly to the three off-screen keyboard/pad records. The Phase-A single-scene Godot bootstrap seeds the native observed value 1. |
| `0x6c3` | — | low | inference | ADV message-window / text-render state in the 0x6bx-0x6cx chrome cluster (`mov 0x6c3,<val>` then `u00415F70(0x6c3)` right after draw-texture; paired with 0x6be). NOT a story flag. Branch-read in 136 scenes. |
| `0x6c9` | adv_hover_history | high | investigation | Pointer-hover flag for the standard ADV History button at (684,572). Its op 0x90 enter/leave callbacks set 1/0; the shared SO001 redraw shows the History tooltip plus generic hover overlay while set. |
| `0x6ca` | adv_hover_auto_message | high | investigation | Pointer-hover flag for the standard ADV Auto-message button at (706,572). See adv_hover_history. |
| `0x6cb` | adv_hover_message_skip | high | investigation | Pointer-hover flag for the standard ADV all-message Skip button at (728,572). See adv_hover_history. |
| `0x6cc` | adv_hover_read_message_skip | high | investigation | Pointer-hover flag for the standard ADV read-message-only Skip button at (750,572). See adv_hover_history. |
| `0x6cd` | adv_hover_hide_window | high | investigation | Pointer-hover flag for the standard ADV Hide-window button at (772,572). See adv_hover_history. |
| `0x62425` | adv_hide_window_enabled | high | investigation | Native ADV-scheduler permission for the standard Hide Window action. After op 0x199 enters the registered yield-A handler, every standard ADV scene calls HIDEWIN.BIN only while this value is nonzero. No script writes it and the complete boot-to-SC0000 VM-write capture does not contain it, so it is native-owned inherited state rather than saved-game or script boot data. The Godot scene bootstrap mirrors the original enabled value 1. |

## unknown

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x3238` | — | low | inference | Config/settings global (CONFIG.BIN/INITCONFIG.BIN writer, scene-reach 0) — NOT a scene story flag; miner over-tagged it. Branch-read in 5 scripts; compared against [1]. |
| `0x3301` | — | low | inference | Config/settings global (INITCONFIG.BIN writer, scene-reach 0) — NOT a scene story flag; miner over-tagged it. Branch-read in 6 scripts; compared against [1, 2]. |
| `0x3303` | — | low | inference | Config/settings global (CONFIG.BIN/INITCONFIG.BIN writer, scene-reach 0) — NOT a scene story flag; miner over-tagged it. Branch-read in 8 scripts; compared against [1]. |
| `0x3304` | — | low | inference | Config/settings global (CONFIG.BIN/INITCONFIG.BIN writer, scene-reach 0) — NOT a scene story flag; miner over-tagged it. Branch-read in 13 scripts; compared against [1]. |
| `0x204f4` | — | low | inference | Map/field display state (FIELD.BIN writer; used in DRAWCHP draw call u00422460 and `div 0x204f4/3`). A numeric display parameter, NOT a story flag. Branch-read in 2 scenes. |
| `0x6249e` | — | low | inference | Graphics-subsystem state in the 0x624xx gfx-object range (set via `mov 0x6249e,<n>` alongside `mov 0x62450,<slot>`). Likely a draw/slot parameter, NOT a story flag. Branch-read in 78 scenes. |
| `0x84a` | — | high | auto-shape | unit-name-table |
| `0xc32` | — | high | auto-shape | unit-desc-table |
| `0x101a` | — | high | auto-shape | unit-desc1-table |
| `0x14ca` | — | high | auto-shape | unit-desc2-table |
| `0x14cb` | — | high | auto-shape | unit-desc3-table |
| `0x1bd2` | — | high | auto-shape | item-name-table |
| `0x1fba` | — | high | auto-shape | item-desc-table |
| `0x23a2` | — | high | auto-shape | skill-name-table |
| `0x24ce` | — | high | auto-shape | skill-desc-table |
| `0x25fa` | — | med | auto-shape | record-table[stride 5] |
| `0x2ba5` | — | med | auto-shape | record-table[stride 6] |
| `0x3239` | — | med | auto-shape | record-table[stride 3] |
| `0x32f0` | — | med | auto-shape | current-entity-index? |
| `0x3ebe` | — | med | auto-shape | record-table[stride 10] |
| `0x69f8` | — | med | auto-shape | record-table[stride 14] |
| `0x6f70` | — | med | auto-shape | record-table[stride 14] |
| `0x74e8` | — | med | auto-shape | record-table[stride 4] |
| `0x7e54` | — | med | auto-shape | record-table[stride 50] |
| `0x20543` | — | med | auto-shape | record-table[stride 27] |
| `0x341ab` | — | med | auto-shape | record-table[stride 53] |
| `0x4e085` | — | med | auto-shape | record-table[stride 3] |
| `0x4e11b` | — | med | auto-shape | record-table[stride 14] |
| `0x4e3d7` | — | med | auto-shape | record-table[stride 14] |
| `0x4e693` | — | med | auto-shape | record-table[stride 300] |
| `0x5212b` | — | med | auto-shape | record-table[stride 4] |
| `0x52383` | — | med | auto-shape | record-table[stride 30] |
| `0x5295f` | — | med | auto-shape | record-table[stride 30] |
| `0x52f3b` | — | med | auto-shape | record-table[stride 30] |
| `0x53517` | — | med | auto-shape | record-table[stride 3] |
| `0x535ad` | — | med | auto-shape | record-table[stride 3] |
| `0x53643` | — | med | auto-shape | record-table[stride 20] |
| `0x53a2b` | — | med | auto-shape | record-table[stride 20] |
| `0x53e13` | — | med | auto-shape | record-table[stride 2] |
| `0x53e77` | — | med | auto-shape | record-table[stride 2] |
| `0x53edd` | — | low | auto-shape | index/counter? |
| `0x53ef5` | — | low | auto-shape | index/counter? |
| `0x53ef7` | — | med | auto-shape | record-table[stride 8] |
| `0x56738` | — | med | auto-shape | record-table[stride 20] |
| `0x57356` | — | med | auto-shape | current-entity-index? |
| `0x573bb` | — | med | auto-shape | record-table[stride 10] |
| `0x62436` | — | low | auto-shape | index/counter? |
| `0x62450` | — | low | auto-shape | index/counter? |
| `0x62ccb` | — | low | auto-shape | index/counter? |
| `0x62cd1` | — | med | auto-shape | record-table[stride 2] |
| `0x62cd3` | — | low | auto-shape | cg-gallery-field? |
| `0x62cd5` | — | low | auto-shape | cg-gallery-field? |
| `0x62cd7` | — | low | auto-shape | cg-gallery-field? |
| `0x62cd9` | — | low | auto-shape | cg-gallery-field? |
| `0x62cda` | — | low | auto-shape | cg-gallery-field? |
| `0x62cdb` | — | low | auto-shape | cg-gallery-field? |
| `0x62cdc` | — | low | auto-shape | cg-gallery-field? |
| `0x62cdd` | — | low | auto-shape | cg-gallery-field? |
| `0x62cde` | — | low | auto-shape | cg-gallery-field? |
| `0x62cdf` | — | low | auto-shape | cg-gallery-field? |
| `0x62ce0` | — | low | auto-shape | cg-gallery-field? |
| `0x62ce1` | — | low | auto-shape | cg-gallery-field? |
| `0x62ce2` | — | low | auto-shape | cg-gallery-field? |
| `0x62ce3` | — | low | auto-shape | cg-gallery-field? |
| `0x62ce5` | — | low | auto-shape | cg-gallery-field? |
| `0x62ce7` | — | low | auto-shape | cg-gallery-field? |
| `0x62ce9` | — | low | auto-shape | cg-gallery-field? |
| `0x62ceb` | — | low | auto-shape | cg-gallery-field? |
| `0x62ced` | — | low | auto-shape | cg-gallery-field? |
| `0x62cef` | — | low | auto-shape | cg-gallery-field? |
| `0x62cf1` | — | low | auto-shape | cg-gallery-field? |
| `0x62cf2` | — | low | auto-shape | cg-gallery-field? |
| `0x62cf3` | — | low | auto-shape | cg-gallery-field? |
| `0x62cf4` | — | low | auto-shape | cg-gallery-field? |
| `0x62cf5` | — | low | auto-shape | cg-gallery-field? |
| `0x62cf6` | — | low | auto-shape | cg-gallery-field? |
| `0x62cf7` | — | low | auto-shape | cg-gallery-field? |
| `0x62cf8` | — | low | auto-shape | cg-gallery-field? |
| `0x62cf9` | — | low | auto-shape | cg-gallery-field? |
| `0x62cfb` | — | low | auto-shape | cg-gallery-field? |
| `0x62cfd` | — | low | auto-shape | cg-gallery-field? |
| `0x62cff` | — | low | auto-shape | cg-gallery-field? |
| `0x62d01` | — | low | auto-shape | cg-gallery-field? |
| `0x62d03` | — | low | auto-shape | cg-gallery-field? |
| `0x62d05` | — | low | auto-shape | cg-gallery-field? |
| `0x62d07` | — | low | auto-shape | cg-gallery-field? |
| `0x62d09` | — | low | auto-shape | cg-gallery-field? |
| `0x62d0b` | — | low | auto-shape | cg-gallery-field? |
| `0x62d0c` | — | low | auto-shape | cg-gallery-field? |
| `0x62d0d` | — | low | auto-shape | cg-gallery-field? |
| `0x62d0e` | — | low | auto-shape | cg-gallery-field? |
| `0x62d0f` | — | low | auto-shape | cg-gallery-field? |
| `0x62d10` | — | low | auto-shape | cg-gallery-field? |
| `0x62d11` | — | low | auto-shape | cg-gallery-field? |
| `0x62d12` | — | low | auto-shape | cg-gallery-field? |
| `0x62d13` | — | low | auto-shape | cg-gallery-field? |
| `0x62d14` | — | low | auto-shape | cg-gallery-field? |
| `0x62d15` | — | low | auto-shape | cg-gallery-field? |
| `0x62d16` | — | low | auto-shape | cg-gallery-field? |
| `0x62d17` | — | low | auto-shape | cg-gallery-field? |
| `0x62d18` | — | low | auto-shape | cg-gallery-field? |
| `0x62d19` | — | low | auto-shape | cg-gallery-field? |
| `0x62d1a` | — | low | auto-shape | cg-gallery-field? |
| `0x62d1b` | — | low | auto-shape | cg-gallery-field? |
| `0x62d1c` | — | low | auto-shape | cg-gallery-field? |
| `0x62d1d` | — | low | auto-shape | cg-gallery-field? |
| `0x62d1e` | — | low | auto-shape | cg-gallery-field? |
| `0x62d1f` | — | low | auto-shape | cg-gallery-field? |
| `0x62d21` | — | low | auto-shape | cg-gallery-field? |
| `0x62d23` | — | low | auto-shape | cg-gallery-field? |
| `0x62d24` | — | low | auto-shape | cg-gallery-field? |
| `0x62d25` | — | low | auto-shape | cg-gallery-field? |
| `0x62d26` | — | low | auto-shape | cg-gallery-field? |
| `0x62d27` | — | low | auto-shape | cg-gallery-field? |
| `0x62d28` | — | low | auto-shape | cg-gallery-field? |
| `0x62d29` | — | low | auto-shape | cg-gallery-field? |
| `0x62d2a` | — | low | auto-shape | cg-gallery-field? |
| `0x62d2b` | — | low | auto-shape | cg-gallery-field? |
| `0x62d2c` | — | low | auto-shape | cg-gallery-field? |
| `0x62d2d` | — | low | auto-shape | cg-gallery-field? |
| `0x62d2e` | — | low | auto-shape | cg-gallery-field? |
| `0x62d2f` | — | low | auto-shape | cg-gallery-field? |
| `0x62d30` | — | low | auto-shape | cg-gallery-field? |
| `0x62d31` | — | low | auto-shape | cg-gallery-field? |
| `0x62d32` | — | low | auto-shape | cg-gallery-field? |
| `0x62d33` | — | low | auto-shape | cg-gallery-field? |
| `0x62d34` | — | low | auto-shape | cg-gallery-field? |
| `0x62d35` | — | low | auto-shape | cg-gallery-field? |
| `0x62d37` | — | low | auto-shape | cg-gallery-field? |
| `0x62d39` | — | low | auto-shape | cg-gallery-field? |
| `0x62d3b` | — | low | auto-shape | cg-gallery-field? |
| `0x62d3c` | — | low | auto-shape | cg-gallery-field? |
| `0x62d3d` | — | low | auto-shape | cg-gallery-field? |
| `0x62d3e` | — | low | auto-shape | cg-gallery-field? |
| `0x62d3f` | — | low | auto-shape | cg-gallery-field? |
| `0x62d40` | — | low | auto-shape | cg-gallery-field? |
| `0x62d41` | — | low | auto-shape | cg-gallery-field? |
| `0x62d42` | — | low | auto-shape | cg-gallery-field? |
| `0x62d43` | — | low | auto-shape | cg-gallery-field? |
| `0x62d45` | — | low | auto-shape | cg-gallery-field? |
| `0x62d47` | — | low | auto-shape | cg-gallery-field? |
| `0x62d48` | — | low | auto-shape | cg-gallery-field? |
| `0x62d49` | — | low | auto-shape | cg-gallery-field? |
| `0x62d4a` | — | low | auto-shape | cg-gallery-field? |
| `0x62d4b` | — | low | auto-shape | cg-gallery-field? |
| `0x62d4c` | — | low | auto-shape | cg-gallery-field? |
| `0x62d4d` | — | low | auto-shape | cg-gallery-field? |
| `0x62d4e` | — | low | auto-shape | cg-gallery-field? |
| `0x62d4f` | — | low | auto-shape | cg-gallery-field? |
| `0x62d50` | — | low | auto-shape | cg-gallery-field? |
| `0x62d51` | — | low | auto-shape | cg-gallery-field? |
| `0x62d53` | — | low | auto-shape | cg-gallery-field? |
| `0x62d54` | — | low | auto-shape | cg-gallery-field? |
| `0x62d55` | — | low | auto-shape | cg-gallery-field? |
| `0x62d56` | — | low | auto-shape | cg-gallery-field? |
| `0x62d57` | — | low | auto-shape | cg-gallery-field? |
| `0x62d58` | — | low | auto-shape | cg-gallery-field? |
| `0x62d59` | — | low | auto-shape | cg-gallery-field? |
| `0x62d5a` | — | low | auto-shape | cg-gallery-field? |
| `0x62d5b` | — | low | auto-shape | cg-gallery-field? |
| `0x62d5c` | — | low | auto-shape | cg-gallery-field? |
| `0x62d5d` | — | low | auto-shape | cg-gallery-field? |
| `0x62d5e` | — | low | auto-shape | cg-gallery-field? |
| `0x62d5f` | — | low | auto-shape | cg-gallery-field? |
| `0x62d60` | — | low | auto-shape | cg-gallery-field? |
| `0x62d61` | — | low | auto-shape | cg-gallery-field? |
| `0x62d62` | — | low | auto-shape | cg-gallery-field? |
| `0x62d63` | — | low | auto-shape | cg-gallery-field? |
| `0x62d64` | — | low | auto-shape | cg-gallery-field? |
| `0x62d65` | — | low | auto-shape | cg-gallery-field? |
| `0x62d67` | — | low | auto-shape | cg-gallery-field? |
| `0x62d69` | — | low | auto-shape | cg-gallery-field? |
| `0x62d6b` | — | low | auto-shape | cg-gallery-field? |
| `0x62d6c` | — | low | auto-shape | cg-gallery-field? |
| `0x62d6d` | — | low | auto-shape | cg-gallery-field? |
| `0x62d6e` | — | low | auto-shape | cg-gallery-field? |
| `0x62d6f` | — | low | auto-shape | cg-gallery-field? |
| `0x62d70` | — | low | auto-shape | cg-gallery-field? |
| `0x62d71` | — | low | auto-shape | cg-gallery-field? |
| `0x62d72` | — | low | auto-shape | cg-gallery-field? |
| `0x62d73` | — | low | auto-shape | cg-gallery-field? |
| `0x62d75` | — | low | auto-shape | cg-gallery-field? |
| `0x62d77` | — | low | auto-shape | cg-gallery-field? |
| `0x62d79` | — | low | auto-shape | cg-gallery-field? |
| `0x62d7b` | — | low | auto-shape | cg-gallery-field? |
| `0x62d7d` | — | low | auto-shape | cg-gallery-field? |
| `0x62d7f` | — | low | auto-shape | cg-gallery-field? |
| `0x62d81` | — | low | auto-shape | cg-gallery-field? |
| `0x62d82` | — | low | auto-shape | cg-gallery-field? |
| `0x62d83` | — | low | auto-shape | cg-gallery-field? |
| `0x62d84` | — | low | auto-shape | cg-gallery-field? |
| `0x62d85` | — | low | auto-shape | cg-gallery-field? |
| `0x62d86` | — | low | auto-shape | cg-gallery-field? |
| `0x62d87` | — | low | auto-shape | cg-gallery-field? |
| `0x62d88` | — | low | auto-shape | cg-gallery-field? |
| `0x62d89` | — | low | auto-shape | cg-gallery-field? |
| `0x62d8a` | — | low | auto-shape | cg-gallery-field? |
| `0x62d8b` | — | low | auto-shape | cg-gallery-field? |
| `0x62d8d` | — | low | auto-shape | cg-gallery-field? |
| `0x62d8f` | — | low | auto-shape | cg-gallery-field? |
| `0x62d91` | — | low | auto-shape | cg-gallery-field? |
| `0x62d93` | — | low | auto-shape | cg-gallery-field? |
| `0x62d95` | — | low | auto-shape | cg-gallery-field? |
| `0x62d97` | — | low | auto-shape | cg-gallery-field? |
| `0x62d99` | — | low | auto-shape | cg-gallery-field? |
| `0x62d9b` | — | low | auto-shape | cg-gallery-field? |
| `0x62d9d` | — | low | auto-shape | cg-gallery-field? |
| `0x62d9f` | — | low | auto-shape | cg-gallery-field? |
| `0x62da1` | — | low | auto-shape | cg-gallery-field? |
| `0x62da3` | — | low | auto-shape | cg-gallery-field? |
| `0x62da5` | — | low | auto-shape | cg-gallery-field? |
| `0x62da7` | — | low | auto-shape | cg-gallery-field? |
| `0x62da9` | — | low | auto-shape | cg-gallery-field? |
| `0x62dab` | — | low | auto-shape | cg-gallery-field? |
| `0x62dad` | — | low | auto-shape | cg-gallery-field? |
| `0x62dae` | — | low | auto-shape | cg-gallery-field? |
| `0x62daf` | — | low | auto-shape | cg-gallery-field? |
| `0x62db0` | — | low | auto-shape | cg-gallery-field? |
| `0x62db1` | — | low | auto-shape | cg-gallery-field? |
| `0x62db2` | — | low | auto-shape | cg-gallery-field? |
| `0x62db3` | — | low | auto-shape | cg-gallery-field? |
| `0x62db4` | — | low | auto-shape | cg-gallery-field? |
| `0x62db5` | — | low | auto-shape | cg-gallery-field? |
| `0x62db6` | — | low | auto-shape | cg-gallery-field? |
| `0x62db7` | — | low | auto-shape | cg-gallery-field? |
| `0x62db8` | — | low | auto-shape | cg-gallery-field? |
| `0x62db9` | — | low | auto-shape | cg-gallery-field? |
| `0x62dba` | — | low | auto-shape | cg-gallery-field? |
| `0x62dbb` | — | low | auto-shape | cg-gallery-field? |
| `0x62dbc` | — | low | auto-shape | cg-gallery-field? |
| `0x62dbd` | — | low | auto-shape | cg-gallery-field? |
| `0x62dbe` | — | low | auto-shape | cg-gallery-field? |
| `0x62dbf` | — | low | auto-shape | cg-gallery-field? |
| `0x62dc0` | — | low | auto-shape | cg-gallery-field? |
| `0x62dc1` | — | low | auto-shape | cg-gallery-field? |
| `0x62dc3` | — | low | auto-shape | cg-gallery-field? |
| `0x62dc5` | — | low | auto-shape | cg-gallery-field? |
| `0x62dc7` | — | low | auto-shape | cg-gallery-field? |
| `0x62dc9` | — | low | auto-shape | cg-gallery-field? |
| `0x62dca` | — | low | auto-shape | cg-gallery-field? |
| `0x62dcb` | — | low | auto-shape | cg-gallery-field? |
| `0x62dcc` | — | low | auto-shape | cg-gallery-field? |
| `0x62dcd` | — | low | auto-shape | cg-gallery-field? |
| `0x62dce` | — | low | auto-shape | cg-gallery-field? |
| `0x62dcf` | — | low | auto-shape | cg-gallery-field? |
| `0x62dd0` | — | low | auto-shape | cg-gallery-field? |
| `0x62dd1` | — | low | auto-shape | cg-gallery-field? |
| `0x62dd2` | — | low | auto-shape | cg-gallery-field? |
| `0x62dd3` | — | low | auto-shape | cg-gallery-field? |
| `0x62dd4` | — | low | auto-shape | cg-gallery-field? |
| `0x62dd5` | — | low | auto-shape | cg-gallery-field? |
| `0x62dd6` | — | low | auto-shape | cg-gallery-field? |
| `0x62dd7` | — | low | auto-shape | cg-gallery-field? |
| `0x62dd8` | — | low | auto-shape | cg-gallery-field? |
| `0x62dd9` | — | low | auto-shape | cg-gallery-field? |
| `0x62dda` | — | low | auto-shape | cg-gallery-field? |
| `0x62ddb` | — | low | auto-shape | cg-gallery-field? |
| `0x62ddc` | — | low | auto-shape | cg-gallery-field? |
| `0x62ddd` | — | low | auto-shape | cg-gallery-field? |
| `0x62ddf` | — | low | auto-shape | cg-gallery-field? |
| `0x62de1` | — | low | auto-shape | cg-gallery-field? |
| `0x62de3` | — | low | auto-shape | cg-gallery-field? |
| `0x62de5` | — | low | auto-shape | cg-gallery-field? |
| `0x62de7` | — | low | auto-shape | cg-gallery-field? |
| `0x62de9` | — | low | auto-shape | cg-gallery-field? |
| `0x62deb` | — | low | auto-shape | cg-gallery-field? |
| `0x62dec` | — | low | auto-shape | cg-gallery-field? |
| `0x62ded` | — | low | auto-shape | cg-gallery-field? |
| `0x62dee` | — | low | auto-shape | cg-gallery-field? |
| `0x62def` | — | low | auto-shape | cg-gallery-field? |
| `0x62df1` | — | low | auto-shape | cg-gallery-field? |
| `0x62df3` | — | low | auto-shape | cg-gallery-field? |
| `0x62df4` | — | low | auto-shape | cg-gallery-field? |
| `0x62df5` | — | low | auto-shape | cg-gallery-field? |
| `0x62df6` | — | low | auto-shape | cg-gallery-field? |
| `0x62df7` | — | low | auto-shape | cg-gallery-field? |
| `0x62df8` | — | low | auto-shape | cg-gallery-field? |
| `0x62df9` | — | low | auto-shape | cg-gallery-field? |
| `0x62dfa` | — | low | auto-shape | cg-gallery-field? |
| `0x62dfb` | — | low | auto-shape | cg-gallery-field? |
| `0x62dfc` | — | low | auto-shape | cg-gallery-field? |
| `0x62dfd` | — | low | auto-shape | cg-gallery-field? |
| `0x62dfe` | — | low | auto-shape | cg-gallery-field? |
| `0x62dff` | — | low | auto-shape | cg-gallery-field? |
| `0x62e00` | — | low | auto-shape | cg-gallery-field? |
| `0x62e01` | — | low | auto-shape | cg-gallery-field? |
| `0x62e02` | — | low | auto-shape | cg-gallery-field? |
| `0x62e03` | — | low | auto-shape | cg-gallery-field? |
| `0x62e04` | — | low | auto-shape | cg-gallery-field? |
| `0x62e05` | — | low | auto-shape | cg-gallery-field? |
| `0x62e06` | — | low | auto-shape | cg-gallery-field? |
| `0x62e07` | — | low | auto-shape | cg-gallery-field? |
| `0x62e08` | — | low | auto-shape | cg-gallery-field? |
| `0x62e09` | — | low | auto-shape | cg-gallery-field? |
| `0x62e0a` | — | low | auto-shape | cg-gallery-field? |
| `0x62e0b` | — | low | auto-shape | cg-gallery-field? |
| `0x62e0c` | — | low | auto-shape | cg-gallery-field? |
| `0x62e0d` | — | low | auto-shape | cg-gallery-field? |
| `0x62e0e` | — | low | auto-shape | cg-gallery-field? |
| `0x62e0f` | — | low | auto-shape | cg-gallery-field? |
| `0x62e10` | — | low | auto-shape | cg-gallery-field? |
| `0x62e11` | — | low | auto-shape | cg-gallery-field? |
| `0x62e12` | — | low | auto-shape | cg-gallery-field? |
| `0x62e13` | — | low | auto-shape | cg-gallery-field? |
| `0x62e14` | — | low | auto-shape | cg-gallery-field? |
| `0x62e15` | — | low | auto-shape | cg-gallery-field? |
| `0x62e16` | — | low | auto-shape | cg-gallery-field? |
| `0x62e17` | — | low | auto-shape | cg-gallery-field? |
| `0x62e18` | — | low | auto-shape | cg-gallery-field? |
| `0x62e19` | — | low | auto-shape | cg-gallery-field? |
| `0x62e1a` | — | low | auto-shape | cg-gallery-field? |
| `0x62e1b` | — | low | auto-shape | cg-gallery-field? |
| `0x62e1c` | — | low | auto-shape | cg-gallery-field? |
| `0x62e1d` | — | low | auto-shape | cg-gallery-field? |
| `0x62e1f` | — | low | auto-shape | cg-gallery-field? |
| `0x62e21` | — | low | auto-shape | cg-gallery-field? |
| `0x62e23` | — | low | auto-shape | cg-gallery-field? |
| `0x62e25` | — | low | auto-shape | cg-gallery-field? |
| `0x62e27` | — | low | auto-shape | cg-gallery-field? |
| `0x62e29` | — | low | auto-shape | cg-gallery-field? |
| `0x62e2b` | — | low | auto-shape | cg-gallery-field? |
| `0x62e2c` | — | low | auto-shape | cg-gallery-field? |
| `0x62e2d` | — | low | auto-shape | cg-gallery-field? |
| `0x62e2e` | — | low | auto-shape | cg-gallery-field? |
| `0x62e2f` | — | low | auto-shape | cg-gallery-field? |
| `0x62e30` | — | low | auto-shape | cg-gallery-field? |
| `0x62e31` | — | low | auto-shape | cg-gallery-field? |
| `0x62e32` | — | low | auto-shape | cg-gallery-field? |
| `0x62e33` | — | low | auto-shape | cg-gallery-field? |
| `0x62e34` | — | low | auto-shape | cg-gallery-field? |
| `0x62e35` | — | low | auto-shape | cg-gallery-field? |
| `0x62e36` | — | low | auto-shape | cg-gallery-field? |
| `0x62e37` | — | low | auto-shape | cg-gallery-field? |
| `0x62e38` | — | low | auto-shape | cg-gallery-field? |
| `0x62e39` | — | low | auto-shape | cg-gallery-field? |
| `0x62e3a` | — | low | auto-shape | cg-gallery-field? |
| `0x62e3b` | — | low | auto-shape | cg-gallery-field? |
| `0x62e3d` | — | low | auto-shape | cg-gallery-field? |
| `0x62e3f` | — | low | auto-shape | cg-gallery-field? |
| `0x62e41` | — | low | auto-shape | cg-gallery-field? |
| `0x62e43` | — | low | auto-shape | cg-gallery-field? |
| `0x62e45` | — | low | auto-shape | cg-gallery-field? |
| `0x62e47` | — | low | auto-shape | cg-gallery-field? |
| `0x62e49` | — | low | auto-shape | cg-gallery-field? |
| `0x62e4b` | — | low | auto-shape | cg-gallery-field? |
| `0x62e4d` | — | low | auto-shape | cg-gallery-field? |
| `0x62e4f` | — | low | auto-shape | cg-gallery-field? |
| `0x62e51` | — | low | auto-shape | cg-gallery-field? |
| `0x62e53` | — | low | auto-shape | cg-gallery-field? |
| `0x62e55` | — | low | auto-shape | cg-gallery-field? |
| `0x62e57` | — | low | auto-shape | cg-gallery-field? |
| `0x62e59` | — | low | auto-shape | cg-gallery-field? |
| `0x62e5b` | — | low | auto-shape | cg-gallery-field? |
| `0x62e5d` | — | low | auto-shape | cg-gallery-field? |
| `0x62e5f` | — | low | auto-shape | cg-gallery-field? |
| `0x62e61` | — | low | auto-shape | cg-gallery-field? |
| `0x62e63` | — | low | auto-shape | cg-gallery-field? |
| `0x62e65` | — | low | auto-shape | cg-gallery-field? |
| `0x62e67` | — | low | auto-shape | cg-gallery-field? |
| `0x62e69` | — | low | auto-shape | cg-gallery-field? |
| `0x62e6d` | — | low | auto-shape | cg-gallery-field? |
| `0x62e6e` | — | low | auto-shape | cg-gallery-field? |
| `0x62e6f` | — | low | auto-shape | cg-gallery-field? |
| `0x62e70` | — | low | auto-shape | cg-gallery-field? |
| `0x62e71` | — | low | auto-shape | cg-gallery-field? |
| `0x62e72` | — | low | auto-shape | cg-gallery-field? |
| `0x62e73` | — | low | auto-shape | cg-gallery-field? |
| `0x62e74` | — | low | auto-shape | cg-gallery-field? |
| `0x62e75` | — | low | auto-shape | cg-gallery-field? |
| `0x62e76` | — | low | auto-shape | cg-gallery-field? |
| `0x62e77` | — | low | auto-shape | cg-gallery-field? |
| `0x62e78` | — | low | auto-shape | cg-gallery-field? |
| `0x62e79` | — | low | auto-shape | cg-gallery-field? |
| `0x62e7a` | — | low | auto-shape | cg-gallery-field? |
| `0x62e7b` | — | low | auto-shape | cg-gallery-field? |
| `0x62e7c` | — | low | auto-shape | cg-gallery-field? |
| `0x62e7d` | — | low | auto-shape | cg-gallery-field? |
| `0x62e7e` | — | low | auto-shape | cg-gallery-field? |
| `0x62e7f` | — | low | auto-shape | cg-gallery-field? |
| `0x62e80` | — | low | auto-shape | cg-gallery-field? |
| `0x62e81` | — | low | auto-shape | cg-gallery-field? |
| `0x62e82` | — | low | auto-shape | cg-gallery-field? |
| `0x62e83` | — | low | auto-shape | cg-gallery-field? |
| `0x62e84` | — | low | auto-shape | cg-gallery-field? |
| `0x62e85` | — | low | auto-shape | cg-gallery-field? |
| `0x62e86` | — | low | auto-shape | cg-gallery-field? |
| `0x62e87` | — | low | auto-shape | cg-gallery-field? |
| `0x62e88` | — | low | auto-shape | cg-gallery-field? |
| `0x62e89` | — | low | auto-shape | cg-gallery-field? |
| `0x62e8a` | — | low | auto-shape | cg-gallery-field? |
| `0x62e8b` | — | low | auto-shape | cg-gallery-field? |
| `0x62e8c` | — | low | auto-shape | cg-gallery-field? |
| `0x62e8d` | — | low | auto-shape | cg-gallery-field? |
| `0x62e8e` | — | low | auto-shape | cg-gallery-field? |
| `0x62e8f` | — | low | auto-shape | cg-gallery-field? |
| `0x62e90` | — | low | auto-shape | cg-gallery-field? |
| `0x62e91` | — | low | auto-shape | cg-gallery-field? |
| `0x62e92` | — | low | auto-shape | cg-gallery-field? |
| `0x62e93` | — | low | auto-shape | cg-gallery-field? |
| `0x62e94` | — | low | auto-shape | cg-gallery-field? |
| `0x62e95` | — | low | auto-shape | cg-gallery-field? |
| `0x62e96` | — | low | auto-shape | cg-gallery-field? |
| `0x62e97` | — | low | auto-shape | cg-gallery-field? |
| `0x62e98` | — | low | auto-shape | cg-gallery-field? |
| `0x62e99` | — | low | auto-shape | cg-gallery-field? |
| `0x62e9a` | — | low | auto-shape | cg-gallery-field? |
| `0x62e9b` | — | low | auto-shape | cg-gallery-field? |
| `0x62e9c` | — | low | auto-shape | cg-gallery-field? |
| `0x62e9d` | — | low | auto-shape | cg-gallery-field? |
| `0x62e9e` | — | low | auto-shape | cg-gallery-field? |
| `0x62e9f` | — | low | auto-shape | cg-gallery-field? |
| `0x62ea0` | — | low | auto-shape | cg-gallery-field? |
| `0x62ea1` | — | low | auto-shape | cg-gallery-field? |
| `0x62ea2` | — | low | auto-shape | cg-gallery-field? |
| `0x62ea3` | — | low | auto-shape | cg-gallery-field? |
| `0x62ea4` | — | low | auto-shape | cg-gallery-field? |
| `0x62ea5` | — | low | auto-shape | cg-gallery-field? |
| `0x62ea6` | — | low | auto-shape | cg-gallery-field? |
| `0x62ea7` | — | low | auto-shape | cg-gallery-field? |
| `0x62ea8` | — | low | auto-shape | cg-gallery-field? |
| `0x62ea9` | — | low | auto-shape | cg-gallery-field? |
| `0x62eaa` | — | low | auto-shape | cg-gallery-field? |
| `0x62eab` | — | low | auto-shape | cg-gallery-field? |
| `0x62eac` | — | low | auto-shape | cg-gallery-field? |
| `0x62ead` | — | low | auto-shape | cg-gallery-field? |
| `0x62eae` | — | low | auto-shape | cg-gallery-field? |
| `0x62eaf` | — | low | auto-shape | cg-gallery-field? |
| `0x62eb0` | — | low | auto-shape | cg-gallery-field? |
| `0x62eb1` | — | low | auto-shape | cg-gallery-field? |
| `0x62eb2` | — | low | auto-shape | cg-gallery-field? |
| `0x62eb3` | — | low | auto-shape | cg-gallery-field? |
| `0x62eb4` | — | low | auto-shape | cg-gallery-field? |
| `0x62eb5` | — | low | auto-shape | cg-gallery-field? |
| `0x62eb6` | — | low | auto-shape | cg-gallery-field? |
| `0x62eb7` | — | low | auto-shape | cg-gallery-field? |
| `0x62eb8` | — | low | auto-shape | cg-gallery-field? |
| `0x62eb9` | — | low | auto-shape | cg-gallery-field? |
| `0x62eba` | — | low | auto-shape | cg-gallery-field? |
| `0x62ebb` | — | low | auto-shape | cg-gallery-field? |
| `0x62ebc` | — | low | auto-shape | cg-gallery-field? |
| `0x62ebd` | — | low | auto-shape | cg-gallery-field? |
| `0x62ebe` | — | low | auto-shape | cg-gallery-field? |
| `0x62ebf` | — | low | auto-shape | cg-gallery-field? |
| `0x62ec0` | — | low | auto-shape | cg-gallery-field? |
| `0x62ec1` | — | low | auto-shape | cg-gallery-field? |
| `0x62ec2` | — | low | auto-shape | cg-gallery-field? |
| `0x62ec3` | — | low | auto-shape | cg-gallery-field? |
| `0x62ec4` | — | low | auto-shape | cg-gallery-field? |
| `0x62ec5` | — | low | auto-shape | cg-gallery-field? |
| `0x62ec6` | — | low | auto-shape | cg-gallery-field? |
| `0x62ec7` | — | low | auto-shape | cg-gallery-field? |
| `0x62ec8` | — | low | auto-shape | cg-gallery-field? |
| `0x62ec9` | — | low | auto-shape | cg-gallery-field? |
| `0x62eca` | — | low | auto-shape | cg-gallery-field? |
| `0x62ecb` | — | low | auto-shape | cg-gallery-field? |
| `0x62ecc` | — | low | auto-shape | cg-gallery-field? |
| `0x62ecd` | — | low | auto-shape | cg-gallery-field? |
| `0x62ece` | — | low | auto-shape | cg-gallery-field? |
| `0x62ecf` | — | low | auto-shape | cg-gallery-field? |
| `0x62ed0` | — | low | auto-shape | cg-gallery-field? |
| `0x62ed1` | — | low | auto-shape | cg-gallery-field? |
| `0x62ed2` | — | low | auto-shape | cg-gallery-field? |
| `0x62ed3` | — | low | auto-shape | cg-gallery-field? |
| `0x62ed4` | — | low | auto-shape | cg-gallery-field? |
| `0x62ed5` | — | low | auto-shape | cg-gallery-field? |
| `0x62ed6` | — | low | auto-shape | cg-gallery-field? |
| `0x62ed7` | — | low | auto-shape | cg-gallery-field? |
| `0x62ed8` | — | low | auto-shape | cg-gallery-field? |
| `0x62ed9` | — | low | auto-shape | cg-gallery-field? |
| `0x62eda` | — | low | auto-shape | cg-gallery-field? |
| `0x62edb` | — | low | auto-shape | cg-gallery-field? |
| `0x62edc` | — | low | auto-shape | cg-gallery-field? |
| `0x62edd` | — | low | auto-shape | cg-gallery-field? |
| `0x62ede` | — | low | auto-shape | cg-gallery-field? |
| `0x62edf` | — | low | auto-shape | cg-gallery-field? |
| `0x62ee0` | — | low | auto-shape | cg-gallery-field? |
| `0x62ee1` | — | low | auto-shape | cg-gallery-field? |
| `0x62ee2` | — | low | auto-shape | cg-gallery-field? |
| `0x62ee3` | — | low | auto-shape | cg-gallery-field? |
| `0x62ee4` | — | low | auto-shape | cg-gallery-field? |
| `0x62ee5` | — | low | auto-shape | cg-gallery-field? |
| `0x62ee6` | — | low | auto-shape | cg-gallery-field? |
| `0x62ee7` | — | low | auto-shape | cg-gallery-field? |
| `0x62ee8` | — | low | auto-shape | cg-gallery-field? |
| `0x62ee9` | — | low | auto-shape | cg-gallery-field? |
| `0x62eea` | — | low | auto-shape | cg-gallery-field? |
| `0x62eeb` | — | low | auto-shape | cg-gallery-field? |
| `0x62eec` | — | low | auto-shape | cg-gallery-field? |
| `0x62eed` | — | low | auto-shape | cg-gallery-field? |
| `0x62eee` | — | low | auto-shape | cg-gallery-field? |
| `0x62eef` | — | low | auto-shape | cg-gallery-field? |
| `0x62ef0` | — | low | auto-shape | cg-gallery-field? |
| `0x62ef1` | — | low | auto-shape | cg-gallery-field? |
| `0x62ef2` | — | low | auto-shape | cg-gallery-field? |
| `0x62ef3` | — | low | auto-shape | cg-gallery-field? |
| `0x62ef4` | — | low | auto-shape | cg-gallery-field? |
| `0x62ef5` | — | low | auto-shape | cg-gallery-field? |
| `0x62ef6` | — | low | auto-shape | cg-gallery-field? |
| `0x62ef7` | — | low | auto-shape | cg-gallery-field? |
| `0x62ef8` | — | low | auto-shape | cg-gallery-field? |
| `0x62ef9` | — | low | auto-shape | cg-gallery-field? |
| `0x62efa` | — | low | auto-shape | cg-gallery-field? |
| `0x62efb` | — | low | auto-shape | cg-gallery-field? |
| `0x62efc` | — | low | auto-shape | cg-gallery-field? |
| `0x62efd` | — | low | auto-shape | cg-gallery-field? |
| `0x62efe` | — | low | auto-shape | cg-gallery-field? |
| `0x62eff` | — | low | auto-shape | cg-gallery-field? |
| `0x62f00` | — | low | auto-shape | cg-gallery-field? |
| `0x62f01` | — | low | auto-shape | cg-gallery-field? |
| `0x62f02` | — | low | auto-shape | cg-gallery-field? |
| `0x62f03` | — | low | auto-shape | cg-gallery-field? |
| `0x62f04` | — | low | auto-shape | cg-gallery-field? |
| `0x62f05` | — | low | auto-shape | cg-gallery-field? |
| `0x62f06` | — | low | auto-shape | cg-gallery-field? |
| `0x62f07` | — | low | auto-shape | cg-gallery-field? |
| `0x62f08` | — | low | auto-shape | cg-gallery-field? |
| `0x62f09` | — | low | auto-shape | cg-gallery-field? |
| `0x62f0a` | — | low | auto-shape | cg-gallery-field? |
| `0x62f0b` | — | low | auto-shape | cg-gallery-field? |
| `0x62f0c` | — | low | auto-shape | cg-gallery-field? |
| `0x62f0d` | — | low | auto-shape | cg-gallery-field? |
| `0x62f0e` | — | low | auto-shape | cg-gallery-field? |
| `0x62f0f` | — | low | auto-shape | cg-gallery-field? |
| `0x62f10` | — | low | auto-shape | cg-gallery-field? |
| `0x62f11` | — | low | auto-shape | cg-gallery-field? |
| `0x62f12` | — | low | auto-shape | cg-gallery-field? |
| `0x62f13` | — | low | auto-shape | cg-gallery-field? |
| `0x62f14` | — | low | auto-shape | cg-gallery-field? |
| `0x62f15` | — | low | auto-shape | cg-gallery-field? |
| `0x62f16` | — | low | auto-shape | cg-gallery-field? |
| `0x62f17` | — | low | auto-shape | cg-gallery-field? |
| `0x62f18` | — | low | auto-shape | cg-gallery-field? |
| `0x62f19` | — | low | auto-shape | cg-gallery-field? |
| `0x62f1a` | — | low | auto-shape | cg-gallery-field? |
| `0x62f1b` | — | low | auto-shape | cg-gallery-field? |
| `0x62f1c` | — | low | auto-shape | cg-gallery-field? |
| `0x62f1d` | — | low | auto-shape | cg-gallery-field? |
| `0x62f1e` | — | low | auto-shape | cg-gallery-field? |
| `0x62f1f` | — | low | auto-shape | cg-gallery-field? |
| `0x62f20` | — | low | auto-shape | cg-gallery-field? |
| `0x62f21` | — | low | auto-shape | cg-gallery-field? |
| `0x62f22` | — | low | auto-shape | cg-gallery-field? |
| `0x62f23` | — | low | auto-shape | cg-gallery-field? |
| `0x62f24` | — | low | auto-shape | cg-gallery-field? |
| `0x62f25` | — | low | auto-shape | cg-gallery-field? |
| `0x62f26` | — | low | auto-shape | cg-gallery-field? |
| `0x62f27` | — | low | auto-shape | cg-gallery-field? |
| `0x62f28` | — | low | auto-shape | cg-gallery-field? |
| `0x62f29` | — | low | auto-shape | cg-gallery-field? |
| `0x62f2a` | — | low | auto-shape | cg-gallery-field? |
| `0x62f2b` | — | low | auto-shape | cg-gallery-field? |
| `0x62f2c` | — | low | auto-shape | cg-gallery-field? |
| `0x62f2d` | — | low | auto-shape | cg-gallery-field? |
| `0x62f2e` | — | low | auto-shape | cg-gallery-field? |
| `0x62f2f` | — | low | auto-shape | cg-gallery-field? |
| `0x62f30` | — | low | auto-shape | cg-gallery-field? |
| `0x62f31` | — | low | auto-shape | cg-gallery-field? |
| `0x62f32` | — | low | auto-shape | cg-gallery-field? |
| `0x62f33` | — | low | auto-shape | cg-gallery-field? |
| `0x62f34` | — | low | auto-shape | cg-gallery-field? |
| `0x62f35` | — | low | auto-shape | cg-gallery-field? |
| `0x62f36` | — | low | auto-shape | cg-gallery-field? |
| `0x62f37` | — | low | auto-shape | cg-gallery-field? |
| `0x62f38` | — | low | auto-shape | cg-gallery-field? |
| `0x62f39` | — | low | auto-shape | cg-gallery-field? |
| `0x62f3a` | — | low | auto-shape | cg-gallery-field? |
| `0x62f3b` | — | low | auto-shape | cg-gallery-field? |
| `0x62f3c` | — | low | auto-shape | cg-gallery-field? |
| `0x62f3d` | — | low | auto-shape | cg-gallery-field? |
| `0x62f3e` | — | low | auto-shape | cg-gallery-field? |
| `0x62f3f` | — | low | auto-shape | cg-gallery-field? |
| `0x62f40` | — | low | auto-shape | cg-gallery-field? |
| `0x62f41` | — | low | auto-shape | cg-gallery-field? |
| `0x62f42` | — | low | auto-shape | cg-gallery-field? |
| `0x62f43` | — | low | auto-shape | cg-gallery-field? |
| `0x62f44` | — | low | auto-shape | cg-gallery-field? |
| `0x62f45` | — | low | auto-shape | cg-gallery-field? |
| `0x62f46` | — | low | auto-shape | cg-gallery-field? |
| `0x62f47` | — | low | auto-shape | cg-gallery-field? |
| `0x62f48` | — | low | auto-shape | cg-gallery-field? |
| `0x62f49` | — | low | auto-shape | cg-gallery-field? |
| `0x62f4a` | — | low | auto-shape | cg-gallery-field? |
| `0x62f4b` | — | low | auto-shape | cg-gallery-field? |
| `0x62f4c` | — | low | auto-shape | cg-gallery-field? |
| `0x62f4d` | — | low | auto-shape | cg-gallery-field? |
| `0x62f4e` | — | low | auto-shape | cg-gallery-field? |
| `0x62f4f` | — | low | auto-shape | cg-gallery-field? |
| `0x62f50` | — | low | auto-shape | cg-gallery-field? |
| `0x62f51` | — | low | auto-shape | cg-gallery-field? |
| `0x62f52` | — | low | auto-shape | cg-gallery-field? |
| `0x62f53` | — | low | auto-shape | cg-gallery-field? |
| `0x62f54` | — | low | auto-shape | cg-gallery-field? |
| `0x62f55` | — | low | auto-shape | cg-gallery-field? |
| `0x62f56` | — | low | auto-shape | cg-gallery-field? |
| `0x62f57` | — | low | auto-shape | cg-gallery-field? |
| `0x62f58` | — | low | auto-shape | cg-gallery-field? |
| `0x62f59` | — | low | auto-shape | cg-gallery-field? |
| `0x62f5a` | — | low | auto-shape | cg-gallery-field? |
| `0x62f5b` | — | low | auto-shape | cg-gallery-field? |
| `0x62f5c` | — | low | auto-shape | cg-gallery-field? |
| `0x62f5d` | — | low | auto-shape | cg-gallery-field? |
| `0x62f5e` | — | low | auto-shape | cg-gallery-field? |
| `0x62f5f` | — | low | auto-shape | cg-gallery-field? |
| `0x62f60` | — | low | auto-shape | cg-gallery-field? |
| `0x62f61` | — | low | auto-shape | cg-gallery-field? |
| `0x62f62` | — | low | auto-shape | cg-gallery-field? |
| `0x62f63` | — | low | auto-shape | cg-gallery-field? |
| `0x62f64` | — | low | auto-shape | cg-gallery-field? |
| `0x62f65` | — | low | auto-shape | cg-gallery-field? |
| `0x62f66` | — | low | auto-shape | cg-gallery-field? |
| `0x62f67` | — | low | auto-shape | cg-gallery-field? |
| `0x62f68` | — | low | auto-shape | cg-gallery-field? |
| `0x62f69` | — | low | auto-shape | cg-gallery-field? |
| `0x62f6a` | — | low | auto-shape | cg-gallery-field? |
| `0x62f6b` | — | low | auto-shape | cg-gallery-field? |
| `0x62f6c` | — | low | auto-shape | cg-gallery-field? |
| `0x62f6d` | — | low | auto-shape | cg-gallery-field? |
| `0x62f6e` | — | low | auto-shape | cg-gallery-field? |
| `0x62f6f` | — | low | auto-shape | cg-gallery-field? |
| `0x62f70` | — | low | auto-shape | cg-gallery-field? |
| `0x62f71` | — | low | auto-shape | cg-gallery-field? |
| `0x62f72` | — | low | auto-shape | cg-gallery-field? |
| `0x62f73` | — | low | auto-shape | cg-gallery-field? |
| `0x62f74` | — | low | auto-shape | cg-gallery-field? |
| `0x62f75` | — | low | auto-shape | cg-gallery-field? |
| `0x62f76` | — | low | auto-shape | cg-gallery-field? |
| `0x62f77` | — | low | auto-shape | cg-gallery-field? |
| `0x62f78` | — | low | auto-shape | cg-gallery-field? |
| `0x62f79` | — | low | auto-shape | cg-gallery-field? |
| `0x62f7a` | — | low | auto-shape | cg-gallery-field? |
| `0x62f7b` | — | low | auto-shape | cg-gallery-field? |
| `0x62f7c` | — | low | auto-shape | cg-gallery-field? |
| `0x62f7d` | — | low | auto-shape | cg-gallery-field? |
| `0x62f7e` | — | low | auto-shape | cg-gallery-field? |
| `0x62f7f` | — | low | auto-shape | cg-gallery-field? |
| `0x62f80` | — | low | auto-shape | cg-gallery-field? |
| `0x62f81` | — | low | auto-shape | cg-gallery-field? |
| `0x62f82` | — | low | auto-shape | cg-gallery-field? |
| `0x62f83` | — | low | auto-shape | cg-gallery-field? |
| `0x62f84` | — | low | auto-shape | cg-gallery-field? |
| `0x62f85` | — | low | auto-shape | cg-gallery-field? |
| `0x62f86` | — | low | auto-shape | cg-gallery-field? |
| `0x62f87` | — | low | auto-shape | cg-gallery-field? |
| `0x62f88` | — | low | auto-shape | cg-gallery-field? |
| `0x62f89` | — | low | auto-shape | cg-gallery-field? |
| `0x62f8a` | — | low | auto-shape | cg-gallery-field? |
| `0x62f8b` | — | low | auto-shape | cg-gallery-field? |
| `0x62f8c` | — | low | auto-shape | cg-gallery-field? |
| `0x62f8d` | — | low | auto-shape | cg-gallery-field? |
| `0x62f8e` | — | low | auto-shape | cg-gallery-field? |
| `0x62f8f` | — | low | auto-shape | cg-gallery-field? |
| `0x62f90` | — | low | auto-shape | cg-gallery-field? |
| `0x62f91` | — | low | auto-shape | cg-gallery-field? |
| `0x62f92` | — | low | auto-shape | cg-gallery-field? |
| `0x62f93` | — | low | auto-shape | cg-gallery-field? |
| `0x62f94` | — | low | auto-shape | cg-gallery-field? |
| `0x62f95` | — | low | auto-shape | cg-gallery-field? |
| `0x62f96` | — | low | auto-shape | cg-gallery-field? |
| `0x62f97` | — | low | auto-shape | cg-gallery-field? |
| `0x62f98` | — | low | auto-shape | cg-gallery-field? |
| `0x62f99` | — | low | auto-shape | cg-gallery-field? |
| `0x62f9a` | — | low | auto-shape | cg-gallery-field? |
| `0x62f9b` | — | low | auto-shape | cg-gallery-field? |
| `0x62f9c` | — | low | auto-shape | cg-gallery-field? |
| `0x62f9d` | — | low | auto-shape | cg-gallery-field? |
| `0x62f9e` | — | low | auto-shape | cg-gallery-field? |
| `0x62f9f` | — | low | auto-shape | cg-gallery-field? |
| `0x62fa0` | — | low | auto-shape | cg-gallery-field? |
| `0x62fa1` | — | low | auto-shape | cg-gallery-field? |
| `0x62fa2` | — | low | auto-shape | cg-gallery-field? |
| `0x62fa3` | — | low | auto-shape | cg-gallery-field? |
| `0x62fa4` | — | low | auto-shape | cg-gallery-field? |
| `0x62fa5` | — | low | auto-shape | cg-gallery-field? |
| `0x62fa6` | — | low | auto-shape | cg-gallery-field? |
| `0x62fa7` | — | low | auto-shape | cg-gallery-field? |
| `0x62fa8` | — | low | auto-shape | cg-gallery-field? |
| `0x62fa9` | — | low | auto-shape | cg-gallery-field? |
| `0x62faa` | — | low | auto-shape | cg-gallery-field? |
| `0x62fab` | — | low | auto-shape | cg-gallery-field? |
| `0x62fac` | — | low | auto-shape | cg-gallery-field? |
| `0x62fad` | — | low | auto-shape | cg-gallery-field? |
| `0x62fae` | — | low | auto-shape | cg-gallery-field? |
| `0x62faf` | — | low | auto-shape | cg-gallery-field? |
| `0x62fb0` | — | low | auto-shape | cg-gallery-field? |
| `0x62fb1` | — | low | auto-shape | cg-gallery-field? |
| `0x62fb2` | — | low | auto-shape | cg-gallery-field? |
| `0x62fb3` | — | low | auto-shape | cg-gallery-field? |
| `0x62fb4` | — | low | auto-shape | cg-gallery-field? |
| `0x62fb5` | — | low | auto-shape | cg-gallery-field? |
| `0x62fb6` | — | low | auto-shape | cg-gallery-field? |
| `0x62fb7` | — | low | auto-shape | cg-gallery-field? |
| `0x62fb8` | — | low | auto-shape | cg-gallery-field? |
| `0x62fb9` | — | low | auto-shape | cg-gallery-field? |
| `0x62fba` | — | low | auto-shape | cg-gallery-field? |
| `0x62fbb` | — | low | auto-shape | cg-gallery-field? |
| `0x62fbc` | — | low | auto-shape | cg-gallery-field? |
| `0x62fbd` | — | low | auto-shape | cg-gallery-field? |
| `0x62fbe` | — | low | auto-shape | cg-gallery-field? |
| `0x62fbf` | — | low | auto-shape | cg-gallery-field? |
| `0x62fc0` | — | low | auto-shape | cg-gallery-field? |
| `0x62fc1` | — | low | auto-shape | cg-gallery-field? |
| `0x62fc2` | — | low | auto-shape | cg-gallery-field? |
| `0x62fc3` | — | low | auto-shape | cg-gallery-field? |
| `0x62fc4` | — | low | auto-shape | cg-gallery-field? |
| `0x62fc5` | — | low | auto-shape | cg-gallery-field? |
| `0x62fc6` | — | low | auto-shape | cg-gallery-field? |
| `0x62fc7` | — | low | auto-shape | cg-gallery-field? |
| `0x62fc8` | — | low | auto-shape | cg-gallery-field? |
| `0x62fc9` | — | low | auto-shape | cg-gallery-field? |
| `0x62fca` | — | low | auto-shape | cg-gallery-field? |
| `0x62fcb` | — | low | auto-shape | cg-gallery-field? |
| `0x62fcc` | — | low | auto-shape | cg-gallery-field? |
| `0x62fcd` | — | low | auto-shape | cg-gallery-field? |
| `0x62fce` | — | low | auto-shape | cg-gallery-field? |
| `0x62fcf` | — | low | auto-shape | cg-gallery-field? |
| `0x62fd0` | — | low | auto-shape | cg-gallery-field? |
| `0x62fd1` | — | low | auto-shape | cg-gallery-field? |
| `0x62fd2` | — | low | auto-shape | cg-gallery-field? |
| `0x62fd3` | — | low | auto-shape | cg-gallery-field? |
| `0x62fd4` | — | low | auto-shape | cg-gallery-field? |
| `0x62fd5` | — | low | auto-shape | cg-gallery-field? |
| `0x62fd6` | — | low | auto-shape | cg-gallery-field? |
| `0x62fd7` | — | low | auto-shape | cg-gallery-field? |
| `0x62fd8` | — | low | auto-shape | cg-gallery-field? |
| `0x62fd9` | — | low | auto-shape | cg-gallery-field? |
| `0x62fda` | — | low | auto-shape | cg-gallery-field? |
| `0x62fdb` | — | low | auto-shape | cg-gallery-field? |
| `0x62fdc` | — | low | auto-shape | cg-gallery-field? |
| `0x62fdd` | — | low | auto-shape | cg-gallery-field? |
| `0x62fde` | — | low | auto-shape | cg-gallery-field? |
| `0x62fdf` | — | low | auto-shape | cg-gallery-field? |
| `0x62fe0` | — | low | auto-shape | cg-gallery-field? |
| `0x62fe1` | — | low | auto-shape | cg-gallery-field? |
| `0x62fe2` | — | low | auto-shape | cg-gallery-field? |
| `0x62fe3` | — | low | auto-shape | cg-gallery-field? |
| `0x62fe4` | — | low | auto-shape | cg-gallery-field? |
| `0x62fe5` | — | low | auto-shape | cg-gallery-field? |
| `0x62fe6` | — | low | auto-shape | cg-gallery-field? |
| `0x62fe7` | — | low | auto-shape | cg-gallery-field? |
| `0x63c72` | — | low | auto-shape | cg-gallery-field? |
| `0x63c73` | — | low | auto-shape | cg-gallery-field? |
| `0x63c74` | — | low | auto-shape | cg-gallery-field? |
| `0x63c75` | — | low | auto-shape | cg-gallery-field? |
| `0x63c76` | — | low | auto-shape | cg-gallery-field? |
| `0x63c77` | — | low | auto-shape | cg-gallery-field? |
| `0x63c78` | — | low | auto-shape | cg-gallery-field? |
| `0x63c79` | — | low | auto-shape | cg-gallery-field? |
| `0x63c7a` | — | low | auto-shape | cg-gallery-field? |
| `0x63c7b` | — | low | auto-shape | cg-gallery-field? |
| `0x63c7c` | — | low | auto-shape | cg-gallery-field? |
| `0x63c7d` | — | low | auto-shape | cg-gallery-field? |
| `0x63c7e` | — | low | auto-shape | cg-gallery-field? |
| `0x63c7f` | — | low | auto-shape | cg-gallery-field? |
| `0x63c80` | — | low | auto-shape | cg-gallery-field? |
| `0x63c81` | — | low | auto-shape | cg-gallery-field? |
| `0x63c82` | — | low | auto-shape | cg-gallery-field? |
| `0x63c83` | — | low | auto-shape | cg-gallery-field? |
| `0x63c84` | — | low | auto-shape | cg-gallery-field? |
| `0x63c85` | — | low | auto-shape | cg-gallery-field? |
| `0x63c86` | — | low | auto-shape | cg-gallery-field? |
| `0x63c87` | — | low | auto-shape | cg-gallery-field? |
| `0x63c88` | — | low | auto-shape | cg-gallery-field? |
| `0x63c89` | — | low | auto-shape | cg-gallery-field? |
| `0x63c8a` | — | low | auto-shape | cg-gallery-field? |
| `0x63c8b` | — | low | auto-shape | cg-gallery-field? |
| `0x63c8c` | — | low | auto-shape | cg-gallery-field? |
| `0x63c8d` | — | low | auto-shape | cg-gallery-field? |
| `0x63c8e` | — | low | auto-shape | cg-gallery-field? |
| `0x63c8f` | — | low | auto-shape | cg-gallery-field? |
| `0x63c90` | — | low | auto-shape | cg-gallery-field? |
| `0x63c91` | — | low | auto-shape | cg-gallery-field? |
| `0x63c92` | — | low | auto-shape | cg-gallery-field? |
| `0x63c93` | — | low | auto-shape | cg-gallery-field? |
| `0x63c94` | — | low | auto-shape | cg-gallery-field? |
| `0x63c95` | — | low | auto-shape | cg-gallery-field? |
| `0x63c96` | — | low | auto-shape | cg-gallery-field? |
| `0x63c97` | — | low | auto-shape | cg-gallery-field? |
| `0x63c98` | — | low | auto-shape | cg-gallery-field? |
| `0x63c99` | — | low | auto-shape | cg-gallery-field? |
| `0x63c9a` | — | low | auto-shape | cg-gallery-field? |
| `0x63c9b` | — | low | auto-shape | cg-gallery-field? |
| `0x63c9c` | — | low | auto-shape | cg-gallery-field? |
| `0x63c9d` | — | low | auto-shape | cg-gallery-field? |
| `0x63c9e` | — | low | auto-shape | cg-gallery-field? |
| `0x63c9f` | — | low | auto-shape | cg-gallery-field? |
| `0x63ca0` | — | low | auto-shape | cg-gallery-field? |
| `0x63ca1` | — | low | auto-shape | cg-gallery-field? |
| `0x63ca2` | — | low | auto-shape | cg-gallery-field? |
| `0x63ca3` | — | low | auto-shape | cg-gallery-field? |
| `0x63ca4` | — | low | auto-shape | cg-gallery-field? |
| `0x63ca5` | — | low | auto-shape | cg-gallery-field? |
| `0x63ca6` | — | low | auto-shape | cg-gallery-field? |
| `0x63ca7` | — | low | auto-shape | cg-gallery-field? |
| `0x63ca8` | — | low | auto-shape | cg-gallery-field? |
| `0x63ca9` | — | low | auto-shape | cg-gallery-field? |
| `0x63caa` | — | low | auto-shape | cg-gallery-field? |
| `0x63cab` | — | low | auto-shape | cg-gallery-field? |
| `0x63cac` | — | low | auto-shape | cg-gallery-field? |
| `0x63cad` | — | low | auto-shape | cg-gallery-field? |
| `0x63cae` | — | low | auto-shape | cg-gallery-field? |
| `0x63caf` | — | low | auto-shape | cg-gallery-field? |
| `0x63cb0` | — | low | auto-shape | cg-gallery-field? |
| `0x63cb1` | — | low | auto-shape | cg-gallery-field? |
| `0x63cb2` | — | low | auto-shape | cg-gallery-field? |
| `0x63cb3` | — | low | auto-shape | cg-gallery-field? |
| `0x63cb4` | — | low | auto-shape | cg-gallery-field? |
| `0x63cb5` | — | low | auto-shape | cg-gallery-field? |
| `0x63cb6` | — | low | auto-shape | cg-gallery-field? |
| `0x63cb7` | — | low | auto-shape | cg-gallery-field? |
| `0x63cb8` | — | low | auto-shape | cg-gallery-field? |
| `0x63cb9` | — | low | auto-shape | cg-gallery-field? |
| `0x63cba` | — | low | auto-shape | cg-gallery-field? |
| `0x63cbb` | — | low | auto-shape | cg-gallery-field? |
| `0x63cbc` | — | low | auto-shape | cg-gallery-field? |
| `0x63cbd` | — | low | auto-shape | cg-gallery-field? |
| `0x63cbe` | — | low | auto-shape | cg-gallery-field? |
| `0x63cbf` | — | low | auto-shape | cg-gallery-field? |
| `0x63cc0` | — | low | auto-shape | cg-gallery-field? |
| `0x63cc1` | — | low | auto-shape | cg-gallery-field? |
| `0x63cc2` | — | low | auto-shape | cg-gallery-field? |
| `0x63cc3` | — | low | auto-shape | cg-gallery-field? |
| `0x63cc4` | — | low | auto-shape | cg-gallery-field? |
| `0x63cc5` | — | low | auto-shape | cg-gallery-field? |
| `0x63cc6` | — | low | auto-shape | cg-gallery-field? |
| `0x63cc7` | — | low | auto-shape | cg-gallery-field? |
| `0x63cc8` | — | low | auto-shape | cg-gallery-field? |
| `0x63cc9` | — | low | auto-shape | cg-gallery-field? |
| `0x63cca` | — | low | auto-shape | cg-gallery-field? |
| `0x63ccb` | — | low | auto-shape | cg-gallery-field? |
| `0x63ccc` | — | low | auto-shape | cg-gallery-field? |
| `0x63ccd` | — | low | auto-shape | cg-gallery-field? |
| `0x63cce` | — | low | auto-shape | cg-gallery-field? |
| `0x63ccf` | — | low | auto-shape | cg-gallery-field? |
| `0x63cd0` | — | low | auto-shape | cg-gallery-field? |
| `0x63cd1` | — | low | auto-shape | cg-gallery-field? |
| `0x63cd2` | — | low | auto-shape | cg-gallery-field? |
| `0x63cd3` | — | low | auto-shape | cg-gallery-field? |
| `0x63cd4` | — | low | auto-shape | cg-gallery-field? |
| `0x63cd5` | — | low | auto-shape | cg-gallery-field? |
| `0x63cd6` | — | low | auto-shape | cg-gallery-field? |
| `0x63cd7` | — | low | auto-shape | cg-gallery-field? |
| `0x63cd8` | — | low | auto-shape | cg-gallery-field? |
| `0x63cd9` | — | low | auto-shape | cg-gallery-field? |
| `0x63cda` | — | low | auto-shape | cg-gallery-field? |
| `0x63cdb` | — | low | auto-shape | cg-gallery-field? |
| `0x63cdc` | — | low | auto-shape | cg-gallery-field? |
| `0x63cdd` | — | low | auto-shape | cg-gallery-field? |
| `0x63cde` | — | low | auto-shape | cg-gallery-field? |
| `0x63cdf` | — | low | auto-shape | cg-gallery-field? |
| `0x63ce0` | — | low | auto-shape | cg-gallery-field? |
| `0x63ce1` | — | low | auto-shape | cg-gallery-field? |
| `0x63ce2` | — | low | auto-shape | cg-gallery-field? |
| `0x63ce3` | — | low | auto-shape | cg-gallery-field? |
| `0x63ce4` | — | low | auto-shape | cg-gallery-field? |
| `0x63ce5` | — | low | auto-shape | cg-gallery-field? |
| `0x63ce6` | — | low | auto-shape | cg-gallery-field? |
| `0x63ce7` | — | low | auto-shape | cg-gallery-field? |
| `0x63ce8` | — | low | auto-shape | cg-gallery-field? |
| `0x63ce9` | — | low | auto-shape | cg-gallery-field? |
| `0x63cea` | — | low | auto-shape | cg-gallery-field? |
| `0x63ceb` | — | low | auto-shape | cg-gallery-field? |
| `0x63cec` | — | low | auto-shape | cg-gallery-field? |
| `0x63ced` | — | low | auto-shape | cg-gallery-field? |
| `0x63cee` | — | low | auto-shape | cg-gallery-field? |
| `0x63cef` | — | low | auto-shape | cg-gallery-field? |
| `0x63cf0` | — | low | auto-shape | cg-gallery-field? |
| `0x63cf1` | — | low | auto-shape | cg-gallery-field? |
| `0x63cf2` | — | low | auto-shape | cg-gallery-field? |
| `0x63cf3` | — | low | auto-shape | cg-gallery-field? |
| `0x63cf4` | — | low | auto-shape | cg-gallery-field? |
| `0x63cf5` | — | low | auto-shape | cg-gallery-field? |
| `0x63cf6` | — | low | auto-shape | cg-gallery-field? |
| `0x63cf7` | — | low | auto-shape | cg-gallery-field? |
| `0x63cf8` | — | low | auto-shape | cg-gallery-field? |
| `0x63cf9` | — | low | auto-shape | cg-gallery-field? |
| `0x63cfa` | — | low | auto-shape | cg-gallery-field? |
| `0x63cfb` | — | low | auto-shape | cg-gallery-field? |
| `0x63cfc` | — | low | auto-shape | cg-gallery-field? |
| `0x63cfd` | — | low | auto-shape | cg-gallery-field? |
| `0x63cfe` | — | low | auto-shape | cg-gallery-field? |
| `0x63cff` | — | low | auto-shape | cg-gallery-field? |
| `0x63d00` | — | low | auto-shape | cg-gallery-field? |
| `0x63d01` | — | low | auto-shape | cg-gallery-field? |
| `0x63d02` | — | low | auto-shape | cg-gallery-field? |
| `0x63d03` | — | low | auto-shape | cg-gallery-field? |
| `0x63d04` | — | low | auto-shape | cg-gallery-field? |
| `0x63d05` | — | low | auto-shape | cg-gallery-field? |
| `0x63d06` | — | low | auto-shape | cg-gallery-field? |
| `0x63d07` | — | low | auto-shape | cg-gallery-field? |
| `0x63d08` | — | low | auto-shape | cg-gallery-field? |
| `0x63d09` | — | low | auto-shape | cg-gallery-field? |
| `0x63d0a` | — | low | auto-shape | cg-gallery-field? |
| `0x63d0b` | — | low | auto-shape | cg-gallery-field? |
| `0x63d0c` | — | low | auto-shape | cg-gallery-field? |
| `0x63d0d` | — | low | auto-shape | cg-gallery-field? |
| `0x63d0e` | — | low | auto-shape | cg-gallery-field? |
| `0x63d0f` | — | low | auto-shape | cg-gallery-field? |
| `0x63d10` | — | low | auto-shape | cg-gallery-field? |
| `0x63d11` | — | low | auto-shape | cg-gallery-field? |
| `0x63d12` | — | low | auto-shape | cg-gallery-field? |
| `0x63d13` | — | low | auto-shape | cg-gallery-field? |
| `0x63d14` | — | low | auto-shape | cg-gallery-field? |
| `0x63d15` | — | low | auto-shape | cg-gallery-field? |
| `0x63d16` | — | low | auto-shape | cg-gallery-field? |
| `0x63d17` | — | low | auto-shape | cg-gallery-field? |
| `0x63d18` | — | low | auto-shape | cg-gallery-field? |
| `0x63d19` | — | low | auto-shape | cg-gallery-field? |
| `0x63d1a` | — | low | auto-shape | cg-gallery-field? |
| `0x63d1b` | — | low | auto-shape | cg-gallery-field? |
| `0x63d1c` | — | low | auto-shape | cg-gallery-field? |
| `0x63d1d` | — | low | auto-shape | cg-gallery-field? |
| `0x63d1e` | — | low | auto-shape | cg-gallery-field? |
| `0x63d1f` | — | low | auto-shape | cg-gallery-field? |
| `0x63d20` | — | low | auto-shape | cg-gallery-field? |
| `0x63d21` | — | low | auto-shape | cg-gallery-field? |
| `0x63d22` | — | low | auto-shape | cg-gallery-field? |
| `0x63d23` | — | low | auto-shape | cg-gallery-field? |
| `0x63d24` | — | low | auto-shape | cg-gallery-field? |
| `0x63d25` | — | low | auto-shape | cg-gallery-field? |
| `0x63d26` | — | low | auto-shape | cg-gallery-field? |
| `0x63d27` | — | low | auto-shape | cg-gallery-field? |
| `0x63d28` | — | low | auto-shape | cg-gallery-field? |
| `0x63d29` | — | low | auto-shape | cg-gallery-field? |
| `0x63d2a` | — | low | auto-shape | cg-gallery-field? |
| `0x63d2b` | — | low | auto-shape | cg-gallery-field? |
| `0x63d2c` | — | low | auto-shape | cg-gallery-field? |
| `0x63d2d` | — | low | auto-shape | cg-gallery-field? |
| `0x63d2e` | — | low | auto-shape | cg-gallery-field? |
| `0x63d2f` | — | low | auto-shape | cg-gallery-field? |
| `0x63d30` | — | low | auto-shape | cg-gallery-field? |
| `0x63d31` | — | low | auto-shape | cg-gallery-field? |
| `0x63d32` | — | low | auto-shape | cg-gallery-field? |
| `0x63d33` | — | low | auto-shape | cg-gallery-field? |
| `0x63d34` | — | low | auto-shape | cg-gallery-field? |
| `0x63d35` | — | low | auto-shape | cg-gallery-field? |
| `0x63d36` | — | low | auto-shape | cg-gallery-field? |
| `0x63d37` | — | low | auto-shape | cg-gallery-field? |
| `0x63d38` | — | low | auto-shape | cg-gallery-field? |
| `0x63d39` | — | low | auto-shape | cg-gallery-field? |
| `0x63d3a` | — | low | auto-shape | cg-gallery-field? |
| `0x63d3b` | — | low | auto-shape | cg-gallery-field? |
| `0x63d3c` | — | low | auto-shape | cg-gallery-field? |
| `0x63d3d` | — | low | auto-shape | cg-gallery-field? |
| `0x63d3f` | — | med | auto-shape | cg-gallery-field |
| `0x64442` | — | low | auto-shape | cg-gallery-field? |
| `0x64443` | — | low | auto-shape | cg-gallery-field? |
| `0x64444` | — | low | auto-shape | cg-gallery-field? |
| `0x64445` | — | low | auto-shape | cg-gallery-field? |
| `0x64446` | — | low | auto-shape | cg-gallery-field? |
| `0x64447` | — | low | auto-shape | cg-gallery-field? |
| `0x64448` | — | low | auto-shape | cg-gallery-field? |
| `0x64449` | — | low | auto-shape | cg-gallery-field? |
| `0x6444a` | — | low | auto-shape | cg-gallery-field? |
| `0x6444b` | — | low | auto-shape | cg-gallery-field? |
| `0x6444c` | — | low | auto-shape | cg-gallery-field? |
| `0x6444d` | — | low | auto-shape | cg-gallery-field? |
| `0x6444e` | — | low | auto-shape | cg-gallery-field? |
| `0x6444f` | — | low | auto-shape | cg-gallery-field? |
| `0x64450` | — | low | auto-shape | cg-gallery-field? |
| `0x64451` | — | low | auto-shape | cg-gallery-field? |
| `0x64452` | — | low | auto-shape | cg-gallery-field? |
| `0x64453` | — | low | auto-shape | cg-gallery-field? |
| `0x64454` | — | low | auto-shape | cg-gallery-field? |
| `0x64455` | — | low | auto-shape | cg-gallery-field? |
| `0x64456` | — | low | auto-shape | cg-gallery-field? |
| `0x64457` | — | low | auto-shape | cg-gallery-field? |
| `0x64458` | — | low | auto-shape | cg-gallery-field? |
| `0x64459` | — | low | auto-shape | cg-gallery-field? |
| `0x6445a` | — | low | auto-shape | cg-gallery-field? |
| `0x6445b` | — | low | auto-shape | cg-gallery-field? |
| `0x6445c` | — | low | auto-shape | cg-gallery-field? |
| `0x6445d` | — | low | auto-shape | cg-gallery-field? |
| `0x6445e` | — | low | auto-shape | cg-gallery-field? |
| `0x6445f` | — | low | auto-shape | cg-gallery-field? |
| `0x64460` | — | low | auto-shape | cg-gallery-field? |
| `0x64461` | — | low | auto-shape | cg-gallery-field? |
| `0x64462` | — | low | auto-shape | cg-gallery-field? |
| `0x64463` | — | low | auto-shape | cg-gallery-field? |
| `0x64464` | — | low | auto-shape | cg-gallery-field? |
| `0x64465` | — | low | auto-shape | cg-gallery-field? |
| `0x64466` | — | low | auto-shape | cg-gallery-field? |
| `0x64467` | — | low | auto-shape | cg-gallery-field? |
| `0x64468` | — | low | auto-shape | cg-gallery-field? |
| `0x64469` | — | low | auto-shape | cg-gallery-field? |
| `0x6446a` | — | low | auto-shape | cg-gallery-field? |
| `0x6446b` | — | low | auto-shape | cg-gallery-field? |
| `0x6446c` | — | low | auto-shape | cg-gallery-field? |
| `0x6446d` | — | low | auto-shape | cg-gallery-field? |
| `0x6446e` | — | low | auto-shape | cg-gallery-field? |
| `0x6446f` | — | low | auto-shape | cg-gallery-field? |
| `0x64470` | — | low | auto-shape | cg-gallery-field? |
| `0x64471` | — | low | auto-shape | cg-gallery-field? |
| `0x64472` | — | low | auto-shape | cg-gallery-field? |
| `0x64473` | — | low | auto-shape | cg-gallery-field? |
| `0x64474` | — | low | auto-shape | cg-gallery-field? |
| `0x64475` | — | low | auto-shape | cg-gallery-field? |
| `0x64476` | — | low | auto-shape | cg-gallery-field? |
| `0x64477` | — | low | auto-shape | cg-gallery-field? |
| `0x64478` | — | low | auto-shape | cg-gallery-field? |
| `0x64479` | — | low | auto-shape | cg-gallery-field? |
| `0x6447a` | — | low | auto-shape | cg-gallery-field? |
| `0x6447b` | — | low | auto-shape | cg-gallery-field? |
| `0x6447c` | — | low | auto-shape | cg-gallery-field? |
| `0x6447d` | — | low | auto-shape | cg-gallery-field? |
| `0x6447e` | — | low | auto-shape | cg-gallery-field? |
| `0x6447f` | — | low | auto-shape | cg-gallery-field? |
| `0x64480` | — | low | auto-shape | cg-gallery-field? |
| `0x64481` | — | low | auto-shape | cg-gallery-field? |
| `0x64482` | — | low | auto-shape | cg-gallery-field? |
| `0x64483` | — | low | auto-shape | cg-gallery-field? |
| `0x64484` | — | low | auto-shape | cg-gallery-field? |
| `0x64485` | — | low | auto-shape | cg-gallery-field? |
| `0x64486` | — | low | auto-shape | cg-gallery-field? |
| `0x64487` | — | low | auto-shape | cg-gallery-field? |
| `0x64488` | — | low | auto-shape | cg-gallery-field? |
| `0x64489` | — | low | auto-shape | cg-gallery-field? |
| `0x6448a` | — | low | auto-shape | cg-gallery-field? |
| `0x6448b` | — | low | auto-shape | cg-gallery-field? |
| `0x6448c` | — | low | auto-shape | cg-gallery-field? |
| `0x6448d` | — | low | auto-shape | cg-gallery-field? |
| `0x6448e` | — | low | auto-shape | cg-gallery-field? |
| `0x6448f` | — | low | auto-shape | cg-gallery-field? |
| `0x64490` | — | low | auto-shape | cg-gallery-field? |
| `0x64491` | — | low | auto-shape | cg-gallery-field? |
| `0x64492` | — | low | auto-shape | cg-gallery-field? |
| `0x64493` | — | low | auto-shape | cg-gallery-field? |
| `0x64494` | — | low | auto-shape | cg-gallery-field? |
| `0x64495` | — | low | auto-shape | cg-gallery-field? |
| `0x64496` | — | low | auto-shape | cg-gallery-field? |
| `0x64497` | — | low | auto-shape | cg-gallery-field? |
| `0x64498` | — | low | auto-shape | cg-gallery-field? |
| `0x64499` | — | low | auto-shape | cg-gallery-field? |
| `0x6449a` | — | low | auto-shape | cg-gallery-field? |
| `0x6449b` | — | low | auto-shape | cg-gallery-field? |
| `0x6449c` | — | low | auto-shape | cg-gallery-field? |
| `0x6449d` | — | low | auto-shape | cg-gallery-field? |
| `0x6449e` | — | low | auto-shape | cg-gallery-field? |
| `0x6449f` | — | low | auto-shape | cg-gallery-field? |
| `0x644a0` | — | low | auto-shape | cg-gallery-field? |
| `0x644a1` | — | low | auto-shape | cg-gallery-field? |
| `0x644a2` | — | low | auto-shape | cg-gallery-field? |
| `0x644a3` | — | low | auto-shape | cg-gallery-field? |
| `0x644a4` | — | low | auto-shape | cg-gallery-field? |
| `0x644a5` | — | low | auto-shape | cg-gallery-field? |
| `0x644a6` | — | low | auto-shape | cg-gallery-field? |
| `0x644a7` | — | low | auto-shape | cg-gallery-field? |
| `0x644a8` | — | low | auto-shape | cg-gallery-field? |
| `0x644a9` | — | low | auto-shape | cg-gallery-field? |
| `0x644aa` | — | low | auto-shape | cg-gallery-field? |
| `0x644ab` | — | low | auto-shape | cg-gallery-field? |
| `0x644ac` | — | low | auto-shape | cg-gallery-field? |
| `0x644ad` | — | low | auto-shape | cg-gallery-field? |
| `0x644ae` | — | low | auto-shape | cg-gallery-field? |
| `0x644af` | — | low | auto-shape | cg-gallery-field? |
| `0x644b0` | — | low | auto-shape | cg-gallery-field? |
| `0x644b1` | — | low | auto-shape | cg-gallery-field? |
| `0x644b2` | — | low | auto-shape | cg-gallery-field? |
| `0x644b3` | — | low | auto-shape | cg-gallery-field? |
| `0x644b4` | — | low | auto-shape | cg-gallery-field? |
| `0x644b5` | — | low | auto-shape | cg-gallery-field? |
| `0x644b6` | — | low | auto-shape | cg-gallery-field? |
| `0x644b7` | — | low | auto-shape | cg-gallery-field? |
| `0x644b8` | — | low | auto-shape | cg-gallery-field? |
| `0x644b9` | — | low | auto-shape | cg-gallery-field? |
| `0x644ba` | — | low | auto-shape | cg-gallery-field? |
| `0x644bb` | — | low | auto-shape | cg-gallery-field? |
| `0x644bc` | — | low | auto-shape | cg-gallery-field? |
| `0x644bd` | — | low | auto-shape | cg-gallery-field? |
| `0x644be` | — | low | auto-shape | cg-gallery-field? |
| `0x644bf` | — | low | auto-shape | cg-gallery-field? |
| `0x644c0` | — | low | auto-shape | cg-gallery-field? |
| `0x644c1` | — | low | auto-shape | cg-gallery-field? |
| `0x644c2` | — | low | auto-shape | cg-gallery-field? |
| `0x644c3` | — | low | auto-shape | cg-gallery-field? |
| `0x644c4` | — | low | auto-shape | cg-gallery-field? |
| `0x644c5` | — | low | auto-shape | cg-gallery-field? |
| `0x644c6` | — | low | auto-shape | cg-gallery-field? |
| `0x644c7` | — | low | auto-shape | cg-gallery-field? |
| `0x644c8` | — | low | auto-shape | cg-gallery-field? |
| `0x644c9` | — | low | auto-shape | cg-gallery-field? |
| `0x644ca` | — | low | auto-shape | cg-gallery-field? |
| `0x644cb` | — | low | auto-shape | cg-gallery-field? |
| `0x644cc` | — | low | auto-shape | cg-gallery-field? |
| `0x644cd` | — | low | auto-shape | cg-gallery-field? |
| `0x644ce` | — | low | auto-shape | cg-gallery-field? |
| `0x644cf` | — | low | auto-shape | cg-gallery-field? |
| `0x644d0` | — | low | auto-shape | cg-gallery-field? |
| `0x644d1` | — | low | auto-shape | cg-gallery-field? |
| `0x644d2` | — | low | auto-shape | cg-gallery-field? |
| `0x644d3` | — | low | auto-shape | cg-gallery-field? |
| `0x644d4` | — | low | auto-shape | cg-gallery-field? |
| `0x644d5` | — | low | auto-shape | cg-gallery-field? |
| `0x644d6` | — | low | auto-shape | cg-gallery-field? |
| `0x644d7` | — | low | auto-shape | cg-gallery-field? |
| `0x644d8` | — | low | auto-shape | cg-gallery-field? |
| `0x644d9` | — | low | auto-shape | cg-gallery-field? |
| `0x644da` | — | low | auto-shape | cg-gallery-field? |
| `0x644db` | — | low | auto-shape | cg-gallery-field? |
| `0x644dc` | — | low | auto-shape | cg-gallery-field? |
| `0x644dd` | — | low | auto-shape | cg-gallery-field? |
| `0x644de` | — | low | auto-shape | cg-gallery-field? |
| `0x644df` | — | low | auto-shape | cg-gallery-field? |
| `0x644e0` | — | low | auto-shape | cg-gallery-field? |
| `0x644e1` | — | low | auto-shape | cg-gallery-field? |
| `0x644e2` | — | low | auto-shape | cg-gallery-field? |
| `0x644e3` | — | low | auto-shape | cg-gallery-field? |
| `0x644e4` | — | low | auto-shape | cg-gallery-field? |
| `0x644e5` | — | low | auto-shape | cg-gallery-field? |
| `0x644e6` | — | low | auto-shape | cg-gallery-field? |
| `0x644e7` | — | low | auto-shape | cg-gallery-field? |
| `0x644e8` | — | low | auto-shape | cg-gallery-field? |
| `0x644e9` | — | low | auto-shape | cg-gallery-field? |
| `0x644ea` | — | low | auto-shape | cg-gallery-field? |
| `0x644eb` | — | low | auto-shape | cg-gallery-field? |
| `0x644ec` | — | low | auto-shape | cg-gallery-field? |
| `0x644ed` | — | low | auto-shape | cg-gallery-field? |
| `0x644ee` | — | low | auto-shape | cg-gallery-field? |
| `0x644ef` | — | low | auto-shape | cg-gallery-field? |
| `0x644f0` | — | low | auto-shape | cg-gallery-field? |
| `0x644f1` | — | low | auto-shape | cg-gallery-field? |
| `0x644f2` | — | low | auto-shape | cg-gallery-field? |
| `0x644f3` | — | low | auto-shape | cg-gallery-field? |
| `0x644f4` | — | low | auto-shape | cg-gallery-field? |
| `0x644f5` | — | low | auto-shape | cg-gallery-field? |
| `0x644f6` | — | low | auto-shape | cg-gallery-field? |
| `0x644f7` | — | low | auto-shape | cg-gallery-field? |
| `0x644f8` | — | low | auto-shape | cg-gallery-field? |
| `0x644f9` | — | low | auto-shape | cg-gallery-field? |
| `0x644fa` | — | low | auto-shape | cg-gallery-field? |
| `0x644fb` | — | low | auto-shape | cg-gallery-field? |
| `0x644fc` | — | low | auto-shape | cg-gallery-field? |
| `0x644fd` | — | low | auto-shape | cg-gallery-field? |
| `0x644fe` | — | low | auto-shape | cg-gallery-field? |
| `0x644ff` | — | low | auto-shape | cg-gallery-field? |
| `0x64500` | — | low | auto-shape | cg-gallery-field? |
| `0x64501` | — | low | auto-shape | cg-gallery-field? |
| `0x64502` | — | low | auto-shape | cg-gallery-field? |
| `0x64503` | — | low | auto-shape | cg-gallery-field? |
| `0x64504` | — | low | auto-shape | cg-gallery-field? |
| `0x64505` | — | low | auto-shape | cg-gallery-field? |
| `0x64506` | — | low | auto-shape | cg-gallery-field? |
| `0x64507` | — | low | auto-shape | cg-gallery-field? |
| `0x64508` | — | low | auto-shape | cg-gallery-field? |
| `0x64509` | — | low | auto-shape | cg-gallery-field? |
| `0x6450a` | — | low | auto-shape | cg-gallery-field? |
| `0x6450b` | — | low | auto-shape | cg-gallery-field? |
| `0x6450c` | — | low | auto-shape | cg-gallery-field? |
| `0x6450d` | — | low | auto-shape | cg-gallery-field? |
| `0x6450e` | — | med | auto-shape | cg-gallery-field |
| `0x64c12` | — | low | auto-shape | cg-gallery-field? |
| `0x64c13` | — | low | auto-shape | cg-gallery-field? |
| `0x64c14` | — | low | auto-shape | cg-gallery-field? |
| `0x64c15` | — | low | auto-shape | cg-gallery-field? |
| `0x64c16` | — | low | auto-shape | cg-gallery-field? |
| `0x64c17` | — | low | auto-shape | cg-gallery-field? |
| `0x64c18` | — | low | auto-shape | cg-gallery-field? |
| `0x64c19` | — | low | auto-shape | cg-gallery-field? |
| `0x64c1a` | — | low | auto-shape | cg-gallery-field? |
| `0x64c1b` | — | low | auto-shape | cg-gallery-field? |
| `0x64c1c` | — | low | auto-shape | cg-gallery-field? |
| `0x64c1d` | — | low | auto-shape | cg-gallery-field? |
| `0x64c1e` | — | low | auto-shape | cg-gallery-field? |
| `0x64c1f` | — | low | auto-shape | cg-gallery-field? |
| `0x64c20` | — | low | auto-shape | cg-gallery-field? |
| `0x64c21` | — | low | auto-shape | cg-gallery-field? |
| `0x64c22` | — | low | auto-shape | cg-gallery-field? |
| `0x64c23` | — | low | auto-shape | cg-gallery-field? |
| `0x64c24` | — | low | auto-shape | cg-gallery-field? |
| `0x64c25` | — | low | auto-shape | cg-gallery-field? |
| `0x64c26` | — | low | auto-shape | cg-gallery-field? |
| `0x64c27` | — | low | auto-shape | cg-gallery-field? |
| `0x64c28` | — | low | auto-shape | cg-gallery-field? |
| `0x64c29` | — | low | auto-shape | cg-gallery-field? |
| `0x64c2a` | — | low | auto-shape | cg-gallery-field? |
| `0x64c2b` | — | low | auto-shape | cg-gallery-field? |
| `0x64c2c` | — | low | auto-shape | cg-gallery-field? |
| `0x64c2d` | — | low | auto-shape | cg-gallery-field? |
| `0x64c2e` | — | low | auto-shape | cg-gallery-field? |
| `0x64c2f` | — | low | auto-shape | cg-gallery-field? |
| `0x64c30` | — | low | auto-shape | cg-gallery-field? |
| `0x64c31` | — | low | auto-shape | cg-gallery-field? |
| `0x64c32` | — | low | auto-shape | cg-gallery-field? |
| `0x64c33` | — | low | auto-shape | cg-gallery-field? |
| `0x64c34` | — | low | auto-shape | cg-gallery-field? |
| `0x64c35` | — | low | auto-shape | cg-gallery-field? |
| `0x64c36` | — | low | auto-shape | cg-gallery-field? |
| `0x64c37` | — | low | auto-shape | cg-gallery-field? |
| `0x64c38` | — | low | auto-shape | cg-gallery-field? |
| `0x64c39` | — | low | auto-shape | cg-gallery-field? |
| `0x64c3a` | — | low | auto-shape | cg-gallery-field? |
| `0x64c3b` | — | low | auto-shape | cg-gallery-field? |
| `0x64c3c` | — | low | auto-shape | cg-gallery-field? |
| `0x64c3d` | — | low | auto-shape | cg-gallery-field? |
| `0x64c3e` | — | low | auto-shape | cg-gallery-field? |
| `0x64c3f` | — | low | auto-shape | cg-gallery-field? |
| `0x64c40` | — | low | auto-shape | cg-gallery-field? |
| `0x64c41` | — | low | auto-shape | cg-gallery-field? |
| `0x64c42` | — | low | auto-shape | cg-gallery-field? |
| `0x64c43` | — | low | auto-shape | cg-gallery-field? |
| `0x64c44` | — | low | auto-shape | cg-gallery-field? |
| `0x64c45` | — | low | auto-shape | cg-gallery-field? |
| `0x64c46` | — | low | auto-shape | cg-gallery-field? |
| `0x64c47` | — | low | auto-shape | cg-gallery-field? |
| `0x64c48` | — | low | auto-shape | cg-gallery-field? |
| `0x64c49` | — | low | auto-shape | cg-gallery-field? |
| `0x64c4a` | — | low | auto-shape | cg-gallery-field? |
| `0x64c4b` | — | low | auto-shape | cg-gallery-field? |
| `0x64c4c` | — | low | auto-shape | cg-gallery-field? |
| `0x64c4d` | — | low | auto-shape | cg-gallery-field? |
| `0x64c4e` | — | low | auto-shape | cg-gallery-field? |
| `0x64c4f` | — | low | auto-shape | cg-gallery-field? |
| `0x64c50` | — | low | auto-shape | cg-gallery-field? |
| `0x64c51` | — | low | auto-shape | cg-gallery-field? |
| `0x64c52` | — | low | auto-shape | cg-gallery-field? |
| `0x64c53` | — | low | auto-shape | cg-gallery-field? |
| `0x64c54` | — | low | auto-shape | cg-gallery-field? |
| `0x64c55` | — | low | auto-shape | cg-gallery-field? |
| `0x64c56` | — | low | auto-shape | cg-gallery-field? |
| `0x64c57` | — | low | auto-shape | cg-gallery-field? |
| `0x64c58` | — | low | auto-shape | cg-gallery-field? |
| `0x64c59` | — | low | auto-shape | cg-gallery-field? |
| `0x64c5a` | — | low | auto-shape | cg-gallery-field? |
| `0x64c5b` | — | low | auto-shape | cg-gallery-field? |
| `0x64c5c` | — | low | auto-shape | cg-gallery-field? |
| `0x64c5d` | — | low | auto-shape | cg-gallery-field? |
| `0x64c5e` | — | low | auto-shape | cg-gallery-field? |
| `0x64c5f` | — | low | auto-shape | cg-gallery-field? |
| `0x64c60` | — | low | auto-shape | cg-gallery-field? |
| `0x64c61` | — | low | auto-shape | cg-gallery-field? |
| `0x64c62` | — | low | auto-shape | cg-gallery-field? |
| `0x64c63` | — | low | auto-shape | cg-gallery-field? |
| `0x64c64` | — | low | auto-shape | cg-gallery-field? |
| `0x64c65` | — | low | auto-shape | cg-gallery-field? |
| `0x64c66` | — | low | auto-shape | cg-gallery-field? |
| `0x64c67` | — | low | auto-shape | cg-gallery-field? |
| `0x64c68` | — | low | auto-shape | cg-gallery-field? |
| `0x64c69` | — | low | auto-shape | cg-gallery-field? |
| `0x64c6a` | — | low | auto-shape | cg-gallery-field? |
| `0x64c6b` | — | low | auto-shape | cg-gallery-field? |
| `0x64c6c` | — | low | auto-shape | cg-gallery-field? |
| `0x64c6d` | — | low | auto-shape | cg-gallery-field? |
| `0x64c6e` | — | low | auto-shape | cg-gallery-field? |
| `0x64c6f` | — | low | auto-shape | cg-gallery-field? |
| `0x64c70` | — | low | auto-shape | cg-gallery-field? |
| `0x64c71` | — | low | auto-shape | cg-gallery-field? |
| `0x64c72` | — | low | auto-shape | cg-gallery-field? |
| `0x64c73` | — | low | auto-shape | cg-gallery-field? |
| `0x64c74` | — | low | auto-shape | cg-gallery-field? |
| `0x64c75` | — | low | auto-shape | cg-gallery-field? |
| `0x64c76` | — | low | auto-shape | cg-gallery-field? |
| `0x64c77` | — | low | auto-shape | cg-gallery-field? |
| `0x64c78` | — | low | auto-shape | cg-gallery-field? |
| `0x64c79` | — | low | auto-shape | cg-gallery-field? |
| `0x64c7a` | — | low | auto-shape | cg-gallery-field? |
| `0x64c7b` | — | low | auto-shape | cg-gallery-field? |
| `0x64c7c` | — | low | auto-shape | cg-gallery-field? |
| `0x64c7d` | — | low | auto-shape | cg-gallery-field? |
| `0x64c7e` | — | low | auto-shape | cg-gallery-field? |
| `0x64c7f` | — | low | auto-shape | cg-gallery-field? |
| `0x64c80` | — | low | auto-shape | cg-gallery-field? |
| `0x64c81` | — | low | auto-shape | cg-gallery-field? |
| `0x64c82` | — | low | auto-shape | cg-gallery-field? |
| `0x64c83` | — | low | auto-shape | cg-gallery-field? |
| `0x64c84` | — | low | auto-shape | cg-gallery-field? |
| `0x64c85` | — | low | auto-shape | cg-gallery-field? |
| `0x64c86` | — | low | auto-shape | cg-gallery-field? |
| `0x64c87` | — | low | auto-shape | cg-gallery-field? |
| `0x64c88` | — | low | auto-shape | cg-gallery-field? |
| `0x64c89` | — | low | auto-shape | cg-gallery-field? |
| `0x64c8a` | — | low | auto-shape | cg-gallery-field? |
| `0x64c8b` | — | low | auto-shape | cg-gallery-field? |
| `0x64c8c` | — | low | auto-shape | cg-gallery-field? |
| `0x64c8d` | — | low | auto-shape | cg-gallery-field? |
| `0x64c8e` | — | low | auto-shape | cg-gallery-field? |
| `0x64c8f` | — | low | auto-shape | cg-gallery-field? |
| `0x64c90` | — | low | auto-shape | cg-gallery-field? |
| `0x64c91` | — | low | auto-shape | cg-gallery-field? |
| `0x64c92` | — | low | auto-shape | cg-gallery-field? |
| `0x64c93` | — | low | auto-shape | cg-gallery-field? |
| `0x64c94` | — | low | auto-shape | cg-gallery-field? |
| `0x64c95` | — | low | auto-shape | cg-gallery-field? |
| `0x64c96` | — | low | auto-shape | cg-gallery-field? |
| `0x64c97` | — | low | auto-shape | cg-gallery-field? |
| `0x64c98` | — | low | auto-shape | cg-gallery-field? |
| `0x64c99` | — | low | auto-shape | cg-gallery-field? |
| `0x64c9a` | — | low | auto-shape | cg-gallery-field? |
| `0x64c9b` | — | low | auto-shape | cg-gallery-field? |
| `0x64c9c` | — | low | auto-shape | cg-gallery-field? |
| `0x64c9d` | — | low | auto-shape | cg-gallery-field? |
| `0x64c9e` | — | low | auto-shape | cg-gallery-field? |
| `0x64c9f` | — | low | auto-shape | cg-gallery-field? |
| `0x64ca0` | — | low | auto-shape | cg-gallery-field? |
| `0x64ca1` | — | low | auto-shape | cg-gallery-field? |
| `0x64ca2` | — | low | auto-shape | cg-gallery-field? |
| `0x64ca3` | — | low | auto-shape | cg-gallery-field? |
| `0x64ca4` | — | low | auto-shape | cg-gallery-field? |
| `0x64ca5` | — | low | auto-shape | cg-gallery-field? |
| `0x64ca6` | — | low | auto-shape | cg-gallery-field? |
| `0x64ca7` | — | low | auto-shape | cg-gallery-field? |
| `0x64ca8` | — | low | auto-shape | cg-gallery-field? |
| `0x64ca9` | — | low | auto-shape | cg-gallery-field? |
| `0x64caa` | — | low | auto-shape | cg-gallery-field? |
| `0x64cab` | — | low | auto-shape | cg-gallery-field? |
| `0x64cac` | — | low | auto-shape | cg-gallery-field? |
| `0x64cad` | — | low | auto-shape | cg-gallery-field? |
| `0x64cae` | — | low | auto-shape | cg-gallery-field? |
| `0x64caf` | — | low | auto-shape | cg-gallery-field? |
| `0x64cb0` | — | low | auto-shape | cg-gallery-field? |
| `0x64cb1` | — | low | auto-shape | cg-gallery-field? |
| `0x64cb2` | — | low | auto-shape | cg-gallery-field? |
| `0x64cb3` | — | low | auto-shape | cg-gallery-field? |
| `0x64cb4` | — | low | auto-shape | cg-gallery-field? |
| `0x64cb5` | — | low | auto-shape | cg-gallery-field? |
| `0x64cb6` | — | low | auto-shape | cg-gallery-field? |
| `0x64cb7` | — | low | auto-shape | cg-gallery-field? |
| `0x64cb8` | — | low | auto-shape | cg-gallery-field? |
| `0x64cb9` | — | low | auto-shape | cg-gallery-field? |
| `0x64cba` | — | low | auto-shape | cg-gallery-field? |
| `0x64cbb` | — | low | auto-shape | cg-gallery-field? |
| `0x64cbc` | — | low | auto-shape | cg-gallery-field? |
| `0x64cbd` | — | low | auto-shape | cg-gallery-field? |
| `0x64cbe` | — | low | auto-shape | cg-gallery-field? |
| `0x64cbf` | — | low | auto-shape | cg-gallery-field? |
| `0x64cc0` | — | low | auto-shape | cg-gallery-field? |
| `0x64cc1` | — | low | auto-shape | cg-gallery-field? |
| `0x64cc2` | — | low | auto-shape | cg-gallery-field? |
| `0x64cc3` | — | low | auto-shape | cg-gallery-field? |
| `0x64cc4` | — | low | auto-shape | cg-gallery-field? |
| `0x64cc5` | — | low | auto-shape | cg-gallery-field? |
| `0x64cc6` | — | low | auto-shape | cg-gallery-field? |
| `0x64cc7` | — | low | auto-shape | cg-gallery-field? |
| `0x64cc8` | — | low | auto-shape | cg-gallery-field? |
| `0x64cc9` | — | low | auto-shape | cg-gallery-field? |
| `0x64cca` | — | low | auto-shape | cg-gallery-field? |
| `0x64ccb` | — | low | auto-shape | cg-gallery-field? |
| `0x64ccc` | — | low | auto-shape | cg-gallery-field? |
| `0x64ccd` | — | low | auto-shape | cg-gallery-field? |
| `0x64cce` | — | low | auto-shape | cg-gallery-field? |
| `0x64ccf` | — | low | auto-shape | cg-gallery-field? |
| `0x64cd0` | — | low | auto-shape | cg-gallery-field? |
| `0x64cd1` | — | low | auto-shape | cg-gallery-field? |
| `0x64cd2` | — | low | auto-shape | cg-gallery-field? |
| `0x64cd3` | — | low | auto-shape | cg-gallery-field? |
| `0x64cd4` | — | low | auto-shape | cg-gallery-field? |
| `0x64cd5` | — | low | auto-shape | cg-gallery-field? |
| `0x64cd6` | — | low | auto-shape | cg-gallery-field? |
| `0x64cd7` | — | low | auto-shape | cg-gallery-field? |
| `0x64cd8` | — | low | auto-shape | cg-gallery-field? |
| `0x64cd9` | — | low | auto-shape | cg-gallery-field? |
| `0x64cda` | — | low | auto-shape | cg-gallery-field? |
| `0x64cdb` | — | low | auto-shape | cg-gallery-field? |
| `0x64cdc` | — | low | auto-shape | cg-gallery-field? |
| `0x64cdd` | — | low | auto-shape | cg-gallery-field? |
| `0x64cde` | — | med | auto-shape | cg-gallery-field |
| `0x6638b` | — | med | auto-shape | record-table[stride 15] |
| `0x66713` | — | low | auto-shape | index/counter? |
| `0x66715` | — | med | auto-shape | current-entity-index? |
| `0x7843e` | — | med | auto-shape | unit-field |
| `0x81c96` | — | med | auto-shape | record-table[stride 3] |
| `0x8284e` | — | med | auto-shape | record-table[stride 3] |
| `0xaac76` | — | low | auto-shape | index/counter? |
| `0xaacf0` | — | med | auto-shape | record-table[stride 5] |
| `0xaad86` | — | med | auto-shape | record-table[stride 55] |
| `0xab3f8` | — | med | auto-shape | record-table[stride 15] |
| `0xab5ba` | — | med | auto-shape | record-table[stride 20] |
| `0xab6fa` | — | med | auto-shape | record-table[stride 11] |
| `0xab7d6` | — | med | auto-shape | record-table[stride 11] |
| `0xab8b2` | — | med | auto-shape | record-table[stride 7] |
| `0xaba96` | — | med | auto-shape | record-table[stride 27] |
| `0xb240e` | — | med | auto-shape | record-table[stride 27] |
| `0xb8d86` | — | med | auto-shape | record-table[stride 27] |
| `0xbf6fe` | — | med | auto-shape | record-table[stride 27] |
| `0xc6077` | — | med | auto-shape | record-table[stride 27] |
| `0xcc9f0` | — | low | auto-shape | index/counter? |
| `0xcca08` | — | med | auto-shape | record-table[stride 5] |
| `0xccbdc` | — | med | auto-shape | record-table[stride 14] |
| `0xccc0b` | — | low | auto-shape | index/counter? |
| `0xccc33` | — | med | auto-shape | record-table[stride 3] |
| `0xccc93` | — | med | auto-shape | record-table[stride 53] |
| `0xe6afe` | — | med | auto-shape | record-table[stride 10] |
| `0xe6fe2` | — | med | auto-shape | record-table[stride 3] |
| `0xe71d6` | — | med | auto-shape | record-table[stride 3] |
| `0xe865d` | — | med | auto-shape | record-table[stride 7] |
| `0xea1b5` | — | med | auto-shape | record-table[stride 7] |
| `0xee035` | — | med | auto-shape | record-table[stride 3] |
| `0xeefd5` | — | med | auto-shape | record-table[stride 3] |
| `0xeff78` | — | med | auto-shape | record-table[stride 20] |
| `0xf4d98` | — | med | auto-shape | record-table[stride 20] |
| `0xf9bb8` | — | med | auto-shape | record-table[stride 20] |
| `0xfe9d8` | — | med | auto-shape | record-table[stride 20] |
| `0x1037f8` | — | med | auto-shape | record-table[stride 20] |
| `0x108618` | — | med | auto-shape | record-table[stride 20] |
| `0x112258` | — | med | auto-shape | record-table[stride 20] |
| `0x117078` | — | med | auto-shape | record-table[stride 20] |
| `0x11be98` | — | med | auto-shape | record-table[stride 20] |
| `0x120cb8` | — | med | auto-shape | record-table[stride 20] |
| `0x125ad8` | — | med | auto-shape | record-table[stride 20] |
| `0x12a8f8` | — | med | auto-shape | record-table[stride 20] |
| `0x147db8` | — | med | auto-shape | record-table[stride 20] |
| `0x14cbd8` | — | med | auto-shape | record-table[stride 20] |
| `0x1519f8` | — | med | auto-shape | current-entity-index? |
| `0x151a5d` | — | med | auto-shape | record-table[stride 3] |
| `0x151b89` | — | med | auto-shape | record-table[stride 3] |
| `0x151de1` | — | med | auto-shape | record-table[stride 3] |
| `0x151f0d` | — | med | auto-shape | record-table[stride 3] |
| `0x152101` | — | med | auto-shape | record-table[stride 3] |
| `0x15222d` | — | med | auto-shape | record-table[stride 3] |
| `0x152486` | — | med | auto-shape | record-table[stride 3] |
| `0x15261a` | — | low | auto-shape | index/counter? |
| `0x15261b` | — | low | auto-shape | index/counter? |
| `0x15261f` | — | med | auto-shape | record-table[stride 300] |
| `0x152878` | — | low | auto-shape | index/counter? |
| `0x15288b` | — | low | auto-shape | index/counter? |
| `0x15288c` | — | med | auto-shape | current-entity-index? |
| `0x15288e` | — | med | auto-shape | record-table[stride 6] |
| `0x153ffe` | — | med | auto-shape | record-table[stride 6] |
| `0x155baa` | — | med | auto-shape | record-table[stride 3] |
| `0x155bbc` | — | med | auto-shape | record-table[stride 3] |
| `0x155bfb` | — | med | auto-shape | record-table[stride 3] |
| `0x155cb8` | — | med | auto-shape | record-table[stride 10] |
| `0x155d8a` | — | med | auto-shape | record-table[stride 10] |
| `0x155e9b` | — | med | auto-shape | record-table[stride 14] |
| `0x156015` | — | med | auto-shape | record-table[stride 10] |
| `0x1569e4` | — | med | auto-shape | record-table[stride 2] |
| `0x1571b4` | — | med | auto-shape | record-table[stride 2] |
| `0x158154` | — | med | auto-shape | record-table[stride 4] |
| `0x1590f4` | — | med | auto-shape | record-table[stride 4] |
| `0x15a097` | — | med | auto-shape | record-table[stride 4] |
| `0x15a2a9` | — | med | auto-shape | record-table[stride 3] |
| `0x15a75b` | — | med | auto-shape | record-table[stride 21] |
| `0x15a785` | — | med | auto-shape | record-table[stride 3] |

