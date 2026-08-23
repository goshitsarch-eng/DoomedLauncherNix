using DoomLauncher.Handlers;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DoomLauncher
{
    public static class PlatformPaths
    {
        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static string Normalize(string path)
        {
            return PathExtensions.Normalize(path);
        }

        public static string CombineNormalized(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative))
                return root;
            var parts = relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var result = root;
            foreach (var part in parts)
                result = Path.Combine(result, part);
            return result;
        }

        public static string GetXdgDataHome()
        {
            string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrEmpty(xdg))
                return Path.Combine(xdg, "doomlauncher");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "doomlauncher");
        }

        public static string GetXdgConfigHome()
        {
            string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrEmpty(xdg))
                return Path.Combine(xdg, "doomlauncher");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "doomlauncher");
        }
    }
}
