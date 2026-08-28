#include "titlepicextractor.h"

#include "doomimage.h"
#include "wadparser.h"

#include <KZip>
#include <KArchiveDirectory>
#include <KArchiveFile>

#include <QFileInfo>
#include <QRegularExpression>

#include <functional>

namespace
{
// MAPINFO/ZMAPINFO/UMAPINFO can point at a custom title screen.
QString titlepageFromMapInfo(const QString &wadPath)
{
    static const QRegularExpression titlePageRe(
        QStringLiteral("titlepage\\s*=?\\s*\"([^\"]+)\""), QRegularExpression::CaseInsensitiveOption);
    for (const QString &lump : {QStringLiteral("MAPINFO"), QStringLiteral("ZMAPINFO"),
                                QStringLiteral("UMAPINFO")}) {
        const QByteArray data = WadParser::lumpData(wadPath, lump);
        if (data.isEmpty())
            continue;
        const auto match = titlePageRe.match(QString::fromLatin1(data));
        if (match.hasMatch())
            return match.captured(1);
    }
    return {};
}

QImage extractFromWad(const QString &wadPath, const QString &hintName)
{
    QString picLump = titlepageFromMapInfo(wadPath);
    QByteArray palette = WadParser::lumpData(wadPath, QStringLiteral("PLAYPAL"));

    QByteArray data;
    if (!picLump.isEmpty())
        data = WadParser::lumpData(wadPath, picLump);
    if (data.isEmpty())
        data = WadParser::lumpData(wadPath, QStringLiteral("TITLEPIC"));
    if (data.isEmpty()) {
        // Heretic/Hexen title screen; pick the matching built-in palette
        // when the wad ships none.
        data = WadParser::lumpData(wadPath, QStringLiteral("TITLE"));
        if (!data.isEmpty() && palette.isEmpty()) {
            const bool hexen = hintName.contains(QStringLiteral("hexen"), Qt::CaseInsensitive)
                || QFileInfo(wadPath).fileName().contains(QStringLiteral("hexen"), Qt::CaseInsensitive);
            palette = DoomImage::defaultPalette(hexen ? DoomImage::Game::Hexen
                                                      : DoomImage::Game::Heretic);
        }
    }
    if (data.isEmpty())
        return {};

    if (palette.isEmpty())
        palette = DoomImage::defaultPalette(DoomImage::Game::Doom);
    return DoomImage::decode(data, palette);
}

// pk3s are plain zips; the title screen is a graphics file, usually under
// graphics/ and often already a PNG.
QImage extractFromPk3(const QString &pk3Path)
{
    KZip zip(pk3Path);
    if (!zip.open(QIODevice::ReadOnly))
        return {};

    std::function<const KArchiveFile *(const KArchiveDirectory *)> find =
        [&](const KArchiveDirectory *dir) -> const KArchiveFile * {
        const QStringList names = dir->entries();
        for (const QString &name : names) {
            const KArchiveEntry *entry = dir->entry(name);
            if (!entry)
                continue;
            if (entry->isDirectory()) {
                if (const KArchiveFile *found =
                        find(static_cast<const KArchiveDirectory *>(entry)))
                    return found;
            } else if (QFileInfo(name).completeBaseName()
                           .compare(QStringLiteral("titlepic"), Qt::CaseInsensitive) == 0
                       || QFileInfo(name).completeBaseName()
                              .compare(QStringLiteral("title"), Qt::CaseInsensitive) == 0) {
                return static_cast<const KArchiveFile *>(entry);
            }
        }
        return nullptr;
    };

    const KArchiveFile *file = find(zip.directory());
    QImage image;
    if (file)
        image = DoomImage::decode(file->data(), DoomImage::defaultPalette(DoomImage::Game::Doom));
    zip.close();
    return image;
}
}

namespace TitlePicExtractor
{
QImage extract(const QStringList &wadPaths, const QStringList &pk3Paths, const QString &hintName)
{
    for (const QString &wadPath : wadPaths) {
        const QImage image = extractFromWad(wadPath, hintName);
        if (!image.isNull())
            return image;
    }
    for (const QString &pk3Path : pk3Paths) {
        const QImage image = extractFromPk3(pk3Path);
        if (!image.isNull())
            return image;
    }
    return {};
}
}
