#pragma once

#include <QObject>
#include <QVariantList>
#include <QVariantMap>

#include "gamelauncher.h"

class Database;
class IdGamesClient;
class LibraryModel;
class LibraryOps;
class SourcePortModel;
class ThemeManager;

// Facade the QML UI talks to. Owns the database and every backend piece.
class LauncherApp : public QObject
{
    Q_OBJECT
    Q_PROPERTY(QObject *library READ library CONSTANT)
    Q_PROPERTY(QObject *sourcePorts READ sourcePorts CONSTANT)
    Q_PROPERTY(QObject *utilities READ utilities CONSTANT)
    Q_PROPERTY(QObject *idGames READ idGames CONSTANT)
    Q_PROPERTY(QObject *theme READ theme CONSTANT)
    Q_PROPERTY(QVariantList tabs READ tabs NOTIFY tabsChanged)
    Q_PROPERTY(bool showPlayDialog READ showPlayDialog WRITE setShowPlayDialog NOTIFY configChanged)
    Q_PROPERTY(bool tileView READ tileView WRITE setTileView NOTIFY configChanged)
    Q_PROPERTY(bool needsSetup READ needsSetup NOTIFY libraryChanged)
    Q_PROPERTY(int lastTabIndex READ lastTabIndex WRITE setLastTabIndex NOTIFY configChanged)
    Q_PROPERTY(bool detecting READ detecting NOTIFY detectingChanged)

public:
    explicit LauncherApp(QObject *parent = nullptr);

    // Returns an empty string on success, else the error to show.
    QString initialize();

    QObject *library() const;
    QObject *sourcePorts() const;
    QObject *utilities() const;
    QObject *idGames() const;
    QObject *theme() const;

    QVariantList tabs() const;
    bool showPlayDialog() const;
    void setShowPlayDialog(bool show);
    bool tileView() const;
    void setTileView(bool tile);
    bool needsSetup() const;
    int lastTabIndex() const;
    void setLastTabIndex(int index);
    bool detecting() const { return m_detecting; }
    Q_INVOKABLE QString localFilePath(const QUrl &url) const;

    // Library operations ---------------------------------------------------
    Q_INVOKABLE void addFiles(const QList<QUrl> &urls, bool asIwads);
    Q_INVOKABLE void addDirectory(const QUrl &url, bool recursive);
    Q_INVOKABLE void deleteGameFiles(const QList<int> &gameFileIds, bool deleteManagedFiles);
    Q_INVOKABLE QString renameGameFile(int gameFileId, const QString &newName);
    Q_INVOKABLE void resyncGameFiles(const QList<int> &gameFileIds);
    Q_INVOKABLE void updateGameFile(int gameFileId, const QVariantMap &fields);
    Q_INVOKABLE QVariantMap gameFile(int gameFileId) const;
    Q_INVOKABLE void openGameFile(int gameFileId);
    Q_INVOKABLE void openTextFile(int gameFileId);
    Q_INVOKABLE void openUrl(const QString &url);

    // Launching -------------------------------------------------------------
    // request keys: gameFileId, sourcePortId, iwadId, map, skill,
    // extraParams, extraParamsOnly, loadLatestSave, additionalFiles,
    // remember, recordDemo, playDemoFile.
    Q_INVOKABLE QString play(const QVariantMap &request);
    // Launches with the entry's remembered settings (row activation).
    Q_INVOKABLE QString playWithDefaults(int gameFileId);
    Q_INVOKABLE QString commandPreview(const QVariantMap &request);
    // Prefilled play dialog data for a game file.
    Q_INVOKABLE QVariantMap playDefaults(int gameFileId) const;

    // Combo data ------------------------------------------------------------
    Q_INVOKABLE QVariantList iwadEntries() const;
    Q_INVOKABLE QVariantList modEntries(int excludeGameFileId) const;
    Q_INVOKABLE QVariantList tagEntries() const;
    Q_INVOKABLE QVariantList tagsOfGameFile(int gameFileId) const;
    Q_INVOKABLE void setGameFileTag(int gameFileId, int tagId, bool enabled);
    Q_INVOKABLE int createTag(const QString &name, bool hasTab);
    Q_INVOKABLE void updateTag(int tagId, const QString &name, bool hasTab);
    Q_INVOKABLE void deleteTag(int tagId);

    // Association files -----------------------------------------------------
    Q_INVOKABLE QVariantList associationFiles(int gameFileId, int fileType) const;
    Q_INVOKABLE void importAssociationFiles(int gameFileId, const QList<QUrl> &urls);
    Q_INVOKABLE void deleteAssociationFile(int fileId);
    Q_INVOKABLE void openAssociationFile(const QString &fileName, int fileType);

    // Source port detection --------------------------------------------------
    Q_INVOKABLE void detectSourcePorts();

    // Steam/GOG/Heroic/Lutris scan: imports found IWADs, expansion wads
    // and the Doom 64 re-release binary. Emits storeScanFinished.
    Q_INVOKABLE void scanGameStores();

    // Settings page ----------------------------------------------------------
    Q_INVOKABLE QVariantList configEntries() const;
    Q_INVOKABLE void setConfigValue(const QString &name, const QString &value);

    // idgames ----------------------------------------------------------------
    Q_INVOKABLE void downloadIdGamesFile(const QVariantMap &row, bool playWhenDone);

    // Statistics summary for the selected files.
    Q_INVOKABLE QVariantMap statsSummary(const QList<int> &gameFileIds) const;
    // Recorded per-level statistics rows for one file, newest first.
    Q_INVOKABLE QVariantList statsForGameFile(int gameFileId) const;

Q_SIGNALS:
    void tabsChanged();
    void detectingChanged();
    void configChanged();
    void libraryChanged();
    void toast(const QString &message);
    void detectFinished(int addedCount);
    void addFinished(int added, int failed);
    void storeScanFinished(int iwads, int pwads, bool foundDoom64);

private:
    LaunchRequest requestFromMap(const QVariantMap &map) const;
    void ensureDefaults();

    Database *m_db = nullptr;
    LibraryModel *m_library = nullptr;
    SourcePortModel *m_sourcePorts = nullptr;
    SourcePortModel *m_utilities = nullptr;
    GameLauncher *m_launcher = nullptr;
    LibraryOps *m_ops = nullptr;
    IdGamesClient *m_idGames = nullptr;
    ThemeManager *m_theme = nullptr;
    QString m_playAfterDownload;
    bool m_detecting = false;
};
