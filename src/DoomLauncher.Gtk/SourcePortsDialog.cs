using DoomLauncher.DataSources;
using DoomLauncher.Interfaces;
using System;
using System.Linq;

namespace DoomLauncher.Linux
{
    public static class SourcePortsDialog
    {
        public static void Show(Gtk.Window parent, SourcePortLaunchType type, Action changed)
        {
            var adapter = DataCache.Instance.DataSourceAdapter;
            var window = GtkUtil.ModalWindow(parent, type.ToString("g"), 560, 420);
            var list = Gtk.ListBox.New();
            list.AddCssClass("boxed-list");

            void reload()
            {
                while (list.GetFirstChild() != null)
                    list.Remove(list.GetFirstChild());
                var items = type switch
                {
                    SourcePortLaunchType.Utility => adapter.GetUtilities(),
                    SourcePortLaunchType.Doom64 => adapter.GetDoom64(),
                    _ => adapter.GetSourcePorts()
                };
                foreach (var port in items)
                {
                    var row = Gtk.ListBoxRow.New();
                    row.Name = port.SourcePortID.ToString();
                    row.SetChild(Gtk.Label.New($"{port.Name}  ({port.Executable})"));
                    list.Append(row);
                }
            }
            reload();

            ISourcePortData Selected()
            {
                var row = list.GetSelectedRow();
                if (row == null || !int.TryParse(row.Name, out int id))
                    return null;
                return adapter.GetSourcePort(id);
            }

            var add = GtkUtil.Button("Add...", () => SourcePortEditDialog.Show(parent, type, null, () => { reload(); changed?.Invoke(); }));
            var edit = GtkUtil.Button("Edit...", () =>
            {
                var port = Selected();
                if (port != null)
                    SourcePortEditDialog.Show(parent, type, port, () => { reload(); changed?.Invoke(); });
            });
            var delete = GtkUtil.Button("Delete", () =>
            {
                var port = Selected();
                if (port == null)
                    return;
                GtkUtil.Confirm(window, "Delete", $"Delete {port.Name}?", ok =>
                {
                    if (!ok)
                        return;
                    adapter.DeleteSourcePort(port);
                    reload();
                    changed?.Invoke();
                });
            });

            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(12);
            box.SetMarginEnd(12);
            box.SetMarginTop(12);
            box.SetMarginBottom(12);
            box.Append(GtkUtil.Scroll(list));
            box.Append(GtkUtil.DialogButtons(add, edit, delete, GtkUtil.Button("Close", () => window.Destroy())));
            window.SetChild(box);
            window.Present();
        }
    }

    public static class SourcePortEditDialog
    {
        public static void Show(Gtk.Window parent, SourcePortLaunchType type, ISourcePortData existing, Action saved)
        {
            var adapter = DataCache.Instance.DataSourceAdapter;
            var window = GtkUtil.ModalWindow(parent, existing == null ? "Add " + type : "Edit " + type, 520, 480);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(16);
            box.SetMarginEnd(16);
            box.SetMarginTop(16);

            var name = Gtk.Entry.New();
            var exec = Gtk.Entry.New();
            exec.SetPlaceholderText("gzdoom or /usr/bin/gzdoom");
            var directory = Gtk.Entry.New();
            var extensions = Gtk.Entry.New();
            var fileOption = Gtk.Entry.New();
            fileOption.SetText("-file");
            var extra = Gtk.Entry.New();
            var altSave = Gtk.Entry.New();
            var archived = Gtk.CheckButton.NewWithLabel("Archived");
            var additional = Gtk.Entry.New();
            additional.SetPlaceholderText("Additional files (semicolon-separated filenames)");

            if (type == SourcePortLaunchType.SourcePort)
                extensions.SetText(string.Join(",", new[] { ".wad" }.Union(Util.GetDehackedExtensions()).Union(Util.GetSourcePortPkExtensions())));
            else if (type == SourcePortLaunchType.Doom64)
                extensions.SetText(string.Join(",", new[] { ".wad" }.Union(Util.GetExtraDoom64Extensions())));
            else
                extensions.SetText(string.Join(",", Util.GetSourcePortPkExtensions()));

            if (existing != null)
            {
                name.SetText(existing.Name ?? string.Empty);
                exec.SetText(existing.Executable ?? string.Empty);
                directory.SetText(existing.Directory?.GetPossiblyRelativePath() ?? string.Empty);
                extensions.SetText(existing.SupportedExtensions ?? string.Empty);
                fileOption.SetText(existing.FileOption ?? "-file");
                extra.SetText(existing.ExtraParameters ?? string.Empty);
                altSave.SetText(existing.AltSaveDirectory?.GetPossiblyRelativePath() ?? string.Empty);
                archived.SetActive(existing.Archived);
                additional.SetText(existing.SettingsFiles ?? string.Empty);
            }

            var browse = GtkUtil.Button("Browse executable...", () => GtkUtil.OpenFile(window, "Select executable", path =>
            {
                if (string.IsNullOrEmpty(path))
                    return;
                exec.SetText(System.IO.Path.GetFileName(path));
                directory.SetText(System.IO.Path.GetDirectoryName(path) ?? string.Empty);
            }));

            box.Append(GtkUtil.LabeledRow("Name", name));
            box.Append(GtkUtil.LabeledRow("Executable", exec));
            box.Append(browse);
            box.Append(GtkUtil.LabeledRow("Directory", directory));
            box.Append(GtkUtil.LabeledRow("Extensions", extensions));
            box.Append(GtkUtil.LabeledRow("File option", fileOption));
            box.Append(GtkUtil.LabeledRow("Extra params", extra));
            box.Append(GtkUtil.LabeledRow("Alt save dir", altSave));
            box.Append(archived);
            box.Append(GtkUtil.LabeledRow("Additional files", additional));

            var save = Gtk.Button.NewWithLabel("Save");
            save.AddCssClass("suggested-action");
            save.OnClicked += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(name.GetText()) || string.IsNullOrWhiteSpace(exec.GetText()))
                {
                    GtkUtil.Alert(window, "Required", "Name and executable are required.");
                    return;
                }
                var port = existing ?? new SourcePortData { LaunchType = type };
                port.Name = name.GetText();
                port.Executable = exec.GetText();
                port.Directory = new LauncherPath(directory.GetText());
                port.SupportedExtensions = extensions.GetText();
                port.FileOption = fileOption.GetText();
                port.ExtraParameters = extra.GetText();
                port.AltSaveDirectory = new LauncherPath(altSave.GetText());
                port.Archived = archived.GetActive();
                port.LaunchType = type;
                port.SettingsFiles = additional.GetText();
                if (existing == null)
                    adapter.InsertSourcePort(port);
                else
                    adapter.UpdateSourcePort(port);
                saved?.Invoke();
                window.Destroy();
            };

            box.Append(GtkUtil.DialogButtons(GtkUtil.Button("Cancel", () => window.Destroy()), save));
            window.SetChild(GtkUtil.Scroll(box));
            window.Present();
        }
    }
}
