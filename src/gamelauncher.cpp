#include "gamelauncher.h"

#include "archivereader.h"
#include "database.h"
#include "launcherpaths.h"
#include "sourceportdetector.h"

#include <QDir>
#include <QElapsedTimer>
#include <QFileInfo>
#include <QRegularExpression>

namespace
{
const QStringList dehackedExtensions = {QStringLiteral("deh"), QStringLiteral("bex")};

bool isDehacked(const QString &path)
{
    return dehackedExtensions.contains(QFileInfo(path).suffix().toLower());
}

QStringList splitExtensions(const QString &supported)
{
    QStringList result;
    const QStringList parts = supported.split(QLatin1Char(','), Qt::SkipEmptyParts);
    for (QString part : parts) {
        part = part.trimmed();
        if (part.startsWith(QLatin1Char('.')))
            part.remove(0, 1);
        if (!part.isEmpty())
            result.append(part.toLower());
    }
    return result;
}

QString storedFilePath(const QVariantMap &gameFile)
{
    const QString fileName = gameFile.value(QStringLiteral("FileName")).toString();
    if (QDir::isAbsolutePath(fileName))
        return fileName;
    return LauncherPaths::gameFilesDir() + QLatin1Char('/') + fileName;
}
}

GameLauncher::GameLauncher(Database *db, QObject *parent)
    : QObject(parent)
    , m_db(db)
{
}

bool GameLauncher::isZDoomFamily(const QString &executable)
{
    const QString key = executable.trimmed().toLower();
    for (const char *token : {"gzdoom", "uzdoom", "vkdoom", "lzdoom", "zandronum", "zdoom", "org.zdoom"}) {
        if (key.contains(QLatin1String(token)))
            return true;
    }
    return false;
}

QStringList GameLauncher::warpArguments(const QString &map, bool zdoomFamily)
{
    Q_UNUSED(zdoomFamily);
    // Classic episodic/numbered names use -warp for maximum compatibility,
    // anything else (UDMF/custom names) uses +map, exactly as before.
    static const QRegularExpression episodic(QStringLiteral("^E\\dM\\d$"));
    static const QRegularExpression numbered(QStringLiteral("^MAP\\d\\d$"));
    const QString upper = map.toUpper();

    if (episodic.match(upper).hasMatch())
        return {QStringLiteral("-warp"), QString(upper.at(1)), QString(upper.at(3))};
    if (numbered.match(upper).hasMatch())
        return {QStringLiteral("-warp"), QString::number(upper.mid(3).toInt())};
    return {QStringLiteral("+map"), map};
}

QString GameLauncher::extractIwad(const QVariantMap &iwadGameFile, const QVariantMap &sourcePort, QString *error)
{
    const QString path = storedFilePath(iwadGameFile);
    if (!QFileInfo::exists(path)) {
        *error = tr("Couldn't find the IWAD at %1").arg(path);
        return {};
    }
    if (!ArchiveReader::isZipContainer(path))
        return path;

    const QStringList extensions = splitExtensions(
        sourcePort.value(QStringLiteral("SupportedExtensions")).toString());
    const QList<ArchiveReader::Entry> entries = ArchiveReader::entries(path);
    for (const ArchiveReader::Entry &entry : entries) {
        const QString suffix = QFileInfo(entry.name).suffix().toLower();
        if (suffix == QStringLiteral("wad") || suffix == QStringLiteral("ipk3")
            || extensions.contains(suffix)) {
            const QString extracted = ArchiveReader::extract(path, entry.name, LauncherPaths::tempDir());
            if (!extracted.isEmpty())
                return extracted;
        }
    }
    *error = tr("No IWAD file found inside %1").arg(QFileInfo(path).fileName());
    return {};
}

QStringList GameLauncher::gameFileLaunchPaths(const QVariantMap &gameFile, const QVariantMap &sourcePort,
                                              const QStringList &specificFiles, QString *error)
{
    const QString path = storedFilePath(gameFile);
    if (QFileInfo(path).isDir())
        return {path};
    if (!QFileInfo::exists(path)) {
        *error = tr("Couldn't find game file at %1").arg(path);
        return {};
    }
    if (!ArchiveReader::isZipContainer(path))
        return {path};

    QStringList result;
    const QStringList extensions = splitExtensions(
        sourcePort.value(QStringLiteral("SupportedExtensions")).toString());
    const QList<ArchiveReader::Entry> entries = ArchiveReader::entries(path);
    for (const ArchiveReader::Entry &entry : entries) {
        if (!specificFiles.isEmpty()) {
            if (!specificFiles.contains(entry.name))
                continue;
        } else if (!extensions.contains(QFileInfo(entry.name).suffix().toLower())) {
            continue;
        }
        const QString extracted = ArchiveReader::extract(path, entry.name, LauncherPaths::tempDir());
        if (!extracted.isEmpty())
            result.append(extracted);
    }
    return result;
}

QString GameLauncher::latestSaveFile(const QVariantMap &sourcePort) const
{
    QStringList dirs;
    const QString altSaveDir = sourcePort.value(QStringLiteral("AltSaveDirectory")).toString();
    if (!altSaveDir.isEmpty())
        dirs << LauncherPaths::resolve(altSaveDir);
    dirs << LauncherPaths::saveGamesDir();

    QString newest;
    QDateTime newestTime;
    for (const QString &dir : dirs) {
        const QFileInfoList saves = QDir(dir).entryInfoList(
            {QStringLiteral("*.zds"), QStringLiteral("*.dsg"), QStringLiteral("*.esg"), QStringLiteral("*.hsg")},
            QDir::Files, QDir::Time);
        if (!saves.isEmpty() && (newest.isEmpty() || saves.first().lastModified() > newestTime)) {
            newest = saves.first().absoluteFilePath();
            newestTime = saves.first().lastModified();
        }
    }
    return newest;
}

GameLauncher::BuiltCommand GameLauncher::build(const LaunchRequest &request)
{
    BuiltCommand command;

    const QVariantMap sourcePort = m_db->sourcePortById(request.sourcePortId);
    if (sourcePort.isEmpty()) {
        command.error = tr("No source port selected.");
        return command;
    }
    const QVariantMap gameFile = m_db->gameFileById(request.gameFileId);
    if (gameFile.isEmpty()) {
        command.error = tr("No file selected.");
        return command;
    }

    const QString executable = sourcePort.value(QStringLiteral("Executable")).toString().trimmed();
    const QString portDirectory = sourcePort.value(QStringLiteral("Directory")).toString();

    // Resolve the program: flatpak:<id>, snap:<name>, or a binary.
    if (executable.startsWith(QStringLiteral("flatpak:"), Qt::CaseInsensitive)) {
        command.program = QStringLiteral("flatpak");
        command.arguments << QStringLiteral("run") << QStringLiteral("--filesystem=host")
                          << QStringLiteral("--filesystem=home") << executable.mid(8).trimmed()
                          << QStringLiteral("--");
    } else if (executable.startsWith(QStringLiteral("snap:"), Qt::CaseInsensitive)) {
        command.program = QStringLiteral("snap");
        command.arguments << QStringLiteral("run") << executable.mid(5).trimmed();
    } else if (QDir::isAbsolutePath(executable) && QFileInfo::exists(executable)) {
        command.program = executable;
    } else {
        QString full = executable;
        if (!portDirectory.isEmpty() && QFileInfo::exists(
                LauncherPaths::resolve(portDirectory) + QLatin1Char('/') + executable))
            full = LauncherPaths::resolve(portDirectory) + QLatin1Char('/') + executable;
        else if (const QString found = SourcePortDetector::findExecutable(executable); !found.isEmpty())
            full = found;
        if (full.isEmpty() || (!QFileInfo::exists(full) && QDir::isAbsolutePath(full))) {
            command.error = tr("Source port executable '%1' was not found. Open Source Ports to "
                               "detect an installed port or enter its full path.").arg(executable);
            return command;
        }
        command.program = full;
    }

    if (!portDirectory.isEmpty())
        command.workingDirectory = LauncherPaths::resolve(portDirectory);
    else if (QDir::isAbsolutePath(command.program))
        command.workingDirectory = QFileInfo(command.program).absolutePath();
    else
        command.workingDirectory = QDir::homePath();

    // Source port extra parameters always apply.
    const QString portExtra = sourcePort.value(QStringLiteral("ExtraParameters")).toString().trimmed();
    if (!portExtra.isEmpty())
        command.arguments << QProcess::splitCommand(portExtra);

    if (request.extraParamsOnly) {
        if (!request.extraParams.trimmed().isEmpty())
            command.arguments << QProcess::splitCommand(request.extraParams.trimmed());
        return command;
    }

    QString error;

    // IWAD.
    if (request.iwadId > 0 && !request.isIwadLaunch) {
        const QVariantMap iwad = m_db->iwadById(request.iwadId);
        const int iwadGameFileId = iwad.value(QStringLiteral("GameFileID")).toInt();
        const QVariantMap iwadGameFile = m_db->gameFileById(iwadGameFileId);
        if (!iwadGameFile.isEmpty()) {
            const QString iwadPath = extractIwad(iwadGameFile, sourcePort, &error);
            if (iwadPath.isEmpty()) {
                command.error = error;
                return command;
            }
            command.arguments << QStringLiteral("-iwad") << iwadPath;
        }
    } else if (request.isIwadLaunch) {
        const QString iwadPath = extractIwad(gameFile, sourcePort, &error);
        if (iwadPath.isEmpty()) {
            command.error = error;
            return command;
        }
        command.arguments << QStringLiteral("-iwad") << iwadPath;
    }

    // Game files: the selected file (unless it is the IWAD) plus additional mods.
    QStringList launchFiles;
    QList<QVariantMap> files;
    for (const QString &fileName : request.additionalFiles) {
        const QVariantMap file = m_db->gameFileByName(fileName);
        if (!file.isEmpty())
            files.append(file);
    }
    if (!request.isIwadLaunch)
        files.append(gameFile);

    const QStringList specificFiles = gameFile.value(QStringLiteral("SettingsSpecificFiles"))
                                          .toString().split(QLatin1Char(';'), Qt::SkipEmptyParts);
    for (const QVariantMap &file : files) {
        const bool isSelected =
            file.value(QStringLiteral("GameFileID")).toInt() == request.gameFileId;
        launchFiles += gameFileLaunchPaths(file, sourcePort,
                                           isSelected ? specificFiles : QStringList(), &error);
        if (!error.isEmpty()) {
            command.error = error;
            return command;
        }
    }

    QStringList dehFiles;
    QStringList normalFiles;
    for (const QString &file : launchFiles) {
        if (isDehacked(file))
            dehFiles.append(file);
        else
            normalFiles.append(file);
    }
    if (!normalFiles.isEmpty()) {
        const QString fileOption = sourcePort.value(QStringLiteral("FileOption")).toString().trimmed();
        command.arguments << (fileOption.isEmpty() ? QStringLiteral("-file") : fileOption);
        command.arguments << normalFiles;
    }
    if (!dehFiles.isEmpty()) {
        command.arguments << QStringLiteral("-deh") << dehFiles;
    }

    // Warp / skill.
    if (!request.map.trimmed().isEmpty()) {
        command.arguments << warpArguments(request.map.trimmed(), isZDoomFamily(executable));
        if (!request.skill.trimmed().isEmpty())
            command.arguments << QStringLiteral("-skill") << request.skill.trimmed();
    }

    // Demos.
    if (!request.playDemoFile.isEmpty())
        command.arguments << QStringLiteral("-playdemo") << request.playDemoFile;
    else if (!request.recordDemoFile.isEmpty())
        command.arguments << QStringLiteral("-record") << request.recordDemoFile;

    // Load latest save.
    if (request.loadLatestSave) {
        const QString save = latestSaveFile(sourcePort);
        if (!save.isEmpty())
            command.arguments << QStringLiteral("-loadgame") << save;
    }

    if (!request.extraParams.trimmed().isEmpty())
        command.arguments << QProcess::splitCommand(request.extraParams.trimmed());

    return command;
}

QString GameLauncher::formatCommand(const LaunchRequest &request)
{
    const BuiltCommand command = build(request);
    if (!command.error.isEmpty())
        return command.error;
    QStringList quoted;
    for (const QString &argument : command.arguments)
        quoted << (argument.contains(QLatin1Char(' '))
                       ? QStringLiteral("\"%1\"").arg(argument) : argument);
    return command.program + QLatin1Char(' ') + quoted.join(QLatin1Char(' '));
}

QString GameLauncher::launch(const LaunchRequest &request)
{
    const BuiltCommand command = build(request);
    if (!command.error.isEmpty())
        return command.error;

    auto *process = new QProcess(this);
    process->setProgram(command.program);
    process->setArguments(command.arguments);
    process->setWorkingDirectory(command.workingDirectory);
    process->setProcessChannelMode(QProcess::ForwardedChannels);

    auto *timer = new QElapsedTimer();
    timer->start();
    const int gameFileId = request.gameFileId;

    connect(process, &QProcess::finished, this,
            [this, process, timer, gameFileId](int, QProcess::ExitStatus) {
                const int minutes = int(timer->elapsed() / 60000);
                delete timer;
                process->deleteLater();
                --m_activeSessions;

                const QVariantMap gameFile = m_db->gameFileById(gameFileId);
                QVariantMap update;
                update.insert(QStringLiteral("LastPlayed"),
                              QDateTime::currentDateTime().toString(QStringLiteral("yyyy-MM-dd HH:mm:ss")));
                update.insert(QStringLiteral("MinutesPlayed"),
                              gameFile.value(QStringLiteral("MinutesPlayed")).toInt() + minutes);
                m_db->updateGameFile(gameFileId, update);
                Q_EMIT processExited(gameFileId, minutes);
            });
    connect(process, &QProcess::errorOccurred, this,
            [this, process](QProcess::ProcessError) {
                Q_EMIT launchFailed(process->errorString());
            });

    ++m_activeSessions;
    process->start();
    if (!process->waitForStarted(5000)) {
        --m_activeSessions;
        const QString errorText = tr("Failed to start %1: %2")
                                      .arg(command.program, process->errorString());
        process->deleteLater();
        return errorText;
    }

    // Persist the choices on the GameFiles row when asked to.
    if (request.remember) {
        QVariantMap update;
        update.insert(QStringLiteral("SourcePortID"), request.sourcePortId);
        update.insert(QStringLiteral("IWadID"), request.iwadId > 0 ? request.iwadId : QVariant());
        update.insert(QStringLiteral("SettingsMap"), request.map);
        update.insert(QStringLiteral("SettingsSkill"), request.skill);
        update.insert(QStringLiteral("SettingsExtraParams"), request.extraParams);
        update.insert(QStringLiteral("SettingsExtraParamsOnly"), request.extraParamsOnly ? 1 : 0);
        update.insert(QStringLiteral("SettingsLoadLatestSave"), request.loadLatestSave ? 1 : 0);
        update.insert(QStringLiteral("SettingsFiles"), request.additionalFiles.join(QLatin1Char(';')));
        update.insert(QStringLiteral("SettingsSaved"), 1);
        m_db->updateGameFile(request.gameFileId, update);
    }

    return {};
}
