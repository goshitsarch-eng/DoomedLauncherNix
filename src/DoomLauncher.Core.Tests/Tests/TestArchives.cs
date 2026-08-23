using DoomLauncher;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnitTest.Tests
{
    [TestClass]
    public class TestArchives
    {
        [TestMethod]
        public void TestCompressedArchives()
        {
            string[] files = new string[] { "archive.zip", "archive.rar", "archive.7z" };

            foreach (string file in files)
            {
                using (IArchiveReader archive = ArchiveReader.Create(Path.Combine("Resources", file)))
                {
                    List<IArchiveEntry> entries = archive.Entries.OrderBy(x => x.FullName).ToList();
                    int index = 0;
                    // .NET zip implementation doesn't include the first folder as an entry...
                    if (archive is ZipArchiveReader)
                    {
                        Assert.AreEqual(5, entries.Count);
                    }
                    else
                    {
                        Assert.AreEqual(6, entries.Count);
                        Assert.AreEqual("Folder/", GetFullPath(entries[0]));
                        Assert.IsTrue(entries[0].IsDirectory);
                        index = 1;
                    }

                    Assert.AreEqual("Folder/SubFolder/", GetFullPath(entries[index]));
                    Assert.IsTrue(entries[index].IsDirectory);

                    Assert.AreEqual("Folder/SubFolder/othertextfile.txt", GetFullPath(entries[index + 1]));
                    Assert.IsFalse(entries[index + 1].IsDirectory);

                    Assert.AreEqual("Folder/switch.WAD", GetFullPath(entries[index + 2]));
                    Assert.IsFalse(entries[index + 2].IsDirectory);

                    Assert.AreEqual("Folder/textfile.txt", GetFullPath(entries[index + 3]));
                    Assert.IsFalse(entries[index + 3].IsDirectory);

                    Assert.AreEqual("mapinfo.txt", GetFullPath(entries[index + 4]));
                    Assert.IsFalse(entries[index + 4].IsDirectory);
                }
            }
        }

        private static string GetFullPath(IArchiveEntry entry)
        {
            string path = entry.FullName.Replace("\\", "/");
            if (entry.IsDirectory && !path.EndsWith("/"))
                path += "/";
            return path;
        }
    }
}
