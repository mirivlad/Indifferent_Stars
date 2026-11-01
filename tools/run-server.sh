#!/usr/bin/env bash
# Start the authoritative headless server. Extra arguments are passed through:
#   tools/run-server.sh --port 7778 --seed 42
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet SDK 10 is required: https://dotnet.microsoft.com/download" >&2
    exit 1
fi

exec dotnet run --project src/IndifferentStars.Server -- "$@"
