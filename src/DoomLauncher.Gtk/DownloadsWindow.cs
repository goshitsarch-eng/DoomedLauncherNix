using System;
using System.Collections.Generic;
using System.Linq;

namespace DoomLauncher.Linux
{
    public class DownloadsWindow : IDownloadView
    {
        public event EventHandler DownloadCancelled;
        public event EventHandler UserPlay;
        public event EventHandler DownloadFinished;

        public Gtk.Window Native { get; }
        public void Present() => Native.Present();
        public static implicit operator Gtk.Window(DownloadsWindow w) => w.Native;

        private readonly Gtk.ListBox m_list = Gtk.ListBox.New();
        private readonly Dictionary<object, Gtk.ProgressBar> m_bars = new Dictionary<object, Gtk.ProgressBar>();
        private readonly Dictionary<object, Gtk.Label> m_labels = new Dictionary<object, Gtk.Label>();
        private readonly HashSet<object> m_cancelled = new HashSet<object>();

        public DownloadsWindow(Gtk.Window parent)
        {
            Native = Gtk.Window.New();
            Native.SetTitle("Downloads");
            Native.SetDefaultSize(420, 320);
            Native.SetTransientFor(parent);
            Native.SetHideOnClose(true);

            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            box.SetMarginStart(12);
            box.SetMarginEnd(12);
            box.SetMarginTop(12);
            m_list.AddCssClass("boxed-list");
            box.Append(GtkUtil.Scroll(m_list));
            var play = GtkUtil.Button("Play downloaded", () => UserPlay?.Invoke(this, EventArgs.Empty));
            var cancel = GtkUtil.Button("Cancel selected", () =>
            {
                var row = m_list.GetSelectedRow();
                if (row != null)
                {
                    foreach (var kv in m_labels)
                    {
                        if (kv.Value.GetParent()?.GetParent() == row)
                            m_cancelled.Add(kv.Key);
                    }
                }
                DownloadCancelled?.Invoke(this, EventArgs.Empty);
            });
            box.Append(GtkUtil.DialogButtons(cancel, play, GtkUtil.Button("Close", () => Native.Close())));
            Native.SetChild(box);
        }

        public void AddDownload(object key, string text)
        {
            GtkUtil.RunOnUi(() =>
            {
                var row = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
                var label = Gtk.Label.New(text);
                label.SetXalign(0);
                var bar = Gtk.ProgressBar.New();
                row.Append(label);
                row.Append(bar);
                var listRow = Gtk.ListBoxRow.New();
                listRow.SetChild(row);
                m_list.Append(listRow);
                m_bars[key] = bar;
                m_labels[key] = label;
            });
        }

        public void UpdateDownload(object key, int progressPercentage)
        {
            GtkUtil.RunOnUi(() =>
            {
                if (m_bars.TryGetValue(key, out var bar))
                    bar.Fraction = Math.Clamp(progressPercentage / 100.0, 0, 1);
                if (progressPercentage >= 100)
                    DownloadFinished?.Invoke(this, EventArgs.Empty);
            });
        }

        public void UpdateDownload(object key, string text)
        {
            GtkUtil.RunOnUi(() =>
            {
                if (m_labels.TryGetValue(key, out var label))
                    label.SetLabel(text);
            });
        }

        public IEnumerable<object> GetCancelledDownloads() => m_cancelled.ToArray();
    }
}
