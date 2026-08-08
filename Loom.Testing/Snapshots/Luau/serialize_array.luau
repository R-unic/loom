const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writeu8 = buffer.writeu8
const buffer_len = buffer.len
const buffer_readu32 = buffer.readu32
const buffer_readu8 = buffer.readu8
const function Scores_serialize_binary(value: Scores): Loom.Serialized
  const b = buffer_create(4 + #value.values)
  buffer_writeu32(b, 0, #value.values)
  local offset = 4
  for i = 1, #value.values do
    const values_item = value.values[i]
    buffer_writeu8(b, offset, values_item)
    offset += 1
  end
  return { buffer = b }
end
const function Scores_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Scores, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 4 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const values_count = buffer_readu32(b, 0)
  local offset = 4
  if buffer_len(b) < offset + values_count then
    return { ok = false, error = { kind = "invalid_length", field = "values" } }
  end
  const values = {}
  for i = 1, values_count do
    const read_0 = buffer_readu8(b, offset)
    offset += 1
    values[i] = read_0
  end
  return { ok = true, value = { values = values } }
end
type Scores = {
  read values: { number },
}
const function _trigger_usage(): ()
  const payload = Scores_serialize_binary((nil :: never) :: Scores)
  const restored = Scores_deserialize_binary(payload)
end