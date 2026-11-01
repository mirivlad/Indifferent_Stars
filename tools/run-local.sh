#!/usr/bin/env bash
# Start the server and a client together on localhost, then shut the server down
# when the client exits. Extra arguments go to the client:
#   tools/run-local.sh --name Nova
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PORT="${IS_PORT:-7777}"
LOG="${IS_SERVER_LOG:-/tmp/indifferent-stars-server.log}"

dotnet build src/IndifferentStars.Server -v q --nologo >/dev/null

dotnet run --project src/IndifferentStars.Server --no-build -- --port "$PORT" >"$LOG" 2>&1 &
SERVER_PID=$!

cleanup() {
    if kill -0 "$SERVER_PID" 2>/dev/null; then
        kill "$SERVER_PID" 2>/dev/null || true
        wait "$SERVER_PID" 2>/dev/null || true
    fi
}
trap cleanup EXIT INT TERM

# Wait until the server reports its listening socket (max ~10 s).
for _ in $(seq 1 50); do
    if grep -q "Listening on udp" "$LOG" 2>/dev/null; then
        break
    fi
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then
        echo "Server failed to start. Log: $LOG" >&2
        cat "$LOG" >&2
        exit 1
    fi
    sleep 0.2
done

echo "Server log: $LOG"
tools/run-client.sh --server "127.0.0.1:$PORT" "$@"
