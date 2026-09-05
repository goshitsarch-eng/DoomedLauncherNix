#pragma once

#include <QProcess>
#include <QStringList>

// All source-port commands cross the same boundary. No shell parsing occurs.
namespace HostProcess
{
bool isSandboxed();
void configure(QProcess &process, const QString &program, const QStringList &arguments,
               const QString &workingDirectory = {});
bool capture(const QString &program, const QStringList &arguments, QByteArray *output,
             int timeoutMs = 10000);
bool isExecutable(const QString &path);
QString homePath();
QStringList executableFiles(const QStringList &directories);
}
