#!/usr/bin/env bash
# Builds the sample with its violations compiled in and checks that TypeRules reports exactly the
# diagnostics listed in Violations/expected-diagnostics.txt: nothing missing, nothing extra.
#
#   ./verify-violations.sh [Configuration]   (default: Release)
set -euo pipefail

cd "$(dirname "$0")"
configuration="${1:-Release}"
log="$(mktemp)"
trap 'rm -f "$log"' EXIT

if dotnet build --configuration "$configuration" -p:IncludeViolations=true >"$log" 2>&1; then
    cat "$log"
    echo "error: the sample built with its violations included" >&2
    exit 1
fi

# The compiler doesn't run analyzers on code that doesn't compile, so a C# error in the
# violation files would hide every TypeRules diagnostic.
if grep -qE "error CS[0-9]+" "$log"; then
    grep -E "error CS[0-9]+" "$log" | sort -u >&2
    echo "error: the violation files don't compile, so no analyzer ran" >&2
    exit 1
fi

actual="$( (grep -oE "(error|warning) TR[0-9]+: [^']*'[^']*'" "$log" || true) \
    | sed -E "s/^(error|warning) (TR[0-9]+): [^']*'([^']*)'$/\2 \3/" \
    | sort -u)"
expected="$(grep -vE '^\s*(#|$)' Violations/expected-diagnostics.txt | sort -u)"

if [ "$actual" != "$expected" ]; then
    cat "$log"
    echo "error: the reported diagnostics differ from Violations/expected-diagnostics.txt" >&2
    echo "(< expected, > reported)" >&2
    diff <(echo "$expected") <(echo "$actual") >&2 || true
    exit 1
fi

echo "All $(echo "$expected" | wc -l | tr -d ' ') expected diagnostics reported, and nothing else."
