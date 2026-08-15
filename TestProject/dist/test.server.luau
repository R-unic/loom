type Nullable<T> = unknown
type NumberOptionalArray = { number | nil }
const x: NumberOptionalArray = {1, nil, 69, nil}
