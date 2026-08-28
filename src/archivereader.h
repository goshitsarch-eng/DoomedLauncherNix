#pragma once

#include <QString>
#include <QStringList>

// Reads game file containers: zip/pk3 archives via KArchive, or plain
// files (wad, deh, ...) treated as a single-entry "archive". This is the
// only file that talks to KArchive so the rest of the backend stays
// framework-agnostic.
namespace ArchiveReader
{
struct Entry {
    QString name;         // file name inside the archive (no path)
    QString fullPath;     // absolute path once available on disk
    bool extractRequired; // true when the entry lives inside an archive
    qint64 size = 0;
};

// True when the file should be opened as a zip container (.zip; a .pk3 or
// .ipk3 is a zip too, but source ports load those directly, so they are
// treated as plain files just like the previous frontend did).
bool isZipContainer(const QString &filePath);

// Lists the entries of the container at filePath (recursively for zips).
QList<Entry> entries(const QString &filePath);

// Extracts one entry to destDir and returns the extracted absolute path,
// or an empty string on failure. For plain files returns the file itself.
QString extract(const QString &filePath, const QString &entryName, const QString &destDir);

// Reads the contents of a text entry (used for idgames .txt descriptions).
QString readTextEntry(const QString &filePath, const QString &entryName);

// Creates a zip at zipPath from every file directly inside directory.
bool zipDirectory(const QString &directory, const QString &zipPath);
}
