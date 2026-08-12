-- Awaiting a future twice gives the same value both times, and awaiting one that has already settled
-- does not park the caller.
assert(sum_of(2, 3) == 5, "sum_of")
assert(settled(7) == 7, "settled")

-- Future.all keeps the results in the order the futures were given, not the order they finished.
local all = all_of(1, 2, 3)
assert(#all == 3, `all_of length: got {#all}`)
assert(all[1] == 1 and all[2] == 2 and all[3] == 3, "all_of order")

-- Every one of these settles immediately, so the race is won by whichever was started first.
assert(first_of(9, 4) == 9, "first_of")

-- The pieces the emitted code does not reach on its own: a rejection surfaces at the await rather
-- than at the call, and a future nobody has settled yet reports itself pending.
local rejected = Loom.future_rejected("boom")
local ok, err = pcall(Loom.await, rejected)
assert(not ok and err == "boom", "a rejected future re-raises on await")
assert(rejected.status == "rejected", "rejected status")

local pending = Loom.future(function()
	coroutine.yield()
	return 1
end)
assert(pending.status == "pending", "a future still running is pending")

-- One rejection rejects the whole set, and the futures that did settle are left alone.
local mixed = Loom.future_all({ Loom.future_resolved(1), Loom.future_rejected("nope") })
local allOk, allErr = pcall(Loom.await, mixed)
assert(not allOk and allErr == "nope", "Future.all rejects with the first failure")
