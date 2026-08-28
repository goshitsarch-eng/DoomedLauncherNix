#include "doomimage.h"

#include <QFile>
#include <QtEndian>

namespace
{
constexpr int paletteBytes = 256 * 3;

QRgb paletteColor(const QByteArray &palette, int index)
{
    const int offset = index * 3;
    if (offset + 2 >= palette.size())
        return qRgba(0, 0, 0, 0);
    return qRgb(quint8(palette.at(offset)), quint8(palette.at(offset + 1)),
                quint8(palette.at(offset + 2)));
}

bool flatDimensions(qsizetype length, int *width, int *height)
{
    switch (length) {
    case 64 * 64: *width = *height = 64; return true;
    case 128 * 128: *width = *height = 128; return true;
    case 256 * 256: *width = *height = 256; return true;
    case 320 * 200: *width = 320; *height = 200; return true;
    case 560 * 200: *width = 560; *height = 200; return true; // Heretic/Hexen wide TITLE
    default: return false;
    }
}

QImage decodeFlat(const QByteArray &data, const QByteArray &palette)
{
    int width = 0;
    int height = 0;
    if (!flatDimensions(data.size(), &width, &height))
        return {};

    QImage image(width, height, QImage::Format_ARGB32);
    for (int y = 0; y < height; ++y) {
        QRgb *line = reinterpret_cast<QRgb *>(image.scanLine(y));
        for (int x = 0; x < width; ++x)
            line[x] = paletteColor(palette, quint8(data.at(y * width + x)));
    }
    return image;
}

QImage decodeColumn(const QByteArray &data, const QByteArray &palette)
{
    if (data.size() < 16)
        return {};
    const char *raw = data.constData();
    const int width = qFromLittleEndian<qint16>(raw);
    const int height = qFromLittleEndian<qint16>(raw + 2);
    if (width <= 0 || width >= 4096 || height <= 0 || height >= 4096)
        return {};
    if (data.size() < 8 + width * 4)
        return {};

    QImage image(width, height, QImage::Format_ARGB32);
    image.fill(Qt::transparent);

    for (int col = 0; col < width; ++col) {
        qint64 pos = qFromLittleEndian<qint32>(raw + 8 + col * 4);
        if (pos < 0 || pos >= data.size())
            return {};

        // Walk the posts of this column; rowStart 0xFF terminates it. Tall
        // patches encode rows past 254 by making rowStart relative to the
        // previous offset.
        int offset = 0;
        while (pos < data.size()) {
            const int rowStart = quint8(data.at(pos++));
            if (rowStart == 0xFF)
                break;
            if (pos + 1 >= data.size())
                return {};
            const int count = quint8(data.at(pos++));
            ++pos; // dummy byte
            if (pos + count + 1 > data.size())
                return {};

            if (rowStart <= offset)
                offset += rowStart;
            else
                offset = rowStart;

            for (int i = 0; i < count; ++i) {
                const int y = offset + i;
                if (y >= 0 && y < height)
                    reinterpret_cast<QRgb *>(image.scanLine(y))[col] =
                        paletteColor(palette, quint8(data.at(pos)));
                ++pos;
            }
            ++pos; // trailing dummy byte
        }
    }
    return image;
}

bool isPng(const QByteArray &data)
{
    return data.size() > 8 && quint8(data.at(0)) == 137 && data.at(1) == 'P' && data.at(2) == 'N'
        && data.at(3) == 'G';
}

bool isJpg(const QByteArray &data)
{
    return data.size() > 10 && quint8(data.at(0)) == 0xFF && quint8(data.at(1)) == 0xD8;
}

bool isBmp(const QByteArray &data)
{
    return data.size() > 14 && data.at(0) == 'B' && data.at(1) == 'M';
}
}

namespace DoomImage
{
QByteArray defaultPalette(Game game)
{
    QString path;
    switch (game) {
    case Game::Heretic: path = QStringLiteral(":/palettes/HereticPalette.pal"); break;
    case Game::Hexen: path = QStringLiteral(":/palettes/HexenPalette.pal"); break;
    default: path = QStringLiteral(":/palettes/PLAYPAL.LMP"); break;
    }
    QFile file(path);
    if (!file.open(QIODevice::ReadOnly))
        return {};
    return file.read(paletteBytes);
}

bool likelyFlat(const QByteArray &data)
{
    int width = 0;
    int height = 0;
    return flatDimensions(data.size(), &width, &height);
}

QImage decode(const QByteArray &data, const QByteArray &palette)
{
    if (data.isEmpty())
        return {};
    if (isPng(data) || isJpg(data) || isBmp(data))
        return QImage::fromData(data);
    if (palette.size() < paletteBytes)
        return {};
    if (likelyFlat(data))
        return decodeFlat(data, palette);
    return decodeColumn(data, palette);
}
}
