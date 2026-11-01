<!-- DO NOT EDIT -- generated from vm-map/globals-kamidori.toml by tools/globals_build.py --build -->
# Global Variable Reference — kamidori (generated)

8298 globals (624 curated, 7674 auto shape-inferred). Source of truth: `vm-map/globals-kamidori.toml`.

## Tables

Open Maid Script tables (design spec §5.3): `table[record].field` is the field's array element.

### unit[1000]

EBINIT unit records keyed by unit id, with the per-unit runtime arrays (history face, voice family and setting slot). `unit[761].stats.max_hp` is `unit_base_stats[761].max_hp`.

| field | global | address | type |
|---|---|---|---|
| speaker_name | unit_speaker_names | `GS 0x322` | `string[1000][5]` |
| full_name | unit_full_names | `GS 0x2ab7` | `string[1000]` |
| display_name | unit_display_names | `GS 0x2e9f` | `string[1000]` |
| initial_class_title | unit_initial_class_titles | `GS 0x3287` | `string[1000]` |
| bestiary_description | unit_bestiary_description_lines | `GS 0x366f` | `string[1000][3]` |
| history_face_asset_id | unit_history_face_asset_ids | `0xfd49c` | `int[1000]` |
| voice_family_unit_id | unit_voice_family_unit_ids | `0xfdc6c` | `int[1000]` |
| voice_setting_slot | unit_voice_setting_slot | `0xfe054` | `int[1000]` |
| voice | unit_voice_asset_ids | `0x2a9b8c` | `int[1000][30]` |
| graphic | unit_graphic_asset_ids | `0x2b10bc` | `int[1000][17]` |
| sort_key | unit_sort_key | `0x2b570c` | `int[1000]` |
| bestiary_listed | unit_bestiary_listed | `0x2b5af4` | `int[1000]` |
| race | unit_race | `0x2b5edc` | `int[1000]` |
| sex | unit_sex | `0x2b62c4` | `int[1000]` |
| boss_class | unit_boss_class | `0x2b66ac` | `int[1000]` |
| canonical_character_id | unit_canonical_character_id | `0x2b6a94` | `int[1000]` |
| cutin_element | unit_cutin_element | `0x2b6e7c` | `int[1000]` |
| element_resistance | unit_element_resistance_levels | `0x2b7264` | `int[1000][8]` |
| default_attack_item_id | unit_default_attack_item_id | `0x2b91a4` | `int[1000]` |
| initial_weapon_level | unit_initial_weapon_level | `0x2b958c` | `int[1000]` |
| equippable_category_mask | unit_equippable_category_mask | `0x2b9974` | `int[1000]` |
| initial_equipment | unit_initial_equipment_item_ids | `0x2b9d5c` | `int[1000][3]` |
| default_costume_item_id | unit_default_costume_item_id | `0x2ba914` | `int[1000]` |
| skill | unit_skill_ids | `0x2bacfc` | `int[1000][8]` |
| initial_party_capacity | unit_initial_party_capacity | `0x2bcc3c` | `int[1000]` |
| unused_0x2bd024 | unit_unused_0x2bd024 | `0x2bd024` | `int[1000]` |
| party_cost | unit_party_cost | `0x2bd40c` | `int[1000]` |
| initial_affinity_tenths | unit_initial_affinity_tenths | `0x2bd7f4` | `int[1000]` |
| starting_level | unit_starting_level | `0x2bdbdc` | `int[1000]` |
| level_cap | unit_level_cap | `0x2bdfc4` | `int[1000]` |
| auto_level_scale_divisor | unit_auto_level_scale_divisor | `0x2be3ac` | `int[1000]` |
| experience_level_cap | unit_experience_level_cap | `0x2be794` | `int[1000]` |
| stats | unit_base_stats | `0x2beb7c` | `int[1000][14]` |
| stat_growth | unit_stat_growth_rates | `0x2c222c` | `int[1000][14]` |
| initial_stat_caps | unit_initial_stat_caps | `0x2c58dc` | `int[1000][14]` |
| experience_reward | unit_experience_reward | `0x2c8f8c` | `int[1000]` |
| bestiary_knowledge_gain | unit_bestiary_knowledge_gain | `0x2c9374` | `int[1000]` |
| drop_item | unit_drop_item_ids | `0x2c975c` | `int[1000][5]` |
| drop_chance | unit_drop_chance_percent | `0x2caae4` | `int[1000][5]` |
| drop_knowledge_threshold | unit_drop_knowledge_threshold | `0x2cbe6c` | `int[1000][5]` |
| drop_required_flag | unit_drop_required_flag_ids | `0x2cd1f4` | `int[1000][10]` |
| drop_excluding_flag | unit_drop_excluding_flag_ids | `0x2cf904` | `int[1000][5]` |
| large_battle_sprite | unit_large_battle_sprite | `0x2d0c8c` | `int[1000]` |
| habitat_area | unit_habitat_area_ids | `0x2d1074` | `int[1000][4]` |

### item[2000]

ITINIT item records keyed by item id, with the per-item inventory and growth state.

| field | global | address | type |
|---|---|---|---|
| name | item_names | `GS 0x4228` | `string[2000]` |
| short_description | item_short_descriptions | `GS 0x49f8` | `string[2000]` |
| inventory_count | item_inventory_counts | `0x7ea4f` | `int[2000]` |
| growth_exp | item_growth_exp | `0x801bf` | `int[2000]` |
| growth_level | item_growth_levels | `0x8098f` | `int[2000]` |
| costume_enhancement_grid | item_costume_enhancement_grid | `0x82100` | `int[2000][225]` |
| costume_gem_sockets | item_costume_gem_sockets | `0xefed0` | `int[2000][4]` |
| handler_script_id | item_handler_script_id | `0x2d2848` | `int[2000]` |
| costume_graphic_asset_ids | item_costume_graphic_asset_ids | `0x2d3018` | `int[2000][17]` |
| icon_asset_id | item_icon_asset_id | `0x2db4e8` | `int[2000]` |
| furniture_sprite_asset_id | item_furniture_sprite_asset_id | `0x2dbcb8` | `int[2000]` |
| sort_key | item_sort_key | `0x2dc488` | `int[2000]` |
| rank | item_rank | `0x2dcc58` | `int[2000]` |
| category | item_category | `0x2dd428` | `int[2000]` |
| min_range | item_min_range | `0x2ddbf8` | `int[2000]` |
| max_range | item_max_range | `0x2de3c8` | `int[2000]` |
| attack_element | item_attack_element | `0x2deb98` | `int[2000]` |
| costume_battle_variant | item_costume_battle_variant | `0x2df368` | `int[2000]` |
| affinity_level_deltas | item_affinity_level_deltas | `0x2dfb38` | `int[2000][8]` |
| character_whitelist | item_character_whitelist | `0x2e39b8` | `int[2000][5]` |
| species_restriction_mask | item_species_restriction_mask | `0x2e60c8` | `int[2000]` |
| sex_restriction_mask | item_sex_restriction_mask | `0x2e6898` | `int[2000]` |
| cursed_flag | item_cursed_flag | `0x2e7068` | `int[2000]` |
| condition_level_deltas | item_condition_level_deltas | `0x2e7838` | `int[2000][30]` |
| equipped_condition_levels | item_equipped_condition_levels | `0x2f6298` | `int[2000][30]` |
| granted_skill_id | item_granted_skill_id | `0x304cf8` | `int[2000]` |
| stat_modifiers | item_stat_modifiers | `0x3054c8` | `int[2000][14]` |
| critical_rate_bonus | item_critical_rate_bonus | `0x30c228` | `int[2000]` |
| resource_recovery_amounts | item_resource_recovery_amounts | `0x30c9f8` | `int[2000][3]` |
| recovery_variance | item_recovery_variance | `0x30e168` | `int[2000]` |
| battle_animation_id | item_battle_animation_id | `0x30e938` | `int[2000]` |
| price | item_prices | `0x30f108` | `int[2000][3]` |
| furniture_area | item_furniture_area | `0x310878` | `int[2000]` |
| furniture_exclusive_group | item_furniture_exclusive_group | `0x311048` | `int[2000]` |
| furniture_width | item_furniture_width | `0x311818` | `int[2000]` |
| furniture_height | item_furniture_height | `0x311fe8` | `int[2000]` |
| garden_production_period | item_garden_production_period | `0x3127b8` | `int[2000]` |
| garden_product_item_id | item_garden_product_item_id | `0x312f88` | `int[2000]` |
| furniture_sales_count_bonus | item_furniture_sales_count_bonus | `0x313758` | `int[2000]` |
| furniture_purchase_desire_bonus | item_furniture_purchase_desire_bonus | `0x313f28` | `int[2000]` |
| furniture_visit_rate_bonus | item_furniture_visit_rate_bonus | `0x3146f8` | `int[2000]` |
| furniture_high_price_bonus | item_furniture_high_price_bonus | `0x314ec8` | `int[2000]` |
| furniture_equipment_bonus | item_furniture_equipment_bonus | `0x315698` | `int[2000]` |
| furniture_information_bonus | item_furniture_information_bonus | `0x315e68` | `int[2000]` |
| furniture_mutation_bonus | item_furniture_mutation_bonus | `0x316638` | `int[2000]` |
| furniture_knowledge_bonus | item_furniture_knowledge_bonus | `0x316e08` | `int[2000]` |
| furniture_staff_count_bonus | item_furniture_staff_count_bonus | `0x3175d8` | `int[2000]` |
| furniture_garden_bonus | item_furniture_garden_bonus | `0x317da8` | `int[2000]` |
| field_use_target_kind | item_field_use_target_kind | `0x318578` | `int[2000]` |
| usability_flag | item_usability_flags | `0x319ce8` | `int[2000]` |
| growth_enabled | item_growth_enabled | `0x31a4b9` | `int[2000]` |
| growth_mastery_reward_items | item_growth_mastery_reward_items | `0x38b939` | `int[2000]` |
| growth_mastery_story_flag | item_growth_mastery_story_flags | `0x38c109` | `int[2000]` |

### skill[1000]

SKINIT skill records keyed by skill id.

| field | global | address | type |
|---|---|---|---|
| name | skill_names | `GS 0x5998` | `string[1000]` |
| short_description | skill_short_descriptions | `GS 0x5d80` | `string[1000]` |
| icon_asset_id | skill_icon_asset_id | `0x3a10c8` | `int[1000]` |
| sort_key | skill_sort_key | `0x3a14b0` | `int[1000]` |
| upgrade_family_id | skill_upgrade_family_id | `0x3a1898` | `int[1000]` |
| category | skill_category | `0x3a1c80` | `int[1000]` |
| catalog_visible | skill_catalog_visible | `0x3a2068` | `int[1000]` |
| equip_cost | skill_equip_cost | `0x3a2450` | `int[1000]` |
| bundled_skill_ids | skill_bundled_skill_ids | `0x3a2838` | `int[1000][5]` |
| min_range_encoded | skill_min_range_encoded | `0x3a3bc0` | `int[1000]` |
| max_range_encoded | skill_max_range_encoded | `0x3a3fa8` | `int[1000]` |
| required_weapon_category_mask | skill_required_weapon_category_mask | `0x3a4390` | `int[1000]` |
| area_target_mode | skill_area_target_mode | `0x3a4778` | `int[1000]` |
| area_shape_id | skill_area_shape_id | `0x3a4b60` | `int[1000]` |
| area_cooldown_turns | skill_area_cooldown_turns | `0x3a4f48` | `int[1000]` |
| attack_element_encoded | skill_attack_element_encoded | `0x3a5330` | `int[1000]` |
| condition_levels | skill_condition_levels | `0x3a5718` | `int[1000][30]` |
| combat_stat_deltas | skill_combat_stat_deltas | `0x3acc48` | `int[1000][10]` |
| critical_rate_bonus | skill_critical_rate_bonus | `0x3af358` | `int[1000]` |
| resource_deltas | skill_resource_deltas | `0x3af740` | `int[1000][3]` |
| proc_chance_percent | skill_proc_chance_percent | `0x3b02f8` | `int[1000]` |
| battle_animation_id | skill_battle_animation_id | `0x3b06e0` | `int[1000]` |
| combo_partner_character_ids | skill_combo_partner_character_ids | `0x3b0ac8` | `int[1000][2]` |
| handler_script_id | skill_handler_script_id | `0x3b1298` | `int[1000]` |
| area_effect_texture_asset_id | skill_area_effect_texture_asset_id | `0x3b1680` | `int[1000]` |
| area_effect_sound_asset_id | skill_area_effect_sound_asset_id | `0x3b1a68` | `int[1000]` |
| area_effect_blend_mode | skill_area_effect_blend_mode | `0x3b1e50` | `int[1000]` |

### stage_enemy[30]

The current stage's enemy placements, keyed by placement slot: STINIT2 writes them and FIELD spawns from them.

| field | global | address | type |
|---|---|---|---|
| spawn_tile_x | stage_enemy_spawn_tile_x | `0x14095a` | `int[30]` |
| spawn_tile_y | stage_enemy_spawn_tile_y | `0x140978` | `int[30]` |
| spawn_object_slot | stage_enemy_spawn_object_slot | `0x140996` | `int[30]` |
| faction_id | stage_enemy_faction_id | `0x1409b4` | `int[30]` |
| difficulty_mask | stage_enemy_difficulty_mask | `0x1409d2` | `int[30]` |
| first_visit_only | stage_enemy_first_visit_only | `0x1409f0` | `int[30]` |
| random_selection_weight | stage_enemy_random_selection_weight | `0x140a0e` | `int[30]` |
| unit_id | stage_enemy_unit_id | `0x140a2c` | `int[30]` |
| min_level | stage_enemy_min_level | `0x140a4a` | `int[30]` |
| max_level | stage_enemy_max_level | `0x140a68` | `int[30]` |
| auto_level_scale_divisor | stage_enemy_auto_level_scale_divisor | `0x140a86` | `int[30]` |

## choice-output

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x2c81` | scjump_chain_pending | med | investigation | Callers set 1 before walking scjump_handler_chain (CHECKREWARD@0x29a/0x2b8) and stop when a SCJUMP branch clears it alongside writing scjump_decision_out. |
| `GS 0x4227` | class_change_title_out | high | investigation | CALCCC@0x00319 copies it into character_class_titles[c]. |
| `0xfec10` | scjump_decision_out | high | investigation | Selected scene id. Written by SCJUMP branches (e.g. SCJUMP@0x118), TOWN@0xad7, STUDIO@0xa1e and GAMESTART@0x1ad5; SYSTEM4@0x2dc resolves it through scjump_scene_script_resource_ids and SYSTEM4@0x329 marks viewed_scene_flags; multi-scene SC scripts branch on it (e.g. SC0010@0x985). |
| `0x13e872` | world_area_selected_list_index | high | investigation | ALLMAP@0xc58 sets it from the clicked marker; SELSTAGE@0x3a6/0x3b7 cycles it with the wheel; ALLMAP@0xc6b resolves it to the area id. |
| `0x2d2014` | class_change_title_priority | high | investigation | Zeroed by CALCCC; CCINIT keeps the title of the highest-threshold rule; nonzero makes ADDEXP@0x00419 / scenes call SHOWCC. |
| `0x2d2015` | class_change_party_capacity_delta | high | investigation | CALCCC@0x000a1 adds to party_cost_capacity 0x7ea3b. |
| `0x2d2016` | class_change_party_cost_delta | high | investigation | CALCCC@0x000b6 adds to character_party_cost 0x6577d[c]. |
| `0x2d2017` | class_change_resistance_deltas | high | investigation | CALCCC@0x001df adds to character_resistances 0x643f5[c][k]. |
| `0x2d201f` | class_change_stat_deltas | high | investigation | CALCCC@0x000f0 adds to character_stats (14-col layout of unit_base_stats). |
| `0x2d202d` | class_change_stat_cap_deltas | high | investigation | CALCCC@0x00114 adds to character_stat_caps, clamped to the global cap 0x2a2da7[0]. |
| `0x2d203b` | class_change_skill_slot_replacements | high | investigation | CALCCC@0x00223 overwrites character_skill_list[c][k] when >0. |
| `0x2d2043` | class_change_skill_learn_flags | high | investigation | CALCCC@0x0026f sets character_learned_skill_flags[c][skill]=1 (and the persistent skill-seen bank 0xf6483 when 0x64201 bit1). |

## counter

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x360f` | world_map_scroll_x | high | investigation | ALLMAP@0x61 centre on area, ALLMAP@0xe2d clamp, drag/joy scroll at ALLMAP@0x793/0x9f7. |
| `0x3610` | world_map_scroll_y | high | investigation | Pair of world_map_scroll_x (ALLMAP@0x6f, 0xe5f, 0x955). |
| `0x3613` | world_area_state_flags | high | investigation | AMINIT@0x1d masks every cell to bit0 then writes 3 per area when its flag test passes. ALLMAP@0xd36 lists areas with bit0 (and x!=0); ALLMAP@0x11e7 greys markers without bit1; SELSTAGE@0xa68 refuses entry (error SE) without bit1; SELSTAGE@0x611 bit1 gates the per-area event scan; INFO3@0x1af3 / INFO5@0x1053 grey undiscovered names. FIELD@0x299 sets bit0 for marker-less areas on entry; ALLMAP@0x49 (render-only path used by SHOWALLMAP) clears the shown area so it can be drawn as a 'new' highlight. |
| `0x3697` | expedition_elapsed_days | high | investigation | Incremented by world_area_travel_days (SELSTAGE@0xc00, TOWN@0x7b, FIELD@0x271); ALLMAP@0x3b4 >= 30 shows '経過日数が30日を超えました／工房で清算を行ってください' and returns home; ALLMAP@0x13d1 draws it; CALCSETTLE/DRAWSETTLE/CHECKREWARD consume it. Also observed: [progression decode, named expedition_elapsed_days] Days elapsed since leaving the studio. CHECKREWARD@0xb0 adds it to every active quest's quest_elapsed_days, settles, then clears it (CHECKREWARD@0x2ed); CHECKCONTRACT@0x1ab seeds a newly posted quest's elapsed days with its negation so the pending days are not charged; INFO2@0x10bf adds it to show live elapsed days; ALLMAP@0x3b4 tests >= 30. |
| `0x523d` | stage_occupation_percent | high | investigation | FIELD@0xc7ac/0xc807 store floor_occupation_percent_by_faction[1]; SELSTAGE@0x104a prints it with '%'. FIELD@0x756: value 100 reveals and claims every non-void tile on entry; FIELD@0xc813: reaching 100 sets stage_objective_reward_state bit 0 of objective 0. |
| `0x37305` | stage_object_use_counts | high | investigation | CHECKOBJ@0x1f and SETOBJ@0x184 compare it with object_type_use_limit; FIELD@0x2eaf increments it on use; FIELD@0x5b0 resets it on map entry for object_type_use_count_resets_on_entry types. |
| `0x4fde0` | floor_occupation_percent_by_faction | high | investigation | CALCOCC@0x39e resets it to [100,0,0,0,0], counts tile_owner_faction_mask bits over all tiles with nonzero visibility class into 0x2a2d5e[5], then sets [k] = 100*count/total and subtracts from [0]; rounding remainder is redistributed. FIELD@0xc7ac/0xc807 stores [1] (player) into stage_occupation_percent. |
| `0x50ec2` | room_count | high | investigation | SETROOM@0xa4 increments per flood-filled room; loop bound in DRAWMAP@0x1d1, SETHALLTEX@0x19 and CALCOCC@0xae. |
| `0x5340b` | dig_pending_gather_counts | high | investigation | FIELD@0x899f adds the object's quantity (0x140540) when a gathering point is used ('素材N個を採取袋に入れた'); DIG@0x1d rolls that many times then clears all 200 cells (DIG@0x195). |
| `0x5385e` | arena_battle_win_counts | high | investigation | STAGECLEAR@0xe94 increments the current battle; ARENA@0x973 sums it for required_total_wins, @0xb51 prerequisite test, @0x746/0x818 fee only while 0, @0x1372 'cleared' badge. |
| `0x545da` | battle_unit_current_pools | high | investigation | Initialised from stat cols 10..12 (SETEN@0x006a8, SETCH@0x00345); COMMITBTL@0x0000a treats hp==0 as defeated. Columns: 0=hp, 1=sp, 2=fs. |
| `0x64265` | character_affinity | med | investigation | UNITECH seeds value*10; COMMITBTL@0x0017a/0x00230 adds 0x3b2b82/0x3b2b7f per battle outcome (clamped 0..1000); SETCH@0x00059 tiers select the cost multiplier; FELLOWSTATUS@0x01549 draws it. Name 'affinity' is inferred (bond-like 0..1000 meter). |
| `0x6432d` | character_levels | high | investigation | UNITECH@0x0012c (first join when 0), ADDEXP@0x003a8 (+1), SETCH@0x001a1 -> battle level, CCINIT level gates. |
| `0x64391` | character_experience | high | investigation | ADDEXP@0x001ae accumulates, levels at 100 and resets to 0; drawn as a 100-tick gauge (ADDEXP@0x006b7). |
| `0x7e1a1` | character_weapon_level | high | investigation | See unit_initial_weapon_level. |
| `0x7e205` | character_weapon_experience | high | investigation | ADDEXP@0x0048b adds level+random per battle and levels weapon LV at thresholds 0x3b2b60. |
| `0x7ea39` | player_money | high | investigation | Player money (所持金). RECEIPT@0x729 adds quest_reward_money on success; FIELD@0x8626 adds a money card's rolled amount; CHECKREWARD@0x247 counts consecutive negative-balance settlements; GAMESTART@0x883 inherits it on new-game option bit3 (GSMES 11). Also observed: [items decode, named player_money] ADDITEM@0x00088, USEITEM@0x0020d, SHOP@0x014b3 and CHANGEARM@0x00103 add sale proceeds through 0xfec0c and clamp to 0x98967f. [alchemy decode, named economy_money] ARRANGE@0xcfe checks it against studio_extension_costs (増築費用が不足しています) and @0xe11 deducts; SALE@0xd93 adds sale proceeds; DRAWSTUDIOLV@0x27d and INFO1@0xd40 draw it. |
| `0x7ea3a` | player_evaluation_points | high | investigation | Player reputation (評価). RECEIPT@0x678/0x6d6 adds quest_reputation_rewards[quest][success?0:1] with +/-9,999,999 clamps; DRAWSTUDIOLV@0x19c draws it under the rank title; SCJUMP gates story branches on thresholds (e.g. SCJUMP@0x3d28 >= 4300); GAMESTART@0x844 inherits it from the shared clear profile when new-game option bit0 is chosen (GSMES 5: 評価をクリアデータから引継ぎます). Also observed: [alchemy decode, named studio_evaluation] ALCHEMY@0xc00/0xcac adds recipe and rank-up evaluation (評価＋N); DRAWSTUDIOLV@0x19c and INFO1@0xcd3 draw it; also referenced by GAMECLEAR, RECEIPT, SAVE and late SC59xx scenes. |
| `0x7ea3b` | party_cost_capacity | high | investigation | Seeded by unit_initial_party_capacity; CALCCC adds 0x2d2015; ORGANIZE@0x01971 compares the summed member cost against it and draws both numbers. |
| `0x7ea3f` | alchemy_synthesis_exp | high | investigation | ALCHEMY@0xa94/0xb31 adds (rank*5 + alchemy_item_rank_exp[0x2dcc58[item]]) * (100 + studio_synthesis_exp_bonus)/100 per crafted unit (/200 for mutation items); DRAWSTUDIOLV@0xd8 draws the progress bar. |
| `0x7ea49` | protagonist_craftsman_rank | high | investigation | CALCCC@0x00333 sets it to the highest applied rule column of character 1. Also observed: [alchemy decode, named studio_craftsman_rank] Gates recipes (CHECKRECIPE@0x5c), studio extensions (ARRANGE@0xcb9 工匠ランクが不足しています) and shop stock (SHINIT gre tests); drawn by DRAWSTUDIOLV@0x172 and INFO1@0xca9. |
| `0x7ea4a` | alchemy_synthesis_rank | high | investigation | ALCHEMY@0xc3e..0xc8e raises it while alchemy_synthesis_exp >= alchemy_synthesis_rank_exp_thresholds[rank+1] (max 6) and prints 合成ランクがXに上昇！; compared by CHECKRECIPE@0x6b and ALCHEMY@0x16be; drawn by DRAWSTUDIOLV@0x26d and INFO1@0xd30. |
| `0x7ea4b` | studio_room_levels | high | investigation | ARRANGE@0x42a/0x440/0x458/0x462 builds index room*20+level into the BUINIT tables; SETSTUDIO@0x0 maps each level to the room's studio parameters; CHECKRECIPE@0x81..0xdf compares level+1 with the recipe room-level requirements; DRAWSTUDIOLV@0x1a9 draws level+1/max. Columns: 0=shop, 1=workshop, 2=quarters, 3=room4. |
| `0x801bf` | item_growth_exp | high | investigation | CHECKIMPROVE@0x29 compares, @0x5a resets; COMMITBTL@0x4a6 adds 1 per battle; DRAWTOOLTIP@0x410 draws exp*50/threshold as a gauge. |
| `0x8098f` | item_growth_levels | high | investigation | CHECKIMPROVE@0x45 increments it and prints 『X』の性能が上がった; CALCARM@0x316 bounds the bonus sum; CHECKRECIPE@0x13b. Also observed: [items decode, named item_upgrade_step_counts] CALCARM@0x00316 loops this many steps and adds 0x320279[item][step*8+k] to the affinity totals and 0x347379[item][step*14+k] to the stat totals. ENHANCE presumably writes the steps. |
| `0x8192f` | alchemy_recipe_craft_counts | high | investigation | ALCHEMY@0xb91 selects first-craft evaluation, @0xbf1 adds the batch count; ALCHEMY@0x1d38 shows the 'new' list badge while 0. |
| `0xf29db` | bestiary_knowledge_percent | med | investigation | COMMITBTL@0x00500 adds unit_bestiary_knowledge_gain per defeat; gates drop slots via unit_drop_knowledge_threshold (COMMITBTL@0x0063d, INFO5@0x0122a). |
| `0xf2dc3` | bestiary_defeat_counts | high | investigation | COMMITBTL@0x0055a increments (cap 999); INFO5@0x013f8 draws it. |
| `0xf35a1` | quest_notification_bits | med | investigation | CHECKCONTRACT sets bits for quest ids < 1000 (@0x1cc/0x2b6/0x25e). INFO2@0x118 shows 新たなクエストが登録されました via MES when bit0 is set, then clears the word. |
| `0x13e871` | world_area_visible_count | high | investigation | ALLMAP@0xd14 resets/increments it; loops at ALLMAP@0xb7, 0x1128 and modulo cycling at ALLMAP@0xaf6, SELSTAGE@0x3a6. |
| `0x13f86c` | studio_shop_shelf_capacity | high | investigation | SETSTUDIO@0x1b3 clamps each of the 20 shelf stocks 0x3713 to it and returns the excess 0x36ff items via ADDITEM; SALE@0x995 compares stock with it. |
| `0x13f86d` | studio_shop_purchase_rate | med | investigation | CALCSETTLE@0x40a scales the per-item purchase threshold by it /100. |
| `0x13f86e` | studio_shop_visitor_rate | med | investigation | CALCSETTLE@0x210 computes 来客数 = base * factor * rate / 100 (min 1); the debug comment 購入判定 来客数= follows immediately. |
| `0x13f86f` | studio_shop_price_rate | med | investigation | CALCSETTLE@0x4e9 revenue per sold item; SALE@0xf65/0x14d6 price display/proceeds. |
| `0x13f870` | studio_material_saving | high | investigation | ALCHEMY@0x104f/0x11a1/0x170c consume max(1, qty*(99 - value)/100 + 1). |
| `0x13f871` | studio_synthesis_exp_bonus | high | investigation | ALCHEMY@0xa71/0xb0e multiply exp by (100 + value). |
| `0x13f872` | studio_mutation_rate | high | investigation | ALCHEMY@0x99c. |
| `0x13f873` | studio_knowledge | high | investigation | CHECKRECIPE@0xfc compares alchemy_recipe_unlock_knowledge. |
| `0x13f874` | studio_resident_capacity | med | investigation | FELLOW@0x18f4 allows stationing while count 0x3c5d42 < value; SETSTUDIOCH@0x24c compares the stationed-character count 0x13f87c[0] against it. |
| `0x13f876` | studio_sense | high | investigation | CHECKRECIPE@0x112 compares alchemy_recipe_unlock_sense. |
| `0x2a2d5e` | floor_tile_count_by_faction | med | investigation | Scratch accumulator of CALCOCC@0x3a3..0x459 used to compute floor_occupation_percent_by_faction. |

## data-table

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x50d` | story_event_flags | high | investigation | Shared one-based story/event flag bank (Himegari analog story_event_flags@0x6d3). CHECKCONTRACT@0x131/0x18b/0x21d/0x32c, INFO1@0x7da/0x834/0x89e, FIELD@0x8a81 (cards) all subtract one from an authored flag id and index this base; scene scripts set cells directly (e.g. SC0000@0x14c8e sets flag 8112 = G[0x24bc]). Flag 10000 is set only by DEBUGAM@0xaf, so data rows gated on it are disabled. |
| `0x3679` | town_location_event_flags | med | investigation | Set by SCJUMP@0x6103, tested by TOWN@0xab3. |
| `0x369b` | studio_room_ornaments | high | investigation | SETSTUDIO@0x95..0x180 adds each placed item's per-item bonus arrays (ITINIT-authored 0x313758, 0x313f28, 0x3146f8, 0x314ec8 for room 0; 0x315698, 0x315e68, 0x316638 room 1; 0x316e08, 0x3175d8 room 2; 0x317da8 room 3) onto the studio parameters; ARRANGE@0x983/0xa8d places/removes items. |
| `0x378e` | floor_terrain_template | high | investigation | Pristine authored floor. MPINIT@0x0 zeroes 53*53 cells from here, then the matching block copies 2*height rows of 2*width cells to 0x378e+y*53+1 (y=1..2H, e.g. MPINIT@0x3c dest 0x37c4 = row 1 col 1). FIELD@0x69e copy-dwords it into current_floor_terrain_grid on fresh entry; RESETLAND@0x73 restores a rectangle from it. 172 maps total: 145 base, 11 $1$, 2 $2$, 14 $3$. |
| `0x4fde5` | current_floor_terrain_grid | high | investigation | Live map. FIELD@0x69e copies floor_terrain_template here; read at [2ty][2tx] for tiles and at odd offsets for edges by DRAWMAP@0x2c1/0x3c1/0x433, DRAWMINIMAP@0x97, SETROOM@0x84/0x3a8, MVSEEK@0x57 (copied into a local work grid), CALCOCC@0x3f0, CALCSUPPORT, BTL, STATUS. SETLAND@0x57 writes a rectangle to set_land_terrain_id (digging/opening), DELLAND@0x73 zeroes a rectangle plus its border edges, RESETLAND@0x83 restores template cells. |
| `0x508de` | tile_owner_faction_mask | high | investigation | Territory control. FIELD@0x68b zeroes it on fresh entry and sets 2 on tiles restored from stage_player_occupied_tile_bitmask (FIELD@0x7da) or all valid tiles at 100%. CALCOCC@0x9f..0x27c ORs the faction bits of units (unit faction 0x60cae) and owner objects (types 1..4) per room and, if a room has exactly one faction, paints all its tiles with that bit (and reveals them for the player). DRAWMAP@0x4ac and DRAWMINIMAP@0x193 colour tiles by the bit (blue/red/green/yellow); MVSEEK@0x390 and SETROUTE@0x162 test it against the mover's allied-faction mask 0x5340a. |
| `0x50bb7` | tile_room_ids | high | investigation | SETROOM@0x0 clears it and flood-fills (queue) from every unassigned nonzero tile across edges whose terrain has terrain_room_join_flags set; edges of non-joining terrain (ids 1 and 3) separate rooms and are drawn as doors. DRAWMAP@0x2df/0x3d8 draws walls/doors where neighbouring room ids differ; CALCOCC@0x75 aggregates faction presence per room. |
| `0x50ec3` | room_min_tile_x | high | investigation | SETROOM@0xd8 (with room_min_tile_y 0x51135, room_width_tiles 0x513a7, room_height_tiles 0x51619). DRAWMAP@0x21e draws a room's hall art over x*100,y*100,w*100,h*100; CALCOCC@0x1b5 iterates the box to paint ownership. |
| `0x51135` | room_min_tile_y | high | investigation | SETROOM@0xe4; see room_min_tile_x. |
| `0x513a7` | room_width_tiles | high | investigation | SETROOM@0xf7 (max_x - min_x + 1); DRAWMAP@0x202, CALCOCC@0x20b. |
| `0x51619` | room_height_tiles | high | investigation | SETROOM@0x10c; DRAWMAP@0x210, CALCOCC@0x1d2. |
| `0x5188b` | room_hall_art_record_ids | med | investigation | SETROOM@0x13d..0x263 matches HAINIT rows (labyrinth 0x2a457b, floor 0x2a4963, x 0x2a4d4b, y 0x2a5133, story-flag gates 0x2a551b/0x2a5ceb) to rooms. SETHALLTEX loads the record's TPnnn.AGF (0x2a4193) into one of 10 surfaces ('テーマパーク上限オーバー' debug comment when >10), and DRAWMAP@0x1df draws it across the room box instead of tiled terrain. |
| `0x538c4` | battle_unit_graphic_asset_ids | high | investigation | Cols 0..14 of unit_graphic_asset_ids (costume-overridden for characters); BTL/DRAWCH/DRAWCHP/STATUS/FIELD read. |
| `0x53bb2` | battle_unit_unit_ids | high | investigation | EBINIT id per battle slot (SETCH@0x0002e write; SETEN@0x00000, COMMITBTL, ADDEXP, BTL read). |
| `0x53be4` | battle_unit_source_index | med | investigation | SETCH@0x0003a writes the roster slot; SETEN@0x0000c reads it as 0x2a9b8a to choose placement overrides (0x140aa4/0x140afe colours, 0x140a86 divisor, 0x140a4a min level, 0x140a68 cap). |
| `0x53c48` | battle_unit_equipment_item_ids | high | investigation | SETEN@0x0054e, SETCH@0x00217; EQUIP fills slot 0 with unit_default_attack_item_id; ADDEXP checks items 1901/1902; COMMITBTL items 1905/1906 add drop draws. |
| `0x53cde` | battle_unit_levels | high | investigation | SETEN@0x00166, SETCH@0x0019a, COMMITBTL@0x002c0. |
| `0x53d10` | battle_unit_cutin_element | med | investigation | See unit_cutin_element. |
| `0x53d42` | battle_unit_resistance_indices | high | investigation | SETEN@0x00513, SETCH@0x001dc; CALCREVISE backs it up in 0x53ed2. |
| `0x54062` | battle_unit_stats | high | investigation | Working (equipment-revised) stats, 14-col layout. SETEN/SETCH write base values; CALCREVISE@0x00043 restores the unrevised snapshot 0x5431e (or saves it), then adds revisions; BTL/CALCDMG/MVSEEK read. |
| `0x5431e` | battle_unit_unrevised_stats | med | investigation | CALCREVISE@0x00043/0x000a4 swaps with battle_unit_stats; ADDEXP@0x0065b refreshes it from character_stats after a level-up. |
| `0x546a2` | battle_unit_skill_ids | med | investigation | SETEN@0x00564..0x00667, SETCH@0x0022d..0x00304. Also observed: [items decode, named unit_skill_slots] CALCREVISE@0x00384 writes item_granted_skill_id per equipment slot into slot 4+n. CALCSCOPE@0x000b7 and CALCREVISE@0x00409 iterate the slots. |
| `0x548c8` | unit_skill_possessed_flags | med | investigation | CALCREVISE@0x00549 rebuilds it from skill slots and bundled/family expansion. ADDILLSUB@0x0003a checks condition_immunity_skill_id against it, and BTRTN@0x00060 tests specific skills. |
| `0x60c18` | battle_unit_party_cost | high | investigation | SETEN@0x00039 (unit_party_cost), SETCH@0x000f5 (affinity-scaled); ADDEXP@0x00675 sums deployed costs into 0x4fdd7. |
| `0x60d44` | unit_condition_levels | high | investigation | Main condition state. RECOVER@0x0005f, ADDILLSUB, CALCREVISE@0x005e6 and CALCSCOPE@0x0002a read it: charm (column 6) blocks all skills and confusion (column 7) blocks non-area skills. Columns: 1=instant_death, 2=hp_drain, 3=sp_drain, 4=fs_drain, 5=curse, 6=charm, 7=confusion, 8=paralysis, 9=poison, 10=water_flow, 11=strong_acid, 12=inflammation, 13=fear, 14=hp_regeneration, 15=sp_regeneration, 16=fs_regeneration, 17=exaltation, 18=accuracy_up, 19=evasion_up, 20=physical_attack_up, 21=physical_defense_up, 22=magic_attack_up, 23=magic_defense_up, 24=speed_up, 25=critical_up, 26=movement_down, 27=no_return, 28=injury. |
| `0x61320` | unit_condition_remaining_turns | high | investigation | Maintained by ADDILLSUB@0x00159 and RECOVER@0x000ac. Columns: 1=instant_death, 2=hp_drain, 3=sp_drain, 4=fs_drain, 5=curse, 6=charm, 7=confusion, 8=paralysis, 9=poison, 10=water_flow, 11=strong_acid, 12=inflammation, 13=fear, 14=hp_regeneration, 15=sp_regeneration, 16=fs_regeneration, 17=exaltation, 18=accuracy_up, 19=evasion_up, 20=physical_attack_up, 21=physical_defense_up, 22=magic_attack_up, 23=magic_defense_up, 24=speed_up, 25=critical_up, 26=movement_down, 27=no_return, 28=injury. |
| `0x618fc` | unit_condition_baseline_levels | high | investigation | Rebuilt by EQUIP@0x000ce from item_equipped_condition_levels. RECOVER resets current levels to it, and ADDILLSUB treats it as the floor. Columns: 1=instant_death, 2=hp_drain, 3=sp_drain, 4=fs_drain, 5=curse, 6=charm, 7=confusion, 8=paralysis, 9=poison, 10=water_flow, 11=strong_acid, 12=inflammation, 13=fear, 14=hp_regeneration, 15=sp_regeneration, 16=fs_regeneration, 17=exaltation, 18=accuracy_up, 19=evasion_up, 20=physical_attack_up, 21=physical_defense_up, 22=magic_attack_up, 23=magic_defense_up, 24=speed_up, 25=critical_up, 26=movement_down, 27=no_return, 28=injury. |
| `0x6338c` | battle_unit_loot_item_ids | high | investigation | SETEN@0x006cd zeroes then stores unit_initial_equipment cols 0..1; COMMITBTL@0x005ad adds each item (count 0x63580) to inventory 0x3c9cd1 on defeat. |
| `0x63580` | battle_unit_loot_counts | high | investigation | Paired with battle_unit_loot_item_ids (SETEN@0x0074c sets 1). |
| `0x6383d` | roster_unit_ids | high | investigation | UNITECH@0x003d1 finds the first empty/same slot and stores the unit id; SETCH@0x00000 and COMMITBTL@0x00124 read it. |
| `0x63905` | roster_equipment_item_ids | high | investigation | UNITECH@0x00496 seeds from unit_initial_equipment_item_ids; SETCH@0x0020c copies to battle equipment; FIELD@0x0043a reads col 1. Columns: 0=weapon, 1=accessory, 2=slot_3. |
| `0x63a31` | roster_equipped_skill_ids | high | investigation | UNITECH@0x004ee copies character_skill_list cols 0..3; SETCH@0x0022d copies into battle skill slots 0..3. |
| `0x63bc1` | roster_flags | med | investigation | UNITECH@0x00505..0x005d0. |
| `0x642c9` | character_costume_item_id | high | investigation | UNITECH seeds from unit_default_costume_item_id; SETCH@0x00117 / BTL@0x04743 use ITINIT 0x2d3018[costume] graphics; CALCREVISE uses its element 0x2df368; ADDEXP@0x00050 gives +25% exp for costume ability 0x118 in 0xefed0. Also observed: [alchemy decode, named character_costume_selection] CSINIT tests 0x642ca..0x642d1 to substitute standing-sprite variants; CGMODE@0x10e3 writes it. |
| `0x643f5` | character_resistances | high | investigation | Seeded from unit_element_resistance_levels (UNITECH@0x0020f), CALCCC adds deltas, SETCH@0x001ca stores 5+v to battle slot. |
| `0x64715` | character_stats | high | investigation | UNITECH seeds from unit_base_stats; ADDEXP grows; CALCCC adds class-change deltas; USEITEM raises; SETCH@0x00173 copies to battle stats. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=agility, 7=luck, 8=movement, 9=customer_service, 10=max_hp, 11=max_sp, 12=max_fs, 13=capacity. |
| `0x64c8d` | character_stat_caps | high | investigation | See unit_initial_stat_caps. |
| `0x65205` | character_stat_growth_fractions | high | investigation | Fractional hundredths carried between level-ups (ADDEXP@0x00255, UNITECH@0x0033e). |
| `0x6577d` | character_party_cost | high | investigation | Seeded from unit_party_cost; CALCCC adds 0x2d2016; SETCH@0x000d2 and ORGANIZE@0x018c8 scale by 0x3b2b90[affinity tier]%. |
| `0x657e1` | character_skill_list | high | investigation | Seeded from unit_skill_ids (UNITECH@0x00236); class change replaces slots; UNITECH@0x004de copies cols 0..3 to roster_equipped_skill_ids. |
| `0x65b01` | character_learned_skill_flags | high | investigation | UNITECH@0x0029c, CALCCC@0x0027d set; CALCCC snapshots it to 0x2d243a for SHOWCC. |
| `0x7ea4f` | item_inventory_counts | high | investigation | ADDITEM@0x00041 and SHOP@0x0148e compare it with 0x3e7. On overflow, ADDITEM converts the excess at item_prices column 1 into money 0x7ea39. SHOP@0x02673 displays it. |
| `0x8115f` | alchemy_recipe_learned_flags | high | investigation | Set by CHECKRECIPE@0x1ef when every unlock condition passes; ALCHEMY@0x1306 lists only learned recipes; INFO4@0x71b/0xdf4 shows ？？？ for unlearned recipes. |
| `0x82100` | item_costume_enhancement_grid | med | investigation | CALCARM@0x000a2 walks all 225 cells of a costume. For each cell with bits 0x70000 it resolves a costume_enhancement id from 0x3a092a[(r+1)*2][(c+1)*2]-100 and applies effects 0..2 whose bit (16+k) is set. SELSEWUP reads and writes the cells. Also observed: [maps decode, named costume_board_node_states] CALCARM@0xa2 iterates 225 nodes (mask 0x70000) to sum bonuses; ENHANCE@0xf53/0xf7e set bit0. |
| `0xefed0` | item_costume_gem_sockets | high | investigation | CALCARM@0x0021a adds each socketed orb's item_affinity_level_deltas and item_stat_modifiers to the costume totals; orbs 283..285 instead add +2/+3/+5 to the skill-activation bonus 0x2a9b87. The open-socket count comes from enhancement type 3 (capped at 4). |
| `0xf35a3` | viewed_scene_flags | high | investigation | SYSTEM4@0x329 sets [scjump_decision_out]=1 and store-shared-profile-int's it (SC scripts repeat this, e.g. SC0010@0x693); INFO8@0x7af tests [v-1] for glossary unlocks; HMODE@0xcd4 reads it. |
| `0xf686b` | alchemy_recipe_ever_learned_flags | high | investigation | CHECKRECIPE@0x1fb sets it and persists it with store-shared-profile-int; INFO4@0x80a/0xf16 lets the encyclopedia show recipes known from previous runs. |
| `0xf8427` | objective_seen_flags | med | investigation | INFO1@0x6be sets 1 for the objective the cursor lands on (NEW badge suppression). |
| `0xf8bf7` | quest_journal_seen_states | med | investigation | INFO2@0x65f copies the current state when a card is highlighted; INFO2@0xe89 draws the NEW badge while state>0 and seen&1==0. |
| `0xf9877` | vocabulary_read_flags | med | investigation | INFO8@0x6cc marks the highlighted entry read; INFO8@0xc0c reads it for the NEW badge. |
| `0xf9c5f` | quest_state_bits | high | investigation | Runtime quest state. CHECKCONTRACT sets bit0 on posting (@0x1b9), bit1 on failure (@0x2b1/0x35d), bit2 on clear (@0x259); CHECKREWARD@0x27 runs RECEIPT for (state&9)==1 && state&6; RECEIPT@0x832 sets bit3. INFO2 draws posted/failed/cleared/settled badges from it. |
| `0xfa42f` | quest_elapsed_days | high | investigation | Seeded to -expedition_elapsed_days on posting (CHECKCONTRACT@0x1ab), accumulated per settlement (CHECKREWARD@0xbe), frozen into the final count on clear/failure; compared with quest_deadline_days. |
| `0xfd49c` | unit_history_face_asset_ids | high | investigation | HISTORY@0x011a1 sets this texture as the speaker icon in the backlog when nonzero (else a default). |
| `0xfd884` | voice_setting_default_text_colors | high | investigation | Default per-character text effect colour. INIT2@0x00238 and INITCONFIG copy it into the user setting 0x3225[slot] (persisted); CONFIG4@0x00fbe resets it; CONFIG2@0x01d16 previews it with set-text-effect-color. |
| `0xfdc6c` | unit_voice_family_unit_ids | high | investigation | Scene scripts, BTL@0x033c9, FIELD, HISTORY@0x00953 map unit -> family -> voice_setting_slot (0xfe054) -> voice_suppressed (0x2c84) before playing a voice. |
| `0xfe054` | unit_voice_setting_slot | high | investigation | Inverse of voice_setting_unit_ids (note unit 11 -> slot 10 whose unit id is 36). Indexed by unit_voice_family_unit_ids in scenes/BTL/FIELD/HISTORY; the slot indexes voice_suppressed 0x2c84 and voice_preview 0xfe824. |
| `0xfe43c` | voice_setting_unit_ids | high | investigation | CONFIG4 joins a setting row to its unit. |
| `0xfe824` | voice_setting_preview_asset_ids | high | investigation | CONFIG4 plays it as the sample; HISTORY@0x00966 treats slot>0 && value>0 as voiced. |
| `0xfec12` | gallery_costume_character_portraits | med | investigation | CGMODE@0xc3a lists rows whose column 0 is nonzero (max 16). Columns: 0=portrait_resource, 1=sprite_resource. |
| `0xfec3a` | gallery_costume_character_slots | med | investigation | CGMODE@0xc88 enables the costume picker when nonzero, matches it against item column 0x2e39b8[item*5+k] to find owned costume items, and @0x10cb writes the choice into character_costume_selection[slot]. Also observed: [progression decode, named gallery_page_character_ids] CGMODE@0x10cb/0x10e3 forces G[0x64201+char] bit1 and G[0x642c9+char]=selected form before calling the EVINIT chain so gallery CGs render in the chosen costume. Not written by CGINIT (populated elsewhere). |
| `0xfec4e` | hmode_character_portraits | med | investigation | HMODE@0xa07 lists nonzero rows; HMODE@0x12cb set-texture column 0; HMODE@0x1332 column 1. Also observed: [progression decode, named h_scene_page_portrait_assets] HMODE@0xa07 lists pages with nonzero column 0 (tab icons, HMODE@0x12cb); HMODE@0x1332 draws column 1 as the page portrait. Columns: 0=portrait_resource, 1=sprite_resource. |
| `0xfec76` | gallery_image_assets | high | investigation | CGMODE@0xbb4 tests column 0 with is-catalog-resource-unlocked and CGMODE@0x10af shows it (after the EVINIT remap); SAVE@0xc03 and ALLMAP@0x52f match column 0 against the current CG and load column 1 (384x216) as the save thumbnail. $1$CGINIT rewrites column 0 of some unlisted ids. Columns: 0=gallery_image_asset_id, 1=save_preview_asset_id. |
| `0x1003e6` | gallery_page_ids | high | investigation | CGMODE@0xb1c groups records by page (pages map to characters through gallery_page_character_ids). |
| `0x100f9e` | gallery_slot_ids | high | investigation | CGMODE@0xb28: key = slot*100 + variant_ordinal into the per-page grid 0x104651. |
| `0x101b56` | gallery_variant_ordinals | high | investigation | CGMODE@0xb0e gate and ordering of variants behind one thumbnail. 1251 merged records. |
| `0x10270e` | gallery_scroll_origin_x | med | investigation | CGMODE@0x1190 negates it as the X origin when the CG size differs from the previous one. |
| `0x1032c6` | gallery_scroll_origin_y | med | investigation | CGMODE@0x11a3 Y counterpart of gallery_scroll_origin_x. |
| `0x103e7e` | gallery_thumbnail_asset_ids | high | investigation | CGMODE@0x1d5c set-texture's the slot thumbnail (147x83) when the slot has unlocked variants. |
| `0x136331` | h_scene_replay_script_ids | high | investigation | HMODE@0x982 counts populated cells and tests is-catalog-resource-unlocked on them; HMODE@0xe99 call-script's the selected replay. |
| `0x136719` | h_scene_thumbnail_asset_ids | high | investigation | HMODE@0x14a5 draws the 297x83 thumbnail for unlocked slots. |
| `0x1372dd` | scjump_scene_script_resource_ids | high | investigation | SCINIT decision-to-script registry. SYSTEM4@0x2dc indexes it by scjump_decision_out and copies the result to the next-script cell G[0x4f6] (0 -> SC0000 0x6d). Base 1545 rows + $1$/$2$/$3$ add/overwrite (1797 merged). |
| `0x1399ed` | scjump_scene_context_tags | high | investigation | SCINIT's parallel authored tag. Not chapter numbers (Himegari's scjump_authored_chapters analog): 925 of 931 decision ids emitted by SCJUMP + $n$SCJUMP carry the tag equal to the 0x3070 context branch that assigns them (6 legacy mismatches: 599, 700, 3514, 3518, 3519, 5900). No script reads it at runtime. |
| `0x13c0fd` | scjump_scene_queue_priorities | high | investigation | When a SCJUMP branch would queue a scene into a pending slot already holding another, it replaces it only if priority[queued] > priority[candidate] ($3$SCJUMP@0x51f2, SCJUMP@0x3c28a vs G[0x13d86d]=priority[6000]); STUDIO@0x9df plays a queued character scene immediately when the character lacks bit 0x100 in G[0x63bc1+char] or the queued scene has a nonzero priority. |
| `0x13e874` | world_area_icon_asset_ids | high | investigation | ALLMAP@0x1142 set-texture into slot 0x12c+list index for each world-map marker; SELSTAGE@0xd16, SHOWALLMAP@0x64 and INFO3@0x1f8d load it as the area picture. |
| `0x13e8d8` | world_area_list_order_keys | high | investigation | Written by AMINIT (runtime chain 0x3d41c0, run by ALLMAP@0x2e/0x235 and TOWN). ALLMAP@0xd7a copies it for each visible area and sort-indices-by-key-sum orders world_area_visible_list (wheel/joy cycling order). |
| `0x13e93c` | world_area_kinds | high | investigation | SELSTAGE@0x439: kind 1 builds the list of STINIT2 stages whose stage_labyrinth_no equals 0x428e (sorted by 0x14ccb4); SELSTAGE@0x4f4: kind 2 runs the 0x3d41e0 (SIINIT) chain instead; SELSTAGE@0xcbf kind 2 sets global 0 = 3 (go to town); SELSTAGE@0x1314/0x133f choose the header texture (stage 0x140d4c vs 0x13eacd). |
| `0x13e9a0` | world_area_map_x | high | investigation | ALLMAP@0x5a/0x1fc centre the scroll (0x360f = x-0x200); ALLMAP@0xd4b lists only areas with state bit0 and x != 0; SHOWALLMAP@0x48 draws the focus marker. FIELD@0x27d sets state bit0 directly for areas whose x is 0 when they are entered. |
| `0x13ea04` | world_area_map_y | high | investigation | ALLMAP@0x68 (0x3610 = y-0x120), ALLMAP@0xdfb builds marker positions, SHOWALLMAP@0x56. |
| `0x13ea68` | world_area_travel_days | high | investigation | SELSTAGE@0xbf9 and TOWN@0x74 add it to 0x3697 on entering; FIELD@0x26a adds it on a fresh labyrinth entry; SELSTAGE@0xdab draws it as a number in the area header. ALLMAP@0x3b4 forces a return home once 0x3697 >= 30 ('経過日数が30日を超えました'). |
| `0x13eacc` | town_background_texture | high | investigation | SIINIT@0x3d writes it; TOWN@0xbda set-texture. |
| `0x13eacd` | town_preview_texture | med | investigation | SIINIT@0x42/0x200/0x237/0x26e/0x2a5 writes it per town; SELSTAGE@0x1354 set-texture when the hovered location is a town (0x13e93c[loc]==2). |
| `0x13eace` | town_bgm | high | investigation | SIINIT@0x38; TOWN@0x1b8 play-bgm. |
| `0x13eacf` | town_site_icon_textures | high | investigation | SIINIT@0x4c..; TOWN@0xc63 set-texture for each visible site. |
| `0x13eaed` | town_site_x | high | investigation | TOWN@0xa0a/0xa6a cursor/marker placement. |
| `0x13eb0b` | town_site_y | high | investigation | TOWN@0xa1d/0xa7f. |
| `0x13eb29` | town_site_cursor_links | med | investigation | TOWN@0x6a8 follows [site*4 + direction] when moving the cursor, skipping hidden sites. Direction order inferred from coordinates: down, left, up, right. Columns: 0=down, 1=left, 2=up, 3=right. |
| `0x13eba1` | town_location_event_scene_ids | med | investigation | SCJUMP context 11 writes [current_town_location] (SCJUMP@0x60a7) and sets town_location_event_flags bit1; TOWN@0xad0 copies it to scjump_decision_out when the location's bit1 is set. TOWN@0x834 clears the array. |
| `0x13ebbf` | town_location_event_markers | low | investigation | SCJUMP@0x60ac..0x60ec writes five columns per location alongside town_location_event_scene_ids; TOWN@0xc9c/0xe3b reads them for the location UI. Column meaning beyond col0 unknown. |
| `0x13ec64` | studio_extension_required_ranks | high | investigation | ARRANGE@0xcb2 rejects with 工匠ランクが不足しています; DRAWSTUDIOLV@0x64 counts buildable levels. |
| `0x13ecb4` | studio_extension_costs | high | investigation | ARRANGE@0xcf7 compares with economy_money (増築費用が不足しています) and @0xe0a deducts it; DRAWSTUDIOLV@0x48 uses nonzero cost as 'level exists'. |
| `0x13ed04` | studio_extension_material_item_ids | high | investigation | ARRANGE@0xd59 checks inventory (増築資材が不足しています) and consumes them. Columns: 0=material_0, 1=material_1, 2=material_2, 3=material_3. |
| `0x13ee44` | studio_extension_material_quantities | high | investigation | ARRANGE@0xd89. Columns: 0=quantity_0, 1=quantity_1, 2=quantity_2, 3=quantity_3. |
| `0x13ef84` | studio_room_layout_grids | high | investigation | ARRANGE@0x486/0x842/0x92a reads the cell under the cursor before placing an ornament into studio_room_ornaments; DRAWBUILD/DRAWBUILDMARK/SELORN/SETSTUDIOCH/STUDIO also read it. Authored by 40 copy-inline-int-array blocks. |
| `0x13f754` | studio_room_upkeep_costs | med | investigation | CALCSETTLE@0x8a sums the four rooms' current-level values into settlement line 0x13fba8. |
| `0x13f7a4` | studio_shop_shelf_capacity_by_level | high | investigation | SETSTUDIO@0x0 copies [studio_room_levels[0]] into studio_shop_shelf_capacity. |
| `0x13f7b8` | studio_shop_purchase_rate_by_level | med | investigation | SETSTUDIO@0xc -> studio_shop_purchase_rate. |
| `0x13f7cc` | studio_shop_visitor_rate_by_level | med | investigation | SETSTUDIO@0x18 -> studio_shop_visitor_rate. |
| `0x13f7e0` | studio_shop_price_rate_by_level | med | investigation | SETSTUDIO@0x24 -> studio_shop_price_rate. |
| `0x13f7f4` | studio_material_saving_by_level | high | investigation | SETSTUDIO@0x30 -> studio_material_saving. |
| `0x13f808` | studio_synthesis_exp_bonus_by_level | high | investigation | SETSTUDIO@0x3c -> studio_synthesis_exp_bonus. |
| `0x13f81c` | studio_mutation_rate_by_level | high | investigation | SETSTUDIO@0x48 -> studio_mutation_rate. |
| `0x13f830` | studio_knowledge_by_level | high | investigation | SETSTUDIO@0x54 -> studio_knowledge. |
| `0x13f844` | studio_resident_capacity_by_level | med | investigation | SETSTUDIO@0x60 -> studio_resident_capacity. |
| `0x13f858` | studio_sense_by_level | high | investigation | SETSTUDIO@0x6c -> studio_sense. |
| `0x140386` | studio_character_event_scene_ids | med | investigation | SCJUMP studio branches queue a scene per character (e.g. SCJUMP@0x3c27d sets [2]=6000, respecting scjump_scene_queue_priorities); STUDIO@0x9ae plays it as scjump_decision_out when the character is visited. |
| `0x140414` | stage_background_texture_asset_ids | high | investigation | SETMAPTEX@0x7 and @0x15 load slots 0/1 into surfaces 0x3e/0x3f. STINIT zeroes both (STINIT@0x20) and writes them per stage. |
| `0x140416` | stage_map_tint_color | med | investigation | DRAWMAP@0x110 passes it to gfx-draw-color on the map layer (0x0, 0x0, 0xff, colour). |
| `0x140417` | stage_bgm_id | med | investigation | FIELD@0x5d4 copies it to the current/saved BGM pair 0x378c/0x378d on a fresh map and restores it from them on continue (FIELD@0x63e/0x646); STAGEREADY and many SCnnnn event scripts overwrite it together with 0x378c to change the stage music mid-map. |
| `0x140418` | stage_turn_limit_outcome | high | investigation | FIELD@0x938c: when the turn counter 0x429b equals stage_turn_limit[row], sets the battle result 0x2a2ddf to 1 (victory) if this is nonzero else 2 (defeat). Row = 0x4a6d[stage] \| 0x14c4e4[stage] (FIELD@0x9362). |
| `0x14041a` | stage_turn_limit | high | investigation | FIELD@0x9377 ends the map when 0x429b equals the row's value (see stage_turn_limit_outcome). STAGEROUND@0x9f1.. loops event checks up to it; DRAWCHP@0x913 displays it. Objective strings ('３０ターン経過') confirm. |
| `0x14041c` | stage_keep_conditions_on_revisit | med | investigation | FIELD@0x556 and INFO3@0xd43: when the stage is a revisit (0x4a6d or 0x14c4e4 set) and this is 0, the 7-cell condition block at 0x14041d is replaced by a default inline array (FIELD@0x56b). 1 keeps the authored conditions. |
| `0x14041d` | stage_victory_capture_object_slots | high | investigation | Head of a 7-cell condition block (STINIT zeroes 0x14041d..0x140423 at @0x0). FIELD@0xaa0b: -1 requires every active type-1 object to be in state 1 (player-owned); otherwise victory needs at least one listed slot to be a player-owned type-1 object (@0xaab0). |
| `0x140420` | stage_victory_defeat_unit | high | investigation | FIELD@0xab1b: victory sub-condition. -1 checks per-faction alive counts 0x4fddb for hostile factions; -2 scans active units whose 0x2b66ac flag is set; positive compares base unit id 0x53bb2 of every live hostile unit. Hostility uses stage_faction_alliance. |
| `0x140421` | stage_victory_story_flag | high | investigation | FIELD@0xacec: victory additionally requires 0x50d[flag-1]==1. FIELD@0xa9f5 skips the victory test when all of 0x14041d/0x140420/0x140421 are 0. |
| `0x140422` | stage_defeat_base_loss_mode | high | investigation | FIELD@0xa921..0xa998 counts active type-1 objects and their stage_object_base_value, owned vs total, and sets defeat (0x2a2ddf=2, comment '拠点陥落'). |
| `0x140423` | stage_defeat_story_flag | high | investigation | FIELD@0xa9ae: if 0x50d[flag-1]==1 the map is lost (0x2a2ddf=2, comment '敗北条件達成'). |
| `0x140424` | stage_reward_row_required_story_flags | med | investigation | FIELD@0xc9b3: for reward row i, if a listed flag is not set the row's completion bit 0x1dcc5[stage][i] bit0 is reset and 0x4e55[stage] cleared. Columns: 0=flag_a, 1=flag_b. |
| `0x14042c` | stage_faction_alliance | high | investigation | FIELD@0xab69/0xac1e/0xacb7 and COMMITBTL/CALCSUPPORT: faction f != 1 is hostile when stage_faction_alliance[f]==0 or differs from [1]. FIELD@0xc45 also uses it when placing enemies. |
| `0x140431` | stage_first_visit_party_restriction | low | investigation | FIELD@0x104a (with 0x4a6d[stage]==0) and ORGANIZE@0xba6 restrict sortie selection on the first visit to units flagged in 0x7ea40. |
| `0x140432` | stage_map_texture_asset_overrides | high | investigation | SETMAPTEX@0xc7..0x125 loads each slot into surface 0x52+slot; DRAWMAP selects surfaces by terrain texture slot. Same contract as Himegari stage_map_texture_asset_overrides. |
| `0x140446` | stage_object_tile_x | high | investigation | DRAWOBJ@0x10b draws at x*100+50 - width/2; FIELD@0x8b4 indexes the 27x27 tile grid 0x508de; CALCOCC/MVSEEK/RTN_M* use it for occupancy and targeting. |
| `0x140478` | stage_object_tile_y | high | investigation | Pair of stage_object_tile_x (DRAWOBJ@0x135, FIELD@0x8c0, CALCOCC, MVSEEK). |
| `0x1404aa` | stage_object_type_id | high | investigation | SETOBJ@0x2a iterates slots with a type; SETMAPTEX@0x156 loads the type's sprite (0x2a363e); DRAWOBJ, CHECKOBJ, CALCOCC, FIELD read OBINIT properties through it. |
| `0x1404dc` | stage_object_base_value | high | investigation | FIELD@0xa8e2/0xa910 sums it over all / player-owned type-1 objects for stage_defeat_base_loss_mode 2 and 3. |
| `0x14050e` | stage_object_primary_param | high | investigation | FIELD@0x83e seeds the persistent object state 0x43655[stage][slot] from it for draw_class-1 types on a first visit; CHECKOBJ@0x1f3 (teleport dest x), @0x29a (chest item vs inventory), @0x2db (sealed-door flag), @0x3b9 (hazard faction). |
| `0x140540` | stage_object_secondary_param | med | investigation | CHECKOBJ@0x208 (teleport destination y occupancy check); FIELD@0x7a49.. reads it when an object is used (item quantity / harvest grade); RTN_M004/005/011/012 read it for AI object targeting. |
| `0x140572` | stage_object_reinforcement_interval_turns | high | investigation | FIELD@0x9cf7..0x9d98: spawns from the object when (turn - activation turn 0x50e90) / interval exceeds the spawned count 0x4f9a9; FIELD@0x9e58 uses it as the activation turn of type-27 (異界の門). |
| `0x1405a4` | stage_object_reinforcement_spawn_limit | high | investigation | FIELD@0x9d05 stops spawning once the per-object count 0x4f9a9 reaches it. |
| `0x1405d6` | stage_object_difficulty_mask | high | investigation | SETOBJ@0x4d: check-bit against 0x320b; clear bit = object not placed. |
| `0x140608` | stage_object_required_story_flags | high | investigation | SETOBJ@0xcc and SELSTAGE: any positive entry whose 0x50d[flag-1]!=1 suppresses the object. |
| `0x140766` | stage_object_forbidden_story_flags | high | investigation | SETOBJ@0x12d and SELSTAGE: any entry whose flag is set suppresses the object. |
| `0x140860` | stage_object_use_story_flags_by_faction | high | investigation | FIELD@0x2e71 and @0x73ff: when a unit of faction f uses the object, sets 0x50d[value-1]=1 (e.g. 8275 = gather done), then increments the use counter 0x37305. |
| `0x14095a` | stage_enemy_spawn_tile_x | high | investigation | FIELD@0xbe9 places the unit at it when stage_enemy_spawn_object_slot is 0. |
| `0x140978` | stage_enemy_spawn_tile_y | high | investigation | Pair of stage_enemy_spawn_tile_x (FIELD@0xbfc). |
| `0x140996` | stage_enemy_spawn_object_slot | high | investigation | FIELD@0xac4: placement at the object's tile; reinforcement pool membership for spawner objects (FIELD@0x9c26). |
| `0x1409b4` | stage_enemy_faction_id | high | investigation | FIELD@0xb8b copies it to the unit faction 0x60cae; bases/gates spawn only enemies whose faction equals the object's owner state (FIELD@0xb0a); ADDEN reads it. |
| `0x1409d2` | stage_enemy_difficulty_mask | high | investigation | FIELD@0x4ae6 and SELSTAGE@0x7d5 check-bit against the difficulty. |
| `0x1409f0` | stage_enemy_first_visit_only | high | investigation | FIELD@0x4b08 and SELSTAGE@0x7f7 skip the enemy when value==2 and 0x4a6d[stage]==1. |
| `0x140a0e` | stage_enemy_random_selection_weight | high | investigation | FIELD@0xad0 queues nonzero entries to their spawn object instead of placing them; FIELD@0x9fc1..0xa040 draws one by weighted random-modulo when the spawner fires. |
| `0x140a2c` | stage_enemy_unit_id | high | investigation | FIELD@0x4ad0 skips empty slots; FIELD@0xb78/0xa040 → 0x2a9b8b → SETEN; SELSTAGE@0x7bf lists enemies; ADDEN/CHANGEEN. |
| `0x140a4a` | stage_enemy_min_level | high | investigation | SETEN@0x234 raises the computed level to it. |
| `0x140a68` | stage_enemy_max_level | high | investigation | SETEN@0x25c clamps the level to it (99 under debug flag 0x321e&2). |
| `0x140a86` | stage_enemy_auto_level_scale_divisor | high | investigation | SETEN@0x1b7: level += (reference level 0x4f9a7 - unit base level) / divisor when positive. |
| `0x140aa4` | stage_enemy_movement_routine_set_ids | high | investigation | SETEN@0x45 copies the 3 cells to the unit's 0x61ed8 row. |
| `0x140afe` | stage_enemy_battle_routine_set_ids | high | investigation | SETEN@0x6c copies the 3 cells to the unit's 0x61f6e row. |
| `0x140b58` | stage_enemy_required_story_flags | high | investigation | FIELD@0x4b50 and SELSTAGE@0x83f. |
| `0x140c2a` | stage_enemy_forbidden_story_flags | high | investigation | FIELD@0x4bb1 and SELSTAGE@0x8a0. |
| `0x140cc0` | stage_ally_start_tile_x | high | investigation | FIELD@0x49bc validates it against the map bounds; FIELD@0x4c05 writes it to the placed party unit's x 0x60ce0; ORGANIZE@0xc7a. |
| `0x140cca` | stage_ally_start_tile_y | high | investigation | FIELD@0x49df/0x4c18, ORGANIZE@0xc9d. |
| `0x140cd4` | stage_ally_forced_unit_id | high | investigation | FIELD@0xe4d..0xf01 finds the party member whose base unit 0x2d2845 matches and deploys it at the slot; ORGANIZE@0xabd marks it fixed. |
| `0x140cde` | stage_ally_forced_first_visit_only | high | investigation | FIELD@0xe62: the forced assignment applies when this is 0 or 0x4a6d[stage]==0; ORGANIZE@0xacb. |
| `0x140ce8` | stage_ally_slot_required_story_flags | high | investigation | FIELD@0x4a2e and ORGANIZE@0xcec: slot unusable unless all set. |
| `0x140d1a` | stage_ally_slot_forbidden_story_flags | high | investigation | FIELD@0x4a88 and ORGANIZE@0xd46: slot unusable if any set. |
| `0x140d4c` | stage_select_background_asset_ids | med | investigation | SELSTAGE@0x1330 takes it for the highlighted stage when the labyrinth's list mode (0x13e93c) is 1 and draws it as the stage-select backdrop. |
| `0x141134` | stage_labyrinth_ids | high | investigation | FIELD@0x7 -> current_labyrinth_id; FIELD@0x3f reverse lookup; SELSTAGE@0x480 filters the stage list by the current labyrinth; CHECKSTAGE@0x1d skips ids <= 0 and @0x14b resolves prerequisites; ARENA, INFO3, ORGANIZE read it. |
| `0x14151c` | stage_floor_numbers | high | investigation | FIELD@0x13 -> current_floor_no; SELSTAGE@0x72e for event-entry stages; CHECKSTAGE@0x160 prerequisite match; ARENA, ORGANIZE. 38 side/companion stages (floors 11..16, 41..45) have no MPINIT map and are event-entry stages. |
| `0x141904` | stage_required_party_unit_ids | high | investigation | SELSTAGE@0x6c0 hides a stage whose required unit is unavailable (0x64201 bit 1 clear) and SELSTAGE@0xb8a refuses to launch with '必須ユニットが同行していません'; column block = 5*(stage_cleared_flags \| stage_boss_flags). |
| `0x144014` | stage_prerequisite_labyrinth_ids | high | investigation | CHECKSTAGE@0x120..0x183: when nonzero, finds the stage with this labyrinth and stage_prerequisite_floor_numbers and returns its stage_cleared_flags. |
| `0x1443fc` | stage_prerequisite_floor_numbers | high | investigation | CHECKSTAGE@0x167, paired with stage_prerequisite_labyrinth_ids. |
| `0x1447e4` | stage_required_story_flag_ids | high | investigation | CHECKSTAGE@0x89..0xc2 returns 'locked' as soon as one listed flag is not 1. |
| `0x14633c` | stage_forbidden_story_flag_ids | high | investigation | CHECKSTAGE@0xe3..0x11c. |
| `0x147e94` | stage_recommended_levels | med | investigation | SELSTAGE@0xfcb draws it as a two-digit number on each stage row (event-entry stages excluded). |
| `0x14827c` | stage_objective_money_rewards | high | investigation | STAGECLEAR@0x8e3/0xaef labels it 'お金'; a slot exists when any of the four reward tables is nonzero (STAGECLEAR@0x56a, SELSTAGE@0x1092, FIELD@0xc834, AIM). Slot conditions come from the STINIT per-stage tables. |
| `0x14921c` | stage_objective_item_rewards | high | investigation | STAGECLEAR@0x90d prints the item name from G-string 0x4228[id]. |
| `0x14a1bc` | stage_objective_evaluation_rewards | high | investigation | STAGECLEAR@0x943 labels it '評価'. |
| `0x14b15c` | stage_objective_bonus_point_rewards | high | investigation | STAGECLEAR@0x96d labels it 'おまけポイント'. |
| `0x14c4e4` | stage_boss_flags | med | investigation | ORed with stage_cleared_flags by FIELD@0x541/0x607 (selects the revisit objective/condition strings), SELSTAGE@0x687 (party-requirement columns 5..9) and @0xeb8; AIM, DRAWCHP and INFO3 also read it. |
| `0x14c8cc` | stage_event_entry_flags | high | investigation | SELSTAGE@0x720 sets current_floor_no and calls the SCJUMP layer chain (0x3d4180); SELSTAGE@0x4b8 lists such stages only if an event is pending; SELSTAGE@0xf82 omits level/occupation display. |
| `0x14ccb4` | stage_select_sort_keys | high | investigation | SELSTAGE@0x44e sort-indices-by-key-sum over all 1000 stages before filtering by labyrinth. |
| `0x14d09e` | map_routine_provider_selectors | high | investigation | RTINIT bank 0 (root 0x14d09e + bank*50000). MVRTN@0x000e0 dispatches call-script via local table 0x7+n (RTN_M001..RTN_M019, RTN_M051..054, RTN_M061, RTN_M071). |
| `0x1593ee` | map_routine_activation_percents | high | investigation | RTINIT bank 1 (root 0x14d09e + bank*50000). MVRTN@0x00253 runs the step only when random-modulo 100 < value. |
| `0x16573e` | map_routine_parameter_1 | med | investigation | RTINIT bank 2 (root 0x14d09e + bank*50000). Read by most RTN_M scripts. RTN_M005@0x00000 compares it with unit x 0x60ce0. |
| `0x171a8e` | map_routine_parameter_2 | med | investigation | RTINIT bank 3 (root 0x14d09e + bank*50000). RTN_M005@0x00010 compares it with unit y 0x60d12. |
| `0x17ddde` | map_routine_parameter_3 | low | investigation | RTINIT bank 4 (root 0x14d09e + bank*50000). Read only by RTN_M011. |
| `0x18a12e` | map_routine_parameter_4 | low | investigation | RTINIT bank 5 (root 0x14d09e + bank*50000). Read only by RTN_M011. |
| `0x1a27ce` | map_routine_use_limits | high | investigation | RTINIT bank 7 (root 0x14d09e + bank*50000). MVRTN@0x00205 disables the step once the per-unit counter 0x62004[unit][step] reaches it. RTN_M004/005/012 read it too. |
| `0x1aeb1e` | map_routine_required_story_flag_ids | high | investigation | RTINIT bank 8 (root 0x14d09e + bank*50000). MVRTN@0x0013c: every listed flag must be 1. Occupies banks 8..10. |
| `0x1d350e` | map_routine_forbidden_story_flag_ids | high | investigation | RTINIT bank 11 (root 0x14d09e + bank*50000). MVRTN@0x001b6: any listed flag equal to 1 disables the step. Occupies banks 11..13. |
| `0x1f7efe` | battle_routine_provider_selectors | high | investigation | RTINIT bank 14 (root 0x14d09e + bank*50000). BTRTN@0x0015a dispatches via local table 0xa+n. |
| `0x20424e` | battle_routine_activation_percents | high | investigation | RTINIT bank 15 (root 0x14d09e + bank*50000). BTRTN@0x002d3 compares it with the random stream 0x4f9dc. |
| `0x21059e` | battle_routine_parameter_1 | med | investigation | RTINIT bank 16 (root 0x14d09e + bank*50000). RTN_B004@0x00000 selects that skill (0x60c4a) if it is in the usable list 0x52c3a. |
| `0x25997e` | battle_routine_use_limits | high | investigation | RTINIT bank 22 (root 0x14d09e + bank*50000). BTRTN@0x0027f compares it with 0x629c8[unit][step]. |
| `0x265cce` | battle_routine_required_story_flag_ids | high | investigation | RTINIT bank 23 (root 0x14d09e + bank*50000). BTRTN@0x001b6. Occupies banks 23..25. |
| `0x28a6be` | battle_routine_forbidden_story_flag_ids | high | investigation | RTINIT bank 26 (root 0x14d09e + bank*50000). BTRTN@0x00230. Occupies banks 26..28. |
| `0x2a2d9a` | map_neighbor_dx | high | investigation | INIT2@0x1da inline array. SETROOM@0x313, MVSEEK@0x287 and DRAWMAP@0xfa1 add it to a tile x (and 2*x+dx to reach the edge cell between two tiles). |
| `0x2a2d9f` | map_neighbor_dy | high | investigation | INIT2@0x1df inline array, paired with map_neighbor_dx by SETROOM, MVSEEK and DRAWMAP. |
| `0x2a3511` | terrain_texture_slot_indices | high | investigation | DRAWMAP@0x304 picks stage_map_texture_asset_overrides[slot] (0x140432) and draws surface 0x52+slot (DRAWMAP@0x37e); DRAWMAP@0x3ea/0x45c draws a seam where neighbouring tiles of the same room use different slots. |
| `0x2a3539` | terrain_room_join_flags | high | investigation | SETROOM@0x3b8 only enqueues a neighbour across an edge whose terrain has the flag; DRAWMAP@0xae0/0xd28 draws a door marker on edges between two tiles whose terrain lacks it; DRAWMINIMAP@0x987 likewise. |
| `0x2a3561` | terrain_visibility_classes | high | investigation | DRAWMAP@0x2d1 skips class 0 tiles; FIELD@0x79c reveals class-1 tiles on entry (all nonzero classes at 100%); MVSEEK@0x49e rejects class 0 and only lets class 3 be crossed if revealed or the unit has skill 4 (探索) (MVSEEK@0x4b4); DRAWMAP@0xb01 hides class-3 doors until revealed and @0xf5a gives class 1/2 fog a frontier tint; CALCOCC@0x400 counts nonzero classes as the occupation denominator; SETLAND@0x91 reveals dug class-1 cells. |
| `0x2a3589` | terrain_element_ids | high | investigation | STATUS@0x1f9c draws the element icon and @0x1ff2 its name; CALCSUPPORT@0x34 grants a terrain bonus when the element is >= 2 and the unit has skill 0x30 or skill 0x28+element; DRAWMINIMAP@0x225 colours the minimap by element in overlay mode bit2. |
| `0x2a35b1` | terrain_move_costs | high | investigation | MVSEEK@0x41a uses it as the step cost (forced to 1 when the mover has skill 1 飛行 or 3 転移 in 0x548c8; tripled for player units under an unexplained mode test); STATUS@0x2092 prints it ('-' when zero); FELLOW reads it too. |
| `0x2a35d9` | terrain_required_skill_ids | high | investigation | MVSEEK@0x51b..0x579 lets a unit enter the tile only if its skill matrix 0x548c8[unit][skill] holds one of the two ids; STATUS@0x2009 prints the skill names (0x5998) joined by '／'. |
| `0x2a3629` | map_texture_default_asset_ids | high | investigation | SETMAPTEX@0x110 loads it into surface 0x52+slot when the current stage's override (0x140432[slot]) is negative; positive overrides are loaded directly and zero skips the slot. |
| `0x2a363e` | object_type_sprite_asset_ids | high | investigation | SETMAPTEX@0x185 load-raw-texture-surface into surfaces 0x66+ for each type present on the stage (cached in 0x2a31cc). |
| `0x2a36a2` | object_type_sprite_widths | high | investigation | DRAWOBJ@0xfd draw width and centring (x*100+50-w/2); STATUS. |
| `0x2a3706` | object_type_sprite_heights | high | investigation | DRAWOBJ@0x104 draw height, centring, and sprite-row stride (row = persistent state * height, @0xd9). |
| `0x2a376a` | object_type_animation_frame_counts | high | investigation | DRAWOBJ@0xa2 animate-gfx-srcrect-loop with 100 ms per frame. |
| `0x2a37ce` | object_type_state_mode | high | investigation | DRAWOBJ@0xb9 (mode 1 sprite row), SETOBJ@0x1b0/0x1d4 and FIELD@0x7581/0x75a5 (bit2 handling), FIELD@0x829 seeds persistent state for mode 1. |
| `0x2a3832` | object_type_available_glow_mode | low | investigation | DRAWOBJ@0x1cd applies it via gfx-draw-color when runtime bit2 is set on a revealed tile. |
| `0x2a3896` | object_type_interaction_class | low | investigation | FIELD@0x751b: bit 2 together with mode flag 0x428a&4 suppresses automatic interaction. |
| `0x2a38fa` | object_type_use_limit | high | investigation | CHECKOBJ@0x11 and SETOBJ@0x176 block / deactivate the object once stage_object_use_counts reaches it; FIELD@0x7565. |
| `0x2a395e` | object_type_use_count_resets_on_entry | high | investigation | FIELD@0x5a2: on a fresh map entry the object's use counter 0x37305 is reset to 0. |
| `0x2a39c2` | object_type_required_story_flag | high | investigation | CHECKOBJ@0x3f blocks use unless 0x50d[flag-1]==1; FIELD@0x80ae shows the requirement. |
| `0x2a3a26` | object_type_player_only | high | investigation | CHECKOBJ@0x85 blocks units whose faction 0x60cae != 1. |
| `0x2a3a8a` | object_type_allowed_unit_ids | high | investigation | CHECKOBJ@0xa8: when column 0 is set the acting unit's base id 0x53bb2 must match one column. |
| `0x2a3bb6` | object_type_required_skill_id | high | investigation | CHECKOBJ@0x110 blocks unless 0x548c8[unit][skill] is nonzero; FIELD@0x808b shows it. |
| `0x2a3c1a` | object_type_immunity_skill_ids | high | investigation | CHECKOBJ@0x143: a unit owning any listed skill is not affected by the hazard object. |
| `0x2a3d46` | object_type_battle_stat_deltas | med | investigation | CALCBTPARAM@0x6c9..0x770 adds cols 1, 2, 6, 0 to the combatant's battle channels 0xf29cc/0xf29ce/0xf29d0/0xf29d2 when standing on the object (col 1 also subtracts from the opponent's 0xf29d2); STATUS shows them. Columns: 0=hit, 1=evasion, 2=unknown_battle_2, 3=display_3, 4=display_4, 5=display_5, 6=agility. |
| `0x2a4002` | object_type_turn_resource_deltas | high | investigation | FIELD@0x96aa applies each column per turn (positive deltas require persistent state == unit faction); FIELD@0x8221, STATUS@0x1f21 display. Descriptions 'ターン毎HP-2', 'ターン毎SP-3', 'ターン毎FS-5' fix the columns. Columns: 0=hp, 1=sp, 2=fs. |
| `0x2a412e` | object_type_action_command_id | med | investigation | FIELD@0x3a54 marks a tile as having an object command when it equals the selected command 0x4fdca; FELLOW@0xb25. |
| `0x2a4193` | dungeon_hall_textures | high | investigation | SETHALLTEX@0x88 loads up to 10 distinct hall textures for the halls placed on the current floor. |
| `0x2a457b` | dungeon_hall_dungeon_ids | high | investigation | SETROOM@0x13d matches it against the current dungeon 0x428e. |
| `0x2a4963` | dungeon_hall_floors | high | investigation | SETROOM@0x14b matches it against the current floor 0x428f. |
| `0x2a4d4b` | dungeon_hall_grid_x | high | investigation | SETROOM@0x239: region = 0x50bb7[y*27 + x]; 0x5188b[region] = hall id. |
| `0x2a5133` | dungeon_hall_grid_y | high | investigation | SETROOM@0x245. |
| `0x2a551b` | dungeon_hall_required_flags | high | investigation | SETROOM@0x189. Columns: 0=flag_0, 1=flag_1. |
| `0x2a5ceb` | dungeon_hall_forbidden_flags | high | investigation | SETROOM@0x1ec. Columns: 0=flag_0, 1=flag_1. |
| `0x2a64bc` | card_definition_type_ids | med | investigation | FIELD@0x86ae gives type 6 (転送のカード) a dedicated branch (FIELD@0x86c3 sets 0x2a2d97 and calls preloaded script slot 0x1f with the leader position); other types act purely through their populated effect arrays. |
| `0x2a6520` | card_definition_required_story_flag_ids | high | investigation | FIELD@0x8a5d eligibility: all nonzero flags must be set. |
| `0x2a664c` | card_definition_forbidden_story_flag_ids | high | investigation | FIELD@0x8ab7 eligibility: rejected if any nonzero flag is set. |
| `0x2a6778` | card_definition_awarded_item_ids | high | investigation | FIELD@0x85a8 awards one through ADDITEM. |
| `0x2a6840` | card_definition_minimum_money | high | investigation | FIELD@0x85ca rolls min + rand(max-min) (when max > min) and adds it to player_money. |
| `0x2a68a4` | card_definition_maximum_money | high | investigation | FIELD@0x85e4 upper bound of the money roll. |
| `0x2a6908` | card_definition_minimum_resource_recovery | high | investigation | FIELD@0x842c adds min + rand(max-min) per column to the party recovery accumulator. Columns: 0=hp, 1=sp, 2=fs. |
| `0x2a6a34` | card_definition_maximum_resource_recovery | high | investigation | FIELD@0x845e. Columns: 0=hp, 1=sp, 2=fs. |
| `0x2a6b60` | card_definition_minimum_resource_damage | high | investigation | FIELD@0x84cb subtracts min + rand(max-min) per column. Columns: 0=hp, 1=sp, 2=fs. |
| `0x2a6c8c` | card_definition_maximum_resource_damage | high | investigation | FIELD@0x84fd. Columns: 0=hp, 1=sp, 2=fs. |
| `0x2a6db8` | card_definition_condition_levels | high | investigation | FIELD@0x867c calls ADDILL (0x3b2238 = column, 0x3b2239 = value) for every nonzero cell; cards 16/17/18 apply conditions 8/9/10 at level 1. |
| `0x2a7970` | card_definition_visual_asset_ids | med | investigation | FIELD@0x8570 passes it to the card presentation routines (FIELD label_bcb9 / label_bbf8, chosen by whether any HP/SP/FS delta was rolled). |
| `0x2a79d5` | card_generation_weight_schedules | high | investigation | FIELD@0x832c weight = base + (turn 0x429b / interval) * growth for each eligible candidate; weighted random pick. Columns: 0=base_weight, 1=growth_interval_turns, 2=growth_weight. |
| `0x2a7b01` | card_generation_card_ids | high | investigation | FIELD@0x8308 candidate list filled by CDINIT per current_card_generation_list_id. |
| `0x2a7b66` | dig_table_roll_thresholds | high | investigation | DIG@0x12f: a pre-rolled 0..99 value from 0x4f9dc row 0 is compared slot by slot; the first enabled slot with roll < threshold awards its item (0x3c9cd1[item]++), then REWARD.BIN runs. |
| `0x2a8336` | dig_table_item_ids | high | investigation | DIG@0x6c reads slot items for every table with pending gathers; SELSTAGE@0x186a lists the obtainable materials of the stage's gathering objects; INFO3@0xca4 reads it for the encyclopaedia. FIELD@0x897e computes the table id from the touched object's type and payload (0x14050e). |
| `0x2a8b06` | dig_table_required_story_flags | high | investigation | DIG@0x8e and SELSTAGE@0x18a5. |
| `0x2a92d6` | dig_table_forbidden_story_flags | high | investigation | DIG@0xcc and SELSTAGE@0x18e3. |
| `0x2a9aa6` | dig_table_message_script_ids | med | investigation | Written for every populated table by DIINIT; no consumer reads it in the shipped scripts. DIMES.BIN itself only switches on 0x2a7b65 to print placeholder text ('何か素材が取れるかも／二行目'). |
| `0x2a9b6e` | calc_equipment_slot_overrides | med | investigation | CALCREVISE@0x0010a substitutes these for the unit's equipped items when previewing equipment, then clears them at exit (copy-to-global@0x00890). |
| `0x2a9b71` | calc_item_affinity_totals | high | investigation | CALCARM@0x00010 seeds it from item_affinity_level_deltas and adds enhancements, gems and upgrade steps. CALCREVISE@0x00264 adds it to the unit affinity levels. Columns: 0=unused, 1=physical, 2=earth, 3=ice, 4=fire, 5=lightning, 6=holy, 7=dark. |
| `0x2a9b79` | calc_item_stat_totals | high | investigation | Seeded from item_stat_modifiers by CALCARM@0x00027, then enhancement, gem and upgrade bonuses are added. CALCREVISE@0x002a8 adds it to the unit stats. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=agility, 7=luck, 8=movement, 9=customer_service, 10=max_hp, 11=max_sp, 12=max_fs, 13=skill_capacity. |
| `0x2a9b87` | calc_item_skill_activation_bonus | med | investigation | CALCARM@0x002ca..0x002fa; CALCREVISE@0x002c7 adds it to unit 0xfd30f (skill activation rate bonus). |
| `0x2a9b88` | calc_item_gem_socket_count | high | investigation | CALCARM@0x001dd counts type-3 enhancements and clamps the total to 4. CALCARM@0x0020c loops over that many sockets. |
| `0x2a9b8c` | unit_voice_asset_ids | high | investigation | Same selector layout as Himegari. FIELD@0x0a717 col 0x5352 = 0 (warp/move), 1 (object pickup), 3 ('を占拠された！' occupation); BTL@0x04bd8 col 7+n attack, 0xa critical, 0xb+2n skill, 0xc+2n critical skill (n random 0..2), 0x13..0x15 damage, 0x17 finishing blow; BTL@0x033ea col 0x16 defeated; SHOWGROW@0x000b2 col 0x18 level up. Cols 2, 4..6, 17, 18 are populated but have no selector. Columns: 0=warp, 1=treasure_capture, 3=objective_occupied, 7=normal_attack_1, 8=normal_attack_2, 9=normal_attack_3, 10=critical_normal_attack, 11=skill_use_1, 12=critical_skill_1, 13=skill_use_2, 14=critical_skill_2, 15=skill_use_3, 16=critical_skill_3, 19=damage_reaction_1, 20=damage_reaction_2, 21=damage_reaction_3, 22=defeated, 23=finishing_blow, 24=level_up. |
| `0x2b10bc` | unit_graphic_asset_ids | med | investigation | SETEN@0x00103 copies cols 0..14 to battle slot 0x538c4; SETCH@0x00155 does the same but prefers the costume item's own 17-col row ITINIT 0x2d3018[costume][k] when nonzero (cross-join). INFO5/INFO3/TOWN/DRAWBUILD read col 7 (bestiary/town sprite); FELLOW/ORGANIZE/SALE/SHOP col 4 (face); CGMODE/ROOM col 3; BTL@0x04725/0x04801 cols 15/16 (costume-overridable); BTL uses battle-slot cols 8, 9, 10, 14. Columns: 0=status_art_a_CS_AZA, 1=status_art_b_CS_AZB, 2=status_art_c_CS_AZC, 3=room_art_CS_AYA, 4=face_CA_AA, 5=face_CA_AB, 6=face_CA_AC, 7=map_sprite_compact_CP_AA, 8=map_sprite_directional_CP_AB, 9=cutin_CIC_AA, 10=cutin_CIC_AB, 11=battle_sprite_1_CB_AAA, 12=battle_sprite_2_CB_AAA_or_AAB, 13=battle_sprite_3_CB_AAA_or_AAB, 14=battle_sprite_CB_AB, 15=cutin_CIC_AC, 16=cutin_CIC_AD. |
| `0x2b570c` | unit_sort_key | high | investigation | INFO3@0x00ade and INFO5@0x006d5 sort-indices-by-key over 1000 ids; SETUNITLIST@0x0005c, SELSTAGE, SETSTUDIOCH, ENHANCE order unit lists by it. |
| `0x2b5af4` | unit_bestiary_listed | high | investigation | INFO3@0x00b10 and INFO5@0x00707 include only ids with this flag in the bestiary list. |
| `0x2b5edc` | unit_race | high | investigation | INFO5@0x00d38 draws 0x61fe[race]; SETARMSTATE@0x00124 tests item race mask 0x2e60c8 bit race; CALCDMG/CALCILL/ADDILLSUB read it for race-dependent effects. |
| `0x2b62c4` | unit_sex | high | investigation | SETARMSTATE@0x00161 checks bit <sex> of the item gender mask 0x2e6898 (Will's clothes mask 2 = bit1, Yuela's 4 = bit2); HISTORY@0x0097b: a backlog line is replayable when the voice slot preview is nonzero and (slot>0 or sex==2), i.e. unregistered female speakers fall back to the extra slot 0. |
| `0x2b66ac` | unit_boss_class | med | investigation | FIELD@0x08d6b takes abs() of both combatants' values and compares (==4 special); FIELD@0x0abef tests >0; CALCDMG@0x00821 adds a per-class bonus row; DRAWCHSTATE@0x000f5 draws a boss marker when nonzero. |
| `0x2b6a94` | unit_canonical_character_id | high | investigation | Variant-to-character normalization used before every persistent-character index: UNITECH@0x0002a, SETCH@0x0000c, ADDEXP@0x0000c, COMMITBTL@0x00130, BTL@0x04703, INFO5@0x01100 etc. (~45 scripts). Result is stored in 0x2d2845. |
| `0x2b6e7c` | unit_cutin_element | med | investigation | CALCREVISE@0x000e1 copies it to battle slot 0x53d10 (costume item field 0x2df368 overrides, CALCREVISE@0x001e2); BTL@0x0443a picks a cut-in background movie from a local table indexed by it. |
| `0x2b7264` | unit_element_resistance_levels | high | investigation | INFO5@0x00f2a draws 0x621a[5+v] for k=1..7 beside element names 0x6212+k; SETEN@0x00501 stores 5+v in battle slot 0x53d42; UNITECH@0x0020f seeds character_resistances (0x643f5). Columns: 1=physical, 2=earth, 3=cold, 4=fire, 5=lightning, 6=holy, 7=dark. |
| `0x2b91a4` | unit_default_attack_item_id | high | investigation | EQUIP@0x0004a equips it in slot 0 when the unit has no weapon; FIELD@0x0042e/0x09049 and FELLOW@0x04170 use it as the attack item; INFO5@0x00e14 draws ITINIT field 0x30c228 of it. |
| `0x2b958c` | unit_initial_weapon_level | high | investigation | UNITECH@0x001a2 sets character_weapon_level (0x7e1a1); ADDEXP@0x004a5 raises it against thresholds 0x3b2b60 and announces '武器ＬＶが<0x623a[lv]>に上がった'. |
| `0x2b9974` | unit_equippable_category_mask | high | investigation | SETARMSTATE@0x00054 ORs 0x404 and marks item i equippable when bit category(i) is set (then filters by exclusive-user list 0x2e39b8, race mask, gender mask). |
| `0x2b9d5c` | unit_initial_equipment_item_ids | high | investigation | SETEN@0x00543 copies to battle equipment 0x53c48 and (cols 0..1, count 1) to the defeat-loot list 0x6338c/0x63580; UNITECH@0x00496 seeds roster equipment 0x63905 and ADDITEMs it (mode bit1); GAMESTART reads it. Columns: 0=weapon, 1=accessory, 2=slot_3. |
| `0x2ba914` | unit_default_costume_item_id | high | investigation | UNITECH@0x000b1: sets character_costume_item_id (0x642c9) on first join and ADDITEMs it if not owned (0x7ea4f). |
| `0x2bacfc` | unit_skill_ids | high | investigation | SETEN@0x00564 copies cols 0..3 to battle skill slots 0..3 and cols 4..7 to slots 7..10 of 0x546a2; UNITECH@0x00236 seeds character_skill_list (0x657e1) and marks each in character_learned_skill_flags; INFO5@0x00f71 lists names via SKINIT base 0x5998; FIELD reads it. Columns: 0=skill_1, 1=skill_2, 2=skill_3, 3=skill_4, 4=skill_5, 5=skill_6, 6=skill_7, 7=skill_8. |
| `0x2bcc3c` | unit_initial_party_capacity | high | investigation | UNITECH@0x0014f: when the protagonist (id 1) first joins, party_cost_capacity (0x7ea3b) = this value. |
| `0x2bd40c` | unit_party_cost | high | investigation | UNITECH@0x00162 seeds character_party_cost (0x6577d); SETEN@0x00032 copies to battle slot 0x60c18; ORGANIZE@0x018c8 sums cost*0x3b2b90[affinity tier]/100 against party_cost_capacity; INFO5@0x00ebf draws it; ADDEN reads it. |
| `0x2bd7f4` | unit_initial_affinity_tenths | high | investigation | UNITECH@0x00175 sets character_affinity (0x64265) = value*10 on first join. |
| `0x2bdbdc` | unit_starting_level | high | investigation | UNITECH@0x0018a sets character_levels on first join; SETEN@0x0016d uses it as the base level for enemies (growth applied for each level above it); INFO5@0x00d88 draws it as LV. |
| `0x2bdfc4` | unit_level_cap | high | investigation | ADDEXP@0x000c7/0x00192 stops levelling at this cap; SETEN@0x00195/0x00279 clamps enemy level to it (placement override 0x140a68, or 99 when 0x321e bit1). |
| `0x2be3ac` | unit_auto_level_scale_divisor | high | investigation | SETEN@0x001d4: when no placement override (0x140a86), level += (0x4f9a7 - starting_level)/divisor if 0x4f9a7 exceeds the starting level (0x4f9a7 = reference/party level, not decoded here). |
| `0x2be794` | unit_experience_level_cap | high | investigation | COMMITBTL@0x002c7: the defeated unit's effective level for experience is min(current level, this value); level difference vs the victor picks a multiplier (>=3:250%, 2:200, 1:150, 0:100, -1:75, -2:40, else 20; 40/30/20/10 on the alternate branch). |
| `0x2beb7c` | unit_base_stats | high | investigation | Copied wholesale by SETEN@0x0012a (-> battle stats 0x54062) and UNITECH@0x001c1 (-> character_stats 0x64715). Column labels: SELSEWUP@0x00000..0x00041 builds the stat-name list 命中,回避,物理攻撃,物理防御,魔法攻撃,魔法防御,敏捷,運,移動,接客,最大ＨＰ,最大ＳＰ,最大ＦＳ,キャパシティ in index order. Corroboration: MVSEEK@0x0002c reads col 8 for move range; SETEN hard mode (0x320b==2)@0x003d4 doubles col 10, adds +20 to col 0 and +10 to col 1, scales cols 2..7 by 12/10; SETEN@0x006a8 and SETCH@0x00345 copy cols 10..12 into current HP/SP/FS; INFO5@0x00d70..0x00ed3 draws cols 0..8,10..13 on the bestiary sheet. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=agility, 7=luck, 8=movement, 9=customer_service, 10=max_hp, 11=max_sp, 12=max_fs, 13=capacity. |
| `0x2c222c` | unit_stat_growth_rates | high | investigation | SETEN@0x0030f: for each level above the starting level, frac[k] += growth; stat += frac/100; frac %= 100; then random(100) < frac grants +1. ADDEXP@0x00255 does the same per player level-up into character_stat_growth_fractions (0x65205), with the luck roll scaled x1.5/x2 by accessories 1901/1902. UNITECH@0x002f6 applies growth*2*(levels gained)/100 when a character joins above its starting level. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=agility, 7=luck, 8=movement, 9=customer_service, 10=max_hp, 11=max_sp, 12=max_fs, 13=capacity. |
| `0x2c58dc` | unit_initial_stat_caps | high | investigation | UNITECH@0x001e8 copies the row into character_stat_caps (0x64c8d). ADDEXP@0x0033b clamps each stat to 0x64c8d[c][k] (or the global 0x2a2da7 cap when 0); USEITEM@0x00115 uses the same cap for stat items; CCINIT/CALCCC raise it via the 0x2d202d cap-delta block; FELLOWSTATUS@0x00d2a draws it as the gauge limit. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=agility, 7=luck, 8=movement, 9=customer_service, 10=max_hp, 11=max_sp, 12=max_fs, 13=capacity. |
| `0x2c8f8c` | unit_experience_reward | high | investigation | COMMITBTL@0x003f9: exp = reward * level-difference multiplier / 100 (min 1) -> ADDEXP. INFO5@0x00eeb draws it on the bestiary sheet. |
| `0x2c9374` | unit_bestiary_knowledge_gain | high | investigation | COMMITBTL@0x004f4 adds it to bestiary_knowledge_percent[canonical id] (0xf29db), clamped 0..100, on each defeat. |
| `0x2c975c` | unit_drop_item_ids | high | investigation | COMMITBTL@0x0062b builds the eligible-drop list, draws from the 0x4f9dc random table and awards 0x3c9cd1[item]++ when the roll < unit_drop_chance_percent. INFO5@0x01144 lists drop names via ITINIT name base 0x4228; INFO3@ reads it too. Columns: 0=drop_1, 1=drop_2, 2=drop_3, 3=drop_4, 4=drop_5. |
| `0x2caae4` | unit_drop_chance_percent | high | investigation | COMMITBTL@0x00819/0x00857. Columns: 0=drop_1, 1=drop_2, 2=drop_3, 3=drop_4, 4=drop_5. |
| `0x2cbe6c` | unit_drop_knowledge_threshold | med | investigation | COMMITBTL@0x0063d and INFO5@0x0121f require bestiary_knowledge_percent[canonical] >= value for a drop slot to be eligible / shown. Unauthored in the shipped data. |
| `0x2cd1f4` | unit_drop_required_flag_ids | high | investigation | COMMITBTL@0x00694 / INFO5@0x01276: drop k is suppressed unless flag_bank[v-1]==1 for both entries 2k, 2k+1 (when nonzero). Columns: 0=drop_1_flag_a, 1=drop_1_flag_b, 2=drop_2_flag_a, 3=drop_2_flag_b, 4=drop_3_flag_a, 5=drop_3_flag_b, 6=drop_4_flag_a, 7=drop_4_flag_b, 8=drop_5_flag_a, 9=drop_5_flag_b. |
| `0x2cf904` | unit_drop_excluding_flag_ids | high | investigation | COMMITBTL@0x006e3 / INFO5@0x012cc: drop k is suppressed when flag_bank[v-1]==1. |
| `0x2d0c8c` | unit_large_battle_sprite | med | investigation | BTL@0x01b6d/0x01bfe/0x02c63/0x031e3 use alternate centered placement for flagged units; FELLOW, SETSTUDIOCH read it too. |
| `0x2d1074` | unit_habitat_area_ids | high | investigation | INFO5@0x01031 draws up to four habitat area names from 0x241d, greyed / '？？？？' unless area flag 0x3613[v] bit0 or 0xf703b[v] is set. |
| `0x2d2848` | item_handler_script_id | high | investigation | Set on every item. SHOP@0x03a49 (and ALCHEMY, FELLOW, INFO3, INFO4, SALE, SELGEM, SELORN) look up the value and pass it straight to call-script. That call dispatches to the item's message/behavior script in the same append layer. |
| `0x2d3018` | item_costume_graphic_asset_ids | high | investigation | Populated only for costume items (category 2). For item 101 the values resolve to CS001AZA/AZB/AZC, CS001AYA, CA001AA/AB/AC, CP001AA/AB, CIC001AA/AB, CB001AAA/AAB/AAC, CB001AB and CIC001AC/AD.AGF. SHOP@0x02c52, SALE@0x017e9, FELLOW@0x0310a and ORGANIZE@0x01175 read column 4 for face images. CGMODE@0x01bab and ENHANCE@0x0191e read column 3. BTL@0x0474a reads column 15 and BTL@0x04826 reads column 16. READICON@0x00007 and SETCH@0x0011e index the column dynamically. Columns: 0=standing_a, 1=standing_b, 2=standing_c, 3=standing_full, 4=face_a, 5=face_b, 6=face_c, 7=portrait_a, 8=portrait_b, 9=icon_a, 10=icon_b, 11=battle_a, 12=battle_b, 13=battle_c, 14=battle_d, 15=battle_icon_c, 16=battle_icon_d. |
| `0x2db4e8` | item_icon_asset_id | high | investigation | Populated for 1313 items. SHOP@0x030c7 passes it to set-texture for the list icon. ENHANCE, SALE, INFO3/INFO4, ALCHEMY and other screens use the same value for item-row presentation. |
| `0x2dbcb8` | item_furniture_sprite_asset_id | med | investigation | Populated only for the 315 furniture items (category 7); DRAWBUILD reads it to draw the placed furniture sprite. |
| `0x2dc488` | item_sort_key | high | investigation | ENHANCE@0x01121, INFO3@0x00893, REWARD@0x0037e, SELORN@0x00799 and SELSTAGE@0x0193e pass it as the key array to sort-indices-by-key-sum over 0x7d0 (2000) ids. SETTOOLLIST@0x0003c uses it as the low word of every composite list-sort key. |
| `0x2dcc58` | item_rank | high | investigation | SETTOOLLIST@0x0010c sorts by it in modes 4 and 5. SETARMSTATE@0x001bf makes an equipment-group item unusable when its rank exceeds the character's weapon rank (0x7e1a1). ALCHEMY@0x00a5c indexes alchemy_exp_by_item_rank with it. CALCSETTLE@0x003e0 lowers the shop-sale chance by 10% per rank (100 - rank*10). |
| `0x2dd428` | item_category | high | investigation | Every record sets it. ITMES@0x00006.. dispatches on it first (eq 0,1,2,3...), and ITMES titles carry the matching prefix (【基本攻撃】【重要】【衣装】【発明】【消費】【成長】【宝珠】【家具】【売専】【素材】【装飾】【鉄槌】【杖】【剣】【弓】【水晶】【煙管】【手袋】【牙】【槍】【鎌】【鈎爪】【掃除機】【薬缶】【器物】【扇子】). SETARMSTATE@0x0007f tests the character's equip mask bit by category. CALCARM@0x00038 handles costumes (2). USEITEM@0x00001 handles growth stones (5). CALCSCOPE@0x00413 builds the weapon-category bit for skill_required_weapon_category_mask. |
| `0x2ddbf8` | item_min_range | high | investigation | CALCREVISE@0x003b5 copies the slot-0 weapon value into the unit's min range (0x637a6). CALCSCOPE@0x0004c uses it as the lower bound of the attack distance loop. CALCBTPARAM@0x00114 marks the attack as ranged when it is set. 85 bows, crystals and similar items carry it; their descriptions say 射程2/射2. |
| `0x2de3c8` | item_max_range | high | investigation | Paired with item_min_range. CALCREVISE@0x003c8 copies it to the unit max range (0x637d8), and CALCSCOPE@0x00058 uses it as the upper distance bound. A value of 3 matches descriptions reading 射程3. |
| `0x2deb98` | item_attack_element | high | investigation | Populated for 323 weapons and innate attacks. Every value agrees with the element word in item_short_descriptions. CALCBTPARAM@0x001ae falls back to it for the attack element (0xf29d8) when the skill has no element. BTRTN@0x0010b feeds it into the affinity lookup, and the RTN_M05x/M071/SELBOMB scripts read it the same way. |
| `0x2df368` | item_costume_battle_variant | low | investigation | When equipment slot 2 is empty, CALCREVISE@0x001d4 copies the character's default-costume value into unit 0x53d10. BTL@0x0443a uses 0x53d10 to pick a per-costume battle movie (play-movie-to-surface). The meaning of each value is unconfirmed. |
| `0x2dfb38` | item_affinity_level_deltas | high | investigation | CALCARM@0x00000 copies the row into work 0x2a9b71. CALCREVISE@0x002f9 adds each equipped item's row to the unit affinity levels 0x53d42[8], then clamps them to 0..11. INFO3@0x017a4 shows GStr[0x621a+5+delta] (affinity_level_names). Orb items 286..292 prove the columns, e.g. 地脈の宝珠 writes column 2 = +1 and 万能の宝珠 writes columns 1..5. Column 0 is never written. Columns: 0=unused, 1=physical, 2=earth, 3=ice, 4=fire, 5=lightning, 6=holy, 7=dark. |
| `0x2e39b8` | item_character_whitelist | high | investigation | SETARMSTATE@0x000a7/0x000e2: if column 0 is set, the item is usable only when one of the 5 columns equals the current character id (0x2d2845). Costume 101 lists Will (1), 111 lists Yuela (2), and the twin nutrient drinks 572..575 list characters 14 and 20. |
| `0x2e60c8` | item_species_restriction_mask | med | investigation | SETARMSTATE@0x0010f: when nonzero, the item is unusable unless the bit for the character's species (0x2b5edc) is set. No authored item writes it. |
| `0x2e6898` | item_sex_restriction_mask | med | investigation | SETARMSTATE@0x0014c: the item is unusable unless the mask bit selected by the character's 0x2b62c4 value is set (same pattern as Himegari item_sex_restriction_mask). Costumes carry it: Will's costumes 2, female characters' costumes 4. |
| `0x2e7068` | item_cursed_flag | high | investigation | FELLOW@0x00ff1/0x010b1 refuses to unequip with message '<name>は外すことが出来ない…'. USEITEM@0x00194..0x001ca with 破術の呪札 (item 243) strips every equipped item that has this flag. Set on 19 dark/'Hetares' weapons and accessories. |
| `0x2e7838` | item_condition_level_deltas | high | investigation | USEITEM@0x004fd passes each nonzero column to ADDILLSUB as (condition id, delta). For example 女神の滴 writes -5 to columns 6..13 and the nutrient drinks write +n to 20 and 22. CALCDMG@0x01360 sums the attacker's slot-0 weapon row with the skill row (skill_condition_levels) and passes each nonzero sum to CALCILL as an on-hit infliction (bow 1327: column 7 confusion = 3). Columns are ILINIT condition ids. Columns: 1=instant_death, 2=hp_drain, 3=sp_drain, 4=fs_drain, 5=curse, 6=charm, 7=confusion, 8=paralysis, 9=poison, 10=water_flow, 11=strong_acid, 12=inflammation, 13=fear, 14=hp_regeneration, 15=sp_regeneration, 16=fs_regeneration, 17=exaltation, 18=accuracy_up, 19=evasion_up, 20=physical_attack_up, 21=physical_defense_up, 22=magic_attack_up, 23=magic_defense_up, 24=speed_up, 25=critical_up, 26=movement_down, 27=no_return, 28=injury. |
| `0x2f6298` | item_equipped_condition_levels | high | investigation | EQUIP@0x000d9 adds every equipped item's row into the unit baseline condition levels 0x618fc (clamped 0..5) and then calls ADDILLSUB. Populated columns are 14..17 and 19 (regeneration, SP/FS regeneration, exaltation, evasion-up). Columns: 1=instant_death, 2=hp_drain, 3=sp_drain, 4=fs_drain, 5=curse, 6=charm, 7=confusion, 8=paralysis, 9=poison, 10=water_flow, 11=strong_acid, 12=inflammation, 13=fear, 14=hp_regeneration, 15=sp_regeneration, 16=fs_regeneration, 17=exaltation, 18=accuracy_up, 19=evasion_up, 20=physical_attack_up, 21=physical_defense_up, 22=magic_attack_up, 23=magic_defense_up, 24=speed_up, 25=critical_up, 26=movement_down, 27=no_return, 28=injury. |
| `0x304cf8` | item_granted_skill_id | high | investigation | CALCREVISE@0x0038f writes it into unit skill slot 4+equip_slot (0x546a2). DRAWTOOLTIP@0x00200 and INFO3@0x01923 show GStr[0x5998+id] (skill_names) and branch on skill_category. Accessory descriptions match, e.g. 狂戦士のお守り 'adds 凶暴化Ⅰ'. |
| `0x3054c8` | item_stat_modifiers | high | investigation | CALCARM@0x00017 copies the row to work 0x2a9b79, and CALCREVISE@0x00341 adds it into unit stats 0x54062[14]. INFO3@0x017c9.. prints the columns. Orb items prove the indices: 271..273 HP/SP/FS write 10/11/12, 274..279 write 2..7, 281 '接客' writes 9 and 282 '移動' writes 8. Weapon summaries confirm 命中 = column 0. Growth stones 261..269 hard-code the same indices in USEITEM@0x00016.. (10,11,12,2,3,6,4,5,7). FELLOW@0x012f0 treats column 13 of the unit stats as skill capacity. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=agility, 7=luck, 8=movement, 9=customer_service, 10=max_hp, 11=max_sp, 12=max_fs, 13=skill_capacity. |
| `0x30c228` | item_critical_rate_bonus | med | investigation | CALCREVISE@0x0036f adds it into unit 0x54670, clamped -99..200. CALCBTPARAM@0x0036e adds 0x54670 plus luck into the critical value 0xf29d4. INFO3@0x018e9 and DRAWTOOLTIP display it. Mostly weapons, plus a few costumes. |
| `0x30c9f8` | item_resource_recovery_amounts | high | investigation | USEITEM@0x00312 adds each column to the unit's current resources 0x545da[3], clamped between the minimum 0x2a2da4 and the maximum (unit stats columns 10..12). Columns: 0=hp, 1=sp, 2=fs. |
| `0x30e168` | item_recovery_variance | med | investigation | USEITEM@0x00344: when nonzero, the recovery amount is randomised by random-modulo(2*v+1)-v. No authored item writes it. |
| `0x30e938` | item_battle_animation_id | high | investigation | When the action has no skill animation, CALCDMG@0x001ea writes the slot-0 weapon value into selected_battle_animation_id (0x3b3b61). Values group by weapon family, e.g. 10 hammers, 17 bows, 22 gloves. |
| `0x30f108` | item_prices | high | investigation | SHOP@0x00e34/0x02687 charges column 0 on purchase. SHOP@0x00e68/0x0102c, ADDITEM@0x00056, USEITEM@0x001fd and CHANGEARM@0x000f3 credit column 1 to money 0x7ea39, including auto-selling overflow above 999 owned. SALE@0x00f5a/0x014cb and CALCSETTLE@0x004de multiply column 2 by the markup percent 0x13f86f /100 for the player's shop. Columns: 0=buy, 1=sell, 2=retail_base. |
| `0x310878` | item_furniture_area | med | investigation | Furniture only; SELORN reads it when filtering placeable furniture. Values match the summaries '『店舗』配置家具' and similar. |
| `0x311048` | item_furniture_exclusive_group | low | investigation | SELORN@0x00ae1 checks it against two placement-state flags, which suggests one-per-group placement restrictions. Unconfirmed. |
| `0x311818` | item_furniture_width | med | investigation | Furniture footprint read by SELORN, DRAWBUILD, ARRANGE and SETSTUDIOCH. 露天風呂セット (described as 2×2) stores 2 here and in item_furniture_height. |
| `0x311fe8` | item_furniture_height | med | investigation | Paired with item_furniture_width. |
| `0x3127b8` | item_garden_production_period | med | investigation | CHECKREWARD@0x0017a divides accumulated garden points by this value to get the number produced and keeps the remainder (mod). |
| `0x312f88` | item_garden_product_item_id | med | investigation | CHECKREWARD@0x001de adds the produced count to the 0x3c9cd1 stock at this item id. Examples: the cypress plantation produces 603 and the hot-spring set 234. |
| `0x313758` | item_furniture_sales_count_bonus | med | investigation | SETSTUDIO sums this field over placed furniture (SELORN also reads it). Populated items' ITMES descriptions advertise '販売数UP'. The column order in SETSTUDIO is 0x313758, 0x313f28, 0x3146f8, 0x314ec8, 0x315698, 0x315e68, 0x316638, 0x316e08, 0x3175d8, 0x317da8. |
| `0x313f28` | item_furniture_purchase_desire_bonus | med | investigation | SETSTUDIO sums this field over placed furniture (SELORN also reads it). Populated items' ITMES descriptions advertise '購買欲UP'. The column order in SETSTUDIO is 0x313758, 0x313f28, 0x3146f8, 0x314ec8, 0x315698, 0x315e68, 0x316638, 0x316e08, 0x3175d8, 0x317da8. |
| `0x3146f8` | item_furniture_visit_rate_bonus | med | investigation | SETSTUDIO sums this field over placed furniture (SELORN also reads it). Populated items' ITMES descriptions advertise '来店率UP'. The column order in SETSTUDIO is 0x313758, 0x313f28, 0x3146f8, 0x314ec8, 0x315698, 0x315e68, 0x316638, 0x316e08, 0x3175d8, 0x317da8. |
| `0x314ec8` | item_furniture_high_price_bonus | med | investigation | SETSTUDIO sums this field over placed furniture (SELORN also reads it). Populated items' ITMES descriptions advertise '高値段UP'. The column order in SETSTUDIO is 0x313758, 0x313f28, 0x3146f8, 0x314ec8, 0x315698, 0x315e68, 0x316638, 0x316e08, 0x3175d8, 0x317da8. |
| `0x315698` | item_furniture_equipment_bonus | med | investigation | SETSTUDIO sums this field over placed furniture (SELORN also reads it). Populated items' ITMES descriptions advertise '設備UP'. The column order in SETSTUDIO is 0x313758, 0x313f28, 0x3146f8, 0x314ec8, 0x315698, 0x315e68, 0x316638, 0x316e08, 0x3175d8, 0x317da8. |
| `0x315e68` | item_furniture_information_bonus | med | investigation | SETSTUDIO sums this field over placed furniture (SELORN also reads it). Populated items' ITMES descriptions advertise '情報UP'. The column order in SETSTUDIO is 0x313758, 0x313f28, 0x3146f8, 0x314ec8, 0x315698, 0x315e68, 0x316638, 0x316e08, 0x3175d8, 0x317da8. |
| `0x316638` | item_furniture_mutation_bonus | med | investigation | SETSTUDIO sums this field over placed furniture (SELORN also reads it). Populated items' ITMES descriptions advertise '変異UP'. The column order in SETSTUDIO is 0x313758, 0x313f28, 0x3146f8, 0x314ec8, 0x315698, 0x315e68, 0x316638, 0x316e08, 0x3175d8, 0x317da8. |
| `0x316e08` | item_furniture_knowledge_bonus | med | investigation | SETSTUDIO sums this field over placed furniture (SELORN also reads it). Populated items' ITMES descriptions advertise '知識UP'. The column order in SETSTUDIO is 0x313758, 0x313f28, 0x3146f8, 0x314ec8, 0x315698, 0x315e68, 0x316638, 0x316e08, 0x3175d8, 0x317da8. |
| `0x3175d8` | item_furniture_staff_count_bonus | med | investigation | SETSTUDIO sums this field over placed furniture (SELORN also reads it). Populated items' ITMES descriptions advertise '店員数UP'. The column order in SETSTUDIO is 0x313758, 0x313f28, 0x3146f8, 0x314ec8, 0x315698, 0x315e68, 0x316638, 0x316e08, 0x3175d8, 0x317da8. |
| `0x317da8` | item_furniture_garden_bonus | low | investigation | The last field SETSTUDIO sums (SETSTUDIO@0x00180), populated on garden furniture. The stat it feeds is not identified. |
| `0x318578` | item_field_use_target_kind | low | investigation | Set only on the field 創石 stones: 201/203 return or teleport an ally unit and store 2; 211/212 seal a vortex or open a door and store 1. FELLOW reads it when choosing the target picker. |
| `0x319ce8` | item_usability_flags | med | investigation | SETARMSTATE rebuilds it for character 0x2d2845. bit0 combines category mask, whitelist, species/sex masks and rank; bit1 marks currently equipped items and socketed gems. SETTOOLLIST@0x001c9 consumes bit0. |
| `0x31a4b9` | item_growth_enabled | high | investigation | FIELD@0x67f0 calls CHECKIMPROVE when it is 1; COMMITBTL@0x483..0x4c0 adds 1 item_growth_exp per battle to each such item equipped by a party member (0x53c48[unit*3+slot]) and calls CHECKIMPROVE; DRAWTOOLTIP, GAMECLEAR, GAMESTART also read it. |
| `0x31ac89` | item_growth_exp_thresholds | high | investigation | CHECKIMPROVE@0xc levels up when item_growth_exp >= threshold; CHECKRECIPE@0x142 treats 0 at the current level as 'fully grown'; DRAWTOOLTIP@0x373/0x425 draws level pips and the progress gauge. |
| `0x320279` | item_growth_affinity_bonuses | high | investigation | CALCARM@0x356 sums rows 0..level-1 into equipment stat block 0x2a9b71[0..7]. Also observed: [items decode, named item_upgrade_affinity_bonuses] See item_upgrade_step_counts (CALCARM@0x00356). |
| `0x347379` | item_growth_stat_bonuses | high | investigation | CALCARM@0x3a4 sums rows 0..level-1 into 0x2a9b79[0..13]. IMINIT writes it with lookup-array-2d through local pointers (mul/add/lookup-array-2d/mov quads). Also observed: [items decode, named item_upgrade_stat_bonuses] See item_upgrade_step_counts (CALCARM@0x003a4). |
| `0x38b939` | item_growth_mastery_reward_items | high | investigation | CHECKIMPROVE@0x124 adds one via ADDITEM and prints 『X』を手に入れた. |
| `0x38c109` | item_growth_mastery_story_flags | high | investigation | CHECKIMPROVE@0x175 sets 0x50d[id-1]=1; GAMESTART@0xba7 re-reads it. |
| `0x38c8d9` | alchemy_recipe_output_item_ids | high | investigation | CHECKRECIPE@0x24 treats a nonzero cell as a populated recipe while scanning ids 0..1999. ALCHEMY@0x1224/0x1275 groups recipes by the output item's category (0x2dd428 -> 0x3c9be8), ALCHEMY@0xfed adds/subtracts planned output count per item, ALCHEMY@0xe19 names it in the mutation message, INFO4@0xeab/0xf32 draws the output item's name, icon (0x2db4e8) and description script (0x2d2848). |
| `0x38d0a9` | alchemy_recipe_quest_markers | med | investigation | ALCHEMY@0x1d4d: when the recipe has never been crafted (alchemy_recipe_craft_counts == 0), a marker of 1 and an output item never obtained (0x7f9ef[item] == 0) select an alternate list badge sprite (0x4c0,0x450) instead of the normal 'new' badge (0x326,0x4a1). |
| `0x38d879` | alchemy_recipe_required_synthesis_ranks | high | investigation | ALCHEMY@0x16b7 marks a recipe uncraftable when this value exceeds alchemy_synthesis_rank; INFO4@0x10a8 draws it through the 0x623a rank-letter strings. |
| `0x38e049` | alchemy_recipe_visible_required_flags | high | investigation | ALCHEMY@0x15d3 (label_15b1, list filter): returns 'hidden' if any nonzero cell's flag 0x50d[id-1] != 1. Columns: 0=flag_0, 1=flag_1, 2=flag_2, 3=flag_3, 4=flag_4. |
| `0x390759` | alchemy_recipe_visible_forbidden_flags | high | investigation | ALCHEMY@0x162d (label_15b1): returns 'hidden' if any nonzero cell's flag 0x50d[id-1] == 1. Columns: 0=flag_0, 1=flag_1, 2=flag_2, 3=flag_3, 4=flag_4. |
| `0x393639` | alchemy_recipe_ingredient_item_ids | high | investigation | ALCHEMY@0x102d/0x117f (plan +/-1), @0x16ea (craftability check), @0x1fb9; INFO4@0x10dc draws each ingredient's icon/name; INFO3 reads it for item cross-reference. Columns: 0=ingredient_0, 1=ingredient_1, 2=ingredient_2, 3=ingredient_3. |
| `0x395579` | alchemy_recipe_ingredient_quantities | high | investigation | ALCHEMY@0x1056/0x11a8/0x1713 consumes max(1, qty*(99 - studio_material_saving)/100 + 1) per craft and compares against inventory 0x7ea4f; INFO4@0x11a2 draws the base quantity next to the owned count. Columns: 0=quantity_0, 1=quantity_1, 2=quantity_2, 3=quantity_3. |
| `0x3974b9` | alchemy_recipe_mutation_chances | high | investigation | ALCHEMY@0x995: for each crafted unit, random(100) < chance * studio_mutation_rate / 100 yields one extra alchemy_recipe_mutation_item_ids item; ALCHEMY@0xe2e prints 『X』の合成で突然変異発生！ |
| `0x397c89` | alchemy_recipe_mutation_required_flags | high | investigation | ALCHEMY@0x1841 (label_1809 mutation gate): mutation disabled if any nonzero cell's flag is not set. Columns: 0=flag_0, 1=flag_1. |
| `0x398c29` | alchemy_recipe_mutation_forbidden_flags | high | investigation | ALCHEMY@0x187e (label_1809). |
| `0x3993f9` | alchemy_recipe_mutation_item_ids | high | investigation | ALCHEMY@0x180e gates mutation on nonzero; ALCHEMY@0x92f/0xa2d adds the mutated item to the result and awards synthesis exp for it; ALCHEMY@0xe48 prints 『Y』をN個手に入れた！ |
| `0x399bc9` | alchemy_recipe_unlock_craftsman_ranks | high | investigation | CHECKRECIPE@0x55 skips the recipe while this exceeds studio_craftsman_rank; INFO4@0xfdd draws it via the 0x6226 title strings. |
| `0x39a399` | alchemy_recipe_unlock_synthesis_ranks | high | investigation | CHECKRECIPE@0x6b skips the recipe while this exceeds alchemy_synthesis_rank; INFO4@0x1023 draws it via the 0x623a rank letters. |
| `0x39ab69` | alchemy_recipe_unlock_grown_item_ids | high | investigation | CHECKRECIPE@0x121..0x14d: looks up the item's current growth level 0x8098f and its next-level threshold 0x31ac89[item*11+level]; a nonzero threshold (not yet maxed) blocks learning. |
| `0x39b339` | alchemy_recipe_unlock_prerequisite_recipes | high | investigation | CHECKRECIPE@0x155 tests the snapshot of alchemy_recipe_learned_flags taken at entry (local copy of 0x8115f). |
| `0x39bb09` | alchemy_recipe_unlock_shop_levels | high | investigation | CHECKRECIPE@0x81: blocks while value > studio_room_levels[0] + 1. Matches 店舗LVn in alchemy_recipe_unlock_texts. |
| `0x39c2d9` | alchemy_recipe_unlock_workshop_levels | high | investigation | CHECKRECIPE@0x9e: blocks while value > studio_room_levels[1] + 1. |
| `0x39caa9` | alchemy_recipe_unlock_quarters_levels | high | investigation | CHECKRECIPE@0xbb: blocks while value > studio_room_levels[2] + 1. |
| `0x39d279` | alchemy_recipe_unlock_room4_levels | high | investigation | CHECKRECIPE@0xd8: blocks while value > studio_room_levels[3] + 1. |
| `0x39da49` | alchemy_recipe_unlock_knowledge | high | investigation | CHECKRECIPE@0xf5 blocks while value > studio_knowledge (0x13f873); every 知識N in alchemy_recipe_unlock_texts matches this cell. |
| `0x39e219` | alchemy_recipe_unlock_sense | high | investigation | CHECKRECIPE@0x10b blocks while value > studio_sense (0x13f876); every センスN in alchemy_recipe_unlock_texts matches this cell. |
| `0x39e9e9` | alchemy_recipe_unlock_story_flags | high | investigation | CHECKRECIPE@0x19d loops k<3 and blocks if any nonzero flag 0x50d[id-1] != 1. Columns: 0=flag_0, 1=flag_1, 2=flag_2. |
| `0x3a0159` | alchemy_recipe_evaluation_rewards | high | investigation | ALCHEMY@0xba6: if alchemy_recipe_craft_counts[r]==0 adds the full value once and counts the rest at value/10 each, accumulates into studio_evaluation (clamped ±9,999,999) and prints 評価＋N. |
| `0x3a0929` | costume_board_background_asset_id | med | investigation | COINIT writes it per costume; ENHANCE draws the board background from it. |
| `0x3a092a` | costume_board_grid | high | investigation | COINIT zeroes 33*33 cells then copies rows 1..30, cols 1..30. CALCARM@0xe5 maps node index n (15x15) to cell [2*(n/15+1)][2*(n%15+1)] and uses value-100 to index 0x3a0e8d/0x3a0ee7/0x3a0f41 (AFINIT); ENHANCE@0xed8 walks links via INIT2's neighbour offsets 0x2a2d9a/0x2a2d9f. |
| `0x3a0d6b` | costume_board_node_unlock_flags | high | investigation | ENHANCE@0xe5a/0xf83 sets 0x50d[flag-1]=1 when the node at tile (x,y) unlocks; GAMESTART@0xd0f reads it. |
| `0x3a0e8d` | costume_enhancement_effect_types | high | investigation | CALCARM@0x00162: type 1 adds the amount to calc_item_affinity_totals[target], type 2 to calc_item_stat_totals[target] and type 3 adds a gem socket. ENHANCE and SELSEWUP display it. |
| `0x3a0ee7` | costume_enhancement_effect_targets | high | investigation | CALCARM@0x00142. |
| `0x3a0f41` | costume_enhancement_effect_amounts | high | investigation | CALCARM@0x00152. |
| `0x3a0f9b` | costume_enhancement_tiers | low | investigation | ENHANCE@0x01c2a reads it (likely the tier required to unlock). |
| `0x3a0fb9` | costume_enhancement_costs | med | investigation | ENHANCE@0x016d8 reads it (likely the money cost). |
| `0x3a0fd7` | costume_enhancement_material_item_ids | low | inference | Set on enhancements 11..26. The consumer was not traced; the role is inferred from the values. |
| `0x3a104f` | costume_enhancement_material_quantities | low | inference | Paired with costume_enhancement_material_item_ids. |
| `0x3a10c8` | skill_icon_asset_id | med | investigation | Read by FELLOW and INFO6 for skill rows. |
| `0x3a14b0` | skill_sort_key | high | investigation | Sort key for sort-indices-by-key-sum over 0x3e8 skills in BTL@0x003e0, CALCDMG@0x005b8, INFO6@0x00716 and SETTOOLLIST@0x00323. |
| `0x3a1898` | skill_upgrade_family_id | high | investigation | CALCREVISE@0x004e9: when a unit holds several skills of one family, the one with the higher skill_equip_cost wins and the possessed flag of the weaker one (0x548c8) is cleared. |
| `0x3a1c80` | skill_category | high | investigation | Set on every skill; the most-read SKINIT field. CALCSCOPE@0x000d9 routes categories 9/10/12 to the map-area list, 7/8 to the support list and the rest to the attack list. CALCBTPARAM@0x00074 treats 7/8/12 as heal/support (no hit roll, negative damage) and 6/10 as magic (matk/mdef instead of patk/pdef). CALCREVISE@0x00439 applies passive range extension only for 5..8. Titles in SKMES confirm the labels. |
| `0x3a2068` | skill_catalog_visible | med | investigation | INFO6@0x00748 lists only skills where this is nonzero in the skill encyclopedia. Set on 441 of 451 skills. |
| `0x3a2450` | skill_equip_cost | high | investigation | FELLOW@0x012f0/0x019fb sums the cost of the character's learned skills plus the candidate and rejects the result if it exceeds unit stat 13 (skill capacity, 0x5406f). CALCREVISE@0x0050a uses it to rank skills within an upgrade family. |
| `0x3a2838` | skill_bundled_skill_ids | high | investigation | CALCREVISE@0x00576 queues every listed id that the unit does not already hold, so composite skills unpack into their parts (飛行潜水 = 1 飛行 + 2 潜水; 四属性耐性 = four resistances). Story scripts (e.g. SC0450@0x08b74) also read it. |
| `0x3a3bc0` | skill_min_range_encoded | high | investigation | CALCSCOPE@0x00143 and CALCREVISE@0x00463 subtract 1 before use. '射N' summaries match max-1, e.g. 色欲の接触 (2,6) = 射5. |
| `0x3a3fa8` | skill_max_range_encoded | high | investigation | Pair of skill_min_range_encoded (CALCSCOPE@0x00172). |
| `0x3a4390` | skill_required_weapon_category_mask | high | investigation | CALCSCOPE@0x003f2: the skill is offered only if the mask contains the category bit of the unit's slot-0 weapon or its species default weapon (0x2b91a4). Examples: 流星の光槍 = 524288 (spear) and 炎刀一撃 = 8192 (sword). |
| `0x3a4778` | skill_area_target_mode | low | investigation | On map-area skills, SELBOMB@0x00d89 draws ○ when the value is 1 and × otherwise, and FIELD@0x0b595 picks the highlight colour by it. The exact meaning (friendly fire or side) is unconfirmed. |
| `0x3a4b60` | skill_area_shape_id | med | investigation | BOMBSEEK@0x0000e branches on it (3, 4, 5 use inline offset tables) to compute the affected cells of a map-area skill. |
| `0x3a4f48` | skill_area_cooldown_turns | med | investigation | FIELD@0x05ebf adds it to the unit's cooldown counter 0x6380a after use. CALCSCOPE@0x003d5 blocks category-9+ skills while that counter is above 0. SELBOMB@0x00db3 and INFO6@0x00fd8 display it. |
| `0x3a5330` | skill_attack_element_encoded | high | investigation | CALCBTPARAM@0x0016a and BTRTN@0x000dc subtract 1 to get the attack element, otherwise falling back to item_attack_element. The summaries agree (流星の光槍 7 = 神, 炎刀一撃 5 = 火, 石弾 3 = 地). |
| `0x3a5718` | skill_condition_levels | high | investigation | CALCDMG@0x0136b adds it to the weapon's item_condition_level_deltas and passes each nonzero sum to CALCILL as an on-hit infliction. FIELD and INFO6 read it too. 炎刀一撃 inflicts column 12 inflammation, 大海嘯Ｍ column 10 water flow and 強化呪付・攻 columns 20/22 (physical/magic attack up). Columns: 1=instant_death, 2=hp_drain, 3=sp_drain, 4=fs_drain, 5=curse, 6=charm, 7=confusion, 8=paralysis, 9=poison, 10=water_flow, 11=strong_acid, 12=inflammation, 13=fear, 14=hp_regeneration, 15=sp_regeneration, 16=fs_regeneration, 17=exaltation, 18=accuracy_up, 19=evasion_up, 20=physical_attack_up, 21=physical_defense_up, 22=magic_attack_up, 23=magic_defense_up, 24=speed_up, 25=critical_up, 26=movement_down, 27=no_return, 28=injury. |
| `0x3acc48` | skill_combat_stat_deltas | high | investigation | CALCBTPARAM@0x00512.. adds column 2 or 4 (attack, as for the unit) to the attack power, column 3 or 5 to defense, column 6 to agility, column 0 to hit and column 7 to critical, and subtracts the opponent's columns 1 and 7. CALCDMG and CALCSUPPORT read it too. Summaries '攻N'/'魔N' equal columns 2 and 4, and heals store accuracy 99. Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=agility, 7=luck, 8=movement, 9=customer_service. |
| `0x3af358` | skill_critical_rate_bonus | high | investigation | CALCBTPARAM@0x005c0 adds it to the critical value 0xf29d4. Also read by CALCDMG and CALCSUPPORT. |
| `0x3af740` | skill_resource_deltas | high | investigation | CALCSCOPE@0x0036d rejects the skill if current resources 0x545da plus the delta would drop below the minimum. CALCBTPARAM@0x001d7 uses -column0 as the heal amount for categories 7/8/12. Summaries 'SPn'/'回復n' match. Columns: 0=hp, 1=sp, 2=fs. |
| `0x3b02f8` | skill_proc_chance_percent | high | investigation | BTL@0x00cb7.. rolls 0x2a3236 (a random 0..99 stream) against it to set battle_triggered_passive_skill_flags. Set on passives such as 先制攻撃Ⅰ = 10. |
| `0x3b06e0` | skill_battle_animation_id | high | investigation | CALCDMG@0x001c4 writes it into selected_battle_animation_id when the action uses a skill. BTL reads it for passive reactions. |
| `0x3b0ac8` | skill_combo_partner_character_ids | high | investigation | CALCSCOPE@0x00453 offers the skill only when the acting character equals one column and a living ally of the other character is adjacent with enough resources. Set on the ☆ combo skills, e.g. ☆双精の乱風射 = 14, 20. |
| `0x3b1298` | skill_handler_script_id | med | investigation | FELLOW and INFO6 call it for the skill description; same pattern as item_handler_script_id. |
| `0x3b1680` | skill_area_effect_texture_asset_id | high | investigation | FIELD@0x06399 calls set-texture with it and slices the texture into 5x4 frames for the map-area effect. |
| `0x3b1a68` | skill_area_effect_sound_asset_id | high | investigation | FIELD@0x066f8 plays it with play-sound-effect during the map-area effect. |
| `0x3b1e50` | skill_area_effect_blend_mode | low | investigation | FIELD@0x06495 passes it as the gfx-draw-color mode of the area effect sprite (1 = additive in Himegari). |
| `0x3b223a` | condition_susceptibility_group | high | investigation | CALCILL@0x00089 and ADDILLSUB@0x00013 use it as the column of species_condition_susceptibility_percent[species]; a value of 0 there means immune. Groups: 1 curse, 2 death/fear, 3 charm/confusion, 4 paralysis/poison, 5 drains, 6 water/acid/inflammation, 7 buffs/misc. |
| `0x3b2258` | condition_immunity_skill_id | high | investigation | ADDILLSUB@0x00033: if the unit holds this skill (0x548c8), the condition is ignored. Examples: water flow → 2 潜水, inflammation → 5 耐熱, instant death → 151, charm → 153, poison → 30. |
| `0x3b2276` | condition_can_affect_bosses | high | investigation | CALCILL@0x00019: a zero value with a nonzero boss flag 0x2b66ac on the target unit skips the infliction (same as Himegari). |
| `0x3b2294` | condition_cleared_by_recover | high | investigation | RECOVER@0x00071 resets only flagged conditions to the equipment baseline. EQUIP@0x0014e zeroes the delta for flagged conditions when re-equipping. |
| `0x3b22b2` | condition_icon_ids | high | investigation | DRAWCHSTATE@0x00164 and STATUS@0x01bed choose the status icon by it. |
| `0x3b22d0` | condition_duration_turns_by_level | high | investigation | ADDILLSUB@0x00164 sets the remaining turns 0x61320 to max(current, duration[level]). For negative deltas it overwrites directly. |
| `0x3b2366` | condition_stat_deltas | high | investigation | CALCREVISE@0x00648 adds [cond][(level-1)*9+k] into unit stats 0x54062 columns 0..8. Values fit the names: curse lowers everything, 命中1..5 add accuracy 4..20 to column 0, and 歩低 lowers movement (column 8). Columns: 0=accuracy, 1=evasion, 2=physical_attack, 3=physical_defense, 4=magic_attack, 5=magic_defense, 6=agility, 7=luck, 8=movement. |
| `0x3b28ac` | condition_critical_rate_deltas | high | investigation | CALCREVISE@0x0069a adds it into unit critical 0x54670. Values: curse -25, exaltation +10, 痛打 +10. |
| `0x3b28ca` | condition_resource_deltas | med | investigation | Read by CALCDMG@0x01481 (drain on hit) and FIELD@0x09775 (per-turn ticks). Regeneration 14..16 give +1..5. |
| `0x3b2a8c` | affinity_damage_percent_by_level | high | investigation | CALCBTPARAM@0x007a3 multiplies attack power by [defender affinity level for the attack element]; a negative value (吸収) heals. BTRTN@0x0012e, SELACT and the RTN_M05x scripts use it to estimate damage. |
| `0x3b2a98` | species_condition_susceptibility_percent | high | investigation | ADDILLSUB@0x0001a and CALCILL@0x00090 index [species 0x2b5edc][condition_susceptibility_group]. AFINIT fills rows 1..16 with copy-inline-int-array. |
| `0x3b2b60` | weapon_rank_exp_thresholds | high | investigation | ADDEXP@0x004c8 raises character weapon rank 0x7e1a1 (max 6) while weapon EXP 0x7e205 reaches the next threshold. FELLOWSTATUS displays it. item_rank is compared against this rank. |
| `0x3b2b67` | alchemy_synthesis_rank_exp_thresholds | high | investigation | ALCHEMY@0xc53 and DRAWSTUDIOLV@0xe6. Also observed: [items decode, named studio_level_exp_thresholds] ALCHEMY@0x00c53 levels the workshop (0x7ea4a) from studio EXP 0x7ea3f. DRAWSTUDIOLV@0x000e6 and INFO1 draw the progress bar. |
| `0x3b2b6e` | alchemy_synthesis_rank_up_evaluation_bonus | high | investigation | ALCHEMY@0xc7b adds it to the rank-up evaluation award. Also observed: [items decode, named studio_level_up_rewards] ALCHEMY@0x00c7b accumulates these per level gained. The receiving resource is not identified. |
| `0x3b2b75` | alchemy_item_rank_exp | high | investigation | ALCHEMY@0xa63/0xb00. Also observed: [items decode, named alchemy_exp_by_item_rank] ALCHEMY@0x00a63 adds [item_rank of the crafted item] plus a scaled bonus to the alchemy result. |
| `0x3b2b7d` | event_delta_scalars | low | investigation | Individual cells are loaded into the generic delta argument 0xfec0c by FIELD (@0x04c6f, 0x09b6c, 0x07707, 0x077c3, 0x07b25, 0x0cc28..0x0cd39) and COMMITBTL (@0x00175, 0x0022b). They are reward/penalty tuning constants with unconfirmed targets. |
| `0x3b2b90` | affinity_tuning_percent_table | low | investigation | Indexed by 0xfd42d in STATUS@0x01a2f, ORGANIZE, SETCH and SHOWCC as a percent multiplier on a character value. Purpose not identified. |
| `0x3b2b95` | shop_level_scaled_table | low | investigation | CALCSETTLE@0x00014 copies [0x7ea49] into 0x13fba7, and CALCSETTLE@0x000cd adds index 20 (0x3b2ba9). Probably a shop-level settlement table. |
| `0x3b2bb6` | battle_triggered_passive_skill_flags | med | investigation | BTL@0x00ccc sets it after skill_proc_chance_percent succeeds and later clears it. |
| `0x3b3b63` | battle_animation_effect_ids | high | investigation | BTANINIT2 writes it at boot. BTANINIT@0x00071 materializes each slot through its effect dispatch. Same layout as Himegari 0x15288e. |
| `0x3b52d3` | battle_animation_effect_start_delays_ms | med | investigation | Pair of battle_animation_effect_ids, at +6000 cells (same as Himegari). |
| `0x3b6a43` | battle_animation_duration_ms | high | investigation | BTL@0x03467 reads [selected_battle_animation_id]. |
| `0x3b6e2b` | battle_effect_visual_asset_ids | high | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e31` | battle_effect_visual_mode_ids | high | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e37` | battle_effect_additive_blend_flags | med | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e3d` | battle_effect_widths | med | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e43` | battle_effect_heights | med | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e49` | battle_effect_atlas_column_counts | med | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e4f` | battle_effect_atlas_row_counts | med | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e55` | battle_effect_atlas_frame_counts | med | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e5b` | battle_effect_durations_ms | med | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e61` | battle_effect_combatant_anchor_flags | high | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e67` | battle_effect_offset_x_pixels | med | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e6d` | battle_effect_offset_y_pixels | med | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e73` | battle_effect_sound_asset_ids | med | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e79` | battle_effect_sound_delays_ms | med | investigation | BTANINIT's per-slot effect work array; the copy-to-global 6 at BTANINIT entry clears it. Same order and layout as Himegari BATTLE_EFFECT_WORK_ARRAYS, shifted by +0x2612d5. BTL reads it (e.g. visual id and mode at BTL@0x03cd0/0x03cde). |
| `0x3b6e7f` | battle_effect_hit_pulse_offsets_ms | med | investigation | BTANINIT sets [slot][0] = 100 on most effects. Analog of Himegari 0x155baa. |
| `0x3b6e92` | quest_requester_icon_asset_ids | high | investigation | INFO2@0xc31/0xc54 set-texture's the requester icon onto each journal card, centred via get-texture-size. |
| `0x3b7662` | quest_journal_sort_keys | high | investigation | INFO2@0x679 sort-indices-by-key-sum over the first 1000 quest ids; reward notices (>=1000) are never listed. |
| `0x3b7e32` | quest_deadline_days | high | investigation | CHECKCONTRACT@0x264 fails a posted quest when quest_elapsed_days + expedition_elapsed_days > deadline; INFO2@0x107b draws "<deadline>日/<elapsed>日経過", 終了 once cleared/failed, なし when 0. 25 quests populate it. |
| `0x3b8602` | quest_prerequisite_quest_ids | high | investigation | CHECKCONTRACT@0xbe: a quest is not posted unless the prerequisite quest's state & 0xf == 0xd (posted+cleared+settled). No shipped layer populates it; chaining is done with flags instead. |
| `0x3b8dd2` | quest_appear_required_flag_ids | high | investigation | CHECKCONTRACT@0x10d: every nonzero column must have story_event_flags[v-1]==1 before the quest is posted. Base quests typically pair a story flag with an 8xxx 'quest offered' scene flag; placeholder ids 1021..1040 use 10000/9999 (debug-only, never posted). Columns: 0=required_flag_1, 1=required_flag_2, 2=required_flag_3. |
| `0x3ba542` | quest_appear_blocking_flag_ids | high | investigation | CHECKCONTRACT@0x167: the quest is not posted while any nonzero column's flag is set. $3$ authors only column 2, pointing at another quest's failure-result flag (e.g. quest 472 is blocked by flag 8920 = quest 471 expired). Columns: 0=blocking_flag_1, 1=blocking_flag_2, 2=blocking_flag_3. |
| `0x3bbcb2` | quest_clear_flag_ids | high | investigation | CHECKCONTRACT@0x1f4: once at least one column is nonzero and all nonzero flags are set, the quest gains state bit2 (cleared), freezes its elapsed days and raises quest_notification_bits bit2. Reward notices (ids >= 1000) skip this and are marked cleared on posting (CHECKCONTRACT@0xae). Columns: 0=clear_flag_1, 1=clear_flag_2, 2=clear_flag_3. |
| `0x3bd422` | quest_failure_trigger_flag_ids | high | investigation | CHECKCONTRACT@0x308: any set flag fails the quest (state bit1) — typically chapter-entry flags (151 = 4章突入, 201 = 5章, 401 = 終章) or arena-loss flag 8919. Columns: 0=failure_flag_1, 1=failure_flag_2, 2=failure_flag_3. |
| `0x3beb92` | quest_failure_result_flag_ids | high | investigation | CHECKCONTRACT@0x2bb/0x367 sets story_event_flags[v-1]=1 when the quest fails by deadline or failure trigger, letting scenes and other quests' blocking flags react. |
| `0x3bf362` | quest_difficulty_stars | high | investigation | INFO2@0xfd1 draws ten star cells, lit while index < value. Authored for reward notices too, where nothing reads it. |
| `0x3bfb32` | quest_reputation_rewards | high | investigation | RECEIPT@0x5e6 displays column (failed?1:0); RECEIPT@0x656/0x6b4 adds it to player_reputation with +/-9,999,999 clamps. Columns: 0=on_success, 1=on_failure. |
| `0x3c0ad2` | quest_reward_money | high | investigation | INFO2@0x118d shows it as お金 ... Ｓ; RECEIPT@0x40a shows and RECEIPT@0x70f pays it into player_money on success only. |
| `0x3c12a2` | quest_reward_item_ids | high | investigation | INFO2@0x11e5 lists them; RECEIPT@0x471 shows name/qty/owned count (0x7ea4f) with a NEW badge when 0x7f9ef[item]==0, and RECEIPT@0x77f awards each through ADDITEM (0x2d2846 item, 0x2d2847 qty) on success. Columns: 0=item_1, 1=item_2, 2=item_3, 3=item_4. |
| `0x3c31e2` | quest_reward_item_quantities | high | investigation | Parallel quantity row for quest_reward_item_ids (INFO2@0x11f7, RECEIPT@0x483/0x7c1). Columns: 0=qty_1, 1=qty_2, 2=qty_3, 3=qty_4. |
| `0x3c5122` | arena_battle_categories | high | investigation | ARENA@0x9a6 skips zero; ARENA@0x9e1 appends the battle to per-tab list 0x40f[category-1]. |
| `0x3c5186` | arena_battle_display_numbers | high | investigation | ARENA@0x13ad draw-formatted-integer. |
| `0x3c51ea` | arena_battle_party_sizes | med | investigation | ARENA@0x13fc / 0x15c5 displayed; matches descriptions ('３対３の戦闘', 'ウィル専用' = 1). |
| `0x3c524e` | arena_battle_levels | med | investigation | ARENA@0x13e8 / 0x15b1 displayed. |
| `0x3c52b2` | arena_battle_rank_indices | high | investigation | ARENA@0x13d8 / 0x15a1 look up string 0x623a+value. |
| `0x3c5316` | arena_battle_prerequisite_ids | high | investigation | ARENA@0xb4a hides the battle while arena_battle_win_counts[prereq] == 0. |
| `0x3c537a` | arena_battle_required_total_wins | high | investigation | ARENA@0xb75 compares with the total computed at ARENA@0x973. |
| `0x3c53de` | arena_battle_required_studio_ranks | high | investigation | ARENA@0xb99 hides the battle while 0x7ea49 < value (0x7ea49 is the rank DRAWSTUDIOLV@0x172 names via 0x6226). |
| `0x3c5442` | arena_battle_required_unit_ids | high | investigation | ARENA@0xbcc: every nonzero entry must match a roster member (0x6383d via 0x2b6a94 base-unit map) or the battle is hidden. |
| `0x3c57c6` | arena_battle_entry_fees | med | investigation | ARENA@0x754 refuses entry when unwon and fee > money 0x7ea39; ARENA@0x82d subtracts it from money after ORGANIZE confirms; ARENA@0x1425/0x1642 display it (0 once won). |
| `0x3c582a` | arena_battle_required_story_flags | high | investigation | ARENA@0xaa5. |
| `0x3c5956` | arena_battle_forbidden_story_flags | high | investigation | ARENA@0xaff. |
| `0x3c5a82` | arena_battle_labyrinth_numbers | high | investigation | ARENA@0x7a1 / 0x147a: the battle's stage is the STINIT2 stage whose 0x141134 equals this and 0x14151c equals arena_battle_floor_numbers; the stage id goes to 0x378b. |
| `0x3c5ae6` | arena_battle_floor_numbers | high | investigation | ARENA@0x7b6 / 0x148f stage lookup (see arena_battle_labyrinth_numbers). |
| `0x3c5b4c` | shop_catalogue_item_ids | high | investigation | SHOP@0x167a clears it, runs every shop_catalogue_hooks entry (SHINIT and its $n$ layers write slots gated by studio_craftsman_rank or story flags), then compacts nonzero slots into the buy list; buy price 0x30f108[item*3+0], sell price col 1 (SHOP@0xe34/0xe68). |
| `0x3cac75` | objective_sort_keys | high | investigation | INFO1@0x6d4 sort-indices-by-key-sum over 2000 ids, then walks the permutation from the end (newest first). |
| `0x3cb445` | objective_required_flag_ids | high | investigation | INFO1@0x7b6: listed only when every nonzero flag is set. Columns: 0=required_flag_1, 1=required_flag_2, 2=required_flag_3. |
| `0x3ccbb5` | objective_forbidden_flag_ids | high | investigation | INFO1@0x810: hidden while any nonzero flag is set. Columns: 0=forbidden_flag_1, 1=forbidden_flag_2, 2=forbidden_flag_3. |
| `0x3ce325` | objective_complete_flag_ids | high | investigation | INFO1@0x875: when all nonzero flags are set the entry is marked done and drawn grey (INFO1@0xf0f). Columns: 0=complete_flag_1, 1=complete_flag_2, 2=complete_flag_3. |
| `0x3cfa95` | objective_incomplete_override_flag_ids | med | investigation | INFO1@0x8d9: any populated column forces done=1 unless one of its flags is set (which forces done=0). Never authored. |
| `0x3d1205` | objective_message_script_ids | high | investigation | INFO1@0xffa call-script's it with current_objective_id set, rendering the FAMES description. |
| `0x3d19d6` | character_profile_sort_keys | high | investigation | INFO7@0x006e2 sort-indices-by-key over 200 profiles. |
| `0x3d1a9e` | character_profile_unit_ids | high | investigation | INFO7@0x007d8: a linked profile is unlocked only when 0xf5cb3[unit] (persistent unit-met flag) is set. |
| `0x3d1b66` | character_profile_unlock_flag_ids | high | investigation | INFO7@0x0073f..0x007ce: profile visible if any listed flag 0xf35a3[v-1] is set (AND the unit-id condition above). |
| `0x3d1dbe` | character_profile_portrait_x_offsets | high | investigation | INFO7@0x00dbc adds it to the portrait x. |
| `0x3d1e86` | character_profile_portrait_y_offsets | high | investigation | INFO7@0x00dcf adds it to the portrait y. |
| `0x3d1f4e` | character_profile_portrait_asset_ids | high | investigation | INFO7@0x00d7d set-texture, positioned with the x/y offsets below. |
| `0x3d2016` | character_profile_biography_script_ids | high | investigation | INFO7@0x00e9a call-script with selector 0x3d19d5 = profile id; CIMES dispatches on 0x3d19d5 and prints the biography. |
| `0x3d20df` | vocabulary_sort_keys | high | investigation | INFO8@0x6e2 sort-indices-by-key-sum over 200 ids. |
| `0x3d24c7` | vocabulary_unlock_scene_ids | high | investigation | INFO8@0x73f: column 0 zero -> listed; otherwise listed if ANY nonzero v has viewed_scene_flags[v-1] set (loaded from the shared profile at INFO8@0x7b6). Columns: 0=unlock_scene_plus_one_1, 1=unlock_scene_plus_one_2, 2=unlock_scene_plus_one_3. |
| `0x3d3c37` | vocabulary_message_script_ids | high | investigation | INFO8@0xd95 call-script's it to render the description of current_vocabulary_id. |
| `0x3d4180` | scjump_handler_chain | high | investigation | INIT2@0x54 seeds slot0 with SCJUMP; $1$AUTORUN@0x54 appends $1$SCJUMP at slot1; $2$/$3$AUTORUN shift the chain right and insert at slot0. CHECKREWARD@0x2cd, ALLMAP, FIELD, SELSTAGE, STUDIO, SYSTEM4, TOWN call each nonzero entry while scjump_chain_pending==1. One of the 18 per-table hook chains at 0x3d40c0 + 0x20*k. |
| `0x3d4240` | event_cg_variant_handler_chain | high | investigation | INIT2@0x72 / $1$AUTORUN@0x72 register EVINIT handlers; 110 SC scripts and CGMODE@0x1133 call them. EVINIT rules test (G[0x64201+k]&2) && G[0x642c9+k]==form for party characters k=1..4 (form ids 101-102, 111-117, 131-137, 151-157); the last matching rule wins. |
| `0x3d4260` | class_change_rule_script_ids | high | investigation | CALCCC@0x00045 calls every nonzero entry in order. |
| `0x0` | — | low | auto-shape | array |
| `0x5` | — | low | auto-shape | array |
| `GS 0xd2` | — | low | auto-shape | array |
| `0xd7` | — | low | auto-shape | array |
| `0x1a9` | — | low | auto-shape | array |
| `0x34d` | — | low | auto-shape | array |
| `0x41f` | — | low | auto-shape | array |
| `GS 0x2263` | — | low | auto-shape | array |
| `GS 0x2277` | — | low | auto-shape | array |
| `GS 0x2340` | — | low | auto-shape | array |
| `GS 0x2354` | — | low | auto-shape | array |
| `0x24b3` | — | low | auto-shape | array |
| `0x30d9` | — | low | auto-shape | array |
| `0x30ed` | — | low | auto-shape | array |
| `0x3101` | — | low | auto-shape | array |
| `0x3115` | — | low | auto-shape | array |
| `0x3129` | — | low | auto-shape | array |
| `0x3140` | — | low | auto-shape | array |
| `0x36ff` | — | low | auto-shape | array |
| `0x3713` | — | low | auto-shape | array |
| `0x3727` | — | low | auto-shape | array |
| `GS 0x61fe` | — | low | auto-shape | array |
| `GS 0x6241` | — | low | auto-shape | array |
| `0x4f9a9` | — | low | auto-shape | array |
| `0x4fdd1` | — | low | auto-shape | array |
| `0x4fdd6` | — | low | auto-shape | array |
| `0x4fddb` | — | low | auto-shape | array |
| `0x50e90` | — | low | auto-shape | array |
| `0x52c16` | — | low | auto-shape | array |
| `0x52c20` | — | low | auto-shape | array |
| `0x52c28` | — | low | auto-shape | array |
| `0x52c32` | — | low | auto-shape | array |
| `0x52c3a` | — | low | auto-shape | array |
| `0x53022` | — | low | auto-shape | array |
| `0x534d6` | — | low | auto-shape | array |
| `0x534e0` | — | low | auto-shape | array |
| `0x537c2` | — | low | auto-shape | array |
| `0x537fa` | — | low | auto-shape | array |
| `0x53c16` | — | low | auto-shape | array |
| `0x54670` | — | low | auto-shape | array |
| `0x60c4a` | — | low | auto-shape | array |
| `0x60c7c` | — | low | auto-shape | array |
| `0x60cae` | — | low | auto-shape | array |
| `0x60ce0` | — | low | auto-shape | array |
| `0x60d12` | — | low | auto-shape | array |
| `0x63774` | — | low | auto-shape | array |
| `0x637a6` | — | low | auto-shape | array |
| `0x637d8` | — | low | auto-shape | array |
| `0x6380a` | — | low | auto-shape | array |
| `0x63c89` | — | low | auto-shape | array |
| `0x63db5` | — | low | auto-shape | array |
| `0x63f45` | — | low | auto-shape | array |
| `0x63fa9` | — | low | auto-shape | array |
| `0x6400d` | — | low | auto-shape | array |
| `0x64071` | — | low | auto-shape | array |
| `0x64139` | — | low | auto-shape | array |
| `0x6419d` | — | low | auto-shape | array |
| `0x7ea40` | — | low | auto-shape | array |
| `0x7f21f` | — | low | auto-shape | array |
| `0x7f9ef` | — | low | auto-shape | array |
| `0xf1e10` | — | low | auto-shape | array |
| `0xf29c8` | — | low | auto-shape | array |
| `0xf29ca` | — | low | auto-shape | array |
| `0xf29cc` | — | low | auto-shape | array |
| `0xf29ce` | — | low | auto-shape | array |
| `0xf29d0` | — | low | auto-shape | array |
| `0xf29d2` | — | low | auto-shape | array |
| `0xf29d4` | — | low | auto-shape | array |
| `0xf29d6` | — | low | auto-shape | array |
| `0xf29d8` | — | low | auto-shape | array |
| `0xf31ab` | — | low | auto-shape | array |
| `0xf5cb3` | — | low | auto-shape | array |
| `0xf609b` | — | low | auto-shape | array |
| `0xf6483` | — | low | auto-shape | array |
| `0xf709f` | — | low | auto-shape | array |
| `0xf786f` | — | low | auto-shape | array |
| `0xf7c57` | — | low | auto-shape | array |
| `0xf93c7` | — | low | auto-shape | array |
| `0xf97af` | — | low | auto-shape | array |
| `0xfd30f` | — | low | auto-shape | array |
| `0xfd342` | — | low | auto-shape | array |
| `0xfd40c` | — | low | auto-shape | array |
| `0xfd43b` | — | low | auto-shape | array |
| `0xfd464` | — | low | auto-shape | array |
| `0xfd478` | — | low | auto-shape | array |
| `0x136eec` | — | low | auto-shape | array |
| `0x136f1e` | — | low | auto-shape | array |
| `0x136f53` | — | low | auto-shape | array |
| `0x136f87` | — | low | auto-shape | array |
| `0x136f9c` | — | low | auto-shape | array |
| `0x137064` | — | low | auto-shape | array |
| `0x137132` | — | low | auto-shape | array |
| `0x137147` | — | low | auto-shape | array |
| `0x13720f` | — | low | auto-shape | array |
| `0x13ec57` | — | low | auto-shape | array |
| `0x13ec5b` | — | low | auto-shape | array |
| `0x13ec5f` | — | low | auto-shape | array |
| `0x13f87c` | — | low | auto-shape | array |
| `0x13fba0` | — | low | auto-shape | array |
| `0x13fba7` | — | low | auto-shape | array |
| `0x13fbae` | — | low | auto-shape | array |
| `0x13fbb6` | — | low | auto-shape | array |
| `0x2a2da4` | — | low | auto-shape | array |
| `0x2a2dc3` | — | low | auto-shape | array |
| `0x2a2dd1` | — | low | auto-shape | array |
| `0x2a2de4` | — | low | auto-shape | array |
| `0x2a31cc` | — | low | auto-shape | array |
| `0x2a3231` | — | low | auto-shape | array |
| `0x2d242c` | — | low | auto-shape | array |
| `0x2d243a` | — | low | auto-shape | array |
| `0x2d2822` | — | low | auto-shape | array |
| `0x2d282a` | — | low | auto-shape | array |
| `0x2d2832` | — | low | auto-shape | array |
| `0x318d48` | — | low | auto-shape | array |
| `0x319518` | — | low | auto-shape | array |
| `0x3b2bb0` | — | low | auto-shape | array |
| `0x3b2bb3` | — | low | auto-shape | array |
| `0x3c5d45` | — | low | auto-shape | array |
| `0x3c9bcc` | — | low | auto-shape | array |
| `0x3c9bd5` | — | low | auto-shape | array |
| `0x3c9be8` | — | low | auto-shape | array |
| `0x3c9cd1` | — | low | auto-shape | array |
| `0x3ca4a1` | — | low | auto-shape | array |
| `0x3d41a0` | — | low | auto-shape | array |
| `0x3d41c0` | — | low | auto-shape | array |
| `0x3d42a0` | — | low | auto-shape | array |

## index-pointer

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x3070` | scjump_context_id | med | investigation | SCJUMP@0x0 dispatches its 16 branches on it. Setters: ALLMAP@0x160 (1), FIELD (2..10,14,16), TOWN@0xd8 (10) / @0x860 (11), SELSTAGE@0x73f (10), STUDIO@0xae8 and SYSTEM4@0x656 (12), SETSTUDIOCH@0xe (13), CHECKREWARD@0x295 (15). |
| `0x3612` | current_world_area_id | high | investigation | ALLMAP@0x10f4 sets it when the cursor moves to an area; ALLMAP@0xc7a copies it to labyrinth no 0x428e before SELSTAGE; FIELD@0x265 sets it from 0x428e; TOWN@0xc reads the area name; ALLMAP@0x3f5 resets it to 1 when forced home. Story SC scripts set it. Also observed: [alchemy decode, named town_current_id] SIINIT@0x2a dispatches on it; TOWN@0xc reads area name 0x241d[id] and 0x13ea68[id]. |
| `0x3678` | current_town_location_id | med | investigation | Town location cursor; TOWN@0x843 initialises it, SCJUMP context 11 tests it (SCJUMP@0x6081). |
| `0x378b` | current_stage_id | high | investigation | FIELD@0x0 reads stage_labyrinth_ids/stage_floor_numbers[stage] into current_labyrinth_id/current_floor_no; when zero FIELD@0x22..0x76 scans all 1000 stages for the matching (labyrinth, floor) and stores the hit back. FIELD@0x7b logs it as 'Field StageNo='. Index for every per-stage array (stage_revealed_tile_bitmask, stage_occupation_percent, ...). |
| `0x4287` | current_floor_width_tiles | high | investigation | Written by each MPINIT block (MPINIT@0x32). Upper bound of every tile-x loop: FIELD@0x72a, DRAWMAP@0x16c, DRAWMINIMAP@0x18 (minimap width = 32+10*W px), SETROOM@0x56, CALCOCC@0x3d4; FIELD@0x650 sets the camera right bound to 50+100*W. |
| `0x4288` | current_floor_height_tiles | high | investigation | Written by each MPINIT block (MPINIT@0x37). Upper bound of tile-y loops (FIELD@0x70d, DRAWMAP@0x264, SETROOM@0x39, CALCOCC@0x3b7); FIELD@0x663 sets the camera bottom bound to 50+100*H. |
| `0x428e` | current_labyrinth_id | high | investigation | MPINIT selector: every map block starts `eq lab, A; eq floor, F; and; jcc` (MPINIT@0x16 ff.). FIELD@0xe sets it from stage_labyrinth_ids and logs it as 'LabNo=' (FIELD@0x81). SELSTAGE lists stages whose stage_labyrinth_ids equals it; SETROOM@0x13d matches HAINIT hall-art records against it. |
| `0x428f` | current_floor_no | high | investigation | Second MPINIT selector (MPINIT@0x1d ...). FIELD@0x1a sets it from stage_floor_numbers and logs it as 'FloorNo=' (FIELD@0x87). SELSTAGE@0x735 writes it before running the SCJUMP event chain for event-entry stages. |
| `0x4290` | field_map_continue_mode | high | investigation | Logged as 'MapContinue=' (FIELD@0x8d). FIELD@0x4c9 runs the MPINIT and STINIT layer chains only when it is not 1; FIELD@0x67d copies floor_terrain_template into current_floor_terrain_grid and rebuilds tile_owner_faction_mask only when it is 0. |
| `0x4295` | field_camera_min_x | low | inference | FIELD@0x64b; paired with field_camera_max_x = 50 + 100*width (tiles are 100 px, DRAWMAP@0x2b3). Consumers not traced. |
| `0x4296` | field_camera_max_x | low | inference | FIELD@0x657. Consumers not traced. |
| `0x4297` | field_camera_min_y | low | inference | FIELD@0x65e. Consumers not traced. |
| `0x4298` | field_camera_max_y | low | inference | FIELD@0x66a. Consumers not traced. |
| `0x537f9` | current_arena_battle_id | high | investigation | ARENA@0x73a sets it from the list; ARENA, STAGECLEAR@0xe94 (win count) index with it. |
| `0xfd41b` | adv_substituted_image_resource | high | investigation | Scenes set it before showing an event CG (SC0010@0x110e), then walk the CSINIT (0x3d4220) and EVINIT (0x3d4240) hook chains while it is unchanged; EVINIT replaces it with the costume/form variant. CGMODE@0x10ba does the same for gallery viewing. Also observed: [alchemy decode, named adv_standing_sprite_resource] CSINIT (573 rules: eq 0xfd41b sprite / eq costume slot value / mov 0xfd41b replacement); scene scripts call costume_sprite_hooks after setting it. |
| `0x13e80d` | world_area_visible_list | high | investigation | Built by ALLMAP@0xdd5; ALLMAP@0xb15/0x1136 and SELSTAGE wheel navigation index it with 0x13e872. |
| `0x13ec55` | studio_selected_room | med | investigation | ARRANGE@0x8cf derives it from the cursor (div 25) and selects the matching studio_room_levels entry. |
| `0x13ec63` | studio_room_level_index | med | investigation | ARRANGE@0x42a, CALCSETTLE@0x4b, SETSTUDIOCH, STUDIO compute it before indexing studio_room_layout_grids / costs. |
| `0x14d09c` | routine_id | high | investigation | BTRTN@0x00029 loads it from the unit's battle routine 0x61f6e[unit][phase 0x320b]. MVRTN@0x0008d loads it from the map routine 0x61ed8[unit][phase]. Every RTINIT bank is indexed [routine_id][step]. |
| `0x14d09d` | routine_step_index | high | investigation | BTRTN@0x00145 and MVRTN@0x000cb loop over the 50 steps, and RTN_* scripts read their parameters at [routine_id][step]. |
| `0x2a3510` | set_land_terrain_id | med | investigation | Argument of SETLAND: SETLAND@0x62 writes it into every cell of the doubled rectangle (2*G[0xfec0c]..2*G[0xfec0e], 2*G[0xfec0d]..2*G[0xfec0f]) and reveals tiles whose visibility class is 1 (or >0 once the stage is fully complete). Set by STAGEREADY/STAGEROUND/STAGECLOSE and story scripts (SC2200, SC3030, ...) to dig or open map areas; SETOCC@0x15 also reads it as a faction bit. |
| `0x2a64bb` | current_card_id | high | investigation | Selected field card. FIELD@0x831d iterates candidates; FIELD@0x83fd stores the weighted pick; every card array is indexed through it. |
| `0x2a79d4` | current_card_generation_list_id | high | investigation | FIELD@0x82a4 sets it before walking the CDINIT hook chain 0x3d42a0; CDINIT branches on it to fill the candidate arrays. |
| `0x2a9b89` | current_battle_slot | high | investigation | Selector for battle_unit_* arrays (SETEN, SETCH, ADDEXP, CALCREVISE, EQUIP). |
| `0x2a9b8b` | current_unit_id | high | investigation | Selector for EBINIT arrays (SETEN@0x00007, UNITECH@0x00025, INFO5). |
| `0x2d2845` | current_character_id | high | investigation | Selector for all character_* arrays and CCINIT. |
| `0x2d2846` | current_item_id | high | investigation | Shared item argument/selection slot. ITMES@0x00006.. compares item_category[current_item_id]; DRAWTOOLTIP@0x00037, CALCARM@0x00000, USEITEM@0x00001, ADDITEM@0x00041 and ADDSTOCK@0x00021 all index ITINIT arrays with it. Also observed: [maps decode, named current_costume_item_id] COINIT selector (`eq local0, G[0x2d2846], K`, COINIT@0x1d..). ENHANCE, CALCARM, GAMESTART set it before calling the 0x3d4280 chain. |
| `0x3a0e8c` | selected_costume_enhancement_id | med | investigation | SELSEWUP@0x00087 decodes it with mod/div 15; ENHANCE@0x01109 sets it. |
| `0x3a10c7` | current_skill_id | med | investigation | Skill argument slot one cell before the first SKINIT array; BOMBSEEK@0x0000e, FIELD@0x0b595 and INFO6@0x00fca index SKINIT arrays with it. |
| `0x3b2238` | current_condition_id | high | investigation | Argument to ADDILLSUB/CALCILL; set by EQUIP@0x00072 and USEITEM@0x0050f. |
| `0x3b2239` | condition_level_delta_arg | high | investigation | ADDILLSUB@0x00062 adds it to the unit level, clamped to the 0x618fc baseline and 5. |
| `0x3b3b61` | selected_battle_animation_id | high | investigation | CALCDMG@0x001cb/0x001f1 sets it from skill_battle_animation_id or item_battle_animation_id; BTL also writes it (@0x0136e, 0x017a8 = 0x21). BTANINIT@0x00071 selects the six-effect row with it (Himegari analog selected_battle_animation_id). |
| `0x3b6e91` | current_quest_id | high | investigation | Loop cursor / selected quest. CHECKCONTRACT@0x0 and CHECKREWARD@0x3 iterate it 0..1999; INFO2@0x653 sets it to the highlighted card; RECEIPT reads every quest array through it. |
| `0x3cac74` | current_objective_id | high | investigation | Selected objective. INFO1@0x6b9 sets it; FAMES dispatches on it; INFO1 reads strings/script through it. |
| `0x3d19d5` | character_profile_selected_id | high | investigation | Set by INFO7, read by CIMES/$1$CIMES dispatch guards. |
| `0x3d20de` | current_vocabulary_id | high | investigation | INFO8@0x6b9 sets it; VIMES dispatches on it. |
| `0x3d41e0` | town_site_hooks | high | investigation | TOWN@0x194 and SELSTAGE@0x539 call-script each nonzero entry to (re)build the current town's site table. |
| `0x3d4200` | shop_catalogue_hooks | high | investigation | SHOP@0x169c call-scripts each nonzero entry. |
| `0x3d4220` | costume_sprite_hooks | high | investigation | Every scene script (e.g. $1$SC2600@0xa60, 113 scripts) calls each entry after choosing a standing sprite so CSINIT can substitute the costume variant; followed by the 0x3d4240 hook list. |
| `0x3d4280` | costume_board_loader_script_ids | high | investigation | INIT2@0x7c stores COINIT; CALCARM@0x6a, ENHANCE@0x137c and GAMESTART@0xca2 call the chain after setting 0x2d2846. |
| `0x3d42c0` | mpinit_layer_script_ids | high | investigation | FIELD@0x4df..0x524 calls every nonzero slot in order (base first, then appends), interleaved with stinit_layer_script_ids. Appends never zero the grid, so they only add (labyrinth, floor) blocks. One of INIT2's 32-slot per-table layer chains (0x3d4180 SCJUMP .. 0x3d42e0 STINIT, stride 0x20). |
| `0x3d42e0` | stage_loader_script_ids | high | investigation | INIT2@0x8b stores STINIT; FIELD@0x50c, SELSTAGE@0x13ed, ORGANIZE, INFO3, ARENA call every nonzero cell in order, so append overlays run after the base loader. |

## story-flag

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x429d` | stage_availability_flags | high | investigation | CHECKSTAGE@0x3c sets bits 0 and 1 for every stage with a positive labyrinth id whose required story flags are all set, no forbidden flag is set, and whose prerequisite (labyrinth, floor) stage is cleared; otherwise CHECKSTAGE@0x57 clears bit1. SELSTAGE@0x48e lists bit0 stages of the current labyrinth; SELSTAGE@0x651 uses bit1. |
| `0x4685` | stage_visited_flags | med | investigation | FIELD@0x1e3 sets it on every entry; SELSTAGE@0x117d uses zero to mark a stage as new. |
| `0x4a6d` | stage_cleared_flags | med | investigation | CHECKSTAGE@0x183 returns it for the prerequisite (labyrinth, floor) stage; FIELD@0x533/0x6ac (first clear vs revisit: inline array 0x14041d, reset of exploration masks), SELSTAGE@0x680 (party-requirement column set), STAGECLEAR/STAGECLOSE/SETSTAGEFLAG write it. |
| `0x4e55` | stage_all_objectives_complete_flags | med | investigation | FIELD@0xc7d4 sets 1 at stage end then clears it (FIELD@0xc87c/0xc9f3) if any defined objective slot is still unearned; SELSTAGE@0xfdf draws a complete badge instead of the occupation %; RESETLAND@0xcb, SETLAND@0x9f, SETOBJ@0x86, AIM and STAGECLEAR also test it. |
| `0x5625` | stage_player_occupied_tile_bitmask | high | investigation | FIELD@0xc8a5..0xc8f6 rebuilds it from tile_owner_faction_mask bit 1 at stage end; FIELD@0x7c1 restores 2 into tile_owner_faction_mask on re-entry; FIELD@0x6e9 clears it on first entry. |
| `0x1dcc5` | stage_objective_reward_state | med | investigation | FIELD@0xc91e..0xca0a sets bit0 when objective k (any of the four STINIT2 reward columns nonzero) has its STINIT condition flags (0x140424[k][2]) satisfied; SELSTAGE@0x10da draws a lit marker when bit1 is set; STAGECLEAR@0x5c5 grants the rewards. |
| `0x1ec65` | stage_revealed_tile_bitmask | high | investigation | FIELD@0x6d4 clears the stage row block on first entry, then sets bits for tiles of visibility class 1 (FIELD@0x7b1) or every non-void tile at 100% (FIELD@0x779). CALCOCC@0x26c reveals player-owned rooms, SETLAND@0xd7 reveals dug cells, DELLAND@0xb4 hides deleted ones, RESETLAND@0x103. DRAWMAP@0xa8b draws fog on unrevealed tiles (lighter 'frontier' tint when a 4-neighbour is revealed) and hides class-3 doors; DRAWMINIMAP@0xb0 only plots revealed tiles; MVSEEK@0x4c9 lets hidden (class 3) tiles be crossed once revealed. |
| `0x64201` | character_join_flags | med | investigation | UNITECH@0x0060c writes the mode bits; bit1 gates skill-encyclopedia registration (UNITECH@0x00618, CALCCC@0x0028d) and affinity gains (COMMITBTL@0x00160). |
| `0x7e269` | character_class_change_state | high | investigation | CCINIT tests !(bit0) to fire a rule and sets bit0; event-gated rules additionally need bit1 (set by story scenes, e.g. SC0450@0x05123 bit-set then CALCCC) or the whole script exits. CALCCC@0x00352 derives the protagonist rank 0x7ea49 from column bits of character 1. |
| `0xf703b` | world_area_visited_profile_flags | high | investigation | FIELD@0x215 and SELSTAGE@0xcdd set it and store-shared-profile-int; INFO3@0x1b15 / INFO5@0x1075 load it to reveal area names/pictures even when not discovered in this save. |

## string-table

| address | name | conf | source | usage |
|---|---|---|---|---|
| `GS 0x281` | battle_unit_titles | high | investigation | SETEN@0x00026, SETCH@0x00054, ADDEXP@0x00629. |
| `GS 0x2b3` | character_class_titles | high | investigation | Seeded from unit_initial_class_titles by UNITECH@0x0039e, replaced by CALCCC; SETCH@0x0004d copies it to the battle title 0x281. |
| `GS 0x322` | unit_speaker_names | high | investigation | Every scene script draws 0x322[0x306d][0x306f] on the name plate (e.g. SC0000@0x1fa1e); scripts select the form with mov 0x306f 0 (25124x), 2 (299x, e.g. '女剣士' before introduction), 1 (262x), 4 (73x, '？？？？'). Column 3 is never written. Columns: 0=name, 1=alternate_name, 2=descriptor, 3=unused, 4=unknown_placeholder. |
| `GS 0x16aa` | voice_setting_labels | high | investigation | CONFIG4 draws the per-character voice/colour rows; SC4500 reads it. Columns: 0=name, 1=descriptor, 2=voice_actress. |
| `GS 0x241d` | world_area_names | high | investigation | AMINIT2 writes 0x241d+id. TOWN@0xc copies the current area's name (0x3612) to the location title; INFO3@0x1b50 and INFO5@0x10b0 list it (or ？？？ when undiscovered and never visited). |
| `GS 0x2481` | town_site_names | high | investigation | SIINIT@0x47.. set-string; cleared to '' for site 1 in single-site areas. |
| `GS 0x249f` | stage_objective_texts | high | investigation | FIELD@0x615 copies row (0x4a6d\|0x14c4e4)[stage] into 0x27b..0x280, which AIM@0x4b1.. draws as the objective panel. |
| `GS 0x24ab` | stage_description_lines | med | investigation | Written by STINIT per stage; SELSTAGE reads 0x24ab.. to show the stage description. |
| `GS 0x24af` | stage_reward_condition_texts | high | investigation | AIM@0x5bc and STAGECLEAR@0x8c6 draw 0x24af[i] next to reward row i (STINIT2 0x14827c[stage][i] etc.) once 0x1dcc5[stage][i] is known. |
| `GS 0x24b3` | stage_display_names | high | investigation | STINIT2 set-string base+id. SELSTAGE lists them; FIELD@0x527 copies the current one to G-string 0x278; AIM, ARENA, STAGECLEAR display it. |
| `GS 0x289b` | terrain_type_names | high | investigation | LAINIT@0x0 (set-string base+id). STATUS@0x1fc5 draws the selected tile's terrain name in the terrain panel. |
| `GS 0x28c3` | object_type_names | high | investigation | FIELD and STATUS display the name of the object under the cursor. |
| `GS 0x2927` | object_type_descriptions | high | investigation | STATUS shows both lines. The generic extractor mis-keys these as records 102..161 / desc fields. |
| `GS 0x29ef` | card_definition_names | high | investigation | FIELD@0x8876 shows the drawn card's name. |
| `GS 0x2a53` | card_definition_result_messages | high | investigation | FIELD@0x8890 shows the result line. |
| `GS 0x2ab7` | unit_full_names | high | investigation | EBINIT name array (record key). FELLOWSTATUS@0x013a6/0x013c0 draws it for the selected party member (0x2a9b8b). Debug/variant suffixes in parentheses identify enemy variants, e.g. 250 'ユエラ（敵／倒せない）'. |
| `GS 0x2e9f` | unit_display_names | high | investigation | EBINIT short name. Drawn by STATUS@0x01280, INFO5@0x00d25 (bestiary header), INFO3@0x01ace, BTL@0x01ff6, SELACT@0x01c5e, DRAWCHP@0x001aa, SELSTAGE@0x01b45, SHOWCC@0x00000. |
| `GS 0x3287` | unit_initial_class_titles | high | investigation | SETEN@0x0001f copies it to the battle-slot title 0x281; UNITECH@0x003a5 seeds character_class_titles (0x2b3) when a character first joins; ADDEXP@0x00629 falls back to it when 0x2b3 is empty; SHOWCC@0x00298 shows the old title on class change. |
| `GS 0x366f` | unit_bestiary_description_lines | high | investigation | INFO5@0x00ffd loops k=0..2 with lookup-array-2d 0x366f[unit][k] and draws each line. e.g. unit 70 -> 'ユイドラの街を守る兵士','槍を巧みに使いこなす','街を守護する志は強い'. The generic extractor's desc2..desc4 values are correct per record but its desc_array_bases meta (0x36fb..) is wrong. Columns: 0=line_1, 1=line_2, 2=line_3. |
| `GS 0x4228` | item_names | high | investigation | ITINIT writes GStr[0x4228+id] for every record (base + $1$/$2$/$3$). ADDSTOCK@0x00021 and FELLOW@0x01007 concatenate it into messages ('...が所持限界数を超えています', '...は外すことが出来ない…'); SHOP@0x02663 draws it in the shop list. |
| `GS 0x49f8` | item_short_descriptions | med | investigation | ITINIT writes GStr[0x49f8+id] after each record. Only STATUS reads the array. Weapon summaries list type, element, range, and status effects; ITMES has the full player-facing title and description. |
| `GS 0x51c8` | alchemy_recipe_unlock_texts | high | investigation | INFO4@0x1069 draws the string right-aligned under the recipe's rank requirements. Generic name-mode extraction mistook these hints for record names. |
| `GS 0x5998` | skill_names | high | investigation | SKINIT writes GStr[0x5998+id]. INFO3@0x0192a and DRAWTOOLTIP show the name of item_granted_skill_id; BTL, SELACT, SELBOMB, FELLOW, STATUS and others also draw it. |
| `GS 0x5d80` | skill_short_descriptions | med | investigation | SKINIT writes GStr[0x5d80+id]. Read by STATUS and FELLOWSTATUS. |
| `GS 0x6168` | condition_level_names | high | investigation | ILINIT writes level names for ids 1..28. Same layout as Himegari condition_level_names (0x25fa). |
| `GS 0x61ff` | species_names | med | investigation | AFINIT writes 16 names. No direct reader was found in the static scan, so the GStr base is probably reached through an offset. |
| `GS 0x621a` | affinity_level_names | high | investigation | INFO3@0x017b6 prints GStr[0x621a + 5 + item_affinity_level_deltas], so level 5 is neutral. |
| `GS 0x625f` | quest_names | high | investigation | QUINIT quest titles (ids 1..999 = journal quests, 1000..1999 = reward notices). CHECKCONTRACT@0x1d and INFO2@0x6ab use a nonempty name as the existence test; INFO2@0x1058 and RECEIPT@0x343 draw it. |
| `GS 0x6a2f` | quest_description_lines | high | investigation | INFO2@0x1171 draws row[quest] columns 0..4 at y=0x77+21i (header + overview), 5..6 at y=0x111 (達成条件 block) and 7..8 at y=0x1ab (失敗条件 block). Authored failure lines read 期限の経過 / ○章突入で失敗 / なし, matching the deadline and failure-trigger arrays. Columns: 0=source_header, 1=overview_1, 2=overview_2, 3=overview_3, 4=overview_4, 5=clear_condition_1, 6=clear_condition_2, 7=failure_condition_1, 8=failure_condition_2. |
| `GS 0xb07f` | quest_requester_names | high | investigation | INFO2@0x106b and RECEIPT@0x2fe draw the requester under the title (RECEIPT right-aligns by strlen*7). |
| `GS 0xb84f` | quest_category_labels | high | investigation | INFO2@0xf27 draws it on the journal card once the quest is posted (state bit0); otherwise ？？ is drawn. |
| `GS 0xc01f` | arena_battle_names | high | investigation | ARENA@0x13c1 list row label, ARENA@0x1566 detail panel title. |
| `GS 0xc083` | arena_battle_texts | high | investigation | ARENA@0x1586 draws column 0; ARENA@0x1656 draws column 1 only while the battle has not been won (else 'なし'). Columns: 0=description, 1=reward_text. |
| `GS 0xc14b` | objective_chapter_labels | high | investigation | INFO1@0xf31 draws the chapter label column of the objective list. |
| `GS 0xc91b` | objective_texts | high | investigation | INFO1@0x706 skips empty strings; INFO1@0xf4f lists and INFO1@0xfb7 headlines the selected objective. 126 live objectives among ids 1..203. |
| `GS 0xd0eb` | character_profile_names | high | investigation | INFO7@0x00714 lists profiles with non-empty names; INFO7@0x00e27 draws the header. |
| `GS 0xd1b3` | character_profile_race_texts | high | investigation | INFO7@0x00e3a. |
| `GS 0xd27b` | character_profile_sex_texts | high | investigation | INFO7@0x00e4a. |
| `GS 0xd343` | character_profile_epithets | high | investigation | INFO7@0x00e5a. |
| `GS 0xd40b` | vocabulary_names | high | investigation | INFO8@0x714 skips empty names and lists the rest; INFO8@0xd52 headlines the selected term. Base 155 + $1$ 2 + $3$ 6 = 163 entries. |
| `GS 0x276` | — | med | auto-shape | string-table (written by SC0000) |
| `GS 0x277` | — | med | auto-shape | string-table (written by SC0150) |
| `GS 0x278` | — | med | auto-shape | string-table (written by ALLMAP) |
| `GS 0x279` | — | med | auto-shape | string-table (written by CONFIG2) |
| `GS 0x27a` | — | med | auto-shape | string-table (written by CONFIG2) |
| `GS 0x27b` | — | med | auto-shape | string-table (written by SC3530) |
| `GS 0x27c` | — | med | auto-shape | string-table (written by SC3530) |
| `GS 0x27d` | — | med | auto-shape | string-table (written by SC3530) |
| `GS 0x27e` | — | med | auto-shape | string-table (written by SC3530) |
| `GS 0x27f` | — | med | auto-shape | string-table (written by SC3530) |
| `GS 0x280` | — | med | auto-shape | string-table (written by SC3530) |
| `GS 0x327` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x328` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x329` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x32b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x32c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x32d` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x32e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x330` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x331` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x332` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x333` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x335` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x336` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x337` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x338` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x33a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x33b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x33c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x33d` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x33f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x340` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x341` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x342` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x344` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x345` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x346` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x347` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x349` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x34a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x34b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x34c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x34e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x34f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x350` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x351` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x353` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x359` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x35a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x35b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x35d` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x35e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x35f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x360` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x362` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x363` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x364` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x365` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x367` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x368` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x369` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x36a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x36c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x36d` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x36e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x36f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x371` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x372` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x373` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x374` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x376` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x386` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x387` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x388` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x38a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x38b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x38c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x38d` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x38f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x390` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x391` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x392` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x394` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x395` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x396` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x397` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x399` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x39a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x39b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x39c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x39e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x39f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x3a0` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x3a1` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x3a3` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x3d6` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x3d7` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x3d8` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x3da` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x41c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x41d` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x41e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x420` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x421` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x422` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x423` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x425` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x426` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x427` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x428` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x42a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x44e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x44f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x450` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x452` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x453` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x454` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x455` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x457` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x458` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x459` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x45a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x45c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x462` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x463` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x464` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x466` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x480` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x481` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x482` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x484` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x485` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x486` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x489` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x48a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x48b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x48c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x48e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x48f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x490` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x491` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x493` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x494` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x495` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x496` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x498` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x499` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x49a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x49b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x49d` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x49e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x49f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4a0` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4a2` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4a3` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4a4` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4a5` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4a7` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4a8` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4a9` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4aa` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4ac` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4ad` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4ae` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4af` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4b1` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4b2` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4b3` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4b4` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4b6` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4b7` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4b8` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4b9` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4bb` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4bc` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4bd` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4be` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4c0` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4c1` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4c2` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4c3` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4c5` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4c6` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4c7` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4c8` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4ca` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4cb` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4cc` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4cd` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4cf` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4d0` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4d1` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4d2` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4d4` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4d5` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4d6` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4d7` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4d9` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4da` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4db` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4dc` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4de` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4df` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4e0` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4e3` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4e4` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4e5` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4e8` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4e9` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4ea` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4ed` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4ee` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4ef` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4f0` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4f2` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4f3` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4f4` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4f7` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4f8` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4f9` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4fc` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x4fd` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x4fe` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x501` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x502` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x503` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x506` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x507` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x508` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x50b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x50c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x510` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x511` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x512` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x515` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x516` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x517` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x51a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x51b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x51c` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x51f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x520` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x521` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x524` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x525` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x526` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x529` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x548` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x54c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x54d` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x54e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x551` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x552` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x553` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x556` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x557` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x558` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x55b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x55c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x55d` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x560` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x561` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x562` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x565` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x566` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x567` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x56a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x56b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x56f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x570` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x574` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x575` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x576` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x579` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x57a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x57b` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x57e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x57f` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x580` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x581` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x583` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x584` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x585` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x586` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x588` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x589` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x58a` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x58b` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x58d` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x58e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x58f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x592` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x593` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x594` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x597` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x598` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x599` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x59c` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x59d` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x59e` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x59f` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x5a1` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x5a2` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5a3` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5a4` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x5a6` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x5a7` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x5a8` | — | med | auto-shape | string-table (written by CNINIT) |
| `GS 0x5ab` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5b1` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5b2` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5b3` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x5b5` | — | med | auto-shape | string-table (written by CNINIT) |
| `0xa66` | — | med | auto-shape | string-table (written by CNINIT) |
| `0xa70` | — | med | auto-shape | string-table (written by CNINIT) |
| `0xb15` | — | med | auto-shape | string-table (written by CNINIT) |
| `0xb1a` | — | med | auto-shape | string-table (written by CNINIT) |
| `0xb1e` | — | med | auto-shape | string-table (written by CNINIT) |
| `0x16aa` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16ad` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16ae` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16b0` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16b1` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16b2` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16b3` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16b4` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16b5` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16b6` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16b7` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16b8` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16b9` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16ba` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16bc` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16bd` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16be` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16bf` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16c0` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16c1` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16c2` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16c3` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16c5` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16c6` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16c7` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16c8` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16c9` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16ca` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16cb` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16cc` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16cd` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16ce` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16cf` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16d1` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16d2` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16d4` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16d5` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16d6` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16d7` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16d8` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16d9` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16da` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16db` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16dc` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16dd` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16de` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16e0` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16e1` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16e2` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16e3` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16e4` | — | med | auto-shape | string-table (written by CVINIT) |
| `0x16e6` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16e7` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16e8` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16e9` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16ea` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16eb` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16ec` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16ed` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16ee` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16ef` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16f0` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16f1` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16f2` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16f3` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16f4` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16f5` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16f6` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16f7` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16f8` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16f9` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16fa` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16fb` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16fc` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16fd` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16fe` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x16ff` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x2262` | — | med | auto-shape | string-table (written by CONFIG2) |
| `GS 0x233f` | — | med | auto-shape | string-table (written by BUNKI) |
| `GS 0x241c` | — | med | auto-shape | string-table (written by SAVE) |
| `GS 0x241e` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x241f` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2420` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2421` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2422` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2423` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2424` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2425` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2426` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2427` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2428` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2429` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x242a` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x242b` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x242c` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x242d` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x242f` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2430` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2431` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2434` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2435` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2436` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2437` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2438` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x243a` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x243d` | — | med | auto-shape | string-table (written by AMINIT2) |
| `GS 0x2482` | — | med | auto-shape | string-table (written by SIINIT) |
| `GS 0x2483` | — | med | auto-shape | string-table (written by SIINIT) |
| `GS 0x2484` | — | med | auto-shape | string-table (written by SIINIT) |
| `GS 0x2485` | — | med | auto-shape | string-table (written by SIINIT) |
| `GS 0x2486` | — | med | auto-shape | string-table (written by SIINIT) |
| `GS 0x2487` | — | med | auto-shape | string-table (written by SIINIT) |
| `GS 0x2488` | — | med | auto-shape | string-table (written by SIINIT) |
| `GS 0x2489` | — | med | auto-shape | string-table (written by SIINIT) |
| `GS 0x248a` | — | med | auto-shape | string-table (written by SIINIT) |
| `GS 0x248b` | — | med | auto-shape | string-table (written by SIINIT) |
| `GS 0x24a0` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24a1` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24a2` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24a3` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24a4` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24a5` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24a6` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24a7` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24a8` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24a9` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24aa` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24ac` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24ad` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24ae` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24b0` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24b1` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24b2` | — | med | auto-shape | string-table (written by STINIT) |
| `GS 0x24b4` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24b5` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24b6` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24b7` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24b8` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24b9` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24ba` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24bb` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24bc` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24bd` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24be` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24bf` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24c6` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24c7` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24c8` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24c9` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24ca` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24cb` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24cc` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24cd` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24ce` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24cf` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24d0` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24d1` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24d2` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24d3` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24d4` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24d5` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24d6` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24d7` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24d8` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24d9` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24dc` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24dd` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24de` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24df` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24e0` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24e6` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24e7` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24e8` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24e9` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24ea` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24eb` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24f0` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24f1` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24f2` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24f3` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24f4` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24f5` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24fa` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24fb` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24fc` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24fd` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24fe` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x24ff` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2504` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2505` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2506` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2507` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2508` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x250e` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x250f` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2510` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2511` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2512` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2522` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2523` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2524` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x252c` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x252d` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2536` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2537` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2538` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2539` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2540` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x254a` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x255e` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x255f` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2560` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2568` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2569` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x256a` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x256b` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2572` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2573` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2574` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2575` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2576` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x257c` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x257d` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x257e` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x257f` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2580` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2586` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2587` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2588` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x259a` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x259b` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x259c` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x259d` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x259e` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25a4` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25a5` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25a6` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25ae` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25af` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25b8` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25b9` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25c2` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25c3` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25c4` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25c5` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25d6` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25fe` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x25ff` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2612` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2613` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2626` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2627` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2630` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2631` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2662` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2676` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2680` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2681` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x269e` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x269f` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x26da` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2842` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2843` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2844` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2845` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2846` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2847` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2848` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2849` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x284a` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x284b` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x284c` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x284d` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x284e` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x284f` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2850` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2851` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2852` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2853` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2854` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2855` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2856` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2857` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2858` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2859` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x285a` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x285b` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x285c` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x285d` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x285e` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x285f` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2860` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2861` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2862` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2863` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2864` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x286a` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x286b` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x286c` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x286d` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x286e` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x286f` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2870` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2871` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2872` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2873` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2874` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2875` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2876` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2877` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2878` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x2879` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x287a` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x287b` | — | med | auto-shape | string-table (written by STINIT2) |
| `GS 0x289c` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x289d` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x289e` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x289f` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28a0` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28a2` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28a3` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28a5` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28a6` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28a7` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28a8` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28a9` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28aa` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28ab` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28ac` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28ad` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28ae` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28b4` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28b6` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28b7` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28bc` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28bd` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28be` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28bf` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28c1` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28c2` | — | med | auto-shape | string-table (written by LAINIT) |
| `GS 0x28c4` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28c5` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28c6` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28c7` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28c8` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28c9` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28ca` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28cb` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28cc` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28cd` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28ce` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28cf` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28d0` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28d1` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28d2` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28d3` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28d4` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28d5` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28d6` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28d7` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28d8` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28d9` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28da` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28db` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28dc` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28dd` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28de` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28df` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28e1` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28e2` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28e3` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28e4` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28e9` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28f6` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28f7` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28f8` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28f9` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28fa` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28fb` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28fc` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28fd` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28fe` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x28ff` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2900` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2929` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x292b` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x292d` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x292f` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2931` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2932` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2933` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2935` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2936` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2937` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2939` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x293b` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x293c` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x293d` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x293e` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x293f` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2940` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2941` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2942` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2943` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2944` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2945` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2947` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2948` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2949` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x294a` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x294b` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x294c` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x294d` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x294e` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x294f` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2950` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2951` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2952` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2953` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2954` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2955` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2957` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2959` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x295b` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x295c` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x295d` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x295f` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2963` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2964` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2965` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2967` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2968` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2969` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x296a` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2973` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2974` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x298d` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x298e` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x298f` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2990` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2991` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2992` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2993` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2994` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2995` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2996` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2997` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2998` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x2999` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x299a` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x299b` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x299c` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x299d` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x299e` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x299f` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x29a0` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x29a1` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x29a2` | — | med | auto-shape | string-table (written by OBINIT) |
| `GS 0x29f0` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x29f1` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x29f2` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x29f3` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x29f4` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x29f5` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x29f6` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x29f7` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x29f8` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x29fd` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x29fe` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x29ff` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a00` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a01` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a02` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a03` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a04` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a05` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a06` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a07` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a08` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a09` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a0a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a0b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a0c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a0d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a0e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a0f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a10` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a11` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a12` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a13` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a14` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a18` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a19` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a1a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a1b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a1c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a1d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a1e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a1f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a20` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a21` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a22` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a23` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a24` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a25` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a26` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a27` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a28` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a29` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a2a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a2b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a2c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a2d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a2e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a2f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a30` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a31` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a32` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a33` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a34` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a35` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a36` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a37` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a38` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a39` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a3a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a3b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a3c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a3d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a3e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a3f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a40` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a41` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a42` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a43` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a44` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a45` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a46` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a47` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a48` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a49` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a4a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a4b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a54` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a55` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a56` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a57` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a58` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a59` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a5a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a5b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a5c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a61` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a62` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a63` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a64` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a65` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a66` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a67` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a68` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a69` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a6a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a6b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a6c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a6d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a6e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a6f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a70` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a71` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a72` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a73` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a74` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a75` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a76` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a77` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a78` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a7c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a7d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a7e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a7f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a80` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a81` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a82` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a83` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a84` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a85` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a86` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a87` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a88` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a89` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a8a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a8b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a8c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a8d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a8e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a8f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a90` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a91` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a92` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a93` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a94` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a95` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a96` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a97` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a98` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a99` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a9a` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a9b` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a9c` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a9d` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a9e` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2a9f` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aa0` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aa1` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aa2` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aa3` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aa4` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aa5` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aa6` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aa7` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aa8` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aa9` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aaa` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aab` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aac` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aad` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aae` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2aaf` | — | med | auto-shape | string-table (written by CDINIT2) |
| `GS 0x2ab8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ab9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2aba` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2abb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2abc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2abd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2abe` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2abf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ac0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ac2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ac3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ac4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ac5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ac6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ac7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2acb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2acc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2acd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ace` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2acf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ad0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2adb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ae9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2aea` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2aeb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2af3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2af4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2af5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2af7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2afd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2afe` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2aff` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b00` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b01` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b02` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b03` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b04` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b05` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b06` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b07` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b08` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b09` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b0a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b0b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b0c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b0d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b0e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b0f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b10` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b11` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b12` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b13` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b14` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b15` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b16` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b17` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b18` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b19` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b1a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b1b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b1c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b1d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b1e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b25` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b26` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b27` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b28` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b29` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b2a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b2b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b2c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b2d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b2e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b2f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b30` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b31` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b32` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b33` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b34` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b35` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b36` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b37` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b38` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b3a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b80` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b81` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b82` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b83` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b85` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b86` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b87` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b8a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b8b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b8c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b8d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b94` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b95` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b96` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b97` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b98` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b9e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2b9f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ba0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bad` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bae` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2baf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bb0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bb1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bb2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bb3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bb4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bb5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bb6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bb7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bb8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bb9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bba` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2be4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2be5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2be9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bea` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2beb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bee` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bef` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bf8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bf9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2bfa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c02` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c03` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c04` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c16` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c17` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c18` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c19` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c1a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c1b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c1c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c1d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c2a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c2b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c2c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c2d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c2e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c2f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c30` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c48` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c49` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c4a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c4b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c4e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c4f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c52` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c53` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c54` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c55` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c56` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c5c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c5d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c66` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c67` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c68` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c69` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c6b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c6c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c6d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c6e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c70` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c71` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c72` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c7a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c7b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c7c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c8e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c8f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2c90` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cac` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cad` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cae` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2caf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cb0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cb1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cb2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cb6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cb7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cb8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cb9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cc0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cca` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ccb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ccc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ccd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cdd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cde` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2cdf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ce0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ce1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ce2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d10` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d11` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d12` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d13` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d14` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d15` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d16` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d1a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d1b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d1c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d1d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d2e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d2f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d30` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d74` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d75` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d76` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d77` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d78` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d79` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d7a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d7e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d7f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d80` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d82` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d83` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d88` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d92` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d93` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d94` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d95` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d97` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d98` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d9c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2d9d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2da1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2da2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2da3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2da6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2da7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2da8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2dab` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2db0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2db1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2db2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2db3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2dba` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2dbb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2dbc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2dd8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2dd9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2dda` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ddb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ddc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2de2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2de3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2de4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2de5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2de6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2de7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2de8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e0a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e0b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e0c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e14` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e15` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e16` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e17` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e18` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e19` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e1a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e1b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e1e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e1f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e20` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e21` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e22` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e28` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e29` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e2a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e32` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e33` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e34` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e35` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e36` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e37` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e38` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e39` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e3c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e3d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e3e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e3f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e40` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e41` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e42` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e43` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e44` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e45` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e46` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e47` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e48` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e49` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e4a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e4b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e4c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e4d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e4e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e4f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e50` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e51` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e52` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e53` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e54` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e55` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e56` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e57` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e58` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e59` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e5a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e5b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e5c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e5d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e5e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e5f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e60` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e61` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e62` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e63` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e64` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e65` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e74` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e75` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e76` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e77` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e78` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e79` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e7a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e7b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e7c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e7d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e7e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e7f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e80` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e81` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e82` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e83` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e84` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e85` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e86` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e87` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e88` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e89` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e8a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e8b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e8c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e8d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e8e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e8f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e90` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e91` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e92` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e93` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e94` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e95` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e96` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e97` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e98` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e99` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e9a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2e9b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ea0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ea1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ea2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ea3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ea4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ea5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ea6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ea7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ea8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2eaa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2eab` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2eac` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ead` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2eae` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2eaf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2eb3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2eb4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2eb5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2eb6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2eb7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2eb8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ec3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ed1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ed2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ed3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2edb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2edc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2edd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2edf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ee5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ee7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ee8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f68` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f69` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f6a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f6b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f6d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f6e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f6f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f72` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f73` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f74` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f75` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f7c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f7d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f7e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f7f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f80` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f86` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f87` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f88` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f95` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f96` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f97` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f98` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f99` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f9a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f9b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f9c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f9d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f9e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2f9f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fa0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fa1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fa2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fcc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fcd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fd1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fd2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fd3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fd6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fd7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fe0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fe1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fe2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fea` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2feb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fec` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2ffe` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x2fff` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3000` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3001` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3002` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3003` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3004` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3005` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3012` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3013` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3014` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3015` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3016` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3017` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3018` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3030` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3031` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3032` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3033` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3036` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3037` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x303a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x303b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x303c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x303d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x303e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3044` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3045` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x304e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x304f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3050` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3051` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3053` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3054` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3055` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3056` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3058` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3059` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x305a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3062` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3063` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3064` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x306c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3076` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3077` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3078` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3094` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3095` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3096` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3097` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3098` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3099` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x309a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x309e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x309f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30a0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30a1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30a8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30b2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30b3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30b4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30b5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x30c5` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x30c6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x30c7` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x30c8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x30c9` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x30ca` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30f8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30f9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30fa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30fb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30fc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30fd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x30fe` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3102` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3103` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3104` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3105` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3116` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3117` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3118` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x315c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x315d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x315e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x315f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3160` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3161` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3162` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3166` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3167` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3168` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x316a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x316b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3170` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x317a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x317b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x317c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x317d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x317f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3180` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3184` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3185` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3189` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x318a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x318b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x318e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x318f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3190` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3193` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3198` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3199` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x319a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x319b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31a2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31a3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31a4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31c0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31c1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31c2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31c3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31c4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31ca` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31cb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31cc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31cd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31ce` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31cf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31d0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31f2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31f3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31f4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31fc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31fd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31fe` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x31ff` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3200` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3201` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3202` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3203` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3206` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3207` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3208` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3209` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x320a` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3210` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3211` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3212` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x321a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x321b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x321c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x321d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x321e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x321f` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3220` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3221` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3224` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3225` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3226` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3227` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3228` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3229` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x322a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x322b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x322c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x322d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x322e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x322f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3230` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3231` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3232` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3233` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3234` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3235` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3236` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3237` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3238` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3239` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x323a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x323b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x323c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x323d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x323e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x323f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3240` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3241` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3242` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3243` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3244` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3245` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3246` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3247` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3248` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3249` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x324a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x324b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x324c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x324d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x325c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x325d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x325e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x325f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3260` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3261` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3262` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3263` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3264` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3265` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3266` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3267` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3268` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3269` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x326a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x326b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x326c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x326d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x326e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x326f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3270` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3271` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3272` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3273` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3274` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3275` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3276` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3277` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3278` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3279` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x327a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x327b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x327c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x327d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x327e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x327f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3280` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3281` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3282` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3283` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3288` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3289` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x328a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x328b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x328c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x328d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x328e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x328f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3290` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3292` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3293` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3294` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3295` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3296` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3297` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x329b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x329c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x329d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x329e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x329f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32a0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32ab` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32b9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32ba` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32bb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32c3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32c4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32c5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32c7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32cd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32cf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x32d0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3350` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3351` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3352` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3353` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3355` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3356` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3357` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x335a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x335b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x335c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x335d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3364` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3365` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3366` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3367` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3368` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x336e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x336f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3370` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x337d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x337e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x337f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3380` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3381` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3382` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3383` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3384` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3385` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3386` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3387` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3388` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3389` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x338a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33b4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33b5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33b9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33ba` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33bb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33be` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33bf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33c8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33c9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33ca` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33d2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33d3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33d4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33e6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33e7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33e8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33e9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33ea` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33eb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33ec` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33ed` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33fa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33fb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33fc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33fd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33fe` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x33ff` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3400` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3418` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3419` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x341a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x341b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x341e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x341f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3422` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3423` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3424` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3425` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3426` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x342c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x342d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3436` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3437` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3438` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3439` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x343b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x343c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x343d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x343e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3440` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3441` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3442` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x344a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x344b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x344c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3454` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x345e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x345f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3460` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x347c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x347d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x347e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x347f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3480` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3481` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3482` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3486` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3487` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3488` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3489` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3490` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x349a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x349b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x349c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x349d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34ad` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34ae` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34af` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34b0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34b1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34b2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34e0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34e1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34e2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34e3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34e4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34e5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34e6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34ea` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34eb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34ec` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34ed` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34fe` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x34ff` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3500` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3544` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3545` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3546` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3547` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3548` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3549` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x354a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x354e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x354f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3550` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3552` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3553` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3558` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3562` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3563` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3564` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3565` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3567` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3568` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x356c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x356d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3571` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3572` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3573` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3576` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3577` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3578` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x357b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3580` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3581` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3582` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3583` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x358a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x358b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x358c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35a8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35a9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35aa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35ab` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35ac` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35b2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35b3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35b4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35b5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35b6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35b7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35b8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35da` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35db` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35dc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35e4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35e5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35e6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35e7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35e8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35e9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35ea` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35eb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35ee` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35ef` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35f0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35f1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35f2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35f8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35f9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x35fa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3602` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3603` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3604` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3605` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3606` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3607` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3608` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3609` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x360c` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x360d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x360e` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3611` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3614` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3615` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3616` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3617` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3618` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3619` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x361a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x361b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x361c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x361d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x361e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x361f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3620` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3621` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3622` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3623` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3624` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3625` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3626` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3627` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3628` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3629` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x362a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x362b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x362c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x362d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x362e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x362f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3630` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3631` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3632` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3633` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3634` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3635` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3644` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3645` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3646` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3647` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3648` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3649` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x364a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x364b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x364c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x364d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x364e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x364f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3650` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3651` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3652` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3653` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3654` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3655` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3656` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3657` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3658` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3659` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x365a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x365b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x365c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x365d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x365e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x365f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3660` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3661` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3662` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3663` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3664` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3665` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3666` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3667` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3668` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3669` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x366a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x366b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3741` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3742` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3743` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3747` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3748` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x374a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x374b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x374c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38ca` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38cb` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x38cd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38ce` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38d0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38d1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38d3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38d4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38d9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38da` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38dc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38dd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38df` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38e0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38e8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38e9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38ea` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38eb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38ec` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38ed` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38ee` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38ef` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38f0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38f1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38f2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x38f3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3906` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3907` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3909` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x390a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x390c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x390d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x390f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3910` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3912` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3913` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3914` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3924` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3925` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3927` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3928` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x392a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x392b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3951` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3952` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3954` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3955` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3957` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3958` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3959` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x395a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x395b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x395c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x395d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x395e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3960` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3961` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3963` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3964` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3966` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3967` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3969` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x396a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x396b` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x396c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x396d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x396f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3970` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3971` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3972` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3973` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3975` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3976` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3978` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3979` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x39f6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x39f7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x39f8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x39f9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x39fa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x39fb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a05` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a06` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a08` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a09` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a14` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a15` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a16` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a17` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a18` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a19` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a32` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a33` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a35` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a36` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a37` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a38` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a39` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a3a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a50` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a51` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a52` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a53` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a54` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a56` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a57` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a58` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a8c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a8d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a8e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a8f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a90` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a91` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a92` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a93` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a94` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a95` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a96` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a98` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a99` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a9a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a9b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a9c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a9d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a9e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3a9f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3aa1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3aa2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ac8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ac9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3aca` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3acb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3acc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ace` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3acf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ad1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ad2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ad4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ad5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ad6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ad7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ad8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ad9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ada` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3adb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3adc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b22` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b23` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b24` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b25` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b26` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b27` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b28` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b29` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b2a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b2b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b2c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b34` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b35` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b37` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b38` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b40` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b41` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b42` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b43` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b44` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b45` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b46` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b47` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b48` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3b49` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b4a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b4b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b4c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b4d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b4e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b5e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b5f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b60` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b61` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b62` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b63` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b7c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b7d` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3b7e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b7f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b80` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b81` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b82` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b83` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b85` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b86` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b8b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b8c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b8d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b8e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b8f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b90` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b91` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b92` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b93` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b94` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b95` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b9a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b9b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b9c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b9d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b9e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3b9f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ba0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ba1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ba2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bb8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bb9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bba` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bbb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bbc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bbd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bbe` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bbf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bc0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bd6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bd7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bd8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bf4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bf5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bf6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bf7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bf8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bf9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bfa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bfb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3bfc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c4e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c4f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c51` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3c52` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c54` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c55` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c57` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c58` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c59` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c5a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c5b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c5c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c5d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c5e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c5f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c60` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c61` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c6c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c6d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c6f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c70` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c71` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c72` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c73` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c74` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c75` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c76` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c77` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c8a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c8b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3c8c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ca8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ca9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cab` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cac` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cad` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cae` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3caf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cb0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cb1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cb2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ce1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ce2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ce3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ce4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ce5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ce6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ce7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ce8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ce9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cea` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ceb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ced` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cee` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cef` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cf0` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3cf1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3cf2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d7a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d7b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d7c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d7d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d7e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d7f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d80` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d81` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d82` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d83` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d84` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d86` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d87` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d89` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d8a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d8c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d8d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d98` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d99` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d9a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d9b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d9c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d9d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d9e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3d9f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3da0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3da1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3da2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3da3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3dd4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3dd5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3dd6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3dd7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3dd8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3dd9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3dda` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ddb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ea6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ea7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ea9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3eaa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3eac` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ead` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3eaf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3eb0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3eb2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3eb3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3eb5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3eb6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3eb8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3eb9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ec4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ec5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ec6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ec7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ec8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ec9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3eca` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ecb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ed0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ed1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ed2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ed3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ed4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ed5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ee2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ee3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f00` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f01` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f02` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3f03` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f04` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f06` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f07` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f08` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f09` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f0a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f0f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f10` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f11` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f12` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f13` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f14` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f1e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f1f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f20` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f21` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f22` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f23` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f2d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f2e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f30` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f31` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f32` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f33` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f34` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f35` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f3c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f3d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f3e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f3f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f40` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f41` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f42` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f43` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f44` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f4b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f4c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f4d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f5a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f5b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f5c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f5d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f5e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f60` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f61` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f63` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f64` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f78` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f79` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f7a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f7b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f7c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f7d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f7e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f7f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3f80` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fd2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fd3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fd4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fd5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fd6` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x3fd7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fd8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fd9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fda` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fdb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fdc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fde` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fdf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ff0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ff1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ff2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ff3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ff4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ff6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ff7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ff9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ffa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ffc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3ffd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x3fff` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4000` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4002` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4003` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4068` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4069` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x406a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x406b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x406c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x406e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x406f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4070` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4086` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4087` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4089` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x408a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x408b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x408c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x408d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x408e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x408f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4090` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4091` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4092` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4093` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4094` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4095` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4096` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4098` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4099` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x409b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x409c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x409d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40a4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40a5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40a7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40a8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40aa` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x40ab` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40ad` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40ae` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40af` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40b0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40b1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40b2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40c2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40c3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40c5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40c6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40c7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40c8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40c9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40ca` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x40e0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40e1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40e2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40e3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40e4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40e6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40e7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40e9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40ea` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40ec` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40ed` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40ef` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40f0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40f2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40f3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40f5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40f6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40fe` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x40ff` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4101` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4102` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4104` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4105` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4107` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4108` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x410a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x410b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x410d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x410e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4110` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4111` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4113` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4114` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4116` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4117` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4119` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x411a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x411c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x411d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x411f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4120` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4122` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4123` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4125` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4126` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4128` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4129` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x412b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x412c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x412e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x412f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4131` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4132` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4134` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4135` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4137` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4138` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x413a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x413b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x413d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x413e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4140` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4141` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4143` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4144` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4146` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4147` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4149` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x414a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x414c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x414d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x414f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4150` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4152` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4153` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4155` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4156` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4158` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4159` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x415b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x415c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x415e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x415f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4161` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4162` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4164` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4165` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4167` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4168` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x416a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x416b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x416d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x416e` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4170` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4171` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4173` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4174` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4176` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4177` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4179` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x417a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41a6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41a7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41a9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41aa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41ac` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41ad` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41af` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41b0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41b2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41b3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41b5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41b6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41b8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41b9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41bb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41bc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41be` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41bf` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41c1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41c2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41c4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41c5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41c7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41c8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41ca` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41cb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41cd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41ce` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41d0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41d1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41d3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41d4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41d6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41d7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41d9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41da` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41dc` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41dd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41df` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41e0` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41e2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41e3` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41e5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41e6` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41e8` | — | med | auto-shape | string-table (written by EBINIT) |
| `0x41e9` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41eb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41ec` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41ee` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41ef` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41f1` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41f2` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41f4` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41f5` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41f7` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41f8` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41fa` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41fb` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41fd` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x41fe` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4200` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4201` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4203` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4204` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4206` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4207` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4209` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x420a` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x420c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x420d` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x420f` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4210` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4212` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4213` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4215` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4216` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4218` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4219` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x421b` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x421c` | — | med | auto-shape | string-table (written by EBINIT) |
| `GS 0x4229` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x422a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x422b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x422c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x422d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x422e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x422f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4230` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4231` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4232` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4233` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4234` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4235` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4236` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4238` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4239` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x423a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x423c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x423d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x423f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4240` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4241` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4242` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4243` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4244` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4245` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4246` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4247` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4248` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4249` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x424a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x424b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x424c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x424d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x424e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x424f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4250` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4251` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4252` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4253` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4254` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4255` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4256` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4258` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x428d` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x4299` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x429a` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x429b` | — | med | auto-shape | string-table (written by ITINIT) |
| `0x429c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42ab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42ac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42ad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42ae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42af` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42b0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42b1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42bf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42c0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42c1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42c2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42c3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42c4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42c5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42d3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42d4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42d5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42d6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42d7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42f1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42f3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42fb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x42fc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x430f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4310` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4311` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4312` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4313` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4314` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4315` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4316` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4317` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4318` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4319` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x431a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x431b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x431c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x431d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x431e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x431f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4320` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4321` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x432d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x432e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x432f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4330` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4331` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4332` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4333` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4334` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4335` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4337` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4338` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4339` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x433a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x433b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x433c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x433d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x433e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x433f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4340` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4341` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4342` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4343` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4344` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4345` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4346` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4347` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4348` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4349` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x434a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x434b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x434c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4355` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4356` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4357` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4358` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4359` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x435a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x435b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x435c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x435d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x435e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x435f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4360` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4361` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4362` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4363` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4364` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4365` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4366` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4367` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4368` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4369` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x436a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x436b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x436c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x436d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x436e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x436f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4370` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4371` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4372` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4373` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4374` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4375` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4376` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4377` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4378` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4379` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x437a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x437b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x437c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x437d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x437e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x437f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4380` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4381` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4382` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4383` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4384` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4385` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4386` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4387` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4388` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4389` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x438a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x438b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x438c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x438d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x438e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x438f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4390` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4391` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4392` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4393` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4394` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4395` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4396` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4397` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4398` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4399` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43a4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43a5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43a6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43a7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43a8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43a9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43aa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43af` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43b0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43b1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43b2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43b3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43b4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43b5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43b6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43b7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43b8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43b9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ba` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43bb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43bc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43bd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43be` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43bf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43c0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43c1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43c2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43c3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43c4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43c5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43c6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43c7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43c8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43c9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43cb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43cc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43cd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ce` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43cf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43d0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43d1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43d2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43d3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43d4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43d5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43d6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43d7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43d8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43d9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43da` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43db` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43dc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43dd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43de` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43df` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43e0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43e1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43e2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43e3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43e4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43e5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43e6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43e7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43e8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43e9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ea` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43eb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ed` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43f0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43f1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43f2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43f3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43f4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43f5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43f6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43f7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43f8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43f9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43fa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43fb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43fc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43fd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43fe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x43ff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4400` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4401` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4402` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4403` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4404` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4405` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4406` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4407` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4408` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4409` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x440a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x440b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x440c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x440d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x440e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x440f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4410` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4411` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4412` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4413` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4414` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4415` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4416` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4417` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4418` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4419` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x441a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x441b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x441c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x441d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x441e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x441f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4420` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4421` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4422` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4423` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4424` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4425` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4431` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4432` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4433` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4438` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4439` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x443a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x443b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x443c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x443d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x443e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x443f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4440` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4441` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4442` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4443` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4444` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4449` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x444a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x444b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x444c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x444d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x444e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x444f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4450` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4451` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4452` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4453` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4454` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4455` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4456` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4457` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4458` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4459` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x445a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x445b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x445c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x445d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x445e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x445f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4460` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4461` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4462` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4463` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4464` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4465` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4466` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4467` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4468` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4469` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x446a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x446b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x446c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x446d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x446e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x446f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4470` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4471` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4472` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4473` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4474` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4475` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4476` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4477` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4478` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4479` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x447a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x447b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x447c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4481` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4482` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4483` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4484` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4485` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4486` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4487` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4488` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x448b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x448c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x448d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4491` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4492` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4493` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4494` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4495` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4496` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4497` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4498` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4499` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x449a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x449b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x449c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x449d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x449e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x449f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44a0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44a1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44a2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44a3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44a4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44a5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44a6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44a7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44a8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44a9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44aa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44ab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44ac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44ad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44ae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44af` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44b0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44b2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44b3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44b4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44b5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44b7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44bd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44be` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44bf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44c0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44c1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44c2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44c3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44c4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44c5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44c6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44c7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44c8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44c9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44ca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44cb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44cc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44ce` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44d0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44db` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44dc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44dd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44e5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44e6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44e7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44e8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44eb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44ec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44ed` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44ee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44ef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44f0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44f1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44f2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44f3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44f4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44f5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44f9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44fa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44fb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44fc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x44fd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4503` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4504` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4505` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4506` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4507` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4509` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x450a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x450d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x450e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4510` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4511` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4512` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4513` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4514` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4515` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4516` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4517` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4518` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4519` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4521` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4522` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4523` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4524` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4525` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4526` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4527` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4528` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4529` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x452a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x452b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x452d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x452e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x452f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4530` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4536` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4537` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x453f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4540` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4549` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x454a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4553` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4554` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4555` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4567` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4571` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4572` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4573` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4574` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4575` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4576` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4577` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x457a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x457b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x457c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x458f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4590` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4591` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4593` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4594` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4595` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x459f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45a0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45a1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45a2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45a3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45a4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45a5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45a6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45a7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45a8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45a9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45aa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45ab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45ad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45ae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45b1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45b2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45b3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45b7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45b8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45b9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45ba` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45bb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45bc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45bd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45be` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45bf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45c0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45c1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45c2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45c3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45c4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45c5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45c6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45cb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45cc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45cd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45ce` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45cf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45d0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45d1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45d2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45d3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45d4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45d5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45d6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45d7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45d8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45d9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45da` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45de` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45df` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45e0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45e1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45e2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45e3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45e4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45e5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45e6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45e7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45e8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45e9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45ea` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45eb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45ec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45ed` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45ee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45ef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45f0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45f1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45f3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45f4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45f5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45f6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45f7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45f8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45f9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45fa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45fb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x45fd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4607` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4608` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4609` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x460a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x460b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x460c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4610` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4611` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4612` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4613` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4614` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4615` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4616` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4617` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x461a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x461b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x461c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x461e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4620` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4621` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4622` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4623` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4624` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4625` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4626` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4629` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x462a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x462b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x462c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x462d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x462e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x462f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4674` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4675` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4676` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4677` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4678` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4679` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x467a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x467b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x467c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x467d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x467e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x467f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4680` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4681` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4682` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4683` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4684` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4686` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4688` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x468a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x468b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x468c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x468d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x468e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x468f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4690` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4692` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46d8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46d9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46da` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46db` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46dc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46dd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46de` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46df` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46e0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46e1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46e2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46e7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46e8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46ec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46ed` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46ee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46ef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46f0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46f1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46f2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46f3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46f5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46f6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46f7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46f8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46f9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x46fa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x473c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x473d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x473e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x473f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4740` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4742` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4743` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4744` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4746` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4747` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4749` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x474a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x474b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x474d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x474e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x474f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4750` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4751` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4753` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4754` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4756` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4757` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x475b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x475c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x475d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x475e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x475f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4761` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4762` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47a0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47a1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47a2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47a3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47a4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47a5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47a6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47a7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47a8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47a9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47aa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47ac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47ad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47d2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47d3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47d4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47d5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47d6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47d7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47d8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47d9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47da` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47db` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47dc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47dd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47de` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47df` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47e0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x47e1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4804` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4805` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4806` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4807` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4808` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4809` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x480a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x480b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x480c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x480d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x480e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x480f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4810` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4811` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4812` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4813` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4814` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4837` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4838` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4839` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x483a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x483b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x483c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x483e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x483f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4840` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4841` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x489a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x489b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x489c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x489d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x489e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x489f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48a0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48a1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48a2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48a3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48a4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48a6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48a7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48a8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48a9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48aa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48ab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48ac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48ad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48cc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48cf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48d0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48d1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48d2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48d4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48d6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48d7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48d8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48d9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x48ff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4900` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4901` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4902` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4903` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4904` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4905` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4906` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4907` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4908` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4909` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x490a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x490b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x490c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4931` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4932` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4933` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4934` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x493b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x493c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x493d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x493e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4945` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4946` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4947` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4948` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x494f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4950` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4951` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4952` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x495a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x495b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x495c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x495d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x495e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x495f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4960` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4961` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4962` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4963` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4964` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4965` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4966` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4967` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4968` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4969` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x496a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x496b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x496c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x496d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x496e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x496f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4970` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4971` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4972` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4973` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4975` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4976` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4977` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4978` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4979` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x497a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x497b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x497c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x497d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x497e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x497f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4980` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4983` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4986` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4989` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x498c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x498f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4990` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4992` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4995` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4996` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4997` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4999` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x499a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x499b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x499d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x499e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x499f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49a0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49a1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49a2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49a3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49a4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49a5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49a6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49a7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49a8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49a9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49aa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49af` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49b0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49b1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49b2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49b3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49b4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49b5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49b6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49b7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49b8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49b9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ba` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49bb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49bc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49bd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49be` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49c0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49c1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49c2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49c3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49c4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49c5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49c6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49c7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49c8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49c9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49cb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49cc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49cd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ce` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49cf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49d0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49d1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49d2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49d3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49d4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49d5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49d6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49d7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49d8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49d9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49da` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49db` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49dc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49dd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49de` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49df` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49e0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49e1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49e2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49e3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49e4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49e5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49e6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49e7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49e8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49e9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ea` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ed` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49f0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49f1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49f2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49f3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49f4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49f5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49f6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49f9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49fa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49fb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49fc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49fd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49fe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x49ff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a00` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a01` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a02` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a03` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a04` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a05` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a06` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a08` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a09` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a0a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a0c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a0d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a0f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a10` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a11` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a12` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a13` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a14` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a15` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a16` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a17` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a18` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a19` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a1a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a1b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a1c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a1d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a1e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a1f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a20` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a21` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a22` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a23` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a24` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a25` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a26` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a28` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a5d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a5e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a67` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a68` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a69` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a6a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a6b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a6c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a7b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a7c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a7d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a7e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a7f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a80` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a81` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a8f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a90` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a91` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a92` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a93` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a94` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4a95` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ac1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ac3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4acb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4acc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4adf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ae0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ae1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ae2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ae3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ae4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ae5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ae6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ae7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ae8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ae9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4aea` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4aeb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4aec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4aed` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4aee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4aef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4af0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4af1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4afd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4afe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4aff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b00` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b01` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b02` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b03` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b04` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b05` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b07` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b08` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b09` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b0a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b0b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b0c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b0d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b0e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b0f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b10` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b11` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b12` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b13` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b14` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b15` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b16` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b17` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b18` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b19` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b1a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b1b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b1c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b25` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b26` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b27` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b28` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b29` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b2a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b2b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b2c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b2d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b2e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b2f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b30` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b31` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b32` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b33` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b34` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b35` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b36` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b37` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b38` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b39` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b3a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b3b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b3c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b3d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b3e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b3f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b40` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b41` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b42` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b43` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b44` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b45` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b46` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b47` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b48` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b49` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b4a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b4b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b4c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b4d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b4e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b4f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b50` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b51` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b52` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b53` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b54` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b55` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b56` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b57` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b58` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b59` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b5a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b5b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b5c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b5d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b5e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b5f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b60` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b61` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b62` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b63` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b64` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b65` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b66` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b67` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b68` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b69` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b74` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b75` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b76` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b77` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b78` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b79` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b7a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b7b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b7c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b7d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b7e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b7f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b80` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b81` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b82` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b83` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b84` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b85` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b86` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b87` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b88` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b89` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b8a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b8b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b8c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b8d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b8e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b8f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b90` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b91` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b92` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b93` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b94` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b95` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b96` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b97` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b98` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b99` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b9a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b9b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b9c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b9d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b9e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4b9f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ba0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ba1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ba2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ba3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ba4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ba5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ba6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ba7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ba8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ba9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4baa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4baf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bb0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bb1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bb2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bb3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bb4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bb5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bb6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bb7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bb8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bb9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bba` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bbb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bbc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bbd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bbe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bbf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bc0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bc1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bc2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bc3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bc4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bc5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bc6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bc7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bc8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bc9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bcb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bcc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bcd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bce` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bcf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bd0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bd1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bd2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bd3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bd4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bd5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bd6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bd7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bd8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bd9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bda` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bdb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bdc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bdd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bde` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bdf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4be0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4be1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4be2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4be3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4be4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4be5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4be6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4be7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4be8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4be9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bea` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4beb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bed` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bf0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bf1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bf2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bf3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bf4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4bf5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c01` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c02` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c03` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c08` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c09` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c0a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c0b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c0c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c0d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c0e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c0f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c10` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c11` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c12` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c13` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c14` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c19` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c1a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c1b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c1c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c1d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c1e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c1f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c20` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c21` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c22` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c23` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c24` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c25` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c26` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c27` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c28` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c29` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c2a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c2b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c2c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c2d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c2e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c2f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c30` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c31` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c32` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c33` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c34` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c35` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c36` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c37` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c38` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c39` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c3a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c3b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c3c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c3d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c3e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c3f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c40` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c41` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c42` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c43` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c44` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c45` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c46` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c47` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c48` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c49` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c4a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c4b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c4c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c51` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c52` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c53` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c54` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c55` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c56` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c57` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c58` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c5b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c5c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c5d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c61` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c62` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c63` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c64` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c65` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c66` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c67` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c68` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c69` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c6a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c6b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c6c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c6d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c6e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c6f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c70` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c71` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c72` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c73` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c74` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c75` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c76` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c77` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c78` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c79` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c7a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c7b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c7c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c7d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c7e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c7f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c80` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c82` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c83` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c84` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c85` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c87` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c8d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c8e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c8f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c90` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c91` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c92` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c93` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c94` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c95` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c96` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c97` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c98` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c99` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c9a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c9b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c9c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4c9e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ca0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cb5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cb6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cb7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cb8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cbb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cbc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cbd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cbe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cbf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cc0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cc1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cc2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cc3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cc4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cc5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cc9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ccb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ccc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ccd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cd3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cd4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cd5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cd6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cd7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cd9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cda` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cdd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cde` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ce0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ce1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ce2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ce3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ce4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ce5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ce6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ce7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ce8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ce9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cf1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cf2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cf3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cf4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cf5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cf6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cf7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cf8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cf9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cfa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cfb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cfd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cfe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4cff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d00` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d06` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d07` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d0f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d10` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d19` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d1a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d23` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d24` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d25` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d37` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d41` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d42` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d43` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d44` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d45` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d46` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d47` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d4a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d4b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d4c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d5f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d60` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d61` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d63` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d64` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d65` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d6f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d70` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d71` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d72` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d73` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d74` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d75` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d76` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d77` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d78` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d79` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d7a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d7b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d7d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d7e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d81` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d82` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d83` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d87` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d88` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d89` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d8a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d8b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d8c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d8d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d8e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d8f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d90` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d91` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d92` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d93` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d94` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d95` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d96` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d9b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d9c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d9d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d9e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4d9f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4da0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4da1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4da2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4da3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4da4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4da5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4da6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4da7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4da8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4da9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4daa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4daf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4db0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4db1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4db2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4db3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4db4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4db5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4db6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4db7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4db8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4db9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dba` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dbb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dbc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dbd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dbe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dbf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dc0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dc1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dc3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dc4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dc5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dc6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dc7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dc8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dc9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dcb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dcd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dd7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dd8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dd9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dda` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ddb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ddc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4de0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4de1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4de2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4de3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4de4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4de5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4de6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4de7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dea` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4deb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4df0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4df1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4df2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4df3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4df4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4df5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4df6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4df9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dfa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dfb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dfc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dfd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dfe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4dff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e44` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e45` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e46` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e47` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e48` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e49` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e4a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e4b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e4c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e4d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e4e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e4f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e50` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e51` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e52` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e53` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e54` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e56` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e58` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e5a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e5b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e5c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e5d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e5e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e5f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e60` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4e62` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ea8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ea9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4eaa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4eab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4eac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ead` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4eae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4eaf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4eb0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4eb1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4eb2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4eb7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4eb8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ebc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ebd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ebe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ebf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ec0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ec1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ec2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ec3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ec5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ec6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ec7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ec8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4ec9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4eca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f0c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f0d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f0e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f0f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f10` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f12` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f13` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f14` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f16` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f17` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f19` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f1a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f1b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f1d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f1e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f1f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f20` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f21` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f23` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f24` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f26` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f27` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f2b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f2c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f2d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f2e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f2f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f31` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f32` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f70` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f71` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f72` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f73` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f74` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f75` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f76` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f77` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f78` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f79` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f7a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f7c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4f7d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fa2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fa3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fa4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fa5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fa6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fa7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fa8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fa9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4faa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4faf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fb0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fb1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fd4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fd5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fd6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fd7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fd8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fd9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fda` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fdb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fdc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fdd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fde` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fdf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fe0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fe1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fe2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fe3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x4fe4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5007` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5008` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5009` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x500a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x500b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x500c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x500e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x500f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5010` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5011` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x506a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x506b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x506c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x506d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x506e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x506f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5070` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5071` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5072` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5073` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5074` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5076` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5077` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5078` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5079` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x507a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x507b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x507c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x507d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x509c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x509f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50a0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50a1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50a2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50a4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50a6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50a7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50a8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50a9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50cf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50d0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50d1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50d2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50d3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50d4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50d5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50d6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50d7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50d8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50d9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50da` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50db` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x50dc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5101` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5102` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5103` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5104` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x510b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x510c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x510d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x510e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5115` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5116` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5117` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5118` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x511f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5120` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5121` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5122` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x512a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x512b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x512c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x512d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x512e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x512f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5130` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5131` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5132` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5133` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5134` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5135` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5136` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5137` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5138` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5139` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x513a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x513b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x513c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x513d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x513e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x513f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5140` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5141` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5142` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5143` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5145` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5146` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5147` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5148` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5149` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x514a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x514b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x514c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x514d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x514e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x514f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5150` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5153` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5156` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5159` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x515c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x515f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5160` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5162` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5165` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5166` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5167` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5169` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x516a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x516b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x516d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x516e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x516f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5170` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5171` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5172` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5173` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5174` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5175` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5176` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5177` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5178` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5179` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x517a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x517b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x517c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x517d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x517e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x517f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5180` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5181` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5182` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5183` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5184` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5185` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5186` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5187` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5188` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5189` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x518a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x518b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x518c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x518d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x518e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5190` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5191` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5192` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5193` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5194` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5195` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5196` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5197` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5198` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x5199` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x519a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x519b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x519c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x519d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x519e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x519f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51a0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51a1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51a2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51a3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51a4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51a5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51a6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51a7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51a8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51a9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51aa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51ab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51ac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51ad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51ae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51af` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51b0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51b1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51b2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51b3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51b4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51b5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51b6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51b7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51b8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51b9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51ba` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51bc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51bd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51be` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51bf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51c0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51c1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51c2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51c3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51c4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51c5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51c6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x51cb` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51d8` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51dd` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51e3` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51e4` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51e5` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51e9` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51ea` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51ed` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51ee` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51ef` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51f0` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51f1` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51f2` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51f3` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x51f8` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5238` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5239` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x523a` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x523b` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x523c` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x524c` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x524d` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x524e` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x524f` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5250` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5251` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5260` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5261` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5262` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5263` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5264` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5265` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x52bf` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x52c1` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x52f6` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x52f7` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x52f9` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x52fa` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x52ff` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5300` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5301` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x530c` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x531d` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x531f` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5323` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5324` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5325` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5326` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5327` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5328` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5346` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5347` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5349` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x534a` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x534c` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x534d` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5351` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5352` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5353` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5361` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x536c` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5372` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5373` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x537f` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5380` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5381` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5382` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5383` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5384` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5385` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5391` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5392` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5397` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5398` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5399` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x539e` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53a2` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53a3` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53a4` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53a5` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53a9` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53aa` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53ac` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53ad` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53ae` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53b0` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53bc` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53c2` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53c3` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53e9` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53ea` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53eb` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53ec` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53ed` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53ee` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53ef` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53f0` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53f1` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53f2` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53f3` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53f4` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53f5` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53f6` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53f7` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53f8` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53f9` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53fa` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53fb` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53fc` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53fd` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53fe` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x53ff` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5400` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5401` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5402` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5403` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5404` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5405` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5406` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5407` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5409` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x540a` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x540b` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x540c` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x558f` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x55b5` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x55b7` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x55c1` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x55c2` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x55c4` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x55c5` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x55cc` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x55cd` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x55ce` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5615` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5619` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5621` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5622` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5623` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5628` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x562b` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x562d` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x562e` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x562f` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5630` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x567a` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x567b` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x567c` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x567e` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x567f` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5680` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5681` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5688` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x568d` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x568f` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5696` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5697` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56dd` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56e4` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56e6` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56e7` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56ed` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56ef` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56f0` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56f7` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56fc` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56fd` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56fe` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x56ff` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5701` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5745` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x574a` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x574c` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x574d` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5775` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5777` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5779` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x577a` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x577b` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x577d` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x577e` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x577f` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x57a9` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x57ac` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x57ad` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x57b0` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x57b1` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x57d8` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x57d9` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x57db` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x57df` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x57e0` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x583c` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x583d` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x583e` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5844` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5848` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5849` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x584c` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x586f` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5872` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5877` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5878` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58a1` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58a2` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58a8` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58d1` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58d2` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58d3` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58d4` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58db` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58dc` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58dd` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58de` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58e5` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58e6` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58e7` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58e8` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58ef` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58f0` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58f1` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x58f2` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5903` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5904` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5905` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5906` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5907` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5908` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5909` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x590a` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5916` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5920` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5923` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5926` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5929` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x592c` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x592f` | — | med | auto-shape | string-table (written by ALINIT) |
| `GS 0x5999` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x599a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x599b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x599c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x599d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x599f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59a0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59a1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59a2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59a3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59a4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59a5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59ad` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59ae` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59b0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59b1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59b2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59b3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59b4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59b5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59b6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59b7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59b8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59b9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59ba` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59bb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59bc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59bd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59be` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59bf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59c0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59c1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59c2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59c3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59c4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59c5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59c6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59c7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59c8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59c9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59ca` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59cb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59cc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59cd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59ce` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59cf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59d0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59d1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59d2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59d3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59d4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59d5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59d6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59d7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59d8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59d9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59da` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59db` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59dc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59dd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59de` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59df` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59e0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59e1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59e2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59e3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59e4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59e5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59e6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59e7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59e8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59e9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59ea` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59eb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59ec` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59ed` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59ee` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59ef` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59f0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59f1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59f2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59f3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59f4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59f5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59f6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59f7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59f8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59f9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59fa` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59fb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59fc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59fd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59fe` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x59ff` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a03` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a04` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a05` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a06` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a07` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a08` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a09` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a0a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a0b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a0c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a0d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a0e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a0f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a10` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a11` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a16` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a17` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a18` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a19` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a1a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a1b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a1d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a1e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a1f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a20` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a21` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a22` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a23` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a24` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a25` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a26` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a27` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a28` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a29` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a2a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a2b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a2c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a2d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a2e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a61` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a62` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a63` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a64` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a65` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a66` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a67` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a68` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a69` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a6a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a6b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a6c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a6d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a6e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a6f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a70` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a71` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a72` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a73` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a74` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a75` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a76` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a77` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a78` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a79` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a7a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a7b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a7c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a7d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a7e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a7f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a80` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a81` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a82` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a83` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a84` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a85` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a86` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a89` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a8a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a8b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a8c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a8d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a8e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a8f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a90` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a91` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a92` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a93` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a94` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a95` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a96` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a97` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a98` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a99` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a9a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a9b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a9c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a9d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a9e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5a9f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aa0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aa1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aa2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aa3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aa4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aa5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aa6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aa7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aa8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aa9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aaa` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aab` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aac` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aad` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aae` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ab0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ab1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ab2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ab3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ab4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ab5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ab6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ab7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ab8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ab9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5aba` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5abb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5abc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ac7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5acb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5acc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5acd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ace` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5acf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ad0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ad1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ad2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b29` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b2a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b2b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b2c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b2d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b2e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b3d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b3e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b3f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b40` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b47` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b48` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b49` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b4a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b4b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b4c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b51` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b52` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b53` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b54` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b55` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b56` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b57` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b58` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b5b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b5c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b5d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b5e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b5f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b60` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b61` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b64` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b65` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b66` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b67` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b68` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b69` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b79` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b7a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b7b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b7c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b83` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b84` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b85` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b86` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b8d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b8e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b8f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b90` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b91` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b92` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b93` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b94` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b97` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b98` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5b99` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ba1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ba2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ba3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bab` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bac` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bad` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bb5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bbf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bc0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bc1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bc2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bc9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bca` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bcb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bf1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5bf2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c05` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c06` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c07` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c0f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c10` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c11` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c12` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c13` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c14` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c15` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c23` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c24` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c25` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c26` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c27` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c28` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c29` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c37` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c38` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c39` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c3a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c3b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c3c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c3d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c41` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c42` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c43` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c44` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c45` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c46` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c47` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c48` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c49` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c55` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c56` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c57` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c58` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c59` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c5a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c5f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c60` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c69` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c6a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c6b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c6c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c6d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c6e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c73` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c74` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c75` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c7d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c87` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c88` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c89` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c91` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c92` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c93` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c94` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c95` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c9b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c9c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c9d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c9e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5c9f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ca5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5caf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5cb0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5cb1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5cb2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5cb3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5cb4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5cb5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5cb6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d81` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d82` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d83` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d84` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d85` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d87` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d88` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d89` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d8a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d8c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d8d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d95` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d96` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d98` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d99` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d9a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d9b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d9c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d9d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d9e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5d9f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5da0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5da1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5da2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5da3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5da4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5da5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5da6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5da7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5da8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5da9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5daa` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dab` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dac` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dad` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dae` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5daf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5db0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5db1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5db2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5db3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5db4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5db5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5db6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5db7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5db8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5db9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dba` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dbb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dbc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dbd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dbe` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dbf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dc0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dc1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dc2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dc3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dc4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dc5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dc6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dc7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dc8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dc9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dca` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dcb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dcc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dcd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dce` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dcf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dd0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dd1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dd2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dd3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dd4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dd5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dd6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dd7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dd8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dd9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dda` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ddb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ddc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ddd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dde` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ddf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5de0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5de1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5de2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5de3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5de4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5de5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5de6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5de7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5deb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dec` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ded` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dee` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5def` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5df0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5df1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5df2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5df3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5df4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5df5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5df6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5df7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5df8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5df9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dfe` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5dff` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e00` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e01` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e02` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e03` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e05` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e06` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e07` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e08` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e09` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e0a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e0b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e0c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e0d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e0e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e0f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e10` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e11` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e12` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e13` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e14` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e15` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e16` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e49` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e4a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e4b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e4c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e4d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e4e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e4f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e50` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e51` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e52` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e53` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e54` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e55` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e56` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e57` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e58` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e59` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e5a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e5b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e5c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e5d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e5e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e5f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e60` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e61` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e62` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e63` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e64` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e65` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e66` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e67` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e68` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e69` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e6a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e6b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e6c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e6d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e6e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e71` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e72` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e73` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e74` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e75` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e76` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e77` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e78` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e79` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e7a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e7b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e7c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e7d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e7e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e7f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e80` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e81` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e82` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e83` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e84` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e85` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e86` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e87` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e88` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e89` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e8a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e8b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e8c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e8d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e8e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e8f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e90` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e91` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e92` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e93` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e94` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e95` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e96` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e98` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e99` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e9a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e9b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e9c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e9d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e9e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5e9f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ea0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ea1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ea2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ea3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ea4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5eaf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5eb3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5eb4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5eb5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5eb6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5eb7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5eb8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5eb9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5eba` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f11` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f12` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f13` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f14` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f15` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f16` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f25` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f26` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f27` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f28` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f2f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f30` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f31` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f32` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f33` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f34` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f39` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f3a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f3b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f3c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f3d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f3e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f3f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f40` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f43` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f44` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f45` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f46` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f47` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f48` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f49` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f4c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f4d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f4e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f4f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f50` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f51` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f61` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f62` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f63` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f64` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f6b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f6c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f6d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f6e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f75` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f76` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f77` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f78` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f79` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f7a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f7b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f7c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f7f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f80` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f81` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f89` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f8a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f8b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f93` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f94` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f95` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5f9d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5fa7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5fa8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5fa9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5faa` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5fb1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5fb2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5fb3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5fd9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5fda` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5fed` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5fee` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5fef` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ff7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ff8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ff9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ffa` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ffb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ffc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x5ffd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x600b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x600c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x600d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x600e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x600f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6010` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6011` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x601f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6020` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6021` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6022` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6023` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6024` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6025` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6029` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x602a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x602b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x602c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x602d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x602e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x602f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6030` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6031` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x603d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x603e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x603f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6040` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6041` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6042` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6047` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6048` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6051` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6052` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6053` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6054` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6055` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6056` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x605b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x605c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x605d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6065` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x606f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6070` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6071` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6079` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x607a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x607b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x607c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x607d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6083` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6084` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6085` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6086` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6087` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x608d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6097` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6098` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x6099` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x609a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x609b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x609c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x609d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x609e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0x616d` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6172` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6173` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6174` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6175` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6176` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6177` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6178` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6179` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x617a` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x617b` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x617c` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x617d` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x617e` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x617f` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6180` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6181` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6182` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6183` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6184` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6185` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6186` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6187` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6188` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6189` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x618a` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x618b` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x618c` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x618d` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x618e` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x618f` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6190` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6191` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6192` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6193` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6194` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6195` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6196` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6197` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6198` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6199` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x619a` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x619b` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x619c` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x619d` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x619e` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x619f` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61a0` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61a1` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61a2` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61a3` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61a4` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61a5` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61a6` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61a7` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61a8` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61a9` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61aa` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ab` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ac` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ad` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ae` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61af` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61b0` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61b1` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61b2` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61b3` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61b4` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61b5` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61b6` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61b7` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61b8` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61b9` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ba` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61bb` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61bc` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61bd` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61be` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61bf` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61c0` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61c1` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61c2` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61c3` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61c4` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61c5` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61c6` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61c7` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61c8` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61c9` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ca` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61cb` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61cc` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61cd` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ce` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61cf` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61d0` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61d1` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61d2` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61d3` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61d4` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61d5` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61d6` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61d7` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61d8` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61d9` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61da` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61db` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61dc` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61dd` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61de` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61df` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61e0` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61e1` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61e2` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61e3` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61e4` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61e5` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61e6` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61e7` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61e8` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61e9` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ea` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61eb` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ec` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ed` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ee` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61ef` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x61f4` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x6200` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6201` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6202` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6203` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6204` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6205` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6206` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6207` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6208` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6209` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x620a` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x620b` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x620c` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x620d` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x620e` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6212` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6213` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6214` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6215` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6216` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6217` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6218` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6219` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x621b` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x621c` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x621d` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x621e` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x621f` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6220` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6221` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6222` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6223` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6224` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6225` | — | med | auto-shape | string-table (written by AFINIT) |
| `GS 0x6226` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6227` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6228` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6229` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x622a` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x622b` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x622c` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x622d` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x622e` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x622f` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6230` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x623a` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x623b` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x623c` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x623d` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x623e` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x623f` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6240` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6242` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6243` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6244` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6245` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6246` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6247` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6248` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6249` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x624a` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x624b` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x624c` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x624d` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x624e` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x624f` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6250` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6251` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6252` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6253` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6254` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6255` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6256` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6257` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6258` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6259` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x625a` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x6260` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6288` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6289` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x628a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x628b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x629c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x629d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x629e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x629f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62b0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62b1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62b2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62b3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62b4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62b5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62b6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62b7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62b8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62c4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62c5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62c6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62c7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62ca` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62cb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62cc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62cd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62ce` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62cf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62d8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62d9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62da` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62ec` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62ed` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x62ee` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6300` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6301` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6302` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6303` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6304` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6305` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6306` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6307` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6308` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6328` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6329` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x632a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x632b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x632c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x632d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x632e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x632f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x633c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x633d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x633e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x633f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6350` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6351` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6352` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6364` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6365` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6366` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6378` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6379` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x638c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x639f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63a0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63a1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63a2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63a3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63a4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63a5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63a6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63b4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63b5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63b6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63b7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63b8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63b9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63ba` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63bb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63bc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63bd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63be` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63bf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63c0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63f0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63f1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63f2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63f3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63f4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63f5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63f6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63f7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63f8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63f9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63fa` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63fb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63fc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63fd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63fe` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x63ff` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6400` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6401` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6402` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6648` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6649` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x664b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x664c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x664d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x664e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x664f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6650` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6651` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6652` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6653` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x665c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x665d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x665e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x665f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6660` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6661` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6662` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6663` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6664` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6665` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6666` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6667` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6668` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6669` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x666a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x666b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x666c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x666d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x666e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x666f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6a38` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6a39` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6a3a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6a3d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6a3f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ba0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ba1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ba2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ba5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ba6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ba7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ba9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6baa` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bab` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bae` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6baf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bb0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bb2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bb3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bb4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bb5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bb7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bb8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bb9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bbb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bbc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bbd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bbe` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bbf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bc0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bc1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6bc2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c54` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c55` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c56` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c57` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c58` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c59` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c5a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c5b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c5d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c5e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c5f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c60` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c62` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c64` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c66` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c67` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c6b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c6d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c6f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c70` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c71` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c74` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6c76` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d08` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d09` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d0a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d0d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d0e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d0f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d11` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d12` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d13` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d16` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d17` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d18` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d1a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d1b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d1c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d1f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d21` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d23` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d24` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d25` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d26` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d27` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d28` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d29` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d2a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d2c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d2d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d2e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d2f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d31` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d32` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d33` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d35` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d36` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d37` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d3a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d3b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d3c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d3e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d3f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d40` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d43` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d45` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d47` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d48` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d4c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d4e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d50` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d51` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d52` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d55` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6d57` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dbc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dbd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dbe` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dc1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dc2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dc3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dc5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dc6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dc7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dc8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dca` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dcb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dcc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dce` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dcf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dd0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dd3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dd4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dd5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dd7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dd8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dd9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dda` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ddc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ddd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dde` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6df2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6df3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6df4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6df5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6df7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6df8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6df9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dfb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dfc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6dfd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e00` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e02` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e04` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e05` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e09` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e0b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e0d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e0e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e12` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e14` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e16` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e17` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e1b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e1d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e1f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e20` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e21` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e24` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e25` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e26` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e70` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e71` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e72` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e75` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e77` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e79` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e7a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e7b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e7e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e80` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e82` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e83` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e84` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e87` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e88` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6e89` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f24` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f25` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f26` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f29` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f2a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f2b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f2d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f2e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f2f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f32` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f33` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f34` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f36` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f37` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f38` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f3b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f3c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6f3d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fd8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fd9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fda` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fdd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fde` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fdf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fe1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fe2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fe3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fe6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fe7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fe8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fea` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6feb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fec` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6fef` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ff0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ff1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ff3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ff4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ff5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ff8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ff9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ffa` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ffc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ffd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x6ffe` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7001` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7002` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7003` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7005` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7006` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7007` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x700a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x700b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x700c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x700e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x700f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7010` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7011` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7013` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7015` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7017` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7018` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x701c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x701e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7020` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7021` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7025` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7027` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7140` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7141` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7142` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7145` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7146` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7147` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7149` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x714a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x714b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x714e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7150` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7152` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7153` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7157` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7158` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7159` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x715b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x715c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7160` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7162` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7164` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7165` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7166` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7169` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x716a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x716b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x716d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x716e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x716f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7172` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7174` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7176` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7177` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x717b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x717c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x717d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x717f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7180` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7184` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7186` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x71f4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x71f5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x71f6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x71f9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x71fb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x71fd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x71fe` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7202` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7203` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7204` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7206` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7207` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x720b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x720d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x720f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7210` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7214` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7215` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7216` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72a8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72a9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72aa` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72ab` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72ac` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72ad` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72ae` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72af` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72b1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72b2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72b3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72b4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72b6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72b7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72b8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72ba` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72bb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72bf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72c0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x72c1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x735c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x735d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x735e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7361` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7363` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7365` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7366` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7367` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7368` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x736a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x736b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x736c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x736e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x736f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7370` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7371` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7372` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7373` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7374` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7375` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7410` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7411` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7412` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7415` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7417` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7419` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x741a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x741b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x741e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x741f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7420` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x74c4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x74c5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x74c6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x74c7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x74c9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x74ca` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x74cb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x756f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7570` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7571` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7572` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7574` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7576` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7577` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7578` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7579` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x757a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x757d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x757e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x757f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7580` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7581` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7582` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7583` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7584` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7585` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7586` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7588` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x758a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x758b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x758c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x758f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7590` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7591` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7593` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7594` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7595` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7596` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7598` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x759a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x759c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x759d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x759e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x759f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75a0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75a1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75a2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75a3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75a5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75a6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75a7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75a8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75aa` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75ab` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75ac` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75ae` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75af` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75b0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75b3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x75b5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x762c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x762d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7631` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7633` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7635` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7636` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7637` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7638` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x763a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x763c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x763e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x763f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7640` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7641` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7643` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7645` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7647` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7648` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7649` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x764a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x764c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x764e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7650` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7651` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7652` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7653` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7655` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7657` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7659` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x765a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x765b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x765c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x765e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7660` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7662` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7663` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7664` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7665` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7667` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7669` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x766b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x766c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x766d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x766e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7670` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7672` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7674` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7675` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7676` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7677` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7679` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x767b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x767d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x767e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x767f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7680` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7682` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7684` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7686` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7687` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7688` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7689` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x768b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x768d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x768f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7690` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7691` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7692` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7694` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7696` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7698` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7699` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x769a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x769b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x769d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x769f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7848` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7849` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x784a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x784b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x784c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x784d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x784e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x784f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7851` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7852` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7853` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7854` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7855` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7856` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7857` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7858` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x785a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x785b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x785c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x785d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x785f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7860` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7861` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7863` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7864` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7865` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7868` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x786a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x786c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x786d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x786e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x786f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7871` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7872` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7873` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7875` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7876` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x787a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x787c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x787e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x787f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7880` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7881` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7883` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7885` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7887` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7888` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x788c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x788e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7890` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7891` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7892` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7895` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7897` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x7899` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x789a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x789b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x789c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x789e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x789f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78a0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78a2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78a3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78a4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78a5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78a6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78a7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78a8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78a9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78ab` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78ac` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78ad` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78b0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78b1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78b2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78b4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78b5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78b6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78b7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78b9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78bb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78bd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78be` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78bf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78c0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78c2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78c4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78c6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78c7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78c8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78c9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78cb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78cd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78cf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78d0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78d1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78d2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78d4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78d6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78d8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78d9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78da` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78db` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78dc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78dd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78de` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78df` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78e1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78e2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78e3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78e6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78e8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78ea` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78eb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78ef` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0x78f1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb080` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0a8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0a9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0aa` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0ab` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0bd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0bf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0d0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0d1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0d2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0d3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0d4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0d5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0d6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0d7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0d8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0e4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0e5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0e6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0e7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0ea` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0eb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0ec` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0ed` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0ee` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0ef` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0f8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0f9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb0fa` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb10c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb10d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb10e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb120` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb121` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb122` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb123` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb124` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb125` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb126` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb127` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb128` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb148` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb149` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb14a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb14b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb14c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb14d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb14e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb14f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb15c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb15d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb15e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb15f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb170` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb171` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb172` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb184` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb185` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb186` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb198` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb199` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1ac` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1bf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1c0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1c1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1c2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1c3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1c4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1c5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1c6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1d4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1d5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1d6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1d7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1d8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1d9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1da` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1db` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1dc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1dd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1de` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1df` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb1e0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb210` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb211` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb212` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb213` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb214` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb215` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb216` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb217` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb218` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb219` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb21a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb21b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb21c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb21d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb21e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb21f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb220` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb221` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb222` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb468` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb469` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb46b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb46c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb46d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb46e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb46f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb470` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb471` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb472` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb473` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb850` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb878` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb879` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb87a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb87b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb88c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb88d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb88e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb88f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8a0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8a1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8a2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8a3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8a4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8a5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8a6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8a7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8a8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8b4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8b5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8b6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8b7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8ba` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8bb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8bc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8bd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8be` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8bf` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8c8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8c9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8ca` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8dc` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8dd` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8de` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8f0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8f1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8f2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8f3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8f4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8f5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8f6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8f7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb8f8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb918` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb919` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb91a` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb91b` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb91c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb91d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb91e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb91f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb92c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb92d` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb92e` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb92f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb940` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb941` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb942` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb954` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb955` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb956` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb968` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb969` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb97c` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb98f` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb990` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb991` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb992` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb993` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb994` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb995` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb996` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9a4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9a5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9a6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9a7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9a8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9a9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9aa` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9ab` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9ac` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9ad` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9ae` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9af` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9b0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9e0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9e1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9e2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9e3` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9e4` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9e5` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9e6` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9e7` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9e8` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9e9` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9ea` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9eb` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9ec` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9ed` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9ee` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9ef` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9f0` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9f1` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xb9f2` | — | med | auto-shape | string-table (written by QUINIT) |
| `GS 0xc020` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc021` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc022` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc023` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc024` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc025` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc026` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc028` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc029` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc02a` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc034` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc035` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc036` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc037` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc038` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc039` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc03a` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc048` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc049` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc04a` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc04b` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc04c` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc04d` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc04e` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc05c` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc05d` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc05e` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc05f` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc060` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc061` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc062` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc063` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc064` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc065` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc066` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc067` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc068` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc069` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc085` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc086` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc087` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc088` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc089` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc08a` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc08b` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc08c` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc08d` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc08e` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc08f` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc090` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc091` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc092` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc095` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc097` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc099` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0ad` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0ae` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0af` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0b0` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0b1` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0b2` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0b3` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0b4` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0b5` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0b6` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0b7` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0b8` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0b9` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0ba` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0d5` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0d6` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0d7` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0d8` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0d9` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0da` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0db` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0dc` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0dd` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0de` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0df` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0e0` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0e1` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0e2` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0fd` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0fe` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc0ff` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc100` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc101` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc102` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc103` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc104` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc105` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc106` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc107` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc108` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc109` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc10a` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc10b` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc10c` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc10d` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc10e` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc10f` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc110` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc111` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc112` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc113` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc114` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc115` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc116` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc117` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc118` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0xc14c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc14d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc14e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc14f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc156` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc160` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc161` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc162` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc16a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc16b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc16c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc16d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc174` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc175` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc176` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc177` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc178` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc179` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc17a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc17b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc17c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc17d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc17e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc17f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc180` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc181` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc182` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc183` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc184` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc185` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc186` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc187` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc188` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc189` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc18c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc18d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc18e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc18f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc190` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc191` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc192` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc193` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc194` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc195` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc196` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc197` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc198` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc199` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc19a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc19b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc19c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1a3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1a4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1a5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1a6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1a7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1a8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1a9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1aa` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ab` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ac` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ad` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ae` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1af` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1b0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1b1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1b2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1b3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1b4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1b5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1c6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1c7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1c8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1c9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ca` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1cb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1cc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1cd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ce` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1cf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1d0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1d1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1d2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1d3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1d4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1d5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1d6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1d7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1d8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1d9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1e6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1e7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1e8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1e9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ea` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1eb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ec` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ed` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ee` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ef` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1f0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1f1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1f2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1f3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc1ff` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc200` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc201` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc202` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc203` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc204` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc205` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc206` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc207` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc208` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc209` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc20a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc20b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc20c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc20d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc20e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc20f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc210` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc211` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc212` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc213` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc214` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc91c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc91d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc91e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc91f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc920` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc921` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc922` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc923` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc924` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc925` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc926` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc927` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc928` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc929` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc92a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc92b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc92c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc92d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc92e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc92f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc930` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc931` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc932` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc933` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc934` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc935` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc936` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc937` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc938` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc939` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc93a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc93b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc93c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc93d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc93e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc93f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc940` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc941` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc942` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc943` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc944` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc945` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc946` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc947` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc948` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc949` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc94a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc94b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc94c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc94d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc94e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc94f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc950` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc951` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc952` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc953` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc954` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc955` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc956` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc957` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc958` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc959` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc95a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc95b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc95c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc95d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc95e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc95f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc960` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc961` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc962` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc963` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc964` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc965` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc966` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc967` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc968` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc969` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc96a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc96b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc96c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc96d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc96e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc96f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc970` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc971` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc972` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc973` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc974` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc975` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc976` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc977` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc978` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc979` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc97a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc97b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc97c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc97d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc97e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc97f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc980` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc981` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc982` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc983` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc984` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc985` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc986` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc987` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc988` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc989` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc98a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc98b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc98c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc98d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc98e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc98f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc990` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc991` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc992` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc993` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc994` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc995` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc996` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc997` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc998` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc999` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc99a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc99b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc99c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc99d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc99e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc99f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9a0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9a1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9a2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9a3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9a4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9a5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9a6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9a7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9a8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9a9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9aa` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9ab` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9ac` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9ad` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9ae` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9af` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9b0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9b1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9b2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9b3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9b4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9b5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9b6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9b7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9b8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9b9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9ba` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9bb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9bc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9bd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9be` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9bf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9c0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9c1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9c2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9c3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9c4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9c5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9c6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9c7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9c8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9c9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9ca` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9cb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9cc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9cd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9ce` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9cf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9d0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9d1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9d2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9d3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9d4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9d5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9d6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9d7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9d8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9d9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9da` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9db` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9dc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9dd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9de` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9df` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9e0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9e1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9e2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9e3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9e4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9e5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xc9e6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0xd0ec` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0ed` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0ee` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0ef` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0f0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0f1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0f2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0f3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0f4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0f5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0f6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0f7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0f8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0f9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0fa` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0fb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0fc` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0fd` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0fe` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd0ff` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd100` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd101` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd102` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd103` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd104` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd105` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd106` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd107` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd108` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd109` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd10a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd10b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd10c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd10d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1b4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1b5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1b6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1b7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1b8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1b9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1ba` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1bb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1bc` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1bd` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1be` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1bf` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1c0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1c1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1c2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1c3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1c4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1c5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1c6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1c7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1c8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1c9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1ca` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1cb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1cc` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1cd` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1ce` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1cf` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1d0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1d1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1d2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1d3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1d4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd1d5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd27c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd27d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd27e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd27f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd280` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd281` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd282` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd283` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd284` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd285` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd286` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd287` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd288` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd289` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd28a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd28b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd28c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd28d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd28e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd28f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd290` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd291` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd292` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd293` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd294` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd295` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd296` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd297` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd298` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd299` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd29a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd29b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd29c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd29d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd344` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd345` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd346` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd347` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd348` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd349` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd34a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd34b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd34c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd34d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd34e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd34f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd350` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd351` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd352` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd353` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd354` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd355` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd356` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd357` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd358` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd359` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd35a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd35b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd35c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd35d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd35e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd35f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd360` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd361` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd362` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd363` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd364` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd365` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0xd40c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd40d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd40e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd40f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd410` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd411` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd412` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd413` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd414` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd415` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd416` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd417` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd418` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd419` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd41a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd41b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd41c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd41d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd41e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd41f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd420` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd421` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd422` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd423` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd424` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd425` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd426` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd427` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd428` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd429` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd42a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd42b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd42c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd42d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd42e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd42f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd430` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd431` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd432` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd433` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd434` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd435` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd436` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd437` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd438` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd439` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd43a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd43b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd43c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd43d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd43e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd43f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd440` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd441` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd442` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd443` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd444` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd445` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd446` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd447` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd448` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd449` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd44a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd44b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd44c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd470` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd471` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd472` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd473` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd474` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd475` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd476` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd477` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd478` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd479` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd47a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd47b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd47c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd47d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd47e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd47f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd480` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd481` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd482` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd483` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd484` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd485` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd486` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd487` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd488` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd489` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd48a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd48b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd48c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd48d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd48e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd48f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd490` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd491` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd492` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd493` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd494` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd495` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd496` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd497` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd498` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd499` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd49a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd49b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd49c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd49d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd49e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd49f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4a0` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4a1` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4a2` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4a3` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4a4` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4a5` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4a6` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4a7` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4a9` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4aa` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4ab` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4ac` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4ad` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4ae` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4af` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4b0` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4b1` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4b2` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4b3` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4b4` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4b5` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4b6` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4b7` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4b8` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4b9` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4ba` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4bb` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4bc` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4bd` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4be` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4bf` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4c0` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4c1` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4c2` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4c3` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4c4` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4c5` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4c6` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4c7` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4c8` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4c9` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0xd4ca` | — | med | auto-shape | string-table (written by VIINIT) |

## ui-toggle

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x2c84` | voice_suppressed | high | investigation | Scene scripts/BTL/FIELD/HISTORY play a voice only when 0x2c84[slot]==0; CONFIG4 toggles, INITCONFIG/LOADCONFIG persist. |
| `0x360e` | world_map_zoom_mode | med | investigation | ALLMAP@0x1d2 picks the scale 0x32/0x64; ALLMAP@0xbd3 toggles it from the two zoom buttons. |
| `0x4f9a5` | map_terrain_dirty_flags | med | investigation | SETLAND@0xf4, DELLAND@0xd1 and RESETLAND@0x120 set bit 0 after editing current_floor_terrain_grid. |
| `0x4f9a6` | map_overlay_mode_flags | med | investigation | DRAWMAP@0xa/0x49e gates the per-tile owner tint; DRAWMINIMAP@0x185 (bit1, owner colours) and @0x217 (bit2, element colours from terrain_element_ids). |
| `0x537f8` | arena_battle_active | med | investigation | ARENA@0x7e4 sets 1 before ORGANIZE; ALLMAP@0x13f and FIELD@0x6a5 test/reset it (arena battles skip exploration state). |
| `0xf35a2` | alchemy_new_recipe_notice | med | investigation | CHECKRECIPE@0x211 sets it to 1 whenever it learns a recipe. |
| `0x1372db` | debug_unlock_all_entries | med | investigation | Debug override referenced by DEBUGAM/DEBUGMAP/DEBUGMAP2. When nonzero INFO2@0x6c0 forces every quest state to 0xd (posted+cleared+settled, destructive), INFO1@0x71b lists every objective and INFO8@0x729 lists every glossary entry. |

## unknown

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x43655` | stage_object_persistent_state | high | investigation | Seeded from stage_object_primary_param on first visit (FIELD@0x83e); DRAWOBJ@0xce uses it as the sprite row for draw_class-1 types; FIELD victory/defeat checks compare it to 1 (player). |
| `0x14c0fc` | stage_unknown_14c0fc | low | inference | Written by STINIT2 (e.g. stage 1 = 3); no direct reader found in the corpus. |
| `0x2a2d63` | stage_object_runtime_flags | high | investigation | SETOBJ rebuilds it: bit0 for every placed object (@0xa3), bit1 when use count < use limit (@0x1f5), bit2 per object_type_state_mode; DRAWOBJ draws bit0 objects, FIELD only interacts with bit1 objects. |
| `0x2bd024` | unit_unused_0x2bd024 | low | investigation | Written by EBINIT@0x0003c for unit 1 only; no reader in the 734-script corpus (base or literal cell). |
| `0x306d` | — | low | auto-shape | index/counter? |
| `0x3088` | — | med | auto-shape | record-table[stride 3] |
| `0x36fb` | — | high | auto-shape | unit-desc2-table |
| `0x36fc` | — | high | auto-shape | unit-desc3-table |
| `0x36fd` | — | high | auto-shape | unit-desc4-table |
| `0x379d` | — | high | auto-shape | unit-desc2-table |
| `0x379e` | — | high | auto-shape | unit-desc3-table |
| `0x379f` | — | high | auto-shape | unit-desc4-table |
| `0x381d` | — | high | auto-shape | unit-desc2-table |
| `0x393e` | — | high | auto-shape | unit-desc3-table |
| `0x393f` | — | high | auto-shape | unit-desc4-table |
| `0x3a0b` | — | high | auto-shape | unit-desc2-table |
| `GS 0x3a0c` | — | high | auto-shape | unit-desc3-table |
| `0x3a0d` | — | high | auto-shape | unit-desc4-table |
| `0x428b` | — | med | auto-shape | current-entity-index? |
| `0x4f9dc` | — | med | auto-shape | record-table[stride 20] |
| `0x4fdc5` | — | low | auto-shape | index/counter? |
| `0x51afd` | — | med | auto-shape | record-table[stride 27] |
| `0x51dd6` | — | med | auto-shape | record-table[stride 27] |
| `0x520af` | — | med | auto-shape | record-table[stride 27] |
| `0x52388` | — | med | auto-shape | record-table[stride 27] |
| `0x52661` | — | med | auto-shape | record-table[stride 27] |
| `0x5293d` | — | med | auto-shape | record-table[stride 27] |
| `0x534e9` | — | med | auto-shape | record-table[stride 27] |
| `0x538c2` | — | low | auto-shape | index/counter? |
| `0x538c3` | — | low | auto-shape | index/counter? |
| `0x53ed2` | — | med | auto-shape | record-table[stride 8] |
| `0x61ed8` | — | med | auto-shape | record-table[stride 3] |
| `0x61f6e` | — | med | auto-shape | record-table[stride 3] |
| `0x62004` | — | med | auto-shape | record-table[stride 50] |
| `0x629c8` | — | med | auto-shape | record-table[stride 50] |
| `0x63e19` | — | med | auto-shape | record-table[stride 3] |
| `0xf29c9` | — | med | auto-shape | current-entity-index? |
| `0xf3599` | — | low | auto-shape | index/counter? |
| `0xfd3a8` | — | med | auto-shape | record-table[stride 25] |
| `0xfd435` | — | low | auto-shape | index/counter? |
| `0xfec0c` | — | low | auto-shape | index/counter? |
| `0xfec0d` | — | low | auto-shape | index/counter? |
| `0x104651` | — | med | auto-shape | record-table[stride 10000] |
| `0x135391` | — | med | auto-shape | record-table[stride 100] |
| `0x135b61` | — | med | auto-shape | record-table[stride 100] |
| `0x13f880` | — | med | auto-shape | record-table[stride 100] |
| `0x13fa10` | — | med | auto-shape | record-table[stride 100] |
| `0x1403ea` | — | med | auto-shape | record-table[stride 4] |
| `0x24d62e` | — | med | auto-shape | record-table[stride 50] |
| `0x27e36e` | — | med | auto-shape | record-table[stride 150] |
| `0x2a2d96` | — | low | auto-shape | index/counter? |
| `0x2a2da7` | — | med | auto-shape | record-table[stride 14] |
| `0x2a2de1` | — | low | auto-shape | index/counter? |
| `0x2a3237` | — | med | auto-shape | record-table[stride 27] |
| `0x2a9b8a` | — | low | auto-shape | index/counter? |
| `0x2d2843` | — | med | auto-shape | current-entity-index? |
| `0x2d2849` | — | med | auto-shape | item-field |
| `0x31a92b` | — | low | auto-shape | iminit-field? |
| `0x31a932` | — | low | auto-shape | iminit-field? |
| `0x31dd6f` | — | med | auto-shape | iminit-field |
| `0x31ddbc` | — | med | auto-shape | iminit-field |
| `0x3b2bad` | — | med | auto-shape | current-entity-index? |
| `0x3b2bae` | — | low | auto-shape | index/counter? |
| `0x3b3386` | — | med | auto-shape | record-table[stride 1000] |
| `0x3b3b56` | — | med | auto-shape | record-table[stride 4] |
| `0x3c5bb0` | — | med | auto-shape | record-table[stride 100] |
| `0x3c5d4c` | — | med | auto-shape | record-table[stride 2000] |
| `0x3d40c0` | — | med | auto-shape | record-table[stride 32] |
| `0x3d4100` | — | med | auto-shape | record-table[stride 32] |
