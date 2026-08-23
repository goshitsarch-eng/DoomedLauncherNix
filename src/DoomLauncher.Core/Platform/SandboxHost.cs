using System;
using System.Diagnostics;
using System.IO;

namespace DoomLauncher
{
    /// <summary>
    /// Detects when Doom Launcher itself is a Flatpak and routes host commands through
    /// <c>flatpak-spawn --host</c> so GZDoom, Steam libraries, and package tools stay reachable.
    /// </summary>
    public static class SandboxHost
    {
        public const string SpawnBinary = "flatpak-spawn";
        public const string DefaultAppId = "com.goshapps.DoomLauncher";

        internal static bool? OverrideIsFlatpak { get; set; }

        public static bool IsFlatpak => OverrideIsFlatpak ?? DetectIsFlatpak();

        private static bool DetectIsFlatpak()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID")))
                return true;
            try
            {
                return File.Exists("/.flatpak-info");
            }
            catch
            {
                return false;
            }
        }

        public static string AppId
        {
            get
            {
                string id = Environment.GetEnvironmentVariable("FLATPAK_ID");
                return string.IsNullOrEmpty(id) ? DefaultAppId : id;
            }
        }

        public static ProcessStartInfo WrapForHost(ProcessStartInfo inner)
        {
            return WrapForHost(inner, IsFlatpak);
        }

        public static ProcessStartInfo WrapForHost(ProcessStartInfo inner, bool sandboxed)
        {
            if (inner == null || !sandboxed)
                return inner;

            string file = inner.FileName ?? string.Empty;
            if (string.Equals(Path.GetFileName(file), SpawnBinary, StringComparison.OrdinalIgnoreCase))
                return inner;

            string command = Quote(file);
            if (!string.IsNullOrWhiteSpace(inner.Arguments))
                command += " " + inner.Arguments;

            var wrapped = new ProcessStartInfo
            {
                FileName = SpawnBinary,
                Arguments = "--host -- " + command,
                WorkingDirectory = inner.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = inner.RedirectStandardOutput,
                RedirectStandardError = inner.RedirectStandardError,
                RedirectStandardInput = inner.RedirectStandardInput,
                CreateNoWindow = inner.CreateNoWindow
            };
            return wrapped;
        }

        public static string WhichOnHost(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            try
            {
                var start = WrapForHost(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = name,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                using (var proc = Process.Start(start))
                {
                    if (proc == null)
                        return null;
                    string output = (proc.StandardOutput.ReadToEnd() ?? string.Empty).Trim();
                    proc.WaitForExit(4000);
                    if (proc.ExitCode == 0 && output.Length > 0 && !output.Contains('\n'))
                        return output;
                }
            }
            catch
            {
            }
            return null;
        }

        public static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";
            if (value.IndexOfAny(new[] { ' ', '\t', '"', '\'' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
