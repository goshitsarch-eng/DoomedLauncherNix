# DoomedLauncherNix release notes

This is a fork of [Doom Launcher](https://github.com/nstlaurent/DoomLauncher) by
[Hobomaster22](https://github.com/nstlaurent). The notes below cover only the Linux/Flatpak work
added by this fork; upstream's own release notes follow further down, unchanged.

Fork versions are numbered separately from upstream releases.

## 4.0.3

- Add, remove and reorder additional mods from the Play dialog; retain their
  remembered load order. Report missing mods and unsupported archive contents.
- Keep library selection and details attached to the correct entry after sorting,
  searching and deleting. Offer details on narrow windows and handle removed tabs.
- Apply edited tags only when Save is pressed. Decode executable picker URLs so
  paths containing spaces and non-ASCII characters work.
- Wire imported demo playback into Details using the remembered launch settings.
- Allow Add IWADs to promote an existing mod; repair default selections after
  deletion and update remembered mod lists and IWAD filenames after renaming.
- Isolate extracted archive members so identically named WADs cannot overwrite
  one another. Keep online archive IDs separate from local artwork and IWAD IDs.
- Cancel stale idgames searches, report malformed/API error responses, stream
  downloads into isolated temporary folders and reject invalid download paths.
  Serialize downloads and clear pending autoplay after failures.
- Reflect source-port detection state across pages and refresh setup after manual
  source-port changes. Prevent mirror country labels being saved as URLs.
- Build and test on native x64 and ARM64 GitHub runners. Publish binary tar.gz
  archives, Flatpak bundles and SHA-256 checksums automatically for each new
  version pushed to development (or a matching v-prefixed tag).

The tar.gz packages contain the executable and desktop integration files. They
require a compatible host Qt/KDE installation (the binaries are built with the
KDE 6.10 SDK); use Flatpak when those runtime libraries are not installed.

## 4.0.2

- Restore Flatpak host detection and launch for native/AppImage, Flatpak and
  Snap source ports with typed argv and host-context working directories.
- Reuse the existing host-spawn permission; no shell strings or new permissions.
- Reject invalid engine IDs, report host-command failures, and clean up failed
  starts and timed-out probes. Add Qt regression coverage for the host boundary.

## 4.0.1

### Complete rewrite in Qt 6 / Kirigami

- The application is now a native C++ Qt 6 application using KDE's Kirigami
  framework, styled like a modern KDE app. The GTK 4 / libadwaita frontend
  and the entire .NET stack are gone.
- Light and dark mode: follows the system color scheme by default, or force
  Breeze Light / Breeze Dark from Settings.
- Existing libraries keep working: the same SQLite database and
  `GameFiles/` layout under `$XDG_DATA_HOME/doomlauncher` is used, so
  entries, tags, source ports and play history carry over.
- File dialogs go through the KDE/XDG portal, so network share locations
  mounted via KIO open directly instead of asking for another app.
- Library (list/tile views, search, sort, tags-as-tabs), play dialog with
  live command preview, source port detection (native/AppImage/Flatpak/
  snap), idgames search + download, play time tracking, setup assistant.
- Flatpak packaging moved to the org.kde.Platform runtime.
- Title screens (TITLEPIC/TITLE, including MAPINFO titlepage overrides and
  embedded PNGs) are extracted as library art, using the wad's own PLAYPAL
  or the built-in Doom/Heretic/Hexen palettes.
- Per-level statistics are recorded again: levelstat (dsda-doom, PrBoom+,
  Crispy, Woof! and friends), statdump (Chocolate Doom family) and
  ZDoom-family save games, stored in the same Stats table and shown in the
  details panel.
- The Steam/GOG/Heroic/Lutris scan is back: installed id classics are
  found via libraryfolders.vdf/appmanifests or the usual install folders,
  IWADs and expansion WADs are imported, and the Doom 64 re-release binary
  is registered as a Doom 64 source port.
- Hardened managed-library file operations against path traversal and stopped
  imports from overwriting untracked files with a colliding base name.
- Restricted external links to HTTP(S), fixed the library search field's QML
  lifetime, and added CTest coverage for path, URL, identity and version rules.

## 3.7.9.3

### Bug Fixes:
- Flatpak source ports are detected again. Detection required the `flatpak` binary on the sandbox PATH, which it never is inside our own Flatpak, so no Flatpak port was ever offered
- Source ports published outside `org.zdoom.*` (Woof, DSDA-Doom, and others) are recognised instead of being filtered out
- UZDoom is found whether installed as a distro package, an AppImage, or a Flatpak, and binaries are matched case-insensitively
- Detection runs in the background and can no longer freeze the window; helper commands have real timeouts instead of unbounded blocking reads
- ZDoom-family saves and statistics are found under any Flatpak app id, not only the three hardcoded `org.zdoom.*` ids
- The About window and update check report the real version instead of `1.0.0.0`

## 3.7.9.2

The first packaged Flatpak release. Everything from the start of the Linux port
through to the fixes below shipped under this one version number.

- GTK 4 + libadwaita Linux frontend with feature parity for the library, play, download, tag, settings, and archive surfaces
- First-run setup assistant: GZDoom install, IWAD import from Steam/GOG/Heroic/Lutris or Freedoom, and mod downloads from idgames, GitHub, and Romero Games
- Gosh Apps Flatpak packaging, with the launcher able to run host source ports through `flatpak-spawn`
- Fixed visual, performance, and security bugs found during the port
- Fixed Flatpak file opens and desktop shortcuts
- Disabled in-place updates inside Flatpak; updates are handled by Flatpak itself

---

# Upstream Doom Launcher release notes

From [nstlaurent/DoomLauncher](https://github.com/nstlaurent/DoomLauncher), reproduced unchanged.

## 3.9.0

## Features:
- Thumbnails can be displayed in 4:3 aspect ratio now, which eliminates pillarboxing
- Aspect ratio config setting to choose between 16:9 and 4:3
- "Resync recommended" button so that users can benefit from significant syncing improvements
- TitlePics are no longer considered screenshots; right click on a screenshot to set as main image
- Support for wide image title pic in Heretic + Hexen

## Bug Fixes:
- Upgraded SharpCompress utility to eliminate moderate vulnerability
- PlayForm functions correctly for Doom64 maps
- Auto load from Steam/Gog corrrectly finds Heretic + Hexen wads
- Hexen IWAD will use the correct palette for the title pic