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
| Source port `.exe` next to the port | Native binaries (`gzdoom`) on `PATH`, absolute paths, or `flatpak:org.zdoom.GZDoom` / `snap:gzdoom` |

The CRT/screen-filter overlay from Windows is stored on the play profile but is not composited over the game window on Linux.

## Feature coverage

The GTK UI includes the original main-window surface: search (title/author/filename, optional include-all), Play, downloads, tags, update indicator, resync-recommended, split summary pane, list and tile views, tabs (Recent / Local / Untagged / IWADs / Id Games / tag tabs), and screenshot/save/demo associations.

Menus and dialogs cover add files/directory/IWADs/recursive, Steam/GOG load, source ports, utilities, Doom 64, create zip, settings, tag manager, play now/random, text generator, cumulative stats, about/help/manual update, view text, open archive, edit, resync, idgames metadata, sort/tag/utility/delete/rename, desktop shortcuts, play profiles (port, IWAD, map, skill, demo play/record, extra params, stats, latest save, additional/specific files, preview launch command).

## Source ports and first-run setup

On first launch (no source ports or no IWADs), the **Setup assistant** walks through GZDoom, IWADs, and mods. You can open it any time from the header or **Add → Setup assistant…**.

The assistant:

1. Detects GZDoom and other ports on `PATH`, in `/usr/bin`, as a Flatpak (`org.zdoom.GZDoom`), or as a snap.
2. Can run `flatpak install --user flathub org.zdoom.GZDoom` when Flatpak is available.
3. Imports IWADs from Steam/GOG/Heroic/Lutris, a file picker, or a Freedoom download.
4. Searches the Doomworld idgames archive (featured mapsets plus free search) and downloads into the library, with **Download and play**.

**Get mods…** in the header opens the same idgames browser without the rest of setup.

### How to record a port

| What you installed | Executable field | Directory |
|---|---|---|
| Distro package / binary on `PATH` | `gzdoom` | `/usr/bin` or empty |
| Absolute binary | `gzdoom` | folder that contains it |
| Flathub GZDoom | `flatpak:org.zdoom.GZDoom` | empty |
| Snap | `snap:gzdoom` | empty |

Flatpak launches as:

```text
flatpak run --filesystem=host --filesystem=home org.zdoom.GZDoom -- <iwad and -file args>
```

`--filesystem=host` is required so the sandbox can read IWADs and mods under `~/.local/share/doomlauncher`. ExtraParameters stay Doom arguments; they are not used as the Flatpak wrapper.

**Source Ports…** has **Detect GZDoom / ports…**. The edit dialog can fill the fields from whatever was detected.

GZDoom-family ports (`gzdoom`, `uzdoom`, `vkdoom`, Flatpak app IDs under `org.zdoom`) use ZDoom save/stat handling, including `~/.config/gzdoom` and `~/.var/app/org.zdoom.GZDoom/config/gzdoom`.
