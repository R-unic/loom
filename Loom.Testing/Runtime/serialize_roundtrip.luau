-- The smallest schema there is: one fixed-width field, no prefix and no branch, so every offset in it
-- is a compile-time constant and nothing is left to a runtime cursor.
local p = MyData_serialize_binary({ id = 3 })
assert(buffer.len(p.buffer) == 1, "one byte for one u8")

local r = MyData_deserialize_binary(p)
assert(r.ok, "ok")
assert(r.value.id == 3, "id")

-- A payload shorter than the fixed minimum reports instead of reading past the end.
local empty = MyData_deserialize_binary({ buffer = buffer.create(0) })
assert(not empty.ok, "an empty buffer is not ok")
assert(empty.error.kind == "truncated", "truncated")

-- A payload with no buffer at all takes the same path rather than indexing nil.
local missing = MyData_deserialize_binary({})
assert(not missing.ok, "a missing buffer is not ok")
assert(missing.error.kind == "truncated", "truncated")
