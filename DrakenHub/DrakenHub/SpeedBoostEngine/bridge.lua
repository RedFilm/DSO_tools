--------------------------------------------------
-- DrakenHub SpeedBoost bridge (runs inside CE / SpeedBoost.exe)
-- Keeps CE memory-record core; DrakenHub UI talks via IPC files.
--------------------------------------------------

local PROCESS_NAME = "dro_client64.exe"
local DESC_SPEED = "speed"
local DESC_CAST  = "cast"

local BOOST_BASE_VALUE = 6
local BOOST_MAX_VALUE = 50
local BOOST_STEP = 0.1
local BOOST_DISPLAY_MIN = 30
local BOOST_DISPLAY_MAX = BOOST_DISPLAY_MIN
  + math.floor(((BOOST_MAX_VALUE - BOOST_BASE_VALUE) / BOOST_STEP) + 0.5)

local CLASS_PROFILES = {
  { name = "global", speedOff = 4, speedOn = 6 },
  { name = "dragonknight", speedOff = 4, speedOn = -1, castOff = 0.5, castOn = -1, sliderEnabled = false },
  { name = "spellweaver", speedOff = 4, speedOn = 6 },
  { name = "ranger", speedOff = 4, speedOn = 6 },
  { name = "mechanicus", speedOff = 4, speedOn = -1, castOff = 0.5, castOn = -1, sliderEnabled = false }
}

local hackEnabled = false
local monitorTimer = nil
local cmdTimer = nil
local reconnectInProgress = false
local lastValidPids = {}
local activeProfile = CLASS_PROFILES[1]
local boostDisplay = BOOST_DISPLAY_MIN
local lastCmdId = 0
local uiHidden = false

local function ipcDir()
  local base = os.getenv("LOCALAPPDATA") or os.getenv("TEMP") or "."
  return base .. "\\DrakenHub\\SpeedBoostIpc"
end

-- NEVER use os.execute here: on Windows it flashes a visible cmd/WindowsTerminal
-- window. That was the "window opens and closes every few seconds" bug (monitor tick
-- -> writeStatus/bridgeLog -> ensureDir -> os.execute mkdir).
local ipcDirReady = false
local function ensureDir(path)
  if ipcDirReady then return true end
  local ok = false
  if createDirectory then
    -- create each segment (CE createDirectory is often single-level)
    local built = path:match("^%a:") and path:sub(1, 2) or ""
    local rest = path:match("^%a:(.*)$") or path
    for part in string.gmatch(rest, "[^\\/]+") do
      built = built .. "\\" .. part
      pcall(createDirectory, built)
    end
    ok = true
  end
  -- Probe: if we can open/create a marker, dir is usable (DrakenHub host also mkdirs).
  local probe = io.open(path .. "\\.bridge_ok", "w")
  if probe then
    probe:write("1")
    probe:close()
    ok = true
  end
  ipcDirReady = ok
  return ok
end

local function readFile(path)
  local f = io.open(path, "rb")
  if not f then return nil end
  local data = f:read("*a")
  f:close()
  return data
end

local function writeFile(path, data)
  local tmp = path .. ".tmp"
  local f = io.open(tmp, "wb")
  if not f then return false end
  f:write(data)
  f:close()
  os.remove(path)
  local ok = os.rename(tmp, path)
  if not ok then
    -- fallback overwrite
    local f2 = io.open(path, "wb")
    if not f2 then return false end
    f2:write(data)
    f2:close()
  end
  return true
end

local function jsonEscape(s)
  s = tostring(s or "")
  s = s:gsub("\\", "\\\\"):gsub('"', '\\"'):gsub("\r", "\\r"):gsub("\n", "\\n")
  return s
end

local function bridgeLog(msg)
  local path = ipcDir() .. "\\bridge.log"
  local f = io.open(path, "a")
  if not f then return end
  f:write(os.date("%H:%M:%S") .. " " .. tostring(msg) .. "\n")
  f:close()
end

local function hideCeUi(force)
  if uiHidden and not force then return end
  pcall(function()
    local mf = getMainForm()
    if mf then
      mf.Visible = false
      pcall(function() mf.ShowInTaskBar = "stNever" end)
      pcall(function() mf.AlphaBlend = true end)
      pcall(function() mf.AlphaBlendValue = 0 end)
    end
  end)
  pcall(function()
    local mv = getMemoryViewForm()
    if mv then mv.Visible = false end
  end)
  pcall(function()
    if getLuaEngine then
      local le = getLuaEngine()
      if le then le.Visible = false end
    end
  end)
  uiHidden = true
end

local function writeStatus(fields)
  local parts = {}
  for k, v in pairs(fields) do
    if type(v) == "string" then
      parts[#parts + 1] = string.format('"%s":"%s"', k, jsonEscape(v))
    elseif type(v) == "boolean" then
      parts[#parts + 1] = string.format('"%s":%s', k, v and "true" or "false")
    else
      parts[#parts + 1] = string.format('"%s":%s', k, tostring(v))
    end
  end
  writeFile(ipcDir() .. "\\status.json", "{" .. table.concat(parts, ",") .. "}")
end

local function parseJsonSimple(text)
  -- minimal extractor for {"id":1,"op":"enable","profile":0,"boost":30}
  local t = {}
  t.id = tonumber(text:match('"id"%s*:%s*(%d+)')) or 0
  t.op = text:match('"op"%s*:%s*"([^"]*)"') or ""
  t.profile = tonumber(text:match('"profile"%s*:%s*(%-?%d+)')) or 0
  t.boost = tonumber(text:match('"boost"%s*:%s*(%-?%d+)')) or BOOST_DISPLAY_MIN
  t.profileId = text:match('"profileId"%s*:%s*"([^"]*)"')
  return t
end

--------------------------------------------------
-- Walk the live pointer chain (verified against working CE memrec).
-- Final offset must be F8 (not FC) — FC is +4 and hits a dead float that
-- looks like ~4.2 but does not control attack speed.
-- MR walk from THREADSTACK0-0x310: +0,+288,+10,+670,+30,+0,+F8
--------------------------------------------------

local SPEED_OFFSETS = { 0x0, 0x288, 0x10, 0x670, 0x30, 0x0, 0xF8 }
local CAST_OFFSETS = SPEED_OFFSETS

local function readPtr(addr)
  if not addr or addr == 0 then return 0 end
  local ok, v = pcall(readQword, addr)
  if ok and v and v ~= 0 then return v end
  return 0
end

local function readF(addr)
  if not addr or addr == 0 then return nil end
  local ok, v = pcall(readFloat, addr)
  if ok then return v end
  return nil
end

local function writeF(addr, value)
  if not addr or addr == 0 or value == nil then return false end
  local ok = pcall(writeFloat, addr, value)
  return ok and true or false
end

local function resolveChain(offsets)
  local ts = getAddress("THREADSTACK0")
  if not ts or ts == 0 then return 0 end
  local cur = ts - 0x310
  for i = 1, #offsets - 1 do
    cur = readPtr(cur + offsets[i])
    if cur == 0 then return 0 end
  end
  return cur + offsets[#offsets]
end

local function selectedSpeedOn(profile)
  if profile.sliderEnabled == false then return profile.speedOn end
  local value = BOOST_BASE_VALUE + ((boostDisplay - BOOST_DISPLAY_MIN) * BOOST_STEP)
  return tonumber(string.format("%.1f", value))
end

local function recordsAreValid(profile)
  local addr = resolveChain(SPEED_OFFSETS)
  local val = readF(addr)
  local ok = addr ~= 0 and val ~= nil and val > 0.05 and val < 500 and val == val
  if not ok then
    local ts = 0
    pcall(function() ts = getAddress("THREADSTACK0") or 0 end)
    bridgeLog(string.format(
      "resolve fail TS0=0x%X addr=0x%X float=%s",
      ts, addr or 0, tostring(val)))
    return false
  end
  return true
end

local lastApplyLog = 0

local function applyProfileValues(profile, enabled, quiet)
  local speedAddr = resolveChain(SPEED_OFFSETS)
  if speedAddr == 0 then return false end
  local speedVal = enabled and selectedSpeedOn(profile) or profile.speedOff
  if speedVal ~= nil and speedVal >= 0 then
    writeF(speedAddr, speedVal)
  end
  if profile.castOn ~= nil then
    local castAddr = resolveChain(CAST_OFFSETS)
    local castVal = enabled and profile.castOn or profile.castOff
    if castAddr ~= 0 and castVal ~= nil and castVal >= 0 then
      writeF(castAddr, castVal)
    end
  end
  -- Drive CE memrecs when present (freeze semantics like standalone).
  pcall(function()
    local ml = getAddressList()
    if not ml then return end
    for i = 0, ml.Count - 1 do
      local rec = ml.getMemoryRecord(i)
      if rec and rec.Description then
        local d = tostring(rec.Description):lower():gsub('"', "")
        if d == "speed" and speedVal ~= nil and speedVal >= 0 then
          rec.Active = enabled
          if enabled then rec.Value = tostring(speedVal) end
        elseif d == "cast" and profile.castOn ~= nil then
          local castVal = enabled and profile.castOn or profile.castOff
          rec.Active = enabled and castVal ~= nil and castVal >= 0
          if rec.Active then rec.Value = tostring(castVal) end
        end
      end
    end
  end)
  if not quiet then
    local now = os.clock()
    if now - lastApplyLog > 1.0 then
      lastApplyLog = now
      local after = readF(speedAddr)
      bridgeLog(string.format("apply enabled=%s speedAddr=0x%X wrote=%s readback=%s",
        tostring(enabled), speedAddr, tostring(speedVal), tostring(after)))
    end
  end
  return true
end

local function getAllGameProcesses()
  local result = {}
  local list = createStringlist()
  getProcesslist(list)
  for i = 0, list.Count - 1 do
    local line = list[i]
    local pidHex, name = line:match("^([%x]+)%-(.+)$")
    if pidHex and name and name:lower() == PROCESS_NAME:lower() then
      local pid = tonumber(pidHex, 16)
      if pid and pid > 0 then table.insert(result, pid) end
    end
  end
  list.destroy()
  return result
end

-- openProcess() makes CE flash its main window. Never call it if already attached.
local function openProcessByPID(pid)
  local opened = 0
  pcall(function() opened = getOpenedProcessID() or 0 end)
  if opened == pid then
    return true
  end

  bridgeLog("openProcess pid=" .. tostring(pid) .. " (was=" .. tostring(opened) .. ")")
  local ok = pcall(function() openProcess(pid) end)
  -- CE shows UI on attach — hide again immediately.
  uiHidden = false
  hideCeUi(true)
  if not ok then return false end
  local now = 0
  pcall(function() now = getOpenedProcessID() or 0 end)
  return now == pid
end

local function isPidAlive(pid)
  for _, p in ipairs(getAllGameProcesses()) do
    if p == pid then return true end
  end
  return false
end

-- Prefer staying on the current CE attachment; only scan/open when needed.
local function applyToAllValidProcesses(profile, enabled)
  local opened = 0
  pcall(function() opened = getOpenedProcessID() or 0 end)

  if opened ~= 0 and isPidAlive(opened) and recordsAreValid(profile) then
    applyProfileValues(profile, enabled)
    lastValidPids = { opened }
    return 1
  end

  local valid = {}
  for _, pid in ipairs(getAllGameProcesses()) do
    if openProcessByPID(pid) and recordsAreValid(profile) then
      table.insert(valid, pid)
      applyProfileValues(profile, enabled)
      -- One valid client is enough; stop scanning (avoids more openProcess flashes).
      lastValidPids = valid
      return 1
    end
  end

  lastValidPids = {}
  return 0
end

local function profileByIdOrIndex(idOrIndex)
  if type(idOrIndex) == "string" then
    for _, p in ipairs(CLASS_PROFILES) do
      if p.name == idOrIndex then return p end
    end
  end
  local index = tonumber(idOrIndex) or 0
  return CLASS_PROFILES[index + 1] or CLASS_PROFILES[1]
end

local function setActiveStatus(count)
  if count == 0 then
    writeStatus({
      ok = true,
      enabled = hackEnabled,
      connected = 0,
      message = "not_connected",
      boost = boostDisplay,
      profile = activeProfile.name
    })
  else
    writeStatus({
      ok = true,
      enabled = hackEnabled,
      connected = count,
      message = "connected",
      boost = boostDisplay,
      profile = activeProfile.name
    })
  end
end

local function reconnectAndApply()
  if not hackEnabled or reconnectInProgress then return end
  reconnectInProgress = true
  local count = applyToAllValidProcesses(activeProfile, true)
  setActiveStatus(count)
  -- Keep CE hidden; openProcess may have shown it.
  uiHidden = false
  hideCeUi(true)
  -- Back off while not connected so we do not spam openProcess every 2s.
  if monitorTimer then
    monitorTimer.Interval = (count > 0) and 2000 or 5000
  end
  bridgeLog("monitor tick count=" .. tostring(count) .. " interval=" .. tostring(monitorTimer and monitorTimer.Interval))
  reconnectInProgress = false
end

local freezeTimer = nil

local function freezeTick()
  if not hackEnabled or reconnectInProgress then return end
  if #(lastValidPids or {}) == 0 then return end
  -- High-rate rewrite emulates CE memrec freeze.
  applyProfileValues(activeProfile, true, true)
end

local function startMonitor()
  if monitorTimer then
    monitorTimer.Enabled = true
  else
    monitorTimer = createTimer(getMainForm())
    monitorTimer.Interval = 2000
    monitorTimer.OnTimer = function() reconnectAndApply() end
    monitorTimer.Enabled = true
  end
  if freezeTimer then
    freezeTimer.Enabled = true
  else
    freezeTimer = createTimer(getMainForm())
    freezeTimer.Interval = 50
    freezeTimer.OnTimer = function() freezeTick() end
    freezeTimer.Enabled = true
  end
end

local function stopMonitor()
  if monitorTimer then monitorTimer.Enabled = false end
  if freezeTimer then freezeTimer.Enabled = false end
end

local function enableBoost()
  hackEnabled = true
  local count = applyToAllValidProcesses(activeProfile, true)
  startMonitor()
  setActiveStatus(count)
  return count
end

local function disableBoost()
  stopMonitor()
  applyToAllValidProcesses(activeProfile, false)
  hackEnabled = false
  lastValidPids = {}
  writeStatus({
    ok = true,
    enabled = false,
    connected = 0,
    message = "disabled",
    boost = boostDisplay,
    profile = activeProfile.name
  })
end

local function withCmdId(fields, cmd)
  fields.id = tonumber(cmd and cmd.id) or 0
  fields.engine = fields.engine or "ready"
  return fields
end

local function handleCommand(cmd)
  if cmd.id ~= 0 and cmd.id == lastCmdId then
    bridgeLog("dup cmd id=" .. tostring(cmd.id) .. " op=" .. tostring(cmd.op))
    return
  end
  lastCmdId = cmd.id
  bridgeLog("cmd id=" .. tostring(cmd.id) .. " op=" .. tostring(cmd.op))

  if cmd.op == "ping" then
    writeStatus(withCmdId({
      ok = true,
      enabled = hackEnabled,
      connected = #(lastValidPids or {}),
      message = hackEnabled and (#(lastValidPids or {}) > 0 and "connected" or "not_connected") or "disabled",
      boost = boostDisplay,
      profile = activeProfile.name
    }, cmd))
    return
  end

  if cmd.op == "quit" then
    disableBoost()
    writeStatus(withCmdId({ ok = true, enabled = false, connected = 0, message = "quit", engine = "stopped" }, cmd))
    closeCE()
    return
  end

  if cmd.profileId and cmd.profileId ~= "" then
    activeProfile = profileByIdOrIndex(cmd.profileId)
  elseif cmd.profile ~= nil then
    activeProfile = profileByIdOrIndex(cmd.profile)
  end

  if cmd.boost then
    boostDisplay = math.max(BOOST_DISPLAY_MIN, math.min(BOOST_DISPLAY_MAX, tonumber(cmd.boost) or BOOST_DISPLAY_MIN))
  end

  if cmd.op == "configure" then
    if hackEnabled then
      local count = applyToAllValidProcesses(activeProfile, true)
      setActiveStatus(count)
      -- stamp last command id
      writeStatus(withCmdId({
        ok = true,
        enabled = true,
        connected = count,
        message = count > 0 and "connected" or "not_connected",
        boost = boostDisplay,
        profile = activeProfile.name
      }, cmd))
    else
      writeStatus(withCmdId({
        ok = true,
        enabled = false,
        connected = 0,
        message = "disabled",
        boost = boostDisplay,
        profile = activeProfile.name
      }, cmd))
    end
    return
  end

  if cmd.op == "enable" then
    local count = enableBoost()
    writeStatus(withCmdId({
      ok = true,
      enabled = true,
      connected = count or 0,
      message = (count or 0) > 0 and "connected" or "not_connected",
      boost = boostDisplay,
      profile = activeProfile.name
    }, cmd))
    return
  end

  if cmd.op == "disable" then
    disableBoost()
    writeStatus(withCmdId({
      ok = true,
      enabled = false,
      connected = 0,
      message = "disabled",
      boost = boostDisplay,
      profile = activeProfile.name
    }, cmd))
    return
  end

  if cmd.op == "toggle" then
    if hackEnabled then
      disableBoost()
      writeStatus(withCmdId({
        ok = true, enabled = false, connected = 0, message = "disabled",
        boost = boostDisplay, profile = activeProfile.name
      }, cmd))
    else
      local count = enableBoost()
      writeStatus(withCmdId({
        ok = true, enabled = true, connected = count or 0,
        message = (count or 0) > 0 and "connected" or "not_connected",
        boost = boostDisplay, profile = activeProfile.name
      }, cmd))
    end
    return
  end
end

local function pollCommands()
  local path = ipcDir() .. "\\command.json"
  local data = readFile(path)
  if not data or data == "" then return end
  os.remove(path)
  local ok, cmd = pcall(parseJsonSimple, data)
  if ok and cmd and cmd.op then
    local ok2, err = pcall(handleCommand, cmd)
    if not ok2 then
      writeStatus({ ok = false, message = "error", error = tostring(err), engine = "ready" })
    end
  end
end

--------------------------------------------------
-- boot
--------------------------------------------------

ensureDir(ipcDir())
bridgeLog("bridge boot pid-ish start")
hideCeUi()

writeStatus({
  ok = true,
  enabled = false,
  connected = 0,
  message = "disabled",
  engine = "ready",
  boost = boostDisplay,
  profile = activeProfile.name
})
bridgeLog("bridge ready status written")

cmdTimer = createTimer(getMainForm())
cmdTimer.Interval = 150
cmdTimer.OnTimer = function()
  pollCommands()
end
cmdTimer.Enabled = true

-- Re-assert hide a few times during CE startup, then stop (no permanent flicker).
local hideAttempts = 0
local hideTimer = createTimer(getMainForm())
hideTimer.Interval = 300
hideTimer.OnTimer = function()
  uiHidden = false
  hideCeUi()
  hideAttempts = hideAttempts + 1
  if hideAttempts >= 10 then
    hideTimer.Enabled = false
  end
end
hideTimer.Enabled = true
