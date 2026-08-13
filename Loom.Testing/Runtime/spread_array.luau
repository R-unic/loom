same(rest, { 2, 3, 4 })
same(prepended, { 1, 2, 3, 4 })
same(appended, { 2, 3, 4, 5 })
same(surrounded, { 0, 2, 3, 4, 5, 6 })
same(joined, { 2, 3, 4, 2, 3, 4 })
same(copied, { 2, 3, 4 })
same(mutable, { 1, 2, 3, 4 })
same(called, { 7, 8, 9 })
same(names, { "a" })
same(mixed, { "a", "b", "a" })

if copied == rest then
  error("a spread aliased its operand instead of copying it")
end
