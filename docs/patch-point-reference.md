<!-- DO NOT EDIT -- generated from vm-map/patch-points-himegari.toml by tools/patch_points_build.py --build -->
# Named patch points — himegari (generated)

Stable site names for OMS fragments (`before site <name> { … }`; mod loader spec §5,
`docs/superpowers/specs/2026-11-03-mod-loader-design.md`). Each point lists its site per script revision;
a fragment compiles only against a revision its point lists.

## `himegari.experience.after_calculation`

Script: `ADDEXP.BIN`

The award is final and not yet applied. L[0x1] holds the experience the unit receives: G[0x62ccb] after the G[0x3302] multiplier bits (0x10 zero, 0x20 double, 0x40 tenfold). current_unit_id is the canonical unit. All three multiplier paths jump here, so `before site` sees every award and may rewrite L[0x1].

| Revision | Site |
|---|---|
| `sha256:b4f38c43f2c500012be48517ce773d3c9e81c12deeb4d712cab4b99749661e1c` | `0x9a` |
