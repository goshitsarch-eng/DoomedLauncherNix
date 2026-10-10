# Platform support and validation

Version 5.0.0-alpha.1 targets the following platforms. A platform target and a
configured CI job are implementation evidence; they are not a claim that a job
has executed or that interactive QA has passed.

| Target | Artifact | Build integration | Validation in this cloud task |
| --- | --- | --- | --- |
| Linux x86_64 | tar.gz | Flutter runner + automatic Cargo/CMake hook | Debug and release builds passed; archive, headless CLI and bundled Rust ABI validated |
| Linux aarch64 | tar.gz | Native ARM64 CI runner + Cargo target | Requires ARM64 CI run |
| Linux Flatpak x86_64 | .flatpak | Offline GNOME SDK build with vendored Cargo and locked Pub inputs | Built, installed, native CLI and visible Xvfb window passed; PWAD import, managed copy and SQLite map metadata verified |
| Linux Flatpak aarch64 | .flatpak | Same offline build on native ARM64 CI runner | Requires ARM64 CI run |
| Windows x64 | Portable ZIP + Inno Setup installer | Flutter runner + automatic MSVC Cargo/CMake hook | Requires Windows CI and interactive QA |
| macOS Intel + Apple Silicon | Universal ZIP + DMG | Xcode Cargo/lipo/embed phase | Requires macOS CI and interactive QA |

The local archive was built and run on Ubuntu 26.04. ELF version inspection
finds GLIBC 2.34 / GLIBCXX 3.4.21 in the runner, GLIBC 2.39 in the Rust core,
and GLIBC 2.18 in the bundled Flutter engine; the bundled plugins do not raise
that minimum. GLIBC 2.39 is therefore a necessary symbol requirement, not proof
that every system GTK/GLib/OpenGL dependency works on Ubuntu 24.04. The
configured CI uses Ubuntu 24.04 for future Linux archives, but those jobs have
not run in this task. Users of older distributions should use the validated
Flatpak bundle with its declared runtime. Development libraries for GTK 3 are required
to compile the Linux runner; users need GTK 3 and the desktop's GL stack at
runtime. Windows requires Windows 10/11 x64. The runner targets macOS 12 or newer. Inspect the generated build's
`LSMinimumSystemVersion` after plugin/toolchain updates before distributing to
older systems. Windows ARM64
hooks exist but do not constitute a tested or required release target. The Windows
process-ownership module and its Job Object tests cross-compile in a standalone
`x86_64-pc-windows-gnu` harness on Linux. This does not validate the entire Rust
core, the MSVC Flutter runner, installer or Windows runtime.

The pinned official Flutter tag was checked against its public Git ref and a
fresh Linux SDK Git clone/bootstrap passed using the same helper as CI. The
release-index endpoint returned HTTP 404 during this task, so CI now uses the
verified Git revision rather than release-index lookup. Windows/macOS SDK
bootstrap and all external CI jobs remain unexecuted here.

## Feature and permission differences

The same Dart frontend and Rust core implement each desktop target. The final
x86_64 Flatpak was compiled offline inside GNOME SDK 49 using its Rust 1.98.1
and LLVM 20.1.8 extensions. All 204 locked Cargo packages resolved from vendored
sources; both the application's and Flutter tooling's locked Pub graphs resolved
from the staged cache after regenerating their paths inside the sandbox.
Installation, headless version output and the Xvfb software-rendered window
passed. A generated MAP01 PWAD entered through the command line and the real
Dart/Rust bridge; its managed copy and SQLite ID/map metadata were verified.
The installed Rust notice file also matched the generated 204-package notice
collection. `flatpak-spawn --host /usr/bin/true` passed through the actual D-Bus
bridge to the container host. Physical desktop portals, GPU/accessibility and
host-engine interactive QA still require a desktop session.

The cloud's Docker defaults initially blocked nested namespaces. Validation used
a dedicated non-root container with SYS_ADMIN/NET_ADMIN, the standard seccomp
profile plus a conditional pivot_root allowance, and unmasked container system
paths. It mounted only a dedicated work directory, read-only source and the
managed CA bundle. Standard Flatpak sandbox commands were unchanged. An isolated
system bus and temporary session bus supplied the normal Flatpak services; no
host kernel or global Docker settings were changed. Source-port
execution uses argument arrays. Windows source ports use native `.exe` paths;
macOS supports application bundles; Linux supports native executables and
host-installed wrappers. Ports remain external applications; no engine or IWAD
is shipped with the launcher. Platform-specific detection cannot guarantee every
third-party installation layout. Users can add a source port or select content
explicitly.

Native CLI `--help` and `--version` exit before opening a display. macOS
Finder Open With events queue until the Dart import handler is ready; Windows
installation registers Open With entries without replacing existing defaults.

Flatpak imports use desktop portals and preserves the old Linux library through
a narrow `xdg-data/doomlauncher` permission. Store scanning may require access
to selected external directories. The `org.freedesktop.Flatpak` permission is
necessary to execute source ports on the host and permits host process
execution; this is an explicit part of the application contract. A portal grant
does not automatically grant every host engine access to every file.

macOS direct-distribution builds omit App Sandbox to allow launching selected
engines. Public release signing/notarization and Windows Authenticode signing
are not configured without the maintainers' credentials. Unsigned builds must
be described as unsigned; no signing or platform QA is inferred from a Linux
build.

## Required interactive QA before a stable release

On each supported OS, test the first-run flow, migration of a copied legacy
library, native file/directory pickers, Unicode and space-containing paths,
import/edit/delete and tag operations, source-port detection/manual setup,
launch/stop/session statistics, ordered mod load plans, save/demo/screenshot
associations, online search/download/cancel, links, system themes, keyboard
navigation, window resizing/DPI and persisted preferences. On Flatpak also test
portal-selected imports, host engine spawning and idgames requests. On macOS
exercise both Intel and Apple Silicon and opening the distributed DMG; on
Windows exercise the installer, portable ZIP and uninstallation without losing
library data. Record actual outcomes in `MIGRATION_AUDIT.md`.
