#pragma once

#include <QObject>
#include <QSqlDatabase>
#include <QVariantList>
#include <QVariantMap>

// Thin wrapper around the launcher's SQLite database. The schema is the
// one the previous releases used (GameFiles, Files, SourcePorts, IWads,
// Tags, TagMapping, Stats, GameProfiles, Configuration), so an existing
// DoomLauncher.sqlite opens unchanged.
class Database : public QObject
{
    Q_OBJECT

public:
    explicit Database(QObject *parent = nullptr);

    bool open(QString *error = nullptr);
    QSqlDatabase &db() { return m_db; }

    // Configuration table -------------------------------------------------
    QString configValue(const QString &name, const QString &defaultValue = QString()) const;
    void setConfigValue(const QString &name, const QString &value);
    bool configBool(const QString &name, bool defaultValue) const;
    int configInt(const QString &name, int defaultValue) const;
    // Rows the user may edit in the settings page.
    QVariantList userConfigEntries() const;

    // Game files -----------------------------------------------------------
    QVariantList gameFiles(const QString &where = QString(),
                           const QVariantList &binds = QVariantList()) const;
    QVariantMap gameFileById(int gameFileId) const;
    QVariantMap gameFileByName(const QString &fileName) const;
    int insertGameFile(const QVariantMap &fields);
    void updateGameFile(int gameFileId, const QVariantMap &fields);
    void deleteGameFile(int gameFileId);
    QList<int> iwadGameFileIds() const;
    QList<int> untaggedGameFileIds() const;

    // Source ports ----------------------------------------------------------
    QVariantList sourcePorts(int launchType = -1) const;
    QVariantMap sourcePortById(int id) const;
    int insertSourcePort(const QVariantMap &fields);
    void updateSourcePort(int id, const QVariantMap &fields);
    void deleteSourcePort(int id);

    // IWads ------------------------------------------------------------------
    QVariantList iwads() const;
    int insertIWad(const QString &name, const QString &fileName, int gameFileId);
    void deleteIWad(int iwadId);
    QVariantMap iwadById(int iwadId) const;

    // Tags -------------------------------------------------------------------
    QVariantList tags() const;
    int insertTag(const QString &name, bool hasTab);
    void updateTag(int tagId, const QString &name, bool hasTab);
    void deleteTag(int tagId);
    void addTagMapping(int gameFileId, int tagId);
    void removeTagMapping(int gameFileId, int tagId);
    QList<int> tagsForGameFile(int gameFileId) const;
    QList<int> gameFileIdsForTag(int tagId) const;

    // Statistics -------------------------------------------------------------
    QVariantList stats(int gameFileId) const;
    void insertStats(const QVariantMap &fields);

    // Association files (screenshots, demos, saves) --------------------------
    QVariantList files(int gameFileId, int fileTypeId) const;
    int insertFile(int gameFileId, const QString &fileName, int fileTypeId,
                   int sourcePortId, const QString &description);
    void deleteFile(int fileId);
    void deleteFilesForGameFile(int gameFileId);

    static QVariantMap rowToMap(const class QSqlQuery &query);

private:
    void ensureSchema();
    void seedConfiguration();

    QSqlDatabase m_db;
};
