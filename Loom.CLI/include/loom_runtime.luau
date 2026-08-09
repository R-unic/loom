--!strict
--!native
--!optimize 2
const bor = bit32.bor
const band = bit32.band
const lshift = bit32.lshift
const rshift = bit32.rshift
const abs = math.abs
const max = math.max
const clamp = math.clamp
const round = math.round
const insert = table.insert
const remove = table.remove
const unpack = table.unpack

const Event = require("./event")
const Loom = {}

export type function NonNullable(ty)
	return types.intersectionof(ty, types.negationof(types.singleton(nil)))
end

export type CreatableInstance = Instance
export type ServiceInstance = Instance
export type Range = { minimum: number, maximum: number }
export type ResultOk<T, Error> = { ok: true, value: T }
export type ResultError<T, Error> = { ok: false, error: Error }
export type Result<T, Error> = ResultOk<T, Error> | ResultError<T, Error>
export type RobloxError = { message: string, traceback: string? }
export type DeserializeErrorKind = "truncated" | "invalid_tag" | "invalid_length" | "missing_blob" | "invalid_blob"
export type Serialized = { buffer: buffer?, blobs: { unknown }? }
export type DeserializeError = { kind: DeserializeErrorKind, field: string?, offset: number? }
export type Serializer<T> = {
	serialize: (value: T) -> Serialized,
	deserialize: (serialized: Serialized) -> Result<T, DeserializeError>,
	diff: (baseline: T, value: T) -> Serialized,
	apply_diff: (baseline: T, diff: Serialized) -> Result<T, DeserializeError>,
}
export type Event<T... = ...any> = Event.Event<T...>
export type UserEvent<T... = ...any> = Event.UserEvent<T...>
export type EventConnection = Event.Connection

Loom.Event = Event

-- xpcall handler for a Roblox API call the compiler wraps. Shared rather than built per call
-- site, and only ever invoked when the call actually raised, so the traceback costs nothing on
-- the success path. Luau permits error() with any value, hence the tostring.
function Loom.roblox_error(message: any): RobloxError
	return { message = tostring(message), traceback = debug.traceback(nil, 2) }
end

function Loom.disconnect_event(connections: { [any]: Event.Connection }, fn: any)
	const connection = connections[fn]
	if connection == nil then
		return
	end

	connections[fn] = nil
	connection:Disconnect()
end

function Loom.merge_meta(...: {})
	const parents = { ... }
	const mt = {}
	mt.__index = function(_self, key)
		for _, parent in parents do
			local value = parent[key]
			if value ~= nil then
				return value
			end
		end
		return nil
	end

	return mt
end

type Task<A... = ...any> = {
	[number]: unknown;
	duration: number,
	elapsed: number,
	condition: (() -> boolean)?,
	body: (A...) -> (),
}

const tasks: { Task } = {}
local heartbeatConn: RBXScriptConnection?

const function stopIfEmpty(): ()
	if heartbeatConn and #tasks == 0 then
		heartbeatConn:Disconnect()
		heartbeatConn = nil
	end
end

const function start(): ()
	if heartbeatConn then
		return
	end

	heartbeatConn = game:GetService("RunService").Heartbeat:Connect(function(dt)
		local count = #tasks
		local i = 1
		while i <= count do
			local task = tasks[i]
			if task.condition and not task.condition() then
				remove(tasks, i)
				count -= 1
				continue
			end

			const argCount = #task
			task.elapsed += dt
			while task.elapsed >= task.duration do
				task.elapsed -= task.duration
				task.body(unpack(task, 1, argCount)) -- args stored as array in task
			end

			i += 1
		end

		stopIfEmpty()
	end)
end

function Loom.every<A...>(
	duration: number,
	condition: (() -> boolean)?,
	body: (A...) -> (),
	...: A...
): ()
	insert(tasks, {
		duration = duration,
		elapsed = 0,
		body = body,
		condition = condition,
		...
	})
	start()
end

-- Serialization support. Emitted serializers inline everything they can; these are the pieces that
-- are either too large to repeat per field or need real math.

-- Roblox exposes no CFrame:ToQuaternion, so the rotation comes out of GetComponents as a 3x3 matrix
-- and is converted here (Shepperd's method - pivot on the largest diagonal term to keep the divisor
-- away from zero).
function Loom.cframe_to_quaternion(cf: CFrame): (number, number, number, number)
	const _, _, _, r00, r01, r02, r10, r11, r12, r20, r21, r22 = cf:GetComponents()
	const trace = r00 + r11 + r22
	if trace > 0 then
		const s = (trace + 1) ^ 0.5 * 2
		return (r21 - r12) / s, (r02 - r20) / s, (r10 - r01) / s, 0.25 * s
	elseif r00 > r11 and r00 > r22 then
		const s = (1 + r00 - r11 - r22) ^ 0.5 * 2
		return 0.25 * s, (r01 + r10) / s, (r02 + r20) / s, (r21 - r12) / s
	elseif r11 > r22 then
		const s = (1 + r11 - r00 - r22) ^ 0.5 * 2
		return (r01 + r10) / s, 0.25 * s, (r12 + r21) / s, (r02 - r20) / s
	else
		const s = (1 + r22 - r00 - r11) ^ 0.5 * 2
		return (r02 + r20) / s, (r12 + r21) / s, 0.25 * s, (r10 - r01) / s
	end
end

-- Smallest-three: a unit quaternion's largest component is recoverable from the other three, and
-- negating the whole quaternion (which names the same rotation) pins its sign positive. The three
-- kept components are then each at most 1/sqrt(2), so scaling by sqrt(2) uses the full range.
const QUATERNION_SCALE = 2 ^ 0.5
const QUATERNION_STEPS = 1023

const function dequantize_component(value: number): number
	return (value / QUATERNION_STEPS * 2 - 1) / QUATERNION_SCALE
end

function Loom.pack_quaternion(qx: number, qy: number, qz: number, qw: number): number
	const ax = abs(qx)
	const ay = abs(qy)
	const az = abs(qz)
	const aw = abs(qw)
	local largest, magnitude = 0, ax
	if ay > magnitude then
		largest, magnitude = 1, ay
	end
	if az > magnitude then
		largest, magnitude = 2, az
	end
	if aw > magnitude then
		largest, magnitude = 3, aw
	end

	const components = { qx, qy, qz, qw }
	if components[largest + 1] < 0 then
		components[1], components[2], components[3], components[4] = -qx, -qy, -qz, -qw
	end

	local packed = largest
	for index = 1, 4 do
		if index - 1 ~= largest then
			packed = bor(lshift(packed, 10), clamp(round((components[index] * QUATERNION_SCALE + 1) * 0.5 * QUATERNION_STEPS), 0, QUATERNION_STEPS))
		end
	end

	return packed
end

function Loom.unpack_quaternion(packed: number): (number, number, number, number)
	const third = dequantize_component(band(packed, 0x3FF))
	const second = dequantize_component(band(rshift(packed, 10), 0x3FF))
	const first = dequantize_component(band(rshift(packed, 20), 0x3FF))
	const largest = band(rshift(packed, 30), 0x3)

	const missing = max(0, 1 - (first * first + second * second + third * third)) ^ 0.5
	const components = { first, second, third }
	insert(components, largest + 1, missing)

	return components[1], components[2], components[3], components[4]
end

-- Delta serialization compares a baseline against a current value field by field; == already
-- covers primitives and Roblox datatypes (Vector3/CFrame overload it structurally), so this only
-- has to recurse for the table shapes arrays, maps and flattened structs come out as.
function Loom.deep_equal(a: any, b: any): boolean
	if a == b then
		return true
	end

	if type(a) ~= "table" or type(b) ~= "table" then
		return false
	end

	for key, value in a do
		if not Loom.deep_equal(value, b[key]) then
			return false
		end
	end

	for key in b do
		if a[key] == nil then
			return false
		end
	end

	return true
end

table.freeze(Loom)
return Loom