const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_writei16 = buffer.writei16
const buffer_len = buffer.len
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_readi16 = buffer.readi16
const function Roster_serialize_binary(value: Roster): Loom.Serialized
  local size = 4 + #value.positions * 6
  size += 4
  for i = 1, #value.names do
    const names_element = value.names[i]
    size += 4 + #names_element
  end
  const b = buffer_create(size)
  buffer_writeu32(b, 0, #value.names)
  local offset = 4
  for i = 1, #value.names do
    const names_element = value.names[i]
    const names_element_length = #names_element
    buffer_writeu32(b, offset, names_element_length)
    offset += 4
    buffer_writestring(b, offset, names_element)
    offset += names_element_length
  end
  buffer_writeu32(b, offset, #value.positions)
  offset += 4
  for i = 1, #value.positions do
    const positions_element = value.positions[i]
    buffer_writei16(b, offset, positions_element.X)
    offset += 2
    buffer_writei16(b, offset, positions_element.Y)
    offset += 2
    buffer_writei16(b, offset, positions_element.Z)
    offset += 2
  end
  return { buffer = b }
end
const function Roster_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Roster, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 8 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const names_count = buffer_readu32(b, 0)
  local offset = 4
  const names = {}
  for i = 1, names_count do
    const names_element_length = buffer_readu32(b, offset)
    offset += 4
    if buffer_len(b) < offset + names_element_length then
      return { ok = false, error = { kind = "invalid_length", field = "names[]" } }
    end
    const names_element = buffer_readstring(b, offset, names_element_length)
    offset += names_element_length
    names[i] = names_element
  end
  const positions_count = buffer_readu32(b, offset)
  offset += 4
  if buffer_len(b) < offset + positions_count * 6 then
    return { ok = false, error = { kind = "invalid_length", field = "positions" } }
  end
  const positions = {}
  for i = 1, positions_count do
    const positions_element_x = buffer_readi16(b, offset)
    offset += 2
    const positions_element_y = buffer_readi16(b, offset)
    offset += 2
    const positions_element_z = buffer_readi16(b, offset)
    offset += 2
    positions[i] = Vector3.new(positions_element_x, positions_element_y, positions_element_z)
  end
  return { ok = true, value = { names = names, positions = positions } }
end
type Roster = {
  read names: { string },
  read positions: { Vector3 },
}
const function _trigger_usage(): ()
  const payload = Roster_serialize_binary((nil :: never) :: Roster)
  const restored = Roster_deserialize_binary(payload)
end