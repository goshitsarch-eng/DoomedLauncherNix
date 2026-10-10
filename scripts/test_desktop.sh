#!/usr/bin/env bash
# Run from the repository root on Linux with Flutter/Rust on PATH.
set -euo pipefail

if [[ -z "${DISPLAY:-}" ]]; then
  exec xvfb-run -a -s '-screen 0 1920x1200x24' "$0" "$@"
fi

openbox > /tmp/doomedlauncher-test-openbox.log 2>&1 &
qa_window_manager_pid=$!
trap 'kill "$qa_window_manager_pid" 2>/dev/null || true' EXIT
# Separate tool and D-Bus lifetimes avoid GTK application registration races
# when Flutter starts the next desktop test bundle.
for qa_suite in integration_test/*_test.dart; do
  dbus-run-session -- flutter test "$qa_suite" -d linux "$@"
done
