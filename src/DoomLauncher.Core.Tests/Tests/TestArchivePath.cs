using System.IO;
using DoomLauncher;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTest.Tests
{
    [TestClass]
    public class TestArchivePath
    {
        private static string Root => Path.Combine(Path.GetTempPath(), "doomlauncher-archivepath-test");

        [TestMethod]
        public void CombineWithinDirectoryAllowsNormalEntry()
        {
            string result = ArchivePath.CombineWithinDirectory(Root, "Folder/textfile.txt");
            string expected = Path.GetFullPath(Path.Combine(Root, Path.Combine("Folder", "textfile.txt")));
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [ExpectedException(typeof(IOException))]
        public void CombineWithinDirectoryRejectsParentTraversal()
        {
            ArchivePath.CombineWithinDirectory(Root, "../evil.txt");
        }

        [TestMethod]
        [ExpectedException(typeof(IOException))]
        public void CombineWithinDirectoryRejectsNestedTraversal()
        {
            ArchivePath.CombineWithinDirectory(Root, "a/b/../../../evil.txt");
        }

        [TestMethod]
        [ExpectedException(typeof(IOException))]
        public void CombineWithinDirectoryRejectsBackslashTraversal()
        {
            ArchivePath.CombineWithinDirectory(Root, "..\\..\\evil.txt");
        }

        [TestMethod]
        [ExpectedException(typeof(IOException))]
        public void CombineWithinDirectoryRejectsAbsolutePath()
        {
            ArchivePath.CombineWithinDirectory(Root, "/etc/passwd");
        }

        [TestMethod]
        public void SafeFileNameStripsDirectoryComponents()
        {
            Assert.AreEqual("evil.desktop", ArchivePath.SafeFileName("../../.config/autostart/evil.desktop"));
            Assert.AreEqual("c.txt", ArchivePath.SafeFileName("a/b/c.txt"));
            Assert.AreEqual("c.txt", ArchivePath.SafeFileName("a\\b\\c.txt"));
            Assert.AreEqual("map.wad", ArchivePath.SafeFileName("map.wad"));
        }

        [TestMethod]
        [ExpectedException(typeof(IOException))]
        public void SafeFileNameRejectsTraversalOnlyEntry()
        {
            ArchivePath.SafeFileName("../");
        }

        [TestMethod]
        [ExpectedException(typeof(IOException))]
        public void SafeFileNameRejectsEmpty()
        {
            ArchivePath.SafeFileName("");
        }
    }
}
