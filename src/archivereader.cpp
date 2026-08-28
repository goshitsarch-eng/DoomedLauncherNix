#include "archivereader.h"

#include <KZip>
#include <KArchiveDirectory>
#include <KArchiveFile>

#include <QDir>
#include <QFileInfo>

namespace
{
void collectEntries(const KArchiveDirectory *dir, const QString &prefix,
                    QList<ArchiveReader::Entry> &result)
{
    const QStringList names = dir->entries();
    for (const QString &name : names) {
        const KArchiveEntry *entry = dir->entry(name);
        if (!entry)
            continue;
        if (entry->isDirectory()) {
            collectEntries(static_cast<const KArchiveDirectory *>(entry),
                           prefix + name + QLatin1Char('/'), result);
        } else {
            const auto *file = static_cast<const KArchiveFile *>(entry);
            ArchiveReader::Entry item;
            item.name = prefix + name;
            item.extractRequired = true;
            item.size = file->size();
            result.append(item);
        }
    }
}

const KArchiveFile *findFile(const KArchiveDirectory *root, const QString &entryName)
{
    const KArchiveEntry *entry = root->entry(entryName);
    if (entry && entry->isFile())
        return static_cast<const KArchiveFile *>(entry);
    return nullptr;
}
}

namespace ArchiveReader
{
bool isZipContainer(const QString &filePath)
{
    return QFileInfo(filePath).suffix().compare(QStringLiteral("zip"), Qt::CaseInsensitive) == 0;
}

QList<Entry> entries(const QString &filePath)
{
    QList<Entry> result;
    if (isZipContainer(filePath)) {
        KZip zip(filePath);
        if (!zip.open(QIODevice::ReadOnly))
            return result;
        collectEntries(zip.directory(), QString(), result);
        zip.close();
        return result;
    }

    QFileInfo info(filePath);
    if (info.exists()) {
        Entry item;
        item.name = info.fileName();
        item.fullPath = info.absoluteFilePath();
        item.extractRequired = false;
        item.size = info.size();
        result.append(item);
    }
    return result;
}

QString extract(const QString &filePath, const QString &entryName, const QString &destDir)
{
    if (!isZipContainer(filePath))
        return QFileInfo(filePath).absoluteFilePath();

    KZip zip(filePath);
    if (!zip.open(QIODevice::ReadOnly))
        return {};
    const KArchiveFile *file = findFile(zip.directory(), entryName);
    if (!file) {
        zip.close();
        return {};
    }

    QDir().mkpath(destDir);
    // Flatten the path: entries inside sub-directories are written directly
    // into destDir under their base name, matching the old Temp layout.
    const QString baseName = entryName.section(QLatin1Char('/'), -1);
    const QString target = destDir + QLatin1Char('/') + baseName;
    QFile::remove(target);
    QFile out(target);
    if (!out.open(QIODevice::WriteOnly)) {
        zip.close();
        return {};
    }
    out.write(file->data());
    out.close();
    zip.close();
    return target;
}

QString readTextEntry(const QString &filePath, const QString &entryName)
{
    if (!isZipContainer(filePath)) {
        QFile file(filePath);
        if (!file.open(QIODevice::ReadOnly))
            return {};
        return QString::fromUtf8(file.readAll());
    }

    KZip zip(filePath);
    if (!zip.open(QIODevice::ReadOnly))
        return {};
    const KArchiveFile *file = findFile(zip.directory(), entryName);
    const QString text = file ? QString::fromLatin1(file->data()) : QString();
    zip.close();
    return text;
}

bool zipDirectory(const QString &directory, const QString &zipPath)
{
    KZip zip(zipPath);
    if (!zip.open(QIODevice::WriteOnly))
        return false;
    const QFileInfoList files = QDir(directory).entryInfoList(QDir::Files);
    for (const QFileInfo &info : files) {
        if (!zip.addLocalFile(info.absoluteFilePath(), info.fileName())) {
            zip.close();
            return false;
        }
    }
    return zip.close();
}
}
