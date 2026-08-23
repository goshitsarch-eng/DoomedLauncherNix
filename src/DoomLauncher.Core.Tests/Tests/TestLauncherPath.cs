using DoomLauncher;
using DoomLauncher.Handlers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace UnitTest.Tests
{
    [TestClass]
    public class TestLauncherPath
    {
        [TestMethod]
        public void TestPaths()
        {
            string folder = "relativepath" + Path.DirectorySeparatorChar;
            LauncherPath path = new LauncherPath(folder);

            Assert.AreEqual(folder, path.GetPossiblyRelativePath());
            Assert.AreEqual(Path.Combine(Directory.GetCurrentDirectory(), folder), path.GetFullPath());

            folder = Path.Combine("subdir", "relativepath") + Path.DirectorySeparatorChar;
            string newfolder = Path.Combine(Directory.GetCurrentDirectory(), folder);

            path = new LauncherPath(newfolder);

            Assert.AreEqual(Path.Combine("subdir", "relativepath"), path.GetPossiblyRelativePath());
            Assert.AreEqual(Path.Combine(Directory.GetCurrentDirectory(), folder), path.GetFullPath());
        }

        [TestMethod]
        public void LauncherPathFile()
        {
            string file = Path.IsPathRooted("/directory/files/file.zip")
                ? Path.Combine(Path.DirectorySeparatorChar.ToString(), "directory", "files", "file.zip")
                : "C:\\directory\\files\\file.zip";
            if (Path.DirectorySeparatorChar == '/')
                file = "/directory/files/file.zip";
            var launcherPath = new LauncherPath(file);
            Assert.AreEqual(file, launcherPath.GetFullPath());
            Assert.AreEqual(file, launcherPath.GetPossiblyRelativePath());
        }

        [TestMethod]
        public void LauncherPathRelativeFile()
        {
            string relative = Path.Combine("files", "file.zip");
            string file = Path.Combine(Directory.GetCurrentDirectory(), relative);
            var launcherPath = new LauncherPath(file);
            Assert.AreEqual(file, launcherPath.GetFullPath());
            Assert.AreEqual(relative, launcherPath.GetPossiblyRelativePath());
        }

        [TestMethod]
        public void GetRelativeDirectory()
        {
            string relative = Path.Combine("something", "local") + Path.DirectorySeparatorChar;
            string path = Path.Combine(Directory.GetCurrentDirectory(), relative);
            var launcherPath = new LauncherPath(path);
            Assert.AreEqual(path, launcherPath.GetFullPath());
            Assert.AreEqual(Path.Combine("something", "local"), launcherPath.GetPossiblyRelativePath());
        }

        [TestMethod]
        public void Empty()
        {
            var launcherPath = new LauncherPath(string.Empty);
            Assert.AreEqual(string.Empty, launcherPath.GetFullPath());
            Assert.AreEqual(string.Empty, launcherPath.GetPossiblyRelativePath());
        }

        [TestMethod]
        public void PartiallyQualified()
        {
            Assert.IsTrue(PathExtensions.IsPartiallyQualified("somefile"));
            Assert.IsTrue(PathExtensions.IsPartiallyQualified("somefile\\"));
            Assert.IsTrue(PathExtensions.IsPartiallyQualified("\\somefile"));
            Assert.IsTrue(PathExtensions.IsPartiallyQualified("\\somefile\\"));

            Assert.IsFalse(PathExtensions.IsPartiallyQualified("c:\\somefile"));
            Assert.IsFalse(PathExtensions.IsPartiallyQualified("c:\\somefile\\"));
            Assert.IsFalse(PathExtensions.IsPartiallyQualified("/usr/bin"));
        }
    }
}
