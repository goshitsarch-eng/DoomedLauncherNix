# Doom Launcher on Linux

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

A `.desktop` file is written to `~/.local/share/applications` on first launch.

## Linux replacements for Windows-only pieces

| Windows | Linux |
|---|---|
| WinForms / WPF UI | GTK 4 + libadwaita (light/dark follows Settings → Color theme, or system) |
| `System.Data.SQLite` | `Microsoft.Data.Sqlite` |
| SevenZipSharp + `7z.dll` | SharpCompress (zip, 7z, rar) |
| Steam/GOG registry | `~/.steam/steam`, Flatpak/snap Steam, GOG/Heroic/Lutris folders |
| `.lnk` / SendTo shortcuts | `.desktop` files on the desktop and in applications |
| Immersive Win32 title bar | Adwaita header bar |
| Source port `.exe` next to the port | Native binaries (`gzdoom`, etc.) on `PATH` or an absolute path |

The CRT/screen-filter overlay from Windows is stored on the play profile but is not composited over the game window on Linux.

## Feature coverage

The GTK UI includes the original main-window surface: search (title/author/filename, optional include-all), Play, downloads, tags, update indicator, resync-recommended, split summary pane, list and tile views, tabs (Recent / Local / Untagged / IWADs / Id Games / tag tabs), and screenshot/save/demo associations.

Menus and dialogs cover add files/directory/IWADs/recursive, Steam/GOG load, source ports, utilities, Doom 64, create zip, settings, tag manager, play now/random, text generator, cumulative stats, about/help/manual update, view text, open archive, edit, resync, idgames metadata, sort/tag/utility/delete/rename, desktop shortcuts, play profiles (port, IWAD, map, skill, demo play/record, extra params, stats, latest save, additional/specific files, preview launch command).

## Source ports

Add a port from **Source Ports…**. For distro packages set Executable to `gzdoom` (or the binary name) and Directory to `/usr/bin` or leave it empty so the launcher resolves `PATH`.
