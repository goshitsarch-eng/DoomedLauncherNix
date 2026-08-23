using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace DoomLauncher
{
    public class DownloadItemCompletedEventArgs : EventArgs
    {
        public IGameFileDownloadable Item { get; }
        public string FilePath { get; }
        public bool Cancelled { get; }
        public Exception Error { get; }

        public DownloadItemCompletedEventArgs(IGameFileDownloadable item, string filePath, bool cancelled, Exception error)
        {
            Item = item;
            FilePath = filePath;
            Cancelled = cancelled;
            Error = error;
        }
    }

    public class DownloadHandler
    {
        public event EventHandler<DownloadItemCompletedEventArgs> ItemDownloadCompleted;

        private readonly List<IGameFileDownloadable> m_currentDownloads = new List<IGameFileDownloadable>();
        private readonly Dictionary<IGameFileDownloadable, string> m_paths = new Dictionary<IGameFileDownloadable, string>();

        public DownloadHandler(LauncherPath downloadDirectory, IDownloadView view)
        {
            DownloadDirectory = downloadDirectory;
            DownloadView = view;
            if (view != null)
                view.DownloadCancelled += view_DownloadCancelled;
        }

        public bool IsDownloading(IGameFileDownloadable dlItem)
        {
            return m_currentDownloads.Contains(dlItem);
        }

        void view_DownloadCancelled(object sender, EventArgs e)
        {
            var cancelled = DownloadView.GetCancelledDownloads();
            foreach (object obj in cancelled)
            {
                IGameFileDownloadable dlItem = obj as IGameFileDownloadable;
                if (dlItem != null)
                {
                    dlItem.Cancel();
                    m_currentDownloads.Remove(dlItem);
                }
            }
        }

        public void Download(IGameFileDataSourceAdapter adapter, IGameFileDownloadable dlItem)
        {
            if (dlItem != null && !IsDownloading(dlItem))
            {
                try
                {
                    m_currentDownloads.Add(dlItem);
                    dlItem.DownloadProgressChanged += dlItem_DownloadProgressChanged;
                    dlItem.DownloadCompleted += dlItem_DownloadCompleted;

                    string dest = Path.Combine(DownloadDirectory.GetFullPath(), dlItem.FileName);
                    m_paths[dlItem] = dest;

                    if (DownloadView != null)
                        DownloadView.AddDownload(dlItem, dlItem.FileName);

                    dlItem.Download(adapter, dest);
                }
                catch
                {
                }
            }
        }

        private DateTime m_dtLastDowanloadUpdate = DateTime.Now;

        void dlItem_DownloadProgressChanged(object sender, System.Net.DownloadProgressChangedEventArgs e)
        {
            IGameFileDownloadable dlItem = sender as IGameFileDownloadable;

            if (DownloadView != null && dlItem != null && 
                (e.ProgressPercentage == 100 || DateTime.Now.Subtract(m_dtLastDowanloadUpdate).TotalMilliseconds > 400))
            {
                m_dtLastDowanloadUpdate = DateTime.Now;
                DownloadView.UpdateDownload(sender, e.ProgressPercentage);
                DownloadView.UpdateDownload(sender, string.Format("{0} - {1}/{2}MB", dlItem.FileName, 
                    Math.Round(e.BytesReceived / 1024.0 / 1024.0, 1), Math.Round(e.TotalBytesToReceive / 1024.0 / 1024.0, 1)));
            }
        }

        void dlItem_DownloadCompleted(object sender, AsyncCompletedEventArgs e)
        {
            IGameFileDownloadable dlItem = sender as IGameFileDownloadable;
            if (dlItem == null)
                return;

            m_paths.TryGetValue(dlItem, out string path);
            m_paths.Remove(dlItem);
            m_currentDownloads.Remove(dlItem);

            if (DownloadView != null)
            {
                DownloadView.UpdateDownload(sender, string.Format("{0} ({1})", dlItem.FileName,
                    e.Cancelled ? "Cancelled" : "Complete"));
            }

            ItemDownloadCompleted?.Invoke(this, new DownloadItemCompletedEventArgs(dlItem, path, e.Cancelled, e.Error));
        }

        public IDownloadView DownloadView { get; set; }
        public LauncherPath DownloadDirectory { get; set; }
    }
}
