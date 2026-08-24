# DoomedLauncherNix release notes

This is a fork of [Doom Launcher](https://github.com/nstlaurent/DoomLauncher) by
[Hobomaster22](https://github.com/nstlaurent). The notes below cover only the Linux/Flatpak work
added by this fork; upstream's own release notes follow further down, unchanged.

Fork versions are numbered separately from upstream releases.

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