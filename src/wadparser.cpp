#include "wadparser.h"

#include <QFile>
#include <QFileInfo>
#include <QRegularExpression>
#include <QtEndian>

namespace
{
struct DirectoryEntry {
    qint32 offset;
    qint32 size;
    QString name;
};

QList<DirectoryEntry> readDirectory(const QString &filePath, QByteArray *magicOut = nullptr)
{
    QList<DirectoryEntry> entries;
    QFile file(filePath);
    if (!file.open(QIODevice::ReadOnly))
        return entries;

    const QByteArray header = file.read(12);
    if (header.size() < 12)
        return entries;
    const QByteArray magic = header.left(4);
    if (magicOut)
        *magicOut = magic;
    if (magic != "IWAD" && magic != "PWAD")
        return entries;

    const qint32 count = qFromLittleEndian<qint32>(header.constData() + 4);
    const qint32 dirOffset = qFromLittleEndian<qint32>(header.constData() + 8);
    if (count <= 0 || count > 65536 || !file.seek(dirOffset))
        return entries;

    const QByteArray dir = file.read(qint64(count) * 16);
    for (qint64 i = 0; i + 16 <= dir.size(); i += 16) {
        DirectoryEntry entry;
        entry.offset = qFromLittleEndian<qint32>(dir.constData() + i);
        entry.size = qFromLittleEndian<qint32>(dir.constData() + i + 4);
        QByteArray name = dir.mid(i + 8, 8);
        const int nul = name.indexOf('\0');
        if (nul >= 0)
            name.truncate(nul);
        entry.name = QString::fromLatin1(name).toUpper();
        entries.append(entry);
    }
    return entries;
}

bool isMapContentLump(const QString &name)
{
    static const QStringList lumps = {
        QStringLiteral("THINGS"),   QStringLiteral("LINEDEFS"), QStringLiteral("SIDEDEFS"),
        QStringLiteral("VERTEXES"), QStringLiteral("SEGS"),     QStringLiteral("SSECTORS"),
        QStringLiteral("NODES"),    QStringLiteral("SECTORS"),  QStringLiteral("REJECT"),
        QStringLiteral("BLOCKMAP"), QStringLiteral("BEHAVIOR"), QStringLiteral("TEXTMAP"),
    };
    return lumps.contains(name);
}
}

namespace WadParser
{
bool isIwad(const QString &filePath)
{
    QFile file(filePath);
    if (!file.open(QIODevice::ReadOnly))
        return false;
    return file.read(4) == "IWAD";
}

bool isWad(const QString &filePath)
{
    QFile file(filePath);
    if (!file.open(QIODevice::ReadOnly))
        return false;
    const QByteArray magic = file.read(4);
    return magic == "IWAD" || magic == "PWAD";
}

QStringList mapNames(const QString &filePath)
{
    QStringList maps;
    const QList<DirectoryEntry> entries = readDirectory(filePath);

    // A map marker is a zero-size-ish lump immediately followed by map
    // content lumps (THINGS/LINEDEFS for classic maps, TEXTMAP for UDMF).
    for (int i = 0; i < entries.size() - 1; ++i) {
        if (!isMapContentLump(entries[i + 1].name))
            continue;
        const QString &name = entries[i].name;
        if (name.isEmpty() || isMapContentLump(name))
            continue;
        if (!maps.contains(name))
            maps.append(name);
    }
    return maps;
}

QStringList lumpNames(const QString &filePath)
{
    QStringList names;
    const QList<DirectoryEntry> entries = readDirectory(filePath);
    names.reserve(entries.size());
    for (const DirectoryEntry &entry : entries)
        names.append(entry.name);
    return names;
}

QByteArray lumpData(const QString &filePath, const QString &lumpName)
{
    const QList<DirectoryEntry> entries = readDirectory(filePath);
    for (const DirectoryEntry &entry : entries) {
        if (entry.name.compare(lumpName, Qt::CaseInsensitive) != 0)
            continue;
        if (entry.size <= 0 || entry.offset < 0)
            return {};
        QFile file(filePath);
        if (!file.open(QIODevice::ReadOnly) || !file.seek(entry.offset))
            return {};
        return file.read(entry.size);
    }
    return {};
}

QString iwadDisplayName(const QString &fileName)
{
    const QString base = QFileInfo(fileName).completeBaseName().toLower();
    static const QHash<QString, QString> known = {
        {QStringLiteral("doom"), QStringLiteral("The Ultimate Doom")},
        {QStringLiteral("doom1"), QStringLiteral("Doom (Shareware)")},
        {QStringLiteral("doom2"), QStringLiteral("Doom II: Hell on Earth")},
        {QStringLiteral("tnt"), QStringLiteral("Final Doom: TNT Evilution")},
        {QStringLiteral("plutonia"), QStringLiteral("Final Doom: The Plutonia Experiment")},
        {QStringLiteral("heretic"), QStringLiteral("Heretic")},
        {QStringLiteral("hexen"), QStringLiteral("Hexen")},
        {QStringLiteral("hexdd"), QStringLiteral("Hexen: Deathkings of the Dark Citadel")},
        {QStringLiteral("strife1"), QStringLiteral("Strife")},
        {QStringLiteral("chex"), QStringLiteral("Chex Quest")},
        {QStringLiteral("freedoom1"), QStringLiteral("Freedoom: Phase 1")},
        {QStringLiteral("freedoom2"), QStringLiteral("Freedoom: Phase 2")},
        {QStringLiteral("doom64"), QStringLiteral("Doom 64")},
    };
    const auto it = known.constFind(base);
    if (it != known.constEnd())
        return it.value();
    return QFileInfo(fileName).completeBaseName();
}
}
