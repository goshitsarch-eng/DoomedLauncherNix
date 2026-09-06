# DoomedLauncherNix

A Doom frontend and WAD library for Linux, built with **Qt 6** and **KDE
Kirigami** so it looks and feels like a modern KDE application — with
light, dark and follow-system color schemes.

Current release: **4.0.3**.

> DoomedLauncherNix is a Linux-focused fork of
> [Doom Launcher](https://github.com/nstlaurent/DoomLauncher) by
> [Hobomaster22 (nstlaurent)](https://github.com/nstlaurent). The launcher
> concept, database layout and the Windows version are their work, shared
> under the GPL. Please support the original project. File issues for this
> fork on [this tracker](https://github.com/goshitsarch-eng/DoomedLauncherNix/issues).

## Features

- **Library** of WADs, pk3s and mods with list and tile views, search,
  sorting, tags (tags can be their own tabs), ratings and comments
- **Play dialog**: source port, IWAD, warp to map, skill, extra
  parameters, additional files, demo recording, load latest save — with a
  live command-line preview; per-file settings are remembered
- **Source port detection**: GZDoom, UZDoom, VKDoom, dsda-doom, Crispy,
  Chocolate, Woof!, Eternity and more — as native binaries, AppImages,
  Flatpaks (`flatpak:org.zdoom.GZDoom`) or snaps (`snap:gzdoom`)
- **Title screens as library art**: TITLEPIC/TITLE lumps are decoded
  (classic Doom picture format, flats, or embedded PNGs, honoring the
  wad's own PLAYPAL and MAPINFO `titlepage=` overrides) and shown in the
  tile view and details panel
- **Per-level statistics**: kills/items/secrets/time recorded after each
  session — from `-levelstat` (dsda-doom, PrBoom+, Crispy, Woof!, …),
  `-statdump` (Chocolate Doom family) or ZDoom-family save games — and
  shown in the details panel
- **Steam/GOG/Heroic/Lutris scan**: finds installed Doom, Doom II, Final
  Doom, Heretic, Hexen, Strife and Doom 64, imports their IWADs and
  expansion WADs (NERVE, SIGIL, Master Levels, …), and registers the
  Doom 64 re-release binary as a source port
- **idgames browser**: search the archive, download from a mirror and
  auto-import into the library, straight from the app
- **Play time tracking**, screenshots/saves/demos per entry, IWAD
  management, first-run setup assistant
- **Light and dark mode**: follows the system color scheme by default, or
  force Breeze Light/Breeze Dark from Settings
- Network shares: file dialogs go through the KDE/XDG portal, so
  locations mounted via KIO (smb://, sftp:// …) open directly — no
  "open with another app" detour

## Downloads and automatic releases

Download the package for your architecture from
[GitHub Releases](https://github.com/goshitsarch-eng/DoomedLauncherNix/releases).
Every new release includes:

| CPU | Native binary archive | Flatpak bundle |
| --- | --- | --- |
| x64 | `DoomedLauncherNix-VERSION-linux-x86_64.tar.gz` | `DoomedLauncherNix-VERSION-x86_64.flatpak` |
| ARM64 | `DoomedLauncherNix-VERSION-linux-aarch64.tar.gz` | `DoomedLauncherNix-VERSION-aarch64.flatpak` |

Install a downloaded Flatpak with `flatpak install --user ./PACKAGE.flatpak`.
The bundle references Flathub for its KDE runtime. The native tar.gz contains
`bin/` and `share/`; extract it and run `./bin/doomedlauncher`. It requires host
Qt/KDE runtime libraries compatible with the KDE 6.10 SDK, including Kirigami,
qqc2-desktop-style and the SQLite driver. It is not a self-contained bundle.
Use Flatpak on systems with older Qt/KDE libraries. Verify downloads with the
included `SHA256SUMS-ARCH.txt` file: `sha256sum --check SHA256SUMS-ARCH.txt`.

The `Build, test and release` GitHub workflow runs on pushes and pull requests.
Both architectures build natively and must pass CTest before publication. To
publish the next version, update CMake, README, AppStream and release notes, then
push to `development`. The workflow creates `vVERSION` and a GitHub release with
all four packages. A matching `vVERSION` tag also triggers publication. Existing
releases are left intact; ordinary pushes still build but do not replace assets.
`python3 scripts/release_metadata.py` checks version consistency locally.

## Using the repaired controls

- In **Play with Options**, choose library mods under **Additional files**, then
  Add, Remove or Move up to arrange them. Additional mods load in listed order,
  before the selected entry. **Remember these settings** preserves that order.
- Use **Show Details** to reach screenshots, saves and statistics in a narrow
  window. Selection remains attached to the same file when sorting; filtering
  a selected file out clears its selection.
- **Add IWADs** also works when the file already exists in the mod library.
- Use the play button beside an imported demo in Details to replay it with the
  entry's remembered source port. The selected engine must support that demo.
- Changes to tags in **Edit File** are applied with Save; Cancel discards them.
- One idgames download runs at a time. Failed downloads do not leave a pending
  request to launch an unrelated later download.

## Scope and remaining limitations

This review covers the Qt library, play options, imports, tags, settings and
idgames paths. Older database tables do not imply complete upstream feature
parity: utility launching and game-profile editing still have no Qt interface.
The store scanner records a Doom 64 re-release executable, but the standard
Doom source-port picker does not launch that separate engine type. Imported demos can be replayed from Details with the entry's remembered source
port. Save associations still open with the desktop application; choosing an
individual associated save does not load it through the launcher. Source-port
specific game rendering and physical desktop/portal behavior require testing
with those engines on a real system.

## Existing libraries keep working

The launcher uses the same SQLite database and data layout as previous
releases (`$XDG_DATA_HOME/doomlauncher/DoomLauncher.sqlite` with
`GameFiles/` next to it). If you used an earlier GTK build, your library,
tags, source ports and play history are picked up as-is.

## Building

Dependencies: Qt ≥ 6.8 (Quick, Controls2, Sql, Network, Widgets,
Concurrent), KDE Frameworks ≥ 6.6 (Kirigami, KCoreAddons, KI18n,
KColorScheme, KArchive, KIconThemes), extra-cmake-modules, and at runtime
qqc2-desktop-style plus breeze-icons. Building the regression tests also
requires the Qt 6 Test module. The KDE 6.10 SDK provides all build-time
dependencies used by the Flatpak manifest.

```bash
cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Release -DBUILD_TESTING=ON
cmake --build build --parallel 2
ctest --test-dir build --output-on-failure
sudo cmake --install build
doomedlauncher
```

Use `-DBUILD_TESTING=OFF` only when producing a minimal package without the
Qt Test development module. `doomedlauncher --version` reports the same
4.0.3 version recorded in CMake and AppStream metadata.

### Flatpak

Version 4.0.2 restores host source-port detection and launch in the Qt build.
Native binaries/AppImages, sibling Flatpaks and Snaps use one typed
`flatpak-spawn --host --watch-bus` boundary, with the working directory
passed using `--directory=`. Detection reads the host PATH and executable
files, not the SDK's `/usr`; arguments are never evaluated by a shell.
The existing `org.freedesktop.Flatpak` permission is required; no additional
permissions were added. Failed host commands are reported and detection
timeouts terminate the bridge. Native installations still run directly.

The `hostprocess_test` CTest regression uses a strict host-bridge fixture
to exercise detection, launch argv, host-only paths, errors and timeouts.
It is not a physical game/rendering test.

```bash
flatpak-builder --user --install --force-clean build-flatpak flatpak/com.goshapps.DoomLauncher.yml
flatpak run com.goshapps.DoomLauncher
```

## License

GPL-3.0-only, like the upstream project. See [LICENSE](LICENSE).

Doom Launcher is an independent tool. It is not affiliated with or
endorsed by id Software, and it ships no game data — supply your own
IWAD, or use a free one such as [Freedoom](https://freedoom.github.io/).
