# DoomedLauncherNix

A Doom frontend and WAD library for Linux, built with **Qt 6** and **KDE
Kirigami** so it looks and feels like a modern KDE application — with
light, dark and follow-system color schemes.

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

## Existing libraries keep working

The launcher uses the same SQLite database and data layout as previous
releases (`$XDG_DATA_HOME/doomlauncher/DoomLauncher.sqlite` with
`GameFiles/` next to it). If you used an earlier GTK build, your library,
tags, source ports and play history are picked up as-is.

## Building

Dependencies: Qt ≥ 6.8 (Quick, Controls2, Sql, Network, Widgets,
Concurrent), KDE Frameworks ≥ 6.6 (Kirigami, KCoreAddons, KI18n,
KColorScheme, KArchive, KIconThemes), extra-cmake-modules, and at runtime
qqc2-desktop-style plus breeze-icons.

```bash
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
sudo cmake --install build
doomedlauncher
```

### Flatpak

```bash
flatpak-builder --user --install --force-clean build-flatpak flatpak/com.goshapps.DoomLauncher.yml
flatpak run com.goshapps.DoomLauncher
```

## License

GPL-3.0-only, like the upstream project. See [LICENSE](LICENSE).

Doom Launcher is an independent tool. It is not affiliated with or
endorsed by id Software, and it ships no game data — supply your own
IWAD, or use a free one such as [Freedoom](https://freedoom.github.io/).
