using DoomLauncher.Adapters;
using DoomLauncher.Adapters.Launch;
using DoomLauncher.Config;
using DoomLauncher.DataSources;
using DoomLauncher.Handlers;
using DoomLauncher.Handlers.Sync;
using DoomLauncher.Interfaces;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using static DoomLauncher.GameLauncher;

namespace DoomLauncher
{
    public class PlayLaunchRequest
    {
        public IGameFile GameFile { get; set; }
        public IGameFile SelectedIWad { get; set; }
        public ISourcePortData SelectedSourcePort { get; set; }
        public IGameProfile SelectedGameProfile { get; set; }
        public string SelectedMap { get; set; }
        public string SelectedSkill { get; set; }
        public List<IGameFile> AdditionalFiles { get; set; } = new List<IGameFile>();
        public string[] SpecificFiles { get; set; }
        public string ExtraParameters { get; set; }
        public bool ExtraParametersOnly { get; set; }
        public bool Record { get; set; }
        public bool PlayDemo { get; set; }
        public IFileData SelectedDemo { get; set; }
        public bool SaveStatistics { get; set; }
        public bool LoadLatestSave { get; set; }
        public bool Remember { get; set; } = true;
        public bool ScreenFilter { get; set; }
    }

    public class LibraryOperations
    {
        public IDataSourceAdapter Adapter => DataCache.Instance.DataSourceAdapter;
        public AppConfiguration Config => DataCache.Instance.AppConfiguration;
        public DirectoryDataSourceAdapter DirectoryAdapter { get; }

        public event Action<string, int, int> SyncProgress;
        public event Action<string> StatusChanged;
        public event GameLaunchExitHandler ProcessExited;

        private readonly List<PlaySession> m_activeSessions = new List<PlaySession>();

        public LibraryOperations()
        {
            DirectoryAdapter = new DirectoryDataSourceAdapter(Config.GameFileDirectory);
        }

        public List<ISyncAction> CreateSyncActions()
        {
            var actions = new List<ISyncAction>
            {
                new TextFileSyncAction(new IdGamesTextFileParser(Config.DateParseFormats).Parse),
                new Doom64SyncAction(Adapter),
                new MapStringSyncAction(Config.TempDirectory),
                new GameInfoSyncAction(),
                new StartupImageSyncAction(),
                new TitlePicSyncAction(Adapter, DataCache.Instance.DefaultPalette, DataCache.Instance.HexenPalette, DataCache.Instance.HereticPalette)
                    .OnlyIf(Config.AutomaticallyPullTitlpic),
                new Doom64TitlePicSyncAction(),
                new IWadTitlesSyncAction(),
                new KnownWadsSyncAction(Adapter),
                new GameConfSyncAction(),
            };
            return actions;
        }

        public SyncResult SyncFiles(string[] files, FileManagement fileManagement)
        {
            var handler = new SyncLibraryHandler(Adapter, DirectoryAdapter, Config, fileManagement, CreateSyncActions());
            handler.SyncFileChanged += e => SyncProgress?.Invoke(e.CurrentSyncFileName, e.SyncFileCurrent, e.SyncFileCount);
            var result = handler.SyncManyFiles(files);
            SyncTitlePics(result);
            return result;
        }

        public void SyncTitlePics(SyncResult syncResult)
        {
            var fileHandler = new FileHandler(Adapter, Config);
            var imageHandler = new GameFileImageHandler(fileHandler, id => Adapter.GetIWad(id), Config.DeleteScreenshotsAfterImport);
            foreach (var pair in syncResult.TitlePics)
            {
                try
                {
                    imageHandler.InsertTitlePic(pair.Key, pair.Value);
                }
                catch
                {
                }
            }
        }

        public FileAddResults AddManagedFiles(string[] fileNames, bool overwrite)
        {
            return CopyFiles(fileNames, Config.GameFileDirectory.GetFullPath(), overwrite);
        }

        public FileAddResults AddUnmanagedFiles(string[] fileNames)
        {
            FileAddResults results = new FileAddResults();
            foreach (string fileName in fileNames)
            {
                string zipName = Path.Combine(Config.GameFileDirectory.GetFullPath(), Path.GetFileNameWithoutExtension(fileName) + ".zip");
                IGameFile existingGameFile = Adapter.GetGameFile(fileName);
                if (File.Exists(zipName))
                    results.Errors.Add(new FileError { FileName = fileName, Error = "File already exists as a managed file." });
                else if (existingGameFile != null && !existingGameFile.IsUnmanaged() && Path.IsPathRooted(existingGameFile.FileName))
                    results.Errors.Add(new FileError { FileName = fileName, Error = "File already exists as an unmanaged file." });
                else
                    results.NewFiles.Add(fileName);
            }
            return results;
        }

        public FileAddResults CopyFiles(string[] files, string directory, bool overwrite)
        {
            FileAddResults results = new FileAddResults();
            HashSet<string> addedNames = new HashSet<string>();
            List<string> fileNames = files.ToList();
            fileNames.Sort();
            int count = 0;

            foreach (string file in fileNames)
            {
                if (file.StartsWith(Config.GameFileDirectory.GetFullPath()))
                {
                    results.ReplacedFiles.Add(Path.GetFileName(file));
                    count++;
                    continue;
                }

                StatusChanged?.Invoke($"Copying {file}...");
                FileInfo fi = new FileInfo(file);
                if (ArchiveUtil.IsTransformableToZip(fi.Extension))
                {
                    if (!ArchiveUtil.CreateZipFrom(fi, Config.TempDirectory.GetFullPath(), out fi))
                    {
                        results.Errors.Add(new FileError { FileName = file, Error = "Failed to create zip from file." });
                        continue;
                    }
                }

                string baseName = fi.Name.Replace(fi.Extension, string.Empty);
                if (!IsZipFile(fi) && addedNames.Contains(baseName))
                    AddEntryToExistingFile(directory, file, fi, baseName);

                addedNames.Add(baseName);

                string zipName = IsZipFile(fi) ? Path.Combine(directory, fi.Name) : Path.Combine(directory, baseName + ".zip");
                try
                {
                    string existingFile = Adapter.GetGameFileNames().FirstOrDefault(x => Path.GetFileName(x).Equals(fi.Name));
                    if (existingFile != null && GameFile.IsUnmanaged(existingFile))
                    {
                        results.Errors.Add(new FileError { FileName = baseName, Error = "File already exists as an unmanaged file." });
                    }
                    else if (File.Exists(zipName))
                    {
                        if (overwrite)
                        {
                            results.ReplacedFiles.Add(baseName + ".zip");
                            if (IsZipFile(fi))
                                fi.CopyTo(zipName, true);
                            else
                                HandleNonZipReplacement(fi, zipName);
                        }
                    }
                    else
                    {
                        results.NewFiles.Add(baseName + ".zip");
                        if (IsZipFile(fi))
                            fi.CopyTo(Path.Combine(directory, fi.Name));
                        else
                            AddZipEntry(file, fi.Name, Path.Combine(directory, baseName + ".zip"));
                    }
                }
                catch (IOException)
                {
                    results.Errors.Add(new FileError { FileName = baseName, Error = "File is in use." });
                }
                catch (Exception ex)
                {
                    results.Errors.Add(new FileError { FileName = baseName, Error = string.Concat("Unknown error: ", ex.HResult) });
                }

                count++;
            }

            return results;
        }

        public SyncResult AddAndSync(string[] files, AddFileType type, FileManagement fileManagement, bool overwrite, ITagData tag)
        {
            FileAddResults addResults = fileManagement == FileManagement.Unmanaged
                ? AddUnmanagedFiles(files)
                : AddManagedFiles(files, overwrite);

            var all = addResults.GetAllFiles().ToArray();
            if (all.Length == 0)
                return SyncResult.EMPTY;

            var sync = SyncFiles(all, fileManagement);
            if (type == AddFileType.IWad)
                SyncIWads(sync.AddedGameFiles);
            if (tag != null)
            {
                foreach (var file in sync.AddedOrUpdatedFiles)
                    DataCache.Instance.AddGameFileTag(new[] { file }, tag, out _);
                DataCache.Instance.TagMapLookup.Refresh(new[] { tag });
            }
            return sync;
        }

        public void SyncIWads(IEnumerable<IGameFile> gameFiles)
        {
            foreach (var gameFile in gameFiles)
            {
                var existing = Adapter.GetIWads().FirstOrDefault(x => string.Equals(x.FileName, gameFile.FileName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    continue;
                var iwad = new IWadData { FileName = gameFile.FileName, Name = gameFile.FileNameBase };
                Adapter.InsertIWad(iwad);
                var inserted = Adapter.GetIWads().FirstOrDefault(x => string.Equals(x.FileName, gameFile.FileName, StringComparison.OrdinalIgnoreCase));
                if (inserted != null)
                {
                    gameFile.IWadID = inserted.IWadID;
                    Adapter.UpdateGameFile(gameFile, new[] { GameFileFieldType.IWadID });
                }
            }
        }

        public LaunchResult Launch(PlayLaunchRequest request)
        {
            if (request.SelectedSourcePort == null)
                return LaunchResult.Failure("No source port selected.");

            if (request.Remember)
                SavePlaySettings(request);

            var features = new List<ILaunchFeature>();
            if (request.SelectedIWad != null)
                features.Add(new IWadLaunchFeature(request.SelectedIWad, true));

            features.AddRange(new List<ILaunchFeature>
            {
                new MapSkillLaunchFeature(request.SelectedMap, request.SelectedSkill),
                new GameFilesLaunchFeature(request.AdditionalFiles, request.SpecificFiles?.ToList(), true),
                new ExtraParametersLaunchFeature(request.ExtraParameters, request.ExtraParametersOnly),
                new SourcePortExtraParametersLaunchFeature()
            });

            if (request.Record)
                features.Add(new RecordLaunchFeature());
            if (request.PlayDemo && request.SelectedDemo != null)
                features.Add(new PlayDemoLaunchFeature(Path.Combine(Config.DemoDirectory.GetFullPath(), request.SelectedDemo.FileName)));
            if (request.SaveStatistics)
                features.Add(new StatisticsReaderLaunchFeature());
            if (request.LoadLatestSave)
                features.Add(new LoadSaveLaunchFeature(GetLoadLatestSave(request.GameFile, request.SelectedSourcePort)));

            var launcher = new GameLauncher(Config, features);
            if (ProcessExited != null)
                launcher.ProcessExited += ProcessExited;

            IStatisticsReader statisticsReader = null;
            if (request.SaveStatistics)
            {
                List<IStatsData> existingStats = new List<IStatsData>();
                if (request.GameFile?.GameFileID != null)
                    existingStats = Adapter.GetStats(request.GameFile.GameFileID.Value).ToList();
                statisticsReader = request.SelectedSourcePort.GetFlavor().CreateStatisticsReader(request.GameFile, existingStats);
                statisticsReader?.Start();
            }

            var result = launcher.Launch(request.GameFile, request.AdditionalFiles, request.SelectedSourcePort, IsIwad(request.GameFile));
            if (!result.Failed)
            {
                m_activeSessions.Add(new PlaySession(result.GameLaunchInfo, statisticsReader, DateTime.Now));
                if (request.GameFile != null)
                {
                    request.GameFile.LastPlayed = DateTime.Now;
                    Adapter.UpdateGameFile(request.GameFile, new[] { GameFileFieldType.LastPlayed });
                }
            }
            return result;
        }

        public string PreviewLaunchParameters(PlayLaunchRequest request)
        {
            var features = new List<ILaunchFeature>();
            if (request.SelectedIWad != null)
                features.Add(new IWadLaunchFeature(request.SelectedIWad, false));
            features.Add(new MapSkillLaunchFeature(request.SelectedMap, request.SelectedSkill));
            features.Add(new GameFilesLaunchFeature(request.AdditionalFiles, request.SpecificFiles?.ToList(), false));
            features.Add(new ExtraParametersLaunchFeature(request.ExtraParameters, request.ExtraParametersOnly));
            features.Add(new SourcePortExtraParametersLaunchFeature());
            var launcher = new GameLauncher(Config, features);
            var parameters = launcher.GetLaunchParameters(request.GameFile, request.AdditionalFiles, request.SelectedSourcePort, IsIwad(request.GameFile));
            if (parameters.Failed)
                return parameters.ErrorMessage;
            return SourcePort.SourcePortLaunch.FormatCommand(request.SelectedSourcePort, parameters.LaunchString);
        }

        public void DeleteGameFile(IGameFile gameFile)
        {
            if (gameFile == null)
                return;
            var fileHandler = new FileHandler(Adapter, Config);
            fileHandler.DeleteFiles(gameFile);
            Adapter.DeleteGameFile(gameFile);
            try
            {
                if (!gameFile.IsUnmanaged())
                {
                    string path = Path.Combine(Config.GameFileDirectory.GetFullPath(), gameFile.FileName);
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
            catch
            {
            }
        }

        public IEnumerable<IGameFile> Search(LibraryTab tab, string text, bool includeAll, IGameFileDataSourceAdapter idGames)
        {
            GameFileSearchField[] fields = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                var names = includeAll
                    ? new[] { "Title", "Author", "Filename", "Description", "Comments" }
                    : new[] { "Title", "Author", "Filename" };
                fields = Util.SearchFieldsFromText(text, names);
            }
            return LibraryTabService.LoadFiles(tab, Adapter, idGames, fields);
        }

        public bool HasActiveSessions => m_activeSessions.Count > 0;

        public void HandleProcessExited(GameLaunchInfo info)
        {
            var session = m_activeSessions.FirstOrDefault(x => x.GameLaunchInfo == info);
            if (session == null)
                return;
            m_activeSessions.Remove(session);
            if (session.StatisticsReader != null)
            {
                session.StatisticsReader.Stop();
            }
            if (session.GameLaunchInfo.GameFile != null)
            {
                var minutes = (int)Math.Max(0, (DateTime.Now - session.Start).TotalMinutes);
                session.GameLaunchInfo.GameFile.MinutesPlayed += minutes;
                Adapter.UpdateGameFile(session.GameLaunchInfo.GameFile, new[] { GameFileFieldType.MinutesPlayed });
            }
        }

        private void SavePlaySettings(PlayLaunchRequest request)
        {
            var gameFile = request.SelectedGameProfile as IGameFile ?? request.GameFile;
            if (gameFile == null)
                return;
            gameFile.SourcePortID = request.SelectedSourcePort?.SourcePortID;
            gameFile.IWadID = request.SelectedIWad?.IWadID;
            gameFile.SettingsMap = request.SelectedMap;
            gameFile.SettingsSkill = request.SelectedSkill;
            gameFile.SettingsExtraParams = request.ExtraParameters;
            gameFile.SettingsExtraParamsOnly = request.ExtraParametersOnly;
            gameFile.SettingsStat = request.SaveStatistics;
            gameFile.SettingsLoadLatestSave = request.LoadLatestSave;
            gameFile.SettingsSaved = true;
            if (request.AdditionalFiles != null)
                gameFile.SettingsFiles = string.Join(";", request.AdditionalFiles.Select(x => x.FileName));
            if (request.SpecificFiles != null)
                gameFile.SettingsSpecificFiles = string.Join(";", request.SpecificFiles);
            Adapter.UpdateGameFile(gameFile, new[]
            {
                GameFileFieldType.SourcePortID, GameFileFieldType.IWadID, GameFileFieldType.SettingsMap,
                GameFileFieldType.SettingsSkill, GameFileFieldType.SettingsExtraParams, GameFileFieldType.SettingsExtraParamsOnly,
                GameFileFieldType.SettingsStat, GameFileFieldType.SettingsLoadLatestSave, GameFileFieldType.SettingsSaved,
                GameFileFieldType.SettingsFiles, GameFileFieldType.SettingsSpecificFiles
            });
        }

        private string GetLoadLatestSave(IGameFile gameFile, ISourcePortData sourcePortData)
        {
            var saveFile = Adapter.GetFiles(gameFile, FileType.SaveGame).Where(x => x.SourcePortID == sourcePortData.SourcePortID)
                .OrderByDescending(x => x.DateCreated).FirstOrDefault();
            if (saveFile != null)
                return Path.Combine(sourcePortData.GetLoadSavePath().GetFullPath(), saveFile.OriginalFileName);
            return string.Empty;
        }

        private bool IsIwad(IGameFile gameFile)
        {
            if (gameFile?.GameFileID == null)
                return false;
            return Adapter.GetGameFileIWads().Any(x => x.GameFileID == gameFile.GameFileID);
        }

        private static bool IsZipFile(FileInfo fi) =>
            fi.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
            fi.Extension.Equals(".pk3", StringComparison.OrdinalIgnoreCase);

        private static void AddZipEntry(string file, string name, string newZipName)
        {
            using ZipArchive za = ZipFile.Open(newZipName, ZipArchiveMode.Create);
            using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var entry = za.CreateEntry(name);
            using var destStream = entry.Open();
            fileStream.CopyTo(destStream);
        }

        private static void HandleNonZipReplacement(FileInfo fi, string zipName)
        {
            using ZipArchive za = ZipFile.Open(zipName, ZipArchiveMode.Update);
            var existing = za.Entries.FirstOrDefault(x => x.Name.Equals(fi.Name, StringComparison.OrdinalIgnoreCase));
            existing?.Delete();
            za.CreateEntryFromFile(fi.FullName, fi.Name);
        }

        private static void AddEntryToExistingFile(string directory, string file, FileInfo fi, string baseName)
        {
            string zipName = Path.Combine(directory, baseName + ".zip");
            if (!File.Exists(zipName))
                return;
            using ZipArchive za = ZipFile.Open(zipName, ZipArchiveMode.Update);
            if (za.Entries.Any(x => x.Name.Equals(fi.Name, StringComparison.OrdinalIgnoreCase)))
                return;
            za.CreateEntryFromFile(file, fi.Name);
        }

        public string GetGameFilePath(IGameFile gameFile)
        {
            if (gameFile == null)
                return null;
            if (gameFile.IsUnmanaged())
                return gameFile.FileName;
            return Path.Combine(Config.GameFileDirectory.GetFullPath(), gameFile.FileName);
        }

        public List<string> ListTextFiles(IGameFile gameFile)
        {
            var names = new List<string>();
            string path = GetGameFilePath(gameFile);
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return names;
            using IArchiveReader reader = ArchiveReader.Create(path);
            foreach (var entry in reader.Entries.Where(x => x.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                names.Add(entry.Name);
            FileInfo fi = new FileInfo(gameFile.FileName);
            string baseFile = fi.Extension.Length > 0 ? fi.Name.Replace(fi.Extension, string.Empty) : fi.Name;
            var first = names.FirstOrDefault(x => x.StartsWith(baseFile, StringComparison.OrdinalIgnoreCase));
            if (first != null)
            {
                names.Remove(first);
                names.Insert(0, first);
            }
            return names;
        }

        public string ExtractTextFile(IGameFile gameFile, string entryName)
        {
            string path = GetGameFilePath(gameFile);
            using IArchiveReader reader = ArchiveReader.Create(path);
            var entry = reader.Entries.FirstOrDefault(x => x.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase))
                ?? reader.Entries.FirstOrDefault(x => x.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return null;
            string dest = Path.Combine(Config.TempDirectory.GetFullPath(), entry.Name);
            Directory.CreateDirectory(Config.TempDirectory.GetFullPath());
            entry.ExtractToFile(dest, true);
            return dest;
        }

        public List<string> ListArchiveEntries(IGameFile gameFile)
        {
            var names = new List<string>();
            string path = GetGameFilePath(gameFile);
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return names;
            using IArchiveReader reader = ArchiveReader.Create(path);
            foreach (var entry in reader.Entries.Where(x => !x.IsDirectory))
                names.Add(entry.FullName);
            return names;
        }

        public string CreateDesktopShortcut(IGameFile gameFile, bool autoPlay)
        {
            string fileName = string.IsNullOrEmpty(gameFile.Title) ? gameFile.FileName : gameFile.Title;
            foreach (char c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, ' ');
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop))
                desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            string dest = Path.Combine(desktop, fileName + ".desktop");
            string exe = Path.Combine(AppContext.BaseDirectory, "DoomLauncher");
            string args = autoPlay && gameFile.GameFileID.HasValue
                ? $"-LaunchGameFileID {gameFile.GameFileID} -AutoClose"
                : $"\"{GetGameFilePath(gameFile)}\"";
            File.WriteAllText(dest, $@"[Desktop Entry]
Type=Application
Name=Doom Launcher - {fileName}
Comment={gameFile.FileName}
Exec={exe} {args}
Icon={Path.Combine(AppContext.BaseDirectory, "DoomLauncher.ico")}
Terminal=false
Categories=Game;
");
            try { Process.Start("chmod", $"+x \"{dest}\""); } catch { }
            return dest;
        }

        public static bool CreateZipFromDirectory(string folderPath, string zipFileName)
        {
            try
            {
                if (File.Exists(zipFileName))
                    File.Delete(zipFileName);
                ZipFile.CreateFromDirectory(folderPath, zipFileName);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RenameGameFile(IGameFile gameFile, string newFileName)
        {
            if (gameFile == null || string.IsNullOrWhiteSpace(newFileName))
                return false;
            string oldPath = GetGameFilePath(gameFile);
            string newPath = gameFile.IsUnmanaged()
                ? Path.Combine(Path.GetDirectoryName(oldPath) ?? string.Empty, newFileName)
                : Path.Combine(Config.GameFileDirectory.GetFullPath(), newFileName);
            if (File.Exists(oldPath) && !gameFile.IsDirectory())
                File.Move(oldPath, newPath, false);
            else if (Directory.Exists(oldPath))
                Directory.Move(oldPath, newPath);
            gameFile.FileName = gameFile.IsUnmanaged() ? newPath : newFileName;
            Adapter.UpdateGameFile(gameFile, new[] { GameFileFieldType.Filename });
            return true;
        }

        public void Resync(IEnumerable<IGameFile> files, AddFileType type, bool pullTitlepic)
        {
            bool previous = Config.AutomaticallyPullTitlpic;
            try
            {
                if (!pullTitlepic)
                    SetAutomaticallyPullTitlepic(false);
                var managed = files.Where(x => !x.IsUnmanaged()).Select(x => Path.Combine(Config.GameFileDirectory.GetFullPath(), x.FileName)).ToArray();
                if (managed.Length > 0)
                    AddAndSync(managed, type, FileManagement.Managed, true, null);
                var unmanaged = files.Where(x => x.IsUnmanaged()).Select(x => x.FileName).ToArray();
                if (unmanaged.Length > 0)
                    AddAndSync(unmanaged, type, FileManagement.Unmanaged, true, null);
            }
            finally
            {
                if (!pullTitlepic)
                    SetAutomaticallyPullTitlepic(previous);
            }
        }

        private void SetAutomaticallyPullTitlepic(bool value)
        {
            var config = Adapter.GetConfiguration().FirstOrDefault(x => x.Name == AppConfiguration.AutomaticallyPullTitlpicName);
            if (config == null)
                return;
            config.Value = value.ToString();
            Adapter.UpdateConfiguration(config);
            Config.Refresh();
        }

        public IEnumerable<IGameFile> SearchIdGamesMetadata(IGameFile localFile, IdGamesDataAdapater adapter)
        {
            if (localFile == null || adapter == null)
                return Array.Empty<IGameFile>();
            var options = new GameFileGetOptions(new GameFileSearchField(GameFileFieldType.Filename, GameFileSearchOp.Like, localFile.FileNameNoPath));
            return adapter.GetGameFiles(options);
        }

        public string GetIdGamesWebUrl(IdGamesGameFile file)
        {
            if (file == null)
                return null;
            return string.Format("{0}?file={1}{2}", Config.IdGamesUrl, file.dir, file.FileName);
        }

        public void AddDoom64SourcePort(string doom64Exe)
        {
            if (string.IsNullOrEmpty(doom64Exe) || Adapter.GetDoom64().Any())
                return;
            var extensions = new List<string> { ".wad" };
            extensions.AddRange(Util.GetExtraDoom64Extensions());
            Adapter.InsertSourcePort(new SourcePortData
            {
                Executable = Path.GetFileName(doom64Exe),
                Name = "Doom 64",
                LaunchType = SourcePortLaunchType.Doom64,
                SupportedExtensions = string.Join(",", extensions),
                Directory = new LauncherPath(Path.GetDirectoryName(doom64Exe)),
                FileOption = "-file"
            });
        }

        public IEnumerable<IGameFile> GetSyncNeeded() => Adapter.GetGameFilesThatNeedSync();

        public string[] ExpandZdlFiles(string[] files)
        {
            var parser = new ZdlParser(Adapter.GetSourcePorts(), Adapter.GetIWads());
            var result = new List<string>();
            foreach (string file in files)
            {
                if (!file.EndsWith(".zdl", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(file);
                    continue;
                }
                try
                {
                    var parsed = parser.Parse(File.ReadAllText(file));
                    foreach (var gf in parsed)
                    {
                        if (!string.IsNullOrEmpty(gf.FileName) && File.Exists(gf.FileName))
                            result.Add(gf.FileName);
                    }
                }
                catch
                {
                    result.Add(file);
                }
            }
            return result.ToArray();
        }

        public IFileData ImportAssociationFile(IGameFile gameFile, FileType type, string path, ISourcePortData sourcePort)
        {
            var handler = new FileHandler(Adapter, Config);
            var file = handler.InsertAndCopy(gameFile, type, path);
            if (file != null && sourcePort != null)
            {
                file.SourcePortID = sourcePort.SourcePortID;
                Adapter.UpdateFile(file);
            }
            return file;
        }

        public void UpdateAssociationFile(IFileData file)
        {
            Adapter.UpdateFile(file);
        }

        public void DeleteAssociationFile(IFileData file)
        {
            new FileHandler(Adapter, Config).DeleteFile(file);
        }

        public IEnumerable<IFileData> GetAssociationFiles(IGameFile gameFile, FileType type)
        {
            return new FileHandler(Adapter, Config).GetFiles(gameFile, type);
        }

        public IFileData GetMainImage(IGameFile gameFile)
        {
            var handler = new FileHandler(Adapter, Config);
            var imageHandler = new GameFileImageHandler(handler, id => Adapter.GetIWad(id), Config.DeleteScreenshotsAfterImport);
            return imageHandler.GetMainImageLarge(gameFile);
        }
    }
}
