using System;
using System.Collections.Generic;
using System.IO;

namespace DoomLauncher.GameStores.Steam
{
    public static class SteamRegistry
    {
        public static string GetSteamPath()
        {
            if (PlatformPaths.IsWindows)
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                foreach (var candidate in new[]
                {
                    Path.Combine(programFiles, "Steam"),
                    Path.Combine(pf64, "Steam"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam")
                })
                {
                    if (Directory.Exists(candidate))
                        return candidate;
                }
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var linuxCandidates = new List<string>
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
                Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam")
            };

            string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrEmpty(xdg))
                linuxCandidates.Insert(0, Path.Combine(xdg, "Steam"));

            foreach (var candidate in linuxCandidates)
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }
}
