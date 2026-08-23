using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DoomLauncher.SourcePort
{
    public class ZDoomSourcePortFlavor : GenericSourcePortFlavor
    {
        private static readonly string[] DirectoryNames = new string[] { "GZDoom", "UZDoom", "VKDoom" };

        private static string UserDirectoryBase => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public static string[] UserSaveGameDirectories => GetLinuxAwareDirectories(true);
        public static string[] UserScreenshotDirectories => GetLinuxAwareDirectories(false);

        public static string UserSaveGameDirectory(string name) => Path.Combine(UserDirectoryBase, "Saved Games", name);
        public static string UserScreenshotDirectory(string name) => Path.Combine(UserDirectoryBase, "Pictures", "Screenshots", name);

        private static string[] GetLinuxAwareDirectories(bool saves)
        {
            var dirs = new List<string>();
            dirs.AddRange(DirectoryNames.Select(x => saves ? UserSaveGameDirectory(x) : UserScreenshotDirectory(x)));

            if (!PlatformPaths.IsWindows)
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(xdgConfig))
                    xdgConfig = Path.Combine(home, ".config");

                foreach (var name in new[] { "gzdoom", "uzdoom", "vkdoom", "lzdoom", "zandronum" })
                    dirs.Add(Path.Combine(xdgConfig, name));

                dirs.Add(Path.Combine(home, ".var", "app", "org.zdoom.GZDoom", "config", "gzdoom"));
                dirs.Add(Path.Combine(home, ".var", "app", "org.zdoom.GZDoom", ".config", "gzdoom"));
                dirs.Add(Path.Combine(home, ".var", "app", "org.zdoom.UZDoom", "config", "uzdoom"));
                dirs.Add(Path.Combine(home, ".var", "app", "org.zdoom.VKDoom", "config", "vkdoom"));
            }

            return dirs.Distinct(StringComparer.Ordinal).ToArray();
        }

        public ZDoomSourcePortFlavor(ISourcePortData sourcePortData)
            : base(sourcePortData)
        {

        }

        public override bool Supported() =>
            SourcePortLaunch.IsZDoomFamily(m_sourcePortData?.Executable) ||
            CheckFileNameContains("zdoom.exe") ||
            CheckFileNameWithoutExtension("zandronum") ||
            CheckFileNameWithoutExtension("vkdoom");

        public override string LoadSaveParameter(SpData data) =>
            $"-loadgame \"{data.Value}\"";

        public override bool StatisticsSupported() => true;

        public override string[] GetScreenshotDirectories() => UserScreenshotDirectories;

        public override string[] GetSaveGameDirectories() => UserSaveGameDirectories;

        public override IStatisticsReader CreateStatisticsReader(IGameFile gameFile, IEnumerable<IStatsData> existingStats)
        {
            string dir = m_sourcePortData.Directory?.GetFullPath();
            if (string.IsNullOrEmpty(dir))
                dir = m_sourcePortData.AltSaveDirectory?.GetFullPath();
            if (string.IsNullOrEmpty(dir))
                dir = Directory.GetCurrentDirectory();
            return new ZDoomStatsReader(gameFile, dir, existingStats);
        }

        public override string WarpParameter(SpData data) => GetMapParameter(data.Value);
    }
}
