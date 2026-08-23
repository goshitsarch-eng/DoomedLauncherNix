using DoomLauncher.DataSources;
using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using WadReader;

namespace DoomLauncher
{
    public static class Util
    {
        public static bool ApproxEquals(this double a, double b, double tolerance = 0.01) => 
            Math.Abs(a - b) <= tolerance;

        public static bool ApproxEquals(this float a, float b, float tolerance = 0.01f) => 
            Math.Abs(a - b) <= tolerance;

        public static IEnumerable<object> TableToStructure(DataTable dt, Type type)
        {
            List<object> ret = new List<object>();
            object convertedObj;
            PropertyInfo[] properties = type.GetProperties().Where(x => x.GetSetMethod() != null && x.GetGetMethod() != null).ToArray();

            foreach (DataRow dr in dt.Rows)
            {
                object obj = Activator.CreateInstance(type);

                foreach (PropertyInfo pi in properties)
                {
                    Type pType = pi.PropertyType;

                    if (pType.IsGenericType && pType.GetGenericTypeDefinition() == typeof(Nullable<>))
                        pType = pType.GetGenericArguments()[0];

                    if (dt.Columns.Contains(pi.Name) && ChangeType(dr[pi.Name].ToString(), pType, out convertedObj))
                        pi.SetValue(obj, convertedObj, null);
                }

                ret.Add(obj);
            }

            return ret;
        }

        public static bool ChangeType(string obj, Type t, out object convertedObj)
        {
            convertedObj = null;
            if (obj == null) return false;

            if (obj.GetType() == typeof(string) && t == typeof(string))
            {
                convertedObj = obj;
                return true;
            }
            else if (obj.GetType() == typeof(string) && t == typeof(bool) &&
                (obj == "0" || obj == "1"))
            {
                if (obj == "0")
                    convertedObj = false;
                else
                    convertedObj = true;
                return true;
            }
            else if (obj.GetType() == typeof(string) && t == typeof(IWadInfo) && IWadInfo.IsGameName(obj))
            {
                convertedObj = IWadInfo.FromGameName(obj);
                return true;
            }
            else if (t.BaseType == typeof(Enum))
            {
                convertedObj = Convert.ToInt32(obj);
                return true;
            }

            MethodInfo method = t.GetMethod("TryParse", new[] { typeof(string), Type.GetType(string.Format("{0}&", t.FullName)) });

            if (method != null)
            {
                object[] args = new object[] { obj, convertedObj };

                if ((bool)method.Invoke(null, args))
                {
                    convertedObj = args[1];
                    return true;
                }
            }

            return false;
        }

        public static List<string> GetMapStringFromWad(string file)
        {
            List<string> maps = new List<string>();

            try
            {
                FileStream fs = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                WadFileReader wadReader = new WadFileReader(fs);

                if (wadReader.WadType != WadType.Unknown)
                {
                    var mapLumps = WadFileReader.GetMapMarkerLumps(wadReader.ReadLumps()).OrderBy(x => x.Name).ToArray();
                    fs.Close();

                    foreach (var mapLump in mapLumps)
                        maps.Add(mapLump.Name);
                }
                else
                {
                    fs.Close();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            return maps;
        }

        public static string GitHubRepository => $"https://github.com/{GitHubUser}/{GitHubRepositoryName}";

        public static string GitHubUser => "nstlaurent";

        public static string GitHubRepositoryName => "DoomLauncher";

        public static string DoomworldThread => "http://www.doomworld.com/vb/doom-general/69346-doom-launcher-doom-frontend-database/";

        public static string Realm667Thread => "http://realm667.com/index.php/en/kunena/doom-launcher";

        public static GameFileSearchField[] SearchFieldsFromText(string searchText, params string[] filters)
        {
            List<GameFileSearchField> ret = new List<GameFileSearchField>();
            if (string.IsNullOrWhiteSpace(searchText))
                return ret.ToArray();

            foreach (string item in filters)
            {
                if (Enum.TryParse(item, out GameFileFieldType type))
                    ret.Add(new GameFileSearchField(type, GameFileSearchOp.Like, searchText));
            }

            return ret.ToArray();
        }

        public static List<ISourcePortData> GetSourcePortsData(IDataSourceAdapter adapter)
        {
            List<ISourcePortData> sourcePorts = adapter.GetSourcePorts().ToList();
            SourcePortData noPort = new SourcePortData();
            noPort.Name = "N/A";
            noPort.SourcePortID = -1;
            sourcePorts.Insert(0, noPort);
            return sourcePorts;
        }

        public static string[] GetSkills()
        {
            return new string[] { "1", "2", "3", "4", "5" };
        }

        public static string GetTimePlayedString(int minutes)
        {
            List<string> items = new List<string>();

            TimeSpan ts = new TimeSpan(0, minutes, 0);

            if (ts.Days > 0)
                items.Add(TimeString(ts.Days, "Day"));
            if (ts.Hours > 0)
                items.Add(TimeString(ts.Hours, "Hour"));

            items.Add(TimeString(ts.Minutes, "Minute"));

            return string.Join(", ", items.ToArray());
        }

        private static string TimeString(int time, string type)
        {
            return string.Concat(time.ToString(), " ",  type, time == 1 ? string.Empty : "s");
        }

        public static List<IGameFile> GetAdditionalFiles(IDataSourceAdapter adapter, IGameProfile gameFile)
        {
            if (gameFile != null && !string.IsNullOrEmpty(gameFile.SettingsFiles))
                return GetAdditionalFiles(adapter, gameFile.SettingsFiles);

            return new List<IGameFile>();
        }

        public static List<IGameFile> GetIWadAdditionalFiles(IDataSourceAdapter adapter, IGameProfile gameFile)
        {
            if (gameFile != null && !string.IsNullOrEmpty(gameFile.SettingsFilesIWAD))
                return GetAdditionalFiles(adapter, gameFile.SettingsFilesIWAD);

            return new List<IGameFile>();
        }

        public static List<IGameFile> GetSourcePortAdditionalFiles(IDataSourceAdapter adapter, IGameProfile gameFile)
        {
            if (gameFile != null && !string.IsNullOrEmpty(gameFile.SettingsFilesSourcePort))
                return GetAdditionalFiles(adapter, gameFile.SettingsFilesSourcePort);

            return new List<IGameFile>();
        }

        public static List<IGameFile> GetAdditionalFiles(IDataSourceAdapter adapter, ISourcePortData sourcePort)
        {
            return GetAdditionalFiles(adapter, sourcePort.SettingsFiles);
        }

        private static List<IGameFile> GetAdditionalFiles(IDataSourceAdapter adapter, string property)
        {
            string[] fileNames = Util.SplitString(property);
            List<IGameFile> gameFiles = new List<IGameFile>();
            Array.ForEach(fileNames, x => gameFiles.Add(adapter.GetGameFile(x)));
            return gameFiles.Where(x => x != null).ToList();
        }

        [Conditional("DEBUG")]
        public static void ThrowDebugException(string msg)
        {
            throw new Exception(msg);
        }

        public static IEnumerable<IArchiveEntry> GetEntriesByExtension(IArchiveReader reader, string[] extensions)
        {
            List<IArchiveEntry> entries = new List<IArchiveEntry>();

            foreach (var ext in extensions)
            {
                 entries.AddRange(reader.Entries
                     .Where(x => x.Name.Contains('.') && Path.GetExtension(x.Name).Equals(ext, StringComparison.OrdinalIgnoreCase)));
            }

            return entries;
        }

        public static string[] GetPkExtenstions()
        {
            return new string[] { ".pk3", ".ipk3", ".pk7", ".zip" };
        }

        public static string[] GetReadablePkExtensions()
        {
            return new string[] { ".pk3", ".ipk3", ".pke", ".zip" };
        }

        public static string[] GetDehackedExtensions()
        {
            return new string[] { ".deh", ".bex" };
        }

        public static string[] GetSourcePortPkExtensions()
        {
            return new string[] { ".pk3", ".ipk3", ".pk7", ".pke"};
        }

        public static string[] GetExtraDoom64Extensions()
        {
            return new string[] { ".kpf" };
        }

        public static GameFileFieldType[] DefaultGameFileUpdateFields
        {
            get
            {
                return new GameFileFieldType[]
                {
                    GameFileFieldType.Author,
                    GameFileFieldType.Title,
                    GameFileFieldType.Description,
                    GameFileFieldType.Downloaded,
                    GameFileFieldType.LastPlayed,
                    GameFileFieldType.ReleaseDate,
                    GameFileFieldType.Comments,
                    GameFileFieldType.Rating,
                    GameFileFieldType.Map,
                    GameFileFieldType.MapCount,
                    GameFileFieldType.IWadID,
                    GameFileFieldType.IntendedGame,
                    GameFileFieldType.IsSyncNeeded
                };
            }
        }

        public static string ExtractTempFile(string tempDirectory, IArchiveEntry entry)
        {
            if (!entry.ExtractRequired)
                return entry.FullName;

            string safeName = ArchivePath.SafeFileName(entry.Name);
            string ext = Path.GetExtension(safeName);
            string file = safeName.Replace(ext, string.Empty) + "_";
            string[] searchFiles = Directory.GetFiles(tempDirectory, file + "*");

            string matchingFile = searchFiles.FirstOrDefault(x => new FileInfo(x).Length == entry.Length);

            if (matchingFile == null)
            {
                string extractFile = Path.Combine(tempDirectory, string.Concat(file, Guid.NewGuid().ToString(), ext));
                entry.ExtractToFile(extractFile);
                return extractFile;
            }

            return matchingFile;
        }

        public static List<IIWadData> GetIWadsDataSource(IDataSourceAdapter adapter)
        {
            List<IIWadData> iwads = adapter.GetIWads().ToList();
            iwads.ForEach(x => x.FileName = Path.GetFileNameWithoutExtension(x.FileName));
            return iwads;
        }

        public static string CleanDescription(string description)
        {
            string[] items = description.Split(new char[] { '\n' });
            StringBuilder sb = new StringBuilder();

            foreach (string item in items)
            {
                string text = Regex.Replace(item, @"\s+", " ");
                if (text.StartsWith(" "))
                    text = text.Substring(1);
                sb.Append(text);
                sb.Append(Environment.NewLine);
            }

            return sb.ToString();
        }

        public static long ReadAfter(MemoryStream ms, byte[] magicID)
        {
            long position = ms.Position;
            byte[] check = new byte[magicID.Length];

            while (ms.Position + magicID.Length < ms.Length)
            {
                ms.Read(check, 0, check.Length);

                if (magicID.SequenceEqual(check))
                    return ms.Position;

                ms.Position = ++position;
            }

            return -1;
        }

        public static int GetPreviewScreenshotWidth(int value)
        {
            if (value > 0)
                return 200 + (40 * value);
            else
                return 200 + (10 * value);
        }

        public static string[] SplitString(string value)
        {
            if (!string.IsNullOrEmpty(value))
                return value.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            else
                return new string[] { };
        }

        public static string GetExecutableNoPath() => AppDomain.CurrentDomain.FriendlyName;

        public static bool IsDirectory(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return false;

            FileAttributes attr = File.GetAttributes(path);
            return (attr & FileAttributes.Directory) == FileAttributes.Directory;
        }

        public static string ReadString(this IArchiveEntry entry, Encoding encoding) =>
            encoding.GetString(ReadEntry(entry));

        public static byte[] ReadEntry(this IArchiveEntry entry)
        {
            byte[] data = new byte[entry.Length];
            entry.Read(data, 0, data.Length);
            return data;
        }

        public static string GetDescription(this Enum value)
        {
            FieldInfo fi = value.GetType().GetField(value.ToString());
            if (fi.GetCustomAttributes(typeof(DescriptionAttribute), false) is DescriptionAttribute[] attributes &&
                attributes.Any())
                return attributes.First().Description;

            return value.ToString();
        }

        public static string GetInteropDirectory()
        {
            if (Environment.Is64BitOperatingSystem)
                return "x64";

            return "x86";
        }

        public static GameFileFieldType[] DefaultGameFileSelectFields => new[]
        {
            GameFileFieldType.GameFileID,
            GameFileFieldType.Filename,
            GameFileFieldType.Title,
            GameFileFieldType.Author,
            GameFileFieldType.ReleaseDate,
            GameFileFieldType.MapCount,
            GameFileFieldType.Rating,
            GameFileFieldType.LastPlayed,
            GameFileFieldType.Downloaded,
            GameFileFieldType.IWadID,
            GameFileFieldType.Comments,
            GameFileFieldType.Map,
            GameFileFieldType.MinutesPlayed,
            GameFileFieldType.SourcePortID,
            GameFileFieldType.IntendedGame,
            GameFileFieldType.IsSyncNeeded
        };
    }
}
