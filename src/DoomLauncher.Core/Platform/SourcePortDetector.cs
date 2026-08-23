using DoomLauncher.SourcePort;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DoomLauncher
{
    public enum DetectedSourcePortKind
    {
        Native,
        Flatpak,
        Snap
    }

    public class DetectedSourcePort
    {
        public string Name { get; set; }
        public string Executable { get; set; }
        public string Directory { get; set; }
        public DetectedSourcePortKind Kind { get; set; }
        public string Details { get; set; }
        public string ConfigDirectory { get; set; }
        public bool Preferred { get; set; }
    }

    public class SourcePortInstallHint
    {
        public string Title { get; set; }
        public string Command { get; set; }
        public bool CanRun { get; set; }
        public string AppId { get; set; }
    }

    public static class SourcePortDetector
    {
        public static readonly (string Binary, string Name)[] KnownBinaries =
        {
            ("gzdoom", "GZDoom"),
            ("uzdoom", "UZDoom"),
            ("vkdoom", "VKDoom"),
            ("lzdoom", "LZDoom"),
            ("zandronum", "Zandronum"),
            ("crispy-doom", "Crispy Doom"),
            ("chocolate-doom", "Chocolate Doom"),
            ("prboom-plus", "PrBoom+"),
            ("dsda-doom", "DSDA-Doom"),
            ("woof", "Woof!"),
            ("helion", "Helion"),
            ("nyan-doom", "Nyan Doom"),
            ("nugget-doom", "Nugget Doom")
        };

        public static readonly (string AppId, string Name)[] KnownFlatpaks =
        {
            ("org.zdoom.GZDoom", "GZDoom (Flatpak)"),
            ("org.zdoom.UZDoom", "UZDoom (Flatpak)"),
            ("org.zdoom.VKDoom", "VKDoom (Flatpak)")
        };

        public static readonly (string Snap, string Name)[] KnownSnaps =
        {
            ("gzdoom", "GZDoom (Snap)")
        };

        public static IReadOnlyList<DetectedSourcePort> Detect()
        {
            return Detect(null, TryReadProcess("flatpak", "list --app --columns=application"), TryReadProcess("snap", "list"));
        }

        public static IReadOnlyList<DetectedSourcePort> Detect(IEnumerable<string> searchPaths, string flatpakListOutput, string snapListOutput)
        {
            var results = new List<DetectedSourcePort>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void add(DetectedSourcePort port)
            {
                if (port == null || string.IsNullOrEmpty(port.Executable))
                    return;
                if (!seen.Add(port.Executable))
                    return;
                results.Add(port);
            }

            foreach (var (binary, name) in KnownBinaries)
            {
                string path = SourcePortLaunch.FindOnPath(binary, searchPaths);
                if (string.IsNullOrEmpty(path))
                    continue;
                add(new DetectedSourcePort
                {
                    Name = name,
                    Executable = binary,
                    Directory = Path.GetDirectoryName(path) ?? string.Empty,
                    Kind = DetectedSourcePortKind.Native,
                    Details = path,
                    ConfigDirectory = DefaultConfigDirectory(binary, DetectedSourcePortKind.Native, null),
                    Preferred = binary.Equals("gzdoom", StringComparison.OrdinalIgnoreCase)
                });
            }

            foreach (var line in SplitLines(flatpakListOutput))
            {
                var known = KnownFlatpaks.FirstOrDefault(x => string.Equals(x.AppId, line, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(known.AppId) && !line.StartsWith("org.zdoom.", StringComparison.OrdinalIgnoreCase))
                    continue;
                string appId = string.IsNullOrEmpty(known.AppId) ? line : known.AppId;
                string name = string.IsNullOrEmpty(known.Name) ? appId + " (Flatpak)" : known.Name;
                add(new DetectedSourcePort
                {
                    Name = name,
                    Executable = SourcePortLaunch.FlatpakPrefix + appId,
                    Directory = string.Empty,
                    Kind = DetectedSourcePortKind.Flatpak,
                    Details = appId,
                    ConfigDirectory = DefaultConfigDirectory(appId, DetectedSourcePortKind.Flatpak, appId),
                    Preferred = appId.Equals("org.zdoom.GZDoom", StringComparison.OrdinalIgnoreCase)
                });
            }

            foreach (var (snap, name) in KnownSnaps)
            {
                if (!SnapListContains(snapListOutput, snap))
                    continue;
                add(new DetectedSourcePort
                {
                    Name = name,
                    Executable = SourcePortLaunch.SnapPrefix + snap,
                    Directory = string.Empty,
                    Kind = DetectedSourcePortKind.Snap,
                    Details = snap,
                    ConfigDirectory = DefaultConfigDirectory(snap, DetectedSourcePortKind.Snap, null),
                    Preferred = snap.Equals("gzdoom", StringComparison.OrdinalIgnoreCase)
                });
            }

            return results
                .OrderByDescending(x => x.Preferred)
                .ThenBy(x => x.Kind)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static SourcePortInstallHint GetGzdoomInstallHint()
        {
            if (SourcePortLaunch.FindOnPath("flatpak") != null)
            {
                return new SourcePortInstallHint
                {
                    Title = "Install GZDoom from Flathub",
                    Command = "flatpak remote-add --if-not-exists --user flathub https://dl.flathub.org/repo/flathub.flatpakrepo && flatpak install --user -y flathub org.zdoom.GZDoom",
                    CanRun = true,
                    AppId = "org.zdoom.GZDoom"
                };
            }

            return new SourcePortInstallHint
            {
                Title = "Install GZDoom with your package manager",
                Command = "sudo apt install gzdoom\nsudo dnf install gzdoom\nsudo pacman -S gzdoom",
                CanRun = false,
                AppId = null
            };
        }

        public static string DefaultConfigDirectory(string nameOrAppId, DetectedSourcePortKind kind, string flatpakAppId)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                return string.Empty;

            if (kind == DetectedSourcePortKind.Flatpak)
            {
                string appId = string.IsNullOrEmpty(flatpakAppId) ? nameOrAppId : flatpakAppId;
                string folder = appId != null && appId.IndexOf("UZDoom", StringComparison.OrdinalIgnoreCase) >= 0 ? "uzdoom" :
                    appId != null && appId.IndexOf("VKDoom", StringComparison.OrdinalIgnoreCase) >= 0 ? "vkdoom" : "gzdoom";
                return Path.Combine(home, ".var", "app", appId, "config", folder);
            }

            string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(xdg))
                xdg = Path.Combine(home, ".config");
            string native = (nameOrAppId ?? "gzdoom").ToLowerInvariant();
            if (native.StartsWith("org.zdoom.", StringComparison.Ordinal))
                native = "gzdoom";
            return Path.Combine(xdg, native);
        }

        public static Process StartInstall(SourcePortInstallHint hint)
        {
            if (hint == null || string.IsNullOrWhiteSpace(hint.Command))
                return null;
            var start = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "-lc " + QuoteForBash(hint.Command),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            return Process.Start(start);
        }

        private static string QuoteForBash(string command)
        {
            return "'" + command.Replace("'", "'\\''") + "'";
        }

        public static string TryReadProcess(string fileName, string arguments)
        {
            if (SourcePortLaunch.FindOnPath(fileName) == null)
                return string.Empty;
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(start))
                {
                    if (proc == null)
                        return string.Empty;
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);
                    return output ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length > 0)
                        yield return line;
                }
            }
        }

        private static bool SnapListContains(string output, string snap)
        {
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrEmpty(snap))
                return false;
            foreach (var line in SplitLines(output))
            {
                if (line.StartsWith("Name ", StringComparison.OrdinalIgnoreCase))
                    continue;
                var first = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (string.Equals(first, snap, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
