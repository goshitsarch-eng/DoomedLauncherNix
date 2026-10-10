# Changelog

## 5.0.0-alpha.1 — 2026-10-09

- Introduce a Flutter desktop interface and a Rust domain core for Linux,
  Windows and macOS, replacing the Qt application as the primary build.
- Migrate the library, launch, content, settings and online workflows with
  explicit asynchronous FFI boundaries and preservation of legacy data.
- Integrate Cargo into standard Flutter desktop builds so native libraries are
  compiled and bundled automatically for debug, profile and release modes.
- Add Linux archives, an offline Flatpak build, a Windows portable ZIP and
  per-user installer, and universal macOS ZIP/DMG packaging.
- Require complete platform builds and checksummed artifacts before a matching
  version tag can publish a release. Alpha tags publish as prereleases.

This is a migration prerelease. See `MIGRATION_AUDIT.md` and
`PLATFORM_SUPPORT.md` for exact implementation and validation status. Signed
Windows/macOS distribution and interactive destination-platform QA are separate
release requirements. Legacy release history remains in `RELEASENOTES.md`.
