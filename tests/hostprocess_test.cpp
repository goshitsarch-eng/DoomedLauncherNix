#include "hostprocess.h"
#include "sourceportdetector.h"
#include "gamelauncher.h"
#include "database.h"

#include <QDir>
#include <QElapsedTimer>
#include <QFile>
#include <QJsonArray>
#include <QJsonDocument>
#include <QSignalSpy>
#include <QTemporaryDir>
#include <QTest>

class HostProcessTest : public QObject
{
    Q_OBJECT
    QTemporaryDir temporary;
    Database database;
    int gameId = -1;
    QByteArray originalPath;

    QStringList lastCommand()
    {
        QFile file(temporary.filePath(QStringLiteral("argv.jsonl")));
        if (!file.open(QIODevice::ReadOnly))
            return {};
        const auto lines = file.readAll().trimmed().split('\n');
        QStringList result;
        for (const auto &value : QJsonDocument::fromJson(lines.last()).array())
            result << value.toString();
        return result;
    }

private Q_SLOTS:
    void initTestCase()
    {
        QVERIFY(temporary.isValid());
        originalPath = qgetenv("PATH");
        qputenv("FLATPAK_ID", "com.goshapps.DoomLauncher");
        qputenv("PATH", QCoreApplication::applicationDirPath().toUtf8());
        qputenv("HOST_TEST_LOG", temporary.filePath(QStringLiteral("argv.jsonl")).toUtf8());
        qputenv("XDG_DATA_HOME", temporary.path().toUtf8());
        QVERIFY(database.open());
        gameId = database.insertGameFile({{QStringLiteral("FileName"), QStringLiteral("fixture.wad")}, {QStringLiteral("Title"), QStringLiteral("Fixture")}});
        QVERIFY(gameId > 0);
    }

    void typedBoundary()
    {
        QProcess process;
        const QString directory = QStringLiteral("/host/a b;$(touch injected)");
        const QStringList args{QStringLiteral("a b"), QStringLiteral(";touch injected"),
                               QStringLiteral("$(id)"), QStringLiteral("--host"), QStringLiteral("")};
        HostProcess::configure(process, QStringLiteral("/host/bin/engine"), args, directory);
        QCOMPARE(process.program(), QStringLiteral("flatpak-spawn"));
        QCOMPARE(process.arguments(), (QStringList{QStringLiteral("--host"), QStringLiteral("--watch-bus"), QStringLiteral("--directory=") + directory,
                                                   QStringLiteral("--"), QStringLiteral("/host/bin/engine")} + args));
        QVERIFY(process.workingDirectory().isEmpty());
        process.start();
        QVERIFY(process.waitForFinished());
        QCOMPARE(process.exitCode(), 0);
        QCOMPARE(lastCommand(), process.arguments());
    }

    void detection()
    {
        QCOMPARE(SourcePortDetector::findExecutable(QStringLiteral("gzdoom")), QStringLiteral("/host/bin/GZDoom"));
        QCOMPARE(SourcePortDetector::findExecutable(QStringLiteral("uzdoom")), QStringLiteral("/host/bin/UZDoom-test.AppImage"));
        QVERIFY(SourcePortDetector::findExecutable(QStringLiteral("missing")).isEmpty());
        const auto ports = SourcePortDetector::detect();
        QStringList executables;
        for (const auto &port : ports)
            executables << port.executable;
        QVERIFY(executables.contains(QStringLiteral("flatpak:org.zdoom.GZDoom")));
        QVERIFY(executables.contains(QStringLiteral("snap:gzdoom")));
        QVERIFY(!executables.contains(QStringLiteral("flatpak:org.zdoom.VKDoom")));
    }

    void launches_data()
    {
        QTest::addColumn<QString>("executable");
        QTest::addColumn<QString>("directory");
        QTest::addColumn<QString>("program");
        QTest::newRow("sibling-flatpak") << QStringLiteral("flatpak:org.zdoom.GZDoom") << QStringLiteral("") << QStringLiteral("flatpak");
        QTest::newRow("snap") << QStringLiteral("snap:gzdoom") << QStringLiteral("") << QStringLiteral("snap");
        QTest::newRow("absolute-host-native") << QStringLiteral("/host/bin/GZDoom") << QStringLiteral("") << QStringLiteral("/host/bin/GZDoom");
        QTest::newRow("configured-host-directory") << QStringLiteral("GZDoom") << QStringLiteral("/host/a b;$(id)") << QStringLiteral("/host/a b;$(id)/GZDoom");
        QTest::newRow("host-PATH-case") << QStringLiteral("gzdoom") << QStringLiteral("") << QStringLiteral("/host/bin/GZDoom");
        QTest::newRow("host-AppImage") << QStringLiteral("uzdoom") << QStringLiteral("") << QStringLiteral("/host/bin/UZDoom-test.AppImage");
    }

    void launches()
    {
        QFETCH(QString, executable);
        QFETCH(QString, directory);
        QFETCH(QString, program);
        const int portId = database.insertSourcePort({{QStringLiteral("Name"), QStringLiteral("Fixture")}, {QStringLiteral("Executable"), executable},
                                                      {QStringLiteral("Directory"), directory}});
        LaunchRequest request;
        request.gameFileId = gameId;
        request.sourcePortId = portId;
        request.extraParamsOnly = true;
        request.saveStatistics = false;
        request.extraParams = QStringLiteral("\"a b\" \";touch injected\" \"$(id)\"");
        GameLauncher launcher(&database);
        QSignalSpy exited(&launcher, &GameLauncher::processExited);
        QSignalSpy failed(&launcher, &GameLauncher::launchFailed);
        const QString error = launcher.launch(request);
        QVERIFY2(error.isEmpty(), qPrintable(error));
        QVERIFY(launcher.hasActiveSessions());
        QTRY_COMPARE(exited.count(), 1);
        QVERIFY(!launcher.hasActiveSessions());
        QCOMPARE(failed.count(), 0);
        const QStringList command = lastCommand();
        QCOMPARE(command.mid(0, 2), (QStringList{QStringLiteral("--host"), QStringLiteral("--watch-bus")}));
        const QString cwd = directory.isEmpty()
            ? (program.startsWith(QLatin1Char('/')) ? QFileInfo(program).absolutePath() : QString(QStringLiteral("/host/home")))
            : directory;
        QCOMPARE(command.at(2), QStringLiteral("--directory=") + cwd);
        QCOMPARE(command.at(3), QStringLiteral("--"));
        QCOMPARE(command.at(4), program);
        QCOMPARE(command.mid(command.size() - 3), (QStringList{QStringLiteral("a b"), QStringLiteral(";touch injected"), QStringLiteral("$(id)")}));
        if (program == QStringLiteral("flatpak"))
            QCOMPARE(command.mid(5, 5), (QStringList{QStringLiteral("run"), QStringLiteral("--filesystem=host"), QStringLiteral("--filesystem=home"),
                                                    QStringLiteral("org.zdoom.GZDoom"), QStringLiteral("--")}));
        if (program == QStringLiteral("snap"))
            QCOMPARE(command.mid(5, 2), (QStringList{QStringLiteral("run"), QStringLiteral("gzdoom")}));
    }

    void failureLifecycle()
    {
        const int portId = database.insertSourcePort({{QStringLiteral("Name"), QStringLiteral("Failure")},
            {QStringLiteral("Executable"), QStringLiteral("flatpak:org.zdoom.GZDoom")},
            {QStringLiteral("Directory"), QStringLiteral("")}});
        QVERIFY(portId > 0);
        LaunchRequest request;
        request.gameFileId = gameId;
        request.sourcePortId = portId;
        request.extraParamsOnly = true;
        GameLauncher launcher(&database);
        QSignalSpy failed(&launcher, &GameLauncher::launchFailed);
        qputenv("HOST_TEST_EXIT", "7");
        QVERIFY(launcher.launch(request).isEmpty());
        QTRY_VERIFY(!launcher.hasActiveSessions());
        QCOMPARE(failed.count(), 1);
        qunsetenv("HOST_TEST_EXIT");
        qputenv("PATH", temporary.path().toUtf8());
        QVERIFY(!launcher.launch(request).isEmpty());
        QVERIFY(!launcher.hasActiveSessions());
        qputenv("PATH", QCoreApplication::applicationDirPath().toUtf8());
        for (const QString &bad : {QString(QStringLiteral("flatpak:--command=sh")), QString(QStringLiteral("snap:--shell")), QString(QStringLiteral("/missing/engine"))}) {
            database.updateSourcePort(portId, {{QStringLiteral("Executable"), bad}});
            QVERIFY(!launcher.launch(request).isEmpty());
            QVERIFY(!launcher.hasActiveSessions());
        }
    }

    void captureFailures()
    {
        QByteArray output;
        QVERIFY(!HostProcess::capture(QStringLiteral("fail-fixture"), {}, &output));
        QVERIFY(output.isEmpty());
        QElapsedTimer timer;
        timer.start();
        QVERIFY(!HostProcess::capture(QStringLiteral("timeout-fixture"), {}, &output, 30));
        QVERIFY(timer.elapsed() < 2000);
        QVERIFY(output.isEmpty());
    }

    void cleanupTestCase()
    {
        qputenv("PATH", originalPath);
    }
};

QTEST_GUILESS_MAIN(HostProcessTest)
#include "hostprocess_test.moc"
