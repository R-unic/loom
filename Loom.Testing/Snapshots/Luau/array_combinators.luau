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
const _result_3 = table.create(#numbers)
for _index_1, n in numbers do
  _result_3[_index_1] = n * 2
end
const _result_4 = table.create(#_result_3)
local _count_1 = 0
for _, n in _result_3 do
  if n > 4 then
    _count_1 += 1
    _result_4[_count_1] = n
  end
end
local _accumulator_1 = 0
for _, n in _result_4 do
  const sum = _accumulator_1
  _accumulator_1 = sum + n
end
const chained = _accumulator_1
const function triple(n: number): number
  return n * 3
end
const _result_5 = table.create(#numbers)
for _index_2, _element in numbers do
  _result_5[_index_2] = triple(_element, _index_2)
end
const tripled = _result_5