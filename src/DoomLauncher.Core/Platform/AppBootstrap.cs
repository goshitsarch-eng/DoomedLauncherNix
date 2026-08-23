using DoomLauncher.Config;
using DoomLauncher.DataSources;
using DoomLauncher.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace DoomLauncher
{
    public static class AppBootstrap
    {
        public static bool Init(out string error)
        {
            error = null;
            CleanOldLibraries();

            if (!VerifyDatabase(out error))
                return false;

            IDataSourceAdapter dataSourceAdapter = DbDataSourceAdapter.CreateAdapter();
            DataCache.Instance.Init(dataSourceAdapter);

            var dbAdapter = dataSourceAdapter as DbDataSourceAdapter;
            if (dbAdapter != null)
            {
                var versionHandler = new VersionHandler(dbAdapter.DataAccess, DbDataSourceAdapter.CreateAdapter(true), DataCache.Instance.AppConfiguration);
                versionHandler.HandleVersionUpdate();
                dataSourceAdapter = DbDataSourceAdapter.CreateAdapter();
                DataCache.Instance.Init(dataSourceAdapter);
            }

            BackupDatabase(Path.Combine(LauncherPath.GetDataDirectory(), DbDataSourceAdapter.DatabaseFileName));
            KillRunningApps();

            if (!VerifyGameFilesDirectory(out error))
                return false;

            ApplySystemTheme();
            return true;
        }

        public static void ApplySystemTheme()
        {
            try
            {
                var adapter = DataCache.Instance.DataSourceAdapter;
                if (DataCache.Instance.AppConfiguration.ConfigurationColorTheme != ColorThemeType.System &&
                    adapter.GetSourcePorts().Any())
                {
                    DataCache.Instance.AppConfiguration.ColorTheme = DataCache.Instance.AppConfiguration.ConfigurationColorTheme;
                    return;
                }

                DataCache.Instance.AppConfiguration.ColorTheme = ColorThemeType.Dark;
                ColorTheme.Current = ColorThemeType.Dark;
            }
            catch
            {
            }
        }

        private static bool VerifyGameFilesDirectory(out string error)
        {
            error = null;
            try
            {
                if (!Directory.Exists(DataCache.Instance.AppConfiguration.GameFileDirectory.GetFullPath()))
                {
                    string basePath = Path.Combine(LauncherPath.GetDataDirectory(), "GameFiles");
                    Directory.CreateDirectory(Path.Combine(basePath, "Demos"));
                    Directory.CreateDirectory(Path.Combine(basePath, "SaveGames"));
                    Directory.CreateDirectory(Path.Combine(basePath, "Screenshots"));
                    Directory.CreateDirectory(Path.Combine(basePath, "Temp"));
                    Directory.CreateDirectory(Path.Combine(basePath, "Thumbnails"));
                    Directory.CreateDirectory(Path.Combine(basePath, "TitlePics"));
                }

                CopyTileImages();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void CopyTileImages()
        {
            string dest = Path.Combine(LauncherPath.GetDataDirectory(), "TileImages");
            Directory.CreateDirectory(dest);
            foreach (var src in new[]
            {
                Path.Combine(AppContext.BaseDirectory, "TileImages"),
                Path.Combine(Directory.GetCurrentDirectory(), "TileImages")
            })
            {
                if (!Directory.Exists(src))
                    continue;
                foreach (var file in Directory.GetFiles(src))
                {
                    string target = Path.Combine(dest, Path.GetFileName(file));
                    if (!File.Exists(target))
                        File.Copy(file, target);
                }
                break;
            }
        }

        private static void BackupDatabase(string dataSource)
        {
            FileInfo fi = new FileInfo(dataSource);
            if (!fi.Exists)
                return;

            Directory.CreateDirectory(Path.Combine(LauncherPath.GetDataDirectory(), "Backup"));
            string backupName = Path.Combine(fi.DirectoryName, "Backup", fi.Name.Replace(fi.Extension, DateTime.Now.ToString("yyyy_MM_dd") + fi.Extension));
            if (!File.Exists(backupName))
                fi.CopyTo(backupName);

            string[] files = Directory.GetFiles(Path.Combine(LauncherPath.GetDataDirectory(), "Backup"), "*.sqlite");
            var filesInfoOrdered = files.Select(x => new FileInfo(x)).OrderBy(x => x.CreationTime).ToList();
            while (filesInfoOrdered.Count > 10)
            {
                filesInfoOrdered.First().Delete();
                filesInfoOrdered.RemoveAt(0);
            }
        }

        private static bool VerifyDatabase(out string error)
        {
            error = null;
            try
            {
                string dest = Path.Combine(LauncherPath.GetDataDirectory(), DbDataSourceAdapter.DatabaseFileName);
                if (File.Exists(dest))
                {
                    if (File.Exists(DbDataSourceAdapter.InitDatabaseFileName))
                        File.Delete(DbDataSourceAdapter.InitDatabaseFileName);
                    return true;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                string init = Path.Combine(AppContext.BaseDirectory, DbDataSourceAdapter.DatabaseFileName);
                if (!File.Exists(init))
                    init = Path.Combine(Directory.GetCurrentDirectory(), DbDataSourceAdapter.DatabaseFileName);
                if (!File.Exists(init))
                    init = Path.Combine(AppContext.BaseDirectory, DbDataSourceAdapter.InitDatabaseFileName);

                if (File.Exists(init))
                {
                    File.Copy(init, dest, true);
                    return true;
                }

                error = "Initialization failure. Could not find DoomLauncher database";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void CleanOldLibraries()
        {
            if (LauncherPath.IsInstalled())
                return;

            try
            {
                foreach (string library in new[] { "SQLite.Interop.dll", "SQLite.Interop.dll.bak", "7z.dll" })
                {
                    if (File.Exists(library))
                        File.Delete(library);
                }
            }
            catch
            {
            }
        }

        private static void KillRunningApps()
        {
            try
            {
                Process currentProc = Process.GetCurrentProcess();
                foreach (Process proc in Process.GetProcessesByName("DoomLauncher").Where(x => x.Id != currentProc.Id))
                {
                    proc.CloseMainWindow();
                    var sw = Stopwatch.StartNew();
                    while (sw.Elapsed.TotalSeconds < 10 && !proc.HasExited)
                    {
                        System.Threading.Thread.Sleep(100);
                        proc.Refresh();
                    }
                }
            }
            catch
            {
            }
        }

        public static void CreateLinuxDesktopEntry()
        {
            if (!PlatformPaths.IsLinux)
                return;

            try
            {
                string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
                Directory.CreateDirectory(apps);
                string exe = Path.Combine(AppContext.BaseDirectory, "DoomLauncher");
                string desktop = Path.Combine(apps, "io.github.doomedlaunchernix.DoomLauncher.desktop");
                File.WriteAllText(desktop, $@"[Desktop Entry]
Type=Application
Name=Doom Launcher
Comment=Doom frontend and wad library
Exec=""{exe}"" %F
Icon=io.github.doomedlaunchernix.DoomLauncher
Terminal=false
Categories=Game;
MimeType=application/x-doom;application/x-wad;
");
            }
            catch
            {
            }
        }
    }
}
