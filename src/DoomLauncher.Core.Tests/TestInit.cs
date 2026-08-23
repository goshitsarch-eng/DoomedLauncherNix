using Microsoft.VisualStudio.TestTools.UnitTesting;
using DoomLauncher;
using UnitTest.Tests;
using System.IO;

[assembly: DoNotParallelize]

namespace UnitTest
{
    [TestClass]
    public static class TestInit
    {
        [AssemblyInitialize()]
        public static void TestInitialize(TestContext testContext)
        {
            // The test process may itself run inside a Flatpak SDK while validating
            // the application package. Unit tests exercise native-host semantics
            // unless a test passes sandboxed: true explicitly.
            SandboxHost.OverrideIsFlatpak = false;

            DbDataSourceAdapter adapter = (DbDataSourceAdapter)TestUtil.CreateAdapter();

            DataCache.Instance.Init(adapter);

            VersionHandler versionHandler = new VersionHandler(adapter.DataAccess, adapter, new AppConfiguration(adapter));
            versionHandler.HandleVersionUpdate();
        }
    }
}
