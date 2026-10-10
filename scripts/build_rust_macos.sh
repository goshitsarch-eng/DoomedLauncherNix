#!/bin/bash
# Called by Xcode after Flutter embeds its frameworks. Handles Debug, Profile,
# Release, Intel, Apple Silicon and universal builds without manual copying.
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="${CARGO_HOME:-$HOME/.cargo}/bin:$PATH"
profile=release
cargo_args=(--release)
if [[ "${CONFIGURATION:-Release}" == Debug* ]]; then
  profile=debug
  cargo_args=()
fi
libraries=()
for arch in ${ARCHS:-$(uname -m)}; do
  case "$arch" in
    arm64) target=aarch64-apple-darwin ;;
    x86_64) target=x86_64-apple-darwin ;;
    *) echo "Unsupported macOS architecture: $arch" >&2; exit 1 ;;
  esac
  cargo build --locked --manifest-path "$repo_root/rust/Cargo.toml" \
    --target "$target" --target-dir "$repo_root/build/native" "${cargo_args[@]}"
  libraries+=("$repo_root/build/native/$target/$profile/libdoomedlauncher_core.dylib")
done
destination="${TARGET_BUILD_DIR:?}/${FRAMEWORKS_FOLDER_PATH:?}"
mkdir -p "$destination"
/usr/bin/lipo -create "${libraries[@]}" -output "$destination/libdoomedlauncher_core.dylib"
/usr/bin/install_name_tool -id '@rpath/libdoomedlauncher_core.dylib' "$destination/libdoomedlauncher_core.dylib"
if [[ "${CODE_SIGNING_ALLOWED:-YES}" != NO ]]; then
  /usr/bin/codesign --force --sign "${EXPANDED_CODE_SIGN_IDENTITY:--}" \
    --timestamp=none "$destination/libdoomedlauncher_core.dylib"
fi
