-- arterial-base's helpers (Lua scripting spec §9.3): changes made the way the game makes them, by calling its own
-- scripts (spec §10). A helper's writes are this base mod's, so a mod that uses one lists none of them under
-- [lua] writes; the trace names the mod whose handler called the helper. A helper that calls a script runs in a
-- handler the interpreter invoked (a hook, a watch or an event), not in init.lua, ome.session.start or the console.

local g = maid.names.g
local items = maid.names.table("item_owned_counts")
local cards = maid.names.table("card_owned_counts")

local base = {}

local function whole(value, what, low, high)
    if type(value) ~= "number" or value ~= math.floor(value) or value < low or value > high then
        error(string.format("bad argument %s (an integer from %d to %d expected, got %s)", what, low, high,
                            tostring(value)), 3)
    end
    return value
end

---Give the player `count` of item `item`, or take them away with a negative count, as the game does: ADDITEM adds the
---change to item_owned_counts, keeps the count from 0 and pays a count past 99 out as money at the item's selling
---price, keeps a key item (kind 1) to one and copies its count into its story cell, marks the item obtained, and
---records it, with the talent it carries, in the shared collection. The argument cells ADDITEM reads are put back as they were.
---@param item integer an item id, 1 to 1999
---@param count? integer the change, -99 to 99; 1 when nil
---@return integer count the item's count after the change
function base.give_item(item, count)
    whole(item, "#1 to 'give_item'", 1, 1999)
    count = whole(count == nil and 1 or count, "#2 to 'give_item'", -99, 99)
    local item_before, count_before = g.item_arg_id, g.item_arg_count
    g.item_arg_id, g.item_arg_count = item, count
    local ok, err = pcall(maid.vm.call, "ADDITEM")
    g.item_arg_id, g.item_arg_count = item_before, count_before
    if not ok then error(err, 0) end
    return items[item]
end

---Give the player `count` of card `card` (its id is row * 30 + slot, as the card table's), or take them away with a
---negative count, as the game does: ADDCARD adds the change to card_owned_counts, keeps the count from 0 and pays a
---count past 99 out as money at the card's selling price, marks the card obtained, and records it, with its talents,
---in the shared collection. The argument cells ADDCARD reads, and the shared loop index it uses, are put back as they
---were.
---@param card integer a card id, 1 to 5999
---@param count? integer the change, -99 to 99; 1 when nil
---@return integer count the card's count after the change
function base.give_card(card, count)
    whole(card, "#1 to 'give_card'", 1, 5999)
    count = whole(count == nil and 1 or count, "#2 to 'give_card'", -99, 99)
    local row_before, slot_before, count_before = g.card_arg_row, g.card_arg_slot, g.card_arg_count
    local index_before = g.shared_loop_index
    g.card_arg_row, g.card_arg_slot, g.card_arg_count = math.floor(card / 30), card % 30, count
    local ok, err = pcall(maid.vm.call, "ADDCARD")
    g.card_arg_row, g.card_arg_slot, g.card_arg_count = row_before, slot_before, count_before
    g.shared_loop_index = index_before
    if not ok then error(err, 0) end
    return cards[card]
end

return base
