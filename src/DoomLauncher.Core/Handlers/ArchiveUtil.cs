using DoomLauncher.Archive.Rar;
using DoomLauncher.Archive.SevenZip;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace DoomLauncher
{
    public static class ArchiveUtil
    {
        public static readonly string[] Exenstions = new string[] { ".7z", ".rar" };
        private static readonly string[] CoreExtensions = new string[] { ".zip", ".7z", ".rar" };

        public static bool IsTransformableToZip(string extension) => Exenstions.Contains(extension, StringComparer.OrdinalIgnoreCase);

        public static bool ShouldReadPackagedArchive(string file) => CoreExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

        public static bool CreateZipFrom(FileInfo fi, string tempDirectory, out FileInfo fileInfo)
        {
            fileInfo = null;
            try
            {
                if (fi.Extension.Equals(".7z", StringComparison.OrdinalIgnoreCase))
                    fileInfo = CreateZipFromSevenZip(fi, tempDirectory);
                else if (fi.Extension.Equals(".rar", StringComparison.OrdinalIgnoreCase))
                    fileInfo = CreateZipFromRar(fi, tempDirectory);

                if (fileInfo != null)
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private static FileInfo CreateZipFromSevenZip(FileInfo fi, string tempDirectory)
        {
            using (var archive = new SevenZipArchiveReader(fi.FullName))
            {
                DirectoryInfo dir = Directory.CreateDirectory(Path.Combine(tempDirectory, Guid.NewGuid().ToString()));
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory)
                    {
                        Directory.CreateDirectory(Path.Combine(dir.FullName, entry.FullName));
                        continue;
                    }

                    string dest = Path.Combine(dir.FullName, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                }

                string zipFile = Path.Combine(tempDirectory, fi.Name.Replace(fi.Extension, ".zip"));
                if (File.Exists(zipFile))
                    File.Delete(zipFile);
                ZipFile.CreateFromDirectory(dir.FullName, zipFile);

                Directory.Delete(dir.FullName, true);
                return new FileInfo(zipFile);
            }
        }

        private static FileInfo CreateZipFromRar(FileInfo fi, string tempDirectory)
        {
            using (RarArchiveReader archive = new RarArchiveReader(fi.FullName))
            {
                DirectoryInfo dir = Directory.CreateDirectory(Path.Combine(tempDirectory, Guid.NewGuid().ToString()));

                var directoryEntries = archive.Entries.Where(entry => entry.IsDirectory);
                foreach (var dirEntry in directoryEntries)
                    Directory.CreateDirectory(Path.Combine(dir.FullName, dirEntry.FullName));

                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory)
                        continue;

                    try
                    { 
                        string path = dir.FullName;
                        if (entry.FullName.Contains(Path.DirectorySeparatorChar) || entry.FullName.Contains(Path.AltDirectorySeparatorChar) || entry.FullName.Contains('/'))
                            path = Path.Combine(dir.FullName, GetSubDirectoryPath(entry));

                        Directory.CreateDirectory(path);
                        entry.ExtractToFile(Path.Combine(path, entry.Name), true);
                    }
                    catch
                    {
                    }
                }

                string zipFile = Path.Combine(tempDirectory, fi.Name.Replace(fi.Extension, ".zip"));
                if (File.Exists(zipFile))
                    File.Delete(zipFile);
                ZipFile.CreateFromDirectory(dir.FullName, zipFile);

                Directory.Delete(dir.FullName, true);
                return new FileInfo(zipFile);
            }
        }

        private static string GetSubDirectoryPath(IArchiveEntry entry)
        {
            string name = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            int index = name.LastIndexOf(Path.DirectorySeparatorChar);

            if (index == -1)
                return string.Empty;
            
            return name.Substring(0, index);
        }
    }
}
