const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_len = buffer.len
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const function Grid_serialize_binary(value: Grid): Loom.Serialized
  local size = 0
  size += 4
  for i = 1, #value.rows do
    size += 4
    for i_2 = 1, #value.rows[i] do
      size += 4 + #value.rows[i][i_2]
    end
  end
  const b = buffer_create(size)
  buffer_writeu32(b, 0, #value.rows)
  local offset = 4
  for i_3 = 1, #value.rows do
    const rows_item = value.rows[i_3]
    buffer_writeu32(b, offset, #rows_item)
    offset += 4
    for i_4 = 1, #rows_item do
      const rows_element_item = rows_item[i_4]
      const rows_element_element_length = #rows_element_item
      buffer_writeu32(b, offset, rows_element_element_length)
      offset += 4
      buffer_writestring(b, offset, rows_element_item)
      offset += rows_element_element_length
    end
  end
  return { buffer = b }
end
const function Grid_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Grid, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 4 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const rows_count = buffer_readu32(b, 0)
  local offset = 4
  const rows = {}
  for i = 1, rows_count do
    const read_0 = buffer_readu32(b, offset)
    offset += 4
    const rows_element_count = read_0
    const rows_element = {}
    for i_2 = 1, rows_element_count do
      const read_1 = buffer_readu32(b, offset)
      offset += 4
      const rows_element_element_length = read_1
      if buffer_len(b) < offset + rows_element_element_length then
        return { ok = false, error = { kind = "invalid_length", field = "rows[][]" } }
      end
      const rows_element_element = buffer_readstring(b, offset, rows_element_element_length)
      offset += rows_element_element_length
      rows_element[i_2] = rows_element_element
    end
    rows[i] = rows_element
  end
  return { ok = true, value = { rows = rows } }
end
type Grid = {
  read rows: { { string } },
}
const function _trigger_usage(): ()
  const payload = Grid_serialize_binary((nil :: never) :: Grid)
  const restored = Grid_deserialize_binary(payload)
end