#!/usr/bin/env bash
# Compiles a Loom.Testing/Snapshots/Luau/<name>.loom source file and writes
# its matching <name>.luau fixture in the exact format Loom.Testing/CompilerTest.cs
# expects: no trailing newline (CompilerTest.AssertCompiled appends one before
# comparing, so a fixture that already ends in one produces a spurious blank
# line and fails the test).
#
# `loomtools compile` writes RenderedLuau verbatim (which does end in \n), so
# redirecting its stdout straight to the fixture file is the wrong byte shape
# - this strips exactly that one trailing newline.
#
# Usage:
#   scripts/loom-new-luau-snapshot.sh <name>
#
# Expects Loom.Testing/Snapshots/Luau/<name>.loom to already exist. Refuses to
# write a fixture (matching CompilerTest.Compile's own Utility.AssertNoErrors
# gate) if the source produced any [Error] diagnostic - a snapshot is meant to
# prove correct output, not freeze in whatever the error-recovery path emits.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

name="${1:-}"
if [ -z "$name" ]; then
    echo "Usage: $0 <name>" >&2
    echo "  (expects Loom.Testing/Snapshots/Luau/<name>.loom to exist)" >&2
    exit 1
fi

src="Loom.Testing/Snapshots/Luau/$name.loom"
dst="Loom.Testing/Snapshots/Luau/$name.luau"

if [ ! -f "$src" ]; then
    echo "Not found: $src" >&2
    exit 1
fi

ERR_FILE="$(mktemp)"
trap 'rm -f "$ERR_FILE"' EXIT

output="$(dotnet run --project Loom.Tools -- compile "$src" 2>"$ERR_FILE")"

if grep -q '^\[Error\]' "$ERR_FILE"; then
    echo "Compile reported errors for $src - fixture not written:" >&2
    cat "$ERR_FILE" >&2
    exit 1
fi

if [ -z "$output" ]; then
    echo "Compile produced no output for $src. Fixture not written." >&2
    cat "$ERR_FILE" >&2
    exit 1
fi

# $(...) already strips trailing newlines, so this is the exact fixture body.
printf '%s' "$output" > "$dst"

echo "Wrote $dst"
