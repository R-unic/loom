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

-- A panic inside a plain async body keeps its traceback: the re-raised error carries the stack it
-- actually failed on, not just the bare message a plain pcall would have left.
local panicking = Loom.future(function()
	error("kaboom")
end)
local panicOk, panicErr = pcall(Loom.await, panicking)
assert(not panicOk, "a panicking async fn rejects rather than resolving")
assert(string.find(panicErr, "kaboom") ~= nil, "the original message survives")
assert(string.find(panicErr, "\n") ~= nil, "the traceback is preserved as extra frames, not lost to a bare pcall")

-- error() permits raising anything; the traceback handler must leave a non-string raise alone
-- rather than coercing it into a string, or a caller matching on the error's shape would break.
local customError = { code = 5 }
local tablePanicking = Loom.future(function()
	error(customError)
end)
local tableOk, tableErr = pcall(Loom.await, tablePanicking)
assert(not tableOk and tableErr == customError, "a non-string raise passes through unchanged")

-- A rejection nobody ever awaits is reported once the current resumption cycle has had its chance
-- to observe it - not the instant it rejects, since awaiting a future some time after creating it
-- (all_of/first_of above do exactly that) is a legitimate pattern, not a mistake.
clear_warnings()
Loom.future_rejected("dropped on the floor")
assert(#warnings == 0, "not reported before the deferred check runs")
flush_deferred()
assert(#warnings == 1, `expected one unhandled-rejection warning, got {#warnings}`)
assert(string.find(warnings[1], "dropped on the floor") ~= nil, "the warning names the actual error")

-- The same rejection, awaited before the deferred check runs this time, is never reported -
-- observing it, however briefly, is enough to count as handled.
clear_warnings()
local observedRejection = Loom.future_rejected("handled in time")
pcall(Loom.await, observedRejection)
flush_deferred()
assert(#warnings == 0, "an awaited rejection is never reported as unhandled")
