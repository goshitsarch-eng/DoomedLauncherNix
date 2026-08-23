using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DoomLauncher.Archive.SevenZip
{
    public class SevenZipArchiveReader : IArchiveReader
    {
        private readonly SharpCompress.Archives.IArchive m_archive;
        private readonly MemoryStreamManager m_streamManager = new MemoryStreamManager();

        public SevenZipArchiveReader(string file)
        {
            m_archive = SevenZipArchive.OpenArchive(new FileInfo(file));
        }

        public IEnumerable<IArchiveEntry> Entries
        {
            get
            {
                foreach (var entry in m_archive.Entries.Where(x => x != null))
                    yield return new SevenZipArchiveEntry(entry, m_streamManager);
            }
        }

        public bool EntriesHaveExtensions => true;

        public void Dispose()
        {
            m_archive.Dispose();
            foreach (var stream in m_streamManager.Streams)
                stream.Dispose();
        }
    }

    public class MemoryStreamManager
    {
        public List<MemoryStream> Streams = new List<MemoryStream>();
    }
}
