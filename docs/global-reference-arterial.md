<!-- DO NOT EDIT -- generated from vm-map/globals-arterial.toml by tools/globals_build.py --build -->
# Global Variable Reference — arterial (generated)

10240 globals (361 curated, 9879 auto shape-inferred). Source of truth: `vm-map/globals-arterial.toml`.

## Tables

Open Maid Script tables (design spec §5.3): `table[record].field` is the field's array element.

### character[1000]

Built by: `boot`.

CHINIT characters keyed by character id: whoever can speak a page. `character[12].name` is character_names[12][0].

| field | global | address | type |
|---|---|---|---|
| name | character_names | `GS 0x4154` | `string[1000][6]` |
| full_name | character_full_names | `GS 0x7994` | `string[1000]` |
| voice_character_id | character_voice_character_ids | `0x297db9` | `int[1000]` |
| voice_kind | character_voice_kinds | `0x29ee8e` | `int[1000]` |
| voice_setting_slot | character_voice_setting_slots | `0x2981a1` | `int[1000]` |
| face_id | character_face_ids | `0x266c91` | `int[1000][100]` |
| face_history_picture | character_face_history_pictures | `0x27f331` | `int[1000][100]` |
| sort_key | character_sort_keys | `0x29eaa6` | `int[1000]` |
| ultimate_id | character_ultimate_ids | `0x29f65e` | `int[1000]` |
| voice_clips | character_voice_clips | `0x2a09e6` | `int[1000][9]` |
| book_picture | character_book_pictures | `0x2a2d0e` | `int[1000][5]` |
| picture | character_pictures | `0x2a4096` | `int[1000][5]` |
| fellow_picture | character_fellow_pictures | `0x2a541e` | `int[1000][5]` |
| unit_picture | character_unit_pictures | `0x2a67a6` | `int[1000][5]` |
| unit_picture_2 | character_unit_pictures_2 | `0x2a7b2e` | `int[1000][5]` |
| unit_extra_picture | character_unit_extra_pictures | `0x2a8eb6` | `int[1000][5]` |
| menu_picture | character_menu_pictures | `0x2aa23e` | `int[1000][5]` |

### card[6000]

Built by: `boot`.

CDINIT cards keyed by row * 30 + slot (200 rows of 30 slots; a deck names a card by its row and its slot).

| field | global | address | type |
|---|---|---|---|
| name | card_names | `GS 0x7d7c` | `string[6000]` |
| flavor | card_flavor_lines | `GS 0x94ec` | `string[6000][3]` |
| append_number | card_append_numbers | `0x2b338f` | `int[6000]` |
| rarity | card_rarities | `0x2b79df` | `int[6000]` |
| race_flags | card_race_flags | `0x2bc02f` | `int[6000]` |
| slots | card_slots | `0x2c4ccf` | `int[6000]` |
| control | card_control | `0x2bef0f` | `int[6000]` |
| cost | card_costs | `0x2bd79f` | `int[6000]` |
| hp | card_hp | `0x2c355f` | `int[6000]` |
| leader_lp | card_leader_lp | `0x2c1def` | `int[6000]` |
| attack | card_attack | `0x2c067f` | `int[6000]` |
| talent_id | card_talent_ids | `0x2c643f` | `int[6000][2]` |
| price | card_prices | `0x2c931f` | `int[6000][2]` |
| picture | card_pictures | `0x2cd96f` | `int[6000][2]` |
| slot_growth | card_slot_growth | `0x30c93f` | `int[6000][10]` |
| hp_growth | card_hp_growth | `0x2fdedf` | `int[6000][10]` |
| leader_lp_growth | card_leader_lp_growth | `0x2ef47f` | `int[6000][10]` |
| attack_growth | card_attack_growth | `0x2e0a1f` | `int[6000][10]` |
| owned_count | card_owned_counts | `0x4f30` | `int[6000]` |
| experience | card_experience | `0x7e10` | `int[6000]` |
| level | card_levels | `0x440e56` | `int[6000]` |
| max_level | card_max_levels | `0x43f6e6` | `int[6000]` |
| level_progress | card_level_progress | `0x4425c6` | `int[6000]` |
| current_slots | card_current_slots | `0x4454a6` | `int[6000]` |
| current_control | card_current_control | `0x448386` | `int[6000]` |
| current_cost | card_current_costs | `0x446c16` | `int[6000]` |
| current_hp | card_current_hp | `0x44c9d6` | `int[6000]` |
| current_leader_lp | card_current_leader_lp | `0x44b266` | `int[6000]` |
| current_attack | card_current_attack | `0x449af6` | `int[6000]` |
| sort_key | card_sort_keys | `0x2b4aff` | `int[6000]` |
| requires_party | card_requires_party_flags | `0x2b626f` | `int[6000]` |
| in_book | card_book_flags | `0x2b914f` | `int[6000]` |
| copyable | card_copyable_flags | `0x2ba8bf` | `int[6000]` |
| subtractive_effect | card_subtractive_effect_flags | `0x2cc1ff` | `int[6000]` |
| leader_picture | card_leader_pictures | `0x2d084f` | `int[6000][2]` |
| attack_picture | card_attack_pictures | `0x2d372f` | `int[6000][2]` |
| recipe_icon | card_recipe_icons | `0x2d660f` | `int[6000]` |
| attack_effect | card_attack_effects | `0x2d7d7f` | `int[6000]` |
| voice_clips | card_voice_clips | `0x2d94ef` | `int[6000][5]` |
| obtained | card_obtained_flags | `0x66a0` | `int[6000]` |

### talent[1000]

Built by: `boot`.

TAINIT talents keyed by talent id.

| field | global | address | type |
|---|---|---|---|
| name | talent_names | `GS 0xdb3c` | `string[1000]` |
| description | talent_descriptions | `GS 0xdf24` | `string[1000]` |
| append_number | talent_append_numbers | `0x31b3a0` | `int[1000]` |
| sort_key | talent_sort_keys | `0x31b788` | `int[1000]` |
| in_book | talent_book_flags | `0x31bb70` | `int[1000]` |
| timing | talent_timings | `0x31bf58` | `int[1000]` |

### skill[1000]

Built by: `boot`.

SKINIT skills keyed by skill id.

| field | global | address | type |
|---|---|---|---|
| name | skill_names | `GS 0xef28` | `string[1000]` |
| text | skill_lines | `GS 0xf310` | `string[1000][3]` |
| sort_key | skill_sort_keys | `0x31e412` | `int[1000]` |
| in_book | skill_book_flags | `0x31e7fa` | `int[1000]` |
| kind | skill_kinds | `0x31ebe2` | `int[1000]` |
| amount | skill_amounts | `0x31efca` | `int[1000]` |
| party_count | party_skill_counts | `0x4502a1` | `int[1000]` |

### item[2000]

Built by: `boot`.

ITINIT items keyed by item id.

| field | global | address | type |
|---|---|---|---|
| name | item_names | `GS 0xfec8` | `string[2000]` |
| text | item_lines | `GS 0x10698` | `string[2000][3]` |
| append_number | item_append_numbers | `0x31f79a` | `int[2000]` |
| rarity | item_rarities | `0x3216da` | `int[2000]` |
| price | item_prices | `0x32b31a` | `int[2000][2]` |
| picture | item_pictures | `0x32c2ba` | `int[2000][2]` |
| owned_count | item_owned_counts | `0x3f90` | `int[2000]` |
| card_row | item_card_rows | `0x326cca` | `int[2000]` |
| card_slot | item_card_slots | `0x32749a` | `int[2000]` |
| required_slots | item_required_slots | `0x32267a` | `int[2000]` |
| required_races | item_required_races | `0x327c6a` | `int[2000]` |
| excluded_races | item_excluded_races | `0x32843a` | `int[2000]` |
| required_cost | item_required_costs | `0x328c0a` | `int[2000]` |
| required_control | item_required_control | `0x3293da` | `int[2000]` |
| required_hp | item_required_hp | `0x32ab4a` | `int[2000]` |
| required_leader_lp | item_required_leader_lp | `0x32a37a` | `int[2000]` |
| required_attack | item_required_attack | `0x329baa` | `int[2000]` |
| sort_key | item_sort_keys | `0x31ff6a` | `int[2000]` |
| kind | item_kinds | `0x32073a` | `int[2000]` |
| no_carry_over | item_no_carry_over_flags | `0x320f0a` | `int[2000]` |
| in_book | item_book_flags | `0x321eaa` | `int[2000]` |
| cost_bonus | item_cost_bonuses | `0x322e4a` | `int[2000]` |
| control_bonus | item_control_bonuses | `0x32361a` | `int[2000]` |
| attack_bonus | item_attack_bonuses | `0x323dea` | `int[2000]` |
| leader_lp_bonus | item_leader_lp_bonuses | `0x3245ba` | `int[2000]` |
| hp_bonus | item_hp_bonuses | `0x324d8a` | `int[2000]` |
| condition_id | item_condition_ids | `0x32555a` | `int[2000]` |
| target | item_targets | `0x325d2a` | `int[2000]` |
| talent_id | item_talent_ids | `0x3264fa` | `int[2000]` |
| effect_style | item_effect_styles | `0x32d25a` | `int[2000]` |
| effect_draw_mode | item_effect_draw_modes | `0x32da2a` | `int[2000]` |
| effect_movie | item_effect_movies | `0x32e1fa` | `int[2000][3]` |
| obtained | item_obtained_flags | `0x4760` | `int[2000]` |

### area_point[200]

Built by: `area`.

MPINIT's points of the area in play, keyed by point number.

| field | global | address | type |
|---|---|---|---|
| name | area_point_names | `GS 0x11f54` | `string[200]` |
| kind | area_point_kinds | `0x330c2d` | `int[200]` |
| target | area_point_targets | `0x330cf5` | `int[200][2]` |
| x | area_point_x | `0x331015` | `int[200]` |
| y | area_point_y | `0x3310dd` | `int[200]` |

### area_route[200]

Built by: `area`.

ROINIT's routes of the area in play, keyed by route number.

| field | global | address | type |
|---|---|---|---|
| points | area_route_points | `0x33207d` | `int[200][2]` |

### area_unit[200]

Built by: `area`.

UNINIT's units of the area in play, keyed by unit number.

| field | global | address | type |
|---|---|---|---|
| card | area_unit_cards | `0x33301e` | `int[200][2]` |
| kind | area_unit_kinds | `0x332dc6` | `int[200]` |
| icon | area_unit_icons | `0x332e8e` | `int[200]` |
| deck | area_unit_decks | `0x332f56` | `int[200]` |
| routine | area_unit_routines | `0x333276` | `int[200]` |
| battle_music | area_unit_battle_music | `0x3342de` | `int[200]` |
| picture | area_unit_pictures | `0x3345fe` | `int[200][2]` |
| extra_picture | area_unit_extra_pictures | `0x33478e` | `int[200]` |
| state_id | area_unit_state_ids | `0x333406` | `int[200]` |

### deck[1000]

Built by: `boot`.

DEINIT decks keyed by deck id: a name and up to 20 cards, each as a row and a slot of CDINIT.

| field | global | address | type |
|---|---|---|---|
| name | deck_names | `GS 0x1201c` | `string[1000]` |
| card_row | deck_card_rows | `0x334856` | `int[1000][30]` |
| card_slot | deck_card_slots | `0x33bd86` | `int[1000][30]` |
| card_max_copies | deck_card_max_copies | `0x3432b6` | `int[1000][30]` |
| card_copies | deck_card_copies | `0x34a7e6` | `int[1000][30]` |
| card_copy_chance | deck_card_copy_chances | `0x351d16` | `int[1000][30]` |
| card_max_unless_scene | deck_card_max_unless_scenes | `0x359246` | `int[1000][30]` |
| card_max_once_scene | deck_card_max_once_scenes | `0x360776` | `int[1000][30]` |
| start_board | deck_start_boards | `0x367ca6` | `int[1000][6]` |
| cpu_profile | deck_cpu_profiles | `0x369416` | `int[1000]` |
| drop_weight | deck_drop_weights | `0x3697fe` | `int[1000][4]` |
| drop_item | deck_drop_items | `0x36a79e` | `int[1000][4]` |
| drop_card_row | deck_drop_card_rows | `0x36b73e` | `int[1000][4]` |
| drop_card_slot | deck_drop_card_slots | `0x36c6de` | `int[1000][4]` |

### party_member[100]

Built by: `boot`.

CCINIT party characters keyed by character id (the same ids as character), with their saved state: what each brings at each of its levels, and its level, experience, 奥義 level, picture set and open skill lines.

| field | global | address | type |
|---|---|---|---|
| start_skill_lines | party_member_start_skill_lines | `0x29fa46` | `int[100][4]` |
| level_card_row | party_member_level_card_rows | `0x2ab5c7` | `int[100][40]` |
| level_card_slot | party_member_level_card_slots | `0x2ac567` | `int[100][40]` |
| level_item | party_member_level_items | `0x2ad507` | `int[100][40]` |
| level_skills | party_member_level_skills | `0x2ae4a7` | `int[100][160]` |
| level_ultimate_level | party_member_level_ultimate_levels | `0x2b2327` | `int[100][40]` |
| flags | party_member_flags | `0x3ae0` | `int[100]` |
| level | party_member_levels | `0x3ba8` | `int[100]` |
| experience | party_member_experience | `0x3c0c` | `int[100]` |
| ultimate_level | party_member_ultimate_levels | `0x3c70` | `int[100]` |
| picture_set | party_member_picture_sets | `0x3d38` | `int[100]` |
| skill_lines | party_member_skill_lines | `0x3d9c` | `int[100][4]` |

### ultimate[100]

Built by: `boot`.

COINIT 奥義 keyed by 奥義 id; the numeric fields have a column for each of the nine levels (1 to 9).

| field | global | address | type |
|---|---|---|---|
| name | ultimate_names | `GS 0xe30c` | `string[100]` |
| level_lines | ultimate_level_lines | `GS 0xe370` | `string[100][30]` |
| append_number | ultimate_append_numbers | `0x31c72a` | `int[100]` |
| sort_key | ultimate_sort_keys | `0x31c78e` | `int[100]` |
| in_book | ultimate_book_flags | `0x31c7f2` | `int[100]` |
| gauge_gain | ultimate_gauge_gains | `0x31c8ba` | `int[100][10]` |
| amount | ultimate_amounts | `0x31cca2` | `int[100][10]` |
| picture_x | ultimate_picture_x | `0x31d08a` | `int[100][10]` |
| picture_y | ultimate_picture_y | `0x31d472` | `int[100][10]` |
| picture | ultimate_pictures | `0x31d85a` | `int[100][10]` |
| picture_2 | ultimate_pictures_2 | `0x31dc42` | `int[100][10]` |
| movie | ultimate_movies | `0x31e02a` | `int[100][10]` |

### condition[32]

Built by: `boot`.

ILINIT battle conditions keyed by condition id.

| field | global | address | type |
|---|---|---|---|
| name | condition_names | `GS 0x11e08` | `string[32]` |
| kind | condition_kinds | `0x32f96a` | `int[32]` |
| attack_change | condition_attack_changes | `0x32f9ca` | `int[32]` |
| hp_change | condition_hp_changes | `0x32f9ea` | `int[32]` |
| turn_hp_change | condition_turn_hp_changes | `0x32fa2a` | `int[32]` |
| turn_hp_change_2 | condition_turn_hp_changes_2 | `0x32fa4a` | `int[32]` |
| icon | condition_icons | `0x32fa6a` | `int[32]` |

### battle[1000]

Built by: `boot`.

DUINIT battles keyed by battle id (battle_id): each side's leader and deck, the opponent's 奥義, the rule, music and pictures.

| field | global | address | type |
|---|---|---|---|
| uses_player_deck | battle_uses_player_deck_flags | `0x36d67e` | `int[1000]` |
| leader_card | battle_leader_cards | `0x36da66` | `int[1000][4]` |
| deck | battle_decks | `0x36ea06` | `int[1000][2]` |
| opponent_ultimate | battle_opponent_ultimates | `0x36f1d6` | `int[1000]` |
| opponent_ultimate_level | battle_opponent_ultimate_levels | `0x36f5be` | `int[1000]` |
| opponent_ultimate_picture | battle_opponent_ultimate_pictures | `0x36f9a6` | `int[1000]` |
| opponent_ultimate_picture_x | battle_opponent_ultimate_picture_x | `0x36fd8e` | `int[1000]` |
| opponent_ultimate_picture_y | battle_opponent_ultimate_picture_y | `0x370176` | `int[1000]` |
| rule | battle_rules | `0x37055e` | `int[1000]` |
| music | battle_music | `0x370946` | `int[1000]` |
| background | battle_backgrounds | `0x371116` | `int[1000][2]` |

### battle_rule[100]

Built by: `boot`.

REINIT battle rules keyed by rule id (battle_rule_id): what wins and loses, the limits, and the deck's restrictions.

| field | global | address | type |
|---|---|---|---|
| text | battle_rule_lines | `GS 0x12404` | `string[100][6]` |
| outcomes | battle_rule_outcomes | `0x3718e6` | `int[100][5]` |
| hp_recovery | battle_rule_hp_recovery_flags | `0x371ada` | `int[100]` |
| turn_limit | battle_rule_turn_limits | `0x371ec2` | `int[100]` |
| hand_size | battle_rule_hand_sizes | `0x371f26` | `int[100]` |
| deck_size | battle_rule_deck_sizes | `0x371f8a` | `int[100]` |
| use_bans | battle_rule_use_bans | `0x371fee` | `int[100]` |
| cost_range | battle_rule_cost_ranges | `0x3723d6` | `int[100][2]` |
| control_range | battle_rule_control_ranges | `0x37249e` | `int[100][2]` |
| attack_range | battle_rule_attack_ranges | `0x372566` | `int[100][2]` |
| hp_range | battle_rule_hp_ranges | `0x37262e` | `int[100][2]` |
| race_masks | battle_rule_race_masks | `0x3726f6` | `int[100][2]` |
| banned_card_rows | battle_rule_banned_card_rows | `0x3727be` | `int[100][10]` |

### recipe[2000]

Built by: `boot`.

ALINIT recipes keyed by recipe id: what each makes, from which ingredients, and the conditions ALCHEMY offers it under.

| field | global | address | type |
|---|---|---|---|
| item | recipe_items | `0x372ba6` | `int[2000]` |
| card_row | recipe_card_rows | `0x373376` | `int[2000]` |
| card_slot | recipe_card_slots | `0x373b46` | `int[2000]` |
| points | recipe_points | `0x374316` | `int[2000]` |
| places | recipe_places | `0x374ae6` | `int[2000]` |
| least_chapter | recipe_least_chapters | `0x3752b6` | `int[2000]` |
| least_day | recipe_least_days | `0x375a86` | `int[2000]` |
| weekdays | recipe_weekdays | `0x376256` | `int[2000]` |
| time_slots | recipe_time_slots | `0x376a26` | `int[2000]` |
| least_rank | recipe_least_ranks | `0x3771f6` | `int[2000]` |
| party_character | recipe_party_characters | `0x3779c6` | `int[2000]` |
| party_character_level | recipe_party_character_levels | `0x378196` | `int[2000]` |
| skill | recipe_skills | `0x378966` | `int[2000]` |
| required_item | recipe_required_items | `0x379136` | `int[2000]` |
| required_card_row | recipe_required_card_rows | `0x379906` | `int[2000]` |
| required_card_slot | recipe_required_card_slots | `0x37a0d6` | `int[2000]` |
| scenes_played | recipe_scenes_played | `0x37a8a6` | `int[2000][5]` |
| scenes_not_played | recipe_scenes_not_played | `0x37cfb6` | `int[2000][5]` |
| ingredient_item | recipe_ingredient_items | `0x37f6c6` | `int[2000][4]` |
| ingredient_card_row | recipe_ingredient_card_rows | `0x381606` | `int[2000][4]` |
| ingredient_card_slot | recipe_ingredient_card_slots | `0x383546` | `int[2000][4]` |
| ingredient_count | recipe_ingredient_counts | `0x385486` | `int[2000][4]` |

### shop_entry[1000]

Built by: `boot`.

SHINIT shop entries keyed by entry id: what each sells, in which shops, and the conditions SHOPP lists it under.

| field | global | address | type |
|---|---|---|---|
| item | shop_entry_items | `0x3873c6` | `int[1000]` |
| card_row | shop_entry_card_rows | `0x3877ae` | `int[1000]` |
| card_slot | shop_entry_card_slots | `0x387b96` | `int[1000]` |
| shops | shop_entry_shops | `0x387f7e` | `int[1000]` |
| least_chapter | shop_entry_least_chapters | `0x388366` | `int[1000]` |
| least_day | shop_entry_least_days | `0x38874e` | `int[1000]` |
| weekdays | shop_entry_weekdays | `0x388b36` | `int[1000]` |
| time_slots | shop_entry_time_slots | `0x388f1e` | `int[1000]` |
| least_rank | shop_entry_least_ranks | `0x389306` | `int[1000]` |
| party_character | shop_entry_party_characters | `0x3896ee` | `int[1000]` |
| party_character_level | shop_entry_party_character_levels | `0x389ad6` | `int[1000]` |
| skill | shop_entry_skills | `0x389ebe` | `int[1000]` |
| scenes_played | shop_entry_scenes_played | `0x38a2a6` | `int[1000][2]` |
| scenes_not_played | shop_entry_scenes_not_played | `0x38aa76` | `int[1000][2]` |

### opponent[1000]

Built by: `boot`.

ARINIT arena opponents keyed by opponent id (current_opponent_id), with their saved state.

| field | global | address | type |
|---|---|---|---|
| name | opponent_names | `GS 0x1268e` | `string[1000]` |
| battle | opponent_battles | `0x38b246` | `int[1000]` |
| arena | opponent_arenas | `0x38b62e` | `int[1000]` |
| sort_key | opponent_sort_keys | `0x38ba16` | `int[1000]` |
| reward_item | opponent_reward_items | `0x38bdfe` | `int[1000]` |
| reward_card_row | opponent_reward_card_rows | `0x38c1e6` | `int[1000]` |
| reward_card_slot | opponent_reward_card_slots | `0x38c5ce` | `int[1000]` |
| reward_count | opponent_reward_counts | `0x38c9b6` | `int[1000]` |
| scenes | opponent_scenes | `0x38d186` | `int[1000][3]` |
| places | opponent_places | `0x38dd3e` | `int[1000]` |
| required_opponent | opponent_required_opponents | `0x38e126` | `int[1000]` |
| weekdays | opponent_weekdays | `0x38ecde` | `int[1000]` |
| scenes_played | opponent_scenes_played | `0x39044e` | `int[1000][2]` |
| scenes_not_played | opponent_scenes_not_played | `0x390c1e` | `int[1000][2]` |
| tournament_group | opponent_tournament_groups | `0x3913ee` | `int[1000]` |
| state | opponent_states | `0x17f36` | `int[1000]` |

### goal[1000]

Built by: `boot`.

FAINIT goals keyed by goal id, opened and closed by which scenes have been played, with their saved state.

| field | global | address | type |
|---|---|---|---|
| title | goal_titles | `GS 0x29e4` | `string[1000]` |
| title_2 | goal_titles_2 | `GS 0x2dcc` | `string[1000]` |
| lines | goal_lines | `GS 0x31b4` | `string[1000][4]` |
| sort_key | goal_sort_keys | `0x260701` | `int[1000]` |
| least_chapter | goal_least_chapters | `0x260ae9` | `int[1000]` |
| open_scenes_played | goal_open_scenes_played | `0x260ed1` | `int[1000][3]` |
| open_scenes_not_played | goal_open_scenes_not_played | `0x261a89` | `int[1000][3]` |
| bit2_scenes_played | goal_bit2_scenes_played | `0x262641` | `int[1000][3]` |
| bit2_scenes_not_played | goal_bit2_scenes_not_played | `0x2631f9` | `int[1000][3]` |
| bit1_scenes_played | goal_bit1_scenes_played | `0x263db1` | `int[1000][3]` |
| bit1_scenes_not_played | goal_bit1_scenes_not_played | `0x264969` | `int[1000][3]` |
| kind | goal_kinds | `0x265521` | `int[1000]` |
| reward_item | goal_reward_items | `0x265909` | `int[1000]` |
| reward_card_row | goal_reward_card_rows | `0x265cf1` | `int[1000]` |
| reward_card_slot | goal_reward_card_slots | `0x2660d9` | `int[1000]` |
| reward_count | goal_reward_counts | `0x2664c1` | `int[1000]` |
| reward_money | goal_reward_money | `0x2668a9` | `int[1000]` |
| state | goal_states | `0x18498` | `int[1000]` |

### area_map[300]

Built by: `boot`.

AMINIT areas keyed by area id (current_area_id): name, place, map pictures, music, unit scale, and a battle's background and rule.

| field | global | address | type |
|---|---|---|---|
| name | area_names | `GS 0x11e28` | `string[300]` |
| place | area_places | `0x32fbc5` | `int[300]` |
| map_pictures | area_map_pictures | `0x32fcf1` | `int[300][6]` |
| music | area_music | `0x3303f9` | `int[300][2]` |
| unit_scale | area_unit_scales | `0x330651` | `int[300][2]` |
| battle_background | area_battle_backgrounds | `0x3308a9` | `int[300][2]` |
| battle_rule | area_battle_rules | `0x330b01` | `int[300]` |

### unit_routine[1000]

Built by: `boot`.

RTINIT map-unit routines keyed by routine id (area_unit_routines), 50 steps each; a field's column is the step.

| field | global | address | type |
|---|---|---|---|
| action | unit_routine_actions | `0x391fa9` | `int[1000][50]` |
| chance | unit_routine_chances | `0x39e2f9` | `int[1000][50]` |
| arg | unit_routine_args | `0x3aa649` | `int[1000][50]` |
| arg_2 | unit_routine_args_2 | `0x3b6999` | `int[1000][50]` |
| arg_3 | unit_routine_args_3 | `0x3c2ce9` | `int[1000][50]` |
| scenes_played | unit_routine_scenes_played | `0x3e76d9` | `int[1000][150]` |
| scenes_not_played | unit_routine_scenes_not_played | `0x40c0c9` | `int[1000][150]` |

### cpu_profile[1000]

Built by: `boot`.

RTINIT CPU battle profiles keyed by profile id (deck_cpu_profiles).

| field | global | address | type |
|---|---|---|---|
| settings | cpu_profile_settings | `0x430ab9` | `int[1000][20]` |
| move_choices | cpu_profile_move_choices | `0x4358d9` | `int[1000][10]` |
| action_choices | cpu_profile_action_choices | `0x437fe9` | `int[1000][10]` |
| attack_choices | cpu_profile_attack_choices | `0x43a6f9` | `int[1000][10]` |
| attack_list | cpu_profile_attack_lists | `0x43ce09` | `int[1000][10]` |

### scene[10000]

Built by: `boot`.

SCINIT scenes keyed by scene id (current_scene_id), with the played and seen cells.

| field | global | address | type |
|---|---|---|---|
| script_id | scene_script_ids | `0x214b71` | `int[10000]` |
| played | scene_played_flags | `0x5e0` | `int[10000]` |
| seen | scene_seen_flags | `0x18880` | `int[10000]` |

### profile[200]

Built by: `boot`.

CIINIT character profiles keyed by profile id.

| field | global | address | type |
|---|---|---|---|
| name | profile_names | `GS 0x6864` | `string[200]` |
| lines | profile_lines | `GS 0x692c` | `string[200][16]` |
| sort_key | profile_sort_keys | `0x29c45b` | `int[200]` |
| character_id | profile_character_ids | `0x29c523` | `int[200]` |
| picture | profile_pictures | `0x29c9d3` | `int[200]` |
| script | profile_scripts | `0x29ca9b` | `int[200]` |

### glossary[1000]

Built by: `boot`.

VIINIT glossary entries keyed by entry id.

| field | global | address | type |
|---|---|---|---|
| title | glossary_titles | `GS 0x647c` | `string[1000]` |
| sort_key | glossary_sort_keys | `0x29a51a` | `int[1000]` |
| scenes | glossary_scenes | `0x29a902` | `int[1000][3]` |
| picture_x | glossary_picture_x | `0x29b4ba` | `int[1000]` |
| picture_y | glossary_picture_y | `0x29b8a2` | `int[1000]` |
| picture | glossary_pictures | `0x29bc8a` | `int[1000]` |
| script | glossary_scripts | `0x29c072` | `int[1000]` |

### tip[1000]

Built by: `boot`.

TIINIT entries keyed by entry id (日常モード …).

| field | global | address | type |
|---|---|---|---|
| title | tip_titles | `GS 0x75ac` | `string[1000]` |
| sort_key | tip_sort_keys | `0x29cb64` | `int[1000]` |
| scenes | tip_scenes | `0x29cf4c` | `int[1000][3]` |
| picture_x | tip_picture_x | `0x29db04` | `int[1000]` |
| picture_y | tip_picture_y | `0x29deec` | `int[1000]` |
| picture | tip_pictures | `0x29e2d4` | `int[1000]` |
| script | tip_scripts | `0x29e6bc` | `int[1000]` |

### cg[3000]

Built by: `boot`.

CGINIT gallery entries keyed by entry id, in sections and groups.

| field | global | address | type |
|---|---|---|---|
| pictures | cg_pictures | `0x2283f1` | `int[3000][2]` |
| section | cg_sections | `0x229b61` | `int[3000]` |
| group | cg_groups | `0x22a719` | `int[3000]` |
| place | cg_places | `0x22b2d1` | `int[3000]` |

### music[50]

Built by: `boot`.

MUINIT tracks keyed by track id.

| field | global | address | type |
|---|---|---|---|
| title | music_titles | `GS 0x29b2` | `string[50]` |
| resource | music_resources | `0x260667` | `int[50]` |

### Scope area

Ids 1 to 299. A data file reaches each table below as `area.<id>.<name>.<record>` and not by its own name (data patches spec §6.2).

An area, by current_area_id (an id of AMINIT; the game's are 1 to 197 and its per-area arrays hold 300). MPINIT, ROINIT and UNINIT clear these tables and fill them from the branch for the area in play.

| name | table |
|---|---|
| point | area_point |
| route | area_route |
| unit | area_unit |

## choice-output

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x21480a` | current_scene_id | high | investigation | The scene being played, an id of SCINIT. A SCJUMP rule sets it, and a scene script branches on it to the scene's code. |
| `0x21480b` | scjump_chain_pending | high | investigation | The caller of the SCJUMP chain sets it to 0x7fffffff; the first rule whose condition holds sets current_scene_id and clears it, so no later rule fires. |

## counter

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x3ad7` | money | high | investigation | The player's money, kept within 0 to 99999: SHOPP takes a purchase's price from it, and ADDCARD and ADDITEM add the second price of what is held over 99. |
| `0x3ad8` | points | high | investigation | Saved: a points total, 0 to 99999; ALCHEMY adds recipe_points to it, and CALCPR sets rank from it. |
| `0x3ad9` | rank | high | investigation | Saved: the rank, 1 to 99, which CALCPR sets from points by rank_point_thresholds. |
| `0x987b` | day | med | investigation | Saved: the day; its day of the week is day % 7, which the weekday bits test. |

## data-table

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x3ae0` | party_member_flags | high | investigation | Saved. Bit 1 while the character is in the party: UNITECH sets bits 0 and 1, LEAVECH clears bit 1 (only bit 0 while bit 0 of 0x36e0 is set). CHECKCARD and the shops' and recipes' tests read bit 1. |
| `0x3ba8` | party_member_levels | high | investigation | Saved: the character's level. RESULT raises it by party_member_experience, to 9, or to 35 once 0x36d1 is set. |
| `0x3c0c` | party_member_experience | high | investigation | Saved: the character's experience; RESULT adds a battle's to it and compares it with character_level_experience. |
| `0x3c70` | party_member_ultimate_levels | high | investigation | Saved: the level at which the scripts read the character's 奥義 (character_ultimate_ids). |
| `0x3d38` | party_member_picture_sets | high | investigation | Saved: which column (0 to 4) of the character's pictures by picture set is used; UNITECH sets it to 0 when the character joins. |
| `0x3d9c` | party_member_skill_lines | high | investigation | Saved: which of the character's four skill lines are open, copied from party_member_start_skill_lines when the character first joins. |
| `0x3f90` | item_owned_counts | high | investigation | How many of an item the player holds, kept by a save: ADDITEM adds item_arg_count and keeps it within 0 to 99. |
| `0x4760` | item_obtained_flags | high | investigation | Saved: 1 once the player has obtained the item (ADDITEM sets it). The recipes' tests read it. |
| `0x4f30` | card_owned_counts | high | investigation | How many of a card the player holds, kept by a save: ADDCARD adds card_arg_count and keeps it within 0 to 99. |
| `0x66a0` | card_obtained_flags | high | investigation | Saved: 1 once the player has obtained the card (ADDCARD sets it). The recipes' tests read it. |
| `0x7e10` | card_experience | high | investigation | A card's experience, 100 to a level, and the one number of a card a save keeps: CALCARM takes the level as value / 100 + 1 and the progress as value % 100, and RESULT adds to it after a battle. |
| `0x16217` | area_unit_deck_copies | med | investigation | Saved: a map unit's copies of each card of its deck, which ALLMAP fills between deck_card_copies and deck_card_max_copies. |
| `0x18880` | scene_seen_flags | high | investigation | Each scene script sets [current_scene_id] to 1 when it plays and stores the cell in the shared profile. |
| `0x3fc08` | face_character_ids | high | investigation | Built by INIT2 from character_face_ids: the character of a face, at [layer][id] where layer is the face id's top byte (an append's selector) and id the rest. set_page_face takes the page's speaker from it. |
| `0x12a208` | face_slots | high | investigation | Built by INIT2 beside face_character_ids: the face's slot in its character's row. set_page_face keeps it in 0x3144. |
| `0x214b71` | scene_script_ids | high | investigation | SCINIT: the script (a packed script id) that plays scene id, from scene 0; 323 is SC0000.BIN. |
| `0x2283f1` | cg_pictures | med | investigation | CGINIT: two pictures: CGMODE shows column 0; where column 1 is set, ALLMAP and SAVE find the entry whose column 0 is the picture last shown (0x36cf) and use column 1 in its place. |
| `0x229b61` | cg_sections | high | investigation | CGINIT: the entry's section in the gallery, 1 to 9. |
| `0x22a719` | cg_groups | high | investigation | CGINIT: its group in the section, 1 to 57. |
| `0x22b2d1` | cg_places | high | investigation | CGINIT: its place in the group, 1 to 35. |
| `0x260667` | music_resources | high | investigation | MUINIT: a resource id SETMEMOIR asks whether the player has unlocked (is_catalog_resource_unlocked). |
| `0x260701` | goal_sort_keys | high | investigation | FAINIT: a sort number; the goal lists (INFOFA, GETFATE, ALLMAP) are in its order. |
| `0x260ae9` | goal_least_chapters | med | investigation | A least chapter; SETFATE's test reads it and no entry writes it. |
| `0x260ed1` | goal_open_scenes_played | high | investigation | FAINIT: three scenes (each id + 1) that must all have been played for the goal to open. |
| `0x261a89` | goal_open_scenes_not_played | high | investigation | FAINIT: three scenes none of which may have been played for it to open. |
| `0x262641` | goal_bit2_scenes_played | high | investigation | FAINIT: three scenes that, all played, set bit 2 of goal_states. |
| `0x2631f9` | goal_bit2_scenes_not_played | high | investigation | FAINIT: three scenes none of which may have been played for that. |
| `0x263db1` | goal_bit1_scenes_played | high | investigation | FAINIT: three scenes that, all played, set bit 1 of goal_states. |
| `0x264969` | goal_bit1_scenes_not_played | high | investigation | FAINIT: three scenes none of which may have been played for that. |
| `0x265521` | goal_kinds | high | investigation | FAINIT: 1 to 3; SETFATE counts the open goals of each kind into 0x3d46c + kind. |
| `0x265909` | goal_reward_items | high | investigation | FAINIT: the goal's reward when it is an item (RECEIPT). |
| `0x265cf1` | goal_reward_card_rows | high | investigation | FAINIT: the reward when it is a card: its row. |
| `0x2660d9` | goal_reward_card_slots | high | investigation | FAINIT: and its slot. |
| `0x2664c1` | goal_reward_counts | med | investigation | FAINIT: a count RECEIPT prints beside the reward (1 on 71). |
| `0x2668a9` | goal_reward_money | high | investigation | FAINIT: money the goal gives, 20 to 200: RECEIPT prints it and adds it to money. |
| `0x266c91` | character_face_ids | high | investigation | CFINIT: a character's faces, slots 1 to 99: the picture ids a dialogue line's face names. INIT2 inverts the array into face_character_ids and face_slots. |
| `0x27f331` | character_face_history_pictures | med | investigation | CFINIT: a second picture for the face in the same slot of character_face_ids. HISTORY alone reads it. |
| `0x2979d1` | voice_setting_text_colors | high | investigation | CVINIT, indexed from 0: a voice setting's text colour. INIT2 copies it to the player's colour (0x36ed) where that is unset. |
| `0x297db9` | character_voice_character_ids | high | investigation | CHINIT: the character whose voice setting this one uses (itself for 91 of 107). The page's voice block reads character_voice_setting_slots through it. |
| `0x2981a1` | character_voice_setting_slots | high | investigation | CVINIT: a character's voice setting. The page's voice block plays the voice only while the player's switch for that setting (0x2d57) is 0. |
| `0x298589` | voice_setting_character_ids | med | investigation | CVINIT: a voice setting's character (an id in CHINIT's range). |
| `0x298971` | voice_setting_sample_voices | high | investigation | CVINIT: a sample voice for the setting; CONFIG4 plays it. |
| `0x29a51a` | glossary_sort_keys | high | investigation | VIINIT: a sort number; INFOVI lists the glossary in its order. |
| `0x29a902` | glossary_scenes | high | investigation | VIINIT: up to three scenes (each id + 1); INFOVI lists the entry once one of them is seen, and always where there is none. |
| `0x29b4ba` | glossary_picture_x | high | investigation | VIINIT: the x at which INFOVI draws the entry's picture. |
| `0x29b8a2` | glossary_picture_y | high | investigation | VIINIT: and the y. |
| `0x29bc8a` | glossary_pictures | high | investigation | VIINIT: the entry's picture. |
| `0x29c072` | glossary_scripts | high | investigation | VIINIT: a script INFOVI runs for the entry (VIMES, or $1$VIMES on one). |
| `0x29c45b` | profile_sort_keys | high | investigation | CIINIT: a sort number; INFOCH lists the profiles in its order. |
| `0x29c523` | profile_character_ids | high | investigation | CIINIT: the profile's character (a CHINIT id); INFOCH shows the profile once the shared profile's cell for that character (0x1af90 + character) is set. |
| `0x29c9d3` | profile_pictures | high | investigation | CIINIT: the profile's picture (INFOCH). |
| `0x29ca9b` | profile_scripts | high | investigation | CIINIT: a script INFOCH runs for the profile (CIMES, or $1$CIMES on three). |
| `0x29cb64` | tip_sort_keys | high | investigation | TIINIT: a sort number; INFOTI lists the entries in its order. |
| `0x29cf4c` | tip_scenes | high | investigation | TIINIT: up to three scenes; INFOTI lists the entry once one of them is seen, and always where there is none. |
| `0x29db04` | tip_picture_x | high | investigation | TIINIT: the x of the entry's picture. |
| `0x29deec` | tip_picture_y | high | investigation | TIINIT: and the y. |
| `0x29e2d4` | tip_pictures | high | investigation | TIINIT: the entry's picture. |
| `0x29e6bc` | tip_scripts | high | investigation | TIINIT: a script INFOTI runs for the entry (TIMES). |
| `0x29eaa6` | character_sort_keys | high | investigation | CHINIT: a sort number; the party lists (FELLOW, RESULT, the books) are in its order. |
| `0x29ee8e` | character_voice_kinds | high | investigation | CHINIT: 1, 2 or 3. The page's voice block plays a voice, and records it in the text history, only for a character with 2. |
| `0x29f65e` | character_ultimate_ids | high | investigation | CHINIT: the character's 奥義, a COINIT id (26 characters have one). |
| `0x29fa46` | party_member_start_skill_lines | high | investigation | CCINIT: which of the character's four skill lines are open (1) at the start. UNITECH copies them into party_member_skill_lines when the character first joins. |
| `0x2a09e6` | character_voice_clips | high | investigation | CHINIT: nine voice clips; RESULT plays column 0 when the character's level rises, ALLMAP column 5 and BTL column 7. |
| `0x2a2d0e` | character_book_pictures | high | investigation | CHINIT: pictures; the 奥義 and skill books (INFOCO, INFOSK) draw column 0. |
| `0x2a4096` | character_pictures | high | investigation | CHINIT: pictures, a column for each picture set (party_member_picture_sets); FELLOW, GAMESTART and GAMECLEAR draw them. |
| `0x2a541e` | character_fellow_pictures | high | investigation | CHINIT: pictures by picture set that FELLOW draws. |
| `0x2a67a6` | character_unit_pictures | high | investigation | CHINIT: the character's unit on the map, by picture set: SETAGENT and SETMAPDATA copy it into the player unit's area_unit_pictures column 0. |
| `0x2a7b2e` | character_unit_pictures_2 | high | investigation | CHINIT: the unit's second picture, copied into area_unit_pictures column 1. |
| `0x2a8eb6` | character_unit_extra_pictures | high | investigation | CHINIT: the unit's third picture, copied into area_unit_extra_pictures. |
| `0x2aa23e` | character_menu_pictures | high | investigation | CHINIT: pictures by picture set that FELLOW and MENU draw. |
| `0x2ab5c7` | party_member_level_card_rows | high | investigation | CCINIT: a card the character brings at each level (the column, 2 to 35): its row. CALCCC gives it with ADDCARD once, when the level is reached. |
| `0x2ac567` | party_member_level_card_slots | high | investigation | CCINIT: and that card's slot. |
| `0x2ad507` | party_member_level_items | med | investigation | An item the character brings at each level: CALCCC gives it with ADDITEM once, when the level is reached. No record of the game writes it. |
| `0x2ae4a7` | party_member_level_skills | high | investigation | CCINIT: the skills (SKINIT ids) the character has at each level, one for each skill line, at column 4 * level + line. COUNTSKILL counts one only while its line is open. |
| `0x2b2327` | party_member_level_ultimate_levels | high | investigation | CCINIT: the 奥義 level (1 to 9) the character reaches at each level; COUNTSKILL raises party_member_ultimate_levels to it. |
| `0x2b32c7` | card_row_character_ids | high | investigation | CDINIT: the character (a CHINIT id) of a card row, written where the row's first card starts. BTL reads a unit's pictures from CHINIT through it. |
| `0x2b338f` | card_append_numbers | high | investigation | CDINIT: the append (1 to 4) that added the card; unset for the base data's. |
| `0x2b4aff` | card_sort_keys | high | investigation | CDINIT: a sort number; the card lists (the book, the deck builder, the shop) are in its order. |
| `0x2b626f` | card_requires_party_flags | high | investigation | CDINIT: 1 on 300 cards that may be in a deck only while their row's character (card_row_character_ids) is in the party (CHECKCARD). |
| `0x2b79df` | card_rarities | high | investigation | CDINIT: 1 to 5, C, UC, R, SR, EU (INIT2 sets the names in 0x12a77 + rarity - 1); the card panel draws one of five marks for it. |
| `0x2b914f` | card_book_flags | high | investigation | CDINIT: 1 on the 903 cards the card book (INFOCD) lists. |
| `0x2ba8bf` | card_copyable_flags | high | investigation | CDINIT: 1 on the 229 cards the skills 複製 and 捕縛 can win from an opponent (DECKBUILD marks them). |
| `0x2bc02f` | card_race_flags | high | investigation | CDINIT: 種族, one bit a race (bits 0 to 6: 無機, 人間, 怪物, 天使, 悪魔, 魔神, 英雄). The card panel prints race_names for each bit set on its 種族 row. |
| `0x2bd79f` | card_costs | high | investigation | CDINIT: コスト, 1 to 8 (the card panel's row of that name shows card_current_costs). |
| `0x2bef0f` | card_control | high | investigation | CDINIT: 支配力, 1 to 3 (the card panel's row of that name shows card_current_control). |
| `0x2c067f` | card_attack | high | investigation | CDINIT: 攻撃力, 1 to 28 (the card panel's row of that name shows card_current_attack). |
| `0x2c1def` | card_leader_lp | high | investigation | CDINIT: ＬＰ, 8 to 400 on 338 cards. For a leader card the panel draws the LP sprite on the row where another card has HP, and shows card_current_leader_lp there. |
| `0x2c355f` | card_hp | high | investigation | CDINIT: ＨＰ, 1 to 70 on 585 cards (the card panel's HP row shows card_current_hp). |
| `0x2c4ccf` | card_slots | high | investigation | CDINIT: スロット, 1 to 3 (the card panel's row of that name shows card_current_slots). |
| `0x2c643f` | card_talent_ids | high | investigation | CDINIT: two ids of TAINIT. The card panel prints talent_names and talent_descriptions for them under 所持能力1 and 所持能力2. |
| `0x2c931f` | card_prices | high | investigation | CDINIT: two prices, the second a fifth of the first. ADDCARD sells what the player holds over 99 at the second; SHOPP and SHOPS read both. Columns: 0=buy, 1=sell. |
| `0x2cc1ff` | card_subtractive_effect_flags | high | investigation | CDINIT: 1 where ATTACK draws the card's attack effect subtractively (gfx_draw_color mode 3) instead of additively (mode 1). |
| `0x2cd96f` | card_pictures | high | investigation | CDINIT: two pictures (resource ids); the card panel draws the first. |
| `0x2d084f` | card_leader_pictures | high | investigation | CDINIT: pictures of the card as a battle's leader, a column for each side (PHASECONTROLLER). |
| `0x2d372f` | card_attack_pictures | high | investigation | CDINIT: two pictures ATTACK draws when the card attacks. |
| `0x2d660f` | card_recipe_icons | high | investigation | CDINIT: the icon DRAWRECIPE draws for the card on a recipe. |
| `0x2d7d7f` | card_attack_effects | high | investigation | CDINIT: the card's attack effect, a resource id ATTACK plays. |
| `0x2d94ef` | card_voice_clips | high | investigation | CDINIT: five voice clips the battle plays for the card: column 0 in the player's actions, 1 and 3 when it attacks, 4 when its talent acts. |
| `0x2e0a1f` | card_attack_growth | high | investigation | IMINIT: what reaching level step + 2 adds to card_attack; CALCARM sums steps 0 to level - 2. |
| `0x2ef47f` | card_leader_lp_growth | high | investigation | IMINIT: what reaching level step + 2 adds to card_leader_lp. |
| `0x2fdedf` | card_hp_growth | high | investigation | IMINIT: what reaching level step + 2 adds to card_hp. |
| `0x30c93f` | card_slot_growth | high | investigation | IMINIT: what reaching level step + 2 adds to card_slots. |
| `0x31b3a0` | talent_append_numbers | high | investigation | TAINIT: the append that added the talent. |
| `0x31b788` | talent_sort_keys | high | investigation | TAINIT: a sort number; the talent book (INFOTA) lists in its order. |
| `0x31bb70` | talent_book_flags | high | investigation | TAINIT: 1 for every talent: the talent book lists those that have it. |
| `0x31bf58` | talent_timings | high | investigation | TAINIT: when the talent acts, 1 to 9: 特殊, 移動, 登場, 行動, 攻撃, 退却, 前衛エリア, 後衛エリア, 全エリア (the word its description opens with). |
| `0x31c72a` | ultimate_append_numbers | high | investigation | COINIT: the append that added the 奥義; it picks the 奥義's scripts in ultimate_script_ids. |
| `0x31c78e` | ultimate_sort_keys | high | investigation | COINIT: a sort number; the 奥義 book (INFOCO) lists in its order. |
| `0x31c7f2` | ultimate_book_flags | high | investigation | COINIT: 1 for every 奥義: the 奥義 book lists those that have it. |
| `0x31c8ba` | ultimate_gauge_gains | high | investigation | COINIT, by level (columns 1 to 9): how much the 奥義 gauge fills each turn; PHASECONTROLLER adds it to the gauge (100 at most). |
| `0x31cca2` | ultimate_amounts | high | investigation | COINIT, by level: the number the level's text gives (damage, turns, a percent or a count); COUPACT uses it. |
| `0x31d08a` | ultimate_picture_x | high | investigation | COINIT, by level: the x at which COUP places the 奥義's picture. |
| `0x31d472` | ultimate_picture_y | high | investigation | COINIT, by level: and its y. |
| `0x31d85a` | ultimate_pictures | high | investigation | COINIT, by level: the 奥義's picture (DECKBUILD, BTL). |
| `0x31dc42` | ultimate_pictures_2 | high | investigation | COINIT, by level: a second picture DECKBUILD takes for the 奥義. |
| `0x31e02a` | ultimate_movies | high | investigation | COINIT, by level: the movie COUP plays for the 奥義. |
| `0x31e412` | skill_sort_keys | high | investigation | SKINIT: a sort number; the skill book (INFOSK) lists in its order. |
| `0x31e7fa` | skill_book_flags | high | investigation | SKINIT: 1 for every skill: the skill book lists those that have it. |
| `0x31ebe2` | skill_kinds | high | investigation | SKINIT: where the skill works: 1 outside battle, 2 in battle, 3 on the battlefield. COUNTSKILL counts a kind-3 skill only for the three role characters (party_leader_id, party_ultimate_character_id, map_agent_character_id). |
| `0x31efca` | skill_amounts | high | investigation | SKINIT: the number the skill's text gives (交渉術Ⅰ: 5 for ５％ＯＦＦ); COUNTSKILL and the battle scripts read it. |
| `0x31f79a` | item_append_numbers | high | investigation | ITINIT: the append that added the item. |
| `0x31ff6a` | item_sort_keys | high | investigation | ITINIT: a sort number; the item lists (ITEM, the book, the shops, SELITEM) are in its order. |
| `0x32073a` | item_kinds | high | investigation | ITINIT: 1 to 5, 重要, 消費, 売却, 素材, 装備 (INIT2 sets the names in 0x12a84 + kind - 1). A card carries a 装備 item; SELITEM offers 消費 items in battle. |
| `0x320f0a` | item_no_carry_over_flags | high | investigation | ITINIT: 1 on 59 of the 62 重要 items: the ones GAMESTART does not carry over into a new game when it carries the items over. |
| `0x3216da` | item_rarities | med | investigation | ITINIT: 1 to 5. The card panel draws it with the marks it draws card_rarities with. |
| `0x321eaa` | item_book_flags | high | investigation | ITINIT: 1 for every item: the item book (INFOIT) lists those that have it. |
| `0x32267a` | item_required_slots | high | investigation | ITINIT: the least card_current_slots a card may have to carry the item. |
| `0x322e4a` | item_cost_bonuses | high | investigation | ITINIT: what the item adds to the コスト of the card that carries it (the card panel, CHECKCARD). |
| `0x32361a` | item_control_bonuses | high | investigation | ITINIT: what it adds to the card's 支配力. |
| `0x323dea` | item_attack_bonuses | high | investigation | ITINIT: what it adds to the card's 攻撃力. |
| `0x3245ba` | item_leader_lp_bonuses | high | investigation | ITINIT: what it adds to a leader card's ＬＰ. |
| `0x324d8a` | item_hp_bonuses | high | investigation | ITINIT: what it adds to the card's ＨＰ. |
| `0x32555a` | item_condition_ids | high | investigation | ITINIT: a condition (an ILINIT id) the 消費 item gives its target in battle (SELITEM). |
| `0x325d2a` | item_targets | high | investigation | ITINIT: which battlefield cells the 消費 item can target: 1 the first side's, 2 the second's, 3 both; 4 and 5 the empty cells of the first or the second side (SELITEM). |
| `0x3264fa` | item_talent_ids | high | investigation | ITINIT: a talent (a TAINIT id) the item gives the card that carries it. |
| `0x326cca` | item_card_rows | high | investigation | ITINIT: where set, the one card row whose cards can carry the item (INIT2 takes an item off a card that fails). |
| `0x32749a` | item_card_slots | high | investigation | ITINIT: where set, the one slot of a row whose card can carry the item. |
| `0x327c6a` | item_required_races | high | investigation | ITINIT: bits of card_race_flags of which a card must have one to carry the item. |
| `0x32843a` | item_excluded_races | high | investigation | ITINIT: bits of card_race_flags a card must not have to carry the item. |
| `0x328c0a` | item_required_costs | high | investigation | ITINIT: the least card_current_costs a card may have to carry the item. |
| `0x3293da` | item_required_control | high | investigation | ITINIT: the least card_current_control a card may have to carry the item. |
| `0x329baa` | item_required_attack | high | investigation | ITINIT: the least card_current_attack a card may have to carry the item. |
| `0x32a37a` | item_required_leader_lp | high | investigation | ITINIT: the least card_current_leader_lp a card may have to carry the item. |
| `0x32ab4a` | item_required_hp | high | investigation | ITINIT: the least card_current_hp a card may have to carry the item. |
| `0x32b31a` | item_prices | high | investigation | ITINIT: two prices, the second a fifth of the first where there is one. ADDITEM sells what the player holds over 99 at the second; SHOPP and SHOPS read both. Columns: 0=buy, 1=sell. |
| `0x32c2ba` | item_pictures | high | investigation | ITINIT: two pictures (resource ids); the card panel draws the second. |
| `0x32d25a` | item_effect_styles | high | investigation | ITINIT: how SELITEM plays the 消費 item's effect movie: 1 on the target cell, 2 in the middle at 512 × 512, 3 at a fixed place. |
| `0x32da2a` | item_effect_draw_modes | high | investigation | ITINIT: the gfx_draw_color mode the effect movie is drawn in (1, additive, on all 31). |
| `0x32e1fa` | item_effect_movies | high | investigation | ITINIT: the effect movies; style 1 takes the target column's. |
| `0x32f96a` | condition_kinds | high | investigation | ILINIT: 1 for a helpful condition (再生, 必中 …), 2 for a harmful one (操血, 封印 …). |
| `0x32f9ca` | condition_attack_changes | high | investigation | ILINIT: what the condition adds to a card's 攻撃力 in battle while it holds (攻１ to 攻４: 1 to 4; 操血: -1). |
| `0x32f9ea` | condition_hp_changes | med | investigation | ILINIT: what the condition adds to a card's ＨＰ or ＬＰ in battle while it holds (操血: -1). |
| `0x32fa2a` | condition_turn_hp_changes | med | investigation | ILINIT: a change each turn (強酸 -1, 再生 +1); PHASECONTROLLER adds 再生's to a card's ＨＰ at the start of a turn. |
| `0x32fa4a` | condition_turn_hp_changes_2 | med | investigation | ILINIT: 再生 1; PHASECONTROLLER adds it to a card's ＨＰ like condition_turn_hp_changes, on another branch. |
| `0x32fa6a` | condition_icons | high | investigation | ILINIT: the condition's icon, 1 to 19: its row in a strip of 15 × 15 icons DRAWFIELD and DRAWTALENT draw from, at the column of its kind. |
| `0x32fa8a` | battle_experience | med | investigation | RFINIT: four rows of nine numbers: the experience RESULT gives the party for each enemy card defeated, at the cell two numbers of that card pick. |
| `0x32fab2` | rank_point_thresholds | high | investigation | RFINIT: the points each rank needs (CALCPR): rank r at cell 10 * (r % 10) + r / 10, ranks 1 to 99, up to 99999. |
| `0x32fb16` | character_level_experience | high | investigation | RFINIT: the experience a character needs for each level, 0 to 9999 (RESULT, FELLOW). |
| `0x32fb57` | random_condition_switches | high | investigation | RFINIT: rows of switches: which of random_conditions BTL may give at random, by a row it picks. |
| `0x32fbbb` | random_conditions | high | investigation | RFINIT: eight conditions (ILINIT ids 17 to 25: 必中, 回避, 攻１ …) BTL gives at random. |
| `0x32fbc5` | area_places | high | investigation | AMINIT: the place (an AMINIT id) the area belongs to; scene code tests whether the area in play is in the same place as a given area. |
| `0x32fcf1` | area_map_pictures | high | investigation | AMINIT: the area's map pictures: columns 0 to 4 by time_slot, column 5 another (SETMAPTEX, ALLMAP). |
| `0x3303f9` | area_music | med | investigation | AMINIT: the area's two tracks of music, by 0x987a (READY). |
| `0x330651` | area_unit_scales | high | investigation | AMINIT: a unit's size at the top and at the bottom of the map, in percent; DRAWOBJ scales a unit between the two by its point's y. Columns: 0=top, 1=bottom. |
| `0x3308a9` | area_battle_backgrounds | high | investigation | AMINIT: two pictures PHASECONTROLLER sets behind the battlefield for a battle in the area. |
| `0x330b01` | area_battle_rules | high | investigation | AMINIT: the rule (a REINIT id) of a battle in the area (16 areas). |
| `0x330c2d` | area_point_kinds | high | investigation | MPINIT, for the area in play: a point's kind, 1 to 3. ALLMAP reads area_point_targets for a point of kind 3. |
| `0x330cf5` | area_point_targets | high | investigation | MPINIT, for the area in play: where a point of kind 3 leads, an area and a point of it. ALLMAP reads the pair when the player is on the point and looks the area up in AMINIT. Columns: 0=area, 1=point. |
| `0x331015` | area_point_x | high | investigation | MPINIT, for the area in play: a point's x on the map; ALLMAP puts the point's hot area around (x, y). |
| `0x3310dd` | area_point_y | high | investigation | MPINIT, for the area in play: a point's y on the map. |
| `0x33207d` | area_route_points | high | investigation | ROINIT, for the area in play: the two points a route joins; DRAWMAP and ALLMAP read the pair. |
| `0x332dc6` | area_unit_kinds | med | investigation | UNINIT, for the area in play: the unit's kind, 1 to 4; ALLMAP and DRAWOBJ treat kinds 1 and 4 apart. |
| `0x332e8e` | area_unit_icons | high | investigation | UNINIT, for the area in play: the unit's icon, 1-based, in a strip DRAWFRAME draws from. |
| `0x332f56` | area_unit_decks | high | investigation | UNINIT, for the area in play: the unit's deck (a DEINIT id): a battle with the unit has it (SETREGULATION), and UNITINFO shows its drops. |
| `0x33301e` | area_unit_cards | high | investigation | UNINIT, for the area in play: a unit's card, as its row and slot of CDINIT; BTL passes the pair to CALCARM. Columns: 0=row, 1=slot. |
| `0x333276` | area_unit_routines | high | investigation | UNINIT, for the area in play: the unit's routine (unit_routine), which MVRTN runs. |
| `0x333406` | area_unit_state_ids | med | investigation | UNINIT, for the area in play: the unit's id in saved state (1 to 126); ALLMAP copies it into the save's 0x9ba7 + slot when it sets the unit up and tests the bits of 0x17cd8 + id. |
| `0x3342de` | area_unit_battle_music | high | investigation | UNINIT, for the area in play: the music of a battle with the unit (BTL). |
| `0x3345fe` | area_unit_pictures | high | investigation | UNINIT, for the area in play: two pictures of the unit (SETMAPTEX); for the player's unit, slot 1, SETAGENT copies them from CHINIT. |
| `0x33478e` | area_unit_extra_pictures | high | investigation | UNINIT, for the area in play: a third picture of the unit. |
| `0x334856` | deck_card_rows | med | investigation | DEINIT: the row in CDINIT (1 to 167) of each card of the deck, slots 1 to 20. |
| `0x33bd86` | deck_card_slots | med | investigation | DEINIT: the slot in that row (1 to 23) of the same card: card id = row * 30 + slot. |
| `0x3432b6` | deck_card_max_copies | high | investigation | DEINIT: the most copies of each card a map unit's deck holds. |
| `0x34a7e6` | deck_card_copies | high | investigation | DEINIT: how many copies of each card the deck holds: a battle's deck takes this many (DECKBUILD), and a map unit's starts from it. |
| `0x351d16` | deck_card_copy_chances | high | investigation | DEINIT: a percent for each card slot; for each copy between deck_card_copies and deck_card_max_copies ALLMAP draws 0 to 99 and adds the copy where the draw is at least this. |
| `0x359246` | deck_card_max_unless_scenes | med | investigation | A scene (id + 1) for each card slot: ALLMAP gives a map unit the most copies of the slot while it has not been played. No record writes it. |
| `0x360776` | deck_card_max_once_scenes | med | investigation | A scene for each card slot: ALLMAP gives a map unit the most copies of the slot once it has been played. No record writes it. |
| `0x367ca6` | deck_start_boards | high | investigation | DEINIT: the opponent's battlefield at the start, two rows of three: 0 or less goes into the cell as it is, a positive value is a slot of the deck whose card starts there (PHASECONTROLLER). |
| `0x369416` | deck_cpu_profiles | high | investigation | DEINIT: the CPU profile (cpu_profile) the deck is played with; SETREGULATION passes it on. |
| `0x3697fe` | deck_drop_weights | high | investigation | DEINIT: four drops, each one's weight. RESULT makes weight sum / 100 drops (one more with the rest's percent), each drawn by weight. |
| `0x36a79e` | deck_drop_items | high | investigation | DEINIT: a drop that is an item: its id. |
| `0x36b73e` | deck_drop_card_rows | high | investigation | DEINIT: a drop that is a card: its row. |
| `0x36c6de` | deck_drop_card_slots | high | investigation | DEINIT: and its slot. |
| `0x36d67e` | battle_uses_player_deck_flags | high | investigation | DUINIT: 1 where the player fights with their own deck and leader; elsewhere the battle sets the player's side too (SETREGULATION). |
| `0x36da66` | battle_leader_cards | high | investigation | DUINIT: the leader card of each side, as a row and a slot. Columns: 0=side_0_row, 1=side_0_slot, 2=side_1_row, 3=side_1_slot. |
| `0x36ea06` | battle_decks | high | investigation | DUINIT: each side's deck (a DEINIT id). Columns: 0=side_0, 1=side_1. |
| `0x36f1d6` | battle_opponent_ultimates | high | investigation | DUINIT: the opponent's 奥義 (a COINIT id). |
| `0x36f5be` | battle_opponent_ultimate_levels | high | investigation | DUINIT: and its level. |
| `0x36f9a6` | battle_opponent_ultimate_pictures | high | investigation | DUINIT: the opponent's 奥義 picture. |
| `0x36fd8e` | battle_opponent_ultimate_picture_x | high | investigation | DUINIT: and its x. |
| `0x370176` | battle_opponent_ultimate_picture_y | high | investigation | DUINIT: and its y. |
| `0x37055e` | battle_rules | high | investigation | DUINIT: the battle's rule (a REINIT id); SETREGULATION takes it. |
| `0x370946` | battle_music | high | investigation | DUINIT: the battle's music (BTL plays it). |
| `0x371116` | battle_backgrounds | high | investigation | DUINIT: two pictures PHASECONTROLLER sets behind the battlefield. |
| `0x3718e6` | battle_rule_outcomes | med | investigation | REINIT: what five events do to the battle, 0 nothing, 1 a win, 2 a loss (by the rules' texts). Without a rule SETREGULATION uses 0, 0, 2, 1, 2. Columns: 0=player_deck_out, 1=opponent_deck_out, 2=player_lp_zero, 3=opponent_lp_zero, 4=turn_limit. |
| `0x371ada` | battle_rule_hp_recovery_flags | high | investigation | REINIT: 1 where the cards' ＨＰ recovers at the start of a turn (ターン開始時にＨＰ回復). |
| `0x371ec2` | battle_rule_turn_limits | high | investigation | REINIT: the turn limit (99 for none). |
| `0x371f26` | battle_rule_hand_sizes | high | investigation | REINIT: the most cards in hand (6 but for 最大手札４). |
| `0x371f8a` | battle_rule_deck_sizes | high | investigation | REINIT: the most cards in a deck (30 but for 編成最大枚数 rules). |
| `0x371fee` | battle_rule_use_bans | med | investigation | REINIT: two bits; PH03_PLR_ACTION tests the first, BTL and SETBTSKILL the second. アイテム・奥義使用不可 has 3. |
| `0x3723d6` | battle_rule_cost_ranges | high | investigation | REINIT: a card's コスト (with its item's bonus) must lie in [least, most], a most of 0 for none (CHECKCARD). Columns: 0=least, 1=most. |
| `0x37249e` | battle_rule_control_ranges | high | investigation | REINIT: the same for 支配力. Columns: 0=least, 1=most. |
| `0x372566` | battle_rule_attack_ranges | high | investigation | REINIT: the same for 攻撃力. Columns: 0=least, 1=most. |
| `0x37262e` | battle_rule_hp_ranges | high | investigation | REINIT: the same for ＨＰ. Columns: 0=least, 1=most. |
| `0x3726f6` | battle_rule_race_masks | high | investigation | REINIT: bits of card_race_flags. A card with a race of the first may not be used, nor a card all of whose races are in the second. Columns: 0=any_banned, 1=all_banned. |
| `0x3727be` | battle_rule_banned_card_rows | high | investigation | REINIT: card rows whose cards may not be used (CHECKCARD). |
| `0x372ba6` | recipe_items | high | investigation | ALINIT: the item the recipe makes, on 185 recipes. |
| `0x373376` | recipe_card_rows | high | investigation | ALINIT: or the card it makes: its row, on the other 432. |
| `0x373b46` | recipe_card_slots | high | investigation | ALINIT: and its slot. |
| `0x374316` | recipe_points | high | investigation | ALINIT: points the recipe earns each time it is made (ALCHEMY adds them to points). |
| `0x374ae6` | recipe_places | high | investigation | ALINIT: the alchemy places that offer the recipe, one bit a place (shop_place_id). |
| `0x3752b6` | recipe_least_chapters | high | investigation | ALINIT: the least chapter. |
| `0x375a86` | recipe_least_days | med | investigation | The least day; ALCHEMY's test reads it and no record writes it. |
| `0x376256` | recipe_weekdays | med | investigation | The days of the week (bits of day % 7); ALCHEMY's test reads it and no record writes it. |
| `0x376a26` | recipe_time_slots | med | investigation | The time slots (bits of time_slot); ALCHEMY's test reads it and no record writes it. |
| `0x3771f6` | recipe_least_ranks | high | investigation | ALINIT: the least rank. |
| `0x3779c6` | recipe_party_characters | high | investigation | ALINIT: a character who must be in the party. |
| `0x378196` | recipe_party_character_levels | high | investigation | ALINIT: and that character's least level. |
| `0x378966` | recipe_skills | high | investigation | ALINIT: a skill the party must have (party_skill_counts). |
| `0x379136` | recipe_required_items | high | investigation | ALINIT: an item the player must have obtained (item_obtained_flags). |
| `0x379906` | recipe_required_card_rows | high | investigation | ALINIT: a card the player must have obtained (card_obtained_flags): its row. |
| `0x37a0d6` | recipe_required_card_slots | high | investigation | ALINIT: and its slot. |
| `0x37a8a6` | recipe_scenes_played | high | investigation | ALINIT: scenes (each id + 1) that must all have been played. |
| `0x37cfb6` | recipe_scenes_not_played | med | investigation | Scenes none of which may have been played; ALCHEMY's test reads it and no record writes it. |
| `0x37f6c6` | recipe_ingredient_items | high | investigation | ALINIT: the four ingredients: an item id where the ingredient is an item. |
| `0x381606` | recipe_ingredient_card_rows | high | investigation | ALINIT: or a card's row where it is a card. |
| `0x383546` | recipe_ingredient_card_slots | high | investigation | ALINIT: and its slot. |
| `0x385486` | recipe_ingredient_counts | high | investigation | ALINIT: how many of each ingredient the recipe takes. |
| `0x3873c6` | shop_entry_items | high | investigation | SHINIT: the item the entry sells, on 135 entries. |
| `0x3877ae` | shop_entry_card_rows | high | investigation | SHINIT: or the card it sells: its row, on the other 97. |
| `0x387b96` | shop_entry_card_slots | high | investigation | SHINIT: and its slot. |
| `0x387f7e` | shop_entry_shops | high | investigation | SHINIT: the shops that sell it, one bit a shop (shop_place_id). |
| `0x388366` | shop_entry_least_chapters | high | investigation | SHINIT: the least chapter. |
| `0x38874e` | shop_entry_least_days | med | investigation | The least day; SHOPP's test reads it and no entry writes it. |
| `0x388b36` | shop_entry_weekdays | med | investigation | The days of the week; SHOPP's test reads it and no entry writes it. |
| `0x388f1e` | shop_entry_time_slots | med | investigation | The time slots; SHOPP's test reads it and no entry writes it. |
| `0x389306` | shop_entry_least_ranks | high | investigation | SHINIT: the least rank. |
| `0x3896ee` | shop_entry_party_characters | high | investigation | SHINIT: a character who must be in the party. |
| `0x389ad6` | shop_entry_party_character_levels | high | investigation | SHINIT: and that character's least level. |
| `0x389ebe` | shop_entry_skills | med | investigation | SHINIT: a skill the party must have (party_skill_counts). ARENA's test also reads this array, at an opponent's id. |
| `0x38a2a6` | shop_entry_scenes_played | high | investigation | SHINIT: scenes (each id + 1) that must all have been played. |
| `0x38aa76` | shop_entry_scenes_not_played | high | investigation | SHINIT: scenes none of which may have been played. |
| `0x38b246` | opponent_battles | high | investigation | ARINIT: the battle (a DUINIT id) the opponent is fought in. |
| `0x38b62e` | opponent_arenas | high | investigation | ARINIT: the arena the opponent fights in, 1 to 9. |
| `0x38ba16` | opponent_sort_keys | high | investigation | ARINIT: a sort number; ARENA lists the opponents in its order. |
| `0x38bdfe` | opponent_reward_items | high | investigation | ARINIT: the reward when it is an item. |
| `0x38c1e6` | opponent_reward_card_rows | high | investigation | ARINIT: the reward when it is a card: its row. |
| `0x38c5ce` | opponent_reward_card_slots | high | investigation | ARINIT: and its slot. |
| `0x38c9b6` | opponent_reward_counts | high | investigation | ARINIT: how many of the reward the player gets. |
| `0x38d186` | opponent_scenes | med | investigation | ARINIT: three scenes: column 0 is played before the battle, BTL plays column 1 after a win and column 2 after a loss. Columns: 0=before, 1=after_win, 2=after_loss. |
| `0x38dd3e` | opponent_places | high | investigation | ARINIT: the places that hold the opponent, one bit a place (arena_place_id). |
| `0x38e126` | opponent_required_opponents | high | investigation | ARINIT: another opponent who must have been beaten (bit 1 of opponent_states). |
| `0x38ecde` | opponent_weekdays | high | investigation | ARINIT: the days of the week (bits of day % 7) the opponent is there. |
| `0x39044e` | opponent_scenes_played | high | investigation | ARINIT: scenes (each id + 1) that must all have been played. |
| `0x390c1e` | opponent_scenes_not_played | high | investigation | ARINIT: scenes none of which may have been played. |
| `0x3913ee` | opponent_tournament_groups | med | investigation | ARINIT: a group (2 to 8) ARENA draws a tournament arena's opponents from. |
| `0x391fa9` | unit_routine_actions | high | investigation | RTINIT: a map unit's routine step by step: the step's action, 1 to 8 for RTN_M001 to RTN_M008; 0 ends the routine (MVRTN). |
| `0x39e2f9` | unit_routine_chances | high | investigation | RTINIT: the percent chance the step runs (MVRTN draws 0 to 99). |
| `0x3aa649` | unit_routine_args | high | investigation | RTINIT: the step's first argument (RTN_M001 waits that long, 20 without one). |
| `0x3b6999` | unit_routine_args_2 | med | investigation | RTINIT: the step's second argument. |
| `0x3c2ce9` | unit_routine_args_3 | med | investigation | RTINIT: the step's third argument. |
| `0x3e76d9` | unit_routine_scenes_played | high | investigation | RTINIT: for each step, three scenes (each id + 1) that must all have been played, at column 3 * step + n. |
| `0x40c0c9` | unit_routine_scenes_not_played | high | investigation | RTINIT: for each step, three scenes none of which may have been played. |
| `0x430ab9` | cpu_profile_settings | med | investigation | RTINIT: a CPU profile's twenty settings, which PH12_CPU_MOVE and PH13_CPU_ACTION read. |
| `0x4358d9` | cpu_profile_move_choices | med | investigation | RTINIT: the profile's choices for moving (PH12_CPU_MOVE). |
| `0x437fe9` | cpu_profile_action_choices | med | investigation | RTINIT: its choices for acting (PH13_CPU_ACTION). |
| `0x43a6f9` | cpu_profile_attack_choices | med | investigation | RTINIT: its choices for attacking (PH14_CPU_ATTACK). |
| `0x43ce09` | cpu_profile_attack_lists | med | investigation | RTINIT: a list ATTACK reads for the profile. |
| `0x43f6e6` | card_max_levels | high | investigation | Computed by CALCARM: 1, or 2 + the highest step at which one of the four growth arrays has a number for the card. |
| `0x440e56` | card_levels | high | investigation | Computed by CALCARM: card_experience / 100 + 1, at most card_max_levels. |
| `0x4425c6` | card_level_progress | high | investigation | Computed by CALCARM: card_experience % 100, or 0 at the highest level. The card panel draws it as a bar. |
| `0x4454a6` | card_current_slots | high | investigation | Computed by CALCARM: card_slots and card_slot_growth for the card's level, kept within 0 to 3. |
| `0x446c16` | card_current_costs | high | investigation | Computed by CALCARM: card_costs, kept within 0 to 9. |
| `0x448386` | card_current_control | high | investigation | Computed by CALCARM: card_control, kept within 0 to 9. |
| `0x449af6` | card_current_attack | high | investigation | Computed by CALCARM: card_attack and card_attack_growth for the card's level, kept within 0 to 999. |
| `0x44b266` | card_current_leader_lp | high | investigation | Computed by CALCARM: card_leader_lp and card_leader_lp_growth for the card's level, kept within 0 to 999. |
| `0x44c9d6` | card_current_hp | high | investigation | Computed by CALCARM: card_hp and card_hp_growth for the card's level, kept within 0 to 999. |
| `0x4502a1` | party_skill_counts | high | investigation | Built by COUNTSKILL: how many open skill lines of the party's characters have each skill (an SKINIT id). The shops', recipes' and arenas' tests read it. |
| `0x4525dd` | scjump_chain_script_ids | high | investigation | The scripts a caller runs to pick the next scene: INIT2 puts SCJUMP in slot 0 and each $n$AUTORUN its $n$SCJUMP in slot n. |
| `0x4525fd` | ready_open_script_ids | high | investigation | The READY and OPEN script of each layer. Columns: 0=ready, 1=open. |
| `0x45263d` | tumes_script_ids | high | investigation | The TUMES script of each layer. |
| `0x45265d` | item_action_script_ids | high | investigation | ITEMACT and each $n$ITEMACT: SELITEM runs the slot of the item's append number for an item used in battle. |
| `0x45267d` | ultimate_script_ids | high | investigation | COUPACT, COUPANIME and COUPRTN of each layer: a 奥義 runs the row of its append number. Columns: 0=act, 1=anime, 2=rtn. |
| `0x4526dd` | talent_script_ids | high | investigation | TALENT, TALENTACT and TALENTRTN of each layer: a talent runs the row of its append number. Columns: 0=talent, 1=act, 2=rtn. |
| `0x45273d` | bginit_script_ids | high | investigation | BGINIT of each layer. |
| `0x45275d` | mpinit_script_ids | high | investigation | MPINIT of each layer: SETMAPDATA runs slots 0 to 31 in turn. |
| `0x45277d` | roinit_script_ids | high | investigation | ROINIT of each layer, run in turn the same way. |
| `0x45279d` | uninit_script_ids | high | investigation | UNINIT of each layer, run in turn the same way. |
| `0x0` | — | low | auto-shape | array |
| `0x5` | — | low | auto-shape | array |
| `0xd7` | — | low | auto-shape | array |
| `GS 0xe8` | — | low | auto-shape | array |
| `GS 0xfc` | — | low | auto-shape | array |
| `0x138` | — | low | auto-shape | array |
| `0x1a9` | — | low | auto-shape | array |
| `GS 0x1c5` | — | low | auto-shape | array |
| `GS 0x1d9` | — | low | auto-shape | array |
| `0x27b` | — | low | auto-shape | array |
| `0x34d` | — | low | auto-shape | array |
| `0x41f` | — | low | auto-shape | array |
| `0x4f1` | — | low | auto-shape | array |
| `0x2d57` | — | low | auto-shape | array |
| `0x3147` | — | low | auto-shape | array |
| `0x3583` | — | low | auto-shape | array |
| `0x3597` | — | low | auto-shape | array |
| `0x35ab` | — | low | auto-shape | array |
| `0x35bf` | — | low | auto-shape | array |
| `0x35d3` | — | low | auto-shape | array |
| `0x35e7` | — | low | auto-shape | array |
| `0x3b44` | — | low | auto-shape | array |
| `0x3cd4` | — | low | auto-shape | array |
| `0x3f2c` | — | low | auto-shape | array |
| `0x97a9` | — | low | auto-shape | array |
| `0x97b0` | — | low | auto-shape | array |
| `0x994e` | — | low | auto-shape | array |
| `0x9a16` | — | low | auto-shape | array |
| `0x9adf` | — | low | auto-shape | array |
| `0x9ba7` | — | low | auto-shape | array |
| `0x9c6f` | — | low | auto-shape | array |
| `0x9d37` | — | low | auto-shape | array |
| `GS 0x1265c` | — | low | auto-shape | array |
| `0x1614f` | — | low | auto-shape | array |
| `0x17988` | — | low | auto-shape | array |
| `0x17992` | — | low | auto-shape | array |
| `0x1799c` | — | low | auto-shape | array |
| `0x179a6` | — | low | auto-shape | array |
| `0x179b0` | — | low | auto-shape | array |
| `0x17a79` | — | low | auto-shape | array |
| `0x17b41` | — | low | auto-shape | array |
| `0x17b48` | — | low | auto-shape | array |
| `0x17c10` | — | low | auto-shape | array |
| `0x17cd8` | — | low | auto-shape | array |
| `0x17da0` | — | low | auto-shape | array |
| `0x17e68` | — | low | auto-shape | array |
| `0x183c4` | — | low | auto-shape | array |
| `0x1af90` | — | low | auto-shape | array |
| `0x1cae8` | — | low | auto-shape | array |
| `0x1ced0` | — | low | auto-shape | array |
| `0x1d6a0` | — | low | auto-shape | array |
| `0x1d704` | — | low | auto-shape | array |
| `0x1daec` | — | low | auto-shape | array |
| `0x361f0` | — | low | auto-shape | array |
| `0x369c0` | — | low | auto-shape | array |
| `0x38518` | — | low | auto-shape | array |
| `0x38ce8` | — | low | auto-shape | array |
| `0x390d0` | — | low | auto-shape | array |
| `0x39134` | — | low | auto-shape | array |
| `0x3951c` | — | low | auto-shape | array |
| `0x395e4` | — | low | auto-shape | array |
| `0x399cc` | — | low | auto-shape | array |
| `0x3b524` | — | low | auto-shape | array |
| `0x3bcf4` | — | low | auto-shape | array |
| `0x3c0dc` | — | low | auto-shape | array |
| `0x3c8ac` | — | low | auto-shape | array |
| `0x3cc94` | — | low | auto-shape | array |
| `0x3d07c` | — | low | auto-shape | array |
| `0x3d468` | — | low | auto-shape | array |
| `0x3d46a` | — | low | auto-shape | array |
| `0x3d46c` | — | low | auto-shape | array |
| `0x3fbad` | — | low | auto-shape | array |
| `0x3fbd3` | — | low | auto-shape | array |
| `0x3fbe7` | — | low | auto-shape | array |
| `0x21481b` | — | low | auto-shape | array |
| `0x214830` | — | low | auto-shape | array |
| `0x2148f8` | — | low | auto-shape | array |
| `0x2149c6` | — | low | auto-shape | array |
| `0x2149db` | — | low | auto-shape | array |
| `0x214aa3` | — | low | auto-shape | array |
| `0x219991` | — | low | auto-shape | array |
| `0x21c0a1` | — | low | auto-shape | array |
| `0x225ce1` | — | low | auto-shape | array |
| `0x22be89` | — | low | auto-shape | array |
| `0x22ca41` | — | low | auto-shape | array |
| `0x260699` | — | low | auto-shape | array |
| `0x2606ce` | — | low | auto-shape | array |
| `0x29c843` | — | low | auto-shape | array |
| `0x29c90b` | — | low | auto-shape | array |
| `0x29f276` | — | low | auto-shape | array |
| `0x32f98a` | — | low | auto-shape | array |
| `0x32f9aa` | — | low | auto-shape | array |
| `0x32fa0a` | — | low | auto-shape | array |
| `0x331975` | — | low | auto-shape | array |
| `0x331a3d` | — | low | auto-shape | array |
| `0x331b05` | — | low | auto-shape | array |
| `0x331bcd` | — | low | auto-shape | array |
| `0x331c95` | — | low | auto-shape | array |
| `0x331d5d` | — | low | auto-shape | array |
| `0x331e25` | — | low | auto-shape | array |
| `0x331eed` | — | low | auto-shape | array |
| `0x331fb5` | — | low | auto-shape | array |
| `0x3326bd` | — | low | auto-shape | array |
| `0x332785` | — | low | auto-shape | array |
| `0x33284d` | — | low | auto-shape | array |
| `0x332915` | — | low | auto-shape | array |
| `0x3329dd` | — | low | auto-shape | array |
| `0x3331ae` | — | low | auto-shape | array |
| `0x33333e` | — | low | auto-shape | array |
| `0x3334ce` | — | low | auto-shape | array |
| `0x333596` | — | low | auto-shape | array |
| `0x33365e` | — | low | auto-shape | array |
| `0x334216` | — | low | auto-shape | array |
| `0x3343a6` | — | low | auto-shape | array |
| `0x33446e` | — | low | auto-shape | array |
| `0x334536` | — | low | auto-shape | array |
| `0x370d2e` | — | low | auto-shape | array |
| `0x38e50e` | — | low | auto-shape | array |
| `0x38e8f6` | — | low | auto-shape | array |
| `0x38f0c6` | — | low | auto-shape | array |
| `0x38f4ae` | — | low | auto-shape | array |
| `0x38f896` | — | low | auto-shape | array |
| `0x38fc7e` | — | low | auto-shape | array |
| `0x390066` | — | low | auto-shape | array |
| `0x3917d6` | — | low | auto-shape | array |
| `0x391bbe` | — | low | auto-shape | array |
| `0x43f519` | — | low | auto-shape | array |
| `0x43f522` | — | low | auto-shape | array |
| `0x43f52d` | — | low | auto-shape | array |
| `0x43f5f9` | — | low | auto-shape | array |
| `0x43f675` | — | low | auto-shape | array |
| `0x43f67e` | — | low | auto-shape | array |
| `0x44e236` | — | low | auto-shape | array |
| `0x44e23b` | — | low | auto-shape | array |
| `0x44e241` | — | low | auto-shape | array |
| `0x44fa6b` | — | low | auto-shape | array |
| `0x45023b` | — | low | auto-shape | array |
| `0x450689` | — | low | auto-shape | array |
| `0x45068f` | — | low | auto-shape | array |
| `0x451e01` | — | low | auto-shape | array |
| `0x4525d1` | — | low | auto-shape | array |
| `0x4527bd` | — | low | auto-shape | array |
| `0x452921` | — | low | auto-shape | array |
| `0x452923` | — | low | auto-shape | array |
| `0x45d325` | — | low | auto-shape | array |
| `0x45d34d` | — | low | auto-shape | array |
| `0x45d34f` | — | low | auto-shape | array |
| `0x45d3a1` | — | low | auto-shape | array |
| `0x45d3f3` | — | low | auto-shape | array |
| `0x45d41b` | — | low | auto-shape | array |
| `0x45d4bb` | — | low | auto-shape | array |
| `0x45d4e3` | — | low | auto-shape | array |
| `0x45d55b` | — | low | auto-shape | array |
| `0x45d74d` | — | low | auto-shape | array |
| `0x45d7b1` | — | low | auto-shape | array |
| `0x45d815` | — | low | auto-shape | array |
| `0x45d817` | — | low | auto-shape | array |
| `0x45d819` | — | low | auto-shape | array |
| `0x45d81b` | — | low | auto-shape | array |
| `0x467fc9` | — | low | auto-shape | array |
| `0x468815` | — | low | auto-shape | array |
| `0x4688e3` | — | low | auto-shape | array |
| `0x4688ef` | — | low | auto-shape | array |
| `0x4688fb` | — | low | auto-shape | array |
| `0x46891f` | — | low | auto-shape | array |
| `0x468921` | — | low | auto-shape | array |
| `0x468923` | — | low | auto-shape | array |
| `0x468925` | — | low | auto-shape | array |
| `0x468927` | — | low | auto-shape | array |
| `0x468929` | — | low | auto-shape | array |
| `0x46892b` | — | low | auto-shape | array |
| `0x46892d` | — | low | auto-shape | array |
| `0x46892f` | — | low | auto-shape | array |
| `0x468931` | — | low | auto-shape | array |
| `0x469103` | — | low | auto-shape | array |
| `0x469106` | — | low | auto-shape | array |
| `0x46910f` | — | low | auto-shape | array |
| `0x46a896` | — | low | auto-shape | array |
| `0x46b066` | — | low | auto-shape | array |
| `0x46b0ca` | — | low | auto-shape | array |
| `0x46d00a` | — | low | auto-shape | array |
| `0x46d06e` | — | low | auto-shape | array |

## index-pointer

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x3140` | adv_speaker_character_id | high | investigation | The page's speaker: a character id, or -1 for narration. set_page_face sets it from the page's face; draw_nameplate and the page's voice block read it. |
| `0x3143` | adv_name_form | high | investigation | Which column of character_names the name plate shows (0 to 5); any other value shows ？？？？ (draw_speaker_name). |
| `0x352f` | scjump_context_id | med | investigation | Which of SCJUMP's seven rule lists runs (1 to 7); the caller sets it before running the chain. What each context is has not been read. |
| `0x3ad5` | battle_id | high | investigation | Saved: the battle to fight, a DUINIT id; scene code sets it, or an arena opponent does. |
| `0x3ada` | party_leader_id | med | investigation | Saved: a party character FELLOW picks (INITDECK sets 1). The map shows it as the player's unit unless map_agent_character_id is set, and RESULT gives it half as much experience again. |
| `0x3adb` | party_ultimate_character_id | med | investigation | Saved: the party character whose 奥義 the player uses, picked in FELLOW; LEAVECH picks another one that has a 奥義 when it leaves. RESULT gives it half as much experience again. |
| `0x3adc` | map_agent_character_id | med | investigation | Saved: the character SETAGENT shows as the player's unit on the map, where set; party_leader_id otherwise. |
| `0x987c` | time_slot | med | investigation | Saved: the time of day, 0 to 4; area_map_pictures has a picture for each, and BGINIT substitutes pictures by it. |
| `0x9883` | current_area_id | high | investigation | The area in play, an id of AMINIT: scripts index AMINIT's arrays with it, MPINIT, ROINIT and UNINIT fill their arrays for it (MPINIT has a branch for each of AMINIT's 150 ids), and most SCJUMP rules test it. |
| `0x1831e` | arena_place_id | med | investigation | Saved: the arena place in use; opponent_places has one bit a place. |
| `0x18323` | current_opponent_id | high | investigation | Saved: the arena opponent in play, an ARINIT id. |
| `0x3fb90` | adv_face_id | med | investigation | A page's face id before set_page_face (a row of CFINIT); other library subs pass a picture id in the same cell. |
| `0x3fb91` | adv_voice_id | high | investigation | The page's voice clip: the page's voice block sets it, clears it when the speaker's voice setting mutes it, and plays it. |
| `0x260700` | current_goal_id | high | investigation | The goal GETFATE makes current: the open kind-1 goal latest in the sort order. |
| `0x29eaa4` | character_arg_id | high | investigation | The character UNITECH, LEAVECH, CALCCC and the panels work on; a caller sets it before running them. |
| `0x43f662` | item_arg_id | high | investigation | The item ADDITEM and the card panel work on. |
| `0x43f663` | item_arg_count | med | investigation | How many ADDITEM adds (or takes, when negative). |
| `0x43f664` | card_arg_row | high | investigation | The row of the card CALCARM and the card panel work on; a caller sets it and card_arg_slot before calling CALCARM through preloaded script slot 0x1c. |
| `0x43f665` | card_arg_slot | high | investigation | The slot of that card. |
| `0x43f666` | card_arg_count | med | investigation | How many ADDCARD adds. |
| `0x4525d8` | shop_place_id | high | investigation | The shop or alchemy place in use: recipe_places and shop_entry_shops have one bit a place. |
| `0x4525dc` | mounted_append_flags | high | investigation | Bit n is set by $n$AUTORUN: append n is mounted. |
| `0x469105` | battle_rule_id | high | investigation | The rule in play, a REINIT id: SETREGULATION takes it from battle_rules, or for a battle on the map from area_battle_rules. |

## story-flag

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x5e0` | scene_played_flags | high | investigation | A scene's own code sets [scene] to 1 (SC0000 sets cells 0 to 9), and a save keeps it. SCJUMP's rules test it: 585 of the base script's begin with their own scene's cell != 1, and others ask that another scene's is 1. |
| `0x36cd` | chapter | high | investigation | Saved: the chapter, which the chapters' first scenes set. The recipes', shop entries' and goals' tests compare it. |
| `0x17f36` | opponent_states | high | investigation | Saved: bit 0 once the opponent's conditions have held (ARENA), bit 1 once beaten (BTL). |
| `0x183d0` | card_row_locks | high | investigation | Saved: a scene sets [row] to 1 to bar a card row from decks for a while and clears it later (SC0180, row 5); CHECKCARD refuses the row's cards meanwhile. |
| `0x18498` | goal_states | high | investigation | Saved: bit 0 once the goal is open, and bits 1 and 2 by two more pairs of scene lists (SETFATE). |

## string-table

| address | name | conf | source | usage |
|---|---|---|---|---|
| `GS 0x29b2` | music_titles | high | investigation | MUINIT: a track's title. |
| `GS 0x29e4` | goal_titles | high | investigation | FAINIT: a line of text (『天慶第二学園』へ). |
| `GS 0x2dcc` | goal_titles_2 | high | investigation | FAINIT: a second line, the same as the first on 449 of the 454 entries. |
| `GS 0x31b4` | goal_lines | high | investigation | FAINIT: four lines; the first is a chapter's heading (【共通ルート】　序章). |
| `GS 0x4154` | character_names | high | investigation | CHINIT. The name plate's text: draw_speaker_name shows character_names[adv_speaker_character_id][adv_name_form]. Columns: 0=name, 1=descriptor, 2=descriptor_2, 3=descriptor_3, 4=descriptor_4, 5=descriptor_5. |
| `GS 0x58c4` | voice_setting_labels | high | investigation | CVINIT: a voice setting's three labels (エキストラ; 仙崎 秀哉 …). |
| `GS 0x647c` | glossary_titles | high | investigation | VIINIT: a glossary entry's title (【用語】ＭＨＩ). |
| `GS 0x6864` | profile_names | high | investigation | CIINIT: a character profile's name. |
| `GS 0x692c` | profile_lines | high | investigation | CIINIT: a profile's sixteen lines. |
| `GS 0x75ac` | tip_titles | high | investigation | TIINIT: the entry's title (日常モード, 拠点アイコン・サムネイル …). |
| `GS 0x7994` | character_full_names | high | investigation | CHINIT: the full name with its reading (仙崎秀哉／せんざきしゅうや), on 27 characters. |
| `GS 0x7d7c` | card_names | high | investigation | CDINIT: a card's name. The card panel draws it (DRAWCARDINFO). |
| `GS 0x94ec` | card_flavor_lines | high | investigation | CDINIT: up to three lines of flavour text. |
| `GS 0xdb3c` | talent_names | high | investigation | TAINIT: a talent's name. |
| `GS 0xdf24` | talent_descriptions | high | investigation | TAINIT: its description (特殊：…). |
| `GS 0xe30c` | ultimate_names | high | investigation | COINIT: a 奥義's name. |
| `GS 0xe370` | ultimate_level_lines | high | investigation | COINIT: three lines of text for each of nine levels, at column 3 * level + line. |
| `GS 0xef28` | skill_names | high | investigation | SKINIT: a skill's name. |
| `GS 0xf310` | skill_lines | high | investigation | SKINIT: three lines of text; the first is the heading (【移動】　飛行). |
| `GS 0xfec8` | item_names | high | investigation | ITINIT: an item's name. |
| `GS 0x10698` | item_lines | high | investigation | ITINIT: three lines of text; the first is the heading (【重要】美來のハンカチ). |
| `GS 0x11e08` | condition_names | high | investigation | ILINIT: a condition's name (操血, 封印, 幻覚 …). |
| `GS 0x11e28` | area_names | high | investigation | AMINIT: an area's name (歪秤世界, 天慶市 …). |
| `GS 0x11f54` | area_point_names | high | investigation | MPINIT, for the area in play: a point's name (出現地点, 西蒼川商業区 …). |
| `GS 0x1201c` | deck_names | high | investigation | DEINIT: a deck's name. |
| `GS 0x12404` | battle_rule_lines | high | investigation | REINIT: six lines of text; the first is the heading ◆特殊勝敗条件・編成制限◆. |
| `GS 0x1268e` | opponent_names | high | investigation | ARINIT: the opponent's name. |
| `GS 0x12a7c` | race_names | high | investigation | Set by INIT2: the names of the seven races of card_race_flags, by bit; the eighth string is バトル. |
| `GS 0xd2` | — | med | auto-shape | string-table (written by DEBUGADV) |
| `GS 0xd3` | — | med | auto-shape | string-table (written by DEBUGADV) |
| `GS 0xde` | — | med | auto-shape | string-table (written by CONFIG2) |
| `GS 0xdf` | — | med | auto-shape | string-table (written by CONFIG2) |
| `GS 0xe0` | — | med | auto-shape | string-table (written by INITDECK) |
| `GS 0xe7` | — | med | auto-shape | string-table (written by CONFIG2) |
| `GS 0x1c4` | — | med | auto-shape | string-table (written by BUNKI) |
| `GS 0x2a1` | — | med | auto-shape | string-table (written by SAVE) |
| `GS 0x29b3` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29b4` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29b5` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29b6` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29b7` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29b8` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29b9` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29ba` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29bb` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29bc` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29bd` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29be` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29bf` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29c0` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29c1` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29c2` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29c3` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29c4` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29c5` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29c6` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29c7` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29c8` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29c9` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29ca` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29cb` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29cc` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29cd` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29ce` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29cf` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29d0` | — | med | auto-shape | string-table (written by MUINIT) |
| `0x29d1` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29d2` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29d3` | — | med | auto-shape | string-table (written by MUINIT) |
| `GS 0x29d4` | — | med | auto-shape | string-table (written by MUINIT) |
| `0x29e5` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x29e6` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x29e7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29e8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29e9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29ea` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29eb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29ec` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29ed` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29ee` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29ef` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29f0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29f1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29f2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29f3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29f4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29f5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29f6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29f7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29f8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29f9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29fa` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29fb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29fc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29fd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29fe` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x29ff` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a00` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a01` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a02` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2a03` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a04` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a05` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a06` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a07` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a08` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a09` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a0a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a0b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a0c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a0d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a0e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a0f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a10` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a11` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a12` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a13` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a14` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a15` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a16` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a17` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a18` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a19` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a1a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a1b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a1c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a1d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a1e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a1f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a20` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a21` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a22` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a23` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a24` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a25` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a26` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a49` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a4a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a4b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a4c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a4d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a4e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a4f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a50` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a51` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a52` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a53` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a54` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a55` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a56` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a57` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a58` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a59` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a5a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a5b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a5c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a5d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a5e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a5f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a60` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a61` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a62` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a63` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a64` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a65` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a66` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a67` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a68` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a69` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a6a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a6b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a6c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a6d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a6e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a6f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a70` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a71` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a72` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a73` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a74` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a75` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a76` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a77` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a78` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a79` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a7a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a7b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a7c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a7d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a7e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a7f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a80` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a81` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a82` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a83` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a84` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a86` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a87` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a88` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2a89` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2aad` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2aae` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2aaf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ab0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ab1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ab2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ab3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ab4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ab5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ab6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ab7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ab8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ab9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2aba` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2abb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2abc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2abd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2abe` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2abf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ac0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ac1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ac2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ac3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ac4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ac5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ac6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ac7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ac8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ac9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2aca` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2acb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2acc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2acd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ace` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2acf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ad0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ad1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ad2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ad3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ad4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ad5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ad6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ad7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ad8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ad9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ada` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2adb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2adc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2add` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ade` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2adf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ae0` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ae1` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ae2` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ae3` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ae4` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ae5` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ae6` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ae7` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ae8` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ae9` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2aea` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2aeb` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2aec` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2aed` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2aee` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2aef` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2af0` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2af1` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2af2` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2af3` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2af4` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2af5` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2af6` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2af7` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2af8` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2af9` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2afa` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2afb` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2afc` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2afd` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2afe` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b11` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b12` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b13` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b14` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b15` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b16` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b17` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b18` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b19` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b1a` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b1b` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b1c` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b1d` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b1e` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b1f` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b20` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b21` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b22` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b23` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b24` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b25` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b26` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b27` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b28` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b29` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b2a` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b2b` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b2c` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b2d` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b2e` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b2f` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b30` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b31` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b32` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b33` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b34` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b35` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b36` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b37` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b38` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2b43` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b44` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b45` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b46` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b47` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b48` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b49` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b4a` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b4b` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b4c` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b4d` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b4e` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b4f` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b50` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b51` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b52` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b53` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b54` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b55` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b56` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b57` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b58` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b59` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b61` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b62` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b63` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b75` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b76` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b77` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b89` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b8a` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b8b` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b8c` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b8d` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b8e` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b8f` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b90` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b91` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b92` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b93` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b94` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b95` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b96` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b97` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b98` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b99` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b9a` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b9b` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b9c` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b9d` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b9e` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2b9f` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ba0` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ba1` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ba2` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ba3` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ba4` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ba5` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ba6` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ba7` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ba8` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2ba9` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2baa` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bab` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bac` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bad` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bae` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2baf` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bb0` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bb1` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bb2` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bb3` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bb4` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bb5` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bb6` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bb7` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bd9` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bda` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bdb` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bdc` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bdd` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bde` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bdf` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2be3` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2be4` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2be5` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2be6` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2be7` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2be8` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2be9` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bea` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2beb` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bec` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bed` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bee` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bef` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bf0` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bf1` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bf2` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bf3` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bf4` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bf5` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x2bf6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bf7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bf8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bf9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bfa` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bfb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bfc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bfd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bfe` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2bff` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2c00` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2c01` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2c02` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2c03` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2c04` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2c05` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dcd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dce` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dcf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dd0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dd1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dd2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dd3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dd4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dd5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dd6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dd7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dd8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dd9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dda` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ddb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ddc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ddd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dde` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ddf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2de0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2de1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2de2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2de3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2de4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2de5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2de6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2de7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2de8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2de9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dea` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2deb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dec` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ded` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dee` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2def` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2df0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2df1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2df2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2df3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2df4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2df5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2df6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2df7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2df8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2df9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dfa` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dfb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dfc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dfd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dfe` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2dff` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e00` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e01` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e02` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e03` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e04` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e05` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e06` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e07` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e08` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e09` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e0a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e0b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e0c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e0d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e0e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e31` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e32` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e33` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e34` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e35` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e36` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e37` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e38` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e39` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e3a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e3b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e3c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e3d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e3e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e3f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e40` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e41` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e42` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e43` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e44` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e45` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e46` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e47` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e48` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e49` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e4a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e4b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e4c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e4d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e4e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e4f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e50` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e51` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e52` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e53` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e54` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e55` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e56` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e57` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e58` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e59` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e5a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e5b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e5c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e5d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e5e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e5f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e60` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e61` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e62` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e63` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e64` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e65` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e66` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e67` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e68` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e69` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e6a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e6b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e6c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e6e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e6f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e70` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e71` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e95` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e96` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e97` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e98` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e99` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e9a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e9b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e9c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e9d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e9e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2e9f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ea0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ea1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ea2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ea3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ea4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ea5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ea6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ea7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ea8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ea9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eaa` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eab` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eac` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ead` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eae` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eaf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eb0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eb1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eb2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eb3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eb4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eb5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eb6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eb7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eb8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eb9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eba` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ebb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ebc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ebd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ebe` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ebf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ec0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ec1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ec2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ec3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ec4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ec5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ec6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ec7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ec8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ec9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eca` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ecb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ecc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ecd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ece` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ecf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ed0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ed1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ed2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ed3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ed4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ed5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ed6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ed7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ed8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ed9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eda` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2edb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2edc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2edd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ede` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2edf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ee0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ee1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ee2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ee3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ee4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ee5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ee6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2ef9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2efa` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2efb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2efc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2efd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2efe` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2eff` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f00` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f01` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f02` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f03` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f04` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f05` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f06` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f07` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f08` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f09` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f0a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f0b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f0c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f0d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f0e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f0f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f10` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f11` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f12` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f13` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f14` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f15` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f16` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f17` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f18` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f19` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f1a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f1b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f1c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f1d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f1e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f1f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f20` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f2b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f2c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f2d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f2e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f2f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f30` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f31` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f32` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f33` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f34` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f35` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f36` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f37` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f38` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f39` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f3a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f3b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f3c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f3d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f3e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f3f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f40` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f41` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f49` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f4a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f4b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f5d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f5e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f5f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f71` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f72` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f73` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f74` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f75` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f76` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f77` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f78` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f79` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f7a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f7b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f7c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f7d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f7e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f7f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f80` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f81` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f82` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f83` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f84` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f85` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f86` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f87` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f88` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f89` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f8a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f8b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f8c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f8d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f8e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f8f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f90` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f91` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f92` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f93` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f94` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f95` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f96` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f97` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f98` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f99` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f9a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f9b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f9c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f9d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f9e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2f9f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fc1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fc2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fc3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fc4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fc5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fc6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fc7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fcb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fcc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fcd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fce` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fcf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fd0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fd1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fd2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fd3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fd4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fd5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fd6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fd7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fd8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fd9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fda` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fdb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fdc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fdd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fde` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fdf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fe0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fe1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fe2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fe3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fe4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fe5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fe6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fe7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fe8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fe9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fea` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2feb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fec` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x2fed` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31b8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31b9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31ba` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31bc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31bd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31be` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31c0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31c1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31c2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31c4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31c5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31c6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31c7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31c8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31c9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31ca` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31cc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31cd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31ce` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31d0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31d1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31d2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31d3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31d4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31d5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31d6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31d7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31d8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31d9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31da` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31dc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31dd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31de` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31df` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31e0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31e1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31e2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31e4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31e5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31e6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31e8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31e9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31ea` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31ec` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31ed` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31ee` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31f0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31f1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31f2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31f4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31f5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31f6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31f7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31f8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31f9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31fa` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31fb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31fc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31fd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31fe` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x31ff` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3200` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3201` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3202` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3204` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3205` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3206` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3208` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3209` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x320c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x320d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x320e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3210` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3211` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3212` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3213` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3214` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3215` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3216` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3218` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3219` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x321a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x321c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x321d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x321e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3220` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3221` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3222` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3224` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3225` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3228` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3229` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x322a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x322c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x322d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3230` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3231` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3232` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3234` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3235` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3236` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3238` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3239` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x323a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x323c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x323d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x323e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3240` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3241` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3244` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3245` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3246` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3248` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3249` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x324c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x324d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x324e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x324f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3250` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3251` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3254` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3255` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3258` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3259` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x325c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x325d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3260` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3261` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3264` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3265` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3268` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3269` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x326c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x326d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3270` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3271` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3272` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3273` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3274` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3275` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3278` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3279` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x327a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x327c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x327d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x327e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3280` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3281` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3284` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3285` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3288` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3289` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x328c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x328d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x328e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3290` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3291` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3292` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3294` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3295` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3298` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3299` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x329a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x329c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x329d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x329e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x329f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32a0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32a1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32a2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32a4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32a5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32a6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32a8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32a9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32ac` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32ad` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32b0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32b1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32b2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32b4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32b5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32b6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32b8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32b9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32bc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32bd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x32be` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3348` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3349` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x334b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x334c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x334d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x334f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3350` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3351` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3353` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3354` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3355` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3357` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3358` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3359` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x335b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x335c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x335d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x335f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3360` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3361` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3363` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3364` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3365` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3367` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3368` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3369` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x336b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x336c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x336d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x336f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3370` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3371` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3372` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3374` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3375` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3377` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3378` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3379` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x337b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x337c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x337d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x337e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x337f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3380` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3381` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3384` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3385` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3387` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3388` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3389` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x338b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x338c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x338d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3390` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3391` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3394` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3395` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3396` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3397` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3398` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3399` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x339c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x339d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x339f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33a0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33a1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33a3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33a4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33a5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33a8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33a9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33ac` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33ad` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33b0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33b1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33b3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33b4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33b5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33b7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33b8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33b9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33bb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33bc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33bd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33bf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33c0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33c1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33c2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33c4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33c5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33c7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33c8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33c9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33cc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33cd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33d0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33d1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33d3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33d4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33d5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33d7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33d8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33d9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33db` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33dc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33dd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33df` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33e0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33e1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33e3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33e4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33e5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33e7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33e8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33e9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33ea` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33ec` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33ed` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33ef` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33f0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33f1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33f3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33f4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33f5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33f7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33f8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33f9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33fb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33fc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33fd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33fe` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x33ff` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3400` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3401` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3402` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3404` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3405` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3406` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3408` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3409` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x340c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x340d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3410` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3411` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3412` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3413` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3414` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3415` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3416` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3417` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3418` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3419` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x341b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x341c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x341d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x341e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3420` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3421` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3423` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3424` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3425` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3427` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3428` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3429` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x342b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x342c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x342d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x342f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3430` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3431` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3434` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3435` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3436` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x343c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x343d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x343f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3440` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3441` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3442` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3444` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3445` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3447` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3448` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3449` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x344a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34d8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34d9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34da` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34dc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34dd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34de` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34e0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34e1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34e4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34e5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34e6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34e7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34e8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34e9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34ea` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34ec` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34ed` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34f0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34f1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34f4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34f5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34f8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34f9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34fa` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34fc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x34fd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3500` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3501` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3504` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3505` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3508` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3509` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x350a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x350c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x350d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3510` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3511` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3514` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3515` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3516` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3518` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3519` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x351c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x351d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3520` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3521` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3524` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3525` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3528` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3529` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x352c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x352d` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3530` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3531` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3532` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3534` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3535` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3538` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3539` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x353a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x353c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x353d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x353e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x353f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3540` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3541` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3542` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3543` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3544` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3545` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3546` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3548` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3549` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x354a` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x354c` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x354d` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3550` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3551` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3554` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3555` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3556` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3558` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3559` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x355a` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x355b` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x355c` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x355d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3560` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3561` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3564` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3565` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3568` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3569` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x356c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x356d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3570` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3571` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3572` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3574` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3575` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3578` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3579` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x357a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x357c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x357d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x357e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3580` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3581` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3582` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3584` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3585` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3588` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3589` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x358a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x358b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x358c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x358d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3590` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3591` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3592` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3594` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3595` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3596` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3598` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3599` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x359c` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x359d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35a0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35a1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35a2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35a4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35a5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35a6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35a8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35a9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35ac` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35ad` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35b0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35b1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35b2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35b4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35b5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35b8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35b9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35bc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35bd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35be` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x35c0` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x35c1` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x35c2` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x35c3` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x35c4` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x35c5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35c8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35c9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35cc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35cd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35d0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35d1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35d4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35d5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35d8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35d9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35dc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35dd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35de` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35e0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35e1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35e2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35e4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35e5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35e6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35e8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35e9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35ec` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35ed` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35f0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35f1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35f4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35f5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35f8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x35f9` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x35fc` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x35fd` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x3600` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3601` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3602` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3604` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3605` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3608` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3609` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x360a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x360b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x360c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x360d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3610` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3611` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3614` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3615` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3618` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3619` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x361a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x361c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x361d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3668` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3669` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x366c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x366d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3670` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3671` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3672` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3674` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3675` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3676` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3677` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3678` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3679` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x367c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x367d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x367e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3680` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3681` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3682` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3684` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3685` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3686` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3688` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3689` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x368a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x368c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x368d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3690` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3691` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3692` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3694` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3695` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3698` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3699` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x369a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x369b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x369c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x369d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36a0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36a1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36a4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36a5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36a6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36a8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36a9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36ac` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36ad` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36ae` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36af` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36b0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36b1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36b2` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36b4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36b5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36b6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36b7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36b8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36b9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36bc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36bd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36c0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36c1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36c4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36c5` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36c8` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36c9` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36ca` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36cc` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36ce` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36cf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36d0` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36d1` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36d4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36d5` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36d6` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36d8` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36d9` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36da` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36dc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36dd` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36e0` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36e1` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36e4` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36e5` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36e6` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36e8` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36e9` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36ea` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36ec` | — | med | auto-shape | string-table (written by FAINIT) |
| `0x36ed` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36f0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36f1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36f4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36f5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36f6` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36f8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36f9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36fc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x36fd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3700` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3701` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3702` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3703` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3704` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3705` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3730` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3731` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3734` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3735` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3738` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3739` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x373c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x373d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3740` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3741` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3744` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3745` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3748` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3749` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x374c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x374d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3750` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3751` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3754` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3755` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3758` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3759` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x375c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x375d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3760` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3761` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3764` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3765` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3768` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3769` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x376c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x376d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3770` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3771` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3774` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3775` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3778` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3779` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x377c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x377d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3780` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3781` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3784` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3785` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3788` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3789` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x37a8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x37ac` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x37b0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x37f8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x37f9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x37fc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x37fd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x37ff` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3800` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3801` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3848` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3849` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x384b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x384c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x384d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x384f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3850` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3851` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3853` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3854` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3855` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3857` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3858` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3859` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x385c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x385d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x385f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3860` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3861` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3863` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3864` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3865` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3868` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3869` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x386b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x386c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x386d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x386f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3870` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3871` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3873` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3874` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3875` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3877` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3878` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3879` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x387a` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x387b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x387c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x387d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x387e` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3880` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3881` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3884` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3885` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3886` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3887` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3888` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3889` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x388b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x388c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x388d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3890` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3891` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3894` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3895` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3897` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3898` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3899` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x389b` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x389c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x389d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x389f` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38a0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38a1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38a3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38a4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38a5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38a7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38a8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38a9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38ac` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38ad` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38af` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38b0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38b1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38b3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38b4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38b5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38b8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38b9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38bc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38bd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38bf` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38c0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38c1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38c3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38c4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38c5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38c7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38c8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38c9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38cb` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38cc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38cd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38d0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38d1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38d3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38d4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38d5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38d7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38d8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38d9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38db` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38dc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38dd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38e0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38e1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38e4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38e5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38e7` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38e8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38e9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38ec` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38ed` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38f0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38f1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38f3` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38f4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38f5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38f8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38f9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38fc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38fd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x38fe` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3900` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3901` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3902` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3903` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3988` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3989` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x398c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x398d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3990` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3991` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3994` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3995` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3998` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3999` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x399c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x399d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39a0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39a1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39b0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39b1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39b4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39b5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39b8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39b9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39bc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39bd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39c0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39c1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39c4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39c5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39c8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39c9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39cc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39cd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39d0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39d1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39d4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39d5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39d8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39d9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39dc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39dd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39e0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39e1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39e4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39e5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39e8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39e9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39ec` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39ed` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39f0` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39f1` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39f4` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39f5` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39f8` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39f9` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39fc` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x39fd` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a00` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a01` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a04` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a05` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a08` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a09` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a0c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a0d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a10` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a11` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a14` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a15` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a18` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a19` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a1c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a1d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a20` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a21` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a24` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a25` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a28` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a29` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a2c` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a2d` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a30` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a31` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a34` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a35` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a38` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x3a39` | — | med | auto-shape | string-table (written by FAINIT) |
| `GS 0x415a` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x415b` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4160` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4161` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4166` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4167` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x416c` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x416d` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4172` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4173` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4178` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x417e` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4184` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x418a` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4190` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4196` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x419c` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x419d` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x419e` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41a2` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41a3` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41a4` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41a5` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41a8` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41ae` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41af` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41b4` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41b5` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41b6` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41b7` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41b8` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41ba` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41bb` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41bc` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41bd` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41be` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41bf` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41c0` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41c1` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41c2` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41c6` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41c7` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41cc` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41d2` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41d3` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41d4` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41d8` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x41d9` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x420e` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4214` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x421a` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4220` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4280` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4281` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4286` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4287` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x428c` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x428d` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4292` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4293` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4298` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4299` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x429a` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x429b` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x429e` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42a4` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42aa` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42ab` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42bc` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42c2` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42c8` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42c9` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42ca` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42ce` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42d0` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42d4` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42da` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42db` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42e0` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42e1` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42e6` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42e7` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42ec` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x42f2` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43ac` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43b2` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43b8` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43b9` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43ba` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43be` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43c4` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43ca` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43d0` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43d6` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43dc` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43e2` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43e8` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43ee` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43f4` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x43fa` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4400` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4604` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x460a` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4610` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4616` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x461c` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4622` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4628` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x462e` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4634` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4635` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x463a` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4640` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4641` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4646` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4647` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4648` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4649` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x464a` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x464b` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x464c` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4652` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4658` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x465e` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x465f` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4660` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4661` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4662` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4664` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x466a` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4730` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4736` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x473c` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x473d` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x4742` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x58be` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x58bf` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x58c7` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58c8` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58ca` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58cb` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58cc` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58cd` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58ce` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58cf` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58d0` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58d1` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58d2` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58d3` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58d4` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58d5` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58d6` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58d7` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58d8` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58d9` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58da` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58db` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58dc` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58dd` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58de` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58df` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58e0` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58e1` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58e2` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58e3` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58e5` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58e6` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58e8` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58e9` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58ea` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58eb` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58ec` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58ed` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58ee` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58ef` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58f0` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58f1` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58f2` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58f4` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58f5` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58f7` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58f8` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58fa` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58fb` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58fd` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58fe` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x58ff` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5900` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5901` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5902` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5903` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5904` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5905` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5906` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5907` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5908` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5909` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x590a` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x590c` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x590d` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x590e` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x590f` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5910` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5911` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5912` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5913` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5915` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5916` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5918` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x5919` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x591b` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x591c` | — | med | auto-shape | string-table (written by CVINIT) |
| `GS 0x647d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x647e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x647f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6480` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6481` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6482` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6483` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6484` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6485` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6486` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6487` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6488` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6489` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x648a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x648b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x648c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x648d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x648e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x648f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6490` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6491` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6492` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6493` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6494` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6495` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6496` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6497` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6498` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6499` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x649a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x649b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x649c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x649d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x649e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x649f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64a0` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64a1` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64a2` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64a3` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64a4` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64a5` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64a6` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64a7` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64a8` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64a9` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64aa` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64ab` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64ac` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64ad` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64ae` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64af` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64b0` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64b1` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64b2` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64b3` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64b4` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64b5` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64b6` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64b7` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64b8` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64b9` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64ba` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64bb` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64bc` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64bd` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64be` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64bf` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64c0` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64c1` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64c2` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64c3` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64c4` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64c5` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64c6` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64c7` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64c8` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64c9` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64ca` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64cb` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64cc` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64cd` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64ce` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64cf` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64d0` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64d1` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64d2` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64d3` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64d4` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64d5` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64d6` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64d7` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64d8` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64d9` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64da` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64db` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x64dc` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6545` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6546` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6547` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6548` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6549` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x654a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x654b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x654c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x654d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x654e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x654f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6550` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6551` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6552` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6553` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6554` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6555` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6556` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6557` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6558` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6559` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x655a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x655b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x655c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x655d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x655e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x655f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6560` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x660d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x660e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x660f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6610` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6611` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6612` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6613` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6614` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6615` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6616` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6617` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6618` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6619` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x661a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x661b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x661c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x661d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x661e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x661f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6620` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6621` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6622` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6623` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6624` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6625` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6626` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6627` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6628` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6629` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x662a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x662b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x662c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x662d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x662e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x662f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6630` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6631` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6632` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6633` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6634` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6635` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6636` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6637` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6638` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6639` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x663a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x663b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x663c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x663d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x663e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x663f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6640` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6641` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6642` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6643` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6644` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6645` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6646` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6647` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6648` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6649` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x664a` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x664b` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x664c` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x664d` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x664e` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x664f` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6650` | — | med | auto-shape | string-table (written by VIINIT) |
| `GS 0x6865` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6866` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6867` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6868` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6869` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x686a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x686b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x686c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x686d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x686e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x686f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6870` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6871` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6872` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6873` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6874` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6875` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6876` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6877` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6878` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6879` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x687a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x687b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x687c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x687d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x687e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x687f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6880` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6881` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6882` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6883` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6884` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x693c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x693d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x693e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x693f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6940` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6941` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6942` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6943` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6944` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6945` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6946` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6947` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6948` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6949` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x694a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x694b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x694c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x694d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x694e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x694f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6950` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6951` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6952` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6953` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6954` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6955` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6956` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6957` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6958` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6959` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x695a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x695b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x695c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x695d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x695e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x695f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6960` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6961` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6962` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6963` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6964` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6965` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6966` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6967` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6968` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6969` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x696a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x696b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x696c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x696d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x696e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x696f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6970` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6971` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6972` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6973` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6974` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6975` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6976` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6977` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6978` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6979` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x697a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x697b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x697c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x697d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x697e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x697f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6980` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6981` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6982` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6983` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6984` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6985` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6986` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6987` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6988` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6989` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x698a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x698b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x698c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x698d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x698e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x698f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6990` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6991` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6992` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6993` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6994` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6995` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6996` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6997` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6998` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6999` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x699a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x699b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x699c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x699d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x699e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x699f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69a0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69a1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69a2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69a3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69a4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69a5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69a6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69a7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69a8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69a9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69aa` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ab` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ac` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ad` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ae` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69af` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69b0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69b1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69b2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69b3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69b4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69b5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69b6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69b7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69b8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69b9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ba` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69bb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69bc` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69bd` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69be` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69bf` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69c0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69c1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69c2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69c3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69c4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69c5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69c6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69c7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69c8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69c9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ca` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69cb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69cc` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69cd` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ce` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69cf` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69d0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69d1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69d2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69d3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69d4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69d5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69d6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69d7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69d8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69d9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69da` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69db` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69dc` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69dd` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69de` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69df` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69e0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69e1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69e2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69e3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69e4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69e5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69e6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69e7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69e8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69e9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ea` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69eb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ec` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ed` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ee` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ef` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69f0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69f1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69f2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69f3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69f4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69f5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69f6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69f7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69f8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69f9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69fa` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69fb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69fc` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69fd` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69fe` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x69ff` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a00` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a01` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a02` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a03` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a04` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a05` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a06` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a07` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a08` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a09` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a0a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a0b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a0c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a0d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a0e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a0f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a10` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a11` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a12` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a13` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a14` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a15` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a16` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a17` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a18` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a19` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a1a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a1b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a1c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a1d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a1e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a1f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a20` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a21` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a22` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a23` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a24` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a25` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a26` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a27` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a28` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a29` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a2a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a2b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a2c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a2d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a2e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a2f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a30` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a31` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a32` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a33` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a34` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a35` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a36` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a37` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a38` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a39` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a3a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a3b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a3c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a3d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a3e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a3f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a40` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a41` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a42` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a43` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a44` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a45` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a46` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a47` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a48` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a49` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a4a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a4b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a4c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a4d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a4e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a4f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a50` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a51` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a52` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a53` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a54` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a55` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a56` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a57` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a58` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a59` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a5a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a5b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a5c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a5d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a5e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a5f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a60` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a61` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a62` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a63` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a64` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a65` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a66` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a67` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a68` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a69` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a6a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a6b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a6c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a6d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a6e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a6f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a70` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a71` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a72` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a73` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a74` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a75` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a76` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a77` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a78` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a79` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a7a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a7b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a7c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a7d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a7e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a7f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a80` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a81` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a82` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a83` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a84` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a85` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a86` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a87` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a88` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a89` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a8a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a8b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a8c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a8d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a8e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a8f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a90` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a91` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a92` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a93` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a94` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a95` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a96` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a97` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a98` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a99` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a9a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a9b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a9c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a9d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a9e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6a9f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aa0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aa1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aa2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aa3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aa4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aa5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aa6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aa7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aa8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aa9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aaa` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aab` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aac` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aad` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aae` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aaf` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ab0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ab1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ab2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ab3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ab4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ab5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ab6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ab7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ab8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ab9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aba` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6abb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6abc` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6abd` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6abe` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6abf` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ac0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ac1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ac2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ac3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ac4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ac5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ac6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ac7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ac8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ac9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aca` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6acb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6acc` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6acd` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ace` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6acf` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ad0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ad1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ad2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ad3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ad4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ad5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ad6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ad7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ad8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ad9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ada` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6adb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6adc` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6add` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ade` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6adf` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ae0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ae1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ae2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ae3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ae4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ae5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ae6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ae7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ae8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6ae9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aea` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aeb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aec` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aed` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aee` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aef` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6af0` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6af1` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6af2` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6af3` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6af4` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6af5` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6af6` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6af7` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6af8` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6af9` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6afa` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6afb` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6afc` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6afd` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6afe` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6aff` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b00` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b01` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b02` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b03` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b04` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b05` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b06` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b07` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b08` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b09` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b0a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b0b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b0c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b0d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b0e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b0f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b10` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b11` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b12` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b13` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b14` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b15` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b16` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b17` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b18` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b19` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b1a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b1b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b1c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b1d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b1e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b1f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b20` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b21` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b22` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b23` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b24` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b25` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b26` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b27` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b28` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b29` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b2a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b2b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b2c` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b2d` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b2e` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b2f` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b30` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b31` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b32` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b33` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b34` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b35` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b36` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b37` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b38` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b39` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b3a` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x6b3b` | — | med | auto-shape | string-table (written by CIINIT) |
| `GS 0x75ad` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75ae` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75af` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75b0` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75b1` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75b2` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75b3` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75b4` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75b5` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75b6` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75b7` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75b8` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75b9` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75ba` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75bb` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75bc` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75bd` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75be` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75bf` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75c0` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75c1` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75c2` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75c3` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75c4` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75c5` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75c6` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75c7` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75c8` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75c9` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75ca` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75cb` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75cc` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75cd` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75ce` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75cf` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75d0` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75d1` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75d2` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75d3` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75d4` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75d5` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75d6` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75d7` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75d8` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75d9` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75da` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75db` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75dc` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75dd` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75de` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75df` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75e0` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75e1` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75e2` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75e3` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75e4` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75e5` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75e6` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75e7` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75e8` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75e9` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75ea` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75eb` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75ec` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75ed` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75ee` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75ef` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75f0` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75f1` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75f2` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75f3` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75f4` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75f5` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75f6` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x75f7` | — | med | auto-shape | string-table (written by TIINIT) |
| `GS 0x7995` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x7996` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x7997` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x7998` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x7999` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x799a` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x799b` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x799c` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x799d` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x799e` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79a0` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79a1` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79a2` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79a3` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79a6` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79a7` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79a8` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79a9` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79aa` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79b3` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79b4` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79b5` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x79b6` | — | med | auto-shape | string-table (written by CHINIT) |
| `GS 0x7d9b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7d9c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7d9d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7d9e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7d9f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7da0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7da1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7da2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7da3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7da4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7da5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7da6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7da7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7da8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7db9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dbb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dbc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dbd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dbe` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dbf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dc0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dc1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dc2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dc3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dc6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dc7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dd7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dd8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dd9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dda` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ddb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ddc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ddd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dde` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ddf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7de0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7de1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7de2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7de3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7de4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7df5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7df6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7df7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7df8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7df9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dfa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dfb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dfc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dfd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dfe` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7dff` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e13` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e14` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e15` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e16` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e17` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e18` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e19` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e1a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e1b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e1c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e1d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e1e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e1f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e31` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e32` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e33` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e34` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e35` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e36` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e37` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e38` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e39` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e3a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e3b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e3c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e3d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e3e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e3f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e4f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e50` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e51` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e52` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e53` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e54` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e55` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e56` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e57` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e58` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e59` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e5a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e6d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e6e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e6f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e70` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e71` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e72` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e73` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e74` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e75` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e76` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e77` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e78` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e8b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e8c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e8d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e8e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e8f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e90` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e91` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e92` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e93` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e94` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e95` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e96` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e97` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7e98` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ea9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7eaa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7eab` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7eac` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ead` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7eae` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7eaf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7eb0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7eb1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7eb2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7eb3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7eb4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7eb5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ec7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ec8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ec9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ee5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ee6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ee7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ee8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ee9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f03` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f04` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f05` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f06` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f07` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f08` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f09` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f21` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f22` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f3f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f40` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f41` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f5d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f5e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f5f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f60` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f7b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f7c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f7d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f7e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f7f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f80` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f81` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f82` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f83` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f99` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f9a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f9b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f9c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f9d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f9e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7f9f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7fb7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7fb8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7fb9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7fba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7fbb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7fbc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7fd5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7fd6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7fd7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7fd8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ff3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x7ff4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8011` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8012` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8013` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8014` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8015` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8016` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8017` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8018` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8019` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x802f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8030` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8031` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8032` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8033` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8034` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x804d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x804e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x811f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8120` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8121` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8122` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8123` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8124` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8125` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8126` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x824b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x824c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x824d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x824e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x824f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8250` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8251` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8252` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8253` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8254` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8255` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8256` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8257` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8258` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8259` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8269` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x826a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x826b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x826c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x826d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x826e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x826f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8287` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8288` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8289` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x828a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82a5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82a6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82a7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82a8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82a9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82aa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82c3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82c4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82c5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82c6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82c7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x82c8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8359` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x835a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x835b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x835c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x835d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x835e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x835f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8360` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8361` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8362` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8363` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8364` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8365` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8366` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8367` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8368` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8369` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8377` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8378` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8379` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x837a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x837b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x837c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x837d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x837e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x837f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8380` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8381` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8382` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8383` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8384` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8385` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8386` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8387` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8388` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8389` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x838a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8395` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8396` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8397` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8398` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8399` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x839a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x839b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x839c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x839d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x839e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x839f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83a0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83a1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83a2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83a3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83a4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83a5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83a6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83a7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83a8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83a9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83aa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83b3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83b4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83b5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83b6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83b7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83b8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83b9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x83ba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x86fb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x86fc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x86fd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x86fe` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x86ff` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8700` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8701` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8702` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8703` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8704` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8705` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8706` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8719` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x871a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x871b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x871c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x871d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x871e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x871f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8737` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8738` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8739` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x873a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x873b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8755` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8756` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8757` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8758` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8759` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x875a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8773` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8774` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8775` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8776` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8777` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8778` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8791` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8792` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8793` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8794` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87af` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87b0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87b1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87b2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87b3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87cd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87ce` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87cf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87d0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87d1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87d2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x87d3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8809` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x880a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x880b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x880c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x880d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x880e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x880f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8810` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8811` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8812` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8813` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8814` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8815` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8816` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8817` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8818` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8819` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x881a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x881b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x881c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8827` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8828` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8829` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x882a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x882b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x882c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x882d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x882e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x882f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8830` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8831` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8832` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8833` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8834` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8835` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8836` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8837` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8845` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8846` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8847` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8848` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8849` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x884a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x884b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x884c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x884d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x884e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x884f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8850` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8851` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8852` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8853` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8854` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8855` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8863` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8864` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8865` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8866` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8867` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8868` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8869` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x886a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8881` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8882` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8883` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8884` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8885` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8886` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8887` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8888` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8889` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x888a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x888b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x888c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x888d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x888e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x888f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8890` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bab` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bac` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bad` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bae` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8baf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bb0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bb1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bb2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bb3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bb4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bb5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bb6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bb7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bc9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bca` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bcb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bcc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bcd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bce` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bcf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bd0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bd1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bd2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bd3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bd4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bd5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bd6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bd7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bd8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bd9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bda` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8be7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8be8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8be9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bea` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8beb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bec` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bee` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bef` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bf0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bf1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bf2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bf4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bf5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bf6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bf7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8bf8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c05` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c06` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c07` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c08` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c09` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c0a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c0b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c0c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c0d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c0e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c0f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c10` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c11` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c12` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c13` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c14` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c15` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c16` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c17` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c18` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c19` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c1a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c1b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c23` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c24` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c25` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c26` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c27` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c28` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c29` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c2a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c2b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c2c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c2d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c2e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c2f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c30` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c31` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c32` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c33` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c34` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c35` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c41` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c42` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c43` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c44` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c45` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c46` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c47` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c48` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c49` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c4a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c4b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c4c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c4d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c4e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c4f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c50` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c51` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c52` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c5f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c60` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c61` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c62` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c63` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c64` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c65` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c66` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c67` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c68` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c69` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c6a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c6b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8c6c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f2f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f30` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f31` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f32` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f33` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f34` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f35` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f36` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f37` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f38` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f39` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f3a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f3b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f3c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f3d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f3e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f3f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f40` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f41` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f42` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f43` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f44` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f4d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f4e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f4f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f50` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f51` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f52` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f53` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f54` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f55` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f56` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f57` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f58` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f59` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f5a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f5b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f5c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f5d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f5e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f5f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f60` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f61` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x8f62` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x905b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x905c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x905d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x905e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x905f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9060` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9061` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9062` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9063` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9064` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9065` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9066` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9067` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9068` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9069` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x906a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x906b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x906c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x906d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x906e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x906f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9079` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x907a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x907b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x907c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x907d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x907e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x907f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9080` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9081` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9082` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9083` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9084` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9097` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9098` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9099` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x909a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x909b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x909c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x909d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x909e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x909f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90a0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90a1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90a2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90a3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90a4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90a5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90a6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90a7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90a8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90a9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90b5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90b6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90b7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90b8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90b9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90ba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90bb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90bc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90bd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90be` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90bf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90c0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90c1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90c2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90c3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90c4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90c5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90c6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90d3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90d4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90d5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90d6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90d7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90d8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90d9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90da` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90db` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90dc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90dd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90de` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90df` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90e0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90e1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90f1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90f2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90f3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90f4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90f5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90f6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90f7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90f8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90f9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90fa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90fb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90fc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90fd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90fe` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x90ff` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9100` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9101` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9102` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9103` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9104` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9105` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x910f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9110` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9111` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9112` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9113` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9114` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9115` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9116` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9117` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9118` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9119` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x93c1` | — | med | auto-shape | string-table (written by DEBUGBTL) |
| `GS 0x93df` | — | med | auto-shape | string-table (written by DEBUGBTL) |
| `GS 0x93fd` | — | med | auto-shape | string-table (written by DEBUGBTL) |
| `GS 0x9549` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x954a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x954c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x954d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x954f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9550` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9552` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9553` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9555` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9556` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9558` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9559` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x955b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x955c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x955e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x955f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9561` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9562` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9564` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9565` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9567` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9568` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x956a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x956b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x956d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9570` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9571` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95a3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95a4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95a6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95a7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95a9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95aa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95ac` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95ad` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95af` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95b0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95b2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95b5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95b8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95bb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95be` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95bf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95c1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95c2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95ca` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95cb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95cd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95ce` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95fd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x95fe` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9600` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9601` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9603` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9604` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9606` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9607` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9609` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x960a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x960c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x960d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x960f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9610` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9612` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9613` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9615` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9616` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9618` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9619` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x961b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x961c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x961e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x961f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9624` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9625` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9657` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9658` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x965a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x965b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x965d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x965e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9660` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9661` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9663` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9664` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9666` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9667` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9669` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x966a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x966c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x966d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x966f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9670` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9672` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9673` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9675` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9676` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96b1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96b2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96b4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96b5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96b7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96b8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96ba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96bb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96bd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96be` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96c0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96c1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96c3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96c4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96c6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96c7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96c9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96ca` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96cc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96cd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96cf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96d0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96d2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96d3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x96d5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x970b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x970c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x970e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9711` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9712` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9714` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9717` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9718` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x971a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x971b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x971d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x971e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9720` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9721` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9723` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9724` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9726` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9727` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9729` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x972a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x972c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x972d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x972f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9730` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9732` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9733` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9735` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9736` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9765` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9766` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9768` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9769` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x976b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x976c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x976e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x976f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9771` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9772` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9774` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9775` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9777` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9778` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x977a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x977b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x977d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x977e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9780` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9781` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9783` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9786` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9787` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97bf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97c0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97c2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97c3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97c5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97c6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97c8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97c9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97cb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97cc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97ce` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97d1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97d2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97d4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97d5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97d7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97d8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97da` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97db` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97dd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97de` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97e0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x97e1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9819` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x981a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x981c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x981d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x981f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9820` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9822` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9823` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9825` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9826` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9828` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9829` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x982b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x982c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x982e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x982f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9831` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9832` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9834` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9835` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9837` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9838` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x983a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x983b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x983d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x983e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9840` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9841` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9873` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9876` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9877` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9879` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x987a` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x987f` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9882` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9885` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9886` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9888` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9889` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x988b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x988c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x988e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x988f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9891` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9892` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9894` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9895` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9897` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9898` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x98cd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x98ce` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x98d0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x98d1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x98d3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x98d4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9927` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9928` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x992a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x992d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9930` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9931` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9933` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9934` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9981` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9982` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9984` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9985` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9987` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9988` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x998a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x998b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x998d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x998e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9990` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9991` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9993` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9994` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x99db` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x99dc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x99de` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x99df` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a35` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a36` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a38` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a39` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a3b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a3c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a8f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a90` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a92` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a93` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a95` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a96` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9a98` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9ae9` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9aea` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9aec` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9aed` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9aef` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9af2` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9af5` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9af8` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9af9` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9afb` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9afc` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9afe` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9aff` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9b01` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9b02` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9b43` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9b44` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b46` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b47` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b49` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b4a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b4c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b4d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b4f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b52` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b53` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b55` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b56` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b9d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9b9e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9ba0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9ba1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9ba3` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9bac` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9bad` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9bf7` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9bf8` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9bfa` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9bfd` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9bfe` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9c00` | — | med | auto-shape | string-table (written by CDINIT) |
| `0x9c01` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9c51` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9c54` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9c55` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cab` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cac` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cae` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cb1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cb2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cb4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cb5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cb7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cb8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cbb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cbd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cc0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cc3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9cc4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d05` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d06` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d08` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d09` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d0b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d0c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d0e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d0f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d11` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d12` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d14` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d15` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d5f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d60` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9d62` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fd5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fd6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fd8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fd9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fdb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fdc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fde` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fdf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fe1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fe2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fe4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fe5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fe7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fe8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9fea` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0x9feb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa359` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa35a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa35c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa35d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa35f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa360` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa362` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa363` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa365` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa366` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa368` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa36b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa36c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa36e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa36f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa371` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa372` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa374` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa375` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa377` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa378` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa37a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa37b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa37d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa37e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa380` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa381` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa383` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa384` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa3b3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa3b4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa3b6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa3b7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa3b9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa3ba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa3bc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa3bf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa3c0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa3c2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa3c3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa40d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa410` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa411` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa413` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa414` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa416` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa417` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa467` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa468` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa46a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa46b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa46d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa470` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa471` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa473` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa474` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa476` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4c1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4c2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4c4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4c5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4c7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4c8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4ca` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4cb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4cd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4ce` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4d0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa4d1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa683` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa684` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa686` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa687` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa689` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa68a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa68c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa68d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa68f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa690` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa692` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa693` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa695` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa696` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa698` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa699` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa69b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa69c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa69e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa69f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6a1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6a4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6a5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6a7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6a8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6aa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6ab` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6ad` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6ae` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6b0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6b1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6b3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6b4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6dd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6de` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6e0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6e1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6e3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6e4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6e6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6e7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6e9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6ea` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6ec` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6ed` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6ef` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6f0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6f2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6f3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6f5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6f6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6f8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6f9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6fb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6fc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6fe` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa6ff` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa701` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa702` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa704` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa705` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa707` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa708` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa70a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa70b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa70d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa70e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa710` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa711` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa713` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa714` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa716` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa717` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa737` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa738` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa73a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa73b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa73d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa73e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa740` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa743` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa744` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa746` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa747` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa749` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa74a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa74c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa74f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa750` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa752` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa753` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa755` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa756` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa758` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa759` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa75b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa75c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa75e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa75f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa761` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa762` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa764` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa765` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa767` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa768` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa76a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa76b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa76d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa76e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa770` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa771` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa773` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa774` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa776` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa777` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa791` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa792` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa794` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa795` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa797` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa798` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa79a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa79b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa79d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa79e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa7a0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa7a3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa7a4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa7a6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xa7a7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb169` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb16a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb16c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb16d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb16f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb170` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb172` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb173` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb175` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb176` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb178` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb179` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb17b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb17c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb17e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb17f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb181` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb182` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb184` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb185` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb187` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb188` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb18a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb18b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb1c3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb1c4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb1c6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb1c7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb1c9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb1cc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb1cd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb1cf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb1d0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb1d2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb21d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb21e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb220` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb221` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb223` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb224` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb226` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb227` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb229` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb22a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb277` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb278` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb27a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb27b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb27d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb27e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb280` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb283` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb284` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb286` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb287` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb2d1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb2d2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb2d4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb2d5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb2d7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb2da` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb2db` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb2dd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb2de` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb2e0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb2e1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb32b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb32c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb32e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb32f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb331` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb332` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb334` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb335` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb385` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb386` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb388` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb389` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb38b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb38c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb38e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb38f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb391` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb392` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3df` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3e0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3e2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3e3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3e5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3e8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3e9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3eb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3ec` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3ee` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3ef` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3f1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb3f2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb493` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb494` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb496` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb499` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb49a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb49c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb49d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb49f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4a0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4a2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4a3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4a5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4a6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4a8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4a9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4ab` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4ac` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4ae` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4b1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4b2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4b4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4b5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4b7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4ba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4bb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4bd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4be` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4c0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4c1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4c3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4c6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4c7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4c9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4ca` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4cc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4cd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4ed` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4f0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4f3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4f4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4f6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4f7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4f9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4fa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4fc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4fd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb4ff` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb500` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb502` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb503` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb505` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb508` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb509` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb50b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb50c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb50e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb50f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb511` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb512` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb514` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb517` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb518` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb51a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb51b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb51d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb51e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb547` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb548` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb54a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb54b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb54d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb54e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb550` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb551` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb553` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb554` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb556` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb557` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb559` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb55a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb55c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb55f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb562` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb565` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb566` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb568` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb569` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb56b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb56c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb56e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb56f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb571` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb572` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb574` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb575` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb577` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb578` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5a1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5a2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5a4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5a5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5a7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5a8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5aa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5ab` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5ad` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5b0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5b1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5b3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5b4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5b6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5b7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5fb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5fc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb5fe` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb601` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb602` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb604` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb605` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb607` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb608` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb60a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb60d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb60e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb610` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb611` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb613` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb614` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb616` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb619` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb61a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb61c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb61d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb61f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb620` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb622` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb623` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb625` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb628` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xb629` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf79` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf7c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf7f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf82` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf83` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf85` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf86` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf88` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf89` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf8b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf8c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf8e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf91` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf94` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf95` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf97` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf98` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf9a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf9d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbf9e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfd3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfd6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfd7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfd9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfdc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfdf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfe2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfe5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfe6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfe8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfe9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfeb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfec` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbfee` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbff1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbff2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbff4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbff5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbff7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbff8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbffa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbffb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbffd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xbffe` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc000` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc001` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc003` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc004` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc006` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc007` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc02d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc02e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc030` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc031` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc033` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc034` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc036` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc037` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc039` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc03a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc03c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc03d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc042` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc045` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc046` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc048` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc049` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc04b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc04c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc04e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc054` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc055` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc057` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc058` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc05a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc05b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc05d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc060` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc061` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc087` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc08a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc08b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc08d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc08e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc090` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc091` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc093` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc094` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc096` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc097` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc099` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc09a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc09c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc09f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0a0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0a2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0a3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0a5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0a6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0a8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0a9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0ab` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0ac` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0ae` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0af` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0b1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0b2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0b4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0b7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0b8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0ba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0bb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0bd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0be` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0c0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0c3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0c4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0c6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0c7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0c9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0ca` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0e1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0e4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0e5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0e7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0e8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0e9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0ea` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0eb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0ed` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0ee` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0f0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0f1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0f3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0f4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0f6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0f9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0fa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0fc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0fd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc0ff` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc100` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc102` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc105` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc106` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc108` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc109` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc10b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc10c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc10e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc10f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc111` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc112` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc114` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc115` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc117` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc13b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc13e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc13f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc141` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc142` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc144` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc145` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc147` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc148` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc14a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc14b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc14d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc14e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc150` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc151` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc153` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc156` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc157` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc159` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc15c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc15d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc15f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc160` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc162` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc163` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc165` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc166` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc168` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc169` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc16b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc16c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc16e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc16f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc195` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc198` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc19b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc19c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc19e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1a1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1a2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1a4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1a5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1a7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1a8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1aa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1ad` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1ae` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1b0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1b1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1b3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1b4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1b6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1b7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1b9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1ba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1bc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xc1bd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca05` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca06` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca08` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca09` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca0b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca0c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca0e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca0f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca11` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca12` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca14` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca15` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca17` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca18` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca1a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca1b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca1d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca1e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca20` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca21` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca23` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca24` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca26` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca27` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca29` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca2a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca2c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca2d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca2f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca32` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca33` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca35` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca36` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca38` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca39` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca3b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca3c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca3e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca3f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca41` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca44` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca45` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca5f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca62` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca65` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca66` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca68` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca69` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca6b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca6c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca6e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca71` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca72` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca74` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca75` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca77` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca78` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca7a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca7b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca7d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca7e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca80` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca81` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca83` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca84` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca86` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca87` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca89` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca8c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca8d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca8f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca90` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca92` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca93` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca95` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca96` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca98` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca99` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca9b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca9c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca9e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xca9f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcd89` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcd8a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcd8c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcd8d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcd8f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcd90` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcd92` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcd95` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcd98` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcd9b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcd9e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcda1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcda4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcda5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcda7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcda8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdaa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdab` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdad` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdae` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdb0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdb1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdb3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdb4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdb6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdb7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdb9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdbc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdbf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdc0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdc2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdc5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdc6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcde3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcde4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcde6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcde7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcde9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdea` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdec` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdef` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdf0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdf2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdf5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdf6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdf8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdf9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdfb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdfc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcdfe` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce01` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce02` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce04` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce05` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce3d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce40` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce43` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce44` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce46` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce47` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce49` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce4a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce4c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce4d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce4f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce50` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce52` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce53` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce55` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce56` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce58` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce59` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce5b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce5c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce5e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce61` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce62` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce64` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce67` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce68` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce6a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce6d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce70` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce71` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce73` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce74` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce97` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce98` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce9a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce9d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xce9e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcea0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcea1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcea3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcea4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcea6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcea7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcea9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xceaa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xceac` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcead` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xceaf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xceb0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xceb2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xceb3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xceb5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xceb6` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xceb8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xceb9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcebb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcebc` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcebe` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcebf` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcec1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcec2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcec4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcec5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcec7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcec8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xceca` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcecb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcef1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcef2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcef4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcef5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcef7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcef8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcefa` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcefb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcefd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf00` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf01` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf03` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf06` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf07` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf09` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf0a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf0c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf0d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf0f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf10` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf12` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf13` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf15` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf16` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf18` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf19` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf1b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf1c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf4b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf4c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf4e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf4f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf51` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf52` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf54` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf55` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf57` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf58` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf5a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf5d` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf5e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf60` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf61` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf63` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf64` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf66` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf69` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf6a` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf6c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf6f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf70` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf72` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf73` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf75` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf78` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf79` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf7b` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf7c` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf7e` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf7f` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf81` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf82` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf84` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf85` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf87` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcf88` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfa5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfa8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfa9` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfab` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfae` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfb1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfb2` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfb4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfb5` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfb7` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfb8` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfba` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfbb` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfbd` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfc0` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfc1` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfc3` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xcfc4` | — | med | auto-shape | string-table (written by CDINIT) |
| `GS 0xdb3d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb3e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb3f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb40` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb41` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb42` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb43` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb44` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb45` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb46` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb47` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb48` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb49` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb4a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb4b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb4c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb4d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb4e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb4f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb50` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb51` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb52` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb53` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb54` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb55` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb56` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb57` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb58` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb59` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb5a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb5b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb5c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdb5d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdba1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdba2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdba3` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdba4` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdba5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdba6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdba7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdba8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdba9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdbaa` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdbab` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdbac` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc05` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc06` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc07` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc08` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc09` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc0a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc0b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc0c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc0d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc0e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc0f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc10` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc11` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc12` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc13` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc14` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc15` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc16` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc17` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc19` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc1a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc1b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc1c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc69` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc6a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc6b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc6c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc6d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc6e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc6f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc70` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc71` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc72` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc73` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc74` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc75` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc76` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc77` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc78` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc79` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc7a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc7b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc7c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc7d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc7e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc7f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc80` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc81` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc82` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc83` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc84` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc85` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc86` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc87` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc88` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc89` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc8a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc8b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc8c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc8d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc8e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc8f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc90` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc91` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc92` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc93` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc94` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc95` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc96` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc97` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc98` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc99` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc9a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc9b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc9c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc9d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc9e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdc9f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdca0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdca1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdca2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdca3` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdca4` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdca5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdca6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdca7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdca8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdca9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcaa` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcab` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcac` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcad` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcae` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcaf` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcb0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcb1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcb2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcb3` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcb4` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcb5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcb6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcb7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcb8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcb9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcba` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdccd` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcce` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdccf` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcd0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcd1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcd2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcd3` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcd4` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcd5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcd6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcd7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcd8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcd9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcda` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcdb` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcdc` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcdd` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcde` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcdf` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdce0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdce1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdce2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdce3` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdce4` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdce5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdce6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdce7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdce8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdce9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdceb` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcec` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdced` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcee` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcef` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcf0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcf1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcf2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcf3` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcf4` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcf5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcf6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcf7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcf8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcf9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdcfa` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd31` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd32` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd33` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd34` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd35` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd36` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd37` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd38` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd3a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd3b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd3c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd3d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd3e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd3f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd40` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd41` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd95` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd96` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd98` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd99` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd9a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd9b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd9c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd9d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd9e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdd9f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdda0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdda1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdda2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdda3` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdda4` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdda5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdda6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdda7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdda8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdda9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddaa` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddab` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddac` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddad` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddae` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddaf` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddb0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddf9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddfa` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddfb` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddfc` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddfd` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddfe` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xddff` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde00` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde01` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde02` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde03` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde04` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde05` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde06` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde07` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde08` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde5d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde5e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde5f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde60` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde61` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde63` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde64` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde65` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde66` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde67` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde68` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde69` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde6a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde6b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde6c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde6d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde6e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde6f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde71` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde72` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde73` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde74` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde75` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde76` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde77` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde78` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde79` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde7a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde7b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde7c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde7d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde7e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde7f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde80` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde81` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde82` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde83` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde84` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde85` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde86` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde87` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xde88` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf25` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf26` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf27` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf28` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf29` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf2a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf2b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf2c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf2d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf2e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf2f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf30` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf31` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf32` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf33` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf34` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf35` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf36` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf37` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf38` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf39` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf3a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf3b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf3c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf3d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf3e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf3f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf40` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf41` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf42` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf43` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf44` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf45` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf89` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf8a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf8b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf8c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf8d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf8e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf8f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf90` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf91` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf92` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf93` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdf94` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdfed` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdfee` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdfef` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdff0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdff1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdff2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdff3` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdff4` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdff5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdff6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdff7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdff8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdff9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdffa` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdffb` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdffc` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdffd` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdffe` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xdfff` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe001` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe002` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe003` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe004` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe051` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe052` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe053` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe054` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe055` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe056` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe057` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe058` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe059` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe05a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe05b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe05c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe05d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe05e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe05f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe060` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe061` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe062` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe063` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe064` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe065` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe066` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe067` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe068` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe069` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe06a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe06b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe06c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe06d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe06e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe06f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe070` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe071` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe072` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe073` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe074` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe075` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe076` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe077` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe078` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe079` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe07a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe07b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe07c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe07d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe07e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe07f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe080` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe081` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe082` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe083` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe084` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe085` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe086` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe087` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe088` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe089` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe08a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe08b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe08c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe08d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe08e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe08f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe090` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe091` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe092` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe093` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe094` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe095` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe096` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe097` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe098` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe099` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe09a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe09b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe09c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe09d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe09e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe09f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0a0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0a1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0a2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0b5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0b6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0b7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0b8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0b9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0ba` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0bb` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0bc` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0bd` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0be` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0bf` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0c0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0c1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0c2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0c3` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0c4` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0c5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0c6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0c7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0c8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0c9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0ca` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0cb` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0cc` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0cd` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0ce` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0cf` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0d0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0d1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0d3` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0d4` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0d5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0d6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0d7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0d8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0d9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0da` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0db` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0dc` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0dd` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0de` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0df` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0e0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0e1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe0e2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe119` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe11a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe11b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe11c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe11d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe11e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe11f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe120` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe122` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe123` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe124` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe125` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe126` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe127` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe128` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe129` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe17d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe17e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe180` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe181` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe182` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe183` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe184` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe185` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe186` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe187` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe188` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe189` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe18a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe18b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe18c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe18d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe18e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe18f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe190` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe191` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe192` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe193` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe194` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe195` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe196` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe197` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe198` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1e1` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1e2` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1e3` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1e4` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1e5` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1e6` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1e7` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1e8` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1e9` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1ea` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1eb` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1ec` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1ed` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1ee` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1ef` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe1f0` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe245` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe246` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe247` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe248` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe249` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe24b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe24c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe24d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe24e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe24f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe250` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe251` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe252` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe253` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe254` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe255` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe256` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe257` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe259` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe25a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe25b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe25c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe25d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe25e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe25f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe260` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe261` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe262` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe263` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe264` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe265` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe266` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe267` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe268` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe269` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe26a` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe26b` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe26c` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe26d` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe26e` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe26f` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe270` | — | med | auto-shape | string-table (written by TAINIT) |
| `GS 0xe30d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe30e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe30f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe310` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe311` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe312` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe313` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe314` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe315` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe316` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe317` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe318` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe319` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe31a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe31b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe31c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe31d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe31e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe31f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe320` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe321` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe322` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe323` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe324` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe325` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe326` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe327` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe391` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe392` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe394` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe395` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe397` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe398` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe39a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe39b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe39d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe39e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3a0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3a1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3a3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3a4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3a6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3a7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3a9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3aa` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3af` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3b0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3b1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3b2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3b3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3b4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3b5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3b6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3b7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3b8` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3b9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3ba` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3bb` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3bc` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3bd` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3be` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3bf` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3c0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3c1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3c2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3c3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3c4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3c5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3c6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3c7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3c8` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3c9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3cd` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3ce` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3cf` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3d0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3d1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3d2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3d3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3d4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3d5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3d6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3d7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3d8` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3d9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3da` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3db` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3dc` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3dd` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3de` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3df` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3e0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3e1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3e2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3e3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3e4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3e5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3e6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3e7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3eb` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3ec` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3ee` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3ef` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3f1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3f2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3f4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3f5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3f7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3f8` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3fa` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3fb` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3fd` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe3fe` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe400` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe401` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe403` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe404` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe409` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe40a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe40c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe40d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe40f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe410` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe412` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe413` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe415` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe416` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe418` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe419` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe41b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe41c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe41e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe41f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe421` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe422` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe427` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe428` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe42a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe42b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe42d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe42e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe430` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe431` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe433` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe434` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe436` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe437` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe439` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe43a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe43c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe43d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe43f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe440` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe445` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe446` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe448` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe449` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe44b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe44c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe44e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe44f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe451` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe452` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe454` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe455` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe457` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe458` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe45a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe45b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe45d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe45e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe463` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe464` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe466` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe467` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe469` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe46a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe46c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe46d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe46f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe470` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe472` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe473` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe475` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe476` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe478` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe479` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe47b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe47c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe481` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe482` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe484` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe485` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe487` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe488` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe48a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe48b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe48d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe48e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe490` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe491` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe493` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe494` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe496` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe497` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe499` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe49a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe49f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4a0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4a2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4a3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4a5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4a6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4a8` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4a9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4ab` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4ac` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4ae` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4af` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4b1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4b2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4b4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4b5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4b7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4b8` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4bd` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4be` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4c0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4c1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4c3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4c4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4c6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4c7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4c9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4ca` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4cc` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4cd` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4cf` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4d0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4d2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4d3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4d5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4d6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4db` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4dc` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4dd` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4de` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4df` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4e0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4e1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4e2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4e3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4e4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4e5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4e6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4e7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4e8` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4e9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4ea` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4eb` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4ec` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4ed` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4ee` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4ef` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4f0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4f1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4f2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4f3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4f4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4f5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4f9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4fa` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4fb` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4fc` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4fd` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4fe` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe4ff` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe500` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe501` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe502` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe503` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe504` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe505` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe506` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe507` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe508` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe509` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe50a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe50b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe50c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe50d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe50e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe50f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe510` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe511` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe512` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe513` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe517` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe518` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe519` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe51a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe51b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe51c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe51d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe51e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe51f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe520` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe521` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe522` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe523` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe524` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe525` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe526` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe527` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe528` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe529` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe52a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe52b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe52c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe52d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe52e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe52f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe530` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe531` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe535` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe536` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe537` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe538` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe539` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe53a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe53b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe53c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe53d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe53e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe53f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe540` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe541` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe542` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe543` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe544` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe545` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe546` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe547` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe548` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe549` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe54a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe54b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe54c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe54d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe54e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe54f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe553` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe554` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe555` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe556` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe557` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe558` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe559` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe55a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe55b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe55c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe55d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe55e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe55f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe560` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe561` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe562` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe563` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe564` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe565` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe566` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe567` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe568` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe569` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe56a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe56b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe56c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe56d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe571` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe572` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe573` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe574` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe575` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe576` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe577` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe578` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe579` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe57a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe57b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe57c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe57d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe57e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe57f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe580` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe581` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe582` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe583` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe584` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe585` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe586` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe587` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe588` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe589` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe58a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe58b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe58f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe590` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe591` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe592` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe593` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe594` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe595` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe596` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe597` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe598` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe599` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe59a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe59b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe59c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe59d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe59e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe59f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5a0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5a1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5a2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5a3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5a4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5a5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5a6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5a7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5a8` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5a9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5ad` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5ae` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5af` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5b0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5b1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5b2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5b3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5b4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5b5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5b6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5b7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5b8` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5b9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5ba` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5bb` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5bc` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5bd` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5be` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5bf` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5c0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5c1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5c2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5c3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5c4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5c5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5c6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5c7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5cb` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5cc` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5cd` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5ce` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5cf` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5d0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5d1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5d2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5d3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5d4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5d5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5d6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5d7` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5d8` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5d9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5da` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5db` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5dc` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5dd` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5de` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5df` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5e0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5e1` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5e2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5e3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5e4` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5e5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5e9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5ea` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5ec` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5ed` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5ef` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5f0` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5f2` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5f3` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5f5` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5f6` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5f8` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5f9` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5fb` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5fc` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5fe` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe5ff` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe601` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe602` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe607` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe608` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe609` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe60a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe60b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe60c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe60d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe60e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe60f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe610` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe611` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe612` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe613` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe614` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe615` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe616` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe617` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe618` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe619` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe61a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe61b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe61c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe61d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe61e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe61f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe620` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe621` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe625` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe626` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe627` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe628` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe629` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe62a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe62b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe62c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe62d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe62e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe62f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe630` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe631` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe632` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe633` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe634` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe635` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe636` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe637` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe638` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe639` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe63a` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe63b` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe63c` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe63d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe63e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe63f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe643` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe644` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe645` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe661` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe662` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe663` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe67f` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe680` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe681` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe69d` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xe69e` | — | med | auto-shape | string-table (written by COINIT) |
| `GS 0xef29` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef2a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef2b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef2c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef2d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef2e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef2f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef30` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef31` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef35` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef36` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef37` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef38` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef39` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef3a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef3b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef3c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef3f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef40` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef41` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef42` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef43` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef44` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef45` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef46` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef49` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef4a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef4b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef4e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef4f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef50` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef51` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef52` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef53` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef54` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef58` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef59` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef5a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef5b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef5c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef5d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef5e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef5f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef60` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef61` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef62` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef63` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef64` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef65` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef66` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef67` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef68` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef69` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef6a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef6b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef6c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef6d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef6e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef70` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef71` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef72` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef73` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef74` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef75` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef76` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef77` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef7a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef7b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef7c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef7d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef7e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef7f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef80` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef81` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef82` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef83` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef84` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef85` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef86` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef87` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef88` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef8c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef8d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef8e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef8f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef90` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef91` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef92` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef93` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef95` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef96` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef97` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef9c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef9d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xef9e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefa1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefa2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefa3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefa7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefb0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefb1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefb2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefb3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefb7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefb8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefb9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefba` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefbb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefbc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefc0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefc1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefc2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefc3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefc4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xefc5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf11d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf11e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf11f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf120` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf121` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf122` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf123` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf124` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf125` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf126` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf127` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf128` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf129` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf12a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf12b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf12c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf12d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf12e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf12f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf130` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf131` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf132` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf133` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf134` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf135` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf136` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf137` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf138` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf139` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf13a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf13b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf13c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf13d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf13e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf13f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf140` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf141` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf142` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf143` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf144` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf145` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf146` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf147` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf148` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf149` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf14a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf14b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf14c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf14d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf14e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf14f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf150` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf151` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf152` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf153` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf154` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf155` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf156` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf157` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf158` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf159` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf15a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf15b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf15c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf15d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf15e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf15f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf160` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf161` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf162` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf163` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf164` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf167` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf168` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf169` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf16a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf16b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf16e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf16f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf170` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf171` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf172` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf173` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf174` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf175` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf176` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf177` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf178` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf179` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf17a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf17b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf17c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf17d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf180` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf185` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf187` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf188` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf189` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf18a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf18b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf18c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf18d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf18e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf18f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf190` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf191` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf192` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf193` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf313` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf314` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf316` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf317` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf319` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf31a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf31c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf31d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf31f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf320` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf322` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf323` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf325` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf326` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf328` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf329` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf32b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf32c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf337` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf338` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf33a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf33b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf33d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf33e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf340` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf341` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf343` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf344` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf346` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf347` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf349` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf34a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf34c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf34d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf355` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf356` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf358` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf359` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf35b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf35c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf35e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf35f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf361` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf362` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf364` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf365` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf367` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf368` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf36a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf36b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf373` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf374` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf376` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf377` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf379` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf37a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf382` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf383` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf385` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf386` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf388` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf389` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf38b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf38c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf38e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf38f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf391` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf392` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf394` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf395` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3a0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3a1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3a3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3a4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3a6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3a7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3a9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3aa` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3ac` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3ad` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3af` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3b0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3b2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3b3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3b5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3b6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3b8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3b9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3bb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3bc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3be` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3bf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3c1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3c2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3c4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3c5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3c7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3c8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3ca` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3cb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3cd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3ce` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3d0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3d1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3d3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3d4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3d5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3d6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3d7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3d8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3d9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3da` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3db` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3dc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3dd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3de` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3df` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3e0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3e1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3e2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3e3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3e8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3e9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3ea` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3eb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3ec` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3ed` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3ee` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3ef` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3f0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3f1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3f2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3f3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3f4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3f5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3f6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3f7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3f8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3f9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3fa` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3fb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3fc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3fd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3fe` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf3ff` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf406` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf407` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf408` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf409` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf40a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf40b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf40c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf40d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf40e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf40f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf410` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf411` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf412` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf413` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf414` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf415` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf416` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf418` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf419` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf41b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf41c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf41e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf41f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf421` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf422` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf424` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf425` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf427` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf428` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf42a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf42b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf42d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf42e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf430` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf431` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf43c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf43d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf43f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf440` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf442` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf443` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf445` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf446` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf448` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf449` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf44b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf44c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf44d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf44e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf44f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf451` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf452` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf457` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf458` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf45a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf45b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf45d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf45e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf46c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf46d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf46e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf46f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf470` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf472` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf473` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf47b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf47c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf47d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf47e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf47f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf480` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf481` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf482` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf483` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf48d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf48e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4a8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4a9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4ab` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4ac` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4ad` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4ae` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4af` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4b0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4b1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4b2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4b3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4bd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4be` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4bf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4c0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4c1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4c2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4c3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4c4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4c5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4c6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4c7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4c8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4c9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4ca` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4cb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4cc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4cd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4ce` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4d8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4d9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4db` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4dc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4de` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4df` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4e1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4e2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4e4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4e5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4e7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf4e8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8ef` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8f0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8f1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8f2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8f3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8f4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8f5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8f6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8f7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8f8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8f9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8fa` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8fb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8fc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8fd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8fe` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf8ff` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf900` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf901` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf902` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf903` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf904` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf905` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf906` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf907` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf908` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf909` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf90a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf90b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf90c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf90d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf90e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf90f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf910` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf911` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf912` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf913` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf914` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf915` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf916` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf917` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf918` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf919` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf91a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf91b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf91c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf91d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf91e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf91f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf920` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf921` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf922` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf923` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf924` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf925` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf926` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf927` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf928` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf929` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf92a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf92b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf92c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf92d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf92e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf92f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf930` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf931` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf932` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf933` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf934` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf935` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf936` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf937` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf938` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf93a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf93b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf93d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf93e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf940` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf941` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf942` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf943` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf944` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf945` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf946` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf947` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf948` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf949` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf94a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf94b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf94c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf94d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf94e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf94f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf950` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf951` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf952` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf953` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf954` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf955` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf956` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf957` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf958` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf959` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf95a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf95b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf95c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf95d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf95e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf95f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf960` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf961` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf962` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf963` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf964` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf965` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf966` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf967` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf968` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf969` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf96a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf96b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf96c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf96d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf96e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf96f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf970` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf971` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf972` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf973` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf974` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf975` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf976` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf977` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf978` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf979` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf97a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf97b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf97c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf97d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf97e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf97f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf980` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf981` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf982` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf983` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf984` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf985` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf986` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf987` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf988` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf989` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf98a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf98b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf98c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf98d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf98e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf98f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf990` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf991` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf992` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf993` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf994` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf995` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf996` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf997` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf998` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf999` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf99a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf99b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf99c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf99d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf99e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf99f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9a0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9a1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9a2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9a3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9a4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9a5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9a6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9a7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9a8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9a9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9aa` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9ab` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9ac` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9ad` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9ae` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9af` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9b0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9b1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9b2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9b3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9b4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9b5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9b6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9b7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9b8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9b9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9ba` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9bb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9bc` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9bd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9be` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9bf` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9c0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9c1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9c2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9c3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9c4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9c5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9c6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9cd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9ce` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9d0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9d1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9d3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9d4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9d5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9d6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9d7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9d8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9d9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9da` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9db` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9e2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9e3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9e5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9e6` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9e8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9e9` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9eb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9ec` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9ed` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9ee` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9ef` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9f0` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9f1` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9f2` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9f3` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9f4` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9f5` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9f7` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9f8` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9fa` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9fb` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9fd` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9fe` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xf9ff` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa00` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa01` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa02` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa03` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa04` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa05` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa06` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa07` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa08` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa09` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa0a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa0b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa0c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa0d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa0e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa0f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa10` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa18` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa19` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa1a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa27` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa28` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa29` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa2d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa2e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa2f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa30` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa31` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa32` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa33` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa34` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa35` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa36` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa37` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa39` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa3a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa3b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa3c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa3d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa3e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa3f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa40` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa41` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa42` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa43` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa44` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa45` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa46` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa47` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa48` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa49` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa4a` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa4b` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa4c` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa4d` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa4e` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa4f` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa50` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa51` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa52` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfa53` | — | med | auto-shape | string-table (written by SKINIT) |
| `GS 0xfec9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfeca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfecb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfecc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfecd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfece` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfecf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfed0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfed1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfed2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfed3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfed4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfed5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfed6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfed7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfed8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfed9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfeda` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfedb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfedc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfedd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfede` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfedf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfee0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfee1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfee2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfee3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfee4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfee5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfee6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfee7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfee8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfee9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfeea` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfeeb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfeec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfeee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff02` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff2a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff2b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff2c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff2d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff2e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff2f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff30` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff31` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff32` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff33` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff34` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff35` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff36` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff37` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff38` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff39` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff3a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff3b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff3c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff3d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff3e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff3f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff40` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff41` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff42` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff43` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff44` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff45` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff46` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff91` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff92` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff93` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff94` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff95` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff96` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff97` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff98` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff99` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff9a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff9b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff9c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff9d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff9e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xff9f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xffa0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xffa1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xffa2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xffa3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfff5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfff6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfff7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfff8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfff9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfffa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfffb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfffc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfffd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xfffe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0xffff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10000` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10001` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10002` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10003` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10004` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10005` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10006` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10007` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10008` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10009` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1000a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1000b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1000c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1000d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1000e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1000f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10010` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10011` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10012` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10013` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10014` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10015` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10016` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10017` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10027` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10028` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10029` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1002a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1002b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1002c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1002d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1002e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1002f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10030` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10031` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10032` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10033` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10034` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10035` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10036` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10037` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10038` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10039` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1003a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1003b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1003c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1003d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1003e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1003f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10040` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10041` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10042` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10043` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10044` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10045` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10046` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10047` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10048` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10049` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1004a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1004b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1004c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1004d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1004e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1004f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10050` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10059` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1005a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1005b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100bd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100be` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100bf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100c0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100c1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100c2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100c3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100c4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100c5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100c6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100c7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100c8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100c9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100ca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100cb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100cc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100cd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100ce` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100cf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100d0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100d1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100d2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100d3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100d4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100d5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100d6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100d7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100d8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100d9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100da` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100db` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100dc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100dd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100de` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100df` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100e0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100e1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100e2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100e3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100e4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100e5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100e6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100e7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100e8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100e9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100ea` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100eb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100ec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100ed` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100ee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100ef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100f0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100f1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100f2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100f3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100f4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100f5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100f6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100f7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100f8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100f9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100fa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100fb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100fc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100fd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100fe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x100ff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10100` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10101` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10102` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10103` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10104` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10105` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10106` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10107` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10108` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10109` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1010a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1010b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1010c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1010d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1010e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1010f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10110` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10111` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10112` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10113` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10114` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10115` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10116` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10117` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10118` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10119` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1011a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1011b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1011c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1011d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1011e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1011f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10120` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10121` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10122` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10123` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10124` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10125` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10126` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10127` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10128` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10129` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1012a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1012b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1012c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1012d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1012e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1012f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10130` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10131` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10132` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1013f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10140` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10141` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10142` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10149` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1014a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1014b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1014c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1014d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1014e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1014f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10150` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10151` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10152` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10153` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10154` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10155` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10156` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10157` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10158` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10159` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1015a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1015b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1015c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1015d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1015e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1015f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10167` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10168` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10169` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1016a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1016b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1016c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1016d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1016e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1016f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10170` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10171` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10172` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10173` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10174` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10175` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10176` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10177` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10178` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10179` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1017a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1017b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1017c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1017d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1017e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1017f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10180` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10181` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10182` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10183` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10184` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10185` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10186` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101e9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101ea` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101eb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101ec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101ed` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101ee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101ef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101f0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101f1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101f2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101f3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101f4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101f5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101f6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101f7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101f8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101f9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101fa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101fb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101fc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101fd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101fe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x101ff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10200` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10201` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10202` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10203` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10204` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10205` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10206` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10207` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10208` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10209` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1020a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1020b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1020c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1021b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1021c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1021d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1021e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1021f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10220` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10221` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10222` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10223` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10224` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10225` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10226` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10227` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10228` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10229` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1022a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1022b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1022c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1022d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1022e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1069b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1069c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1069e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1069f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106a1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106a2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106a4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106a5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106a7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106a8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106aa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106ab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106ad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106ae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106b0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106b1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106b3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106b4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106b6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106b7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106b9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106ba` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106bc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106bd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106bf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106c0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106c1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106c2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106c3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106c5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106c6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106c7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106c8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106c9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106ca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106cb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106cc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106cd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106ce` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106cf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106d0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106d1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106d2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106d4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106d5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106d7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106d8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106da` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106db` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106dd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106de` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106e0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106e1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106e3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106e4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106e6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106e7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106e9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106ea` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106ec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106ed` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106ef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106f0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106f2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106f3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106f5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106f6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106f8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106f9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106fb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106fc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106fe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x106ff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10701` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10702` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10704` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10705` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1070a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1070b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10746` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10747` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107be` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107bf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107c0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107c1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107c2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107c3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107c4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107c5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107c6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107c7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107c8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107ca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107cb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107cd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107ce` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107d0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107d1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107d3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107d4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107d6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107d7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107d9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107da` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107dc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107dd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107df` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107e0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107e2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107e3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107e5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107e6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107e8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107e9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107eb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107ec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107ee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107ef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107f1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107f2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107f4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107f5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107f7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107f8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107fa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107fb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107fd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x107fe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10800` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10801` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10803` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10804` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10806` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10807` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10809` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1080a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1080c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1080d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1080f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10810` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10812` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10813` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x108f3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x108f4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x108f6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x108f7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x108f9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x108fa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x108fc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x108fd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x108ff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10900` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10902` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10903` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10905` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10906` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10908` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10909` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1090b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1090c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1090e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1090f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10911` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10912` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10914` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10915` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10917` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10918` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1091a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1091b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1091d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1091e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10920` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10921` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10923` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10924` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10926` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10927` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10929` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1092a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a1f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a20` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a22` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a23` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a25` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a26` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a28` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a29` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a2b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a2c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a2e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a2f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a31` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a32` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a34` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a35` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a37` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a38` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a3a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a3b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a3d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a3e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a40` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a41` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a43` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a44` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a46` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a47` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a49` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a4a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a4c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a4d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a4f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a50` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a52` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a53` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a55` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a56` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a58` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a59` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a5b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a5c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a5e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a5f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a61` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a62` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a64` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a65` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a67` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a68` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a6a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a6b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a6d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a6e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a70` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a71` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a73` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a74` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a76` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a77` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a79` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a7a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a7c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a7d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a7f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a80` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a82` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a83` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a85` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10a86` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ab5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ab6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ab8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ab9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10abb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10abc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10abe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10abf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ac1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ac2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ac4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ac5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ac7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ac8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10aca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10acb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10acd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ace` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ad0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ad1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ad3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ad4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ad6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ad7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ad9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ada` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10adc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10add` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10adf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ae0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ae2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ae3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ae5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ae6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ae8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ae9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10aeb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10aec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10aee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10aef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10af1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10af2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10af4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10af5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10af7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10af8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10afa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10afb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10afd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10afe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b00` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b01` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b03` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b04` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b06` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b07` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b09` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b0a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b0c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b0d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b0f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b10` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b12` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b13` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b15` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b16` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b18` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b19` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b1b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b1c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b1e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b1f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b21` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b22` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b24` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b25` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b27` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b28` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b2a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b2b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b2d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b2e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b30` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b31` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b4b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b4c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b4d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b4e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b4f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b50` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b51` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b52` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10b53` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c77` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c78` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c7a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c7b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c7d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c7e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c80` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c81` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c83` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c84` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c86` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c87` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c89` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c8a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c8c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c8d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c8f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c90` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c92` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c93` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c95` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c96` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c98` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c99` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c9a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c9b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c9c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c9e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10c9f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ca1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ca2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ca4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ca5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ca7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ca8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10caa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cb0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cb1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cb3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cb4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cb6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cb7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cb9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cba` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cbc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cbd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cbf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cc0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cc2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cc3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cc5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cc6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cc8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cc9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ccb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ccc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ccd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cce` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ccf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cd0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cd1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cd2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cd4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cd5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cd7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cd8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cda` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cdb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cdd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cde` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ce0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ce1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ce3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ce4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ce6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ce7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ce8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ce9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cea` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cec` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ced` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cee` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cef` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cf0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cf2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cf3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cf5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cf6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cf8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cf9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cfb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cfc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cfe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10cff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d01` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d02` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d04` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d05` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d07` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d08` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d0a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d0b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d0c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d0d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d0e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d10` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d11` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d13` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d14` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d16` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d17` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d19` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d1a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d1c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d1d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d1f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d20` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d22` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d23` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d25` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d26` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d28` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d29` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d2b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d2c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d2e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d2f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d31` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d32` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d34` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d35` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d37` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d38` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d39` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d3a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d3b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d3d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d3e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d40` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d41` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d43` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d44` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d46` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d47` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d48` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d49` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d4a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d4c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d4d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d4f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d50` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d52` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d53` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d55` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d56` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d58` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d59` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d5b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d5c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d5e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d5f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d61` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d62` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d64` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d65` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d66` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d67` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d68` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d6a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d6b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d6d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d6e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d70` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d71` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d73` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d74` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d76` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d77` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d79` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d7a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d7c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d7d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d7f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d80` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d82` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d83` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d84` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d85` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d86` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d88` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d89` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d8b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d8c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d8e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d8f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d91` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d92` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d94` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d95` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d97` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d98` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d9a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d9b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d9c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d9d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10d9e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10da0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10da1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10da3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10da4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10da6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10da7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10da9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10daa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10daf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10db0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10db2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10db3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10db5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10db6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10db8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10db9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dbb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dbc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dbe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dbf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dc1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dc2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dc4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dc5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dc7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dc8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dcb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dcd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dce` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dd0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dd1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dd3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dd4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dd6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dd7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dd8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dfd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dfe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10dff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e00` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e01` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e02` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e03` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e04` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e05` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e06` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e07` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e08` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e1b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e1c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e1e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e1f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e21` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e22` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e24` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e25` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e27` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e28` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e2a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e2b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e2d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e2e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e30` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e31` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e33` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e34` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e36` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e37` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e39` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e3a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e3c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e3d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e3f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e40` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e41` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e42` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e43` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e45` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e46` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e48` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e49` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e4b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e4c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e4e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e4f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e51` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e52` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e54` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e55` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e57` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e58` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e59` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e5a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e5b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e5c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e5d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e5e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e75` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e76` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e78` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e79` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e7b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e7c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e7e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e7f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e81` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e82` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e84` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e85` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e87` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e88` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e8a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e8b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e8d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e8e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e90` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e91` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e92` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e93` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e94` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e96` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e97` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e99` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e9a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e9b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e9c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e9d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10e9f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ea0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ea2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ea3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ea5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ea6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ea8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ea9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eab` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eae` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eaf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eb1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eb2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eb4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eb5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eb6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eb7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eb8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eba` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ebb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ebd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ebe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ec0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ec1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ec3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ec4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ec5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ec6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ec7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ec8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ec9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10eca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ecc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ecd` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ecf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ed0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ed2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ed3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ffb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ffc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10ffe` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x10fff` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11001` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11002` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11004` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11005` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11007` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11008` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1100a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1100b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1100d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1100e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11010` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11011` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11013` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11014` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11016` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11017` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11019` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1101a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1101c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1101d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1101f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11020` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11022` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11023` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11025` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11026` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11028` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11029` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1102b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1102c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1102e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1102f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11031` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11032` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11034` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11035` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11037` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11038` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1103a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1103b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1103d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1103e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11040` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11041` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11043` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11044` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11046` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11047` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11049` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1104a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1104c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1104d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1104f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11050` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11052` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11053` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11055` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11056` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11058` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11059` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1105b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1105c` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1105e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1105f` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11061` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11062` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11064` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11065` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11091` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11092` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11094` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11095` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11097` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11098` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1109a` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1109b` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1109d` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x1109e` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110a0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110a1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110a3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110a4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110a6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110a7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110a9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110aa` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110ac` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110ad` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110af` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110b0` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110b2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110b3` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110b5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110b6` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110b8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110b9` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110bb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110bc` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110be` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110bf` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110c1` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110c2` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110c4` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110c5` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110c7` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110c8` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110ca` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x110cb` | — | med | auto-shape | string-table (written by ITINIT) |
| `GS 0x11e09` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e0a` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e0b` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e0c` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e0d` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e0e` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e0f` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e10` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e18` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e19` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e1a` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e1b` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e1c` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e1d` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e1e` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e1f` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e20` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e21` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e22` | — | med | auto-shape | string-table (written by ILINIT) |
| `GS 0x11e29` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e2b` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e2c` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e2d` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e2e` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e2f` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e30` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e31` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e32` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e33` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e34` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e35` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e36` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e37` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e38` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e39` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e3a` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e3b` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e3c` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e3d` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e3e` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e3f` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e40` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e41` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e42` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e43` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e4a` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e4b` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e4c` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e4d` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e4e` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e4f` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e51` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e52` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e53` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e54` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e55` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e56` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e57` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e5b` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e5c` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e5d` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e5e` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e5f` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e60` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e61` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e62` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e63` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e64` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e65` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e66` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e67` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e68` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e69` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e6a` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e6b` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e6c` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e6d` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e6e` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e6f` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e70` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e74` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e75` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e76` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e77` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e78` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e79` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e7e` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e7f` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e80` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e81` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e83` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e88` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e89` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e8a` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e8d` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e8e` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e8f` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e90` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e92` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e93` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e97` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e98` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e99` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e9a` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e9c` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e9d` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11e9e` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ea1` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ea6` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11eab` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11eac` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ead` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11eae` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11eb0` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11eb5` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11eb6` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11eb7` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11eba` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ec4` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ec9` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11eca` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ecb` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ecc` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ecd` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ece` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ecf` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ed0` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ed1` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11ed2` | — | med | auto-shape | string-table (written by AMINIT) |
| `GS 0x11f55` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f56` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f57` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f58` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f59` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f5a` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f5b` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f5c` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f5d` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f5e` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f5f` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f60` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f61` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f62` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f63` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f64` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f65` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f66` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f67` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f68` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f69` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f6a` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f6b` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f6c` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f6d` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f6e` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f6f` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f70` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f71` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f72` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f73` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f74` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f75` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f76` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f77` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f78` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f79` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f7a` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f7b` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f7c` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f7d` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f7e` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f7f` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f80` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f81` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f82` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f83` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f84` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f85` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f86` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f87` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f88` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f89` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f8a` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f8b` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f8c` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f8d` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f8e` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f8f` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f90` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f91` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f92` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f93` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f94` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f95` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f96` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f97` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f98` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f99` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f9a` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f9b` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f9c` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f9d` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f9e` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11f9f` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fa0` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fa1` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fa2` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fa3` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fa4` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fa5` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fa6` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fa7` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fa8` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fa9` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11faa` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fab` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fac` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fad` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fae` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11faf` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fb0` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fb1` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fb2` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fb3` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fb4` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fb5` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fb6` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fb7` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fb8` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fb9` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fba` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fbb` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fbc` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fbd` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fbe` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fbf` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fc0` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fc1` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fc2` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fc3` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fc4` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fc5` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fc6` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fc7` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fc8` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fc9` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fca` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fcb` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fcc` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fce` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fcf` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fd0` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x11fd2` | — | med | auto-shape | string-table (written by MPINIT) |
| `GS 0x1201d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1201e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1201f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12020` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12021` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12022` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12023` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12024` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12025` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12026` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12027` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12028` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12029` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1202a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1202b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1202c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1202d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1202e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1202f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12030` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12031` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12032` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12033` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12034` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12035` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12036` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12037` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12038` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12039` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1203a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1203b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1203c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1203d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1203e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1203f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12040` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12041` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12042` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12043` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12044` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12045` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12046` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12047` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12048` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12049` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1204a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1204b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1204c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1204d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1204e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1204f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12050` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12051` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12052` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12053` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12054` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12055` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12056` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12057` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12058` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12059` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1205a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1205b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1205c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1205d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1205e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1205f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12060` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12061` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12062` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12063` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12064` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12065` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12066` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12067` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12068` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12069` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1206a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1206b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1206c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1206d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1206e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1206f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12070` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12071` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12072` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12073` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12074` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12075` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12076` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12077` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12078` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12079` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1207a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1207b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1207c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1207d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1207e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1207f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12080` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12081` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12082` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12083` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12084` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12085` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12086` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12087` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12088` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12089` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1208a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1208b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1208c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1208d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120e5` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120e6` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120e7` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120e8` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120e9` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120ea` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120eb` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120ec` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120ed` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120ee` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120ef` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120f0` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120f1` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120f2` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120f9` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120fa` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120fb` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120fc` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120fd` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120fe` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x120ff` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12100` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12101` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12102` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12103` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12104` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12105` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12106` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12107` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12108` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12109` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1210a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1210b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1210c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1210d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1210e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1210f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12110` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12111` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12112` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12113` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12114` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12115` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12116` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12117` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12118` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12119` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1211a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1211b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1211c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12211` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12212` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12213` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12214` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12215` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12216` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12217` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12218` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12219` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1221b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1221c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1221d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1221e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1221f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12220` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12221` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12225` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12226` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12227` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12228` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12229` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1222a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1222b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1222f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12230` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12231` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12232` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12233` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12234` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12235` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12236` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12237` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12238` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12239` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1223a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1223b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1223c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1223d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1223e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1223f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12240` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12241` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12242` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12243` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12244` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12245` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12246` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12247` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12248` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12249` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1224a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1224b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1224c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1224d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1224e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1224f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12257` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12261` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12262` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12263` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12264` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12265` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12266` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12267` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12268` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12269` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1226a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1226b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1226c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1226d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1226e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1226f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12270` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12271` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12272` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12273` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12274` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12275` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12276` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12277` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12278` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12279` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1227a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1227b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1227c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1227d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1227e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1227f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12280` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12281` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12282` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12283` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12284` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12285` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12286` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12287` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12288` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12289` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1228a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1228b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1228c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1228d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1228e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1228f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12290` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12291` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12292` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12293` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12294` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12295` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12296` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12297` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12298` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12299` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1229a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1229b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1229c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1229d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1229e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1229f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122a0` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122a1` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122a2` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122a3` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122a4` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122a5` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122a6` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122a7` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122a8` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122a9` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122b1` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122b2` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122b3` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122b4` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122b5` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122b6` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122b7` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122bb` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122bc` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122bd` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122be` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122bf` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122c0` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122c1` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122c2` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122c3` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122c5` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122c6` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122c7` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122c8` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122cf` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122d0` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122d1` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122d2` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122d3` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122d9` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122da` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122db` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122dc` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122dd` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122de` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122df` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122e0` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122e3` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122e4` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122e5` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122e6` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122e7` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122e8` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122e9` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122ed` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122ee` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122ef` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122f0` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122f1` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122f2` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122f3` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122f4` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122f5` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122f6` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122f7` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122f8` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122f9` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122fa` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x122fb` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12301` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12302` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12303` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12304` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12305` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12306` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1230b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1230c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1230d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1230e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1230f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12310` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12311` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12312` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12313` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12314` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12315` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12316` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12317` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12318` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12319` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1231a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1231b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1231c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1231d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1231f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12320` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12321` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12322` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12323` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12324` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12325` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12326` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12327` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12328` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12329` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1232a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1232b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1232c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1232d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1232e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1232f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12330` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12331` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12332` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12333` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12334` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12335` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12336` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12337` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1233d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1233e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1233f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12340` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12341` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12342` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12343` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12344` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12345` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12346` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12347` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12348` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12349` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1234a` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1234b` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1234c` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1234d` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1234e` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x1234f` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12350` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123a1` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123a2` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123a3` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123a4` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123a5` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123a6` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123a7` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123a8` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123a9` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123aa` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123ab` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123ac` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123ad` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123ae` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123af` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123b0` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123b1` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123b2` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123b3` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123b4` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123b5` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123b6` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123b7` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123b8` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123b9` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123ba` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123bb` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123bc` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123bd` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123be` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123bf` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123c0` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123c1` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123c2` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123c3` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123c4` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123c5` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123c6` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123c7` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123c8` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123c9` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123ca` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123cb` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123cc` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123cd` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123ce` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123cf` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123d0` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123d1` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123d2` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123d3` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123d4` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123d5` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123d6` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123d7` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123d8` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x123d9` | — | med | auto-shape | string-table (written by DEINIT) |
| `GS 0x12403` | — | med | auto-shape | string-table (written by DEBUGBTL) |
| `GS 0x1240a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1240b` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1240c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12410` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12411` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12412` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12413` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12416` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12417` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12418` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12419` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1241a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1241c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1241d` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1241e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12422` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12423` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12428` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12429` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1242a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1242b` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1242e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1242f` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12434` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12439` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1243a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1243b` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12440` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12441` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12446` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12447` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12448` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12449` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1244c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1244d` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12452` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12453` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12458` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12459` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1245e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1245f` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12460` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12464` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12465` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1246a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1246b` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12476` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12477` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1247c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1247d` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1247e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12482` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12483` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12484` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1248e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1248f` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12490` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12494` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12495` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12496` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12499` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1249a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1249b` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1249c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124a0` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124a1` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124a6` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124a7` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124ac` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124ad` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124ae` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124b2` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124b3` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124b8` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124b9` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124ba` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124be` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124bf` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124c0` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124c4` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124c5` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124c6` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124ca` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124cb` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124cc` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124cd` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124d0` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124d1` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124d2` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124d6` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124d7` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124d8` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124d9` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124da` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124dc` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124dd` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124de` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124df` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124e0` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124e2` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124e3` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124e4` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124e5` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124e6` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124e8` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124e9` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124ea` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124eb` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124ec` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124ee` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124ef` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124f0` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124f1` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124f2` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124f4` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124f5` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124f6` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124f7` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124f8` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124f9` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124fa` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124fb` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124fc` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x124fd` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12500` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12501` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12502` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12503` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12504` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12506` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12507` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12508` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12509` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1250a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1250c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1250d` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1250e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1250f` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12510` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12512` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12513` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12514` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12515` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12516` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12518` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12519` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1251a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1251b` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1251c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1251e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1251f` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12520` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12521` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12522` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12524` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12525` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12526` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12527` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12528` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1252a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1252b` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1252c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1252d` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1252e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12530` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12531` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12532` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12533` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12534` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12536` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12537` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12538` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12539` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1253a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1253b` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1253c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1253d` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1253e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1253f` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12540` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12541` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12542` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12543` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12544` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12545` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12546` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12548` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12549` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1254a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1254b` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1254c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1254d` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1254e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1254f` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12550` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12551` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12552` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12553` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12554` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12555` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12556` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12557` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12558` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12559` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1255a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1255b` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1255c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1255d` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1255e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1255f` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12560` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12561` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12562` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12563` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12564` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12565` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12566` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12567` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12568` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12569` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1256a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1256b` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1256c` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1256d` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1256e` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1256f` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12570` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12572` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12573` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12574` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12575` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12576` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12578` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12579` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x1257a` | — | med | auto-shape | string-table (written by REINIT) |
| `GS 0x12620` | — | med | auto-shape | string-table (written by DEBUGBTL) |
| `GS 0x12621` | — | med | auto-shape | string-table (written by DEBUGBTL) |
| `GS 0x12622` | — | med | auto-shape | string-table (written by DEBUGBTL) |
| `GS 0x12623` | — | med | auto-shape | string-table (written by DEBUGBTL) |
| `GS 0x12624` | — | med | auto-shape | string-table (written by DEBUGBTL) |
| `GS 0x1265d` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1265e` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1265f` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12660` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12661` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12662` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12663` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12664` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12665` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1266a` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1266b` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1266e` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1266f` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12670` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12672` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12673` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12674` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12676` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12677` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1267a` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1267b` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1267c` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1267d` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1267e` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1267f` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12680` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12682` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12683` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12684` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12686` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12687` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12688` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12689` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1268a` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1268b` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1268f` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12690` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12691` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12692` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12693` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12694` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12695` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12696` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12697` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x126f3` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x126f4` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x126f5` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12757` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12758` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12759` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1275a` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1275b` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1275c` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1275d` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x127bb` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x127bc` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1281f` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12820` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12821` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12822` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12823` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12829` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1282a` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1282b` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1282c` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1282d` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12833` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12834` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12883` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12884` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12885` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12886` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12887` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12897` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12898` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12899` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1289a` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1289b` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x128e7` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x128e8` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x128e9` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x128ea` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x128eb` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x128ec` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x128ed` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1294b` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1294c` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1294d` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1294e` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x1294f` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12950` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12951` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x129af` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x129b0` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x129b1` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x129b2` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x129b3` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x129b4` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x129b5` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x129b6` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x129b7` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x129b8` | — | med | auto-shape | string-table (written by ARINIT) |
| `GS 0x12a77` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a78` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a79` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a7a` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a7b` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a7d` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a7e` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a7f` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a80` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a81` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a82` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a83` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a84` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a85` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a86` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a87` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a88` | — | med | auto-shape | string-table (written by INIT2) |
| `GS 0x12a8b` | — | med | auto-shape | string-table (written by SETREGULATION) |
| `GS 0x12a8c` | — | med | auto-shape | string-table (written by SETREGULATION) |
| `GS 0x12a8d` | — | med | auto-shape | string-table (written by SETREGULATION) |
| `GS 0x12a8e` | — | med | auto-shape | string-table (written by SETREGULATION) |
| `GS 0x12a8f` | — | med | auto-shape | string-table (written by SETREGULATION) |
| `GS 0x12a90` | — | med | auto-shape | string-table (written by SETREGULATION) |
| `GS 0x12a91` | — | med | auto-shape | string-table (written by PH03_PLR_ACTION) |
| `GS 0x12a92` | — | med | auto-shape | string-table (written by ANIMEDRAW) |

## ui-toggle

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x3141` | adv_page_has_text | high | investigation | 1 once a page has text: every dialogue page sets it first, clear_message_text clears it, and publish_message_text publishes the layout only while it is set. |

## unknown

| address | name | conf | source | usage |
|---|---|---|---|---|
| `0x18` | — | med | auto-shape | record-table[stride 4] |
| `0x48` | — | med | auto-shape | record-table[stride 4] |
| `0x78` | — | med | auto-shape | record-table[stride 4] |
| `0xa8` | — | med | auto-shape | record-table[stride 4] |
| `0x4159` | — | high | auto-shape | chinit-name-table |
| `0x7993` | — | high | auto-shape | chinit-desc-table |
| `0x9580` | — | med | auto-shape | current-entity-index? |
| `0x9581` | — | med | auto-shape | record-table[stride 30] |
| `0x9635` | — | med | auto-shape | record-table[stride 30] |
| `0x96e9` | — | med | auto-shape | record-table[stride 30] |
| `0x979d` | — | med | auto-shape | record-table[stride 2] |
| `0x9ade` | — | low | auto-shape | index/counter? |
| `0x9dff` | — | med | auto-shape | record-table[stride 50] |
| `0xc50f` | — | med | auto-shape | record-table[stride 200] |
| `GS 0x12666` | — | med | auto-shape | record-table[stride 4] |
| `0x17b47` | — | low | auto-shape | index/counter? |
| `0x18321` | — | med | auto-shape | current-entity-index? |
| `0x18324` | — | med | auto-shape | record-table[stride 8] |
| `0x18374` | — | med | auto-shape | record-table[stride 8] |
| `0x1b378` | — | med | auto-shape | record-table[stride 30] |
| `0x1db50` | — | med | auto-shape | record-table[stride 1000] |
| `0x36da8` | — | med | auto-shape | record-table[stride 30] |
| `0x39db4` | — | med | auto-shape | record-table[stride 30] |
| `0x3fba3` | — | low | auto-shape | index/counter? |
| `0x3fba7` | — | low | auto-shape | index/counter? |
| `0x21480c` | — | low | auto-shape | index/counter? |
| `0x22d5f9` | — | med | auto-shape | record-table[stride 100] |
| `0x22ddcc` | — | med | auto-shape | record-table[stride 10000] |
| `0x25eb0c` | — | med | auto-shape | record-table[stride 100] |
| `0x25f2dc` | — | med | auto-shape | record-table[stride 100] |
| `0x25faac` | — | med | auto-shape | record-table[stride 50] |
| `0x25fe94` | — | med | auto-shape | record-table[stride 50] |
| `0x26027f` | — | med | auto-shape | record-table[stride 50] |
| `0x2978d2` | — | low | auto-shape | chinit-field? |
| `0x2978d8` | — | low | auto-shape | chinit-field? |
| `0x2978dd` | — | low | auto-shape | chinit-field? |
| `0x297987` | — | low | auto-shape | chinit-field? |
| `0x297988` | — | low | auto-shape | chinit-field? |
| `0x297991` | — | low | auto-shape | chinit-field? |
| `0x297996` | — | low | auto-shape | chinit-field? |
| `0x29799b` | — | low | auto-shape | chinit-field? |
| `0x2979a2` | — | low | auto-shape | chinit-field? |
| `0x2979a4` | — | low | auto-shape | chinit-field? |
| `0x2979aa` | — | low | auto-shape | chinit-field? |
| `0x2979ae` | — | low | auto-shape | chinit-field? |
| `0x2979b4` | — | low | auto-shape | chinit-field? |
| `0x2979b9` | — | low | auto-shape | chinit-field? |
| `0x2979be` | — | low | auto-shape | chinit-field? |
| `0x2979c3` | — | low | auto-shape | chinit-field? |
| `0x2979c8` | — | low | auto-shape | chinit-field? |
| `0x2979cd` | — | low | auto-shape | chinit-field? |
| `0x2979d2` | — | low | auto-shape | chinit-field? |
| `0x2979d7` | — | low | auto-shape | chinit-field? |
| `0x297b8a` | — | low | auto-shape | chinit-field? |
| `0x297b8f` | — | low | auto-shape | chinit-field? |
| `0x297b94` | — | low | auto-shape | chinit-field? |
| `0x297b99` | — | low | auto-shape | chinit-field? |
| `0x297b9e` | — | low | auto-shape | chinit-field? |
| `0x297ba3` | — | low | auto-shape | chinit-field? |
| `0x297ba8` | — | low | auto-shape | chinit-field? |
| `0x297bad` | — | low | auto-shape | chinit-field? |
| `0x297bb2` | — | low | auto-shape | chinit-field? |
| `0x297bb7` | — | low | auto-shape | chinit-field? |
| `0x297bbc` | — | low | auto-shape | chinit-field? |
| `0x297bbf` | — | low | auto-shape | chinit-field? |
| `0x297bc6` | — | low | auto-shape | chinit-field? |
| `0x297bcb` | — | low | auto-shape | chinit-field? |
| `0x297bda` | — | low | auto-shape | chinit-field? |
| `0x297c6b` | — | low | auto-shape | chinit-field? |
| `0x297c6f` | — | low | auto-shape | chinit-field? |
| `0x297c74` | — | low | auto-shape | chinit-field? |
| `0x297c79` | — | low | auto-shape | chinit-field? |
| `0x297c7f` | — | low | auto-shape | chinit-field? |
| `0x297c82` | — | low | auto-shape | chinit-field? |
| `0x297c84` | — | low | auto-shape | chinit-field? |
| `0x297c87` | — | low | auto-shape | chinit-field? |
| `0x297c8e` | — | low | auto-shape | chinit-field? |
| `0x297c93` | — | low | auto-shape | chinit-field? |
| `0x297ca3` | — | low | auto-shape | chinit-field? |
| `0x297ca7` | — | low | auto-shape | chinit-field? |
| `0x297cac` | — | low | auto-shape | chinit-field? |
| `0x297cae` | — | low | auto-shape | chinit-field? |
| `0x297cb5` | — | low | auto-shape | chinit-field? |
| `0x297cba` | — | low | auto-shape | chinit-field? |
| `0x297cbf` | — | low | auto-shape | chinit-field? |
| `0x297cc4` | — | low | auto-shape | chinit-field? |
| `0x297d1a` | — | low | auto-shape | chinit-field? |
| `0x297d1f` | — | low | auto-shape | chinit-field? |
| `0x297d24` | — | low | auto-shape | chinit-field? |
| `0x297d54` | — | low | auto-shape | chinit-field? |
| `0x297d58` | — | low | auto-shape | chinit-field? |
| `0x297d5b` | — | low | auto-shape | chinit-field? |
| `0x297d5f` | — | low | auto-shape | chinit-field? |
| `0x297d63` | — | low | auto-shape | chinit-field? |
| `0x297d65` | — | low | auto-shape | chinit-field? |
| `0x297d6b` | — | low | auto-shape | chinit-field? |
| `0x297d73` | — | low | auto-shape | chinit-field? |
| `0x297d79` | — | low | auto-shape | chinit-field? |
| `0x297d7b` | — | low | auto-shape | chinit-field? |
| `0x297d81` | — | low | auto-shape | chinit-field? |
| `0x297d88` | — | low | auto-shape | chinit-field? |
| `0x297d8d` | — | low | auto-shape | chinit-field? |
| `0x297d9c` | — | low | auto-shape | chinit-field? |
| `0x297da1` | — | low | auto-shape | chinit-field? |
| `0x297da5` | — | low | auto-shape | chinit-field? |
| `0x297daa` | — | low | auto-shape | chinit-field? |
| `0x297daf` | — | low | auto-shape | chinit-field? |
| `0x297db4` | — | low | auto-shape | chinit-field? |
| `0x298d59` | — | med | auto-shape | record-table[stride 2] |
| `0x298d81` | — | med | auto-shape | record-table[stride 2] |
| `0x298da9` | — | med | auto-shape | record-table[stride 6] |
| `0x29c45a` | — | low | auto-shape | index/counter? |
| `0x29c5eb` | — | med | auto-shape | record-table[stride 3] |
| `0x29d727` | — | low | auto-shape | chinit-field? |
| `0x29e5ba` | — | low | auto-shape | chinit-field? |
| `0x29e5be` | — | low | auto-shape | chinit-field? |
| `0x29e5c4` | — | low | auto-shape | chinit-field? |
| `0x29e5c9` | — | low | auto-shape | chinit-field? |
| `0x29e66e` | — | low | auto-shape | chinit-field? |
| `0x29e674` | — | low | auto-shape | chinit-field? |
| `0x29e67d` | — | low | auto-shape | chinit-field? |
| `0x29e682` | — | low | auto-shape | chinit-field? |
| `0x29e687` | — | low | auto-shape | chinit-field? |
| `0x29e690` | — | low | auto-shape | chinit-field? |
| `0x29e696` | — | low | auto-shape | chinit-field? |
| `0x29e69a` | — | low | auto-shape | chinit-field? |
| `0x29e6a0` | — | low | auto-shape | chinit-field? |
| `0x29e6a5` | — | low | auto-shape | chinit-field? |
| `0x29e6aa` | — | low | auto-shape | chinit-field? |
| `0x29e6af` | — | low | auto-shape | chinit-field? |
| `0x29e6b4` | — | low | auto-shape | chinit-field? |
| `0x29e6b9` | — | low | auto-shape | chinit-field? |
| `0x29e6be` | — | low | auto-shape | chinit-field? |
| `0x29e6c3` | — | low | auto-shape | chinit-field? |
| `0x29e871` | — | low | auto-shape | chinit-field? |
| `0x29e876` | — | low | auto-shape | chinit-field? |
| `0x29e87b` | — | low | auto-shape | chinit-field? |
| `0x29e880` | — | low | auto-shape | chinit-field? |
| `0x29e885` | — | low | auto-shape | chinit-field? |
| `0x29e88a` | — | low | auto-shape | chinit-field? |
| `0x29e88f` | — | low | auto-shape | chinit-field? |
| `0x29e894` | — | low | auto-shape | chinit-field? |
| `0x29e899` | — | low | auto-shape | chinit-field? |
| `0x29e89e` | — | low | auto-shape | chinit-field? |
| `0x29e8a3` | — | low | auto-shape | chinit-field? |
| `0x29e8a8` | — | low | auto-shape | chinit-field? |
| `0x29e8ab` | — | low | auto-shape | chinit-field? |
| `0x29e8b2` | — | low | auto-shape | chinit-field? |
| `0x29e8b7` | — | low | auto-shape | chinit-field? |
| `0x29e952` | — | low | auto-shape | chinit-field? |
| `0x29e957` | — | low | auto-shape | chinit-field? |
| `0x29e95b` | — | low | auto-shape | chinit-field? |
| `0x29e960` | — | low | auto-shape | chinit-field? |
| `0x29e965` | — | low | auto-shape | chinit-field? |
| `0x29e96b` | — | low | auto-shape | chinit-field? |
| `0x29e96e` | — | low | auto-shape | chinit-field? |
| `0x29e973` | — | low | auto-shape | chinit-field? |
| `0x29e97a` | — | low | auto-shape | chinit-field? |
| `0x29e97f` | — | low | auto-shape | chinit-field? |
| `0x29e98d` | — | low | auto-shape | chinit-field? |
| `0x29e993` | — | low | auto-shape | chinit-field? |
| `0x29e998` | — | low | auto-shape | chinit-field? |
| `0x29e99a` | — | low | auto-shape | chinit-field? |
| `0x29e9a1` | — | low | auto-shape | chinit-field? |
| `0x29e9a2` | — | low | auto-shape | chinit-field? |
| `0x29e9a6` | — | low | auto-shape | chinit-field? |
| `0x29e9ab` | — | low | auto-shape | chinit-field? |
| `0x29e9ac` | — | low | auto-shape | chinit-field? |
| `0x29e9b0` | — | low | auto-shape | chinit-field? |
| `0x29e9b1` | — | low | auto-shape | chinit-field? |
| `0x29ea01` | — | low | auto-shape | chinit-field? |
| `0x29ea06` | — | low | auto-shape | chinit-field? |
| `0x29ea0b` | — | low | auto-shape | chinit-field? |
| `0x29ea10` | — | low | auto-shape | chinit-field? |
| `0x29ea3c` | — | low | auto-shape | chinit-field? |
| `0x29ea40` | — | low | auto-shape | chinit-field? |
| `0x29ea47` | — | low | auto-shape | chinit-field? |
| `0x29ea4b` | — | low | auto-shape | chinit-field? |
| `0x29ea4f` | — | low | auto-shape | chinit-field? |
| `0x29ea51` | — | low | auto-shape | chinit-field? |
| `0x29ea56` | — | low | auto-shape | chinit-field? |
| `0x29ea57` | — | low | auto-shape | chinit-field? |
| `0x29ea5b` | — | low | auto-shape | chinit-field? |
| `0x29ea5c` | — | low | auto-shape | chinit-field? |
| `0x29ea5f` | — | low | auto-shape | chinit-field? |
| `0x29ea65` | — | low | auto-shape | chinit-field? |
| `0x29ea67` | — | low | auto-shape | chinit-field? |
| `0x29ea6a` | — | low | auto-shape | chinit-field? |
| `0x29ea6d` | — | low | auto-shape | chinit-field? |
| `0x29ea6f` | — | low | auto-shape | chinit-field? |
| `0x29ea74` | — | low | auto-shape | chinit-field? |
| `0x29ea78` | — | low | auto-shape | chinit-field? |
| `0x29ea79` | — | low | auto-shape | chinit-field? |
| `0x29ea7e` | — | low | auto-shape | chinit-field? |
| `0x29ea82` | — | low | auto-shape | chinit-field? |
| `0x29ea83` | — | low | auto-shape | chinit-field? |
| `0x29ea88` | — | low | auto-shape | chinit-field? |
| `0x29ea8d` | — | low | auto-shape | chinit-field? |
| `0x29ea91` | — | low | auto-shape | chinit-field? |
| `0x29ea92` | — | low | auto-shape | chinit-field? |
| `0x29ea96` | — | low | auto-shape | chinit-field? |
| `0x29ea97` | — | low | auto-shape | chinit-field? |
| `0x29ea9b` | — | low | auto-shape | chinit-field? |
| `0x29ea9c` | — | low | auto-shape | chinit-field? |
| `0x29eaa0` | — | low | auto-shape | chinit-field? |
| `0x29eaa1` | — | low | auto-shape | chinit-field? |
| `0x29eaa5` | — | low | auto-shape | chinit-field? |
| `0x29eaab` | — | low | auto-shape | chinit-field? |
| `0x29ec59` | — | low | auto-shape | chinit-field? |
| `0x29ec5e` | — | low | auto-shape | chinit-field? |
| `0x29ec63` | — | low | auto-shape | chinit-field? |
| `0x29ec68` | — | low | auto-shape | chinit-field? |
| `0x29ec6d` | — | low | auto-shape | chinit-field? |
| `0x29ec72` | — | low | auto-shape | chinit-field? |
| `0x29ec77` | — | low | auto-shape | chinit-field? |
| `0x29ec7c` | — | low | auto-shape | chinit-field? |
| `0x29ec81` | — | low | auto-shape | chinit-field? |
| `0x29ec86` | — | low | auto-shape | chinit-field? |
| `0x29ec8b` | — | low | auto-shape | chinit-field? |
| `0x29ec90` | — | low | auto-shape | chinit-field? |
| `0x29ec93` | — | low | auto-shape | chinit-field? |
| `0x29ec9a` | — | low | auto-shape | chinit-field? |
| `0x29ec9f` | — | low | auto-shape | chinit-field? |
| `0x29ed3a` | — | low | auto-shape | chinit-field? |
| `0x29ed3f` | — | low | auto-shape | chinit-field? |
| `0x29ed43` | — | low | auto-shape | chinit-field? |
| `0x29ed48` | — | low | auto-shape | chinit-field? |
| `0x29ed4d` | — | low | auto-shape | chinit-field? |
| `0x29ed53` | — | low | auto-shape | chinit-field? |
| `0x29ed56` | — | low | auto-shape | chinit-field? |
| `0x29ed5b` | — | low | auto-shape | chinit-field? |
| `0x29ed62` | — | low | auto-shape | chinit-field? |
| `0x29ed67` | — | low | auto-shape | chinit-field? |
| `0x29ed75` | — | low | auto-shape | chinit-field? |
| `0x29ed7b` | — | low | auto-shape | chinit-field? |
| `0x29ed80` | — | low | auto-shape | chinit-field? |
| `0x29ed82` | — | low | auto-shape | chinit-field? |
| `0x29ed89` | — | low | auto-shape | chinit-field? |
| `0x29ed8e` | — | low | auto-shape | chinit-field? |
| `0x29ed93` | — | low | auto-shape | chinit-field? |
| `0x29ed98` | — | low | auto-shape | chinit-field? |
| `0x29ede9` | — | low | auto-shape | chinit-field? |
| `0x29edee` | — | low | auto-shape | chinit-field? |
| `0x29edf3` | — | low | auto-shape | chinit-field? |
| `0x29edf8` | — | low | auto-shape | chinit-field? |
| `0x29ee24` | — | low | auto-shape | chinit-field? |
| `0x29ee28` | — | low | auto-shape | chinit-field? |
| `0x29ee2f` | — | low | auto-shape | chinit-field? |
| `0x29ee33` | — | low | auto-shape | chinit-field? |
| `0x29ee37` | — | low | auto-shape | chinit-field? |
| `0x29ee39` | — | low | auto-shape | chinit-field? |
| `0x29ee3f` | — | low | auto-shape | chinit-field? |
| `0x29ee47` | — | low | auto-shape | chinit-field? |
| `0x29ee4d` | — | low | auto-shape | chinit-field? |
| `0x29ee4f` | — | low | auto-shape | chinit-field? |
| `0x29ee55` | — | low | auto-shape | chinit-field? |
| `0x29ee5c` | — | low | auto-shape | chinit-field? |
| `0x29ee61` | — | low | auto-shape | chinit-field? |
| `0x29ee66` | — | low | auto-shape | chinit-field? |
| `0x29ee6b` | — | low | auto-shape | chinit-field? |
| `0x29ee70` | — | low | auto-shape | chinit-field? |
| `0x29ee75` | — | low | auto-shape | chinit-field? |
| `0x29ee79` | — | low | auto-shape | chinit-field? |
| `0x29ee7e` | — | low | auto-shape | chinit-field? |
| `0x29ee83` | — | low | auto-shape | chinit-field? |
| `0x29ee88` | — | low | auto-shape | chinit-field? |
| `0x29ee8d` | — | low | auto-shape | chinit-field? |
| `0x29f513` | — | low | auto-shape | chinit-field? |
| `0x29f51d` | — | low | auto-shape | chinit-field? |
| `0x29f523` | — | low | auto-shape | chinit-field? |
| `0x29f526` | — | low | auto-shape | chinit-field? |
| `0x29f52b` | — | low | auto-shape | chinit-field? |
| `0x29f532` | — | low | auto-shape | chinit-field? |
| `0x29f537` | — | low | auto-shape | chinit-field? |
| `0x29f552` | — | low | auto-shape | chinit-field? |
| `0x29f559` | — | low | auto-shape | chinit-field? |
| `0x29f55e` | — | low | auto-shape | chinit-field? |
| `0x29f568` | — | low | auto-shape | chinit-field? |
| `0x29f61f` | — | low | auto-shape | chinit-field? |
| `0x29f625` | — | low | auto-shape | chinit-field? |
| `0x29f631` | — | low | auto-shape | chinit-field? |
| `0x29f636` | — | low | auto-shape | chinit-field? |
| `0x29f63b` | — | low | auto-shape | chinit-field? |
| `0x29f640` | — | low | auto-shape | chinit-field? |
| `0x29f645` | — | low | auto-shape | chinit-field? |
| `0x29f649` | — | low | auto-shape | chinit-field? |
| `0x29f64e` | — | low | auto-shape | chinit-field? |
| `0x29f653` | — | low | auto-shape | chinit-field? |
| `0x29f658` | — | low | auto-shape | chinit-field? |
| `0x29f65d` | — | low | auto-shape | chinit-field? |
| `0x2a09f0` | — | low | auto-shape | chinit-field? |
| `0x2a09f1` | — | low | auto-shape | chinit-field? |
| `0x2a09f2` | — | low | auto-shape | chinit-field? |
| `0x2a09f3` | — | low | auto-shape | chinit-field? |
| `0x2a09f4` | — | low | auto-shape | chinit-field? |
| `0x2a09f5` | — | low | auto-shape | chinit-field? |
| `0x2a09f6` | — | low | auto-shape | chinit-field? |
| `0x2a09f7` | — | low | auto-shape | chinit-field? |
| `0x2a09f8` | — | low | auto-shape | chinit-field? |
| `0x2a09f9` | — | low | auto-shape | chinit-field? |
| `0x2a09fa` | — | low | auto-shape | chinit-field? |
| `0x2a09fb` | — | low | auto-shape | chinit-field? |
| `0x2a09fc` | — | low | auto-shape | chinit-field? |
| `0x2a09fd` | — | low | auto-shape | chinit-field? |
| `0x2a09fe` | — | low | auto-shape | chinit-field? |
| `0x2a09ff` | — | low | auto-shape | chinit-field? |
| `0x2a0a00` | — | low | auto-shape | chinit-field? |
| `0x2a0a01` | — | low | auto-shape | chinit-field? |
| `0x2a0a02` | — | low | auto-shape | chinit-field? |
| `0x2a0a03` | — | low | auto-shape | chinit-field? |
| `0x2a0a04` | — | low | auto-shape | chinit-field? |
| `0x2a0a05` | — | low | auto-shape | chinit-field? |
| `0x2a0a06` | — | low | auto-shape | chinit-field? |
| `0x2a0a07` | — | low | auto-shape | chinit-field? |
| `0x2a0a08` | — | low | auto-shape | chinit-field? |
| `0x2a0a09` | — | low | auto-shape | chinit-field? |
| `0x2a0a0a` | — | low | auto-shape | chinit-field? |
| `0x2a0a0b` | — | low | auto-shape | chinit-field? |
| `0x2a0a0c` | — | low | auto-shape | chinit-field? |
| `0x2a0a0d` | — | low | auto-shape | chinit-field? |
| `0x2a0a0e` | — | low | auto-shape | chinit-field? |
| `0x2a0a0f` | — | low | auto-shape | chinit-field? |
| `0x2a0a10` | — | low | auto-shape | chinit-field? |
| `0x2a0a11` | — | low | auto-shape | chinit-field? |
| `0x2a0a12` | — | low | auto-shape | chinit-field? |
| `0x2a0a13` | — | low | auto-shape | chinit-field? |
| `0x2a0a14` | — | low | auto-shape | chinit-field? |
| `0x2a0a15` | — | low | auto-shape | chinit-field? |
| `0x2a0a16` | — | low | auto-shape | chinit-field? |
| `0x2a0a17` | — | low | auto-shape | chinit-field? |
| `0x2a0a86` | — | low | auto-shape | chinit-field? |
| `0x2a0a87` | — | low | auto-shape | chinit-field? |
| `0x2a0a88` | — | low | auto-shape | chinit-field? |
| `0x2a0a89` | — | low | auto-shape | chinit-field? |
| `0x2a0a8a` | — | low | auto-shape | chinit-field? |
| `0x2a0a8b` | — | low | auto-shape | chinit-field? |
| `0x2a0a8c` | — | low | auto-shape | chinit-field? |
| `0x2a0a8d` | — | low | auto-shape | chinit-field? |
| `0x2a0a8e` | — | low | auto-shape | chinit-field? |
| `0x2a0a8f` | — | low | auto-shape | chinit-field? |
| `0x2a0a90` | — | low | auto-shape | chinit-field? |
| `0x2a0a91` | — | low | auto-shape | chinit-field? |
| `0x2a0a92` | — | low | auto-shape | chinit-field? |
| `0x2a0a9f` | — | low | auto-shape | chinit-field? |
| `0x2a0aa0` | — | low | auto-shape | chinit-field? |
| `0x2a0aa1` | — | low | auto-shape | chinit-field? |
| `0x2a0aa2` | — | low | auto-shape | chinit-field? |
| `0x2a0aa3` | — | low | auto-shape | chinit-field? |
| `0x2a0aa4` | — | low | auto-shape | chinit-field? |
| `0x2a0aa5` | — | low | auto-shape | chinit-field? |
| `0x2a0aa6` | — | low | auto-shape | chinit-field? |
| `0x2a0aa7` | — | low | auto-shape | chinit-field? |
| `0x2a0aa8` | — | low | auto-shape | chinit-field? |
| `0x2a0aa9` | — | low | auto-shape | chinit-field? |
| `0x2a0aaa` | — | low | auto-shape | chinit-field? |
| `0x2a0aab` | — | low | auto-shape | chinit-field? |
| `0x2a0aac` | — | low | auto-shape | chinit-field? |
| `0x2a0aad` | — | low | auto-shape | chinit-field? |
| `0x2a0aae` | — | low | auto-shape | chinit-field? |
| `0x2a2ccf` | — | low | auto-shape | chinit-field? |
| `0x2a2cd1` | — | low | auto-shape | chinit-field? |
| `0x2a2cd2` | — | low | auto-shape | chinit-field? |
| `0x2a2cd3` | — | low | auto-shape | chinit-field? |
| `0x2a2cd6` | — | low | auto-shape | chinit-field? |
| `0x2a2cd7` | — | low | auto-shape | chinit-field? |
| `0x2a2cda` | — | low | auto-shape | chinit-field? |
| `0x2a2cdd` | — | low | auto-shape | chinit-field? |
| `0x2a2cde` | — | low | auto-shape | chinit-field? |
| `0x2a2ce0` | — | low | auto-shape | chinit-field? |
| `0x2a2d03` | — | low | auto-shape | chinit-field? |
| `0x2a2d05` | — | low | auto-shape | chinit-field? |
| `0x2a2d09` | — | low | auto-shape | chinit-field? |
| `0x2a2d0a` | — | low | auto-shape | chinit-field? |
| `0x2a2d0b` | — | low | auto-shape | chinit-field? |
| `0x2a2d0c` | — | low | auto-shape | chinit-field? |
| `0x2a2d0d` | — | low | auto-shape | chinit-field? |
| `0x2a2d0f` | — | low | auto-shape | chinit-field? |
| `0x2a2d10` | — | low | auto-shape | chinit-field? |
| `0x2a2d11` | — | low | auto-shape | chinit-field? |
| `0x2a4057` | — | low | auto-shape | chinit-field? |
| `0x2a4059` | — | low | auto-shape | chinit-field? |
| `0x2a405a` | — | low | auto-shape | chinit-field? |
| `0x2a405b` | — | low | auto-shape | chinit-field? |
| `0x2a405e` | — | low | auto-shape | chinit-field? |
| `0x2a405f` | — | low | auto-shape | chinit-field? |
| `0x2a4062` | — | low | auto-shape | chinit-field? |
| `0x2a4065` | — | low | auto-shape | chinit-field? |
| `0x2a4066` | — | low | auto-shape | chinit-field? |
| `0x2a4068` | — | low | auto-shape | chinit-field? |
| `0x2a408b` | — | low | auto-shape | chinit-field? |
| `0x2a408d` | — | low | auto-shape | chinit-field? |
| `0x2a4091` | — | low | auto-shape | chinit-field? |
| `0x2a4092` | — | low | auto-shape | chinit-field? |
| `0x2a4093` | — | low | auto-shape | chinit-field? |
| `0x2a4094` | — | low | auto-shape | chinit-field? |
| `0x2a4095` | — | low | auto-shape | chinit-field? |
| `0x2a4097` | — | low | auto-shape | chinit-field? |
| `0x2a4098` | — | low | auto-shape | chinit-field? |
| `0x2a4099` | — | low | auto-shape | chinit-field? |
| `0x2a53df` | — | low | auto-shape | chinit-field? |
| `0x2a53e1` | — | low | auto-shape | chinit-field? |
| `0x2a53e2` | — | low | auto-shape | chinit-field? |
| `0x2a53e3` | — | low | auto-shape | chinit-field? |
| `0x2a53e6` | — | low | auto-shape | chinit-field? |
| `0x2a53e7` | — | low | auto-shape | chinit-field? |
| `0x2a53ea` | — | low | auto-shape | chinit-field? |
| `0x2a53ed` | — | low | auto-shape | chinit-field? |
| `0x2a53ee` | — | low | auto-shape | chinit-field? |
| `0x2a53f0` | — | low | auto-shape | chinit-field? |
| `0x2a5413` | — | low | auto-shape | chinit-field? |
| `0x2a5415` | — | low | auto-shape | chinit-field? |
| `0x2a5419` | — | low | auto-shape | chinit-field? |
| `0x2a541a` | — | low | auto-shape | chinit-field? |
| `0x2a541b` | — | low | auto-shape | chinit-field? |
| `0x2a541c` | — | low | auto-shape | chinit-field? |
| `0x2a541d` | — | low | auto-shape | chinit-field? |
| `0x2a541f` | — | low | auto-shape | chinit-field? |
| `0x2a5420` | — | low | auto-shape | chinit-field? |
| `0x2a5421` | — | low | auto-shape | chinit-field? |
| `0x2a6767` | — | low | auto-shape | chinit-field? |
| `0x2a6769` | — | low | auto-shape | chinit-field? |
| `0x2a676a` | — | low | auto-shape | chinit-field? |
| `0x2a676b` | — | low | auto-shape | chinit-field? |
| `0x2a676e` | — | low | auto-shape | chinit-field? |
| `0x2a676f` | — | low | auto-shape | chinit-field? |
| `0x2a6772` | — | low | auto-shape | chinit-field? |
| `0x2a6775` | — | low | auto-shape | chinit-field? |
| `0x2a6776` | — | low | auto-shape | chinit-field? |
| `0x2a6778` | — | low | auto-shape | chinit-field? |
| `0x2a679b` | — | low | auto-shape | chinit-field? |
| `0x2a679d` | — | low | auto-shape | chinit-field? |
| `0x2a67a1` | — | low | auto-shape | chinit-field? |
| `0x2a67a3` | — | low | auto-shape | chinit-field? |
| `0x2a67a4` | — | low | auto-shape | chinit-field? |
| `0x2a67a5` | — | low | auto-shape | chinit-field? |
| `0x2a67a7` | — | low | auto-shape | chinit-field? |
| `0x2a67a8` | — | low | auto-shape | chinit-field? |
| `0x2a67a9` | — | low | auto-shape | chinit-field? |
| `0x2a7aef` | — | low | auto-shape | chinit-field? |
| `0x2a7af1` | — | low | auto-shape | chinit-field? |
| `0x2a7af2` | — | low | auto-shape | chinit-field? |
| `0x2a7af3` | — | low | auto-shape | chinit-field? |
| `0x2a7af6` | — | low | auto-shape | chinit-field? |
| `0x2a7af7` | — | low | auto-shape | chinit-field? |
| `0x2a7afa` | — | low | auto-shape | chinit-field? |
| `0x2a7afd` | — | low | auto-shape | chinit-field? |
| `0x2a7afe` | — | low | auto-shape | chinit-field? |
| `0x2a7b00` | — | low | auto-shape | chinit-field? |
| `0x2a7b23` | — | low | auto-shape | chinit-field? |
| `0x2a7b25` | — | low | auto-shape | chinit-field? |
| `0x2a7b29` | — | low | auto-shape | chinit-field? |
| `0x2a7b2b` | — | low | auto-shape | chinit-field? |
| `0x2a7b2c` | — | low | auto-shape | chinit-field? |
| `0x2a7b2d` | — | low | auto-shape | chinit-field? |
| `0x2a7b2f` | — | low | auto-shape | chinit-field? |
| `0x2a7b30` | — | low | auto-shape | chinit-field? |
| `0x2a7b31` | — | low | auto-shape | chinit-field? |
| `0x2a8e77` | — | low | auto-shape | chinit-field? |
| `0x2a8e79` | — | low | auto-shape | chinit-field? |
| `0x2a8e7a` | — | low | auto-shape | chinit-field? |
| `0x2a8e7b` | — | low | auto-shape | chinit-field? |
| `0x2a8e7e` | — | low | auto-shape | chinit-field? |
| `0x2a8e7f` | — | low | auto-shape | chinit-field? |
| `0x2a8e82` | — | low | auto-shape | chinit-field? |
| `0x2a8e85` | — | low | auto-shape | chinit-field? |
| `0x2a8e86` | — | low | auto-shape | chinit-field? |
| `0x2a8e88` | — | low | auto-shape | chinit-field? |
| `0x2a8eab` | — | low | auto-shape | chinit-field? |
| `0x2a8ead` | — | low | auto-shape | chinit-field? |
| `0x2a8eb1` | — | low | auto-shape | chinit-field? |
| `0x2a8eb2` | — | low | auto-shape | chinit-field? |
| `0x2a8eb3` | — | low | auto-shape | chinit-field? |
| `0x2a8eb4` | — | low | auto-shape | chinit-field? |
| `0x2a8eb5` | — | low | auto-shape | chinit-field? |
| `0x2a8eb7` | — | low | auto-shape | chinit-field? |
| `0x2a8eb8` | — | low | auto-shape | chinit-field? |
| `0x2a8eb9` | — | low | auto-shape | chinit-field? |
| `0x2aa1ff` | — | low | auto-shape | chinit-field? |
| `0x2aa201` | — | low | auto-shape | chinit-field? |
| `0x2aa202` | — | low | auto-shape | chinit-field? |
| `0x2aa203` | — | low | auto-shape | chinit-field? |
| `0x2aa206` | — | low | auto-shape | chinit-field? |
| `0x2aa207` | — | low | auto-shape | chinit-field? |
| `0x2aa20a` | — | low | auto-shape | chinit-field? |
| `0x2aa20d` | — | low | auto-shape | chinit-field? |
| `0x2aa20e` | — | low | auto-shape | chinit-field? |
| `0x2aa210` | — | low | auto-shape | chinit-field? |
| `0x2aa233` | — | low | auto-shape | chinit-field? |
| `0x2aa235` | — | low | auto-shape | chinit-field? |
| `0x2aa239` | — | low | auto-shape | chinit-field? |
| `0x2aa23a` | — | low | auto-shape | chinit-field? |
| `0x2aa23b` | — | low | auto-shape | chinit-field? |
| `0x2aa23c` | — | low | auto-shape | chinit-field? |
| `0x2aa23d` | — | low | auto-shape | chinit-field? |
| `0x2aa23f` | — | low | auto-shape | chinit-field? |
| `0x2aa240` | — | low | auto-shape | chinit-field? |
| `0x2aa241` | — | low | auto-shape | chinit-field? |
| `0x31c728` | — | med | auto-shape | current-entity-index? |
| `0x330e85` | — | med | auto-shape | record-table[stride 2] |
| `0x3311a5` | — | med | auto-shape | record-table[stride 5] |
| `0x33158d` | — | med | auto-shape | record-table[stride 5] |
| `0x33220d` | — | med | auto-shape | record-table[stride 3] |
| `0x332465` | — | med | auto-shape | record-table[stride 3] |
| `0x332b6d` | — | med | auto-shape | record-table[stride 3] |
| `0x332dc5` | — | med | auto-shape | current-entity-index? |
| `0x333726` | — | med | auto-shape | record-table[stride 7] |
| `0x333c9e` | — | med | auto-shape | record-table[stride 7] |
| `0x391fa7` | — | med | auto-shape | current-entity-index? |
| `0x43f667` | — | med | auto-shape | current-entity-index? |
| `0x43f669` | — | med | auto-shape | current-entity-index? |
| `0x43f66a` | — | med | auto-shape | current-entity-index? |
| `0x43f672` | — | med | auto-shape | current-entity-index? |
| `0x43f674` | — | med | auto-shape | current-entity-index? |
| `0x43f687` | — | med | auto-shape | record-table[stride 10] |
| `0x44e146` | — | med | auto-shape | record-table[stride 30] |
| `0x44e182` | — | med | auto-shape | record-table[stride 30] |
| `0x44e1be` | — | med | auto-shape | record-table[stride 30] |
| `0x44e1fa` | — | med | auto-shape | record-table[stride 30] |
| `0x44e23a` | — | med | auto-shape | current-entity-index? |
| `0x44e247` | — | med | auto-shape | record-table[stride 30] |
| `0x44e2fb` | — | med | auto-shape | record-table[stride 30] |
| `0x4502a0` | — | low | auto-shape | index/counter? |
| `0x450691` | — | med | auto-shape | record-table[stride 30] |
| `0x4527e1` | — | med | auto-shape | record-table[stride 40] |
| `0x452831` | — | med | auto-shape | record-table[stride 40] |
| `0x452881` | — | med | auto-shape | record-table[stride 40] |
| `0x4528d1` | — | med | auto-shape | record-table[stride 40] |
| `0x452925` | — | med | auto-shape | record-table[stride 40] |
| `0x452975` | — | med | auto-shape | record-table[stride 40] |
| `0x4529c5` | — | med | auto-shape | record-table[stride 40] |
| `0x452a15` | — | med | auto-shape | record-table[stride 40] |
| `0x452a65` | — | med | auto-shape | record-table[stride 40] |
| `0x452ab5` | — | med | auto-shape | record-table[stride 40] |
| `0x452b05` | — | med | auto-shape | record-table[stride 40] |
| `0x452b55` | — | med | auto-shape | record-table[stride 1440] |
| `0x453695` | — | med | auto-shape | record-table[stride 120] |
| `0x453785` | — | med | auto-shape | record-table[stride 9600] |
| `0x458285` | — | med | auto-shape | record-table[stride 9600] |
| `0x45cd85` | — | med | auto-shape | record-table[stride 40] |
| `0x45cdd5` | — | med | auto-shape | record-table[stride 40] |
| `0x45ce25` | — | med | auto-shape | record-table[stride 40] |
| `0x45ce75` | — | med | auto-shape | record-table[stride 40] |
| `0x45cec5` | — | med | auto-shape | record-table[stride 40] |
| `0x45cf15` | — | med | auto-shape | record-table[stride 120] |
| `0x45d005` | — | med | auto-shape | record-table[stride 40] |
| `0x45d055` | — | med | auto-shape | record-table[stride 40] |
| `0x45d0a5` | — | med | auto-shape | record-table[stride 40] |
| `0x45d0f5` | — | med | auto-shape | record-table[stride 40] |
| `0x45d145` | — | med | auto-shape | record-table[stride 40] |
| `0x45d195` | — | med | auto-shape | record-table[stride 40] |
| `0x45d1e5` | — | med | auto-shape | record-table[stride 40] |
| `0x45d285` | — | med | auto-shape | record-table[stride 40] |
| `0x45d2d5` | — | med | auto-shape | record-table[stride 40] |
| `0x45d351` | — | med | auto-shape | record-table[stride 40] |
| `0x45d3a3` | — | med | auto-shape | record-table[stride 40] |
| `0x45d50b` | — | med | auto-shape | record-table[stride 40] |
| `0x45d55d` | — | med | auto-shape | record-table[stride 3] |
| `0x45d569` | — | med | auto-shape | record-table[stride 3] |
| `0x45d575` | — | med | auto-shape | record-table[stride 3] |
| `0x45d581` | — | med | auto-shape | record-table[stride 3] |
| `0x45d58d` | — | med | auto-shape | record-table[stride 3] |
| `0x45d599` | — | med | auto-shape | record-table[stride 3] |
| `0x45d5a5` | — | med | auto-shape | record-table[stride 3] |
| `0x45d5b1` | — | med | auto-shape | record-table[stride 3] |
| `0x467e63` | — | med | auto-shape | record-table[stride 40] |
| `0x4687ac` | — | med | auto-shape | current-entity-index? |
| `0x4687af` | — | low | auto-shape | index/counter? |
| `0x4687b1` | — | low | auto-shape | index/counter? |
| `0x4687b2` | — | low | auto-shape | index/counter? |
| `0x4687c0` | — | med | auto-shape | current-entity-index? |
| `0x4687c3` | — | low | auto-shape | index/counter? |
| `0x4687c5` | — | med | auto-shape | record-table[stride 40] |
| `0x468817` | — | med | auto-shape | record-table[stride 4] |
| `0x468847` | — | med | auto-shape | record-table[stride 4] |
| `0x468877` | — | med | auto-shape | record-table[stride 3] |
| `0x46889b` | — | med | auto-shape | record-table[stride 3] |
| `0x4688bf` | — | med | auto-shape | record-table[stride 3] |
| `0x4688d1` | — | med | auto-shape | record-table[stride 3] |
| `0x4688fe` | — | med | auto-shape | record-table[stride 3] |
| `0x46890a` | — | med | auto-shape | record-table[stride 3] |
| `0x468919` | — | med | auto-shape | current-entity-index? |
| `0x46891a` | — | med | auto-shape | current-entity-index? |
| `0x46891b` | — | med | auto-shape | record-table[stride 2] |
| `0x468920` | — | low | auto-shape | index/counter? |
| `0x468933` | — | med | auto-shape | record-table[stride 1000] |
| `0x469126` | — | med | auto-shape | record-table[stride 30] |
| `0x46b89a` | — | med | auto-shape | record-table[stride 30] |
