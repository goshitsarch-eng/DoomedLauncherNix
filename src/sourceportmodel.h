#pragma once

#include <QAbstractListModel>
#include <QVariantList>

class Database;

// Source ports (and utilities) list with CRUD used by the source ports
// page and the play dialog combo box.
class SourcePortModel : public QAbstractListModel
{
    Q_OBJECT
    Q_PROPERTY(int count READ rowCount NOTIFY countChanged)
    // 0 = SourcePort, 1 = Utility, 2 = Doom64 (matches SourcePortLaunchType).
    Q_PROPERTY(int launchType READ launchType WRITE setLaunchType NOTIFY launchTypeChanged)

public:
    enum Roles {
        IdRole = Qt::UserRole + 1,
        NameRole,
        ExecutableRole,
        DirectoryRole,
        ExtensionsRole,
        FileOptionRole,
        ExtraParametersRole,
        AltSaveDirectoryRole,
    };

    explicit SourcePortModel(Database *db, QObject *parent = nullptr);

    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role) const override;
    QHash<int, QByteArray> roleNames() const override;

    int launchType() const { return m_launchType; }
    void setLaunchType(int type);

    Q_INVOKABLE void reload();
    Q_INVOKABLE QVariantMap get(int row) const;
    Q_INVOKABLE int rowForId(int sourcePortId) const;
    Q_INVOKABLE int idForRow(int row) const;
    Q_INVOKABLE void save(int sourcePortId, const QVariantMap &fields);
    Q_INVOKABLE void remove(int sourcePortId);

signals:
    void countChanged();
    void launchTypeChanged();

private:
    Database *m_db;
    QVariantList m_rows;
    int m_launchType = 0;
};
