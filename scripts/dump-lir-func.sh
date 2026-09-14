#!/bin/bash
# dump-lir-func.sh — Dump LIR for a specific function from a Dark expression
#
# Usage:
#   ./scripts/dump-lir-func.sh "dark expression" function_name
#   ./scripts/dump-lir-func.sh "dark expression"  # dumps all functions
#
# Examples:
#   ./scripts/dump-lir-func.sh "iter([1,2,3], 0)" iter
#   ./scripts/dump-lir-func.sh "Base64.encode(Blob.fromList([72uy]))" tail_i64
#
# Shows both pre- and post-register-allocation LIR for the named function.

set -euo pipefail

EXPR="${1:-}"
FUNC="${2:-}"

if [ -z "$EXPR" ]; then
    echo "Usage: $0 'dark-expression' [function_name]"
    exit 1
fi

OUTFILE="$(mktemp -t dark-lir-output.XXXXXX)"

cleanup() {
    rm -f "$OUTFILE"
}

trap cleanup EXIT

dump_args=(--dump-lir)
if [ -n "$FUNC" ]; then
    dump_args+=("--dump-function=$FUNC")
fi

# Filtering happens before formatting in the compiler, so a focused request
# never materializes the full LIR dump.
if [ -f "$EXPR" ]; then
    ./dark "${dump_args[@]}" "$EXPR" -o "$OUTFILE"
else
    ./dark "${dump_args[@]}" -e "$EXPR" -o "$OUTFILE"
fi
