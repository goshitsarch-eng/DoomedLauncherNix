using DoomLauncher.DataSources;
using DoomLauncher.Handlers.Sync;
using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DoomLauncher.Linux
{
    public static class StatsDialog
    {
        public static void Show(Gtk.Window parent, string title, IEnumerable<IGameFile> gameFiles, bool global)
        {
            var files = gameFiles?.Where(x => x != null).ToList() ?? new List<IGameFile>();
            var stats = DataCache.Instance.DataSourceAdapter.GetStats(files).ToList();
            int statsMinutes = stats.Sum(x => (int)(x.LevelTime / 60.0));
            int launchMinutes = files.Sum(x => x.MinutesPlayed);
            int kills = stats.Sum(x => x.KillCount);
            int totalKills = stats.Sum(x => x.TotalKills);
            int secrets = stats.Sum(x => x.SecretCount);
            int items = stats.Sum(x => x.ItemCount);

            var window = GtkUtil.ModalWindow(parent, $"Cumulative Stats - {title}", 420, 320);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(16);
            box.SetMarginEnd(16);
            box.SetMarginTop(16);
            box.Append(Gtk.Label.New($"Note: Search filters apply ({files.Count} Files)"));
            box.Append(Gtk.Label.New($"Time launched: {Util.GetTimePlayedString(launchMinutes)}"));
            box.Append(Gtk.Label.New($"Time played (stats): {Util.GetTimePlayedString(statsMinutes)}"));
            box.Append(Gtk.Label.New($"Kills: {kills}/{totalKills}"));
            box.Append(Gtk.Label.New($"Secrets: {secrets}"));
            box.Append(Gtk.Label.New($"Items: {items}"));
            box.Append(GtkUtil.Button("Close", () => window.Destroy()));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class PlayRandomDialog
    {
        public static void Show(Gtk.Window parent, IList<IGameFile> currentTab, Action<IGameFile, string, bool> play)
        {
            var window = GtkUtil.ModalWindow(parent, "Play Random", 420, 280);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(16);
            box.SetMarginEnd(16);
            box.SetMarginTop(16);
            var types = new[] { "Any", "Unplayed", "Unrated", "Current Tab" };
            var typeDrop = GtkUtil.DropDownFrom(types);
            var showDialog = Gtk.CheckButton.NewWithLabel("Show play dialog");
            showDialog.SetActive(DataCache.Instance.AppConfiguration.ShowPlayDialog);
            var preview = Gtk.Label.New(string.Empty);
            preview.SetWrap(true);
            IGameFile generated = null;
            string map = null;

            void generate()
            {
                var adapter = DataCache.Instance.DataSourceAdapter;
                IEnumerable<IGameFile> files = typeDrop.GetSelected() switch
                {
                    1 => adapter.GetGameFiles().Where(x => !x.LastPlayed.HasValue),
                    2 => adapter.GetGameFiles().Where(x => !x.Rating.HasValue),
                    3 => currentTab,
                    _ => adapter.GetGameFiles()
                };
                var list = files.Where(x => x != null).ToList();
                if (list.Count == 0)
                {
                    preview.SetLabel("No files found.");
                    generated = null;
                    return;
                }
                generated = list[Random.Shared.Next(list.Count)];
                var maps = string.IsNullOrEmpty(generated.Map) ? Array.Empty<string>() : GameFile.GetMaps(generated);
                map = maps.Length > 0 ? maps[Random.Shared.Next(maps.Length)] : null;
                preview.SetLabel($"{generated.Title}\n{generated.FileNameNoPath}\nMap: {map}");
            }

            box.Append(GtkUtil.LabeledRow("Type", typeDrop));
            box.Append(showDialog);
            box.Append(preview);
            box.Append(GtkUtil.Button("Generate", generate));
            var ok = Gtk.Button.NewWithLabel("Play");
            ok.AddCssClass("suggested-action");
            ok.OnClicked += (s, e) =>
            {
                if (generated == null)
                    generate();
                if (generated != null)
                    play(generated, map, showDialog.GetActive());
                window.Destroy();
            };
            box.Append(GtkUtil.DialogButtons(GtkUtil.Button("Cancel", () => window.Destroy()), ok));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class TxtGeneratorDialog
    {
        public static void Show(Gtk.Window parent, IGameFile gameFile)
        {
            var adapter = DataCache.Instance.DataSourceAdapter;
            var request = TxtFileGenerator.FromGameFile(gameFile, adapter);
            var window = GtkUtil.ModalWindow(parent, "Generate Text File", 560, 700);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
            box.SetMarginStart(16);
            box.SetMarginEnd(16);
            box.SetMarginTop(16);

            Gtk.Entry E(string label, string value)
            {
                var entry = Gtk.Entry.New();
                entry.SetText(value ?? string.Empty);
                box.Append(GtkUtil.LabeledRow(label, entry));
                return entry;
            }

            var title = E("Title", request.Title);
            var filename = E("Filename", request.Filename);
            var author = E("Author", request.Author);
            var email = E("Email", request.Email);
            var desc = E("Description", request.Description);
            var maps = E("Maps", request.Maps);
            var engine = E("Engine", request.Engine);
            var game = E("Game", request.Game);
            var purpose = E("Purpose", request.PrimaryPurpose);
            var sounds = Gtk.CheckButton.NewWithLabel("Sounds");
            var music = Gtk.CheckButton.NewWithLabel("Music");
            var graphics = Gtk.CheckButton.NewWithLabel("Graphics");
            var deh = Gtk.CheckButton.NewWithLabel("Dehacked");
            var demos = Gtk.CheckButton.NewWithLabel("Demos");
            var mayMod = Gtk.CheckButton.NewWithLabel("Authors MAY modify");
            var mayDist = Gtk.CheckButton.NewWithLabel("MAY distribute");
            mayDist.SetActive(true);
            box.Append(sounds);
            box.Append(music);
            box.Append(graphics);
            box.Append(deh);
            box.Append(demos);
            box.Append(mayMod);
            box.Append(mayDist);

            var generate = Gtk.Button.NewWithLabel("Generate...");
            generate.AddCssClass("suggested-action");
            generate.OnClicked += (s, e) =>
            {
                request.Title = title.GetText();
                request.Filename = filename.GetText();
                request.Author = author.GetText();
                request.Email = email.GetText();
                request.Description = desc.GetText();
                request.Maps = maps.GetText();
                request.Engine = engine.GetText();
                request.Game = game.GetText();
                request.PrimaryPurpose = purpose.GetText();
                request.Sounds = sounds.GetActive();
                request.Music = music.GetActive();
                request.Graphics = graphics.GetActive();
                request.Dehacked = deh.GetActive();
                request.Demos = demos.GetActive();
                request.MayModify = mayMod.GetActive();
                request.MayDistribute = mayDist.GetActive();
                var text = TxtFileGenerator.Generate(request);
                GtkUtil.SaveFile(window, "Save text file", Path.GetFileNameWithoutExtension(request.Filename) + ".txt", path =>
                {
                    if (!string.IsNullOrEmpty(path))
                        File.WriteAllText(path, text);
                });
            };
            box.Append(GtkUtil.DialogButtons(GtkUtil.Button("Close", () => window.Destroy()), generate));
            window.SetChild(GtkUtil.Scroll(box));
            window.Present();
        }
    }

    public static class SpecificFilesDialog
    {
        public static void Show(Gtk.Window parent, IGameFile gameFile, Action<string[]> chosen)
        {
            var ops = new LibraryOperations();
            var entries = ops.ListArchiveEntries(gameFile);
            var window = GtkUtil.ModalWindow(parent, "Specific Files", 480, 420);
            var list = Gtk.ListBox.New();
            list.SetSelectionMode(Gtk.SelectionMode.Multiple);
            foreach (var entry in entries)
            {
                var row = Gtk.ListBoxRow.New();
                row.SetChild(Gtk.CheckButton.NewWithLabel(entry));
                list.Append(row);
            }
            var ok = Gtk.Button.NewWithLabel("OK");
            ok.OnClicked += (s, e) =>
            {
                var selected = new List<string>();
                for (var child = list.GetFirstChild(); child != null; child = child.GetNextSibling())
                {
                    if (child is Gtk.ListBoxRow row && row.GetChild() is Gtk.CheckButton check && check.GetActive())
                        selected.Add(check.GetLabel());
                }
                chosen(selected.ToArray());
                window.Destroy();
            };
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(12);
            box.SetMarginEnd(12);
            box.SetMarginTop(12);
            box.Append(GtkUtil.Scroll(list));
            box.Append(GtkUtil.DialogButtons(GtkUtil.Button("Cancel", () => window.Destroy()), ok));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class FileSelectDialog
    {
        public static void Show(Gtk.Window parent, Action<IGameFile> chosen)
        {
            var window = GtkUtil.ModalWindow(parent, "Select File", 480, 420);
            var search = Gtk.SearchEntry.New();
            var list = Gtk.ListBox.New();
            list.AddCssClass("boxed-list");
            var files = DataCache.Instance.DataSourceAdapter.GetGameFiles().ToList();
            void render(string text)
            {
                while (list.GetFirstChild() != null)
                    list.Remove(list.GetFirstChild());
                foreach (var file in files.Where(x => string.IsNullOrEmpty(text) ||
                    (x.Title ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    (x.FileName ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase)))
                {
                    var row = Gtk.ListBoxRow.New();
                    row.Name = file.GameFileID?.ToString() ?? file.FileName;
                    row.SetChild(Gtk.Label.New(string.IsNullOrEmpty(file.Title) ? file.FileNameNoPath : file.Title));
                    list.Append(row);
                }
            }
            render(null);
            search.OnSearchChanged += (s, e) => render(search.GetText());
            list.OnRowActivated += (s, e) =>
            {
                var file = files.FirstOrDefault(x => (x.GameFileID?.ToString() ?? x.FileName) == e.Row.Name);
                if (file != null)
                    chosen(file);
                window.Destroy();
            };
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(12);
            box.SetMarginEnd(12);
            box.SetMarginTop(12);
            box.Append(search);
            box.Append(GtkUtil.Scroll(list));
            box.Append(GtkUtil.Button("Cancel", () => window.Destroy()));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class SimpleSelectDialog
    {
        public static void Show(Gtk.Window parent, string title, IEnumerable<string> items, Action<string> chosen)
        {
            var window = GtkUtil.ModalWindow(parent, title, 360, 320);
            var list = Gtk.ListBox.New();
            foreach (var item in items)
            {
                var row = Gtk.ListBoxRow.New();
                row.SetChild(Gtk.Label.New(item));
                row.Name = item;
                list.Append(row);
            }
            list.OnRowActivated += (s, e) =>
            {
                chosen(e.Row.Name);
                window.Destroy();
            };
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(12);
            box.SetMarginEnd(12);
            box.SetMarginTop(12);
            box.Append(GtkUtil.Scroll(list));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class TextPromptDialog
    {
        public static void Show(Gtk.Window parent, string title, string initial, Action<string> done)
        {
            var window = GtkUtil.ModalWindow(parent, title, 360, 140);
            var entry = Gtk.Entry.New();
            entry.SetText(initial ?? string.Empty);
            var ok = Gtk.Button.NewWithLabel("OK");
            ok.AddCssClass("suggested-action");
            ok.OnClicked += (s, e) =>
            {
                done(entry.GetText());
                window.Destroy();
            };
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(16);
            box.SetMarginEnd(16);
            box.SetMarginTop(16);
            box.Append(entry);
            box.Append(GtkUtil.DialogButtons(GtkUtil.Button("Cancel", () => window.Destroy()), ok));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class FileManagementDialog
    {
        public static void Show(Gtk.Window parent, Action<FileManagement> chosen)
        {
            var window = GtkUtil.ModalWindow(parent, "File Management", 420, 200);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(16);
            box.SetMarginEnd(16);
            box.SetMarginTop(16);
            box.Append(Gtk.Label.New("Copy files into the Doom Launcher library, or keep them unmanaged at their current location?"));
            box.Append(GtkUtil.Button("Managed (copy)", () => { chosen(FileManagement.Managed); window.Destroy(); }));
            box.Append(GtkUtil.Button("Unmanaged (link)", () => { chosen(FileManagement.Unmanaged); window.Destroy(); }));
            box.Append(GtkUtil.Button("Cancel", () => window.Destroy()));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class ScreenshotViewerDialog
    {
        public static void Show(Gtk.Window parent, string path, IGameFile gameFile)
        {
            var window = GtkUtil.ModalWindow(parent, string.IsNullOrEmpty(gameFile?.Title) ? Path.GetFileName(path) : gameFile.Title, 720, 540);
            var picture = Gtk.Picture.New();
            picture.SetContentFit(Gtk.ContentFit.Contain);
            GtkUtil.SetPictureFromFile(picture, path);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.Append(picture);
            picture.Hexpand = true;
            picture.Vexpand = true;
            var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            buttons.SetHalign(Gtk.Align.End);
            buttons.Append(GtkUtil.Button("Open externally", () => GtkUtil.OpenPath(path)));
            buttons.Append(GtkUtil.Button("Close", () => window.Destroy()));
            box.Append(buttons);
            window.SetChild(box);
            window.Present();
        }
    }

    public static class WelcomeDialog
    {
        public static void Show(Gtk.Window parent)
        {
            var window = GtkUtil.ModalWindow(parent, "Welcome", 480, 260);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
            box.SetMarginStart(20);
            box.SetMarginEnd(20);
            box.SetMarginTop(20);
            var title = Gtk.Label.New("Welcome to Doom Launcher");
            title.AddCssClass("title-2");
            box.Append(title);
            var info = Gtk.Label.New("If this is your first time using Doom Launcher it is recommended to view the help document, then add a source port and IWADs.");
            info.SetWrap(true);
            box.Append(info);
            box.Append(GtkUtil.Button("View help", () => GtkUtil.OpenPath(Path.Combine(AppContext.BaseDirectory, "Help.pdf"))));
            box.Append(GtkUtil.Button("Continue", () => window.Destroy()));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class SyncStatusDialog
    {
        public static void Show(Gtk.Window parent, SyncResult result)
        {
            var window = GtkUtil.ModalWindow(parent, "Sync Status", 480, 320);
            var view = Gtk.TextView.New();
            view.SetEditable(false);
            var buf = view.GetBuffer();
            var lines = result.InvalidFiles.Select(x => $"{x.FileName}: {x.Reason}");
            buf.SetText(string.Join("\n", lines), -1);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(12);
            box.SetMarginEnd(12);
            box.SetMarginTop(12);
            box.Append(GtkUtil.Scroll(view));
            box.Append(GtkUtil.Button("Close", () => window.Destroy()));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class MetadataDialog
    {
        public static void Show(Gtk.Window parent, IGameFile[] localFiles, IdGamesDataAdapater idGames, Action saved)
        {
            var window = GtkUtil.ModalWindow(parent, "Update metadata", 520, 420);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(16);
            box.SetMarginEnd(16);
            box.SetMarginTop(16);
            var status = Gtk.Label.New("Searching idgames...");
            box.Append(status);
            window.SetChild(box);
            window.Present();

            System.Threading.Tasks.Task.Run(() =>
            {
                var adapter = DataCache.Instance.DataSourceAdapter;
                var ops = new LibraryOperations();
                int updated = 0;
                foreach (var local in localFiles)
                {
                    var remote = ops.SearchIdGamesMetadata(local, idGames).FirstOrDefault();
                    if (remote == null)
                        continue;
                    local.Title = remote.Title;
                    local.Author = remote.Author;
                    local.Description = remote.Description;
                    local.ReleaseDate = remote.ReleaseDate;
                    local.Rating = remote.Rating;
                    adapter.UpdateGameFile(local, new[]
                    {
                        GameFileFieldType.Title, GameFileFieldType.Author, GameFileFieldType.Description,
                        GameFileFieldType.ReleaseDate, GameFileFieldType.Rating
                    });
                    updated++;
                }
                GtkUtil.RunOnUi(() =>
                {
                    status.SetLabel($"Updated {updated} file(s) from idgames.");
                    box.Append(GtkUtil.Button("Close", () =>
                    {
                        saved?.Invoke();
                        window.Destroy();
                    }));
                });
            });
        }
    }

    public static class ScreenshotEditDialog
    {
        public static void Show(Gtk.Window parent, IFileData file, Action saved)
        {
            var window = GtkUtil.ModalWindow(parent, "Edit file details", 400, 280);
            var title = Gtk.Entry.New();
            title.SetText(file.UserTitle ?? string.Empty);
            var desc = Gtk.Entry.New();
            desc.SetText(file.UserDescription ?? string.Empty);
            var map = Gtk.Entry.New();
            map.SetText(file.Map ?? string.Empty);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(16);
            box.SetMarginEnd(16);
            box.SetMarginTop(16);
            box.Append(GtkUtil.LabeledRow("Title", title));
            box.Append(GtkUtil.LabeledRow("Description", desc));
            box.Append(GtkUtil.LabeledRow("Map", map));
            var save = Gtk.Button.NewWithLabel("Save");
            save.OnClicked += (s, e) =>
            {
                file.UserTitle = title.GetText();
                file.UserDescription = desc.GetText();
                file.Map = map.GetText();
                saved();
                window.Destroy();
            };
            box.Append(GtkUtil.DialogButtons(GtkUtil.Button("Cancel", () => window.Destroy()), save));
            window.SetChild(box);
            window.Present();
        }
    }
}
