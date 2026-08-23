using DoomLauncher.DataSources;
using DoomLauncher.Interfaces;
using System;
using System.Globalization;
using System.Linq;

namespace DoomLauncher.Linux
{
    public static class GameFileEditDialog
    {
        public static void Show(Gtk.Window parent, IGameFile[] files, Action saved)
        {
            if (files == null || files.Length == 0)
                return;
            var adapter = DataCache.Instance.DataSourceAdapter;
            var first = adapter.GetGameFile(files[0].FileName) ?? files[0];
            var window = GtkUtil.ModalWindow(parent, files.Length > 1 ? "*** Multiple Edit" : "Edit", 520, 560);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(16);
            box.SetMarginEnd(16);
            box.SetMarginTop(16);

            bool multi = files.Length > 1;
            var title = Field(box, "Title", first.Title, multi);
            var author = Field(box, "Author", first.Author, multi);
            var released = Field(box, "Release date", first.ReleaseDate?.ToString("yyyy-MM-dd") ?? string.Empty, multi);
            var description = Field(box, "Description", first.Description, multi, true);
            var comments = Field(box, "Comments", first.Comments, multi, true);
            var maps = Field(box, "Maps", first.Map, multi);
            var rating = Field(box, "Rating", first.Rating?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, multi);

            var iwads = Util.GetIWadsDataSource(adapter);
            var iwadDrop = GtkUtil.DropDownFrom(iwads.Select(x => x.Name).ToList());
            if (first.IWadID.HasValue)
            {
                var match = iwads.FirstOrDefault(x => x.IWadID == first.IWadID.Value);
                if (match != null)
                    GtkUtil.SetDropDownText(iwadDrop, match.Name);
            }
            box.Append(GtkUtil.LabeledRow("IWAD", iwadDrop));

            var ports = adapter.GetSourcePorts().ToList();
            var portDrop = GtkUtil.DropDownFrom(ports.Select(x => x.Name).ToList());
            if (first.SourcePortID.HasValue)
            {
                var match = ports.FirstOrDefault(x => x.SourcePortID == first.SourcePortID.Value);
                if (match != null)
                    GtkUtil.SetDropDownText(portDrop, match.Name);
            }
            box.Append(GtkUtil.LabeledRow("Source port", portDrop));

            var tagBox = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
            var tagChecks = DataCache.Instance.Tags.ToDictionary(t => t, t =>
            {
                var check = Gtk.CheckButton.NewWithLabel(t.FavoriteName);
                var existing = DataCache.Instance.TagMapLookup.GetTags(first);
                check.SetActive(existing.Contains(t));
                tagBox.Append(check);
                return check;
            });
            box.Append(Gtk.Label.New("Tags"));
            box.Append(GtkUtil.Scroll(tagBox));

            var save = Gtk.Button.NewWithLabel("Save");
            save.AddCssClass("suggested-action");
            save.OnClicked += (s, e) =>
            {
                foreach (var file in files)
                {
                    var target = adapter.GetGameFile(file.FileName) ?? file;
                    if (!multi || title.Use.GetActive())
                        target.Title = title.Entry.GetText();
                    if (!multi || author.Use.GetActive())
                        target.Author = author.Entry.GetText();
                    if (!multi || released.Use.GetActive())
                    {
                        if (DateTime.TryParse(released.Entry.GetText(), out var dt))
                            target.ReleaseDate = dt;
                    }
                    if (!multi || description.Use.GetActive())
                        target.Description = description.Entry.GetText();
                    if (!multi || comments.Use.GetActive())
                        target.Comments = comments.Entry.GetText();
                    if (!multi || maps.Use.GetActive())
                        target.Map = maps.Entry.GetText();
                    if (!multi || rating.Use.GetActive())
                    {
                        if (double.TryParse(rating.Entry.GetText(), NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
                            target.Rating = r;
                    }
                    var iwad = iwads.FirstOrDefault(x => x.Name == GtkUtil.GetDropDownText(iwadDrop));
                    if (iwad != null)
                        target.IWadID = iwad.IWadID;
                    var port = ports.FirstOrDefault(x => x.Name == GtkUtil.GetDropDownText(portDrop));
                    if (port != null)
                        target.SourcePortID = port.SourcePortID;
                    adapter.UpdateGameFile(target);
                    var selectedTags = tagChecks.Where(x => x.Value.GetActive()).Select(x => x.Key).ToArray();
                    DataCache.Instance.UpdateGameFileTags(new[] { target }, selectedTags);
                }
                saved?.Invoke();
                window.Destroy();
            };

            box.Append(GtkUtil.DialogButtons(GtkUtil.Button("Cancel", () => window.Destroy()), save));
            window.SetChild(GtkUtil.Scroll(box));
            window.Present();
        }

        private static (Gtk.Entry Entry, Gtk.CheckButton Use) Field(Gtk.Box box, string label, string value, bool multi, bool multiline = false)
        {
            var entry = Gtk.Entry.New();
            entry.SetText(value ?? string.Empty);
            var use = Gtk.CheckButton.NewWithLabel("Apply");
            use.SetActive(false);
            use.Visible = multi;
            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            var lbl = Gtk.Label.New(label);
            lbl.SetWidthChars(14);
            lbl.SetXalign(0);
            entry.Hexpand = true;
            row.Append(lbl);
            row.Append(entry);
            row.Append(use);
            box.Append(row);
            return (entry, use);
        }
    }
}
