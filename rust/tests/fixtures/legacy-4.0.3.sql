-- Independent schema snapshot from DoomedLauncherNix 4.0.3.
-- Git commit SHA: f48510d8b9eea4051e09d42e6393517485b5bd06
-- Source: src/database.cpp, Database::ensureSchema
-- Source Git blob SHA: 95a98b04fe85c877ceb4515474dfe0007e68e8c3
-- Adjacent C++ string literals were decoded and concatenated without changing SQL.
-- Do not replace this fixture with the current Rust schema during migrations.

CREATE TABLE IF NOT EXISTS GameFiles (GameFileID INTEGER NOT NULL, FileName VARCHAR NOT NULL, Title VARCHAR, Author VARCHAR,ReleaseDate TEXT, Description VARCHAR, Map VARCHAR, SourcePortID INTEGER, Thumbnail VARCHAR,Comments VARCHAR, Rating INTEGER, GameID INTEGER, IWadID INTEGER, LastPlayed TEXT, Downloaded TEXT,SettingsMap TEXT, SettingsSkill TEXT, SettingsExtraParams TEXT, SettingsFiles TEXT, MapCount int,SettingsSpecificFiles TEXT, MinutesPlayed int, SettingsStat INTEGER, SettingsFilesSourcePort TEXT,SettingsFilesIWAD TEXT, SettingsGameProfileID INTEGER, SettingsSaved INTEGER,SettingsLoadLatestSave INTEGER, SettingsExtraParamsOnly INTEGER, PRIMARY KEY(GameFileID));

CREATE TABLE IF NOT EXISTS Files (FileID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, GameFileID INTEGER NOT NULL,FileName TEXT NOT NULL, DateCreated TEXT NOT NULL, FileTypeID INTEGER NOT NULL,SourcePortID INTEGER NOT NULL, Description TEXT, OriginalFileName TEXT, OriginalFilePath TEXT,FileOrder int, UserTitle TEXT, UserDescription TEXT, Map TEXT);

CREATE TABLE IF NOT EXISTS SourcePorts (SourcePortID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL,Executable TEXT NOT NULL, SupportedExtensions TEXT NOT NULL, Directory TEXT NOT NULL,SettingsFiles TEXT, LaunchType TEXT, FileOption TEXT, ExtraParameters TEXT,AltSaveDirectory TEXT, Archived INTEGER);

CREATE TABLE IF NOT EXISTS IWads (IWadID INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, FileName TEXT, GameFileID int);

CREATE TABLE IF NOT EXISTS Configuration (ConfigID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Value TEXT NOT NULL,AvailableValues TEXT NOT NULL, UserCanModify INTEGER);

CREATE TABLE IF NOT EXISTS Tags (TagID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, HasTab INTEGER NOT NULL,HasColor int, Color int, ExcludeFromOtherTabs INTEGER, Favorite INTEGER);

CREATE TABLE IF NOT EXISTS TagMapping (FileID INTEGER NOT NULL, TagID INTEGER NOT NULL, PRIMARY KEY(FileID, TagID));

CREATE TABLE IF NOT EXISTS Stats (StatID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, GameFileID INTEGER NOT NULL,KillCount INTEGER NOT NULL, TotalKills INTEGER NOT NULL, SecretCount INTEGER NOT NULL,TotalSecrets INTEGER NOT NULL, LevelTime REAL NOT NULL, ItemCount INTEGER NOT NULL,TotalItems INTEGER NOT NULL, SourcePortID INTEGER NOT NULL, MapName TEXT NOT NULL,RecordTime TEXT NOT NULL, Skill INTEGER);

CREATE TABLE IF NOT EXISTS GameProfiles (GameProfileID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, GameFileID INTEGER NOT NULL,SourcePortID INTEGER NOT NULL, IWadID INTEGER NOT NULL, Name TEXT NOT NULL, SettingsMap TEXT,SettingsSkill TEXT, SettingsExtraParams TEXT, SettingsFiles TEXT, SettingsFilesSourcePort TEXT,SettingsFilesIWAD TEXT, SettingsSpecificFiles TEXT, SettingsStat INTEGER, SettingsSaved INTEGER,SettingsLoadLatestSave INTEGER, SettingsExtraParamsOnly INTEGER);

CREATE TABLE IF NOT EXISTS CleanupFiles (CleanupFileID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, FileName TEXT NOT NULL);
