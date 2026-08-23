using DoomLauncher.DataSources;
using DoomLauncher.Interfaces;
using System;
using System.Linq;

namespace DoomLauncher.Linux
{
    public static class TagsDialog
    {
        public static void Show(Gtk.Window parent, Action changed)
        {
            var adapter = DataCache.Instance.DataSourceAdapter;
            var window = GtkUtil.ModalWindow(parent, "Tags", 480, 420);
            var list = Gtk.ListBox.New();
            list.AddCssClass("boxed-list");

            void reload()
            {
                GtkUtil.ClearList(list);
                DataCache.Instance.UpdateTags();
                foreach (var tag in DataCache.Instance.Tags)
                {
                    var row = Gtk.ListBoxRow.New();
                    row.Name = tag.TagID.ToString();
                    row.SetChild(Gtk.Label.New(tag.FavoriteName));
                    list.Append(row);
                }
            }
            reload();

            ITagData Selected()
            {
                var row = list.GetSelectedRow();
                if (row == null || !int.TryParse(row.Name, out int id))
                    return null;
                return DataCache.Instance.Tags.FirstOrDefault(x => x.TagID == id);
            }

            var add = GtkUtil.Button("Add...", () => TagEditDialog.Show(parent, null, () => { reload(); changed?.Invoke(); }));
            var edit = GtkUtil.Button("Edit...", () =>
            {
                var tag = Selected();
                if (tag != null)
                    TagEditDialog.Show(parent, tag, () => { reload(); changed?.Invoke(); });
            });
            var delete = GtkUtil.Button("Delete", () =>
            {
                var tag = Selected();
                if (tag == null)
                    return;
                GtkUtil.Confirm(window, "Delete", $"Delete tag {tag.Name}?", ok =>
                {
                    if (!ok)
                        return;
                    adapter.DeleteTagMapping(tag.TagID);
                    adapter.DeleteTag(tag);
                    DataCache.Instance.UpdateTags();
                    reload();
                    changed?.Invoke();
                });
            });

            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(12);
            box.SetMarginEnd(12);
            box.SetMarginTop(12);
            box.Append(GtkUtil.Scroll(list));
            box.Append(GtkUtil.DialogButtons(add, edit, delete, GtkUtil.Button("Close", () => window.Destroy())));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class TagEditDialog
    {
        public static void Show(Gtk.Window parent, ITagData existing, Action saved)
        {
            var adapter = DataCache.Instance.DataSourceAdapter;
            var window = GtkUtil.ModalWindow(parent, existing == null ? "New Tag" : "Edit Tag", 400, 360);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(16);
            box.SetMarginEnd(16);
            box.SetMarginTop(16);

            var name = Gtk.Entry.New();
            var hasTab = Gtk.CheckButton.NewWithLabel("Show as tab");
            var favorite = Gtk.CheckButton.NewWithLabel("Favorite");
            var exclude = Gtk.CheckButton.NewWithLabel("Exclude from other tabs");
            var hasColor = Gtk.CheckButton.NewWithLabel("Use color");
            var color = Gtk.Entry.New();
            color.SetPlaceholderText("ARGB integer or #RRGGBB");
            hasTab.SetActive(true);

            if (existing != null)
            {
                name.SetText(existing.Name ?? string.Empty);
                hasTab.SetActive(existing.HasTab);
                favorite.SetActive(existing.Favorite);
                exclude.SetActive(existing.ExcludeFromOtherTabs);
                hasColor.SetActive(existing.HasColor);
                if (existing.Color.HasValue)
                    color.SetText(existing.Color.Value.ToString());
            }

            box.Append(GtkUtil.LabeledRow("Name", name));
            box.Append(hasTab);
            box.Append(favorite);
            box.Append(exclude);
            box.Append(hasColor);
            box.Append(GtkUtil.LabeledRow("Color", color));

            var save = Gtk.Button.NewWithLabel("Save");
            save.AddCssClass("suggested-action");
            save.OnClicked += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(name.GetText()))
                    return;
                var tag = existing ?? new TagData();
                tag.Name = name.GetText();
                tag.HasTab = hasTab.GetActive();
                tag.Favorite = favorite.GetActive();
                tag.ExcludeFromOtherTabs = exclude.GetActive();
                tag.HasColor = hasColor.GetActive();
                if (int.TryParse(color.GetText(), out int argb))
                    tag.Color = argb;
                else if (color.GetText().StartsWith("#") && color.GetText().Length == 7)
                    tag.Color = Convert.ToInt32(color.GetText().TrimStart('#'), 16) | unchecked((int)0xFF000000);
                if (existing == null)
                    adapter.InsertTag(tag);
                else
                    adapter.UpdateTag(tag);
                DataCache.Instance.UpdateTags();
                saved?.Invoke();
                window.Destroy();
            };
            box.Append(GtkUtil.DialogButtons(GtkUtil.Button("Cancel", () => window.Destroy()), save));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class TagSelectDialog
    {
        public static void Show(Gtk.Window parent, Action<ITagData> chosen)
        {
            var window = GtkUtil.ModalWindow(parent, "Select Tag", 360, 400);
            var list = Gtk.ListBox.New();
            list.AddCssClass("boxed-list");
            foreach (var tag in DataCache.Instance.Tags)
            {
                var row = Gtk.ListBoxRow.New();
                row.Name = tag.TagID.ToString();
                row.SetChild(Gtk.Label.New(tag.FavoriteName));
                list.Append(row);
            }
            list.OnRowActivated += (s, e) =>
            {
                if (int.TryParse(e.Row.Name, out int id))
                {
                    var tag = DataCache.Instance.Tags.FirstOrDefault(x => x.TagID == id);
                    if (tag != null)
                        chosen(tag);
                    window.Destroy();
                }
            };
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(12);
            box.SetMarginEnd(12);
            box.SetMarginTop(12);
            box.Append(GtkUtil.Scroll(list));
            box.Append(GtkUtil.Button("Cancel", () => window.Destroy()));
            window.SetChild(box);
            window.Present();
        }
    }
}
