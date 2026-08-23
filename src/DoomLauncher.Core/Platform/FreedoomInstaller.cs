using Octokit;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DoomLauncher
{
    public class FreedoomInstallResult
    {
        public bool Succeeded { get; set; }
        public string Error { get; set; }
        public string[] WadPaths { get; set; } = Array.Empty<string>();
    }

    public static class FreedoomInstaller
    {
        public const string FallbackUrl = "https://github.com/freedoom/freedoom/releases/download/v0.13.0/freedoom-0.13.0.zip";

        public static async Task<FreedoomInstallResult> InstallAsync(string destinationDirectory, IProgress<string> progress, CancellationToken cancellationToken)
        {
            var result = new FreedoomInstallResult();
            if (string.IsNullOrEmpty(destinationDirectory))
            {
                result.Error = "No destination directory.";
                return result;
            }

            try
            {
                Directory.CreateDirectory(destinationDirectory);
                progress?.Report("Finding the latest Freedoom release...");
                string url = await ResolveDownloadUrl(cancellationToken).ConfigureAwait(false);
                progress?.Report("Downloading Freedoom...");

                string zipPath = Path.Combine(destinationDirectory, "freedoom-download.zip");
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("DoomLauncher");
                    using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = File.Create(zipPath))
                            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    }
                }

                progress?.Report("Extracting WADs...");
                string extractDir = Path.Combine(destinationDirectory, "freedoom-extract");
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                var wads = Directory.GetFiles(extractDir, "*.wad", SearchOption.AllDirectories)
                    .Where(IsFreedoomWad)
                    .ToArray();
                if (wads.Length == 0)
                {
                    result.Error = "The Freedoom archive did not contain expected WAD files.";
                    return result;
                }

                var copied = new System.Collections.Generic.List<string>();
                foreach (var wad in wads)
                {
                    string dest = Path.Combine(destinationDirectory, Path.GetFileName(wad));
                    File.Copy(wad, dest, true);
                    copied.Add(dest);
                }

                TryDelete(zipPath);
                TryDeleteDirectory(extractDir);

                result.Succeeded = true;
                result.WadPaths = copied.ToArray();
                progress?.Report($"Installed {copied.Count} Freedoom WAD(s).");
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
        }

        public static string[] ExtractWadsFromZip(string zipPath, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var copied = new System.Collections.Generic.List<string>();
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) || !entry.Name.EndsWith(".wad", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!IsFreedoomWad(entry.Name))
                        continue;
                    string dest = Path.Combine(destinationDirectory, Path.GetFileName(entry.Name));
                    entry.ExtractToFile(dest, true);
                    copied.Add(dest);
                }
                return copied.ToArray();
            }
        }

        private static bool IsFreedoomWad(string path)
        {
            string name = Path.GetFileName(path);
            return name.Equals("freedoom1.wad", StringComparison.OrdinalIgnoreCase)
                || name.Equals("freedoom2.wad", StringComparison.OrdinalIgnoreCase)
                || name.Equals("freedm.wad", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string> ResolveDownloadUrl(CancellationToken cancellationToken)
        {
            try
            {
                var client = new GitHubClient(new ProductHeaderValue("DoomLauncher"));
                var release = await client.Repository.Release.GetLatest("freedoom", "freedoom").ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var asset = release?.Assets?.FirstOrDefault(a =>
                    a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    a.Name.IndexOf("freedoom", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    a.Name.IndexOf("freedm-", StringComparison.OrdinalIgnoreCase) < 0);
                if (asset != null && !string.IsNullOrEmpty(asset.BrowserDownloadUrl))
                    return asset.BrowserDownloadUrl;
            }
            catch
            {
            }

            return FallbackUrl;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { }
        }
    }
}
