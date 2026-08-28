#pragma once

#include <QAbstractListModel>
#include <QVariantList>

class Database;

// The game file list shown by the library page. One instance is reused
// for every tab; load() re-queries for the given tab/search/sort.
class LibraryModel : public QAbstractListModel
{
    Q_OBJECT
    Q_PROPERTY(int count READ rowCount NOTIFY countChanged)
    Q_PROPERTY(QString sortField READ sortField WRITE setSortField NOTIFY sortChanged)
    Q_PROPERTY(bool sortDescending READ sortDescending WRITE setSortDescending NOTIFY sortChanged)

public:
    enum Roles {
        GameFileIdRole = Qt::UserRole + 1,
        FileNameRole,
        TitleRole,
        AuthorRole,
        DescriptionRole,
        MapsRole,
        RatingRole,
        LastPlayedRole,
        MinutesPlayedRole,
        ImagePathRole,
        IsIwadRole,
        CommentsRole,
        ReleaseDateRole,
    };

    // Mirrors the previous frontend's tab kinds.
    enum TabKind { Recent, Local, Untagged, IWads, IdGames, Tag };
    Q_ENUM(TabKind)

    explicit LibraryModel(Database *db, QObject *parent = nullptr);

    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role) const override;
    QHash<int, QByteArray> roleNames() const override;

    Q_INVOKABLE void load(int tabKind, int tagId, const QString &searchText);
    // Replaces the contents with rows from the idgames API (already mapped).
    Q_INVOKABLE void setExternalRows(const QVariantList &rows);
    Q_INVOKABLE QVariantMap get(int row) const;
    Q_INVOKABLE int rowForGameFileId(int gameFileId) const;

    QString sortField() const { return m_sortField; }
    void setSortField(const QString &field);
    bool sortDescending() const { return m_sortDescending; }
    void setSortDescending(bool descending);

signals:
    void countChanged();
    void sortChanged();

private:
    void applySort();
    QString imageForGameFile(const QVariantMap &row) const;

    Database *m_db;
    QVariantList m_rows;
    QList<int> m_iwadIds;
    QString m_sortField = QStringLiteral("Title");
    bool m_sortDescending = false;
    int m_lastTabKind = Local;
    int m_lastTagId = -1;
    QString m_lastSearch;
};
