# Loom

[![CI Status](https://github.com/rbx-loom/loom/actions/workflows/ci.yml/badge.svg)](https://github.com/rbx-loom/loom/workflows)
[![Coverage Status](https://coveralls.io/repos/github/rbx-loom/loom/badge.svg?branch=master)](https://coveralls.io/github/rbx-loom/loom)
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

## Quick look

```ts
interface User { name: string, age: number }
let user = new User { name: "Ada", age: 30 };
let { name, age } = user;
```

```luau
type User = {
  read name: string,
  read age: number,
}
const user = { name = "Ada", age = 30 }
const name = user.name
const age = user.age
```

More in [Destructuring](#destructuring) and [Tuples](#tuples) below.

## Table of Contents

- [Features](#features)
- [Upcoming Features](#upcoming-features)
- [Examples](#working-examples)
  - [Variables & Mutability](#variables--mutability)
  - [Operators](#operators)
  - [Generic Types](#generic-types)
  - [Number Literals](#number-literals)
  - [Reassignment & Chained Assignment](#reassignment--chained-assignment)
  - [Functions](#functions)
  - [Arrays](#arrays)
  - [Destructuring](#destructuring)
  - [Tuples](#tuples)
  - [nameof](#nameof)
  - [Ranges](#ranges)
  - [Enums](#enums)
  - [Control Flow](#control-flow)
  - [Declare Statements & Casting](#declare-statements--casting)
  - [Function Types](#function-types)
  - [Interfaces](#interfaces)
  - [While Loops](#while-loops)
  - [Sealed & Declared Interfaces](#sealed--declared-interfaces)
  - [`after` Statements](#after-statements)
  - [`every` Statements](#every-statements)
  - [For Loops](#for-loops)
  - [Ternary Operator](#ternary-operator)
  - [keyof](#keyof)
  - [Result Pattern](#result-pattern)
  - [Panics and `[fallible]`](#panics-and-fallible)
  - [Deprecation](#deprecation)
  - [Error Propagation](#error-propagation)
  - [Array.join()](#arrayjoin)
  - [Range.clamp()](#rangeclamp)
  - [String Methods](#string-methods)
  - [Pattern Matching](#pattern-matching)
  - [Discriminated Unions](#discriminated-unions)
  - [Optional Chaining](#optional-chaining)
  - [Null-Forgiving Expression](#null-forgiving-expression)
  - [Instance Helpers](#instance-helpers)
  - [string() & number()](#string--number)
  - [Decorators](#decorators)
  - [Traits & implementations](#traits--implementations)
  - [typeof](#typeof)
  - [Events](#events)
  - [Exports](#exports)
  - [Imports & Modules](#imports--modules)
  - [`in` operator](#in-operator)
  - [`type_is`](#type_is)
  - [Operator Overloading](#operator-overloading)
  - [Type Indexing](#type-indexing)
  - [Serialization](#serialization)
- [Contributing](#contributing)
- [License](#license)

## Features

- **Immutability by default** – Variables, fields, and arrays are immutable unless explicitly marked `mut`
- **Structural type system** – Duck typing with compile-time safety
- **Modern syntax** – Familiar syntax inspired by Rust and TypeScript
- **Rich type inference** – Minimal annotations required
- **Extended number literals** – Automatic math for units of time and frequency, as well as binary/octal/hex support
- **Range expressions** – `1..10` for slicing and bounds
- **`nameof` operator** – Get names as strings at compile time. See [example](#nameof).
- **Flow-sensitive typing** - Loom supports discriminated unions and narrowing to the correct union member based on a common property. See
  [example](#discriminated-unions).
- **Pattern matching** – `match` expressions with literal, range, guard, or-pattern, and destructuring arms, plus exhaustiveness checking over unions. See
  [example](#pattern-matching).
- **Optional chaining** – `?.` short-circuits through nullable member and index access instead of throwing. See [example](#optional-chaining).
- **Null-forgiving expression** – `!` strips optionality from a type as a compile-time-only assertion, no runtime check. See [example](#null-forgiving-expression).
- **Default parameter values** – Omit trailing arguments at the call site and fall back to a default. See [example](#functions).
- **Generic functions and types** – Full support for type parameters including constraints and defaults
- **Result pattern for errors** – Error handling uses the result pattern from Rust, no more `pcall`s. See [example](#result-pattern).
- **Error propagation** – The postfix `?` operator unwraps a `Result<T, E>`, returning early on failure - same idea as Rust's `?`. See
  [example](#error-propagation).
- **Events** – Built-in user events with shorthand syntax. See [example](#events).
- **Traits** – Define reusable behavior that interfaces can implement, enabling shared APIs and generic constraints that reflect behavior, including an
  explicit `@` self receiver inside implementations. See [example](#traits--implementations).
- **Named imports/exports** - See [example](#exports)
- **Modules & packages** – Relative imports resolve inside a project, bare specifiers (`math`, `scope/math`) name a package declared in `[dependencies]`, and a
  dependency's Luau is written into the consuming project's output. See [example](#imports--modules).
- **Binary serialization** – `[serializable]` interfaces get generated `buffer`-backed codecs, with `[packed]` bit-packing and delta encoding for sending only
  what changed. No runtime schema is walked. See [example](#serialization).
- **Operator overloading** – Traits carrying `[luau_metamethod]` methods make `+`, `==`, `<` and friends work on your own types. See
  [example](#operator-overloading).
- **Type indexing** – `Foo["bar"]`, enum member types, and indexing through a generic parameter, all resolved at compile time. See [example](#type-indexing).
- **Language server** – Diagnostics, hover, go-to-definition, and completion over `.loom` files.
- **Watch mode** – `loom watch` rebuilds on change.
- **Indices starting at one** – Same as Luau for familiarity
- **Zero-cost abstractions** – Transpiles to idiomatic Luau with minimal overhead
- **Batteries included** - Comes with a set of built-in compile-time macros included with data types such as [Array.join()](#arrayjoin),
  [Range.clamp()](#rangeclamp), or [String Methods](#string-methods), fully-typed standard libraries like `math`, and Roblox-specific
  [Instance Helpers](#instance-helpers)
- **Destructuring** – Bind array elements or object fields straight out of a value, including renaming a field on bind. See [example](#destructuring).
- **Tuple types** – Fixed-arity, positional types with their own literal, indexing, destructuring, and `match` pattern syntax, plus a `Tuple` generic
  constraint for variadic-tuple rest parameters. See [example](#tuples).
- **Decorators** – Ordinary functions applied with `[attr]` syntax: behavior-wrapping on functions, zero-cost compile-time metadata everywhere else, queried
  with `get_metadata`/`has_attribute` and restricted per-target with `attribute_usage`. See [example](#decorators).

## Upcoming Features

- `defer` statements (#73)
- Pipe operators (#64)
- Generic `event` declarations (#132)
- Mapped object types (#75)
- `export * from "./module"` (#169)
- Package management & installation pipeline (#111 & #112 respectively)
- Context-aware and property auto-completion in the language server (#174 & #172)
- Linter AST visitor (#18)

---

## Working Examples

Each example is separated by a line. Top code is written in Loom, bottom code is the Luau output.

### Variables & Mutability

```rs
let x: bool = false;
```

```luau
const x: boolean = false
```

---

```rs
mut x = 1;
```

```luau
local x = 1
```
---
### Operators

```rs
let s = "abc" + "def";
```

```luau
local s = "abc" .. "def"
```

---

```rs
let x = 1 & 2 & 3;
```

```luau
local x = bit32.band(1, 2, 3)
```
---
### Generic Types

```rs
type Union<A, B> = A | B;
let x: Union<bool, string> = false;
```

```luau
type Union<A, B> = A | B
const x: Union<boolean, string> = false
```
---
### Number Literals

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
---
### Reassignment & Chained Assignment

```rs
mut x = 69;
x = 420;
```

```luau
local x = 69
x = 420
```

---

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
---
### Functions

Loom supports shorthand function bodies that return single expressions.

```rs
fn one -> 1;
```

```luau
const function one()
	return 1
end
```

---

```rs
fn id<T>(value: T) -> value;
```

```luau
const function id<T>(value: T)
	return value
end
```

---

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

---

Trailing parameters can declare a default value, used whenever the argument is omitted at the call site.

```rs
fn greet(name: string, greeting: string = "Hello") -> print(greeting + ", " + name + "!");

greet("Ada");
greet("Ada", "Hi");
```

```luau
const function greet(name: string, greeting: string?)
	if greeting == nil then
		greeting = "Hello"
	end
	return print(greeting .. ", " .. name .. "!")
end
greet("Ada")
greet("Ada", "Hi")
```
---
### Arrays

```rs
let arr: number[] = [1, 2, 3];
```

```luau
const arr: { number } = {1, 2, 3}
```

---

Arrays are immutable by default, but can be declared as mutable.

```rs
let arr: number[mut] = mut [1, 2, 3];
```

```luau
const arr: { number } = {1, 2, 3}
```

---

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
---
## Destructuring

Bind array elements or object fields directly out of a value.

```rs
let array = [1, 2, 3];
let [first, second] = array;
```

```luau
const array = {1, 2, 3}
const first = array[1]
const second = array[2]
```

---

```rs
interface User { name: string, age: number }
let user = new User { name: "Ada", age: 30 };
let { name, age } = user;
```

```luau
type User = {
  read name: string,
  read age: number,
}
const user = { name = "Ada", age = 30 }
const name = user.name
const age = user.age
```

---

A field can be bound under a different name.

```rs
let { age: userAge } = user;
```

```luau
const userAge = user.age
```
---
## Tuples

Tuples are a fixed-arity type with a positional type per element, distinct from arrays.

```rs
let my_tuple: (string, number) = ("abc", 420);
print(my_tuple[1]);
print(my_tuple[2]);
```

```luau
const my_tuple: { string | number } = {"abc", 420}
print(my_tuple[1])
print(my_tuple[2])
```

---

Returning a tuple literal directly returns the raw values - no table is ever built.

```rs
fn returns_tuple: (string, number) {
    return ("abc", 420);
}
```

```luau
const function returns_tuple(): (string, number)
	return "abc", 420
end
```

---

Returning a tuple-typed value that already lives in a table unpacks it instead.

```rs
fn returns_tuple: (string, number) {
    let t = ("abc", 420);
    return t;
}

let (one, two) = returns_tuple();
```

```luau
const function returns_tuple(): (string, number)
	const t = {"abc", 420}
	return table.unpack(t)
end
const one, two = returns_tuple()
```

---

Tuples can also be matched positionally.

```rs
let t: (string, number) = ("abc", 420);
let result = match t {
    (a, b) -> a,
    _ -> "none",
};
```

```luau
const t: { string | number } = {"abc", 420}
local _match
if typeof(t) == "table" then
	const a = t[1]
	const b = t[2]
	_match = a
else
	_match = "none"
end
const result = _match
```

---

The `Tuple` generic constraint expands a rest parameter into positional arguments matching the tuple's arity.

```rs
declare fn something<T: Tuple>(..args: T): void;
something::<(string, number)>("abc", 420);
```

```luau
something("abc", 420)
```
---
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

---

```rs
let range = 1..10;
let name = nameof(range.minimum);
```

```luau
const range = { minimum = 1, maximum = 10 }
const name = "range.minimum"
```
---
### Ranges

Ranges are constructs that represent a minimum and a maximum number.

```rs
let range = 1..10;
```

```luau
const range = { minimum = 1, maximum = 10 }
```

---

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

---

```rs
let arr = [1, 2, 3, 4, 5];
let slice = arr[1..3];
```

```luau
const arr = {1, 2, 3, 4, 5}
const _length = #arr
const slice = table.move(arr, math.clamp(1, 1, _length), math.clamp(3, 1, _length), 1, {})
```

---

As well as strings.

```rs
let s = "abcdef";
let slice = s[1..3];
```

```luau
const s = "abcdef"
const slice = string.sub(s, 1, 3)
```

---

```rs
let s = "abcdef";
let char = s[1];
```

```luau
const s = "abcdef"
const char = string.sub(s, 1, 1)
```

---

```rs
let min = (1..10).minimum;
```

```luau
const min = ({ minimum = 1, maximum = 10 }).minimum
```
---
### Enums

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

---

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
---
### Control Flow

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
---
### Declare Statements & Casting

Declare statements allow you to declare types for symbols that may not exist in your file but you know exist in your environment.

```rs
declare fn print(msg: unknown): void;
print("hello, world!");
```

```luau
print("hello, world!")
```

---

```ts
declare let x: number;
let y = x + 1;
```

```luau
const y = x + 1
```

---

```rs
let unknown = 69 as unknown;
```

```luau
const unknown = (69 :: unknown)
```
---
### Function Types

```rs
type Callback = fn(): void
```

```luau
type Callback = () -> ()
```
---
### Interfaces

```ts
interface HasName {
    name: string;
}

interface HasAge {
    age: number;
}

interface Person: HasName, HasAge {
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

---

```ts
interface ImmutRecord<K, V> {
    [K]: V;
}
```

```luau
type ImmutRecord<K, V> = { read [K]: V }
```

---

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

---

```ts
interface Person {
    name: string;
    mut
    age: number;
}

let runic = new Person { name: "Runic", age: 21 };
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
---
### While Loops

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
---
### Sealed & Declared Interfaces

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

---

In this example Foo cannot be used as a constraint to other interfaces.

```cs
sealed interface Foo { bar: string }
```

```luau
type Foo = {
	read bar: string
}
```
---
### `after` Statements

After statements are a shorthand to `task.delay`. They **never yield**.

```cs
after 100ms {
    print("done!");
}
```

```luau
task.delay(0.1, print, "done!")
```

---
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
### `every` Statements

Every statements schedule a function to be called forever (or until the optional condition returns false) with a specified duration.

```cs
every 500ms {
    print("half a second passed!");
}
```

```luau
Loom.every(0.5, nil, print, "half a second passed!")
```

---

```cs
mut counter = 0;
every 10hz while counter < 10 {
    counter += 1;
    print("polling while counter < 10");
}
```

```luau
local counter = 0
Loom.every(0.1, function()
    return counter < 10
end, function()
    counter += 1
    print("polling while counter < 10")
end)
```

---

### For Loops

```ts
let collection = [1, 2, 3, 4];
for v, i : collection {
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

---

```rs
for n : 1. .10
    print(n)
```

```luau
for n in 1, 10 do
	print(n)
end
```

---

```rs
for n : 10..1
    print(n)
```

```luau
for n in 10, 1, -1 do
	print(n)
end
```
---
### Ternary Operator

```ts
let condition = true
let value = condition ? 69 : none;
```

```luau
const condition = true
const value = if condition then 69 else nil
```
---
### keyof

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
---
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

### Combinators

A `Result<T, E>` carries the usual combinators. None of them survive to runtime - each is lowered inline, so a
`Result` stays a two-field table rather than one carrying six closures, and the receiver is evaluated exactly once.

| Combinator | Result | Panics? |
| --- | --- | --- |
| `unwrap()` | the value, or raises the error | yes |
| `expect(message)` | the value, or raises `message` | yes |
| `unwrap_or(fallback)` | the value, or `fallback` | no |
| `unwrap_or_else(compute)` | the value, or `compute(error)` | no |
| `map(transform)` | `Result<U, E>` with `transform` applied to the value | no |
| `and_then(transform)` | `transform(value)`, or the original error | no |

```rs
let a = unsafe_function(true).unwrap_or(0);
let b = unsafe_function(true).map(double).unwrap_or(0);
```

```luau
const _result = unsafe_function(true)
const a = if _result.ok then _result.value else 0
const _result_1 = unsafe_function(true)
const _result_2 = if _result_1.ok then { ok = true, value = double(_result_1.value) } else _result_1
const b = if _result_2.ok then _result_2.value else 0
```
---
## Panics and `[fallible]`

`unwrap` and `expect` raise the error rather than returning it, as does `error()`. An operation that can panic is
only allowed inside a function marked `[fallible]`, so a signature never hides the fact that calling it may end
the thread.

```rs
[fallible]
fn load_or_die(): number {
    return unsafe_function(true).unwrap();  ## ok - the function declares it
}

fn load(): Result<number, string> {
    let value = unsafe_function(true)?;     ## ok - propagates, never panics
    return Result.ok(value);
}

fn careless(): number {
    return unsafe_function(true).unwrap();  ## error - not marked [fallible]
}
```

Calling a `[fallible]` function is itself a panicking operation, so the marker travels up until something handles
the `Result` instead. Two ways out, and the signature tells you which one a function chose: **propagate** by
returning `Result` and using `?`, or **panic** by marking `[fallible]` and using `unwrap`.

Top-level code cannot be marked, and neither can a function expression - an event handler runs on a thread Roblox
owns, with no caller to propagate to and nothing above it to recover. Both must handle the `Result` inline.

Loom cannot catch every Luau fault. Integer division by zero, stack overflow and script timeouts are raised by the
VM itself and are outside the `Result` discipline entirely.
---
## Deprecation

`[deprecated]` marks a member whose use should be warned about, with an optional replacement hint. It is generated
onto every Roblox API member the engine marks deprecated.

```rs
[deprecated("use new_way instead")]
fn old_way(): number -> 1;

let n = old_way();  ## warning: 'old_way' is deprecated. (use new_way instead)
```
---
## Error Propagation

The postfix `?` operator unwraps a `Result<T, E>` in one step: on success it evaluates to the `value`; on failure it returns the whole `Result` from
the enclosing function immediately, rather than making you write the `if !result.ok return result;` check by hand every time. It requires the
enclosing function to declare a `Result<T, E>` return type, and the propagated error must be assignable to that function's own error type.

```rs
fn some_other_unsafe_fn(): Result<number, string> {
    return Result.ok(1);
}

fn unsafe_fn(): Result<number, string> {
    let value = some_other_unsafe_fn()?;
    return Result.ok(69 + value);
}
```

```luau
const function some_other_unsafe_fn(): Result<number, string>
  return { ok = true, value = 1 }
end
const function unsafe_fn(): Result<number, string>
  const _result = some_other_unsafe_fn()
  if not _result.ok then
    return _result
  end
  const value = _result.value
  return { ok = true, value = 69 + value }
end
```

`foo()?.bar` always means [optional chaining](#optional-chaining) on `foo()` itself - `?.` is its own token, tokenized before `?` ever gets a chance
to mean error propagation. To propagate and then access a member, parenthesize: `(foo()?).bar`.

---
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

---

```ts
let arr = [1, 2, 3, 4];
print(arr.length)
```

```luau
const arr = {1, 2, 3, 4}
print(#arr)
```

---

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
---
## Range.length

```rs
print((1..10).length)
```

```luau
print(10)
```

---

```rs
let range = 1..10;
print(range.length)
```

```luau
const range = { minimum = 1, maximum = 10 }
print(1 + math.abs(range.maximum - range.minimum))
```
---
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

---

```rs
let range = 1..10;
print(range.clamp(69))
```

```luau
const range = { minimum = 1, maximum = 10 }
print(math.clamp(69, range.minimum, range.maximum))
```
---
## String Methods

Strings come with a set of built-in methods, compiling straight down to Luau's `string` library with no runtime overhead.

```rs
let s = "  Hello, World!  ";
print(s.trim());
print(s.upper());
print(s.lower());
print(s.split(", "));
print(s.has("World"));
print(s.starts_with("  Hello"));
print(s.ends_with("!  "));
print(s.length);
```

```luau
const s = "  Hello, World!  "
print(string.gsub(s, "^%s*(.-)%s*$", "%1"))
print(string.upper(s))
print(string.lower(s))
print(string.split(s, ", "))
print(string.find(s, "World", 1, true) ~= nil)
const _prefix = "  Hello"
print(string.sub(s, 1, #_prefix) == _prefix)
const _suffix = "!  "
print(string.sub(s, #s - #_suffix + 1) == _suffix)
print(#s)
```

`reverse`, `repeat`, `byte`, and `replace` are also available. Fully-typed versions of Luau's built-in libraries, like `math`, are usable as-is with no
import required.

---

## Pattern Matching

`match` expressions support literal, range, guard (`when`), or-pattern (`|`), and destructuring arms, compiling to a chain of `if`/`elseif` with no runtime
matcher.

```rs
fn describe(n: number): string -> match n {
    0 | 1 -> "zero or one",
    2..10 -> "small",
    m when m > 0 -> "positive",
    _ -> "other",
};

print(describe(1));
print(describe(5));
print(describe(50));
print(describe(-1));
```

```luau
const function describe(n: number): string
	local _match
	if n == 0 or n == 1 then
		_match = "zero or one"
	elseif typeof(n) == "number" and n >= 2 and n <= 10 then
		_match = "small"
	elseif n > 0 then
		const m = n
		_match = "positive"
	else
		_match = "other"
	end
	return _match
end
print(describe(1))
print(describe(5))
print(describe(50))
print(describe(-1))
```
---
## Discriminated Unions

Interfaces sharing a common literal-typed property form a discriminated union. Narrowing on that property inside a branch gives you safe access to the
member's other fields, without a `match`.

```rs
interface Circle { kind: "circle", radius: number }
interface Square { kind: "square", side: number }

type Shape = Circle | Square;

fn area(shape: Shape): number ->
    shape.kind == "circle" ? math.pi * shape.radius * shape.radius : shape.side * shape.side;
```

```luau
type Circle = {
	read kind: "circle",
	read radius: number,
}
type Square = {
	read kind: "square",
	read side: number,
}
type Shape = Circle | Square
const function area(shape: Shape): number
	return if shape.kind == "circle" then math.pi * shape.radius * shape.radius else shape.side * shape.side
end
```
---
## Optional Chaining

`?.` short-circuits to `nil` the moment any link in the chain is `nil`, instead of erroring.

```rs
interface Inner { c: number }
interface Outer { b: Inner? }

let a: Outer? = none;
let x = a?.b?.c;
```

```luau
type Inner = {
	read c: number,
}
type Outer = {
	read b: Inner?,
}
const a: Outer? = nil
const x = if a ~= nil then if a.b ~= nil then a.b.c else nil else nil
```
---
## Null-Forgiving Expression

`!` asserts an expression isn't `nil` without a runtime check — a compile-time-only type cast, purely for the cases where you know more than the type checker does.

```rs
let nullable: number? = 5;
let forgiven = nullable!;
```

```luau
const nullable: number? = 5
const forgiven = nullable :: Loom.NonNullable<typeof(nullable)>
```
---
## Instance Helpers

Roblox `Instance`s get typed helpers for common patterns, with generic type arguments compiling to `IsA` checks.

```rs
fn on_touch(part: Part): void {
    if part.is_a::<Part>() {
        print(part.get_children::<Part>());
    }
}
```

```luau
const function on_touch(part: Part): ()
	if part:IsA("Part") then
		const _result: { Part } = {}
		for _, child in part:GetChildren() do
			if not child:IsA("Part") then continue end
			table.insert(_result, child)
		end
		print(_result)
	end
end
```

`get_descendants`, `find_first_child_of_class`, `find_first_child_which_is_a`, `find_first_ancestor_of_class`, and `find_first_ancestor_which_is_a` follow the
same pattern.
---
## string() & number()

`string()` and `number()` mirror Luau's `tostring`/`tonumber` exactly, and fold to a literal at compile time whenever their argument is already known.

```rs
let digits = string(69420);
let n = number(digits);
```

```luau
const digits = "69420"
const n = tonumber(digits)
```

---

Radix is inferred from a `0x` prefix instead of a second argument.

```rs
let n = number("0xF00D")
```

```luau
const n = 61453
```
---
## Decorators

Decorators are ordinary functions applied with `[attr]`/`[attr(args)]` syntax, but what they do depends on what they decorate. On a function, a decorator wraps every call — the decorator receives a thunk that produces the original result plus the function's name, and can transform it, retry it, log around it, or anything else. Decorator factories work too: invoking the attribute expression itself (`[log("info")]`) configures the decorator before it wraps anything.

```rs
fn log(f: fn(): void, name: string): void {
    print(name);
    f();
}

[log]
fn greet(name: string) {
    print($"hi, {name}");
}
```

```luau
const function log(f: () -> (), name: string): ()
  print(name)
  f()
end
const function greet(name: string)
  return log(function()
    print(`hi, {name}`)
  end, "greet")
end
```

A function decorator can opt out of wrapping by marking its own declaration `[metadata_only]`. Applied to a function this way, it behaves exactly like a decorator on an interface, property, or event: purely passive metadata, compile-time-constant arguments only, nothing emitted for it, no thunk required.

```rs
[metadata_only]
fn replicated(): void {}

[replicated]
fn greet(name: string) {
    print($"hi, {name}");
}
```

```luau
const function replicated(): ()
end
const function greet(name: string)
  print(`hi, {name}`)
end
```

On an interface, a property, or an interface-nested event, a decorator is purely passive metadata — it never runs, never wraps anything, and costs nothing at runtime. Its arguments must be compile-time constants, and nothing is emitted for it anywhere except at an actual query. `get_metadata` resolves entirely at compile time, folding straight to the matched attribute's arguments (or `none` if it isn't present); `has_attribute` folds the same way to a plain `true`/`false`.

```rs
[attribute_usage(AttributeTargets.Property)]
fn replicated(): void {}

interface Player {
    [replicated]
    health: number

    name: string
}

let is_replicated = has_attribute::<Player>("health", replicated);
```

```luau
const function replicated(): ()
end
type Player = {
  read health: number,
  read name: string,
}
const is_replicated = true
```

`attribute_usage` restricts which kinds of declarations a decorator may be applied to, using the `AttributeTargets` bitflag enum — combine targets with `|` just like any other bitflags.

```rs
enum AttributeTargets {
    Function = 1 << 0,
    Interface = 1 << 1,
    Property = 1 << 2,
    Event = 1 << 3,
}

[attribute_usage(AttributeTargets.Property | AttributeTargets.Event)]
fn replicated(): void {}
```

---
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
const user = setmetatable({ name = "Runic", age = 21 }, ToString_for_User) :: User
print(user:to_string())
```

---

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
    fn serialize -> $"{name}, {age}"
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
	return `{self.name}, {self.age}`
end
const user = setmetatable({ name = "Runic", age = 21 }, Serialize_string_for_User) :: User
print(user:serialize())
```

---

Bare names resolve to the implementing interface's members automatically, but `@` is available as an explicit self receiver when you want it.

```rs
trait FlagContainer {
    fn has_flag(flag: number): bool;
}

interface Flags {
    [number]: bool;
}

implement FlagContainer for Flags {
    fn has_flag(flag) -> flag in @;
}

let flags = new Flags { [69]: true, [420]: true };
print(person.has_flag(69));
```

```luau
const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
type FlagContainer = {
	has_flag: (number) -> boolean,
}
type Flags = { [number]: boolean } & FlagContainer
local FlagContainer_for_Flags = {}
FlagContainer_for_Flags.__index = FlagContainer_for_Flags
FlagContainer_for_Flags = FlagContainer_for_Flags :: Flags
function FlagContainer_for_Flags.has_flag(self: Flags, flag)
	return self[flag] ~= nil
end
const flags = setmetatable({ [69] = true, [420] = true }, FlagContainer_for_Flags) :: Flags
print(flags:has_flag(69))
```
---
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
---
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

## `in` operator

Check if a key/index exists within a collection

```rs
interface Object { field: number? }
let object = new Object { field: 69 };
print("field" in object)
```

```luau
type Object = { field: number? }
const object = { field = 69 }
print(object.field ~= nil)
```

---

## Imports & Modules

A relative specifier resolves inside the importing project. Only exported members cross the boundary, and the require path is derived from where the output
lands.

```rs
// math_utils.loom
export fn square(n: number): number -> n * n;
export let tau = 6.28318;
```

```rs
// main.server.loom
import { square, tau } from "./math_utils";

print(square(4));
print(tau);
```

```luau
-- math_utils.luau
const function square(n: number): number
  return n * n
end
const tau = 6.28318
return { square = square, tau = tau }
```

```luau
-- main.server.luau
const math_utils = require("@game/ServerScriptService/Loom/math_utils")
const square = math_utils.square
const tau = math_utils.tau
print(square(4))
print(tau)
```

A bare specifier names a package instead of a path. `import { clamp } from "math"` reaches the root publishing that package, and only a package listed in the
importing project's `[dependencies]` is importable. A dependency's compiled Luau is written into the *consuming* project's output, under
`<output>/packages/<scope>/<name>`.

---

## `type_is`

Narrow an `unknown` to a concrete type at runtime.

```rs
let v = 69 as unknown;
if type_is(v, "number") {
    print(v + 1);
}
```

```luau
const v = 69 :: unknown
if typeof(v) == "number" then
  print(v + 1)
end
```

---

## Operator Overloading

A trait method carrying `[luau_metamethod]` binds an operator to your own type. The call site keeps working as a plain method call too.

```rs
interface Location {
    position: number;
}

trait Add<T> {
    [luau_metamethod("__add")]
    fn add(other: T): T;
}

implement Add<Location> for Location {
    fn add(other) -> new Location { position: position + other.position }
}

let start = new Location { position: 69 };
let finish = new Location { position: 420 };
let result1 = start + finish;
let result2 = start.add(finish);
```

```luau
type Location = {
  read position: number,
} & Add<Location>
type Add<T> = {
  add: (Add<T>, T) -> T,
}
local Add_Location_for_Location = {}
Add_Location_for_Location.__index = Add_Location_for_Location
Add_Location_for_Location = Add_Location_for_Location :: Location
function Add_Location_for_Location.add(self: Location, other)
  return setmetatable({ position = self.position + other.position }, Add_Location_for_Location) :: Location
end
Add_Location_for_Location.__add = Add_Location_for_Location.add
const start = setmetatable({ position = 69 }, Add_Location_for_Location) :: Location
const finish = setmetatable({ position = 420 }, Add_Location_for_Location) :: Location
const result1 = start + finish
const result2 = start:add(finish)
```

---

## Type Indexing

`Target[Index]` names the type of a member. It works on interfaces, on enums, and through a generic parameter - the index resolves when the generic is
instantiated. An enum member's type expands to its literal value.

```rs
interface Config { retries: number }

type Retries = Config["retries"];

enum Message { Hello, Goodbye }
enum Kind : string { Local = "local", Remote = "remote" }

type HelloTag = Message["Hello"];
type LocalKind = Kind["Local"];

type Pick<K: keyof(Config)> = Config[K];
let attempts: Pick<"retries"> = 3;
```

```luau
type Config = {
  read retries: number,
}
type Retries = index<Config, "retries">
type Message = number
type Kind = "local" | "remote"
type HelloTag = number
type LocalKind = "local"
type Pick<K> = index<Config, K & keyof<Config>>
const attempts: Pick<"retries"> = 3
```

---

## Serialization

`[serializable]` generates a `buffer`-backed codec for an interface. Sized types (`u8`, `i16`, `f32`, `string<u8>`) pin the width; `[packed]` packs booleans,
optionals and union tags into a shared bit header. `diff_binary`/`apply_diff_binary` send only the fields that changed.

There is no runtime schema - every offset the type pins at compile time becomes a literal in the emitted code.

```rs
[serializable, packed]
interface PlayerState {
  health: u8;
  name: string<u8>;
}

let state = new PlayerState { health: 100, name: "Ada" };
let payload = serialize_binary(state);
let restored = deserialize_binary::<PlayerState>(payload);
if restored.ok
  print(restored.value.health);

let diff = diff_binary(state, new PlayerState { health: 90, name: "Ada" });
let updated = apply_diff_binary(state, diff);
```

```luau
const function PlayerState_serialize_binary(value: PlayerState): Loom.Serialized
  const b = buffer_create(1 + 1 + #value.name)
  buffer_writeu8(b, 0, value.health)
  const name_value = value.name
  const name_length = #name_value
  buffer_writeu8(b, 1, name_length)
  local offset = 2
  buffer_writestring(b, offset, name_value)
  offset += name_length
  return { buffer = b }
end
const function PlayerState_deserialize_binary(serialized: Loom.Serialized): Loom.Result<PlayerState, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 2 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const name_length = buffer_readu8(b, 1)
  local offset = 2
  if buffer_len(b) < offset + name_length then
    return { ok = false, error = { kind = "invalid_length", field = "name" } }
  end
  const name = buffer_readstring(b, offset, name_length)
  offset += name_length
  return { ok = true, value = { health = buffer_readu8(b, 0), name = name } }
end
```

Deserializing returns a `Result`, so a truncated or malformed payload reports rather than throwing. Arrays, `Record` maps, nested interfaces, optionals,
discriminated unions, Roblox datatypes (`Vector3<i16>`, `CFrame<f32>`) and unserializable values passed through as blobs are all supported.

---

## Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) for details on the process for submitting pull requests and building language
features.

---

## License

This project is licensed under the Apache-2.0 License - see the [LICENSE](LICENSE) file for details.
