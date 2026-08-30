#include "launcherapp.h"

#include "externalurl.h"

#include "database.h"
#include "idgamesclient.h"
#include "launcherpaths.h"
#include "librarymodel.h"
#include "libraryops.h"
#include "sourceportdetector.h"
#include "sourceportmodel.h"
#include "storescanner.h"
#include "thememanager.h"

#include <QDesktopServices>
#include <QDir>
#include <QDirIterator>
#include <QFileInfo>
#include <QUrl>
#include <QtConcurrent>

#include <algorithm>

LauncherApp::LauncherApp(QObject *parent)
    : QObject(parent)
{
}

QString LauncherApp::initialize()
{
    m_db = new Database(this);
    QString error;
    if (!m_db->open(&error))
        return tr("Failed to open the library database: %1").arg(error);

    m_library = new LibraryModel(m_db, this);
    m_sourcePorts = new SourcePortModel(m_db, this);
    m_utilities = new SourcePortModel(m_db, this);
    m_utilities->setLaunchType(1);
    m_launcher = new GameLauncher(m_db, this);
    m_ops = new LibraryOps(m_db, this);
    m_idGames = new IdGamesClient(m_db, this);
    m_theme = new ThemeManager(m_db, this);
    m_theme->apply();

    connect(m_launcher, &GameLauncher::processExited, this, [this](int, int minutes) {
        Q_EMIT toast(tr("Play session finished (%1 min)").arg(minutes));
        Q_EMIT libraryChanged();
    });
    connect(m_launcher, &GameLauncher::launchFailed, this, [this](const QString &message) {
        Q_EMIT toast(tr("Launch failed: %1").arg(message));
    });
    connect(m_launcher, &GameLauncher::statisticsRecorded, this, [this](int, int levelCount) {
        Q_EMIT toast(tr("Recorded statistics for %n level(s)", nullptr, levelCount));
        Q_EMIT libraryChanged();
    });
    connect(m_idGames, &IdGamesClient::downloadFinished, this,
            [this](const QString &fileName, const QString &localPath) {
                const LibraryOps::AddResult result = m_ops->addFiles({localPath}, false);
                QFile::remove(localPath);
                Q_EMIT libraryChanged();
                if (!result.added.isEmpty()) {
                    Q_EMIT toast(tr("Downloaded and imported %1").arg(fileName));
                    if (m_playAfterDownload.compare(fileName, Qt::CaseInsensitive) == 0) {
                        m_playAfterDownload.clear();
                        const QVariantMap file = m_db->gameFileByName(fileName);
                        if (!file.isEmpty())
                            playWithDefaults(file.value(QStringLiteral("GameFileID")).toInt());
                    }
                } else {
                    Q_EMIT toast(tr("Failed to import %1").arg(fileName));
                }
            });
    connect(m_idGames, &IdGamesClient::downloadFailed, this,
            [this](const QString &fileName, const QString &message) {
                Q_EMIT toast(tr("Download of %1 failed: %2").arg(fileName, message));
            });

    ensureDefaults();
    return {};
}

QObject *LauncherApp::library() const { return m_library; }
QObject *LauncherApp::sourcePorts() const { return m_sourcePorts; }
QObject *LauncherApp::utilities() const { return m_utilities; }
QObject *LauncherApp::idGames() const { return m_idGames; }
QObject *LauncherApp::theme() const { return m_theme; }

QVariantList LauncherApp::tabs() const
{
    QVariantList result;
    const QStringList visible = m_db->configValue(QStringLiteral("VisibleViews"),
                                                  QStringLiteral("Recent;Untagged;IWads;Id Games"))
                                    .split(QLatin1Char(';'), Qt::SkipEmptyParts);

    auto tab = [](const QString &title, int kind, int tagId = -1) {
        return QVariantMap{{QStringLiteral("title"), title},
                           {QStringLiteral("kind"), kind},
                           {QStringLiteral("tagId"), tagId}};
    };

    if (visible.contains(QStringLiteral("Recent")))
        result.append(tab(tr("Recent"), LibraryModel::Recent));
    result.append(tab(tr("Local"), LibraryModel::Local));
    if (visible.contains(QStringLiteral("Untagged")))
        result.append(tab(tr("Untagged"), LibraryModel::Untagged));
    if (visible.contains(QStringLiteral("IWads")))
        result.append(tab(tr("IWADs"), LibraryModel::IWads));
    if (visible.contains(QStringLiteral("Id Games")))
        result.append(tab(tr("Id Games"), LibraryModel::IdGames));

    const QVariantList tagRows = m_db->tags();
    for (const QVariant &tagVariant : tagRows) {
        const QVariantMap tagRow = tagVariant.toMap();
        if (tagRow.value(QStringLiteral("HasTab")).toInt() != 0)
            result.append(tab(tagRow.value(QStringLiteral("Name")).toString(), LibraryModel::Tag,
                              tagRow.value(QStringLiteral("TagID")).toInt()));
    }
    return result;
}

bool LauncherApp::showPlayDialog() const
{
    return m_db->configBool(QStringLiteral("ShowPlayDialog"), true);
}

void LauncherApp::setShowPlayDialog(bool show)
{
    m_db->setConfigValue(QStringLiteral("ShowPlayDialog"),
                         show ? QStringLiteral("true") : QStringLiteral("false"));
    Q_EMIT configChanged();
}

bool LauncherApp::tileView() const
{
    return m_db->configValue(QStringLiteral("GameFileViewType"), QStringLiteral("GridView"))
               .compare(QStringLiteral("GridView")) != 0;
}

void LauncherApp::setTileView(bool tile)
{
    m_db->setConfigValue(QStringLiteral("GameFileViewType"),
                         tile ? QStringLiteral("TileView") : QStringLiteral("GridView"));
    Q_EMIT configChanged();
}

bool LauncherApp::needsSetup() const
{
    return m_db->sourcePorts(0).isEmpty() || m_db->iwads().isEmpty();
}

int LauncherApp::lastTabIndex() const
{
    return m_db->configInt(QStringLiteral("LastSelectedTabIndex"), 0);
}

void LauncherApp::setLastTabIndex(int index)
{
    m_db->setConfigValue(QStringLiteral("LastSelectedTabIndex"), QString::number(index));
}

// Library operations ---------------------------------------------------------

void LauncherApp::addFiles(const QList<QUrl> &urls, bool asIwads)
{
    QStringList paths;
    for (const QUrl &url : urls) {
        // The KDE file dialog hands out local paths for network shares
        // through kio-fuse; anything still remote is skipped with a toast.
        if (url.isLocalFile())
            paths.append(url.toLocalFile());
        else if (url.scheme().isEmpty())
            paths.append(url.toString());
        else
            Q_EMIT toast(tr("Skipped remote location %1").arg(url.toDisplayString()));
    }
    if (paths.isEmpty())
        return;

    const LibraryOps::AddResult result = m_ops->addFiles(paths, asIwads);
    if (asIwads)
        ensureDefaults();
    Q_EMIT addFinished(result.added.size(), result.failed.size());
    Q_EMIT libraryChanged();
    Q_EMIT toast(tr("Added %1 file(s)").arg(result.added.size()));
}

void LauncherApp::addDirectory(const QUrl &url, bool recursive)
{
    const QString dir = url.isLocalFile() ? url.toLocalFile() : url.toString();
    if (dir.isEmpty() || !QFileInfo(dir).isDir())
        return;

    const QStringList nameFilters = {QStringLiteral("*.wad"), QStringLiteral("*.pk3"),
                                     QStringLiteral("*.pk7"), QStringLiteral("*.ipk3"),
                                     QStringLiteral("*.zip"), QStringLiteral("*.deh"),
                                     QStringLiteral("*.bex")};
    QStringList files;
    QDirIterator it(dir, nameFilters, QDir::Files,
                    recursive ? QDirIterator::Subdirectories : QDirIterator::NoIteratorFlags);
    while (it.hasNext())
        files.append(it.next());

    if (files.isEmpty()) {
        Q_EMIT toast(tr("No game files found in %1").arg(dir));
        return;
    }
    const LibraryOps::AddResult result = m_ops->addFiles(files, false);
    Q_EMIT addFinished(result.added.size(), result.failed.size());
    Q_EMIT libraryChanged();
    Q_EMIT toast(tr("Added %1 file(s)").arg(result.added.size()));
}

void LauncherApp::deleteGameFiles(const QList<int> &gameFileIds, bool deleteManagedFiles)
{
    for (int id : gameFileIds)
        m_ops->deleteGameFile(id, deleteManagedFiles);
    Q_EMIT libraryChanged();
}

QString LauncherApp::renameGameFile(int gameFileId, const QString &newName)
{
    const QString error = m_ops->renameGameFile(gameFileId, newName);
    if (error.isEmpty())
        Q_EMIT libraryChanged();
    return error;
}

void LauncherApp::resyncGameFiles(const QList<int> &gameFileIds)
{
    for (int id : gameFileIds)
        m_ops->resync(id);
    Q_EMIT libraryChanged();
    Q_EMIT toast(tr("Resync complete"));
}

void LauncherApp::updateGameFile(int gameFileId, const QVariantMap &fields)
{
    m_db->updateGameFile(gameFileId, fields);
    Q_EMIT libraryChanged();
}

QVariantMap LauncherApp::gameFile(int gameFileId) const
{
    return m_db->gameFileById(gameFileId);
}

void LauncherApp::openGameFile(int gameFileId)
{
    const QString path = m_ops->gameFilePath(gameFileId);
    if (!path.isEmpty())
        QDesktopServices::openUrl(QUrl::fromLocalFile(path));
}

void LauncherApp::openTextFile(int gameFileId)
{
    const QStringList names = m_ops->listTextFiles(gameFileId);
    if (names.isEmpty()) {
        Q_EMIT toast(tr("No text file found."));
        return;
    }
    const QString path = m_ops->extractTextFile(gameFileId, names.first());
    if (!path.isEmpty())
        QDesktopServices::openUrl(QUrl::fromLocalFile(path));
}

void LauncherApp::openUrl(const QString &url)
{
    const QUrl target = ExternalUrl::fromHttpInput(url);
    if (target.isEmpty()) {
        Q_EMIT toast(tr("Refused to open an unsupported URL."));
        return;
    }
    QDesktopServices::openUrl(target);
}

// Launching -------------------------------------------------------------------

LaunchRequest LauncherApp::requestFromMap(const QVariantMap &map) const
{
    LaunchRequest request;
    request.gameFileId = map.value(QStringLiteral("gameFileId"), -1).toInt();
    request.sourcePortId = map.value(QStringLiteral("sourcePortId"), -1).toInt();
    request.iwadId = map.value(QStringLiteral("iwadId"), -1).toInt();
    request.map = map.value(QStringLiteral("map")).toString();
    request.skill = map.value(QStringLiteral("skill")).toString();
    request.extraParams = map.value(QStringLiteral("extraParams")).toString();
    request.extraParamsOnly = map.value(QStringLiteral("extraParamsOnly"), false).toBool();
    request.loadLatestSave = map.value(QStringLiteral("loadLatestSave"), false).toBool();
    request.saveStatistics = map.value(QStringLiteral("saveStatistics"), true).toBool();
    request.additionalFiles = map.value(QStringLiteral("additionalFiles")).toStringList();
    request.remember = map.value(QStringLiteral("remember"), true).toBool();
    request.playDemoFile = map.value(QStringLiteral("playDemoFile")).toString();

    if (map.value(QStringLiteral("recordDemo"), false).toBool()) {
        const QString stamp =
            QDateTime::currentDateTime().toString(QStringLiteral("yyyyMMdd_HHmmss"));
        request.recordDemoFile = LauncherPaths::demosDir() + QStringLiteral("/demo_") + stamp;
    }

    // Launching an IWAD entry means -iwad itself instead of -file.
    request.isIwadLaunch = m_db->iwadGameFileIds().contains(request.gameFileId);
    if (request.isIwadLaunch && request.iwadId <= 0) {
        const QVariantList iwadRows = m_db->iwads();
        for (const QVariant &iwadVariant : iwadRows) {
            const QVariantMap iwadRow = iwadVariant.toMap();
            if (iwadRow.value(QStringLiteral("GameFileID")).toInt() == request.gameFileId)
                request.iwadId = iwadRow.value(QStringLiteral("IWadID")).toInt();
        }
    }
    return request;
}

QString LauncherApp::play(const QVariantMap &requestMap)
{
    const QString error = m_launcher->launch(requestFromMap(requestMap));
    if (error.isEmpty())
        Q_EMIT toast(tr("Launched"));
    return error;
}

QString LauncherApp::playWithDefaults(int gameFileId)
{
    return play(playDefaults(gameFileId));
}

QString LauncherApp::commandPreview(const QVariantMap &requestMap)
{
    return m_launcher->formatCommand(requestFromMap(requestMap));
}

QVariantMap LauncherApp::playDefaults(int gameFileId) const
{
    const QVariantMap file = m_db->gameFileById(gameFileId);
    QVariantMap map;
    map.insert(QStringLiteral("gameFileId"), gameFileId);

    int sourcePortId = file.value(QStringLiteral("SourcePortID"), -1).toInt();
    if (sourcePortId <= 0 || m_db->sourcePortById(sourcePortId).isEmpty())
        sourcePortId = m_db->configInt(QStringLiteral("DefaultSourcePort"), -1);
    if (sourcePortId <= 0 && !m_db->sourcePorts(0).isEmpty())
        sourcePortId = m_db->sourcePorts(0).first().toMap()
                           .value(QStringLiteral("SourcePortID")).toInt();
    map.insert(QStringLiteral("sourcePortId"), sourcePortId);

    int iwadId = file.value(QStringLiteral("IWadID"), -1).toInt();
    if (iwadId <= 0)
        iwadId = m_db->configInt(QStringLiteral("DefaultIWad"), -1);
    map.insert(QStringLiteral("iwadId"), iwadId);

    map.insert(QStringLiteral("map"), file.value(QStringLiteral("SettingsMap")).toString());
    map.insert(QStringLiteral("skill"), file.value(QStringLiteral("SettingsSkill")).toString());
    map.insert(QStringLiteral("extraParams"),
               file.value(QStringLiteral("SettingsExtraParams")).toString());
    map.insert(QStringLiteral("extraParamsOnly"),
               file.value(QStringLiteral("SettingsExtraParamsOnly")).toInt() != 0);
    map.insert(QStringLiteral("loadLatestSave"),
               file.value(QStringLiteral("SettingsLoadLatestSave")).toInt() != 0);
    // SettingsStat defaults on: a NULL column means "record statistics".
    const QVariant settingsStat = file.value(QStringLiteral("SettingsStat"));
    map.insert(QStringLiteral("saveStatistics"),
               settingsStat.isNull() || settingsStat.toInt() != 0);
    map.insert(QStringLiteral("additionalFiles"),
               file.value(QStringLiteral("SettingsFiles")).toString()
                   .split(QLatin1Char(';'), Qt::SkipEmptyParts));
    map.insert(QStringLiteral("remember"), true);
    map.insert(QStringLiteral("maps"),
               file.value(QStringLiteral("Map")).toString()
                   .split(QRegularExpression(QStringLiteral(",\\s*")), Qt::SkipEmptyParts));
    return map;
}

// Combo data ------------------------------------------------------------------

QVariantList LauncherApp::iwadEntries() const
{
    QVariantList result;
    const QVariantList iwadRows = m_db->iwads();
    for (const QVariant &iwadVariant : iwadRows) {
        const QVariantMap iwadRow = iwadVariant.toMap();
        QVariantMap entry;
        entry.insert(QStringLiteral("iwadId"), iwadRow.value(QStringLiteral("IWadID")));
        const QString name = iwadRow.value(QStringLiteral("Name")).toString();
        entry.insert(QStringLiteral("name"),
                     name.isEmpty() ? iwadRow.value(QStringLiteral("FileName")).toString() : name);
        result.append(entry);
    }
    return result;
}

QVariantList LauncherApp::tagEntries() const
{
    QVariantList result;
    const QVariantList tagRows = m_db->tags();
    for (const QVariant &tagVariant : tagRows) {
        const QVariantMap tagRow = tagVariant.toMap();
        result.append(QVariantMap{
            {QStringLiteral("tagId"), tagRow.value(QStringLiteral("TagID"))},
            {QStringLiteral("name"), tagRow.value(QStringLiteral("Name"))},
            {QStringLiteral("hasTab"), tagRow.value(QStringLiteral("HasTab")).toInt() != 0},
        });
    }
    return result;
}

QVariantList LauncherApp::tagsOfGameFile(int gameFileId) const
{
    QVariantList result;
    for (int tagId : m_db->tagsForGameFile(gameFileId))
        result.append(tagId);
    return result;
}

void LauncherApp::setGameFileTag(int gameFileId, int tagId, bool enabled)
{
    if (enabled)
        m_db->addTagMapping(gameFileId, tagId);
    else
        m_db->removeTagMapping(gameFileId, tagId);
    Q_EMIT libraryChanged();
}

int LauncherApp::createTag(const QString &name, bool hasTab)
{
    const int id = m_db->insertTag(name, hasTab);
    Q_EMIT tabsChanged();
    return id;
}

void LauncherApp::updateTag(int tagId, const QString &name, bool hasTab)
{
    m_db->updateTag(tagId, name, hasTab);
    Q_EMIT tabsChanged();
    Q_EMIT libraryChanged();
}

void LauncherApp::deleteTag(int tagId)
{
    m_db->deleteTag(tagId);
    Q_EMIT tabsChanged();
    Q_EMIT libraryChanged();
}

// Association files ------------------------------------------------------------

QVariantList LauncherApp::associationFiles(int gameFileId, int fileType) const
{
    return m_db->files(gameFileId, fileType);
}

void LauncherApp::importAssociationFiles(int gameFileId, const QList<QUrl> &urls)
{
    for (const QUrl &url : urls) {
        if (!url.isLocalFile())
            continue;
        const QString path = url.toLocalFile();
        const QString suffix = QFileInfo(path).suffix().toLower();
        int type = 1; // screenshot
        if (suffix == QStringLiteral("lmp"))
            type = 2;
        else if (suffix == QStringLiteral("zds") || suffix == QStringLiteral("dsg")
                 || suffix == QStringLiteral("esg") || suffix == QStringLiteral("hsg")
                 || suffix == QStringLiteral("save"))
            type = 3;
        m_ops->importAssociationFile(gameFileId, path, type);
    }
    Q_EMIT libraryChanged();
}

void LauncherApp::deleteAssociationFile(int fileId)
{
    m_db->deleteFile(fileId);
    Q_EMIT libraryChanged();
}

void LauncherApp::openAssociationFile(const QString &fileName, int fileType)
{
    QString base;
    switch (fileType) {
    case 2: base = LauncherPaths::demosDir(); break;
    case 3: base = LauncherPaths::saveGamesDir(); break;
    case 6: base = LauncherPaths::titlePicsDir(); break;
    default: base = LauncherPaths::screenshotsDir(); break;
    }
    const QString path =
        QDir::isAbsolutePath(fileName) ? fileName : base + QLatin1Char('/') + fileName;
    QDesktopServices::openUrl(QUrl::fromLocalFile(path));
}

// Source port detection ----------------------------------------------------------

void LauncherApp::detectSourcePorts()
{
    auto future = QtConcurrent::run([] { return SourcePortDetector::detect(); });
    auto *watcher = new QFutureWatcher<QList<SourcePortDetector::DetectedPort>>(this);
    connect(watcher, &QFutureWatcherBase::finished, this, [this, watcher]() {
        const QList<SourcePortDetector::DetectedPort> detected = watcher->result();
        watcher->deleteLater();

        int added = 0;
        const QVariantList existing = m_db->sourcePorts(-1);
        for (const SourcePortDetector::DetectedPort &port : detected) {
            bool known = false;
            for (const QVariant &rowVariant : existing) {
                if (rowVariant.toMap().value(QStringLiteral("Executable")).toString()
                        .compare(port.executable, Qt::CaseInsensitive) == 0)
                    known = true;
            }
            if (known)
                continue;
            QVariantMap fields;
            fields.insert(QStringLiteral("Name"), port.name);
            fields.insert(QStringLiteral("Executable"), port.executable);
            fields.insert(QStringLiteral("Directory"), port.directory);
            m_db->insertSourcePort(fields);
            ++added;
        }
        ensureDefaults();
        m_sourcePorts->reload();
        Q_EMIT detectFinished(added);
        Q_EMIT libraryChanged();
    });
    watcher->setFuture(future);
}

void LauncherApp::scanGameStores()
{
    auto future = QtConcurrent::run([] { return StoreScanner::scan(); });
    auto *watcher = new QFutureWatcher<StoreScanner::Result>(this);
    connect(watcher, &QFutureWatcherBase::finished, this, [this, watcher]() {
        const StoreScanner::Result result = watcher->result();
        watcher->deleteLater();

        // Imports copy into the managed GameFiles directory, so the store
        // install stays untouched.
        int iwads = 0;
        int pwads = 0;
        if (!result.iwads.isEmpty())
            iwads = m_ops->addFiles(result.iwads, true).added.size();
        if (!result.pwads.isEmpty())
            pwads = m_ops->addFiles(result.pwads, false).added.size();

        bool foundDoom64 = false;
        if (!result.doom64Exe.isEmpty()) {
            foundDoom64 = true;
            bool known = false;
            const QVariantList existing = m_db->sourcePorts(-1);
            for (const QVariant &rowVariant : existing) {
                if (rowVariant.toMap().value(QStringLiteral("Executable")).toString()
                        .compare(result.doom64Exe, Qt::CaseInsensitive) == 0)
                    known = true;
            }
            if (!known) {
                QVariantMap fields;
                fields.insert(QStringLiteral("Name"), tr("Doom 64 (Re-release)"));
                fields.insert(QStringLiteral("Executable"), result.doom64Exe);
                fields.insert(QStringLiteral("Directory"),
                              QFileInfo(result.doom64Exe).absolutePath());
                fields.insert(QStringLiteral("LaunchType"), 2 /*Doom64*/);
                m_db->insertSourcePort(fields);
                m_sourcePorts->reload();
            }
        }

        ensureDefaults();
        Q_EMIT storeScanFinished(iwads, pwads, foundDoom64);
        Q_EMIT libraryChanged();
        if (iwads + pwads > 0)
            Q_EMIT toast(tr("Imported %1 WAD(s) from Steam/GOG installs").arg(iwads + pwads));
        else
            Q_EMIT toast(tr("No Steam/GOG Doom installs found"));
    });
    watcher->setFuture(future);
}

void LauncherApp::ensureDefaults()
{
    // Pick a default source port (preferring the ZDoom family) and a
    // default IWAD (preferring Doom II, then Freedoom) when unset.
    const QVariantList ports = m_db->sourcePorts(0);
    if (!ports.isEmpty()) {
        const int current = m_db->configInt(QStringLiteral("DefaultSourcePort"), 0);
        if (current <= 0 || m_db->sourcePortById(current).isEmpty()) {
            int chosen = ports.first().toMap().value(QStringLiteral("SourcePortID")).toInt();
            for (const QVariant &portVariant : ports) {
                const QVariantMap portRow = portVariant.toMap();
                if (portRow.value(QStringLiteral("Executable")).toString()
                        .contains(QStringLiteral("zdoom"), Qt::CaseInsensitive)) {
                    chosen = portRow.value(QStringLiteral("SourcePortID")).toInt();
                    break;
                }
            }
            m_db->setConfigValue(QStringLiteral("DefaultSourcePort"), QString::number(chosen));
        }
    }

    const QVariantList iwadRows = m_db->iwads();
    if (!iwadRows.isEmpty()) {
        const int current = m_db->configInt(QStringLiteral("DefaultIWad"), 0);
        if (current <= 0 || m_db->iwadById(current).isEmpty()) {
            int chosen = iwadRows.first().toMap().value(QStringLiteral("IWadID")).toInt();
            for (const QString &preferred : {QStringLiteral("doom2"), QStringLiteral("freedoom2")}) {
                bool found = false;
                for (const QVariant &iwadVariant : iwadRows) {
                    const QVariantMap iwadRow = iwadVariant.toMap();
                    if (iwadRow.value(QStringLiteral("FileName")).toString()
                            .contains(preferred, Qt::CaseInsensitive)) {
                        chosen = iwadRow.value(QStringLiteral("IWadID")).toInt();
                        found = true;
                        break;
                    }
                }
                if (found)
                    break;
            }
            m_db->setConfigValue(QStringLiteral("DefaultIWad"), QString::number(chosen));
        }
    }
}

// Settings ------------------------------------------------------------------------

QVariantList LauncherApp::configEntries() const
{
    return m_db->userConfigEntries();
}

void LauncherApp::setConfigValue(const QString &name, const QString &value)
{
    m_db->setConfigValue(name, value);
    Q_EMIT configChanged();
    Q_EMIT tabsChanged();
}

// idgames -------------------------------------------------------------------------

void LauncherApp::downloadIdGamesFile(const QVariantMap &row, bool playWhenDone)
{
    const QString dir = row.value(QStringLiteral("dir")).toString();
    const QString fileName = row.value(QStringLiteral("FileName")).toString();
    if (fileName.isEmpty())
        return;
    if (playWhenDone)
        m_playAfterDownload = fileName;
    Q_EMIT toast(tr("Downloading %1...").arg(fileName));
    m_idGames->download(dir, fileName);
}

// Statistics -----------------------------------------------------------------------

QVariantMap LauncherApp::statsSummary(const QList<int> &gameFileIds) const
{
    int totalMinutes = 0;
    int fileCount = 0;
    int mapCount = 0;
    int kills = 0;
    int secrets = 0;
    int sessions = 0;
    for (int id : gameFileIds) {
        const QVariantMap file = m_db->gameFileById(id);
        if (file.isEmpty())
            continue;
        ++fileCount;
        totalMinutes += file.value(QStringLiteral("MinutesPlayed")).toInt();
        mapCount += file.value(QStringLiteral("MapCount")).toInt();
        const QVariantList statRows = m_db->stats(id);
        sessions += statRows.size();
        for (const QVariant &statVariant : statRows) {
            const QVariantMap stat = statVariant.toMap();
            kills += stat.value(QStringLiteral("KillCount")).toInt();
            secrets += stat.value(QStringLiteral("SecretCount")).toInt();
        }
    }
    return {
        {QStringLiteral("files"), fileCount},
        {QStringLiteral("maps"), mapCount},
        {QStringLiteral("minutesPlayed"), totalMinutes},
        {QStringLiteral("kills"), kills},
        {QStringLiteral("secrets"), secrets},
        {QStringLiteral("recordedLevels"), sessions},
    };
}

QVariantList LauncherApp::statsForGameFile(int gameFileId) const
{
    QVariantList rows = m_db->stats(gameFileId);
    std::reverse(rows.begin(), rows.end());
    return rows;
}
