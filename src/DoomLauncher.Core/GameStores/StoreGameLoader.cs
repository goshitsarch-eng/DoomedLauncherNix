using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using DoomLauncher.GameStores.Steam;

namespace DoomLauncher.GameStores
{
    public static class StoreGameLoader
    {
        public delegate string GameFinder(StoreGame game);

        public static GameStoreFiles LoadAllStoreGamesFromRegistry()
        {
            var gameFinders = new List<GameFinder> { GetSteamGameFolder, GetGogGameFolder };
            return LoadAllStoreGames(gameFinders);
        }

        private static string GetSteamGameFolder(StoreGame game)
        {
            return SteamLoader.GetGameFolder(SteamRegistry.GetSteamPath(), game);
        }

        private static string GetGogGameFolder(StoreGame game)
        {
            if (game.GogId == null)
                return null;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new List<string>
            {
                Path.Combine(home, "GOG Games", game.Name),
                Path.Combine(home, "Games", "Heroic", "Prefixes", game.Name),
                Path.Combine(home, ".local", "share", "lutris", "runners", "gog", game.Name)
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        public static GameStoreFiles LoadAllStoreGames(List<GameFinder> gameFinders)
        {
            var gameStoreFiles = 
                from game in StoreGame.GAMES_IN_PRIORITY_ORDER
                from finder in gameFinders
                select LoadStoreGame(game, finder(game));

            return gameStoreFiles.Aggregate(GameStoreFiles.EMPTY, (a, b) => a.Combine(b));
        }

        public static GameStoreFiles LoadStoreGame(StoreGame game, string gamePath)
        {
            if (!string.IsNullOrEmpty(gamePath))
                gamePath = PlatformPaths.Normalize(gamePath);

            if (gamePath == null || !Directory.Exists(gamePath))
                return GameStoreFiles.EMPTY;

            List<string> installedIwads =
                (from iwad in game.ExpectedIWadFiles
                 let absolutePath = PlatformPaths.CombineNormalized(gamePath, iwad)
                 where File.Exists(absolutePath)
                 select absolutePath).ToList();

            List<string> installedPwads =
                (from pwad in game.ExpectedPWadFiles
                 let absolutePath = PlatformPaths.CombineNormalized(gamePath, pwad)
                 where File.Exists(absolutePath)
                 select absolutePath).ToList();

            string installedDoom64Exe = null;
            if (game.ExpectedDoom64Exe != null)
            {
                var absolutePath = PlatformPaths.CombineNormalized(gamePath, game.ExpectedDoom64Exe);
                if (File.Exists(absolutePath))
                    installedDoom64Exe = absolutePath;
                else
                {
                    var linuxName = Path.GetFileNameWithoutExtension(game.ExpectedDoom64Exe);
                    var linuxPath = Path.Combine(gamePath, linuxName);
                    if (File.Exists(linuxPath))
                        installedDoom64Exe = linuxPath;
                }
            }

            return new GameStoreFiles(installedIwads, installedPwads, installedDoom64Exe);
        }
    }
}
