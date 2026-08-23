using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace DoomLauncher
{
    class RenameFile
    {
        public RenameFile(string originalName, string newName)
        {
            OriginalName = originalName;
            NewName = newName;
        }

        public string OriginalName;
        public string NewName;
    }

    public class ApplicationUpdater
    {
        private static readonly string s_updateExtension = ".bak";
        private readonly string m_zipArchive;
        private readonly string m_executingDirectory;

        public string LastError { get; private set; } = string.Empty;

        public ApplicationUpdater(string zipArchive, string executingDirectory)
        {
            m_zipArchive = zipArchive;
            m_executingDirectory = executingDirectory;
        }

        public bool Execute()
        {
            if (LauncherPath.IsInstalled() && PlatformPaths.IsWindows)
                return ExecuteInstallUpdate();
            return ExecuteNonInstallUpdate();
        }

        private bool ExecuteInstallUpdate()
        {
            try
            {
                string extractFolder = Path.Combine(Path.GetTempPath(), "DoomLauncherUpdate");
                if (Directory.Exists(extractFolder))
                    Directory.Delete(extractFolder, true);
                using (ZipArchive za = ZipFile.OpenRead(m_zipArchive))
                    za.ExtractToDirectory(extractFolder);

                string setup = Path.Combine(extractFolder, "setup.exe");
                if (File.Exists(setup))
                {
                    Process.Start(setup);
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                SetLastErrorException(ex);
                return false;
            }

            return true;
        }

        private bool ExecuteNonInstallUpdate()
        {
            List<RenameFile> renamedFiles = new List<RenameFile>();

            try
            {
                using (ZipArchive za = ZipFile.OpenRead(m_zipArchive))
                {
                    var entries = za.Entries.Where(x => x.Name.Length > 0 &&
                        (x.FullName.StartsWith("DoomLauncher\\") || x.FullName.StartsWith("DoomLauncher/")));

                    if (!entries.Any())
                        entries = za.Entries.Where(x => x.Name.Length > 0 && !x.FullName.EndsWith("/"));

                    if (!entries.Any())
                    {
                        LastError = "Could not find DoomLauncher files in archive.";
                        return false;
                    }

                    foreach (var entry in entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;
                        string file = GetCopyFile(entry);
                        Directory.CreateDirectory(Path.GetDirectoryName(file));
                        FileInfo fileInfo = new FileInfo(file);

                        if (fileInfo.Exists)
                            renamedFiles.Add(new RenameFile(fileInfo.Name, RenameAppFile(fileInfo)));
                        entry.ExtractToFile(file, true);
                    }
                }
            }
            catch (Exception ex)
            {
                SetLastErrorException(ex);
                RevertRenamedFiles(renamedFiles);
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(m_executingDirectory, Util.GetExecutableNoPath()),
                    WorkingDirectory = m_executingDirectory,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
            return true;
        }

        private string GetCopyFile(ZipArchiveEntry entry)
        {
            if (entry.FullName.Contains("x86"))
                return Path.Combine(Directory.GetCurrentDirectory(), "x86", entry.Name);
            if (entry.FullName.Contains("x64"))
                return Path.Combine(Directory.GetCurrentDirectory(), "x64", entry.Name);

            return Path.Combine(Directory.GetCurrentDirectory(), entry.Name);
        }

        private void SetLastErrorException(Exception ex)
        {
            LastError = string.Concat("**ERROR TRACE**", Environment.NewLine, ex.Message, Environment.NewLine, ex.StackTrace);
        }

        private void RevertRenamedFiles(List<RenameFile> renamedFiles)
        {
            foreach (RenameFile file in renamedFiles)
            {
                if (File.Exists(file.OriginalName))
                    File.Delete(file.OriginalName);

                File.Move(file.NewName, file.OriginalName);
            }
        }

        private string RenameAppFile(FileInfo fileInfo)
        {
            string backupName = CreateBackupFileName(fileInfo);
            if (File.Exists(backupName))
                File.Delete(backupName);
            fileInfo.MoveTo(backupName);
            return backupName;
        }

        private string CreateBackupFileName(FileInfo fileInfo)
        {
            return fileInfo.FullName + s_updateExtension;
        }

        public static void CleanupUpdateFiles(string executingDirectory)
        {
            var files = Directory.GetFiles(executingDirectory).Where(x => x.EndsWith(s_updateExtension));

            try
            {
                foreach (string file in files)
                    File.Delete(file);
            }
            catch
            {
            }
        }
    }
}
