#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
python3 scripts/prepare_flatpak.py
flatpak-builder --user --force-clean --disable-rofiles-fuse --jobs="${BUILD_JOBS:-2}" \
  --repo=build/flatpak-repo build/flatpak-app flatpak/com.goshapps.DoomLauncher.yml
version="$(python3 scripts/check_release.py)"
arch="$(flatpak --default-arch)"
mkdir -p dist
artifact="DoomedLauncher-$version-linux-$arch.flatpak"
flatpak build-bundle --arch="$arch" \
  --runtime-repo=https://flathub.org/repo/flathub.flatpakrepo \
  build/flatpak-repo "dist/$artifact" com.goshapps.DoomLauncher stable
(cd dist && sha256sum "$artifact" > "SHA256SUMS-flatpak-$arch.txt")
