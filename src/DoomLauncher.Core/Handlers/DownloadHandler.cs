using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace DoomLauncher
{
    public class DownloadHandler
    {
        private readonly List<IGameFileDownloadable> m_currentDownloads = new List<IGameFileDownloadable>();

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

                    if (DownloadView != null)
                        DownloadView.AddDownload(dlItem, dlItem.FileName);

                    dlItem.Download(adapter, Path.Combine(DownloadDirectory.GetFullPath(), dlItem.FileName));
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
            if (DownloadView != null && dlItem != null)
            {
                DownloadView.UpdateDownload(sender, string.Format("{0} ({1})", dlItem.FileName, 
                    e.Cancelled ? "Cancelled" : "Complete"));

                m_currentDownloads.Remove(dlItem);
            }
        }

        public IDownloadView DownloadView { get; set; }
        public LauncherPath DownloadDirectory { get; set; }
    }
}
