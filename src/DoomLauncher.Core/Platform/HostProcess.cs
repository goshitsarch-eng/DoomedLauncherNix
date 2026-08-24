using System;
using System.Diagnostics;
using System.Text;

namespace DoomLauncher
{
    public class HostProcessResult
    {
        public bool Started { get; set; }
        public bool TimedOut { get; set; }
        public int ExitCode { get; set; } = -1;
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;

        public bool Succeeded => Started && !TimedOut && ExitCode == 0;
    }

    /// <summary>
    /// Runs the short-lived helper commands detection needs (<c>which</c>, <c>flatpak list</c>,
    /// <c>snap list</c>) with a hard timeout.
    /// <para>
    /// Both pipes are drained by the runtime's async readers instead of a blocking
    /// <c>ReadToEnd</c>, so a command that writes a lot to stderr cannot fill a pipe buffer and
    /// wedge the caller, and a command that never exits is killed instead of blocking forever.
    /// The old code did <c>ReadToEnd()</c> before <c>WaitForExit(timeout)</c>, which made the
    /// timeout meaningless and froze the UI whenever <c>flatpak-spawn</c> did not answer.
    /// </para>
    /// </summary>
    public static class HostProcess
    {
        public const int DefaultTimeoutMs = 4000;

        public static HostProcessResult Run(string fileName, string arguments, int timeoutMs = DefaultTimeoutMs, bool? viaHost = null)
        {
            var result = new HostProcessResult();
            if (string.IsNullOrWhiteSpace(fileName))
                return result;
            if (timeoutMs <= 0)
                timeoutMs = DefaultTimeoutMs;

            bool throughHost = viaHost ?? SandboxHost.IsFlatpak;
            if (throughHost && !SandboxHost.HostResponsive)
                return result;   // A host call already timed out; do not pay the timeout again.

            var start = SandboxHost.WrapForHost(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }, throughHost);

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            object sync = new object();

            try
            {
                using (var proc = Process.Start(start))
                {
                    if (proc == null)
                        return result;

                    result.Started = true;
                    proc.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data == null)
                            return;
                        lock (sync)
                            stdout.AppendLine(e.Data);
                    };
                    proc.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data == null)
                            return;
                        lock (sync)
                            stderr.AppendLine(e.Data);
                    };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    // Nothing is ever typed at these commands; an open stdin can keep one waiting.
                    try { proc.StandardInput.Close(); } catch { }

                    if (proc.WaitForExit(timeoutMs))
                    {
                        // Parameterless overload flushes the async readers before we read the buffers.
                        proc.WaitForExit();
                        result.ExitCode = proc.ExitCode;
                    }
                    else
                    {
                        result.TimedOut = true;
                        if (throughHost)
                            SandboxHost.NoteHostTimeout();
                        TryKill(proc);
                        proc.WaitForExit(500);
                    }
                }
            }
            catch
            {
                // Missing binary, denied portal, or a sandbox with no flatpak-spawn: nothing found.
            }

            lock (sync)
            {
                result.StandardOutput = stdout.ToString();
                result.StandardError = stderr.ToString();
            }
            return result;
        }

        public static string RunForOutput(string fileName, string arguments, int timeoutMs = DefaultTimeoutMs, bool? viaHost = null)
        {
            var result = Run(fileName, arguments, timeoutMs, viaHost);
            return result.Succeeded ? result.StandardOutput : string.Empty;
        }

        private static void TryKill(Process proc)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(true);
            }
            catch
            {
            }
        }
    }
}
