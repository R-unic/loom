assert(alone == 6, "1 + 2 + 3, got " .. alone)
assert(after_fixed == 36, "10 + 20 + 6, got " .. after_fixed)
assert(before_more == 106, "6 + 100, got " .. before_more)
assert(two_spreads == 12, "6 + 6, got " .. two_spreads)
assert(counted == 3, "three numbers after the label, got " .. counted)
assert(generic == 3, "three items, got " .. generic)
assert(forwarded == 3, "three items forwarded, got " .. forwarded)

same(xs, { 1, 2, 3 })

local member_count = 0
for _ in members do
  member_count += 1
end
assert(member_count == 3, "three members, got " .. member_count)
assert(members[2] == true, "2 is a member")
assert(mixed_members[9] == true, "9 is a member")
assert(mixed_members[3] == true, "3 is a member")
