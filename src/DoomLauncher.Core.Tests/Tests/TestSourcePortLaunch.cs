using DoomLauncher;
using DoomLauncher.DataSources;
using DoomLauncher.SourcePort;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace UnitTest.Tests
{
    [TestClass]
    public class TestSourcePortLaunch
    {
        [TestMethod]
        public void FlatpakUsesRunAndHostFilesystem()
        {
            var port = new SourcePortData
            {
                Name = "GZDoom",
                Executable = "flatpak:org.zdoom.GZDoom",
                Directory = LauncherPath.NoPath,
                FileOption = "-file"
            };

            Assert.IsTrue(SourcePortLaunch.IsManaged(port.Executable));
            Assert.IsTrue(SourcePortLaunch.IsZDoomFamily(port.Executable));
            Assert.AreEqual("org.zdoom.GZDoom", SourcePortLaunch.GetAppId(port.Executable));

            var start = SourcePortLaunch.CreateStartInfo(port, "-iwad \"doom2.wad\" -file \"mod.pk3\"", Directory.GetCurrentDirectory());
            Assert.AreEqual("flatpak", Path.GetFileName(start.FileName));
            Assert.IsTrue(start.Arguments.StartsWith("run --filesystem=host --filesystem=home org.zdoom.GZDoom --", StringComparison.Ordinal));
            Assert.IsTrue(start.Arguments.Contains("-iwad \"doom2.wad\""));
            Assert.IsTrue(start.Arguments.Contains("-file \"mod.pk3\""));
            Assert.AreEqual(false, start.UseShellExecute);
        }

        [TestMethod]
        public void SnapUsesSnapRun()
        {
            var port = new SourcePortData { Executable = "snap:gzdoom", Directory = LauncherPath.NoPath };
            var start = SourcePortLaunch.CreateStartInfo(port, "-iwad doom2.wad", Directory.GetCurrentDirectory());
            Assert.AreEqual("snap", Path.GetFileName(start.FileName));
            Assert.AreEqual("run gzdoom -iwad doom2.wad", start.Arguments);
        }

        [TestMethod]
        public void PathBinaryKeepsDoomArgumentsOnly()
        {
            var port = new SourcePortData { Executable = "gzdoom", Directory = LauncherPath.NoPath };
            var parsed = SourcePortLaunch.Parse(port);
            Assert.AreEqual(SourcePortLaunch.LaunchKind.Path, parsed.Kind);
            Assert.AreEqual("gzdoom", parsed.FileName);
            Assert.AreEqual("-iwad doom2.wad", SourcePortLaunch.CombineArgs(parsed.PrefixArguments, "-iwad doom2.wad"));
        }

        [TestMethod]
        public void ZDoomFlavorMatchesGzdoomAndFlatpak()
        {
            Assert.IsInstanceOfType(new SourcePortData { Executable = "gzdoom" }.GetFlavor(), typeof(ZDoomSourcePortFlavor));
            Assert.IsInstanceOfType(new SourcePortData { Executable = "GZDoom.exe" }.GetFlavor(), typeof(ZDoomSourcePortFlavor));
            Assert.IsInstanceOfType(new SourcePortData { Executable = "flatpak:org.zdoom.GZDoom" }.GetFlavor(), typeof(ZDoomSourcePortFlavor));
            Assert.IsInstanceOfType(new SourcePortData { Executable = "uzdoom" }.GetFlavor(), typeof(ZDoomSourcePortFlavor));
            Assert.IsInstanceOfType(new SourcePortData { Executable = "vkdoom" }.GetFlavor(), typeof(ZDoomSourcePortFlavor));
            Assert.IsInstanceOfType(new SourcePortData { Executable = "chocolate-doom" }.GetFlavor(), typeof(StatdumpSourcePortFlavor));
        }

        [TestMethod]
        public void DetectorParsesFlatpakAndSearchPath()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dl-port-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string binary = Path.Combine(dir, "gzdoom");
            File.WriteAllText(binary, "#!/bin/sh\n");

            try
            {
                var found = SourcePortDetector.Detect(new[] { dir }, "org.zdoom.GZDoom\norg.gnome.gedit\n", "Name  Version\ngzdoom  4.11.3\n");
                Assert.IsTrue(found.Any(x => x.Executable == "gzdoom" && x.Kind == DetectedSourcePortKind.Native));
                Assert.IsTrue(found.Any(x => x.Executable == "flatpak:org.zdoom.GZDoom"));
                Assert.IsTrue(found.Any(x => x.Executable == "snap:gzdoom"));
                Assert.IsFalse(found.Any(x => x.Executable != null && x.Executable.Contains("gedit")));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void SetupSkipsDuplicateExecutables()
        {
            var existing = new[]
            {
                new SourcePortData { Executable = "gzdoom", Name = "GZDoom" }
            };
            var detected = new DetectedSourcePort { Name = "GZDoom", Executable = "gzdoom" };
            Assert.IsTrue(SourcePortSetup.AlreadyConfigured(existing, detected));
            Assert.IsFalse(SourcePortSetup.AlreadyConfigured(existing, new DetectedSourcePort { Executable = "flatpak:org.zdoom.GZDoom" }));

            var port = SourcePortSetup.ToSourcePort(new DetectedSourcePort
            {
                Name = "GZDoom (Flatpak)",
                Executable = "flatpak:org.zdoom.GZDoom",
                Directory = string.Empty,
                Kind = DetectedSourcePortKind.Flatpak,
                ConfigDirectory = "/tmp/gzdoom-config"
            });
            Assert.AreEqual("flatpak:org.zdoom.GZDoom", port.Executable);
            Assert.AreEqual("-file", port.FileOption);
            Assert.IsTrue(port.SupportedExtensions.Contains(".pk3"));
        }

        [TestMethod]
        public void FreedoomExtractsExpectedWads()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dl-freedoom-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string zip = Path.Combine(dir, "freedoom.zip");
            try
            {
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    WriteEntry(archive, "freedoom-0.13.0/freedoom1.wad", "iwad1");
                    WriteEntry(archive, "freedoom-0.13.0/freedoom2.wad", "iwad2");
                    WriteEntry(archive, "freedoom-0.13.0/readme.txt", "nope");
                    WriteEntry(archive, "freedoom-0.13.0/other.wad", "skip");
                }

                string dest = Path.Combine(dir, "out");
                var wads = FreedoomInstaller.ExtractWadsFromZip(zip, dest);
                Assert.AreEqual(2, wads.Length);
                Assert.IsTrue(wads.Any(x => Path.GetFileName(x).Equals("freedoom1.wad", StringComparison.OrdinalIgnoreCase)));
                Assert.IsTrue(wads.Any(x => Path.GetFileName(x).Equals("freedoom2.wad", StringComparison.OrdinalIgnoreCase)));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void PreviewIncludesFlatpakWrapper()
        {
            var port = new SourcePortData { Executable = "flatpak:org.zdoom.GZDoom" };
            string preview = SourcePortLaunch.FormatCommand(port, "-iwad doom2.wad");
            Assert.IsTrue(preview.Contains("flatpak"));
            Assert.IsTrue(preview.Contains("org.zdoom.GZDoom --"));
            Assert.IsTrue(preview.Contains("-iwad doom2.wad"));
        }

        [TestMethod]
        public void LinuxSaveDirectoriesIncludeConfigAndFlatpak()
        {
            var dirs = ZDoomSourcePortFlavor.UserSaveGameDirectories;
            Assert.IsTrue(dirs.Any(x => x.IndexOf("gzdoom", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [TestMethod]
        public void SandboxWrapPrefixesFlatpakSpawn()
        {
            var inner = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "flatpak",
                Arguments = "run --filesystem=host --filesystem=home org.zdoom.GZDoom -- -iwad doom2.wad",
                UseShellExecute = false
            };
            var wrapped = SandboxHost.WrapForHost(inner, sandboxed: true);
            Assert.AreEqual("flatpak-spawn", System.IO.Path.GetFileName(wrapped.FileName));
            Assert.IsTrue(wrapped.Arguments.StartsWith("--host -- ", StringComparison.Ordinal));
            Assert.IsTrue(wrapped.Arguments.Contains("flatpak run --filesystem=host"));
            Assert.AreSame(inner, SandboxHost.WrapForHost(inner, sandboxed: false));
            Assert.AreSame(wrapped, SandboxHost.WrapForHost(wrapped, sandboxed: true));
        }

        [TestMethod]
        public void CreateStartInfoWrapsWhenSandboxed()
        {
            var port = new SourcePortData
            {
                Name = "GZDoom",
                Executable = "flatpak:org.zdoom.GZDoom",
                Directory = LauncherPath.NoPath,
                FileOption = "-file"
            };
            var start = SourcePortLaunch.CreateStartInfo(port, "-iwad doom2.wad", Directory.GetCurrentDirectory(), sandboxed: true);
            Assert.AreEqual("flatpak-spawn", Path.GetFileName(start.FileName));
            Assert.IsTrue(start.Arguments.Contains("--host --"));
            Assert.IsTrue(start.Arguments.Contains("org.zdoom.GZDoom --"));
            Assert.IsTrue(start.Arguments.Contains("-iwad doom2.wad"));
        }

        [TestMethod]
        public void FlatpakLauncherUsesXdgDataDirectory()
        {
            bool? previous = SandboxHost.OverrideIsFlatpak;
            try
            {
                SandboxHost.OverrideIsFlatpak = true;
                Assert.IsTrue(SandboxHost.IsFlatpak);
                Assert.IsTrue(LauncherPath.IsInstalled());
                Assert.AreEqual(PlatformPaths.GetXdgDataHome(), LauncherPath.GetDataDirectory());
            }
            finally
            {
                SandboxHost.OverrideIsFlatpak = previous;
            }
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
