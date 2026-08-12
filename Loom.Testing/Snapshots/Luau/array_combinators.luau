const numbers = {1, 2, 3, 4, 5}
const _result = table.create(#numbers)
for _index, n in numbers do
  _result[_index] = n * 2
end
const doubled = _result
const _result_1 = table.create(#numbers)
for i, n in numbers do
  _result_1[i] = n * i
end
const weighted = _result_1
const _result_2 = table.create(#numbers)
local _count = 0
for _, n in numbers do
  if n > 2 then
    _count += 1
    _result_2[_count] = n
  end
end
const big = _result_2
local _accumulator = 0
for _, n in numbers do
  const sum = _accumulator
  _accumulator = sum + n
end
const total = _accumulator
local _accumulator_1 = 0
for _, n in numbers do
  const n = n * 2
  if n > 4 then
    const sum = _accumulator_1
    _accumulator_1 = sum + n
  end
end
const chained = _accumulator_1
const function triple(n: number): number
  return n * 3
end
const _result_3 = table.create(#numbers)
for _index_1, _element in numbers do
  _result_3[_index_1] = triple(_element, _index_1)
end
const tripled = _result_3
local _found = false
for _, n in numbers do
  if n > 4 then
    _found = true
    break
  end
end
const has_big = _found
local _satisfied = true
for _, n in numbers do
  if n > 0 then continue end
  _satisfied = false
  break
end
const all_positive = _satisfied
local _count_1 = 0
for _, n in numbers do
  if n > 2 then
    _count_1 += 1
  end
end
const big_count = _count_1
const _result_4 = {}
local _count_2 = 0
for _, n in numbers do
  const _segment = {n, n * 10}
  const _length = #_segment
  table.move(_segment, 1, _length, _count_2 + 1, _result_4)
  _count_2 += _length
end
const paired = _result_4
const rows = {{1, 2}, {3}}
const _result_5 = {}
local _count_3 = 0
for _, _segment_1 in rows do
  const _length_1 = #_segment_1
  table.move(_segment_1, 1, _length_1, _count_3 + 1, _result_5)
  _count_3 += _length_1
end
const flat = _result_5
const _result_6 = {}
local _count_4 = 0
for _, _segment_2 in rows do
  const _length_2 = #_segment_2
  table.move(_segment_2, 1, _length_2, _count_4 + 1, _result_6)
  _count_4 += _length_2
end
const _result_7 = table.create(#_result_6)
local _count_5 = 0
for _, n in _result_6 do
  if n > 1 then
    _count_5 += 1
    _result_7[_count_5] = n
  end
end
const flat_big = _result_7