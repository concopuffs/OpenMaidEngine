#!/usr/bin/env python3
"""Kamidori (SYS4433) *INIT specializations for tools/extract_init.py.

Specialized schemas encode one game's global addresses and authored counts, so Kamidori's
live here rather than beside Himegari's in extract_init.py. Each extractor decodes a fixed
absolute layout, so it reads the base table and any `--append N` layer unchanged. Tables not
listed (EBINIT and the INIT/INITCONFIG drivers) fall back to the shared shape modes.

Domain modules (layouts documented in docs/kamidori-data-tables.md):
  init_kamidori_characters   CCINIT CNINIT CIINIT CVINIT
  init_kamidori_items        ITINIT SKINIT ILINIT AFINIT RTINIT BTANINIT BTANINIT2
  init_kamidori_alchemy      ALINIT BUINIT IMINIT SHINIT HAINIT CSINIT SIINIT
  init_kamidori_maps         MPINIT STINIT STINIT2 LAINIT OBINIT COINIT AMINIT AMINIT2 DIINIT ARINIT
  init_kamidori_progression  QUINIT FAINIT VIINIT CGINIT SPINIT CDINIT CDINIT2 SCINIT EVINIT EUINIT
"""
from __future__ import annotations


def _table_extractors() -> dict[str, tuple[str, object]]:
    """TABLE -> (forced mode, extractor). Name mode keeps extract_init's message-table join."""
    import init_kamidori_alchemy as alchemy
    import init_kamidori_characters as characters
    import init_kamidori_items as items
    import init_kamidori_maps as maps
    import init_kamidori_progression as progression

    return {
        # characters
        "CCINIT": ("rules", characters.extract_class_change_rules),
        "CNINIT": ("name", characters.extract_character_names),
        "CIINIT": ("name", characters.extract_character_profiles),       # + CIMES join
        "CVINIT": ("name", characters.extract_voice_configuration),
        # items / combat
        "ITINIT": ("name", items.extract_items),                         # + ITMES join
        "SKINIT": ("name", items.extract_skills),                        # + SKMES join
        "ILINIT": ("name", items.extract_condition_definitions),
        "AFINIT": ("name", items.extract_affinity_tuning),
        "RTINIT": ("numeric", items.extract_routine_banks),
        "BTANINIT2": ("numeric", items.extract_battle_animations),
        "BTANINIT": ("mixed", items.extract_battle_effect_definitions),
        # alchemy / studio / town
        "ALINIT": ("numeric", alchemy.extract_alchemy_recipes),
        "BUINIT": ("numeric", alchemy.extract_studio_extensions),
        "IMINIT": ("numeric", alchemy.extract_equipment_growth),
        "SHINIT": ("mixed", alchemy.extract_shop_catalogue),
        "HAINIT": ("numeric", alchemy.extract_dungeon_halls),
        "CSINIT": ("mixed", alchemy.extract_costume_sprite_variants),
        "SIINIT": ("mixed", alchemy.extract_town_sites),
        # maps / world
        "MPINIT": ("mixed", maps.extract_floor_maps),
        "STINIT": ("mixed", maps.extract_stage_loader),
        "STINIT2": ("name", maps.extract_stage_catalog),
        "LAINIT": ("name", maps.extract_terrain_definitions),
        "OBINIT": ("name", maps.extract_object_types),
        "COINIT": ("mixed", maps.extract_costume_boards),
        "AMINIT2": ("name", maps.extract_world_areas),
        "AMINIT": ("mixed", maps.extract_area_unlock_rules),
        "DIINIT": ("numeric", maps.extract_dig_tables),
        "ARINIT": ("name", maps.extract_arena_battles),
        # progression
        "QUINIT": ("name", progression.extract_quests),
        "FAINIT": ("name", progression.extract_objectives),
        "VIINIT": ("name", progression.extract_vocabulary),              # + VIMES join
        "CGINIT": ("numeric", progression.extract_gallery),
        "SPINIT": ("numeric", progression.extract_h_scene_gallery),
        "CDINIT2": ("name", progression.extract_card_definitions),
        "CDINIT": ("numeric", progression.extract_card_generation_lists),
        "SCINIT": ("numeric", progression.extract_scene_dispatch),
        "EVINIT": ("mixed", progression.extract_event_cg_variants),
        "EUINIT": ("numeric", progression.extract_gallery_portraits),
    }


def specializations() -> dict:
    tables = _table_extractors()
    return {
        "forced_modes": {table: mode for table, (mode, _) in tables.items()},
        "extractors": {(mode, table): fn for table, (mode, fn) in tables.items()},
    }
