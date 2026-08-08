const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writebits = buffer.writebits
const buffer_writestring = buffer.writestring
const buffer_len = buffer.len
const buffer_readu32 = buffer.readu32
const buffer_readbits = buffer.readbits
const buffer_readstring = buffer.readstring
const function Canvas_serialize_binary(value: Canvas): Loom.Serialized
  local size = 0
  size += 4
  for i = 1, #value.masks do
    size += 4 + (#value.masks[i] + 7) // 8
  end
  size += 4
  size += (#value.shapes + 7) // 8
  for i_2 = 1, #value.shapes do
    const shapes_element_value = value.shapes[i_2]
    local shapes_element_tag = 0
    if shapes_element_value.kind == "Line" then
      shapes_element_tag = 1
    end
    if shapes_element_tag == 1 then
      size += 4 + #value.shapes[i_2].label
    end
  end
  const b = buffer_create(size)
  buffer_writeu32(b, 0, #value.masks)
  local offset = 4
  for i_3 = 1, #value.masks do
    const masks_item = value.masks[i_3]
    buffer_writeu32(b, offset, #masks_item)
    offset += 4
    const masks_element_bits = offset * 8
    offset += (#masks_item + 7) // 8
    for i_4 = 1, #masks_item do
      const masks_element_item = masks_item[i_4]
      buffer_writebits(b, masks_element_bits + (i_4 - 1), 1, if masks_element_item then 1 else 0)
    end
  end
  buffer_writeu32(b, offset, #value.shapes)
  offset += 4
  const shapes_bits = offset * 8
  offset += (#value.shapes + 7) // 8
  for i_5 = 1, #value.shapes do
    const shapes_item = value.shapes[i_5]
    const shapes_element_value = shapes_item
    local shapes_element_tag = 0
    if shapes_element_value.kind == "Line" then
      shapes_element_tag = 1
    end
    buffer_writebits(b, shapes_bits + (i_5 - 1), 1, shapes_element_tag)
    if shapes_element_tag == 1 then
      const label_value = shapes_item.label
      const label_length = #label_value
      buffer_writeu32(b, offset, label_length)
      offset += 4
      buffer_writestring(b, offset, label_value)
      offset += label_length
    end
  end
  return { buffer = b }
end
const function Canvas_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Canvas, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 8 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const masks_count = buffer_readu32(b, 0)
  local offset = 4
  const masks = {}
  for i = 1, masks_count do
    const read_0 = buffer_readu32(b, offset)
    offset += 4
    const masks_element_count = read_0
    const masks_element_bits = offset * 8
    offset += (masks_element_count + 7) // 8
    const masks_element = {}
    for i_2 = 1, masks_element_count do
      masks_element[i_2] = buffer_readbits(b, masks_element_bits + (i_2 - 1), 1) == 1
    end
    masks[i] = masks_element
  end
  const read_1 = buffer_readu32(b, offset)
  offset += 4
  const shapes_count = read_1
  const shapes_bits = offset * 8
  offset += (shapes_count + 7) // 8
  const shapes = {}
  for i_3 = 1, shapes_count do
    const shapes_element_tag = buffer_readbits(b, shapes_bits + (i_3 - 1), 1)
    local shapes_element = nil
    if shapes_element_tag == 0 then
      shapes_element = { kind = "Dot" }
    elseif shapes_element_tag == 1 then
      const read_2 = buffer_readu32(b, offset)
      offset += 4
      const label_length = read_2
      if buffer_len(b) < offset + label_length then
        return { ok = false, error = { kind = "invalid_length", field = "shapes[].label" } }
      end
      const label = buffer_readstring(b, offset, label_length)
      offset += label_length
      shapes_element = { kind = "Line", label = label }
    else
      return { ok = false, error = { kind = "invalid_tag", field = "shapes[]" } }
    end
    shapes[i_3] = shapes_element
  end
  return { ok = true, value = { masks = masks, shapes = shapes } }
end
type IShape<Kind> = {
  read kind: Kind & string,
}
type Dot = IShape<"Dot"> & {}
type Line = IShape<"Line"> & {
  read label: string,
}
type Canvas = {
  read masks: { { boolean } },
  read shapes: { (Dot | Line) },
}
const function _trigger_usage(): ()
  const payload = Canvas_serialize_binary((nil :: never) :: Canvas)
  const restored = Canvas_deserialize_binary(payload)
end