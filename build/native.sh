#!/usr/bin/env bash
# Build the vendored FreeRDP wrapper (native/freerdp-wrapper) and copy it where Shellboat.Rdp loads it from.
#
#   macOS:  brew install cmake pkgconf freerdp
#   Linux:  apt install cmake pkg-config freerdp3-dev   (or your distro's FreeRDP 3 dev packages)
#   Windows: use build/native.ps1 (vcpkg)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="$root/native/freerdp-wrapper"
out="$root/native/build"

prefix_args=()
if [[ "$(uname)" == "Darwin" ]] && command -v brew >/dev/null; then
  prefix_args=(-DCMAKE_PREFIX_PATH="$(brew --prefix freerdp)")
fi

cmake -S "$src" -B "$out" -DCMAKE_BUILD_TYPE=Release "${prefix_args[@]}"
cmake --build "$out" -j

case "$(uname)" in
  Darwin) lib="libfreerdp_wrapper.dylib"; rid="osx-$( [[ $(uname -m) == arm64 ]] && echo arm64 || echo x64)";;
  Linux)  lib="libfreerdp_wrapper.so";    rid="linux-$( [[ $(uname -m) == aarch64 ]] && echo arm64 || echo x64)";;
  *) echo "unsupported OS: $(uname)" >&2; exit 1;;
esac

dest="$root/native/out/$rid"
mkdir -p "$dest"
# -L: copy the real file behind the versioned symlink chain.
cp -L "$out/$lib" "$dest/$lib"
echo "built $dest/$lib"
