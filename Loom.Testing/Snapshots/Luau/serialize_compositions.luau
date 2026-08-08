const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu8 = buffer.writeu8
const buffer_len = buffer.len
const buffer_readu8 = buffer.readu8
const buffer_writebits = buffer.writebits
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_writef32 = buffer.writef32
const buffer_readbits = buffer.readbits
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_readf32 = buffer.readf32
const function Leaf_serialize_binary(value: Leaf): Loom.Serialized
  const b = buffer_create(1)
  buffer_writeu8(b, 0, value.n)
  return { buffer = b }
end
const function Leaf_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Leaf, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 1 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  return { ok = true, value = { n = buffer_readu8(b, 0) } }
end
const function Compositions_serialize_binary(value: Compositions): Loom.Serialized
  local size = 1 + (if value.maybe_leaf ~= nil then 1 else 0)
  if value.maybe_map ~= nil then
    size += 4
    local maybe_map_measured = 0
    for maybe_map_k, maybe_map_v in value.maybe_map do
      maybe_map_measured += 1
      size += 4 + #maybe_map_k
      size += 4
    end
  end
  size += 4
  for i = 1, #value.map_list do
    size += 4
    local map_list_element_measured = 0
    for map_list_element_k, map_list_element_v in value.map_list[i] do
      map_list_element_measured += 1
      size += 4 + #map_list_element_k
      size += 1
    end
  end
  size += 4
  local sparse_measured = 0
  for sparse_k, sparse_v in value.sparse do
    sparse_measured += 1
    size += 4 + #sparse_k
    if sparse_v ~= nil then
      size += 4 + #sparse_v
    end
  end
  size += (sparse_measured + 7) // 8
  if value.maybe_names ~= nil then
    size += 4
    for i_2 = 1, #value.maybe_names do
      size += 4 + #value.maybe_names[i_2]
    end
  end
  const b = buffer_create(size)
  buffer_writebits(b, 0, 1, if value.maybe_map ~= nil then 1 else 0)
  local offset = 1
  if value.maybe_map ~= nil then
    local maybe_map_written = 0
    for maybe_map_key in value.maybe_map do
      maybe_map_written += 1
    end
    buffer_writeu32(b, offset, maybe_map_written)
    offset += 4
    for maybe_map_k_out, maybe_map_v_out in value.maybe_map do
      const maybe_map_k_length = #maybe_map_k_out
      buffer_writeu32(b, offset, maybe_map_k_length)
      offset += 4
      buffer_writestring(b, offset, maybe_map_k_out)
      offset += maybe_map_k_length
      buffer_writef32(b, offset, maybe_map_v_out)
      offset += 4
    end
  end
  buffer_writeu32(b, offset, #value.map_list)
  offset += 4
  for i_3 = 1, #value.map_list do
    const map_list_item = value.map_list[i_3]
    local map_list_element_written = 0
    for map_list_element_key in map_list_item do
      map_list_element_written += 1
    end
    buffer_writeu32(b, offset, map_list_element_written)
    offset += 4
    for map_list_element_k_out, map_list_element_v_out in map_list_item do
      const map_list_element_k_length = #map_list_element_k_out
      buffer_writeu32(b, offset, map_list_element_k_length)
      offset += 4
      buffer_writestring(b, offset, map_list_element_k_out)
      offset += map_list_element_k_length
      buffer_writeu8(b, offset, map_list_element_v_out.n)
      offset += 1
    end
  end
  local sparse_written = 0
  for sparse_key in value.sparse do
    sparse_written += 1
  end
  buffer_writeu32(b, offset, sparse_written)
  offset += 4
  const sparse_bits = offset * 8
  offset += (sparse_written + 7) // 8
  local sparse_index = 1
  for sparse_k_out, sparse_v_out in value.sparse do
    const sparse_k_length = #sparse_k_out
    buffer_writeu32(b, offset, sparse_k_length)
    offset += 4
    buffer_writestring(b, offset, sparse_k_out)
    offset += sparse_k_length
    buffer_writebits(b, sparse_bits + (sparse_index - 1), 1, if sparse_v_out ~= nil then 1 else 0)
    if sparse_v_out ~= nil then
      const sparse_v_length = #sparse_v_out
      buffer_writeu32(b, offset, sparse_v_length)
      offset += 4
      buffer_writestring(b, offset, sparse_v_out)
      offset += sparse_v_length
    end
    sparse_index += 1
  end
  buffer_writebits(b, 1, 1, if value.maybe_leaf ~= nil then 1 else 0)
  if value.maybe_leaf ~= nil then
    buffer_writeu8(b, offset, value.maybe_leaf.n)
    offset += 1
  end
  buffer_writebits(b, 2, 1, if value.maybe_names ~= nil then 1 else 0)
  if value.maybe_names ~= nil then
    buffer_writeu32(b, offset, #value.maybe_names)
    offset += 4
    for i_4 = 1, #value.maybe_names do
      const maybe_names_item = value.maybe_names[i_4]
      const maybe_names_element_length = #maybe_names_item
      buffer_writeu32(b, offset, maybe_names_element_length)
      offset += 4
      buffer_writestring(b, offset, maybe_names_item)
      offset += maybe_names_element_length
    end
  end
  return { buffer = b }
end
const function Compositions_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Compositions, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 9 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const maybe_map_present = buffer_readbits(b, 0, 1) == 1
  local maybe_map = nil
  local offset = 1
  if maybe_map_present then
    const read_0 = buffer_readu32(b, offset)
    offset += 4
    const maybe_map_2_count = read_0
    const maybe_map_2 = {}
    for i = 1, maybe_map_2_count do
      const read_1 = buffer_readu32(b, offset)
      offset += 4
      const maybe_map_k_length = read_1
      if buffer_len(b) < offset + maybe_map_k_length then
        return { ok = false, error = { kind = "invalid_length", field = "maybe_map[k]" } }
      end
      const maybe_map_k = buffer_readstring(b, offset, maybe_map_k_length)
      offset += maybe_map_k_length
      const read_2 = buffer_readf32(b, offset)
      offset += 4
      maybe_map_2[maybe_map_k] = read_2
    end
    maybe_map = maybe_map_2
  end
  const read_3 = buffer_readu32(b, offset)
  offset += 4
  const map_list_count = read_3
  const map_list = {}
  for i_2 = 1, map_list_count do
    const read_4 = buffer_readu32(b, offset)
    offset += 4
    const map_list_element_count = read_4
    const map_list_element = {}
    for i_3 = 1, map_list_element_count do
      const read_5 = buffer_readu32(b, offset)
      offset += 4
      const map_list_element_k_length = read_5
      if buffer_len(b) < offset + map_list_element_k_length then
        return { ok = false, error = { kind = "invalid_length", field = "map_list[][k]" } }
      end
      const map_list_element_k = buffer_readstring(b, offset, map_list_element_k_length)
      offset += map_list_element_k_length
      const read_6 = buffer_readu8(b, offset)
      offset += 1
      map_list_element[map_list_element_k] = { n = read_6 }
    end
    map_list[i_2] = map_list_element
  end
  const read_7 = buffer_readu32(b, offset)
  offset += 4
  const sparse_count = read_7
  const sparse_bits = offset * 8
  offset += (sparse_count + 7) // 8
  const sparse = {}
  for i_4 = 1, sparse_count do
    const read_8 = buffer_readu32(b, offset)
    offset += 4
    const sparse_k_length = read_8
    if buffer_len(b) < offset + sparse_k_length then
      return { ok = false, error = { kind = "invalid_length", field = "sparse[k]" } }
    end
    const sparse_k = buffer_readstring(b, offset, sparse_k_length)
    offset += sparse_k_length
    const sparse_v_present = buffer_readbits(b, sparse_bits + (i_4 - 1), 1) == 1
    local sparse_v = nil
    if sparse_v_present then
      const read_9 = buffer_readu32(b, offset)
      offset += 4
      const sparse_v_2_length = read_9
      if buffer_len(b) < offset + sparse_v_2_length then
        return { ok = false, error = { kind = "invalid_length", field = "sparse[v]" } }
      end
      const sparse_v_2 = buffer_readstring(b, offset, sparse_v_2_length)
      offset += sparse_v_2_length
      sparse_v = sparse_v_2
    end
    sparse[sparse_k] = sparse_v
  end
  const maybe_leaf_present = buffer_readbits(b, 1, 1) == 1
  local maybe_leaf = nil
  if maybe_leaf_present then
    if buffer_len(b) < offset + 1 then
      return { ok = false, error = { kind = "truncated", field = "maybe_leaf" } }
    end
    const read_10 = buffer_readu8(b, offset)
    offset += 1
    maybe_leaf = { n = read_10 }
  end
  const maybe_names_present = buffer_readbits(b, 2, 1) == 1
  local maybe_names = nil
  if maybe_names_present then
    const read_11 = buffer_readu32(b, offset)
    offset += 4
    const maybe_names_2_count = read_11
    const maybe_names_2 = {}
    for i_5 = 1, maybe_names_2_count do
      const read_12 = buffer_readu32(b, offset)
      offset += 4
      const maybe_names_element_length = read_12
      if buffer_len(b) < offset + maybe_names_element_length then
        return { ok = false, error = { kind = "invalid_length", field = "maybe_names[]" } }
      end
      const maybe_names_element = buffer_readstring(b, offset, maybe_names_element_length)
      offset += maybe_names_element_length
      maybe_names_2[i_5] = maybe_names_element
    end
    maybe_names = maybe_names_2
  end
  return { ok = true, value = { maybe_map = maybe_map, map_list = map_list, sparse = sparse, maybe_leaf = maybe_leaf, maybe_names = maybe_names } }
end
type Leaf = {
  read n: number,
}
type Compositions = {
  read maybe_map: Record<string, number>?,
  read map_list: { Record<string, Leaf> },
  read sparse: Record<string, string?>,
  read maybe_leaf: Leaf?,
  read maybe_names: { string }?,
}
const function _trigger_usage(): ()
  const leaf_payload = Leaf_serialize_binary((nil :: never) :: Leaf)
  const leaf_restored = Leaf_deserialize_binary(leaf_payload)
  const compositions_payload = Compositions_serialize_binary((nil :: never) :: Compositions)
  const compositions_restored = Compositions_deserialize_binary(compositions_payload)
end