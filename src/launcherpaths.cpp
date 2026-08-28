#include "launcherpaths.h"

#include <QDir>
#include <QProcessEnvironment>
#include <QStandardPaths>

namespace
{
QString xdgDataHome()
{
    const QString xdg = qEnvironmentVariable("XDG_DATA_HOME");
    if (!xdg.isEmpty())
        return xdg;
    return QDir::homePath() + QStringLiteral("/.local/share");
}
}

namespace LauncherPaths
{
QString dataDir()
{
    return xdgDataHome() + QStringLiteral("/doomlauncher");
}

QString databaseFile()
{
    return dataDir() + QStringLiteral("/DoomLauncher.sqlite");
}

QString gameFilesDir()
{
    return dataDir() + QStringLiteral("/GameFiles");
}

QString screenshotsDir()
{
    return gameFilesDir() + QStringLiteral("/Screenshots");
}

QString tempDir()
{
    return gameFilesDir() + QStringLiteral("/Temp");
}

QString demosDir()
{
    return gameFilesDir() + QStringLiteral("/Demos");
}

QString saveGamesDir()
{
    return gameFilesDir() + QStringLiteral("/SaveGames");
}

QString thumbnailsDir()
{
    return gameFilesDir() + QStringLiteral("/Thumbnails");
}

QString titlePicsDir()
{
    return gameFilesDir() + QStringLiteral("/TitlePics");
}

void ensureLayout()
{
    for (const QString &dir : {dataDir(), gameFilesDir(), screenshotsDir(), tempDir(),
                               demosDir(), saveGamesDir(), thumbnailsDir(), titlePicsDir()})
        QDir().mkpath(dir);
}

QString resolve(const QString &storedPath)
{
    if (storedPath.isEmpty())
        return storedPath;
    if (QDir::isAbsolutePath(storedPath))
        return storedPath;
    return dataDir() + QLatin1Char('/') + storedPath;
}

QString toStored(const QString &absolutePath)
{
    const QString base = dataDir() + QLatin1Char('/');
    if (absolutePath.startsWith(base))
        return absolutePath.mid(base.length());
    return absolutePath;
}
}
