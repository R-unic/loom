# Loom

[![CI Status](https://github.com/rbx-loom/loom/actions/workflows/ci.yml/badge.svg)](https://github.com/rbx-loom/loom/workflows)
[![Coverage Status](https://coveralls.io/repos/github/R-unic/loom/badge.svg?branch=master)](https://coveralls.io/github/R-unic/loom)
[![License: Apache-2.0](https://img.shields.io/badge/License-Apache--2.0-yellow.svg)](https://opensource.org/licenses/apache-2.0)

### A domain-specific language for Roblox that transpiles to Luau.

<br/>
<img width="701" height="195" alt="image" src="https://github.com/user-attachments/assets/f6c34f43-b802-459f-9b27-a3d77c2b74e5" />
<br/>
<br/>

> ⚠️ This project is a work-in-progress.
> - Nothing is final.
> - Breaking changes may occur at any time.
> - Expect bugs.

## Features

- **Immutability by default** – Variables, fields, and arrays are immutable unless explicitly marked `mut`
- **Structural type system** – Duck typing with compile-time safety
- **Modern syntax** – Familiar syntax inspired by Rust and TypeScript
- **Rich type inference** – Minimal annotations required
- **Extended number literals** – Automatic math for units of time and frequency, as well as binary/octal/hex support
- **Range expressions** – `1..10` for slicing and bounds
- **`nameof` operator** – Get names as strings at compile time. See [example](#nameof).
- **Flow-sensitive typing** - Loom supports discriminated unions and narrowing to the correct union member based on a common property
- **Generic functions and types** – Full support for type parameters including constraints and defaults
- **Result pattern for errors** – Error handling uses the result pattern from Rust, no more `pcall`s. See [example](#result-pattern).
- **Events** – Built-in user events with shorthand syntax. See [example](#events).
- **Traits** – Define reusable behavior that interfaces can implement, enabling shared APIs and generic constraints that reflect behavior
- **Indices starting at one** – Same as Luau for familiarity
- **Zero-cost abstractions** – Transpiles to idiomatic Luau with minimal overhead
- **Batteries included** - Comes with a set of built-in compile-time macros included with data types such as [Array.join()](#arrayjoin)
  or [Range.clamp()](#rangeclamp)

## Upcoming Features

- `x in collection`
- `defer` statements
- Full module system (imports/exports)
- Standard libraries (math, string, buffer, etc.)

---

## Working Examples

Each example is separated by a line. Top code is written in Loom, bottom code is the Luau output.

```rs
let x: bool = false;
```

```luau
const x: boolean = false
```

##

```rs
mut x = 1;
```

```luau
local x = 1
```

##

```rs
let s = "abc" + "def";
```

```luau
local s = "abc" .. "def"
```

##

```rs
let x = 1 & 2 & 3;
```

```luau
local x = bit32.band(1, 2, 3)
```

##

```rs
type Union<A, B> = A | B;
let x: Union<bool, string> = false;
```

```luau
type Union<A, B> = A | B
const x: Union<boolean, string> = false
```

##

Loom supports extended number literals that let you do boilerplate math to convert to a specific unit instantaneously.

```rs
let a = 10s;
let b = 100ms;
let c = 10m;
let d = 1h;
let e = 16hz;
let f = 100_000_000
let hex = 0xF00D;
let binary = 0b11001;
let octal = 0o400;
```

```luau
const a = 10
const b = 0.1
const c = 600
const d = 3600
const e = 0.0625
const f = 100000000
const hex = 61453
const binary = 25
const octal = 256
```

##

```rs
mut x = 69;
x = 420;
```

```luau
local x = 69
x = 420
```

##

```rs
mut x = 69;
mut y = 420;
let z = x = y = 1;
```

```luau
local x = 69
local y = 420
y = 1
x = y
const z = x
```

##

Loom supports shorthand function bodies that return single expressions.

```rs
fn one -> 1;
```

```luau
const function one()
	return 1
end
```

##

```rs
fn id<T>(value: T) -> value;
```

```luau
const function id<T>(value: T)
	return value
end
```

##

```rs
fn id<T: number>(value: T): T {
    return value;
}
id::<number>(69)
```

```luau
const function id<T>(value: T & number): T & number
	return value
end
id(69)
```

##

```rs
let arr: number[] = [1, 2, 3];
```

```luau
const arr: { number } = {1, 2, 3}
```

##

Arrays are immutable by default, but can be declared as mutable.

```rs
let arr: number[mut] = mut [1, 2, 3];
```

```luau
const arr: { number } = {1, 2, 3}
```

##

Assignments are expressions in loom.

```rs
let arr = mut [1, 2, 3];
let x = arr[1] = 69;
```

```luau
const arr: { number } = {1, 2, 3}
const x = 69
arr[1] = x
```

## nameof

The `nameof` operator can be used to read the tokens of `Name` expressions as a string.

```rs
let abc = 69;
let name = nameof(abc)
```

```luau
const abc = 69;
const name = "abc"
```

##

```rs
let range = 1..10;
let name = nameof(range.minimum);
```

```luau
const range = { minimum = 1, maximum = 10 }
const name = "range.minimum"
```

##

Ranges are constructs that represent a minimum and a maximum number.

```rs
let range = 1..10;
```

```luau
const range = { minimum = 1, maximum = 10 }
```

##

They can be used to slice arrays.

```rs
let range = 1..3;
let arr = [1, 2, 3, 4, 5];
let slice = arr[range];
```

```luau
const range = { minimum = 1, maximum = 3 }
const arr = {1, 2, 3, 4, 5}
const _length = #arr
const slice = table.move(arr, math.clamp(range.minimum, 1, _length), math.clamp(range.maximum, 1, _length), 1, {})
```

##

```rs
let arr = [1, 2, 3, 4, 5];
let slice = arr[1..3];
```

```luau
const arr = {1, 2, 3, 4, 5}
const _length = #arr
const slice = table.move(arr, math.clamp(1, 1, _length), math.clamp(3, 1, _length), 1, {})
```

##

As well as strings.

```rs
let s = "abcdef";
let slice = s[1..3];
```

```luau
const s = "abcdef"
const slice = string.sub(s, 1, 3)
```

##

```rs
let s = "abcdef";
let char = s[1];
```

```luau
const s = "abcdef"
const char = string.sub(s, 1, 1)
```

##

```rs
let min = (1..10).minimum;
```

```luau
const min = ({ minimum = 1, maximum = 10 }).minimum
```

##

Enums are named compile-time constants.

```rs
enum Abc { A, B = 69, C }
let a = Abc.A;
let b = Abc.B;
let c = Abc.C;
```

```luau
type Abc = number
const a = 0
const b = 69
const c = 70
```

##

They can also be used with strings.

```rs
enum Tag: string {
    Lava = "lava",
    Something = "something"
}
let tag = Tag.Lava
```

```luau
type Tag = "lava" | "something"
const tag = "lava"
```

##

```rs
if 69 == 420 {
    let foo = 69
} else if 69 == 69 {
    let yes = "yes"
}
```

```luau
if 69 == 420 then
	const foo = 69
elseif 69 == 69 then
	const yes = "yes"
end
```

##

Declare statements allow you to declare types for symbols that may not exist in your file but you know exist in your environment.

```rs
declare fn print(msg: unknown): void;
print("hello, world!");
```

```luau
print("hello, world!")
```

##

```ts
declare let x: number;
let y = x + 1;
```

```luau
const y = x + 1
```

##

```rs
let unknown = 69 as unknown;
```

```luau
const unknown = (69 :: unknown)
```

##

```rs
type Callback = fn(): void
```

```luau
type Callback = () -> ()
```

##

```ts
interface HasName {
    name: string;
}

interface HasAge {
    age: number;
}

interface Person

:
HasName, HasAge
{
    job: string;
}
```

```luau
type HasName = {
	read name: string;
}
type HasAge = {
	read age: number;
}
type Person = HasName & HasAge & {
	read job: string;
}
```

##

```ts
interface ImmutRecord<K, V> {
    [K]: V;
}
```

```luau
type ImmutRecord<K, V> = { read [K]: V }
```

##

In this example `S` resolves to `string`.

```ts
interface Foo {
    bar: string
}

type S = Foo["bar"];
```

```luau
type Foo = {
	read bar: string
}
type S = index<Foo, "bar">
```

##

```ts
interface Person {
    name: string;
    mut
    age: number;
}

let runic = new Person
{
    name: "Runic", age
:
    21
}
;
runic.age = 69;
```

```luau
type Person = {
	read name: string,
	age: number
}
const runic = { name = "Runic", age = 21 }
runic.age = 69
```

##

```rs
mut i = 0;
while i < 10
    i += 1;
    
print(i)
```

```luau
local i = 0
while i < 10 do
	i += 1
end
print(i)
```

##

In this example Foo is only a type and cannot be instantiated.

```ts
declare interface Foo {
    bar: string
}
```

```luau
type Foo = {
	read bar: string
}
```

##

In this example Foo cannot be used as a constraint to other interfaces.

```cs
sealed interface Foo { bar: string }
```

```luau
type Foo = {
	read bar: string
}
```

##

After statements are a shorthand to `task.delay`. They **never yield**.

```cs
after 100ms {
    print("done!");
}
```

```luau
task.delay(0.1, print, "done!")
```

```cs
after 250ms {
    let computed = 69 + 420;
	print(computed);
}
```

```luau
task.delay(0.25, function(): ()
	const computed = 69 + 420
	print(computed)
end)
```

##

```ts
let collection = [1, 2, 3, 4];
for v, i :
collection
{
    print(i);
    print(v);
}
```

```luau
const collection = {1, 2, 3, 4}
for i, v in collection do
	print(i)
	print(v)
end
```

##

```rs
for n : 1. .10
    print(n)
```

```luau
for n in 1, 10 do
	print(n)
end
```

##

```rs
for n : 10..1
    print(n)
```

```luau
for n in 10, 1, -1 do
	print(n)
end
```

##

```ts
let condition = true
let value = condition ? 69 : none;
```

```luau
const condition = true
const value = if condition then 69 else nil
```

##

In this example `K` resolves to `number | "bar" | "baz"`.

```ts
interface Foo {
    [number]: string;
    bar: string;
    baz: number;
}

type K = keyof (Foo);
```

```luau
type Foo = {
	read [number]: string,
	read bar: string,
	read baz: number
}
type K = keyof<Foo>
```

## Result Pattern

```rs
fn unsafe_function(condition: bool): Result<number, string> ->
    condition ? Result.ok(69) : Result.err("function failed!");
    
let result = unsafe_function(true);
print(result.ok ? result.value : result.error);
```

```luau
const function unsafe_function(condition: boolean): Result<number, string>
	return if condition then { ok = true, value = 69 } else { ok = false, error = "function failed!" }
end
const result = unsafe_function(true)
print(if result.ok then result.value else result.error)
```

## Array.join()

```ts
let arr = [1, 2, 3, 4];
print(arr.join())
print(arr.join(", "))
```

```luau
const arr = {1, 2, 3, 4}
print(table.concat(arr))
print(table.concat(arr, ", "))
```

##

```ts
let arr = [1, 2, 3, 4];
print(arr.length)
```

```luau
const arr = {1, 2, 3, 4}
print(#arr)
```

##

Mutable arrays support in-place methods (`push`, `pop`, `insert`, `remove`), and every array supports `index_of` and `has`.

```rs
let arr = mut [1, 2, 3];
arr.push(4);
arr.insert(1, 0);
arr.pop();
arr.remove(1);
print(arr.index_of(2));
print(arr.has(2))
```

```luau
const arr = {1, 2, 3}
table.insert(arr, 4)
table.insert(arr, 1, 0)
table.remove(arr)
table.remove(arr, 1)
print(table.find(arr, 2))
print(table.find(arr, 2) ~= nil)
```

##

```rs
print((1..10).length)
```

```luau
print(10)
```

##

```rs
let range = 1..10;
print(range.length)
```

```luau
const range = { minimum = 1, maximum = 10 }
print(1 + math.abs(range.maximum - range.minimum))
```

## Range.clamp()

```rs
print((1..10).clamp(5))
print((1..10).clamp(-10))
print((1..10).clamp(6.9 + 4.2))
```

```luau
print(5)
print(1)
print(10)
```

##

```rs
let range = 1..10;
print(range.clamp(69))
```

```luau
const range = { minimum = 1, maximum = 10 }
print(math.clamp(69, range.minimum, range.maximum))
```

## string() & number()

```rs
let digits = string(69420);
let n = number(digits);
```

```luau
const digits = tostring(69420)
const n = tonumber(digits)
```

##

```rs
let n = number("F00D", 16)
```

```luau
const n = tonumber("F00D", 16)
```

## Traits & implementations

Traits let you define reusable behavior independently of an interface's data. An implement block attaches a trait to an interface, making its methods available
on every instance without storing additional fields. During compilation, Loom generates Luau metatables that provide method dispatch while preserving type
safety.

```rs
trait ToString {
    fn to_string: string;
}

interface User {
    name: string;
    age: number;
}

implement ToString for User {
    fn to_string -> nameof(User) + " { name: ''" + name + "', age: " + string(age) + " }"
}

let user = new User { name: "Runic", age: 21 };
print(user.to_string());
```

```luau
const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
type ToString = {
	to_string: (ToString) -> string,
}
type User = {
	read name: string,
	read age: number,
} & ToString
local ToString_for_User = {}
ToString_for_User.__index = ToString_for_User
ToString_for_User = ToString_for_User :: User
function ToString_for_User.to_string(self: User)
	return "User" .. " { name: ''" .. self.name .. "', age: " .. tostring(self.age) .. " }"
end
const user = setmetatable({ name = "Runic", age = 21 }, Loom.merge_meta(ToString_for_User)) :: User
print(user:to_string())
```

##

Traits can also be implemented per generic instantiation. Multiple implementations of the same trait with different type arguments will result in an error.

```rs
trait Serialize<T> {
    fn serialize: T;
}

interface User {
    name: string;
    age: number;
}

implement Serialize<string> for User {
    fn serialize -> name + ", " + string(age)
}

let user = new User { name: "Runic", age: 21 };
print(user.serialize());
```

```luau
const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
type Serialize<T> = {
	serialize: (Serialize<T>) -> T,
}
type User = {
	read name: string,
	read age: number,
} & Serialize
local Serialize_string_for_User = {}
Serialize_string_for_User.__index = Serialize_string_for_User
Serialize_string_for_User = Serialize_string_for_User :: User
function Serialize_string_for_User.serialize(self: User)
	return self.name .. ", " .. tostring(self.age)
end
const user = setmetatable({ name = "Runic", age = 21 }, Loom.merge_meta(Serialize_string_for_User)) :: User
print(user:serialize())
```

## typeof

Inspect types of dynamic expressions.

```ts
mut my_number = 69;
type NumberType = typeof(my_number);
let x: NumberType = 420;
```

```luau
local my_number = 69
type NumberType = typeof(my_number)
const x: NumberType = 420
```

## Events

Built-in syntaxes for creating, connecting, and disconnecting.

```cs
event my_event(data: string);

fn handler(data: string): void -> print(data);

my_event += handler;
my_event("hello!");
my_event -= handler;
```

```luau
const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const my_event: Loom.Event<string> = Loom.Event.new()

const function handler(data: string): ()
    return print(data)
end

const handler_conn = my_event:Connect(handler);
my_event:Fire("hello!");
handler_conn:Disconnect();
```

---

## Exports

Expose `let` and `fn` with `export`

```rs
export let pi = 3.14;

export fn square(n: number): number -> n * n;

fn double(n: number): number -> n * 2;
```

```luau
const pi = 3.14
const function square(n: number): number
  return n * n
end
const function double(n: number): number
  return n * 2
end
return { pi = pi, square = square }
```

`double` is still emitted, but only the exported members (`pi`, `square`) appear in the returned table, so only they are visible to other modules.

---

## Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) for details on the process for submitting pull requests and building language
features.

---

## License

This project is licensed under the Apache-2.0 License - see the [LICENSE](LICENSE) file for details.
