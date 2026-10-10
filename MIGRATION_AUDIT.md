# Migration audit: Flutter desktop and Rust core

Baseline: `f48510d8b9eea4051e09d42e6393517485b5bd06`, DoomedLauncherNix
4.0.3. Migrated application: `5.0.0-alpha.1`.

The audited library, launcher, store discovery, archive and settings workflows
now have Flutter/Dart controls and a Rust implementation. The application uses
one Flutter desktop frontend and does not require Qt/Kirigami at runtime. The old
`src/`, `tests/` and root Qt CMake entry point have been retired; there is no
active duplicate implementation. Historical Qt paths below refer to the baseline
commit, where the original implementation and its tests remain recoverable.

Implementation status and verification scope are separate. **Implemented** means
the corresponding code and controls exist. **Verified** names an actual check
that passed. A Linux fixture, widget test or successful build does not establish
physical game rendering, desktop portal behavior, accessibility certification,
or successful Windows/macOS/Flatpak installation.

## Implemented architecture

| Responsibility | Current implementation |
| --- | --- |
| Desktop shell, navigation, selection, search/filter/sort, responsive library and details, dialog drafts | `lib/app.dart`, `lib/ui/library_page.dart`, `lib/ui/details_panel.dart`, `lib/ui/play_dialog.dart` and `lib/ui/edit_dialog.dart` |
| Source ports, tags, store scans, settings, idgames search/download lifecycle | `lib/ui/management.dart`, `lib/ui/management/`, `lib/ui/idgames.dart` |
| Window lifecycle, platform data directories, CLI arguments, native file/URL integration | `lib/main.dart`, `lib/bridge/backend.dart`, platform Flutter runners and plugins |
| Coarse application commands and compatible persistence | `rust/src/api.rs`, `rust/src/storage.rs`, `rust/src/schema.sql` |
| WADs, ZIP/PK3, metadata, artwork and bounded extraction | `rust/src/content.rs`, `rust/src/content/` |
| Ports, launch plans, typed argv, background sessions, statistics | `rust/src/runtime.rs` |
| Store installation discovery, idgames HTTP, background downloads | `rust/src/discovery.rs` |
| Native bridge | `rust/src/ffi.rs`, `lib/bridge/backend.dart`, `docs/BRIDGE_CONTRACT.md` |
| Structured diagnostic logs, bounded rotation and safe error reporting | `lib/state/diagnostics.dart`, bridge/controller hooks |
| Desktop/native-library build, installers, Flatpak staging and release checks | `linux/`, `windows/`, `macos/`, `packaging/`, `flatpak/`, `scripts/`, `.github/workflows/release.yml` |

The selected bridge is an ABI-versioned, synchronous C boundary invoked from Dart
worker isolates. It transports bounded UTF-8 JSON domain requests and structured
results/errors; it does not expose SQL or widget callbacks. Rust owns returned
allocations until `dl_free`, and panics are contained at the boundary. File
parsing, SQLite operations, hashing, discovery and launch preparation stay off
Flutter's UI isolate. Downloads and engine sessions have separate background
workers and observable lifecycle records.

The runtime keeps structured program/argument arrays through native launches and
the Flatpak host bridge. Displayed commands are previews, never shell programs.
Dialog editing remains local until Save or Launch; record IDs preserve selection
when presentation order changes.

## Storage and compatibility

Linux keeps `$XDG_DATA_HOME/doomlauncher` or `~/.local/share/doomlauncher`.
Windows/macOS use their platform application-support locations. An explicit
`DOOMEDLAUNCHER_DATA_DIR` selects another library, including test fixtures.

`DoomLauncher.sqlite` retains the legacy tables, IDs, unknown columns/tables and
configuration values: GameFiles, Files, SourcePorts, IWads, Configuration, Tags,
TagMapping, Stats, GameProfiles and CleanupFiles. Versioned migrations add missing
columns transactionally after an integrity check and create a SQLite backup
before migrating an existing database. Initial schema migration leaves the
managed content tree in place; it does not create a second complete copy of the
game collection. Destructive managed-file operations stage files under `Trash`,
and entry deletion records recovery information.

GameFileID allocation uses a durable high-water mark rather than SQLite's
reusable row IDs. Deleted games therefore cannot donate their retained save or
session directories to new imports. Initial seeding includes existing IDs,
orphaned references, saved engine directories and session journals; external
legacy SQLite inserts also advance the sequence. Failed imports roll back their
identifier reservation together with metadata and files.

The independent baseline schema is
[`rust/tests/fixtures/legacy-4.0.3.sql`](rust/tests/fixtures/legacy-4.0.3.sql).
Its comments record the original commit and source-file Git blob SHA. Tests build
real SQLite databases from this frozen schema, add synthetic legacy records and
files, then exercise public requests. This is stronger than testing a database
created from the new schema, but it is not a claim that an unavailable personal
production library was migrated.

Managed paths retain the established GameFiles, Screenshots, Temp, Demos,
SaveGames, Thumbnails and TitlePics layout. Absolute external records remain
external and are protected from managed deletion. Foreign-platform paths and
missing files remain visible with diagnostic information instead of preventing
the rest of the library from loading. Portable-name validation applies to new
managed imports; legacy names are retained. Unicode case variants cannot create
conflicting new library entries.

TagMapping.FileID still refers to GameFileID. Association type IDs remain 1
screenshot, 2 demo, 3 save, 4 thumbnail and 6 titlepic. Remembered files retain
ordered legacy semicolon strings for ordinary names; names containing semicolons
use a tagged JSON string array so filenames and order remain unambiguous. Both
representations can be read and renamed, including profile and port references.
SourcePorts.LaunchType retains its legacy
TEXT affinity and is converted correctly at the domain boundary. Old local
textual dates remain preserved; new durable session journals use UTC timestamps.
Date ordering and LastPlayed updates compare parsed instants, interpreting legacy
timestamps without offsets as local time. Unknown nonempty legacy date values
remain intact; session journals retain the new timestamp independently.

The first migration translates `LastSelectedTabIndex` by tab identity: Local
now precedes optional Recent, and visible tag offsets account for idgames moving
out of the library tabs. A former idgames selection opens Local; idgames remains
available through the main navigation. Translation runs once and preserves the
original value in the pre-migration database backup.

## Feature migration matrix

Evidence keys are defined in the validation record below. Every row marked
Implemented has a current backend/control path; the evidence column states the
part actually exercised.

| Baseline behavior or workflow | Migration status and implementation | Verification scope / remaining QA |
| --- | --- | --- |
| First-run setup, detect ports, add IWADs and enter library | Implemented in desktop shell, management controls and readiness snapshot | F tests shell/navigation; management tests detection/import commands. Real native picker/portal QA remains platform-dependent. |
| Import several files or recursively scan a directory | Implemented with worker-isolate calls, bounded parsing, per-file success/failure results and managed copies | L verifies recursive/nonrecursive import, malformed files, collisions and failure rollback; B covers Unicode paths. |
| Promote an existing mod to IWAD without replacing an identical file | Implemented using hash comparison and preserved GameFileID | L verifies repeat promotion, duplicate detection, ASCII/Unicode case variants and unchanged bytes. |
| Library list/tile views, search and stable selection | Implemented in Flutter presentation and controller state | F verifies filtering, search, numeric sorting and compact shell layout. A 5,000-entry native snapshot benchmark is recorded below; large-library UI frame/scroll performance and physical display QA remain to be measured. |
| Local, Recent, Untagged, IWAD and custom tag tabs | Implemented; Recent retains the baseline 25-most-recently-downloaded rule | F verifies membership and ordering; L verifies tag mappings and IWAD identity. |
| Sort title, author, filename, rating, last played and downloaded | Implemented with title fallback and numeric/date-aware presentation | F checks presentation ordering; L verifies chronological session updates do not move LastPlayed backwards. |
| Details, description, comments and summary/level statistics | Implemented, including metadata for unavailable content | L verifies details/text/stat records and unavailable paths; native desktop visual QA remains scoped to observed windows. |
| Edit title, author, rating, comments and tags; Cancel discards | Implemented with local drafts and transactional command validation | F verifies cancel sends no write; L verifies metadata/tag rollback together; B verifies persistence through the real FFI. |
| Rename, metadata resync and confirmed deletion | Implemented with coordinated filesystem/database handling and recovery staging | L verifies rename references in GameFiles/IWads/GameProfiles/SourcePorts, failed-rename and sidecar restoration, annotation/map preservation for uninspectable resources, deletion cascades, external-file protection and shared legacy file references. |
| Tag creation/deletion and tab visibility | Implemented with validated names and ID-based mappings | M validates forms/tab choice; L verifies mappings and deletion; B verifies persisted tags. |
| Open original game files, contained/sidecar text and web pages | Implemented through validated domain paths and native external opener | R/L reject arbitrary text members and traversal. Actual associated external apps and platform URL handlers require desktop QA. |
| Configure, detect, edit and remove source ports | Implemented with typed port records, executable discovery and defaults | M verifies complete form preservation and required fields; R tests executable discovery and rejection paths; L verifies legacy types/default updates. |
| Play and Play with Options, remembered defaults | Implemented with command preview, validation, process start and remember-after-start behavior | F tests option drafts; L/B prove failed launches do not overwrite saved choices; E verifies actual Chocolate/Crispy engine rendering through the packaged core. Other engines/platforms require runtime QA. |
| IWAD, map warp, skill, extra parameters and extra-only mode | Implemented with typed argv and standalone engine behavior | R verifies warp/parameters and engine-specific validation; L verifies native argv and self-IWAD defaults; F/M verify standalone Doom64 controls. |
| Ordered additional files, archive members and DEH/BEX | Implemented with ordered resolution and isolated extraction | R verifies member order, identical basenames, CRC/size/path limits and DEH/BEX arguments; F verifies dialog reordering; L rejects missing remembered mods. |
| Latest save, demo recording and associated demo replay | Implemented with per-entry/per-port save directories and engine-specific filename/slot contracts | R verifies compatible save selection, demo plans and isolated recordings; L rejects unassociated playback before spawn; E verifies real Chocolate/Crispy save, load, record and replay. Other formats/engines require runtime QA. |
| Play time and per-level statistics | Implemented using background sessions, durable journals and transactional harvest IDs | R covers levelstat/statdump/ZDoom saves, failure/stop/interrupted states; L verifies idempotent journal harvest; E verifies two actual completed levels per engine, stable database counts and retained Crispy output history. |
| Screenshot/demo/save associations: import, open and remove | Implemented with isolated collision names, shared-reference protection and legacy `.save` support | L verifies types, collisions, sharing and external path preservation. Native viewers and codecs beyond fixture checks require platform QA. |
| Title artwork from WAD/PK3 and existing image associations | Implemented with original palettes, bounded decoding/cache and screenshot/titlepic/thumbnail priority | R verifies Doom/Heretic/Hexen palettes, flat/patch/PNG formats, transparency, custom title definitions and malformed input limits; L verifies ordered screenshots, titlepic and thumbnail fallback. |
| Steam/GOG/Heroic/Lutris scans | Implemented with platform roots, Steam registry/default/custom libraries, VDF parsing, configuration discovery and traversal bounds | R verifies Windows escape syntax, root libraries, configured libraries, GOG prefixes, Heroic/Lutris configuration and symlink boundaries. Actual stores on Windows/macOS remain untested. |
| Doom 64 rerelease detection and explicit engine type | Implemented with dedicated standalone launch settings; foreign native executables are rejected | R verifies discovery and platform-executable safeguards; M/F verify registration/type preservation and sanitized options. Real Doom64 launch requires platform QA. |
| idgames latest/search/details/download/import/download-and-play | Implemented with stale-result handling, byte progress, cancellation, retry, exact imported-entry targeting and explicit temporary-file cleanup | R uses local HTTP fixtures for JSON, status codes, safe URLs/paths, streaming, truncation, limits and redirects; M verifies ordering/cancel/dispose/import/play lifecycle. Live archive/mirror uptime is not asserted. |
| System/light/dark, play-dialog and list/tile preferences | Implemented with validated persistent configuration | F/M verify relevant controls; B verifies persistence. Native system-theme changes still need each-platform QA. |
| Window size and last-tab restoration | Implemented through window lifecycle, durable configuration and one-time translation of the old tab order | L covers old Recent/Local/tag positions, optional tabs, idgames fallback and nonrepeating migration; F checks compact layout. Actual multi-monitor bounds, scaling and maximization need physical display QA. |
| About, CLI help/version and positional file import | Implemented in the Flutter runner/application startup path | Packaging checks validate version metadata; full native CLI/file-association behavior remains subject to recorded platform runs. |
| Drag/drop, shared keyboard actions and clipboard command preview | Added through Flutter desktop plugins and Actions/Shortcuts | Widget/domain tests establish command handling; physical desktop drop, screen-reader/focus traversal and keymap QA remain. |
| Close with active sessions/downloads; temporary-cache preference | Implemented with cancel/stop/wait/error behavior, shutdown admission and guarded cache cleanup | F verifies cancel/confirm and failures; final release GUI QA confirms cancelling restores a usable library and Stop and quit terminates a running fixture and its child process. R tests download cleanup; L verifies CleanTemp and interrupted journals. |
| Structured diagnostics and actionable errors | Implemented with JSONL logs, bounded rotation, safe metadata and UI error detail | D verifies real bridge successes/errors, rotation, concurrent writes, redaction, sink failure and controller lifecycle behavior. Request arguments, paths, credentials and raw error messages are excluded from persisted logs. |
| Legacy utility launches and game-profile editor | Legacy profile records preserved; no corresponding 4.0.3 UI existed | L verifies profile preservation, rename references and cascade cleanup. A new utility/profile product UI is outside existing-feature parity. |
| Undo/redo, printing, multiple windows and notifications | No such 4.0.3 UI was found; no parity claim is made | These are not counted as migrated existing controls. |

PK7 remains an engine input; generic PK7 extraction was not a baseline feature
and is not implemented. ZIP import containers and PK3/IPK3 resources have bounded
extraction/inspection. A process fixture proves argument/session behavior without
proving that a commercial IWAD or game engine renders correctly.

## Validation record for the migrated application

Verified on the Linux cloud machine; commands below refer to this checkout:

* **R — Rust unit/domain tests:** `cargo test --manifest-path rust/Cargo.toml
  --locked` passed **72 unit tests**. They cover content parsing/artwork/cache,
  store and HTTP fixtures, process plans/sessions/statistics and host boundaries.
* **L — Rust library workflow tests:** `cargo test --manifest-path rust/Cargo.toml
  --locked --test library_workflows` passed **34 integration
  tests** in `rust/tests/library_workflows.rs`, using the independent 4.0.3 SQLite
  schema, synthetic WADs/archives and actual native fixture scripts. Coverage
  includes artwork priority, metadata retention, tab migration and durable
  nonrepeating game identifiers and mixed local/RFC3339 dates. The date regression
  also passed with `TZ=America/Los_Angeles`.
  `rust/tests/load_order.rs` additionally passed **3 integration tests** for
  semicolon/Unicode filenames, native fixture-engine remember/defaults, rename
  rollback across all reference owners, legacy strings and corrupt tagged data.
  The complete **109-test Rust run** is recorded at
  `/workspace/.cloud-setup/doomedlauncher/final-rust-tests.log`.
* **F — Frontend tests:** `flutter test test/frontend_test.dart --reporter
  expanded` passed **17 tests**, covering Recent search limits, tagged IWADs,
  null-aware sorting, selection across sorting/filtering, edit cancellation,
  launch-option ordering, compact shell layout, unavailable-entry controls and
  legacy-local/RFC3339 date ordering, quit lifecycle behavior, macOS menu callbacks, guarded native quit requests
  and synchronous rejection of new work while shutdown is being confirmed.
  These platform-channel/widget checks do not establish macOS binary execution.
* **M — Management/idgames tests:** `flutter test test/ui/management_test.dart`
  passed **19 tests**, covering editor validation, store imports, URL validation,
  stale search results, exact download/import/play ownership, retry, cancel,
  disposal, retained settings drafts, surfaced asynchronous errors, a 640-pixel
  pane, scrolling at doubled text scale and download admission during shutdown.
* **B — Real bridge tests:** `flutter test --no-pub
  test/native_backend_test.dart` passed **7 tests** using the actual Rust native
  library. They cover Unicode CRUD/tags, failure recovery, concurrent isolates,
  persistence, real native fixture-process arguments/session recording and shared
  sidecar rename/delete/collision behavior.
* **D — Diagnostics and controller tests:** `flutter test --no-pub
  test/diagnostics_test.dart` passed **8 tests**, including actual FFI calls,
  bounded log rotation and redaction. `test/app_controller_test.dart` passed
  **8 tests** for stale responses, disposal, initialization retry and shutdown
  admission that prevents late imports or launches.
* The complete Flutter suite passed **59 tests**, recorded at
  `/workspace/.cloud-setup/doomedlauncher/frontend-date-suite.log`.
  `flutter analyze --no-pub` reported no issues. Rust `cargo fmt --check` and
  full-crate Clippy with `--all-targets -- -D warnings` passed.
  The final Clippy command/result summary is retained in
  `/workspace/.cloud-setup/doomedlauncher/final-clippy-summary.log`.
* A **Linux debug GTK/Flutter build passed**. `flutter test --no-pub
  integration_test/library_flow_test.dart -d linux --reporter expanded` passed
  **1/1 real desktop integration test** on an X11 virtual display. This exercised
  the actual native core, imported content and edit/save/menu behavior. A real
  popup gesture conflict found during this check was fixed before the passing run.
  The final source rerun also passed **1/1** after shutdown/menu changes, recorded
  in `/workspace/.cloud-setup/doomedlauncher/frontend-final-integration.log`.
* **Linux release build/package passed** with `scripts/package.py --offline`.
  `scripts/verify_native_bundle.py` checked headless `--help`, version
  `5.0.0-alpha.1`, ABI version 1 and actual exported native-library entry points.
  The generated archive is
  `dist/DoomedLauncher-5.0.0-alpha.1-linux-x86_64.tar.gz`.
* **Actual Linux release desktop interaction passed** on an X11 virtual display
  using the packaged executable and Rust library. Wide/compact list and tile
  rendering, details, play options and fixture-process sessions, edit cancel,
  port edit/save with SQLite confirmation, tags, persistent dark/light settings
  and native GTK picker import were exercised. Five actual captures and the
  action-by-action scope are recorded in [`docs/QA.md`](docs/QA.md). This uses
  synthetic game resources and a fixture process, not a game renderer or portal.
  Final release interaction also confirmed active-session close cancellation,
  restoration of search, and Stop and quit removing both the fixture process
  and its child. A single warm-cache/five-entry startup observation measured
  visible window at **1.440 s**, observed rendered library at **1.767 s**, and
  **283,336 KiB idle RSS**; the scope and raw evidence are in `docs/QA.md`.
* **E — Real engine QA passed through the final packaged native core:** Chocolate
  Doom **3.1.1** and Crispy Doom **7.1.0** rendered an original generated MAP01
  using **Freedoom 0.13.0** under Linux/Xvfb. Both saved through their engine UI,
  loaded the latest save with the proper numeric slot, recorded and replayed a
  real demo, completed two map runs with persisted statistics, exited normally
  and responded to Stop. Repeated reads preserved exactly two statistics rows
  per engine; Crispy's current and historical level-statistics files survived
  shutdown. These sessions were shorter than one minute. Evidence is in
  `/workspace/.cloud-setup/doomedlauncher/real-engine-qa/run-478fd412/report.json`;
  two shareable captures and precise scope are in [`docs/QA.md`](docs/QA.md).
  [`docs/ENGINE_COMPATIBILITY.md`](docs/ENGINE_COMPATIBILITY.md) distinguishes
  these real binary checks from inspected upstream contracts for other ports.
* Release metadata validation and **5 Python release-gate regressions passed**.
  AppStream and desktop-file validation passed in the tooling image.
* **Flatpak x86_64 build, install and runtime smoke checks passed:** the final
  source compiled offline in GNOME 49 with Rust 1.98.1, 204 vendored Cargo
  packages and locked app/tool Pub dependencies. The exported bundle installed,
  reported `5.0.0-alpha.1`, opened a visible Xvfb window and imported a generated
  MAP01 PWAD through Dart/Rust; managed bytes and SQLite ID, map count and map
  name were verified. `flatpak-spawn --host /usr/bin/true` succeeded through the
  actual D-Bus bridge to the container host. All 204 Rust package notices were
  verified against their recorded hashes. Build, installation and runtime logs
  are retained as `flatpak-build-final.log`, `flatpak-install-final.log`,
  `flatpak-runtime-final.log` and `flatpak-host-bridge-final.log` under
  `/workspace/.cloud-setup/doomedlauncher/`. The installation retry ran in the
  required D-Bus session after an initial missing-session error.
* **Rust dependency advisory check passed:** `cargo-audit 0.22.2` reported
  **0 known vulnerabilities and 0 warnings across 204 lockfile entries**, using
  the RustSec database updated on 2026-10-09. The official audit-tool archive
  digest was verified. Declared dependency licenses, exact database/lock hashes
  and repeat instructions are recorded in
  [`docs/DEPENDENCIES.md`](docs/DEPENDENCIES.md); Dependabot schedules lockfile
  update proposals for later review.
* A live idgames latest/search smoke test reached the service through normal TLS
  and returned **HTTP 403 / Cloudflare challenge**. Error handling preserved a
  usable local library. This does not establish live remote search or download
  availability; evidence is retained in
  `/workspace/.cloud-setup/doomedlauncher/live-idgames.json`.
* A release-mode native FFI snapshot benchmark used existing synthetic WADs,
  one tag mapping and one image association per entry. For **5,000 entries**,
  three native snapshots took **0.801, 0.136 and 0.169 seconds**; the response was
  approximately 4 MB. This measures backend snapshot construction, not Flutter
  frame/scroll performance. Full results, including 1,000-entry samples, are in
  `/workspace/.cloud-setup/doomedlauncher/snapshot-performance-optimized.json`.

## Cross-platform and release status

Native build integration and CI are implemented for Linux x86_64/aarch64, Windows
x86_64 and macOS universal builds, with Rust-library bundling, version consistency,
artifacts and checksum/release gating. Windows installers and macOS packaging
exist in source, but their build/install/runtime results must come from those
operating systems. CI configuration is not evidence that a remote CI run passed.
Artifacts are unsigned unless a separate signing setup is provided.

Flatpak uses the Flutter/GTK runtime path and a typed host-process boundary.
Sibling-engine filesystem grants are limited to managed/required paths rather
than granting the whole host home directory. Fixture tests check grant planning,
symlink target access and preservation of argument boundaries. The earlier
`bwrap` namespace restriction was resolved in an isolated tooling container.
The final x86_64 package passed sandboxed build, installation, visible startup,
real import and a host-bridge command check. The artifact is
`dist/DoomedLauncher-5.0.0-alpha.1-linux-x86_64.flatpak`, with SHA-256
`1323b2b7e3ac5370035a7bffacfd337cd5d33aa0f5d3a80ee24fa2edf1774127`.
These checks do not establish physical portal, GPU or interactive host-engine
behavior.

Still required before a broadly verified release: native Windows/macOS build,
installation and interaction; Flatpak portals and interactive host engines; additional engines,
games, save/demo variants, audio and multiplayer; Wayland; physical multi-monitor/DPI,
keyboard, accessibility and system-theme checks; and large-library UI performance.
The application is a migrated prerelease, not an assertion that all platform QA
has already happened.

## Historical baseline evidence retained

The 4.0.3 baseline was C++/Qt 6.8+, QML/Quick Controls, KDE Kirigami and Frameworks
6.6+, built by CMake/Ninja. Its schema, behavior and expected controls informed
this migration. Historical sources can be inspected with, for example,
`git show f48510d8b9eea4051e09d42e6393517485b5bd06:src/database.cpp`.

Before migration, a retained Ubuntu 26.04 development image configured and built
the baseline using Qt 6.10.2 and KF6 6.24. All **8/8 baseline CTest targets passed**:
hostprocess, managedpath, externalurl, metadata consistency, command-line version,
library regressions, idgames fixtures and QML smoke. The saved installation script
repeated that result, and the old metadata checker returned `4.0.3`.

The baseline evidence established these limited results:

* Regression fixtures covered IWAD promotion and rename references, colliding
  imports, unassociated-demo rejection, online IDs versus local artwork, isolated
  archive extraction, source-port/default updates and missing additional files.
* QML smoke loaded pages/dialogs, preserved selection after sorting and exercised
  additional-file collection, edit cancellation and source-port fields.
* Local HTTP fixtures covered latest-search ordering, invalid JSON, unsafe paths,
  streamed isolated downloads and cleanup; they did not establish live uptime.
* Host-bridge fixtures covered argv, discovery, launch failures, capture and
  timeout; they did not render a game.
* The old binary imported a synthetic PWAD through its positional argument;
  SQLite recorded `audit.wad`, `MAP01`, MapCount=1 and passed integrity_check.
* Actual X11 virtual-display interaction opened the setup assistant and fallback
  Qt file picker; selecting a fixture created GameFiles/IWads records. It was not
  a portal session. The old wide layout clipped part of Recent and emitted QML
  placement warnings, without a fatal startup error.

Historical screenshots/logs remain outside the checkout under
`/workspace/.cloud-setup/doomedlauncher/`: `baseline-setup.png`,
`baseline-picker.png`, `baseline-picker-path.png`, `baseline-imported.png`,
`baseline-build.log` and `saved-install-validation2.log`.

Flutter 3.47.7/Dart 3.13.5 and Rust 1.99.0 were initially validated with an external
counter template, Rust diagnostic tests and a Dart-isolate/native-library probe.
Those earlier toolchain demonstrations are retained as setup evidence; current
migration claims are based on the application checks above.
