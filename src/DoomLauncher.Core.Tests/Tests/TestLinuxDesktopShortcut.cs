using DoomLauncher;
using DoomLauncher.Handlers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace UnitTest.Tests
{
    [TestClass]
    public class TestLinuxDesktopShortcut
    {
        [TestMethod]
        public void FlatpakShortcutReentersTheApplicationThroughFlatpakRun()
        {
            SandboxHost.OverrideIsFlatpak = true;
            try
            {
                Assert.AreEqual(
                    "flatpak run com.goshapps.DoomLauncher",
                    LibraryOperations.GetDesktopShortcutExecutable());
                Assert.AreEqual(
                    "com.goshapps.DoomLauncher",
                    LibraryOperations.GetDesktopShortcutIcon());
            }
            finally
            {
                SandboxHost.OverrideIsFlatpak = false;
            }
        }

        [TestMethod]
        public void NativeShortcutUsesTheInstalledExecutableAndIcon()
        {
            SandboxHost.OverrideIsFlatpak = false;
            Assert.AreEqual(
                Path.Combine(AppContext.BaseDirectory, "DoomLauncher"),
                LibraryOperations.GetDesktopShortcutExecutable());
            Assert.AreEqual(
                Path.Combine(AppContext.BaseDirectory, "DoomLauncher.ico"),
                LibraryOperations.GetDesktopShortcutIcon());
        }
    }
}
