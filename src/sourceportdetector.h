#pragma once

#include <QString>
#include <QVariantList>

// Finds installed Doom source ports: native binaries on PATH (and the
// usual game directories), Flatpak apps and snaps. Flatpak ports are
// stored as "flatpak:org.zdoom.GZDoom", snaps as "snap:gzdoom" — the same
// convention the previous releases used, so existing rows keep launching.
namespace SourcePortDetector
{
struct DetectedPort {
    QString name;       // display name, e.g. "GZDoom (Flatpak)"
    QString executable; // binary name, flatpak:<id> or snap:<name>
    QString directory;  // directory of a native binary, else empty
    bool preferred = false;
};

// Blocking; call from a worker thread — it shells out to flatpak/snap.
QList<DetectedPort> detect();

// Resolves a bare binary name against PATH plus the common game and
// AppImage directories. Returns an absolute path or an empty string.
QString findExecutable(const QString &name);
}
