using System;
using System.IO;

namespace DoomLauncher.Handlers
{
    public static class PathExtensions
    {
        public static readonly char[] DirectorySeparators = { '/', '\\' };

        // Path.IsRootedPath incorrectly allows path starting with directory separated chars to be counted as rooted.
        // .NET Framework does not have this function so it is copied from .NET core source
        public static bool IsPartiallyQualified(string path)
        {
            if (path.Length < 2)
            {
                // It isn't fixed, it must be relative.  There is no way to specify a fixed
                // path with one character (or less).
                return true;
            }

            if (IsDirectorySeparator(path[0]))
            {
                // UNC, device paths, or Unix absolute paths are fully qualified.
                if (path[1] == '?' || IsDirectorySeparator(path[1]))
                    return false;
                if (path[0] == '/')
                    return false;
                return true;
            }

            // The only way to specify a fixed path that doesn't begin with two slashes
            // is the drive, colon, slash format- i.e. C:\
            return !((path.Length >= 3)
                && (path[1] == ':' || path[1] == Path.VolumeSeparatorChar)
                && IsDirectorySeparator(path[2])
                // To match old behavior we'll check the drive character for validity as the path is technically
                // not qualified if you don't have a valid drive. "=:\" is the "=" file's default data stream.
                && IsValidDriveChar(path[0]));
        }

        public static string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            int i = path.LastIndexOfAny(DirectorySeparators);
            return i >= 0 ? path.Substring(i + 1) : path;
        }

        public static string GetFileNameWithoutExtension(string path)
        {
            return Path.GetFileNameWithoutExtension(GetFileName(path));
        }

        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        }

        internal static bool IsDirectorySeparator(char c)
        {
            return c == '/' || c == '\\';
        }

        internal static bool IsValidDriveChar(char value)
        {
            return ((value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z'));
        }
    }
}
