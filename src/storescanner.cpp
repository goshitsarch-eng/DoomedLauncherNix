#include "storescanner.h"

#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QRegularExpression>

namespace
{
struct StoreGame {
    int steamId;
    const char *name; // GOG/Heroic/Lutris install folder name
    QStringList iwads;
    QStringList pwads;
    const char *doom64Exe;
};

// Same catalogue the previous releases shipped, in priority order.
// Paths are relative to the game folder; the Windows-style rerelease
// layout is used verbatim (with / separators).
const QList<StoreGame> &storeGames()
{
    static const QList<StoreGame> games = {
        {2280, "DOOM + DOOM II",
         {QStringLiteral("rerelease/doom.wad"), QStringLiteral("rerelease/doom2.wad"),
          QStringLiteral("rerelease/plutonia.wad"), QStringLiteral("rerelease/tnt.wad"),
          QStringLiteral("base/doom.wad"), QStringLiteral("base/doom2/doom2.wad"),
          QStringLiteral("base/plutonia/plutonia.wad"), QStringLiteral("base/tnt/tnt.wad")},
         {QStringLiteral("rerelease/id1.wad"), QStringLiteral("rerelease/nerve.wad"),
          QStringLiteral("rerelease/masterlevels.wad"), QStringLiteral("rerelease/sigil.wad"),
          QStringLiteral("rerelease/sigil2.wad")},
         nullptr},
        {2300, "DOOM II", {QStringLiteral("base/doom2.wad")}, {}, nullptr},
        {2290, "Final DOOM",
         {QStringLiteral("base/plutonia.wad"), QStringLiteral("base/tnt.wad")}, {}, nullptr},
        {3286930, "Heretic + Hexen",
         {QStringLiteral("heretic.wad"), QStringLiteral("hexen.wad")},
         {QStringLiteral("hexdd.wad")}, nullptr},
        {2390, "Heretic: Shadow of the Serpent Riders",
         {QStringLiteral("base/heretic.wad")}, {}, nullptr},
        {2360, "Hexen: Beyond Heretic", {QStringLiteral("base/hexen.wad")}, {}, nullptr},
        {317040, "Strife: Veteran Edition", {QStringLiteral("strife1.wad")}, {}, nullptr},
        {1148590, "Doom 64", {QStringLiteral("doom64.wad")}, {}, "doom64_x64.exe"},
    };
    return games;
}

QString steamRoot()
{
    QStringList candidates;
    const QString xdgData = qEnvironmentVariable("XDG_DATA_HOME");
    if (!xdgData.isEmpty())
        candidates << xdgData + QStringLiteral("/Steam");
    const QString home = QDir::homePath();
    candidates << home + QStringLiteral("/.steam/steam")
               << home + QStringLiteral("/.steam/root")
               << home + QStringLiteral("/.local/share/Steam")
               << home + QStringLiteral("/.var/app/com.valvesoftware.Steam/.local/share/Steam")
               << home + QStringLiteral("/snap/steam/common/.local/share/Steam");
    for (const QString &candidate : candidates) {
        if (QDir(candidate).exists())
            return candidate;
    }
    return {};
}

QString steamGameFolder(const QString &steamPath, int steamId)
{
    QFile vdf(steamPath + QStringLiteral("/config/libraryfolders.vdf"));
    if (!vdf.open(QIODevice::ReadOnly))
        return {};
    const QStringList libraries =
        StoreScanner::parseLibraryPaths(QString::fromUtf8(vdf.readAll()));

    for (const QString &library : libraries) {
        QFile acf(library + QStringLiteral("/steamapps/appmanifest_%1.acf").arg(steamId));
        if (!acf.open(QIODevice::ReadOnly))
            continue;
        const QString installDir =
            StoreScanner::parseInstallDir(QString::fromUtf8(acf.readAll()));
        if (installDir.isEmpty())
            continue;
        const QString gamePath =
            library + QStringLiteral("/steamapps/common/") + installDir;
        if (QDir(gamePath).exists())
            return gamePath;
    }
    return {};
}

QString gogGameFolder(const QString &gameName)
{
    const QString home = QDir::homePath();
    const QStringList candidates = {
        home + QStringLiteral("/GOG Games/") + gameName,
        home + QStringLiteral("/Games/Heroic/") + gameName,
        home + QStringLiteral("/Games/Heroic/Prefixes/") + gameName,
        home + QStringLiteral("/.local/share/lutris/runners/gog/") + gameName,
    };
    for (const QString &candidate : candidates) {
        if (QDir(candidate).exists())
            return candidate;
    }
    return {};
}

// Resolves a relative wad path inside gamePath, tolerating case
// differences (GOG installs sometimes ship DOOM2.WAD).
QString findGameFile(const QString &gamePath, const QString &relative)
{
    const QString direct = gamePath + QLatin1Char('/') + relative;
    if (QFileInfo::exists(direct))
        return direct;

    QDir dir(gamePath);
    const QStringList parts = relative.split(QLatin1Char('/'), Qt::SkipEmptyParts);
    for (int i = 0; i < parts.size(); ++i) {
        const bool last = i == parts.size() - 1;
        const QStringList entries =
            dir.entryList(last ? QDir::Files : QDir::Dirs | QDir::NoDotAndDotDot);
        QString matched;
        for (const QString &entry : entries) {
            if (entry.compare(parts.at(i), Qt::CaseInsensitive) == 0) {
                matched = entry;
                break;
            }
        }
        if (matched.isEmpty())
            return {};
        if (last)
            return dir.absoluteFilePath(matched);
        if (!dir.cd(matched))
            return {};
    }
    return {};
}
}

namespace StoreScanner
{
QStringList parseLibraryPaths(const QString &vdfText)
{
    QStringList paths;
    static const QRegularExpression pathRe(
        QStringLiteral("\"path\"\\s+\"([^\"]+)\""));
    auto it = pathRe.globalMatch(vdfText);
    while (it.hasNext()) {
        QString path = it.next().captured(1);
        path.replace(QStringLiteral("\\\\"), QStringLiteral("/"));
        if (!paths.contains(path))
            paths.append(path);
    }
    return paths;
}

QString parseInstallDir(const QString &acfText)
{
    static const QRegularExpression installDirRe(
        QStringLiteral("\"installdir\"\\s+\"([^\"]+)\""));
    const auto match = installDirRe.match(acfText);
    return match.hasMatch() ? match.captured(1) : QString();
}

Result scan()
{
    Result result;
    const QString steamPath = steamRoot();

    for (const StoreGame &game : storeGames()) {
        QString gamePath;
        if (!steamPath.isEmpty())
            gamePath = steamGameFolder(steamPath, game.steamId);
        if (gamePath.isEmpty())
            gamePath = gogGameFolder(QString::fromUtf8(game.name));
        if (gamePath.isEmpty())
            continue;

        for (const QString &iwad : game.iwads) {
            const QString path = findGameFile(gamePath, iwad);
            if (!path.isEmpty() && !result.iwads.contains(path))
                result.iwads.append(path);
        }
        for (const QString &pwad : game.pwads) {
            const QString path = findGameFile(gamePath, pwad);
            if (!path.isEmpty() && !result.pwads.contains(path))
                result.pwads.append(path);
        }
        if (game.doom64Exe && result.doom64Exe.isEmpty()) {
            QString exe = findGameFile(gamePath, QString::fromUtf8(game.doom64Exe));
            if (exe.isEmpty()) {
                // The Linux build drops the .exe suffix.
                exe = findGameFile(gamePath,
                                   QFileInfo(QString::fromUtf8(game.doom64Exe)).completeBaseName());
            }
            result.doom64Exe = exe;
        }
    }
    return result;
}
}
