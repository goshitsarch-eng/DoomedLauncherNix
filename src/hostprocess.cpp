#include "hostprocess.h"

#include <QDir>
#include <QFileInfo>

namespace HostProcess
{
bool isSandboxed()
{
    return QFileInfo::exists(QStringLiteral("/.flatpak-info"))
        || !qEnvironmentVariableIsEmpty("FLATPAK_ID");
}

void configure(QProcess &process, const QString &program, const QStringList &arguments,
               const QString &workingDirectory)
{
    if (isSandboxed()) {
        process.setProgram(QStringLiteral("flatpak-spawn"));
        QStringList hostArguments{QStringLiteral("--host"), QStringLiteral("--watch-bus")};
        if (!workingDirectory.isEmpty())
            hostArguments << QStringLiteral("--directory=") + workingDirectory;
        hostArguments << QStringLiteral("--") << program;
        hostArguments << arguments;
        process.setArguments(hostArguments);
        // A host /usr or /opt directory may not exist inside this sandbox.
        process.setWorkingDirectory({});
    } else {
        process.setProgram(program);
        process.setArguments(arguments);
        process.setWorkingDirectory(workingDirectory);
    }
}

bool capture(const QString &program, const QStringList &arguments, QByteArray *output, int timeoutMs)
{
    output->clear();
    QProcess process;
    configure(process, program, arguments);
    process.start();
    if (!process.waitForFinished(timeoutMs)) {
        // Closing the host bridge's bus connection also terminates its child.
        process.kill();
        process.waitForFinished();
        return false;
    }
    if (process.exitStatus() != QProcess::NormalExit || process.exitCode() != 0)
        return false;
    *output = process.readAllStandardOutput();
    return true;
}

bool isExecutable(const QString &path)
{
    if (!QDir::isAbsolutePath(path))
        return false;
    if (!isSandboxed()) {
        const QFileInfo info(path);
        return info.isFile() && info.isExecutable();
    }
    QByteArray output;
    return capture(QStringLiteral("/usr/bin/test"), {QStringLiteral("-f"), path}, &output)
        && capture(QStringLiteral("/usr/bin/test"), {QStringLiteral("-x"), path}, &output);
}

QString homePath()
{
    if (!isSandboxed())
        return QDir::homePath();
    QByteArray output;
    if (!capture(QStringLiteral("/usr/bin/printenv"), {QStringLiteral("HOME")}, &output))
        return {};
    if (output.endsWith('\n'))
        output.chop(1);
    const QString home = QString::fromUtf8(output);
    return QDir::isAbsolutePath(home) ? home : QString();
}

QStringList executableFiles(const QStringList &directories)
{
    QStringList files;
    for (const QString &directory : directories) {
        // Absolute roots cannot be interpreted as find expressions/options.
        if (!QDir::isAbsolutePath(directory))
            continue;
        QByteArray output;
        if (!capture(QStringLiteral("/usr/bin/find"),
                     {QStringLiteral("-L"), directory, QStringLiteral("-maxdepth"), QStringLiteral("1"),
                      QStringLiteral("-type"), QStringLiteral("f"), QStringLiteral("-executable"),
                      QStringLiteral("-print0")}, &output))
            continue;
        for (const QByteArray &file : output.split('\0')) {
            if (!file.isEmpty())
                files << QString::fromUtf8(file);
        }
    }
    return files;
}
}
