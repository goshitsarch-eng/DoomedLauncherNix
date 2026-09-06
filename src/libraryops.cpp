#include "libraryops.h"

#include "archivereader.h"
#include "database.h"
#include "launcherpaths.h"
#include "managedpath.h"
#include "titlepicextractor.h"
#include "wadparser.h"

#include <QDateTime>
#include <QCryptographicHash>
#include <QDir>
#include <QFileInfo>
#include <QImage>
#include <QRegularExpression>
#include <QSqlQuery>
#include <QUuid>

namespace
{
// Parses idgames-template text files for the fields the library shows.
struct TextInfo {
    QString title;
    QString author;
    QString description;
    QString releaseDate;
};

TextInfo parseTextFile(const QString &content)
{
    TextInfo info;
    static const QRegularExpression titleRe(
        QStringLiteral("^\\s*Title\\s*:\\s*(.+)$"), QRegularExpression::MultilineOption);
    static const QRegularExpression authorRe(
        QStringLiteral("^\\s*Author(?:\\(s\\))?\\s*:\\s*(.+)$"), QRegularExpression::MultilineOption);
    static const QRegularExpression dateRe(
        QStringLiteral("^\\s*(?:Release )?[Dd]ate\\s*:\\s*(.+)$"), QRegularExpression::MultilineOption);
    static const QRegularExpression descRe(
        QStringLiteral("^\\s*Description\\s*:\\s*(.+)$"), QRegularExpression::MultilineOption);

    if (const auto match = titleRe.match(content); match.hasMatch())
        info.title = match.captured(1).trimmed();
    if (const auto match = authorRe.match(content); match.hasMatch())
        info.author = match.captured(1).trimmed();
    if (const auto match = dateRe.match(content); match.hasMatch())
        info.releaseDate = match.captured(1).trimmed();
    if (const auto match = descRe.match(content); match.hasMatch()) {
        // The description usually continues on the following indented lines.
        info.description = match.captured(1).trimmed();
        const int start = match.capturedEnd();
        const QStringList rest = content.mid(start).split(QLatin1Char('\n'));
        for (const QString &line : rest) {
            const QString trimmed = line.trimmed();
            if (trimmed.isEmpty() || trimmed.contains(QLatin1Char(':')))
                break;
            info.description += QLatin1Char(' ') + trimmed;
        }
    }
    return info;
}

}

LibraryOps::LibraryOps(Database *db, QObject *parent)
    : QObject(parent)
    , m_db(db)
{
}

LibraryOps::AddResult LibraryOps::addFiles(const QStringList &paths, bool asIwads)
{
    AddResult result;
    LauncherPaths::ensureLayout();

    int current = 0;
    for (const QString &sourcePath : paths) {
        ++current;
        const QFileInfo sourceInfo(sourcePath);
        Q_EMIT progress(sourceInfo.fileName(), current, paths.size());

        if (!sourceInfo.exists() || !sourceInfo.isFile()) {
            result.failed.append(sourcePath);
            continue;
        }

        // Managed storage: copy into GameFiles/ under the base name unless
        // the file already lives there.
        QString storedName = sourceInfo.fileName();
        const QString destPath = LauncherPaths::gameFilesDir() + QLatin1Char('/') + storedName;
        if (sourceInfo.absoluteFilePath() != QFileInfo(destPath).absoluteFilePath()) {
            if (QFileInfo::exists(destPath) && !m_db->gameFileByName(storedName).isEmpty()) {
                QFile incoming(sourceInfo.absoluteFilePath());
                QFile managed(destPath);
                QCryptographicHash incomingHash(QCryptographicHash::Sha256);
                QCryptographicHash managedHash(QCryptographicHash::Sha256);
                if (!incoming.open(QIODevice::ReadOnly) || !managed.open(QIODevice::ReadOnly)
                    || !incomingHash.addData(&incoming) || !managedHash.addData(&managed)
                    || incomingHash.result() != managedHash.result()) {
                    result.failed.append(sourcePath);
                    continue;
                }
                // Keep the managed copy, but continue below so Add IWADs
                // can promote a file that was previously imported as a mod.
            } else {
                // Never replace an untracked file in managed storage merely
                // because an import happens to use the same base name.
                if (QFileInfo::exists(destPath)) {
                    result.failed.append(sourcePath);
                    continue;
                }
                if (!QFile::copy(sourceInfo.absoluteFilePath(), destPath)) {
                    result.failed.append(sourcePath);
                    continue;
                }
            }
        }

        QVariantMap existing = m_db->gameFileByName(storedName);
        int gameFileId = existing.value(QStringLiteral("GameFileID"), -1).toInt();
        if (existing.isEmpty()) {
            QVariantMap fields;
            fields.insert(QStringLiteral("FileName"), storedName);
            fields.insert(QStringLiteral("Downloaded"),
                          QDateTime::currentDateTime().toString(QStringLiteral("yyyy-MM-dd HH:mm:ss")));
            fields.insert(QStringLiteral("SettingsStat"), 1);
            gameFileId = m_db->insertGameFile(fields);
        }
        if (gameFileId < 0) {
            result.failed.append(sourcePath);
            continue;
        }

        fillMetadata(gameFileId, storedName);

        if (asIwads) {
            bool known = false;
            const QVariantList iwadRows = m_db->iwads();
            for (const QVariant &iwadVariant : iwadRows) {
                if (iwadVariant.toMap().value(QStringLiteral("GameFileID")).toInt() == gameFileId)
                    known = true;
            }
            if (!known)
                m_db->insertIWad(WadParser::iwadDisplayName(storedName), storedName, gameFileId);
        }

        result.added.append(storedName);
    }
    return result;
}

void LibraryOps::fillMetadata(int gameFileId, const QString &storedName)
{
    const QString path = LauncherPaths::gameFilesDir() + QLatin1Char('/') + storedName;

    // Collect the wads and pk3s making up this game file (extracting from
    // a zip container when needed); maps and the title pic both use them.
    QStringList wadPaths;
    QStringList pk3Paths;
    const QString suffix = QFileInfo(path).suffix().toLower();
    if (ArchiveReader::isZipContainer(path)) {
        const QList<ArchiveReader::Entry> entries = ArchiveReader::entries(path);
        for (const ArchiveReader::Entry &entry : entries) {
            const QString entrySuffix = QFileInfo(entry.name).suffix().toLower();
            if (entrySuffix == QStringLiteral("wad") || entrySuffix == QStringLiteral("iwad")) {
                const QString extracted =
                    ArchiveReader::extract(path, entry.name, LauncherPaths::tempDir());
                if (!extracted.isEmpty())
                    wadPaths.append(extracted);
            } else if (entrySuffix == QStringLiteral("pk3") || entrySuffix == QStringLiteral("ipk3")) {
                const QString extracted =
                    ArchiveReader::extract(path, entry.name, LauncherPaths::tempDir());
                if (!extracted.isEmpty())
                    pk3Paths.append(extracted);
            }
        }
    } else if (WadParser::isWad(path)) {
        wadPaths.append(path);
    } else if (suffix == QStringLiteral("pk3") || suffix == QStringLiteral("ipk3")) {
        pk3Paths.append(path);
    }

    QStringList maps;
    for (const QString &wadPath : wadPaths)
        maps += WadParser::mapNames(wadPath);
    maps.removeDuplicates();

    generateTitlePic(gameFileId, wadPaths, pk3Paths, storedName);

    QVariantMap update;
    if (!maps.isEmpty()) {
        update.insert(QStringLiteral("Map"), maps.join(QStringLiteral(", ")));
        update.insert(QStringLiteral("MapCount"), maps.size());
    }

    // Metadata from the idgames-style text file.
    const QStringList textFiles = listTextFiles(gameFileId);
    QString textEntry;
    // Prefer a .txt named like the archive itself.
    const QString base = QFileInfo(storedName).completeBaseName().toLower();
    for (const QString &name : textFiles) {
        if (QFileInfo(name).completeBaseName().toLower() == base) {
            textEntry = name;
            break;
        }
    }
    if (textEntry.isEmpty() && !textFiles.isEmpty())
        textEntry = textFiles.first();
    if (!textEntry.isEmpty()) {
        const TextInfo info = parseTextFile(ArchiveReader::readTextEntry(path, textEntry));
        const QVariantMap row = m_db->gameFileById(gameFileId);
        if (!info.title.isEmpty() && row.value(QStringLiteral("Title")).toString().isEmpty())
            update.insert(QStringLiteral("Title"), info.title);
        if (!info.author.isEmpty() && row.value(QStringLiteral("Author")).toString().isEmpty())
            update.insert(QStringLiteral("Author"), info.author);
        if (!info.description.isEmpty()
            && row.value(QStringLiteral("Description")).toString().isEmpty())
            update.insert(QStringLiteral("Description"), info.description);
    }

    if (update.isEmpty())
        return;
    m_db->updateGameFile(gameFileId, update);
}

void LibraryOps::generateTitlePic(int gameFileId, const QStringList &wadPaths,
                                  const QStringList &pk3Paths, const QString &hintName)
{
    if (wadPaths.isEmpty() && pk3Paths.isEmpty())
        return;
    if (!m_db->configBool(QStringLiteral("AutomaticallyPullTitlpic"), true))
        return;

    // Keep an existing title pic (a resync re-extracts only when the row's
    // file went missing on disk).
    const QVariantList existing = m_db->files(gameFileId, 6 /*TitlePic*/);
    for (const QVariant &fileVariant : existing) {
        const QString name = fileVariant.toMap().value(QStringLiteral("FileName")).toString();
        if (QFileInfo::exists(LauncherPaths::titlePicsDir() + QLatin1Char('/') + name))
            return;
        m_db->deleteFile(fileVariant.toMap().value(QStringLiteral("FileID")).toInt());
    }

    const QImage image = TitlePicExtractor::extract(wadPaths, pk3Paths, hintName);
    if (image.isNull())
        return;

    const QString name =
        QUuid::createUuid().toString(QUuid::WithoutBraces) + QStringLiteral(".png");
    if (!image.save(LauncherPaths::titlePicsDir() + QLatin1Char('/') + name, "PNG"))
        return;
    m_db->insertFile(gameFileId, name, 6 /*TitlePic*/, -1, QStringLiteral("TITLEPIC"));
}

void LibraryOps::resync(int gameFileId)
{
    const QVariantMap row = m_db->gameFileById(gameFileId);
    if (row.isEmpty())
        return;
    const QString fileName = row.value(QStringLiteral("FileName")).toString();
    if (ManagedPath::isSafeFileName(fileName))
        fillMetadata(gameFileId, fileName);
}

void LibraryOps::deleteGameFile(int gameFileId, bool deleteManagedFile)
{
    const QVariantMap row = m_db->gameFileById(gameFileId);
    if (row.isEmpty())
        return;
    if (deleteManagedFile) {
        const QString fileName = row.value(QStringLiteral("FileName")).toString();
        if (ManagedPath::isSafeFileName(fileName))
            QFile::remove(LauncherPaths::gameFilesDir() + QLatin1Char('/') + fileName);
    }
    m_db->deleteGameFile(gameFileId);
}

QString LibraryOps::renameGameFile(int gameFileId, const QString &newName)
{
    const QVariantMap row = m_db->gameFileById(gameFileId);
    if (row.isEmpty())
        return tr("File not found.");
    const QString oldName = row.value(QStringLiteral("FileName")).toString();
    if (!ManagedPath::isSafeFileName(oldName))
        return tr("Unmanaged files cannot be renamed from the launcher.");
    if (!ManagedPath::isSafeFileName(newName))
        return tr("The new name must be a file name without directory components.");

    const QString oldPath = LauncherPaths::gameFilesDir() + QLatin1Char('/') + oldName;
    const QString newPath = LauncherPaths::gameFilesDir() + QLatin1Char('/') + newName;
    if (QFileInfo::exists(newPath))
        return tr("A file named %1 already exists.").arg(newName);
    if (!QFile::rename(oldPath, newPath))
        return tr("Failed to rename %1.").arg(oldName);

    m_db->updateGameFile(gameFileId, {{QStringLiteral("FileName"), newName}});
    // Remembered additional-file lists refer to names, not IDs.
    for (const QVariant &value : m_db->gameFiles()) {
        const QVariantMap file = value.toMap();
        QStringList names = file.value(QStringLiteral("SettingsFiles")).toString()
                                .split(QLatin1Char(';'), Qt::SkipEmptyParts);
        if (names.contains(oldName)) {
            for (QString &name : names) {
                if (name == oldName)
                    name = newName;
            }
            m_db->updateGameFile(file.value(QStringLiteral("GameFileID")).toInt(),
                                {{QStringLiteral("SettingsFiles"), names.join(QLatin1Char(';'))}});
        }
    }
    QSqlQuery iwad(m_db->db());
    iwad.prepare(QStringLiteral("UPDATE IWads SET FileName = ? WHERE GameFileID = ?"));
    iwad.addBindValue(newName);
    iwad.addBindValue(gameFileId);
    iwad.exec();
    return {};
}

QStringList LibraryOps::listTextFiles(int gameFileId) const
{
    QStringList names;
    const QString path = gameFilePath(gameFileId);
    if (path.isEmpty())
        return names;
    const QList<ArchiveReader::Entry> entries = ArchiveReader::entries(path);
    for (const ArchiveReader::Entry &entry : entries) {
        if (QFileInfo(entry.name).suffix().compare(QStringLiteral("txt"), Qt::CaseInsensitive) == 0)
            names.append(entry.name);
    }
    return names;
}

QString LibraryOps::extractTextFile(int gameFileId, const QString &entryName) const
{
    const QString path = gameFilePath(gameFileId);
    if (path.isEmpty())
        return {};
    return ArchiveReader::extract(path, entryName, LauncherPaths::tempDir());
}

QString LibraryOps::gameFilePath(int gameFileId) const
{
    const QVariantMap row = m_db->gameFileById(gameFileId);
    if (row.isEmpty())
        return {};
    const QString fileName = row.value(QStringLiteral("FileName")).toString();
    if (QDir::isAbsolutePath(fileName))
        return fileName;
    if (!ManagedPath::isSafeFileName(fileName))
        return {};
    return LauncherPaths::gameFilesDir() + QLatin1Char('/') + fileName;
}

bool LibraryOps::importAssociationFile(int gameFileId, const QString &path, int fileType)
{
    const QFileInfo info(path);
    if (!info.exists())
        return false;

    QString destDir;
    switch (fileType) {
    case 1: destDir = LauncherPaths::screenshotsDir(); break;
    case 2: destDir = LauncherPaths::demosDir(); break;
    case 3: destDir = LauncherPaths::saveGamesDir(); break;
    default: destDir = LauncherPaths::screenshotsDir(); break;
    }

    QString destName = info.fileName();
    QString destPath = destDir + QLatin1Char('/') + destName;
    int suffix = 1;
    while (QFileInfo::exists(destPath)) {
        destName = QStringLiteral("%1_%2.%3")
                       .arg(info.completeBaseName()).arg(suffix++).arg(info.suffix());
        destPath = destDir + QLatin1Char('/') + destName;
    }
    if (!QFile::copy(info.absoluteFilePath(), destPath))
        return false;

    m_db->insertFile(gameFileId, destName, fileType, -1, info.fileName());
    return true;
}
