-- A dispatch table keyed by the enum member the packet belongs to. The codec arrives as a value, so
-- every call goes through the object rather than the named function the declaring file would use.
local codec = create_message_serializer(0)
assert(codec ~= nil, "a serializer for ShootGun")

local p = codec.serialize({ velocity = 42 })
local r = codec.deserialize(p)
assert(r.ok, "ok")
assert(r.value.velocity == 42, "velocity")

-- Diffing through the same object. A changed field sends its bit and its bytes.
local changed = codec.diff({ velocity = 42 }, { velocity = 9 })
local applied = codec.apply_diff({ velocity = 42 }, changed)
assert(applied.ok, "applying a change is ok")
assert(applied.value.velocity == 9, "the changed velocity")

-- An unchanged field sends its bit and nothing else, and the baseline supplies the value.
local same = codec.diff({ velocity = 42 }, { velocity = 42 })
assert(buffer.len(same.buffer) < buffer.len(changed.buffer), "an unchanged field costs fewer bytes")

local unchanged = codec.apply_diff({ velocity = 42 }, same)
assert(unchanged.ok, "applying no change is ok")
assert(unchanged.value.velocity == 42, "the baseline velocity")
