#!/bin/bash
set -euo pipefail

if [[ "$(uname -s)" != Darwin ]]; then
    echo "Este desinstalador requiere macOS." >&2; exit 1
fi
ROOT="$(cd -- "$(dirname -- "$0")" && pwd)"
APP="$HOME/Applications/Usage Orbit.app"
AGENT="$HOME/Library/LaunchAgents/com.usageorbit.widget.plist"
SETTINGS="$HOME/Library/Application Support/UsageOrbit"
/bin/launchctl bootout "gui/$(id -u)/com.usageorbit.widget" >/dev/null 2>&1 || true
BIN="$APP/Contents/MacOS/Usage Orbit"
if [[ ! -x "$BIN" ]]; then BIN="$ROOT/build/Usage Orbit.app/Contents/MacOS/Usage Orbit"; fi
if [[ -x "$BIN" ]]; then
    "$BIN" --quit || { echo "No se pudo cerrar Usage Orbit. Ciérralo antes de desinstalar." >&2; exit 1; }
else
    /usr/bin/osascript -e 'if application id "com.usageorbit.widget" is running then tell application id "com.usageorbit.widget" to quit'
fi
# No se eliminan credenciales, sesiones ni instalaciones de las CLI.
/bin/rm -rf -- "$APP" "$SETTINGS"
/bin/rm -f -- "$AGENT"
echo "Usage Orbit desinstalado. Las carpetas ~/.claude y ~/.codex se conservan."
