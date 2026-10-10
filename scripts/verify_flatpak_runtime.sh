#!/bin/bash
# Run inside a temporary desktop session: dbus-run-session -- xvfb-run -a ...
set -euo pipefail
app=com.goshapps.DoomLauncher
log="${1:-build/flatpak-runtime.log}"
mkdir -p "$(dirname "$log")"
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
mkdir -p "$data_home/doomlauncher"
smoke="$(mktemp -d "$data_home/doomlauncher/flatpak-smoke.XXXXXX")"
python3 - "$smoke/fixture.wad" <<'PY'
from pathlib import Path
import struct
import sys
records = b"".join(struct.pack("<II8s", 12, 0, name) for name in (b"MAP01", b"THINGS", b"LINEDEFS"))
Path(sys.argv[1]).write_bytes(b"PWAD" + struct.pack("<II", 3, 12) + records)
PY
flatpak run --instance-id-fd=3 --env=GDK_BACKEND=x11 --env=LIBGL_ALWAYS_SOFTWARE=1 \
  --env="DOOMEDLAUNCHER_DATA_DIR=$smoke/data" "$app" "$smoke/fixture.wad" \
  3>"$smoke/instance" >"$log" 2>&1 &
launcher_pid=$!
cleanup() {
  instance="$(cat "$smoke/instance")"
  if [[ "$instance" =~ ^[0-9]+$ ]]; then
    flatpak kill "$instance" >/dev/null 2>&1 || true
  fi
  kill "$launcher_pid" 2>/dev/null || true
  wait "$launcher_pid" 2>/dev/null || true
  rm -rf "$smoke"
}
trap cleanup EXIT
for attempt in {1..30}; do
  if ! kill -0 "$launcher_pid" 2>/dev/null; then
    cat "$log" >&2
    echo 'Flatpak exited before its desktop window appeared' >&2
    exit 1
  fi
  if xdotool search --onlyvisible --name '^Doom Launcher$' >/dev/null 2>&1; then
    if python3 - "$smoke" <<'PY'
from pathlib import Path
import sqlite3
import sys
root = Path(sys.argv[1])
database = root / "data/DoomLauncher.sqlite"
if not database.is_file():
    raise SystemExit(1)
try:
    with sqlite3.connect(database.as_uri() + "?mode=ro", uri=True) as connection:
        row = connection.execute("SELECT GameFileID, FileName, MapCount, Map FROM GameFiles").fetchone()
    if not row or row[0] <= 0 or row[2:] != (1, "MAP01"):
        raise SystemExit(1)
    managed = root / "data/GameFiles" / row[1]
    if managed.read_bytes() != (root / "fixture.wad").read_bytes():
        raise SystemExit(1)
except (sqlite3.Error, OSError):
    raise SystemExit(1)
print("Installed Flatpak opened its window and imported MAP01 through Dart/Rust into SQLite")
PY
    then
      kill -0 "$launcher_pid"
      exit 0
    fi
  fi
  sleep 1
done
cat "$log" >&2
echo 'Flatpak did not open its window and import the fixture within 30 seconds' >&2
exit 1
