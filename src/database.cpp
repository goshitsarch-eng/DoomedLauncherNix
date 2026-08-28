#include "database.h"
#include "launcherpaths.h"

#include <QDateTime>
#include <QFile>
#include <QSqlError>
#include <QSqlQuery>
#include <QSqlRecord>

Database::Database(QObject *parent)
    : QObject(parent)
{
}

bool Database::open(QString *error)
{
    LauncherPaths::ensureLayout();

    m_db = QSqlDatabase::addDatabase(QStringLiteral("QSQLITE"));
    m_db.setDatabaseName(LauncherPaths::databaseFile());
    if (!m_db.open()) {
        if (error)
            *error = m_db.lastError().text();
        return false;
    }

    ensureSchema();
    seedConfiguration();
    return true;
}

void Database::ensureSchema()
{
    // Matches the schema written by earlier releases; every statement is a
    // no-op on a database that already has the table/column.
    const QStringList statements = {
        QStringLiteral("CREATE TABLE IF NOT EXISTS GameFiles ("
                       "GameFileID INTEGER NOT NULL, FileName VARCHAR NOT NULL, Title VARCHAR, Author VARCHAR,"
                       "ReleaseDate TEXT, Description VARCHAR, Map VARCHAR, SourcePortID INTEGER, Thumbnail VARCHAR,"
                       "Comments VARCHAR, Rating INTEGER, GameID INTEGER, IWadID INTEGER, LastPlayed TEXT, Downloaded TEXT,"
                       "SettingsMap TEXT, SettingsSkill TEXT, SettingsExtraParams TEXT, SettingsFiles TEXT, MapCount int,"
                       "SettingsSpecificFiles TEXT, MinutesPlayed int, SettingsStat INTEGER, SettingsFilesSourcePort TEXT,"
                       "SettingsFilesIWAD TEXT, SettingsGameProfileID INTEGER, SettingsSaved INTEGER,"
                       "SettingsLoadLatestSave INTEGER, SettingsExtraParamsOnly INTEGER, PRIMARY KEY(GameFileID))"),
        QStringLiteral("CREATE TABLE IF NOT EXISTS Files ("
                       "FileID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, GameFileID INTEGER NOT NULL,"
                       "FileName TEXT NOT NULL, DateCreated TEXT NOT NULL, FileTypeID INTEGER NOT NULL,"
                       "SourcePortID INTEGER NOT NULL, Description TEXT, OriginalFileName TEXT, OriginalFilePath TEXT,"
                       "FileOrder int, UserTitle TEXT, UserDescription TEXT, Map TEXT)"),
        QStringLiteral("CREATE TABLE IF NOT EXISTS SourcePorts ("
                       "SourcePortID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL,"
                       "Executable TEXT NOT NULL, SupportedExtensions TEXT NOT NULL, Directory TEXT NOT NULL,"
                       "SettingsFiles TEXT, LaunchType TEXT, FileOption TEXT, ExtraParameters TEXT,"
                       "AltSaveDirectory TEXT, Archived INTEGER)"),
        QStringLiteral("CREATE TABLE IF NOT EXISTS IWads ("
                       "IWadID INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, FileName TEXT, GameFileID int)"),
        QStringLiteral("CREATE TABLE IF NOT EXISTS Configuration ("
                       "ConfigID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Value TEXT NOT NULL,"
                       "AvailableValues TEXT NOT NULL, UserCanModify INTEGER)"),
        QStringLiteral("CREATE TABLE IF NOT EXISTS Tags ("
                       "TagID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, HasTab INTEGER NOT NULL,"
                       "HasColor int, Color int, ExcludeFromOtherTabs INTEGER, Favorite INTEGER)"),
        QStringLiteral("CREATE TABLE IF NOT EXISTS TagMapping ("
                       "FileID INTEGER NOT NULL, TagID INTEGER NOT NULL, PRIMARY KEY(FileID, TagID))"),
        QStringLiteral("CREATE TABLE IF NOT EXISTS Stats ("
                       "StatID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, GameFileID INTEGER NOT NULL,"
                       "KillCount INTEGER NOT NULL, TotalKills INTEGER NOT NULL, SecretCount INTEGER NOT NULL,"
                       "TotalSecrets INTEGER NOT NULL, LevelTime REAL NOT NULL, ItemCount INTEGER NOT NULL,"
                       "TotalItems INTEGER NOT NULL, SourcePortID INTEGER NOT NULL, MapName TEXT NOT NULL,"
                       "RecordTime TEXT NOT NULL, Skill INTEGER)"),
        QStringLiteral("CREATE TABLE IF NOT EXISTS GameProfiles ("
                       "GameProfileID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, GameFileID INTEGER NOT NULL,"
                       "SourcePortID INTEGER NOT NULL, IWadID INTEGER NOT NULL, Name TEXT NOT NULL, SettingsMap TEXT,"
                       "SettingsSkill TEXT, SettingsExtraParams TEXT, SettingsFiles TEXT, SettingsFilesSourcePort TEXT,"
                       "SettingsFilesIWAD TEXT, SettingsSpecificFiles TEXT, SettingsStat INTEGER, SettingsSaved INTEGER,"
                       "SettingsLoadLatestSave INTEGER, SettingsExtraParamsOnly INTEGER)"),
        QStringLiteral("CREATE TABLE IF NOT EXISTS CleanupFiles ("
                       "CleanupFileID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, FileName TEXT NOT NULL)"),
    };

    for (const QString &statement : statements) {
        QSqlQuery query(m_db);
        query.exec(statement);
    }
}

void Database::seedConfiguration()
{
    struct Entry {
        const char *name;
        const char *value;
        const char *available;
        int userCanModify;
    };
    // Only inserted when the row is missing; existing values always win.
    const Entry defaults[] = {
        {"IdGamesUrl", "https://www.doomworld.com/idgames/", "", 1},
        {"ApiPage", "api/api.php", "", 1},
        {"MirrorUrl", "https://www.quaddicted.com/files/idgames/",
         "Germany;https://www.quaddicted.com/files/idgames/;Idaho;https://mirrors.syringanetworks.net/idgames/;"
         "New York;http://youfailit.net/pub/idgames/;Florida;https://www.gamers.org/pub/idgames/;", 1},
        {"DefaultSourcePort", "0", "", 0},
        {"DefaultIWad", "0", "", 0},
        {"ShowPlayDialog", "true", "", 1},
        {"ColorThemeType", "System", "System;Default;Dark;", 1},
        {"GameFileViewType", "GridView", "GridView;TileView;", 1},
        {"VisibleViews", "Recent;Untagged;IWads;Id Games", "", 1},
        {"CleanTemp", "true", "", 0},
        {"LastSelectedTabIndex", "0", "", 0},
        {"AppWidth", "1100", "", 0},
        {"AppHeight", "720", "", 0},
    };

    for (const Entry &entry : defaults) {
        QSqlQuery check(m_db);
        check.prepare(QStringLiteral("select count(*) from Configuration where Name = ?"));
        check.addBindValue(QString::fromLatin1(entry.name));
        if (!check.exec() || !check.next() || check.value(0).toInt() > 0)
            continue;
        QSqlQuery insert(m_db);
        insert.prepare(QStringLiteral(
            "insert into Configuration (Name, Value, AvailableValues, UserCanModify) values (?, ?, ?, ?)"));
        insert.addBindValue(QString::fromLatin1(entry.name));
        insert.addBindValue(QString::fromLatin1(entry.value));
        insert.addBindValue(QString::fromLatin1(entry.available));
        insert.addBindValue(entry.userCanModify);
        insert.exec();
    }
}

QVariantMap Database::rowToMap(const QSqlQuery &query)
{
    QVariantMap map;
    const QSqlRecord record = query.record();
    for (int i = 0; i < record.count(); ++i)
        map.insert(record.fieldName(i), record.value(i));
    return map;
}

// Configuration ------------------------------------------------------------

QString Database::configValue(const QString &name, const QString &defaultValue) const
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("select Value from Configuration where Name = ?"));
    query.addBindValue(name);
    if (query.exec() && query.next())
        return query.value(0).toString();
    return defaultValue;
}

void Database::setConfigValue(const QString &name, const QString &value)
{
    QSqlQuery update(m_db);
    update.prepare(QStringLiteral("update Configuration set Value = ? where Name = ?"));
    update.addBindValue(value);
    update.addBindValue(name);
    update.exec();
    if (update.numRowsAffected() == 0) {
        QSqlQuery insert(m_db);
        insert.prepare(QStringLiteral(
            "insert into Configuration (Name, Value, AvailableValues, UserCanModify) values (?, ?, '', 1)"));
        insert.addBindValue(name);
        insert.addBindValue(value);
        insert.exec();
    }
}

bool Database::configBool(const QString &name, bool defaultValue) const
{
    const QString value = configValue(name);
    if (value.isEmpty())
        return defaultValue;
    return value.compare(QStringLiteral("true"), Qt::CaseInsensitive) == 0 || value == QStringLiteral("1");
}

int Database::configInt(const QString &name, int defaultValue) const
{
    bool ok = false;
    const int value = configValue(name).toInt(&ok);
    return ok ? value : defaultValue;
}

QVariantList Database::userConfigEntries() const
{
    QVariantList entries;
    QSqlQuery query(m_db);
    query.exec(QStringLiteral("select * from Configuration where UserCanModify = 1 order by Name"));
    while (query.next())
        entries.append(rowToMap(query));
    return entries;
}

// Game files ---------------------------------------------------------------

QVariantList Database::gameFiles(const QString &where, const QVariantList &binds) const
{
    QVariantList result;
    QString sql = QStringLiteral("select * from GameFiles");
    if (!where.isEmpty())
        sql += QStringLiteral(" where ") + where;
    QSqlQuery query(m_db);
    query.prepare(sql);
    for (const QVariant &bind : binds)
        query.addBindValue(bind);
    if (!query.exec())
        return result;
    while (query.next())
        result.append(rowToMap(query));
    return result;
}

QVariantMap Database::gameFileById(int gameFileId) const
{
    const QVariantList rows = gameFiles(QStringLiteral("GameFileID = ?"), {gameFileId});
    return rows.isEmpty() ? QVariantMap() : rows.first().toMap();
}

QVariantMap Database::gameFileByName(const QString &fileName) const
{
    const QVariantList rows =
        gameFiles(QStringLiteral("FileName = ? COLLATE NOCASE"), {fileName});
    return rows.isEmpty() ? QVariantMap() : rows.first().toMap();
}

int Database::insertGameFile(const QVariantMap &fields)
{
    QStringList names;
    QStringList placeholders;
    QVariantList values;
    for (auto it = fields.constBegin(); it != fields.constEnd(); ++it) {
        names << it.key();
        placeholders << QStringLiteral("?");
        values << it.value();
    }
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("insert into GameFiles (%1) values (%2)")
                      .arg(names.join(QStringLiteral(", ")), placeholders.join(QStringLiteral(", "))));
    for (const QVariant &value : values)
        query.addBindValue(value);
    if (!query.exec())
        return -1;
    return query.lastInsertId().toInt();
}

void Database::updateGameFile(int gameFileId, const QVariantMap &fields)
{
    if (fields.isEmpty())
        return;
    QStringList sets;
    QVariantList values;
    for (auto it = fields.constBegin(); it != fields.constEnd(); ++it) {
        sets << it.key() + QStringLiteral(" = ?");
        values << it.value();
    }
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("update GameFiles set %1 where GameFileID = ?")
                      .arg(sets.join(QStringLiteral(", "))));
    for (const QVariant &value : values)
        query.addBindValue(value);
    query.addBindValue(gameFileId);
    query.exec();
}

void Database::deleteGameFile(int gameFileId)
{
    for (const QString &sql : {QStringLiteral("delete from GameFiles where GameFileID = ?"),
                               QStringLiteral("delete from Files where GameFileID = ?"),
                               QStringLiteral("delete from TagMapping where FileID = ?"),
                               QStringLiteral("delete from Stats where GameFileID = ?"),
                               QStringLiteral("delete from IWads where GameFileID = ?")}) {
        QSqlQuery query(m_db);
        query.prepare(sql);
        query.addBindValue(gameFileId);
        query.exec();
    }
}

QList<int> Database::iwadGameFileIds() const
{
    QList<int> ids;
    QSqlQuery query(m_db);
    query.exec(QStringLiteral("select GameFileID from IWads where GameFileID is not null"));
    while (query.next())
        ids.append(query.value(0).toInt());
    return ids;
}

QList<int> Database::untaggedGameFileIds() const
{
    QList<int> ids;
    QSqlQuery query(m_db);
    query.exec(QStringLiteral(
        "select GameFiles.GameFileID from GameFiles left join TagMapping on "
        "GameFiles.GameFileID = TagMapping.FileID where TagMapping.FileID is null"));
    while (query.next())
        ids.append(query.value(0).toInt());
    return ids;
}

// Source ports --------------------------------------------------------------

QVariantList Database::sourcePorts(int launchType) const
{
    QVariantList ports;
    QSqlQuery query(m_db);
    if (launchType >= 0) {
        query.prepare(QStringLiteral(
            "select * from SourcePorts where LaunchType = ? and (Archived is null or Archived = 0) "
            "order by Name collate nocase"));
        query.addBindValue(launchType);
    } else {
        query.prepare(QStringLiteral(
            "select * from SourcePorts where (Archived is null or Archived = 0) order by Name collate nocase"));
    }
    if (!query.exec())
        return ports;
    while (query.next())
        ports.append(rowToMap(query));
    return ports;
}

QVariantMap Database::sourcePortById(int id) const
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("select * from SourcePorts where SourcePortID = ?"));
    query.addBindValue(id);
    if (query.exec() && query.next())
        return rowToMap(query);
    return {};
}

int Database::insertSourcePort(const QVariantMap &fields)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral(
        "insert into SourcePorts (Name, Executable, SupportedExtensions, Directory, SettingsFiles, LaunchType, "
        "FileOption, ExtraParameters, AltSaveDirectory, Archived) values (?, ?, ?, ?, ?, ?, ?, ?, ?, 0)"));
    query.addBindValue(fields.value(QStringLiteral("Name")));
    query.addBindValue(fields.value(QStringLiteral("Executable")));
    query.addBindValue(fields.value(QStringLiteral("SupportedExtensions"),
                                    QStringLiteral(".wad,.deh,.bex,.pk3,.pk7,.ipk3,.zip")));
    query.addBindValue(fields.value(QStringLiteral("Directory"), QString()));
    query.addBindValue(fields.value(QStringLiteral("SettingsFiles"), QString()));
    query.addBindValue(fields.value(QStringLiteral("LaunchType"), 0));
    query.addBindValue(fields.value(QStringLiteral("FileOption"), QStringLiteral("-file")));
    query.addBindValue(fields.value(QStringLiteral("ExtraParameters"), QString()));
    query.addBindValue(fields.value(QStringLiteral("AltSaveDirectory"), QString()));
    if (!query.exec())
        return -1;
    return query.lastInsertId().toInt();
}

void Database::updateSourcePort(int id, const QVariantMap &fields)
{
    QStringList sets;
    QVariantList values;
    for (auto it = fields.constBegin(); it != fields.constEnd(); ++it) {
        sets << it.key() + QStringLiteral(" = ?");
        values << it.value();
    }
    if (sets.isEmpty())
        return;
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("update SourcePorts set %1 where SourcePortID = ?")
                      .arg(sets.join(QStringLiteral(", "))));
    for (const QVariant &value : values)
        query.addBindValue(value);
    query.addBindValue(id);
    query.exec();
}

void Database::deleteSourcePort(int id)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("delete from SourcePorts where SourcePortID = ?"));
    query.addBindValue(id);
    query.exec();
}

// IWads ----------------------------------------------------------------------

QVariantList Database::iwads() const
{
    QVariantList result;
    QSqlQuery query(m_db);
    query.exec(QStringLiteral("select * from IWads order by Name collate nocase"));
    while (query.next())
        result.append(rowToMap(query));
    return result;
}

int Database::insertIWad(const QString &name, const QString &fileName, int gameFileId)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("insert into IWads (Name, FileName, GameFileID) values (?, ?, ?)"));
    query.addBindValue(name);
    query.addBindValue(fileName);
    query.addBindValue(gameFileId);
    if (!query.exec())
        return -1;
    return query.lastInsertId().toInt();
}

void Database::deleteIWad(int iwadId)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("delete from IWads where IWadID = ?"));
    query.addBindValue(iwadId);
    query.exec();
}

QVariantMap Database::iwadById(int iwadId) const
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("select * from IWads where IWadID = ?"));
    query.addBindValue(iwadId);
    if (query.exec() && query.next())
        return rowToMap(query);
    return {};
}

// Tags ------------------------------------------------------------------------

QVariantList Database::tags() const
{
    QVariantList result;
    QSqlQuery query(m_db);
    query.exec(QStringLiteral("select * from Tags order by Name collate nocase"));
    while (query.next())
        result.append(rowToMap(query));
    return result;
}

int Database::insertTag(const QString &name, bool hasTab)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("insert into Tags (Name, HasTab) values (?, ?)"));
    query.addBindValue(name);
    query.addBindValue(hasTab ? 1 : 0);
    if (!query.exec())
        return -1;
    return query.lastInsertId().toInt();
}

void Database::updateTag(int tagId, const QString &name, bool hasTab)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("update Tags set Name = ?, HasTab = ? where TagID = ?"));
    query.addBindValue(name);
    query.addBindValue(hasTab ? 1 : 0);
    query.addBindValue(tagId);
    query.exec();
}

void Database::deleteTag(int tagId)
{
    for (const QString &sql : {QStringLiteral("delete from Tags where TagID = ?"),
                               QStringLiteral("delete from TagMapping where TagID = ?")}) {
        QSqlQuery query(m_db);
        query.prepare(sql);
        query.addBindValue(tagId);
        query.exec();
    }
}

void Database::addTagMapping(int gameFileId, int tagId)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("insert or ignore into TagMapping (FileID, TagID) values (?, ?)"));
    query.addBindValue(gameFileId);
    query.addBindValue(tagId);
    query.exec();
}

void Database::removeTagMapping(int gameFileId, int tagId)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("delete from TagMapping where FileID = ? and TagID = ?"));
    query.addBindValue(gameFileId);
    query.addBindValue(tagId);
    query.exec();
}

QList<int> Database::tagsForGameFile(int gameFileId) const
{
    QList<int> ids;
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("select TagID from TagMapping where FileID = ?"));
    query.addBindValue(gameFileId);
    if (query.exec()) {
        while (query.next())
            ids.append(query.value(0).toInt());
    }
    return ids;
}

QList<int> Database::gameFileIdsForTag(int tagId) const
{
    QList<int> ids;
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("select FileID from TagMapping where TagID = ?"));
    query.addBindValue(tagId);
    if (query.exec()) {
        while (query.next())
            ids.append(query.value(0).toInt());
    }
    return ids;
}

// Statistics ---------------------------------------------------------------------

QVariantList Database::stats(int gameFileId) const
{
    QVariantList result;
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("select * from Stats where GameFileID = ? order by StatID"));
    query.addBindValue(gameFileId);
    if (!query.exec())
        return result;
    while (query.next())
        result.append(rowToMap(query));
    return result;
}

void Database::insertStats(const QVariantMap &fields)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral(
        "insert into Stats (GameFileID, KillCount, TotalKills, SecretCount, TotalSecrets, LevelTime, "
        "ItemCount, TotalItems, SourcePortID, MapName, RecordTime, Skill) "
        "values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)"));
    query.addBindValue(fields.value(QStringLiteral("GameFileID")));
    query.addBindValue(fields.value(QStringLiteral("KillCount"), 0));
    query.addBindValue(fields.value(QStringLiteral("TotalKills"), 0));
    query.addBindValue(fields.value(QStringLiteral("SecretCount"), 0));
    query.addBindValue(fields.value(QStringLiteral("TotalSecrets"), 0));
    query.addBindValue(fields.value(QStringLiteral("LevelTime"), 0.0));
    query.addBindValue(fields.value(QStringLiteral("ItemCount"), 0));
    query.addBindValue(fields.value(QStringLiteral("TotalItems"), 0));
    query.addBindValue(fields.value(QStringLiteral("SourcePortID"), -1));
    query.addBindValue(fields.value(QStringLiteral("MapName")));
    query.addBindValue(QDateTime::currentDateTime().toString(QStringLiteral("yyyy-MM-dd HH:mm:ss")));
    query.addBindValue(fields.value(QStringLiteral("Skill")));
    query.exec();
}

// Association files ------------------------------------------------------------

QVariantList Database::files(int gameFileId, int fileTypeId) const
{
    QVariantList result;
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral(
        "select * from Files where GameFileID = ? and FileTypeID = ? order by FileOrder, FileID"));
    query.addBindValue(gameFileId);
    query.addBindValue(fileTypeId);
    if (!query.exec())
        return result;
    while (query.next())
        result.append(rowToMap(query));
    return result;
}

int Database::insertFile(int gameFileId, const QString &fileName, int fileTypeId,
                         int sourcePortId, const QString &description)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral(
        "insert into Files (GameFileID, FileName, DateCreated, FileTypeID, SourcePortID, Description, FileOrder) "
        "values (?, ?, ?, ?, ?, ?, 2)"));
    query.addBindValue(gameFileId);
    query.addBindValue(fileName);
    query.addBindValue(QDateTime::currentDateTime().toString(QStringLiteral("yyyy-MM-dd HH:mm:ss")));
    query.addBindValue(fileTypeId);
    query.addBindValue(sourcePortId);
    query.addBindValue(description);
    if (!query.exec())
        return -1;
    return query.lastInsertId().toInt();
}

void Database::deleteFile(int fileId)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("delete from Files where FileID = ?"));
    query.addBindValue(fileId);
    query.exec();
}

void Database::deleteFilesForGameFile(int gameFileId)
{
    QSqlQuery query(m_db);
    query.prepare(QStringLiteral("delete from Files where GameFileID = ?"));
    query.addBindValue(gameFileId);
    query.exec();
}
