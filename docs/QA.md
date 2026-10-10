# Desktop QA record

Doom Launcher 5.0.0-alpha.1 was exercised on Linux on 2026-10-09 using its
packaged Flutter release executable and Rust shared library. Pointer and keyboard
input were sent through X11 on Xvfb, with software rendering. Screenshots below
are actual application captures at 1400×900 and 800×650; they are not mockups.

The launcher-interface test library contains synthetic WAD headers/map markers
and metadata. Its **QA fixture engine** is a short-lived shell process,
not a game renderer. No commercial game data is included. The visible fixture
names and generic artwork therefore do not establish compatibility with a
particular commercial game or source port.

| Actual desktop action | Observed result |
| --- | --- |
| Start the packaged executable against an isolated existing library | Library, IWAD, source port and tag records loaded through the real Rust bridge. |
| Select an entry, switch list/tiles, resize from 1200×800 to 1400×900 | Selection persisted; wide details showed metadata, rating and tags; both views rendered without Flutter layout exceptions. |
| Open Play with options, enable map selection, enter MAP01 and extra parameters, launch | Preview showed the native executable and argument list; the fixture process started and a running session appeared. Completion was reported in the session strip. |
| Open the running-session Stop action | Confirmation appeared; session status subsequently showed completion. The process naturally completed during inspection, so this is not evidence of forcible engine termination. Native and widget tests cover stop/shutdown behavior separately. |
| Open metadata editing, inspect title/author/description/comments/date/rating/tags, cancel | Full draft form rendered and closed; cancellation behavior is also asserted in widget and real-bridge integration tests. |
| Open source-port editing, change the fixture port name and save | Native SQLite query confirmed the new port name; executable and other configuration fields were retained. |
| Navigate Tags and Settings; change Dark to Light with keyboard selection | Tag controls rendered; the entire shell updated to Light and persisted the preference. |
| Resize to 800×650 | Navigation collapsed to labeled icons, details moved out of the side panel, tiles and controls remained usable. |
| Press Ctrl+O; use the native GTK picker, enter an absolute synthetic WAD path, choose Open | The app reported “Imported 1 file”; SQLite contained the new library row and the tile appeared. This is GTK fallback picker evidence, not Flatpak portal evidence. |
| Send the normal native window-close request after jobs completed | The application closed gracefully; SQLite retained Light theme and the final 800×650 geometry. |
| Close while a 120-second fixture process is running, then choose Cancel | The one-active-session confirmation appeared; cancelling restored search and a usable library while the session kept running. |
| Close again and choose Stop and quit | The launcher exited with status 0; the fixture shell and its sleeping child process were both gone. |

Five shareable captures:

- [Library list and details in dark mode](screenshots/library-list-dark.png)
- [Library tiles and details in dark mode](screenshots/library-tiles-dark.png)
- [Play options and native command preview](screenshots/play-options.png)
- [Settings in light mode](screenshots/settings-light.png)
- [Compact library in light mode](screenshots/library-compact-light.png)

The frontend regression suite `flutter test test/frontend_test.dart` passed **17/17 tests**;
the full Flutter suite passed **59/59**. `flutter analyze --no-pub` reported no issues.
It checks recent-search limits, tagged IWAD visibility, nullable legacy sorting,
chronological local/RFC3339 timestamps (also checked in America/Los_Angeles),
stable selection across sorting, clearing selection when search hides an entry,
metadata cancellation, additional-file ordering, complete launch options,
standalone Doom 64 options, compact navigation, unavailable-entry editing and
safe shutdown confirmation/failure, macOS menu callbacks and the guarded native
quit protocol, including blocking new work while closing and expired native
quit acknowledgments. The macOS cases use widget/platform-channel tests and do not
establish a compiled or running macOS bundle. Management and idgames controls have a
separate suite in `test/ui/management_test.dart`.

The real desktop integration test in `integration_test/library_flow_test.dart`
passed through the actual Rust core: import, metadata save/cancel, immediate
reopening of a row menu, tags and settings. It exposed a double-click gesture
conflict around row popup buttons; the recognizer was moved to selectable
content, and the integration test then passed. See `MIGRATION_AUDIT.md` for the broader native, bridge and packaging evidence.

One final release startup observation used a warm filesystem/tool cache, a
five-entry library and a 1440×1000 Xvfb display with software rendering. The window
became visible at **1.440 seconds**; rendered library content was observed at
**1.767 seconds**. After five idle seconds, resident memory was **283,336 KiB**
(about 276.7 MiB), with a **300,772 KiB** peak. These are single-run observations,
not cold-start or large-library performance claims. The raw observation is
`/workspace/.cloud-setup/doomedlauncher/screenshots/performance.json`.
The final active-session close evidence is retained beside it in
`final-close-qa.log`, `final-close-confirm.png` and `final-close-cancelled.png`.
GTK teardown emitted Flutter implicit-view/message-handler warnings while the
window closed successfully; no Dart exception or render overflow was observed.

## Real engine checks using the packaged native core

Chocolate Doom **3.1.1** and Crispy Doom **7.1.0** passed real engine checks on
2026-10-09 through the release bundle's Rust library. The test called the public
C ABI and drove the engine windows with X11 input under Xvfb in Ubuntu 26.04.
It used **Freedoom 0.13.0** from the distribution package and an original generated
one-room MAP01 with an exit switch. Audio was disabled. This run exercised actual
engine rendering and file formats separately from the interface fixture above.

| Engine action, performed for both ports | Observed result |
| --- | --- |
| Detect the installed engines, import the IWAD and generated PWAD, launch MAP01 | Actual gameplay rendered. |
| Save through F2, stop, launch with Load latest save and no map override | Each engine created `doomsav0.dsg` in its entry/port directory; the launch plan used numeric `-loadgame 0`, and saved gameplay loaded. |
| Complete the map while recording, finish the recording | A real `.lmp` file became a library association. The engine's documented successful recording exit status 255 was recognized while retaining the original code in session details. |
| Replay the associated recording | Gameplay replayed and the engine exited successfully with status 0. |
| Complete the map again without recording, quit through F10/Y | Normal exit status 0; each engine had exactly two level-statistics records. Reopening details retained the same counts. |
| Request Stop through the backend | The engine stopped and the session reached `stopped`. |
| Shut down after the Crispy statistics sessions | Managed `levelstat.txt` and the previous run's `levelstat-history` file remained available. |

Chocolate statistics came from `-statdump`; Crispy statistics came from
`-levelstat`. Sessions lasted seconds, so this run does not validate a real
one-minute increment of `MinutesPlayed`; the automated session tests cover that
accounting separately.

- [Crispy Doom gameplay](screenshots/crispy-doom-gameplay.png)
- [Chocolate Doom map completion](screenshots/chocolate-doom-intermission.png)

These captures display [Freedoom](https://freedoom.github.io/) assets and the
generated test map, with no commercial game data. Full cloud evidence is retained
under `/workspace/.cloud-setup/doomedlauncher/real-engine-qa/run-478fd412/`,
including `report.json`, saves, demos, session logs and additional captures.
The tested native library SHA-256 is
`118e85170ef0d07caca5eeb70462e0248603b671794c961cf8dbc9d61c3dc8cc`.
See [engine compatibility](ENGINE_COMPATIBILITY.md) for the supported argument
contracts and the narrower source/test evidence for other engines.

## Flatpak build and runtime checks

The final x86_64 Flatpak compiled offline inside the GNOME 49 SDK, using Rust
1.98.1 and locked/vendored dependencies. The exported bundle installed in an
isolated Linux tooling environment, reported version `5.0.0-alpha.1` and opened
a visible Xvfb window. Its positional file-import command imported a generated
MAP01 PWAD through the actual Dart/Rust bridge; the managed copy and SQLite
GameFileID, MapCount and Map values were verified.

`flatpak-spawn --host /usr/bin/true` succeeded through the real D-Bus bridge to
the container host. This verifies the bridge command path; interactive host-game
windows and physical desktop portals remain untested. The Rust dependency
notice inventory contained all 204 packages with matching recorded hashes.

The artifact is `dist/DoomedLauncher-5.0.0-alpha.1-linux-x86_64.flatpak`, with
SHA-256 `1323b2b7e3ac5370035a7bffacfd337cd5d33aa0f5d3a80ee24fa2edf1774127`.
Build, installation, runtime and host-bridge evidence is retained under
`/workspace/.cloud-setup/doomedlauncher/` in `flatpak-build-final.log`,
`flatpak-install-final.log`, `flatpak-runtime-final.log` and
`flatpak-host-bridge-final.log`. The initial installation attempt lacked a D-Bus
session; installation and runtime checks passed after using that required session.

Remaining physical validation includes Windows and macOS runners/installers,
Finder/Open With handoff, hardware-accelerated rendering, Wayland, desktop
screen-reader behavior, Flatpak portal dialogs and interactive host-game launches,
additional source ports, audio, multiplayer and other game/save/demo formats. The cloud X11
session reported missing AT-SPI service/DRI3 support and a transient initial
OpenGL resize warning; the app continued rendering. Live idgames availability
requires its own successful service checks.
These constraints are not described as passed platform QA.
