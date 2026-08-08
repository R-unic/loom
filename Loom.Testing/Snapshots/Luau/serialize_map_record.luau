const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_writef32 = buffer.writef32
const buffer_len = buffer.len
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_readf32 = buffer.readf32
const function Scores_serialize_binary(value: Scores): Loom.Serialized
  local size = 0
  size += 4
  local entries_measured = 0
  for entries_k, entries_v in value.entries do
    entries_measured += 1
    size += 4 + #entries_k
    size += 4
  end
  const b = buffer_create(size)
  local entries_written = 0
  for entries_key in value.entries do
    entries_written += 1
  end
  buffer_writeu32(b, 0, entries_written)
  local offset = 4
  for entries_k_out, entries_v_out in value.entries do
    const entries_k_length = #entries_k_out
    buffer_writeu32(b, offset, entries_k_length)
    offset += 4
    buffer_writestring(b, offset, entries_k_out)
    offset += entries_k_length
    buffer_writef32(b, offset, entries_v_out)
    offset += 4
  end
  return { buffer = b }
end
const function Scores_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Scores, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 4 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const entries_count = buffer_readu32(b, 0)
  local offset = 4
  const entries = {}
  for i = 1, entries_count do
    const read_0 = buffer_readu32(b, offset)
    offset += 4
    const entries_k_length = read_0
    if buffer_len(b) < offset + entries_k_length then
      return { ok = false, error = { kind = "invalid_length", field = "entries[k]" } }
    end
    const entries_k = buffer_readstring(b, offset, entries_k_length)
    offset += entries_k_length
    const read_1 = buffer_readf32(b, offset)
    offset += 4
    entries[entries_k] = read_1
  end
  return { ok = true, value = { entries = entries } }
end
type Scores = {
  read entries: Record<string, number>,
}
const function _trigger_usage(): ()
  const payload = Scores_serialize_binary((nil :: never) :: Scores)
  const restored = Scores_deserialize_binary(payload)
end