const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_writebits = buffer.writebits
const buffer_writef32 = buffer.writef32
const buffer_writeu8 = buffer.writeu8
const buffer_len = buffer.len
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_readbits = buffer.readbits
const buffer_readf32 = buffer.readf32
const buffer_readu8 = buffer.readu8
const function Registry_serialize_binary(value: Registry): Loom.Serialized
  local size = 0
  size += 4
  local flags_measured = 0
  for flags_k, flags_v in value.flags do
    flags_measured += 1
    size += 4 + #flags_k
  end
  size += (flags_measured + 7) // 8
  size += 4
  local entries_measured = 0
  for entries_k, entries_v in value.entries do
    entries_measured += 1
    size += 4
    size += 1
    size += 4 + #entries_v.label
  end
  size += 4
  local lists_measured = 0
  for lists_k, lists_v in value.lists do
    lists_measured += 1
    size += 4 + #lists_k
    size += 4 + #lists_v * 4
  end
  const b = buffer_create(size)
  local flags_written = 0
  for flags_key in value.flags do
    flags_written += 1
  end
  buffer_writeu32(b, 0, flags_written)
  local offset = 4
  const flags_bits = offset * 8
  offset += (flags_written + 7) // 8
  local flags_index = 1
  for flags_k_out, flags_v_out in value.flags do
    const flags_k_length = #flags_k_out
    buffer_writeu32(b, offset, flags_k_length)
    offset += 4
    buffer_writestring(b, offset, flags_k_out)
    offset += flags_k_length
    buffer_writebits(b, flags_bits + (flags_index - 1), 1, if flags_v_out then 1 else 0)
    flags_index += 1
  end
  local entries_written = 0
  for entries_key in value.entries do
    entries_written += 1
  end
  buffer_writeu32(b, offset, entries_written)
  offset += 4
  for entries_k_out, entries_v_out in value.entries do
    buffer_writef32(b, offset, entries_k_out)
    offset += 4
    buffer_writeu8(b, offset, entries_v_out.level)
    offset += 1
    const label_value = entries_v_out.label
    const label_length = #label_value
    buffer_writeu32(b, offset, label_length)
    offset += 4
    buffer_writestring(b, offset, label_value)
    offset += label_length
  end
  local lists_written = 0
  for lists_key in value.lists do
    lists_written += 1
  end
  buffer_writeu32(b, offset, lists_written)
  offset += 4
  for lists_k_out, lists_v_out in value.lists do
    const lists_k_length = #lists_k_out
    buffer_writeu32(b, offset, lists_k_length)
    offset += 4
    buffer_writestring(b, offset, lists_k_out)
    offset += lists_k_length
    buffer_writeu32(b, offset, #lists_v_out)
    offset += 4
    for i = 1, #lists_v_out do
      const lists_v_item = lists_v_out[i]
      buffer_writef32(b, offset, lists_v_item)
      offset += 4
    end
  end
  return { buffer = b }
end
const function Registry_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Registry, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 12 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const flags_count = buffer_readu32(b, 0)
  local offset = 4
  const flags_bits = offset * 8
  offset += (flags_count + 7) // 8
  const flags = {}
  for i = 1, flags_count do
    const read_0 = buffer_readu32(b, offset)
    offset += 4
    const flags_k_length = read_0
    if buffer_len(b) < offset + flags_k_length then
      return { ok = false, error = { kind = "invalid_length", field = "flags[k]" } }
    end
    const flags_k = buffer_readstring(b, offset, flags_k_length)
    offset += flags_k_length
    flags[flags_k] = buffer_readbits(b, flags_bits + (i - 1), 1) == 1
  end
  const read_1 = buffer_readu32(b, offset)
  offset += 4
  const entries_count = read_1
  const entries = {}
  for i_2 = 1, entries_count do
    const read_2 = buffer_readf32(b, offset)
    offset += 4
    const read_3 = buffer_readu8(b, offset)
    offset += 1
    const read_4 = buffer_readu32(b, offset)
    offset += 4
    const label_length = read_4
    if buffer_len(b) < offset + label_length then
      return { ok = false, error = { kind = "invalid_length", field = "entries[v].label" } }
    end
    const label = buffer_readstring(b, offset, label_length)
    offset += label_length
    entries[read_2] = { level = read_3, label = label }
  end
  const read_5 = buffer_readu32(b, offset)
  offset += 4
  const lists_count = read_5
  const lists = {}
  for i_3 = 1, lists_count do
    const read_6 = buffer_readu32(b, offset)
    offset += 4
    const lists_k_length = read_6
    if buffer_len(b) < offset + lists_k_length then
      return { ok = false, error = { kind = "invalid_length", field = "lists[k]" } }
    end
    const lists_k = buffer_readstring(b, offset, lists_k_length)
    offset += lists_k_length
    const read_7 = buffer_readu32(b, offset)
    offset += 4
    const lists_v_count = read_7
    if buffer_len(b) < offset + lists_v_count * 4 then
      return { ok = false, error = { kind = "invalid_length", field = "lists[v]" } }
    end
    const lists_v = {}
    for i_4 = 1, lists_v_count do
      const read_8 = buffer_readf32(b, offset)
      offset += 4
      lists_v[i_4] = read_8
    end
    lists[lists_k] = lists_v
  end
  return { ok = true, value = { flags = flags, entries = entries, lists = lists } }
end
type Entry = {
  read level: number,
  read label: string,
}
type Registry = {
  read flags: Record<string, boolean>,
  read entries: Record<number, Entry>,
  read lists: Record<string, { number }>,
}
const function _trigger_usage(): ()
  const payload = Registry_serialize_binary((nil :: never) :: Registry)
  const restored = Registry_deserialize_binary(payload)
end