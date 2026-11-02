#!/usr/bin/env bash
# Exercise launcher failure handling without building or running the game.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
test_dir="$(mktemp -d /tmp/indifferent-startup-tests-XXXXXX)"
trap 'rm -rf -- "$test_dir"' EXIT
mkdir -p "$test_dir/bin"

cat > "$test_dir/bin/dotnet" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
cat > "$test_dir/bin/godot" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
case " $* " in
    *' --version '*) echo '4.7.2.stable.mono.official.test'; exit 0 ;;
    *' --script '*)
        if [[ ! -e "$STARTUP_TEST_DIR/imported" || "$STARTUP_TEST_MODE" == bad_resources ]]; then exit 1; fi
        if [[ "$STARTUP_TEST_MODE" == missing_marker ]]; then exit 0; fi
        echo 'Client resources verified: models, music, icon, C# client and main scene.'
        if [[ "$STARTUP_TEST_MODE" == probe_error ]]; then echo 'ERROR: Runtime verification error.'; fi
        exit 0 ;;
    *' --editor '*)
        echo import >> "$STARTUP_TEST_DIR/calls"
        touch "$STARTUP_TEST_DIR/imported"
        case "$STARTUP_TEST_MODE" in
            known_crash|bad_resources|crash_with_error|early_crash|colored_crash_error)
                if [[ "$STARTUP_TEST_MODE" != early_crash ]]; then echo '[ DONE ] loading_editor_layout'; fi
                echo 'ERROR: Parameter "singleton" is null.'
                echo '   at: is_cmdline_mode (editor/editor_node.cpp:6618)'
                echo 'handle_crash: Program crashed with signal 11'
                if [[ "$STARTUP_TEST_MODE" == crash_with_error ]]; then echo 'ERROR: Other import failure.'; fi
                if [[ "$STARTUP_TEST_MODE" == colored_crash_error ]]; then printf '\033[31mERROR: Other import failure.\033[0m\n'; fi
                exit 134 ;;
            unexpected_crash) echo 'Other native crash'; exit 139 ;;
            error_exit_zero) echo 'ERROR: Cannot import an asset.'; exit 0 ;;
            *) exit 0 ;;
        esac ;;
    *) echo game >> "$STARTUP_TEST_DIR/calls" ;;
esac
MOCK
chmod +x "$test_dir/bin/dotnet" "$test_dir/bin/godot"

check_case() {
    local scenario="$1" expected_status="$2" expected_calls="$3" status=0 actual_calls
    rm -f -- "$test_dir/imported"
    : > "$test_dir/calls"
    if [[ "$scenario" == cached ]]; then touch "$test_dir/imported"; fi
    PATH="$test_dir/bin:$PATH" GODOT_BIN="$test_dir/bin/godot" STARTUP_TEST_DIR="$test_dir" STARTUP_TEST_MODE="$scenario" \
        "$repo_root/tools/run-client.sh" >"$test_dir/output" 2>&1 || status=$?
    actual_calls="$(tr '\n' ' ' < "$test_dir/calls")"
    if [[ "$status" != "$expected_status" || "$actual_calls" != "$expected_calls" ]]; then
        cat "$test_dir/output" >&2
        echo "FAIL $scenario: exit=$status calls=$actual_calls" >&2
        exit 1
    fi
    if [[ "$scenario" == known_crash ]]; then
        grep -Fq 'independent runtime resource checks passed' "$test_dir/output"
    fi
    echo "PASS $scenario"
}

check_case first_import 0 'import game '
check_case cached 0 'game '
check_case known_crash 0 'import game '
check_case bad_resources 1 'import '
check_case unexpected_crash 139 'import '
check_case error_exit_zero 1 'import '
check_case missing_marker 1 'import '
check_case probe_error 1 'import '
check_case crash_with_error 134 'import '
check_case early_crash 134 'import '

check_case colored_crash_error 134 'import '
