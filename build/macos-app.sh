#!/usr/bin/env bash
# Wrap Shellboat into a macOS .app bundle so the Dock / Cmd-Tab show the Shellboat icon and name.
# (A bare `dotnet run` executable gets the generic "exec" icon; macOS only reads icons from a bundle's
# Info.plist + .icns — Avalonia's Window.Icon doesn't reach the Dock.)
#
#   ./build/macos-app.sh            Debug build → dist/Shellboat.app
#   ./build/macos-app.sh --run      ...then launch it (SHELLBOAT_PROFILE is passed through, e.g. =dev)
#   ./build/macos-app.sh --release  self-contained Release publish for this Mac's architecture
#
# The RDP wrapper is included when ./build/native.sh has been run (Shellboat.Rdp.csproj copies it into the output).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
[[ "$(uname)" == "Darwin" ]] || { echo "macOS only." >&2; exit 1; }

run=false; release=false
for arg in "$@"; do
  case "$arg" in
    --run) run=true ;;
    --release) release=true ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

project="$root/src/Shellboat.Desktop/Shellboat.Desktop.csproj"
app="$root/dist/Shellboat.app"
stage="$root/dist/.stage"
rm -rf "$stage"

if $release; then
  rid="osx-$( [[ "$(uname -m)" == "arm64" ]] && echo arm64 || echo x64 )"
  dotnet publish "$project" -c Release -r "$rid" --self-contained true -o "$stage" --nologo -v quiet
else
  dotnet build "$project" -c Debug -o "$stage" --nologo -v quiet
fi

version="$(cd "$root" && git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || true)"
version="${version:-0.1.0}"

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -a "$stage/." "$app/Contents/MacOS/"
rm -rf "$stage"
cp "$root/assets/brand/icons/shellboat.icns" "$app/Contents/Resources/shellboat.icns"
cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Shellboat</string>
  <key>CFBundleDisplayName</key><string>Shellboat</string>
  <key>CFBundleIdentifier</key><string>hr.moberg.shellboat</string>
  <key>CFBundleExecutable</key><string>shellboat</string>
  <key>CFBundleIconFile</key><string>shellboat.icns</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleVersion</key><string>${version}</string>
  <key>CFBundleShortVersionString</key><string>${version}</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
chmod +x "$app/Contents/MacOS/shellboat"
# Make Finder/Dock notice the new icon if an older bundle was cached.
touch "$app"
echo "Built $app ($version)"

if $run; then
  # Exec the bundle's binary directly (not `open`) so env vars such as SHELLBOAT_PROFILE reach it;
  # macOS still treats it as the bundle and uses its icon and name.
  exec "$app/Contents/MacOS/shellboat"
fi
