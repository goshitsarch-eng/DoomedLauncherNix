using System.Collections.Generic;
using System.IO;

namespace DoomLauncher.GameStores.Steam
{
    public static class SteamLoader
    {
        public static string GetGameFolder(string steamPath, StoreGame game)
        {
            if (Directory.Exists(steamPath))
            {
                var libraryPaths = GetLibraryPaths(steamPath);
                foreach (var libraryPath in libraryPaths)
                {
                    var gameDirectory = GetGameDirectory(libraryPath, game.SteamId);
                    if (gameDirectory != null)
                    {
                        return PlatformPaths.CombineNormalized(libraryPath, Path.Combine("steamapps", "common", gameDirectory));
                    }
                }
            }

            return null;
        }

        private static List<string> GetLibraryPaths(string steamPath)
        {
            var vdfPath = PlatformPaths.CombineNormalized(steamPath, Path.Combine("config", "libraryfolders.vdf"));
            if (SteamFileUtils.TryGetLibraryPaths(vdfPath, out var libraryPaths))
            {
                return libraryPaths;
            }
            return new List<string>();
        }

        private static string GetGameDirectory(string libraryPath, int gameSteamId)
        {
            var acfPath = PlatformPaths.CombineNormalized(libraryPath, Path.Combine("steamapps", $"appmanifest_{gameSteamId}.acf"));
            if (SteamFileUtils.TryGetInstallDir(acfPath, out var installDir))
            {
                return installDir;
            }

            return null;
        }
    }
}
