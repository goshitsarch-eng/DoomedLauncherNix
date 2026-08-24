# Doom Launcher on Linux

> This is the Linux documentation for **DoomedLauncherNix**, a fork of
> [Doom Launcher](https://github.com/nstlaurent/DoomLauncher) by
> [Hobomaster22](https://github.com/nstlaurent). The launcher, its database, and its
> Windows frontend are their work;
> everything described in this file is the Linux stack added by the fork.
> See [README.adoc](README.adoc) for the full attribution, and file Linux issues on
> [this fork's tracker](https://github.com/goshitsarch-eng/DoomedLauncherNix/issues).

This tree adds a **.NET 8** stack that runs Doom Launcher natively on Linux with **GTK 4** and **libadwaita**, matching the Windows WinForms frontend’s library, play, download, tag, settings, and archive features.

Windows users should keep building `DoomLauncher/DoomLauncher.csproj` (.NET Framework 4.8 + WinForms). Linux users build this stack:

```bash
sudo apt install dotnet-sdk-8.0 libgtk-4-1 libadwaita-1-0
dotnet build DoomLauncher.Linux.sln
dotnet run --project src/DoomLauncher.Gtk/DoomLauncher.Gtk.csproj
```

## Layout

| Project | Role |
|---|---|
| `src/WadReader` | WAD image reading via ImageSharp (no System.Drawing) |
| `src/DoomLauncher.Core` | Shared library, launch, SQLite, Steam/GOG, archives, sync |
| `src/DoomLauncher.Gtk` | GTK 4 + Adwaita UI |
| `src/DoomLauncher.Core.Tests` | Ported unit tests |

## Data directory

If `DoomLauncher.sqlite` is next to the executable (or current directory), the app runs in portable mode. Otherwise it uses `$XDG_DATA_HOME/doomlauncher` (default `~/.local/share/doomlauncher`).

When the launcher **itself** is a Flatpak, data always goes under the sandbox XDG directory (`~/.var/app/com.goshapps.DoomLauncher/data/doomlauncher`). The sqlite next to `/app/bin` is read-only and is not treated as portable mode.

A `.desktop` file is written to `~/.local/share/applications` on first launch for a native install. A Flatpak build ships its own desktop file and skips that write.

## Linux replacements for Windows-only pieces

| Windows | Linux |
|---|---|
| WinForms / WPF UI | GTK 4 + libadwaita (light/dark follows Settings → Color theme, or system) |
| `System.Data.SQLite` | `Microsoft.Data.Sqlite` |
| SevenZipSharp + `7z.dll` | SharpCompress (zip, 7z, rar) |
| Steam/GOG registry | `~/.steam/steam`, Flatpak/snap Steam, GOG/Heroic/Lutris folders |
| `.lnk` / SendTo shortcuts | `.desktop` files on the desktop and in applications |
| Immersive Win32 title bar | Adwaita header bar |
| Source port `.exe` next to the port | Native binaries (`gzdoom`) on `PATH`, absolute paths, or `flatpak:org.zdoom.GZDoom` / `snap:gzdoom` |

The CRT/screen-filter overlay from Windows is stored on the play profile but is not composited over the game window on Linux.

## Feature coverage

The GTK UI includes the original main-window surface: search (title/author/filename, optional include-all), Play, downloads, tags, update indicator, resync-recommended, split summary pane, list and tile views, tabs (Recent / Local / Untagged / IWADs / Id Games / tag tabs), and screenshot/save/demo associations.

Menus and dialogs cover add files/directory/IWADs/recursive, Steam/GOG load, source ports, utilities, Doom 64, create zip, settings, tag manager, play now/random, text generator, cumulative stats, about/help/manual update, view text, open archive, edit, resync, idgames metadata, sort/tag/utility/delete/rename, desktop shortcuts, play profiles (port, IWAD, map, skill, demo play/record, extra params, stats, latest save, additional/specific files, preview launch command).

## Source ports and first-run setup

On first launch (no source ports or no IWADs), the **Setup assistant** walks through GZDoom, IWADs, and mods. You can open it any time from the header or **Add → Setup assistant…**.

The assistant:

1. Detects GZDoom, UZDoom, VKDoom, and other ports on `PATH`, in `/usr/bin`, `/usr/local/bin`, `/usr/games`, `/usr/local/games`, `~/.local/bin`, and `~/bin`; as an AppImage under `~/Applications`, `~/AppImages`, `~/bin`, `~/.local/bin`, or `~/Downloads`; as a Flatpak; or as a snap. Binary names are matched case-insensitively, so a build that ships `UZDoom` is found by a `uzdoom` lookup and is then recorded (and launched) under the name it actually has on disk. Detection runs in the background, so the window stays responsive while it looks.
2. Can run `flatpak install --user flathub org.zdoom.GZDoom` when Flatpak is available.
3. Imports IWADs from Steam/GOG/Heroic/Lutris, a file picker, or a Freedoom download.
4. Downloads featured mods from **idgames, GitHub, Romero Games**, and other community sites, or opens ModDB/Codeberg pages when a direct zip is not allowed. **Download and play** still works.

**Get mods…** in the header opens that catalog (featured, idgames search, community site links) without the rest of setup.

### Featured sources

| Source | Examples | What the launcher does |
|---|---|---|
| Doomworld /idgames | Valiant, Eviternity, Gossip, Sunless Empire | Search + auto-download into the library |
| GitHub releases | Beautiful Doom PK3, Freedoom | Resolve the latest matching asset, with a pinned fallback URL |
| GitHub archive zip | Project Brutality `PB_Staging` | Direct zip URL, auto-download |
| Romero Games | SIGIL, SIGIL II | Official free zip URLs, auto-download |
| ModDB / Codeberg | Brutal Doom, Ashes 2063, Hedon, Hideous Destructor | **Open page** (those sites do not offer a stable direct file URL) |

Community site bookmarks include Cacowards, Realm667, DSDA, ZDoom forums, and DoomWiki.

### How to record a port

| What you installed | Executable field | Directory |
|---|---|---|
| Distro package / binary on `PATH` | `gzdoom` | `/usr/bin` or empty |
| Absolute binary | `gzdoom` | folder that contains it |
| AppImage | `UZDoom-4.14.0-x86_64.AppImage` | folder that contains it |
| Flathub GZDoom | `flatpak:org.zdoom.GZDoom` | empty |
| Any other port Flatpak | `flatpak:<app id>` | empty |
| Snap | `snap:gzdoom` | empty |

Flatpak launches as:

```text
flatpak run --filesystem=host --filesystem=home org.zdoom.GZDoom -- <IWAD and -file args>
```

`--filesystem=host` is required so the sandbox can read IWADs and mods under `~/.local/share/doomlauncher`. ExtraParameters stay Doom arguments; they are not used as the Flatpak wrapper.

When **this launcher** is itself a Flatpak, the same GZDoom command is prefixed with `flatpak-spawn --host --` so host Flatpak/snap/PATH binaries stay reachable, and host GZDoom can still see files under `~/.var/app/com.goshapps.DoomLauncher/`.

**Source Ports…** has **Detect GZDoom / ports…**. The edit dialog can fill the fields from whatever was detected. Both re-scan from scratch rather than reusing an earlier answer, so a port installed while the launcher is open is picked up.

GZDoom-family ports (`gzdoom`, `uzdoom`, `vkdoom`, `lzdoom`, `zandronum`, and Flatpak app IDs containing those names) use ZDoom save/stat handling. Save and statistics lookup covers `~/.config/<port>` and `~/.var/app/*/config/<port>` for every installed Flatpak, not just `org.zdoom.*`, so a UZDoom Flatpak published under another app ID still reports its saves and stats.

### Which Flatpaks count as a source port

Detection does not require a `org.zdoom.*` app ID. An installed Flatpak is offered as a source port when its ID matches a known port binary (`org.zdoom.GZDoom`, `io.github.fabiangreffrath.Woof`, `io.github.kraflab.dsda-doom`, …), when its last segment ends in `doom`, or when the ID mentions `zdoom`, `zandronum`, or `doomsday`. Doom Launcher never offers itself.

Installed Flatpaks are found two ways, and both are used:

* `flatpak list --app --columns=application`, run on the host through `flatpak-spawn` when the launcher is itself a Flatpak
* a direct read of `~/.local/share/flatpak/app` and `/var/lib/flatpak/app`, which still works when the `flatpak` CLI is not reachable at all

Helper commands (`flatpak`, `snap`, and binary lookups) run with a hard timeout and both pipes drained, and a host call that times out short-circuits the rest of that pass, so a Flatpak portal that stops answering cannot hang the launcher.

## Running as a Flatpak

A starter manifest lives in `flatpak/com.goshapps.DoomLauncher.yml`. It needs network (idgames/GitHub/Romero downloads), Wayland/X11, DRI, home filesystem access (Steam libraries and WAD folders), and `talk-name=org.freedesktop.Flatpak` so `flatpak-spawn --host` can start GZDoom and `flatpak` on the host.

```bash
flatpak-builder --user --install --force-clean build-dir flatpak/com.goshapps.DoomLauncher.yml
flatpak run com.goshapps.DoomLauncher
```

Inside that sandbox the launcher:

* Stores the database under XDG, not next to the `/app` binary
* Detects host `gzdoom` / `flatpak` / `snap` through `flatpak-spawn --host`, resolving every candidate binary in a single host round trip rather than one process per name
* Launches host binaries by absolute path, so a port in `/usr/local/games` or `~/.local/bin` works even though the sandbox cannot see it
* Opens HTTP(S) pages through the desktop portal
* Does not overwrite the Flatpak-provided `.desktop` file

## Credits

The Linux port would not exist without the projects it builds on:

* **[Doom Launcher](https://github.com/nstlaurent/DoomLauncher)** by [Hobomaster22](https://github.com/nstlaurent) — the launcher this forks, GPL-3.0-only.
* **[GTK](https://www.gtk.org/)** and **[libadwaita](https://gitlab.gnome.org/GNOME/libadwaita)** — the toolkit and platform library.
* **[gir.core](https://github.com/gircore/gir.core)** by Marcel Tiede — the C# bindings that make GTK 4 usable from .NET.
* **[SharpCompress](https://github.com/adamhathcock/sharpcompress)**, **[ImageSharp](https://github.com/SixLabors/ImageSharp)**, **[Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/)** — the cross-platform replacements for the Windows-only pieces.
* **[GZDoom](https://zdoom.org/)** and the wider source port community, and **[Flathub](https://flathub.org/)** for distributing them.

Full details, including licences, are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Screenshot

image::docs/linux-screenshot.png[Get mods dialog on Linux]

image::docs/linux-main-window.png[GTK main window on Linux]

