#!/usr/bin/env bash
# Reproducible icon build for Shellboat (same pipeline shape as Bearing's assets/brand/build-icons.sh).
#
# Renders shellboat-tile.svg to a PNG size ramp — sizes <=24px from shellboat-tile-small.svg, whose simplified
# mark survives at tab/tray size — then packs:
#   - icons/shellboat.ico   Windows exe icon (16/24/32/48/256)
#   - icons/shellboat.icns  macOS (16..1024 + Retina slots)
#   - src/Shellboat.App/Assets/shellboat-256.png  the in-app window icon
#
# Renderer: ImageMagick (`magick`) when installed, otherwise headless Google Chrome / Chromium.
# Also needs python3.
set -euo pipefail
cd "$(dirname "$0")"

SRC="shellboat-tile.svg"
SRC_SMALL="shellboat-tile-small.svg"
SMALL_MAX=24
OUT="icons"
PNG="$OUT/png"
mkdir -p "$PNG"

chrome=""
for c in "${CHROME:-}" "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" google-chrome chromium chromium-browser; do
  if [[ -n "$c" ]] && command -v "$c" >/dev/null 2>&1; then chrome="$c"; break; fi
done

render() { # size
  local size="$1" src="$SRC"
  (( size <= SMALL_MAX )) && src="$SRC_SMALL"
  if command -v magick >/dev/null 2>&1; then
    magick -background none -density 384 "$src" -resize "${size}x${size}" "$PNG/tile-${size}.png"
  elif [[ -n "$chrome" ]]; then
    local page; page="$(mktemp -t shellboat-icon).html"
    printf '<html><body style="margin:0"><img src="file://%s" width="%s" height="%s"></body></html>' \
      "$PWD/$src" "$size" "$size" > "$page"
    "$chrome" --headless --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
      --default-background-color=00000000 --window-size="${size},${size}" \
      --screenshot="$PWD/$PNG/tile-${size}.png" "file://$page" >/dev/null 2>&1
    rm -f "$page"
  else
    echo "Need ImageMagick (magick) or Google Chrome/Chromium to render SVGs." >&2
    exit 1
  fi
}

echo "Rendering PNG ramp from $SRC (<=${SMALL_MAX}px from $SRC_SMALL) ..."
for s in 16 24 32 48 64 128 256 512 1024; do
  render "$s"
done

echo "Packing $OUT/shellboat.ico ..."
python3 pack-ico.py "$OUT/shellboat.ico" \
  "$PNG/tile-16.png" "$PNG/tile-24.png" "$PNG/tile-32.png" "$PNG/tile-48.png" "$PNG/tile-256.png"

echo "Packing $OUT/shellboat.icns ..."
python3 pack-icns.py "$OUT/shellboat.icns" \
  16:"$PNG/tile-16.png"   32:"$PNG/tile-32.png"   64:"$PNG/tile-64.png" \
  128:"$PNG/tile-128.png" 256:"$PNG/tile-256.png" 512:"$PNG/tile-512.png" \
  1024:"$PNG/tile-1024.png"

echo "Window icon ..."
cp "$PNG/tile-256.png" ../../src/Shellboat.App/Assets/shellboat-256.png

echo "Done."
ls -la "$OUT"
