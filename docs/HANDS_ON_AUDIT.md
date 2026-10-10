# Hands-on desktop audit

Baseline: `8784e53` (`5.0.0-alpha.1`). This is a new audit requested after
the migration PR; earlier migration checks are context, not fresh results.
Audit date: 2026-10-10. Fixes are on `codex/desktop-hands-on-audit`, based on
development merge `0543ebf`. The final native build uses this PR’s source and
retains version `5.0.0-alpha.1`; no public release was published.

## Method and environment

Linux x86_64, an isolated Ubuntu 26.04 desktop container, GTK 3, Flutter
3.47.7 / Dart 3.13.5 and Rust 1.99.0. Xvfb provides an actual X11 display;
software rendering replaces a physical GPU. Use a fresh disposable library.
Exercise the release executable with native X11 mouse/keyboard input and
repeatable integration tests running on the real GTK Flutter runner and Rust
core. Native pickers are separate GTK windows. Never infer a pass from code.

Evidence and logs: `/workspace/.cloud-setup/doomedlauncher/hands-on-audit/`.
The inventory distinguishes exercised controls from platform behavior blocked
by the available environment. Screenshot review supplements control activation.

## Verified run/build structure

`lib/main.dart` initializes the native backend and desktop window lifecycle;
`lib/app.dart` supplies menus, navigation, imports and sessions. Dart widgets
call `AppController` and the asynchronous native bridge. `rust/src/api.rs`
dispatches storage, content, discovery, download and engine operations; the
SQLite database and managed files live beneath the configured data directory.
The audit always set `DOOMEDLAUNCHER_DATA_DIR` to disposable fixture storage.

Flutter's Linux CMake build includes the Rust shared library and GTK plugins.
The verified commands are in [BUILDING.md](../BUILDING.md): Flutter analysis,
unit tests, real desktop integration, Cargo tests/clippy/format and Python
packaging checks. `scripts/package.py` builds the host release and packages the
Linux portable tarball, macOS application/archive or Windows executable/archive
and installer. Flatpak has a separate build workflow. Existing release artifacts
from the migration are not evidence that the later UI changes were rebuilt.

## UI inventory and execution checklist

| ID | Controls and states to exercise | Status / evidence |
| --- | --- | --- |
| SHELL-01 | Fresh launch, setup banner, empty library; add port / base game / find installed games | Fresh launch, empty state and all three setup actions passed. |
| SHELL-02 | Navigation rail: Library, Get mods, Source ports, Tags, Settings; rapid/repeated switching | All five pages and twelve repeated transitions passed. |
| FILE-01 | File menu: Import files; native chooser select/multiple/cancel/filter/invalid path | Native WAD/Unicode/ZIP, cancel, invalid path, duplicate and corrupt import passed; final release multi-select imported two read-only WADs, with All files filtering and unsupported-file recovery. |
| FILE-02 | File menu: Import base games; IWAD classification; cancel | Native Freedoom import and IWAD classification passed; cancellation passed. |
| FILE-03 | File menu: Import directory; recursive files; repeated import; Ctrl+Shift+O | Native directory and recursive folder drop imported actual files; repeated-import handling passed. |
| FILE-04 | File menu: Scan Steam/GOG/Heroic/Lutris; About | About and all scan controls passed. |
| MENU-01 | Library menu: Play selected, Play with options, Edit selected, Delete selected, Refresh; no selection | All five commands activated; selected/no-selection states passed after BUG-002. |
| MENU-02 | View menu: each of the five pages | All five View menu entries passed, including Get mods in the HTTP regression. |
| MENU-03 | Help menu: About, View licenses/license detail/back, Close; Keyboard shortcuts/Close | Both About routes, actual bundled license list/detail/back, Close and shortcuts passed. |
| LIB-01 | Toolbar import files/directory, search typing/clear/Ctrl+F; empty/no matches; Unicode/long input | Import menus, search/clear, Ctrl+F, empty/no match and Unicode passed. |
| LIB-02 | All mods/Recent/Untagged/IWADs/tag tabs; remembered tab and deleted/hidden tab fallback | All built-in/tag tabs, assignment and deleted/hidden-tab fallback passed. |
| LIB-03 | Every sort field, ascending/descending; list/tiles; selection; details toggle | All seven sort fields, both directions and both layouts passed; detail toggle included in expanded test. |
| LIB-04 | Row/tile selection, double click, action button, right-click menu; focus/arrow/Enter/Delete behavior | Row/tile/action selection, double-click, right-click and Ctrl+E/P focus regression passed. |
| CTX-01 | Play; Play with options; Details & statistics; Edit metadata | All four actions activated successfully. |
| CTX-02 | Rename file (empty/invalid/duplicate/Unicode/cancel/save); Resync metadata; promote to IWAD | Invalid/collision/Unicode rename recovery passed; resync and IWAD promotion passed. |
| CTX-03 | Open text file; Open externally; Show in folder; Copy path and verify clipboard | Copy path, actual Alpha.txt/session-log opening in Mousepad and Show in folder passed; missing WAD handler exposed BUG-013 and its real GTK retest passed. |
| CTX-04 | Delete cancel; preserve file; delete managed file; recovery; unavailable-file state | Cancel, preserve managed file, delete managed file and disabled missing-file actions passed. |
| EDIT-01 | Title, Author, Description, Comments, Release date; every rating; tags; Save/Cancel/Escape/reopen | All five fields, six ratings, tag assignments, Unicode/multiline/punctuation and Save/Cancel/Escape passed. |
| DETAIL-01 | Detail panel/dialog close, Play, Edit, Open; artwork, metadata, text members, statistics | Dialog close, nested Edit, metadata refresh, native detail Play and actual map statistics passed; external Open exposed BUG-013, reproduced and fixed below. |
| DETAIL-02 | Import screenshot/save/demo; open image/file; replay demo; remove cancel/confirm; failed association | Native multi-selection classified screenshot/save/demo; image opened in ImageMagick; missing save/demo handlers reported errors; removal, actual save/load and recording/replay passed. |
| PLAY-01 | Source port/IWAD dropdowns; missing prerequisites; utility/Doom64 behavior | Missing-port validation, real Chocolate/Freedoom launch, utility success/failure and Doom64 selector variation passed; no Doom64 binary available. |
| PLAY-02 | Start-map checkbox, typed map and map menu, every skill; invalid/long map | Map toggle/menu MAP07 and all five skills passed; real MAP01 launched. |
| PLAY-03 | Extra parameters, extra-only, latest save, statistics, demo, remember toggles; invalid quoting | All five flags toggled, extra arguments and invalid quoting passed; remembered options reloaded. |
| PLAY-04 | Additional file picker/add/up/down/remove; order, duplicates; command preview; Launch/Cancel/Escape | Add/reorder/remove, preview and launch argv passed; Cancel/Escape passed. |
| SESSION-01 | Real engine launch, running status, session log, Stop cancel/confirm; failed engine | Real Chocolate gameplay/record/replay, external session log, immediate Stop/cancel/confirm and failed-process error display passed. |
| SESSION-02 | Window close while running, cancel, stop-and-quit; relaunch; no orphan child | Native close/cancel and Stop and quit passed with real replay; no launcher/engine remained. |
| PORT-01 | Add, detect installed, card click, edit icon, remove cancel/confirm; empty state | Add/edit/remove cancel/confirm and detection of Chocolate and Crispy passed. |
| PORT-02 | Name, executable/picker, all launch types, working directory/picker, extensions, file parameter, extra parameters, save directory/picker | Every field, all three native pickers (select/cancel), launch types and defaults passed. |
| PORT-03 | Empty/space/Unicode/long/invalid inputs; Save/Cancel/Escape/reopen; missing executable | Empty/Unicode/long content, Cancel/Escape and missing-engine validation passed. |
| TAG-01 | New tag, name field, shown-as-tab toggle, Save/Cancel; empty/duplicate/Unicode/long names | Empty/spaces/duplicate/Unicode/long, tab toggle and Save/Cancel passed. |
| TAG-02 | Tag card, rename, inline tab switch, delete cancel/confirm; assignments and persistence | Assignments, rename/cancel, inline tab switch and delete cancel/confirm passed. |
| STORE-01 | Automatic scan, choose-folder/cancel, root removal, scan again, select all/individual, import/close; empty/error states | Scan/scan-again, native choose/cancel, root removal, both selection modes, import/close and minimum size passed. |
| STORE-02 | Doom64 result toggle and registration | Doom64 result toggle and actual source-port registration passed. |
| SET-01 | Follow system/Light/Dark; List/Tiles; play-dialog switch; title-picture switch | All themes, both layouts and both switches passed and persisted. |
| SET-02 | Recent/Untagged/IWAD/idgames visibility chips; default port/IWAD options | All four visibility chips toggled both ways; automatic and selected port/IWAD defaults passed. |
| SET-03 | Mirror/website/API path edit, empty/invalid URL/path, Save, dirty fields during refresh; Advanced expand/collapse | All network fields, invalid URL/path, corrected save, dirty fields during refresh and Advanced passed. |
| NET-01 | All search types; query/Enter/Search/Latest; empty, Unicode, error and fixture results | All four search types verified against real HTTP requests; empty/Unicode/Latest and HTTP503 recovery passed. |
| NET-02 | Result details/Close/Website/Download; row download/import/play/website | Details/Close/Download, row download/import/play, Website no-handler error and both native Website actions opening NetSurf passed. |
| NET-03 | Progress/cancel/retry/import/retry import/play; failed download; duplicate; navigation during download | 503/cancel/retry/import/play, navigation state, corrupt retry-import/discard and duplicate play passed. |
| INPUT-01 | Tab/Shift+Tab/Enter/Escape/arrows/Space/Delete/Backspace; focus visibility/order/trapping; shortcuts from fields/dialogs | Ctrl+O/F/E/P, F5, Escape, native text entry and Tab/Shift+Tab/Space/Backspace/Delete passed; native dropdown arrows/Enter checked below. |
| DND-01 | Native file/folder drop; multiple files; invalid file; drop blocked during modal/shutdown | Native file/folder drag, overlay and mixed multi-file drop passed (two WADs imported, unsupported file reported); modal blocked import without changing database. |
| GEOM-01 | Minimum 760×560, medium, large, maximized/restored, wide/short/tall; each page/dialog/popover; scroll accessibility | Four sizes across local pages, populated details, store/network and long management/Play/Delete dialogs; maximize/restore passed. |
| PERSIST-01 | Settings, data, tags, launch settings, view/tab, window size; restart; intentionally transient search/selection/sort | SQLite settings/tag/port and launch parameters passed; native release restarts retained Dark theme, Tiles, Recent tab and 1000×700 size; search/selection reset. |
| ERROR-01 | Corrupt/missing/read-only files, import failure dialog, error banner dismiss/retry, escaped user text; logs | Corrupt WAD/database, read-only directory/recovery, duplicate import, missing text/file menus, invalid rename, HTTP503 and invalid quoting passed. |
| PERF-01 | Startup/render time, repeated navigation/dialogs, large list/scroll/search, CPU/memory observations | 250-entry render/search/scroll, four sizes, maximize/restore and repeated navigation passed; native release window visible in 0.56–0.58 seconds (warm fixture library). |
| FINAL-01 | Full repeat walkthrough after fixes, checks, final release launch/smoke | PASS: all ten desktop scenarios passed together after the final fixes; rebuilt release native smoke, persistence, errors and engine/browser handoff passed. |

macOS application/Edit/Window menus, Finder events, Command shortcuts, Retina,
Windows shell/installer behavior, physical GPU/Wayland/portals and assistive
technology require their native environment and are not reachable on X11 Linux.

## Defects found and corrected

### BUG-001 — Empty library clips its primary action at minimum size

- Area/severity: Library / medium.
- Reproduction: launch with a fresh library; resize the native window to
  760×560. The first-game banner remains visible. Inspect the empty state.
- Expected: explanatory text and the Import files action remain reachable.
- Actual: the text runs off the bottom and the empty-state Import files button
  is outside the viewport, with no scrolling. Screenshot: `02-minimum-empty.png`.
- Cause: an intrinsically tall centered Column has no scrolling inside the
  remaining library viewport.
- Files: `lib/ui/library_page.dart`.
- Fix: make the empty state scroll within its available viewport.
- Retest: real GTK pass 2 passed Library at 760×560, 1800×560, 760×1100
  and 1200×900; source-port, tag and settings empty pages also passed those
  sizes. In the final rebuilt release, scrolling exposed Import files at
  760×560; clicking it opened the native picker and cancellation returned normally.

### BUG-002 — Library commands appear enabled without a selection

- Area/severity: Library menu / medium.
- Reproduction: fresh empty library; open Library and click Play selected,
  Play with options, Edit selected and Delete selected individually.
- Expected: actions requiring an entry are visibly disabled.
- Actual: all four are enabled but every click silently does nothing.
  Screenshot: `03-library-menu-empty.png`.
- Cause: non-macOS menu callbacks are always installed, unlike the toolbar.
- Files: `lib/app.dart`.
- Fix: recompute selection-dependent menu availability when opening the menu.
- Retest: real GTK pass 2 confirms the four commands are disabled with no
  selection. The full rerun also passed each selected-entry menu command.

### BUG-003 — Filename search always fails before reaching idgames

- Area/severity: Get mods / high (one entire advertised search mode broken).
- Reproduction: select Filename, enter a nonempty query, click Search. The real
  UI + Rust + local HTTP test reproduces it without depending on the live API.
- Expected: search requests use idgames' `type=filename` parameter.
- Actual: “Search failed: Unsupported archive search field”; no HTTP request.
- Cause: the Flutter dropdown sent `name`; Rust correctly accepts `filename`.
- Fix: corrected the dropdown value in `lib/ui/idgames.dart`.
- Retest: all four search types passed with exact request parameters received
  by the local HTTP service; download failure, retry, cancellation and import passed.

### BUG-004 — Rename failure dismisses the user's draft

- Area/severity: Library > Rename / medium.
- Reproduction: rename an imported entry to `../escape.wad`; press Rename.
- Expected: reject the name and keep the dialog open so it can be corrected.
- Actual: the dialog closes; the error appears behind it as a page banner.
- Cause: the dialog returned its value before the asynchronous rename ran.
- Fix: `lib/ui/library_page.dart` now awaits the result inside the dialog,
  prevents duplicate submission, shows an inline error and closes on success.
- Retest: invalid traversal and existing filename retained the dialog; corrected
  Unicode filename succeeded. Cancellation and adjacent metadata/deletion passed.

### BUG-005 — Launch notification blocks the running game's controls

- Area/severity: Session bar / medium.
- Reproduction: launch a game and immediately click Stop game while the
  “Started…” notification is showing.
- Expected: the notification leaves the session log and Stop controls usable.
- Actual: the notification overlays the session bar and intercepts the click.
  Real GTK hit testing identifies the snackbar paragraph above the Stop button;
  the stop confirmation never opens. `play-audit.log` records the failed click.
- Cause: the session bar was the final child of Scaffold's body, so snackbar
  placement did not reserve its height.
- Fix: `lib/app.dart` exposes the session bar as Scaffold's bottom navigation
  area, which the snackbar layout already avoids.
- Retest: immediate Stop opened despite the launch notification; Cancel retained
  the process, confirmed Stop terminated it, and launch settings were restored.

### BUG-006 — Management headings have inconsistent left margins

- Area/severity: Source ports and Tags / low (visual alignment).
- Reproduction: open Source ports at 1200×900, then compare its heading and
  actions with Library, Settings and Get mods.
- Expected: page headings/actions align with the common content margin.
- Actual: Source ports and Tags center a narrow heading block inside the page;
  list cards start much farther left. `native-source-ports.png` shows the gap.
- Cause: a shrink-wrapped PageIntro was centered by its parent Column.
- Fix: `lib/ui/management/helpers.dart` gives the common intro full available
  width while retaining its standard padding.
- Retest: the full GTK rerun passed all management sizes. The rebuilt native
  release was visually checked at 1200×900 and 760×560; headings align with cards.

### BUG-007 — Search retains keyboard focus after choosing an entry

- Area/severity: Library keyboard navigation / medium.
- Reproduction: Ctrl+F, type a matching title, click its row, press Ctrl+P
  or Ctrl+E. Compare with clicking the Options/Edit buttons.
- Expected: keyboard commands act on the selected entry.
- Actual: search retains focus and consumes editing shortcuts; no dialog opens.
- Cause: selecting a row updated selection without moving text-field focus.
- Fix: `lib/ui/library_page.dart` gives entries their own focus node and requests
  it for list, tile and context-menu selection.
- Retest: real GTK verifies search loses focus and both shortcuts open their
  corresponding dialogs; all four selected-entry Library commands also open.
  Native release Ctrl+P and Ctrl+E also opened the correct dialogs after search and row selection.

### BUG-008 — Long entry titles overflow the Play dialog

- Area/severity: Play / medium.
- Reproduction: import an entry with a roughly 1,000-character Unicode title;
  resize to 760×560 and open Play with options.
- Expected: heading and actions fit; options can scroll.
- Actual: the title consumes the dialog height, producing a 416-pixel vertical
  RenderFlex overflow (`expanded-audit.log`).
- Cause: an unbounded wrapping heading outside the scrollable content.
- Fix: `lib/ui/play_dialog.dart` limits the heading to two lines with ellipsis.
  Full metadata remains available in entry details and editing.
- Retest: `long-picker-retest.log` passes the long-title minimum-size dialog,
  long tag/source-port dialogs, and repeated Escape/cancel without saving.


### BUG-009 — Library Play commands remain enabled for missing files

- Area/severity: Library menu / medium.
- Reproduction: import and select a WAD, remove its managed file externally,
  refresh, then open the row menu and the Library menu.
- Expected: both menus disable commands that require the missing file.
- Actual: the row menu disables Play, but Library Play commands remain enabled
  and their guarded callbacks silently return. The real GTK test in
  `expanded-pass5.log` confirms enabled callbacks for the unavailable selection.
- Cause: the top menu checked selection existence but omitted `pathError`.
- Fix: `lib/app.dart` uses a shared availability getter for both Play commands
  in the Linux/Windows menu and the macOS platform menu.
- Retest: real GTK confirms both top-level Play commands are disabled for the
  missing file, matching the row menu. macOS native rendering requires a macOS
  host; its widget/menu coverage is separate.

### BUG-010 — Failed startup cannot close its window

- Area/severity: Desktop lifecycle / high.
- Reproduction: use a deliberately corrupt SQLite library, launch, then press
  Alt+F4 or use the window close button. Retry the close request.
- Expected: an application that never initialized can close without repairing
  the database first.
- Actual: the window remains open and repeats the database error.
  `native-corrupt-close.png` captures the still-running window.
- Cause: `DesktopLifecycle.requestQuit` skipped active-work shutdown for an empty
  snapshot but still tried to persist geometry into the unusable database.
- Fix: `lib/main.dart` only saves geometry after successful initialization and
  the existing shutdown checks. Active-work protection is unchanged.
- Retest: the native-backend regression opens a real corrupt SQLite file,
  confirms startup failure, approves quit without geometry writes and verifies
  the corrupt file was not altered. The rebuilt release displayed both corrupt
  and read-only startup errors, then closed through Alt+F4 with exit code 0.

### BUG-011 — Long idgames titles overflow the details dialog

- Area/severity: Get mods > Details / medium.
- Reproduction: serve a valid result with a long Unicode title, resize to
  760×560 and click the result.
- Expected: every detail and action remains reachable through scrolling.
- Actual: a 928-pixel vertical overflow; screenshot `network-title-before.png`.
- Cause: only the body scrolled while the unrestricted heading consumed height.
- Fix: `lib/ui/idgames.dart` makes the dialog heading and body scroll together,
  preserving the complete title and keeping the action row reachable.
- Retest: full-title details and scrolling to its author/body passed at minimum
  size, together with search, download, retry-import, discard and duplicate play.

### BUG-012 — Long titles hide the delete-file checkbox

- Area/severity: Library > Delete / medium.
- Reproduction: select the long-title fixture, resize to 760×560, choose
  Library > Delete selected.
- Expected: the complete confirmation and optional file-deletion checkbox stay
  reachable; Cancel remains available.
- Actual: the content Column overflows by 182 pixels and pushes the checkbox
  outside its viewport. Screenshot: `delete-title-before.png`.
- Cause: the confirmation body has no scroll container.
- Fix: `lib/ui/library_page.dart` uses AlertDialog's scrollable content layout.
- Retest: minimum-size confirmation scrolled to the checkbox, toggled it twice
  and cancelled without deletion. Adjacent rename and both deletion modes passed.

### BUG-013 — External Open silently succeeds without a handler

- Area/severity: external file/website actions / medium.
- Reproduction: on the native Linux desktop with no WAD handler, select Alpha,
  click Open file externally or the details Open icon. No program or error opens.
- Expected: open the registered program, or explain that no program handles it.
- Actual: `launchUrl` returns true, but nothing opens. A real GTK probe confirmed
  the direct call returns true and the UI contains no notification. GIO reports
  “No application is registered as handling this file”; `gtk_show_uri_on_window`
  with a parent window acknowledges the same request.
- Cause: the application trusted GTK's launch acknowledgment as proof of handling.
- Files: `lib/platform/external_links.dart`, `lib/ui/widgets/common.dart`,
  `lib/ui/idgames.dart`.
- Fix: native Linux checks for an available handler before submitting the request.
  Flatpak continues through its host portal; other platforms retain their native
  launch behavior. File and website actions give a readable failure message.
- Retest: the real GTK probe now finds the visible missing-handler message while
  the unguarded plugin call still returns true. Four unit/widget regressions cover
  missing file/browser handlers, successful encoded-URI dispatch and other desktop
  behavior. The full GTK rerun also visibly reports a missing browser at 760×560.
  The final rebuilt release shows the missing-handler message from both the
  details Open icon and row action; Alpha.txt still opens correctly in Mousepad.

## Native walkthrough observations

- Chocolate Doom 3.1.1 launched from the Play dialog with Freedoom 0.13.0 and
  the generated playable MAP01. The engine recorded a real demo; after its
  recording-complete dialog was acknowledged, the launcher showed completion,
  three minutes of play time and a replayable association. The association's
  Replay button opened a correct `-playdemo` command and launched the engine.
- The session log opened in Mousepad; Show in folder opened the managed game
  directory in Thunar. A missing text association produced a readable banner;
  Dismiss cleared it. The first browser check had no registered handler, exposing BUG-013.
  NetSurf 3.11 was subsequently added from official Ubuntu packages in the
  disposable container; both Website buttons launched and rendered a local
  audit page. Its unpacking preserved the mounted read-only CA bundle.
- Closing during the real replay displayed one active session. Cancel left the
  launcher searchable. Closing again and choosing Stop and quit terminated both
  launcher and engine; process inspection found neither remaining.
- A deliberately corrupted SQLite file showed `file is not a database` with
  Retry startup and Dismiss. Both controls were activated. Scanning stores in
  that state showed a recoverable inline database error.
- A fresh read-only storage directory showed `Permission denied (os error 13)`.
  Restoring directory permissions and clicking Retry startup loaded the normal
  setup screen. Expected permission failures were logged to stderr because the
  local diagnostic log was also unwritable.
- The 250-entry fixture scenario took 18,509 ms to create/import all fixture
  files and display the first library frame. This is not a startup/render-only
  benchmark. Scrolling both layouts, filtering the final entry, four detail
  dialog sizes, maximize/restore and twelve page transitions passed.

GTK's native directory chooser can leave a typed location unaccepted while in
Recent view. Navigating Home before entering the path succeeded on the same
chooser. The native test now performs that real navigation. Tests wait for
asynchronous license loading and distinguish transient download animation from
settled UI. Harness failures are retained in raw logs and are not counted as
application defects.

Final release native checks additionally verified dropdown Down/Enter selection,
mixed multi-file imports from read-only sources, actual Chocolate Doom save/load,
statistics collected after map completion, and close/cancel/stop-and-quit with no
remaining engine process. The saved game was created through the engine’s F2 UI;
Load latest save produced `-loadgame 0` and reopened actual gameplay. The engine’s
ENDOOM screen required a final key before the launcher reported completion.

A five-second idle sample after the native walkthrough used 337,220 KiB RSS at
both endpoints and 0.0% of one CPU core at the timer's resolution. This short
sample is not a memory-leak or long-duration performance certification. Native
window-visible timings measure warm launches with the disposable small library;
they are not cold-start guarantees. No unhandled Flutter exceptions or layout
overflows remain in the successful full regression logs. The disposable desktop
also logged “Glycin running without sandbox” while its external GTK application
loaded image resources; this concerns that container's image-loader sandbox,
not the launcher’s Dart/Rust import or engine process.

## Test-environment and harness notes

- The minimal container lacked the AT-SPI bus and CJK/emoji fonts. Installed
  `at-spi2-core`, `fonts-noto-cjk` and `fonts-noto-color-emoji`; subsequent launches
  activate the accessibility bus. This is not screen-reader certification.
- A first persistence assertion used synthetic text entry before native focus
  settled, leaving the old invalid text visible. Direct X11 paste + Save stored
  the custom URL in SQLite. The harness now settles focus before typing and
  asserts the visible field value; this was not classified as an app defect.

Two late harness assertions read native state too soon: Cancel was present but
not yet enabled during job allocation, and Stop returns before the owned child
exits. The tests now wait for an enabled Cancel control and then for the actual
cancelled/stopped state, with a ten-second bound. They still fail if completion
does not arrive; they do not substitute backend calls for UI interaction.

## Repeatable checks added

`integration_test/desktop_audit_test.dart` runs nine scenarios against the actual
GTK runner, Rust library, disposable filesystem and loopback HTTP server. It
covers empty/menu states, management/settings persistence, library editing and
renaming, all idgames search modes and download transitions, real child-process
launch/stop/argv, long content, native pickers/associations/store scans, tag and
default/missing-resource behavior, and keyboard/large-library geometry.
`test/native_backend_test.dart` adds a real corrupt-database quit regression.
`test/external_links_test.dart` adds four external-handler regressions, including
visible UI feedback when the native plugin would falsely report success.

`scripts/test_desktop.sh` starts Xvfb/Openbox as needed and runs each desktop
suite with its own Flutter tool and D-Bus session. A single Flutter invocation
for both test files failed to start the second GTK bundle in this SDK; separate
lifetimes avoid that harness-level application-registration race. CI installs
the required desktop tools and uses the same script. Flutter's test binding
intentionally omits licenses, so the audit registers the real bundled
`NOTICES.Z` contents before drilling into the actual license-list UI.

The fixture engine in the repeatable launch test is a child shell process; it
validates ownership, arguments and stop behavior. The separate native Chocolate
Doom walkthrough establishes actual game rendering, recording and replay.

## Verification record

- `full-final-integration.log`: nine audit scenarios plus the existing library
  flow scenario passed through `scripts/test_desktop.sh` (10 desktop scenarios).
- `utility-retest.log`: the tag/default/missing-file scenario passed again with
  added utility success/failure launches and visible failure-message assertion.
- `unit-tests-final.log`: 64 Flutter unit/native-bridge tests passed, including
  the new corrupt-startup quit regression.
- `core-tests.log`: 109 Rust tests passed (72 unit, 34 library, 3 load-order).
- `package-tests.log`: five Python packaging checks passed.
- `release-final-build.log`: the final Linux release rebuild passed. Native
  engine launch/stop, missing-handler feedback, positive text opening, fresh
  minimum-size import, and corrupt/read-only startup closing passed again.
- `analyze-final.log`, `clippy.log`: Flutter analysis and Rust Clippy with
  warnings denied passed. Dart/Cargo formatting, release metadata validation
  and `git diff --check` passed.

## Selected visual evidence

- Empty library: [before](screenshots/audit/empty-before.png),
  [after](screenshots/audit/empty-after.png).
- Management heading alignment: [before](screenshots/audit/management-before.png),
  [after](screenshots/audit/management-after.png).
- [Long Play title after correction](screenshots/audit/long-play-after.png).
- Long network details: [before](screenshots/audit/network-title-before.png),
  [after, scrolled to metadata](screenshots/audit/network-title-after.png).
- Long delete confirmation: [before](screenshots/audit/delete-title-before.png),
  [after, checkbox reachable](screenshots/audit/delete-title-after.png).
- [Store scan at minimum size](screenshots/audit/store-scan.png).
- [Actual Chocolate Doom gameplay](screenshots/audit/real-engine.png).
- [Read-only startup error](screenshots/audit/read-only-error.png) and
  [successful Retry after restoring permissions](screenshots/audit/permission-recovered.png).
- [Corrupt startup still open after close, before correction](screenshots/audit/corrupt-close-before.png).
- [Final release with actual engine and unobstructed session controls](screenshots/audit/final-engine.png).
- [Native keyboard shortcut after search selection](screenshots/audit/keyboard-after.png).
- [Mixed import result](screenshots/audit/mixed-import.png).
- [Actual collected map statistics](screenshots/audit/statistics.png).
- [Imported screenshot, actual save and actual demo](screenshots/audit/associations.png).
- [Dark theme, tile layout and Recent tab restored after restart](screenshots/audit/persistence.png).
- [Missing browser feedback after correction](screenshots/audit/website-handler-after.png).

- Missing file handler: [before](screenshots/audit/no-handler-before.png),
  [after](screenshots/audit/no-handler-after.png).

- [Both native Website actions rendered the local page in NetSurf](screenshots/audit/browser-handoff.png).
- [Screenshot association opened in ImageMagick](screenshots/audit/image-handler.png).

## Remaining verification limits

The local integration tests use a real loopback HTTP server to exercise response
and failure paths deterministically. They do not certify availability or content
of the public idgames service. A direct request to the public latest-files API
returned HTTP 403 with Cloudflare’s “Just a moment...” challenge in this
environment; live public archive browsing is therefore externally blocked.
Missing-browser feedback and successful NetSurf handoff/rendering were both
exercised using the local HTTP fixture. Doom64 registration and launch-type
controls were exercised, but actual Doom64 gameplay requires a user-supplied
engine and data.

GitHub Actions on the merged migration could not start. GitHub's check annotation
states: **“The job was not started because your account is locked due to a billing
issue.”** This is an external account blocker, not a successful CI result.

macOS/Windows native behavior, Linux ARM64, physical GPU/Wayland, live portal
behavior and assistive-technology operation require their matching environments.
The old Flatpak artifact belongs to the migration baseline; this audit rebuilds
the Linux native release and does not claim a new Flatpak/platform sign-off.

## Final result

**PASS for the exercised Linux X11 walkthrough and final native release smoke.**
The complete 46-row inventory was revisited through real desktop interaction and
the full regression runner. All 13 reproduced application defects were corrected
and retested; 64 Flutter, 109 Rust, 10 desktop and 5 packaging tests pass. The
remaining external service, hosted CI and platform verification limits are listed
above. No release was published and no untested platform is marked as passed.
