-- Every event arterial-base raises, with its fields, in the order raised. The driver's console reads the list:
--   lua mod=example.arterial-event-log return table.concat(seen, "; ")
local FIELDS = {
  ["arterial.data.layered"] = {},
  ["arterial.data.ready"] = {},
  ["arterial.area.loaded"] = {"area"},
  ["arterial.area.ready"] = {"area"},
  ["arterial.battle.start"] = {"battle", "rule", "opponent"},
  ["arterial.battle.close"] = {"battle", "result", "opponent"},
  ["arterial.experience.award"] = {"character", "amount", "level"},
}

seen = {}

function on_event(event)
  local parts = { event.event }
  for _, name in ipairs(FIELDS[event.event] or {}) do
    parts[#parts + 1] = name .. "=" .. tostring(event[name])
  end
  local line = table.concat(parts, " ")
  seen[#seen + 1] = line
  maid.log.info(line)
end
