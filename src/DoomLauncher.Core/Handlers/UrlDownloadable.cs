using DoomLauncher.Interfaces;
using System;
using System.ComponentModel;
using System.Net;

namespace DoomLauncher
{
    public class UrlDownloadable : IGameFileDownloadable, IDisposable
    {
        public event DownloadProgressChangedEventHandler DownloadProgressChanged;
        public event AsyncCompletedEventHandler DownloadCompleted;

        private WebClient m_webClient;

        public UrlDownloadable(string url, string fileName)
        {
            Url = url;
            FileName = string.IsNullOrEmpty(fileName) ? "download.zip" : fileName;
        }

        public string Url { get; }
        public string FileName { get; set; }
        public int FileSizeBytes { get; set; }

        public void Download(IGameFileDataSourceAdapter adapter, string dlFilename)
        {
            m_webClient = new WebClient();
            m_webClient.Headers[HttpRequestHeader.UserAgent] = "DoomLauncher";
            m_webClient.DownloadProgressChanged += (s, e) => DownloadProgressChanged?.Invoke(this, e);
            m_webClient.DownloadFileCompleted += (s, e) =>
            {
                DownloadCompleted?.Invoke(this, e);
                Dispose();
            };
            m_webClient.DownloadFileAsync(new Uri(Url), dlFilename);
        }

        public void Cancel()
        {
            try { m_webClient?.CancelAsync(); }
            catch { }
        }

        public void Dispose()
        {
            m_webClient?.Dispose();
            m_webClient = null;
        }
    }
}
