-- himegari-base's helpers (Lua scripting spec §9.3): changes made the way the game makes them, by calling its own
-- scripts (spec §10). A helper's writes are this base mod's, so a mod that uses one lists none of them under
-- [lua] writes; the trace names the mod whose handler called the helper. A helper that calls a script runs in a
-- handler the interpreter invoked (a hook, a watch or an event), not in init.lua, ome.session.start or the console.

local g = maid.names.g
local counts = maid.names.table("item_inventory_counts")

local base = {}

local function whole(value, what, low, high)
    if type(value) ~= "number" or value ~= math.floor(value) or value < low or value > high then
        error(string.format("bad argument %s (an integer from %d to %d expected, got %s)", what, low, high,
                            tostring(value)), 3)
    end
    return value
end

---Give the player `count` of item `item`, or take them away with a negative count, as the game does: ADDITEM adds the
---change to item_inventory_counts and keeps the count from 0 to 99, marks an item gained as obtained, and sets a key
---item's story flag. The argument cells ADDITEM reads are put back as they were.
---@param item integer an item id, 1 to 999
---@param count? integer the change, -99 to 99; 1 when nil
---@return integer count the item's count after the change
function base.give_item(item, count)
    whole(item, "#1 to 'give_item'", 1, 999)
    count = whole(count == nil and 1 or count, "#2 to 'give_item'", -99, 99)
    local item_before, change_before = g.current_item_id, g.current_item_count_change
    g.current_item_id, g.current_item_count_change = item, count
    local ok, err = pcall(maid.vm.call, "ADDITEM")
    g.current_item_id, g.current_item_count_change = item_before, change_before
    if not ok then error(err, 0) end
    return counts[item]
end

return base
