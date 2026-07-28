<!-- DO NOT EDIT -- generated from vm-map/globals.toml by tools/globals_build.py --build -->
# Global Variable Reference (generated)

4278 globals (446 curated, 3832 auto shape-inferred). Source of truth: `vm-map/globals.toml`.

## choice-output

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x0` | system_flow_request | high | investigation | Return-mode request shared by TITLE/GAMESTART and SYSTEM4. The natural New Game path writes 1 immediately after GAMESTART's TUNE call; TITLE returns and SYSTEM4 routes value 1 into its ADV scene loop. Values 2/3/5 route to FORT/FIELD/CAMP; other writers use the same system-level request channel. |
| `0x699` | next_script_resource_id | high | investigation | SYSTEM4's computed child-script resource id. On the normal ADV path SYSTEM4 copies G[0x87a57][scjump_decision_out] here, substitutes raw id 0x22 (SC0000.BIN) when zero, executes call-script through this cell at offset 0x477, then clears it after the child returns. |
| `0xa68` | — | med | auto-shape | TODO: confirm. Branch-read in 11 scenes / 12 scripts; compared against [0, 1]; writers=['SC0740.BIN', 'SC1580.BIN', 'SC1590.BIN']. |
| `0x62ccc` | scjump_decision_out2 | low | inference | Adjacent to scjump_decision_out (0x62ccf) in the 0x62ccc-0x62ccf progression decision-output cluster; same 136-scene reach, written by CAMP/CLOSE/DEBUGADV. INFERENCE from adjacency — confirm meaning before relying on it. |
| `0x62ccf` | scjump_decision_out | high | investigation | SCJUMP's selected progression decision id. Its chapter/flag decision tree writes one of 847 distinct values across 1,755 sites; SYSTEM4, FIELD, SALLY, and TRAIN use it to index scjump_scene_script_resource_ids and dispatch the corresponding SCxxxx scene script. |
| `0xcc9f1` | movement_search_mode | high | investigation | Transient MVSEEK mode. Zero replaces the caller-supplied origin with the current entity tile and uses movement+1 as the search allowance; nonzero modes retain the caller coordinate and use a broad allowance of 9999. Mode 2 additionally masks the doubled-coordinate terrain cells occupied by active entities of another faction before the flood fill. MVSEEK resets the mode to zero on exit. |
| `0xeff77` | routine_execution_state | high | investigation | Shared movement/battle routine result state. MVRTN and BTRTN clear it before scanning steps; a nonzero provider result stops the scan. Movement providers use state 1 for ordinary field movement/progress, state 2 for an immediate offensive battle action, and state 3 for an immediate healing/support action; MVRTN supplies state 1 when no provider produced another result. |

## counter

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x671b` | shared_spendable_points | high | investigation | Shared spendable point balance used by the alchemy, study, evolution, and summoning systems. ALCHEMY requires and deducts each recipe's alchemy_recipe_point_costs value; STAGECLEAR awards points to this balance and caps it at 999. The exact player-facing Japanese resource label remains unresolved. |
| `0x671c` | item_tuning_facility_level | high | investigation | Current equipment-tuning facility level. IMPROVE indexes facility_level_progress_thresholds row 0 with this value and raises it, up to 6, as item_tuning_facility_progress accumulates. |
| `0x671d` | alchemy_level | high | investigation | Current alchemy level. ALCHEMY hides recipes whose alchemy_recipe_minimum_levels value exceeds this level and raises it, up to 6, when alchemy_level_progress reaches the current threshold. |
| `0x671e` | magic_facility_level | high | investigation | Current magic/research facility level. MAGIC and USEMAGIC index facility_level_progress_thresholds row 2 with this level; USEMAGIC raises it up to 6 when magic_facility_progress reaches the current threshold. |
| `0x671f` | item_tuning_facility_progress | high | investigation | Progress toward the next equipment-tuning facility level. IMPROVE adds the selected item's tuning-level increases, applies row 0 of facility_level_progress_thresholds, and carries or clamps progress when the level rises. |
| `0x6720` | alchemy_level_progress | high | investigation | Progress toward the next alchemy level. Each successful ALCHEMY synthesis increments it; reaching the current threshold raises alchemy_level and resets this counter. |
| `0x6721` | magic_facility_progress | high | investigation | Progress toward the next magic/research facility level. USEMAGIC adds the selected action's progress award and applies row 2 of facility_level_progress_thresholds; MAGIC renders the same current/threshold pair. |
| `0x6722` | familiar_alignment | high | investigation | The familiar's alignment/personality axis. TRAIN decodes TRINIT minimum and maximum gates by subtracting 100 from the stored threshold, matching the locked hints' kind-versus-evil wording, and applies training_action_alignment_delta_hundredths through the paired fractional accumulator. DRAWCHP renders the signed value and EVOLVE/scene scripts use the same axis. |
| `0x6723` | familiar_alignment_fraction | high | investigation | Fractional accumulator paired with familiar_alignment. TRAIN adds the selected action's hundredths delta, applies the integral quotient and probabilistic remainder, and preserves the remaining fraction. |
| `0x6724` | familiar_training_progress | high | investigation | Cumulative familiar training/sexual-magic progress. TRAIN tests it against TRINIT's minimum/maximum progress gates, then applies training_action_training_progress_delta_hundredths through the paired fractional accumulator and clamps the result to 99. DRAWCHP and EVOLVE display or compare the same value. |
| `0x6725` | familiar_training_progress_fraction | high | investigation | Fractional accumulator paired with familiar_training_progress. TRAIN adds the action's hundredths delta, advances the integer value by the quotient and probabilistic remainder, and preserves the remaining fraction. |
| `0x6726` | training_action_total_execution_count | high | investigation | TRAIN increments this once after every successful action, independently of the selected action's own execution count. DRAWCHP displays it with the familiar's other training statistics and SAVE persists it. |
| `0x6727` | training_action_execution_counts | high | investigation | Per-action completed execution counts. TRAIN uses the prior count as the column in training_action_event_story_flag_ids, increments the selected cell, and treats a zero next event as the cap; GAMESTART restores each count and replays its completed event flags. |
| `0x204f4` | current_stage_turn | high | investigation | FIELD initializes this to 1, increments it after end-of-turn processing, compares it with STINIT's stage turn limit and timed object schedules, and displays it through DRAWCHP. Card generation divides it by each CDINIT growth interval to increase that card's weighted-selection share. |
| `0x204f8` | party_reference_level | high | investigation | FIELD's enemy auto-level reference. It averages all eligible party units when at most five exist, otherwise the five highest-level units. SETEN raises scalable enemies above unit_starting_level by the positive gap divided by the selected auto-level divisor before applying stage level clamps. Difficulty separately adds -5/0/+5 stat-growth iterations and does not change this stored runtime level. |
| `0x20530` | current_spirit | high | investigation | Current 精気 (spirit/essence) resource. TRAIN rejects a selected action when adding its negative TRINIT spirit delta would fall below zero, deducts the cost on execution, and passes the updated value to DRAWCHP. Field and scene reward paths update the same resource and clamp it to maximum_spirit. |
| `0x20534` | maximum_spirit | high | investigation | Maximum 精気 capacity paired with current_spirit. TRAIN clamps the post-cost current value against it, DRAWCHP renders the current/maximum gauge, and field/scene reward paths raise or restore the same capacity. |
| `0x4dfbb` | stage_card_spendable_point_bonus | high | investigation | FIELD clears this on stage entry and adds CDINIT2 type-3 card bonuses. STAGECLEAR adds the accumulated amount to its computed stage award before increasing shared_spendable_points. |
| `0x4dfbc` | scjump_progress_a | med | inference | Dominant SCJUMP switch input (1609 comparison reads) — a per-chapter story-progress counter/position the progression machine branches on. INFERENCE from SCJUMP usage; confirm exact meaning via a listing/playthrough. |
| `0x665d6` | modal_message_line_count | high | investigation | Number of populated strings in modal_message_lines. Dozens of menu/gameplay producers append at lines[count] and increment this value; MES renders the resulting non-selecting modal and clears the count, while SBUNKI consumes the same buffer as selectable options. |
| `0x665e2` | modal_annotation_count | high | investigation | Number of populated modal_annotation_texts entries. MES and SBUNKI iterate this many annotations and clear it on dismissal; no direct shipped producer was found. |
| `0xaac77` | current_condition_delta_levels | high | investigation | Shared signed level delta passed to ADDILL/ADDILLSUB for current_condition_id. Positive values apply or strengthen a condition; negative values weaken/remove it subject to the equipment/passive baseline floor. |
| `0xcc9f2` | usable_action_min_range | high | investigation | CALCSCOPE initializes this from the equipped/default attack's minimum range and widens it for usable offensive and healing skills. It is the lower bound shared by the offensive and healing action-scope tables. |
| `0xcc9f3` | usable_action_max_range | high | investigation | CALCSCOPE initializes this from the equipped/default attack's maximum range and widens it for usable offensive and healing skills. ATSEEK bounds its action-range flood fill with this value; RTN_M051/052 and RTN_M061 use it as their upper scan bound. |
| `0xe6c5d` | scjump_progress_d | med | inference | SCJUMP switch input (168 comparison reads) — progression counter/position. INFERENCE from SCJUMP usage. |
| `0x152619` | battle_exchange_step | high | investigation | BTL's six-step alternating exchange counter. Dividing it by two selects the first, second, or third ordinary-attack/skill voice variant and related animation slots. |

## data-table

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x5` | save_slot_portrait_entity_indices | high | investigation | SAVE and SELSTAGE snapshot current_entity_index through op 0x1a2. SAVE.BIN restores the slot cell with op 0x1a3 and uses its form range when choosing the portrait atlas region. |
| `0xd7` | save_slot_protagonist_levels | high | investigation | SAVE and SELSTAGE copy unit_current_levels[current_entity_index] into this shared SAVE.DAT-selected bank. SAVE.BIN restores and renders it as the row's LV value. |
| `0x1a9` | save_slot_training_action_counts | high | investigation | SAVE and SELSTAGE snapshot training_action_total_execution_count into this shared SAVE.DAT-selected per-slot bank; copy/move/delete operations keep it aligned with the rest of the preview record. |
| `0x27b` | save_slot_growth_values | high | investigation | SAVE and SELSTAGE snapshot familiar_training_progress through op 0x1a2. SAVE.BIN restores and renders it as the row's growth value. |
| `0x34d` | save_slot_personality_values | high | investigation | SAVE and SELSTAGE snapshot familiar_alignment through op 0x1a2. SAVE.BIN restores and renders the signed value in the row's personality field. |
| `0x41f` | save_slot_difficulty_indices | high | investigation | SAVE snapshots difficulty_index through op 0x1a2. SAVE.BIN restores the per-slot value and selects the corresponding 34x17 row badge from SO010. |
| `0x4f1` | save_slot_cleared_ending_masks | high | investigation | SAVE snapshots the current cleared-ending inheritance mask through op 0x1a2. SAVE.BIN restores it from shared SAVE.DAT, scans bits 0..14, and draws one 17x17 badge for every achieved ending on that slot and subsequent inherited-loop saves. |
| `0x5c3` | save_slot_append_install_masks | high | investigation | SAVE snapshots the current installed-append mask through op 0x1a2. SAVE.BIN compares each saved bit against the current mask and marks previews whose required append data is missing. |
| `0x6d3` | story_event_flags | high | investigation | Shared one-based story/event flag bank. Progression, recruitment, item, and stage logic read or write individual cells; STINIT object prerequisite ids are decremented by one before SETOBJ tests this array. |
| `0x2e49` | character_voice_suppressed | high | investigation | Base of the per-character voice enable/suppress settings. INITCONFIG zero-fills all 13 cells and registers each with the shared profile service; LOADCONFIG restores them. CONFIG indexes the table to preview a character voice and write 0/1. ROOM reads cell 0 before assigning its selected greeter's greeting/farewell voice ids, so the port's former scalar interpretation of zero-int-range (writing 13 into the base cell) suppressed those voices on every natural boot. This names the script-visible setting array without choosing a persistence backend for op 0x1a2/0x1a3. |
| `0x3239` | adv_layer_surface_slots | high | investigation | Shared ADV graphics-layer surface-slot registry. Scene setup initializes the three banks. The CG loader uses the primary slot for a fresh retained object, alternates between columns zero and one when replacing an already-bound layer, and uses column two for the transition/crossfade surface and cleanup. The corpus has 2,657 table-base accesses across 309 scripts. Columns: 0=primary_surface_slot, 1=alternate_surface_slot, 2=transition_surface_slot. |
| `0x453b` | training_action_text | high | investigation | TRINIT's six-string row for each of 21 training/sexual-magic actions. TRAIN renders columns 0..2 for the action description and cost/reward summary, or columns 3..5 for the unmet-condition hint. Columns: 0=description_line_1, 1=description_line_2, 2=description_line_3, 3=locked_hint_line_1, 4=locked_hint_line_2, 5=locked_hint_line_3. |
| `0x65ce` | skill_acquired_flags | high | investigation | Persistent acquired-skill flags. ADDSKILL sets the selected skill after resolving the unit's equipped-skill slots; FORT checks the flag before granting a skill; CHMENU combines it with skill_change_catalog_eligible to build the available skill-change catalog. |
| `0x673c` | party_slot_flags | high | investigation | Per-party-slot state flags for slots 0..99. UNITECH creates the initial unit by setting slot 2 to 0x13; CALCARR counts slots whose flags intersect 0x6, and CHMENU includes slots with bit 1 set. Exact meanings of the remaining bits are not yet classified. |
| `0x67a0` | party_slot_character_id | high | investigation | Character/unit definition id stored for each party slot. UNITECH writes character id 2 into initial slot 2 on a natural New Game; CHMENU reads this table for every active party_slot_flags entry when constructing its roster. |
| `0x6930` | unit_current_levels | high | investigation | Persistent current level by playable-unit id. CCINIT compares current_unit_id's row with each promotion threshold; ADDEXP increments and reports the same value. |
| `0x6994` | unit_experience_progress | high | investigation | Persistent experience progress by playable-unit id. ADDEXP adds the current award, animates the increase to 100, raises unit_current_levels, resets this cell to zero, and repeats while award remains and the unit is below unit_level_cap. |
| `0x69f8` | unit_current_stats | high | investigation | Persistent fourteen-stat row for each playable unit, using the same column order as unit_base_stats. CALCCC adds class_change_stat_bonuses to the current unit's row and clamps each result to the shared stat caps. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power, 10=movement, 11=max_hp, 12=max_sp, 13=max_fs. |
| `0x6f70` | unit_stat_growth_fractions | high | investigation | Persistent fractional-growth row paired with each playable unit's unit_current_stats. ADDEXP adds unit_stat_growth_rates, TRAIN adds training_action_stat_growth_hundredths, and UNITECH applies level catch-up growth; each path awards the quotient divided by 100 to the current stat and retains the remainder modulo 100. GAMESTART serializes the rows, while GAMECLEAR and EVOLVE copy them with the rest of the persistent unit record. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power, 10=movement, 11=max_hp, 12=max_sp, 13=max_fs. |
| `0x74e8` | unit_skill_ids | high | investigation | Persistent four-skill row for each playable unit. CALCCC copies positive class_change_skill_awards into the first three slots after a promotion; ADDEXP compares the before/after row to report learned or replaced skills. Columns: 0=skill_slot_1, 1=skill_slot_2, 2=skill_slot_3, 3=skill_slot_4. |
| `0x7684` | stage_clear_state | high | investigation | Persistent per-stage completion state indexed by stage id. STAGECLEAR sets the current stage cell to 1, while FORT, SELSTAGE, and FIELD use zero versus one to distinguish an uncleared mission from a cleared replay. |
| `0x141a4` | stage_object_runtime_state | high | investigation | Per-stage, per-object-slot runtime state. FIELD initializes mode-1 objects from STINIT's first tagged payload on a fresh stage and updates capturable-object ownership; DRAWOBJ uses it as the vertical sprite-row index. RTN_M015 treats the state of Magic Pillar types 2..4 as their controlling faction and targets pillars whose value differs from the acting entity's faction. |
| `0x20543` | tile_faction_traversal_masks | high | investigation | Row-major tile traversal permissions. Movement checks the acting entity's faction bit before enqueuing a tile in movement-limited MVSEEK searches. RTN_M013 builds either one selected faction bit or all bits except the actor's, requires the current tile not to match that set, and approaches the nearest reachable tile whose mask does. |
| `0x341ab` | current_stage_terrain_grid | high | investigation | Current stage's mutable terrain grid. FIELD clears all 2,000 rows and copies the selected stage_terrain_atlas rectangle; rendering, minimap, occupancy, battle, and movement scripts read it, while SETLAND/DELLAND alter cells and RESETLAND restores atlas values. |
| `0x4dfbd` | entity_unit_definition_ids | high | investigation | Definition id backing each runtime map entity. SETEN writes current_unit_id when materializing an enemy; BTL uses the opposing entity's value to read unit_experience_reward and drop tables, while ADDEXP uses the faction-1 participant's value to select persistent playable-unit state. |
| `0x4e021` | entity_runtime_flags | high | investigation | Per-runtime-entity state flags. RTN_M007 requires bit 0 when selecting an injured ally, and MVSEEK mode 2 requires it before masking a foreign entity's occupied terrain cell. |
| `0x4e053` | entity_levels | high | investigation | Displayed/runtime level for each map entity. SETEN begins enemies at unit_starting_level, applies party-reference auto-scaling and stage minimum/maximum clamps, and BTL subtracts the surviving faction-1 participant's level from the opponent's level to choose the experience multiplier bracket. |
| `0x4e085` | entity_current_resources | high | investigation | Per-runtime-entity current resources. DRAWCHP/DRAWENP render columns 0..2 against max-stat columns 11..13, skill/item resource deltas modify the same three columns, and movement search compares path cost with current_fs. Columns: 0=current_hp, 1=current_sp, 2=current_fs. |
| `0x4e11b` | entity_effective_stats | high | investigation | Per-runtime-entity effective fourteen-stat row after CALCREVISE applies base data, equipment, skills, and conditions. Combat/UI consumers use columns 0..10 directly; columns 11..13 are the maxima paired with entity_current_resources HP/SP/FS. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power, 10=movement, 11=max_hp, 12=max_sp, 13=max_fs. |
| `0x4e693` | entity_skill_flags | high | investigation | Per-runtime-entity skill-state row. CALCREVISE and CALCSCOPE mark equipped skills, combat/field consumers test specific skill ids, and RTN_M009 requires skill 22 (Unlock) before treating an unopened type-7 chest as a treasure target. |
| `0x5212b` | entity_equipped_active_skill_ids | high | investigation | Four equipped active-skill slots per runtime entity. CALCSCOPE maps each usable offensive or healing skill into the corresponding action bit and range band; RTN_M051/052 select offensive skills and RTN_M061 selects a healing skill from these slots. |
| `0x52289` | entity_selected_action_ids | high | investigation | Per-entity selected action. RTN_M051/052 write either zero for the normal attack or an offensive equipped skill and return routine execution state 2; RTN_M061 writes a healing skill and returns state 3. BTRTN may replace the value through a battle-provider decision. |
| `0x522ed` | entity_faction_ids | high | investigation | Per-runtime-entity faction id. RTN_M007 restricts its injured-unit target to the acting entity's faction; MVSEEK mode 2 compares this value while masking active foreign-entity cells, and ordinary movement checks the acting faction's bit in tile passability masks. |
| `0x5231f` | entity_tile_x | high | investigation | Per-runtime-entity map X coordinate. FIELD movement and occupancy logic maintain it; movement providers pair it with entity_tile_y and compare or route from the current tile. |
| `0x52351` | entity_tile_y | high | investigation | Per-runtime-entity map Y coordinate paired with entity_tile_x. Values use the same stage map-space cell keys as STINIT object and enemy coordinates. |
| `0x52383` | entity_condition_levels | high | investigation | Per-runtime-entity current condition levels. ADDILLSUB applies signed level deltas, clamps the minimum to entity_condition_baseline_levels and the maximum to 5, and assigns condition duration. CALCREVISE, FIELD, combat, and UI consumers index the same ILINIT-defined 30-column condition id space. Columns: 1=instant_death, 2=hp_drain, 3=sp_drain, 4=fs_drain, 5=curse, 6=charm, 7=confusion, 8=paralysis, 9=poison, 10=water_flow, 11=fear, 12=reserved, 13=regeneration, 14=exaltation. |
| `0x5295f` | entity_condition_remaining_turns | high | investigation | Per-runtime-entity duration counters paired with entity_condition_levels. FIELD decrements positive counters and removes the condition when a counter reaches zero; DRAWENP displays negative values as infinity. RECOVER writes -1 when a recoverable condition retains an equipment/passive baseline and zero when it clears completely. Columns: 1=instant_death, 2=hp_drain, 3=sp_drain, 4=fs_drain, 5=curse, 6=charm, 7=confusion, 8=paralysis, 9=poison, 10=water_flow, 11=fear, 12=reserved, 13=regeneration, 14=exaltation. |
| `0x52f3b` | entity_condition_baseline_levels | high | investigation | Per-runtime-entity minimum condition levels supplied by equipment and passive effects. SETCH/SETEN populate the row from item_equipped_status_levels, ADDILLSUB cannot reduce current levels below it, CALCREVISE separates transient current levels from this baseline, and RECOVER restores eligible conditions to it. Columns: 1=instant_death, 2=hp_drain, 3=sp_drain, 4=fs_drain, 5=curse, 6=charm, 7=confusion, 8=paralysis, 9=poison, 10=water_flow, 11=fear, 12=reserved, 13=regeneration, 14=exaltation. |
| `0x53517` | entity_movement_routine_set_ids | high | investigation | Per-runtime-entity movement routine set for easy/normal/hard. SETEN copies a stage-specific override or the EBINIT default into this row; MVRTN selects the current difficulty column and stores it in current_routine_set_id. |
| `0x535ad` | entity_battle_routine_set_ids | high | investigation | Per-runtime-entity battle routine set for easy/normal/hard. SETEN copies a stage-specific override or the EBINIT default into this row; BTRTN selects the current difficulty column and stores it in current_routine_set_id. |
| `0x53643` | entity_movement_routine_progress | high | investigation | Per-runtime-entity movement-routine progress counters. FIELD clears all fifty rows, movement providers increment the current step, and MVRTN compares it with movement_routine_minimum_progress_counts before enabling a step. |
| `0x53e13` | entity_carried_item_ids | high | investigation | Two carried/drop-item slots per runtime entity. SETEN initializes slot 0 from the unit's starting equipment item, FIELD fills an empty or matching slot when treasure is collected, and RTN_M009 requires an empty slot or an item-id match before approaching a chest/treasure object. Columns: 0=slot_0, 1=slot_1. |
| `0x53e77` | entity_carried_item_counts | high | investigation | Quantities for the two per-entity carried/drop-item slots. SETEN seeds a starting item with quantity one; FIELD adds treasure quantities to the matching slot and caps the result at 999. Columns: 0=slot_0, 1=slot_1. |
| `0x53ede` | training_action_unlock_flags | high | investigation | Persistent per-action discovered/unlocked state maintained by TRAIN after evaluating eligibility. TRAIN uses it while choosing the locked-versus-known menu presentation, GAMESTART restores every cell, and SAVE persists the same block. |
| `0x56223` | unit_story_speaker_seen_flags | high | investigation | Scene scripts set and persist a unit's cell when drawing that speaker's name. CONFIG reloads the twelve CVINIT-mapped unit cells and enables each named character-voice control only after its speaker has been encountered. |
| `0x5660b` | skill_info_revealed_flags | high | investigation | Persistent skill-information visibility flags. ADDSKILL sets the selected skill, BTL marks every equipped skill when it is observed in combat, and INFOIT suppresses a skill's icon/handler-driven details until this flag is nonzero. This is broader than skill_acquired_flags. |
| `0x56738` | entity_battle_routine_random_rolls | high | investigation | Per-runtime-entity battle-step random rolls. FIELD and BTL fill every active entity's twenty cells with random-modulo-100; BTRTN executes a step when the matching roll is below battle_routine_activation_percents. |
| `0x56b20` | entity_patrol_waypoint_indices | high | investigation | RTN_M011 compares each step's one-based waypoint_ordinal minus one with the current entity's value. Reaching the selected destination advances this value modulo the largest RTN_M011 waypoint ordinal in the routine set. |
| `0x56b85` | enemy_encyclopedia_revealed_flags | high | investigation | Persistent enemy-information reveal state. BTL marks both participating unit definitions as revealed and synchronizes their cells; INFOEN masks unrevealed unit names and details while still listing EBINIT rows enabled by unit_enemy_info_listed. |
| `0x57357` | unit_deployment_cost_adjustments | high | investigation | Persistent per-unit delta added to unit_deployment_cost_base. CALCCC increments it by class_change_deployment_cost_delta, while SETCH, SETEN, ADDEXP, and deployment scripts include it when constructing or updating runtime command cost. |
| `0x573bb` | unit_class_change_state | high | investigation | Persistent ten-slot class-change state for each playable unit. CALCCC copies the current unit's row to class_change_state_work before invoking the rule scripts and persists the updated row; CCINIT requires the rule's slot to be zero so each promotion is applied once. Columns: 0=promotion_slot_1, 1=promotion_slot_2, 2=promotion_slot_3, 3=promotion_slot_4, 4=promotion_slot_5, 5=promotion_slot_6, 6=promotion_slot_7, 7=promotion_slot_8, 8=promotion_slot_9, 9=promotion_slot_10. |
| `0x5f0ed` | scene_decision_seen_flags | high | investigation | Persistent seen-state for progression decisions. Numbered scene scripts set the current scjump_decision_out cell after playback and synchronize it through the persistence opcode; INFOVO uses VIINIT's one-based prerequisites to unlock glossary topics. |
| `0x62455` | adv_gfx_object_handles | high | investigation | ADV retained-object handles indexed by adv_gfx_layer_index. INIT2 seeds handles 0xcb20, 0xcb2a, and 0xcb8e..0xcbc0 for the first nine cells; scene loaders query, bind, animate, and erase those objects while the eight-row surface-slot registry governs layers zero through seven. |
| `0x624bf` | unit_voice_family_unit_ids | high | investigation | CNINIT's sparse unit-id keyed voice-family normalization map. Story, history, field, and battle voice paths map the current unit variant through this table before selecting the family-level voice-suppression state; 175 of 277 authored rows alias a variant to another representative unit id. |
| `0x628a7` | unit_voice_suppression_flag_ids | high | investigation | CVINIT's unit-to-setting inverse map for the twelve named character voice controls. Story, history, field, and battle paths normalize the current unit through unit_voice_family_unit_ids, index this table by that representative unit id, and suppress voice playback when the selected character_voice_suppressed cell is nonzero. |
| `0x62c8f` | character_voice_setting_unit_ids | high | investigation | CVINIT's CONFIG-row join. CONFIG iterates setting slots 1..12, resolves each unit id through unit_story_display_names for its label, and tests the same unit's unit_story_speaker_seen_flags cell before exposing the row. CVINIT also writes the exact inverse mapping to unit_voice_suppression_flag_ids. Columns: 0=system_or_reserved, 1=lily, 2=sylphine, 3=sasune, 4=vidal, 5=estelle, 6=nelly, 7=tiofania, 8=colette, 9=bridget, 10=octavia, 11=fam, 12=deirdre. |
| `0x62cad` | character_voice_preview_asset_ids | high | investigation | CVINIT's thirteen voice-setting preview clips. CONFIG indexes this array by setting slot and plays the selected clip; slot 0 is the non-unit system voice, while slots 1..12 join to character_voice_setting_unit_ids. Columns: 0=system_voice, 1=lily, 2=sylphine, 3=sasune, 4=vidal, 5=estelle, 6=nelly, 7=tiofania, 8=colette, 9=bridget, 10=octavia, 11=fam, 12=deirdre. |
| `0x62cd1` | gallery_image_assets | high | investigation | CGINIT's sparse gallery asset rows. CGMODE tests and displays column 0 as the full-size gallery image. SAVE and SELSTAGE scan column 0 for the current image and, when column 1 is populated, load that 112x84 preview instead of capturing the current screen. Columns: 0=gallery_image_asset_id, 1=save_stage_preview_asset_id. |
| `0x63c71` | gallery_thumbnail_sheet_ids | high | investigation | CGINIT's gallery thumbnail-atlas selector. CGMODE subtracts one, remaps the configured sheet through gallery_thumbnail_sheet_asset_ids, and groups every populated gallery record under that sheet. |
| `0x64441` | gallery_thumbnail_slot_ids | high | investigation | CGINIT's slot within the selected 6x5 thumbnail atlas. CGMODE subtracts one, draws that one of thirty 126x95 cells, and reports the unlocked/total variant counts associated with the slot. |
| `0x64c11` | gallery_variant_ordinals | high | investigation | CGINIT's ordering key for multiple full-size images behind one thumbnail slot. CGMODE subtracts one and stores the gallery record id at sheet[slot*100 + ordinal], then walks that ordered variant list in the image viewer. |
| `0x66381` | gallery_thumbnail_sheet_asset_ids | high | investigation | CGMODE's configured thumbnail-sheet resources. INIT2 populates the first four cells with SO026A.AGF through SO026D.AGF and leaves the remaining six reserved; CGMODE compacts nonzero cells into its visible sheet list before applying gallery_thumbnail_sheet_ids. |
| `0x6638b` | h_scene_gallery_script_ids | high | investigation | SPINIT's H-scene gallery registry. HMODE compacts the eight configured thumbnail pages, scans each page's fifteen script slots, filters populated resources through opcode 0x19d, and call-scripts the selected available entry. Columns: 0=slot_0, 1=slot_1, 2=slot_2, 3=slot_3, 4=slot_4, 5=slot_5, 6=slot_6, 7=slot_7, 8=slot_8, 9=slot_9, 10=slot_10, 11=slot_11, 12=slot_12, 13=slot_13, 14=slot_14. |
| `0x66421` | h_scene_gallery_thumbnail_sheet_assets | high | investigation | INIT2's eight HMODE thumbnail-page assets. HMODE compacts nonzero configured pages and pairs each page index with the corresponding row of h_scene_gallery_script_ids. |
| `0x665e3` | modal_annotation_horizontal_cells | high | investigation | Per-annotation horizontal anchor input shared by MES and SBUNKI. Each renderer multiplies the cell by 21 pixels and offsets it by half the annotation's rendered width. |
| `0x66647` | modal_annotation_row_offsets | high | investigation | Per-annotation vertical row offset shared by MES and SBUNKI. Renderers multiply it by 30 pixels; a negative value additionally shifts the annotation left by one 21-pixel cell. |
| `0x66716` | unit_voice_asset_ids | high | investigation | EBINIT per-unit voice bank for 116 voiced characters and variants. FIELD directly selects column 0 before WARPD, column 1 when an acting unit takes chest/treasure contents, and column 3 after occupying, losing, or sealing a stage objective. BTL selects the acting unit's columns 7..9 for ordinary attacks, 10 for an ordinary critical, odd columns 11/13/15 for skill uses, and even columns 12/14/16 for critical skills; the three variants follow its six-step exchange selector. If the target survives positive damage, BTL selects target columns 19/20/21 with 60/30/10 percent weights; column 22 belongs to a target reduced to zero HP, while actor column 23 is selected for a finishing blow. SHOWGROW selects column 24 after level gain. Populated columns 4..6 and 17..18 have no reachable selector in the shipped script corpus and are retained as explicit unused authoring slots; columns 17 and 18 duplicate columns 15 and 16 in all 116 populated rows. Columns: 0=warp, 1=treasure_capture, 3=objective_interaction, 4=unused_slot_4, 5=unused_slot_5, 6=unused_slot_6, 7=normal_attack_1, 8=normal_attack_2, 9=normal_attack_3, 10=critical_normal_attack, 11=skill_use_1, 12=critical_skill_1, 13=skill_use_2, 14=critical_skill_2, 15=skill_use_3, 16=critical_skill_3, 17=unused_slot_17, 18=unused_slot_18, 19=damage_reaction_1, 20=damage_reaction_2, 21=damage_reaction_3, 22=defeated, 23=finishing_blow, 24=level_up. |
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
| `0x72296` | unit_boss_class | med | investigation | Nonzero for 112 bosses, special encounters, summoned adds, and map hazards. CALCDMG grants every nonzero class the boss adjustment, CALCILL grants condition immunity, and FIELD/MAGIC/USEMAGIC exclude nonzero classes from ordinary-unit rules. FIELD draws the normal faction-colored unit banner for classes 0 and ±1..±3, then adds the separate yellow boss marker below it when this value is nonzero; class ±4 suppresses both standard markers for its special presentation. On stages whose clear rule is defeat-boss targets, FIELD blocks victory only while a positive-class enemy remains; negative peers keep boss treatment but are ignored, as with Octavia beside Bridget, Deirdre's shadows, EX-8 adds beside Tiamat, and the final boss's three organs. Classes 1..3 are otherwise authoring categories with no distinct shipped-script branch. Either sign of class 4 selects the final-boss battle BGM and special tactical-map presentation. |
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
| `0x7843e` | unit_power_tier | med | inference | Authoring-only EBINIT power/progression tier populated for 243 units. It is independent of species, sex, defense element, and boss class, but rises strongly with deployment cost, essence yield, level cap, and base stats. Lily's child/girl/adult forms are tiers 2/4/6, and recurring heroine boss definitions generally rise as their story appearances become stronger. No shipped script reads the array, and the /v2 native image contains no literal reference to its global index, so the descriptive name is correlation-based rather than a runtime behavior claim. |
| `0x78826` | unit_weapon_item_category | high | investigation | EBINIT weapon/equipment-family restriction for 179 units. CHMENU compares an item's item_category directly with this value when deciding whether the selected unit can equip it; Lily's forms store -1 for unrestricted handling. |
| `0x78c0e` | unit_starting_equipment_item_id | high | investigation | Fixed equipment for 22 named/boss unit records. Values cross-resolve to weapons, shields, armor, and accessories in ITINIT. SETEN equips the item instead of unit_default_attack_item_id, UNITECH grants it on first recruitment, and INFOEN derives the displayed defense element from it when applicable. |
| `0x78ff6` | unit_starting_skill_ids | high | investigation | EBINIT four-slot starting skill record. The shipped table populates the first three columns. SETEN, UNITECH, and SALLY copy the row into each runtime unit's skill list; GAMESTART, FORT, FIELD, and DRAWENP inspect the same ids, all of which cross-resolve to SKINIT. Columns: 0=skill_slot_1, 1=skill_slot_2, 2=skill_slot_3, 3=skill_slot_4. |
| `0x79f96` | unit_deployment_cost_base | high | investigation | Base deployment/command cost for 204 units. SETCH, SETEN, and ADDEXP copy it plus the per-unit adjustment at 0x57357 into runtime cost 0x5f0bb; SALLY checks the prospective total against party capacity, while FIELD/READY add it to and REMOVECH subtracts it from the deployed-cost aggregate. |
| `0x7a37e` | unit_starting_level | high | investigation | EBINIT initial level for 242 units. Lily's three forms store levels 1, 20, and 40. SETEN and SALLY copy or compare it while constructing a runtime unit, and UNITECH uses it when synchronizing form and character state. |
| `0x7a766` | unit_level_cap | high | investigation | EBINIT maximum level for 243 units. ADDEXP permits level growth only while the runtime level is below this field, and SETEN uses it as the default upper bound when no scenario-specific enemy cap is supplied. |
| `0x7ab4e` | unit_auto_level_scale_divisor | high | investigation | Default level-scaling divisor for 192 enemy definitions. SETEN uses a scenario override when supplied, otherwise divides the difference between scenario level and unit_starting_level by this value, adds the result to the runtime level, and clamps it to unit_level_cap. |
| `0x7af36` | unit_base_stats | high | investigation | EBINIT fourteen-column base-stat record copied wholesale into each runtime unit by SETEN, UNITECH, and SALLY. Its layout matches item_stat_modifiers: columns 2 physical attack, 3 physical defense, 4 magic attack, 5 magic defense, 6 speed, 7 luck, 10 movement, 11 max HP, 12 max SP, and 13 max FS; accuracy/evasion and other unpopulated columns begin at zero. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power, 10=movement, 11=max_hp, 12=max_sp, 13=max_fs. |
| `0x7e5e6` | unit_stat_growth_rates | high | investigation | EBINIT per-level growth record with the same fourteen-column layout as unit_base_stats. ADDEXP adds each rate to a fractional accumulator, divides by 100 to award whole stat points, and retains the remainder; SETEN and UNITECH apply the same rates when materializing units above their starting level. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power, 10=movement, 11=max_hp, 12=max_sp, 13=max_fs. |
| `0x83406` | unit_experience_reward | high | investigation | Per-opponent base experience progress. After each battle exchange, BTL awards a surviving faction-1 participant against the opposing unit definition. If the opponent survives, enemy-minus-player level differences >=3/2/1/<=0 select 40/30/20/10 percent. If the opponent is defeated, differences >=3/2/1/0/-1/-2/<=-3 select 250/200/150/100/75/40/20 percent. Integer division truncates. The result enters ADDEXP, whose normally cleared modifier bits can suppress, double, or multiply it by ten. A cell with no writer in the ordered INIT sequence remains zero; append unit 900 is such a cell and therefore awards zero at every bracket. |
| `0x837ee` | unit_drop_item_ids | high | investigation | Enemy drop table. Values cross-resolve to ITINIT items (for example treasure puttetto drops bronze/silver/gold coins and planet stones); BTL rolls and awards the rows, while INFOEN displays the possible drops. Columns: 0=drop_1_item_id, 1=drop_2_item_id, 2=drop_3_item_id, 3=drop_4_item_id, 4=drop_5_item_id, 5=drop_6_item_id, 6=drop_7_item_id, 7=drop_8_item_id. |
| `0x8572e` | unit_drop_chance_percent | high | investigation | Per-slot drop chance paired with unit_drop_item_ids. BTL draws random-modulo 100 for each populated slot and awards the item when the result is below this value; only columns 0..4 are populated in shipped EBINIT. Columns: 0=drop_1_percent, 1=drop_2_percent, 2=drop_3_percent, 3=drop_4_percent, 4=drop_5_percent, 5=drop_6_percent, 6=drop_7_percent, 7=drop_8_percent. |
| `0x8766e` | unit_large_battle_sprite | high | investigation | Presentation flag for 33 large demons, dragons, gods, and their variants. BTL and INFOEN use it to anchor the CB battle figure at the lower screen edge and omit the ordinary-unit framing treatment required by smaller sprites. |
| `0x87a57` | scjump_scene_script_resource_ids | high | investigation | SCINIT's sparse decision-to-script registry. SYSTEM4, FIELD, SALLY, and TRAIN index it by scjump_decision_out, then either call the returned packed resource id or copy it to next_script_resource_id. SCINIT contains 1,209 final decision rows backed by 2,179 source assignments; all 135 distinct packed ids resolve to numbered SC scene scripts. |
| `0x8a167` | scjump_authored_chapters | high | investigation | SCINIT's parallel authored chapter metadata for each scjump_scene_script_resource_ids entry. Source assignments form contiguous chapter 1..9 runs followed by an unassigned -1 run. Of the 847 decision ids currently emitted by SCJUMP, 844 final tags agree with SCJUMP's independently decoded chapter paths; three retained mismatches are legacy/stale metadata. No shipped script reads this array directly. |
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
| `0xaac78` | condition_effectiveness_element_ids | high | investigation | ILINIT condition metadata. CALCILL uses the selected condition's value as the element-table column for its application-chance effectiveness calculation. |
| `0xaac96` | condition_can_affect_bosses | high | investigation | ILINIT policy vector. CALCILL rejects a condition against a boss-class target when this cell is zero; nonzero cells bypass that boss immunity gate. |
| `0xaacb4` | condition_cleared_by_recover | high | investigation | ILINIT recovery-policy vector. RECOVER and DISARM use it to identify charm, confusion, paralysis, poison, water-flow, and fear as removable conditions; RECOVER resets each eligible current level to its equipment/passive baseline. |
| `0xaacd2` | condition_icon_ids | high | investigation | ILINIT display metadata. DRAWENP and FIELD use these ids to deduplicate active-condition indicators and select the corresponding status-icon atlas entry. |
| `0xaacf0` | condition_duration_turns_by_level | high | investigation | ILINIT's five-column duration row for each condition. ADDILLSUB indexes it by the resulting condition level minus one and stores the value in entity_condition_remaining_turns. Columns: 0=level_1_turns, 1=level_2_turns, 2=level_3_turns, 3=level_4_turns, 4=level_5_turns. |
| `0xaad86` | condition_stat_deltas | high | investigation | ILINIT's five-level by eleven-stat matrix for each condition. CALCREVISE indexes (condition level - 1) * 11 + stat column and adds the result to entity_effective_stats columns 0..10. Shipped rows populate curse, confusion, paralysis, and exaltation. Columns: 0=level_1_accuracy, 1=level_1_evasion, 2=level_1_physical_attack, 3=level_1_physical_defense, 4=level_1_magic_attack, 5=level_1_magic_defense, 6=level_1_speed, 7=level_1_luck, 8=level_1_critical_chance, 9=level_1_capture_power, 10=level_1_movement, 11=level_2_accuracy, 12=level_2_evasion, 13=level_2_physical_attack, 14=level_2_physical_defense, 15=level_2_magic_attack, 16=level_2_magic_defense, 17=level_2_speed, 18=level_2_luck, 19=level_2_critical_chance, 20=level_2_capture_power, 21=level_2_movement, 22=level_3_accuracy, 23=level_3_evasion, 24=level_3_physical_attack, 25=level_3_physical_defense, 26=level_3_magic_attack, 27=level_3_magic_defense, 28=level_3_speed, 29=level_3_luck, 30=level_3_critical_chance, 31=level_3_capture_power, 32=level_3_movement, 33=level_4_accuracy, 34=level_4_evasion, 35=level_4_physical_attack, 36=level_4_physical_defense, 37=level_4_magic_attack, 38=level_4_magic_defense, 39=level_4_speed, 40=level_4_luck, 41=level_4_critical_chance, 42=level_4_capture_power, 43=level_4_movement, 44=level_5_accuracy, 45=level_5_evasion, 46=level_5_physical_attack, 47=level_5_physical_defense, 48=level_5_magic_attack, 49=level_5_magic_defense, 50=level_5_speed, 51=level_5_luck, 52=level_5_critical_chance, 53=level_5_capture_power, 54=level_5_movement. |
| `0xab3f8` | condition_resource_deltas | high | investigation | ILINIT's five-level by three-resource matrix for each condition. FIELD applies the active row each turn; CALCDMG also consumes the HP/SP/FS drain condition rows during battle. Positive values restore/drain to the acting side as defined by the caller, while negative values are periodic damage or loss. Columns: 0=level_1_hp, 1=level_1_sp, 2=level_1_fs, 3=level_2_hp, 4=level_2_sp, 5=level_2_fs, 6=level_3_hp, 7=level_3_sp, 8=level_3_fs, 9=level_4_hp, 10=level_4_sp, 11=level_4_fs, 12=level_5_hp, 13=level_5_sp, 14=level_5_fs. |
| `0xab5ba` | attack_element_effectiveness_percent | high | investigation | AFINIT-authored element matchup table with thirteen authored defense rows and eighteen authored cells per row inside the reserved 20-by-20 layout. Positive values make an action eligible in SETMVWORK and RTN_M051/052; CALCBTPARAM multiplies battle parameters by the selected percentage and handles negative values as special/immunity cases. INFOAF displays eight attack columns for its selected defense rows. |
| `0xab6fa` | item_tuning_stat_bonus_curves | high | investigation | Eighteen usable equipment-growth curves selected by item_tuning_curve_ids, plus an explicitly zeroed reserved row 19. TUNE and IMPROVE use the nonzero prefix as the available tuning-level range; CALCREVISE and DRAWTIP add the selected zero-based tuning level's bonus to the corresponding stat. Columns: 0=tuning_level_1, 1=tuning_level_2, 2=tuning_level_3, 3=tuning_level_4, 4=tuning_level_5, 5=tuning_level_6, 6=tuning_level_7, 7=tuning_level_8, 8=tuning_level_9, 9=tuning_level_10, 10=reserved. |
| `0xab7d6` | item_tuning_point_cost_curves | high | investigation | Point-cost curves paired by curve id and tuning level with item_tuning_stat_bonus_curves. IMPROVE sums costs for newly selected levels, subtracts refunds for removed levels, checks the resulting total against the tuning-point balance, and deducts it on confirmation. Columns: 0=tuning_level_1, 1=tuning_level_2, 2=tuning_level_3, 3=tuning_level_4, 4=tuning_level_5, 5=tuning_level_6, 6=tuning_level_7, 7=tuning_level_8, 8=tuning_level_9, 9=tuning_level_10, 10=reserved. |
| `0xab8b2` | facility_level_progress_thresholds | high | investigation | AFINIT's three facility-progression rows. IMPROVE uses row 0 for equipment tuning, ALCHEMY uses row 1, and MAGIC/USEMAGIC use row 2; each indexes the row by the current level and caps advancement at level 6. Columns: 0=level_0_to_1, 1=level_1_to_2, 2=level_2_to_3, 3=level_3_to_4, 4=level_4_to_5, 5=level_5_to_6, 6=level_6_cap. |
| `0xab8c7` | class_change_rule_script_ids | high | investigation | CALCCC iterates these 32 cells and call-scripts every positive entry to evaluate class-change providers. CCINIT is the shipped rule program decoded into build/data/CCINIT.json. |
| `0xaba64` | stage_object_runtime_flags | high | investigation | Per-current-stage object flags. RTN_M010 and RTN_M015 require bit 1 before considering Healing Feathers or Magic Pillars as movement targets; FIELD and object rendering maintain the broader type-dependent bitfield. |
| `0xaba96` | pathfinding_remaining_route_steps | high | investigation | MVSEEK's row-major reachability grid. Mode 0 searches from the current entity with movement+1 at the origin; modes 1/2 search from the caller-supplied coordinate with 9999 at the origin. Each traversed edge decrements the value, so a larger positive value is nearer to the origin. RTN_M006/007/015 use origin-minus-target values as route-step radii; RTN_M010 ranks Healing Feathers by the negated value. |
| `0xb240e` | pathfinding_movement_costs | high | investigation | MVSEEK's row-major movement-cost work grid, indexed as [tile_y][tile_x] with stride 27. FIELD and RTN_M providers accept candidate destinations only when this cost is within the acting entity's current FS or a provider-specific override. |
| `0xb8d86` | action_range_distance_grid | high | investigation | ATSEEK's row-major action-range flood-fill grid. It starts from the acting entity, expands through map-valid doubled-coordinate neighbors up to usable_action_max_range, and stores 1 for the first range band, 2 for the next, and so on. RTN_M051/052 select offensive targets from it; RTN_M061 selects in-range healing targets. |
| `0xbf6fe` | pathfinding_filtered_route_scores | high | investigation | SETMVWORK begins by copying pathfinding_remaining_route_steps, then filters occupied entity cells according to faction and the acting entity's usable offensive-action mask/effectiveness. RTN_M providers combine this target-proximity score with a movement-limited MVSEEK grid to select a reachable tile nearest an entity or stage object. |
| `0xc6077` | selected_movement_route_steps | high | investigation | Selected movement-route work grid indexed as [tile_y][tile_x]. MVRTN, FIELD, and SETROUTE clear all 27,000 cells. SETROUTE starts at map_target_tile_x/y, copies pathfinding_remaining_route_steps into the route, and follows the cardinal neighbor whose score is one greater until it reaches the acting entity. FIELD uses the monotone values to draw directional route markers and drive movement; SELACT tests whether an action target follows movement; seventeen RTN_M providers reject candidate endpoints whose route cell is zero. |
| `0xcc9f4` | offensive_action_scope_masks | high | investigation | CALCSCOPE clears ten encoded-range cells, sets bit 0 over the equipped/default attack's range, and sets bits 1..4 over each usable offensive skill's range. SETMVWORK uses the range-0 mask and paired attack elements when filtering target cells; RTN_M003/006/016/017/018 require its normal-attack bit. Columns: 0=range_0, 1=range_1, 2=range_2, 3=range_3, 4=range_4, 5=range_5, 6=range_6, 7=range_7, 8=range_8, 9=range_9. |
| `0xcc9fe` | healing_action_scope_masks | high | investigation | CALCSCOPE clears ten encoded-range cells and sets the equipped-slot bit for each usable category-7 healing skill over its applicable range. RTN_M061 uses the masks to restrict ally targets and to select the range-enabled healing skill. Columns: 0=range_0, 1=range_1, 2=range_2, 3=range_3, 4=range_4, 5=range_5, 6=range_6, 7=range_7, 8=range_8, 9=range_9. |
| `0xcca08` | offensive_action_attack_elements | high | investigation | CALCSCOPE stores the normal attack element in action column 0 and each usable equipped skill's attack element in columns 1..4 for every applicable range band. SETMVWORK and RTN_M051/052 test these elements against attack_element_effectiveness_percent. |
| `0xccbcf` | cardinal_tile_delta_x | high | investigation | INIT2's shared no-move-plus-four-cardinal-neighbor X offsets. SETROUTE uses indices 1..4 while tracing a selected route toward the acting entity; MVSEEK, ATSEEK, FIELD, SALLY, and DRAWMAP use the same neighbor order. |
| `0xccbd4` | cardinal_tile_delta_y | high | investigation | INIT2's Y offsets paired with cardinal_tile_delta_x. Indices 1..4 enumerate positive Y, negative X, negative Y, and positive X neighbors; index zero leaves the coordinate unchanged. |
| `0xccc93` | stage_terrain_atlas | high | investigation | Immutable sparse half-tile terrain atlas loaded by MPINIT. Each footer copy writes fifty cells at row stride 53. FIELD doubles STINIT2's tile bounds and copies the selected rectangle into current_stage_terrain_grid; DRAWMINIMAP reads the atlas outside the active rectangle for border context, and RESETLAND restores changed cells from it. |
| `0xe6aa4` | terrain_texture_slot_indices | high | investigation | LAINIT mapping from terrain id to the index in stage_map_texture_asset_overrides. DRAWMAP uses the selected slot to choose the current stage's tiled terrain surface. |
| `0xe6ac2` | terrain_area_fill_flags | high | investigation | LAINIT terrain topology flag. DRAWMAP and CALCOCC use neighboring values to expand room-like regions across the alternating half-tile grid; DRAWMINIMAP uses the same distinction when joining adjacent revealed cells. |
| `0xe6ae0` | terrain_layout_classes | high | investigation | LAINIT terrain class. Zero cells are rejected by map rendering, occupancy, and pathfinding; ordinary areas, passages, and hidden spaces take distinct DRAWMAP/CALCOCC/MVSEEK paths. Combined with terrain_area_fill_flags, it distinguishes hidden rooms from hidden passages. |
| `0xe6afe` | terrain_combat_stat_deltas | high | investigation | LAINIT terrain-effect matrix. CALCBTPARAM reads the acting unit's doubled-coordinate battle tile and adds columns 0..9 through the same combat-parameter channels used by SKINIT skill deltas. INFOAF displays the first eight columns for its terrain rows, and the five populated terrain_effect_descriptions exactly summarize the shipped nonzero cells. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power. |
| `0xe6c2a` | terrain_required_skill_ids | high | investigation | LAINIT traversal/reveal requirement indexed by terrain id. MVSEEK and FIELD reject ordinary terrain unless the moving unit owns the referenced SKINIT skill; hidden passages/rooms use the exploration skill through the dedicated hidden-terrain path. FIELD and INFOAF resolve populated ids to skill names for display. |
| `0xe6c48` | stage_map_texture_default_asset_ids | high | investigation | Shared fallback map texture for each of twenty stage texture slots. FIELD loads a positive stage_map_texture_asset_overrides value directly, skips zero, and when the stage value is -1 loads this slot's MP000A/B/C/E/H/I/J/K/L/N.AGF default into surface 0x52+slot. |
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
| `0xe73bb` | stage_object_primary_payload | high | investigation | STINIT's primary per-object payload array. FIELD interprets it by the parallel object type: initial faction for Magic Pillars, teleport destination X, item id, card-generation list id for type 28, non-triggering faction for hazards/barriers, or initial object state for the consumer-proven stateful types. |
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
| `0xe7e8d` | stage_unlock_group_ids | high | investigation | FIELD propagates an unlocked current stage to every other stage whose nonzero group id matches. The shipped groups bind route variants 32..34, 35..36, 71..72, and 82..83. |
| `0xe8275` | stage_main_progression_flags | high | investigation | When returning after a clear, FORT scans uncleared stage ids in descending order and auto-selects the first available row marked one. Side missions and all eight EX dungeons leave the flag zero. |
| `0xe865d` | stage_forbidden_story_flag_ids | high | investigation | FORT and SELSTAGE reject a stage when any populated cell resolves to story flag value one. The scripts subtract one before indexing story_event_flags, establishing the stored one-based id convention. Columns: 0=forbidden_flag_1, 1=forbidden_flag_2, 2=forbidden_flag_3, 3=forbidden_flag_4, 4=forbidden_flag_5, 5=forbidden_flag_6, 6=forbidden_flag_7. |
| `0xea1b5` | stage_required_story_flag_ids | high | investigation | FORT and SELSTAGE require every populated cell to resolve to story flag value one. Every shipped stage carries at least one gate, including common progression flags in columns two and three. Columns: 0=required_flag_1, 1=required_flag_2, 2=required_flag_3, 3=required_flag_4, 4=required_flag_5, 5=required_flag_6, 6=required_flag_7. |
| `0xebd0d` | stage_display_number_major | high | investigation | FORT, SELSTAGE, AIM, STAGECLEAR, and management screens render this as the major half of the stage number. A negative value renders EX, while both number components zero identify an event-only row. |
| `0xec0f5` | stage_display_number_minor | high | investigation | Rendered after stage_display_number_major as the minor stage number. FORT treats a row with both components zero as a direct event and dispatches its entry SCJUMP decision without entering FIELD. |
| `0xec4dd` | stage_map_min_tile_x | high | investigation | STINIT2's inclusive left map bound indexed by stage id. FIELD, DRAWMAP, DRAWMINIMAP, CALCOCC, and movement providers use it with stage_map_max_tile_x; terrain-grid accesses multiply the coordinate by two. |
| `0xec8c5` | stage_map_max_tile_x | high | investigation | STINIT2's inclusive right map bound indexed by stage id. Consumers pair it with stage_map_min_tile_x for iteration, camera/minimap limits, random placement, and the doubled-coordinate terrain-atlas copy. |
| `0xeccad` | stage_map_min_tile_y | high | investigation | STINIT2's inclusive top map bound indexed by stage id. FIELD and all map readers pair it with stage_map_max_tile_y and multiply it by two when addressing the half-tile terrain grid. |
| `0xed095` | stage_map_max_tile_y | high | investigation | STINIT2's inclusive bottom map bound indexed by stage id. The maximum shipped value 800 explains MPINIT's final authored doubled grid row at Y 1600. |
| `0xed47d` | stage_minimap_atlas_origin_y | high | investigation | FIELD and SELSTAGE copy this to the minimap drawing origin. SELSTAGE subtracts it from stage_map_min_tile_y to crop the selected stage's vertically packed minimap strip and labels the following 150 atlas rows. |
| `0xed865` | stage_clear_base_spendable_point_rewards | high | investigation | STAGECLEAR multiplies this base award by the turn/party-performance percentage, divides by 100, adds any card bonus, and credits the capped shared_spendable_points balance. |
| `0xedc4d` | stage_authoring_difficulty_tiers | med | inference | Authoring-only stage challenge/progression tier. No shipped script reads the array and the native engine can only reach VM globals through script operands, so it has no runtime effect. The ordinal correlates strongly with base clear points and encounter progression: late story maps are tier 6, EX maps use tiers 5 and 7, and the final EX map alone is tier 8. The descriptive name is correlation-based. |
| `0xee035` | stage_scjump_decision_ids | high | investigation | FORT dispatches column zero before entering a stage or for an event-only row. FIELD and a few scene return paths dispatch column one after clear and column two after failure or forced-retreat outcomes; all 174 populated references resolve through SCINIT. Columns: 0=entry, 1=clear, 2=failure. |
| `0xeebed` | stage_extra_dungeon_flags | high | investigation | Marks the eight shipped EX dungeons. FORT, SELSTAGE, and management screens combine it with stage_clear_state when deciding whether to show and apply the stage's post-clear coin rewards. |
| `0xeefd5` | stage_clear_coin_quantities | high | investigation | FORT and SELSTAGE render the three clear-reward quantities beside coin icons. Management screens grant the same values as item ids 91..93 after a cleared or EX-marked stage. Columns: 0=bronze_coin_item_91, 1=silver_coin_item_92, 2=gold_coin_item_93. |
| `0xefb8d` | stage_loader_script_ids | high | investigation | FIELD indirectly calls the selected STINIT2 row before initializing the map. Every shipped stage points to the shared STINIT selector program, which then populates stage-specific objects, enemies, rules, textures, and audio. |
| `0xeff78` | movement_routine_provider_selectors | high | investigation | RTINIT movement bank 0. MVRTN indexes it by current_routine_set_id and routine_step_index, resolves the selector through its RTN_M001..018/051..053/061 provider table, and call-scripts the selected movement routine. |
| `0xf4d98` | movement_routine_activation_percents | high | investigation | RTINIT movement bank 1. After all other step gates pass, MVRTN executes the provider only when random-modulo-100 is below this value. |
| `0xf9bb8` | movement_routine_parameter_1 | high | investigation | RTINIT movement bank 2. Its meaning is tagged by movement_routine_provider_selectors: RTN_M004 uses a stage_object_slot_index; RTN_M005/011/012 use destination_tile_x; RTN_M006/007/015 use maximum_target_route_steps; RTN_M010 uses resource_index (0=HP, 1=SP, 2=FS; shipped cells are unwritten/default zero); RTN_M013 uses target_faction_filter; and RTN_M014 uses maximum_threat_route_steps. The one RTN_M001 and one RTN_M008 authored cells are never read by those providers. |
| `0xfe9d8` | movement_routine_parameter_2 | high | investigation | RTINIT movement bank 3. Its meaning is tagged by movement_routine_provider_selectors: RTN_M005/011/012 use destination_tile_y, RTN_M007 uses maximum_target_hp_percent, and RTN_M010 uses maximum_resource_percent. The lone RTN_M001-authored value is never read by that provider. |
| `0x1037f8` | movement_routine_parameter_3 | high | investigation | RTINIT movement bank 4. RTN_M011 uses it as a one-based waypoint_ordinal, executing only the step whose ordinal matches the entity's current zero-based waypoint index. |
| `0x108618` | movement_routine_parameter_4 | high | investigation | RTINIT movement bank 5. RTN_M011 uses a nonzero value as path_cost_limit_override; zero or an unwritten cell falls back to the entity's current FS. |
| `0x112258` | movement_routine_minimum_progress_counts | high | investigation | RTINIT movement bank 7. MVRTN requires the current entity's matching movement-step progress counter to reach this value; movement providers increment those counters as their steps execute. |
| `0x117078` | movement_routine_required_story_flag_ids | high | investigation | RTINIT movement bank 8. MVRTN subtracts one and rejects the step when the referenced story flag is not set. |
| `0x11be98` | movement_routine_forbidden_story_flag_ids | high | investigation | RTINIT movement bank 9. MVRTN subtracts one and rejects the step when the referenced story flag is set. |
| `0x120cb8` | battle_routine_provider_selectors | high | investigation | RTINIT battle bank 10. BTRTN indexes it by current_routine_set_id and routine_step_index, resolves the selector through RTN_B001..004, and call-scripts the selected battle routine. |
| `0x125ad8` | battle_routine_activation_percents | high | investigation | RTINIT battle bank 11. BTRTN executes a candidate step only when the current entity's matching random-modulo-100 battle-step roll is below this value. |
| `0x12a8f8` | battle_routine_parameter_1 | high | investigation | RTINIT battle bank 12. RTN_B004 uses it to choose an entry from the prepared battle-action candidate table. |
| `0x147db8` | battle_routine_required_story_flag_ids | high | investigation | RTINIT battle bank 18. BTRTN subtracts one and rejects the step when the referenced story flag is not set. |
| `0x14cbd8` | battle_routine_forbidden_story_flag_ids | high | investigation | RTINIT battle bank 19. BTRTN subtracts one and rejects the step when the referenced story flag is set. |
| `0x1519f9` | card_definition_type_ids | high | investigation | CDINIT2 behavior class by card id. FIELD applies the common populated effect arrays, uses type 6 for the random valid-tile warp path, and uses type 1 to dispatch the card's event after showing its result. |
| `0x151a5d` | card_definition_required_story_flag_ids | high | investigation | CDINIT2 card eligibility rows. FIELD checks only columns 0 and 1 before admitting a card to weighted selection. CDINIT2 nevertheless authors column 2 for eighteen cards; those third requirements are engine-dead in the shipped FIELD loop and are retained separately by the extractor. Columns: 0=required_flag_1, 1=required_flag_2, 2=engine_dead_required_flag_3. |
| `0x151b89` | card_definition_forbidden_story_flag_ids | high | investigation | CDINIT2 card exclusion rows. FIELD checks columns 0 and 1 and rejects a card when either populated flag is set; the shipped definitions populate only column 0. Columns: 0=forbidden_flag_1, 1=forbidden_flag_2, 2=reserved_forbidden_flag_3. |
| `0x151cb5` | card_definition_awarded_item_ids | high | investigation | Optional item granted by FIELD through ADDITEM after card selection. All 24 populated ids resolve through ITINIT. |
| `0x151d19` | card_definition_event_story_flag_ids | high | investigation | Event dispatched for type-1 cards after FIELD displays the card result. All 40 populated ids resolve through SCINIT. |
| `0x151d7d` | card_definition_stage_clear_point_bonuses | high | investigation | Type-3 card reward accumulated into stage_card_spendable_point_bonus. STAGECLEAR adds it to the normal performance-adjusted award before updating shared_spendable_points; shipped small/medium/large values are 5, 10, and 15. |
| `0x151de1` | card_definition_minimum_resource_recovery | high | investigation | Lower bounds for type-4 HP/SP/FS card recovery. FIELD starts each resource delta at this value and, when the paired upper bound is larger, adds a random value below their difference. Columns: 0=hp, 1=sp, 2=fs. |
| `0x151f0d` | card_definition_maximum_resource_recovery | high | investigation | Upper bounds paired with card_definition_minimum_resource_recovery. Equal bounds produce the fixed minimum; larger bounds produce a uniformly selected value from minimum through upper-bound-minus-one. Columns: 0=hp, 1=sp, 2=fs. |
| `0x152039` | card_definition_minimum_spirit_recovery | high | investigation | Lower bound for type-4 familiar-spirit recovery. FIELD selects the value with the same minimum/exclusive-upper algorithm as HP/SP/FS and clamps current_spirit to maximum_spirit. |
| `0x15209d` | card_definition_maximum_spirit_recovery | high | investigation | Upper bound paired with card_definition_minimum_spirit_recovery. Equal bounds produce the fixed minimum; larger bounds exclude this upper value. |
| `0x152101` | card_definition_minimum_resource_damage | high | investigation | Lower bounds for type-5 trap damage. FIELD subtracts these randomized HP/SP/FS amounts from the active entity through the same resource-delta path used for recovery. Columns: 0=hp, 1=sp, 2=fs. |
| `0x15222d` | card_definition_maximum_resource_damage | high | investigation | Upper bounds paired with card_definition_minimum_resource_damage. Equal bounds produce fixed damage; larger bounds exclude this upper value. Columns: 0=hp, 1=sp, 2=fs. |
| `0x152359` | card_definition_condition_ids | high | investigation | Condition applied by type-5 trap cards through ADDILL. The three populated rows resolve to paralysis, poison, and water_flow. |
| `0x1523bd` | card_definition_condition_levels | high | investigation | Condition level paired with card_definition_condition_ids and passed to ADDILL. All three shipped condition traps use level 1. |
| `0x152421` | card_definition_visual_asset_ids | high | investigation | Card-result visual selected by FIELD before applying and presenting the effect. All 81 authored rows resolve to MVS*.AGF assets, with related card families sharing artwork. |
| `0x152486` | card_generation_weight_schedules | high | investigation | CDINIT's selector-specific candidate weights, parallel to card_generation_card_ids. FIELD computes base_weight + floor(current_stage_turn / growth_interval_turns) * growth_weight, with a zero interval selecting the fixed base path. The shipped lists populate 11..75 one-based slots. Columns: 0=base_weight, 1=growth_interval_turns, 2=growth_weight. |
| `0x1525b2` | card_generation_card_ids | high | investigation | Card ids parallel to card_generation_weight_schedules. FIELD scans slots 0..99, filters each nonzero id through the CDINIT2 story-flag rows, and performs cumulative weighted random selection. CDINIT clears only the first 50 slots even though its largest authored list reaches slot 75. |
| `0x15261f` | battle_triggered_passive_skill_flags | high | investigation | Per-exchange passive-skill activation matrix. CALCDMG clears both 300-cell side rows, considers the four equipped skills on each battle entity, retains category-4 passives allowed for that actor/target role, rolls skill_proc_chance_percent, and applies species, boss, movement-skill, action-category, and mutual-exclusion filters. BTL consumes the surviving flags to draw triggered-passive icons, play each passive's battle animation, and execute Roar, Counter, Reflect, Absorb, Pierce, Shield, Parry, and Revive behavior. |
| `0x152877` | battle_entity_indices | high | investigation | The two runtime entity rows participating in BTL. battle_actor_side_index and battle_target_side_index select these cells before BTL and CALCDMG access HP, unit definitions, skills, animation state, and voice banks. Columns: 0=side_0, 1=side_1. |
| `0x152879` | battle_selected_skill_ids | high | investigation | Selected skill for each battle side. BTL displays a positive entry through the skill-name table and selects skill-use voice columns; CALCDMG uses the same id for skill parameters. A zero entry follows the equipped/default ordinary-attack path. Columns: 0=side_0, 1=side_1. |
| `0x152883` | battle_critical_chance_percent | high | investigation | Final per-side critical chance for the current exchange. CALCBTPARAM starts with the side's effective critical-chance stat plus its effective luck minus the opposing side's effective luck, applies selected-skill and applicable target-tile terrain critical/luck deltas through the same channels, and clamps the result to 0..100. Equipment has already entered through entity_effective_stats: CT affects only its owner's outgoing critical chance, whereas luck both raises outgoing chance and lowers the opponent's. After a successful hit, CALCDMG rolls random-modulo-100 against this value; success sets battle_hit_result to 2 and doubles the signed HP delta. Columns: 0=side_0, 1=side_1. |
| `0x15288e` | battle_animation_effect_ids | high | investigation | BTANINIT2's sparse battle-animation composition table. BTANINIT expands the selected row into six runtime effect work slots. BTL anchors requested effects in slots 0 and 1 to the actor and slots 2 through 5 to the target; shipped data never populates slot 4. Columns: 0=effect_slot_0, 1=effect_slot_1, 2=effect_slot_2, 3=effect_slot_3, 4=reserved_effect_slot_4, 5=effect_slot_5. |
| `0x153ffe` | battle_animation_effect_start_delays_ms | high | investigation | Per-effect start delays paired with battle_animation_effect_ids. BTL passes each populated value to movie/sprite animation setup and adds it to the effect-local sound and hit-pulse offsets. Shipped data authors delays only for slots 2, 3, and 5. Columns: 0=effect_slot_0, 1=effect_slot_1, 2=effect_slot_2, 3=effect_slot_3, 4=reserved_effect_slot_4, 5=effect_slot_5. |
| `0x15576e` | battle_animation_duration_ms | high | investigation | Overall timing boundary for one BTANINIT2 animation row. BTL uses it to schedule delayed battle voice and to begin the 500 ms HP interpolation after the main animation. The passive/defeat auxiliary rows 801..809 intentionally omit it. |
| `0x155b56` | battle_effect_visual_asset_ids | high | investigation | BTANINIT clears and materializes the selected animation's six visual resources. BTL opens mode-0 values as nonblocking MPEG-in-AGF movies and mode-1/2 values as sprite sheets. All 202 effect definitions resolve through SYS4INI. |
| `0x155b5c` | battle_effect_visual_mode_ids | high | investigation | BTANINIT visual playback mode per materialized effect slot. BTL routes zero through play-movie-to-surface, mode 1 through set-texture with green color key 0x00ff00, and mode 2 through set-texture with no color key. |
| `0x155b62` | battle_effect_additive_blend_flags | high | investigation | When nonzero, BTL applies gfx-draw-color mode 1 (SRCALPHA/ONE additive glow) to the materialized battle-effect surface. The flag is populated on all 186 movie effects and the sole green-colorkey sprite-sheet effect. |
| `0x155b68` | battle_effect_widths | high | investigation | Width of the destination surface BTL creates for each materialized battle effect. It also participates in centering the surface before applying battle_effect_offset_x_pixels. |
| `0x155b6e` | battle_effect_heights | high | investigation | Height of the destination surface BTL creates for each materialized battle effect. It also participates in centering the surface before applying battle_effect_offset_y_pixels. |
| `0x155b74` | battle_effect_atlas_column_counts | high | investigation | Sprite-sheet column count passed by BTL to animate-gfx-srcrect-target. The sixteen sprite-sheet definitions pair it with battle_effect_atlas_frame_counts and a finite duration. |
| `0x155b7a` | battle_effect_atlas_row_counts | high | investigation | Authored sprite-sheet row count. It agrees with ceil(frame_count / column_count) for all sixteen sprite effects, but BTL never reads this array and instead passes the already-authored frame count and column count to animate-gfx-srcrect-target. |
| `0x155b80` | battle_effect_atlas_frame_counts | high | investigation | Total sprite-sheet frame count passed by BTL to animate-gfx-srcrect-target. Shipped values equal atlas columns times authored rows. |
| `0x155b86` | battle_effect_durations_ms | high | investigation | Per-effect playback duration. BTANINIT authors it for sixteen sprite-sheet effects; for movie effects BTL overwrites the slot with query-surface-stop-time-ms. BTL uses start delay plus this duration when extending the animation timeline. |
| `0x155b8c` | battle_effect_combatant_anchor_flags | high | investigation | When nonzero, BTL anchors effect slots 0 and 1 to the actor-side combatant and slots 2 through 5 to the target-side combatant. Zero uses the battlefield center. |
| `0x155b92` | battle_effect_offset_x_pixels | high | investigation | Horizontal offset added after BTL centers the effect surface on its selected combatant or the battlefield. |
| `0x155b98` | battle_effect_offset_y_pixels | high | investigation | Vertical offset added after BTL centers the effect surface on its selected combatant or the battlefield. |
| `0x155b9e` | battle_effect_sound_asset_ids | high | investigation | Optional WAV loaded by BTL for a materialized effect slot. BTL schedules playback at the animation row's effect start delay plus battle_effect_sound_delays_ms. All 199 populated ids resolve through SYS4INI. |
| `0x155ba4` | battle_effect_sound_delays_ms | high | investigation | Effect-local sound delay added to battle_animation_effect_start_delays_ms before BTL schedules the loaded WAV channel. |
| `0x155baa` | battle_effect_hit_pulse_offsets_ms | high | investigation | Up to three BTL hit-feedback pulse offsets per materialized effect slot. BTL adds the animation row's effect start delay, fires each pulse within a 100 ms window, and tracks it once per slot/column. Shipped BTANINIT populates only pulse_1, at 100 ms, on 26 effects. Columns: 0=pulse_1, 1=pulse_2, 2=pulse_3. |
| `0x155bbc` | training_action_required_story_flag_ids | high | investigation | Up to three prerequisite story flags per TRINIT action. TRAIN subtracts one before indexing story_event_flags and rejects the action unless every populated flag equals one. Columns: 0=required_story_flag_id_1, 1=required_story_flag_id_2, 2=required_story_flag_id_3. |
| `0x155bfb` | training_action_forbidden_story_flag_ids | high | investigation | Reserved three-slot exclusion table paired with training_action_required_story_flag_ids. TRAIN rejects an action when any populated flag equals one; shipped TRINIT leaves all 63 cells zero. Columns: 0=forbidden_story_flag_id_1, 1=forbidden_story_flag_id_2, 2=forbidden_story_flag_id_3. |
| `0x155c3a` | training_action_minimum_unit_levels | high | investigation | Minimum familiar level for each TRINIT action. TRAIN compares the selected unit's level and rejects values below the populated threshold. |
| `0x155c4f` | training_action_maximum_unit_levels | high | investigation | Maximum familiar level for each TRINIT action. TRAIN rejects levels above a populated threshold; shipped TRINIT leaves this reserved family empty. |
| `0x155c64` | training_action_minimum_alignment_thresholds_encoded | high | investigation | Encoded lower alignment/personality gates. TRAIN subtracts 100 from a populated value and requires familiar_alignment to be at least that signed threshold; the five shipped gates correspond to the locked hints' kind-personality requirements. |
| `0x155c79` | training_action_maximum_alignment_thresholds_encoded | high | investigation | Encoded upper alignment/personality gates. TRAIN subtracts 100 and requires familiar_alignment not to exceed the signed threshold; the five shipped gates correspond to evil-personality requirements. |
| `0x155c8e` | training_action_minimum_progress | high | investigation | Minimum familiar_training_progress required by each TRINIT action. TRAIN rejects the action when the current value is below a populated threshold. |
| `0x155ca3` | training_action_maximum_progress | high | investigation | Upper training-progress gate paired with training_action_minimum_progress. TRAIN enforces populated values; shipped TRINIT leaves all 21 cells zero. |
| `0x155cb8` | training_action_minimum_unit_stats | high | investigation | Ten-column minimum-stat gate using the first ten unit-stat ABI columns. TRAIN compares each populated cell with the selected familiar's current stat; shipped TRINIT leaves the table empty. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power. |
| `0x155d8a` | training_action_maximum_unit_stats | high | investigation | Ten-column maximum-stat gate paired with training_action_minimum_unit_stats. TRAIN enforces populated cells; shipped TRINIT leaves the table empty. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power. |
| `0x155e5c` | training_action_required_item_ids | high | investigation | Optional item prerequisite for each TRINIT action. TRAIN requires a nonzero inventory count and displays the joined item name; the item is a gate, not consumed by this path. |
| `0x155e71` | training_action_required_skill_ids | high | investigation | Optional acquired-skill prerequisite. TRAIN requires the corresponding skill_acquired_flags cell; shipped TRINIT leaves all 21 cells zero. |
| `0x155e86` | training_action_spirit_deltas | high | investigation | Signed current_spirit delta for each TRINIT action. TRAIN rejects an action whose post-delta spirit would be negative, displays the negated value as its cost, and applies the delta before scene dispatch. |
| `0x155e9b` | training_action_unit_stat_deltas | high | investigation | Fourteen-column familiar stat effects for each TRINIT action. TRAIN adds populated values through the unit stat-growth ABI, carries fractional growth, applies caps, and invokes SHOWGROW when any stat changes. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power, 10=movement, 11=max_hp, 12=max_sp, 13=max_fs. |
| `0x155fc1` | training_action_alignment_deltas_hundredths | high | investigation | Alignment/personality change for each TRINIT action in hundredths. TRAIN combines it with familiar_alignment_fraction, applies the integral and stochastic fractional change to familiar_alignment, and clamps the result to -99..99. |
| `0x155fd6` | training_action_progress_deltas_hundredths | high | investigation | Training-progress gain for each TRINIT action in hundredths. TRAIN combines it with familiar_training_progress_fraction, advances familiar_training_progress with fractional probability, and clamps the result to 99. |
| `0x155feb` | training_action_awarded_skill_ids | high | investigation | Optional skill granted by a TRINIT action. TRAIN checks whether it is new, marks the reward path, and passes the id to ADDSKILL; the extractor joins all eight populated ids to SKINIT names. |
| `0x156000` | training_action_awarded_item_ids | high | investigation | Optional item granted by a TRINIT action. TRAIN suppresses already-owned key/story rewards, displays the joined item name, and calls ADDITEM for the three populated shipped actions. |
| `0x156015` | training_action_event_story_flag_ids | high | investigation | Per-action event sequence selected by the prior execution count. TRAIN copies the chosen id to scjump_decision_out and dispatches it through SCINIT; GAMESTART marks every slot below a restored count in story_event_flags. Repeated ids intentionally reuse a scene, and the first zero defines the execution cap. Columns: 0=execution_1, 1=execution_2, 2=execution_3, 3=execution_4, 4=execution_5, 5=execution_6, 6=execution_7, 7=execution_8, 8=execution_9, 9=execution_10. |
| `0x1561f6` | magic_action_information_handler_script_ids | high | investigation | MAINIT handler column indexed by current_magic_action_id. MAGIC and STUDY call the selected packed script id; every shipped action routes to MAMES, whose text dispatcher has authored descriptions for ids 1..9 only. |
| `0x156214` | alchemy_recipe_output_item_ids | high | investigation | Output item id indexed by sparse alchemy recipe id. ALCHEMY scans recipe ids 0..999, treats a nonzero cell as a populated recipe, and adds one copy of this item after a successful synthesis. |
| `0x1565fc` | alchemy_recipe_minimum_levels | high | investigation | Minimum alchemy level indexed by recipe id. ALCHEMY exposes a populated recipe only when this value is less than or equal to alchemy_level. |
| `0x1569e4` | alchemy_recipe_required_story_flags | high | investigation | Two positive story prerequisites per recipe. ALCHEMY subtracts one from every populated id and hides the recipe unless the corresponding story_event_flags cell equals 1; shipped ALINIT data populates only the first column. Columns: 0=required_flag_1, 1=required_flag_2. |
| `0x1571b4` | alchemy_recipe_forbidden_story_flags | high | investigation | Two negative story prerequisites per recipe. ALCHEMY subtracts one from every populated id and hides the recipe when the corresponding story_event_flags cell equals 1; shipped ALINIT data populates only the first column. Columns: 0=forbidden_flag_1, 1=forbidden_flag_2. |
| `0x157d6c` | alchemy_recipe_point_costs | high | investigation | Spendable point cost indexed by recipe id. ALCHEMY checks recipe availability against its point-capacity condition, then requires and deducts this value from shared_spendable_points on synthesis. |
| `0x158154` | alchemy_recipe_ingredient_item_ids | high | investigation | Up to four ingredient item ids per recipe. ALCHEMY checks total inventory for each populated slot and removes the paired alchemy_recipe_ingredient_quantities amount during synthesis. Columns: 0=ingredient_1, 1=ingredient_2, 2=ingredient_3, 3=ingredient_4. |
| `0x1590f4` | alchemy_recipe_ingredient_quantities | high | investigation | Required ingredient quantities paired cell-for-cell with alchemy_recipe_ingredient_item_ids. ALCHEMY requires and consumes this many copies of each populated ingredient. Columns: 0=ingredient_1, 1=ingredient_2, 2=ingredient_3, 3=ingredient_4. |
| `0x15a097` | information_message_handler_script_ids | high | investigation | INFOMES's 32-row, four-column handler registry. INIT2 populates row zero with CIMES (0x334a), EIMES (0x334b), and VIMES (0x334c); INFOMES scans rows in order for the current tab, calls nonzero handlers, and stops after information_message_handled is set. Columns: 0=character_handler, 1=enemy_handler, 2=glossary_handler, 3=reserved_handler. |
| `0x15a118` | character_profile_unit_ids | high | investigation | CIINIT's profile-to-unit join. INFOCH uses the selected unit id for reveal state, map sprite, short unit descriptions, species, and sex while CIMES remains keyed by the enclosing profile id. |
| `0x15a17c` | character_profile_portrait_asset_ids | high | investigation | Optional CIINIT portrait resource indexed by character profile id. INFOCH draws it when nonzero and otherwise falls back to the backing unit's map-sprite asset. |
| `0x15a1e0` | character_profile_portrait_x_offsets | high | investigation | INFOCH adds the selected cell to the centered portrait x coordinate. The 100-cell spacing to the adjacent CIINIT arrays and the direct indexed reader establish the reserved per-profile placement column. |
| `0x15a244` | character_profile_portrait_y_offsets | high | investigation | INFOCH adds the selected cell to the bottom-aligned portrait y coordinate. The 100-cell spacing to the adjacent CIINIT arrays and the direct indexed reader establish the reserved per-profile placement column. |
| `0x15a2a9` | glossary_topic_unlock_seen_decision_ids | high | investigation | VIINIT's three unlock prerequisites per glossary topic. INFOVO subtracts one from each positive value and exposes the topic when any referenced scene_decision_seen_flags cell is set; debug modes expose the same populated topics unconditionally. Columns: 0=unlock_seen_decision_1, 1=unlock_seen_decision_2, 2=unlock_seen_decision_3. |
| `0x2e2` | — | low | auto-shape | array |
| `0x69e` | — | low | auto-shape | array |
| `0x6fe` | — | low | auto-shape | array |
| `0x712` | — | low | auto-shape | array |
| `0x3276` | — | low | auto-shape | array |
| `0x328a` | — | low | auto-shape | array |
| `0x329e` | — | low | auto-shape | array |
| `0x32b2` | — | low | auto-shape | array |
| `0x32c6` | — | low | auto-shape | array |
| `0x32da` | — | low | auto-shape | array |
| `0x3306` | — | low | auto-shape | array |
| `0x36ee` | — | low | auto-shape | array |
| `0x3ad6` | — | low | auto-shape | array |
| `0x6804` | — | low | auto-shape | array |
| `0x6868` | — | low | auto-shape | array |
| `0x7a6c` | — | low | auto-shape | array |
| `0x204fb` | — | low | auto-shape | array |
| `0x2052f` | — | low | auto-shape | array |
| `0x20533` | — | low | auto-shape | array |
| `0x20537` | — | low | auto-shape | array |
| `0x2053b` | — | low | auto-shape | array |
| `0x2053f` | — | low | auto-shape | array |
| `0x4dfef` | — | low | auto-shape | array |
| `0x521f3` | — | low | auto-shape | array |
| `0x52225` | — | low | auto-shape | array |
| `0x52257` | — | low | auto-shape | array |
| `0x522bb` | — | low | auto-shape | array |
| `0x55e3b` | — | low | auto-shape | array |
| `0x56b52` | — | low | auto-shape | array |
| `0x56f6d` | — | low | auto-shape | array |
| `0x577a3` | — | low | auto-shape | array |
| `0x5f0bb` | — | low | auto-shape | array |
| `0x617fe` | — | low | auto-shape | array |
| `0x61be6` | — | low | auto-shape | array |
| `0x61c4a` | — | low | auto-shape | array |
| `0x62032` | — | low | auto-shape | array |
| `0x6243a` | — | low | auto-shape | array |
| `0x6243c` | — | low | auto-shape | array |
| `0x62469` | — | low | auto-shape | array |
| `0x6247d` | — | low | auto-shape | array |
| `0x624a3` | — | low | auto-shape | array |
| `0x653e1` | — | low | auto-shape | array |
| `0x65bb1` | — | low | auto-shape | array |
| `0x6642d` | — | low | auto-shape | array |
| `0x66442` | — | low | auto-shape | array |
| `0x6650a` | — | low | auto-shape | array |
| `0x665d8` | — | low | auto-shape | array |
| `0x8fb41` | — | low | auto-shape | array |
| `0xa5eb9` | — | low | auto-shape | array |
| `0xaba3b` | — | low | auto-shape | array |
| `0xaba43` | — | low | auto-shape | array |
| `0xaba4b` | — | low | auto-shape | array |
| `0xaba53` | — | low | auto-shape | array |
| `0xaba5f` | — | low | auto-shape | array |
| `0xcca3a` | — | low | auto-shape | array |
| `0xccb66` | — | low | auto-shape | array |
| `0xccbd9` | — | low | auto-shape | array |
| `0xccbf8` | — | low | auto-shape | array |
| `0xccc11` | — | low | auto-shape | array |
| `0xccc2f` | — | low | auto-shape | array |
| `0xe7305` | — | low | auto-shape | array |
| `0xe730e` | — | low | auto-shape | array |
| `0xe73ed` | — | low | auto-shape | array |
| `0xe770d` | — | low | auto-shape | array |
| `0x15287b` | — | low | auto-shape | array |
| `0x15287d` | — | low | auto-shape | array |
| `0x15287f` | — | low | auto-shape | array |
| `0x152881` | — | low | auto-shape | array |
| `0x152885` | — | low | auto-shape | array |
| `0x152887` | — | low | auto-shape | array |
| `0x152889` | — | low | auto-shape | array |
| `0x15619c` | — | low | auto-shape | array |
| `0x1561ba` | — | low | auto-shape | array |
| `0x15a501` | — | low | auto-shape | array |
| `0x15a5c9` | — | low | auto-shape | array |
| `0x15a691` | — | low | auto-shape | array |

## index-pointer

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x32f1` | difficulty_index | high | investigation | GAMESTART's three-way difficulty selection. SETEN uses the zero-based index to select difficulty stat adjustments; FIELD indexes three-column stage-enemy arrays with it, and SETOBJ tests it against each object's difficulty mask. |
| `0x6718` | selected_party_slot | high | investigation | Current/selected slot in the 100-entry party-unit arrays. UNITECH chooses a free slot here before populating it; CHMENU replaces it with the selected sorted roster slot, then uses it to index party_slot_flags, party_slot_character_id, and companion per-slot tables. A natural New Game enters SC0000 with slot 2 selected. |
| `0x2052e` | current_faction_id | high | investigation | Active tactical faction/side. FIELD initializes it during stage entry, assigns 1 for the player phase, advances (value + 1) modulo 4 between phases, filters runtime entities and capturable objects by faction equality, and uses it as the bit index into tile_faction_traversal_masks. SCJUMP reads the same live stage context; this is not a story-progress counter. |
| `0x53edd` | selected_training_action_id | high | investigation | TRAIN's selected action row. It indexes all TRINIT eligibility, effect, text, award, and event arrays during detail rendering and execution. |
| `0x62424` | adv_gfx_resource_id | high | investigation | Transient ADV graphics-load input. Scene scripts set this immediately before their common layer loader, which passes it to set-texture together with adv_gfx_surface_slot_work. |
| `0x62450` | adv_gfx_layer_index | high | investigation | Current ADV graphics-layer row. All immediate assignments across the corpus are 0..7; common scene loaders use it to index adv_gfx_object_handles, per-layer geometry arrays, and adv_layer_surface_slots. |
| `0x62452` | adv_gfx_surface_slot_work | high | investigation | Working surface slot for the current ADV layer. Opcode 0x215 writes the retained object's existing source slot or -1; the common loader falls back to adv_layer_surface_slots, then passes the selected slot through set-texture, size queries, draw binding, and release. |
| `0x66713` | acting_entity_index | high | investigation | Entity whose movement or battle turn is being resolved. FIELD sets it before MVRTN; RTN_M providers use it for faction, position, resources, selected action, and routine progress. Together with target_entity_index it forms the attacker/target pair passed into battle. |
| `0x66714` | target_entity_index | high | investigation | Selected target/opponent entity. RTN_M051/052 choose and write an active foreign-faction target; FIELD pairs it with acting_entity_index for movement presentation and battle setup. |
| `0x66715` | current_unit_id | high | investigation | Shared current-unit selector used by character growth and setup scripts. CCINIT keys every class-change rule on this value; CALCCC, ADDEXP, SETEN, SALLY, and related scripts use it to index unit definitions and persistent per-unit state. |
| `0x8c877` | current_item_id | high | investigation | Shared item-id argument/selection slot. Item menus and gameplay scripts write a chosen item id, use it to index ITINIT arrays, and dispatch through item_handler_script_id; ITMES compares it against all 287 item ids to select the matching player-facing title and description. |
| `0xa6e59` | current_skill_id | high | investigation | Shared skill-id argument/selection slot. Skill menus and combat scripts write the chosen skill id and use it to index SKINIT arrays; SKMES compares it against all 131 skill ids to select the matching player-facing title and description. |
| `0xaac76` | current_condition_id | high | investigation | Shared condition selector consumed by CALCILL, ADDILL, ADDILLSUB, DISARM, FIELD, and related condition handlers. It indexes the 30-column runtime condition rows and ILINIT definition arrays. |
| `0xccc0a` | map_target_tile_x | high | investigation | Shared map target/focus X coordinate. Seventeen RTN_M providers unpack candidate coordinates here before testing selected_movement_route_steps, FIELD and SETROUTE use it as the selected destination, and LOOK converts it to the centered world X coordinate when no enemy spawn slot is selected. Fifty-five scripts reference the paired target coordinates. |
| `0xccc0b` | map_target_tile_y | high | investigation | Shared map target/focus Y coordinate paired with map_target_tile_x. Movement providers unpack the low sixteen bits of candidate coordinates here; FIELD and SETROUTE select the route endpoint, while story scripts and LOOK use the same pair to center the battlefield camera on scripted map locations. |
| `0xeff75` | current_routine_set_id | high | investigation | Shared RTINIT row selector. MVRTN loads the current entity's difficulty-selected movement routine set; BTRTN loads its battle routine set. Both then iterate routine_step_index across the selected twenty-slot row. |
| `0xeff76` | routine_step_index | high | investigation | Shared RTINIT step selector. MVRTN and BTRTN iterate it from zero through nineteen and use it as the column index in every routine bank and matching per-entity runtime row. |
| `0x1519f8` | current_card_id | high | investigation | FIELD's selected card-definition row. Card generation copies a surviving CDINIT card id here, uses it to test CDINIT2 story-flag gates, retains it through weighted selection, and indexes the chosen card's effects, graphics, name, and result text. |
| `0x152485` | current_card_generation_list_id | high | investigation | Selector consumed by CDINIT. FIELD loads it from the current STINIT type-28 card object's payload before rebuilding the parallel card-id and weight-schedule buffers; selectors 55 and 94 are authored but have no shipped STINIT object reference. |
| `0x152616` | current_entity_index | med | investigation | Primary current-entity row index (RECOVER-confirmed; purity 0.51, 363 row-index uses). |
| `0x152617` | current_stage_enemy_spawn_slot | high | investigation | Current STINIT enemy-template slot. FIELD selects slots 1..29 while materializing stage units, SETEN records the slot on the runtime entity and reads every parallel enemy buffer through it, and ADDEN uses slot 0 for its special generated unit. |
| `0x15261a` | battle_actor_side_index | high | investigation | Index of the acting side in BTL's two-entry battle arrays. CALCDMG derives battle_target_side_index as 1 minus this value; BTL uses the actor's unit-definition row for attack, skill, critical, and finishing-blow voices. |
| `0x15261b` | battle_target_side_index | high | investigation | Index of the target side in BTL's two-entry battle arrays. CALCDMG sets it to 1 - battle_actor_side_index; BTL applies battle_hp_delta to this side and uses its unit-definition row for damage and defeated voices. |
| `0x15288c` | selected_battle_animation_id | high | investigation | CALCDMG selects a skill_battle_animation_id or an equipped item's item_weapon_class, and BTL also writes passive reaction ids 801..808 and hardcoded defeat id 809. BTANINIT uses this value to select one six-effect timeline. |
| `0x1560e7` | current_magic_action_id | high | investigation | Shared selected magic/research/growth action id. MAGIC and STUDY index MAINIT's parallel columns with it, FIELD and USEMAGIC consume the selected action, and MAMES dispatches ids 1..9 to their untitled help descriptions. |
| `0x15a095` | information_tab_index | high | investigation | Selected INFO screen tab. INFO dispatches the five tab scripts with it; INFOMES uses values 0..2 as the column of information_message_handler_script_ids for INFOCH, INFOEN, and INFOVO. |
| `0x15a117` | current_character_profile_id | high | investigation | INFOCH's selected character-information row. It indexes CIINIT's name, unit, portrait, and placement arrays; CIMES compares the same id against all 24 profiles to dispatch the character biography. |
| `0x15a2a8` | current_glossary_topic_id | high | investigation | INFOVO's selected glossary/help topic. It indexes the VIINIT title and presentation arrays, then VIMES compares it against all 65 shipped topic ids to dispatch the full player-facing explanation. |
| `0x15a759` | current_enemy_encyclopedia_unit_id | high | investigation | INFOEN's selected enemy-encyclopedia unit id. It indexes EBINIT presentation and reveal arrays, seeds a temporary runtime entity for stat display, and EIMES compares it against 192 sparse unit ids to dispatch summary and strategy lines. |

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
| `0x7679` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 4 scripts; compared against [0, 1, 4, 8, 16, 256, 512, 1024]; writers=['FIELD.BIN']. |
| `0x767a` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 4 scripts; compared against [0]; writers=['DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN', 'FIELD.BIN']. |
| `0x767b` | — | med | auto-shape | TODO: confirm. Branch-read in 136 scenes / 150 scripts; compared against [0, 1]; writers=['DEBUGADV2.BIN', 'DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN']. |
| `0x767d` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 9 scripts; compared against [0, 1, 2, 3]; writers=['CAMP.BIN', 'DEBUGADV2.BIN', 'DEBUGMAP.BIN', 'DEBUGMAP2.BIN']. |
| `0x204f5` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against [1, 2, 4, 8, 16, 32, 64]; writers=['FIELD.BIN']. |
| `0x204f7` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 3 scripts; compared against [1, 2, 4, 6]; writers=['DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN', 'GAMESTART.BIN']. |
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
| `0x665d7` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 5 scripts; compared against [1, 3]; writers=['ADDSKILL.BIN', 'SBUNKI.BIN']. |
| `0xaba5c` | — | med | auto-shape | TODO: confirm. Branch-read in 136 scenes / 149 scripts; compared against [0, 1]; writers=['DEBUGADV.BIN', 'DEBUGADV2.BIN', 'SC0000.BIN', 'SC0010.BIN']. |
| `0xaba5e` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 5 scripts; compared against [2]; writers=['DEBUGMAP.BIN', 'DEBUGMAP2.BIN', 'DEBUGMAP3.BIN']. |
| `0xccc21` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 4 scripts; compared against [0]; writers=['CHMENU.BIN']. |
| `0x15a094` | — | med | auto-shape | TODO: confirm. Branch-read in 0 scenes / 6 scripts; compared against [0]; writers=['ALCHEMY.BIN', 'EVOLVE.BIN', 'SELSTAGE.BIN', 'STUDY.BIN']. |

## string-table

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0xd2` | save_slot_location_names | high | investigation | SAVE and SELSTAGE snapshot the current location label into this shared SAVE.DAT-selected string bank. SAVE.BIN restores it with op 0x1aa for the row's location field. |
| `0x1a4` | save_slot_protagonist_names | high | investigation | SAVE and SELSTAGE snapshot the active familiar's display name into this shared SAVE.DAT-selected string bank. SAVE.BIN restores it with op 0x1aa beside the portrait. |
| `0x27e` | unit_class_titles | high | investigation | Persistent per-unit class/title string table. CALCCC writes the selected class_change_title_output into the current unit's cell; character and status presentation scripts read the resulting title. |
| `0x315` | unit_story_display_names | high | investigation | CNINIT's sparse unit-id keyed story-name table. Scene scripts and HISTORY draw the selected speaker's value, DEBUGADV uses it for its scripted speaker previews, and INPUTNAME rejects a player-entered familiar name that collides with any populated row. The table intentionally leaves Lily's form ids 2..4 empty. |
| `0x7db` | modal_message_lines | high | investigation | Shared ten-string modal buffer. Producers append messages or menu options at modal_message_line_count; MES measures and draws each string, and SBUNKI reuses the same entries for an interactive selection list. |
| `0x7e5` | modal_annotation_texts | high | investigation | Optional small-font annotations rendered by MES and SBUNKI after the primary modal lines. No direct shipped producer was found; the paired count and placement arrays expose a reserved/extensible annotation ABI. |
| `0x25fa` | condition_level_names | high | investigation | ILINIT's row-major five-name matrix. Runtime condition UI selects the row by condition id and the column by current level minus one; id 1 has only the unnumbered instant-death label, id 12 and ids 15..29 are reserved. Columns: 0=level_1, 1=level_2, 2=level_3, 3=level_4, 4=level_5. |
| `0x2690` | attack_element_names | high | investigation | AFINIT attack-side affinity vocabulary. DRAWTIP indexes ids 1..8 from ITINIT, while INFOAF displays those same eight ordinary attack elements above attack_element_effectiveness_percent. Columns: 1=physical, 2=universal, 3=fire, 4=ice, 5=lightning, 6=earth, 7=holy, 8=dark, 11=resistance_1, 12=resistance_2, 13=resistance_3, 14=resistance_4, 15=resistance_5, 16=resistance_6, 17=resistance_7. |
| `0x26a4` | defense_element_names | high | investigation | AFINIT defense-side affinity vocabulary. DRAWENP and DRAWTIP index it with unit/item defense element ids; the same id selects a row of attack_element_effectiveness_percent. Columns: 1=physical, 2=universal, 3=fire, 4=ice, 5=lightning, 6=earth, 7=holy, 8=dark, 9=divinity, 10=demon, 11=spirit, 12=undead. |
| `0x26b4` | class_change_title_output | high | investigation | CCINIT writes the title selected by each eligible class-change rule. CALCCC copies it to unit_class_titles for a successful promotion, and ADDEXP includes the same string in the level-up notification. |
| `0x26b5` | terrain_type_names | high | investigation | LAINIT's terrain vocabulary. MPINIT's atlas cells contain terrain ids 0..19; the named ids distinguish passages, rooms, hidden spaces, bases, water, openings, altars, lava, and themed room variants. |
| `0x26d3` | terrain_effect_descriptions | high | investigation | LAINIT's player-facing terrain effect summary indexed by terrain id. FIELD shows it beneath the selected tile's terrain name; each populated string exactly describes that row of terrain_combat_stat_deltas. |
| `0x26f1` | object_type_names | high | investigation | OBINIT writes the authoritative object names. FIELD, SETOBJ, and DRAWOBJ use STINIT's object type id to select these definitions; extract_init joins the names to stage object placements. |
| `0x2755` | object_type_descriptions | high | investigation | OBINIT writes the short object descriptions displayed by the field object-information path. extract_init joins populated descriptions to STINIT object placements by type id. |
| `0x27b9` | stage_victory_condition_1 | high | investigation | STINIT writes one value for each of its 74 stage records. AIM renders this line first in the victory-condition section, and FIELD copies it into the current mission-condition display. |
| `0x27ba` | stage_victory_condition_2 | high | investigation | STINIT writes one value for each of its 74 stage records. AIM renders nonempty values after stage_victory_condition_1, and FIELD copies the slot into the current mission-condition display. |
| `0x27bb` | stage_defeat_condition_1 | high | investigation | STINIT writes one value for each of its 74 stage records. AIM renders this line first in the defeat-condition section, and FIELD copies it into the current mission-condition display. |
| `0x27bc` | stage_defeat_condition_2 | high | investigation | STINIT writes one value for each of its 74 stage records. AIM renders nonempty values after stage_defeat_condition_1, and FIELD copies the slot into the current mission-condition display. |
| `0x27bd` | stage_display_names | high | investigation | FORT and SELSTAGE enumerate this sparse catalog and render the selected title, while FIELD copies the active title into its shared display buffer. STINIT2 reserves 1,000 ids even though the shipped rows end at 170. |
| `0x2ba5` | stage_description_lines | high | investigation | SELSTAGE multiplies stage_clear_state by three and renders the corresponding three-column half of the selected STINIT2 row. The explicit 1,000-by-6 geometry prevents these strings from being mistaken for additional stage records. Columns: 0=uncleared_line_1, 1=uncleared_line_2, 2=uncleared_line_3, 3=cleared_line_1, 4=cleared_line_2, 5=cleared_line_3. |
| `0x4315` | card_definition_names | high | investigation | Card names populated by CDINIT2. FIELD indexes this table with current_card_id when presenting the card selected from CDINIT's weighted generation list. |
| `0x4379` | card_definition_result_messages | high | investigation | Short player-facing result text paired with card_definition_names. FIELD displays the selected card's row after applying its CDINIT2 effect. |
| `0x43dd` | name_entry_character_palette | high | investigation | INPUTNAME's five 70-cell character pages: row 0 hiragana, row 1 katakana, row 2 full-width Latin letters, row 3 Arabic/Kanji/Roman/circled numerals, and row 4 symbols. Cursor slots 70..74 select the page; selecting a populated cell copies it into the seven-character name buffer. |
| `0x45b9` | magic_action_names | high | investigation | MAINIT's one-based action-name array: eleven shipped records occupy ids 1..11 in a reserved 30-cell span. MAGIC, STUDY, and EVOLVE render the selected entry. |
| `0x45d7` | character_profile_names | high | investigation | CIINIT writes the character-information screen's 24 display names. INFOCH lists them by profile id, masks unrevealed rows, and uses the same id to select the backing unit metadata and CIMES biography. |
| `0x463b` | glossary_topic_titles | high | investigation | VIINIT writes the sparse glossary title table. INFOVO renders the selected title, and extract_init joins all 65 populated ids to VIMES's full help text. |
| `0x276` | — | med | auto-shape | string-table (written by SC0130) |
| `0x277` | — | med | auto-shape | string-table (written by SC0130) |
| `0x278` | — | med | auto-shape | string-table (written by FIELD) |
| `0x279` | — | med | auto-shape | string-table (written by INPUTNAME) |
| `0x27a` | — | med | auto-shape | string-table (written by READY) |
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
| `0x15a096` | information_message_handled | high | investigation | INFOMES clears this before walking handler rows and stops when it becomes one. CIMES, EIMES, and VIMES set it after rendering a matching character, enemy, or glossary message, implementing a first-handler-wins extension chain. |

## unknown

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x3238` | — | low | inference | Config/settings global (CONFIG.BIN/INITCONFIG.BIN writer, scene-reach 0) — NOT a scene story flag; miner over-tagged it. Branch-read in 5 scripts; compared against [1]. |
| `0x3301` | — | low | inference | Config/settings global (INITCONFIG.BIN writer, scene-reach 0) — NOT a scene story flag; miner over-tagged it. Branch-read in 6 scripts; compared against [1, 2]. |
| `0x3303` | — | low | inference | Config/settings global (CONFIG.BIN/INITCONFIG.BIN writer, scene-reach 0) — NOT a scene story flag; miner over-tagged it. Branch-read in 8 scripts; compared against [1]. |
| `0x3304` | — | low | inference | Config/settings global (CONFIG.BIN/INITCONFIG.BIN writer, scene-reach 0) — NOT a scene story flag; miner over-tagged it. Branch-read in 13 scripts; compared against [1]. |
| `0x6249e` | — | low | inference | Graphics-subsystem state in the 0x624xx gfx-object range (set via `mov 0x6249e,<n>` alongside `mov 0x62450,<slot>`). Likely a draw/slot parameter, NOT a story flag. Branch-read in 78 scenes. |
| `0xab8e7` | class_change_selected_level | high | investigation | CALCCC clears this accumulator before invoking the class-change rule scripts. Each eligible CCINIT rule replaces it only when its threshold is higher than the current selection; nonzero then signals CALCCC and ADDEXP to apply and report the chosen promotion. |
| `0xab8e8` | class_change_deployment_cost_delta | high | investigation | Shared class-change output initialized by CALCCC and incremented by the selected CCINIT rule. CALCCC adds it to unit_deployment_cost_adjustments and ADDEXP reports the signed cost change. |
| `0xab8e9` | class_change_stat_bonuses | high | investigation | Fourteen-cell class-change accumulator using the unit stat column order. CALCCC zeroes it, CCINIT adds the selected rule's bonuses, and CALCCC adds the result to unit_current_stats with per-column caps. EVOLVE separately clears it and calls CCINIT to preview the next Lily form's movement bonus. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=speed, 7=luck, 8=critical_chance, 9=capture_power, 10=movement, 11=max_hp, 12=max_sp, 13=max_fs. |
| `0xab8f7` | class_change_skill_awards | high | investigation | Four-cell class-change skill output. CALCCC zeroes the buffer, CCINIT writes awarded skill ids, and CALCCC copies positive values from the first three cells into unit_skill_ids before ADDEXP reports the changes. Columns: 0=skill_slot_1, 1=skill_slot_2, 2=skill_slot_3, 3=skill_slot_4. |
| `0xab8fb` | class_change_state_work | high | investigation | Ten-cell working copy of the current unit's unit_class_change_state row. CCINIT sets the slot belonging to an awarded rule; CALCCC manages the block copy between this buffer and persistent per-unit state. Columns: 0=promotion_slot_1, 1=promotion_slot_2, 2=promotion_slot_3, 3=promotion_slot_4, 4=promotion_slot_5, 5=promotion_slot_6, 6=promotion_slot_7, 7=promotion_slot_8, 8=promotion_slot_9, 9=promotion_slot_10. |
| `0x152618` | battle_outcome_flags | high | investigation | BTL derives the low three bits from the acting and target entities' post-exchange HP. Mask 4 selects the nonlethal experience table; otherwise BTL uses the defeat table. FIELD consumes and combines the result during post-battle flow, then passes it to SCJUMP, whose former progress-C comparisons are battle-outcome decisions. |
| `0x15261c` | battle_hit_result | high | investigation | CALCDMG initializes this to miss, changes it to hit after the accuracy gate, and then to critical after the critical-chance gate. BTL uses the same result to choose presentation, cut-ins, and critical voice columns; -1 is written only by a synthetic pre-battle path. |
| `0x15261d` | battle_hp_delta | high | investigation | CALCDMG's signed HP result. BTL subtracts it from the target's current HP, so positive values deal damage and negative values heal; zero and result state gate damage reactions and defeat handling. |
| `0x15261e` | battle_actor_hp_recovery | high | investigation | CALCDMG's actor-side HP recovery output. Absorb (SKINIT skill 40) contributes half of positive battle_hp_delta with a minimum of one, and the target's HP-absorption condition can add its level-scaled amount capped at the damage dealt. BTL adds the result to the actor's current HP, renders the green recovery number, and clamps the resource through the ordinary post-battle path. |
| `0x84a` | — | high | auto-shape | unit-name-table |
| `0xc32` | — | high | auto-shape | unit-desc-table |
| `0x101a` | — | high | auto-shape | unit-desc1-table |
| `0x14ca` | — | high | auto-shape | unit-desc2-table |
| `0x14cb` | — | high | auto-shape | unit-desc3-table |
| `0x1bd2` | — | high | auto-shape | item-name-table |
| `0x1fba` | — | high | auto-shape | item-desc-table |
| `0x23a2` | — | high | auto-shape | skill-name-table |
| `0x24ce` | — | high | auto-shape | skill-desc-table |
| `0x32f0` | — | med | auto-shape | current-entity-index? |
| `0x3ebe` | — | med | auto-shape | record-table[stride 10] |
| `0x7e54` | — | med | auto-shape | record-table[stride 50] |
| `0x4e3d7` | — | med | auto-shape | record-table[stride 14] |
| `0x53a2b` | — | med | auto-shape | record-table[stride 20] |
| `0x53ef5` | — | low | auto-shape | index/counter? |
| `0x53ef7` | — | med | auto-shape | record-table[stride 8] |
| `0x57356` | — | med | auto-shape | current-entity-index? |
| `0x62436` | — | low | auto-shape | index/counter? |
| `0x62ccb` | — | low | auto-shape | index/counter? |
| `0x81c96` | — | med | auto-shape | record-table[stride 3] |
| `0x8284e` | — | med | auto-shape | record-table[stride 3] |
| `0xcc9f0` | — | low | auto-shape | index/counter? |
| `0xccbdc` | — | med | auto-shape | record-table[stride 14] |
| `0xccc33` | — | med | auto-shape | record-table[stride 3] |
| `0xe6c5e` | — | med | auto-shape | obinit-field |
| `0xe6cc2` | — | med | auto-shape | obinit-field |
| `0xe6d26` | — | med | auto-shape | obinit-field |
| `0xe6d8a` | — | med | auto-shape | obinit-field |
| `0xe6e52` | — | low | auto-shape | obinit-field? |
| `0xe6eb6` | — | med | auto-shape | obinit-field |
| `0xe6f1a` | — | low | auto-shape | obinit-field? |
| `0xe6f7e` | — | low | auto-shape | obinit-field? |
| `0xe6fe2` | — | med | auto-shape | record-table[stride 3] |
| `0xe710e` | — | low | auto-shape | obinit-field? |
| `0xe7172` | — | low | auto-shape | obinit-field? |
| `0xe71d6` | — | med | auto-shape | record-table[stride 3] |
| `0x1519fa` | — | med | auto-shape | cdinit2-field |
| `0x151a7a` | — | low | auto-shape | cdinit2-field? |
| `0x151a7c` | — | low | auto-shape | cdinit2-field? |
| `0x151a7d` | — | low | auto-shape | cdinit2-field? |
| `0x151a7e` | — | low | auto-shape | cdinit2-field? |
| `0x151a7f` | — | low | auto-shape | cdinit2-field? |
| `0x151a80` | — | low | auto-shape | cdinit2-field? |
| `0x151a81` | — | low | auto-shape | cdinit2-field? |
| `0x151a82` | — | low | auto-shape | cdinit2-field? |
| `0x151a83` | — | low | auto-shape | cdinit2-field? |
| `0x151a84` | — | low | auto-shape | cdinit2-field? |
| `0x151a85` | — | low | auto-shape | cdinit2-field? |
| `0x151a86` | — | low | auto-shape | cdinit2-field? |
| `0x151a87` | — | low | auto-shape | cdinit2-field? |
| `0x151a88` | — | low | auto-shape | cdinit2-field? |
| `0x151a89` | — | low | auto-shape | cdinit2-field? |
| `0x151a8a` | — | low | auto-shape | cdinit2-field? |
| `0x151a8b` | — | low | auto-shape | cdinit2-field? |
| `0x151a8c` | — | low | auto-shape | cdinit2-field? |
| `0x151a8d` | — | low | auto-shape | cdinit2-field? |
| `0x151a8e` | — | low | auto-shape | cdinit2-field? |
| `0x151a8f` | — | low | auto-shape | cdinit2-field? |
| `0x151a90` | — | low | auto-shape | cdinit2-field? |
| `0x151a91` | — | low | auto-shape | cdinit2-field? |
| `0x151a92` | — | low | auto-shape | cdinit2-field? |
| `0x151a94` | — | low | auto-shape | cdinit2-field? |
| `0x151a95` | — | low | auto-shape | cdinit2-field? |
| `0x151a96` | — | low | auto-shape | cdinit2-field? |
| `0x151a97` | — | low | auto-shape | cdinit2-field? |
| `0x151a98` | — | low | auto-shape | cdinit2-field? |
| `0x151a99` | — | low | auto-shape | cdinit2-field? |
| `0x151a9a` | — | low | auto-shape | cdinit2-field? |
| `0x151a9b` | — | low | auto-shape | cdinit2-field? |
| `0x151a9c` | — | low | auto-shape | cdinit2-field? |
| `0x151a9d` | — | low | auto-shape | cdinit2-field? |
| `0x151a9e` | — | low | auto-shape | cdinit2-field? |
| `0x151a9f` | — | low | auto-shape | cdinit2-field? |
| `0x151aa0` | — | low | auto-shape | cdinit2-field? |
| `0x151aa1` | — | low | auto-shape | cdinit2-field? |
| `0x151aa2` | — | low | auto-shape | cdinit2-field? |
| `0x151aa3` | — | low | auto-shape | cdinit2-field? |
| `0x151aa4` | — | low | auto-shape | cdinit2-field? |
| `0x151aa5` | — | low | auto-shape | cdinit2-field? |
| `0x151aa6` | — | low | auto-shape | cdinit2-field? |
| `0x151aa7` | — | low | auto-shape | cdinit2-field? |
| `0x151aa8` | — | low | auto-shape | cdinit2-field? |
| `0x151aa9` | — | low | auto-shape | cdinit2-field? |
| `0x151aaa` | — | low | auto-shape | cdinit2-field? |
| `0x151aac` | — | low | auto-shape | cdinit2-field? |
| `0x151aad` | — | low | auto-shape | cdinit2-field? |
| `0x151aae` | — | low | auto-shape | cdinit2-field? |
| `0x151aaf` | — | low | auto-shape | cdinit2-field? |
| `0x151ab0` | — | low | auto-shape | cdinit2-field? |
| `0x151ab1` | — | low | auto-shape | cdinit2-field? |
| `0x151ab2` | — | low | auto-shape | cdinit2-field? |
| `0x151ab3` | — | low | auto-shape | cdinit2-field? |
| `0x151ab4` | — | low | auto-shape | cdinit2-field? |
| `0x151ab5` | — | low | auto-shape | cdinit2-field? |
| `0x151ab6` | — | low | auto-shape | cdinit2-field? |
| `0x151ab7` | — | low | auto-shape | cdinit2-field? |
| `0x151ab8` | — | low | auto-shape | cdinit2-field? |
| `0x151ab9` | — | low | auto-shape | cdinit2-field? |
| `0x151aba` | — | low | auto-shape | cdinit2-field? |
| `0x151abb` | — | low | auto-shape | cdinit2-field? |
| `0x151abc` | — | low | auto-shape | cdinit2-field? |
| `0x151abd` | — | low | auto-shape | cdinit2-field? |
| `0x151abe` | — | low | auto-shape | cdinit2-field? |
| `0x151abf` | — | low | auto-shape | cdinit2-field? |
| `0x151ac0` | — | low | auto-shape | cdinit2-field? |
| `0x151ac1` | — | low | auto-shape | cdinit2-field? |
| `0x151ba6` | — | low | auto-shape | cdinit2-field? |
| `0x151ba8` | — | low | auto-shape | cdinit2-field? |
| `0x151baa` | — | low | auto-shape | cdinit2-field? |
| `0x151bac` | — | low | auto-shape | cdinit2-field? |
| `0x151bae` | — | low | auto-shape | cdinit2-field? |
| `0x151bb0` | — | low | auto-shape | cdinit2-field? |
| `0x151bb2` | — | low | auto-shape | cdinit2-field? |
| `0x151bb4` | — | low | auto-shape | cdinit2-field? |
| `0x151bb6` | — | low | auto-shape | cdinit2-field? |
| `0x151bb8` | — | low | auto-shape | cdinit2-field? |
| `0x151bba` | — | low | auto-shape | cdinit2-field? |
| `0x151bbe` | — | low | auto-shape | cdinit2-field? |
| `0x151bc0` | — | low | auto-shape | cdinit2-field? |
| `0x151bc2` | — | low | auto-shape | cdinit2-field? |
| `0x151bc4` | — | low | auto-shape | cdinit2-field? |
| `0x151bc6` | — | low | auto-shape | cdinit2-field? |
| `0x151bc8` | — | low | auto-shape | cdinit2-field? |
| `0x151bca` | — | low | auto-shape | cdinit2-field? |
| `0x151bcc` | — | low | auto-shape | cdinit2-field? |
| `0x151bce` | — | low | auto-shape | cdinit2-field? |
| `0x151bd0` | — | low | auto-shape | cdinit2-field? |
| `0x151bd2` | — | low | auto-shape | cdinit2-field? |
| `0x151bd6` | — | low | auto-shape | cdinit2-field? |
| `0x151bd8` | — | low | auto-shape | cdinit2-field? |
| `0x151bda` | — | low | auto-shape | cdinit2-field? |
| `0x151bdc` | — | low | auto-shape | cdinit2-field? |
| `0x151bde` | — | low | auto-shape | cdinit2-field? |
| `0x151be0` | — | low | auto-shape | cdinit2-field? |
| `0x151be2` | — | low | auto-shape | cdinit2-field? |
| `0x151be4` | — | low | auto-shape | cdinit2-field? |
| `0x151be6` | — | low | auto-shape | cdinit2-field? |
| `0x151be8` | — | low | auto-shape | cdinit2-field? |
| `0x151bea` | — | low | auto-shape | cdinit2-field? |
| `0x151cb6` | — | low | auto-shape | cdinit2-field? |
| `0x151d1a` | — | low | auto-shape | cdinit2-field? |
| `0x151d7e` | — | low | auto-shape | cdinit2-field? |
| `0x151de6` | — | low | auto-shape | cdinit2-field? |
| `0x151de7` | — | low | auto-shape | cdinit2-field? |
| `0x151de8` | — | low | auto-shape | cdinit2-field? |
| `0x151de9` | — | low | auto-shape | cdinit2-field? |
| `0x151dea` | — | low | auto-shape | cdinit2-field? |
| `0x151f12` | — | low | auto-shape | cdinit2-field? |
| `0x151f13` | — | low | auto-shape | cdinit2-field? |
| `0x151f14` | — | low | auto-shape | cdinit2-field? |
| `0x151f15` | — | low | auto-shape | cdinit2-field? |
| `0x151f16` | — | low | auto-shape | cdinit2-field? |
| `0x15203a` | — | low | auto-shape | cdinit2-field? |
| `0x15209e` | — | low | auto-shape | cdinit2-field? |
| `0x152166` | — | low | auto-shape | cdinit2-field? |
| `0x152167` | — | low | auto-shape | cdinit2-field? |
| `0x152168` | — | low | auto-shape | cdinit2-field? |
| `0x152169` | — | low | auto-shape | cdinit2-field? |
| `0x15216a` | — | low | auto-shape | cdinit2-field? |
| `0x152171` | — | low | auto-shape | cdinit2-field? |
| `0x152174` | — | low | auto-shape | cdinit2-field? |
| `0x152292` | — | low | auto-shape | cdinit2-field? |
| `0x152293` | — | low | auto-shape | cdinit2-field? |
| `0x152294` | — | low | auto-shape | cdinit2-field? |
| `0x152295` | — | low | auto-shape | cdinit2-field? |
| `0x152296` | — | low | auto-shape | cdinit2-field? |
| `0x15229d` | — | low | auto-shape | cdinit2-field? |
| `0x1522a0` | — | low | auto-shape | cdinit2-field? |
| `0x15235a` | — | low | auto-shape | cdinit2-field? |
| `0x1523be` | — | low | auto-shape | cdinit2-field? |
| `0x152422` | — | med | auto-shape | cdinit2-field |
| `0x152878` | — | low | auto-shape | index/counter? |
| `0x15288b` | — | low | auto-shape | index/counter? |
| `0x152894` | — | low | auto-shape | btaninit2-field? |
| `0x152895` | — | low | auto-shape | btaninit2-field? |
| `0x152896` | — | low | auto-shape | btaninit2-field? |
| `0x152897` | — | low | auto-shape | btaninit2-field? |
| `0x152899` | — | low | auto-shape | btaninit2-field? |
| `0x15289a` | — | low | auto-shape | btaninit2-field? |
| `0x15289b` | — | low | auto-shape | btaninit2-field? |
| `0x15289c` | — | low | auto-shape | btaninit2-field? |
| `0x15289d` | — | low | auto-shape | btaninit2-field? |
| `0x15289f` | — | low | auto-shape | btaninit2-field? |
| `0x1528a0` | — | low | auto-shape | btaninit2-field? |
| `0x1528a1` | — | low | auto-shape | btaninit2-field? |
| `0x1528a2` | — | low | auto-shape | btaninit2-field? |
| `0x1528a3` | — | low | auto-shape | btaninit2-field? |
| `0x1528a5` | — | low | auto-shape | btaninit2-field? |
| `0x1528a6` | — | low | auto-shape | btaninit2-field? |
| `0x1528a7` | — | low | auto-shape | btaninit2-field? |
| `0x1528a8` | — | low | auto-shape | btaninit2-field? |
| `0x1528a9` | — | low | auto-shape | btaninit2-field? |
| `0x1528ab` | — | low | auto-shape | btaninit2-field? |
| `0x1528ac` | — | low | auto-shape | btaninit2-field? |
| `0x1528ad` | — | low | auto-shape | btaninit2-field? |
| `0x1528ae` | — | low | auto-shape | btaninit2-field? |
| `0x1528af` | — | low | auto-shape | btaninit2-field? |
| `0x1528b1` | — | low | auto-shape | btaninit2-field? |
| `0x1528b2` | — | low | auto-shape | btaninit2-field? |
| `0x1528b3` | — | low | auto-shape | btaninit2-field? |
| `0x1528b4` | — | low | auto-shape | btaninit2-field? |
| `0x1528b5` | — | low | auto-shape | btaninit2-field? |
| `0x1528b7` | — | low | auto-shape | btaninit2-field? |
| `0x1528b8` | — | low | auto-shape | btaninit2-field? |
| `0x1528b9` | — | low | auto-shape | btaninit2-field? |
| `0x1528ba` | — | low | auto-shape | btaninit2-field? |
| `0x1528bb` | — | low | auto-shape | btaninit2-field? |
| `0x1528bd` | — | low | auto-shape | btaninit2-field? |
| `0x1528be` | — | low | auto-shape | btaninit2-field? |
| `0x1528bf` | — | low | auto-shape | btaninit2-field? |
| `0x1528c0` | — | low | auto-shape | btaninit2-field? |
| `0x1528c1` | — | low | auto-shape | btaninit2-field? |
| `0x1528c3` | — | low | auto-shape | btaninit2-field? |
| `0x1528c4` | — | low | auto-shape | btaninit2-field? |
| `0x1528c5` | — | low | auto-shape | btaninit2-field? |
| `0x1528c6` | — | low | auto-shape | btaninit2-field? |
| `0x1528c7` | — | low | auto-shape | btaninit2-field? |
| `0x1528c9` | — | low | auto-shape | btaninit2-field? |
| `0x1528ca` | — | low | auto-shape | btaninit2-field? |
| `0x1528cb` | — | low | auto-shape | btaninit2-field? |
| `0x1528cc` | — | low | auto-shape | btaninit2-field? |
| `0x1528cd` | — | low | auto-shape | btaninit2-field? |
| `0x1528cf` | — | low | auto-shape | btaninit2-field? |
| `0x1528d0` | — | low | auto-shape | btaninit2-field? |
| `0x1528d1` | — | low | auto-shape | btaninit2-field? |
| `0x1528d2` | — | low | auto-shape | btaninit2-field? |
| `0x1528d3` | — | low | auto-shape | btaninit2-field? |
| `0x1528d5` | — | low | auto-shape | btaninit2-field? |
| `0x1528d6` | — | low | auto-shape | btaninit2-field? |
| `0x1528d7` | — | low | auto-shape | btaninit2-field? |
| `0x1528d8` | — | low | auto-shape | btaninit2-field? |
| `0x1528d9` | — | low | auto-shape | btaninit2-field? |
| `0x1528db` | — | low | auto-shape | btaninit2-field? |
| `0x1528dc` | — | low | auto-shape | btaninit2-field? |
| `0x1528dd` | — | low | auto-shape | btaninit2-field? |
| `0x1528de` | — | low | auto-shape | btaninit2-field? |
| `0x1528df` | — | low | auto-shape | btaninit2-field? |
| `0x1528e1` | — | low | auto-shape | btaninit2-field? |
| `0x1528e2` | — | low | auto-shape | btaninit2-field? |
| `0x1528e3` | — | low | auto-shape | btaninit2-field? |
| `0x1528e4` | — | low | auto-shape | btaninit2-field? |
| `0x1528e5` | — | low | auto-shape | btaninit2-field? |
| `0x1528e7` | — | low | auto-shape | btaninit2-field? |
| `0x1528e8` | — | low | auto-shape | btaninit2-field? |
| `0x1528e9` | — | low | auto-shape | btaninit2-field? |
| `0x1528ea` | — | low | auto-shape | btaninit2-field? |
| `0x1528eb` | — | low | auto-shape | btaninit2-field? |
| `0x1528ed` | — | low | auto-shape | btaninit2-field? |
| `0x1528ee` | — | low | auto-shape | btaninit2-field? |
| `0x1528ef` | — | low | auto-shape | btaninit2-field? |
| `0x1528f0` | — | low | auto-shape | btaninit2-field? |
| `0x1528f1` | — | low | auto-shape | btaninit2-field? |
| `0x1528f3` | — | low | auto-shape | btaninit2-field? |
| `0x1528f4` | — | low | auto-shape | btaninit2-field? |
| `0x1528f5` | — | low | auto-shape | btaninit2-field? |
| `0x1528f6` | — | low | auto-shape | btaninit2-field? |
| `0x1528f7` | — | low | auto-shape | btaninit2-field? |
| `0x1528f9` | — | low | auto-shape | btaninit2-field? |
| `0x1528fa` | — | low | auto-shape | btaninit2-field? |
| `0x1528fb` | — | low | auto-shape | btaninit2-field? |
| `0x1528fc` | — | low | auto-shape | btaninit2-field? |
| `0x1528fd` | — | low | auto-shape | btaninit2-field? |
| `0x1528ff` | — | low | auto-shape | btaninit2-field? |
| `0x152900` | — | low | auto-shape | btaninit2-field? |
| `0x152901` | — | low | auto-shape | btaninit2-field? |
| `0x152902` | — | low | auto-shape | btaninit2-field? |
| `0x152903` | — | low | auto-shape | btaninit2-field? |
| `0x152905` | — | low | auto-shape | btaninit2-field? |
| `0x152906` | — | low | auto-shape | btaninit2-field? |
| `0x152907` | — | low | auto-shape | btaninit2-field? |
| `0x152908` | — | low | auto-shape | btaninit2-field? |
| `0x152909` | — | low | auto-shape | btaninit2-field? |
| `0x15290b` | — | low | auto-shape | btaninit2-field? |
| `0x15290c` | — | low | auto-shape | btaninit2-field? |
| `0x15290d` | — | low | auto-shape | btaninit2-field? |
| `0x15290e` | — | low | auto-shape | btaninit2-field? |
| `0x15290f` | — | low | auto-shape | btaninit2-field? |
| `0x152911` | — | low | auto-shape | btaninit2-field? |
| `0x152aec` | — | low | auto-shape | btaninit2-field? |
| `0x152aed` | — | low | auto-shape | btaninit2-field? |
| `0x152aee` | — | low | auto-shape | btaninit2-field? |
| `0x152aef` | — | low | auto-shape | btaninit2-field? |
| `0x152af1` | — | low | auto-shape | btaninit2-field? |
| `0x152af2` | — | low | auto-shape | btaninit2-field? |
| `0x152af3` | — | low | auto-shape | btaninit2-field? |
| `0x152af4` | — | low | auto-shape | btaninit2-field? |
| `0x152af6` | — | low | auto-shape | btaninit2-field? |
| `0x152af7` | — | low | auto-shape | btaninit2-field? |
| `0x152af8` | — | low | auto-shape | btaninit2-field? |
| `0x152af9` | — | low | auto-shape | btaninit2-field? |
| `0x152afb` | — | low | auto-shape | btaninit2-field? |
| `0x152afc` | — | low | auto-shape | btaninit2-field? |
| `0x152afd` | — | low | auto-shape | btaninit2-field? |
| `0x152afe` | — | low | auto-shape | btaninit2-field? |
| `0x152b00` | — | low | auto-shape | btaninit2-field? |
| `0x152b01` | — | low | auto-shape | btaninit2-field? |
| `0x152b02` | — | low | auto-shape | btaninit2-field? |
| `0x152b03` | — | low | auto-shape | btaninit2-field? |
| `0x152b05` | — | low | auto-shape | btaninit2-field? |
| `0x152b06` | — | low | auto-shape | btaninit2-field? |
| `0x152b07` | — | low | auto-shape | btaninit2-field? |
| `0x152b08` | — | low | auto-shape | btaninit2-field? |
| `0x152b0a` | — | low | auto-shape | btaninit2-field? |
| `0x152b0b` | — | low | auto-shape | btaninit2-field? |
| `0x152b0c` | — | low | auto-shape | btaninit2-field? |
| `0x152b0d` | — | low | auto-shape | btaninit2-field? |
| `0x152b0f` | — | low | auto-shape | btaninit2-field? |
| `0x152b10` | — | low | auto-shape | btaninit2-field? |
| `0x152b11` | — | low | auto-shape | btaninit2-field? |
| `0x152b12` | — | low | auto-shape | btaninit2-field? |
| `0x152b14` | — | low | auto-shape | btaninit2-field? |
| `0x152b15` | — | low | auto-shape | btaninit2-field? |
| `0x152b16` | — | low | auto-shape | btaninit2-field? |
| `0x152b17` | — | low | auto-shape | btaninit2-field? |
| `0x152b19` | — | low | auto-shape | btaninit2-field? |
| `0x152b1a` | — | low | auto-shape | btaninit2-field? |
| `0x152b1b` | — | low | auto-shape | btaninit2-field? |
| `0x152b1c` | — | low | auto-shape | btaninit2-field? |
| `0x152b1e` | — | low | auto-shape | btaninit2-field? |
| `0x152b1f` | — | low | auto-shape | btaninit2-field? |
| `0x152b20` | — | low | auto-shape | btaninit2-field? |
| `0x152b21` | — | low | auto-shape | btaninit2-field? |
| `0x152b23` | — | low | auto-shape | btaninit2-field? |
| `0x152b24` | — | low | auto-shape | btaninit2-field? |
| `0x152b25` | — | low | auto-shape | btaninit2-field? |
| `0x152b26` | — | low | auto-shape | btaninit2-field? |
| `0x152b28` | — | low | auto-shape | btaninit2-field? |
| `0x152b29` | — | low | auto-shape | btaninit2-field? |
| `0x152b2a` | — | low | auto-shape | btaninit2-field? |
| `0x152b2b` | — | low | auto-shape | btaninit2-field? |
| `0x152b2d` | — | low | auto-shape | btaninit2-field? |
| `0x152b2e` | — | low | auto-shape | btaninit2-field? |
| `0x152b2f` | — | low | auto-shape | btaninit2-field? |
| `0x152b30` | — | low | auto-shape | btaninit2-field? |
| `0x152b32` | — | low | auto-shape | btaninit2-field? |
| `0x152b33` | — | low | auto-shape | btaninit2-field? |
| `0x152b34` | — | low | auto-shape | btaninit2-field? |
| `0x152b35` | — | low | auto-shape | btaninit2-field? |
| `0x152b37` | — | low | auto-shape | btaninit2-field? |
| `0x152b38` | — | low | auto-shape | btaninit2-field? |
| `0x152b39` | — | low | auto-shape | btaninit2-field? |
| `0x152b3a` | — | low | auto-shape | btaninit2-field? |
| `0x152b3c` | — | low | auto-shape | btaninit2-field? |
| `0x152b3d` | — | low | auto-shape | btaninit2-field? |
| `0x152b3e` | — | low | auto-shape | btaninit2-field? |
| `0x152b3f` | — | low | auto-shape | btaninit2-field? |
| `0x152b41` | — | low | auto-shape | btaninit2-field? |
| `0x152b42` | — | low | auto-shape | btaninit2-field? |
| `0x152b43` | — | low | auto-shape | btaninit2-field? |
| `0x152b44` | — | low | auto-shape | btaninit2-field? |
| `0x152b46` | — | low | auto-shape | btaninit2-field? |
| `0x152b47` | — | low | auto-shape | btaninit2-field? |
| `0x152b48` | — | low | auto-shape | btaninit2-field? |
| `0x152b49` | — | low | auto-shape | btaninit2-field? |
| `0x152b4b` | — | low | auto-shape | btaninit2-field? |
| `0x152b4c` | — | low | auto-shape | btaninit2-field? |
| `0x152b4d` | — | low | auto-shape | btaninit2-field? |
| `0x152b4e` | — | low | auto-shape | btaninit2-field? |
| `0x152b50` | — | low | auto-shape | btaninit2-field? |
| `0x152b51` | — | low | auto-shape | btaninit2-field? |
| `0x152b52` | — | low | auto-shape | btaninit2-field? |
| `0x152b53` | — | low | auto-shape | btaninit2-field? |
| `0x152b55` | — | low | auto-shape | btaninit2-field? |
| `0x152b56` | — | low | auto-shape | btaninit2-field? |
| `0x152b57` | — | low | auto-shape | btaninit2-field? |
| `0x152b58` | — | low | auto-shape | btaninit2-field? |
| `0x152b5a` | — | low | auto-shape | btaninit2-field? |
| `0x152b5b` | — | low | auto-shape | btaninit2-field? |
| `0x152b5c` | — | low | auto-shape | btaninit2-field? |
| `0x152b5d` | — | low | auto-shape | btaninit2-field? |
| `0x152b5f` | — | low | auto-shape | btaninit2-field? |
| `0x152b60` | — | low | auto-shape | btaninit2-field? |
| `0x152b61` | — | low | auto-shape | btaninit2-field? |
| `0x152b62` | — | low | auto-shape | btaninit2-field? |
| `0x152b64` | — | low | auto-shape | btaninit2-field? |
| `0x152b65` | — | low | auto-shape | btaninit2-field? |
| `0x152b66` | — | low | auto-shape | btaninit2-field? |
| `0x152b67` | — | low | auto-shape | btaninit2-field? |
| `0x152b69` | — | low | auto-shape | btaninit2-field? |
| `0x152b6a` | — | low | auto-shape | btaninit2-field? |
| `0x152b6b` | — | low | auto-shape | btaninit2-field? |
| `0x152b6c` | — | low | auto-shape | btaninit2-field? |
| `0x152b6e` | — | low | auto-shape | btaninit2-field? |
| `0x152b6f` | — | low | auto-shape | btaninit2-field? |
| `0x152b70` | — | low | auto-shape | btaninit2-field? |
| `0x152b71` | — | low | auto-shape | btaninit2-field? |
| `0x152b73` | — | low | auto-shape | btaninit2-field? |
| `0x152b74` | — | low | auto-shape | btaninit2-field? |
| `0x152b75` | — | low | auto-shape | btaninit2-field? |
| `0x152b76` | — | low | auto-shape | btaninit2-field? |
| `0x152b78` | — | low | auto-shape | btaninit2-field? |
| `0x154006` | — | low | auto-shape | btaninit2-field? |
| `0x154007` | — | low | auto-shape | btaninit2-field? |
| `0x154009` | — | low | auto-shape | btaninit2-field? |
| `0x15400c` | — | low | auto-shape | btaninit2-field? |
| `0x15400d` | — | low | auto-shape | btaninit2-field? |
| `0x15400f` | — | low | auto-shape | btaninit2-field? |
| `0x154012` | — | low | auto-shape | btaninit2-field? |
| `0x154013` | — | low | auto-shape | btaninit2-field? |
| `0x154015` | — | low | auto-shape | btaninit2-field? |
| `0x154018` | — | low | auto-shape | btaninit2-field? |
| `0x154019` | — | low | auto-shape | btaninit2-field? |
| `0x15401b` | — | low | auto-shape | btaninit2-field? |
| `0x15401e` | — | low | auto-shape | btaninit2-field? |
| `0x15401f` | — | low | auto-shape | btaninit2-field? |
| `0x154021` | — | low | auto-shape | btaninit2-field? |
| `0x154024` | — | low | auto-shape | btaninit2-field? |
| `0x154025` | — | low | auto-shape | btaninit2-field? |
| `0x154027` | — | low | auto-shape | btaninit2-field? |
| `0x15402a` | — | low | auto-shape | btaninit2-field? |
| `0x15402b` | — | low | auto-shape | btaninit2-field? |
| `0x15402d` | — | low | auto-shape | btaninit2-field? |
| `0x154030` | — | low | auto-shape | btaninit2-field? |
| `0x154031` | — | low | auto-shape | btaninit2-field? |
| `0x154033` | — | low | auto-shape | btaninit2-field? |
| `0x154036` | — | low | auto-shape | btaninit2-field? |
| `0x154037` | — | low | auto-shape | btaninit2-field? |
| `0x154039` | — | low | auto-shape | btaninit2-field? |
| `0x15403c` | — | low | auto-shape | btaninit2-field? |
| `0x15403d` | — | low | auto-shape | btaninit2-field? |
| `0x15403f` | — | low | auto-shape | btaninit2-field? |
| `0x154042` | — | low | auto-shape | btaninit2-field? |
| `0x154043` | — | low | auto-shape | btaninit2-field? |
| `0x154045` | — | low | auto-shape | btaninit2-field? |
| `0x154048` | — | low | auto-shape | btaninit2-field? |
| `0x154049` | — | low | auto-shape | btaninit2-field? |
| `0x15404b` | — | low | auto-shape | btaninit2-field? |
| `0x15404e` | — | low | auto-shape | btaninit2-field? |
| `0x15404f` | — | low | auto-shape | btaninit2-field? |
| `0x154051` | — | low | auto-shape | btaninit2-field? |
| `0x154054` | — | low | auto-shape | btaninit2-field? |
| `0x154055` | — | low | auto-shape | btaninit2-field? |
| `0x154057` | — | low | auto-shape | btaninit2-field? |
| `0x15405a` | — | low | auto-shape | btaninit2-field? |
| `0x15405b` | — | low | auto-shape | btaninit2-field? |
| `0x15405d` | — | low | auto-shape | btaninit2-field? |
| `0x154060` | — | low | auto-shape | btaninit2-field? |
| `0x154061` | — | low | auto-shape | btaninit2-field? |
| `0x154063` | — | low | auto-shape | btaninit2-field? |
| `0x154066` | — | low | auto-shape | btaninit2-field? |
| `0x154067` | — | low | auto-shape | btaninit2-field? |
| `0x154069` | — | low | auto-shape | btaninit2-field? |
| `0x15406c` | — | low | auto-shape | btaninit2-field? |
| `0x15406d` | — | low | auto-shape | btaninit2-field? |
| `0x15406f` | — | low | auto-shape | btaninit2-field? |
| `0x154072` | — | low | auto-shape | btaninit2-field? |
| `0x154073` | — | low | auto-shape | btaninit2-field? |
| `0x154075` | — | low | auto-shape | btaninit2-field? |
| `0x154078` | — | low | auto-shape | btaninit2-field? |
| `0x154079` | — | low | auto-shape | btaninit2-field? |
| `0x15407b` | — | low | auto-shape | btaninit2-field? |
| `0x15407e` | — | low | auto-shape | btaninit2-field? |
| `0x15407f` | — | low | auto-shape | btaninit2-field? |
| `0x154081` | — | low | auto-shape | btaninit2-field? |
| `0x15425e` | — | low | auto-shape | btaninit2-field? |
| `0x15425f` | — | low | auto-shape | btaninit2-field? |
| `0x154261` | — | low | auto-shape | btaninit2-field? |
| `0x154263` | — | low | auto-shape | btaninit2-field? |
| `0x154264` | — | low | auto-shape | btaninit2-field? |
| `0x154266` | — | low | auto-shape | btaninit2-field? |
| `0x154268` | — | low | auto-shape | btaninit2-field? |
| `0x154269` | — | low | auto-shape | btaninit2-field? |
| `0x15426b` | — | low | auto-shape | btaninit2-field? |
| `0x15426d` | — | low | auto-shape | btaninit2-field? |
| `0x15426e` | — | low | auto-shape | btaninit2-field? |
| `0x154270` | — | low | auto-shape | btaninit2-field? |
| `0x154272` | — | low | auto-shape | btaninit2-field? |
| `0x154273` | — | low | auto-shape | btaninit2-field? |
| `0x154275` | — | low | auto-shape | btaninit2-field? |
| `0x154277` | — | low | auto-shape | btaninit2-field? |
| `0x154278` | — | low | auto-shape | btaninit2-field? |
| `0x15427a` | — | low | auto-shape | btaninit2-field? |
| `0x15427c` | — | low | auto-shape | btaninit2-field? |
| `0x15427d` | — | low | auto-shape | btaninit2-field? |
| `0x15427f` | — | low | auto-shape | btaninit2-field? |
| `0x154281` | — | low | auto-shape | btaninit2-field? |
| `0x154282` | — | low | auto-shape | btaninit2-field? |
| `0x154284` | — | low | auto-shape | btaninit2-field? |
| `0x154286` | — | low | auto-shape | btaninit2-field? |
| `0x154287` | — | low | auto-shape | btaninit2-field? |
| `0x154289` | — | low | auto-shape | btaninit2-field? |
| `0x15428b` | — | low | auto-shape | btaninit2-field? |
| `0x15428c` | — | low | auto-shape | btaninit2-field? |
| `0x15428e` | — | low | auto-shape | btaninit2-field? |
| `0x154290` | — | low | auto-shape | btaninit2-field? |
| `0x154291` | — | low | auto-shape | btaninit2-field? |
| `0x154293` | — | low | auto-shape | btaninit2-field? |
| `0x154295` | — | low | auto-shape | btaninit2-field? |
| `0x154296` | — | low | auto-shape | btaninit2-field? |
| `0x154298` | — | low | auto-shape | btaninit2-field? |
| `0x15429a` | — | low | auto-shape | btaninit2-field? |
| `0x15429b` | — | low | auto-shape | btaninit2-field? |
| `0x15429d` | — | low | auto-shape | btaninit2-field? |
| `0x15429f` | — | low | auto-shape | btaninit2-field? |
| `0x1542a0` | — | low | auto-shape | btaninit2-field? |
| `0x1542a2` | — | low | auto-shape | btaninit2-field? |
| `0x1542a4` | — | low | auto-shape | btaninit2-field? |
| `0x1542a5` | — | low | auto-shape | btaninit2-field? |
| `0x1542a7` | — | low | auto-shape | btaninit2-field? |
| `0x1542a9` | — | low | auto-shape | btaninit2-field? |
| `0x1542aa` | — | low | auto-shape | btaninit2-field? |
| `0x1542ac` | — | low | auto-shape | btaninit2-field? |
| `0x1542ae` | — | low | auto-shape | btaninit2-field? |
| `0x1542af` | — | low | auto-shape | btaninit2-field? |
| `0x1542b1` | — | low | auto-shape | btaninit2-field? |
| `0x1542b3` | — | low | auto-shape | btaninit2-field? |
| `0x1542b4` | — | low | auto-shape | btaninit2-field? |
| `0x1542b6` | — | low | auto-shape | btaninit2-field? |
| `0x1542b8` | — | low | auto-shape | btaninit2-field? |
| `0x1542b9` | — | low | auto-shape | btaninit2-field? |
| `0x1542bb` | — | low | auto-shape | btaninit2-field? |
| `0x1542bd` | — | low | auto-shape | btaninit2-field? |
| `0x1542be` | — | low | auto-shape | btaninit2-field? |
| `0x1542c0` | — | low | auto-shape | btaninit2-field? |
| `0x1542c2` | — | low | auto-shape | btaninit2-field? |
| `0x1542c3` | — | low | auto-shape | btaninit2-field? |
| `0x1542c5` | — | low | auto-shape | btaninit2-field? |
| `0x1542c7` | — | low | auto-shape | btaninit2-field? |
| `0x1542c8` | — | low | auto-shape | btaninit2-field? |
| `0x1542ca` | — | low | auto-shape | btaninit2-field? |
| `0x1542cc` | — | low | auto-shape | btaninit2-field? |
| `0x1542cd` | — | low | auto-shape | btaninit2-field? |
| `0x1542cf` | — | low | auto-shape | btaninit2-field? |
| `0x1542d1` | — | low | auto-shape | btaninit2-field? |
| `0x1542d2` | — | low | auto-shape | btaninit2-field? |
| `0x1542d4` | — | low | auto-shape | btaninit2-field? |
| `0x1542d6` | — | low | auto-shape | btaninit2-field? |
| `0x1542d7` | — | low | auto-shape | btaninit2-field? |
| `0x1542d9` | — | low | auto-shape | btaninit2-field? |
| `0x1542db` | — | low | auto-shape | btaninit2-field? |
| `0x1542dc` | — | low | auto-shape | btaninit2-field? |
| `0x1542de` | — | low | auto-shape | btaninit2-field? |
| `0x1542e0` | — | low | auto-shape | btaninit2-field? |
| `0x1542e1` | — | low | auto-shape | btaninit2-field? |
| `0x1542e3` | — | low | auto-shape | btaninit2-field? |
| `0x1542e5` | — | low | auto-shape | btaninit2-field? |
| `0x1542e6` | — | low | auto-shape | btaninit2-field? |
| `0x1542e8` | — | low | auto-shape | btaninit2-field? |
| `0x15576f` | — | low | auto-shape | btaninit2-field? |
| `0x155770` | — | low | auto-shape | btaninit2-field? |
| `0x155771` | — | low | auto-shape | btaninit2-field? |
| `0x155772` | — | low | auto-shape | btaninit2-field? |
| `0x155773` | — | low | auto-shape | btaninit2-field? |
| `0x155774` | — | low | auto-shape | btaninit2-field? |
| `0x155775` | — | low | auto-shape | btaninit2-field? |
| `0x155776` | — | low | auto-shape | btaninit2-field? |
| `0x155777` | — | low | auto-shape | btaninit2-field? |
| `0x155778` | — | low | auto-shape | btaninit2-field? |
| `0x155779` | — | low | auto-shape | btaninit2-field? |
| `0x15577a` | — | low | auto-shape | btaninit2-field? |
| `0x15577b` | — | low | auto-shape | btaninit2-field? |
| `0x15577c` | — | low | auto-shape | btaninit2-field? |
| `0x15577d` | — | low | auto-shape | btaninit2-field? |
| `0x15577e` | — | low | auto-shape | btaninit2-field? |
| `0x15577f` | — | low | auto-shape | btaninit2-field? |
| `0x155780` | — | low | auto-shape | btaninit2-field? |
| `0x155781` | — | low | auto-shape | btaninit2-field? |
| `0x155782` | — | low | auto-shape | btaninit2-field? |
| `0x155783` | — | low | auto-shape | btaninit2-field? |
| `0x1557d3` | — | med | auto-shape | btaninit2-field |
| `0x1560e8` | — | med | auto-shape | mainit-field |
| `0x156106` | — | med | auto-shape | mainit-field |
| `0x156124` | — | med | auto-shape | mainit-field |
| `0x156142` | — | med | auto-shape | mainit-field |
| `0x156160` | — | med | auto-shape | mainit-field |
| `0x15617e` | — | low | auto-shape | mainit-field? |
| `0x1561d8` | — | low | auto-shape | mainit-field? |
| `0x15a75b` | — | med | auto-shape | record-table[stride 21] |
| `0x15a785` | — | med | auto-shape | record-table[stride 3] |

