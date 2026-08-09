const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const data_store_service = game:GetService("DataStoreService")
const function load(key: string): Loom.Result<unknown, Loom.RobloxError>
  const _ok, _value = xpcall(data_store_service.GetDataStore, Loom.roblox_error, data_store_service, "players")
  const _result = if _ok then { ok = true, value = _value } else { ok = false, error = _value }
  if not _result.ok then
    return _result
  end
  const store = _result.value
  const _ok_1, _value_1 = xpcall(store.GetAsync, Loom.roblox_error, store, key)
  const _result_1 = if _ok_1 then { ok = true, value = _value_1 } else { ok = false, error = _value_1 }
  if not _result_1.ok then
    return _result_1
  end
  const value = _result_1.value
  return { ok = true, value = value }
end
const outcome = load("player_1")
print(if outcome.ok then "loaded" else outcome.error.message)