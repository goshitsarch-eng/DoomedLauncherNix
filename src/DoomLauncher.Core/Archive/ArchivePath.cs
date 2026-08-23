using System;
using System.IO;

namespace DoomLauncher
{
    /// <summary>
    /// Helpers that make extracting untrusted archive entries safe against
    /// path-traversal ("zip slip") attacks. Malicious archives can contain
    /// entry names such as "../../.config/autostart/evil.desktop" or absolute
    /// paths; combining those directly with a destination directory would write
    /// files outside of it.
    /// </summary>
    public static class ArchivePath
    {
        /// <summary>
        /// Combines <paramref name="root"/> with an archive-supplied relative
        /// path and verifies the fully resolved destination stays inside
        /// <paramref name="root"/>. Throws <see cref="IOException"/> when the
        /// entry would escape the destination directory.
        /// </summary>
        public static string CombineWithinDirectory(string root, string relativePath)
        {
            if (string.IsNullOrEmpty(root))
                throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(relativePath))
                throw new IOException("Archive entry has an empty name.");

            string normalizedRelative = relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            // Reject rooted/absolute entry paths outright; combining them would
            // ignore the destination directory entirely.
            if (Path.IsPathRooted(normalizedRelative))
                throw new IOException($"Archive entry '{relativePath}' uses an absolute path.");

            string rootFull = Path.GetFullPath(root);
            string combined = Path.GetFullPath(Path.Combine(rootFull, normalizedRelative));

            string rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;

            if (!combined.StartsWith(rootWithSeparator, StringComparison.Ordinal)
                && !string.Equals(combined, rootFull, StringComparison.Ordinal))
            {
                throw new IOException($"Archive entry '{relativePath}' escapes the destination directory.");
            }

            return combined;
        }

        /// <summary>
        /// Returns just the file-name portion of an archive entry name, dropping
        /// any directory components or traversal segments. Use this when the
        /// caller only wants to extract into a flat directory. Throws
        /// <see cref="IOException"/> when nothing usable remains.
        /// </summary>
        public static string SafeFileName(string entryName)
        {
            if (string.IsNullOrEmpty(entryName))
                throw new IOException("Archive entry has an empty name.");

            string normalized = entryName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            string fileName = Path.GetFileName(normalized);
            if (string.IsNullOrEmpty(fileName) || fileName == "." || fileName == "..")
                throw new IOException($"Archive entry '{entryName}' does not have a valid file name.");

            return fileName;
        }
    }
}
