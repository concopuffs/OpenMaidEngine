-- The kamidori.tables format's handlers (data patches spec §6, docs/superpowers/specs/2026-11-17-data-patches-design.md).
-- Hand-written. A document names records by table, decimal id and field, by the registry's names, as OMS does:
-- unit["761"].stats.max_hp is unit[761].stats.max_hp. ome-records checks the whole document against the record map
-- before its first write, and writes through this base mod's own accessors. What builds each table and which fields a
-- save holds come from lua/generated/tables.lua.
local records = require("ome-records")

local applier = records.applier {
    map = require("generated.tables"),
    table = maid.names.table,
    labels = { "boot" },
    -- A file that names a table or a field by an old name gets one line for each old name in lua.log.
    log = maid.log.warn,
}

--- At kamidori.data.layered: a contribution's boot tables, after every layer and before the boot uses them.
function apply_boot(event, contribution)
    return applier:apply(contribution, { built_by = "boot" })
end
