using DoomLauncher.Config;
using DoomLauncher.SourcePort;
using GdkPixbuf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DoomLauncher.Linux
{
    public static class GtkUtil
    {
        public static void ApplyColorTheme(ColorThemeType theme)
        {
            try
            {
                var manager = Adw.StyleManager.GetDefault();
                manager.ColorScheme = theme switch
                {
                    ColorThemeType.Dark => Adw.ColorScheme.ForceDark,
                    ColorThemeType.Default => Adw.ColorScheme.ForceLight,
                    _ => Adw.ColorScheme.Default
                };
            }
            catch
            {
            }
        }

        public static void Alert(Gtk.Window parent, string title, string message)
        {
            var dialog = Adw.AlertDialog.New(title, message);
            dialog.AddResponse("ok", "OK");
            dialog.SetDefaultResponse("ok");
            dialog.SetCloseResponse("ok");
            dialog.Present(parent);
        }

        public static void Confirm(Gtk.Window parent, string title, string message, Action<bool> done)
        {
            var dialog = Adw.AlertDialog.New(title, message);
            dialog.AddResponse("cancel", "Cancel");
            dialog.AddResponse("ok", "OK");
            dialog.SetDefaultResponse("ok");
            dialog.SetCloseResponse("cancel");
            dialog.SetResponseAppearance("ok", Adw.ResponseAppearance.Suggested);
            dialog.OnResponse += (sender, args) => done?.Invoke(args.Response == "ok");
            dialog.Present(parent);
        }

        public static async void OpenFiles(Gtk.Window parent, string title, string[] patterns, Action<string[]> done)
        {
            var dialog = Gtk.FileDialog.New();
            dialog.SetTitle(title);
            if (patterns != null && patterns.Length > 0)
            {
                var filters = Gio.ListStore.New(Gtk.FileFilter.GetGType());
                var all = Gtk.FileFilter.New();
                all.SetName("Supported files");
                foreach (var pattern in patterns)
                    all.AddPattern(pattern.Contains('*') ? pattern : "*" + (pattern.StartsWith(".") ? pattern : "." + pattern));
                filters.Append(all);
                var star = Gtk.FileFilter.New();
                star.SetName("All files");
                star.AddPattern("*");
                filters.Append(star);
                dialog.SetFilters(filters);
            }

            try
            {
                var list = await dialog.OpenMultipleAsync(parent);
                done?.Invoke(FilesFromList(list));
            }
            catch
            {
                done?.Invoke(Array.Empty<string>());
            }
        }

        public static async void OpenFolder(Gtk.Window parent, string title, Action<string> done)
        {
            var dialog = Gtk.FileDialog.New();
            dialog.SetTitle(title);
            try
            {
                var file = await dialog.SelectFolderAsync(parent);
                done?.Invoke(file?.GetPath());
            }
            catch
            {
                done?.Invoke(null);
            }
        }

        public static async void SaveFile(Gtk.Window parent, string title, string suggested, Action<string> done)
        {
            var dialog = Gtk.FileDialog.New();
            dialog.SetTitle(title);
            if (!string.IsNullOrEmpty(suggested))
                dialog.SetInitialName(suggested);
            try
            {
                var file = await dialog.SaveAsync(parent);
                done?.Invoke(file?.GetPath());
            }
            catch
            {
                done?.Invoke(null);
            }
        }

        public static async void OpenFile(Gtk.Window parent, string title, Action<string> done)
        {
            var dialog = Gtk.FileDialog.New();
            dialog.SetTitle(title);
            try
            {
                var file = await dialog.OpenAsync(parent);
                done?.Invoke(file?.GetPath());
            }
            catch
            {
                done?.Invoke(null);
            }
        }

        private static string[] FilesFromList(Gio.ListModel list)
        {
            if (list == null)
                return Array.Empty<string>();
            var files = new List<string>();
            uint n = list.GetNItems();
            for (uint i = 0; i < n; i++)
            {
                nint ptr = list.GetItem(i);
                if (ptr == IntPtr.Zero)
                    continue;
                try
                {
                    var obj = GObject.Internal.InstanceWrapper.WrapHandle<GObject.Object>(ptr, false);
                    string path = (obj as Gio.File)?.GetPath();
                    if (string.IsNullOrEmpty(path) && obj is Gio.FileHelper helper)
                        path = helper.GetPath();
                    if (!string.IsNullOrEmpty(path))
                        files.Add(path);
                }
                catch
                {
                }
            }
            return files.ToArray();
        }

        public static Gtk.DropDown DropDownFrom(IList<string> items, int selected = 0)
        {
            var dropdown = Gtk.DropDown.NewFromStrings(items.ToArray());
            if (selected >= 0 && selected < items.Count)
                dropdown.SetSelected((uint)selected);
            return dropdown;
        }

        public static string GetDropDownText(Gtk.DropDown dropdown)
        {
            if (dropdown.SelectedItem is Gtk.StringObject so)
                return so.String;
            return null;
        }

        public static void SetDropDownText(Gtk.DropDown dropdown, string value)
        {
            if (dropdown.Model is Gtk.StringList list)
            {
                uint n = list.GetNItems();
                for (uint i = 0; i < n; i++)
                {
                    if (string.Equals(list.GetString(i), value, StringComparison.OrdinalIgnoreCase))
                    {
                        dropdown.SetSelected(i);
                        return;
                    }
                }
            }
        }

        public static Gtk.Box LabeledRow(string label, Gtk.Widget widget)
        {
            var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            var lbl = Gtk.Label.New(label);
            lbl.SetXalign(0);
            lbl.SetWidthChars(18);
            widget.Hexpand = true;
            box.Append(lbl);
            box.Append(widget);
            return box;
        }

        public static Gtk.ScrolledWindow Scroll(Gtk.Widget child)
        {
            var scroll = Gtk.ScrolledWindow.New();
            scroll.SetPolicy(Gtk.PolicyType.Automatic, Gtk.PolicyType.Automatic);
            scroll.SetChild(child);
            scroll.Hexpand = true;
            scroll.Vexpand = true;
            return scroll;
        }

        public static Gtk.Button Button(string label, Action clicked)
        {
            var button = Gtk.Button.NewWithLabel(label);
            button.OnClicked += (s, e) => clicked();
            return button;
        }

        public static void OpenPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                if (path.Contains("://"))
                {
                    Gio.Functions.AppInfoLaunchDefaultForUri(path, null);
                    return;
                }

                if (File.Exists(path) || Directory.Exists(path))
                {
                    Process.Start(SandboxHost.WrapForHost(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = SandboxHost.Quote(path),
                        UseShellExecute = false
                    }));
                    return;
                }

                Gio.Functions.AppInfoLaunchDefaultForUri(path.StartsWith("file:", StringComparison.Ordinal) ? path : "file://" + path, null);
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
                catch { }
            }
        }

        public static void ClearList(Gtk.ListBox list)
        {
            if (list == null)
                return;
            var child = list.GetFirstChild();
            while (child != null)
            {
                var next = child.GetNextSibling();
                // Only rows are managed children of the ListBox. Other widgets
                // (e.g. a Popover parented to the list for a context menu) must
                // not be passed to Remove(), which would emit
                // "Tried to remove non-child" warnings and leave them orphaned.
                if (child is Gtk.ListBoxRow)
                    list.Remove(child);
                child = next;
            }
        }

        public static void ClearFlow(Gtk.FlowBox flow)
        {
            if (flow == null)
                return;
            var child = flow.GetFirstChild();
            while (child != null)
            {
                var next = child.GetNextSibling();
                if (child is Gtk.FlowBoxChild)
                    flow.Remove(child);
                child = next;
            }
        }

        public static void SetPictureFromFile(Gtk.Picture picture, string path)
        {
            if (picture == null)
                return;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                picture.SetFile(null);
                return;
            }
            try
            {
                picture.SetFile(Gio.FileHelper.NewForPath(path));
            }
            catch
            {
            }
        }

        public static Pixbuf PixbufFromFile(string path, int maxWidth = 0, int maxHeight = 0)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                if (maxWidth > 0 && maxHeight > 0)
                    return Pixbuf.NewFromFileAtSize(path, maxWidth, maxHeight);
                return Pixbuf.NewFromFile(path);
            }
            catch
            {
                return null;
            }
        }

        public static Pixbuf PixbufFromImage(Image image, int maxWidth = 0, int maxHeight = 0)
        {
            if (image == null)
                return null;
            try
            {
                using var ms = new MemoryStream();
                image.Save(ms, new PngEncoder());
                ms.Position = 0;
                var loader = PixbufLoader.New();
                loader.Write(ms.ToArray());
                loader.Close();
                var pixbuf = loader.GetPixbuf();
                if (pixbuf != null && maxWidth > 0 && maxHeight > 0)
                    return pixbuf.ScaleSimple(maxWidth, maxHeight, InterpType.Bilinear);
                return pixbuf;
            }
            catch
            {
                return null;
            }
        }

        public static void RunOnUi(Action action)
        {
            GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
            {
                // An exception thrown inside a GLib callback tears the main loop down, so a
                // background continuation must never let one escape.
                try
                {
                    action();
                }
                catch
                {
                }
                return false;
            });
        }

        public static Task RunBackground(Action action)
        {
            return Task.Run(action);
        }

        /// <summary>
        /// Detects source ports on a worker thread. Detection runs <c>flatpak list</c>, scans the
        /// filesystem, and inside our own Flatpak makes a host round trip per lookup; doing that
        /// inline is what froze the window while it looked for GZDoom/UZDoom.
        /// </summary>
        public static void DetectPortsAsync(Action<IReadOnlyList<DetectedSourcePort>> found, bool refresh = false)
        {
            RunBackground(() =>
            {
                IReadOnlyList<DetectedSourcePort> detected;
                try
                {
                    if (refresh)
                        SourcePortLaunch.ClearCache();
                    detected = SourcePortDetector.Detect();
                }
                catch
                {
                    detected = Array.Empty<DetectedSourcePort>();
                }
                RunOnUi(() => found?.Invoke(detected));
            });
        }

        /// <summary>
        /// Detects off the UI thread, then writes the new ports and calls back on the UI thread so
        /// the database is only ever touched from one thread.
        /// </summary>
        public static void EnsureDetectedPortsAsync(Action<IReadOnlyList<ISourcePortData>> done, bool refresh = false)
        {
            DetectPortsAsync(detected =>
            {
                IReadOnlyList<ISourcePortData> added = Array.Empty<ISourcePortData>();
                try
                {
                    added = SourcePortSetup.EnsurePorts(DataCache.Instance.DataSourceAdapter, detected);
                }
                catch
                {
                }
                done?.Invoke(added);
            }, refresh);
        }

        public static Gtk.Window ModalWindow(Gtk.Window parent, string title, int width, int height)
        {
            var window = Gtk.Window.New();
            window.SetTitle(title);
            window.SetDefaultSize(width, height);
            window.SetModal(true);
            window.SetTransientFor(parent);
            window.SetResizable(true);
            return window;
        }

        // Adwaita window with no separate native titlebar. Use this (with SetContent)
        // for windows that embed their own Adw.HeaderBar, so they don't end up with a
        // second title bar and duplicate window controls stacked on top.
        public static Adw.Window AdwModalWindow(Gtk.Window parent, string title, int width, int height)
        {
            var window = Adw.Window.New();
            window.SetTitle(title);
            window.SetDefaultSize(width, height);
            window.SetModal(true);
            window.SetTransientFor(parent);
            window.SetResizable(true);
            return window;
        }

        public static Gtk.Box DialogButtons(params Gtk.Widget[] buttons)
        {
            var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            box.SetHalign(Gtk.Align.End);
            box.SetMarginTop(8);
            foreach (var button in buttons)
            {
                if (button != null)
                    box.Append(button);
            }
            return box;
        }
    }
}
