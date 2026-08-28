#pragma once

#include <QString>

// Filesystem layout of the launcher's data. The same directories the
// previous frontend used are kept so an existing library keeps working:
// $XDG_DATA_HOME/doomlauncher/DoomLauncher.sqlite with GameFiles/,
// GameFiles/Screenshots, GameFiles/Temp, GameFiles/Demos,
// GameFiles/SaveGames, GameFiles/Thumbnails and GameFiles/TitlePics.
namespace LauncherPaths
{
QString dataDir();
QString databaseFile();
QString gameFilesDir();
QString screenshotsDir();
QString tempDir();
QString demosDir();
QString saveGamesDir();
QString thumbnailsDir();
QString titlePicsDir();

// Creates every directory above that does not exist yet.
void ensureLayout();

// Resolves a stored file name that may be relative to the data directory
// (managed) or absolute (unmanaged).
QString resolve(const QString &storedPath);

// Returns storedPath relative to the data directory when it lives inside
// it, otherwise returns it unchanged.
QString toStored(const QString &absolutePath);
}
