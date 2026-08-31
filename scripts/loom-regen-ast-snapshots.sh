#!/usr/bin/env bash
# Regenerates Loom.Testing/Snapshots/AST/*.ast fixtures and reports only the
# files that genuinely changed.
#
# The generator (Loom.Tools generate-ast-snapshots) writes LF-only output, but
# a number of committed snapshots predate that and are still CRLF. Every
# regeneration run therefore rewrites those files even when nothing but the
# line ending changed, which pollutes `git status`/diff with noise on files
# unrelated to whatever feature prompted the regeneration. This wrapper
# reverts any file whose diff is line-ending-only (via `git diff -w --quiet`)
# so what's left staged/modified is only real content changes worth reading.
#
# Usage:
#   scripts/loom-regen-ast-snapshots.sh

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

SNAPSHOT_DIR="Loom.Testing/Snapshots/AST"
LOG_FILE="$(mktemp)"
trap 'rm -f "$LOG_FILE"' EXIT

dotnet run --project Loom.Tools -- generate-ast-snapshots "$SNAPSHOT_DIR" >"$LOG_FILE" 2>&1 \
    || { cat "$LOG_FILE"; exit 1; }

reverted=()
real=()

while IFS= read -r line; do
    [ -n "$line" ] || continue
    status="${line:0:2}"
    file="${line:3}"

    # Untracked (new) files have nothing to diff against - always real, and
    # `git checkout` can't restore what git doesn't know about yet.
    if [ "$status" = "??" ]; then
        real+=("$file")
        continue
    fi

    if git diff --ignore-all-space --quiet -- "$file"; then
        git checkout -- "$file"
        reverted+=("$file")
    else
        real+=("$file")
    fi
done < <(git status --porcelain -- "$SNAPSHOT_DIR")

echo "Regenerated. ${#real[@]} file(s) with real changes, ${#reverted[@]} line-ending-only diff(s) reverted."
if [ "${#real[@]}" -gt 0 ]; then
    echo "Changed:"
    printf '  %s\n' "${real[@]}"
fi
