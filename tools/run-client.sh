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
check_log="$(mktemp /tmp/indifferent-stars-resources-XXXXXX.log)"
trap 'rm -f -- "$check_log"' EXIT
verified_message='Client resources verified: models, music, icon, C# client and main scene.'
verify_client_resources() {
    "$GODOT" --headless --path client --script res://tools/verify_resources.gd "$@" >"$check_log" 2>&1 &&
        grep -Fxq "$verified_message" "$check_log" &&
        ! grep -Eq 'ERROR:|Parse Error' "$check_log"
}
if ! verify_client_resources; then
    import_log="$(mktemp /tmp/indifferent-stars-import-XXXXXX.log)"
    echo "Importing client resources. Log: $import_log"
    import_status=0
    "$GODOT" --headless --path client --editor --import >"$import_log" 2>&1 || import_status=$?
    if [[ "$import_status" == 0 ]]; then
        if grep -Eq 'ERROR:|Parse Error' "$import_log"; then
            cat "$import_log" >&2
            exit 1
        fi
    else
        # Accept only the observed editor shutdown crash, then independently verify its output.
        if [[ "$import_status" != 134 && "$import_status" != 139 ]] ||
            ! grep -Fq 'is_cmdline_mode (editor/editor_node.cpp:' "$import_log" ||
            ! grep -Fq 'handle_crash: Program crashed with signal 11' "$import_log" ||
            ! grep -Fq 'loading_editor_layout' "$import_log" ||
            ! grep -Fq 'ERROR: Parameter "singleton" is null.' "$import_log" ||
            ! awk '/ERROR:/ && !/ERROR: Parameter "singleton" is null\./ { bad=1 } /SCRIPT ERROR:|Parse Error/ { bad=1 } END { exit bad }' "$import_log"; then
            cat "$import_log" >&2
            exit "$import_status"
        fi
    fi
    if ! verify_client_resources -- --record; then
        cat "$check_log" >&2
        echo "Client preparation failed. Import log: $import_log" >&2
        exit 1
    fi
    if [[ "$import_status" != 0 ]]; then
        echo "Warning: Godot's import editor crashed during shutdown; independent runtime resource checks passed. Log: $import_log" >&2
    fi
fi
cat "$check_log"
rm -f -- "$check_log"
trap - EXIT
exec "$GODOT" --path client --resolution "$RESOLUTION" -- "$@"
