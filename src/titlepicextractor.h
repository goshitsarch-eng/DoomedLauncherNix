#pragma once

#include <QImage>
#include <QString>
#include <QStringList>

// Pulls the title screen out of a game file so the library can show it as
// the entry's image. Looks for TITLEPIC (or the lump a MAPINFO's
// titlepage= names), falling back to the Heretic/Hexen TITLE lump, using
// the wad's own PLAYPAL when present and the built-in palettes otherwise.
namespace TitlePicExtractor
{
// wadPaths: extracted/plain wad files belonging to the game file.
// pk3Paths: pk3/ipk3 archives belonging to it (searched for graphic files).
// hintName: file name of the game file, used to pick the Hexen palette.
QImage extract(const QStringList &wadPaths, const QStringList &pk3Paths, const QString &hintName);
}
