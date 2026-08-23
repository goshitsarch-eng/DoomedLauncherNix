using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DoomLauncher
{
    public static class SpecificFilesUtil
    {
        public static string[] GetSupportedFiles(string gameFileDirectory, IGameFile gameFile, string[] supportedExtensions)
        {
            var files = new List<string>();
            string path = Path.Combine(gameFileDirectory, gameFile.FileName);

            if (gameFile.IsDirectory() || (gameFile.IsUnmanaged() && !ArchiveUtil.ShouldReadPackagedArchive(gameFile.FileName)))
            {
                files.Add(gameFile.FileName);
                return files.ToArray();
            }

            using (IArchiveReader reader = CreateArchiveReader(gameFile, path))
            {
                foreach (IArchiveEntry entry in reader.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;
                    if (!supportedExtensions.Any(x => x.Equals(Path.GetExtension(entry.Name), StringComparison.OrdinalIgnoreCase)))
                        continue;
                    files.Add(entry.FullName);
                }
            }

            return files.ToArray();
        }

        public static IArchiveReader CreateArchiveReader(IGameFile gameFile, string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return ArchiveReader.EmptyArchiveReader;

            bool isPackagedArchive = ArchiveUtil.ShouldReadPackagedArchive(path);
            if (gameFile.IsUnmanaged() && !isPackagedArchive)
                return new FileArchiveReader(path);

            if (isPackagedArchive)
                return ArchiveReader.Create(path);

            if (ArchiveReader.IsPk(Path.GetExtension(path)))
                return new ZipArchiveReader(path);

            return ArchiveReader.Create(path);
        }
    }
}
