#include "librarymodel.h"

#include "database.h"
#include "launcherpaths.h"

#include <QDateTime>
#include <QDir>
#include <QFileInfo>

#include <algorithm>

LibraryModel::LibraryModel(Database *db, QObject *parent)
    : QAbstractListModel(parent)
    , m_db(db)
{
}

int LibraryModel::rowCount(const QModelIndex &parent) const
{
    return parent.isValid() ? 0 : m_rows.size();
}

QHash<int, QByteArray> LibraryModel::roleNames() const
{
    return {
        {GameFileIdRole, "gameFileId"},
        {FileNameRole, "fileName"},
        {TitleRole, "title"},
        {AuthorRole, "author"},
        {DescriptionRole, "description"},
        {MapsRole, "maps"},
        {RatingRole, "rating"},
        {LastPlayedRole, "lastPlayed"},
        {MinutesPlayedRole, "minutesPlayed"},
        {ImagePathRole, "imagePath"},
        {IsIwadRole, "isIwad"},
        {CommentsRole, "comments"},
        {ReleaseDateRole, "releaseDate"},
    };
}

QVariant LibraryModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() >= m_rows.size())
        return {};
    const QVariantMap row = m_rows.at(index.row()).toMap();
    switch (role) {
    case GameFileIdRole:
        return row.value(QStringLiteral("GameFileID"), -1);
    case FileNameRole: {
        const QString fileName = row.value(QStringLiteral("FileName")).toString();
        return QFileInfo(fileName).fileName();
    }
    case TitleRole: {
        const QString title = row.value(QStringLiteral("Title")).toString();
        if (!title.isEmpty())
            return title;
        return QFileInfo(row.value(QStringLiteral("FileName")).toString()).fileName();
    }
    case AuthorRole:
        return row.value(QStringLiteral("Author")).toString();
    case DescriptionRole:
        return row.value(QStringLiteral("Description")).toString();
    case MapsRole:
        return row.value(QStringLiteral("Map")).toString();
    case RatingRole:
        return row.value(QStringLiteral("Rating"));
    case LastPlayedRole: {
        const QString value = row.value(QStringLiteral("LastPlayed")).toString();
        return value.left(10);
    }
    case MinutesPlayedRole:
        return row.value(QStringLiteral("MinutesPlayed"), 0).toInt();
    case ImagePathRole:
        return imageForGameFile(row);
    case IsIwadRole:
        return m_iwadIds.contains(row.value(QStringLiteral("GameFileID")).toInt());
    case CommentsRole:
        return row.value(QStringLiteral("Comments")).toString();
    case ReleaseDateRole:
        return row.value(QStringLiteral("ReleaseDate")).toString().left(10);
    default:
        return {};
    }
}

QString LibraryModel::imageForGameFile(const QVariantMap &row) const
{
    const int id = row.value(QStringLiteral("GameFileID"), -1).toInt();
    if (id < 0)
        return {};

    // Prefer the main screenshot, then the title pic, matching the tiles
    // of the previous frontend.
    for (int fileType : {1 /*Screenshot*/, 6 /*TitlePic*/, 4 /*Thumbnail*/}) {
        const QVariantList files = m_db->files(id, fileType);
        for (const QVariant &fileVariant : files) {
            const QVariantMap file = fileVariant.toMap();
            const QString name = file.value(QStringLiteral("FileName")).toString();
            QString base;
            switch (fileType) {
            case 1: base = LauncherPaths::screenshotsDir(); break;
            case 6: base = LauncherPaths::titlePicsDir(); break;
            default: base = LauncherPaths::thumbnailsDir(); break;
            }
            const QString path = QDir::isAbsolutePath(name) ? name : base + QLatin1Char('/') + name;
            if (QFileInfo::exists(path))
                return QStringLiteral("file://") + path;
        }
    }
    return {};
}

void LibraryModel::load(int tabKind, int tagId, const QString &searchText)
{
    m_lastTabKind = tabKind;
    m_lastTagId = tagId;
    m_lastSearch = searchText;

    beginResetModel();
    m_rows.clear();
    m_iwadIds = m_db->iwadGameFileIds();

    QVariantList rows;
    const QString search = searchText.trimmed();
    QString where;
    QVariantList binds;
    if (!search.isEmpty()) {
        where = QStringLiteral(
            "(Title like ? or Author like ? or FileName like ? or Description like ?)");
        const QString pattern = QLatin1Char('%') + search + QLatin1Char('%');
        binds = {pattern, pattern, pattern, pattern};
    }

    switch (tabKind) {
    case Local:
    case Recent:
    case Untagged: {
        rows = m_db->gameFiles(where, binds);
        // Local-style tabs hide IWADs; Untagged additionally hides tagged
        // files; Recent keeps the 25 most recently added.
        QList<int> untagged;
        if (tabKind == Untagged)
            untagged = m_db->untaggedGameFileIds();
        QVariantList filtered;
        for (const QVariant &rowVariant : rows) {
            const int id = rowVariant.toMap().value(QStringLiteral("GameFileID")).toInt();
            if (m_iwadIds.contains(id))
                continue;
            if (tabKind == Untagged && !untagged.contains(id))
                continue;
            filtered.append(rowVariant);
        }
        rows = filtered;
        if (tabKind == Recent) {
            std::stable_sort(rows.begin(), rows.end(), [](const QVariant &a, const QVariant &b) {
                return a.toMap().value(QStringLiteral("Downloaded")).toString()
                     > b.toMap().value(QStringLiteral("Downloaded")).toString();
            });
            while (rows.size() > 25)
                rows.removeLast();
        }
        break;
    }
    case IWads: {
        rows.clear();
        const QVariantList all = m_db->gameFiles(where, binds);
        for (const QVariant &rowVariant : all) {
            if (m_iwadIds.contains(rowVariant.toMap().value(QStringLiteral("GameFileID")).toInt()))
                rows.append(rowVariant);
        }
        break;
    }
    case Tag: {
        const QList<int> ids = m_db->gameFileIdsForTag(tagId);
        const QVariantList all = m_db->gameFiles(where, binds);
        for (const QVariant &rowVariant : all) {
            if (ids.contains(rowVariant.toMap().value(QStringLiteral("GameFileID")).toInt()))
                rows.append(rowVariant);
        }
        break;
    }
    case IdGames:
        // Filled asynchronously through setExternalRows().
        break;
    }

    m_rows = rows;
    applySort();
    endResetModel();
    Q_EMIT countChanged();
}

void LibraryModel::setExternalRows(const QVariantList &rows)
{
    beginResetModel();
    m_rows = rows;
    endResetModel();
    Q_EMIT countChanged();
}

void LibraryModel::applySort()
{
    const QString field = m_sortField;
    const bool descending = m_sortDescending;
    std::stable_sort(m_rows.begin(), m_rows.end(), [&](const QVariant &a, const QVariant &b) {
        const QVariantMap ma = a.toMap();
        const QVariantMap mb = b.toMap();
        QVariant va = ma.value(field);
        QVariant vb = mb.value(field);
        if (field == QStringLiteral("Title")) {
            // Fall back to the file name so untitled entries still sort sanely.
            if (va.toString().isEmpty())
                va = QFileInfo(ma.value(QStringLiteral("FileName")).toString()).fileName();
            if (vb.toString().isEmpty())
                vb = QFileInfo(mb.value(QStringLiteral("FileName")).toString()).fileName();
        }
        int compared;
        if (va.typeId() == QMetaType::Int || va.typeId() == QMetaType::Double
            || vb.typeId() == QMetaType::Int || vb.typeId() == QMetaType::Double)
            compared = va.toDouble() < vb.toDouble() ? -1 : (va.toDouble() > vb.toDouble() ? 1 : 0);
        else
            compared = QString::compare(va.toString(), vb.toString(), Qt::CaseInsensitive);
        return descending ? compared > 0 : compared < 0;
    });
}

void LibraryModel::setSortField(const QString &field)
{
    if (m_sortField == field)
        return;
    m_sortField = field;
    beginResetModel();
    applySort();
    endResetModel();
    Q_EMIT sortChanged();
}

void LibraryModel::setSortDescending(bool descending)
{
    if (m_sortDescending == descending)
        return;
    m_sortDescending = descending;
    beginResetModel();
    applySort();
    endResetModel();
    Q_EMIT sortChanged();
}

QVariantMap LibraryModel::get(int row) const
{
    if (row < 0 || row >= m_rows.size())
        return {};
    QVariantMap map = m_rows.at(row).toMap();
    // Convenience values for QML.
    const QModelIndex index = this->index(row);
    map.insert(QStringLiteral("displayTitle"), data(index, TitleRole));
    map.insert(QStringLiteral("imagePath"), data(index, ImagePathRole));
    map.insert(QStringLiteral("isIwad"), data(index, IsIwadRole));
    return map;
}

int LibraryModel::rowForGameFileId(int gameFileId) const
{
    for (int i = 0; i < m_rows.size(); ++i) {
        if (m_rows.at(i).toMap().value(QStringLiteral("GameFileID")).toInt() == gameFileId)
            return i;
    }
    return -1;
}
