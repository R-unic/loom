const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu8 = buffer.writeu8
const buffer_writebits = buffer.writebits
const buffer_writei16 = buffer.writei16
const buffer_len = buffer.len
const buffer_readu8 = buffer.readu8
const buffer_readbits = buffer.readbits
const buffer_readi16 = buffer.readi16
const function Entity_serialize_binary(value: Entity): Loom.Serialized
  const b = buffer_create(2 + (if value.target ~= nil then 2 else 0))
  buffer_writeu8(b, 1, value.id)
  buffer_writebits(b, 0, 1, if value.target ~= nil then 1 else 0)
  local offset = 2
  if value.target ~= nil then
    buffer_writei16(b, offset, value.target)
    offset += 2
  end
  return { buffer = b }
end
const function Entity_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Entity, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 2 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const target_present = buffer_readbits(b, 0, 1) == 1
  local target = nil
  local offset = 2
  if target_present then
    if buffer_len(b) < offset + 2 then
      return { ok = false, error = { kind = "truncated", field = "target" } }
    end
    const read_0 = buffer_readi16(b, offset)
    offset += 2
    target = read_0
  end
  return { ok = true, value = { id = buffer_readu8(b, 1), target = target } }
end
type Entity = {
  read id: number,
  read target: number?,
}
const function _trigger_usage(): ()
  const payload = Entity_serialize_binary((nil :: never) :: Entity)
  const restored = Entity_deserialize_binary(payload)
end