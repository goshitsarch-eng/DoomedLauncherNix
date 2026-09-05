// A strict, typed host boundary fixture. It never executes user arguments.
#include <QCoreApplication>
#include <QFile>
#include <QJsonArray>
#include <QJsonDocument>
#include <QThread>

int main(int argc, char **argv)
{
    QCoreApplication app(argc, argv);
    QStringList args = app.arguments().mid(1);
    QFile log(qEnvironmentVariable("HOST_TEST_LOG"));
    if (!log.open(QIODevice::WriteOnly | QIODevice::Append))
        return 90;
    log.write(QJsonDocument(QJsonArray::fromStringList(args)).toJson(QJsonDocument::Compact) + '\n');
    log.close();
    if (args.takeFirst() != QStringLiteral("--host") || args.takeFirst() != QStringLiteral("--watch-bus"))
        return 91;
    if (args.first().startsWith(QStringLiteral("--directory=")))
        args.takeFirst();
    if (args.takeFirst() != QStringLiteral("--") || args.isEmpty())
        return 92;
    const QString program = args.takeFirst();
    QFile out;
    if (!out.open(stdout, QIODevice::WriteOnly))
        return 93;
    if (program == QStringLiteral("/usr/bin/printenv")) {
        out.write(args == QStringList{QStringLiteral("PATH")} ? "/host/bin\n" : "/host/home\n");
    } else if (program == QStringLiteral("/usr/bin/find")) {
        if (args != QStringList{QStringLiteral("-L"), QStringLiteral("/host/bin"),
                               QStringLiteral("-maxdepth"), QStringLiteral("1"), QStringLiteral("-type"),
                               QStringLiteral("f"), QStringLiteral("-executable"), QStringLiteral("-print0")})
            return 1;
        out.write(QByteArrayLiteral("/host/bin/GZDoom\0/host/bin/UZDoom-test.AppImage\0"));
    } else if (program == QStringLiteral("/usr/bin/test")) {
        return args.size() == 2 && args.last().startsWith(QStringLiteral("/host/")) ? 0 : 1;
    } else if (program == QStringLiteral("flatpak") && args.first() == QStringLiteral("list")) {
        out.write("org.zdoom.GZDoom\norg.zdoom.VKDoom.NotTheApp\n");
    } else if (program == QStringLiteral("snap") && args.first() == QStringLiteral("list")) {
        out.write("Name Version Rev\ngzdoom 4 1\n");
    } else if (program == QStringLiteral("timeout-fixture")) {
        QThread::msleep(5000);
    } else if (program == QStringLiteral("fail-fixture")) {
        out.write("must not accept failed output");
        return 7;
    } else {
        QThread::msleep(100);
        return qEnvironmentVariableIntValue("HOST_TEST_EXIT");
    }
    return 0;
}
