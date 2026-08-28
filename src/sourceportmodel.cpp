#include "sourceportmodel.h"

#include "database.h"

SourcePortModel::SourcePortModel(Database *db, QObject *parent)
    : QAbstractListModel(parent)
    , m_db(db)
{
    reload();
}

int SourcePortModel::rowCount(const QModelIndex &parent) const
{
    return parent.isValid() ? 0 : m_rows.size();
}

QHash<int, QByteArray> SourcePortModel::roleNames() const
{
    return {
        {IdRole, "sourcePortId"},
        {NameRole, "name"},
        {ExecutableRole, "executable"},
        {DirectoryRole, "directory"},
        {ExtensionsRole, "supportedExtensions"},
        {FileOptionRole, "fileOption"},
        {ExtraParametersRole, "extraParameters"},
        {AltSaveDirectoryRole, "altSaveDirectory"},
    };
}

QVariant SourcePortModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() >= m_rows.size())
        return {};
    const QVariantMap row = m_rows.at(index.row()).toMap();
    switch (role) {
    case IdRole:
        return row.value(QStringLiteral("SourcePortID"));
    case NameRole:
        return row.value(QStringLiteral("Name"));
    case ExecutableRole:
        return row.value(QStringLiteral("Executable"));
    case DirectoryRole:
        return row.value(QStringLiteral("Directory"));
    case ExtensionsRole:
        return row.value(QStringLiteral("SupportedExtensions"));
    case FileOptionRole:
        return row.value(QStringLiteral("FileOption"));
    case ExtraParametersRole:
        return row.value(QStringLiteral("ExtraParameters"));
    case AltSaveDirectoryRole:
        return row.value(QStringLiteral("AltSaveDirectory"));
    default:
        return {};
    }
}

void SourcePortModel::setLaunchType(int type)
{
    if (m_launchType == type)
        return;
    m_launchType = type;
    reload();
    Q_EMIT launchTypeChanged();
}

void SourcePortModel::reload()
{
    beginResetModel();
    m_rows = m_db->sourcePorts(m_launchType);
    endResetModel();
    Q_EMIT countChanged();
}

QVariantMap SourcePortModel::get(int row) const
{
    if (row < 0 || row >= m_rows.size())
        return {};
    return m_rows.at(row).toMap();
}

int SourcePortModel::rowForId(int sourcePortId) const
{
    for (int i = 0; i < m_rows.size(); ++i) {
        if (m_rows.at(i).toMap().value(QStringLiteral("SourcePortID")).toInt() == sourcePortId)
            return i;
    }
    return -1;
}

int SourcePortModel::idForRow(int row) const
{
    if (row < 0 || row >= m_rows.size())
        return -1;
    return m_rows.at(row).toMap().value(QStringLiteral("SourcePortID")).toInt();
}

void SourcePortModel::save(int sourcePortId, const QVariantMap &fields)
{
    QVariantMap dbFields;
    dbFields.insert(QStringLiteral("Name"), fields.value(QStringLiteral("name")));
    dbFields.insert(QStringLiteral("Executable"), fields.value(QStringLiteral("executable")));
    dbFields.insert(QStringLiteral("Directory"), fields.value(QStringLiteral("directory")));
    dbFields.insert(QStringLiteral("SupportedExtensions"),
                    fields.value(QStringLiteral("supportedExtensions"),
                                 QStringLiteral(".wad,.deh,.bex,.pk3,.pk7,.ipk3,.zip")));
    dbFields.insert(QStringLiteral("FileOption"),
                    fields.value(QStringLiteral("fileOption"), QStringLiteral("-file")));
    dbFields.insert(QStringLiteral("ExtraParameters"),
                    fields.value(QStringLiteral("extraParameters"), QString()));
    dbFields.insert(QStringLiteral("AltSaveDirectory"),
                    fields.value(QStringLiteral("altSaveDirectory"), QString()));

    if (sourcePortId > 0) {
        m_db->updateSourcePort(sourcePortId, dbFields);
    } else {
        dbFields.insert(QStringLiteral("LaunchType"), m_launchType);
        m_db->insertSourcePort(dbFields);
    }
    reload();
}

void SourcePortModel::remove(int sourcePortId)
{
    m_db->deleteSourcePort(sourcePortId);
    reload();
}
