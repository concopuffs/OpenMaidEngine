-- Random enemies, a tech demo. When a stage's loader has written its enemy slots, and before FIELD places them, each
-- ordinary enemy becomes one drawn from every ordinary enemy of the game, early stage or late. Bosses and named
-- characters stay, so a stage can still be won. A playthrough draws a seed at its first stage and keeps it in
-- maid.state, which its saves keep: a stage gets the same enemies each time that playthrough reaches it, a load
-- included, and a new game draws another seed.

local game = require("himegari-base")

local MODULUS = 2147483647   -- 2^31 - 1: the Park-Miller generator's modulus
local SLOTS = 30             -- stage_enemy_unit_id[0..29]; slot 0 is ADDEN's own unit, so 1..29 are the stage's

local pool                   -- the ordinary enemies; Lua globals last one session, so each session builds it again

local function ordinary_enemies()
  local listed = game.table("unit_enemy_info_listed")
  local boss = game.table("unit_boss_class")
  local figure = game.table("unit_battle_sprite_asset_id")
  local units = {}
  for unit = 1, #listed - 1 do
    if listed[unit] ~= 0 and boss[unit] == 0 and figure[unit] > 0 then units[#units + 1] = unit end
  end
  return units
end

-- One draw for (seed, stage, slot), from 1 to count: a few Park-Miller steps from a mix of the three. Lua's numbers are
-- doubles, which hold whole numbers exactly below 2^53, and 48271 * (2^31 - 1) is below that.
local function pick(seed, stage, slot, count)
  local x = (seed + stage * 7919 + slot * 104729) % MODULUS
  if x == 0 then x = 1 end
  for _ = 1, 4 do x = (x * 48271) % MODULUS end
  return x % count + 1
end

function on_stage_loaded(event)
  if maid.state.seed == nil then
    maid.state.seed = math.random(1, MODULUS - 1)   -- a new playthrough: under the driver's rand-seed, a known one
  end
  pool = pool or ordinary_enemies()
  local units = game.table("stage_enemy_unit_id")
  local boss = game.table("unit_boss_class")
  local names = game.table("unit_display_names")
  local drawn = {}
  for slot = 1, SLOTS - 1 do
    local unit = units[slot]
    if unit ~= 0 and boss[unit] == 0 then
      local new = pool[pick(maid.state.seed, event.stage, slot, #pool)]
      units[slot] = new
      drawn[#drawn + 1] = string.format("%d:%s>%s", slot, names[unit], names[new])
    end
  end
  maid.state.last = { stage = event.stage, drawn = #drawn }
  maid.log.info(string.format("stage %d (seed %d): %s", event.stage, maid.state.seed, table.concat(drawn, " ")))
end

-- For the driver's lua step: the playthrough's seed and the last stage's slots as FIELD will place them.
function describe()
  local units = game.table("stage_enemy_unit_id")
  local slots = {}
  for slot = 1, SLOTS - 1 do
    if units[slot] ~= 0 then slots[#slots + 1] = string.format("%d=%d", slot, units[slot]) end
  end
  local last = maid.state.last or {}
  return string.format("seed=%s stage=%s drawn=%s %s", tostring(maid.state.seed), tostring(last.stage),
                       tostring(last.drawn), table.concat(slots, " "))
end
