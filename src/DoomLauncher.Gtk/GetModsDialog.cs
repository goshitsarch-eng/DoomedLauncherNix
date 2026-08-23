using DoomLauncher.DataSources;
using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DoomLauncher.Linux
{
    public static class GetModsDialog
    {
        public static void Show(Gtk.Window parent, IdGamesDataAdapater idGames, Action<IGameFileDownloadable, bool> download)
        {
            var window = GtkUtil.ModalWindow(parent, "Get mods", 780, 700);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(12);
            box.SetMarginEnd(12);
            box.SetMarginTop(12);
            box.SetMarginBottom(12);
            box.Append(Create(window, idGames, download));
            box.Append(GtkUtil.DialogButtons(GtkUtil.Button("Close", () => window.Destroy())));
            window.SetChild(box);
            window.Present();
        }

        public static Gtk.Widget Create(Gtk.Window parent, IdGamesDataAdapater idGames, Action<IGameFileDownloadable, bool> download)
        {
            var root = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            var intro = Gtk.Label.New("Featured mods come from idgames, GitHub, Romero Games, and ModDB. Auto-download uses official zips and PK3s when the site allows it; otherwise Open page. Search still uses the Doomworld idgames archive.");
            intro.SetWrap(true);
            intro.SetXalign(0);
            root.Append(intro);

            var search = Gtk.SearchEntry.New();
            search.SetPlaceholderText("Search idgames by title...");
            root.Append(search);

            var status = Gtk.Label.New("Pick a featured mod, search idgames, or open a community site.");
            status.AddCssClass("dim-label");
            status.SetXalign(0);
            status.SetWrap(true);
            root.Append(status);

            var notebook = Gtk.Notebook.New();
            var featured = Gtk.ListBox.New();
            featured.AddCssClass("boxed-list");
            var results = Gtk.ListBox.New();
            results.AddCssClass("boxed-list");
            var sites = Gtk.ListBox.New();
            sites.AddCssClass("boxed-list");
            notebook.AppendPage(GtkUtil.Scroll(featured), Gtk.Label.New("Featured"));
            notebook.AppendPage(GtkUtil.Scroll(results), Gtk.Label.New("Search results"));
            notebook.AppendPage(GtkUtil.Scroll(sites), Gtk.Label.New("Community sites"));
            notebook.Vexpand = true;
            root.Append(notebook);

            var resultFiles = new List<IGameFile>();
            int i = 0;
            foreach (var mod in ModCatalog.Featured)
            {
                featured.Append(FeaturedRow(i, mod));
                i++;
            }

            int siteIndex = 0;
            foreach (var site in ModCatalog.CommunitySites)
            {
                sites.Append(SiteRow(siteIndex, site.Title, site.Summary));
                siteIndex++;
            }

            RemoteMod SelectedFeatured()
            {
                if (notebook.GetCurrentPage() != 0)
                    return null;
                var row = featured.GetSelectedRow();
                if (row == null || !int.TryParse(row.Name, out int index) || index < 0 || index >= ModCatalog.Featured.Length)
                    return null;
                return ModCatalog.Featured[index];
            }

            (string Title, string Url, string Summary)? SelectedSite()
            {
                var row = sites.GetSelectedRow();
                if (row == null || !int.TryParse(row.Name, out int index) || index < 0 || index >= ModCatalog.CommunitySites.Length)
                    return null;
                return ModCatalog.CommunitySites[index];
            }

            IGameFileDownloadable SelectedSearchDownloadable()
            {
                if (notebook.GetCurrentPage() != 1)
                    return null;
                var row = results.GetSelectedRow();
                if (row != null && int.TryParse(row.Name, out int index) && index >= 0 && index < resultFiles.Count)
                    return resultFiles[index] as IGameFileDownloadable;
                return null;
            }

            void fillResults(IEnumerable<IGameFile> files, string emptyMessage)
            {
                GtkUtil.ClearList(results);
                resultFiles.Clear();
                foreach (var file in files)
                {
                    resultFiles.Add(file);
                    var row = Gtk.ListBoxRow.New();
                    row.Name = (resultFiles.Count - 1).ToString();
                    var item = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
                    item.SetMarginStart(8);
                    item.SetMarginEnd(8);
                    item.SetMarginTop(6);
                    item.SetMarginBottom(6);
                    var title = Gtk.Label.New(string.IsNullOrEmpty(file.Title) ? file.FileNameNoPath : file.Title);
                    title.SetXalign(0);
                    var meta = Gtk.Label.New($"{file.Author}  ·  {file.FileNameNoPath}");
                    meta.SetXalign(0);
                    meta.AddCssClass("dim-label");
                    item.Append(title);
                    item.Append(meta);
                    row.SetChild(item);
                    results.Append(row);
                }
                if (resultFiles.Count == 0)
                    status.SetLabel(emptyMessage);
                else
                    status.SetLabel($"Found {resultFiles.Count} file(s). Download one, then play it from your library.");
                notebook.SetCurrentPage(1);
            }

            void runSearch(GameFileFieldType field, string query)
            {
                status.SetLabel("Searching idgames...");
                Task.Run(() =>
                {
                    IEnumerable<IGameFile> files = CuratedMods.Search(idGames, field, query);
                    GtkUtil.RunOnUi(() => fillResults(files, "No idgames results. Try a shorter title or pick a featured wad."));
                });
            }

            search.OnActivate += (s, e) =>
            {
                var text = search.GetText();
                if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 3)
                {
                    status.SetLabel("Type at least 3 characters to search idgames.");
                    return;
                }
                runSearch(GameFileFieldType.Title, text.Trim());
            };

            void openSelectedPage()
            {
                if (notebook.GetCurrentPage() == 2)
                {
                    var site = SelectedSite();
                    if (site == null)
                    {
                        GtkUtil.Alert(parent, "Select a site", "Pick a community site first.");
                        return;
                    }
                    GtkUtil.OpenPath(site.Value.Url);
                    return;
                }

                var mod = SelectedFeatured();
                if (mod == null || string.IsNullOrEmpty(mod.PageUrl))
                {
                    GtkUtil.Alert(parent, "Select a mod", "Pick a featured mod or a community site.");
                    return;
                }
                GtkUtil.OpenPath(mod.PageUrl);
            }

            void startIdGamesDownload(RemoteMod mod, bool play)
            {
                var curated = ModCatalog.ToIdGamesQuery(mod);
                status.SetLabel($"Searching idgames for {mod.Title}...");
                Task.Run(() =>
                {
                    var files = CuratedMods.Search(idGames, curated).ToList();
                    GtkUtil.RunOnUi(() =>
                    {
                        if (files.Count == 0)
                        {
                            status.SetLabel($"No idgames match for {mod.Title}. Try Open page.");
                            return;
                        }
                        fillResults(files, "No match.");
                        var first = files[0] as IGameFileDownloadable;
                        if (first != null)
                        {
                            status.SetLabel($"Downloading {first.FileName}...");
                            download?.Invoke(first, play);
                        }
                    });
                });
            }

            void startUrlDownload(RemoteMod mod, bool play)
            {
                status.SetLabel($"Resolving {mod.Title}...");
                Task.Run(async () =>
                {
                    var resolved = await ModCatalog.ResolveAsync(mod, CancellationToken.None);
                    GtkUtil.RunOnUi(() =>
                    {
                        if (!resolved.Succeeded)
                        {
                            status.SetLabel(resolved.Error ?? "Could not resolve download.");
                            if (!string.IsNullOrEmpty(mod.PageUrl))
                                GtkUtil.OpenPath(mod.PageUrl);
                            return;
                        }
                        status.SetLabel($"Downloading {resolved.FileName}...");
                        download?.Invoke(new UrlDownloadable(resolved.Url, resolved.FileName), play);
                    });
                });
            }

            void startDownload(bool play)
            {
                IGameFileDownloadable file = SelectedSearchDownloadable();
                if (file != null)
                {
                    status.SetLabel($"Downloading {file.FileName}...");
                    download?.Invoke(file, play);
                    return;
                }

                var mod = SelectedFeatured();
                if (mod == null)
                {
                    GtkUtil.Alert(parent, "Select a mod", "Pick a featured mod, a search result, or use Open page for a community site.");
                    return;
                }

                if (!mod.CanAutoDownload || mod.Kind == RemoteModKind.BrowserPage)
                {
                    status.SetLabel($"Opening {mod.SourceName} for {mod.Title}...");
                    GtkUtil.OpenPath(mod.PageUrl);
                    return;
                }

                if (mod.Kind == RemoteModKind.IdGames)
                {
                    startIdGamesDownload(mod, play);
                    return;
                }

                startUrlDownload(mod, play);
            }

            var downloadBtn = Gtk.Button.NewWithLabel("Download");
            downloadBtn.AddCssClass("suggested-action");
            var playBtn = Gtk.Button.NewWithLabel("Download and play");
            var openBtn = Gtk.Button.NewWithLabel("Open page");
            var latestBtn = GtkUtil.Button("Latest uploads", () =>
            {
                status.SetLabel("Loading latest idgames uploads...");
                Task.Run(() =>
                {
                    var files = CuratedMods.Latest(idGames);
                    GtkUtil.RunOnUi(() => fillResults(files, "Could not load latest uploads."));
                });
            });

            downloadBtn.OnClicked += (s, e) => startDownload(false);
            playBtn.OnClicked += (s, e) => startDownload(true);
            openBtn.OnClicked += (s, e) => openSelectedPage();

            var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            buttons.Append(latestBtn);
            buttons.Append(openBtn);
            buttons.Append(downloadBtn);
            buttons.Append(playBtn);
            root.Append(buttons);
            return root;
        }

        private static Gtk.ListBoxRow FeaturedRow(int index, RemoteMod mod)
        {
            var row = Gtk.ListBoxRow.New();
            row.Name = index.ToString();
            var item = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
            item.SetMarginStart(8);
            item.SetMarginEnd(8);
            item.SetMarginTop(6);
            item.SetMarginBottom(6);
            var title = Gtk.Label.New($"{mod.Title}  ·  {mod.Group}");
            title.SetXalign(0);
            title.AddCssClass("heading");
            string how = mod.CanAutoDownload ? "auto-download" : "open in browser";
            var summary = Gtk.Label.New($"{mod.Summary}\n{mod.SourceName} · {how}");
            summary.SetXalign(0);
            summary.SetWrap(true);
            summary.AddCssClass("dim-label");
            item.Append(title);
            item.Append(summary);
            row.SetChild(item);
            return row;
        }

        private static Gtk.ListBoxRow SiteRow(int index, string titleText, string summaryText)
        {
            var row = Gtk.ListBoxRow.New();
            row.Name = index.ToString();
            var item = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
            item.SetMarginStart(8);
            item.SetMarginEnd(8);
            item.SetMarginTop(6);
            item.SetMarginBottom(6);
            var title = Gtk.Label.New(titleText);
            title.SetXalign(0);
            title.AddCssClass("heading");
            var summary = Gtk.Label.New(summaryText);
            summary.SetXalign(0);
            summary.SetWrap(true);
            summary.AddCssClass("dim-label");
            item.Append(title);
            item.Append(summary);
            row.SetChild(item);
            return row;
        }
    }
}
