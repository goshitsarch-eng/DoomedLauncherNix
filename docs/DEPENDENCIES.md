# Dependencies and advisory review

This is a review of the committed dependency locks on **2026-10-09**. It records
declared licenses, why dependencies are used and checks actually performed.
Advisory databases describe known issues; a clean result does not prove that a
dependency or the application is free of vulnerabilities.

## Rust

The Rust core owns database, archive, image, network and process operations.
Dependencies are resolved in [`rust/Cargo.lock`](../rust/Cargo.lock); build and
release instructions use `--locked`.

| Direct dependency, locked version | Purpose and selected scope | Declared license |
| --- | --- | --- |
| `rusqlite` 0.37.0 | SQLite library storage, migration and backups; bundled SQLite avoids requiring a separate system installation | MIT |
| `zip` 4.6.1 | ZIP/PK3 parsing and extraction; default features disabled, deflate enabled | MIT |
| `image` 0.25.10 | Artwork decoding; only PNG, JPEG and BMP codecs enabled | MIT OR Apache-2.0 |
| `reqwest` 0.12.28 | HTTP requests with Rustls and native trusted certificate roots; default features disabled | MIT OR Apache-2.0 |
| `serde` 1.0.229, `serde_json` 1.0.151 | Typed bridge requests, replies and persisted JSON | MIT OR Apache-2.0 |
| `thiserror` 2.0.21 | Structured error handling | MIT OR Apache-2.0 |
| `sha2` 0.10.9, `uuid` 1.27.0 | Content digests and collision-resistant operation identifiers | MIT OR Apache-2.0 |
| `regex` 1.13.1, `url` 2.5.8, `shlex` 1.3.0 | Structured text/URL parsing and argument tokenization | MIT OR Apache-2.0 |
| `chrono` 0.4.45 | Dates and session timestamps | MIT OR Apache-2.0 |
| `walkdir` 2.5.0 | Filesystem traversal | Unlicense OR MIT |
| `libc` 0.2.190, `windows-sys` 0.61.2 | Target-specific process and operating-system APIs | MIT OR Apache-2.0 |
| `tempfile` 3.27.0 | Isolated temporary fixtures in development tests | MIT OR Apache-2.0 |

Versions shown are the direct resolved dependencies; additional versions may
exist transitively. All 204 package records returned by locked Cargo metadata
had declared license expressions. Transitive declarations also include BSD,
ISC, Zlib, Unicode-3.0 and LLVM-exception alternatives. This inventory is not a
substitute for retaining the upstream notices required when distributing
third-party components. The application core declares GPL-3.0-only.

### RustSec result

**Passed: 0 known vulnerabilities and 0 warnings across 204 lockfile entries.**
`cargo-audit` **0.22.2** ran with `--deny warnings`, without advisory ignores or
target OS/architecture filters. It queried RustSec database commit
`7eebec69c352c7191b1f13eb95dd510eeca5d1de`, updated on 2026-10-09 and containing
1,296 advisories. Yanked-package checking was enabled.

The official [RustSec release](https://github.com/rustsec/rustsec/releases/tag/cargo-audit/v0.22.2)
Linux musl executable was downloaded using normal HTTPS certificate checks.
Its archive SHA-256 matched the digest published with the release asset:
`7fb9497f8594b389e5fce5ef9b92db08432996895b2e0c5a0167a69ed445c428`.
The checked `rust/Cargo.lock` SHA-256 was
`6cc0bbdff21457a430a95e633b59ac3caa5821dd9b10e1376cb8432c5d59f64c`.

Repeat after installing `cargo-audit` from its official distribution:

```sh
cargo audit --file rust/Cargo.lock --deny warnings
```

## Flutter and Dart

The UI uses Flutter's rendering, semantics and standard controls. Native plugins
provide desktop integration; gameplay, archives, network downloads and storage
remain in Rust. [`pubspec.yaml`](../pubspec.yaml) declares Dart
`>=3.12.0 <4.0.0`, Flutter `>=3.44.0` and compatible package ranges; [`pubspec.lock`](../pubspec.lock)
locks the application dependency graph. Builds use
`flutter pub get --enforce-lockfile` with the pinned Flutter toolchain described
in [BUILDING.md](../BUILDING.md).

| Package | Declared range → locked version | Purpose / declared desktop platforms | License |
| --- | --- | --- | --- |
| `ffi` | `^2.1.4` → 2.2.0 | Memory allocation helpers for the Rust C ABI | BSD-3-Clause |
| `path` | `^1.9.1` → 1.9.1 | Platform-aware path handling in Dart | BSD-3-Clause |
| `path_provider` | `^2.1.5` → 2.1.6 | Application data locations; Linux, macOS, Windows | BSD-3-Clause |
| `file_selector` | `^1.0.4` → 1.1.0 | Native file/directory selection; Linux, macOS, Windows | BSD-3-Clause |
| `url_launcher` | `^6.3.2` → 6.3.3 | Open external files and validated links; Linux, macOS, Windows | BSD-3-Clause |
| `window_manager` | `^0.5.1` → 0.5.2 | Window lifecycle and geometry; Linux, macOS, Windows | MIT |
| `desktop_drop` | `^0.7.0` → 0.7.1 | Drag and drop; Linux, macOS, Windows | Apache-2.0 |
| `flutter_lints` (development) | `^6.0.0` → 6.0.0 | Static-analysis rules | BSD-3-Clause |

Installed package manifests, licenses and changelogs were inspected, including
the platform implementations and their interface packages. The Flutter SDK and
its test packages come from the pinned SDK. Hosted lock entries include SHA-256
checksums, and the application has no dependency overrides. The declared SDK
floors now match the existing lock's minimum; the tested toolchain is newer.

Maintenance considerations:

- The Flutter-maintained plugins come from [flutter/packages](https://github.com/flutter/packages).
  `window_manager` and its transitive `screen_retriever` announce a planned
  transition to [nativeapi-flutter](https://github.com/libnativeapi/nativeapi-flutter).
  They remain the locked integration; that announcement is not evidence that
  the current packages are abandoned.
- `path_provider_foundation` 2.6.0 uses Foundation through FFI. Its changelog
  records a prior 2.5.0 retraction for production-build problems. Actual macOS
  release validation remains necessary. The installed macOS file selector's
  native manifests require macOS 10.15 despite an older README minimum.
- Transitive `jni` 1.1.0 is registered in the Linux/Windows FFI plugin graph.
  Its native CMake integration conditionally builds `libdartjni` when JNI/JDK
  components are available; it is not necessarily present in every desktop
  bundle. Review generated native dependencies when packaging on a new machine.
- `flutter pub outdated --json` completed successfully. The only outdated
  direct package was `desktop_drop`: locked 0.7.1, latest/resolvable 0.8.4,
  outside the declared `^0.7.0` range. No upgrade was applied; a change needs
  drag/drop regression and platform checks. Transitive updates were also
  reported, so this is not a claim that every dependency is current.

### Pub advisory result

**0 matching published advisories for the locked versions.** The official
`https://pub.dev/api/packages/<package>/advisories` endpoints were queried for
all **80 hosted packages** in `pubspec.lock`: 80 successful responses and no
request errors. One historical advisory was returned for `http`:
[GHSA-4rgh-jx4f-qfcq / CVE-2020-35669](https://github.com/advisories/GHSA-4rgh-jx4f-qfcq)
affects versions below 0.13.3; the lock uses 1.6.0. The other 79 responses
contained no advisories. This check covers the hosted Dart package advisory
feed; it is not an audit of the Flutter engine, operating-system libraries or
all native code used by plugins.

The checked `pubspec.lock` SHA-256 was
`7e7edec10e2fff2f073e403e877be11e9b9e3403cbf2050d4f48ce6d45e309ca`.

## Distributing original notices

[`scripts/third_party_notices.py`](../scripts/third_party_notices.py) collects
the original Rust package `LICENSE`, `COPYING`, `NOTICE` and copyright files,
including a declared `license_file`. It uses locked, offline Cargo metadata,
so cached registry packages or the Flatpak vendor tree must be available.
The application GPL text is included from the repository's `LICENSE`.
The reviewed exception is `r-efi` 6.0.0, whose published `AUTHORS` contains its
MIT permission text, copyright holders and alternative-license notices.

```sh
cargo fetch --locked --manifest-path rust/Cargo.toml
python3 scripts/third_party_notices.py --output build/THIRD_PARTY_NOTICES.md
```

The generator fails with the package name when a license is missing, invalid
UTF-8, oversized or outside its package. It emits deterministic text without
local source paths, including all resolved packages across development and
target-specific dependencies. The checked graph produced notices for all 204
packages. Native distributions include `THIRD_PARTY_NOTICES.md`; Flatpak installs
it under `/app/share/licenses/com.goshapps.DoomLauncher/`. Flutter's generated
Dart notices remain in the Flutter assets alongside these Rust notices.

## Keeping the review current

[Dependabot](../.github/dependabot.yml) checks Pub and Cargo dependencies weekly
and GitHub Actions monthly. It proposes pull requests; updates still need
review, lockfile changes and the relevant native/Flutter tests. No dependency
was upgraded merely to obtain this advisory result. Repeat advisory checks
before publishing a release and after dependency changes.

Desktop plugin platform declarations describe the publisher's implementations.
They do not establish that this application's Windows and macOS bundles have
been exercised. Consult [PLATFORM_SUPPORT.md](../PLATFORM_SUPPORT.md) for actual
platform evidence and remaining checks.
