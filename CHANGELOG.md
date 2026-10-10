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
- Fix filename search, rename error recovery, selection focus and unavailable
  Library commands found during the hands-on desktop audit.
- Keep minimum-size and long-title dialogs usable, align management headings,
  prevent launch notifications from covering session controls, and allow
  closing after database startup failure.
- Report missing Linux file/browser handlers instead of accepting GTK’s silent
  launch acknowledgment; preserve Flatpak portal handling.
- Add repeatable GTK/Rust desktop audits with native pickers, local HTTP failure
  fixtures, keyboard and geometry coverage, and a corrupt-startup regression.
- Require complete platform builds and checksummed artifacts before a matching
  version tag can publish a release. Alpha tags publish as prereleases.

This is a migration prerelease. See `MIGRATION_AUDIT.md` and
`PLATFORM_SUPPORT.md` for exact implementation and validation status. Signed
Windows/macOS distribution and interactive destination-platform QA are separate
release requirements. Legacy release history remains in `RELEASENOTES.md`.
