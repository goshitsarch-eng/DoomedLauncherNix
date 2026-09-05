#include "sourceportdetector.h"
#include "hostprocess.h"

#include <QDir>
#include <QFileInfo>
#include <QProcess>
#include <QStandardPaths>

namespace
{
struct KnownBinary {
    const char *binary;
    const char *name;
};

const KnownBinary knownBinaries[] = {
    {"gzdoom", "GZDoom"},
    {"uzdoom", "UZDoom"},
    {"vkdoom", "VKDoom"},
    {"lzdoom", "LZDoom"},
    {"zdoom", "ZDoom"},
    {"zandronum", "Zandronum"},
    {"crispy-doom", "Crispy Doom"},
    {"chocolate-doom", "Chocolate Doom"},
    {"prboom-plus", "PrBoom+"},
    {"dsda-doom", "DSDA-Doom"},
    {"woof", "Woof!"},
    {"helion", "Helion"},
    {"nyan-doom", "Nyan Doom"},
    {"nugget-doom", "Nugget Doom"},
    {"doomretro", "DOOM Retro"},
    {"eternity", "Eternity Engine"},
    {"odamex", "Odamex"},
    {"doomsday", "Doomsday"},
};

const KnownBinary knownFlatpaks[] = {
    {"org.zdoom.GZDoom", "GZDoom (Flatpak)"},
    {"org.zdoom.UZDoom", "UZDoom (Flatpak)"},
    {"org.zdoom.VKDoom", "VKDoom (Flatpak)"},
};

QString runAndCapture(const QString &program, const QStringList &arguments)
{
    QByteArray output;
    if (!HostProcess::capture(program, arguments, &output))
        return {};
    return QString::fromUtf8(output);
}

QStringList extraSearchDirectories()
{
    const QString home = HostProcess::homePath();
    return {
        QStringLiteral("/usr/games"),
        QStringLiteral("/usr/local/games"),
        home + QStringLiteral("/.local/bin"),
        home + QStringLiteral("/bin"),
        // AppImage builds (UZDoom and others ship these) never land on PATH.
        home + QStringLiteral("/Applications"),
        home + QStringLiteral("/AppImages"),
        home + QStringLiteral("/Downloads"),
    };
}

QString normalized(const QString &value)
{
    QString result;
    for (const QChar c : value) {
        if (c.isLetterOrNumber())
            result.append(c.toLower());
    }
    return result;
}

QString findAppImage(const QString &name)
{
    const QString key = normalized(name);
    if (key.isEmpty())
        return {};
    for (const QString &dir : extraSearchDirectories()) {
        const QFileInfoList files = QDir(dir).entryInfoList({QStringLiteral("*.AppImage")}, QDir::Files);
        for (const QFileInfo &info : files) {
            if (normalized(info.completeBaseName()).startsWith(key))
                return info.absoluteFilePath();
        }
    }
    return {};
}
}

namespace SourcePortDetector
{
QString findExecutable(const QString &name)
{
    if (HostProcess::isSandboxed()) {
        if (QDir::isAbsolutePath(name))
            return HostProcess::isExecutable(name) ? name : QString();
        if (name.isEmpty() || name.contains(QLatin1Char('/')))
            return {};
        const QString hostPath = runAndCapture(QStringLiteral("/usr/bin/printenv"),
                                               {QStringLiteral("PATH")}).trimmed();
        const QStringList files = HostProcess::executableFiles(
            hostPath.split(QLatin1Char(':'), Qt::SkipEmptyParts) + extraSearchDirectories());
        for (const QString &file : files) {
            if (QFileInfo(file).fileName() == name)
                return file;
        }
        for (const QString &file : files) {
            if (QFileInfo(file).fileName().compare(name, Qt::CaseInsensitive) == 0)
                return file;
        }
        for (const QString &file : files) {
            const QFileInfo info(file);
            if (info.suffix() == QStringLiteral("AppImage")
                && normalized(info.completeBaseName()).startsWith(normalized(name)))
                return file;
        }
        return {};
    }
    QString path = QStandardPaths::findExecutable(name);
    if (!path.isEmpty())
        return path;
    path = QStandardPaths::findExecutable(name, extraSearchDirectories());
    if (!path.isEmpty())
        return path;

    // Tarball builds keep upstream capitalisation (UZDoom); PATH lookups
    // are case sensitive, so scan directories ignoring case.
    const QStringList pathDirs =
        qEnvironmentVariable("PATH").split(QLatin1Char(':'), Qt::SkipEmptyParts) + extraSearchDirectories();
    for (const QString &dir : pathDirs) {
        const QFileInfoList files = QDir(dir).entryInfoList(QDir::Files | QDir::Executable);
        for (const QFileInfo &info : files) {
            if (info.fileName().compare(name, Qt::CaseInsensitive) == 0)
                return info.absoluteFilePath();
        }
    }

    return findAppImage(name);
}

QList<DetectedPort> detect()
{
    QList<DetectedPort> results;
    QStringList seen;

    auto add = [&](const DetectedPort &port) {
        if (port.executable.isEmpty() || seen.contains(port.executable, Qt::CaseInsensitive))
            return;
        seen.append(port.executable);
        results.append(port);
    };

    for (const KnownBinary &known : knownBinaries) {
        const QString path = findExecutable(QString::fromLatin1(known.binary));
        if (path.isEmpty())
            continue;
        DetectedPort port;
        port.name = QString::fromLatin1(known.name);
        // Record the on-disk name: a port shipped as "UZDoom" must be
        // launched as "UZDoom" on a case sensitive filesystem.
        port.executable = QFileInfo(path).fileName();
        port.directory = QFileInfo(path).absolutePath();
        port.preferred = qstrcmp(known.binary, "gzdoom") == 0;
        add(port);
    }

    const QString flatpakList =
        runAndCapture(QStringLiteral("flatpak"), {QStringLiteral("list"), QStringLiteral("--app"),
                                                  QStringLiteral("--columns=application")});
    for (const KnownBinary &known : knownFlatpaks) {
        const QString appId = QString::fromLatin1(known.binary);
        if (!flatpakList.split(QLatin1Char('\n'), Qt::SkipEmptyParts).contains(appId))
            continue;
        DetectedPort port;
        port.name = QString::fromLatin1(known.name);
        port.executable = QStringLiteral("flatpak:") + appId;
        port.preferred = appId.compare(QStringLiteral("org.zdoom.GZDoom"), Qt::CaseInsensitive) == 0;
        add(port);
    }

    const QString snapList = runAndCapture(QStringLiteral("snap"), {QStringLiteral("list")});
    for (const QString &line : snapList.split(QLatin1Char('\n'), Qt::SkipEmptyParts)) {
        if (!line.startsWith(QStringLiteral("gzdoom"), Qt::CaseInsensitive))
            continue;
        DetectedPort port;
        port.name = QStringLiteral("GZDoom (Snap)");
        port.executable = QStringLiteral("snap:gzdoom");
        add(port);
        break;
    }

    std::stable_sort(results.begin(), results.end(), [](const DetectedPort &a, const DetectedPort &b) {
        if (a.preferred != b.preferred)
            return a.preferred;
        return a.name.compare(b.name, Qt::CaseInsensitive) < 0;
    });
    return results;
}
}
