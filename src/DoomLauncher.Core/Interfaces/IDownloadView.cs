using System.Collections.Generic;

namespace DoomLauncher
{
    public interface IDownloadView
    {
        event System.EventHandler DownloadCancelled;
        event System.EventHandler UserPlay;

        void AddDownload(object key, string text);
        void UpdateDownload(object key, int progressPercentage);
        void UpdateDownload(object key, string text);
        IEnumerable<object> GetCancelledDownloads();
    }
}
