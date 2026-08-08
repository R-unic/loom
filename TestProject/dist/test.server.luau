const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu8 = buffer.writeu8
const buffer_writestring = buffer.writestring
const buffer_len = buffer.len
const buffer_readu8 = buffer.readu8
const buffer_readstring = buffer.readstring
const buffer_writebits = buffer.writebits
const buffer_copy = buffer.copy
const buffer_readbits = buffer.readbits
const function HelloPacket_serialize_binary(value: HelloPacket): Loom.Serialized
  const b = buffer_create(1 + 1 + #value.message)
  buffer_writeu8(b, 0, value.id)
  const message_value = value.message
  const message_length = #message_value
  buffer_writeu8(b, 1, message_length)
  local offset = 2
  buffer_writestring(b, offset, message_value)
  offset += message_length
  return { buffer = b }
end
const function HelloPacket_deserialize_binary(serialized: Loom.Serialized): Loom.Result<HelloPacket, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 2 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const message_length = buffer_readu8(b, 1)
  local offset = 2
  if buffer_len(b) < offset + message_length then
    return { ok = false, error = { kind = "invalid_length", field = "message" } }
  end
  const message = buffer_readstring(b, offset, message_length)
  offset += message_length
  return { ok = true, value = { id = buffer_readu8(b, 0), message = message } }
end
const function HelloPacket_diff_binary_write(baseline: HelloPacket, value: HelloPacket, b: buffer)
  local offset = 1
  const id_changed = not Loom.deep_equal(baseline.id, value.id)
  buffer_writebits(b, 0, 1, if id_changed then 1 else 0)
  if id_changed then
    buffer_writeu8(b, offset, value.id)
    offset += 1
  end
  const message_changed = not Loom.deep_equal(baseline.message, value.message)
  buffer_writebits(b, 1, 1, if message_changed then 1 else 0)
  if message_changed then
    const message_value = value.message
    const message_length = #message_value
    buffer_writeu8(b, offset, message_length)
    offset += 1
    buffer_writestring(b, offset, message_value)
    offset += message_length
  end
  return offset
end
const function HelloPacket_diff_binary_attempt(baseline: HelloPacket, value: HelloPacket, size: number)
  const scratch = buffer_create(size)
  const ok, written = pcall(HelloPacket_diff_binary_write, baseline, value, scratch)
  if ok then
    return scratch, written
  end
  return HelloPacket_diff_binary_attempt(baseline, value, size * 2)
end
const function HelloPacket_diff_binary(baseline: HelloPacket, value: HelloPacket): Loom.Serialized
  const baseline_s = HelloPacket_serialize_binary(baseline)
  const value_s = HelloPacket_serialize_binary(value)
  const size = (if baseline_s.buffer ~= nil then buffer.len(baseline_s.buffer) else 0) + (if value_s.buffer ~= nil then buffer.len(value_s.buffer) else 0) + 1
  const scratch, written = HelloPacket_diff_binary_attempt(baseline, value, size)
  const result = buffer_create(written)
  buffer_copy(result, 0, scratch, 0, written)
  return { buffer = result }
end
const function HelloPacket_apply_diff_binary_read(baseline: HelloPacket, diff: Loom.Serialized): Loom.Result<HelloPacket, Loom.DeserializeError>
  const b = diff.buffer
  if b == nil or buffer_len(b) < 1 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  local offset = 1
  const id_changed = buffer_readbits(b, 0, 1) == 1
  local id = nil
  if id_changed then
    const read_0 = buffer_readu8(b, offset)
    offset += 1
    id = read_0
  else
    id = baseline.id
  end
  const message_changed = buffer_readbits(b, 1, 1) == 1
  local message = nil
  if message_changed then
    const read_1 = buffer_readu8(b, offset)
    offset += 1
    const message_2_length = read_1
    if buffer_len(b) < offset + message_2_length then
      return { ok = false, error = { kind = "invalid_length", field = "message" } }
    end
    const message_2 = buffer_readstring(b, offset, message_2_length)
    offset += message_2_length
    message = message_2
  else
    message = baseline.message
  end
  return { ok = true, value = { id = id, message = message } }
end
const function HelloPacket_apply_diff_binary(baseline: HelloPacket, diff: Loom.Serialized): Loom.Result<HelloPacket, Loom.DeserializeError>
  const ok, result = pcall(HelloPacket_apply_diff_binary_read, baseline, diff)
  if ok then
    return result
  end
  return { ok = false, error = { kind = "truncated" } }
end
const HelloPacket_serializer: Loom.Serializer<HelloPacket> = { serialize = HelloPacket_serialize_binary, deserialize = HelloPacket_deserialize_binary, diff = HelloPacket_diff_binary, apply_diff = HelloPacket_apply_diff_binary }
const MessageData_serializer_map = { [0] = HelloPacket_serializer }
const replicated = game:GetService("ReplicatedStorage")
const remote = replicated:FindFirstChild("re") :: Loom.NonNullable<typeof(replicated:FindFirstChild("re"))>
export type Message = number
export type HelloPacket = {
  read id: number,
  read message: string,
}
type MessageData = { read [index<Message, "Hello">]: HelloPacket }
const function on<K>(message: K & Message, handler: (index<MessageData, K & Message>) -> ()): ()
  const message_serializer = MessageData_serializer_map[message]
  remote.OnServerEvent:Connect(function(_, __)
  end)
end
return { on = on, HelloPacket_serializer = HelloPacket_serializer }
