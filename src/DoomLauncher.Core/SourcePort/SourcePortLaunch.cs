using DoomLauncher.Interfaces;
using System;
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
                return FindOnPath("flatpak") != null;
            if (parsed.Kind == LaunchKind.Snap)
                return FindOnPath("snap") != null;
            if (parsed.Kind == LaunchKind.Path)
                return FindOnPath(parsed.FileName) != null;
            if (File.Exists(parsed.FileName))
                return true;
            return SandboxHost.IsFlatpak && FindOnPath(Path.GetFileName(parsed.FileName)) != null;
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

        public static string FindOnPath(string name, IEnumerable<string> extraPaths = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

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
            paths.Add("/bin");
            if (!string.IsNullOrEmpty(home))
                paths.Add(Path.Combine(home, ".local", "bin"));

            foreach (string dir in paths.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                }
            }

            if (SandboxHost.IsFlatpak)
                return SandboxHost.WhichOnHost(name);

            return null;
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
