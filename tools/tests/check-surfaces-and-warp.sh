#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
GODOT="${GODOT_BIN:-godot-dotnet}"
PORT="${IS_TEST_PORT:-17893}"
CHECK_DIR="${IS_CHECK_DIR:-$(mktemp -d /tmp/indifferent-third-flight-XXXXXX)}"
mkdir -p "$CHECK_DIR"
export XDG_DATA_HOME="$CHECK_DIR/user-data"
SERVER_PID=""
cleanup() {
    if [[ -n "$SERVER_PID" ]]; then
        kill "$SERVER_PID" 2>/dev/null || true
        wait "$SERVER_PID" 2>/dev/null || true
    fi
}
trap cleanup EXIT INT TERM
echo "Verification output: $CHECK_DIR"

python3 tools/art/export_collision_meshes.py --check
if [[ -n "${BLENDER_BIN:-}" ]]; then
    "$BLENDER_BIN" --background --factory-startup --python tools/art/check_collision_blender.py > "$CHECK_DIR/blender.log" 2>&1
    if rg -q 'Traceback|Error:' "$CHECK_DIR/blender.log"; then cat "$CHECK_DIR/blender.log"; exit 1; fi
    rg 'BLENDER SURFACE VERIFIED' "$CHECK_DIR/blender.log"
fi
dotnet build -c Release -m:1 -v minimal
dotnet test -c Release --no-build -v minimal
dotnet build client/IndifferentStars.Client.csproj -m:1 -v minimal
timeout 60 "$GODOT" --headless --path client --editor --import --quit > "$CHECK_DIR/import.log" 2>&1
check_log() {
    if rg -q 'ERROR:|Exception|SCRIPT ERROR' "$1"; then cat "$1"; exit 1; fi
}
check_log "$CHECK_DIR/import.log"
for scene in SurfaceGeometryChecks ShipTrackChecks EffectsChecks NavigationChecks; do
    timeout 45 "$GODOT" --headless --path client "res://tests/$scene.tscn" > "$CHECK_DIR/$scene.log" 2>&1
    check_log "$CHECK_DIR/$scene.log"
    tail -2 "$CHECK_DIR/$scene.log"
done
IS_SURFACE_CAPTURE="$CHECK_DIR/surfaces" timeout 45 "$GODOT" --path client res://tests/SurfaceFlightChecks.tscn --resolution 1600x900 --audio-driver Dummy > "$CHECK_DIR/SurfaceFlightChecks.log" 2>&1
check_log "$CHECK_DIR/SurfaceFlightChecks.log"
dotnet src/IndifferentStars.Server/bin/Release/net10.0/IndifferentStars.Server.dll --port "$PORT" > "$CHECK_DIR/server.log" 2>&1 &
SERVER_PID=$!
for _ in $(seq 1 50); do
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then cat "$CHECK_DIR/server.log"; exit 1; fi
    if rg -q 'Listening on udp' "$CHECK_DIR/server.log"; then break; fi
    sleep 0.1
done
rg -q 'Listening on udp' "$CHECK_DIR/server.log"
IS_WARP_CAPTURE="$CHECK_DIR/warp" timeout 75 "$GODOT" --path client res://tests/WarpChecks.tscn --resolution 1600x900 --audio-driver Dummy -- --server "127.0.0.1:$PORT" > "$CHECK_DIR/WarpChecks.log" 2>&1
check_log "$CHECK_DIR/WarpChecks.log"
rg 'WARP FRAME TELEMETRY|WARP CHECKS PASSED' "$CHECK_DIR/WarpChecks.log"
IS_CAMERA_CAPTURE="$CHECK_DIR/camera" timeout 80 "$GODOT" --path client res://tests/CameraChecks.tscn --resolution 1600x900 --audio-driver Dummy -- --server "127.0.0.1:$PORT" > "$CHECK_DIR/CameraChecks.log" 2>&1
check_log "$CHECK_DIR/CameraChecks.log"
rg 'CAMERA CHECKS PASSED' "$CHECK_DIR/CameraChecks.log"
for scene in ImpactChecks ScanRecoveryChecks; do
    IS_IMPACT_CAPTURE="$CHECK_DIR/impacts" timeout 45 "$GODOT" --path client "res://tests/$scene.tscn" --resolution 1600x900 --audio-driver Dummy -- --server "127.0.0.1:$PORT" > "$CHECK_DIR/$scene.log" 2>&1
    check_log "$CHECK_DIR/$scene.log"
    rg 'CHECKS PASSED' "$CHECK_DIR/$scene.log"
done
echo "Third Flight checks passed. Screenshots and logs: $CHECK_DIR"
