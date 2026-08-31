# Agent tooling

Token-saving wrappers for the two most expensive things to do by hand in this
repo: reading the generated Roblox intrinsic files, and regenerating test
fixtures. Prefer these over raw `Read`/`Grep`/`dotnet run` for the cases they
cover — see [CLAUDE.md](../CLAUDE.md) for the one-line summary that's kept in
front of every agent session.

All three are POSIX shell, run fine under Git Bash on Windows, and assume the
repo root as the working directory (they `cd` there themselves via
`$BASH_SOURCE`, so it's safe to invoke them from anywhere).

## `loom-intrinsic.sh <Name>` / `loom-intrinsic.sh --list [filter]`

`Loom.Core/TypeChecking/Intrinsic/generated/*.loom` holds the generated
Roblox API type declarations — `None.loom` alone is ~18k lines, and there are
~2,900 `declare interface`/`type`/`enum` blocks across the two generated
files. Reading one of these files in full, or grepping with wide `-C`
context, burns a lot of tokens for what's usually "show me the shape of one
class."

This extracts exactly one declaration by brace-matched scanning from the
`declare` line to its closing `}` (or trailing `;` for a body-less forward
declaration like `declare sealed interface buffer;`), across every intrinsic
file, handles declarations whose generic parameter list spans multiple lines
before the `{`, and prints a `file:line` header per match so you can jump
straight to it if you need to edit the generator instead.

```bash
scripts/loom-intrinsic.sh Instance
scripts/loom-intrinsic.sh ConsumerEvent
scripts/loom-intrinsic.sh --list Player   # discover the exact name first
```

If nothing matches, it prints the closest case-insensitive substring matches
instead of just failing blind.

Note this only covers the *generated* Roblox surface. The small handwritten
files in the same directory (`loom.loom`, `math.loom`, `buffer.loom`,
`runtime.loom`, `traits.loom` — each well under 250 lines) are cheap enough
to `Read` directly when you need the whole file.

## `loom-regen-ast-snapshots.sh`

Wraps `dotnet run --project Loom.Tools -- generate-ast-snapshots
Loom.Testing/Snapshots/AST`. The generator writes LF-only output, but a
number of committed `.ast` fixtures predate that and are still CRLF, so a
plain regen run rewrites every one of those on every invocation even when
nothing but the line ending changed — as of this writing that's 14 files,
none of which have anything to do with whatever feature you're actually
touching.

This runs the generator, then for each file it touched checks
`git diff --ignore-all-space --quiet`: a clean result means the only
difference was line endings, so it reverts that file with `git checkout --`;
anything left is a real content change. Untracked (brand-new) fixture files
are always kept — there's nothing to `git checkout` back to.

```bash
scripts/loom-regen-ast-snapshots.sh
```

Output tells you how many files were real vs. reverted, and lists the real
ones so you know what to actually go read.

## `loom-new-luau-snapshot.sh <name>`

Compiles `Loom.Testing/Snapshots/Luau/<name>.loom` (which must already exist)
and writes `<name>.luau` next to it. This exists because getting the byte
shape right by hand is fiddly: `CompilerTest.AssertCompiled` compares
`expected + '\n'` against the compiled output, which only works if the
`.luau` fixture on disk has **no trailing newline** — but `loomtools compile`
writes `RenderedLuau` verbatim to stdout, which *does* end in `\n`, so a
plain `> file` redirect produces a fixture with one newline too many and a
failing test.

```bash
scripts/loom-new-luau-snapshot.sh destructure_default
```

It also refuses to write the fixture (and prints the diagnostics) if the
source produced any `[Error]`-severity diagnostic, or if the compiler
produced no output at all (parse failed hard enough that `Compiler.Compile`
returned null) — matching the `Utility.AssertNoErrors` gate the snapshot test
itself runs under, so you can't accidentally commit a fixture that freezes an
error-recovery artifact instead of real output.

There's no equivalent bulk-regenerate for `Snapshots/Luau/*.luau` (unlike the
AST ones) because each source file can need different surrounding
declarations to compile — `loomtools compile` only takes one file at a time,
which is what this wraps.
