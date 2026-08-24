using DoomLauncher.SourcePort;
using System;
using System.Collections.Generic;
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
            ("zdoom", "ZDoom"),
            ("zandronum", "Zandronum"),
            ("crispy-doom", "Crispy Doom"),
            ("chocolate-doom", "Chocolate Doom"),
            ("prboom-plus", "PrBoom+"),
            ("dsda-doom", "DSDA-Doom"),
            ("woof", "Woof!"),
            ("helion", "Helion"),
            ("nyan-doom", "Nyan Doom"),
            ("nugget-doom", "Nugget Doom"),
            ("doomretro", "DOOM Retro"),
            ("eternity", "Eternity Engine"),
            ("odamex", "Odamex"),
            ("doomsday", "Doomsday")
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

        /// <summary>
        /// Detection spawns helper processes and touches the filesystem. Call it from a background
        /// thread; the GTK front end does, because doing it inline froze the window.
        /// </summary>
        public static IReadOnlyList<DetectedSourcePort> Detect()
        {
            // One host round trip for every candidate binary instead of one per name. "flatpak"
            // and "snap" are primed too so the install hint and the per-port "is it installed"
            // check can answer from the cache instead of spawning on the UI thread.
            if (SandboxHost.IsFlatpak)
            {
                SandboxHost.ResetHostResponsive();
                SandboxHost.PrimeHostLookups(KnownBinaries.Select(x => x.Binary).Concat(new[] { "flatpak", "snap" }));
            }

            return Detect(null, string.Join("\n", ListFlatpakApps()), TryReadProcess("snap", "list"));
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
                    // Record the name on disk, not the name we searched for: a port shipped as
                    // "UZDoom" must be launched as "UZDoom" on a case sensitive filesystem.
                    Executable = Path.GetFileName(path),
                    Directory = Path.GetDirectoryName(path) ?? string.Empty,
                    Kind = DetectedSourcePortKind.Native,
                    Details = path,
                    ConfigDirectory = DefaultConfigDirectory(binary, DetectedSourcePortKind.Native, null),
                    Preferred = binary.Equals("gzdoom", StringComparison.OrdinalIgnoreCase)
                });
            }

            foreach (string appId in ParseFlatpakIds(flatpakListOutput))
            {
                add(new DetectedSourcePort
                {
                    Name = FlatpakDisplayName(appId),
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

        /// <summary>
        /// Every installed Flatpak app id, from <c>flatpak list</c> plus a direct read of the
        /// per-user and system installation directories. The directory scan matters because the
        /// <c>flatpak</c> binary is not on the PATH inside our own sandbox, and it is also what
        /// keeps detection working when the CLI is missing entirely.
        /// </summary>
        public static IReadOnlyList<string> ListFlatpakApps()
        {
            var ids = new List<string>();
            ids.AddRange(SplitLines(TryReadProcess("flatpak", "list --app --columns=application")));
            ids.AddRange(ScanFlatpakInstallations());
            return ids
                .Select(x => x.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> ScanFlatpakInstallations()
        {
            foreach (string root in FlatpakAppRoots())
            {
                string[] directories;
                try
                {
                    if (!Directory.Exists(root))
                        continue;
                    directories = Directory.GetDirectories(root);
                }
                catch
                {
                    continue;
                }

                foreach (string directory in directories)
                {
                    string id = Path.GetFileName(directory);
                    if (!string.IsNullOrEmpty(id))
                        yield return id;
                }
            }
        }

        private static IEnumerable<string> FlatpakAppRoots()
        {
            var roots = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string installation = Environment.GetEnvironmentVariable("FLATPAK_USER_DIR");
            if (!string.IsNullOrEmpty(installation))
                roots.Add(Path.Combine(installation, "app"));

            string xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrEmpty(xdgData))
                roots.Add(Path.Combine(xdgData, "flatpak", "app"));

            if (!string.IsNullOrEmpty(home))
            {
                // Our own sandbox redirects XDG_DATA_HOME into ~/.var/app, so ask for the real
                // per-user installation by name as well.
                roots.Add(Path.Combine(home, ".local", "share", "flatpak", "app"));
            }

            roots.Add("/var/lib/flatpak/app");
            return roots.Distinct(StringComparer.Ordinal);
        }

        private static IEnumerable<string> ParseFlatpakIds(string flatpakListOutput)
        {
            foreach (string line in SplitLines(flatpakListOutput))
            {
                // "flatpak list" prints a header row and extra columns when attached to a terminal.
                string appId = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (IsSourcePortAppId(appId))
                    yield return appId;
            }
        }

        /// <summary>
        /// Recognises a Flatpak app id as a Doom source port. The old check only accepted ids
        /// starting with <c>org.zdoom.</c>, so ports published under any other reverse-DNS name
        /// (UZDoom, Woof, Crispy, DSDA) were never offered.
        /// </summary>
        public static bool IsSourcePortAppId(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return false;

            appId = appId.Trim();
            if (appId.IndexOf('.') < 0 || appId.Any(char.IsWhiteSpace))
                return false;
            if (appId.Equals(SandboxHost.DefaultAppId, StringComparison.OrdinalIgnoreCase) ||
                appId.Equals(SandboxHost.AppId, StringComparison.OrdinalIgnoreCase))
                return false;
            if (KnownFlatpaks.Any(x => string.Equals(x.AppId, appId, StringComparison.OrdinalIgnoreCase)))
                return true;

            string segment = Normalize(LastSegment(appId));
            if (KnownBinaries.Any(x => Normalize(x.Binary) == segment))
                return true;
            if (segment.EndsWith("doom", StringComparison.Ordinal))
                return true;

            string whole = Normalize(appId);
            return whole.Contains("zdoom") || whole.Contains("zandronum") || whole.Contains("doomsday");
        }

        private static string FlatpakDisplayName(string appId)
        {
            var known = KnownFlatpaks.FirstOrDefault(x => string.Equals(x.AppId, appId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(known.Name))
                return known.Name;

            string segment = LastSegment(appId);
            var binary = KnownBinaries.FirstOrDefault(x => Normalize(x.Binary) == Normalize(segment));
            return (string.IsNullOrEmpty(binary.Name) ? segment : binary.Name) + " (Flatpak)";
        }

        private static string LastSegment(string appId)
        {
            if (string.IsNullOrEmpty(appId))
                return string.Empty;
            int dot = appId.LastIndexOf('.');
            return dot < 0 || dot == appId.Length - 1 ? appId : appId.Substring(dot + 1);
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        public static SourcePortInstallHint GetGzdoomInstallHint()
        {
            if (SourcePortLaunch.FindOnPath("flatpak") != null || (SandboxHost.IsFlatpak && SandboxHost.CanRunHostCommands))
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
                return Path.Combine(home, ".var", "app", appId ?? string.Empty, "config", ConfigFolderName(appId));
            }

            string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(xdg))
                xdg = Path.Combine(home, ".config");
            string native = (nameOrAppId ?? "gzdoom").ToLowerInvariant();
            if (native.StartsWith("org.zdoom.", StringComparison.Ordinal))
                native = "gzdoom";
            return Path.Combine(xdg, native);
        }

        private static string ConfigFolderName(string appId)
        {
            string whole = Normalize(appId);
            foreach (string family in new[] { "uzdoom", "vkdoom", "lzdoom", "gzdoom", "zandronum" })
            {
                if (whole.Contains(family))
                    return family;
            }

            string segment = LastSegment(appId);
            return string.IsNullOrEmpty(segment) ? "gzdoom" : segment.ToLowerInvariant();
        }

        /// <summary>
        /// Runs an install command to completion. Both pipes are drained concurrently, so the
        /// progress output of <c>flatpak install</c> cannot fill a buffer and hang the install.
        /// </summary>
        public static HostProcessResult RunInstall(SourcePortInstallHint hint, int timeoutMs = 15 * 60 * 1000)
        {
            if (hint == null || string.IsNullOrWhiteSpace(hint.Command))
                return new HostProcessResult();
            return HostProcess.Run("/bin/bash", "-lc " + QuoteForBash(hint.Command), timeoutMs);
        }

        private static string QuoteForBash(string command)
        {
            return "'" + command.Replace("'", "'\\''") + "'";
        }

        public static string TryReadProcess(string fileName, string arguments)
        {
            // Inside our own Flatpak these tools live on the host, not on the sandbox PATH.
            // Requiring a sandbox-local hit is what made every host Flatpak invisible.
            string resolved = SourcePortLaunch.FindOnPath(fileName);
            if (resolved == null)
            {
                if (!SandboxHost.IsFlatpak || !SandboxHost.CanRunHostCommands)
                    return string.Empty;

                // flatpak-spawn hands the command the sandbox PATH, so fall back to the bare name
                // only when we could not resolve it; an absolute path is always preferred.
                resolved = fileName;
            }

            return HostProcess.RunForOutput(resolved, arguments, 8000);
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
