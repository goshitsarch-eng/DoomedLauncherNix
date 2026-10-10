# Contributing

Read `ARCHITECTURE.md`, `MIGRATION_AUDIT.md` and `BUILDING.md` before changing the
application. Use the existing checkout; cloud tasks are already isolated.

Dart owns presentation, navigation and the asynchronous typed bridge. Rust owns
library persistence, content parsing, source-port discovery, launch planning,
process/session management and network operations. Avoid placing domain rules
in widget callbacks or introducing an application shell around another runtime.
Keep the JSON FFI protocol explicit and add contract tests when changing it.
Never unwind across C ABI boundaries or free a Rust allocation using Dart's
allocator.

Preserve legacy data, including unknown configuration and schema fields. Run
migration against a copied fixture and verify a backup exists before changing a
live database. Do not log credentials, embed tokens in configuration or relax
TLS, checksum or archive-path validation. Use separate data roots for tests.

Follow the formatting, analysis, Rust and Flutter test commands in `BUILDING.md`.
Add focused regression coverage for changed domain behavior and integration
coverage for cross-language or platform boundaries. Test actual native builds
when touching runners, FFI or packaging. Report untested OS behavior plainly.

A pull request should explain the user-visible behavior, why the change is
needed, validation performed and any remaining platform limits. Keep changes
reviewable. Do not update lockfiles incidentally; when updating a dependency,
include the reason and verify reproducible locked installation.

For releases, update the version in `pubspec.yaml`, `rust/Cargo.toml`, the latest
AppStream release, `macos/Runner/Configs/AppInfo.xcconfig` native CLI version
and `CHANGELOG.md` together. Run `scripts/check_release.py`.
The tag workflow publishes only when all required builds and artifact checksum
gates pass. A feature-complete implementation is not a substitute for the
interactive platform QA checklist in `PLATFORM_SUPPORT.md`.
