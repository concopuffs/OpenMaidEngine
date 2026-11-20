-- The arterial.tables format's handlers (data patches spec §6, docs/superpowers/specs/2026-11-17-data-patches-design.md).
-- Hand-written. A document names records by table, decimal id and field, by the registry's names, as OMS does:
-- card["31"].cost is card[31].cost. A table the record map keys by a scope is named through it: area["1"].unit["2"].deck
-- is area 1's unit 2's deck. ome-records checks the whole document against the record map before its first write, and
-- writes through this base mod's own accessors. What builds each table, its scope, and which fields a save holds come
-- from lua/generated/tables.lua.
local records = require("ome-records")

local applier = records.applier {
    map = require("generated.tables"),
    table = maid.names.table,
    labels = { "boot", "area" },
    -- The event each label is applied at: a document with a part at an event this install does not raise is refused
    -- whole, rather than applied in part (spec §6.2).
    events = { boot = "arterial.data.layered", area = "arterial.area.loaded" },
    -- A file that names a table or a field by an old name gets one line for each old name in lua.log.
    log = maid.log.warn,
}

--- At arterial.data.layered: a contribution's boot tables, after every layer and before the boot uses them.
function apply_boot(event, contribution)
    return applier:apply(contribution, { built_by = "boot" })
end

--- At arterial.area.loaded: a contribution's part for the area whose loaders have just run, each time they run.
function apply_area(event, contribution)
    return applier:apply(contribution, { built_by = "area", scope = { area = event.area } })
end
