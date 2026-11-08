#!/bin/bash
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "$0")" && pwd)"
if [[ "$(uname -s)" != Darwin ]]; then
    echo "La compilación requiere macOS 13 o posterior." >&2
    exit 1
fi
if ! /usr/bin/xcode-select -p >/dev/null 2>&1 || ! /usr/bin/xcrun --find swiftc >/dev/null 2>&1; then
    echo "Faltan las Command Line Tools. Ejecuta: xcode-select --install" >&2
    exit 1
fi

APP="$ROOT/build/Usage Orbit.app"
mkdir -p "$APP/Contents/MacOS"
SDK="$(/usr/bin/xcrun --sdk macosx --show-sdk-path)"
/usr/bin/xcrun swiftc -O -swift-version 5 -sdk "$SDK" \
    -target "$(uname -m)-apple-macosx13.0" \
    -framework AppKit -framework CoreServices \
    "$ROOT"/Sources/*.swift -o "$APP/Contents/MacOS/Usage Orbit"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
    <key>CFBundleIdentifier</key><string>com.usageorbit.widget</string>
    <key>CFBundleName</key><string>Usage Orbit</string>
    <key>CFBundleDisplayName</key><string>Usage Orbit</string>
    <key>CFBundleExecutable</key><string>Usage Orbit</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>1.0</string>
    <key>CFBundleVersion</key><string>1</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>LSUIElement</key><true/>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSPrincipalClass</key><string>NSApplication</string>
    <key>NSHumanReadableCopyright</key><string>Copyright © 2026 LeobardoVilla88 y LeobardoVillalobos88</string>
</dict></plist>
PLIST
/usr/bin/codesign --force --sign - "$APP"
printf 'Compilación terminada: %s\n' "$APP"
