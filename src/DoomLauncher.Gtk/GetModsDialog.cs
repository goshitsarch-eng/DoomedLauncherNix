using DoomLauncher.DataSources;
using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DoomLauncher.Linux
{
    public static class GetModsDialog
    {
        public static void Show(Gtk.Window parent, IdGamesDataAdapater idGames, Action<IGameFileDownloadable, bool> download)
        {
            var window = GtkUtil.ModalWindow(parent, "Get mods", 720, 640);
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
            var intro = Gtk.Label.New("Search the Doomworld idgames archive or pick a well-known wad. Downloads are imported into your library; you can launch them as soon as they finish.");
            intro.SetWrap(true);
            intro.SetXalign(0);
            root.Append(intro);

            var search = Gtk.SearchEntry.New();
            search.SetPlaceholderText("Search idgames by title...");
            root.Append(search);

            var status = Gtk.Label.New("Pick a featured wad or search.");
            status.AddCssClass("dim-label");
            status.SetXalign(0);
            status.SetWrap(true);
            root.Append(status);

            var notebook = Gtk.Notebook.New();
            var featured = Gtk.ListBox.New();
            featured.AddCssClass("boxed-list");
            var results = Gtk.ListBox.New();
            results.AddCssClass("boxed-list");
            notebook.AppendPage(GtkUtil.Scroll(featured), Gtk.Label.New("Featured"));
            notebook.AppendPage(GtkUtil.Scroll(results), Gtk.Label.New("Search results"));
            notebook.Vexpand = true;
            root.Append(notebook);

            IGameFile selected = null;
            var resultFiles = new List<IGameFile>();

            void selectFromList(Gtk.ListBox list, List<IGameFile> files)
            {
                var row = list.GetSelectedRow();
                if (row == null || !int.TryParse(row.Name, out int index) || index < 0 || index >= files.Count)
                    selected = null;
                else
                    selected = files[index];
            }

            featured.OnRowSelected += (s, e) =>
            {
                selectFromList(featured, CuratedMods.All.Select(x => (IGameFile)null).ToList());
            };

            var curatedFiles = new List<IGameFile>();
            int i = 0;
            foreach (var mod in CuratedMods.All)
            {
                var row = Gtk.ListBoxRow.New();
                row.Name = i.ToString();
                var item = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
                item.SetMarginStart(8);
                item.SetMarginEnd(8);
                item.SetMarginTop(6);
                item.SetMarginBottom(6);
                var title = Gtk.Label.New($"{mod.Title}  ·  {mod.Group}");
                title.SetXalign(0);
                title.AddCssClass("heading");
                var summary = Gtk.Label.New(mod.Summary);
                summary.SetXalign(0);
                summary.SetWrap(true);
                summary.AddCssClass("dim-label");
                item.Append(title);
                item.Append(summary);
                row.SetChild(item);
                featured.Append(row);
                curatedFiles.Add(null);
                i++;
            }

            CuratedMod SelectedCurated()
            {
                var row = featured.GetSelectedRow();
                if (row == null || !int.TryParse(row.Name, out int index) || index < 0 || index >= CuratedMods.All.Length)
                    return null;
                return CuratedMods.All[index];
            }

            IGameFileDownloadable SelectedDownloadable()
            {
                if (notebook.GetCurrentPage() == 1)
                {
                    var row = results.GetSelectedRow();
                    if (row != null && int.TryParse(row.Name, out int index) && index >= 0 && index < resultFiles.Count)
                        return resultFiles[index] as IGameFileDownloadable;
                }
                return selected as IGameFileDownloadable;
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
                    GtkUtil.RunOnUi(() => fillResults(files, "No idgames results. Try a shorter title or the Id Games tab."));
                });
            }

            search.OnActivate += (s, e) =>
            {
                var text = search.GetText();
                if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 3)
                {
                    status.SetLabel("Type at least 3 characters to search.");
                    return;
                }
                runSearch(GameFileFieldType.Title, text.Trim());
            };

            var downloadBtn = Gtk.Button.NewWithLabel("Download");
            downloadBtn.AddCssClass("suggested-action");
            var playBtn = Gtk.Button.NewWithLabel("Download and play");
            var latestBtn = GtkUtil.Button("Latest uploads", () =>
            {
                status.SetLabel("Loading latest idgames uploads...");
                Task.Run(() =>
                {
                    var files = CuratedMods.Latest(idGames);
                    GtkUtil.RunOnUi(() => fillResults(files, "Could not load latest uploads."));
                });
            });

            void startDownload(bool play)
            {
                IGameFileDownloadable file = SelectedDownloadable();
                if (file == null)
                {
                    var curated = SelectedCurated();
                    if (curated == null)
                    {
                        GtkUtil.Alert(parent, "Select a mod", "Pick a featured wad or a search result first.");
                        return;
                    }
                    status.SetLabel($"Searching idgames for {curated.Title}...");
                    Task.Run(() =>
                    {
                        var files = CuratedMods.Search(idGames, curated).ToList();
                        GtkUtil.RunOnUi(() =>
                        {
                            if (files.Count == 0)
                            {
                                status.SetLabel($"No idgames match for {curated.Title}.");
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
                    return;
                }
                status.SetLabel($"Downloading {file.FileName}...");
                download?.Invoke(file, play);
            }

            downloadBtn.OnClicked += (s, e) => startDownload(false);
            playBtn.OnClicked += (s, e) => startDownload(true);

            var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            buttons.Append(latestBtn);
            buttons.Append(downloadBtn);
            buttons.Append(playBtn);
            root.Append(buttons);
            return root;
        }
    }
}
