#!/usr/bin/env bash
# Prints a single declaration block from the generated Roblox intrinsic files
# (Loom.Core/TypeChecking/Intrinsic/**/*.loom) without reading the whole file.
#
# Those files are huge (None.loom ~18k lines, PluginSecurity.loom ~10k lines),
# so grepping them with wide context or Read-ing a guessed offset burns a lot
# of tokens. This does exact brace-matched extraction instead.
#
# Usage:
#   scripts/loom-intrinsic.sh <Name>        Print the declare block for <Name>
#   scripts/loom-intrinsic.sh --list [Name]  List declared names (optionally filtered)
#
# Examples:
#   scripts/loom-intrinsic.sh Instance
#   scripts/loom-intrinsic.sh ConsumerEvent
#   scripts/loom-intrinsic.sh --list Player

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INTRINSIC_DIR="$ROOT/Loom.Core/TypeChecking/Intrinsic"
FILES=("$INTRINSIC_DIR"/*.loom "$INTRINSIC_DIR"/generated/*.loom)

usage() {
    echo "Usage: $0 <Name>" >&2
    echo "       $0 --list [filter]" >&2
    exit 1
}

[ $# -ge 1 ] || usage

if [ "$1" = "--list" ]; then
    filter="${2:-}"
    grep -ohE '^declare (sealed )?(interface|type|enum) [A-Za-z0-9_]+' "${FILES[@]}" \
        | awk '{print $NF}' \
        | sort -u \
        | { if [ -n "$filter" ]; then grep -i "$filter"; else cat; fi; }
    exit 0
fi

target="$1"
found_any=0

for file in "${FILES[@]}"; do
    [ -f "$file" ] || continue
    result="$(awk -v target="$target" -v fname="$file" '
        BEGIN { found = 0; depth = 0 }
        {
            if (!found) {
                if ($0 ~ ("^declare( sealed)? (interface|type|enum) " target "([<:; ]|$)")) {
                    found = 1
                    print fname ":" NR
                } else {
                    next
                }
            }
            print
            depth += gsub(/\{/, "{")
            depth -= gsub(/\}/, "}")
            if (depth == 0 && $0 ~ /[;}][[:space:]]*$/) exit
        }
    ' "$file")"

    if [ -n "$result" ]; then
        echo "$result"
        echo "---"
        found_any=1
    fi
done

if [ "$found_any" -eq 0 ]; then
    echo "No declaration named '$target' found. Closest matches:" >&2
    grep -ohE '^declare (sealed )?(interface|type|enum) [A-Za-z0-9_]+' "${FILES[@]}" \
        | awk '{print $NF}' \
        | sort -u \
        | grep -i "$target" >&2 || echo "  (none)" >&2
    exit 1
fi
