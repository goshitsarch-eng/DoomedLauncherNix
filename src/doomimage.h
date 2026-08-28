#pragma once

#include <QByteArray>
#include <QImage>

// Decodes Doom graphics lumps (TITLEPIC and friends) to QImage: embedded
// PNG/JPG/BMP lumps, raw flats (64x64 … 560x200) and the classic Doom
// column/post picture format with tall-patch support. Ported from the old
// WadReader project.
namespace DoomImage
{
// A palette is the first 768-byte layer of a PLAYPAL-style lump.
enum class Game { Doom, Heretic, Hexen };

// Built-in palette for wads that ship none themselves.
QByteArray defaultPalette(Game game);

// True when the data looks like a raw flat of a known dimension.
bool likelyFlat(const QByteArray &data);

// Decodes any supported lump. palette must hold at least 768 bytes for
// paletted formats; PNG/JPG/BMP lumps ignore it. Returns a null image on
// failure.
QImage decode(const QByteArray &data, const QByteArray &palette);
}
