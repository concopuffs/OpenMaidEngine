-- ome-records: checks and applies edits to record tables that a game keeps as parallel arrays (data patches spec
-- §6.4, docs/superpowers/specs/2026-11-17-data-patches-design.md). It names no game and no table: a base mod passes
-- its record map (its lua/generated/tables.lua) and its own access in, and binds the applier to its own events.
--
--   local records = require("ome-records")
--   local applier = records.applier { map = require("generated.tables"), table = maid.names.table, labels = { "boot" } }
--   applier:apply(contribution, { built_by = "boot" })
--
-- A contribution is { mod = id, file = path, data = document }. The document names records by table, decimal id and
-- field: data.unit["761"].stats.max_hp = 150. It may be a Lua table or a read-only view of one (a userdata), so the
-- walk uses pairs and indexing only.
--
-- A table or a field may also be named by an old name, which the map lists (old_tables beside tables, old_fields in a
-- table): it writes the cell its current name does, and config.log, when given, gets one line for each file and old
-- name. A document that names one cell twice, by an old name and the current one, is refused.

local records = { format = 1 }

local MAX_SHOWN = 20
local INT_MIN, INT_MAX = -2147483648, 4294967295

local function is_table(value)
    local kind = type(value)
    return kind == "table" or kind == "userdata"
end

local function describe(value)
    if is_table(value) then return "a table" end
    if type(value) == "string" then return "a string" end
    if type(value) == "boolean" then return "a boolean" end
    return tostring(value)
end

local function sorted_names(map)
    local names = {}
    for name in pairs(map) do names[#names + 1] = name end
    table.sort(names)
    return names
end

-- A decimal key: digits with no sign and no leading zero. Its number, or nil and why it is not one.
local function decimal(key, what)
    if type(key) ~= "string" then
        return nil, what .. " is a key in decimal, not an array's position"
    end
    if key == "0" or key:match("^[1-9]%d*$") then return tonumber(key) end
    if key:match("^0[xX]%x+$") then
        return nil, what .. " is written in decimal: " .. tostring(tonumber(key))
    end
    if key:match("^0%d+$") then
        return nil, what .. " is written without a leading zero: " .. tostring(tonumber(key))
    end
    if key:match("^[+-]") then return nil, what .. " has no sign" end
    return nil, what .. " is a whole number in decimal, not " .. key
end

local Applier = {}
Applier.__index = Applier

--- The applier for one record map: what a base mod builds once, when its tables.lua loads.
---@param config table map: a record map (format 1); table: a global's name to its accessor (maid.names.table);
---  labels: the built_by labels the caller applies at an event of its own; log (optional): a function that takes the
---  line naming an old name a file used, such as maid.log.warn
function records.applier(config)
    if type(config) ~= "table" then error("records.applier takes a table: { map = …, table = …, labels = … }", 2) end
    local map = config.map
    if not is_table(map) or map.format ~= records.format then
        error("records.applier: map is a record map of format " .. records.format .. ", not "
              .. (is_table(map) and tostring(map.format) or describe(map)), 2)
    end
    if type(config.table) ~= "function" then
        error("records.applier: table is the caller's access, a function from a global's name to its accessor", 2)
    end
    if type(config.labels) ~= "table" or config.labels[1] == nil then
        error("records.applier: labels lists the built_by labels the caller applies, such as { \"boot\" }", 2)
    end
    if config.log ~= nil and type(config.log) ~= "function" then
        error("records.applier: log is a function that takes a line, such as maid.log.warn", 2)
    end
    local labels = {}
    for _, label in ipairs(config.labels) do labels[label] = true end
    -- Each row field's column names by index, so a named column given by its number can be refused with its name.
    local named = {}
    for name, spec in pairs(map.tables) do
        for field, field_spec in pairs(spec.fields) do
            if field_spec.columns then
                local by_index = {}
                for column, index in pairs(field_spec.columns) do by_index[index] = column end
                named[name .. "." .. field] = by_index
            end
        end
    end
    return setmetatable({ map = map, access = config.table, labels = labels, named = named, views = {},
                          results = {}, log = config.log }, Applier)
end

-- A table by its name or an old name: its spec and its current name, or nil.
function Applier:table_of(name)
    if type(name) ~= "string" then return nil end
    local spec = self.map.tables[name]
    if spec ~= nil then return spec, name end
    local current = self.map.old_tables and self.map.old_tables[name]
    if current ~= nil then return self.map.tables[current], current end
    return nil
end

-- A table's field by its name or an old name: its spec and its current name, or nil.
local function field_of(spec, field)
    if type(field) ~= "string" then return nil end
    local field_spec = spec.fields[field]
    if field_spec ~= nil then return field_spec, field end
    local current = spec.old_fields and spec.old_fields[field]
    if current ~= nil then return spec.fields[current], current end
    return nil
end

-- A document's problems: how many it has and the first MAX_SHOWN by path, as { path, reason }; and the old names it
-- uses, as sorted lines without the file. Only those MAX_SHOWN are kept, and each old name once: a file may hold
-- 400,000 values, and a list as long costs the session seconds (data patches plan §10).
function Applier:problems(data)
    local count, shown, kept = 0, {}, 0
    local function problem(path, reason)
        count = count + 1
        if kept == MAX_SHOWN and path >= shown[kept][1] then return end
        if kept < MAX_SHOWN then kept = kept + 1 end
        local at = kept
        while at > 1 and path < shown[at - 1][1] do
            shown[at] = shown[at - 1]
            at = at - 1
        end
        shown[at] = { path, reason }
    end
    local tables = self.map.tables
    -- Each cell the document names, by its current spelling, to the paths that name it; and the old names it uses.
    local cells, old_names = {}, {}
    local function cell(path, canonical)
        local paths = cells[canonical]
        if paths == nil then cells[canonical] = { path } else paths[#paths + 1] = path end
    end

    local function value_of(path, kind, value)
        if kind == "integer" then
            if type(value) ~= "number" then
                problem(path, "takes a whole number, not " .. describe(value))
            elseif value ~= math.floor(value) or value < INT_MIN or value > INT_MAX then
                problem(path, "takes a whole number in 32 bits, not " .. tostring(value))
            end
        elseif kind == "float" then
            if type(value) ~= "number" then
                problem(path, "takes a number, not " .. describe(value))
            elseif value ~= value or value == math.huge or value == -math.huge then
                problem(path, "takes a finite number, not " .. tostring(value))
            end
        elseif type(value) ~= "string" then
            problem(path, "takes a string, not " .. describe(value))
        else
            local fits, reason = maid.text_fits(value)
            if not fits then problem(path, reason) end
        end
    end

    local function row_of(name, field, spec, path, row, canonical)
        local named = self.named[name .. "." .. field] or {}
        for column, value in pairs(row) do
            local column_path = path .. "." .. tostring(column)
            if type(column) ~= "string" then
                problem(path, name .. "." .. field .. " takes its columns by name, or by number where a column has no "
                        .. "name, not a whole row as an array")
                return
            end
            local index = spec.columns and spec.columns[column]
            if index == nil then
                local number, reason = decimal(column, "a column")
                if number == nil then
                    problem(column_path, name .. "." .. field .. " has no column " .. column)
                elseif number >= spec.width then
                    problem(column_path, name .. "." .. field .. " has columns 0 to " .. (spec.width - 1))
                elseif named[number] then
                    problem(column_path, "column " .. number .. " is named: write " .. path .. "." .. named[number])
                else
                    index = number
                end
            end
            if index ~= nil then
                if is_table(value) then problem(column_path, "a column takes one value, not a table")
                else
                    value_of(column_path, spec.kind, value)
                    cell(column_path, canonical .. "." .. tostring(column))
                end
            end
        end
    end

    if not is_table(data) then
        problem("(the file)", "a document is a table of record tables, not " .. describe(data))
        return count, shown, {}
    end
    for name, records_of in pairs(data) do
        local spec, current = self:table_of(name)
        local path = tostring(name)
        if spec ~= nil and current ~= name then
            old_names[name .. " is an old name of the table " .. current .. "; write " .. current] = true
        end
        if spec == nil then
            problem(path, "no record table is named " .. path .. " (tables: "
                    .. table.concat(sorted_names(tables), ", ") .. ")")
        elseif not self.labels[spec.built_by] then
            problem(path, name .. " is built by " .. spec.built_by .. ", which this format does not edit")
        elseif not is_table(records_of) then
            problem(path, name .. " takes its records by id: [" .. name .. ".<id>]")
        else
            for key, record in pairs(records_of) do
                local id_path = path .. "." .. tostring(key)
                local id, reason = decimal(key, "a record id")
                if id == nil then
                    problem(id_path, reason)
                elseif id >= spec.records then
                    problem(id_path, name .. " has records 0 to " .. (spec.records - 1))
                elseif not is_table(record) then
                    problem(id_path, "a record is a table of its fields, not " .. describe(record))
                else
                    for field, value in pairs(record) do
                        local field_path = id_path .. "." .. tostring(field)
                        local field_spec, field_name = field_of(spec, field)
                        local canonical = current .. "." .. key .. "." .. tostring(field_name)
                        if field_spec ~= nil and field_name ~= field then
                            old_names[field .. " is an old name of the field " .. current .. "." .. field_name
                                      .. "; write " .. field_name] = true
                        end
                        if field_spec == nil then
                            problem(field_path, name .. " has no field " .. tostring(field))
                        elseif field_spec.state then
                            problem(field_path, name .. "." .. field .. " is saved state: a load brings the saved "
                                    .. "value back, so a file does not set it")
                        elseif field_spec.width == nil then
                            if is_table(value) then problem(field_path, name .. "." .. field .. " takes one value, not a table")
                            else
                                value_of(field_path, field_spec.kind, value)
                                cell(field_path, canonical)
                            end
                        elseif not is_table(value) then
                            problem(field_path, name .. "." .. field .. " is a row of " .. field_spec.width
                                    .. " columns: give its columns (" .. field .. ".<column> = …)")
                        else
                            row_of(current, field_name, field_spec, field_path, value, canonical)
                        end
                    end
                end
            end
        end
    end
    -- One cell named twice, by an old name and the current one (data patches plan §9.1).
    for canonical, paths in pairs(cells) do
        if #paths > 1 then
            table.sort(paths)
            problem(paths[1], table.concat(paths, " and ") .. " name one cell, " .. canonical .. "; write it once, as "
                    .. canonical)
        end
    end
    return count, shown, sorted_names(old_names)
end

-- The kept result of a contribution's check: { ok = true } or { ok = false, message = … }, made once for the applier's
-- life (one Lua session), by the contribution's mod and file.
function Applier:result(contribution)
    if type(contribution) ~= "table" and type(contribution) ~= "userdata" then
        error("a contribution is a table: { mod = …, file = …, data = … }", 3)
    end
    local mod, file = contribution.mod, contribution.file
    if type(mod) ~= "string" or type(file) ~= "string" then
        error("a contribution names its mod and its file as strings", 3)
    end
    local key = mod .. "\n" .. file
    local result = self.results[key]
    if result then return result end
    local count, shown, old_names = self:problems(contribution.data)
    -- One line for each old name the file uses, once in the applier's life (one session).
    if self.log ~= nil then
        for _, line in ipairs(old_names) do self.log(mod .. " " .. file .. ": " .. line) end
    end
    if count == 0 then
        result = { ok = true }
    else
        local lines = { mod .. " " .. file .. ": " .. count .. (count == 1 and " problem" or " problems")
                        .. ", so nothing of it is applied" }
        for _, found in ipairs(shown) do lines[#lines + 1] = "  " .. found[1] .. ": " .. found[2] end
        if count > MAX_SHOWN then lines[#lines + 1] = "  and " .. (count - MAX_SHOWN) .. " more" end
        result = { ok = false, message = table.concat(lines, "\n") }
    end
    self.results[key] = result
    return result
end

--- Whether a contribution's document passes every check: true, or false and the message apply raises.
function Applier:check(contribution)
    local result = self:result(contribution)
    if result.ok then return true end
    return false, result.message
end

function Applier:view(global)
    local view = self.views[global]
    if view == nil then
        view = self.access(global)
        self.views[global] = view
    end
    return view
end

--- Write a contribution's parts whose table is built by options.built_by, after checking the whole document. A
--- document that fails raises one error, at its first apply, and writes nothing; each later apply of it returns 0.
---@return integer written the number of cells written
function Applier:apply(contribution, options)
    local label = type(options) == "table" and options.built_by or nil
    if type(label) ~= "string" or not self.labels[label] then
        error("apply: options.built_by is one of the applier's labels", 2)
    end
    local result = self:result(contribution)
    if not result.ok then
        if result.reported then return 0 end
        result.reported = true
        error(result.message, 0)
    end
    local written = 0
    for name, records_of in pairs(contribution.data) do
        local spec = self:table_of(name)
        if spec.built_by == label then
            for key, record in pairs(records_of) do
                local id = tonumber(key)
                for field, value in pairs(record) do
                    local field_spec = field_of(spec, field)
                    local view = self:view(field_spec.global)
                    if field_spec.width == nil then
                        view[id] = value
                        written = written + 1
                    else
                        local row = view[id]
                        local columns = field_spec.columns
                        for column, cell in pairs(value) do
                            row[(columns and columns[column]) or tonumber(column)] = cell
                            written = written + 1
                        end
                    end
                end
            end
        end
    end
    return written
end

return records
