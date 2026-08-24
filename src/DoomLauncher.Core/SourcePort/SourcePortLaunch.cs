using DoomLauncher.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DoomLauncher.SourcePort
{
    /// <summary>
    /// Builds a process to start a source port, including Flatpak and snap wrappers.
    /// Store Flatpak ports as <c>flatpak:org.zdoom.GZDoom</c> and snap ports as <c>snap:gzdoom</c>.
    /// ExtraParameters remain Doom arguments appended after the wrapper, not the wrapper itself.
    /// </summary>
    public static class SourcePortLaunch
    {
        public const string FlatpakPrefix = "flatpak:";
        public const string SnapPrefix = "snap:";

        private static readonly ConcurrentDictionary<string, string> PathLookups =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, string[]> DirectoryListings =
            new ConcurrentDictionary<string, string[]>(StringComparer.Ordinal);

        public static bool IsManaged(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable))
                return false;
            executable = executable.Trim();
            return executable.StartsWith(FlatpakPrefix, StringComparison.OrdinalIgnoreCase)
                || executable.StartsWith(SnapPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsZDoomFamily(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable))
                return false;

            string key = executable.Trim().ToLowerInvariant();
            string[] tokens = { "gzdoom", "uzdoom", "vkdoom", "lzdoom", "zandronum", "zdoom", "org.zdoom" };
            return tokens.Any(token => key.IndexOf(token, StringComparison.Ordinal) >= 0);
        }

        public static string GetAppId(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable))
                return string.Empty;
            executable = executable.Trim();
            if (executable.StartsWith(FlatpakPrefix, StringComparison.OrdinalIgnoreCase))
                return executable.Substring(FlatpakPrefix.Length).Trim();
            if (executable.StartsWith(SnapPrefix, StringComparison.OrdinalIgnoreCase))
                return executable.Substring(SnapPrefix.Length).Trim();
            return string.Empty;
        }

        public static ParsedLaunch Parse(ISourcePortData sourcePort)
        {
            string exec = sourcePort?.Executable?.Trim() ?? string.Empty;
            if (exec.StartsWith(FlatpakPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string appId = exec.Substring(FlatpakPrefix.Length).Trim();
                return new ParsedLaunch
                {
                    FileName = FindOnPath("flatpak") ?? "flatpak",
                    PrefixArguments = $"run --filesystem=host --filesystem=home {appId} --",
                    RequiresExistingFile = false,
                    Kind = LaunchKind.Flatpak
                };
            }

            if (exec.StartsWith(SnapPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string snap = exec.Substring(SnapPrefix.Length).Trim();
                return new ParsedLaunch
                {
                    FileName = FindOnPath("snap") ?? "snap",
                    PrefixArguments = $"run {snap}",
                    RequiresExistingFile = false,
                    Kind = LaunchKind.Snap
                };
            }

            string full = sourcePort?.GetFullExecutablePath() ?? exec;
            bool hasSlash = exec.IndexOfAny(new[] { '/', '\\' }) >= 0;
            if (!string.IsNullOrEmpty(full) && File.Exists(full))
            {
                return new ParsedLaunch
                {
                    FileName = full,
                    PrefixArguments = string.Empty,
                    RequiresExistingFile = true,
                    Kind = LaunchKind.Native
                };
            }

            if (SandboxHost.IsFlatpak && !hasSlash && !string.IsNullOrEmpty(full) && Path.IsPathRooted(full))
            {
                // A host binary outside /usr/bin is not visible inside our sandbox, so File.Exists
                // says no. Hand flatpak-spawn the absolute path anyway rather than a bare name
                // that would have to be resolved against whatever PATH the host command inherits.
                return new ParsedLaunch
                {
                    FileName = full,
                    PrefixArguments = string.Empty,
                    RequiresExistingFile = false,
                    Kind = LaunchKind.Native
                };
            }

            if (!hasSlash && !string.IsNullOrEmpty(exec))
            {
                return new ParsedLaunch
                {
                    FileName = exec,
                    PrefixArguments = string.Empty,
                    RequiresExistingFile = false,
                    Kind = LaunchKind.Path
                };
            }

            return new ParsedLaunch
            {
                FileName = string.IsNullOrEmpty(full) ? exec : full,
                PrefixArguments = string.Empty,
                RequiresExistingFile = true,
                Kind = LaunchKind.Native
            };
        }

        public static bool CanExecute(ISourcePortData sourcePort)
        {
            var parsed = Parse(sourcePort);
            if (string.IsNullOrEmpty(parsed.FileName))
                return false;
            if (parsed.Kind == LaunchKind.Flatpak)
                return CanRunWrapper("flatpak");
            if (parsed.Kind == LaunchKind.Snap)
                return CanRunWrapper("snap");
            if (parsed.Kind == LaunchKind.Path)
                return FindOnPath(parsed.FileName) != null;
            if (File.Exists(parsed.FileName))
                return true;
            return SandboxHost.IsFlatpak && FindOnPath(Path.GetFileName(parsed.FileName)) != null;
        }

        /// <summary>
        /// Inside our own Flatpak the wrapper runs on the host through flatpak-spawn, so a failed
        /// lookup inside the sandbox is not proof that it is missing. Do not block the launch on it.
        /// </summary>
        private static bool CanRunWrapper(string wrapper)
        {
            if (FindOnPath(wrapper) != null)
                return true;
            return SandboxHost.IsFlatpak && SandboxHost.CanRunHostCommands;
        }

        public static ProcessStartInfo CreateStartInfo(ISourcePortData sourcePort, string doomArguments, string workingDirectory, bool? sandboxed = null)
        {
            var parsed = Parse(sourcePort);
            if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                if (parsed.Kind == LaunchKind.Native && File.Exists(parsed.FileName))
                    workingDirectory = Path.GetDirectoryName(parsed.FileName);
                else
                    workingDirectory = Directory.GetCurrentDirectory();
            }

            var start = new ProcessStartInfo
            {
                FileName = parsed.FileName,
                Arguments = CombineArgs(parsed.PrefixArguments, doomArguments),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false
            };
            return SandboxHost.WrapForHost(start, sandboxed ?? SandboxHost.IsFlatpak);
        }

        public static string FormatCommand(ISourcePortData sourcePort, string doomArguments)
        {
            var start = CreateStartInfo(sourcePort, doomArguments, Directory.GetCurrentDirectory());
            if (string.IsNullOrWhiteSpace(start.Arguments))
                return start.FileName;
            return $"{start.FileName} {start.Arguments}";
        }

        public static string CombineArgs(string prefix, string doomArguments)
        {
            prefix = prefix?.Trim() ?? string.Empty;
            doomArguments = doomArguments?.Trim() ?? string.Empty;
            if (prefix.Length == 0)
                return doomArguments;
            if (doomArguments.Length == 0)
                return prefix;
            return prefix + " " + doomArguments;
        }

        /// <summary>
        /// Resolves a binary name to a full path. Results are cached because detection asks for a
        /// dozen names in a row and a sandboxed lookup costs a host process each time.
        /// </summary>
        public static string FindOnPath(string name, IEnumerable<string> extraPaths = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            bool cacheable = extraPaths == null;
            if (cacheable && PathLookups.TryGetValue(name, out string cached))
                return cached.Length == 0 ? null : cached;

            string found = FindOnPathUncached(name, extraPaths);
            if (cacheable)
                PathLookups[name] = found ?? string.Empty;
            return found;
        }

        private static string FindOnPathUncached(string name, IEnumerable<string> extraPaths)
        {
            var directories = SearchDirectories(extraPaths);

            foreach (string dir in directories)
            {
                try
                {
                    string candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                }
            }

            // Ports shipped as a tarball or AppImage often keep their upstream capitalisation
            // (UZDoom, VkDoom); PATH lookups on Linux are case sensitive and would miss them.
            foreach (string dir in directories)
            {
                string match = FindIgnoringCase(dir, name);
                if (match != null)
                    return match;
            }

            string appImage = FindAppImage(name);
            if (appImage != null)
                return appImage;

            if (SandboxHost.IsFlatpak)
                return SandboxHost.WhichOnHost(name);

            return null;
        }

        /// <summary>
        /// Finds a port distributed as an AppImage. UZDoom and several other ports ship one, and
        /// an AppImage never lands on PATH, so a plain name lookup could never see it.
        /// </summary>
        private static string FindAppImage(string name)
        {
            string key = NormalizeName(name);
            if (key.Length == 0)
                return null;

            foreach (string dir in AppImageDirectories())
            {
                string[] files;
                try
                {
                    if (!Directory.Exists(dir))
                        continue;
                    files = DirectoryListings.GetOrAdd(dir, d =>
                    {
                        try
                        {
                            return Directory.GetFiles(d);
                        }
                        catch
                        {
                            return Array.Empty<string>();
                        }
                    });
                }
                catch
                {
                    continue;
                }

                foreach (string file in files)
                {
                    if (!string.Equals(Path.GetExtension(file), ".AppImage", StringComparison.OrdinalIgnoreCase))
                        continue;
                    // "UZDoom-4.14.0-x86_64.AppImage" should answer a lookup for "uzdoom".
                    if (NormalizeName(Path.GetFileNameWithoutExtension(file)).StartsWith(key, StringComparison.Ordinal))
                        return file;
                }
            }

            return null;
        }

        private static IEnumerable<string> AppImageDirectories()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                yield break;

            yield return Path.Combine(home, "Applications");
            yield return Path.Combine(home, "AppImages");
            yield return Path.Combine(home, ".local", "bin");
            yield return Path.Combine(home, "bin");
            yield return Path.Combine(home, "Downloads");
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static List<string> SearchDirectories(IEnumerable<string> extraPaths)
        {
            var paths = new List<string>();
            if (extraPaths != null)
                paths.AddRange(extraPaths);

            string env = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(env))
                paths.AddRange(env.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries));

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            paths.Add("/usr/bin");
            paths.Add("/usr/local/bin");
            paths.Add("/usr/games");
            paths.Add("/usr/local/games");
            paths.Add("/bin");
            if (!string.IsNullOrEmpty(home))
            {
                paths.Add(Path.Combine(home, ".local", "bin"));
                paths.Add(Path.Combine(home, "bin"));
            }

            return paths
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string FindIgnoringCase(string directory, string name)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return null;
                var names = DirectoryListings.GetOrAdd(directory, dir =>
                {
                    try
                    {
                        return Directory.GetFiles(dir);
                    }
                    catch
                    {
                        return Array.Empty<string>();
                    }
                });
                return names.FirstOrDefault(x =>
                    string.Equals(Path.GetFileName(x), name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Forgets cached lookups so a port installed while the app is open is found.</summary>
        public static void ClearCache()
        {
            PathLookups.Clear();
            DirectoryListings.Clear();
            SandboxHost.ClearLookupCache();
        }

        public enum LaunchKind
        {
            Native,
            Path,
            Flatpak,
            Snap
        }

        public struct ParsedLaunch
        {
            public string FileName { get; set; }
            public string PrefixArguments { get; set; }
            public bool RequiresExistingFile { get; set; }
            public LaunchKind Kind { get; set; }
        }
    }
}
