#pragma once

#include <QString>
#include <QStringList>

// Minimal WAD directory reader: enough to list the maps a WAD contains
// and to recognise IWADs. Ported from the old WadReader project.
namespace WadParser
{
// True when the file starts with the IWAD magic.
bool isIwad(const QString &filePath);

// True for IWAD or PWAD magic.
bool isWad(const QString &filePath);

// Map lump names (E1M1, MAP01, ...) in file order, without duplicates.
QStringList mapNames(const QString &filePath);

// All lump names in the WAD, upper-cased, in file order.
QStringList lumpNames(const QString &filePath);

// Contents of the first lump with the given (case-insensitive) name, or
// an empty array when absent.
QByteArray lumpData(const QString &filePath, const QString &lumpName);

// Friendly name for a known IWAD file name (doom2.wad -> "Doom II"), or
// the base file name when unknown.
QString iwadDisplayName(const QString &fileName);
}
