type Map = { read [number]: number }
type GetMapType<K> = index<Map, K & keyof<Map>>
const x: index<Map, number> = 69
