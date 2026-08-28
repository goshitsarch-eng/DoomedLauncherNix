#pragma once

#include <QObject>
#include <QStringList>

class Database;

// File management: importing game files and IWADs into the library,
// reading their maps and idgames-style text files, renaming and deleting.
class LibraryOps : public QObject
{
    Q_OBJECT

public:
    explicit LibraryOps(Database *db, QObject *parent = nullptr);

    struct AddResult {
        QStringList added;
        QStringList failed;
    };

    // Copies the given local files into GameFiles/ (unless they already
    // live there), creates the GameFiles rows and reads maps + metadata.
    // asIwads also registers the files in the IWads table.
    AddResult addFiles(const QStringList &paths, bool asIwads);

    // Re-reads maps and text-file metadata for an existing entry.
    void resync(int gameFileId);

    void deleteGameFile(int gameFileId, bool deleteManagedFile);
    QString renameGameFile(int gameFileId, const QString &newName);

    // Text files inside the archive (readme/idgames .txt).
    QStringList listTextFiles(int gameFileId) const;
    QString extractTextFile(int gameFileId, const QString &entryName) const;

    // Absolute path of the stored game file.
    QString gameFilePath(int gameFileId) const;

    // Copies an external image/demo/save next to the game file entry and
    // records it in the Files table. Returns false on failure.
    bool importAssociationFile(int gameFileId, const QString &path, int fileType);

signals:
    void progress(const QString &fileName, int current, int total);

private:
    void fillMetadata(int gameFileId, const QString &storedName);

    Database *m_db;
};
