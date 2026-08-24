using DoomLauncher;
using DoomLauncher.SourcePort;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace UnitTest.Tests
{
    [TestClass]
    public class TestSourcePortDetection
    {
        [TestMethod]
        public void DetectsSourcePortFlatpaksOutsideOrgZdoom()
        {
            string list = string.Join("\n", new[]
            {
                "org.zdoom.GZDoom",
                "io.github.UZDoom.UZDoom",
                "io.github.fabiangreffrath.Woof",
                "io.github.kraflab.dsda-doom",
                "org.gnome.gedit",
                "com.valvesoftware.Steam"
            });

            var found = SourcePortDetector.Detect(Array.Empty<string>(), list, string.Empty);
            var executables = found.Select(x => x.Executable).ToList();

            Assert.IsTrue(executables.Contains("flatpak:org.zdoom.GZDoom"));
            Assert.IsTrue(executables.Contains("flatpak:io.github.UZDoom.UZDoom"));
            Assert.IsTrue(executables.Contains("flatpak:io.github.fabiangreffrath.Woof"));
            Assert.IsTrue(executables.Contains("flatpak:io.github.kraflab.dsda-doom"));
            Assert.IsFalse(executables.Any(x => x.IndexOf("gedit", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsFalse(executables.Any(x => x.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [TestMethod]
        public void NamesUnknownFlatpaksFromTheirAppId()
        {
            var found = SourcePortDetector.Detect(Array.Empty<string>(), "io.github.UZDoom.UZDoom", string.Empty);
            var port = found.Single();

            Assert.AreEqual("UZDoom (Flatpak)", port.Name);
            Assert.AreEqual(DetectedSourcePortKind.Flatpak, port.Kind);
            Assert.IsTrue(port.ConfigDirectory.EndsWith(Path.Combine("io.github.UZDoom.UZDoom", "config", "uzdoom"), StringComparison.Ordinal));
        }

        [TestMethod]
        public void SkipsFlatpakListHeaderAndExtraColumns()
        {
            string list = "Application ID\tVersion\tBranch\norg.zdoom.GZDoom\t4.14.0\tstable\n";
            var found = SourcePortDetector.Detect(Array.Empty<string>(), list, string.Empty);

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("flatpak:org.zdoom.GZDoom", found[0].Executable);
        }

        [TestMethod]
        public void NeverOffersDoomLauncherItselfAsASourcePort()
        {
            Assert.IsFalse(SourcePortDetector.IsSourcePortAppId(SandboxHost.DefaultAppId));
            Assert.IsFalse(SourcePortDetector.IsSourcePortAppId("org.gnome.Calculator"));
            Assert.IsFalse(SourcePortDetector.IsSourcePortAppId("notanappid"));
            Assert.IsFalse(SourcePortDetector.IsSourcePortAppId(string.Empty));
            Assert.IsTrue(SourcePortDetector.IsSourcePortAppId("org.zdoom.GZDoom"));
            Assert.IsTrue(SourcePortDetector.IsSourcePortAppId("io.github.UZDoom.UZDoom"));
        }

        [TestMethod]
        public void FindsBinariesThatKeepTheirUpstreamCasing()
        {
            string dir = NewTempDirectory();
            try
            {
                WriteExecutable(Path.Combine(dir, "UZDoom"));

                string resolved = SourcePortLaunch.FindOnPath("uzdoom", new[] { dir });
                Assert.AreEqual(Path.Combine(dir, "UZDoom"), resolved);

                var found = SourcePortDetector.Detect(new[] { dir }, string.Empty, string.Empty);
                var port = found.Single(x => x.Kind == DetectedSourcePortKind.Native);

                // Launching needs the name as it exists on disk, not the name we searched for.
                Assert.AreEqual("UZDoom", port.Executable);
                Assert.AreEqual("UZDoom", port.Name);
                Assert.AreEqual(dir, port.Directory);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void PrefersAnExactBinaryNameOverACaseInsensitiveMatch()
        {
            string dir = NewTempDirectory();
            try
            {
                WriteExecutable(Path.Combine(dir, "GZDoom"));
                WriteExecutable(Path.Combine(dir, "gzdoom"));
                Assert.AreEqual(Path.Combine(dir, "gzdoom"), SourcePortLaunch.FindOnPath("gzdoom", new[] { dir }));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void FindsAPortShippedAsAnAppImage()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                Assert.Inconclusive("No home directory to place an AppImage under.");

            string dir = Path.Combine(home, "Applications");
            string appImage = Path.Combine(dir, "UZDoom-4.14.0-x86_64.AppImage");
            bool createdDir = !Directory.Exists(dir);
            try
            {
                Directory.CreateDirectory(dir);
                WriteExecutable(appImage);
                SourcePortLaunch.ClearCache();

                // An AppImage is never on PATH, so a plain name lookup has to reach for it.
                Assert.AreEqual(appImage, SourcePortLaunch.FindOnPath("uzdoom"));
                Assert.IsNull(SourcePortLaunch.FindOnPath("chocolate-doom"));
            }
            finally
            {
                try { File.Delete(appImage); } catch { }
                if (createdDir)
                {
                    try { Directory.Delete(dir); } catch { }
                }
                SourcePortLaunch.ClearCache();
            }
        }

        [TestMethod]
        public void ReadsInstalledFlatpaksFromDiskWhenTheCliIsUnreachable()
        {
            string root = NewTempDirectory();
            string previous = Environment.GetEnvironmentVariable("FLATPAK_USER_DIR");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "app", "io.github.UZDoom.UZDoom"));
                Directory.CreateDirectory(Path.Combine(root, "app", "org.gnome.gedit"));
                Environment.SetEnvironmentVariable("FLATPAK_USER_DIR", root);

                var ids = SourcePortDetector.ListFlatpakApps();
                Assert.IsTrue(ids.Contains("io.github.UZDoom.UZDoom"));
                Assert.IsTrue(ids.Contains("org.gnome.gedit"));

                var found = SourcePortDetector.Detect(Array.Empty<string>(), string.Join("\n", ids), string.Empty);
                Assert.IsTrue(found.Any(x => x.Executable == "flatpak:io.github.UZDoom.UZDoom"));
                Assert.IsFalse(found.Any(x => x.Executable.Contains("gedit")));
            }
            finally
            {
                Environment.SetEnvironmentVariable("FLATPAK_USER_DIR", previous);
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void HelperCommandThatNeverExitsIsKilledInsteadOfHanging()
        {
            var timer = Stopwatch.StartNew();
            var result = HostProcess.Run("sh", "-c \"sleep 30\"", 700, viaHost: false);
            timer.Stop();

            Assert.IsTrue(result.Started);
            Assert.IsTrue(result.TimedOut);
            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(timer.Elapsed.TotalSeconds < 10, $"Run blocked for {timer.Elapsed.TotalSeconds:0.0}s instead of timing out.");
        }

        [TestMethod]
        public void ChattyHelperCommandCannotDeadlockOnAFullPipe()
        {
            // More than one pipe buffer on both streams. Reading one to the end before the other
            // (what the detector used to do) wedges here forever.
            string script = "i=0; while [ $i -lt 4000 ]; do echo out-$i; echo err-$i 1>&2; i=$((i+1)); done";
            var result = HostProcess.Run("sh", "-c \"" + script + "\"", 30000, viaHost: false);

            Assert.IsTrue(result.Succeeded);
            Assert.IsTrue(result.StandardOutput.Contains("out-3999"));
            Assert.IsTrue(result.StandardError.Contains("err-3999"));
        }

        [TestMethod]
        public void OneWedgedHostCallStopsTheRestFromPayingTheTimeout()
        {
            bool? previous = SandboxHost.OverrideIsFlatpak;
            try
            {
                SandboxHost.OverrideIsFlatpak = true;
                SandboxHost.ResetHostResponsive();
                Assert.IsTrue(SandboxHost.HostResponsive);
                Assert.IsTrue(SandboxHost.CanRunHostCommands || !File.Exists("/usr/bin/flatpak-spawn"));

                SandboxHost.NoteHostTimeout();
                Assert.IsFalse(SandboxHost.HostResponsive);
                Assert.IsFalse(SandboxHost.CanRunHostCommands);

                // With the portal marked unresponsive, further host calls return at once instead
                // of each waiting out its own timeout.
                var timer = Stopwatch.StartNew();
                var result = HostProcess.Run("sh", "-c \"sleep 30\"", 5000);
                timer.Stop();
                Assert.IsFalse(result.Started);
                Assert.IsTrue(timer.Elapsed.TotalSeconds < 2, $"Call still waited {timer.Elapsed.TotalSeconds:0.0}s.");
                Assert.IsNull(SandboxHost.WhichOnHost("gzdoom"));

                SandboxHost.ClearLookupCache();
                Assert.IsTrue(SandboxHost.HostResponsive);
            }
            finally
            {
                SandboxHost.ResetHostResponsive();
                SandboxHost.OverrideIsFlatpak = previous;
            }
        }

        [TestMethod]
        public void MissingHelperCommandIsReportedRatherThanThrown()
        {
            var result = HostProcess.Run("doom-launcher-no-such-binary", "--version", 2000, viaHost: false);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(string.Empty, result.StandardOutput);
        }

        [TestMethod]
        public void OnlyPlainBinaryNamesReachTheHostShell()
        {
            Assert.IsTrue(SandboxHost.IsSafeCommandName("gzdoom"));
            Assert.IsTrue(SandboxHost.IsSafeCommandName("crispy-doom"));
            Assert.IsFalse(SandboxHost.IsSafeCommandName("gzdoom; rm -rf ~"));
            Assert.IsFalse(SandboxHost.IsSafeCommandName("$(id)"));
            Assert.IsFalse(SandboxHost.IsSafeCommandName("/usr/bin/gzdoom"));
        }

        [TestMethod]
        public void RepeatedLookupsDoNotRepeatTheSearch()
        {
            string dir = NewTempDirectory();
            try
            {
                SourcePortLaunch.ClearCache();
                Assert.IsNull(SourcePortLaunch.FindOnPath("doom-launcher-no-such-port"));

                // Cached miss: creating the file afterwards must not change the answer until the
                // caches are dropped, which is what the "Detect again" button does.
                WriteExecutable(Path.Combine(dir, "doom-launcher-no-such-port"));
                Assert.IsNull(SourcePortLaunch.FindOnPath("doom-launcher-no-such-port"));
                Assert.AreEqual(
                    Path.Combine(dir, "doom-launcher-no-such-port"),
                    SourcePortLaunch.FindOnPath("doom-launcher-no-such-port", new[] { dir }));
            }
            finally
            {
                SourcePortLaunch.ClearCache();
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void FindsZDoomFamilySavesUnderAnyFlatpakAppId()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                Assert.Inconclusive("No home directory to place a Flatpak config under.");

            string appId = "io.github.doomlauncher.TestUZDoom";
            string appRoot = Path.Combine(home, ".var", "app", appId);
            string config = Path.Combine(appRoot, "config", "uzdoom");
            try
            {
                Directory.CreateDirectory(config);
                var dirs = ZDoomSourcePortFlavor.UserSaveGameDirectories;
                Assert.IsTrue(dirs.Contains(config),
                    "A UZDoom Flatpak published outside org.zdoom.* still keeps its saves under ~/.var/app.");
            }
            finally
            {
                try { Directory.Delete(appRoot, true); } catch { }
            }
        }

        private static string NewTempDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dl-detect-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteExecutable(string path)
        {
            File.WriteAllText(path, "#!/bin/sh\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
