type Vector2 = {
  read zero: Vector2,
  read create: (number, number) -> Vector2,
}
const v = Vector2.create(1, 2)
const z = Vector2.zero