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

## Installation

Loom is distributed with [Rokit](https://github.com/rojo-rbx/rokit), a toolchain manager for Roblox projects:

```sh
rokit add rbx-loom/loom
```

This pins the compiler in the project's `rokit.toml`, so everyone working on it builds with the same version:

```toml
[tools]
loom = "rbx-loom/loom@0.1.0"
```

The published binaries are self-contained — there is no runtime or SDK to install alongside them — and cover Windows, macOS
and Linux on both x86-64 and arm64. If you would rather not use Rokit, download the archive for your platform from the
[releases page](https://github.com/rbx-loom/loom/releases) and put the binary on your `PATH`.

Then:

```sh
loom new my-game    # scaffold a project
cd my-game
loom build          # compile src/ to dist/
loom watch          # ... and keep compiling as files change
```

Every command takes the project directory as an optional argument, defaulting to the current one.

## Quick look

```ts
interface User { name: string, age: number }
let user = new User { name: "Poppy", age: 31 };
let { name, age } = user;
```

```luau
type User = {
  read name: string,
  read age: number,
}
const user = { name = "Poppy", age = 31 }
const name = user.name
const age = user.age
```

More in [Destructuring](#destructuring) and [Tuples](#tuples) below.

## Table of Contents

- [Installation](#installation)
- [Features](#features)
- [Upcoming Features](#upcoming-features)
- [Examples](#working-examples)
  - [Comments & Documentation](#comments--documentation)
  - [Variables & Mutability](#variables--mutability)
  - [Operators](#operators)
  - [Generic Types](#generic-types)
  - [Number Literals](#number-literals)
  - [Reassignment & Chained Assignment](#reassignment--chained-assignment)
  - [Functions](#functions)
  - [Arrays](#arrays)
  - [Spread Elements](#spread-elements)
    - [Spreading into a call](#spreading-into-a-call)
  - [Destructuring](#destructuring)
  - [Tuples](#tuples)
  - [nameof](#nameof)
  - [Ranges](#ranges)
  - [Enums](#enums)
  - [Control Flow](#control-flow)
  - [Declare Statements & Casting](#declare-statements--casting)
  - [Function Types](#function-types)
  - [Interfaces](#interfaces)
  - [With Operator](#with-operator)
  - [While Loops](#while-loops)
  - [Sealed & Declared Interfaces](#sealed--declared-interfaces)
  - [`after` Statements](#after-statements)
  - [`every` Statements](#every-statements)
  - [For Loops](#for-loops)
  - [Iterators](#iterators)
  - [Ternary Operator](#ternary-operator)
  - [keyof](#keyof)
  - [Sets](#sets)
  - [Result Pattern](#result-pattern)
  - [Fallible Roblox API calls](#fallible-roblox-api-calls)
  - [Panics and `[fallible]`](#panics-and-fallible)
  - [`async` and `await`](#async-and-await)
  - [Deprecation](#deprecation)
  - [Error Propagation](#error-propagation)
  - [Array.join()](#arrayjoin)
  - [Range.length](#rangelength)
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
    - [Default Trait Method Bodies](#default-trait-method-bodies)
    - [Structural Traits: Display, Eq, Hash](#structural-traits-display-eq-hash)
  - [typeof](#typeof)
  - [Events](#events)
  - [Once Event Operator](#once-event-operator)
  - [Exports](#exports)
  - [Imports & Modules](#imports--modules)
  - [Realms](#realms)
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
- **Result pattern for errors** – Error handling uses the result pattern from Rust, no more `pcall`s. Roblox API methods that can fail return
  `Result<T, RobloxError>`, so the failure is in the signature rather than waiting to kill the thread. See [example](#result-pattern).
- **Error propagation** – The postfix `?` operator unwraps a `Result<T, E>`, returning early on failure - same idea as Rust's `?`. See
  [example](#error-propagation).
- **`async`/`await`** – Luau yields invisibly; `async` puts it in the signature. Calling one starts it and gives a `Future<T>`, so two yielding
  calls can be in flight at once, and every Roblox method the dump tags `Yields` is generated as `async fn`. See [example](#async-and-await).
- **Sets** – `Set<T>`/`MutSet<T>` with the usual algebra, lowered to a plain table whose keys are its members. No
  runtime library, no wrapper object. See [example](#sets).
- **Events** – Built-in user events with shorthand syntax. See [example](#events).
- **Once event operator** – `event ^= handler` connects a handler that disconnects itself after firing exactly once. See [example](#once-event-operator).
- **Traits** – Define reusable behavior that interfaces can implement, enabling shared APIs and generic constraints that reflect behavior, including an
  explicit `@` self receiver inside implementations. See [example](#traits--implementations).
- **Default trait method bodies** – A trait method can carry a body of its own, used by every `implement` block that doesn't override it - shared as one
  Luau function rather than re-emitted per site. See [example](#default-trait-method-bodies).
- **Built-in `Display`, `Eq` and `Hash` traits** – Every interface can `implement` these for free: a recursive, Rust-`Debug`-style `to_string()`,
  structural `equals()`, and a `hash()` consistent with it - no fields to write by hand, no runtime library exposed to Loom code. See
  [example](#structural-traits-display-eq-hash).
- **`with` operator** – `value with { field: newValue }` builds a new instance from an existing one, copying every field not explicitly overridden.
  See [example](#with-operator).
- **Named imports/exports** – Including `export * from "./module"` to forward everything another module publishes, and `export type *` to forward only its
  types. See [example](#exports).
- **Modules & packages** – Relative imports resolve inside a project, bare specifiers (`math`, `scope/math`) name a package declared in `[dependencies]`, and a
  dependency's Luau is written into the consuming project's output. See [example](#imports--modules).
- **Binary serialization** – `[serializable]` interfaces get generated `buffer`-backed codecs, with `[packed]` bit-packing and delta encoding for sending only
  what changed. No runtime schema is walked. See [example](#serialization).
- **Operator overloading** – Traits carrying `[luau_metamethod]` methods make `+`, `==`, `<` and friends work on your own types. See
  [example](#operator-overloading).
- **Type indexing** – `Foo["bar"]`, enum member types, and indexing through a generic parameter, all resolved at compile time. See [example](#type-indexing).
- **Language server** – Diagnostics, hover, go-to-definition and go-to-type/implementation, context-aware completion (members, attributes, module
  specifiers, and type-vs-value position) with auto-import, signature help, inlay hints, find-references, rename, document highlight, outline, folding,
  and quick fixes over `.loom` files.
- **Watch mode** – `loom watch` rebuilds on change.
- **Indices starting at one** – Same as Luau for familiarity
- **Zero-cost abstractions** – Transpiles to idiomatic Luau with minimal overhead
- **Batteries included** - Comes with a set of built-in compile-time macros included with data types such as [Array.join()](#arrayjoin),
  [Range.clamp()](#rangeclamp), or [String Methods](#string-methods), fully-typed standard libraries like `math`, and Roblox-specific
  [Instance Helpers](#instance-helpers)
- **Spread elements** – `[69, ..rest]` builds a new array out of an existing one, so a prepend, append or concatenation is an expression rather than a
  sequence of mutations, and `f(..rest)` passes one as separate arguments to a rest parameter. See [example](#spread-elements).
- **Iterators** – A type that implements `Iterator<T>` drives a `for` loop itself, deciding what "the next one" means and carrying its own position. See
  [example](#iterators).
- **Realms** – `[realms]` says which directories run on the client and which on the server, and an import that crosses that boundary is an error rather than
  a runtime surprise. `[server]`/`[client]` narrow a single declaration inside a shared module. See [example](#realms).
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
- Package management & installation pipeline (#111 & #112 respectively)
- Linter AST visitor (#18)

---

## Working Examples

Each example is separated by a line. Top code is written in Loom, bottom code is the Luau output.

### Comments & Documentation

`##` is a line comment and `#: ... :#` a block comment; neither reaches the output. `###` is a doc comment: a run of them
documents the declaration directly below, and the language server shows that prose on hover, in completion, and in
signature help. A doc comment may sit above a declaration's attributes or between them and its keyword.

```rs
## an ordinary comment, for whoever is reading the source

### Adds two numbers together.
###
### Returns their sum.
fn add(x: number, y: number): number {
  return x + y;
}
```

```luau
local function add(x: number, y: number): number
	return x + y
end
```

---

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

greet("Poppy");
greet("Poppy", "Hi");
```

```luau
const function greet(name: string, greeting: string?)
	if greeting == nil then
		greeting = "Hello"
	end
	return print(greeting .. ", " .. name .. "!")
end
greet("Poppy")
greet("Poppy", "Hi")
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
### Spread Elements

`..` inside an array literal copies another array's elements into the one being built. Since arrays are immutable by default, this is how a
prepend, an append or a concatenation is written: the operand is never touched, and what you get back is a new array.

```rs
let base = [1, 2, 3];
let prepended = [0, ..base];
let appended = [..base, 4];
let joined = [..base, ..base];
let copied = [..base];
```

```luau
const base = {1, 2, 3}
const _result = {0}
local _count = 1
const _length = #base
table.move(base, 1, _length, _count + 1, _result)
_count += _length
const prepended = _result
const _result_1 = table.clone(base)
local _count_1 = #_result_1
_count_1 += 1
_result_1[_count_1] = 4
const appended = _result_1
const _result_2 = table.clone(base)
local _count_2 = #_result_2
const _length_1 = #base
table.move(base, 1, _length_1, _count_2 + 1, _result_2)
_count_2 += _length_1
const joined = _result_2
const copied = table.clone(base)
```

A spread copies, so the operand's mutability never reaches the result: a `mut` array spreads into an immutable one and an immutable array spreads
into a `mut` one. The element type is the union of everything the literal contributes, spreads included.

```rs
let numbers = mut [1, 2];
let names = ["a"];

let widened = [..numbers, "b"];       ## (number | string)[]
let mutable = mut [0, ..numbers];     ## number[mut]
let annotated: string[] = [..names];  ## string[]
```

Only an array may be spread — anything else is an error rather than an implicit conversion:

```rs
let x = 69;
let xs = [..x];
##          ^ Only an array may be spread, got '69'.
```

---

### Spreading into a call

`..` also works at a call site, passing an array's elements as separate arguments — the other half of the `..args` a
[rest parameter](#functions) collects them with, so forwarding one function's arguments to another is just spreading them
back out.

```rs
fn sum(..ns: number[]): number {
    mut total = 0;
    for n : ns
        total += n;

    return total;
}

let xs = [1, 2, 3];
let alone = sum(..xs);
let after_fixed = sum(10, 20, ..xs);
let before_more = sum(..xs, 100);
```

```luau
const function sum(...: number): number
  local ns = {...}
  local total = 0
  for _, n in ns do
    total += n
  end
  return total
end
const xs = {1, 2, 3}
const alone = sum(table.unpack(xs))
const after_fixed = sum(10, 20, table.unpack(xs))
const _result = table.clone(xs)
local _count = #_result
_count += 1
_result[_count] = 100
const before_more = sum(table.unpack(_result))
```

`table.unpack` only expands in last position, so a spread with anything after it builds the whole tail as one array first —
the same lowering an array literal uses — and unpacks that.

A generic rest parameter infers from the element type, so `..` carries the type through a forwarding function unchanged:

```rs
fn count_of<T>(..items: T[]): number -> items.length;
fn forward(..args: number[]): number -> count_of(..args);
```

**A spread argument must land in the rest parameter.** A rest parameter is the only place a count nobody knows until runtime
can go — everything from it on arrives as one array, so how many elements the spread carries changes nothing about which
parameter anything lands on. A fixed parameter has to know which argument it is being handed:

```rs
fn add(a: number, b: number): number -> a + b;
add(..xs);
##  ^ Only a rest parameter may be given a spread argument.
##    hint: this function takes a fixed number of arguments, so pass them one at a time

fn labelled(label: string, ..ns: number[]): number -> ns.length;
labelled(..xs);
##       ^ A spread argument must come after every fixed parameter, and 1 of them is still unfilled.

fn point(..coordinates: (number, string)): number -> 1;
point(..xs);
##    ^ Rest parameter of type '(number, string)' expects an exact number of arguments,
##      so it cannot be given a spread argument.
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
let user = new User { name: "Poppy", age: 31 };
let { name, age } = user;
```

```luau
type User = {
  read name: string,
  read age: number,
}
const user = { name = "Poppy", age = 31 }
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
let a = Abc::A;
let b = Abc::B;
let c = Abc::C;
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
let tag = Tag::Lava
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
### With Operator

`value with { field: newValue }` builds a new instance of `value`'s interface, copying every field not mentioned in the `{ ... }` block from `value`
itself. Nothing is merged at runtime - each field is resolved to either the override or the original at compile time.

```rs
interface User { name: string, age: number }
let user = new User { name: "Poppy", age: 31 };
let older = user with { age: 32 };
```

```luau
type User = {
  read name: string,
  read age: number,
}
const user = { name = "Poppy", age = 31 }
const older = { name = user.name, age = 32 }
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

Over a keyed collection, one name binds the *value* and the key is discarded. Name the key by taking two.

```rs
interface Scores { alice: number, bob: number }
let scores = new Scores { alice: 1, bob: 2 };

for score : scores
    print(score)

for name, score : scores
    print(name, score)
```

```luau
type Scores = {
  read alice: number,
  read bob: number,
}
const scores = { alice = 1, bob = 2 }
for _, score in scores do
  print(score)
end
for name, score in scores do
  print(name, score)
end
```
---
### Iterators

A type that implements the built-in `Iterator<T>` trait can be looped over directly. The loop calls `next` until it answers `none`, so the iterator
carries its own position and decides for itself what "the next one" means — which a loop counting on its behalf cannot do.

```rs
interface Countdown { mut remaining: number }

implement Iterator<number> for Countdown {
    fn next(): number? {
        if remaining <= 0 {
            return none;
        }

        remaining -= 1;
        return remaining + 1;
    }
}

for value : new Countdown { remaining: 3 }
    print(value);
```

```luau
type Countdown = {
  remaining: number,
} & Iterator<number>
local Iterator_number_for_Countdown = {}
Iterator_number_for_Countdown.__index = Iterator_number_for_Countdown
Iterator_number_for_Countdown = Iterator_number_for_Countdown :: Countdown
function Iterator_number_for_Countdown.next(self: Countdown): number?
  if self.remaining <= 0 then
    return nil
  end
  self.remaining -= 1
  return self.remaining + 1
end
const _iterator = setmetatable({ remaining = 3 }, Iterator_number_for_Countdown) :: Countdown
for value in function()
  return _iterator:next()
end do
  print(value)
end
```

This lowers to Luau's generic `for` over a function alone: given no state or control variable, Luau calls that function once per step and stops at the
first `nil`, which is exactly what `next` answering `none` means. The receiver is bound to a name before the loop, since re-evaluating an expression
that *produces* an iterator would produce a fresh one every step and never finish.

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
## Sets

A `Set<T>` is a table whose keys are its members. Nothing about it survives to runtime: the constructors build
that table directly and every operation is lowered inline, so a set costs exactly what the table costs.

```rs
let tags = Set::of("boss", "flying");
let flying = tags.has("flying");
let names = ["ana", "bo", "ana"].to_set();
```

```luau
const tags = { ["boss"] = true, ["flying"] = true }
const flying = tags["flying"] == true
const _result = {}
for _, _element in {"ana", "bo", "ana"} do
  _result[_element] = true
end
const names = _result
```

---

`Set<T>` is read-only. `MutSet<T>` adds `add` and `remove`, and is assignable to `Set<T>` - so a set you are still
building can be passed to anything that only reads one.

```rs
mut visited = MutSet::of(1);
visited.add(2);
visited.remove(1);
let more = visited.union(Set::of(3));
```

```luau
local visited = { [1] = true }
visited[2] = true
visited[1] = nil
const _other = { [3] = true }
const _result = table.clone(visited)
for _key in _other do
  _result[_key] = true
end
const more = _result
```

---

| Member | Result | Cost |
| --- | --- | --- |
| `size` | how many members | walks the set |
| `is_empty` | whether it has none | `next(t) == nil` |
| `has(value)` | whether `value` is a member | one lookup |
| `add(value)` / `remove(value)` | `MutSet<T>` only | one assignment |
| `union(other)` | every member of either | clones, then walks `other` |
| `intersect(other)` / `difference(other)` | members in both / in this one only | walks this one |
| `is_subset_of(other)` | whether `other` has all of them | walks until one is missing, then stops |
| `to_array()` | the members as a `T[]`, in no particular order | walks the set |

`size` is a count, not a stored field - storing one would collide with the members, since they *are* the keys. Reach
for `is_empty` over `size == 0`: it stops at the first member instead of counting them all.

Membership is `value == true` rather than "key present". The indexer is declared `[T]: bool` and `MutSet`'s is
mutable, so writing `false` through it directly means what it says.

---

## Result Pattern

```rs
fn unsafe_function(condition: bool): Result<number, string> ->
    condition ? BaseResult::ok(69) : BaseResult::err("function failed!");
    
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
### Fallible Roblox API calls

A Roblox API method that can raise returns `Result<T, RobloxError>` instead. The call is lowered to an `xpcall`
with a shared handler, so nothing is allocated per call site beyond the `Result`, and `?` propagates it like any
other.

A method that also *yields* is `async fn` on top of this, so it is awaited as well - see
[`async` and `await`](#async-and-await). `get_data_store` does not yield; `get_async` does.

```rs
async fn load(key: string): Result<unknown, RobloxError> {
    let store = data_store_service.get_data_store("players")?;
    let value = await store.get_async(key)?;
    return BaseResult::ok(value);
}
```

```luau
const function load(key: string): Loom.Result<unknown, Loom.RobloxError>
  const _ok, _value = xpcall(data_store_service.GetDataStore, Loom.roblox_error, data_store_service, "players")
  const _result = if _ok then { ok = true, value = _value } else { ok = false, error = _value }
  if not _result.ok then
    return _result
  end
  const store = _result.value
  ...
end
```

Which methods are fallible is decided during type generation: the API dump's `Yields`/`CanYield` tags seed the
set, corrected by a reviewed override list. Roblox publishes nothing that says "throws", so a method nobody has
classified is treated as fallible - a wrong "fallible" costs a frame, a wrong "infallible" costs a crash.

`RobloxError` carries a `message` and an optional `traceback`.
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
    return BaseResult::ok(value);
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
## `async` and `await`

Luau yields invisibly: a call that parks the calling thread looks exactly like one that returns straight away.
`async` puts that in the signature, the way `[fallible]` puts raising in one. Calling an `async fn` **starts** it
and evaluates to a `Future<T>`; `await` is what waits for the value.

```rs
async fn fetch(key: string): number -> 1;

async fn total(a: string, b: string): number {
    let first = fetch(a);   ## starts now
    let second = fetch(b);  ## starts now, alongside the first
    return await first + await second;
}
```

```luau
const function fetch(key: string): number
  return 1
end
const function total(a: string, b: string): number
  const first = Loom.future(fetch, a)
  const second = Loom.future(fetch, b)
  return Loom.await(first) + Loom.await(second)
end
```

Where an `await` consumes a call directly, the future is never built - starting a function and immediately waiting
for it is what a plain call already does:

```rs
async fn one_at_a_time(key: string): number -> await fetch(key);
```

```luau
const function one_at_a_time(key: string): number
  return fetch(key)
end
```

### Where `await` may appear

Inside an `async fn`, and inside a function expression - an event handler is anonymous and runs on a thread Roblox
owns, so it has no signature for `async` to appear on and nobody to propagate to. At the top level of a module it
is an error: yielding there blocks every thread that requires it.

`async` is part of the function *type*, so an `async fn` cannot be passed where a plain `fn` is expected - `map`
will not silently take a callback that yields.

### `[no_yield]`

Luau raises rather than suspends when a thread yields across a C-call boundary — inside a metamethod, or inside a callback Luau invokes itself such as
`table.sort`'s comparator. A function that stands in one of those places says so with `[no_yield]`, and awaiting inside it becomes an error instead of a
crash at whichever call first happened to yield.

```rs
[no_yield]
fn compare(a: number, b: number): bool {
    let value = await fetch(a);
    ##          ^ 'compare' is marked '[no_yield]', so it cannot await.
    return value < b;
}
```

Marking a function both `async` and `[no_yield]` is rejected too: one of the two was meant, and nothing in the signature can say which. A function
expression runs on a thread of its own, so it does not inherit the promise from the function it is written inside.

The compiler applies the same rule by itself wherever it already knows the answer: a trait method carrying `[luau_metamethod]` may not be `async`,
because Luau invokes a metamethod across that same boundary. See [Operator Overloading](#operator-overloading).

### Yielding Roblox API calls

Every Roblox method the API dump tags `Yields` or `CanYield` is generated as `async fn`. Most of those also raise,
so they are `Result`-returning as well, and the two compose - `await` binds tighter than `?`, so `await call()?`
already means `(await call())?`:

```rs
async fn load(key: string): Result<unknown, RobloxError> {
    let store = data_store_service.get_data_store("players")?;
    let value = await store.get_async(key)?;
    return BaseResult::ok(value);
}
```

Both fusions apply, so the success path allocates neither a future nor a `Result`:

```luau
const function load(key: string): Loom.Result<unknown, Loom.RobloxError>
  ...
  const _ok_1, _value_1 = xpcall(store.GetAsync, Loom.roblox_error, store, key)
  if not _ok_1 then
    return { ok = false, error = _value_1 }
  end
  const value = _value_1
  return { ok = true, value = value }
end
```

### Chains

One `await` covers a whole chain of yielding calls. Each future is resolved on the way past, so a
`wait_for_child` chain does not need a set of parentheses per link:

```rs
let torso = await character.wait_for_child("Humanoid").wait_for_child("Torso");
```

```luau
const torso = character:WaitForChild("Humanoid"):WaitForChild("Torso")
```

Every link is fused, so the chain costs exactly what it would have written by hand. The `await` still says the
expression parks the thread; what it stops saying is how many times.

The read-through applies to **calls** only - another suspension point following. A field read off a future still
takes the parenthesised form, which is also what keeps a future's own members reachable:

```rs
let name = await find().name;      ## error - 'name' belongs to the awaited value
let name = (await find()).name;    ## ok
let status = load().status;        ## ok - polling a future, no await involved
```

And only the awaited expression's own spine reads through, so a chain buried in an argument is not quietly
awaited along with it.

Everywhere else `await` follows JS precedence - it takes the whole postfix chain.

### `Future`

| Member | Result |
| --- | --- |
| `status` | `"pending"`, `"resolved"` or `"rejected"` |
| `value` | the settled value, or `none` while pending and after a failure; reading it never waits |
| `Future::all(futures)` | every value, in the order the futures were given; the first failure fails the whole set |
| `Future::race(futures)` | whichever settles first, however it settles |
| `Future::resolved(value)` | an already-settled future, so a synchronous path can hand back the same type |
| `Future::rejected(error)` | an already-failed future; awaiting it re-raises |

Awaiting a future that failed re-raises, so awaiting a `[fallible]` async function is itself a panicking operation.
A future nobody awaits swallows its failure.

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
    return BaseResult::ok(1);
}

fn unsafe_fn(): Result<number, string> {
    let value = some_other_unsafe_fn()?;
    return BaseResult::ok(69 + value);
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
[attribute_usage(AttributeTargets::Property)]
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

[attribute_usage(AttributeTargets::Property | AttributeTargets::Event)]
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
### Default Trait Method Bodies

A trait method can carry a body of its own instead of just a signature. An `implement` block that doesn't override the method gets that body for free;
one that does override it works exactly as before. The default is emitted once - as one shared Luau function - and every non-overriding `implement`
wires it in with a direct field assignment rather than a per-call metatable lookup.

```rs
trait Greeting {
    fn greet(): string -> "Hello!";
}

interface A { }
interface B { }

implement Greeting for A { }
implement Greeting for B {
    fn greet(): string -> "Howdy!";
}

let a = new A { };
let b = new B { };
print(a.greet());
print(b.greet());
```

```luau
type Greeting = {
  greet: (Greeting) -> string,
}
type A = {} & Greeting
type B = {} & Greeting
local Greeting_for_A = {}
Greeting_for_A.__index = Greeting_for_A
Greeting_for_A = Greeting_for_A :: A
const function Greeting_greet_default(self: unknown): string
  return "Hello!"
end
Greeting_for_A.greet = Greeting_greet_default
local Greeting_for_B = {}
Greeting_for_B.__index = Greeting_for_B
Greeting_for_B = Greeting_for_B :: B
function Greeting_for_B.greet(self: B): string
  return "Howdy!"
end
const a = setmetatable({}, Greeting_for_A) :: A
const b = setmetatable({}, Greeting_for_B) :: B
print(a:greet())
print(b:greet())
```

Inside a default body, `@` types as `unknown` rather than any one implementer's interface - the body is shared verbatim across every type that picks it
up, so it can't assume a specific shape. Two traits defaulting the same method name on the same interface, with neither overridden, is an error rather
than a silent pick of whichever `implement` came first.

---

### Structural Traits: Display, Eq, Hash

Every interface can `implement` these three for free, with no methods of its own:

```rs
interface User { name: string, age: number }

implement Display for User { }
implement Eq for User { }
implement Hash for User { }

let a = new User { name: "Poppy", age: 31 };
let b = new User { name: "Poppy", age: 31 };
print(a.to_string());
print(a.equals(b));
print(a.hash());
```

```luau
const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
type User = {
  read name: string,
  read age: number,
} & Display & Eq & Hash
local Display_for_User = {}
Display_for_User.__index = Display_for_User
Display_for_User = Display_for_User :: User
const function Display_to_string_default(self: unknown): string
  return Loom.deep_display(self)
end
Display_for_User.to_string = Display_to_string_default
local Eq_for_User = {}
Eq_for_User.__index = Eq_for_User
Eq_for_User = Eq_for_User :: User
const function Eq_equals_default(self: unknown, other: unknown): boolean
  return Loom.deep_equal(self, other)
end
Eq_for_User.equals = Eq_equals_default
local Hash_for_User = {}
Hash_for_User.__index = Hash_for_User
Hash_for_User = Hash_for_User :: User
const function Hash_hash_default(self: unknown): number
  return Loom.deep_hash(self)
end
Hash_for_User.hash = Hash_hash_default
local User_meta = Loom.merge_meta(Display_for_User, Eq_for_User, Hash_for_User)
const a = setmetatable({ name = "Poppy", age = 31 }, User_meta) :: User
const b = setmetatable({ name = "Poppy", age = 31 }, User_meta) :: User
print(a:to_string())
print(a:equals(b))
print(a:hash())
```

- **`Display.to_string()`** – A human-readable representation of every field, recursively, the way Rust's derived `Debug` would - never the placeholder
  `<object>` a naive default might fall back to.
- **`Eq.equals(other)`** – Recursive, field-by-field structural equality, not reference equality: two separately constructed values with identical
  fields are equal.
- **`Hash.hash()`** – A deterministic structural hash consistent with `Eq.equals`: two values that compare equal always hash the same.

Override any of the three where a more specific behavior is wanted - `Display` and `Eq`/`Hash` in particular should usually be overridden together, or
the derived versions can disagree about which fields matter. The structural walk these defaults use (`Loom.deep_display`/`deep_equal`/`deep_hash`) lives
in the runtime library but is never callable from Loom code directly - only these three traits reach it.

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

An event may end in a rest parameter, in which case it fires with any number of trailing arguments and a handler names as many
of them as it cares about. This is what Roblox's own `RemoteEvent` looks like, so it is the ordinary case rather than a corner:

```rs
event abc(..data: unknown[]);
event labelled(label: string, ..rest: number[]);

abc += fn(a, b, c) { print(a, b, c); };
labelled += fn(label, first) { print(label, first); };

let ns = [1, 2];
abc(1, "two", true);
labelled("hi", ..ns);
```

```luau
const abc: Loom.Event<...unknown> = Loom.Event.new()
const labelled: Loom.Event<string, ...number> = Loom.Event.new()
abc:Connect(function(a, b, c)
  print(a, b, c)
end)
labelled:Connect(function(label, first)
  print(label, first)
end)
const ns = {1, 2}
abc:Fire(1, "two", true)
labelled:Fire("hi", table.unpack(ns))
```

The handler's parameters infer from what the rest parameter holds — `a`, `b` and `c` are `unknown`, `first` is `number` — and the
rest parameter reaches Luau as a variadic type pack (`...unknown`) rather than the array it is written as, so the emitted `Connect`
still type-checks. Firing takes a [spread](#spreading-into-a-call) like any other rest parameter.

---

## Once Event Operator

`^=` connects a handler the same way `+=` does, but the connection disconnects itself after the event fires once - no manual `-=` needed.

```rs
event my_event(data: string);

fn handler(data: string): void -> print(data);

my_event ^= handler;
my_event("hello!");
```

```luau
const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const my_event: Loom.Event<string> = Loom.Event.new()
const function handler(data: string): ()
  return print(data)
end
const handler_conn = my_event:Once(handler)
my_event:Fire("hello!")
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

`export *` forwards everything another module publishes, under the name that module publishes it with, so a package can have one entry point that
re-exports its parts.

```rs
// geometry.loom
export let pi = 3.14159;
export fn area(r: number): number -> pi * r * r;
```

```rs
// shapes.loom
export * from "./geometry";
export let unit = 1;
```

```luau
-- shapes.luau
const geometry = require("./geometry")
const unit = 1
return { unit = unit, pi = geometry.pi, area = geometry.area }
```

An export the file makes itself wins over one a star would forward, wherever the two sit relative to each other in source; two stars offering the same
name is an error instead, since nothing at the use site would say which one it meant. `export type * from "./module"` forwards only that module's types.

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

## Realms

`[realms]` in `loom-config.toml` maps directories under the source directory to `shared`, `client` or `server`. A project that declares none has one
realm and no boundary anything can cross.

```toml
[files]
source_directory = "src"

[realms]
client = "client"
server = "server"
net = "shared"
"net/server" = "server"
```

A file takes the realm of the *longest* directory naming it, so a realm declared inside another narrows it rather than being shadowed by it: `net` is
shared while `net/server` is not.

An import that crosses a realm boundary is an error. Replication is what makes this an error rather than a convention — a server module is never
delivered to the client, so client code importing one names something that is not there at runtime, and server code importing a client module ships
code it should not have. Shared is importable from either side, which is what makes it shared.

```rs
// src/client/hud.loom
import { save_profile } from "../server/profiles";
##                           ^ A client module cannot import a server one.
##                             hint: move what both realms need into a shared directory,
##                                   or declare this module's directory as 'server'
```

A dependency declares where its own code runs: which realm a file is in is answered by its own root, so a consumer laying its directories out
differently does not overrule it.

### `[server]` and `[client]`

A shared module is importable from either side, but a declaration inside one may still belong to a single realm. `[server]` and `[client]` narrow one
declaration below whatever `[realms]` says about the directory it is written in.

```rs
// src/net/remotes.loom  (shared)
export fn describe(id: number): string -> $"player {id}";

[server]
export fn grant_admin(id: number): void {
    admins.push(id);
}
```

```rs
// src/client/hud.loom
import { grant_admin } from "../net/remotes";
##       ^ 'grant_admin' is server-only, so client code cannot import it.
```

The attribute says who may *reach* a declaration; the directory keeps the stronger guarantee. A shared module replicates in full, so marking a
declaration inside one does not stop its code reaching the client — only a server directory does that. It applies to functions, interfaces, properties
and events, and is checked where an imported name binds to what it names, since everything in one file shares that file's realm.

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

A metamethod cannot be `async`. Luau invokes it itself, across a C-call boundary where a yielding thread raises rather than suspends, so an operator that
awaits does not block — it fails, at whichever call first reached the yield, with an error naming neither the operator nor the type it belongs to. It is
the rule [`[no_yield]`](#no_yield) states by hand, applied where the compiler already knows the answer.

```rs
trait Add<T> {
    [luau_metamethod("__add")]
    async fn add(other: T): T;
    ## ^ 'add' is a metamethod, so it cannot be 'async'.
}
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

let state = new PlayerState { health: 100, name: "Poppy" };
let payload = serialize_binary(state);
let restored = deserialize_binary::<PlayerState>(payload);
if restored.ok
  print(restored.value.health);

let diff = diff_binary(state, new PlayerState { health: 90, name: "Poppy" });
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
