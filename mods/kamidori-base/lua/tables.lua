-- The kamidori.tables format's handlers (data patches spec §6, docs/superpowers/specs/2026-11-17-data-patches-design.md).
-- Hand-written. A document names records by table, decimal id and field, by the registry's names, as OMS does:
-- unit["761"].stats.max_hp is unit[761].stats.max_hp. A table the record map keys by a scope is named through it:
-- stage["12"].enemy["3"].unit_id is stage 12's enemy slot 3. ome-records checks the whole document against the
-- record map before its first write, and writes through this base mod's own accessors. What builds each table, its
-- scope, and which fields a save holds come from lua/generated/tables.lua.
local records = require("ome-records")

local applier = records.applier {
    map = require("generated.tables"),
    table = maid.names.table,
    labels = { "boot", "stage" },
    -- The event each label is applied at: a document with a part at an event this install does not raise is refused
    -- whole, rather than applied in part (spec §6.2).
    events = { boot = "kamidori.data.layered", stage = "kamidori.stage.loaded" },
    -- A file that names a table or a field by an old name gets one line for each old name in lua.log.
    log = maid.log.warn,
}

--- At kamidori.data.layered: a contribution's boot tables, after every layer and before the boot uses them.
function apply_boot(event, contribution)
    return applier:apply(contribution, { built_by = "boot" })
end

--- At kamidori.stage.loaded: a contribution's part for the stage whose loaders have just run, each time they run.
function apply_stage(event, contribution)
    return applier:apply(contribution, { built_by = "stage", scope = { stage = event.stage } })
end
