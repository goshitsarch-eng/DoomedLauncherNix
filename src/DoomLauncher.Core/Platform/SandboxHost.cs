using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

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

        /// <summary>
        /// Host lookups cost a process spawn each, and detection asks for a dozen binaries in a
        /// row. Cache both hits and misses so a repeated "Detect" never re-pays that cost.
        /// </summary>
        private static readonly ConcurrentDictionary<string, string> HostLookups =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private static int s_hostUnresponsive;

        internal static bool? OverrideIsFlatpak { get; set; }

        public static bool IsFlatpak => OverrideIsFlatpak ?? DetectIsFlatpak();

        public static bool SupportsInPlaceUpdate => !IsFlatpak;

        /// <summary>
        /// False once a host command has timed out. A portal that is not answering makes every
        /// following lookup pay the full timeout, which turned one wedged call into minutes of
        /// waiting; after the first one we stop asking until the next explicit detection.
        /// </summary>
        public static bool HostResponsive => Volatile.Read(ref s_hostUnresponsive) == 0;

        internal static void NoteHostTimeout()
        {
            Volatile.Write(ref s_hostUnresponsive, 1);
        }

        /// <summary>Lets host commands be tried again, e.g. when the user asks to detect again.</summary>
        public static void ResetHostResponsive()
        {
            Volatile.Write(ref s_hostUnresponsive, 0);
        }

        /// <summary>
        /// True when host commands can actually be started: always outside a sandbox, and inside
        /// one only when <c>flatpak-spawn</c> is present. Without this the detector spawns a
        /// process per candidate binary that can only ever fail.
        /// </summary>
        public static bool CanRunHostCommands
        {
            get
            {
                if (!IsFlatpak)
                    return true;
                if (!HostResponsive)
                    return false;
                try
                {
                    return File.Exists("/usr/bin/" + SpawnBinary) || File.Exists("/bin/" + SpawnBinary);
                }
                catch
                {
                    return false;
                }
            }
        }

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

        /// <summary>
        /// Resolves a binary name against the <em>host</em> PATH. flatpak-spawn hands the sandbox
        /// environment to the host process, so the sandbox PATH (usually just /app/bin:/usr/bin)
        /// would hide anything in /usr/local/bin, /usr/games, ~/.local/bin, or the Flatpak export
        /// directories. The lookup therefore sets a full search path of its own.
        /// </summary>
        public static string WhichOnHost(string name)
        {
            if (!IsSafeCommandName(name) || !CanRunHostCommands)
                return null;

            string found = HostLookups.GetOrAdd(name, key => LookupOnHost(key) ?? string.Empty);
            return found.Length == 0 ? null : found;
        }

        /// <summary>
        /// Resolves several binaries in a single host round trip and fills the cache with the
        /// answers. Detection asks about ~20 names; one shell loop beats twenty process spawns,
        /// and twenty spawns each waiting on a portal is what made detection feel like a hang.
        /// </summary>
        public static void PrimeHostLookups(IEnumerable<string> names)
        {
            if (names == null || !CanRunHostCommands)
                return;

            var pending = names
                .Where(IsSafeCommandName)
                .Distinct(StringComparer.Ordinal)
                .Where(x => !HostLookups.ContainsKey(x))
                .ToList();
            if (pending.Count == 0)
                return;

            // "exit 0" matters: without it the loop reports the status of the last lookup, so a
            // trailing miss would look like a failed probe and every absent port would be asked
            // for again one process at a time.
            string script = "PATH=" + HostSearchPath() + "; for n in " + string.Join(" ", pending) +
                            "; do p=$(command -v -- $n 2>/dev/null) && echo $n=$p; done; exit 0";
            var result = HostProcess.Run("sh", "-c \"" + script + "\"", 6000);

            if (result.Started && !result.TimedOut)
            {
                foreach (string line in result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0)
                        continue;
                    string name = line.Substring(0, split).Trim();
                    string path = line.Substring(split + 1).Trim();
                    if (path.Length > 0 && path[0] == '/' && pending.Contains(name, StringComparer.Ordinal))
                        HostLookups[name] = path;
                }
            }

            // Anything the loop did not report is absent on the host. Record the miss so the
            // per-name fallback does not spawn a process for it again.
            if (result.Succeeded)
            {
                foreach (string name in pending)
                    HostLookups.TryAdd(name, string.Empty);
            }
        }

        /// <summary>Drops cached host lookups so a port installed while the app is open is seen.</summary>
        public static void ClearLookupCache()
        {
            HostLookups.Clear();
            ResetHostResponsive();
        }

        private static string LookupOnHost(string name)
        {
            // Separate assignment, not a "PATH=x command" prefix: both work in dash and bash, but
            // only this form is unambiguous about which PATH the lookup uses.
            string script = "PATH=" + HostSearchPath() + "; command -v -- " + name;
            var result = HostProcess.Run("sh", "-c \"" + script + "\"", 3000);
            string path = FirstPath(result.Succeeded ? result.StandardOutput : null);
            if (path != null)
                return path;
            if (result.TimedOut)
                return null;   // The host is not answering; a second probe would only stall again.

            // Hosts without a POSIX-compliant /bin/sh still normally have which(1).
            result = HostProcess.Run("which", name, 3000);
            return FirstPath(result.Succeeded ? result.StandardOutput : null);
        }

        private static string HostSearchPath()
        {
            // $HOME and $PATH are expanded by the host shell, not here.
            return "/usr/bin:/usr/local/bin:/usr/games:/usr/local/games:/bin:/snap/bin:" +
                   "$HOME/.local/bin:/var/lib/flatpak/exports/bin:$HOME/.local/share/flatpak/exports/bin:$PATH";
        }

        private static string FirstPath(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;
            string line = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .FirstOrDefault(x => x.Length > 0 && x[0] == '/');
            return string.IsNullOrEmpty(line) ? null : line;
        }

        /// <summary>
        /// Guards the shell lookup: source port executables are user supplied, so only plain
        /// binary names reach a command line.
        /// </summary>
        internal static bool IsSafeCommandName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-' && c != '+')
                    return false;
            }
            return true;
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
