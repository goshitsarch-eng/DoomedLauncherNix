# Building Doom Launcher

The primary application is Flutter/Dart with an in-process Rust library. The
previous Qt implementation remains available in Git history for migration
comparisons. These commands build the Flutter/Rust application. No game engine or commercial IWAD
is bundled.

## Toolchain and locked dependencies

Use Flutter **3.47.7 stable** (Dart 3.13.5), Rust **1.99.0**, Git and Python
**3.11+**. Install Flutter from its official signed/checksummed distribution and
Rust with rustup. CI uses `scripts/setup_flutter.py` to shallow-clone the
official `3.47.7` tag and verify commit
`abaf9c523780a608bd46686fd5e53740a07077f8` before bootstrapping with Flutter's
official wrapper; this avoids depending on the release-index endpoint. You can
use the same helper with `--directory /path/to/flutter-sdk`. Add Flutter's
`bin` and Cargo's `bin` to `PATH`. Do not disable
TLS or package checksum verification. `pubspec.lock` and `rust/Cargo.lock` are
committed application lockfiles.

```sh
flutter doctor -v
rustup component add rustfmt clippy
flutter pub get --enforce-lockfile
cargo fetch --manifest-path rust/Cargo.toml --locked
```

Routine development uses the existing checkout. Cloud tasks already have an
isolated workspace; an extra Git worktree is unnecessary. In the prepared cloud
machine, source `/workspace/.cloud-setup/doomedlauncher/env.sh`. Native Linux
builds there use `/workspace/.cloud-setup/doomedlauncher/dev` followed by the
normal command because the outer host lacks GTK development packages.

## Linux

On Ubuntu 24.04 or newer:

```sh
sudo apt-get update
sudo apt-get install clang cmake ninja-build pkg-config libgtk-3-dev liblzma-dev libstdc++-14-dev
flutter run -d linux
flutter build linux --release
python3 scripts/package.py --skip-build
```

The bundle is `build/linux/x64/release/bundle` on x86_64 and
`build/linux/arm64/release/bundle` on aarch64. Run its `doomedlauncher` executable
with its `data` and `lib` directories intact. Both `flutter run` and `flutter
build` invoke Cargo automatically and install `libdoomedlauncher_core.so` beside
Flutter's native library in `bundle/lib`. Profile builds use Cargo release mode.
Use the native architecture toolchain; the scripts do not promise cross-linking
a GNU/Linux binary against an unrelated sysroot.

## Windows

Install Visual Studio 2022 with **Desktop development with C++**, the current
Windows SDK, Git, Python and Inno Setup 6. Use Rust's MSVC toolchain (not GNU).

```powershell
flutter config --enable-windows-desktop
flutter run -d windows
flutter build windows --release
python scripts/package.py --skip-build
```

CMake builds the Rust DLL and puts it next to `doomedlauncher.exe` in
`build/windows/x64/runner/Release`. Packaging includes the permitted Visual C++
redistributable DLLs for a portable ZIP and creates a per-user Inno installer
without requiring administrator privileges. The installer preserves application
library data when uninstalled. `--skip-installer` produces only the ZIP when
Inno Setup is unavailable. ARM64 native hooks exist, but the required release
and QA matrix currently covers Windows x64.

## macOS

Install Xcode and accept its license, install command-line tools, then install
both native Rust targets:

```sh
rustup target add aarch64-apple-darwin x86_64-apple-darwin
flutter config --enable-macos-desktop
flutter run -d macos
python3 scripts/package.py
```

The Xcode target invokes `scripts/build_rust_macos.sh` for each architecture in
`ARCHS`, combines them with `lipo`, and embeds the resulting dylib in
`doomedlauncher.app/Contents/Frameworks`. Standard release builds and packaging
are universal (Intel and Apple Silicon); the packaging command verifies both
architectures before labeling the ZIP/DMG universal. The app is in
`build/macos/Build/Products/Release`.

Local bundles are ad hoc signed. Public Developer ID signing and notarization
need maintainers' Apple credentials and are not simulated by CI. An unsigned
prerelease may trigger Gatekeeper. Use macOS's Open confirmation for a bundle
you have verified; do not disable Gatekeeper globally. The app deliberately
omits App Sandbox because launching user-selected external engines is a core
feature; this distribution targets direct installation, not the Mac App Store.

## Flatpak

The GNOME 49 runtime supplies GTK 3 and system integration. The application,
Flutter engine and Rust core are built and bundled together. The Rust SDK
extension supplies Cargo/rustc, and the LLVM 20 extension supplies Flutter's
Clang toolchain. Install the matching architecture runtimes:

```sh
sudo apt-get install flatpak flatpak-builder elfutils dbus
flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install --user --noninteractive flathub org.gnome.Platform//49 org.gnome.Sdk//49 org.freedesktop.Sdk.Extension.rust-stable//25.08 org.freedesktop.Sdk.Extension.llvm20//25.08
bash scripts/package_flatpak.sh
flatpak install --user dist/DoomedLauncher-*-linux-*.flatpak
flatpak run com.goshapps.DoomLauncher
```

`prepare_flatpak.py` first warms the installed official Flutter SDK, resolves
locked Pub packages, and vendors Cargo dependencies with their verified registry
checksums. It stages both the application's and Flutter tooling's Pub graphs
under `build/flatpak-input`, and regenerates their machine-specific package paths
offline inside the build sandbox. The manifest
then builds **offline inside the SDK**, so a tarball compiled on a newer host is
never mislabeled as a compatible Flatpak. Staged SDK inputs are
architecture-specific. Run preparation again after lockfile or SDK changes.
Allow roughly 12 GB of free disk space for SDKs, vendored sources and build trees.
CI installs the resulting bundle, checks its native CLI and exercises desktop
startup under Xvfb before accepting a release artifact. The runtime check also
imports a generated PWAD through the real Dart/Rust bridge and verifies its
managed copy and SQLite map metadata.

Default permissions allow display/GPU/IPC, network requests for idgames,
`xdg-data/doomlauncher` for the existing library, and the Flatpak host launcher
D-Bus service for user-selected host engines. File imports use document portals;
the manifest grants neither all of `home` nor `host`. Host execution via
`org.freedesktop.Flatpak` is a powerful capability required for external engine
launching. Some store discovery roots are intentionally outside the default
filesystem grant; select a directory through the picker or add a narrow
`flatpak override --user --filesystem=/absolute/game/directory:ro
com.goshapps.DoomLauncher` for the desired game installation.

Headless/container environments may lack user namespaces, a user session bus,
portals or an actual compositor. Successful manifest generation is not proof of
Flatpak runtime behavior. See `PLATFORM_SUPPORT.md` for the validation record.

## Checks

```sh
dart format --output=none --set-exit-if-changed lib test integration_test
flutter analyze --fatal-infos
cargo build --manifest-path rust/Cargo.toml --locked
flutter test
cargo fmt --manifest-path rust/Cargo.toml -- --check
cargo clippy --manifest-path rust/Cargo.toml --locked --all-targets -- -D warnings
cargo test --manifest-path rust/Cargo.toml --locked
python3 -m unittest discover -s scripts/tests -v
python3 scripts/check_release.py
```

On Linux, install `xvfb xauth dbus-x11 openbox xdotool` and run
`scripts/test_desktop.sh`. It starts a desktop display when needed and exercises
the actual GTK runner, native file pickers, and Rust core with disposable files
and a local HTTP fixture. CJK/emoji fonts improve Unicode screenshot coverage.
Set `DOOMEDLAUNCHER_QA_EVIDENCE` to an output directory and install ImageMagick
to capture audit checkpoints. See [the hands-on audit](docs/HANDS_ON_AUDIT.md).
GUI, file portal, external engine and signed package
QA must run on the destination OS, in addition to headless unit tests. The macOS
CI job also runs the existing Runner scheme's native XCTest target to check the
application identity, Finder document registration and embedded Rust ABI.

## Release artifacts

`python3 scripts/package.py` runs the native release build and creates packages
in `dist`; `--skip-build` reuses an already verified release bundle. `--offline` uses
already cached locked Pub packages for isolated development environments. Each package
includes the project license and generated `THIRD_PARTY_NOTICES.md` with original
license texts from the locked Cargo graph. Flutter's own notice asset accompanies
its bundled Dart dependencies. Flatpak installs Rust notices under
`/app/share/licenses/com.goshapps.DoomLauncher`. Notice generation uses offline
Cargo metadata, so run the documented locked `cargo fetch` first.
Each package set has a SHA256 manifest. The workflow validates Flutter/Rust/AppStream/changelog
versions, analyzes and tests all host platforms, builds the actual desktop
applications, and builds both Flatpak architectures. It publishes only for a
matching `vVERSION` tag after every required job succeeds and
`scripts/verify_artifacts.py` verifies all eight artifacts and hashes.
Prerelease versions such as `5.0.0-alpha.1` are marked as GitHub prereleases.
Ordinary branch pushes and pull requests never publish releases. No signing
credentials are needed to build local or unsigned CI packages.
