using SharpCompress.Archives;
using System;
using System.IO;

namespace DoomLauncher.Archive.SevenZip
{
    public class SevenZipArchiveEntry : AbstractArchiveEntry
    {
        private readonly SharpCompress.Archives.IArchiveEntry m_entry;
        private readonly MemoryStreamManager m_streamManager;
        private MemoryStream m_ms;

        public SevenZipArchiveEntry(SharpCompress.Archives.IArchiveEntry entry, MemoryStreamManager streamManager)
        {
            m_entry = entry;
            m_streamManager = streamManager;
        }

        public override long Length => m_entry.Size;

        public override string Name => Path.GetFileName(m_entry.Key);

        public override string FullName => m_entry.Key;

        public override bool ExtractRequired => true;

        public override bool IsDirectory => m_entry.IsDirectory;

        public override void ExtractToFile(string file, bool overwrite = false)
        {
            if (!overwrite && File.Exists(file))
                return;

            m_entry.WriteToFile(file);
        }

        public override void Read(byte[] buffer, int offset, int length)
        {
            if (m_ms == null)
            {
                m_ms = new MemoryStream();
                m_streamManager.Streams.Add(m_ms);
                m_entry.WriteTo(m_ms);
            }

            m_ms.Position = 0;
            m_ms.ReadExactly(buffer.AsSpan(offset, length));
        }

        public override bool Equals(object obj)
        {
            if (!(obj is IArchiveEntry entry))
                return false;

            return entry.FullName == FullName;
        }

        public override int GetHashCode()
        {
            return FullName.GetHashCode();
        }

        public override string GetNameWithoutExtension() => Path.GetFileNameWithoutExtension(Name);
    }
}
