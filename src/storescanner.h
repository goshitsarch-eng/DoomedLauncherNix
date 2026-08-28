#pragma once

#include <QString>
#include <QStringList>

// Finds Doom-engine games installed through Steam, GOG (Heroic) or
// Lutris and returns the IWADs, expansion PWADs and the Doom 64
// re-release executable they ship. Blocking; run on a worker thread.
namespace StoreScanner
{
struct Result {
    QStringList iwads;      // absolute paths to installed IWAD files
    QStringList pwads;      // expansion wads (NERVE, SIGIL, Master Levels, ...)
    QString doom64Exe;      // Doom 64 re-release binary, when installed
};

Result scan();

// Exposed for testing: parse "path" entries out of libraryfolders.vdf and
// "installdir" out of an appmanifest .acf.
QStringList parseLibraryPaths(const QString &vdfText);
QString parseInstallDir(const QString &acfText);
}
