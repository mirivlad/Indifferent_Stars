#!/usr/bin/env bash
# Start a Godot client. Extra arguments are passed to the game itself:
#   tools/run-client.sh --name Nova --server 127.0.0.1:7777
# Resolution can be changed with IS_RESOLUTION, e.g. IS_RESOLUTION=1280x720.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

GODOT="${GODOT_BIN:-}"
if [[ -z "$GODOT" ]]; then
    for candidate in godot-dotnet godot4-dotnet godot; do
        if command -v "$candidate" >/dev/null 2>&1; then
            GODOT="$(command -v "$candidate")"
            break
        fi
    done
fi
if [[ -z "$GODOT" && -x "$HOME/.local/bin/godot-dotnet" ]]; then
    GODOT="$HOME/.local/bin/godot-dotnet"
fi
if [[ -z "$GODOT" ]]; then
    echo "Godot 4.7.2 .NET not found. Set GODOT_BIN=/path/to/godot-dotnet." >&2
    exit 1
fi

RESOLUTION="${IS_RESOLUTION:-1600x900}"

exec "$GODOT" --path client --resolution "$RESOLUTION" -- "$@"
