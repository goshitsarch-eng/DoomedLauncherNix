# Third-Party Notices

Doom Launcher makes use of third-party assets — these may include libraries, images, icons, fonts, and more. The licenses for these assets are reproduced or linked below, alongside any other information deemed to be relevant to the asset. If we have failed to include a license for a third-party asset, please make us aware so that we can add it to this document.

This file covers both the original Windows application and the Linux/GTK stack added by this fork. Doom Launcher itself is licensed GPL-3.0-or-later; see [LICENSE](LICENSE).

## The original project

**Doom Launcher** by [Hobomaster22](https://github.com/nstlaurent) and the upstream contributors —
<https://github.com/nstlaurent/DoomLauncher> — GPL-3.0-or-later.

This repository is a fork of that project. All of the launcher's original design,
database, syncing, and Windows UI work is theirs.

---

## Bundled source

These components are vendored into this repository as source, not consumed as packages.

### BindingListView

A type-safe, sortable, filterable, data-bindable view over lists of objects, in the
`Equin.ApplicationFramework` namespace. Vendored in `BindingListView/`.

* Project, as recorded in the bundled `BindingListView.nuspec`: <https://github.com/waynebloss/BindingListView>
* License, as recorded in that nuspec: `https://github.com/waynebloss/BindingListView/blob/master/license.txt`
* Copyright notice in that nuspec: "Copyright 2014"

> ⚠️ **Attribution gap.** No licence text for this component is stored in this
> repository, and the licence URL recorded in the bundled nuspec no longer
> resolves. If you maintain or recognise this code, please open an issue so the
> correct licence can be reproduced here in full.

### CheckBoxComboBox / PopupComboBox

Windows Forms controls in the `PresentationControls` namespace. Vendored in
`CheckBoxComboBox/`.

* `CheckBoxComboBox` by **Martin Lottering** (2007), per the header comment in `CheckBoxComboBox/CheckBoxComboBox.cs`.
* Extends the CodeProject "Simple pop-up control" `PopupComboBox` by **Lukasz Swiatkowski** — <https://www.codeproject.com/Articles/17502/Simple-pop-up-control>

> ⚠️ **Attribution gap.** No licence text for these controls is stored in this
> repository. The authorship above is taken from the source headers. Please open
> an issue if you can point to the licence these were published under, so it can
> be reproduced here.

---

## Packages — Linux stack (.NET 8)

| Component | Author | License | Project |
|---|---|---|---|
| Gameloop.Vdf | Shravan Rajinikanth | MIT | <https://github.com/shravan2x/Gameloop.Vdf> |
| Microsoft.Data.Sqlite | Microsoft | MIT | <https://learn.microsoft.com/dotnet/standard/data/sqlite/> |
| Newtonsoft.Json | James Newton-King | MIT | <https://www.newtonsoft.com/json> |
| Octokit | GitHub | MIT | <https://github.com/octokit/octokit.net> |
| SharpCompress | Adam Hathcock | MIT | <https://github.com/adamhathcock/sharpcompress> |
| SixLabors.ImageSharp | Six Labors and contributors | Six Labors Split License 1.0 | <https://github.com/SixLabors/ImageSharp> |
| GirCore.Gtk-4.0 | Marcel Tiede (gir.core) | MIT | <https://github.com/gircore/gir.core> |
| GirCore.Adw-1 | Marcel Tiede (gir.core) | MIT | <https://github.com/gircore/gir.core> |
| GirCore.GdkPixbuf-2.0 | Marcel Tiede (gir.core) | MIT | <https://github.com/gircore/gir.core> |
| MSTest (test-only) | Microsoft | MIT | <https://github.com/microsoft/testfx> |
| Microsoft.NET.Test.Sdk (test-only) | Microsoft | MIT | <https://github.com/microsoft/vstest> |

The Linux frontend links against the system **GTK 4** and **libadwaita** libraries
(LGPL-2.1-or-later), which are not distributed in this repository.

**Note on ImageSharp:** version 3.x is published under the Six Labors Split License,
not a plain Apache licence. Read the terms at
<https://github.com/SixLabors/ImageSharp/blob/main/LICENSE> before redistributing.

## Packages — Windows stack (.NET Framework 4.8)

| Component | Author | License | Project |
|---|---|---|---|
| System.Data.SQLite | SQLite Development Team | Public domain (<https://www.sqlite.org/copyright.html>) | <https://system.data.sqlite.org/> |
| Squid-Box.SevenZipSharp | Squid-Box, after Markovtsev Vadim's SevenZipSharp | LGPL-3.0-only | <https://github.com/squid-box/SevenZipSharp> |
| SharpCompress | Adam Hathcock | MIT | <https://github.com/adamhathcock/sharpcompress> |
| Gameloop.Vdf | Shravan Rajinikanth | MIT | <https://github.com/shravan2x/Gameloop.Vdf> |
| Newtonsoft.Json | James Newton-King | MIT | <https://www.newtonsoft.com/json> |
| Octokit | GitHub | MIT | <https://github.com/octokit/octokit.net> |
| EntityFramework | Microsoft | Apache-2.0 | <https://github.com/dotnet/ef6> |

Licences in these two tables are the SPDX expressions published with each package
on nuget.org, checked against the versions pinned in `DoomLauncher/packages.config`
and the `.csproj` files in `src/`.

---

## Full license texts

### Gameloop.Vdf

[GitHub repo](https://github.com/shravan2x/Gameloop.Vdf)

The MIT License (MIT)

Copyright (c) 2016 Shravan Rajinikanth

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

## Services and content the launcher links to

Doom Launcher downloads from and links out to community resources. They are not
affiliated with this project, and their content remains theirs:

* **Doomworld /idgames archive** — <https://www.doomworld.com/idgames/> (search and download API)
* **Romero Games** — SIGIL and SIGIL II, offered as free downloads by their authors — <https://romero.com/>
* **Freedoom** — a free IWAD project — <https://freedoom.github.io/>
* **GZDoom / ZDoom** and the wider source port community — <https://zdoom.org/>
* **ModDB**, **Codeberg**, **Realm667**, **DSDA**, and **DoomWiki**, opened in a browser rather than scraped

Doom and the Doom engine are the work of **id Software**. Doom Launcher is an
independent tool, not affiliated with or endorsed by id Software or ZeniMax, and
ships no id Software data. Retail IWADs must come from your own copy of the game.
