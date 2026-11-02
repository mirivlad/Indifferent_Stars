#!/usr/bin/env bash
# Start a Godot client. Extra arguments are passed to the game itself:
#   tools/run-client.sh --name Nova --server 127.0.0.1:7777
# Resolution can be changed with IS_RESOLUTION, e.g. IS_RESOLUTION=1280x720.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

GODOT="${GODOT_BIN:-}"
if [[ -z "$GODOT" ]]; then
    for candidate in godot-dotnet godot4-dotnet "$HOME/.local/bin/godot-dotnet" godot; do
        if command -v "$candidate" >/dev/null 2>&1; then
            candidate_path="$(command -v "$candidate")"
            if candidate_version="$("$candidate_path" --version 2>/dev/null)" && [[ "$candidate_version" == 4.7.2.*.mono.* ]]; then
                GODOT="$candidate_path"
                break
            fi
        fi
    done
fi
if [[ -z "$GODOT" ]]; then
    echo "Godot 4.7.2 .NET not found. Run tools/install-godot-dotnet.sh or set GODOT_BIN=/path/to/godot-dotnet." >&2
    exit 1
fi
if ! godot_version="$("$GODOT" --version)" || [[ "$godot_version" != 4.7.2.*.mono.* ]]; then
    echo "Godot 4.7.2 .NET is required (version must contain '.mono.'). GODOT_BIN=$GODOT" >&2
    echo "Installing the .NET SDK does not add C# support to standard Godot." >&2
    exit 1
fi

RESOLUTION="${IS_RESOLUTION:-1600x900}"

dotnet build client/IndifferentStars.Client.csproj --nologo -v minimal
"$GODOT" --headless --path client --editor --import
exec "$GODOT" --path client --resolution "$RESOLUTION" -- "$@"
