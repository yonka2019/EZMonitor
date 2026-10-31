local server = libs.server
local timer = libs.timer
local fs = libs.fs

-- Written by EZMonitor each second: time, ram, then one "app" line per app
local LIVE = os.getenv("ProgramData") .. "\EZMonitor\live.txt"
local tid

local function update()
  local ok, text = pcall(function() return fs.exists(LIVE) and fs.read(LIVE) or "" end)
  if not ok then return end -- file being replaced: keep what is shown
  local time, ram, items = 0, "", {}
  for line in text:gmatch("[^\r\n]+") do
    local key, a, b = line:match("^(%a+)\t([^\t]*)\t?(.*)$")
    if key == "time" then time = tonumber(a) or 0
    elseif key == "ram" then ram = a
    elseif key == "app" then table.insert(items, { type = "item", text = (#items + 1) .. ". " .. a .. "\n" .. b }) end
  end
  if os.time() - time > 5 then
    server.update({ id = "ram", text = "EZMonitor not running" })
    server.update({ id = "apps", children = {} })
    return
  end
  server.update({ id = "ram", text = "RAM " .. ram })
  server.update({ id = "apps", children = items })
end

events.focus = function()
  update()
  tid = timer.interval(update, 2000)
end

events.blur = function()
  if tid then timer.cancel(tid) end
  tid = nil
end

actions.refresh = function()
  update()
end
