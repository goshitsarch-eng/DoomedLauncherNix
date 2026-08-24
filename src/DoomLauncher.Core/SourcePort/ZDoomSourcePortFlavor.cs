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

                foreach (var name in FamilyNames)
                    dirs.Add(Path.Combine(xdgConfig, name));

                dirs.Add(Path.Combine(home, ".var", "app", "org.zdoom.GZDoom", "config", "gzdoom"));
                dirs.Add(Path.Combine(home, ".var", "app", "org.zdoom.GZDoom", ".config", "gzdoom"));
                dirs.Add(Path.Combine(home, ".var", "app", "org.zdoom.UZDoom", "config", "uzdoom"));
                dirs.Add(Path.Combine(home, ".var", "app", "org.zdoom.VKDoom", "config", "vkdoom"));
                dirs.AddRange(FlatpakConfigDirectories(home));
            }

            return dirs.Distinct(StringComparer.Ordinal).ToArray();
        }

        private static readonly string[] FamilyNames = { "gzdoom", "uzdoom", "vkdoom", "lzdoom", "zandronum" };

        /// <summary>
        /// Saves and statistics for a ZDoom-family port installed as a Flatpak live under
        /// ~/.var/app/&lt;app id&gt;/config. The app id is not always org.zdoom.*, so look at what is
        /// actually installed instead of only checking the three ids we happen to know.
        /// </summary>
        private static IEnumerable<string> FlatpakConfigDirectories(string home)
        {
            string root = Path.Combine(home, ".var", "app");
            string[] apps;
            try
            {
                if (!Directory.Exists(root))
                    yield break;
                apps = Directory.GetDirectories(root);
            }
            catch
            {
                yield break;
            }

            foreach (string app in apps)
            {
                foreach (string name in FamilyNames)
                {
                    foreach (string configRoot in new[] { "config", ".config" })
                    {
                        string candidate = Path.Combine(app, configRoot, name);
                        bool exists;
                        try
                        {
                            exists = Directory.Exists(candidate);
                        }
                        catch
                        {
                            exists = false;
                        }
                        if (exists)
                            yield return candidate;
                    }
                }
            }
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
