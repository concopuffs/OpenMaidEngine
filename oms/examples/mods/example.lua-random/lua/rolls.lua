-- Lua scripting gate 11 (docs/modding/lua.md, "Random numbers"). Each time the prologue starts, roll for a few
-- items in a pairs loop, so the order the loop visits them decides which draw each one gets, then roll luck and a d6.
-- Under the driver's rand-seed every run rolls the same; without one the rolls differ from run to run.
local weights = { sword = 3, shield = 2, ring = 5, cape = 1, boots = 4, helm = 6, lance = 2, orb = 9 }

function on_prologue(ctx)
  local rolls = {}
  for item, weight in pairs(weights) do
    rolls[#rolls + 1] = item .. "=" .. math.random(weight * 10)
  end
  rolls[#rolls + 1] = string.format("luck=%.6f", math.random())
  rolls[#rolls + 1] = "d6=" .. math.random(6)
  maid.state.rolls = table.concat(rolls, " ")
  maid.state.entered = (maid.state.entered or 0) + 1
  maid.log.info("rolled " .. maid.state.rolls)
end

-- For the driver's lua step: lua mod=example.lua-random return describe()
function describe()
  return string.format("entered=%d %s", maid.state.entered or 0, maid.state.rolls or "")
end
