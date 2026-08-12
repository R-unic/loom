-- A length-prefixed string writes its prefix and its text as one run, so the text has to land past the
-- prefix rather than at it. An empty name still writes a prefix, which is what the up-front minimum counts.
local cases = { "", "a", "a longer name with spaces", string.rep("x", 300) }

for _, name in cases do
  local p = Player_serialize_binary({ id = 7, name = name })
  local r = Player_deserialize_binary(p)
  assert(r.ok, "ok for " .. #name .. " chars")
  assert(r.value.id == 7, "id")
  assert(r.value.name == name, "name")
end

-- The buffer is sized to the byte, so a shorter one means the writes ran off the end.
local exact = Player_serialize_binary({ id = 1, name = "abcdef" })
assert(buffer.len(exact.buffer) == 1 + 4 + 6, "buffer sized to the byte")

-- A length prefix claiming more than the payload holds reports rather than throwing out of the read.
local truncated = buffer.create(8)
buffer.copy(truncated, 0, exact.buffer, 0, 8)

local short = Player_deserialize_binary({ buffer = truncated })
assert(not short.ok, "a truncated payload is not ok")
assert(short.error.kind == "invalid_length", "invalid_length, not a throw")
assert(short.error.field == "name", "names the field that did not fit")
